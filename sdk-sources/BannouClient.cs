using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BeyondImmersion.BannouService.Connect.Protocol;

namespace BeyondImmersion.Bannou.Client.SDK;

/// <summary>
/// High-level client for connecting to Bannou services via WebSocket.
/// Handles authentication, connection lifecycle, capability discovery, and message correlation.
/// </summary>
public class BannouClient : IAsyncDisposable
{
    private ClientWebSocket? _webSocket;
    private ConnectionState? _connectionState;
    private readonly ConcurrentDictionary<ulong, TaskCompletionSource<BinaryMessage>> _pendingRequests = new();
    private readonly ConcurrentDictionary<string, Guid> _apiMappings = new();
    private readonly ConcurrentDictionary<string, Action<string>> _eventHandlers = new();
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private CancellationTokenSource? _receiveLoopCts;
    private Task? _receiveLoopTask;
    private TaskCompletionSource<bool>? _capabilityManifestReceived;

    private string? _accessToken;
    private string? _refreshToken;
    private string? _serverBaseUrl;
    private string? _sessionId;
    private string? _lastError;
    private string? _connectUrl;

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
    /// Session ID assigned by the server after connection.
    /// </summary>
    public string? SessionId => _sessionId;

    /// <summary>
    /// All available API endpoints with their client-salted GUIDs.
    /// Key format: "serviceName:METHOD:/path"
    /// </summary>
    public IReadOnlyDictionary<string, Guid> AvailableApis => _apiMappings;

    /// <summary>
    /// Legacy property for backwards compatibility.
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
    /// Last error message from a failed operation.
    /// </summary>
    public string? LastError => _lastError;

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
    /// Gets the service GUID for a specific API endpoint.
    /// </summary>
    /// <param name="method">HTTP method (GET, POST, etc.)</param>
    /// <param name="path">API path (e.g., "/accounts/profile")</param>
    /// <returns>The client-salted GUID, or null if not found</returns>
    public Guid? GetServiceGuid(string method, string path)
    {
        // Try various key formats
        foreach (var kvp in _apiMappings)
        {
            // Format: "serviceName:METHOD:/path"
            if (kvp.Key.Contains($":{method}:{path}"))
            {
                return kvp.Value;
            }
        }

        // Try by method and path only
        foreach (var kvp in _apiMappings)
        {
            var parts = kvp.Key.Split(':', 3);
            if (parts.Length >= 3 && parts[1] == method && parts[2] == path)
            {
                return kvp.Value;
            }
        }

        return null;
    }

    /// <summary>
    /// Invokes a service method by specifying the HTTP method and path.
    /// </summary>
    /// <typeparam name="TRequest">Request model type</typeparam>
    /// <typeparam name="TResponse">Response model type</typeparam>
    /// <param name="method">HTTP method (GET, POST, PUT, DELETE)</param>
    /// <param name="path">API path (e.g., "/accounts/profile")</param>
    /// <param name="request">Request payload</param>
    /// <param name="channel">Message channel for ordering (default 0)</param>
    /// <param name="timeout">Request timeout</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Response from service</returns>
    public async Task<TResponse> InvokeAsync<TRequest, TResponse>(
        string method,
        string path,
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
        var serviceGuid = GetServiceGuid(method, path);
        if (serviceGuid == null)
        {
            var availableEndpoints = string.Join(", ", _apiMappings.Keys.Take(10));
            throw new ArgumentException($"Unknown endpoint: {method} {path}. Available: {availableEndpoints}...");
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
            serviceGuid: serviceGuid.Value,
            messageId: messageId,
            payload: payloadBytes);

        // Set up response awaiter
        var tcs = new TaskCompletionSource<BinaryMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[messageId] = tcs;
        _connectionState.AddPendingMessage(messageId, $"{method}:{path}", DateTimeOffset.UtcNow);

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
                throw new TimeoutException($"Request to {method} {path} timed out after {effectiveTimeout.TotalSeconds} seconds");
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
    /// <param name="method">HTTP method</param>
    /// <param name="path">API path</param>
    /// <param name="request">Request payload</param>
    /// <param name="channel">Message channel for ordering</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task SendEventAsync<TRequest>(
        string method,
        string path,
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
        var serviceGuid = GetServiceGuid(method, path)
            ?? throw new ArgumentException($"Unknown endpoint: {method} {path}");

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
    /// Registers a handler for server-pushed events.
    /// </summary>
    /// <param name="eventType">Event type to handle (e.g., "capability_manifest")</param>
    /// <param name="handler">Handler function receiving the JSON payload</param>
    public void OnEvent(string eventType, Action<string> handler)
    {
        _eventHandlers[eventType] = handler;
    }

    /// <summary>
    /// Removes an event handler.
    /// </summary>
    /// <param name="eventType">Event type to unregister</param>
    public void RemoveEventHandler(string eventType)
    {
        _eventHandlers.TryRemove(eventType, out _);
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
        _apiMappings.Clear();
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

        try
        {
            var response = await _httpClient.PostAsJsonAsync(loginUrl, loginRequest, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _lastError = $"Login failed ({response.StatusCode}): {errorContent}";
                return false;
            }

            var result = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken: cancellationToken);
            if (result == null || string.IsNullOrEmpty(result.AccessToken))
            {
                _lastError = "Login response missing access token";
                return false;
            }

            _accessToken = result.AccessToken;
            _refreshToken = result.RefreshToken;
            _connectUrl = result.ConnectUrl;
            return true;
        }
        catch (Exception ex)
        {
            _lastError = $"Login exception: {ex.Message}";
            return false;
        }
    }

