// =============================================================================
// Character History Service Events
// Event consumer registration and handlers for cache invalidation.
// =============================================================================

using BeyondImmersion.BannouService.CharacterHistory.Caching;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace BeyondImmersion.BannouService.CharacterHistory;

/// <summary>
/// Partial class for CharacterHistoryService event handling.
/// Contains event consumer registration and handler implementations for cache invalidation.
/// </summary>
public partial class CharacterHistoryService : IBannouService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor via IBannouService interface.
    /// </summary>
    void IBannouService.RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        // Self-subscribe to our own backstory events for cache invalidation.
        // This ensures cache is invalidated on ALL nodes in a multi-instance deployment,
        // not just the node that processed the request.
        eventConsumer.RegisterHandler<ICharacterHistoryService, CharacterBackstoryCreatedEvent>(
            CharacterHistoryPublishedTopics.CharacterBackstoryCreated,
            async (svc, evt) => await ((CharacterHistoryService)svc).HandleBackstoryCreatedAsync(evt));

        eventConsumer.RegisterHandler<ICharacterHistoryService, CharacterBackstoryUpdatedEvent>(
            CharacterHistoryPublishedTopics.CharacterBackstoryUpdated,
            async (svc, evt) => await ((CharacterHistoryService)svc).HandleBackstoryUpdatedAsync(evt));

        eventConsumer.RegisterHandler<ICharacterHistoryService, CharacterBackstoryDeletedEvent>(
            CharacterHistoryPublishedTopics.CharacterBackstoryDeleted,
            async (svc, evt) => await ((CharacterHistoryService)svc).HandleBackstoryDeletedAsync(evt));

        eventConsumer.RegisterHandler<ICharacterHistoryService, CharacterHistoryDeletedEvent>(
            CharacterHistoryPublishedTopics.CharacterHistoryDeleted,
            async (svc, evt) => await ((CharacterHistoryService)svc).HandleHistoryDeletedAsync(evt));
    }

    /// <summary>
    /// Handles character-history.backstory.created events.
    /// Invalidates the backstory cache for the affected character.
    /// </summary>
    private async Task HandleBackstoryCreatedAsync(CharacterBackstoryCreatedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-history", "CharacterHistoryService.HandleBackstoryCreatedAsync");
        _logger.LogDebug(
            "Received backstory.created event for character {CharacterId}, invalidating cache",
            evt.CharacterId);

        _backstoryCache.Invalidate(evt.CharacterId);

        await Task.Yield();
    }

    /// <summary>
    /// Handles character-history.backstory.updated events.
    /// Invalidates the backstory cache for the affected character.
    /// </summary>
    private async Task HandleBackstoryUpdatedAsync(CharacterBackstoryUpdatedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-history", "CharacterHistoryService.HandleBackstoryUpdatedAsync");
        _logger.LogDebug(
            "Received backstory.updated event for character {CharacterId}, invalidating cache",
            evt.CharacterId);

        _backstoryCache.Invalidate(evt.CharacterId);

        await Task.Yield();
    }

    /// <summary>
    /// Handles character-history.backstory.deleted events.
    /// Invalidates the backstory cache for the affected character.
    /// </summary>
    private async Task HandleBackstoryDeletedAsync(CharacterBackstoryDeletedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-history", "CharacterHistoryService.HandleBackstoryDeletedAsync");
        _logger.LogDebug(
            "Received backstory.deleted event for character {CharacterId}, invalidating cache",
            evt.CharacterId);

        _backstoryCache.Invalidate(evt.CharacterId);

        await Task.Yield();
    }

    /// <summary>
    /// Handles character-history.deleted events.
    /// Invalidates the backstory cache for the affected character.
    /// </summary>
    private async Task HandleHistoryDeletedAsync(CharacterHistoryDeletedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-history", "CharacterHistoryService.HandleHistoryDeletedAsync");
        _logger.LogDebug(
            "Received character-history.deleted event for character {CharacterId}, invalidating cache",
            evt.CharacterId);

        _backstoryCache.Invalidate(evt.CharacterId);

        await Task.Yield();
    }
}
