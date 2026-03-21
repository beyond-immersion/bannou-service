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
        eventConsumer.RegisterHandler<IDivineService, AnalyticsScoreUpdatedEvent>(
            "analytics.score.updated",
            async (svc, evt) => await ((DivineService)svc).HandleAnalyticsScoreUpdatedAsync(evt));

        eventConsumer.RegisterHandler<IDivineService, CharacterCreatedEvent>(
            "character.created",
            async (svc, evt) => await ((DivineService)svc).HandleCharacterCreatedAsync(evt));

        eventConsumer.RegisterHandler<IDivineService, CharacterUpdatedEvent>(
            "character.updated",
            async (svc, evt) => await ((DivineService)svc).HandleCharacterUpdatedAsync(evt));
    }

    /// <summary>
    /// Handles analytics.score.updated events for domain-relevant divinity generation.
    /// Maps analytics categories to domain codes and queues divinity generation events.
    /// </summary>
    /// <param name="evt">The analytics score updated event data.</param>
    public async Task HandleAnalyticsScoreUpdatedAsync(AnalyticsScoreUpdatedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.divine", "DivineService.HandleAnalyticsScoreUpdatedAsync");
        // TODO: Map analytics categories to domain codes, queue DivinityEventModel entries
        _logger.LogInformation("Received {Topic} event for game service {GameServiceId}", "analytics.score.updated", evt.GameServiceId);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Handles character.created events for patron deity auto-bonding.
    /// When a character is created, checks if the character's realm has patron deities
    /// and auto-bonds the character to the appropriate deity based on species/location.
    /// </summary>
    /// <param name="evt">The character created event data.</param>
    public async Task HandleCharacterCreatedAsync(CharacterCreatedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.divine", "DivineService.HandleCharacterCreatedAsync");
        // TODO: Check realm patron deities, auto-bond character
        _logger.LogInformation("Received {Topic} event for character {CharacterId}", "character.created", evt.CharacterId);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Handles character.updated events for patron deity re-evaluation.
    /// When a character's species or realm changes, re-evaluates patron deity bindings.
    /// </summary>
    /// <param name="evt">The character updated event data.</param>
    public async Task HandleCharacterUpdatedAsync(CharacterUpdatedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.divine", "DivineService.HandleCharacterUpdatedAsync");
        // TODO: Re-evaluate patron deity bindings on character changes
        _logger.LogInformation("Received {Topic} event for character {CharacterId}", "character.updated", evt.CharacterId);
        await Task.CompletedTask;
    }
}
