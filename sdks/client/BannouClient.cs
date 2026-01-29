using BeyondImmersion.Bannou.Client.Events;
using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Connect.Protocol;
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

namespace BeyondImmersion.Bannou.Client;

/// <summary>
/// High-level client for connecting to Bannou services via WebSocket.
/// Handles authentication, connection lifecycle, capability discovery, and message correlation.
/// </summary>
public partial class BannouClient : IBannouClient
{
    private ClientWebSocket? _webSocket;
    private ConnectionState? _connectionState;
    private readonly ConcurrentDictionary<ulong, TaskCompletionSource<BinaryMessage>> _pendingRequests = new();
    private readonly ConcurrentDictionary<string, Guid> _apiMappings = new();
    private readonly ConcurrentDictionary<string, Action<string>> _eventHandlers = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Delegate>> _typedEventHandlers = new();
    private readonly ConcurrentDictionary<Type, string> _eventTypeToNameCache = new();
    private readonly Dictionary<string, ClientCapabilityEntry> _previousCapabilities = new();
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
    private string? _serviceToken;

    private const string InternalAuthorizationHeader = "Internal";

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
    /// Key format: "/path" (e.g., "/species/get")
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
    /// Event raised when an event handler throws an exception during invocation.
    /// This allows consumers to observe and log handler failures without propagating
    /// exceptions that could disrupt the event dispatch loop.
    /// </summary>
    public event Action<Exception>? EventHandlerFailed;

    /// <summary>
    /// Event raised when new capabilities are added to the session.
    /// Fires once per capability manifest update with all newly added capabilities.
    /// </summary>
    public event Action<IReadOnlyList<ClientCapabilityEntry>>? OnCapabilitiesAdded;

    /// <summary>
    /// Event raised when capabilities are removed from the session.
    /// Fires once per capability manifest update with all removed capabilities.
    /// </summary>
    public event Action<IReadOnlyList<ClientCapabilityEntry>>? OnCapabilitiesRemoved;

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
        _serviceToken = null;

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
        _serviceToken = null;

