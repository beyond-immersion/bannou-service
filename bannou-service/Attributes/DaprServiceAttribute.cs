namespace BeyondImmersion.BannouService.Attributes;

/// <summary>
/// Attribute for auto-loading dapr services.
/// Use {Name}_SERVICE_ENABLE as ENV or switch to enable/disable.
/// For concrete classes only.
/// </summary>
[AttributeUsage(validOn: AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class DaprServiceAttribute : BaseServiceAttribute
{
    /// <summary>
    /// IDaprService handler type.
    /// Must implement/inherit IDaprService.
    /// Can be an interface, abstract or concrete class.
    /// If null, will use exact class type.
    /// 
    /// MUST match the service pulled from DI in related controllers.
    /// </summary>
    public Type? Type { get; }

    /// <summary>
    /// How long the service instance lasts.
    /// - Transient - One instance per service/reference.
    /// - Scoped - One instance per request.
    /// - Singleton - Only one instance, re-used each request.
    /// </summary>
    public ServiceLifetime Lifetime { get; }

    /// <summary>
    /// Automatic priority over any other handlers.
    /// </summary>
    public bool Priority { get; }

    /// <summary>
    /// Name of the service.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Dapr application this service falls under (by default).
    /// This can be overridden with configuration.
    /// Defaults to "bannou" for all shared services.
    /// </summary>
    public string DefaultApp { get; } = "bannou";

    private DaprServiceAttribute() { }
    public DaprServiceAttribute(string name, Type? type = null, string? defaultApp = null, bool priority = false, ServiceLifetime lifetime = ServiceLifetime.Singleton)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentNullException(nameof(name));

        if (type != null && !type.IsAssignableTo(typeof(IDaprService)))
            throw new InvalidCastException($"Dapr service type specified must implement {nameof(IDaprService)}.");

        Name = name;
        Priority = priority;
        Type = type;
        Lifetime = lifetime;

        if (!string.IsNullOrWhiteSpace(defaultApp))
            DefaultApp = defaultApp;
    }
}
