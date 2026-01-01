using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Services;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Voice;

/// <summary>
/// Service interface for Voice API
/// </summary>
public partial interface IVoiceService : IBannouService
{
        /// <summary>
        /// CreateVoiceRoom operation
        /// </summary>
        Task<(StatusCodes, VoiceRoomResponse?)> CreateVoiceRoomAsync(CreateVoiceRoomRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// GetVoiceRoom operation
        /// </summary>
        Task<(StatusCodes, VoiceRoomResponse?)> GetVoiceRoomAsync(GetVoiceRoomRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// JoinVoiceRoom operation
        /// </summary>
        Task<(StatusCodes, JoinVoiceRoomResponse?)> JoinVoiceRoomAsync(JoinVoiceRoomRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// LeaveVoiceRoom operation
        /// </summary>
        Task<StatusCodes> LeaveVoiceRoomAsync(LeaveVoiceRoomRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// DeleteVoiceRoom operation
        /// </summary>
        Task<StatusCodes> DeleteVoiceRoomAsync(DeleteVoiceRoomRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// PeerHeartbeat operation
        /// </summary>
        Task<StatusCodes> PeerHeartbeatAsync(PeerHeartbeatRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// AnswerPeer operation
        /// </summary>
        Task<StatusCodes> AnswerPeerAsync(AnswerPeerRequest body, CancellationToken cancellationToken = default(CancellationToken));

}
