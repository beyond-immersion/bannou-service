using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;
using Microsoft.AspNetCore.Mvc.ModelBinding;
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
                return BadRequest();

            (System.Net.HttpStatusCode, string?) registerResult = await Service.Register(request.Username, request.Password, request.Email);
            if (registerResult.Item1 != System.Net.HttpStatusCode.OK)
            {
                if (registerResult.Item1 == System.Net.HttpStatusCode.InternalServerError)
                    return StatusCode(500);

                return Forbid();
            }

            var response = new RegisterResponse()
            {
                Token = registerResult.Item2
            };
            return Ok(response);
        }
        catch (Exception exc)
        {
            Program.Logger?.Log(LogLevel.Error, exc, $"An exception was thrown handling API request to [{nameof(Register)}] endpoint on [{nameof(AuthorizationController)}].");
            return StatusCode(500);
        }
    }

    /// <summary>
    /// Returns forbidden if no user found, to avoid leaking to avoid even leaking usernames.
    /// </summary>
    [HttpPost]
    [DaprRoute("login")]
    public async Task<IActionResult> Login(
        [FromHeader(Name = "username")] string username,
        [FromHeader(Name = "password")] string password,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] LoginRequest? request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                return BadRequest();

            (System.Net.HttpStatusCode, IAuthorizationService.LoginResult?) loginResult = await Service.Login(username, password);
            if (loginResult.Item1 != System.Net.HttpStatusCode.OK)
            {
                if (loginResult.Item1 == System.Net.HttpStatusCode.InternalServerError)
                    return StatusCode(500);

                return Forbid();
            }

            if (loginResult.Item2?.AccessToken == null)
                return Forbid();

            var response = new LoginResponse()
            {
                AccessToken = loginResult.Item2.AccessToken,
                RefreshToken = loginResult.Item2.RefreshToken
            };
            return Ok(response);
        }
        catch (Exception exc)
        {
            Program.Logger?.Log(LogLevel.Error, exc, $"An exception was thrown handling API request to [{nameof(Login)}] endpoint on [{nameof(AuthorizationController)}].");
            return StatusCode(500);
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
            return Ok(response);
        }
        catch (Exception exc)
        {
            Program.Logger?.Log(LogLevel.Error, exc, $"An exception was thrown handling API request to [{nameof(ValidateToken)}] endpoint on [{nameof(AuthorizationController)}].");
            return StatusCode(500);
        }
    }
}
