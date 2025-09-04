using BeyondImmersion.BannouService.Services;

namespace BeyondImmersion.BannouService.Authorization;

/// <summary>
/// Service component responsible for authorization handling.
/// </summary>
public interface IAuthorizationService : IDaprService
{
    /// <summary>
    /// Register a new user account using a username/password combination.
    /// </summary>
    Task<ServiceResponse<AccessData?>> Register(string username, string password, string? email);

    /// <summary>
    /// Login to the system using a username/password.
    /// Returns at least an access token (JWT) on success, and potentially a refresh token.
    /// </summary>
    Task<ServiceResponse<AccessData?>> LoginWithCredentials(string username, string password);

    /// <summary>
    /// Login to the system using a refresh token.
    /// Returns at least an access token (JWT) on success, and potentially another refresh token.
    /// </summary>
    Task<ServiceResponse<AccessData?>> LoginWithToken(string token);
}
