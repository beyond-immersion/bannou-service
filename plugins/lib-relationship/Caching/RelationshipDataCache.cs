// =============================================================================
// Relationship Data Cache Implementation
// Caches character relationship data with TTL.
// Owned by lib-relationship per service hierarchy (L2).
// =============================================================================

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace BeyondImmersion.BannouService.Relationship.Caching;

/// <summary>
/// Caches character relationship data for actor behavior execution.
/// Uses ConcurrentDictionary for thread-safety (IMPLEMENTATION TENETS compliant).
/// </summary>
public sealed class RelationshipDataCache : IRelationshipDataCache
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RelationshipDataCache> _logger;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly TimeSpan _cacheTtl;
    private readonly ConcurrentDictionary<Guid, CachedEntry> _cache = new();

    // Long-lived typeId→code mapping that persists across cache refreshes
    private readonly ConcurrentDictionary<Guid, string> _typeCodeCache = new();

    /// <summary>
    /// Creates a new relationship data cache.
    /// </summary>
    /// <param name="scopeFactory">Service scope factory for creating scoped service clients.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    /// <param name="configuration">Service configuration with cache TTL.</param>
    /// <param name="telemetryProvider">Telemetry provider for distributed tracing.</param>
    public RelationshipDataCache(
        IServiceScopeFactory scopeFactory,
        ILogger<RelationshipDataCache> logger,
        RelationshipServiceConfiguration configuration,
        ITelemetryProvider telemetryProvider)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _telemetryProvider = telemetryProvider;
        _cacheTtl = TimeSpan.FromSeconds(configuration.ProviderCacheTtlSeconds);
    }

    /// <inheritdoc/>
    public async Task<CachedRelationshipData?> GetOrLoadAsync(Guid characterId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.relationship", "RelationshipDataCache.GetOrLoadAsync");

        // Check cache first
        CachedEntry? cached = null;
        if (_cache.TryGetValue(characterId, out cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            return cached.Data;
        }

        _logger.LogDebug("Relationship cache miss for character {CharacterId}, loading from service", characterId);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var relationshipClient = scope.ServiceProvider.GetRequiredService<IRelationshipClient>();

            // Load all active relationships for this character with pagination
            var countsByTypeCode = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var totalCount = 0;
            var page = 1;
            const int pageSize = 100;

            while (true)
            {
                var response = await relationshipClient.ListRelationshipsByEntityAsync(
                    new ListRelationshipsByEntityRequest
                    {
                        EntityId = characterId,
                        EntityType = EntityType.Character,
                        IncludeEnded = false,
                        Page = page,
                        PageSize = pageSize,
                    },
                    ct);

                if (response?.Relationships == null || response.Relationships.Count == 0)
                    break;

                foreach (var rel in response.Relationships)
                {
                    totalCount++;

                    // Resolve typeId to code
                    var code = await ResolveTypeCodeAsync(rel.RelationshipTypeId, relationshipClient, ct);
                    if (code != null)
                    {
                        if (countsByTypeCode.TryGetValue(code, out var existing))
                        {
                            countsByTypeCode[code] = existing + 1;
                        }
                        else
                        {
                            countsByTypeCode[code] = 1;
                        }
                    }
                }

                if (!response.HasNextPage)
                    break;

                page++;
            }

            var data = new CachedRelationshipData
            {
                CountsByTypeCode = countsByTypeCode,
                TotalCount = totalCount,
            };

            _cache[characterId] = new CachedEntry(data, DateTimeOffset.UtcNow.Add(_cacheTtl));
            _logger.LogDebug("Cached relationship data for character {CharacterId}: {TotalCount} relationships, {TypeCount} types",
                characterId, totalCount, countsByTypeCode.Count);

            return data;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load relationship data for character {CharacterId}", characterId);
            // Stale-if-error: return cached data if available
            return cached?.Data;
        }
    }

    /// <inheritdoc/>
    public void Invalidate(Guid characterId)
    {
        _cache.TryRemove(characterId, out _);
        _logger.LogDebug("Invalidated relationship data cache for character {CharacterId}", characterId);
    }

    /// <inheritdoc/>
    public void InvalidateAll()
    {
        _cache.Clear();
        _logger.LogInformation("Cleared all relationship data cache entries");
    }

    /// <summary>
    /// Resolves a relationship type ID to its code string, using the long-lived code cache.
    /// </summary>
    private async Task<string?> ResolveTypeCodeAsync(Guid typeId, IRelationshipClient relationshipClient, CancellationToken ct)
    {
        if (_typeCodeCache.TryGetValue(typeId, out var code))
        {
            return code;
        }

        try
        {
            var typeResponse = await relationshipClient.GetRelationshipTypeAsync(
                new GetRelationshipTypeRequest { RelationshipTypeId = typeId },
                ct);

            if (typeResponse != null)
            {
                _typeCodeCache[typeId] = typeResponse.Code;
                return typeResponse.Code;
            }
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogDebug("Relationship type {TypeId} not found", typeId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve relationship type code for {TypeId}", typeId);
        }

        return null;
    }

    /// <summary>
    /// Cached relationship data entry with expiration time.
    /// </summary>
    private sealed record CachedEntry(CachedRelationshipData Data, DateTimeOffset ExpiresAt);
}
