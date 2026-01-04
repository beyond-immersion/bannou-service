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
    /// <remarks>
    /// WARNING: For routing decisions, use <c>Program.Configuration.EffectiveAppId</c> instead!
    /// This constant is only for internal fallback logic in <c>AppConfiguration.EffectiveAppId</c>.
    /// Direct use of this constant bypasses configuration and can cause routing issues.
    /// </remarks>
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

    // ==========================================================================
    // Environment Variable Names
    // ==========================================================================

    /// <summary>
    /// Environment variable name for app ID.
    /// </summary>
    public const string ENV_BANNOU_APP_ID = "BANNOU_APP_ID";

    /// <summary>
    /// Environment variable name for HTTP endpoint.
    /// </summary>
    public const string ENV_BANNOU_HTTP_ENDPOINT = "BANNOU_HTTP_ENDPOINT";

    // ==========================================================================
    // Protocol Constants
    // ==========================================================================

    /// <summary>
    /// Special GUID for broadcast messages. When ServiceGuid equals this value,
    /// the message is broadcast to all connected peers (excluding sender).
    /// Only allowed in Relayed and Internal connection modes; External mode rejects broadcast.
    /// </summary>
    public static readonly Guid BROADCAST_GUID = new("FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF");

}
