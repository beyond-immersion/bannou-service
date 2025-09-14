using BeyondImmersion.BannouService.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Connect;

/// <summary>
/// Manual implementation for endpoints that require custom logic.
/// This partial class extends the generated ConnectControllerBase.
/// </summary>
public partial class ConnectController : ConnectControllerBaseControllerBase
{
    private readonly IConnectService _connectService;
    private readonly IAuthClient _authClient;
    private readonly ILogger<ConnectController> _logger;

    public ConnectController(
        IConnectService connectService,
        IAuthClient authClient,
        ILogger<ConnectController> logger)
    {
        _connectService = connectService;
        _authClient = authClient;
        _logger = logger;
    }

    /// <summary>
    /// Proxies internal HTTP requests through Dapr with permission validation.
    /// </summary>
    public override async Task<ActionResult<InternalProxyResponse>> ProxyInternalRequest(
        InternalProxyRequest body,
        CancellationToken cancellationToken = default)
    {
        var result = await _connectService.ProxyInternalRequestAsync(body, cancellationToken);
        return result.Item1 switch
        {
            StatusCodes.OK => Ok(result.Item2),
            StatusCodes.Forbidden => Forbid(),
            StatusCodes.NotFound => NotFound(),
            _ => StatusCode(500)
        };
    }

    /// <summary>
    /// Discovers available APIs for the current session using Permissions service.
    /// </summary>
    public override async Task<ActionResult<ApiDiscoveryResponse>> DiscoverAPIs(
        ApiDiscoveryRequest body,
        CancellationToken cancellationToken = default)
    {
        var result = await _connectService.DiscoverAPIsAsync(body, cancellationToken);
        return result.Item1 switch
        {
            StatusCodes.OK => Ok(result.Item2),
            StatusCodes.NotFound => NotFound(),
            _ => StatusCode(500)
        };
    }

    /// <summary>
    /// Returns current service routing mappings for monitoring and debugging.
    /// </summary>
    public override async Task<ActionResult<ServiceMappingsResponse>> GetServiceMappings(
        CancellationToken cancellationToken = default)
    {
        var result = await _connectService.GetServiceMappingsAsync(cancellationToken);
        return result.Item1 switch
        {
            StatusCodes.OK => Ok(result.Item2),
            _ => StatusCode(500)
        };
    }

    /// <summary>
    /// Establishes WebSocket connection with JWT authentication.
    /// Implements the 31-byte binary protocol for zero-copy message routing.
    /// </summary>
    public override async Task<IActionResult> ConnectWebSocket(
        Connection connection,
        Upgrade upgrade,
        string authorization,
        CancellationToken cancellationToken = default)
    {
        return await HandleWebSocketConnectionAsync(authorization, cancellationToken);
    }

    /// <summary>
    /// Alternative POST method for WebSocket connections (same implementation).
    /// </summary>
    public override async Task<IActionResult> ConnectWebSocketPost(
        Connection2 connection,
        Upgrade2 upgrade,
        string authorization,
        ConnectRequest? body = null,
        CancellationToken cancellationToken = default)
    {
        return await HandleWebSocketConnectionAsync(authorization, cancellationToken);
    }

    /// <summary>
    /// Common WebSocket connection handling logic.
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

            // Validate and parse JWT token
            var sessionId = await ValidateJWTAndExtractSessionAsync(authorization, cancellationToken);
            if (sessionId == null)
            {
                return Unauthorized("Invalid or expired JWT token");
            }

            _logger.LogInformation("WebSocket connection request from session {SessionId}", sessionId);

            // Accept WebSocket connection
            var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
            _logger.LogInformation("WebSocket connection established for session {SessionId}", sessionId);

            // Handle WebSocket communication with binary protocol
            await HandleWebSocketCommunicationAsync(webSocket, sessionId, cancellationToken);

