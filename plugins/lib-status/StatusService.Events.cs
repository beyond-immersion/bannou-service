using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Seed;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Status;

/// <summary>
/// Partial class for StatusService event handling.
/// Contains event consumer registration and handler implementations for
/// seed cache invalidation and account deletion cleanup per FOUNDATION TENETS
/// (Account Deletion Cleanup Obligation).
/// Note: Character cleanup uses lib-resource (x-references), not event subscription.
/// </summary>
/// <remarks>
/// <para>
/// The <c>seed.capability.updated</c> subscription provides the distributed guarantee
/// for seed effects cache invalidation across all nodes. The DI listener
/// (<see cref="StatusSeedEvolutionListener"/>) provides fast local notification.
/// </para>
/// </remarks>
public partial class StatusService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        // Account deletion cleanup per FOUNDATION TENETS (Account Deletion Cleanup Obligation).
        // Account-owned status containers must be deleted when the owning account is deleted.
        eventConsumer.RegisterHandler<IStatusService, AccountDeletedEvent>(
            "account.deleted",
            async (svc, evt) => await ((StatusService)svc).HandleAccountDeletedAsync(evt));

        eventConsumer.RegisterHandler<IStatusService, SeedCapabilityUpdatedEvent>(
            "seed.capability.updated",
            async (svc, evt) => await ((StatusService)svc).HandleSeedCapabilityUpdatedAsync(evt));
    }

    /// <summary>
    /// Handles account.deleted events by cleaning up all account-owned status containers.
    /// Delegates to the existing CleanupByOwnerAsync endpoint logic.
    /// Per FOUNDATION TENETS: Account deletion is always CASCADE — data has no owner and must be removed.
    /// </summary>
    public async Task HandleAccountDeletedAsync(AccountDeletedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.status", "StatusService.HandleAccountDeleted");
        _logger.LogInformation("Handling account.deleted for account {AccountId}", evt.AccountId);

        try
        {
            await CleanupByOwnerAsync(
                new CleanupByOwnerRequest
                {
                    OwnerType = EntityType.Account,
                    OwnerId = evt.AccountId
                }, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clean up status containers for account {AccountId}", evt.AccountId);
            await _messageBus.TryPublishErrorAsync(
                "status",
                "CleanupStatusForAccount",
                ex.GetType().Name,
                ex.Message,
                dependency: null,
                endpoint: "account.deleted",
                details: $"accountId={evt.AccountId}",
                stack: ex.StackTrace);
        }
    }

    /// <summary>
    /// Handles seed.capability.updated events for distributed cache invalidation.
    /// Complements the DI listener which only fires on the node that processed the API call.
    /// The event carries seedId only; we look up the seed owner to invalidate the correct cache entry.
    /// </summary>
    /// <param name="evt">The seed capability updated event.</param>
    public async Task HandleSeedCapabilityUpdatedAsync(SeedCapabilityUpdatedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.status", "StatusService.HandleSeedCapabilityUpdatedAsync");
        if (!_configuration.SeedEffectsEnabled)
        {
            return;
        }

        // SeedCapabilityUpdatedEvent carries seedId, not ownerId;
        // look up the seed to find the owner for cache invalidation (L2 — hard dependency per FOUNDATION TENETS)
        try
        {
            var seed = await _seedClient.GetSeedAsync(
                new GetSeedRequest { SeedId = evt.SeedId },
                CancellationToken.None);

            var cacheKey = BuildSeedEffectsCacheKey(seed.OwnerId, seed.OwnerType);
            await _seedEffectsCacheStore.DeleteAsync(cacheKey);
            _logger.LogDebug(
                "Invalidated seed effects cache for {OwnerType} {OwnerId} via event (seed {SeedId})",
                seed.OwnerType, seed.OwnerId, evt.SeedId);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogDebug("Seed {SeedId} not found for cache invalidation via event", evt.SeedId);
        }
        catch (Exception ex)
        {
            // Cache invalidation failure is non-fatal; cache will expire via TTL
            _logger.LogWarning(ex,
                "Failed to invalidate seed effects cache for seed {SeedId} via event",
                evt.SeedId);
        }
    }
}
