using BeyondImmersion.BannouService;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Auth
{
    /// <summary>
    /// Service interface for Auth API - generated from controller
    /// </summary>
    public interface IAuthService
    {
        /// <summary>
        /// Register operation  
        /// </summary>
        Task<(StatusCodes, RegisterResponse?)> RegisterAsync(/* TODO: Add parameters from schema */);

        /// <summary>
        /// LoginWithCredentialsGet operation  
        /// </summary>
        Task<(StatusCodes, LoginResponse?)> LoginWithCredentialsGetAsync(/* TODO: Add parameters from schema */);

        /// <summary>
        /// LoginWithCredentialsPost operation  
        /// </summary>
        Task<(StatusCodes, LoginResponse?)> LoginWithCredentialsPostAsync(/* TODO: Add parameters from schema */);

        /// <summary>
        /// LoginWithTokenGet operation  
        /// </summary>
        Task<(StatusCodes, LoginResponse?)> LoginWithTokenGetAsync(/* TODO: Add parameters from schema */);

        /// <summary>
        /// LoginWithTokenPost operation  
        /// </summary>
        Task<(StatusCodes, LoginResponse?)> LoginWithTokenPostAsync(/* TODO: Add parameters from schema */);

        /// <summary>
        /// ValidateToken operation  
        /// </summary>
        Task<(StatusCodes, ValidateTokenResponse?)> ValidateTokenAsync(/* TODO: Add parameters from schema */);

    }
}
