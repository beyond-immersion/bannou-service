using Microsoft.AspNetCore.Mvc;
using BeyondImmersion.BannouService.Controllers.Generated;

namespace BeyondImmersion.BannouService.Connect;

/// <summary>
/// Interface for Connect service operations.
/// Implements business logic for the generated ConnectController methods.
/// </summary>
public interface IConnectService
{
    /// <summary>
    /// Internal API proxy for stateless requests
    /// </summary>
    Task<ActionResult<InternalProxyResponse>> ProxyInternalRequestAsync(
        InternalProxyRequest body,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get available APIs for current session
    /// </summary>
    Task<ActionResult<ApiDiscoveryResponse>> DiscoverAPIsAsync(
        ApiDiscoveryRequest body,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Connect WebSocket (GET method)
    /// </summary>
    Task<IActionResult> ConnectWebSocketAsync(
        Connection connection,
        Upgrade upgrade,
        string authorization,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Connect WebSocket (POST method)
    /// </summary>
    Task<IActionResult> ConnectWebSocketPostAsync(
        Connection2 connection,
        Upgrade2 upgrade,
        string authorization,
        ConnectRequest? body,
        CancellationToken cancellationToken = default);
}