using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Mesh.Services;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Mesh;

/// <summary>
/// Implementation of the Mesh service.
/// Provides service discovery, endpoint registration, load balancing, and routing.
/// Uses lib-state via IMeshStateManager (NOT via mesh) to avoid circular dependencies.
/// Service mappings are managed via IServiceAppMappingResolver (shared across all services).
/// </summary>
[BannouService("mesh", typeof(IMeshService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.Infrastructure)]
public partial class MeshService : IMeshService
{
    private readonly IMessageBus _messageBus;
    private readonly ILogger<MeshService> _logger;
    private readonly MeshServiceConfiguration _configuration;
    private readonly IMeshStateManager _stateManager;
    private readonly IServiceAppMappingResolver _mappingResolver;

    // Round-robin counter for load balancing (per app-id)
    // Uses RoundRobinCounter from MeshServiceModels.cs for thread-safe atomic increments
    private static readonly ConcurrentDictionary<string, RoundRobinCounter> _roundRobinCounters = new();

    // Weighted round-robin current weights (key: "appId:instanceId" -> currentWeight)
    // Uses smooth weighted round-robin algorithm (nginx-style)
    private static readonly ConcurrentDictionary<string, double> _weightedRoundRobinCurrentWeights = new();

    // Track service start time for uptime
    private static readonly DateTimeOffset _serviceStartTime = DateTimeOffset.UtcNow;

    // TTL for endpoint registrations comes from _configuration.EndpointTtlSeconds

    /// <summary>
    /// Initializes a new instance of the MeshService class.
    /// </summary>
    /// <param name="messageBus">The message bus for pub/sub operations (replaces mesh client).</param>
    /// <param name="logger">The logger.</param>
    /// <param name="configuration">The service configuration.</param>
    /// <param name="stateManager">The state manager for endpoint registry access via lib-state.</param>
    /// <param name="mappingResolver">The service-to-app-id mapping resolver (shared across all services).</param>
    /// <param name="eventConsumer">The event consumer for registering event handlers.</param>
    public MeshService(
        IMessageBus messageBus,
        ILogger<MeshService> logger,
        MeshServiceConfiguration configuration,
        IMeshStateManager stateManager,
        IServiceAppMappingResolver mappingResolver,
        IEventConsumer eventConsumer)
    {
        _messageBus = messageBus;
        _logger = logger;
        _configuration = configuration;
        _stateManager = stateManager;
        _mappingResolver = mappingResolver;

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

        var endpoints = await _stateManager.GetEndpointsForAppIdAsync(
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

    /// <summary>
    /// List all endpoints in the mesh.
    /// Admin operation for monitoring and debugging.
    /// </summary>
    public async Task<(StatusCodes, ListEndpointsResponse?)> ListEndpointsAsync(
        ListEndpointsRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Listing all endpoints, appIdFilter: {AppIdFilter}, statusFilter: {StatusFilter}",
            body.AppIdFilter, body.StatusFilter);

        var endpoints = await _stateManager.GetAllEndpointsAsync(body.AppIdFilter);

        // Apply status filter if specified
        if (body.StatusFilter.HasValue)
        {
            endpoints = endpoints.Where(e => e.Status == body.StatusFilter.Value).ToList();
        }

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

            var success = await _stateManager.RegisterEndpointAsync(endpoint, _configuration.EndpointTtlSeconds);

            if (!success)
            {
                _logger.LogWarning("Failed to register endpoint {InstanceId}", instanceId);
                return (StatusCodes.InternalServerError, null);
            }

            // Publish registration event
            await PublishEndpointRegisteredEventAsync(endpoint, cancellationToken);

            var response = new RegisterEndpointResponse
            {
                Endpoint = endpoint,
                TtlSeconds = _configuration.EndpointTtlSeconds
            };

            return (StatusCodes.OK, response);
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
    public async Task<StatusCodes> DeregisterEndpointAsync(
        DeregisterEndpointRequest body,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Deregistering endpoint {InstanceId}", body.InstanceId);

        try
        {
            // Look up the endpoint to get appId for deregistration
            var endpoint = await _stateManager.GetEndpointByInstanceIdAsync(body.InstanceId);
            if (endpoint == null)
            {
                _logger.LogWarning("Endpoint {InstanceId} not found for deregistration", body.InstanceId);
                return StatusCodes.NotFound;
            }

            var success = await _stateManager.DeregisterEndpointAsync(body.InstanceId, endpoint.AppId);

            if (!success)
            {
                _logger.LogWarning("Failed to deregister endpoint {InstanceId}", body.InstanceId);
                return StatusCodes.NotFound;
            }

            // Publish deregistration event
            await PublishEndpointDeregisteredEventAsync(
                body.InstanceId,
                endpoint.AppId,
                MeshEndpointDeregisteredEventReason.Graceful,
                cancellationToken);

            return StatusCodes.OK;
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
            return StatusCodes.InternalServerError;
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
            var endpoint = await _stateManager.GetEndpointByInstanceIdAsync(body.InstanceId);
            if (endpoint == null)
            {
                _logger.LogWarning(
                    "Heartbeat rejected for unknown endpoint {InstanceId}",
                    body.InstanceId);
                return (StatusCodes.NotFound, null);
            }

            if (body.Issues != null && body.Issues.Count > 0)
            {
                _logger.LogDebug(
                    "Endpoint {InstanceId} reporting {IssueCount} issue(s): {Issues}",
                    body.InstanceId, body.Issues.Count, string.Join(", ", body.Issues));
            }

            var success = await _stateManager.UpdateHeartbeatAsync(
                body.InstanceId,
                endpoint.AppId,
                body.Status ?? EndpointStatus.Healthy,
                body.LoadPercent ?? 0,
                body.CurrentConnections ?? 0,
                body.Issues,
                _configuration.EndpointTtlSeconds);

            if (!success)
            {
                _logger.LogWarning(
                    "Heartbeat update failed for endpoint {InstanceId}",
                    body.InstanceId);
                return (StatusCodes.NotFound, null);
            }

            var response = new HeartbeatResponse
            {
                NextHeartbeatSeconds = _configuration.HeartbeatIntervalSeconds,
                TtlSeconds = _configuration.EndpointTtlSeconds
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
            var endpoints = await _stateManager.GetEndpointsForAppIdAsync(body.AppId, false);

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

            // Filter out degraded endpoints (stale heartbeat)
            var degradationThreshold = DateTimeOffset.UtcNow.AddSeconds(-_configuration.DegradationThresholdSeconds);
            var healthyEndpoints = endpoints
                .Where(e => e.LastSeen >= degradationThreshold)
                .ToList();

            // Filter out overloaded endpoints (above load threshold)
            if (healthyEndpoints.Count > 1)
            {
                var underThreshold = healthyEndpoints
                    .Where(e => e.LoadPercent <= _configuration.LoadThresholdPercent)
                    .ToList();
                if (underThreshold.Count > 0)
                {
                    healthyEndpoints = underThreshold;
                }
            }

            // Fall back to all endpoints if filtering removed everything
            if (healthyEndpoints.Count == 0)
            {
                healthyEndpoints = endpoints;
            }

            // Determine effective algorithm (use configured default when null or not specified)
            var effectiveAlgorithm = body.Algorithm ?? (LoadBalancerAlgorithm)_configuration.DefaultLoadBalancer;

            // Apply load balancing algorithm
            var selectedEndpoint = SelectEndpoint(healthyEndpoints, body.AppId, effectiveAlgorithm);

            // Get alternates (other healthy endpoints)
            var alternates = healthyEndpoints
                .Where(e => e.InstanceId != selectedEndpoint.InstanceId)
                .Take(_configuration.MaxTopEndpointsReturned)
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
    /// Mappings are managed via IServiceAppMappingResolver, updated by FullServiceMappingsEvent from RabbitMQ.
    /// </summary>
    public async Task<(StatusCodes, GetMappingsResponse?)> GetMappingsAsync(
        GetMappingsRequest body,
        CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        _logger.LogDebug("Getting service mappings");

        // Get mappings from shared resolver (populated via RabbitMQ events)
        var allMappings = _mappingResolver.GetAllMappings();
        var version = _mappingResolver.CurrentVersion;

        // Apply filter if specified
        Dictionary<string, string> mappings;
        if (!string.IsNullOrEmpty(body.ServiceNameFilter))
        {
            mappings = allMappings
                .Where(kvp => kvp.Key.StartsWith(body.ServiceNameFilter, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }
        else
        {
            mappings = new Dictionary<string, string>(allMappings);
        }

        var response = new GetMappingsResponse
        {
            Mappings = mappings,
            DefaultAppId = AppConstants.DEFAULT_APP_NAME,
            Version = version
        };

        return (StatusCodes.OK, response);
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
            var (isHealthy, _, _) = await _stateManager.CheckHealthAsync();

            // Get overall mesh statistics
            var endpoints = await _stateManager.GetAllEndpointsAsync();
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
            LoadBalancerAlgorithm.WeightedRoundRobin => SelectWeightedRoundRobin(endpoints, appId),
            LoadBalancerAlgorithm.Random => SelectRandom(endpoints),
            _ => SelectRoundRobin(endpoints, appId)
        };
    }

    private MeshEndpoint SelectRoundRobin(List<MeshEndpoint> endpoints, string appId)
    {
        // Enforce cache size limit if configured (0 means unlimited)
        if (_configuration.LoadBalancingStateMaxAppIds > 0 &&
            _roundRobinCounters.Count >= _configuration.LoadBalancingStateMaxAppIds &&
            !_roundRobinCounters.ContainsKey(appId))
        {
            // Evict oldest entry (FIFO approximation - ConcurrentDictionary doesn't track order,
            // so we evict an arbitrary entry which provides eventual fairness)
            var keyToRemove = _roundRobinCounters.Keys.FirstOrDefault();
            if (keyToRemove != null)
            {
                _roundRobinCounters.TryRemove(keyToRemove, out _);
            }
        }

        var counter = _roundRobinCounters.GetOrAdd(appId, _ => new RoundRobinCounter());
        var index = counter.GetNext() % endpoints.Count;
        return endpoints[index];
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

    /// <summary>
    /// Smooth weighted round-robin algorithm (nginx-style).
    /// Combines predictable round-robin ordering with load-based weighting.
    /// Less loaded endpoints receive proportionally more requests while
    /// maintaining a deterministic distribution pattern.
    /// </summary>
    private MeshEndpoint SelectWeightedRoundRobin(List<MeshEndpoint> endpoints, string appId)
    {
        // Calculate effective weights based on inverse of load (less loaded = higher weight)
        var weighted = endpoints
            .Select(e => (
                Endpoint: e,
                Key: $"{appId}:{e.InstanceId}",
                EffectiveWeight: Math.Max(100 - e.LoadPercent, 1)))
            .ToList();

        var totalEffectiveWeight = weighted.Sum(w => w.EffectiveWeight);

        // Enforce cache size limit if configured (0 means unlimited)
        // Note: This is a soft limit - we evict before adding new keys, but the dictionary
        // uses endpoint keys which are more granular than appId keys
        if (_configuration.LoadBalancingStateMaxAppIds > 0)
        {
            var currentUniqueAppIds = _weightedRoundRobinCurrentWeights.Keys
                .Select(k => k.Split(':')[0])
                .Distinct()
                .Count();

            if (currentUniqueAppIds >= _configuration.LoadBalancingStateMaxAppIds &&
                !_weightedRoundRobinCurrentWeights.Keys.Any(k => k.StartsWith($"{appId}:")))
            {
                // Evict all entries for an arbitrary app-id to make room
                var appIdToRemove = _weightedRoundRobinCurrentWeights.Keys
                    .Select(k => k.Split(':')[0])
                    .FirstOrDefault();

                if (appIdToRemove != null)
                {
                    foreach (var key in _weightedRoundRobinCurrentWeights.Keys
                        .Where(k => k.StartsWith($"{appIdToRemove}:"))
                        .ToList())
                    {
                        _weightedRoundRobinCurrentWeights.TryRemove(key, out _);
                    }
                }
            }
        }

        // Update current weights and find the endpoint with highest current weight
        MeshEndpoint? selected = null;
        string? selectedKey = null;
        double highestCurrentWeight = double.MinValue;

        foreach (var (endpoint, key, effectiveWeight) in weighted)
        {
            // Increment current weight by effective weight
            var currentWeight = _weightedRoundRobinCurrentWeights.AddOrUpdate(
                key,
                effectiveWeight,
                (_, current) => current + effectiveWeight);

            if (currentWeight > highestCurrentWeight)
            {
                highestCurrentWeight = currentWeight;
                selected = endpoint;
                selectedKey = key;
            }
        }

        // Reduce selected endpoint's current weight by total effective weight
        if (selectedKey != null)
        {
            _weightedRoundRobinCurrentWeights.AddOrUpdate(
                selectedKey,
                0,
                (_, current) => current - totalEffectiveWeight);
        }

        return selected ?? endpoints[0];
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
            var evt = new MeshEndpointRegisteredEvent
            {
                EventName = "mesh.endpoint_registered",
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                InstanceId = endpoint.InstanceId,
                AppId = endpoint.AppId,
                Host = endpoint.Host,
                Port = endpoint.Port,
                Services = endpoint.Services
            };

            await _messageBus.TryPublishAsync(
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
        MeshEndpointDeregisteredEventReason reason,
        CancellationToken cancellationToken)
    {
        try
        {
            var evt = new MeshEndpointDeregisteredEvent
            {
                EventName = "mesh.endpoint_deregistered",
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                InstanceId = instanceId,
                AppId = appId,
                Reason = reason
            };

            await _messageBus.TryPublishAsync(
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

    #region Test Helpers

    /// <summary>
    /// Reset the static load balancing state for test isolation. For testing purposes only.
    /// Service mappings are managed by IServiceAppMappingResolver (use ClearAllMappingsForTests there).
    /// </summary>
    internal static void ResetLoadBalancingStateForTesting()
    {
        _roundRobinCounters.Clear();
        _weightedRoundRobinCurrentWeights.Clear();
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

    #region Permission Registration

    /// <summary>
    /// Registers this service's API permissions with the Permission service on startup.
    /// Overrides the default IBannouService implementation to use generated permission data.
    /// </summary>
    public async Task RegisterServicePermissionsAsync(string appId)
    {
        _logger.LogInformation("Registering Mesh service permissions...");
        await MeshPermissionRegistration.RegisterViaEventAsync(_messageBus, appId, _logger);
    }

    #endregion
}
