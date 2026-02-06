// =============================================================================
// Personality Data Cache Interface
// Caches character personality and combat preferences data.
// Owned by lib-character-personality per service hierarchy.
// =============================================================================

namespace BeyondImmersion.BannouService.CharacterPersonality.Caching;

/// <summary>
/// Caches character personality and combat preferences data for actor behavior execution.
/// Supports TTL-based expiration and event-driven invalidation.
/// </summary>
public interface IPersonalityDataCache
{
    /// <summary>
    /// Gets personality for a character, loading from service if not cached or expired.
    /// </summary>
    /// <param name="characterId">The character ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The personality response, or null if not found.</returns>
    Task<PersonalityResponse?> GetOrLoadPersonalityAsync(Guid characterId, CancellationToken ct = default);

    /// <summary>
    /// Gets combat preferences for a character, loading from service if not cached or expired.
    /// </summary>
    /// <param name="characterId">The character ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The combat preferences response, or null if not found.</returns>
    Task<CombatPreferencesResponse?> GetOrLoadCombatPreferencesAsync(Guid characterId, CancellationToken ct = default);

    /// <summary>
    /// Invalidates cached data for a character.
    /// Called when personality.evolved event is received.
    /// </summary>
    /// <param name="characterId">The character ID to invalidate.</param>
    void Invalidate(Guid characterId);

    /// <summary>
    /// Invalidates all cached data.
    /// </summary>
    void InvalidateAll();
}
