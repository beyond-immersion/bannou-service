using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using System.ComponentModel.DataAnnotations;

namespace BeyondImmersion.BannouService.Mesh;

/// <summary>
/// Configuration class for Mesh service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(typeof(MeshService))]
public class MeshServiceConfiguration : IServiceConfiguration
{
    /// <inheritdoc />
    public string? Force_Service_ID { get; set; }

    /// <summary>
    /// Use local-only routing instead of Redis. All calls route to local instance. ONLY for testing/minimal infrastructure.
    /// Environment variable: MESH_USE_LOCAL_ROUTING
    /// </summary>
    public bool UseLocalRouting { get; set; } = false;

    /// <summary>
    /// Redis connection string for service registry storage
    /// Environment variable: MESH_REDIS_CONNECTION_STRING
    /// </summary>
    public string RedisConnectionString { get; set; } = "localhost:6379";

    /// <summary>
    /// Prefix for all mesh-related Redis keys
    /// Environment variable: MESH_REDIS_KEY_PREFIX
    /// </summary>
    public string RedisKeyPrefix { get; set; } = "mesh:";

    /// <summary>
    /// Total timeout in seconds for Redis connection establishment including retries
    /// Environment variable: MESH_REDIS_CONNECTION_TIMEOUT_SECONDS
    /// </summary>
    public int RedisConnectionTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Maximum number of Redis connection retry attempts
    /// Environment variable: MESH_REDIS_CONNECT_RETRY_COUNT
    /// </summary>
    public int RedisConnectRetryCount { get; set; } = 5;

    /// <summary>
    /// Timeout in milliseconds for synchronous Redis operations
    /// Environment variable: MESH_REDIS_SYNC_TIMEOUT_MS
    /// </summary>
    public int RedisSyncTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// Recommended interval between heartbeats
    /// Environment variable: MESH_HEARTBEAT_INTERVAL_SECONDS
    /// </summary>
    public int HeartbeatIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// TTL for endpoint registration (should be > 2x heartbeat interval)
    /// Environment variable: MESH_ENDPOINT_TTL_SECONDS
    /// </summary>
    public int EndpointTtlSeconds { get; set; } = 90;

    /// <summary>
    /// Time without heartbeat before marking endpoint as degraded
    /// Environment variable: MESH_DEGRADATION_THRESHOLD_SECONDS
    /// </summary>
    public int DegradationThresholdSeconds { get; set; } = 60;

    /// <summary>
    /// Default load balancing algorithm (RoundRobin, LeastConnections, Weighted, Random)
    /// Environment variable: MESH_DEFAULT_LOAD_BALANCER
    /// </summary>
    public string DefaultLoadBalancer { get; set; } = "RoundRobin";

    /// <summary>
    /// Load percentage above which an endpoint is considered high-load
    /// Environment variable: MESH_LOAD_THRESHOLD_PERCENT
    /// </summary>
    public int LoadThresholdPercent { get; set; } = 80;

    /// <summary>
    /// Default app-id when no service mapping exists (omnipotent routing)
    /// Environment variable: MESH_DEFAULT_APP_ID
    /// </summary>
    public string DefaultAppId { get; set; } = "bannou";

    /// <summary>
    /// Whether to subscribe to FullServiceMappingsEvent for routing updates
    /// Environment variable: MESH_ENABLE_SERVICE_MAPPING_SYNC
    /// </summary>
    public bool EnableServiceMappingSync { get; set; } = true;

    /// <summary>
    /// Whether to perform active health checks on endpoints
    /// Environment variable: MESH_HEALTH_CHECK_ENABLED
    /// </summary>
    public bool HealthCheckEnabled { get; set; } = false;

    /// <summary>
    /// Interval between active health checks
    /// Environment variable: MESH_HEALTH_CHECK_INTERVAL_SECONDS
    /// </summary>
    public int HealthCheckIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Timeout for health check requests
    /// Environment variable: MESH_HEALTH_CHECK_TIMEOUT_SECONDS
    /// </summary>
    public int HealthCheckTimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// Whether to enable circuit breaker for failed endpoints
    /// Environment variable: MESH_CIRCUIT_BREAKER_ENABLED
    /// </summary>
    public bool CircuitBreakerEnabled { get; set; } = true;

    /// <summary>
    /// Number of consecutive failures before opening circuit
    /// Environment variable: MESH_CIRCUIT_BREAKER_THRESHOLD
    /// </summary>
    public int CircuitBreakerThreshold { get; set; } = 5;

    /// <summary>
    /// Seconds before attempting to close circuit
    /// Environment variable: MESH_CIRCUIT_BREAKER_RESET_SECONDS
    /// </summary>
    public int CircuitBreakerResetSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum retry attempts for failed service calls
    /// Environment variable: MESH_MAX_RETRIES
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Initial delay between retries (doubles on each retry)
    /// Environment variable: MESH_RETRY_DELAY_MILLISECONDS
    /// </summary>
    public int RetryDelayMilliseconds { get; set; } = 100;

    /// <summary>
    /// Whether to log detailed routing decisions
    /// Environment variable: MESH_ENABLE_DETAILED_LOGGING
    /// </summary>
    public bool EnableDetailedLogging { get; set; } = false;

    /// <summary>
    /// Whether to collect routing metrics
    /// Environment variable: MESH_METRICS_ENABLED
    /// </summary>
    public bool MetricsEnabled { get; set; } = true;

}
