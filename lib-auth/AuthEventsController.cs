using BeyondImmersion.BannouService.Accounts;
using BeyondImmersion.BannouService.Subscriptions;
using Dapr;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Linq;
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
            // Read and parse event using shared helper (handles both CloudEvents and raw formats)
            var accountDeletedEvent = await DaprEventHelper.ReadEventAsync<AccountDeletedEvent>(Request);

            if (accountDeletedEvent == null)
            {
                _logger.LogWarning("[AUTH-EVENT] Failed to parse AccountDeletedEvent from request body");
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

    /// <summary>
    /// Handle account update events from the Accounts service.
    /// Called by Dapr when an account's roles change to propagate to sessions.
    /// </summary>
    [Topic("bannou-pubsub", "account.updated")]
    [HttpPost("handle-account-updated")]
    public async Task<IActionResult> HandleAccountUpdatedAsync()
    {
        try
        {
            var accountUpdatedEvent = await DaprEventHelper.ReadEventAsync<AccountUpdatedEvent>(Request);

            if (accountUpdatedEvent == null)
            {
                _logger.LogWarning("[AUTH-EVENT] Failed to parse AccountUpdatedEvent from request body");
                return BadRequest("Invalid event data");
            }

            _logger.LogInformation("[AUTH-EVENT] Processing account.updated event for AccountId: {AccountId}, ChangedFields: {ChangedFields}",
                accountUpdatedEvent.AccountId, string.Join(", ", accountUpdatedEvent.ChangedFields ?? new List<string>()));

            // Only propagate if roles changed
            if (accountUpdatedEvent.ChangedFields?.Contains("roles") != true)
            {
                _logger.LogDebug("[AUTH-EVENT] Account update did not include role changes, skipping propagation");
                return Ok();
            }

            var authService = _authService as AuthService;
            if (authService == null)
            {
                _logger.LogWarning("[AUTH-EVENT] Auth service implementation not available for role propagation");
                return Ok();
            }

            // In the new event pattern, Roles contains the current state (full model data)
            var newRoles = accountUpdatedEvent.Roles?.ToList() ?? new List<string>();

            await authService.PropagateRoleChangesAsync(accountUpdatedEvent.AccountId, newRoles, CancellationToken.None);

            _logger.LogInformation("[AUTH-EVENT] Successfully propagated role changes for account: {AccountId}",
                accountUpdatedEvent.AccountId);

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AUTH-EVENT] Failed to process account.updated event");
            return StatusCode(500, "Internal server error processing event");
        }
    }

    /// <summary>
    /// Handle subscription update events from the Subscriptions service.
    /// Called by Dapr when a subscription changes to propagate authorization changes to sessions.
    /// </summary>
    [Topic("bannou-pubsub", "subscription.updated")]
    [HttpPost("handle-subscription-updated")]
    public async Task<IActionResult> HandleSubscriptionUpdatedAsync()
    {
        try
        {
            var subscriptionUpdatedEvent = await DaprEventHelper.ReadEventAsync<SubscriptionUpdatedEvent>(Request);

            if (subscriptionUpdatedEvent == null)
            {
                _logger.LogWarning("[AUTH-EVENT] Failed to parse SubscriptionUpdatedEvent from request body");
                return BadRequest("Invalid event data");
            }

            _logger.LogInformation("[AUTH-EVENT] Processing subscription.updated event for AccountId: {AccountId}, StubName: {StubName}, Action: {Action}",
                subscriptionUpdatedEvent.AccountId, subscriptionUpdatedEvent.StubName, subscriptionUpdatedEvent.Action);

            var authService = _authService as AuthService;
            if (authService == null)
            {
                _logger.LogWarning("[AUTH-EVENT] Auth service implementation not available for authorization propagation");
                return Ok();
            }

            await authService.PropagateSubscriptionChangesAsync(subscriptionUpdatedEvent.AccountId, CancellationToken.None);

            _logger.LogInformation("[AUTH-EVENT] Successfully propagated subscription changes for account: {AccountId}",
                subscriptionUpdatedEvent.AccountId);

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AUTH-EVENT] Failed to process subscription.updated event");
            return StatusCode(500, "Internal server error processing event");
        }
    }
}
