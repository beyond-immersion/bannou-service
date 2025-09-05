using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Controllers;
using BeyondImmersion.BannouService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Net.Mime;
using Generated = BeyondImmersion.BannouService.Controllers.Generated;

namespace BeyondImmersion.BannouService.Accounts;

/// <summary>
/// Concrete accounts controller that bridges the generated abstract controller
/// with the existing IAccountService interface and Bannou's service architecture.
/// </summary>
[DaprController(typeof(IAccountService))]
[Consumes(MediaTypeNames.Application.Json)]
[Produces(MediaTypeNames.Application.Json)]
public class AccountsGeneratedController : Generated.AccountsControllerControllerBase, IDaprController
{
    protected IAccountService Service { get; }
    protected ILogger Logger { get; }

    public AccountsGeneratedController(IAccountService service, ILogger<AccountsGeneratedController> logger)
    {
        Service = service;
        Logger = logger;
    }

    /// <summary>
    /// Create new user account - bridges generated controller to existing service.
    /// </summary>
    public override async Task<ActionResult<Generated.CreateAccountResponse>> CreateAccount(
        Generated.CreateAccountRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Validate that at least one identity method is provided
            if (string.IsNullOrWhiteSpace(body.Username) &&
                string.IsNullOrWhiteSpace(body.Email) &&
                string.IsNullOrWhiteSpace(body.Steam_id) &&
                string.IsNullOrWhiteSpace(body.Google_id))
                return BadRequest("At least one identity method must be provided (username, email, or OAuth)");

            // Convert generated request DTO to service call parameters
            ServiceResponse<AccountData?> serviceResult = await Service.CreateAccount(
                email: body.Email,
                emailVerified: body.Email_verified,
                twoFactorEnabled: body.Two_factor_enabled,
                region: body.Region,
                username: body.Username,
                password: body.Password,
                steamID: body.Steam_id,
                steamToken: body.Steam_token,
                googleID: body.Google_id,
                googleToken: body.Google_token,
                roleClaims: body.Role_claims?.ToHashSet(),
                appClaims: body.App_claims?.ToHashSet(),
                scopeClaims: body.Scope_claims?.ToHashSet(),
                identityClaims: body.Identity_claims?.ToHashSet(),
                profileClaims: body.Profile_claims?.ToHashSet());

            // Handle service response and convert to generated response DTO
            switch (serviceResult.StatusCode)
            {
                case StatusCodes.OK:
                    if (serviceResult.Value == null)
                        return StatusCode(500, "Service returned OK but null account data");

                    return Ok(MapAccountDataToCreateResponse(serviceResult.Value));

                case StatusCodes.BadRequest:
                    return BadRequest("Invalid account data provided");

                case StatusCodes.Conflict:
                    return Conflict("Account with this identity already exists");

                default:
                    return StatusCode(500, "Internal server error during account creation");
            }
        }
        catch (Exception exc)
        {
            Logger.LogError(exc, "Exception occurred in {Method} on {Controller}",
                nameof(CreateAccount), nameof(AccountsGeneratedController));
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Retrieve user account - bridges generated controller to existing service.
    /// </summary>
    public override async Task<ActionResult<Generated.GetAccountResponse>> GetAccount(
        Generated.GetAccountRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Validate that at least one identifier is provided
            if (body.Id == 0 &&
                string.IsNullOrWhiteSpace(body.Username) &&
                string.IsNullOrWhiteSpace(body.Email) &&
                string.IsNullOrWhiteSpace(body.Steam_id) &&
                string.IsNullOrWhiteSpace(body.Google_id) &&
                string.IsNullOrWhiteSpace(body.Identity_claim))
                return BadRequest("At least one identifier must be provided");

            // Convert generated request DTO to service call parameters
            ServiceResponse<AccountData?> serviceResult = await Service.GetAccount(
                includeClaims: body.Include_claims,
                id: body.Id == 0 ? null : body.Id,
                username: body.Username,
                email: body.Email,
                steamID: body.Steam_id,
                googleID: body.Google_id,
                identityClaim: body.Identity_claim);

            // Handle service response and convert to generated response DTO
            switch (serviceResult.StatusCode)
            {
                case StatusCodes.OK:
                    if (serviceResult.Value == null)
                        return NotFound("Account not found");

                    return Ok(MapAccountDataToGetResponse(serviceResult.Value, body.Include_claims));

                case StatusCodes.BadRequest:
                    return BadRequest("Invalid account lookup parameters");

                case StatusCodes.NotFound:
                    return NotFound("Account not found");

                default:
                    return StatusCode(500, "Internal server error during account retrieval");
            }
        }
        catch (Exception exc)
        {
            Logger.LogError(exc, "Exception occurred in {Method} on {Controller}",
                nameof(GetAccount), nameof(AccountsGeneratedController));
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Maps AccountData to CreateAccountResponse DTO.
    /// </summary>
    private Generated.CreateAccountResponse MapAccountDataToCreateResponse(AccountData accountData)
    {
        return new Generated.CreateAccountResponse
        {
            Id = accountData.ID,
            Username = accountData.Username,
            Email = accountData.Email,
            Email_verified = accountData.EmailVerified,
            Region = accountData.Region,
            Security_token = accountData.SecurityToken,
            Two_factor_enabled = accountData.TwoFactorEnabled,
            Lockout_end = accountData.LockoutEnd,
            Last_login_at = accountData.LastLoginAt,
            Created_at = accountData.CreatedAt,
            Updated_at = accountData.UpdatedAt,
            Deleted_at = accountData.DeletedAt,
            App_claims = accountData.AppClaims?.ToList(),
            Identity_claims = accountData.IdentityClaims?.ToList()
        };
    }

    /// <summary>
    /// Maps AccountData to GetAccountResponse DTO with conditional claims.
    /// </summary>
    private Generated.GetAccountResponse MapAccountDataToGetResponse(AccountData accountData, bool includeClaims)
    {
        var response = new Generated.GetAccountResponse
        {
            // Base properties from CreateAccountResponse
            Id = accountData.ID,
            Username = accountData.Username,
            Email = accountData.Email,
            Email_verified = accountData.EmailVerified,
            Region = accountData.Region,
            Security_token = accountData.SecurityToken,
            Two_factor_enabled = accountData.TwoFactorEnabled,
            Lockout_end = accountData.LockoutEnd,
            Last_login_at = accountData.LastLoginAt,
            Created_at = accountData.CreatedAt,
            Updated_at = accountData.UpdatedAt,
            Deleted_at = accountData.DeletedAt,
            App_claims = accountData.AppClaims?.ToList(),
            Identity_claims = accountData.IdentityClaims?.ToList()
        };

        // Add additional claims if requested
        if (includeClaims)
        {
            response.Role_claims = accountData.RoleClaims?.ToList();
            response.Scope_claims = accountData.ScopeClaims?.ToList();
            response.Profile_claims = accountData.ProfileClaims?.ToList();
        }

        return response;
    }
}
