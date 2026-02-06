using BeyondImmersion.BannouService.Events;

namespace BeyondImmersion.BannouService.Puppetmaster;

/// <summary>
/// Partial class for PuppetmasterService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
public partial class PuppetmasterService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<IPuppetmasterService, RealmCreatedEvent>(
            "realm.created",
            async (svc, evt) => await ((PuppetmasterService)svc).HandleRealmCreatedAsync(evt));

    }

    /// <summary>
    /// Handles realm.created events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleRealmCreatedAsync(RealmCreatedEvent evt)
    {
        // TODO: Implement realm.created event handling
        _logger.LogInformation("[EVENT] Received realm.created event");
        return Task.CompletedTask;
    }
}
