using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;

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
        eventConsumer.RegisterHandler<IResourceService, ResourceReferenceRegisteredEvent>(
            "resource.reference.registered",
            async (svc, evt) => await ((ResourceService)svc).HandleReferenceRegisteredAsync(evt));

        eventConsumer.RegisterHandler<IResourceService, ResourceReferenceUnregisteredEvent>(
            "resource.reference.unregistered",
            async (svc, evt) => await ((ResourceService)svc).HandleReferenceUnregisteredAsync(evt));
    }

    /// <summary>
    /// Handles resource.reference.registered events.
    /// Delegates to the RegisterReferenceAsync API for consistent logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleReferenceRegisteredAsync(ResourceReferenceRegisteredEvent evt)
    {
        _logger.LogDebug(
            "Processing resource.reference.registered event: {SourceType}:{SourceId} -> {ResourceType}:{ResourceId}",
            evt.SourceType, evt.SourceId, evt.ResourceType, evt.ResourceId);

        // Delegate to the API method for consistent logic
        var request = new RegisterReferenceRequest
        {
            ResourceType = evt.ResourceType,
            ResourceId = evt.ResourceId,
            SourceType = evt.SourceType,
            SourceId = evt.SourceId
        };

        var (status, response) = await RegisterReferenceAsync(request, CancellationToken.None);

        if (status != StatusCodes.OK || response == null)
        {
            _logger.LogWarning(
                "Failed to register reference from event: {Status}",
                status);
        }
    }

    /// <summary>
    /// Handles resource.reference.unregistered events.
    /// Delegates to the UnregisterReferenceAsync API for consistent logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleReferenceUnregisteredAsync(ResourceReferenceUnregisteredEvent evt)
    {
        _logger.LogDebug(
            "Processing resource.reference.unregistered event: {SourceType}:{SourceId} -> {ResourceType}:{ResourceId}",
            evt.SourceType, evt.SourceId, evt.ResourceType, evt.ResourceId);

        // Delegate to the API method for consistent logic
        var request = new UnregisterReferenceRequest
        {
            ResourceType = evt.ResourceType,
            ResourceId = evt.ResourceId,
            SourceType = evt.SourceType,
            SourceId = evt.SourceId
        };

        var (status, response) = await UnregisterReferenceAsync(request, CancellationToken.None);

        if (status != StatusCodes.OK || response == null)
        {
            _logger.LogWarning(
                "Failed to unregister reference from event: {Status}",
                status);
        }
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
