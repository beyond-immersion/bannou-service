// =============================================================================
// Location Context Variable Provider
// Provides location data for ABML expressions via ${location.*} paths.
// Owned by lib-location per service hierarchy (L2).
// =============================================================================

using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;

namespace BeyondImmersion.BannouService.Location.Providers;

/// <summary>
/// Provides location context data for ABML expressions.
/// Supports paths like ${location.zone}, ${location.type}, ${location.entity_count}, etc.
/// </summary>
public sealed class LocationContextProvider : IVariableProvider
{
    /// <summary>
    /// Empty provider for non-character actors or characters with no current location.
    /// </summary>
    public static LocationContextProvider Empty { get; } = new(null);

    private readonly LocationContextData? _data;

    /// <inheritdoc/>
    public string Name => "location";

    /// <summary>
    /// Creates a new location context provider with the given context data.
    /// </summary>
    /// <param name="data">The location context data, or null for empty provider.</param>
    public LocationContextProvider(LocationContextData? data)
    {
        _data = data;
    }

    /// <inheritdoc/>
    public object? GetValue(ReadOnlySpan<string> path)
    {
        if (path.Length == 0) return GetRootValue();
        if (_data is null) return null;

        var segment = path[0];

        if (segment.Equals("zone", StringComparison.OrdinalIgnoreCase))
            return _data.Zone;

        if (segment.Equals("name", StringComparison.OrdinalIgnoreCase))
            return _data.Name;

        if (segment.Equals("region", StringComparison.OrdinalIgnoreCase))
            return _data.Region;

        if (segment.Equals("type", StringComparison.OrdinalIgnoreCase))
            return _data.Type.ToString();

        if (segment.Equals("depth", StringComparison.OrdinalIgnoreCase))
            return _data.Depth;

        if (segment.Equals("realm", StringComparison.OrdinalIgnoreCase))
            return _data.Realm;

        if (segment.Equals("nearby_pois", StringComparison.OrdinalIgnoreCase))
            return _data.NearbyPois;

        if (segment.Equals("entity_count", StringComparison.OrdinalIgnoreCase))
            return _data.EntityCount;

        return null;
    }

    /// <inheritdoc/>
    public object? GetRootValue()
    {
        if (_data is null) return null;

        return new Dictionary<string, object?>
        {
            ["zone"] = _data.Zone,
            ["name"] = _data.Name,
            ["region"] = _data.Region,
            ["type"] = _data.Type.ToString(),
            ["depth"] = _data.Depth,
            ["realm"] = _data.Realm,
            ["nearby_pois"] = _data.NearbyPois,
            ["entity_count"] = _data.EntityCount
        };
    }

    /// <inheritdoc/>
    public bool CanResolve(ReadOnlySpan<string> path)
    {
        if (path.Length == 0) return true;

        var segment = path[0];
        return segment.Equals("zone", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("name", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("region", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("type", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("depth", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("realm", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("nearby_pois", StringComparison.OrdinalIgnoreCase) ||
                segment.Equals("entity_count", StringComparison.OrdinalIgnoreCase);
    }
}
