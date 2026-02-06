// =============================================================================
// Personality Variable Provider Factory
// Creates PersonalityProvider instances for ABML expression evaluation.
// Registered with DI for Actor to consume via IEnumerable<IVariableProviderFactory>.
// =============================================================================

using BeyondImmersion.BannouService.Abml.Expressions;
using BeyondImmersion.BannouService.CharacterPersonality.Caching;
using BeyondImmersion.BannouService.Providers;

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
    public string ProviderName => "personality";

    /// <inheritdoc/>
    public async Task<IVariableProvider> CreateAsync(Guid? entityId, CancellationToken ct)
    {
        if (!entityId.HasValue)
        {
            return PersonalityProvider.Empty;
        }

        var personality = await _cache.GetOrLoadPersonalityAsync(entityId.Value, ct);
        return new PersonalityProvider(personality);
    }
}
