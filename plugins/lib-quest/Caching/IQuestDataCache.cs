// =============================================================================
// Quest Data Cache Interface
// Caches character quest data.
// Owned by lib-quest per service hierarchy.
// =============================================================================

namespace BeyondImmersion.BannouService.Quest.Caching;

/// <summary>
/// Caches character quest data for actor behavior execution.
/// Supports TTL-based expiration and event-driven invalidation.
/// </summary>
public interface IQuestDataCache
{
    /// <summary>
    /// Gets active quests for a character, loading from service if not cached or expired.
    /// </summary>
    /// <param name="characterId">The character ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The quest list response (never null, may be empty).</returns>
    Task<ListQuestsResponse> GetActiveQuestsOrLoadAsync(Guid characterId, CancellationToken ct = default);

    /// <summary>
    /// Invalidates cached quest data for a character.
    /// </summary>
    /// <param name="characterId">The character ID to invalidate.</param>
    void Invalidate(Guid characterId);

    /// <summary>
    /// Invalidates all cached quest data.
    /// </summary>
    void InvalidateAll();
}
