using BeyondImmersion.BannouService;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Connect;

/// <summary>
/// Service interface for Connect API
/// </summary>
public interface IConnectService
{
        /// <summary>
        /// ProxyInternalRequest operation
        /// </summary>
        Task<(StatusCodes, InternalProxyResponse?)> ProxyInternalRequestAsync(InternalProxyRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// DiscoverAPIs operation
        /// </summary>
        Task<(StatusCodes, ApiDiscoveryResponse?)> DiscoverAPIsAsync(ApiDiscoveryRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// GetServiceMappings operation
        /// </summary>
        Task<(StatusCodes, ServiceMappingsResponse?)> GetServiceMappingsAsync(CancellationToken cancellationToken = default(CancellationToken));

}
