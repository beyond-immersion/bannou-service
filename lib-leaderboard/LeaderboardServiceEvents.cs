using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Leaderboard;

/// <summary>
/// Partial class for LeaderboardService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
public partial class LeaderboardService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<ILeaderboardService, AnalyticsScoreUpdatedEvent>(
            "analytics.score.updated",
            async (svc, evt) => await ((LeaderboardService)svc).HandleScoreUpdatedAsync(evt));

        eventConsumer.RegisterHandler<ILeaderboardService, AnalyticsRatingUpdatedEvent>(
            "analytics.rating.updated",
            async (svc, evt) => await ((LeaderboardService)svc).HandleRatingUpdatedAsync(evt));

    }

    /// <summary>
    /// Handles analytics.score.updated events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleScoreUpdatedAsync(AnalyticsScoreUpdatedEvent evt)
    {
        // TODO: Implement analytics.score.updated event handling
        _logger.LogInformation("[EVENT] Received analytics.score.updated event");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles analytics.rating.updated events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleRatingUpdatedAsync(AnalyticsRatingUpdatedEvent evt)
    {
        // TODO: Implement analytics.rating.updated event handling
        _logger.LogInformation("[EVENT] Received analytics.rating.updated event");
        return Task.CompletedTask;
    }
}
