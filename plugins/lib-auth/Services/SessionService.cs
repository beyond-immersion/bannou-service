using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Auth.Services;

/// <summary>
/// Implementation of session lifecycle management.
/// Handles session storage, retrieval, indexing, and invalidation in Redis.
/// </summary>
public class SessionService : ISessionService
{
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IMessageBus _messageBus;
    private readonly AuthServiceConfiguration _configuration;
    private readonly IEdgeRevocationService _edgeRevocationService;
    private readonly ILogger<SessionService> _logger;
    private const string SESSION_INVALIDATED_TOPIC = "session.invalidated";
    private const string SESSION_UPDATED_TOPIC = "session.updated";

    // Device info placeholders - device capture is unimplemented
    private const string UNKNOWN_PLATFORM = "Unknown";
    private const string UNKNOWN_BROWSER = "Unknown";

    /// <summary>
    /// Initializes a new instance of SessionService.
    /// </summary>
    public SessionService(
        IStateStoreFactory stateStoreFactory,
        IMessageBus messageBus,
        AuthServiceConfiguration configuration,
        IEdgeRevocationService edgeRevocationService,
        ILogger<SessionService> logger)
    {
        _stateStoreFactory = stateStoreFactory;
        _messageBus = messageBus;
        _configuration = configuration;
        _edgeRevocationService = edgeRevocationService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<List<SessionInfo>> GetAccountSessionsAsync(Guid accountId, CancellationToken cancellationToken = default)
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
                        // LastActiveAt falls back to CreatedAt for sessions created before
                        // this field was introduced (LastActiveAtUnix defaults to 0)
                        var lastActive = result.SessionData.LastActiveAtUnix > 0
                            ? result.SessionData.LastActiveAt
                            : result.SessionData.CreatedAt;

                        sessions.Add(new SessionInfo
                        {
                            SessionId = result.SessionData.SessionId,
                            CreatedAt = result.SessionData.CreatedAt,
                            LastActive = lastActive,
                            DeviceInfo = new DeviceInfo
                            {
                                DeviceType = DeviceInfoDeviceType.Desktop,
                                Platform = UNKNOWN_PLATFORM,
                                Browser = UNKNOWN_BROWSER
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
            await _messageBus.TryPublishErrorAsync(
                "auth",
                "GetAccountSessions",
                ex.GetType().Name,
                ex.Message,
                dependency: "state",
                endpoint: "post:/auth/sessions",
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            throw; // Don't mask state store failures - empty list should mean "no sessions", not "error"
        }
    }

    /// <inheritdoc/>
    public async Task AddSessionToAccountIndexAsync(Guid accountId, string sessionKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var indexKey = $"account-sessions:{accountId}";
            var listStore = _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.Auth);
            var existingSessions = await listStore.GetAsync(indexKey, cancellationToken) ?? new List<string>();

            if (!existingSessions.Contains(sessionKey))
            {
                existingSessions.Add(sessionKey);

                var ttlSeconds = (_configuration.JwtExpirationMinutes * 60) + 300; // +5 minutes buffer
                await listStore.SaveAsync(
                    indexKey,
                    existingSessions,
                    new StateOptions { Ttl = ttlSeconds },
                    cancellationToken);

                _logger.LogDebug("Added session to account index: AccountId={AccountId}, SessionKey={SessionKey}", accountId, sessionKey);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add session to account index: AccountId={AccountId}, SessionKey={SessionKey}", accountId, sessionKey);
            await _messageBus.TryPublishErrorAsync(
                "auth",
                "AddSessionToAccountIndex",
                ex.GetType().Name,
                ex.Message,
                dependency: "state",
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
        }
    }

    /// <inheritdoc/>
    public async Task RemoveSessionFromAccountIndexAsync(Guid accountId, string sessionKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var indexKey = $"account-sessions:{accountId}";
            var listStore = _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.Auth);
            var existingSessions = await listStore.GetAsync(indexKey, cancellationToken);

            if (existingSessions != null && existingSessions.Contains(sessionKey))
            {
                existingSessions.Remove(sessionKey);

                if (existingSessions.Count > 0)
                {
                    var ttlSeconds = (_configuration.JwtExpirationMinutes * 60) + 300;
                    await listStore.SaveAsync(
                        indexKey,
                        existingSessions,
                        new StateOptions { Ttl = ttlSeconds },
                        cancellationToken);
                }
                else
                {
                    await listStore.DeleteAsync(indexKey, cancellationToken);
                }

                _logger.LogDebug("Removed session from account index: AccountId={AccountId}, SessionKey={SessionKey}", accountId, sessionKey);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove session from account index: AccountId={AccountId}, SessionKey={SessionKey}", accountId, sessionKey);
            await _messageBus.TryPublishErrorAsync(
                "auth",
                "RemoveSessionFromAccountIndex",
                ex.GetType().Name,
                ex.Message,
                dependency: "state",
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
        }
    }

    /// <inheritdoc/>
    public async Task AddSessionIdReverseIndexAsync(Guid sessionId, string sessionKey, int ttlSeconds, CancellationToken cancellationToken = default)
    {
        try
        {
            var stringStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Auth);
            await stringStore.SaveAsync(
                $"session-id-index:{sessionId}",
                sessionKey,
                new StateOptions { Ttl = ttlSeconds },
                cancellationToken);

            _logger.LogDebug("Added reverse index: SessionId={SessionId} -> SessionKey={SessionKey}", sessionId, sessionKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add reverse index: SessionId={SessionId}", sessionId);
            await _messageBus.TryPublishErrorAsync(
                "auth",
                "AddSessionIdReverseIndex",
                ex.GetType().Name,
                ex.Message,
                dependency: "state",
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
        }
    }

    /// <inheritdoc/>
    public async Task RemoveSessionIdReverseIndexAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var stringStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Auth);
            await stringStore.DeleteAsync($"session-id-index:{sessionId}", cancellationToken);
            _logger.LogDebug("Removed reverse index: SessionId={SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove reverse index: SessionId={SessionId}", sessionId);
        }
    }

    /// <inheritdoc/>
    public async Task<string?> FindSessionKeyBySessionIdAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var stringStore = _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Auth);
            var sessionKey = await stringStore.GetAsync($"session-id-index:{sessionId}", cancellationToken);

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
            await _messageBus.TryPublishErrorAsync(
                "auth",
                "FindSessionKeyBySessionId",
                ex.GetType().Name,
                ex.Message,
                dependency: "state",
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task DeleteSessionAsync(string sessionKey, CancellationToken cancellationToken = default)
    {
        var sessionStore = _stateStoreFactory.GetStore<SessionDataModel>(StateStoreDefinitions.Auth);
        await sessionStore.DeleteAsync($"session:{sessionKey}", cancellationToken);
        _logger.LogDebug("Deleted session: SessionKey={SessionKey}", sessionKey);
    }

    /// <inheritdoc/>
    public async Task<SessionDataModel?> GetSessionAsync(string sessionKey, CancellationToken cancellationToken = default)
    {
        var sessionStore = _stateStoreFactory.GetStore<SessionDataModel>(StateStoreDefinitions.Auth);
        return await sessionStore.GetAsync($"session:{sessionKey}", cancellationToken);
    }

    /// <inheritdoc/>
    public async Task SaveSessionAsync(string sessionKey, SessionDataModel sessionData, int? ttlSeconds = null, CancellationToken cancellationToken = default)
    {
        var options = ttlSeconds.HasValue
            ? new StateOptions { Ttl = ttlSeconds.Value }
            : null;

        var sessionStore = _stateStoreFactory.GetStore<SessionDataModel>(StateStoreDefinitions.Auth);
        await sessionStore.SaveAsync(
            $"session:{sessionKey}",
            sessionData,
            options,
            cancellationToken);

        _logger.LogDebug("Saved session: SessionKey={SessionKey}, AccountId={AccountId}", sessionKey, sessionData.AccountId);
    }

    /// <inheritdoc/>
    public async Task<List<string>> GetSessionKeysForAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var indexKey = $"account-sessions:{accountId}";
        var listStore = _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.Auth);
        var sessionKeys = await listStore.GetAsync(indexKey, cancellationToken);

