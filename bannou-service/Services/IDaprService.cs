using Microsoft.Extensions.Logging;
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
    public string GetName()
        => GetType().GetServiceName();

    /// <summary>
    /// Returns whether the configuration indicates the service should be enabled.
    /// </summary>
    public bool IsEnabled()
        => IsEnabled(GetType());

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
        => BuildConfiguration(GetType()) ?? new ServiceConfiguration();

    /// <summary>
    /// Builds the best discovered configuration for the given service from available Config.json, ENVs, and command line switches.
    /// </summary>
    public static IServiceConfiguration BuildConfiguration<T>(string[]? args = null)
        where T : class, IDaprService
        => BuildConfiguration(typeof(T), args) ?? new ServiceConfiguration();

    /// <summary>
    /// Builds the best discovered configuration for the given service from available Config.json, ENVs, and command line switches.
    /// </summary>
    public static IServiceConfiguration? BuildConfiguration(Type serviceType, string[]? args = null)
    {
        if (!typeof(IDaprService).IsAssignableFrom(serviceType))
            throw new InvalidCastException($"Type provided does not implement {nameof(IDaprService)}");

        foreach ((Type, ServiceConfigurationAttribute) classWithAttr in IServiceAttribute.GetClassesWithAttribute<ServiceConfigurationAttribute>())
            if (serviceType.IsAssignableFrom(classWithAttr.Item2.ServiceType))
                return IServiceConfiguration.BuildConfiguration(classWithAttr.Item1, args, classWithAttr.Item2.EnvPrefix);

        string? envPrefix = null;
        ServiceConfigurationAttribute? configAttr = typeof(IServiceConfiguration).GetCustomAttribute<ServiceConfigurationAttribute>();
        if (configAttr != null)
            envPrefix = configAttr.EnvPrefix;

        return IServiceConfiguration.BuildConfiguration(typeof(ServiceConfiguration), args, envPrefix);
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
        if (!typeof(IDaprService).IsAssignableFrom(serviceType))
            return false;

        return IServiceAttribute.GetClassesWithAttribute<ServiceConfigurationAttribute>()
            .Where(t => t.Item2.ServiceType == serviceType)
            .All(t => IServiceConfiguration.HasRequiredForType(t.Item1));
    }

    /// <summary>
    /// Returns the best service configuration type for the given service type.
    /// Returned type is based on DaprServiceAttribute service target type.
    /// </summary>
    public static Type GetConfigurationType(Type serviceType)
    {
        if (!typeof(IDaprService).IsAssignableFrom(serviceType))
            throw new InvalidCastException($"Type provided does not implement {nameof(IDaprService)}");

        Type? serviceConfigType = null;
        foreach (var configAttr in IServiceAttribute.GetClassesWithAttribute<ServiceConfigurationAttribute>())
        {
            if (serviceType.IsAssignableFrom(configAttr.Item2.ServiceType))
            {
                if (serviceConfigType != null && !configAttr.Item2.Primary)
                    continue;

                serviceConfigType = configAttr.Item1;
            }
        }

        return serviceConfigType ?? typeof(ServiceConfiguration);
    }

    /// <summary>
    /// Returns whether all enabled services have required configuration set.
    /// </summary>
    public static bool AllHaveRequiredConfiguration()
    {
        foreach ((Type, DaprServiceAttribute) serviceClassData in FindAll(enabledOnly: true))
        {
            Type serviceType = serviceClassData.Item1;
            var serviceConfig = GetConfigurationType(serviceType);
            if (serviceConfig == null)
                continue;

            if (!IServiceConfiguration.HasRequiredForType(serviceConfig))
            {
                Program.Logger?.Log(LogLevel.Error, null, $"Required configuration is missing to start an enabled dapr service.",
                    logParams: new JObject() { ["service_type"] = serviceType.Name });

                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns whether the configuration indicates ANY services should be enabled.
    /// </summary>
    public static bool IsAnyEnabled()
        => FindAll(enabledOnly: true).Any();

    /// <summary>
    /// Returns whether the configuration indicates the service should be enabled.
    /// </summary>
    public static bool IsEnabled<T>()
        => IsEnabled(typeof(T));

    /// <summary>
    /// Returns whether the configuration indicates the service should be enabled.
    /// </summary>
    public static bool IsEnabled(Type serviceType)
    {
        if (!typeof(IDaprService).IsAssignableFrom(serviceType))
            throw new InvalidCastException($"Type provided does not implement {nameof(IDaprService)}");

        var serviceName = serviceType.GetServiceName();
        if (string.IsNullOrWhiteSpace(serviceName))
            return false;

        return IsEnabled(serviceName);
    }

    /// <summary>
    /// Returns whether the configuration indicates the service should be enabled.
    /// </summary>
    public static bool IsEnabled(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            return false;

        IConfigurationRoot configRoot = IServiceConfiguration.BuildConfigurationRoot();
        var serviceEnabledFlag = configRoot.GetValue<bool?>($"{serviceName.ToUpper()}_SERVICE_ENABLED", null);
        if (serviceEnabledFlag.HasValue)
            return serviceEnabledFlag.Value;

        return ServiceConstants.ENABLE_SERVICES_BY_DEFAULT;
    }

    /// <summary>
    /// Find the highest priority/derived service type with the given name.
    /// </summary>
    public static (Type, DaprServiceAttribute)? FindByName(string name, bool enabledOnly = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        foreach (var serviceInfo in FindAll(enabledOnly))
            if (string.Equals(name, serviceInfo.Item2.Name))
                return serviceInfo;

        return null;
    }

    /// <summary>
    /// Gets the full list of all dapr service classes (with associated attribute) in loaded assemblies.
    /// If BestOnly selected, will only return the highest priority service with each name (not case sensitive).
    /// </summary>
    public static (Type, DaprServiceAttribute)[] FindAll(bool enabledOnly = false)
    {
        var servicesByName = new Dictionary<string, (Type, DaprServiceAttribute)>();

        // first get all service types
        List<(Type, DaprServiceAttribute)> serviceClasses = IServiceAttribute.GetClassesWithAttribute<DaprServiceAttribute>();
        if (!serviceClasses.Any())
        {
            Program.Logger?.Log(LogLevel.Trace, null, $"No service handler types were located.");
            return servicesByName.Values.ToArray();
        }

        // now filter by "best types", into lookup
        foreach ((Type, DaprServiceAttribute) serviceClass in serviceClasses)
        {
            Type serviceType = serviceClass.Item1;
            DaprServiceAttribute serviceAttr = serviceClass.Item2;

            if (serviceType.IsAbstract || serviceType.IsInterface || !serviceType.IsAssignableTo(typeof(IDaprService)))
            {
                Program.Logger?.Log(LogLevel.Trace, null, $"Invalid service handler type {serviceType.Name} won't be returned.");
                continue;
            }

            if (servicesByName.TryGetValue(serviceAttr.Name.ToLower(), out var existingEntry))
            {
                if (existingEntry.Item2.Priority)
                {
                    Program.Logger?.Log(LogLevel.Trace, null, $"Service handler type {serviceType.Name} skipped in favour of existing override.");
                    continue;
                }

                // derived types get automatic priority
                if (existingEntry.Item1.IsAssignableFrom(serviceType))
                    _ = servicesByName.Remove(serviceAttr.Name.ToLower());
            }

            // don't return a disabled service type, in a way that ensures types it derives from will also be disabled
            if (enabledOnly && !IsEnabled(serviceType))
            {
                Program.Logger?.Log(LogLevel.Trace, null, $"Service handler type {serviceType.Name} has been disabled, and won't be returned.");
                continue;
            }

            servicesByName[serviceAttr.Name.ToLower()] = serviceClass;
        }

        return servicesByName.Values.ToArray();
    }
}
