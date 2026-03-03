// =============================================================================
// Relationship Data Cache Interface
// Defines the contract for caching character relationship data.
// Owned by lib-relationship per service hierarchy (L2).
// =============================================================================

namespace BeyondImmersion.BannouService.Relationship.Caching;

/// <summary>
/// Cache interface for character relationship data used by the variable provider.
/// Caches relationship counts by type code for ABML expression evaluation.
/// </summary>
public interface IRelationshipDataCache
{
    /// <summary>
    /// Gets or loads relationship data for a character.
    /// Returns cached data if fresh, otherwise loads from the Relationship service.
    /// </summary>
    /// <param name="characterId">The character to load relationship data for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Cached relationship data, or null if loading failed.</returns>
    Task<CachedRelationshipData?> GetOrLoadAsync(Guid characterId, CancellationToken ct);

    /// <summary>
    /// Invalidates cached data for a specific character.
    /// Called when relationship events indicate stale data.
    /// </summary>
    /// <param name="characterId">The character whose cache entry to invalidate.</param>
    void Invalidate(Guid characterId);

    /// <summary>
    /// Invalidates all cached relationship data.
    /// </summary>
    void InvalidateAll();
}
