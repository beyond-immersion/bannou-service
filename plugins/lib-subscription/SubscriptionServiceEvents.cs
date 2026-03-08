using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Subscription;

/// <summary>
/// Partial class for SubscriptionService event handling.
/// Contains event consumer registration and handler implementations for
/// account deletion cleanup per FOUNDATION TENETS (Account Deletion Cleanup Obligation).
/// </summary>
public partial class SubscriptionService : IBannouService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor via IBannouService interface.
    /// </summary>
    void IBannouService.RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        // Account deletion cleanup per FOUNDATION TENETS (Account Deletion Cleanup Obligation).
        // Account-owned subscriptions must be deleted when the owning account is deleted.
        eventConsumer.RegisterHandler<ISubscriptionService, AccountDeletedEvent>(
            "account.deleted",
            async (svc, evt) => await ((SubscriptionService)svc).HandleAccountDeletedAsync(evt));
    }

    /// <summary>
    /// Handles account.deleted events by cleaning up all account-owned subscriptions
    /// and their associated index entries.
    /// Per FOUNDATION TENETS: Account deletion is always CASCADE — data has no owner and must be removed.
    /// </summary>
    internal async Task HandleAccountDeletedAsync(AccountDeletedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.subscription", "SubscriptionService.HandleAccountDeleted");
        _logger.LogInformation("Handling account.deleted for account {AccountId}", evt.AccountId);

        try
        {
            await CleanupSubscriptionsForAccountAsync(evt.AccountId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clean up subscriptions for account {AccountId}", evt.AccountId);
            await _messageBus.TryPublishErrorAsync(
                "subscription",
                "CleanupSubscriptionsForAccount",
                ex.GetType().Name,
                ex.Message,
                dependency: null,
                endpoint: "account.deleted",
                details: $"accountId={evt.AccountId}",
                stack: ex.StackTrace);
        }
    }

    /// <summary>
    /// Cleans up all subscriptions for a given account by deleting subscription records,
    /// service index entries, global index entries, and the account index itself.
    /// Publishes subscription.updated lifecycle events for each deleted subscription.
    /// Per-subscription failures are logged as warnings and do not abort overall cleanup.
    /// </summary>
    /// <param name="accountId">The account whose subscriptions should be cleaned up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal async Task<int> CleanupSubscriptionsForAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.subscription", "SubscriptionService.CleanupSubscriptionsForAccount");

        var accountIndexKey = BuildAccountSubscriptionsKey(accountId);
        var subscriptionIds = await _indexStore.GetAsync(accountIndexKey, cancellationToken);

        if (subscriptionIds == null || subscriptionIds.Count == 0)
        {
            _logger.LogDebug("No subscriptions found for account {AccountId}, skipping cleanup", accountId);
            return 0;
        }

        var deletedCount = 0;
        foreach (var subscriptionId in subscriptionIds)
        {
            try
            {
                var model = await _subscriptionStore.GetAsync(BuildSubscriptionKey(subscriptionId), cancellationToken);

                if (model != null)
                {
                    // Remove from service index
                    await RemoveFromIndexAsync(BuildServiceSubscriptionsKey(model.ServiceId), subscriptionId, cancellationToken);

                    // Remove from global subscription index
                    await RemoveFromIndexAsync(SUBSCRIPTION_INDEX_KEY, subscriptionId, cancellationToken);

                    // Delete the subscription record
                    await _subscriptionStore.DeleteAsync(BuildSubscriptionKey(subscriptionId), cancellationToken);

                    // Publish lifecycle event for each deleted subscription
                    await PublishSubscriptionUpdatedEventAsync(model, SubscriptionAction.Cancelled, cancellationToken);

                    deletedCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clean up subscription {SubscriptionId} for account {AccountId}",
                    subscriptionId, accountId);
            }
        }

        // Delete the account subscriptions index itself
        await _indexStore.DeleteAsync(accountIndexKey, cancellationToken);

        _logger.LogInformation("Cleaned up {DeletedCount} subscriptions for account {AccountId}", deletedCount, accountId);
        return deletedCount;
    }

    /// <summary>
    /// Removes a subscription ID from a list-based index using distributed locking.
    /// </summary>
    private async Task RemoveFromIndexAsync(string indexKey, Guid subscriptionId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.subscription", "SubscriptionService.RemoveFromIndex");

        var lockOwner = $"index-remove-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.SubscriptionLock,
            $"index:{indexKey}",
            lockOwner,
            _configuration.LockTimeoutSeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogWarning("Failed to acquire lock for index removal: {IndexKey}, subscription {SubscriptionId}",
                indexKey, subscriptionId);
            return;
        }

        var ids = await _indexStore.GetAsync(indexKey, cancellationToken);
        if (ids != null && ids.Remove(subscriptionId))
        {
            await _indexStore.SaveAsync(indexKey, ids, cancellationToken: cancellationToken);
        }
    }
}
