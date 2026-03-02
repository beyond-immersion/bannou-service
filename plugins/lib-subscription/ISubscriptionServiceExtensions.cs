namespace BeyondImmersion.BannouService.Subscription;

/// <summary>
/// Extension to the generated ISubscriptionService interface for background worker operations.
/// These methods are used internally by the expiration background service and are not part of the public API.
/// </summary>
public partial interface ISubscriptionService
{
    /// <summary>
    /// Mark a subscription as expired. Called by the expiration background worker.
    /// Uses distributed locking per IMPLEMENTATION TENETS for multi-instance safety.
    /// </summary>
    /// <param name="subscriptionId">The subscription ID to expire.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the subscription was successfully expired, false if not found or already inactive.</returns>
    Task<bool> ExpireSubscriptionAsync(Guid subscriptionId, CancellationToken cancellationToken);
}
