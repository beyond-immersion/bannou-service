using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BeyondImmersion.BannouService.Connect.Protocol;

namespace BeyondImmersion.Bannou.Client.SDK;

/// <summary>
/// High-level client for connecting to Bannou services via WebSocket.
/// Handles authentication, connection lifecycle, and message correlation.
/// </summary>
public class BannouClient : IAsyncDisposable
{
    private ClientWebSocket? _webSocket;
    private ConnectionState? _connectionState;
    private readonly ConcurrentDictionary<ulong, TaskCompletionSource<BinaryMessage>> _pendingRequests = new();
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private CancellationTokenSource? _receiveLoopCts;
    private Task? _receiveLoopTask;

    private string? _accessToken;
    private string? _refreshToken;
    private string? _serverBaseUrl;

    /// <summary>
    /// Creates a new BannouClient with a default HttpClient.
    /// </summary>
    public BannouClient()
    {
        _httpClient = new HttpClient();
        _ownsHttpClient = true;
    }

    /// <summary>
    /// Creates a new BannouClient with a provided HttpClient.
    /// </summary>
    /// <param name="httpClient">HTTP client to use for authentication requests.</param>
    public BannouClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _ownsHttpClient = false;
    }

    /// <summary>
    /// Current connection state.
    /// </summary>
    public bool IsConnected => _webSocket?.State == WebSocketState.Open;

    /// <summary>
    /// Available services with their client-salted GUIDs.
    /// Populated after successful connection.
    /// </summary>
    public IReadOnlyDictionary<string, Guid> AvailableServices =>
        _connectionState?.ServiceMappings ?? new Dictionary<string, Guid>();

    /// <summary>
    /// Current access token (JWT).
    /// </summary>
    public string? AccessToken => _accessToken;

    /// <summary>
    /// Current refresh token for re-authentication.
    /// </summary>
    public string? RefreshToken => _refreshToken;

    /// <summary>
    /// Connects to a Bannou server using username/password authentication.
    /// </summary>
    /// <param name="serverUrl">Base URL (e.g., "http://localhost:8080" or "https://game.example.com")</param>
    /// <param name="email">Account email</param>
    /// <param name="password">Account password</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if connection successful</returns>
    public async Task<bool> ConnectAsync(
        string serverUrl,
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        _serverBaseUrl = serverUrl.TrimEnd('/');

        // Step 1: Authenticate via HTTP to get JWT
        var loginResult = await LoginAsync(email, password, cancellationToken);
        if (!loginResult)
        {
            return false;
        }

        // Step 2: Establish WebSocket connection with JWT
        return await EstablishWebSocketAsync(cancellationToken);
    }

    /// <summary>
    /// Connects using an existing JWT token.
    /// </summary>
    /// <param name="serverUrl">Base URL (e.g., "http://localhost:8080")</param>
    /// <param name="accessToken">Valid JWT access token</param>
    /// <param name="refreshToken">Optional refresh token for re-authentication</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if connection successful</returns>
    public async Task<bool> ConnectWithTokenAsync(
        string serverUrl,
        string accessToken,
        string? refreshToken = null,
        CancellationToken cancellationToken = default)
    {
        _serverBaseUrl = serverUrl.TrimEnd('/');
        _accessToken = accessToken;
        _refreshToken = refreshToken;

        return await EstablishWebSocketAsync(cancellationToken);
    }

    /// <summary>
    /// Registers a new account and connects.
    /// </summary>
    /// <param name="serverUrl">Base URL</param>
    /// <param name="username">Desired username</param>
    /// <param name="email">Email address</param>
    /// <param name="password">Password</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if registration and connection successful</returns>
    public async Task<bool> RegisterAndConnectAsync(
        string serverUrl,
        string username,
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        _serverBaseUrl = serverUrl.TrimEnd('/');

        // Step 1: Register account
        var registerResult = await RegisterAsync(username, email, password, cancellationToken);
        if (!registerResult)
        {
            return false;
        }

        // Step 2: Establish WebSocket connection with JWT
        return await EstablishWebSocketAsync(cancellationToken);
    }

    /// <summary>
    /// Invokes a service method and awaits the response.
    /// </summary>
    /// <typeparam name="TRequest">Request model type</typeparam>
    /// <typeparam name="TResponse">Response model type</typeparam>
    /// <param name="serviceName">Service name (e.g., "accounts", "auth")</param>
    /// <param name="request">Request payload</param>
    /// <param name="channel">Message channel for ordering (default 0)</param>
    /// <param name="timeout">Request timeout</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Response from service</returns>
    public async Task<TResponse> InvokeAsync<TRequest, TResponse>(
        string serviceName,
        TRequest request,
        ushort channel = 0,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (_webSocket?.State != WebSocketState.Open)
        {
            throw new InvalidOperationException("WebSocket is not connected. Call ConnectAsync first.");
        }

        if (_connectionState == null)
        {
            throw new InvalidOperationException("Connection state not initialized.");
        }

        // Get service GUID
        if (!_connectionState.ServiceMappings.TryGetValue(serviceName, out var serviceGuid))
        {
            throw new ArgumentException($"Unknown service: {serviceName}. Available: {string.Join(", ", _connectionState.ServiceMappings.Keys)}");
        }

        // Serialize request to JSON
        var jsonPayload = JsonSerializer.Serialize(request);
        var payloadBytes = Encoding.UTF8.GetBytes(jsonPayload);

        // Create binary message
        var messageId = GuidGenerator.GenerateMessageId();
        var sequenceNumber = _connectionState.GetNextSequenceNumber(channel);

        var message = new BinaryMessage(
            flags: MessageFlags.None,
            channel: channel,
            sequenceNumber: sequenceNumber,
            serviceGuid: serviceGuid,
            messageId: messageId,
            payload: payloadBytes);

        // Set up response awaiter
        var tcs = new TaskCompletionSource<BinaryMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[messageId] = tcs;
        _connectionState.AddPendingMessage(messageId, serviceName, DateTimeOffset.UtcNow);

        try
        {
            // Send message
            var messageBytes = message.ToByteArray();
            await _webSocket.SendAsync(
                new ArraySegment<byte>(messageBytes),
                WebSocketMessageType.Binary,
                endOfMessage: true,
                cancellationToken);

            // Wait for response with timeout
            var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(30);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(effectiveTimeout);

            try
            {
                var response = await tcs.Task.WaitAsync(timeoutCts.Token);

                // Deserialize response
                var responseJson = response.GetJsonPayload();
                var result = JsonSerializer.Deserialize<TResponse>(responseJson)
                    ?? throw new InvalidOperationException($"Failed to deserialize response as {typeof(TResponse).Name}");

                return result;
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"Request to {serviceName} timed out after {effectiveTimeout.TotalSeconds} seconds");
            }
        }
        finally
        {
            _pendingRequests.TryRemove(messageId, out _);
            _connectionState.RemovePendingMessage(messageId);
        }
    }

    /// <summary>
    /// Sends a fire-and-forget event (no response expected).
    /// </summary>
    /// <typeparam name="TRequest">Request model type</typeparam>
    /// <param name="serviceName">Service name</param>
    /// <param name="request">Request payload</param>
    /// <param name="channel">Message channel for ordering</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task SendEventAsync<TRequest>(
        string serviceName,
        TRequest request,
        ushort channel = 0,
        CancellationToken cancellationToken = default)
    {
        if (_webSocket?.State != WebSocketState.Open)
        {
            throw new InvalidOperationException("WebSocket is not connected. Call ConnectAsync first.");
        }

        if (_connectionState == null)
        {
            throw new InvalidOperationException("Connection state not initialized.");
        }

        // Get service GUID
        if (!_connectionState.ServiceMappings.TryGetValue(serviceName, out var serviceGuid))
        {
            throw new ArgumentException($"Unknown service: {serviceName}");
        }

        // Serialize request to JSON
        var jsonPayload = JsonSerializer.Serialize(request);
        var payloadBytes = Encoding.UTF8.GetBytes(jsonPayload);

        // Create binary message with Event flag
        var messageId = GuidGenerator.GenerateMessageId();
        var sequenceNumber = _connectionState.GetNextSequenceNumber(channel);

        var message = new BinaryMessage(
            flags: MessageFlags.Event,
            channel: channel,
            sequenceNumber: sequenceNumber,
            serviceGuid: serviceGuid,
            messageId: messageId,
            payload: payloadBytes);

        // Send message
        var messageBytes = message.ToByteArray();
        await _webSocket.SendAsync(
            new ArraySegment<byte>(messageBytes),
            WebSocketMessageType.Binary,
            endOfMessage: true,
            cancellationToken);
    }

    /// <summary>
    /// Disconnects from the server.
    /// </summary>
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        _receiveLoopCts?.Cancel();

        if (_webSocket?.State == WebSocketState.Open)
        {
            try
            {
                await _webSocket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "Client disconnecting",
                    cancellationToken);
            }
            catch
            {
                // Ignore close errors
            }
        }

        if (_receiveLoopTask != null)
        {
            try
            {
                await _receiveLoopTask;
            }
            catch
            {
                // Ignore receive loop errors
            }
        }

        _webSocket?.Dispose();
        _webSocket = null;
        _connectionState = null;
        _accessToken = null;
        _refreshToken = null;
    }

    /// <summary>
    /// Disposes resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();

        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    #region Private Methods

    private async Task<bool> LoginAsync(string email, string password, CancellationToken cancellationToken)
    {
        var loginUrl = $"{_serverBaseUrl}/auth/login";

        var loginRequest = new { email, password };
        var response = await _httpClient.PostAsJsonAsync(loginUrl, loginRequest, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        var result = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken: cancellationToken);
        if (result == null || string.IsNullOrEmpty(result.AccessToken))
        {
            return false;
        }

        _accessToken = result.AccessToken;
        _refreshToken = result.RefreshToken;
        return true;
    }

    private async Task<bool> RegisterAsync(string username, string email, string password, CancellationToken cancellationToken)
    {
        var registerUrl = $"{_serverBaseUrl}/accounts/register";

        var registerRequest = new { username, email, password };
        var response = await _httpClient.PostAsJsonAsync(registerUrl, registerRequest, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return false;
        }

        var result = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken: cancellationToken);
        if (result == null || string.IsNullOrEmpty(result.AccessToken))
        {
            return false;
        }

        _accessToken = result.AccessToken;
        _refreshToken = result.RefreshToken;
        return true;
    }

    private async Task<bool> EstablishWebSocketAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_accessToken))
        {
            return false;
        }

        // Convert HTTP URL to WebSocket URL
        var wsUrl = _serverBaseUrl!
            .Replace("https://", "wss://")
            .Replace("http://", "ws://");
        wsUrl = $"{wsUrl}/ws";

        _webSocket = new ClientWebSocket();
        _webSocket.Options.SetRequestHeader("Authorization", $"Bearer {_accessToken}");

        try
        {
            await _webSocket.ConnectAsync(new Uri(wsUrl), cancellationToken);
        }
        catch
        {
            _webSocket.Dispose();
            _webSocket = null;
            return false;
        }

        // Initialize connection state
        var sessionId = Guid.NewGuid().ToString(); // Client-side session ID
        _connectionState = new ConnectionState(sessionId);

        // Start receive loop
        _receiveLoopCts = new CancellationTokenSource();
        _receiveLoopTask = ReceiveLoopAsync(_receiveLoopCts.Token);

        // TODO: Discover APIs and populate service mappings
        // For now, client needs to know the service GUIDs from capabilities endpoint

        return true;
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];

        try
        {
            while (!cancellationToken.IsCancellationRequested && _webSocket?.State == WebSocketState.Open)
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Binary && result.Count >= BinaryMessage.HeaderSize)
                {
                    try
                    {
                        var message = BinaryMessage.Parse(buffer, result.Count);
                        HandleReceivedMessage(message);
                    }
                    catch
                    {
                        // Ignore malformed messages
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation
        }
        catch (WebSocketException)
        {
            // Connection lost
        }
    }

    private void HandleReceivedMessage(BinaryMessage message)
    {
        if (message.IsResponse)
        {
            // Complete pending request
            if (_pendingRequests.TryRemove(message.MessageId, out var tcs))
            {
                tcs.TrySetResult(message);
            }
        }
        else if (message.Flags.HasFlag(MessageFlags.Event))
        {
            // TODO: Dispatch to event handlers
        }
    }

    #endregion

    #region Internal Models

    private class LoginResponse
    {
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
    }

    #endregion
}
