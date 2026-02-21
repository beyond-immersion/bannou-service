using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;

namespace BeyondImmersion.BannouService.Resource;

/// <summary>
/// Partial class for ResourceService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
public partial class ResourceService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        // No event subscriptions remaining after reference tracking migration to direct API calls
    }

    /// <summary>
    /// Maintains the callback index when defining cleanup callbacks.
    /// Must be called after saving the callback definition.
    /// </summary>
    private async Task MaintainCallbackIndexAsync(
        string resourceType,
        string sourceType,
        CancellationToken cancellationToken)
    {
        var cacheStore = _stateStoreFactory.GetCacheableStore<string>(StateStoreDefinitions.ResourceCleanup);

        // Add to per-resource-type index
        var indexKey = $"callback-index:{resourceType}";
        await cacheStore.AddToSetAsync(indexKey, sourceType, cancellationToken: cancellationToken);

        // Add to master resource type index (for listing all callbacks)
        await cacheStore.AddToSetAsync(MasterResourceTypeIndexKey, resourceType, cancellationToken: cancellationToken);
    }
}
