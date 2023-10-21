using BeyondImmersion.BannouService.Configuration;
using Newtonsoft.Json.Linq;
using System.Reflection;

namespace BeyondImmersion.BannouService.Services;

/// <summary>
/// Interface to implement for all internal dapr service,
/// which provides the logic for any given set of APIs.
/// 
/// For example, the Inventory service is in charge of
/// any API calls that desire to create/modify inventory
/// data in the game.
/// </summary>
public interface IDaprService
{
    async Task OnStart()
    {
        await Task.CompletedTask;
    }

    async Task OnRunning()
    {
        await Task.CompletedTask;
    }

    async Task OnShutdown()
    {
        await Task.CompletedTask;
    }

    public string? GetName()
        => GetType().GetServiceName();

    /// <summary>
    /// Returns whether the configuration indicates the service should be disabled.
    /// </summary>
    public bool IsDisabled()
        => IsDisabled(GetType());

    /// <summary>
    /// Returns the best configuration type for this service type.
    /// </summary>
    public Type GetConfigurationType()
        => GetConfigurationType(GetType());

    /// <summary>
    /// Returns whether the configuration is provided for a service to run properly.
    /// </summary>
    public bool HasRequiredConfiguration()
        => IServiceConfiguration.HasRequiredForType(GetConfigurationType());

    /// <summary>
    /// Builds the best discovered configuration for the given service from available Config.json, ENVs, and command line switches.
    /// </summary>
    public IServiceConfiguration BuildConfiguration()
        => BuildConfiguration(GetType()) ?? new AppConfiguration();

    /// <summary>
    /// Builds the best discovered configuration for the given service from available Config.json, ENVs, and command line switches.
    /// </summary>
    public static IServiceConfiguration BuildConfiguration<T>(string[]? args = null)
        where T : class, IDaprService
        => BuildConfiguration(typeof(T), args) ?? new AppConfiguration();

    /// <summary>
    /// Builds the best discovered configuration for the given service from available Config.json, ENVs, and command line switches.
    /// </summary>
    public static IServiceConfiguration? BuildConfiguration(Type serviceType, string[]? args = null)
    {
        if (!typeof(IDaprService).IsAssignableFrom(serviceType))
            throw new InvalidCastException($"Type provided does not implement {nameof(IDaprService)}");

        foreach ((Type, ServiceConfigurationAttribute) classWithAttr in IServiceAttribute.GetClassesWithAttribute<ServiceConfigurationAttribute>())
        {
            if (serviceType.IsAssignableFrom(classWithAttr.Item2.ServiceType))
                return IServiceConfiguration.BuildConfiguration(classWithAttr.Item1, args, classWithAttr.Item2.EnvPrefix);
        }

        string? envPrefix = null;
        ServiceConfigurationAttribute? configAttr = typeof(IServiceConfiguration).GetCustomAttribute<ServiceConfigurationAttribute>();
        if (configAttr != null)
            envPrefix = configAttr.EnvPrefix;

        return IServiceConfiguration.BuildConfiguration(typeof(AppConfiguration), args, envPrefix);
    }

    /// <summary>
    /// Returns whether the configuration is provided for a given service type to run properly.
    /// </summary>
    public static bool HasRequiredConfiguration<T>()
        where T : class, IDaprService
        => HasRequiredConfiguration(typeof(T));

    /// <summary>
    /// Returns whether the configuration is provided for a given service type to run properly.
    /// </summary>
    public static bool HasRequiredConfiguration(Type serviceType)
    {
        return typeof(IDaprService).IsAssignableFrom(serviceType)
            && IServiceAttribute.GetClassesWithAttribute<ServiceConfigurationAttribute>()
                .Where(t => t.Item2.ServiceType == serviceType)
                .All(t => IServiceConfiguration.HasRequiredForType(t.Item1));
    }

    /// <summary>
    /// Returns the best service configuration type for the given service type.
    /// Returned type is based on DaprServiceAttribute service target type.
    /// </summary>
    public static Type GetConfigurationType(Type handlerType)
    {
        if (!typeof(IDaprService).IsAssignableFrom(handlerType))
            throw new InvalidCastException($"Type provided does not implement {nameof(IDaprService)}");

        Type? serviceConfigType = null;
        foreach ((Type, ServiceConfigurationAttribute) configAttr in IServiceAttribute.GetClassesWithAttribute<ServiceConfigurationAttribute>())
        {
            if (handlerType.IsAssignableFrom(configAttr.Item2.ServiceType))
            {
                if (serviceConfigType != null)
                    continue;

                serviceConfigType = configAttr.Item1;
            }
        }

        return serviceConfigType ?? typeof(AppConfiguration);
    }

