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
    public Type? ServiceImplementationType { get; }

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

    public ServiceConfigurationAttribute(Type? serviceImplementation = null, string? envPrefix = null)
    {
        if (serviceImplementation != null && !typeof(IDaprService).IsAssignableFrom(serviceImplementation))
            throw new InvalidCastException($"Type provided does not implement {nameof(IDaprService)}");

        ServiceImplementationType = serviceImplementation;
        if (serviceImplementation != null)
            ServiceAttribute = serviceImplementation.GetCustomAttribute<DaprServiceAttribute>();

        EnvPrefix = envPrefix;
        if (ServiceAttribute != null && envPrefix == null)
            EnvPrefix = $"{ServiceAttribute.Name.ToUpper()}_";
    }
}
