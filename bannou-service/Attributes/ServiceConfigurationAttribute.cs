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

    /// <summary>
    /// Initializes a new instance of the ServiceConfigurationAttribute for app-level configuration (no associated service).
    /// </summary>
    /// <param name="envPrefix">The prefix for environment variables used by this configuration.</param>
    public ServiceConfigurationAttribute(string? envPrefix = null)
    {
        ServiceImplementationType = null;
        ServiceAttribute = null;
        EnvPrefix = envPrefix;
    }

    /// <summary>
    /// Initializes a new instance of the ServiceConfigurationAttribute with the specified configuration.
    /// </summary>
    /// <param name="serviceImplementation">The service implementation type this configuration is for (required).</param>
    /// <param name="envPrefix">The prefix for environment variables used by this configuration.</param>
    public ServiceConfigurationAttribute(Type serviceImplementation, string? envPrefix = null)
    {
        if (serviceImplementation == null)
            throw new ArgumentNullException(nameof(serviceImplementation), "Service implementation type is required");

        if (!typeof(IDaprService).IsAssignableFrom(serviceImplementation))
            throw new InvalidCastException($"Service implementation type provided does not implement {nameof(IDaprService)}");

        if (serviceImplementation.IsAbstract || serviceImplementation.IsInterface)
            throw new InvalidCastException($"Service implementation type provided to config must be a concrete class.");

        ServiceImplementationType = serviceImplementation;
        ServiceAttribute = serviceImplementation.GetCustomAttribute<DaprServiceAttribute>();

        EnvPrefix = envPrefix;
        if (ServiceAttribute != null && envPrefix == null)
        {
            // Convert service name to env prefix: "game-session" -> "GAMESESSION_"
            // Hyphens are removed (not converted to underscores) to match property naming conventions
            var normalizedName = ServiceAttribute.Name.Replace("-", "").ToUpper();
            EnvPrefix = $"{normalizedName}_";
        }
    }
}
