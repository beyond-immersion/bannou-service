using BeyondImmersion.BannouService.Events;

namespace BeyondImmersion.BannouService.GameSession;

/// <summary>
/// Partial class for GameSessionService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
public partial class GameSessionService
{
    /// <summary>
    /// Registers event consumers for Bannou pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<IGameSessionService, SessionConnectedEvent>(
            "session.connected",
            async (svc, evt) => await ((GameSessionService)svc).HandleSessionConnectedAsync(evt));

        eventConsumer.RegisterHandler<IGameSessionService, SessionDisconnectedEvent>(
            "session.disconnected",
            async (svc, evt) => await ((GameSessionService)svc).HandleSessionDisconnectedAsync(evt));

        eventConsumer.RegisterHandler<IGameSessionService, SessionReconnectedEvent>(
            "session.reconnected",
            async (svc, evt) => await ((GameSessionService)svc).HandleSessionReconnectedAsync(evt));

        eventConsumer.RegisterHandler<IGameSessionService, SubscriptionUpdatedEvent>(
            "subscription.updated",
            async (svc, evt) => await ((GameSessionService)svc).HandleSubscriptionUpdatedAsync(evt));

    }

    /// <summary>
    /// Handles session.connected events.
    /// Delegates to the internal handler with extracted parameters.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleSessionConnectedAsync(SessionConnectedEvent evt)
    {
        await HandleSessionConnectedInternalAsync(evt.SessionId.ToString(), evt.AccountId.ToString());
    }

    /// <summary>
    /// Handles session.disconnected events.
    /// Delegates to the internal handler with extracted parameters.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleSessionDisconnectedAsync(SessionDisconnectedEvent evt)
    {
        await HandleSessionDisconnectedInternalAsync(evt.SessionId.ToString(), evt.AccountId);
    }

    /// <summary>
    /// Handles session.reconnected events.
    /// Re-publishes shortcuts as if it were a new connection.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleSessionReconnectedAsync(SessionReconnectedEvent evt)
    {
        // Reconnection is treated the same as a new connection for shortcut publishing
        await HandleSessionConnectedInternalAsync(evt.SessionId.ToString(), evt.AccountId.ToString());
    }

    /// <summary>
    /// Handles subscription.updated events.
    /// Updates subscription cache and publishes/revokes shortcuts for affected sessions.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleSubscriptionUpdatedAsync(SubscriptionUpdatedEvent evt)
    {
        await HandleSubscriptionUpdatedInternalAsync(
            evt.AccountId,
            evt.StubName,
            evt.Action,
            evt.IsActive);
    }
}
