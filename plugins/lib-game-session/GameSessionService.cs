using BeyondImmersion.Bannou.Core;
using BeyondImmersion.Bannou.GameSession.ClientEvents;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Connect;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Permission;
using BeyondImmersion.BannouService.Protocol;
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
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<GameSessionService> _logger;
    private readonly GameSessionServiceConfiguration _configuration;
    private readonly IPermissionClient _permissionClient;
    private readonly ISubscriptionClient _subscriptionClient;
    private readonly IClientEventPublisher _clientEventPublisher;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly IConnectClient _connectClient;
    private readonly ITelemetryProvider _telemetryProvider;

    internal const string SESSION_KEY_PREFIX = "session:";
    internal const string SESSION_LIST_KEY = "session-list";
    private const string LOBBY_KEY_PREFIX = "lobby:";
    private const string SUBSCRIBER_SESSIONS_PREFIX = "subscriber-sessions:";
    private const string SESSION_CREATED_TOPIC = "game-session.created";
    private const string SESSION_UPDATED_TOPIC = "game-session.updated";
    private const string SESSION_DELETED_TOPIC = "game-session.deleted";
    private const string PLAYER_JOINED_TOPIC = "game-session.player-joined";
    private const string PLAYER_LEFT_TOPIC = "game-session.player-left";

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
    /// <param name="stateStoreFactory">State store factory for state operations.</param>
    /// <param name="messageBus">Message bus for pub/sub operations.</param>
    /// <param name="logger">Logger for this service.</param>
    /// <param name="configuration">Service configuration.</param>
    /// <param name="eventConsumer">Event consumer for pub/sub fan-out.</param>
    /// <param name="clientEventPublisher">Client event publisher for pushing events to WebSocket clients.</param>
    /// <param name="permissionClient">Permission client for setting game-session:in_game state.</param>
    /// <param name="subscriptionClient">Subscription client for fetching account subscriptions.</param>
    /// <param name="connectClient">Connect client for querying connected sessions per account.</param>
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
        ITelemetryProvider telemetryProvider)
    {
        _stateStoreFactory = stateStoreFactory;
        _messageBus = messageBus;
        _logger = logger;
        _configuration = configuration;
        _clientEventPublisher = clientEventPublisher;
        _permissionClient = permissionClient;
        _subscriptionClient = subscriptionClient;
        _lockProvider = lockProvider;
        _connectClient = connectClient;
        _telemetryProvider = telemetryProvider;

        // Initialize supported game services from configuration
        // Central validation in PluginLoader ensures non-nullable strings are not empty
        var configuredServices = configuration.SupportedGameServices.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _supportedGameServices = new HashSet<string>(configuredServices, StringComparer.OrdinalIgnoreCase);

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
        var sessionIds = await _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.GameSession)
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

            // Apply game type filter if provided (non-default value)
            // GameType defaults to "generic" if not specified
            // So we just skip filtering if the request body doesn't have explicit filter values

            // Apply status filter - skip finished sessions by default
            if (session.Status == SessionStatus.Finished)
                continue;

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

        // Save to state store
        await _stateStoreFactory.GetStore<GameSessionModel>(StateStoreDefinitions.GameSession)
            .SaveAsync(SESSION_KEY_PREFIX + session.SessionId, session, SessionTtlOptions, cancellationToken);

        // Add to session list under distributed lock (read-modify-write)
        await using var listLock = await _lockProvider.LockAsync(
            "game-session", SESSION_LIST_KEY, Guid.NewGuid().ToString(), _configuration.LockTimeoutSeconds, cancellationToken);
        if (!listLock.Success)
        {
            _logger.LogWarning("Could not acquire session-list lock when creating session {SessionId}", session.SessionId);
            return (StatusCodes.Conflict, null);
        }

        var sessionListStore = _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.GameSession);
        var sessionIds = await sessionListStore.GetAsync(SESSION_LIST_KEY, cancellationToken) ?? new List<string>();

        sessionIds.Add(session.SessionId.ToString());

        await sessionListStore.SaveAsync(SESSION_LIST_KEY, sessionIds, cancellationToken: cancellationToken);

        // Publish event with full model data
        await _messageBus.TryPublishAsync(
            SESSION_CREATED_TOPIC,
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
            "game-session", sessionKey, Guid.NewGuid().ToString(), _configuration.LockTimeoutSeconds, cancellationToken);
        if (!sessionLock.Success)
        {
            _logger.LogWarning("Could not acquire session lock for lobby {LobbyId}", lobbyId);
            return (StatusCodes.Conflict, null);
        }

        var sessionStore = _stateStoreFactory.GetStore<GameSessionModel>(StateStoreDefinitions.GameSession);
        var model = await sessionStore.GetAsync(sessionKey, cancellationToken);

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
                ServiceId = "game-session",
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
        await sessionStore.SaveAsync(sessionKey, model, SessionTtlOptions, cancellationToken);

        // Publish domain event (SessionId in event = lobby ID for game session identification)
        await _messageBus.TryPublishAsync(
            PLAYER_JOINED_TOPIC,
            new GameSessionPlayerJoinedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SessionId = lobbyId.Value,
                AccountId = accountId
            });

        // Publish lifecycle event
        await _messageBus.TryPublishAsync(
            SESSION_UPDATED_TOPIC,
            BuildUpdatedEvent(model, "currentPlayers", "status"));

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
        // body.SessionId is the WebSocket session ID
        // body.AccountId is the player performing the action
        // body.GameType determines which lobby
        var clientSessionId = body.SessionId;
        var accountId = body.AccountId;
        var gameType = body.GameType;

        _logger.LogDebug("Performing game action {ActionType} in game {GameType} by {AccountId}",
            body.ActionType, gameType, accountId);

        // Get the lobby for this game type (don't auto-create for action)
        var lobbyId = await GetLobbySessionAsync(gameType);
        if (lobbyId == null)
        {
            _logger.LogWarning("No lobby exists for game type {GameType}", gameType);
            return (StatusCodes.NotFound, null);
        }

        var model = await _stateStoreFactory.GetStore<GameSessionModel>(StateStoreDefinitions.GameSession)
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

        // Validate action data is present for mutation actions
        var actionType = body.ActionType;
        if (body.ActionData == null && actionType != GameActionType.Move)
        {
            // Move can have empty data for "continue moving" semantics; other actions need data
            _logger.LogDebug("No action data provided for action type {ActionType} - proceeding with empty data", actionType);
        }

        // Create action response - success only after all validations pass
        var actionId = Guid.NewGuid();
        var actionTimestamp = DateTimeOffset.UtcNow;

        // Publish game action event so other systems can react
        // TryPublishAsync handles buffering, retry, and error logging internally
        await _messageBus.TryPublishAsync("game-session.action.performed", new GameSessionActionPerformedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = actionTimestamp,
            SessionId = lobbyId.Value,
            AccountId = accountId,
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
            "game-session", sessionKey, Guid.NewGuid().ToString(), _configuration.LockTimeoutSeconds, cancellationToken);
        if (!sessionLock.Success)
        {
            _logger.LogWarning("Could not acquire session lock for lobby {LobbyId}", lobbyId);
            return StatusCodes.Conflict;
        }

        var sessionStore = _stateStoreFactory.GetStore<GameSessionModel>(StateStoreDefinitions.GameSession);
        var model = await sessionStore.GetAsync(sessionKey, cancellationToken);

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
                ServiceId = "game-session"
            }, cancellationToken);
        }
        catch (ApiException ex)
        {
            // Permission service returned an error
            // Continue anyway - player wants to leave, don't trap them; state cleaned up on session expiry
            _logger.LogWarning(ex, "Permission service error clearing session state for {SessionId}: {StatusCode}",
                body.SessionId, ex.StatusCode);
            await _messageBus.TryPublishErrorAsync(
                "game-session",
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
                "game-session",
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
        await sessionStore.SaveAsync(sessionKey, model, SessionTtlOptions, cancellationToken);

        // Publish domain event
        await _messageBus.TryPublishAsync(
            PLAYER_LEFT_TOPIC,
            new GameSessionPlayerLeftEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SessionId = lobbyId.Value,
                AccountId = leavingPlayer.AccountId,
                Kicked = false
            });

        // Publish lifecycle event
        await _messageBus.TryPublishAsync(
            SESSION_UPDATED_TOPIC,
            BuildUpdatedEvent(model, "currentPlayers", "status"));

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
            "game-session", sessionKey, Guid.NewGuid().ToString(), _configuration.LockTimeoutSeconds, cancellationToken);
        if (!sessionLock.Success)
        {
            _logger.LogWarning("Could not acquire session lock for {GameSessionId}", gameSessionId);
            return (StatusCodes.Conflict, null);
        }

        var sessionStore = _stateStoreFactory.GetStore<GameSessionModel>(StateStoreDefinitions.GameSession);
        var model = await sessionStore.GetAsync(sessionKey, cancellationToken);

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
                ServiceId = "game-session",
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
        await sessionStore.SaveAsync(sessionKey, model, SessionTtlOptions, cancellationToken);

        // Publish domain event
        await _messageBus.TryPublishAsync(
            PLAYER_JOINED_TOPIC,
            new GameSessionPlayerJoinedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SessionId = body.GameSessionId,
                AccountId = accountId
            });

        // Publish lifecycle event
        await _messageBus.TryPublishAsync(
            SESSION_UPDATED_TOPIC,
            BuildUpdatedEvent(model, "currentPlayers", "status", "reservations"));

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
            "game-session", sessionKey, Guid.NewGuid().ToString(), _configuration.LockTimeoutSeconds, cancellationToken);
        if (!sessionLock.Success)
        {
            _logger.LogWarning("Could not acquire session lock for {GameSessionId}", gameSessionId);
            return StatusCodes.Conflict;
        }

        var sessionStore = _stateStoreFactory.GetStore<GameSessionModel>(StateStoreDefinitions.GameSession);
        var model = await sessionStore.GetAsync(sessionKey, cancellationToken);

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
                    ServiceId = "game-session"
                }, cancellationToken);
            }
            catch (ApiException ex)
            {
                // Permission service returned an error - continue anyway, state cleaned up on session expiry
                _logger.LogWarning(ex, "Permission service error clearing session state for {SessionId}: {StatusCode}",
                    body.WebSocketSessionId.Value, ex.StatusCode);
                await _messageBus.TryPublishErrorAsync(
                    "game-session",
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
                    "game-session",
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
        await sessionStore.SaveAsync(sessionKey, model, SessionTtlOptions, cancellationToken);

        // Publish domain event
        await _messageBus.TryPublishAsync(
            PLAYER_LEFT_TOPIC,
            new GameSessionPlayerLeftEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SessionId = body.GameSessionId,
                AccountId = leavingPlayer.AccountId,
                Kicked = false
            });

        // Publish lifecycle event
        await _messageBus.TryPublishAsync(
            SESSION_UPDATED_TOPIC,
            BuildUpdatedEvent(model, "currentPlayers", "status"));

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
        var sessionStore = _stateStoreFactory.GetStore<GameSessionModel>(StateStoreDefinitions.GameSession);
        var model = await sessionStore.GetAsync(SESSION_KEY_PREFIX + gameSessionId, cancellationToken);

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
            "game-session",
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
                    SourceService = "game-session",
                    TargetService = "game-session",
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
            "game-session", sessionKey, Guid.NewGuid().ToString(), _configuration.LockTimeoutSeconds, cancellationToken);
        if (!sessionLock.Success)
        {
            _logger.LogWarning("Could not acquire session lock for {SessionId}", sessionId);
            return StatusCodes.Conflict;
        }

        var sessionStore = _stateStoreFactory.GetStore<GameSessionModel>(StateStoreDefinitions.GameSession);
        var model = await sessionStore.GetAsync(sessionKey, cancellationToken);

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

        model.Players.Remove(playerToKick);
        model.CurrentPlayers = model.Players.Count;

        // Clear game-session:in_game state via Permission service for the kicked player
        try
        {
            await _permissionClient.ClearSessionStateAsync(new Permission.ClearSessionStateRequest
            {
                SessionId = playerToKick.SessionId,
                ServiceId = "game-session"
            }, cancellationToken);
        }
        catch (ApiException ex)
        {
            // Permission service returned an error - continue anyway, state cleaned up on session expiry
            _logger.LogWarning(ex, "Permission service error clearing session state for kicked player {SessionId}: {StatusCode}",
                playerToKick.SessionId, ex.StatusCode);
            await _messageBus.TryPublishErrorAsync(
                "game-session",
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
                "game-session",
                "ClearSessionState",
                ex.GetType().Name,
                ex.Message,
                dependency: "permission",
                endpoint: "post:/permission/clear-session-state",
                details: new { SessionId = playerToKick.SessionId },
                stack: ex.StackTrace);
        }

        // Update status
        if (model.Status == SessionStatus.Full)
        {
            model.Status = SessionStatus.Active;
        }

        // Save updated session
        await sessionStore.SaveAsync(sessionKey, model, SessionTtlOptions, cancellationToken);

        // Publish domain event
        await _messageBus.TryPublishAsync(
            PLAYER_LEFT_TOPIC,
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
        await _messageBus.TryPublishAsync(
            SESSION_UPDATED_TOPIC,
            BuildUpdatedEvent(model, "currentPlayers", "status"));

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
        // body.SessionId is the WebSocket session ID
        // body.AccountId is the sender's account
        // body.GameType determines which lobby
        var clientSessionId = body.SessionId;
        var senderId = body.AccountId;
        var gameType = body.GameType;

        _logger.LogDebug("Chat message in game {GameType}: {MessageType}", gameType, body.MessageType);

        // Get the lobby for this game type (don't auto-create for chat)
        var lobbyId = await GetLobbySessionAsync(gameType);
        if (lobbyId == null)
        {
            _logger.LogWarning("No lobby exists for game type {GameType}", gameType);
            return StatusCodes.NotFound;
        }

        var model = await _stateStoreFactory.GetStore<GameSessionModel>(StateStoreDefinitions.GameSession)
            .GetAsync(SESSION_KEY_PREFIX + lobbyId.ToString(), cancellationToken);

        if (model == null)
        {
            _logger.LogWarning("Game lobby {LobbyId} not found for chat", lobbyId);
            return StatusCodes.NotFound;
        }

        // Find the sender player
        var senderPlayer = model.Players.FirstOrDefault(p => p.AccountId == senderId);

        // Build typed client event (SessionId = lobby ID for game context)
        var chatEvent = new SessionChatReceivedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SessionId = lobbyId.Value,
            MessageId = Guid.NewGuid(),
            SenderId = senderId,
            SenderName = senderPlayer?.DisplayName,
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
                var targetEvent = new SessionChatReceivedEvent
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

            if (ourServices.Count > 0)
            {
                _logger.LogDebug("Account {AccountId} has {Count} subscriptions matching our services: {Services}",
                    accountId, ourServices.Count, string.Join(", ", ourServices));

                // Store subscriber session in lib-state (distributed tracking) if not already stored
                if (!genericPublished)
                {
                    await StoreSubscriberSessionAsync(accountId, sessionId);
                }

                foreach (var stubName in ourServices)
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
    internal async Task HandleSubscriptionUpdatedInternalAsync(Guid accountId, string stubName, SubscriptionUpdatedEventAction action, bool isActive)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.game-session", "GameSessionService.HandleSubscriptionUpdatedInternal");

        _logger.LogInformation("Subscription update for account {AccountId}: stubName={StubName}, action={Action}, isActive={IsActive}",
            accountId, stubName, action, isActive);

        // Update the cache
        if (isActive && (action == SubscriptionUpdatedEventAction.Created || action == SubscriptionUpdatedEventAction.Renewed || action == SubscriptionUpdatedEventAction.Updated))
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
        else if (!isActive || action == SubscriptionUpdatedEventAction.Cancelled || action == SubscriptionUpdatedEventAction.Expired)
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

        foreach (var sessionId in connectedSessionsForAccount)
        {
            if (isActive)
            {
                // Store in subscriber-sessions so join validation works
                await StoreSubscriberSessionAsync(accountId, sessionId);
                await PublishJoinShortcutAsync(sessionId, accountId, stubName);
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
            var store = _stateStoreFactory.GetStore<SubscriberSessionsModel>(StateStoreDefinitions.GameSession);
            var key = SUBSCRIBER_SESSIONS_PREFIX + accountId.ToString();

            for (var attempt = 0; attempt < _configuration.SubscriberSessionRetryMaxAttempts; attempt++)
            {
                var (existing, etag) = await store.GetWithETagAsync(key);
                var model = existing ?? new SubscriberSessionsModel { AccountId = accountId };
                model.SessionIds.Add(sessionId);
                model.UpdatedAt = DateTimeOffset.UtcNow;

                // Empty string ETag signals a new record to TrySaveAsync when
                // GetWithETagAsync returns null etag for non-existent keys
                var result = await store.TrySaveAsync(key, model, etag ?? string.Empty);
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
            var store = _stateStoreFactory.GetStore<SubscriberSessionsModel>(StateStoreDefinitions.GameSession);
            var key = SUBSCRIBER_SESSIONS_PREFIX + accountId.ToString();

            for (var attempt = 0; attempt < _configuration.SubscriberSessionRetryMaxAttempts; attempt++)
            {
                var (existing, etag) = await store.GetWithETagAsync(key);
                if (existing == null)
                {
                    _logger.LogDebug("No subscriber sessions found for account {AccountId}, nothing to remove", accountId);
                    return;
                }

                existing.SessionIds.Remove(sessionId);
                existing.UpdatedAt = DateTimeOffset.UtcNow;

                if (existing.SessionIds.Count == 0)
                {
                    await store.DeleteAsync(key);
                    _logger.LogDebug("Removed last subscriber session {SessionId} for account {AccountId}", sessionId, accountId);
                    return;
                }

                // Empty string ETag signals a new record to TrySaveAsync when
                // GetWithETagAsync returns null etag for non-existent keys
                var result = await store.TrySaveAsync(key, existing, etag ?? string.Empty);
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
            var store = _stateStoreFactory.GetStore<SubscriberSessionsModel>(StateStoreDefinitions.GameSession);
            var key = SUBSCRIBER_SESSIONS_PREFIX + accountId.ToString();

            var existing = await store.GetAsync(key);
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
            var store = _stateStoreFactory.GetStore<SubscriberSessionsModel>(StateStoreDefinitions.GameSession);
            var key = SUBSCRIBER_SESSIONS_PREFIX + accountId.ToString();

            var existing = await store.GetAsync(key);
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
                "game-session",
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
                        SourceService = "game-session",
                        TargetService = "game-session",
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
                RevokeByService = "game-session",
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
            var sessionStore = _stateStoreFactory.GetStore<GameSessionModel>(StateStoreDefinitions.GameSession);

            // Check for existing lobby (fast path without lock)
            var existingLobby = await sessionStore.GetAsync(lobbyKey);
            if (existingLobby != null && existingLobby.Status != SessionStatus.Finished)
            {
                _logger.LogDebug("Found existing lobby {LobbyId} for {StubName}", existingLobby.SessionId, stubName);
                return existingLobby.SessionId;
            }

            // Lock on lobby key to prevent duplicate lobby creation across instances
            await using var lobbyLock = await _lockProvider.LockAsync(
                "game-session", lobbyKey, Guid.NewGuid().ToString(), _configuration.LockTimeoutSeconds);
            if (!lobbyLock.Success)
            {
                _logger.LogWarning("Could not acquire lobby lock for {StubName}, retrying read", stubName);
                // Another instance may have created the lobby while we waited
                existingLobby = await sessionStore.GetAsync(lobbyKey);
                return existingLobby?.SessionId;
            }

            // Re-check under lock (another instance may have created it)
            existingLobby = await sessionStore.GetAsync(lobbyKey);
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
            await sessionStore.SaveAsync(SESSION_KEY_PREFIX + lobbyId, lobby, SessionTtlOptions);
            await sessionStore.SaveAsync(lobbyKey, lobby, SessionTtlOptions);

            // Add to session list under distributed lock (read-modify-write)
            await using var listLock = await _lockProvider.LockAsync(
                "game-session", SESSION_LIST_KEY, Guid.NewGuid().ToString(), _configuration.LockTimeoutSeconds);
            if (listLock.Success)
            {
                var sessionListStore = _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.GameSession);
                var sessionIds = await sessionListStore.GetAsync(SESSION_LIST_KEY) ?? new List<string>();
                sessionIds.Add(lobbyId.ToString());
                await sessionListStore.SaveAsync(SESSION_LIST_KEY, sessionIds);
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
            var sessionStore = _stateStoreFactory.GetStore<GameSessionModel>(StateStoreDefinitions.GameSession);
            var existingLobby = await sessionStore.GetAsync(lobbyKey);

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

    private async Task<GameSessionResponse?> LoadSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.game-session", "GameSessionService.LoadSession");

        var model = await _stateStoreFactory.GetStore<GameSessionModel>(StateStoreDefinitions.GameSession)
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
