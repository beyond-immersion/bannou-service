using System.Text.Json;
using System.Text.Json.Serialization;

namespace BeyondImmersion.BannouService.Configuration;

/// <summary>
/// Static helper for JSON serialization/deserialization that always uses Bannou's
/// standard configuration. This is the SINGLE SOURCE OF TRUTH for JSON serialization
/// settings across all Bannou services and SDKs.
///
/// USE THIS INSTEAD OF JsonSerializer.Deserialize/Serialize DIRECTLY.
///
/// This ensures consistent behavior across the codebase:
/// - Case-insensitive property matching (handles both PascalCase and camelCase)
/// - Enums serialize as strings matching C# enum names (e.g., "GettingStarted")
/// - Null values are ignored when writing
/// - Strict number handling (no string-to-number coercion)
/// - Proper handling of all Bannou model types
///
/// Example usage:
///   var model = BannouJson.Deserialize&lt;MyModel&gt;(jsonString);
///   var json = BannouJson.Serialize(model);
/// </summary>
public static class BannouJson
{
    /// <summary>
    /// Standard JSON serializer options used throughout Bannou.
    /// This is the canonical configuration - all JSON operations should use this.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        IgnoreReadOnlyFields = false,
        IgnoreReadOnlyProperties = false,
        IncludeFields = false,
        MaxDepth = 32,
        NumberHandling = JsonNumberHandling.Strict,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        UnknownTypeHandling = JsonUnknownTypeHandling.JsonElement,
        WriteIndented = false,
        // Serialize enums as strings matching C# enum names (e.g., GettingStarted -> "GettingStarted")
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Deserialize JSON string to object using Bannou's standard configuration.
    /// </summary>
    public static T? Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, Options);
    }

    /// <summary>
    /// Deserialize JSON string to object using Bannou's standard configuration.
    /// Throws if result is null.
    /// </summary>
    public static T DeserializeRequired<T>(string json) where T : class
    {
        return JsonSerializer.Deserialize<T>(json, Options)
            ?? throw new JsonException($"Failed to deserialize JSON to {typeof(T).Name}");
    }

    /// <summary>
    /// Serialize object to JSON string using Bannou's standard configuration.
    /// </summary>
    public static string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, Options);
    }

    /// <summary>
    /// Serialize object to JSON string using Bannou's standard configuration with explicit type.
    /// </summary>
    public static string Serialize(object value, Type inputType)
    {
        return JsonSerializer.Serialize(value, inputType, Options);
    }

    /// <summary>
    /// Deserialize from UTF-8 bytes using Bannou's standard configuration.
    /// </summary>
    public static T? Deserialize<T>(ReadOnlySpan<byte> utf8Json)
    {
        return JsonSerializer.Deserialize<T>(utf8Json, Options);
    }

    /// <summary>
    /// Serialize object to UTF-8 bytes using Bannou's standard configuration.
    /// </summary>
    public static byte[] SerializeToUtf8Bytes<T>(T value)
    {
        return JsonSerializer.SerializeToUtf8Bytes(value, Options);
    }

    /// <summary>
    /// Async deserialize from stream using Bannou's standard configuration.
    /// </summary>
    public static ValueTask<T?> DeserializeAsync<T>(Stream utf8Json, CancellationToken cancellationToken = default)
    {
        return JsonSerializer.DeserializeAsync<T>(utf8Json, Options, cancellationToken);
    }

    /// <summary>
    /// Async serialize to stream using Bannou's standard configuration.
    /// </summary>
    public static async Task SerializeAsync<T>(Stream utf8Json, T value, CancellationToken cancellationToken = default)
    {
        await JsonSerializer.SerializeAsync(utf8Json, value, Options, cancellationToken);
    }

    /// <summary>
    /// Serialize object to JsonElement using Bannou's standard configuration.
    /// </summary>
    public static JsonElement SerializeToElement<T>(T value)
    {
        return JsonSerializer.SerializeToElement(value, Options);
    }

    /// <summary>
    /// Apply Bannou's standard JSON serializer settings to an existing options object.
    /// Use this when configuring external libraries (MassTransit, etc.) that provide their own options.
    /// </summary>
    /// <param name="target">The options object to configure</param>
    /// <returns>The configured options object for fluent chaining</returns>
    public static JsonSerializerOptions ApplyBannouSettings(JsonSerializerOptions target)
    {
        target.AllowTrailingCommas = Options.AllowTrailingCommas;
        target.DefaultIgnoreCondition = Options.DefaultIgnoreCondition;
        target.IgnoreReadOnlyFields = Options.IgnoreReadOnlyFields;
        target.IgnoreReadOnlyProperties = Options.IgnoreReadOnlyProperties;
        target.IncludeFields = Options.IncludeFields;
        target.MaxDepth = Options.MaxDepth;
        target.NumberHandling = Options.NumberHandling;
        target.PropertyNameCaseInsensitive = Options.PropertyNameCaseInsensitive;
        target.ReadCommentHandling = Options.ReadCommentHandling;
        target.UnknownTypeHandling = Options.UnknownTypeHandling;
        target.WriteIndented = Options.WriteIndented;

        // Add converters (avoid duplicates)
        foreach (var converter in Options.Converters)
        {
            if (!target.Converters.Any(c => c.GetType() == converter.GetType()))
            {
                target.Converters.Add(converter);
            }
        }

        return target;
    }
}

/// <summary>
/// Extension methods for JSON serialization using Bannou's standard configuration.
/// Provides a fluent API for common JSON operations.
/// </summary>
public static class BannouJsonExtensions
{
    /// <summary>
    /// Deserialize this JSON string using Bannou's standard configuration.
    /// </summary>
    public static T? FromJson<T>(this string json)
    {
        return BannouJson.Deserialize<T>(json);
    }

    /// <summary>
    /// Serialize this object to JSON using Bannou's standard configuration.
    /// </summary>
    public static string ToJson<T>(this T value)
    {
        return BannouJson.Serialize(value);
    }
}
