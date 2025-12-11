using Dapr.Client;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Auth.Services;

/// <summary>
/// Implementation of session lifecycle management.
/// Handles session storage, retrieval, indexing, and invalidation in Redis via Dapr.
/// </summary>
public class SessionService : ISessionService
{
    private readonly DaprClient _daprClient;
    private readonly AuthServiceConfiguration _configuration;
    private readonly ILogger<SessionService> _logger;
    private const string REDIS_STATE_STORE = "auth-statestore";
    private const string PUBSUB_NAME = "bannou-pubsub";
    private const string SESSION_INVALIDATED_TOPIC = "session.invalidated";
    private const string SESSION_UPDATED_TOPIC = "session.updated";

    /// <summary>
    /// Initializes a new instance of SessionService.
    /// </summary>
    public SessionService(
        DaprClient daprClient,
        AuthServiceConfiguration configuration,
        ILogger<SessionService> logger)
    {
        _daprClient = daprClient ?? throw new ArgumentNullException(nameof(daprClient));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<List<SessionInfo>> GetAccountSessionsAsync(string accountId, CancellationToken cancellationToken = default)
    {
        try
        {
            var sessionKeys = await GetSessionKeysForAccountAsync(accountId, cancellationToken);

            if (sessionKeys.Count == 0)
            {
                return new List<SessionInfo>();
            }

            var sessionDataTasks = sessionKeys.Select(async key =>
            {
                try
                {
                    var sessionData = await GetSessionAsync(key, cancellationToken);
                    return new { SessionKey = key, SessionData = sessionData };
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to retrieve session data for key: {SessionKey}", key);
                    return new { SessionKey = key, SessionData = (SessionDataModel?)null };
                }
            });

            var sessionResults = await Task.WhenAll(sessionDataTasks);

            var sessions = new List<SessionInfo>();
            var expiredSessionKeys = new List<string>();

            foreach (var result in sessionResults)
            {
                if (result.SessionData != null)
                {
                    if (result.SessionData.ExpiresAt > DateTimeOffset.UtcNow)
                    {
                        sessions.Add(new SessionInfo
                        {
                            SessionId = result.SessionData.SessionId,
                            CreatedAt = result.SessionData.CreatedAt,
                            LastActive = result.SessionData.CreatedAt,
                            DeviceInfo = new DeviceInfo
                            {
                                DeviceType = DeviceInfoDeviceType.Desktop,
                                Platform = "Unknown",
                                Browser = "Unknown"
                            }
                        });
                    }
                    else
                    {
                        expiredSessionKeys.Add(result.SessionKey);
                    }
                }
                else
                {
                    expiredSessionKeys.Add(result.SessionKey);
                }
            }

            // Clean up expired sessions
            foreach (var expiredKey in expiredSessionKeys)
            {
                await RemoveSessionFromAccountIndexAsync(accountId, expiredKey, cancellationToken);
            }

            _logger.LogDebug("Retrieved {ActiveCount} active sessions for account {AccountId} (cleaned up {ExpiredCount} expired)",
                sessions.Count, accountId, expiredSessionKeys.Count);

            return sessions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get sessions for account {AccountId}", accountId);
            return new List<SessionInfo>();
        }
    }

    /// <inheritdoc/>
    public async Task AddSessionToAccountIndexAsync(string accountId, string sessionKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var indexKey = $"account-sessions:{accountId}";
            var existingSessions = await _daprClient.GetStateAsync<List<string>>(
                REDIS_STATE_STORE,
                indexKey,
                cancellationToken: cancellationToken) ?? new List<string>();

            if (!existingSessions.Contains(sessionKey))
            {
                existingSessions.Add(sessionKey);

                var ttl = (_configuration.JwtExpirationMinutes * 60) + 300; // +5 minutes buffer
                await _daprClient.SaveStateAsync(
                    REDIS_STATE_STORE,
                    indexKey,
                    existingSessions,
                    metadata: new Dictionary<string, string> { { "ttl", ttl.ToString() } },
                    cancellationToken: cancellationToken);

                _logger.LogDebug("Added session to account index: AccountId={AccountId}, SessionKey={SessionKey}", accountId, sessionKey);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add session to account index: AccountId={AccountId}, SessionKey={SessionKey}", accountId, sessionKey);
        }
    }

    /// <inheritdoc/>
    public async Task RemoveSessionFromAccountIndexAsync(string accountId, string sessionKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var indexKey = $"account-sessions:{accountId}";
            var existingSessions = await _daprClient.GetStateAsync<List<string>>(
                REDIS_STATE_STORE,
                indexKey,
                cancellationToken: cancellationToken);

            if (existingSessions != null && existingSessions.Contains(sessionKey))
            {
                existingSessions.Remove(sessionKey);

                if (existingSessions.Count > 0)
                {
                    var ttl = (_configuration.JwtExpirationMinutes * 60) + 300;
                    await _daprClient.SaveStateAsync(
                        REDIS_STATE_STORE,
                        indexKey,
                        existingSessions,
                        metadata: new Dictionary<string, string> { { "ttl", ttl.ToString() } },
                        cancellationToken: cancellationToken);
                }
                else
                {
                    await _daprClient.DeleteStateAsync(REDIS_STATE_STORE, indexKey, cancellationToken: cancellationToken);
                }

                _logger.LogDebug("Removed session from account index: AccountId={AccountId}, SessionKey={SessionKey}", accountId, sessionKey);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove session from account index: AccountId={AccountId}, SessionKey={SessionKey}", accountId, sessionKey);
        }
    }

    /// <inheritdoc/>
    public async Task AddSessionIdReverseIndexAsync(string sessionId, string sessionKey, int ttlSeconds, CancellationToken cancellationToken = default)
    {
        try
        {
            await _daprClient.SaveStateAsync(
                REDIS_STATE_STORE,
                $"session-id-index:{sessionId}",
                sessionKey,
                metadata: new Dictionary<string, string> { { "ttl", ttlSeconds.ToString() } },
                cancellationToken: cancellationToken);

            _logger.LogDebug("Added reverse index: SessionId={SessionId} -> SessionKey={SessionKey}", sessionId, sessionKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add reverse index: SessionId={SessionId}", sessionId);
        }
    }

    /// <inheritdoc/>
    public async Task RemoveSessionIdReverseIndexAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _daprClient.DeleteStateAsync(REDIS_STATE_STORE, $"session-id-index:{sessionId}", cancellationToken: cancellationToken);
            _logger.LogDebug("Removed reverse index: SessionId={SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove reverse index: SessionId={SessionId}", sessionId);
        }
    }

