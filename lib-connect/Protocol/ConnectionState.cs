using System;
using System.Collections.Generic;

namespace BeyondImmersion.BannouService.Connect.Protocol;

/// <summary>
/// Represents the state of a WebSocket connection.
/// Dependency-free class for Client SDK extraction.
/// </summary>
public class ConnectionState
{
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
    /// Adds a service mapping for this connection.
    /// </summary>
    public void AddServiceMapping(string serviceName, Guid serviceGuid)
    {
        ServiceMappings[serviceName] = serviceGuid;
        GuidMappings[serviceGuid] = serviceName;
    }

    /// <summary>
    /// Clears all service mappings (used before rebuilding capabilities).
    /// </summary>
    public void ClearServiceMappings()
    {
        ServiceMappings.Clear();
        GuidMappings.Clear();
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
