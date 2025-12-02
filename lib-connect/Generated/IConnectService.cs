using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Services;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Connect;

/// <summary>
/// Service interface for Connect API
/// </summary>
public interface IConnectService : IDaprService
{
        /// <summary>
        /// ProxyInternalRequest operation
        /// </summary>
        Task<(StatusCodes, InternalProxyResponse?)> ProxyInternalRequestAsync(InternalProxyRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// GetServiceMappings operation
        /// </summary>
        Task<(StatusCodes, ServiceMappingsResponse?)> GetServiceMappingsAsync(CancellationToken cancellationToken = default(CancellationToken));

}
