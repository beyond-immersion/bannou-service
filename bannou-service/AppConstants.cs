namespace BeyondImmersion.BannouService;

/// <summary>
/// Application constants used throughout the Bannou service platform.
/// </summary>
public static class AppConstants
{
    /// <summary>
    /// Default application name for service routing ("bannou" - omnipotent routing).
    /// Used when no specific app-id is configured for distributed deployment.
    /// </summary>
    public const string DEFAULT_APP_NAME = "bannou";

    /// <summary>
    /// Logical service name for the orchestrator control plane.
    /// </summary>
    public const string ORCHESTRATOR_SERVICE_NAME = "orchestrator";

    // ==========================================================================
    // Infrastructure Component Names
    // ==========================================================================

    /// <summary>
    /// Default Dapr pub/sub component name used by all services.
    /// </summary>
    public const string PUBSUB_NAME = "bannou-pubsub";

    // ==========================================================================
    // Default Ports (used when not explicitly configured)
    // ==========================================================================

    /// <summary>
    /// Default Redis port.
    /// </summary>
    public const int DEFAULT_REDIS_PORT = 6379;

    /// <summary>
    /// Default RabbitMQ AMQP port.
    /// </summary>
    public const int DEFAULT_RABBITMQ_PORT = 5672;

    /// <summary>
    /// Default Dapr HTTP sidecar port.
    /// </summary>
    public const int DEFAULT_DAPR_HTTP_PORT = 3500;

    /// <summary>
    /// Default Dapr gRPC sidecar port.
    /// </summary>
    public const int DEFAULT_DAPR_GRPC_PORT = 50001;

    // ==========================================================================
    // Environment Variable Names (for documented Tenet 21 exceptions)
    // ==========================================================================

    /// <summary>
    /// Environment variable for Dapr app ID. Used by PermissionRegistration and
    /// ServiceHeartbeatManager. This is a Tenet 21 exception - needed before DI is available.
    /// </summary>
    public const string ENV_DAPR_APP_ID = "DAPR_APP_ID";

    /// <summary>
    /// Environment variable for Dapr HTTP endpoint. This is a Tenet 21 exception -
    /// needed to bootstrap DaprClient before configuration system initializes.
    /// </summary>
    public const string ENV_DAPR_HTTP_ENDPOINT = "DAPR_HTTP_ENDPOINT";

    /// <summary>
    /// Environment variable for Dapr gRPC endpoint. This is a Tenet 21 exception -
    /// needed to bootstrap DaprClient before configuration system initializes.
    /// </summary>
    public const string ENV_DAPR_GRPC_ENDPOINT = "DAPR_GRPC_ENDPOINT";
}
