using Dapr.Extensions.Configuration;
using DotNetEnv;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace BeyondImmersion.BannouService.Configuration;

/// <summary>
/// Interface for service configuration with support for environment variables, JSON files, and command line arguments.
/// </summary>
public interface IServiceConfiguration
{
    /// <summary>
    /// Shared serializer options, between all dapr services/consumers.
    /// References BannouJson.Options as the single source of truth for JSON serialization.
    /// IMPORTANT: Must include JsonStringEnumConverter to ensure enum values serialize
    /// as strings (e.g., "permissions.capabilities_refresh") instead of numbers (e.g., 0).
    /// This is critical for client event handling where event_name is matched by string value.
    /// </summary>
    public static readonly JsonSerializerOptions DaprSerializerConfig = BannouJson.Options;

    /// <summary>
    /// Set to override GUID for administrative service endpoints.
    /// If not set, will generate a new GUID automatically on service startup.
    /// </summary>
    public string? Force_Service_ID { get; }


    /// <summary>
    /// Returns whether this configuration has values set for all required properties.
    /// </summary>
    public bool HasRequired()
    {
        return IServiceAttribute.GetPropertiesWithAttribute<ConfigRequiredAttribute>(GetType())
            .All(t =>
            {
                var propValue = t.Item1.GetValue(this);
                return propValue != null && (t.Item2.AllowEmptyStrings ||
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

        string? envPrefix = null;
        ServiceConfigurationAttribute? configAttr = configurationType.GetCustomAttribute<ServiceConfigurationAttribute>();
        if (configAttr != null)
            envPrefix = configAttr.EnvPrefix;

        IServiceConfiguration? serviceConfig = BuildConfiguration(configurationType, envPrefix: envPrefix);
        return serviceConfig == null || serviceConfig.HasRequired();
    }

    /// <summary>
    /// Builds the service configuration root from available .env files, Config.json, ENVs, and command line switches.
    /// </summary>
    public static IConfigurationRoot BuildConfigurationRoot(string[]? args = null, string? envPrefix = null)
    {
        // Load .env file first for local development support
        try
        {
            if (File.Exists("../.env"))
            {
                Env.Load("../.env");
            }
            else if (File.Exists(".env"))
            {
                Env.Load();
            }
        }
        catch (Exception)
        {
            // .env file is optional, ignore if not present
        }

        IConfigurationBuilder configurationBuilder = new ConfigurationBuilder()
            .AddJsonFile("Config.json", true)
            .AddEnvironmentVariables(envPrefix)
            .AddCommandLine(args ?? Environment.GetCommandLineArgs(), CreateAllSwitchMappings());

        return configurationBuilder.Build();
    }

    /// <summary>
    /// Builds the service configuration from available Config.json, ENVs, and command line switches.
    /// </summary>
    public static IServiceConfiguration BuildConfiguration(string[]? args = null, string? envPrefix = null)
        => BuildConfigurationRoot(args, envPrefix)
            .Get<AppConfiguration>((options) => options.BindNonPublicProperties = true) ?? new();

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
    /// Environment variables are read with the service-specific prefix (e.g., ASSET_, CONNECT_) and
    /// keys are normalized from UPPER_SNAKE_CASE to PascalCase to match C# property naming.
    /// Example: ASSET_STORAGE_ACCESS_KEY -> StorageAccessKey
    /// </summary>
    public static IServiceConfiguration? BuildConfiguration(Type configurationType, string[]? args = null, string? envPrefix = null)
    {
        if (!typeof(IServiceConfiguration).IsAssignableFrom(configurationType))
            throw new InvalidCastException($"Type provided does not implement {nameof(IServiceConfiguration)}");

        // Load .env file first for local development support (same as BuildConfigurationRoot)
        try
        {
            if (File.Exists("../.env"))
            {
                Env.Load("../.env");
            }
            else if (File.Exists(".env"))
            {
                Env.Load();
            }
        }
        catch (Exception)
        {
            // .env file is optional, ignore if not present
        }

        // Use normalized env vars to support UPPER_SNAKE_CASE -> PascalCase mapping
        // Example: ASSET_STORAGE_ACCESS_KEY (with ASSET_ prefix) -> StorageAccessKey
        var normalizedEnvVars = GetNormalizedEnvVars(envPrefix);

        IConfigurationBuilder configurationBuilder = new ConfigurationBuilder()
            .AddJsonFile("Config.json", true)
            .AddInMemoryCollection(normalizedEnvVars)
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

    /// <summary>
    /// Normalizes an environment variable key from UPPER_SNAKE_CASE to PascalCase.
    /// Example: STORAGE_ACCESS_KEY -> StorageAccessKey
    /// </summary>
    public static string NormalizeEnvVarKey(string envVarKey)
    {
        if (string.IsNullOrEmpty(envVarKey))
            return envVarKey;

        // Split by underscore and convert each part to title case
        var parts = envVarKey.Split('_');
        var result = new System.Text.StringBuilder();

        foreach (var part in parts)
        {
            if (string.IsNullOrEmpty(part))
                continue;

            // First letter uppercase, rest lowercase
            result.Append(char.ToUpperInvariant(part[0]));
            if (part.Length > 1)
                result.Append(part.Substring(1).ToLowerInvariant());
        }

        return result.ToString();
    }

    /// <summary>
    /// Reads environment variables with the given prefix and returns them with normalized keys.
    /// Keys are converted from UPPER_SNAKE_CASE to PascalCase to match C# property naming.
    /// </summary>
    public static IDictionary<string, string?> GetNormalizedEnvVars(string? envPrefix)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var prefix = envPrefix ?? string.Empty;

        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var key = entry.Key?.ToString();
            if (key == null)
                continue;

            // Check if key starts with prefix (case-insensitive)
            if (!string.IsNullOrEmpty(prefix) &&
                !key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            // Strip prefix and normalize key
            var strippedKey = string.IsNullOrEmpty(prefix) ? key : key.Substring(prefix.Length);
            var normalizedKey = NormalizeEnvVarKey(strippedKey);

            // Only add if we got a valid normalized key
            if (!string.IsNullOrEmpty(normalizedKey))
                result[normalizedKey] = entry.Value?.ToString();
        }

        return result;
    }
}
