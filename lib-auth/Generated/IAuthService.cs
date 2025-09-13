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
        /// Login operation
        /// </summary>
        Task<(StatusCodes, AuthResponse?)> LoginAsync(LoginRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// Register operation
        /// </summary>
        Task<(StatusCodes, RegisterResponse?)> RegisterAsync(RegisterRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// CompleteOAuth operation
        /// </summary>
        Task<(StatusCodes, AuthResponse?)> CompleteOAuthAsync(Provider2 provider, OAuthCallbackRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// VerifySteamAuth operation
        /// </summary>
        Task<(StatusCodes, AuthResponse?)> VerifySteamAuthAsync(SteamVerifyRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// RefreshToken operation
        /// </summary>
        Task<(StatusCodes, AuthResponse?)> RefreshTokenAsync(RefreshRequest body, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// ValidateToken operation
        /// </summary>
        Task<(StatusCodes, ValidateTokenResponse?)> ValidateTokenAsync(CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>
        /// GetSessions operation
        /// </summary>
        Task<(StatusCodes, SessionsResponse?)> GetSessionsAsync(CancellationToken cancellationToken = default(CancellationToken));

}
