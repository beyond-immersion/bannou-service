using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Permission;

/// <summary>
/// DI Listener for session activity events from Connect service.
/// Registered as Singleton in DI; discovered by Connect via <c>IEnumerable&lt;ISessionActivityListener&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Distributed Safety:</b> This is a DI Listener (push pattern) with local-only fan-out.
/// All listener reactions write to distributed state (Redis): TTL refresh via EXPIRE,
/// session state setup via state store, and active connection set mutations. All nodes
/// see updated state on their next read.
/// </para>
/// <para>
/// <b>Why a separate class?</b> Follows the established DI listener pattern
/// (see StatusSeedEvolutionListener, FactionSeedEvolutionListener). Keeps listener
/// concerns (TTL refresh, delegation to service methods) separate from the main
/// PermissionService business logic.
/// </para>
/// <para>
/// This listener replaces the <c>session.connected</c> and <c>session.disconnected</c>
/// event subscriptions that Permission previously used. Since Connect and Permission are
/// both L1 AppFoundation (always co-located), the DI listener provides guaranteed delivery
/// with zero event bus overhead. The <c>session.updated</c> subscription (from Auth service)
/// is retained as it comes from a different source.
/// </para>
/// </remarks>
public class PermissionSessionActivityListener : ISessionActivityListener
{
    private readonly PermissionService _permissionService;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly PermissionServiceConfiguration _configuration;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly ILogger<PermissionSessionActivityListener> _logger;

    /// <summary>
    /// Redis key prefix for the permission state store.
    /// IRedisOperations works with raw (unprefixed) Redis keys,
    /// so we must manually apply the store's key prefix.
    /// </summary>
    private const string REDIS_KEY_PREFIX = "permission";

    /// <summary>
    /// Key pattern for session states, with session ID placeholder.
    /// Format matches PermissionService.SESSION_STATES_KEY.
    /// </summary>
    private const string SESSION_STATES_KEY = "session:{0}:states";

    /// <summary>
    /// Key pattern for session permissions, with session ID placeholder.
    /// Format matches PermissionService.SESSION_PERMISSIONS_KEY.
    /// </summary>
    private const string SESSION_PERMISSIONS_KEY = "session:{0}:permissions";

    /// <summary>
    /// Initializes the session activity listener for permission TTL management and session lifecycle handling.
    /// </summary>
    /// <param name="permissionService">The permission service for session lifecycle delegation.</param>
    /// <param name="stateStoreFactory">Factory for accessing Redis operations.</param>
    /// <param name="configuration">Permission service configuration (for TTL values).</param>
    /// <param name="telemetryProvider">Telemetry provider for span creation.</param>
    /// <param name="logger">Logger instance.</param>
    public PermissionSessionActivityListener(
        IPermissionService permissionService,
        IStateStoreFactory stateStoreFactory,
        PermissionServiceConfiguration configuration,
        ITelemetryProvider telemetryProvider,
        ILogger<PermissionSessionActivityListener> logger)
    {
        // PermissionService is the only IPermissionService implementation and is always Singleton.
        // Cast is safe within the same plugin assembly.
        _permissionService = (PermissionService)permissionService;
        _stateStoreFactory = stateStoreFactory;
        _configuration = configuration;
        _telemetryProvider = telemetryProvider;
        _logger = logger;
    }

    /// <summary>
    /// Refreshes Redis TTL on session data keys.
    /// O(1) Redis EXPIRE — no data read/write needed.
    /// </summary>
    public async Task OnHeartbeatAsync(Guid sessionId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.permission", "PermissionSessionActivityListener.OnHeartbeat");

        if (_configuration.SessionDataTtlSeconds <= 0)
        {
            // TTL disabled — nothing to refresh
            return;
        }

        var redisOps = _stateStoreFactory.GetRedisOperations();
        if (redisOps == null)
        {
            // InMemory mode — no TTL to refresh
            return;
        }

        var sessionIdStr = sessionId.ToString();
        var ttl = TimeSpan.FromSeconds(_configuration.SessionDataTtlSeconds);

        // Raw Redis keys: {prefix}:{key}
        var statesKey = $"{REDIS_KEY_PREFIX}:{string.Format(SESSION_STATES_KEY, sessionIdStr)}";
        var permissionsKey = $"{REDIS_KEY_PREFIX}:{string.Format(SESSION_PERMISSIONS_KEY, sessionIdStr)}";

        await redisOps.ExpireAsync(statesKey, ttl, ct);
        await redisOps.ExpireAsync(permissionsKey, ttl, ct);

        _logger.LogDebug("Refreshed TTL on session data keys for session {SessionId}", sessionId);
    }

