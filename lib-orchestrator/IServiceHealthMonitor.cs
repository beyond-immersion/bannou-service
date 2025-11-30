using BeyondImmersion.BannouService.Orchestrator;

namespace LibOrchestrator;

/// <summary>
/// Interface for monitoring service health based on ServiceHeartbeatEvent data.
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
}
