using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Services;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.State;

/// <summary>
/// Service interface for State API
/// </summary>
public partial interface IStateService : IBannouService
{
    /// <summary>
    /// GetState operation
    /// </summary>
    Task<(StatusCodes, GetStateResponse?)> GetStateAsync(GetStateRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// SaveState operation
    /// </summary>
    Task<(StatusCodes, SaveStateResponse?)> SaveStateAsync(SaveStateRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// DeleteState operation
    /// </summary>
    Task<(StatusCodes, DeleteStateResponse?)> DeleteStateAsync(DeleteStateRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// QueryState operation
    /// </summary>
    Task<(StatusCodes, QueryStateResponse?)> QueryStateAsync(QueryStateRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// BulkGetState operation
    /// </summary>
    Task<(StatusCodes, BulkGetStateResponse?)> BulkGetStateAsync(BulkGetStateRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// ListStores operation
    /// </summary>
    Task<(StatusCodes, ListStoresResponse?)> ListStoresAsync(ListStoresRequest body, CancellationToken cancellationToken = default(CancellationToken));

}
