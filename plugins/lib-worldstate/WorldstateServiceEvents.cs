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

        eventConsumer.RegisterHandler<IWorldstateService, CalendarTemplateDeletedEvent>(
            "worldstate.calendar-template.deleted",
            async (svc, evt) => await ((WorldstateService)svc).HandleCalendarTemplateDeletedAsync(evt));

        eventConsumer.RegisterHandler<IWorldstateService, WorldstateRatioChangedEvent>(
            "worldstate.ratio-changed",
            async (svc, evt) => await ((WorldstateService)svc).HandleRatioChangedAsync(evt));

        eventConsumer.RegisterHandler<IWorldstateService, RealmConfigDeletedEvent>(
            "worldstate.realm-config.deleted",
            async (svc, evt) => await ((WorldstateService)svc).HandleRealmConfigDeletedAsync(evt));
    }

    /// <summary>
    /// Handles worldstate.calendar-template.updated events.
    /// Invalidates local calendar template cache for cross-node consistency.
    /// When Node A updates a calendar template, Node B receives this event
    /// via RabbitMQ and clears its stale local cache entry.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleCalendarTemplateUpdatedAsync(CalendarTemplateUpdatedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.worldstate", "WorldstateServiceEvents.HandleCalendarTemplateUpdated");

        _calendarTemplateCache.Invalidate(evt.GameServiceId, evt.TemplateCode);
        _logger.LogDebug("Invalidated calendar template cache for {TemplateCode} (game service {GameServiceId})",
            evt.TemplateCode, evt.GameServiceId);
        await Task.CompletedTask; // IMPLEMENTATION TENETS: async methods must contain await
    }

    /// <summary>
    /// Handles worldstate.calendar-template.deleted events.
    /// Invalidates local calendar template cache for cross-node consistency.
    /// When Node A deletes a calendar template, Node B receives this event
    /// via RabbitMQ and clears its stale local cache entry.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleCalendarTemplateDeletedAsync(CalendarTemplateDeletedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.worldstate", "WorldstateServiceEvents.HandleCalendarTemplateDeleted");

        _calendarTemplateCache.Invalidate(evt.GameServiceId, evt.TemplateCode);
        _logger.LogDebug("Invalidated calendar template cache for deleted template {TemplateCode} (game service {GameServiceId})",
            evt.TemplateCode, evt.GameServiceId);
        await Task.CompletedTask; // IMPLEMENTATION TENETS: async methods must contain await
    }

    /// <summary>
    /// Handles worldstate.ratio-changed events.
    /// Invalidates local realm clock cache for cross-node consistency.
    /// When Node A changes a realm's time ratio via SetTimeRatio, Node B receives
    /// this event via RabbitMQ and clears its stale clock cache entry so the next
    /// read fetches fresh data from Redis.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleRatioChangedAsync(WorldstateRatioChangedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.worldstate", "WorldstateServiceEvents.HandleRatioChanged");

        _realmClockCache.Invalidate(evt.RealmId);
        _logger.LogDebug("Invalidated realm clock cache for realm {RealmId} due to ratio change ({PreviousRatio} -> {NewRatio})",
            evt.RealmId, evt.PreviousRatio, evt.NewRatio);
        await Task.CompletedTask; // IMPLEMENTATION TENETS: async methods must contain await
    }

    /// <summary>
    /// Handles worldstate.realm-config.deleted events.
    /// Invalidates local realm clock cache for cross-node consistency.
    /// When Node A deletes a realm's worldstate configuration (via CleanupByRealm),
    /// Node B receives this event via RabbitMQ and clears its stale clock cache entry.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleRealmConfigDeletedAsync(RealmConfigDeletedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.worldstate", "WorldstateServiceEvents.HandleRealmConfigDeleted");

        _realmClockCache.Invalidate(evt.RealmId);
        _logger.LogDebug("Invalidated realm clock cache for realm {RealmId} due to realm config deletion",
            evt.RealmId);
        await Task.CompletedTask; // IMPLEMENTATION TENETS: async methods must contain await
    }
}
