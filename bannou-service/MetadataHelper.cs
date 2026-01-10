using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace BeyondImmersion.BannouService;

/// <summary>
/// Utility class for converting metadata objects between different representations.
/// Handles deserialization of JsonElement metadata to typed dictionaries.
/// </summary>
/// <remarks>
/// This class is used across multiple services (Analytics, Leaderboard, Achievement)
/// to convert metadata from JSON payloads into usable dictionary formats.
/// All dictionaries use case-insensitive key comparison for consistent metadata access.
/// </remarks>
public static class MetadataHelper
{
    /// <summary>
    /// Converts metadata object to a case-insensitive Dictionary&lt;string, object&gt;.
    /// Handles JsonElement, IDictionary&lt;string, object&gt;, and generic IDictionary types.
    /// </summary>
    /// <param name="metadata">The metadata object to convert. Can be null.</param>
    /// <returns>A case-insensitive dictionary, or null if input is null or not a valid dictionary type.</returns>
    public static Dictionary<string, object>? ConvertToDictionary(object? metadata)
    {
        if (metadata == null)
        {
            return null;
        }

        if (metadata is IDictionary<string, object> typedDictionary)
        {
            return new Dictionary<string, object>(typedDictionary, StringComparer.OrdinalIgnoreCase);
        }

        if (metadata is System.Collections.IDictionary dictionary)
        {
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (System.Collections.DictionaryEntry entry in dictionary)
            {
                if (entry.Key is string key && entry.Value != null)
                {
                    result[key] = entry.Value;
                }
            }
            return result;
        }

        if (metadata is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Object)
        {
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in jsonElement.EnumerateObject())
            {
                var value = ConvertJsonElement(property.Value);
                if (value != null)
                {
                    result[property.Name] = value;
                }
            }
            return result;
        }

        return null;
    }

    /// <summary>
    /// Converts metadata object to a case-insensitive IReadOnlyDictionary&lt;string, object&gt;.
    /// </summary>
    /// <param name="metadata">The metadata object to convert.</param>
    /// <returns>A read-only dictionary, or null if input is null or not a valid dictionary type.</returns>
    public static IReadOnlyDictionary<string, object>? ConvertToReadOnlyDictionary(object? metadata)
        => ConvertToDictionary(metadata);

    /// <summary>
    /// Converts metadata object to a case-insensitive Dictionary&lt;string, string&gt;.
    /// All values are converted to their string representation.
    /// </summary>
    /// <param name="metadata">The metadata object to convert.</param>
    /// <returns>A string dictionary, or null if input is null or not a valid dictionary type.</returns>
    public static IReadOnlyDictionary<string, string>? ConvertToStringDictionary(object? metadata)
    {
        if (metadata == null)
        {
            return null;
        }

        if (metadata is Dictionary<string, string> strDict)
        {
            return strDict;
        }

        if (metadata is Dictionary<string, object> objDict)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in objDict)
            {
                result[kvp.Key] = kvp.Value?.ToString() ?? "";
            }
            return result;
        }

        if (metadata is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Object)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in jsonElement.EnumerateObject())
            {
                result[property.Name] = GetStringValue(property.Value);
            }
            return result;
        }

        // Try converting via the object dictionary path
        var objectDict = ConvertToDictionary(metadata);
        if (objectDict != null)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in objectDict)
            {
                result[kvp.Key] = kvp.Value?.ToString() ?? "";
            }
            return result;
        }

        return null;
    }

    /// <summary>
    /// Attempts to get a string value from metadata by key.
    /// </summary>
    /// <param name="metadata">The metadata dictionary (converted or original).</param>
    /// <param name="key">The key to look up.</param>
    /// <param name="value">The string value if found.</param>
    /// <returns>True if a non-empty string value was found.</returns>
    public static bool TryGetString(IReadOnlyDictionary<string, object>? metadata, string key, out string value)
    {
        value = string.Empty;
        if (metadata == null || !metadata.TryGetValue(key, out var raw) || raw == null)
        {
            return false;
        }

        if (raw is string text && !string.IsNullOrWhiteSpace(text))
        {
            value = text;
            return true;
        }

        if (raw is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                var textValue = element.GetString();
                if (!string.IsNullOrWhiteSpace(textValue))
                {
                    value = textValue;
                    return true;
                }
            }
            else if (element.ValueKind == JsonValueKind.Number)
            {
                value = element.ToString();
                return true;
            }
        }

        var rawText = raw.ToString();
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return false;
        }

        value = rawText;
        return true;
    }

    /// <summary>
    /// Converts a JsonElement to its appropriate .NET type.
    /// </summary>
    /// <param name="element">The JSON element to convert.</param>
    /// <returns>The converted value, or null for null/undefined elements.</returns>
    public static object? ConvertJsonElement(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(p => p.Name, p => ConvertJsonElement(p.Value), StringComparer.OrdinalIgnoreCase),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToList(),
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            _ => element.ToString()
        };

    /// <summary>
    /// Gets the string representation of a JsonElement value.
    /// </summary>
    private static string GetStringValue(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Number => element.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "",
            JsonValueKind.Undefined => "",
            _ => element.ToString()
        };
}
