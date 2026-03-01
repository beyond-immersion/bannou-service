using BeyondImmersion.Bannou.Core;
using BeyondImmersion.Bannou.Worldstate.ClientEvents;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.GameService;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Realm;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using BeyondImmersion.BannouService.Telemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Worldstate;

/// <summary>
/// Per-realm game time authority, calendar system, and temporal event broadcasting service.
/// Maps real-world time to configurable game-time progression with per-realm time ratios,
/// calendar templates, and day-period cycles. Publishes boundary events at game-time transitions
/// and provides the ${world.*} variable namespace for ABML behavior expressions.
/// </summary>
[BannouService("worldstate", typeof(IWorldstateService), lifetime: ServiceLifetime.Scoped, layer: ServiceLayer.GameFoundation)]
public partial class WorldstateService : IWorldstateService
{
    private readonly IMessageBus _messageBus;
    private readonly IStateStore<RealmClockModel> _clockStore;
    private readonly IStateStore<CalendarTemplateModel> _calendarStore;
    private readonly IStateStore<TimeRatioHistoryModel> _ratioHistoryStore;
    private readonly IStateStore<RealmWorldstateConfigModel> _realmConfigStore;
    private readonly IQueryableStateStore<CalendarTemplateModel> _queryableCalendarStore;
    private readonly IQueryableStateStore<RealmWorldstateConfigModel> _queryableRealmConfigStore;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly IResourceClient _resourceClient;
    private readonly IRealmClient _realmClient;
    private readonly IGameServiceClient _gameServiceClient;
    private readonly IEntitySessionRegistry _entitySessionRegistry;
    private readonly ILogger<WorldstateService> _logger;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly WorldstateServiceConfiguration _configuration;
    private readonly IWorldstateTimeCalculator _timeCalculator;
    private readonly ICalendarTemplateCache _calendarTemplateCache;
    private readonly IRealmClockCache _realmClockCache;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorldstateService"/> class.
    /// </summary>
    public WorldstateService(
        IMessageBus messageBus,
        IStateStoreFactory stateStoreFactory,
        IDistributedLockProvider lockProvider,
        IResourceClient resourceClient,
        IRealmClient realmClient,
        IGameServiceClient gameServiceClient,
        IEntitySessionRegistry entitySessionRegistry,
        ILogger<WorldstateService> logger,
        ITelemetryProvider telemetryProvider,
        WorldstateServiceConfiguration configuration,
        IEventConsumer eventConsumer,
        IWorldstateTimeCalculator timeCalculator,
        ICalendarTemplateCache calendarTemplateCache,
        IRealmClockCache realmClockCache)
    {
        _messageBus = messageBus;
        _clockStore = stateStoreFactory.GetStore<RealmClockModel>(StateStoreDefinitions.WorldstateRealmClock);
        _calendarStore = stateStoreFactory.GetStore<CalendarTemplateModel>(StateStoreDefinitions.WorldstateCalendar);
        _ratioHistoryStore = stateStoreFactory.GetStore<TimeRatioHistoryModel>(StateStoreDefinitions.WorldstateRatioHistory);
        _realmConfigStore = stateStoreFactory.GetStore<RealmWorldstateConfigModel>(StateStoreDefinitions.WorldstateCalendar);
        _queryableCalendarStore = stateStoreFactory.GetQueryableStore<CalendarTemplateModel>(StateStoreDefinitions.WorldstateCalendar);
        _queryableRealmConfigStore = stateStoreFactory.GetQueryableStore<RealmWorldstateConfigModel>(StateStoreDefinitions.WorldstateCalendar);
        _lockProvider = lockProvider;
        _resourceClient = resourceClient;
        _realmClient = realmClient;
        _gameServiceClient = gameServiceClient;
        _entitySessionRegistry = entitySessionRegistry;
        _logger = logger;
        _telemetryProvider = telemetryProvider;
        _configuration = configuration;
        _timeCalculator = timeCalculator;
        _calendarTemplateCache = calendarTemplateCache;
        _realmClockCache = realmClockCache;

        RegisterEventConsumers(eventConsumer);
    }

    /// <summary>
    /// Gets the current game time snapshot for a realm.
    /// Retrieves the cached clock state from Redis.
    /// </summary>
    /// <param name="body">The request containing the realm ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The current game time snapshot, or NotFound if no clock is initialized.</returns>
    public async Task<(StatusCodes, GameTimeSnapshot?)> GetRealmTimeAsync(GetRealmTimeRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting realm time for realm {RealmId}", body.RealmId);

        var clockKey = $"realm:{body.RealmId}";
        var clock = await _clockStore.GetAsync(clockKey, cancellationToken);
        if (clock == null)
        {
            _logger.LogDebug("No clock initialized for realm {RealmId}", body.RealmId);
            return (StatusCodes.NotFound, null);
        }

        return (StatusCodes.OK, WorldstateBoundaryEventPublisher.MapClockToSnapshot(clock));
    }

    /// <summary>
    /// Gets the current game time snapshot for a realm resolved by its code.
    /// Resolves the realm code to a realm ID via the Realm service, then delegates
    /// to the standard realm time retrieval logic.
    /// </summary>
    /// <param name="body">The request containing the realm code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The current game time snapshot, or NotFound if the realm or clock does not exist.</returns>
    public async Task<(StatusCodes, GameTimeSnapshot?)> GetRealmTimeByCodeAsync(GetRealmTimeByCodeRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting realm time by code {RealmCode}", body.RealmCode);

        // Resolve realm by code via Realm service (L2 hard dependency)
        RealmResponse realm;
        try
        {
            realm = await _realmClient.GetRealmByCodeAsync(
                new GetRealmByCodeRequest { Code = body.RealmCode }, cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Realm service call failed for realm code {RealmCode}", body.RealmCode);
            return ((StatusCodes)ex.StatusCode, null);
        }

        // Delegate to GetRealmTimeAsync logic
        return await GetRealmTimeAsync(new GetRealmTimeRequest { RealmId = realm.RealmId }, cancellationToken);
    }

    /// <summary>
    /// Gets the current game time snapshots for multiple realms in a single request.
    /// Validates the batch size against configuration limits and returns snapshots
    /// for all realms that have initialized clocks.
    /// </summary>
    /// <param name="body">The request containing the list of realm IDs.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of game time snapshots for all found realms.</returns>
    public async Task<(StatusCodes, BatchGetRealmTimesResponse?)> BatchGetRealmTimesAsync(BatchGetRealmTimesRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Batch getting realm times for {Count} realm(s)", body.RealmIds.Count);

        // Validate batch size
        if (body.RealmIds.Count > _configuration.MaxBatchRealmTimeQueries)
        {
            _logger.LogWarning("Batch realm time request exceeds maximum of {Max}, requested {Count}",
                _configuration.MaxBatchRealmTimeQueries, body.RealmIds.Count);
            return (StatusCodes.BadRequest, null);
        }

        var snapshots = new List<GameTimeSnapshot>();
        var notFoundRealmIds = new List<Guid>();

        foreach (var realmId in body.RealmIds)
        {
            var clockKey = $"realm:{realmId}";
            var clock = await _clockStore.GetAsync(clockKey, cancellationToken);
            if (clock != null)
            {
                snapshots.Add(WorldstateBoundaryEventPublisher.MapClockToSnapshot(clock));
            }
            else
            {
                _logger.LogDebug("No clock initialized for realm {RealmId}, skipping in batch results", realmId);
                notFoundRealmIds.Add(realmId);
            }
        }

        return (StatusCodes.OK, new BatchGetRealmTimesResponse
        {
            Snapshots = snapshots,
            NotFoundRealmIds = notFoundRealmIds
        });
    }

    /// <summary>
    /// Computes the elapsed game time between two real-world timestamps for a realm.
    /// Uses piecewise integration across time ratio segments to accurately account
    /// for ratio changes over time. Returns both raw game-seconds and decomposed
    /// calendar units (days, hours, minutes).
    /// </summary>
    /// <param name="body">The request containing realm ID and real-time range.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The elapsed game time in raw seconds and decomposed calendar units.</returns>
    public async Task<(StatusCodes, GetElapsedGameTimeResponse?)> GetElapsedGameTimeAsync(GetElapsedGameTimeRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Computing elapsed game time for realm {RealmId} from {From} to {To}",
            body.RealmId, body.FromRealTime, body.ToRealTime);

        // Validate time range ordering
        if (body.FromRealTime >= body.ToRealTime)
        {
            _logger.LogWarning("Invalid time range for elapsed game time: fromRealTime {From} must be before toRealTime {To}",
                body.FromRealTime, body.ToRealTime);
            return (StatusCodes.BadRequest, null);
        }

        // Load ratio history for piecewise integration
        var ratioKey = $"ratio:{body.RealmId}";
        var ratioHistory = await _ratioHistoryStore.GetAsync(ratioKey, cancellationToken);
        if (ratioHistory == null)
        {
            _logger.LogDebug("No ratio history found for realm {RealmId} (no clock initialized)", body.RealmId);
            return (StatusCodes.NotFound, null);
        }

        // Compute total elapsed game-seconds via piecewise integration
        var totalGameSeconds = _timeCalculator.ComputeElapsedGameTime(
            ratioHistory.Segments, body.FromRealTime, body.ToRealTime);

        // Load realm config to get the calendar template for decomposition
        var configKey = $"realm-config:{body.RealmId}";
        var realmConfig = await _realmConfigStore.GetAsync(configKey, cancellationToken);
        if (realmConfig == null)
        {
            _logger.LogWarning("Ratio history exists but realm config is missing for realm {RealmId} (possible data corruption)",
                body.RealmId);
            return (StatusCodes.NotFound, null);
        }

        // Load calendar template for decomposition
        var calendarKey = $"calendar:{realmConfig.GameServiceId}:{realmConfig.CalendarTemplateCode}";
        var calendar = await _calendarStore.GetAsync(calendarKey, cancellationToken);
        if (calendar == null)
        {
            _logger.LogWarning("Calendar template {TemplateCode} not found for game service {GameServiceId} during elapsed time computation",
                realmConfig.CalendarTemplateCode, realmConfig.GameServiceId);
            return (StatusCodes.NotFound, null);
        }

        // Decompose into calendar units
        var (days, hours, minutes) = _timeCalculator.DecomposeGameSeconds(calendar, totalGameSeconds);

        return (StatusCodes.OK, new GetElapsedGameTimeResponse
        {
            TotalGameSeconds = totalGameSeconds,
            GameDays = days,
            GameHours = hours,
            GameMinutes = minutes
        });
    }

