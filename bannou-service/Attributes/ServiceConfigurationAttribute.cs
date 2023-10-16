using System.Reflection;
using System.Xml.Linq;

namespace BeyondImmersion.BannouService.Attributes;

/// <summary>
/// Attribute for easily auto-building configuration.
/// If a service target is provided, will make discoverable through service interface methods.
/// </summary>
[AttributeUsage(validOn: AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class ServiceConfigurationAttribute : BaseServiceAttribute
{
    /// <summary>
    /// The specific service type this configuration is meant for.
    /// Can be null.
    /// </summary>
    public Type? ServiceType { get; }

    /// <summary>
    /// Attribute attached to the service.
    /// </summary>
    public DaprServiceAttribute? ServiceAttribute { get; }

    /// <summary>
    /// Prefix for ENVs for this configuration.
    /// Can be null.
    /// </summary>
    public string? EnvPrefix { get; }

    public ServiceConfigurationAttribute() { }

    public ServiceConfigurationAttribute(Type? serviceType = null, string? envPrefix = null)
    {
        if (serviceType != null && !typeof(IDaprService).IsAssignableFrom(serviceType))
            throw new InvalidCastException($"Type provided does not implement {nameof(IDaprService)}");

        ServiceType = serviceType;
        if (serviceType != null)
            ServiceAttribute = serviceType.GetCustomAttribute<DaprServiceAttribute>();

        EnvPrefix = envPrefix;
        if (ServiceAttribute != null && envPrefix == null)
            EnvPrefix = $"{ServiceAttribute.Name.ToUpper()}_";
    }
}
