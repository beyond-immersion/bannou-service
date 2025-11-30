using BeyondImmersion.BannouService.Orchestrator;

namespace LibOrchestrator;

/// <summary>
/// Interface for managing direct Redis connections for orchestrator service.
/// Enables unit testing through mocking.
/// </summary>
public interface IOrchestratorRedisManager : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Initialize Redis connection with wait-on-startup retry logic.
    /// Uses exponential backoff to handle infrastructure startup delays.
    /// </summary>
    /// <returns>True if connection successful, false otherwise.</returns>
    Task<bool> InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all service heartbeat data from Redis.
    /// Pattern: service:heartbeat:{serviceId}:{appId}
    /// TTL: 90 seconds (from ServiceHeartbeatEvent schema)
    /// </summary>
    Task<List<ServiceHealthStatus>> GetServiceHeartbeatsAsync();

    /// <summary>
    /// Check if Redis is currently connected and healthy.
    /// </summary>
    Task<(bool IsHealthy, string? Message, TimeSpan? PingTime)> CheckHealthAsync();

    /// <summary>
    /// Get specific service heartbeat by serviceId and appId.
    /// </summary>
    Task<ServiceHealthStatus?> GetServiceHeartbeatAsync(string serviceId, string appId);
}