    /// <summary>
    /// Triggers an on-demand time synchronization for all sessions observing a specific entity
    /// within a realm. Builds a full game time snapshot and publishes it to the entity's
    /// connected sessions via the entity session registry.
    /// </summary>
    /// <param name="body">The request containing the realm ID and entity ID to sync.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of sessions notified, or NotFound if no clock is initialized.</returns>
    public async Task<(StatusCodes, TriggerTimeSyncResponse?)> TriggerTimeSyncAsync(TriggerTimeSyncRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Triggering time sync for realm {RealmId}, entity {EntityId}",
            body.RealmId, body.EntityId);

        // Load current clock state
        var clockKey = $"realm:{body.RealmId}";
        var clock = await _clockStore.GetAsync(clockKey, cancellationToken);
        if (clock == null)
        {
            _logger.LogDebug("No clock initialized for realm {RealmId}", body.RealmId);
            return (StatusCodes.NotFound, null);
        }

        // Build time sync client event with full game time snapshot
        var syncEvent = new WorldstateTimeSyncEvent
        {
            RealmId = body.RealmId,
            Year = clock.Year,
            MonthIndex = clock.MonthIndex,
            MonthCode = clock.MonthCode,
            DayOfMonth = clock.DayOfMonth,
            DayOfYear = clock.DayOfYear,
            Hour = clock.Hour,
            Minute = clock.Minute,
            Period = clock.Period,
            IsDaylight = clock.IsDaylight,
            Season = clock.Season,
            SeasonIndex = clock.SeasonIndex,
            SeasonProgress = clock.SeasonProgress,
            TotalGameSecondsSinceEpoch = clock.TotalGameSecondsSinceEpoch,
            TimeRatio = clock.TimeRatio,
            PreviousPeriod = null,
            SyncReason = TimeSyncReason.TriggerSync
        };

        // Publish to all sessions observing this entity within the realm
        var sessionsNotified = await _entitySessionRegistry.PublishToEntitySessionsAsync(
            "realm", body.EntityId, syncEvent, cancellationToken);

        _logger.LogInformation("Time sync triggered for realm {RealmId}, entity {EntityId}: {SessionsNotified} session(s) notified",
            body.RealmId, body.EntityId, sessionsNotified);

