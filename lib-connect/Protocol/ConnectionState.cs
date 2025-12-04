using System;
using System.Collections.Generic;

namespace BeyondImmersion.BannouService.Connect.Protocol;

/// <summary>
/// Represents the state of a WebSocket connection.
/// Dependency-free class for Client SDK extraction.
/// Thread-safe for concurrent capability updates.
/// </summary>
public class ConnectionState
{
    private readonly object _mappingsLock = new object();

    /// <summary>
    /// Unique session ID for this connection
    /// </summary>
    public string SessionId { get; }

    /// <summary>
    /// When this connection was established
    /// </summary>
    public DateTimeOffset ConnectedAt { get; }

    /// <summary>
    /// Last activity timestamp for connection management
    /// </summary>
    public DateTimeOffset LastActivity { get; set; }

    /// <summary>
    /// Service GUID mappings for this session (service name -> GUID)
    /// </summary>
    public Dictionary<string, Guid> ServiceMappings { get; }

    /// <summary>
    /// Reverse GUID mappings for routing (GUID -> service name)
    /// </summary>
    public Dictionary<Guid, string> GuidMappings { get; }

    /// <summary>
    /// Per-channel sequence numbers for message ordering
    /// </summary>
    public Dictionary<ushort, uint> ChannelSequences { get; }

    /// <summary>
    /// Pending messages awaiting responses (Message ID -> Request Info)
    /// </summary>
    public Dictionary<ulong, PendingMessageInfo> PendingMessages { get; }

    /// <summary>
    /// Connection flags and capabilities
    /// </summary>
    public ConnectionFlags Flags { get; set; }

    #region Reconnection Support

    /// <summary>
    /// Token for reconnecting to this session (generated on disconnect, valid for reconnection window)
    /// </summary>
    public string? ReconnectionToken { get; set; }

    /// <summary>
    /// When the reconnection window expires (5 minutes after disconnect)
    /// </summary>
    public DateTimeOffset? ReconnectionExpiresAt { get; set; }

    /// <summary>
    /// When the connection was disconnected (null if still connected)
    /// </summary>
    public DateTimeOffset? DisconnectedAt { get; set; }

    /// <summary>
    /// User roles at time of disconnect (preserved for reconnection)
    /// </summary>
    public ICollection<string>? UserRoles { get; set; }

    /// <summary>
    /// Whether this session is in reconnection window (disconnected but not expired)
    /// </summary>
    public bool IsInReconnectionWindow =>
        DisconnectedAt.HasValue &&
        ReconnectionExpiresAt.HasValue &&
        DateTimeOffset.UtcNow < ReconnectionExpiresAt.Value;

    #endregion

    /// <summary>
    /// Creates a new connection state.
    /// </summary>
    public ConnectionState(string sessionId)
    {
        SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
        ConnectedAt = DateTimeOffset.UtcNow;
        LastActivity = ConnectedAt;
        ServiceMappings = new Dictionary<string, Guid>();
        GuidMappings = new Dictionary<Guid, string>();
        ChannelSequences = new Dictionary<ushort, uint>();
        PendingMessages = new Dictionary<ulong, PendingMessageInfo>();
        Flags = ConnectionFlags.None;
    }

    /// <summary>
    /// Adds a service mapping for this connection (thread-safe).
    /// </summary>
    public void AddServiceMapping(string serviceName, Guid serviceGuid)
    {
        lock (_mappingsLock)
        {
            ServiceMappings[serviceName] = serviceGuid;
            GuidMappings[serviceGuid] = serviceName;
        }
    }

    /// <summary>
    /// Clears all service mappings (used before rebuilding capabilities, thread-safe).
    /// </summary>
    public void ClearServiceMappings()
    {
        lock (_mappingsLock)
        {
            ServiceMappings.Clear();
            GuidMappings.Clear();
        }
    }

    /// <summary>
    /// Atomically updates all service mappings at once (thread-safe).
    /// This prevents race conditions during capability updates.
    /// </summary>
    public void UpdateAllServiceMappings(Dictionary<string, Guid> newMappings)
    {
        lock (_mappingsLock)
        {
            ServiceMappings.Clear();
            GuidMappings.Clear();

            foreach (var mapping in newMappings)
            {
                ServiceMappings[mapping.Key] = mapping.Value;
                GuidMappings[mapping.Value] = mapping.Key;
            }
        }
    }

