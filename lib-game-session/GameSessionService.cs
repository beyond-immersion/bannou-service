using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Dapr.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.GameSession;

/// <summary>
/// GameSession service implementation.
/// Manages game sessions for Arcadia and other multiplayer games.
/// </summary>
[DaprService("game-session", typeof(IGameSessionService), lifetime: ServiceLifetime.Scoped)]
public class GameSessionService : IGameSessionService
{
    private readonly DaprClient _daprClient;
    private readonly ILogger<GameSessionService> _logger;
    private readonly GameSessionServiceConfiguration _configuration;
    private readonly IErrorEventEmitter _errorEventEmitter;

    private const string STATE_STORE = "game-session-statestore";
    private const string SESSION_KEY_PREFIX = "session:";
    private const string SESSION_LIST_KEY = "session-list";
    private const string PUBSUB_NAME = "bannou-pubsub";
    private const string SESSION_CREATED_TOPIC = "game-session.created";
    private const string SESSION_UPDATED_TOPIC = "game-session.updated";
    private const string SESSION_DELETED_TOPIC = "game-session.deleted";
    private const string PLAYER_JOINED_TOPIC = "game-session.player-joined";
    private const string PLAYER_LEFT_TOPIC = "game-session.player-left";

    public GameSessionService(
        DaprClient daprClient,
        ILogger<GameSessionService> logger,
        GameSessionServiceConfiguration configuration,
        IErrorEventEmitter errorEventEmitter)
    {
        _daprClient = daprClient ?? throw new ArgumentNullException(nameof(daprClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _errorEventEmitter = errorEventEmitter ?? throw new ArgumentNullException(nameof(errorEventEmitter));
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
            var sessionIds = await _daprClient.GetStateAsync<List<string>>(
                STATE_STORE,
                SESSION_LIST_KEY,
                cancellationToken: cancellationToken) ?? new List<string>();

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
            var sessionId = Guid.NewGuid().ToString();

            _logger.LogInformation("Creating game session {SessionId} - GameType: {GameType}, MaxPlayers: {MaxPlayers}",
                sessionId, body.GameType, body.MaxPlayers);

            // Create the session model
            var session = new GameSessionModel
            {
                SessionId = sessionId,
                GameType = MapRequestGameTypeToResponse(body.GameType),
                SessionName = body.SessionName,
                MaxPlayers = body.MaxPlayers,
                IsPrivate = body.IsPrivate,
                Owner = Guid.Empty, // TODO: Get from auth context when available
                Status = GameSessionResponseStatus.Waiting,
                CurrentPlayers = 0,
                Players = new List<GamePlayer>(),
                CreatedAt = DateTimeOffset.UtcNow,
                GameSettings = body.GameSettings
            };

            // Save to state store
            await _daprClient.SaveStateAsync(
                STATE_STORE,
                SESSION_KEY_PREFIX + sessionId,
                session,
                cancellationToken: cancellationToken);

            // Add to session list
            var sessionIds = await _daprClient.GetStateAsync<List<string>>(
                STATE_STORE,
                SESSION_LIST_KEY,
                cancellationToken: cancellationToken) ?? new List<string>();

            sessionIds.Add(sessionId);

            await _daprClient.SaveStateAsync(
                STATE_STORE,
                SESSION_LIST_KEY,
                sessionIds,
                cancellationToken: cancellationToken);

            // Publish event
            await _daprClient.PublishEventAsync(
                PUBSUB_NAME,
                SESSION_CREATED_TOPIC,
                new { SessionId = sessionId, GameType = body.GameType.ToString() },
                cancellationToken);

            var response = MapModelToResponse(session);

            _logger.LogInformation("Game session {SessionId} created successfully", sessionId);
            return (StatusCodes.Created, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create game session");
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

            var model = await _daprClient.GetStateAsync<GameSessionModel>(
                STATE_STORE,
                SESSION_KEY_PREFIX + sessionId,
                cancellationToken: cancellationToken);

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
                    SessionId = sessionId,
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
                    SessionId = sessionId,
                    PlayerRole = JoinGameSessionResponsePlayerRole.Player
                });
            }

            // TODO: Get actual account ID from auth context
            var accountId = Guid.NewGuid();

            // Check if player already in session
            if (model.Players.Any(p => p.AccountId == accountId))
            {
                _logger.LogWarning("Player {AccountId} already in session {SessionId}", accountId, sessionId);
                return (StatusCodes.Conflict, new JoinGameSessionResponse
                {
                    Success = false,
                    SessionId = sessionId,
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
            await _daprClient.SaveStateAsync(
                STATE_STORE,
                SESSION_KEY_PREFIX + sessionId,
                model,
                cancellationToken: cancellationToken);

            // Publish event
            await _daprClient.PublishEventAsync(
                PUBSUB_NAME,
                PLAYER_JOINED_TOPIC,
                new { SessionId = sessionId, AccountId = accountId.ToString() },
                cancellationToken);

            var response = new JoinGameSessionResponse
            {
                Success = true,
                SessionId = sessionId,
                PlayerRole = JoinGameSessionResponsePlayerRole.Player,
                GameData = model.GameSettings ?? new { },
                NewPermissions = new List<string>
                {
                    $"game-session:{sessionId}:action",
                    $"game-session:{sessionId}:chat"
                }
            };

            _logger.LogInformation("Player {AccountId} joined game session {SessionId}", accountId, sessionId);
            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to join game session {SessionId}", body.SessionId);
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

            var model = await _daprClient.GetStateAsync<GameSessionModel>(
                STATE_STORE,
                SESSION_KEY_PREFIX + sessionId,
                cancellationToken: cancellationToken);

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
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Leaves a game session.
    /// </summary>
    public async Task<(StatusCodes, object?)> LeaveGameSessionAsync(
        LeaveGameSessionRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sessionId = body.SessionId.ToString();
            _logger.LogInformation("Player leaving game session {SessionId}", sessionId);

            var model = await _daprClient.GetStateAsync<GameSessionModel>(
                STATE_STORE,
                SESSION_KEY_PREFIX + sessionId,
                cancellationToken: cancellationToken);

            if (model == null)
            {
                _logger.LogWarning("Game session {SessionId} not found for leave", sessionId);
                return (StatusCodes.NotFound, null);
            }

            // TODO: Get actual account ID from auth context
            // For now, remove the first player as a demo
            if (model.Players.Count > 0)
            {
                var leavingPlayer = model.Players[0];
                model.Players.RemoveAt(0);
                model.CurrentPlayers = model.Players.Count;

                // Update status
                if (model.CurrentPlayers == 0)
                {
                    model.Status = GameSessionResponseStatus.Finished;
                }
                else if (model.Status == GameSessionResponseStatus.Full)
                {
                    model.Status = GameSessionResponseStatus.Active;
                }

                // Save updated session
                await _daprClient.SaveStateAsync(
                    STATE_STORE,
                    SESSION_KEY_PREFIX + sessionId,
                    model,
                    cancellationToken: cancellationToken);

                // Publish event
                await _daprClient.PublishEventAsync(
                    PUBSUB_NAME,
                    PLAYER_LEFT_TOPIC,
                    new { SessionId = sessionId, AccountId = leavingPlayer.AccountId.ToString() },
                    cancellationToken);

                _logger.LogInformation("Player left game session {SessionId}", sessionId);
            }

            return (StatusCodes.OK, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to leave game session {SessionId}", body.SessionId);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Kicks a player from a game session.
    /// </summary>
    public async Task<(StatusCodes, object?)> KickPlayerAsync(
        KickPlayerRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sessionId = body.SessionId.ToString();
            var targetAccountId = body.TargetAccountId;

            _logger.LogInformation("Kicking player {TargetAccountId} from session {SessionId}. Reason: {Reason}",
                targetAccountId, sessionId, body.Reason);

            var model = await _daprClient.GetStateAsync<GameSessionModel>(
                STATE_STORE,
                SESSION_KEY_PREFIX + sessionId,
                cancellationToken: cancellationToken);

            if (model == null)
            {
                _logger.LogWarning("Game session {SessionId} not found for kick", sessionId);
                return (StatusCodes.NotFound, null);
            }

            // Find and remove the player
            var playerToKick = model.Players.FirstOrDefault(p => p.AccountId == targetAccountId);
            if (playerToKick == null)
            {
                _logger.LogWarning("Player {TargetAccountId} not found in session {SessionId}",
                    targetAccountId, sessionId);
                return (StatusCodes.NotFound, null);
            }

            model.Players.Remove(playerToKick);
            model.CurrentPlayers = model.Players.Count;

            // Update status
            if (model.Status == GameSessionResponseStatus.Full)
            {
                model.Status = GameSessionResponseStatus.Active;
            }

            // Save updated session
            await _daprClient.SaveStateAsync(
                STATE_STORE,
                SESSION_KEY_PREFIX + sessionId,
                model,
                cancellationToken: cancellationToken);

            // Publish event
            await _daprClient.PublishEventAsync(
                PUBSUB_NAME,
                PLAYER_LEFT_TOPIC,
                new { SessionId = sessionId, AccountId = targetAccountId.ToString(), Kicked = true, Reason = body.Reason },
                cancellationToken);

            _logger.LogInformation("Player {TargetAccountId} kicked from session {SessionId}", targetAccountId, sessionId);
            return (StatusCodes.OK, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to kick player from session {SessionId}", body.SessionId);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Sends a chat message in a game session.
    /// </summary>
    public async Task<(StatusCodes, object?)> SendChatMessageAsync(
        ChatMessageRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sessionId = body.SessionId.ToString();

            _logger.LogInformation("Chat message in session {SessionId}: {MessageType}",
                sessionId, body.MessageType);

            var model = await _daprClient.GetStateAsync<GameSessionModel>(
                STATE_STORE,
                SESSION_KEY_PREFIX + sessionId,
                cancellationToken: cancellationToken);

            if (model == null)
            {
                _logger.LogWarning("Game session {SessionId} not found for chat", sessionId);
                return (StatusCodes.NotFound, null);
            }

            // Publish chat event to all session participants
            await _daprClient.PublishEventAsync(
                PUBSUB_NAME,
                $"game-session.{sessionId}.chat",
                new
                {
                    SessionId = sessionId,
                    Message = body.Message,
                    MessageType = body.MessageType.ToString(),
                    TargetPlayerId = body.TargetPlayerId == Guid.Empty ? null : body.TargetPlayerId.ToString(),
                    Timestamp = DateTimeOffset.UtcNow
                },
                cancellationToken);

            _logger.LogDebug("Chat message sent in session {SessionId}", sessionId);
            return (StatusCodes.OK, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send chat message in session {SessionId}", body.SessionId);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #region Helper Methods

    private async Task<GameSessionResponse?> LoadSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        var model = await _daprClient.GetStateAsync<GameSessionModel>(
            STATE_STORE,
            SESSION_KEY_PREFIX + sessionId,
            cancellationToken: cancellationToken);

        return model != null ? MapModelToResponse(model) : null;
    }

    private static GameSessionResponse MapModelToResponse(GameSessionModel model)
    {
        return new GameSessionResponse
        {
            SessionId = model.SessionId,
            GameType = model.GameType,
            SessionName = model.SessionName ?? string.Empty,
            Status = model.Status,
            MaxPlayers = model.MaxPlayers,
            CurrentPlayers = model.CurrentPlayers,
            IsPrivate = model.IsPrivate,
            Owner = model.Owner,
            Players = model.Players,
            CreatedAt = model.CreatedAt,
            GameSettings = model.GameSettings ?? new { }
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

    #endregion

    #region Permission Registration

    /// <summary>
    /// Registers this service's API permissions with the Permissions service on startup.
    /// Uses generated permission data from x-permissions sections in the OpenAPI schema.
    /// </summary>
    public async Task RegisterServicePermissionsAsync()
    {
        _logger.LogInformation("Registering GameSession service permissions...");
        await GameSessionPermissionRegistration.RegisterViaEventAsync(_daprClient, _logger);
    }

    #endregion

    #region Error Event Publishing

    /// <summary>
    /// Publishes an error event for unexpected/internal failures.
    /// Does NOT publish for validation errors or expected failure cases.
    /// </summary>
    private Task PublishErrorEventAsync(
        string operation,
        string errorType,
        string message,
        string? dependency = null,
        object? details = null)
    {
        return _errorEventEmitter.TryPublishAsync(
            serviceId: "game-session",
            operation: operation,
            errorType: errorType,
            message: message,
            dependency: dependency,
            details: details);
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
}
