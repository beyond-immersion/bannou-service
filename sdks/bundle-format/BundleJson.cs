using BeyondImmersion.Bannou.Core;
using System.Text.Json;

namespace BeyondImmersion.Bannou.Bundle.Format;

/// <summary>
/// JSON serialization helper for bundle format code.
/// Delegates to BannouJson for consistent serialization settings.
/// </summary>
internal static class BundleJson
{
    /// <summary>
    /// Standard JSON serializer options for bundle format.
    /// Uses BannouJson.Options as the single source of truth.
    /// </summary>
    public static JsonSerializerOptions Options => BannouJson.Options;

    /// <summary>
    /// Deserialize JSON string to object.
    /// </summary>
    public static T? Deserialize<T>(string json) => BannouJson.Deserialize<T>(json);

    /// <summary>
    /// Serialize object to JSON string.
    /// </summary>
    public static string Serialize<T>(T value) => BannouJson.Serialize(value);
}
