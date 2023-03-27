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
    /// Returns whether the configuration is provided for a service to run properly.
    /// </summary>
    public bool HasRequiredConfiguration()
        => IServiceConfiguration.HasRequiredConfiguration(GetType());

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
            if (classWithAttr.Item2.ServiceType == serviceType)
                return IServiceConfiguration.BuildConfiguration(classWithAttr.Item1, args, classWithAttr.Item2.EnvPrefix);

        string? envPrefix = null;
        ServiceConfigurationAttribute? configAttr = typeof(IServiceConfiguration).GetCustomAttribute<ServiceConfigurationAttribute>();
        if (configAttr != null)
            envPrefix = configAttr.EnvPrefix;

        return IServiceConfiguration.BuildConfiguration(typeof(IServiceConfiguration), args, envPrefix);
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
            .All(t => IServiceConfiguration.HasRequiredConfiguration(t.Item1));
    }

    /// <summary>
    /// Returns the best service configuration type for the given service type.
    /// Returned type is based on DaprServiceAttribute service target type.
    /// </summary>
    public static Type GetConfiguration(Type serviceType)
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
            var serviceConfig = GetConfiguration(serviceType);
            if (serviceConfig == null)
                continue;

            if (!HasRequiredConfiguration(serviceConfig))
            {
                Program.Logger.Log(LogLevel.Debug, null, $"Required configuration is missing to start an enabled dapr service.",
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
        => IServiceAttribute.GetClassesWithAttribute<DaprServiceAttribute>()
            .Any(t => IsEnabled(t.Item1));

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

        return IsEnabled(serviceType.GetServiceName());
    }

    /// <summary>
    /// Returns whether the configuration indicates the service should be enabled.
    /// </summary>
    public static bool IsEnabled(string serviceName)
    {
        if (serviceName.EndsWith("Service", comparisonType: StringComparison.InvariantCultureIgnoreCase))
            serviceName = serviceName.Remove(serviceName.Length - "Service".Length, "Service".Length);

        if (serviceName.EndsWith("Controller", comparisonType: StringComparison.CurrentCultureIgnoreCase))
            serviceName = serviceName.Remove(serviceName.Length - "Controller".Length, "Controller".Length);

        if (serviceName.EndsWith("Dapr", comparisonType: StringComparison.CurrentCultureIgnoreCase))
            serviceName = serviceName.Remove(serviceName.Length - "Dapr".Length, "Dapr".Length);

        IConfigurationRoot configRoot = IServiceConfiguration.BuildConfigurationRoot();
        var serviceEnabledFlag = configRoot.GetValue<bool?>($"{serviceName.ToUpper()}_SERVICE_ENABLED", null);
        if (serviceEnabledFlag.HasValue)
            return serviceEnabledFlag.Value;

        return ServiceConstants.ENABLE_SERVICES_BY_DEFAULT;
    }

    /// <summary>
    /// Gets the full list of all dapr service classes (with associated attribute) in loaded assemblies.
    /// </summary>
    public static (Type, DaprServiceAttribute)[] FindAll(bool enabledOnly = false)
    {
        List<(Type, DaprServiceAttribute)> serviceClasses = IServiceAttribute.GetClassesWithAttribute<DaprServiceAttribute>();
        if (!serviceClasses.Any())
        {
            Program.Logger.Log(LogLevel.Error, null, $"No dapr services found to instantiate.");
            return Array.Empty<(Type, DaprServiceAttribute)>();
        }

        // prefixes need to be unique, so assign to a tmp hash/dictionary lookup
        var serviceLookup = new Dictionary<string, (Type, DaprServiceAttribute)>();
        foreach ((Type, DaprServiceAttribute) serviceClass in serviceClasses)
        {
            Type serviceType = serviceClass.Item1;
            DaprServiceAttribute serviceAttr = serviceClass.Item2;

            if (!typeof(IDaprService).IsAssignableFrom(serviceType))
            {
                Program.Logger.Log(LogLevel.Error, null, $"Dapr service attribute attached to a non-service class.",
                    logParams: new JObject() { ["service_type"] = serviceType.Name });
                continue;
            }

            if (enabledOnly && !IsEnabled(serviceType))
                continue;

            var servicePrefix = ((IDaprService)serviceType).GetName().ToLower();
            if (!serviceLookup.ContainsKey(servicePrefix) || serviceClass.GetType().Assembly != Assembly.GetExecutingAssembly())
                serviceLookup[servicePrefix] = serviceClass;
        }

        return serviceLookup.Values.ToArray();
    }
}
