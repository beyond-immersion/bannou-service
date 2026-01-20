using BeyondImmersion.BannouService.Events;

namespace BeyondImmersion.BannouService.CharacterEncounter;

/// <summary>
/// Partial class for CharacterEncounterService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
public partial class CharacterEncounterService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<ICharacterEncounterService, CharacterDeletedEvent>(
            "character.deleted",
            async (svc, evt) => await ((CharacterEncounterService)svc).OnCharacterDeletedAsync(evt));

    }

    /// <summary>
    /// Handles character.deleted events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task OnCharacterDeletedAsync(CharacterDeletedEvent evt)
    {
        // TODO: Implement character.deleted event handling
        _logger.LogInformation("[EVENT] Received character.deleted event");
        return Task.CompletedTask;
    }
}
