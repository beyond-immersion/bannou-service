using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Services;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.GameSession;

/// <summary>
/// Service interface for GameSession API
/// </summary>
public partial interface IGameSessionService : IBannouService
{
        /// <summary>
        /// ListGameSessions operation
        /// </summary>
        Task<(StatusCodes, GameSessionListResponse?)> ListGameSessionsAsync(ListGameSessionsRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// CreateGameSession operation
        /// </summary>
        Task<(StatusCodes, GameSessionResponse?)> CreateGameSessionAsync(CreateGameSessionRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// GetGameSession operation
        /// </summary>
        Task<(StatusCodes, GameSessionResponse?)> GetGameSessionAsync(GetGameSessionRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// JoinGameSession operation
        /// </summary>
        Task<(StatusCodes, JoinGameSessionResponse?)> JoinGameSessionAsync(JoinGameSessionRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// LeaveGameSession operation
        /// </summary>
        Task<StatusCodes> LeaveGameSessionAsync(LeaveGameSessionRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// KickPlayer operation
        /// </summary>
        Task<StatusCodes> KickPlayerAsync(KickPlayerRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// SendChatMessage operation
        /// </summary>
        Task<StatusCodes> SendChatMessageAsync(ChatMessageRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// PerformGameAction operation
        /// </summary>
        Task<(StatusCodes, GameActionResponse?)> PerformGameActionAsync(GameActionRequest body, CancellationToken cancellationToken = default(CancellationToken));

}
