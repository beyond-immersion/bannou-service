// =============================================================================
// Quest Data Cache Implementation
// Caches character quest data with TTL.
// Owned by lib-quest per service hierarchy.
// =============================================================================

using BeyondImmersion.Bannou.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Quest.Caching;

/// <summary>
/// Caches character quest data for actor behavior execution.
/// Uses ConcurrentDictionary for thread-safety (IMPLEMENTATION TENETS compliant).
/// </summary>
public sealed class QuestDataCache : IQuestDataCache
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<QuestDataCache> _logger;
    private readonly ConcurrentDictionary<Guid, CachedQuests> _cache = new();
    private readonly TimeSpan _cacheTtl;

    /// <summary>
    /// Creates a new quest data cache.
    /// </summary>
    public QuestDataCache(
        IServiceScopeFactory scopeFactory,
        ILogger<QuestDataCache> logger,
        QuestServiceConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _cacheTtl = TimeSpan.FromSeconds(configuration.QuestDataCacheTtlSeconds);
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
            var newCached = new CachedQuests(result, DateTimeOffset.UtcNow.Add(_cacheTtl));
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
            _cache[characterId] = new CachedQuests(emptyResponse, DateTimeOffset.UtcNow.Add(_cacheTtl));
            return emptyResponse;
        }
        catch (ApiException ex)
        {
            // API error - log and rethrow
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
