using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Orchestrator;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using ServiceMappingAction = BeyondImmersion.BannouService.Events.ServiceMappingEventAction;

namespace LibOrchestrator;

/// <summary>
/// Monitors service health based on ServiceHeartbeatEvent data from Redis.
/// Uses existing heartbeat schema from common-events.yaml.
/// Writes heartbeat data and service routing to Redis for NGINX to read.
/// </summary>
public class ServiceHealthMonitor : IServiceHealthMonitor
{
    private readonly ILogger<ServiceHealthMonitor> _logger;
    private readonly OrchestratorServiceConfiguration _configuration;
    private readonly IOrchestratorRedisManager _redisManager;
    private readonly IOrchestratorEventManager _eventManager;

    // Cache of current service routings to detect changes
    private readonly ConcurrentDictionary<string, ServiceRouting> _currentRoutings = new();

    public ServiceHealthMonitor(
        ILogger<ServiceHealthMonitor> logger,
        OrchestratorServiceConfiguration configuration,
        IOrchestratorRedisManager redisManager,
        IOrchestratorEventManager eventManager)
    {
        _logger = logger;
        _configuration = configuration;
        _redisManager = redisManager;
        _eventManager = eventManager;

        // Subscribe to real-time heartbeat events from RabbitMQ
        _eventManager.HeartbeatReceived += OnHeartbeatReceived;

        // Subscribe to service mapping events from RabbitMQ
        _eventManager.ServiceMappingReceived += OnServiceMappingReceived;
    }

    /// <summary>
    /// Handle real-time heartbeat events from RabbitMQ.
    /// Writes heartbeat to Redis and updates service routing for each service in the heartbeat.
    /// </summary>
    private void OnHeartbeatReceived(ServiceHeartbeatEvent heartbeat)
    {
        var serviceNames = heartbeat.Services?.Select(s => s.ServiceName) ?? Enumerable.Empty<string>();
        _logger.LogDebug(
            "Aggregated heartbeat from {AppId}: InstanceId={InstanceId}, Status={Status}, Services=[{Services}]",
            heartbeat.AppId,
            heartbeat.ServiceId,
            heartbeat.Status,
            string.Join(", ", serviceNames));

        // Write heartbeat to Redis (fire and forget, but log errors)
        _ = WriteHeartbeatAndUpdateRoutingAsync(heartbeat);
    }

    /// <summary>
    /// Handle service mapping events from RabbitMQ.
    /// Updates Redis routing when topology changes.
    /// </summary>
    private void OnServiceMappingReceived(ServiceMappingEvent mappingEvent)
    {
        _logger.LogInformation(
            "Service mapping event: {ServiceName} -> {AppId} ({Action})",
            mappingEvent.ServiceName,
            mappingEvent.AppId,
            mappingEvent.Action);

        _ = UpdateServiceRoutingFromMappingAsync(mappingEvent);
    }

