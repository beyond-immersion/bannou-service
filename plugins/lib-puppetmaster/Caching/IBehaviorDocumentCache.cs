using BeyondImmersion.Bannou.BehaviorCompiler.Documents;

namespace BeyondImmersion.BannouService.Puppetmaster.Caching;

/// <summary>
/// Interface for behavior document cache operations.
/// </summary>
public interface IBehaviorDocumentCache
{
    /// <summary>
    /// Gets the number of cached behavior documents.
    /// </summary>
    int CachedCount { get; }

    /// <summary>
    /// Gets or loads a behavior document by reference.
    /// </summary>
    /// <param name="behaviorRef">The behavior reference (asset ID).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The loaded document, or null if loading fails.</returns>
    Task<AbmlDocument?> GetOrLoadAsync(string behaviorRef, CancellationToken ct);

    /// <summary>
    /// Invalidates a specific cached behavior.
    /// </summary>
    /// <param name="behaviorRef">The behavior reference to invalidate.</param>
    /// <returns>True if the behavior was cached and removed.</returns>
    bool Invalidate(string behaviorRef);

    /// <summary>
    /// Invalidates all cached behaviors.
    /// </summary>
    /// <returns>Number of behaviors invalidated.</returns>
    int InvalidateAll();
}
