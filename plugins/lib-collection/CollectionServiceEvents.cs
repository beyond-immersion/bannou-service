using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Collection;

/// <summary>
/// Partial class for CollectionService event handling.
/// Contains event consumer registration and handler implementations for
/// account.deleted cleanup.
/// Note: Character cleanup uses lib-resource (x-references), not event subscription, per FOUNDATION TENETS.
/// </summary>
public partial class CollectionService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// Note: Character cleanup uses lib-resource (x-references), not event subscription, per FOUNDATION TENETS.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<ICollectionService, AccountDeletedEvent>(
            "account.deleted",
            async (svc, evt) => await ((CollectionService)svc).HandleAccountDeletedAsync(evt));
    }

    /// <summary>
    /// Handles account.deleted events by cleaning up all account-owned collections.
    /// Deletes inventory containers and cache entries for each collection.
    /// Wraps cleanup in try-catch since event handlers have no generated controller boundary.
    /// </summary>
    /// <param name="evt">The account deleted event data.</param>
    public async Task HandleAccountDeletedAsync(AccountDeletedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.collection", "CollectionService.HandleAccountDeleted");
        _logger.LogInformation("Handling account.deleted for account {AccountId}", evt.AccountId);
        try
        {
            await CleanupCollectionsForOwnerAsync(evt.AccountId, EntityType.Account);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clean up collections for account {AccountId}", evt.AccountId);
            await _messageBus.TryPublishErrorAsync(
                "collection",
                "CleanupCollectionsForAccount",
                "cleanup_failed",
                ex.Message,
                dependency: null,
                endpoint: "account.deleted",
                details: $"ownerId={evt.AccountId}",
                stack: ex.StackTrace);
        }
    }

    /// <summary>
    /// Cleans up all collections for a given owner by deleting containers, cache, and instance records.
    /// Returns the number of collections successfully deleted. Per-collection failures are logged as
    /// warnings and do not abort the overall cleanup. Infrastructure exceptions propagate to caller.
    /// </summary>
    /// <param name="ownerId">The owner whose collections should be cleaned up.</param>
    /// <param name="ownerType">The owner type discriminator.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal async Task<int> CleanupCollectionsForOwnerAsync(Guid ownerId, EntityType ownerType, CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.collection", "CollectionService.CleanupCollectionsForOwner");

        var collections = await CollectionStore.QueryAsync(
            c => c.OwnerId == ownerId && c.OwnerType == ownerType,
            cancellationToken: cancellationToken);

        if (collections.Count == 0)
        {
            _logger.LogDebug("No collections found for {OwnerType} {OwnerId}, skipping cleanup", ownerType, ownerId);
            return 0;
        }

        var deletedCount = 0;
        foreach (var collection in collections)
        {
            try
            {
                // Delete the inventory container
                await _inventoryClient.DeleteContainerAsync(
                    new Inventory.DeleteContainerRequest { ContainerId = collection.ContainerId },
                    cancellationToken);

                // Delete the cache entry
                await CollectionCache.DeleteAsync(BuildCacheKey(collection.CollectionId), cancellationToken);

                // Delete the collection instance
                await CollectionStore.DeleteAsync(BuildCollectionKey(collection.CollectionId), cancellationToken);

                // Also delete the owner+type lookup key
                await CollectionStore.DeleteAsync(
                    BuildCollectionByOwnerKey(collection.OwnerId, collection.OwnerType, collection.GameServiceId, collection.CollectionType),
                    cancellationToken);

                // Publish collection.deleted lifecycle event per FOUNDATION TENETS
                await _messageBus.TryPublishAsync(
                    "collection.deleted",
                    new CollectionDeletedEvent
                    {
                        CollectionId = collection.CollectionId,
                        OwnerId = collection.OwnerId,
                        OwnerType = collection.OwnerType,
                        CollectionType = collection.CollectionType,
                        GameServiceId = collection.GameServiceId,
                        ContainerId = collection.ContainerId,
                        CreatedAt = collection.CreatedAt,
                        DeletedReason = $"Owner {ownerType} deleted"
                    },
                    cancellationToken: cancellationToken);

                deletedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clean up collection {CollectionId} for {OwnerType} {OwnerId}",
                    collection.CollectionId, ownerType, ownerId);
            }
        }

        _logger.LogInformation("Cleaned up {DeletedCount}/{TotalCount} collections for {OwnerType} {OwnerId}",
            deletedCount, collections.Count, ownerType, ownerId);

        return deletedCount;
    }
}
