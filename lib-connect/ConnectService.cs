using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.Permissions;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using Dapr.Client;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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

    // Session to service GUID mappings
    private readonly ConcurrentDictionary<string, Dictionary<string, Guid>> _sessionServiceMappings;
    private readonly string _serverSalt;

    public ConnectService(
        IAuthClient authClient,
        IPermissionsClient permissionsClient,
        DaprClient daprClient,
        IServiceAppMappingResolver appMappingResolver,
        ConnectServiceConfiguration configuration,
        ILogger<ConnectService> logger)
        : base()
    {
        _authClient = authClient ?? throw new ArgumentNullException(nameof(authClient));
        _permissionsClient = permissionsClient ?? throw new ArgumentNullException(nameof(permissionsClient));
        _daprClient = daprClient ?? throw new ArgumentNullException(nameof(daprClient));
        _appMappingResolver = appMappingResolver ?? throw new ArgumentNullException(nameof(appMappingResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _sessionServiceMappings = new ConcurrentDictionary<string, Dictionary<string, Guid>>();

        // Generate server salt for GUID generation
        _serverSalt = Guid.NewGuid().ToString();
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
                var serviceGuid = GenerateServiceGuid(body.SessionId.ToString(), serviceName);
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
            _sessionServiceMappings[body.SessionId.ToString()] = serviceMappings;

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
    /// Generate a client-salted GUID for a service.
    /// Uses SHA256 for better security than MD5.
    /// </summary>
    private Guid GenerateServiceGuid(string sessionId, string serviceName)
    {
        var input = $"{serviceName}:{sessionId}:{_serverSalt}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));

        // Use first 16 bytes of hash as GUID
        var guidBytes = new byte[16];
        Array.Copy(hash, guidBytes, 16);

        return new Guid(guidBytes);
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
    /// Handles WebSocket communication using the 31-byte binary protocol.
    /// Protocol: [MessageFlags:1][Channel:2][Sequence:4][ServiceGUID:16][MessageID:8][JSONPayload:variable]
    /// </summary>
    public async Task HandleWebSocketCommunicationAsync(
        WebSocket webSocket,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[4096]; // Buffer for receiving messages

        try
        {
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

                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    await HandleBinaryMessageAsync(webSocket, sessionId, buffer, result.Count, cancellationToken);
                }
                else if (result.MessageType == WebSocketMessageType.Text)
                {
                    // For backwards compatibility, also handle text messages
                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    _logger.LogDebug("Received text message from session {SessionId}: {Message}",
                        sessionId, message);

                    // Echo back for now (implement JSON-RPC later)
                    var echo = Encoding.UTF8.GetBytes($"Echo: {message}");
                    await webSocket.SendAsync(new ArraySegment<byte>(echo),
                        WebSocketMessageType.Text, true, cancellationToken);
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
            if (webSocket.State == WebSocketState.Open)
            {
                try
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.InternalServerError,
                        "Server error", CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error closing WebSocket for session {SessionId}", sessionId);
                }
            }
        }
    }

    /// <summary>
    /// Handles binary messages using the 31-byte header protocol.
    /// </summary>
    private async Task HandleBinaryMessageAsync(
        WebSocket webSocket,
        string sessionId,
        byte[] buffer,
        int messageLength,
        CancellationToken cancellationToken)
    {
        try
        {
            // Validate minimum message length (31-byte header + at least some JSON)
            if (messageLength < 32)
            {
                _logger.LogWarning("Received binary message too short ({Length} bytes) from session {SessionId}",
                    messageLength, sessionId);
                return;
            }

            // Parse 31-byte header
            var messageFlags = buffer[0];                           // Byte 0: Message flags
            var channel = BitConverter.ToUInt16(buffer, 1);         // Bytes 1-2: Channel
            var sequence = BitConverter.ToUInt32(buffer, 3);        // Bytes 3-6: Sequence number
            var serviceGuidBytes = new byte[16];                    // Bytes 7-22: Service GUID
            Array.Copy(buffer, 7, serviceGuidBytes, 0, 16);
            var serviceGuid = new Guid(serviceGuidBytes);
            var messageId = BitConverter.ToUInt64(buffer, 23);      // Bytes 23-30: Message ID

            // Extract JSON payload (remaining bytes after 31-byte header)
            var jsonLength = messageLength - 31;
            var jsonPayload = Encoding.UTF8.GetString(buffer, 31, jsonLength);

            _logger.LogDebug("Binary message from session {SessionId}: Flags={Flags}, Channel={Channel}, " +
                           "Sequence={Sequence}, ServiceGUID={ServiceGuid}, MessageID={MessageId}, PayloadLength={Length}",
                           sessionId, messageFlags, channel, sequence, serviceGuid, messageId, jsonLength);

            // TODO: Implement message routing logic
            // 1. Look up service name from serviceGuid using session mappings
            // 2. Route JSON payload to appropriate service via Dapr
            // 3. Handle response and send back with same messageId

            // For now, send a simple acknowledgment
            await SendBinaryAckAsync(webSocket, messageId, sequence, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling binary message from session {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// Sends a binary acknowledgment message.
    /// </summary>
    private async Task SendBinaryAckAsync(
        WebSocket webSocket,
        ulong originalMessageId,
        uint originalSequence,
        CancellationToken cancellationToken)
    {
        try
        {
            // Create acknowledgment payload
            var ackPayload = new { status = "received", messageId = originalMessageId };
            var jsonPayload = System.Text.Json.JsonSerializer.Serialize(ackPayload);
            var jsonBytes = Encoding.UTF8.GetBytes(jsonPayload);

            // Create 31-byte header for acknowledgment
            var header = new byte[31];
            header[0] = 0x01;                                      // Flags: ACK message
            BitConverter.GetBytes((ushort)0).CopyTo(header, 1);    // Channel 0
            BitConverter.GetBytes(originalSequence).CopyTo(header, 3); // Same sequence
            // Service GUID: all zeros for system messages
            BitConverter.GetBytes(originalMessageId).CopyTo(header, 23); // Same message ID

            // Combine header and payload
            var response = new byte[31 + jsonBytes.Length];
            Array.Copy(header, 0, response, 0, 31);
            Array.Copy(jsonBytes, 0, response, 31, jsonBytes.Length);

            await webSocket.SendAsync(new ArraySegment<byte>(response),
                WebSocketMessageType.Binary, true, cancellationToken);

            _logger.LogDebug("Sent binary ACK for message {MessageId}", originalMessageId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send binary acknowledgment for message {MessageId}", originalMessageId);
        }
    }

    /// <summary>
    /// Common WebSocket connection handling logic.
    /// </summary>
    private async Task<IActionResult> HandleWebSocketConnectionAsync(
        string authorization,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Validate WebSocket upgrade request
            if (!HttpContext.WebSockets.IsWebSocketRequest)
            {
                return BadRequest("This endpoint only accepts WebSocket connections");
            }

            // Validate and parse JWT token
            var sessionId = await ValidateJWTAndExtractSessionAsync(authorization, cancellationToken);
            if (sessionId == null)
            {
                return Unauthorized("Invalid or expired JWT token");
            }

            _logger.LogInformation("WebSocket connection request from session {SessionId}", sessionId);

            // Accept WebSocket connection
            var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
            _logger.LogInformation("WebSocket connection established for session {SessionId}", sessionId);

            // Handle WebSocket communication with binary protocol
            await HandleWebSocketCommunicationAsync(webSocket, sessionId, cancellationToken);

            return Ok();
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("WebSocket connection cancelled");
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling WebSocket connection");
            return StatusCode(500, "WebSocket connection failed");
        }
    }
}
