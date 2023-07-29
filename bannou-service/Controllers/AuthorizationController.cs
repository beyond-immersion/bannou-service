using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using BeyondImmersion.BannouService.Controllers.Messages;
using Newtonsoft.Json.Linq;

namespace BeyondImmersion.BannouService.Controllers;

/// <summary>
/// Auth APIs- backed by the Authorization service.
/// </summary>
[DaprController(template: "authorization", serviceType: typeof(AuthorizationService), Name = "authorization")]
[Consumes(MediaTypeNames.Application.Json)]
[Produces(MediaTypeNames.Application.Json)]
public class AuthorizationController : BaseDaprController
{
    protected AuthorizationService Service { get; }

    public AuthorizationController(AuthorizationService service)
    {
        Service = service;
    }

    [HttpPost]
    [DaprRoute("token")]
    public async Task<IActionResult> POST_Token(
        [FromHeader(Name = "username")] string username,
        [FromHeader(Name = "password")] string password,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)]AuthorizationTokenRequest? request)
    {
        await Task.CompletedTask;
        string token = Guid.NewGuid().ToString();

        Program.Logger?.Log(LogLevel.Debug, $"Authorization API generated token [{token}] for user [{username}].");

        var response = new AuthorizationTokenResponse()
        {
            Token = token
        };
        return new OkObjectResult(response);
    }
}
