using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Transit;

/// <summary>
/// Partial class for TransitService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
public partial class TransitService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<ITransitService, WorldstateSeasonChangedEvent>(
            "worldstate.season-changed",
            async (svc, evt) => await ((TransitService)svc).HandleSeasonChangedAsync(evt));

    }

    /// <summary>
    /// Handles worldstate.season-changed events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleSeasonChangedAsync(WorldstateSeasonChangedEvent evt)
    {
        // TODO: Implement worldstate.season-changed event handling
        _logger.LogInformation("Received {Topic} event", "worldstate.season-changed");
        return Task.CompletedTask;
    }
}
