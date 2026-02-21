// =============================================================================
// Backstory Cache Interface
// Caches character backstory data.
// Owned by lib-character-history per service hierarchy.
// =============================================================================

namespace BeyondImmersion.BannouService.CharacterHistory.Caching;

/// <summary>
/// Caches character backstory data for actor behavior execution.
/// Supports TTL-based expiration and event-driven invalidation.
/// </summary>
public interface IBackstoryCache
{
    /// <summary>
    /// Gets backstory for a character, loading from service if not cached or expired.
    /// </summary>
    /// <param name="characterId">The character ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The backstory response, or null if not found.</returns>
    Task<BackstoryResponse?> GetOrLoadAsync(Guid characterId, CancellationToken ct = default);

    /// <summary>
    /// Invalidates cached backstory data for a character.
    /// </summary>
    /// <param name="characterId">The character ID to invalidate.</param>
    void Invalidate(Guid characterId);

    /// <summary>
    /// Invalidates all cached backstory data.
    /// </summary>
    void InvalidateAll();
}
