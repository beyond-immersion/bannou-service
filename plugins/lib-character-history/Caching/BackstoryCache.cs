// =============================================================================
// Backstory Cache Implementation
// Caches character backstory data with TTL.
// Owned by lib-character-history per service hierarchy.
// =============================================================================

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.CharacterHistory.Caching;

/// <summary>
/// Caches character backstory data for actor behavior execution.
/// Uses ConcurrentDictionary for thread-safety (IMPLEMENTATION TENETS compliant).
/// </summary>
public sealed class BackstoryCache : IBackstoryCache
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BackstoryCache> _logger;
    private readonly ConcurrentDictionary<Guid, CachedBackstory> _backstoryCache = new();
    private readonly TimeSpan _cacheTtl;

    /// <summary>
    /// Creates a new backstory cache.
    /// </summary>
    public BackstoryCache(
        IServiceScopeFactory scopeFactory,
        ILogger<BackstoryCache> logger,
        CharacterHistoryServiceConfiguration config)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _cacheTtl = TimeSpan.FromSeconds(config.BackstoryCacheTtlSeconds);
    }

    /// <inheritdoc/>
    public async Task<BackstoryResponse?> GetOrLoadAsync(Guid characterId, CancellationToken ct = default)
    {
        // Check cache first
        if (_backstoryCache.TryGetValue(characterId, out var cached) && !cached.IsExpired)
        {
            _logger.LogDebug("Backstory cache hit for character {CharacterId}", characterId);
            return cached.Backstory;
        }

        // Load from service
        _logger.LogDebug("Backstory cache miss for character {CharacterId}, loading from service", characterId);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<ICharacterHistoryClient>();

            var response = await client.GetBackstoryAsync(
                new GetBackstoryRequest { CharacterId = characterId },
                ct);

            if (response != null)
            {
                var newCached = new CachedBackstory(response, DateTimeOffset.UtcNow.Add(_cacheTtl));
                _backstoryCache[characterId] = newCached;
                _logger.LogDebug("Cached backstory for character {CharacterId} with {ElementCount} elements",
                    characterId, response.Elements.Count);
            }

            return response;
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogDebug("No backstory found for character {CharacterId}", characterId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load backstory for character {CharacterId}", characterId);
            return cached?.Backstory; // Return stale data if available
        }
    }

    /// <inheritdoc/>
    public void Invalidate(Guid characterId)
    {
        _backstoryCache.TryRemove(characterId, out _);
        _logger.LogDebug("Invalidated backstory cache for character {CharacterId}", characterId);
    }

    /// <inheritdoc/>
    public void InvalidateAll()
    {
        _backstoryCache.Clear();
        _logger.LogInformation("Cleared all backstory cache entries");
    }

    /// <summary>
    /// Cached backstory data with expiration time.
    /// </summary>
    private sealed record CachedBackstory(BackstoryResponse? Backstory, DateTimeOffset ExpiresAt)
    {
        public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
    }
}
