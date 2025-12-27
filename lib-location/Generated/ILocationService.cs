using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Services;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Location;

/// <summary>
/// Service interface for Location API
/// </summary>
public partial interface ILocationService : IBannouService
{
    /// <summary>
    /// GetLocation operation
    /// </summary>
    Task<(StatusCodes, LocationResponse?)> GetLocationAsync(GetLocationRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// GetLocationByCode operation
    /// </summary>
    Task<(StatusCodes, LocationResponse?)> GetLocationByCodeAsync(GetLocationByCodeRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// ListLocations operation
    /// </summary>
    Task<(StatusCodes, LocationListResponse?)> ListLocationsAsync(ListLocationsRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// ListLocationsByRealm operation
    /// </summary>
    Task<(StatusCodes, LocationListResponse?)> ListLocationsByRealmAsync(ListLocationsByRealmRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// ListLocationsByParent operation
    /// </summary>
    Task<(StatusCodes, LocationListResponse?)> ListLocationsByParentAsync(ListLocationsByParentRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// ListRootLocations operation
    /// </summary>
    Task<(StatusCodes, LocationListResponse?)> ListRootLocationsAsync(ListRootLocationsRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// GetLocationAncestors operation
    /// </summary>
    Task<(StatusCodes, LocationListResponse?)> GetLocationAncestorsAsync(GetLocationAncestorsRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// GetLocationDescendants operation
    /// </summary>
    Task<(StatusCodes, LocationListResponse?)> GetLocationDescendantsAsync(GetLocationDescendantsRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// CreateLocation operation
    /// </summary>
    Task<(StatusCodes, LocationResponse?)> CreateLocationAsync(CreateLocationRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// UpdateLocation operation
    /// </summary>
    Task<(StatusCodes, LocationResponse?)> UpdateLocationAsync(UpdateLocationRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// SetLocationParent operation
    /// </summary>
    Task<(StatusCodes, LocationResponse?)> SetLocationParentAsync(SetLocationParentRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// RemoveLocationParent operation
    /// </summary>
    Task<(StatusCodes, LocationResponse?)> RemoveLocationParentAsync(RemoveLocationParentRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// DeleteLocation operation
    /// </summary>
    Task<(StatusCodes, object?)> DeleteLocationAsync(DeleteLocationRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// DeprecateLocation operation
    /// </summary>
    Task<(StatusCodes, LocationResponse?)> DeprecateLocationAsync(DeprecateLocationRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// UndeprecateLocation operation
    /// </summary>
    Task<(StatusCodes, LocationResponse?)> UndeprecateLocationAsync(UndeprecateLocationRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// LocationExists operation
    /// </summary>
    Task<(StatusCodes, LocationExistsResponse?)> LocationExistsAsync(LocationExistsRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// SeedLocations operation
    /// </summary>
    Task<(StatusCodes, SeedLocationsResponse?)> SeedLocationsAsync(SeedLocationsRequest body, CancellationToken cancellationToken = default(CancellationToken));

}
