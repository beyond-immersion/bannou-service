using BeyondImmersion.BannouService.Services;
using System.Net;

namespace BeyondImmersion.BannouService.Authorization;

/// <summary>
/// Service component responsible for authorization handling.
/// </summary>
public interface IAuthorizationService : IDaprService
{
    public class LoginResult
    {
        public string? AccessToken { get; set; }
        public string? RefreshToken { get; set; }
    }

    /// <summary>
    /// Register a new user account using a username/password combination.
    /// </summary>
    Task<(HttpStatusCode, LoginResult?)> Register(string username, string password, string? email);

    /// <summary>
    /// Login to the system using a username/password.
    /// Returns at least an access token (JWT) on success, and potentially a refresh token.
    /// </summary>
    Task<(HttpStatusCode, LoginResult?)> LoginWithCredentials(string username, string password);

    /// <summary>
    /// Login to the system using a refresh token.
    /// Returns at least an access token (JWT) on success, and potentially another refresh token.
    /// </summary>
    Task<(HttpStatusCode, LoginResult?)> LoginWithToken(string username, string token);
}
