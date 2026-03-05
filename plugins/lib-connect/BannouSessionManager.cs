using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Connect;

/// <summary>
/// Session management for distributed WebSocket connection state.
/// Uses Redis state store and message bus for infrastructure access.
/// </summary>
public class BannouSessionManager : ISessionManager
{
    private readonly IMessageBus _messageBus;
    private readonly ConnectServiceConfiguration _configuration;
    private readonly ILogger<BannouSessionManager> _logger;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>State store for connection state data (session lifecycle, reconnection).</summary>
    private readonly IStateStore<ConnectionStateData> _connectionStateStore;

    /// <summary>State store for session heartbeat tracking (TTL-based liveness).</summary>
    private readonly IStateStore<SessionHeartbeat> _heartbeatStore;

    /// <summary>State store for string values (reconnection token mappings).</summary>
    private readonly IStateStore<string> _stringStore;

    /// <summary>Cacheable state store for Redis set operations (account-session indexes).</summary>
    private readonly ICacheableStateStore<string> _cacheableStringStore;

    // Key prefixes - MUST be unique across all services to avoid key collisions
    // (mesh prefixes keys with app-id, not component name, so all components share key namespace)
    private const string SESSION_KEY_PREFIX = "ws-session:";
    internal const string SESSION_HEARTBEAT_KEY_PREFIX = "heartbeat:";
    private const string RECONNECTION_TOKEN_KEY_PREFIX = "reconnect:";
    private const string ACCOUNT_SESSIONS_KEY_PREFIX = "account-sessions:";

    // TTL values now come from configuration (SessionTtlSeconds, HeartbeatTtlSeconds)

    /// <summary>
    /// Creates a new BannouSessionManager with the specified infrastructure services.
    /// </summary>
    public BannouSessionManager(
        IStateStoreFactory stateStoreFactory,
        IMessageBus messageBus,
        ConnectServiceConfiguration configuration,
        ILogger<BannouSessionManager> logger,
        ITelemetryProvider telemetryProvider)
    {
        _connectionStateStore = stateStoreFactory.GetStore<ConnectionStateData>(StateStoreDefinitions.Connect);
        _heartbeatStore = stateStoreFactory.GetStore<SessionHeartbeat>(StateStoreDefinitions.Connect);
        _stringStore = stateStoreFactory.GetStore<string>(StateStoreDefinitions.Connect);
        _cacheableStringStore = stateStoreFactory.GetCacheableStore<string>(StateStoreDefinitions.Connect);
        _messageBus = messageBus;
        _configuration = configuration;
        _logger = logger;
        _telemetryProvider = telemetryProvider;
    }

    #region Connection State

    /// <inheritdoc />
    public async Task SetConnectionStateAsync(
        string sessionId,
        ConnectionStateData stateData,
        TimeSpan? ttl = null)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.connect", "BannouSessionManager.SetConnectionStateAsync");
        try
        {
            var key = SESSION_KEY_PREFIX + sessionId;
            var ttlTimeSpan = ttl ?? TimeSpan.FromSeconds(_configuration.SessionTtlSeconds);

            await _connectionStateStore.SaveAsync(key, stateData, new StateOptions { Ttl = (int)ttlTimeSpan.TotalSeconds });

            _logger.LogDebug("Stored connection state for session {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store connection state for {SessionId}", sessionId);
            await _messageBus.TryPublishErrorAsync(
                "connect",
                "SetConnectionState",
                ex.GetType().Name,
                ex.Message,
                dependency: "state",
                stack: ex.StackTrace);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ConnectionStateData?> GetConnectionStateAsync(string sessionId)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.connect", "BannouSessionManager.GetConnectionStateAsync");
        try
        {
            var key = SESSION_KEY_PREFIX + sessionId;
            var stateData = await _connectionStateStore.GetAsync(key);

            if (stateData == null)
            {
                return null;
            }

            _logger.LogDebug("Retrieved connection state for session {SessionId}", sessionId);
            return stateData;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve connection state for {SessionId}", sessionId);
            await _messageBus.TryPublishErrorAsync(
                "connect",
                "GetConnectionState",
                ex.GetType().Name,
                ex.Message,
                dependency: "state",
                stack: ex.StackTrace);
            throw; // Don't mask state store failures - null should mean "not found", not "error"
        }
    }

