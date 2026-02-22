using Microsoft.AspNetCore.Mvc;

namespace BeyondImmersion.BannouService.Auth;

/// <summary>
/// Concrete controller extending generated AuthControllerBase.
/// Provides the manual InitOAuth implementation (browser redirect, not JSON).
/// </summary>
public partial class AuthController : AuthControllerBase
{
    private readonly IAuthService _authService;

    /// <summary>
    /// Passes through to the base class constructor and stores service reference
    /// for manual endpoint implementations (base class field is private).
    /// </summary>
    public AuthController(
        IAuthService implementation,
        BeyondImmersion.BannouService.Services.ITelemetryProvider telemetryProvider)
        : base(implementation, telemetryProvider)
    {
        _authService = implementation;
    }

    /// <summary>
    /// Initialize OAuth2 flow - redirects browser to OAuth provider.
    /// Overrides the abstract method from AuthControllerBase because this endpoint
    /// returns a 302 redirect, not JSON (x-controller-only in schema).
    /// </summary>
    public override async Task<IActionResult> InitOAuth(
        [Microsoft.AspNetCore.Mvc.ModelBinding.BindRequired] Provider provider,
        [FromQuery][Microsoft.AspNetCore.Mvc.ModelBinding.BindRequired] string redirectUri,
        [FromQuery] string? state,
        CancellationToken cancellationToken = default)
    {
        // Cast to concrete implementation to access InitOAuthAsync (not on interface due to x-controller-only)
        var authService = (AuthService)_authService;
        var (statusCode, result) = await authService.InitOAuthAsync(provider, redirectUri, state, cancellationToken);

        if (statusCode != StatusCodes.OK || result?.AuthorizationUrl == null)
        {
            return statusCode switch
            {
                StatusCodes.BadRequest => BadRequest("Invalid OAuth provider"),
                StatusCodes.InternalServerError => StatusCode(500, "OAuth configuration error"),
                _ => StatusCode((int)statusCode, "Error initializing OAuth")
            };
        }

        // Return 302 redirect to the OAuth provider
        return Redirect(result.AuthorizationUrl.ToString());
    }
}
