using System.Reflection;

namespace BeyondImmersion.BannouService.Controllers;

/// <summary>
/// Interface implemented for all dapr API controllers.
/// </summary>
public interface IDaprController
{
    private static (Type, DaprControllerAttribute)[] _controllers;

    /// <summary>
    /// Gets the full list of dapr controllers.
    /// </summary>
    [Obsolete]
    public static (Type, DaprControllerAttribute)[] Controllers
    {
        get
        {
            if (_controllers == null)
            {
                IEnumerable<(Type, DaprControllerAttribute)> controllerClasses = IServiceAttribute.GetClassesWithAttribute<DaprControllerAttribute>()
                    .Where(t =>
                    {
                        if (!typeof(IDaprController).IsAssignableFrom(t.Item1))
                            return false;

                        return true;
                    });

                _controllers = controllerClasses?.ToArray() ?? [];
            }

            return _controllers;
        }
    }

    /// <summary>
    /// Gets the full list of non-service dapr API controllers.
    /// </summary>
    [Obsolete]
    public static (Type, DaprControllerAttribute)[] NonServiceControllers
    {
        get => [.. Controllers.Where(t => t.Item2?.InterfaceType == null)];
    }

    /// <summary>
    /// Gets the full list of service dapr API controllers.
    /// </summary>
    [Obsolete]
    public static (Type, DaprControllerAttribute)[] ServiceControllers
    {
        get => [.. Controllers.Where(t => t.Item2?.InterfaceType != null)];
    }

    /// <summary>
    /// Gets the full list of enabled service dapr API controllers.
    /// </summary>
    [Obsolete]
    public static (Type, DaprControllerAttribute)[] EnabledServiceControllers
    {
        get
        {
            return [.. Controllers.Where(t =>
                {
                    if (t.Item2?.InterfaceType == null)
                        return false;

                    (Type, Type, DaprServiceAttribute)? serviceInfo = ((Type, Type, DaprServiceAttribute)?)IDaprService.GetServiceInfo(t.Item2.InterfaceType);
                    return serviceInfo != null && !IDaprService.IsDisabled(serviceInfo.Value.Item3.Name);
                })];
        }
    }

    /// <summary>
    /// Gets the full list of associated controllers to the given service interface.
    /// </summary>
    [Obsolete]
    public static (Type, DaprControllerAttribute)[] FindForInterface<T>()
        where T : class, IDaprService
        => FindForInterface(typeof(T));

    /// <summary>
    /// Gets the full list of associated controllers to the given service implementation.
    /// </summary>
    [Obsolete]
    public static (Type, DaprControllerAttribute)[] FindForImplementation<T>()
        where T : class, IDaprService
        => FindForImplementation(typeof(T));

    /// <summary>
    /// Gets the full list of associated controllers to a given service interface.
    /// </summary>
    [Obsolete]
    public static (Type, DaprControllerAttribute)[] FindForInterface(Type interfaceType)
    {
        if (!typeof(IDaprService).IsAssignableFrom(interfaceType))
            throw new InvalidCastException($"Type provided does not implement {nameof(IDaprService)}");

        IEnumerable<(Type, DaprControllerAttribute)> controllerClasses = ServiceControllers
            .Where(t => interfaceType == t.Item2.InterfaceType);

        return controllerClasses?.ToArray() ?? [];
    }

    /// <summary>
    /// Gets the full list of associated controllers to a given service implementation.
    /// </summary>
    [Obsolete]
    public static (Type, DaprControllerAttribute)[] FindForImplementation(Type implementationType)
    {
        if (!typeof(IDaprService).IsAssignableFrom(implementationType))
            throw new InvalidCastException($"Type provided does not implement {nameof(IDaprService)}");

        DaprServiceAttribute? serviceAttr = implementationType.GetCustomAttribute<DaprServiceAttribute>();
        if (serviceAttr == null || serviceAttr.InterfaceType == null)
            return [];

        IEnumerable<(Type, DaprControllerAttribute)> controllerClasses = ServiceControllers
            .Where(t =>
            {
                return t.Item2.InterfaceType?.IsAssignableFrom(implementationType) ?? false;
            });

        return controllerClasses?.ToArray() ?? [];
    }
}