        return (StatusCodes.OK, new TriggerTimeSyncResponse
        {
            SessionsNotified = sessionsNotified
        });
    }

    /// <summary>
    /// Initializes a realm clock for the first time.
    /// Validates the realm exists, resolves the calendar template, creates the realm config,
    /// clock model, and ratio history, registers realm references, and publishes events.
    /// </summary>
    /// <param name="body">The initialization request with realm ID and optional overrides.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The initialization response, or an error status.</returns>
    public async Task<(StatusCodes, InitializeRealmClockResponse?)> InitializeRealmClockAsync(InitializeRealmClockRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Initializing realm clock for realm {RealmId}", body.RealmId);

        // Validate realm exists (L2 hard dependency)
        RealmResponse realm;
        try
        {
            realm = await _realmClient.GetRealmAsync(
                new GetRealmRequest { RealmId = body.RealmId }, cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Realm service call failed for realm {RealmId}", body.RealmId);
            return ((StatusCodes)ex.StatusCode, null);
        }

        // Resolve calendar template code: request -> config default -> BadRequest
        var templateCode = body.CalendarTemplateCode ?? _configuration.DefaultCalendarTemplateCode;
        if (string.IsNullOrEmpty(templateCode))
        {
            _logger.LogWarning("No calendar template code specified and no default configured for realm {RealmId}", body.RealmId);
            return (StatusCodes.BadRequest, null);
        }

        // Validate calendar template exists
        var calendarKey = $"calendar:{realm.GameServiceId}:{templateCode}";
        var calendar = await _calendarStore.GetAsync(calendarKey, cancellationToken);
        if (calendar == null)
        {
            _logger.LogWarning("Calendar template {TemplateCode} not found for game service {GameServiceId}",
                templateCode, realm.GameServiceId);
            return (StatusCodes.NotFound, null);
        }

        // Acquire distributed lock to prevent concurrent initialization race condition
        var lockOwner = $"worldstate-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.WorldstateLock,
            $"clock:{body.RealmId}",
            lockOwner,
            _configuration.DistributedLockTimeoutSeconds,
            cancellationToken);
        if (!lockResponse.Success)
        {
            _logger.LogWarning("Failed to acquire lock for realm clock initialization {RealmId}", body.RealmId);
            return (StatusCodes.Conflict, null);
        }

        // Check no existing clock (prevent re-initialization)
        var clockKey = $"realm:{body.RealmId}";
        var existingClock = await _clockStore.GetAsync(clockKey, cancellationToken);
        if (existingClock != null)
        {
            _logger.LogWarning("Clock already initialized for realm {RealmId}", body.RealmId);
            return (StatusCodes.Conflict, null);
        }

        // Resolve initial values with fallbacks
        var initialRatio = body.TimeRatio ?? _configuration.DefaultTimeRatio;
        var epoch = body.Epoch ?? DateTimeOffset.UtcNow;
        var startingYear = body.StartingYear ?? 1;
        var downtimePolicy = body.DowntimePolicy ?? _configuration.DefaultDowntimePolicy;

        // Resolve initial period and season from calendar via time calculator
        var initialPeriod = _timeCalculator.ResolveDayPeriod(calendar, 0);
        var initialSeason = _timeCalculator.ResolveSeason(calendar, 0);

        // Compute initial day of year (day 1 of month 0 = day 1 of year)
        var initialDayOfYear = 1;

        // Create durable realm config (MySQL)
        var realmConfig = new RealmWorldstateConfigModel
        {
            RealmId = body.RealmId,
            GameServiceId = realm.GameServiceId,
            CalendarTemplateCode = templateCode,
            TimeRatio = initialRatio,
            DowntimePolicy = downtimePolicy,
            RealmEpoch = epoch
        };
        var configKey = $"realm-config:{body.RealmId}";
        await _realmConfigStore.SaveAsync(configKey, realmConfig, cancellationToken: cancellationToken);

        // Create initial clock model (Redis)
        var clockModel = new RealmClockModel
        {
            RealmId = body.RealmId,
            GameServiceId = realm.GameServiceId,
            CalendarTemplateCode = templateCode,
            Year = startingYear,
            MonthIndex = 0,
            MonthCode = calendar.Months[0].Code,
            DayOfMonth = 1,
            DayOfYear = initialDayOfYear,
            Hour = 0,
            Minute = 0,
            Period = initialPeriod.code,
            IsDaylight = initialPeriod.isDaylight,
            Season = initialSeason.code,
            SeasonIndex = initialSeason.index,
            SeasonProgress = 0.0f,
            TotalGameSecondsSinceEpoch = 0,
            TimeRatio = initialRatio,
            LastAdvancedRealTime = DateTimeOffset.UtcNow,
            RealmEpoch = epoch,
            DowntimePolicy = downtimePolicy,
            IsActive = initialRatio > 0.0f
        };
        await _clockStore.SaveAsync(clockKey, clockModel, cancellationToken: cancellationToken);

        // Create initial ratio history (MySQL)
        var ratioHistory = new TimeRatioHistoryModel
        {
            RealmId = body.RealmId,
            Segments = new List<TimeRatioSegmentEntry>
            {
                new TimeRatioSegmentEntry
                {
                    SegmentStartRealTime = epoch,
                    TimeRatio = initialRatio,
                    Reason = TimeRatioChangeReason.Initial
                }
            }
        };
        var ratioKey = $"ratio:{body.RealmId}";
        await _ratioHistoryStore.SaveAsync(ratioKey, ratioHistory, cancellationToken: cancellationToken);

        // Register realm reference for cleanup tracking
        await RegisterRealmReferenceAsync($"clock:{body.RealmId}", body.RealmId, cancellationToken);

        // Publish initialization event
        await _messageBus.TryPublishAsync("worldstate.realm-clock.initialized", new WorldstateRealmClockInitializedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RealmId = body.RealmId,
            CalendarTemplateCode = templateCode,
            InitialTimeRatio = initialRatio
        }, cancellationToken: cancellationToken);

        // Publish realm config lifecycle event
        await _messageBus.TryPublishAsync("worldstate.realm-config.created", new RealmConfigCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RealmId = body.RealmId,
            GameServiceId = realm.GameServiceId,
            CalendarTemplateCode = templateCode,
            CurrentTimeRatio = initialRatio,
            DowntimePolicy = downtimePolicy,
            RealmEpoch = epoch
        }, cancellationToken: cancellationToken);

        _logger.LogInformation("Realm clock initialized for realm {RealmId} with template {TemplateCode}, ratio {TimeRatio}",
            body.RealmId, templateCode, initialRatio);

        return (StatusCodes.Created, new InitializeRealmClockResponse
        {
            GameServiceId = realm.GameServiceId,
            CalendarTemplateCode = templateCode,
            TimeRatio = initialRatio,
            DowntimePolicy = downtimePolicy,
            Epoch = epoch,
            StartingYear = startingYear
        });
    }

    /// <summary>
    /// Changes the game-time-to-real-time ratio for a realm. Materializes the current
    /// clock state by computing and advancing elapsed game-seconds since the last
    /// advancement, publishes any boundary events crossed during materialization,
    /// appends the new ratio segment to the ratio history, and publishes ratio changed
    /// and time sync events.
    /// </summary>
    /// <param name="body">The request containing realm ID, new ratio, and reason.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The ratio change response with previous and new ratios.</returns>
    public async Task<(StatusCodes, SetTimeRatioResponse?)> SetTimeRatioAsync(SetTimeRatioRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Setting time ratio for realm {RealmId} to {NewRatio} (reason: {Reason})",
            body.RealmId, body.NewRatio, body.Reason);

        // Validate non-negative ratio (negative ratio would mean time running backwards)
        if (body.NewRatio < 0.0f)
        {
            _logger.LogWarning("Negative time ratio {NewRatio} rejected for realm {RealmId}", body.NewRatio, body.RealmId);
            return (StatusCodes.BadRequest, null);
        }

        // Acquire distributed lock — must use the same lock key as AdvanceClockAsync and
        // the background worker (clock:{realmId}) because this method reads, mutates, and
        // writes the same RealmClockModel in Redis. Using a different key (ratio:{realmId})
        // would allow concurrent execution with clock operations, causing lost updates.
        var lockOwner = $"worldstate-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.WorldstateLock,
            $"clock:{body.RealmId}",
            lockOwner,
            _configuration.DistributedLockTimeoutSeconds,
            cancellationToken);
        if (!lockResponse.Success)
        {
            _logger.LogWarning("Failed to acquire lock for ratio change on realm {RealmId}", body.RealmId);
            return (StatusCodes.Conflict, null);
        }

        // Load clock
        var clockKey = $"realm:{body.RealmId}";
        var clock = await _clockStore.GetAsync(clockKey, cancellationToken);
        if (clock == null)
        {
            _logger.LogDebug("No clock initialized for realm {RealmId}", body.RealmId);
            return (StatusCodes.NotFound, null);
        }

        var previousRatio = clock.TimeRatio;

        // Load calendar template for materialization
        var calendarKey = $"calendar:{clock.GameServiceId}:{clock.CalendarTemplateCode}";
        var calendar = await _calendarStore.GetAsync(calendarKey, cancellationToken);
        if (calendar == null)
        {
            _logger.LogWarning("Calendar template {TemplateCode} not found for realm {RealmId} during ratio change",
                clock.CalendarTemplateCode, body.RealmId);
            return (StatusCodes.NotFound, null);
        }

        // Materialize current state: compute elapsed game-seconds since last advancement
        var now = DateTimeOffset.UtcNow;
        var realElapsed = now - clock.LastAdvancedRealTime;
        var gameSecondsElapsed = (long)(realElapsed.TotalSeconds * clock.TimeRatio);

        // Advance the clock to materialize current state
        List<BoundaryCrossing> boundaries = new();
        if (gameSecondsElapsed > 0)
        {
            boundaries = _timeCalculator.AdvanceGameTime(clock, calendar, gameSecondsElapsed);
        }

        // Publish boundary events from materialization
        await PublishBoundaryEventsAsync(body.RealmId, clock, boundaries, false, cancellationToken);

        // Update clock with new ratio
        clock.TimeRatio = body.NewRatio;
        clock.IsActive = body.NewRatio > 0.0f;
        clock.LastAdvancedRealTime = now;
        await _clockStore.SaveAsync(clockKey, clock, cancellationToken: cancellationToken);

        // Invalidate clock cache so variable provider reads fresh data
        _realmClockCache.Invalidate(body.RealmId);

        // Update realm config
        var configKey = $"realm-config:{body.RealmId}";
        var realmConfig = await _realmConfigStore.GetAsync(configKey, cancellationToken);
        if (realmConfig != null)
        {
            realmConfig.TimeRatio = body.NewRatio;
            await _realmConfigStore.SaveAsync(configKey, realmConfig, cancellationToken: cancellationToken);
        }

        // Append new ratio segment to history
        var ratioKey = $"ratio:{body.RealmId}";
        var ratioHistory = await _ratioHistoryStore.GetAsync(ratioKey, cancellationToken);
        if (ratioHistory != null)
        {
            ratioHistory.Segments.Add(new TimeRatioSegmentEntry
            {
                SegmentStartRealTime = now,
                TimeRatio = body.NewRatio,
                Reason = body.Reason
            });
            await _ratioHistoryStore.SaveAsync(ratioKey, ratioHistory, cancellationToken: cancellationToken);
        }

        // Publish ratio changed event
        await _messageBus.TryPublishAsync("worldstate.ratio-changed", new WorldstateRatioChangedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            RealmId = body.RealmId,
            PreviousRatio = previousRatio,
            NewRatio = body.NewRatio,
            Reason = body.Reason
        }, cancellationToken: cancellationToken);

        // Publish realm config updated lifecycle event for the TimeRatio change
        if (realmConfig != null)
        {
            await _messageBus.TryPublishAsync("worldstate.realm-config.updated", new RealmConfigUpdatedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                RealmId = body.RealmId,
                GameServiceId = realmConfig.GameServiceId,
                CalendarTemplateCode = realmConfig.CalendarTemplateCode,
                CurrentTimeRatio = realmConfig.TimeRatio,
                DowntimePolicy = realmConfig.DowntimePolicy,
                RealmEpoch = realmConfig.RealmEpoch,
                ChangedFields = new List<string> { "currentTimeRatio" }
            }, cancellationToken: cancellationToken);
        }

        // Publish time sync client event
        var syncEvent = new WorldstateTimeSyncEvent
        {
            RealmId = body.RealmId,
            Year = clock.Year,
            MonthIndex = clock.MonthIndex,
            MonthCode = clock.MonthCode,
            DayOfMonth = clock.DayOfMonth,
            DayOfYear = clock.DayOfYear,
            Hour = clock.Hour,
            Minute = clock.Minute,
            Period = clock.Period,
            IsDaylight = clock.IsDaylight,
            Season = clock.Season,
            SeasonIndex = clock.SeasonIndex,
            SeasonProgress = clock.SeasonProgress,
            TotalGameSecondsSinceEpoch = clock.TotalGameSecondsSinceEpoch,
            TimeRatio = clock.TimeRatio,
            PreviousPeriod = null,
            SyncReason = TimeSyncReason.RatioChanged
        };
        await _entitySessionRegistry.PublishToEntitySessionsAsync("realm", body.RealmId, syncEvent, cancellationToken);

        _logger.LogInformation("Time ratio changed for realm {RealmId}: {PreviousRatio} -> {NewRatio} (reason: {Reason})",
            body.RealmId, previousRatio, body.NewRatio, body.Reason);

        return (StatusCodes.OK, new SetTimeRatioResponse
        {
            PreviousRatio = previousRatio
        });
    }

    /// <summary>
    /// Manually advances a realm's game clock by a specified amount of game time.
    /// Acquires a distributed lock, loads the clock and calendar template, advances
    /// the clock by the requested game-seconds (converting from days/months/years if
    /// specified), publishes boundary events, updates the clock store, and publishes
    /// a time sync client event.
    /// </summary>
    /// <param name="body">The request specifying realm ID and time amount to advance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The previous and new time snapshots plus boundary event count, or an error status.</returns>
    public async Task<(StatusCodes, AdvanceClockResponse?)> AdvanceClockAsync(AdvanceClockRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Advancing clock for realm {RealmId}", body.RealmId);

        // Acquire distributed lock
        var lockOwner = $"worldstate-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.WorldstateLock,
            $"clock:{body.RealmId}",
            lockOwner,
            _configuration.DistributedLockTimeoutSeconds,
            cancellationToken);
        if (!lockResponse.Success)
        {
            _logger.LogWarning("Failed to acquire lock for clock advancement on realm {RealmId}", body.RealmId);
            return (StatusCodes.Conflict, null);
        }

        // Load clock
        var clockKey = $"realm:{body.RealmId}";
        var clock = await _clockStore.GetAsync(clockKey, cancellationToken);
        if (clock == null)
        {
            _logger.LogDebug("No clock initialized for realm {RealmId}", body.RealmId);
            return (StatusCodes.NotFound, null);
        }

        // Capture previous snapshot before advancement
        var previousSnapshot = WorldstateBoundaryEventPublisher.MapClockToSnapshot(clock);

        // Load realm config to get calendar template code
        var configKey = $"realm-config:{body.RealmId}";
        var realmConfig = await _realmConfigStore.GetAsync(configKey, cancellationToken);
        if (realmConfig == null)
        {
            _logger.LogWarning("Clock exists but realm config is missing for realm {RealmId} (possible data corruption)",
                body.RealmId);
            return (StatusCodes.NotFound, null);
        }

        // Load calendar template
        var calendarKey = $"calendar:{realmConfig.GameServiceId}:{realmConfig.CalendarTemplateCode}";
        var calendar = await _calendarStore.GetAsync(calendarKey, cancellationToken);
        if (calendar == null)
        {
            _logger.LogWarning("Calendar template {TemplateCode} not found for realm {RealmId} during clock advancement",
                realmConfig.CalendarTemplateCode, body.RealmId);
            return (StatusCodes.NotFound, null);
        }

        // Validate that at least one time parameter was provided
        var hasAnyTimeParam = body.GameSeconds.HasValue
            || body.GameYears.HasValue
            || body.GameMonths.HasValue
            || body.GameDays.HasValue;

        if (!hasAnyTimeParam)
        {
            _logger.LogWarning("Clock advancement for realm {RealmId} has no time parameters (gameSeconds, gameDays, gameMonths, gameYears all absent)",
                body.RealmId);
            return (StatusCodes.BadRequest, null);
        }

        // Convert days/months/years to game-seconds, then add any explicit gameSeconds
        var totalGameSeconds = body.GameSeconds ?? 0L;

        if (body.GameYears.HasValue && body.GameYears.Value > 0)
        {
            var secondsPerDay = (long)calendar.GameHoursPerDay * 3600L;
            totalGameSeconds += (long)body.GameYears.Value * calendar.DaysPerYear * secondsPerDay;
        }

        if (body.GameMonths.HasValue && body.GameMonths.Value > 0)
        {
            var secondsPerDay = (long)calendar.GameHoursPerDay * 3600L;
            // Use average days per month since months can have different lengths
            var avgDaysPerMonth = (double)calendar.DaysPerYear / calendar.MonthsPerYear;
            totalGameSeconds += (long)(body.GameMonths.Value * avgDaysPerMonth * secondsPerDay);
        }

        if (body.GameDays.HasValue && body.GameDays.Value > 0)
        {
            var secondsPerDay = (long)calendar.GameHoursPerDay * 3600L;
            totalGameSeconds += (long)body.GameDays.Value * secondsPerDay;
        }

        if (totalGameSeconds <= 0)
        {
            _logger.LogWarning("Clock advancement for realm {RealmId} resulted in zero or negative total game-seconds from provided parameters",
                body.RealmId);
            return (StatusCodes.BadRequest, null);
        }

        // Advance the clock
        var boundaries = _timeCalculator.AdvanceGameTime(clock, calendar, totalGameSeconds);

        // Respect batch size limit to prevent event storms (consistent with worker behavior)
        var boundariesToPublish = boundaries;
        if (boundariesToPublish.Count > _configuration.BoundaryEventBatchSize)
        {
            boundariesToPublish = boundariesToPublish
                .Take(_configuration.BoundaryEventBatchSize)
                .ToList();
        }

        // Publish boundary events
        await PublishBoundaryEventsAsync(body.RealmId, clock, boundariesToPublish, false, cancellationToken);

        // Update clock store with new state
        clock.LastAdvancedRealTime = DateTimeOffset.UtcNow;
        await _clockStore.SaveAsync(clockKey, clock, cancellationToken: cancellationToken);

        // Invalidate clock cache so variable provider reads fresh data
        _realmClockCache.Invalidate(body.RealmId);

        // Publish clock-advanced service event for cross-node cache invalidation
        await _messageBus.PublishAsync("worldstate.clock-advanced", new WorldstateClockAdvancedEvent
        {
            RealmId = body.RealmId
        }, cancellationToken);

        // Publish time sync client event (populate PreviousPeriod from boundaries if available,
        // consistent with worker behavior)
        var syncEvent = new WorldstateTimeSyncEvent
        {
            RealmId = body.RealmId,
            Year = clock.Year,
            MonthIndex = clock.MonthIndex,
            MonthCode = clock.MonthCode,
            DayOfMonth = clock.DayOfMonth,
            DayOfYear = clock.DayOfYear,
            Hour = clock.Hour,
            Minute = clock.Minute,
            Period = clock.Period,
            IsDaylight = clock.IsDaylight,
            Season = clock.Season,
            SeasonIndex = clock.SeasonIndex,
            SeasonProgress = clock.SeasonProgress,
            TotalGameSecondsSinceEpoch = clock.TotalGameSecondsSinceEpoch,
            TimeRatio = clock.TimeRatio,
            PreviousPeriod = boundariesToPublish
                .Where(b => b.Type == BoundaryType.Period)
                .Select(b => b.PreviousStringValue)
                .FirstOrDefault(),
            SyncReason = TimeSyncReason.AdminAdvance
        };
        await _entitySessionRegistry.PublishToEntitySessionsAsync("realm", body.RealmId, syncEvent, cancellationToken);

        var newSnapshot = WorldstateBoundaryEventPublisher.MapClockToSnapshot(clock);

        _logger.LogInformation("Advanced clock for realm {RealmId} by {GameSeconds} game-seconds, {BoundaryCount} boundary events published (of {TotalBoundaries} total)",
            body.RealmId, totalGameSeconds, boundariesToPublish.Count, boundaries.Count);

        return (StatusCodes.OK, new AdvanceClockResponse
        {
            PreviousTime = previousSnapshot,
            NewTime = newSnapshot,
            BoundaryEventsPublished = boundariesToPublish.Count
        });
    }

    /// <summary>
    /// Creates a new calendar template for a game service.
    /// Validates the game service exists, enforces uniqueness per game service,
    /// validates structural consistency (day periods, months, seasons), and
    /// enforces the per-game-service calendar limit.
    /// </summary>
    /// <param name="body">The seed calendar request containing template definition.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created calendar template response, or an error status.</returns>
    public async Task<(StatusCodes, CalendarTemplateResponse?)> SeedCalendarAsync(SeedCalendarRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Seeding calendar template {TemplateCode} for game service {GameServiceId}",
            body.TemplateCode, body.GameServiceId);

        // Validate game service exists (L2 hard dependency)
        try
        {
            await _gameServiceClient.GetServiceAsync(
                new GetServiceRequest { ServiceId = body.GameServiceId }, cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Game service call failed for {GameServiceId}", body.GameServiceId);
            return ((StatusCodes)ex.StatusCode, null);
        }

        var calendarKey = $"calendar:{body.GameServiceId}:{body.TemplateCode}";

        // Check uniqueness: template code must be unique per game service
        var existing = await _calendarStore.GetAsync(calendarKey, cancellationToken);
        if (existing != null)
        {
            _logger.LogWarning("Calendar template {TemplateCode} already exists for game service {GameServiceId}",
                body.TemplateCode, body.GameServiceId);
            return (StatusCodes.Conflict, null);
        }

        // Enforce max calendars per game service
        var existingCalendars = await _queryableCalendarStore.QueryAsync(
            x => x.GameServiceId == body.GameServiceId, cancellationToken);
        if (existingCalendars.Count >= _configuration.MaxCalendarsPerGameService)
        {
            _logger.LogWarning("Game service {GameServiceId} has reached the maximum calendar limit of {Max}",
                body.GameServiceId, _configuration.MaxCalendarsPerGameService);
            return (StatusCodes.BadRequest, null);
        }

        // Validate structural consistency
        var validationError = ValidateCalendarStructure(
            body.GameHoursPerDay,
            body.DayPeriods,
            body.Months,
            body.Seasons);
        if (validationError != null)
        {
            _logger.LogWarning("Calendar template validation failed: {Error}", validationError);
            return (StatusCodes.BadRequest, null);
        }

        // Compute derived fields
        var daysPerYear = body.Months.Sum(m => m.DaysInMonth);
        var monthsPerYear = body.Months.Count;
        var seasonsPerYear = body.Seasons.Count;

        // Build storage model
        var model = new CalendarTemplateModel
        {
            TemplateCode = body.TemplateCode,
            GameServiceId = body.GameServiceId,
            GameHoursPerDay = body.GameHoursPerDay,
            DayPeriods = body.DayPeriods.Select(dp => new DayPeriodDefinition
            {
                Code = dp.Code,
                StartHour = dp.StartHour,
                EndHour = dp.EndHour,
                IsDaylight = dp.IsDaylight
            }).ToList(),
            Months = body.Months.Select(m => new MonthDefinition
            {
                Code = m.Code,
                Name = m.Name,
                DaysInMonth = m.DaysInMonth,
                SeasonCode = m.SeasonCode
            }).ToList(),
            Seasons = body.Seasons.Select(s => new SeasonDefinition
            {
                Code = s.Code,
                Name = s.Name,
                Ordinal = s.Ordinal
            }).ToList(),
            DaysPerYear = daysPerYear,
            MonthsPerYear = monthsPerYear,
            SeasonsPerYear = seasonsPerYear,
            EraLabels = body.EraLabels?.Select(e => new EraLabel
            {
                Code = e.Code,
                StartYear = e.StartYear,
                EndYear = e.EndYear
            }).ToList()
        };

        // Save to store
        await _calendarStore.SaveAsync(calendarKey, model, cancellationToken: cancellationToken);

        // Register game-service reference for cleanup tracking
        await RegisterGameServiceReferenceAsync(calendarKey, body.GameServiceId, cancellationToken);

        // Publish lifecycle event
        await _messageBus.TryPublishAsync("worldstate.calendar-template.created", new CalendarTemplateCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            TemplateCode = model.TemplateCode,
            GameServiceId = model.GameServiceId,
            GameHoursPerDay = model.GameHoursPerDay,
            DayPeriods = model.DayPeriods,
            Months = model.Months,
            Seasons = model.Seasons,
            DaysPerYear = model.DaysPerYear,
            MonthsPerYear = model.MonthsPerYear,
            SeasonsPerYear = model.SeasonsPerYear,
            EraLabels = model.EraLabels
        }, cancellationToken: cancellationToken);

        _logger.LogInformation("Created calendar template {TemplateCode} for game service {GameServiceId}",
            model.TemplateCode, model.GameServiceId);

        return (StatusCodes.Created, MapToCalendarResponse(model));
    }

    /// <summary>
    /// Retrieves a calendar template by game service ID and template code.
    /// </summary>
    /// <param name="body">The get calendar request containing game service ID and template code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The calendar template response, or NotFound if it does not exist.</returns>
    public async Task<(StatusCodes, CalendarTemplateResponse?)> GetCalendarAsync(GetCalendarRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting calendar template {TemplateCode} for game service {GameServiceId}",
            body.TemplateCode, body.GameServiceId);

        var calendarKey = $"calendar:{body.GameServiceId}:{body.TemplateCode}";
        var model = await _calendarStore.GetAsync(calendarKey, cancellationToken);
        if (model == null)
        {
            _logger.LogDebug("Calendar template {TemplateCode} not found for game service {GameServiceId}",
                body.TemplateCode, body.GameServiceId);
            return (StatusCodes.NotFound, null);
        }

        return (StatusCodes.OK, MapToCalendarResponse(model));
    }

    /// <summary>
    /// Lists all calendar templates for a game service.
    /// </summary>
    /// <param name="body">The list calendars request containing the game service ID filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of calendar template responses for the specified game service.</returns>
    public async Task<(StatusCodes, ListCalendarsResponse?)> ListCalendarsAsync(ListCalendarsRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Listing calendar templates for game service {GameServiceId}", body.GameServiceId);

        var results = await _queryableCalendarStore.QueryAsync(
            x => x.GameServiceId == body.GameServiceId, cancellationToken);

        var templates = results.Select(MapToCalendarResponse).ToList();

        return (StatusCodes.OK, new ListCalendarsResponse
        {
            Templates = templates
        });
    }

    /// <summary>
    /// Updates an existing calendar template with partial field updates.
    /// Acquires a distributed lock to prevent concurrent modifications.
    /// Re-validates structural consistency after applying updates.
    /// </summary>
    /// <param name="body">The update calendar request containing fields to modify.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated calendar template response, or an error status.</returns>
    public async Task<(StatusCodes, CalendarTemplateResponse?)> UpdateCalendarAsync(UpdateCalendarRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Updating calendar template {TemplateCode} for game service {GameServiceId}",
            body.TemplateCode, body.GameServiceId);

        var calendarKey = $"calendar:{body.GameServiceId}:{body.TemplateCode}";
        var lockOwner = $"worldstate-{Guid.NewGuid():N}";

        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.WorldstateLock,
            calendarKey,
            lockOwner,
            _configuration.DistributedLockTimeoutSeconds,
            cancellationToken);
        if (!lockResponse.Success)
        {
            _logger.LogWarning("Failed to acquire lock for calendar template {TemplateCode}", body.TemplateCode);
            return (StatusCodes.Conflict, null);
        }

        // Load existing template
        var model = await _calendarStore.GetAsync(calendarKey, cancellationToken);
        if (model == null)
        {
            _logger.LogDebug("Calendar template {TemplateCode} not found for game service {GameServiceId}",
                body.TemplateCode, body.GameServiceId);
            return (StatusCodes.NotFound, null);
        }

        // Track changed fields
        var changedFields = new List<string>();

        // Apply partial updates (only non-null fields)
        if (body.GameHoursPerDay.HasValue)
        {
            model.GameHoursPerDay = body.GameHoursPerDay.Value;
            changedFields.Add("gameHoursPerDay");
        }

        if (body.DayPeriods != null)
        {
            model.DayPeriods = body.DayPeriods.Select(dp => new DayPeriodDefinition
            {
                Code = dp.Code,
                StartHour = dp.StartHour,
                EndHour = dp.EndHour,
                IsDaylight = dp.IsDaylight
            }).ToList();
            changedFields.Add("dayPeriods");
        }

        if (body.Months != null)
        {
            model.Months = body.Months.Select(m => new MonthDefinition
            {
                Code = m.Code,
                Name = m.Name,
                DaysInMonth = m.DaysInMonth,
                SeasonCode = m.SeasonCode
            }).ToList();
            changedFields.Add("months");
        }

        if (body.Seasons != null)
        {
            model.Seasons = body.Seasons.Select(s => new SeasonDefinition
            {
                Code = s.Code,
                Name = s.Name,
                Ordinal = s.Ordinal
            }).ToList();
            changedFields.Add("seasons");
        }

        if (body.EraLabels != null)
        {
            model.EraLabels = body.EraLabels.Select(e => new EraLabel
            {
                Code = e.Code,
                StartYear = e.StartYear,
                EndYear = e.EndYear
            }).ToList();
            changedFields.Add("eraLabels");
        }

        // No-op update: if no fields were changed, return current state without saving or publishing
        if (changedFields.Count == 0)
        {
            _logger.LogDebug("No fields changed for calendar template {TemplateCode}, returning current state",
                body.TemplateCode);
            return (StatusCodes.OK, MapToCalendarResponse(model));
        }

        // Re-validate structural consistency after applying updates
        var validationError = ValidateCalendarStructure(
            model.GameHoursPerDay,
            model.DayPeriods,
            model.Months,
            model.Seasons);
        if (validationError != null)
        {
            _logger.LogWarning("Calendar template update validation failed: {Error}", validationError);
            return (StatusCodes.BadRequest, null);
        }

        // Recompute derived fields
        model.DaysPerYear = model.Months.Sum(m => m.DaysInMonth);
        model.MonthsPerYear = model.Months.Count;
        model.SeasonsPerYear = model.Seasons.Count;

        // Save updated model
        await _calendarStore.SaveAsync(calendarKey, model, cancellationToken: cancellationToken);

        // Publish lifecycle event with changedFields
        await _messageBus.TryPublishAsync("worldstate.calendar-template.updated", new CalendarTemplateUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            TemplateCode = model.TemplateCode,
            GameServiceId = model.GameServiceId,
            GameHoursPerDay = model.GameHoursPerDay,
            DayPeriods = model.DayPeriods,
            Months = model.Months,
            Seasons = model.Seasons,
            DaysPerYear = model.DaysPerYear,
            MonthsPerYear = model.MonthsPerYear,
            SeasonsPerYear = model.SeasonsPerYear,
            EraLabels = model.EraLabels,
            ChangedFields = changedFields
        }, cancellationToken: cancellationToken);

        _logger.LogInformation("Updated calendar template {TemplateCode} for game service {GameServiceId}, changed fields: {ChangedFields}",
            model.TemplateCode, model.GameServiceId, string.Join(", ", changedFields));

        return (StatusCodes.OK, MapToCalendarResponse(model));
    }

    /// <summary>
    /// Deletes a calendar template. Requires that no realm configurations reference this template.
    /// Acquires a distributed lock to prevent concurrent modifications.
    /// </summary>
    /// <param name="body">The delete calendar request containing game service ID and template code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An empty delete response on success, or an error status.</returns>
    public async Task<(StatusCodes, DeleteCalendarResponse?)> DeleteCalendarAsync(DeleteCalendarRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Deleting calendar template {TemplateCode} for game service {GameServiceId}",
            body.TemplateCode, body.GameServiceId);

        var calendarKey = $"calendar:{body.GameServiceId}:{body.TemplateCode}";
        var lockOwner = $"worldstate-{Guid.NewGuid():N}";

        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.WorldstateLock,
            calendarKey,
            lockOwner,
            _configuration.DistributedLockTimeoutSeconds,
            cancellationToken);
        if (!lockResponse.Success)
        {
            _logger.LogWarning("Failed to acquire lock for calendar template {TemplateCode}", body.TemplateCode);
            return (StatusCodes.Conflict, null);
        }

        // Load existing template
        var model = await _calendarStore.GetAsync(calendarKey, cancellationToken);
        if (model == null)
        {
            _logger.LogDebug("Calendar template {TemplateCode} not found for game service {GameServiceId}",
                body.TemplateCode, body.GameServiceId);
            return (StatusCodes.NotFound, null);
        }

        // Check that no realm configs reference this template
        var referencingConfigs = await _queryableRealmConfigStore.QueryAsync(
            rc => rc.CalendarTemplateCode == body.TemplateCode && rc.GameServiceId == body.GameServiceId,
            cancellationToken);
        if (referencingConfigs.Count > 0)
        {
            _logger.LogWarning("Cannot delete calendar template {TemplateCode}: referenced by {Count} realm configuration(s)",
                body.TemplateCode, referencingConfigs.Count);
            return (StatusCodes.Conflict, null);
        }

        // Unregister game-service reference
        await UnregisterGameServiceReferenceAsync(calendarKey, body.GameServiceId, cancellationToken);

        // Delete from store
        await _calendarStore.DeleteAsync(calendarKey, cancellationToken);

        // Publish lifecycle event
        await _messageBus.TryPublishAsync("worldstate.calendar-template.deleted", new CalendarTemplateDeletedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            TemplateCode = model.TemplateCode,
            GameServiceId = model.GameServiceId,
            GameHoursPerDay = model.GameHoursPerDay,
            DayPeriods = model.DayPeriods,
            Months = model.Months,
            Seasons = model.Seasons,
            DaysPerYear = model.DaysPerYear,
            MonthsPerYear = model.MonthsPerYear,
            SeasonsPerYear = model.SeasonsPerYear,
            EraLabels = model.EraLabels
        }, cancellationToken: cancellationToken);

        _logger.LogInformation("Deleted calendar template {TemplateCode} for game service {GameServiceId}",
            model.TemplateCode, model.GameServiceId);

        return (StatusCodes.OK, new DeleteCalendarResponse());
    }

    /// <summary>
    /// Gets the durable realm worldstate configuration.
    /// </summary>
    /// <param name="body">The request containing the realm ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The realm config response, or NotFound if no config exists.</returns>
    public async Task<(StatusCodes, RealmConfigResponse?)> GetRealmConfigAsync(GetRealmConfigRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Getting realm config for realm {RealmId}", body.RealmId);

        var configKey = $"realm-config:{body.RealmId}";
        var config = await _realmConfigStore.GetAsync(configKey, cancellationToken);
        if (config == null)
        {
            _logger.LogDebug("Realm config not found for realm {RealmId}", body.RealmId);
            return (StatusCodes.NotFound, null);
        }

        // Enrich with current active state from Redis clock
        var clockKey = $"realm:{body.RealmId}";
        var clock = await _clockStore.GetAsync(clockKey, cancellationToken);

        if (clock == null)
        {
            // Clock and config are always created together by InitializeRealmClockAsync.
            // A missing clock indicates corrupt state — log a warning but still return the config.
            _logger.LogWarning("Realm config exists but clock is missing for realm {RealmId} (possible data corruption)",
                body.RealmId);
        }

        return (StatusCodes.OK, MapToRealmConfigResponse(config, clock?.IsActive ?? false));
    }

    /// <summary>
    /// Updates a realm's worldstate configuration with partial field updates.
    /// Only downtimePolicy and calendarTemplateCode can be updated. Ratio changes
    /// require the SetTimeRatio endpoint; epoch is immutable.
    /// Acquires a distributed lock to prevent concurrent modifications.
    /// </summary>
    /// <param name="body">The update request containing the realm ID and fields to modify.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The updated realm config response, or an error status.</returns>
    public async Task<(StatusCodes, RealmConfigResponse?)> UpdateRealmConfigAsync(UpdateRealmConfigRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Updating realm config for realm {RealmId}", body.RealmId);

        var lockOwner = $"worldstate-{Guid.NewGuid():N}";
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.WorldstateLock,
            $"clock:{body.RealmId}",
            lockOwner,
            _configuration.DistributedLockTimeoutSeconds,
            cancellationToken);
        if (!lockResponse.Success)
        {
            _logger.LogWarning("Failed to acquire lock for realm config {RealmId}", body.RealmId);
            return (StatusCodes.Conflict, null);
        }

        // Load existing config
        var configKey = $"realm-config:{body.RealmId}";
        var config = await _realmConfigStore.GetAsync(configKey, cancellationToken);
        if (config == null)
        {
            _logger.LogDebug("Realm config not found for realm {RealmId}", body.RealmId);
            return (StatusCodes.NotFound, null);
        }

        // Track changed fields
        var changedFields = new List<string>();

        // Apply partial updates
        if (body.DowntimePolicy.HasValue)
        {
            config.DowntimePolicy = body.DowntimePolicy.Value;
            changedFields.Add("downtimePolicy");
        }

        if (!string.IsNullOrEmpty(body.CalendarTemplateCode) && body.CalendarTemplateCode != config.CalendarTemplateCode)
        {
            // Validate new calendar template exists
            var newCalendarKey = $"calendar:{config.GameServiceId}:{body.CalendarTemplateCode}";
            var newCalendar = await _calendarStore.GetAsync(newCalendarKey, cancellationToken);
            if (newCalendar == null)
            {
                _logger.LogWarning("Calendar template {TemplateCode} not found for game service {GameServiceId}",
                    body.CalendarTemplateCode, config.GameServiceId);
                return (StatusCodes.NotFound, null);
            }

            config.CalendarTemplateCode = body.CalendarTemplateCode;
            changedFields.Add("calendarTemplateCode");
        }

        // No-op update: if no fields were changed, return current state
        if (changedFields.Count == 0)
        {
            _logger.LogDebug("No fields changed for realm config {RealmId}, returning current state", body.RealmId);

            var clockForNoOp = await _clockStore.GetAsync($"realm:{body.RealmId}", cancellationToken);
            if (clockForNoOp == null)
            {
                _logger.LogWarning("Realm config exists but clock is missing for realm {RealmId} (possible data corruption)",
                    body.RealmId);
            }

            return (StatusCodes.OK, MapToRealmConfigResponse(config, clockForNoOp?.IsActive ?? false));
        }

        // Save updated config
        await _realmConfigStore.SaveAsync(configKey, config, cancellationToken: cancellationToken);

        // Sync changed fields to the Redis clock model
        var clockKey = $"realm:{body.RealmId}";
        var clock = await _clockStore.GetAsync(clockKey, cancellationToken);
        if (clock != null)
        {
            var clockDirty = false;

            if (changedFields.Contains("calendarTemplateCode"))
            {
                clock.CalendarTemplateCode = config.CalendarTemplateCode;
                clockDirty = true;
            }

            if (changedFields.Contains("downtimePolicy"))
            {
                clock.DowntimePolicy = config.DowntimePolicy;
                clockDirty = true;
            }

            if (clockDirty)
            {
                await _clockStore.SaveAsync(clockKey, clock, cancellationToken: cancellationToken);
            }
        }

        // Publish realm config updated lifecycle event
        await _messageBus.TryPublishAsync("worldstate.realm-config.updated", new RealmConfigUpdatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            RealmId = config.RealmId,
            GameServiceId = config.GameServiceId,
            CalendarTemplateCode = config.CalendarTemplateCode,
            CurrentTimeRatio = config.TimeRatio,
            DowntimePolicy = config.DowntimePolicy,
            RealmEpoch = config.RealmEpoch,
            ChangedFields = changedFields
        }, cancellationToken: cancellationToken);

        _logger.LogInformation("Updated realm config for realm {RealmId}, changed fields: {ChangedFields}",
            body.RealmId, string.Join(", ", changedFields));

        if (clock == null)
        {
            _logger.LogWarning("Realm config exists but clock is missing for realm {RealmId} (possible data corruption)",
                body.RealmId);
        }

        return (StatusCodes.OK, MapToRealmConfigResponse(config, clock?.IsActive ?? false));
    }

    /// <summary>
    /// Lists realm clock summaries with optional game service filtering and pagination.
    /// Queries durable realm configs from MySQL and enriches with Redis clock snapshots.
    /// </summary>
    /// <param name="body">The list request with optional gameServiceId filter and pagination.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A paginated list of realm clock summaries.</returns>
    public async Task<(StatusCodes, ListRealmClocksResponse?)> ListRealmClocksAsync(ListRealmClocksRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Listing realm clocks with gameServiceId filter {GameServiceId}, page {Page}, pageSize {PageSize}",
            body.GameServiceId, body.Page, body.PageSize);

        // Build predicate based on optional gameServiceId filter
        System.Linq.Expressions.Expression<Func<RealmWorldstateConfigModel, bool>>? predicate = null;
        if (body.GameServiceId.HasValue)
        {
            var gameServiceId = body.GameServiceId.Value;
            predicate = rc => rc.GameServiceId == gameServiceId;
        }

        var pagedResult = await _queryableRealmConfigStore.QueryPagedAsync(
            predicate,
            body.Page,
            body.PageSize,
            orderBy: null,
            descending: false,
            cancellationToken);

        // Enrich each config with current clock snapshot from Redis
        var summaries = new List<RealmClockSummary>();
        foreach (var config in pagedResult.Items)
        {
            var clockKey = $"realm:{config.RealmId}";
            var clock = await _clockStore.GetAsync(clockKey, cancellationToken);

            if (clock == null)
            {
                // Clock and config are always created together by InitializeRealmClockAsync.
                // A missing clock indicates corrupt state — skip this entry and log a warning.
                _logger.LogWarning("Realm config exists but clock is missing for realm {RealmId}, skipping from list results (possible data corruption)",
                    config.RealmId);
                continue;
            }

            summaries.Add(new RealmClockSummary
            {
                RealmId = config.RealmId,
                CalendarTemplateCode = config.CalendarTemplateCode,
                CurrentTimeRatio = config.TimeRatio,
                CurrentSeason = clock.Season,
                CurrentYear = clock.Year,
                LastAdvancedAt = clock.LastAdvancedRealTime
            });
        }

        return (StatusCodes.OK, new ListRealmClocksResponse
        {
            Items = summaries,
            TotalCount = (int)Math.Min(pagedResult.TotalCount, int.MaxValue),
            Page = body.Page,
            PageSize = body.PageSize
        });
    }

    /// <summary>
    /// Cleans up all worldstate data for a realm. Deletes the clock, ratio history,
    /// and realm config from their respective stores, unregisters the realm reference,
    /// publishes a realm config deleted event, and invalidates local caches.
    /// Called by lib-resource cleanup callbacks when a realm is deleted.
    /// </summary>
    /// <param name="body">The request containing the realm ID to clean up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An empty response on success.</returns>
    public async Task<(StatusCodes, CleanupByRealmResponse?)> CleanupByRealmAsync(CleanupByRealmRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Cleaning up worldstate data for realm {RealmId}", body.RealmId);

        // Load realm config before deletion for the lifecycle event
        var configKey = $"realm-config:{body.RealmId}";
        var realmConfig = await _realmConfigStore.GetAsync(configKey, cancellationToken);
        if (realmConfig == null)
        {
            _logger.LogWarning("Realm config not found for realm {RealmId} during cleanup (partially initialized or already cleaned up)",
                body.RealmId);
        }

        // Delete clock from Redis
        var clockKey = $"realm:{body.RealmId}";
        await _clockStore.DeleteAsync(clockKey, cancellationToken);

        // Delete ratio history from MySQL
        var ratioKey = $"ratio:{body.RealmId}";
        await _ratioHistoryStore.DeleteAsync(ratioKey, cancellationToken);

        // Delete realm config from MySQL
        await _realmConfigStore.DeleteAsync(configKey, cancellationToken);

        // Unregister realm reference for cleanup tracking
        await UnregisterRealmReferenceAsync($"clock:{body.RealmId}", body.RealmId, cancellationToken);

        // Publish realm config deleted lifecycle event only when config existed.
        // When realmConfig is null (partially initialized or already cleaned up), we skip
        // the event rather than populating it with sentinel values (per IMPLEMENTATION TENETS:
        // no sentinel values for absence).
        if (realmConfig != null)
        {
            await _messageBus.TryPublishAsync("worldstate.realm-config.deleted", new RealmConfigDeletedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                RealmId = body.RealmId,
                GameServiceId = realmConfig.GameServiceId,
                CalendarTemplateCode = realmConfig.CalendarTemplateCode,
                CurrentTimeRatio = realmConfig.TimeRatio,
                DowntimePolicy = realmConfig.DowntimePolicy,
                RealmEpoch = realmConfig.RealmEpoch
            }, cancellationToken: cancellationToken);
        }

        // Invalidate local caches
        _realmClockCache.Invalidate(body.RealmId);
        if (realmConfig != null)
        {
            _calendarTemplateCache.Invalidate(realmConfig.GameServiceId, realmConfig.CalendarTemplateCode);
        }

        _logger.LogInformation("Cleaned up worldstate data for realm {RealmId}", body.RealmId);

        return (StatusCodes.OK, new CleanupByRealmResponse());
    }

    /// <summary>
    /// Cleans up all calendar templates for a game service. Queries all calendars belonging
    /// to the game service, deletes each one, unregisters game-service references, and
    /// publishes deletion events. Called by lib-resource cleanup callbacks when a game
    /// service is deleted.
    /// </summary>
    /// <param name="body">The request containing the game service ID to clean up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The count of templates removed.</returns>
    public async Task<(StatusCodes, CleanupByGameServiceResponse?)> CleanupByGameServiceAsync(CleanupByGameServiceRequest body, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Cleaning up worldstate calendars for game service {GameServiceId}", body.GameServiceId);

        // Query all calendars for this game service
        var calendars = await _queryableCalendarStore.QueryAsync(
            x => x.GameServiceId == body.GameServiceId, cancellationToken);

        // Delete each calendar template
        foreach (var template in calendars)
        {
            var calendarKey = $"calendar:{body.GameServiceId}:{template.TemplateCode}";

            // Delete from store
            await _calendarStore.DeleteAsync(calendarKey, cancellationToken);

            // Unregister game-service reference
            await UnregisterGameServiceReferenceAsync(
                $"calendar:{body.GameServiceId}:{template.TemplateCode}", body.GameServiceId, cancellationToken);

            // Publish lifecycle event for each deleted template
            await _messageBus.TryPublishAsync("worldstate.calendar-template.deleted", new CalendarTemplateDeletedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                TemplateCode = template.TemplateCode,
                GameServiceId = template.GameServiceId,
                GameHoursPerDay = template.GameHoursPerDay,
                DayPeriods = template.DayPeriods,
                Months = template.Months,
                Seasons = template.Seasons,
                DaysPerYear = template.DaysPerYear,
                MonthsPerYear = template.MonthsPerYear,
                SeasonsPerYear = template.SeasonsPerYear,
                EraLabels = template.EraLabels
            }, cancellationToken: cancellationToken);

            // Invalidate calendar cache for this template
            _calendarTemplateCache.Invalidate(template.GameServiceId, template.TemplateCode);
        }

        _logger.LogInformation("Cleaned up {Count} calendar template(s) for game service {GameServiceId}",
            calendars.Count, body.GameServiceId);

        return (StatusCodes.OK, new CleanupByGameServiceResponse
        {
            TemplatesRemoved = calendars.Count
        });
    }

    #region Private Helpers

    /// <summary>
    /// Validates the structural consistency of a calendar template definition.
    /// Checks day period coverage, month season references, and days-per-year consistency.
    /// </summary>
    /// <param name="gameHoursPerDay">Number of game hours in one game day.</param>
    /// <param name="dayPeriods">Day period definitions to validate.</param>
    /// <param name="months">Month definitions to validate.</param>
    /// <param name="seasons">Season definitions to validate.</param>
    /// <returns>An error message if validation fails, or null if valid.</returns>
    private static string? ValidateCalendarStructure(
        int gameHoursPerDay,
        ICollection<DayPeriodDefinition> dayPeriods,
        ICollection<MonthDefinition> months,
        ICollection<SeasonDefinition> seasons)
    {
        if (gameHoursPerDay <= 0)
        {
            return "gameHoursPerDay must be greater than zero";
        }

        if (dayPeriods.Count == 0)
        {
            return "At least one day period is required";
        }

        if (months.Count == 0)
        {
            return "At least one month is required";
        }

        if (seasons.Count == 0)
        {
            return "At least one season is required";
        }

        // Validate unique codes within each collection
        var dayPeriodCodes = new HashSet<string>();
        foreach (var dp in dayPeriods)
        {
            if (!dayPeriodCodes.Add(dp.Code))
            {
                return $"Duplicate day period code '{dp.Code}'";
            }
        }

        var monthCodes = new HashSet<string>();
        foreach (var m in months)
        {
            if (!monthCodes.Add(m.Code))
            {
                return $"Duplicate month code '{m.Code}'";
            }
        }

        var seasonCodeSet = new HashSet<string>();
        foreach (var s in seasons)
        {
            if (!seasonCodeSet.Add(s.Code))
            {
                return $"Duplicate season code '{s.Code}'";
            }
        }

        // Validate day periods cover [0, gameHoursPerDay) without gaps or overlaps
        var sortedPeriods = dayPeriods.OrderBy(dp => dp.StartHour).ToList();

        // Check that first period starts at 0
        if (sortedPeriods[0].StartHour != 0)
        {
            return $"Day periods must start at hour 0, but first period '{sortedPeriods[0].Code}' starts at hour {sortedPeriods[0].StartHour}";
        }

        // Check contiguous coverage and no overlaps
        for (var i = 0; i < sortedPeriods.Count; i++)
        {
            var period = sortedPeriods[i];

            if (period.StartHour < 0 || period.EndHour <= period.StartHour)
            {
                return $"Day period '{period.Code}' has invalid hour range [{period.StartHour}, {period.EndHour})";
            }

            if (i < sortedPeriods.Count - 1)
            {
                var nextPeriod = sortedPeriods[i + 1];
                if (period.EndHour != nextPeriod.StartHour)
                {
                    return $"Gap or overlap between day periods '{period.Code}' (ends {period.EndHour}) and '{nextPeriod.Code}' (starts {nextPeriod.StartHour})";
                }
            }
        }

        // Check that the last period ends at gameHoursPerDay
        var lastPeriod = sortedPeriods[^1];
        if (lastPeriod.EndHour != gameHoursPerDay)
        {
            return $"Day periods must cover up to hour {gameHoursPerDay}, but last period '{lastPeriod.Code}' ends at hour {lastPeriod.EndHour}";
        }

        // Validate that each month's season code references a defined season
        var seasonCodes = new HashSet<string>(seasons.Select(s => s.Code));
        foreach (var month in months)
        {
            if (!seasonCodes.Contains(month.SeasonCode))
            {
                return $"Month '{month.Code}' references undefined season code '{month.SeasonCode}'";
            }

            if (month.DaysInMonth <= 0)
            {
                return $"Month '{month.Code}' must have at least 1 day, but has {month.DaysInMonth}";
            }
        }

        // Validate season ordinals are unique and contiguous (0, 1, 2, ...)
        var sortedOrdinals = seasons.Select(s => s.Ordinal).OrderBy(o => o).ToList();
        for (var i = 0; i < sortedOrdinals.Count; i++)
        {
            if (sortedOrdinals[i] != i)
            {
                return $"Season ordinals must be contiguous starting from 0, but found ordinal {sortedOrdinals[i]} at position {i}";
            }
        }

        return null;
    }

    /// <summary>
    /// Maps an internal calendar template storage model to the API response type.
    /// </summary>
    /// <param name="model">The internal storage model.</param>
    /// <returns>The calendar template response.</returns>
    private static CalendarTemplateResponse MapToCalendarResponse(CalendarTemplateModel model)
    {
        return new CalendarTemplateResponse
        {
            TemplateCode = model.TemplateCode,
            GameServiceId = model.GameServiceId,
            GameHoursPerDay = model.GameHoursPerDay,
            DayPeriods = model.DayPeriods,
            Months = model.Months,
            Seasons = model.Seasons,
            DaysPerYear = model.DaysPerYear,
            MonthsPerYear = model.MonthsPerYear,
            SeasonsPerYear = model.SeasonsPerYear,
            EraLabels = model.EraLabels
        };
    }

    /// <summary>
    /// Maps an internal realm worldstate config model and active state to the API response type.
    /// </summary>
    /// <param name="config">The internal realm config storage model.</param>
    /// <param name="isActive">Whether the realm clock is currently active.</param>
    /// <returns>The realm config response.</returns>
    private static RealmConfigResponse MapToRealmConfigResponse(RealmWorldstateConfigModel config, bool isActive)
    {
        return new RealmConfigResponse
        {
            RealmId = config.RealmId,
            GameServiceId = config.GameServiceId,
            CalendarTemplateCode = config.CalendarTemplateCode,
            CurrentTimeRatio = config.TimeRatio,
            DowntimePolicy = config.DowntimePolicy,
            RealmEpoch = config.RealmEpoch,
            IsActive = isActive
        };
    }

    /// <summary>
    /// Publishes boundary events (hour, period, day, month, season, year) based on the
    /// list of boundary crossings detected during clock advancement. Delegates to the
    /// shared static helper to avoid duplication with the worker's boundary publishing.
    /// </summary>
    /// <param name="realmId">The realm whose clock crossed boundaries.</param>
    /// <param name="clock">The current clock state after advancement.</param>
    /// <param name="boundaries">The list of boundary crossings to publish events for.</param>
    /// <param name="isCatchUp">Whether these boundaries were crossed during catch-up processing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task PublishBoundaryEventsAsync(
        Guid realmId,
        RealmClockModel clock,
        List<BoundaryCrossing> boundaries,
        bool isCatchUp,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.worldstate", "WorldstateService.PublishBoundaryEvents");

        var snapshot = WorldstateBoundaryEventPublisher.MapClockToSnapshot(clock);
        await WorldstateBoundaryEventPublisher.PublishBoundaryEventsAsync(
            realmId, snapshot, clock, boundaries, isCatchUp, _messageBus, cancellationToken);
    }

    #endregion

}
