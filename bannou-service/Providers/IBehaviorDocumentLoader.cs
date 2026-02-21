// =============================================================================
// Behavior Document Loader Interface
// Abstraction for loading ABML behavior documents via provider chains.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Documents;

namespace BeyondImmersion.BannouService.Providers;

/// <summary>
/// Loads ABML behavior documents via a priority-ordered provider chain.
/// </summary>
/// <remarks>
/// <para>
/// This interface enables testing by allowing mock implementations.
/// The concrete <c>BehaviorDocumentLoader</c> in lib-actor aggregates
/// multiple <see cref="IBehaviorDocumentProvider"/> implementations.
/// </para>
/// </remarks>
public interface IBehaviorDocumentLoader
{
    /// <summary>
    /// Gets the number of registered providers.
    /// </summary>
    int ProviderCount { get; }

    /// <summary>
    /// Loads a behavior document by reference.
    /// </summary>
    /// <param name="behaviorRef">The behavior reference (asset ID, seeded name, etc.).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The loaded ABML document, or null if no provider can serve it.</returns>
    Task<AbmlDocument?> GetDocumentAsync(string behaviorRef, CancellationToken ct = default);

    /// <summary>
    /// Invalidates a specific behavior across all providers.
    /// </summary>
    /// <param name="behaviorRef">The behavior reference to invalidate.</param>
    void Invalidate(string behaviorRef);

    /// <summary>
    /// Invalidates behaviors matching a behavior ID across all providers.
    /// </summary>
    /// <param name="behaviorId">The behavior ID to match for invalidation.</param>
    void InvalidateByBehaviorId(string behaviorId);

    /// <summary>
    /// Invalidates all cached behaviors across all providers.
    /// </summary>
    void InvalidateAll();
}