    /// <summary>
    /// Returns whether all enabled services have required configuration set.
    /// </summary>
    public static bool AllHaveRequiredConfiguration((Type, Type, DaprServiceAttribute)[] enabledServiceInfo)
    {
        foreach (var serviceInfo in enabledServiceInfo)
        {
            Type handlerType = serviceInfo.Item1;
            Type serviceType = serviceInfo.Item2;
            Type serviceConfig = GetConfigurationType(handlerType);
            if (serviceConfig == null)
                continue;

            if (!IServiceConfiguration.HasRequiredForType(serviceConfig))
            {
                Program.Logger?.Log(LogLevel.Error, null, $"Required configuration is missing to start an enabled dapr service.");
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns whether the configuration indicates ANY services should be enabled.
    /// </summary>
    public static bool IsAnyEnabled()
        => GetAllServiceInfo(enabledOnly: true).Any();

    /// <summary>
    /// Returns whether the configuration indicates the service should be enabled.
    /// </summary>
    public static bool IsDisabled<T>()
        => IsDisabled(typeof(T));

    /// <summary>
    /// Returns whether the configuration indicates the service should be disabled.
    /// </summary>
    public static bool IsDisabled(Type serviceType)
    {
        if (!typeof(IDaprService).IsAssignableFrom(serviceType))
            throw new InvalidCastException($"Type provided does not implement {nameof(IDaprService)}");

        var serviceName = serviceType.GetServiceName();
        return IsDisabled(serviceName);
    }

    /// <summary>
    /// Returns whether the configuration indicates the service should be disabled.
    /// </summary>
    public static bool IsDisabled(string? serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            return !Program.Configuration.Services_Enabled;

        var config = IServiceConfiguration.BuildConfiguration(typeof(BaseServiceConfiguration), null, serviceName.ToUpper() + "_");
        return config?.Service_Disabled == null ? !Program.Configuration.Services_Enabled : config.Service_Disabled.Value;
    }

    /// <summary>
    /// Find the highest priority/derived service type with the given name.
    /// </summary>
    /// <returns>Interface Type, Implementation Type, Service Attribute</returns>
    public static (Type, Type, DaprServiceAttribute)? GetServiceInfo(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        foreach ((Type, Type, DaprServiceAttribute) serviceInfo in GetAllServiceInfo())
        {
            if (string.Equals(name, serviceInfo.Item3.Name, StringComparison.InvariantCultureIgnoreCase))
                return serviceInfo;
        }

        return null;
    }

    /// <summary>
    /// Find the implementation for the given service handler.
    /// </summary>
    /// <returns>Interface Type, Implementation Type, Service Attribute</returns>
    public static (Type, Type, DaprServiceAttribute)? GetServiceInfo(Type handlerType)
    {
        foreach ((Type, Type, DaprServiceAttribute) serviceInfo in GetAllServiceInfo())
        {
            if (serviceInfo.Item1 == handlerType)
                return serviceInfo;
        }

        return null;
    }

    /// <summary>
    /// Gets the full list of all dapr service classes (with associated attribute) in loaded assemblies.
    /// If enabledOnly set, will not return services that have been disabled via configuration.
    /// </summary>
    /// <returns>Interface Type, Implementation Type, Service Attribute</returns>
    public static (Type, Type, DaprServiceAttribute)[] GetAllServiceInfo(bool enabledOnly = false)
    {
        // first get all service types
        List<(Type, DaprServiceAttribute)> serviceProviders = IServiceAttribute.GetClassesWithAttribute<DaprServiceAttribute>();
        if (!serviceProviders.Any())
        {
            Program.Logger?.Log(LogLevel.Trace, null, $"No service handler types were located.");
            return Array.Empty<(Type, Type, DaprServiceAttribute)>();
        }

        var handlerLookup = new Dictionary<Type, (Type, Type, DaprServiceAttribute)>();

        // now filter by "best types", into lookup
        foreach ((Type, DaprServiceAttribute) serviceProvider in serviceProviders)
        {
            Type serviceType = serviceProvider.Item1;
            DaprServiceAttribute serviceAttr = serviceProvider.Item2;
            Type handlerType = serviceAttr.InterfaceType ?? serviceType;

            Program.Logger?.Log(LogLevel.Trace, null, $"Checking service type {serviceType.Name}...");

            if (serviceType.IsAbstract || serviceType.IsInterface || !serviceType.IsAssignableTo(typeof(IDaprService)))
            {
                Program.Logger?.Log(LogLevel.Debug, null, $"Invalid service type {serviceType.Name} won't be returned.");
                continue;
            }

            if (!handlerType.IsAssignableTo(typeof(IDaprService)))
            {
                Program.Logger?.Log(LogLevel.Debug, null, $"Invalid handler for service type {serviceType.Name}.");
                continue;
            }

            if (handlerLookup.TryGetValue(handlerType, out (Type, Type, DaprServiceAttribute) existingEntry))
            {
                if (existingEntry.Item3.Priority)
                {
                    Program.Logger?.Log(LogLevel.Debug, null, $"Service type {serviceType.Name} skipped in favour of existing override {existingEntry.Item2.Name}.");
                    continue;
                }

                if (existingEntry.Item2.IsAssignableTo(serviceType))
                {
                    Program.Logger?.Log(LogLevel.Debug, null, $"Service type {serviceType.Name} skipped in favour of existing more-derived type {existingEntry.Item2.Name}.");
                    continue;
                }

                // derived types get automatic priority
                if (existingEntry.Item2.IsAssignableFrom(serviceType))
                {
                    Program.Logger?.Log(LogLevel.Debug, null, $"Service type {serviceType.Name} is more derived than existing type {existingEntry.Item2.Name}, and will override it.");
                    _ = handlerLookup.Remove(handlerType);
                }
            }

            // don't return a disabled service type
            if (enabledOnly && IsDisabled(serviceType))
            {
                Program.Logger?.Log(LogLevel.Debug, null, $"Service type {serviceType.Name} has been disabled, and won't be returned.");
                continue;
            }

            handlerLookup[handlerType] = (handlerType, serviceType, serviceAttr);
        }

        return handlerLookup.Values.ToArray();
    }
}
