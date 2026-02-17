using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Connect.Helpers;
using BeyondImmersion.BannouService.Connect.Protocol;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Meta;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BeyondImmersion.BannouService.Connect;

/// <summary>
/// WebSocket-first edge gateway service providing zero-copy message routing.
/// Uses Permission service for dynamic API discovery and capability management.
/// Implements IDisposable to enable graceful shutdown with WebSocket close frames.
/// </summary>
[BannouService("connect", typeof(IConnectService), lifetime: ServiceLifetime.Singleton, layer: ServiceLayer.AppFoundation)]
public partial class ConnectService : IConnectService, IDisposable
{
    private readonly IAuthClient _authClient;
    private readonly IMeshInvocationClient _meshClient;
    private readonly IMessageBus _messageBus;
    private readonly IServiceAppMappingResolver _appMappingResolver;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<ConnectService> _logger;
    private readonly WebSocketConnectionManager _connectionManager;
    private readonly ISessionManager _sessionManager;
    private readonly ConnectServiceConfiguration _configuration;
    private readonly ICapabilityManifestBuilder _manifestBuilder;
    private readonly IEntitySessionRegistry _entitySessionRegistry;

    // Client event subscriptions via lib-messaging (per-session raw byte subscriptions)
    private readonly IMessageSubscriber _messageSubscriber;
    private readonly ConcurrentDictionary<string, IAsyncDisposable> _sessionSubscriptions = new();
    private readonly ILoggerFactory _loggerFactory;

    // Pending RPCs awaiting client responses (MessageId -> RPC info for response forwarding)
    private readonly ConcurrentDictionary<ulong, PendingRPCInfo> _pendingRPCs = new();
    private Timer? _pendingRPCCleanupTimer;

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

    // Reconnection window now comes from configuration (ReconnectionWindowSeconds)
    // During this time after disconnect, session state is preserved and client can reconnect

    private readonly string _serverSalt;
    private readonly Guid _instanceId;

    // Connection mode configuration
    private readonly ConnectionMode _connectionMode;
    private readonly InternalAuthMode _internalAuthMode;
    private readonly string? _internalServiceToken;

    public ConnectService(
        IAuthClient authClient,
        IMeshInvocationClient meshClient,
        IMessageBus messageBus,
        IMessageSubscriber messageSubscriber,
        IServiceAppMappingResolver appMappingResolver,
        IServiceScopeFactory serviceScopeFactory,
        ConnectServiceConfiguration configuration,
        ILogger<ConnectService> logger,
        ILoggerFactory loggerFactory,
        IEventConsumer eventConsumer,
        ISessionManager sessionManager,
        ICapabilityManifestBuilder manifestBuilder,
        IEntitySessionRegistry entitySessionRegistry)
    {
        _authClient = authClient;
        _meshClient = meshClient;
        _messageBus = messageBus;
        _messageSubscriber = messageSubscriber;
        _appMappingResolver = appMappingResolver;
        _serviceScopeFactory = serviceScopeFactory;
        _configuration = configuration;
        _logger = logger;
        _loggerFactory = loggerFactory;
        _sessionManager = sessionManager;
        _manifestBuilder = manifestBuilder;
        _entitySessionRegistry = entitySessionRegistry;

        _connectionManager = new WebSocketConnectionManager(
            configuration.ConnectionShutdownTimeoutSeconds,
            configuration.ConnectionCleanupIntervalSeconds,
            configuration.InactiveConnectionTimeoutMinutes,
            _logger);

        // Server salt from configuration - REQUIRED (fail-fast for production safety)
        // All service instances must share the same salt for session shortcuts to work correctly
        if (string.IsNullOrEmpty(configuration.ServerSalt))
        {
            throw new InvalidOperationException(
                "CONNECT_SERVERSALT is required. All service instances must share the same salt for session shortcuts to work correctly.");
        }
        _serverSalt = configuration.ServerSalt;

        // Generate unique instance ID for distributed deployment
        _instanceId = Guid.NewGuid();

        // Register event handlers via partial class (ConnectServiceEvents.cs)
        RegisterEventConsumers(eventConsumer);

        // Connection mode configuration
        _connectionMode = configuration.ConnectionMode;
        _internalAuthMode = configuration.InternalAuthMode;
        _internalServiceToken = string.IsNullOrEmpty(configuration.InternalServiceToken)
            ? null
            : configuration.InternalServiceToken;

        // Validate Internal mode configuration
        if (_connectionMode == ConnectionMode.Internal &&
            _internalAuthMode == InternalAuthMode.ServiceToken &&
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
        _logger.LogInformation("Processing internal proxy request to {TargetService}/{Method} {Endpoint}",
            body.TargetService, body.Method, body.TargetEndpoint);

        // Validate session has access to this API via connection state capability mappings
        // Capabilities are pushed via SessionCapabilitiesEvent from Permission service
        // and stored in ConnectionState.ServiceMappings (the active source of truth)
        // Key format: "serviceName:/path" (no HTTP method in key - all WebSocket endpoints are POST)
        var endpointKey = $"{body.TargetService}:{body.TargetEndpoint}";
        var connection = _connectionManager.GetConnection(body.SessionId.ToString());

        if (connection == null)
        {
            _logger.LogWarning("Session {SessionId} not found for internal proxy request to {Service}/{Method}",
                body.SessionId, body.TargetService, body.Method);
            return (StatusCodes.NotFound, null);
        }

        var hasAccess = connection.ConnectionState.HasServiceMapping(endpointKey);

        if (!hasAccess)
        {
            _logger.LogWarning("Session {SessionId} denied access to {Service}/{Method} - not in capability manifest",
                body.SessionId, body.TargetService, body.Method);
            return (StatusCodes.Forbidden, null);
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
                StatusCode = 503,
                Error = $"Service invocation failed: {meshEx.Message}",
                ExecutionTime = (int)(DateTime.UtcNow - startTime).TotalMilliseconds
            };

            return (StatusCodes.ServiceUnavailable, errorResponse);
        }
    }

