namespace BeyondImmersion.BannouService.Application;

[ServiceConfiguration]
public class ServiceConfiguration : IServiceConfiguration
{
    /// <summary>
    /// Set to override GUID for administrative service endpoints.
    /// If not set, will generate a new GUID automatically on service startup.
    /// </summary>
    public string? Force_Service_ID { get; set; }

    public string? Configuration_Store { get; set; }

    public bool Integration_Testing { get; set; }

    public int Web_Host_Port { get; set; } = 80;

    public LogLevel App_Logging_Level { get; set; } = LogLevel.Warning;
    public LogLevel Web_Host_Logging_Level { get; set; } = LogLevel.Warning;
}
