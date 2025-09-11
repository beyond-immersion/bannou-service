using BeyondImmersion.BannouService;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Accounts
{
    /// <summary>
    /// Service interface for Accounts API - generated from controller
    /// </summary>
    public interface IAccountsService
    {
        /// <summary>
        /// ListAccounts operation  
        /// </summary>
        Task<(StatusCodes, AccountListResponse?)> ListAccountsAsync(/* TODO: Add parameters from schema */);

        /// <summary>
        /// CreateAccount operation  
        /// </summary>
        Task<(StatusCodes, AccountResponse?)> CreateAccountAsync(/* TODO: Add parameters from schema */);

        /// <summary>
        /// GetAccount operation  
        /// </summary>
        Task<(StatusCodes, AccountResponse?)> GetAccountAsync(/* TODO: Add parameters from schema */);

        /// <summary>
        /// UpdateAccount operation  
        /// </summary>
        Task<(StatusCodes, AccountResponse?)> UpdateAccountAsync(/* TODO: Add parameters from schema */);

        /// <summary>
        /// GetAccountByEmail operation  
        /// </summary>
        Task<(StatusCodes, AccountResponse?)> GetAccountByEmailAsync(/* TODO: Add parameters from schema */);

        /// <summary>
        /// GetAuthMethods operation  
        /// </summary>
        Task<(StatusCodes, AuthMethodsResponse?)> GetAuthMethodsAsync(/* TODO: Add parameters from schema */);

        /// <summary>
        /// AddAuthMethod operation  
        /// </summary>
        Task<(StatusCodes, AuthMethodResponse?)> AddAuthMethodAsync(/* TODO: Add parameters from schema */);

        /// <summary>
        /// GetAccountByProvider operation  
        /// </summary>
        Task<(StatusCodes, AccountResponse?)> GetAccountByProviderAsync(/* TODO: Add parameters from schema */);

        /// <summary>
        /// UpdateProfile operation  
        /// </summary>
        Task<(StatusCodes, AccountResponse?)> UpdateProfileAsync(/* TODO: Add parameters from schema */);

    }
}
