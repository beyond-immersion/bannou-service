// =============================================================================
// Encounters Variable Provider Factory
// Creates EncountersProvider instances for ABML expression evaluation.
// Registered with DI for Actor to consume via IEnumerable<IVariableProviderFactory>.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using BeyondImmersion.BannouService.CharacterEncounter.Caching;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using System.Diagnostics;

namespace BeyondImmersion.BannouService.CharacterEncounter.Providers;

/// <summary>
/// Factory for creating EncountersProvider instances.
/// Registered with DI as IVariableProviderFactory for Actor to discover.
/// </summary>
public sealed class EncountersProviderFactory : IVariableProviderFactory
{
    private readonly IEncounterDataCache _cache;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>
    /// Creates a new encounters provider factory.
    /// </summary>
    public EncountersProviderFactory(IEncounterDataCache cache, ITelemetryProvider telemetryProvider)
    {
        _cache = cache;
        ArgumentNullException.ThrowIfNull(telemetryProvider, nameof(telemetryProvider));
        _telemetryProvider = telemetryProvider;
    }

    /// <inheritdoc/>
    public string ProviderName => VariableProviderDefinitions.Encounters;

    /// <inheritdoc/>
    public async Task<IVariableProvider> CreateAsync(Guid? characterId, Guid realmId, Guid? locationId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-encounter", "EncountersProviderFactory.CreateAsync");
        if (!characterId.HasValue)
        {
            return EncountersProvider.Empty;
        }

        // Load basic encounter list
        // Note: sentiment, hasMet, and pairEncounters are loaded on-demand via the cache
        // For now, we just load the basic encounter list for the provider
        var encounters = await _cache.GetEncountersOrLoadAsync(characterId.Value, ct);
        return new EncountersProvider(encounters);
    }
}
