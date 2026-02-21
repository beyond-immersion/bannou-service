// =============================================================================
// Location Context Variable Provider Factory
// Creates LocationContextProvider instances for ABML expression evaluation.
// Registered with DI for Actor to consume via IEnumerable<IVariableProviderFactory>.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using BeyondImmersion.BannouService.Location.Caching;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;

namespace BeyondImmersion.BannouService.Location.Providers;

/// <summary>
/// Factory for creating LocationContextProvider instances.
/// Registered with DI as IVariableProviderFactory for Actor to discover.
/// </summary>
public sealed class LocationContextProviderFactory : IVariableProviderFactory
{
    private readonly ILocationDataCache _cache;

    /// <summary>
    /// Creates a new location context provider factory.
    /// </summary>
    /// <param name="cache">The location data cache for loading context data.</param>
    public LocationContextProviderFactory(ILocationDataCache cache)
    {
        _cache = cache;
    }

    /// <inheritdoc/>
    public string ProviderName => VariableProviderDefinitions.Location;

    /// <inheritdoc/>
    public async Task<IVariableProvider> CreateAsync(Guid? characterId, Guid realmId, Guid? locationId, CancellationToken ct)
    {
        if (!characterId.HasValue)
        {
            return LocationContextProvider.Empty;
        }

        var context = await _cache.GetOrLoadLocationContextAsync(characterId.Value, realmId, locationId, ct);
        return new LocationContextProvider(context);
    }
}
