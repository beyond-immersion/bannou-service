using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Orchestrator;

namespace LibOrchestrator;

/// <summary>
/// Interface for managing orchestrator state via lib-state infrastructure.
/// Replaces direct Redis connections with proper infrastructure lib abstraction.
/// </summary>
public interface IOrchestratorStateManager : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Initialize state stores with retry logic for infrastructure startup delays.
    /// </summary>
    /// <returns>True if initialization successful, false otherwise.</returns>
    Task<bool> InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all service heartbeat data.
    /// Uses index-based pattern to avoid KEYS/SCAN operations.
    /// </summary>
    Task<List<ServiceHealthEntry>> GetServiceHeartbeatsAsync();

    /// <summary>
    /// Check if state stores are connected and healthy.
    /// </summary>
    Task<(bool IsHealthy, string? Message, TimeSpan? OperationTime)> CheckHealthAsync();

    /// <summary>
    /// Get specific service heartbeat by serviceId and appId.
    /// </summary>
    Task<ServiceHealthEntry?> GetServiceHeartbeatAsync(string serviceId, string appId);

    /// <summary>
    /// Write service heartbeat data.
    /// Called when heartbeat events are received from message bus.
    /// TTL: 90 seconds
    /// </summary>
    Task WriteServiceHeartbeatAsync(ServiceHeartbeatEvent heartbeat);

    /// <summary>
    /// Write service routing mapping for dynamic routing.
    /// </summary>
    Task WriteServiceRoutingAsync(string serviceName, ServiceRouting routing);

    /// <summary>
    /// Get a specific service routing mapping.
    /// </summary>
    /// <param name="serviceName">Name of the service.</param>
    /// <returns>The routing if found, null otherwise.</returns>
    Task<ServiceRouting?> GetServiceRoutingAsync(string serviceName);

    /// <summary>
    /// Get all service routing mappings.
    /// Uses index-based pattern to avoid KEYS/SCAN operations.
    /// </summary>
    Task<Dictionary<string, ServiceRouting>> GetServiceRoutingsAsync();

    /// <summary>
    /// Remove service routing mapping.
    /// Called when a service is unregistered.
    /// </summary>
    Task RemoveServiceRoutingAsync(string serviceName);

    /// <summary>
    /// Clear all service routing mappings.
    /// Called when resetting to default topology.
    /// </summary>
    Task ClearAllServiceRoutingsAsync();

    /// <summary>
    /// Set all known service routings to the default app-id.
    /// Unlike ClearAllServiceRoutingsAsync which deletes routes (causing fallback to hardcoded defaults),
    /// this method explicitly sets each service to route to the specified default app-id.
    /// This ensures routing proxies (like OpenResty) have explicit routes rather than relying on fallbacks.
    /// </summary>
    /// <param name="defaultAppId">The default app-id to route all services to (typically "bannou").</param>
    /// <returns>List of service names that were set to default.</returns>
    Task<List<string>> SetAllServiceRoutingsToDefaultAsync(string defaultAppId);

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
    /// Get a list of items.
    /// </summary>
    Task<List<T>?> GetListAsync<T>(string key) where T : class;

    /// <summary>
    /// Set a list of items.
    /// </summary>
    Task SetListAsync<T>(string key, List<T> items) where T : class;

    /// <summary>
    /// Get a hash (dictionary).
    /// </summary>
    Task<Dictionary<string, T>?> GetHashAsync<T>(string key) where T : class;

    /// <summary>
    /// Set a hash (dictionary).
    /// </summary>
    Task SetHashAsync<T>(string key, Dictionary<string, T> hash) where T : class;

    /// <summary>
    /// Get a single value.
    /// </summary>
    Task<T?> GetValueAsync<T>(string key) where T : class;

    /// <summary>
    /// Set a single value.
    /// </summary>
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

    /// <summary>Unique identifier for this deployment instance.</summary>
    public string? DeploymentId { get; set; }

    /// <summary>Deployment preset name used (if applicable).</summary>
    public string? PresetName { get; set; }

    /// <summary>Service configurations in this deployment.</summary>
    public Dictionary<string, ServiceDeploymentConfig> Services { get; set; } = new();

    /// <summary>Environment variables applied.</summary>
    public Dictionary<string, string> EnvironmentVariables { get; set; } = new();

    /// <summary>Reason/description for this configuration.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Previous deployment state for rollback purposes.
    /// Contains the state before this deployment was applied.
    /// Only one level deep - PreviousDeploymentState.PreviousDeploymentState is always null.
    /// </summary>
    public DeploymentConfiguration? PreviousDeploymentState { get; set; }
}

/// <summary>
/// Service-level deployment configuration.
/// </summary>
public class ServiceDeploymentConfig
{
    /// <summary>Whether the service is enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>app-id for the service.</summary>
    public string? AppId { get; set; }

    /// <summary>Number of replicas.</summary>
    public int Replicas { get; set; } = 1;

    /// <summary>Service-specific settings.</summary>
    public Dictionary<string, string> Settings { get; set; } = new();
}

/// <summary>
/// Service routing information for dynamic routing.
/// Contains the app_id and host:port for routing.
/// </summary>
public class ServiceRouting
{
    /// <summary>app-id for this service.</summary>
    public required string AppId { get; set; }

    /// <summary>Container/host name where the service is running.</summary>
    public required string Host { get; set; }

    /// <summary>Port the service is listening on.</summary>
    public int Port { get; set; } = 80;

    /// <summary>Service health status (null if no status known yet).</summary>
    public ServiceHealthStatus? Status { get; set; }

    /// <summary>Last update timestamp.</summary>
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Load percentage (0-100) for load balancing.</summary>
    public int LoadPercent { get; set; }
}

/// <summary>
/// Instance health status stored in state store.
/// Represents the aggregated health state of a bannou app instance.
/// </summary>
public class InstanceHealthState
{
    /// <summary>Unique GUID identifying this bannou instance.</summary>
    public Guid InstanceId { get; set; }

    /// <summary>app-id for this instance.</summary>
    public required string AppId { get; set; }

    /// <summary>Overall instance health status.</summary>
    public required InstanceHealthStatus Status { get; set; }

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

/// <summary>
/// Index for tracking known heartbeat app IDs.
/// Stored at a special key to enable listing without KEYS/SCAN.
/// </summary>
internal class HeartbeatIndex
{
    /// <summary>Set of known app IDs with active heartbeats.</summary>
    public HashSet<string> AppIds { get; set; } = new();

    /// <summary>When the index was last updated.</summary>
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Index for tracking known service routing names.
/// Stored at a special key to enable listing without KEYS/SCAN.
/// </summary>
internal class RoutingIndex
{
    /// <summary>Set of known service names with active routings.</summary>
    public HashSet<string> ServiceNames { get; set; } = new();

    /// <summary>When the index was last updated.</summary>
    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Wrapper for storing configuration version as a class (required by IStateStore).
/// </summary>
internal class ConfigVersion
{
    /// <summary>Current version number.</summary>
    public int Version { get; set; }
}
