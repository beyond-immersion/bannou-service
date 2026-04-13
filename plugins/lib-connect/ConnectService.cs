using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Connect.Helpers;
using BeyondImmersion.BannouService.Connect.Protocol;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
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
/// Implements IAsyncDisposable/IDisposable to enable graceful shutdown with WebSocket close frames.
/// </summary>
[BannouService("connect", typeof(IConnectService), lifetime: ServiceLifetime.Singleton, layer: ServiceLayer.AppFoundation)]
public partial class ConnectService : IConnectService, IDisposable, IAsyncDisposable
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
    private readonly InterNodeBroadcastManager _interNodeBroadcast;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly IReadOnlyList<ISessionActivityListener> _sessionActivityListeners;

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
    /// Builds the session-specific topic key for RabbitMQ routing.
    /// Format: {SESSION_TOPIC_PREFIX}{sessionId}
    /// </summary>
    internal static string BuildSessionTopicKey(string sessionId)
        => $"{SESSION_TOPIC_PREFIX}{sessionId}";

    /// <summary>
    /// Dedicated direct exchange for client events.
    /// Defined in provisioning/rabbitmq/definitions.json.
    /// </summary>
    private const string CLIENT_EVENTS_EXCHANGE = "bannou-client-events";

    /// <summary>
    /// Wire protocol constant: ServiceGuid value for system/control messages (capability manifests,
    /// client events, disconnect notifications). Not an absence sentinel — this is a defined protocol
    /// value meaning "this message is from the Connect system, not routed to a backend service".
    /// </summary>
    private static readonly Guid SystemServiceGuid = Guid.Empty;

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
        IEntitySessionRegistry entitySessionRegistry,
        InterNodeBroadcastManager interNodeBroadcast,
        IMeshInstanceIdentifier meshInstanceIdentifier,
        ITelemetryProvider telemetryProvider,
        IEnumerable<ISessionActivityListener> sessionActivityListeners)
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
        _interNodeBroadcast = interNodeBroadcast;
        _telemetryProvider = telemetryProvider;
        _sessionActivityListeners = sessionActivityListeners.ToList();

        if (_sessionActivityListeners.Count > 0)
        {
            _logger.LogInformation("Connect service initialized with {Count} session activity listeners: {Listeners}",
                _sessionActivityListeners.Count,
                string.Join(", ", _sessionActivityListeners.Select(l => l.GetType().Name)));
        }

        _connectionManager = new WebSocketConnectionManager(
            configuration.ConnectionShutdownTimeoutSeconds,
            configuration.ConnectionCleanupIntervalSeconds,
            configuration.InactiveConnectionTimeoutMinutes,
            _logger,
            configuration.CompressionEnabled,
            configuration.CompressionThresholdBytes,
            configuration.CompressionQuality,
            _telemetryProvider);

        // Server salt from configuration - REQUIRED (fail-fast for production safety)
        // All service instances must share the same salt for session shortcuts to work correctly
        if (string.IsNullOrEmpty(configuration.ServerSalt))
        {
            throw new InvalidOperationException(
                "CONNECT_SERVERSALT is required. All service instances must share the same salt for session shortcuts to work correctly.");
        }
        _serverSalt = configuration.ServerSalt;

        // Use process-stable instance ID from lib-mesh for distributed deployment
        // per IMPLEMENTATION TENETS — consistent identity across broadcast mesh and session heartbeats
        _instanceId = meshInstanceIdentifier.InstanceId;

        // Register event handlers via partial class (ConnectServiceEvents.cs)
        RegisterEventConsumers(eventConsumer);

        // Wire up inter-node broadcast receive: parse binary message and deliver to local clients
        _interNodeBroadcast.OnBroadcastReceived = async (buffer, length) =>
        {
            var message = BinaryMessage.Parse(buffer, length);
            await _connectionManager.BroadcastMessageAsync(message);
        };

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
            HttpMethodType.Get => HttpMethod.Get,
            HttpMethodType.Post => HttpMethod.Post,
            HttpMethodType.Put => HttpMethod.Put,
            HttpMethodType.Delete => HttpMethod.Delete,
            HttpMethodType.Patch => HttpMethod.Patch,
            _ => HttpMethod.Get
        };

        var startTime = DateTime.UtcNow;

        try
        {
            // Route through Bannou service invocation
            HttpResponseMessage httpResponse;

            if (body.Method == HttpMethodType.Get ||
                body.Method == HttpMethodType.Delete)
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

        // SessionKey is non-nullable Guid in generated type; Guid.Empty indicates validation failure
        if (validationResponse == null || validationResponse.SessionKey == Guid.Empty)
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
            Method = HttpMethodType.Post
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
            Version = 1
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
            SessionIds = sessions.Where(s => Guid.TryParse(s, out _)).Select(s => Guid.Parse(s)).ToList()
        };

        _logger.LogInformation("Returning {Count} sessions for account {AccountId}",
            sessions.Count, body.AccountId);

        return (StatusCodes.OK, response);
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
    /// Validates a service token for broadcast WebSocket authentication.
    /// Returns true if auth mode is not ServiceToken, or if the token matches.
    /// </summary>
    /// <param name="serviceToken">The X-Service-Token header value.</param>
    /// <returns>True if the token is valid or not required.</returns>
    public bool ValidateBroadcastServiceToken(string? serviceToken)
    {
        if (_internalAuthMode != InternalAuthMode.ServiceToken) return true;
        return !string.IsNullOrEmpty(serviceToken) && serviceToken == _internalServiceToken;
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

    #region IDisposable / IAsyncDisposable

    private bool _disposed;

    /// <summary>
    /// Asynchronously disposes the ConnectService, gracefully closing all WebSocket connections.
    /// Preferred by the DI container during application shutdown over the synchronous Dispose.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _logger.LogInformation("ConnectService shutting down - closing all WebSocket connections");

        _pendingRPCCleanupTimer?.Dispose();
        _pendingRPCCleanupTimer = null;

        _interNodeBroadcast.Dispose();

        await _connectionManager.DisposeAsync();

        _logger.LogInformation("ConnectService shutdown complete");
    }

    /// <summary>
    /// Synchronous fallback disposal. The DI container prefers DisposeAsync when available.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _logger.LogInformation("ConnectService shutting down - closing all WebSocket connections");

        _pendingRPCCleanupTimer?.Dispose();
        _pendingRPCCleanupTimer = null;

        _interNodeBroadcast.Dispose();
        _connectionManager.Dispose();

        _logger.LogInformation("ConnectService shutdown complete");
    }

    #endregion

}
