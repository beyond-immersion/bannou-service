using BeyondImmersion.BannouService.Asset;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.SaveLoad.Compression;
using BeyondImmersion.BannouService.SaveLoad.Hashing;
using BeyondImmersion.BannouService.SaveLoad.Models;
using BeyondImmersion.BannouService.SaveLoad.Processing;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.SaveLoad.Helpers;

/// <summary>
/// Implementation of version cleanup operations.
/// Handles rolling cleanup and slot-level version management.
/// </summary>
[BannouHelperService("version-cleanup", typeof(ISaveLoadService), typeof(IVersionCleanupManager), lifetime: ServiceLifetime.Scoped)]
public sealed class VersionCleanupManager : IVersionCleanupManager
{
    /// <summary>Queryable store for version manifests (MySQL-backed for LINQ queries).</summary>
    private readonly IQueryableStateStore<SaveVersionManifest> _versionQueryStore;
    /// <summary>Store for version manifests (basic CRUD operations).</summary>
    private readonly IStateStore<SaveVersionManifest> _versionStore;
    /// <summary>Hot cache store for fast save data retrieval (Redis-backed with TTL).</summary>
    private readonly IStateStore<HotSaveEntry> _hotCacheStore;
    /// <summary>Cacheable store for pending upload entries (Redis-backed with set operations).</summary>
    private readonly ICacheableStateStore<PendingUploadEntry> _pendingStore;
    /// <summary>Store for save slot metadata (basic CRUD operations).</summary>
    private readonly IStateStore<SaveSlotMetadata> _slotStore;
    private readonly SaveLoadServiceConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly IMessageBus _messageBus;
    private readonly IVersionDataLoader _versionDataLoader;
    private readonly ILogger<VersionCleanupManager> _logger;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="VersionCleanupManager"/> class.
    /// </summary>
    public VersionCleanupManager(
        IStateStoreFactory stateStoreFactory,
        SaveLoadServiceConfiguration configuration,
        IServiceProvider serviceProvider,
        IMessageBus messageBus,
        IVersionDataLoader versionDataLoader,
        ILogger<VersionCleanupManager> logger,
        ITelemetryProvider telemetryProvider)
    {
        _versionQueryStore = stateStoreFactory.GetQueryableStore<SaveVersionManifest>(StateStoreDefinitions.SaveLoadVersions);
        _versionStore = stateStoreFactory.GetStore<SaveVersionManifest>(StateStoreDefinitions.SaveLoadVersions);
        _hotCacheStore = stateStoreFactory.GetStore<HotSaveEntry>(StateStoreDefinitions.SaveLoadCache);
        _pendingStore = stateStoreFactory.GetCacheableStore<PendingUploadEntry>(StateStoreDefinitions.SaveLoadPending);
        _slotStore = stateStoreFactory.GetStore<SaveSlotMetadata>(StateStoreDefinitions.SaveLoadSlots);
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _messageBus = messageBus;
        _versionDataLoader = versionDataLoader;
        _logger = logger;
        _telemetryProvider = telemetryProvider;
    }

