using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Matchmaking;

/// <summary>
/// Partial class for MatchmakingService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
public partial class MatchmakingService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<IMatchmakingService, SessionConnectedEvent>(
            "session.connected",
            async (svc, evt) => await ((MatchmakingService)svc).HandleSessionConnectedAsync(evt));

        eventConsumer.RegisterHandler<IMatchmakingService, SessionDisconnectedEvent>(
            "session.disconnected",
            async (svc, evt) => await ((MatchmakingService)svc).HandleSessionDisconnectedAsync(evt));

        eventConsumer.RegisterHandler<IMatchmakingService, SessionReconnectedEvent>(
            "session.reconnected",
            async (svc, evt) => await ((MatchmakingService)svc).HandleSessionReconnectedAsync(evt));

    }

    /// <summary>
    /// Handles session.connected events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleSessionConnectedAsync(SessionConnectedEvent evt)
    {
        // TODO: Implement session.connected event handling
        _logger.LogInformation("Received session.connected event");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles session.disconnected events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleSessionDisconnectedAsync(SessionDisconnectedEvent evt)
    {
        // TODO: Implement session.disconnected event handling
        _logger.LogInformation("Received session.disconnected event");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles session.reconnected events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleSessionReconnectedAsync(SessionReconnectedEvent evt)
    {
        // TODO: Implement session.reconnected event handling
        _logger.LogInformation("Received session.reconnected event");
        return Task.CompletedTask;
    }
}
