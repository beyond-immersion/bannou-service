// =============================================================================
// Personality Cache Interface
// Caches character personality data for actor behavior execution.
// =============================================================================

using BeyondImmersion.BannouService.CharacterHistory;
using BeyondImmersion.BannouService.CharacterPersonality;

namespace BeyondImmersion.BannouService.Actor.Caching;

/// <summary>
/// Caches character personality data for actor behavior execution.
/// Supports TTL-based expiration and event-driven invalidation.
/// </summary>
public interface IPersonalityCache
{
    /// <summary>
    /// Gets personality for a character, loading from service if not cached or expired.
    /// </summary>
    /// <param name="characterId">The character ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The personality response, or null if not found.</returns>
    Task<PersonalityResponse?> GetOrLoadAsync(Guid characterId, CancellationToken ct = default);

    /// <summary>
    /// Gets combat preferences for a character, loading from service if not cached or expired.
    /// </summary>
    /// <param name="characterId">The character ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The combat preferences response, or null if not found.</returns>
    Task<CombatPreferencesResponse?> GetCombatPreferencesOrLoadAsync(Guid characterId, CancellationToken ct = default);

    /// <summary>
    /// Gets backstory for a character, loading from service if not cached or expired.
    /// </summary>
    /// <param name="characterId">The character ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The backstory response, or null if not found.</returns>
    Task<BackstoryResponse?> GetBackstoryOrLoadAsync(Guid characterId, CancellationToken ct = default);

    /// <summary>
    /// Invalidates cached personality data for a character.
    /// Called when personality.evolved event is received.
    /// </summary>
    /// <param name="characterId">The character ID to invalidate.</param>
    void Invalidate(Guid characterId);

    /// <summary>
    /// Invalidates all cached personality data.
    /// </summary>
    void InvalidateAll();
}
