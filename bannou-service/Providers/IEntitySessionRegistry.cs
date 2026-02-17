namespace BeyondImmersion.BannouService.Providers;

/// <summary>
/// Registry for mapping entities to WebSocket session IDs.
/// Hosted by Connect (L1). Higher-layer services register entity-to-session
/// bindings; entity-based services query the registry to route client events.
/// </summary>
/// <remarks>
/// <para>
/// Uses dual Redis indexes: forward (<c>entity-sessions:{entityType}:{entityId}</c> to Set of sessionId)
/// and reverse (<c>session-entities:{sessionId}</c> to Set of <c>"{entityType}:{entityId}"</c>).
/// Both use atomic SADD/SREM operations matching the existing account-sessions pattern.
/// </para>
/// <para>
/// The reverse index enables O(n) cleanup on disconnect: sweep all entity bindings for a session
/// in one pass via <see cref="UnregisterSessionAsync"/>.
/// </para>
/// <para>
/// Stale session filtering uses heartbeat cross-reference when querying entity sessions,
/// matching the <c>GetSessionsForAccountAsync</c> pattern in BannouSessionManager.
/// </para>
/// <para>
/// <b>Distributed safety</b>: This is a pull-based registry (entity-based services query it on demand).
/// All state lives in Redis (distributed). Safe for multi-node deployments per IMPLEMENTATION TENETS.
/// </para>
/// </remarks>
public interface IEntitySessionRegistry
{
    /// <summary>
    /// Register a session as interested in an entity.
    /// Adds the session to the forward index (entity to sessions) and the entity to the
    /// reverse index (session to entities) using atomic Redis SADD operations.
    /// </summary>
    /// <param name="entityType">The entity type (opaque string, e.g., "character", "seed", "inventory").</param>
    /// <param name="entityId">The entity identifier.</param>
    /// <param name="sessionId">The WebSocket session ID to register.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RegisterAsync(string entityType, Guid entityId, string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Unregister a session from an entity.
    /// Removes the session from the forward index and the entity from the reverse index
    /// using atomic Redis SREM operations.
    /// </summary>
    /// <param name="entityType">The entity type (opaque string).</param>
    /// <param name="entityId">The entity identifier.</param>
    /// <param name="sessionId">The WebSocket session ID to unregister.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UnregisterAsync(string entityType, Guid entityId, string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Get all sessions interested in an entity.
    /// Filters stale sessions via heartbeat cross-reference and lazily cleans up dead entries.
    /// </summary>
    /// <param name="entityType">The entity type (opaque string).</param>
    /// <param name="entityId">The entity identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Set of live session IDs registered for the entity. Empty set if none.</returns>
    Task<IReadOnlySet<string>> GetSessionsForEntityAsync(string entityType, Guid entityId, CancellationToken ct = default);

    /// <summary>
    /// Unregister a session from ALL entity bindings (called on disconnect).
    /// Sweeps the reverse index to find all entity bindings for the session, removes the session
    /// from each forward index, then deletes the reverse index key.
    /// </summary>
    /// <param name="sessionId">The WebSocket session ID being disconnected.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UnregisterSessionAsync(string sessionId, CancellationToken ct = default);
}
