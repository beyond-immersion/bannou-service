namespace BeyondImmersion.BannouService.Configuration;

[ServiceConfiguration]
public class AppConfiguration : BaseServiceConfiguration
{
    [Flags]
    public enum LogModes
    {
        None = 0,
        File = 1 << 0,
        Console = 1 << 1,
        Cloud = 1 << 2,
        // convenience
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
    public LogModes Log_Mode { get; set; } = LogModes.Console;

    /// <summary>
    /// The minimum level of logs for the application code to write to the console.
    /// </summary>
    public LogLevel App_Logging_Level { get; set; } = LogLevel.Warning;

    /// <summary>
    /// The minimum level of logs for kestrel to write to the console.
    /// </summary>
    public LogLevel Web_Host_Logging_Level { get; set; } = LogLevel.Warning;
}
