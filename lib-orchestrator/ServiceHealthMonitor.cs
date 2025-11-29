using Microsoft.Extensions.Logging;
using BeyondImmersion.BannouService.Orchestrator;

namespace LibOrchestrator;

/// <summary>
/// Monitors service health based on ServiceHeartbeatEvent data from Redis.
/// Uses existing heartbeat schema from common-events.yaml.
/// </summary>
public class ServiceHealthMonitor
{
    private readonly ILogger<ServiceHealthMonitor> _logger;
    private readonly OrchestratorServiceConfiguration _configuration;
    private readonly OrchestratorRedisManager _redisManager;
    private readonly OrchestratorEventManager _eventManager;

    public ServiceHealthMonitor(
        ILogger<ServiceHealthMonitor> logger,
        OrchestratorServiceConfiguration configuration,
        OrchestratorRedisManager redisManager,
        OrchestratorEventManager eventManager)
    {
        _logger = logger;
        _configuration = configuration;
        _redisManager = redisManager;
        _eventManager = eventManager;

        // Subscribe to real-time heartbeat events from RabbitMQ
        _eventManager.HeartbeatReceived += OnHeartbeatReceived;
    }

    /// <summary>
    /// Handle real-time heartbeat events from RabbitMQ.
    /// </summary>
    private void OnHeartbeatReceived(ServiceHeartbeatEvent heartbeat)
    {
        _logger.LogDebug(
            "Real-time heartbeat: {ServiceId}:{AppId} - {Status} (Connections: {Current}/{Max})",
            heartbeat.ServiceId,
            heartbeat.AppId,
            heartbeat.Status,
            heartbeat.Capacity?.CurrentConnections,
            heartbeat.Capacity?.MaxConnections);
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
