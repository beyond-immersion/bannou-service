using BeyondImmersion.Bannou.GameSession.ClientEvents;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging.Services;
using BeyondImmersion.BannouService.Permissions;
using BeyondImmersion.BannouService.Protocol;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State.Services;
using BeyondImmersion.BannouService.Subscriptions;
using BeyondImmersion.BannouService.Voice;
using Microsoft.AspNetCore.Http;
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
    private readonly IVoiceClient? _voiceClient;
    private readonly IPermissionsClient? _permissionsClient;
    private readonly ISubscriptionsClient? _subscriptionsClient;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IClientEventPublisher? _clientEventPublisher;

    private const string STATE_STORE = "game-session-statestore";
    private const string SESSION_KEY_PREFIX = "session:";
    private const string SESSION_LIST_KEY = "session-list";
    private const string LOBBY_KEY_PREFIX = "lobby:";
    private const string SESSION_CREATED_TOPIC = "game-session.created";
    private const string SESSION_UPDATED_TOPIC = "game-session.updated";
    private const string SESSION_DELETED_TOPIC = "game-session.deleted";
    private const string PLAYER_JOINED_TOPIC = "game-session.player-joined";
    private const string PLAYER_LEFT_TOPIC = "game-session.player-left";

    /// <summary>
    /// Game service stub names that this service handles. Matches gameType enum.
    /// </summary>
    private static readonly HashSet<string> _supportedGameServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "arcadia",
        "generic"
    };

    /// <summary>
    /// Tracks connected WebSocket sessions: WebSocket SessionId -> AccountId.
    /// Static for multi-instance safety (Tenet 4).
    /// </summary>
    private static readonly ConcurrentDictionary<string, Guid> _connectedSessions = new();

    /// <summary>
    /// Caches subscription info: AccountId -> Set of subscribed stubNames.
    /// Static for multi-instance safety (Tenet 4).
    /// </summary>
    private static readonly ConcurrentDictionary<Guid, HashSet<string>> _accountSubscriptions = new();

    /// <summary>
    /// Reverse index for client event publishing: AccountId -> Set of WebSocket session IDs.
    /// Static for multi-instance safety (Tenet 4).
    /// </summary>
    private static readonly ConcurrentDictionary<Guid, HashSet<string>> _accountSessions = new();

    /// <summary>
    /// Server salt for GUID generation. Comes from configuration, with fallback to random generation for development.
    /// Instance-based to support configuration injection (Tenet 21).
    /// </summary>
    private readonly string _serverSalt;

    /// <summary>
    /// Creates a new GameSessionService instance.
    /// </summary>
    /// <param name="stateStoreFactory">State store factory for state operations.</param>
    /// <param name="messageBus">Message bus for pub/sub operations.</param>
    /// <param name="logger">Logger for this service.</param>
    /// <param name="configuration">Service configuration.</param>
    /// <param name="httpContextAccessor">HTTP context accessor for reading request headers.</param>
    /// <param name="eventConsumer">Event consumer for pub/sub fan-out.</param>
    /// <param name="clientEventPublisher">Optional client event publisher for pushing events to WebSocket clients. May be null in contexts without Connect service.</param>
    /// <param name="voiceClient">Optional voice client for voice room coordination. May be null if voice service is disabled.</param>
    /// <param name="permissionsClient">Optional permissions client for setting game-session:in_game state. May be null if Permissions service is not loaded.</param>
    /// <param name="subscriptionsClient">Optional subscriptions client for fetching account subscriptions. May be null if Subscriptions service is not loaded.</param>
    public GameSessionService(
        IStateStoreFactory stateStoreFactory,
        IMessageBus messageBus,
        ILogger<GameSessionService> logger,
        GameSessionServiceConfiguration configuration,
        IHttpContextAccessor httpContextAccessor,
        IEventConsumer eventConsumer,
        IClientEventPublisher? clientEventPublisher = null,
        IVoiceClient? voiceClient = null,
        IPermissionsClient? permissionsClient = null,
        ISubscriptionsClient? subscriptionsClient = null)
    {
        _stateStoreFactory = stateStoreFactory ?? throw new ArgumentNullException(nameof(stateStoreFactory));
        _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _clientEventPublisher = clientEventPublisher; // Nullable - may not be available in all contexts (Tenet 6)
        _voiceClient = voiceClient; // Nullable - voice service may be disabled (Tenet 6)
        _permissionsClient = permissionsClient; // Nullable - permissions service may not be loaded (Tenet 6)
        _subscriptionsClient = subscriptionsClient; // Nullable - subscriptions service may not be loaded (Tenet 6)

        // Server salt from configuration - REQUIRED (fail-fast for production safety)
        if (string.IsNullOrEmpty(configuration.ServerSalt))
        {
            throw new InvalidOperationException(
                "GAMESESSION_SERVERSALT is required. All service instances must share the same salt for session shortcuts to work correctly.");
        }
        _serverSalt = configuration.ServerSalt;

        // Register event handlers via partial class (GameSessionServiceEvents.cs)
        ArgumentNullException.ThrowIfNull(eventConsumer, nameof(eventConsumer));
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
            var sessionIds = await _stateStoreFactory.GetStore<List<string>>(STATE_STORE)
                .GetAsync(SESSION_LIST_KEY, cancellationToken) ?? new List<string>();

            var sessions = new List<GameSessionResponse>();

            foreach (var sessionId in sessionIds)
            {
                var session = await LoadSessionAsync(sessionId, cancellationToken);
                if (session == null) continue;

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

            // Create the session model
            var session = new GameSessionModel
            {
                SessionId = sessionId.ToString(),
                GameType = MapRequestGameTypeToResponse(body.GameType),
                SessionName = body.SessionName,
                MaxPlayers = body.MaxPlayers,
                IsPrivate = body.IsPrivate,
                Owner = body.OwnerId,
                Status = GameSessionResponseStatus.Waiting,
                CurrentPlayers = 0,
                Players = new List<GamePlayer>(),
                CreatedAt = DateTimeOffset.UtcNow,
                GameSettings = body.GameSettings,
                VoiceEnabled = _voiceClient != null // Voice enabled if voice service is available
            };

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
            await _stateStoreFactory.GetStore<GameSessionModel>(STATE_STORE)
                .SaveAsync(SESSION_KEY_PREFIX + session.SessionId, session, cancellationToken: cancellationToken);

            // Add to session list
            var sessionListStore = _stateStoreFactory.GetStore<List<string>>(STATE_STORE);
            var sessionIds = await sessionListStore.GetAsync(SESSION_LIST_KEY, cancellationToken) ?? new List<string>();

            sessionIds.Add(session.SessionId);

            await sessionListStore.SaveAsync(SESSION_LIST_KEY, sessionIds, cancellationToken: cancellationToken);

            // Publish event with full model data
            await _messageBus.PublishAsync(
                SESSION_CREATED_TOPIC,
                new GameSessionCreatedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    SessionId = Guid.Parse(session.SessionId),
                    GameType = session.GameType.ToString(),
                    SessionName = session.SessionName ?? string.Empty,
                    Status = session.Status.ToString(),
                    MaxPlayers = session.MaxPlayers,
                    CurrentPlayers = session.CurrentPlayers,
                    IsPrivate = session.IsPrivate,
                    Owner = session.Owner,
                    CreatedAt = session.CreatedAt
                });

            var response = MapModelToResponse(session);

            _logger.LogInformation("Game session {SessionId} created successfully", session.SessionId);
            return (StatusCodes.Created, response);
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
            var sessionId = body.SessionId.ToString();
            _logger.LogInformation("Player joining game session {SessionId}", sessionId);

            var sessionStore = _stateStoreFactory.GetStore<GameSessionModel>(STATE_STORE);
            var model = await sessionStore.GetAsync(SESSION_KEY_PREFIX + sessionId, cancellationToken);

            if (model == null)
            {
                _logger.LogWarning("Game session {SessionId} not found for join", sessionId);
                return (StatusCodes.NotFound, null);
            }

            // Check if session is full
            if (model.CurrentPlayers >= model.MaxPlayers)
            {
                _logger.LogWarning("Game session {SessionId} is full", sessionId);
                return (StatusCodes.Conflict, new JoinGameSessionResponse
                {
                    Success = false,
                    SessionId = Guid.Parse(sessionId),
                    PlayerRole = JoinGameSessionResponsePlayerRole.Player
                });
            }

            // Check session status
            if (model.Status == GameSessionResponseStatus.Finished)
            {
                _logger.LogWarning("Game session {SessionId} is finished", sessionId);
                return (StatusCodes.Conflict, new JoinGameSessionResponse
                {
                    Success = false,
                    SessionId = Guid.Parse(sessionId),
                    PlayerRole = JoinGameSessionResponsePlayerRole.Player
                });
            }

            // AccountId comes from the request body (populated by shortcut system)
            var accountId = body.AccountId;

            // Check if player already in session
            if (model.Players.Any(p => p.AccountId == accountId))
            {
                _logger.LogWarning("Player {AccountId} already in session {SessionId}", accountId, sessionId);
                return (StatusCodes.Conflict, new JoinGameSessionResponse
                {
                    Success = false,
                    SessionId = Guid.Parse(sessionId),
                    PlayerRole = JoinGameSessionResponsePlayerRole.Player
                });
            }

            // Add player to session
            var player = new GamePlayer
            {
                AccountId = accountId,
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

            // Save updated session
            await sessionStore.SaveAsync(SESSION_KEY_PREFIX + sessionId, model, cancellationToken: cancellationToken);

            // Publish event
            await _messageBus.PublishAsync(
                PLAYER_JOINED_TOPIC,
                new GameSessionPlayerJoinedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    SessionId = Guid.Parse(sessionId),
                    AccountId = accountId
                });

            // Set game-session:in_game state to enable leave/chat/action endpoints (Tenet 10)
            var clientSessionId = _httpContextAccessor.HttpContext?.Request.Headers["X-Bannou-Session-Id"].FirstOrDefault();
            if (_permissionsClient != null && !string.IsNullOrEmpty(clientSessionId))
            {
                try
                {
                    await _permissionsClient.UpdateSessionStateAsync(new SessionStateUpdate
                    {
                        SessionId = Guid.Parse(clientSessionId),
                        ServiceId = "game-session",
                        NewState = "in_game"
                    }, cancellationToken);
                    _logger.LogDebug("Set game-session:in_game state for session {SessionId}", clientSessionId);
                }
                catch (Exception ex)
                {
                    // Log but don't fail - the join succeeded, state is secondary
                    _logger.LogWarning(ex, "Failed to set game-session:in_game state for session {SessionId}", clientSessionId);
                }
            }
            else if (_permissionsClient == null)
            {
                _logger.LogDebug("Permissions client not available, game-session:in_game state not set");
            }

            // Build response
            var response = new JoinGameSessionResponse
            {
                Success = true,
                SessionId = Guid.Parse(sessionId),
                PlayerRole = JoinGameSessionResponsePlayerRole.Player,
                GameData = model.GameSettings ?? new object(),
                NewPermissions = new List<string>
                {
                    $"game-session:{sessionId}:action",
                    $"game-session:{sessionId}:chat"
                }
            };

            // Join voice room if voice is enabled and voice endpoint provided
            if (model.VoiceEnabled && model.VoiceRoomId.HasValue && _voiceClient != null)
            {
                try
                {
                    // Get the client's WebSocket session ID from the X-Bannou-Session-Id header
                    // This header is set by Connect service when routing WebSocket requests
                    // Using the actual session ID allows VoiceService to set permissions that the client
                    // will receive via capability updates (they must match activeConnections)
                    var voiceSessionId = _httpContextAccessor.HttpContext?.Request.Headers["X-Bannou-Session-Id"].FirstOrDefault();
                    if (string.IsNullOrEmpty(voiceSessionId))
                    {
                        _logger.LogWarning("X-Bannou-Session-Id header missing - voice join requires WebSocket session context");
                        // Fall back to generating a session ID for HTTP-only requests (e.g., direct API calls for testing)
                        voiceSessionId = Guid.NewGuid().ToString();
                    }

                    _logger.LogDebug("Player {AccountId} joining voice room {VoiceRoomId} with voice session {VoiceSessionId}",
                        accountId, model.VoiceRoomId, voiceSessionId);

                    // Convert VoiceSipEndpoint to Voice service SipEndpoint
                    var sipEndpoint = new SipEndpoint
                    {
                        SdpOffer = body.VoiceEndpoint?.SdpOffer ?? string.Empty,
                        IceCandidates = body.VoiceEndpoint?.IceCandidates?.ToList() ?? new List<string>()
                    };

                    var voiceResponse = await _voiceClient.JoinVoiceRoomAsync(new JoinVoiceRoomRequest
                    {
                        RoomId = model.VoiceRoomId.Value,
                        SessionId = voiceSessionId,
                        DisplayName = player.DisplayName,
                        SipEndpoint = sipEndpoint
                    }, cancellationToken);

                    if (voiceResponse.Success)
                    {
                        // Event-only pattern: Only return minimal voice metadata
                        // Peers are delivered via VoicePeerJoinedEvent to avoid race conditions
                        response.Voice = new VoiceConnectionInfo
                        {
                            VoiceEnabled = true,
                            RoomId = voiceResponse.RoomId,
                            Tier = MapVoiceTierToConnectionInfoTier(voiceResponse.Tier),
                            Codec = MapVoiceCodecToConnectionInfoCodec(voiceResponse.Codec),
                            StunServers = voiceResponse.StunServers?.ToList() ?? new List<string>()
                        };

                        // Store the voice session ID in the player for cleanup on leave
                        player.VoiceSessionId = voiceSessionId;

                        // Save updated session with VoiceSessionId so leave can properly clean up
                        await sessionStore.SaveAsync(SESSION_KEY_PREFIX + sessionId, model, cancellationToken: cancellationToken);

                        _logger.LogInformation("Player {AccountId} joined voice room {VoiceRoomId}",
                            accountId, model.VoiceRoomId);
                    }
                    else
                    {
                        _logger.LogWarning("Failed to join voice room {VoiceRoomId} for player {AccountId}",
                            model.VoiceRoomId, accountId);
                    }
                }
                catch (Exception ex)
                {
                    // Voice join failure is non-fatal - player can still participate in session
                    _logger.LogWarning(ex, "Failed to join voice room for player {AccountId} in session {SessionId}",
                        accountId, sessionId);
                }
            }

            _logger.LogInformation("Player {AccountId} joined game session {SessionId}", accountId, sessionId);
            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to join game session {SessionId}", body.SessionId);
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
            var sessionId = body.SessionId.ToString();
            _logger.LogInformation("Performing game action {ActionType} in session {SessionId}",
                body.ActionType, sessionId);

            var model = await _stateStoreFactory.GetStore<GameSessionModel>(STATE_STORE)
                .GetAsync(SESSION_KEY_PREFIX + sessionId, cancellationToken);

            if (model == null)
            {
                _logger.LogWarning("Game session {SessionId} not found for action", sessionId);
                return (StatusCodes.NotFound, null);
            }

            if (model.Status == GameSessionResponseStatus.Finished)
            {
                _logger.LogWarning("Cannot perform action on finished session {SessionId}", sessionId);
                return (StatusCodes.BadRequest, new GameActionResponse
                {
                    Success = false,
                    ActionId = Guid.NewGuid()
                });
            }

            // Create action response
            var actionId = Guid.NewGuid();
            var response = new GameActionResponse
            {
                Success = true,
                ActionId = actionId,
                Result = new Dictionary<string, object?>
                {
                    ["actionType"] = body.ActionType.ToString(),
                    ["timestamp"] = DateTimeOffset.UtcNow.ToString("O")
                },
                NewGameState = body.ActionData
            };

            _logger.LogInformation("Game action {ActionId} performed successfully in session {SessionId}",
                actionId, sessionId);
            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform game action in session {SessionId}", body.SessionId);
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
            var sessionId = body.SessionId.ToString();
            _logger.LogInformation("Player leaving game session {SessionId}", sessionId);

            var sessionStore = _stateStoreFactory.GetStore<GameSessionModel>(STATE_STORE);
            var model = await sessionStore.GetAsync(SESSION_KEY_PREFIX + sessionId, cancellationToken);

            if (model == null)
            {
                _logger.LogWarning("Game session {SessionId} not found for leave", sessionId);
                return StatusCodes.NotFound;
            }

            // AccountId comes from the request body (populated by shortcut system)
            var accountId = body.AccountId;

            // Find the player in the session
            var leavingPlayer = model.Players.FirstOrDefault(p => p.AccountId == accountId);
            if (leavingPlayer == null)
            {
                _logger.LogWarning("Player {AccountId} not found in session {SessionId}", accountId, sessionId);
                return StatusCodes.NotFound;
            }

            model.Players.Remove(leavingPlayer);
            model.CurrentPlayers = model.Players.Count;

            // Leave voice room if voice is enabled and player has a voice session
            if (model.VoiceEnabled && model.VoiceRoomId.HasValue && _voiceClient != null
                && !string.IsNullOrEmpty(leavingPlayer.VoiceSessionId))
            {
                try
                {
                    _logger.LogDebug("Player {AccountId} leaving voice room {VoiceRoomId} with voice session {VoiceSessionId}",
                        leavingPlayer.AccountId, model.VoiceRoomId, leavingPlayer.VoiceSessionId);

                    await _voiceClient.LeaveVoiceRoomAsync(new LeaveVoiceRoomRequest
                    {
                        RoomId = model.VoiceRoomId.Value,
                        SessionId = leavingPlayer.VoiceSessionId
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
                        _logger.LogDebug("Deleting voice room {VoiceRoomId} as session {SessionId} has ended",
                            model.VoiceRoomId, sessionId);

                        await _voiceClient.DeleteVoiceRoomAsync(new DeleteVoiceRoomRequest
                        {
                            RoomId = model.VoiceRoomId.Value,
                            Reason = "session_ended"
                        }, cancellationToken);

                        _logger.LogInformation("Voice room {VoiceRoomId} deleted for ended session {SessionId}",
                            model.VoiceRoomId, sessionId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete voice room {VoiceRoomId} for session {SessionId}",
                            model.VoiceRoomId, sessionId);
                    }
                }
            }
            else if (model.Status == GameSessionResponseStatus.Full)
            {
                model.Status = GameSessionResponseStatus.Active;
            }

            // Save updated session
            await sessionStore.SaveAsync(SESSION_KEY_PREFIX + sessionId, model, cancellationToken: cancellationToken);

            // Publish event
            await _messageBus.PublishAsync(
                PLAYER_LEFT_TOPIC,
                new GameSessionPlayerLeftEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    SessionId = Guid.Parse(sessionId),
                    AccountId = leavingPlayer.AccountId,
                    Kicked = false
                });

            // Clear game-session:in_game state to remove leave/chat/action endpoint access (Tenet 10)
            var clientSessionId = _httpContextAccessor.HttpContext?.Request.Headers["X-Bannou-Session-Id"].FirstOrDefault();
            if (_permissionsClient != null && !string.IsNullOrEmpty(clientSessionId))
            {
                try
                {
                    await _permissionsClient.ClearSessionStateAsync(new ClearSessionStateRequest
                    {
                        SessionId = Guid.Parse(clientSessionId),
                        ServiceId = "game-session"
                    }, cancellationToken);
                    _logger.LogDebug("Cleared game-session:in_game state for session {SessionId}", clientSessionId);
                }
                catch (Exception ex)
                {
                    // Log but don't fail - the leave succeeded, state cleanup is secondary
                    _logger.LogWarning(ex, "Failed to clear game-session:in_game state for session {SessionId}", clientSessionId);
                }
            }

            _logger.LogInformation("Player left game session {SessionId}", sessionId);

            return StatusCodes.OK;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to leave game session {SessionId}", body.SessionId);
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

            var sessionStore = _stateStoreFactory.GetStore<GameSessionModel>(STATE_STORE);
            var model = await sessionStore.GetAsync(SESSION_KEY_PREFIX + sessionId, cancellationToken);

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
            await sessionStore.SaveAsync(SESSION_KEY_PREFIX + sessionId, model, cancellationToken: cancellationToken);

            // Publish event
            await _messageBus.PublishAsync(
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
            var sessionId = body.SessionId.ToString();

            _logger.LogInformation("Chat message in session {SessionId}: {MessageType}",
                sessionId, body.MessageType);

            var model = await _stateStoreFactory.GetStore<GameSessionModel>(STATE_STORE)
                .GetAsync(SESSION_KEY_PREFIX + sessionId, cancellationToken);

            if (model == null)
            {
                _logger.LogWarning("Game session {SessionId} not found for chat", sessionId);
                return StatusCodes.NotFound;
            }

            // Check if client event publisher is available
            if (_clientEventPublisher == null)
            {
                _logger.LogWarning("IClientEventPublisher not available - cannot send chat message to session {SessionId}", sessionId);
                return StatusCodes.InternalServerError;
            }

            // Get sender info from request context
            var senderAccountIdStr = _httpContextAccessor.HttpContext?.Request.Headers["X-Bannou-Account-Id"].FirstOrDefault();
            var senderId = Guid.TryParse(senderAccountIdStr, out var parsedSenderId) ? parsedSenderId : Guid.Empty;
            var senderPlayer = model.Players.FirstOrDefault(p => p.AccountId == senderId);

            // Build typed client event
            var chatEvent = new ChatMessageReceivedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                EventName = ChatMessageReceivedEventEventName.Game_session_chat_received,
                SessionId = body.SessionId,
                MessageId = Guid.NewGuid(),
                SenderId = senderId,
                SenderName = senderPlayer?.DisplayName,
                Message = body.Message,
                MessageType = MapChatMessageType(body.MessageType),
                IsWhisperToMe = false // Will be set per-recipient for whispers
            };

            // Get WebSocket session IDs for all players in the game session
            var targetSessionIds = new List<string>();
            foreach (var player in model.Players)
            {
                if (_accountSessions.TryGetValue(player.AccountId, out var sessions))
                {
                    lock (sessions)
                    {
                        targetSessionIds.AddRange(sessions);
                    }
                }
            }

            if (targetSessionIds.Count == 0)
            {
                _logger.LogWarning("No connected WebSocket sessions found for game session {SessionId}", sessionId);
                return StatusCodes.OK; // Not an error - players may have disconnected
            }

            // Handle whisper messages - only send to sender and target
            if (body.MessageType == ChatMessageRequestMessageType.Whisper && body.TargetPlayerId != Guid.Empty)
            {
                var whisperTargets = new List<string>();

                // Add sender's sessions
                if (_accountSessions.TryGetValue(senderId, out var senderSessions))
                {
                    lock (senderSessions)
                    {
                        whisperTargets.AddRange(senderSessions);
                    }
                }

                // Add target's sessions with is_whisper_to_me = true
                if (_accountSessions.TryGetValue(body.TargetPlayerId, out var targetSessions))
                {
                    List<string> targetSessionsCopy;
                    lock (targetSessions)
                    {
                        targetSessionsCopy = targetSessions.ToList();
                    }

                    // Send to target with IsWhisperToMe = true
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
                    await _clientEventPublisher.PublishToSessionsAsync(targetSessionsCopy, targetEvent, cancellationToken);
                }

                // Send to sender with is_whisper_to_me = false
                if (whisperTargets.Count > 0)
                {
                    await _clientEventPublisher.PublishToSessionsAsync(whisperTargets, chatEvent, cancellationToken);
                }
            }
            else
            {
                // Public message - send to all session participants
                var sentCount = await _clientEventPublisher.PublishToSessionsAsync(targetSessionIds, chatEvent, cancellationToken);
                _logger.LogDebug("Chat message sent to {SentCount}/{TotalCount} sessions in game session {SessionId}",
                    sentCount, targetSessionIds.Count, sessionId);
            }

            return StatusCodes.OK;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send chat message in session {SessionId}", body.SessionId);
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

        // Track this session (forward index: SessionId -> AccountId)
        _connectedSessions[sessionId] = accountGuid;

        // Maintain reverse index for client event publishing (AccountId -> Set<SessionId>)
        _accountSessions.AddOrUpdate(
            accountGuid,
            _ => new HashSet<string> { sessionId },
            (_, existingSet) =>
            {
                lock (existingSet)
                {
                    existingSet.Add(sessionId);
                }
                return existingSet;
            });

        _logger.LogDebug("Tracking session {SessionId} for account {AccountId}", sessionId, accountId);

        // Fetch subscriptions if not cached
        if (!_accountSubscriptions.ContainsKey(accountGuid))
        {
            await FetchAndCacheSubscriptionsAsync(accountGuid);
        }

        // Publish shortcuts for subscribed game services
        if (_accountSubscriptions.TryGetValue(accountGuid, out var stubNames))
        {
            var ourServices = stubNames.Where(IsOurService).ToList();
            _logger.LogDebug("Account {AccountId} has {Count} subscriptions matching our services: {Services}",
                accountId, ourServices.Count, string.Join(", ", ourServices));

            foreach (var stubName in ourServices)
            {
                await PublishJoinShortcutAsync(sessionId, accountGuid, stubName);
            }
        }
        else
        {
            _logger.LogDebug("No subscriptions found for account {AccountId}", accountId);
        }
    }

    /// <summary>
    /// Handles session.disconnected event from Connect service.
    /// Removes session from tracking.
    /// Called internally by GameSessionEventsController.
    /// </summary>
    /// <param name="sessionId">WebSocket session ID that disconnected.</param>
    internal Task HandleSessionDisconnectedInternalAsync(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return Task.CompletedTask;
        }

        if (_connectedSessions.TryRemove(sessionId, out var accountId))
        {
            // Remove from reverse index
            if (_accountSessions.TryGetValue(accountId, out var sessions))
            {
                lock (sessions)
                {
                    sessions.Remove(sessionId);
                    // Clean up empty sets to prevent memory leaks
                    if (sessions.Count == 0)
                    {
                        _accountSessions.TryRemove(accountId, out _);
                    }
                }
            }
            _logger.LogDebug("Removed session {SessionId} (account {AccountId}) from tracking", sessionId, accountId);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles subscription.updated event from Subscriptions service.
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

        var connectedSessionsForAccount = _connectedSessions
            .Where(kv => kv.Value == accountId)
            .Select(kv => kv.Key)
            .ToList();

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
    /// Fetches and caches subscriptions for an account from the Subscriptions service.
    /// </summary>
    private async Task FetchAndCacheSubscriptionsAsync(Guid accountId)
    {
        if (_subscriptionsClient == null)
        {
            _logger.LogDebug("Subscriptions client not available, cannot fetch subscriptions for account {AccountId}", accountId);
            return;
        }

        try
        {
            var response = await _subscriptionsClient.GetCurrentSubscriptionsAsync(
                new GetCurrentSubscriptionsRequest { AccountId = accountId });

            if (response?.Subscriptions != null && response.Subscriptions.Count > 0)
            {
                var stubs = new HashSet<string>(
                    response.Subscriptions.Select(s => s.StubName ?? string.Empty).Where(s => !string.IsNullOrEmpty(s)),
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
    /// Publishes a join shortcut for a session to access a game lobby.
    /// </summary>
    private async Task PublishJoinShortcutAsync(string sessionId, Guid accountId, string stubName)
    {
        try
        {
            // Get or create the lobby session for this game service
            var lobbySessionId = await GetOrCreateLobbySessionAsync(stubName);
            if (lobbySessionId == Guid.Empty)
            {
                _logger.LogWarning("Failed to get/create lobby for {StubName}, cannot publish shortcut", stubName);
                return;
            }

            var shortcutName = $"join_game_{stubName.ToLowerInvariant()}";

            // Generate shortcut GUID (v7 for shortcuts - session-unique)
            var routeGuid = GuidGenerator.GenerateSessionShortcutGuid(
                sessionId,
                shortcutName,
                "game-session",
                _serverSalt);

            // Generate target GUID (v5 for service capability)
            var targetGuid = GuidGenerator.GenerateServiceGuid(
                sessionId,
                "game-session/sessions/join",
                _serverSalt);

            // Create the pre-bound payload with accountId included
            var boundPayload = new JoinGameSessionRequest
            {
                SessionId = lobbySessionId,
                AccountId = accountId
            };

            var shortcutEvent = new ShortcutPublishedEvent
            {
                EventId = Guid.NewGuid(),
                EventName = ShortcutPublishedEventEventName.Session_shortcut_published,
                Timestamp = DateTimeOffset.UtcNow,
                SessionId = Guid.Parse(sessionId),
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
            // CRITICAL: Must use IClientEventPublisher for session-specific events (Tenet 6)
            // Using _messageBus directly would publish to fanout exchange "bannou" instead
            // of direct exchange "bannou-client-events" with proper routing key
            if (_clientEventPublisher == null)
            {
                _logger.LogWarning("IClientEventPublisher not available - cannot publish shortcut to session {SessionId}", sessionId);
                return;
            }

            var published = await _clientEventPublisher.PublishToSessionAsync(sessionId, shortcutEvent);
            if (published)
            {
                _logger.LogInformation("Published join shortcut {RouteGuid} for session {SessionId} -> lobby {LobbyId} ({StubName})",
                    routeGuid, sessionId, lobbySessionId, stubName);
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
    private async Task RevokeShortcutsForSessionAsync(string sessionId, string stubName)
    {
        try
        {
            var revokeEvent = new ShortcutRevokedEvent
            {
                EventId = Guid.NewGuid(),
                EventName = ShortcutRevokedEventEventName.Session_shortcut_revoked,
                Timestamp = DateTimeOffset.UtcNow,
                SessionId = Guid.Parse(sessionId),
                RevokeByService = "game-session",
                Reason = $"Subscription to {stubName} ended"
            };

            // Publish to session-specific client event channel using direct exchange
            // CRITICAL: Must use IClientEventPublisher for session-specific events (Tenet 6)
            if (_clientEventPublisher == null)
            {
                _logger.LogWarning("IClientEventPublisher not available - cannot revoke shortcuts for session {SessionId}", sessionId);
                return;
            }

            var published = await _clientEventPublisher.PublishToSessionAsync(sessionId, revokeEvent);
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
            var sessionStore = _stateStoreFactory.GetStore<GameSessionModel>(STATE_STORE);

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
                MaxPlayers = 100, // Lobbies can hold many players
                IsPrivate = false,
                Status = GameSessionResponseStatus.Active,
                CurrentPlayers = 0,
                Players = new List<GamePlayer>(),
                CreatedAt = DateTimeOffset.UtcNow,
                Owner = Guid.Empty, // System-owned lobby
                VoiceEnabled = false // Lobbies don't need voice by default
            };

            // Save the lobby
            await sessionStore.SaveAsync(SESSION_KEY_PREFIX + lobbyId, lobby);
            await sessionStore.SaveAsync(lobbyKey, lobby);

            // Add to session list
            var sessionListStore = _stateStoreFactory.GetStore<List<string>>(STATE_STORE);
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
    /// Checks if a service stub name is handled by this service.
    /// </summary>
    private static bool IsOurService(string stubName)
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
        var model = await _stateStoreFactory.GetStore<GameSessionModel>(STATE_STORE)
            .GetAsync(SESSION_KEY_PREFIX + sessionId, cancellationToken);

        return model != null ? MapModelToResponse(model) : null;
    }

    private static GameSessionResponse MapModelToResponse(GameSessionModel model)
    {
        return new GameSessionResponse
        {
            SessionId = Guid.Parse(model.SessionId),
            GameType = model.GameType,
            SessionName = model.SessionName ?? string.Empty,
            Status = model.Status,
            MaxPlayers = model.MaxPlayers,
            CurrentPlayers = model.CurrentPlayers,
            IsPrivate = model.IsPrivate,
            Owner = model.Owner,
            Players = model.Players,
            CreatedAt = model.CreatedAt,
            GameSettings = model.GameSettings ?? new object()
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
    /// Registers this service's API permissions with the Permissions service on startup.
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
}
