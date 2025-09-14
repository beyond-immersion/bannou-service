using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.Permissions;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.ServiceClients;
using Dapr.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
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
    public Task<(StatusCodes, Dictionary<string, string>?)> GetServiceMappingsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var mappings = _appMappingResolver.GetAllMappings();
            var result = new Dictionary<string, string>(mappings);

            // Add default mapping info if no custom mappings exist
            if (result.Count == 0)
            {
                result["_default"] = "bannou";
                result["_info"] = "All services routing to default 'bannou' app-id";
            }

            _logger.LogDebug("Returning {Count} service mappings", result.Count);
            return Task.FromResult<(StatusCodes, Dictionary<string, string>?)>((StatusCodes.OK, result));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving service mappings");
            return Task.FromResult<(StatusCodes, Dictionary<string, string>?)>((StatusCodes.InternalServerError, null));
        }
    }
}