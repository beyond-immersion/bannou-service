using BeyondImmersion.BannouService.Asset;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.SaveLoad.Models;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.SaveLoad.Helpers;

/// <summary>
/// Implementation of version cleanup operations.
/// Handles rolling cleanup and slot-level version management.
/// </summary>
public sealed class VersionCleanupManager : IVersionCleanupManager
{
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly SaveLoadServiceConfiguration _configuration;
    private readonly IAssetClient _assetClient;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<VersionCleanupManager> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="VersionCleanupManager"/> class.
    /// </summary>
    public VersionCleanupManager(
        IStateStoreFactory stateStoreFactory,
        SaveLoadServiceConfiguration configuration,
        IAssetClient assetClient,
        IMessageBus messageBus,
        ILogger<VersionCleanupManager> logger)
    {
        _stateStoreFactory = stateStoreFactory;
        _configuration = configuration;
        _assetClient = assetClient;
        _messageBus = messageBus;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> PerformRollingCleanupAsync(
        SaveSlotMetadata slot,
        IStateStore<SaveVersionManifest> versionStore,
        IStateStore<HotSaveEntry> hotCacheStore,
        CancellationToken cancellationToken)
    {
        if (slot.VersionCount <= slot.MaxVersions)
        {
            return 0;
        }

        var cleanedUp = 0;
        var targetCleanup = slot.VersionCount - slot.MaxVersions;

        // Start from oldest version (1) and clean up non-pinned versions
        for (var v = 1; v <= (slot.LatestVersion ?? 0) && cleanedUp < targetCleanup; v++)
        {
            var versionKey = SaveVersionManifest.GetStateKey(slot.SlotId, v);
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

            // Delete version and hot cache entry
            await versionStore.DeleteAsync(versionKey, cancellationToken);
            var hotKey = HotSaveEntry.GetStateKey(slot.SlotId, v);
            await hotCacheStore.DeleteAsync(hotKey, cancellationToken);

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
        if (slot.VersionCount <= slot.MaxVersions)
        {
            return;
        }

        var versionQueryStore = _stateStoreFactory.GetQueryableStore<SaveVersionManifest>(_configuration.VersionManifestStoreName);
        var versions = await versionQueryStore.QueryAsync(
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

        var versionStore = _stateStoreFactory.GetStore<SaveVersionManifest>(_configuration.VersionManifestStoreName);
        var hotStore = _stateStoreFactory.GetStore<HotSaveEntry>(_configuration.HotCacheStoreName);
        long bytesFreed = 0;

        foreach (var version in versionsToDelete)
        {
            try
            {
                // Delete asset if exists
                if (!string.IsNullOrEmpty(version.AssetId) && Guid.TryParse(version.AssetId, out var assetGuid))
                {
                    try
                    {
                        await _assetClient.DeleteAssetAsync(new DeleteAssetRequest { AssetId = assetGuid.ToString() }, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete asset {AssetId} during cleanup", version.AssetId);
                    }
                }

                // Delete version manifest
                var versionKey = SaveVersionManifest.GetStateKey(slot.SlotId, version.VersionNumber);
                await versionStore.DeleteAsync(versionKey, cancellationToken);

                // Delete from hot cache
                var hotCacheKey = HotSaveEntry.GetStateKey(slot.SlotId, version.VersionNumber);
                await hotStore.DeleteAsync(hotCacheKey, cancellationToken);

                bytesFreed += version.CompressedSizeBytes ?? version.SizeBytes;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cleanup version {Version} in slot {SlotId}",
                    version.VersionNumber, slot.SlotId);
            }
        }

        // Update slot metadata
        var slotStore = _stateStoreFactory.GetStore<SaveSlotMetadata>(_configuration.SlotMetadataStoreName);
        slot.VersionCount -= versionsToDelete.Count;
        slot.TotalSizeBytes -= bytesFreed;
        slot.UpdatedAt = DateTimeOffset.UtcNow;
        await slotStore.SaveAsync(slot.GetStateKey(), slot, cancellationToken: cancellationToken);

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
        await _messageBus.TryPublishAsync(
            "save-load.cleanup.completed",
            cleanupEvent,
            cancellationToken: cancellationToken);
    }
}
