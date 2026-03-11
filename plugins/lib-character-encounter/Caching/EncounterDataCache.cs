// =============================================================================
// Encounter Data Cache Implementation
// Caches character encounter data with TTL.
// Owned by lib-character-encounter per service hierarchy.
// =============================================================================

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.CharacterEncounter.Caching;

/// <summary>
/// Caches character encounter data for actor behavior execution.
/// Composes <see cref="VariableProviderCacheBucket{TKey, TData}"/> for thread-safe
/// TTL-based caching with stale-data fallback (IMPLEMENTATION TENETS compliant).
/// </summary>
[BannouHelperService("encounter-data", typeof(ICharacterEncounterService), typeof(IEncounterDataCache), lifetime: ServiceLifetime.Singleton)]
public sealed class EncounterDataCache : IEncounterDataCache
{
    private const string PAIR_KEY_PREFIX = "pair:";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly int _maxEncounterResultsPerQuery;

    private readonly VariableProviderCacheBucket<Guid, EncounterListResponse> _encounterListBucket;
    private readonly VariableProviderCacheBucket<string, SentimentResponse> _sentimentBucket;
    private readonly VariableProviderCacheBucket<string, HasMetResponse> _hasMetBucket;
    private readonly VariableProviderCacheBucket<string, EncounterListResponse> _pairEncounterBucket;

    /// <summary>
    /// Creates a new encounter data cache.
    /// </summary>
    public EncounterDataCache(
        IServiceScopeFactory scopeFactory,
        ILogger<EncounterDataCache> logger,
        CharacterEncounterServiceConfiguration configuration,
        ITelemetryProvider telemetryProvider)
    {
        _scopeFactory = scopeFactory;
        ArgumentNullException.ThrowIfNull(telemetryProvider, nameof(telemetryProvider));
        _maxEncounterResultsPerQuery = configuration.EncounterCacheMaxResultsPerQuery;

        var ttl = TimeSpan.FromMinutes(configuration.EncounterCacheTtlMinutes);
        _encounterListBucket = new VariableProviderCacheBucket<Guid, EncounterListResponse>(
            ttl, logger, telemetryProvider, "bannou.character-encounter", "EncounterListCache");
        _sentimentBucket = new VariableProviderCacheBucket<string, SentimentResponse>(
            ttl, logger, telemetryProvider, "bannou.character-encounter", "SentimentCache");
        _hasMetBucket = new VariableProviderCacheBucket<string, HasMetResponse>(
            ttl, logger, telemetryProvider, "bannou.character-encounter", "HasMetCache");
        _pairEncounterBucket = new VariableProviderCacheBucket<string, EncounterListResponse>(
            ttl, logger, telemetryProvider, "bannou.character-encounter", "PairEncounterCache");
    }

    /// <inheritdoc/>
    public async Task<EncounterListResponse?> GetEncountersOrLoadAsync(Guid characterId, CancellationToken ct = default)
    {
        return await _encounterListBucket.GetOrLoadAsync(characterId, async loadCt =>
        {
            using var scope = _scopeFactory.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<ICharacterEncounterClient>();
            return await client.QueryByCharacterAsync(
                new QueryByCharacterRequest
                {
                    CharacterId = characterId,
                    PageSize = _maxEncounterResultsPerQuery
                },
                loadCt);
        }, ct);
    }

    /// <inheritdoc/>
    public async Task<SentimentResponse?> GetSentimentOrLoadAsync(Guid characterId, Guid targetCharacterId, CancellationToken ct = default)
    {
        var cacheKey = BuildPairKey(characterId, targetCharacterId);
        return await _sentimentBucket.GetOrLoadAsync(cacheKey, async loadCt =>
        {
            using var scope = _scopeFactory.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<ICharacterEncounterClient>();
            return await client.GetSentimentAsync(
                new GetSentimentRequest
                {
                    CharacterId = characterId,
                    TargetCharacterId = targetCharacterId
                },
                loadCt);
        }, ct);
    }

    /// <inheritdoc/>
    public async Task<HasMetResponse?> HasMetOrLoadAsync(Guid characterId, Guid targetCharacterId, CancellationToken ct = default)
    {
        var cacheKey = BuildPairKey(characterId, targetCharacterId);
        return await _hasMetBucket.GetOrLoadAsync(cacheKey, async loadCt =>
        {
            using var scope = _scopeFactory.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<ICharacterEncounterClient>();
            return await client.HasMetAsync(
                new HasMetRequest
                {
                    CharacterIdA = characterId,
                    CharacterIdB = targetCharacterId
                },
                loadCt);
        }, ct);
    }

    /// <inheritdoc/>
    public async Task<EncounterListResponse?> GetEncountersBetweenOrLoadAsync(Guid characterIdA, Guid characterIdB, CancellationToken ct = default)
    {
        var cacheKey = BuildPairKey(characterIdA, characterIdB);
        return await _pairEncounterBucket.GetOrLoadAsync(cacheKey, async loadCt =>
        {
            using var scope = _scopeFactory.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<ICharacterEncounterClient>();
            return await client.QueryBetweenAsync(
                new QueryBetweenRequest
                {
                    CharacterIdA = characterIdA,
                    CharacterIdB = characterIdB,
                    PageSize = _maxEncounterResultsPerQuery
                },
                loadCt);
        }, ct);
    }

    /// <inheritdoc/>
    public void Invalidate(Guid characterId)
    {
        _encounterListBucket.Invalidate(characterId);

        // Remove all sentiment, has-met, and pair encounter entries involving this character
        var charIdString = characterId.ToString();
        _sentimentBucket.InvalidateWhere(k => k.Contains(charIdString));
        _hasMetBucket.InvalidateWhere(k => k.Contains(charIdString));
        _pairEncounterBucket.InvalidateWhere(k => k.Contains(charIdString));
    }

    /// <inheritdoc/>
    public void InvalidateAll()
    {
        _encounterListBucket.InvalidateAll();
        _sentimentBucket.InvalidateAll();
        _hasMetBucket.InvalidateAll();
        _pairEncounterBucket.InvalidateAll();
    }

    /// <summary>
    /// Creates a consistent cache key for a pair of character IDs.
    /// </summary>
    internal static string BuildPairKey(Guid charA, Guid charB)
    {
        // Always put the smaller GUID first for consistent keying
        return charA < charB ? $"{PAIR_KEY_PREFIX}{charA}:{charB}" : $"{PAIR_KEY_PREFIX}{charB}:{charA}";
    }
}
