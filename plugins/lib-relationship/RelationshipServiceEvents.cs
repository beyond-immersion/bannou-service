// =============================================================================
// Relationship Service Events
// Event consumer registration and handlers for cache invalidation.
// =============================================================================

using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Relationship.Caching;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace BeyondImmersion.BannouService.Relationship;

/// <summary>
/// Partial class for RelationshipService event handling.
/// Contains event consumer registration and handler implementations for cache invalidation.
/// </summary>
public partial class RelationshipService : IBannouService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor via IBannouService interface.
    /// </summary>
    void IBannouService.RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        // Self-subscribe to relationship-changing events for cache invalidation.
        // When relationships are created, updated, or deleted, invalidate the cache so
        // running actors get fresh data on their next behavior tick.
        eventConsumer.RegisterHandler<IRelationshipService, RelationshipCreatedEvent>(
            "relationship.created",
            async (svc, evt) => await ((RelationshipService)svc).HandleRelationshipCreatedAsync(evt));

        eventConsumer.RegisterHandler<IRelationshipService, RelationshipUpdatedEvent>(
            "relationship.updated",
            async (svc, evt) => await ((RelationshipService)svc).HandleRelationshipUpdatedAsync(evt));

        eventConsumer.RegisterHandler<IRelationshipService, RelationshipDeletedEvent>(
            "relationship.deleted",
            async (svc, evt) => await ((RelationshipService)svc).HandleRelationshipDeletedAsync(evt));
    }

    /// <summary>
    /// Handles relationship.created events.
    /// Invalidates the relationship cache for any character entities involved.
    /// </summary>
    private async Task HandleRelationshipCreatedAsync(RelationshipCreatedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.relationship", "RelationshipService.HandleRelationshipCreatedAsync");
        _logger.LogDebug(
            "Received relationship.created event for {RelationshipId}, invalidating caches",
            evt.RelationshipId);

        InvalidateCharacterCache(evt.Entity1Id, evt.Entity1Type);
        InvalidateCharacterCache(evt.Entity2Id, evt.Entity2Type);
        await Task.Yield();
    }

    /// <summary>
    /// Handles relationship.updated events.
    /// Invalidates the relationship cache for any character entities involved.
    /// </summary>
    private async Task HandleRelationshipUpdatedAsync(RelationshipUpdatedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.relationship", "RelationshipService.HandleRelationshipUpdatedAsync");
        _logger.LogDebug(
            "Received relationship.updated event for {RelationshipId}, invalidating caches",
            evt.RelationshipId);

        InvalidateCharacterCache(evt.Entity1Id, evt.Entity1Type);
        InvalidateCharacterCache(evt.Entity2Id, evt.Entity2Type);
        await Task.Yield();
    }

    /// <summary>
    /// Handles relationship.deleted events.
    /// Invalidates the relationship cache for any character entities involved.
    /// </summary>
    private async Task HandleRelationshipDeletedAsync(RelationshipDeletedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.relationship", "RelationshipService.HandleRelationshipDeletedAsync");
        _logger.LogDebug(
            "Received relationship.deleted event for {RelationshipId}, invalidating caches",
            evt.RelationshipId);

        InvalidateCharacterCache(evt.Entity1Id, evt.Entity1Type);
        InvalidateCharacterCache(evt.Entity2Id, evt.Entity2Type);
        await Task.Yield();
    }

    /// <summary>
    /// Invalidates the relationship data cache for an entity if it is a Character.
    /// Only Character-type entities participate in ABML behavior evaluation.
    /// </summary>
    private void InvalidateCharacterCache(Guid entityId, EntityType entityType)
    {
        if (entityType == EntityType.Character)
        {
            _relationshipCache.Invalidate(entityId);
        }
    }
}
