using BeyondImmersion.BannouService;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Auth;

/// <summary>
/// Service interface for Auth API - generated from controller
/// </summary>
public interface IAuthService
{
        /// <summary>
        /// Register operation
        /// </summary>
        Task<(StatusCodes, RegisterResponse?)> RegisterAsync(RegisterRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// LoginWithCredentialsGet operation
        /// </summary>
        Task<(StatusCodes, LoginResponse?)> LoginWithCredentialsGetAsync(string username, string password, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// LoginWithCredentialsPost operation
        /// </summary>
        Task<(StatusCodes, LoginResponse?)> LoginWithCredentialsPostAsync(string username, string password, LoginRequest? body = null, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// LoginWithTokenGet operation
        /// </summary>
        Task<(StatusCodes, LoginResponse?)> LoginWithTokenGetAsync(string token, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// LoginWithTokenPost operation
        /// </summary>
        Task<(StatusCodes, LoginResponse?)> LoginWithTokenPostAsync(string token, LoginRequest? body = null, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// ValidateToken operation
        /// </summary>
        Task<(StatusCodes, ValidateTokenResponse?)> ValidateTokenAsync(ValidateTokenRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// GetOAuthProviders operation
        /// </summary>
        Task<(StatusCodes, OAuthProvidersResponse?)> GetOAuthProvidersAsync(CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// HandleOAuthCallback operation
        /// </summary>
        Task<(StatusCodes, LoginResponse?)> HandleOAuthCallbackAsync(Provider provider, OAuthCallbackRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// UpdateRoutingPreference operation
        /// </summary>
        Task<(StatusCodes, RoutingPreferenceResponse?)> UpdateRoutingPreferenceAsync(RoutingPreferenceRequest body, CancellationToken cancellationToken = default(CancellationToken));

}
