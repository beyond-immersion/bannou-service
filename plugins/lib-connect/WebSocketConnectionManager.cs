using BeyondImmersion.BannouService.Connect.Protocol;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace BeyondImmersion.BannouService.Connect;

/// <summary>
/// Manages WebSocket connections and their associated state.
/// Handles connection lifecycle, message queuing, and cleanup.
/// </summary>
public class WebSocketConnectionManager
{
    private readonly ConcurrentDictionary<string, WebSocketConnection> _connections;
    private readonly ConcurrentDictionary<Guid, string> _peerGuidToSessionId;
    private readonly Timer _cleanupTimer;
    private readonly object _lockObject = new();
    private readonly int _connectionShutdownTimeoutSeconds;
    private readonly int _inactiveConnectionTimeoutMinutes;
    private readonly ILogger? _logger;

    public WebSocketConnectionManager(
        int connectionShutdownTimeoutSeconds = 5,
        int connectionCleanupIntervalSeconds = 30,
        int inactiveConnectionTimeoutMinutes = 30,
        ILogger? logger = null)
    {
        _connections = new ConcurrentDictionary<string, WebSocketConnection>();
        _peerGuidToSessionId = new ConcurrentDictionary<Guid, string>();
        _connectionShutdownTimeoutSeconds = connectionShutdownTimeoutSeconds;
        _inactiveConnectionTimeoutMinutes = inactiveConnectionTimeoutMinutes;
        _logger = logger;

        // Start cleanup timer
        _cleanupTimer = new Timer(CleanupExpiredConnections, null,
            TimeSpan.FromSeconds(connectionCleanupIntervalSeconds),
            TimeSpan.FromSeconds(connectionCleanupIntervalSeconds));
    }

    /// <summary>
    /// Adds a new WebSocket connection and registers its peer GUID for peer-to-peer routing.
    /// </summary>
    public void AddConnection(string sessionId, WebSocket webSocket, ConnectionState connectionState)
    {
        var connection = new WebSocketConnection(sessionId, webSocket, connectionState);
        _connections.AddOrUpdate(sessionId, connection, (key, existingConnection) =>
        {
            // Unregister old connection's peer GUID before replacing
            _peerGuidToSessionId.TryRemove(existingConnection.ConnectionState.PeerGuid, out _);

            // Close existing connection if present
            _ = Task.Run(async () =>
            {
                try
                {
                    if (existingConnection.WebSocket.State == WebSocketState.Open)
                    {
                        await existingConnection.WebSocket.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "New connection established",
                            CancellationToken.None);
                    }
                }
                catch (Exception ex)
                {
                    // Log cleanup errors at debug level - expected during connection replacement
                    _logger?.LogDebug(ex, "Error closing existing WebSocket connection during replacement");
                }
            });

            return connection;
        });

