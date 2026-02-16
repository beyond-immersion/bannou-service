// =============================================================================
// Location Data Cache Interface
// Caches pre-resolved location context data for ABML variable providers.
// Owned by lib-location per service hierarchy.
// =============================================================================

namespace BeyondImmersion.BannouService.Location.Caching;

/// <summary>
/// Caches location context data for actor behavior execution.
/// Supports TTL-based expiration with configurable cache lifetime.
/// </summary>
public interface ILocationDataCache
{
    /// <summary>
    /// Gets location context for a character, loading from service if not cached or expired.
    /// When <paramref name="locationId"/> is provided, skips the entity-location lookup
    /// (saving an API call) and uses the known location directly.
    /// </summary>
    /// <param name="characterId">The character ID to look up location context for.</param>
    /// <param name="realmId">The realm the character is in (avoids realm lookup when location is known).</param>
    /// <param name="locationId">
    /// Known location ID from perception events, or null to look up via entity-location API.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The location context data, or null if the character has no current location.</returns>
    Task<LocationContextData?> GetOrLoadLocationContextAsync(
        Guid characterId,
        Guid realmId,
        Guid? locationId,
        CancellationToken ct = default);

    /// <summary>
    /// Invalidates cached data for a character.
    /// </summary>
    /// <param name="characterId">The character ID to invalidate.</param>
    void Invalidate(Guid characterId);

    /// <summary>
    /// Invalidates all cached data.
    /// </summary>
    void InvalidateAll();
}
