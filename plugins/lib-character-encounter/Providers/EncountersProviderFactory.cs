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
using BeyondImmersion.BannouService.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace BeyondImmersion.BannouService.CharacterEncounter.Providers;

/// <summary>
/// Factory for creating EncountersProvider instances.
/// Registered with DI as IVariableProviderFactory for Actor to discover.
/// </summary>
[BannouHelperService("encounters-provider", typeof(ICharacterEncounterService), typeof(IVariableProviderFactory), lifetime: ServiceLifetime.Singleton)]
public sealed class EncountersProviderFactory : IVariableProviderFactory
{
    private readonly IEncounterDataCache _cache;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly CharacterEncounterServiceConfiguration _configuration;

    /// <summary>
    /// Creates a new encounters provider factory.
    /// </summary>
    public EncountersProviderFactory(
        IEncounterDataCache cache,
        ITelemetryProvider telemetryProvider,
        CharacterEncounterServiceConfiguration configuration)
    {
        _cache = cache;
        ArgumentNullException.ThrowIfNull(telemetryProvider, nameof(telemetryProvider));
        _telemetryProvider = telemetryProvider;
        _configuration = configuration;
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

        var encounters = await _cache.GetEncountersOrLoadAsync(characterId.Value, ct);

        // Extract unique participant IDs (excluding this character) to load relational data
        var otherParticipants = new HashSet<Guid>();
        if (encounters?.Encounters != null)
        {
            foreach (var encounter in encounters.Encounters)
            {
                foreach (var pid in encounter.Encounter.ParticipantIds)
                {
                    if (pid != characterId.Value)
                    {
                        otherParticipants.Add(pid);
                    }
                }
            }
        }

        // Load sentiment and has-met data for all known participants via the cache
        var sentiments = new Dictionary<Guid, SentimentResponse>();
        var hasMet = new Dictionary<Guid, HasMetResponse>();
        foreach (var targetId in otherParticipants)
        {
            var sentiment = await _cache.GetSentimentOrLoadAsync(characterId.Value, targetId, ct);
            if (sentiment != null) sentiments[targetId] = sentiment;

            var met = await _cache.HasMetOrLoadAsync(characterId.Value, targetId, ct);
            if (met != null) hasMet[targetId] = met;
        }

        return new EncountersProvider(
            encounters,
            sentiments: sentiments,
            hasMet: hasMet,
            grudgeThreshold: (float)_configuration.GrudgeSentimentThreshold,
            allyThreshold: (float)_configuration.AllySentimentThreshold);
    }
}
