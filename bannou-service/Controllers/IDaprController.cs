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
        IEnumerable<(Type, DaprControllerAttribute)> controllerClasses = IServiceAttribute.GetClassesWithAttribute<DaprControllerAttribute>()
            .Where(t =>
            {
                if (!typeof(IDaprController).IsAssignableFrom(t.Item1))
                    return false;

                if (!enabledOnly)
                    return true;

                if (t.Item2?.ServiceType == null)
                    return true;

                (Type, Type, DaprServiceAttribute)? handlerType = IDaprService.GetServiceInfo(t.Item2.ServiceType);
                return handlerType != null && IDaprService.IsDisabled(handlerType.Value.Item2);
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
    /// Gets the full list of associated controllers to a given handler type.
    /// </summary>
    public static (Type, DaprControllerAttribute)[] FindForHandler(Type handlerType)
    {
        if (!typeof(IDaprService).IsAssignableFrom(handlerType))
            throw new InvalidCastException($"Type provided does not implement {nameof(IDaprService)}");

        IEnumerable<(Type, DaprControllerAttribute)> controllerClasses = FindAll()
            .Where(t =>
            {
                return handlerType == (t.Item2?.ServiceType);
            });

        return controllerClasses?.ToArray() ?? Array.Empty<(Type, DaprControllerAttribute)>();
    }
}
