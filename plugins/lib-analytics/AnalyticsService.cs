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
using System.Diagnostics;
using System.Linq;
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
    private readonly IGameServiceClient _gameServiceClient;
    private readonly IGameSessionClient _gameSessionClient;
    private readonly IRealmClient _realmClient;
    private readonly ICharacterClient _characterClient;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly ILogger<AnalyticsService> _logger;
    private readonly AnalyticsServiceConfiguration _configuration;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>Entity summary data store (MySQL) for durable analytics summary persistence.</summary>
    private readonly IStateStore<EntitySummaryData> _summaryDataStore;

    /// <summary>JSON-queryable entity summary data store (MySQL) for server-side filtered queries.</summary>
    private readonly IJsonQueryableStateStore<EntitySummaryData> _summaryDataQueryStore;

    /// <summary>Skill rating store (Redis) for Glicko-2 rating data.</summary>
    private readonly IStateStore<SkillRatingData> _ratingStore;

    /// <summary>Controller history store (MySQL) for durable history records.</summary>
    private readonly IStateStore<ControllerHistoryData> _historyDataStore;

    /// <summary>JSON-queryable controller history store (MySQL) for server-side filtered queries.</summary>
    private readonly IJsonQueryableStateStore<ControllerHistoryData> _historyDataQueryStore;

    /// <summary>Game service resolution cache store (Redis) for stub-name-to-ID lookups.</summary>
    private readonly IStateStore<GameServiceCacheEntry> _gameServiceCacheStore;

    /// <summary>Game session mapping store (Redis) for session-to-gameService ID lookups.</summary>
    private readonly IStateStore<GameSessionMappingData> _sessionMappingStore;

    /// <summary>Realm-to-gameService resolution cache store (Redis).</summary>
    private readonly IStateStore<RealmGameServiceCacheEntry> _realmGameServiceCacheStore;

    /// <summary>Character-to-realm resolution cache store (Redis).</summary>
    private readonly IStateStore<CharacterRealmCacheEntry> _characterRealmCacheStore;

    /// <summary>Cacheable event buffer store (Redis) for buffered analytics event entries.</summary>
    private readonly ICacheableStateStore<BufferedAnalyticsEvent> _eventBufferStore;

    /// <summary>Cacheable event buffer index store (Redis) for sorted set buffer index.</summary>
    private readonly ICacheableStateStore<object> _eventBufferIndexStore;

    /// <summary>String store on analytics-rating (Redis) for account→rating-key reverse indexes.</summary>
    private readonly IStateStore<string> _ratingIndexStore;

    /// <summary>Whether the analytics summary store backend is Redis (required for buffered ingestion).</summary>
    private readonly bool _summaryStoreIsRedis;

    // State store key prefixes per FOUNDATION TENETS (Build*Key pattern)
    private const string ENTITY_KEY_PREFIX = "analytics-entity:";
    private const string RATING_KEY_PREFIX = "analytics-rating:";
    private const string CONTROLLER_KEY_PREFIX = "analytics-controller:";
    private const string EVENT_BUFFER_INDEX_KEY = "analytics-event-buffer-index";
    private const string EVENT_BUFFER_ENTRY_PREFIX = "analytics-event-buffer-entry";
    private const string SESSION_MAPPING_PREFIX = "analytics-session-mapping";
    private const string GAME_SERVICE_CACHE_PREFIX = "analytics-game-service-cache";
    private const string REALM_GAME_SERVICE_CACHE_PREFIX = "analytics-realm-game-service-cache";
    private const string CHARACTER_REALM_CACHE_PREFIX = "analytics-character-realm-cache";
    private const string BUFFER_LOCK_RESOURCE = "analytics-event-buffer-flush";
    private const string ACCOUNT_RATING_INDEX_PREFIX = "account-rating-index:";

    // Glicko-2 scale conversion constant
    private const double GlickoScale = 173.7178;

    // Cached parsed milestone thresholds from configuration
    private readonly int[] _milestoneThresholds;

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
        IEventConsumer eventConsumer,
        ITelemetryProvider telemetryProvider)
    {
        _messageBus = messageBus;
        _gameServiceClient = gameServiceClient;
        _gameSessionClient = gameSessionClient;
        _realmClient = realmClient;
        _characterClient = characterClient;
        _lockProvider = lockProvider;
        _logger = logger;
        _configuration = configuration;
        ArgumentNullException.ThrowIfNull(telemetryProvider, nameof(telemetryProvider));
        _telemetryProvider = telemetryProvider;

        // Constructor-cache all state store references per FOUNDATION TENETS
        _summaryDataStore = stateStoreFactory.GetStore<EntitySummaryData>(StateStoreDefinitions.AnalyticsSummaryData);
        _summaryDataQueryStore = stateStoreFactory.GetJsonQueryableStore<EntitySummaryData>(StateStoreDefinitions.AnalyticsSummaryData);
        _ratingStore = stateStoreFactory.GetStore<SkillRatingData>(StateStoreDefinitions.AnalyticsRating);
        _historyDataStore = stateStoreFactory.GetStore<ControllerHistoryData>(StateStoreDefinitions.AnalyticsHistoryData);
        _historyDataQueryStore = stateStoreFactory.GetJsonQueryableStore<ControllerHistoryData>(StateStoreDefinitions.AnalyticsHistoryData);
        _gameServiceCacheStore = stateStoreFactory.GetStore<GameServiceCacheEntry>(StateStoreDefinitions.AnalyticsSummary);
        _sessionMappingStore = stateStoreFactory.GetStore<GameSessionMappingData>(StateStoreDefinitions.AnalyticsSummary);
        _realmGameServiceCacheStore = stateStoreFactory.GetStore<RealmGameServiceCacheEntry>(StateStoreDefinitions.AnalyticsSummary);
        _characterRealmCacheStore = stateStoreFactory.GetStore<CharacterRealmCacheEntry>(StateStoreDefinitions.AnalyticsSummary);
        _eventBufferStore = stateStoreFactory.GetCacheableStore<BufferedAnalyticsEvent>(StateStoreDefinitions.AnalyticsSummary);
        _eventBufferIndexStore = stateStoreFactory.GetCacheableStore<object>(StateStoreDefinitions.AnalyticsSummary);
        _ratingIndexStore = stateStoreFactory.GetStore<string>(StateStoreDefinitions.AnalyticsRating);
        _summaryStoreIsRedis = stateStoreFactory.GetBackendType(StateStoreDefinitions.AnalyticsSummary) == StateBackend.Redis;

        // Parse milestone thresholds from configuration array
        _milestoneThresholds = ParseMilestoneThresholds(configuration.MilestoneThresholds);

        RegisterEventConsumers(eventConsumer);
    }

    /// <summary>
    /// Parses milestone thresholds from configuration array.
    /// </summary>
    private static int[] ParseMilestoneThresholds(string[] thresholdsConfig)
    {
        if (thresholdsConfig.Length == 0)
        {
            return Array.Empty<int>();
        }

        return thresholdsConfig
            .Select(s => int.TryParse(s, out var v) ? v : (int?)null)
            .OfType<int>()
            .OrderBy(v => v)
            .ToArray();
    }

    /// <summary>
    /// Builds a composite key for entity data using polymorphic pattern.
    /// Format: {ENTITY_KEY_PREFIX}{serviceType}:{serviceId}:{entityType}:{entityId}
    /// </summary>
    internal static string BuildEntityKey(AnalyticsServiceType serviceType, string serviceId, EntityType entityType, Guid entityId)
        => $"{ENTITY_KEY_PREFIX}{serviceType}:{serviceId}:{entityType}:{entityId}";

    /// <summary>
    /// Builds a key for skill rating data.
    /// Format: {RATING_KEY_PREFIX}{serviceType}:{serviceId}:{ratingType}:{entityType}:{entityId}
    /// </summary>
    internal static string BuildRatingKey(AnalyticsServiceType serviceType, string serviceId, string ratingType, EntityType entityType, Guid entityId)
        => $"{RATING_KEY_PREFIX}{serviceType}:{serviceId}:{ratingType}:{entityType}:{entityId}";

    /// <summary>
    /// Builds a key for controller history.
    /// Format: {CONTROLLER_KEY_PREFIX}{serviceType}:{serviceId}:{accountId}:{timestamp}
    /// </summary>
    internal static string BuildControllerKey(AnalyticsServiceType serviceType, string serviceId, Guid accountId, DateTimeOffset timestamp)
        => $"{CONTROLLER_KEY_PREFIX}{serviceType}:{serviceId}:{accountId}:{timestamp:o}";

    /// <summary>
    /// Implementation of IngestEvent operation.
    /// Ingests a single analytics event and updates entity summaries.
    /// </summary>
    public async Task<(StatusCodes, IngestEventResponse?)> IngestEventAsync(IngestEventRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Ingesting analytics event for entity {EntityType}:{EntityId}", body.EntityType, body.EntityId);

        {
            var bufferedEvent = new BufferedAnalyticsEvent
            {
                EventId = Guid.NewGuid(),
                ServiceType = body.ServiceType,
                ServiceId = body.ServiceId,
                EntityId = body.EntityId,
                EntityType = body.EntityType,
                EventType = body.EventType,
                Timestamp = body.Timestamp,
                Value = body.Value,
                SessionId = body.SessionId,
                Metadata = body.Metadata
            };

            var buffered = await BufferAnalyticsEventAsync(bufferedEvent, cancellationToken);
            if (!buffered)
            {
                return (StatusCodes.InternalServerError, null);
            }

            return (StatusCodes.OK, new IngestEventResponse
            {
                EventId = bufferedEvent.EventId
            });
        }
    }

    /// <summary>
    /// Implementation of IngestEventBatch operation.
    /// Ingests multiple analytics events efficiently.
    /// </summary>
    public async Task<(StatusCodes, IngestEventBatchResponse?)> IngestEventBatchAsync(IngestEventBatchRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Ingesting batch of {Count} analytics events", body.Events.Count);

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
                    ServiceType = evt.ServiceType,
                    ServiceId = evt.ServiceId,
                    EntityId = evt.EntityId,
                    EntityType = evt.EntityType,
                    EventType = evt.EventType,
                    Timestamp = evt.Timestamp,
                    Value = evt.Value,
                    SessionId = evt.SessionId,
                    Metadata = evt.Metadata
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

    /// <summary>
    /// Implementation of GetEntitySummary operation.
    /// Retrieves aggregated statistics for an entity.
    /// </summary>
    public async Task<(StatusCodes, EntitySummaryResponse?)> GetEntitySummaryAsync(GetEntitySummaryRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting entity summary for {EntityType}:{EntityId}", body.EntityType, body.EntityId);

        {
            var entityKey = BuildEntityKey(body.ServiceType, body.ServiceId, body.EntityType, body.EntityId);

            var summary = await _summaryDataStore.GetAsync(entityKey, cancellationToken);
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
    }

    /// <summary>
    /// Implementation of QueryEntitySummaries operation.
    /// Queries multiple entity summaries with filtering.
    /// </summary>
    public async Task<(StatusCodes, QueryEntitySummariesResponse?)> QueryEntitySummariesAsync(QueryEntitySummariesRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Querying entity summaries for service {ServiceType}:{ServiceId}", body.ServiceType, body.ServiceId);

        {
            if (body.Limit <= 0)
            {
                _logger.LogDebug("Invalid limit {Limit} for analytics summary query", body.Limit);
                return (StatusCodes.BadRequest, null);
            }

            if (body.Offset < 0)
            {
                _logger.LogDebug("Invalid offset {Offset} for analytics summary query", body.Offset);
                return (StatusCodes.BadRequest, null);
            }

            if (body.MinEvents < 0)
            {
                _logger.LogDebug("Invalid minEvents {MinEvents} for analytics summary query", body.MinEvents);
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
                    _logger.LogDebug("Unsupported sortBy value {SortBy} for analytics summary query", body.SortBy);
                    return (StatusCodes.BadRequest, null);
                }
            }

            var conditions = new List<QueryCondition>
            {
                new QueryCondition
                {
                    Path = "$.ServiceType",
                    Operator = QueryOperator.Equals,
                    Value = body.ServiceType.ToString()
                },
                new QueryCondition
                {
                    Path = "$.ServiceId",
                    Operator = QueryOperator.Equals,
                    Value = body.ServiceId
                }
            };

            if (body.EntityType.HasValue)
            {
                conditions.Add(new QueryCondition
                {
                    Path = "$.EntityType",
                    Operator = QueryOperator.Equals,
                    Value = (int)body.EntityType.Value
                });
            }

            if (!string.IsNullOrEmpty(body.EventType))
            {
                conditions.Add(new QueryCondition
                {
                    Path = $"$.EventCounts.{body.EventType}",
                    Operator = QueryOperator.Exists,
                    Value = true
                });
            }

            if (body.MinEvents > 0)
            {
                conditions.Add(new QueryCondition
                {
                    Path = "$.TotalEvents",
                    Operator = QueryOperator.GreaterThanOrEqual,
                    Value = body.MinEvents
                });
            }

            JsonSortSpec? sortSpec = null;
            if (!string.IsNullOrWhiteSpace(body.SortBy))
            {
                var sortPath = body.SortBy.Trim().ToLowerInvariant() switch
                {
                    "totalevents" => "$.TotalEvents",
                    "firsteventat" => "$.FirstEventAt",
                    "lasteventat" => "$.LastEventAt",
                    "eventcount" when !string.IsNullOrEmpty(body.EventType) => $"$.EventCounts.{body.EventType}",
                    _ => (string?)null
                };
                if (sortPath != null)
                {
                    sortSpec = new JsonSortSpec { Path = sortPath, Descending = body.SortDescending };
                }
            }

            var result = await _summaryDataQueryStore.JsonQueryPagedAsync(
                conditions,
                body.Offset,
                body.Limit,
                sortSpec,
                cancellationToken);

            var mapped = result.Items
                .Select(item => new EntitySummaryResponse
                {
                    EntityId = item.Value.EntityId,
                    EntityType = item.Value.EntityType,
                    TotalEvents = item.Value.TotalEvents,
                    FirstEventAt = item.Value.FirstEventAt,
                    LastEventAt = item.Value.LastEventAt,
                    EventCounts = item.Value.EventCounts,
                    Aggregates = item.Value.Aggregates
                })
                .ToList();

            return (StatusCodes.OK, new QueryEntitySummariesResponse
            {
                Summaries = mapped,
                Total = (int)result.TotalCount
            });
        }
    }

    /// <summary>
    /// Implementation of GetSkillRating operation.
    /// Retrieves Glicko-2 skill rating for an entity.
    /// </summary>
    public async Task<(StatusCodes, SkillRatingResponse?)> GetSkillRatingAsync(GetSkillRatingRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting skill rating for {EntityType}:{EntityId}, type {RatingType}",
            body.EntityType, body.EntityId, body.RatingType);

        var ratingKey = BuildRatingKey(body.ServiceType, body.ServiceId, body.RatingType, body.EntityType, body.EntityId);

        var rating = await _ratingStore.GetAsync(ratingKey, cancellationToken);
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

    /// <summary>
    /// Implementation of UpdateSkillRating operation.
    /// Updates Glicko-2 skill ratings after a match.
    /// </summary>
    public async Task<(StatusCodes, UpdateSkillRatingResponse?)> UpdateSkillRatingAsync(UpdateSkillRatingRequest body, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Updating skill ratings for match {MatchId} with {Count} participants",
            body.MatchId, body.Results.Count);

        if (body.Results.Count < 2)
        {
            return (StatusCodes.BadRequest, null);
        }

        var now = DateTimeOffset.UtcNow;

        // Acquire distributed lock to serialize rating updates for this game+type combination
        var lockResourceId = $"rating-update:{body.ServiceType}:{body.ServiceId}:{body.RatingType}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.AnalyticsRating,
            lockResourceId,
            Guid.NewGuid().ToString(),
            _configuration.RatingUpdateLockExpirySeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogWarning("Failed to acquire rating update lock for {ResourceId} during match {MatchId}",
                lockResourceId, body.MatchId);
            return (StatusCodes.Conflict, null);
        }

        // Load all current ratings (no ETags needed - lock provides exclusivity)
        var currentRatings = new Dictionary<string, SkillRatingData>();
        foreach (var result in body.Results)
        {
            var key = BuildRatingKey(body.ServiceType, body.ServiceId, body.RatingType, result.EntityType, result.EntityId);
            var rating = await _ratingStore.GetAsync(key, cancellationToken);
            currentRatings[key] = rating ?? new SkillRatingData
            {
                EntityId = result.EntityId,
                EntityType = result.EntityType,
                RatingType = body.RatingType,
                ServiceType = body.ServiceType,
                ServiceId = body.ServiceId,
                Rating = _configuration.Glicko2DefaultRating,
                RatingDeviation = _configuration.Glicko2DefaultDeviation,
                Volatility = _configuration.Glicko2DefaultVolatility,
                MatchesPlayed = 0
            };
        }

        // Snapshot pre-match ratings for opponent lookups (prevents mutation bug)
        var originalRatings = currentRatings.ToDictionary(
            kvp => kvp.Key,
            kvp => (Rating: kvp.Value.Rating, RD: kvp.Value.RatingDeviation, Volatility: kvp.Value.Volatility));

        // Calculate ALL new ratings using pre-match snapshots for opponent lookups
        var calculatedResults = new List<(string Key, MatchResult Result, double NewRating, double NewRD, double NewVolatility, double PreviousRating)>();
        foreach (var result in body.Results)
        {
            var key = BuildRatingKey(body.ServiceType, body.ServiceId, body.RatingType, result.EntityType, result.EntityId);
            var playerRating = currentRatings[key];
            var previousRating = playerRating.Rating;

            // Calculate opponents' combined effect using pairwise outcomes and ORIGINAL ratings
            var opponents = body.Results.Where(r => r.EntityId != result.EntityId).ToList();
            var opponentData = opponents.Select(o =>
            {
                var oppKey = BuildRatingKey(body.ServiceType, body.ServiceId, body.RatingType, o.EntityType, o.EntityId);
                var oppOriginal = originalRatings[oppKey];
                var opponentSnapshot = new SkillRatingData
                {
                    Rating = oppOriginal.Rating,
                    RatingDeviation = oppOriginal.RD,
                    Volatility = oppOriginal.Volatility
                };
                var pairwiseOutcome = result.Outcome > o.Outcome ? 1.0
                    : result.Outcome < o.Outcome ? 0.0
                    : 0.5;
                return (opponentSnapshot, pairwiseOutcome);
            }).ToList();

            var (newRating, newRD, newVolatility) = CalculateGlicko2Update(playerRating, opponentData);
            calculatedResults.Add((key, result, newRating, newRD, newVolatility, previousRating));
        }

        // Apply all calculated values and save (all-or-nothing under lock)
        var updatedRatings = new List<SkillRatingChange>();
        foreach (var (key, result, newRating, newRD, newVolatility, previousRating) in calculatedResults)
        {
            var playerRating = currentRatings[key];
            playerRating.Rating = newRating;
            playerRating.RatingDeviation = newRD;
            playerRating.Volatility = newVolatility;
            playerRating.MatchesPlayed++;
            playerRating.LastMatchAt = now;

            await _ratingStore.SaveAsync(key, playerRating, cancellationToken: cancellationToken);

            // Maintain reverse index for account.deleted cleanup per FOUNDATION TENETS
            if (result.EntityType == EntityType.Account)
            {
                await _ratingIndexStore.AddToStringListAsync(
                    BuildAccountRatingIndexKey(result.EntityId),
                    key,
                    _configuration.RatingIndexMaxRetries,
                    _logger,
                    cancellationToken);
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
        }

        // Publish all events after all saves complete
        foreach (var (key, result, newRating, newRD, newVolatility, previousRating) in calculatedResults)
        {
            var ratingChange = newRating - previousRating;
            var ratingEvent = new AnalyticsRatingUpdatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                ServiceType = body.ServiceType,
                ServiceId = body.ServiceId,
                EntityId = result.EntityId,
                EntityType = result.EntityType,
                RatingType = body.RatingType,
                PreviousRating = previousRating,
                NewRating = newRating,
                RatingChange = ratingChange,
                NewRatingDeviation = newRD,
                MatchId = body.MatchId
            };
            await _messageBus.PublishAnalyticsRatingUpdatedAsync(ratingEvent, cancellationToken);
        }

        return (StatusCodes.OK, new UpdateSkillRatingResponse
        {
            UpdatedRatings = updatedRatings
        });
    }

    /// <summary>
    /// Implementation of RecordControllerEvent operation.
    /// Records a controller possession/release event for history tracking.
    /// </summary>
    public async Task<StatusCodes> RecordControllerEventAsync(RecordControllerEventRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Recording controller {Action} event: account {AccountId} -> {EntityType}:{EntityId}",
            body.Action, body.AccountId, body.TargetEntityType, body.TargetEntityId);

        var eventId = Guid.NewGuid();
        var key = BuildControllerKey(body.ServiceType, body.ServiceId, body.AccountId, body.Timestamp);

        var historyEvent = new ControllerHistoryData
        {
            EventId = eventId,
            ServiceType = body.ServiceType,
            ServiceId = body.ServiceId,
            AccountId = body.AccountId,
            TargetEntityId = body.TargetEntityId,
            TargetEntityType = body.TargetEntityType,
            Action = body.Action,
            Timestamp = body.Timestamp,
            SessionId = body.SessionId
        };

        await _historyDataStore.SaveAsync(key, historyEvent, options: null, cancellationToken);

        var controllerEvent = new AnalyticsControllerRecordedEvent
        {
            EventId = eventId,
            Timestamp = body.Timestamp,
            ServiceType = body.ServiceType,
            ServiceId = body.ServiceId,
            AccountId = body.AccountId,
            TargetEntityId = body.TargetEntityId,
            TargetEntityType = body.TargetEntityType,
            Action = body.Action,
            SessionId = body.SessionId
        };
        await _messageBus.PublishAnalyticsControllerRecordedAsync(controllerEvent, cancellationToken);

        return StatusCodes.OK;
    }

    /// <summary>
    /// Implementation of QueryControllerHistory operation.
    /// Queries controller possession/release history.
    /// </summary>
    public async Task<(StatusCodes, QueryControllerHistoryResponse?)> QueryControllerHistoryAsync(QueryControllerHistoryRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Querying controller history for service {ServiceType}:{ServiceId}", body.ServiceType, body.ServiceId);

        {
            if (body.Limit <= 0)
            {
                _logger.LogDebug("Invalid limit {Limit} for controller history query", body.Limit);
                return (StatusCodes.BadRequest, null);
            }

            if (body.Offset < 0)
            {
                _logger.LogDebug("Invalid offset {Offset} for controller history query", body.Offset);
                return (StatusCodes.BadRequest, null);
            }

            if (body.StartTime.HasValue && body.EndTime.HasValue && body.StartTime > body.EndTime)
            {
                _logger.LogDebug("Invalid controller history time range {StartTime} to {EndTime}",
                    body.StartTime, body.EndTime);
                return (StatusCodes.BadRequest, null);
            }

            var conditions = new List<QueryCondition>
            {
                new QueryCondition
                {
                    Path = "$.ServiceType",
                    Operator = QueryOperator.Equals,
                    Value = body.ServiceType.ToString()
                },
                new QueryCondition
                {
                    Path = "$.ServiceId",
                    Operator = QueryOperator.Equals,
                    Value = body.ServiceId
                }
            };

            if (body.AccountId.HasValue)
            {
                conditions.Add(new QueryCondition
                {
                    Path = "$.AccountId",
                    Operator = QueryOperator.Equals,
                    Value = body.AccountId.Value.ToString()
                });
            }

            if (body.TargetEntityId.HasValue)
            {
                conditions.Add(new QueryCondition
                {
                    Path = "$.TargetEntityId",
                    Operator = QueryOperator.Equals,
                    Value = body.TargetEntityId.Value.ToString()
                });
            }

            if (body.TargetEntityType.HasValue)
            {
                conditions.Add(new QueryCondition
                {
                    Path = "$.TargetEntityType",
                    Operator = QueryOperator.Equals,
                    Value = (int)body.TargetEntityType.Value
                });
            }

            if (body.StartTime.HasValue)
            {
                conditions.Add(new QueryCondition
                {
                    Path = "$.Timestamp",
                    Operator = QueryOperator.GreaterThanOrEqual,
                    Value = body.StartTime.Value.ToString("o")
                });
            }

            if (body.EndTime.HasValue)
            {
                conditions.Add(new QueryCondition
                {
                    Path = "$.Timestamp",
                    Operator = QueryOperator.LessThanOrEqual,
                    Value = body.EndTime.Value.ToString("o")
                });
            }

            var sortSpec = new JsonSortSpec { Path = "$.Timestamp", Descending = true };

            var result = await _historyDataQueryStore.JsonQueryPagedAsync(
                conditions,
                body.Offset,
                body.Limit,
                sortSpec,
                cancellationToken);

            var events = result.Items
                .Select(item => new ControllerHistoryEvent
                {
                    EventId = item.Value.EventId,
                    AccountId = item.Value.AccountId,
                    TargetEntityId = item.Value.TargetEntityId,
                    TargetEntityType = item.Value.TargetEntityType,
                    Action = item.Value.Action,
                    Timestamp = item.Value.Timestamp,
                    SessionId = item.Value.SessionId
                })
                .ToList();

            return (StatusCodes.OK, new QueryControllerHistoryResponse
            {
                Events = events
            });
        }
    }

    /// <summary>
    /// Implementation of CleanupControllerHistory operation.
    /// Deletes controller history records older than the configured retention period.
    /// </summary>
    public async Task<(StatusCodes, CleanupControllerHistoryResponse?)> CleanupControllerHistoryAsync(
        CleanupControllerHistoryRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Cleaning up controller history (dryRun={DryRun}, olderThanDays={OlderThanDays}, serviceType={ServiceType}, serviceId={ServiceId})",
            body.DryRun, body.OlderThanDays, body.ServiceType, body.ServiceId);

        var retentionDays = body.OlderThanDays ?? _configuration.ControllerHistoryRetentionDays;
        if (retentionDays <= 0)
        {
            _logger.LogInformation("Controller history cleanup skipped: retention days is 0 (indefinite retention)");
            return (StatusCodes.OK, new CleanupControllerHistoryResponse
            {
                RecordsDeleted = 0
            });
        }

        var cutoffTime = DateTimeOffset.UtcNow.AddDays(-retentionDays);

        var conditions = new List<QueryCondition>
            {
                new QueryCondition
                {
                    Path = "$.Timestamp",
                    Operator = QueryOperator.LessThan,
                    Value = cutoffTime.ToString("o")
                }
            };

        if (body.ServiceType.HasValue && body.ServiceId != null)
        {
            conditions.Add(new QueryCondition
            {
                Path = "$.ServiceType",
                Operator = QueryOperator.Equals,
                Value = body.ServiceType.Value.ToString()
            });
            conditions.Add(new QueryCondition
            {
                Path = "$.ServiceId",
                Operator = QueryOperator.Equals,
                Value = body.ServiceId
            });
        }

        if (body.DryRun)
        {
            var count = await _historyDataQueryStore.JsonCountAsync(conditions, cancellationToken);
            _logger.LogInformation("Controller history cleanup dry run: {Count} records would be deleted", count);
            return (StatusCodes.OK, new CleanupControllerHistoryResponse
            {
                RecordsDeleted = count
            });
        }

        var batchSize = _configuration.ControllerHistoryCleanupBatchSize;
        var totalDeleted = 0L;

        // Delete in batches to avoid overwhelming the database (per IMPLEMENTATION TENETS: use configuration)
        while (totalDeleted < batchSize)
        {
            var batchLimit = Math.Min(_configuration.ControllerHistoryCleanupSubBatchSize, (int)(batchSize - totalDeleted));
            var batch = await _historyDataQueryStore.JsonQueryPagedAsync(
                conditions, 0, batchLimit, null, cancellationToken);

            if (batch.Items.Count == 0)
            {
                break;
            }

            foreach (var item in batch.Items)
            {
                await _historyDataStore.DeleteAsync(item.Key, cancellationToken);
                totalDeleted++;
            }
        }

        _logger.LogInformation("Controller history cleanup completed: {DeletedCount} records deleted", totalDeleted);
        return (StatusCodes.OK, new CleanupControllerHistoryResponse
        {
            RecordsDeleted = totalDeleted
        });
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

        // Clamp values to reasonable ranges (per IMPLEMENTATION TENETS: use configuration, not hardcoded tunables)
        newRating = Math.Clamp(newRating, _configuration.Glicko2MinRating, _configuration.Glicko2MaxRating);
        newRD = Math.Clamp(newRD, _configuration.Glicko2MinDeviation, maxDeviation);

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
            if (iterations >= _configuration.Glicko2MaxVolatilityIterations)
            {
                _logger.LogWarning("Glicko-2 upper bound search did not converge after {MaxIterations} iterations, returning current sigma {Sigma}", _configuration.Glicko2MaxVolatilityIterations, sigma);
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
            if (iterations >= _configuration.Glicko2MaxVolatilityIterations)
            {
                _logger.LogWarning("Glicko-2 Illinois algorithm did not converge after {MaxIterations} iterations, returning current sigma {Sigma}", _configuration.Glicko2MaxVolatilityIterations, sigma);
                return sigma;
            }
        }

        return Math.Exp(upperBound / 2);
    }

    #endregion

    #region Key Building Helpers

    /// <summary>
    /// Builds the key for a buffered analytics event entry.
    /// </summary>
    internal static string BuildEventBufferEntryKey(Guid eventId)
        => $"{EVENT_BUFFER_ENTRY_PREFIX}:{eventId}";

    /// <summary>
    /// Builds the cache key for a game service stub.
    /// </summary>
    internal static string BuildGameServiceCacheKey(string stubName)
        => $"{GAME_SERVICE_CACHE_PREFIX}:{stubName}";

    /// <summary>
    /// Builds the cache key for a game session mapping.
    /// </summary>
    internal static string BuildSessionMappingKey(Guid sessionId)
        => $"{SESSION_MAPPING_PREFIX}:{sessionId}";

    /// <summary>
    /// Builds the cache key for a realm's game service lookup.
    /// </summary>
    internal static string BuildRealmGameServiceCacheKey(Guid realmId)
        => $"{REALM_GAME_SERVICE_CACHE_PREFIX}:{realmId}";

    /// <summary>
    /// Builds the cache key for a character's realm lookup.
    /// </summary>
    internal static string BuildCharacterRealmCacheKey(Guid characterId)
        => $"{CHARACTER_REALM_CACHE_PREFIX}:{characterId}";

    /// <summary>
    /// Builds the reverse index key for an account's skill ratings.
    /// Used to look up all rating keys for cleanup on account.deleted.
    /// </summary>
    internal static string BuildAccountRatingIndexKey(Guid accountId)
        => $"{ACCOUNT_RATING_INDEX_PREFIX}{accountId}";

    private StateOptions? BuildResolutionCacheOptions()
    {
        if (_configuration.ResolutionCacheTtlSeconds <= 0)
        {
            return null;
        }

        return new StateOptions { Ttl = _configuration.ResolutionCacheTtlSeconds };
    }

    private StateOptions? BuildSessionMappingCacheOptions()
    {
        if (_configuration.SessionMappingTtlSeconds <= 0)
        {
            return null;
        }

        return new StateOptions { Ttl = _configuration.SessionMappingTtlSeconds };
    }


    #endregion

    #region Permission Registration

    #endregion
}
