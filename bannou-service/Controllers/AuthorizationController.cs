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
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] AuthorizationTokenRequest? request)
    {
        string? token = await Service.GetJWT(username, password);
        if (token == null)
        {
            // technically this could be a 403 / unauthorized situation
            // but it's best not to leak that the account even exists
            Program.Logger?.Log(LogLevel.Debug, $"Authorization API could not generate token for user [{username}].");
            return new NotFoundResult();
        }

        Program.Logger?.Log(LogLevel.Debug, $"Authorization API generated token [{token}] for user [{username}].");

        var response = new AuthorizationTokenResponse()
        {
            Token = token
        };
        return new OkObjectResult(response);
    }

    [HttpPost]
    [DaprRoute("validate")]
    public async Task<IActionResult> POST_Validate(
        [FromBody] AuthorizationValidateRequest request)
    {
        await Task.CompletedTask;

        Program.Logger?.Log(LogLevel.Debug, $"Authorization API validating token [{request.Token}].");

        var response = new AuthorizationValidateResponse()
        {
        };
        return new OkObjectResult(response);
    }
}
