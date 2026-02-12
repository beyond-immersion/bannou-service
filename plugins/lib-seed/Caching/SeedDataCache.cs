// =============================================================================
// Seed Data Cache Implementation
// Caches character seed data with TTL for actor behavior execution.
// Owned by lib-seed per service hierarchy (L2).
// =============================================================================

using BeyondImmersion.Bannou.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Seed.Caching;

/// <summary>
/// Caches character seed data for actor behavior execution.
/// Uses ConcurrentDictionary for thread-safety (IMPLEMENTATION TENETS compliant).
/// Loads data via ISeedClient through mesh, matching the established provider cache pattern.
/// </summary>
public sealed class SeedDataCache : ISeedDataCache
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SeedDataCache> _logger;
    private readonly ConcurrentDictionary<Guid, CachedEntry> _cache = new();
    private readonly TimeSpan _cacheTtl;

    /// <summary>
    /// Creates a new seed data cache.
    /// </summary>
    public SeedDataCache(
        IServiceScopeFactory scopeFactory,
        ILogger<SeedDataCache> logger,
        SeedServiceConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _cacheTtl = TimeSpan.FromSeconds(configuration.SeedDataCacheTtlSeconds);
    }

    /// <inheritdoc/>
    public async Task<CachedSeedData> GetSeedDataOrLoadAsync(Guid characterId, CancellationToken ct = default)
    {
        // Check cache first
        if (_cache.TryGetValue(characterId, out var cached) && !cached.IsExpired)
        {
            _logger.LogDebug("Seed data cache hit for character {CharacterId}", characterId);
            return cached.Data;
        }

        // Load from service
        _logger.LogDebug("Seed data cache miss for character {CharacterId}, loading from service", characterId);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var client = scope.ServiceProvider.GetRequiredService<ISeedClient>();

            // Step 1: Get all seeds for this character
            var seedsResponse = await client.GetSeedsByOwnerAsync(new GetSeedsByOwnerRequest
            {
                OwnerId = characterId,
                OwnerType = "Character",
                IncludeArchived = false
            }, ct);

            // Filter to active seeds only
            var activeSeeds = seedsResponse.Seeds
                .Where(s => s.Status == SeedStatus.Active)
                .ToList();

            if (activeSeeds.Count == 0)
            {
                _logger.LogDebug("No active seeds for character {CharacterId}", characterId);
                var emptyData = CachedSeedData.Empty;
                _cache[characterId] = new CachedEntry(emptyData, DateTimeOffset.UtcNow.Add(_cacheTtl));
                return emptyData;
            }

            // Step 2: Load growth and capability data for each active seed
            var growthDict = new Dictionary<Guid, GrowthResponse>();
            var capabilityDict = new Dictionary<Guid, CapabilityManifestResponse>();

            foreach (var seed in activeSeeds)
            {
                try
                {
                    var growth = await client.GetGrowthAsync(new GetGrowthRequest
                    {
                        SeedId = seed.SeedId
                    }, ct);
                    growthDict[seed.SeedId] = growth;
                }
                catch (ApiException ex) when (ex.StatusCode == 404)
                {
                    _logger.LogDebug("No growth data for seed {SeedId}", seed.SeedId);
                }

                try
                {
                    var manifest = await client.GetCapabilityManifestAsync(new GetCapabilityManifestRequest
                    {
                        SeedId = seed.SeedId
                    }, ct);
                    capabilityDict[seed.SeedId] = manifest;
                }
                catch (ApiException ex) when (ex.StatusCode == 404)
                {
                    _logger.LogDebug("No capability manifest for seed {SeedId}", seed.SeedId);
                }
            }

            var data = new CachedSeedData(activeSeeds, growthDict, capabilityDict);
            _cache[characterId] = new CachedEntry(data, DateTimeOffset.UtcNow.Add(_cacheTtl));

            _logger.LogDebug("Cached {SeedCount} active seeds for character {CharacterId}",
                activeSeeds.Count, characterId);

            return data;
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            // Character has no seeds - valid state, return empty
            _logger.LogDebug("No seeds found for character {CharacterId}", characterId);
            var emptyData = CachedSeedData.Empty;
            _cache[characterId] = new CachedEntry(emptyData, DateTimeOffset.UtcNow.Add(_cacheTtl));
            return emptyData;
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Seed API returned {StatusCode} for character {CharacterId}",
                ex.StatusCode, characterId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load seed data for character {CharacterId}", characterId);
            throw;
        }
    }

    /// <inheritdoc/>
    public void Invalidate(Guid characterId)
    {
        _cache.TryRemove(characterId, out _);
        _logger.LogDebug("Invalidated seed data cache for character {CharacterId}", characterId);
    }

    /// <inheritdoc/>
    public void InvalidateAll()
    {
        _cache.Clear();
        _logger.LogInformation("Cleared all seed data cache entries");
    }

    /// <summary>
    /// Cached seed data with expiration time.
    /// </summary>
    private sealed record CachedEntry(CachedSeedData Data, DateTimeOffset ExpiresAt)
    {
        public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
    }
}
