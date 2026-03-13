using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Account;

/// <summary>
/// Background worker that permanently purges soft-deleted account records after the configured
/// retention period. Runs on a configurable interval (default: once per day).
/// </summary>
/// <remarks>
/// <para>
/// <b>FOUNDATION TENETS - Deletion Finality:</b>
/// Account is the sole service permitted to use soft-delete. This worker ensures soft-deleted
/// records are time-limited by hard-deleting them after <see cref="AccountServiceConfiguration.RetentionPeriodDays"/>.
/// </para>
/// <para>
/// <b>Design:</b> At soft-delete time, <c>DeleteAccountAsync</c> already removes email indexes,
/// provider indexes, and auth methods, and publishes <c>account.deleted</c>. This worker only
/// needs to hard-delete the account record itself — all secondary data is already cleaned up.
/// </para>
/// </remarks>
public class AccountRetentionWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AccountRetentionWorker> _logger;
    private readonly AccountServiceConfiguration _configuration;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>
    /// Initializes the account retention cleanup worker.
    /// </summary>
    /// <param name="serviceProvider">Service provider for creating scopes to access scoped services.</param>
    /// <param name="logger">Logger for structured logging.</param>
    /// <param name="configuration">Service configuration with retention period and interval settings.</param>
    /// <param name="telemetryProvider">Telemetry provider for per-cycle span instrumentation.</param>
    public AccountRetentionWorker(
        IServiceProvider serviceProvider,
        ILogger<AccountRetentionWorker> logger,
        AccountServiceConfiguration configuration,
        ITelemetryProvider telemetryProvider)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
        _telemetryProvider = telemetryProvider;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 1. Startup delay (configurable, with its own cancellation handler)
        try
        {
            await Task.Delay(
                TimeSpan.FromSeconds(_configuration.RetentionCleanupStartupDelaySeconds),
                stoppingToken);
        }
        catch (OperationCanceledException) { return; }

        _logger.LogInformation("{Worker} starting, interval: {Interval}s, retention: {Days} days",
            nameof(AccountRetentionWorker),
            _configuration.RetentionCleanupIntervalSeconds,
            _configuration.RetentionPeriodDays);

        // 2. Main loop
        while (!stoppingToken.IsCancellationRequested)
        {
            // 3. Work with double-catch cancellation filter
            try
            {
                using var activity = _telemetryProvider.StartActivity(
                    "bannou.account", "AccountRetentionWorker.ProcessCycle");
                await ProcessRetentionCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // Graceful shutdown — NOT an error
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Worker} cycle failed", nameof(AccountRetentionWorker));
                await _serviceProvider.TryPublishWorkerErrorAsync(
                    "account", "RetentionCleanup", ex, _logger, stoppingToken);
            }

            // 4. Delay with its own cancellation handler
            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(_configuration.RetentionCleanupIntervalSeconds),
                    stoppingToken);
            }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("{Worker} stopped", nameof(AccountRetentionWorker));
    }

    /// <summary>
    /// Queries for soft-deleted accounts past the retention period and hard-deletes them.
    /// Uses per-item error isolation so one corrupt record does not block all purging.
    /// </summary>
    private async Task ProcessRetentionCycleAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var stateStoreFactory = scope.ServiceProvider.GetRequiredService<IStateStoreFactory>();

        var queryableStore = stateStoreFactory.GetJsonQueryableStore<AccountModel>(
            StateStoreDefinitions.Account);
        var accountStore = stateStoreFactory.GetStore<AccountModel>(
            StateStoreDefinitions.Account);

        var cutoffUnix = DateTimeOffset.UtcNow
            .AddDays(-_configuration.RetentionPeriodDays)
            .ToUnixTimeSeconds();

        // Find soft-deleted accounts where DeletedAtUnix exists and is older than the cutoff
        var conditions = new List<QueryCondition>
        {
            new QueryCondition
            {
                Path = "$.AccountId",
                Operator = QueryOperator.Exists,
                Value = true
            },
            new QueryCondition
            {
                Path = "$.DeletedAtUnix",
                Operator = QueryOperator.Exists,
                Value = true
            },
            new QueryCondition
            {
                Path = "$.DeletedAtUnix",
                Operator = QueryOperator.LessThan,
                Value = cutoffUnix
            }
        };

        var expired = await queryableStore.JsonQueryAsync(conditions, ct);

        if (expired.Count == 0)
        {
            _logger.LogDebug("{Worker} found no expired accounts to purge", nameof(AccountRetentionWorker));
            return;
        }

        _logger.LogInformation("{Worker} found {Count} expired accounts to purge",
            nameof(AccountRetentionWorker), expired.Count);

        var purgedCount = 0;
        foreach (var result in expired)
        {
            // Per-item error isolation per IMPLEMENTATION TENETS
            try
            {
                await accountStore.DeleteAsync(result.Key, ct);
                purgedCount++;

                _logger.LogDebug("Purged expired account record {AccountId}",
                    result.Value.AccountId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to purge expired account record {Key}, will retry next cycle",
                    result.Key);
            }
        }

        _logger.LogInformation("{Worker} purged {Purged}/{Total} expired account records",
            nameof(AccountRetentionWorker), purgedCount, expired.Count);
    }
}
