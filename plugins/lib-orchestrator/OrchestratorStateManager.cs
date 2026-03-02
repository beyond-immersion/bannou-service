using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Orchestrator;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using ServiceHealthEntry = BeyondImmersion.BannouService.Orchestrator.ServiceHealthEntry;

namespace LibOrchestrator;

/// <summary>
/// Manages orchestrator state via lib-state infrastructure.
/// Uses IStateStoreFactory for Redis operations, replacing direct StackExchange.Redis dependency.
/// </summary>
public class OrchestratorStateManager : IOrchestratorStateManager
{
    private readonly ILogger<OrchestratorStateManager> _logger;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly OrchestratorServiceConfiguration _configuration;
    private readonly ITelemetryProvider _telemetryProvider;

    private const string HEARTBEATS_STORE = StateStoreDefinitions.OrchestratorHeartbeats;
    private const string ROUTINGS_STORE = StateStoreDefinitions.OrchestratorRoutings;
    private const string CONFIG_STORE = StateStoreDefinitions.OrchestratorConfig;
    private const string POOL_STORE = StateStoreDefinitions.Orchestrator;

    // Index keys for tracking known entities (avoids KEYS/SCAN)
    private const string HEARTBEAT_INDEX_KEY = "_index";
    private const string ROUTING_INDEX_KEY = "_index";

    // Configuration keys
    private const string CONFIG_VERSION_KEY = "version";
    private const string CONFIG_CURRENT_KEY = "current";
    private const string CONFIG_HISTORY_PREFIX = "history:";

    // Processing pool key patterns (internal to state manager — FOUNDATION TENETS)
    private const string POOL_INSTANCES_KEY = "processing-pool:{0}:instances";
    private const string POOL_AVAILABLE_KEY = "processing-pool:{0}:available";
    private const string POOL_LEASES_KEY = "processing-pool:{0}:leases";
    private const string POOL_METRICS_KEY = "processing-pool:{0}:metrics";
    private const string POOL_CONFIG_KEY = "processing-pool:{0}:config";
    private const string POOL_KNOWN_KEY = "processing-pool:known";

    // Cached stores (lazy initialization after InitializeAsync)
    private IStateStore<InstanceHealthState>? _heartbeatStore;
    private IStateStore<HeartbeatIndex>? _heartbeatIndexStore;
    private IStateStore<ServiceRouting>? _routingStore;
    private IStateStore<RoutingIndex>? _routingIndexStore;
    private IStateStore<DeploymentConfiguration>? _configStore;
    private IStateStore<ConfigVersion>? _versionStore;
    private IStateStore<List<ProcessorInstance>>? _poolInstanceListStore;
    private IStateStore<Dictionary<string, ProcessorLease>>? _poolLeaseStore;
    private IStateStore<PoolConfiguration>? _poolConfigStore;
    private IStateStore<PoolMetricsData>? _poolMetricsStore;
    private IStateStore<List<string>>? _poolKnownStore;

    private int _initialized; // 0=false, 1=true; Interlocked for thread safety in Singleton

    /// <summary>
    /// Creates OrchestratorStateManager with state store factory from lib-state.
    /// </summary>
    public OrchestratorStateManager(
        IStateStoreFactory stateStoreFactory,
        ILogger<OrchestratorStateManager> logger,
        OrchestratorServiceConfiguration configuration,
        ITelemetryProvider telemetryProvider)
    {
        _stateStoreFactory = stateStoreFactory;
        _logger = logger;
        _configuration = configuration;
        ArgumentNullException.ThrowIfNull(telemetryProvider, nameof(telemetryProvider));
        _telemetryProvider = telemetryProvider;
    }

