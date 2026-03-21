using BeyondImmersion.Bannou.Core;
using BeyondImmersion.Bannou.GameSession.ClientEvents;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Connect;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.GameService;
using BeyondImmersion.BannouService.Permission;
using BeyondImmersion.BannouService.Protocol;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Subscription;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.GameSession;

// =============================================================================
// GameSessionService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by GameSessionService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (GameSessionService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in IGameSessionService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (GameSessionService.Helpers.cs):
//     Contains all private/internal helper methods, core logic extracted
//     from endpoints, event publishing helpers, query builders, mapping
//     functions, and any other non-public methods. Every async method in
//     this file MUST call ITelemetryProvider.StartActivity to ensure
//     sub-operations are properly instrumented.
//
// Structural tests enforce both rules:
//   - Services_PrimaryFile_DoesNotCallStartActivity
//   - Services_HelperFiles_HaveStartActivityWhenAsync
//
// WHAT GOES HERE:
//   - Private async helper methods (with StartActivity spans)
//   - Private sync helper methods (query builders, mappers, validators)
//   - Internal static key builders (already in primary file by convention,
//     but may be moved here if the primary file is large)
//   - Event publishing helper methods
//   - Any extracted "core" logic (e.g., CreateAccountCoreAsync)
//
// WHAT STAYS IN THE PRIMARY FILE:
//   - Public interface method implementations (/// <inheritdoc/> methods)
//   - Constructor and field declarations
//   - Constants and key prefix definitions
//
// See: docs/reference/tenets/IMPLEMENTATION-BEHAVIOR.md (T30)
// See: docs/reference/HELPERS-AND-COMMON-PATTERNS.md
// =============================================================================

