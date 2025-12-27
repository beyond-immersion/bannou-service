using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Mesh;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace BeyondImmersion.BannouService.Mesh.Services;

/// <summary>
/// Manages direct Redis connections for mesh service.
/// CRITICAL: Uses direct connection (NOT via mesh) to avoid circular dependencies.
/// This is the service mesh - it provides service discovery.
/// </summary>
public class MeshRedisManager : IMeshRedisManager
{
    private readonly ILogger<MeshRedisManager> _logger;
    private readonly MeshServiceConfiguration _config;
    private readonly string _connectionString;
    private ConnectionMultiplexer? _redis;
    private IDatabase? _database;

    // Redis key patterns for mesh
    private const string ENDPOINT_KEY_PREFIX = "mesh:endpoint:";
    private const string ENDPOINTS_BY_APPID_PREFIX = "mesh:appid:";
    private const string SERVICE_MAPPINGS_KEY = "mesh:service-mappings";
    private const string MAPPINGS_VERSION_KEY = "mesh:mappings-version";
    private const string ENDPOINT_INDEX_KEY = "mesh:endpoint-index";

    /// <summary>
    /// Creates MeshRedisManager with configuration injected via DI.
    /// </summary>
    /// <param name="config">Mesh service configuration.</param>
    /// <param name="logger">Logger instance.</param>
    public MeshRedisManager(MeshServiceConfiguration config, ILogger<MeshRedisManager> logger)
    {
        _logger = logger;
        _config = config;

        if (string.IsNullOrEmpty(config.RedisConnectionString))
        {
            throw new InvalidOperationException(
                "MESH_REDIS_CONNECTION_STRING is required for mesh service.");
        }

        _connectionString = config.RedisConnectionString;
    }

