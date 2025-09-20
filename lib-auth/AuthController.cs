using BeyondImmersion.BannouService.Accounts;
using Dapr;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Auth;

/// <summary>
/// Manual implementation for event handlers and custom endpoints.
/// This class extends the generated AuthController partial class.
/// </summary>
public partial class AuthController
{
    /// <summary>
    /// Event handler for account.deleted events
    /// </summary>
    /// <remarks>
    /// Subscribe to account deletion events to invalidate sessions
    /// </remarks>
    [Topic("bannou-pubsub", "account.deleted")]
    [HttpPost("/dapr/events/account-deleted")]
    public async Task<IActionResult> HandleAccountDeletedEvent([FromBody] AccountDeletedEvent eventData)
    {
        try
        {
            // Delegate to service's OnEventReceivedAsync method
            await _implementation.OnEventReceivedAsync("account.deleted", eventData);
            return Ok(new { status = "processed" });
        }
        catch (System.Exception ex)
        {
            // Log error but don't expose internal details
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}
