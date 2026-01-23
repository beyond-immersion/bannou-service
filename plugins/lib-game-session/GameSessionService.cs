using BeyondImmersion.Bannou.Core;
using BeyondImmersion.Bannou.GameSession.ClientEvents;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Permission;
using BeyondImmersion.BannouService.Protocol;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Subscription;
using BeyondImmersion.BannouService.Voice;
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
/// Manages game sessions for Arcadia and other multiplayer games.
/// Handles session shortcuts for subscribed accounts via mesh pubsub events.
/// </summary>
[BannouService("game-session", typeof(IGameSessionService), lifetime: ServiceLifetime.Scoped)]
public partial class GameSessionService : IGameSessionService
{
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IMessageBus _messageBus;
    private readonly ILogger<GameSessionService> _logger;
    private readonly GameSessionServiceConfiguration _configuration;
    private readonly IVoiceClient _voiceClient;
    private readonly IPermissionClient _permissionClient;
    private readonly ISubscriptionClient _subscriptionClient;
    private readonly IClientEventPublisher _clientEventPublisher;
    private readonly IDistributedLockProvider _lockProvider;

    private const string SESSION_KEY_PREFIX = "session:";
    private const string SESSION_LIST_KEY = "session-list";
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
    /// Called when a subscription is cancelled.
    /// Thread-safe for concurrent access.
    /// </summary>
    /// <param name="accountId">The account ID to update.</param>
    /// <param name="stubName">The service stub name to remove.</param>
    public static void RemoveAccountSubscription(Guid accountId, string stubName)
    {
        if (_accountSubscriptions.TryGetValue(accountId, out var stubs))
        {
            lock (stubs)
            {
                stubs.Remove(stubName);
            }
        }
    }

    /// <summary>
    /// Server salt for GUID generation. Comes from configuration, with fallback to random generation for development.
    /// Instance-based to support configuration injection (IMPLEMENTATION TENETS).
    /// </summary>
    private readonly string _serverSalt;

