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
    /// Invalidates local calendar template cache for cross-node consistency.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleCalendarTemplateUpdatedAsync(CalendarTemplateUpdatedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.worldstate", "WorldstateServiceEvents.HandleCalendarTemplateUpdated");

        // TODO: Implement cross-node cache invalidation when CalendarTemplateCache is built
        _logger.LogDebug("Received calendar template updated event for {TemplateCode}", evt.TemplateCode);
        await Task.CompletedTask; // IMPLEMENTATION TENETS: async methods must contain await
    }
}
