using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using System.ComponentModel.DataAnnotations;

namespace BeyondImmersion.BannouService.State;

/// <summary>
/// Configuration class for State service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(typeof(StateService))]
public class StateServiceConfiguration : IServiceConfiguration
{
    /// <inheritdoc />
    public string? Force_Service_ID { get; set; }

    /// <summary>
    /// Redis connection string (host:port format) for Redis-backed state stores
    /// Environment variable: REDIS_CONNECTION_STRING
    /// </summary>
    public string RedisConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// MySQL connection string for MySQL-backed state stores
    /// Environment variable: MYSQL_CONNECTION_STRING
    /// </summary>
    public string MySqlConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Default consistency level for state operations (strong or eventual)
    /// Environment variable: DEFAULT_CONSISTENCY
    /// </summary>
    public string DefaultConsistency { get; set; } = "strong";

    /// <summary>
    /// Enable metrics collection for state operations
    /// Environment variable: ENABLE_METRICS
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// Enable distributed tracing for state operations
    /// Environment variable: ENABLE_TRACING
    /// </summary>
    public bool EnableTracing { get; set; } = true;

}
