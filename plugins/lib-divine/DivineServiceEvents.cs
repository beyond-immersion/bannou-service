using BeyondImmersion.BannouService.Events;

namespace BeyondImmersion.BannouService.Divine;

/// <summary>
/// Partial class for DivineService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
public partial class DivineService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<IDivineService, AnalyticsScoreUpdatedEvent>(
            "analytics.score.updated",
            async (svc, evt) => await ((DivineService)svc).HandleAnalyticsScoreUpdatedAsync(evt));

    }

    /// <summary>
    /// Handles analytics.score.updated events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleAnalyticsScoreUpdatedAsync(AnalyticsScoreUpdatedEvent evt)
    {
        // TODO: Implement analytics.score.updated event handling
        _logger.LogInformation("[EVENT] Received analytics.score.updated event");
        return Task.CompletedTask;
    }
}
