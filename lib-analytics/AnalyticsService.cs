using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("lib-analytics.tests")]

namespace BeyondImmersion.BannouService.Analytics;

/// <summary>
/// Implementation of the Analytics service.
/// Provides event ingestion, entity statistics, Glicko-2 skill ratings, and controller history tracking.
/// </summary>
/// <remarks>
/// <para>
/// <b>FOUNDATION TENETS - PARTIAL CLASS REQUIRED:</b> This class MUST remain a partial class.
/// Generated code (event handlers, permissions) is placed in companion partial classes.
/// </para>
/// <para>
/// Standard structure:
/// <list type="bullet">
///   <item>AnalyticsService.cs (this file) - Business logic</item>
///   <item>AnalyticsServiceEvents.cs - Event consumer handlers (generated)</item>
///   <item>Generated/AnalyticsPermissionRegistration.cs - Permission registration (generated)</item>
/// </list>
/// </para>
/// </remarks>
[BannouService("analytics", typeof(IAnalyticsService), lifetime: ServiceLifetime.Scoped)]
public partial class AnalyticsService : IAnalyticsService
{
    private readonly IMessageBus _messageBus;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly ILogger<AnalyticsService> _logger;
    private readonly AnalyticsServiceConfiguration _configuration;

    // State store key prefixes
    private const string SUMMARY_INDEX_PREFIX = "analytics-summary-index";
    private const string CONTROLLER_INDEX_PREFIX = "analytics-controller-index";

    // Glicko-2 scale conversion constant
    private const double GlickoScale = 173.7178;

