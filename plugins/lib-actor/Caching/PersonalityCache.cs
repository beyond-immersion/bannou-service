// =============================================================================
// Personality Cache Implementation
// Caches character personality data for actor behavior execution with TTL.
// =============================================================================

using BeyondImmersion.BannouService.CharacterPersonality;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Actor.Caching;

/// <summary>
/// Caches character personality data for actor behavior execution.
/// Uses ConcurrentDictionary for thread-safety (IMPLEMENTATION TENETS compliant).
/// </summary>
public sealed class PersonalityCache : IPersonalityCache
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PersonalityCache> _logger;
    private readonly ConcurrentDictionary<Guid, CachedPersonality> _personalityCache = new();
    private readonly ConcurrentDictionary<Guid, CachedCombatPreferences> _combatCache = new();

    /// <summary>
    /// Time-to-live for cached personality data (5 minutes).
    /// </summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Creates a new personality cache.
    /// </summary>
    public PersonalityCache(
        IServiceScopeFactory scopeFactory,
        ILogger<PersonalityCache> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<PersonalityResponse?> GetOrLoadAsync(Guid characterId, CancellationToken ct = default)
    {
        // Check cache first
        if (_personalityCache.TryGetValue(characterId, out var cached) && !cached.IsExpired)
        {
            _logger.LogDebug("Personality cache hit for character {CharacterId}", characterId);
            return cached.Personality;
        }

        // Load from service
        _logger.LogDebug("Personality cache miss for character {CharacterId}, loading from service", characterId);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<ICharacterPersonalityClient>();

            var response = await client.GetPersonalityAsync(
                new GetPersonalityRequest { CharacterId = characterId },
                ct);

            if (response != null)
            {
                var newCached = new CachedPersonality(response, DateTimeOffset.UtcNow.Add(CacheTtl));
                _personalityCache[characterId] = newCached;
                _logger.LogDebug("Cached personality for character {CharacterId}, version {Version}",
                    characterId, response.Version);
            }

            return response;
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogDebug("No personality found for character {CharacterId}", characterId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load personality for character {CharacterId}", characterId);
            return cached?.Personality; // Return stale data if available
        }
    }

    /// <inheritdoc/>
    public async Task<CombatPreferencesResponse?> GetCombatPreferencesOrLoadAsync(Guid characterId, CancellationToken ct = default)
    {
        // Check cache first
        if (_combatCache.TryGetValue(characterId, out var cached) && !cached.IsExpired)
        {
            _logger.LogDebug("Combat preferences cache hit for character {CharacterId}", characterId);
            return cached.Preferences;
        }

        // Load from service
        _logger.LogDebug("Combat preferences cache miss for character {CharacterId}, loading from service", characterId);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<ICharacterPersonalityClient>();

            var response = await client.GetCombatPreferencesAsync(
                new GetCombatPreferencesRequest { CharacterId = characterId },
                ct);

            if (response != null)
            {
                var newCached = new CachedCombatPreferences(response, DateTimeOffset.UtcNow.Add(CacheTtl));
                _combatCache[characterId] = newCached;
                _logger.LogDebug("Cached combat preferences for character {CharacterId}", characterId);
            }

            return response;
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogDebug("No combat preferences found for character {CharacterId}", characterId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load combat preferences for character {CharacterId}", characterId);
            return cached?.Preferences; // Return stale data if available
        }
    }

    /// <inheritdoc/>
    public void Invalidate(Guid characterId)
    {
        _personalityCache.TryRemove(characterId, out _);
        _combatCache.TryRemove(characterId, out _);
        _logger.LogDebug("Invalidated personality cache for character {CharacterId}", characterId);
    }

    /// <inheritdoc/>
    public void InvalidateAll()
    {
        _personalityCache.Clear();
        _combatCache.Clear();
        _logger.LogInformation("Cleared all personality cache entries");
    }

    /// <summary>
    /// Cached personality data with expiration time.
    /// </summary>
    private sealed record CachedPersonality(PersonalityResponse? Personality, DateTimeOffset ExpiresAt)
    {
        public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
    }

    /// <summary>
    /// Cached combat preferences data with expiration time.
    /// </summary>
    private sealed record CachedCombatPreferences(CombatPreferencesResponse? Preferences, DateTimeOffset ExpiresAt)
    {
        public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
    }
}
