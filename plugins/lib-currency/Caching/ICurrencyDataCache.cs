// =============================================================================
// Currency Data Cache Interface
// Caches character currency balance data for actor behavior execution.
// Owned by lib-currency per service hierarchy.
// =============================================================================

namespace BeyondImmersion.BannouService.Currency.Caching;

/// <summary>
/// Caches character currency balance data for actor behavior execution.
/// Supports TTL-based expiration and event-driven invalidation.
/// </summary>
public interface ICurrencyDataCache
{
    /// <summary>
    /// Gets currency data for a character in a realm, loading from service if not cached or expired.
    /// Loads both realm-scoped and global wallets, merging balances by currency code.
    /// </summary>
    /// <param name="characterId">The character ID.</param>
    /// <param name="realmId">The realm ID for realm-scoped wallet lookup.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The cached currency data, or null if loading failed and no stale data available.</returns>
    Task<CachedCurrencyData?> GetOrLoadAsync(Guid characterId, Guid realmId, CancellationToken ct = default);

    /// <summary>
    /// Invalidates cached data for a character across all realms.
    /// Called when balance-changing events (credited, debited, transferred) are received.
    /// Events do not carry realmId, so all realm entries for the character are invalidated.
    /// </summary>
    /// <param name="characterId">The character ID to invalidate.</param>
    void Invalidate(Guid characterId);

    /// <summary>
    /// Invalidates all cached data.
    /// </summary>
    void InvalidateAll();
}
