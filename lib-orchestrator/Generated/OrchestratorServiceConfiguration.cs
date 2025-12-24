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
    /// Cache TTL in minutes for orchestrator data
    /// Environment variable: ORCHESTRATOR_CACHE_TTL_MINUTES or BANNOU_ORCHESTRATOR_CACHE_TTL_MINUTES
    /// </summary>
    public int OrchestratorCacheTtlMinutes { get; set; } = 5;

    /// <summary>
    /// Service heartbeat timeout in seconds
    /// Environment variable: ORCHESTRATOR_HEARTBEAT_TIMEOUT_SECONDS or BANNOU_ORCHESTRATOR_HEARTBEAT_TIMEOUT_SECONDS
    /// </summary>
    public int HeartbeatTimeoutSeconds { get; set; } = 90;

    /// <summary>
    /// Time in minutes before a service is marked as degraded
    /// Environment variable: ORCHESTRATOR_DEGRADATION_THRESHOLD_MINUTES or BANNOU_ORCHESTRATOR_DEGRADATION_THRESHOLD_MINUTES
    /// </summary>
    public int DegradationThresholdMinutes { get; set; } = 5;

    /// <summary>
    /// Portainer API URL
    /// Environment variable: ORCHESTRATOR_PORTAINER_URL or BANNOU_ORCHESTRATOR_PORTAINER_URL
    /// </summary>
    public string PortainerUrl { get; set; } = string.Empty;

    /// <summary>
    /// Portainer API key
    /// Environment variable: ORCHESTRATOR_PORTAINER_API_KEY or BANNOU_ORCHESTRATOR_PORTAINER_API_KEY
    /// </summary>
    public string PortainerApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Portainer endpoint ID
    /// Environment variable: ORCHESTRATOR_PORTAINER_ENDPOINT_ID or BANNOU_ORCHESTRATOR_PORTAINER_ENDPOINT_ID
    /// </summary>
    public int PortainerEndpointId { get; set; } = 1;

    /// <summary>
    /// Docker host for direct Docker API access
    /// Environment variable: ORCHESTRATOR_DOCKER_HOST or BANNOU_ORCHESTRATOR_DOCKER_HOST
    /// </summary>
    public string DockerHost { get; set; } = "unix:///var/run/docker.sock";

    /// <summary>
    /// Kubernetes namespace for deployments
    /// Environment variable: ORCHESTRATOR_KUBERNETES_NAMESPACE or BANNOU_ORCHESTRATOR_KUBERNETES_NAMESPACE
    /// </summary>
    public string KubernetesNamespace { get; set; } = "default";

    /// <summary>
    /// Redis connection string for orchestrator state
    /// Environment variable: ORCHESTRATOR_REDIS_CONNECTION_STRING or BANNOU_ORCHESTRATOR_REDIS_CONNECTION_STRING
    /// </summary>
    public string RedisConnectionString { get; set; } = "localhost:6379";

    /// <summary>
    /// RabbitMQ connection string for orchestrator messaging
    /// Environment variable: ORCHESTRATOR_RABBITMQ_CONNECTION_STRING or BANNOU_ORCHESTRATOR_RABBITMQ_CONNECTION_STRING
    /// </summary>
    public string RabbitMqConnectionString { get; set; } = "amqp://guest:guest@localhost:5672/";

}
