using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Orchestrator;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;
using ServiceHealthStatus = BeyondImmersion.BannouService.Orchestrator.ServiceHealthStatus;

namespace LibOrchestrator;

/// <summary>
/// Manages direct Redis connections for orchestrator service.
/// CRITICAL: Uses direct connection (NOT Dapr) to avoid chicken-and-egg dependency.
/// Writes service heartbeats and routing information for NGINX to read.
/// </summary>
public class OrchestratorRedisManager : IOrchestratorRedisManager
{
    private readonly ILogger<OrchestratorRedisManager> _logger;
    private readonly string _connectionString;
    private ConnectionMultiplexer? _redis;
    private IDatabase? _database;

    private const int MAX_RETRY_ATTEMPTS = 10;
    private const int INITIAL_RETRY_DELAY_MS = 1000;
    private const int MAX_RETRY_DELAY_MS = 60000;

    // Redis key patterns - must match what NGINX Lua reads
    private const string HEARTBEAT_KEY_PREFIX = "service:heartbeat:";
    private const string ROUTING_KEY_PREFIX = "service:routing:";

    // Configuration versioning key patterns
    private const string CONFIG_VERSION_KEY = "config:version";
    private const string CONFIG_CURRENT_KEY = "config:current";
    private const string CONFIG_HISTORY_PREFIX = "config:history:";

    // TTL values
    private static readonly TimeSpan HEARTBEAT_TTL = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan ROUTING_TTL = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CONFIG_HISTORY_TTL = TimeSpan.FromDays(30); // Keep history for 30 days

    // Use centralized BannouJson for consistent serialization/deserialization

    /// <summary>
    /// Creates OrchestratorRedisManager with configuration injected via DI.
    /// </summary>
    /// <param name="config">Orchestrator service configuration.</param>
    /// <param name="logger">Logger instance.</param>
    public OrchestratorRedisManager(OrchestratorServiceConfiguration config, ILogger<OrchestratorRedisManager> logger)
    {
        _logger = logger;
        _connectionString = config.RedisConnectionString
            ?? throw new InvalidOperationException(
                "ORCHESTRATOR_REDIS_CONNECTION_STRING is required for orchestrator service.");
    }

    /// <summary>
    /// Initialize Redis connection with wait-on-startup retry logic.
    /// Uses exponential backoff to handle infrastructure startup delays.
    /// </summary>
    public async Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
    {
        var retryDelay = INITIAL_RETRY_DELAY_MS;

        for (int attempt = 1; attempt <= MAX_RETRY_ATTEMPTS; attempt++)
        {
            try
            {
                _logger.LogInformation(
                    "Attempting Redis connection (attempt {Attempt}/{MaxAttempts}): {ConnectionString}",
                    attempt, MAX_RETRY_ATTEMPTS, _connectionString);

                var options = ConfigurationOptions.Parse(_connectionString);
                options.AbortOnConnectFail = false;  // ✅ Automatic reconnection
                options.ConnectTimeout = 5000;
                options.SyncTimeout = 5000;

                _redis = await ConnectionMultiplexer.ConnectAsync(options);
                _database = _redis.GetDatabase();

                // Verify connection with ping
                var pingTime = await _database.PingAsync();

                _logger.LogInformation(
                    "Redis connection established successfully (ping: {PingMs}ms)",
                    pingTime.TotalMilliseconds);

                return true;
            }
            catch (RedisConnectionException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Redis connection failed (attempt {Attempt}/{MaxAttempts}). Retrying in {DelayMs}ms...",
                    attempt, MAX_RETRY_ATTEMPTS, retryDelay);

                if (attempt < MAX_RETRY_ATTEMPTS)
                {
                    await Task.Delay(retryDelay, cancellationToken);
                    retryDelay = Math.Min(retryDelay * 2, MAX_RETRY_DELAY_MS);  // Exponential backoff
                }
            }
        }

        _logger.LogError(
            "Failed to establish Redis connection after {MaxAttempts} attempts",
            MAX_RETRY_ATTEMPTS);

