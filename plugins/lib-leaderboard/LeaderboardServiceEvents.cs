using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
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
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleScoreUpdatedAsync(AnalyticsScoreUpdatedEvent evt)
    {
        try
        {
            var definition = await GetDefinitionForAnalyticsEventAsync(
                evt.GameServiceId,
                evt.ScoreType,
                "scoreType",
                "event:analytics.score.updated",
                CancellationToken.None);
            if (definition == null)
            {
                return;
            }

            var entityType = MapToEntityType(evt.EntityType);
            if (definition.EntityTypes != null && !definition.EntityTypes.Contains(entityType))
            {
                _logger.LogWarning(
                    "Analytics score update entity type {EntityType} not allowed by leaderboard {LeaderboardId}",
                    evt.EntityType,
                    definition.LeaderboardId);
                return;
            }

            var score = definition.UpdateMode == UpdateMode.Increment ? evt.Delta : evt.NewValue;
            var (status, _) = await SubmitScoreAsync(new SubmitScoreRequest
            {
                GameServiceId = evt.GameServiceId,
                LeaderboardId = definition.LeaderboardId,
                EntityId = evt.EntityId,
                EntityType = entityType,
                Score = score
            }, CancellationToken.None);

            if (status != StatusCodes.OK)
            {
                var message = "Failed to submit analytics score update to leaderboard";
                _logger.LogError(
                    "{Message} (LeaderboardId: {LeaderboardId}, Status: {Status}, EntityType: {EntityType}, EntityId: {EntityId})",
                    message,
                    definition.LeaderboardId,
                    status,
                    evt.EntityType,
                    evt.EntityId);
                await _messageBus.TryPublishErrorAsync(
                    "leaderboard",
                    "HandleScoreUpdated",
                    "leaderboard_submit_failed",
                    message,
                    dependency: null,
                    endpoint: "event:analytics.score.updated",
                    details: $"leaderboardId:{definition.LeaderboardId};status:{status}",
                    stack: null,
                    cancellationToken: CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error handling analytics.score.updated event for {EntityType}:{EntityId} ({ScoreType})",
                evt.EntityType,
                evt.EntityId,
                evt.ScoreType);
            await _messageBus.TryPublishErrorAsync(
                "leaderboard",
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
    /// Handles analytics.rating.updated events.
    /// </summary>
    /// <param name="evt">The event data.</param>
    public async Task HandleRatingUpdatedAsync(AnalyticsRatingUpdatedEvent evt)
    {
        try
        {
            var definition = await GetDefinitionForAnalyticsEventAsync(
                evt.GameServiceId,
                evt.RatingType,
                "ratingType",
                "event:analytics.rating.updated",
                CancellationToken.None);
            if (definition == null)
            {
                return;
            }

            var entityType = MapToEntityType(evt.EntityType);
            if (definition.EntityTypes != null && !definition.EntityTypes.Contains(entityType))
            {
                _logger.LogWarning(
                    "Analytics rating update entity type {EntityType} not allowed by leaderboard {LeaderboardId}",
                    evt.EntityType,
                    definition.LeaderboardId);
                return;
            }

            if (definition.UpdateMode != UpdateMode.Replace)
            {
                _logger.LogWarning(
                    "Rating updates require Replace mode but leaderboard {LeaderboardId} uses {UpdateMode}",
                    definition.LeaderboardId,
                    definition.UpdateMode);
                return;
            }

            var (status, _) = await SubmitScoreAsync(new SubmitScoreRequest
            {
                GameServiceId = evt.GameServiceId,
                LeaderboardId = definition.LeaderboardId,
                EntityId = evt.EntityId,
                EntityType = entityType,
                Score = evt.NewRating
            }, CancellationToken.None);

            if (status != StatusCodes.OK)
            {
                var message = "Failed to submit analytics rating update to leaderboard";
                _logger.LogError(
                    "{Message} (LeaderboardId: {LeaderboardId}, Status: {Status}, EntityType: {EntityType}, EntityId: {EntityId})",
                    message,
                    definition.LeaderboardId,
                    status,
                    evt.EntityType,
                    evt.EntityId);
                await _messageBus.TryPublishErrorAsync(
                    "leaderboard",
                    "HandleRatingUpdated",
                    "leaderboard_submit_failed",
                    message,
                    dependency: null,
                    endpoint: "event:analytics.rating.updated",
                    details: $"leaderboardId:{definition.LeaderboardId};status:{status}",
                    stack: null,
                    cancellationToken: CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error handling analytics.rating.updated event for {EntityType}:{EntityId} ({RatingType})",
                evt.EntityType,
                evt.EntityId,
                evt.RatingType);
            await _messageBus.TryPublishErrorAsync(
                "leaderboard",
                "HandleRatingUpdated",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "event:analytics.rating.updated",
                details: $"ratingType:{evt.RatingType}",
                stack: ex.StackTrace,
                cancellationToken: CancellationToken.None);
        }
    }

    private async Task<LeaderboardDefinitionData?> GetDefinitionForAnalyticsEventAsync(
        Guid gameServiceId,
        string eventType,
        string metadataKey,
        string eventTopic,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            return null;
        }

        var definitionStore = _stateStoreFactory.GetStore<LeaderboardDefinitionData>(StateStoreDefinitions.LeaderboardDefinition);
        var normalized = eventType.Trim();
        var candidateIds = normalized.Equals(normalized.ToLowerInvariant(), StringComparison.Ordinal)
            ? new[] { normalized }
            : new[] { normalized, normalized.ToLowerInvariant() };

        foreach (var candidate in candidateIds)
        {
            var candidateKey = GetDefinitionKey(gameServiceId, candidate);
            var direct = await definitionStore.GetAsync(candidateKey, cancellationToken);
            if (direct != null)
            {
                return direct;
            }
        }

        var indexKey = GetDefinitionIndexKey(gameServiceId);
        var definitionIds = await definitionStore.GetSetAsync<string>(indexKey, cancellationToken);
        if (definitionIds.Count == 0)
        {
            return null;
        }

        LeaderboardDefinitionData? matched = null;
        foreach (var leaderboardId in definitionIds)
        {
            var key = GetDefinitionKey(gameServiceId, leaderboardId);
            var definition = await definitionStore.GetAsync(key, cancellationToken);
            if (definition == null)
            {
                continue;
            }

            var metadata = MetadataHelper.ConvertToReadOnlyDictionary(definition.Metadata);
            if (!MetadataHelper.TryGetString(metadata, metadataKey, out var mappedType))
            {
                continue;
            }

            if (!string.Equals(mappedType, eventType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (matched != null)
            {
                _logger.LogWarning(
                    "Multiple leaderboard definitions match analytics event type {EventType} for game service {GameServiceId}",
                    eventType,
                    gameServiceId);
                return null;
            }

            matched = definition;
        }

        return matched;
    }

    private static EntityType MapToEntityType(AnalyticsScoreUpdatedEventEntityType entityType)
        => entityType switch
        {
            AnalyticsScoreUpdatedEventEntityType.Account => EntityType.Account,
            AnalyticsScoreUpdatedEventEntityType.Character => EntityType.Character,
            AnalyticsScoreUpdatedEventEntityType.Guild => EntityType.Guild,
            AnalyticsScoreUpdatedEventEntityType.Actor => EntityType.Actor,
            AnalyticsScoreUpdatedEventEntityType.Custom => EntityType.Custom,
            _ => EntityType.Custom
        };

    private static EntityType MapToEntityType(AnalyticsRatingUpdatedEventEntityType entityType)
        => entityType switch
        {
            AnalyticsRatingUpdatedEventEntityType.Account => EntityType.Account,
            AnalyticsRatingUpdatedEventEntityType.Character => EntityType.Character,
            AnalyticsRatingUpdatedEventEntityType.Guild => EntityType.Guild,
            AnalyticsRatingUpdatedEventEntityType.Actor => EntityType.Actor,
            AnalyticsRatingUpdatedEventEntityType.Custom => EntityType.Custom,
            _ => EntityType.Custom
        };
}
