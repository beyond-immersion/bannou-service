using BeyondImmersion.Bannou.Core;
using BeyondImmersion.Bannou.Subscription.ClientEvents;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Subscription;

// =============================================================================
// SubscriptionService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by SubscriptionService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (SubscriptionService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in ISubscriptionService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (SubscriptionService.Helpers.cs):
//     Contains all private/internal helper methods, core logic extracted
//     from endpoints, event publishing helpers, query builders, mapping
//     functions, and any other non-public methods. Every async method in
//     this file MUST call ITelemetryProvider.StartActivity to ensure
//     sub-operations are properly instrumented.
//
// Structural tests enforce both rules:
//   - Services_PrimaryFile_DoesNotCallStartActivity
//   - Services_HelperFiles_HaveStartActivityWhenAsync
//
// WHAT GOES HERE:
//   - Private async helper methods (with StartActivity spans)
//   - Private sync helper methods (query builders, mappers, validators)
//   - Internal static key builders (already in primary file by convention,
//     but may be moved here if the primary file is large)
//   - Event publishing helper methods
//   - Any extracted "core" logic (e.g., CreateAccountCoreAsync)
//
// WHAT STAYS IN THE PRIMARY FILE:
//   - Public interface method implementations (/// <inheritdoc/> methods)
//   - Constructor and field declarations
//   - Constants and key prefix definitions
//
// See: docs/reference/tenets/IMPLEMENTATION-BEHAVIOR.md (T30)
// See: docs/reference/HELPERS-AND-COMMON-PATTERNS.md
// =============================================================================

