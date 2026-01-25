namespace BeyondImmersion.BannouService.Configuration;

/// <summary>
/// Base configuration class for Bannou services with common configuration properties.
/// </summary>
public class BaseServiceConfiguration : IServiceConfiguration
{
    /// <summary>
    /// Set to override GUID for administrative service endpoints.
    /// If not set, will generate a new GUID automatically on service startup.
    /// </summary>
    public Guid? ForceServiceId { get; set; }

}
