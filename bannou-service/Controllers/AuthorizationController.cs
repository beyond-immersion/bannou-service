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
    public async Task<IActionResult> GetToken(
        [FromHeader(Name = "username")] string username,
        [FromHeader(Name = "password")] string password,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] AuthorizationGetTokenRequest? request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(username))
                return new BadRequestResult();

            if (string.IsNullOrWhiteSpace(password))
                return new BadRequestResult();

            string? token = await Service.GetJWT(username, password);
            if (token == null)
                return new NotFoundResult();

            var response = new AuthorizationGetTokenResponse()
            {
                Token = token
            };
            return new OkObjectResult(response);
        }
        catch (Exception exc)
        {
            Program.Logger?.Log(LogLevel.Error, exc, $"An exception was thrown handling API request to [{nameof(GetToken)}] endpoint on [{nameof(AuthorizationController)}].");
            return new StatusCodeResult(500);
        }
    }

    [HttpPost]
    [DaprRoute("validate")]
    public async Task<IActionResult> ValidateToken(
        [FromBody] AuthorizationValidateTokenRequest request)
    {
        try
        {
            await Task.CompletedTask;

            var response = new AuthorizationValidateTokenResponse()
            {
            };
            return new OkObjectResult(response);
        }
        catch (Exception exc)
        {
            Program.Logger?.Log(LogLevel.Error, exc, $"An exception was thrown handling API request to [{nameof(ValidateToken)}] endpoint on [{nameof(AuthorizationController)}].");
            return new StatusCodeResult(500);
        }
    }
}
