// =============================================================================
// Personality Data Cache Implementation
// Caches character personality and combat preferences data with TTL.
// Owned by lib-character-personality per service hierarchy.
// =============================================================================

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.CharacterPersonality.Caching;

/// <summary>
/// Caches character personality and combat preferences data for actor behavior execution.
/// Composes <see cref="VariableProviderCacheBucket{TKey, TData}"/> for thread-safe
/// TTL-based caching with stale-data fallback (IMPLEMENTATION TENETS compliant).
/// </summary>
[BannouHelperService("personality-data", typeof(ICharacterPersonalityService), typeof(IPersonalityDataCache), lifetime: ServiceLifetime.Singleton)]
public sealed class PersonalityDataCache : IPersonalityDataCache
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly VariableProviderCacheBucket<Guid, PersonalityResponse> _personalityBucket;
    private readonly VariableProviderCacheBucket<Guid, CombatPreferencesResponse> _combatBucket;

    /// <summary>
    /// Creates a new personality data cache.
    /// </summary>
    public PersonalityDataCache(
        IServiceScopeFactory scopeFactory,
        ILogger<PersonalityDataCache> logger,
        CharacterPersonalityServiceConfiguration config,
        ITelemetryProvider telemetryProvider)
    {
        _scopeFactory = scopeFactory;
        ArgumentNullException.ThrowIfNull(telemetryProvider, nameof(telemetryProvider));

        var ttl = TimeSpan.FromMinutes(config.PersonalityCacheTtlMinutes);
        _personalityBucket = new VariableProviderCacheBucket<Guid, PersonalityResponse>(
            ttl, logger, telemetryProvider, "bannou.character-personality", "PersonalityCache");
        _combatBucket = new VariableProviderCacheBucket<Guid, CombatPreferencesResponse>(
            ttl, logger, telemetryProvider, "bannou.character-personality", "CombatPreferencesCache");
    }

    /// <inheritdoc/>
    public async Task<PersonalityResponse?> GetOrLoadPersonalityAsync(Guid characterId, CancellationToken ct = default)
    {
        return await _personalityBucket.GetOrLoadAsync(characterId, async loadCt =>
        {
            using var scope = _scopeFactory.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<ICharacterPersonalityClient>();
            return await client.GetPersonalityAsync(
                new GetPersonalityRequest { CharacterId = characterId },
                loadCt);
        }, ct);
    }

    /// <inheritdoc/>
    public async Task<CombatPreferencesResponse?> GetOrLoadCombatPreferencesAsync(Guid characterId, CancellationToken ct = default)
    {
        return await _combatBucket.GetOrLoadAsync(characterId, async loadCt =>
        {
            using var scope = _scopeFactory.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<ICharacterPersonalityClient>();
            return await client.GetCombatPreferencesAsync(
                new GetCombatPreferencesRequest { CharacterId = characterId },
                loadCt);
        }, ct);
    }

    /// <inheritdoc/>
    public void Invalidate(Guid characterId)
    {
        _personalityBucket.Invalidate(characterId);
        _combatBucket.Invalidate(characterId);
    }

    /// <inheritdoc/>
    public void InvalidateAll()
    {
        _personalityBucket.InvalidateAll();
        _combatBucket.InvalidateAll();
    }
}
