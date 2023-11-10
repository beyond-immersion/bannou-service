using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;
using BeyondImmersion.BannouService.Controllers;
using BeyondImmersion.BannouService.Attributes;
using Microsoft.Extensions.Logging;
using BeyondImmersion.BannouService.Authorization.Messages;

namespace BeyondImmersion.BannouService.Authorization;

/// <summary>
/// Auth APIs- backed by the Authorization service.
/// </summary>
[DaprController(typeof(IAuthorizationService))]
[Consumes(MediaTypeNames.Application.Json)]
[Produces(MediaTypeNames.Application.Json)]
public class AuthorizationController : BaseDaprController
{
    protected IAuthorizationService Service { get; }

    public AuthorizationController(IAuthorizationService service)
    {
        Service = service;
    }

    /// <summary>
    /// Register new user account.
    /// </summary>
    [HttpPost]
    [DaprRoute("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
                return StatusCodes.BadRequest.ToActionResult();

            Services.ServiceResponse<AccessData?> registerResult = await Service.Register(request.Username, request.Password, request.Email);
            if (registerResult.StatusCode != StatusCodes.OK)
            {
                if (registerResult.StatusCode == StatusCodes.InternalServerError)
                    return StatusCodes.InternalServerError.ToActionResult();

                return StatusCodes.Forbidden.ToActionResult();
            }

            if (string.IsNullOrWhiteSpace(registerResult.Value?.AccessToken))
                return StatusCodes.Forbidden.ToActionResult();

            var response = request.CreateResponse();
            response.AccessToken = registerResult.Value.AccessToken;
            response.RefreshToken = registerResult.Value.RefreshToken;
            return StatusCodes.OK.ToActionResult(response);
        }
        catch (Exception exc)
        {
            Program.Logger?.Log(LogLevel.Error, exc, $"An exception was thrown handling API request to [{nameof(Register)}] endpoint on [{nameof(AuthorizationController)}].");
            return StatusCodes.InternalServerError.ToActionResult();
        }
    }

    /// <summary>
    /// Returns forbidden if no user found, to avoid leaking to avoid even leaking usernames.
    /// </summary>
    [HttpGet]
    [HttpPost]
    [DaprRoute("login/credentials")]
    public async Task<IActionResult> LoginWithCredentials([FromHeader(Name = "username")] string username, [FromHeader(Name = "password")] string? password)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                return StatusCodes.BadRequest.ToActionResult();

            Services.ServiceResponse<AccessData?> loginResult = await Service.LoginWithCredentials(username, password);
            if (loginResult.StatusCode != StatusCodes.OK)
            {
                if (loginResult.StatusCode == StatusCodes.InternalServerError)
                    return StatusCodes.InternalServerError.ToActionResult();

                return StatusCodes.Forbidden.ToActionResult();
            }

            if (string.IsNullOrWhiteSpace(loginResult.Value?.AccessToken))
                return StatusCodes.Forbidden.ToActionResult();

            var response = new LoginResponse()
            {
                AccessToken = loginResult.Value.AccessToken,
                RefreshToken = loginResult.Value.RefreshToken
            };

            return StatusCodes.OK.ToActionResult(response);
        }
        catch (Exception exc)
        {
            Program.Logger?.Log(LogLevel.Error, exc, $"An exception was thrown handling API request to [{nameof(LoginWithToken)}] endpoint on [{nameof(AuthorizationController)}].");
            return StatusCodes.InternalServerError.ToActionResult();
        }
    }

    /// <summary>
    /// Returns forbidden if no user found, to avoid leaking to avoid even leaking usernames.
    /// </summary>
    [HttpGet]
    [HttpPost]
    [DaprRoute("login/token")]
    public async Task<IActionResult> LoginWithToken([FromHeader(Name = "token")] string? token)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(token))
                return StatusCodes.BadRequest.ToActionResult();

            Services.ServiceResponse<AccessData?> loginResult = await Service.LoginWithToken(token);
            if (loginResult.StatusCode != StatusCodes.OK)
            {
                if (loginResult.StatusCode == StatusCodes.InternalServerError)
                    return StatusCodes.InternalServerError.ToActionResult();

                return StatusCodes.Forbidden.ToActionResult();
            }

            if (string.IsNullOrWhiteSpace(loginResult.Value?.AccessToken))
                return StatusCodes.Forbidden.ToActionResult();

            var response = new LoginResponse()
            {
                AccessToken = loginResult.Value.AccessToken,
                RefreshToken = loginResult.Value.RefreshToken
            };

            return StatusCodes.OK.ToActionResult(response);
        }
        catch (Exception exc)
        {
            Program.Logger?.Log(LogLevel.Error, exc, $"An exception was thrown handling API request to [{nameof(LoginWithToken)}] endpoint on [{nameof(AuthorizationController)}].");
            return StatusCodes.InternalServerError.ToActionResult();
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
            return StatusCodes.OK.ToActionResult(response);
        }
        catch (Exception exc)
        {
            Program.Logger?.Log(LogLevel.Error, exc, $"An exception was thrown handling API request to [{nameof(ValidateToken)}] endpoint on [{nameof(AuthorizationController)}].");
            return StatusCodes.InternalServerError.ToActionResult();
        }
    }
}