    /// <summary>
    /// Initializes a new instance of the AnalyticsService.
    /// </summary>
    public AnalyticsService(
        IMessageBus messageBus,
        IStateStoreFactory stateStoreFactory,
        ILogger<AnalyticsService> logger,
        AnalyticsServiceConfiguration configuration,
        IEventConsumer eventConsumer)
    {
        _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
        _stateStoreFactory = stateStoreFactory ?? throw new ArgumentNullException(nameof(stateStoreFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

        RegisterEventConsumers(eventConsumer);
    }

    /// <summary>
    /// Generates a composite key for entity data using polymorphic pattern.
    /// Format: gameServiceId:entityType:entityId
    /// </summary>
    private static string GetEntityKey(Guid gameServiceId, EntityType entityType, Guid entityId)
        => $"{gameServiceId}:{entityType}:{entityId}";

    /// <summary>
    /// Generates a key for skill rating data.
    /// Format: gameServiceId:ratingType:entityType:entityId
    /// </summary>
    private static string GetRatingKey(Guid gameServiceId, string ratingType, EntityType entityType, Guid entityId)
        => $"{gameServiceId}:{ratingType}:{entityType}:{entityId}";

    /// <summary>
    /// Generates a key for controller history.
    /// Format: gameServiceId:controller:accountId:timestamp
    /// </summary>
    private static string GetControllerKey(Guid gameServiceId, Guid accountId, DateTimeOffset timestamp)
        => $"{gameServiceId}:controller:{accountId}:{timestamp:o}";

    /// <summary>
    /// Generates the key for the entity summary index.
    /// Format: analytics-summary-index:gameServiceId
    /// </summary>
    private static string GetSummaryIndexKey(Guid gameServiceId)
        => $"{SUMMARY_INDEX_PREFIX}:{gameServiceId}";

    /// <summary>
    /// Generates the key for the controller history index.
    /// Format: analytics-controller-index:gameServiceId
    /// </summary>
    private static string GetControllerIndexKey(Guid gameServiceId)
        => $"{CONTROLLER_INDEX_PREFIX}:{gameServiceId}";

    /// <summary>
    /// Generates the key for controller history by account.
    /// Format: analytics-controller-index:gameServiceId:account:accountId
    /// </summary>
    private static string GetControllerAccountIndexKey(Guid gameServiceId, Guid accountId)
        => $"{CONTROLLER_INDEX_PREFIX}:{gameServiceId}:account:{accountId}";

    /// <summary>
    /// Implementation of IngestEvent operation.
    /// Ingests a single analytics event and updates entity summaries.
    /// </summary>
    public async Task<(StatusCodes, IngestEventResponse?)> IngestEventAsync(IngestEventRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Ingesting analytics event for entity {EntityType}:{EntityId}", body.EntityType, body.EntityId);

        try
        {
            var eventId = Guid.NewGuid();
            var summaryStore = _stateStoreFactory.GetStore<EntitySummaryData>(_configuration.SummaryStoreName);
            var entityKey = GetEntityKey(body.GameServiceId, body.EntityType, body.EntityId);

            // Get or create entity summary
            var summary = await summaryStore.GetAsync(entityKey, cancellationToken);
            var isNew = summary == null;
            var previousValue = summary?.Aggregates?.GetValueOrDefault(body.EventType);

            summary ??= new EntitySummaryData
            {
                EntityId = body.EntityId,
                EntityType = body.EntityType,
                GameServiceId = body.GameServiceId,
                FirstEventAt = body.Timestamp,
                EventCounts = new Dictionary<string, long>(),
                Aggregates = new Dictionary<string, double>()
            };

            // Update summary
            summary.TotalEvents++;
            summary.LastEventAt = body.Timestamp;
            summary.EventCounts[body.EventType] = summary.EventCounts.GetValueOrDefault(body.EventType) + 1;

            // Update aggregates (sum values by event type)
            var newValue = summary.Aggregates.GetValueOrDefault(body.EventType) + body.Value;
            summary.Aggregates[body.EventType] = newValue;

            await summaryStore.SaveAsync(entityKey, summary, options: null, cancellationToken);
            await summaryStore.AddToSetAsync(
                GetSummaryIndexKey(body.GameServiceId),
                entityKey,
                cancellationToken: cancellationToken);

            // Publish score updated event if value changed
            if (body.Value != 0)
            {
                var scoreEvent = new AnalyticsScoreUpdatedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    GameServiceId = body.GameServiceId,
                    EntityId = body.EntityId,
                    EntityType = MapToScoreEventEntityType(body.EntityType),
                    ScoreType = body.EventType,
                    PreviousValue = previousValue,
                    NewValue = newValue,
                    Delta = body.Value
                };
                await _messageBus.TryPublishAsync("analytics.score.updated", scoreEvent, cancellationToken: cancellationToken);

                // Check for milestones (100, 500, 1000, 5000, 10000, etc.)
                await CheckAndPublishMilestoneAsync(body.GameServiceId, body.EntityId, body.EntityType, body.EventType, newValue, cancellationToken);
            }

            return (StatusCodes.OK, new IngestEventResponse
            {
                EventId = eventId,
                Accepted = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ingesting analytics event");
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "IngestEvent",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/analytics/event/ingest",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of IngestEventBatch operation.
    /// Ingests multiple analytics events efficiently.
    /// </summary>
    public async Task<(StatusCodes, IngestEventBatchResponse?)> IngestEventBatchAsync(IngestEventBatchRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Ingesting batch of {Count} analytics events", body.Events.Count);

        try
        {
            var accepted = 0;
            var rejected = 0;
            var errors = new List<string>();

            foreach (var evt in body.Events)
            {
                try
                {
                    var (status, _) = await IngestEventAsync(evt, cancellationToken);
                    if (status == StatusCodes.OK)
                    {
                        accepted++;
                    }
                    else
                    {
                        rejected++;
                        errors.Add($"Event for {evt.EntityType}:{evt.EntityId} returned status {status}");
                    }
                }
                catch (Exception ex)
                {
                    rejected++;
                    errors.Add($"Event for {evt.EntityType}:{evt.EntityId} failed: {ex.Message}");
                }
            }

            return (StatusCodes.OK, new IngestEventBatchResponse
            {
                Accepted = accepted,
                Rejected = rejected,
                Errors = errors.Count > 0 ? errors : null
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ingesting batch analytics events");
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "IngestEventBatch",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/analytics/event/ingest-batch",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of GetEntitySummary operation.
    /// Retrieves aggregated statistics for an entity.
    /// </summary>
    public async Task<(StatusCodes, EntitySummaryResponse?)> GetEntitySummaryAsync(GetEntitySummaryRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting entity summary for {EntityType}:{EntityId}", body.EntityType, body.EntityId);

        try
        {
            var summaryStore = _stateStoreFactory.GetStore<EntitySummaryData>(_configuration.SummaryStoreName);
            var entityKey = GetEntityKey(body.GameServiceId, body.EntityType, body.EntityId);

            var summary = await summaryStore.GetAsync(entityKey, cancellationToken);
            if (summary == null)
            {
                return (StatusCodes.NotFound, null);
            }

            return (StatusCodes.OK, new EntitySummaryResponse
            {
                EntityId = summary.EntityId,
                EntityType = summary.EntityType,
                TotalEvents = summary.TotalEvents,
                FirstEventAt = summary.FirstEventAt,
                LastEventAt = summary.LastEventAt,
                EventCounts = summary.EventCounts,
                Aggregates = summary.Aggregates
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting entity summary");
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "GetEntitySummary",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/analytics/summary/get",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of QueryEntitySummaries operation.
    /// Queries multiple entity summaries with filtering.
    /// </summary>
    public async Task<(StatusCodes, QueryEntitySummariesResponse?)> QueryEntitySummariesAsync(QueryEntitySummariesRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Querying entity summaries for game service {GameServiceId}", body.GameServiceId);

        try
        {
            if (body.Limit <= 0)
            {
                _logger.LogWarning("Invalid limit {Limit} for analytics summary query", body.Limit);
                return (StatusCodes.BadRequest, null);
            }

            if (body.Offset < 0)
            {
                _logger.LogWarning("Invalid offset {Offset} for analytics summary query", body.Offset);
                return (StatusCodes.BadRequest, null);
            }

            if (body.MinEvents < 0)
            {
                _logger.LogWarning("Invalid minEvents {MinEvents} for analytics summary query", body.MinEvents);
                return (StatusCodes.BadRequest, null);
            }

            if (!string.IsNullOrWhiteSpace(body.SortBy))
            {
                var sortBy = body.SortBy.Trim().ToLowerInvariant();
                if (sortBy != "totalevents" &&
                    sortBy != "firsteventat" &&
                    sortBy != "lasteventat" &&
                    sortBy != "eventcount")
                {
                    _logger.LogWarning("Unsupported sortBy value {SortBy} for analytics summary query", body.SortBy);
                    return (StatusCodes.BadRequest, null);
                }
            }

            var summaryStore = _stateStoreFactory.GetStore<EntitySummaryData>(_configuration.SummaryStoreName);
            var indexKey = GetSummaryIndexKey(body.GameServiceId);
            var entityKeys = await summaryStore.GetSetAsync<string>(indexKey, cancellationToken);

            if (entityKeys.Count == 0)
            {
                return (StatusCodes.OK, new QueryEntitySummariesResponse
                {
                    Summaries = new List<EntitySummaryResponse>(),
                    Total = 0
                });
            }

            var summaries = new List<EntitySummaryData>();

            foreach (var entityKey in entityKeys)
            {
                var summary = await summaryStore.GetAsync(entityKey, cancellationToken);
                if (summary == null)
                {
                    continue;
                }

                if (body.EntityType.HasValue && summary.EntityType != body.EntityType.Value)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(body.EventType))
                {
                    if (summary.EventCounts == null || !summary.EventCounts.ContainsKey(body.EventType))
                    {
                        continue;
                    }
                }

                if (body.MinEvents > 0 && summary.TotalEvents < body.MinEvents)
                {
                    continue;
                }

                summaries.Add(summary);
            }

            var total = summaries.Count;
            var ordered = ApplySummarySort(summaries, body);
            var paged = ordered
                .Skip(body.Offset)
                .Take(body.Limit)
                .Select(summary => new EntitySummaryResponse
                {
                    EntityId = summary.EntityId,
                    EntityType = summary.EntityType,
                    TotalEvents = summary.TotalEvents,
                    FirstEventAt = summary.FirstEventAt,
                    LastEventAt = summary.LastEventAt,
                    EventCounts = summary.EventCounts,
                    Aggregates = summary.Aggregates
                })
                .ToList();

            return (StatusCodes.OK, new QueryEntitySummariesResponse
            {
                Summaries = paged,
                Total = total
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying entity summaries");
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "QueryEntitySummaries",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/analytics/summary/query",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of GetSkillRating operation.
    /// Retrieves Glicko-2 skill rating for an entity.
    /// </summary>
    public async Task<(StatusCodes, SkillRatingResponse?)> GetSkillRatingAsync(GetSkillRatingRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Getting skill rating for {EntityType}:{EntityId}, type {RatingType}",
            body.EntityType, body.EntityId, body.RatingType);

        try
        {
            var ratingStore = _stateStoreFactory.GetStore<SkillRatingData>(_configuration.RatingStoreName);
            var ratingKey = GetRatingKey(body.GameServiceId, body.RatingType, body.EntityType, body.EntityId);

            var rating = await ratingStore.GetAsync(ratingKey, cancellationToken);
            if (rating == null)
            {
                var defaultRating = _configuration.Glicko2DefaultRating;
                var defaultDeviation = _configuration.Glicko2DefaultDeviation;
                var defaultVolatility = _configuration.Glicko2DefaultVolatility;

                // Return default rating for new players
                return (StatusCodes.OK, new SkillRatingResponse
                {
                    EntityId = body.EntityId,
                    EntityType = body.EntityType,
                    RatingType = body.RatingType,
                    Rating = defaultRating,
                    RatingDeviation = defaultDeviation,
                    Volatility = defaultVolatility,
                    MatchesPlayed = 0,
                    LastMatchAt = null
                });
            }

            return (StatusCodes.OK, new SkillRatingResponse
            {
                EntityId = rating.EntityId,
                EntityType = rating.EntityType,
                RatingType = rating.RatingType,
                Rating = rating.Rating,
                RatingDeviation = rating.RatingDeviation,
                Volatility = rating.Volatility,
                MatchesPlayed = rating.MatchesPlayed,
                LastMatchAt = rating.LastMatchAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting skill rating");
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "GetSkillRating",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/analytics/rating/get",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of UpdateSkillRating operation.
    /// Updates Glicko-2 skill ratings after a match.
    /// </summary>
    public async Task<(StatusCodes, UpdateSkillRatingResponse?)> UpdateSkillRatingAsync(UpdateSkillRatingRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Updating skill ratings for match {MatchId} with {Count} participants",
            body.MatchId, body.Results.Count);

        try
        {
            if (body.Results.Count < 2)
            {
                return (StatusCodes.BadRequest, null);
            }

            var ratingStore = _stateStoreFactory.GetStore<SkillRatingData>(_configuration.RatingStoreName);
            var updatedRatings = new List<SkillRatingChange>();
            var now = DateTimeOffset.UtcNow;

            // Load all current ratings
            var currentRatings = new Dictionary<string, SkillRatingData>();
            foreach (var result in body.Results)
            {
                var key = GetRatingKey(body.GameServiceId, body.RatingType, result.EntityType, result.EntityId);
                var rating = await ratingStore.GetAsync(key, cancellationToken);
                currentRatings[key] = rating ?? new SkillRatingData
                {
                    EntityId = result.EntityId,
                    EntityType = result.EntityType,
                    RatingType = body.RatingType,
                    GameServiceId = body.GameServiceId,
                    Rating = _configuration.Glicko2DefaultRating,
                    RatingDeviation = _configuration.Glicko2DefaultDeviation,
                    Volatility = _configuration.Glicko2DefaultVolatility,
                    MatchesPlayed = 0
                };
            }

            // Calculate new ratings using Glicko-2
            foreach (var result in body.Results)
            {
                var key = GetRatingKey(body.GameServiceId, body.RatingType, result.EntityType, result.EntityId);
                var playerRating = currentRatings[key];
                var previousRating = playerRating.Rating;

                // Calculate opponents' combined effect
                var opponents = body.Results.Where(r => r.EntityId != result.EntityId).ToList();
                var (newRating, newRD, newVolatility) = CalculateGlicko2Update(
                    playerRating,
                    opponents.Select(o =>
                    {
                        var oppKey = GetRatingKey(body.GameServiceId, body.RatingType, o.EntityType, o.EntityId);
                        return (currentRatings[oppKey], result.Outcome);
                    }).ToList()
                );

                // Update rating data
                playerRating.Rating = newRating;
                playerRating.RatingDeviation = newRD;
                playerRating.Volatility = newVolatility;
                playerRating.MatchesPlayed++;
                playerRating.LastMatchAt = now;

                await ratingStore.SaveAsync(key, playerRating, options: null, cancellationToken);

                var ratingChange = newRating - previousRating;
                updatedRatings.Add(new SkillRatingChange
                {
                    EntityId = result.EntityId,
                    EntityType = result.EntityType,
                    PreviousRating = previousRating,
                    NewRating = newRating,
                    RatingChange = ratingChange
                });

                // Publish rating updated event
                var ratingEvent = new AnalyticsRatingUpdatedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = now,
                    GameServiceId = body.GameServiceId,
                    EntityId = result.EntityId,
                    EntityType = MapToRatingEventEntityType(result.EntityType),
                    RatingType = body.RatingType,
                    PreviousRating = previousRating,
                    NewRating = newRating,
                    RatingChange = ratingChange,
                    NewRatingDeviation = newRD,
                    MatchId = body.MatchId
                };
                await _messageBus.TryPublishAsync("analytics.rating.updated", ratingEvent, cancellationToken: cancellationToken);
            }

            return (StatusCodes.OK, new UpdateSkillRatingResponse
            {
                MatchId = body.MatchId,
                UpdatedRatings = updatedRatings
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating skill rating");
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "UpdateSkillRating",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/analytics/rating/update",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Implementation of RecordControllerEvent operation.
    /// Records a controller possession/release event for history tracking.
    /// </summary>
    public async Task<StatusCodes> RecordControllerEventAsync(RecordControllerEventRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Recording controller {Action} event: account {AccountId} -> {EntityType}:{EntityId}",
            body.Action, body.AccountId, body.TargetEntityType, body.TargetEntityId);

        try
        {
            var controllerStore = _stateStoreFactory.GetStore<ControllerHistoryData>(_configuration.HistoryStoreName);
            var eventId = Guid.NewGuid();
            var key = GetControllerKey(body.GameServiceId, body.AccountId, body.Timestamp);

            var historyEvent = new ControllerHistoryData
            {
                EventId = eventId,
                GameServiceId = body.GameServiceId,
                AccountId = body.AccountId,
                TargetEntityId = body.TargetEntityId,
                TargetEntityType = body.TargetEntityType,
                Action = body.Action,
                Timestamp = body.Timestamp,
                SessionId = body.SessionId
            };

            await controllerStore.SaveAsync(key, historyEvent, options: null, cancellationToken);
            await controllerStore.AddToSetAsync(
                GetControllerIndexKey(body.GameServiceId),
                key,
                cancellationToken: cancellationToken);
            await controllerStore.AddToSetAsync(
                GetControllerAccountIndexKey(body.GameServiceId, body.AccountId),
                key,
                cancellationToken: cancellationToken);

            return StatusCodes.OK;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording controller event");
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "RecordControllerEvent",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/analytics/controller-history/record",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return StatusCodes.InternalServerError;
        }
    }

    /// <summary>
    /// Implementation of QueryControllerHistory operation.
    /// Queries controller possession/release history.
    /// </summary>
    public async Task<(StatusCodes, QueryControllerHistoryResponse?)> QueryControllerHistoryAsync(QueryControllerHistoryRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Querying controller history for game service {GameServiceId}", body.GameServiceId);

        try
        {
            if (body.Limit <= 0)
            {
                _logger.LogWarning("Invalid limit {Limit} for controller history query", body.Limit);
                return (StatusCodes.BadRequest, null);
            }

            if (body.StartTime.HasValue && body.EndTime.HasValue && body.StartTime > body.EndTime)
            {
                _logger.LogWarning("Invalid controller history time range {StartTime} to {EndTime}",
                    body.StartTime, body.EndTime);
                return (StatusCodes.BadRequest, null);
            }

            var controllerStore = _stateStoreFactory.GetStore<ControllerHistoryData>(_configuration.HistoryStoreName);
            var indexKey = body.AccountId.HasValue
                ? GetControllerAccountIndexKey(body.GameServiceId, body.AccountId.Value)
                : GetControllerIndexKey(body.GameServiceId);
            var eventKeys = await controllerStore.GetSetAsync<string>(indexKey, cancellationToken);

            if (eventKeys.Count == 0)
            {
                return (StatusCodes.OK, new QueryControllerHistoryResponse
                {
                    Events = new List<ControllerHistoryEvent>()
                });
            }

            var events = new List<ControllerHistoryData>();

            foreach (var eventKey in eventKeys)
            {
                var historyEvent = await controllerStore.GetAsync(eventKey, cancellationToken);
                if (historyEvent == null)
                {
                    continue;
                }

                if (body.AccountId.HasValue && historyEvent.AccountId != body.AccountId.Value)
                {
                    continue;
                }

                if (body.TargetEntityId.HasValue && historyEvent.TargetEntityId != body.TargetEntityId.Value)
                {
                    continue;
                }

                if (body.TargetEntityType.HasValue && historyEvent.TargetEntityType != body.TargetEntityType.Value)
                {
                    continue;
                }

                if (body.StartTime.HasValue && historyEvent.Timestamp < body.StartTime.Value)
                {
                    continue;
                }

                if (body.EndTime.HasValue && historyEvent.Timestamp > body.EndTime.Value)
                {
                    continue;
                }

                events.Add(historyEvent);
            }

            var ordered = events
                .OrderByDescending(e => e.Timestamp)
                .Take(body.Limit)
                .Select(evt => new ControllerHistoryEvent
                {
                    EventId = evt.EventId,
                    AccountId = evt.AccountId,
                    TargetEntityId = evt.TargetEntityId,
                    TargetEntityType = evt.TargetEntityType,
                    Action = evt.Action,
                    Timestamp = evt.Timestamp,
                    SessionId = evt.SessionId
                })
                .ToList();

            return (StatusCodes.OK, new QueryControllerHistoryResponse
            {
                Events = ordered
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying controller history");
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "QueryControllerHistory",
                "unexpected_exception",
                ex.Message,
                dependency: null,
                endpoint: "post:/analytics/controller-history/query",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #region Glicko-2 Algorithm Implementation

    /// <summary>
    /// Calculates the Glicko-2 rating update for a player based on match results.
    /// </summary>
    private (double newRating, double newRD, double newVolatility) CalculateGlicko2Update(
        SkillRatingData player,
        List<(SkillRatingData opponent, double outcome)> results)
    {
        var baseRating = _configuration.Glicko2DefaultRating;
        var maxDeviation = _configuration.Glicko2DefaultDeviation;

        // Convert ratings to Glicko-2 scale (μ = (r - baseRating) / 173.7178)
        var mu = (player.Rating - baseRating) / GlickoScale;
        var phi = player.RatingDeviation / GlickoScale;
        var sigma = player.Volatility;

        if (results.Count == 0)
        {
            // No games played, only update RD due to rating period
            var newPhi = Math.Sqrt(phi * phi + sigma * sigma);
            return (player.Rating, Math.Min(newPhi * GlickoScale, maxDeviation), sigma);
        }

        // Calculate v (estimated variance)
        var v = 0.0;
        var delta = 0.0;

        foreach (var (opponent, outcome) in results)
        {
            var muJ = (opponent.Rating - baseRating) / GlickoScale;
            var phiJ = opponent.RatingDeviation / GlickoScale;

            var gPhiJ = G(phiJ);
            var e = E(mu, muJ, phiJ);

            v += gPhiJ * gPhiJ * e * (1 - e);
            delta += gPhiJ * (outcome - e);
        }

        v = 1.0 / v;
        delta = v * delta;

        // Calculate new volatility using iterative algorithm
        var newSigma = CalculateNewVolatility(sigma, phi, v, delta);

        // Update phi* (pre-rating period RD)
        var phiStar = Math.Sqrt(phi * phi + newSigma * newSigma);

        // Calculate new phi
        var newPhi2 = 1.0 / (1.0 / (phiStar * phiStar) + 1.0 / v);
        var newPhiValue = Math.Sqrt(newPhi2);

        // Calculate new mu
        var newMu = mu + newPhi2 * delta / v;

        // Convert back to Glicko-1 scale
        var newRating = newMu * GlickoScale + baseRating;
        var newRD = newPhiValue * GlickoScale;

        // Clamp values to reasonable ranges
        newRating = Math.Clamp(newRating, 100, 4000);
        newRD = Math.Clamp(newRD, 30, maxDeviation);

        return (newRating, newRD, newSigma);
    }

    /// <summary>
    /// Calculates the g(φ) function used in Glicko-2.
    /// </summary>
    private static double G(double phi)
        => 1.0 / Math.Sqrt(1.0 + 3.0 * phi * phi / (Math.PI * Math.PI));

    /// <summary>
    /// Calculates the E(μ, μj, φj) expected score function.
    /// </summary>
    private static double E(double mu, double muJ, double phiJ)
        => 1.0 / (1.0 + Math.Exp(-G(phiJ) * (mu - muJ)));

    /// <summary>
    /// Calculates new volatility using iterative algorithm (Illinois algorithm).
    /// </summary>
    private double CalculateNewVolatility(double sigma, double phi, double v, double delta)
    {
        var a = Math.Log(sigma * sigma);
        var deltaSq = delta * delta;
        var phiSq = phi * phi;
        var tau = _configuration.Glicko2SystemConstant;

        double f(double x)
        {
            var ex = Math.Exp(x);
            var num = ex * (deltaSq - phiSq - v - ex);
            var den = 2.0 * Math.Pow(phiSq + v + ex, 2);
            return num / den - (x - a) / (tau * tau);
        }

        // Initial bounds
        var upperBound = deltaSq > phiSq + v
            ? Math.Log(deltaSq - phiSq - v)
            : a - tau;

        while (f(upperBound) < 0)
        {
            upperBound -= tau;
        }

        var lowerBound = a;

        // Illinois algorithm iteration
        var fA = f(lowerBound);
        var fB = f(upperBound);

        while (Math.Abs(upperBound - lowerBound) > 0.000001)
        {
            var c = lowerBound + (lowerBound - upperBound) * fA / (fB - fA);
            var fC = f(c);

            if (fC * fB <= 0)
            {
                lowerBound = upperBound;
                fA = fB;
            }
            else
            {
                fA /= 2;
            }

            upperBound = c;
            fB = fC;
        }

        return Math.Exp(upperBound / 2);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Checks if a milestone has been reached and publishes event if so.
    /// </summary>
    private async Task CheckAndPublishMilestoneAsync(
        Guid gameServiceId,
        Guid entityId,
        EntityType entityType,
        string scoreType,
        double newValue,
        CancellationToken cancellationToken)
    {
        // Common milestone thresholds
        int[] milestones = { 10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10000, 25000, 50000, 100000 };

        foreach (var milestone in milestones)
        {
            // Check if we just crossed this milestone (newValue >= milestone and previous < milestone)
            if (newValue >= milestone && newValue - milestone < Math.Abs(newValue * 0.1))
            {
                var milestoneEvent = new AnalyticsMilestoneReachedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    GameServiceId = gameServiceId,
                    EntityId = entityId,
                    EntityType = MapToMilestoneEventEntityType(entityType),
                    MilestoneType = scoreType,
                    MilestoneValue = milestone,
                    MilestoneName = $"{scoreType}_{milestone}"
                };
                await _messageBus.TryPublishAsync("analytics.milestone.reached", milestoneEvent, cancellationToken: cancellationToken);
                break; // Only publish one milestone per event
            }
        }
    }

    /// <summary>
    /// Applies sorting rules for entity summary queries.
    /// </summary>
    private IReadOnlyList<EntitySummaryData> ApplySummarySort(
        List<EntitySummaryData> summaries,
        QueryEntitySummariesRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.SortBy))
        {
            return summaries;
        }

        var sortBy = body.SortBy.Trim().ToLowerInvariant();
        var descending = body.SortDescending;

        IEnumerable<EntitySummaryData> ordered = sortBy switch
        {
            "totalevents" => descending
                ? summaries.OrderByDescending(s => s.TotalEvents)
                : summaries.OrderBy(s => s.TotalEvents),
            "firsteventat" => descending
                ? summaries.OrderByDescending(s => s.FirstEventAt)
                : summaries.OrderBy(s => s.FirstEventAt),
            "lasteventat" => descending
                ? summaries.OrderByDescending(s => s.LastEventAt)
                : summaries.OrderBy(s => s.LastEventAt),
            "eventcount" => OrderByEventCount(summaries, body.EventType, descending),
            _ => summaries
        };

        return ordered.ToList();
    }

    private static IEnumerable<EntitySummaryData> OrderByEventCount(
        List<EntitySummaryData> summaries,
        string? eventType,
        bool descending)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            return summaries;
        }

        return descending
            ? summaries.OrderByDescending(s => s.EventCounts.GetValueOrDefault(eventType))
            : summaries.OrderBy(s => s.EventCounts.GetValueOrDefault(eventType));
    }

    /// <summary>
    /// Maps EntityType to AnalyticsScoreUpdatedEventEntityType.
    /// </summary>
    private static AnalyticsScoreUpdatedEventEntityType MapToScoreEventEntityType(EntityType entityType)
        => entityType switch
        {
            EntityType.Account => AnalyticsScoreUpdatedEventEntityType.Account,
            EntityType.Character => AnalyticsScoreUpdatedEventEntityType.Character,
            EntityType.Guild => AnalyticsScoreUpdatedEventEntityType.Guild,
            EntityType.Actor => AnalyticsScoreUpdatedEventEntityType.Actor,
            EntityType.Custom => AnalyticsScoreUpdatedEventEntityType.Custom,
            _ => AnalyticsScoreUpdatedEventEntityType.Custom
        };

    /// <summary>
    /// Maps EntityType to AnalyticsRatingUpdatedEventEntityType.
    /// </summary>
    private static AnalyticsRatingUpdatedEventEntityType MapToRatingEventEntityType(EntityType entityType)
        => entityType switch
        {
            EntityType.Account => AnalyticsRatingUpdatedEventEntityType.Account,
            EntityType.Character => AnalyticsRatingUpdatedEventEntityType.Character,
            EntityType.Guild => AnalyticsRatingUpdatedEventEntityType.Guild,
            EntityType.Actor => AnalyticsRatingUpdatedEventEntityType.Actor,
            EntityType.Custom => AnalyticsRatingUpdatedEventEntityType.Custom,
            _ => AnalyticsRatingUpdatedEventEntityType.Custom
        };

    /// <summary>
    /// Maps EntityType to AnalyticsMilestoneReachedEventEntityType.
    /// </summary>
    private static AnalyticsMilestoneReachedEventEntityType MapToMilestoneEventEntityType(EntityType entityType)
        => entityType switch
        {
            EntityType.Account => AnalyticsMilestoneReachedEventEntityType.Account,
            EntityType.Character => AnalyticsMilestoneReachedEventEntityType.Character,
            EntityType.Guild => AnalyticsMilestoneReachedEventEntityType.Guild,
            EntityType.Actor => AnalyticsMilestoneReachedEventEntityType.Actor,
            EntityType.Custom => AnalyticsMilestoneReachedEventEntityType.Custom,
            _ => AnalyticsMilestoneReachedEventEntityType.Custom
        };

    #endregion
}

#region Internal Data Models

/// <summary>
/// Internal storage model for entity summary data.
/// </summary>
internal class EntitySummaryData
{
    public Guid EntityId { get; set; }
    public EntityType EntityType { get; set; }
    public Guid GameServiceId { get; set; }
    public long TotalEvents { get; set; }
    public DateTimeOffset FirstEventAt { get; set; }
    public DateTimeOffset LastEventAt { get; set; }
    public Dictionary<string, long> EventCounts { get; set; } = new();
    public Dictionary<string, double> Aggregates { get; set; } = new();
}

/// <summary>
/// Internal storage model for Glicko-2 skill rating data.
/// </summary>
internal class SkillRatingData
{
    public Guid EntityId { get; set; }
    public EntityType EntityType { get; set; }
    public string RatingType { get; set; } = string.Empty;
    public Guid GameServiceId { get; set; }
    public double Rating { get; set; }
    public double RatingDeviation { get; set; }
    public double Volatility { get; set; }
    public int MatchesPlayed { get; set; }
    public DateTimeOffset? LastMatchAt { get; set; }
}

/// <summary>
/// Internal storage model for controller history events.
/// </summary>
internal class ControllerHistoryData
{
    public Guid EventId { get; set; }
    public Guid GameServiceId { get; set; }
    public Guid AccountId { get; set; }
    public Guid TargetEntityId { get; set; }
    public EntityType TargetEntityType { get; set; }
    public ControllerAction Action { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public Guid? SessionId { get; set; }
}

#endregion
