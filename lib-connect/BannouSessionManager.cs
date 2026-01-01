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
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<BannouSessionManager> _logger;

    // State store name (must match Redis configuration)
    private const string STATE_STORE = "connect-statestore";
    private const string SESSION_EVENTS_TOPIC = "connect.session-events";

    // Key prefixes - MUST be unique across all services to avoid key collisions
    // (mesh prefixes keys with app-id, not component name, so all components share key namespace)
    private const string SESSION_KEY_PREFIX = "ws-session:";
    private const string SESSION_MAPPINGS_KEY_PREFIX = "ws-mappings:";
    private const string SESSION_HEARTBEAT_KEY_PREFIX = "heartbeat:";
    private const string RECONNECTION_TOKEN_KEY_PREFIX = "reconnect:";

    // Default TTL values
    private static readonly int SESSION_TTL_SECONDS = (int)TimeSpan.FromHours(24).TotalSeconds;
    private static readonly int HEARTBEAT_TTL_SECONDS = (int)TimeSpan.FromMinutes(5).TotalSeconds;

    /// <summary>
    /// Creates a new BannouSessionManager with the specified infrastructure services.
    /// </summary>
    public BannouSessionManager(
        IStateStoreFactory stateStoreFactory,
        IMessageBus messageBus,
        ILogger<BannouSessionManager> logger)
    {
        _stateStoreFactory = stateStoreFactory ?? throw new ArgumentNullException(nameof(stateStoreFactory));
        _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    #region Service Mappings

    /// <inheritdoc />
    public async Task SetSessionServiceMappingsAsync(
        string sessionId,
        Dictionary<string, Guid> serviceMappings,
        TimeSpan? ttl = null)
    {
        try
        {
            var key = SESSION_MAPPINGS_KEY_PREFIX + sessionId;
            var ttlTimeSpan = ttl ?? TimeSpan.FromSeconds(SESSION_TTL_SECONDS);

            var store = _stateStoreFactory.GetStore<Dictionary<string, Guid>>(STATE_STORE);
            await store.SaveAsync(key, serviceMappings, new StateOptions { Ttl = (int)ttlTimeSpan.TotalSeconds });

            _logger.LogDebug("Stored service mappings for session {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store session service mappings for {SessionId}", sessionId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, Guid>?> GetSessionServiceMappingsAsync(string sessionId)
    {
        try
        {
            var key = SESSION_MAPPINGS_KEY_PREFIX + sessionId;
            var store = _stateStoreFactory.GetStore<Dictionary<string, Guid>>(STATE_STORE);
            var mappings = await store.GetAsync(key);

            if (mappings == null)
            {
                return null;
            }

            _logger.LogDebug("Retrieved service mappings for session {SessionId}", sessionId);
            return mappings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve session service mappings for {SessionId}", sessionId);
            throw; // Don't mask state store failures - null should mean "not found", not "error"
        }
    }

    #endregion

    #region Connection State

    /// <inheritdoc />
    public async Task SetConnectionStateAsync(
        string sessionId,
        ConnectionStateData stateData,
        TimeSpan? ttl = null)
    {
        try
        {
            var key = SESSION_KEY_PREFIX + sessionId;
            var ttlTimeSpan = ttl ?? TimeSpan.FromSeconds(SESSION_TTL_SECONDS);

            var store = _stateStoreFactory.GetStore<ConnectionStateData>(STATE_STORE);
            await store.SaveAsync(key, stateData, new StateOptions { Ttl = (int)ttlTimeSpan.TotalSeconds });

            _logger.LogDebug("Stored connection state for session {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store connection state for {SessionId}", sessionId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ConnectionStateData?> GetConnectionStateAsync(string sessionId)
    {
        try
        {
            var key = SESSION_KEY_PREFIX + sessionId;
            var store = _stateStoreFactory.GetStore<ConnectionStateData>(STATE_STORE);
            var stateData = await store.GetAsync(key);

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
            throw; // Don't mask state store failures - null should mean "not found", not "error"
        }
    }

    #endregion

    #region Heartbeat

    /// <inheritdoc />
    public async Task UpdateSessionHeartbeatAsync(string sessionId, string instanceId)
    {
        try
        {
            var key = SESSION_HEARTBEAT_KEY_PREFIX + sessionId;
            var heartbeatData = new SessionHeartbeat
            {
                SessionId = sessionId,
                InstanceId = instanceId,
                LastSeen = DateTimeOffset.UtcNow,
                ConnectionCount = 1
            };

            var store = _stateStoreFactory.GetStore<SessionHeartbeat>(STATE_STORE);
            await store.SaveAsync(key, heartbeatData, new StateOptions { Ttl = HEARTBEAT_TTL_SECONDS });

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
        try
        {
            var key = RECONNECTION_TOKEN_KEY_PREFIX + reconnectionToken;

            var store = _stateStoreFactory.GetStore<string>(STATE_STORE);
            await store.SaveAsync(key, sessionId, new StateOptions { Ttl = (int)reconnectionWindow.TotalSeconds });

            _logger.LogDebug("Stored reconnection token for session {SessionId} (window: {Window})",
                sessionId, reconnectionWindow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store reconnection token for {SessionId}", sessionId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<string?> ValidateReconnectionTokenAsync(string reconnectionToken)
    {
        try
        {
            var key = RECONNECTION_TOKEN_KEY_PREFIX + reconnectionToken;
            var store = _stateStoreFactory.GetStore<string>(STATE_STORE);
            var sessionId = await store.GetAsync(key);

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
            throw; // Don't mask state store failures - null should mean "not found", not "error"
        }
    }

    /// <inheritdoc />
    public async Task RemoveReconnectionTokenAsync(string reconnectionToken)
    {
        try
        {
            var key = RECONNECTION_TOKEN_KEY_PREFIX + reconnectionToken;
            var store = _stateStoreFactory.GetStore<string>(STATE_STORE);
            await store.DeleteAsync(key);

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
            await SetConnectionStateAsync(sessionId, existingState, reconnectionWindow.Add(TimeSpan.FromMinutes(1)));

            // Store token -> sessionId mapping
            await SetReconnectionTokenAsync(reconnectionToken, sessionId, reconnectionWindow);

            _logger.LogInformation("Initiated reconnection window for session {SessionId} (expires: {ExpiresAt})",
                sessionId, existingState.ReconnectionExpiresAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initiate reconnection window for {SessionId}", sessionId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<ConnectionStateData?> RestoreSessionFromReconnectionAsync(
        string sessionId,
        string reconnectionToken)
    {
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
            await SetConnectionStateAsync(sessionId, state, TimeSpan.FromHours(24));

            // Remove reconnection token
            await RemoveReconnectionTokenAsync(reconnectionToken);

            _logger.LogInformation("Restored session {SessionId} from reconnection", sessionId);
            return state;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore session {SessionId} from reconnection", sessionId);
            throw; // Don't mask state store failures - reconnection depends on reliable state access
        }
    }

    #endregion

    #region Session Cleanup

    /// <inheritdoc />
    public async Task RemoveSessionAsync(string sessionId)
    {
        try
        {
            // Remove all session-related keys in parallel
            var sessionKey = SESSION_KEY_PREFIX + sessionId;
            var mappingsKey = SESSION_MAPPINGS_KEY_PREFIX + sessionId;
            var heartbeatKey = SESSION_HEARTBEAT_KEY_PREFIX + sessionId;

            // Get stores for each type
            var connectionStore = _stateStoreFactory.GetStore<ConnectionStateData>(STATE_STORE);
            var mappingsStore = _stateStoreFactory.GetStore<Dictionary<string, Guid>>(STATE_STORE);
            var heartbeatStore = _stateStoreFactory.GetStore<SessionHeartbeat>(STATE_STORE);

            var deleteTasks = new[]
            {
                connectionStore.DeleteAsync(sessionKey),
                mappingsStore.DeleteAsync(mappingsKey),
                heartbeatStore.DeleteAsync(heartbeatKey)
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

    #region Session Events

    /// <inheritdoc />
    public async Task PublishSessionEventAsync(string eventType, string sessionId, object? eventData = null)
    {
        try
        {
            var sessionEvent = new SessionEvent
            {
                EventType = eventType,
                SessionId = sessionId,
                Timestamp = DateTimeOffset.UtcNow,
                Data = eventData
            };

            await _messageBus.TryPublishAsync(SESSION_EVENTS_TOPIC, sessionEvent);

            _logger.LogDebug("Published session event {EventType} for session {SessionId}",
                eventType, sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish session event {EventType} for {SessionId}",
                eventType, sessionId);
            await _messageBus.TryPublishErrorAsync(
                "connect",
                "PublishSessionEvent",
                "event_publishing_failed",
                ex.Message,
                dependency: "messaging",
                endpoint: null,
                details: $"eventType={eventType},sessionId={sessionId}",
                stack: ex.StackTrace);
            // Don't throw - event publishing failures shouldn't break main functionality
        }
    }

    #endregion
}
