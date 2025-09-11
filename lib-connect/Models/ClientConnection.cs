using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Claims;

namespace BeyondImmersion.BannouService.Connect.Models;

/// <summary>
/// Represents a connected WebSocket client with routing and authentication information.
/// </summary>
public class ClientConnection : IDisposable
{
    private readonly WebSocket _webSocket;
    private readonly SemaphoreSlim _sendSemaphore;
    private volatile bool _disposed;

    public ClientConnection(string clientId, WebSocket webSocket, ClaimsPrincipal? principal = null)
    {
        ClientId = clientId ?? throw new ArgumentNullException(nameof(clientId));
        _webSocket = webSocket ?? throw new ArgumentNullException(nameof(webSocket));
        Principal = principal;
        ConnectedAt = DateTime.UtcNow;
        LastActivity = DateTime.UtcNow;
        ServiceMappings = new ConcurrentDictionary<string, Guid>();
        _sendSemaphore = new SemaphoreSlim(1, 1);
    }

    /// <summary>
    /// Unique client identifier.
    /// </summary>
    public string ClientId { get; }

    /// <summary>
    /// WebSocket connection state.
    /// </summary>
    public WebSocketState State => _webSocket.State;

    /// <summary>
    /// Authenticated user principal (null if unauthenticated).
    /// </summary>
    public ClaimsPrincipal? Principal { get; private set; }

    /// <summary>
    /// When the client connected.
    /// </summary>
    public DateTime ConnectedAt { get; }

    /// <summary>
    /// Last activity timestamp.
    /// </summary>
    public DateTime LastActivity { get; private set; }

    /// <summary>
    /// Service method to GUID mappings for this client.
    /// Each client gets unique GUIDs for security.
    /// </summary>
    public ConcurrentDictionary<string, Guid> ServiceMappings { get; }

    /// <summary>
    /// Rate limiting: message count in current window.
    /// </summary>
    public int MessageCount { get; private set; }

    /// <summary>
    /// Rate limiting: current window start time.
    /// </summary>
    public DateTime RateLimitWindowStart { get; private set; } = DateTime.UtcNow;

    /// <summary>
    /// Updates the authenticated principal for this connection.
    /// </summary>
    public void UpdatePrincipal(ClaimsPrincipal principal)
    {
        Principal = principal;
        LastActivity = DateTime.UtcNow;
    }

    /// <summary>
    /// Updates service mappings based on authentication state.
    /// </summary>
    public void UpdateServiceMappings(IEnumerable<string> availableServices)
    {
        ServiceMappings.Clear();
        
        foreach (var service in availableServices)
        {
            // Generate unique GUID for this client/service combination
            var salt = $"{ClientId}:{service}:{ConnectedAt:O}";
            var guidBytes = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(salt));
            var serviceGuid = new Guid(guidBytes);
            ServiceMappings[service] = serviceGuid;
        }

        LastActivity = DateTime.UtcNow;
    }

    /// <summary>
    /// Gets the service name for a given GUID (reverse lookup).
    /// </summary>
    public string? GetServiceForGuid(Guid serviceGuid)
    {
        return ServiceMappings.FirstOrDefault(kvp => kvp.Value == serviceGuid).Key;
    }

    /// <summary>
    /// Checks if rate limit is exceeded.
    /// </summary>
    public bool IsRateLimited(int maxMessagesPerMinute, int windowMinutes = 1)
    {
        var windowDuration = TimeSpan.FromMinutes(windowMinutes);
        var now = DateTime.UtcNow;

        // Reset window if expired
        if (now - RateLimitWindowStart > windowDuration)
        {
            MessageCount = 0;
            RateLimitWindowStart = now;
        }

        return MessageCount >= maxMessagesPerMinute;
    }

    /// <summary>
    /// Increments the message count for rate limiting.
    /// </summary>
    public void IncrementMessageCount()
    {
        MessageCount++;
        LastActivity = DateTime.UtcNow;
    }

    /// <summary>
    /// Sends a message to this client asynchronously.
    /// </summary>
    public async Task<bool> SendAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken = default)
    {
        if (_disposed || State != WebSocketState.Open)
            return false;

        try
        {
            await _sendSemaphore.WaitAsync(cancellationToken);
            try
            {
                if (State == WebSocketState.Open)
                {
                    await _webSocket.SendAsync(message, WebSocketMessageType.Binary, true, cancellationToken);
                    LastActivity = DateTime.UtcNow;
                    return true;
                }
            }
            finally
            {
                _sendSemaphore.Release();
            }
        }
        catch (WebSocketException)
        {
            // Connection is broken
        }
        catch (OperationCanceledException)
        {
            // Operation was cancelled
        }

        return false;
    }

    /// <summary>
    /// Receives a message from this client asynchronously.
    /// </summary>
    public async Task<WebSocketReceiveResult?> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_disposed || State != WebSocketState.Open)
            return null;

        try
        {
            var result = await _webSocket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType != WebSocketMessageType.Close)
            {
                LastActivity = DateTime.UtcNow;
            }
            return result;
        }
        catch (WebSocketException)
        {
            // Connection is broken
            return null;
        }
        catch (OperationCanceledException)
        {
            // Operation was cancelled
            return null;
        }
    }

    /// <summary>
    /// Closes the WebSocket connection gracefully.
    /// </summary>
    public async Task CloseAsync(WebSocketCloseStatus closeStatus = WebSocketCloseStatus.NormalClosure, 
        string? statusDescription = null, CancellationToken cancellationToken = default)
    {
        if (!_disposed && State == WebSocketState.Open)
        {
            try
            {
                await _webSocket.CloseAsync(closeStatus, statusDescription, cancellationToken);
            }
            catch
            {
                // Ignore errors during close
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        
        try
        {
            if (State == WebSocketState.Open)
            {
                _webSocket.CloseAsync(WebSocketCloseStatus.Going Away, "Server shutdown", CancellationToken.None)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
            }
        }
        catch
        {
            // Ignore errors during disposal
        }

        _webSocket.Dispose();
        _sendSemaphore.Dispose();
    }
}