            return Ok();
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("WebSocket connection cancelled");
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling WebSocket connection");
            return StatusCode(500, "WebSocket connection failed");
        }
    }

    /// <summary>
    /// Validates JWT token and extracts session ID.
    /// </summary>
    private async Task<string?> ValidateJWTAndExtractSessionAsync(
        string authorization,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrEmpty(authorization))
            {
                return null;
            }

            // Handle "Bearer <token>" format
            if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var token = authorization.Substring(7);

                // Use auth service to validate token (pass token via Authorization header)
                // The AuthClient should handle the Bearer token automatically through HttpContext
                var validationResponse = await _authClient.ValidateTokenAsync(cancellationToken);

                if (validationResponse.Valid && !string.IsNullOrEmpty(validationResponse.SessionId))
                {
                    return validationResponse.SessionId;
                }
            }
            // Handle "Reconnect <token>" format (future enhancement)
            else if (authorization.StartsWith("Reconnect ", StringComparison.OrdinalIgnoreCase))
            {
                // TODO: Implement reconnection logic with stored session tokens
                _logger.LogWarning("Reconnection tokens not yet implemented");
                return null;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "JWT validation failed");
            return null;
        }
    }

    /// <summary>
    /// Handles WebSocket communication using the 31-byte binary protocol.
    /// Protocol: [MessageFlags:1][Channel:2][Sequence:4][ServiceGUID:16][MessageID:8][JSONPayload:variable]
    /// </summary>
    private async Task HandleWebSocketCommunicationAsync(
        WebSocket webSocket,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[4096]; // Buffer for receiving messages

        try
        {
            while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("WebSocket close requested for session {SessionId}", sessionId);
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure,
                        "Connection closed by client", cancellationToken);
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    await HandleBinaryMessageAsync(webSocket, sessionId, buffer, result.Count, cancellationToken);
                }
                else if (result.MessageType == WebSocketMessageType.Text)
                {
                    // For backwards compatibility, also handle text messages
                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    _logger.LogDebug("Received text message from session {SessionId}: {Message}",
                        sessionId, message);

                    // Echo back for now (implement JSON-RPC later)
                    var echo = Encoding.UTF8.GetBytes($"Echo: {message}");
                    await webSocket.SendAsync(new ArraySegment<byte>(echo),
                        WebSocketMessageType.Text, true, cancellationToken);
                }
            }
        }
        catch (WebSocketException wsEx)
        {
            _logger.LogWarning(wsEx, "WebSocket error for session {SessionId}: {Error}",
                sessionId, wsEx.Message);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("WebSocket operation cancelled for session {SessionId}", sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in WebSocket communication for session {SessionId}", sessionId);
        }
        finally
        {
            if (webSocket.State == WebSocketState.Open)
            {
                try
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.InternalServerError,
                        "Server error", CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error closing WebSocket for session {SessionId}", sessionId);
                }
            }
        }
    }

    /// <summary>
    /// Handles binary messages using the 31-byte header protocol.
    /// </summary>
    private async Task HandleBinaryMessageAsync(
        WebSocket webSocket,
        string sessionId,
        byte[] buffer,
        int messageLength,
        CancellationToken cancellationToken)
    {
        try
        {
            // Validate minimum message length (31-byte header + at least some JSON)
            if (messageLength < 32)
            {
                _logger.LogWarning("Received binary message too short ({Length} bytes) from session {SessionId}",
                    messageLength, sessionId);
                return;
            }

            // Parse 31-byte header
            var messageFlags = buffer[0];                           // Byte 0: Message flags
            var channel = BitConverter.ToUInt16(buffer, 1);         // Bytes 1-2: Channel
            var sequence = BitConverter.ToUInt32(buffer, 3);        // Bytes 3-6: Sequence number
            var serviceGuidBytes = new byte[16];                    // Bytes 7-22: Service GUID
            Array.Copy(buffer, 7, serviceGuidBytes, 0, 16);
            var serviceGuid = new Guid(serviceGuidBytes);
            var messageId = BitConverter.ToUInt64(buffer, 23);      // Bytes 23-30: Message ID

            // Extract JSON payload (remaining bytes after 31-byte header)
            var jsonLength = messageLength - 31;
            var jsonPayload = Encoding.UTF8.GetString(buffer, 31, jsonLength);

            _logger.LogDebug("Binary message from session {SessionId}: Flags={Flags}, Channel={Channel}, " +
                           "Sequence={Sequence}, ServiceGUID={ServiceGuid}, MessageID={MessageId}, PayloadLength={Length}",
                           sessionId, messageFlags, channel, sequence, serviceGuid, messageId, jsonLength);

            // TODO: Implement message routing logic
            // 1. Look up service name from serviceGuid using session mappings
            // 2. Route JSON payload to appropriate service via Dapr
            // 3. Handle response and send back with same messageId

            // For now, send a simple acknowledgment
            await SendBinaryAckAsync(webSocket, messageId, sequence, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling binary message from session {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// Sends a binary acknowledgment message.
    /// </summary>
    private async Task SendBinaryAckAsync(
        WebSocket webSocket,
        ulong originalMessageId,
        uint originalSequence,
        CancellationToken cancellationToken)
    {
        try
        {
            // Create acknowledgment payload
            var ackPayload = new { status = "received", messageId = originalMessageId };
            var jsonPayload = System.Text.Json.JsonSerializer.Serialize(ackPayload);
            var jsonBytes = Encoding.UTF8.GetBytes(jsonPayload);

            // Create 31-byte header for acknowledgment
            var header = new byte[31];
            header[0] = 0x01;                                      // Flags: ACK message
            BitConverter.GetBytes((ushort)0).CopyTo(header, 1);    // Channel 0
            BitConverter.GetBytes(originalSequence).CopyTo(header, 3); // Same sequence
            // Service GUID: all zeros for system messages
            BitConverter.GetBytes(originalMessageId).CopyTo(header, 23); // Same message ID

            // Combine header and payload
            var response = new byte[31 + jsonBytes.Length];
            Array.Copy(header, 0, response, 0, 31);
            Array.Copy(jsonBytes, 0, response, 31, jsonBytes.Length);

            await webSocket.SendAsync(new ArraySegment<byte>(response),
                WebSocketMessageType.Binary, true, cancellationToken);

            _logger.LogDebug("Sent binary ACK for message {MessageId}", originalMessageId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send binary acknowledgment for message {MessageId}", originalMessageId);
        }
    }
}