    /// <summary>
    /// Write heartbeat to Redis and update routing for each service in the heartbeat.
    /// Heartbeats can only initialize routing for new services or update status/load for services
    /// already routed to this app-id. They cannot change the app-id for a service - only
    /// explicit ServiceMappingEvents from orchestrator can do that.
    /// </summary>
    private async Task WriteHeartbeatAndUpdateRoutingAsync(ServiceHeartbeatEvent heartbeat)
    {
        try
        {
            // Write instance heartbeat to Redis
            await _redisManager.WriteServiceHeartbeatAsync(heartbeat);

            // Update routing for each service in the aggregated heartbeat
            if (heartbeat.Services == null || heartbeat.Services.Count == 0)
            {
                _logger.LogWarning("Heartbeat from {AppId} contains no services", heartbeat.AppId);
                return;
            }

            var loadPercent = CalculateLoadPercent(heartbeat);

            foreach (var serviceStatus in heartbeat.Services)
            {
                var serviceName = serviceStatus.ServiceName;

                // Check if routing already exists for this service
                if (_currentRoutings.TryGetValue(serviceName, out var existingRouting))
                {
                    // Routing exists - only update if heartbeat is from the SAME app-id
                    // This prevents heartbeats from overwriting explicit routing set by orchestrator
                    if (!string.Equals(existingRouting.AppId, heartbeat.AppId, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogDebug(
                            "Ignoring heartbeat for {ServiceName} from {HeartbeatAppId} - service is routed to {RoutedAppId}",
                            serviceName, heartbeat.AppId, existingRouting.AppId);
                        continue;
                    }

                    // Same app-id - update status and load only
                    existingRouting.Status = serviceStatus.Status.ToString().ToLowerInvariant();
                    existingRouting.LoadPercent = loadPercent;
                    existingRouting.LastUpdated = DateTimeOffset.UtcNow;

                    await _redisManager.WriteServiceRoutingAsync(serviceName, existingRouting);

                    _logger.LogDebug(
                        "Updated health for {ServiceName} on {AppId} (status: {Status}, load: {Load}%)",
                        serviceName, heartbeat.AppId, serviceStatus.Status, loadPercent);
                }
                else
                {
                    // No existing routing - heartbeat can initialize it
                    var newRouting = new ServiceRouting
                    {
                        AppId = heartbeat.AppId,
                        Host = heartbeat.AppId, // In Docker, container name = app_id
                        Port = 80,
                        Status = serviceStatus.Status.ToString().ToLowerInvariant(),
                        LoadPercent = loadPercent
                    };

                    await _redisManager.WriteServiceRoutingAsync(serviceName, newRouting);
                    _currentRoutings[serviceName] = newRouting;

                    _logger.LogInformation(
                        "Initialized routing for {ServiceName} -> {AppId} (status: {Status})",
                        serviceName, heartbeat.AppId, serviceStatus.Status);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write heartbeat/routing for instance {InstanceId} ({AppId})",
                heartbeat.ServiceId, heartbeat.AppId);
        }
    }

    /// <summary>
    /// Update service routing from explicit mapping event.
    /// </summary>
    private async Task UpdateServiceRoutingFromMappingAsync(ServiceMappingEvent mappingEvent)
    {
        try
        {
            if (mappingEvent.Action == ServiceMappingAction.Unregister)
            {
                await _redisManager.RemoveServiceRoutingAsync(mappingEvent.ServiceName);
                _currentRoutings.TryRemove(mappingEvent.ServiceName, out _);
            }
            else
            {
                var routing = new ServiceRouting
                {
                    AppId = mappingEvent.AppId,
                    Host = mappingEvent.AppId,
                    Port = 80,
                    Status = "healthy"
                };

                await _redisManager.WriteServiceRoutingAsync(mappingEvent.ServiceName, routing);
                _currentRoutings[mappingEvent.ServiceName] = routing;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update routing from mapping event for {ServiceName}",
                mappingEvent.ServiceName);
        }
    }

    /// <summary>
    /// Calculate load percentage from heartbeat capacity data.
    /// </summary>
    private static int CalculateLoadPercent(ServiceHeartbeatEvent heartbeat)
    {
        if (heartbeat.Capacity == null || heartbeat.Capacity.MaxConnections == 0)
        {
            return 0;
        }

        return (int)((double)heartbeat.Capacity.CurrentConnections /
                    heartbeat.Capacity.MaxConnections * 100);
    }

    /// <summary>
    /// Get comprehensive health report for all services.
    /// Reads heartbeat data from Redis and evaluates health status.
    /// </summary>
    public async Task<ServiceHealthReport> GetServiceHealthReportAsync()
    {
        var heartbeats = await _redisManager.GetServiceHeartbeatsAsync();
        var now = DateTimeOffset.UtcNow;
        var heartbeatTimeout = TimeSpan.FromSeconds(_configuration.HeartbeatTimeoutSeconds);

        var healthyServices = new List<ServiceHealthStatus>();
        var unhealthyServices = new List<ServiceHealthStatus>();

        foreach (var heartbeat in heartbeats)
        {
            var timeSinceLastHeartbeat = now - heartbeat.LastSeen;
            var isExpired = timeSinceLastHeartbeat > heartbeatTimeout;

            if (isExpired || heartbeat.Status == "unavailable" || heartbeat.Status == "shutting_down")
            {
                unhealthyServices.Add(heartbeat);
            }
            else
            {
                healthyServices.Add(heartbeat);
            }
        }

        var totalServices = healthyServices.Count + unhealthyServices.Count;
        var healthPercentage = totalServices > 0
            ? (float)healthyServices.Count / totalServices * 100
            : 0.0f;

        return new ServiceHealthReport
        {
            Timestamp = now,
            TotalServices = totalServices,
            HealthPercentage = healthPercentage,
            HealthyServices = healthyServices,
            UnhealthyServices = unhealthyServices
        };
    }

    /// <summary>
    /// Check if a specific service needs restart based on health metrics.
    /// Returns recommendation aligned with smart restart logic (5-minute degradation threshold).
    /// </summary>
    public async Task<RestartRecommendation> ShouldRestartServiceAsync(string serviceName)
    {
        // Get all heartbeats for this service (may be multiple app-ids)
        var allHeartbeats = await _redisManager.GetServiceHeartbeatsAsync();
        var serviceHeartbeats = allHeartbeats
            .Where(h => h.ServiceId == serviceName)
            .ToList();

        if (!serviceHeartbeats.Any())
        {
            return new RestartRecommendation
            {
                ShouldRestart = true,
                ServiceName = serviceName,
                CurrentStatus = "unavailable",
                Reason = "No heartbeat data found - service appears to be down"
            };
        }

        // Check the worst status among all instances
        var worstStatus = DetermineWorstStatus(serviceHeartbeats);
        var latestHeartbeat = serviceHeartbeats
            .OrderByDescending(h => h.LastSeen)
            .First();

        var timeSinceLastHeartbeat = DateTimeOffset.UtcNow - latestHeartbeat.LastSeen;
        var degradationThreshold = TimeSpan.FromMinutes(_configuration.DegradationThresholdMinutes);

        // Smart restart logic:
        // - Healthy: No restart
        // - Degraded < 5 min: No restart (transient issue)
        // - Degraded > 5 min: Restart recommended
        // - Unavailable: Restart needed
        bool shouldRestart = false;
        string reason;
        string? degradedDuration = null;

        if (worstStatus == "unavailable" || worstStatus == "shutting_down")
        {
            shouldRestart = true;
            reason = $"Service is {worstStatus}";
        }
        else if (worstStatus == "degraded" || worstStatus == "overloaded")
        {
            if (timeSinceLastHeartbeat > degradationThreshold)
            {
                shouldRestart = true;
                degradedDuration = timeSinceLastHeartbeat.ToString(@"hh\:mm\:ss");
                reason = $"Service has been {worstStatus} for {degradedDuration} (threshold: {degradationThreshold.TotalMinutes} minutes)";
            }
            else
            {
                shouldRestart = false;
                degradedDuration = timeSinceLastHeartbeat.ToString(@"hh\:mm\:ss");
                reason = $"Service is {worstStatus} but within threshold ({degradedDuration} < {degradationThreshold.TotalMinutes} minutes)";
            }
        }
        else
        {
            shouldRestart = false;
            reason = $"Service is {worstStatus} - no restart needed";
        }

        return new RestartRecommendation
        {
            ShouldRestart = shouldRestart,
            ServiceName = serviceName,
            CurrentStatus = worstStatus,
            LastSeen = latestHeartbeat.LastSeen,
            DegradedDuration = degradedDuration ?? string.Empty,
            Reason = reason
        };
    }

    /// <summary>
    /// Determine the worst status among multiple service instances.
    /// Priority: unavailable > shutting_down > overloaded > degraded > healthy
    /// </summary>
    private string DetermineWorstStatus(List<ServiceHealthStatus> heartbeats)
    {
        var statusPriority = new Dictionary<string, int>
        {
            { "unavailable", 5 },
            { "shutting_down", 4 },
            { "overloaded", 3 },
            { "degraded", 2 },
            { "healthy", 1 }
        };

        var worstStatus = "healthy";
        var worstPriority = 0;

        foreach (var heartbeat in heartbeats)
        {
            var status = heartbeat.Status ?? "unavailable";
            if (statusPriority.TryGetValue(status, out var priority))
            {
                if (priority > worstPriority)
                {
                    worstPriority = priority;
                    worstStatus = status;
                }
            }
        }

        return worstStatus;
    }

    /// <summary>
    /// Get health status for a specific service instance.
    /// </summary>
    public async Task<ServiceHealthStatus?> GetServiceHealthStatusAsync(string serviceId, string appId)
    {
        return await _redisManager.GetServiceHeartbeatAsync(serviceId, appId);
    }
}
