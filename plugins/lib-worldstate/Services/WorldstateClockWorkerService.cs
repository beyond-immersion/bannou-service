using BeyondImmersion.Bannou.Worldstate.ClientEvents;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Worldstate;

/// <summary>
/// Background service that periodically advances game clocks for all active realms.
/// Computes elapsed real time since the last tick, converts to game-seconds via
/// each realm's time ratio, advances the clock through the time calculator,
/// publishes boundary events, and handles catch-up for server downtime.
/// </summary>
public class WorldstateClockWorkerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly ILogger<WorldstateClockWorkerService> _logger;
    private readonly WorldstateServiceConfiguration _configuration;

    /// <summary>
    /// Creates a new instance of the WorldstateClockWorkerService.
    /// </summary>
    /// <param name="serviceProvider">Service provider for scoped resolution.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="configuration">Worldstate service configuration.</param>
    public WorldstateClockWorkerService(
        IServiceProvider serviceProvider,
        ITelemetryProvider telemetryProvider,
        ILogger<WorldstateClockWorkerService> logger,
        WorldstateServiceConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _telemetryProvider = telemetryProvider;
        _logger = logger;
        _configuration = configuration;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worldstate clock worker starting, tick interval: {IntervalSeconds}s, startup delay: {DelaySeconds}s",
            _configuration.ClockTickIntervalSeconds, _configuration.ClockTickStartupDelaySeconds);

        // Wait before first tick to allow other services to start
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(_configuration.ClockTickStartupDelaySeconds), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Worldstate clock worker cancelled during startup delay");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Expected during shutdown
                break;
            }
            catch (Exception ex)
            {
                // Top-level catch for the entire tick (e.g., scope creation failure, MySQL enumeration failure).
                // Per-realm errors are caught inside ProcessTickAsync so a single realm failure
                // does not stop the worker. This catch handles infrastructure-level failures.
                _logger.LogError(ex, "Error during worldstate clock tick");
                try
                {
                    using var errorScope = _serviceProvider.CreateScope();
                    var messageBus = errorScope.ServiceProvider.GetRequiredService<IMessageBus>();
                    await messageBus.TryPublishErrorAsync(
                        "worldstate",
                        "WorldstateClockWorkerService.ProcessTick",
                        ex.GetType().Name,
                        ex.Message,
                        severity: ServiceErrorEventSeverity.Error);
                }
                catch (Exception pubEx)
                {
                    // Don't let error publishing failures affect the loop
                    _logger.LogDebug(pubEx, "Failed to publish error event for clock worker tick failure");
                }
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_configuration.ClockTickIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Worldstate clock worker stopped");
    }

    /// <summary>
    /// Processes a single clock tick: enumerates all active realms from MySQL,
    /// computes elapsed game time for each, advances clocks, and publishes events.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal async Task ProcessTickAsync(CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.worldstate", "WorldstateClockWorkerService.ProcessTick");

        using var scope = _serviceProvider.CreateScope();
        var stateStoreFactory = scope.ServiceProvider.GetRequiredService<IStateStoreFactory>();
        var lockProvider = scope.ServiceProvider.GetRequiredService<IDistributedLockProvider>();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var timeCalculator = scope.ServiceProvider.GetRequiredService<IWorldstateTimeCalculator>();
        var entitySessionRegistry = scope.ServiceProvider.GetRequiredService<IEntitySessionRegistry>();

        // Enumerate all realm configs from MySQL (authoritative list of initialized realms)
        var queryableRealmConfigStore = stateStoreFactory.GetQueryableStore<RealmWorldstateConfigModel>(
            StateStoreDefinitions.WorldstateCalendar);
        var realmConfigs = await queryableRealmConfigStore.QueryAsync(
            _ => true, cancellationToken);

        if (realmConfigs.Count == 0)
        {
            _logger.LogDebug("No realm configs found, skipping tick");
            return;
        }

        var clockStore = stateStoreFactory.GetStore<RealmClockModel>(StateStoreDefinitions.WorldstateRealmClock);
        var calendarStore = stateStoreFactory.GetStore<CalendarTemplateModel>(StateStoreDefinitions.WorldstateCalendar);
        var now = DateTimeOffset.UtcNow;

        _logger.LogDebug("Processing clock tick for {Count} realm(s)", realmConfigs.Count);

        foreach (var config in realmConfigs)
        {
            try
            {
                await ProcessRealmTickAsync(
                    config, clockStore, calendarStore, lockProvider,
                    messageBus, timeCalculator, entitySessionRegistry, now, cancellationToken);
            }
            catch (Exception ex)
            {
                // Per-realm error handling: log, publish error event, continue to next realm.
                // Single realm failure must not stop the worker (per IMPLEMENTATION TENETS:
                // catch is required for specific recovery logic in loops).
                _logger.LogError(ex, "Error processing clock tick for realm {RealmId}", config.RealmId);
                try
                {
                    await messageBus.TryPublishErrorAsync(
                        "worldstate",
                        "WorldstateClockWorkerService.ProcessRealmTick",
                        ex.GetType().Name,
                        ex.Message,
                        severity: ServiceErrorEventSeverity.Error);
                }
                catch (Exception pubEx)
                {
                    _logger.LogDebug(pubEx, "Failed to publish error event for realm {RealmId} tick failure", config.RealmId);
                }
            }
        }
    }

    /// <summary>
    /// Processes a single realm's clock tick: acquires lock, fetches clock from Redis,
    /// computes elapsed game time, handles catch-up if needed, advances clock, publishes
    /// boundary events, and updates Redis.
    /// </summary>
    /// <param name="config">The realm's durable configuration from MySQL.</param>
    /// <param name="clockStore">Redis store for clock state.</param>
    /// <param name="calendarStore">MySQL store for calendar templates.</param>
    /// <param name="lockProvider">Distributed lock provider.</param>
    /// <param name="messageBus">Message bus for event publishing.</param>
    /// <param name="timeCalculator">Time calculator for clock advancement.</param>
    /// <param name="entitySessionRegistry">Entity session registry for client event publishing.</param>
    /// <param name="now">Current real-world timestamp for consistent processing.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task ProcessRealmTickAsync(
        RealmWorldstateConfigModel config,
        IStateStore<RealmClockModel> clockStore,
        IStateStore<CalendarTemplateModel> calendarStore,
        IDistributedLockProvider lockProvider,
        IMessageBus messageBus,
        IWorldstateTimeCalculator timeCalculator,
        IEntitySessionRegistry entitySessionRegistry,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Acquire lock for this realm's clock
        var lockOwner = $"worldstate-worker-{Guid.NewGuid():N}";
        await using var lockResponse = await lockProvider.LockAsync(
            StateStoreDefinitions.WorldstateLock,
            $"clock:{config.RealmId}",
            lockOwner,
            _configuration.DistributedLockTimeoutSeconds,
            cancellationToken);
        if (!lockResponse.Success)
        {
            _logger.LogDebug("Skipping realm {RealmId} tick, lock held by another process", config.RealmId);
            return;
        }

        // Fetch clock from Redis
        var clockKey = $"realm:{config.RealmId}";
        var clock = await clockStore.GetAsync(clockKey, cancellationToken);
        if (clock == null)
        {
            _logger.LogWarning("Realm config exists but clock is missing in Redis for realm {RealmId} (possible data corruption)",
                config.RealmId);
            return;
        }

        // Skip inactive clocks (ratio == 0)
        if (!clock.IsActive || clock.TimeRatio <= 0.0f)
        {
            return;
        }

        // Load calendar template
        var calendarKey = $"calendar:{config.GameServiceId}:{config.CalendarTemplateCode}";
        var calendar = await calendarStore.GetAsync(calendarKey, cancellationToken);
        if (calendar == null)
        {
            _logger.LogWarning("Calendar template {TemplateCode} not found for realm {RealmId} during clock tick",
                config.CalendarTemplateCode, config.RealmId);
            return;
        }

        // Compute elapsed real time since last advancement
        var realElapsed = now - clock.LastAdvancedRealTime;
        var realElapsedSeconds = realElapsed.TotalSeconds;

        // Check for catch-up scenario (large gap indicates downtime)
        var secondsPerDay = (long)calendar.GameHoursPerDay * 3600L;
        var maxCatchUpGameSeconds = (long)_configuration.MaxCatchUpGameDays * secondsPerDay;
        var rawGameSeconds = (long)(realElapsedSeconds * clock.TimeRatio);

        if (rawGameSeconds <= 0)
        {
            return;
        }

        // Determine if this is a catch-up scenario and apply downtime policy
        var isCatchUp = rawGameSeconds > maxCatchUpGameSeconds;
        long gameSecondsToAdvance;

        if (isCatchUp)
        {
            switch (clock.DowntimePolicy)
            {
                case DowntimePolicy.Advance:
                    // Cap at MaxCatchUpGameDays
                    gameSecondsToAdvance = maxCatchUpGameSeconds;
                    _logger.LogInformation(
                        "Catch-up for realm {RealmId}: capping at {MaxDays} game-days ({GameSeconds} game-seconds, raw was {RawSeconds})",
                        config.RealmId, _configuration.MaxCatchUpGameDays, gameSecondsToAdvance, rawGameSeconds);
                    break;

                case DowntimePolicy.Pause:
                    // Set LastAdvancedRealTime to now, continue from where clock stopped
                    clock.LastAdvancedRealTime = now;
                    await clockStore.SaveAsync(clockKey, clock, cancellationToken: cancellationToken);
                    _logger.LogInformation(
                        "Catch-up for realm {RealmId}: pause policy applied, resetting LastAdvancedRealTime without advancement",
                        config.RealmId);
                    return;

                default:
                    gameSecondsToAdvance = maxCatchUpGameSeconds;
                    break;
            }
        }
        else
        {
            gameSecondsToAdvance = rawGameSeconds;
        }

        // Advance the clock
        var boundaries = timeCalculator.AdvanceGameTime(clock, calendar, gameSecondsToAdvance);

        // During catch-up, skip hour-changed events (too noisy) — only publish coarser boundaries
        List<BoundaryCrossing> boundariesToPublish;
        if (isCatchUp)
        {
            boundariesToPublish = boundaries
                .Where(b => b.Type != BoundaryType.Hour)
                .ToList();
        }
        else
        {
            boundariesToPublish = boundaries;
        }

        // Respect batch size limit per tick
        if (boundariesToPublish.Count > _configuration.BoundaryEventBatchSize)
        {
            boundariesToPublish = boundariesToPublish
                .Take(_configuration.BoundaryEventBatchSize)
                .ToList();
        }

        // Publish boundary events via shared helper (eliminates duplication with service)
        var snapshot = WorldstateBoundaryEventPublisher.MapClockToSnapshot(clock);
        await WorldstateBoundaryEventPublisher.PublishBoundaryEventsAsync(
            config.RealmId, snapshot, clock, boundariesToPublish, isCatchUp, messageBus, cancellationToken);

        // If period-changed boundary was crossed, publish time sync client event.
        // Must check boundariesToPublish (the filtered/batched list), not the unfiltered
        // boundaries list, to stay consistent with what was actually published.
        var periodChanged = boundariesToPublish.Any(b => b.Type == BoundaryType.Period);
        if (periodChanged)
        {
            var syncEvent = new WorldstateTimeSyncEvent
            {
                RealmId = config.RealmId,
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
                SyncReason = TimeSyncReason.PeriodChanged
            };
            await entitySessionRegistry.PublishToEntitySessionsAsync("realm", config.RealmId, syncEvent, cancellationToken);
        }

        // Update Redis clock with new state
        clock.LastAdvancedRealTime = now;
        await clockStore.SaveAsync(clockKey, clock, cancellationToken: cancellationToken);
    }

}
