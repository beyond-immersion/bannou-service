using BeyondImmersion.BannouService;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Accounts;

/// <summary>
/// Service interface for Accounts API - generated from controller
/// </summary>
public interface IAccountsService
{
        /// <summary>
        /// ListAccounts operation
        /// </summary>
        Task<(StatusCodes, AccountListResponse?)> ListAccountsAsync(string? email = null, string? displayName = null, Provider? provider = null, bool? verified = null, int? page = 1, int? pageSize = 20, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// CreateAccount operation
        /// </summary>
        Task<(StatusCodes, AccountResponse?)> CreateAccountAsync(CreateAccountRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// GetAccount operation
        /// </summary>
        Task<(StatusCodes, AccountResponse?)> GetAccountAsync(Guid accountId, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// UpdateAccount operation
        /// </summary>
        Task<(StatusCodes, AccountResponse?)> UpdateAccountAsync(Guid accountId, UpdateAccountRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// DeleteAccount operation
        /// </summary>
        Task<(StatusCodes, object?)> DeleteAccountAsync(Guid accountId, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// GetAccountByEmail operation
        /// </summary>
        Task<(StatusCodes, AccountResponse?)> GetAccountByEmailAsync(string email, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// GetAuthMethods operation
        /// </summary>
        Task<(StatusCodes, AuthMethodsResponse?)> GetAuthMethodsAsync(Guid accountId, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// AddAuthMethod operation
        /// </summary>
        Task<(StatusCodes, AuthMethodResponse?)> AddAuthMethodAsync(Guid accountId, AddAuthMethodRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// RemoveAuthMethod operation
        /// </summary>
        Task<(StatusCodes, object?)> RemoveAuthMethodAsync(Guid accountId, Guid methodId, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// GetAccountByProvider operation
        /// </summary>
        Task<(StatusCodes, AccountResponse?)> GetAccountByProviderAsync(Provider2 provider, string externalId, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// UpdateProfile operation
        /// </summary>
        Task<(StatusCodes, AccountResponse?)> UpdateProfileAsync(Guid accountId, UpdateProfileRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// UpdatePasswordHash operation
        /// </summary>
        Task<(StatusCodes, object?)> UpdatePasswordHashAsync(Guid accountId, UpdatePasswordRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// UpdateVerificationStatus operation
        /// </summary>
        Task<(StatusCodes, object?)> UpdateVerificationStatusAsync(Guid accountId, UpdateVerificationRequest body, CancellationToken cancellationToken = default(CancellationToken));

}
