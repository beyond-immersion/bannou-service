using BeyondImmersion.BannouService.Asset;
using BeyondImmersion.BannouService.SaveLoad.Compression;
using BeyondImmersion.BannouService.SaveLoad.Delta;
using BeyondImmersion.BannouService.SaveLoad.Models;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.SaveLoad.Helpers;

/// <summary>
/// Implementation of version data loading operations.
/// Handles hot cache access, asset service retrieval, and delta chain reconstruction.
/// </summary>
public sealed class VersionDataLoader : IVersionDataLoader
{
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly SaveLoadServiceConfiguration _configuration;
    private readonly IAssetClient _assetClient;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<VersionDataLoader> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="VersionDataLoader"/> class.
    /// </summary>
    public VersionDataLoader(
        IStateStoreFactory stateStoreFactory,
        SaveLoadServiceConfiguration configuration,
        IAssetClient assetClient,
        IHttpClientFactory httpClientFactory,
        ILogger<VersionDataLoader> logger)
    {
        _stateStoreFactory = stateStoreFactory ?? throw new ArgumentNullException(nameof(stateStoreFactory));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _assetClient = assetClient ?? throw new ArgumentNullException(nameof(assetClient));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<byte[]?> LoadVersionDataAsync(
        string slotId,
        SaveVersionManifest version,
        CancellationToken cancellationToken)
    {
        // Try hot cache first
        var hotCacheStore = _stateStoreFactory.GetStore<HotSaveEntry>(_configuration.HotCacheStoreName);
        var hotKey = HotSaveEntry.GetStateKey(slotId, version.VersionNumber);
        var hotEntry = await hotCacheStore.GetAsync(hotKey, cancellationToken);

        if (hotEntry != null)
        {
            _logger.LogDebug("Hot cache hit for slot {SlotId} version {Version}", slotId, version.VersionNumber);
            var compressedData = Convert.FromBase64String(hotEntry.Data);
            var hotCompressionType = Enum.TryParse<CompressionType>(hotEntry.CompressionType, out var hct) ? hct : CompressionType.NONE;
            return CompressionHelper.Decompress(compressedData, hotCompressionType);
        }

        _logger.LogDebug("Hot cache miss for slot {SlotId} version {Version}", slotId, version.VersionNumber);

        // Load from asset service
        if (string.IsNullOrEmpty(version.AssetId))
        {
            _logger.LogWarning(
                "No asset ID for version {Version} and no hot cache entry",
                version.VersionNumber);
            return null;
        }

        if (!Guid.TryParse(version.AssetId, out var assetGuid))
        {
            _logger.LogError("Invalid asset ID format: {AssetId}", version.AssetId);
            return null;
        }

        try
        {
            var assetResponse = await _assetClient.GetAssetAsync(
                new GetAssetRequest { AssetId = assetGuid.ToString() },
                cancellationToken);

            if (assetResponse?.DownloadUrl == null)
            {
                _logger.LogError("Failed to get download URL for asset {AssetId}", version.AssetId);
                return null;
            }

            using var httpClient = _httpClientFactory.CreateClient();
            var compressedData = await httpClient.GetByteArrayAsync(assetResponse.DownloadUrl, cancellationToken);
            var versionCompressionType = Enum.TryParse<CompressionType>(version.CompressionType, out var vct) ? vct : CompressionType.NONE;
            return CompressionHelper.Decompress(compressedData, versionCompressionType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load data from asset {AssetId}", version.AssetId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<byte[]?> ReconstructFromDeltaChainAsync(
        string slotId,
        SaveVersionManifest targetVersion,
        IStateStore<SaveVersionManifest> versionStore,
        CancellationToken cancellationToken)
    {
        // Build the chain from target back to base snapshot
        var chain = new List<SaveVersionManifest>();
        var current = targetVersion;

        while (current.IsDelta && current.BaseVersionNumber.HasValue)
        {
            chain.Add(current);
            var baseKey = SaveVersionManifest.GetStateKey(slotId, current.BaseVersionNumber.Value);
            current = await versionStore.GetAsync(baseKey, cancellationToken);

            if (current == null)
            {
                _logger.LogError(
                    "Delta chain broken: base version {Version} not found",
                    chain.Last().BaseVersionNumber);
                return null;
            }
        }

        // current is now the base snapshot
        var baseData = await LoadVersionDataAsync(slotId, current, cancellationToken);
        if (baseData == null)
        {
            _logger.LogError("Failed to load base snapshot version {Version}", current.VersionNumber);
            return null;
        }

        // Apply deltas in order (reverse the chain since we built it backwards)
        chain.Reverse();

        var deltaProcessor = new DeltaProcessor(
            _logger as ILogger<DeltaProcessor> ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DeltaProcessor>.Instance,
            _configuration.MigrationMaxPatchOperations);

        var result = baseData;
        foreach (var deltaVersion in chain)
        {
            var deltaData = await LoadVersionDataAsync(slotId, deltaVersion, cancellationToken);
            if (deltaData == null)
            {
                _logger.LogError(
                    "Failed to load delta data for version {Version}",
                    deltaVersion.VersionNumber);
                return null;
            }

            var algorithm = deltaVersion.DeltaAlgorithm ?? _configuration.DefaultDeltaAlgorithm;
            result = deltaProcessor.ApplyDelta(result, deltaData, algorithm);

            if (result == null)
            {
                _logger.LogError(
                    "Failed to apply delta for version {Version}",
                    deltaVersion.VersionNumber);
                return null;
            }
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<byte[]?> LoadFromAssetServiceAsync(
        string assetId,
        CancellationToken cancellationToken)
    {
        try
        {
            var getRequest = new GetAssetRequest
            {
                AssetId = assetId
            };

            var response = await _assetClient.GetAssetAsync(getRequest, cancellationToken);

            if (response?.DownloadUrl == null)
            {
                _logger.LogWarning("Asset service returned no download URL for asset {AssetId}", assetId);
                return null;
            }

            // Download from presigned URL
            using var httpClient = _httpClientFactory.CreateClient();
            var data = await httpClient.GetByteArrayAsync(response.DownloadUrl, cancellationToken);
            return data;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading from Asset service for asset {AssetId}", assetId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task CacheInHotStoreAsync(
        string slotId,
        int versionNumber,
        byte[] decompressedData,
        string contentHash,
        SaveVersionManifest manifest,
        IStateStore<HotSaveEntry> hotCacheStore,
        CancellationToken cancellationToken)
    {
        try
        {
            // Re-compress for storage efficiency
            var compressionType = Enum.TryParse<CompressionType>(manifest.CompressionType, out var ct) ? ct : CompressionType.NONE;
            var dataToStore = compressionType != CompressionType.NONE
                ? CompressionHelper.Compress(decompressedData, compressionType)
                : decompressedData;

            var hotEntry = new HotSaveEntry
            {
                SlotId = slotId,
                VersionNumber = versionNumber,
                Data = Convert.ToBase64String(dataToStore),
                ContentHash = contentHash,
                IsCompressed = compressionType != CompressionType.NONE,
                CompressionType = compressionType.ToString(),
                SizeBytes = dataToStore.Length,
                CachedAt = DateTimeOffset.UtcNow,
                IsDelta = manifest.IsDelta
            };

            var hotKey = HotSaveEntry.GetStateKey(slotId, versionNumber);
            await hotCacheStore.SaveAsync(hotKey, hotEntry, cancellationToken: cancellationToken);

            _logger.LogDebug("Cached version {Version} in hot store ({Size} bytes)", versionNumber, dataToStore.Length);
        }
        catch (Exception ex)
        {
            // Log but don't fail - caching is best-effort
            _logger.LogWarning(ex, "Failed to cache version {Version} in hot store", versionNumber);
        }
    }

    /// <inheritdoc />
    public async Task<int> FindVersionByCheckpointAsync(
        SaveSlotMetadata slot,
        string checkpointName,
        IStateStore<SaveVersionManifest> versionStore,
        CancellationToken cancellationToken)
    {
        // Search through versions from newest to oldest
        for (var v = slot.LatestVersion ?? 0; v >= 1; v--)
        {
            var versionKey = SaveVersionManifest.GetStateKey(slot.SlotId, v);
            var manifest = await versionStore.GetAsync(versionKey, cancellationToken);

            if (manifest?.CheckpointName == checkpointName)
            {
                _logger.LogDebug("Found checkpoint {CheckpointName} at version {Version}", checkpointName, v);
                return v;
            }
        }

        _logger.LogDebug("Checkpoint {CheckpointName} not found in slot {SlotId}", checkpointName, slot.SlotId);
        return 0;
    }
}
