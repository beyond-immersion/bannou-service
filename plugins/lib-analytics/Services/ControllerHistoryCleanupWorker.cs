using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Analytics;

/// <summary>
/// Background service that periodically purges expired controller history records.
/// Uses the same cleanup logic as the CleanupControllerHistory endpoint, running
/// automatically at a configurable interval to avoid unbounded data growth.
/// </summary>
public class ControllerHistoryCleanupWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ControllerHistoryCleanupWorker> _logger;
    private readonly AnalyticsServiceConfiguration _configuration;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>
    /// Initializes a new instance of the ControllerHistoryCleanupWorker.
    /// </summary>
    /// <param name="serviceProvider">Service provider for scope creation and error publishing.</param>
    /// <param name="logger">Logger for structured logging.</param>
    /// <param name="configuration">Analytics service configuration with cleanup tunables.</param>
    /// <param name="telemetryProvider">Telemetry provider for per-cycle span instrumentation.</param>
    public ControllerHistoryCleanupWorker(
        IServiceProvider serviceProvider,
        ILogger<ControllerHistoryCleanupWorker> logger,
        AnalyticsServiceConfiguration configuration,
        ITelemetryProvider telemetryProvider)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
        _telemetryProvider = telemetryProvider;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(
                TimeSpan.FromSeconds(_configuration.ControllerHistoryCleanupStartupDelaySeconds),
                stoppingToken);
        }
        catch (OperationCanceledException) { return; }

        _logger.LogInformation("{Worker} starting, interval: {Interval}s",
            nameof(ControllerHistoryCleanupWorker), _configuration.ControllerHistoryCleanupIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var activity = _telemetryProvider.StartActivity(
                    "bannou.analytics", "ControllerHistoryCleanupWorker.ProcessCycle");
                await ProcessCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Worker} cycle failed", nameof(ControllerHistoryCleanupWorker));
                await _serviceProvider.TryPublishWorkerErrorAsync(
                    "analytics", "ControllerHistoryCleanup", ex, _logger, stoppingToken);
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(_configuration.ControllerHistoryCleanupIntervalSeconds),
                    stoppingToken);
            }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("{Worker} stopped", nameof(ControllerHistoryCleanupWorker));
    }

    /// <summary>
    /// Executes a single cleanup cycle, deleting controller history records older than
    /// the configured retention period in batched sub-operations.
    /// </summary>
    private async Task ProcessCycleAsync(CancellationToken cancellationToken)
    {
        var retentionDays = _configuration.ControllerHistoryRetentionDays;
        if (retentionDays <= 0)
        {
            return;
        }

        using var scope = _serviceProvider.CreateScope();
        var stateStoreFactory = scope.ServiceProvider.GetRequiredService<IStateStoreFactory>();

        var historyDataStore = stateStoreFactory.GetStore<ControllerHistoryData>(StateStoreDefinitions.AnalyticsHistoryData);
        var historyDataQueryStore = stateStoreFactory.GetJsonQueryableStore<ControllerHistoryData>(StateStoreDefinitions.AnalyticsHistoryData);

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

        var batchSize = _configuration.ControllerHistoryCleanupBatchSize;
        var totalDeleted = 0L;

        while (totalDeleted < batchSize)
        {
            var batchLimit = Math.Min(
                _configuration.ControllerHistoryCleanupSubBatchSize,
                (int)(batchSize - totalDeleted));

            var batch = await historyDataQueryStore.JsonQueryPagedAsync(
                conditions, 0, batchLimit, null, cancellationToken);

            if (batch.Items.Count == 0)
            {
                break;
            }

            foreach (var item in batch.Items)
            {
                try
                {
                    await historyDataStore.DeleteAsync(item.Key, cancellationToken);
                    totalDeleted++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete controller history record {Key}, continuing", item.Key);
                }
            }
        }

        if (totalDeleted > 0)
        {
            _logger.LogInformation("{Worker} cleanup completed: {DeletedCount} records deleted",
                nameof(ControllerHistoryCleanupWorker), totalDeleted);
        }
    }
}
