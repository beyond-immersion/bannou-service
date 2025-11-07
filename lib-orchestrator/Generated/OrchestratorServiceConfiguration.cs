using System.ComponentModel.DataAnnotations;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;

namespace BeyondImmersion.BannouService.Orchestrator;

/// <summary>
/// Configuration class for Orchestrator service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(typeof(OrchestratorService), envPrefix: "BANNOU_")]
public class OrchestratorServiceConfiguration : IServiceConfiguration
{
    /// <inheritdoc />
    public string? Force_Service_ID { get; set; }

    /// <summary>
    /// Direct Redis connection (not Dapr component)
    /// Environment variable: REDISCONNECTIONSTRING or BANNOU_REDISCONNECTIONSTRING
    /// </summary>
    public string RedisConnectionString { get; set; } = "redis:6379";

    /// <summary>
    /// Direct RabbitMQ connection (not Dapr component)
    /// Environment variable: RABBITMQCONNECTIONSTRING or BANNOU_RABBITMQCONNECTIONSTRING
    /// </summary>
    public string RabbitMqConnectionString { get; set; } = "amqp://guest:guest@rabbitmq:5672";

    /// <summary>
    /// Docker API endpoint (socket for dev, TLS for prod)
    /// Environment variable: DOCKERHOST or BANNOU_DOCKERHOST
    /// </summary>
    public string DockerHost { get; set; } = "unix:///var/run/docker.sock";

    /// <summary>
    /// Interval for service health checks (seconds)
    /// Environment variable: SERVICEHEALTHCHECKINTERVAL or BANNOU_SERVICEHEALTHCHECKINTERVAL
    /// </summary>
    public int ServiceHealthCheckInterval { get; set; } = 30;

    /// <summary>
    /// Interval for infrastructure health checks (seconds)
    /// Environment variable: INFRASTRUCTUREHEALTHCHECKINTERVAL or BANNOU_INFRASTRUCTUREHEALTHCHECKINTERVAL
    /// </summary>
    public int InfrastructureHealthCheckInterval { get; set; } = 10;

    /// <summary>
    /// Minutes of degradation before restart recommended
    /// Environment variable: DEGRADATIONTHRESHOLDMINUTES or BANNOU_DEGRADATIONTHRESHOLDMINUTES
    /// </summary>
    public int DegradationThresholdMinutes { get; set; } = 5;

    /// <summary>
    /// Seconds before heartbeat considered expired
    /// Environment variable: HEARTBEATTIMEOUTSECONDS or BANNOU_HEARTBEATTIMEOUTSECONDS
    /// </summary>
    public int HeartbeatTimeoutSeconds { get; set; } = 90;

}