    private async Task<bool> RegisterAsync(string username, string email, string password, CancellationToken cancellationToken)
    {
        // Registration goes through the Auth service, not Accounts
        var registerUrl = $"{_serverBaseUrl}/auth/register";

        var registerRequest = new { username, email, password };

        try
        {
            var response = await _httpClient.PostAsJsonAsync(registerUrl, registerRequest, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _lastError = $"Registration failed ({response.StatusCode}): {errorContent}";
                return false;
            }

            var result = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken: cancellationToken);
            if (result == null || string.IsNullOrEmpty(result.AccessToken))
            {
                _lastError = "Registration response missing access token";
                return false;
            }

            _accessToken = result.AccessToken;
            _refreshToken = result.RefreshToken;
            _connectUrl = result.ConnectUrl;
            return true;
        }
        catch (Exception ex)
        {
            _lastError = $"Registration exception: {ex.Message}";
            return false;
        }
    }

    private async Task<bool> EstablishWebSocketAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_accessToken))
        {
            _lastError = "No access token available for WebSocket connection";
            return false;
        }

        string wsUrl;
        if (!string.IsNullOrEmpty(_connectUrl))
        {
            // Use the connectUrl from auth response, converting to WebSocket protocol
            wsUrl = _connectUrl
                .Replace("https://", "wss://")
                .Replace("http://", "ws://");
        }
        else
        {
            // Fall back to default /connect path (not /ws)
            wsUrl = _serverBaseUrl!
                .Replace("https://", "wss://")
                .Replace("http://", "ws://");
            wsUrl = $"{wsUrl}/connect";
        }

        _webSocket = new ClientWebSocket();
        _webSocket.Options.SetRequestHeader("Authorization", $"Bearer {_accessToken}");

        try
        {
            await _webSocket.ConnectAsync(new Uri(wsUrl), cancellationToken);
        }
        catch (Exception ex)
        {
            _lastError = $"WebSocket connection failed to {wsUrl}: {ex.Message}";
            _webSocket.Dispose();
            _webSocket = null;
            return false;
        }

        // Initialize connection state
        var sessionId = Guid.NewGuid().ToString(); // Client-side session ID (will be updated from manifest)
        _connectionState = new ConnectionState(sessionId);

        // Set up capability manifest receiver
        _capabilityManifestReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Start receive loop
        _receiveLoopCts = new CancellationTokenSource();
        _receiveLoopTask = ReceiveLoopAsync(_receiveLoopCts.Token);

        // Wait for capability manifest with timeout (30 seconds)
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));

            await _capabilityManifestReceived.Task.WaitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            // Timeout waiting for capability manifest - connection still works, but no APIs available yet
            // This is okay - capabilities may come later via update
        }

        return true;
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[65536]; // Larger buffer for capability manifests

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
            // Server-pushed event
            HandleEventMessage(message);
        }
    }

    private void HandleEventMessage(BinaryMessage message)
    {
        if (message.Payload.Length == 0)
        {
            return;
        }

        try
        {
            var payloadJson = Encoding.UTF8.GetString(message.Payload.Span);

            // Try to parse as JSON to get the event type
            using var document = JsonDocument.Parse(payloadJson);
            var root = document.RootElement;

            if (root.TryGetProperty("type", out var typeElement))
            {
                var eventType = typeElement.GetString();

                if (eventType == "capability_manifest")
                {
                    HandleCapabilityManifest(root);
                }

                // Dispatch to registered event handlers
                if (eventType != null && _eventHandlers.TryGetValue(eventType, out var handler))
                {
                    handler(payloadJson);
                }
            }
        }
        catch
        {
            // Ignore parse errors for events
        }
    }

    private void HandleCapabilityManifest(JsonElement manifest)
    {
        try
        {
            // Extract session ID
            if (manifest.TryGetProperty("sessionId", out var sessionIdElement))
            {
                _sessionId = sessionIdElement.GetString();
            }

            // Extract available APIs
            if (manifest.TryGetProperty("availableAPIs", out var apisElement) &&
                apisElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var api in apisElement.EnumerateArray())
                {
                    var endpointKey = api.TryGetProperty("endpointKey", out var keyElement)
                        ? keyElement.GetString()
                        : null;

                    var serviceGuidStr = api.TryGetProperty("serviceGuid", out var guidElement)
                        ? guidElement.GetString()
                        : null;

                    if (!string.IsNullOrEmpty(endpointKey) &&
                        !string.IsNullOrEmpty(serviceGuidStr) &&
                        Guid.TryParse(serviceGuidStr, out var serviceGuid))
                    {
                        _apiMappings[endpointKey] = serviceGuid;

                        // Also add to connection state for backwards compatibility
                        _connectionState?.AddServiceMapping(endpointKey, serviceGuid);
                    }
                }
            }

            // Signal that capabilities are received
            _capabilityManifestReceived?.TrySetResult(true);
        }
        catch
        {
            // Ignore manifest parse errors
        }
    }

    #endregion

    #region Internal Models

    private class LoginResponse
    {
        [JsonPropertyName("accessToken")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("refreshToken")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("connectUrl")]
        public string? ConnectUrl { get; set; }
    }

    #endregion
}