/// <summary>
/// Private and internal helper methods for GameSessionService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class GameSessionService
{
    #region Internal Event Handlers

    /// <summary>
    /// Handles session.connected event from Connect service.
    /// Tracks the session and publishes join shortcuts for subscribed accounts.
    /// If GenericLobbiesEnabled is true and this instance handles "generic", publishes
    /// a generic lobby shortcut to ALL authenticated sessions without requiring subscription.
    /// Called internally by GameSessionEventsController.
    /// </summary>
    /// <param name="sessionId">WebSocket session ID that connected.</param>
    /// <param name="accountId">Account ID owning the session.</param>
    internal async Task HandleSessionConnectedInternalAsync(Guid sessionId, Guid accountId)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.game-session", "GameSessionService.HandleSessionConnectedInternal");

        // Track if we've already published generic shortcut to avoid duplication
        var genericPublished = false;

        // GENERIC LOBBIES: If enabled and we handle "generic", publish shortcut immediately
        // to ALL authenticated sessions - no subscription required
        if (_configuration.GenericLobbiesEnabled && IsOurService("generic"))
        {
            await StoreSubscriberSessionAsync(accountId, sessionId);
            await PublishJoinShortcutAsync(sessionId, accountId, "generic");
            genericPublished = true;
            _logger.LogDebug("Published generic lobby shortcut to authenticated session {SessionId} (GenericLobbiesEnabled)", sessionId);
        }

        // SUBSCRIPTION-BASED SHORTCUTS: Check subscriptions for non-generic services
        // (or for generic if GenericLobbiesEnabled is false)

        // Check if account is in our local subscription cache (fast filter)
        if (!_accountSubscriptions.ContainsKey(accountId))
        {
            await FetchAndCacheSubscriptionsAsync(accountId);
        }

        // Publish shortcuts for subscribed game services
        if (_accountSubscriptions.TryGetValue(accountId, out var stubNames))
        {
            // Filter to our services, excluding generic if already published
            var ourServices = stubNames
                .Where(IsOurService)
                .Where(stub => !(genericPublished && string.Equals(stub, "generic", StringComparison.OrdinalIgnoreCase)))
                .ToList();

            // Filter to services with autoLobbyEnabled (check via GameService)
            // Games with autoLobbyEnabled=false have entry managed by higher-layer orchestration (e.g., Gardener)
            var autoLobbyServices = new List<string>();
            foreach (var stub in ourServices)
            {
                if (await IsAutoLobbyEnabledAsync(stub))
                {
                    autoLobbyServices.Add(stub);
                }
                else
                {
                    _logger.LogDebug(
                        "Skipping lobby shortcut for {StubName}: autoLobbyEnabled is false (entry managed by higher-layer orchestration)",
                        stub);
                }
            }

            if (autoLobbyServices.Count > 0)
            {
                _logger.LogDebug("Account {AccountId} has {Count} auto-lobby subscriptions matching our services: {Services}",
                    accountId, autoLobbyServices.Count, string.Join(", ", autoLobbyServices));

                // Store subscriber session in lib-state (distributed tracking) if not already stored
                if (!genericPublished)
                {
                    await StoreSubscriberSessionAsync(accountId, sessionId);
                }

                foreach (var stubName in autoLobbyServices)
                {
                    await PublishJoinShortcutAsync(sessionId, accountId, stubName);
                }
            }
            else
            {
                _logger.LogDebug("Account {AccountId} has no subscriptions matching our services", accountId);
            }
        }
        else if (!genericPublished)
        {
            _logger.LogDebug("No subscriptions found for account {AccountId}", accountId);
        }
    }

    /// <summary>
    /// Handles session.disconnected event from Connect service.
    /// Removes session from distributed subscriber tracking.
    /// Called internally by GameSessionEventsController.
    /// </summary>
    /// <param name="sessionId">WebSocket session ID that disconnected.</param>
    /// <param name="accountId">Account ID from the disconnect event (null if session was unauthenticated).</param>
    internal async Task HandleSessionDisconnectedInternalAsync(Guid sessionId, Guid? accountId)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.game-session", "GameSessionService.HandleSessionDisconnectedInternal");

        // Only remove from subscriber tracking if the session was authenticated
        if (accountId.HasValue)
        {
            await RemoveSubscriberSessionAsync(accountId.Value, sessionId);
            _logger.LogDebug("Removed session {SessionId} (account {AccountId}) from subscriber tracking", sessionId, accountId.Value);
        }
        else
        {
            _logger.LogDebug("Session {SessionId} disconnected (was not authenticated, no subscriber tracking to remove)", sessionId);
        }
    }

    /// <summary>
    /// Handles subscription.updated event from Subscription service.
    /// Updates subscription cache and publishes/revokes shortcuts for affected connected sessions.
    /// Called internally by GameSessionEventsController.
    /// </summary>
    /// <param name="accountId">Account whose subscription changed.</param>
    /// <param name="stubName">Stub name of the service (e.g., "my-game").</param>
    /// <param name="action">Action that triggered the event.</param>
    /// <param name="isActive">Whether the subscription is currently active.</param>
    internal async Task HandleSubscriptionUpdatedInternalAsync(Guid accountId, string stubName, SubscriptionAction action, bool isActive)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.game-session", "GameSessionService.HandleSubscriptionUpdatedInternal");

        _logger.LogInformation("Subscription update for account {AccountId}: stubName={StubName}, action={Action}, isActive={IsActive}",
            accountId, stubName, action, isActive);

        // Update the cache
        if (isActive && (action == SubscriptionAction.Created || action == SubscriptionAction.Renewed || action == SubscriptionAction.Updated))
        {
            _accountSubscriptions.AddOrUpdate(
                accountId,
                _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { stubName },
                (_, existingSet) =>
                {
                    lock (existingSet)
                    {
                        existingSet.Add(stubName);
                    }
                    return existingSet;
                });
            _logger.LogDebug("Added {StubName} to subscription cache for account {AccountId}", stubName, accountId);
        }
        else if (!isActive || action == SubscriptionAction.Cancelled || action == SubscriptionAction.Expired)
        {
            if (_accountSubscriptions.TryGetValue(accountId, out var existingSet))
            {
                bool shouldRemoveEntry;
                lock (existingSet)
                {
                    existingSet.Remove(stubName);
                    shouldRemoveEntry = existingSet.Count == 0;
                }

                if (shouldRemoveEntry)
                {
                    _accountSubscriptions.TryRemove(accountId, out _);
                }

                _logger.LogDebug("Removed {StubName} from subscription cache for account {AccountId}", stubName, accountId);
            }
        }

        // Find connected sessions for this account and update their shortcuts
        if (!IsOurService(stubName))
        {
            _logger.LogDebug("Service {StubName} is not handled by game-session, skipping shortcut update", stubName);
            return;
        }

        // Query Connect service for ALL connected sessions for this account
        // This finds sessions that connected BEFORE the subscription was created,
        // not just those already in our subscriber-sessions store
        List<Guid> connectedSessionsForAccount;
        try
        {
            var connectResponse = await _connectClient.GetAccountSessionsAsync(
                new GetAccountSessionsRequest { AccountId = accountId });

            // SessionIds are already Guids from the generated model
            connectedSessionsForAccount = connectResponse.SessionIds.ToList();
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Connect service error getting sessions for account {AccountId}: {StatusCode}",
                accountId, ex.StatusCode);
            // Fall back to local subscriber-sessions store
            connectedSessionsForAccount = await GetSubscriberSessionsAsync(accountId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get connected sessions from Connect for account {AccountId}", accountId);
            // Fall back to local subscriber-sessions store
            connectedSessionsForAccount = await GetSubscriberSessionsAsync(accountId);
        }

        _logger.LogDebug("Found {Count} connected sessions for account {AccountId}", connectedSessionsForAccount.Count, accountId);

        // For active subscriptions, check if this game service has auto-lobby enabled
        // Revocation path is unconditional — shortcuts may exist from when autoLobbyEnabled was true
        bool shouldPublishShortcuts = isActive && await IsAutoLobbyEnabledAsync(stubName);

        foreach (var sessionId in connectedSessionsForAccount)
        {
            if (isActive)
            {
                // Always store subscriber session for authorization tracking
                await StoreSubscriberSessionAsync(accountId, sessionId);
                if (shouldPublishShortcuts)
                {
                    await PublishJoinShortcutAsync(sessionId, accountId, stubName);
                }
            }
            else
            {
                await RevokeShortcutsForSessionAsync(sessionId, stubName);
            }
        }
    }

    /// <summary>
    /// Fetches and caches subscriptions for an account from the Subscription service.
    /// </summary>
    private async Task FetchAndCacheSubscriptionsAsync(Guid accountId)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.game-session", "GameSessionService.FetchAndCacheSubscriptions");

        try
        {
            var response = await _subscriptionClient.QueryCurrentSubscriptionsAsync(
                new QueryCurrentSubscriptionsRequest { AccountId = accountId });

            if (response?.Subscriptions != null && response.Subscriptions.Count > 0)
            {
                // Filter first, then select - StubName is required (non-nullable) per schema
                var stubs = response.Subscriptions
                    .Where(s => !string.IsNullOrEmpty(s.StubName))
                    .Select(s => s.StubName)
                    .ToList();

                // Use AddOrUpdate with lock for thread-safe replacement (IMPLEMENTATION TENETS)
                _accountSubscriptions.AddOrUpdate(
                    accountId,
                    _ =>
                    {
                        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var stub in stubs) set.Add(stub);
                        return set;
                    },
                    (_, existingSet) =>
                    {
                        lock (existingSet)
                        {
                            existingSet.Clear();
                            foreach (var stub in stubs) existingSet.Add(stub);
                        }
                        return existingSet;
                    });
                _logger.LogDebug("Cached {Count} subscriptions for account {AccountId}: {Stubs}",
                    stubs.Count, accountId, string.Join(", ", stubs));
            }
            else
            {
                // Do not cache empty sets for unsubscribed accounts to prevent unbounded cache growth.
                // Unsubscribed accounts will be re-checked on their next connect event, which also
                // handles the case where a subscription.updated event was missed during a restart.
                _logger.LogDebug("No subscriptions found for account {AccountId}, not caching", accountId);
            }
        }
        catch (ApiException ex)
        {
            // Subscription service returned an error - don't cache, allow retry
            _logger.LogWarning(ex, "Subscription service error fetching subscriptions for account {AccountId}: {StatusCode}",
                accountId, ex.StatusCode);
        }
        catch (Exception ex)
        {
            // Unexpected error - don't cache, allow retry
            _logger.LogWarning(ex, "Failed to fetch subscriptions for account {AccountId}", accountId);
        }
    }

    /// <summary>
    /// Stores a subscriber session in distributed state.
    /// Called when a session connects for a subscribed account.
    /// </summary>
    private async Task StoreSubscriberSessionAsync(Guid accountId, Guid sessionId)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.game-session", "GameSessionService.StoreSubscriberSession");

        try
        {
            var key = SUBSCRIBER_SESSIONS_PREFIX + accountId.ToString();

            for (var attempt = 0; attempt < _configuration.SubscriberSessionRetryMaxAttempts; attempt++)
            {
                var (existing, etag) = await _subscriberSessionStore.GetWithETagAsync(key);
                var model = existing ?? new SubscriberSessionsModel { AccountId = accountId };
                model.SessionIds.Add(sessionId);
                model.UpdatedAt = DateTimeOffset.UtcNow;

                // Empty string ETag signals a new record to TrySaveAsync when
                // GetWithETagAsync returns null etag for non-existent keys
                var result = await _subscriberSessionStore.TrySaveAsync(key, model, etag ?? string.Empty);
                if (result != null)
                {
                    _logger.LogDebug("Stored subscriber session {SessionId} for account {AccountId}", sessionId, accountId);
                    return;
                }

                _logger.LogDebug("Concurrent modification on subscriber sessions for account {AccountId}, retrying (attempt {Attempt})",
                    accountId, attempt + 1);
            }

            _logger.LogWarning("Failed to store subscriber session {SessionId} for account {AccountId} after retries", sessionId, accountId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to store subscriber session {SessionId} for account {AccountId}", sessionId, accountId);
        }
    }

    /// <summary>
    /// Removes a subscriber session from distributed state.
    /// Called when a session disconnects.
    /// </summary>
    private async Task RemoveSubscriberSessionAsync(Guid accountId, Guid sessionId)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.game-session", "GameSessionService.RemoveSubscriberSession");

        try
        {
            var key = SUBSCRIBER_SESSIONS_PREFIX + accountId.ToString();

            for (var attempt = 0; attempt < _configuration.SubscriberSessionRetryMaxAttempts; attempt++)
            {
                var (existing, etag) = await _subscriberSessionStore.GetWithETagAsync(key);
                if (existing == null)
                {
                    _logger.LogDebug("No subscriber sessions found for account {AccountId}, nothing to remove", accountId);
                    return;
                }

                existing.SessionIds.Remove(sessionId);
                existing.UpdatedAt = DateTimeOffset.UtcNow;

                if (existing.SessionIds.Count == 0)
                {
                    await _subscriberSessionStore.DeleteAsync(key);
                    _logger.LogDebug("Removed last subscriber session {SessionId} for account {AccountId}", sessionId, accountId);
                    return;
                }

                // Empty string ETag signals a new record to TrySaveAsync when
                // GetWithETagAsync returns null etag for non-existent keys
                var result = await _subscriberSessionStore.TrySaveAsync(key, existing, etag ?? string.Empty);
                if (result != null)
                {
                    _logger.LogDebug("Removed subscriber session {SessionId} for account {AccountId}", sessionId, accountId);
                    return;
                }

                _logger.LogDebug("Concurrent modification on subscriber sessions for account {AccountId}, retrying (attempt {Attempt})",
                    accountId, attempt + 1);
            }

            _logger.LogWarning("Failed to remove subscriber session {SessionId} for account {AccountId} after retries", sessionId, accountId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove subscriber session {SessionId} for account {AccountId}", sessionId, accountId);
        }
    }

    /// <summary>
    /// Gets all subscriber sessions for an account from distributed state.
    /// </summary>
    private async Task<List<Guid>> GetSubscriberSessionsAsync(Guid accountId)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.game-session", "GameSessionService.GetSubscriberSessions");

        try
        {
            var key = SUBSCRIBER_SESSIONS_PREFIX + accountId.ToString();

            var existing = await _subscriberSessionStore.GetAsync(key);
            return existing?.SessionIds.ToList() ?? new List<Guid>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get subscriber sessions for account {AccountId}", accountId);
            return new List<Guid>();
        }
    }

    /// <summary>
    /// Checks if a session is a valid subscriber session for an account.
    /// Used for join validation.
    /// </summary>
    private async Task<bool> IsValidSubscriberSessionAsync(Guid accountId, Guid sessionId)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.game-session", "GameSessionService.IsValidSubscriberSession");

        try
        {
            var key = SUBSCRIBER_SESSIONS_PREFIX + accountId.ToString();

            var existing = await _subscriberSessionStore.GetAsync(key);
            return existing?.SessionIds.Contains(sessionId) == true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to validate subscriber session for account {AccountId}", accountId);
            return false;
        }
    }

    /// <summary>
    /// Publishes a join shortcut for a session to access a game lobby.
    /// </summary>
    private async Task PublishJoinShortcutAsync(Guid sessionId, Guid accountId, string stubName)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.game-session", "GameSessionService.PublishJoinShortcut");

        try
        {
            // Get or create the lobby for this game service (internal state ID, not exposed to client)
            var lobbyId = await GetOrCreateLobbySessionAsync(stubName);
            if (lobbyId == null)
            {
                _logger.LogWarning("Failed to get/create lobby for {StubName}, cannot publish shortcut", stubName);
                return;
            }

            var shortcutName = $"join_game_{stubName.ToLowerInvariant()}";

            // Generate shortcut GUID (v7 for shortcuts - session-unique)
            var sessionIdStr = sessionId.ToString();
            var routeGuid = GuidGenerator.GenerateSessionShortcutGuid(
                sessionIdStr,
                shortcutName,
                StateStoreDefinitions.GameSessionLock,
                _serverSalt);

            // Generate target GUID (v5 for service capability)
            var targetGuid = GuidGenerator.GenerateServiceGuid(
                sessionIdStr,
                "game-session/sessions/join",
                _serverSalt);

            // Create the pre-bound payload with WebSocket sessionId, accountId, and gameType
            // sessionId here is the WebSocket session ID - used for event delivery to this client
            // gameType determines which lobby to join
            var boundPayload = new JoinGameSessionRequest
            {
                SessionId = sessionId,
                AccountId = accountId,
                GameType = stubName  // e.g., generic
            };

            var shortcutEvent = new ShortcutPublishedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SessionId = sessionId,
                Shortcut = new SessionShortcut
                {
                    RouteGuid = routeGuid,
                    TargetGuid = targetGuid,
                    BoundPayload = BannouJson.Serialize(boundPayload),
                    Metadata = new SessionShortcutMetadata
                    {
                        Name = shortcutName,
                        Description = $"Join the {stubName} game lobby",
                        SourceService = StateStoreDefinitions.GameSessionLock,
                        TargetService = StateStoreDefinitions.GameSessionLock,
                        TargetMethod = "POST",
                        TargetEndpoint = "/sessions/join",
                        CreatedAt = DateTimeOffset.UtcNow
                    }
                },
                ReplaceExisting = true
            };

            // Publish to session-specific client event channel using direct exchange
            // CRITICAL: Must use IClientEventPublisher for session-specific events (IMPLEMENTATION TENETS)
            // Using _messageBus directly would publish to fanout exchange "bannou" instead
            // of direct exchange "bannou-client-events" with proper routing key
            var published = await _clientEventPublisher.PublishToSessionAsync(sessionIdStr, shortcutEvent);
            if (published)
            {
                _logger.LogInformation("Published join shortcut {RouteGuid} for session {SessionId} -> lobby {LobbyId} ({StubName})",
                    routeGuid, sessionId, lobbyId, stubName);
            }
            else
            {
                _logger.LogWarning("Failed to publish join shortcut to session {SessionId}", sessionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish join shortcut for session {SessionId}, stub {StubName}", sessionId, stubName);
        }
    }

    /// <summary>
    /// Revokes all shortcuts from game-session service for a session.
    /// </summary>
    private async Task RevokeShortcutsForSessionAsync(Guid sessionId, string stubName)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.game-session", "GameSessionService.RevokeShortcutsForSession");

        try
        {
            var revokeEvent = new ShortcutRevokedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SessionId = sessionId,
                RevokeByService = StateStoreDefinitions.GameSessionLock,
                Reason = $"Subscription to {stubName} ended"
            };

            // Publish to session-specific client event channel using direct exchange
            // CRITICAL: Must use IClientEventPublisher for session-specific events (IMPLEMENTATION TENETS)
            var published = await _clientEventPublisher.PublishToSessionAsync(sessionId.ToString(), revokeEvent);
            if (published)
            {
                _logger.LogInformation("Revoked game-session shortcuts for session {SessionId} (reason: {StubName} subscription ended)",
                    sessionId, stubName);
            }
            else
            {
                _logger.LogWarning("Failed to publish shortcut revocation to session {SessionId}", sessionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to revoke shortcuts for session {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// Gets or creates a lobby session for a game service.
    /// Lobbies are persistent game sessions that serve as entry points for subscribed users.
    /// </summary>
    private async Task<Guid?> GetOrCreateLobbySessionAsync(string stubName)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.game-session", "GameSessionService.GetOrCreateLobbySession");

        var lobbyKey = LOBBY_KEY_PREFIX + stubName.ToLowerInvariant();

        try
        {


            // Check for existing lobby (fast path without lock)
            var existingLobby = await _sessionStore.GetAsync(lobbyKey);
            if (existingLobby != null && existingLobby.Status != SessionStatus.Finished)
            {
                _logger.LogDebug("Found existing lobby {LobbyId} for {StubName}", existingLobby.SessionId, stubName);
                return existingLobby.SessionId;
            }

            // Lock on lobby key to prevent duplicate lobby creation across instances
            await using var lobbyLock = await _lockProvider.LockAsync(
                StateStoreDefinitions.GameSessionLock, lobbyKey, Guid.NewGuid().ToString(), _configuration.LockTimeoutSeconds);
            if (!lobbyLock.Success)
            {
                _logger.LogWarning("Could not acquire lobby lock for {StubName}, retrying read", stubName);
                // Another instance may have created the lobby while we waited
                existingLobby = await _sessionStore.GetAsync(lobbyKey);
                return existingLobby?.SessionId;
            }

            // Re-check under lock (another instance may have created it)
            existingLobby = await _sessionStore.GetAsync(lobbyKey);
            if (existingLobby != null && existingLobby.Status != SessionStatus.Finished)
            {
                _logger.LogDebug("Found existing lobby {LobbyId} for {StubName} (created by another instance)", existingLobby.SessionId, stubName);
                return existingLobby.SessionId;
            }

            // Create new lobby
            var lobbyId = Guid.NewGuid();

            var lobby = new GameSessionModel
            {
                SessionId = lobbyId,
                SessionName = $"{stubName} Lobby",
                GameType = stubName,
                MaxPlayers = _configuration.DefaultLobbyMaxPlayers,
                IsPrivate = false,
                Status = SessionStatus.Active,
                CurrentPlayers = 0,
                Players = new List<GamePlayer>(),
                CreatedAt = DateTimeOffset.UtcNow,
                Owner = null // System-owned lobby
            };

            // Save the lobby
            await _sessionStore.SaveAsync(SESSION_KEY_PREFIX + lobbyId, lobby, SessionTtlOptions);
            await _sessionStore.SaveAsync(lobbyKey, lobby, SessionTtlOptions);

            // Add to session list under distributed lock (read-modify-write)
            await using var listLock = await _lockProvider.LockAsync(
                StateStoreDefinitions.GameSessionLock, SESSION_LIST_KEY, Guid.NewGuid().ToString(), _configuration.LockTimeoutSeconds);
            if (listLock.Success)
            {
                var sessionIds = await _sessionListStore.GetAsync(SESSION_LIST_KEY) ?? new List<string>();
                sessionIds.Add(lobbyId.ToString());
                await _sessionListStore.SaveAsync(SESSION_LIST_KEY, sessionIds);
            }
            else
            {
                _logger.LogWarning("Could not acquire session-list lock when creating lobby {LobbyId} for {StubName}", lobbyId, stubName);
            }

            _logger.LogInformation("Created lobby {LobbyId} for {StubName}", lobbyId, stubName);
            return lobbyId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get/create lobby for {StubName}", stubName);
            return null;
        }
    }

    /// <summary>
    /// Gets an existing lobby session for a game type (does NOT create if missing/finished).
    /// Use this for Join/Leave/Action operations that require an existing active lobby.
    /// </summary>
    private async Task<Guid?> GetLobbySessionAsync(string gameType)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.game-session", "GameSessionService.GetLobbySession");

        var lobbyKey = LOBBY_KEY_PREFIX + gameType.ToLowerInvariant();

        try
        {

            var existingLobby = await _sessionStore.GetAsync(lobbyKey);

            if (existingLobby != null)
            {
                _logger.LogDebug("Found lobby {LobbyId} for {GameType} with status {Status}",
                    existingLobby.SessionId, gameType, existingLobby.Status);
                return existingLobby.SessionId;
            }

            _logger.LogDebug("No lobby found for {GameType}", gameType);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get lobby for {GameType}", gameType);
            return null;
        }
    }

    /// <summary>
    /// Checks if a service stub name is handled by this service.
    /// </summary>
    private bool IsOurService(string stubName)
    {
        return _supportedGameServices.Contains(stubName);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Checks if a game service has autoLobbyEnabled set to true.
    /// Returns true (fail-open) if the game service cannot be queried,
    /// preserving backward-compatible shortcut publishing behavior.
    /// </summary>
    /// <param name="stubName">The game service stub name to check.</param>
    /// <returns>True if auto-lobby is enabled or the check failed; false if explicitly disabled.</returns>
    private async Task<bool> IsAutoLobbyEnabledAsync(string stubName)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.game-session", "GameSessionService.IsAutoLobbyEnabled");

        try
        {
            var service = await _gameServiceClient.GetServiceAsync(
                new GetServiceRequest { StubName = stubName });
            return service.AutoLobbyEnabled;
        }
        catch (ApiException ex)
        {
            // Game service not found or returned an error status - default to publishing shortcuts
            // for backward compatibility (fail-open per IMPLEMENTATION TENETS)
            _logger.LogWarning(ex,
                "Failed to check autoLobbyEnabled for {StubName} (status {StatusCode}), defaulting to enabled",
                stubName, ex.StatusCode);
            return true;
        }
        catch (Exception ex)
        {
            // Infrastructure failure (timeout, connection refused, etc.) - fail-open
            _logger.LogWarning(ex,
                "Unexpected error checking autoLobbyEnabled for {StubName}, defaulting to enabled",
                stubName);
            return true;
        }
    }

    private async Task<GameSessionResponse?> LoadSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.game-session", "GameSessionService.LoadSession");

        var model = await _sessionStore
            .GetAsync(SESSION_KEY_PREFIX + sessionId, cancellationToken);

        return model != null ? MapModelToResponse(model) : null;
    }

    private static GameSessionResponse MapModelToResponse(GameSessionModel model)
    {
        return new GameSessionResponse
        {
            SessionId = model.SessionId,
            GameType = model.GameType,
            SessionType = model.SessionType,
            SessionName = model.SessionName,
            Status = model.Status,
            MaxPlayers = model.MaxPlayers,
            CurrentPlayers = model.CurrentPlayers,
            IsPrivate = model.IsPrivate,
            Owner = model.Owner,
            Players = model.Players,
            CreatedAt = model.CreatedAt,
            GameSettings = model.GameSettings ?? new object(),
            Reservations = model.Reservations.Count > 0
                ? model.Reservations.Select(r => new ReservationInfo
                {
                    AccountId = r.AccountId,
                    Token = r.Token,
                    ExpiresAt = model.ReservationExpiresAt ?? DateTimeOffset.UtcNow
                }).ToList()
                : null,
            ReservationExpiresAt = model.ReservationExpiresAt
        };
    }

    /// <summary>
    /// Builds a lifecycle Updated event from the current session model.
    /// Used to publish game-session.updated after any session state mutation.
    /// </summary>
    /// <param name="model">The session model after mutation.</param>
    /// <param name="changedFields">List of fields that were modified.</param>
    private static GameSessionUpdatedEvent BuildUpdatedEvent(GameSessionModel model, params string[] changedFields)
    {
        return new GameSessionUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SessionId = model.SessionId,
            GameType = model.GameType,
            SessionType = model.SessionType,
            SessionName = model.SessionName,
            Status = model.Status,
            MaxPlayers = model.MaxPlayers,
            CurrentPlayers = model.CurrentPlayers,
            IsPrivate = model.IsPrivate,
            Owner = model.Owner,
            CreatedAt = model.CreatedAt,
            GameSettings = model.GameSettings,
            ReservationExpiresAt = model.ReservationExpiresAt,
            ChangedFields = changedFields.ToList()
        };
    }

    /// <summary>
    /// Builds a lifecycle Deleted event from the session model.
    /// Used to publish game-session.deleted when a session is removed.
    /// </summary>
    /// <param name="model">The session model being deleted.</param>
    /// <param name="reason">Optional reason for deletion.</param>
    internal static GameSessionDeletedEvent BuildDeletedEvent(GameSessionModel model, string? reason = null)
    {
        return new GameSessionDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SessionId = model.SessionId,
            GameType = model.GameType,
            SessionType = model.SessionType,
            SessionName = model.SessionName,
            Status = model.Status,
            MaxPlayers = model.MaxPlayers,
            CurrentPlayers = model.CurrentPlayers,
            IsPrivate = model.IsPrivate,
            Owner = model.Owner,
            CreatedAt = model.CreatedAt,
            GameSettings = model.GameSettings,
            ReservationExpiresAt = model.ReservationExpiresAt,
            DeletedReason = reason
        };
    }

    /// <summary>
    /// Generates a secure random token for session reservations.
    /// Uses cryptographically secure random bytes encoded as base64.
    /// </summary>
    private static string GenerateReservationToken()
    {
        var bytes = new byte[32];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    #endregion

    #region Permission Registration

    #endregion
}
