using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Environment;

/// <summary>
/// Partial class for EnvironmentService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
public partial class EnvironmentService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<IEnvironmentService, WorldstatePeriodChangedEvent>(
            "worldstate.period-changed",
            async (svc, evt) => await ((EnvironmentService)svc).HandlePeriodChangedAsync(evt));

        eventConsumer.RegisterHandler<IEnvironmentService, WorldstateSeasonChangedEvent>(
            "worldstate.season-changed",
            async (svc, evt) => await ((EnvironmentService)svc).HandleSeasonChangedAsync(evt));

        eventConsumer.RegisterHandler<IEnvironmentService, WorldstateDayChangedEvent>(
            "worldstate.day-changed",
            async (svc, evt) => await ((EnvironmentService)svc).HandleDayChangedAsync(evt));

        eventConsumer.RegisterHandler<IEnvironmentService, LocationCreatedEvent>(
            "location.created",
            async (svc, evt) => await ((EnvironmentService)svc).HandleLocationCreatedAsync(evt));

        eventConsumer.RegisterHandler<IEnvironmentService, LocationUpdatedEvent>(
            "location.updated",
            async (svc, evt) => await ((EnvironmentService)svc).HandleLocationUpdatedAsync(evt));

        eventConsumer.RegisterHandler<IEnvironmentService, ClimateTemplateUpdatedEvent>(
            "environment.climate-template.updated",
            async (svc, evt) => await ((EnvironmentService)svc).HandleClimateTemplateUpdatedCacheInvalidationAsync(evt));

        eventConsumer.RegisterHandler<IEnvironmentService, ClimateTemplateDeletedEvent>(
            "environment.climate-template.deleted",
            async (svc, evt) => await ((EnvironmentService)svc).HandleClimateTemplateDeletedCacheInvalidationAsync(evt));

        eventConsumer.RegisterHandler<IEnvironmentService, EnvironmentConditionsChangedEvent>(
            "environment.conditions.changed",
            async (svc, evt) => await ((EnvironmentService)svc).HandleConditionsChangedCacheInvalidationAsync(evt));

    }

    /// <summary>
    /// Handles worldstate.period-changed events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandlePeriodChangedAsync(WorldstatePeriodChangedEvent evt)
    {
        // TODO: Implement worldstate.period-changed event handling
        _logger.LogInformation("Received {Topic} event", "worldstate.period-changed");
        return Task.CompletedTask;
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

    /// <summary>
    /// Handles worldstate.day-changed events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleDayChangedAsync(WorldstateDayChangedEvent evt)
    {
        // TODO: Implement worldstate.day-changed event handling
        _logger.LogInformation("Received {Topic} event", "worldstate.day-changed");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles location.created events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleLocationCreatedAsync(LocationCreatedEvent evt)
    {
        // TODO: Implement location.created event handling
        _logger.LogInformation("Received {Topic} event", "location.created");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles location.updated events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleLocationUpdatedAsync(LocationUpdatedEvent evt)
    {
        // TODO: Implement location.updated event handling
        _logger.LogInformation("Received {Topic} event", "location.updated");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles environment.climate-template.updated events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleClimateTemplateUpdatedCacheInvalidationAsync(ClimateTemplateUpdatedEvent evt)
    {
        // TODO: Implement environment.climate-template.updated event handling
        _logger.LogInformation("Received {Topic} event", "environment.climate-template.updated");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles environment.climate-template.deleted events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleClimateTemplateDeletedCacheInvalidationAsync(ClimateTemplateDeletedEvent evt)
    {
        // TODO: Implement environment.climate-template.deleted event handling
        _logger.LogInformation("Received {Topic} event", "environment.climate-template.deleted");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles environment.conditions.changed events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleConditionsChangedCacheInvalidationAsync(EnvironmentConditionsChangedEvent evt)
    {
        // TODO: Implement environment.conditions.changed event handling
        _logger.LogInformation("Received {Topic} event", "environment.conditions.changed");
        return Task.CompletedTask;
    }
}
