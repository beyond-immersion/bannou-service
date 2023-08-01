namespace BeyondImmersion.BannouService.Application;

[ServiceConfiguration]
public class ServiceConfiguration : IServiceConfiguration
{
    /// <summary>
    /// Set to override GUID for administrative service endpoints.
    /// If not set, will generate a new GUID automatically on service startup.
    /// </summary>
    public string? Force_Service_ID { get; set; } = null;

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
    /// The port the webhost is listening on (whether HTTP or HTTPS).
    /// </summary>
    public int Web_Host_Port { get; set; } = 80;

    /// <summary>
    /// The minimum level of logs for the application code to write to the console.
    /// </summary>
    public LogLevel App_Logging_Level { get; set; } = LogLevel.Warning;

    /// <summary>
    /// The minimum level of logs for kestrel to write to the console.
    /// </summary>
    public LogLevel Web_Host_Logging_Level { get; set; } = LogLevel.Warning;
}
