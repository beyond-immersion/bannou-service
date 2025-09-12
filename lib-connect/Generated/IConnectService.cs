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
        Task<(StatusCodes, InternalProxyResponse?)> ProxyInternalRequestAsync(InternalProxyRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Get available APIs for current session
        /// </summary>
        /// <returns>Available APIs discovered successfully</returns>
        public abstract Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<ApiDiscoveryResponse>> DiscoverAPIs(ApiDiscoveryRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Establish WebSocket connection
        /// </summary>
        /// <remarks>
        /// Initiates a WebSocket connection for real-time communication.
        /// <br/>Requires JWT authentication via Authorization header.
        /// <br/>
        /// <br/>**Connection Flow:**
        /// <br/>1. Send HTTP GET request with `Connection: Upgrade` and `Upgrade: websocket` headers
        /// <br/>2. Include `Authorization: Bearer &lt;jwt_token&gt;` header for authentication
        /// <br/>3. Server validates JWT and extracts user claims (roles, scopes, services)
        /// <br/>4. Connection upgrades to WebSocket protocol
        /// <br/>5. Client can send binary messages using the custom protocol
        /// <br/>
        /// <br/>**Reconnection:**
        /// <br/>For existing sessions, use `Authorization: Reconnect &lt;reconnect_token&gt;` instead.
        /// </remarks>
        /// <param name="connection">Must be "Upgrade" to initiate WebSocket connection</param>
        /// <param name="upgrade">Must be "websocket" to specify protocol upgrade</param>
        /// <param name="authorization">JWT Bearer token for new connections: "Bearer &lt;jwt_token&gt;"
        /// <br/>Reconnect token for existing sessions: "Reconnect &lt;reconnect_token&gt;"</param>
        public abstract Tasks.Task<Microsoft.AspNetCore.Mvc.IActionResult> ConnectWebSocket(Connection connection, Upgrade upgrade, string authorization, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Establish WebSocket connection (POST variant)
        /// </summary>
        /// <remarks>
        /// Alternative POST method for establishing WebSocket connections.
        /// <br/>Functionally identical to the GET method but supports clients that
        /// <br/>require POST for WebSocket upgrades.
        /// </remarks>
        /// <param name="body">Optional connection parameters</param>
        public abstract Tasks.Task<Microsoft.AspNetCore.Mvc.IActionResult> ConnectWebSocketPost(Connection2 connection, Upgrade2 upgrade, string authorization, ConnectRequest? body = null, CancellationToken cancellationToken = default(CancellationToken));

    }
}
