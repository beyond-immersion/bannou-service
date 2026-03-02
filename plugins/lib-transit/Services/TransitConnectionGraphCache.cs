using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Transit;

/// <summary>
/// Manages per-realm connection graph caches in Redis for efficient route calculation.
/// Rebuilds from MySQL on cache miss. Invalidated on connection create/delete/status-change.
/// </summary>
/// <remarks>
/// <para>
/// The connection graph cache stores per-realm adjacency lists in Redis
/// (<c>transit-connection-graph</c> store) with a configurable TTL from
/// <c>ConnectionGraphCacheSeconds</c>. Cross-realm connections appear in
/// both realms' cached graphs.
/// </para>
/// <para>
/// <b>Cache Invalidation</b>: Call <see cref="InvalidateAsync"/> whenever a connection
/// is created, deleted, or changes status. The next read will rebuild from MySQL.
/// </para>
/// <para>
/// <b>IMPLEMENTATION TENETS</b>: Telemetry spans on all async methods. Structured logging
/// with message templates. Uses IStateStoreFactory for state access.
/// </para>
/// </remarks>
internal sealed class TransitConnectionGraphCache : ITransitConnectionGraphCache
{
    private readonly IStateStore<List<ConnectionGraphEntry>> _connectionGraphStore;
    private readonly IQueryableStateStore<TransitConnectionModel> _connectionsStore;
    private readonly TransitServiceConfiguration _configuration;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly ILogger<TransitConnectionGraphCache> _logger;

    private const string GRAPH_KEY_PREFIX = "graph:";

    /// <summary>
    /// Creates a new instance of the TransitConnectionGraphCache.
    /// </summary>
    /// <param name="stateStoreFactory">Factory for creating state stores.</param>
    /// <param name="configuration">Transit service configuration for cache TTL settings.</param>
    /// <param name="telemetryProvider">Telemetry provider for distributed tracing spans.</param>
    /// <param name="logger">Structured logger.</param>
    public TransitConnectionGraphCache(
        IStateStoreFactory stateStoreFactory,
        TransitServiceConfiguration configuration,
        ITelemetryProvider telemetryProvider,
        ILogger<TransitConnectionGraphCache> logger)
    {
        _connectionGraphStore = stateStoreFactory.GetStore<List<ConnectionGraphEntry>>(StateStoreDefinitions.TransitConnectionGraph);
        _connectionsStore = stateStoreFactory.GetQueryableStore<TransitConnectionModel>(StateStoreDefinitions.TransitConnections);
        _configuration = configuration;
        _telemetryProvider = telemetryProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ConnectionGraphEntry>> GetGraphAsync(Guid realmId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.transit", "TransitConnectionGraphCache.GetGraphAsync");

        var key = BuildGraphKey(realmId);

        // Attempt to read from Redis cache
        var cached = await _connectionGraphStore.GetAsync(key, ct);
        if (cached != null)
        {
            _logger.LogDebug("Connection graph cache hit for realm {RealmId}, {EntryCount} entries",
                realmId, cached.Count);
            return cached;
        }

        // Cache miss: rebuild from MySQL
        _logger.LogDebug("Connection graph cache miss for realm {RealmId}, rebuilding from MySQL", realmId);
        var graph = await RebuildGraphForRealmAsync(realmId, ct);

        // Store in Redis with TTL if caching is enabled
        if (_configuration.ConnectionGraphCacheSeconds > 0)
        {
            await _connectionGraphStore.SaveAsync(
                key,
                graph,
                new StateOptions { Ttl = _configuration.ConnectionGraphCacheSeconds },
                ct);

            _logger.LogDebug("Cached connection graph for realm {RealmId} with TTL {TtlSeconds}s, {EntryCount} entries",
                realmId, _configuration.ConnectionGraphCacheSeconds, graph.Count);
        }

        return graph;
    }

    /// <inheritdoc/>
    public async Task InvalidateAsync(IEnumerable<Guid> realmIds, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.transit", "TransitConnectionGraphCache.InvalidateAsync");

        foreach (var realmId in realmIds)
        {
            var key = BuildGraphKey(realmId);
            await _connectionGraphStore.DeleteAsync(key, ct);
            _logger.LogDebug("Invalidated connection graph cache for realm {RealmId}", realmId);
        }
    }

    /// <summary>
    /// Rebuilds the adjacency list for a realm from the MySQL connections store.
    /// Includes connections where fromRealmId or toRealmId matches the realm.
    /// Cross-realm connections appear in both realms' graphs.
    /// </summary>
    /// <param name="realmId">The realm to rebuild the graph for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of graph entries representing the adjacency list.</returns>
    private async Task<List<ConnectionGraphEntry>> RebuildGraphForRealmAsync(Guid realmId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.transit", "TransitConnectionGraphCache.RebuildGraphForRealmAsync");

        // Query all connections that touch this realm (from or to)
        var connections = await _connectionsStore.QueryAsync(
            c => c.FromRealmId == realmId || c.ToRealmId == realmId,
            ct);

        var entries = new List<ConnectionGraphEntry>();

        foreach (var conn in connections)
        {
            // Add forward edge
            entries.Add(new ConnectionGraphEntry
            {
                FromLocationId = conn.FromLocationId,
                ToLocationId = conn.ToLocationId,
                ConnectionId = conn.Id,
                DistanceKm = conn.DistanceKm,
                TerrainType = conn.TerrainType,
                CompatibleModes = conn.CompatibleModes.ToList(),
                BaseRiskLevel = conn.BaseRiskLevel,
                Status = conn.Status,
                Discoverable = conn.Discoverable,
                Code = conn.Code,
                Name = conn.Name,
                SeasonalAvailability = conn.SeasonalAvailability?.Select(s => new SeasonalAvailabilityModel
                {
                    Season = s.Season,
                    Available = s.Available
                }).ToList(),
                WaypointTransferTimeGameHours = null
            });

            // Add reverse edge for bidirectional connections
            if (conn.Bidirectional)
            {
                entries.Add(new ConnectionGraphEntry
                {
                    FromLocationId = conn.ToLocationId,
                    ToLocationId = conn.FromLocationId,
                    ConnectionId = conn.Id,
                    DistanceKm = conn.DistanceKm,
                    TerrainType = conn.TerrainType,
                    CompatibleModes = conn.CompatibleModes.ToList(),
                    BaseRiskLevel = conn.BaseRiskLevel,
                    Status = conn.Status,
                    Discoverable = conn.Discoverable,
                    Code = conn.Code,
                    Name = conn.Name,
                    SeasonalAvailability = conn.SeasonalAvailability?.Select(s => new SeasonalAvailabilityModel
                    {
                        Season = s.Season,
                        Available = s.Available
                    }).ToList(),
                    WaypointTransferTimeGameHours = null
                });
            }
        }

        _logger.LogInformation("Rebuilt connection graph for realm {RealmId}: {ConnectionCount} connections, {EntryCount} graph edges",
            realmId, connections.Count, entries.Count);

        return entries;
    }

    /// <summary>
    /// Builds the Redis key for a realm's cached graph.
    /// </summary>
    /// <param name="realmId">The realm ID.</param>
    /// <returns>The cache key string.</returns>
    private static string BuildGraphKey(Guid realmId) => $"{GRAPH_KEY_PREFIX}{realmId}";
}
