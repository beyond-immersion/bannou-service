using BeyondImmersion.BannouService.Controllers.Messages;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.Collections.Concurrent;
using System.Net.Mime;

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

    [HttpPost]
    [DaprRoute("")]
    public async Task<IActionResult> Post(
        [FromHeader(Name = "token")] string? authorizationToken,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ConnectRequest? request)
    {
        await Task.CompletedTask;

        if (string.IsNullOrWhiteSpace(authorizationToken))
            return new BadRequestResult();

        var result = new ConnectResponse()
        {

        };
        return new OkObjectResult(result);
    }
}
