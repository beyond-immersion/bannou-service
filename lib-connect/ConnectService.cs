using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.Permissions;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Connect.Protocol;
using Dapr;
using Dapr.Client;
using StackExchange.Redis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("lib-connect.tests")]

namespace BeyondImmersion.BannouService.Connect;

/// <summary>
/// WebSocket-first edge gateway service providing zero-copy message routing.
/// Uses Permissions service for dynamic API discovery and capability management.
/// </summary>
[DaprService("connect", typeof(IConnectService), lifetime: ServiceLifetime.Singleton)]
public class ConnectService : DaprService<ConnectServiceConfiguration>, IConnectService
{
    private readonly IAuthClient _authClient;
    private readonly IPermissionsClient _permissionsClient;
    private readonly DaprClient _daprClient;
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
        : base()
    {
        _authClient = authClient ?? throw new ArgumentNullException(nameof(authClient));
        _permissionsClient = permissionsClient ?? throw new ArgumentNullException(nameof(permissionsClient));
        _daprClient = daprClient ?? throw new ArgumentNullException(nameof(daprClient));
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

    /// <summary>
    /// Get available APIs for current session using Permissions service.
    /// Returns client-salted GUIDs for security isolation.
    /// </summary>
    public async Task<(StatusCodes, ApiDiscoveryResponse?)> DiscoverAPIsAsync(
        ApiDiscoveryRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Processing API discovery request for session {SessionId}",
                body.SessionId);

            // Get capabilities from Permissions service
            var capabilityRequest = new CapabilityRequest
            {
                SessionId = body.SessionId,
                ServiceIds = new List<string>() // Get all available services
            };

            CapabilityResponse capabilities;
            try
            {
                capabilities = await _permissionsClient.GetCapabilitiesAsync(
                    capabilityRequest, cancellationToken);
            }
            catch (ApiException apiEx)
            {
                _logger.LogWarning("Failed to get capabilities for session {SessionId}: {Error}",
                    body.SessionId, apiEx.Message);

                if (apiEx.StatusCode == 404)
                {
                    return (StatusCodes.NotFound, null);
                }

                return (StatusCodes.InternalServerError, null);
            }

            // Build available APIs with client-salted GUIDs
            var availableApis = new List<ApiEndpointInfo>();
            var serviceCapabilities = new Dictionary<string, ICollection<string>>();
            var serviceMappings = new Dictionary<string, Guid>();

            foreach (var servicePermission in capabilities.Permissions)
            {
                var serviceName = servicePermission.Key;
                var methods = servicePermission.Value;

                // Generate client-salted GUID for this service
                var serviceGuid = GuidGenerator.GenerateServiceGuid(
                    body.SessionId.ToString(), serviceName, _serverSalt);
                serviceMappings[serviceName] = serviceGuid;

                // Create API endpoint info for each method
                foreach (var method in methods)
                {
                    // Parse method format (e.g., "GET:/accounts/{id}")
                    var parts = method.Split(':', 2);
                    if (parts.Length != 2) continue;

                    var httpMethod = ParseMethod(parts[0]);
                    var endpoint = parts[1];

                    availableApis.Add(new ApiEndpointInfo
                    {
                        ServiceGuid = serviceGuid,
                        ServiceName = serviceName,
                        Endpoint = endpoint,
                        Method = httpMethod,
                        Description = $"{httpMethod} {endpoint} on {serviceName}",
                        RequiredPermissions = new List<string> { $"{serviceName}:{parts[0].ToLower()}" },
                        Category = GetServiceCategory(serviceName),
                        Channel = GetPreferredChannel(serviceName, endpoint)
                    });
                }

                // Add to service capabilities map
                if (!serviceCapabilities.ContainsKey(serviceName))
                {
                    serviceCapabilities[serviceName] = new List<string>();
                }
                ((List<string>)serviceCapabilities[serviceName]).AddRange(methods);
            }

            // Store service mappings for this session (for reverse lookup during routing)
            if (_sessionManager != null)
            {
                // Use Redis for distributed session management
                await _sessionManager.SetSessionServiceMappingsAsync(body.SessionId.ToString(), serviceMappings);
            }
            else
            {
                // Fallback to in-memory storage
                _sessionServiceMappings[body.SessionId.ToString()] = serviceMappings;
            }

            var response = new ApiDiscoveryResponse
            {
                SessionId = body.SessionId,
                AvailableAPIs = availableApis,
                ServiceCapabilities = serviceCapabilities,
                Version = 1, // Default version for now
                GeneratedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(60) // Default 60 minute expiry
            };

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing API discovery request");
            return (StatusCodes.InternalServerError, null);
        }
    }


    /// <summary>
    /// Parse HTTP method string to enum.
    /// </summary>
    private ApiEndpointInfoMethod ParseMethod(string method)
    {
        return method.ToUpperInvariant() switch
        {
            "GET" => ApiEndpointInfoMethod.GET,
            "POST" => ApiEndpointInfoMethod.POST,
            "PUT" => ApiEndpointInfoMethod.PUT,
            "DELETE" => ApiEndpointInfoMethod.DELETE,
            "PATCH" => ApiEndpointInfoMethod.PATCH,
            _ => ApiEndpointInfoMethod.GET
        };
    }

    /// <summary>
    /// Get service category for grouping in discovery response.
    /// </summary>
    private string GetServiceCategory(string serviceName)
    {
        return serviceName.ToLowerInvariant() switch
        {
            "auth" => "authentication",
            "accounts" => "accounts",
            "behavior" => "game",
            "gamesession" => "game",
            "permissions" => "system",
            "website" => "public",
            "connect" => "infrastructure",
            _ => "general"
        };
    }

    /// <summary>
    /// Get preferred channel for message ordering.
    /// Higher channels guarantee sequential processing.
    /// </summary>
    private int GetPreferredChannel(string serviceName, string endpoint)
    {
        // Assign channels based on service criticality and message ordering needs
        // Channel 0 = default/unordered
        // Higher channels = sequential processing guaranteed

        if (serviceName == "auth" || endpoint.Contains("/login") || endpoint.Contains("/logout"))
            return 1; // Auth operations on channel 1

        if (serviceName == "gamesession" || endpoint.Contains("/join") || endpoint.Contains("/leave"))
            return 2; // Game session operations on channel 2

        if (serviceName == "behavior")
            return 3; // Behavior operations on channel 3

        if (serviceName == "permissions")
            return 4; // Permission operations on channel 4

        return 0; // Default channel for everything else
    }

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
            return Task.FromResult<(StatusCodes, ServiceMappingsResponse?)>((StatusCodes.OK, response));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving service mappings");
            return Task.FromResult<(StatusCodes, ServiceMappingsResponse?)>((StatusCodes.InternalServerError, null));
        }
    }

    /// <summary>
    /// Validates JWT token and extracts session ID.
    /// </summary>
    public async Task<string?> ValidateJWTAndExtractSessionAsync(
        string authorization,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrEmpty(authorization))
            {
                return null;
            }

            // Handle "Bearer <token>" format
            if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = authorization.Substring(7);

                // Use auth service to validate token (pass token via Authorization header)
                // The AuthClient should handle the Bearer token automatically through HttpContext
                var validationResponse = await _authClient.ValidateTokenAsync(cancellationToken);

                if (validationResponse.Valid && !string.IsNullOrEmpty(validationResponse.SessionId))
                {
                    return validationResponse.SessionId;
                }
            }
            // Handle "Reconnect <token>" format (future enhancement)
            else if (authorization.StartsWith("Reconnect ", StringComparison.OrdinalIgnoreCase))
            {
                // TODO: Implement reconnection logic with stored session tokens
                _logger.LogWarning("Reconnection tokens not yet implemented");
                return null;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "JWT validation failed");
            return null;
        }
    }

    /// <summary>
    /// Handles WebSocket communication using the enhanced 31-byte binary protocol.
    /// Creates persistent connection state and processes messages with proper routing.
    /// </summary>
    public async Task HandleWebSocketCommunicationAsync(
        WebSocket webSocket,
        string sessionId,
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
            // Add to pending messages for response correlation
            if (routeInfo.RequiresResponse)
            {
                connectionState.AddPendingMessage(message.MessageId, routeInfo.ServiceName!, DateTimeOffset.UtcNow);
            }

            // Get JSON payload for service call
            var jsonPayload = message.GetJsonPayload();

            // Use ServiceAppMappingResolver for dynamic app-id resolution
            var appId = _appMappingResolver.GetAppIdForService(routeInfo.ServiceName!);

            _logger.LogDebug("Routing WebSocket message to service {Service} via app-id {AppId}",
                routeInfo.ServiceName, appId);

            // TODO: Parse the JSON payload to determine the specific API endpoint
            // For now, this is a placeholder - we need to implement proper JSON-RPC or similar
            // to determine which endpoint and HTTP method to call

            // Create a simple service response for now
            if (routeInfo.RequiresResponse)
            {
                var responsePayload = new
                {
                    status = "success",
                    message = "Service routing not fully implemented yet",
                    originalMessageId = message.MessageId,
                    targetService = routeInfo.ServiceName
                };

                var responseJson = JsonSerializer.Serialize(responsePayload);
                var responseMessage = BinaryMessage.CreateResponse(
                    message, ResponseCodes.OK, Encoding.UTF8.GetBytes(responseJson));

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
    public async Task OnStartAsync(WebApplication webApp, CancellationToken cancellationToken)
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
                // Regenerate service discovery for the session
                var discoveryRequest = new ApiDiscoveryRequest { SessionId = eventData.SessionId };
                var (statusCode, discoveryResponse) = await DiscoverAPIsAsync(discoveryRequest, CancellationToken.None);

                if (statusCode == StatusCodes.OK && discoveryResponse != null)
                {
                    // Create capability update message
                    var updatePayload = new
                    {
                        type = "capability_update",
                        sessionId = eventData.SessionId,
                        version = eventData.Version,
                        addedCapabilities = eventData.AddedCapabilities,
                        removedCapabilities = eventData.RemovedCapabilities,
                        availableAPIs = discoveryResponse.AvailableAPIs,
                        updatedAt = DateTimeOffset.UtcNow
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
                    _logger.LogWarning("Failed to regenerate API discovery for session {SessionId}", eventData.SessionId);
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
    /// Refreshes session capabilities and sends real-time update to client.
    /// </summary>
    private async Task RefreshSessionCapabilitiesAsync(string sessionId, string newState)
    {
        try
        {
            var discoveryRequest = new ApiDiscoveryRequest { SessionId = sessionId };
            var (statusCode, discoveryResponse) = await DiscoverAPIsAsync(discoveryRequest, CancellationToken.None);

            if (statusCode == StatusCodes.OK && discoveryResponse != null)
            {
                var updatePayload = new
                {
                    type = "auth_state_change",
                    sessionId = sessionId,
                    newState = newState,
                    availableAPIs = discoveryResponse.AvailableAPIs,
                    serviceCapabilities = discoveryResponse.ServiceCapabilities,
                    updatedAt = DateTimeOffset.UtcNow
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh capabilities for session {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// Validates that a session still exists and is valid.
    /// </summary>
    private async Task<bool> ValidateSessionAsync(string sessionId)
    {
        try
        {
            // Use Auth service to validate the session
            var validationResponse = await _authClient.ValidateTokenAsync(CancellationToken.None);
            return validationResponse.Valid && validationResponse.SessionId == sessionId;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Session validation failed for {SessionId}", sessionId);
            return false;
        }
    }

    #endregion

}
