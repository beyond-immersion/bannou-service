// =============================================================================
// Inventory Service Events
// Event consumer registration and handlers for cache invalidation.
// =============================================================================

using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Inventory.Caching;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace BeyondImmersion.BannouService.Inventory;

/// <summary>
/// Partial class for InventoryService event handling.
/// Contains event consumer registration and handler implementations for cache invalidation.
/// </summary>
public partial class InventoryService : IBannouService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor via IBannouService interface.
    /// </summary>
    void IBannouService.RegisterEventConsumers(IEventConsumer eventConsumer)
    {
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
}
