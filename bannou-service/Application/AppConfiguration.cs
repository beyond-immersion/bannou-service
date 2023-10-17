using BeyondImmersion.BannouService.Configuration;

namespace BeyondImmersion.BannouService.Application;

[ServiceConfiguration]
public class AppConfiguration : BaseServiceConfiguration
{
    /// <summary>
    /// Application ID.
    /// </summary>
    public string App_ID { get; set; } = "bannou";

    /// <summary>
    /// Dapr configuration store name to use.
    /// </summary>
    public string Dapr_Configuration_Store { get; set; }

    /// <summary>
    /// Dapr secret store name to use.
    /// </summary>
    public string Dapr_Secret_Store { get; set; }

    /// <summary>
    /// Whether we're integration testing, or running normally.
    /// </summary>
    public bool Integration_Testing { get; set; } = false;

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
