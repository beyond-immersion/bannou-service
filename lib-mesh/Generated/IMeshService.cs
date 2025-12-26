using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Services;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Mesh;

/// <summary>
/// Service interface for Mesh API
/// </summary>
[Obsolete]
public partial interface IMeshService : IDaprService
{
    /// <summary>
    /// GetEndpoints operation
    /// </summary>
    Task<(StatusCodes, GetEndpointsResponse?)> GetEndpointsAsync(GetEndpointsRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// ListEndpoints operation
    /// </summary>
    Task<(StatusCodes, ListEndpointsResponse?)> ListEndpointsAsync(ListEndpointsRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// RegisterEndpoint operation
    /// </summary>
    Task<(StatusCodes, RegisterEndpointResponse?)> RegisterEndpointAsync(RegisterEndpointRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// DeregisterEndpoint operation
    /// </summary>
    Task<(StatusCodes, object?)> DeregisterEndpointAsync(DeregisterEndpointRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// Heartbeat operation
    /// </summary>
    Task<(StatusCodes, HeartbeatResponse?)> HeartbeatAsync(HeartbeatRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// GetRoute operation
    /// </summary>
    Task<(StatusCodes, GetRouteResponse?)> GetRouteAsync(GetRouteRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// GetMappings operation
    /// </summary>
    Task<(StatusCodes, GetMappingsResponse?)> GetMappingsAsync(GetMappingsRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// GetHealth operation
    /// </summary>
    Task<(StatusCodes, MeshHealthResponse?)> GetHealthAsync(GetHealthRequest body, CancellationToken cancellationToken = default(CancellationToken));

}