        return sessionKeys ?? new List<string>();
    }

    /// <inheritdoc/>
    public async Task DeleteAccountSessionsIndexAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var indexKey = $"account-sessions:{accountId}";
        var listStore = _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.Auth);
        await listStore.DeleteAsync(indexKey, cancellationToken);
        _logger.LogDebug("Deleted account sessions index: AccountId={AccountId}", accountId);
    }

    /// <inheritdoc/>
    public async Task InvalidateAllSessionsForAccountAsync(Guid accountId, SessionInvalidatedEventReason reason = SessionInvalidatedEventReason.AccountDeleted, CancellationToken cancellationToken = default)
    {
        try
        {
            var sessionKeys = await GetSessionKeysForAccountAsync(accountId, cancellationToken);

            if (sessionKeys.Count == 0)
            {
                _logger.LogDebug("No sessions found for account {AccountId}", accountId);
                return;
            }

            _logger.LogDebug("Invalidating {SessionCount} sessions for account {AccountId}", sessionKeys.Count, accountId);

            // Collect JTIs from sessions before deleting for edge revocation
            var sessionsToRevoke = new List<(string jti, TimeSpan ttl)>();

            foreach (var sessionKey in sessionKeys)
            {
                try
                {
                    // Get session data to extract JTI before deletion
                    var sessionData = await GetSessionAsync(sessionKey, cancellationToken);
                    if (sessionData?.Jti != null)
                    {
                        // Calculate remaining TTL for edge revocation
                        var remainingTtl = sessionData.ExpiresAt - DateTimeOffset.UtcNow;
                        if (remainingTtl > TimeSpan.Zero)
                        {
                            sessionsToRevoke.Add((sessionData.Jti, remainingTtl));
                        }
                    }

                    await DeleteSessionAsync(sessionKey, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete session {SessionKey} for account {AccountId}", sessionKey, accountId);
                }
            }

            await DeleteAccountSessionsIndexAsync(accountId, cancellationToken);

            // Push revocations to edge providers (defense-in-depth)
            if (_edgeRevocationService.IsEnabled && sessionsToRevoke.Count > 0)
            {
                _logger.LogDebug("Pushing {Count} token revocations to edge providers for account {AccountId}",
                    sessionsToRevoke.Count, accountId);

                foreach (var (jti, ttl) in sessionsToRevoke)
                {
                    try
                    {
                        await _edgeRevocationService.RevokeTokenAsync(jti, accountId, ttl, reason.ToString(), cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        // Edge revocation failures should not block session invalidation
                        _logger.LogWarning(ex, "Failed to push edge revocation for JTI {Jti}", jti);
                    }
                }
            }

            _logger.LogInformation("Invalidated {SessionCount} sessions for account {AccountId}", sessionKeys.Count, accountId);

            await PublishSessionInvalidatedEventAsync(accountId, sessionKeys, reason, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to invalidate sessions for account {AccountId}", accountId);
            await _messageBus.TryPublishErrorAsync(
                "auth",
                "InvalidateAllSessionsForAccount",
                ex.GetType().Name,
                ex.Message,
                dependency: "state",
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            throw;
        }
    }

    /// <inheritdoc/>
    public async Task PublishSessionInvalidatedEventAsync(Guid accountId, List<string> sessionIds, SessionInvalidatedEventReason reason, CancellationToken cancellationToken = default)
    {
        try
        {
            // Session keys are stored as Guid.ToString("N") format - parse back to Guids
            var sessionIdGuids = sessionIds
                .Select(s => Guid.TryParse(s, out var g) ? g : (Guid?)null)
                .Where(g => g.HasValue)
                .Select(g => g.Value)
                .ToList();

            var eventModel = new SessionInvalidatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                AccountId = accountId,
                SessionIds = sessionIdGuids,
                Reason = reason,
                DisconnectClients = true
            };

            await _messageBus.TryPublishAsync(SESSION_INVALIDATED_TOPIC, eventModel);
            _logger.LogInformation("Published SessionInvalidatedEvent for account {AccountId}: {SessionCount} sessions, reason: {Reason}",
                accountId, sessionIds.Count, reason);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish SessionInvalidatedEvent for account {AccountId}", accountId);
            await _messageBus.TryPublishErrorAsync(
                "auth",
                "PublishSessionInvalidatedEvent",
                ex.GetType().Name,
                ex.Message,
                dependency: "messaging",
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
        }
    }

    /// <inheritdoc/>
    public async Task PublishSessionUpdatedEventAsync(Guid accountId, Guid sessionId, List<string> roles, List<string> authorizations, SessionUpdatedEventReason reason, CancellationToken cancellationToken = default)
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

            await _messageBus.TryPublishAsync(SESSION_UPDATED_TOPIC, eventModel);
            _logger.LogDebug("Published SessionUpdatedEvent for session {SessionId}, reason: {Reason}", sessionId, reason);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish SessionUpdatedEvent for session {SessionId}", sessionId);
        }
    }
}
