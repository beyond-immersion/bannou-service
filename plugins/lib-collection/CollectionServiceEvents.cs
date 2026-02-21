using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Collection;

/// <summary>
/// Partial class for CollectionService event handling.
/// Contains event consumer registration and handler implementations for
/// character.deleted and account.deleted cleanup.
/// </summary>
public partial class CollectionService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<ICollectionService, CharacterDeletedEvent>(
            "character.deleted",
            async (svc, evt) => await ((CollectionService)svc).HandleCharacterDeletedAsync(evt));

        eventConsumer.RegisterHandler<ICollectionService, AccountDeletedEvent>(
            "account.deleted",
            async (svc, evt) => await ((CollectionService)svc).HandleAccountDeletedAsync(evt));
    }

    /// <summary>
    /// Handles character.deleted events by cleaning up all character-owned collections.
    /// Deletes inventory containers and cache entries for each collection.
    /// </summary>
    /// <param name="evt">The character deleted event data.</param>
    public async Task HandleCharacterDeletedAsync(CharacterDeletedEvent evt)
    {
        _logger.LogInformation("Handling character.deleted for character {CharacterId}", evt.CharacterId);
        await CleanupCollectionsForOwnerAsync(evt.CharacterId, "character");
    }

    /// <summary>
    /// Handles account.deleted events by cleaning up all account-owned collections.
    /// Deletes inventory containers and cache entries for each collection.
    /// </summary>
    /// <param name="evt">The account deleted event data.</param>
    public async Task HandleAccountDeletedAsync(AccountDeletedEvent evt)
    {
        _logger.LogInformation("Handling account.deleted for account {AccountId}", evt.AccountId);
        await CleanupCollectionsForOwnerAsync(evt.AccountId, "account");
    }

    /// <summary>
    /// Cleans up all collections for a given owner by deleting containers, cache, and instance records.
    /// </summary>
    private async Task CleanupCollectionsForOwnerAsync(Guid ownerId, string ownerType)
    {
        try
        {
            var collections = await CollectionStore.QueryAsync(
                c => c.OwnerId == ownerId && c.OwnerType == ownerType);

            if (collections.Count == 0)
            {
                _logger.LogDebug("No collections found for {OwnerType} {OwnerId}, skipping cleanup", ownerType, ownerId);
                return;
            }

            var deletedCount = 0;
            foreach (var collection in collections)
            {
                try
                {
                    // Delete the inventory container
                    await _inventoryClient.DeleteContainerAsync(
                        new Inventory.DeleteContainerRequest { ContainerId = collection.ContainerId });

                    // Delete the cache entry
                    await CollectionCache.DeleteAsync(BuildCacheKey(collection.CollectionId));

                    // Delete the collection instance
                    await CollectionStore.DeleteAsync(BuildCollectionKey(collection.CollectionId));

                    // Also delete the owner+type lookup key
                    await CollectionStore.DeleteAsync(
                        BuildCollectionByOwnerKey(collection.OwnerId, collection.OwnerType, collection.GameServiceId, collection.CollectionType));

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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clean up collections for {OwnerType} {OwnerId}", ownerType, ownerId);
            await _messageBus.TryPublishErrorAsync(
                "collection",
                $"CleanupCollectionsFor{ownerType}",
                "cleanup_failed",
                ex.Message,
                dependency: null,
                endpoint: $"{ownerType}.deleted",
                details: $"ownerId={ownerId}",
                stack: ex.StackTrace);
        }
    }
}
