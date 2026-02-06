// =============================================================================
// Resource Archive Provider
// Exposes resource snapshot data as an IVariableProvider for ABML expressions.
// =============================================================================

using System.Text.Json;
using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using BeyondImmersion.BannouService.Puppetmaster.Caching;

namespace BeyondImmersion.BannouService.Puppetmaster.Providers;

/// <summary>
/// Variable provider that exposes resource snapshot data for ABML expression evaluation.
/// </summary>
/// <remarks>
/// <para>
/// This provider enables Event Brain actors to access arbitrary resource data via
/// the standard ABML variable syntax:
/// <code>
/// ${candidate.personality.aggression}  # Access personality.aggression from snapshot
/// ${candidate.history.participations}  # Access history.participations
/// </code>
/// </para>
/// <para>
/// <b>Namespace Mapping</b>: Each snapshot entry's sourceType is mapped to a
/// short namespace for convenience:
/// <list type="bullet">
///   <item>"character-base" → "base"</item>
///   <item>"character-personality" → "personality"</item>
///   <item>"character-history" → "history"</item>
///   <item>"character-encounter" → "encounters"</item>
///   <item>"storyline" → "storyline" (identity)</item>
///   <item>"quest" → "quest" (identity)</item>
/// </list>
/// Unknown source types use the full sourceType as the namespace, or strip
/// the "character-" prefix if present.
/// </para>
/// <para>
/// <b>Path Navigation</b>: The provider navigates JSON data dynamically.
/// Paths like "personality.traits.AGGRESSION" are resolved by parsing
/// the decompressed JSON and navigating properties.
/// </para>
/// </remarks>
public sealed class ResourceArchiveProvider : IVariableProvider
{
    private static readonly IReadOnlyDictionary<string, string> NamespaceAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["character-base"] = "base",
            ["character-personality"] = "personality",
            ["character-history"] = "history",
            ["character-encounter"] = "encounters"
            // Note: "storyline" and "quest" use identity mapping (sourceType == namespace)
            // via the fallback logic in GetNamespace(), not explicit aliases
        };

    private readonly string _name;
    private readonly ResourceSnapshotData _snapshot;
    private readonly Dictionary<string, JsonDocument> _parsedEntries;

    /// <summary>
    /// Creates a new resource archive provider.
    /// </summary>
    /// <param name="name">The provider name (used in ABML as ${name.path}).</param>
    /// <param name="snapshot">The snapshot data to expose.</param>
    public ResourceArchiveProvider(string name, ResourceSnapshotData snapshot)
    {
        _name = name;
        _snapshot = snapshot;
        _parsedEntries = new Dictionary<string, JsonDocument>(StringComparer.OrdinalIgnoreCase);

        // Pre-parse all entries for efficient access
        foreach (var (sourceType, entry) in snapshot.Entries)
        {
            try
            {
                var ns = GetNamespace(sourceType);
                var doc = JsonDocument.Parse(entry.Data);
                _parsedEntries[ns] = doc;
            }
            catch (JsonException)
            {
                // Skip entries that fail to parse
            }
        }
    }

    /// <inheritdoc />
    public string Name => _name;

    /// <inheritdoc />
    public object? GetRootValue()
    {
        // Return a dictionary of available namespaces
        return _parsedEntries.Keys.ToList();
    }

    /// <inheritdoc />
    public object? GetValue(ReadOnlySpan<string> path)
    {
        if (path.Length == 0)
        {
            return GetRootValue();
        }

        // First segment is the namespace (e.g., "personality")
        var ns = path[0];
        if (!_parsedEntries.TryGetValue(ns, out var doc))
        {
            return null;
        }

        if (path.Length == 1)
        {
            return ConvertElement(doc.RootElement);
        }

        // Navigate the remaining path within the JSON document
        return NavigateJson(doc.RootElement, path[1..]);
    }

    /// <inheritdoc />
    public bool CanResolve(ReadOnlySpan<string> path)
    {
        if (path.Length == 0)
        {
            return true;
        }

        return _parsedEntries.ContainsKey(path[0]);
    }

    /// <summary>
    /// Creates an empty provider for graceful degradation.
    /// </summary>
    /// <param name="name">The provider name.</param>
    /// <returns>An empty provider that returns null for all paths.</returns>
    public static ResourceArchiveProvider Empty(string name)
    {
        return new ResourceArchiveProvider(
            name,
            new ResourceSnapshotData(
                "empty",
                Guid.Empty,
                new Dictionary<string, ResourceSnapshotEntry>(),
                DateTimeOffset.UtcNow));
    }

    private static string GetNamespace(string sourceType)
    {
        if (NamespaceAliases.TryGetValue(sourceType, out var alias))
        {
            return alias;
        }

        // Use the sourceType itself, stripping common prefixes
        if (sourceType.StartsWith("character-", StringComparison.OrdinalIgnoreCase))
        {
            return sourceType["character-".Length..];
        }

        return sourceType;
    }

    private static object? NavigateJson(JsonElement element, ReadOnlySpan<string> path)
    {
        var current = element;

        foreach (var segment in path)
        {
            switch (current.ValueKind)
            {
                case JsonValueKind.Object:
                    if (!current.TryGetProperty(segment, out var prop))
                    {
                        // Try case-insensitive lookup
                        var found = false;
                        foreach (var p in current.EnumerateObject())
                        {
                            if (string.Equals(p.Name, segment, StringComparison.OrdinalIgnoreCase))
                            {
                                current = p.Value;
                                found = true;
                                break;
                            }
                        }
                        if (!found) return null;
                    }
                    else
                    {
                        current = prop;
                    }
                    break;

                case JsonValueKind.Array:
                    if (int.TryParse(segment, out var index))
                    {
                        var arr = current.EnumerateArray().ToList();
                        if (index >= 0 && index < arr.Count)
                        {
                            current = arr[index];
                        }
                        else
                        {
                            return null;
                        }
                    }
                    else
                    {
                        return null;
                    }
                    break;

                default:
                    return null;
            }
        }

        return ConvertElement(current);
    }

    private static object? ConvertElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            // GetString() returns string? but cannot return null when ValueKind is String;
            // coalesce satisfies compiler's nullable analysis (will never execute)
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number when element.TryGetInt64(out var l) => l,
            JsonValueKind.Number when element.TryGetDouble(out var d) => d,
            JsonValueKind.Array => ConvertArray(element),
            JsonValueKind.Object => ConvertObject(element),
            _ => null
        };
    }

    private static List<object?> ConvertArray(JsonElement element)
    {
        var list = new List<object?>();
        foreach (var item in element.EnumerateArray())
        {
            list.Add(ConvertElement(item));
        }
        return list;
    }

    private static Dictionary<string, object?> ConvertObject(JsonElement element)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in element.EnumerateObject())
        {
            dict[prop.Name] = ConvertElement(prop.Value);
        }
        return dict;
    }
}
