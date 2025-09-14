using BeyondImmersion.BannouService.Connect.Protocol;
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
    private readonly Timer _cleanupTimer;
    private readonly object _lockObject = new();

    public WebSocketConnectionManager()
    {
        _connections = new ConcurrentDictionary<string, WebSocketConnection>();

        // Start cleanup timer (runs every 30 seconds)
        _cleanupTimer = new Timer(CleanupExpiredConnections, null,
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// Adds a new WebSocket connection.
    /// </summary>
    public void AddConnection(string sessionId, WebSocket webSocket, ConnectionState connectionState)
    {
        var connection = new WebSocketConnection(sessionId, webSocket, connectionState);
        _connections.AddOrUpdate(sessionId, connection, (key, existingConnection) =>
        {
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
                catch
                {
                    // Ignore cleanup errors
                }
            });

            return connection;
        });
    }

    /// <summary>
    /// Gets a WebSocket connection by session ID.
    /// </summary>
    public WebSocketConnection? GetConnection(string sessionId)
    {
        return _connections.TryGetValue(sessionId, out var connection) ? connection : null;
    }

    /// <summary>
    /// Removes a WebSocket connection.
    /// </summary>
    public bool RemoveConnection(string sessionId)
    {
        return _connections.TryRemove(sessionId, out _);
    }

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
    public async Task<bool> SendMessageAsync(string sessionId, BinaryMessage message, CancellationToken cancellationToken = default)
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
        catch
        {
            // Remove failed connection
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
                (now - connection.ConnectionState.LastActivity).TotalMinutes > 30)
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
                        "Server shutdown", CancellationToken.None).Wait(TimeSpan.FromSeconds(5));
                }
            }
            catch
            {
                // Ignore cleanup errors
            }
        });

        _connections.Clear();
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

    public WebSocketConnection(string sessionId, WebSocket webSocket, ConnectionState connectionState)
    {
        SessionId = sessionId;
        WebSocket = webSocket;
        ConnectionState = connectionState;
        CreatedAt = DateTimeOffset.UtcNow;
    }
}
