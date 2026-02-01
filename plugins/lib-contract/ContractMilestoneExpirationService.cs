using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Contract;

/// <summary>
/// Background service that periodically checks for overdue milestones
/// and enforces deadlines for active contracts.
/// </summary>
public class ContractMilestoneExpirationService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ContractMilestoneExpirationService> _logger;
    private readonly ContractServiceConfiguration _configuration;

    private const string STATUS_INDEX_PREFIX = "status-idx:";
    private const string INSTANCE_PREFIX = "instance:";

    /// <summary>
    /// Interval between milestone deadline checks, from configuration.
    /// </summary>
    private TimeSpan CheckInterval => TimeSpan.FromSeconds(_configuration.MilestoneDeadlineCheckIntervalSeconds);

    /// <summary>
    /// Startup delay before first check, from configuration.
    /// </summary>
    private TimeSpan StartupDelay => TimeSpan.FromSeconds(_configuration.MilestoneDeadlineStartupDelaySeconds);

    /// <summary>
    /// Initializes a new instance of the ContractMilestoneExpirationService.
    /// </summary>
    public ContractMilestoneExpirationService(
        IServiceProvider serviceProvider,
        ILogger<ContractMilestoneExpirationService> logger,
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
            "Contract milestone expiration service starting, check interval: {Interval}",
            CheckInterval);

        // Wait a bit before first check to allow other services to start
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Contract milestone expiration service cancelled during startup");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckOverdueMilestonesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Expected during shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during milestone expiration check");
                try
                {
                    using var errorScope = _serviceProvider.CreateScope();
                    var messageBus = errorScope.ServiceProvider.GetRequiredService<IMessageBus>();
                    await messageBus.TryPublishErrorAsync(
                        "contract",
                        "MilestoneExpirationCheck",
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

        _logger.LogInformation("Contract milestone expiration service stopped");
    }

    /// <summary>
    /// Checks for overdue milestones in active contracts and enforces deadlines.
    /// </summary>
    private async Task CheckOverdueMilestonesAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Checking for overdue milestones");

        using var scope = _serviceProvider.CreateScope();
        var stateStoreFactory = scope.ServiceProvider.GetRequiredService<IStateStoreFactory>();
        var lockProvider = scope.ServiceProvider.GetRequiredService<IDistributedLockProvider>();
        var contractService = scope.ServiceProvider.GetRequiredService<IContractService>();

        // Get active contracts from status index
        var indexStore = stateStoreFactory.GetStore<List<string>>(StateStoreDefinitions.Contract);
        var activeContractIds = await indexStore.GetAsync($"{STATUS_INDEX_PREFIX}active", cancellationToken);

        if (activeContractIds == null || activeContractIds.Count == 0)
        {
            _logger.LogDebug("No active contracts to check");
            return;
        }

        _logger.LogDebug("Checking {Count} active contracts for overdue milestones", activeContractIds.Count);

        var processedCount = 0;
        var contractStore = stateStoreFactory.GetStore<ContractInstanceModel>(StateStoreDefinitions.Contract);

        foreach (var contractIdStr in activeContractIds)
        {
            if (!Guid.TryParse(contractIdStr, out var contractId))
            {
                _logger.LogWarning("Invalid contract ID in active index: {ContractId}", contractIdStr);
                continue;
            }

            try
            {
                // Use distributed lock to prevent concurrent processing
                var lockKey = $"milestone-check:{contractId}";
                var lockOwner = $"milestone-service-{Environment.MachineName}-{Environment.ProcessId}";
                await using var lockHandle = await lockProvider.LockAsync(
                    StateStoreDefinitions.Contract,
                    lockKey,
                    lockOwner,
                    30, // 30 seconds lock expiry
                    cancellationToken);

                if (!lockHandle.Success)
                {
                    // Another instance is processing this contract
                    _logger.LogDebug("Could not acquire lock for contract {ContractId}, skipping", contractId);
                    continue;
                }

                var instanceKey = $"{INSTANCE_PREFIX}{contractId}";
                var (contract, etag) = await contractStore.GetWithETagAsync(instanceKey, cancellationToken);

                if (contract == null || contract.Status != ContractStatus.Active)
                {
                    continue;
                }

                if (contract.Milestones == null || contract.Milestones.Count == 0)
                {
                    continue;
                }

                var anyProcessed = false;
                var now = DateTimeOffset.UtcNow;

                foreach (var milestone in contract.Milestones)
                {
                    if (milestone.Status != MilestoneStatus.Active || !milestone.ActivatedAt.HasValue)
                        continue;

                    if (string.IsNullOrEmpty(milestone.Deadline))
                        continue;

                    var duration = ParseIsoDuration(milestone.Deadline);
                    if (!duration.HasValue)
                        continue;

                    var absoluteDeadline = milestone.ActivatedAt.Value.Add(duration.Value);
                    if (absoluteDeadline >= now)
                        continue; // Not overdue

                    // Process overdue milestone by invoking the GetMilestone endpoint
                    // which triggers lazy enforcement
                    try
                    {
                        await contractService.GetMilestoneAsync(
                            new GetMilestoneRequest
                            {
                                ContractId = contractId,
                                MilestoneCode = milestone.Code
                            },
                            cancellationToken);

                        anyProcessed = true;
                        _logger.LogInformation(
                            "Processed overdue milestone {MilestoneCode} for contract {ContractId}",
                            milestone.Code, contractId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "Failed to process overdue milestone {MilestoneCode} for contract {ContractId}",
                            milestone.Code, contractId);
                    }
                }

                if (anyProcessed)
                {
                    processedCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check milestones for contract {ContractId}", contractId);
            }
        }

        if (processedCount > 0)
        {
            _logger.LogInformation("Processed overdue milestones in {Count} contracts", processedCount);
        }
        else
        {
            _logger.LogDebug("No overdue milestones processed this cycle");
        }
    }

    /// <summary>
    /// Parses an ISO 8601 duration string (e.g., "P10D", "PT2H", "P1DT12H") into a TimeSpan.
    /// </summary>
    private static TimeSpan? ParseIsoDuration(string? duration)
    {
        if (string.IsNullOrEmpty(duration)) return null;
        try
        {
            return System.Xml.XmlConvert.ToTimeSpan(duration);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
