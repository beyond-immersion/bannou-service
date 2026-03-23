using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Divine;

/// <summary>
/// Partial class for DivineService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
public partial class DivineService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<IDivineService, CharacterCreatedEvent>(
            "character.created",
            async (svc, evt) => await ((DivineService)svc).HandleCharacterCreatedAsync(evt));

        eventConsumer.RegisterHandler<IDivineService, CharacterUpdatedEvent>(
            "character.updated",
            async (svc, evt) => await ((DivineService)svc).HandleCharacterUpdatedAsync(evt));
    }

    /// <summary>
    /// Handles character.created events for patron deity auto-bonding.
    /// If the character has a patronDeityCode, looks up the deity in the bond template
    /// registry and auto-initiates seed bonds between the character's domain seeds and
    /// the patron god's matching seeds.
    /// </summary>
    /// <param name="evt">The character created event data.</param>
    public async Task HandleCharacterCreatedAsync(CharacterCreatedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.divine", "DivineService.HandleCharacterCreatedAsync");
        // TODO: If evt.PatronDeityCode is set, look up deity in bond template registry, auto-initiate seed bonds
        _logger.LogInformation("Received {Topic} event for character {CharacterId}", "character.created", evt.CharacterId);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Handles character.updated events for patron deity re-evaluation.
    /// If changedFields includes patronDeityCode, dissolves old patron seed bonds
    /// (if any) and creates new bonds per the new deity's bond template.
    /// </summary>
    /// <param name="evt">The character updated event data.</param>
    public async Task HandleCharacterUpdatedAsync(CharacterUpdatedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.divine", "DivineService.HandleCharacterUpdatedAsync");
        // TODO: If changedFields includes patronDeityCode, dissolve old bonds, create new bonds per deity template
        _logger.LogInformation("Received {Topic} event for character {CharacterId}", "character.updated", evt.CharacterId);
        await Task.CompletedTask;
    }
}
