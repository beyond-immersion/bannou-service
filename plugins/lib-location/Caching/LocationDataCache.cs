// =============================================================================
// Location Data Cache Implementation
// Caches pre-resolved location context data with TTL for actor behavior execution.
// Owned by lib-location per service hierarchy (L2).
// =============================================================================

using BeyondImmersion.Bannou.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Location.Caching;

/// <summary>
/// Caches location context data for actor behavior execution.
/// Uses ConcurrentDictionary for thread-safety (IMPLEMENTATION TENETS compliant).
/// Loads data via ILocationClient and IRealmClient through mesh, matching the established provider cache pattern.
/// </summary>
public sealed class LocationDataCache : ILocationDataCache
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LocationDataCache> _logger;
    private readonly ConcurrentDictionary<Guid, CachedEntry> _cache = new();
    private readonly TimeSpan _cacheTtl;
    private readonly int _nearbyPoisLimit;

    /// <summary>
    /// Creates a new location data cache.
    /// </summary>
    public LocationDataCache(
        IServiceScopeFactory scopeFactory,
        ILogger<LocationDataCache> logger,
        LocationServiceConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _cacheTtl = TimeSpan.FromSeconds(configuration.ContextCacheTtlSeconds);
        _nearbyPoisLimit = configuration.ContextNearbyPoisLimit;
    }

    /// <inheritdoc/>
    public async Task<LocationContextData?> GetOrLoadLocationContextAsync(
        Guid characterId,
        Guid realmId,
        Guid? locationId,
        CancellationToken ct = default)
    {
        // Check cache first (keyed by characterId for consistency)
        if (_cache.TryGetValue(characterId, out var cached) && !cached.IsExpired)
        {
            _logger.LogDebug("Location context cache hit for character {CharacterId}", characterId);
            return cached.Data;
        }

        // Load from service
        _logger.LogDebug("Location context cache miss for character {CharacterId}, loading from service", characterId);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var locationClient = scope.ServiceProvider.GetRequiredService<ILocationClient>();
            var realmClient = scope.ServiceProvider.GetRequiredService<Realm.IRealmClient>();

            Guid resolvedLocationId;

            if (locationId.HasValue)
            {
                // Optimization: locationId known from perception events, skip entity-location lookup
                resolvedLocationId = locationId.Value;
                _logger.LogDebug(
                    "Using known locationId {LocationId} for character {CharacterId} (skipped entity-location lookup)",
                    resolvedLocationId, characterId);
            }
            else
            {
                // Fallback: look up where this character is via entity-location API
                var entityLocationResponse = await locationClient.GetEntityLocationAsync(
                    new GetEntityLocationRequest
                    {
                        EntityType = "character",
                        EntityId = characterId
                    }, ct);

                if (entityLocationResponse is null || !entityLocationResponse.Found || !entityLocationResponse.LocationId.HasValue)
                {
                    _logger.LogDebug("Character {CharacterId} has no current location", characterId);
                    return null;
                }

                resolvedLocationId = entityLocationResponse.LocationId.Value;
            }

            // Step 2: Get location details
            var locationResponse = await locationClient.GetLocationAsync(
                new GetLocationRequest { LocationId = resolvedLocationId }, ct);

            if (locationResponse is null)
            {
                _logger.LogWarning("Location {LocationId} not found for character {CharacterId}", resolvedLocationId, characterId);
                return null;
            }

            // Step 3: Find nearest REGION-typed ancestor
            string? regionCode = null;
            var ancestorsResponse = await locationClient.GetLocationAncestorsAsync(
                new GetLocationAncestorsRequest { LocationId = resolvedLocationId }, ct);

            if (ancestorsResponse?.Locations is not null)
            {
                var regionAncestor = ancestorsResponse.Locations
                    .FirstOrDefault(a => a.LocationType == LocationType.REGION);
                regionCode = regionAncestor?.Code;
            }

            // Step 4: Get sibling location codes (nearby POIs)
            var nearbyPois = new List<string>();
            if (locationResponse.ParentLocationId.HasValue)
            {
                var siblingsResponse = await locationClient.ListLocationsByParentAsync(
                    new ListLocationsByParentRequest
                    {
                        ParentLocationId = locationResponse.ParentLocationId.Value,
                        PageSize = _nearbyPoisLimit
                    }, ct);

                if (siblingsResponse?.Locations is not null)
                {
                    nearbyPois = siblingsResponse.Locations
                        .Where(l => l.LocationId != resolvedLocationId && !l.IsDeprecated)
                        .Select(l => l.Code)
                        .ToList();
                }
            }

            // Step 5: Get entity count at this location
            var entitiesResponse = await locationClient.ListEntitiesAtLocationAsync(
                new ListEntitiesAtLocationRequest
                {
                    LocationId = resolvedLocationId,
                    PageSize = 1 // We only need the totalCount
                }, ct);

            var entityCount = entitiesResponse?.TotalCount ?? 0;

            // Step 6: Get realm code
            var realmResponse = await realmClient.GetRealmAsync(
                new Realm.GetRealmRequest { RealmId = realmId }, ct);

            var realmCode = realmResponse?.Code
                ?? throw new InvalidOperationException(
                    $"Realm {realmId} not found for location {resolvedLocationId}");

            // Build and cache the context data
            var data = new LocationContextData(
                Zone: locationResponse.Code,
                Name: locationResponse.Name,
                Region: regionCode,
                Type: locationResponse.LocationType,
                Depth: locationResponse.Depth,
                Realm: realmCode,
                NearbyPois: nearbyPois,
                EntityCount: entityCount);

            _cache[characterId] = new CachedEntry(data, DateTimeOffset.UtcNow.Add(_cacheTtl));

            _logger.LogDebug(
                "Cached location context for character {CharacterId}: zone={Zone}, realm={Realm}, entityCount={EntityCount}",
                characterId, data.Zone, data.Realm, data.EntityCount);

            return data;
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            // Character has no location or location not found - valid state
            _logger.LogDebug("Location context not found (404) for character {CharacterId}", characterId);
            return null;
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Location API returned {StatusCode} for character {CharacterId}",
                ex.StatusCode, characterId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load location context for character {CharacterId}", characterId);
            throw;
        }
    }

    /// <inheritdoc/>
    public void Invalidate(Guid characterId)
    {
        _cache.TryRemove(characterId, out _);
        _logger.LogDebug("Invalidated location context cache for character {CharacterId}", characterId);
    }

    /// <inheritdoc/>
    public void InvalidateAll()
    {
        _cache.Clear();
        _logger.LogInformation("Cleared all location context cache entries");
    }

    /// <summary>
    /// Cached location context data with expiration time.
    /// </summary>
    private sealed record CachedEntry(LocationContextData? Data, DateTimeOffset ExpiresAt)
    {
        public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;
    }
}
