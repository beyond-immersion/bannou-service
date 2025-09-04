namespace BeyondImmersion.BannouService.Configuration;

public class BaseServiceConfiguration : IServiceConfiguration
{
    /// <summary>
    /// Set to override GUID for administrative service endpoints.
    /// If not set, will generate a new GUID automatically on service startup.
    /// </summary>
    public string? Force_Service_ID { get; set; }

    /// <summary>
    /// Whether the service has been disabled.
    /// Services are enabled by default, and the attempt should be made to
    /// include/exclude by assemblies instead. This means services are enabled
    /// so long as their assemblies are loaded.
    /// </summary>
    public bool? Service_Disabled { get; set; }
}
