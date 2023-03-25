using System.Reflection;
using System.Text.Json;
using Dapr.Extensions.Configuration;

namespace BeyondImmersion.BannouService.Application;

public interface IServiceConfiguration
{
    /// <summary>
    /// Shared serializer options, between all dapr services/consumers.
    /// </summary>
    public static readonly JsonSerializerOptions DaprSerializerConfig = new()
    {
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
        IgnoreReadOnlyFields = false,
        IgnoreReadOnlyProperties = false,
        IncludeFields = false,
        MaxDepth = 32,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict,
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        UnknownTypeHandling = System.Text.Json.Serialization.JsonUnknownTypeHandling.JsonElement,
        WriteIndented = false
    };

    public string? ForceServiceID { get; }

    /// <summary>
    /// Returns whether the configuration indicates ANY services should be enabled.
    /// </summary>
    public static bool IsAnyServiceEnabled()
        => IServiceAttribute.GetClassesWithAttribute<DaprServiceAttribute>()
            .Any(t => IsServiceEnabled(t.Item1));

    /// <summary>
    /// Returns whether the configuration indicates the service should be enabled.
    /// </summary>
    public static bool IsServiceEnabled<T>()
        => IsServiceEnabled(typeof(T));

    /// <summary>
    /// Returns whether the configuration indicates the service should be enabled.
    /// </summary>
    public static bool IsServiceEnabled(Type serviceType)
        => IsServiceEnabled(serviceType.GetServiceName());

    /// <summary>
    /// Returns whether the configuration indicates the service should be enabled.
    /// </summary>
    public static bool IsServiceEnabled(string serviceName)
    {
        if (serviceName.EndsWith("Service", comparisonType: StringComparison.InvariantCultureIgnoreCase))
            serviceName = serviceName.Remove(serviceName.Length - "Service".Length, "Service".Length);

        if (serviceName.EndsWith("Controller", comparisonType: StringComparison.CurrentCultureIgnoreCase))
            serviceName = serviceName.Remove(serviceName.Length - "Controller".Length, "Controller".Length);

        if (serviceName.EndsWith("Dapr", comparisonType: StringComparison.CurrentCultureIgnoreCase))
            serviceName = serviceName.Remove(serviceName.Length - "Dapr".Length, "Dapr".Length);

        IConfigurationRoot configRoot = BuildConfigurationRoot();
        var serviceEnabledFlag = configRoot.GetValue<bool?>($"{serviceName.ToUpper()}_SERVICE_ENABLED", null);
        if (serviceEnabledFlag.HasValue)
            return serviceEnabledFlag.Value;

        return ServiceConstants.ENABLE_SERVICES_BY_DEFAULT;
    }

    /// <summary>
    /// Returns whether the configuration is provided for a given service type to run properly.
    /// </summary>
    public static bool HasRequiredConfiguration<T>()
        where T : class, IDaprService
    {
        return IServiceAttribute.GetClassesWithAttribute<ServiceConfigurationAttribute>()
            .Where(t => t.Item2.ServiceType == typeof(T))
            .All(t => HasRequiredConfiguration(t.Item1));
    }

    /// <summary>
    /// Returns whether the configuration is provided for a service to run properly.
    /// </summary>
    public static bool HasRequiredConfiguration(Type configurationType)
    {
        IServiceConfiguration? serviceConfig = BuildConfiguration(configurationType);
        if (serviceConfig == null)
            return true;

        return IServiceAttribute.GetPropertiesWithAttribute(configurationType, typeof(ConfigRequiredAttribute))
            .All(t =>
            {
                var propValue = t.Item1.GetValue(serviceConfig);
                if (propValue == null)
                    return false;

                return true;
            });
    }

    /// <summary>
    /// Builds the service configuration root from available Config.json, ENVs, and command line switches.
    /// </summary>
    public static IConfigurationRoot BuildConfigurationRoot(string[]? args = null, string? envPrefix = null)
    {
        IConfigurationBuilder configurationBuilder = new ConfigurationBuilder()
            .AddJsonFile("Config.json", true)
            .AddEnvironmentVariables(envPrefix)
            .AddCommandLine(args ?? Array.Empty<string>(), CreateAllSwitchMappings());

        if (Program.DaprClient != null)
            configurationBuilder.AddDaprConfigurationStore("ConfigurationStore", Array.Empty<string>(), Program.DaprClient, TimeSpan.FromSeconds(3), null);

        return configurationBuilder.Build();
    }

