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

            var response = new CreateAccountResponse(accountData.Guid, accountData.SecurityToken, accountData.CreatedAt)
            {
                Username = accountData.Username,
                Email = accountData.Email,
                EmailVerified = accountData.EmailVerified,
                SecurityToken = accountData.SecurityToken,
                TwoFactorEnabled = accountData.TwoFactorEnabled,
                LockoutEnd = accountData.LockoutEnd,
                LastLoginAt = accountData.LastLoginAt,
                CreatedAt = accountData.CreatedAt,
                UpdatedAt = accountData.UpdatedAt,
                RemovedAt = accountData.RemovedAt,
                AppClaims = accountData.AppClaims,
                IdentityClaims = accountData.IdentityClaims,
                ProfileClaims = accountData.ProfileClaims,
                RoleClaims = accountData.RoleClaims,
                ScopeClaims = accountData.ScopeClaims
            };

            if (request.RequestIDs != null && request.RequestIDs.Count > 0)
                response.RequestIDs = request.RequestIDs;

            return Ok(response);
        }
        catch (Exception exc)
        {
            Program.Logger?.Log(LogLevel.Error, exc, $"An exception was thrown handling API request to [{nameof(GetAccount)}] endpoint on [{nameof(AccountController)}].");
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

            var response = new GetAccountResponse(accountData.Guid, accountData.SecurityToken, accountData.CreatedAt)
            {
                Username = accountData.Username,
                Email = accountData.Email,
                EmailVerified = accountData.EmailVerified,
                SecurityToken = accountData.SecurityToken,
                TwoFactorEnabled = accountData.TwoFactorEnabled,
                LockoutEnd = accountData.LockoutEnd,
                LastLoginAt = accountData.LastLoginAt,
                CreatedAt = accountData.CreatedAt,
                UpdatedAt = accountData.UpdatedAt,
                RemovedAt = accountData.RemovedAt,
                AppClaims = accountData.AppClaims,
                IdentityClaims = accountData.IdentityClaims,
                ProfileClaims = accountData.ProfileClaims,
                RoleClaims = accountData.RoleClaims,
                ScopeClaims = accountData.ScopeClaims
            };

            if (request.RequestIDs != null && request.RequestIDs.Count > 0)
                response.RequestIDs = request.RequestIDs;

            return Ok(response);
        }
        catch (Exception exc)
        {
            Program.Logger?.Log(LogLevel.Error, exc, $"An exception was thrown handling API request to [{nameof(GetAccount)}] endpoint on [{nameof(AccountController)}].");
            return StatusCode(500);
        }
    }
}
