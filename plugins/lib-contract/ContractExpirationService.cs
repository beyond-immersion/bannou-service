using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Contract;

/// <summary>
/// Background service that periodically checks active contracts for expiration conditions
/// (contract-level effectiveUntil and milestone deadlines) and payment schedule enforcement.
/// Delegates to <see cref="IContractService.GetContractInstanceStatusAsync"/> for lazy enforcement
/// of expiration/milestones, and directly checks payment due dates for event publishing.
/// </summary>
public class ContractExpirationService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ContractExpirationService> _logger;
    private readonly ContractServiceConfiguration _configuration;

    private const string STATUS_INDEX_PREFIX = "status-idx:";

    /// <summary>
    /// Interval between expiration checks, from configuration.
    /// </summary>
    private TimeSpan CheckInterval => TimeSpan.FromSeconds(_configuration.MilestoneDeadlineCheckIntervalSeconds);

    /// <summary>
    /// Startup delay before first check, from configuration.
    /// </summary>
    private TimeSpan StartupDelay => TimeSpan.FromSeconds(_configuration.MilestoneDeadlineStartupDelaySeconds);

    /// <summary>
    /// Initializes a new instance of the <see cref="ContractExpirationService"/> class.
    /// </summary>
    public ContractExpirationService(
        IServiceProvider serviceProvider,
        ILogger<ContractExpirationService> logger,
        ContractServiceConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Contract expiration service starting, check interval: {Interval}",
            CheckInterval);

        // Wait before first check to allow other services to start
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Contract expiration service cancelled during startup");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckActiveContractsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Expected during shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during contract expiration check");
                try
                {
                    using var errorScope = _serviceProvider.CreateScope();
                    var messageBus = errorScope.ServiceProvider.GetRequiredService<IMessageBus>();
                    await messageBus.TryPublishErrorAsync(
                        "contract",
                        "ExpirationCheck",
                        ex.GetType().Name,
                        ex.Message,
                        severity: ServiceErrorEventSeverity.Error);
                }
                catch (Exception pubEx)
                {
                    // Don't let error publishing failures affect the loop
                    _logger.LogDebug(pubEx, "Failed to publish error event - continuing expiration loop");
                }
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Contract expiration service stopped");
    }

    /// <summary>
    /// Checks active contracts for expirations, milestones, and payment schedules,
    /// and pending contracts for effectiveFrom activation.
    /// </summary>
    private async Task CheckActiveContractsAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Checking contracts for expirations, activations, and payment schedules");

        using var scope = _serviceProvider.CreateScope();
        var telemetryProvider = scope.ServiceProvider.GetRequiredService<ITelemetryProvider>();
        using var activity = telemetryProvider.StartActivity(
            "bannou.contract", "ContractExpirationService.CheckActiveContractsAsync");

        var stateStoreFactory = scope.ServiceProvider.GetRequiredService<IStateStoreFactory>();
        var contractService = scope.ServiceProvider.GetRequiredService<IContractService>();

        var indexStore = stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.Contract);
        var now = DateTimeOffset.UtcNow;

        // Cast to concrete type for access to payment schedule methods
        var contractServiceImpl = contractService as ContractService;

        // Check pending contracts for effectiveFrom activation
        var pendingContractIds = await indexStore.GetAsync($"{STATUS_INDEX_PREFIX}pending", cancellationToken);
        var activatedCount = 0;

        if (pendingContractIds?.Count > 0)
        {
            _logger.LogDebug("Checking {Count} pending contracts for activation", pendingContractIds.Count);

            foreach (var contractIdStr in pendingContractIds)
            {
                if (!Guid.TryParse(contractIdStr, out var contractId))
                {
                    continue;
                }

                try
                {
                    // GetContractInstanceStatusAsync performs lazy Pending→Active enforcement
                    var (status, _) = await contractService.GetContractInstanceStatusAsync(
                        new GetContractInstanceStatusRequest { ContractId = contractId },
                        cancellationToken);

                    if (status == StatusCodes.OK)
                    {
                        activatedCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to check pending contract {ContractId} for activation", contractId);
                }
            }
        }

        // Check active contracts for expirations, milestones, and payment schedules
        var activeContractIds = await indexStore.GetAsync($"{STATUS_INDEX_PREFIX}active", cancellationToken);

        if (activeContractIds == null || activeContractIds.Count == 0)
        {
            if (activatedCount > 0)
            {
                _logger.LogDebug("Activated {Count} pending contracts, no active contracts to check", activatedCount);
            }
            else
            {
                _logger.LogDebug("No active or pending contracts to check");
            }
            return;
        }

        _logger.LogDebug("Checking {Count} active contracts for expirations and payments", activeContractIds.Count);

        var processedCount = 0;
        var paymentsDueCount = 0;

        foreach (var contractIdStr in activeContractIds)
        {
            if (!Guid.TryParse(contractIdStr, out var contractId))
            {
                _logger.LogWarning("Invalid contract ID in active index: {ContractId}", contractIdStr);
                continue;
            }

            try
            {
                // GetContractInstanceStatusAsync performs lazy enforcement of both
                // effectiveUntil expiration and milestone deadline checks
                var (status, _) = await contractService.GetContractInstanceStatusAsync(
                    new GetContractInstanceStatusRequest { ContractId = contractId },
                    cancellationToken);

                if (status == StatusCodes.OK)
                {
                    processedCount++;
                }

                // Check payment schedule for due payments
                if (contractServiceImpl != null)
                {
                    paymentsDueCount += await CheckPaymentScheduleAsync(
                        contractServiceImpl, stateStoreFactory, contractId, now, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check contract {ContractId} for expiration/payment", contractId);
            }
        }

        if (processedCount > 0 || paymentsDueCount > 0 || activatedCount > 0)
        {
            _logger.LogDebug(
                "Contract check complete: {Activated} activated, {Processed} active checked, {PaymentsDue} payments due",
                activatedCount, processedCount, paymentsDueCount);
        }
    }

    /// <summary>
    /// Checks if a payment is due for a specific contract and publishes the event if so.
    /// Uses optimistic concurrency to safely advance the payment schedule.
    /// </summary>
    /// <returns>The number of payment due events published (0 or 1).</returns>
    private static async Task<int> CheckPaymentScheduleAsync(
        ContractService contractService,
        IStateStoreFactory stateStoreFactory,
        Guid contractId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var store = stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract);
        var instanceKey = $"instance:{contractId}";
        var (model, etag) = await store.GetWithETagAsync(instanceKey, ct);

        if (model == null || model.NextPaymentDue == null)
        {
            return 0;
        }

        if (!contractService.CheckAndAdvancePaymentSchedule(model, now))
        {
            return 0;
        }

        // Publish the payment due event before saving (event publishing is idempotent via eventId)
        await contractService.PublishPaymentDueEventAsync(model, model.PaymentsDuePublished, ct);

        // Save with optimistic concurrency; if it fails, the next cycle will retry
        model.UpdatedAt = now;
        await store.TrySaveAsync(instanceKey, model, etag ?? string.Empty, ct);

        return 1;
    }
}
