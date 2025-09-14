using Microsoft.AspNetCore.Mvc;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Auth;

/// <summary>
/// Manual implementation for endpoints that require custom logic.
/// This partial class extends the generated AuthController.
/// </summary>
public partial class AuthController
{
    /// <summary>
    /// Validate access token - Controller-only method with JWT extraction
    /// </summary>
    /// <returns>Token validation result</returns>
    public Task<ActionResult<ValidateTokenResponse>> ValidateToken(CancellationToken cancellationToken)
    {
        // Extract JWT from Authorization header
        var authHeader = HttpContext.Request.Headers.Authorization.FirstOrDefault();
        if (authHeader == null || !authHeader.StartsWith("Bearer "))
        {
            return Task.FromResult<ActionResult<ValidateTokenResponse>>(Unauthorized(new ValidateTokenResponse { Valid = false, Message = "Missing or invalid Authorization header" }));
        }

        var token = authHeader.Substring(7); // Remove "Bearer " prefix

        // Basic token validation (implement proper JWT validation as needed)
        if (string.IsNullOrEmpty(token))
        {
            return Task.FromResult<ActionResult<ValidateTokenResponse>>(Unauthorized(new ValidateTokenResponse { Valid = false, Message = "Empty token" }));
        }

        // For now, return valid for any non-empty token
        // In production, implement proper JWT validation with signature verification
        return Task.FromResult<ActionResult<ValidateTokenResponse>>(Ok(new ValidateTokenResponse { Valid = true, Message = "Token is valid" }));
    }
}
