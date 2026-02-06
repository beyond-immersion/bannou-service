// =============================================================================
// Encounters Variable Provider Factory
// Creates EncountersProvider instances for ABML expression evaluation.
// Registered with DI for Actor to consume via IEnumerable<IVariableProviderFactory>.
// =============================================================================

using BeyondImmersion.BannouService.Abml.Expressions;
using BeyondImmersion.BannouService.CharacterEncounter.Caching;
using BeyondImmersion.BannouService.Providers;

namespace BeyondImmersion.BannouService.CharacterEncounter.Providers;

/// <summary>
/// Factory for creating EncountersProvider instances.
/// Registered with DI as IVariableProviderFactory for Actor to discover.
/// </summary>
public sealed class EncountersProviderFactory : IVariableProviderFactory
{
    private readonly IEncounterDataCache _cache;

    /// <summary>
    /// Creates a new encounters provider factory.
    /// </summary>
    public EncountersProviderFactory(IEncounterDataCache cache)
    {
        _cache = cache;
    }

    /// <inheritdoc/>
    public string ProviderName => "encounters";

    /// <inheritdoc/>
    public async Task<IVariableProvider> CreateAsync(Guid? entityId, CancellationToken ct)
    {
        if (!entityId.HasValue)
        {
            return EncountersProvider.Empty;
        }

        // Load basic encounter list
        // Note: sentiment, hasMet, and pairEncounters are loaded on-demand via the cache
        // For now, we just load the basic encounter list for the provider
        var encounters = await _cache.GetEncountersOrLoadAsync(entityId.Value, ct);
        return new EncountersProvider(encounters);
    }
}
