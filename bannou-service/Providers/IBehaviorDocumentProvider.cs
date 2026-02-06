// =============================================================================
// Behavior Document Provider Interface
// Abstraction for loading ABML behavior documents from various sources.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Documents;

namespace BeyondImmersion.BannouService.Providers;

/// <summary>
/// Provider for loading ABML behavior documents.
/// </summary>
/// <remarks>
/// <para>
/// Multiple providers can be registered, each with a different priority.
/// When loading a document, providers are checked in priority order (highest first).
/// The first provider that can serve the requested behavior reference wins.
/// </para>
/// <para>
/// Standard priority levels:
/// <list type="bullet">
/// <item>100 - Dynamic/runtime behaviors (lib-puppetmaster)</item>
/// <item>50 - Seeded/pre-defined behaviors (lib-actor)</item>
/// <item>0 - Fallback/stub behaviors</item>
/// </list>
/// </para>
/// </remarks>
public interface IBehaviorDocumentProvider
{
    /// <summary>
    /// Provider priority. Higher values are checked first.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Returns true if this provider can serve the given behavior reference.
    /// </summary>
    /// <param name="behaviorRef">The behavior reference to check.</param>
    /// <returns>True if this provider can load the behavior.</returns>
    bool CanProvide(string behaviorRef);

    /// <summary>
    /// Loads an ABML document by reference.
    /// </summary>
    /// <param name="behaviorRef">The behavior reference to load.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The loaded document, or null if not found.</returns>
    Task<AbmlDocument?> GetDocumentAsync(string behaviorRef, CancellationToken ct);

    /// <summary>
    /// Invalidates a cached document (if caching is implemented).
    /// </summary>
    /// <param name="behaviorRef">The behavior reference to invalidate.</param>
    void Invalidate(string behaviorRef);

    /// <summary>
    /// Invalidates all cached documents.
    /// </summary>
    void InvalidateAll();
}
