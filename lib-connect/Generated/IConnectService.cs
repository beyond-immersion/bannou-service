using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Services;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Connect;

/// <summary>
/// Service interface for Connect API
/// </summary>
[Obsolete]
public partial interface IConnectService : IDaprService
{
    /// <summary>
    /// ProxyInternalRequest operation
    /// </summary>
    Task<(StatusCodes, InternalProxyResponse?)> ProxyInternalRequestAsync(InternalProxyRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// GetClientCapabilities operation
    /// </summary>
    Task<(StatusCodes, ClientCapabilitiesResponse?)> GetClientCapabilitiesAsync(GetClientCapabilitiesRequest body, CancellationToken cancellationToken = default(CancellationToken));

}