        return await EstablishWebSocketAsync(cancellationToken);
    }

    /// <summary>
    /// Connects in internal mode using a service token (or network-trust if token is null) without JWT login.
    /// </summary>
    /// <param name="connectUrl">Full WebSocket URL to the Connect service (internal node).</param>
    /// <param name="serviceToken">Optional X-Service-Token for internal auth mode.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if connection successful</returns>
    public async Task<bool> ConnectInternalAsync(
        string connectUrl,
        string? serviceToken = null,
        CancellationToken cancellationToken = default)
    {
        _serverBaseUrl = null;
        _connectUrl = connectUrl.TrimEnd('/');
        _accessToken = null;
        _refreshToken = null;
        _serviceToken = serviceToken;

        return await EstablishWebSocketAsync(cancellationToken, allowAnonymousInternal: true);
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
        _serviceToken = null;

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
    /// <param name="endpoint">API path (e.g., "/account/get")</param>
    /// <returns>The client-salted GUID, or null if not found</returns>
    public Guid? GetServiceGuid(string endpoint)
    {
        return _apiMappings.TryGetValue(endpoint, out var guid) ? guid : null;
    }

    /// <summary>
    /// Invokes a service method by specifying the API endpoint path.
    /// </summary>
    /// <typeparam name="TRequest">Request model type</typeparam>
    /// <typeparam name="TResponse">Response model type</typeparam>
    /// <param name="endpoint">API path (e.g., "/account/get")</param>
    /// <param name="request">Request payload</param>
    /// <param name="channel">Message channel for ordering (default 0)</param>
    /// <param name="timeout">Request timeout</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>ApiResponse containing either the success result or error details</returns>
    public async Task<ApiResponse<TResponse>> InvokeAsync<TRequest, TResponse>(
        string endpoint,
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
        var serviceGuid = GetServiceGuid(endpoint);
        if (serviceGuid == null)
        {
            var availableEndpoints = string.Join(", ", _apiMappings.Keys.Take(10));
            throw new ArgumentException($"Unknown endpoint: {endpoint}. Available: {availableEndpoints}...");
        }

        // Serialize request to JSON
        var jsonPayload = BannouJson.Serialize(request);
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
        _connectionState.AddPendingMessage(messageId, endpoint, DateTimeOffset.UtcNow);

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

                // Check response code from binary header (byte 15 of 16-byte response header)
                // ResponseCode 0 = OK, non-zero = error
                if (response.ResponseCode != 0)
                {
                    // Error response - payload is empty, response code is in header
                    return ApiResponse<TResponse>.Failure(new ErrorResponse
                    {
                        ResponseCode = ErrorResponse.MapToHttpStatusCode(response.ResponseCode),
                        ErrorName = ErrorResponse.GetErrorName(response.ResponseCode),
                        Message = null, // No message in binary error responses
                        MessageId = messageId,
                        Endpoint = endpoint
                    });
                }

                // Success response - parse JSON payload
                // Handle empty success responses (e.g., 200 OK with no content)
                if (response.Payload.Length == 0)
                {
                    // Return empty success for endpoints that return 200 with no body
                    return ApiResponse<TResponse>.SuccessEmpty();
                }

                var responseJson = response.GetJsonPayload();
                var result = BannouJson.Deserialize<TResponse>(responseJson);
                if (result == null)
                {
                    return ApiResponse<TResponse>.Failure(new ErrorResponse
                    {
                        ResponseCode = 500,
                        ErrorName = "DeserializationError",
                        Message = $"Failed to deserialize response as {typeof(TResponse).Name}",
                        MessageId = messageId,
                        Endpoint = endpoint
                    });
                }

                return ApiResponse<TResponse>.Success(result);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"Request to {endpoint} timed out after {effectiveTimeout.TotalSeconds} seconds");
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
    /// <param name="endpoint">API path (e.g., "/events/publish")</param>
    /// <param name="request">Request payload</param>
    /// <param name="channel">Message channel for ordering</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task SendEventAsync<TRequest>(
        string endpoint,
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
        var serviceGuid = GetServiceGuid(endpoint)
            ?? throw new ArgumentException($"Unknown endpoint: {endpoint}");

        // Serialize request to JSON
        var jsonPayload = BannouJson.Serialize(request);
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
    /// Requests metadata about an endpoint instead of executing it.
    /// Uses the Meta flag (0x80) which triggers route transformation at Connect service.
    /// Meta type is encoded in Channel field (Connect never reads payloads - zero-copy principle).
    /// </summary>
    /// <typeparam name="T">The expected data type for the meta response (e.g., JsonSchemaData, EndpointInfoData)</typeparam>
    /// <param name="endpoint">API path (e.g., "/account/get")</param>
    /// <param name="metaType">Type of metadata to request</param>
    /// <param name="timeout">Request timeout (default 10 seconds)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>MetaResponse containing the requested metadata</returns>
    public async Task<MetaResponse<T>> GetEndpointMetaAsync<T>(
        string endpoint,
        MetaType metaType = MetaType.FullSchema,
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

        // Get service GUID (same as regular requests)
        var serviceGuid = GetServiceGuid(endpoint);
        if (serviceGuid == null)
        {
            var availableEndpoints = string.Join(", ", _apiMappings.Keys.Take(10));
            throw new ArgumentException($"Unknown endpoint: {endpoint}. Available: {availableEndpoints}...");
        }

        // Create meta message - meta type encoded in Channel field, payload is EMPTY
        var messageId = GuidGenerator.GenerateMessageId();
        var sequenceNumber = _connectionState.GetNextSequenceNumber((ushort)metaType);

        var message = new BinaryMessage(
            flags: MessageFlags.Meta,            // Meta flag triggers route transformation
            channel: (ushort)metaType,           // Meta type in Channel (0=info, 1=req, 2=resp, 3=full)
            sequenceNumber: sequenceNumber,
            serviceGuid: serviceGuid.Value,
            messageId: messageId,
            payload: Array.Empty<byte>());       // EMPTY - Connect never reads payloads

        // Set up response awaiter (same pattern as InvokeAsync)
        var tcs = new TaskCompletionSource<BinaryMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[messageId] = tcs;
        _connectionState.AddPendingMessage(messageId, $"META:{endpoint}", DateTimeOffset.UtcNow);

        try
        {
            // Send message
            var messageBytes = message.ToByteArray();
            await _webSocket.SendAsync(
                new ArraySegment<byte>(messageBytes),
                WebSocketMessageType.Binary,
                endOfMessage: true,
                cancellationToken);

            // Wait for response with timeout (shorter default for meta requests)
            var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(10);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(effectiveTimeout);

            try
            {
                var response = await tcs.Task.WaitAsync(timeoutCts.Token);

                // Check response code
                if (response.ResponseCode != 0)
                {
                    throw new InvalidOperationException(
                        $"Meta request failed with response code {response.ResponseCode}");
                }

                // Parse meta response
                var responseJson = response.GetJsonPayload();
                var result = BannouJson.Deserialize<MetaResponse<T>>(responseJson)
                    ?? throw new InvalidOperationException("Failed to deserialize meta response");

                return result;
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"Meta request for {endpoint} timed out after {effectiveTimeout.TotalSeconds} seconds");
            }
        }
        finally
        {
            _pendingRequests.TryRemove(messageId, out _);
            _connectionState.RemovePendingMessage(messageId);
        }
    }

    /// <summary>
    /// Gets human-readable endpoint information (summary, description, tags, deprecated status).
    /// </summary>
    /// <param name="endpoint">API path (e.g., "/account/get")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public Task<MetaResponse<EndpointInfoData>> GetEndpointInfoAsync(
        string endpoint,
        CancellationToken cancellationToken = default)
        => GetEndpointMetaAsync<EndpointInfoData>(endpoint, MetaType.EndpointInfo, null, cancellationToken);

    /// <summary>
    /// Gets JSON Schema for the request body of an endpoint.
    /// </summary>
    /// <param name="endpoint">API path (e.g., "/account/get")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public Task<MetaResponse<JsonSchemaData>> GetRequestSchemaAsync(
        string endpoint,
        CancellationToken cancellationToken = default)
        => GetEndpointMetaAsync<JsonSchemaData>(endpoint, MetaType.RequestSchema, null, cancellationToken);

    /// <summary>
    /// Gets JSON Schema for the response body of an endpoint.
    /// </summary>
    /// <param name="endpoint">API path (e.g., "/account/get")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public Task<MetaResponse<JsonSchemaData>> GetResponseSchemaAsync(
        string endpoint,
        CancellationToken cancellationToken = default)
        => GetEndpointMetaAsync<JsonSchemaData>(endpoint, MetaType.ResponseSchema, null, cancellationToken);

    /// <summary>
    /// Gets full schema including info, request schema, and response schema.
    /// </summary>
    /// <param name="endpoint">API path (e.g., "/account/get")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public Task<MetaResponse<FullSchemaData>> GetFullSchemaAsync(
        string endpoint,
        CancellationToken cancellationToken = default)
        => GetEndpointMetaAsync<FullSchemaData>(endpoint, MetaType.FullSchema, null, cancellationToken);

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
    /// Subscribe to a typed event with automatic deserialization.
    /// The event type must be a generated client event class inheriting from <see cref="BaseClientEvent"/>.
    /// </summary>
    /// <typeparam name="TEvent">Event type to subscribe to (e.g., ChatMessageReceivedEvent)</typeparam>
    /// <param name="handler">Handler to invoke when event is received, with the deserialized event object</param>
    /// <returns>Subscription handle - call <see cref="IDisposable.Dispose"/> to unsubscribe</returns>
    /// <exception cref="ArgumentException">Thrown if TEvent is not a registered client event type</exception>
    public IEventSubscription OnEvent<TEvent>(Action<TEvent> handler) where TEvent : BaseClientEvent
    {
        ArgumentNullException.ThrowIfNull(handler);

        var eventName = GetEventNameForType<TEvent>();
        if (string.IsNullOrEmpty(eventName))
        {
            throw new ArgumentException(
                $"Unknown event type: {typeof(TEvent).Name}. The event class must have a non-empty EventName property.");
        }

        var subscriptionId = Guid.NewGuid();
        var handlers = _typedEventHandlers.GetOrAdd(eventName, _ => new ConcurrentDictionary<Guid, Delegate>());
        handlers[subscriptionId] = handler;

        return new EventSubscription(eventName, subscriptionId, id =>
        {
            if (_typedEventHandlers.TryGetValue(eventName, out var h))
            {
                h.TryRemove(id, out _);
            }
        });
    }

    /// <summary>
    /// Remove all typed handlers for a specific event type.
    /// </summary>
    /// <typeparam name="TEvent">Event type to remove all handlers for</typeparam>
    public void RemoveEventHandlers<TEvent>() where TEvent : BaseClientEvent
    {
        var eventName = GetEventNameForType<TEvent>();
        if (!string.IsNullOrEmpty(eventName))
        {
            _typedEventHandlers.TryRemove(eventName, out _);
        }
    }

    /// <summary>
    /// Get the eventName for a typed event class from the ClientEventRegistry.
    /// Results are cached for performance.
    /// </summary>
    private string? GetEventNameForType<TEvent>() where TEvent : BaseClientEvent
    {
        return _eventTypeToNameCache.GetOrAdd(typeof(TEvent), _ =>
            ClientEventRegistry.GetEventName<TEvent>() ?? string.Empty);
    }

    /// <summary>
    /// Dispatches an event to all typed handlers registered for the given eventName.
    /// </summary>
    private void DispatchTypedEvent(string eventName, string payloadJson)
    {
        if (!_typedEventHandlers.TryGetValue(eventName, out var handlers) || handlers.IsEmpty)
        {
            return;
        }

        // Look up the type from the registry
        var eventType = ClientEventRegistry.GetEventType(eventName);
        if (eventType == null)
        {
            return;
        }

        // Deserialize once, invoke all handlers
        object? evt = null;
        try
        {
            evt = BannouJson.Deserialize(payloadJson, eventType);
        }
        catch (JsonException)
        {
            return; // Skip if deserialization fails
        }

        if (evt == null)
        {
            return;
        }

        foreach (var kvp in handlers)
        {
            try
            {
                kvp.Value.DynamicInvoke(evt);
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException or ThreadAbortException))
            {
                // Notify subscribers but don't propagate handler exceptions
                // Fatal exceptions (OOM, stack overflow, thread abort) are rethrown
                EventHandlerFailed?.Invoke(ex);
            }
        }
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
        _serviceToken = null;
        _apiMappings.Clear();
        _previousCapabilities.Clear();
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

    private string BuildAuthorizationHeader(bool allowAnonymousInternal)
    {
        if (!string.IsNullOrEmpty(_accessToken))
        {
            return $"Bearer {_accessToken}";
        }

        if (!string.IsNullOrEmpty(_serviceToken) || allowAnonymousInternal)
        {
            return InternalAuthorizationHeader;
        }

        return string.Empty;
    }

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
        // Registration goes through the Auth service, not Account
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

    private async Task<bool> EstablishWebSocketAsync(CancellationToken cancellationToken, bool allowAnonymousInternal = false)
    {
        if (string.IsNullOrEmpty(_accessToken) && string.IsNullOrEmpty(_serviceToken) && !allowAnonymousInternal)
        {
            _lastError = "No access token available for WebSocket connection";
            return false;
        }

        // Convert to WebSocket protocol - the URL should already include the full path
        // When using ConnectAsync/RegisterAndConnectAsync, _connectUrl comes from auth response
        // When using ConnectWithTokenAsync, the caller provides the full URL
        string wsUrl;
        if (!string.IsNullOrEmpty(_connectUrl))
        {
            // Use the connectUrl from auth response
            wsUrl = _connectUrl
                .Replace("https://", "wss://")
                .Replace("http://", "ws://");
        }
        else
        {
            // Use the provided server URL as-is (caller should include /connect path)
            wsUrl = _serverBaseUrl!
                .Replace("https://", "wss://")
                .Replace("http://", "ws://");
        }

        _webSocket = new ClientWebSocket();
        var authorizationHeader = BuildAuthorizationHeader(allowAnonymousInternal);
        if (!string.IsNullOrEmpty(authorizationHeader))
        {
            _webSocket.Options.SetRequestHeader("Authorization", authorizationHeader);
        }

        if (!string.IsNullOrEmpty(_serviceToken))
        {
            _webSocket.Options.SetRequestHeader("X-Service-Token", _serviceToken);
        }

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

                // Check for minimum message size - response headers are 16 bytes, request headers are 31 bytes
                // Use ResponseHeaderSize as minimum since that's the smallest valid message
                if (result.MessageType == WebSocketMessageType.Binary && result.Count >= BinaryMessage.ResponseHeaderSize)
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

            // All events use eventName property for consistency
            if (root.TryGetProperty("eventName", out var eventNameElement))
            {
                var eventName = eventNameElement.GetString();

                if (eventName == "connect.capability_manifest")
                {
                    HandleCapabilityManifest(root);
                }

                // Dispatch to registered string-based event handlers (existing behavior)
                if (eventName != null && _eventHandlers.TryGetValue(eventName, out var handler))
                {
                    handler(payloadJson);
                }

                // Dispatch to typed event handlers
                if (eventName != null)
                {
                    DispatchTypedEvent(eventName, payloadJson);
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

            // Parse all capabilities into typed entries
            var currentCapabilities = new Dictionary<string, ClientCapabilityEntry>();

            if (manifest.TryGetProperty("availableApis", out var apisElement) &&
                apisElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var api in apisElement.EnumerateArray())
                {
                    var endpoint = api.TryGetProperty("endpoint", out var endpointElement)
                        ? endpointElement.GetString()
                        : null;

                    var serviceIdStr = api.TryGetProperty("serviceId", out var idElement)
                        ? idElement.GetString()
                        : null;

                    var service = api.TryGetProperty("service", out var serviceElement)
                        ? serviceElement.GetString()
                        : null;

                    var description = api.TryGetProperty("description", out var descElement)
                        ? descElement.GetString()
                        : null;

                    if (!string.IsNullOrEmpty(endpoint) &&
                        !string.IsNullOrEmpty(serviceIdStr) &&
                        !string.IsNullOrEmpty(service) &&
                        Guid.TryParse(serviceIdStr, out var serviceGuid))
                    {
                        var entry = new ClientCapabilityEntry
                        {
                            ServiceId = serviceGuid,
                            Endpoint = endpoint,
                            Service = service,
                            Description = description
                        };

                        currentCapabilities[endpoint] = entry;

                        // Update API mappings for InvokeAsync lookups
                        _apiMappings[endpoint] = serviceGuid;

                        // Also add to connection state for backwards compatibility
                        _connectionState?.AddServiceMapping(endpoint, serviceGuid);
                    }
                }
            }

            // Diff: find added capabilities (in current but not in previous)
            var added = new List<ClientCapabilityEntry>();
            foreach (var kvp in currentCapabilities)
            {
                if (!_previousCapabilities.ContainsKey(kvp.Key))
                {
                    added.Add(kvp.Value);
                }
            }

            // Diff: find removed capabilities (in previous but not in current)
            var removed = new List<ClientCapabilityEntry>();
            foreach (var kvp in _previousCapabilities)
            {
                if (!currentCapabilities.ContainsKey(kvp.Key))
                {
                    removed.Add(kvp.Value);

                    // Remove from API mappings
                    _apiMappings.TryRemove(kvp.Key, out _);
                }
            }

            // Update previous state for next diff
            _previousCapabilities.Clear();
            foreach (var kvp in currentCapabilities)
            {
                _previousCapabilities[kvp.Key] = kvp.Value;
            }

            // Fire events if there were changes
            if (added.Count > 0)
            {
                OnCapabilitiesAdded?.Invoke(added);
            }

            if (removed.Count > 0)
            {
                OnCapabilitiesRemoved?.Invoke(removed);
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

/// <summary>
/// Represents an error response from a Bannou API call.
/// Contains all available information about the failed request.
/// </summary>
public class ErrorResponse
{
    /// <summary>
    /// The HTTP status code equivalent for this error.
    /// Standard HTTP codes: 400 = Bad Request, 401 = Unauthorized, 404 = Not Found, 409 = Conflict, 500 = Internal Server Error.
    /// </summary>
    public int ResponseCode { get; init; }

    /// <summary>
    /// Maps internal WebSocket protocol response codes to standard HTTP status codes.
    /// This hides the binary protocol implementation details from client code.
    /// </summary>
    internal static int MapToHttpStatusCode(int wsResponseCode) => wsResponseCode switch
    {
        0 => 200,   // OK
        50 => 400,  // Service_BadRequest
        51 => 404,  // Service_NotFound
        52 => 401,  // Service_Unauthorized (covers both 401/403)
        53 => 409,  // Service_Conflict
        60 => 500,  // Service_InternalServerError
        // Pass through other codes as 500 for safety
        _ => 500
    };

    /// <summary>
    /// Maps internal WebSocket protocol response codes to error names.
    /// </summary>
    internal static string GetErrorName(int wsResponseCode) => wsResponseCode switch
    {
        0 => "OK",
        50 => "BadRequest",
        51 => "NotFound",
        52 => "Unauthorized",
        53 => "Conflict",
        60 => "InternalServerError",
        _ => "UnknownError"
    };

    /// <summary>
    /// Human-readable error name (e.g., "Service_InternalServerError", "Unauthorized").
    /// </summary>
    public string? ErrorName { get; init; }

    /// <summary>
    /// Detailed error message from the server, if provided.
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// The unique message ID for request/response correlation.
    /// </summary>
    public ulong MessageId { get; init; }

    /// <summary>
    /// The endpoint path of the original request (e.g., "/subscription/create").
    /// </summary>
    public string Endpoint { get; init; } = string.Empty;
}

/// <summary>
/// Represents the result of an API call, containing either a success response or error details.
/// </summary>
/// <typeparam name="T">The expected response type on success.</typeparam>
public class ApiResponse<T>
{
    /// <summary>
    /// Whether the API call was successful.
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// The successful response data. Only valid when IsSuccess is true.
    /// </summary>
    public T? Result { get; init; }

    /// <summary>
    /// Error details when the request failed. Only valid when IsSuccess is false.
    /// </summary>
    public ErrorResponse? Error { get; init; }

    /// <summary>
    /// Creates a successful response.
    /// </summary>
    public static ApiResponse<T> Success(T result) => new()
    {
        IsSuccess = true,
        Result = result
    };

    /// <summary>
    /// Creates a successful response with no content (empty body).
    /// Used for endpoints that return 200 OK without a response body.
    /// </summary>
    public static ApiResponse<T> SuccessEmpty() => new()
    {
        IsSuccess = true,
        Result = default
    };

    /// <summary>
    /// Creates an error response.
    /// </summary>
    public static ApiResponse<T> Failure(ErrorResponse error) => new()
    {
        IsSuccess = false,
        Error = error
    };

    /// <summary>
    /// Gets the result if successful, or throws an InvalidOperationException with error details.
    /// For empty success responses (200 with no body), returns default(T).
    /// Useful for test code or scenarios where exceptions are preferred over explicit error handling.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the response is an error.</exception>
    public T? GetResultOrThrow()
    {
        if (IsSuccess)
        {
            // Return result even if null/default (for empty success responses)
            return Result;
        }

        var error = Error;
        throw new InvalidOperationException(
            $"{error?.ErrorName ?? "Error"}: {error?.Message ?? "Request failed"} (code: {error?.ResponseCode ?? -1})");
    }
}
