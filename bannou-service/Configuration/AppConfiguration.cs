namespace BeyondImmersion.BannouService.Configuration;

/// <summary>
/// Main application configuration for the Bannou service platform.
/// </summary>
[ServiceConfiguration(envPrefix: "BANNOU_")]
[Obsolete]
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
    public string? Network_Mode { get; set; } = "bannou";

    /// <summary>
    /// The assemblies to load from the /libs directory.
    /// </summary>
    public string? Include_Assemblies { get; set; } = "all";

    /// <summary>
    /// Dapr configuration store name to use.
    /// </summary>
    public string? Dapr_Configuration_Store { get; set; }

    /// <summary>
    /// Dapr secret store name to use.
    /// </summary>
    public string? Dapr_Secret_Store { get; set; }

    /// <summary>
    /// Whether services are enabled by default.
    /// </summary>
    public bool Services_Enabled { get; set; } = true;
    /// <summary>
    /// Time in milliseconds for any given service startup to
    /// throw an error and start application shutdown.
    /// </summary>
    public int Service_Start_Timeout { get; set; } = (int)TimeSpan.FromMinutes(3).TotalMilliseconds;

    /// <summary>
    /// Time in milliseconds to wait for Dapr to be ready before failing startup.
    /// Set to 0 to disable Dapr readiness checks.
    /// </summary>
    public int Dapr_Readiness_Timeout { get; set; } = (int)TimeSpan.FromMinutes(2).TotalMilliseconds;

    /// <summary>
    /// The port the HTTP webhost is listening on.
    /// </summary>
    public int HTTP_Web_Host_Port { get; set; } = 80;

    /// <summary>
    /// The port the HTTPS webhost is listening on.
    /// </summary>
    public int HTTPS_Web_Host_Port { get; set; } = 443;

    /// <summary>
    /// The log destination.
    /// </summary>
    public virtual LogModes Log_Mode { get; set; } = LogModes.Console;

    /// <summary>
    /// The minimum level of logs for the application code to write to the console.
    /// </summary>
    public virtual LogLevel App_Logging_Level { get; set; } = LogLevel.Information;

    /// <summary>
    /// The minimum level of logs for kestrel to write to the console.
    /// </summary>
    public virtual LogLevel Web_Host_Logging_Level { get; set; } = LogLevel.Information;

    /// <summary>
    /// The app-id to query for initial service mappings during startup.
    /// Only set on containers deployed by orchestrator - when set, this container
    /// will query the specified app-id for service mappings before publishing heartbeats.
    /// This ensures newly deployed containers have correct routing information.
    /// </summary>
    public string? MappingSourceAppId { get; set; }

    /// <summary>
    /// Interval in seconds between service heartbeats.
    /// Environment variable: HEARTBEAT_INTERVAL_SECONDS or BANNOU_HEARTBEAT_INTERVAL_SECONDS
    /// </summary>
    public int HeartbeatIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Whether to re-register permissions on each heartbeat.
    /// Environment variable: PERMISSION_HEARTBEAT_ENABLED or BANNOU_PERMISSION_HEARTBEAT_ENABLED
    /// </summary>
    public bool PermissionHeartbeatEnabled { get; set; } = true;

    /// <summary>
    /// The Dapr App ID for this service instance.
    /// Environment variable: DAPR_APP_ID (or APP_ID for backwards compatibility)
    /// Note: DAPR_APP_ID is a bootstrap variable read during Dapr client initialization.
    /// </summary>
    public string? DaprAppId { get; set; }

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
}
