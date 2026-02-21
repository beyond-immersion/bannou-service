using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Connect;

/// <summary>
/// Manual implementation for endpoints that require custom logic.
/// This class extends the generated ConnectControllerBase.
/// </summary>
public partial class ConnectController : ConnectControllerBase
{
    private readonly IConnectService _connectService;
    private readonly ILogger<ConnectController> _logger;

    public ConnectController(IConnectService connectService, BeyondImmersion.BannouService.Services.ITelemetryProvider telemetryProvider, ILogger<ConnectController> logger) : base(connectService, telemetryProvider)
    {
        _connectService = connectService;
        _logger = logger;
    }

    /// <summary>
    /// Handles WebSocket connection via GET upgrade request.
    /// </summary>
    public override async System.Threading.Tasks.Task<Microsoft.AspNetCore.Mvc.IActionResult> ConnectWebSocket([Microsoft.AspNetCore.Mvc.FromHeader][Microsoft.AspNetCore.Mvc.ModelBinding.BindRequired] Connection connection, [Microsoft.AspNetCore.Mvc.FromHeader][Microsoft.AspNetCore.Mvc.ModelBinding.BindRequired] Upgrade upgrade, [Microsoft.AspNetCore.Mvc.FromHeader][Microsoft.AspNetCore.Mvc.ModelBinding.BindRequired] string authorization, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken))
    {
        return await HandleWebSocketConnectionAsync(authorization, cancellationToken);
    }

    /// <summary>
    /// Handles WebSocket connection via POST request with optional body.
    /// </summary>
    public override async System.Threading.Tasks.Task<Microsoft.AspNetCore.Mvc.IActionResult> ConnectWebSocketPost([Microsoft.AspNetCore.Mvc.FromHeader][Microsoft.AspNetCore.Mvc.ModelBinding.BindRequired] Connection2 connection, [Microsoft.AspNetCore.Mvc.FromHeader][Microsoft.AspNetCore.Mvc.ModelBinding.BindRequired] Upgrade2 upgrade, [Microsoft.AspNetCore.Mvc.FromHeader][Microsoft.AspNetCore.Mvc.ModelBinding.BindRequired] string authorization, [Microsoft.AspNetCore.Mvc.FromBody] ConnectRequest? body, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken))
    {
        return await HandleWebSocketConnectionAsync(authorization, cancellationToken);
    }

    /// <summary>
    /// Common WebSocket connection handling logic.
    /// Validates JWT, accepts WebSocket upgrade, and initiates binary protocol communication.
    /// </summary>
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

            // Extract X-Service-Token header for Internal mode authentication
            string? serviceTokenHeader = null;
            if (HttpContext.Request.Headers.TryGetValue("X-Service-Token", out var serviceTokenValues))
            {
                serviceTokenHeader = serviceTokenValues.FirstOrDefault();
            }

            // Validate and parse JWT token (or service token for Internal mode)
            var (sessionId, accountId, roles, authorizations, isReconnection) = await connectService.ValidateJWTAndExtractSessionAsync(authorization, serviceTokenHeader, cancellationToken);
            if (sessionId == null)
            {
                return Unauthorized("Invalid or expired JWT token");
            }

            // Check connection capacity BEFORE accepting WebSocket upgrade
            // This allows returning 503 Service Unavailable instead of accepting then immediately closing
            if (!connectService.CanAcceptNewConnection())
            {
                _logger.LogWarning("Maximum concurrent connections ({MaxConnections}) reached, rejecting WebSocket upgrade for session {SessionId}",
                    connectService.MaxConcurrentConnections, sessionId);
                return StatusCode(503, "Service temporarily unavailable: maximum connections reached");
            }

            // Accept WebSocket connection - this starts the HTTP 101 response
            var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();

            // Handle WebSocket communication with binary protocol, passing account ID, roles, authorizations, and reconnection flag
            await connectService.HandleWebSocketCommunicationAsync(webSocket, sessionId, accountId, roles, authorizations, isReconnection, cancellationToken);

            // Return EmptyResult since the response already started with 101 Switching Protocols
            return new EmptyResult();
        }
        catch (OperationCanceledException)
        {
            // Connection was cancelled - if WebSocket was already accepted, return empty
            if (HttpContext.WebSockets.IsWebSocketRequest && HttpContext.Response.HasStarted)
            {
                return new EmptyResult();
            }
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WebSocket connection failed");
            // Only return error status if response hasn't started yet
            if (!HttpContext.Response.HasStarted)
            {
                return StatusCode(500, "WebSocket connection failed");
            }
            return new EmptyResult();
        }
    }
}
