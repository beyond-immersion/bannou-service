using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Achievement;

/// <summary>
/// Partial class for AchievementService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
public partial class AchievementService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<IAchievementService, AnalyticsScoreUpdatedEvent>(
            "analytics.score.updated",
            async (svc, evt) => await ((AchievementService)svc).HandleScoreUpdatedAsync(evt));

        eventConsumer.RegisterHandler<IAchievementService, AnalyticsMilestoneReachedEvent>(
            "analytics.milestone.reached",
            async (svc, evt) => await ((AchievementService)svc).HandleMilestoneReachedAsync(evt));

        eventConsumer.RegisterHandler<IAchievementService, LeaderboardRankChangedEvent>(
            "leaderboard.rank.changed",
            async (svc, evt) => await ((AchievementService)svc).HandleRankChangedAsync(evt));

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
    /// Handles analytics.milestone.reached events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleMilestoneReachedAsync(AnalyticsMilestoneReachedEvent evt)
    {
        // TODO: Implement analytics.milestone.reached event handling
        _logger.LogInformation("[EVENT] Received analytics.milestone.reached event");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles leaderboard.rank.changed events.
    /// TODO: Implement event handling logic.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public Task HandleRankChangedAsync(LeaderboardRankChangedEvent evt)
    {
        // TODO: Implement leaderboard.rank.changed event handling
        _logger.LogInformation("[EVENT] Received leaderboard.rank.changed event");
        return Task.CompletedTask;
    }
}
