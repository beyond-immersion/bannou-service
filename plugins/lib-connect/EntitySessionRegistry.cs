using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Connect;

/// <summary>
/// Redis-backed entity session registry mapping (entityType, entityId) to sets of session IDs.
/// Uses dual indexes (forward: entity to sessions, reverse: session to entities) with atomic
/// Redis set operations, matching the existing account-sessions pattern in BannouSessionManager.
/// </summary>
/// <remarks>
/// <para>
/// Registered as Singleton because ConnectService is Singleton and cannot consume scoped services.
/// All state is in Redis (distributed) -- safe for multi-node deployments per IMPLEMENTATION TENETS.
/// </para>
/// <para>
/// Error handling follows the BannouSessionManager convention: errors are logged, error events
/// are published via <c>TryPublishErrorAsync</c>, but exceptions are NOT thrown from cleanup
/// operations. Query operations return empty sets on error so callers degrade gracefully.
/// </para>
/// </remarks>
public class EntitySessionRegistry : IEntitySessionRegistry
{
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IMessageBus _messageBus;
    private readonly IClientEventPublisher _clientEventPublisher;
    private readonly ConnectServiceConfiguration _configuration;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly ILogger<EntitySessionRegistry> _logger;

    // Forward index: entity -> sessions
    private const string ENTITY_SESSIONS_KEY_PREFIX = "entity-sessions:";

    // Reverse index: session -> entities (for disconnect cleanup)
    private const string SESSION_ENTITIES_KEY_PREFIX = "session-entities:";

    // Heartbeat key prefix — single source of truth in BannouSessionManager
    private const string SESSION_HEARTBEAT_KEY_PREFIX = BannouSessionManager.SESSION_HEARTBEAT_KEY_PREFIX;

