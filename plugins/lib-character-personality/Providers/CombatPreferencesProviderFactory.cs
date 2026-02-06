// =============================================================================
// Combat Preferences Variable Provider Factory
// Creates CombatPreferencesProvider instances for ABML expression evaluation.
// Registered with DI for Actor to consume via IEnumerable<IVariableProviderFactory>.
// =============================================================================

using BeyondImmersion.BannouService.Abml.Expressions;
using BeyondImmersion.BannouService.CharacterPersonality.Caching;
using BeyondImmersion.BannouService.Providers;

namespace BeyondImmersion.BannouService.CharacterPersonality.Providers;

/// <summary>
/// Factory for creating CombatPreferencesProvider instances.
/// Registered with DI as IVariableProviderFactory for Actor to discover.
/// </summary>
public sealed class CombatPreferencesProviderFactory : IVariableProviderFactory
{
    private readonly IPersonalityDataCache _cache;

    /// <summary>
    /// Creates a new combat preferences provider factory.
    /// </summary>
    public CombatPreferencesProviderFactory(IPersonalityDataCache cache)
    {
        _cache = cache;
    }

    /// <inheritdoc/>
    public string ProviderName => "combat";

    /// <inheritdoc/>
    public async Task<IVariableProvider> CreateAsync(Guid? entityId, CancellationToken ct)
    {
        if (!entityId.HasValue)
        {
            return CombatPreferencesProvider.Empty;
        }

        var combatPrefs = await _cache.GetOrLoadCombatPreferencesAsync(entityId.Value, ct);
        return new CombatPreferencesProvider(combatPrefs);
    }
}
