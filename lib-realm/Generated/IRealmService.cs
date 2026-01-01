using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Services;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Realm;

/// <summary>
/// Service interface for Realm API
/// </summary>
public partial interface IRealmService : IBannouService
{
        /// <summary>
        /// GetRealm operation
        /// </summary>
        Task<(StatusCodes, RealmResponse?)> GetRealmAsync(GetRealmRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// GetRealmByCode operation
        /// </summary>
        Task<(StatusCodes, RealmResponse?)> GetRealmByCodeAsync(GetRealmByCodeRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// ListRealms operation
        /// </summary>
        Task<(StatusCodes, RealmListResponse?)> ListRealmsAsync(ListRealmsRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// CreateRealm operation
        /// </summary>
        Task<(StatusCodes, RealmResponse?)> CreateRealmAsync(CreateRealmRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// UpdateRealm operation
        /// </summary>
        Task<(StatusCodes, RealmResponse?)> UpdateRealmAsync(UpdateRealmRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// DeleteRealm operation
        /// </summary>
        Task<StatusCodes> DeleteRealmAsync(DeleteRealmRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// DeprecateRealm operation
        /// </summary>
        Task<(StatusCodes, RealmResponse?)> DeprecateRealmAsync(DeprecateRealmRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// UndeprecateRealm operation
        /// </summary>
        Task<(StatusCodes, RealmResponse?)> UndeprecateRealmAsync(UndeprecateRealmRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// RealmExists operation
        /// </summary>
        Task<(StatusCodes, RealmExistsResponse?)> RealmExistsAsync(RealmExistsRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// SeedRealms operation
        /// </summary>
        Task<(StatusCodes, SeedRealmsResponse?)> SeedRealmsAsync(SeedRealmsRequest body, CancellationToken cancellationToken = default(CancellationToken));

}