    /// <summary>
    /// State options for session saves. Applies TTL when DefaultSessionTimeoutSeconds is configured (greater than 0).
    /// When null (default = 0), sessions have no expiry.
    /// </summary>
    private StateOptions? SessionTtlOptions => _configuration.DefaultSessionTimeoutSeconds > 0
        ? new StateOptions { Ttl = _configuration.DefaultSessionTimeoutSeconds }
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
    /// <param name="voiceClient">Voice client for voice room coordination.</param>
    /// <param name="permissionClient">Permission client for setting game-session:in_game state.</param>
    /// <param name="subscriptionClient">Subscription client for fetching account subscriptions.</param>
    public GameSessionService(
        IStateStoreFactory stateStoreFactory,
        IMessageBus messageBus,
        ILogger<GameSessionService> logger,
        GameSessionServiceConfiguration configuration,
        IEventConsumer eventConsumer,
        IClientEventPublisher clientEventPublisher,
        IVoiceClient voiceClient,
        IPermissionClient permissionClient,
        ISubscriptionClient subscriptionClient,
        IDistributedLockProvider lockProvider)
    {
        _stateStoreFactory = stateStoreFactory;
        _messageBus = messageBus;
        _logger = logger;
        _configuration = configuration;
        _clientEventPublisher = clientEventPublisher;
        _voiceClient = voiceClient;
        _permissionClient = permissionClient;
        _subscriptionClient = subscriptionClient;
        _lockProvider = lockProvider;

        // Initialize supported game services from configuration
        var configuredServices = configuration.SupportedGameServices?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _supportedGameServices = new HashSet<string>(configuredServices ?? new[] { "arcadia", "generic" }, StringComparer.OrdinalIgnoreCase);

        // Server salt from configuration - REQUIRED (fail-fast for production safety)
        if (string.IsNullOrEmpty(configuration.ServerSalt))
        {
            throw new InvalidOperationException(
                "GAME_SESSION_SERVER_SALT is required. All service instances must share the same salt for session shortcuts to work correctly.");
        }
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
        try
        {
            _logger.LogInformation("Listing game sessions - GameType: {GameType}, Status: {Status}",
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
                // Since enums default to first value (Arcadia=0), we can't distinguish "not set" from "arcadia"
                // So we just skip filtering if the request body doesn't have explicit filter values

                // Apply status filter - skip finished sessions by default
                if (session.Status == GameSessionResponseStatus.Finished)
                    continue;

                sessions.Add(session);
            }

            var response = new GameSessionListResponse
            {
                Sessions = sessions,
                TotalCount = sessions.Count
            };

            _logger.LogInformation("Returning {Count} game sessions", sessions.Count);
            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list game sessions");
            await _messageBus.TryPublishErrorAsync(
                "game-session",
                "ListGameSessions",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/game-session/list",
                details: null,
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Creates a new game session.
    /// </summary>
    public async Task<(StatusCodes, GameSessionResponse?)> CreateGameSessionAsync(
        CreateGameSessionRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sessionId = Guid.NewGuid();

            _logger.LogInformation("Creating game session {SessionId} - GameType: {GameType}, MaxPlayers: {MaxPlayers}",
                sessionId, body.GameType, body.MaxPlayers);

            // Determine session type (default to lobby if not specified)
            // Note: SessionType is not nullable in generated code, so we check for default enum value
            var sessionType = body.SessionType;
            var reservationTtl = body.ReservationTtlSeconds > 0 ? body.ReservationTtlSeconds : _configuration.DefaultReservationTtlSeconds;

            // Enforce max players cap from configuration
            var maxPlayers = body.MaxPlayers > 0
                ? Math.Min(body.MaxPlayers, _configuration.MaxPlayersPerSession)
                : _configuration.MaxPlayersPerSession;

            // Create the session model
            var session = new GameSessionModel
            {
                SessionId = sessionId.ToString(),
                GameType = MapRequestGameTypeToResponse(body.GameType),
                SessionName = body.SessionName,
                MaxPlayers = maxPlayers,
                IsPrivate = body.IsPrivate,
                Owner = body.OwnerId,
                Status = GameSessionResponseStatus.Waiting,
                CurrentPlayers = 0,
                Players = new List<GamePlayer>(),
                CreatedAt = DateTimeOffset.UtcNow,
                GameSettings = body.GameSettings,
                VoiceEnabled = _voiceClient != null, // Voice enabled if voice service is available
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

            // Create voice room if voice service is available
            if (_voiceClient != null)
            {
                try
                {
                    _logger.LogDebug("Creating voice room for session {SessionId}", sessionId);
                    var voiceResponse = await _voiceClient.CreateVoiceRoomAsync(new CreateVoiceRoomRequest
                    {
                        SessionId = sessionId,
                        PreferredTier = VoiceTier.P2p,
                        Codec = VoiceCodec.Opus,
                        MaxParticipants = body.MaxPlayers
                    }, cancellationToken);

                    session.VoiceRoomId = voiceResponse.RoomId;
                    _logger.LogInformation("Voice room {VoiceRoomId} created for session {SessionId}",
                        voiceResponse.RoomId, sessionId);
                }
                catch (Exception ex)
                {
                    // Voice room creation failure is non-fatal - session can still work without voice
                    _logger.LogWarning(ex, "Failed to create voice room for session {SessionId}, continuing without voice", sessionId);
                    session.VoiceEnabled = false;
                }
            }

            // Save to state store
            await _stateStoreFactory.GetStore<GameSessionModel>(StateStoreDefinitions.GameSession)
                .SaveAsync(SESSION_KEY_PREFIX + session.SessionId, session, SessionTtlOptions, cancellationToken);

            // Add to session list
            var sessionListStore = _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.GameSession);
            var sessionIds = await sessionListStore.GetAsync(SESSION_LIST_KEY, cancellationToken) ?? new List<string>();

            sessionIds.Add(session.SessionId);

            await sessionListStore.SaveAsync(SESSION_LIST_KEY, sessionIds, cancellationToken: cancellationToken);

            // Publish event with full model data
            await _messageBus.TryPublishAsync(
                SESSION_CREATED_TOPIC,
                new GameSessionCreatedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    SessionId = Guid.Parse(session.SessionId),
                    GameType = session.GameType.ToString(),
                    SessionName = session.SessionName,
                    Status = session.Status.ToString(),
                    MaxPlayers = session.MaxPlayers,
                    CurrentPlayers = session.CurrentPlayers,
                    IsPrivate = session.IsPrivate,
                    Owner = session.Owner,
                    CreatedAt = session.CreatedAt
                });

            var response = MapModelToResponse(session);

            _logger.LogInformation("Game session {SessionId} created successfully", session.SessionId);
            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create game session");
            await _messageBus.TryPublishErrorAsync(
                "game-session",
                "CreateGameSession",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/game-session/create",
                details: null,
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Gets a game session by ID.
    /// </summary>
    public async Task<(StatusCodes, GameSessionResponse?)> GetGameSessionAsync(
        GetGameSessionRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Getting game session {SessionId}", body.SessionId);

            var session = await LoadSessionAsync(body.SessionId.ToString(), cancellationToken);

            if (session == null)
            {
                _logger.LogWarning("Game session {SessionId} not found", body.SessionId);
                return (StatusCodes.NotFound, null);
            }

            return (StatusCodes.OK, session);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get game session {SessionId}", body.SessionId);
            await _messageBus.TryPublishErrorAsync(
                "game-session",
                "GetGameSession",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/game-session/get",
                details: new { SessionId = body.SessionId },
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Joins a game session.
    /// </summary>
    public async Task<(StatusCodes, JoinGameSessionResponse?)> JoinGameSessionAsync(
        JoinGameSessionRequest body,
        CancellationToken cancellationToken = default)
    {
        try
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
            if (lobbyId == Guid.Empty)
            {
                _logger.LogWarning("No lobby exists for game type {GameType}", gameType);
                return (StatusCodes.NotFound, null);
            }

            // Acquire lock on session (multiple players may join concurrently)
            var sessionKey = SESSION_KEY_PREFIX + lobbyId.ToString();
            await using var sessionLock = await _lockProvider.LockAsync(
                "game-session", sessionKey, Guid.NewGuid().ToString(), 60, cancellationToken);
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
            if (model.Status == GameSessionResponseStatus.Finished)
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
                Role = GamePlayerRole.Player,
                JoinedAt = DateTimeOffset.UtcNow,
                CharacterData = body.CharacterData
            };

            model.Players.Add(player);
            model.CurrentPlayers = model.Players.Count;

            // Update status if full
            if (model.CurrentPlayers >= model.MaxPlayers)
            {
                model.Status = GameSessionResponseStatus.Full;
            }
            else if (model.Status == GameSessionResponseStatus.Waiting && model.CurrentPlayers > 0)
            {
                model.Status = GameSessionResponseStatus.Active;
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update session state for {SessionId}", body.SessionId);
                // Remove the player we just added since they won't have permissions
                model.Players.Remove(player);
                return (StatusCodes.InternalServerError, null);
            }

            // Save updated session
            await sessionStore.SaveAsync(sessionKey, model, SessionTtlOptions, cancellationToken);

            // Publish event (SessionId in event = lobby ID for game session identification)
            await _messageBus.TryPublishAsync(
                PLAYER_JOINED_TOPIC,
                new GameSessionPlayerJoinedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    SessionId = lobbyId,
                    AccountId = accountId
                });

            // Build response (SessionId = lobby ID so client knows which game they joined)
            var response = new JoinGameSessionResponse
            {
                SessionId = lobbyId,
                PlayerRole = JoinGameSessionResponsePlayerRole.Player,
                GameData = model.GameSettings ?? new object(),
                NewPermissions = new List<string>
                {
                    $"game-session:{lobbyId}:action",
                    $"game-session:{lobbyId}:chat"
                }
            };

            // Voice room join handled by lib-voice integration when voice service is available

            _logger.LogInformation("Player {AccountId} joined game {GameType} (lobby {LobbyId}) from session {ClientSessionId}",
                accountId, gameType, lobbyId, clientSessionId);
            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to join game {GameType}", body.GameType);
            await _messageBus.TryPublishErrorAsync(
                "game-session",
                "JoinGameSession",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/game-session/join",
                details: new { SessionId = body.SessionId },
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Performs a game action within a session.
    /// </summary>
    public async Task<(StatusCodes, GameActionResponse?)> PerformGameActionAsync(
        GameActionRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // body.SessionId is the WebSocket session ID
            // body.AccountId is the player performing the action
            // body.GameType determines which lobby
            var clientSessionId = body.SessionId;
            var accountId = body.AccountId;
            var gameType = body.GameType;

            _logger.LogInformation("Performing game action {ActionType} in game {GameType} by {AccountId}",
                body.ActionType, gameType, accountId);

            // Get the lobby for this game type (don't auto-create for action)
            var lobbyId = await GetLobbySessionAsync(gameType);
            if (lobbyId == Guid.Empty)
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

            if (model.Status == GameSessionResponseStatus.Finished)
            {
                _logger.LogWarning("Cannot perform action on finished lobby {LobbyId}", lobbyId);
                return (StatusCodes.BadRequest, null);
            }

            // Validate action data is present for mutation actions
            var actionType = body.ActionType;
            if (body.ActionData == null && actionType != GameActionRequestActionType.Move)
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
                SessionId = body.SessionId,
                ActionId = actionId,
                ActionType = body.ActionType.ToString(),
                TargetId = body.TargetId
            });

            var response = new GameActionResponse
            {
                ActionId = actionId,
                Result = new Dictionary<string, object?>
                {
                    ["actionType"] = body.ActionType.ToString(),
                    ["timestamp"] = actionTimestamp.ToString("O")
                },
                NewGameState = body.ActionData ?? new Dictionary<string, object?>()
            };

            _logger.LogInformation("Game action {ActionId} performed successfully in lobby {LobbyId}",
                actionId, lobbyId);
            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform game action in game {GameType}", body.GameType);
            await _messageBus.TryPublishErrorAsync(
                "game-session",
                "PerformGameAction",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/game-session/action",
                details: new { SessionId = body.SessionId, ActionType = body.ActionType.ToString() },
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Leaves a game session.
    /// </summary>
    public async Task<StatusCodes> LeaveGameSessionAsync(
        LeaveGameSessionRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // body.SessionId is the WebSocket session ID
            // body.GameType determines which lobby to leave
            var clientSessionId = body.SessionId;
            var gameType = body.GameType;

            _logger.LogInformation("Player leaving game {GameType} from session {SessionId}", gameType, clientSessionId);

            // Get the lobby for this game type (don't auto-create for leave)
            var lobbyId = await GetLobbySessionAsync(gameType);
            if (lobbyId == Guid.Empty)
            {
                _logger.LogWarning("No lobby exists for game type {GameType}", gameType);
                return StatusCodes.NotFound;
            }

            // Acquire lock on session (multiple players may leave concurrently)
            var sessionKey = SESSION_KEY_PREFIX + lobbyId.ToString();
            await using var sessionLock = await _lockProvider.LockAsync(
                "game-session", sessionKey, Guid.NewGuid().ToString(), 60, cancellationToken);
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
            catch (Exception ex)
            {
                // Log error and publish error event - this is an internal failure, not user error
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

            // Leave voice room if voice is enabled and player has a voice session (best effort - has TTL)
            if (model.VoiceEnabled && model.VoiceRoomId.HasValue && _voiceClient != null
                && leavingPlayer.VoiceSessionId.HasValue)
            {
                try
                {
                    _logger.LogDebug("Player {AccountId} leaving voice room {VoiceRoomId} with voice session {VoiceSessionId}",
                        leavingPlayer.AccountId, model.VoiceRoomId, leavingPlayer.VoiceSessionId);

                    await _voiceClient.LeaveVoiceRoomAsync(new LeaveVoiceRoomRequest
                    {
                        RoomId = model.VoiceRoomId.Value,
                        SessionId = leavingPlayer.VoiceSessionId.Value
                    }, cancellationToken);

                    _logger.LogInformation("Player {AccountId} left voice room {VoiceRoomId}",
                        leavingPlayer.AccountId, model.VoiceRoomId);
                }
                catch (Exception ex)
                {
                    // Voice leave failure is non-fatal
                    _logger.LogWarning(ex, "Failed to leave voice room for player {AccountId}",
                        leavingPlayer.AccountId);
                }
            }

            // Update status
            if (model.CurrentPlayers == 0)
            {
                model.Status = GameSessionResponseStatus.Finished;

                // Delete voice room when session ends
                if (model.VoiceEnabled && model.VoiceRoomId.HasValue && _voiceClient != null)
                {
                    try
                    {
                        _logger.LogDebug("Deleting voice room {VoiceRoomId} as lobby {LobbyId} has ended",
                            model.VoiceRoomId, lobbyId);

                        await _voiceClient.DeleteVoiceRoomAsync(new DeleteVoiceRoomRequest
                        {
                            RoomId = model.VoiceRoomId.Value,
                            Reason = "session_ended"
                        }, cancellationToken);

                        _logger.LogInformation("Voice room {VoiceRoomId} deleted for ended lobby {LobbyId}",
                            model.VoiceRoomId, lobbyId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete voice room {VoiceRoomId} for lobby {LobbyId}",
                            model.VoiceRoomId, lobbyId);
                    }
                }
            }
            else if (model.Status == GameSessionResponseStatus.Full)
            {
                model.Status = GameSessionResponseStatus.Active;
            }

            // Save updated session
            await sessionStore.SaveAsync(sessionKey, model, SessionTtlOptions, cancellationToken);

            // Publish event
            await _messageBus.TryPublishAsync(
                PLAYER_LEFT_TOPIC,
                new GameSessionPlayerLeftEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    SessionId = lobbyId,
                    AccountId = leavingPlayer.AccountId,
                    Kicked = false
                });

            _logger.LogInformation("Player {AccountId} left game {GameType} (lobby {LobbyId})",
                accountId, gameType, lobbyId);

            return StatusCodes.OK;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to leave game {GameType}", body.GameType);
            await _messageBus.TryPublishErrorAsync(
                "game-session",
                "LeaveGameSession",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/game-session/leave",
                details: new { SessionId = body.SessionId },
                stack: ex.StackTrace);
            return StatusCodes.InternalServerError;
        }
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
        try
        {
            var clientSessionId = body.WebSocketSessionId;
            var gameSessionId = body.GameSessionId.ToString();
            var accountId = body.AccountId;

            _logger.LogInformation("Player {AccountId} joining game session {GameSessionId} from WebSocket session {SessionId}",
                accountId, gameSessionId, clientSessionId);

            // Acquire lock on session (multiple players may join concurrently)
            var sessionKey = SESSION_KEY_PREFIX + gameSessionId;
            await using var sessionLock = await _lockProvider.LockAsync(
                "game-session", sessionKey, Guid.NewGuid().ToString(), 60, cancellationToken);
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
            if (model.Status == GameSessionResponseStatus.Finished)
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
                Role = GamePlayerRole.Player,
                JoinedAt = DateTimeOffset.UtcNow,
                CharacterData = body.CharacterData
            };

            model.Players.Add(player);
            model.CurrentPlayers = model.Players.Count;

            // Update status
            if (model.CurrentPlayers >= model.MaxPlayers)
            {
                model.Status = GameSessionResponseStatus.Full;
            }
            else if (model.Status == GameSessionResponseStatus.Waiting && model.CurrentPlayers > 0)
            {
                model.Status = GameSessionResponseStatus.Active;
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update session state for {SessionId}", body.WebSocketSessionId);
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

            // Save updated session
            await sessionStore.SaveAsync(sessionKey, model, SessionTtlOptions, cancellationToken);

            // Publish event
            await _messageBus.TryPublishAsync(
                PLAYER_JOINED_TOPIC,
                new GameSessionPlayerJoinedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    SessionId = body.GameSessionId,
                    AccountId = accountId
                });

            // Build response
            var response = new JoinGameSessionResponse
            {
                SessionId = body.GameSessionId,
                PlayerRole = JoinGameSessionResponsePlayerRole.Player,
                GameData = model.GameSettings ?? new object(),
                NewPermissions = new List<string>
                {
                    $"game-session:{gameSessionId}:action",
                    $"game-session:{gameSessionId}:chat"
                }
            };

            _logger.LogInformation("Player {AccountId} joined game session {GameSessionId} from WebSocket session {ClientSessionId}",
                accountId, gameSessionId, clientSessionId);
            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to join game session {GameSessionId}", body.GameSessionId);
            await _messageBus.TryPublishErrorAsync(
                "game-session",
                "JoinGameSessionById",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/game-session/join-session",
                details: new { SessionId = body.GameSessionId },
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Leaves a specific game session by ID.
    /// Alternative to LeaveGameSessionAsync that takes session ID directly.
    /// </summary>
    public async Task<StatusCodes> LeaveGameSessionByIdAsync(
        LeaveGameSessionByIdRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var clientSessionId = body.WebSocketSessionId;
            var gameSessionId = body.GameSessionId.ToString();
            var accountId = body.AccountId;

            _logger.LogInformation("Player {AccountId} leaving game session {GameSessionId} from WebSocket session {SessionId}",
                accountId, gameSessionId, clientSessionId);

            // Acquire lock on session (multiple players may leave concurrently)
            var sessionKey = SESSION_KEY_PREFIX + gameSessionId;
            await using var sessionLock = await _lockProvider.LockAsync(
                "game-session", sessionKey, Guid.NewGuid().ToString(), 60, cancellationToken);
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
            try
            {
                await _permissionClient.ClearSessionStateAsync(new Permission.ClearSessionStateRequest
                {
                    SessionId = body.WebSocketSessionId,
                    ServiceId = "game-session"
                }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clear session state for {SessionId} during leave", body.WebSocketSessionId);
                await _messageBus.TryPublishErrorAsync(
                    "game-session",
                    "ClearSessionState",
                    ex.GetType().Name,
                    ex.Message,
                    dependency: "permission",
                    endpoint: "post:/permission/clear-session-state",
                    details: new { SessionId = body.WebSocketSessionId },
                    stack: ex.StackTrace);
            }

            model.Players.Remove(leavingPlayer);
            model.CurrentPlayers = model.Players.Count;

            // Leave voice room if applicable
            if (model.VoiceEnabled && model.VoiceRoomId.HasValue && _voiceClient != null
                && leavingPlayer.VoiceSessionId.HasValue)
            {
                try
                {
                    await _voiceClient.LeaveVoiceRoomAsync(new LeaveVoiceRoomRequest
                    {
                        RoomId = model.VoiceRoomId.Value,
                        SessionId = leavingPlayer.VoiceSessionId.Value
                    }, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to leave voice room for player {AccountId}", leavingPlayer.AccountId);
                }
            }

            // Update status
            if (model.CurrentPlayers == 0)
            {
                model.Status = GameSessionResponseStatus.Finished;

                // Delete voice room when session ends
                if (model.VoiceEnabled && model.VoiceRoomId.HasValue && _voiceClient != null)
                {
                    try
                    {
                        await _voiceClient.DeleteVoiceRoomAsync(new DeleteVoiceRoomRequest
                        {
                            RoomId = model.VoiceRoomId.Value,
                            Reason = "session_ended"
                        }, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete voice room {VoiceRoomId} for session {SessionId}",
                            model.VoiceRoomId, gameSessionId);
                    }
                }
            }
            else if (model.Status == GameSessionResponseStatus.Full)
            {
                model.Status = GameSessionResponseStatus.Active;
            }

            // Save updated session
            await sessionStore.SaveAsync(sessionKey, model, SessionTtlOptions, cancellationToken);

            // Publish event
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

            _logger.LogInformation("Player {AccountId} left game session {GameSessionId}", accountId, gameSessionId);
            return StatusCodes.OK;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to leave game session {GameSessionId}", body.GameSessionId);
            await _messageBus.TryPublishErrorAsync(
                "game-session",
                "LeaveGameSessionById",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/game-session/leave-session",
                details: new { SessionId = body.GameSessionId },
                stack: ex.StackTrace);
            return StatusCodes.InternalServerError;
        }
    }

    /// <summary>
    /// Publishes a join shortcut for a matchmade session to a specific player.
    /// Called by the matchmaking service after creating a session with reservations.
    /// </summary>
    public async Task<(StatusCodes, PublishJoinShortcutResponse?)> PublishJoinShortcutAsync(
        PublishJoinShortcutRequest body,
        CancellationToken cancellationToken = default)
    {
        try
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
                return (StatusCodes.NotFound, new PublishJoinShortcutResponse { Success = false });
            }

            // Verify the reservation token is valid
            var reservation = model.Reservations.FirstOrDefault(r => r.AccountId == accountId && r.Token == reservationToken);
            if (reservation == null)
            {
                _logger.LogWarning("Invalid reservation token for player {AccountId} in session {GameSessionId}",
                    accountId, gameSessionId);
                return (StatusCodes.BadRequest, new PublishJoinShortcutResponse { Success = false });
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
                Success = true,
                ShortcutRouteGuid = routeGuid
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish join shortcut for game session {GameSessionId}", body.GameSessionId);
            await _messageBus.TryPublishErrorAsync(
                "game-session",
                "PublishJoinShortcut",
                "unexpected_exception",
                ex.Message,
                dependency: "client-events",
                endpoint: "post:/game-session/publish-join-shortcut",
                details: new { SessionId = body.GameSessionId },
                stack: ex.StackTrace);
            return (StatusCodes.InternalServerError, new PublishJoinShortcutResponse { Success = false });
        }
    }

    /// <summary>
    /// Kicks a player from a game session.
    /// </summary>
    public async Task<StatusCodes> KickPlayerAsync(
        KickPlayerRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sessionId = body.SessionId.ToString();
            var targetAccountId = body.TargetAccountId;

            _logger.LogInformation("Kicking player {TargetAccountId} from session {SessionId}. Reason: {Reason}",
                targetAccountId, sessionId, body.Reason);

            // Acquire lock on session (concurrent modification protection)
            var sessionKey = SESSION_KEY_PREFIX + sessionId;
            await using var sessionLock = await _lockProvider.LockAsync(
                "game-session", sessionKey, Guid.NewGuid().ToString(), 60, cancellationToken);
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

            // Update status
            if (model.Status == GameSessionResponseStatus.Full)
            {
                model.Status = GameSessionResponseStatus.Active;
            }

            // Save updated session
            await sessionStore.SaveAsync(sessionKey, model, SessionTtlOptions, cancellationToken);

            // Publish event
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

            _logger.LogInformation("Player {TargetAccountId} kicked from session {SessionId}", targetAccountId, sessionId);
            return StatusCodes.OK;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to kick player from session {SessionId}", body.SessionId);
            await _messageBus.TryPublishErrorAsync(
                "game-session",
                "KickPlayer",
                "unexpected_exception",
                ex.Message,
                dependency: "state",
                endpoint: "post:/game-session/kick",
                details: new { SessionId = body.SessionId, TargetAccountId = body.TargetAccountId },
                stack: ex.StackTrace);
            return StatusCodes.InternalServerError;
        }
    }

    /// <summary>
    /// Sends a chat message in a game session.
    /// </summary>
    public async Task<StatusCodes> SendChatMessageAsync(
        ChatMessageRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // body.SessionId is the WebSocket session ID
            // body.AccountId is the sender's account
            // body.GameType determines which lobby
            var clientSessionId = body.SessionId;
            var senderId = body.AccountId;
            var gameType = body.GameType;

            _logger.LogInformation("Chat message in game {GameType}: {MessageType}", gameType, body.MessageType);

            // Get the lobby for this game type (don't auto-create for chat)
            var lobbyId = await GetLobbySessionAsync(gameType);
            if (lobbyId == Guid.Empty)
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
            var chatEvent = new ChatMessageReceivedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SessionId = lobbyId,
                MessageId = Guid.NewGuid(),
                SenderId = senderId,
                SenderName = senderPlayer?.DisplayName,
                Message = body.Message,
                MessageType = MapChatMessageType(body.MessageType),
                IsWhisperToMe = false // Will be set per-recipient for whispers
            };

            // Get WebSocket session IDs directly from player records (each player.SessionId is the WebSocket session that joined)
            // IClientEventPublisher uses string routing keys for RabbitMQ topics
            var targetSessionIds = model.Players
                .Where(p => p.SessionId != Guid.Empty)
                .Select(p => p.SessionId.ToString())
                .ToList();

            if (targetSessionIds.Count == 0)
            {
                _logger.LogWarning("No player sessions found for game {GameType}", gameType);
                return StatusCodes.OK; // Not an error - players may have left
            }

            // Handle whisper messages - only send to sender and target
            if (body.MessageType == ChatMessageRequestMessageType.Whisper && body.TargetPlayerId != Guid.Empty)
            {
                // Find sender and target player sessions
                var targetPlayer = model.Players.FirstOrDefault(p => p.AccountId == body.TargetPlayerId);

                // Send to target with IsWhisperToMe = true
                if (targetPlayer != null)
                {
                    var targetEvent = new ChatMessageReceivedEvent
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send chat message in game {GameType}", body.GameType);
            await _messageBus.TryPublishErrorAsync(
                "game-session",
                "SendChatMessage",
                "unexpected_exception",
                ex.Message,
                dependency: "pubsub",
                endpoint: "post:/game-session/chat",
                details: new { SessionId = body.SessionId },
                stack: ex.StackTrace);
            return StatusCodes.InternalServerError;
        }
    }

    #region Internal Event Handlers

    /// <summary>
    /// Handles session.connected event from Connect service.
    /// Tracks the session and publishes join shortcuts for subscribed accounts.
    /// Called internally by GameSessionEventsController.
    /// </summary>
    /// <param name="sessionId">WebSocket session ID that connected.</param>
    /// <param name="accountId">Account ID owning the session.</param>
    internal async Task HandleSessionConnectedInternalAsync(string sessionId, string accountId)
    {
        if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(accountId))
        {
            _logger.LogWarning("Invalid session.connected event - sessionId or accountId missing");
            return;
        }

        if (!Guid.TryParse(accountId, out var accountGuid))
        {
            _logger.LogWarning("Invalid accountId format: {AccountId}", accountId);
            return;
        }

        if (!Guid.TryParse(sessionId, out var sessionGuid))
        {
            _logger.LogWarning("Invalid sessionId format: {SessionId}", sessionId);
            return;
        }

        // Check if account is in our local subscription cache (fast filter)
        if (!_accountSubscriptions.ContainsKey(accountGuid))
        {
            await FetchAndCacheSubscriptionsAsync(accountGuid);
        }

        // Publish shortcuts for subscribed game services
        if (_accountSubscriptions.TryGetValue(accountGuid, out var stubNames))
        {
            var ourServices = stubNames.Where(IsOurService).ToList();
            if (ourServices.Count > 0)
            {
                _logger.LogDebug("Account {AccountId} has {Count} subscriptions matching our services: {Services}",
                    accountId, ourServices.Count, string.Join(", ", ourServices));

                // Store subscriber session in lib-state (distributed tracking)
                await StoreSubscriberSessionAsync(accountGuid, sessionGuid);

                foreach (var stubName in ourServices)
                {
                    await PublishJoinShortcutAsync(sessionGuid, accountGuid, stubName);
                }
            }
            else
            {
                _logger.LogDebug("Account {AccountId} has no subscriptions matching our services", accountId);
            }
        }
        else
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
    internal async Task HandleSessionDisconnectedInternalAsync(string sessionId, Guid? accountId)
    {
        if (string.IsNullOrEmpty(sessionId) || !Guid.TryParse(sessionId, out var sessionGuid))
        {
            return;
        }

        // Only remove from subscriber tracking if the session was authenticated
        if (accountId.HasValue)
        {
            await RemoveSubscriberSessionAsync(accountId.Value, sessionGuid);
            _logger.LogDebug("Removed session {SessionId} (account {AccountId}) from subscriber tracking", sessionGuid, accountId.Value);
        }
        else
        {
            _logger.LogDebug("Session {SessionId} disconnected (was not authenticated, no subscriber tracking to remove)", sessionGuid);
        }
    }

    /// <summary>
    /// Handles subscription.updated event from Subscription service.
    /// Updates subscription cache and publishes/revokes shortcuts for affected connected sessions.
    /// Called internally by GameSessionEventsController.
    /// </summary>
    /// <param name="accountId">Account whose subscription changed.</param>
    /// <param name="stubName">Stub name of the service (e.g., "arcadia").</param>
    /// <param name="action">Action that triggered the event (created, updated, cancelled, expired, renewed).</param>
    /// <param name="isActive">Whether the subscription is currently active.</param>
    internal async Task HandleSubscriptionUpdatedInternalAsync(Guid accountId, string stubName, string action, bool isActive)
    {
        _logger.LogInformation("Subscription update for account {AccountId}: stubName={StubName}, action={Action}, isActive={IsActive}",
            accountId, stubName, action, isActive);

        // Update the cache
        if (isActive && (action == "created" || action == "renewed" || action == "updated"))
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
        else if (!isActive || action == "cancelled" || action == "expired")
        {
            if (_accountSubscriptions.TryGetValue(accountId, out var existingSet))
            {
                lock (existingSet)
                {
                    existingSet.Remove(stubName);
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

        // Get sessions from lib-state (distributed subscriber tracking)
        var connectedSessionsForAccount = await GetSubscriberSessionsAsync(accountId);

        _logger.LogDebug("Found {Count} connected sessions for account {AccountId}", connectedSessionsForAccount.Count, accountId);

        foreach (var sessionId in connectedSessionsForAccount)
        {
            if (isActive)
            {
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
        try
        {
            var response = await _subscriptionClient.QueryCurrentSubscriptionsAsync(
                new QueryCurrentSubscriptionsRequest { AccountId = accountId });

            if (response?.Subscriptions != null && response.Subscriptions.Count > 0)
            {
                // Filter first, then select - StubName is required (non-nullable) per schema
                var stubs = new HashSet<string>(
                    response.Subscriptions.Where(s => !string.IsNullOrEmpty(s.StubName)).Select(s => s.StubName),
                    StringComparer.OrdinalIgnoreCase);

                _accountSubscriptions[accountId] = stubs;
                _logger.LogDebug("Cached {Count} subscriptions for account {AccountId}: {Stubs}",
                    stubs.Count, accountId, string.Join(", ", stubs));
            }
            else
            {
                _logger.LogDebug("No subscriptions found for account {AccountId}", accountId);
                _accountSubscriptions[accountId] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch subscriptions for account {AccountId}", accountId);
            // Don't cache empty set on error - allow retry on next request
        }
    }

    /// <summary>
    /// Stores a subscriber session in distributed state.
    /// Called when a session connects for a subscribed account.
    /// </summary>
    private async Task StoreSubscriberSessionAsync(Guid accountId, Guid sessionId)
    {
        try
        {
            var store = _stateStoreFactory.GetStore<SubscriberSessionsModel>(StateStoreDefinitions.GameSession);
            var key = SUBSCRIBER_SESSIONS_PREFIX + accountId.ToString();

            var existing = await store.GetAsync(key) ?? new SubscriberSessionsModel { AccountId = accountId };
            existing.SessionIds.Add(sessionId);
            existing.UpdatedAt = DateTimeOffset.UtcNow;

            await store.SaveAsync(key, existing);
            _logger.LogDebug("Stored subscriber session {SessionId} for account {AccountId}", sessionId, accountId);
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
        try
        {
            var store = _stateStoreFactory.GetStore<SubscriberSessionsModel>(StateStoreDefinitions.GameSession);
            var key = SUBSCRIBER_SESSIONS_PREFIX + accountId.ToString();

            var existing = await store.GetAsync(key);
            if (existing != null)
            {
                existing.SessionIds.Remove(sessionId);
                existing.UpdatedAt = DateTimeOffset.UtcNow;

                if (existing.SessionIds.Count == 0)
                {
                    await store.DeleteAsync(key);
                }
                else
                {
                    await store.SaveAsync(key, existing);
                }
            }
            _logger.LogDebug("Removed subscriber session {SessionId} for account {AccountId}", sessionId, accountId);
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
        try
        {
            // Get or create the lobby for this game service (internal state ID, not exposed to client)
            var lobbyId = await GetOrCreateLobbySessionAsync(stubName);
            if (lobbyId == Guid.Empty)
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
                GameType = stubName  // e.g., "arcadia", "generic"
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
    private async Task<Guid> GetOrCreateLobbySessionAsync(string stubName)
    {
        var lobbyKey = LOBBY_KEY_PREFIX + stubName.ToLowerInvariant();

        try
        {
            var sessionStore = _stateStoreFactory.GetStore<GameSessionModel>(StateStoreDefinitions.GameSession);

            // Check for existing lobby
            var existingLobby = await sessionStore.GetAsync(lobbyKey);

            if (existingLobby != null && existingLobby.Status != GameSessionResponseStatus.Finished)
            {
                _logger.LogDebug("Found existing lobby {LobbyId} for {StubName}", existingLobby.SessionId, stubName);
                return Guid.Parse(existingLobby.SessionId);
            }

            // Create new lobby
            var lobbyId = Guid.NewGuid();
            var gameType = MapStubNameToGameType(stubName);

            var lobby = new GameSessionModel
            {
                SessionId = lobbyId.ToString(),
                SessionName = $"{stubName} Lobby",
                GameType = gameType,
                MaxPlayers = _configuration.DefaultLobbyMaxPlayers,
                IsPrivate = false,
                Status = GameSessionResponseStatus.Active,
                CurrentPlayers = 0,
                Players = new List<GamePlayer>(),
                CreatedAt = DateTimeOffset.UtcNow,
                Owner = Guid.Empty, // System-owned lobby
                VoiceEnabled = false // Lobbies don't need voice by default
            };

            // Save the lobby
            await sessionStore.SaveAsync(SESSION_KEY_PREFIX + lobbyId, lobby, SessionTtlOptions);
            await sessionStore.SaveAsync(lobbyKey, lobby, SessionTtlOptions);

            // Add to session list
            var sessionListStore = _stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.GameSession);
            var sessionIds = await sessionListStore.GetAsync(SESSION_LIST_KEY) ?? new List<string>();
            sessionIds.Add(lobbyId.ToString());
            await sessionListStore.SaveAsync(SESSION_LIST_KEY, sessionIds);

            _logger.LogInformation("Created lobby {LobbyId} for {StubName}", lobbyId, stubName);
            return lobbyId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get/create lobby for {StubName}", stubName);
            return Guid.Empty;
        }
    }

    /// <summary>
    /// Gets an existing lobby session for a game type (does NOT create if missing/finished).
    /// Use this for Join/Leave/Action operations that require an existing active lobby.
    /// </summary>
    private async Task<Guid> GetLobbySessionAsync(string gameType)
    {
        var lobbyKey = LOBBY_KEY_PREFIX + gameType.ToLowerInvariant();

        try
        {
            var sessionStore = _stateStoreFactory.GetStore<GameSessionModel>(StateStoreDefinitions.GameSession);
            var existingLobby = await sessionStore.GetAsync(lobbyKey);

            if (existingLobby != null)
            {
                _logger.LogDebug("Found lobby {LobbyId} for {GameType} with status {Status}",
                    existingLobby.SessionId, gameType, existingLobby.Status);
                return Guid.Parse(existingLobby.SessionId);
            }

            _logger.LogDebug("No lobby found for {GameType}", gameType);
            return Guid.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get lobby for {GameType}", gameType);
            return Guid.Empty;
        }
    }

    /// <summary>
    /// Checks if a service stub name is handled by this service.
    /// </summary>
    private bool IsOurService(string stubName)
    {
        return _supportedGameServices.Contains(stubName);
    }

    /// <summary>
    /// Maps a stub name to a game type enum.
    /// </summary>
    private static GameSessionResponseGameType MapStubNameToGameType(string stubName)
    {
        return stubName.ToLowerInvariant() switch
        {
            "arcadia" => GameSessionResponseGameType.Arcadia,
            _ => GameSessionResponseGameType.Generic
        };
    }

    #endregion

    #region Helper Methods

    private async Task<GameSessionResponse?> LoadSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        var model = await _stateStoreFactory.GetStore<GameSessionModel>(StateStoreDefinitions.GameSession)
            .GetAsync(SESSION_KEY_PREFIX + sessionId, cancellationToken);

        return model != null ? MapModelToResponse(model) : null;
    }

    private static GameSessionResponse MapModelToResponse(GameSessionModel model)
    {
        return new GameSessionResponse
        {
            SessionId = Guid.Parse(model.SessionId),
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

    private static GameSessionResponseGameType MapRequestGameTypeToResponse(CreateGameSessionRequestGameType gameType)
    {
        return gameType switch
        {
            CreateGameSessionRequestGameType.Arcadia => GameSessionResponseGameType.Arcadia,
            CreateGameSessionRequestGameType.Generic => GameSessionResponseGameType.Generic,
            _ => GameSessionResponseGameType.Generic
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

    private static ChatMessageReceivedEventMessageType MapChatMessageType(ChatMessageRequestMessageType messageType)
    {
        return messageType switch
        {
            ChatMessageRequestMessageType.Public => ChatMessageReceivedEventMessageType.Public,
            ChatMessageRequestMessageType.Whisper => ChatMessageReceivedEventMessageType.Whisper,
            ChatMessageRequestMessageType.System => ChatMessageReceivedEventMessageType.System,
            _ => ChatMessageReceivedEventMessageType.Public
        };
    }

    private static VoiceConnectionInfoTier MapVoiceTierToConnectionInfoTier(VoiceTier tier)
    {
        return tier switch
        {
            VoiceTier.P2p => VoiceConnectionInfoTier.P2p,
            VoiceTier.Scaled => VoiceConnectionInfoTier.Scaled,
            _ => VoiceConnectionInfoTier.P2p
        };
    }

    private static VoiceConnectionInfoCodec MapVoiceCodecToConnectionInfoCodec(VoiceCodec codec)
    {
        return codec switch
        {
            VoiceCodec.Opus => VoiceConnectionInfoCodec.Opus,
            VoiceCodec.G711 => VoiceConnectionInfoCodec.G711,
            VoiceCodec.G722 => VoiceConnectionInfoCodec.G722,
            _ => VoiceConnectionInfoCodec.Opus
        };
    }

    #endregion

    #region Permission Registration

    /// <summary>
    /// Registers this service's API permissions with the Permission service on startup.
    /// Uses generated permission data from x-permissions sections in the OpenAPI schema.
    /// </summary>
    public async Task RegisterServicePermissionsAsync()
    {
        _logger.LogInformation("Registering GameSession service permissions...");
        await GameSessionPermissionRegistration.RegisterViaEventAsync(_messageBus, _logger);
    }

    #endregion
}

/// <summary>
/// Internal model for storing game session data in the state store.
/// Separates storage from API response format.
/// </summary>
internal class GameSessionModel
{
    public string SessionId { get; set; } = string.Empty;
    public GameSessionResponseGameType GameType { get; set; }
    public string? SessionName { get; set; }
    public GameSessionResponseStatus Status { get; set; }
    public int MaxPlayers { get; set; }
    public int CurrentPlayers { get; set; }
    public bool IsPrivate { get; set; }
    public Guid Owner { get; set; }
    public List<GamePlayer> Players { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public object? GameSettings { get; set; }

    /// <summary>
    /// Whether voice communication is enabled for this session.
    /// </summary>
    public bool VoiceEnabled { get; set; }

    /// <summary>
    /// The voice room ID if voice is enabled.
    /// </summary>
    public Guid? VoiceRoomId { get; set; }

    /// <summary>
    /// Type of session - lobby (persistent) or matchmade (time-limited with reservations).
    /// </summary>
    public SessionType SessionType { get; set; } = SessionType.Lobby;

    /// <summary>
    /// For matchmade sessions - list of player reservations.
    /// </summary>
    public List<ReservationModel> Reservations { get; set; } = new();

    /// <summary>
    /// For matchmade sessions - when reservations expire.
    /// </summary>
    public DateTimeOffset? ReservationExpiresAt { get; set; }
}

/// <summary>
/// Internal model for storing reservation data.
/// </summary>
internal class ReservationModel
{
    /// <summary>
    /// Account ID this reservation is for.
    /// </summary>
    public Guid AccountId { get; set; }

    /// <summary>
    /// One-time use token for claiming this reservation.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// When this reservation was created.
    /// </summary>
    public DateTimeOffset ReservedAt { get; set; }

    /// <summary>
    /// Whether this reservation has been claimed.
    /// </summary>
    public bool Claimed { get; set; }

    /// <summary>
    /// When this reservation was claimed.
    /// </summary>
    public DateTimeOffset? ClaimedAt { get; set; }
}

/// <summary>
/// Model for tracking subscriber sessions in distributed state.
/// Stores which WebSocket sessions are connected for each subscribed account.
/// </summary>
internal class SubscriberSessionsModel
{
    /// <summary>
    /// Account ID for this subscriber.
    /// </summary>
    public Guid AccountId { get; set; }

    /// <summary>
    /// Set of active WebSocket session IDs for this account.
    /// </summary>
    public HashSet<Guid> SessionIds { get; set; } = new();

    /// <summary>
    /// When this record was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
