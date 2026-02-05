// =============================================================================
// Quest Cache Implementation
// Caches character quest data for actor behavior execution with TTL.
// =============================================================================

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Quest;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Actor.Caching;

/// <summary>
/// Caches character quest data for actor behavior execution.
/// Uses ConcurrentDictionary for thread-safety (IMPLEMENTATION TENETS compliant).
/// </summary>
public sealed class QuestCache : IQuestCache
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<QuestCache> _logger;
    private readonly ActorServiceConfiguration _configuration;
    private readonly ConcurrentDictionary<Guid, CachedQuests> _cache = new();

    /// <summary>
    /// Time-to-live for cached quest data.
    /// </summary>
    private TimeSpan CacheTtl => TimeSpan.FromMinutes(_configuration.QuestCacheTtlMinutes);

    /// <summary>
    /// Creates a new quest cache.
    /// </summary>
    /// <param name="scopeFactory">Service scope factory for creating scoped clients.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="configuration">Actor service configuration.</param>
    public QuestCache(
        IServiceScopeFactory scopeFactory,
        ILogger<QuestCache> logger,
        ActorServiceConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _configuration = configuration;
    }

    /// <inheritdoc/>
    public async Task<ListQuestsResponse> GetActiveQuestsOrLoadAsync(Guid characterId, CancellationToken ct = default)
    {
        // Check cache first
        if (_cache.TryGetValue(characterId, out var cached) && !cached.IsExpired)
        {
            _logger.LogDebug("Quest cache hit for character {CharacterId}", characterId);
            return cached.Response;
        }

        // Load from service
        _logger.LogDebug("Quest cache miss for character {CharacterId}, loading from service", characterId);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<IQuestClient>();

            var request = new ListQuestsRequest
            {
                CharacterId = characterId,
                Statuses = new[] { QuestStatus.ACTIVE }
            };

            var response = await client.ListQuestsAsync(request, ct);

            // Cache the result (response is never null from client, but be defensive)
            var result = response ?? new ListQuestsResponse { Quests = new List<QuestInstanceResponse>(), Total = 0 };
            var newCached = new CachedQuests(result, DateTimeOffset.UtcNow.Add(CacheTtl));
            _cache[characterId] = newCached;

            _logger.LogDebug("Cached {QuestCount} active quests for character {CharacterId}",
                result.Quests.Count, characterId);

            return result;
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            // Character has no quests - valid state, return empty response
            _logger.LogDebug("No quests found for character {CharacterId}", characterId);
            var emptyResponse = new ListQuestsResponse { Quests = new List<QuestInstanceResponse>(), Total = 0 };
            _cache[characterId] = new CachedQuests(emptyResponse, DateTimeOffset.UtcNow.Add(CacheTtl));
            return emptyResponse;
        }
        catch (ApiException ex)
        {
            // API error - log and rethrow (no graceful degradation per TENET)
            _logger.LogWarning(ex, "Quest API returned {StatusCode} for character {CharacterId}",
                ex.StatusCode, characterId);
            throw;
        }
        catch (Exception ex)
        {
            // Infrastructure error - log at Error level, rethrow
            _logger.LogError(ex, "Failed to load quest data for character {CharacterId}", characterId);
            throw;
        }
    }

    /// <inheritdoc/>
    public void Invalidate(Guid characterId)
    {
        _cache.TryRemove(characterId, out _);
        _logger.LogDebug("Invalidated quest cache for character {CharacterId}", characterId);
    }

    /// <inheritdoc/>
    public void InvalidateAll()
    {
        _cache.Clear();
        _logger.LogInformation("Cleared all quest cache entries");
    }

    /// <summary>
    /// Cached quest data with expiration time.
    /// </summary>
    private sealed record CachedQuests(ListQuestsResponse Response, DateTimeOffset ExpiresAt)
    {
        public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
    }
}
