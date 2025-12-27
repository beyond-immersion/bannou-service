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

        eventConsumer.RegisterHandler<IAuthService, SubscriptionUpdatedEvent>(
            "subscription.updated",
            async (svc, evt) => await ((AuthService)svc).HandleSubscriptionUpdatedAsync(evt));
    }

    /// <summary>
    /// Handles account.deleted events.
    /// Invalidates all sessions for the deleted account.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleAccountDeletedAsync(AccountDeletedEvent evt)
    {
        _logger.LogInformation("Processing account.deleted event for AccountId: {AccountId}, Email: {Email}",
            evt.AccountId, evt.Email);

        await InvalidateAccountSessionsAsync(evt.AccountId);

        _logger.LogInformation("Successfully invalidated sessions for deleted account: {AccountId}",
            evt.AccountId);
    }

    /// <summary>
    /// Handles account.updated events.
    /// Propagates role changes to active sessions when roles are modified.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleAccountUpdatedAsync(AccountUpdatedEvent evt)
    {
        _logger.LogInformation("Processing account.updated event for AccountId: {AccountId}, ChangedFields: {ChangedFields}",
            evt.AccountId, string.Join(", ", evt.ChangedFields ?? new List<string>()));

        // Only propagate if roles changed
        if (evt.ChangedFields?.Contains("roles") != true)
        {
            _logger.LogDebug("Account update did not include role changes, skipping propagation");
            return;
        }

        // In the new event pattern, Roles contains the current state (full model data)
        var newRoles = evt.Roles?.ToList() ?? new List<string>();

        await PropagateRoleChangesAsync(evt.AccountId, newRoles, CancellationToken.None);

        _logger.LogInformation("Successfully propagated role changes for account: {AccountId}",
            evt.AccountId);
    }

    /// <summary>
    /// Handles subscription.updated events.
    /// Propagates authorization changes to active sessions.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleSubscriptionUpdatedAsync(SubscriptionUpdatedEvent evt)
    {
        _logger.LogInformation("Processing subscription.updated event for AccountId: {AccountId}, StubName: {StubName}, Action: {Action}",
            evt.AccountId, evt.StubName, evt.Action);

        await PropagateSubscriptionChangesAsync(evt.AccountId, CancellationToken.None);

        _logger.LogInformation("Successfully propagated subscription changes for account: {AccountId}",
            evt.AccountId);
    }
}
