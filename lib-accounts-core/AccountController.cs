using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Accounts.Messages;
using Microsoft.Extensions.Logging;
using System.Net;

namespace BeyondImmersion.BannouService.Accounts;

/// <summary>
/// Auth APIs- backed by the Account service.
/// </summary>
[DaprController(typeof(IAccountService))]
[Consumes(MediaTypeNames.Application.Json)]
[Produces(MediaTypeNames.Application.Json)]
public class AccountController : Controllers.BaseDaprController
{
    protected IAccountService Service { get; }
    protected ILogger Logger { get; }

    public AccountController(IAccountService service, ILogger<AccountController> logger)
    {
        Service = service;
        Logger = logger;
    }

    [HttpPost]
    [DaprRoute("create")]
    public async Task<IActionResult> CreateAccount([FromBody] CreateAccountRequest request)
    {
        try
        {
            // no identities provided
            if (string.IsNullOrWhiteSpace(request.Username) &&
                string.IsNullOrWhiteSpace(request.Email) &&
                (request.IdentityClaims == null ||
                request.IdentityClaims.Count == 0))
                return BadRequest();

            (HttpStatusCode, IAccountService.AccountData?) accountData = await Service.CreateAccount(
                request.Email, request.EmailVerified, request.TwoFactorEnabled, request.Region,
                request.Username, request.Password, request.SteamID, request.SteamToken, request.GoogleID, request.GoogleToken,
                request.RoleClaims, request.AppClaims, request.ScopeClaims, request.IdentityClaims, request.ProfileClaims);

            switch (accountData.Item1)
            {
                case HttpStatusCode.OK:
                    if (accountData.Item2 == null)
                        return StatusCode(500);
                    break;
                case HttpStatusCode.BadRequest:
                    return BadRequest();
                case HttpStatusCode.Conflict:
                    return Conflict();
                default:
                    return StatusCode(500);
            }

            var userAccount = accountData.Item2;
            var response = request.CreateResponse();
            response.ID = userAccount.ID;
            response.Username = userAccount.Username;
            response.Email = userAccount.Email;
            response.EmailVerified = userAccount.EmailVerified;
            response.Region = userAccount.Region;
            response.SecurityToken = userAccount.SecurityToken;
            response.TwoFactorEnabled = userAccount.TwoFactorEnabled;
            response.LockoutEnd = userAccount.LockoutEnd;
            response.LastLoginAt = userAccount.LastLoginAt;
            response.CreatedAt = userAccount.CreatedAt;
            response.UpdatedAt = userAccount.UpdatedAt;
            response.RemovedAt = userAccount.RemovedAt;
            response.AppClaims = userAccount.AppClaims;
            response.IdentityClaims = userAccount.IdentityClaims;
            response.ProfileClaims = userAccount.ProfileClaims;
            response.RoleClaims = userAccount.RoleClaims;
            response.ScopeClaims = userAccount.ScopeClaims;
            return Ok(response);
        }
        catch (Exception exc)
        {
            Program.Logger?.Log(LogLevel.Error, exc, $"An exception was thrown handling API request to [{nameof(CreateAccount)}] endpoint on [{nameof(AccountController)}].");
            return StatusCode(500);
        }
    }

