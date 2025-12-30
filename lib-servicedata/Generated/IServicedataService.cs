using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Services;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Servicedata;

/// <summary>
/// Service interface for Servicedata API
/// </summary>
public partial interface IServicedataService : IBannouService
{
    /// <summary>
    /// ListServices operation
    /// </summary>
    Task<(StatusCodes, ListServicesResponse?)> ListServicesAsync(ListServicesRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// GetService operation
    /// </summary>
    Task<(StatusCodes, ServiceInfo?)> GetServiceAsync(GetServiceRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// CreateService operation
    /// </summary>
    Task<(StatusCodes, ServiceInfo?)> CreateServiceAsync(CreateServiceRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// UpdateService operation
    /// </summary>
    Task<(StatusCodes, ServiceInfo?)> UpdateServiceAsync(UpdateServiceRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// DeleteService operation
    /// </summary>
    Task<StatusCodes> DeleteServiceAsync(DeleteServiceRequest body, CancellationToken cancellationToken = default(CancellationToken));

}
