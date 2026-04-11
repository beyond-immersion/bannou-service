#nullable enable

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Seed;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Genesis.Services;

/// <summary>
/// Background service that drains the <see cref="GenesisGrowthState"/> growth accumulator on a fixed
/// interval and applies batched seed growth through <see cref="ISeedClient.RecordGrowthBatchAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose:</b> The <see cref="GenesisCurrencyTransactionListener"/> buffers every matching wallet
/// mutation into an in-memory accumulator. This worker periodically drains that accumulator and writes
/// the aggregated growth to Seed via a batch API — one call per entity per flush interval, regardless
/// of how many wallet mutations accumulated.
/// </para>
/// <para>
/// <b>Scale rationale:</b> At 100K genesis entities each with autogain-enabled wallets, the naive
/// approach (one Seed call per currency mutation) would require 100K+ Seed lock acquisitions per
/// autogain cycle. Batching collapses that to one Seed call per entity per flush interval — a
/// 10-20x reduction in lock contention at the cost of flush latency (default 5 seconds), which is
/// invisible for entities whose growth phases span game-hours or game-days.
/// </para>
/// <para>
/// <b>Workflow per cycle:</b>
/// <list type="number">
///   <item>Atomically drain the growth accumulator (<see cref="GenesisGrowthState.DrainAccumulator"/>).</item>
///   <item>For each entity in the drained set, look up the entity record to resolve its SeedId.</item>
///   <item>Look up the template to get the growth mappings snapshot.</item>
///   <item>Apply template mappings (amount × ratio filtered by direction) to produce domain entries.</item>
///   <item>Call <see cref="ISeedClient.RecordGrowthBatchAsync"/> once per entity with the computed entries.</item>
///   <item>Per-entity try-catch: one entity's failure cannot block other entities from processing.</item>
/// </list>
/// </para>
/// <para>
/// <b>Phase transition dispatch:</b> This worker does not observe phase transitions directly. Seed's
/// internal <see cref="ISeedEvolutionListener"/> dispatch fires after each <c>RecordGrowthBatchAsync</c>
/// call, which invokes <see cref="GenesisSeedEvolutionListener"/> for the cognitive stage transitions.
/// </para>
/// <para>
/// Follows the canonical <see cref="BackgroundService"/> polling loop per FOUNDATION TENETS:
/// startup delay → main loop with double-catch cancellation filter → per-cycle telemetry span →
/// error event publishing → interval delay with its own cancellation handler.
/// </para>
/// </remarks>
[BannouHelperService("genesis-growth-flush", typeof(IGenesisService), lifetime: ServiceLifetime.Singleton, RegistrationMode = HelperRegistrationMode.HostedService)]
public class GenesisGrowthFlushWorkerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly GenesisGrowthState _state;
    private readonly ILogger<GenesisGrowthFlushWorkerService> _logger;
    private readonly GenesisServiceConfiguration _configuration;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="GenesisGrowthFlushWorkerService"/> class.
    /// </summary>
    public GenesisGrowthFlushWorkerService(
        IServiceProvider serviceProvider,
        GenesisGrowthState state,
        ILogger<GenesisGrowthFlushWorkerService> logger,
        GenesisServiceConfiguration configuration,
        ITelemetryProvider telemetryProvider)
    {
        _serviceProvider = serviceProvider;
        _state = state;
        _logger = logger;
        _configuration = configuration;
        _telemetryProvider = telemetryProvider;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Startup delay so the plugin lifecycle has time to populate the wallet map before we start flushing
        try
        {
            await Task.Delay(
                TimeSpan.FromSeconds(_configuration.StartupDelaySeconds), stoppingToken);
        }
        catch (OperationCanceledException) { return; }

        _logger.LogInformation(
            "{Worker} starting, flush interval: {IntervalSeconds}s",
            nameof(GenesisGrowthFlushWorkerService), _configuration.GrowthFlushIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var activity = _telemetryProvider.StartActivity(
                    "bannou.genesis", "GenesisGrowthFlushWorkerService.ProcessCycle");
                await ProcessFlushCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // Graceful shutdown — not an error
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Worker} cycle failed", nameof(GenesisGrowthFlushWorkerService));
                await _serviceProvider.TryPublishWorkerErrorAsync(
                    "genesis", "GrowthFlushCycle", ex, _logger, stoppingToken);
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(_configuration.GrowthFlushIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("{Worker} stopped", nameof(GenesisGrowthFlushWorkerService));
    }

    /// <summary>
    /// Drains the accumulator and applies batched growth for every entity with pending buffered credits.
    /// </summary>
    private async Task ProcessFlushCycleAsync(CancellationToken cancellationToken)
    {
        var drained = _state.DrainAccumulator();
        if (drained.Count == 0)
        {
            _logger.LogDebug("Growth flush cycle: no entities with pending growth");
            return;
        }

        _logger.LogDebug("Growth flush cycle: draining {EntityCount} entities with buffered growth",
            drained.Count);

        using var scope = _serviceProvider.CreateScope();
        var stateStoreFactory = scope.ServiceProvider.GetRequiredService<IStateStoreFactory>();
        var entityStore = stateStoreFactory.GetStore<GenesisEntityModel>(StateStoreDefinitions.GenesisEntities);
        var templateStore = stateStoreFactory.GetStore<GenesisTemplateModel>(StateStoreDefinitions.GenesisTemplates);
        var seedClient = scope.ServiceProvider.GetRequiredService<ISeedClient>();

        var successCount = 0;
        var failureCount = 0;
        var skippedCount = 0;

        foreach (var (entityId, entries) in drained)
        {
            try
            {
                var flushed = await FlushEntityAsync(entityId, entries, entityStore, templateStore, seedClient, cancellationToken);
                if (flushed) successCount++;
                else skippedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to flush growth for entity {EntityId}, continuing with remaining entities",
                    entityId);
                failureCount++;
            }
        }

        _logger.LogInformation(
            "Growth flush complete: {Success} succeeded, {Skipped} skipped, {Failed} failed",
            successCount, skippedCount, failureCount);
    }

    /// <summary>
    /// Flushes buffered growth for a single entity. Returns true if growth was applied, false if skipped
    /// (entity destroyed, template missing, or no mappings matched).
    /// </summary>
    private async Task<bool> FlushEntityAsync(
        Guid entityId,
        List<GrowthBufferEntry> entries,
        IStateStore<GenesisEntityModel> entityStore,
        IStateStore<GenesisTemplateModel> templateStore,
        ISeedClient seedClient,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.genesis", "GenesisGrowthFlushWorkerService.FlushEntity");

        var entity = await entityStore.GetAsync(GenesisService.BuildEntityKey(entityId), cancellationToken);
        if (entity == null)
        {
            // Entity destroyed between buffering and flushing — drop the buffered entries
            _logger.LogDebug(
                "Dropping buffered growth for entity {EntityId}: entity no longer exists",
                entityId);
            return false;
        }

        var template = await templateStore.GetAsync(GenesisService.BuildTemplateKey(entity.TemplateCode), cancellationToken);
        if (template == null)
        {
            _logger.LogWarning(
                "Dropping buffered growth for entity {EntityId}: template {TemplateCode} missing",
                entityId, entity.TemplateCode);
            return false;
        }

        // Aggregate buffered credits by (walletCode, domain) to minimize entries sent to Seed
        var domainAmounts = new Dictionary<string, float>();
        foreach (var entry in entries)
        {
            foreach (var mapping in template.Economy.GrowthMappings)
            {
                if (!string.Equals(mapping.WalletCode, entry.WalletCode, StringComparison.Ordinal))
                    continue;
                if (!DirectionMatches(mapping.Direction, entry.Direction))
                    continue;

                var contribution = (float)(entry.Amount * mapping.Ratio);
                if (contribution <= 0f)
                    continue;

                domainAmounts[mapping.Domain] = domainAmounts.GetValueOrDefault(mapping.Domain) + contribution;
            }
        }

        if (domainAmounts.Count == 0)
        {
            _logger.LogDebug(
                "No growth mappings matched for entity {EntityId}: {EntryCount} buffered entries had no effect",
                entityId, entries.Count);
            return false;
        }

        var growthEntries = domainAmounts
            .Select(kvp => new GrowthEntry { Domain = kvp.Key, Amount = kvp.Value })
            .ToList();

        try
        {
            await seedClient.RecordGrowthBatchAsync(
                new RecordGrowthBatchRequest
                {
                    SeedId = entity.SeedId,
                    Entries = growthEntries,
                    Source = "genesis-growth-flush",
                }, cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex,
                "Seed RecordGrowthBatch failed for entity {EntityId} seed {SeedId}",
                entityId, entity.SeedId);
            return false;
        }

        _logger.LogDebug(
            "Flushed {DomainCount} domain contributions for entity {EntityId} (seed {SeedId})",
            growthEntries.Count, entityId, entity.SeedId);
        return true;
    }

    /// <summary>
    /// Returns true if a growth mapping with the given direction should accept a buffered entry
    /// with the given direction. Mappings with <see cref="GrowthDirection.Both"/> accept either;
    /// single-direction mappings must match exactly.
    /// </summary>
    private static bool DirectionMatches(GrowthDirection mappingDirection, GrowthDirection entryDirection)
    {
        return mappingDirection switch
        {
            GrowthDirection.Both => true,
            _ => mappingDirection == entryDirection,
        };
    }
}
