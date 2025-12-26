using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Mesh.Services;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Mesh;

/// <summary>
/// Implementation of the Mesh service.
/// Provides service discovery, endpoint registration, load balancing, and routing.
/// Uses direct Redis connection (NOT Dapr) to avoid circular dependencies.
/// </summary>
[DaprService("mesh", typeof(IMeshService), lifetime: ServiceLifetime.Scoped)]
[Obsolete]
public partial class MeshService : IMeshService
{
    private readonly IMessageBus _messageBus;
    private readonly ILogger<MeshService> _logger;
    private readonly MeshServiceConfiguration _configuration;
    private readonly IMeshRedisManager _redisManager;

    // Local cache for service mappings (thread-safe, updated via events)
    private static readonly ConcurrentDictionary<string, string> _serviceMappingsCache = new();
    private static long _mappingsCacheVersion = 0;
    private static readonly object _versionLock = new();

    // Round-robin counter for load balancing (per app-id)
    private static readonly ConcurrentDictionary<string, int> _roundRobinCounters = new();

    // Track service start time for uptime
    private static readonly DateTimeOffset _serviceStartTime = DateTimeOffset.UtcNow;

    // Default TTL for endpoint registrations (90 seconds)
    private const int DEFAULT_TTL_SECONDS = 90;

    /// <summary>
    /// Initializes a new instance of the MeshService class.
    /// </summary>
    /// <param name="messageBus">The message bus for pub/sub operations (replaces DaprClient).</param>
    /// <param name="logger">The logger.</param>
    /// <param name="configuration">The service configuration.</param>
    /// <param name="errorEventEmitter">The error event emitter.</param>
    /// <param name="redisManager">The Redis manager for direct Redis access.</param>
    /// <param name="eventConsumer">The event consumer for registering event handlers.</param>
    public MeshService(
        IMessageBus messageBus,
        ILogger<MeshService> logger,
        MeshServiceConfiguration configuration,
        IMeshRedisManager redisManager,
        IEventConsumer eventConsumer)
    {
        _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _redisManager = redisManager ?? throw new ArgumentNullException(nameof(redisManager));

        ArgumentNullException.ThrowIfNull(eventConsumer, nameof(eventConsumer));
        RegisterEventConsumers(eventConsumer);
    }

