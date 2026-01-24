using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Character;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.GameService;
using BeyondImmersion.BannouService.GameSession;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;

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
    private readonly IGameServiceClient _gameServiceClient;
    private readonly IGameSessionClient _gameSessionClient;
    private readonly IRealmClient _realmClient;
    private readonly ICharacterClient _characterClient;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly ILogger<AnalyticsService> _logger;
    private readonly AnalyticsServiceConfiguration _configuration;

    // State store key prefixes
    private const string SUMMARY_INDEX_PREFIX = "analytics-summary-index";
    private const string CONTROLLER_INDEX_PREFIX = "analytics-controller-index";
    private const string EVENT_BUFFER_INDEX_KEY = "analytics-event-buffer-index";
    private const string EVENT_BUFFER_ENTRY_PREFIX = "analytics-event-buffer-entry";
    private const string SESSION_MAPPING_PREFIX = "analytics-session-mapping";
    private const string GAME_SERVICE_CACHE_PREFIX = "analytics-game-service-cache";
    private const string REALM_GAME_SERVICE_CACHE_PREFIX = "analytics-realm-game-service-cache";
    private const string CHARACTER_REALM_CACHE_PREFIX = "analytics-character-realm-cache";
    private const string BUFFER_LOCK_RESOURCE = "analytics-event-buffer-flush";

    // Glicko-2 scale conversion constant
    private const double GlickoScale = 173.7178;

    // Maximum iterations for Glicko-2 volatility convergence
    private const int MaxVolatilityIterations = 100;

    /// <summary>
    /// Initializes a new instance of the AnalyticsService.
    /// </summary>
    public AnalyticsService(
        IMessageBus messageBus,
        IStateStoreFactory stateStoreFactory,
        IGameServiceClient gameServiceClient,
        IGameSessionClient gameSessionClient,
        IRealmClient realmClient,
        ICharacterClient characterClient,
        IDistributedLockProvider lockProvider,
        ILogger<AnalyticsService> logger,
        AnalyticsServiceConfiguration configuration,
        IEventConsumer eventConsumer)
    {
        _messageBus = messageBus;
        _stateStoreFactory = stateStoreFactory;
        _gameServiceClient = gameServiceClient;
        _gameSessionClient = gameSessionClient;
        _realmClient = realmClient;
        _characterClient = characterClient;
        _lockProvider = lockProvider;
        _logger = logger;
        _configuration = configuration;

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
            var bufferedEvent = new BufferedAnalyticsEvent
            {
                EventId = Guid.NewGuid(),
                GameServiceId = body.GameServiceId,
                EntityId = body.EntityId,
                EntityType = body.EntityType,
                EventType = body.EventType,
                Timestamp = body.Timestamp,
                Value = body.Value,
                Metadata = MetadataHelper.ConvertToDictionary(body.Metadata)
            };

            var buffered = await BufferAnalyticsEventAsync(bufferedEvent, cancellationToken);
            if (!buffered)
            {
                return (StatusCodes.InternalServerError, null);
            }

            return (StatusCodes.OK, new IngestEventResponse
            {
                EventId = bufferedEvent.EventId,
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
                    var bufferedEvent = new BufferedAnalyticsEvent
                    {
                        EventId = Guid.NewGuid(),
                        GameServiceId = evt.GameServiceId,
                        EntityId = evt.EntityId,
                        EntityType = evt.EntityType,
                        EventType = evt.EventType,
                        Timestamp = evt.Timestamp,
                        Value = evt.Value,
                        Metadata = MetadataHelper.ConvertToDictionary(evt.Metadata)
                    };

                    var buffered = await BufferAnalyticsEventAsync(
                        bufferedEvent,
                        cancellationToken,
                        flushAfterEnqueue: false);
                    if (buffered)
                    {
                        accepted++;
                    }
                    else
                    {
                        rejected++;
                        errors.Add($"Event for {evt.EntityType}:{evt.EntityId} failed to buffer");
                    }
                }
                catch (Exception ex)
                {
                    rejected++;
                    errors.Add($"Event for {evt.EntityType}:{evt.EntityId} failed: {ex.Message}");
                }
            }

            if (accepted > 0)
            {
                await FlushBufferedEventsIfNeededAsync(cancellationToken);
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
            var summaryStore = _stateStoreFactory.GetStore<EntitySummaryData>(StateStoreDefinitions.AnalyticsSummary);
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

            var summaryStore = _stateStoreFactory.GetStore<EntitySummaryData>(StateStoreDefinitions.AnalyticsSummary);
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
                    await summaryStore.RemoveFromSetAsync(indexKey, entityKey, cancellationToken);
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
            var ratingStore = _stateStoreFactory.GetStore<SkillRatingData>(StateStoreDefinitions.AnalyticsRating);
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

            var ratingStore = _stateStoreFactory.GetStore<SkillRatingData>(StateStoreDefinitions.AnalyticsRating);
            var updatedRatings = new List<SkillRatingChange>();
            var now = DateTimeOffset.UtcNow;

            // Load all current ratings with ETags for optimistic concurrency
            var currentRatings = new Dictionary<string, SkillRatingData>();
            var ratingEtags = new Dictionary<string, string>();
            foreach (var result in body.Results)
            {
                var key = GetRatingKey(body.GameServiceId, body.RatingType, result.EntityType, result.EntityId);
                var (rating, etag) = await ratingStore.GetWithETagAsync(key, cancellationToken);
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
                ratingEtags[key] = etag ?? string.Empty;
            }

            // Calculate new ratings using Glicko-2
            foreach (var result in body.Results)
            {
                var key = GetRatingKey(body.GameServiceId, body.RatingType, result.EntityType, result.EntityId);
                var playerRating = currentRatings[key];
                var previousRating = playerRating.Rating;

                // Calculate opponents' combined effect using pairwise outcomes:
                // player beat opponent (higher outcome) = 1.0, lost = 0.0, tied = 0.5
                var opponents = body.Results.Where(r => r.EntityId != result.EntityId).ToList();
                var (newRating, newRD, newVolatility) = CalculateGlicko2Update(
                    playerRating,
                    opponents.Select(o =>
                    {
                        var oppKey = GetRatingKey(body.GameServiceId, body.RatingType, o.EntityType, o.EntityId);
                        var pairwiseOutcome = result.Outcome > o.Outcome ? 1.0
                            : result.Outcome < o.Outcome ? 0.0
                            : 0.5;
                        return (currentRatings[oppKey], pairwiseOutcome);
                    }).ToList()
                );

                // Update rating data
                playerRating.Rating = newRating;
                playerRating.RatingDeviation = newRD;
                playerRating.Volatility = newVolatility;
                playerRating.MatchesPlayed++;
                playerRating.LastMatchAt = now;

                // Save with optimistic concurrency check
                var newEtag = await ratingStore.TrySaveAsync(key, playerRating, ratingEtags[key], cancellationToken);
                if (newEtag == null)
                {
                    _logger.LogWarning("Concurrent modification detected for rating {Key} during match {MatchId}", key, body.MatchId);
                    return (StatusCodes.Conflict, null);
                }

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
            var controllerStore = _stateStoreFactory.GetStore<ControllerHistoryData>(StateStoreDefinitions.AnalyticsHistory);
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

            var controllerStore = _stateStoreFactory.GetStore<ControllerHistoryData>(StateStoreDefinitions.AnalyticsHistory);
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

        var iterations = 0;
        while (f(upperBound) < 0)
        {
            upperBound -= tau;
            iterations++;
            if (iterations >= MaxVolatilityIterations)
            {
                _logger.LogWarning("Glicko-2 upper bound search did not converge after {MaxIterations} iterations, returning current sigma {Sigma}", MaxVolatilityIterations, sigma);
                return sigma;
            }
        }

        var lowerBound = a;

        // Illinois algorithm iteration
        var fA = f(lowerBound);
        var fB = f(upperBound);

        iterations = 0;
        while (Math.Abs(upperBound - lowerBound) > _configuration.Glicko2VolatilityConvergenceTolerance)
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

            iterations++;
            if (iterations >= MaxVolatilityIterations)
            {
                _logger.LogWarning("Glicko-2 Illinois algorithm did not converge after {MaxIterations} iterations, returning current sigma {Sigma}", MaxVolatilityIterations, sigma);
                return sigma;
            }
        }

        return Math.Exp(upperBound / 2);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Builds the key for a buffered analytics event entry.
    /// </summary>
    private static string GetEventBufferEntryKey(Guid eventId)
        => $"{EVENT_BUFFER_ENTRY_PREFIX}:{eventId}";

    /// <summary>
    /// Builds the cache key for a game service stub.
    /// </summary>
    private static string GetGameServiceCacheKey(string stubName)
        => $"{GAME_SERVICE_CACHE_PREFIX}:{stubName}";

    /// <summary>
    /// Builds the cache key for a game session mapping.
    /// </summary>
    private static string GetSessionMappingKey(Guid sessionId)
        => $"{SESSION_MAPPING_PREFIX}:{sessionId}";

    private StateOptions? BuildSummaryCacheOptions()
    {
        if (_configuration.SummaryCacheTtlSeconds <= 0)
        {
            return null;
        }

        return new StateOptions { Ttl = _configuration.SummaryCacheTtlSeconds };
    }

    private async Task<bool> EnsureSummaryStoreRedisAsync(CancellationToken cancellationToken)
    {
        try
        {
            var backend = _stateStoreFactory.GetBackendType(StateStoreDefinitions.AnalyticsSummary);
            if (backend == StateBackend.Redis)
            {
                return true;
            }

            var message = "Analytics summary store must use Redis to support buffered ingestion";
            _logger.LogError(
                "{Message} (StoreName: {StoreName}, Backend: {Backend})",
                message,
                StateStoreDefinitions.AnalyticsSummary,
                backend);
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "EnsureSummaryStoreRedis",
                "analytics_summary_store_invalid",
                message,
                dependency: "state",
                endpoint: "state:summary",
                details: $"store:{StateStoreDefinitions.AnalyticsSummary};backend:{backend}",
                stack: null,
                cancellationToken: cancellationToken);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to determine analytics summary store backend");
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "EnsureSummaryStoreRedis",
                "analytics_summary_store_lookup_failed",
                ex.Message,
                dependency: "state",
                endpoint: "state:summary",
                details: $"store:{StateStoreDefinitions.AnalyticsSummary}",
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return false;
        }
    }

    private async Task<Guid?> ResolveGameServiceIdAsync(string gameType, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(gameType))
        {
            var message = "Game type is required to resolve game service ID";
            _logger.LogError(message);
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "ResolveGameServiceId",
                "game_type_missing",
                message,
                dependency: null,
                endpoint: "event:game-session",
                details: null,
                stack: null,
                cancellationToken: cancellationToken);
            return null;
        }

        var stubName = gameType.Trim().ToLowerInvariant();
        var cacheOptions = BuildSummaryCacheOptions();
        if (cacheOptions != null)
        {
            var cacheStore = _stateStoreFactory.GetStore<GameServiceCacheEntry>(StateStoreDefinitions.AnalyticsSummary);
            var cacheKey = GetGameServiceCacheKey(stubName);
            var cached = await cacheStore.GetAsync(cacheKey, cancellationToken);
            if (cached != null)
            {
                return cached.ServiceId;
            }
        }

        try
        {
            var response = await _gameServiceClient.GetServiceAsync(new GetServiceRequest
            {
                StubName = stubName
            }, cancellationToken);

            if (response == null)
            {
                var message = "Game service lookup returned no data";
                _logger.LogError("{Message} (GameType: {GameType})", message, gameType);
                await _messageBus.TryPublishErrorAsync(
                    "analytics",
                    "ResolveGameServiceId",
                    "game_service_lookup_empty",
                    message,
                    dependency: "game-service",
                    endpoint: "service:game-service/get",
                    details: $"gameType:{gameType}",
                    stack: null,
                    cancellationToken: cancellationToken);
                return null;
            }

            if (cacheOptions != null)
            {
                var cacheStore = _stateStoreFactory.GetStore<GameServiceCacheEntry>(StateStoreDefinitions.AnalyticsSummary);
                var cacheKey = GetGameServiceCacheKey(stubName);
                await cacheStore.SaveAsync(cacheKey, new GameServiceCacheEntry
                {
                    ServiceId = response.ServiceId,
                    CachedAt = DateTimeOffset.UtcNow
                }, cacheOptions, cancellationToken);
            }

            return response.ServiceId;
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "Failed to resolve game service for game type {GameType}", gameType);
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "ResolveGameServiceId",
                "game_service_lookup_failed",
                ex.Message,
                dependency: "game-service",
                endpoint: "service:game-service/get",
                details: $"gameType:{gameType}",
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error resolving game service for game type {GameType}", gameType);
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "ResolveGameServiceId",
                "unexpected_exception",
                ex.Message,
                dependency: "game-service",
                endpoint: "service:game-service/get",
                details: $"gameType:{gameType}",
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return null;
        }
    }

    private async Task<Guid?> ResolveGameServiceIdForSessionAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        try
        {
            var mappingStore = _stateStoreFactory.GetStore<GameSessionMappingData>(StateStoreDefinitions.AnalyticsSummary);
            var mappingKey = GetSessionMappingKey(sessionId);
            var mapping = await mappingStore.GetAsync(mappingKey, cancellationToken);
            if (mapping != null)
            {
                return mapping.GameServiceId;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read game session mapping for session {SessionId}", sessionId);
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "ResolveGameServiceIdForSession",
                "session_mapping_lookup_failed",
                ex.Message,
                dependency: "state",
                endpoint: "state:summary",
                details: $"sessionId:{sessionId}",
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
        }

        try
        {
            var session = await _gameSessionClient.GetGameSessionAsync(new GetGameSessionRequest
            {
                SessionId = sessionId
            }, cancellationToken);

            if (session == null)
            {
                var message = "Game session lookup returned no data";
                _logger.LogError("{Message} (SessionId: {SessionId})", message, sessionId);
                await _messageBus.TryPublishErrorAsync(
                    "analytics",
                    "ResolveGameServiceIdForSession",
                    "game_session_lookup_empty",
                    message,
                    dependency: "game-session",
                    endpoint: "service:game-session/get",
                    details: $"sessionId:{sessionId}",
                    stack: null,
                    cancellationToken: cancellationToken);
                return null;
            }

            var gameType = session.GameType.ToString().ToLowerInvariant();
            var gameServiceId = await ResolveGameServiceIdAsync(gameType, cancellationToken);
            if (gameServiceId.HasValue)
            {
                await SaveGameSessionMappingAsync(sessionId, gameType, gameServiceId.Value, cancellationToken);
            }

            return gameServiceId;
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "Failed to resolve game session {SessionId} for analytics event", sessionId);
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "ResolveGameServiceIdForSession",
                "game_session_lookup_failed",
                ex.Message,
                dependency: "game-session",
                endpoint: "service:game-session/get",
                details: $"sessionId:{sessionId}",
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error resolving game session {SessionId}", sessionId);
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "ResolveGameServiceIdForSession",
                "unexpected_exception",
                ex.Message,
                dependency: "game-session",
                endpoint: "service:game-session/get",
                details: $"sessionId:{sessionId}",
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return null;
        }
    }

    private async Task SaveGameSessionMappingAsync(
        Guid sessionId,
        string gameType,
        Guid gameServiceId,
        CancellationToken cancellationToken)
    {
        var mappingStore = _stateStoreFactory.GetStore<GameSessionMappingData>(StateStoreDefinitions.AnalyticsSummary);
        var mappingKey = GetSessionMappingKey(sessionId);
        var cacheOptions = BuildSummaryCacheOptions();
        await mappingStore.SaveAsync(mappingKey, new GameSessionMappingData
        {
            SessionId = sessionId,
            GameType = gameType,
            GameServiceId = gameServiceId,
            UpdatedAt = DateTimeOffset.UtcNow
        }, cacheOptions, cancellationToken);
    }

    private async Task RemoveGameSessionMappingAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var mappingStore = _stateStoreFactory.GetStore<GameSessionMappingData>(StateStoreDefinitions.AnalyticsSummary);
        var mappingKey = GetSessionMappingKey(sessionId);
        await mappingStore.DeleteAsync(mappingKey, cancellationToken);
    }

    /// <summary>
    /// Resolves the game service ID for a given realm by looking up the realm via client.
    /// Results are cached using the standard summary cache TTL.
    /// </summary>
    private async Task<Guid?> ResolveGameServiceIdForRealmAsync(Guid realmId, CancellationToken cancellationToken)
    {
        var cacheOptions = BuildSummaryCacheOptions();
        if (cacheOptions != null)
        {
            var cacheStore = _stateStoreFactory.GetStore<RealmGameServiceCacheEntry>(StateStoreDefinitions.AnalyticsSummary);
            var cacheKey = $"{REALM_GAME_SERVICE_CACHE_PREFIX}:{realmId}";
            var cached = await cacheStore.GetAsync(cacheKey, cancellationToken);
            if (cached != null)
            {
                return cached.GameServiceId;
            }
        }

        try
        {
            var realm = await _realmClient.GetRealmAsync(new GetRealmRequest
            {
                RealmId = realmId
            }, cancellationToken);

            if (realm == null)
            {
                _logger.LogError("Realm lookup returned no data for realm {RealmId}", realmId);
                await _messageBus.TryPublishErrorAsync(
                    "analytics",
                    "ResolveGameServiceIdForRealm",
                    "realm_lookup_empty",
                    "Realm lookup returned no data",
                    dependency: "realm",
                    endpoint: "service:realm/get",
                    details: $"realmId:{realmId}",
                    stack: null,
                    cancellationToken: cancellationToken);
                return null;
            }

            if (cacheOptions != null)
            {
                var cacheStore = _stateStoreFactory.GetStore<RealmGameServiceCacheEntry>(StateStoreDefinitions.AnalyticsSummary);
                var cacheKey = $"{REALM_GAME_SERVICE_CACHE_PREFIX}:{realmId}";
                await cacheStore.SaveAsync(cacheKey, new RealmGameServiceCacheEntry
                {
                    GameServiceId = realm.GameServiceId,
                    CachedAt = DateTimeOffset.UtcNow
                }, cacheOptions, cancellationToken);
            }

            return realm.GameServiceId;
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "Failed to resolve game service for realm {RealmId}", realmId);
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "ResolveGameServiceIdForRealm",
                "realm_lookup_failed",
                ex.Message,
                dependency: "realm",
                endpoint: "service:realm/get",
                details: $"realmId:{realmId}",
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error resolving game service for realm {RealmId}", realmId);
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "ResolveGameServiceIdForRealm",
                "unexpected_exception",
                ex.Message,
                dependency: "realm",
                endpoint: "service:realm/get",
                details: $"realmId:{realmId}",
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return null;
        }
    }

    /// <summary>
    /// Resolves the game service ID for a given character by looking up the character's realm,
    /// then resolving the realm's game service ID.
    /// Results are cached using the standard summary cache TTL.
    /// </summary>
    private async Task<Guid?> ResolveGameServiceIdForCharacterAsync(Guid characterId, CancellationToken cancellationToken)
    {
        var cacheOptions = BuildSummaryCacheOptions();
        if (cacheOptions != null)
        {
            var cacheStore = _stateStoreFactory.GetStore<CharacterRealmCacheEntry>(StateStoreDefinitions.AnalyticsSummary);
            var cacheKey = $"{CHARACTER_REALM_CACHE_PREFIX}:{characterId}";
            var cached = await cacheStore.GetAsync(cacheKey, cancellationToken);
            if (cached != null)
            {
                return await ResolveGameServiceIdForRealmAsync(cached.RealmId, cancellationToken);
            }
        }

        try
        {
            var character = await _characterClient.GetCharacterAsync(new GetCharacterRequest
            {
                CharacterId = characterId
            }, cancellationToken);

            if (character == null)
            {
                _logger.LogError("Character lookup returned no data for character {CharacterId}", characterId);
                await _messageBus.TryPublishErrorAsync(
                    "analytics",
                    "ResolveGameServiceIdForCharacter",
                    "character_lookup_empty",
                    "Character lookup returned no data",
                    dependency: "character",
                    endpoint: "service:character/get",
                    details: $"characterId:{characterId}",
                    stack: null,
                    cancellationToken: cancellationToken);
                return null;
            }

            if (cacheOptions != null)
            {
                var cacheStore = _stateStoreFactory.GetStore<CharacterRealmCacheEntry>(StateStoreDefinitions.AnalyticsSummary);
                var cacheKey = $"{CHARACTER_REALM_CACHE_PREFIX}:{characterId}";
                await cacheStore.SaveAsync(cacheKey, new CharacterRealmCacheEntry
                {
                    RealmId = character.RealmId,
                    CachedAt = DateTimeOffset.UtcNow
                }, cacheOptions, cancellationToken);
            }

            return await ResolveGameServiceIdForRealmAsync(character.RealmId, cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogError(ex, "Failed to resolve game service for character {CharacterId}", characterId);
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "ResolveGameServiceIdForCharacter",
                "character_lookup_failed",
                ex.Message,
                dependency: "character",
                endpoint: "service:character/get",
                details: $"characterId:{characterId}",
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error resolving game service for character {CharacterId}", characterId);
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "ResolveGameServiceIdForCharacter",
                "unexpected_exception",
                ex.Message,
                dependency: "character",
                endpoint: "service:character/get",
                details: $"characterId:{characterId}",
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return null;
        }
    }

    private async Task<bool> BufferAnalyticsEventAsync(
        BufferedAnalyticsEvent bufferedEvent,
        CancellationToken cancellationToken,
        bool flushAfterEnqueue = true)
    {
        if (!await EnsureSummaryStoreRedisAsync(cancellationToken))
        {
            return false;
        }

        if (bufferedEvent.Metadata != null && bufferedEvent.Metadata.Count == 0)
        {
            bufferedEvent.Metadata = null;
        }

        IStateStore<BufferedAnalyticsEvent>? bufferStore = null;
        IStateStore<object>? bufferIndexStore = null;
        string? eventKey = null;

        try
        {
            bufferStore = _stateStoreFactory.GetStore<BufferedAnalyticsEvent>(StateStoreDefinitions.AnalyticsSummary);
            bufferIndexStore = _stateStoreFactory.GetStore<object>(StateStoreDefinitions.AnalyticsSummary);
            eventKey = GetEventBufferEntryKey(bufferedEvent.EventId);

            await bufferStore.SaveAsync(eventKey, bufferedEvent, options: null, cancellationToken);
            await bufferIndexStore.SortedSetAddAsync(
                EVENT_BUFFER_INDEX_KEY,
                eventKey,
                bufferedEvent.Timestamp.ToUnixTimeMilliseconds(),
                options: null,
                cancellationToken: cancellationToken);

            if (flushAfterEnqueue)
            {
                await FlushBufferedEventsIfNeededAsync(cancellationToken);
            }

            return true;
        }
        catch (Exception ex)
        {
            try
            {
                if (bufferStore != null && eventKey != null)
                {
                    await bufferStore.DeleteAsync(eventKey, cancellationToken);
                }
                if (bufferIndexStore != null && eventKey != null)
                {
                    await bufferIndexStore.SortedSetRemoveAsync(EVENT_BUFFER_INDEX_KEY, eventKey, cancellationToken);
                }
            }
            catch (Exception cleanupException)
            {
                _logger.LogError(cleanupException,
                    "Failed to clean up buffered analytics event {EventId} after error",
                    bufferedEvent.EventId);
            }

            _logger.LogError(ex,
                "Failed to buffer analytics event {EventId} for {EntityType}:{EntityId}",
                bufferedEvent.EventId,
                bufferedEvent.EntityType,
                bufferedEvent.EntityId);
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "BufferAnalyticsEvent",
                "analytics_event_buffer_failed",
                ex.Message,
                dependency: "state",
                endpoint: "state:summary",
                details: $"eventId:{bufferedEvent.EventId}",
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
            return false;
        }
    }

    private async Task FlushBufferedEventsIfNeededAsync(CancellationToken cancellationToken)
    {
        if (!await EnsureSummaryStoreRedisAsync(cancellationToken))
        {
            return;
        }

        var bufferSize = _configuration.EventBufferSize;
        if (bufferSize <= 0)
        {
            var message = "Analytics event buffer size must be greater than zero";
            _logger.LogError(message);
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "FlushBufferedEventsIfNeeded",
                "analytics_buffer_size_invalid",
                message,
                dependency: null,
                endpoint: "config:analytics",
                details: $"bufferSize:{bufferSize}",
                stack: null,
                cancellationToken: cancellationToken);
            return;
        }

        var flushIntervalSeconds = _configuration.EventBufferFlushIntervalSeconds;
        if (flushIntervalSeconds < 0)
        {
            var message = "Analytics event buffer flush interval must be non-negative";
            _logger.LogError(message);
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "FlushBufferedEventsIfNeeded",
                "analytics_buffer_interval_invalid",
                message,
                dependency: null,
                endpoint: "config:analytics",
                details: $"flushIntervalSeconds:{flushIntervalSeconds}",
                stack: null,
                cancellationToken: cancellationToken);
            return;
        }

        var bufferIndexStore = _stateStoreFactory.GetStore<object>(StateStoreDefinitions.AnalyticsSummary);
        var bufferCount = await bufferIndexStore.SortedSetCountAsync(EVENT_BUFFER_INDEX_KEY, cancellationToken);
        if (bufferCount == 0)
        {
            return;
        }

        var shouldFlush = bufferCount >= bufferSize;
        if (!shouldFlush && flushIntervalSeconds > 0)
        {
            var oldest = await bufferIndexStore.SortedSetRangeByRankAsync(
                EVENT_BUFFER_INDEX_KEY,
                0,
                0,
                descending: false,
                cancellationToken);
            if (oldest.Count == 0)
            {
                return;
            }

            var oldestTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(oldest[0].score));
            shouldFlush = (DateTimeOffset.UtcNow - oldestTimestamp).TotalSeconds >= flushIntervalSeconds;
        }

        if (!shouldFlush)
        {
            return;
        }

        var lockExpirySeconds = Math.Max(_configuration.EventBufferLockExpiryBaseSeconds, flushIntervalSeconds > 0 ? flushIntervalSeconds * 2 : _configuration.EventBufferLockExpiryBaseSeconds);
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.AnalyticsSummary,
            BUFFER_LOCK_RESOURCE,
            Guid.NewGuid().ToString(),
            lockExpirySeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            return;
        }

        bufferCount = await bufferIndexStore.SortedSetCountAsync(EVENT_BUFFER_INDEX_KEY, cancellationToken);
        if (bufferCount == 0)
        {
            return;
        }

        shouldFlush = bufferCount >= bufferSize;
        if (!shouldFlush && flushIntervalSeconds > 0)
        {
            var oldest = await bufferIndexStore.SortedSetRangeByRankAsync(
                EVENT_BUFFER_INDEX_KEY,
                0,
                0,
                descending: false,
                cancellationToken);
            if (oldest.Count == 0)
            {
                return;
            }

            var oldestTimestamp = DateTimeOffset.FromUnixTimeMilliseconds(Convert.ToInt64(oldest[0].score));
            shouldFlush = (DateTimeOffset.UtcNow - oldestTimestamp).TotalSeconds >= flushIntervalSeconds;
        }

        if (!shouldFlush)
        {
            return;
        }

        try
        {
            var bufferStore = _stateStoreFactory.GetStore<BufferedAnalyticsEvent>(StateStoreDefinitions.AnalyticsSummary);
            await FlushBufferedEventsBatchAsync(bufferIndexStore, bufferStore, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error flushing analytics event buffer");
            await _messageBus.TryPublishErrorAsync(
                "analytics",
                "FlushBufferedEventsIfNeeded",
                "analytics_buffer_flush_failed",
                ex.Message,
                dependency: "state",
                endpoint: "state:summary",
                details: null,
                stack: ex.StackTrace,
                cancellationToken: cancellationToken);
        }
    }

    private async Task FlushBufferedEventsBatchAsync(
        IStateStore<object> bufferIndexStore,
        IStateStore<BufferedAnalyticsEvent> bufferStore,
        CancellationToken cancellationToken)
    {
        var summaryStore = _stateStoreFactory.GetStore<EntitySummaryData>(StateStoreDefinitions.AnalyticsSummary);
        var summaryOptions = BuildSummaryCacheOptions();
        var batchSize = Math.Max(1, _configuration.EventBufferSize);

        while (true)
        {
            var entries = await bufferIndexStore.SortedSetRangeByRankAsync(
                EVENT_BUFFER_INDEX_KEY,
                0,
                batchSize - 1,
                descending: false,
                cancellationToken: cancellationToken);

            if (entries.Count == 0)
            {
                return;
            }

            var eventKeys = entries.Select(e => e.member).ToList();
            var bufferedEvents = await bufferStore.GetBulkAsync(eventKeys, cancellationToken);
            var envelopes = new List<(string key, BufferedAnalyticsEvent evt)>();

            foreach (var key in eventKeys)
            {
                if (bufferedEvents.TryGetValue(key, out var bufferedEvent))
                {
                    envelopes.Add((key, bufferedEvent));
                }
                else
                {
                    await bufferIndexStore.SortedSetRemoveAsync(EVENT_BUFFER_INDEX_KEY, key, cancellationToken);
                }
            }

            if (envelopes.Count == 0)
            {
                if (entries.Count < batchSize)
                {
                    return;
                }

                continue;
            }

            var eventsByEntity = new Dictionary<string, List<(string key, BufferedAnalyticsEvent evt)>>();
            foreach (var envelope in envelopes)
            {
                var entityKey = GetEntityKey(envelope.evt.GameServiceId, envelope.evt.EntityType, envelope.evt.EntityId);
                if (!eventsByEntity.TryGetValue(entityKey, out var list))
                {
                    list = new List<(string key, BufferedAnalyticsEvent evt)>();
                    eventsByEntity[entityKey] = list;
                }
                list.Add(envelope);
            }

            foreach (var kvp in eventsByEntity)
            {
                var entityKey = kvp.Key;
                var entityEvents = kvp.Value;
                var (summary, summaryEtag) = await summaryStore.GetWithETagAsync(entityKey, cancellationToken);

                if (summary == null)
                {
                    var firstEvent = entityEvents[0].evt;
                    summary = new EntitySummaryData
                    {
                        EntityId = firstEvent.EntityId,
                        EntityType = firstEvent.EntityType,
                        GameServiceId = firstEvent.GameServiceId,
                        FirstEventAt = firstEvent.Timestamp,
                        EventCounts = new Dictionary<string, long>(),
                        Aggregates = new Dictionary<string, double>()
                    };
                }
                else
                {
                    summary.EventCounts ??= new Dictionary<string, long>();
                    summary.Aggregates ??= new Dictionary<string, double>();
                }

                var scoreEvents = new List<AnalyticsScoreUpdatedEvent>();
                var milestoneChecks = new List<(Guid gameServiceId, Guid entityId, EntityType entityType, string scoreType, double previousValue, double newValue)>();

                foreach (var envelope in entityEvents)
                {
                    var bufferedEvent = envelope.evt;
                    var hasPrevious = summary.Aggregates.TryGetValue(bufferedEvent.EventType, out var previousAggregate);
                    var previousValue = hasPrevious ? previousAggregate : 0.0;
                    var newValue = previousValue + bufferedEvent.Value;

                    summary.Aggregates[bufferedEvent.EventType] = newValue;
                    summary.EventCounts[bufferedEvent.EventType] = summary.EventCounts.GetValueOrDefault(bufferedEvent.EventType) + 1;
                    summary.TotalEvents++;

                    if (summary.FirstEventAt == default || bufferedEvent.Timestamp < summary.FirstEventAt)
                    {
                        summary.FirstEventAt = bufferedEvent.Timestamp;
                    }
                    if (summary.LastEventAt == default || bufferedEvent.Timestamp > summary.LastEventAt)
                    {
                        summary.LastEventAt = bufferedEvent.Timestamp;
                    }

                    if (bufferedEvent.Value != 0)
                    {
                        scoreEvents.Add(new AnalyticsScoreUpdatedEvent
                        {
                            EventId = Guid.NewGuid(),
                            Timestamp = DateTimeOffset.UtcNow,
                            GameServiceId = bufferedEvent.GameServiceId,
                            EntityId = bufferedEvent.EntityId,
                            EntityType = MapToScoreEventEntityType(bufferedEvent.EntityType),
                            ScoreType = bufferedEvent.EventType,
                            PreviousValue = hasPrevious ? previousValue : null,
                            NewValue = newValue,
                            Delta = bufferedEvent.Value,
                            SessionId = bufferedEvent.SessionId
                        });
                        milestoneChecks.Add((bufferedEvent.GameServiceId, bufferedEvent.EntityId, bufferedEvent.EntityType, bufferedEvent.EventType, previousValue, newValue));
                    }
                }

                var summarySaved = false;
                try
                {
                    var newSummaryEtag = await summaryStore.TrySaveAsync(entityKey, summary, summaryEtag ?? string.Empty, cancellationToken);
                    if (newSummaryEtag == null)
                    {
                        _logger.LogWarning("Concurrent modification detected for analytics summary {EntityKey}, skipping batch", entityKey);
                        continue;
                    }
                    summarySaved = true;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error saving analytics summary for {EntityKey}", entityKey);
                    await _messageBus.TryPublishErrorAsync(
                        "analytics",
                        "FlushBufferedEventsBatch",
                        "analytics_summary_save_failed",
                        ex.Message,
                        dependency: "state",
                        endpoint: "state:summary",
                        details: $"entityKey:{entityKey}",
                        stack: ex.StackTrace,
                        cancellationToken: cancellationToken);
                }

                if (!summarySaved)
                {
                    continue;
                }

                try
                {
                    await summaryStore.AddToSetAsync(
                        GetSummaryIndexKey(summary.GameServiceId),
                        entityKey,
                        summaryOptions,
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error updating analytics summary index for {EntityKey}", entityKey);
                    await _messageBus.TryPublishErrorAsync(
                        "analytics",
                        "FlushBufferedEventsBatch",
                        "analytics_summary_index_failed",
                        ex.Message,
                        dependency: "state",
                        endpoint: "state:summary",
                        details: $"entityKey:{entityKey}",
                        stack: ex.StackTrace,
                        cancellationToken: cancellationToken);
                }

                foreach (var scoreEvent in scoreEvents)
                {
                    await _messageBus.TryPublishAsync(
                        "analytics.score.updated",
                        scoreEvent,
                        cancellationToken: cancellationToken);
                }

                foreach (var milestone in milestoneChecks)
                {
                    await CheckAndPublishMilestoneAsync(
                        milestone.gameServiceId,
                        milestone.entityId,
                        milestone.entityType,
                        milestone.scoreType,
                        milestone.previousValue,
                        milestone.newValue,
                        cancellationToken);
                }

                foreach (var envelope in entityEvents)
                {
                    await bufferStore.DeleteAsync(envelope.key, cancellationToken);
                    await bufferIndexStore.SortedSetRemoveAsync(EVENT_BUFFER_INDEX_KEY, envelope.key, cancellationToken);
                }
            }

            if (entries.Count < batchSize)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Checks if a milestone has been crossed and publishes event if so.
    /// A milestone is considered crossed when previousValue was below the threshold
    /// and newValue is at or above it. This ensures each milestone is only triggered
    /// once when the entity first reaches it, not on subsequent updates.
    /// </summary>
    private async Task CheckAndPublishMilestoneAsync(
        Guid gameServiceId,
        Guid entityId,
        EntityType entityType,
        string scoreType,
        double previousValue,
        double newValue,
        CancellationToken cancellationToken)
    {
        // Common milestone thresholds
        int[] milestones = { 10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10000, 25000, 50000, 100000 };

        foreach (var milestone in milestones)
        {
            // Check if we just crossed this milestone: was below, now at or above
            if (previousValue < milestone && newValue >= milestone)
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

    #region Permission Registration

    /// <summary>
    /// Registers this service's API permissions with the Permission service on startup.
    /// Overrides the default IBannouService implementation to use generated permission data.
    /// </summary>
    public async Task RegisterServicePermissionsAsync(string appId)
    {
        _logger.LogInformation("Registering Analytics service permissions...");
        await AnalyticsPermissionRegistration.RegisterViaEventAsync(_messageBus, appId, _logger);
    }

    #endregion
}

#region Internal Data Models

/// <summary>
/// Buffered analytics event stored prior to summary aggregation.
/// </summary>
internal sealed class BufferedAnalyticsEvent
{
    public Guid EventId { get; set; }
    public Guid GameServiceId { get; set; }
    public Guid EntityId { get; set; }
    public EntityType EntityType { get; set; }
    public string EventType { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public double Value { get; set; }
    public Guid? SessionId { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Cached mapping for game service IDs by stub name.
/// </summary>
internal sealed class GameServiceCacheEntry
{
    public Guid ServiceId { get; set; }
    public DateTimeOffset CachedAt { get; set; }
}

/// <summary>
/// Cached mapping between game sessions and game service IDs.
/// </summary>
internal sealed class GameSessionMappingData
{
    public Guid SessionId { get; set; }
    public string GameType { get; set; } = string.Empty;
    public Guid GameServiceId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

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

/// <summary>
/// Cached mapping between realm IDs and game service IDs.
/// </summary>
internal sealed class RealmGameServiceCacheEntry
{
    public Guid GameServiceId { get; set; }
    public DateTimeOffset CachedAt { get; set; }
}

/// <summary>
/// Cached mapping between character IDs and realm IDs.
/// </summary>
internal sealed class CharacterRealmCacheEntry
{
    public Guid RealmId { get; set; }
    public DateTimeOffset CachedAt { get; set; }
}

#endregion
