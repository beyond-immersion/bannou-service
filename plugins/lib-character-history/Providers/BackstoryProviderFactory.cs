// =============================================================================
// Backstory Variable Provider Factory
// Creates BackstoryProvider instances for ABML expression evaluation.
// Registered with DI for Actor to consume via IEnumerable<IVariableProviderFactory>.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using BeyondImmersion.BannouService.CharacterHistory.Caching;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;

namespace BeyondImmersion.BannouService.CharacterHistory.Providers;

/// <summary>
/// Factory for creating BackstoryProvider instances.
/// Registered with DI as IVariableProviderFactory for Actor to discover.
/// </summary>
public sealed class BackstoryProviderFactory : IVariableProviderFactory
{
    private readonly IBackstoryCache _cache;

    /// <summary>
    /// Creates a new backstory provider factory.
    /// </summary>
    public BackstoryProviderFactory(IBackstoryCache cache)
    {
        _cache = cache;
    }

    /// <inheritdoc/>
    public string ProviderName => VariableProviderDefinitions.Backstory;

    /// <inheritdoc/>
    public async Task<IVariableProvider> CreateAsync(Guid? characterId, Guid realmId, Guid? locationId, CancellationToken ct)
    {
        if (!characterId.HasValue)
        {
            return BackstoryProvider.Empty;
        }

        var backstory = await _cache.GetOrLoadAsync(characterId.Value, ct);
        return new BackstoryProvider(backstory);
    }
}