    /// <summary>
    /// Permission-gated proxy for endpoint metadata over HTTP.
    /// Validates the caller's JWT, looks up their active WebSocket session via the JWT's sessionKey,
    /// checks the session's capability mappings for the underlying endpoint, and proxies the internal
    /// meta GET request if authorized. Returns the meta endpoint response directly.
    /// Requires an active WebSocket connection -- the session's compiled capability mappings in
    /// Connect's in-memory connection state are the permission source, exactly as for WebSocket meta requests.
    /// </summary>
    public async Task<(StatusCodes, GetEndpointMetaResponse?)> GetEndpointMetaAsync(
        GetEndpointMetaRequest body,
        CancellationToken cancellationToken = default)
    {
        // Step 1: Extract JWT from the HTTP request Authorization header
        // ConnectService is Singleton, so resolve IHttpContextAccessor from a scope
        string? authorization;
        await using (var httpScope = _serviceScopeFactory.CreateAsyncScope())
        {
            var httpContextAccessor = httpScope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
            authorization = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
        }

        if (string.IsNullOrEmpty(authorization) ||
            !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("GetEndpointMeta: Missing or invalid Authorization header");
            return (StatusCodes.Unauthorized, null);
        }

        var token = authorization.Substring(7);

        // Step 2: Validate JWT via Auth service to get the sessionKey
        var authClient = (AuthClient)_authClient;
        var validationResponse = await authClient
            .WithAuthorization(token)
            .ValidateTokenAsync(cancellationToken);

        if (validationResponse == null || !validationResponse.Valid || validationResponse.SessionKey == Guid.Empty)
        {
            _logger.LogWarning("GetEndpointMeta: JWT validation failed");
            return (StatusCodes.Unauthorized, null);
        }

        var sessionKey = validationResponse.SessionKey.ToString();

        // Step 3: Look up the existing WebSocket connection using the sessionKey
        var connection = _connectionManager.GetConnection(sessionKey);
        if (connection == null)
        {
            _logger.LogWarning("GetEndpointMeta: No active WebSocket connection for session {SessionKey}", sessionKey);
            return (StatusCodes.Unauthorized, null);
        }

        var connectionState = connection.ConnectionState;

        // Step 4: Parse the meta path to extract service name, base endpoint, and meta type
        // Expected format: "/account/get/meta/info" or "/character/create/meta/request-schema"
        var metaMarkerIndex = body.Path.IndexOf("/meta/", StringComparison.OrdinalIgnoreCase);
        if (metaMarkerIndex < 0)
        {
            _logger.LogWarning("GetEndpointMeta: Path missing /meta/ segment: {Path}", body.Path);
            return (StatusCodes.NotFound, null);
        }

        var basePath = body.Path[..metaMarkerIndex];
        var metaSuffix = body.Path[(metaMarkerIndex + 6)..]; // Skip "/meta/"

        // Validate meta suffix
        if (metaSuffix != "info" && metaSuffix != "request-schema" &&
            metaSuffix != "response-schema" && metaSuffix != "schema")
        {
            _logger.LogWarning("GetEndpointMeta: Invalid meta type suffix: {MetaSuffix}", metaSuffix);
            return (StatusCodes.NotFound, null);
        }

        // Extract service name from the first path segment (e.g., "/account/get" -> "account")
        var pathWithoutLeadingSlash = basePath.TrimStart('/');
        var firstSlash = pathWithoutLeadingSlash.IndexOf('/');
        if (firstSlash < 0)
        {
            _logger.LogWarning("GetEndpointMeta: Cannot extract service name from path: {BasePath}", basePath);
            return (StatusCodes.NotFound, null);
        }

        var serviceName = pathWithoutLeadingSlash[..firstSlash];

        // Step 5: Check capability mappings -- same check used by ProxyInternalRequestAsync and WebSocket routing
        // Key format: "serviceName:/path" (no HTTP method in key -- all WebSocket endpoints are POST)
        var endpointKey = $"{serviceName}:{basePath}";
        if (!connectionState.HasServiceMapping(endpointKey))
        {
            _logger.LogWarning("GetEndpointMeta: Session {SessionKey} lacks access to {EndpointKey}",
                sessionKey, endpointKey);
            return (StatusCodes.Forbidden, null);
        }

        // Step 6: Proxy the meta GET request via ServiceNavigator -- same routing as HandleMetaRequestAsync
        var metaPath = $"{basePath}/meta/{metaSuffix}";
        RawApiResult? apiResult;

        try
        {
            await using var scope = _serviceScopeFactory.CreateAsyncScope();
            var navigator = scope.ServiceProvider.GetRequiredService<IServiceNavigator>();

            apiResult = await navigator.ExecuteRawApiAsync(
                serviceName,
                metaPath,
                ReadOnlyMemory<byte>.Empty,
                HttpMethod.Get,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetEndpointMeta: Service invocation failed for {Service} {MetaPath}",
                serviceName, metaPath);
            await PublishErrorEventAsync("GetEndpointMeta", ex.GetType().Name, ex.Message,
                dependency: serviceName, details: new { MetaPath = metaPath });
            return (StatusCodes.ServiceUnavailable, null);
        }

        if (apiResult == null || !apiResult.IsSuccess || string.IsNullOrEmpty(apiResult.ResponseBody))
        {
            _logger.LogWarning("GetEndpointMeta: Meta endpoint returned non-success for {MetaPath}: {StatusCode}",
                metaPath, apiResult?.StatusCode);
            return (StatusCodes.NotFound, null);
        }

        // Step 7: Deserialize the internal MetaResponse and map to the generated response type
        var metaResponse = BannouJson.Deserialize<MetaResponse>(apiResult.ResponseBody);
        if (metaResponse == null)
        {
            _logger.LogError("GetEndpointMeta: Failed to deserialize MetaResponse from {MetaPath}", metaPath);
            await PublishErrorEventAsync("GetEndpointMeta", "deserialization_error",
                "Failed to deserialize MetaResponse", dependency: serviceName,
                details: new { MetaPath = metaPath });
            return (StatusCodes.ServiceUnavailable, null);
        }

        var response = new GetEndpointMetaResponse
        {
            MetaType = metaResponse.MetaType,
            ServiceName = metaResponse.ServiceName,
            Method = metaResponse.Method,
            Path = metaResponse.Path,
            Data = metaResponse.Data,
            GeneratedAt = metaResponse.GeneratedAt,
            SchemaVersion = metaResponse.SchemaVersion
        };

        return (StatusCodes.OK, response);
    }