        return false;
    }

    /// <summary>
    /// Get all service heartbeat data from Redis.
    /// Pattern: service:heartbeat:{serviceId}:{appId}
    /// TTL: 90 seconds (from ServiceHeartbeatEvent schema)
    /// </summary>
    public async Task<List<ServiceHealthStatus>> GetServiceHeartbeatsAsync()
    {
        if (_database == null)
        {
            _logger.LogWarning("Redis database not initialized. Cannot retrieve heartbeats.");
            return new List<ServiceHealthStatus>();
        }

        var heartbeats = new List<ServiceHealthStatus>();

        try
        {
            // Get all keys matching heartbeat pattern
            var server = _redis?.GetServer(_redis.GetEndPoints().First());
            if (server == null)
            {
                _logger.LogWarning("Cannot get Redis server endpoint");
                return heartbeats;
            }

            var keys = server.Keys(pattern: "service:heartbeat:*").ToArray();

            _logger.LogDebug("Found {Count} service heartbeat keys in Redis", keys.Length);

            foreach (var key in keys)
            {
                try
                {
                    var value = await _database.StringGetAsync(key);
                    if (value.IsNullOrEmpty)
                    {
                        continue;
                    }

                    var heartbeat = BannouJson.Deserialize<ServiceHealthStatus>(value.ToString());
                    if (heartbeat != null)
                    {
                        heartbeats.Add(heartbeat);
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize heartbeat from key: {Key}", key);
                }
            }
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Redis error while retrieving service heartbeats");
        }

        return heartbeats;
    }

    /// <summary>
    /// Check if Redis is currently connected and healthy.
    /// </summary>
    public async Task<(bool IsHealthy, string? Message, TimeSpan? PingTime)> CheckHealthAsync()
    {
        if (_redis == null || !_redis.IsConnected)
        {
            return (false, "Redis connection not established", null);
        }

        if (_database == null)
        {
            return (false, "Redis database not initialized", null);
        }

        try
        {
            var pingTime = await _database.PingAsync();
            return (true, $"Redis connected (ping: {pingTime.TotalMilliseconds:F2}ms)", pingTime);
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Redis health check failed");
            return (false, $"Redis ping failed: {ex.Message}", null);
        }
    }

    /// <summary>
    /// Get specific service heartbeat by serviceId and appId.
    /// </summary>
    public async Task<ServiceHealthStatus?> GetServiceHeartbeatAsync(string serviceId, string appId)
    {
        if (_database == null)
        {
            _logger.LogWarning("Redis database not initialized");
            return null;
        }

        try
        {
            var key = $"service:heartbeat:{serviceId}:{appId}";
            var value = await _database.StringGetAsync(key);

            if (value.IsNullOrEmpty)
            {
                _logger.LogDebug("No heartbeat found for {ServiceId}:{AppId}", serviceId, appId);
                return null;
            }

            return BannouJson.Deserialize<ServiceHealthStatus>(value.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get heartbeat for {ServiceId}:{AppId}", serviceId, appId);
            return null;
        }
    }

    /// <summary>
    /// Synchronous dispose for DI container compatibility.
    /// </summary>
    public void Dispose()
    {
        if (_redis != null)
        {
            _redis.Close();
            _redis.Dispose();
            _logger.LogDebug("Redis connection closed synchronously");
        }
    }

    /// <summary>
    /// Async dispose for async-aware disposal contexts.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_redis != null)
        {
            await _redis.CloseAsync();
            _redis.Dispose();
            _logger.LogInformation("Redis connection closed");
        }
    }

    /// <summary>
    /// Write aggregated instance heartbeat data to Redis.
    /// Called when heartbeat events are received from RabbitMQ.
    /// Pattern: service:heartbeat:{appId} (keyed by app-id, not service name)
    /// TTL: 90 seconds
    /// </summary>
    public async Task WriteServiceHeartbeatAsync(ServiceHeartbeatEvent heartbeat)
    {
        if (_database == null)
        {
            _logger.LogWarning("Redis database not initialized. Cannot write heartbeat.");
            return;
        }

        try
        {
            // Key by AppId since heartbeat is now aggregated per-instance
            var key = $"{HEARTBEAT_KEY_PREFIX}{heartbeat.AppId}";

            // Store the full aggregated heartbeat
            var healthStatus = new InstanceHealthStatus
            {
                InstanceId = heartbeat.ServiceId,
                AppId = heartbeat.AppId,
                Status = heartbeat.Status.ToString().ToLowerInvariant(),
                LastSeen = DateTimeOffset.UtcNow,
                Services = heartbeat.Services?.Select(s => s.ServiceName).ToList() ?? new List<string>(),
                Issues = heartbeat.Issues?.ToList(),
                MaxConnections = heartbeat.Capacity?.MaxConnections ?? 0,
                CurrentConnections = heartbeat.Capacity?.CurrentConnections ?? 0,
                CpuUsage = heartbeat.Capacity?.CpuUsage ?? 0,
                MemoryUsage = heartbeat.Capacity?.MemoryUsage ?? 0
            };

            var value = BannouJson.Serialize(healthStatus);
            await _database.StringSetAsync(key, value, HEARTBEAT_TTL);

            _logger.LogDebug(
                "Written instance heartbeat to Redis: {Key} - {Status} ({ServiceCount} services)",
                key, heartbeat.Status, healthStatus.Services.Count);
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Failed to write heartbeat for instance {AppId}",
                heartbeat.AppId);
        }
    }

    /// <summary>
    /// Write service routing mapping to Redis for NGINX to read.
    /// Pattern: service:routing:{serviceName}
    /// Contains: app_id, host, port for NGINX to route requests.
    /// </summary>
    public async Task WriteServiceRoutingAsync(string serviceName, ServiceRouting routing)
    {
        if (_database == null)
        {
            _logger.LogWarning("Redis database not initialized. Cannot write routing.");
            return;
        }

        try
        {
            var key = $"{ROUTING_KEY_PREFIX}{serviceName}";
            routing.LastUpdated = DateTimeOffset.UtcNow;

            var value = BannouJson.Serialize(routing);
            await _database.StringSetAsync(key, value, ROUTING_TTL);

            _logger.LogInformation(
                "Written routing to Redis: {ServiceName} -> {AppId} @ {Host}:{Port}",
                serviceName, routing.AppId, routing.Host, routing.Port);
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Failed to write routing for {ServiceName}", serviceName);
        }
    }

    /// <summary>
    /// Get all service routing mappings from Redis.
    /// Used by NGINX Lua script for dynamic routing.
    /// </summary>
    public async Task<Dictionary<string, ServiceRouting>> GetServiceRoutingsAsync()
    {
        var routings = new Dictionary<string, ServiceRouting>();

        if (_database == null)
        {
            _logger.LogWarning("Redis database not initialized. Cannot retrieve routings.");
            return routings;
        }

        try
        {
            var server = _redis?.GetServer(_redis.GetEndPoints().First());
            if (server == null)
            {
                _logger.LogWarning("Cannot get Redis server endpoint");
                return routings;
            }

            var keys = server.Keys(pattern: $"{ROUTING_KEY_PREFIX}*").ToArray();

            foreach (var key in keys)
            {
                try
                {
                    var value = await _database.StringGetAsync(key);
                    if (value.IsNullOrEmpty)
                    {
                        continue;
                    }

                    var routing = BannouJson.Deserialize<ServiceRouting>(value.ToString());
                    if (routing != null)
                    {
                        // Extract service name from key
                        var serviceName = key.ToString().Replace(ROUTING_KEY_PREFIX, "");
                        routings[serviceName] = routing;
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize routing from key: {Key}", key);
                }
            }
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Redis error while retrieving service routings");
        }

        return routings;
    }

    /// <summary>
    /// Remove service routing mapping from Redis.
    /// Called when a service is unregistered.
    /// </summary>
    public async Task RemoveServiceRoutingAsync(string serviceName)
    {
        if (_database == null)
        {
            _logger.LogWarning("Redis database not initialized. Cannot remove routing.");
            return;
        }

        try
        {
            var key = $"{ROUTING_KEY_PREFIX}{serviceName}";
            await _database.KeyDeleteAsync(key);

            _logger.LogInformation("Removed routing from Redis: {ServiceName}", serviceName);
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Failed to remove routing for {ServiceName}", serviceName);
        }
    }

    /// <summary>
    /// Clear all service routing mappings from Redis.
    /// Called when resetting to default topology.
    /// </summary>
    public async Task ClearAllServiceRoutingsAsync()
    {
        if (_database == null)
        {
            _logger.LogWarning("Redis database not initialized. Cannot clear routings.");
            return;
        }

        try
        {
            var server = _redis?.GetServer(_redis.GetEndPoints().First());
            if (server == null)
            {
                _logger.LogWarning("Cannot get Redis server endpoint");
                return;
            }

            var keys = server.Keys(pattern: $"{ROUTING_KEY_PREFIX}*").ToArray();
            var deletedCount = 0;

            foreach (var key in keys)
            {
                await _database.KeyDeleteAsync(key);
                deletedCount++;
            }

            _logger.LogInformation("Cleared {Count} service routing entries from Redis", deletedCount);
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Failed to clear service routings");
        }
    }

    /// <summary>
    /// Get the current configuration version number.
    /// </summary>
    public async Task<int> GetConfigVersionAsync()
    {
        if (_database == null)
        {
            _logger.LogWarning("Redis database not initialized. Returning version 0.");
            return 0;
        }

        try
        {
            var value = await _database.StringGetAsync(CONFIG_VERSION_KEY);
            if (value.IsNullOrEmpty)
            {
                return 0;
            }

            return int.TryParse(value.ToString(), out var version) ? version : 0;
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Failed to get configuration version");
            return 0;
        }
    }

    /// <summary>
    /// Save the current configuration state as a new version.
    /// Stores deployment topology for potential rollback.
    /// </summary>
    public async Task<int> SaveConfigurationVersionAsync(DeploymentConfiguration configuration)
    {
        if (_database == null)
        {
            _logger.LogWarning("Redis database not initialized. Cannot save configuration.");
            return 0;
        }

        try
        {
            // Get current version and increment
            var currentVersion = await GetConfigVersionAsync();
            var newVersion = currentVersion + 1;

            configuration.Version = newVersion;
            configuration.Timestamp = DateTimeOffset.UtcNow;

            var json = BannouJson.Serialize(configuration);

            // Save to history with TTL
            var historyKey = $"{CONFIG_HISTORY_PREFIX}{newVersion}";
            await _database.StringSetAsync(historyKey, json, CONFIG_HISTORY_TTL);

            // Update current configuration (no TTL - always present)
            await _database.StringSetAsync(CONFIG_CURRENT_KEY, json);

            // Update version number (no TTL)
            await _database.StringSetAsync(CONFIG_VERSION_KEY, newVersion.ToString());

            _logger.LogInformation(
                "Saved configuration version {Version} with {ServiceCount} services",
                newVersion, configuration.Services.Count);

            return newVersion;
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Failed to save configuration version");
            return 0;
        }
    }

    /// <summary>
    /// Get a specific configuration version from history.
    /// </summary>
    public async Task<DeploymentConfiguration?> GetConfigurationVersionAsync(int version)
    {
        if (_database == null)
        {
            _logger.LogWarning("Redis database not initialized. Cannot get configuration.");
            return null;
        }

        try
        {
            var key = $"{CONFIG_HISTORY_PREFIX}{version}";
            var value = await _database.StringGetAsync(key);

            if (value.IsNullOrEmpty)
            {
                _logger.LogDebug("Configuration version {Version} not found", version);
                return null;
            }

            return BannouJson.Deserialize<DeploymentConfiguration>(value.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get configuration version {Version}", version);
            return null;
        }
    }

    /// <summary>
    /// Get the current active configuration.
    /// </summary>
    public async Task<DeploymentConfiguration?> GetCurrentConfigurationAsync()
    {
        if (_database == null)
        {
            _logger.LogWarning("Redis database not initialized. Cannot get current configuration.");
            return null;
        }

        try
        {
            var value = await _database.StringGetAsync(CONFIG_CURRENT_KEY);

            if (value.IsNullOrEmpty)
            {
                _logger.LogDebug("No current configuration found");
                return null;
            }

            return BannouJson.Deserialize<DeploymentConfiguration>(value.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get current configuration");
            return null;
        }
    }

    /// <summary>
    /// Restore a previous configuration version as the current configuration.
    /// </summary>
    public async Task<bool> RestoreConfigurationVersionAsync(int version)
    {
        if (_database == null)
        {
            _logger.LogWarning("Redis database not initialized. Cannot restore configuration.");
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

            var json = BannouJson.Serialize(historicalConfig);

            // Save the rollback as a new version in history
            var historyKey = $"{CONFIG_HISTORY_PREFIX}{rollbackVersion}";
            await _database.StringSetAsync(historyKey, json, CONFIG_HISTORY_TTL);

            // Update current configuration
            await _database.StringSetAsync(CONFIG_CURRENT_KEY, json);

            // Update version number
            await _database.StringSetAsync(CONFIG_VERSION_KEY, rollbackVersion.ToString());

            _logger.LogInformation(
                "Restored configuration from version {OldVersion} to version {NewVersion} (rollback to v{RestoredVersion})",
                currentVersion, rollbackVersion, version);

            return true;
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Failed to restore configuration version {Version}", version);
            return false;
        }
    }

    /// <summary>
    /// Clear the current configuration, resetting to default (no custom deployments).
    /// Saves an empty configuration as a new version for audit trail.
    /// </summary>
    public async Task<int> ClearCurrentConfigurationAsync()
    {
        if (_database == null)
        {
            _logger.LogWarning("Redis database not initialized. Cannot clear configuration.");
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
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Failed to clear current configuration");
            return 0;
        }
    }

    #region Generic Storage Methods for Processing Pools

    /// <summary>
    /// Get a list of items from Redis.
    /// </summary>
    public async Task<List<T>?> GetListAsync<T>(string key) where T : class
    {
        if (_database == null)
        {
            _logger.LogWarning("Redis database not initialized. Cannot get list.");
            return null;
        }

        try
        {
            var value = await _database.StringGetAsync(key);
            if (value.IsNullOrEmpty)
            {
                return null;
            }

            return BannouJson.Deserialize<List<T>>(value.ToString());
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Failed to get list from key {Key}", key);
            return null;
        }
    }

    /// <summary>
    /// Set a list of items in Redis.
    /// </summary>
    public async Task SetListAsync<T>(string key, List<T> items) where T : class
    {
        if (_database == null)
        {
            _logger.LogWarning("Redis database not initialized. Cannot set list.");
            return;
        }

        try
        {
            var json = BannouJson.Serialize(items);
            await _database.StringSetAsync(key, json);
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Failed to set list at key {Key}", key);
        }
    }

    /// <summary>
    /// Get a hash (dictionary) from Redis.
    /// </summary>
    public async Task<Dictionary<string, T>?> GetHashAsync<T>(string key) where T : class
    {
        if (_database == null)
        {
            _logger.LogWarning("Redis database not initialized. Cannot get hash.");
            return null;
        }

        try
        {
            var value = await _database.StringGetAsync(key);
            if (value.IsNullOrEmpty)
            {
                return null;
            }

            return BannouJson.Deserialize<Dictionary<string, T>>(value.ToString());
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Failed to get hash from key {Key}", key);
            return null;
        }
    }

    /// <summary>
    /// Set a hash (dictionary) in Redis.
    /// </summary>
    public async Task SetHashAsync<T>(string key, Dictionary<string, T> hash) where T : class
    {
        if (_database == null)
        {
            _logger.LogWarning("Redis database not initialized. Cannot set hash.");
            return;
        }

        try
        {
            var json = BannouJson.Serialize(hash);
            await _database.StringSetAsync(key, json);
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Failed to set hash at key {Key}", key);
        }
    }

    /// <summary>
    /// Get a single value from Redis.
    /// </summary>
    public async Task<T?> GetValueAsync<T>(string key) where T : class
    {
        if (_database == null)
        {
            _logger.LogWarning("Redis database not initialized. Cannot get value.");
            return null;
        }

        try
        {
            var value = await _database.StringGetAsync(key);
            if (value.IsNullOrEmpty)
            {
                return null;
            }

            return BannouJson.Deserialize<T>(value.ToString());
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Failed to get value from key {Key}", key);
            return null;
        }
    }

    /// <summary>
    /// Set a single value in Redis.
    /// </summary>
    public async Task SetValueAsync<T>(string key, T value, TimeSpan? ttl = null) where T : class
    {
        if (_database == null)
        {
            _logger.LogWarning("Redis database not initialized. Cannot set value.");
            return;
        }

        try
        {
            var json = BannouJson.Serialize(value);
            if (ttl.HasValue)
            {
                await _database.StringSetAsync(key, json, ttl.Value);
            }
            else
            {
                await _database.StringSetAsync(key, json);
            }
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Failed to set value at key {Key}", key);
        }
    }

    #endregion
}