    /// <inheritdoc/>
    public async Task<string?> FindSessionKeyBySessionIdAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var sessionKey = await _daprClient.GetStateAsync<string>(
                REDIS_STATE_STORE,
                $"session-id-index:{sessionId}",
                cancellationToken: cancellationToken);

            if (!string.IsNullOrEmpty(sessionKey))
            {
                _logger.LogDebug("Found session key for SessionId={SessionId}: {SessionKey}", sessionId, sessionKey);
                return sessionKey;
            }

            _logger.LogWarning("No session key found for SessionId={SessionId}", sessionId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding session key for SessionId={SessionId}", sessionId);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task DeleteSessionAsync(string sessionKey, CancellationToken cancellationToken = default)
    {
        await _daprClient.DeleteStateAsync(REDIS_STATE_STORE, $"session:{sessionKey}", cancellationToken: cancellationToken);
        _logger.LogDebug("Deleted session: SessionKey={SessionKey}", sessionKey);
    }

    /// <inheritdoc/>
    public async Task<SessionDataModel?> GetSessionAsync(string sessionKey, CancellationToken cancellationToken = default)
    {
        return await _daprClient.GetStateAsync<SessionDataModel>(
            REDIS_STATE_STORE,
            $"session:{sessionKey}",
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc/>
    public async Task SaveSessionAsync(string sessionKey, SessionDataModel sessionData, int? ttlSeconds = null, CancellationToken cancellationToken = default)
    {
        var metadata = ttlSeconds.HasValue
            ? new Dictionary<string, string> { { "ttl", ttlSeconds.Value.ToString() } }
            : null;

        await _daprClient.SaveStateAsync(
            REDIS_STATE_STORE,
            $"session:{sessionKey}",
            sessionData,
            metadata: metadata,
            cancellationToken: cancellationToken);

        _logger.LogDebug("Saved session: SessionKey={SessionKey}, AccountId={AccountId}", sessionKey, sessionData.AccountId);
    }

    /// <inheritdoc/>
    public async Task<List<string>> GetSessionKeysForAccountAsync(string accountId, CancellationToken cancellationToken = default)
    {
        var indexKey = $"account-sessions:{accountId}";
        var sessionKeys = await _daprClient.GetStateAsync<List<string>>(
            REDIS_STATE_STORE,
            indexKey,
            cancellationToken: cancellationToken);

        return sessionKeys ?? new List<string>();
    }

    /// <inheritdoc/>
    public async Task DeleteAccountSessionsIndexAsync(string accountId, CancellationToken cancellationToken = default)
    {
        var indexKey = $"account-sessions:{accountId}";
        await _daprClient.DeleteStateAsync(REDIS_STATE_STORE, indexKey, cancellationToken: cancellationToken);
        _logger.LogDebug("Deleted account sessions index: AccountId={AccountId}", accountId);
    }

    /// <inheritdoc/>
    public async Task InvalidateAllSessionsForAccountAsync(Guid accountId, SessionInvalidatedEventReason reason = SessionInvalidatedEventReason.Account_deleted, CancellationToken cancellationToken = default)
    {
        try
        {
            var sessionKeys = await GetSessionKeysForAccountAsync(accountId.ToString(), cancellationToken);

            if (sessionKeys.Count == 0)
            {
                _logger.LogDebug("No sessions found for account {AccountId}", accountId);
                return;
            }

            _logger.LogDebug("Invalidating {SessionCount} sessions for account {AccountId}", sessionKeys.Count, accountId);

            foreach (var sessionKey in sessionKeys)
            {
                try
                {
                    await DeleteSessionAsync(sessionKey, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete session {SessionKey} for account {AccountId}", sessionKey, accountId);
                }
            }

            await DeleteAccountSessionsIndexAsync(accountId.ToString(), cancellationToken);

            _logger.LogInformation("Invalidated {SessionCount} sessions for account {AccountId}", sessionKeys.Count, accountId);

            await PublishSessionInvalidatedEventAsync(accountId, sessionKeys, reason, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate sessions for account {AccountId}", accountId);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task PublishSessionInvalidatedEventAsync(Guid accountId, List<string> sessionIds, SessionInvalidatedEventReason reason, CancellationToken cancellationToken = default)
    {
        try
        {
            var eventModel = new SessionInvalidatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                AccountId = accountId,
                SessionIds = sessionIds,
                Reason = reason,
                DisconnectClients = true
            };

            await _daprClient.PublishEventAsync(PUBSUB_NAME, SESSION_INVALIDATED_TOPIC, eventModel, cancellationToken);
            _logger.LogInformation("Published SessionInvalidatedEvent for account {AccountId}: {SessionCount} sessions, reason: {Reason}",
                accountId, sessionIds.Count, reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish SessionInvalidatedEvent for account {AccountId}", accountId);
        }
    }

    /// <inheritdoc/>
    public async Task PublishSessionUpdatedEventAsync(Guid accountId, string sessionId, List<string> roles, List<string> authorizations, SessionUpdatedEventReason reason, CancellationToken cancellationToken = default)
    {
        try
        {
            var eventModel = new SessionUpdatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                AccountId = accountId,
                SessionId = sessionId,
                Roles = roles,
                Authorizations = authorizations,
                Reason = reason
            };

            await _daprClient.PublishEventAsync(PUBSUB_NAME, SESSION_UPDATED_TOPIC, eventModel, cancellationToken);
            _logger.LogDebug("Published SessionUpdatedEvent for session {SessionId}, reason: {Reason}", sessionId, reason);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish SessionUpdatedEvent for session {SessionId}", sessionId);
        }
    }
}
