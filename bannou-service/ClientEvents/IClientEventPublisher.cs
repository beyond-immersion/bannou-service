using BeyondImmersion.Bannou.Core;

namespace BeyondImmersion.BannouService.ClientEvents;

/// <summary>
/// Interface for publishing events to specific WebSocket clients via session-specific channels.
/// Services use this to push events to clients without knowing which Connect instance handles them.
/// </summary>
/// <remarks>
/// <para>
/// Events are published to RabbitMQ topics named CONNECT_SESSION_{sessionId}.
/// The Connect service subscribes to these topics and forwards events to the appropriate WebSocket.
/// </para>
/// <para>
/// <b>IMPORTANT - Connect-Internal Events:</b> Some events (ShortcutPublishedEvent, ShortcutRevokedEvent)
/// are consumed by Connect internally and NOT forwarded to the WebSocket client. Connect updates its
/// internal state and sends an updated capability manifest to the client instead. Clients never see
/// these raw events - they only see the resulting manifest changes.
/// </para>
/// <para>
/// All events must have a valid eventName that exists in the ClientEventWhitelist.
/// Events with invalid names will be rejected by the publisher.
/// </para>
/// </remarks>
public interface IClientEventPublisher
{
    /// <summary>
    /// Publishes an event to a specific client session.
    /// </summary>
    /// <typeparam name="TEvent">Type of client event (must inherit from BaseClientEvent).</typeparam>
    /// <param name="sessionId">The target session ID.</param>
    /// <param name="eventData">The event data to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the event was published successfully, false otherwise.</returns>
    /// <exception cref="ArgumentException">Thrown if the event name is not in the whitelist.</exception>
    Task<bool> PublishToSessionAsync<TEvent>(
        string sessionId,
        TEvent eventData,
        CancellationToken cancellationToken = default)
        where TEvent : BaseClientEvent;

    /// <summary>
    /// Publishes an event to multiple client sessions.
    /// </summary>
    /// <typeparam name="TEvent">Type of client event (must inherit from BaseClientEvent).</typeparam>
    /// <param name="sessionIds">The target session IDs.</param>
    /// <param name="eventData">The event data to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of sessions the event was successfully published to.</returns>
    /// <exception cref="ArgumentException">Thrown if the event name is not in the whitelist.</exception>
    Task<int> PublishToSessionsAsync<TEvent>(
        IEnumerable<string> sessionIds,
        TEvent eventData,
        CancellationToken cancellationToken = default)
        where TEvent : BaseClientEvent;

    /// <summary>
    /// Checks if an event name is valid (exists in the whitelist).
    /// </summary>
    /// <param name="eventName">The event name to check.</param>
    /// <returns>True if the event name is valid.</returns>
    bool IsValidEventName(string? eventName);
}