    #endregion

    #region Heartbeat

    /// <inheritdoc />
    public async Task UpdateSessionHeartbeatAsync(string sessionId, Guid instanceId)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.connect", "BannouSessionManager.UpdateSessionHeartbeatAsync");
        try
        {
            var key = SESSION_HEARTBEAT_KEY_PREFIX + sessionId;
            var heartbeatData = new SessionHeartbeat
            {
                SessionId = Guid.Parse(sessionId),
                InstanceId = instanceId,
                LastSeen = DateTimeOffset.UtcNow,
                ConnectionCount = 1
            };

            await _heartbeatStore.SaveAsync(key, heartbeatData, new StateOptions { Ttl = _configuration.HeartbeatTtlSeconds });

            _logger.LogDebug("Updated heartbeat for session {SessionId} on instance {InstanceId}",
                sessionId, instanceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update session heartbeat for {SessionId}", sessionId);
            await _messageBus.TryPublishErrorAsync(
                "connect",
                "UpdateSessionHeartbeat",
                "state_heartbeat_failed",
                ex.Message,
                dependency: "state",
                endpoint: null,
                details: $"sessionId={sessionId}",
                stack: ex.StackTrace);
            // Don't throw - heartbeat failures shouldn't break main functionality
        }
    }

    #endregion

    #region Reconnection Support

