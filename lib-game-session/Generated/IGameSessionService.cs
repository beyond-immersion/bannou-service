using BeyondImmersion.BannouService;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.GameSession;

/// <summary>
/// Service interface for GameSession API - generated from controller
/// </summary>
public interface IGameSessionService
{
    /// <summary>
    /// ListGameSessions operation
    /// </summary>
    Task<(StatusCodes, GameSessionListResponse?)> ListGameSessionsAsync(GameType? gameType = null, Status? status = null, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// CreateGameSession operation
    /// </summary>
    Task<(StatusCodes, GameSessionResponse?)> CreateGameSessionAsync(CreateGameSessionRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// GetGameSession operation
    /// </summary>
    Task<(StatusCodes, GameSessionResponse?)> GetGameSessionAsync(Guid sessionId, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// JoinGameSession operation
    /// </summary>
    Task<(StatusCodes, JoinGameSessionResponse?)> JoinGameSessionAsync(Guid sessionId, JoinGameSessionRequest? body = null, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// PerformGameAction operation
    /// </summary>
    Task<(StatusCodes, GameActionResponse?)> PerformGameActionAsync(Guid sessionId, GameActionRequest body, CancellationToken cancellationToken = default(CancellationToken));

}
