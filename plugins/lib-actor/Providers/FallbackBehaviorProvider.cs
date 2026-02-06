// =============================================================================
// Fallback Behavior Provider
// Last-resort provider that logs when no other provider can serve a behavior.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorCompiler.Documents;
using BeyondImmersion.BannouService.Providers;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Actor.Providers;

/// <summary>
/// Fallback behavior provider that logs when no other provider can serve a behavior.
/// </summary>
/// <remarks>
/// <para>
/// This is the last provider in the chain (Priority 0). It always claims it can
/// provide any behavior, but always returns null after logging a warning. This
/// ensures graceful degradation when a behavior cannot be found.
/// </para>
/// <para>
/// Standard priority levels:
/// <list type="bullet">
///   <item>100 - DynamicBehaviorProvider (lib-puppetmaster): Asset-based behaviors</item>
///   <item>50 - SeededBehaviorProvider (lib-actor): Embedded behaviors</item>
///   <item>0 - FallbackBehaviorProvider (lib-actor): Graceful degradation</item>
/// </list>
/// </para>
/// </remarks>
public sealed class FallbackBehaviorProvider : IBehaviorDocumentProvider
{
    private readonly ILogger<FallbackBehaviorProvider> _logger;

    /// <summary>
    /// Creates a new fallback behavior provider.
    /// </summary>
    /// <param name="logger">Logger instance.</param>
    public FallbackBehaviorProvider(ILogger<FallbackBehaviorProvider> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    /// <remarks>Priority 0 = lowest, checked last after all other providers.</remarks>
    public int Priority => 0;

    /// <inheritdoc />
    /// <remarks>
    /// Always returns true. This provider is the catch-all for unhandled behavior refs.
    /// </remarks>
    public bool CanProvide(string behaviorRef)
    {
        return true;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Always returns null after logging a warning. This is intentional - the fallback
    /// provider exists to log when no other provider can serve a behavior, enabling
    /// operators to diagnose missing behavior configurations.
    /// </remarks>
    public Task<AbmlDocument?> GetDocumentAsync(string behaviorRef, CancellationToken ct)
    {
        _logger.LogWarning(
            "No provider could load behavior {BehaviorRef}. " +
            "Ensure the behavior exists in asset storage or is registered as a seeded behavior.",
            behaviorRef);

        return Task.FromResult<AbmlDocument?>(null);
    }

    /// <inheritdoc />
    /// <remarks>No-op: fallback provider has no cache to invalidate.</remarks>
    public void Invalidate(string behaviorRef)
    {
        // No-op: fallback provider has no cache
    }

    /// <inheritdoc />
    /// <remarks>No-op: fallback provider has no cache to invalidate.</remarks>
    public void InvalidateAll()
    {
        // No-op: fallback provider has no cache
    }
}
