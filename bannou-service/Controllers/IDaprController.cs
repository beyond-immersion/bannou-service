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

                _controllers = controllerClasses?.ToArray() ?? Array.Empty<(Type, DaprControllerAttribute)>();
            }

            return _controllers;
        }
    }

    /// <summary>
    /// Gets the full list of non-service dapr API controllers.
    /// </summary>
    public static (Type, DaprControllerAttribute)[] NonServiceControllers
    {
        get => Controllers.Where(t => t.Item2?.InterfaceType == null).ToArray();
    }

    /// <summary>
    /// Gets the full list of service dapr API controllers.
    /// </summary>
    public static (Type, DaprControllerAttribute)[] ServiceControllers
    {
        get => Controllers.Where(t => t.Item2?.InterfaceType != null).ToArray();
    }

    /// <summary>
    /// Gets the full list of enabled service dapr API controllers.
    /// </summary>
    public static (Type, DaprControllerAttribute)[] EnabledServiceControllers
    {
        get
        {
            return Controllers.Where(t =>
                {
                    if (t.Item2?.InterfaceType == null)
                        return false;

                    (Type, Type, DaprServiceAttribute)? serviceInfo = IDaprService.GetServiceInfo(t.Item2.InterfaceType);
                    return serviceInfo != null && !IDaprService.IsDisabled(serviceInfo.Value.Item2);
                })
                .ToArray();
        }
    }

    /// <summary>
    /// Gets the full list of associated controllers to the given service interface.
    /// </summary>
    public static (Type, DaprControllerAttribute)[] FindForInterface<T>()
        where T : class, IDaprService
        => FindForInterface(typeof(T));

    /// <summary>
    /// Gets the full list of associated controllers to the given service implementation.
    /// </summary>
    public static (Type, DaprControllerAttribute)[] FindForImplementation<T>()
        where T : class, IDaprService
        => FindForImplementation(typeof(T));

    /// <summary>
    /// Gets the full list of associated controllers to a given service interface.
    /// </summary>
    public static (Type, DaprControllerAttribute)[] FindForInterface(Type interfaceType)
    {
        if (!typeof(IDaprService).IsAssignableFrom(interfaceType))
            throw new InvalidCastException($"Type provided does not implement {nameof(IDaprService)}");

        IEnumerable<(Type, DaprControllerAttribute)> controllerClasses = ServiceControllers
            .Where(t =>
            {
                return interfaceType == (t.Item2.InterfaceType);
            });

        return controllerClasses?.ToArray() ?? Array.Empty<(Type, DaprControllerAttribute)>();
    }

    /// <summary>
    /// Gets the full list of associated controllers to a given service implementation.
    /// </summary>
    public static (Type, DaprControllerAttribute)[] FindForImplementation(Type implementationType)
    {
        if (!typeof(IDaprService).IsAssignableFrom(implementationType))
            throw new InvalidCastException($"Type provided does not implement {nameof(IDaprService)}");

        DaprServiceAttribute? serviceAttr = implementationType.GetCustomAttribute<DaprServiceAttribute>();
        if (serviceAttr == null || serviceAttr.InterfaceType == null)
            return Array.Empty<(Type, DaprControllerAttribute)>();

        IEnumerable<(Type, DaprControllerAttribute)> controllerClasses = ServiceControllers
            .Where(t =>
            {
                return t.Item2.InterfaceType?.IsAssignableFrom(implementationType) ?? false;
            });

        return controllerClasses?.ToArray() ?? Array.Empty<(Type, DaprControllerAttribute)>();
    }
}