    /// <summary>
    /// Builds the service configuration from available Config.json, ENVs, and command line switches.
    /// </summary>
    public static IServiceConfiguration BuildConfiguration(string[]? args = null, string? envPrefix = null)
        => BuildConfigurationRoot(args, envPrefix)
            .Get<ServiceConfiguration>((options) => options.BindNonPublicProperties = true) ?? new();

    /// <summary>
    /// Builds the given service configuration from available Config.json, ENVs, and command line switches.
    /// </summary>
    public static T BuildConfiguration<T>(string[]? args = null)
        where T : class, IServiceConfiguration, new()
    {
        string? envPrefix = null;
        ServiceConfigurationAttribute? configAttr = typeof(T).GetCustomAttribute<ServiceConfigurationAttribute>();
        if (configAttr != null)
            envPrefix = configAttr.EnvPrefix;

        return BuildConfiguration(typeof(T), args, envPrefix) as T ?? new();
    }

    /// <summary>
    /// Builds the best discovered configuration for the given service from available Config.json, ENVs, and command line switches.
    /// </summary>
    public static IServiceConfiguration? BuildServiceConfiguration<T>(string[]? args = null)
        where T : class, IDaprService
    {
        foreach ((Type, ServiceConfigurationAttribute) classWithAttr in IServiceAttribute.GetClassesWithAttribute<ServiceConfigurationAttribute>())
            if (classWithAttr.Item2.ServiceType == typeof(T))
                return BuildConfiguration(classWithAttr.Item1, args, classWithAttr.Item2.EnvPrefix);

        string? envPrefix = null;
        ServiceConfigurationAttribute? configAttr = typeof(IServiceConfiguration).GetCustomAttribute<ServiceConfigurationAttribute>();
        if (configAttr != null)
            envPrefix = configAttr.EnvPrefix;

        return BuildConfiguration(typeof(IServiceConfiguration), args, envPrefix);
    }

    /// <summary>
    /// Builds the service configuration from available Config.json, ENVs, and command line switches.
    /// </summary>
    public static IServiceConfiguration? BuildConfiguration(Type configurationType, string[]? args = null, string? envPrefix = null)
    {
        IConfigurationBuilder configurationBuilder = new ConfigurationBuilder()
            .AddJsonFile("Config.json", true)
            .AddEnvironmentVariables(envPrefix)
            .AddCommandLine(args ?? Array.Empty<string>(), CreateSwitchMappings(configurationType));

        return configurationBuilder.Build()
            .Get(configurationType, (options) => options.BindNonPublicProperties = true) as IServiceConfiguration;
    }

    /// <summary>
    /// Create and return the full lookup of switch mappings for all configuration classes.
    /// </summary>
    public static IDictionary<string, string>? CreateAllSwitchMappings()
    {
        var allSwitchMappings = new Dictionary<string, string>();
        foreach ((Type, ServiceConfigurationAttribute) classWithAttr in IServiceAttribute.GetClassesWithAttribute<ServiceConfigurationAttribute>())
            foreach (var kvp in CreateSwitchMappings(classWithAttr.Item1))
                if (!allSwitchMappings.ContainsKey(kvp.Key))
                    allSwitchMappings[kvp.Key] = kvp.Value;

        return allSwitchMappings;
    }

    /// <summary>
    /// Create and return the full lookup of switch mappings for the configuration class.
    /// </summary>
    public static IDictionary<string, string> CreateSwitchMappings<T>()
        where T : IServiceConfiguration
        => CreateSwitchMappings(typeof(T));

    /// <summary>
    /// Create and return the full lookup of switch mappings for the configuration class.
    /// </summary>
    public static IDictionary<string, string> CreateSwitchMappings(Type configurationType)
    {
        Dictionary<string, string> keyMappings = new();
        foreach (PropertyInfo propertyInfo in configurationType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            keyMappings[CreateSwitchFromName(propertyInfo.Name)] = propertyInfo.Name;

        return keyMappings;
    }

    /// <summary>
    /// Create a deterministic command switch (ie: --some-switch ) from the given property name.
    /// </summary>
    public static string CreateSwitchFromName(string propertyName)
    {
        propertyName = propertyName.ToLower();
        propertyName = propertyName.Replace('_', '-');
        propertyName = "--" + propertyName;
        return propertyName;
    }
}