    [HttpPost]
    [DaprRoute("get")]
    public async Task<IActionResult> GetAccount([FromBody] GetAccountRequest request)
    {
        try
        {
            if (request.ID == null &&
                string.IsNullOrWhiteSpace(request.Username) &&
                string.IsNullOrWhiteSpace(request.Email) &&
                string.IsNullOrWhiteSpace(request.IdentityClaim) &&
                string.IsNullOrWhiteSpace(request.GoogleID) &&
                string.IsNullOrWhiteSpace(request.SteamID))
                return BadRequest();

            (HttpStatusCode, IAccountService.AccountData?) accountData = await Service.GetAccount(
                includeClaims: request.IncludeClaims, id: request.ID, username: request.Username, email: request.Email,
                steamID: request.SteamID, googleID: request.GoogleID, identityClaim: request.IdentityClaim);

            switch (accountData.Item1)
            {
                case HttpStatusCode.OK:
                    if (accountData.Item2 == null)
                        return NotFound();
                    break;
                case HttpStatusCode.BadRequest:
                    return BadRequest();
                case HttpStatusCode.NotFound:
                    return NotFound();
                default:
                    return StatusCode(500);
            }

            var userAccount = accountData.Item2;
            var response = request.CreateResponse();
            response.ID = userAccount.ID;
            response.Username = userAccount.Username;
            response.Email = userAccount.Email;
            response.EmailVerified = userAccount.EmailVerified;
            response.Region = userAccount.Region;
            response.SecurityToken = userAccount.SecurityToken;
            response.TwoFactorEnabled = userAccount.TwoFactorEnabled;
            response.LockoutEnd = userAccount.LockoutEnd;
            response.LastLoginAt = userAccount.LastLoginAt;
            response.CreatedAt = userAccount.CreatedAt;
            response.UpdatedAt = userAccount.UpdatedAt;
            response.RemovedAt = userAccount.RemovedAt;
            response.AppClaims = userAccount.AppClaims;
            response.IdentityClaims = userAccount.IdentityClaims;
            response.ProfileClaims = userAccount.ProfileClaims;
            response.RoleClaims = userAccount.RoleClaims;
            response.ScopeClaims = userAccount.ScopeClaims;
            return Ok(response);
        }
        catch (Exception exc)
        {
            Program.Logger?.Log(LogLevel.Error, exc, $"An exception was thrown handling API request to [{nameof(GetAccount)}] endpoint on [{nameof(AccountController)}].");
            return StatusCode(500);
        }
    }

    [HttpPost]
    [DaprRoute("update")]
    public async Task<IActionResult> UpdateAccount([FromBody] UpdateAccountRequest request)
    {
        try
        {
            if (request.ID < 0)
                return BadRequest();

            (HttpStatusCode, IAccountService.AccountData?) accountData = await Service.UpdateAccount(
                id: request.ID, email: request.Email, emailVerified: request.EmailVerified, twoFactorEnabled: request.TwoFactorEnabled, region: request.Region,
                username: request.Username, password: request.Password, steamID: request.SteamID, steamToken: request.SteamToken, googleID: request.GoogleID, googleToken: request.GoogleToken,
                roleClaims: request.RoleClaims, appClaims: request.AppClaims, scopeClaims: request.ScopeClaims, identityClaims: request.IdentityClaims, profileClaims: request.ProfileClaims);

            switch (accountData.Item1)
            {
                case HttpStatusCode.OK:
                    if (accountData.Item2 == null)
                        return NotFound();
                    break;
                case HttpStatusCode.BadRequest:
                    return BadRequest();
                case HttpStatusCode.Conflict:
                    return Conflict();
                case HttpStatusCode.NotFound:
                    return NotFound();
                default:
                    return StatusCode(500);
            }

            var userAccount = accountData.Item2;
            var response = request.CreateResponse();
            response.ID = userAccount.ID;
            response.Username = userAccount.Username;
            response.Email = userAccount.Email;
            response.EmailVerified = userAccount.EmailVerified;
            response.Region = userAccount.Region;
            response.SecurityToken = userAccount.SecurityToken;
            response.TwoFactorEnabled = userAccount.TwoFactorEnabled;
            response.LockoutEnd = userAccount.LockoutEnd;
            response.LastLoginAt = userAccount.LastLoginAt;
            response.CreatedAt = userAccount.CreatedAt;
            response.UpdatedAt = userAccount.UpdatedAt;
            response.RemovedAt = userAccount.RemovedAt;
            response.AppClaims = userAccount.AppClaims;
            response.IdentityClaims = userAccount.IdentityClaims;
            response.ProfileClaims = userAccount.ProfileClaims;
            response.RoleClaims = userAccount.RoleClaims;
            response.ScopeClaims = userAccount.ScopeClaims;
            return Ok(response);
        }
        catch (Exception exc)
        {
            Program.Logger?.Log(LogLevel.Error, exc, $"An exception was thrown handling API request to [{nameof(GetAccount)}] endpoint on [{nameof(AccountController)}].");
            return StatusCode(500);
        }
    }
}
