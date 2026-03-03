// =============================================================================
// Inventory Data Cache Interface
// Caches character inventory data for actor behavior execution.
// Owned by lib-inventory per service hierarchy.
// =============================================================================

namespace BeyondImmersion.BannouService.Inventory.Caching;

/// <summary>
/// Caches character inventory data for actor behavior execution.
/// Supports TTL-based expiration and event-driven invalidation.
/// </summary>
public interface IInventoryDataCache
{
    /// <summary>
    /// Gets inventory data for a character, loading from service if not cached or expired.
    /// Loads all containers and items, aggregating counts by template code.
    /// </summary>
    /// <param name="characterId">The character ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The cached inventory data, or null if loading failed and no stale data available.</returns>
    Task<CachedInventoryData?> GetOrLoadAsync(Guid characterId, CancellationToken ct = default);

    /// <summary>
    /// Invalidates cached data for a character.
    /// Called when inventory-changing events are received.
    /// </summary>
    /// <param name="characterId">The character ID to invalidate.</param>
    void Invalidate(Guid characterId);

    /// <summary>
    /// Invalidates all cached data.
    /// </summary>
    void InvalidateAll();
}
