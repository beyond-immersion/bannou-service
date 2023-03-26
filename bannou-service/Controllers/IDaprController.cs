using System.Reflection;

namespace BeyondImmersion.BannouService.Controllers;

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

        return IServiceConfiguration.IsServiceEnabled(serviceType);
    }

    /// <summary>
    /// Returns whether the configuration is provided for a service to run properly.
    /// </summary>
    public bool HasRequiredConfiguration(Type serviceType)
    {
        if (!typeof(IDaprService).IsAssignableFrom(serviceType))
            throw new InvalidCastException($"Type provided does not implement {nameof(IDaprService)}");

        return IServiceConfiguration.HasRequiredConfiguration(serviceType);
    }

    /// <summary>
    /// Gets the full list of associated controllers to the given service type.
    /// </summary>
    public static (Type, DaprControllerAttribute?)[] FindAll()
    {
        List<(Type, DaprControllerAttribute?)> results = new();
        Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        if (loadedAssemblies == null || loadedAssemblies.Length == 0)
            return results.ToArray();

        foreach (Assembly assembly in loadedAssemblies)
        {
            Type[] classTypes = assembly.GetTypes();
            if (classTypes == null || classTypes.Length == 0)
                continue;

            foreach (Type classType in classTypes)
            {
                if (!typeof(IDaprController).IsAssignableFrom(classType))
                    continue;

                DaprControllerAttribute? attr = classType.GetCustomAttribute<DaprControllerAttribute>();
                results.Add((classType, attr));
            }
        }

        return results.ToArray();
    }

    /// <summary>
    /// Gets the full list of associated controllers to the given service type.
    /// </summary>
    public static (Type, DaprControllerAttribute?)[] FindAll<T>()
        where T : class, IDaprService
    {
        List<(Type, DaprControllerAttribute?)> results = new();
        Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        if (loadedAssemblies == null || loadedAssemblies.Length == 0)
            return results.ToArray();

        foreach (Assembly assembly in loadedAssemblies)
        {
            Type[] classTypes = assembly.GetTypes();
            if (classTypes == null || classTypes.Length == 0)
                continue;

            foreach (Type classType in classTypes)
            {
                if (!typeof(IDaprController<T>).IsAssignableFrom(classType))
                    continue;

                DaprControllerAttribute? attr = classType.GetCustomAttribute<DaprControllerAttribute>();
                results.Add((classType, attr));
            }
        }

        return results.ToArray();
    }

    /// <summary>
    /// Gets the full list of associated controllers to a given service type.
    /// </summary>
    public static (Type, DaprControllerAttribute?)[] FindAll(Type serviceType)
    {
        if (!typeof(IDaprService).IsAssignableFrom(serviceType))
            throw new InvalidCastException($"Type provided does not implement {nameof(IDaprService)}");

        List<(Type, DaprControllerAttribute?)> results = new();
        Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        if (loadedAssemblies == null || loadedAssemblies.Length == 0)
            return results.ToArray();

        foreach (Assembly assembly in loadedAssemblies)
        {
            Type[] classTypes = assembly.GetTypes();
            if (classTypes == null || classTypes.Length == 0)
                continue;

            Type genericControllerType = typeof(IDaprController<>).MakeGenericType(serviceType);
            foreach (Type classType in classTypes)
            {
                if (!genericControllerType.IsAssignableFrom(classType))
                    continue;

                DaprControllerAttribute? attr = classType.GetCustomAttribute<DaprControllerAttribute>();
                results.Add((classType, attr));
            }
        }

        return results.ToArray();
    }
}

/// <summary>
/// Implemented for all service API controller.
/// </summary>
public interface IDaprController<T> : IDaprController
    where T : class, IDaprService
{
    /// <summary>
    /// Returns whether the configuration indicates the service should be enabled.
    /// </summary>
    public bool IsEnabled()
        => IServiceConfiguration.IsServiceEnabled(typeof(T));

    /// <summary>
    /// Returns whether the configuration is provided for a service to run properly.
    /// </summary>
    public bool HasRequiredConfiguration()
        => IServiceConfiguration.HasRequiredConfiguration(typeof(T));
}
