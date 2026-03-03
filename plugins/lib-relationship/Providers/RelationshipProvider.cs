// =============================================================================
// Relationship Variable Provider
// Provides relationship data for ABML expressions via ${relationship.*} paths.
// Owned by lib-relationship per service hierarchy (L2).
// =============================================================================

using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using BeyondImmersion.BannouService.Relationship.Caching;
using BeyondImmersion.BannouService.Services;

namespace BeyondImmersion.BannouService.Relationship.Providers;

/// <summary>
/// Provides relationship data for ABML expressions.
/// Supports paths like ${relationship.has.PARENT}, ${relationship.count.ALLY},
/// ${relationship.total}.
/// </summary>
public sealed class RelationshipProvider : IVariableProvider
{
    /// <summary>
    /// Empty provider for non-character actors.
    /// </summary>
    public static RelationshipProvider Empty { get; } = new(CachedRelationshipData.Empty);

    private readonly CachedRelationshipData _data;

    /// <inheritdoc/>
    public string Name => VariableProviderDefinitions.Relationship;

    /// <summary>
    /// Creates a new relationship provider with the given cached data.
    /// </summary>
    /// <param name="data">The cached relationship data.</param>
    public RelationshipProvider(CachedRelationshipData data)
    {
        _data = data;
    }

    /// <inheritdoc/>
    public object? GetValue(ReadOnlySpan<string> path)
    {
        if (path.Length == 0) return GetRootValue();

        var firstSegment = path[0];

        // Handle ${relationship.has.{typeCode}} - boolean: has at least one relationship of this type
        if (firstSegment.Equals("has", StringComparison.OrdinalIgnoreCase))
        {
            if (path.Length < 2) return null;
            var code = path[1];
            return _data.CountsByTypeCode.TryGetValue(code, out var count) && count > 0;
        }

        // Handle ${relationship.count.{typeCode}} - count of relationships of this type
        if (firstSegment.Equals("count", StringComparison.OrdinalIgnoreCase))
        {
            if (path.Length < 2) return null;
            var code = path[1];
            return _data.CountsByTypeCode.TryGetValue(code, out var count) ? (double)count : 0.0;
        }

        // Handle ${relationship.total} - total active relationship count
        if (firstSegment.Equals("total", StringComparison.OrdinalIgnoreCase))
        {
            return (double)_data.TotalCount;
        }

        return null;
    }

    /// <inheritdoc/>
    public object? GetRootValue()
    {
        return new Dictionary<string, object?>
        {
            ["total"] = (double)_data.TotalCount,
        };
    }

    /// <inheritdoc/>
    public bool CanResolve(ReadOnlySpan<string> path)
    {
        if (path.Length == 0) return true;

        var firstSegment = path[0];
        return firstSegment.Equals("has", StringComparison.OrdinalIgnoreCase) ||
               firstSegment.Equals("count", StringComparison.OrdinalIgnoreCase) ||
               firstSegment.Equals("total", StringComparison.OrdinalIgnoreCase);
    }
}