    /// <summary>
    /// Delegates to PermissionService to set up session state and compile initial capabilities.
    /// </summary>
    public async Task OnConnectedAsync(
        Guid sessionId,
        Guid accountId,
        IReadOnlyList<string>? roles,
        IReadOnlyList<string>? authorizations,
        CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.permission", "PermissionSessionActivityListener.OnConnected");

        _logger.LogDebug("Session activity listener: session connected {SessionId}, account {AccountId}",
            sessionId, accountId);

        var result = await _permissionService.HandleSessionConnectedAsync(
            sessionId.ToString(),
            accountId.ToString(),
            (ICollection<string>?)roles?.ToList(),
            (ICollection<string>?)authorizations?.ToList(),
            ct);

        if (result.Item1 != StatusCodes.OK)
        {
            _logger.LogWarning("HandleSessionConnectedAsync returned {StatusCode} for session {SessionId}",
                result.Item1, sessionId);
        }
    }

    /// <summary>
    /// Re-adds session to active connections, refreshes TTL, and recompiles from existing Redis state.
    /// </summary>
    public async Task OnReconnectedAsync(Guid sessionId, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.permission", "PermissionSessionActivityListener.OnReconnected");

        _logger.LogDebug("Session activity listener: session reconnected {SessionId}", sessionId);

        await _permissionService.RecompileForReconnectionAsync(sessionId.ToString(), ct);

        // Refresh TTL on session data keys after reconnection
        await RefreshSessionTtlAsync(sessionId, ct);
    }

    /// <summary>
    /// Delegates to PermissionService for disconnect handling with TTL alignment.
    /// When reconnectable, aligns Redis TTL to Connect's actual reconnection window.
    /// </summary>
    public async Task OnDisconnectedAsync(
        Guid sessionId,
        bool reconnectable,
        TimeSpan? reconnectionWindow,
        CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.permission", "PermissionSessionActivityListener.OnDisconnected");

        _logger.LogDebug("Session activity listener: session disconnected {SessionId}, reconnectable: {Reconnectable}",
            sessionId, reconnectable);

        var result = await _permissionService.HandleSessionDisconnectedAsync(
            sessionId.ToString(),
            reconnectable,
            ct);

        if (result.Item1 != StatusCodes.OK)
        {
            _logger.LogWarning("HandleSessionDisconnectedAsync returned {StatusCode} for session {SessionId}",
                result.Item1, sessionId);
        }

        // Align Redis TTL to reconnection window when session can reconnect
        if (reconnectable && reconnectionWindow.HasValue)
        {
            await AlignSessionTtlAsync(sessionId, reconnectionWindow.Value, ct);
        }
    }

    /// <summary>
    /// Refreshes TTL on session data keys using the configured SessionDataTtlSeconds.
    /// </summary>
    private async Task RefreshSessionTtlAsync(Guid sessionId, CancellationToken ct)
    {
        if (_configuration.SessionDataTtlSeconds <= 0)
            return;

        var redisOps = _stateStoreFactory.GetRedisOperations();
        if (redisOps == null)
            return;

        var sessionIdStr = sessionId.ToString();
        var ttl = TimeSpan.FromSeconds(_configuration.SessionDataTtlSeconds);

        var statesKey = $"{REDIS_KEY_PREFIX}:{string.Format(SESSION_STATES_KEY, sessionIdStr)}";
        var permissionsKey = $"{REDIS_KEY_PREFIX}:{string.Format(SESSION_PERMISSIONS_KEY, sessionIdStr)}";

        await redisOps.ExpireAsync(statesKey, ttl, ct);
        await redisOps.ExpireAsync(permissionsKey, ttl, ct);
    }

    /// <summary>
    /// Aligns session data TTL to the reconnection window duration.
    /// This ensures Permission's data lifecycle matches Connect's actual reconnection timeout
    /// rather than using the blanket SessionDataTtlSeconds.
    /// </summary>
    private async Task AlignSessionTtlAsync(Guid sessionId, TimeSpan reconnectionWindow, CancellationToken ct)
    {
        var redisOps = _stateStoreFactory.GetRedisOperations();
        if (redisOps == null)
            return;

        var sessionIdStr = sessionId.ToString();
        var statesKey = $"{REDIS_KEY_PREFIX}:{string.Format(SESSION_STATES_KEY, sessionIdStr)}";
        var permissionsKey = $"{REDIS_KEY_PREFIX}:{string.Format(SESSION_PERMISSIONS_KEY, sessionIdStr)}";

        await redisOps.ExpireAsync(statesKey, reconnectionWindow, ct);
        await redisOps.ExpireAsync(permissionsKey, reconnectionWindow, ct);

        _logger.LogDebug("Aligned session data TTL to reconnection window ({WindowSeconds}s) for session {SessionId}",
            reconnectionWindow.TotalSeconds, sessionId);
    }
}
