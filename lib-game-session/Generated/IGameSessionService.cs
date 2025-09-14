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
        Task<(StatusCodes, GameSessionListResponse?)> ListGameSessionsAsync(GameType? gameType, Status? status, CancellationToken cancellationToken = default(CancellationToken));

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
        Task<(StatusCodes, JoinGameSessionResponse?)> JoinGameSessionAsync(Guid sessionId, JoinGameSessionRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// LeaveGameSession operation
        /// </summary>
        Task<(StatusCodes, object?)> LeaveGameSessionAsync(Guid sessionId, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// KickPlayer operation
        /// </summary>
        Task<(StatusCodes, object?)> KickPlayerAsync(Guid sessionId, KickPlayerRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// SendChatMessage operation
        /// </summary>
        Task<(StatusCodes, object?)> SendChatMessageAsync(Guid sessionId, ChatMessageRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// PerformGameAction operation
        /// </summary>
        Task<(StatusCodes, GameActionResponse?)> PerformGameActionAsync(Guid sessionId, GameActionRequest body, CancellationToken cancellationToken = default(CancellationToken));

}
