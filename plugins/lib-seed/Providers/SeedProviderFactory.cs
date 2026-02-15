// =============================================================================
// Seed Variable Provider Factory
// Creates SeedProvider instances for ABML expression evaluation.
// Registered with DI for Actor to consume via IEnumerable<IVariableProviderFactory>.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Seed.Caching;
using BeyondImmersion.BannouService.Services;

namespace BeyondImmersion.BannouService.Seed.Providers;

/// <summary>
/// Factory for creating SeedProvider instances.
/// Registered with DI as IVariableProviderFactory for Actor to discover.
/// </summary>
public sealed class SeedProviderFactory : IVariableProviderFactory
{
    private readonly ISeedDataCache _cache;

    /// <summary>
    /// Creates a new seed provider factory.
    /// </summary>
    public SeedProviderFactory(ISeedDataCache cache)
    {
        _cache = cache;
    }

    /// <inheritdoc/>
    public string ProviderName => VariableProviderDefinitions.Seed;

    /// <inheritdoc/>
    public async Task<IVariableProvider> CreateAsync(Guid? entityId, CancellationToken ct)
    {
        if (!entityId.HasValue)
        {
            return SeedProvider.Empty;
        }

        var data = await _cache.GetSeedDataOrLoadAsync(entityId.Value, ct);
        return new SeedProvider(data);
    }
}
