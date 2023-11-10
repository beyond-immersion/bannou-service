using BeyondImmersion.BannouService.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.Net.Mime;
using System.Net.WebSockets;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Connect.Messages;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Connect;

/// <summary>
/// Connect APIs- backed by the Connect service.
/// </summary>
[DaprController(typeof(IConnectService))]
[Consumes(MediaTypeNames.Application.Json)]
[Produces(MediaTypeNames.Application.Json)]
public sealed class ConnectController : BaseDaprController
{
    public IConnectService Service { get; }

    public ConnectController(IConnectService service)
    {
        Service = service;
    }

    [DaprRoute("connect")]
    public async Task<IActionResult> Post(
        [FromHeader(Name = "token")] string? accessToken,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ConnectRequest? request)
    {
        await Task.CompletedTask;

        Program.Logger.Log(LogLevel.Warning, "Connection request received from client.");

        if (string.IsNullOrWhiteSpace(accessToken))
            return new BadRequestResult();

        // use service handler to obtain JWT
        if (HttpContext.WebSockets.IsWebSocketRequest)
        {
            Program.Logger.Log(LogLevel.Warning, "Websocket connection request received from client.");

            //using var websocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
            //await EchoMessage(websocket);
        }

        var result = new ConnectResponse()
        {

        };

        return StatusCodes.OK.ToActionResult(result);
    }

    private static async Task EchoMessage(WebSocket webSocket)
    {
        var buffer = new byte[1024 * 4];
        var receiveResult = await webSocket.ReceiveAsync(
            new ArraySegment<byte>(buffer), CancellationToken.None);

        while (!receiveResult.CloseStatus.HasValue)
        {
            await webSocket.SendAsync(
                new ArraySegment<byte>(buffer, 0, receiveResult.Count),
                receiveResult.MessageType,
                receiveResult.EndOfMessage,
                CancellationToken.None);

            receiveResult = await webSocket.ReceiveAsync(
                new ArraySegment<byte>(buffer), CancellationToken.None);
        }

        await webSocket.CloseAsync(
            receiveResult.CloseStatus.Value,
            receiveResult.CloseStatusDescription,
            CancellationToken.None);
    }
}
