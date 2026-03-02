using BeyondImmersion.Bannou.BehaviorCompiler.Documents;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Puppetmaster.Caching;
using BeyondImmersion.BannouService.Services;

namespace BeyondImmersion.BannouService.Puppetmaster.Providers;

/// <summary>
/// Behavior document provider that loads behaviors from the asset service via cache.
/// Priority 100 (highest) - checked before seeded and fallback providers.
/// </summary>
public sealed class DynamicBehaviorProvider : IBehaviorDocumentProvider
{
    private readonly IBehaviorDocumentCache _cache;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>
    /// Creates a new dynamic behavior provider.
    /// </summary>
    /// <param name="cache">The behavior document cache.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    public DynamicBehaviorProvider(IBehaviorDocumentCache cache, ITelemetryProvider telemetryProvider)
    {
        _cache = cache;
        _telemetryProvider = telemetryProvider;
    }

    /// <inheritdoc />
    public int Priority => 100;

    /// <inheritdoc />
    public bool CanProvide(string behaviorRef)
    {
        // Can provide any behavior that looks like a valid asset reference (GUID format)
        return !string.IsNullOrWhiteSpace(behaviorRef)
            && Guid.TryParse(behaviorRef, out _);
    }

    /// <inheritdoc />
    public async Task<AbmlDocument?> GetDocumentAsync(string behaviorRef, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.puppetmaster", "DynamicBehaviorProvider.GetDocumentAsync");
        return await _cache.GetOrLoadAsync(behaviorRef, ct);
    }

    /// <inheritdoc />
    public void Invalidate(string behaviorRef)
    {
        _cache.Invalidate(behaviorRef);
    }

    /// <inheritdoc />
    public void InvalidateAll()
    {
        _cache.InvalidateAll();
    }
}
