using BeyondImmersion.BannouService.Connect.Protocol;
using System.Net.WebSockets;

namespace BeyondImmersion.BannouService.Connect;

/// <summary>
/// Internal data models for ConnectService.
/// </summary>
/// <remarks>
/// <para>
/// This file contains internal data models, DTOs, and helper structures used
/// exclusively by this service. These are NOT exposed via the API and are NOT
/// generated from schemas.
/// </para>
/// <para>
/// <b>When to add models here:</b>
/// <list type="bullet">
///   <item>Storage models for state stores (different from API request/response types)</item>
///   <item>Cache entry structures</item>
///   <item>Internal DTOs for service-to-service communication not exposed in API</item>
///   <item>Helper records for intermediate processing</item>
/// </list>
/// </para>
/// <para>
/// <b>IMPLEMENTATION TENETS - Type Safety:</b> Internal models MUST use proper C# types
/// (enums, Guids, DateTimeOffset) - never string representations. "JSON requires strings"
/// is FALSE - BannouJson handles serialization correctly.
/// </para>
/// </remarks>
public partial class ConnectService
{
    // This partial class declaration exists to signal that the models below
    // are owned by and used exclusively by this service. The models themselves
    // are defined at namespace level as internal classes.
}

/// <summary>
/// Data model for storing connection state in distributed state store.
/// Contains only serializable fields suitable for lib-state storage.
/// </summary>
public class ConnectionStateData
{
    /// <summary>
    /// Unique session ID for this connection.
    /// </summary>
    public Guid SessionId { get; set; }

    /// <summary>
    /// Account ID owning this session.
    /// </summary>
    public Guid? AccountId { get; set; }

    // Store as Unix epoch timestamps (long) to avoid System.Text.Json DateTimeOffset serialization issues
    public long ConnectedAtUnix { get; set; }
    public long LastActivityUnix { get; set; }
    public long? ReconnectionExpiresAtUnix { get; set; }
    public long? DisconnectedAtUnix { get; set; }

    /// <summary>
    /// When this connection was established.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public DateTimeOffset ConnectedAt
    {
        get => DateTimeOffset.FromUnixTimeSeconds(ConnectedAtUnix);
        set => ConnectedAtUnix = value.ToUnixTimeSeconds();
    }

    /// <summary>
    /// Last activity timestamp for connection management.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public DateTimeOffset LastActivity
    {
        get => DateTimeOffset.FromUnixTimeSeconds(LastActivityUnix);
        set => LastActivityUnix = value.ToUnixTimeSeconds();
    }

    /// <summary>
    /// Token for reconnecting to this session.
    /// </summary>
    public string? ReconnectionToken { get; set; }

    /// <summary>
    /// When the reconnection window expires.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public DateTimeOffset? ReconnectionExpiresAt
    {
        get => ReconnectionExpiresAtUnix.HasValue ? DateTimeOffset.FromUnixTimeSeconds(ReconnectionExpiresAtUnix.Value) : null;
        set => ReconnectionExpiresAtUnix = value?.ToUnixTimeSeconds();
    }

    /// <summary>
    /// When the connection was disconnected (null if still connected).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public DateTimeOffset? DisconnectedAt
    {
        get => DisconnectedAtUnix.HasValue ? DateTimeOffset.FromUnixTimeSeconds(DisconnectedAtUnix.Value) : null;
        set => DisconnectedAtUnix = value?.ToUnixTimeSeconds();
    }

    /// <summary>
    /// User roles at time of disconnect (preserved for reconnection).
    /// </summary>
    public List<string>? UserRoles { get; set; }

    /// <summary>
    /// Authorization strings at time of disconnect (preserved for reconnection).
    /// Format: "{stubName}:{state}" (e.g., "my-game:authorized")
    /// </summary>
    public List<string>? Authorizations { get; set; }

    /// <summary>
    /// Whether this session is in reconnection window (disconnected but not expired).
    /// </summary>
    public bool IsInReconnectionWindow =>
        DisconnectedAt.HasValue &&
        ReconnectionExpiresAt.HasValue &&
        DateTimeOffset.UtcNow < ReconnectionExpiresAt.Value;
}

/// <summary>
/// Heartbeat data for tracking active sessions.
/// </summary>
public class SessionHeartbeat
{
    /// <summary>
    /// The session ID this heartbeat is for.
    /// </summary>
    public Guid SessionId { get; set; }

    /// <summary>
    /// The instance ID managing this session.
    /// </summary>
    public Guid InstanceId { get; set; }

    // Store as Unix epoch timestamp (long) to avoid System.Text.Json DateTimeOffset serialization issues
    public long LastSeenUnix { get; set; }

    /// <summary>
    /// When the session was last seen.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public DateTimeOffset LastSeen
    {
        get => DateTimeOffset.FromUnixTimeSeconds(LastSeenUnix);
        set => LastSeenUnix = value.ToUnixTimeSeconds();
    }

    /// <summary>
    /// Number of active connections for this session (usually 1).
    /// </summary>
    public int ConnectionCount { get; set; }
}

/// <summary>
/// Event published when session state changes.
/// </summary>
public class SessionEvent
{
    /// <summary>
    /// Type of session event (e.g., "connected", "disconnected", "reconnected").
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// The session ID this event is for.
    /// </summary>
    public Guid SessionId { get; set; }

    // Store as Unix epoch timestamp (long) to avoid System.Text.Json DateTimeOffset serialization issues
    public long TimestampUnix { get; set; }

    /// <summary>
    /// When this event occurred.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public DateTimeOffset Timestamp
    {
        get => DateTimeOffset.FromUnixTimeSeconds(TimestampUnix);
        set => TimestampUnix = value.ToUnixTimeSeconds();
    }

    /// <summary>
    /// Additional event-specific data.
    /// </summary>
    public object? Data { get; set; }
}

/// <summary>
/// Represents a WebSocket connection with its associated state.
/// </summary>
public class WebSocketConnection
{
    public string SessionId { get; }
    public WebSocket WebSocket { get; }
    public ConnectionState ConnectionState { get; }
    public DateTimeOffset CreatedAt { get; }

    /// <summary>
    /// Metadata dictionary for storing connection-specific flags like forced_disconnect.
    /// </summary>
    public Dictionary<string, object> Metadata { get; } = new();

    public WebSocketConnection(string sessionId, WebSocket webSocket, ConnectionState connectionState)
    {
        SessionId = sessionId;
        WebSocket = webSocket;
        ConnectionState = connectionState;
        CreatedAt = DateTimeOffset.UtcNow;
    }
}

/// <summary>
/// Registry entry for a Connect instance in the broadcast sorted set.
/// Serialized as JSON for the sorted set member; score is the heartbeat Unix timestamp.
/// </summary>
internal record BroadcastRegistryEntry(
    Guid InstanceId,
    string InternalUrl,
    BroadcastMode BroadcastMode);
