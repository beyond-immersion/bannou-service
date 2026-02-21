// =============================================================================
// Personality Variable Provider Factory
// Creates PersonalityProvider instances for ABML expression evaluation.
// Registered with DI for Actor to consume via IEnumerable<IVariableProviderFactory>.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using BeyondImmersion.BannouService.CharacterPersonality.Caching;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;

namespace BeyondImmersion.BannouService.CharacterPersonality.Providers;

/// <summary>
/// Factory for creating PersonalityProvider instances.
/// Registered with DI as IVariableProviderFactory for Actor to discover.
/// </summary>
public sealed class PersonalityProviderFactory : IVariableProviderFactory
{
    private readonly IPersonalityDataCache _cache;

    /// <summary>
    /// Creates a new personality provider factory.
    /// </summary>
    public PersonalityProviderFactory(IPersonalityDataCache cache)
    {
        _cache = cache;
    }

    /// <inheritdoc/>
    public string ProviderName => VariableProviderDefinitions.Personality;

    /// <inheritdoc/>
    public async Task<IVariableProvider> CreateAsync(Guid? characterId, Guid realmId, Guid? locationId, CancellationToken ct)
    {
        if (!characterId.HasValue)
        {
            return PersonalityProvider.Empty;
        }

        var personality = await _cache.GetOrLoadPersonalityAsync(characterId.Value, ct);
        return new PersonalityProvider(personality);
    }
}
