using System.Reflection;

namespace BeyondImmersion.BannouService.Controllers;

/// <summary>
/// Interface implemented for all Bannou API controllers.
/// </summary>
public interface IBannouController
{
    private static (Type, BannouControllerAttribute)[] _controllers;

    /// <summary>
    /// Gets the full list of Bannou controllers.
    /// </summary>
    public static (Type, BannouControllerAttribute)[] Controllers
    {
        get
        {
            if (_controllers == null)
            {
                IEnumerable<(Type, BannouControllerAttribute)> controllerClasses = IServiceAttribute.GetClassesWithAttribute<BannouControllerAttribute>()
                    .Where(t =>
                    {
                        if (!typeof(IBannouController).IsAssignableFrom(t.Item1))
                            return false;

                        return true;
                    });

                _controllers = controllerClasses?.ToArray() ?? [];
            }

            return _controllers;
        }
    }

    /// <summary>
    /// Gets the full list of non-service Bannou API controllers.
    /// </summary>
    public static (Type, BannouControllerAttribute)[] NonServiceControllers
    {
        get => [.. Controllers.Where(t => t.Item2?.InterfaceType == null)];
    }

    /// <summary>
    /// Gets the full list of service Bannou API controllers.
    /// </summary>
    public static (Type, BannouControllerAttribute)[] ServiceControllers
    {
        get => [.. Controllers.Where(t => t.Item2?.InterfaceType != null)];
    }

    /// <summary>
    /// Gets the full list of enabled service Bannou API controllers.
    /// </summary>
    public static (Type, BannouControllerAttribute)[] EnabledServiceControllers
    {
        get
        {
            return [.. Controllers.Where(t =>
                {
                    if (t.Item2?.InterfaceType == null)
                        return false;

                    (Type, Type, BannouServiceAttribute)? serviceInfo = ((Type, Type, BannouServiceAttribute)?)IBannouService.GetServiceInfo(t.Item2.InterfaceType);
                    return serviceInfo != null && !IBannouService.IsDisabled(serviceInfo.Value.Item3.Name);
                })];
        }
    }

    /// <summary>
    /// Gets the full list of associated controllers to the given service interface.
    /// </summary>
    public static (Type, BannouControllerAttribute)[] FindForInterface<T>()
        where T : class, IBannouService
        => FindForInterface(typeof(T));

    /// <summary>
    /// Gets the full list of associated controllers to the given service implementation.
    /// </summary>
    public static (Type, BannouControllerAttribute)[] FindForImplementation<T>()
        where T : class, IBannouService
        => FindForImplementation(typeof(T));

    /// <summary>
    /// Gets the full list of associated controllers to a given service interface.
    /// </summary>
    public static (Type, BannouControllerAttribute)[] FindForInterface(Type interfaceType)
    {
        if (!typeof(IBannouService).IsAssignableFrom(interfaceType))
            throw new InvalidCastException($"Type provided does not implement {nameof(IBannouService)}");

        IEnumerable<(Type, BannouControllerAttribute)> controllerClasses = ServiceControllers
            .Where(t => interfaceType == t.Item2.InterfaceType);

        return controllerClasses?.ToArray() ?? [];
    }

    /// <summary>
    /// Gets the full list of associated controllers to a given service implementation.
    /// </summary>
    public static (Type, BannouControllerAttribute)[] FindForImplementation(Type implementationType)
    {
        if (!typeof(IBannouService).IsAssignableFrom(implementationType))
            throw new InvalidCastException($"Type provided does not implement {nameof(IBannouService)}");

        BannouServiceAttribute? serviceAttr = implementationType.GetCustomAttribute<BannouServiceAttribute>();
        if (serviceAttr == null || serviceAttr.InterfaceType == null)
            return [];

        IEnumerable<(Type, BannouControllerAttribute)> controllerClasses = ServiceControllers
            .Where(t =>
            {
                return t.Item2.InterfaceType?.IsAssignableFrom(implementationType) ?? false;
            });

        return controllerClasses?.ToArray() ?? [];
    }
}