/// <summary>
/// Private and internal helper methods for SubscriptionService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class SubscriptionService
{
    #region Public Methods for Background Job

    /// <summary>
    /// Mark a subscription as expired. Called by the expiration background worker.
    /// Uses distributed locking per IMPLEMENTATION TENETS for multi-instance safety.
    /// </summary>
    /// <param name="subscriptionId">The subscription ID to expire.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the subscription was successfully expired, false if not found or already inactive.</returns>
    public async Task<bool> ExpireSubscriptionAsync(Guid subscriptionId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.subscription", "SubscriptionService.ExpireSubscription");
        _logger.LogDebug("Expiring subscription {SubscriptionId}", subscriptionId);

        var lockOwner = $"expire-sub-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.SubscriptionLock,
            subscriptionId.ToString(),
            lockOwner,
            _configuration.LockTimeoutSeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogWarning("Failed to acquire lock for subscription expiration: {SubscriptionId}", subscriptionId);
            return false;
        }

        var model = await _subscriptionStore.GetAsync($"{SUBSCRIPTION_KEY_PREFIX}{subscriptionId}", cancellationToken);

        if (model == null || !model.IsActive)
        {
            return false;
        }

        model.IsActive = false;
        model.UpdatedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        await _subscriptionStore.SaveAsync($"{SUBSCRIPTION_KEY_PREFIX}{subscriptionId}", model, cancellationToken: cancellationToken);

        await PublishSubscriptionUpdatedEventAsync(model, SubscriptionAction.Expired, cancellationToken);

        _logger.LogInformation("Expired subscription {SubscriptionId} for account {AccountId}",
            subscriptionId, model.AccountId);

        return true;
    }

    #endregion
    #region Private Helpers

    private async Task AddToAccountIndexAsync(Guid accountId, Guid subscriptionId, CancellationToken cancellationToken)
    {
        await AddToIndexAsync($"{ACCOUNT_SUBSCRIPTIONS_PREFIX}{accountId}", subscriptionId, cancellationToken);
    }

    private async Task AddToServiceIndexAsync(Guid serviceId, Guid subscriptionId, CancellationToken cancellationToken)
    {
        await AddToIndexAsync($"{SERVICE_SUBSCRIPTIONS_PREFIX}{serviceId}", subscriptionId, cancellationToken);
    }

    private async Task AddToSubscriptionIndexAsync(Guid subscriptionId, CancellationToken cancellationToken)
    {
        await AddToIndexAsync(SUBSCRIPTION_INDEX_KEY, subscriptionId, cancellationToken);
    }

    /// <summary>
    /// Adds a subscription ID to a list-based index using distributed locking.
    /// Serializes writes per index key to prevent first-write races where two concurrent
    /// creates for the same account (but different services) could overwrite each other's
    /// index entries when the index key doesn't exist yet.
    /// </summary>
    private async Task AddToIndexAsync(string indexKey, Guid subscriptionId, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.subscription", "SubscriptionService.AddToIndex");

        var lockOwner = $"index-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.SubscriptionLock,
            $"index:{indexKey}",
            lockOwner,
            _configuration.LockTimeoutSeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            throw new InvalidOperationException(
                $"Could not acquire distributed lock for index {indexKey} (subscription {subscriptionId})");
        }

        var subscriptionIds = await _indexStore.GetAsync(indexKey, cancellationToken) ?? new List<Guid>();

        if (subscriptionIds.Contains(subscriptionId))
        {
            return; // Already present
        }

        subscriptionIds.Add(subscriptionId);
        await _indexStore.SaveAsync(indexKey, subscriptionIds, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Publishes a subscription.updated event via lib-messaging.
    /// </summary>
    private async Task PublishSubscriptionUpdatedEventAsync(
        SubscriptionDataModel model, SubscriptionAction action, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.subscription", "SubscriptionService.PublishSubscriptionUpdatedEvent");
        var eventData = new SubscriptionUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            SubscriptionId = model.SubscriptionId,
            AccountId = model.AccountId,
            ServiceId = model.ServiceId,
            StubName = model.StubName,
            DisplayName = model.DisplayName,
            Action = action,
            IsActive = model.IsActive,
            ExpirationDate = model.ExpirationDateUnix.HasValue
                ? DateTimeOffset.FromUnixTimeSeconds(model.ExpirationDateUnix.Value)
                : null
        };

        await _messageBus.PublishSubscriptionUpdatedAsync(eventData);

        _logger.LogDebug("Published subscription.updated event for {SubscriptionId} with action {Action}",
            model.SubscriptionId, action);

        // Push client event to all sessions for the affected account (best-effort)
        try
        {
            await PublishSubscriptionClientEventAsync(model, action, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to push subscription client event for account {AccountId}, action {Action}. " +
                "State mutation was already committed",
                model.AccountId, action);
        }
    }

    /// <summary>
    /// Publishes a subscription status changed client event to all WebSocket sessions
    /// for the affected account via the Entity Session Registry's account routing.
    /// </summary>
    private async Task PublishSubscriptionClientEventAsync(
        SubscriptionDataModel model, SubscriptionAction action, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.subscription", "SubscriptionService.PublishSubscriptionClientEvent");

        var clientEvent = new SubscriptionStatusChangedClientEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            AccountId = model.AccountId,
            ServiceId = model.ServiceId,
            ServiceName = model.DisplayName,
            Action = action,
            IsActive = model.IsActive,
            ExpiresAt = model.ExpirationDateUnix.HasValue
                ? DateTimeOffset.FromUnixTimeSeconds(model.ExpirationDateUnix.Value)
                : null
        };

        var count = await _entitySessionRegistry.PublishToEntitySessionsAsync(
            "account", model.AccountId, clientEvent, cancellationToken);

        _logger.LogDebug(
            "Published subscription.status_changed to {SessionCount} sessions for account {AccountId}",
            count, model.AccountId);
    }

    private static SubscriptionInfo MapToSubscriptionInfo(SubscriptionDataModel model)
    {
        return new SubscriptionInfo
        {
            SubscriptionId = model.SubscriptionId,
            AccountId = model.AccountId,
            ServiceId = model.ServiceId,
            StubName = model.StubName,
            DisplayName = model.DisplayName,
            StartDate = DateTimeOffset.FromUnixTimeSeconds(model.StartDateUnix),
            ExpirationDate = model.ExpirationDateUnix.HasValue
                ? DateTimeOffset.FromUnixTimeSeconds(model.ExpirationDateUnix.Value)
                : null,
            IsActive = model.IsActive,
            CancelledAt = model.CancelledAtUnix.HasValue
                ? DateTimeOffset.FromUnixTimeSeconds(model.CancelledAtUnix.Value)
                : null,
            CancellationReason = model.CancellationReason,
            CreatedAt = DateTimeOffset.FromUnixTimeSeconds(model.CreatedAtUnix),
            UpdatedAt = model.UpdatedAtUnix.HasValue
                ? DateTimeOffset.FromUnixTimeSeconds(model.UpdatedAtUnix.Value)
                : null
        };
    }

    #endregion
}
