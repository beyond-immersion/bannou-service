namespace BeyondImmersion.BannouService.Configuration;

/// <summary>
/// Main application configuration for the Bannou service platform.
/// </summary>
[ServiceConfiguration(envPrefix: "BANNOU_")]
public class AppConfiguration : BaseServiceConfiguration
{
    /// <summary>
    /// Enumeration for log output destinations.
    /// </summary>
    [Flags]
    public enum LogModes
    {
        /// <summary>
        /// No logging output.
        /// </summary>
        None = 0,
        /// <summary>
        /// Log output to file.
        /// </summary>
        File = 1 << 0,
        /// <summary>
        /// Log output to console.
        /// </summary>
        Console = 1 << 1,
        /// <summary>
        /// Log output to cloud services.
        /// </summary>
        Cloud = 1 << 2,
        /// <summary>
        /// Log output to all destinations (convenience flag).
        /// </summary>
        All = File | Cloud | Console
    }

    /// <summary>
    /// Network mode- determines service -> app mappings.
    /// </summary>
    public string? NetworkMode { get; set; } = "bannou";

    /// <summary>
    /// The assemblies to load from the /libs directory.
    /// </summary>
    public string? IncludeAssemblies { get; set; } = "all";

    /// <summary>
    /// Mesh configuration store name to use.
    /// </summary>
    public string? MeshConfigurationStore { get; set; }

    /// <summary>
    /// Secret store name to use.
    /// </summary>
    public string? SecretStore { get; set; }

    /// <summary>
    /// Master kill switch for all services. When false, all services are disabled unless
    /// individually overridden with {SERVICE}_SERVICE_ENABLED=true.
    /// Environment variable: BANNOU_SERVICES_ENABLED
    /// </summary>
    public bool ServicesEnabled { get; set; } = true;

    /// <summary>
    /// Whether App Foundation (L1) services are enabled.
    /// Controls: account, auth, chat, connect, contract, permission, resource.
    /// Environment variable: BANNOU_ENABLE_APP_FOUNDATION
    /// </summary>
    public bool EnableAppFoundation { get; set; } = true;

    /// <summary>
    /// Whether Game Foundation (L2) services are enabled.
    /// Controls: character, realm, species, location, currency, item, inventory, etc.
    /// Environment variable: BANNOU_ENABLE_GAME_FOUNDATION
    /// </summary>
    public bool EnableGameFoundation { get; set; } = true;

    /// <summary>
    /// Whether App Features (L3) services are enabled.
    /// Controls: asset, orchestrator, documentation, website, voice, broadcast.
    /// Environment variable: BANNOU_ENABLE_APP_FEATURES
    /// </summary>
    public bool EnableAppFeatures { get; set; } = true;

    /// <summary>
    /// Whether Game Features (L4) services are enabled.
    /// Controls: behavior, matchmaking, achievement, analytics, escrow, etc.
    /// Environment variable: BANNOU_ENABLE_GAME_FEATURES
    /// </summary>
    public bool EnableGameFeatures { get; set; } = true;

    /// <summary>
    /// Whether Extensions (L5) third-party plugins are enabled.
    /// Environment variable: BANNOU_ENABLE_EXTENSIONS
    /// </summary>
    public bool EnableExtensions { get; set; } = true;

    /// <summary>
    /// Time in milliseconds for any given service startup to
    /// throw an error and start application shutdown.
    /// </summary>
    public int ServiceStartTimeout { get; set; } = (int)TimeSpan.FromMinutes(3).TotalMilliseconds;

    /// <summary>
    /// Time in milliseconds to wait for mesh connectivity before failing startup.
    /// Set to 0 to disable mesh readiness checks.
    /// </summary>
    public int MeshReadinessTimeout { get; set; } = (int)TimeSpan.FromMinutes(2).TotalMilliseconds;

    /// <summary>
    /// The port the HTTP webhost is listening on.
    /// </summary>
    public int HttpWebHostPort { get; set; } = 80;

    /// <summary>
    /// The port the HTTPS webhost is listening on.
    /// </summary>
    public int HttpsWebHostPort { get; set; } = 443;

    /// <summary>
    /// The HTTP endpoint URL for service-to-service communication.
    /// Used by ServiceClientBase and ConnectService for mesh routing.
    /// Environment variable: BANNOU_HTTPENDPOINT
    /// Default: http://localhost:{HttpWebHostPort}
    /// </summary>
    public string? HttpEndpoint { get; set; }

    /// <summary>
    /// Gets the effective HTTP endpoint URL, with fallback to localhost:{HttpWebHostPort}.
    /// Use this property instead of direct environment variable access.
    /// </summary>
    public string EffectiveHttpEndpoint => HttpEndpoint ?? $"http://localhost:{HttpWebHostPort}";

    /// <summary>
    /// The log destination.
    /// </summary>
    public virtual LogModes LogMode { get; set; } = LogModes.Console;

    /// <summary>
    /// The minimum level of logs for the application code to write to the console.
    /// </summary>
    public virtual LogLevel AppLoggingLevel { get; set; } = LogLevel.Information;

    /// <summary>
    /// The minimum level of logs for kestrel to write to the console.
    /// </summary>
    public virtual LogLevel WebHostLoggingLevel { get; set; } = LogLevel.Information;

    /// <summary>
    /// The app-id to query for initial service mappings during startup.
    /// Only set on containers deployed by orchestrator - when set, this container
    /// will query the specified app-id for service mappings before publishing heartbeats.
    /// This ensures newly deployed containers have correct routing information.
    /// </summary>
    public string? MappingSourceAppId { get; set; }

    /// <summary>
    /// Whether the heartbeat system is enabled.
    /// Set to false only for minimal infrastructure testing where pub/sub is not configured.
    /// Environment variable: BANNOU_HEARTBEAT_ENABLED
    /// </summary>
    public bool HeartbeatEnabled { get; set; } = true;

    /// <summary>
    /// Interval in seconds between service heartbeats.
    /// Environment variable: BANNOU_HEARTBEAT_INTERVAL_SECONDS
    /// </summary>
    public int HeartbeatIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// The App ID for this service instance used for mesh routing.
    /// Environment variable: BANNOU_APP_ID
    /// </summary>
    public string? AppId { get; set; }

    /// <summary>
    /// Gets the effective App ID, with fallback to the default "bannou" if not configured.
    /// Use this property for routing decisions to ensure consistent fallback behavior.
    /// </summary>
    public string EffectiveAppId => AppId ?? AppConstants.DEFAULT_APP_NAME;

    /// <summary>
    /// JWT secret key for token signing and validation.
    /// Environment variable: BANNOU_JWTSECRET
    /// REQUIRED - application will fail to start if not configured.
    /// </summary>
    public string? JwtSecret { get; set; }

    /// <summary>
    /// JWT issuer for token validation.
    /// Environment variable: BANNOU_JWTISSUER
    /// </summary>
    public string JwtIssuer { get; set; } = "bannou-auth";

    /// <summary>
    /// JWT audience for token validation.
    /// Environment variable: BANNOU_JWTAUDIENCE
    /// </summary>
    public string JwtAudience { get; set; } = "bannou-api";

    /// <summary>
    /// Service domain for URL generation.
    /// When set, OAuth redirect URIs and WebSocket URLs default to this domain.
    /// Environment variable: BANNOU_SERVICE_DOMAIN
    /// </summary>
    public string? ServiceDomain { get; set; }
}
