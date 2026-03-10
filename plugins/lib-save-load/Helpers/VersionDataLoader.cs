using BeyondImmersion.BannouService.Asset;
using BeyondImmersion.BannouService.SaveLoad.Compression;
using BeyondImmersion.BannouService.SaveLoad.Delta;
using BeyondImmersion.BannouService.SaveLoad.Models;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BeyondImmersion.BannouService.Attributes;

namespace BeyondImmersion.BannouService.SaveLoad.Helpers;

/// <summary>
/// Implementation of version data loading operations.
/// Handles hot cache access, asset service retrieval, and delta chain reconstruction.
/// </summary>
[BannouHelperService("version-data", typeof(ISaveLoadService), typeof(IVersionDataLoader), lifetime: ServiceLifetime.Scoped)]
public sealed class VersionDataLoader : IVersionDataLoader
{
    /// <summary>Hot cache store for fast save data retrieval (Redis-backed with TTL).</summary>
    private readonly IStateStore<HotSaveEntry> _hotCacheStore;
    private readonly SaveLoadServiceConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<VersionDataLoader> _logger;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="VersionDataLoader"/> class.
    /// </summary>
    /// <param name="stateStoreFactory">State store factory for hot cache and version data access.</param>
    /// <param name="configuration">Save-load service configuration.</param>
    /// <param name="serviceProvider">Service provider for L3 soft dependency resolution.</param>
    /// <param name="httpClientFactory">HTTP client factory for presigned URL downloads.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    public VersionDataLoader(
        IStateStoreFactory stateStoreFactory,
        SaveLoadServiceConfiguration configuration,
        IServiceProvider serviceProvider,
        IHttpClientFactory httpClientFactory,
        ILogger<VersionDataLoader> logger,
        ITelemetryProvider telemetryProvider)
    {
        _hotCacheStore = stateStoreFactory.GetStore<HotSaveEntry>(StateStoreDefinitions.SaveLoadCache);
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _telemetryProvider = telemetryProvider;
    }

    /// <inheritdoc />
    public async Task<byte[]?> LoadVersionDataAsync(
        string slotId,
        SaveVersionManifest version,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.save-load", "VersionDataLoader.LoadVersionDataAsync");
        // Try hot cache first
        var hotKey = HotSaveEntry.BuildStateKey(slotId, version.VersionNumber);
        var hotEntry = await _hotCacheStore.GetAsync(hotKey, cancellationToken);

        if (hotEntry != null)
        {
            _logger.LogDebug("Hot cache hit for slot {SlotId} version {Version}", slotId, version.VersionNumber);
            var compressedData = Convert.FromBase64String(hotEntry.Data);
            // HotSaveEntry.CompressionType is now a nullable enum - use it directly or default to NONE
            var hotCompressionType = hotEntry.CompressionType ?? CompressionType.None;
            return CompressionHelper.Decompress(compressedData, hotCompressionType);
        }

        _logger.LogDebug("Hot cache miss for slot {SlotId} version {Version}", slotId, version.VersionNumber);

        // Load from asset service
        if (!version.AssetId.HasValue)
        {
            _logger.LogWarning(
                "No asset ID for version {Version} and no hot cache entry",
                version.VersionNumber);
            return null;
        }

        var assetGuid = version.AssetId.Value;

        try
        {
            // L3 soft dependency per FOUNDATION TENETS — Asset service may not be enabled
            var assetClient = _serviceProvider.GetService<IAssetClient>();
            if (assetClient == null)
            {
                _logger.LogDebug("Asset service not available, cannot load version data from storage");
                return null;
            }

            var assetResponse = await assetClient.GetAssetAsync(
                new GetAssetRequest { AssetId = assetGuid.ToString() },
                cancellationToken);

            if (assetResponse?.DownloadUrl == null)
            {
                _logger.LogError("Failed to get download URL for asset {AssetId}", version.AssetId);
                return null;
            }

            using var httpClient = _httpClientFactory.CreateClient();
            var compressedData = await httpClient.GetByteArrayAsync(assetResponse.DownloadUrl, cancellationToken);
            // SaveVersionManifest.CompressionType is now an enum - use it directly
            return CompressionHelper.Decompress(compressedData, version.CompressionType);
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
        using var activity = _telemetryProvider.StartActivity("bannou.save-load", "VersionDataLoader.ReconstructFromDeltaChainAsync");
        // Build the chain from target back to base snapshot
        var chain = new List<SaveVersionManifest>();
        var current = targetVersion;

        while (current.IsDelta && current.BaseVersionNumber.HasValue)
        {
            chain.Add(current);
            var baseKey = SaveVersionManifest.BuildStateKey(slotId, current.BaseVersionNumber.Value);
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
        using var activity = _telemetryProvider.StartActivity("bannou.save-load", "VersionDataLoader.LoadFromAssetServiceAsync");
        try
        {
            // L3 soft dependency per FOUNDATION TENETS — Asset service may not be enabled
            var assetClient = _serviceProvider.GetService<IAssetClient>();
            if (assetClient == null)
            {
                _logger.LogDebug("Asset service not available, cannot load from asset storage");
                return null;
            }

            var getRequest = new GetAssetRequest
            {
                AssetId = assetId
            };

            var response = await assetClient.GetAssetAsync(getRequest, cancellationToken);

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
        using var activity = _telemetryProvider.StartActivity("bannou.save-load", "VersionDataLoader.CacheInHotStoreAsync");
        try
        {
            // Re-compress for storage efficiency
            // SaveVersionManifest.CompressionType is now an enum - use it directly
            var compressionType = manifest.CompressionType;
            var cacheCompressionLevel = compressionType == CompressionType.Brotli
                ? _configuration.BrotliCompressionLevel
                : compressionType == CompressionType.Gzip
                    ? _configuration.GzipCompressionLevel
                    : (int?)null;
            var dataToStore = compressionType != CompressionType.None
                ? CompressionHelper.Compress(decompressedData, compressionType, cacheCompressionLevel)
                : decompressedData;

            // HotSaveEntry.SlotId is now Guid - parse the string slotId
            var hotEntry = new HotSaveEntry
            {
                SlotId = Guid.Parse(slotId),
                VersionNumber = versionNumber,
                Data = Convert.ToBase64String(dataToStore),
                ContentHash = contentHash,
                IsCompressed = compressionType != CompressionType.None,
                CompressionType = compressionType,
                SizeBytes = dataToStore.Length,
                CachedAt = DateTimeOffset.UtcNow,
                IsDelta = manifest.IsDelta
            };

            var hotKey = HotSaveEntry.BuildStateKey(slotId, versionNumber);
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
    public async Task<int?> FindVersionByCheckpointAsync(
        SaveSlotMetadata slot,
        string checkpointName,
        IStateStore<SaveVersionManifest> versionStore,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.save-load", "VersionDataLoader.FindVersionByCheckpointAsync");
        // Search through versions from newest to oldest
        if (!slot.LatestVersion.HasValue)
        {
            return null;
        }

        for (var v = slot.LatestVersion.Value; v >= 1; v--)
        {
            var versionKey = SaveVersionManifest.BuildStateKey(slot.SlotId.ToString(), v);
            var manifest = await versionStore.GetAsync(versionKey, cancellationToken);

            if (manifest?.CheckpointName == checkpointName)
            {
                _logger.LogDebug("Found checkpoint {CheckpointName} at version {Version}", checkpointName, v);
                return v;
            }
        }

        _logger.LogDebug("Checkpoint {CheckpointName} not found in slot {SlotId}", checkpointName, slot.SlotId);
        return null;
    }
}
