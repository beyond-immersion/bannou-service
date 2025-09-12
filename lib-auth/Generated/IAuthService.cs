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
        Task<(StatusCodes, RegisterResponse?)> RegisterAsync(RegisterRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Login with username and password (GET)
        /// </summary>
        /// <remarks>
        /// Authenticate user with username and password provided via headers.
        /// <br/>Returns JWT access token and refresh token on successful authentication.
        /// <br/>Uses GET method for simple credential-based login.
        /// </remarks>
        /// <param name="username">Username for authentication</param>
        /// <param name="password">Password for authentication</param>
        /// <returns>Login successful</returns>
        public abstract Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<LoginResponse>> LoginWithCredentialsGet(string username, string password, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Login with username and password (POST)
        /// </summary>
        /// <remarks>
        /// Authenticate user with username and password provided via headers.
        /// <br/>Returns JWT access token and refresh token on successful authentication.
        /// <br/>Uses POST method for credential-based login with potential request body.
        /// </remarks>
        /// <param name="username">Username for authentication</param>
        /// <param name="password">Password for authentication</param>
        /// <param name="body">Optional login request body (currently unused)</param>
        /// <returns>Login successful</returns>
        public abstract Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<LoginResponse>> LoginWithCredentialsPost(string username, string password, LoginRequest? body = null, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Login with refresh token (GET)
        /// </summary>
        /// <remarks>
        /// Authenticate user with a refresh token provided via header.
        /// <br/>Returns new JWT access token and potentially new refresh token.
        /// <br/>Used for token refresh flows without requiring password re-entry.
        /// </remarks>
        /// <param name="token">Refresh token for authentication</param>
        /// <returns>Token refresh successful</returns>
        public abstract Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<LoginResponse>> LoginWithTokenGet(string token, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Login with refresh token (POST)
        /// </summary>
        /// <remarks>
        /// Authenticate user with a refresh token provided via header.
        /// <br/>Returns new JWT access token and potentially new refresh token.
        /// <br/>Uses POST method for token refresh with potential request body.
        /// </remarks>
        /// <param name="token">Refresh token for authentication</param>
        /// <param name="body">Optional login request body (currently unused)</param>
        /// <returns>Token refresh successful</returns>
        public abstract Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<LoginResponse>> LoginWithTokenPost(string token, LoginRequest? body = null, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Validate JWT access token
        /// </summary>
        /// <remarks>
        /// Validates a JWT access token and returns token status information.
        /// <br/>Can be used to check if a token is valid, expired, or contains specific claims.
        /// <br/>Currently returns basic validation response.
        /// </remarks>
        /// <returns>Token validation completed</returns>
        public abstract Tasks.Task<Microsoft.AspNetCore.Mvc.ActionResult<ValidateTokenResponse>> ValidateToken(ValidateTokenRequest body, CancellationToken cancellationToken = default(CancellationToken));

    }
}