        // Register the peer GUID for this connection
        _peerGuidToSessionId[connectionState.PeerGuid] = sessionId;
    }

    /// <summary>
    /// Gets a WebSocket connection by session ID.
    /// </summary>
    public virtual WebSocketConnection? GetConnection(string sessionId)
    {
        return _connections.TryGetValue(sessionId, out var connection) ? connection : null;
    }

    /// <summary>
    /// Removes a WebSocket connection and unregisters its peer GUID.
    /// </summary>
    public virtual bool RemoveConnection(string sessionId)
    {
        if (_connections.TryRemove(sessionId, out var connection))
        {
            // Unregister the peer GUID
            _peerGuidToSessionId.TryRemove(connection.ConnectionState.PeerGuid, out _);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Removes a WebSocket connection only if the WebSocket instance matches.
    /// This prevents race conditions during session subsume where the old connection's
    /// cleanup could accidentally remove a new connection that replaced it.
    /// </summary>
    /// <param name="sessionId">The session ID to remove.</param>
    /// <param name="expectedWebSocket">The WebSocket instance that must match for removal.</param>
    /// <returns>True if the connection was removed, false if not found or instance didn't match.</returns>
    public virtual bool RemoveConnectionIfMatch(string sessionId, WebSocket expectedWebSocket)
    {
        if (_connections.TryGetValue(sessionId, out var connection))
        {
            // Only remove if it's the SAME WebSocket instance (reference equality)
            // This ensures that during subsume, the old connection's cleanup doesn't
            // accidentally remove the new connection that replaced it
            if (ReferenceEquals(connection.WebSocket, expectedWebSocket))
            {
                if (_connections.TryRemove(new KeyValuePair<string, WebSocketConnection>(sessionId, connection)))
                {
                    // Unregister the peer GUID
                    _peerGuidToSessionId.TryRemove(connection.ConnectionState.PeerGuid, out _);
                    return true;
                }
            }
        }
        return false;
    }

    #region Peer-to-Peer Routing

    /// <summary>
    /// Gets a WebSocket connection by its peer GUID.
    /// </summary>
    /// <param name="peerGuid">The peer GUID to look up.</param>
    /// <returns>The connection if found, null otherwise.</returns>
    public virtual WebSocketConnection? GetConnectionByPeerGuid(Guid peerGuid)
    {
        if (_peerGuidToSessionId.TryGetValue(peerGuid, out var sessionId))
        {
            return GetConnection(sessionId);
        }
        return null;
    }

    /// <summary>
    /// Tries to get the session ID for a peer GUID.
    /// </summary>
    /// <param name="peerGuid">The peer GUID to look up.</param>
    /// <param name="sessionId">The session ID if found.</param>
    /// <returns>True if the peer GUID was found.</returns>
    public virtual bool TryGetSessionIdByPeerGuid(Guid peerGuid, out string? sessionId)
    {
        if (_peerGuidToSessionId.TryGetValue(peerGuid, out var id))
        {
            sessionId = id;
            return true;
        }
        sessionId = null;
        return false;
    }

    /// <summary>
    /// Sends a message to a peer by their peer GUID.
    /// </summary>
    /// <param name="peerGuid">The peer GUID to send to.</param>
    /// <param name="message">The message to send.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the message was sent successfully.</returns>
    public virtual async Task<bool> SendMessageToPeerAsync(Guid peerGuid, BinaryMessage message, CancellationToken cancellationToken = default)
    {
        if (!_peerGuidToSessionId.TryGetValue(peerGuid, out var sessionId))
        {
            return false;
        }
        return await SendMessageAsync(sessionId, message, cancellationToken);
    }

    /// <summary>
    /// Gets the count of registered peer GUIDs.
    /// </summary>
    public int PeerCount => _peerGuidToSessionId.Count;

    #endregion

    /// <summary>
    /// Gets all active connection session IDs.
    /// </summary>
    public IEnumerable<string> GetActiveSessionIds()
    {
        return _connections.Keys.ToList();
    }

    /// <summary>
    /// Gets total connection count.
    /// </summary>
    public int ConnectionCount => _connections.Count;

    /// <summary>
    /// Sends a message to a specific session.
    /// </summary>
    public virtual async Task<bool> SendMessageAsync(string sessionId, BinaryMessage message, CancellationToken cancellationToken = default)
    {
        var connection = GetConnection(sessionId);
        if (connection?.WebSocket.State != WebSocketState.Open)
        {
            return false;
        }

        try
        {
            var messageBytes = message.ToByteArray();
            await connection.WebSocket.SendAsync(
                new ArraySegment<byte>(messageBytes),
                WebSocketMessageType.Binary,
                true,
                cancellationToken);

            connection.ConnectionState.UpdateActivity();
            return true;
        }
        catch (Exception ex)
        {
            // Log send failure and remove the broken connection
            _logger?.LogDebug(ex, "WebSocket send failed for session {SessionId}, removing connection", sessionId);
            RemoveConnection(sessionId);
            return false;
        }
    }

    /// <summary>
    /// Broadcasts a message to all connected sessions.
    /// </summary>
    public async Task BroadcastMessageAsync(BinaryMessage message, CancellationToken cancellationToken = default)
    {
        var tasks = new List<Task<bool>>();
        foreach (var sessionId in GetActiveSessionIds())
        {
            tasks.Add(SendMessageAsync(sessionId, message, cancellationToken));
        }

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Sends a message to all connected admin sessions.
    /// Filters connections by checking for "admin" role in ConnectionState.UserRoles.
    /// </summary>
    public async Task<int> SendToAdminsAsync(BinaryMessage message, CancellationToken cancellationToken = default)
    {
        var adminConnections = _connections.Values
            .Where(c => c.WebSocket.State == WebSocketState.Open &&
                        c.ConnectionState.UserRoles?.Any(r =>
                            r.Equals("admin", StringComparison.OrdinalIgnoreCase)) == true)
            .ToList();

        if (adminConnections.Count == 0)
        {
            return 0;
        }

        var tasks = adminConnections.Select(c => SendMessageAsync(c.SessionId, message, cancellationToken));
        var results = await Task.WhenAll(tasks);
        return results.Count(r => r);
    }

    /// <summary>
    /// Gets the count of connected admin sessions.
    /// </summary>
    public int GetAdminConnectionCount()
    {
        return _connections.Values.Count(c =>
            c.WebSocket.State == WebSocketState.Open &&
            c.ConnectionState.UserRoles?.Any(r =>
                r.Equals("admin", StringComparison.OrdinalIgnoreCase)) == true);
    }

    /// <summary>
    /// Cleanup expired connections and pending messages.
    /// </summary>
    private void CleanupExpiredConnections(object? state)
    {
        var expiredSessions = new List<string>();
        var now = DateTimeOffset.UtcNow;

        foreach (var kvp in _connections.ToList())
        {
            var sessionId = kvp.Key;
            var connection = kvp.Value;

            // Check if connection is closed or inactive
            if (connection.WebSocket.State != WebSocketState.Open ||
                (now - connection.ConnectionState.LastActivity).TotalMinutes > _inactiveConnectionTimeoutMinutes)
            {
                expiredSessions.Add(sessionId);
            }
            else
            {
                // Clean up expired pending messages
                var expiredMessageIds = connection.ConnectionState.GetExpiredMessages();
                foreach (var messageId in expiredMessageIds)
                {
                    connection.ConnectionState.RemovePendingMessage(messageId);
                }
            }
        }

        // Remove expired connections
        foreach (var sessionId in expiredSessions)
        {
            RemoveConnection(sessionId);
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();

        // Close all connections
        Parallel.ForEach(_connections.Values, connection =>
        {
            try
            {
                if (connection.WebSocket.State == WebSocketState.Open)
                {
                    connection.WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure,
                        "Server shutdown", CancellationToken.None).Wait(TimeSpan.FromSeconds(_connectionShutdownTimeoutSeconds));
                }
            }
            catch (Exception ex)
            {
                // Log cleanup errors during shutdown - expected during connection teardown
                _logger?.LogDebug(ex, "Error closing WebSocket connection during shutdown");
            }
        });

        _connections.Clear();
        _peerGuidToSessionId.Clear();
    }
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
