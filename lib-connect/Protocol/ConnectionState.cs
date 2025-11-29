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
    public string ServiceName { get; set; } = string.Empty;
    public DateTimeOffset SentAt { get; set; }
    public DateTimeOffset TimeoutAt { get; set; }
}

/// <summary>
/// Connection capability flags.
/// </summary>
[Flags]
public enum ConnectionFlags : byte
{
    None = 0x00,
    Authenticated = 0x01,
    ClientToClientEnabled = 0x02,
    HighPriorityAccess = 0x04,
    CompressionEnabled = 0x08,
    EncryptionEnabled = 0x10
}
