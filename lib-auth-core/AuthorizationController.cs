using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;
using BeyondImmersion.BannouService.Controllers;
using BeyondImmersion.BannouService.Attributes;
using Microsoft.Extensions.Logging;
using BeyondImmersion.BannouService.Authorization.Messages;
using System.Net;

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

            (HttpStatusCode, IAuthorizationService.LoginResult?) registerResult = await Service.Register(request.Username, request.Password, request.Email);
            if (registerResult.Item1 != HttpStatusCode.OK)
            {
                if (registerResult.Item1 == HttpStatusCode.InternalServerError)
                    return StatusCodes.ServerError.ToActionResult();

                return StatusCodes.Unauthorized.ToActionResult();
            }

            if (string.IsNullOrWhiteSpace(registerResult.Item2?.AccessToken))
                return StatusCodes.Unauthorized.ToActionResult();

            var response = request.CreateResponse();
            response.AccessToken = registerResult.Item2.AccessToken;
            response.RefreshToken = registerResult.Item2.RefreshToken;
            return StatusCodes.Ok.ToActionResult(response);
        }
        catch (Exception exc)
        {
            Program.Logger?.Log(LogLevel.Error, exc, $"An exception was thrown handling API request to [{nameof(Register)}] endpoint on [{nameof(AuthorizationController)}].");
            return StatusCodes.ServerError.ToActionResult();
        }
    }

    /// <summary>
    /// Returns forbidden if no user found, to avoid leaking to avoid even leaking usernames.
    /// </summary>
    [HttpGet]
    [HttpPost]
    [DaprRoute("login/credentials")]
    public async Task<IActionResult> Login([FromHeader(Name = "username")] string username, [FromHeader(Name = "password")] string password)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                return StatusCodes.BadRequest.ToActionResult();

            (HttpStatusCode, IAuthorizationService.LoginResult?) loginResult = await Service.Login(username, password);
            if (loginResult.Item1 != HttpStatusCode.OK)
            {
                if (loginResult.Item1 == HttpStatusCode.InternalServerError)
                    return StatusCodes.ServerError.ToActionResult();

                return StatusCodes.Unauthorized.ToActionResult();
            }

            if (string.IsNullOrWhiteSpace(loginResult.Item2?.AccessToken))
                return StatusCodes.Unauthorized.ToActionResult();

            var response = new LoginResponse()
            {
                AccessToken = loginResult.Item2.AccessToken,
                RefreshToken = loginResult.Item2.RefreshToken
            };

            return StatusCodes.Ok.ToActionResult(response);
        }
        catch (Exception exc)
        {
            Program.Logger?.Log(LogLevel.Error, exc, $"An exception was thrown handling API request to [{nameof(Login)}] endpoint on [{nameof(AuthorizationController)}].");
            return StatusCodes.ServerError.ToActionResult();
        }
    }

    /// <summary>
    /// Returns forbidden if no user found, to avoid leaking to avoid even leaking usernames.
    /// </summary>
    [HttpGet]
    [HttpPost]
    [DaprRoute("login/token")]
    public async Task<IActionResult> Login([FromHeader(Name = "token")] string token)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(token))
                return StatusCodes.BadRequest.ToActionResult();

            (HttpStatusCode, IAuthorizationService.LoginResult?) loginResult = await Service.Login(token);
            if (loginResult.Item1 != HttpStatusCode.OK)
            {
                if (loginResult.Item1 == HttpStatusCode.InternalServerError)
                    return StatusCodes.ServerError.ToActionResult();

                return StatusCodes.Unauthorized.ToActionResult();
            }

            if (string.IsNullOrWhiteSpace(loginResult.Item2?.AccessToken))
                return StatusCodes.Unauthorized.ToActionResult();

            var response = new LoginResponse()
            {
                AccessToken = loginResult.Item2.AccessToken,
                RefreshToken = loginResult.Item2.RefreshToken
            };
            return StatusCodes.Ok.ToActionResult(response);
        }
        catch (Exception exc)
        {
            Program.Logger?.Log(LogLevel.Error, exc, $"An exception was thrown handling API request to [{nameof(Login)}] endpoint on [{nameof(AuthorizationController)}].");
            return StatusCodes.ServerError.ToActionResult();
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
            return StatusCodes.Ok.ToActionResult(response);
        }
        catch (Exception exc)
        {
            Program.Logger?.Log(LogLevel.Error, exc, $"An exception was thrown handling API request to [{nameof(ValidateToken)}] endpoint on [{nameof(AuthorizationController)}].");
            return StatusCodes.ServerError.ToActionResult();
        }
    }
}
