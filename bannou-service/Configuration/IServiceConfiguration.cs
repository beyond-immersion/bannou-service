using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Attributes;
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
    /// Shared serializer options, between all Bannou services/consumers.
    /// References BannouJson.Options as the single source of truth for JSON serialization.
    /// IMPORTANT: Must include JsonStringEnumConverter to ensure enum values serialize
    /// as strings (e.g., "permission.capabilities_refresh") instead of numbers (e.g., 0).
    /// This is critical for client event handling where event_name is matched by string value.
    /// </summary>
    public static readonly JsonSerializerOptions BannouSerializerConfig = BannouJson.Options;

    /// <summary>
    /// Legacy switch mappings for backward compatibility.
    /// Maps legacy CLI switch names to their corresponding PascalCase property names.
    /// This allows users to use traditional --kebab-case switches even when properties
    /// are named in PascalCase (e.g., --force-service-id maps to ForceServiceId).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> LegacySwitchMappings = new Dictionary<string, string>
    {
        // Core configuration switches
        ["--force-service-id"] = "ForceServiceId",
    };

    /// <summary>
    /// Set to override GUID for administrative service endpoints.
    /// If not set, will generate a new GUID automatically on service startup.
    /// </summary>
    public Guid? ForceServiceId { get; }


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
    /// Performs all configuration validation checks.
    /// Calls <see cref="ValidateNonNullableStrings"/>, <see cref="ValidateNumericRanges"/>, and <see cref="ValidateStringLengths"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when any validation check fails.</exception>
    public void Validate()
    {
        ValidateNonNullableStrings();
        ValidateNumericRanges();
        ValidateStringLengths();
    }

    /// <summary>
    /// Validates that all non-nullable string properties have non-empty values.
    /// IMPLEMENTATION TENETS: Non-nullable strings with schema defaults must not be empty.
    /// If an env var sets a non-nullable string to empty, that's a configuration error - schema
    /// provides the default, so the only way to get empty is explicit override to empty string.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when any non-nullable string property is empty or whitespace.</exception>
    public void ValidateNonNullableStrings()
    {
        var nullabilityContext = new NullabilityInfoContext();
        var invalidProperties = new List<string>();

        foreach (var property in GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            // Only check string properties
            if (property.PropertyType != typeof(string))
                continue;

            // Check nullability - if the property is nullable (string?), skip validation
            var nullabilityInfo = nullabilityContext.Create(property);
            if (nullabilityInfo.WriteState == NullabilityState.Nullable)
                continue;

            // Non-nullable string - check if empty
            var value = property.GetValue(this) as string;
            if (string.IsNullOrWhiteSpace(value))
            {
                invalidProperties.Add(property.Name);
            }
        }

        if (invalidProperties.Count > 0)
        {
            var configTypeName = GetType().Name;
            var envPrefix = GetType().GetCustomAttribute<ServiceConfigurationAttribute>()?.EnvPrefix ?? "";
            throw new InvalidOperationException(
                $"Configuration validation failed for {configTypeName}: " +
                $"Non-nullable string properties cannot be empty. " +
                $"The following properties have empty values: {string.Join(", ", invalidProperties)}. " +
                $"These properties have schema-defined defaults - if they are empty, it means an explicit " +
                $"override to empty string was set (e.g., {envPrefix}{invalidProperties[0].ToUpperInvariant()}=\"\"). " +
                $"Either remove the override to use the schema default, or provide a valid non-empty value.");
        }
    }

    /// <summary>
    /// Validates that all numeric properties with <see cref="ConfigRangeAttribute"/> are within their allowed ranges.
    /// IMPLEMENTATION TENETS: Numeric configuration values must satisfy schema-defined constraints.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when any numeric property is outside its allowed range.</exception>
    public void ValidateNumericRanges()
    {
        var invalidProperties = new List<string>();

        foreach (var property in GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var rangeAttr = property.GetCustomAttribute<ConfigRangeAttribute>();
            if (rangeAttr == null)
                continue;

            // Get the value and convert to double for comparison
            var rawValue = property.GetValue(this);
            if (rawValue == null)
                continue; // Nullable numeric with null value - skip (handled by other validation if needed)

            double value;
            try
            {
                value = Convert.ToDouble(rawValue);
            }
            catch (InvalidCastException)
            {
                // Property type doesn't convert to double - skip
                continue;
            }

            if (!rangeAttr.IsValid(value))
            {
                var envPrefix = GetType().GetCustomAttribute<ServiceConfigurationAttribute>()?.EnvPrefix ?? "";
                invalidProperties.Add(
                    $"{property.Name}={value} (must be in range {rangeAttr.GetRangeDescription()}, " +
                    $"env: {envPrefix}{ToUpperSnakeCase(property.Name)})");
            }
        }

        if (invalidProperties.Count > 0)
        {
            var configTypeName = GetType().Name;
            throw new InvalidOperationException(
                $"Configuration validation failed for {configTypeName}: " +
                $"Numeric properties outside allowed range. " +
                $"The following properties have invalid values: {string.Join("; ", invalidProperties)}. " +
                $"Check environment variable values or remove overrides to use schema defaults.");
        }
    }

    /// <summary>
    /// Validates that all string properties with <see cref="ConfigStringLengthAttribute"/> meet their length constraints.
    /// IMPLEMENTATION TENETS: String configuration values must satisfy schema-defined length constraints.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when any string property is outside its allowed length range.</exception>
    public void ValidateStringLengths()
    {
        var nullabilityContext = new NullabilityInfoContext();
        var invalidProperties = new List<string>();

        foreach (var property in GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var lengthAttr = property.GetCustomAttribute<ConfigStringLengthAttribute>();
            if (lengthAttr == null)
                continue;

            // Only validate string properties
            if (property.PropertyType != typeof(string))
                continue;

            var value = property.GetValue(this) as string;

            // Skip validation for null values on nullable properties
            if (value == null)
            {
                var nullabilityInfo = nullabilityContext.Create(property);
                if (nullabilityInfo.WriteState == NullabilityState.Nullable)
                    continue;
            }

            if (!lengthAttr.IsValid(value))
            {
                var envPrefix = GetType().GetCustomAttribute<ServiceConfigurationAttribute>()?.EnvPrefix ?? "";
                var actualLength = value?.Length ?? 0;
                invalidProperties.Add(
                    $"{property.Name} (length={actualLength}, must be {lengthAttr.GetLengthDescription()}, " +
                    $"env: {envPrefix}{ToUpperSnakeCase(property.Name)})");
            }
        }

        if (invalidProperties.Count > 0)
        {
            var configTypeName = GetType().Name;
            throw new InvalidOperationException(
                $"Configuration validation failed for {configTypeName}: " +
                $"String properties outside allowed length range. " +
                $"The following properties have invalid lengths: {string.Join("; ", invalidProperties)}. " +
                $"Check environment variable values or remove overrides to use schema defaults.");
        }
    }

    /// <summary>
    /// Converts a PascalCase property name to UPPER_SNAKE_CASE for environment variable display.
    /// </summary>
    private static string ToUpperSnakeCase(string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
            return propertyName;

        var result = new System.Text.StringBuilder();
        for (int i = 0; i < propertyName.Length; i++)
        {
            var c = propertyName[i];
            if (i > 0 && char.IsUpper(c))
                result.Append('_');
            result.Append(char.ToUpperInvariant(c));
        }
        return result.ToString();
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
    /// Environment variables are normalized from UPPER_SNAKE_CASE to PascalCase.
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

        // Use normalized env vars to support UPPER_SNAKE_CASE -> PascalCase mapping
        var normalizedEnvVars = GetNormalizedEnvVars(envPrefix);

        IConfigurationBuilder configurationBuilder = new ConfigurationBuilder()
            .AddJsonFile("Config.json", true)
            .AddInMemoryCollection(normalizedEnvVars)
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
    /// Includes both generated switches and legacy switch mappings.
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

        // Add all legacy switch mappings (they apply globally)
        foreach (var legacyMapping in LegacySwitchMappings)
        {
            if (!allSwitchMappings.ContainsKey(legacyMapping.Key))
                allSwitchMappings[legacyMapping.Key] = legacyMapping.Value;
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
    /// Includes both generated switches from property names and legacy switch mappings.
    /// </summary>
    public static IDictionary<string, string> CreateSwitchMappings(Type configurationType)
    {
        if (!typeof(IServiceConfiguration).IsAssignableFrom(configurationType))
            throw new InvalidCastException($"Type provided does not implement {nameof(IServiceConfiguration)}");

        Dictionary<string, string> keyMappings = new();

        // Add generated switches from property names
        foreach (PropertyInfo propertyInfo in configurationType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            keyMappings[CreateSwitchFromName(propertyInfo.Name)] = propertyInfo.Name;

        // Add legacy switch mappings for backward compatibility
        // Only add if the property exists on this configuration type
        foreach (var legacyMapping in LegacySwitchMappings)
        {
            var propertyName = legacyMapping.Value;
            if (configurationType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance) != null)
            {
                keyMappings[legacyMapping.Key] = propertyName;
            }
        }

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

        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var key = entry.Key?.ToString();
            if (key == null)
                continue;

            // Check if key starts with prefix (case-insensitive)
            // null/empty prefix means no filtering - include all env vars
            if (!string.IsNullOrEmpty(envPrefix) &&
                !key.StartsWith(envPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            // Strip prefix and normalize key
            var strippedKey = string.IsNullOrEmpty(envPrefix) ? key : key.Substring(envPrefix.Length);
            var normalizedKey = NormalizeEnvVarKey(strippedKey);

            // Only add if we got a valid normalized key
            if (!string.IsNullOrEmpty(normalizedKey))
                result[normalizedKey] = entry.Value?.ToString();
        }

        return result;
    }
}
