using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Services;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Species;

/// <summary>
/// Service interface for Species API
/// </summary>
public partial interface ISpeciesService : IBannouService
{
        /// <summary>
        /// GetSpecies operation
        /// </summary>
        Task<(StatusCodes, SpeciesResponse?)> GetSpeciesAsync(GetSpeciesRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// GetSpeciesByCode operation
        /// </summary>
        Task<(StatusCodes, SpeciesResponse?)> GetSpeciesByCodeAsync(GetSpeciesByCodeRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// ListSpecies operation
        /// </summary>
        Task<(StatusCodes, SpeciesListResponse?)> ListSpeciesAsync(ListSpeciesRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// ListSpeciesByRealm operation
        /// </summary>
        Task<(StatusCodes, SpeciesListResponse?)> ListSpeciesByRealmAsync(ListSpeciesByRealmRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// CreateSpecies operation
        /// </summary>
        Task<(StatusCodes, SpeciesResponse?)> CreateSpeciesAsync(CreateSpeciesRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// UpdateSpecies operation
        /// </summary>
        Task<(StatusCodes, SpeciesResponse?)> UpdateSpeciesAsync(UpdateSpeciesRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// DeleteSpecies operation
        /// </summary>
        Task<(StatusCodes, object?)> DeleteSpeciesAsync(DeleteSpeciesRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// DeprecateSpecies operation
        /// </summary>
        Task<(StatusCodes, SpeciesResponse?)> DeprecateSpeciesAsync(DeprecateSpeciesRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// UndeprecateSpecies operation
        /// </summary>
        Task<(StatusCodes, SpeciesResponse?)> UndeprecateSpeciesAsync(UndeprecateSpeciesRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// MergeSpecies operation
        /// </summary>
        Task<(StatusCodes, MergeSpeciesResponse?)> MergeSpeciesAsync(MergeSpeciesRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// AddSpeciesToRealm operation
        /// </summary>
        Task<(StatusCodes, SpeciesResponse?)> AddSpeciesToRealmAsync(AddSpeciesToRealmRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// RemoveSpeciesFromRealm operation
        /// </summary>
        Task<(StatusCodes, SpeciesResponse?)> RemoveSpeciesFromRealmAsync(RemoveSpeciesFromRealmRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// SeedSpecies operation
        /// </summary>
        Task<(StatusCodes, SeedSpeciesResponse?)> SeedSpeciesAsync(SeedSpeciesRequest body, CancellationToken cancellationToken = default(CancellationToken));

}
