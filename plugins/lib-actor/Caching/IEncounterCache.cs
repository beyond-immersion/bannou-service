// =============================================================================
// Encounter Cache Interface
// Caches character encounter data for actor behavior execution.
// =============================================================================

using BeyondImmersion.BannouService.CharacterEncounter;

namespace BeyondImmersion.BannouService.Actor.Caching;

/// <summary>
/// Caches character encounter data for actor behavior execution.
/// Supports TTL-based expiration and event-driven invalidation.
/// </summary>
public interface IEncounterCache
{
    /// <summary>
    /// Gets encounters for a character, loading from service if not cached or expired.
    /// </summary>
    /// <param name="characterId">The character ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The encounter list response, or null if not found.</returns>
    Task<EncounterListResponse?> GetEncountersOrLoadAsync(Guid characterId, CancellationToken ct = default);

    /// <summary>
    /// Gets sentiment toward a specific target character, loading from service if not cached or expired.
    /// </summary>
    /// <param name="characterId">The character ID.</param>
    /// <param name="targetCharacterId">The target character ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The sentiment response, or null if not found.</returns>
    Task<SentimentResponse?> GetSentimentOrLoadAsync(Guid characterId, Guid targetCharacterId, CancellationToken ct = default);

    /// <summary>
    /// Checks if two characters have met, loading from service if not cached or expired.
    /// </summary>
    /// <param name="characterId">The first character ID.</param>
    /// <param name="targetCharacterId">The second character ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The has-met response, or null on error.</returns>
    Task<HasMetResponse?> HasMetOrLoadAsync(Guid characterId, Guid targetCharacterId, CancellationToken ct = default);

    /// <summary>
    /// Gets encounters between two characters, loading from service if not cached or expired.
    /// </summary>
    /// <param name="characterIdA">The first character ID.</param>
    /// <param name="characterIdB">The second character ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The encounter list response, or null if not found.</returns>
    Task<EncounterListResponse?> GetEncountersBetweenOrLoadAsync(Guid characterIdA, Guid characterIdB, CancellationToken ct = default);

    /// <summary>
    /// Invalidates cached encounter data for a character.
    /// Called when encounter events are received.
    /// </summary>
    /// <param name="characterId">The character ID to invalidate.</param>
    void Invalidate(Guid characterId);

    /// <summary>
    /// Invalidates all cached encounter data.
    /// </summary>
    void InvalidateAll();
}
