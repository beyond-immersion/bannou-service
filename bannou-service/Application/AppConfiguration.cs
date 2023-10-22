using BeyondImmersion.BannouService.Configuration;

namespace BeyondImmersion.BannouService.Application;

[ServiceConfiguration]
public class AppConfiguration : BaseServiceConfiguration
{
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
    /// Whether we're integration testing, or running normally.
    /// </summary>
    public bool Integration_Testing { get; set; } = false;

    /// <summary>
    /// Whether services are enabled by default.
    /// </summary>
    public bool Services_Enabled { get; set; } = true;

    /// <summary>
    /// If disabled, will skip a bit of reflection that checks
    /// for the custom delineator to use for all API requests.
    /// 
    /// Configurable to measure impact.
    /// </summary>
    public bool Enable_Custom_Header_Delineation { get; set; } = false;

    /// <summary>
    /// The port the HTTP webhost is listening on.
    /// </summary>
    public int HTTP_Web_Host_Port { get; set; } = 80;

    /// <summary>
    /// The port the HTTPS webhost is listening on.
    /// </summary>
    public int HTTPS_Web_Host_Port { get; set; } = 443;

    /// <summary>
    /// The minimum level of logs for the application code to write to the console.
    /// </summary>
    public LogLevel App_Logging_Level { get; set; } = LogLevel.Warning;

    /// <summary>
    /// The minimum level of logs for kestrel to write to the console.
    /// </summary>
    public LogLevel Web_Host_Logging_Level { get; set; } = LogLevel.Warning;
}
