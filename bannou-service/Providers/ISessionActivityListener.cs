// =============================================================================
// Session Activity Listener Interface
// Enables in-process notification for session lifecycle events and heartbeats.
// Connect (L1) discovers listeners via DI; co-located L1 services implement them.
// =============================================================================

namespace BeyondImmersion.BannouService.Providers;

/// <summary>
/// Listener interface for receiving session lifecycle and heartbeat notifications via DI.
/// </summary>
/// <remarks>
/// <para>
/// This interface enables direct in-process delivery of session lifecycle events
/// from Connect to co-located services without requiring event bus subscriptions:
/// </para>
/// <list type="bullet">
///   <item>Connect (L1) discovers this interface via <c>IEnumerable&lt;ISessionActivityListener&gt;</c></item>
///   <item>Co-located L1 services implement the listener and register as Singleton</item>
///   <item>Connect calls listeners AFTER broadcast events are published via <c>IMessageBus</c></item>
///   <item>Listener failures are logged as warnings and never affect Connect or other listeners</item>
/// </list>
/// <para>
/// <b>Why DI instead of events?</b> Connect and Permission are both L1 AppFoundation —
/// always co-located, always available. In-process DI calls provide guaranteed delivery
/// with zero event bus overhead. The broadcast events (<c>session.connected</c>,
/// <c>session.disconnected</c>, <c>session.reconnected</c>) are still published for
/// any future distributed consumers. Heartbeat notifications are DI-only (not published
/// as events) due to their high frequency (every 30 seconds per session).
/// </para>
/// <para>
/// <b>DISTRIBUTED SAFETY — LOCAL-ONLY FAN-OUT:</b> This is a push-based Listener pattern.
/// In multi-node deployments, only listeners on the node that owns the WebSocket connection
/// are called. Other nodes are NOT notified via this interface. This is safe because
/// listener reactions write to distributed state (Redis), so all nodes see the updated
/// state on their next read. If per-node awareness is required (e.g., invalidating a local
/// cache on every node), the consumer MUST subscribe to broadcast events via
/// <c>IEventConsumer</c> instead. See SERVICE-HIERARCHY.md §"DI Provider vs Listener".
/// </para>
/// <para>
/// <b>Example Implementation</b>:
/// </para>
/// <code>
/// public class PermissionSessionActivityListener : ISessionActivityListener
/// {
///     public Task OnHeartbeatAsync(Guid sessionId, CancellationToken ct)
///     {
///         // Refresh Redis TTL on session data keys
///     }
///     public Task OnConnectedAsync(Guid sessionId, Guid accountId,
///         IReadOnlyList&lt;string&gt;? roles, IReadOnlyList&lt;string&gt;? authorizations, CancellationToken ct)
///     {
///         // Set up session state and compile initial capabilities
///     }
///     public Task OnReconnectedAsync(Guid sessionId, CancellationToken ct)
///     {
///         // Refresh TTL, recompile from existing Redis state
///     }
///     public Task OnDisconnectedAsync(Guid sessionId, bool reconnectable,
///         TimeSpan? reconnectionWindow, CancellationToken ct)
///     {
///         // Remove from active connections, align TTL to reconnection window
///     }
/// }
/// // DI registration: services.AddSingleton&lt;ISessionActivityListener, PermissionSessionActivityListener&gt;();
/// </code>
/// </remarks>
public interface ISessionActivityListener
{
    /// <summary>
    /// Called on client heartbeat (every ~30 seconds per session).
    /// Use for TTL refresh on session data keys.
    /// </summary>
    /// <param name="sessionId">The session that heartbeated.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// High frequency: called every heartbeat interval per connected session.
    /// Implementations MUST be lightweight (e.g., Redis EXPIRE, not full reads).
    /// Exceptions are caught by Connect and logged as warnings — they never
    /// affect the heartbeat operation or other listeners.
    /// </remarks>
    Task OnHeartbeatAsync(Guid sessionId, CancellationToken ct);

    /// <summary>
    /// Called when a new session connects and its RabbitMQ subscription is established.
    /// </summary>
    /// <param name="sessionId">The connected session.</param>
    /// <param name="accountId">Account owning the session.</param>
    /// <param name="roles">User roles from JWT (e.g., "user", "admin"), or null for pre-auth connections.</param>
    /// <param name="authorizations">Authorization strings in "stubName:state" format, or null.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// Called AFTER Connect publishes the <c>session.connected</c> broadcast event.
    /// Implementations should handle their own errors internally. Exceptions are caught
    /// by Connect and logged as warnings — they never affect the connection or other listeners.
    /// </remarks>
    Task OnConnectedAsync(
        Guid sessionId,
        Guid accountId,
        IReadOnlyList<string>? roles,
        IReadOnlyList<string>? authorizations,
        CancellationToken ct);

    /// <summary>
    /// Called when a session reconnects within its reconnection window.
    /// </summary>
    /// <param name="sessionId">The reconnected session.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// Called AFTER Connect publishes the <c>session.reconnected</c> broadcast event.
    /// Session state was preserved in Redis during the reconnection window.
    /// Implementations should refresh TTLs and recompile from existing distributed state.
    /// Exceptions are caught by Connect and logged as warnings — they never affect
    /// the reconnection or other listeners.
    /// </remarks>
    Task OnReconnectedAsync(Guid sessionId, CancellationToken ct);

    /// <summary>
    /// Called when a session disconnects (gracefully or forced).
    /// </summary>
    /// <param name="sessionId">The disconnected session.</param>
    /// <param name="reconnectable">Whether the session can reconnect within the reconnection window.</param>
    /// <param name="reconnectionWindow">
    /// Duration of the reconnection window when <paramref name="reconnectable"/> is true.
    /// Listeners can use this to align Redis TTLs with Connect's actual reconnection timeout.
    /// Null when not reconnectable (session data should be cleaned up immediately).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// Called AFTER Connect publishes the <c>session.disconnected</c> broadcast event.
    /// Implementations should handle their own errors internally. Exceptions are caught
    /// by Connect and logged as warnings — they never affect the disconnect or other listeners.
    /// </remarks>
    Task OnDisconnectedAsync(
        Guid sessionId,
        bool reconnectable,
        TimeSpan? reconnectionWindow,
        CancellationToken ct);
}
