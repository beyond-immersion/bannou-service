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
    /// Cleans up all encounter data for the deleted character including:
    /// - All perspectives belonging to the character
    /// - All encounters the character participated in
    /// - All related indexes (character, pair, location)
    /// </summary>
    /// <param name="evt">The event data containing the deleted character's ID.</param>
    public async Task OnCharacterDeletedAsync(CharacterDeletedEvent evt)
    {
        _logger.LogInformation("Processing character.deleted event for CharacterId: {CharacterId}, Reason: {Reason}",
            evt.CharacterId, evt.DeletedReason ?? "not specified");

        try
        {
            // Use the existing DeleteByCharacter implementation
            var (status, response) = await DeleteByCharacterAsync(
                new DeleteByCharacterRequest { CharacterId = evt.CharacterId },
                CancellationToken.None);

            if (status == StatusCodes.OK && response != null)
            {
                _logger.LogInformation(
                    "Successfully cleaned up encounter data for deleted character {CharacterId}: {EncountersDeleted} encounters, {PerspectivesDeleted} perspectives deleted",
                    evt.CharacterId, response.EncountersDeleted, response.PerspectivesDeleted);
            }
            else if (status == StatusCodes.NotFound)
            {
                _logger.LogDebug("No encounter data found for deleted character {CharacterId}", evt.CharacterId);
            }
            else
            {
                _logger.LogWarning(
                    "Unexpected status {Status} when cleaning up encounter data for deleted character {CharacterId}",
                    status, evt.CharacterId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cleaning up encounter data for deleted character {CharacterId}", evt.CharacterId);
            await _messageBus.TryPublishErrorAsync(
                "character-encounter",
                "OnCharacterDeleted",
                "event_handler_exception",
                ex.Message,
                dependency: "state",
                endpoint: "event:character.deleted",
                details: new { evt.CharacterId, evt.DeletedReason },
                stack: ex.StackTrace,
                cancellationToken: CancellationToken.None);
        }
    }
}
