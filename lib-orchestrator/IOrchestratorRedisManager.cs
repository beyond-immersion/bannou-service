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

    /// <summary>
    /// Clear all service routing mappings from Redis.
    /// Called when resetting to default topology.
    /// </summary>
    Task ClearAllServiceRoutingsAsync();

    /// <summary>
    /// Get the current configuration version number.
    /// </summary>
    Task<int> GetConfigVersionAsync();

    /// <summary>
    /// Save the current configuration state as a new version.
    /// Stores deployment topology for potential rollback.
    /// </summary>
    /// <param name="configuration">Configuration snapshot to save.</param>
    /// <returns>The new version number.</returns>
    Task<int> SaveConfigurationVersionAsync(DeploymentConfiguration configuration);

    /// <summary>
    /// Get a specific configuration version from history.
    /// </summary>
    /// <param name="version">Version number to retrieve.</param>
    /// <returns>Configuration at that version, or null if not found.</returns>
    Task<DeploymentConfiguration?> GetConfigurationVersionAsync(int version);

    /// <summary>
    /// Get the current active configuration.
    /// </summary>
    Task<DeploymentConfiguration?> GetCurrentConfigurationAsync();

    /// <summary>
    /// Restore a previous configuration version as the current configuration.
    /// </summary>
    /// <param name="version">Version to restore.</param>
    /// <returns>True if successful.</returns>
    Task<bool> RestoreConfigurationVersionAsync(int version);

    /// <summary>
    /// Clear the current configuration, resetting to default (no custom deployments).
    /// Saves an empty configuration as a new version for audit trail.
    /// </summary>
    /// <returns>The new version number.</returns>
    Task<int> ClearCurrentConfigurationAsync();

    #region Generic Storage Methods for Processing Pools

    /// <summary>
    /// Get a list of items from Redis.
    /// </summary>
    /// <typeparam name="T">Type of items in the list.</typeparam>
    /// <param name="key">Redis key.</param>
    /// <returns>List of items, or null if key doesn't exist.</returns>
    Task<List<T>?> GetListAsync<T>(string key) where T : class;

    /// <summary>
    /// Set a list of items in Redis.
    /// </summary>
    /// <typeparam name="T">Type of items in the list.</typeparam>
    /// <param name="key">Redis key.</param>
    /// <param name="items">Items to store.</param>
    Task SetListAsync<T>(string key, List<T> items) where T : class;

    /// <summary>
    /// Get a hash (dictionary) from Redis.
    /// </summary>
    /// <typeparam name="T">Type of values in the hash.</typeparam>
    /// <param name="key">Redis key.</param>
    /// <returns>Dictionary of key-value pairs, or null if key doesn't exist.</returns>
    Task<Dictionary<string, T>?> GetHashAsync<T>(string key) where T : class;

    /// <summary>
    /// Set a hash (dictionary) in Redis.
    /// </summary>
    /// <typeparam name="T">Type of values in the hash.</typeparam>
    /// <param name="key">Redis key.</param>
    /// <param name="hash">Dictionary to store.</param>
    Task SetHashAsync<T>(string key, Dictionary<string, T> hash) where T : class;

    /// <summary>
    /// Get a single value from Redis.
    /// </summary>
    /// <typeparam name="T">Type of the value.</typeparam>
    /// <param name="key">Redis key.</param>
    /// <returns>Value, or null if key doesn't exist.</returns>
    Task<T?> GetValueAsync<T>(string key) where T : class;

    /// <summary>
    /// Set a single value in Redis.
    /// </summary>
    /// <typeparam name="T">Type of the value.</typeparam>
    /// <param name="key">Redis key.</param>
    /// <param name="value">Value to store.</param>
    /// <param name="ttl">Optional TTL for the key.</param>
    Task SetValueAsync<T>(string key, T value, TimeSpan? ttl = null) where T : class;

    #endregion
}

/// <summary>
/// Configuration snapshot for deployment versioning.
/// Tracks which services are deployed with what settings.
/// </summary>
public class DeploymentConfiguration
{
    /// <summary>Version number of this configuration.</summary>
    public int Version { get; set; }

    /// <summary>When this configuration was created.</summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Deployment preset name used (if applicable).</summary>
    public string? PresetName { get; set; }

    /// <summary>Service configurations in this deployment.</summary>
    public Dictionary<string, ServiceDeploymentConfig> Services { get; set; } = new();

    /// <summary>Environment variables applied.</summary>
    public Dictionary<string, string> EnvironmentVariables { get; set; } = new();

    /// <summary>Reason/description for this configuration.</summary>
    public string? Description { get; set; }
}

/// <summary>
/// Service-level deployment configuration.
/// </summary>
public class ServiceDeploymentConfig
{
    /// <summary>Whether the service is enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Dapr app-id for the service.</summary>
    public string? AppId { get; set; }

    /// <summary>Number of replicas.</summary>
    public int Replicas { get; set; } = 1;

    /// <summary>Service-specific settings.</summary>
    public Dictionary<string, string> Settings { get; set; } = new();
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
