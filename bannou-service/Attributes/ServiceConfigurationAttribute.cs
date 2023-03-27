namespace BeyondImmersion.BannouService.Attributes;

/// <summary>
/// Attribute for easily auto-building configuration.
/// If a service target is provided, will make discoverable through service interface methods.
/// </summary>
[AttributeUsage(validOn: AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class ServiceConfigurationAttribute : BaseServiceAttribute
{
    /// <summary>
    /// Is this the 'primary' configuration for this service type?
    /// </summary>
    public bool Primary { get; private set; } = true;

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

    public ServiceConfigurationAttribute(Type? serviceType = null, string? envPrefix = null, bool primary = true)
    {
        if (serviceType != null && !typeof(IDaprService).IsAssignableFrom(serviceType))
            throw new InvalidCastException($"Type provided does not implement {nameof(IDaprService)}");

        ServiceType = serviceType;
        EnvPrefix = envPrefix;
        Primary = primary;
    }
}
