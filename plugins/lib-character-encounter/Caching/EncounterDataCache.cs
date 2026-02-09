// =============================================================================
// Encounter Data Cache Implementation
// Caches character encounter data with TTL.
// Owned by lib-character-encounter per service hierarchy.
// =============================================================================

using BeyondImmersion.Bannou.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.CharacterEncounter.Caching;

/// <summary>
/// Caches character encounter data for actor behavior execution.
/// Uses ConcurrentDictionary for thread-safety (IMPLEMENTATION TENETS compliant).
/// </summary>
public sealed class EncounterDataCache : IEncounterDataCache
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EncounterDataCache> _logger;
    private readonly ConcurrentDictionary<Guid, CachedEncounterList> _encounterListCache = new();
    private readonly ConcurrentDictionary<string, CachedSentiment> _sentimentCache = new();
    private readonly ConcurrentDictionary<string, CachedHasMet> _hasMetCache = new();
    private readonly ConcurrentDictionary<string, CachedEncounterList> _pairEncounterCache = new();

    private readonly TimeSpan _cacheTtl;
    private readonly int _maxEncounterResultsPerQuery;

    /// <summary>
    /// Creates a new encounter data cache.
    /// </summary>
    public EncounterDataCache(
        IServiceScopeFactory scopeFactory,
        ILogger<EncounterDataCache> logger,
        CharacterEncounterServiceConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _cacheTtl = TimeSpan.FromMinutes(configuration.Encounter_cacheTtlMinutes);
        _maxEncounterResultsPerQuery = configuration.EncounterCacheMaxResultsPerQuery;
    }

    /// <inheritdoc/>
    public async Task<EncounterListResponse?> GetEncountersOrLoadAsync(Guid characterId, CancellationToken ct = default)
    {
        // Check cache first
        if (_encounterListCache.TryGetValue(characterId, out var cached) && !cached.IsExpired)
        {
            _logger.LogDebug("Encounter list cache hit for character {CharacterId}", characterId);
            return cached.Encounters;
        }

        // Load from service
        _logger.LogDebug("Encounter list cache miss for character {CharacterId}, loading from service", characterId);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<ICharacterEncounterClient>();

            var response = await client.QueryByCharacterAsync(
                new QueryByCharacterRequest
                {
                    CharacterId = characterId,
                    PageSize = _maxEncounterResultsPerQuery
                },
                ct);

            if (response != null)
            {
                var newCached = new CachedEncounterList(response, DateTimeOffset.UtcNow.Add(_cacheTtl));
                _encounterListCache[characterId] = newCached;
                _logger.LogDebug("Cached {Count} encounters for character {CharacterId}",
                    response.TotalCount, characterId);
            }

            return response;
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogDebug("No encounters found for character {CharacterId}", characterId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load encounters for character {CharacterId}", characterId);
            return cached?.Encounters; // Return stale data if available
        }
    }

    /// <inheritdoc/>
    public async Task<SentimentResponse?> GetSentimentOrLoadAsync(Guid characterId, Guid targetCharacterId, CancellationToken ct = default)
    {
        var cacheKey = GetPairKey(characterId, targetCharacterId);

        // Check cache first
        if (_sentimentCache.TryGetValue(cacheKey, out var cached) && !cached.IsExpired)
        {
            _logger.LogDebug("Sentiment cache hit for {CharacterId} toward {TargetId}", characterId, targetCharacterId);
            return cached.Sentiment;
        }

        // Load from service
        _logger.LogDebug("Sentiment cache miss for {CharacterId} toward {TargetId}, loading from service",
            characterId, targetCharacterId);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<ICharacterEncounterClient>();

            var response = await client.GetSentimentAsync(
                new GetSentimentRequest
                {
                    CharacterId = characterId,
                    TargetCharacterId = targetCharacterId
                },
                ct);

            if (response != null)
            {
                var newCached = new CachedSentiment(response, DateTimeOffset.UtcNow.Add(_cacheTtl));
                _sentimentCache[cacheKey] = newCached;
                _logger.LogDebug("Cached sentiment {Sentiment} for {CharacterId} toward {TargetId}",
                    response.Sentiment, characterId, targetCharacterId);
            }

            return response;
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogDebug("No sentiment data found for {CharacterId} toward {TargetId}",
                characterId, targetCharacterId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load sentiment for {CharacterId} toward {TargetId}",
                characterId, targetCharacterId);
            return cached?.Sentiment; // Return stale data if available
        }
    }

    /// <inheritdoc/>
    public async Task<HasMetResponse?> HasMetOrLoadAsync(Guid characterId, Guid targetCharacterId, CancellationToken ct = default)
    {
        var cacheKey = GetPairKey(characterId, targetCharacterId);

        // Check cache first
        if (_hasMetCache.TryGetValue(cacheKey, out var cached) && !cached.IsExpired)
        {
            _logger.LogDebug("HasMet cache hit for {CharacterId} and {TargetId}", characterId, targetCharacterId);
            return cached.HasMet;
        }

        // Load from service
        _logger.LogDebug("HasMet cache miss for {CharacterId} and {TargetId}, loading from service",
            characterId, targetCharacterId);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<ICharacterEncounterClient>();

            var response = await client.HasMetAsync(
                new HasMetRequest
                {
                    CharacterIdA = characterId,
                    CharacterIdB = targetCharacterId
                },
                ct);

            if (response != null)
            {
                var newCached = new CachedHasMet(response, DateTimeOffset.UtcNow.Add(_cacheTtl));
                _hasMetCache[cacheKey] = newCached;
                _logger.LogDebug("Cached HasMet={HasMet} for {CharacterId} and {TargetId}",
                    response.HasMet, characterId, targetCharacterId);
            }

            return response;
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogDebug("No encounter data found for {CharacterId} and {TargetId}",
                characterId, targetCharacterId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check if {CharacterId} has met {TargetId}",
                characterId, targetCharacterId);
            return cached?.HasMet; // Return stale data if available
        }
    }

    /// <inheritdoc/>
    public async Task<EncounterListResponse?> GetEncountersBetweenOrLoadAsync(Guid characterIdA, Guid characterIdB, CancellationToken ct = default)
    {
        var cacheKey = GetPairKey(characterIdA, characterIdB);

        // Check cache first
        if (_pairEncounterCache.TryGetValue(cacheKey, out var cached) && !cached.IsExpired)
        {
            _logger.LogDebug("Pair encounter cache hit for {CharA} and {CharB}", characterIdA, characterIdB);
            return cached.Encounters;
        }

        // Load from service
        _logger.LogDebug("Pair encounter cache miss for {CharA} and {CharB}, loading from service",
            characterIdA, characterIdB);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<ICharacterEncounterClient>();

            var response = await client.QueryBetweenAsync(
                new QueryBetweenRequest
                {
                    CharacterIdA = characterIdA,
                    CharacterIdB = characterIdB,
                    PageSize = _maxEncounterResultsPerQuery
                },
                ct);

            if (response != null)
            {
                var newCached = new CachedEncounterList(response, DateTimeOffset.UtcNow.Add(_cacheTtl));
                _pairEncounterCache[cacheKey] = newCached;
                _logger.LogDebug("Cached {Count} encounters between {CharA} and {CharB}",
                    response.TotalCount, characterIdA, characterIdB);
            }

            return response;
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogDebug("No encounters found between {CharA} and {CharB}", characterIdA, characterIdB);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load encounters between {CharA} and {CharB}",
                characterIdA, characterIdB);
            return cached?.Encounters; // Return stale data if available
        }
    }

    /// <inheritdoc/>
    public void Invalidate(Guid characterId)
    {
        _encounterListCache.TryRemove(characterId, out _);

        // Remove all sentiment and has-met entries involving this character
        var keysToRemove = _sentimentCache.Keys.Where(k => k.Contains(characterId.ToString())).ToList();
        foreach (var key in keysToRemove)
        {
            _sentimentCache.TryRemove(key, out _);
        }

        keysToRemove = _hasMetCache.Keys.Where(k => k.Contains(characterId.ToString())).ToList();
        foreach (var key in keysToRemove)
        {
            _hasMetCache.TryRemove(key, out _);
        }

        keysToRemove = _pairEncounterCache.Keys.Where(k => k.Contains(characterId.ToString())).ToList();
        foreach (var key in keysToRemove)
        {
            _pairEncounterCache.TryRemove(key, out _);
        }

        _logger.LogDebug("Invalidated encounter cache for character {CharacterId}", characterId);
    }

    /// <inheritdoc/>
    public void InvalidateAll()
    {
        _encounterListCache.Clear();
        _sentimentCache.Clear();
        _hasMetCache.Clear();
        _pairEncounterCache.Clear();
        _logger.LogInformation("Cleared all encounter cache entries");
    }

    /// <summary>
    /// Creates a consistent cache key for a pair of character IDs.
    /// </summary>
    private static string GetPairKey(Guid charA, Guid charB)
    {
        // Always put the smaller GUID first for consistent keying
        return charA < charB ? $"{charA}:{charB}" : $"{charB}:{charA}";
    }

    /// <summary>
    /// Cached encounter list with expiration time.
    /// </summary>
    private sealed record CachedEncounterList(EncounterListResponse? Encounters, DateTimeOffset ExpiresAt)
    {
        public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
    }

    /// <summary>
    /// Cached sentiment data with expiration time.
    /// </summary>
    private sealed record CachedSentiment(SentimentResponse? Sentiment, DateTimeOffset ExpiresAt)
    {
        public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
    }

    /// <summary>
    /// Cached has-met data with expiration time.
    /// </summary>
    private sealed record CachedHasMet(HasMetResponse? HasMet, DateTimeOffset ExpiresAt)
    {
        public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
    }
}
