using Microsoft.AspNetCore.Mvc;
using BeyondImmersion.BannouService.Controllers.Generated;

namespace BeyondImmersion.BannouService.Auth;

/// <summary>
/// Interface for authentication service operations.
/// Implements business logic for JWT authentication, OAuth flows, and session management.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Authenticates user with email and password.
    /// </summary>
    Task<ActionResult<AuthResponse>> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a new user account.
    /// </summary>
    Task<ActionResult<AuthResponse>> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Refreshes an access token using a refresh token.
    /// </summary>
    Task<ActionResult<AuthResponse>> RefreshAsync(
        RefreshRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a JWT token and returns user information.
    /// </summary>
    Task<ActionResult<ValidateTokenResponse>> ValidateTokenAsync(
        string token,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs out a user by invalidating their refresh tokens.
    /// </summary>
    Task<IActionResult> LogoutAsync(
        LogoutRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Initiates OAuth2 flow for supported providers.
    /// </summary>
    Task<IActionResult> InitOAuthAsync(
        string provider,
        string redirectUri,
        string? state = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes OAuth2 flow and returns authentication response.
    /// </summary>
    Task<ActionResult<AuthResponse>> CompleteOAuthAsync(
        string provider,
        OAuthCallbackRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Initiates Steam authentication flow.
    /// </summary>
    Task<IActionResult> InitSteamAuthAsync(
        string returnUrl,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes Steam authentication flow.
    /// </summary>
    Task<ActionResult<AuthResponse>> CompleteSteamAuthAsync(
        SteamCallbackRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes user password (requires current password).
    /// </summary>
    Task<IActionResult> ChangePasswordAsync(
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Initiates password reset flow via email.
    /// </summary>
    Task<IActionResult> ForgotPasswordAsync(
        ForgotPasswordRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes password reset with token from email.
    /// </summary>
    Task<IActionResult> ResetPasswordAsync(
        ResetPasswordRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies email address with token sent during registration.
    /// </summary>
    Task<IActionResult> VerifyEmailAsync(
        VerifyEmailRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resends email verification token.
    /// </summary>
    Task<IActionResult> ResendEmailVerificationAsync(
        ResendEmailVerificationRequest request,
        CancellationToken cancellationToken = default);
}