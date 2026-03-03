// =============================================================================
// Cached Relationship Data
// Immutable snapshot of a character's relationship data for provider consumption.
// Owned by lib-relationship per service hierarchy (L2).
// =============================================================================

namespace BeyondImmersion.BannouService.Relationship.Caching;

/// <summary>
/// Immutable snapshot of a character's relationship data for ABML variable evaluation.
/// Contains relationship counts aggregated by type code.
/// </summary>
public sealed record CachedRelationshipData
{
    /// <summary>
    /// Empty instance for characters with no relationships.
    /// </summary>
    public static CachedRelationshipData Empty { get; } = new()
    {
        CountsByTypeCode = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
        TotalCount = 0,
    };

    /// <summary>
    /// Number of active relationships per relationship type code.
    /// Key is the relationship type code (case-insensitive), value is the count.
    /// </summary>
    public required IReadOnlyDictionary<string, int> CountsByTypeCode { get; init; }

    /// <summary>
    /// Total number of active relationships for this character.
    /// </summary>
    public required int TotalCount { get; init; }
}
