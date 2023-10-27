using Microsoft.AspNetCore.Mvc;
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
                return new BadRequestResult();

            IAccountService.AccountData? accountData = await Service.CreateAccount(
                request.Username, request.Email, request.EmailVerified, request.TwoFactorEnabled,
                request.RoleClaims, request.AppClaims, request.ScopeClaims, request.IdentityClaims, request.ProfileClaims);

            if (accountData == null)
                return new NotFoundResult();

            var response = new CreateAccountResponse(accountData.GUID, accountData.SecurityToken, accountData.CreatedAt)
            {
                Username = accountData.Username,
                Email = accountData.Email,
                EmailVerified = accountData.EmailVerified,
                SecretSalt = accountData.SecretSalt,
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

            return new OkObjectResult(response);
        }
        catch (Exception exc)
        {
            Program.Logger?.Log(LogLevel.Error, exc, $"An exception was thrown handling API request to [{nameof(GetAccount)}] endpoint on [{nameof(AccountController)}].");
            return new StatusCodeResult(500);
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
                return new BadRequestResult();

            IAccountService.AccountData? accountData = await Service.GetAccount(false, request.GUID, request.Username, request.Email, request.IdentityClaim);
            if (accountData == null)
                return new NotFoundResult();

            var response = new GetAccountResponse(accountData.GUID, accountData.SecurityToken, accountData.CreatedAt)
            {
                Username = accountData.Username,
                Email = accountData.Email,
                EmailVerified = accountData.EmailVerified,
                SecretSalt = accountData.SecretSalt,
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

            return new OkObjectResult(response);
        }
        catch (Exception exc)
        {
            Program.Logger?.Log(LogLevel.Error, exc, $"An exception was thrown handling API request to [{nameof(GetAccount)}] endpoint on [{nameof(AccountController)}].");
            return new StatusCodeResult(500);
        }
    }
}
