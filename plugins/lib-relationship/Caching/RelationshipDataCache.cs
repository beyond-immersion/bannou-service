// =============================================================================
// Relationship Data Cache Implementation
// Caches character relationship data with TTL.
// Owned by lib-relationship per service hierarchy (L2).
// =============================================================================

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace BeyondImmersion.BannouService.Relationship.Caching;

/// <summary>
/// Caches character relationship data for actor behavior execution.
/// Uses <see cref="VariableProviderCacheBucket{TKey, TData}"/> for thread-safe
/// TTL-based caching with stale-data fallback (IMPLEMENTATION TENETS compliant).
/// </summary>
[BannouHelperService("relationship-data", typeof(IRelationshipService), typeof(IRelationshipDataCache), lifetime: ServiceLifetime.Singleton)]
public sealed class RelationshipDataCache : IRelationshipDataCache
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RelationshipDataCache> _logger;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly int _queryPageSize;
    private readonly VariableProviderCacheBucket<Guid, CachedRelationshipData> _bucket;

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
        _queryPageSize = configuration.ProviderQueryPageSize;
        _bucket = new VariableProviderCacheBucket<Guid, CachedRelationshipData>(
            TimeSpan.FromSeconds(configuration.ProviderCacheTtlSeconds),
            logger,
            telemetryProvider,
            "bannou.relationship",
            "RelationshipDataCache");
    }

    /// <inheritdoc/>
    public async Task<CachedRelationshipData?> GetOrLoadAsync(Guid characterId, CancellationToken ct)
    {
        return await _bucket.GetOrLoadAsync(characterId, async innerCt =>
        {
            using var scope = _scopeFactory.CreateScope();
            var relationshipClient = scope.ServiceProvider.GetRequiredService<IRelationshipClient>();

            // Load all active relationships for this character with pagination
            var countsByTypeCode = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var totalCount = 0;
            var page = 1;

            while (true)
            {
                var response = await relationshipClient.ListRelationshipsByEntityAsync(
                    new ListRelationshipsByEntityRequest
                    {
                        EntityId = characterId,
                        EntityType = EntityType.Character,
                        IncludeEnded = false,
                        Page = page,
                        PageSize = _queryPageSize,
                    },
                    innerCt);

                if (response?.Relationships == null || response.Relationships.Count == 0)
                    break;

                foreach (var rel in response.Relationships)
                {
                    totalCount++;

                    // Resolve typeId to code
                    var code = await ResolveTypeCodeAsync(rel.RelationshipTypeId, relationshipClient, innerCt);
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

            _logger.LogDebug("Loaded relationship data for character {CharacterId}: {TotalCount} relationships, {TypeCount} types",
                characterId, totalCount, countsByTypeCode.Count);

            return new CachedRelationshipData
            {
                CountsByTypeCode = countsByTypeCode,
                TotalCount = totalCount,
            };
        }, ct);
    }

    /// <inheritdoc/>
    public void Invalidate(Guid characterId)
    {
        _bucket.Invalidate(characterId);
        _logger.LogDebug("Invalidated relationship data cache for character {CharacterId}", characterId);
    }

    /// <inheritdoc/>
    public void InvalidateAll()
    {
        _bucket.InvalidateAll();
        _logger.LogInformation("Cleared all relationship data cache entries");
    }

    /// <summary>
    /// Resolves a relationship type ID to its code string, using the long-lived code cache.
    /// </summary>
    private async Task<string?> ResolveTypeCodeAsync(Guid typeId, IRelationshipClient relationshipClient, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.relationship", "RelationshipDataCache.ResolveTypeCode");

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
}