    /// <inheritdoc/>
    public async Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
    {
        // Use configurable timeouts from configuration
        var totalTimeoutSeconds = _config.RedisConnectionTimeoutSeconds;
        var maxRetries = _config.RedisConnectRetryCount;
        var syncTimeoutMs = _config.RedisSyncTimeoutMs;

        // Calculate delay per retry to fit within total timeout
        // Reserve time for actual connection attempts (syncTimeout per attempt)
        var totalRetryDelayMs = (totalTimeoutSeconds * 1000) - (maxRetries * syncTimeoutMs);
        var retryDelayMs = Math.Max(1000, totalRetryDelayMs / Math.Max(1, maxRetries - 1));

        _logger.LogInformation(
            "Initializing mesh Redis connection (timeout: {TotalTimeout}s, retries: {MaxRetries})",
            totalTimeoutSeconds, maxRetries);

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                _logger.LogDebug(
                    "Attempting Redis connection (attempt {Attempt}/{MaxAttempts}): {ConnectionString}",
                    attempt, maxRetries, _connectionString);

                var options = ConfigurationOptions.Parse(_connectionString);
                options.AbortOnConnectFail = false;
                options.ConnectTimeout = syncTimeoutMs;
                options.SyncTimeout = syncTimeoutMs;

                _redis = await ConnectionMultiplexer.ConnectAsync(options);
                _database = _redis.GetDatabase();

                var pingTime = await _database.PingAsync();

                _logger.LogInformation(
                    "Mesh Redis connection established (ping: {PingMs}ms)",
                    pingTime.TotalMilliseconds);

                return true;
            }
            catch (RedisConnectionException ex)
            {
                _logger.LogWarning(
                    "Redis connection failed (attempt {Attempt}/{MaxAttempts}): {Message}",
                    attempt, maxRetries, ex.Message);

                if (attempt < maxRetries)
                {
                    await Task.Delay(retryDelayMs, cancellationToken);
                }
            }
        }

        _logger.LogError(
            "Failed to establish Redis connection after {MaxAttempts} attempts within {TotalTimeout}s",
            maxRetries, totalTimeoutSeconds);

        return false;
    }

    /// <inheritdoc/>
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
            return (true, $"Mesh Redis connected (ping: {pingTime.TotalMilliseconds:F2}ms)", pingTime);
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Mesh Redis health check failed");
            return (false, $"Redis ping failed: {ex.Message}", null);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> RegisterEndpointAsync(MeshEndpoint endpoint, int ttlSeconds)
    {
        if (_database == null)
        {
            _logger.LogWarning("Redis database not initialized. Cannot register endpoint.");
            return false;
        }

        try
        {
            var instanceId = endpoint.InstanceId.ToString();
            var endpointKey = $"{ENDPOINT_KEY_PREFIX}{instanceId}";
            var appIdKey = $"{ENDPOINTS_BY_APPID_PREFIX}{endpoint.AppId}";
            var ttl = TimeSpan.FromSeconds(ttlSeconds);

            // Store the endpoint data
            var json = BannouJson.Serialize(endpoint);
            await _database.StringSetAsync(endpointKey, json, ttl);

            // Add to the app-id set for quick lookup
            await _database.SetAddAsync(appIdKey, instanceId);
            await _database.KeyExpireAsync(appIdKey, ttl);

            // Add to global index
            await _database.SetAddAsync(ENDPOINT_INDEX_KEY, instanceId);

            _logger.LogDebug(
                "Registered endpoint {InstanceId} for app {AppId} at {Host}:{Port}",
                instanceId, endpoint.AppId, endpoint.Host, endpoint.Port);

            return true;
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Failed to register endpoint {InstanceId}", endpoint.InstanceId);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> DeregisterEndpointAsync(Guid instanceId, string appId)
    {
        if (_database == null)
        {
            _logger.LogWarning("Redis database not initialized. Cannot deregister endpoint.");
            return false;
        }

        try
        {
            var instanceIdStr = instanceId.ToString();
            var endpointKey = $"{ENDPOINT_KEY_PREFIX}{instanceIdStr}";
            var appIdKey = $"{ENDPOINTS_BY_APPID_PREFIX}{appId}";

            // Remove from endpoint storage
            await _database.KeyDeleteAsync(endpointKey);

            // Remove from app-id set
            await _database.SetRemoveAsync(appIdKey, instanceIdStr);

            // Remove from global index
            await _database.SetRemoveAsync(ENDPOINT_INDEX_KEY, instanceIdStr);

            _logger.LogInformation(
                "Deregistered endpoint {InstanceId} from app {AppId}",
                instanceId, appId);

            return true;
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Failed to deregister endpoint {InstanceId}", instanceId);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> UpdateHeartbeatAsync(
        Guid instanceId,
        string appId,
        EndpointStatus status,
        float loadPercent,
        int currentConnections,
        int ttlSeconds)
    {
        if (_database == null)
        {
            _logger.LogWarning("Redis database not initialized. Cannot update heartbeat.");
            return false;
        }

        try
        {
            var endpointKey = $"{ENDPOINT_KEY_PREFIX}{instanceId}";
            var ttl = TimeSpan.FromSeconds(ttlSeconds);

            // Get existing endpoint
            var existing = await _database.StringGetAsync(endpointKey);
            if (existing.IsNullOrEmpty)
            {
                _logger.LogWarning(
                    "Heartbeat for unknown endpoint {InstanceId}. Endpoint must be registered first.",
                    instanceId);
                return false;
            }

            var endpoint = BannouJson.Deserialize<MeshEndpoint>(existing.ToString());
            if (endpoint == null)
            {
                return false;
            }

            // Update metrics
            endpoint.Status = status;
            endpoint.LoadPercent = loadPercent;
            endpoint.CurrentConnections = currentConnections;
            endpoint.LastSeen = DateTimeOffset.UtcNow;

            // Save updated endpoint
            var json = BannouJson.Serialize(endpoint);
            await _database.StringSetAsync(endpointKey, json, ttl);

            // Refresh app-id set TTL
            var appIdKey = $"{ENDPOINTS_BY_APPID_PREFIX}{appId}";
            await _database.KeyExpireAsync(appIdKey, ttl);

            _logger.LogDebug(
                "Updated heartbeat for {InstanceId}: {Status}, load {LoadPercent}%",
                instanceId, status, loadPercent);

            return true;
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Failed to update heartbeat for {InstanceId}", instanceId);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<List<MeshEndpoint>> GetEndpointsForAppIdAsync(string appId, bool includeUnhealthy = false)
    {
        var endpoints = new List<MeshEndpoint>();

        if (_database == null)
        {
            _logger.LogWarning("Redis database not initialized. Cannot get endpoints.");
            return endpoints;
        }

        try
        {
            var appIdKey = $"{ENDPOINTS_BY_APPID_PREFIX}{appId}";
            var instanceIds = await _database.SetMembersAsync(appIdKey);

            foreach (var instanceId in instanceIds)
            {
                var endpointKey = $"{ENDPOINT_KEY_PREFIX}{instanceId}";
                var value = await _database.StringGetAsync(endpointKey);

                if (value.IsNullOrEmpty)
                {
                    // Instance ID in set but endpoint expired - clean up
                    await _database.SetRemoveAsync(appIdKey, instanceId);
                    continue;
                }

                var endpoint = BannouJson.Deserialize<MeshEndpoint>(value.ToString());
                if (endpoint == null)
                {
                    continue;
                }

                // Filter by health status if requested
                if (!includeUnhealthy && endpoint.Status != EndpointStatus.Healthy)
                {
                    continue;
                }

                endpoints.Add(endpoint);
            }
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Failed to get endpoints for app {AppId}", appId);
            throw; // Don't mask Redis failures - caller needs to know if discovery failed
        }

        return endpoints;
    }

    /// <inheritdoc/>
    public async Task<List<MeshEndpoint>> GetAllEndpointsAsync(string? appIdPrefix = null)
    {
        var endpoints = new List<MeshEndpoint>();

        if (_database == null)
        {
            _logger.LogWarning("Redis database not initialized. Cannot get all endpoints.");
            return endpoints;
        }

        try
        {
            var instanceIds = await _database.SetMembersAsync(ENDPOINT_INDEX_KEY);

            foreach (var instanceId in instanceIds)
            {
                var endpointKey = $"{ENDPOINT_KEY_PREFIX}{instanceId}";
                var value = await _database.StringGetAsync(endpointKey);

                if (value.IsNullOrEmpty)
                {
                    // Clean up stale index entry
                    await _database.SetRemoveAsync(ENDPOINT_INDEX_KEY, instanceId);
                    continue;
                }

                var endpoint = BannouJson.Deserialize<MeshEndpoint>(value.ToString());
                if (endpoint == null)
                {
                    continue;
                }

                // Apply prefix filter if specified
                if (!string.IsNullOrEmpty(appIdPrefix) &&
                    !endpoint.AppId.StartsWith(appIdPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                endpoints.Add(endpoint);
            }
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Failed to get all endpoints");
            throw; // Don't mask Redis failures - caller needs to know if discovery failed
        }

        return endpoints;
    }

    /// <inheritdoc/>
    public async Task<Dictionary<string, string>> GetServiceMappingsAsync()
    {
        if (_database == null)
        {
            _logger.LogWarning("Redis database not initialized. Cannot get service mappings.");
            return new Dictionary<string, string>();
        }

        try
        {
            var value = await _database.StringGetAsync(SERVICE_MAPPINGS_KEY);
            if (value.IsNullOrEmpty)
            {
                return new Dictionary<string, string>();
            }

            return BannouJson.Deserialize<Dictionary<string, string>>(value.ToString())
                ?? new Dictionary<string, string>();
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Failed to get service mappings");
            throw; // Don't mask Redis failures - empty dict should mean "no mappings", not "error"
        }
    }

    /// <inheritdoc/>
    public async Task<bool> UpdateServiceMappingsAsync(Dictionary<string, string> mappings, long version)
    {
        if (_database == null)
        {
            _logger.LogWarning("Redis database not initialized. Cannot update service mappings.");
            return false;
        }

        try
        {
            // Get current version
            var currentVersion = await GetMappingsVersionAsync();

            // Reject stale updates
            if (version <= currentVersion)
            {
                _logger.LogDebug(
                    "Rejecting stale mappings update: version {Version} <= current {CurrentVersion}",
                    version, currentVersion);
                return false;
            }

            // Atomic update using transaction
            var transaction = _database.CreateTransaction();
            transaction.AddCondition(Condition.StringEqual(MAPPINGS_VERSION_KEY, currentVersion.ToString()));

            var mappingsJson = BannouJson.Serialize(mappings);
            _ = transaction.StringSetAsync(SERVICE_MAPPINGS_KEY, mappingsJson);
            _ = transaction.StringSetAsync(MAPPINGS_VERSION_KEY, version.ToString());

            var committed = await transaction.ExecuteAsync();

            if (committed)
            {
                _logger.LogInformation(
                    "Updated service mappings to version {Version} with {Count} mappings",
                    version, mappings.Count);
            }
            else
            {
                // Concurrent update - retry will handle
                _logger.LogDebug("Concurrent update detected, transaction not committed");
            }

            return committed;
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Failed to update service mappings");
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<long> GetMappingsVersionAsync()
    {
        if (_database == null)
        {
            return 0;
        }

        try
        {
            var value = await _database.StringGetAsync(MAPPINGS_VERSION_KEY);
            if (value.IsNullOrEmpty)
            {
                return 0;
            }

            return long.TryParse(value.ToString(), out var version) ? version : 0;
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Failed to get mappings version");
            throw; // Don't mask Redis failures - 0 should mean "no version set", not "error"
        }
    }

    /// <inheritdoc/>
    public async Task<MeshEndpoint?> GetEndpointByInstanceIdAsync(Guid instanceId)
    {
        if (_database == null)
        {
            _logger.LogWarning("Redis database not initialized. Cannot get endpoint.");
            return null;
        }

        try
        {
            var endpointKey = $"{ENDPOINT_KEY_PREFIX}{instanceId}";
            var value = await _database.StringGetAsync(endpointKey);

            if (value.IsNullOrEmpty)
            {
                return null;
            }

            return BannouJson.Deserialize<MeshEndpoint>(value.ToString());
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Failed to get endpoint {InstanceId}", instanceId);
            throw; // Don't mask Redis failures - null should mean "not found", not "error"
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_redis != null)
        {
            _redis.Close();
            _redis.Dispose();
            _logger.LogDebug("Mesh Redis connection closed synchronously");
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_redis != null)
        {
            await _redis.CloseAsync();
            _redis.Dispose();
            _logger.LogInformation("Mesh Redis connection closed");
        }
    }
}
