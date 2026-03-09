// =============================================================================
// Save-Load Service Events
// Event consumer registration and handlers for account deletion cleanup.
// =============================================================================

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.SaveLoad.Models;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.SaveLoad;

/// <summary>
/// Partial class for SaveLoadService event handling.
/// Contains event consumer registration and handler implementations for
/// account deletion cleanup per FOUNDATION TENETS (Account Deletion Cleanup Obligation).
/// </summary>
public partial class SaveLoadService : IBannouService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor via IBannouService interface.
    /// </summary>
    void IBannouService.RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        // Account deletion cleanup per FOUNDATION TENETS (Account Deletion Cleanup Obligation).
        // Account-owned save slots, versions, hot cache entries, and associated assets
        // must be deleted when the owning account is deleted.
        eventConsumer.RegisterHandler<ISaveLoadService, AccountDeletedEvent>(
            "account.deleted",
            async (svc, evt) => await ((SaveLoadService)svc).HandleAccountDeletedAsync(evt));
    }

    /// <summary>
    /// Handles account.deleted events by cleaning up all account-owned save slots,
    /// their versions, hot cache entries, and associated MinIO assets.
    /// Per FOUNDATION TENETS: Account deletion is always CASCADE — data has no owner and must be removed.
    /// </summary>
    internal async Task HandleAccountDeletedAsync(AccountDeletedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.save-load", "SaveLoadService.HandleAccountDeletedAsync");
        var accountId = evt.AccountId;

        _logger.LogInformation("Processing account deletion cleanup for account {AccountId}", accountId);

        try
        {
            // Find all slots owned by this account
            var accountSlots = await _slotQueryStore.QueryAsync(
                s => s.OwnerId == accountId && s.OwnerType == EntityType.Account,
                cancellationToken: default);

            if (accountSlots == null || accountSlots.Count == 0)
            {
                _logger.LogDebug("No save slots found for deleted account {AccountId}", accountId);
                return;
            }

            var totalSlotsDeleted = 0;
            var totalVersionsDeleted = 0;
            long totalBytesFreed = 0;

            foreach (var slot in accountSlots)
            {
                try
                {
                    // Get all versions for this slot
                    var versions = await _versionQueryStore.QueryAsync(
                        v => v.SlotId == slot.SlotId,
                        cancellationToken: default);

                    foreach (var version in versions ?? [])
                    {
                        // Delete hot cache entry
                        var hotCacheKey = HotSaveEntry.BuildStateKey(slot.SlotId.ToString(), version.VersionNumber);
                        await _hotCacheStore.DeleteAsync(hotCacheKey, default);

                        // Delete asset if exists (L3 soft dependency per FOUNDATION TENETS)
                        if (version.AssetId.HasValue)
                        {
                            try
                            {
                                var assetClient = _serviceProvider.GetService<BeyondImmersion.BannouService.Asset.IAssetClient>();
                                if (assetClient != null)
                                    await assetClient.DeleteAssetAsync(
                                        new BeyondImmersion.BannouService.Asset.DeleteAssetRequest { AssetId = version.AssetId.Value.ToString() },
                                        default);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to delete asset {AssetId} during account cleanup", version.AssetId);
                            }
                        }

                        // Delete version manifest
                        var versionKey = SaveVersionManifest.BuildStateKey(slot.SlotId.ToString(), version.VersionNumber);
                        await _versionQueryStore.DeleteAsync(versionKey, default);
                        totalVersionsDeleted++;
                        totalBytesFreed += version.CompressedSizeBytes ?? version.SizeBytes;
                    }

                    // Delete the slot itself
                    var slotKey = slot.BuildStateKey();
                    await _slotQueryStore.DeleteAsync(slotKey, default);
                    totalSlotsDeleted++;
                }
                catch (Exception ex)
                {
                    // Per-item error isolation per IMPLEMENTATION TENETS
                    _logger.LogWarning(ex, "Failed to cleanup save slot {SlotId} for deleted account {AccountId}", slot.SlotId, accountId);
                }
            }

            _logger.LogInformation(
                "Account deletion cleanup complete for {AccountId}: {SlotsDeleted} slots, {VersionsDeleted} versions, {BytesFreed} bytes freed",
                accountId, totalSlotsDeleted, totalVersionsDeleted, totalBytesFreed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process account deletion cleanup for account {AccountId}", accountId);
            await _messageBus.TryPublishErrorAsync(
                "save-load",
                "HandleAccountDeleted",
                "AccountCleanupFailed",
                $"Failed to cleanup saves for deleted account {accountId}: {ex.Message}");
        }
    }
}
