using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Connect.Protocol;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BeyondImmersion.BannouService.Connect;

/// <summary>
/// WebSocket-first edge gateway service providing zero-copy message routing.
/// Uses Permissions service for dynamic API discovery and capability management.
/// </summary>
[BannouService("connect", typeof(IConnectService), lifetime: ServiceLifetime.Singleton)]
public partial class ConnectService : IConnectService
{
    /// <summary>
    /// Named HttpClient for mesh proxying. Configured via IHttpClientFactory.
    /// </summary>
    internal const string HttpClientName = "ConnectMeshProxy";

    // Static cached header values to avoid per-request allocations
    private static readonly MediaTypeWithQualityHeaderValue s_jsonAcceptHeader = new("application/json");
    private static readonly MediaTypeHeaderValue s_jsonContentType = new("application/json") { CharSet = "utf-8" };

    private readonly IAuthClient _authClient;
    private readonly IMeshInvocationClient _meshClient;
    private readonly IMessageBus _messageBus;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceAppMappingResolver _appMappingResolver;
    private readonly ILogger<ConnectService> _logger;
    private readonly WebSocketConnectionManager _connectionManager;
    private readonly ISessionManager _sessionManager;

    // Client event subscriptions via lib-messaging (per-session raw byte subscriptions)
    private readonly IMessageSubscriber _messageSubscriber;
    private readonly ConcurrentDictionary<string, IAsyncDisposable> _sessionSubscriptions = new();
    private readonly ILoggerFactory _loggerFactory;

    // Pending RPCs awaiting client responses (MessageId -> RPC info for response forwarding)
    private readonly ConcurrentDictionary<ulong, PendingRPCInfo> _pendingRPCs = new();

    /// <summary>
    /// Prefix for session-specific queue routing keys.
    /// Must match the routing key used by MessageBusClientEventPublisher.
    /// </summary>
    private const string SESSION_TOPIC_PREFIX = "CONNECT_SESSION_";

    /// <summary>
    /// Dedicated direct exchange for client events.
    /// Defined in provisioning/rabbitmq/definitions.json.
    /// </summary>
    private const string CLIENT_EVENTS_EXCHANGE = "bannou-client-events";

    /// <summary>
    /// Reconnection window duration. During this time after disconnect:
    /// - Session state is preserved in Redis
    /// - Client event queue buffers messages
    /// - Client can reconnect with reconnection token
    /// </summary>
    private static readonly TimeSpan RECONNECTION_WINDOW = TimeSpan.FromMinutes(5);

    // Session to service GUID mappings (in-memory for low-latency lookups, persisted via ISessionManager)
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, Guid>> _sessionServiceMappings;
    private readonly string _serverSalt;
    private readonly string _instanceId;

    // Connection mode configuration
    private readonly string _connectionMode;
    private readonly string _internalAuthMode;
    private readonly string? _internalServiceToken;

    public ConnectService(
        IAuthClient authClient,
        IMeshInvocationClient meshClient,
        IMessageBus messageBus,
        IMessageSubscriber messageSubscriber,
        IHttpClientFactory httpClientFactory,
        IServiceAppMappingResolver appMappingResolver,
        ConnectServiceConfiguration configuration,
        ILogger<ConnectService> logger,
        ILoggerFactory loggerFactory,
        IEventConsumer eventConsumer,
        ISessionManager sessionManager)
    {
        _authClient = authClient;
        _meshClient = meshClient;
        _messageBus = messageBus;
        _messageSubscriber = messageSubscriber;
        _httpClientFactory = httpClientFactory;
        _appMappingResolver = appMappingResolver;
        _logger = logger;
        _loggerFactory = loggerFactory;
        _sessionManager = sessionManager;

        _sessionServiceMappings = new ConcurrentDictionary<string, ConcurrentDictionary<string, Guid>>();
        _connectionManager = new WebSocketConnectionManager();

        // Server salt from configuration - REQUIRED (fail-fast for production safety)
        // All service instances must share the same salt for session shortcuts to work correctly
        if (string.IsNullOrEmpty(configuration.ServerSalt))
        {
            throw new InvalidOperationException(
                "CONNECT_SERVERSALT is required. All service instances must share the same salt for session shortcuts to work correctly.");
        }
        _serverSalt = configuration.ServerSalt;

        // Generate unique instance ID for distributed deployment
        _instanceId = Environment.MachineName + "-" + Guid.NewGuid().ToString("N")[..8];

        // Register event handlers via partial class (ConnectServiceEvents.cs)
        RegisterEventConsumers(eventConsumer);

        // Connection mode configuration
        _connectionMode = configuration.ConnectionMode ?? "external";
        _internalAuthMode = configuration.InternalAuthMode ?? "service-token";
        _internalServiceToken = string.IsNullOrEmpty(configuration.InternalServiceToken)
            ? null
            : configuration.InternalServiceToken;

        // Validate Internal mode configuration
        if (_connectionMode == "internal" &&
            _internalAuthMode == "service-token" &&
            string.IsNullOrEmpty(_internalServiceToken))
        {
            throw new InvalidOperationException(
                "CONNECT_INTERNAL_SERVICE_TOKEN is required when ConnectionMode is 'internal' and InternalAuthMode is 'service-token'");
        }

        _logger.LogInformation("Connect service initialized: InstanceId={InstanceId}, Mode={ConnectionMode}",
            _instanceId, _connectionMode);
    }

    /// <summary>
    /// Internal API proxy for stateless requests.
    /// Routes requests through mesh to the appropriate service.
    /// </summary>
    public async Task<(StatusCodes, InternalProxyResponse?)> ProxyInternalRequestAsync(
        InternalProxyRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Processing internal proxy request to {TargetService}/{Method} {Endpoint}",
                body.TargetService, body.Method, body.TargetEndpoint);

            // Validate session has access to this API via local capability mappings
            // Session capabilities are pushed via SessionCapabilitiesEvent from Permissions service
            var endpointKey = $"{body.TargetService}:{body.Method}:{body.TargetEndpoint}";
            var hasAccess = false;

            if (_sessionServiceMappings.TryGetValue(body.SessionId, out var sessionMappings))
            {
                hasAccess = sessionMappings.ContainsKey(endpointKey);
            }