    /// <summary>
    /// Creates a new EntitySessionRegistry with the specified infrastructure services.
    /// </summary>
    /// <param name="stateStoreFactory">State store factory for Redis access.</param>
    /// <param name="messageBus">Message bus for error event publication.</param>
    /// <param name="clientEventPublisher">Client event publisher for WebSocket push delivery.</param>
    /// <param name="configuration">Connect service configuration (provides SessionTtlSeconds).</param>
    /// <param name="telemetryProvider">Telemetry provider for distributed tracing spans.</param>
    /// <param name="logger">Logger instance.</param>
    public EntitySessionRegistry(
        IStateStoreFactory stateStoreFactory,
        IMessageBus messageBus,
        IClientEventPublisher clientEventPublisher,
        ConnectServiceConfiguration configuration,
        ITelemetryProvider telemetryProvider,
        ILogger<EntitySessionRegistry> logger)
    {
        ArgumentNullException.ThrowIfNull(stateStoreFactory, nameof(stateStoreFactory));
        ArgumentNullException.ThrowIfNull(messageBus, nameof(messageBus));
        ArgumentNullException.ThrowIfNull(clientEventPublisher, nameof(clientEventPublisher));
        ArgumentNullException.ThrowIfNull(configuration, nameof(configuration));
        ArgumentNullException.ThrowIfNull(telemetryProvider, nameof(telemetryProvider));
        ArgumentNullException.ThrowIfNull(logger, nameof(logger));

        _stateStoreFactory = stateStoreFactory;
        _messageBus = messageBus;
        _clientEventPublisher = clientEventPublisher;
        _configuration = configuration;
        _telemetryProvider = telemetryProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task RegisterAsync(string entityType, Guid entityId, string sessionId, CancellationToken ct = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.connect", "EntitySessionRegistry.RegisterAsync");
        _logger.LogDebug("Registering session {SessionId} for entity {EntityType}:{EntityId}",
            sessionId, entityType, entityId);

        try
        {
            var forwardKey = BuildForwardKey(entityType, entityId);
            var reverseKey = BuildReverseKey(sessionId);
            var reverseValue = BuildReverseValue(entityType, entityId);

            var cacheStore = _stateStoreFactory.GetCacheableStore<string>(StateStoreDefinitions.Connect);
            var ttlSeconds = _configuration.SessionTtlSeconds;

            // Atomic SADD on both indexes
            await cacheStore.AddToSetAsync(forwardKey, sessionId,
                new StateOptions { Ttl = ttlSeconds }, ct);
            await cacheStore.AddToSetAsync(reverseKey, reverseValue,
                new StateOptions { Ttl = ttlSeconds }, ct);

            _logger.LogDebug("Registered session {SessionId} for entity {EntityType}:{EntityId}",
                sessionId, entityType, entityId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register session {SessionId} for entity {EntityType}:{EntityId}",
                sessionId, entityType, entityId);
            await _messageBus.TryPublishErrorAsync(
                "connect",
                "EntitySessionRegistry.Register",
                ex.GetType().Name,
                ex.Message,
                dependency: "state",
                details: new { entityType, entityId, sessionId },
                stack: ex.StackTrace);
            // Don't throw -- registration failures shouldn't break the caller's flow
        }
    }

    /// <inheritdoc />
    public async Task UnregisterAsync(string entityType, Guid entityId, string sessionId, CancellationToken ct = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.connect", "EntitySessionRegistry.UnregisterAsync");
        _logger.LogDebug("Unregistering session {SessionId} from entity {EntityType}:{EntityId}",
            sessionId, entityType, entityId);

        try
        {
            var forwardKey = BuildForwardKey(entityType, entityId);
            var reverseKey = BuildReverseKey(sessionId);
            var reverseValue = BuildReverseValue(entityType, entityId);

            var cacheStore = _stateStoreFactory.GetCacheableStore<string>(StateStoreDefinitions.Connect);

            // Atomic SREM on both indexes
            await cacheStore.RemoveFromSetAsync(forwardKey, sessionId, ct);
            await cacheStore.RemoveFromSetAsync(reverseKey, reverseValue, ct);

            // Clean up empty forward set
            var remaining = await cacheStore.SetCountAsync(forwardKey, ct);
            if (remaining == 0)
            {
                await cacheStore.DeleteSetAsync(forwardKey, ct);
                _logger.LogDebug("Removed last session from entity {EntityType}:{EntityId} index, deleted key",
                    entityType, entityId);
            }
            else
            {
                _logger.LogDebug("Unregistered session {SessionId} from entity {EntityType}:{EntityId} (remaining: {Count})",
                    sessionId, entityType, entityId, remaining);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unregister session {SessionId} from entity {EntityType}:{EntityId}",
                sessionId, entityType, entityId);
            await _messageBus.TryPublishErrorAsync(
                "connect",
                "EntitySessionRegistry.Unregister",
                ex.GetType().Name,
                ex.Message,
                dependency: "state",
                details: new { entityType, entityId, sessionId },
                stack: ex.StackTrace);
            // Don't throw -- unregistration failures shouldn't break the caller's flow
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> GetSessionsForEntityAsync(string entityType, Guid entityId, CancellationToken ct = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.connect", "EntitySessionRegistry.GetSessionsForEntityAsync");
        _logger.LogDebug("Getting sessions for entity {EntityType}:{EntityId}", entityType, entityId);

        try
        {
            var forwardKey = BuildForwardKey(entityType, entityId);
            var cacheStore = _stateStoreFactory.GetCacheableStore<string>(StateStoreDefinitions.Connect);

            // Get all session IDs from the atomic Redis set
            var sessions = await cacheStore.GetSetAsync<string>(forwardKey, ct);
            if (sessions.Count == 0)
            {
                return new HashSet<string>();
            }

            // Filter stale sessions by cross-referencing heartbeat data.
            // Heartbeat keys have a TTL of HeartbeatTtlSeconds (default 5 min) and are updated
            // every 30 seconds during active connections. Missing heartbeat = dead session.
            var heartbeatStore = _stateStoreFactory.GetStore<SessionHeartbeat>(StateStoreDefinitions.Connect);
            var liveSessions = new HashSet<string>();
            var staleSessions = new List<string>();

            foreach (var sessionId in sessions)
            {
                var heartbeatKey = SESSION_HEARTBEAT_KEY_PREFIX + sessionId;
                var heartbeat = await heartbeatStore.GetAsync(heartbeatKey, ct);
                if (heartbeat != null)
                {
                    liveSessions.Add(sessionId);
                }
                else
                {
                    staleSessions.Add(sessionId);
                }
            }

            // Lazily clean up stale entries from the forward index (best-effort, per-item)
            if (staleSessions.Count > 0)
            {
                _logger.LogDebug("Cleaning {StaleCount} stale sessions from entity {EntityType}:{EntityId} index",
                    staleSessions.Count, entityType, entityId);
                foreach (var staleSessionId in staleSessions)
                {
                    try
                    {
                        await cacheStore.RemoveFromSetAsync(forwardKey, staleSessionId, ct);
                    }
                    catch (Exception ex)
                    {
                        // Best-effort: stale cleanup failure must not discard valid liveSessions
                        _logger.LogWarning(ex, "Failed to remove stale session {SessionId} from entity {EntityType}:{EntityId} index",
                            staleSessionId, entityType, entityId);
                    }
                }
            }

            return liveSessions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get sessions for entity {EntityType}:{EntityId}",
                entityType, entityId);
            await _messageBus.TryPublishErrorAsync(
                "connect",
                "EntitySessionRegistry.GetSessionsForEntity",
                ex.GetType().Name,
                ex.Message,
                dependency: "state",
                details: new { entityType, entityId },
                stack: ex.StackTrace);
            // Return empty set on error -- callers degrade gracefully
            return new HashSet<string>();
        }
    }

    /// <inheritdoc />
    public async Task<int> PublishToEntitySessionsAsync<TEvent>(string entityType, Guid entityId, TEvent eventData, CancellationToken ct = default)
        where TEvent : BaseClientEvent
    {
        using var activity = _telemetryProvider.StartActivity("bannou.connect", "EntitySessionRegistry.PublishToEntitySessionsAsync");
        try
        {
            var forwardKey = BuildForwardKey(entityType, entityId);
            var cacheStore = _stateStoreFactory.GetCacheableStore<string>(StateStoreDefinitions.Connect);

            var sessions = await cacheStore.GetSetAsync<string>(forwardKey, ct);
            if (sessions.Count == 0)
            {
                return 0;
            }

            var published = await _clientEventPublisher.PublishToSessionsAsync(sessions, eventData, ct);

            _logger.LogDebug("Published {EventName} to {Published}/{Total} sessions for entity {EntityType}:{EntityId}",
                eventData.EventName, published, sessions.Count, entityType, entityId);

            return published;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish {EventName} to sessions for entity {EntityType}:{EntityId}",
                eventData.EventName, entityType, entityId);
            await _messageBus.TryPublishErrorAsync(
                "connect",
                "EntitySessionRegistry.PublishToEntitySessions",
                ex.GetType().Name,
                ex.Message,
                dependency: "messaging",
                details: new { entityType, entityId, eventName = eventData.EventName },
                stack: ex.StackTrace);
            return 0;
        }
    }

    /// <inheritdoc />
    public async Task UnregisterSessionAsync(string sessionId, CancellationToken ct = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.connect", "EntitySessionRegistry.UnregisterSessionAsync");
        _logger.LogDebug("Unregistering all entity bindings for session {SessionId}", sessionId);

        try
        {
            var reverseKey = BuildReverseKey(sessionId);
            var cacheStore = _stateStoreFactory.GetCacheableStore<string>(StateStoreDefinitions.Connect);

            // Get all entity bindings for this session from the reverse index
            var entityBindings = await cacheStore.GetSetAsync<string>(reverseKey, ct);
            if (entityBindings.Count == 0)
            {
                _logger.LogDebug("No entity bindings to clean up for session {SessionId}", sessionId);
                return;
            }

            _logger.LogDebug("Cleaning up {BindingCount} entity bindings for disconnected session {SessionId}",
                entityBindings.Count, sessionId);

            // Remove the session from each entity's forward index
            foreach (var binding in entityBindings)
            {
                try
                {
                    var (entityType, entityId) = ParseReverseValue(binding);
                    var forwardKey = BuildForwardKey(entityType, entityId);

                    await cacheStore.RemoveFromSetAsync(forwardKey, sessionId, ct);

                    // Clean up empty forward set
                    var remaining = await cacheStore.SetCountAsync(forwardKey, ct);
                    if (remaining == 0)
                    {
                        await cacheStore.DeleteSetAsync(forwardKey, ct);
                    }
                }
                catch (Exception ex)
                {
                    // Best-effort: log and continue with remaining bindings
                    _logger.LogWarning(ex, "Failed to remove session {SessionId} from entity binding {Binding}",
                        sessionId, binding);
                }
            }

            // Delete the reverse index key
            await cacheStore.DeleteSetAsync(reverseKey, ct);

            _logger.LogDebug("Completed entity binding cleanup for session {SessionId}, removed {Count} bindings",
                sessionId, entityBindings.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clean up entity bindings for session {SessionId}", sessionId);
            await _messageBus.TryPublishErrorAsync(
                "connect",
                "EntitySessionRegistry.UnregisterSession",
                ex.GetType().Name,
                ex.Message,
                dependency: "state",
                details: new { sessionId },
                stack: ex.StackTrace);
            // Don't throw -- cleanup failures shouldn't break the disconnect flow
        }
    }

    /// <summary>
    /// Builds the forward index key: entity-sessions:{entityType}:{entityId:N}.
    /// </summary>
    private static string BuildForwardKey(string entityType, Guid entityId)
        => $"{ENTITY_SESSIONS_KEY_PREFIX}{entityType}:{entityId:N}";

    /// <summary>
    /// Builds the reverse index key: session-entities:{sessionId}.
    /// </summary>
    private static string BuildReverseKey(string sessionId)
        => SESSION_ENTITIES_KEY_PREFIX + sessionId;

    /// <summary>
    /// Builds the reverse index value: "{entityType}:{entityId:N}".
    /// </summary>
    private static string BuildReverseValue(string entityType, Guid entityId)
        => $"{entityType}:{entityId:N}";

    /// <summary>
    /// Parses a reverse index value back into (entityType, entityId).
    /// </summary>
    private static (string entityType, Guid entityId) ParseReverseValue(string value)
    {
        var separatorIndex = value.LastIndexOf(':');
        if (separatorIndex < 0)
        {
            throw new FormatException($"Invalid entity binding format: {value}");
        }

        var entityType = value[..separatorIndex];
        var entityId = Guid.Parse(value[(separatorIndex + 1)..]);
        return (entityType, entityId);
    }
}
