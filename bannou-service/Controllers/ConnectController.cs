using BeyondImmersion.BannouService.Controllers.Messages;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.Collections.Concurrent;
using System.Net.Mime;
using System.Net.WebSockets;

namespace BeyondImmersion.BannouService.Controllers;

/// <summary>
/// Connect APIs- backed by the Connect service.
/// </summary>
[DaprController(template: "connect", serviceType: typeof(ConnectService), Name = "connect")]
[Consumes(MediaTypeNames.Application.Json)]
[Produces(MediaTypeNames.Application.Json)]
public sealed class ConnectController : BaseDaprController
{
    public ConnectService Service { get; }

    public ConnectController(ConnectService service)
    {
        Service = service;
    }

    [DaprRoute("")]
    public async Task<IActionResult> Post(
        [FromHeader(Name = "token")] string? authorizationToken,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ConnectRequest? request)
    {
        await Task.CompletedTask;

        if (string.IsNullOrWhiteSpace(authorizationToken))
            return new BadRequestResult();

        // use service handler to obtain JWT
        if (HttpContext.WebSockets.IsWebSocketRequest)
        {
            //using var websocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
            //await EchoMessage(websocket);
        }

        var result = new ConnectResponse()
        {

        };
        return new OkObjectResult(result);
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
