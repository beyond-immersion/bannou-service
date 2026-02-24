using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Auth;

/// <summary>
/// Partial class for AuthService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
public partial class AuthService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<IAuthService, AccountDeletedEvent>(
            "account.deleted",
            async (svc, evt) => await ((AuthService)svc).HandleAccountDeletedAsync(evt));

        eventConsumer.RegisterHandler<IAuthService, AccountUpdatedEvent>(
            "account.updated",
            async (svc, evt) => await ((AuthService)svc).HandleAccountUpdatedAsync(evt));
    }

    /// <summary>
    /// Handles account.deleted events.
    /// Invalidates all sessions and cleans up OAuth links for the deleted account.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleAccountDeletedAsync(AccountDeletedEvent evt)
    {
        _logger.LogInformation("Processing account.deleted event for AccountId: {AccountId}, Email: {Email}",
            evt.AccountId, evt.Email);

        await InvalidateAccountSessionsAsync(evt.AccountId);
        await _oauthService.CleanupOAuthLinksForAccountAsync(evt.AccountId);

        _logger.LogInformation("Successfully invalidated sessions and cleaned up OAuth links for deleted account: {AccountId}",
            evt.AccountId);
    }

    /// <summary>
    /// Handles account.updated events.
    /// Propagates role and email changes to active sessions when modified.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleAccountUpdatedAsync(AccountUpdatedEvent evt)
    {
        _logger.LogInformation("Processing account.updated event for AccountId: {AccountId}, ChangedFields: {ChangedFields}",
            evt.AccountId, string.Join(", ", evt.ChangedFields ?? new List<string>()));

        var changedFields = evt.ChangedFields ?? new List<string>();
        var rolesChanged = changedFields.Contains("roles");
        var emailChanged = changedFields.Contains("email");

        if (!rolesChanged && !emailChanged)
        {
            _logger.LogDebug("Account update did not include role or email changes, skipping session propagation");
            return;
        }

        if (rolesChanged)
        {
            var newRoles = evt.Roles?.ToList() ?? new List<string>();
            await PropagateRoleChangesAsync(evt.AccountId, newRoles, CancellationToken.None);
            _logger.LogInformation("Propagated role changes for account: {AccountId}", evt.AccountId);
        }

        if (emailChanged)
        {
            await PropagateEmailChangeAsync(evt.AccountId, evt.Email, CancellationToken.None);
            _logger.LogInformation("Propagated email change for account: {AccountId}", evt.AccountId);
        }
    }

}
