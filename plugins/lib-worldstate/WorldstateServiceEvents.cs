using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Worldstate;

/// <summary>
/// Partial class for WorldstateService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
public partial class WorldstateService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<IWorldstateService, CalendarTemplateUpdatedEvent>(
            "worldstate.calendar-template.updated",
            async (svc, evt) => await ((WorldstateService)svc).HandleCalendarTemplateUpdatedAsync(evt));

    }

    /// <summary>
    /// Handles worldstate.calendar-template.updated events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleCalendarTemplateUpdatedAsync(CalendarTemplateUpdatedEvent evt)
    {
        // TODO: Implement worldstate.calendar-template.updated event handling
        _logger.LogInformation("Received {Topic} event", "worldstate.calendar-template.updated");
        return Task.CompletedTask;
    }
}
