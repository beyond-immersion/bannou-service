using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using BeyondImmersion.BannouService.Obligation;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;

namespace BeyondImmersion.BannouService.Obligation.Providers;

/// <summary>
/// Factory for creating <see cref="ObligationProvider"/> instances.
/// Registered with DI as <see cref="IVariableProviderFactory"/> for Actor (L2) to discover
/// via <c>IEnumerable&lt;IVariableProviderFactory&gt;</c> dependency injection.
/// </summary>
/// <remarks>
/// Reads from the obligation cache (Redis). If the cache is empty for a character,
/// returns an empty provider (zero obligations). The cache is populated by contract
/// lifecycle event handlers and the QueryObligations/InvalidateCache endpoints.
/// </remarks>
public sealed class ObligationProviderFactory : IVariableProviderFactory
{
    private readonly IStateStore<ObligationManifestModel> _cacheStore;

    /// <summary>
    /// Creates a new obligation provider factory.
    /// </summary>
    /// <param name="stateStoreFactory">State store factory for accessing the obligation cache.</param>
    public ObligationProviderFactory(IStateStoreFactory stateStoreFactory)
    {
        _cacheStore = stateStoreFactory.GetStore<ObligationManifestModel>(
            StateStoreDefinitions.ObligationCache);
    }

    /// <inheritdoc/>
    public string ProviderName => "obligations";

    /// <inheritdoc/>
    public async Task<IVariableProvider> CreateAsync(Guid? entityId, CancellationToken ct)
    {
        if (!entityId.HasValue)
        {
            return ObligationProvider.Empty;
        }

        var manifest = await _cacheStore.GetAsync(entityId.Value.ToString(), ct);
        return new ObligationProvider(manifest);
    }
}
