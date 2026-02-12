using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.GameSession;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Seed;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Gardener;

/// <summary>
/// Background service that manages active scenario instance lifecycle.
/// Detects timed-out and abandoned scenarios, awards partial growth,
/// and coordinates cleanup (game session, history archival).
/// </summary>
/// <remarks>
/// <para>
/// <b>IMPLEMENTATION TENETS - Background Service Pattern:</b>
/// Uses IServiceProvider.CreateScope() to access scoped services per cycle.
/// Follows established patterns from SeedDecayWorkerService.
/// </para>
/// <para>
/// <b>IMPLEMENTATION TENETS - Configuration-First:</b>
/// Uses ScenarioTimeoutMinutes and AbandonDetectionMinutes from configuration
/// for lifecycle thresholds. Uses GrowthAwardMultiplier for partial growth calculation.
/// </para>
/// </remarks>
public class GardenerScenarioLifecycleWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<GardenerScenarioLifecycleWorker> _logger;
    private readonly GardenerServiceConfiguration _configuration;

    /// <summary>
    /// Interval between lifecycle evaluation cycles, from configuration.
    /// </summary>
    private TimeSpan WorkerInterval =>
        TimeSpan.FromSeconds(_configuration.ScenarioLifecycleWorkerIntervalSeconds);

    /// <summary>
    /// Startup delay before first cycle, from configuration.
    /// </summary>
    private TimeSpan StartupDelay =>
        TimeSpan.FromSeconds(_configuration.BackgroundServiceStartupDelaySeconds);

    /// <summary>
    /// Initializes the scenario lifecycle worker with required dependencies.
    /// </summary>
    /// <param name="serviceProvider">Service provider for creating scopes to access scoped services.</param>
    /// <param name="logger">Logger for structured logging.</param>
    /// <param name="configuration">Service configuration with lifecycle settings.</param>
    public GardenerScenarioLifecycleWorker(
        IServiceProvider serviceProvider,
        ILogger<GardenerScenarioLifecycleWorker> logger,
        GardenerServiceConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Main execution loop for the background service.
    /// Runs at ScenarioLifecycleWorkerIntervalSeconds after an initial startup delay.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token for graceful shutdown.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Scenario lifecycle worker starting, interval: {Interval}s, startup delay: {Delay}s",
            _configuration.ScenarioLifecycleWorkerIntervalSeconds,
            _configuration.BackgroundServiceStartupDelaySeconds);

        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Scenario lifecycle worker cancelled during startup");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessLifecycleCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during scenario lifecycle processing cycle");
                try
                {
                    using var errorScope = _serviceProvider.CreateScope();
                    var messageBus = errorScope.ServiceProvider.GetRequiredService<IMessageBus>();
                    await messageBus.TryPublishErrorAsync(
                        "gardener",
                        "GardenerScenarioLifecycleWorker",
                        ex.GetType().Name,
                        ex.Message,
                        severity: ServiceErrorEventSeverity.Error);
                }
                catch (Exception pubEx)
                {
                    _logger.LogDebug(pubEx, "Failed to publish error event - continuing lifecycle loop");
                }
            }

            try
            {
                await Task.Delay(WorkerInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Scenario lifecycle worker stopped");
    }

    /// <summary>
    /// Processes one full lifecycle cycle: iterates all active scenarios,
    /// checks for timeouts and abandonment.
    /// </summary>
    private async Task ProcessLifecycleCycleAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var stateStoreFactory = scope.ServiceProvider.GetRequiredService<IStateStoreFactory>();
        var lockProvider = scope.ServiceProvider.GetRequiredService<IDistributedLockProvider>();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var seedClient = scope.ServiceProvider.GetRequiredService<ISeedClient>();
        var gameSessionClient = scope.ServiceProvider.GetRequiredService<IGameSessionClient>();

        var scenarioStore = stateStoreFactory.GetStore<ScenarioInstanceModel>(
            StateStoreDefinitions.GardenerScenarioInstances);
        var templateStore = stateStoreFactory.GetJsonQueryableStore<ScenarioTemplateModel>(
            StateStoreDefinitions.GardenerScenarioTemplates);
        var historyStore = stateStoreFactory.GetJsonQueryableStore<ScenarioHistoryModel>(
            StateStoreDefinitions.GardenerScenarioHistory);
        var cacheStore = stateStoreFactory.GetCacheableStore<GardenInstanceModel>(
            StateStoreDefinitions.GardenerVoidInstances);

        // Get all active scenario account IDs from tracking set
        var activeAccountIds = await cacheStore.GetSetAsync<Guid>(
            GardenerService.ActiveScenariosTrackingKey, ct);

        if (activeAccountIds.Count == 0)
            return;

        _logger.LogDebug(
            "Processing lifecycle cycle for {Count} active scenarios", activeAccountIds.Count);

        var now = DateTimeOffset.UtcNow;
        var timedOut = 0;
        var abandoned = 0;

        foreach (var accountId in activeAccountIds)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var result = await ProcessScenarioLifecycleAsync(
                    accountId, now, scenarioStore, templateStore, historyStore,
                    cacheStore, lockProvider, seedClient, gameSessionClient, messageBus, ct);

                if (result == LifecycleResult.TimedOut) timedOut++;
                else if (result == LifecycleResult.Abandoned) abandoned++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to process lifecycle for account {AccountId}", accountId);
            }
        }

        if (timedOut > 0 || abandoned > 0)
        {
            _logger.LogInformation(
                "Lifecycle cycle complete: timedOut={TimedOut}, abandoned={Abandoned}",
                timedOut, abandoned);
        }
    }

    /// <summary>
    /// Processes a single scenario instance for timeout or abandonment.
    /// </summary>
    private async Task<LifecycleResult> ProcessScenarioLifecycleAsync(
        Guid accountId,
        DateTimeOffset now,
        IStateStore<ScenarioInstanceModel> scenarioStore,
        IJsonQueryableStateStore<ScenarioTemplateModel> templateStore,
        IJsonQueryableStateStore<ScenarioHistoryModel> historyStore,
        ICacheableStateStore<GardenInstanceModel> cacheStore,
        IDistributedLockProvider lockProvider,
        ISeedClient seedClient,
        IGameSessionClient gameSessionClient,
        IMessageBus messageBus,
        CancellationToken ct)
    {
        var scenarioKey = $"scenario:{accountId}";
        var scenario = await scenarioStore.GetAsync(scenarioKey, ct);

        if (scenario == null)
        {
            // Scenario no longer exists; clean up tracking set
            await cacheStore.RemoveFromSetAsync<Guid>(
                GardenerService.ActiveScenariosTrackingKey, accountId, ct);
            return LifecycleResult.None;
        }

        if (scenario.Status != ScenarioStatus.Active)
        {
            // Non-active scenario in tracking set; clean up
            await cacheStore.RemoveFromSetAsync<Guid>(
                GardenerService.ActiveScenariosTrackingKey, accountId, ct);
            return LifecycleResult.None;
        }

        // Check timeout: scenario exceeded maximum duration
        var durationMinutes = (now - scenario.CreatedAt).TotalMinutes;
        if (durationMinutes >= _configuration.ScenarioTimeoutMinutes)
        {
            _logger.LogInformation(
                "Scenario {ScenarioId} timed out after {Duration} minutes for account {AccountId}",
                scenario.ScenarioInstanceId, durationMinutes, accountId);

            await ForceCompleteScenarioAsync(
                accountId, scenario, scenarioStore, templateStore, historyStore,
                cacheStore, lockProvider, seedClient, gameSessionClient, messageBus,
                isTimeout: true, ct);

            return LifecycleResult.TimedOut;
        }

        // Check abandonment: no activity for configured duration
        var inactiveMinutes = (now - scenario.LastActivityAt).TotalMinutes;
        if (inactiveMinutes >= _configuration.AbandonDetectionMinutes)
        {
            _logger.LogInformation(
                "Scenario {ScenarioId} abandoned after {Inactive} minutes of inactivity for account {AccountId}",
                scenario.ScenarioInstanceId, inactiveMinutes, accountId);

            await ForceCompleteScenarioAsync(
                accountId, scenario, scenarioStore, templateStore, historyStore,
                cacheStore, lockProvider, seedClient, gameSessionClient, messageBus,
                isTimeout: false, ct);

            return LifecycleResult.Abandoned;
        }

        return LifecycleResult.None;
    }

    /// <summary>
    /// Force-completes a scenario due to timeout or abandonment.
    /// Awards partial growth, moves to history, cleans up game session.
    /// </summary>
    private async Task ForceCompleteScenarioAsync(
        Guid accountId,
        ScenarioInstanceModel scenario,
        IStateStore<ScenarioInstanceModel> scenarioStore,
        IJsonQueryableStateStore<ScenarioTemplateModel> templateStore,
        IJsonQueryableStateStore<ScenarioHistoryModel> historyStore,
        ICacheableStateStore<GardenInstanceModel> cacheStore,
        IDistributedLockProvider lockProvider,
        ISeedClient seedClient,
        IGameSessionClient gameSessionClient,
        IMessageBus messageBus,
        bool isTimeout,
        CancellationToken ct)
    {
        var lockOwner = $"lifecycle-{Guid.NewGuid():N}";
        await using var lockResult = await lockProvider.LockAsync(
            StateStoreDefinitions.GardenerLock,
            $"scenario:{accountId}",
            lockOwner,
            _configuration.DistributedLockTimeoutSeconds,
            ct);

        if (!lockResult.Success)
        {
            _logger.LogDebug(
                "Could not acquire lock for scenario lifecycle, account {AccountId}", accountId);
            return;
        }

        // Re-read under lock for freshness
        var scenarioKey = $"scenario:{accountId}";
        scenario = await scenarioStore.GetAsync(scenarioKey, ct)
            ?? throw new InvalidOperationException(
                $"Scenario for account {accountId} disappeared during lifecycle processing");

        if (scenario.Status != ScenarioStatus.Active)
            return;

        // Load template for growth calculation
        var template = await templateStore.GetAsync(
            $"template:{scenario.ScenarioTemplateId}", ct);

        // Calculate partial growth based on time spent
        var partialGrowth = CalculatePartialGrowth(scenario, template);

        // Award partial growth via seed client
        if (partialGrowth.Count > 0)
        {
            var primaryParticipant = scenario.Participants.FirstOrDefault();
            if (primaryParticipant != null)
            {
                var entries = partialGrowth.Select(kvp =>
                    new GrowthEntry { Domain = kvp.Key, Amount = kvp.Value }).ToList();

                foreach (var participant in scenario.Participants)
                {
                    try
                    {
                        await seedClient.RecordGrowthBatchAsync(new RecordGrowthBatchRequest
                        {
                            SeedId = participant.SeedId,
                            Entries = entries,
                            Source = "gardener"
                        }, ct);
                    }
                    catch (ApiException ex)
                    {
                        _logger.LogWarning(ex,
                            "Failed to award partial growth for participant seed {SeedId}",
                            participant.SeedId);
                    }
                }
            }
        }

        // Update scenario status
        scenario.Status = isTimeout ? ScenarioStatus.Completed : ScenarioStatus.Abandoned;
        scenario.CompletedAt = DateTimeOffset.UtcNow;
        scenario.GrowthAwarded = partialGrowth;

        // Write to durable history
        var primaryForHistory = scenario.Participants.FirstOrDefault();
        if (primaryForHistory != null)
        {
            var durationSeconds = scenario.CompletedAt.HasValue
                ? (float)(scenario.CompletedAt.Value - scenario.CreatedAt).TotalSeconds
                : (float)(DateTimeOffset.UtcNow - scenario.CreatedAt).TotalSeconds;

            var history = new ScenarioHistoryModel
            {
                ScenarioInstanceId = scenario.ScenarioInstanceId,
                ScenarioTemplateId = scenario.ScenarioTemplateId,
                AccountId = primaryForHistory.AccountId,
                SeedId = primaryForHistory.SeedId,
                CompletedAt = scenario.CompletedAt ?? DateTimeOffset.UtcNow,
                Status = scenario.Status,
                GrowthAwarded = scenario.GrowthAwarded,
                DurationSeconds = durationSeconds,
                TemplateCode = template?.Code
            };

            await historyStore.SaveAsync(
                $"history:{scenario.ScenarioInstanceId}", history, cancellationToken: ct);
        }

        // Clean up from Redis
        await scenarioStore.DeleteAsync(scenarioKey, ct);

        // Remove from tracking set
        await cacheStore.RemoveFromSetAsync<Guid>(
            GardenerService.ActiveScenariosTrackingKey, accountId, ct);

        // Clean up game session
        foreach (var participant in scenario.Participants)
        {
            try
            {
                // IMPLEMENTATION TENETS: Guid.Empty is a sentinel (no-sentinel violation);
                // game-session schema requires non-nullable webSocketSessionId but
                // server-side leave has no real session. Needs game-session schema fix.
                await gameSessionClient.LeaveGameSessionByIdAsync(
                    new LeaveGameSessionByIdRequest
                    {
                        GameSessionId = scenario.GameSessionId,
                        AccountId = participant.AccountId,
                        WebSocketSessionId = Guid.Empty
                    }, ct);
            }
            catch (ApiException ex)
            {
                _logger.LogWarning(ex,
                    "Failed to remove participant {AccountId} from game session {SessionId}",
                    participant.AccountId, scenario.GameSessionId);
            }
        }

        // Publish event
        var eventTopic = isTimeout
            ? "gardener.scenario.completed"
            : "gardener.scenario.abandoned";

        if (isTimeout)
        {
            await messageBus.TryPublishAsync(eventTopic,
                new GardenerScenarioCompletedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    ScenarioInstanceId = scenario.ScenarioInstanceId,
                    ScenarioTemplateId = scenario.ScenarioTemplateId,
                    AccountId = accountId,
                    GrowthAwarded = partialGrowth
                }, cancellationToken: ct);
        }
        else
        {
            await messageBus.TryPublishAsync(eventTopic,
                new GardenerScenarioAbandonedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    ScenarioInstanceId = scenario.ScenarioInstanceId,
                    AccountId = accountId
                }, cancellationToken: ct);
        }
    }

    /// <summary>
    /// Calculates partial growth awards for a timed-out or abandoned scenario
    /// using the shared growth calculation logic.
    /// </summary>
    private Dictionary<string, float> CalculatePartialGrowth(
        ScenarioInstanceModel scenario,
        ScenarioTemplateModel? template)
    {
        return GardenerGrowthCalculation.CalculateGrowth(
            scenario, template, _configuration.GrowthAwardMultiplier, fullCompletion: false,
            (float)_configuration.GrowthFullCompletionMaxRatio,
            (float)_configuration.GrowthFullCompletionMinRatio,
            (float)_configuration.GrowthPartialMaxRatio,
            _configuration.DefaultEstimatedDurationMinutes);
    }

    /// <summary>
    /// Result of processing a single scenario's lifecycle.
    /// </summary>
    private enum LifecycleResult
    {
        /// <summary>No action taken.</summary>
        None,
        /// <summary>Scenario was force-completed due to timeout.</summary>
        TimedOut,
        /// <summary>Scenario was marked abandoned due to inactivity.</summary>
        Abandoned
    }
}
