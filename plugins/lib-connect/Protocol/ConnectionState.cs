using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

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

    #region Peer-to-Peer Routing

    /// <summary>
    /// Unique GUID for peer-to-peer routing.
    /// Other connections can route messages to this connection using this GUID.
    /// Generated on connection establishment and stable for the connection lifetime.
    /// </summary>
    public Guid PeerGuid { get; }

    #endregion

    #region Session Shortcuts

    /// <summary>
    /// Session shortcuts indexed by route GUID.
    /// Uses ConcurrentDictionary for thread-safe access without explicit locks.
    /// Shortcuts are session-scoped and not persisted to Redis.
    /// </summary>
    public ConcurrentDictionary<Guid, SessionShortcutData> SessionShortcuts { get; }

    /// <summary>
    /// Index for bulk revocation by source service.
    /// Maps source service name to set of route GUIDs from that service.
    /// </summary>
    public ConcurrentDictionary<string, HashSet<Guid>> ShortcutsByService { get; }

    #endregion

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
        SessionId = sessionId;
        ConnectedAt = DateTimeOffset.UtcNow;
        LastActivity = ConnectedAt;
        ServiceMappings = new Dictionary<string, Guid>();
        GuidMappings = new Dictionary<Guid, string>();
        ChannelSequences = new Dictionary<ushort, uint>();
        PendingMessages = new Dictionary<ulong, PendingMessageInfo>();
        SessionShortcuts = new ConcurrentDictionary<Guid, SessionShortcutData>();
        ShortcutsByService = new ConcurrentDictionary<string, HashSet<Guid>>();
        Flags = ConnectionFlags.None;
        PeerGuid = Guid.NewGuid();
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

    #region Session Shortcut Management

    /// <summary>
    /// Adds or updates a session shortcut.
    /// Thread-safe via ConcurrentDictionary.
    /// </summary>
    /// <param name="shortcut">Shortcut data to add or update</param>
    public void AddOrUpdateShortcut(SessionShortcutData shortcut)
    {
        if (shortcut == null)
            throw new ArgumentNullException(nameof(shortcut));

        // Add to main shortcuts dictionary
        SessionShortcuts[shortcut.RouteGuid] = shortcut;

        // Add to service index for bulk revocation (only if SourceService is specified)
        if (!string.IsNullOrEmpty(shortcut.SourceService))
        {
            var guids = ShortcutsByService.GetOrAdd(shortcut.SourceService, _ => new HashSet<Guid>());
            lock (guids)
            {
                guids.Add(shortcut.RouteGuid);
            }
        }
    }

    /// <summary>
    /// Removes a specific shortcut by route GUID.
    /// </summary>
    /// <param name="routeGuid">Route GUID of the shortcut to remove</param>
    /// <returns>True if the shortcut was found and removed</returns>
    public bool RemoveShortcut(Guid routeGuid)
    {
        if (!SessionShortcuts.TryRemove(routeGuid, out var shortcut))
            return false;

        // Remove from service index (only if SourceService was specified)
        if (!string.IsNullOrEmpty(shortcut.SourceService) &&
            ShortcutsByService.TryGetValue(shortcut.SourceService, out var guids))
        {
            lock (guids)
            {
                guids.Remove(routeGuid);
            }

            // Clean up empty service entries
            if (guids.Count == 0)
            {
                ShortcutsByService.TryRemove(shortcut.SourceService, out _);
            }
        }

        return true;
    }

    /// <summary>
    /// Removes all shortcuts from a specific service.
    /// </summary>
    /// <param name="sourceService">Source service to revoke shortcuts from</param>
    /// <returns>Number of shortcuts removed</returns>
    public int RemoveShortcutsByService(string sourceService)
    {
        if (string.IsNullOrEmpty(sourceService))
            return 0;

        if (!ShortcutsByService.TryRemove(sourceService, out var guids))
            return 0;

        var count = 0;
        lock (guids)
        {
            foreach (var guid in guids)
            {
                if (SessionShortcuts.TryRemove(guid, out _))
                    count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Tries to get a shortcut by route GUID.
    /// </summary>
    /// <param name="routeGuid">Route GUID to look up</param>
    /// <param name="shortcut">The shortcut data if found</param>
    /// <returns>True if the shortcut was found</returns>
    public bool TryGetShortcut(Guid routeGuid, out SessionShortcutData? shortcut)
    {
        return SessionShortcuts.TryGetValue(routeGuid, out shortcut);
    }

    /// <summary>
    /// Clears all shortcuts. Called on disconnect.
    /// Shortcuts are not persisted and must be re-published on reconnection.
    /// </summary>
    public void ClearAllShortcuts()
    {
        SessionShortcuts.Clear();
        ShortcutsByService.Clear();
    }

    /// <summary>
    /// Gets all shortcuts as a list (snapshot for capability manifest).
    /// </summary>
    /// <returns>List of all current shortcuts</returns>
    public List<SessionShortcutData> GetAllShortcuts()
    {
        return SessionShortcuts.Values.ToList();
    }

    /// <summary>
    /// Gets the count of active shortcuts.
    /// </summary>
    public int ShortcutCount => SessionShortcuts.Count;

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
/// Information about a pending RPC call awaiting client response.
/// Used to track service-to-client RPC calls and forward responses back.
/// </summary>
public class PendingRPCInfo
{
    /// <summary>
    /// Session ID of the client that received the RPC.
    /// </summary>
    public string ClientSessionId { get; set; } = string.Empty;

    /// <summary>
    /// Name of the service that initiated the RPC.
    /// </summary>
    public string ServiceName { get; set; } = string.Empty;

    /// <summary>
    /// RabbitMQ channel where the response should be published.
    /// </summary>
    public string ResponseChannel { get; set; } = string.Empty;

    /// <summary>
    /// Service GUID used for the RPC (for correlation).
    /// </summary>
    public Guid ServiceGuid { get; set; }

    /// <summary>
    /// When the RPC was sent.
    /// </summary>
    public DateTimeOffset SentAt { get; set; }

    /// <summary>
    /// When the RPC will be considered timed out.
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

/// <summary>
/// Internal representation of a session shortcut.
/// Contains the pre-bound payload and metadata for a shortcut capability.
/// </summary>
public class SessionShortcutData
{
    /// <summary>
    /// Client-salted GUID for invoking this shortcut.
    /// Generated using GuidGenerator.GenerateSessionShortcutGuid() with version 7 bits.
    /// </summary>
    public Guid RouteGuid { get; set; }

    /// <summary>
    /// The actual service capability GUID this shortcut invokes.
    /// Must be a valid capability in the client's current capability manifest.
    /// </summary>
    public Guid TargetGuid { get; set; }

    /// <summary>
    /// Pre-serialized JSON payload passed unchanged to the target service.
    /// Connect treats this as opaque bytes - no deserialization or modification.
    /// </summary>
    public byte[] BoundPayload { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// The service that created this shortcut.
    /// Used for bulk revocation and audit logging. Null if not specified.
    /// </summary>
    public string? SourceService { get; set; }

    /// <summary>
    /// Machine-readable shortcut identifier. Null if not specified.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Human-readable description of what this shortcut does.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The service this shortcut invokes (required for routing).
    /// </summary>
    public string? TargetService { get; set; }

    /// <summary>
    /// The HTTP method for this shortcut (required for routing, e.g., "POST").
    /// </summary>
    public string? TargetMethod { get; set; }

    /// <summary>
    /// The endpoint path this shortcut invokes (required for routing, e.g., "/sessions/join").
    /// </summary>
    public string? TargetEndpoint { get; set; }

    /// <summary>
    /// User-friendly name for display in client UIs.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Categorization tags for client-side organization.
    /// </summary>
    public string[]? Tags { get; set; }

    /// <summary>
    /// When this shortcut was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Optional TTL for this shortcut.
    /// If set, the shortcut is automatically removed after this time.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>
    /// Checks if this shortcut has expired.
    /// </summary>
    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value < DateTimeOffset.UtcNow;
}
