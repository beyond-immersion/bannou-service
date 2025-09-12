using Microsoft.AspNetCore.Mvc;
using BeyondImmersion.BannouService.Controllers.Generated;

namespace BeyondImmersion.BannouService.Auth;

/// <summary>
/// Interface for authentication service operations.
/// Implements business logic for generated AuthController methods.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Register new user account
    /// </summary>
    Task<ActionResult<RegisterResponse>> RegisterAsync(
        RegisterRequest body,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Login with username and password (GET)
    /// </summary>
    Task<ActionResult<LoginResponse>> LoginWithCredentialsGetAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Login with username and password (POST)
    /// </summary>
    Task<ActionResult<LoginResponse>> LoginWithCredentialsPostAsync(
        string username,
        string password,
        LoginRequest? body,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Login with refresh token (GET)
    /// </summary>
    Task<ActionResult<LoginResponse>> LoginWithTokenGetAsync(
        string token,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Login with refresh token (POST)
    /// </summary>
    Task<ActionResult<LoginResponse>> LoginWithTokenPostAsync(
        string token,
        LoginRequest? body,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate JWT access token
    /// </summary>
    Task<ActionResult<ValidateTokenResponse>> ValidateTokenAsync(
        ValidateTokenRequest body,
        CancellationToken cancellationToken = default);
}
