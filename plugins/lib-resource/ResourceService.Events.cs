using BeyondImmersion.BannouService.Events;

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
}