    // REMOVED: PublishServiceMappingUpdateAsync - Service mapping events belong to Orchestrator
    // REMOVED: GetServiceMappingsAsync - Service routing is now in Orchestrator API
    // REMOVED: DiscoverAPIsAsync - API discovery belongs to Permission service
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
        _logger.LogDebug("GetClientCapabilitiesAsync called for session {SessionId} with filter: {Filter}",
            body.SessionId, body.ServiceFilter ?? "(none)");

        // Look up the connection by session ID
        var connection = _connectionManager.GetConnection(body.SessionId.ToString());
        if (connection == null)
        {
            _logger.LogWarning("No active WebSocket connection found for session {SessionId}", body.SessionId);
            return (StatusCodes.NotFound, null);
        }

        var connectionState = connection.ConnectionState;

        // Build capabilities using helper
        var apiEntries = _manifestBuilder.BuildApiList(connectionState.ServiceMappings, body.ServiceFilter);
        var capabilities = apiEntries.Select(api => new ClientCapability
        {
            Guid = api.ServiceGuid,
            Service = api.ServiceName,
            Endpoint = api.Path,
            Method = ClientCapabilityMethod.POST
        }).ToList();

        // Build shortcuts using helper, removing expired/invalid ones
        var shortcutEntries = _manifestBuilder.BuildShortcutList(
            connectionState.GetAllShortcuts(),
            expiredGuid => connectionState.RemoveShortcut(expiredGuid));

        var shortcuts = shortcutEntries.Select(s => new ClientShortcut
        {
            Guid = s.RouteGuid,
            TargetService = s.TargetService,
            TargetEndpoint = s.TargetEndpoint,
            Name = s.Name,
            Description = s.Description
        }).ToList();

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

    /// <summary>
    /// Gets all active WebSocket session IDs for an account.
    /// Internal endpoint for service-to-service session discovery.
    /// </summary>
    public async Task<(StatusCodes, GetAccountSessionsResponse?)> GetAccountSessionsAsync(
        GetAccountSessionsRequest body,
        CancellationToken cancellationToken = default)
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
        // Internal mode authentication - bypass JWT validation
        if (_connectionMode == ConnectionMode.Internal)
        {
            if (_internalAuthMode == InternalAuthMode.NetworkTrust)
            {
                // Network trust: Accept connection without authentication
                var sessionId = Guid.NewGuid().ToString();
                _logger.LogInformation("Internal mode (network-trust): Creating session {SessionId}", sessionId);
                return (sessionId, null, new List<string> { "internal" }, null, false);
            }

            if (_internalAuthMode == InternalAuthMode.ServiceToken)
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

            _logger.LogDebug("Token validation result - Valid: {Valid}, SessionKey: {SessionKey}, AccountId: {AccountId}, RolesCount: {RolesCount}, AuthorizationsCount: {AuthorizationsCount}",
                validationResponse.Valid,
                validationResponse.SessionKey,
                validationResponse.AccountId,
                validationResponse.Roles?.Count ?? 0,
                validationResponse.Authorizations?.Count ?? 0);

            // Defensive: guard against corrupt Redis data (SessionKey should always be populated on valid sessions)
            if (validationResponse.Valid && validationResponse.SessionKey != Guid.Empty)
            {
                _logger.LogDebug("JWT validated successfully, SessionKey: {SessionKey}", validationResponse.SessionKey);
                // Return session key, account ID, roles, and authorizations for capability initialization
                // This is a new connection (Bearer token), not a reconnection
                return (validationResponse.SessionKey.ToString(), validationResponse.AccountId, validationResponse.Roles, validationResponse.Authorizations, false);
            }
            else
            {
                _logger.LogWarning("JWT validation failed, Valid: {Valid}, SessionKey: {SessionKey}",
                    validationResponse.Valid, validationResponse.SessionKey);
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
                    // AccountId is now Guid? type, no parsing needed
                    // Mark as reconnection so services can re-publish shortcuts
                    return (sessionId, restoredState.AccountId, restoredState.UserRoles, restoredState.Authorizations, true);
                }
            }

