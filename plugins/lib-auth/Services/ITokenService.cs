using BeyondImmersion.BannouService.Account;

namespace BeyondImmersion.BannouService.Auth.Services;

/// <summary>
/// Service responsible for JWT token generation and validation.
/// Handles access token creation, refresh token management, and JWT validation.
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Generates a new access token (JWT) for the given account.
    /// Creates session data in Redis and returns the signed JWT plus sessionId.
    /// </summary>
    /// <param name="account">The account to generate the token for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tuple of (accessToken, sessionId) for event publishing.</returns>
    Task<(string accessToken, string sessionId)> GenerateAccessTokenAsync(AccountResponse account, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a new refresh token.
    /// </summary>
    /// <returns>A unique refresh token string.</returns>
    string GenerateRefreshToken();

    /// <summary>
    /// Stores a refresh token associated with an account.
    /// </summary>
    /// <param name="accountId">The account ID.</param>
    /// <param name="refreshToken">The refresh token to store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StoreRefreshTokenAsync(string accountId, string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a refresh token and returns the associated account ID.
    /// </summary>
    /// <param name="refreshToken">The refresh token to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The account ID if valid, null otherwise.</returns>
    Task<string?> ValidateRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a refresh token from storage.
    /// </summary>
    /// <param name="refreshToken">The refresh token to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RemoveRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a JWT access token and returns session information.
    /// </summary>
    /// <param name="jwt">The JWT token to validate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tuple of (StatusCode, ValidateTokenResponse) - null response if invalid.</returns>
    Task<(StatusCodes, ValidateTokenResponse?)> ValidateTokenAsync(string jwt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a cryptographically secure token for password reset.
    /// </summary>
    /// <returns>A secure random token string.</returns>
    string GenerateSecureToken();
}
