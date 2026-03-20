using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.ServiceClients;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Resource;

/// <summary>
/// Background service that recovers transactions not properly committed or aborted.
/// Handles TTL validation, commit resume, compensation retry, and metadata retention purge.
/// </summary>
/// <remarks>
/// <para>
/// <b>FOUNDATION TENETS — Background Service Pattern:</b>
/// Follows the canonical BackgroundService polling loop pattern (T6).
/// Uses IServiceProvider.CreateScope() per cycle for scoped store access.
/// Per-item error isolation on every transaction processed (T7).
/// </para>
/// <para>
/// Four scan types per cycle:
/// 1. TTL validation — Active transactions past expiry → validate, auto-commit, or auto-abort
/// 2. Commit resume — Committing transactions with uncheckpointed provisions → resume registration
/// 3. Compensation retry — Aborting transactions with CompensationFailed provisions → retry with backoff
/// 4. Metadata retention — Committed/Aborted past retention → purge records
/// </para>
/// </remarks>
public class TransactionRecoveryWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TransactionRecoveryWorker> _logger;
    private readonly ResourceServiceConfiguration _configuration;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>
    /// Initializes the transaction recovery worker.
    /// </summary>
    public TransactionRecoveryWorker(
        IServiceProvider serviceProvider,
        ILogger<TransactionRecoveryWorker> logger,
        ResourceServiceConfiguration configuration,
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
        // 1. Startup delay with its own cancellation handler
        try
        {
            await Task.Delay(
                TimeSpan.FromSeconds(_configuration.TransactionRecoveryWorkerStartupDelaySeconds),
                stoppingToken);
        }
        catch (OperationCanceledException) { return; }

        _logger.LogInformation("{Worker} starting, interval: {Interval}s",
            nameof(TransactionRecoveryWorker), _configuration.TransactionRecoveryWorkerIntervalSeconds);

        // 2. Main loop
        while (!stoppingToken.IsCancellationRequested)
        {
            // 3. Work with double-catch cancellation filter
            try
            {
                using var activity = _telemetryProvider.StartActivity(
                    "bannou.resource", "TransactionRecoveryWorker.ProcessCycle");
                await ProcessCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break; // Graceful shutdown — NOT an error
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Worker} cycle failed", nameof(TransactionRecoveryWorker));
                await _serviceProvider.TryPublishWorkerErrorAsync(
                    "resource", "TransactionRecoveryWorker", ex, _logger, stoppingToken);
            }

            // 4. Delay with its own cancellation handler
            try
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(_configuration.TransactionRecoveryWorkerIntervalSeconds),
                    stoppingToken);
            }
            catch (OperationCanceledException) { break; }
        }

        _logger.LogInformation("{Worker} stopped", nameof(TransactionRecoveryWorker));
    }

    /// <summary>
    /// Processes one recovery cycle: all four scan types in sequence.
    /// </summary>
    private async Task ProcessCycleAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var stateStoreFactory = scope.ServiceProvider.GetRequiredService<IStateStoreFactory>();

        // Resolve stores once per cycle, pass as parameters (FOUNDATION TENETS T6)
        var transactionStore = stateStoreFactory.GetStore<ResourceTransactionModel>(
            StateStoreDefinitions.ResourceTransactions);
        var queryableTransactionStore = stateStoreFactory.GetQueryableStore<ResourceTransactionModel>(
            StateStoreDefinitions.ResourceTransactions);
        var provisionStore = stateStoreFactory.GetStore<ResourceProvisionModel>(
            StateStoreDefinitions.ResourceProvisions);
        var provisionStringStore = stateStoreFactory.GetStore<string>(
            StateStoreDefinitions.ResourceProvisions);
        var navigator = scope.ServiceProvider.GetRequiredService<IServiceNavigator>();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var now = DateTimeOffset.UtcNow;

        // Scan 1: TTL validation (Active transactions past expiry)
        await ScanTtlValidationAsync(
            queryableTransactionStore, transactionStore, provisionStore, provisionStringStore,
            navigator, messageBus, now, ct);

        // Scan 2: Resume commit (Committing transactions)
        await ScanCommitResumeAsync(
            queryableTransactionStore, transactionStore, provisionStore, provisionStringStore,
            messageBus, now, ct);

        // Scan 3: Compensation retry (Aborting transactions)
        await ScanCompensationRetryAsync(
            queryableTransactionStore, transactionStore, provisionStore, provisionStringStore,
            navigator, messageBus, now, ct);

        // Scan 4: Metadata retention purge (Committed/Aborted past retention)
        await ScanRetentionPurgeAsync(
            queryableTransactionStore, transactionStore, provisionStore, provisionStringStore,
            now, ct);
    }

    // =========================================================================
    // Scan 1: TTL Validation
    // =========================================================================

    private async Task ScanTtlValidationAsync(
        IQueryableStateStore<ResourceTransactionModel> queryStore,
        IStateStore<ResourceTransactionModel> transactionStore,
        IStateStore<ResourceProvisionModel> provisionStore,
        IStateStore<string> provisionStringStore,
        IServiceNavigator navigator,
        IMessageBus messageBus,
        DateTimeOffset now,
        CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.resource", "TransactionRecoveryWorker.ScanTtlValidation");

        var activeTransactions = await queryStore.QueryAsync(
            t => t.Status == TransactionStatus.Active, ct);

        foreach (var transaction in activeTransactions)
        {
            // Per-item error isolation (IMPLEMENTATION TENETS T7)
            try
            {
                var expiresAt = transaction.CreatedAt.AddSeconds(transaction.TtlSeconds);
                if (now < expiresAt)
                    continue; // Not yet expired

                // Count current provisions
                var provisions = await GetProvisionListAsync(
                    provisionStringStore, transaction.TransactionId, ct);

                // If expected count not met, orchestrator crashed before finishing provisioning
                if (transaction.ExpectedProvisionCount.HasValue &&
                    provisions.Count < transaction.ExpectedProvisionCount.Value)
                {
                    _logger.LogInformation(
                        "Transaction {TransactionId} TTL expired with {Actual}/{Expected} provisions — auto-aborting",
                        transaction.TransactionId, provisions.Count, transaction.ExpectedProvisionCount.Value);
                    await AutoAbortAsync(transaction, transactionStore, provisionStore,
                        provisionStringStore, navigator, messageBus,
                        "TTL expired, provision count not met", ct);
                    continue;
                }

                // Check validation exhaustion
                if (transaction.ValidationAttempts >= _configuration.TransactionValidationMaxRetries)
                {
                    await messageBus.PublishResourceTransactionValidationExhaustedAsync(
                        new ResourceTransactionValidationExhaustedEvent
                        {
                            EventId = Guid.NewGuid(),
                            Timestamp = now,
                            TransactionId = transaction.TransactionId,
                            OwnerService = transaction.OwnerService,
                            ParentResourceType = transaction.ParentResourceType,
                            ParentResourceId = transaction.ParentResourceId,
                            ValidationAttempts = transaction.ValidationAttempts,
                        }, ct);
                    continue; // Remain Active for admin
                }

                // No validation check provided — conservative auto-abort
                if (string.IsNullOrEmpty(transaction.CompletionValidation))
                {
                    _logger.LogInformation(
                        "Transaction {TransactionId} TTL expired with no completion validation — auto-aborting",
                        transaction.TransactionId);
                    await AutoAbortAsync(transaction, transactionStore, provisionStore,
                        provisionStringStore, navigator, messageBus,
                        "TTL expired, no completion validation", ct);
                    continue;
                }

                // Execute completion validation
                var validationApi = BannouJson.Deserialize<PreboundApi>(transaction.CompletionValidation);
                if (validationApi == null)
                {
                    await AutoAbortAsync(transaction, transactionStore, provisionStore,
                        provisionStringStore, navigator, messageBus,
                        "TTL expired, invalid completion validation", ct);
                    continue;
                }

                var context = new Dictionary<string, object?>
                {
                    ["parentResourceId"] = transaction.ParentResourceId.ToString()
                };
                var result = await navigator.ExecutePreboundApiAsync(validationApi, context, ct);
                var transformed = ResponseTransformer.Transform(
                    result.Result.StatusCode, result.Result.ResponseBody,
                    validationApi.ResponseTransformation);

                if (transformed.Outcome == TransformationOutcome.TransientFailure)
                {
                    // Unreachable — increment attempts and retry next cycle
                    transaction.ValidationAttempts++;
                    transaction.UpdatedAt = now;
                    await transactionStore.SaveAsync(
                        ResourceService.BuildTransactionKey(transaction.TransactionId),
                        transaction, cancellationToken: ct);
                    _logger.LogDebug(
                        "Transaction {TransactionId} TTL validation transient failure (attempt {Attempts})",
                        transaction.TransactionId, transaction.ValidationAttempts);
                    continue;
                }

                if (transformed.IsSuccess)
                {
                    // Entity exists — auto-commit
                    _logger.LogInformation(
                        "Transaction {TransactionId} TTL validation succeeded — auto-committing",
                        transaction.TransactionId);
                    await AutoCommitAsync(transaction, transactionStore, provisionStore,
                        provisionStringStore, messageBus, now, ct);
                }
                else
                {
                    // Entity does not exist (4xx) — auto-abort
                    _logger.LogInformation(
                        "Transaction {TransactionId} TTL validation returned {Status} — auto-aborting",
                        transaction.TransactionId, transformed.StatusCode);
                    await AutoAbortAsync(transaction, transactionStore, provisionStore,
                        provisionStringStore, navigator, messageBus,
                        $"TTL validation: entity not found (status {transformed.StatusCode})", ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "TTL validation failed for transaction {TransactionId}, will retry next cycle",
                    transaction.TransactionId);
            }
        }
    }

    // =========================================================================
    // Scan 2: Commit Resume
    // =========================================================================

    private async Task ScanCommitResumeAsync(
        IQueryableStateStore<ResourceTransactionModel> queryStore,
        IStateStore<ResourceTransactionModel> transactionStore,
        IStateStore<ResourceProvisionModel> provisionStore,
        IStateStore<string> provisionStringStore,
        IMessageBus messageBus,
        DateTimeOffset now,
        CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.resource", "TransactionRecoveryWorker.ScanCommitResume");

        var committingTransactions = await queryStore.QueryAsync(
            t => t.Status == TransactionStatus.Committing, ct);

        foreach (var transaction in committingTransactions)
        {
            try
            {
                // Check if commit resume has exceeded max retries based on time in Committing state
                // Each worker cycle is one implicit retry attempt
                var secondsInCommitting = (now - transaction.UpdatedAt).TotalSeconds;
                var maxCommitSeconds = _configuration.TransactionCommitMaxRetries
                    * _configuration.TransactionRecoveryWorkerIntervalSeconds;

                if (secondsInCommitting > maxCommitSeconds)
                {
                    _logger.LogWarning(
                        "Transaction {TransactionId} commit resume exhausted ({Seconds}s in Committing, max {Max}s) — falling back to abort",
                        transaction.TransactionId, (int)secondsInCommitting, maxCommitSeconds);

                    var provisions = await GetOrderedProvisionsAsync(
                        provisionStore, provisionStringStore, transaction.TransactionId, ct);
                    var registeredCount = provisions.Count(p => p.Status == ProvisionStatus.ReferenceRegistered);
                    var failedCount = provisions.Count(p => p.Status == ProvisionStatus.Provisioned);

                    await messageBus.PublishResourceTransactionCommitFailedAsync(
                        new ResourceTransactionCommitFailedEvent
                        {
                            EventId = Guid.NewGuid(),
                            Timestamp = now,
                            TransactionId = transaction.TransactionId,
                            OwnerService = transaction.OwnerService,
                            ParentResourceType = transaction.ParentResourceType,
                            ParentResourceId = transaction.ParentResourceId,
                            RegisteredCount = registeredCount,
                            FailedCount = failedCount,
                        }, ct);

                    // Transition to Aborting — compensation retry scan will handle
                    transaction.Status = TransactionStatus.Aborting;
                    transaction.AbortReason = "Commit resume retries exhausted";
                    transaction.UpdatedAt = now;
                    await transactionStore.SaveAsync(
                        ResourceService.BuildTransactionKey(transaction.TransactionId),
                        transaction, cancellationToken: ct);
                    continue;
                }

                await AutoCommitAsync(transaction, transactionStore, provisionStore,
                    provisionStringStore, messageBus, now, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Commit resume failed for transaction {TransactionId}, will retry next cycle",
                    transaction.TransactionId);
            }
        }
    }

    // =========================================================================
    // Scan 3: Compensation Retry
    // =========================================================================

    private async Task ScanCompensationRetryAsync(
        IQueryableStateStore<ResourceTransactionModel> queryStore,
        IStateStore<ResourceTransactionModel> transactionStore,
        IStateStore<ResourceProvisionModel> provisionStore,
        IStateStore<string> provisionStringStore,
        IServiceNavigator navigator,
        IMessageBus messageBus,
        DateTimeOffset now,
        CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.resource", "TransactionRecoveryWorker.ScanCompensationRetry");

        var abortingTransactions = await queryStore.QueryAsync(
            t => t.Status == TransactionStatus.Aborting, ct);

        foreach (var transaction in abortingTransactions)
        {
            try
            {
                var provisions = await GetOrderedProvisionsAsync(
                    provisionStore, provisionStringStore, transaction.TransactionId, ct);
                var hasCompensationFailed = false;

                foreach (var provision in provisions.OrderByDescending(p => p.SequenceNumber))
                {
                    if (provision.Status != ProvisionStatus.CompensationFailed)
                        continue;

                    if (provision.CompensationAttempts >= _configuration.TransactionCompensationMaxRetries)
                    {
                        hasCompensationFailed = true;
                        continue; // Exhausted — leave as CompensationFailed
                    }

                    // Exponential backoff: skip if not enough time has passed since last attempt
                    var backoffSeconds = _configuration.TransactionCompensationBackoffBaseSeconds
                        * Math.Pow(2, provision.CompensationAttempts - 1);
                    var earliestRetry = transaction.UpdatedAt.AddSeconds(backoffSeconds);
                    if (now < earliestRetry)
                        continue; // Too soon — wait for next cycle

                    try
                    {
                        var compensationApi = BannouJson.Deserialize<PreboundApi>(provision.Compensation);
                        if (compensationApi == null)
                        {
                            provision.CompensationAttempts++;
                            provision.LastCompensationError = "Invalid compensation PreboundApi definition";
                            await provisionStore.SaveAsync(
                                ResourceService.BuildProvisionKey(provision.ProvisionId),
                                provision, cancellationToken: ct);
                            continue;
                        }

                        var context = new Dictionary<string, object?>
                        {
                            ["provisionResourceId"] = provision.ResourceId.ToString()
                        };
                        var result = await navigator.ExecutePreboundApiAsync(compensationApi, context, ct);
                        var transformed = ResponseTransformer.Transform(
                            result.Result.StatusCode, result.Result.ResponseBody,
                            compensationApi.ResponseTransformation);

                        if (transformed.IsSuccess || result.Result.StatusCode == 404)
                        {
                            provision.Status = ProvisionStatus.Compensated;
                            provision.CompensatedAt = now;
                        }
                        else
                        {
                            provision.CompensationAttempts++;
                            provision.LastCompensationError =
                                result.Result.ErrorMessage ?? $"Status {result.Result.StatusCode}";
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "Compensation retry failed for provision {ProvisionId}",
                            provision.ProvisionId);
                        provision.CompensationAttempts++;
                        provision.LastCompensationError = ex.Message;
                    }

                    await provisionStore.SaveAsync(
                        ResourceService.BuildProvisionKey(provision.ProvisionId),
                        provision, cancellationToken: ct);
                }

                // Check if all provisions are now resolved
                var remainingFailed = provisions.Count(p => p.Status == ProvisionStatus.CompensationFailed);
                if (remainingFailed == 0)
                {
                    transaction.Status = TransactionStatus.Aborted;
                    transaction.UpdatedAt = now;
                    await transactionStore.SaveAsync(
                        ResourceService.BuildTransactionKey(transaction.TransactionId),
                        transaction, cancellationToken: ct);

                    var compensatedCount = provisions.Count(p => p.Status == ProvisionStatus.Compensated);
                    await messageBus.PublishResourceTransactionAbortedAsync(
                        new ResourceTransactionAbortedEvent
                        {
                            EventId = Guid.NewGuid(),
                            Timestamp = now,
                            TransactionId = transaction.TransactionId,
                            OwnerService = transaction.OwnerService,
                            ParentResourceType = transaction.ParentResourceType,
                            ParentResourceId = transaction.ParentResourceId,
                            CompensatedCount = compensatedCount,
                            FailedCount = 0,
                        }, ct);

                    _logger.LogInformation(
                        "Transaction {TransactionId} fully aborted ({Compensated} compensated)",
                        transaction.TransactionId, compensatedCount);
                }
                else if (hasCompensationFailed)
                {
                    // Some provisions exhausted retries — publish error event
                    var exhaustedCount = provisions.Count(p =>
                        p.Status == ProvisionStatus.CompensationFailed &&
                        p.CompensationAttempts >= _configuration.TransactionCompensationMaxRetries);

                    if (exhaustedCount > 0)
                    {
                        // Finalize to Aborted with error
                        transaction.Status = TransactionStatus.Aborted;
                        transaction.UpdatedAt = now;
                        await transactionStore.SaveAsync(
                            ResourceService.BuildTransactionKey(transaction.TransactionId),
                            transaction, cancellationToken: ct);

                        await messageBus.PublishResourceTransactionCompensationExhaustedAsync(
                            new ResourceTransactionCompensationExhaustedEvent
                            {
                                EventId = Guid.NewGuid(),
                                Timestamp = now,
                                TransactionId = transaction.TransactionId,
                                OwnerService = transaction.OwnerService,
                                ParentResourceType = transaction.ParentResourceType,
                                ParentResourceId = transaction.ParentResourceId,
                                FailedProvisionCount = exhaustedCount,
                            }, ct);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Compensation retry scan failed for transaction {TransactionId}",
                    transaction.TransactionId);
            }
        }
    }

    // =========================================================================
    // Scan 4: Metadata Retention Purge
    // =========================================================================

    private async Task ScanRetentionPurgeAsync(
        IQueryableStateStore<ResourceTransactionModel> queryStore,
        IStateStore<ResourceTransactionModel> transactionStore,
        IStateStore<ResourceProvisionModel> provisionStore,
        IStateStore<string> provisionStringStore,
        DateTimeOffset now,
        CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.resource", "TransactionRecoveryWorker.ScanRetentionPurge");

        var retentionCutoff = now.AddDays(-_configuration.TransactionRetentionDays);

        // Query terminal transactions past retention
        var terminalTransactions = await queryStore.QueryAsync(
            t => (t.Status == TransactionStatus.Committed || t.Status == TransactionStatus.Aborted)
                && t.UpdatedAt < retentionCutoff, ct);

        foreach (var transaction in terminalTransactions)
        {
            try
            {
                // Purge all provisions for this transaction
                var provisionIds = await GetProvisionListAsync(
                    provisionStringStore, transaction.TransactionId, ct);

                foreach (var provisionId in provisionIds)
                {
                    await provisionStore.DeleteAsync(
                        ResourceService.BuildProvisionKey(provisionId), ct);
                }

                // Delete provision index
                await provisionStringStore.DeleteAsync(
                    ResourceService.BuildProvisionTxIndexKey(transaction.TransactionId), ct);

                // Delete transaction record
                await transactionStore.DeleteAsync(
                    ResourceService.BuildTransactionKey(transaction.TransactionId), ct);

                _logger.LogDebug(
                    "Purged transaction {TransactionId} ({Status}, {ProvisionCount} provisions)",
                    transaction.TransactionId, transaction.Status, provisionIds.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Retention purge failed for transaction {TransactionId}",
                    transaction.TransactionId);
            }
        }
    }

    // =========================================================================
    // Shared Helpers
    // =========================================================================

    /// <summary>
    /// Auto-commits a transaction by resuming reference registration from the last checkpoint.
    /// Used by both TTL validation (auto-commit) and commit resume scans.
    /// </summary>
    private async Task AutoCommitAsync(
        ResourceTransactionModel transaction,
        IStateStore<ResourceTransactionModel> transactionStore,
        IStateStore<ResourceProvisionModel> provisionStore,
        IStateStore<string> provisionStringStore,
        IMessageBus messageBus,
        DateTimeOffset now,
        CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.resource", "TransactionRecoveryWorker.AutoCommit");

        // Ensure in Committing state
        if (transaction.Status == TransactionStatus.Active)
        {
            transaction.Status = TransactionStatus.Committing;
            transaction.UpdatedAt = now;
            await transactionStore.SaveAsync(
                ResourceService.BuildTransactionKey(transaction.TransactionId),
                transaction, cancellationToken: ct);
        }

        // Resume reference registration from last checkpoint
        var provisions = await GetOrderedProvisionsAsync(
            provisionStore, provisionStringStore, transaction.TransactionId, ct);

        // Use a scope to get a ResourceService instance for reference registration
        using var serviceScope = _serviceProvider.CreateScope();
        var resourceService = serviceScope.ServiceProvider.GetRequiredService<IResourceService>();

        var referencesRegistered = 0;
        foreach (var provision in provisions)
        {
            if (provision.Status != ProvisionStatus.Provisioned)
                continue;

            try
            {
                var (refStatus, _) = await resourceService.RegisterReferenceAsync(
                    new RegisterReferenceRequest
                    {
                        ResourceType = transaction.ParentResourceType,
                        ResourceId = transaction.ParentResourceId,
                        SourceType = provision.ResourceType,
                        SourceId = provision.ResourceId.ToString()
                    }, ct);

                if (refStatus == StatusCodes.OK)
                {
                    provision.Status = ProvisionStatus.ReferenceRegistered;
                    await provisionStore.SaveAsync(
                        ResourceService.BuildProvisionKey(provision.ProvisionId),
                        provision, cancellationToken: ct);
                    referencesRegistered++;
                }
                else
                {
                    _logger.LogWarning(
                        "Worker: reference registration returned {Status} for provision {ProvisionId}",
                        refStatus, provision.ProvisionId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Worker: failed to register reference for provision {ProvisionId}",
                    provision.ProvisionId);
                // Don't mark as failed — leave as Provisioned for next cycle retry
            }
        }

        // Check if all provisions are now registered
        var unregistered = provisions.Count(p => p.Status == ProvisionStatus.Provisioned);
        if (unregistered == 0)
        {
            // Capture original status BEFORE transitioning — determines which event to publish
            var wasAutoCommit = transaction.Status == TransactionStatus.Active;

            transaction.Status = TransactionStatus.Committed;
            transaction.UpdatedAt = now;
            await transactionStore.SaveAsync(
                ResourceService.BuildTransactionKey(transaction.TransactionId),
                transaction, cancellationToken: ct);

            var isAutoCommit = wasAutoCommit;
            if (isAutoCommit)
            {
                await messageBus.PublishResourceTransactionAutoCommittedAsync(
                    new ResourceTransactionAutoCommittedEvent
                    {
                        EventId = Guid.NewGuid(),
                        Timestamp = now,
                        TransactionId = transaction.TransactionId,
                        OwnerService = transaction.OwnerService,
                        ParentResourceType = transaction.ParentResourceType,
                        ParentResourceId = transaction.ParentResourceId,
                    }, ct);
            }
            else
            {
                await messageBus.PublishResourceTransactionCommittedAsync(
                    new ResourceTransactionCommittedEvent
                    {
                        EventId = Guid.NewGuid(),
                        Timestamp = now,
                        TransactionId = transaction.TransactionId,
                        OwnerService = transaction.OwnerService,
                        ParentResourceType = transaction.ParentResourceType,
                        ParentResourceId = transaction.ParentResourceId,
                        ProvisionCount = referencesRegistered,
                    }, ct);
            }

            _logger.LogInformation(
                "Transaction {TransactionId} committed by worker ({Registered} references registered)",
                transaction.TransactionId, referencesRegistered);
        }
    }

    /// <summary>
    /// Auto-aborts a transaction, compensating all provisions in reverse order.
    /// </summary>
    private async Task AutoAbortAsync(
        ResourceTransactionModel transaction,
        IStateStore<ResourceTransactionModel> transactionStore,
        IStateStore<ResourceProvisionModel> provisionStore,
        IStateStore<string> provisionStringStore,
        IServiceNavigator navigator,
        IMessageBus messageBus,
        string reason,
        CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.resource", "TransactionRecoveryWorker.AutoAbort");

        var now = DateTimeOffset.UtcNow;

        // Transition to Aborting
        transaction.Status = TransactionStatus.Aborting;
        transaction.AbortReason = reason;
        transaction.UpdatedAt = now;
        await transactionStore.SaveAsync(
            ResourceService.BuildTransactionKey(transaction.TransactionId),
            transaction, cancellationToken: ct);

        // Compensate in reverse order
        var provisions = await GetOrderedProvisionsAsync(
            provisionStore, provisionStringStore, transaction.TransactionId, ct);

        var compensatedCount = 0;
        var failedCount = 0;

        foreach (var provision in provisions.OrderByDescending(p => p.SequenceNumber))
        {
            if (provision.Status == ProvisionStatus.Compensated)
                continue;

            if (provision.Status == ProvisionStatus.Pending)
            {
                provision.Status = ProvisionStatus.Compensated;
                provision.CompensatedAt = now;
                await provisionStore.SaveAsync(
                    ResourceService.BuildProvisionKey(provision.ProvisionId),
                    provision, cancellationToken: ct);
                compensatedCount++;
                continue;
            }

            try
            {
                var compensationApi = BannouJson.Deserialize<PreboundApi>(provision.Compensation);
                if (compensationApi == null)
                {
                    provision.Status = ProvisionStatus.CompensationFailed;
                    provision.CompensationAttempts++;
                    provision.LastCompensationError = "Invalid compensation PreboundApi definition";
                    failedCount++;
                    await provisionStore.SaveAsync(
                        ResourceService.BuildProvisionKey(provision.ProvisionId),
                        provision, cancellationToken: ct);
                    continue;
                }

                var context = new Dictionary<string, object?>
                {
                    ["provisionResourceId"] = provision.ResourceId.ToString()
                };
                var result = await navigator.ExecutePreboundApiAsync(compensationApi, context, ct);
                var transformed = ResponseTransformer.Transform(
                    result.Result.StatusCode, result.Result.ResponseBody,
                    compensationApi.ResponseTransformation);

                if (transformed.IsSuccess || result.Result.StatusCode == 404)
                {
                    provision.Status = ProvisionStatus.Compensated;
                    provision.CompensatedAt = now;
                    compensatedCount++;
                }
                else
                {
                    provision.Status = ProvisionStatus.CompensationFailed;
                    provision.CompensationAttempts++;
                    provision.LastCompensationError =
                        result.Result.ErrorMessage ?? $"Status {result.Result.StatusCode}";
                    failedCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Worker: compensation failed for provision {ProvisionId}",
                    provision.ProvisionId);
                provision.Status = ProvisionStatus.CompensationFailed;
                provision.CompensationAttempts++;
                provision.LastCompensationError = ex.Message;
                failedCount++;
            }

            await provisionStore.SaveAsync(
                ResourceService.BuildProvisionKey(provision.ProvisionId),
                provision, cancellationToken: ct);
        }

        if (failedCount == 0)
        {
            transaction.Status = TransactionStatus.Aborted;
            transaction.UpdatedAt = now;
            await transactionStore.SaveAsync(
                ResourceService.BuildTransactionKey(transaction.TransactionId),
                transaction, cancellationToken: ct);

            await messageBus.PublishResourceTransactionAutoAbortedAsync(
                new ResourceTransactionAutoAbortedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = now,
                    TransactionId = transaction.TransactionId,
                    OwnerService = transaction.OwnerService,
                    ParentResourceType = transaction.ParentResourceType,
                    ParentResourceId = transaction.ParentResourceId,
                }, ct);
        }
        // else: remain Aborting — compensation retry scan will handle on next cycle

        _logger.LogInformation(
            "Transaction {TransactionId} auto-abort: {Compensated} compensated, {Failed} failed",
            transaction.TransactionId, compensatedCount, failedCount);
    }

    /// <summary>
    /// Gets the list of provision IDs for a transaction from the string index.
    /// </summary>
    private static async Task<List<Guid>> GetProvisionListAsync(
        IStateStore<string> provisionStringStore, Guid transactionId, CancellationToken ct)
    {
        var indexJson = await provisionStringStore.GetAsync(
            ResourceService.BuildProvisionTxIndexKey(transactionId), ct);

        if (string.IsNullOrEmpty(indexJson))
            return new List<Guid>();

        var idStrings = BannouJson.Deserialize<List<string>>(indexJson);
        if (idStrings == null)
            return new List<Guid>();

        return idStrings
            .Select(s => Guid.TryParse(s, out var id) ? id : (Guid?)null)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToList();
    }

    /// <summary>
    /// Gets all provision models for a transaction, ordered by sequence number.
    /// </summary>
    private static async Task<List<ResourceProvisionModel>> GetOrderedProvisionsAsync(
        IStateStore<ResourceProvisionModel> provisionStore,
        IStateStore<string> provisionStringStore,
        Guid transactionId,
        CancellationToken ct)
    {
        var provisionIds = await GetProvisionListAsync(provisionStringStore, transactionId, ct);
        var provisions = new List<ResourceProvisionModel>();

        foreach (var provisionId in provisionIds)
        {
            var provision = await provisionStore.GetAsync(
                ResourceService.BuildProvisionKey(provisionId), ct);
            if (provision != null)
                provisions.Add(provision);
        }

        return provisions.OrderBy(p => p.SequenceNumber).ToList();
    }
}
