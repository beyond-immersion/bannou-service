using Dapr.Client;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Connect;

/// <summary>
/// Dapr-based session management for distributed WebSocket connection state.
/// Uses Dapr state store and pub/sub components for infrastructure access.
/// </summary>
public class DaprSessionManager : ISessionManager
{
    private readonly DaprClient _daprClient;
    private readonly ILogger<DaprSessionManager> _logger;

    // Dapr component names (must match component YAML configurations)
    private const string STATE_STORE = "connect-statestore";
    private const string PUBSUB_NAME = "bannou-pubsub";
    private const string SESSION_EVENTS_TOPIC = "connect.session-events";

    // Key prefixes - MUST be unique across all services to avoid key collisions
    // (Dapr prefixes keys with app-id, not component name, so all components share key namespace)
    private const string SESSION_KEY_PREFIX = "ws-session:";
    private const string SESSION_MAPPINGS_KEY_PREFIX = "ws-mappings:";
    private const string SESSION_HEARTBEAT_KEY_PREFIX = "heartbeat:";
    private const string RECONNECTION_TOKEN_KEY_PREFIX = "reconnect:";

    // Default TTL values
    private static readonly int SESSION_TTL_SECONDS = (int)TimeSpan.FromHours(24).TotalSeconds;
    private static readonly int HEARTBEAT_TTL_SECONDS = (int)TimeSpan.FromMinutes(5).TotalSeconds;

    /// <summary>
    /// Creates a new DaprSessionManager with the specified Dapr client.
    /// </summary>
    public DaprSessionManager(DaprClient daprClient, ILogger<DaprSessionManager> logger)
    {
        _daprClient = daprClient ?? throw new ArgumentNullException(nameof(daprClient));
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
            var ttlSeconds = (int)(ttl?.TotalSeconds ?? SESSION_TTL_SECONDS);

            await _daprClient.SaveStateAsync(
                STATE_STORE,
                key,
                serviceMappings,
                metadata: new Dictionary<string, string> { { "ttlInSeconds", ttlSeconds.ToString() } });

            _logger.LogDebug("Stored service mappings for session {SessionId} via Dapr", sessionId);
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
            var mappings = await _daprClient.GetStateAsync<Dictionary<string, Guid>>(STATE_STORE, key);

            if (mappings == null)
            {
                return null;
            }

            _logger.LogDebug("Retrieved service mappings for session {SessionId} via Dapr", sessionId);
            return mappings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve session service mappings for {SessionId}", sessionId);
            return null;
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
            var ttlSeconds = (int)(ttl?.TotalSeconds ?? SESSION_TTL_SECONDS);

            await _daprClient.SaveStateAsync(
                STATE_STORE,
                key,
                stateData,
                metadata: new Dictionary<string, string> { { "ttlInSeconds", ttlSeconds.ToString() } });

            _logger.LogDebug("Stored connection state for session {SessionId} via Dapr", sessionId);
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
            var stateData = await _daprClient.GetStateAsync<ConnectionStateData>(STATE_STORE, key);

            if (stateData == null)
            {
                return null;
            }

            _logger.LogDebug("Retrieved connection state for session {SessionId} via Dapr", sessionId);
            return stateData;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve connection state for {SessionId}", sessionId);
            return null;
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

            await _daprClient.SaveStateAsync(
                STATE_STORE,
                key,
                heartbeatData,
                metadata: new Dictionary<string, string> { { "ttlInSeconds", HEARTBEAT_TTL_SECONDS.ToString() } });

            _logger.LogDebug("Updated heartbeat for session {SessionId} on instance {InstanceId}",
                sessionId, instanceId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update session heartbeat for {SessionId}", sessionId);
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
            var ttlSeconds = (int)reconnectionWindow.TotalSeconds;

            await _daprClient.SaveStateAsync(
                STATE_STORE,
                key,
                sessionId,
                metadata: new Dictionary<string, string> { { "ttlInSeconds", ttlSeconds.ToString() } });

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
            var sessionId = await _daprClient.GetStateAsync<string>(STATE_STORE, key);

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
            return null;
        }
    }

    /// <inheritdoc />
    public async Task RemoveReconnectionTokenAsync(string reconnectionToken)
    {
        try
        {
            var key = RECONNECTION_TOKEN_KEY_PREFIX + reconnectionToken;
            await _daprClient.DeleteStateAsync(STATE_STORE, key);

            _logger.LogDebug("Removed reconnection token");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove reconnection token");
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
            return null;
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

            var deleteTasks = new[]
            {
                _daprClient.DeleteStateAsync(STATE_STORE, sessionKey),
                _daprClient.DeleteStateAsync(STATE_STORE, mappingsKey),
                _daprClient.DeleteStateAsync(STATE_STORE, heartbeatKey)
            };

            await Task.WhenAll(deleteTasks);

            _logger.LogDebug("Removed session data for {SessionId} via Dapr", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove session data for {SessionId}", sessionId);
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

            await _daprClient.PublishEventAsync(PUBSUB_NAME, SESSION_EVENTS_TOPIC, sessionEvent);

            _logger.LogDebug("Published session event {EventType} for session {SessionId}",
                eventType, sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish session event {EventType} for {SessionId}",
                eventType, sessionId);
            // Don't throw - event publishing failures shouldn't break main functionality
        }
    }

    #endregion
}
