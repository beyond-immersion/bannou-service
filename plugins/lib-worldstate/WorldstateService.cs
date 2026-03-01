using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.ClientEvents;
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
    private readonly IClientEventPublisher _clientEventPublisher;
    private readonly ILogger<WorldstateService> _logger;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly WorldstateServiceConfiguration _configuration;

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
        IClientEventPublisher clientEventPublisher,
        ILogger<WorldstateService> logger,
        ITelemetryProvider telemetryProvider,
        WorldstateServiceConfiguration configuration,
        IEventConsumer eventConsumer)
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
        _clientEventPublisher = clientEventPublisher;
        _logger = logger;
        _telemetryProvider = telemetryProvider;
        _configuration = configuration;

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

        return (StatusCodes.OK, MapClockToSnapshot(clock));
    }

    /// <summary>
    /// Implementation of GetRealmTimeByCode operation.
    /// </summary>
    public async Task<(StatusCodes, GameTimeSnapshot?)> GetRealmTimeByCodeAsync(GetRealmTimeByCodeRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement GetRealmTimeByCode
        await Task.CompletedTask;
        throw new NotImplementedException("Method GetRealmTimeByCode not yet implemented");
    }

    /// <summary>
    /// Implementation of BatchGetRealmTimes operation.
    /// </summary>
    public async Task<(StatusCodes, BatchGetRealmTimesResponse?)> BatchGetRealmTimesAsync(BatchGetRealmTimesRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement BatchGetRealmTimes
        await Task.CompletedTask;
        throw new NotImplementedException("Method BatchGetRealmTimes not yet implemented");
    }

    /// <summary>
    /// Implementation of GetElapsedGameTime operation.
    /// </summary>
    public async Task<(StatusCodes, GetElapsedGameTimeResponse?)> GetElapsedGameTimeAsync(GetElapsedGameTimeRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement GetElapsedGameTime
        await Task.CompletedTask;
        throw new NotImplementedException("Method GetElapsedGameTime not yet implemented");
    }

    /// <summary>
    /// Implementation of TriggerTimeSync operation.
    /// </summary>
    public async Task<(StatusCodes, TriggerTimeSyncResponse?)> TriggerTimeSyncAsync(TriggerTimeSyncRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement TriggerTimeSync
        await Task.CompletedTask;
        throw new NotImplementedException("Method TriggerTimeSync not yet implemented");
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

        // Resolve initial period and season from calendar
        var initialPeriod = ResolveDayPeriod(calendar, 0);
        var initialSeason = ResolveSeason(calendar, 0);

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
            RealmId = body.RealmId,
            GameServiceId = realm.GameServiceId,
            CalendarTemplateCode = templateCode,
            TimeRatio = initialRatio,
            DowntimePolicy = downtimePolicy,
            Epoch = epoch,
            StartingYear = startingYear
        });
    }

    /// <summary>
    /// Implementation of SetTimeRatio operation.
    /// </summary>
    public async Task<(StatusCodes, SetTimeRatioResponse?)> SetTimeRatioAsync(SetTimeRatioRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement SetTimeRatio
        await Task.CompletedTask;
        throw new NotImplementedException("Method SetTimeRatio not yet implemented");
    }

    /// <summary>
    /// Implementation of AdvanceClock operation.
    /// </summary>
    public async Task<(StatusCodes, AdvanceClockResponse?)> AdvanceClockAsync(AdvanceClockRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement AdvanceClock
        await Task.CompletedTask;
        throw new NotImplementedException("Method AdvanceClock not yet implemented");
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
    /// Implementation of CleanupByRealm operation.
    /// </summary>
    public async Task<(StatusCodes, CleanupByRealmResponse?)> CleanupByRealmAsync(CleanupByRealmRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement CleanupByRealm
        await Task.CompletedTask;
        throw new NotImplementedException("Method CleanupByRealm not yet implemented");
    }

    /// <summary>
    /// Implementation of CleanupByGameService operation.
    /// </summary>
    public async Task<(StatusCodes, CleanupByGameServiceResponse?)> CleanupByGameServiceAsync(CleanupByGameServiceRequest body, CancellationToken cancellationToken)
    {
        // TODO: Implement CleanupByGameService
        await Task.CompletedTask;
        throw new NotImplementedException("Method CleanupByGameService not yet implemented");
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
    /// Resolves the day period for a given game hour based on the calendar template.
    /// </summary>
    /// <param name="calendar">The calendar template with day period definitions.</param>
    /// <param name="hour">The game hour to resolve (0-based).</param>
    /// <returns>A tuple of (period code, isDaylight) for the matching day period.</returns>
    private static (string code, bool isDaylight) ResolveDayPeriod(CalendarTemplateModel calendar, int hour)
    {
        foreach (var period in calendar.DayPeriods)
        {
            if (hour >= period.StartHour && hour < period.EndHour)
            {
                return (period.Code, period.IsDaylight);
            }
        }

        // Calendars are validated before storage — if no period matches, data is corrupt.
        throw new InvalidOperationException(
            $"No day period covers hour {hour} in calendar template '{calendar.TemplateCode}'. This indicates corrupted calendar data.");
    }

    /// <summary>
    /// Resolves the season for a given month index based on the calendar template.
    /// </summary>
    /// <param name="calendar">The calendar template with month and season definitions.</param>
    /// <param name="monthIndex">The month index (0-based) to resolve.</param>
    /// <returns>A tuple of (season code, season index) for the month's season.</returns>
    private static (string code, int index) ResolveSeason(CalendarTemplateModel calendar, int monthIndex)
    {
        var month = calendar.Months[monthIndex];
        var seasonCode = month.SeasonCode;

        for (var i = 0; i < calendar.Seasons.Count; i++)
        {
            if (calendar.Seasons[i].Code == seasonCode)
            {
                return (seasonCode, calendar.Seasons[i].Ordinal);
            }
        }

        // Calendars are validated before storage — if no season matches, data is corrupt.
        throw new InvalidOperationException(
            $"Month at index {monthIndex} references season code '{seasonCode}' which does not exist in calendar template '{calendar.TemplateCode}'. This indicates corrupted calendar data.");
    }

    /// <summary>
    /// Maps a clock storage model to the API game time snapshot response type.
    /// </summary>
    /// <param name="clock">The internal clock storage model.</param>
    /// <returns>The game time snapshot response.</returns>
    private static GameTimeSnapshot MapClockToSnapshot(RealmClockModel clock)
    {
        return new GameTimeSnapshot
        {
            RealmId = clock.RealmId,
            GameServiceId = clock.GameServiceId,
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
            Timestamp = clock.LastAdvancedRealTime
        };
    }

    #endregion

}