    /// <summary>
    /// Get endpoints for a specific app-id.
    /// Returns healthy endpoints by default, optionally including unhealthy ones.
    /// </summary>
    public async Task<(StatusCodes, GetEndpointsResponse?)> GetEndpointsAsync(
        GetEndpointsRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting endpoints for app {AppId}", body.AppId);

        try
        {
            var endpoints = await _redisManager.GetEndpointsForAppIdAsync(
                body.AppId,
                body.IncludeUnhealthy);

            // Filter by service name if specified
            if (!string.IsNullOrEmpty(body.ServiceName))
            {
                endpoints = endpoints
                    .Where(e => e.Services?.Contains(body.ServiceName) == true)
                    .ToList();
            }

            var response = new GetEndpointsResponse
            {
                AppId = body.AppId,
                Endpoints = endpoints,
                HealthyCount = endpoints.Count(e => e.Status == EndpointStatus.Healthy),
                TotalCount = endpoints.Count
            };

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting endpoints for app {AppId}", body.AppId);
            await _messageBus.TryPublishErrorAsync(
                "mesh",
                "GetEndpoints",
                ex.GetType().Name,
                ex.Message,
                dependency: "redis",
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// List all endpoints in the mesh.
    /// Admin operation for monitoring and debugging.
    /// </summary>
    public async Task<(StatusCodes, ListEndpointsResponse?)> ListEndpointsAsync(
        ListEndpointsRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Listing all endpoints, filter: {Filter}", body.AppIdFilter);

        try
        {
            var endpoints = await _redisManager.GetAllEndpointsAsync(body.AppIdFilter);

            // Group by status for summary
            var byStatus = endpoints.GroupBy(e => e.Status)
                .ToDictionary(g => g.Key, g => g.Count());

            var response = new ListEndpointsResponse
            {
                Endpoints = endpoints,
                Summary = new EndpointSummary
                {
                    TotalEndpoints = endpoints.Count,
                    HealthyCount = byStatus.GetValueOrDefault(EndpointStatus.Healthy, 0),
                    DegradedCount = byStatus.GetValueOrDefault(EndpointStatus.Degraded, 0),
                    UnavailableCount = byStatus.GetValueOrDefault(EndpointStatus.Unavailable, 0),
                    UniqueAppIds = endpoints.Select(e => e.AppId).Distinct().Count()
                }
            };

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing endpoints");
            await _messageBus.TryPublishErrorAsync(
                "mesh",
                "ListEndpoints",
                ex.GetType().Name,
                ex.Message,
                dependency: "redis",
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Register a new endpoint in the mesh.
    /// Called by services on startup to announce their availability.
    /// </summary>
    public async Task<(StatusCodes, RegisterEndpointResponse?)> RegisterEndpointAsync(
        RegisterEndpointRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Registering endpoint for app {AppId} at {Host}:{Port}",
            body.AppId, body.Host, body.Port);

        try
        {
            var instanceId = body.InstanceId != Guid.Empty ? body.InstanceId : Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;

            var endpoint = new MeshEndpoint
            {
                InstanceId = instanceId,
                AppId = body.AppId,
                Host = body.Host,
                Port = body.Port,
                Status = EndpointStatus.Healthy,
                Services = body.Services ?? new List<string>(),
                MaxConnections = body.MaxConnections,
                CurrentConnections = 0,
                LoadPercent = 0,
                RegisteredAt = now,
                LastSeen = now
            };

            var success = await _redisManager.RegisterEndpointAsync(endpoint, DEFAULT_TTL_SECONDS);

            if (!success)
            {
                _logger.LogWarning("Failed to register endpoint {InstanceId}", instanceId);
                return (StatusCodes.InternalServerError, null);
            }

            // Publish registration event
            await PublishEndpointRegisteredEventAsync(endpoint, cancellationToken);

            var response = new RegisterEndpointResponse
            {
                Success = true,
                Endpoint = endpoint,
                TtlSeconds = DEFAULT_TTL_SECONDS
            };

            return (StatusCodes.Created, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering endpoint for app {AppId}", body.AppId);
            await _messageBus.TryPublishErrorAsync(
                "mesh",
                "RegisterEndpoint",
                ex.GetType().Name,
                ex.Message,
                dependency: "redis",
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Deregister an endpoint from the mesh.
    /// Called on graceful shutdown.
    /// </summary>
    public async Task<(StatusCodes, object?)> DeregisterEndpointAsync(
        DeregisterEndpointRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Deregistering endpoint {InstanceId}", body.InstanceId);

        try
        {
            // Look up the endpoint to get appId for deregistration
            var endpoint = await _redisManager.GetEndpointByInstanceIdAsync(body.InstanceId);
            if (endpoint == null)
            {
                _logger.LogWarning("Endpoint {InstanceId} not found for deregistration", body.InstanceId);
                return (StatusCodes.NotFound, null);
            }

            var success = await _redisManager.DeregisterEndpointAsync(body.InstanceId, endpoint.AppId);

            if (!success)
            {
                _logger.LogWarning("Failed to deregister endpoint {InstanceId}", body.InstanceId);
                return (StatusCodes.NotFound, null);
            }

            // Publish deregistration event
            await PublishEndpointDeregisteredEventAsync(
                body.InstanceId,
                endpoint.AppId,
                "Graceful",
                cancellationToken);

            return (StatusCodes.NoContent, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deregistering endpoint {InstanceId}", body.InstanceId);
            await _messageBus.TryPublishErrorAsync(
                "mesh",
                "DeregisterEndpoint",
                ex.GetType().Name,
                ex.Message,
                dependency: "redis",
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Process heartbeat from an endpoint.
    /// Updates last seen timestamp and metrics, refreshes TTL.
    /// </summary>
    public async Task<(StatusCodes, HeartbeatResponse?)> HeartbeatAsync(
        HeartbeatRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Heartbeat from {InstanceId}, status: {Status}, load: {Load}%",
            body.InstanceId, body.Status, body.LoadPercent);

        try
        {
            // Look up the endpoint to get appId
            var endpoint = await _redisManager.GetEndpointByInstanceIdAsync(body.InstanceId);
            if (endpoint == null)
            {
                _logger.LogWarning(
                    "Heartbeat rejected for unknown endpoint {InstanceId}",
                    body.InstanceId);
                return (StatusCodes.NotFound, null);
            }

            var success = await _redisManager.UpdateHeartbeatAsync(
                body.InstanceId,
                endpoint.AppId,
                body.Status,
                body.LoadPercent,
                body.CurrentConnections,
                DEFAULT_TTL_SECONDS);

            if (!success)
            {
                _logger.LogWarning(
                    "Heartbeat update failed for endpoint {InstanceId}",
                    body.InstanceId);
                return (StatusCodes.NotFound, null);
            }

            var response = new HeartbeatResponse
            {
                Success = true,
                NextHeartbeatSeconds = Math.Max(DEFAULT_TTL_SECONDS / 3, 10),
                TtlSeconds = DEFAULT_TTL_SECONDS
            };

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing heartbeat for {InstanceId}", body.InstanceId);
            await _messageBus.TryPublishErrorAsync(
                "mesh",
                "Heartbeat",
                ex.GetType().Name,
                ex.Message,
                dependency: "redis",
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Get the best route for a service call.
    /// Applies load balancing algorithm to select optimal endpoint.
    /// </summary>
    public async Task<(StatusCodes, GetRouteResponse?)> GetRouteAsync(
        GetRouteRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting route for app {AppId}", body.AppId);

        try
        {
            var endpoints = await _redisManager.GetEndpointsForAppIdAsync(body.AppId, false);

            if (endpoints.Count == 0)
            {
                _logger.LogWarning("No healthy endpoints found for app {AppId}", body.AppId);
                return (StatusCodes.NotFound, null);
            }

            // Filter by service name if specified
            if (!string.IsNullOrEmpty(body.ServiceName))
            {
                endpoints = endpoints
                    .Where(e => e.Services?.Contains(body.ServiceName) == true)
                    .ToList();

                if (endpoints.Count == 0)
                {
                    _logger.LogWarning(
                        "No endpoints found for service {ServiceName} on app {AppId}",
                        body.ServiceName, body.AppId);
                    return (StatusCodes.NotFound, null);
                }
            }

            // Apply load balancing algorithm
            var selectedEndpoint = SelectEndpoint(endpoints, body.AppId, body.Algorithm);

            // Get alternates (other healthy endpoints)
            var alternates = endpoints
                .Where(e => e.InstanceId != selectedEndpoint.InstanceId)
                .Take(2)
                .ToList();

            var response = new GetRouteResponse
            {
                Endpoint = selectedEndpoint,
                Alternates = alternates
            };

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting route for app {AppId}", body.AppId);
            await _messageBus.TryPublishErrorAsync(
                "mesh",
                "GetRoute",
                ex.GetType().Name,
                ex.Message,
                dependency: "redis",
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Get the current service-to-app-id mappings.
    /// Used for routing decisions based on service name.
    /// </summary>
    public async Task<(StatusCodes, GetMappingsResponse?)> GetMappingsAsync(
        GetMappingsRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting service mappings");

        try
        {
            // Try local cache first
            Dictionary<string, string> mappings;
            long version;

            lock (_versionLock)
            {
                if (_serviceMappingsCache.Count > 0)
                {
                    mappings = _serviceMappingsCache.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                    version = _mappingsCacheVersion;
                }
                else
                {
                    mappings = new Dictionary<string, string>();
                    version = 0;
                }
            }

            // Refresh from Redis if cache is empty
            if (mappings.Count == 0)
            {
                mappings = await _redisManager.GetServiceMappingsAsync();
                version = await _redisManager.GetMappingsVersionAsync();

                // Update cache
                UpdateMappingsCache(mappings, version);
            }

            // Apply filter if specified
            if (!string.IsNullOrEmpty(body.ServiceNameFilter))
            {
                mappings = mappings
                    .Where(kvp => kvp.Key.StartsWith(body.ServiceNameFilter, StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            }

            var response = new GetMappingsResponse
            {
                Mappings = mappings,
                DefaultAppId = "bannou",
                Version = version
            };

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting service mappings");
            await _messageBus.TryPublishErrorAsync(
                "mesh",
                "GetMappings",
                ex.GetType().Name,
                ex.Message,
                dependency: "redis",
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Get mesh health status.
    /// Returns overall mesh health and component statuses.
    /// </summary>
    public async Task<(StatusCodes, MeshHealthResponse?)> GetHealthAsync(
        GetHealthRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting mesh health status");

        try
        {
            var (isHealthy, message, pingTime) = await _redisManager.CheckHealthAsync();

            // Get overall mesh statistics
            var endpoints = await _redisManager.GetAllEndpointsAsync();
            var healthyCount = endpoints.Count(e => e.Status == EndpointStatus.Healthy);
            var degradedCount = endpoints.Count(e => e.Status == EndpointStatus.Degraded);
            var unavailableCount = endpoints.Count(e => e.Status == EndpointStatus.Unavailable);

            // Determine overall status
            EndpointStatus overallStatus;
            if (!isHealthy || unavailableCount > healthyCount)
            {
                overallStatus = EndpointStatus.Unavailable;
            }
            else if (degradedCount > 0 || unavailableCount > 0)
            {
                overallStatus = EndpointStatus.Degraded;
            }
            else
            {
                overallStatus = EndpointStatus.Healthy;
            }

            // Calculate uptime
            var uptime = DateTimeOffset.UtcNow - _serviceStartTime;
            var uptimeString = FormatUptime(uptime);

            var response = new MeshHealthResponse
            {
                Status = overallStatus,
                Summary = new EndpointSummary
                {
                    TotalEndpoints = endpoints.Count,
                    HealthyCount = healthyCount,
                    DegradedCount = degradedCount,
                    UnavailableCount = unavailableCount,
                    UniqueAppIds = endpoints.Select(e => e.AppId).Distinct().Count()
                },
                RedisConnected = isHealthy,
                LastUpdateTime = DateTimeOffset.UtcNow,
                Uptime = uptimeString
            };

            // Include endpoints if requested
            if (body.IncludeEndpoints)
            {
                response.Endpoints = endpoints;
            }

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting mesh health");
            await _messageBus.TryPublishErrorAsync(
                "mesh",
                "GetHealth",
                ex.GetType().Name,
                ex.Message,
                dependency: "redis",
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #region Load Balancing

    /// <summary>
    /// Select an endpoint using the specified load balancing algorithm.
    /// </summary>
    private MeshEndpoint SelectEndpoint(
        List<MeshEndpoint> endpoints,
        string appId,
        LoadBalancerAlgorithm algorithm)
    {
        return algorithm switch
        {
            LoadBalancerAlgorithm.RoundRobin => SelectRoundRobin(endpoints, appId),
            LoadBalancerAlgorithm.LeastConnections => SelectLeastConnections(endpoints),
            LoadBalancerAlgorithm.Weighted => SelectWeighted(endpoints),
            LoadBalancerAlgorithm.Random => SelectRandom(endpoints),
            _ => SelectRoundRobin(endpoints, appId)
        };
    }

    private MeshEndpoint SelectRoundRobin(List<MeshEndpoint> endpoints, string appId)
    {
        var counter = _roundRobinCounters.AddOrUpdate(
            appId,
            0,
            (_, current) => (current + 1) % endpoints.Count);

        return endpoints[counter % endpoints.Count];
    }

    private static MeshEndpoint SelectLeastConnections(List<MeshEndpoint> endpoints)
    {
        return endpoints.OrderBy(e => e.CurrentConnections).First();
    }

    private static MeshEndpoint SelectWeighted(List<MeshEndpoint> endpoints)
    {
        // Weight by inverse of load (less loaded = higher weight)
        var weighted = endpoints
            .Select(e => (Endpoint: e, Weight: Math.Max(100 - e.LoadPercent, 1)))
            .ToList();

        var totalWeight = weighted.Sum(w => w.Weight);
        var random = Random.Shared.NextDouble() * totalWeight;

        double cumulative = 0;
        foreach (var (endpoint, weight) in weighted)
        {
            cumulative += weight;
            if (random <= cumulative)
            {
                return endpoint;
            }
        }

        return endpoints[0];
    }

    private static MeshEndpoint SelectRandom(List<MeshEndpoint> endpoints)
    {
        return endpoints[Random.Shared.Next(endpoints.Count)];
    }

    #endregion

    #region Event Publishing

    private async Task PublishEndpointRegisteredEventAsync(
        MeshEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        try
        {
            var evt = new
            {
                eventId = Guid.NewGuid(),
                timestamp = DateTimeOffset.UtcNow,
                instanceId = endpoint.InstanceId,
                appId = endpoint.AppId,
                host = endpoint.Host,
                port = endpoint.Port,
                services = endpoint.Services
            };

            await _messageBus.PublishAsync(
                "mesh.endpoint.registered",
                evt,
                cancellationToken: cancellationToken);

            _logger.LogDebug("Published endpoint registered event for {InstanceId}", endpoint.InstanceId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish endpoint registered event");
        }
    }

    private async Task PublishEndpointDeregisteredEventAsync(
        Guid instanceId,
        string appId,
        string reason,
        CancellationToken cancellationToken)
    {
        try
        {
            var evt = new
            {
                eventId = Guid.NewGuid(),
                timestamp = DateTimeOffset.UtcNow,
                instanceId = instanceId,
                appId = appId,
                reason = reason
            };

            await _messageBus.PublishAsync(
                "mesh.endpoint.deregistered",
                evt,
                cancellationToken: cancellationToken);

            _logger.LogDebug("Published endpoint deregistered event for {InstanceId}", instanceId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish endpoint deregistered event");
        }
    }

    #endregion

    #region Cache Management

    /// <summary>
    /// Update the local mappings cache with new values.
    /// Used by event handlers when FullServiceMappingsEvent is received.
    /// </summary>
    internal static bool UpdateMappingsCache(Dictionary<string, string> mappings, long version)
    {
        lock (_versionLock)
        {
            if (version <= _mappingsCacheVersion)
            {
                return false;
            }

            _serviceMappingsCache.Clear();
            foreach (var kvp in mappings)
            {
                _serviceMappingsCache[kvp.Key] = kvp.Value;
            }
            _mappingsCacheVersion = version;
            return true;
        }
    }

    /// <summary>
    /// Gets the current cache version. For testing purposes only.
    /// </summary>
    internal static long GetCacheVersion()
    {
        lock (_versionLock)
        {
            return _mappingsCacheVersion;
        }
    }

    /// <summary>
    /// Reset the static cache for test isolation. For testing purposes only.
    /// This method clears all cached mappings and resets the version to 0.
    /// </summary>
    internal static void ResetCacheForTesting()
    {
        lock (_versionLock)
        {
            _serviceMappingsCache.Clear();
            _mappingsCacheVersion = 0;
            _roundRobinCounters.Clear();
        }
    }

    #endregion

    #region Helpers

    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1)
        {
            return $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m";
        }
        if (uptime.TotalHours >= 1)
        {
            return $"{(int)uptime.TotalHours}h {uptime.Minutes}m";
        }
        return $"{uptime.Minutes}m {uptime.Seconds}s";
    }

    #endregion
}