    /// <inheritdoc />
    public async Task SetReconnectionTokenAsync(
        string reconnectionToken,
        string sessionId,
        TimeSpan reconnectionWindow)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.connect", "BannouSessionManager.SetReconnectionTokenAsync");
        try
        {
            var key = RECONNECTION_TOKEN_KEY_PREFIX + reconnectionToken;

            await _stringStore.SaveAsync(key, sessionId, new StateOptions { Ttl = (int)reconnectionWindow.TotalSeconds });

            _logger.LogDebug("Stored reconnection token for session {SessionId} (window: {Window})",
                sessionId, reconnectionWindow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store reconnection token for {SessionId}", sessionId);
            await _messageBus.TryPublishErrorAsync(
                "connect",
                "SetReconnectionToken",
                ex.GetType().Name,
                ex.Message,
                dependency: "state",
                stack: ex.StackTrace);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<string?> ValidateReconnectionTokenAsync(string reconnectionToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.connect", "BannouSessionManager.ValidateReconnectionTokenAsync");
        try
        {
            var key = RECONNECTION_TOKEN_KEY_PREFIX + reconnectionToken;
            var sessionId = await _stringStore.GetAsync(key);

            if (string.IsNullOrEmpty(sessionId))
            {
                _logger.LogDebug("Reconnection token not found or expired");
                return null;
            }

            return sessionId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate reconnection token");
            await _messageBus.TryPublishErrorAsync(
                "connect",
                "ValidateReconnectionToken",
                ex.GetType().Name,
                ex.Message,
                dependency: "state",
                stack: ex.StackTrace);
            throw; // Don't mask state store failures - null should mean "not found", not "error"
        }
    }

    /// <inheritdoc />
    public async Task RemoveReconnectionTokenAsync(string reconnectionToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.connect", "BannouSessionManager.RemoveReconnectionTokenAsync");
        try
        {
            var key = RECONNECTION_TOKEN_KEY_PREFIX + reconnectionToken;
            await _stringStore.DeleteAsync(key);

            _logger.LogDebug("Removed reconnection token");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove reconnection token");
            await _messageBus.TryPublishErrorAsync(
                "connect",
                "RemoveReconnectionToken",
                "state_cleanup_failed",
                ex.Message,
                dependency: "state",
                endpoint: null,
                details: null,
                stack: ex.StackTrace);
            // Don't throw - token cleanup failures shouldn't break main functionality
        }
    }

    /// <inheritdoc />
    public async Task InitiateReconnectionWindowAsync(
        string sessionId,
        string reconnectionToken,
        TimeSpan reconnectionWindow,
        ICollection<string>? userRoles)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.connect", "BannouSessionManager.InitiateReconnectionWindowAsync");
        try
        {
            // Get existing connection state
            var existingState = await GetConnectionStateAsync(sessionId);

            if (existingState == null)
            {
                _logger.LogWarning("Cannot initiate reconnection window - session {SessionId} not found", sessionId);
                return;
            }

            // Update state with reconnection info
            existingState.ReconnectionToken = reconnectionToken;
            existingState.ReconnectionExpiresAt = DateTimeOffset.UtcNow.Add(reconnectionWindow);
            existingState.DisconnectedAt = DateTimeOffset.UtcNow;
            existingState.UserRoles = userRoles?.ToList();

            // Store updated state with extended TTL for reconnection window
            await SetConnectionStateAsync(sessionId, existingState, reconnectionWindow.Add(TimeSpan.FromMinutes(_configuration.ReconnectionWindowExtensionMinutes)));

            // Store token -> sessionId mapping
            await SetReconnectionTokenAsync(reconnectionToken, sessionId, reconnectionWindow);

            _logger.LogInformation("Initiated reconnection window for session {SessionId} (expires: {ExpiresAt})",
                sessionId, existingState.ReconnectionExpiresAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initiate reconnection window for {SessionId}", sessionId);
            await _messageBus.TryPublishErrorAsync(
                "connect",
                "InitiateReconnectionWindow",
                ex.GetType().Name,
                ex.Message,
                dependency: "state",
                stack: ex.StackTrace);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ConnectionStateData?> RestoreSessionFromReconnectionAsync(
        string sessionId,
        string reconnectionToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.connect", "BannouSessionManager.RestoreSessionFromReconnectionAsync");
        try
        {
            var state = await GetConnectionStateAsync(sessionId);

            if (state == null)
            {
                _logger.LogWarning("Session {SessionId} not found for reconnection", sessionId);
                return null;
            }

            if (!state.IsInReconnectionWindow)
            {
                _logger.LogWarning("Session {SessionId} reconnection window expired", sessionId);
                return null;
            }

            if (state.ReconnectionToken != reconnectionToken)
            {
                _logger.LogWarning("Invalid reconnection token for session {SessionId}", sessionId);
                return null;
            }

            // Clear reconnection state
            state.DisconnectedAt = null;
            state.ReconnectionExpiresAt = null;
            state.ReconnectionToken = null;
            state.LastActivity = DateTimeOffset.UtcNow;

            // Store updated state with normal TTL
            await SetConnectionStateAsync(sessionId, state, TimeSpan.FromSeconds(_configuration.SessionTtlSeconds));

            // Remove reconnection token
            await RemoveReconnectionTokenAsync(reconnectionToken);

            _logger.LogInformation("Restored session {SessionId} from reconnection", sessionId);
            return state;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore session {SessionId} from reconnection", sessionId);
            await _messageBus.TryPublishErrorAsync(
                "connect",
                "RestoreSessionFromReconnection",
                ex.GetType().Name,
                ex.Message,
                dependency: "state",
                stack: ex.StackTrace);
            throw; // Don't mask state store failures - reconnection depends on reliable state access
        }
    }

    #endregion

    #region Session Cleanup

    /// <inheritdoc />
    public async Task RemoveSessionAsync(string sessionId)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.connect", "BannouSessionManager.RemoveSessionAsync");
        try
        {
            // Remove all session-related keys in parallel
            var sessionKey = SESSION_KEY_PREFIX + sessionId;
            var heartbeatKey = SESSION_HEARTBEAT_KEY_PREFIX + sessionId;

            var deleteTasks = new[]
            {
                _connectionStateStore.DeleteAsync(sessionKey),
                _heartbeatStore.DeleteAsync(heartbeatKey)
            };

            await Task.WhenAll(deleteTasks);

            _logger.LogDebug("Removed session data for {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove session data for {SessionId}", sessionId);
            await _messageBus.TryPublishErrorAsync(
                "connect",
                "RemoveSessionData",
                "state_cleanup_failed",
                ex.Message,
                dependency: "state",
                endpoint: null,
                details: $"sessionId={sessionId}",
                stack: ex.StackTrace);
            // Don't throw - cleanup failures shouldn't break main functionality
        }
    }

    #endregion

    #region Account Session Index

    /// <inheritdoc />
    public async Task AddSessionToAccountAsync(Guid accountId, string sessionId)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.connect", "BannouSessionManager.AddSessionToAccountAsync");
        try
        {
            var key = ACCOUNT_SESSIONS_KEY_PREFIX + accountId.ToString("N");

            // Atomic SADD operation - no read-modify-write race condition
            await _cacheableStringStore.AddToSetAsync(key, sessionId,
                new StateOptions { Ttl = _configuration.SessionTtlSeconds });

            _logger.LogDebug("Added session {SessionId} to account {AccountId} index",
                sessionId, accountId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add session {SessionId} to account {AccountId} index",
                sessionId, accountId);
            await _messageBus.TryPublishErrorAsync(
                "connect",
                "AddSessionToAccount",
                "state_update_failed",
                ex.Message,
                dependency: "state",
                endpoint: null,
                details: $"accountId={accountId},sessionId={sessionId}",
                stack: ex.StackTrace);
            // Don't throw - index update failures shouldn't break main functionality
        }
    }

    /// <inheritdoc />
    public async Task RemoveSessionFromAccountAsync(Guid accountId, string sessionId)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.connect", "BannouSessionManager.RemoveSessionFromAccountAsync");
        try
        {
            var key = ACCOUNT_SESSIONS_KEY_PREFIX + accountId.ToString("N");

            // Atomic SREM operation - no read-modify-write race condition
            await _cacheableStringStore.RemoveFromSetAsync(key, sessionId);

            // Check if set is now empty and clean up
            var remaining = await _cacheableStringStore.SetCountAsync(key);
            if (remaining == 0)
            {
                await _cacheableStringStore.DeleteSetAsync(key);
                _logger.LogDebug("Removed last session from account {AccountId} index, deleted key", accountId);
            }
            else
            {
                _logger.LogDebug("Removed session {SessionId} from account {AccountId} index (remaining: {Count})",
                    sessionId, accountId, remaining);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove session {SessionId} from account {AccountId} index",
                sessionId, accountId);
            await _messageBus.TryPublishErrorAsync(
                "connect",
                "RemoveSessionFromAccount",
                "state_update_failed",
                ex.Message,
                dependency: "state",
                endpoint: null,
                details: $"accountId={accountId},sessionId={sessionId}",
                stack: ex.StackTrace);
            // Don't throw - index update failures shouldn't break main functionality
        }
    }

    /// <inheritdoc />
    public async Task<HashSet<string>> GetSessionsForAccountAsync(Guid accountId)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.connect", "BannouSessionManager.GetSessionsForAccountAsync");
        try
        {
            var key = ACCOUNT_SESSIONS_KEY_PREFIX + accountId.ToString("N");

            // Get all session IDs from the atomic Redis set
            var sessions = await _cacheableStringStore.GetSetAsync<string>(key);
            if (sessions.Count == 0)
            {
                return new HashSet<string>();
            }

            // Filter stale sessions by cross-referencing heartbeat data.
            // Heartbeat keys have a 5-minute TTL (HeartbeatTtlSeconds) and are updated
            // every 30 seconds during active connections. Missing heartbeat = dead session.
            var liveSessions = new HashSet<string>();
            var staleSessions = new List<string>();

            foreach (var sessionId in sessions)
            {
                var heartbeatKey = SESSION_HEARTBEAT_KEY_PREFIX + sessionId;
                var heartbeat = await _heartbeatStore.GetAsync(heartbeatKey);
                if (heartbeat != null)
                {
                    liveSessions.Add(sessionId);
                }
                else
                {
                    staleSessions.Add(sessionId);
                }
            }

            // Lazily clean up stale entries from the set
            if (staleSessions.Count > 0)
            {
                _logger.LogDebug("Cleaning {StaleCount} stale sessions from account {AccountId} index",
                    staleSessions.Count, accountId);
                foreach (var staleSessionId in staleSessions)
                {
                    await _cacheableStringStore.RemoveFromSetAsync(key, staleSessionId);
                }
            }

            return liveSessions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get sessions for account {AccountId}", accountId);
            await _messageBus.TryPublishErrorAsync(
                "connect",
                "GetSessionsForAccount",
                ex.GetType().Name,
                ex.Message,
                dependency: "state",
                stack: ex.StackTrace);
            // Return empty set on error - callers can handle gracefully
            return new HashSet<string>();
        }
    }

    #endregion
}