    /// <summary>
    /// Tries to get a service name from a GUID (thread-safe).
    /// </summary>
    public bool TryGetServiceName(Guid guid, out string? serviceName)
    {
        lock (_mappingsLock)
        {
            return GuidMappings.TryGetValue(guid, out serviceName);
        }
    }

    /// <summary>
    /// Gets the next sequence number for a channel.
    /// </summary>
    public uint GetNextSequenceNumber(ushort channel)
    {
        if (!ChannelSequences.TryGetValue(channel, out var current))
        {
            current = 0;
        }

        current++;
        ChannelSequences[channel] = current;
        return current;
    }

    /// <summary>
    /// Adds a pending message awaiting response.
    /// </summary>
    public void AddPendingMessage(ulong messageId, string serviceName, DateTimeOffset sentAt)
    {
        PendingMessages[messageId] = new PendingMessageInfo
        {
            ServiceName = serviceName,
            SentAt = sentAt,
            TimeoutAt = sentAt.AddSeconds(30) // Default 30 second timeout
        };
    }

    /// <summary>
    /// Removes a pending message (when response received).
    /// </summary>
    public PendingMessageInfo? RemovePendingMessage(ulong messageId)
    {
        if (PendingMessages.TryGetValue(messageId, out var info))
        {
            PendingMessages.Remove(messageId);
            return info;
        }
        return null;
    }

    /// <summary>
    /// Gets expired pending messages for cleanup.
    /// </summary>
    public List<ulong> GetExpiredMessages()
    {
        var now = DateTimeOffset.UtcNow;
        var expired = new List<ulong>();

        foreach (var kvp in PendingMessages)
        {
            if (kvp.Value.TimeoutAt <= now)
            {
                expired.Add(kvp.Key);
            }
        }

        return expired;
    }

    /// <summary>
    /// Updates last activity timestamp.
    /// </summary>
    public void UpdateActivity()
    {
        LastActivity = DateTimeOffset.UtcNow;
    }

    #region Reconnection Lifecycle Methods

    /// <summary>
    /// Initiates reconnection window for this connection.
    /// Called when WebSocket disconnects to allow reconnection within the window.
    /// </summary>
    /// <param name="reconnectionWindowMinutes">Duration of reconnection window in minutes (default: 5)</param>
    /// <param name="userRoles">User roles to preserve for reconnection</param>
    /// <returns>Generated reconnection token</returns>
    public string InitiateReconnectionWindow(int reconnectionWindowMinutes = 5, ICollection<string>? userRoles = null)
    {
        DisconnectedAt = DateTimeOffset.UtcNow;
        ReconnectionExpiresAt = DisconnectedAt.Value.AddMinutes(reconnectionWindowMinutes);
        UserRoles = userRoles;

        // Generate secure reconnection token
        ReconnectionToken = GenerateSecureToken();
        return ReconnectionToken;
    }

    /// <summary>
    /// Clears reconnection state when client successfully reconnects.
    /// </summary>
    public void ClearReconnectionState()
    {
        DisconnectedAt = null;
        ReconnectionExpiresAt = null;
        ReconnectionToken = null;
        // UserRoles are preserved for the reconnected session
        LastActivity = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Generates a cryptographically secure random token for reconnection.
    /// </summary>
    private static string GenerateSecureToken()
    {
        var tokenBytes = new byte[32];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(tokenBytes);
        return Convert.ToBase64String(tokenBytes);
    }

    #endregion
}

/// <summary>
/// Information about a pending message awaiting response.
/// </summary>
public class PendingMessageInfo
{
    /// <summary>
    /// Name of the service the message was sent to.
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// When the message was sent.
    /// </summary>
    public DateTimeOffset SentAt { get; set; }

    /// <summary>
    /// When the message will be considered timed out.
    /// </summary>
    public DateTimeOffset TimeoutAt { get; set; }
}

/// <summary>
/// Connection capability flags.
/// </summary>
[Flags]
public enum ConnectionFlags : byte
{
    /// <summary>No special capabilities.</summary>
    None = 0x00,

    /// <summary>Connection has been authenticated with a valid JWT.</summary>
    Authenticated = 0x01,

    /// <summary>Client-to-client messaging is enabled.</summary>
    ClientToClientEnabled = 0x02,

    /// <summary>High-priority message routing is enabled.</summary>
    HighPriorityAccess = 0x04,

    /// <summary>Message compression is enabled.</summary>
    CompressionEnabled = 0x08,

    /// <summary>End-to-end encryption is enabled.</summary>
    EncryptionEnabled = 0x10
}
