using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Seed;

/// <summary>
/// Background service that periodically applies growth decay to seed domains.
/// For each seed type where decay is enabled (per-type or global), iterates all non-archived
/// seeds and applies exponential decay to domains based on inactivity duration.
/// Phase regressions are detected and published as events.
/// </summary>
/// <remarks>
/// <para>
/// <b>IMPLEMENTATION TENETS - Background Service Pattern:</b>
/// Uses IServiceProvider.CreateScope() to access scoped services (IMessageBus, IStateStoreFactory).
/// Follows established patterns from MemoryDecaySchedulerService, ContractMilestoneExpirationService.
/// </para>
/// <para>
/// Decay formula per domain: depth *= (1 - ratePerDay) ^ decayDays
/// where decayDays = (now - max(domain.LastActivityAt, model.LastDecayedAt ?? seed.CreatedAt)).TotalDays
/// </para>
/// </remarks>
public class SeedDecayWorkerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SeedDecayWorkerService> _logger;
    private readonly SeedServiceConfiguration _configuration;

    /// <summary>
    /// Interval between decay worker cycles, from configuration.
    /// </summary>
    private TimeSpan WorkerInterval => TimeSpan.FromSeconds(_configuration.DecayWorkerIntervalSeconds);

    /// <summary>
    /// Startup delay before first cycle, from configuration.
    /// </summary>
    private TimeSpan StartupDelay => TimeSpan.FromSeconds(_configuration.DecayWorkerStartupDelaySeconds);

    /// <summary>
    /// Initializes the seed decay worker with required dependencies.
    /// </summary>
    /// <param name="serviceProvider">Service provider for creating scopes to access scoped services.</param>
    /// <param name="logger">Logger for structured logging.</param>
    /// <param name="configuration">Service configuration with decay settings.</param>
    public SeedDecayWorkerService(
        IServiceProvider serviceProvider,
        ILogger<SeedDecayWorkerService> logger,
        SeedServiceConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Main execution loop for the background service.
    /// Always runs on its configured interval -- per-type decay config is resolved
    /// inside the cycle, so types with overrides work even when the global default is off.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token for graceful shutdown.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Seed decay worker starting, interval: {Interval}s, startup delay: {Delay}s",
            _configuration.DecayWorkerIntervalSeconds,
            _configuration.DecayWorkerStartupDelaySeconds);

        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Seed decay worker cancelled during startup");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDecayCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during seed decay processing cycle");
                try
                {
                    using var errorScope = _serviceProvider.CreateScope();
                    var messageBus = errorScope.ServiceProvider.GetRequiredService<IMessageBus>();
                    await messageBus.TryPublishErrorAsync(
                        "seed",
                        "SeedDecayWorker",
                        ex.GetType().Name,
                        ex.Message,
                        severity: ServiceErrorEventSeverity.Error);
                }
                catch (Exception pubEx)
                {
                    _logger.LogDebug(pubEx, "Failed to publish error event - continuing decay loop");
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

        _logger.LogInformation("Seed decay worker stopped");
    }

    /// <summary>
    /// Processes one full decay cycle: iterates all seed types, applies decay to eligible seeds.
    /// </summary>
    private async Task ProcessDecayCycleAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Starting seed decay cycle");

        using var scope = _serviceProvider.CreateScope();
        var stateStoreFactory = scope.ServiceProvider.GetRequiredService<IStateStoreFactory>();
        var lockProvider = scope.ServiceProvider.GetRequiredService<IDistributedLockProvider>();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var typeStore = stateStoreFactory.GetJsonQueryableStore<SeedTypeDefinitionModel>(StateStoreDefinitions.SeedTypeDefinitions);
        var seedQueryStore = stateStoreFactory.GetJsonQueryableStore<SeedModel>(StateStoreDefinitions.Seed);
        var seedStore = stateStoreFactory.GetStore<SeedModel>(StateStoreDefinitions.Seed);
        var growthStore = stateStoreFactory.GetStore<SeedGrowthModel>(StateStoreDefinitions.SeedGrowth);
        var cacheStore = stateStoreFactory.GetStore<CapabilityManifestModel>(StateStoreDefinitions.SeedCapabilitiesCache);

        // Query all seed type definitions
        var typeConditions = new List<QueryCondition>
        {
            new QueryCondition { Path = "$.SeedTypeCode", Operator = QueryOperator.Exists, Value = true }
        };
        var typeResult = await typeStore.JsonQueryPagedAsync(typeConditions, 0, 500, null, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var totalSeedsProcessed = 0;
        var totalDomainsDecayed = 0;
        var totalPhasesRegressed = 0;

        foreach (var typeItem in typeResult.Items)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var seedType = typeItem.Value;
            var (decayEnabled, ratePerDay) = SeedService.ResolveDecayConfig(seedType, _configuration);

            if (!decayEnabled || ratePerDay <= 0f)
                continue;

            // Process all non-archived seeds of this type (paginated)
            var (processed, decayed, regressed) = await ProcessSeedsForTypeAsync(
                seedType, ratePerDay, now,
                seedQueryStore, seedStore, growthStore, cacheStore, lockProvider, messageBus,
                cancellationToken);

            totalSeedsProcessed += processed;
            totalDomainsDecayed += decayed;
            totalPhasesRegressed += regressed;
        }

        _logger.LogInformation(
            "Seed decay cycle complete: seeds={Seeds}, domainsDecayed={Domains}, phasesRegressed={Phases}",
            totalSeedsProcessed,
            totalDomainsDecayed,
            totalPhasesRegressed);
    }

    /// <summary>
    /// Processes decay for all non-archived seeds of a specific type.
    /// </summary>
    /// <returns>Tuple of (seeds processed, domains decayed, phases regressed).</returns>
    private async Task<(int Processed, int Decayed, int Regressed)> ProcessSeedsForTypeAsync(
        SeedTypeDefinitionModel seedType,
        float ratePerDay,
        DateTimeOffset now,
        IJsonQueryableStateStore<SeedModel> seedQueryStore,
        IStateStore<SeedModel> seedStore,
        IStateStore<SeedGrowthModel> growthStore,
        IStateStore<CapabilityManifestModel> cacheStore,
        IDistributedLockProvider lockProvider,
        IMessageBus messageBus,
        CancellationToken cancellationToken)
    {
        var conditions = new List<QueryCondition>
        {
            new QueryCondition { Path = "$.GameServiceId", Operator = QueryOperator.Equals, Value = seedType.GameServiceId.ToString() },
            new QueryCondition { Path = "$.SeedTypeCode", Operator = QueryOperator.Equals, Value = seedType.SeedTypeCode },
            new QueryCondition { Path = "$.Status", Operator = QueryOperator.NotEquals, Value = SeedStatus.Archived.ToString() },
            new QueryCondition { Path = "$.SeedId", Operator = QueryOperator.Exists, Value = true }
        };

        var pageSize = _configuration.DefaultQueryPageSize;
        var offset = 0;
        var totalProcessed = 0;
        var totalDecayed = 0;
        var totalRegressed = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var result = await seedQueryStore.JsonQueryPagedAsync(conditions, offset, pageSize, null, cancellationToken);

            foreach (var item in result.Items)
            {
                if (cancellationToken.IsCancellationRequested) break;

                var seed = item.Value;
                try
                {
                    var (decayed, regressed) = await ProcessSeedDecayAsync(
                        seed, seedType, ratePerDay, now,
                        seedStore, growthStore, cacheStore, lockProvider, messageBus,
                        cancellationToken);

                    totalProcessed++;
                    totalDecayed += decayed;
                    if (regressed) totalRegressed++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process decay for seed {SeedId}", seed.SeedId);
                }
            }

            offset += pageSize;
            if (result.Items.Count < pageSize || totalProcessed >= result.TotalCount)
                break;
        }

        return (totalProcessed, totalDecayed, totalRegressed);
    }

    /// <summary>
    /// Applies decay to a single seed's growth domains under a distributed lock.
    /// </summary>
    /// <returns>Tuple of (domains decayed, phase regressed).</returns>
    private async Task<(int DomainsDecayed, bool PhaseRegressed)> ProcessSeedDecayAsync(
        SeedModel seed,
        SeedTypeDefinitionModel seedType,
        float ratePerDay,
        DateTimeOffset now,
        IStateStore<SeedModel> seedStore,
        IStateStore<SeedGrowthModel> growthStore,
        IStateStore<CapabilityManifestModel> cacheStore,
        IDistributedLockProvider lockProvider,
        IMessageBus messageBus,
        CancellationToken cancellationToken)
    {
        var lockOwner = $"decay-{Guid.NewGuid():N}";
        await using var lockResponse = await lockProvider.LockAsync(
            StateStoreDefinitions.SeedLock, seed.SeedId.ToString(), lockOwner, 10, cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogDebug("Could not acquire lock for seed {SeedId} during decay, skipping", seed.SeedId);
            return (0, false);
        }

        // Re-read under lock for freshness
        seed = await seedStore.GetAsync($"seed:{seed.SeedId}", cancellationToken)
            ?? throw new InvalidOperationException($"Seed {seed.SeedId} disappeared during decay processing");

        var growth = await growthStore.GetAsync($"growth:{seed.SeedId}", cancellationToken);
        if (growth == null || growth.Domains.Count == 0)
            return (0, false);

        var domainsDecayed = 0;

        foreach (var (domainKey, entry) in growth.Domains)
        {
            // Determine when decay should start for this domain
            var decayStartTime = entry.LastActivityAt > (growth.LastDecayedAt ?? DateTimeOffset.MinValue)
                ? entry.LastActivityAt
                : growth.LastDecayedAt ?? seed.CreatedAt;

            var decayDays = (now - decayStartTime).TotalDays;
            if (decayDays <= 0)
                continue;

            var previousDepth = entry.Depth;
            entry.Depth = (float)Math.Max(0, entry.Depth * Math.Pow(1.0 - ratePerDay, decayDays));

            if (Math.Abs(previousDepth - entry.Depth) > float.Epsilon)
                domainsDecayed++;
        }

        if (domainsDecayed == 0)
        {
            // Still update LastDecayedAt to avoid reprocessing same time window
            growth.LastDecayedAt = now;
            await growthStore.SaveAsync($"growth:{seed.SeedId}", growth, cancellationToken: cancellationToken);
            return (0, false);
        }

        growth.LastDecayedAt = now;
        await growthStore.SaveAsync($"growth:{seed.SeedId}", growth, cancellationToken: cancellationToken);

        // Recompute total growth and check phase regression
        var newTotalGrowth = growth.Domains.Values.Sum(d => d.Depth);
        var previousPhase = seed.GrowthPhase;
        seed.TotalGrowth = newTotalGrowth;

        var phaseRegressed = false;
        if (seedType.GrowthPhases.Count > 0)
        {
            var phases = seedType.GrowthPhases.OrderBy(p => p.MinTotalGrowth).ToList();
            GrowthPhaseDefinition currentPhase = phases[0];
            for (var i = 0; i < phases.Count; i++)
            {
                if (newTotalGrowth >= phases[i].MinTotalGrowth)
                    currentPhase = phases[i];
                else
                    break;
            }

            seed.GrowthPhase = currentPhase.PhaseCode;
        }

        await seedStore.SaveAsync($"seed:{seed.SeedId}", seed, cancellationToken: cancellationToken);

        // Publish phase change event if phase regressed
        if (previousPhase != seed.GrowthPhase)
        {
            phaseRegressed = true;
            _logger.LogInformation(
                "Seed {SeedId} regressed from phase {OldPhase} to {NewPhase} due to decay",
                seed.SeedId, previousPhase, seed.GrowthPhase);

            await messageBus.TryPublishAsync("seed.phase.changed", new SeedPhaseChangedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                SeedId = seed.SeedId,
                SeedTypeCode = seed.SeedTypeCode,
                PreviousPhase = previousPhase,
                NewPhase = seed.GrowthPhase,
                TotalGrowth = newTotalGrowth,
                Direction = PhaseChangeDirection.Regressed
            }, cancellationToken: cancellationToken);
        }

        // Invalidate capability cache
        await cacheStore.DeleteAsync($"cap:{seed.SeedId}", cancellationToken);

        return (domainsDecayed, phaseRegressed);
    }
}
