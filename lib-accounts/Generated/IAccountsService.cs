using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Services;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Accounts;

/// <summary>
/// Service interface for Accounts API
/// </summary>
public interface IAccountsService : IDaprService
{
    /// <summary>
    /// ListAccounts operation
    /// </summary>
    Task<(StatusCodes, AccountListResponse?)> ListAccountsAsync(ListAccountsRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// CreateAccount operation
    /// </summary>
    Task<(StatusCodes, AccountResponse?)> CreateAccountAsync(CreateAccountRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// GetAccount operation
    /// </summary>
    Task<(StatusCodes, AccountResponse?)> GetAccountAsync(GetAccountRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// UpdateAccount operation
    /// </summary>
    Task<(StatusCodes, AccountResponse?)> UpdateAccountAsync(UpdateAccountRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// DeleteAccount operation
    /// </summary>
    Task<(StatusCodes, object?)> DeleteAccountAsync(DeleteAccountRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// GetAccountByEmail operation
    /// </summary>
    Task<(StatusCodes, AccountResponse?)> GetAccountByEmailAsync(GetAccountByEmailRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// GetAuthMethods operation
    /// </summary>
    Task<(StatusCodes, AuthMethodsResponse?)> GetAuthMethodsAsync(GetAuthMethodsRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// AddAuthMethod operation
    /// </summary>
    Task<(StatusCodes, AuthMethodResponse?)> AddAuthMethodAsync(AddAuthMethodRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// RemoveAuthMethod operation
    /// </summary>
    Task<(StatusCodes, object?)> RemoveAuthMethodAsync(RemoveAuthMethodRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// GetAccountByProvider operation
    /// </summary>
    Task<(StatusCodes, AccountResponse?)> GetAccountByProviderAsync(GetAccountByProviderRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// UpdateProfile operation
    /// </summary>
    Task<(StatusCodes, AccountResponse?)> UpdateProfileAsync(UpdateProfileRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// UpdatePasswordHash operation
    /// </summary>
    Task<(StatusCodes, object?)> UpdatePasswordHashAsync(UpdatePasswordRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// UpdateVerificationStatus operation
    /// </summary>
    Task<(StatusCodes, object?)> UpdateVerificationStatusAsync(UpdateVerificationRequest body, CancellationToken cancellationToken = default(CancellationToken));

}
