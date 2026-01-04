using BeyondImmersion.BannouService.Orchestrator;

namespace LibOrchestrator;

/// <summary>
/// Interface for monitoring service health based on ServiceHeartbeatEvent data.
/// Writes heartbeat data and service routing to Redis for NGINX to read.
/// Publishes FullServiceMappingsEvent periodically and on routing changes.
/// Enables unit testing through mocking.
/// </summary>
public interface IServiceHealthMonitor
{
    /// <summary>
    /// Get comprehensive health report for all services.
    /// Reads heartbeat data from Redis and evaluates health status.
    /// </summary>
    Task<ServiceHealthReport> GetServiceHealthReportAsync();

    /// <summary>
    /// Check if a specific service needs restart based on health metrics.
    /// Returns recommendation aligned with smart restart logic (5-minute degradation threshold).
    /// </summary>
    Task<RestartRecommendation> ShouldRestartServiceAsync(string serviceName);

    /// <summary>
    /// Get health status for a specific service instance.
    /// </summary>
    Task<ServiceHealthStatus?> GetServiceHealthStatusAsync(string serviceId, string appId);

    /// <summary>
    /// Update service routing for a specific service. Called by OrchestratorService during deployment.
    /// This is the authoritative way to set routing - heartbeats cannot override these.
    /// </summary>
    Task SetServiceRoutingAsync(string serviceName, string appId);

    /// <summary>
    /// Restore service routing to default. Called by OrchestratorService during teardown.
    /// Instead of deleting the routing entry (which causes proxies to fall back to hardcoded defaults),
    /// this sets the routing to the orchestrator's EffectiveAppId.
    /// </summary>
    Task RestoreServiceRoutingToDefaultAsync(string serviceName);

    /// <summary>
    /// Publish the complete service mappings to all bannou instances.
    /// Called periodically (every 30s) and immediately when routing changes.
    /// </summary>
    Task PublishFullMappingsAsync(string reason);

    /// <summary>
    /// Reset all service mappings to default ("bannou").
    /// Called when resetting to default topology.
    /// Clears all custom routing and publishes updated mappings.
    /// </summary>
    Task ResetAllMappingsToDefaultAsync();
}
