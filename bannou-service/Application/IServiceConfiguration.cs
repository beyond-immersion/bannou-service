using Dapr.Extensions.Configuration;
using System.Reflection;
using System.Text.Json;

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
    /// Returns whether this configuration has values set for all required properties.
    /// </summary>
    public bool HasRequired()
    {
        return IServiceAttribute.GetPropertiesWithAttribute<ConfigRequiredAttribute>(GetType())
            .All(t =>
            {
                var propValue = t.Item1.GetValue(this);
                return propValue != null
&& (t.Item2.AllowEmptyStrings ||
                    t.Item1.PropertyType != typeof(string) ||
!string.IsNullOrWhiteSpace((string)propValue));
            });
    }

    /// <summary>
    /// Returns whether the required configuration is provided for the given configuration type.
    /// </summary>
    public static bool HasRequiredForType<T>()
        where T : class, IServiceConfiguration
        => HasRequiredForType(typeof(T));

    /// <summary>
    /// Returns whether the required configuration is provided for the given configuration type.
    /// </summary>
    public static bool HasRequiredForType(Type configurationType)
    {
        if (!typeof(IServiceConfiguration).IsAssignableFrom(configurationType))
            throw new InvalidCastException($"Type provided does not implement {nameof(IServiceConfiguration)}");

        IServiceConfiguration? serviceConfig = BuildConfiguration(configurationType);
        return serviceConfig == null || serviceConfig.HasRequired();
    }

    /// <summary>
    /// Builds the service configuration root from available Config.json, ENVs, and command line switches.
    /// </summary>
    public static IConfigurationRoot BuildConfigurationRoot(string[]? args = null, string? envPrefix = null)
    {
        IConfigurationBuilder configurationBuilder = new ConfigurationBuilder()
            .AddJsonFile("Config.json", true)
            .AddEnvironmentVariables(envPrefix)
            .AddCommandLine(args ?? Environment.GetCommandLineArgs(), CreateAllSwitchMappings());

        if (Program.DaprClient != null && Program.Configuration?.DaprConfigurationName != null)
        {
            _ = configurationBuilder.AddDaprConfigurationStore(Program.Configuration.DaprConfigurationName,
                Array.Empty<string>(), Program.DaprClient, TimeSpan.FromSeconds(3), null);
        }

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
    /// Builds the service configuration from available Config.json, ENVs, and command line switches.
    /// </summary>
    public static IServiceConfiguration? BuildConfiguration(Type configurationType, string[]? args = null, string? envPrefix = null)
    {
        if (!typeof(IServiceConfiguration).IsAssignableFrom(configurationType))
            throw new InvalidCastException($"Type provided does not implement {nameof(IServiceConfiguration)}");

        IConfigurationBuilder configurationBuilder = new ConfigurationBuilder()
            .AddJsonFile("Config.json", true)
            .AddEnvironmentVariables(envPrefix)
            .AddCommandLine(args ?? Environment.GetCommandLineArgs(), CreateSwitchMappings(configurationType));

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
        {
            foreach (KeyValuePair<string, string> kvp in CreateSwitchMappings(classWithAttr.Item1))
            {
                if (!allSwitchMappings.ContainsKey(kvp.Key))
                    allSwitchMappings[kvp.Key] = kvp.Value;
            }
        }

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
        if (!typeof(IServiceConfiguration).IsAssignableFrom(configurationType))
            throw new InvalidCastException($"Type provided does not implement {nameof(IServiceConfiguration)}");

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
