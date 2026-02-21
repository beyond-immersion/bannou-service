// =============================================================================
// Backstory Variable Provider
// Provides backstory data for ABML expressions via ${backstory.*} paths.
// Owned by lib-character-history per service hierarchy (L3).
// =============================================================================

using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using BeyondImmersion.BannouService.Services;

namespace BeyondImmersion.BannouService.CharacterHistory.Providers;

/// <summary>
/// Provides backstory data for ABML expressions.
/// Supports paths like ${backstory.origin}, ${backstory.fear.value}, ${backstory.elements.TRAUMA}, etc.
/// </summary>
public sealed class BackstoryProvider : IVariableProvider
{
    /// <summary>
    /// Empty provider for non-character actors.
    /// </summary>
    public static BackstoryProvider Empty { get; } = new(null);

    private readonly Dictionary<string, BackstoryElement> _elementsByType;
    private readonly Dictionary<string, List<BackstoryElement>> _elementGroupsByType;
    private readonly List<BackstoryElement> _allElements;

    /// <inheritdoc/>
    public string Name => VariableProviderDefinitions.Backstory;

    /// <summary>
    /// Creates a new backstory provider.
    /// </summary>
    /// <param name="backstory">The backstory response, or null for empty provider.</param>
    public BackstoryProvider(BackstoryResponse? backstory)
    {
        _elementsByType = new Dictionary<string, BackstoryElement>(StringComparer.OrdinalIgnoreCase);
        _elementGroupsByType = new Dictionary<string, List<BackstoryElement>>(StringComparer.OrdinalIgnoreCase);
        _allElements = backstory?.Elements?.ToList() ?? new List<BackstoryElement>();

        if (backstory?.Elements != null)
        {
            foreach (var element in backstory.Elements)
            {
                var typeName = element.ElementType.ToString();

                // Store first element of each type for direct access
                if (!_elementsByType.ContainsKey(typeName))
                {
                    _elementsByType[typeName] = element;
                }

                // Also store lowercase version
                var lowerTypeName = typeName.ToLowerInvariant();
                if (!_elementsByType.ContainsKey(lowerTypeName))
                {
                    _elementsByType[lowerTypeName] = element;
                }

                // Group all elements by type
                if (!_elementGroupsByType.TryGetValue(typeName, out var group))
                {
                    group = new List<BackstoryElement>();
                    _elementGroupsByType[typeName] = group;
                    _elementGroupsByType[lowerTypeName] = group;
                }
                group.Add(element);
            }
        }
    }

    /// <inheritdoc/>
    public object? GetValue(ReadOnlySpan<string> path)
    {
        if (path.Length == 0) return GetRootValue();

        var firstSegment = path[0];

        // Handle ${backstory.elements} - returns all elements
        if (firstSegment.Equals("elements", StringComparison.OrdinalIgnoreCase))
        {
            if (path.Length == 1) return _allElements;

            // Handle ${backstory.elements.TRAUMA} - returns all elements of that type
            var typeName = path[1];
            if (_elementGroupsByType.TryGetValue(typeName, out var group))
            {
                return group;
            }
            return Array.Empty<BackstoryElement>();
        }

        // Handle direct type access: ${backstory.origin}, ${backstory.fear}, etc.
        if (_elementsByType.TryGetValue(firstSegment, out var element))
        {
            if (path.Length == 1)
            {
                // ${backstory.origin} returns the value directly for simple usage
                return element.Value;
            }

            // Handle property access: ${backstory.origin.key}, ${backstory.origin.value}, etc.
            var property = path[1];
            return GetElementProperty(element, property);
        }

        return null;
    }

    /// <summary>
    /// Gets a specific property from a backstory element.
    /// </summary>
    private static object? GetElementProperty(BackstoryElement element, string property)
    {
        return property.ToLowerInvariant() switch
        {
            "key" => element.Key,
            "value" => element.Value,
            "strength" => element.Strength,
            "type" or "elementtype" => element.ElementType.ToString().ToLowerInvariant(),
            "relatedentityid" => element.RelatedEntityId?.ToString(),
            "relatedentitytype" => element.RelatedEntityType,
            _ => null
        };
    }

    /// <inheritdoc/>
    public object? GetRootValue()
    {
        // Return a dictionary representation of all backstory elements grouped by type
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var kvp in _elementsByType)
        {
            // Only add lowercase keys to avoid duplicates
            if (kvp.Key == kvp.Key.ToLowerInvariant())
            {
                result[kvp.Key] = kvp.Value.Value;
            }
        }

        result["elements"] = _allElements;
        return result;
    }

    /// <inheritdoc/>
    public bool CanResolve(ReadOnlySpan<string> path)
    {
        if (path.Length == 0) return true;

        var firstSegment = path[0];

        return firstSegment.Equals("elements", StringComparison.OrdinalIgnoreCase) ||
                _elementsByType.ContainsKey(firstSegment);
    }
}