    /// <inheritdoc />
    public async Task<int> PerformRollingCleanupAsync(
        SaveSlotMetadata slot,
        IStateStore<SaveVersionManifest> versionStore,
        IStateStore<HotSaveEntry> hotCacheStore,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.save-load", "VersionCleanupManager.PerformRollingCleanupAsync");
        if (slot.VersionCount <= slot.MaxVersions)
        {
            return 0;
        }

        var cleanedUp = 0;
        var targetCleanup = slot.VersionCount - slot.MaxVersions;

        // Start from oldest version (1) and clean up non-pinned versions
        // SaveSlotMetadata.SlotId is now Guid - convert to string for state key
        for (var v = 1; v <= (slot.LatestVersion ?? 0) && cleanedUp < targetCleanup; v++)
        {
            var versionKey = SaveVersionManifest.BuildStateKey(slot.SlotId.ToString(), v);
            var manifest = await versionStore.GetAsync(versionKey, cancellationToken);

            if (manifest == null)
            {
                continue;
            }

            if (manifest.IsPinned)
            {
                // Skip pinned versions
                continue;
            }

            // Delete version, hot cache entry, and asset
            await versionStore.DeleteAsync(versionKey, cancellationToken);
            var hotKey = HotSaveEntry.BuildStateKey(slot.SlotId.ToString(), v);
            await hotCacheStore.DeleteAsync(hotKey, cancellationToken);

            if (manifest.AssetId.HasValue)
            {
                // L3 soft dependency per FOUNDATION TENETS — Asset service may not be enabled
                var assetClient = _serviceProvider.GetService<IAssetClient>();
                if (assetClient != null)
                {
                    try
                    {
                        await assetClient.DeleteAssetAsync(
                            new DeleteAssetRequest { AssetId = manifest.AssetId.Value.ToString() },
                            cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete asset {AssetId} during rolling cleanup", manifest.AssetId.Value);
                    }
                }
            }

            cleanedUp++;
            slot.VersionCount--;
            slot.TotalSizeBytes -= manifest.CompressedSizeBytes ?? manifest.SizeBytes;

            _logger.LogDebug("Cleaned up version {Version} from slot {SlotId}", v, slot.SlotId);
        }

        return cleanedUp;
    }

    /// <inheritdoc />
    public async Task CleanupOldVersionsAsync(
        SaveSlotMetadata slot,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.save-load", "VersionCleanupManager.CleanupOldVersionsAsync");
        if (slot.VersionCount <= slot.MaxVersions)
        {
            return;
        }

        // SaveSlotMetadata.SlotId and SaveVersionManifest.SlotId are both Guid - compare directly
        var versions = await _versionQueryStore.QueryAsync(
            v => v.SlotId == slot.SlotId,
            cancellationToken);

        // Get unpinned versions sorted by version number (oldest first)
        var unpinnedVersions = versions
            .Where(v => !v.IsPinned)
            .OrderBy(v => v.VersionNumber)
            .ToList();

        var pinnedCount = versions.Count(v => v.IsPinned);
        var targetUnpinnedCount = Math.Max(0, slot.MaxVersions - pinnedCount);
        var versionsToDelete = unpinnedVersions.Take(unpinnedVersions.Count - targetUnpinnedCount).ToList();

        if (versionsToDelete.Count == 0)
        {
            return;
        }

        long bytesFreed = 0;

        foreach (var version in versionsToDelete)
        {
            try
            {
                // Delete asset if exists
                if (version.AssetId.HasValue)
                {
                    // L3 soft dependency per FOUNDATION TENETS — Asset service may not be enabled
                    var assetClient = _serviceProvider.GetService<IAssetClient>();
                    if (assetClient != null)
                    {
                        try
                        {
                            await assetClient.DeleteAssetAsync(new DeleteAssetRequest { AssetId = version.AssetId.Value.ToString() }, cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to delete asset {AssetId} during cleanup", version.AssetId.Value);
                        }
                    }
                }

                // Delete version manifest
                var versionKey = SaveVersionManifest.BuildStateKey(slot.SlotId.ToString(), version.VersionNumber);
                await _versionStore.DeleteAsync(versionKey, cancellationToken);

                // Delete from hot cache
                var hotCacheKey = HotSaveEntry.BuildStateKey(slot.SlotId.ToString(), version.VersionNumber);
                await _hotCacheStore.DeleteAsync(hotCacheKey, cancellationToken);

                bytesFreed += version.CompressedSizeBytes ?? version.SizeBytes;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cleanup version {Version} in slot {SlotId}",
                    version.VersionNumber, slot.SlotId);
            }
        }

        // Update slot metadata
        slot.VersionCount -= versionsToDelete.Count;
        slot.TotalSizeBytes -= bytesFreed;
        slot.UpdatedAt = DateTimeOffset.UtcNow;
        await _slotStore.SaveAsync(slot.BuildStateKey(), slot, cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Rolling cleanup deleted {Count} versions from slot {SlotId}, freed {BytesFreed} bytes",
            versionsToDelete.Count, slot.SlotId, bytesFreed);

        // Publish cleanup event
        var cleanupEvent = new CleanupCompletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            VersionsDeleted = versionsToDelete.Count,
            SlotsDeleted = 0,
            BytesFreed = bytesFreed
        };
        await _messageBus.PublishCleanupCompletedAsync(cleanupEvent, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> CollapseExcessiveDeltaChainsAsync(
        SaveSlotMetadata slot,
        IReadOnlyList<SaveVersionManifest> deltaVersions,
        int maxChainLength,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.save-load", "VersionCleanupManager.CollapseExcessiveDeltaChainsAsync");
        if (deltaVersions.Count == 0)
        {
            return 0;
        }

        var slotIdString = slot.SlotId.ToString();

        var collapsedCount = 0;

        // Find delta versions that are at the end of chains exceeding maxChainLength
        foreach (var deltaVersion in deltaVersions)
        {
            try
            {
                // Calculate chain length by walking back to base
                var chainLength = await CalculateChainLengthAsync(slotIdString, deltaVersion, cancellationToken);

                if (chainLength <= maxChainLength)
                {
                    continue;
                }

                _logger.LogInformation(
                    "Collapsing delta chain for slot {SlotId} version {Version} (chain length {Length} exceeds max {Max})",
                    slot.SlotId, deltaVersion.VersionNumber, chainLength, maxChainLength);

                // Reconstruct full data from delta chain
                var reconstructedData = await _versionDataLoader.ReconstructFromDeltaChainAsync(
                    slotIdString, deltaVersion, _versionStore, cancellationToken);

                if (reconstructedData == null)
                {
                    _logger.LogWarning(
                        "Failed to reconstruct delta chain for slot {SlotId} version {Version} during auto-collapse",
                        slot.SlotId, deltaVersion.VersionNumber);
                    continue;
                }

                // Get the version with ETag for optimistic concurrency
                var versionKey = SaveVersionManifest.BuildStateKey(slotIdString, deltaVersion.VersionNumber);
                var (currentVersion, versionEtag) = await _versionStore.GetWithETagAsync(versionKey, cancellationToken);

                if (currentVersion == null || !currentVersion.IsDelta)
                {
                    // Version was already collapsed or deleted by another process
                    continue;
                }

                // Compress the reconstructed data
                var compressionType = _configuration.DefaultCompressionType;
                var compressionLevel = compressionType == CompressionType.Brotli
                    ? _configuration.BrotliCompressionLevel
                    : compressionType == CompressionType.Gzip
                        ? _configuration.GzipCompressionLevel
                        : (int?)null;
                var compressedData = CompressionHelper.Compress(reconstructedData, compressionType, compressionLevel);

                // Update the version to be a full snapshot
                var contentHash = ContentHasher.ComputeHash(reconstructedData);
                currentVersion.IsDelta = false;
                currentVersion.BaseVersionNumber = null;
                currentVersion.DeltaAlgorithm = null;
                currentVersion.SizeBytes = reconstructedData.Length;
                currentVersion.CompressedSizeBytes = compressedData.Length;
                currentVersion.CompressionType = compressionType;
                currentVersion.ContentHash = contentHash;
                currentVersion.UploadStatus = _configuration.AsyncUploadEnabled ? UploadStatus.Pending : UploadStatus.Complete;

                // Save updated manifest with optimistic concurrency
                var newEtag = await _versionStore.TrySaveAsync(versionKey, currentVersion, versionEtag ?? string.Empty, cancellationToken: cancellationToken);
                if (newEtag == null)
                {
                    _logger.LogDebug(
                        "Concurrent modification during auto-collapse for slot {SlotId} version {Version}",
                        slot.SlotId, deltaVersion.VersionNumber);
                    continue;
                }

                // Store in hot cache
                var hotEntry = new HotSaveEntry
                {
                    SlotId = slot.SlotId,
                    VersionNumber = currentVersion.VersionNumber,
                    Data = Convert.ToBase64String(compressedData),
                    ContentHash = contentHash,
                    IsCompressed = compressionType != CompressionType.None,
                    CompressionType = compressionType,
                    SizeBytes = reconstructedData.Length,
                    CachedAt = DateTimeOffset.UtcNow,
                    IsDelta = false
                };
                var hotCacheTtl = (int)TimeSpan.FromMinutes(_configuration.HotCacheTtlMinutes).TotalSeconds;
                await _hotCacheStore.SaveAsync(
                    hotEntry.BuildStateKey(),
                    hotEntry,
                    new StateOptions { Ttl = hotCacheTtl },
                    cancellationToken);

                // Queue for async upload if enabled
                if (_configuration.AsyncUploadEnabled)
                {
                    var uploadId = Guid.NewGuid();
                    var pendingEntry = new PendingUploadEntry
                    {
                        UploadId = uploadId,
                        SlotId = slot.SlotId,
                        SlotName = slot.SlotName,
                        VersionNumber = currentVersion.VersionNumber,
                        GameId = slot.GameId,
                        OwnerId = slot.OwnerId,
                        OwnerType = slot.OwnerType,
                        Data = Convert.ToBase64String(compressedData),
                        ContentHash = contentHash,
                        CompressionType = compressionType,
                        SizeBytes = reconstructedData.Length,
                        CompressedSizeBytes = compressedData.Length,
                        AttemptCount = 0,
                        QueuedAt = DateTimeOffset.UtcNow
                    };
                    var pendingKey = PendingUploadEntry.BuildStateKey(uploadId.ToString());
                    var pendingTtl = (int)TimeSpan.FromMinutes(_configuration.PendingUploadTtlMinutes).TotalSeconds;
                    await _pendingStore.SaveAsync(pendingKey, pendingEntry, new StateOptions { Ttl = pendingTtl }, cancellationToken);

                    // Add to tracking set for Redis-based queue processing
                    await _pendingStore.AddToSetAsync(SaveUploadWorker.PendingUploadIdsSetKey, uploadId.ToString(), cancellationToken: cancellationToken);
                }

                collapsedCount++;
                _logger.LogDebug(
                    "Successfully collapsed delta chain for slot {SlotId} version {Version}",
                    slot.SlotId, deltaVersion.VersionNumber);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Error during auto-collapse for slot {SlotId} version {Version}",
                    slot.SlotId, deltaVersion.VersionNumber);
                // Continue with next delta version
            }
        }

        if (collapsedCount > 0)
        {
            _logger.LogInformation(
                "Auto-collapse completed for slot {SlotId}: {Count} delta chains collapsed",
                slot.SlotId, collapsedCount);
        }

        return collapsedCount;
    }

    /// <summary>
    /// Calculates the length of a delta chain by walking back to the base snapshot.
    /// </summary>
    private async Task<int> CalculateChainLengthAsync(
        string slotId,
        SaveVersionManifest deltaVersion,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.save-load", "VersionCleanupManager.CalculateChainLengthAsync");
        var chainLength = 0;
        var current = deltaVersion;

        while (current.IsDelta && current.BaseVersionNumber.HasValue)
        {
            chainLength++;
            var baseKey = SaveVersionManifest.BuildStateKey(slotId, current.BaseVersionNumber.Value);
            current = await _versionStore.GetAsync(baseKey, cancellationToken);

            if (current == null)
            {
                // Broken chain - return current length
                break;
            }
        }

        return chainLength;
    }
}
