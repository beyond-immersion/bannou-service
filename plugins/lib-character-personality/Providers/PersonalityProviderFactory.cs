// =============================================================================
// Personality Variable Provider Factory
// Creates PersonalityProvider instances for ABML expression evaluation.
// Registered with DI for Actor to consume via IEnumerable<IVariableProviderFactory>.
// =============================================================================

using BeyondImmersion.Bannou.BehaviorExpressions.Expressions;
using BeyondImmersion.BannouService.CharacterPersonality.Caching;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using System.Diagnostics;

namespace BeyondImmersion.BannouService.CharacterPersonality.Providers;

/// <summary>
/// Factory for creating PersonalityProvider instances.
/// Registered with DI as IVariableProviderFactory for Actor to discover.
/// </summary>
public sealed class PersonalityProviderFactory : IVariableProviderFactory
{
    private readonly IPersonalityDataCache _cache;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>
    /// Creates a new personality provider factory.
    /// </summary>
    public PersonalityProviderFactory(IPersonalityDataCache cache, ITelemetryProvider telemetryProvider)
    {
        _cache = cache;
        ArgumentNullException.ThrowIfNull(telemetryProvider, nameof(telemetryProvider));
        _telemetryProvider = telemetryProvider;
    }

    /// <inheritdoc/>
    public string ProviderName => VariableProviderDefinitions.Personality;

    /// <inheritdoc/>
    public async Task<IVariableProvider> CreateAsync(Guid? characterId, Guid realmId, Guid? locationId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.character-personality", "PersonalityProviderFactory.CreateAsync");
        if (!characterId.HasValue)
        {
            return PersonalityProvider.Empty;
        }

        var personality = await _cache.GetOrLoadPersonalityAsync(characterId.Value, ct);
        return new PersonalityProvider(personality);
    }
}
