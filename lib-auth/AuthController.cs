using Microsoft.AspNetCore.Mvc;

namespace BeyondImmersion.BannouService.Auth;

/// <summary>
/// Manual implementation for endpoints marked with x-manual-implementation in the schema.
/// This partial class extends the generated AuthController with custom endpoint implementations.
/// </summary>
public partial class AuthController
{
    /// <summary>
    /// Initialize OAuth2 flow - redirects browser to OAuth provider.
    /// This is a manual implementation because it returns a 302 redirect, not JSON.
    /// </summary>
    [HttpGet("auth/oauth/{provider}/init")]
    public async Task<IActionResult> InitOAuth(
        [FromRoute] Provider provider,
        [FromQuery][Microsoft.AspNetCore.Mvc.ModelBinding.BindRequired] string redirectUri,
        [FromQuery] string? state,
        CancellationToken cancellationToken = default)
    {
        // Cast to concrete implementation to access InitOAuthAsync (not on interface due to x-manual-implementation)
        var authService = (AuthService)_implementation;
        var (statusCode, result) = await authService.InitOAuthAsync(provider, redirectUri, state, cancellationToken);

        if (statusCode != StatusCodes.OK || result?.Authorization_url == null)
        {
            return statusCode switch
            {
                StatusCodes.BadRequest => BadRequest("Invalid OAuth provider"),
                StatusCodes.InternalServerError => StatusCode(500, "OAuth configuration error"),
                _ => StatusCode((int)statusCode, "Error initializing OAuth")
            };
        }

        // Return 302 redirect to the OAuth provider
        return Redirect(result.Authorization_url.ToString());
    }
}
