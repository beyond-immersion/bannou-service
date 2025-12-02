using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.Connect.Protocol;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Permissions;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using Dapr;
using Dapr.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Net.Http;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

[assembly: InternalsVisibleTo("lib-connect.tests")]

namespace BeyondImmersion.BannouService.Connect;

/// <summary>
/// WebSocket-first edge gateway service providing zero-copy message routing.
/// Uses Permissions service for dynamic API discovery and capability management.
/// </summary>
[DaprService("connect", typeof(IConnectService), lifetime: ServiceLifetime.Singleton)]
public class ConnectService : IConnectService
{
    private readonly IAuthClient _authClient;
    private readonly IPermissionsClient _permissionsClient;
    private readonly DaprClient _daprClient;
    private readonly HttpClient _httpClient;
    private readonly IServiceAppMappingResolver _appMappingResolver;
    private readonly ILogger<ConnectService> _logger;
    private readonly WebSocketConnectionManager _connectionManager;
    private readonly RedisSessionManager? _sessionManager;

    // Session to service GUID mappings (legacy - moving to Redis)
    private readonly ConcurrentDictionary<string, Dictionary<string, Guid>> _sessionServiceMappings;
    private readonly string _serverSalt;
    private readonly string _instanceId;

    public ConnectService(
        IAuthClient authClient,
        IPermissionsClient permissionsClient,
        DaprClient daprClient,
        IServiceAppMappingResolver appMappingResolver,
        ConnectServiceConfiguration configuration,
        ILogger<ConnectService> logger,
        RedisSessionManager? sessionManager = null)
    {
        _authClient = authClient ?? throw new ArgumentNullException(nameof(authClient));
        _permissionsClient = permissionsClient ?? throw new ArgumentNullException(nameof(permissionsClient));
        _daprClient = daprClient ?? throw new ArgumentNullException(nameof(daprClient));
        _httpClient = new HttpClient(); // Reusable HttpClient for Dapr service invocation
        _appMappingResolver = appMappingResolver ?? throw new ArgumentNullException(nameof(appMappingResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _sessionManager = sessionManager; // Optional Redis session management

        _sessionServiceMappings = new ConcurrentDictionary<string, Dictionary<string, Guid>>();
        _connectionManager = new WebSocketConnectionManager();

        // Generate server salt for GUID generation (use cryptographic salt)
        _serverSalt = GuidGenerator.GenerateServerSalt();

        // Generate unique instance ID for distributed deployment
        _instanceId = Environment.MachineName + "-" + Guid.NewGuid().ToString("N")[..8];

        _logger.LogInformation("Connect service initialized with instance ID: {InstanceId}, Redis: {RedisEnabled}",
            _instanceId, sessionManager != null ? "Enabled" : "Disabled");
    }

    /// <summary>
    /// Internal API proxy for stateless requests.
    /// Routes requests through Dapr to the appropriate service.
    /// </summary>
    public async Task<(StatusCodes, InternalProxyResponse?)> ProxyInternalRequestAsync(
        InternalProxyRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Processing internal proxy request to {TargetService}/{Method} {Endpoint}",
                body.TargetService, body.Method, body.TargetEndpoint);

            // Validate session has access to this API
            var validationRequest = new ValidationRequest
            {
                SessionId = body.SessionId,
                ServiceId = body.TargetService,
                Method = $"{body.Method}:{body.TargetEndpoint}"
            };

            var validationResponse = await _permissionsClient.ValidateApiAccessAsync(
                validationRequest, cancellationToken);

            if (!validationResponse.Allowed)
            {
                _logger.LogWarning("Session {SessionId} denied access to {Service}/{Method}",
                    body.SessionId, body.TargetService, body.Method);

                return (StatusCodes.Forbidden, new InternalProxyResponse
                {
                    Success = false,
                    StatusCode = 403,
                    Error = validationResponse.Reason ?? "Access denied"
                });
            }

            // Build the full URL path with path parameters
            var endpoint = body.TargetEndpoint;
            if (body.PathParameters != null)
            {
                foreach (var param in body.PathParameters)
                {
                    endpoint = endpoint.Replace($"{{{param.Key}}}", param.Value);
                }
            }

            // Add query parameters
            if (body.QueryParameters != null && body.QueryParameters.Count > 0)
            {
                var queryString = string.Join("&",
                    body.QueryParameters.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
                endpoint = $"{endpoint}?{queryString}";
            }

            // Use ServiceAppMappingResolver for dynamic app-id resolution
            // This enables distributed deployment where services can run on different nodes
            var appId = _appMappingResolver.GetAppIdForService(body.TargetService);

            _logger.LogDebug("Routing request to service {Service} via app-id {AppId}",
                body.TargetService, appId);

            // Create HTTP request
            var httpMethod = body.Method switch
            {
                InternalProxyRequestMethod.GET => HttpMethod.Get,
                InternalProxyRequestMethod.POST => HttpMethod.Post,
                InternalProxyRequestMethod.PUT => HttpMethod.Put,
                InternalProxyRequestMethod.DELETE => HttpMethod.Delete,
                InternalProxyRequestMethod.PATCH => HttpMethod.Patch,
                _ => HttpMethod.Get
            };

            var startTime = DateTime.UtcNow;

            try
            {
                // Route through Dapr service invocation
                HttpResponseMessage httpResponse;

                if (body.Method == InternalProxyRequestMethod.GET ||
                    body.Method == InternalProxyRequestMethod.DELETE)
                {
                    // For GET/DELETE, no body
                    var request = _daprClient.CreateInvokeMethodRequest(httpMethod, appId, endpoint);
                    httpResponse = await _daprClient.InvokeMethodWithResponseAsync(request, cancellationToken);
                }
                else
                {
                    // For POST/PUT/PATCH, include body
                    var jsonBody = body.Body != null ? JsonSerializer.Serialize(body.Body) : null;

                    var content = jsonBody != null ?
                        new StringContent(jsonBody, Encoding.UTF8, "application/json") : null;
                    var request = _daprClient.CreateInvokeMethodRequest(httpMethod, appId, endpoint);
                    if (content != null)
                    {
                        request.Content = content;
                    }
                    httpResponse = await _daprClient.InvokeMethodWithResponseAsync(request, cancellationToken);
                }

                // Read response
                var responseContent = await httpResponse.Content.ReadAsStringAsync(cancellationToken);

                // Convert headers
                var responseHeaders = new Dictionary<string, ICollection<string>>();
                foreach (var header in httpResponse.Headers)
                {
                    responseHeaders[header.Key] = header.Value.ToList();
                }
                foreach (var header in httpResponse.Content.Headers)
                {
                    responseHeaders[header.Key] = header.Value.ToList();
                }

                var executionTime = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;

                var response = new InternalProxyResponse
                {
                    Success = httpResponse.IsSuccessStatusCode,
                    StatusCode = (int)httpResponse.StatusCode,
                    Response = responseContent,
                    Headers = responseHeaders,
                    ExecutionTime = executionTime
                };

                return (StatusCodes.OK, response);
            }
            catch (Exception daprEx)
            {
                _logger.LogError(daprEx, "Dapr service invocation failed for {Service}/{Endpoint}",
                    body.TargetService, endpoint);

                var errorResponse = new InternalProxyResponse
                {
                    Success = false,
                    StatusCode = 503,
                    Error = $"Service invocation failed: {daprEx.Message}",
                    ExecutionTime = (int)(DateTime.UtcNow - startTime).TotalMilliseconds
                };

                return (StatusCodes.InternalServerError, errorResponse);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing internal proxy request");
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Publishes a service mapping update event to notify all services of routing changes.
    /// Used when services come online or change their deployment topology.
    /// </summary>
    private async Task PublishServiceMappingUpdateAsync(string serviceName, string appId, string action = "update")
    {
        try
        {
            var mappingEvent = new
            {
                EventId = Guid.NewGuid().ToString(),
                Timestamp = DateTime.UtcNow,
                ServiceName = serviceName,
                AppId = appId,
                Action = action,
                Metadata = new Dictionary<string, object>
                {
                    { "source", "connect-service" },
                    { "region", Environment.GetEnvironmentVariable("SERVICE_REGION") ?? "default" }
                }
            };

            await _daprClient.PublishEventAsync(
                "bannou-pubsub",
                "bannou-service-mappings",
                mappingEvent);

            _logger.LogInformation("Published service mapping update: {Service} -> {AppId} ({Action})",
                serviceName, appId, action);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish service mapping update for {Service}", serviceName);
            // Non-critical - continue operation even if event publishing fails
        }
    }

    // REMOVED: DiscoverAPIsAsync - API discovery belongs to Permissions service, not Connect service
    // Connect service ONLY handles WebSocket connections and message routing


    // REMOVED: ParseMethod, GetServiceCategory, GetPreferredChannel methods
    // These were only used by the removed DiscoverAPIsAsync method

    /// <summary>
    /// Gets current service routing information for monitoring/debugging.
    /// Shows how services are mapped to app-ids in the current deployment.
    /// </summary>
    public Task<(StatusCodes, ServiceMappingsResponse?)> GetServiceMappingsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var mappings = _appMappingResolver.GetAllMappings();

            var response = new ServiceMappingsResponse
            {
                Mappings = new Dictionary<string, string>(mappings),
                DefaultMapping = "bannou",
                GeneratedAt = DateTimeOffset.UtcNow,
                TotalServices = mappings.Count
            };

            // Add default mapping info if no custom mappings exist
            if (response.Mappings.Count == 0)
            {
                response.Mappings["_info"] = "All services routing to default 'bannou' app-id";
            }

            _logger.LogDebug("Returning {Count} service mappings", response.TotalServices);
            return Task.FromResult<(StatusCodes, ServiceMappingsResponse?)>(((StatusCodes, ServiceMappingsResponse?))(StatusCodes.OK, response));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving service mappings");
            return Task.FromResult<(StatusCodes, ServiceMappingsResponse?)>((StatusCodes.InternalServerError, null));
        }
    }

    /// <summary>
    /// Validates JWT token and extracts session ID and user roles.
    /// Returns a tuple with session ID and roles for capability initialization.
    /// </summary>
    public async Task<(string? SessionId, ICollection<string>? Roles)> ValidateJWTAndExtractSessionAsync(
        string authorization,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrEmpty(authorization))
            {
                return (null, null);
            }

            // Handle "Bearer <token>" format
            if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = authorization.Substring(7);

                // Use auth service to validate token with header-based authorization
                var validationResponse = await ((AuthClient)_authClient)
                    .WithAuthorization(token)
                    .ValidateTokenAsync(cancellationToken);

                if (validationResponse.Valid && !string.IsNullOrEmpty(validationResponse.SessionId))
                {
                    // Return both session ID and roles for capability initialization
                    return (validationResponse.SessionId, validationResponse.Roles);
                }
            }
            // Handle "Reconnect <token>" format (future enhancement)
            else if (authorization.StartsWith("Reconnect ", StringComparison.OrdinalIgnoreCase))
            {
                // TODO: Implement reconnection logic with stored session tokens
                _logger.LogWarning("Reconnection tokens not yet implemented");
                return (null, null);
            }

            return (null, null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "JWT validation failed");
            return (null, null);
        }
    }

    /// <summary>
    /// Determines the highest priority role from a collection of roles.
    /// Priority: admin > user > anonymous
    /// </summary>
    private static string DetermineHighestPriorityRole(ICollection<string>? roles)
    {
        if (roles == null || roles.Count == 0)
        {
            return "anonymous";
        }

        // Check for admin role (highest priority)
        if (roles.Any(r => r.Equals("admin", StringComparison.OrdinalIgnoreCase)))
        {
            return "admin";
        }

        // Check for user role
        if (roles.Any(r => r.Equals("user", StringComparison.OrdinalIgnoreCase)))
        {
            return "user";
        }

        // If roles exist but none are recognized, use the first one
        return roles.FirstOrDefault() ?? "anonymous";
    }

    /// <summary>
    /// Handles WebSocket communication using the enhanced 31-byte binary protocol.
    /// Creates persistent connection state and processes messages with proper routing.
    /// </summary>
    public async Task HandleWebSocketCommunicationAsync(
        WebSocket webSocket,
        string sessionId,
        ICollection<string>? userRoles,
        CancellationToken cancellationToken)
    {
        // Create connection state with service mappings from discovery
        var connectionState = new ConnectionState(sessionId);

        // Transfer service mappings from session discovery to connection state
        Dictionary<string, Guid>? sessionMappings = null;

        if (_sessionManager != null)
        {
            // Try to get mappings from Redis first
            sessionMappings = await _sessionManager.GetSessionServiceMappingsAsync(sessionId);
        }

        if (sessionMappings == null && _sessionServiceMappings.TryGetValue(sessionId, out var fallbackMappings))
        {
            // Fallback to in-memory mappings
            sessionMappings = fallbackMappings;
        }

        // If no mappings exist, initialize capabilities from Permissions service
        if (sessionMappings == null || sessionMappings.Count == 0)
        {
            // Determine the highest-priority role for capability initialization
            // Priority: admin > user > anonymous
            var role = DetermineHighestPriorityRole(userRoles);
            _logger.LogInformation("No existing mappings for session {SessionId}, initializing capabilities for role {Role}", sessionId, role);
            sessionMappings = await InitializeSessionCapabilitiesAsync(sessionId, role, cancellationToken);
        }

        if (sessionMappings != null)
        {
            foreach (var mapping in sessionMappings)
            {
                connectionState.AddServiceMapping(mapping.Key, mapping.Value);
            }
        }

        // Add connection to manager
        _connectionManager.AddConnection(sessionId, webSocket, connectionState);

        var buffer = new byte[65536]; // Larger buffer for binary protocol

        try
        {
            _logger.LogInformation("WebSocket connection established for session {SessionId}", sessionId);

            // Update session heartbeat in Redis
            if (_sessionManager != null)
            {
                await _sessionManager.UpdateSessionHeartbeatAsync(sessionId, _instanceId);
            }

            // Push initial capability manifest to client
            // This tells the client what APIs they can access and provides their client-salted GUIDs
            await SendCapabilityManifestAsync(webSocket, sessionId, connectionState, cancellationToken);

            while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("WebSocket close requested for session {SessionId}", sessionId);
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure,
                        "Connection closed by client", cancellationToken);
                    break;
                }

                connectionState.UpdateActivity();

                // Periodic heartbeat update (every 30 seconds)
                if (_sessionManager != null &&
                    (DateTimeOffset.UtcNow - connectionState.LastActivity).TotalSeconds >= 30)
                {
                    await _sessionManager.UpdateSessionHeartbeatAsync(sessionId, _instanceId);
                }

                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    await HandleBinaryMessageAsync(sessionId, connectionState, buffer, result.Count, cancellationToken);
                }
                else if (result.MessageType == WebSocketMessageType.Text)
                {
                    // For backwards compatibility, handle text messages as JSON-wrapped binary
                    var textMessage = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    await HandleLegacyTextMessageAsync(sessionId, connectionState, textMessage, cancellationToken);
                }
            }
        }
        catch (WebSocketException wsEx)
        {
            _logger.LogWarning(wsEx, "WebSocket error for session {SessionId}: {Error}",
                sessionId, wsEx.Message);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("WebSocket operation cancelled for session {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in WebSocket communication for session {SessionId}", sessionId);
        }
        finally
        {
            // Remove from connection manager
            _connectionManager.RemoveConnection(sessionId);

            // Clean up Redis session data
            if (_sessionManager != null)
            {
                await _sessionManager.RemoveSessionAsync(sessionId);
                await _sessionManager.PublishSessionEventAsync("disconnect", sessionId, new { instanceId = _instanceId });
            }

            if (webSocket.State == WebSocketState.Open)
            {
                try
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure,
                        "Session ended", CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error closing WebSocket for session {SessionId}", sessionId);
                }
            }

            _logger.LogInformation("WebSocket session {SessionId} cleanup completed", sessionId);
        }
    }

    /// <summary>
    /// Handles binary messages using the enhanced 31-byte protocol with proper routing.
    /// </summary>
    private async Task HandleBinaryMessageAsync(
        string sessionId,
        ConnectionState connectionState,
        byte[] buffer,
        int messageLength,
        CancellationToken cancellationToken)
    {
        try
        {
            // Parse binary message using protocol class
            var message = BinaryMessage.Parse(buffer, messageLength);

            _logger.LogDebug("Binary message from session {SessionId}: {Message}",
                sessionId, message.ToString());

            // Analyze message for routing
            var routeInfo = MessageRouter.AnalyzeMessage(message, connectionState);

            if (!routeInfo.IsValid)
            {
                _logger.LogWarning("Invalid message from session {SessionId}: {Error}",
                    sessionId, routeInfo.ErrorMessage);

                var errorResponse = MessageRouter.CreateErrorResponse(
                    message, routeInfo.ErrorCode, routeInfo.ErrorMessage);

                await _connectionManager.SendMessageAsync(sessionId, errorResponse, cancellationToken);
                return;
            }

            // Check rate limiting
            var rateLimitResult = MessageRouter.CheckRateLimit(connectionState);
            if (!rateLimitResult.IsAllowed)
            {
                _logger.LogWarning("Rate limit exceeded for session {SessionId}", sessionId);

                var rateLimitResponse = MessageRouter.CreateErrorResponse(
                    message, ResponseCodes.TooManyRequests, "Rate limit exceeded");

                await _connectionManager.SendMessageAsync(sessionId, rateLimitResponse, cancellationToken);
                return;
            }

            // Route message to appropriate handler
            if (routeInfo.RouteType == RouteType.Service)
            {
                await RouteToServiceAsync(message, routeInfo, sessionId, connectionState, cancellationToken);
            }
            else if (routeInfo.RouteType == RouteType.Client)
            {
                await RouteToClientAsync(message, routeInfo, sessionId, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling binary message from session {SessionId}", sessionId);

            // Send generic error response if we can parse the message
            try
            {
                var message = BinaryMessage.Parse(buffer, messageLength);
                var errorResponse = MessageRouter.CreateErrorResponse(
                    message, ResponseCodes.RequestError, "Internal server error");

                await _connectionManager.SendMessageAsync(sessionId, errorResponse, cancellationToken);
            }
            catch
            {
                // If we can't even parse the message, just ignore it
            }
        }
    }

    /// <summary>
    /// Routes a message to a Dapr service and handles the response.
    /// ServiceName format: "servicename:METHOD:/path" (e.g., "orchestrator:GET:/orchestrator/health/infrastructure")
    /// </summary>
    private async Task RouteToServiceAsync(
        BinaryMessage message,
        MessageRouteInfo routeInfo,
        string sessionId,
        ConnectionState connectionState,
        CancellationToken cancellationToken)
    {
        try
        {
            // Validate service name early - format: "servicename:METHOD:/path"
            var endpointKey = routeInfo.ServiceName ?? throw new InvalidOperationException("Service name is null in route info");

            // Add to pending messages for response correlation
            if (routeInfo.RequiresResponse)
            {
                connectionState.AddPendingMessage(message.MessageId, endpointKey, DateTimeOffset.UtcNow);
            }

            // Parse endpoint key to extract service name, HTTP method, and path
            // Format: "servicename:METHOD:/path"
            var parts = endpointKey.Split(':', 3);
            if (parts.Length < 3)
            {
                throw new InvalidOperationException($"Invalid endpoint key format: {endpointKey}");
            }

            var serviceName = parts[0];
            var httpMethod = parts[1].ToUpperInvariant();
            var path = parts[2];

            // Use ServiceAppMappingResolver for dynamic app-id resolution (using just service name)
            var appId = _appMappingResolver.GetAppIdForService(serviceName);

            _logger.LogInformation("Routing WebSocket message to service {Service} ({Method} {Path}) via app-id {AppId}",
                serviceName, httpMethod, path, appId);

            // Get JSON payload from message - may contain request body for POST/PUT
            var jsonPayload = message.GetJsonPayload();
            object? requestBody = null;

            // Parse the JSON payload if present - client may send structured request
            if (!string.IsNullOrWhiteSpace(jsonPayload))
            {
                try
                {
                    using var jsonDoc = JsonDocument.Parse(jsonPayload);
                    var root = jsonDoc.RootElement;

                    // Check if client sent body in the payload
                    if (root.TryGetProperty("body", out var bodyElement) &&
                        bodyElement.ValueKind != JsonValueKind.Null)
                    {
                        // If body is a string, parse it as JSON; otherwise use as-is
                        if (bodyElement.ValueKind == JsonValueKind.String)
                        {
                            var bodyStr = bodyElement.GetString();
                            if (!string.IsNullOrWhiteSpace(bodyStr))
                            {
                                requestBody = JsonSerializer.Deserialize<JsonElement>(bodyStr);
                            }
                        }
                        else
                        {
                            requestBody = bodyElement.Clone();
                        }
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogDebug(ex, "Could not parse JSON payload as structured request, using raw payload");
                    // If parsing fails, use raw payload for POST/PUT body
                    if (httpMethod is "POST" or "PUT" or "PATCH")
                    {
                        requestBody = jsonPayload;
                    }
                }
            }

            // Make the actual Dapr service invocation via direct HTTP
            // This preserves the full path including /v1.0/invoke/{appId}/method/ prefix
            // which is required because NSwag-generated controllers have route prefix [Route("v1.0/invoke/bannou/method")]
            string? responseJson = null;
            HttpResponseMessage? httpResponse = null;

            try
            {
                // Build full Dapr URL like DaprServiceClientBase does
                var daprHttpEndpoint = Environment.GetEnvironmentVariable("DAPR_HTTP_ENDPOINT") ?? "http://localhost:3500";
                var daprUrl = $"{daprHttpEndpoint}/v1.0/invoke/{appId}/method/{path.TrimStart('/')}";

                // Create HTTP request with proper headers
                using var request = new HttpRequestMessage(new HttpMethod(httpMethod), daprUrl);

                // Add dapr-app-id header for routing (like DaprServiceClientBase.PrepareRequest)
                request.Headers.Add("dapr-app-id", appId);
                request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

                _logger.LogInformation("Created Dapr HTTP request: Method={Method}, URI={Uri}, AppId={AppId}",
                    request.Method, daprUrl, appId);

                // Add request body for methods that support it
                if (requestBody != null && httpMethod is "POST" or "PUT" or "PATCH")
                {
                    request.Content = new StringContent(
                        requestBody is string str ? str : JsonSerializer.Serialize(requestBody),
                        Encoding.UTF8,
                        "application/json");
                }

                // Invoke the service via direct HTTP (preserves full path)
                httpResponse = await _httpClient.SendAsync(request, cancellationToken);

                // Read response content
                responseJson = await httpResponse.Content.ReadAsStringAsync(cancellationToken);

                // Log response details - use Info level for non-success to help debug routing issues
                if (httpResponse.IsSuccessStatusCode)
                {
                    _logger.LogDebug("Service {Service} responded with status {Status}: {ResponsePreview}",
                        serviceName, httpResponse.StatusCode,
                        responseJson?.Substring(0, Math.Min(200, responseJson?.Length ?? 0)) ?? "(empty)");
                }
                else
                {
                    _logger.LogWarning("Service {Service} returned non-success status {StatusCode}: {ResponsePreview}",
                        serviceName, (int)httpResponse.StatusCode,
                        responseJson?.Substring(0, Math.Min(500, responseJson?.Length ?? 0)) ?? "(empty body)");
                }
            }
            catch (HttpRequestException httpEx)
            {
                _logger.LogWarning(httpEx, "HTTP request to Dapr failed for {Service} {Method} {Path}",
                    serviceName, httpMethod, path);

                // Create error response
                var errorPayload = new
                {
                    error = "Service invocation failed",
                    statusCode = (int?)httpEx.StatusCode ?? 500,
                    message = httpEx.Message
                };
                responseJson = JsonSerializer.Serialize(errorPayload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error invoking service {Service} {Method} {Path}",
                    serviceName, httpMethod, path);

                var errorPayload = new
                {
                    error = "Internal server error",
                    message = ex.Message
                };
                responseJson = JsonSerializer.Serialize(errorPayload);
            }

            // Send response back to WebSocket client
            if (routeInfo.RequiresResponse)
            {
                var responseCode = httpResponse?.IsSuccessStatusCode == true
                    ? ResponseCodes.OK
                    : ResponseCodes.Service_InternalServerError;

                var responseMessage = BinaryMessage.CreateResponse(
                    message, responseCode, Encoding.UTF8.GetBytes(responseJson ?? "{}"));

                await _connectionManager.SendMessageAsync(sessionId, responseMessage, cancellationToken);
            }

            // Remove from pending messages
            connectionState.RemovePendingMessage(message.MessageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error routing message to service {Service}", routeInfo.ServiceName);

            if (routeInfo.RequiresResponse)
            {
                var errorResponse = MessageRouter.CreateErrorResponse(
                    message, ResponseCodes.Service_InternalServerError, "Service routing failed");

                await _connectionManager.SendMessageAsync(sessionId, errorResponse, cancellationToken);
                connectionState.RemovePendingMessage(message.MessageId);
            }
        }
    }

    /// <summary>
    /// Routes a message to another WebSocket client (client-to-client communication).
    /// </summary>
    private async Task RouteToClientAsync(
        BinaryMessage message,
        MessageRouteInfo routeInfo,
        string sessionId,
        CancellationToken cancellationToken)
    {
        try
        {
            var targetSessionId = routeInfo.TargetId;
            if (string.IsNullOrEmpty(targetSessionId))
            {
                var errorResponse = MessageRouter.CreateErrorResponse(
                    message, ResponseCodes.ClientNotFound, "Target client ID not specified");

                await _connectionManager.SendMessageAsync(sessionId, errorResponse, cancellationToken);
                return;
            }

            // Try to send message to target client
            var sent = await _connectionManager.SendMessageAsync(targetSessionId, message, cancellationToken);

            if (!sent)
            {
                var errorResponse = MessageRouter.CreateErrorResponse(
                    message, ResponseCodes.ClientNotFound, $"Target client {targetSessionId} not connected");

                await _connectionManager.SendMessageAsync(sessionId, errorResponse, cancellationToken);
            }
            else if (message.ExpectsResponse)
            {
                // For client-to-client, we send an acknowledgment that the message was delivered
                var ackPayload = new
                {
                    status = "delivered",
                    targetClient = targetSessionId,
                    originalMessageId = message.MessageId
                };

                var ackMessage = BinaryMessage.CreateResponse(
                    message, ResponseCodes.OK, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(ackPayload)));

                await _connectionManager.SendMessageAsync(sessionId, ackMessage, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error routing message to client {TargetClient}", routeInfo.TargetId);

            if (message.ExpectsResponse)
            {
                var errorResponse = MessageRouter.CreateErrorResponse(
                    message, ResponseCodes.RequestError, "Client routing failed");

                await _connectionManager.SendMessageAsync(sessionId, errorResponse, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Handles legacy text messages by wrapping them in binary protocol format.
    /// </summary>
    private async Task HandleLegacyTextMessageAsync(
        string sessionId,
        ConnectionState connectionState,
        string textMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Received legacy text message from session {SessionId}: {Message}",
                sessionId, textMessage);

            // For now, just echo back the message wrapped in a binary response
            var echo = $"Echo: {textMessage}";
            var messageId = MessageRouter.GenerateMessageId();
            var channel = connectionState.GetNextSequenceNumber(0);

            var binaryMessage = BinaryMessage.FromJson(
                0, // Default channel
                channel,
                Guid.Empty, // System message
                messageId,
                echo);

            await _connectionManager.SendMessageAsync(sessionId, binaryMessage, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling legacy text message from session {SessionId}", sessionId);
        }
    }

    #region Service Lifecycle

    /// <summary>
    /// Service startup method - registers RabbitMQ event handler endpoints.
    /// </summary>
    public Task OnStartAsync(WebApplication webApp, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Registering Connect service RabbitMQ event handlers");

        // Register session capability update handler
        webApp.MapPost("/events/session-capabilities", ProcessSessionCapabilityUpdateAsync)
            .WithTopic("bannou-pubsub", "bannou-session-capabilities")
            .WithMetadata("Connect service capability update handler");

        // Register auth event handler
        webApp.MapPost("/events/auth-events", ProcessAuthEventAsync)
            .WithTopic("bannou-pubsub", "bannou-auth-events")
            .WithMetadata("Connect service auth event handler");

        // Register service registration handler
        webApp.MapPost("/events/service-registered", ProcessServiceRegistrationAsync)
            .WithTopic("bannou-pubsub", "bannou-service-registered")
            .WithMetadata("Connect service registration handler");

        // Register client message handler
        webApp.MapPost("/events/client-messages", ProcessClientMessageEventAsync)
            .WithTopic("bannou-pubsub", "bannou-client-messages")
            .WithMetadata("Connect service client message handler");

        // Register client RPC handler
        webApp.MapPost("/events/client-rpc", ProcessClientRPCEventAsync)
            .WithTopic("bannou-pubsub", "bannou-client-rpc")
            .WithMetadata("Connect service client RPC handler");

        _logger.LogInformation("Connect service RabbitMQ event handlers registered successfully");
        return Task.CompletedTask;
    }

    #endregion

    #region RabbitMQ Event Processing Methods

    /// <summary>
    /// Processes session capability updates from Permission service.
    /// Triggers real-time capability updates for connected WebSocket clients.
    /// </summary>
    internal async Task<object> ProcessSessionCapabilityUpdateAsync(SessionCapabilityUpdateEvent eventData)
    {
        try
        {
            _logger.LogInformation("Processing capability update for session {SessionId}: Added={Added}, Removed={Removed}",
                eventData.SessionId, eventData.AddedCapabilities?.Count ?? 0, eventData.RemovedCapabilities?.Count ?? 0);

            // Check if the session has an active WebSocket connection
            if (HasConnection(eventData.SessionId))
            {
                // Send capability update directly without API discovery
                // The client can request fresh capabilities from the Permissions service if needed
                var updatePayload = new
                {
                    type = "capability_update",
                    sessionId = eventData.SessionId,
                    version = eventData.Version,
                    addedCapabilities = eventData.AddedCapabilities,
                    removedCapabilities = eventData.RemovedCapabilities,
                    updatedAt = DateTimeOffset.UtcNow,
                    message = "Capabilities updated. Use Permissions service to get fresh capability list."
                };

                // Send real-time update via WebSocket
                var messageId = MessageRouter.GenerateMessageId();
                var updateMessage = BinaryMessage.FromJson(
                    channel: 4, // Permissions channel
                    sequenceNumber: 0, // Event message (no sequence)
                    serviceGuid: Guid.Empty, // System message
                    messageId: messageId,
                    JsonSerializer.Serialize(updatePayload)
                );

                var sent = await SendMessageAsync(eventData.SessionId, updateMessage, CancellationToken.None);

                if (sent)
                {
                    _logger.LogInformation("Sent capability update to session {SessionId}", eventData.SessionId);
                }
                else
                {
                    _logger.LogWarning("Failed to send capability update to session {SessionId} - connection not found", eventData.SessionId);
                }
            }
            else
            {
                _logger.LogDebug("Session {SessionId} not connected to this instance, skipping capability update", eventData.SessionId);
            }

            return new { status = "processed", sessionId = eventData.SessionId };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process capability update for session {SessionId}", eventData.SessionId);
            throw;
        }
    }

    /// <summary>
    /// Processes authentication events (login/logout) from Auth service.
    /// Updates session capabilities when authentication state changes.
    /// </summary>
    internal async Task<object> ProcessAuthEventAsync(AuthEvent eventData)
    {
        try
        {
            _logger.LogInformation("Processing auth event {EventType} for session {SessionId}",
                eventData.EventType, eventData.SessionId);

            // Check if the session has an active WebSocket connection
            if (HasConnection(eventData.SessionId))
            {
                if (eventData.EventType == AuthEventType.Login)
                {
                    // User logged in - regenerate capabilities with authenticated permissions
                    await RefreshSessionCapabilitiesAsync(eventData.SessionId, "authenticated");
                }
                else if (eventData.EventType == AuthEventType.Logout)
                {
                    // User logged out - downgrade to anonymous permissions
                    await RefreshSessionCapabilitiesAsync(eventData.SessionId, "anonymous");

                    // Optionally close the WebSocket connection on logout
                    // await DisconnectAsync(eventData.SessionId, "User logged out");
                }
                else if (eventData.EventType == AuthEventType.TokenRefresh)
                {
                    // Token refreshed - validate session still exists
                    var sessionValid = await ValidateSessionAsync(eventData.SessionId);
                    if (!sessionValid)
                    {
                        _logger.LogWarning("Session {SessionId} invalid after token refresh, disconnecting", eventData.SessionId);
                        await DisconnectAsync(eventData.SessionId, "Session invalid");
                    }
                }
            }
            else
            {
                _logger.LogDebug("Session {SessionId} not connected to this instance, skipping auth event", eventData.SessionId);
            }

            return new { status = "processed", sessionId = eventData.SessionId, eventType = eventData.EventType };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process auth event for session {SessionId}", eventData.SessionId);
            throw;
        }
    }

    /// <summary>
    /// Processes service registration events for permission recompilation.
    /// When services register new APIs, update permission cache and notify clients.
    /// </summary>
    internal async Task<object> ProcessServiceRegistrationAsync(ServiceRegistrationEvent eventData)
    {
        try
        {
            _logger.LogInformation("Processing service registration for {ServiceId}", eventData.ServiceId);

            // Notify permission service that a new service was registered
            // This will trigger permission recompilation for all sessions
            await _daprClient.PublishEventAsync(
                "bannou-pubsub",
                "bannou-permission-recompile",
                new PermissionRecompileEvent
                {
                    EventId = Guid.NewGuid().ToString(),
                    Timestamp = DateTimeOffset.UtcNow,
                    Reason = PermissionRecompileEventReason.Service_registered,
                    ServiceId = eventData.ServiceId,
                    Metadata = new Dictionary<string, object>
                    {
                        { "triggeredBy", "connect-service" },
                        { "instanceId", _instanceId }
                    }
                });

            _logger.LogInformation("Triggered permission recompilation for service registration: {ServiceId}", eventData.ServiceId);

            return new { status = "processed", serviceId = eventData.ServiceId };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process service registration event for {ServiceId}", eventData.ServiceId);
            throw;
        }
    }

    /// <summary>
    /// Processes client message events for server-to-client push messaging.
    /// Allows services to send messages directly to connected WebSocket clients.
    /// </summary>
    internal async Task<object> ProcessClientMessageEventAsync(ClientMessageEvent eventData)
    {
        try
        {
            _logger.LogDebug("Received client message event for {ClientId} from service {ServiceName}",
                eventData.ClientId, eventData.ServiceName);

            // Find target client connection
            if (HasConnection(eventData.ClientId))
            {
                // Create binary message from event data
                var message = BinaryMessage.FromBinary(
                    channel: (ushort)eventData.Channel,
                    sequenceNumber: 0, // Server-initiated event
                    serviceGuid: eventData.ServiceGuid,
                    messageId: (ulong)eventData.MessageId,
                    eventData.Payload,
                    (MessageFlags)eventData.Flags | MessageFlags.Event // Mark as server event
                );

                // Send to client
                var sent = await SendMessageAsync(eventData.ClientId, message, CancellationToken.None);

                if (sent)
                {
                    _logger.LogDebug("Forwarded event message to client {ClientId}", eventData.ClientId);
                    return new { status = "delivered", clientId = eventData.ClientId };
                }
                else
                {
                    _logger.LogWarning("Failed to deliver message to client {ClientId} - connection issue", eventData.ClientId);
                    return new { error = "delivery_failed", clientId = eventData.ClientId };
                }
            }
            else
            {
                _logger.LogDebug("Target client {ClientId} not connected to this instance", eventData.ClientId);
                return new { error = "client_not_found", clientId = eventData.ClientId };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process client message event for {ClientId}", eventData.ClientId);
            throw;
        }
    }

    /// <summary>
    /// Processes bidirectional RPC events where services call clients and expect responses.
    /// </summary>
    internal async Task<object> ProcessClientRPCEventAsync(ClientRPCEvent eventData)
    {
        try
        {
            _logger.LogDebug("Received client RPC event for {ClientId} from service {ServiceName}",
                eventData.ClientId, eventData.ServiceName);

            // Find target client connection
            if (HasConnection(eventData.ClientId))
            {
                // Create binary message for RPC call
                var message = BinaryMessage.FromBinary(
                    channel: (ushort)eventData.Channel,
                    sequenceNumber: 0, // Server-initiated RPC
                    serviceGuid: eventData.ServiceGuid,
                    messageId: (ulong)eventData.MessageId,
                    eventData.Payload,
                    MessageFlags.None // Regular RPC - expects response
                );

                // Send to client
                var sent = await SendMessageAsync(eventData.ClientId, message, CancellationToken.None);

                if (sent)
                {
                    _logger.LogDebug("Sent RPC message to client {ClientId}, waiting for response", eventData.ClientId);

                    // TODO: Implement response timeout and forwarding back to service
                    // The client response will come back through the normal WebSocket message handling
                    // and should be routed back to the originating service via RabbitMQ

                    return new { status = "sent", clientId = eventData.ClientId, messageId = eventData.MessageId };
                }
                else
                {
                    _logger.LogWarning("Failed to send RPC to client {ClientId} - connection issue", eventData.ClientId);
                    return new { error = "delivery_failed", clientId = eventData.ClientId };
                }
            }
            else
            {
                _logger.LogDebug("Target client {ClientId} not connected to this instance", eventData.ClientId);
                return new { error = "client_not_found", clientId = eventData.ClientId };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process client RPC event for {ClientId}", eventData.ClientId);
            throw;
        }
    }

    #endregion

    #region Helper Methods for Event Handling

    /// <summary>
    /// Checks if a session has an active WebSocket connection.
    /// </summary>
    internal bool HasConnection(string sessionId)
    {
        try
        {
            return _connectionManager.GetConnection(sessionId) != null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking connection for session {SessionId}", sessionId);
            return false;
        }
    }

    /// <summary>
    /// Sends a binary message to a specific session's WebSocket connection.
    /// </summary>
    internal async Task<bool> SendMessageAsync(string sessionId, BinaryMessage message, CancellationToken cancellationToken)
    {
        try
        {
            return await _connectionManager.SendMessageAsync(sessionId, message, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message to session {SessionId}", sessionId);
            return false;
        }
    }

    /// <summary>
    /// Disconnects a session's WebSocket connection with a reason.
    /// </summary>
    internal async Task DisconnectAsync(string sessionId, string reason)
    {
        try
        {
            var connection = _connectionManager.GetConnection(sessionId);
            if (connection?.WebSocket != null && connection.WebSocket.State == System.Net.WebSockets.WebSocketState.Open)
            {
                await connection.WebSocket.CloseAsync(
                    System.Net.WebSockets.WebSocketCloseStatus.NormalClosure,
                    reason,
                    CancellationToken.None);
            }
            _connectionManager.RemoveConnection(sessionId);
            _logger.LogInformation("Disconnected session {SessionId}: {Reason}", sessionId, reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disconnecting session {SessionId}: {Reason}", sessionId, reason);
        }
    }

    /// <summary>
    /// Sends auth state change notification to client.
    /// Client should request fresh capabilities from Permissions service.
    /// </summary>
    private async Task RefreshSessionCapabilitiesAsync(string sessionId, string newState)
    {
        try
        {
            var updatePayload = new
            {
                type = "auth_state_change",
                sessionId = sessionId,
                newState = newState,
                updatedAt = DateTimeOffset.UtcNow,
                message = "Authentication state changed. Request fresh capabilities from Permissions service."
            };

            var messageId = MessageRouter.GenerateMessageId();
            var updateMessage = BinaryMessage.FromJson(
                channel: 1, // Auth channel
                sequenceNumber: 0, // Event message
                serviceGuid: Guid.Empty, // System message
                messageId: messageId,
                JsonSerializer.Serialize(updatePayload)
            );

            await _connectionManager.SendMessageAsync(sessionId, updateMessage, CancellationToken.None);
            _logger.LogInformation("Sent auth state change update to session {SessionId}: {NewState}", sessionId, newState);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send auth state change for session {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// Validates that a session still exists and is valid by checking the connection manager.
    /// </summary>
    private Task<bool> ValidateSessionAsync(string sessionId)
    {
        try
        {
            // Check if session exists in our connection manager
            // This is used for token refresh events where we only have session ID
            var connection = _connectionManager.GetConnection(sessionId);
            return Task.FromResult(connection != null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Session validation failed for {SessionId}", sessionId);
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Initializes session capabilities by querying the Permissions service.
    /// Generates client-salted GUIDs for each available API endpoint.
    /// </summary>
    internal async Task<Dictionary<string, Guid>> InitializeSessionCapabilitiesAsync(
        string sessionId,
        string? role = "anonymous",
        CancellationToken cancellationToken = default)
    {
        var serviceMappings = new Dictionary<string, Guid>();

        try
        {
            _logger.LogInformation("Initializing capabilities for session {SessionId} with role {Role}", sessionId, role);

            // Initialize session in Permissions service with role
            if (role != "anonymous")
            {
                await _permissionsClient.UpdateSessionRoleAsync(new SessionRoleUpdate
                {
                    SessionId = sessionId,
                    NewRole = role
                }, cancellationToken);
            }

            // Get available capabilities from Permissions service
            var capabilityResponse = await _permissionsClient.GetCapabilitiesAsync(
                new CapabilityRequest { SessionId = sessionId },
                cancellationToken);

            if (capabilityResponse?.Permissions == null)
            {
                _logger.LogWarning("No capabilities returned for session {SessionId}", sessionId);
                return serviceMappings;
            }

            _logger.LogInformation("Session {SessionId} has access to {ServiceCount} services",
                sessionId, capabilityResponse.Permissions.Count);

            // Generate client-salted GUIDs for each service:method combination
            foreach (var servicePermissions in capabilityResponse.Permissions)
            {
                var serviceName = servicePermissions.Key;
                var methods = servicePermissions.Value;

                foreach (var method in methods)
                {
                    // Create unique key combining service and method
                    var endpointKey = $"{serviceName}:{method}";

                    // Generate client-salted GUID using session ID as salt
                    var guid = GuidGenerator.GenerateServiceGuid(endpointKey, sessionId, _serverSalt);

                    serviceMappings[endpointKey] = guid;

                    _logger.LogDebug("Generated GUID {Guid} for endpoint {Endpoint} in session {SessionId}",
                        guid, endpointKey, sessionId);
                }
            }

            // Store mappings in session manager for persistence
            if (_sessionManager != null)
            {
                await _sessionManager.SetSessionServiceMappingsAsync(sessionId, serviceMappings);
            }

            // Store in in-memory cache as fallback
            _sessionServiceMappings[sessionId] = serviceMappings;

            _logger.LogInformation("Initialized {Count} service GUIDs for session {SessionId}",
                serviceMappings.Count, sessionId);

            return serviceMappings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize capabilities for session {SessionId}", sessionId);
            return serviceMappings;
        }
    }

    /// <summary>
    /// Sends the capability manifest to the client after WebSocket connection is established.
    /// This tells the client what APIs they can access and provides their client-salted GUIDs.
    /// </summary>
    private async Task SendCapabilityManifestAsync(
        WebSocket webSocket,
        string sessionId,
        ConnectionState connectionState,
        CancellationToken cancellationToken)
    {
        try
        {
            // Build the capability manifest with available APIs and their GUIDs
            var availableApis = new List<object>();

            foreach (var mapping in connectionState.ServiceMappings)
            {
                // Parse the endpoint key format: "serviceName:METHOD:/path"
                var endpointKey = mapping.Key;
                var guid = mapping.Value;

                // Split into service name and method:path
                var firstColon = endpointKey.IndexOf(':');
                if (firstColon <= 0) continue;

                var serviceName = endpointKey[..firstColon];
                var methodAndPath = endpointKey[(firstColon + 1)..];

                // Split method and path (format: "GET:/some/path")
                var methodPathColon = methodAndPath.IndexOf(':');
                var method = methodPathColon > 0 ? methodAndPath[..methodPathColon] : methodAndPath;
                var path = methodPathColon > 0 ? methodAndPath[(methodPathColon + 1)..] : "";

                availableApis.Add(new
                {
                    serviceGuid = guid.ToString(),
                    serviceName = serviceName,
                    method = method,
                    path = path,
                    endpointKey = endpointKey
                });
            }

            var capabilityManifest = new
            {
                type = "capability_manifest",
                sessionId = sessionId,
                availableAPIs = availableApis,
                version = 1,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            var manifestJson = JsonSerializer.Serialize(capabilityManifest);
            var manifestBytes = Encoding.UTF8.GetBytes(manifestJson);

            // Create a binary message as an Event (no response expected)
            var capabilityMessage = new BinaryMessage(
                flags: MessageFlags.Event, // Server-initiated event, no response expected
                channel: 0, // Control channel for system messages
                sequenceNumber: 0,
                serviceGuid: Guid.Empty, // System message
                messageId: GuidGenerator.GenerateMessageId(),
                payload: manifestBytes
            );

            var messageBytes = capabilityMessage.ToByteArray();

            _logger.LogInformation(
                "Sending capability manifest to session {SessionId} with {ApiCount} available APIs",
                sessionId, availableApis.Count);

            await webSocket.SendAsync(
                new ArraySegment<byte>(messageBytes),
                WebSocketMessageType.Binary,
                endOfMessage: true,
                cancellationToken);

            _logger.LogDebug("Capability manifest sent successfully to session {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send capability manifest to session {SessionId}", sessionId);
            // Don't throw - capability manifest is informational, connection can continue
        }
    }

    /// <summary>
    /// Pushes updated capability manifest to a connected WebSocket client.
    /// Called when permissions change (e.g., new service registered, role change, state change).
    /// </summary>
    public async Task PushCapabilityUpdateAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var connection = _connectionManager.GetConnection(sessionId);
            if (connection == null)
            {
                _logger.LogDebug("No active connection found for session {SessionId}, skipping capability update", sessionId);
                return;
            }

            // Re-fetch capabilities from permissions service
            var capabilityRequest = new Permissions.CapabilityRequest { SessionId = sessionId };
            var capabilitiesResult = await _permissionsClient.GetCapabilitiesAsync(capabilityRequest, cancellationToken);
            if (capabilitiesResult == null || capabilitiesResult.Permissions == null)
            {
                _logger.LogWarning("Failed to get capabilities for session {SessionId}", sessionId);
                return;
            }

            // Regenerate client-salted GUIDs and update connection state
            var connectionState = connection.ConnectionState;
            connectionState.ClearServiceMappings();

            // Generate client-salted GUIDs for each service:method combination
            foreach (var servicePermissions in capabilitiesResult.Permissions)
            {
                var serviceName = servicePermissions.Key;
                var methods = servicePermissions.Value;

                foreach (var method in methods)
                {
                    var endpointKey = $"{serviceName}:{method}";
                    var guid = GuidGenerator.GenerateServiceGuid(endpointKey, sessionId, _serverSalt);
                    connectionState.AddServiceMapping(endpointKey, guid);
                }
            }

            // Build and send updated capability manifest
            var availableApis = new List<object>();
            foreach (var mapping in connectionState.ServiceMappings)
            {
                var endpointKey = mapping.Key;
                var guid = mapping.Value;

                var firstColon = endpointKey.IndexOf(':');
                if (firstColon <= 0) continue;

                var serviceName = endpointKey[..firstColon];
                var methodAndPath = endpointKey[(firstColon + 1)..];
                var methodPathColon = methodAndPath.IndexOf(':');
                var method = methodPathColon > 0 ? methodAndPath[..methodPathColon] : methodAndPath;
                var path = methodPathColon > 0 ? methodAndPath[(methodPathColon + 1)..] : "";

                availableApis.Add(new
                {
                    serviceGuid = guid.ToString(),
                    serviceName = serviceName,
                    method = method,
                    path = path,
                    endpointKey = endpointKey
                });
            }

            var capabilityManifest = new
            {
                type = "capability_manifest",
                sessionId = sessionId,
                availableAPIs = availableApis,
                version = 1,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                reason = "capabilities_updated"
            };

            var manifestJson = JsonSerializer.Serialize(capabilityManifest);
            var manifestBytes = Encoding.UTF8.GetBytes(manifestJson);

            var capabilityMessage = new BinaryMessage(
                flags: MessageFlags.Event,
                channel: 0,
                sequenceNumber: 0,
                serviceGuid: Guid.Empty,
                messageId: GuidGenerator.GenerateMessageId(),
                payload: manifestBytes
            );

            await _connectionManager.SendMessageAsync(sessionId, capabilityMessage, cancellationToken);

            _logger.LogInformation(
                "Pushed capability update to session {SessionId} with {ApiCount} available APIs",
                sessionId, availableApis.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to push capability update to session {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// Pushes capability updates to all connected WebSocket clients.
    /// Called when a new service registers (no specific sessions affected).
    /// </summary>
    public async Task PushCapabilityUpdateToAllAsync(CancellationToken cancellationToken = default)
    {
        var sessionIds = _connectionManager.GetActiveSessionIds().ToList();
        _logger.LogInformation("Pushing capability updates to {Count} connected sessions", sessionIds.Count);

        var tasks = sessionIds.Select(sessionId => PushCapabilityUpdateAsync(sessionId, cancellationToken));
        await Task.WhenAll(tasks);
    }

    #endregion

}
