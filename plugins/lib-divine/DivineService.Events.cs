using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;

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
    /// Handles analytics.score.updated events for domain-relevant divinity generation.
    /// Maps analytics categories to domain codes and queues divinity generation events.
    /// </summary>
    /// <param name="evt">The analytics score updated event data.</param>
    public async Task HandleAnalyticsScoreUpdatedAsync(AnalyticsScoreUpdatedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.divine", "DivineService.HandleAnalyticsScoreUpdatedAsync");
        // TODO: Map analytics categories to domain codes, queue DivinityEventModel entries
        _logger.LogInformation("Received {Topic} event for game service {GameServiceId}", "analytics.score.updated", evt.GameServiceId);
        await Task.CompletedTask;
    }
}
