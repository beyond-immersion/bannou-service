using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Status;

/// <summary>
/// Partial class for StatusService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
public partial class StatusService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<IStatusService, SeedCapabilityUpdatedEvent>(
            "seed.capability.updated",
            async (svc, evt) => await ((StatusService)svc).HandleSeedCapabilityUpdatedAsync(evt));

    }

    /// <summary>
    /// Handles seed.capability.updated events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleSeedCapabilityUpdatedAsync(SeedCapabilityUpdatedEvent evt)
    {
        // TODO: Implement seed.capability.updated event handling
        _logger.LogInformation("Received {Topic} event", "seed.capability.updated");
        return Task.CompletedTask;
    }
}