            _logger.LogWarning("Invalid or expired reconnection token");
            return (null, null, null, null, false);
        }

        _logger.LogWarning("Authorization format not recognized (expected 'Bearer' or 'Reconnect' prefix)");
        return (null, null, null, null, false);
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
    /// Checks if the server can accept additional WebSocket connections.
    /// Returns true if current connection count is below MaxConcurrentConnections.
    /// </summary>
    public bool CanAcceptNewConnection()
    {
        return _connectionManager.ConnectionCount < _configuration.MaxConcurrentConnections;
    }

    /// <summary>
    /// Gets the current connection count for monitoring purposes.
    /// </summary>
    public int CurrentConnectionCount => _connectionManager.ConnectionCount;

    /// <summary>
    /// Gets the maximum allowed concurrent connections from configuration.
    /// </summary>
    public int MaxConcurrentConnections => _configuration.MaxConcurrentConnections;

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
        await HandleWebSocketCommunicationCoreAsync(
            webSocket,
            sessionId,
            accountId,
            userRoles,
            authorizations,
            isReconnection,
            cancellationToken);
    }

    private async Task HandleWebSocketCommunicationCoreAsync(
        WebSocket webSocket,
        string sessionId,
        Guid? accountId,
        ICollection<string>? userRoles,
        ICollection<string>? authorizations,
        bool isReconnection,
        CancellationToken cancellationToken)
    {
        // Note: Connection limit check is performed in ConnectController.HandleWebSocketConnectionAsync
        // BEFORE accepting the WebSocket upgrade, allowing proper 503 response instead of accepting
        // then immediately closing. A secondary check is retained here as defense-in-depth for
        // race conditions where multiple connections pass the controller check simultaneously.
        if (_connectionManager.ConnectionCount >= _configuration.MaxConcurrentConnections)
        {
            _logger.LogWarning("Connection limit race condition: Maximum concurrent connections ({MaxConnections}) reached after WebSocket accepted, closing session {SessionId}",
                _configuration.MaxConcurrentConnections, sessionId);
            await webSocket.CloseAsync(WebSocketCloseStatus.PolicyViolation,
                "Maximum connections exceeded", cancellationToken);
            return;
        }

        // Create connection state with service mappings from discovery
        var connectionState = new ConnectionState(sessionId);
        connectionState.UserRoles = userRoles;

        // INTERNAL MODE: Skip all capability initialization - just peer routing
        if (_connectionMode == ConnectionMode.Internal)
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

        // Initialize capabilities from Permission service
        // Service mappings are populated via SessionCapabilitiesEvent from Permission service
        // and stored in ConnectionState.ServiceMappings (the active source of truth)
        var role = DetermineHighestPriorityRole(userRoles);
        _logger.LogInformation("Initializing capabilities for session {SessionId}, role {Role} with {AuthCount} authorizations",
            sessionId, role, authorizations?.Count ?? 0);
        var sessionMappings = await InitializeSessionCapabilitiesAsync(sessionId, role, authorizations, cancellationToken);

        if (sessionMappings != null)
        {
            foreach (var mapping in sessionMappings)
            {
                connectionState.AddServiceMapping(mapping.Key, mapping.Value);
            }
        }

        // Add connection to manager
        _connectionManager.AddConnection(sessionId, webSocket, connectionState);

        var buffer = new byte[_configuration.BufferSize]; // Configurable buffer for binary protocol

        try
        {
            _logger.LogInformation("WebSocket connection established for session {SessionId}", sessionId);

            // Update session heartbeat in Redis
            await _sessionManager.UpdateSessionHeartbeatAsync(sessionId, _instanceId);

            // Create initial connection state in Redis for reconnection support
            var connectionStateData = new ConnectionStateData
            {
                SessionId = Guid.Parse(sessionId),
                AccountId = accountId,
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
                    queueTtl: TimeSpan.FromSeconds(_configuration.ReconnectionWindowSeconds),
                    cancellationToken: cancellationToken);

                _sessionSubscriptions[sessionId] = subscription;
                _logger.LogDebug("Subscribed to client events for session {SessionId} on queue {QueueName} (TTL: {TtlSeconds}sec)",
                    sessionId, queueName, _configuration.ReconnectionWindowSeconds);

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
                    ConnectInstanceId = _instanceId,
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
            // 2. Permission compiles capabilities and publishes SessionCapabilitiesEvent
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

                // Periodic heartbeat update (configurable interval)
                if (_sessionManager != null &&
                    (DateTimeOffset.UtcNow - connectionState.LastActivity).TotalSeconds >= _configuration.HeartbeatIntervalSeconds)
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

            // CRITICAL: Check subsume BEFORE publishing any events or cleaning up state.
            // RemoveConnectionIfMatch uses WebSocket reference equality: if the stored WebSocket
            // doesn't match ours, a new connection replaced us (subsume). In that case we must NOT
            // publish session.disconnected or remove indexes — the session is still active on the
            // new connection, and false disconnect events cause state churn across all consumers
            // (Permission, GameSession, Actor, Matchmaking) that immediately rebuild on session.connected
            var wasRemoved = _connectionManager.RemoveConnectionIfMatch(sessionId, webSocket);

            if (!wasRemoved)
            {
                // Connection was subsumed — session is NOT disconnecting, just transferred.
                // Do NOT publish session.disconnected (session is still active).
                // Do NOT remove from account index (new connection needs it).
                // Do NOT unsubscribe from RabbitMQ (new connection is using the subscription).
                // Do NOT initiate reconnection window (session is still connected).
                _logger.LogInformation("Session {SessionId} was subsumed by new connection - skipping disconnect and cleanup", sessionId);
            }
            else
            {
                // Real disconnect — publish event BEFORE unsubscribing from RabbitMQ.
                // This ensures Permission removes session from activeConnections before the exchange
                // is torn down. Without this ordering, services could still try to publish to the
                // session during the brief cleanup window.
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

                    // Clean up all entity session bindings for this session
                    await _entitySessionRegistry.UnregisterSessionAsync(sessionId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to publish session.disconnected event for session {SessionId}", sessionId);
                    // Fire-and-forget error event - we're in finally cleanup
                    _ = PublishErrorEventAsync("PublishSessionDisconnected", ex.GetType().Name, ex.Message, dependency: "messaging", details: new { SessionId = sessionId });
                }
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

                    var reconnectionWindowSeconds = _configuration.ReconnectionWindowSeconds;
                    var reconnectionWindow = TimeSpan.FromSeconds(reconnectionWindowSeconds);
                    var reconnectionToken = connectionState.InitiateReconnectionWindow(
                        reconnectionWindowMinutes: reconnectionWindowSeconds / 60,
                        userRoles: userRoles);

                    // Store reconnection state in Redis
                    await _sessionManager.InitiateReconnectionWindowAsync(
                        sessionId,
                        reconnectionToken,
                        reconnectionWindow,
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
        BinaryMessage? parsedMessage = null;
        try
        {
            // Parse binary message using protocol class
            var message = BinaryMessage.Parse(buffer, messageLength);
            parsedMessage = message;

            _logger.LogDebug("Binary message from session {SessionId}: {Message}",
                sessionId, message.ToString());

            // RPC RESPONSE INTERCEPTION: Check if this is a response to a pending RPC
            if (message.IsResponse && _pendingRPCs.TryRemove(message.MessageId, out var pendingRPC))
            {
                await ForwardRPCResponseAsync(message, pendingRPC, sessionId, cancellationToken);
                return;
            }

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
            var rateLimitResult = MessageRouter.CheckRateLimit(connectionState, _configuration.MaxMessagesPerMinute, _configuration.RateLimitWindowMinutes);
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
                if (!_configuration.EnableClientToClientRouting)
                {
                    _logger.LogWarning("Client-to-client routing disabled, rejecting P2P message from session {SessionId}", sessionId);
                    var errorResponse = MessageRouter.CreateErrorResponse(
                        message, ResponseCodes.BroadcastNotAllowed, "Client-to-client routing is disabled");
                    await _connectionManager.SendMessageAsync(sessionId, errorResponse, cancellationToken);
                    return;
                }

                await RouteToClientAsync(message, routeInfo, sessionId, cancellationToken);
            }
            else if (routeInfo.RouteType == RouteType.Broadcast)
            {
                // Broadcast: Send to all connected peers (except sender)
                // Mode enforcement: External mode rejects broadcast with BroadcastNotAllowed
                if (_connectionMode == ConnectionMode.External)
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

            // Send error response using already-parsed message (avoids double-parse)
            if (parsedMessage.HasValue)
            {
                try
                {
                    var errorResponse = MessageRouter.CreateErrorResponse(
                        parsedMessage.Value, ResponseCodes.RequestError, "Internal server error");

                    await _connectionManager.SendMessageAsync(sessionId, errorResponse, cancellationToken);
                }
                catch (Exception sendEx)
                {
                    _logger.LogError(sendEx, "Failed to send error response to session {SessionId}", sessionId);
                }
            }
        }
    }

    /// <summary>
    /// Routes a message to a Bannou service and handles the response.
    /// Uses ServiceNavigator.ExecuteRawApiAsync for unified service invocation.
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
                connectionState.AddPendingMessage(message.MessageId, endpointKey, DateTimeOffset.UtcNow, _configuration.PendingMessageTimeoutSeconds);
            }

            // Parse endpoint key to extract service name, method, and path
            // Format: "serviceName:/path" (defaults to POST) or "serviceName:METHOD:/path"
            var firstColon = endpointKey.IndexOf(':');
            if (firstColon <= 0)
            {
                throw new InvalidOperationException($"Invalid endpoint key format: {endpointKey}");
            }

            var serviceName = endpointKey[..firstColon];
            var rest = endpointKey[(firstColon + 1)..];

            // Check if rest starts with an HTTP method (GET, POST, PUT, DELETE, PATCH)
            // If so, parse the three-part format: serviceName:METHOD:/path
            var httpMethod = "POST"; // Default for WebSocket endpoints
            var path = rest;

            if (rest.StartsWith("GET:", StringComparison.OrdinalIgnoreCase))
            {
                httpMethod = "GET";
                path = rest[4..]; // Skip "GET:"
            }
            else if (rest.StartsWith("POST:", StringComparison.OrdinalIgnoreCase))
            {
                httpMethod = "POST";
                path = rest[5..]; // Skip "POST:"
            }
            else if (rest.StartsWith("PUT:", StringComparison.OrdinalIgnoreCase))
            {
                httpMethod = "PUT";
                path = rest[4..]; // Skip "PUT:"
            }
            else if (rest.StartsWith("DELETE:", StringComparison.OrdinalIgnoreCase))
            {
                httpMethod = "DELETE";
                path = rest[7..]; // Skip "DELETE:"
            }
            else if (rest.StartsWith("PATCH:", StringComparison.OrdinalIgnoreCase))
            {
                httpMethod = "PATCH";
                path = rest[6..]; // Skip "PATCH:"
            }

            _logger.LogInformation("Routing WebSocket message to service {Service} ({Method} {Path})",
                serviceName, httpMethod, path);

            // Get raw payload bytes - true zero-copy forwarding without UTF-16 string conversion
            // Connect service should NEVER parse the payload - zero-copy routing based on GUID only
            var payloadBytes = message.Payload;

            // Execute service invocation via ServiceNavigator
            // ServiceNavigator is Scoped, so we create a scope for each request
            RawApiResult? apiResult = null;
            try
            {
                // Set session context for tracing (ServiceNavigator will read from ServiceRequestContext)
                ServiceRequestContext.SessionId = sessionId;

                await using var scope = _serviceScopeFactory.CreateAsyncScope();
                var navigator = scope.ServiceProvider.GetRequiredService<IServiceNavigator>();

                _logger.LogDebug("WebSocket -> ServiceNavigator: {Method} {Path}",
                    httpMethod, path);

                // Execute via ServiceNavigator - uses zero-copy byte forwarding
                apiResult = await navigator.ExecuteRawApiAsync(
                    serviceName,
                    path,
                    payloadBytes,
                    new HttpMethod(httpMethod),
                    cancellationToken);

                // Use Warning level for response timing to ensure visibility in CI
                _logger.LogWarning("ServiceNavigator response in {DurationMs}ms: {StatusCode}",
                    apiResult.Duration.TotalMilliseconds, apiResult.StatusCode);

                // Log response details
                if (apiResult.IsSuccess)
                {
                    _logger.LogDebug("Service {Service} responded with status {Status}: {ResponsePreview}",
                        serviceName, apiResult.StatusCode,
                        apiResult.ResponseBody?.Substring(0, Math.Min(200, apiResult.ResponseBody?.Length ?? 0)) ?? "(empty)");
                }
                else if (apiResult.ErrorMessage != null)
                {
                    // Transport-level error (timeout, connection refused, etc.)
                    _logger.LogError("Service {Service} transport error: {Error}",
                        serviceName, apiResult.ErrorMessage);
                    await PublishErrorEventAsync("RouteToService", "transport_error", apiResult.ErrorMessage,
                        dependency: serviceName, details: new { Method = httpMethod, Path = path, StatusCode = apiResult.StatusCode });
                }
                else
                {
                    // HTTP error response
                    _logger.LogWarning("Service {Service} returned non-success status {StatusCode}: {ResponsePreview}",
                        serviceName, apiResult.StatusCode,
                        apiResult.ResponseBody?.Substring(0, Math.Min(500, apiResult.ResponseBody?.Length ?? 0)) ?? "(empty body)");
                }
            }
            finally
            {
                // Always clear context after request
                ServiceRequestContext.SessionId = null;
                ServiceRequestContext.CorrelationId = null;
            }

            // Send response back to WebSocket client
            if (routeInfo.RequiresResponse)
            {
                var responseCode = MapHttpStatusToResponseCode(apiResult?.StatusCode);
                var responseJson = apiResult?.ResponseBody ?? "{}";

                var responseMessage = BinaryMessage.CreateResponse(
                    message, responseCode, Encoding.UTF8.GetBytes(responseJson));

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

        // Parse endpoint key: "serviceName:/path"
        var firstColon = routeInfo.ServiceName.IndexOf(':');
        if (firstColon <= 0)
        {
            _logger.LogWarning("Invalid endpoint key format for meta request: {EndpointKey}", routeInfo.ServiceName);
            var errorResponse = BinaryMessage.CreateResponse(message, ResponseCodes.RequestError);
            await _connectionManager.SendMessageAsync(sessionId, errorResponse, cancellationToken);
            return;
        }

        var serviceName = routeInfo.ServiceName[..firstColon];
        var originalPath = routeInfo.ServiceName[(firstColon + 1)..];
        const string httpMethod = "POST"; // All WebSocket-routed endpoints are POST

        // Explicit permission check: verify the base endpoint is in the client's service mappings
        // routeInfo.ServiceName is already in "serviceName:/path" format -- the same key used by HasServiceMapping
        if (!connectionState.HasServiceMapping(routeInfo.ServiceName))
        {
            _logger.LogWarning("Meta request denied: session {SessionId} lacks access to {EndpointKey}",
                sessionId, routeInfo.ServiceName);
            var unauthorizedResponse = BinaryMessage.CreateResponse(message, ResponseCodes.Unauthorized);
            await _connectionManager.SendMessageAsync(sessionId, unauthorizedResponse, cancellationToken);
            return;
        }

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
        var buffer = new byte[_configuration.BufferSize];

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
    /// Handles text WebSocket messages by returning a TextProtocolNotSupported error.
    /// The binary protocol is required for all messages after authentication.
    /// See docs/WEBSOCKET-PROTOCOL.md for protocol specification.
    /// </summary>
    private async Task HandleTextMessageFallbackAsync(
        string sessionId,
        ConnectionState connectionState,
        string textMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogWarning(
                "Session {SessionId} sent text WebSocket message after authentication. " +
                "Text protocol is not supported; binary protocol required. Message preview: {Preview}",
                sessionId,
                textMessage.Length > 50 ? textMessage[..50] + "..." : textMessage);

            // Return error response: text protocol is not supported after AUTH
            // Error responses have empty payloads - the response code tells the story
            var messageId = MessageRouter.GenerateMessageId();
            var sequenceNumber = connectionState.GetNextSequenceNumber(0);

            var errorResponse = new BinaryMessage(
                MessageFlags.Response,
                0, // Default channel
                sequenceNumber,
                messageId,
                (byte)ResponseCodes.TextProtocolNotSupported,
                ReadOnlyMemory<byte>.Empty);

            await _connectionManager.SendMessageAsync(sessionId, errorResponse, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling text message rejection for session {SessionId}", sessionId);
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

        // Start periodic cleanup of expired pending RPCs to prevent memory leaks
        _pendingRPCCleanupTimer = new Timer(CleanupExpiredPendingRPCs, null,
            TimeSpan.FromSeconds(_configuration.RpcCleanupIntervalSeconds),
            TimeSpan.FromSeconds(_configuration.RpcCleanupIntervalSeconds));
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
            var sessionIdStr = eventData.SessionId.ToString();
            if (HasConnection(sessionIdStr))
            {
                if (eventData.EventType == AuthEventType.Login)
                {
                    // User logged in - Permission service will automatically recompile capabilities
                    // and push updated connect.capability_manifest to client
                    _logger.LogDebug("Auth login event for session {SessionId} - capabilities will be updated by Permission service",
                        eventData.SessionId);
                }
                else if (eventData.EventType == AuthEventType.Logout)
                {
                    // User logged out - Permission service will automatically recompile capabilities
                    // and push updated connect.capability_manifest to client
                    _logger.LogDebug("Auth logout event for session {SessionId} - capabilities will be updated by Permission service",
                        eventData.SessionId);

                    // Optionally close the WebSocket connection on logout
                    // await DisconnectAsync(eventData.SessionId, "User logged out");
                }
                else if (eventData.EventType == AuthEventType.TokenRefresh)
                {
                    // Token refreshed - validate session still exists
                    var sessionValid = await ValidateSessionAsync(sessionIdStr);
                    if (!sessionValid)
                    {
                        _logger.LogWarning("Session {SessionId} invalid after token refresh, disconnecting", eventData.SessionId);
                        await DisconnectAsync(sessionIdStr, "Session invalid");
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
                    // Register pending RPC for response forwarding
                    var now = DateTimeOffset.UtcNow;

                    // Warn if ServiceName is missing - indicates publisher failed to set required metadata
                    if (string.IsNullOrEmpty(eventData.ServiceName))
                    {
                        _logger.LogWarning("RPC event {MessageId} missing ServiceName - publisher should include service metadata", eventData.MessageId);
                    }

                    var pendingRPC = new PendingRPCInfo
                    {
                        ClientSessionId = Guid.Parse(eventData.ClientId),
                        ServiceName = eventData.ServiceName ?? "unknown",
                        ResponseChannel = eventData.ResponseChannel,
                        ServiceGuid = eventData.ServiceGuid,
                        SentAt = now,
                        TimeoutAt = now.AddSeconds(eventData.TimeoutSeconds > 0 ? eventData.TimeoutSeconds : _configuration.DefaultRpcTimeoutSeconds)
                    };

                    _pendingRPCs[(ulong)eventData.MessageId] = pendingRPC;

                    _logger.LogDebug("Sent RPC message {MessageId} to client {ClientId}, awaiting response for channel {ResponseChannel}",
                        eventData.MessageId, eventData.ClientId, eventData.ResponseChannel);

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

    /// <summary>
    /// Forwards an RPC response from a client back to the originating service.
    /// </summary>
    private async Task ForwardRPCResponseAsync(
        BinaryMessage response,
        PendingRPCInfo pendingRPC,
        string sessionId,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Forwarding RPC response from session {SessionId} to service {ServiceName} via channel {ResponseChannel}",
            sessionId, pendingRPC.ServiceName, pendingRPC.ResponseChannel);

        try
        {
            // Create typed response event
            var responseEvent = new ClientRPCResponseEvent
            {
                ClientId = sessionId,
                ServiceName = pendingRPC.ServiceName,
                ServiceGuid = pendingRPC.ServiceGuid,
                MessageId = (long)response.MessageId,
                Payload = response.Payload.ToArray(),
                ResponseCode = response.ResponseCode,
                Timestamp = DateTimeOffset.UtcNow
            };

            await _messageBus.TryPublishAsync(
                pendingRPC.ResponseChannel,
                responseEvent,
                cancellationToken: cancellationToken);

            _logger.LogDebug("RPC response forwarded to {ResponseChannel} for message {MessageId}",
                pendingRPC.ResponseChannel, response.MessageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to forward RPC response to {ResponseChannel}", pendingRPC.ResponseChannel);
        }
    }

    /// <summary>
    /// Periodically removes expired pending RPCs to prevent memory leaks.
    /// Called by _pendingRPCCleanupTimer every 30 seconds.
    /// </summary>
    private void CleanupExpiredPendingRPCs(object? state)
    {
        var now = DateTimeOffset.UtcNow;
        var expiredCount = 0;

        foreach (var kvp in _pendingRPCs)
        {
            if (kvp.Value.TimeoutAt < now)
            {
                if (_pendingRPCs.TryRemove(kvp.Key, out _))
                {
                    expiredCount++;
                }
            }
        }

        if (expiredCount > 0)
        {
            _logger.LogInformation("Cleaned up {ExpiredCount} expired pending RPCs", expiredCount);
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
            if (eventName == "permission.session_capabilities" ||
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
    /// SessionCapabilitiesEvent from Permission service after session.connected event.
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
        // 2. Permission receives event, compiles capabilities, publishes SessionCapabilitiesEvent
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
            // Uses helper for parsing and filtering logic
            var apiEntries = _manifestBuilder.BuildApiList(connectionState.ServiceMappings);
            var shortcutEntries = _manifestBuilder.BuildShortcutList(
                connectionState.GetAllShortcuts(),
                expiredGuid => connectionState.RemoveShortcut(expiredGuid));

            // Transform to WebSocket JSON format using new ClientCapabilityEntry schema
            var availableApis = new List<object>();

            foreach (var api in apiEntries)
            {
                availableApis.Add(new
                {
                    serviceId = api.ServiceGuid.ToString(),
                    endpoint = api.Path,
                    service = api.ServiceName,
                    description = api.Description
                });
            }

            // Add shortcuts to availableAPIs - they look identical to regular endpoints from client perspective
            foreach (var shortcut in shortcutEntries)
            {
                // Shortcuts use new schema format with "shortcut" as service
                availableApis.Add(new
                {
                    serviceId = shortcut.RouteGuid.ToString(),
                    endpoint = shortcut.Name,
                    service = "shortcut",
                    description = shortcut.Description ?? $"Shortcut to {shortcut.Name}"
                });
            }

            var capabilityManifest = new
            {
                eventName = "connect.capability_manifest",
                sessionId = sessionId,
                availableApis = availableApis,
                version = 1,
                timestamp = DateTimeOffset.UtcNow,
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
    /// Processes capabilities received from Permission service via SessionCapabilitiesEvent.
    /// Generates client-salted GUIDs, updates connection state, and sends manifest to client.
    /// NO API call to Permission service - capabilities are passed directly from the event.
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
                var paths = servicePermissions.Value;

                foreach (var path in paths)
                {
                    // Key format: "serviceName:/path" (no HTTP method - all WebSocket endpoints are POST)
                    var endpointKey = $"{serviceName}:{path}";
                    var guid = GuidGenerator.GenerateServiceGuid(endpointKey, sessionId, _serverSalt);
                    newMappings[endpointKey] = guid;
                }
            }

            // Atomic update to prevent race conditions
            connectionState.UpdateAllServiceMappings(newMappings);

            // Build and send updated capability manifest
            // INTERNAL format: "serviceName:/path" - used for server-side routing
            // CLIENT format matches new ClientCapabilityEntry schema
            var availableApis = new List<object>();
            foreach (var mapping in newMappings)
            {
                // Parse the internal endpoint key format: "serviceName:/path"
                var endpointKey = mapping.Key;
                var guid = mapping.Value;

                var firstColon = endpointKey.IndexOf(':');
                if (firstColon <= 0) continue;

                var serviceName = endpointKey[..firstColon];
                var path = endpointKey[(firstColon + 1)..];

                // Skip endpoints with path templates - WebSocket requires POST with JSON body
                if (path.Contains('{'))
                {
                    continue;
                }

                availableApis.Add(new
                {
                    serviceId = guid.ToString(),
                    endpoint = path,
                    service = serviceName
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

                // Shortcuts use new schema format with "shortcut" as service
                availableApis.Add(new
                {
                    serviceId = shortcut.RouteGuid.ToString(),
                    endpoint = shortcut.Name ?? shortcut.RouteGuid.ToString(),
                    service = "shortcut",
                    description = shortcut.Description ?? $"Shortcut to {shortcut.Name}"
                });
            }

            // Build manifest as dictionary for conditional peerGuid inclusion
            // External mode: No peerGuid (clients cannot route to each other)
            // Relayed/Internal mode: Include peerGuid (enables peer-to-peer routing)
            var capabilityManifest = new Dictionary<string, object>
            {
                ["eventName"] = "connect.capability_manifest",
                ["sessionId"] = sessionId,
                ["availableApis"] = availableApis,
                ["version"] = 1,
                ["timestamp"] = DateTimeOffset.UtcNow,
                ["reason"] = reason
            };

            // Include peerGuid only in relayed/internal modes where peer routing is supported
            if (_connectionMode != ConnectionMode.External)
            {
                capabilityManifest["peerGuid"] = connectionState.PeerGuid.ToString();
            }

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

    #endregion

    #region Permission Registration

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
                // Parse name (optional metadata)
                if (metadataElement.TryGetProperty("name", out var nameElement) ||
                    metadataElement.TryGetProperty("Name", out nameElement))
                {
                    shortcutData.Name = nameElement.GetString();
                }

                // Parse sourceService (optional metadata)
                if (metadataElement.TryGetProperty("sourceService", out var sourceElement))
                {
                    shortcutData.SourceService = sourceElement.GetString();
                }

                // Parse targetService (required - validation at end catches null/empty)
                if (metadataElement.TryGetProperty("targetService", out var targetServiceElement))
                {
                    shortcutData.TargetService = targetServiceElement.GetString();
                }

                // Parse targetMethod (required - validation at end catches null/empty)
                if (metadataElement.TryGetProperty("targetMethod", out var targetMethodElement))
                {
                    shortcutData.TargetMethod = targetMethodElement.GetString();
                }

                // Parse targetEndpoint (required - validation at end catches null/empty)
                if (metadataElement.TryGetProperty("targetEndpoint", out var targetEndpointElement))
                {
                    shortcutData.TargetEndpoint = targetEndpointElement.GetString();
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
            // Check if ServiceMappings is populated - if empty, initial capabilities haven't arrived yet
            // In that case, skip sending manifest now; the shortcut is already in connectionState
            // and will be included when ProcessCapabilitiesAsync sends the full manifest
            if (!connectionState.ServiceMappings.Any())
            {
                _logger.LogDebug(
                    "Deferring shortcut manifest for session {SessionId} - awaiting initial capabilities",
                    sessionId);
                return;
            }

            // Build the capability manifest with available APIs using helper
            var apiEntries = _manifestBuilder.BuildApiList(connectionState.ServiceMappings);
            var shortcutEntries = _manifestBuilder.BuildShortcutList(
                connectionState.GetAllShortcuts(),
                expiredGuid => connectionState.RemoveShortcut(expiredGuid));

            // Transform to WebSocket JSON format using new ClientCapabilityEntry schema
            var availableApis = new List<object>();

            foreach (var api in apiEntries)
            {
                availableApis.Add(new
                {
                    serviceId = api.ServiceGuid.ToString(),
                    endpoint = api.Path,
                    service = api.ServiceName,
                    description = api.Description
                });
            }

            // Add shortcuts to availableAPIs - they look identical to regular endpoints from client perspective
            foreach (var shortcut in shortcutEntries)
            {
                // Shortcuts use new schema format with "shortcut" as service
                availableApis.Add(new
                {
                    serviceId = shortcut.RouteGuid.ToString(),
                    endpoint = shortcut.Name,
                    service = "shortcut",
                    description = shortcut.Description ?? $"Shortcut to {shortcut.Name}"
                });
            }

            var capabilityManifest = new
            {
                eventName = "connect.capability_manifest",
                sessionId = sessionId,
                availableApis = availableApis,
                version = 1,
                timestamp = DateTimeOffset.UtcNow,
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

    /// <summary>
    /// Maps HTTP status codes (as int) to WebSocket ResponseCodes.
    /// Overload for use with RawApiResult.StatusCode.
    /// </summary>
    private static ResponseCodes MapHttpStatusToResponseCode(int? statusCode)
    {
        if (statusCode == null || statusCode == 0)
        {
            return ResponseCodes.Service_InternalServerError;
        }

        return statusCode.Value switch
        {
            200 or 201 or 202 or 204 => ResponseCodes.OK,
            400 => ResponseCodes.Service_BadRequest,
            401 or 403 => ResponseCodes.Service_Unauthorized,
            404 => ResponseCodes.Service_NotFound,
            409 => ResponseCodes.Service_Conflict,
            _ => ResponseCodes.Service_InternalServerError
        };
    }

    #endregion

    #region IDisposable

    private bool _disposed;

    /// <summary>
    /// Disposes the ConnectService, gracefully closing all WebSocket connections.
    /// Called by the DI container during application shutdown.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _logger.LogInformation("ConnectService shutting down - closing all WebSocket connections");

        // Dispose the pending RPC cleanup timer
        _pendingRPCCleanupTimer?.Dispose();
        _pendingRPCCleanupTimer = null;

        // Dispose the connection manager, which gracefully closes all WebSocket connections
        // with "Server shutdown" close frames and waits up to ConnectionShutdownTimeoutSeconds
        _connectionManager.Dispose();

        _logger.LogInformation("ConnectService shutdown complete");
    }

    #endregion

}
