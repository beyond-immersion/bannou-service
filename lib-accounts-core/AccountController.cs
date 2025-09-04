using BeyondImmersion.BannouService.Accounts.Messages;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Net.Mime;

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

            ServiceResponse<AccountData?> accountData = await Service.CreateAccount(
                request.Email, request.EmailVerified, request.TwoFactorEnabled, request.Region,
                request.Username, request.Password, request.SteamID, request.SteamToken, request.GoogleID, request.GoogleToken,
                request.RoleClaims, request.AppClaims, request.ScopeClaims, request.IdentityClaims, request.ProfileClaims);

            switch (accountData.StatusCode)
            {
                case StatusCodes.OK:
                    if (accountData.Value == null)
                        return StatusCode(500);
                    break;
                case StatusCodes.BadRequest:
                    return BadRequest();
                case StatusCodes.Conflict:
                    return Conflict();
                default:
                    return StatusCode(500);
            }

            var userAccount = accountData.Value;
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
            response.DeletedAt = userAccount.DeletedAt;
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

            ServiceResponse<AccountData?> accountData = await Service.GetAccount(
                includeClaims: request.IncludeClaims, id: request.ID, username: request.Username, email: request.Email,
                steamID: request.SteamID, googleID: request.GoogleID, identityClaim: request.IdentityClaim);

            switch (accountData.StatusCode)
            {
                case StatusCodes.OK:
                    if (accountData.Value == null)
                        return NotFound();
                    break;
                case StatusCodes.BadRequest:
                    return BadRequest();
                case StatusCodes.NotFound:
                    return NotFound();
                default:
                    return StatusCode(500);
            }

            var userAccount = accountData.Value;
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
            response.DeletedAt = userAccount.DeletedAt;
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

            ServiceResponse<AccountData?> accountData = await Service.UpdateAccount(
                id: request.ID, email: request.Email, emailVerified: request.EmailVerified, twoFactorEnabled: request.TwoFactorEnabled, region: request.Region,
                username: request.Username, password: request.Password, steamID: request.SteamID, steamToken: request.SteamToken, googleID: request.GoogleID, googleToken: request.GoogleToken,
                roleClaims: request.RoleClaims, appClaims: request.AppClaims, scopeClaims: request.ScopeClaims, identityClaims: request.IdentityClaims, profileClaims: request.ProfileClaims);

            switch (accountData.StatusCode)
            {
                case StatusCodes.OK:
                    if (accountData.Value == null)
                        return NotFound();
                    break;
                case StatusCodes.BadRequest:
                    return BadRequest();
                case StatusCodes.Conflict:
                    return Conflict();
                case StatusCodes.NotFound:
                    return NotFound();
                default:
                    return StatusCode(500);
            }

            var userAccount = accountData.Value;
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
            response.DeletedAt = userAccount.DeletedAt;
            response.AppClaims = userAccount.AppClaims;
            response.IdentityClaims = userAccount.IdentityClaims;
            response.ProfileClaims = userAccount.ProfileClaims;
            response.RoleClaims = userAccount.RoleClaims;
            response.ScopeClaims = userAccount.ScopeClaims;

            return Ok(response);
        }
        catch (Exception exc)
        {
            Program.Logger?.Log(LogLevel.Error, exc, $"An exception was thrown handling API request to [{nameof(UpdateAccount)}] endpoint on [{nameof(AccountController)}].");
            return StatusCode(500);
        }
    }

    [HttpPost]
    [DaprRoute("delete")]
    public async Task<IActionResult> DeleteAccount([FromBody] DeleteAccountRequest request)
    {
        try
        {
            if (request.ID < 0)
                return BadRequest();

            ServiceResponse<DateTime?> accountData = await Service.DeleteAccount(id: request.ID);

            switch (accountData.StatusCode)
            {
                case StatusCodes.OK:
                    if (accountData.Value == null)
                        return NotFound();
                    break;
                case StatusCodes.BadRequest:
                    return BadRequest();
                case StatusCodes.Conflict:
                    return Conflict();
                case StatusCodes.NotFound:
                    return NotFound();
                default:
                    return StatusCode(500);
            }

            var response = request.CreateResponse();
            response.DeletedAt = accountData.Value;

            return Ok(response);
        }
        catch (Exception exc)
        {
            Program.Logger?.Log(LogLevel.Error, exc, $"An exception was thrown handling API request to [{nameof(DeleteAccount)}] endpoint on [{nameof(AccountController)}].");
            return StatusCode(500);
        }
    }
}
