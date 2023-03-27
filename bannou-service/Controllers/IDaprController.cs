namespace BeyondImmersion.BannouService.Controllers;

/// <summary>
/// Interface implemented for all dapr API controllers.
/// </summary>
public interface IDaprController
{
    public string GetName()
        => GetType().GetServiceName();

    /// <summary>
    /// Returns whether the configuration indicates the service should be enabled.
    /// </summary>
    public bool IsEnabled(Type serviceType)
    {
        if (!typeof(IDaprService).IsAssignableFrom(serviceType))
            throw new InvalidCastException($"Type provided does not implement {nameof(IDaprService)}");

        return IDaprService.IsEnabled(serviceType);
    }

    /// <summary>
    /// Returns whether the configuration is provided for a service to run properly.
    /// </summary>
    public bool HasRequiredConfiguration(Type serviceType)
    {
        if (!typeof(IDaprService).IsAssignableFrom(serviceType))
            throw new InvalidCastException($"Type provided does not implement {nameof(IDaprService)}");

        return IServiceConfiguration.HasRequiredForType(serviceType);
    }

    /// <summary>
    /// Gets the full list of dapr controllers.
    /// </summary>
    public static (Type, DaprControllerAttribute)[] FindAll()
    {
        var controllerClasses = IServiceAttribute.GetClassesWithAttribute<DaprControllerAttribute>()
            .Where(t => {
                if (!typeof(IDaprController).IsAssignableFrom(t.Item1))
                    return false;

                return true;
            });

        return controllerClasses?.ToArray() ?? Array.Empty<(Type, DaprControllerAttribute)>();
    }

    /// <summary>
    /// Gets the full list of associated controllers to the given service type.
    /// </summary>
    public static (Type, DaprControllerAttribute)[] FindAll<T>()
        where T : class, IDaprService
        => FindAll(typeof(T));

    /// <summary>
    /// Gets the full list of associated controllers to a given service type.
    /// </summary>
    public static (Type, DaprControllerAttribute)[] FindAll(Type serviceType)
    {
        if (!typeof(IDaprService).IsAssignableFrom(serviceType))
            throw new InvalidCastException($"Type provided does not implement {nameof(IDaprService)}");

        var controllerClasses = FindAll()
            .Where(t => {
                if (!serviceType.IsAssignableFrom(t.Item2?.ServiceType))
                    return false;

                return true;
            });

        return controllerClasses?.ToArray() ?? Array.Empty<(Type, DaprControllerAttribute)>();
    }
}
