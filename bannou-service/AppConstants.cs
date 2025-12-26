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
    /// Default pub/sub component name used by all services.
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
    /// Default HTTP port for service mesh communication.
    /// </summary>
    public const int DEFAULT_BANNOU_HTTP_PORT = 3500;

    /// <summary>
    /// Default gRPC port (legacy constant name, kept for compatibility).
    /// </summary>
    public const int DEFAULT_DAPR_GRPC_PORT = 50001;

    // ==========================================================================
    // Environment Variable Names (for documented Tenet 21 exceptions)
    // ==========================================================================

    /// <summary>
    /// Environment variable for app ID (legacy env var name). Used by PermissionRegistration and
    /// ServiceHeartbeatManager. This is a Tenet 21 exception - needed before DI is available.
    /// </summary>
    public const string ENV_BANNOU_APP_ID = "BANNOU_APP_ID";

    /// <summary>
    /// Environment variable for mesh HTTP endpoint. This is a Tenet 21 exception -
    /// needed to bootstrap mesh client before configuration system initializes.
    /// </summary>
    public const string ENV_BANNOU_HTTP_ENDPOINT = "BANNOU_HTTP_ENDPOINT";

    /// <summary>
    /// Environment variable for gRPC endpoint (legacy env var name). This is a Tenet 21 exception -
    /// needed to bootstrap infrastructure before configuration system initializes.
    /// </summary>
    public const string ENV_DAPR_GRPC_ENDPOINT = "DAPR_GRPC_ENDPOINT";
}
