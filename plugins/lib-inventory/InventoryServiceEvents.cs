// =============================================================================
// Inventory Service Events
// Event consumer registration and handlers for cache invalidation and
// account deletion cleanup.
// =============================================================================

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Inventory.Caching;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace BeyondImmersion.BannouService.Inventory;

/// <summary>
/// Partial class for InventoryService event handling.
/// Contains event consumer registration and handler implementations for cache invalidation
/// and account deletion cleanup per FOUNDATION TENETS (Account Deletion Cleanup Obligation).
/// Note: Non-account entity cleanup (characters, locations) uses lib-resource (x-references), not event subscription.
/// </summary>
public partial class InventoryService : IBannouService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor via IBannouService interface.
    /// </summary>
    void IBannouService.RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        // Account deletion cleanup per FOUNDATION TENETS (Account Deletion Cleanup Obligation).
        // Account-owned containers must be deleted when the owning account is deleted.
        eventConsumer.RegisterHandler<IInventoryService, AccountDeletedEvent>(
            "account.deleted",
            async (svc, evt) => await ((InventoryService)svc).HandleAccountDeletedAsync(evt));

        // Self-subscribe to item-changing events for cache invalidation.
        // When items are placed, removed, or transferred, invalidate the cache so
        // running actors get fresh data on their next behavior tick.
        eventConsumer.RegisterHandler<IInventoryService, InventoryItemPlacedEvent>(
            "inventory.item.placed",
            async (svc, evt) => await ((InventoryService)svc).HandleItemPlacedAsync(evt));

        eventConsumer.RegisterHandler<IInventoryService, InventoryItemRemovedEvent>(
            "inventory.item.removed",
            async (svc, evt) => await ((InventoryService)svc).HandleItemRemovedAsync(evt));

        eventConsumer.RegisterHandler<IInventoryService, InventoryItemTransferredEvent>(
            "inventory.item.transferred",
            async (svc, evt) => await ((InventoryService)svc).HandleItemTransferredAsync(evt));

        // Container lifecycle events affect total_containers and has_space
        eventConsumer.RegisterHandler<IInventoryService, InventoryContainerCreatedEvent>(
            "inventory.container.created",
            async (svc, evt) => await ((InventoryService)svc).HandleContainerCreatedAsync(evt));

        eventConsumer.RegisterHandler<IInventoryService, InventoryContainerDeletedEvent>(
            "inventory.container.deleted",
            async (svc, evt) => await ((InventoryService)svc).HandleContainerDeletedAsync(evt));
    }

    /// <summary>
    /// Handles inventory.item.placed events.
    /// Invalidates the inventory cache for the container owner.
    /// </summary>
    private async Task HandleItemPlacedAsync(InventoryItemPlacedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.inventory", "InventoryService.HandleItemPlacedAsync");
        _logger.LogDebug(
            "Received inventory.item.placed event for owner {OwnerId}, invalidating cache",
            evt.OwnerId);

        _inventoryCache.Invalidate(evt.OwnerId);
        await Task.Yield();
    }

    /// <summary>
    /// Handles inventory.item.removed events.
    /// Invalidates the inventory cache for the container owner.
    /// </summary>
    private async Task HandleItemRemovedAsync(InventoryItemRemovedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.inventory", "InventoryService.HandleItemRemovedAsync");
        _logger.LogDebug(
            "Received inventory.item.removed event for owner {OwnerId}, invalidating cache",
            evt.OwnerId);

        _inventoryCache.Invalidate(evt.OwnerId);
        await Task.Yield();
    }

    /// <summary>
    /// Handles inventory.item.transferred events.
    /// Invalidates the inventory cache for both source and target owners.
    /// </summary>
    private async Task HandleItemTransferredAsync(InventoryItemTransferredEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.inventory", "InventoryService.HandleItemTransferredAsync");
        _logger.LogDebug(
            "Received inventory.item.transferred event from {SourceOwnerId} to {TargetOwnerId}, invalidating caches",
            evt.SourceOwnerId, evt.TargetOwnerId);

        _inventoryCache.Invalidate(evt.SourceOwnerId);
        _inventoryCache.Invalidate(evt.TargetOwnerId);
        await Task.Yield();
    }

    /// <summary>
    /// Handles inventory.container.created events.
    /// Invalidates the inventory cache for the container owner.
    /// </summary>
    private async Task HandleContainerCreatedAsync(InventoryContainerCreatedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.inventory", "InventoryService.HandleContainerCreatedAsync");
        _logger.LogDebug(
            "Received inventory.container.created event for owner {OwnerId}, invalidating cache",
            evt.OwnerId);

        _inventoryCache.Invalidate(evt.OwnerId);
        await Task.Yield();
    }

    /// <summary>
    /// Handles inventory.container.deleted events.
    /// Invalidates the inventory cache for the container owner.
    /// </summary>
    private async Task HandleContainerDeletedAsync(InventoryContainerDeletedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.inventory", "InventoryService.HandleContainerDeletedAsync");
        _logger.LogDebug(
            "Received inventory.container.deleted event for owner {OwnerId}, invalidating cache",
            evt.OwnerId);

        _inventoryCache.Invalidate(evt.OwnerId);
        await Task.Yield();
    }

    /// <summary>
    /// Handles account.deleted events by cleaning up all account-owned containers
    /// and their items. Per FOUNDATION TENETS: Account deletion is always CASCADE —
    /// data has no owner and must be removed.
    /// </summary>
    internal async Task HandleAccountDeletedAsync(AccountDeletedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.inventory", "InventoryService.HandleAccountDeleted");
        _logger.LogInformation("Handling account.deleted for account {AccountId}", evt.AccountId);

        try
        {
            await CleanupContainersForAccountAsync(evt.AccountId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clean up containers for account {AccountId}", evt.AccountId);
            await _messageBus.TryPublishErrorAsync(
                "inventory",
                "CleanupContainersForAccount",
                ex.GetType().Name,
                ex.Message,
                dependency: null,
                endpoint: "account.deleted",
                details: $"accountId={evt.AccountId}",
                stack: ex.StackTrace);
        }
    }

    /// <summary>
    /// Cleans up all containers for a given account by deleting each container
    /// and its items (using Destroy item handling). Per-container failures are
    /// logged as warnings and do not abort overall cleanup.
    /// </summary>
    /// <param name="accountId">The account whose containers should be cleaned up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal async Task<int> CleanupContainersForAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.inventory", "InventoryService.CleanupContainersForAccount");

        var ownerIndexKey = BuildOwnerIndexKey(ContainerOwnerType.Account, accountId);
        var idsJson = await _containerStringStore.GetAsync(ownerIndexKey, cancellationToken);
        var ids = string.IsNullOrEmpty(idsJson)
            ? new List<string>()
            : BannouJson.Deserialize<List<string>>(idsJson) ?? new List<string>();

        if (ids.Count == 0)
        {
            _logger.LogDebug("No containers found for account {AccountId}, skipping cleanup", accountId);
            return 0;
        }

        var deletedCount = 0;
        foreach (var id in ids)
        {
            try
            {
                if (!Guid.TryParse(id, out var containerId))
                {
                    _logger.LogWarning("Invalid container ID in account index: {Id}", id);
                    continue;
                }

                var result = await DeleteContainerAsync(
                    new DeleteContainerRequest
                    {
                        ContainerId = containerId,
                        ItemHandling = ItemHandling.Destroy
                    }, cancellationToken);

                if (result.Item1 == StatusCodes.OK || result.Item1 == StatusCodes.NotFound)
                {
                    deletedCount++;
                }
                else
                {
                    _logger.LogWarning("Failed to delete container {ContainerId} for account {AccountId}: status {Status}",
                        containerId, accountId, result.Item1);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clean up container {ContainerId} for account {AccountId}",
                    id, accountId);
            }
        }

        // Clean up the owner index itself (DeleteContainerAsync removes individual entries,
        // but if some failed, we still want to remove the index)
        await _containerStringStore.DeleteAsync(ownerIndexKey, cancellationToken);

        _logger.LogInformation("Cleaned up {DeletedCount} containers for account {AccountId}", deletedCount, accountId);
        return deletedCount;
    }
}
