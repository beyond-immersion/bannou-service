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
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>
    /// Creates a new obligation provider factory.
    /// </summary>
    /// <param name="stateStoreFactory">State store factory for accessing the obligation cache.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    public ObligationProviderFactory(IStateStoreFactory stateStoreFactory, ITelemetryProvider telemetryProvider)
    {
        _cacheStore = stateStoreFactory.GetStore<ObligationManifestModel>(
            StateStoreDefinitions.ObligationCache);
        _telemetryProvider = telemetryProvider;
    }

    /// <inheritdoc/>
    public string ProviderName => VariableProviderDefinitions.Obligations;

    /// <inheritdoc/>
    public async Task<IVariableProvider> CreateAsync(Guid? characterId, Guid realmId, Guid? locationId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.obligation", "ObligationProviderFactory.CreateAsync");
        if (!characterId.HasValue)
        {
            return ObligationProvider.Empty;
        }

        var manifest = await _cacheStore.GetAsync(characterId.Value.ToString(), ct);
        return new ObligationProvider(manifest);
    }
}
