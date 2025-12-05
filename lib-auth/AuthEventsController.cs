using BeyondImmersion.BannouService.Accounts;
using Dapr;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Text.Json;

namespace BeyondImmersion.BannouService.Auth;

/// <summary>
/// Controller for handling Dapr pubsub events related to the Auth service.
/// This controller is separate from the generated AuthController to allow
/// Dapr subscription discovery while maintaining schema-first architecture.
/// </summary>
[ApiController]
[Route("[controller]")]
public class AuthEventsController : ControllerBase
{
    private readonly ILogger<AuthEventsController> _logger;
    private readonly IAuthService _authService;

    public AuthEventsController(
        ILogger<AuthEventsController> logger,
        IAuthService authService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
    }

    /// <summary>
    /// Handle account deletion events from the Accounts service.
    /// Called by Dapr when an account is deleted to invalidate all associated sessions.
    /// </summary>
    [Topic("bannou-pubsub", "account.deleted")]
    [HttpPost("handle-account-deleted")]
    public async Task<IActionResult> HandleAccountDeletedAsync()
    {
        try
        {
            // Read the request body - Dapr pubsub delivers CloudEvents format
            string rawBody;
            using (var reader = new StreamReader(Request.Body, leaveOpen: true))
            {
                rawBody = await reader.ReadToEndAsync();
            }

            _logger.LogInformation("[AUTH-EVENT] Received account.deleted event - raw body length: {Length}",
                rawBody?.Length ?? 0);

            if (string.IsNullOrWhiteSpace(rawBody))
            {
                _logger.LogWarning("[AUTH-EVENT] Received empty body for account.deleted event");
                return BadRequest("Empty event body");
            }

            // Parse CloudEvents wrapper to extract the actual event data
            var cloudEvent = JsonSerializer.Deserialize<JsonElement>(rawBody);

            if (!cloudEvent.TryGetProperty("data", out var dataElement))
            {
                _logger.LogWarning("[AUTH-EVENT] CloudEvent missing 'data' property for account.deleted");
                return BadRequest("Missing event data");
            }

            // Deserialize the actual AccountDeletedEvent from the data property
            var accountDeletedEvent = JsonSerializer.Deserialize<AccountDeletedEvent>(
                dataElement.GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (accountDeletedEvent == null)
            {
                _logger.LogWarning("[AUTH-EVENT] Failed to deserialize AccountDeletedEvent");
                return BadRequest("Invalid event data");
            }

            _logger.LogInformation("[AUTH-EVENT] Processing account.deleted event for AccountId: {AccountId}, Email: {Email}",
                accountDeletedEvent.AccountId, accountDeletedEvent.Email);

            // Cast to concrete service to call session invalidation method
            var authService = _authService as AuthService;
            if (authService == null)
            {
                _logger.LogWarning("[AUTH-EVENT] Auth service implementation not available for session invalidation");
                return Ok();
            }

            // Invalidate all sessions for the deleted account
            await authService.InvalidateAccountSessionsAsync(accountDeletedEvent.AccountId);

            _logger.LogInformation("[AUTH-EVENT] Successfully invalidated sessions for deleted account: {AccountId}",
                accountDeletedEvent.AccountId);

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AUTH-EVENT] Failed to process account.deleted event");
            return StatusCode(500, "Internal server error processing event");
        }
    }
}
