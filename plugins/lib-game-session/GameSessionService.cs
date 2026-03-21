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

/// <summary>
/// GameSession service implementation.
/// Manages game sessions for multiplayer games.
/// Handles session shortcuts for subscribed accounts via mesh pubsub events.
/// </summary>
[BannouService("game-session", typeof(IGameSessionService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.GameFoundation)]
public partial class GameSessionService : IGameSessionService
{
    /// <summary>Ephemeral store for individual game session records (Redis).</summary>
    private readonly IStateStore<GameSessionModel> _sessionStore;

    /// <summary>Ephemeral store for per-game session ID lists (Redis).</summary>
    private readonly IStateStore<List<string>> _sessionListStore;

    /// <summary>Ephemeral store for per-subscriber active session mappings (Redis).</summary>
    private readonly IStateStore<SubscriberSessionsModel> _subscriberSessionStore;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<GameSessionService> _logger;
    private readonly GameSessionServiceConfiguration _configuration;
    private readonly IPermissionClient _permissionClient;
    private readonly ISubscriptionClient _subscriptionClient;
    private readonly IClientEventPublisher _clientEventPublisher;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly IConnectClient _connectClient;
    private readonly IGameServiceClient _gameServiceClient;
    private readonly ITelemetryProvider _telemetryProvider;

    internal const string SESSION_KEY_PREFIX = "session:";
    internal const string SESSION_LIST_KEY = "session-list";
    private const string LOBBY_KEY_PREFIX = "lobby:";
    private const string SUBSCRIBER_SESSIONS_PREFIX = "subscriber-sessions:";

    #region Key Building Helpers

    internal static string BuildSessionKey(Guid sessionId)
        => $"{SESSION_KEY_PREFIX}{sessionId}";

    internal static string BuildLobbyKey(string stubName)
        => $"{LOBBY_KEY_PREFIX}{stubName}";

    internal static string BuildSubscriberSessionsKey(Guid accountId)
        => $"{SUBSCRIBER_SESSIONS_PREFIX}{accountId}";

    #endregion

    /// <summary>
    /// Game service stub names that this service handles, populated from configuration.
    /// </summary>
    private readonly HashSet<string> _supportedGameServices;

    /// <summary>
    /// Local cache of subscribed accounts: AccountId -> Set of subscribed stubNames.
    /// Used for fast filtering of session.connected events - we only care about subscribed accounts.
    /// Loaded on service startup from Subscription service, updated via subscription.updated events.
    /// This is a local filter cache - authoritative subscriber session state is in lib-state.
    /// </summary>
    private static readonly ConcurrentDictionary<Guid, HashSet<string>> _accountSubscriptions = new();

    /// <summary>
    /// Adds an account subscription to the local filter cache.
    /// Called by GameSessionStartupService during initialization.
    /// Thread-safe for concurrent access.
    /// </summary>
    /// <param name="accountId">The account ID to add.</param>
    /// <param name="stubName">The service stub name the account is subscribed to.</param>
    public static void AddAccountSubscription(Guid accountId, string stubName)
    {
        var stubs = _accountSubscriptions.GetOrAdd(accountId, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        lock (stubs)
        {
            stubs.Add(stubName);
        }
    }

    /// <summary>
    /// Removes an account subscription from the local filter cache.
    /// If the account has no remaining subscriptions, the entry is evicted entirely
    /// to prevent unbounded cache growth from accounts that cancel all subscriptions.
    /// Thread-safe for concurrent access.
    /// </summary>
    /// <param name="accountId">The account ID to update.</param>
    /// <param name="stubName">The service stub name to remove.</param>
    public static void RemoveAccountSubscription(Guid accountId, string stubName)
    {
        if (_accountSubscriptions.TryGetValue(accountId, out var stubs))
        {
            bool shouldRemoveEntry;
            lock (stubs)
            {
                stubs.Remove(stubName);
                shouldRemoveEntry = stubs.Count == 0;
            }

            if (shouldRemoveEntry)
            {
                _accountSubscriptions.TryRemove(accountId, out _);
            }
        }
    }

    /// <summary>
    /// Server salt for GUID generation. Comes from configuration, with fallback to random generation for development.
    /// Instance-based to support configuration injection (IMPLEMENTATION TENETS).
    /// </summary>
    private readonly string _serverSalt;

    /// <summary>
    /// State options for session saves. Applies TTL when DefaultSessionTimeoutSeconds is configured.
    /// When null, sessions have no expiry.
    /// </summary>
    private StateOptions? SessionTtlOptions => _configuration.DefaultSessionTimeoutSeconds.HasValue
        ? new StateOptions { Ttl = _configuration.DefaultSessionTimeoutSeconds.Value }
        : null;

    /// <summary>
    /// Creates a new GameSessionService instance.
    /// </summary>
    /// <param name="stateStoreFactory">State store factory for resolving state stores (FOUNDATION TENETS).</param>
    /// <param name="messageBus">Message bus for pub/sub operations.</param>
    /// <param name="logger">Logger for this service.</param>
    /// <param name="configuration">Service configuration.</param>
    /// <param name="eventConsumer">Event consumer for pub/sub fan-out.</param>
    /// <param name="clientEventPublisher">Client event publisher for pushing events to WebSocket clients.</param>
    /// <param name="permissionClient">Permission client for setting game-session:in_game state.</param>
    /// <param name="subscriptionClient">Subscription client for fetching account subscriptions.</param>
    /// <param name="connectClient">Connect client for querying connected sessions per account.</param>
    /// <param name="gameServiceClient">Game service client for checking autoLobbyEnabled on game service definitions.</param>
    /// <param name="telemetryProvider">Telemetry provider for distributed tracing spans.</param>
    public GameSessionService(
        IStateStoreFactory stateStoreFactory,
        IMessageBus messageBus,
        ILogger<GameSessionService> logger,
        GameSessionServiceConfiguration configuration,
        IEventConsumer eventConsumer,
        IClientEventPublisher clientEventPublisher,
        IPermissionClient permissionClient,
        ISubscriptionClient subscriptionClient,
        IDistributedLockProvider lockProvider,
        IConnectClient connectClient,
        IGameServiceClient gameServiceClient,
        ITelemetryProvider telemetryProvider)
    {
        _sessionStore = stateStoreFactory.GetStore<GameSessionModel>(StateStoreDefinitions.GameSession);
        _sessionListStore = stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.GameSession);
        _subscriberSessionStore = stateStoreFactory.GetStore<SubscriberSessionsModel>(StateStoreDefinitions.GameSession);
        _messageBus = messageBus;
        _logger = logger;
        _configuration = configuration;
        _clientEventPublisher = clientEventPublisher;
        _permissionClient = permissionClient;
        _subscriptionClient = subscriptionClient;
        _lockProvider = lockProvider;
        _connectClient = connectClient;
        _gameServiceClient = gameServiceClient;
        _telemetryProvider = telemetryProvider;

        // Initialize supported game services from configuration (typed array per IMPLEMENTATION TENETS)
        _supportedGameServices = new HashSet<string>(configuration.SupportedGameServices, StringComparer.OrdinalIgnoreCase);

        // Server salt from configuration - all instances must share the same salt
        _serverSalt = configuration.ServerSalt;

        // Register event handlers via partial class (GameSessionServiceEvents.cs)
        RegisterEventConsumers(eventConsumer);
    }

    /// <summary>
    /// Lists game sessions with optional filtering by game type and status.
    /// </summary>
    public async Task<(StatusCodes, GameSessionListResponse?)> ListGameSessionsAsync(
        ListGameSessionsRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Listing game sessions - GameType: {GameType}, Status: {Status}",
            body.GameType, body.Status);

        // Get all session IDs
        var sessionIds = await _sessionListStore
            .GetAsync(SESSION_LIST_KEY, cancellationToken) ?? new List<string>();

        var sessions = new List<GameSessionResponse>();

        foreach (var sessionId in sessionIds)
        {
            var session = await LoadSessionAsync(sessionId, cancellationToken);
            if (session == null)
            {
                _logger.LogWarning("Session {SessionId} in index but failed to load - possible data inconsistency", sessionId);
                continue;
            }

            // Apply game type filter if provided
            if (body.GameType != null && session.GameType != body.GameType)
                continue;

            // Apply status filter if provided; otherwise skip finished sessions by default
            if (body.Status.HasValue)
            {
                if (session.Status != body.Status.Value)
                    continue;
            }
            else if (session.Status == SessionStatus.Finished)
            {
                continue;
            }

            sessions.Add(session);
        }

        var response = new GameSessionListResponse
        {
            Sessions = sessions,
            TotalCount = sessions.Count
        };

        _logger.LogDebug("Returning {Count} game sessions", sessions.Count);
        return (StatusCodes.OK, response);
    }

    /// <summary>
    /// Creates a new game session.
    /// </summary>
    public async Task<(StatusCodes, GameSessionResponse?)> CreateGameSessionAsync(
        CreateGameSessionRequest body,
        CancellationToken cancellationToken = default)
    {
        var sessionId = Guid.NewGuid();

        _logger.LogDebug("Creating game session {SessionId} - GameType: {GameType}, MaxPlayers: {MaxPlayers}",
            sessionId, body.GameType, body.MaxPlayers);

        // Determine session type (default to lobby if not specified)
        var sessionType = body.SessionType ?? SessionType.Lobby;
        var reservationTtl = body.ReservationTtlSeconds > 0 ? body.ReservationTtlSeconds : _configuration.DefaultReservationTtlSeconds;

        // Enforce max players cap from configuration
        var maxPlayers = body.MaxPlayers > 0
            ? Math.Min(body.MaxPlayers, _configuration.MaxPlayersPerSession)
            : _configuration.MaxPlayersPerSession;

        // Create the session model
        var session = new GameSessionModel
        {
            SessionId = sessionId,
            GameType = body.GameType,
            SessionName = body.SessionName,
            MaxPlayers = maxPlayers,
            IsPrivate = body.IsPrivate,
            Owner = body.OwnerId,
            Status = SessionStatus.Waiting,
            CurrentPlayers = 0,
            Players = new List<GamePlayer>(),
            CreatedAt = DateTimeOffset.UtcNow,
            GameSettings = body.GameSettings,
            SessionType = sessionType
        };

        // For matchmade sessions, create reservations for expected players
        if (sessionType == SessionType.Matchmade && body.ExpectedPlayers != null && body.ExpectedPlayers.Count > 0)
        {
            var reservationExpiry = DateTimeOffset.UtcNow.AddSeconds(reservationTtl);
            session.ReservationExpiresAt = reservationExpiry;

            foreach (var playerAccountId in body.ExpectedPlayers)
            {
                var reservationToken = GenerateReservationToken();
                session.Reservations.Add(new ReservationModel
                {
                    AccountId = playerAccountId,
                    Token = reservationToken,
                    ReservedAt = DateTimeOffset.UtcNow,
                    Claimed = false
                });
            }

            _logger.LogInformation("Created {Count} reservations for matchmade session {SessionId}, expires at {ExpiresAt}",
                session.Reservations.Count, sessionId, reservationExpiry);
        }

        // Acquire session-list lock before saving to prevent orphaned session records
        await using var listLock = await _lockProvider.LockAsync(
            StateStoreDefinitions.GameSessionLock, SESSION_LIST_KEY, Guid.NewGuid().ToString(), _configuration.LockTimeoutSeconds, cancellationToken);
        if (!listLock.Success)
        {
            _logger.LogWarning("Could not acquire session-list lock when creating session {SessionId}", session.SessionId);
            return (StatusCodes.Conflict, null);
        }

        // Save to state store (inside lock so no orphan if list update fails)
        await _sessionStore
            .SaveAsync(BuildSessionKey(session.SessionId), session, SessionTtlOptions, cancellationToken);

        // Add to session list (read-modify-write under lock)
        var sessionIds = await _sessionListStore.GetAsync(SESSION_LIST_KEY, cancellationToken) ?? new List<string>();

        sessionIds.Add(session.SessionId.ToString());

        await _sessionListStore.SaveAsync(SESSION_LIST_KEY, sessionIds, cancellationToken: cancellationToken);

        // Publish event with full model data
        await _messageBus.PublishGameSessionCreatedAsync(
            new GameSessionCreatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SessionId = session.SessionId,
                GameType = session.GameType,
                SessionName = session.SessionName,
                Status = session.Status,
                MaxPlayers = session.MaxPlayers,
                CurrentPlayers = session.CurrentPlayers,
                IsPrivate = session.IsPrivate,
                Owner = session.Owner,
                CreatedAt = session.CreatedAt,
                SessionType = session.SessionType,
                GameSettings = session.GameSettings,
                ReservationExpiresAt = session.ReservationExpiresAt
            });

        var response = MapModelToResponse(session);

        _logger.LogInformation("Game session {SessionId} created successfully", session.SessionId);
        return (StatusCodes.OK, response);
    }

    /// <summary>
    /// Gets a game session by ID.
    /// </summary>
    public async Task<(StatusCodes, GameSessionResponse?)> GetGameSessionAsync(
        GetGameSessionRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting game session {SessionId}", body.SessionId);

        var session = await LoadSessionAsync(body.SessionId.ToString(), cancellationToken);

        if (session == null)
        {
            _logger.LogWarning("Game session {SessionId} not found", body.SessionId);
            return (StatusCodes.NotFound, null);
        }

        return (StatusCodes.OK, session);
    }

    /// <summary>
    /// Joins a game session.
    /// </summary>
    public async Task<(StatusCodes, JoinGameSessionResponse?)> JoinGameSessionAsync(
        JoinGameSessionRequest body,
        CancellationToken cancellationToken = default)
    {
        // body.SessionId is the WebSocket session ID (for event delivery)
        // body.GameType determines which lobby to join
        // body.AccountId identifies the player
        var clientSessionId = body.SessionId;
        var gameType = body.GameType;
        var accountId = body.AccountId;

        _logger.LogInformation("Player {AccountId} joining game {GameType} from session {SessionId}",
            accountId, gameType, clientSessionId);

        // Validate that this session is authorized to join (must be in distributed subscriber sessions)
        // This check ensures the player has an active subscription and a valid connected session
        if (!await IsValidSubscriberSessionAsync(accountId, clientSessionId))
        {
            _logger.LogWarning("Session {SessionId} for account {AccountId} is not a valid subscriber session",
                clientSessionId, accountId);
            return (StatusCodes.Unauthorized, null);
        }

        // Get the lobby for this game type (don't auto-create for join)
        var lobbyId = await GetLobbySessionAsync(gameType);
        if (lobbyId == null)
        {
            _logger.LogWarning("No lobby exists for game type {GameType}", gameType);
            return (StatusCodes.NotFound, null);
        }

        // Acquire lock on session (multiple players may join concurrently)
        var sessionKey = SESSION_KEY_PREFIX + lobbyId.ToString();
        await using var sessionLock = await _lockProvider.LockAsync(
            StateStoreDefinitions.GameSessionLock, sessionKey, Guid.NewGuid().ToString(), _configuration.LockTimeoutSeconds, cancellationToken);
        if (!sessionLock.Success)
        {
            _logger.LogWarning("Could not acquire session lock for lobby {LobbyId}", lobbyId);
            return (StatusCodes.Conflict, null);
        }


        var model = await _sessionStore.GetAsync(sessionKey, cancellationToken);

        if (model == null)
        {
            _logger.LogWarning("Game lobby {LobbyId} not found for game type {GameType}", lobbyId, gameType);
            return (StatusCodes.NotFound, null);
        }

        // Check if session is full
        if (model.CurrentPlayers >= model.MaxPlayers)
        {
            _logger.LogWarning("Game lobby {LobbyId} is full ({Current}/{Max} players)",
                lobbyId, model.CurrentPlayers, model.MaxPlayers);
            return (StatusCodes.Conflict, null);
        }

        // Check session status
        if (model.Status == SessionStatus.Finished)
        {
            _logger.LogWarning("Game lobby {LobbyId} is finished", lobbyId);
            return (StatusCodes.Conflict, null);
        }

        // Check if player already in session
        if (model.Players.Any(p => p.AccountId == accountId))
        {
            _logger.LogWarning("Player {AccountId} already in lobby {LobbyId}", accountId, lobbyId);
            return (StatusCodes.Conflict, null);
        }

        // Add player to session with their WebSocket session ID for event delivery
        var player = new GamePlayer
        {
            AccountId = accountId,
            SessionId = clientSessionId,  // WebSocket session for event delivery
            DisplayName = "Player " + (model.CurrentPlayers + 1),
            Role = PlayerRole.Player,
            JoinedAt = DateTimeOffset.UtcNow,
            CharacterData = body.CharacterData
        };

        model.Players.Add(player);
        model.CurrentPlayers = model.Players.Count;

        // Capture status before mutation for client event
        var previousStatus = model.Status;

        // Update status if full
        if (model.CurrentPlayers >= model.MaxPlayers)
        {
            model.Status = SessionStatus.Full;
        }
        else if (model.Status == SessionStatus.Waiting && model.CurrentPlayers > 0)
        {
            model.Status = SessionStatus.Active;
        }

        // Set game-session:in_game state via Permission service (required for API access)
        try
        {
            await _permissionClient.UpdateSessionStateAsync(new Permission.SessionStateUpdate
            {
                SessionId = body.SessionId,
                ServiceId = StateStoreDefinitions.GameSessionLock,
                NewState = "in_game"
            }, cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Permission service error updating session state for {SessionId}: {StatusCode}",
                body.SessionId, ex.StatusCode);
            // Remove the player we just added since they won't have permissions
            model.Players.Remove(player);
            return (StatusCodes.InternalServerError, null);
        }
        catch (Exception ex) when (ex is not ApiException)
        {
            // Rollback: remove player since they won't have permissions
            model.Players.Remove(player);
            throw;
        }

        // Save updated session
        await _sessionStore.SaveAsync(sessionKey, model, SessionTtlOptions, cancellationToken);

        // Publish domain event (SessionId in event = lobby ID for game session identification)
        await _messageBus.PublishGameSessionPlayerJoinedAsync(
            new GameSessionPlayerJoinedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SessionId = lobbyId.Value,
                AccountId = accountId
            });

        // Publish lifecycle event
        await _messageBus.PublishGameSessionUpdatedAsync(
            BuildUpdatedEvent(model, "currentPlayers", "status"));

        // Push client events to session participants
        var otherSessionIds = model.Players
            .Where(p => p.AccountId != accountId)
            .Select(p => p.SessionId.ToString())
            .ToList();

        if (otherSessionIds.Count > 0)
        {
            await _clientEventPublisher.PublishToSessionsAsync(otherSessionIds, new PlayerJoinedClientEvent
            {
                SessionId = lobbyId.Value,
                Player = new PlayerInfo
                {
                    AccountId = player.AccountId,
                    DisplayName = player.DisplayName,
                    Role = player.Role,
                    CharacterData = player.CharacterData
                },
                CurrentPlayerCount = model.CurrentPlayers,
                MaxPlayers = model.MaxPlayers
            }, cancellationToken);
        }

        if (previousStatus != model.Status)
        {
            var allSessionIds = model.Players.Select(p => p.SessionId.ToString()).ToList();
            await _clientEventPublisher.PublishToSessionsAsync(allSessionIds, new SessionStateChangedClientEvent
            {
                SessionId = lobbyId.Value,
                PreviousState = previousStatus,
                NewState = model.Status,
                Reason = "player_joined",
                ChangedBy = accountId
            }, cancellationToken);
        }

        // Build response (SessionId = lobby ID so client knows which game they joined)
        var response = new JoinGameSessionResponse
        {
            SessionId = lobbyId.Value,
            PlayerRole = PlayerRole.Player,
            GameData = model.GameSettings
        };

        _logger.LogInformation("Player {AccountId} joined game {GameType} (lobby {LobbyId}) from session {ClientSessionId}",
            accountId, gameType, lobbyId, clientSessionId);
        return (StatusCodes.OK, response);
    }

    /// <summary>
    /// Performs a game action within a session.
    /// </summary>
    public async Task<(StatusCodes, GameActionResponse?)> PerformGameActionAsync(
        GameActionRequest body,
        CancellationToken cancellationToken = default)
    {
        // Identify the caller from the X-Bannou-Session-Id header (set by Connect)
        var callerSessionIdStr = ServiceRequestContext.SessionId;
        if (string.IsNullOrEmpty(callerSessionIdStr) || !Guid.TryParse(callerSessionIdStr, out var callerSessionId))
        {
            _logger.LogWarning("Game action rejected: no valid session context");
            return (StatusCodes.Forbidden, null);
        }

        var gameType = body.GameType;
        _logger.LogDebug("Performing game action {ActionType} in game {GameType} by session {SessionId}",
            body.ActionType, gameType, callerSessionId);

        // Get the lobby for this game type (don't auto-create for action)
        var lobbyId = await GetLobbySessionAsync(gameType);
        if (lobbyId == null)
        {
            _logger.LogWarning("No lobby exists for game type {GameType}", gameType);
            return (StatusCodes.NotFound, null);
        }

        var model = await _sessionStore
            .GetAsync(SESSION_KEY_PREFIX + lobbyId.ToString(), cancellationToken);

        if (model == null)
        {
            _logger.LogWarning("Game lobby {LobbyId} not found for action in game {GameType}", lobbyId, gameType);
            return (StatusCodes.NotFound, null);
        }

        if (model.Status == SessionStatus.Finished)
        {
            _logger.LogWarning("Cannot perform action on finished lobby {LobbyId}", lobbyId);
            return (StatusCodes.BadRequest, null);
        }

        // Validate that the caller is actually in this session (by WebSocket session ID, not client input)
        var callerPlayer = model.Players.FirstOrDefault(p => p.SessionId == callerSessionId);
        if (callerPlayer == null)
        {
            _logger.LogWarning("Session {CallerSessionId} is not a member of lobby {LobbyId}", callerSessionId, lobbyId);
            return (StatusCodes.Forbidden, null);
        }

        // Create action response - success only after all validations pass
        var actionId = Guid.NewGuid();
        var actionTimestamp = DateTimeOffset.UtcNow;

        // Publish game action event so other systems can react
        await _messageBus.PublishGameSessionActionPerformedAsync(new GameSessionActionPerformedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = actionTimestamp,
            SessionId = lobbyId.Value,
            WebSocketSessionId = callerSessionId,
            ActionId = actionId,
            ActionType = body.ActionType,
            TargetId = body.TargetId
        });

        var response = new GameActionResponse
        {
            ActionId = actionId
        };

        _logger.LogInformation("Game action {ActionId} performed successfully in lobby {LobbyId}",
            actionId, lobbyId);
        return (StatusCodes.OK, response);
    }

    /// <summary>
    /// Leaves a game session.
    /// </summary>
    public async Task<StatusCodes> LeaveGameSessionAsync(
        LeaveGameSessionRequest body,
        CancellationToken cancellationToken = default)
    {
        // body.SessionId is the WebSocket session ID
        // body.GameType determines which lobby to leave
        var clientSessionId = body.SessionId;
        var gameType = body.GameType;

        _logger.LogInformation("Player leaving game {GameType} from session {SessionId}", gameType, clientSessionId);

        // Get the lobby for this game type (don't auto-create for leave)
        var lobbyId = await GetLobbySessionAsync(gameType);
        if (lobbyId == null)
        {
            _logger.LogWarning("No lobby exists for game type {GameType}", gameType);
            return StatusCodes.NotFound;
        }

        // Acquire lock on session (multiple players may leave concurrently)
        var sessionKey = SESSION_KEY_PREFIX + lobbyId.ToString();
        await using var sessionLock = await _lockProvider.LockAsync(
            StateStoreDefinitions.GameSessionLock, sessionKey, Guid.NewGuid().ToString(), _configuration.LockTimeoutSeconds, cancellationToken);
        if (!sessionLock.Success)
        {
            _logger.LogWarning("Could not acquire session lock for lobby {LobbyId}", lobbyId);
            return StatusCodes.Conflict;
        }


        var model = await _sessionStore.GetAsync(sessionKey, cancellationToken);

        if (model == null)
        {
            _logger.LogWarning("Game lobby {LobbyId} not found for game type {GameType}", lobbyId, gameType);
            return StatusCodes.NotFound;
        }

        // AccountId comes from the request body (populated by shortcut system)
        var accountId = body.AccountId;

        // Find the player in the session
        var leavingPlayer = model.Players.FirstOrDefault(p => p.AccountId == accountId);
        if (leavingPlayer == null)
        {
            _logger.LogWarning("Player {AccountId} not found in lobby {LobbyId}", accountId, lobbyId);
            return StatusCodes.NotFound;
        }

        // Clear game-session:in_game state via Permission service
        try
        {
            await _permissionClient.ClearSessionStateAsync(new Permission.ClearSessionStateRequest
            {
                SessionId = body.SessionId,
                ServiceId = StateStoreDefinitions.GameSessionLock
            }, cancellationToken);
        }
        catch (ApiException ex)
        {
            // Permission service returned an error
            // Continue anyway - player wants to leave, don't trap them; state cleaned up on session expiry
            _logger.LogWarning(ex, "Permission service error clearing session state for {SessionId}: {StatusCode}",
                body.SessionId, ex.StatusCode);
            await _messageBus.TryPublishErrorAsync(
                StateStoreDefinitions.GameSessionLock,
                "ClearSessionState",
                "api_exception",
                ex.Message,
                dependency: "permission",
                endpoint: "post:/permission/clear-session-state",
                details: new { SessionId = body.SessionId, StatusCode = ex.StatusCode },
                stack: ex.StackTrace);
        }
        catch (Exception ex)
        {
            // Unexpected error - this is an internal failure, not user error
            // Continue anyway - player wants to leave, don't trap them; state cleaned up on session expiry
            _logger.LogError(ex, "Failed to clear session state for {SessionId} during leave", body.SessionId);
            await _messageBus.TryPublishErrorAsync(
                StateStoreDefinitions.GameSessionLock,
                "ClearSessionState",
                ex.GetType().Name,
                ex.Message,
                dependency: "permission",
                endpoint: "post:/permission/clear-session-state",
                details: new { SessionId = body.SessionId },
                stack: ex.StackTrace);
        }

        model.Players.Remove(leavingPlayer);
        model.CurrentPlayers = model.Players.Count;

        // Capture status before mutation for client event
        var previousStatus = model.Status;

        // Update status
        if (model.CurrentPlayers == 0)
        {
            model.Status = SessionStatus.Finished;
        }
        else if (model.Status == SessionStatus.Full)
        {
            model.Status = SessionStatus.Active;
        }

        // Save updated session
        await _sessionStore.SaveAsync(sessionKey, model, SessionTtlOptions, cancellationToken);

        // Publish domain event
        await _messageBus.PublishGameSessionPlayerLeftAsync(
            new GameSessionPlayerLeftEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SessionId = lobbyId.Value,
                AccountId = leavingPlayer.AccountId,
                Kicked = false
            });

        // Publish lifecycle event
        await _messageBus.PublishGameSessionUpdatedAsync(
            BuildUpdatedEvent(model, "currentPlayers", "status"));

        // Push client events to remaining session participants
        var remainingSessionIds = model.Players.Select(p => p.SessionId.ToString()).ToList();

        if (remainingSessionIds.Count > 0)
        {
            await _clientEventPublisher.PublishToSessionsAsync(remainingSessionIds, new PlayerLeftClientEvent
            {
                SessionId = lobbyId.Value,
                PlayerId = leavingPlayer.AccountId,
                DisplayName = leavingPlayer.DisplayName,
                CurrentPlayerCount = model.CurrentPlayers
            }, cancellationToken);
        }

        if (previousStatus != model.Status && remainingSessionIds.Count > 0)
        {
            await _clientEventPublisher.PublishToSessionsAsync(remainingSessionIds, new SessionStateChangedClientEvent
            {
                SessionId = lobbyId.Value,
                PreviousState = previousStatus,
                NewState = model.Status,
                Reason = "player_left"
            }, cancellationToken);
        }

        _logger.LogInformation("Player {AccountId} left game {GameType} (lobby {LobbyId})",
            accountId, gameType, lobbyId);

        return StatusCodes.OK;
    }

    /// <summary>
    /// Joins a specific game session by ID.
    /// Used for matchmade games where the session is pre-created by the matchmaking service.
    /// For matchmade sessions, a valid reservation token is required.
    /// </summary>
    public async Task<(StatusCodes, JoinGameSessionResponse?)> JoinGameSessionByIdAsync(
        JoinGameSessionByIdRequest body,
        CancellationToken cancellationToken = default)
    {
        var clientSessionId = body.WebSocketSessionId;
        var gameSessionId = body.GameSessionId.ToString();
        var accountId = body.AccountId;

        _logger.LogInformation("Player {AccountId} joining game session {GameSessionId} from WebSocket session {SessionId}",
            accountId, gameSessionId, clientSessionId);

        // Acquire lock on session (multiple players may join concurrently)
        var sessionKey = SESSION_KEY_PREFIX + gameSessionId;
        await using var sessionLock = await _lockProvider.LockAsync(
            StateStoreDefinitions.GameSessionLock, sessionKey, Guid.NewGuid().ToString(), _configuration.LockTimeoutSeconds, cancellationToken);
        if (!sessionLock.Success)
        {
            _logger.LogWarning("Could not acquire session lock for {GameSessionId}", gameSessionId);
            return (StatusCodes.Conflict, null);
        }


        var model = await _sessionStore.GetAsync(sessionKey, cancellationToken);

        if (model == null)
        {
            _logger.LogWarning("Game session {GameSessionId} not found", gameSessionId);
            return (StatusCodes.NotFound, null);
        }

        // For matchmade sessions, validate reservation token
        if (model.SessionType == SessionType.Matchmade)
        {
            // Check if reservations have expired
            if (model.ReservationExpiresAt.HasValue && DateTimeOffset.UtcNow > model.ReservationExpiresAt.Value)
            {
                _logger.LogWarning("Reservations for session {GameSessionId} have expired", gameSessionId);
                return (StatusCodes.Conflict, null);
            }

            // Find the reservation for this player
            var reservation = model.Reservations.FirstOrDefault(r => r.AccountId == accountId);
            if (reservation == null)
            {
                _logger.LogWarning("No reservation found for player {AccountId} in session {GameSessionId}",
                    accountId, gameSessionId);
                return (StatusCodes.Forbidden, null);
            }

            // Validate reservation token
            if (string.IsNullOrEmpty(body.ReservationToken) || reservation.Token != body.ReservationToken)
            {
                _logger.LogWarning("Invalid reservation token for player {AccountId} in session {GameSessionId}",
                    accountId, gameSessionId);
                return (StatusCodes.Forbidden, null);
            }

            // Check if reservation already claimed
            if (reservation.Claimed)
            {
                _logger.LogWarning("Reservation already claimed for player {AccountId} in session {GameSessionId}",
                    accountId, gameSessionId);
                return (StatusCodes.Conflict, null);
            }

            // Mark reservation as claimed
            reservation.Claimed = true;
            reservation.ClaimedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            // For lobbies, check if session is full
            if (model.CurrentPlayers >= model.MaxPlayers)
            {
                _logger.LogWarning("Game session {GameSessionId} is full ({Current}/{Max} players)",
                    gameSessionId, model.CurrentPlayers, model.MaxPlayers);
                return (StatusCodes.Conflict, null);
            }
        }

        // Check session status
        if (model.Status == SessionStatus.Finished)
        {
            _logger.LogWarning("Game session {GameSessionId} is finished", gameSessionId);
            return (StatusCodes.Conflict, null);
        }

        // Check if player already in session
        if (model.Players.Any(p => p.AccountId == accountId))
        {
            _logger.LogWarning("Player {AccountId} already in session {GameSessionId}", accountId, gameSessionId);
            return (StatusCodes.Conflict, null);
        }

        // Add player to session
        var player = new GamePlayer
        {
            AccountId = accountId,
            SessionId = clientSessionId,
            DisplayName = "Player " + (model.CurrentPlayers + 1),
            Role = PlayerRole.Player,
            JoinedAt = DateTimeOffset.UtcNow,
            CharacterData = body.CharacterData
        };

        model.Players.Add(player);
        model.CurrentPlayers = model.Players.Count;

        // Capture status before mutation for client event
        var previousStatus = model.Status;

        // Update status
        if (model.CurrentPlayers >= model.MaxPlayers)
        {
            model.Status = SessionStatus.Full;
        }
        else if (model.Status == SessionStatus.Waiting && model.CurrentPlayers > 0)
        {
            model.Status = SessionStatus.Active;
        }

        // Set game-session:in_game state via Permission service
        try
        {
            await _permissionClient.UpdateSessionStateAsync(new Permission.SessionStateUpdate
            {
                SessionId = body.WebSocketSessionId,
                ServiceId = StateStoreDefinitions.GameSessionLock,
                NewState = "in_game"
            }, cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Permission service error updating session state for {SessionId}: {StatusCode}",
                body.WebSocketSessionId, ex.StatusCode);
            model.Players.Remove(player);
            // If matchmade, unmark reservation as claimed
            if (model.SessionType == SessionType.Matchmade)
            {
                var reservation = model.Reservations.FirstOrDefault(r => r.AccountId == accountId);
                if (reservation != null)
                {
                    reservation.Claimed = false;
                    reservation.ClaimedAt = null;
                }
            }
            return (StatusCodes.InternalServerError, null);
        }
        catch (Exception ex) when (ex is not ApiException)
        {
            // Rollback: remove player and unmark reservation since they won't have permissions
            model.Players.Remove(player);
            if (model.SessionType == SessionType.Matchmade)
            {
                var reservation = model.Reservations.FirstOrDefault(r => r.AccountId == accountId);
                if (reservation != null)
                {
                    reservation.Claimed = false;
                    reservation.ClaimedAt = null;
                }
            }
            throw;
        }

        // Save updated session
        await _sessionStore.SaveAsync(sessionKey, model, SessionTtlOptions, cancellationToken);

        // Publish domain event
        await _messageBus.PublishGameSessionPlayerJoinedAsync(
            new GameSessionPlayerJoinedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SessionId = body.GameSessionId,
                AccountId = accountId
            });

        // Publish lifecycle event
        await _messageBus.PublishGameSessionUpdatedAsync(
            BuildUpdatedEvent(model, "currentPlayers", "status", "reservations"));

        // Push client events to session participants
        var otherSessionIds = model.Players
            .Where(p => p.AccountId != accountId)
            .Select(p => p.SessionId.ToString())
            .ToList();

        if (otherSessionIds.Count > 0)
        {
            await _clientEventPublisher.PublishToSessionsAsync(otherSessionIds, new PlayerJoinedClientEvent
            {
                SessionId = body.GameSessionId,
                Player = new PlayerInfo
                {
                    AccountId = player.AccountId,
                    DisplayName = player.DisplayName,
                    Role = player.Role,
                    CharacterData = player.CharacterData
                },
                CurrentPlayerCount = model.CurrentPlayers,
                MaxPlayers = model.MaxPlayers
            }, cancellationToken);
        }

        if (previousStatus != model.Status)
        {
            var allSessionIds = model.Players.Select(p => p.SessionId.ToString()).ToList();
            await _clientEventPublisher.PublishToSessionsAsync(allSessionIds, new SessionStateChangedClientEvent
            {
                SessionId = body.GameSessionId,
                PreviousState = previousStatus,
                NewState = model.Status,
                Reason = "player_joined",
                ChangedBy = accountId
            }, cancellationToken);
        }

        // Build response
        var response = new JoinGameSessionResponse
        {
            SessionId = body.GameSessionId,
            PlayerRole = PlayerRole.Player,
            GameData = model.GameSettings
        };

        _logger.LogInformation("Player {AccountId} joined game session {GameSessionId} from WebSocket session {ClientSessionId}",
            accountId, gameSessionId, clientSessionId);
        return (StatusCodes.OK, response);
    }

    /// <summary>
    /// Leaves a specific game session by ID.
    /// Alternative to LeaveGameSessionAsync that takes session ID directly.
    /// </summary>
    public async Task<StatusCodes> LeaveGameSessionByIdAsync(
        LeaveGameSessionByIdRequest body,
        CancellationToken cancellationToken = default)
    {
        var clientSessionId = body.WebSocketSessionId;
        var gameSessionId = body.GameSessionId.ToString();
        var accountId = body.AccountId;

        _logger.LogInformation("Player {AccountId} leaving game session {GameSessionId} from WebSocket session {SessionId}",
            accountId, gameSessionId, clientSessionId);

        // Acquire lock on session (multiple players may leave concurrently)
        var sessionKey = SESSION_KEY_PREFIX + gameSessionId;
        await using var sessionLock = await _lockProvider.LockAsync(
            StateStoreDefinitions.GameSessionLock, sessionKey, Guid.NewGuid().ToString(), _configuration.LockTimeoutSeconds, cancellationToken);
        if (!sessionLock.Success)
        {
            _logger.LogWarning("Could not acquire session lock for {GameSessionId}", gameSessionId);
            return StatusCodes.Conflict;
        }


        var model = await _sessionStore.GetAsync(sessionKey, cancellationToken);

        if (model == null)
        {
            _logger.LogWarning("Game session {GameSessionId} not found", gameSessionId);
            return StatusCodes.NotFound;
        }

        // Find the player in the session
        var leavingPlayer = model.Players.FirstOrDefault(p => p.AccountId == accountId);
        if (leavingPlayer == null)
        {
            _logger.LogWarning("Player {AccountId} not found in session {GameSessionId}", accountId, gameSessionId);
            return StatusCodes.NotFound;
        }

        // Clear game-session:in_game state via Permission service
        // Skip when WebSocketSessionId is null (server-side cleanup with no real session)
        if (body.WebSocketSessionId.HasValue)
        {
            try
            {
                await _permissionClient.ClearSessionStateAsync(new Permission.ClearSessionStateRequest
                {
                    SessionId = body.WebSocketSessionId.Value,
                    ServiceId = StateStoreDefinitions.GameSessionLock
                }, cancellationToken);
            }
            catch (ApiException ex)
            {
                // Permission service returned an error - continue anyway, state cleaned up on session expiry
                _logger.LogWarning(ex, "Permission service error clearing session state for {SessionId}: {StatusCode}",
                    body.WebSocketSessionId.Value, ex.StatusCode);
                await _messageBus.TryPublishErrorAsync(
                    StateStoreDefinitions.GameSessionLock,
                    "ClearSessionState",
                    "api_exception",
                    ex.Message,
                    dependency: "permission",
                    endpoint: "post:/permission/clear-session-state",
                    details: new { SessionId = body.WebSocketSessionId.Value, StatusCode = ex.StatusCode },
                    stack: ex.StackTrace);
            }
            catch (Exception ex)
            {
                // Unexpected error - continue anyway, state cleaned up on session expiry
                _logger.LogError(ex, "Failed to clear session state for {SessionId} during leave", body.WebSocketSessionId.Value);
                await _messageBus.TryPublishErrorAsync(
                    StateStoreDefinitions.GameSessionLock,
                    "ClearSessionState",
                    ex.GetType().Name,
                    ex.Message,
                    dependency: "permission",
                    endpoint: "post:/permission/clear-session-state",
                    details: new { SessionId = body.WebSocketSessionId.Value },
                    stack: ex.StackTrace);
            }
        }

        model.Players.Remove(leavingPlayer);
        model.CurrentPlayers = model.Players.Count;

        // Capture status before mutation for client event
        var previousStatus = model.Status;

        // Update status
        if (model.CurrentPlayers == 0)
        {
            model.Status = SessionStatus.Finished;
        }
        else if (model.Status == SessionStatus.Full)
        {
            model.Status = SessionStatus.Active;
        }

        // Save updated session
        await _sessionStore.SaveAsync(sessionKey, model, SessionTtlOptions, cancellationToken);

        // Publish domain event
        await _messageBus.PublishGameSessionPlayerLeftAsync(
            new GameSessionPlayerLeftEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SessionId = body.GameSessionId,
                AccountId = leavingPlayer.AccountId,
                Kicked = false
            });

        // Publish lifecycle event
        await _messageBus.PublishGameSessionUpdatedAsync(
            BuildUpdatedEvent(model, "currentPlayers", "status"));

        // Push client events to remaining session participants
        var remainingSessionIds = model.Players.Select(p => p.SessionId.ToString()).ToList();

        if (remainingSessionIds.Count > 0)
        {
            await _clientEventPublisher.PublishToSessionsAsync(remainingSessionIds, new PlayerLeftClientEvent
            {
                SessionId = body.GameSessionId,
                PlayerId = leavingPlayer.AccountId,
                DisplayName = leavingPlayer.DisplayName,
                CurrentPlayerCount = model.CurrentPlayers
            }, cancellationToken);
        }

        if (previousStatus != model.Status && remainingSessionIds.Count > 0)
        {
            await _clientEventPublisher.PublishToSessionsAsync(remainingSessionIds, new SessionStateChangedClientEvent
            {
                SessionId = body.GameSessionId,
                PreviousState = previousStatus,
                NewState = model.Status,
                Reason = "player_left"
            }, cancellationToken);
        }

        _logger.LogInformation("Player {AccountId} left game session {GameSessionId}", accountId, gameSessionId);
        return StatusCodes.OK;
    }

    /// <summary>
    /// Publishes a join shortcut for a matchmade session to a specific player.
    /// Called by the matchmaking service after creating a session with reservations.
    /// </summary>
    public async Task<(StatusCodes, PublishJoinShortcutResponse?)> PublishJoinShortcutAsync(
        PublishJoinShortcutRequest body,
        CancellationToken cancellationToken = default)
    {
        var targetSessionId = body.TargetWebSocketSessionId;
        var targetSessionIdStr = targetSessionId.ToString();
        var gameSessionId = body.GameSessionId.ToString();
        var accountId = body.AccountId;
        var reservationToken = body.ReservationToken;

        _logger.LogInformation("Publishing join shortcut for game session {GameSessionId} to WebSocket session {TargetSessionId}",
            gameSessionId, targetSessionId);

        // Verify the game session exists

        var model = await _sessionStore.GetAsync(SESSION_KEY_PREFIX + gameSessionId, cancellationToken);

        if (model == null)
        {
            _logger.LogWarning("Game session {GameSessionId} not found for shortcut publishing", gameSessionId);
            return (StatusCodes.NotFound, null);
        }

        // Verify the reservation token is valid
        var reservation = model.Reservations.FirstOrDefault(r => r.AccountId == accountId && r.Token == reservationToken);
        if (reservation == null)
        {
            _logger.LogWarning("Invalid reservation token for player {AccountId} in session {GameSessionId}",
                accountId, gameSessionId);
            return (StatusCodes.BadRequest, null);
        }

        var shortcutName = $"join_match_{gameSessionId}";

        // Generate shortcut GUID (v7 for shortcuts - session-unique)
        var routeGuid = GuidGenerator.GenerateSessionShortcutGuid(
            targetSessionIdStr,
            shortcutName,
            StateStoreDefinitions.GameSessionLock,
            _serverSalt);

        // Generate target GUID (v5 for service capability) - points to join-session endpoint
        var targetGuid = GuidGenerator.GenerateServiceGuid(
            targetSessionIdStr,
            "game-session/sessions/join-session",
            _serverSalt);

        // Build the pre-bound request for the shortcut
        var preboundRequest = new JoinGameSessionByIdRequest
        {
            WebSocketSessionId = targetSessionId,
            AccountId = accountId,
            GameSessionId = body.GameSessionId,
            ReservationToken = reservationToken
        };

        // Publish the shortcut to the player's WebSocket session
        var shortcutEvent = new ShortcutPublishedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SessionId = targetSessionId,
            Shortcut = new SessionShortcut
            {
                RouteGuid = routeGuid,
                TargetGuid = targetGuid,
                BoundPayload = BannouJson.Serialize(preboundRequest),
                Metadata = new SessionShortcutMetadata
                {
                    Name = shortcutName,
                    Description = $"Join matchmade game session {gameSessionId}",
                    SourceService = StateStoreDefinitions.GameSessionLock,
                    TargetService = StateStoreDefinitions.GameSessionLock,
                    TargetMethod = "POST",
                    TargetEndpoint = "/sessions/join-session",
                    CreatedAt = DateTimeOffset.UtcNow,
                    ExpiresAt = model.ReservationExpiresAt
                }
            },
            ReplaceExisting = true
        };

        await _clientEventPublisher.PublishToSessionAsync(targetSessionIdStr, shortcutEvent);

        _logger.LogInformation("Published join shortcut for game session {GameSessionId} to WebSocket session {TargetSessionId} with route GUID {RouteGuid}",
            gameSessionId, targetSessionId, routeGuid);

        return (StatusCodes.OK, new PublishJoinShortcutResponse
        {
            ShortcutRouteGuid = routeGuid
        });
    }

    /// <summary>
    /// Kicks a player from a game session.
    /// </summary>
    public async Task<StatusCodes> KickPlayerAsync(
        KickPlayerRequest body,
        CancellationToken cancellationToken = default)
    {
        var sessionId = body.SessionId.ToString();
        var targetAccountId = body.TargetAccountId;

        _logger.LogDebug("Kicking player {TargetAccountId} from session {SessionId}. Reason: {Reason}",
            targetAccountId, sessionId, body.Reason);

        // Acquire lock on session (concurrent modification protection)
        var sessionKey = SESSION_KEY_PREFIX + sessionId;
        await using var sessionLock = await _lockProvider.LockAsync(
            StateStoreDefinitions.GameSessionLock, sessionKey, Guid.NewGuid().ToString(), _configuration.LockTimeoutSeconds, cancellationToken);
        if (!sessionLock.Success)
        {
            _logger.LogWarning("Could not acquire session lock for {SessionId}", sessionId);
            return StatusCodes.Conflict;
        }


        var model = await _sessionStore.GetAsync(sessionKey, cancellationToken);

        if (model == null)
        {
            _logger.LogWarning("Game session {SessionId} not found for kick", sessionId);
            return StatusCodes.NotFound;
        }

        // Find and remove the player
        var playerToKick = model.Players.FirstOrDefault(p => p.AccountId == targetAccountId);
        if (playerToKick == null)
        {
            _logger.LogWarning("Player {TargetAccountId} not found in session {SessionId}",
                targetAccountId, sessionId);
            return StatusCodes.NotFound;
        }

        // Capture kicked player info for client events before removal
        var kickedPlayerSessionId = playerToKick.SessionId.ToString();
        var kickedPlayerDisplayName = playerToKick.DisplayName;

        model.Players.Remove(playerToKick);
        model.CurrentPlayers = model.Players.Count;

        // Capture status before mutation for client event
        var previousStatus = model.Status;

        // Clear game-session:in_game state via Permission service for the kicked player
        try
        {
            await _permissionClient.ClearSessionStateAsync(new Permission.ClearSessionStateRequest
            {
                SessionId = playerToKick.SessionId,
                ServiceId = StateStoreDefinitions.GameSessionLock
            }, cancellationToken);
        }
        catch (ApiException ex)
        {
            // Permission service returned an error - continue anyway, state cleaned up on session expiry
            _logger.LogWarning(ex, "Permission service error clearing session state for kicked player {SessionId}: {StatusCode}",
                playerToKick.SessionId, ex.StatusCode);
            await _messageBus.TryPublishErrorAsync(
                StateStoreDefinitions.GameSessionLock,
                "ClearSessionState",
                "api_exception",
                ex.Message,
                dependency: "permission",
                endpoint: "post:/permission/clear-session-state",
                details: new { SessionId = playerToKick.SessionId, StatusCode = ex.StatusCode },
                stack: ex.StackTrace);
        }
        catch (Exception ex)
        {
            // Unexpected error - continue anyway, state cleaned up on session expiry
            _logger.LogError(ex, "Failed to clear session state for kicked player {SessionId}", playerToKick.SessionId);
            await _messageBus.TryPublishErrorAsync(
                StateStoreDefinitions.GameSessionLock,
                "ClearSessionState",
                ex.GetType().Name,
                ex.Message,
                dependency: "permission",
                endpoint: "post:/permission/clear-session-state",
                details: new { SessionId = playerToKick.SessionId },
                stack: ex.StackTrace);
        }

        // Update status (same logic as Leave — check empty first, then full)
        if (model.CurrentPlayers == 0)
        {
            model.Status = SessionStatus.Finished;
        }
        else if (model.Status == SessionStatus.Full)
        {
            model.Status = SessionStatus.Active;
        }

        // Save updated session
        await _sessionStore.SaveAsync(sessionKey, model, SessionTtlOptions, cancellationToken);

        // Publish domain event
        await _messageBus.PublishGameSessionPlayerLeftAsync(
            new GameSessionPlayerLeftEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SessionId = Guid.Parse(sessionId),
                AccountId = targetAccountId,
                Kicked = true,
                Reason = body.Reason
            });

        // Publish lifecycle event
        await _messageBus.PublishGameSessionUpdatedAsync(
            BuildUpdatedEvent(model, "currentPlayers", "status"));

        // Push client events to remaining session participants and the kicked player
        var remainingSessionIds = model.Players.Select(p => p.SessionId.ToString()).ToList();
        var allTargetSessionIds = new List<string>(remainingSessionIds) { kickedPlayerSessionId };

        await _clientEventPublisher.PublishToSessionsAsync(allTargetSessionIds, new PlayerKickedClientEvent
        {
            SessionId = Guid.Parse(sessionId),
            KickedPlayerId = targetAccountId,
            KickedPlayerName = kickedPlayerDisplayName,
            Reason = body.Reason
        }, cancellationToken);

        if (previousStatus != model.Status && remainingSessionIds.Count > 0)
        {
            await _clientEventPublisher.PublishToSessionsAsync(remainingSessionIds, new SessionStateChangedClientEvent
            {
                SessionId = Guid.Parse(sessionId),
                PreviousState = previousStatus,
                NewState = model.Status,
                Reason = "player_kicked"
            }, cancellationToken);
        }

        _logger.LogInformation("Player {TargetAccountId} kicked from session {SessionId}", targetAccountId, sessionId);
        return StatusCodes.OK;
    }

    /// <summary>
    /// Sends a chat message in a game session.
    /// </summary>
    public async Task<StatusCodes> SendChatMessageAsync(
        ChatMessageRequest body,
        CancellationToken cancellationToken = default)
    {
        // Identify the caller from the X-Bannou-Session-Id header (set by Connect)
        var callerSessionIdStr = ServiceRequestContext.SessionId;
        if (string.IsNullOrEmpty(callerSessionIdStr) || !Guid.TryParse(callerSessionIdStr, out var callerSessionId))
        {
            _logger.LogWarning("Chat message rejected: no valid session context");
            return StatusCodes.Forbidden;
        }

        var gameType = body.GameType;
        _logger.LogDebug("Chat message in game {GameType}: {MessageType}", gameType, body.MessageType);

        // Get the lobby for this game type (don't auto-create for chat)
        var lobbyId = await GetLobbySessionAsync(gameType);
        if (lobbyId == null)
        {
            _logger.LogWarning("No lobby exists for game type {GameType}", gameType);
            return StatusCodes.NotFound;
        }

        var model = await _sessionStore
            .GetAsync(SESSION_KEY_PREFIX + lobbyId.ToString(), cancellationToken);

        if (model == null)
        {
            _logger.LogWarning("Game lobby {LobbyId} not found for chat", lobbyId);
            return StatusCodes.NotFound;
        }

        // Find the sender by their WebSocket session ID (from the header, not client input)
        var senderPlayer = model.Players.FirstOrDefault(p => p.SessionId == callerSessionId);
        if (senderPlayer == null)
        {
            _logger.LogWarning("Session {CallerSessionId} is not a member of lobby {LobbyId}", callerSessionId, lobbyId);
            return StatusCodes.Forbidden;
        }

        // Build typed client event (SessionId = lobby ID for game context)
        var chatEvent = new SessionChatReceivedClientEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SessionId = lobbyId.Value,
            MessageId = Guid.NewGuid(),
            SenderId = senderPlayer.AccountId,
            SenderName = senderPlayer.DisplayName,
            Message = body.Message,
            MessageType = body.MessageType,
            IsWhisperToMe = false // Will be set per-recipient for whispers
        };

        // Get WebSocket session IDs directly from player records (each player.SessionId is the WebSocket session that joined)
        // IClientEventPublisher uses string routing keys for RabbitMQ topics
        var targetSessionIds = model.Players
            .Select(p => p.SessionId.ToString())
            .ToList();

        if (targetSessionIds.Count == 0)
        {
            _logger.LogWarning("No player sessions found for game {GameType}", gameType);
            return StatusCodes.OK; // Not an error - players may have left
        }

        // Handle whisper messages - only send to sender and target
        if (body.MessageType == ChatMessageType.Whisper && body.TargetPlayerId.HasValue)
        {
            // Find sender and target player sessions
            var targetPlayer = model.Players.FirstOrDefault(p => p.AccountId == body.TargetPlayerId);

            // Send to target with IsWhisperToMe = true
            if (targetPlayer != null)
            {
                var targetEvent = new SessionChatReceivedClientEvent
                {
                    EventId = chatEvent.EventId,
                    Timestamp = chatEvent.Timestamp,
                    EventName = chatEvent.EventName,
                    SessionId = chatEvent.SessionId,
                    MessageId = chatEvent.MessageId,
                    SenderId = chatEvent.SenderId,
                    SenderName = chatEvent.SenderName,
                    Message = chatEvent.Message,
                    MessageType = chatEvent.MessageType,
                    IsWhisperToMe = true
                };
                await _clientEventPublisher.PublishToSessionAsync(targetPlayer.SessionId.ToString(), targetEvent, cancellationToken);
            }

            // Send to sender with IsWhisperToMe = false
            if (senderPlayer != null)
            {
                await _clientEventPublisher.PublishToSessionAsync(senderPlayer.SessionId.ToString(), chatEvent, cancellationToken);
            }
        }
        else
        {
            // Public message - send to all players in the game session
            var sentCount = await _clientEventPublisher.PublishToSessionsAsync(targetSessionIds, chatEvent, cancellationToken);
            _logger.LogDebug("Chat message sent to {SentCount}/{TotalCount} players in game {GameType}",
                sentCount, targetSessionIds.Count, gameType);

            // Warn if we had recipients but couldn't deliver to any of them
            if (sentCount == 0 && targetSessionIds.Count > 0)
            {
                _logger.LogWarning(
                    "Chat message in game {GameType} had {RecipientCount} target sessions but 0 delivered - possible pubsub issue",
                    gameType, targetSessionIds.Count);
            }
        }

        return StatusCodes.OK;
    }

}
