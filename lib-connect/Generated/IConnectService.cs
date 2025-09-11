using BeyondImmersion.BannouService;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Connect
{
    /// <summary>
    /// Service interface for Connect API - generated from controller
    /// </summary>
    public interface IConnectService
    {
        /// <summary>
        /// ProxyInternalRequest operation  
        /// </summary>
        Task<(StatusCodes, InternalProxyResponse?)> ProxyInternalRequestAsync(/* TODO: Add parameters from schema */);

        /// <summary>
        /// DiscoverAPIs operation  
        /// </summary>
        Task<(StatusCodes, ApiDiscoveryResponse?)> DiscoverAPIsAsync(/* TODO: Add parameters from schema */);

    }
}
