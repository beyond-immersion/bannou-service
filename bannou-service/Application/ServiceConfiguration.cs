namespace BeyondImmersion.BannouService.Application;

[ServiceConfiguration]
public class ServiceConfiguration : IServiceConfiguration
{
    /// <summary>
    /// Set to override GUID for administrative service endpoints.
    /// If not set, will generate a new GUID automatically on service startup.
    /// </summary>
    public string? ForceServiceID { get; set; }

    public string? DaprConfigurationName { get; set; }

    public LogLevel AppLoggingLevel { get; set; } = LogLevel.Warning;
}
