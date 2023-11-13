using BeyondImmersion.BannouService.Controllers;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;
using BeyondImmersion.BannouService.Attributes;
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

    [HttpGet]
    [HttpPost]
    [DaprRoute("connect")]
    public async Task Post()
    {
        Program.Logger.Log(LogLevel.Warning, "Connection request received from client.");

        // hand off to service without processing-
        // we don't know if it's doing websockets or TCP/UDP/whatever
        var response = await Service.ConnectAsync(HttpContext);
        switch (response.StatusCode)
        {
            case StatusCodes.OK:
                HttpContext.Response.StatusCode = 200;
                return;
            case StatusCodes.BadRequest:
                HttpContext.Response.StatusCode = 400;
                return;
            case StatusCodes.Forbidden:
                HttpContext.Response.StatusCode = 403;
                return;
            default:
                HttpContext.Response.StatusCode = 500;
                break;
        }
    }
}
