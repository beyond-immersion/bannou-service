// =============================================================================
// Quest Cache Interface
// Caches character quest data for actor behavior execution.
// =============================================================================

using BeyondImmersion.BannouService.Quest;

namespace BeyondImmersion.BannouService.Actor.Caching;

/// <summary>
/// Caches character quest data for actor behavior execution.
/// Supports TTL-based expiration and event-driven invalidation.
/// </summary>
public interface IQuestCache
{
    /// <summary>
    /// Gets active quests for a character, loading from service if not cached or expired.
    /// Returns an empty response (not null) if character has no active quests.
    /// </summary>
    /// <param name="characterId">The character ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The quest list response. Never null - returns empty quests list if none found.</returns>
    Task<ListQuestsResponse> GetActiveQuestsOrLoadAsync(Guid characterId, CancellationToken ct = default);

    /// <summary>
    /// Invalidates cached quest data for a character.
    /// Called when quest state changes (accepted, completed, failed, abandoned, progress updated).
    /// </summary>
    /// <param name="characterId">The character ID to invalidate.</param>
    void Invalidate(Guid characterId);

    /// <summary>
    /// Invalidates all cached quest data.
    /// </summary>
    void InvalidateAll();
}
