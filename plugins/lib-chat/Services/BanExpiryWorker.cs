using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Chat;

/// <summary>
/// Background service that periodically scans the chat-bans store for expired
/// time-limited bans and deletes them.
/// </summary>
/// <remarks>
/// <para>
/// <b>IMPLEMENTATION TENETS - Background Service Pattern:</b>
/// Uses IServiceProvider.CreateScope() to access scoped services.
/// Follows established patterns from IdleRoomCleanupWorker and ContractMilestoneExpirationService.
/// </para>
/// <para>
/// <b>IMPLEMENTATION TENETS - Multi-Instance Safety:</b>
/// Acquires a distributed lock per cycle to prevent multiple instances from
/// processing the same expired bans concurrently.
/// </para>
/// <para>
/// <b>IMPLEMENTATION TENETS - Configuration-First:</b>
/// Uses BanExpiryIntervalMinutes, BanExpiryStartupDelaySeconds, and BanExpiryBatchSize
/// from ChatServiceConfiguration.
/// </para>
/// </remarks>
public class BanExpiryWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BanExpiryWorker> _logger;
    private readonly ChatServiceConfiguration _configuration;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>
    /// Interval between expiry cycles, from configuration.
    /// </summary>
    private TimeSpan WorkerInterval => TimeSpan.FromMinutes(_configuration.BanExpiryIntervalMinutes);

    /// <summary>
    /// Initializes the ban expiry worker with required dependencies.
    /// </summary>
    /// <param name="serviceProvider">Service provider for creating scopes to access scoped services.</param>
    /// <param name="logger">Logger for structured logging.</param>
    /// <param name="configuration">Service configuration with ban expiry settings.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    public BanExpiryWorker(
        IServiceProvider serviceProvider,
        ILogger<BanExpiryWorker> logger,
        ChatServiceConfiguration configuration,
        ITelemetryProvider telemetryProvider)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
        _telemetryProvider = telemetryProvider;
    }

    /// <summary>
    /// Main execution loop for the background service.
    /// Runs on a configurable interval and scans for expired bans to delete.
    /// </summary>
    /// <param name="stoppingToken">Cancellation token for graceful shutdown.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Ban expiry worker starting, interval: {Interval} minutes, batch size: {BatchSize}",
            _configuration.BanExpiryIntervalMinutes,
            _configuration.BanExpiryBatchSize);

        // Initial delay before first cycle to let the system stabilize
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(_configuration.BanExpiryStartupDelaySeconds), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Ban expiry worker cancelled during startup");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessExpiredBansAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during ban expiry cycle");
                try
                {
                    using var errorScope = _serviceProvider.CreateScope();
                    var messageBus = errorScope.ServiceProvider.GetRequiredService<IMessageBus>();
                    await messageBus.TryPublishErrorAsync(
                        "chat",
                        "BanExpiryWorker",
                        ex.GetType().Name,
                        ex.Message,
                        severity: ServiceErrorEventSeverity.Error);
                }
                catch (Exception pubEx)
                {
                    _logger.LogDebug(pubEx, "Failed to publish error event - continuing ban expiry loop");
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

        _logger.LogInformation("Ban expiry worker stopped");
    }

    /// <summary>
    /// Processes one expiry cycle: acquires a distributed lock, queries for expired bans,
    /// and deletes them from the ban store.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task ProcessExpiredBansAsync(CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.chat", "BanExpiryWorker.ProcessExpiredBans");

        _logger.LogDebug("Starting ban expiry cycle");

        using var scope = _serviceProvider.CreateScope();
        var stateStoreFactory = scope.ServiceProvider.GetRequiredService<IStateStoreFactory>();
        var lockProvider = scope.ServiceProvider.GetRequiredService<IDistributedLockProvider>();

        // Distributed lock prevents multiple instances from processing the same cycle concurrently.
        // Uses dedicated lock expiry (default 120s) rather than general-purpose LockExpirySeconds (15s)
        // because the batch deletion of up to BanExpiryBatchSize records may take longer than a
        // single per-room mutation.
        await using var lockResponse = await lockProvider.LockAsync(
            StateStoreDefinitions.ChatLock,
            "ban-expiry-cycle",
            Guid.NewGuid().ToString(),
            _configuration.BanExpiryLockExpirySeconds,
            cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogDebug("Could not acquire ban expiry lock, another instance is processing this cycle");
            return;
        }

        var banStore = stateStoreFactory.GetJsonQueryableStore<ChatBanModel>(StateStoreDefinitions.ChatBans);

        var now = DateTimeOffset.UtcNow;
        var conditions = new List<QueryCondition>
        {
            new() { Path = "$.ExpiresAt", Operator = QueryOperator.Exists, Value = true },
            new() { Path = "$.ExpiresAt", Operator = QueryOperator.LessThan, Value = now.ToString("O") },
        };

        var result = await banStore.JsonQueryPagedAsync(
            conditions, 0, _configuration.BanExpiryBatchSize, cancellationToken: cancellationToken);

        if (result.Items.Count == 0)
        {
            _logger.LogDebug("No expired bans found this cycle");
            return;
        }

        var deletedCount = 0;
        foreach (var item in result.Items)
        {
            try
            {
                await banStore.DeleteAsync(item.Key, cancellationToken);
                deletedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete expired ban {BanKey}", item.Key);
            }
        }

        _logger.LogInformation(
            "Ban expiry cycle complete: deleted {DeletedCount} expired bans",
            deletedCount);
    }
}
