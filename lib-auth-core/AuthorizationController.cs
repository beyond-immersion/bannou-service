using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;
using Microsoft.AspNetCore.Mvc.ModelBinding;

/// <summary>
/// Auth APIs- backed by the Authorization service.
/// </summary>
[DaprController(typeof(AuthorizationService))]
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
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] GetTokenRequest? request)
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

            var response = new GetTokenResponse()
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
        [FromBody] ValidateTokenRequest request)
    {
        try
        {
            await Task.CompletedTask;

            var response = new ValidateTokenResponse()
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
