using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Analytics;

/// <summary>
/// Partial class for AnalyticsService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
public partial class AnalyticsService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<IAnalyticsService, GameSessionActionPerformedEvent>(
            "game-session.action.performed",
            async (svc, evt) => await ((AnalyticsService)svc).HandleGameActionPerformedAsync(evt));

        eventConsumer.RegisterHandler<IAnalyticsService, GameSessionCreatedEvent>(
            "game-session.created",
            async (svc, evt) => await ((AnalyticsService)svc).HandleGameSessionCreatedAsync(evt));

        eventConsumer.RegisterHandler<IAnalyticsService, GameSessionDeletedEvent>(
            "game-session.deleted",
            async (svc, evt) => await ((AnalyticsService)svc).HandleGameSessionDeletedAsync(evt));

    }

    /// <summary>
    /// Handles game-session.action.performed events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleGameActionPerformedAsync(GameSessionActionPerformedEvent evt)
    {
        // TODO: Implement game-session.action.performed event handling
        _logger.LogInformation("Received game-session.action.performed event for {GameServiceId}", evt.GameServiceId);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Handles game-session.created events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleGameSessionCreatedAsync(GameSessionCreatedEvent evt)
    {
        // TODO: Implement game-session.created event handling
        _logger.LogInformation("Received game-session.created event for {GameServiceId}", evt.GameServiceId);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Handles game-session.deleted events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleGameSessionDeletedAsync(GameSessionDeletedEvent evt)
    {
        // TODO: Implement game-session.deleted event handling
        _logger.LogInformation("Received game-session.deleted event for {GameServiceId}", evt.GameServiceId);
        await Task.CompletedTask;
    }
}
