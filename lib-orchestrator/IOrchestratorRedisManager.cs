using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Orchestrator;
using ServiceHealthStatus = BeyondImmersion.BannouService.Orchestrator.ServiceHealthStatus;

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

    /// <summary>
    /// Write service heartbeat data to Redis.
    /// Called when heartbeat events are received from RabbitMQ.
    /// Pattern: service:heartbeat:{serviceId}:{appId}
    /// TTL: 90 seconds
    /// </summary>
    Task WriteServiceHeartbeatAsync(ServiceHeartbeatEvent heartbeat);

    /// <summary>
    /// Write service routing mapping to Redis for NGINX to read.
    /// Pattern: service:routing:{serviceName}
    /// Contains: app_id, host, port for NGINX to route requests.
    /// </summary>
    Task WriteServiceRoutingAsync(string serviceName, ServiceRouting routing);

    /// <summary>
    /// Get all service routing mappings from Redis.
    /// Used by NGINX Lua script for dynamic routing.
    /// </summary>
    Task<Dictionary<string, ServiceRouting>> GetServiceRoutingsAsync();

    /// <summary>
    /// Remove service routing mapping from Redis.
    /// Called when a service is unregistered.
    /// </summary>
    Task RemoveServiceRoutingAsync(string serviceName);
}

/// <summary>
/// Service routing information for NGINX.
/// Contains the app_id and host:port for external HTTP routing.
/// </summary>
public class ServiceRouting
{
    /// <summary>Dapr app-id for this service.</summary>
    public required string AppId { get; set; }

    /// <summary>Container/host name where the service is running.</summary>
    public required string Host { get; set; }

    /// <summary>Port the service is listening on.</summary>
    public int Port { get; set; } = 80;

    /// <summary>Service health status.</summary>
    public string Status { get; set; } = "unknown";

    /// <summary>Last update timestamp.</summary>
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Load percentage (0-100) for load balancing.</summary>
    public int LoadPercent { get; set; }
}

/// <summary>
/// Instance health status stored in Redis.
/// Represents the aggregated health state of a bannou app instance.
/// </summary>
public class InstanceHealthStatus
{
    /// <summary>Unique GUID identifying this bannou instance.</summary>
    public Guid InstanceId { get; set; }

    /// <summary>Dapr app-id for this instance.</summary>
    public required string AppId { get; set; }

    /// <summary>Overall instance health status.</summary>
    public required string Status { get; set; }

    /// <summary>When the last heartbeat was received.</summary>
    public DateTimeOffset LastSeen { get; set; }

    /// <summary>List of service names hosted by this instance.</summary>
    public List<string> Services { get; set; } = new();

    /// <summary>Current issues affecting this instance.</summary>
    public List<string>? Issues { get; set; }

    /// <summary>Maximum connections this instance can handle.</summary>
    public int MaxConnections { get; set; }

    /// <summary>Current active connections.</summary>
    public int CurrentConnections { get; set; }

    /// <summary>CPU usage percentage (0.0 - 1.0).</summary>
    public float CpuUsage { get; set; }

    /// <summary>Memory usage percentage (0.0 - 1.0).</summary>
    public float MemoryUsage { get; set; }
}
