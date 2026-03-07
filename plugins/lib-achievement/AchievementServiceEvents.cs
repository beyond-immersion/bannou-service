using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

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
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleScoreUpdatedAsync(AnalyticsScoreUpdatedEvent evt)
    {
        try
        {
            using var activity = _telemetryProvider.StartActivity("bannou.achievement", "AchievementService.HandleScoreUpdatedAsync");
            var definitions = await LoadAchievementDefinitionsAsync(evt.GameServiceId, CancellationToken.None);
            if (definitions.Count == 0)
            {
                return;
            }

            if (!TryConvertDeltaToIncrement(evt.Delta, out var increment))
            {
                if (evt.Delta <= 0)
                {
                    return;
                }

                var message = "Analytics score delta must be a positive integer for progressive achievements";
                _logger.LogError(
                    "{Message} (ScoreType: {ScoreType}, Delta: {Delta})",
                    message,
                    evt.ScoreType,
                    evt.Delta);
                await _messageBus.TryPublishErrorAsync(
                    "achievement",
                    "HandleScoreUpdated",
                    "achievement_invalid_progress_increment",
                    message,
                    dependency: null,
                    endpoint: "event:analytics.score.updated",
                    details: $"scoreType:{evt.ScoreType};delta:{evt.Delta}",
                    stack: null,
                    cancellationToken: CancellationToken.None);
                return;
            }

            // Cast Analytics.EntityType to Achievement.EntityType (same enum values, different namespaces)
            var entityType = (EntityType)evt.EntityType;

            foreach (var definition in definitions)
            {
                if (!definition.IsActive)
                {
                    continue;
                }

                if (definition.AchievementType != AchievementType.Progressive || !definition.ProgressTarget.HasValue)
                {
                    continue;
                }

                if (definition.EntityTypes != null && !definition.EntityTypes.Contains(entityType))
                {
                    continue;
                }

                // FOUNDATION TENETS - T29 compliance: typed field instead of metadata bag
                if (string.IsNullOrEmpty(definition.ScoreType))
                {
                    continue;
                }

                if (!string.Equals(definition.ScoreType, evt.ScoreType, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var (status, _) = await UpdateAchievementProgressAsync(new UpdateAchievementProgressRequest
                {
                    GameServiceId = evt.GameServiceId,
                    AchievementId = definition.AchievementId,
                    EntityId = evt.EntityId,
                    EntityType = entityType,
                    Increment = increment
                }, CancellationToken.None);

                if (status == StatusCodes.InternalServerError)
                {
                    var message = "Failed to update achievement progress from analytics score update";
                    _logger.LogError(
                        "{Message} (AchievementId: {AchievementId}, Status: {Status}, ScoreType: {ScoreType})",
                        message,
                        definition.AchievementId,
                        status,
                        evt.ScoreType);
                    await _messageBus.TryPublishErrorAsync(
                        "achievement",
                        "HandleScoreUpdated",
                        "achievement_progress_update_failed",
                        message,
                        dependency: null,
                        endpoint: "event:analytics.score.updated",
                        details: $"achievementId:{definition.AchievementId};status:{status}",
                        stack: null,
                        cancellationToken: CancellationToken.None);
                }
                else if (status != StatusCodes.OK)
                {
                    _logger.LogDebug(
                        "Achievement progress update returned expected status {Status} for {AchievementId}",
                        status,
                        definition.AchievementId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error handling analytics.score.updated event for {EntityType}:{EntityId}",
                evt.EntityType,
                evt.EntityId);
            await _messageBus.TryPublishErrorAsync(
                "achievement",
                "HandleScoreUpdated",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "event:analytics.score.updated",
                details: $"scoreType:{evt.ScoreType}",
                stack: ex.StackTrace,
                cancellationToken: CancellationToken.None);
        }
    }

    /// <summary>
    /// Handles analytics.milestone.reached events.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleMilestoneReachedAsync(AnalyticsMilestoneReachedEvent evt)
    {
        try
        {
            using var activity = _telemetryProvider.StartActivity("bannou.achievement", "AchievementService.HandleMilestoneReachedAsync");
            var definitions = await LoadAchievementDefinitionsAsync(evt.GameServiceId, CancellationToken.None);
            if (definitions.Count == 0)
            {
                return;
            }

            // Cast Analytics.EntityType to Achievement.EntityType (same enum values, different namespaces)
            var entityType = (EntityType)evt.EntityType;

            foreach (var definition in definitions)
            {
                if (!definition.IsActive)
                {
                    continue;
                }

                if (definition.AchievementType == AchievementType.Progressive)
                {
                    continue;
                }

                if (definition.EntityTypes != null && !definition.EntityTypes.Contains(entityType))
                {
                    continue;
                }

                // FOUNDATION TENETS - T29 compliance: typed fields instead of metadata bag
                if (string.IsNullOrEmpty(definition.MilestoneType))
                {
                    continue;
                }

                if (!string.Equals(definition.MilestoneType, evt.MilestoneType, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (definition.MilestoneValue.HasValue &&
                    !IsCloseTo(evt.MilestoneValue, definition.MilestoneValue.Value))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(definition.MilestoneName) &&
                    !string.Equals(definition.MilestoneName, evt.MilestoneName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var (status, _) = await UnlockAchievementAsync(new UnlockAchievementRequest
                {
                    GameServiceId = evt.GameServiceId,
                    AchievementId = definition.AchievementId,
                    EntityId = evt.EntityId,
                    EntityType = entityType
                }, CancellationToken.None);

                if (status == StatusCodes.InternalServerError)
                {
                    var message = "Failed to unlock achievement from analytics milestone event";
                    _logger.LogError(
                        "{Message} (AchievementId: {AchievementId}, Status: {Status}, MilestoneType: {MilestoneType})",
                        message,
                        definition.AchievementId,
                        status,
                        evt.MilestoneType);
                    await _messageBus.TryPublishErrorAsync(
                        "achievement",
                        "HandleMilestoneReached",
                        "achievement_unlock_failed",
                        message,
                        dependency: null,
                        endpoint: "event:analytics.milestone.reached",
                        details: $"achievementId:{definition.AchievementId};status:{status}",
                        stack: null,
                        cancellationToken: CancellationToken.None);
                }
                else if (status != StatusCodes.OK)
                {
                    _logger.LogDebug(
                        "Achievement unlock returned expected status {Status} for {AchievementId}",
                        status,
                        definition.AchievementId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error handling analytics.milestone.reached event for {EntityType}:{EntityId}",
                evt.EntityType,
                evt.EntityId);
            await _messageBus.TryPublishErrorAsync(
                "achievement",
                "HandleMilestoneReached",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "event:analytics.milestone.reached",
                details: $"milestoneType:{evt.MilestoneType}",
                stack: ex.StackTrace,
                cancellationToken: CancellationToken.None);
        }
    }

    /// <summary>
    /// Handles leaderboard.rank.changed events.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleRankChangedAsync(LeaderboardRankChangedEvent evt)
    {
        try
        {
            using var activity = _telemetryProvider.StartActivity("bannou.achievement", "AchievementService.HandleRankChangedAsync");
            var definitions = await LoadAchievementDefinitionsAsync(evt.GameServiceId, CancellationToken.None);
            if (definitions.Count == 0)
            {
                return;
            }

            // Cast Leaderboard.EntityType to Achievement.EntityType (same enum values, different namespaces)
            var entityType = (EntityType)evt.EntityType;

            foreach (var definition in definitions)
            {
                if (!definition.IsActive)
                {
                    continue;
                }

                if (definition.AchievementType == AchievementType.Progressive)
                {
                    continue;
                }

                if (definition.EntityTypes != null && !definition.EntityTypes.Contains(entityType))
                {
                    continue;
                }

                // FOUNDATION TENETS - T29 compliance: typed fields instead of metadata bag
                if (string.IsNullOrEmpty(definition.LeaderboardId))
                {
                    continue;
                }

                if (!string.Equals(definition.LeaderboardId, evt.LeaderboardId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!definition.RankThreshold.HasValue || definition.RankThreshold.Value <= 0)
                {
                    var message = "Leaderboard rank achievement is missing a valid rankThreshold value";
                    _logger.LogError(
                        "{Message} (AchievementId: {AchievementId}, LeaderboardId: {LeaderboardId})",
                        message,
                        definition.AchievementId,
                        evt.LeaderboardId);
                    await _messageBus.TryPublishErrorAsync(
                        "achievement",
                        "HandleRankChanged",
                        "achievement_rank_threshold_invalid",
                        message,
                        dependency: null,
                        endpoint: "event:leaderboard.rank.changed",
                        details: $"achievementId:{definition.AchievementId};leaderboardId:{evt.LeaderboardId}",
                        stack: null,
                        cancellationToken: CancellationToken.None);
                    continue;
                }

                if (evt.NewRank > definition.RankThreshold.Value)
                {
                    continue;
                }

                var (status, _) = await UnlockAchievementAsync(new UnlockAchievementRequest
                {
                    GameServiceId = evt.GameServiceId,
                    AchievementId = definition.AchievementId,
                    EntityId = evt.EntityId,
                    EntityType = entityType
                }, CancellationToken.None);

                if (status == StatusCodes.InternalServerError)
                {
                    var message = "Failed to unlock achievement from leaderboard rank change";
                    _logger.LogError(
                        "{Message} (AchievementId: {AchievementId}, Status: {Status}, LeaderboardId: {LeaderboardId})",
                        message,
                        definition.AchievementId,
                        status,
                        evt.LeaderboardId);
                    await _messageBus.TryPublishErrorAsync(
                        "achievement",
                        "HandleRankChanged",
                        "achievement_unlock_failed",
                        message,
                        dependency: null,
                        endpoint: "event:leaderboard.rank.changed",
                        details: $"achievementId:{definition.AchievementId};status:{status}",
                        stack: null,
                        cancellationToken: CancellationToken.None);
                }
                else if (status != StatusCodes.OK)
                {
                    _logger.LogDebug(
                        "Achievement unlock returned expected status {Status} for {AchievementId}",
                        status,
                        definition.AchievementId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error handling leaderboard.rank.changed event for {EntityType}:{EntityId} on {LeaderboardId}",
                evt.EntityType,
                evt.EntityId,
                evt.LeaderboardId);
            await _messageBus.TryPublishErrorAsync(
                "achievement",
                "HandleRankChanged",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "event:leaderboard.rank.changed",
                details: $"leaderboardId:{evt.LeaderboardId}",
                stack: ex.StackTrace,
                cancellationToken: CancellationToken.None);
        }
    }

    private async Task<IReadOnlyList<AchievementDefinitionData>> LoadAchievementDefinitionsAsync(
        Guid gameServiceId,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.achievement", "AchievementService.LoadAchievementDefinitionsAsync");
        var indexKey = BuildDefinitionIndexKey(gameServiceId);
        var achievementIds = await _definitionStore.GetSetAsync<string>(indexKey, cancellationToken);

        if (achievementIds.Count == 0)
        {
            return Array.Empty<AchievementDefinitionData>();
        }

        var definitions = new List<AchievementDefinitionData>();
        foreach (var achievementId in achievementIds)
        {
            var key = BuildDefinitionKey(gameServiceId, achievementId);
            var definition = await _definitionStore.GetAsync(key, cancellationToken);
            if (definition != null)
            {
                definitions.Add(definition);
            }
        }

        return definitions;
    }


    private static bool TryConvertDeltaToIncrement(double delta, out int increment)
    {
        increment = 0;
        if (delta <= 0)
        {
            return false;
        }

        var rounded = Math.Round(delta);
        if (Math.Abs(delta - rounded) > 0.000001 || rounded > int.MaxValue)
        {
            return false;
        }

        increment = (int)rounded;
        return true;
    }

    private static bool IsCloseTo(double actual, double expected)
        => Math.Abs(actual - expected) < 0.000001;

}
