// =============================================================================
// Seed Variable Provider Factory
// Creates SeedProvider instances for ABML expression evaluation.
// Registered with DI for Actor to consume via IEnumerable<IVariableProviderFactory>.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Seed.Caching;
using BeyondImmersion.BannouService.Services;
using System.Diagnostics;

namespace BeyondImmersion.BannouService.Seed.Providers;

/// <summary>
/// Factory for creating SeedProvider instances.
/// Registered with DI as IVariableProviderFactory for Actor to discover.
/// </summary>
public sealed class SeedProviderFactory : IVariableProviderFactory
{
    private readonly ISeedDataCache _cache;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>
    /// Creates a new seed provider factory.
    /// </summary>
    /// <param name="cache">Seed data cache for character seed lookups.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    public SeedProviderFactory(ISeedDataCache cache, ITelemetryProvider telemetryProvider)
    {
        _cache = cache;
        _telemetryProvider = telemetryProvider;
    }

    /// <inheritdoc/>
    public string ProviderName => VariableProviderDefinitions.Seed;

    /// <inheritdoc/>
    public async Task<IVariableProvider> CreateAsync(Guid? characterId, Guid realmId, Guid? locationId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.seed", "SeedProviderFactory.Create");

        if (!characterId.HasValue)
        {
            return SeedProvider.Empty;
        }

        var data = await _cache.GetSeedDataOrLoadAsync(characterId.Value, ct);
        return new SeedProvider(data);
    }
}
