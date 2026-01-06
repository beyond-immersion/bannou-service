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
    public async Task HandleScoreUpdatedAsync(AnalyticsScoreUpdatedEvent evt)
    {
        // TODO: Implement analytics.score.updated event handling
        _logger.LogInformation("Received analytics.score.updated event for {EntityType}:{EntityId} ({ScoreType})",
            evt.EntityType, evt.EntityId, evt.ScoreType);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Handles analytics.rating.updated events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleRatingUpdatedAsync(AnalyticsRatingUpdatedEvent evt)
    {
        // TODO: Implement analytics.rating.updated event handling
        _logger.LogInformation("Received analytics.rating.updated event for {EntityType}:{EntityId} ({RatingType})",
            evt.EntityType, evt.EntityId, evt.RatingType);
        await Task.CompletedTask;
    }
}