            if (!hasAccess)
            {
                _logger.LogWarning("Session {SessionId} denied access to {Service}/{Method} - not in capability manifest",
                    body.SessionId, body.TargetService, body.Method);

                return (StatusCodes.Forbidden, new InternalProxyResponse
                {
                    Success = false,
                    StatusCode = 403,
                    Error = "Access denied - endpoint not in capability manifest"
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
                // Route through Bannou service invocation
                HttpResponseMessage httpResponse;

                if (body.Method == InternalProxyRequestMethod.GET ||
                    body.Method == InternalProxyRequestMethod.DELETE)
                {
                    // For GET/DELETE, no body
                    var request = _meshClient.CreateInvokeMethodRequest(httpMethod, appId, endpoint);
                    httpResponse = await _meshClient.InvokeMethodWithResponseAsync(request, cancellationToken);
                }
                else
                {
                    // For POST/PUT/PATCH, include body
                    var jsonBody = body.Body != null ? BannouJson.Serialize(body.Body) : null;

                    var content = jsonBody != null ?
                        new StringContent(jsonBody, Encoding.UTF8, "application/json") : null;
                    var request = _meshClient.CreateInvokeMethodRequest(httpMethod, appId, endpoint);
                    if (content != null)
                    {
                        request.Content = content;
                    }
                    httpResponse = await _meshClient.InvokeMethodWithResponseAsync(request, cancellationToken);
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
            catch (Exception meshEx)
            {
                _logger.LogError(meshEx, "Bannou service invocation failed for {Service}/{Endpoint}",
                    body.TargetService, endpoint);
                await PublishErrorEventAsync("ProxyInternalRequest", meshEx.GetType().Name, meshEx.Message, dependency: body.TargetService, details: new { Endpoint = endpoint });

                var errorResponse = new InternalProxyResponse
                {
                    Success = false,
                    StatusCode = 503,
                    Error = $"Service invocation failed: {meshEx.Message}",
                    ExecutionTime = (int)(DateTime.UtcNow - startTime).TotalMilliseconds
                };

                return (StatusCodes.InternalServerError, errorResponse);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing internal proxy request");
            await PublishErrorEventAsync("ProxyInternalRequest", ex.GetType().Name, ex.Message);
            return (StatusCodes.InternalServerError, null);
        }
    }

    // REMOVED: PublishServiceMappingUpdateAsync - Service mapping events belong to Orchestrator
    // REMOVED: GetServiceMappingsAsync - Service routing is now in Orchestrator API
    // REMOVED: DiscoverAPIsAsync - API discovery belongs to Permissions service
    // Connect service ONLY handles WebSocket connections and message routing

    /// <summary>
    /// Gets the client capability manifest (GUID to API mappings) for a connected session.
    /// Debugging endpoint to inspect what capabilities a WebSocket client currently has.
    /// </summary>
    public async Task<(StatusCodes, ClientCapabilitiesResponse?)> GetClientCapabilitiesAsync(
        GetClientCapabilitiesRequest body,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask; // Satisfy async requirement for sync method
        try
        {
            _logger.LogDebug("GetClientCapabilitiesAsync called for session {SessionId} with filter: {Filter}",
                body.SessionId, body.ServiceFilter ?? "(none)");

            // Look up the connection by session ID
            var connection = _connectionManager.GetConnection(body.SessionId);
            if (connection == null)
            {
                _logger.LogWarning("No active WebSocket connection found for session {SessionId}", body.SessionId);
                return (StatusCodes.NotFound, null);
            }

            var connectionState = connection.ConnectionState;
            var capabilities = new List<ClientCapability>();
            var shortcuts = new List<ClientShortcut>();

            // Build capabilities from service mappings
            foreach (var mapping in connectionState.ServiceMappings)
            {
                var endpointKey = mapping.Key;
                var guid = mapping.Value;

                var firstColon = endpointKey.IndexOf(':');
                if (firstColon <= 0) continue;

                var serviceName = endpointKey[..firstColon];
                var methodAndPath = endpointKey[(firstColon + 1)..];

                // Apply service filter if provided
                if (!string.IsNullOrEmpty(body.ServiceFilter) &&
                    !serviceName.StartsWith(body.ServiceFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var methodPathColon = methodAndPath.IndexOf(':');
                var method = methodPathColon > 0 ? methodAndPath[..methodPathColon] : methodAndPath;
                var path = methodPathColon > 0 ? methodAndPath[(methodPathColon + 1)..] : "";

                // Skip template endpoints (zero-copy routing requirement)
                if (path.Contains('{')) continue;

                // Only expose POST endpoints to WebSocket clients
                if (method != "POST") continue;

                capabilities.Add(new ClientCapability
                {
                    Guid = guid,
                    Service = serviceName,
                    Endpoint = path,
                    Method = ClientCapabilityMethod.POST
                });
            }

            // Build shortcuts from connection state
            foreach (var shortcut in connectionState.GetAllShortcuts())
            {
                // Skip expired shortcuts
                if (shortcut.IsExpired)
                {
                    connectionState.RemoveShortcut(shortcut.RouteGuid);
                    continue;
                }

                // Validate required fields - these should never be null after validation during creation
                if (string.IsNullOrEmpty(shortcut.TargetService) || string.IsNullOrEmpty(shortcut.TargetEndpoint))
                {
                    _logger.LogError(
                        "Invalid shortcut {RouteGuid} has null/empty required fields: TargetService={TargetService}, TargetEndpoint={TargetEndpoint}",
                        shortcut.RouteGuid, shortcut.TargetService, shortcut.TargetEndpoint);
                    await PublishErrorEventAsync(
                        "GetClientCapabilities",
                        "invalid_shortcut_data",
                        $"Shortcut {shortcut.RouteGuid} has null/empty required fields",
                        details: new { shortcut.RouteGuid, shortcut.TargetService, shortcut.TargetEndpoint });
                    connectionState.RemoveShortcut(shortcut.RouteGuid);
                    continue;
                }

                shortcuts.Add(new ClientShortcut
                {
                    Guid = shortcut.RouteGuid,
                    TargetService = shortcut.TargetService,
                    TargetEndpoint = shortcut.TargetEndpoint,
                    Name = shortcut.Name ?? shortcut.RouteGuid.ToString(),
                    Description = shortcut.Description
                });
            }

            var response = new ClientCapabilitiesResponse
            {
                SessionId = body.SessionId,
                Capabilities = capabilities,
                Shortcuts = shortcuts.Count > 0 ? shortcuts : null,
                Version = 1,
                GeneratedAt = DateTimeOffset.UtcNow
            };

            _logger.LogInformation("Returning {CapabilityCount} capabilities and {ShortcutCount} shortcuts for session {SessionId}",
                capabilities.Count, shortcuts.Count, body.SessionId);

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving client capabilities for session {SessionId}", body.SessionId);
            await PublishErrorEventAsync("GetClientCapabilities", ex.GetType().Name, ex.Message, details: new { SessionId = body.SessionId });
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Gets all active WebSocket session IDs for an account.
    /// Internal endpoint for service-to-service session discovery.
    /// </summary>
    public async Task<(StatusCodes, GetAccountSessionsResponse?)> GetAccountSessionsAsync(
        GetAccountSessionsRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("GetAccountSessionsAsync called for account {AccountId}", body.AccountId);

            var sessions = await _sessionManager.GetSessionsForAccountAsync(body.AccountId);

            var response = new GetAccountSessionsResponse
            {
                AccountId = body.AccountId,
                SessionIds = sessions.ToList(),
                Count = sessions.Count,
                RetrievedAt = DateTimeOffset.UtcNow
            };

            _logger.LogInformation("Returning {Count} sessions for account {AccountId}",
                sessions.Count, body.AccountId);

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving sessions for account {AccountId}", body.AccountId);
            await PublishErrorEventAsync("GetAccountSessions", ex.GetType().Name, ex.Message, details: new { AccountId = body.AccountId });
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Validates JWT token and extracts session ID and user roles.
    /// Returns a tuple with session ID, roles, and whether this is a reconnection for capability initialization.
    /// </summary>
    /// <param name="authorization">Authorization header (Bearer token or Reconnect token).</param>
    /// <param name="serviceTokenHeader">Optional X-Service-Token header for Internal mode authentication.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<(string? SessionId, Guid? AccountId, ICollection<string>? Roles, ICollection<string>? Authorizations, bool IsReconnection)> ValidateJWTAndExtractSessionAsync(
        string? authorization,
        string? serviceTokenHeader,
        CancellationToken cancellationToken)
    {
        try
        {
            // Internal mode authentication - bypass JWT validation
            if (_connectionMode == "internal")
            {
                if (_internalAuthMode == "network-trust")
                {
                    // Network trust: Accept connection without authentication
                    var sessionId = Guid.NewGuid().ToString();
                    _logger.LogInformation("Internal mode (network-trust): Creating session {SessionId}", sessionId);
                    return (sessionId, null, new List<string> { "internal" }, null, false);
                }

                if (_internalAuthMode == "service-token")
                {
                    // Service token: Validate X-Service-Token header
                    if (string.IsNullOrEmpty(serviceTokenHeader))
                    {
                        _logger.LogWarning("Internal mode requires X-Service-Token header");
                        return (null, null, null, null, false);
                    }

                    if (serviceTokenHeader != _internalServiceToken)
                    {
                        _logger.LogWarning("Invalid X-Service-Token provided");
                        return (null, null, null, null, false);
                    }

                    // Valid service token - create session
                    var sessionId = Guid.NewGuid().ToString();
                    _logger.LogInformation("Internal mode (service-token): Creating session {SessionId}", sessionId);
                    return (sessionId, null, new List<string> { "internal" }, null, false);
                }
            }

            // External/Relayed mode: Require JWT authentication
            _logger.LogDebug("JWT validation starting, AuthorizationLength: {Length}, HasBearerPrefix: {IsBearer}",
                authorization?.Length ?? 0, authorization?.StartsWith("Bearer ") ?? false);

            if (string.IsNullOrEmpty(authorization))
            {
                _logger.LogWarning("Authorization header missing or empty");
                return (null, null, null, null, false);
            }

            // Handle "Bearer <token>" format
            if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = authorization.Substring(7);

                _logger.LogDebug("Validating JWT token, TokenLength: {TokenLength}", token.Length);

                // Use auth service to validate token with header-based authorization
                // Cast to concrete type to access fluent WithAuthorization method
                var authClient = (AuthClient)_authClient;
                var validationResponse = await authClient
                    .WithAuthorization(token)
                    .ValidateTokenAsync(cancellationToken);

                if (validationResponse == null)
                {
                    _logger.LogError("Auth service returned null validation response");
                    await PublishErrorEventAsync("ValidateJWT", "null_response", "Auth service returned null validation response", dependency: "auth");
                    return (null, null, null, null, false);
                }

                _logger.LogDebug("Token validation result - Valid: {Valid}, SessionId: {SessionId}, AccountId: {AccountId}, RolesCount: {RolesCount}, AuthorizationsCount: {AuthorizationsCount}",
                    validationResponse.Valid,
                    validationResponse.SessionId,
                    validationResponse.AccountId,
                    validationResponse.Roles?.Count ?? 0,
                    validationResponse.Authorizations?.Count ?? 0);

                if (validationResponse.Valid && validationResponse.SessionId != Guid.Empty)
                {
                    _logger.LogDebug("JWT validated successfully, SessionId: {SessionId}", validationResponse.SessionId);
                    // Return session ID, account ID, roles, and authorizations for capability initialization
                    // This is a new connection (Bearer token), not a reconnection
                    return (validationResponse.SessionId.ToString(), validationResponse.AccountId, validationResponse.Roles, validationResponse.Authorizations, false);
                }
                else
                {
                    _logger.LogWarning("JWT validation failed, Valid: {Valid}, SessionId: {SessionId}",
                        validationResponse.Valid, validationResponse.SessionId);
                }
            }
            // Handle "Reconnect <token>" format for session reconnection
            else if (authorization.StartsWith("Reconnect ", StringComparison.OrdinalIgnoreCase))
            {
                var reconnectionToken = authorization.Substring("Reconnect ".Length).Trim();

                if (string.IsNullOrEmpty(reconnectionToken))
                {
                    _logger.LogWarning("Empty reconnection token provided");
                    return (null, null, null, null, false);
                }

                // Use Redis session manager to validate reconnection token
                var sessionId = await _sessionManager.ValidateReconnectionTokenAsync(reconnectionToken);

                if (!string.IsNullOrEmpty(sessionId))
                {
                    // Restore the session from reconnection state
                    var restoredState = await _sessionManager.RestoreSessionFromReconnectionAsync(sessionId, reconnectionToken);

                    if (restoredState != null)
                    {
                        _logger.LogInformation("Session {SessionId} reconnected successfully", sessionId);
                        // Return stored roles and authorizations from reconnection state
                        // Parse AccountId from string back to Guid (stored as string for Redis serialization)
                        // Mark as reconnection so services can re-publish shortcuts
                        Guid? restoredAccountId = Guid.TryParse(restoredState.AccountId, out var parsedGuid) ? parsedGuid : null;
                        return (sessionId, restoredAccountId, restoredState.UserRoles, restoredState.Authorizations, true);
                    }
                }

                _logger.LogWarning("Invalid or expired reconnection token");
                return (null, null, null, null, false);
            }

            _logger.LogWarning("Authorization format not recognized (expected 'Bearer' or 'Reconnect' prefix)");
            return (null, null, null, null, false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "JWT validation failed with exception");
            await PublishErrorEventAsync("ValidateJWT", ex.GetType().Name, ex.Message, dependency: "auth");
            return (null, null, null, null, false);
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
        Guid? accountId,
        ICollection<string>? userRoles,
        ICollection<string>? authorizations,
        bool isReconnection,
        CancellationToken cancellationToken)
    {
        // Create connection state with service mappings from discovery
        var connectionState = new ConnectionState(sessionId);

        // INTERNAL MODE: Skip all capability initialization - just peer routing
        if (_connectionMode == "internal")
        {
            // Add connection to manager (enables peer routing via PeerGuid)
            _connectionManager.AddConnection(sessionId, webSocket, connectionState);

            // Send minimal response with sessionId and peerGuid
            var internalResponse = new
            {
                sessionId = sessionId,
                peerGuid = connectionState.PeerGuid.ToString()
            };
            var responseJson = BannouJson.Serialize(internalResponse);
            var responseBytes = Encoding.UTF8.GetBytes(responseJson);

            await webSocket.SendAsync(
                new ArraySegment<byte>(responseBytes),
                WebSocketMessageType.Text,
                endOfMessage: true,
                cancellationToken);

            _logger.LogInformation("Internal mode: Session {SessionId} connected with PeerGuid {PeerGuid}",
                sessionId, connectionState.PeerGuid);

            // Enter simplified message loop - only binary messages, no capability/event handling
            await HandleInternalModeMessageLoopAsync(webSocket, sessionId, connectionState, cancellationToken);
            return;
        }

        // EXTERNAL/RELAYED MODE: Full capability initialization

        // Transfer service mappings from session discovery to connection state
        // Try to get mappings from Redis first
        var sessionMappings = await _sessionManager.GetSessionServiceMappingsAsync(sessionId);

        if (sessionMappings == null && _sessionServiceMappings.TryGetValue(sessionId, out var fallbackMappings))
        {
            // Fallback to in-memory mappings (thread-safe copy)
            sessionMappings = new Dictionary<string, Guid>(fallbackMappings);
        }

        // If no mappings exist, initialize capabilities from Permissions service
        if (sessionMappings == null || sessionMappings.Count == 0)
        {
            // Determine the highest-priority role for capability initialization
            // Priority: admin > user > anonymous
            var role = DetermineHighestPriorityRole(userRoles);
            _logger.LogInformation("No existing mappings for session {SessionId}, initializing capabilities for role {Role} with {AuthCount} authorizations",
                sessionId, role, authorizations?.Count ?? 0);
            sessionMappings = await InitializeSessionCapabilitiesAsync(sessionId, role, authorizations, cancellationToken);
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
            await _sessionManager.UpdateSessionHeartbeatAsync(sessionId, _instanceId);

            // Create initial connection state in Redis for reconnection support
            var connectionStateData = new ConnectionStateData
            {
                SessionId = sessionId,
                AccountId = accountId?.ToString(),
                ConnectedAt = DateTimeOffset.UtcNow,
                LastActivity = DateTimeOffset.UtcNow,
                UserRoles = userRoles?.ToList(),
                Authorizations = authorizations?.ToList()
            };
            await _sessionManager.SetConnectionStateAsync(sessionId, connectionStateData);
            _logger.LogDebug("Created connection state in Redis for session {SessionId}, AccountId {AccountId}", sessionId, accountId);

            // Subscribe to session-specific client events via lib-messaging
            // IMPORTANT: Must subscribe BEFORE sending capability manifest to avoid race condition
            // where events published during capability compilation are lost
            try
            {
                var topic = $"{SESSION_TOPIC_PREFIX}{sessionId}";
                // Use deterministic queue name based on session ID for reconnection support
                // Queue survives disconnect with TTL matching reconnection window - messages buffer during disconnect
                var queueName = $"session.events.{sessionId}";
                var subscription = await _messageSubscriber.SubscribeDynamicRawAsync(
                    topic: topic,
                    handler: (bytes, ct) => HandleClientEventAsync(sessionId, bytes),
                    exchange: CLIENT_EVENTS_EXCHANGE,
                    exchangeType: SubscriptionExchangeType.Direct,
                    queueName: queueName,
                    queueTtl: RECONNECTION_WINDOW,
                    cancellationToken: cancellationToken);

                _sessionSubscriptions[sessionId] = subscription;
                _logger.LogDebug("Subscribed to client events for session {SessionId} on queue {QueueName} (TTL: {TtlMinutes}min)",
                    sessionId, queueName, RECONNECTION_WINDOW.TotalMinutes);

                // CRITICAL: Publish session.connected event AFTER RabbitMQ subscription
                // This ensures the exchange exists before any service tries to publish to it
                // Fixes race condition where services publish to non-existent exchange (crashes RabbitMQ channel)
                var sessionConnectedEvent = new SessionConnectedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    SessionId = Guid.Parse(sessionId),
                    AccountId = accountId ?? Guid.Empty,
                    Roles = userRoles?.ToList(),
                    Authorizations = authorizations?.ToList(),
                    ConnectInstanceId = Guid.TryParse(_instanceId.Split('-').LastOrDefault(), out var instanceGuid)
                        ? instanceGuid : (Guid?)null,
                    PeerGuid = connectionState.PeerGuid
                };
                await _messageBus.TryPublishAsync("session.connected", sessionConnectedEvent, cancellationToken: cancellationToken);
                _logger.LogInformation("Published session.connected event for session {SessionId} with PeerGuid {PeerGuid}",
                    sessionId, connectionState.PeerGuid);

                // Add to account session index for distributed lookup
                if (accountId.HasValue && accountId.Value != Guid.Empty)
                {
                    await _sessionManager.AddSessionToAccountAsync(accountId.Value, sessionId);
                }

                // If this is a reconnection, publish session.reconnected event so services can re-publish shortcuts
                // Shortcuts don't survive reconnection - services must re-evaluate and re-publish them
                // Note: PeerGuid is a NEW GUID for this connection - peers must be notified of the change
                if (isReconnection)
                {
                    var sessionReconnectedEvent = new SessionReconnectedEvent
                    {
                        EventId = Guid.NewGuid(),
                        Timestamp = DateTimeOffset.UtcNow,
                        SessionId = Guid.Parse(sessionId),
                        AccountId = accountId ?? Guid.Empty,
                        Roles = userRoles?.ToList(),
                        Authorizations = authorizations?.ToList(),
                        PreviousDisconnectAt = connectionState.DisconnectedAt,
                        PeerGuid = connectionState.PeerGuid,
                        ReconnectionContext = new Dictionary<string, object>
                        {
                            ["connect_instance_id"] = _instanceId
                        }
                    };
                    await _messageBus.TryPublishAsync("session.reconnected", sessionReconnectedEvent, cancellationToken: cancellationToken);
                    _logger.LogInformation("Published session.reconnected event for session {SessionId} with new PeerGuid {PeerGuid} - services should re-publish shortcuts",
                        sessionId, connectionState.PeerGuid);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to subscribe to client events for session {SessionId}", sessionId);
            }

            // Capability manifest is delivered via event-driven flow:
            // 1. session.connected event published above
            // 2. Permissions compiles capabilities and publishes SessionCapabilitiesEvent
            // 3. RabbitMQ subscriber receives event and calls ProcessCapabilitiesAsync
            // 4. ProcessCapabilitiesAsync sends the manifest with real APIs
            // DO NOT send an empty placeholder manifest here - it causes race conditions

            while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("WebSocket close requested for session {SessionId}", sessionId);
                    // Don't close here - let the finally block handle sending disconnect_notification first
                    // The WebSocket is still in CloseReceived state and can send messages
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
                    await HandleTextMessageFallbackAsync(sessionId, connectionState, textMessage, cancellationToken);
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
            await PublishErrorEventAsync("WebSocketCommunication", ex.GetType().Name, ex.Message, details: new { SessionId = sessionId });
        }
        finally
        {
            // Check if this was a forced disconnect BEFORE any cleanup
            var connection = _connectionManager.GetConnection(sessionId);
            var isForcedDisconnect = connection?.Metadata?.ContainsKey("forced_disconnect") == true;

            // CRITICAL: Publish session.disconnected event BEFORE unsubscribing from RabbitMQ
            // This ensures Permissions removes session from activeConnections before the exchange is torn down
            // Without this, services could still try to publish to the session during the brief cleanup window
            try
            {
                var sessionDisconnectedEvent = new SessionDisconnectedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    SessionId = Guid.Parse(sessionId),
                    AccountId = accountId ?? Guid.Empty,
                    Reconnectable = !isForcedDisconnect,
                    Reason = isForcedDisconnect ? "forced_disconnect" : "graceful_disconnect"
                };
                await _messageBus.TryPublishAsync("session.disconnected", sessionDisconnectedEvent);
                _logger.LogInformation("Published session.disconnected event for session {SessionId}, reconnectable: {Reconnectable}",
                    sessionId, !isForcedDisconnect);

                // Remove from account session index
                if (accountId.HasValue && accountId.Value != Guid.Empty)
                {
                    await _sessionManager.RemoveSessionFromAccountAsync(accountId.Value, sessionId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish session.disconnected event for session {SessionId}", sessionId);
                // Fire-and-forget error event - we're in finally cleanup
                _ = PublishErrorEventAsync("PublishSessionDisconnected", ex.GetType().Name, ex.Message, dependency: "messaging", details: new { SessionId = sessionId });
            }

            // Remove from connection manager - use instance-matching removal to prevent
            // race condition during session subsume where old connection's cleanup could
            // accidentally remove a new connection that replaced it (FOUNDATION TENETS compliance)
            var wasRemoved = _connectionManager.RemoveConnectionIfMatch(sessionId, webSocket);

            // CRITICAL: If wasRemoved is false, this connection was SUBSUMED by a new connection
            // with the same session ID. In this case, we must NOT:
            // - Unsubscribe from RabbitMQ (new connection is using the subscription)
            // - Initiate reconnection window (session is still active)
            // - Publish disconnect events (session is not actually disconnecting)
            if (!wasRemoved)
            {
                _logger.LogDebug("Session {SessionId} was subsumed by new connection - skipping cleanup", sessionId);
                // Skip all cleanup - new connection owns this session now
            }
            else
            {
                // Initiate reconnection window instead of immediate cleanup (unless forced disconnect)
                if (isForcedDisconnect)
                {
                    // Forced disconnect - unsubscribe from RabbitMQ and clean up immediately
                    if (_sessionSubscriptions.TryRemove(sessionId, out var subscription))
                    {
                        await subscription.DisposeAsync();
                        _logger.LogDebug("Unsubscribed from client events for forced disconnect session {SessionId}", sessionId);
                    }

                    await _sessionManager.RemoveSessionAsync(sessionId);
                    await _sessionManager.PublishSessionEventAsync("disconnect", sessionId, new { instanceId = _instanceId, reconnectable = false });
                    _logger.LogInformation("Session {SessionId} force disconnected - no reconnection allowed", sessionId);
                }
                else
                {
                    // Normal disconnect - initiate reconnection window
                    // Cancel RabbitMQ consumer - queue will buffer messages automatically
                    // RabbitMQ handles buffering natively: queue persists, messages accumulate
                    // On reconnect, we re-subscribe and get all pending messages from queue
                    if (_sessionSubscriptions.TryRemove(sessionId, out var subscription))
                    {
                        await subscription.DisposeAsync();
                        _logger.LogDebug("Cancelled RabbitMQ consumer for session {SessionId} - queue will buffer messages",
                            sessionId);
                    }

                    var reconnectionToken = connectionState.InitiateReconnectionWindow(
                        reconnectionWindowMinutes: (int)RECONNECTION_WINDOW.TotalMinutes,
                        userRoles: userRoles);

                    // Store reconnection state in Redis
                    await _sessionManager.InitiateReconnectionWindowAsync(
                        sessionId,
                        reconnectionToken,
                        RECONNECTION_WINDOW,
                        userRoles);

                    // Publish disconnect event with reconnection info
                    await _sessionManager.PublishSessionEventAsync("disconnect", sessionId, new
                    {
                        instanceId = _instanceId,
                        reconnectable = true,
                        reconnectionExpiresAt = connectionState.ReconnectionExpiresAt
                    });

                    _logger.LogInformation("Session {SessionId} disconnected - reconnection window active until {ExpiresAt}",
                        sessionId, connectionState.ReconnectionExpiresAt);
                }
            }

            // Can send messages when Open (server-initiated close) or CloseReceived (client-initiated close)
            // Skip this when subsumed - the subsume mechanism already closes the old WebSocket with "New connection established"
            if (wasRemoved && (webSocket.State == WebSocketState.Open || webSocket.State == WebSocketState.CloseReceived))
            {
                try
                {
                    // Send disconnect notification with reconnection token (if applicable)
                    if (!isForcedDisconnect && connectionState.ReconnectionToken != null)
                    {
                        var disconnectNotification = new
                        {
                            eventName = "connect.disconnect_notification",
                            reconnectionToken = connectionState.ReconnectionToken,
                            expiresAt = connectionState.ReconnectionExpiresAt?.ToString("O"),
                            reconnectable = true,
                            reason = "graceful_disconnect"
                        };

                        var notificationJson = BannouJson.Serialize(disconnectNotification);
                        var notificationBytes = System.Text.Encoding.UTF8.GetBytes(notificationJson);

                        await webSocket.SendAsync(
                            new ArraySegment<byte>(notificationBytes),
                            WebSocketMessageType.Text,
                            endOfMessage: true,
                            CancellationToken.None);

                        _logger.LogDebug("Sent disconnect notification with reconnection token to session {SessionId}", sessionId);
                    }

                    // Use CloseOutputAsync when client already sent close (CloseReceived state)
                    // Use CloseAsync when server initiates (Open state)
                    if (webSocket.State == WebSocketState.CloseReceived)
                    {
                        await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure,
                            "Session ended", CancellationToken.None);
                    }
                    else
                    {
                        await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure,
                            "Session ended", CancellationToken.None);
                    }
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

            // META REQUEST INTERCEPTION: Check before validation/routing
            // When Meta flag is set, route to companion meta endpoints instead of executing the endpoint
            if (message.IsMeta)
            {
                if (!routeInfo.IsValid)
                {
                    _logger.LogDebug("Meta request validation failed from session {SessionId}: {Error}",
                        sessionId, routeInfo.ErrorMessage);
                    var errorResponse = BinaryMessage.CreateResponse(message, routeInfo.ErrorCode);
                    await _connectionManager.SendMessageAsync(sessionId, errorResponse, cancellationToken);
                    return;
                }

                if (routeInfo.ServiceName == null)
                {
                    _logger.LogDebug("Meta request for unknown GUID {Guid} from session {SessionId}",
                        message.ServiceGuid, sessionId);
                    var errorResponse = BinaryMessage.CreateResponse(message, ResponseCodes.ServiceNotFound);
                    await _connectionManager.SendMessageAsync(sessionId, errorResponse, cancellationToken);
                    return;
                }

                await HandleMetaRequestAsync(message, routeInfo, sessionId, connectionState, cancellationToken);
                return;
            }

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
            else if (routeInfo.RouteType == RouteType.SessionShortcut)
            {
                // Session shortcut: rewrite message with target GUID and inject pre-bound payload
                if (routeInfo.TargetGuid == null || routeInfo.InjectedPayload == null)
                {
                    _logger.LogError("SessionShortcut route missing TargetGuid or InjectedPayload for session {SessionId}", sessionId);
                    await PublishErrorEventAsync("HandleBinaryMessage", "shortcut_config_error", "SessionShortcut route missing TargetGuid or InjectedPayload", details: new { SessionId = sessionId });
                    var errorResponse = MessageRouter.CreateErrorResponse(
                        message, ResponseCodes.ShortcutTargetNotFound, "Shortcut configuration error");
                    await _connectionManager.SendMessageAsync(sessionId, errorResponse, cancellationToken);
                    return;
                }

                _logger.LogInformation("Routing shortcut '{ShortcutName}' for session {SessionId}: {RouteGuid} -> {TargetGuid}",
                    routeInfo.ShortcutName ?? "unnamed", sessionId, message.ServiceGuid, routeInfo.TargetGuid.Value);

                // Create rewritten message with target GUID and injected payload
                var rewrittenMessage = new BinaryMessage(
                    flags: message.Flags,
                    channel: message.Channel,
                    sequenceNumber: message.SequenceNumber,
                    serviceGuid: routeInfo.TargetGuid.Value,
                    messageId: message.MessageId,
                    payload: routeInfo.InjectedPayload
                );

                // Route the rewritten message to the target service
                await RouteToServiceAsync(rewrittenMessage, routeInfo, sessionId, connectionState, cancellationToken);
            }
            else if (routeInfo.RouteType == RouteType.Client)
            {
                await RouteToClientAsync(message, routeInfo, sessionId, cancellationToken);
            }
            else if (routeInfo.RouteType == RouteType.Broadcast)
            {
                // Broadcast: Send to all connected peers (except sender)
                // Mode enforcement: External mode rejects broadcast with BroadcastNotAllowed
                if (_connectionMode == "external")
                {
                    _logger.LogWarning("Broadcast rejected in External mode for session {SessionId}", sessionId);
                    var errorResponse = MessageRouter.CreateErrorResponse(
                        message, ResponseCodes.BroadcastNotAllowed, "Broadcast not allowed in External mode");
                    await _connectionManager.SendMessageAsync(sessionId, errorResponse, cancellationToken);
                    return;
                }

                // Relayed or Internal mode: Allow broadcast
                await RouteToBroadcastAsync(message, routeInfo, sessionId, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling binary message from session {SessionId}", sessionId);
            await PublishErrorEventAsync("HandleBinaryMessage", ex.GetType().Name, ex.Message, details: new { SessionId = sessionId });

            // Send generic error response if we can parse the message
            try
            {
                var message = BinaryMessage.Parse(buffer, messageLength);
                var errorResponse = MessageRouter.CreateErrorResponse(
                    message, ResponseCodes.RequestError, "Internal server error");

                await _connectionManager.SendMessageAsync(sessionId, errorResponse, cancellationToken);
            }
            catch (Exception parseEx)
            {
                // If we can't even parse the message or send error response, log and continue
                // This is a last resort - the outer exception handler already logged the original error
                _logger.LogError(parseEx, "Failed to send error response to session {SessionId} - message may be corrupted", sessionId);
                _ = PublishErrorEventAsync("HandleBinaryMessage", parseEx.GetType().Name, "Failed to send error response - message may be corrupted", details: new { SessionId = sessionId });
            }
        }
    }

    /// <summary>
    /// Routes a message to a Bannou service and handles the response.
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

            // Get raw payload bytes - true zero-copy forwarding without UTF-16 string conversion
            // Connect service should NEVER parse the payload - zero-copy routing based on GUID only
            var payloadBytes = message.Payload;

            // Make the actual Bannou service invocation via direct HTTP
            string? responseJson = null;
            HttpResponseMessage? httpResponse = null;

            try
            {
                // Build mesh URL using direct path
                var bannouHttpEndpoint = Program.Configuration.EffectiveHttpEndpoint;
                var meshUrl = $"{bannouHttpEndpoint}/{path.TrimStart('/')}";

                // Create HTTP request with proper headers
                using var request = new HttpRequestMessage(new HttpMethod(httpMethod), meshUrl);

                // Add bannou-app-id header for routing (like ServiceClientBase.PrepareRequest)
                request.Headers.Add("bannou-app-id", appId);
                // Use static cached Accept header to avoid per-request allocation
                request.Headers.Accept.Add(s_jsonAcceptHeader);

                // TRACING ONLY: Pass client's WebSocket session ID to downstream services for request correlation.
                // WARNING: This header is ONLY for distributed tracing and logging correlation.
                // DO NOT use this for ownership, attribution, or access control decisions.
                // For ownership/audit trail, services must receive an explicit "owner" field in request bodies
                // containing either a service name (for service-initiated operations) or an accountId
                // (for user-initiated operations).
                request.Headers.Add("X-Bannou-Session-Id", sessionId);

                // Use Warning level to ensure visibility even when app logging is set to Warning
                _logger.LogWarning("WebSocket -> mesh HTTP: {Method} {Uri} AppId={AppId}",
                    request.Method, meshUrl, appId);

                // Pass raw payload bytes directly to service - true zero-copy forwarding
                // Uses ByteArrayContent instead of StringContent to avoid UTF-8 → UTF-16 → UTF-8 conversion
                // All WebSocket binary protocol endpoints should be POST with JSON body
                if (payloadBytes.Length > 0 && httpMethod == "POST")
                {
                    var content = new ByteArrayContent(payloadBytes.ToArray());
                    content.Headers.ContentType = s_jsonContentType;
                    request.Content = content;
                    _logger.LogDebug("Request body: {Length} bytes", payloadBytes.Length);
                }

                // Track timing for long-running requests
                var requestStartTime = DateTimeOffset.UtcNow;
                _logger.LogDebug("Sending HTTP request to mesh at {StartTime}", requestStartTime);

                // Create HttpClient from factory (properly pooled, configured with 120s timeout)
                using var httpClient = _httpClientFactory.CreateClient(HttpClientName);

                // Invoke the service via direct HTTP (preserves full path)
                httpResponse = await httpClient.SendAsync(request, cancellationToken);

                var requestDuration = DateTimeOffset.UtcNow - requestStartTime;
                // Use Warning level for response timing to ensure visibility in CI
                _logger.LogWarning("mesh HTTP response in {DurationMs}ms: {StatusCode}",
                    requestDuration.TotalMilliseconds, (int)httpResponse.StatusCode);

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
            catch (TaskCanceledException tcEx) when (tcEx.InnerException is TimeoutException)
            {
                // HTTP client timeout reached (120 seconds)
                _logger.LogError("mesh HTTP request timed out (120s) for {Service} {Method} {Path}",
                    serviceName, httpMethod, path);
                await PublishErrorEventAsync("RouteToService", "timeout", $"mesh HTTP request timed out (120s) for {serviceName}", dependency: serviceName, details: new { Method = httpMethod, Path = path });
                // Error payload discarded by BinaryMessage.CreateResponse for non-OK responses
            }
            catch (TaskCanceledException tcEx)
            {
                // Either cancellation was requested or timeout without inner TimeoutException
                var isTimeout = !cancellationToken.IsCancellationRequested;
                if (isTimeout)
                {
                    _logger.LogError("mesh HTTP request timed out for {Service} {Method} {Path}",
                        serviceName, httpMethod, path);
                    await PublishErrorEventAsync("RouteToService", "timeout", $"mesh HTTP request timed out for {serviceName}", dependency: serviceName, details: new { Method = httpMethod, Path = path });
                }
                else
                {
                    _logger.LogWarning(tcEx, "mesh HTTP request cancelled for {Service} {Method} {Path}",
                        serviceName, httpMethod, path);
                }
                // Error payload discarded by BinaryMessage.CreateResponse for non-OK responses
            }
            catch (HttpRequestException httpEx)
            {
                _logger.LogWarning(httpEx, "HTTP request to mesh failed for {Service} {Method} {Path}",
                    serviceName, httpMethod, path);
                await PublishErrorEventAsync("RouteToService", "http_error", httpEx.Message, dependency: serviceName, details: new { Method = httpMethod, Path = path, StatusCode = (int?)httpEx.StatusCode });
                // Error payload discarded by BinaryMessage.CreateResponse for non-OK responses
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error invoking service {Service} {Method} {Path}",
                    serviceName, httpMethod, path);
                await PublishErrorEventAsync("RouteToService", ex.GetType().Name, ex.Message, dependency: serviceName, details: new { Method = httpMethod, Path = path });
                // Error payload discarded by BinaryMessage.CreateResponse for non-OK responses
            }

            // Send response back to WebSocket client
            if (routeInfo.RequiresResponse)
            {
                var responseCode = MapHttpStatusToResponseCode(httpResponse?.StatusCode);

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
            await PublishErrorEventAsync("RouteToService", ex.GetType().Name, ex.Message, dependency: routeInfo.ServiceName, details: new { SessionId = sessionId });

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
    /// Handles meta endpoint requests by transforming the path and routing to companion endpoints.
    /// Meta type is encoded in the Channel field (0=info, 1=request-schema, 2=response-schema, 3=full-schema).
    /// </summary>
    private async Task HandleMetaRequestAsync(
        BinaryMessage message,
        MessageRouteInfo routeInfo,
        string sessionId,
        ConnectionState connectionState,
        CancellationToken cancellationToken)
    {
        // ServiceName must be validated by caller before invoking this method
        if (routeInfo.ServiceName == null)
        {
            throw new InvalidOperationException(
                "HandleMetaRequestAsync called with null ServiceName - caller must validate");
        }

        // Parse endpoint key: "serviceName:METHOD:/path"
        var parts = routeInfo.ServiceName.Split(':', 3);
        if (parts.Length < 3)
        {
            _logger.LogWarning("Invalid endpoint key format for meta request: {EndpointKey}", routeInfo.ServiceName);
            var errorResponse = BinaryMessage.CreateResponse(message, ResponseCodes.RequestError);
            await _connectionManager.SendMessageAsync(sessionId, errorResponse, cancellationToken);
            return;
        }

        var serviceName = parts[0];
        var httpMethod = parts[1];
        var originalPath = parts[2];

        // Determine meta type from Channel field
        var metaType = (MetaType)message.Channel;
        var metaSuffix = metaType switch
        {
            MetaType.EndpointInfo => "info",
            MetaType.RequestSchema => "request-schema",
            MetaType.ResponseSchema => "response-schema",
            MetaType.FullSchema => "schema",
            _ => "info"  // Default fallback for unknown values
        };

        // Transform path to companion endpoint
        var metaPath = $"{originalPath}/meta/{metaSuffix}";

        _logger.LogTrace("Meta request: {MetaType} for {ServiceName}:{HttpMethod}:{Path} -> {MetaPath}",
            metaType, serviceName, httpMethod, originalPath, metaPath);

        // Create modified routeInfo with transformed path
        // Meta endpoints are always GET requests
        var metaRouteInfo = new MessageRouteInfo
        {
            Message = routeInfo.Message,
            IsValid = routeInfo.IsValid,
            RouteType = routeInfo.RouteType,
            TargetType = routeInfo.TargetType,
            TargetId = routeInfo.TargetId,
            Channel = routeInfo.Channel,
            Priority = routeInfo.Priority,
            RequiresResponse = true, // Meta requests always expect a response
            ServiceName = $"{serviceName}:GET:{metaPath}"
        };

        // Route to service (companion endpoint handles the response)
        await RouteToServiceAsync(message, metaRouteInfo, sessionId, connectionState, cancellationToken);
    }

    /// <summary>
    /// Routes a message to another WebSocket client (peer-to-peer communication).
    /// Uses the ServiceGuid from the message header to look up the target peer's connection.
    /// The payload is forwarded zero-copy without deserialization.
    /// </summary>
    private async Task RouteToClientAsync(
        BinaryMessage message,
        MessageRouteInfo routeInfo,
        string sessionId,
        CancellationToken cancellationToken)
    {
        try
        {
            // For peer-to-peer routing, the ServiceGuid in the message header is the target peer's GUID
            var targetPeerGuid = message.ServiceGuid;

            if (targetPeerGuid == Guid.Empty)
            {
                var errorResponse = MessageRouter.CreateErrorResponse(
                    message, ResponseCodes.ClientNotFound, "Target peer GUID not specified");

                await _connectionManager.SendMessageAsync(sessionId, errorResponse, cancellationToken);
                return;
            }

            // Look up the target peer's session ID using the peer GUID registry
            if (!_connectionManager.TryGetSessionIdByPeerGuid(targetPeerGuid, out var targetSessionId) || targetSessionId == null)
            {
                _logger.LogDebug("Peer GUID {PeerGuid} not found in registry for session {SessionId}",
                    targetPeerGuid, sessionId);

                var errorResponse = MessageRouter.CreateErrorResponse(
                    message, ResponseCodes.ClientNotFound, $"Target peer {targetPeerGuid} not connected");

                await _connectionManager.SendMessageAsync(sessionId, errorResponse, cancellationToken);
                return;
            }

            _logger.LogDebug("Routing peer-to-peer message from {SourceSession} to peer {TargetPeerGuid} (session {TargetSession}), payload size {PayloadSize}",
                sessionId, targetPeerGuid, targetSessionId, message.Payload.Length);

            // Forward the message zero-copy to the target peer
            var sent = await _connectionManager.SendMessageAsync(targetSessionId, message, cancellationToken);

            if (!sent)
            {
                var errorResponse = MessageRouter.CreateErrorResponse(
                    message, ResponseCodes.ClientNotFound, $"Target peer {targetPeerGuid} disconnected");

                await _connectionManager.SendMessageAsync(sessionId, errorResponse, cancellationToken);
            }
            else if (message.ExpectsResponse)
            {
                // For peer-to-peer, send acknowledgment that the message was delivered
                var ackPayload = new
                {
                    status = "delivered",
                    targetPeerGuid = targetPeerGuid.ToString(),
                    originalMessageId = message.MessageId
                };

                var ackMessage = BinaryMessage.CreateResponse(
                    message, ResponseCodes.OK, Encoding.UTF8.GetBytes(BannouJson.Serialize(ackPayload)));

                await _connectionManager.SendMessageAsync(sessionId, ackMessage, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error routing peer-to-peer message to {TargetPeerGuid}", message.ServiceGuid);
            await PublishErrorEventAsync("RouteToClient", ex.GetType().Name, ex.Message, details: new { SessionId = sessionId, TargetPeerGuid = message.ServiceGuid });

            if (message.ExpectsResponse)
            {
                var errorResponse = MessageRouter.CreateErrorResponse(
                    message, ResponseCodes.RequestError, "Peer routing failed");

                await _connectionManager.SendMessageAsync(sessionId, errorResponse, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Routes a message to all connected WebSocket clients (broadcast).
    /// Only allowed in Relayed and Internal connection modes - External mode blocks broadcast.
    /// </summary>
    private async Task RouteToBroadcastAsync(
        BinaryMessage message,
        MessageRouteInfo routeInfo,
        string senderSessionId,
        CancellationToken cancellationToken)
    {
        try
        {
            // Get all active session IDs except sender
            var allSessionIds = _connectionManager.GetActiveSessionIds()
                .Where(id => id != senderSessionId)
                .ToList();

            _logger.LogDebug("Broadcasting message from session {SessionId} to {PeerCount} peers, payload size {PayloadSize}",
                senderSessionId, allSessionIds.Count, message.Payload.Length);

            if (allSessionIds.Count == 0)
            {
                _logger.LogDebug("No other peers to broadcast to from session {SessionId}", senderSessionId);

                // If sender expects response, send acknowledgment with zero recipients
                if (message.ExpectsResponse)
                {
                    var ackPayload = new
                    {
                        status = "broadcast_complete",
                        recipientCount = 0,
                        originalMessageId = message.MessageId
                    };

                    var ackMessage = BinaryMessage.CreateResponse(
                        message, ResponseCodes.OK, Encoding.UTF8.GetBytes(BannouJson.Serialize(ackPayload)));

                    await _connectionManager.SendMessageAsync(senderSessionId, ackMessage, cancellationToken);
                }
                return;
            }

            // Forward message to all peers (excluding sender) in parallel
            var tasks = allSessionIds.Select(targetSessionId =>
                _connectionManager.SendMessageAsync(targetSessionId, message, cancellationToken));

            var results = await Task.WhenAll(tasks);
            var successCount = results.Count(r => r);

            _logger.LogInformation("Broadcast from {SessionId} sent to {SuccessCount}/{TotalCount} peers",
                senderSessionId, successCount, allSessionIds.Count);

            // If sender expects response, send acknowledgment
            if (message.ExpectsResponse)
            {
                var ackPayload = new
                {
                    status = "broadcast_complete",
                    recipientCount = successCount,
                    originalMessageId = message.MessageId
                };

                var ackMessage = BinaryMessage.CreateResponse(
                    message, ResponseCodes.OK, Encoding.UTF8.GetBytes(BannouJson.Serialize(ackPayload)));

                await _connectionManager.SendMessageAsync(senderSessionId, ackMessage, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting message from session {SessionId}", senderSessionId);
            await PublishErrorEventAsync("RouteToBroadcast", ex.GetType().Name, ex.Message, details: new { SessionId = senderSessionId });

            if (message.ExpectsResponse)
            {
                var errorResponse = MessageRouter.CreateErrorResponse(
                    message, ResponseCodes.RequestError, "Broadcast failed");

                await _connectionManager.SendMessageAsync(senderSessionId, errorResponse, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Simplified message loop for Internal mode connections.
    /// Only handles binary messages for peer routing and broadcast - no capability/event handling.
    /// </summary>
    private async Task HandleInternalModeMessageLoopAsync(
        WebSocket webSocket,
        string sessionId,
        ConnectionState connectionState,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[65536];

        try
        {
            while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("Internal mode: WebSocket close requested for session {SessionId}", sessionId);
                    break;
                }

                connectionState.UpdateActivity();

                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    await HandleBinaryMessageAsync(sessionId, connectionState, buffer, result.Count, cancellationToken);
                }
                // Internal mode ignores text messages - binary protocol only
            }
        }
        catch (WebSocketException wsEx)
        {
            _logger.LogWarning(wsEx, "Internal mode: WebSocket error for session {SessionId}: {Error}",
                sessionId, wsEx.Message);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Internal mode: WebSocket operation cancelled for session {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Internal mode: Unexpected error for session {SessionId}", sessionId);
            await PublishErrorEventAsync("InternalModeMessageLoop", ex.GetType().Name, ex.Message, details: new { SessionId = sessionId });
        }
        finally
        {
            // Clean up connection
            _connectionManager.RemoveConnection(sessionId);
            _logger.LogInformation("Internal mode: Session {SessionId} disconnected", sessionId);
        }
    }

    /// <summary>
    /// Handles text WebSocket messages by wrapping them in binary protocol response format.
    /// This provides backwards compatibility for clients not yet using the binary protocol.
    /// </summary>
    private async Task HandleTextMessageFallbackAsync(
        string sessionId,
        ConnectionState connectionState,
        string textMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Received text message from session {SessionId}: {Message}",
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
            _logger.LogError(ex, "Error handling text message from session {SessionId}", sessionId);
            await PublishErrorEventAsync("HandleTextMessage", ex.GetType().Name, ex.Message, details: new { SessionId = sessionId });
        }
    }

    #region Service Lifecycle

    /// <summary>
    /// Service startup method - registers RabbitMQ event handler endpoints.
    /// Note: Capability updates are handled via ConnectEventsController which subscribes
    /// to permissions.capabilities-updated topic via subscriptions.yaml.
    /// </summary>
    public async Task OnStartAsync(WebApplication webApp, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        _logger.LogInformation("Registering Connect service RabbitMQ event handlers");

        // Register auth event handler (subscribes via MassTransit, not mesh Topics)
        webApp.MapPost("/events/auth-events", ProcessAuthEventAsync)
            .WithMetadata("Connect service auth event handler");

        // Register service registration handler
        webApp.MapPost("/events/service-registered", ProcessServiceRegistrationAsync)
            .WithMetadata("Connect service registration handler");

        // Register client message handler
        webApp.MapPost("/events/client-messages", ProcessClientMessageEventAsync)
            .WithMetadata("Connect service client message handler");

        // Register client RPC handler
        webApp.MapPost("/events/client-rpc", ProcessClientRPCEventAsync)
            .WithMetadata("Connect service client RPC handler");

        _logger.LogInformation("Connect service RabbitMQ event handlers registered successfully");

        // Client event subscriptions are created dynamically per-session via lib-messaging
        // using SubscribeDynamicRawAsync when sessions connect
        _logger.LogInformation("Client event subscriptions will be created per-session via lib-messaging");
    }

    #endregion

    #region RabbitMQ Event Processing Methods

    // Note: Capability updates are handled by ConnectEventsController.HandleCapabilitiesUpdatedAsync()
    // which subscribes to permissions.capabilities-updated topic via subscriptions.yaml.

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
                    // User logged in - Permissions service will automatically recompile capabilities
                    // and push updated connect.capability_manifest to client
                    _logger.LogDebug("Auth login event for session {SessionId} - capabilities will be updated by Permissions service",
                        eventData.SessionId);
                }
                else if (eventData.EventType == AuthEventType.Logout)
                {
                    // User logged out - Permissions service will automatically recompile capabilities
                    // and push updated connect.capability_manifest to client
                    _logger.LogDebug("Auth logout event for session {SessionId} - capabilities will be updated by Permissions service",
                        eventData.SessionId);

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
            await PublishErrorEventAsync("ProcessAuthEvent", ex.GetType().Name, ex.Message, details: new { SessionId = eventData.SessionId, EventType = eventData.EventType });
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
            await _messageBus.TryPublishAsync(
                "bannou-permission-recompile",
                new PermissionRecompileEvent
                {
                    EventId = Guid.NewGuid().ToString(),
                    Timestamp = DateTimeOffset.UtcNow,
                    Reason = PermissionRecompileEventReason.Service_registered,
                    ServiceId = eventData.ServiceName,
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
            await PublishErrorEventAsync("ProcessServiceRegistration", ex.GetType().Name, ex.Message, details: new { ServiceId = eventData.ServiceId });
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
            await PublishErrorEventAsync("ProcessClientMessage", ex.GetType().Name, ex.Message, details: new { ClientId = eventData.ClientId, ServiceName = eventData.ServiceName });
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
            await PublishErrorEventAsync("ProcessClientRPC", ex.GetType().Name, ex.Message, details: new { ClientId = eventData.ClientId, ServiceName = eventData.ServiceName });
            throw;
        }
    }

    #endregion

    #region Helper Methods for Event Handling

    /// <summary>
    /// Handles client events received from RabbitMQ and routes them to WebSocket connections.
    /// This is the callback invoked by lib-messaging's SubscribeDynamicRawAsync when events arrive.
    /// Internal events (like CapabilitiesRefreshEvent) are handled locally without forwarding to client.
    /// </summary>
    /// <remarks>
    /// <para>
    /// IMPORTANT: This method throws exceptions on delivery failure to trigger RabbitMQ message requeue.
    /// The lib-messaging subscriber NACKs with requeue=false when this method throws.
    /// Message buffering relies on RabbitMQ queue persistence during disconnect windows.
    /// </para>
    /// <para>
    /// Delivery failure cases:
    /// - Client disconnected: Message requeued, delivered on reconnect
    /// - WebSocket send failed: Message requeued, retried on next connection
    /// - Internal event handling failed: Message requeued for retry
    /// </para>
    /// </remarks>
    private async Task HandleClientEventAsync(string sessionId, byte[] eventPayload)
    {
        // Check if this is an internal event that should be handled locally, not forwarded to client
        if (await TryHandleInternalEventAsync(sessionId, eventPayload))
        {
            // Internal event was handled - don't forward to client
            return;
        }

        // Validate event against whitelist and normalize the event_name
        // NSwag/JSON serialization can mangle event names (e.g., "system.notification" -> "system_notification")
        // We need to validate the mangled name and rewrite it to canonical form before sending to client
        var (canonicalName, clientPayload) = ClientEventNormalizer.NormalizeEventPayload(eventPayload);

        if (string.IsNullOrEmpty(canonicalName))
        {
            // Invalid event - don't requeue, just discard
            _logger.LogWarning("Rejected client event for session {SessionId} - event name not in whitelist or missing",
                sessionId);
            return;
        }

        _logger.LogDebug("Validated and normalized client event '{CanonicalName}' for session {SessionId}",
            canonicalName, sessionId);

        // Check if session has active WebSocket connection
        var connection = _connectionManager.GetConnection(sessionId);
        if (connection == null)
        {
            // Client disconnected - throw to trigger NACK with requeue
            // RabbitMQ will buffer the message until client reconnects
            _logger.LogDebug("Session {SessionId} not connected - message will be requeued by RabbitMQ",
                sessionId);
            throw new InvalidOperationException($"Client not connected for session {sessionId} - requeue for later delivery");
        }

        // Create binary message for the client event
        var eventMessage = new BinaryMessage(
            flags: MessageFlags.Event, // Server-initiated event, no response expected
            channel: 0, // Event channel
            sequenceNumber: 0, // Events don't need sequence numbers
            serviceGuid: Guid.Empty, // System event
            messageId: GuidGenerator.GenerateMessageId(),
            payload: clientPayload
        );

        // Send to WebSocket client
        var sent = await _connectionManager.SendMessageAsync(sessionId, eventMessage, CancellationToken.None);

        if (sent)
        {
            _logger.LogDebug("Delivered client event to session {SessionId} ({PayloadSize} bytes)",
                sessionId, eventPayload.Length);
        }
        else
        {
            // Send failed - client may have disconnected between check and send
            // Throw to trigger NACK with requeue
            _logger.LogDebug("WebSocket send failed for session {SessionId} - message will be requeued by RabbitMQ",
                sessionId);
            throw new InvalidOperationException($"WebSocket send failed for session {sessionId} - requeue for later delivery");
        }
    }

    /// <summary>
    /// Attempts to handle internal events that should not be forwarded to clients.
    /// Returns true if the event was handled internally, false if it should be forwarded to the client.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Internal events like CapabilitiesRefreshEvent are used for service-to-Connect communication
    /// via session-specific RabbitMQ channels. These events trigger internal actions (like refreshing
    /// capabilities) but should NOT be sent to the WebSocket client.
    /// </para>
    /// </remarks>
    private async Task<bool> TryHandleInternalEventAsync(string sessionId, byte[] eventPayload)
    {
        try
        {
            // Try to parse just the event_name field to determine event type
            using var doc = JsonDocument.Parse(eventPayload);
            var root = doc.RootElement;

            // Unwrap MassTransit envelope - MassTransit wraps messages with metadata,
            // and the actual event data is in the "message" property
            if (root.TryGetProperty("message", out var messageElement) &&
                messageElement.ValueKind == JsonValueKind.Object)
            {
                root = messageElement;
            }

            if (!root.TryGetProperty("eventName", out var eventNameElement))
            {
                return false; // Not a valid event, let normal handling proceed
            }

            var eventName = eventNameElement.GetString();
            if (string.IsNullOrEmpty(eventName))
            {
                return false;
            }

            // Handle SessionCapabilitiesEvent - extract permissions and send to client
            // Check both the EnumMember value and the C# enum name (JsonStringEnumConverter uses C# name)
            if (eventName == "permissions.session_capabilities" ||
                eventName == "Permissions_session_capabilities")
            {
                _logger.LogDebug("Handling SessionCapabilitiesEvent for session {SessionId}", sessionId);

                // Extract permissions from event payload - no API call needed
                if (root.TryGetProperty("permissions", out var permissionsElement) ||
                    root.TryGetProperty("Permissions", out permissionsElement))
                {
                    var permissions = new Dictionary<string, List<string>>();
                    foreach (var service in permissionsElement.EnumerateObject())
                    {
                        var methods = new List<string>();
                        foreach (var method in service.Value.EnumerateArray())
                        {
                            var methodStr = method.GetString();
                            if (!string.IsNullOrEmpty(methodStr))
                            {
                                methods.Add(methodStr);
                            }
                        }
                        permissions[service.Name] = methods;
                    }

                    // Extract reason if present
                    var reason = "capabilities_updated";
                    if (root.TryGetProperty("reason", out var reasonElement) ||
                        root.TryGetProperty("Reason", out reasonElement))
                    {
                        reason = reasonElement.GetString() ?? reason;
                    }

                    await ProcessCapabilitiesAsync(sessionId, permissions, reason);
                }
                else
                {
                    _logger.LogWarning("SessionCapabilitiesEvent missing permissions for session {SessionId}", sessionId);
                }

                return true; // Event handled internally
            }

            // Handle ShortcutPublishedEvent - add shortcut to session and update manifest
            if (eventName == "session.shortcut_published" || eventName == "Session_shortcut_published")
            {
                _logger.LogDebug("Handling ShortcutPublishedEvent for session {SessionId}", sessionId);
                await HandleShortcutPublishedAsync(sessionId, root);
                return true; // Event handled internally
            }

            // Handle ShortcutRevokedEvent - remove shortcut(s) and update manifest
            if (eventName == "session.shortcut_revoked" || eventName == "Session_shortcut_revoked")
            {
                _logger.LogDebug("Handling ShortcutRevokedEvent for session {SessionId}", sessionId);
                await HandleShortcutRevokedAsync(sessionId, root);
                return true; // Event handled internally
            }

            return false; // Not an internal event, forward to client
        }
        catch (JsonException)
        {
            // Not valid JSON - let normal handling proceed (it will likely fail, but consistently)
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking for internal event for session {SessionId}", sessionId);
            return false;
        }
    }

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
            _ = PublishErrorEventAsync("SendMessage", ex.GetType().Name, ex.Message, details: new { SessionId = sessionId });
            return false;
        }
    }

    /// <summary>
    /// Disconnects a session's WebSocket connection with a reason.
    /// This is a forced disconnect that does NOT allow reconnection.
    /// </summary>
    internal async Task DisconnectAsync(string sessionId, string reason)
    {
        try
        {
            var connection = _connectionManager.GetConnection(sessionId);
            if (connection != null)
            {
                // Mark as forced disconnect - no reconnection allowed
                connection.Metadata["forced_disconnect"] = true;

                if (connection.WebSocket.State == System.Net.WebSockets.WebSocketState.Open)
                {
                    await connection.WebSocket.CloseAsync(
                        System.Net.WebSockets.WebSocketCloseStatus.NormalClosure,
                        reason,
                        CancellationToken.None);
                }
            }
            _connectionManager.RemoveConnection(sessionId);

            // Clean up Redis session data (forced disconnect = no reconnection)
            await _sessionManager.RemoveSessionAsync(sessionId);

            _logger.LogInformation("Force disconnected session {SessionId}: {Reason}", sessionId, reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disconnecting session {SessionId}: {Reason}", sessionId, reason);
            _ = PublishErrorEventAsync("Disconnect", ex.GetType().Name, ex.Message, details: new { SessionId = sessionId, Reason = reason });
        }
    }

    /// <summary>
    /// Validates that a session still exists and is valid by checking the connection manager.
    /// </summary>
    private async Task<bool> ValidateSessionAsync(string sessionId)
    {
        await Task.CompletedTask; // Satisfy async requirement for sync method
        try
        {
            // Check if session exists in our connection manager
            // This is used for token refresh events where we only have session ID
            var connection = _connectionManager.GetConnection(sessionId);
            return connection != null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Session validation failed for {SessionId}", sessionId);
            return false;
        }
    }

    /// <summary>
    /// Placeholder for session capability initialization. Capabilities are delivered via
    /// SessionCapabilitiesEvent from Permissions service after session.connected event.
    /// This method exists for reconnection scenarios where existing mappings may be restored.
    /// </summary>
    internal async Task<Dictionary<string, Guid>> InitializeSessionCapabilitiesAsync(
        string sessionId,
        string? role = "anonymous",
        ICollection<string>? authorizations = null,
        CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask; // Satisfy async requirement for sync method
        // Capabilities are delivered via event-driven flow:
        // 1. Connect publishes session.connected with roles/authorizations
        // 2. Permissions receives event, compiles capabilities, publishes SessionCapabilitiesEvent
        // 3. Connect receives SessionCapabilitiesEvent and calls ProcessCapabilitiesAsync
        //
        // For reconnection scenarios, existing mappings are loaded from session manager.
        // For new sessions, return empty mappings - capabilities arrive via event.

        _logger.LogInformation("Session {SessionId} will receive capabilities via event (role: {Role}, auth count: {AuthCount})",
            sessionId, role ?? "anonymous", authorizations?.Count ?? 0);

        // Return empty mappings - real capabilities come via SessionCapabilitiesEvent
        return new Dictionary<string, Guid>();
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
            // INTERNAL format: "serviceName:METHOD:/path" - used for server-side routing
            // CLIENT format: "METHOD:/path" - exposed in capability manifest (no service name leak)
            var availableApis = new List<object>();

            foreach (var mapping in connectionState.ServiceMappings)
            {
                // Parse the internal endpoint key format: "serviceName:METHOD:/path"
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

                // CRITICAL: Skip endpoints with path templates (e.g., /accounts/{accountId})
                // WebSocket binary protocol requires POST endpoints with JSON body parameters
                // Template paths would require Connect to parse the payload, breaking zero-copy routing
                if (path.Contains('{'))
                {
                    _logger.LogDebug("Skipping template endpoint from capability manifest: {EndpointKey}", endpointKey);
                    continue;
                }

                // Only expose POST endpoints to WebSocket clients
                // GET and other HTTP methods are not supported through WebSocket binary protocol
                // Services can still publish GET endpoints in their registration events for other uses
                if (method != "POST")
                {
                    _logger.LogDebug("Skipping non-POST endpoint from capability manifest: {EndpointKey}", endpointKey);
                    continue;
                }

                availableApis.Add(new
                {
                    serviceGuid = guid.ToString(),
                    method = method,
                    path = path,
                    endpointKey = $"{method}:{path}",
                    // Informational only - not used for routing (routing uses serviceGuid)
                    serviceName = serviceName
                });
            }

            // Add shortcuts to availableAPIs - they look identical to regular endpoints from client perspective
            foreach (var shortcut in connectionState.GetAllShortcuts())
            {
                // Skip expired shortcuts
                if (shortcut.IsExpired)
                {
                    connectionState.RemoveShortcut(shortcut.RouteGuid);
                    continue;
                }

                // Shortcuts appear as regular APIs with "SHORTCUT:" prefix in endpointKey
                availableApis.Add(new
                {
                    serviceGuid = shortcut.RouteGuid.ToString(),
                    method = "SHORTCUT",
                    path = shortcut.Name ?? shortcut.RouteGuid.ToString(),
                    endpointKey = $"SHORTCUT:{shortcut.Name ?? shortcut.RouteGuid.ToString()}",
                    description = shortcut.Description ?? $"Shortcut to {shortcut.Name}"
                });
            }

            var capabilityManifest = new
            {
                eventName = "connect.capability_manifest",
                sessionId = sessionId,
                availableAPIs = availableApis,
                version = 1,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                peerGuid = connectionState.PeerGuid.ToString()
            };

            var manifestJson = BannouJson.Serialize(capabilityManifest);
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
                "Sending capability manifest to session {SessionId} with {ApiCount} available APIs (including shortcuts)",
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
            await PublishErrorEventAsync("SendCapabilityManifest", ex.GetType().Name, ex.Message, details: new { SessionId = sessionId });
            // Don't throw - capability manifest is informational, connection can continue
        }
    }

    /// <summary>
    /// Processes capabilities received from Permissions service via SessionCapabilitiesEvent.
    /// Generates client-salted GUIDs, updates connection state, and sends manifest to client.
    /// NO API call to Permissions - capabilities are passed directly from the event.
    /// </summary>
    private async Task ProcessCapabilitiesAsync(string sessionId, Dictionary<string, List<string>> permissions, string reason, CancellationToken cancellationToken = default)
    {
        try
        {
            var connection = _connectionManager.GetConnection(sessionId);
            if (connection == null)
            {
                _logger.LogDebug("No active connection found for session {SessionId}, skipping capability processing", sessionId);
                return;
            }

            // Generate client-salted GUIDs for each service:method combination
            var connectionState = connection.ConnectionState;
            var newMappings = new Dictionary<string, Guid>();

            foreach (var servicePermissions in permissions)
            {
                var serviceName = servicePermissions.Key;
                var methods = servicePermissions.Value;

                foreach (var method in methods)
                {
                    var endpointKey = $"{serviceName}:{method}";
                    var guid = GuidGenerator.GenerateServiceGuid(endpointKey, sessionId, _serverSalt);
                    newMappings[endpointKey] = guid;
                }
            }

            // Atomic update to prevent race conditions
            connectionState.UpdateAllServiceMappings(newMappings);

            // Build and send updated capability manifest
            // INTERNAL format: "serviceName:METHOD:/path" - used for server-side routing
            // CLIENT format: "METHOD:/path" - exposed in capability manifest (no service name leak)
            var availableApis = new List<object>();
            foreach (var mapping in newMappings)
            {
                // Parse the internal endpoint key format
                var endpointKey = mapping.Key;
                var guid = mapping.Value;

                var firstColon = endpointKey.IndexOf(':');
                if (firstColon <= 0) continue;

                var serviceName = endpointKey[..firstColon];
                var methodAndPath = endpointKey[(firstColon + 1)..];
                var methodPathColon = methodAndPath.IndexOf(':');
                var method = methodPathColon > 0 ? methodAndPath[..methodPathColon] : methodAndPath;
                var path = methodPathColon > 0 ? methodAndPath[(methodPathColon + 1)..] : "";

                // Skip endpoints with path templates - WebSocket requires POST with JSON body
                if (path.Contains('{'))
                {
                    continue;
                }

                // Only expose POST endpoints to WebSocket clients
                // GET and other HTTP methods are not supported through WebSocket binary protocol
                if (method != "POST")
                {
                    continue;
                }

                availableApis.Add(new
                {
                    serviceGuid = guid.ToString(),
                    method = method,
                    path = path,
                    endpointKey = $"{method}:{path}",
                    // Informational only - not used for routing (routing uses serviceGuid)
                    serviceName = serviceName
                });
            }

            // Add shortcuts to availableAPIs - they look identical to regular endpoints from client perspective
            foreach (var shortcut in connectionState.GetAllShortcuts())
            {
                // Skip expired shortcuts
                if (shortcut.IsExpired)
                {
                    connectionState.RemoveShortcut(shortcut.RouteGuid);
                    continue;
                }

                // Shortcuts appear as regular APIs with "SHORTCUT:" prefix in endpointKey
                availableApis.Add(new
                {
                    serviceGuid = shortcut.RouteGuid.ToString(),
                    method = "SHORTCUT",
                    path = shortcut.Name ?? shortcut.RouteGuid.ToString(),
                    endpointKey = $"SHORTCUT:{shortcut.Name ?? shortcut.RouteGuid.ToString()}",
                    description = shortcut.Description ?? $"Shortcut to {shortcut.Name}"
                });
            }

            var capabilityManifest = new
            {
                eventName = "connect.capability_manifest",
                sessionId = sessionId,
                availableAPIs = availableApis,
                version = 1,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                reason = reason
            };

            var manifestJson = BannouJson.Serialize(capabilityManifest);
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
                "Processed capabilities for session {SessionId}: {ApiCount} APIs (including shortcuts) (reason: {Reason})",
                sessionId, availableApis.Count, reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process capabilities for session {SessionId}", sessionId);
            await PublishErrorEventAsync("ProcessCapabilities", ex.GetType().Name, ex.Message, details: new { SessionId = sessionId, Reason = reason });
        }
    }

    /// <summary>
    /// Disconnects a WebSocket session due to session invalidation (logout, account deletion, etc.).
    /// Sends a close message with the reason and removes the connection.
    /// This is a forced disconnect that does NOT allow reconnection.
    /// </summary>
    public async Task DisconnectSessionAsync(string sessionId, string reason, CancellationToken cancellationToken = default)
    {
        WebSocketConnection? connection = null;
        try
        {
            connection = _connectionManager.GetConnection(sessionId);
            if (connection == null)
            {
                _logger.LogDebug("Session {SessionId} not found for disconnection (may already be disconnected)", sessionId);
                // Still clean up Redis in case session exists there
                await _sessionManager.RemoveSessionAsync(sessionId);
                return;
            }

            // Mark as forced disconnect - no reconnection allowed
            connection.Metadata["forced_disconnect"] = true;

            if (connection.WebSocket.State == System.Net.WebSockets.WebSocketState.Open)
            {
                // Send close message with reason
                await connection.WebSocket.CloseAsync(
                    System.Net.WebSockets.WebSocketCloseStatus.NormalClosure,
                    $"Session invalidated: {reason}",
                    cancellationToken);

                _logger.LogInformation("Force disconnected session {SessionId} due to: {Reason}", sessionId, reason);
            }
            else
            {
                _logger.LogDebug("Session {SessionId} WebSocket not in Open state ({State}), skipping close",
                    sessionId, connection.WebSocket.State);
            }

            // Remove from connection manager - use instance-matching to ensure we only
            // remove the connection we fetched, not a potential replacement
            _connectionManager.RemoveConnectionIfMatch(sessionId, connection.WebSocket);

            // Clean up Redis session data (forced disconnect = no reconnection)
            await _sessionManager.RemoveSessionAsync(sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disconnecting session {SessionId}: {Reason}", sessionId, reason);
            // Still try to remove from connection manager even if close fails
            // Use instance-matching if we have a valid connection reference
            if (connection != null)
            {
                _connectionManager.RemoveConnectionIfMatch(sessionId, connection.WebSocket);
            }
        }
    }

    #endregion

    #region Permission Registration

    /// <summary>
    /// Registers this service's API permissions with the Permissions service on startup.
    /// Overrides the default IBannouService implementation to use generated permission data.
    /// </summary>
    public async Task RegisterServicePermissionsAsync()
    {
        _logger.LogInformation("Registering Connect service permissions... (starting)");
        try
        {
            await ConnectPermissionRegistration.RegisterViaEventAsync(_messageBus, _logger);
            _logger.LogInformation("Connect service permissions registered via event (complete)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register Connect service permissions");
            await PublishErrorEventAsync("RegisterServicePermissions", ex.GetType().Name, ex.Message, dependency: "permissions");
            throw;
        }
    }

    #endregion

    #region Session Shortcuts

    /// <summary>
    /// Handles ShortcutPublishedEvent - adds or updates a shortcut in the session.
    /// The shortcut is stored in ConnectionState and included in capability manifests.
    /// </summary>
    private async Task HandleShortcutPublishedAsync(string sessionId, JsonElement root)
    {
        try
        {
            var connection = _connectionManager.GetConnection(sessionId);
            if (connection == null)
            {
                _logger.LogDebug("No active connection for session {SessionId}, ignoring shortcut publish", sessionId);
                return;
            }

            var connectionState = connection.ConnectionState;

            // Parse the shortcut from the event payload
            if (!root.TryGetProperty("shortcut", out var shortcutElement) &&
                !root.TryGetProperty("Shortcut", out shortcutElement))
            {
                _logger.LogWarning("ShortcutPublishedEvent missing shortcut for session {SessionId}", sessionId);
                return;
            }

            // Extract routeGuid
            if (!shortcutElement.TryGetProperty("routeGuid", out var routeGuidElement))
            {
                _logger.LogWarning("Shortcut missing routeGuid for session {SessionId}", sessionId);
                return;
            }

            if (!Guid.TryParse(routeGuidElement.GetString(), out var routeGuid))
            {
                _logger.LogWarning("Invalid routeGuid format for session {SessionId}", sessionId);
                return;
            }

            // Extract targetGuid
            if (!shortcutElement.TryGetProperty("targetGuid", out var targetGuidElement))
            {
                _logger.LogWarning("Shortcut missing targetGuid for session {SessionId}", sessionId);
                return;
            }

            if (!Guid.TryParse(targetGuidElement.GetString(), out var targetGuid))
            {
                _logger.LogWarning("Invalid targetGuid format for session {SessionId}", sessionId);
                return;
            }

            // Extract boundPayload (can be base64 encoded or raw JSON string)
            byte[] boundPayload = Array.Empty<byte>();
            if (shortcutElement.TryGetProperty("boundPayload", out var payloadElement))
            {
                var payloadStr = payloadElement.GetString();
                if (!string.IsNullOrEmpty(payloadStr))
                {
                    // Try to decode as base64, fall back to UTF8 encoding of raw string
                    try
                    {
                        boundPayload = Convert.FromBase64String(payloadStr);
                    }
                    catch (FormatException)
                    {
                        // Not base64, treat as raw JSON string
                        boundPayload = Encoding.UTF8.GetBytes(payloadStr);
                    }
                }
            }

            // Extract metadata
            var hasMetadata = shortcutElement.TryGetProperty("metadata", out JsonElement metadataElement) ||
                            shortcutElement.TryGetProperty("Metadata", out metadataElement);

            var shortcutData = new SessionShortcutData
            {
                RouteGuid = routeGuid,
                TargetGuid = targetGuid,
                BoundPayload = boundPayload,
                CreatedAt = DateTimeOffset.UtcNow
            };

            if (hasMetadata)
            {
                // Parse name
                if (metadataElement.TryGetProperty("name", out var nameElement) ||
                    metadataElement.TryGetProperty("Name", out nameElement))
                {
                    shortcutData.Name = nameElement.GetString() ?? string.Empty;
                }

                // Parse sourceService
                if (metadataElement.TryGetProperty("sourceService", out var sourceElement))
                {
                    shortcutData.SourceService = sourceElement.GetString() ?? string.Empty;
                }

                // Parse targetService (required for shortcut-only endpoints)
                if (metadataElement.TryGetProperty("targetService", out var targetServiceElement))
                {
                    shortcutData.TargetService = targetServiceElement.GetString() ?? string.Empty;
                }

                // Parse targetMethod (required for routing, e.g., "POST")
                if (metadataElement.TryGetProperty("targetMethod", out var targetMethodElement))
                {
                    shortcutData.TargetMethod = targetMethodElement.GetString() ?? string.Empty;
                }

                // Parse targetEndpoint (required for routing, e.g., "/sessions/join")
                if (metadataElement.TryGetProperty("targetEndpoint", out var targetEndpointElement))
                {
                    shortcutData.TargetEndpoint = targetEndpointElement.GetString() ?? string.Empty;
                }

                // Parse optional fields
                if (metadataElement.TryGetProperty("description", out var descElement) ||
                    metadataElement.TryGetProperty("Description", out descElement))
                {
                    shortcutData.Description = descElement.GetString();
                }

                if (metadataElement.TryGetProperty("displayName", out var displayElement))
                {
                    shortcutData.DisplayName = displayElement.GetString();
                }

                if (metadataElement.TryGetProperty("expires_at", out var expiresElement) ||
                    metadataElement.TryGetProperty("Expires_at", out expiresElement))
                {
                    if (DateTimeOffset.TryParse(expiresElement.GetString(), out var expiresAt))
                    {
                        shortcutData.ExpiresAt = expiresAt;
                    }
                }

                if (metadataElement.TryGetProperty("tags", out var tagsElement) ||
                    metadataElement.TryGetProperty("Tags", out tagsElement))
                {
                    var tags = new List<string>();
                    foreach (var tag in tagsElement.EnumerateArray())
                    {
                        var tagStr = tag.GetString();
                        if (!string.IsNullOrEmpty(tagStr))
                        {
                            tags.Add(tagStr);
                        }
                    }
                    shortcutData.Tags = tags.ToArray();
                }
            }

            // Shortcuts MUST have all routing fields in metadata - no fallback guessing
            if (string.IsNullOrEmpty(shortcutData.TargetService))
            {
                _logger.LogError("Shortcut '{ShortcutName}' missing required target_service in metadata for session {SessionId}. " +
                    "The shortcut publisher must provide target_service.",
                    shortcutData.Name, sessionId);
                await PublishErrorEventAsync("HandleShortcutPublished", "missing_target_service", $"Shortcut '{shortcutData.Name}' missing required target_service", details: new { SessionId = sessionId, ShortcutName = shortcutData.Name });
                return; // Reject invalid shortcut
            }

            if (string.IsNullOrEmpty(shortcutData.TargetMethod))
            {
                _logger.LogError("Shortcut '{ShortcutName}' missing required target_method in metadata for session {SessionId}. " +
                    "The shortcut publisher must provide target_method (e.g., 'POST').",
                    shortcutData.Name, sessionId);
                await PublishErrorEventAsync("HandleShortcutPublished", "missing_target_method", $"Shortcut '{shortcutData.Name}' missing required target_method", details: new { SessionId = sessionId, ShortcutName = shortcutData.Name });
                return; // Reject invalid shortcut
            }

            if (string.IsNullOrEmpty(shortcutData.TargetEndpoint))
            {
                _logger.LogError("Shortcut '{ShortcutName}' missing required target_endpoint in metadata for session {SessionId}. " +
                    "The shortcut publisher must provide target_endpoint (e.g., '/sessions/join').",
                    shortcutData.Name, sessionId);
                await PublishErrorEventAsync("HandleShortcutPublished", "missing_target_endpoint", $"Shortcut '{shortcutData.Name}' missing required target_endpoint", details: new { SessionId = sessionId, ShortcutName = shortcutData.Name });
                return; // Reject invalid shortcut
            }

            // Add or update the shortcut in connection state
            connectionState.AddOrUpdateShortcut(shortcutData);

            _logger.LogInformation("Added shortcut '{ShortcutName}' ({RouteGuid}) for session {SessionId}",
                shortcutData.Name, routeGuid, sessionId);

            // Send updated capability manifest with shortcuts to client
            await SendCapabilityManifestWithShortcutsAsync(connection.WebSocket, sessionId, connectionState, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling ShortcutPublishedEvent for session {SessionId}", sessionId);
            await PublishErrorEventAsync("HandleShortcutPublished", ex.GetType().Name, ex.Message, details: new { SessionId = sessionId });
        }
    }

    /// <summary>
    /// Handles ShortcutRevokedEvent - removes shortcut(s) from the session.
    /// Supports both single-shortcut revocation and bulk revocation by source service.
    /// </summary>
    private async Task HandleShortcutRevokedAsync(string sessionId, JsonElement root)
    {
        try
        {
            var connection = _connectionManager.GetConnection(sessionId);
            if (connection == null)
            {
                _logger.LogDebug("No active connection for session {SessionId}, ignoring shortcut revocation", sessionId);
                return;
            }

            var connectionState = connection.ConnectionState;
            var removedCount = 0;

            // Extract reason for logging
            string? reason = null;
            if (root.TryGetProperty("reason", out var reasonElement) ||
                root.TryGetProperty("Reason", out reasonElement))
            {
                reason = reasonElement.GetString();
            }

            // Check for single shortcut revocation by routeGuid
            if (root.TryGetProperty("routeGuid", out var routeGuidElement))
            {
                var routeGuidStr = routeGuidElement.GetString();
                if (!string.IsNullOrEmpty(routeGuidStr) && Guid.TryParse(routeGuidStr, out var routeGuid))
                {
                    if (connectionState.RemoveShortcut(routeGuid))
                    {
                        removedCount = 1;
                        _logger.LogInformation("Revoked shortcut {RouteGuid} for session {SessionId}: {Reason}",
                            routeGuid, sessionId, reason ?? "no reason");
                    }
                    else
                    {
                        _logger.LogDebug("Shortcut {RouteGuid} not found for session {SessionId}", routeGuid, sessionId);
                    }
                }
            }
            // Check for bulk revocation by source service
            else if (root.TryGetProperty("revokeByService", out var serviceElement))
            {
                var sourceService = serviceElement.GetString();
                if (!string.IsNullOrEmpty(sourceService))
                {
                    removedCount = connectionState.RemoveShortcutsByService(sourceService);
                    _logger.LogInformation("Revoked {Count} shortcuts from service '{SourceService}' for session {SessionId}: {Reason}",
                        removedCount, sourceService, sessionId, reason ?? "no reason");
                }
            }
            else
            {
                _logger.LogWarning("ShortcutRevokedEvent missing both routeGuid and revokeByService for session {SessionId}", sessionId);
                return;
            }

            // Send updated capability manifest if shortcuts were removed
            if (removedCount > 0)
            {
                await SendCapabilityManifestWithShortcutsAsync(connection.WebSocket, sessionId, connectionState, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling ShortcutRevokedEvent for session {SessionId}", sessionId);
            await PublishErrorEventAsync("HandleShortcutRevoked", ex.GetType().Name, ex.Message, details: new { SessionId = sessionId });
        }
    }

    /// <summary>
    /// Sends capability manifest including session shortcuts to the client.
    /// This is the unified method for sending manifests that include both APIs and shortcuts.
    /// </summary>
    private async Task SendCapabilityManifestWithShortcutsAsync(
        WebSocket webSocket,
        string sessionId,
        ConnectionState connectionState,
        CancellationToken cancellationToken)
    {
        try
        {
            // Build the capability manifest with available APIs
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

                // Skip template endpoints (zero-copy routing requirement)
                if (path.Contains('{')) continue;

                // Only expose POST endpoints to WebSocket clients
                if (method != "POST") continue;

                availableApis.Add(new
                {
                    serviceGuid = guid.ToString(),
                    method = method,
                    path = path,
                    endpointKey = $"{method}:{path}",
                    serviceName = serviceName
                });
            }

            // Add shortcuts to availableAPIs - they look identical to regular endpoints from client perspective
            foreach (var shortcut in connectionState.GetAllShortcuts())
            {
                // Skip expired shortcuts
                if (shortcut.IsExpired)
                {
                    connectionState.RemoveShortcut(shortcut.RouteGuid);
                    continue;
                }

                // Shortcuts appear as regular APIs with "SHORTCUT:" prefix in endpointKey
                availableApis.Add(new
                {
                    serviceGuid = shortcut.RouteGuid.ToString(),
                    method = "SHORTCUT",
                    path = shortcut.Name ?? shortcut.RouteGuid.ToString(),
                    endpointKey = $"SHORTCUT:{shortcut.Name ?? shortcut.RouteGuid.ToString()}",
                    description = shortcut.Description ?? $"Shortcut to {shortcut.Name}"
                });
            }

            var capabilityManifest = new
            {
                eventName = "connect.capability_manifest",
                sessionId = sessionId,
                availableAPIs = availableApis,
                version = 1,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                peerGuid = connectionState.PeerGuid.ToString()
            };

            var manifestJson = BannouJson.Serialize(capabilityManifest);
            var manifestBytes = Encoding.UTF8.GetBytes(manifestJson);

            var capabilityMessage = new BinaryMessage(
                flags: MessageFlags.Event,
                channel: 0,
                sequenceNumber: 0,
                serviceGuid: Guid.Empty,
                messageId: GuidGenerator.GenerateMessageId(),
                payload: manifestBytes
            );

            var messageBytes = capabilityMessage.ToByteArray();

            _logger.LogInformation(
                "Sending capability manifest to session {SessionId} with {ApiCount} APIs (including shortcuts)",
                sessionId, availableApis.Count);

            await webSocket.SendAsync(
                new ArraySegment<byte>(messageBytes),
                WebSocketMessageType.Binary,
                endOfMessage: true,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send capability manifest with shortcuts to session {SessionId}", sessionId);
            await PublishErrorEventAsync("SendCapabilityManifestWithShortcuts", ex.GetType().Name, ex.Message, details: new { SessionId = sessionId });
        }
    }

    #endregion

    #region Error Event Publishing

    /// <summary>
    /// Publishes an error event for unexpected/internal failures.
    /// Does NOT publish for validation errors or expected failure cases.
    /// </summary>
    private async Task PublishErrorEventAsync(
        string operation,
        string errorType,
        string message,
        string? dependency = null,
        object? details = null)
    {
        await _messageBus.TryPublishErrorAsync(
            serviceName: "connect",
            operation: operation,
            errorType: errorType,
            message: message,
            dependency: dependency,
            details: details);
    }

    /// <summary>
    /// Maps HTTP status codes to WebSocket ResponseCodes for proper error reporting.
    /// </summary>
    private static ResponseCodes MapHttpStatusToResponseCode(System.Net.HttpStatusCode? statusCode)
    {
        if (statusCode == null)
        {
            return ResponseCodes.Service_InternalServerError;
        }

        return statusCode.Value switch
        {
            System.Net.HttpStatusCode.OK => ResponseCodes.OK,
            System.Net.HttpStatusCode.Created => ResponseCodes.OK,
            System.Net.HttpStatusCode.Accepted => ResponseCodes.OK,
            System.Net.HttpStatusCode.NoContent => ResponseCodes.OK,
            System.Net.HttpStatusCode.BadRequest => ResponseCodes.Service_BadRequest,
            System.Net.HttpStatusCode.Unauthorized => ResponseCodes.Service_Unauthorized,
            System.Net.HttpStatusCode.Forbidden => ResponseCodes.Service_Unauthorized,
            System.Net.HttpStatusCode.NotFound => ResponseCodes.Service_NotFound,
            System.Net.HttpStatusCode.Conflict => ResponseCodes.Service_Conflict,
            _ => ResponseCodes.Service_InternalServerError
        };
    }

    #endregion

}
