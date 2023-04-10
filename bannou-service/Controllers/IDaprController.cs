namespace BeyondImmersion.BannouService.Controllers;

/// <summary>
/// Interface implemented for all dapr API controllers.
/// </summary>
public interface IDaprController
{
    /// <summary>
    /// Gets the full list of dapr controllers.
    /// </summary>
    public static (Type, DaprControllerAttribute)[] FindAll(bool enabledOnly = false)
    {
        var controllerClasses = IServiceAttribute.GetClassesWithAttribute<DaprControllerAttribute>()
            .Where(t => {
                if (!typeof(IDaprController).IsAssignableFrom(t.Item1))
                    return false;

                if (!enabledOnly)
                    return true;

                if (t.Item2?.ServiceType == null)
                    return true;

                var handlerType = IDaprService.FindHandler(t.Item2.ServiceType);
                if (handlerType == null)
                    return false;

                return IDaprService.IsEnabled(handlerType.Value.Item2);
            });

        return controllerClasses?.ToArray() ?? Array.Empty<(Type, DaprControllerAttribute)>();
    }

    /// <summary>
    /// Gets the full list of associated controllers to the given service type.
    /// </summary>
    public static (Type, DaprControllerAttribute)[] FindForHandler<T>()
        where T : class, IDaprService
        => FindForHandler(typeof(T));

    /// <summary>
    /// Gets the full list of associated controllers to a given service type.
    /// </summary>
    public static (Type, DaprControllerAttribute)[] FindForHandler(Type handlerType)
    {
        if (!typeof(IDaprService).IsAssignableFrom(handlerType))
            throw new InvalidCastException($"Type provided does not implement {nameof(IDaprService)}");

        var controllerClasses = FindAll()
            .Where(t => {
                if (handlerType != t.Item2?.ServiceType)
                    return false;

                return true;
            });

        return controllerClasses?.ToArray() ?? Array.Empty<(Type, DaprControllerAttribute)>();
    }
}
