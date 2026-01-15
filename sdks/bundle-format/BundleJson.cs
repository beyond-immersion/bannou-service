using System.Text.Json;
using System.Text.Json.Serialization;

namespace BeyondImmersion.Bannou.Bundle.Format;

/// <summary>
/// JSON serialization helper for bundle format code.
/// This provides consistent JSON serialization settings compatible with
/// Bannou's service-side serialization (BannouJson).
/// </summary>
internal static class BundleJson
{
    /// <summary>
    /// Standard JSON serializer options for bundle format.
    /// Matches the configuration used by Bannou services.
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
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Deserialize JSON string to object.
    /// </summary>
    public static T? Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, Options);
    }

    /// <summary>
    /// Serialize object to JSON string.
    /// </summary>
    public static string Serialize<T>(T value)
    {
        return JsonSerializer.Serialize(value, Options);
    }
}
