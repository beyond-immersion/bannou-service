using Microsoft.AspNetCore.Mvc;
using System.Net.Mime;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Accounts.Messages;
using Microsoft.Extensions.Logging;

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
            Program.Logger.Log(LogLevel.Warning, $"User ID in account/create request: {request.RequestIDs?.GetValueOrDefault("USER_ID")}");

            // no identities provided
            if (string.IsNullOrWhiteSpace(request.Username) &&
                string.IsNullOrWhiteSpace(request.Email) &&
                (request.IdentityClaims == null ||
                request.IdentityClaims.Count == 0))
                return BadRequest();

            IAccountService.AccountData? accountData = await Service.CreateAccount(
                request.Email, request.EmailVerified, request.TwoFactorEnabled, request.Username, request.Password,
                request.SteamID, request.SteamToken, request.GoogleID, request.GoogleToken,
                request.RoleClaims, request.AppClaims, request.ScopeClaims, request.IdentityClaims, request.ProfileClaims);

            if (accountData == null)
                return StatusCode(500);

            var response = request.CreateResponse();
            response.ID = accountData.ID;
            response.Username = accountData.Username;
            response.Email = accountData.Email;
            response.EmailVerified = accountData.EmailVerified;
            response.SecurityToken = accountData.SecurityToken;
            response.TwoFactorEnabled = accountData.TwoFactorEnabled;
            response.LockoutEnd = accountData.LockoutEnd;
            response.LastLoginAt = accountData.LastLoginAt;
            response.CreatedAt = accountData.CreatedAt;
            response.UpdatedAt = accountData.UpdatedAt;
            response.RemovedAt = accountData.RemovedAt;
            response.AppClaims = accountData.AppClaims;
            response.IdentityClaims = accountData.IdentityClaims;
            response.ProfileClaims = accountData.ProfileClaims;
            response.RoleClaims = accountData.RoleClaims;
            response.ScopeClaims = accountData.ScopeClaims;

            Program.Logger.Log(LogLevel.Warning, $"User ID in account/create response: {response.RequestIDs?.GetValueOrDefault("USER_ID")}");
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
            if (string.IsNullOrWhiteSpace(request.GUID) &&
                string.IsNullOrWhiteSpace(request.Username) &&
                string.IsNullOrWhiteSpace(request.Email) &&
                string.IsNullOrWhiteSpace(request.IdentityClaim))
                return BadRequest();

            IAccountService.AccountData? accountData = await Service.GetAccount(false, request.GUID, request.Username, request.Email, request.IdentityClaim);
            if (accountData == null)
                return NotFound();

            var response = request.CreateResponse();
            response.ID = accountData.ID;
            response.Username = accountData.Username;
            response.Email = accountData.Email;
            response.EmailVerified = accountData.EmailVerified;
            response.SecurityToken = accountData.SecurityToken;
            response.TwoFactorEnabled = accountData.TwoFactorEnabled;
            response.LockoutEnd = accountData.LockoutEnd;
            response.LastLoginAt = accountData.LastLoginAt;
            response.CreatedAt = accountData.CreatedAt;
            response.UpdatedAt = accountData.UpdatedAt;
            response.RemovedAt = accountData.RemovedAt;
            response.AppClaims = accountData.AppClaims;
            response.IdentityClaims = accountData.IdentityClaims;
            response.ProfileClaims = accountData.ProfileClaims;
            response.RoleClaims = accountData.RoleClaims;
            response.ScopeClaims = accountData.ScopeClaims;
            return Ok(response);
        }
        catch (Exception exc)
        {
            Program.Logger?.Log(LogLevel.Error, exc, $"An exception was thrown handling API request to [{nameof(GetAccount)}] endpoint on [{nameof(AccountController)}].");
            return StatusCode(500);
        }
    }
}
