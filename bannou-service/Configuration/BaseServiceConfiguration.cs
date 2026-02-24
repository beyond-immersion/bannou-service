namespace BeyondImmersion.BannouService.Configuration;

/// <summary>
/// Base configuration class for Bannou services with common configuration properties.
/// All generated service configurations extend this class to inherit common properties.
/// </summary>
public class BaseServiceConfiguration : IServiceConfiguration
{
    /// <summary>
    /// Set to override GUID for administrative service endpoints.
    /// If not set, will generate a new GUID automatically on service startup.
    /// </summary>
    public Guid? ForceServiceId { get; set; }

    /// <summary>
    /// Nullable service enablement override. When set, takes precedence over layer-level
    /// and master kill switch controls for this specific service.
    /// Environment variable: {SERVICE}_SERVICE_ENABLED (e.g., ACCOUNT_SERVICE_ENABLED)
    /// null = defer to layer/master controls, true = force enable, false = force disable.
    /// </summary>
    public bool? ServiceEnabled { get; set; }
}
