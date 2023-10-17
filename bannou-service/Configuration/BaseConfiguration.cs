namespace BeyondImmersion.BannouService.Configuration;

public abstract class BaseServiceConfiguration : IServiceConfiguration
{
    /// <summary>
    /// Set to override GUID for administrative service endpoints.
    /// If not set, will generate a new GUID automatically on service startup.
    /// </summary>
    public string? Force_Service_ID { get; set; } = null;
}
