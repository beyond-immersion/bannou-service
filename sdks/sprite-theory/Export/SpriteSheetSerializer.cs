using System.Text.Json;
using System.Text.Json.Serialization;
using BeyondImmersion.Bannou.SpriteTheory.Metadata;

namespace BeyondImmersion.Bannou.SpriteTheory.Export;

/// <summary>
/// Serializes and deserializes <see cref="SpriteSheet"/> metadata to and from JSON.
/// Uses System.Text.Json with camelCase naming, indented output, and null-value omission.
/// </summary>
/// <remarks>
/// <para>
/// This is a standalone SDK — it uses System.Text.Json directly, not BannouJson,
/// because sprite-theory has no dependency on bannou-service.
/// </para>
/// <para>
/// The JSON format is the canonical output for sprite sheet metadata, consumed by
/// game runtimes in any language. Property names use camelCase to match JavaScript conventions.
/// </para>
/// </remarks>
public static class SpriteSheetSerializer
{
    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Serializes a <see cref="SpriteSheet"/> to a JSON string with camelCase property names,
    /// indented formatting, and null values omitted.
    /// </summary>
    /// <param name="spriteSheet">The sprite sheet metadata to serialize.</param>
    /// <returns>A JSON string representation of the sprite sheet.</returns>
    public static string Serialize(SpriteSheet spriteSheet)
    {
        return JsonSerializer.Serialize(spriteSheet, SerializeOptions);
    }

    /// <summary>
    /// Deserializes a JSON string to a <see cref="SpriteSheet"/> instance.
    /// Property matching is case-insensitive.
    /// </summary>
    /// <param name="json">The JSON string to deserialize.</param>
    /// <returns>The deserialized sprite sheet metadata.</returns>
    /// <exception cref="JsonException">Thrown when the JSON is invalid or deserialization produces null.</exception>
    public static SpriteSheet Deserialize(string json)
    {
        return JsonSerializer.Deserialize<SpriteSheet>(json, DeserializeOptions)
            ?? throw new JsonException("Failed to deserialize SpriteSheet: result was null");
    }
}
