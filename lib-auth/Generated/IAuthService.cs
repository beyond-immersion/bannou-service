using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Services;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Auth;

/// <summary>
/// Service interface for Auth API
/// </summary>
public partial interface IAuthService : IBannouService
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
    /// InitOAuth operation
    /// </summary>
    Task<(StatusCodes, object?)> InitOAuthAsync(Provider provider, string redirectUri, string? state, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// CompleteOAuth operation
    /// </summary>
    Task<(StatusCodes, AuthResponse?)> CompleteOAuthAsync(Provider provider, OAuthCallbackRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// VerifySteamAuth operation
    /// </summary>
    Task<(StatusCodes, AuthResponse?)> VerifySteamAuthAsync(SteamVerifyRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// RefreshToken operation
    /// </summary>
    Task<(StatusCodes, AuthResponse?)> RefreshTokenAsync(string jwt, RefreshRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// ValidateToken operation
    /// </summary>
    Task<(StatusCodes, ValidateTokenResponse?)> ValidateTokenAsync(string jwt, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// Logout operation
    /// </summary>
    Task<(StatusCodes, object?)> LogoutAsync(string jwt, LogoutRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// GetSessions operation
    /// </summary>
    Task<(StatusCodes, SessionsResponse?)> GetSessionsAsync(string jwt, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// TerminateSession operation
    /// </summary>
    Task<(StatusCodes, object?)> TerminateSessionAsync(string jwt, TerminateSessionRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// RequestPasswordReset operation
    /// </summary>
    Task<(StatusCodes, object?)> RequestPasswordResetAsync(PasswordResetRequest body, CancellationToken cancellationToken = default(CancellationToken));

    /// <summary>
    /// ConfirmPasswordReset operation
    /// </summary>
    Task<(StatusCodes, object?)> ConfirmPasswordResetAsync(PasswordResetConfirmRequest body, CancellationToken cancellationToken = default(CancellationToken));

}
