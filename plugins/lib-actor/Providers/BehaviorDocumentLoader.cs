// =============================================================================
// Behavior Document Loader
// Loads ABML documents via a priority-ordered provider chain.
// Replaces the old IBehaviorDocumentCache to eliminate L3 dependencies.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Documents;
using BeyondImmersion.BannouService.Providers;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Actor.Providers;

/// <summary>
/// Loads ABML behavior documents via a priority-ordered provider chain.
/// </summary>
/// <remarks>
/// <para>
/// This replaces the old <c>IBehaviorDocumentCache</c> which had an illegal L3 dependency
/// on <c>IAssetClient</c>. The provider chain pattern enables proper layer separation:
/// </para>
/// <list type="bullet">
///   <item>lib-puppetmaster (L4) provides <c>DynamicBehaviorProvider</c> (Priority 100) for asset-based behaviors</item>
///   <item>lib-actor (L2) provides <c>SeededBehaviorProvider</c> (Priority 50) for embedded behaviors</item>
///   <item>lib-actor (L2) provides <c>FallbackBehaviorProvider</c> (Priority 0) for graceful degradation</item>
/// </list>
/// <para>
/// Providers are discovered via DI (<c>IEnumerable&lt;IBehaviorDocumentProvider&gt;</c>) and
/// sorted by priority descending. The first provider that can serve a behavior reference wins.
/// </para>
/// </remarks>
public sealed class BehaviorDocumentLoader
{
    private readonly IReadOnlyList<IBehaviorDocumentProvider> _providers;
    private readonly ILogger<BehaviorDocumentLoader> _logger;

    /// <summary>
    /// Creates a new behavior document loader.
    /// </summary>
    /// <param name="providers">Behavior document providers discovered via DI.</param>
    /// <param name="logger">Logger instance.</param>
    public BehaviorDocumentLoader(
        IEnumerable<IBehaviorDocumentProvider> providers,
        ILogger<BehaviorDocumentLoader> logger)
    {
        // Sort providers by priority descending (highest priority first)
        _providers = providers
            .OrderByDescending(p => p.Priority)
            .ToList();
        _logger = logger;

        _logger.LogDebug(
            "Initialized behavior document loader with {Count} providers: {Providers}",
            _providers.Count,
            string.Join(", ", _providers.Select(p => $"{p.GetType().Name}(P{p.Priority})")));
    }

    /// <summary>
    /// Gets the number of registered providers.
    /// </summary>
    public int ProviderCount => _providers.Count;

    /// <summary>
    /// Loads a behavior document by reference.
    /// </summary>
    /// <param name="behaviorRef">The behavior reference (asset ID, seeded name, etc.).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The loaded ABML document, or null if no provider can serve it.</returns>
    /// <remarks>
    /// Providers are tried in priority order. The first provider that returns
    /// <c>CanProvide(behaviorRef) == true</c> AND successfully loads the document wins.
    /// If a provider claims it can provide but fails to load, we continue to the next provider.
    /// </remarks>
    public async Task<AbmlDocument?> GetDocumentAsync(string behaviorRef, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(behaviorRef))
        {
            _logger.LogWarning("Attempted to load behavior with null or empty reference");
            return null;
        }

        foreach (var provider in _providers)
        {
            if (!provider.CanProvide(behaviorRef))
            {
                continue;
            }

            _logger.LogDebug(
                "Provider {Provider} can serve behavior {BehaviorRef}",
                provider.GetType().Name,
                behaviorRef);

            try
            {
                var document = await provider.GetDocumentAsync(behaviorRef, ct);
                if (document != null)
                {
                    _logger.LogDebug(
                        "Loaded behavior {BehaviorRef} via {Provider}",
                        behaviorRef,
                        provider.GetType().Name);
                    return document;
                }

                _logger.LogDebug(
                    "Provider {Provider} returned null for {BehaviorRef}, trying next provider",
                    provider.GetType().Name,
                    behaviorRef);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Provider {Provider} failed to load {BehaviorRef}, trying next provider",
                    provider.GetType().Name,
                    behaviorRef);
            }
        }

        _logger.LogWarning("No provider could load behavior {BehaviorRef}", behaviorRef);
        return null;
    }

    /// <summary>
    /// Invalidates a specific behavior across all providers.
    /// </summary>
    /// <param name="behaviorRef">The behavior reference to invalidate.</param>
    public void Invalidate(string behaviorRef)
    {
        if (string.IsNullOrWhiteSpace(behaviorRef))
        {
            return;
        }

        foreach (var provider in _providers)
        {
            try
            {
                provider.Invalidate(behaviorRef);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Provider {Provider} failed to invalidate {BehaviorRef}",
                    provider.GetType().Name,
                    behaviorRef);
            }
        }

        _logger.LogDebug("Invalidated behavior {BehaviorRef} across all providers", behaviorRef);
    }

    /// <summary>
    /// Invalidates behaviors matching a behavior ID across all providers.
    /// </summary>
    /// <param name="behaviorId">The behavior ID to match for invalidation.</param>
    /// <remarks>
    /// This is used for hot-reload when a behavior is updated. The behavior ID
    /// may match multiple cached entries (e.g., same behavior loaded via different refs).
    /// Each provider interprets this as appropriate for its caching strategy.
    /// </remarks>
    public void InvalidateByBehaviorId(string behaviorId)
    {
        if (string.IsNullOrWhiteSpace(behaviorId))
        {
            return;
        }

        // Invalidate by treating behaviorId as a ref (exact match)
        // Providers that support more sophisticated matching can override
        Invalidate(behaviorId);
    }

    /// <summary>
    /// Invalidates all cached behaviors across all providers.
    /// </summary>
    public void InvalidateAll()
    {
        foreach (var provider in _providers)
        {
            try
            {
                provider.InvalidateAll();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Provider {Provider} failed to invalidate all",
                    provider.GetType().Name);
            }
        }

        _logger.LogInformation("Invalidated all behaviors across all providers");
    }
}
