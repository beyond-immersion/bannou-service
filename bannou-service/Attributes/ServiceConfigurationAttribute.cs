using System.ComponentModel.DataAnnotations;

namespace BeyondImmersion.BannouService.Attributes;

/// <summary>
/// Attribute for auto-loading service configuration.
/// </summary>
[AttributeUsage(validOn: AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class ServiceConfigurationAttribute : BaseServiceAttribute
{
    /// <summary>
    /// The specific service type this configuration is meant for.
    /// Can be null.
    /// </summary>
    public Type? ServiceType { get; private set; }

    /// <summary>
    /// Prefix for ENVs for this configuration.
    /// Can be null.
    /// </summary>
    public string? EnvPrefix { get; private set; }

    public ServiceConfigurationAttribute() { }

    public ServiceConfigurationAttribute(Type? serviceType = null, string? envPrefix = null)
    {
        ServiceType = serviceType;
        EnvPrefix = envPrefix;
    }
}
