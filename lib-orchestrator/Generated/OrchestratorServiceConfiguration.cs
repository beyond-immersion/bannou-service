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
    /// Path to TLS certificates for Docker API (production)
    /// Environment variable: DOCKERTLSCERTPATH or BANNOU_DOCKERTLSCERTPATH
    /// </summary>
    public string DockerTlsCertPath { get; set; } = string.Empty;

    /// <summary>
    /// Path to kubeconfig file for Kubernetes backend. If empty, uses in-cluster config or KUBECONFIG env var.
    /// Environment variable: KUBERNETESCONFIG or BANNOU_KUBERNETESCONFIG
    /// </summary>
    public string KubernetesConfig { get; set; } = string.Empty;

    /// <summary>
    /// Kubernetes namespace for deployments
    /// Environment variable: KUBERNETESNAMESPACE or BANNOU_KUBERNETESNAMESPACE
    /// </summary>
    public string KubernetesNamespace { get; set; } = "bannou";

    /// <summary>
    /// Portainer API URL (e.g., "https://portainer.local:9443")
    /// Environment variable: PORTAINERURL or BANNOU_PORTAINERURL
    /// </summary>
    public string PortainerUrl { get; set; } = string.Empty;

    /// <summary>
    /// Portainer API key for authentication
    /// Environment variable: PORTAINERAPIKEY or BANNOU_PORTAINERAPIKEY
    /// </summary>
    public string PortainerApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Portainer environment/endpoint ID
    /// Environment variable: PORTAINERENDPOINTID or BANNOU_PORTAINERENDPOINTID
    /// </summary>
    public int PortainerEndpointId { get; set; } = 1;

    /// <summary>
    /// Preferred backend. auto uses priority detection. If specified backend unavailable, deployment fails (no fallback).
    /// Environment variable: PREFERREDBACKEND or BANNOU_PREFERREDBACKEND
    /// </summary>
    public string PreferredBackend { get; set; } = "auto";

    /// <summary>
    /// Directory containing deployment preset YAML files
    /// Environment variable: PRESETSDIRECTORY or BANNOU_PRESETSDIRECTORY
    /// </summary>
    public string PresetsDirectory { get; set; } = "provisioning/orchestrator/presets";

    /// <summary>
    /// Directory containing reference compose files
    /// Environment variable: COMPOSEFILESDIRECTORY or BANNOU_COMPOSEFILESDIRECTORY
    /// </summary>
    public string ComposeFilesDirectory { get; set; } = "provisioning";

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

    /// <summary>
    /// Default timeout for deployments (seconds)
    /// Environment variable: DEFAULTDEPLOYMENTTIMEOUT or BANNOU_DEFAULTDEPLOYMENTTIMEOUT
    /// </summary>
    public int DefaultDeploymentTimeout { get; set; } = 300;

    /// <summary>
    /// Default graceful shutdown wait time (seconds)
    /// Environment variable: GRACEFULSHUTDOWNTIMEOUT or BANNOU_GRACEFULSHUTDOWNTIMEOUT
    /// </summary>
    public int GracefulShutdownTimeout { get; set; } = 30;

    /// <summary>
    /// Number of health check retries before marking unhealthy
    /// Environment variable: HEALTHCHECKRETRIES or BANNOU_HEALTHCHECKRETRIES
    /// </summary>
    public int HealthCheckRetries { get; set; } = 10;

    /// <summary>
    /// Interval between health check retries (seconds)
    /// Environment variable: HEALTHCHECKINTERVAL or BANNOU_HEALTHCHECKINTERVAL
    /// </summary>
    public int HealthCheckInterval { get; set; } = 5;

}