    /// <summary>
    /// Initialize state stores with retry logic.
    /// </summary>
    public async Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorStateManager.InitializeAsync");
        try
        {
            // Prevent re-initialization (thread-safe for Singleton — IMPLEMENTATION TENETS)
            if (Interlocked.CompareExchange(ref _initialized, 1, 0) != 0)
            {
                _logger.LogDebug("State stores already initialized, skipping...");
                return true;
            }

            _logger.LogInformation("Initializing orchestrator state stores via lib-state...");

            // Initialize the factory (connects to Redis)
            await _stateStoreFactory.InitializeAsync(cancellationToken);

            // Get stores for each type
            _heartbeatStore = await _stateStoreFactory.GetStoreAsync<InstanceHealthState>(HEARTBEATS_STORE, cancellationToken);
            _heartbeatIndexStore = await _stateStoreFactory.GetStoreAsync<HeartbeatIndex>(HEARTBEATS_STORE, cancellationToken);
            _routingStore = await _stateStoreFactory.GetStoreAsync<ServiceRouting>(ROUTINGS_STORE, cancellationToken);
            _routingIndexStore = await _stateStoreFactory.GetStoreAsync<RoutingIndex>(ROUTINGS_STORE, cancellationToken);
            _configStore = await _stateStoreFactory.GetStoreAsync<DeploymentConfiguration>(CONFIG_STORE, cancellationToken);
            _versionStore = await _stateStoreFactory.GetStoreAsync<ConfigVersion>(CONFIG_STORE, cancellationToken);

            // Processing pool stores — using primary orchestrator state store (FOUNDATION TENETS)
            _poolInstanceListStore = await _stateStoreFactory.GetStoreAsync<List<ProcessorInstance>>(POOL_STORE, cancellationToken);
            _poolLeaseStore = await _stateStoreFactory.GetStoreAsync<Dictionary<string, ProcessorLease>>(POOL_STORE, cancellationToken);
            _poolConfigStore = await _stateStoreFactory.GetStoreAsync<PoolConfiguration>(POOL_STORE, cancellationToken);
            _poolMetricsStore = await _stateStoreFactory.GetStoreAsync<PoolMetricsData>(POOL_STORE, cancellationToken);
            _poolKnownStore = await _stateStoreFactory.GetStoreAsync<List<string>>(POOL_STORE, cancellationToken);

            // Verify connectivity with a simple operation
            var testResult = await CheckHealthAsync();

            if (testResult.IsHealthy)
            {
                _logger.LogInformation(
                    "Orchestrator state stores initialized successfully (operation time: {OperationTime}ms)",
                    testResult.OperationTime?.TotalMilliseconds ?? 0);
                return true;
            }

            _logger.LogWarning("State store health check failed: {Message}", testResult.Message);
            Interlocked.Exchange(ref _initialized, 0); // Reset so retry can succeed
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize orchestrator state stores");
            Interlocked.Exchange(ref _initialized, 0); // Reset so retry can succeed
            return false;
        }
    }

    /// <summary>
    /// Check if state stores are connected and healthy.
    /// </summary>
    public async Task<(bool IsHealthy, string? Message, TimeSpan? OperationTime)> CheckHealthAsync()
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorStateManager.CheckHealthAsync");
        if (_versionStore == null)
        {
            return (false, "State stores not initialized", null);
        }

        try
        {
            var stopwatch = Stopwatch.StartNew();

            // Simple operation to verify connectivity
            await _versionStore.ExistsAsync(CONFIG_VERSION_KEY);

            stopwatch.Stop();
            return (true, $"State stores healthy (operation: {stopwatch.ElapsedMilliseconds}ms)", stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "State store health check failed");
            return (false, $"Health check failed: {ex.Message}", null);
        }
    }

    #region Heartbeat Operations

    /// <summary>
    /// Write service heartbeat data.
    /// Uses index pattern to track known app IDs.
    /// </summary>
    public async Task WriteServiceHeartbeatAsync(ServiceHeartbeatEvent heartbeat)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorStateManager.WriteServiceHeartbeatAsync");
        if (_heartbeatStore == null || _heartbeatIndexStore == null)
        {
            _logger.LogWarning("State stores not initialized. Cannot write heartbeat.");
            return;
        }

        try
        {
            // Create health status from heartbeat event
            var healthStatus = new InstanceHealthState
            {
                InstanceId = heartbeat.ServiceId,
                AppId = heartbeat.AppId,
                Status = heartbeat.Status,
                LastSeen = DateTimeOffset.UtcNow,
                Services = heartbeat.Services?.Select(s => s.ServiceName).ToList() ?? new List<string>(),
                Issues = heartbeat.Issues?.ToList(),
                MaxConnections = heartbeat.Capacity?.MaxConnections ?? 0,
                CurrentConnections = heartbeat.Capacity?.CurrentConnections ?? 0,
                CpuUsage = heartbeat.Capacity?.CpuUsage ?? 0,
                MemoryUsage = heartbeat.Capacity?.MemoryUsage ?? 0
            };

            // Save heartbeat with TTL
            var options = new StateOptions { Ttl = _configuration.HeartbeatTtlSeconds };
            await _heartbeatStore.SaveAsync(heartbeat.AppId, healthStatus, options);

            // Update index to track this app ID
            await UpdateHeartbeatIndexAsync(heartbeat.AppId);

            _logger.LogDebug(
                "Written instance heartbeat: {AppId} - {Status} ({ServiceCount} services)",
                heartbeat.AppId, heartbeat.Status, healthStatus.Services.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write heartbeat for instance {AppId}", heartbeat.AppId);
            throw; // Don't mask state store failures - heartbeat system depends on reliable writes
        }
    }

    /// <summary>
    /// Update the heartbeat index to include this app ID.
    /// Uses retry logic to handle concurrent modifications from multiple containers.
    /// </summary>
    private async Task UpdateHeartbeatIndexAsync(string appId)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorStateManager.UpdateHeartbeatIndexAsync");
        if (_heartbeatIndexStore == null) return;

        const int maxRetries = 3;

        for (int retry = 0; retry < maxRetries; retry++)
        {
            try
            {
                var (index, etag) = await _heartbeatIndexStore.GetWithETagAsync(HEARTBEAT_INDEX_KEY);

                index ??= new HeartbeatIndex();
                index.AppIds.Add(appId);
                index.LastUpdated = DateTimeOffset.UtcNow;

                string? savedEtag;
                if (etag != null)
                {
                    savedEtag = await _heartbeatIndexStore.TrySaveAsync(HEARTBEAT_INDEX_KEY, index, etag);
                }
                else
                {
                    savedEtag = await _heartbeatIndexStore.SaveAsync(HEARTBEAT_INDEX_KEY, index);
                }

                if (savedEtag != null)
                {
                    return; // Success
                }

                // TrySaveAsync returned null (ETag mismatch due to concurrent modification) - retry
                _logger.LogDebug(
                    "Heartbeat index update for {AppId} failed due to concurrent modification, retrying ({Retry}/{MaxRetries})",
                    appId, retry + 1, maxRetries);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to update heartbeat index for {AppId} (attempt {Retry}/{MaxRetries})",
                    appId, retry + 1, maxRetries);
            }
        }

        _logger.LogWarning(
            "Failed to update heartbeat index for {AppId} after {MaxRetries} retries - orchestrator may not detect this container",
            appId, maxRetries);
    }

    /// <summary>
    /// Get all service heartbeats using index-based pattern.
    /// </summary>
    public async Task<List<ServiceHealthEntry>> GetServiceHeartbeatsAsync()
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorStateManager.GetServiceHeartbeatsAsync");
        if (_heartbeatStore == null || _heartbeatIndexStore == null)
        {
            _logger.LogWarning("State stores not initialized. Cannot retrieve heartbeats.");
            return new List<ServiceHealthEntry>();
        }

        var heartbeats = new List<ServiceHealthEntry>();

        try
        {
            // Get index of known app IDs
            var index = await _heartbeatIndexStore.GetAsync(HEARTBEAT_INDEX_KEY);
            if (index == null || index.AppIds.Count == 0)
            {
                _logger.LogDebug("No heartbeat index found or index is empty");
                return heartbeats;
            }

            // Bulk get all known heartbeats
            var results = await _heartbeatStore.GetBulkAsync(index.AppIds);

            // Track expired entries for index cleanup
            var expiredAppIds = new List<string>();

            foreach (var appId in index.AppIds)
            {
                if (results.TryGetValue(appId, out var healthStatus))
                {
                    // Convert InstanceHealthState to ServiceHealthEntry
                    heartbeats.Add(new ServiceHealthEntry
                    {
                        ServiceId = healthStatus.InstanceId.ToString(),
                        AppId = healthStatus.AppId,
                        Status = healthStatus.Status,
                        LastSeen = healthStatus.LastSeen,
                        Metadata = healthStatus.Issues?.Count > 0
                            ? new Dictionary<string, object?> { ["issues"] = healthStatus.Issues }
                            : null
                    });
                }
                else
                {
                    // Entry has expired (TTL), mark for index cleanup
                    expiredAppIds.Add(appId);
                }
            }

            // Clean up expired entries from index
            if (expiredAppIds.Count > 0)
            {
                await CleanupHeartbeatIndexAsync(expiredAppIds);
            }

            _logger.LogDebug("Retrieved {Count} service heartbeats", heartbeats.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while retrieving service heartbeats");
            throw; // Don't mask state store failures - empty list should mean "no heartbeats", not "error"
        }

        return heartbeats;
    }

    /// <summary>
    /// Remove expired entries from heartbeat index.
    /// </summary>
    private async Task CleanupHeartbeatIndexAsync(List<string> expiredAppIds)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorStateManager.CleanupHeartbeatIndexAsync");
        if (_heartbeatIndexStore == null) return;

        try
        {
            var (index, etag) = await _heartbeatIndexStore.GetWithETagAsync(HEARTBEAT_INDEX_KEY);
            if (index == null || etag == null) return;

            foreach (var appId in expiredAppIds)
            {
                index.AppIds.Remove(appId);
            }
            index.LastUpdated = DateTimeOffset.UtcNow;

            await _heartbeatIndexStore.TrySaveAsync(HEARTBEAT_INDEX_KEY, index, etag);
            _logger.LogDebug("Cleaned up {Count} expired heartbeat entries from index", expiredAppIds.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanup heartbeat index");
        }
    }

    /// <summary>
    /// Get specific service heartbeat by serviceId and appId.
    /// </summary>
    public async Task<ServiceHealthEntry?> GetServiceHeartbeatAsync(string serviceId, string appId)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorStateManager.GetServiceHeartbeatAsync");
        if (_heartbeatStore == null)
        {
            _logger.LogWarning("State stores not initialized");
            return null;
        }

        try
        {
            var healthStatus = await _heartbeatStore.GetAsync(appId);
            if (healthStatus == null)
            {
                _logger.LogDebug("No heartbeat found for {ServiceId}:{AppId}", serviceId, appId);
                return null;
            }

            return new ServiceHealthEntry
            {
                ServiceId = healthStatus.InstanceId.ToString(),
                AppId = healthStatus.AppId,
                Status = healthStatus.Status,
                LastSeen = healthStatus.LastSeen,
                Metadata = healthStatus.Issues?.Count > 0
                    ? new Dictionary<string, object?> { ["issues"] = healthStatus.Issues }
                    : null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get heartbeat for {ServiceId}:{AppId}", serviceId, appId);
            throw; // Don't mask state store failures - null should mean "not found", not "error"
        }
    }

    #endregion

    #region Routing Operations

    /// <summary>
    /// Write service routing mapping.
    /// </summary>
    public async Task WriteServiceRoutingAsync(string serviceName, ServiceRouting routing)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorStateManager.WriteServiceRoutingAsync");
        if (_routingStore == null || _routingIndexStore == null)
        {
            _logger.LogWarning("State stores not initialized. Cannot write routing.");
            return;
        }

        try
        {
            routing.LastUpdated = DateTimeOffset.UtcNow;

            var options = new StateOptions { Ttl = _configuration.RoutingTtlSeconds };
            await _routingStore.SaveAsync(serviceName, routing, options);

            // Update index
            await UpdateRoutingIndexAsync(serviceName);

            _logger.LogInformation(
                "Written routing: {ServiceName} -> {AppId} @ {Host}:{Port}",
                serviceName, routing.AppId, routing.Host, routing.Port);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write routing for {ServiceName}", serviceName);
            throw; // Don't mask state store failures - routing system depends on reliable writes
        }
    }

    /// <summary>
    /// Get a specific service routing mapping.
    /// </summary>
    public async Task<ServiceRouting?> GetServiceRoutingAsync(string serviceName)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorStateManager.GetServiceRoutingAsync");
        if (_routingStore == null)
        {
            _logger.LogWarning("State stores not initialized. Cannot get routing.");
            return null;
        }

        try
        {
            return await _routingStore.GetAsync(serviceName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get routing for {ServiceName}", serviceName);
            return null;
        }
    }

    /// <summary>
    /// Update the routing index to include this service name.
    /// </summary>
    private async Task UpdateRoutingIndexAsync(string serviceName)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorStateManager.UpdateRoutingIndexAsync");
        if (_routingIndexStore == null) return;

        const int maxRetries = 3;

        for (int retry = 0; retry < maxRetries; retry++)
        {
            try
            {
                var (index, etag) = await _routingIndexStore.GetWithETagAsync(ROUTING_INDEX_KEY);

                index ??= new RoutingIndex();
                index.ServiceNames.Add(serviceName);
                index.LastUpdated = DateTimeOffset.UtcNow;

                string? savedEtag;
                if (etag != null)
                {
                    savedEtag = await _routingIndexStore.TrySaveAsync(ROUTING_INDEX_KEY, index, etag);
                }
                else
                {
                    savedEtag = await _routingIndexStore.SaveAsync(ROUTING_INDEX_KEY, index);
                }

                if (savedEtag != null)
                {
                    return; // Success
                }

                // TrySaveAsync returned null (ETag mismatch due to concurrent modification) - retry
                _logger.LogDebug(
                    "Routing index update for {ServiceName} failed due to concurrent modification, retrying ({Retry}/{MaxRetries})",
                    serviceName, retry + 1, maxRetries);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to update routing index for {ServiceName} (attempt {Retry}/{MaxRetries})",
                    serviceName, retry + 1, maxRetries);
            }
        }

        _logger.LogWarning(
            "Failed to update routing index for {ServiceName} after {MaxRetries} retries - service may not appear in routing queries",
            serviceName, maxRetries);
    }

    /// <summary>
    /// Get all service routing mappings using index-based pattern.
    /// </summary>
    public async Task<Dictionary<string, ServiceRouting>> GetServiceRoutingsAsync()
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorStateManager.GetServiceRoutingsAsync");
        var routings = new Dictionary<string, ServiceRouting>();

        if (_routingStore == null || _routingIndexStore == null)
        {
            _logger.LogWarning("State stores not initialized. Cannot retrieve routings.");
            return routings;
        }

        try
        {
            // Get index of known service names
            var index = await _routingIndexStore.GetAsync(ROUTING_INDEX_KEY);
            if (index == null || index.ServiceNames.Count == 0)
            {
                _logger.LogDebug("No routing index found or index is empty");
                return routings;
            }

            // Bulk get all known routings
            var results = await _routingStore.GetBulkAsync(index.ServiceNames);

            // Track expired entries for index cleanup
            var expiredServiceNames = new List<string>();

            foreach (var serviceName in index.ServiceNames)
            {
                if (results.TryGetValue(serviceName, out var routing))
                {
                    routings[serviceName] = routing;
                }
                else
                {
                    // Entry has expired (TTL), mark for index cleanup
                    expiredServiceNames.Add(serviceName);
                }
            }

            // Clean up expired entries from index
            if (expiredServiceNames.Count > 0)
            {
                await CleanupRoutingIndexAsync(expiredServiceNames);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while retrieving service routings");
            throw; // Don't mask state store failures - empty dict should mean "no routings", not "error"
        }

        return routings;
    }

    /// <summary>
    /// Remove expired entries from routing index.
    /// </summary>
    private async Task CleanupRoutingIndexAsync(List<string> expiredServiceNames)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorStateManager.CleanupRoutingIndexAsync");
        if (_routingIndexStore == null) return;

        try
        {
            var (index, etag) = await _routingIndexStore.GetWithETagAsync(ROUTING_INDEX_KEY);
            if (index == null || etag == null) return;

            foreach (var serviceName in expiredServiceNames)
            {
                index.ServiceNames.Remove(serviceName);
            }
            index.LastUpdated = DateTimeOffset.UtcNow;

            await _routingIndexStore.TrySaveAsync(ROUTING_INDEX_KEY, index, etag);
            _logger.LogDebug("Cleaned up {Count} expired routing entries from index", expiredServiceNames.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanup routing index");
        }
    }

    /// <summary>
    /// Remove service routing mapping.
    /// </summary>
    public async Task RemoveServiceRoutingAsync(string serviceName)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorStateManager.RemoveServiceRoutingAsync");
        if (_routingStore == null || _routingIndexStore == null)
        {
            _logger.LogWarning("State stores not initialized. Cannot remove routing.");
            return;
        }

        try
        {
            await _routingStore.DeleteAsync(serviceName);
            await RemoveFromRoutingIndexAsync(serviceName);

            _logger.LogInformation("Removed routing: {ServiceName}", serviceName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove routing for {ServiceName}", serviceName);
            throw; // Don't mask state store failures - routing cleanup depends on reliable deletes
        }
    }

    /// <summary>
    /// Remove service name from routing index.
    /// </summary>
    private async Task RemoveFromRoutingIndexAsync(string serviceName)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorStateManager.RemoveFromRoutingIndexAsync");
        if (_routingIndexStore == null) return;

        try
        {
            var (index, etag) = await _routingIndexStore.GetWithETagAsync(ROUTING_INDEX_KEY);
            if (index == null || etag == null) return;

            index.ServiceNames.Remove(serviceName);
            index.LastUpdated = DateTimeOffset.UtcNow;

            await _routingIndexStore.TrySaveAsync(ROUTING_INDEX_KEY, index, etag);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove {ServiceName} from routing index", serviceName);
        }
    }

    /// <summary>
    /// Clear all service routing mappings.
    /// </summary>
    public async Task ClearAllServiceRoutingsAsync()
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorStateManager.ClearAllServiceRoutingsAsync");
        if (_routingStore == null || _routingIndexStore == null)
        {
            _logger.LogWarning("State stores not initialized. Cannot clear routings.");
            return;
        }

        try
        {
            // Get current index
            var index = await _routingIndexStore.GetAsync(ROUTING_INDEX_KEY);
            if (index != null)
            {
                // Delete each routing entry
                foreach (var serviceName in index.ServiceNames)
                {
                    await _routingStore.DeleteAsync(serviceName);
                }
            }

            // Clear the index
            await _routingIndexStore.DeleteAsync(ROUTING_INDEX_KEY);

            _logger.LogInformation("Cleared all service routing entries");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear service routings");
            throw; // Don't mask state store failures - routing clear depends on reliable deletes
        }
    }

    /// <summary>
    /// Set all known service routings to the default app-id.
    /// Unlike ClearAllServiceRoutingsAsync which deletes routes, this method explicitly
    /// sets each service to route to the specified default app-id.
    /// </summary>
    public async Task<List<string>> SetAllServiceRoutingsToDefaultAsync(string defaultAppId)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorStateManager.SetAllServiceRoutingsToDefaultAsync");
        var updatedServices = new List<string>();

        if (_routingStore == null || _routingIndexStore == null)
        {
            _logger.LogWarning("State stores not initialized. Cannot set default routings.");
            return updatedServices;
        }

        try
        {
            // Get current index of known services
            var index = await _routingIndexStore.GetAsync(ROUTING_INDEX_KEY);
            if (index == null || index.ServiceNames.Count == 0)
            {
                _logger.LogDebug("No routing index found or index is empty - no services to reset");
                return updatedServices;
            }

            // Set each service to route to the default app-id
            var defaultRouting = new ServiceRouting
            {
                AppId = defaultAppId,
                Host = defaultAppId,
                Port = _configuration.DefaultServicePort,
                Status = ServiceHealthStatus.Healthy,
                LastUpdated = DateTimeOffset.UtcNow
            };

            var options = new StateOptions { Ttl = _configuration.RoutingTtlSeconds };

            foreach (var serviceName in index.ServiceNames)
            {
                try
                {
                    await _routingStore.SaveAsync(serviceName, defaultRouting, options);
                    updatedServices.Add(serviceName);

                    _logger.LogDebug(
                        "Set default routing: {ServiceName} -> {AppId}",
                        serviceName, defaultAppId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to set default routing for {ServiceName}",
                        serviceName);
                }
            }

            _logger.LogInformation(
                "Set {Count} service routings to default app-id '{DefaultAppId}'",
                updatedServices.Count, defaultAppId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set service routings to default");
            throw;
        }

        return updatedServices;
    }

    #endregion

    #region Configuration Operations

    /// <summary>
    /// Get the current configuration version number.
    /// </summary>
    public async Task<int> GetConfigVersionAsync()
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorStateManager.GetConfigVersionAsync");
        if (_versionStore == null)
        {
            _logger.LogWarning("State stores not initialized. Returning version 0.");
            return 0;
        }

        try
        {
            var versionObj = await _versionStore.GetAsync(CONFIG_VERSION_KEY);
            return versionObj?.Version ?? 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get configuration version");
            throw; // Don't mask state store failures - caller needs to know
        }
    }

    /// <summary>
    /// Atomically increment and return the mappings version counter.
    /// Stored in Redis for multi-instance safety (IMPLEMENTATION TENETS).
    /// </summary>
    public async Task<int> IncrementMappingsVersionAsync()
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorStateManager.IncrementMappingsVersionAsync");
        if (_versionStore == null)
        {
            _logger.LogWarning("State stores not initialized. Returning version 0.");
            return 0;
        }

        const string mappingsVersionKey = "mappings-version";
        const int maxRetries = 3;

        for (int retry = 0; retry < maxRetries; retry++)
        {
            try
            {
                var (versionObj, etag) = await _versionStore.GetWithETagAsync(mappingsVersionKey);
                var newVersion = (versionObj?.Version ?? 0) + 1;

                if (etag == null)
                {
                    // Key doesn't exist yet — initial creation (no concurrency check needed)
                    await _versionStore.SaveAsync(mappingsVersionKey, new ConfigVersion { Version = newVersion });
                    return newVersion;
                }

                var newEtag = await _versionStore.TrySaveAsync(
                    mappingsVersionKey,
                    new ConfigVersion { Version = newVersion },
                    etag);

                if (newEtag != null)
                {
                    return newVersion;
                }

                // ETag mismatch — another instance incremented concurrently; retry
                _logger.LogDebug(
                    "Mappings version increment conflict on attempt {Attempt}, retrying",
                    retry + 1);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to increment mappings version on attempt {Attempt}", retry + 1);
                throw;
            }
        }

        // All retries exhausted — should be extremely rare
        _logger.LogWarning("Mappings version increment failed after {MaxRetries} retries due to contention", maxRetries);
        var fallback = await _versionStore.GetAsync(mappingsVersionKey);
        return fallback?.Version ?? 0;
    }

    /// <summary>
    /// Save the current configuration state as a new version.
    /// </summary>
    public async Task<int> SaveConfigurationVersionAsync(DeploymentConfiguration configuration)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorStateManager.SaveConfigurationVersionAsync");
        if (_configStore == null || _versionStore == null)
        {
            _logger.LogWarning("State stores not initialized. Cannot save configuration.");
            return 0;
        }

        try
        {
            // Get current version and increment
            var currentVersion = await GetConfigVersionAsync();
            var newVersion = currentVersion + 1;

            configuration.Version = newVersion;
            configuration.Timestamp = DateTimeOffset.UtcNow;

            // Save to history with TTL
            var historyKey = $"{CONFIG_HISTORY_PREFIX}{newVersion}";
            var historyOptions = new StateOptions { Ttl = (int)TimeSpan.FromDays(_configuration.ConfigHistoryTtlDays).TotalSeconds };
            await _configStore.SaveAsync(historyKey, configuration, historyOptions);

            // Update current configuration (no TTL - always present)
            await _configStore.SaveAsync(CONFIG_CURRENT_KEY, configuration);

            // Update version number
            await _versionStore.SaveAsync(CONFIG_VERSION_KEY, new ConfigVersion { Version = newVersion });

            _logger.LogInformation(
                "Saved configuration version {Version} with {ServiceCount} services",
                newVersion, configuration.Services.Count);

            return newVersion;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save configuration version");
            throw; // Don't mask state store failures - caller needs to know
        }
    }

    /// <summary>
    /// Get a specific configuration version from history.
    /// </summary>
    public async Task<DeploymentConfiguration?> GetConfigurationVersionAsync(int version)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorStateManager.GetConfigurationVersionAsync");
        if (_configStore == null)
        {
            _logger.LogWarning("State stores not initialized. Cannot get configuration.");
            return null;
        }

        try
        {
            var key = $"{CONFIG_HISTORY_PREFIX}{version}";
            var config = await _configStore.GetAsync(key);

            if (config == null)
            {
                _logger.LogDebug("Configuration version {Version} not found", version);
            }

            return config;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get configuration version {Version}", version);
            throw; // Don't mask state store failures - null return should mean "not found", not "error"
        }
    }

    /// <summary>
    /// Get the current active configuration.
    /// </summary>
    public async Task<DeploymentConfiguration?> GetCurrentConfigurationAsync()
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorStateManager.GetCurrentConfigurationAsync");
        if (_configStore == null)
        {
            _logger.LogWarning("State stores not initialized. Cannot get current configuration.");
            return null;
        }

        try
        {
            var config = await _configStore.GetAsync(CONFIG_CURRENT_KEY);

            if (config == null)
            {
                _logger.LogDebug("No current configuration found");
            }

            return config;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get current configuration");
            throw; // Don't mask state store failures - null return should mean "not configured", not "error"
        }
    }

    /// <summary>
    /// Restore a previous configuration version as the current configuration.
    /// </summary>
    public async Task<bool> RestoreConfigurationVersionAsync(int version)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorStateManager.RestoreConfigurationVersionAsync");
        if (_configStore == null || _versionStore == null)
        {
            _logger.LogWarning("State stores not initialized. Cannot restore configuration.");
            return false;
        }

        try
        {
            // Get the historical configuration
            var historicalConfig = await GetConfigurationVersionAsync(version);
            if (historicalConfig == null)
            {
                _logger.LogWarning("Cannot restore configuration version {Version} - not found", version);
                return false;
            }

            // Get current version for reference
            var currentVersion = await GetConfigVersionAsync();

            // Create a new version representing the rollback (don't overwrite history)
            var rollbackVersion = currentVersion + 1;
            historicalConfig.Version = rollbackVersion;
            historicalConfig.Timestamp = DateTimeOffset.UtcNow;
            historicalConfig.Description = $"Rolled back from version {currentVersion} to version {version}";

            // Save the rollback as a new version in history
            var historyKey = $"{CONFIG_HISTORY_PREFIX}{rollbackVersion}";
            var historyOptions = new StateOptions { Ttl = (int)TimeSpan.FromDays(_configuration.ConfigHistoryTtlDays).TotalSeconds };
            await _configStore.SaveAsync(historyKey, historicalConfig, historyOptions);

            // Update current configuration
            await _configStore.SaveAsync(CONFIG_CURRENT_KEY, historicalConfig);

            // Update version number
            await _versionStore.SaveAsync(CONFIG_VERSION_KEY, new ConfigVersion { Version = rollbackVersion });

            _logger.LogInformation(
                "Restored configuration from version {OldVersion} to version {NewVersion} (rollback to v{RestoredVersion})",
                currentVersion, rollbackVersion, version);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore configuration version {Version}", version);
            return false;
        }
    }

    /// <summary>
    /// Clear the current configuration, resetting to default.
    /// </summary>
    public async Task<int> ClearCurrentConfigurationAsync()
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorStateManager.ClearCurrentConfigurationAsync");
        if (_configStore == null)
        {
            _logger.LogWarning("State stores not initialized. Cannot clear configuration.");
            return 0;
        }

        try
        {
            // Create an empty "default" configuration
            var defaultConfig = new DeploymentConfiguration
            {
                PresetName = "default",
                Description = "Reset to default topology - all services route to 'bannou'",
                Services = new Dictionary<string, ServiceDeploymentConfig>(),
                EnvironmentVariables = new Dictionary<string, string>()
            };

            // Save as new version (maintains audit trail)
            var newVersion = await SaveConfigurationVersionAsync(defaultConfig);

            _logger.LogInformation(
                "Cleared configuration - saved default topology as version {Version}",
                newVersion);

            return newVersion;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear current configuration");
            throw; // Don't mask state store failures - caller needs to know
        }
    }

    #endregion

    #region Processing Pool State Management

    /// <inheritdoc />
    public async Task<List<ProcessorInstance>?> GetAvailableProcessorsAsync(string poolType)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorStateManager.GetAvailableProcessorsAsync");
        if (_poolInstanceListStore == null) throw new InvalidOperationException("State stores not initialized");
        var key = string.Format(POOL_AVAILABLE_KEY, poolType);
        return await _poolInstanceListStore.GetAsync(key);
    }

    /// <inheritdoc />
    public async Task SetAvailableProcessorsAsync(string poolType, List<ProcessorInstance> instances)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorStateManager.SetAvailableProcessorsAsync");
        if (_poolInstanceListStore == null) throw new InvalidOperationException("State stores not initialized");
        var key = string.Format(POOL_AVAILABLE_KEY, poolType);
        await _poolInstanceListStore.SaveAsync(key, instances);
    }

    /// <inheritdoc />
    public async Task<List<ProcessorInstance>?> GetPoolInstancesAsync(string poolType)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorStateManager.GetPoolInstancesAsync");
        if (_poolInstanceListStore == null) throw new InvalidOperationException("State stores not initialized");
        var key = string.Format(POOL_INSTANCES_KEY, poolType);
        return await _poolInstanceListStore.GetAsync(key);
    }

    /// <inheritdoc />
    public async Task SetPoolInstancesAsync(string poolType, List<ProcessorInstance> instances)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorStateManager.SetPoolInstancesAsync");
        if (_poolInstanceListStore == null) throw new InvalidOperationException("State stores not initialized");
        var key = string.Format(POOL_INSTANCES_KEY, poolType);
        await _poolInstanceListStore.SaveAsync(key, instances);
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, ProcessorLease>?> GetLeasesAsync(string poolType)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorStateManager.GetLeasesAsync");
        if (_poolLeaseStore == null) throw new InvalidOperationException("State stores not initialized");
        var key = string.Format(POOL_LEASES_KEY, poolType);
        return await _poolLeaseStore.GetAsync(key);
    }

    /// <inheritdoc />
    public async Task SetLeasesAsync(string poolType, Dictionary<string, ProcessorLease> leases)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorStateManager.SetLeasesAsync");
        if (_poolLeaseStore == null) throw new InvalidOperationException("State stores not initialized");
        var key = string.Format(POOL_LEASES_KEY, poolType);
        await _poolLeaseStore.SaveAsync(key, leases);
    }

    /// <inheritdoc />
    public async Task<PoolConfiguration?> GetPoolConfigurationAsync(string poolType)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorStateManager.GetPoolConfigurationAsync");
        if (_poolConfigStore == null) throw new InvalidOperationException("State stores not initialized");
        var key = string.Format(POOL_CONFIG_KEY, poolType);
        return await _poolConfigStore.GetAsync(key);
    }

    /// <inheritdoc />
    public async Task SetPoolConfigurationAsync(string poolType, PoolConfiguration config)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorStateManager.SetPoolConfigurationAsync");
        if (_poolConfigStore == null) throw new InvalidOperationException("State stores not initialized");
        var key = string.Format(POOL_CONFIG_KEY, poolType);
        await _poolConfigStore.SaveAsync(key, config);
    }

    /// <inheritdoc />
    public async Task<PoolMetricsData?> GetPoolMetricsAsync(string poolType)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorStateManager.GetPoolMetricsAsync");
        if (_poolMetricsStore == null) throw new InvalidOperationException("State stores not initialized");
        var key = string.Format(POOL_METRICS_KEY, poolType);
        return await _poolMetricsStore.GetAsync(key);
    }

    /// <inheritdoc />
    public async Task SetPoolMetricsAsync(string poolType, PoolMetricsData metrics, TimeSpan? ttl = null)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorStateManager.SetPoolMetricsAsync");
        if (_poolMetricsStore == null) throw new InvalidOperationException("State stores not initialized");
        var key = string.Format(POOL_METRICS_KEY, poolType);
        var options = ttl.HasValue ? new StateOptions { Ttl = (int)ttl.Value.TotalSeconds } : null;
        await _poolMetricsStore.SaveAsync(key, metrics, options);
    }

    /// <inheritdoc />
    public async Task<List<string>?> GetKnownPoolTypesAsync()
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorStateManager.GetKnownPoolTypesAsync");
        if (_poolKnownStore == null) throw new InvalidOperationException("State stores not initialized");
        return await _poolKnownStore.GetAsync(POOL_KNOWN_KEY);
    }

    /// <inheritdoc />
    public async Task SetKnownPoolTypesAsync(List<string> poolTypes)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorStateManager.SetKnownPoolTypesAsync");
        if (_poolKnownStore == null) throw new InvalidOperationException("State stores not initialized");
        await _poolKnownStore.SaveAsync(POOL_KNOWN_KEY, poolTypes);
    }

    #endregion

    #region Dispose

    public void Dispose()
    {
        // State store factory handles cleanup
        _logger.LogDebug("OrchestratorStateManager disposed");
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        await Task.CompletedTask;
    }

    #endregion

}
