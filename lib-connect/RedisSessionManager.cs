using BeyondImmersion.BannouService.Connect.Protocol;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;

namespace BeyondImmersion.BannouService.Connect;

/// <summary>
/// Redis-based session management for distributed WebSocket connection state.
/// Replaces in-memory session mappings with persistent, scalable Redis storage.
/// </summary>
public class RedisSessionManager
{
    private readonly IDatabase _database;
    private readonly ILogger<RedisSessionManager> _logger;

    // Redis key patterns
    private const string SESSION_KEY_PREFIX = "bannou:connect:session:";
    private const string SESSION_MAPPINGS_KEY_PREFIX = "bannou:connect:mappings:";
    private const string SESSION_HEARTBEAT_KEY_PREFIX = "bannou:connect:heartbeat:";

    // Default TTL values
    private static readonly TimeSpan SESSION_TTL = TimeSpan.FromHours(24);
    private static readonly TimeSpan HEARTBEAT_TTL = TimeSpan.FromMinutes(5);

    public RedisSessionManager(IConnectionMultiplexer redis, ILogger<RedisSessionManager> logger)
    {
        _database = redis.GetDatabase();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Stores session service mappings in Redis with TTL.
    /// </summary>
    public async Task SetSessionServiceMappingsAsync(
        string sessionId,
        Dictionary<string, Guid> serviceMappings,
        TimeSpan? ttl = null)
    {
        try
        {
            var key = SESSION_MAPPINGS_KEY_PREFIX + sessionId;
            var serializedMappings = JsonSerializer.Serialize(serviceMappings);

            await _database.StringSetAsync(key, serializedMappings, ttl ?? SESSION_TTL);

            _logger.LogDebug("Stored service mappings for session {SessionId} in Redis", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store session service mappings for {SessionId}", sessionId);
            throw;
        }
    }

    /// <summary>
    /// Retrieves session service mappings from Redis.
    /// </summary>
    public async Task<Dictionary<string, Guid>?> GetSessionServiceMappingsAsync(string sessionId)
    {
        try
        {
            var key = SESSION_MAPPINGS_KEY_PREFIX + sessionId;
            var serializedMappings = await _database.StringGetAsync(key);

            if (!serializedMappings.HasValue)
            {
                return null;
            }

            // Convert RedisValue to string - safe after HasValue check
            string mappingsValue = serializedMappings.ToString();
            var mappings = JsonSerializer.Deserialize<Dictionary<string, Guid>>(mappingsValue);

            _logger.LogDebug("Retrieved service mappings for session {SessionId} from Redis", sessionId);
            return mappings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve session service mappings for {SessionId}", sessionId);
            return null;
        }
    }

    /// <summary>
    /// Stores connection state in Redis.
    /// </summary>
    public async Task SetConnectionStateAsync(
        string sessionId,
        ConnectionStateData stateData,
        TimeSpan? ttl = null)
    {
        try
        {
            var key = SESSION_KEY_PREFIX + sessionId;
            var serializedState = JsonSerializer.Serialize(stateData);

            await _database.StringSetAsync(key, serializedState, ttl ?? SESSION_TTL);

            _logger.LogDebug("Stored connection state for session {SessionId} in Redis", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store connection state for {SessionId}", sessionId);
            throw;
        }
    }

    /// <summary>
    /// Retrieves connection state from Redis.
    /// </summary>
    public async Task<ConnectionStateData?> GetConnectionStateAsync(string sessionId)
    {
        try
        {
            var key = SESSION_KEY_PREFIX + sessionId;
            var serializedState = await _database.StringGetAsync(key);

            if (!serializedState.HasValue)
            {
                return null;
            }

            // Convert RedisValue to string - safe after HasValue check
            string stateValue = serializedState.ToString();
            var stateData = JsonSerializer.Deserialize<ConnectionStateData>(stateValue);

            _logger.LogDebug("Retrieved connection state for session {SessionId} from Redis", sessionId);
            return stateData;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve connection state for {SessionId}", sessionId);
            return null;
        }
    }

    /// <summary>
    /// Updates session heartbeat timestamp in Redis.
    /// Used for connection liveness tracking across distributed instances.
    /// </summary>
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
                ConnectionCount = 1 // TODO: Support multiple connections per session
            };

            var serializedHeartbeat = JsonSerializer.Serialize(heartbeatData);
            await _database.StringSetAsync(key, serializedHeartbeat, HEARTBEAT_TTL);

            _logger.LogDebug("Updated heartbeat for session {SessionId} on instance {InstanceId}",
                sessionId, instanceId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update session heartbeat for {SessionId}", sessionId);
            // Don't throw - heartbeat failures shouldn't break main functionality
        }
    }

    /// <summary>
    /// Gets all active session heartbeats for monitoring.
    /// </summary>
    public async Task<List<SessionHeartbeat>> GetActiveSessionsAsync()
    {
        try
        {
            var pattern = SESSION_HEARTBEAT_KEY_PREFIX + "*";
            var server = _database.Multiplexer.GetServer(_database.Multiplexer.GetEndPoints().First());
            var keys = server.Keys(pattern: pattern);

            var heartbeats = new List<SessionHeartbeat>();

            foreach (var key in keys)
            {
                var serializedHeartbeat = await _database.StringGetAsync(key);
                if (serializedHeartbeat.HasValue)
                {
                    // Convert RedisValue to string - serializedHeartbeat comes from Redis string array
                    string heartbeatValue = serializedHeartbeat.ToString();
                    var heartbeat = JsonSerializer.Deserialize<SessionHeartbeat>(heartbeatValue);
                    if (heartbeat != null)
                    {
                        heartbeats.Add(heartbeat);
                    }
                }
            }

            return heartbeats;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve active sessions");
            return new List<SessionHeartbeat>();
        }
    }

    /// <summary>
    /// Removes session data from Redis (cleanup on disconnect).
    /// </summary>
    public Task RemoveSessionAsync(string sessionId)
    {
        try
        {
            var batch = _database.CreateBatch();

            // Remove all session-related keys
            var sessionKey = SESSION_KEY_PREFIX + sessionId;
            var mappingsKey = SESSION_MAPPINGS_KEY_PREFIX + sessionId;
            var heartbeatKey = SESSION_HEARTBEAT_KEY_PREFIX + sessionId;

            _ = batch.KeyDeleteAsync(sessionKey);
            _ = batch.KeyDeleteAsync(mappingsKey);
            _ = batch.KeyDeleteAsync(heartbeatKey);

            batch.Execute();

            _logger.LogDebug("Removed session data for {SessionId} from Redis", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove session data for {SessionId}", sessionId);
            // Don't throw - cleanup failures shouldn't break main functionality
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Publishes a session event to Redis pub/sub for cross-instance communication.
    /// </summary>
    [Obsolete]
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

            var channel = "bannou:connect:session-events";
            var message = JsonSerializer.Serialize(sessionEvent);

            await _database.Multiplexer.GetSubscriber().PublishAsync(channel, message);

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
}

/// <summary>
/// Serializable connection state data for Redis storage.
/// </summary>
public class ConnectionStateData
{
    public string SessionId { get; set; } = string.Empty;
    public DateTimeOffset ConnectedAt { get; set; }
    public DateTimeOffset LastActivity { get; set; }
    public Dictionary<string, Guid> ServiceMappings { get; set; } = new();
    public Dictionary<ushort, uint> ChannelSequences { get; set; } = new();
    public ConnectionFlags Flags { get; set; }
}

/// <summary>
/// Session heartbeat data for distributed connection tracking.
/// </summary>
public class SessionHeartbeat
{
    public string SessionId { get; set; } = string.Empty;
    public string InstanceId { get; set; } = string.Empty;
    public DateTimeOffset LastSeen { get; set; }
    public int ConnectionCount { get; set; }
}

/// <summary>
/// Session event for cross-instance communication.
/// </summary>
public class SessionEvent
{
    public string EventType { get; set; } = string.Empty;
    public string SessionId { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public object? Data { get; set; }
}
