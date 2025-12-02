using Microsoft.AspNetCore.Mvc;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Connect;

/// <summary>
/// Manual implementation for endpoints that require custom logic.
/// This class extends the generated ConnectControllerBase.
/// </summary>
public class ConnectController : ConnectControllerBase
{
    private readonly IConnectService _connectService;

    public ConnectController(IConnectService connectService) : base(connectService)
    {
        _connectService = connectService;
    }

    [Obsolete("This method is deprecated and will be removed in future versions")]
    public override async System.Threading.Tasks.Task<Microsoft.AspNetCore.Mvc.IActionResult> ConnectWebSocket([Microsoft.AspNetCore.Mvc.FromHeader][Microsoft.AspNetCore.Mvc.ModelBinding.BindRequired] Connection connection, [Microsoft.AspNetCore.Mvc.FromHeader][Microsoft.AspNetCore.Mvc.ModelBinding.BindRequired] Upgrade upgrade, [Microsoft.AspNetCore.Mvc.FromHeader][Microsoft.AspNetCore.Mvc.ModelBinding.BindRequired] string authorization, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken))
    {
        return await HandleWebSocketConnectionAsync(authorization, cancellationToken);
    }

    [Obsolete("This method is deprecated and will be removed in future versions")]
    public override async System.Threading.Tasks.Task<Microsoft.AspNetCore.Mvc.IActionResult> ConnectWebSocketPost([Microsoft.AspNetCore.Mvc.FromHeader][Microsoft.AspNetCore.Mvc.ModelBinding.BindRequired] Connection2 connection, [Microsoft.AspNetCore.Mvc.FromHeader][Microsoft.AspNetCore.Mvc.ModelBinding.BindRequired] Upgrade2 upgrade, [Microsoft.AspNetCore.Mvc.FromHeader][Microsoft.AspNetCore.Mvc.ModelBinding.BindRequired] string authorization, [Microsoft.AspNetCore.Mvc.FromBody] ConnectRequest? body, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken))
    {
        return await HandleWebSocketConnectionAsync(authorization, cancellationToken);
    }

    /// <summary>
    /// Common WebSocket connection handling logic.
    /// </summary>
    [Obsolete]
    private async Task<IActionResult> HandleWebSocketConnectionAsync(
        string authorization,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Validate WebSocket upgrade request
            if (!HttpContext.WebSockets.IsWebSocketRequest)
            {
                return BadRequest("This endpoint only accepts WebSocket connections");
            }

            // Cast to concrete service type for WebSocket handling
            var connectService = _connectService as ConnectService;
            if (connectService == null)
            {
                return StatusCode(500, "Service implementation not available");
            }

            // Validate and parse JWT token, extracting session ID and roles
            var (sessionId, roles) = await connectService.ValidateJWTAndExtractSessionAsync(authorization, cancellationToken);
            if (sessionId == null)
            {
                return Unauthorized("Invalid or expired JWT token");
            }

            // Accept WebSocket connection
            var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();

            // Handle WebSocket communication with binary protocol, passing user roles for capability initialization
            await connectService.HandleWebSocketCommunicationAsync(webSocket, sessionId, roles, cancellationToken);

            return Ok();
        }
        catch (OperationCanceledException)
        {
            return NoContent();
        }
        catch (Exception)
        {
            return StatusCode(500, "WebSocket connection failed");
        }
    }
}
