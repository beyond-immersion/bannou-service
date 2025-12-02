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

    // TTL values
    private static readonly TimeSpan HEARTBEAT_TTL = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan ROUTING_TTL = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Creates OrchestratorRedisManager with connection string read directly from environment.
    /// This avoids DI lifetime conflicts with scoped configuration classes.
    /// </summary>
    public OrchestratorRedisManager(ILogger<OrchestratorRedisManager> logger)
    {
        _logger = logger;
        // Read connection string directly from environment to avoid DI lifetime conflicts
        _connectionString = Environment.GetEnvironmentVariable("BANNOU_RedisConnectionString")
            ?? Environment.GetEnvironmentVariable("RedisConnectionString")
            ?? "redis:6379";
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

                    var heartbeat = JsonSerializer.Deserialize<ServiceHealthStatus>(value.ToString());
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

            return JsonSerializer.Deserialize<ServiceHealthStatus>(value.ToString());
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
                Status = heartbeat.Status,
                LastSeen = DateTimeOffset.UtcNow,
                Services = heartbeat.Services?.Select(s => s.ServiceName).ToList() ?? new List<string>(),
                Issues = heartbeat.Issues,
                MaxConnections = heartbeat.Capacity?.MaxConnections ?? 0,
                CurrentConnections = heartbeat.Capacity?.CurrentConnections ?? 0,
                CpuUsage = heartbeat.Capacity?.CpuUsage ?? 0,
                MemoryUsage = heartbeat.Capacity?.MemoryUsage ?? 0
            };

            var value = JsonSerializer.Serialize(healthStatus);
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

            var value = JsonSerializer.Serialize(routing);
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

                    var routing = JsonSerializer.Deserialize<ServiceRouting>(value.ToString());
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
}
