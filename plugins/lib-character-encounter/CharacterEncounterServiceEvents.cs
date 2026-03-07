// =============================================================================
// Character Encounter Service Events
// Event consumer registration and handlers for cache invalidation.
// =============================================================================

using BeyondImmersion.BannouService.CharacterEncounter.Caching;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace BeyondImmersion.BannouService.CharacterEncounter;

/// <summary>
/// Partial class for CharacterEncounterService event handling.
/// Contains event consumer registration and handler implementations for cache invalidation.
/// </summary>
public partial class CharacterEncounterService : IBannouService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor via IBannouService interface.
    /// </summary>
    void IBannouService.RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        // Self-subscribe to our own events for cache invalidation.
        // This ensures cache is invalidated on ALL nodes in a multi-instance deployment,
        // not just the node that processed the request.
        eventConsumer.RegisterHandler<ICharacterEncounterService, EncounterRecordedEvent>(
            ENCOUNTER_RECORDED_TOPIC,
            async (svc, evt) => await ((CharacterEncounterService)svc).HandleEncounterRecordedAsync(evt));

        eventConsumer.RegisterHandler<ICharacterEncounterService, EncounterPerspectiveUpdatedEvent>(
            ENCOUNTER_PERSPECTIVE_UPDATED_TOPIC,
            async (svc, evt) => await ((CharacterEncounterService)svc).HandlePerspectiveUpdatedAsync(evt));

        eventConsumer.RegisterHandler<ICharacterEncounterService, EncounterMemoryRefreshedEvent>(
            ENCOUNTER_MEMORY_REFRESHED_TOPIC,
            async (svc, evt) => await ((CharacterEncounterService)svc).HandleMemoryRefreshedAsync(evt));

        eventConsumer.RegisterHandler<ICharacterEncounterService, EncounterMemoryFadedEvent>(
            ENCOUNTER_MEMORY_FADED_TOPIC,
            async (svc, evt) => await ((CharacterEncounterService)svc).HandleMemoryFadedAsync(evt));

        eventConsumer.RegisterHandler<ICharacterEncounterService, EncounterDeletedEvent>(
            ENCOUNTER_DELETED_TOPIC,
            async (svc, evt) => await ((CharacterEncounterService)svc).HandleEncounterDeletedAsync(evt));
    }

    /// <summary>
    /// Handles encounter.recorded events.
    /// Invalidates the encounter cache for all participants.
    /// </summary>
    private async Task HandleEncounterRecordedAsync(EncounterRecordedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.HandleEncounterRecordedAsync");
        _logger.LogDebug(
            "Received encounter.recorded event for encounter {EncounterId}, invalidating cache for {Count} participants",
            evt.EncounterId, evt.ParticipantIds.Count);

        foreach (var participantId in evt.ParticipantIds)
        {
            _encounterDataCache.Invalidate(participantId);
        }

        await Task.Yield();
    }

    /// <summary>
    /// Handles encounter.perspective.updated events.
    /// Invalidates the encounter cache for the affected character.
    /// </summary>
    private async Task HandlePerspectiveUpdatedAsync(EncounterPerspectiveUpdatedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.HandlePerspectiveUpdatedAsync");
        _logger.LogDebug(
            "Received encounter.perspective.updated event for character {CharacterId}, invalidating cache",
            evt.CharacterId);

        _encounterDataCache.Invalidate(evt.CharacterId);

        await Task.Yield();
    }

    /// <summary>
    /// Handles encounter.memory.refreshed events.
    /// Invalidates the encounter cache for the affected character.
    /// </summary>
    private async Task HandleMemoryRefreshedAsync(EncounterMemoryRefreshedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.HandleMemoryRefreshedAsync");
        _logger.LogDebug(
            "Received encounter.memory.refreshed event for character {CharacterId}, invalidating cache",
            evt.CharacterId);

        _encounterDataCache.Invalidate(evt.CharacterId);

        await Task.Yield();
    }

    /// <summary>
    /// Handles encounter.memory.faded events.
    /// Invalidates the encounter cache for the affected character.
    /// </summary>
    private async Task HandleMemoryFadedAsync(EncounterMemoryFadedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.HandleMemoryFadedAsync");
        _logger.LogDebug(
            "Received encounter.memory.faded event for character {CharacterId}, invalidating cache",
            evt.CharacterId);

        _encounterDataCache.Invalidate(evt.CharacterId);

        await Task.Yield();
    }

    /// <summary>
    /// Handles encounter.deleted events.
    /// Invalidates the encounter cache for all participants.
    /// </summary>
    private async Task HandleEncounterDeletedAsync(EncounterDeletedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "CharacterEncounterService.HandleEncounterDeletedAsync");
        _logger.LogDebug(
            "Received encounter.deleted event for encounter {EncounterId}, invalidating cache for {Count} participants",
            evt.EncounterId, evt.ParticipantIds.Count);

        foreach (var participantId in evt.ParticipantIds)
        {
            _encounterDataCache.Invalidate(participantId);
        }

        await Task.Yield();
    }
}
