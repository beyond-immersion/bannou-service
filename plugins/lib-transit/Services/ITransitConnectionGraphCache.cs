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
/// </remarks>
public interface ITransitConnectionGraphCache
{
    /// <summary>
    /// Gets the cached connection graph for a realm, rebuilding from MySQL on cache miss.
    /// </summary>
    /// <param name="realmId">The realm to get the graph for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of connection graph entries representing the adjacency list for this realm.</returns>
    Task<IReadOnlyList<ConnectionGraphEntry>> GetGraphAsync(Guid realmId, CancellationToken ct);

    /// <summary>
    /// Invalidates the cached graph for the specified realm(s), forcing a rebuild on next access.
    /// </summary>
    /// <param name="realmIds">The realm IDs whose graphs should be invalidated.</param>
    /// <param name="ct">Cancellation token.</param>
    Task InvalidateAsync(IEnumerable<Guid> realmIds, CancellationToken ct);
}
