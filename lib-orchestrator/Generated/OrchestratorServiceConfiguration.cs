using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Configuration;
using System.ComponentModel.DataAnnotations;

namespace BeyondImmersion.BannouService.Orchestrator;

/// <summary>
/// Configuration class for Orchestrator service.
/// Properties are automatically bound from environment variables.
/// </summary>
[ServiceConfiguration(typeof(OrchestratorService))]
public class OrchestratorServiceConfiguration : IServiceConfiguration
{
    /// <inheritdoc />
    public string? Force_Service_ID { get; set; }

    /// <summary>
    /// Cache TTL in minutes for orchestrator data
    /// Environment variable: ORCHESTRATOR_CACHE_TTL_MINUTES
    /// </summary>
    public int OrchestratorCacheTtlMinutes { get; set; } = 5;

    /// <summary>
    /// Service heartbeat timeout in seconds
    /// Environment variable: ORCHESTRATOR_HEARTBEAT_TIMEOUT_SECONDS
    /// </summary>
    public int HeartbeatTimeoutSeconds { get; set; } = 90;

    /// <summary>
    /// Time in minutes before a service is marked as degraded
    /// Environment variable: ORCHESTRATOR_DEGRADATION_THRESHOLD_MINUTES
    /// </summary>
    public int DegradationThresholdMinutes { get; set; } = 5;

    /// <summary>
    /// Portainer API URL
    /// Environment variable: ORCHESTRATOR_PORTAINER_URL
    /// </summary>
    public string PortainerUrl { get; set; } = string.Empty;

    /// <summary>
    /// Portainer API key
    /// Environment variable: ORCHESTRATOR_PORTAINER_API_KEY
    /// </summary>
    public string PortainerApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Portainer endpoint ID
    /// Environment variable: ORCHESTRATOR_PORTAINER_ENDPOINT_ID
    /// </summary>
    public int PortainerEndpointId { get; set; } = 1;

    /// <summary>
    /// Docker host for direct Docker API access
    /// Environment variable: ORCHESTRATOR_DOCKER_HOST
    /// </summary>
    public string DockerHost { get; set; } = "unix:///var/run/docker.sock";

    /// <summary>
    /// Docker network name for deployed containers
    /// Environment variable: ORCHESTRATOR_DOCKER_NETWORK
    /// </summary>
    public string DockerNetwork { get; set; } = "bannou_default";

    /// <summary>
    /// Host path for Dapr components (mounted into containers)
    /// Environment variable: ORCHESTRATOR_DAPR_COMPONENTS_HOST_PATH
    /// </summary>
    public string DaprComponentsHostPath { get; set; } = "/app/provisioning/dapr/components";

    /// <summary>
    /// Container path for mounted Dapr components
    /// Environment variable: ORCHESTRATOR_DAPR_COMPONENTS_CONTAINER_PATH
    /// </summary>
    public string DaprComponentsContainerPath { get; set; } = "/tmp/dapr-components";

    /// <summary>
    /// Docker image for Dapr sidecar containers
    /// Environment variable: ORCHESTRATOR_DAPR_IMAGE
    /// </summary>
    public string DaprImage { get; set; } = "daprio/daprd:1.16.3";

    /// <summary>
    /// Dapr placement service host:port
    /// Environment variable: ORCHESTRATOR_PLACEMENT_HOST
    /// </summary>
    public string PlacementHost { get; set; } = "placement:50006";

    /// <summary>
    /// Host path for TLS certificates
    /// Environment variable: ORCHESTRATOR_CERTIFICATES_HOST_PATH
    /// </summary>
    public string CertificatesHostPath { get; set; } = "/app/provisioning/certificates";

    /// <summary>
    /// Host path for orchestrator deployment presets
    /// Environment variable: ORCHESTRATOR_PRESETS_HOST_PATH
    /// </summary>
    public string PresetsHostPath { get; set; } = "/app/provisioning/orchestrator/presets";

    /// <summary>
    /// Docker volume name for logs
    /// Environment variable: ORCHESTRATOR_LOGS_VOLUME
    /// </summary>
    public string LogsVolumeName { get; set; } = "logs-data";

    /// <summary>
    /// Kubernetes namespace for deployments
    /// Environment variable: ORCHESTRATOR_KUBERNETES_NAMESPACE
    /// </summary>
    public string KubernetesNamespace { get; set; } = "default";

    /// <summary>
    /// Redis connection string for orchestrator state (required, no default)
    /// Environment variable: ORCHESTRATOR_REDIS_CONNECTION_STRING
    /// </summary>
    public string RedisConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// RabbitMQ connection string for orchestrator messaging (required, no default)
    /// Environment variable: ORCHESTRATOR_RABBITMQ_CONNECTION_STRING
    /// </summary>
    public string RabbitMqConnectionString { get; set; } = string.Empty;

}
