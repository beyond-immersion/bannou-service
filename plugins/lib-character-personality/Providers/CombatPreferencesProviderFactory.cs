// =============================================================================
// Combat Preferences Variable Provider Factory
// Creates CombatPreferencesProvider instances for ABML expression evaluation.
// Registered with DI for Actor to consume via IEnumerable<IVariableProviderFactory>.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using BeyondImmersion.BannouService.CharacterPersonality.Caching;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using System.Diagnostics;

namespace BeyondImmersion.BannouService.CharacterPersonality.Providers;

/// <summary>
/// Factory for creating CombatPreferencesProvider instances.
/// Registered with DI as IVariableProviderFactory for Actor to discover.
/// </summary>
public sealed class CombatPreferencesProviderFactory : IVariableProviderFactory
{
    private readonly IPersonalityDataCache _cache;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>
    /// Creates a new combat preferences provider factory.
    /// </summary>
    public CombatPreferencesProviderFactory(IPersonalityDataCache cache, ITelemetryProvider telemetryProvider)
    {
        _cache = cache;
        ArgumentNullException.ThrowIfNull(telemetryProvider, nameof(telemetryProvider));
        _telemetryProvider = telemetryProvider;
    }

    /// <inheritdoc/>
    public string ProviderName => VariableProviderDefinitions.Combat;

    /// <inheritdoc/>
    public async Task<IVariableProvider> CreateAsync(Guid? characterId, Guid realmId, Guid? locationId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-personality", "CombatPreferencesProviderFactory.CreateAsync");
        if (!characterId.HasValue)
        {
            return CombatPreferencesProvider.Empty;
        }

        var combatPrefs = await _cache.GetOrLoadCombatPreferencesAsync(characterId.Value, ct);
        return new CombatPreferencesProvider(combatPrefs);
    }
}
