// =============================================================================
// Behavior Document Cache Interface
// Caches parsed ABML documents for actor behavior execution.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Documents;

namespace BeyondImmersion.BannouService.Actor.Caching;

/// <summary>
/// Caches parsed ABML documents for actor behavior execution.
/// Handles loading from lib-asset and hot-reload invalidation.
/// </summary>
public interface IBehaviorDocumentCache
{
    /// <summary>
    /// Gets or loads a behavior document by reference.
    /// </summary>
    /// <param name="behaviorRef">The behavior reference (e.g., "behaviors/npc/standard.abml").</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The parsed ABML document.</returns>
    Task<AbmlDocument> GetOrLoadAsync(string behaviorRef, CancellationToken ct = default);

    /// <summary>
    /// Invalidates a cached behavior document.
    /// </summary>
    /// <param name="behaviorRef">The behavior reference to invalidate.</param>
    void Invalidate(string behaviorRef);

    /// <summary>
    /// Invalidates all cached behaviors matching a behavior ID.
    /// Called when behavior.updated event is received.
    /// </summary>
    /// <param name="behaviorId">The behavior ID from the updated event.</param>
    void InvalidateByBehaviorId(string behaviorId);
}
