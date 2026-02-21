using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Escrow;

/// <summary>
/// Background service that checks for expired confirmation deadlines
/// and applies the configured timeout behavior (auto_confirm, dispute, or refund).
/// </summary>
public class EscrowConfirmationTimeoutService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EscrowConfirmationTimeoutService> _logger;
    private readonly EscrowServiceConfiguration _configuration;

    /// <summary>
    /// Interval between timeout checks, from configuration.
    /// </summary>
    private TimeSpan CheckInterval => TimeSpan.FromSeconds(_configuration.ConfirmationTimeoutCheckIntervalSeconds);

    /// <summary>
    /// Maximum escrows to process per cycle, from configuration.
    /// </summary>
    private int BatchSize => _configuration.ConfirmationTimeoutBatchSize;

    /// <summary>
    /// Startup delay before first check to allow other services to start.
    /// </summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(15);

    public EscrowConfirmationTimeoutService(
        IServiceProvider serviceProvider,
        ILogger<EscrowConfirmationTimeoutService> logger,
        EscrowServiceConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Escrow confirmation timeout service starting, check interval: {Interval}s",
            _configuration.ConfirmationTimeoutCheckIntervalSeconds);

        // Wait a bit before first check to allow other services to start
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Escrow confirmation timeout service cancelled during startup");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndProcessExpiredConfirmationsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Expected during shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during confirmation timeout check");
                try
                {
                    using var errorScope = _serviceProvider.CreateScope();
                    var messageBus = errorScope.ServiceProvider.GetRequiredService<IMessageBus>();
                    await messageBus.TryPublishErrorAsync(
                        "escrow",
                        "ConfirmationTimeoutCheck",
                        ex.GetType().Name,
                        ex.Message,
                        severity: ServiceErrorEventSeverity.Error);
                }
                catch (Exception pubEx)
                {
                    // Don't let error publishing failures affect the loop
                    _logger.LogDebug(pubEx, "Failed to publish error event - continuing timeout loop");
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

        _logger.LogInformation("Escrow confirmation timeout service stopped");
    }

    /// <summary>
    /// Checks for escrows with expired confirmation deadlines and applies the configured behavior.
    /// </summary>
    private async Task CheckAndProcessExpiredConfirmationsAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Checking for expired confirmation deadlines");

        using var scope = _serviceProvider.CreateScope();
        var stateStoreFactory = scope.ServiceProvider.GetRequiredService<IStateStoreFactory>();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var agreementStore = stateStoreFactory.GetQueryableStore<EscrowAgreementModel>(StateStoreDefinitions.EscrowAgreements);
        var statusIndexStore = stateStoreFactory.GetStore<StatusIndexEntry>(StateStoreDefinitions.EscrowStatusIndex);

        var now = DateTimeOffset.UtcNow;
        var processed = 0;

        // Check both Releasing and Refunding statuses
        var statusesToCheck = new[] { EscrowStatus.Releasing, EscrowStatus.Refunding };

        foreach (var status in statusesToCheck)
        {
            if (processed >= BatchSize) break;

            // Query escrows in this status with expired confirmation deadlines
            var agreements = await agreementStore.QueryAsync(
                a => a.Status == status && a.ConfirmationDeadline != null && a.ConfirmationDeadline <= now,
                cancellationToken);

            if (agreements.Count == 0) continue;

            // Apply batch limit
            var toProcess = agreements.Take(BatchSize - processed);

            foreach (var agreement in toProcess)
            {
                if (processed >= BatchSize) break;

                var wasProcessed = await ProcessExpiredEscrowAsync(
                    agreementStore,
                    statusIndexStore,
                    messageBus,
                    agreement,
                    now,
                    cancellationToken);

                if (wasProcessed) processed++;
            }
        }

        if (processed > 0)
        {
            _logger.LogInformation("Processed {Count} escrows with expired confirmation deadlines", processed);
        }
        else
        {
            _logger.LogDebug("No expired confirmation deadlines this cycle");
        }
    }

    /// <summary>
    /// Processes a single escrow with an expired confirmation deadline.
    /// </summary>
    private async Task<bool> ProcessExpiredEscrowAsync(
        IQueryableStateStore<EscrowAgreementModel> agreementStore,
        IStateStore<StatusIndexEntry> statusIndexStore,
        IMessageBus messageBus,
        EscrowAgreementModel agreement,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var agreementKey = $"agreement:{agreement.EscrowId}";
        var previousStatus = agreement.Status;

        // Get the full agreement with ETag for optimistic concurrency
        var (currentAgreement, etag) = await agreementStore.GetWithETagAsync(agreementKey, cancellationToken);
        if (currentAgreement == null)
        {
            _logger.LogDebug("Escrow {EscrowId} no longer exists", agreement.EscrowId);
            return false;
        }

        // Re-check deadline hasn't passed (might have been updated)
        if (currentAgreement.ConfirmationDeadline == null || currentAgreement.ConfirmationDeadline > now)
        {
            return false;
        }

        // Re-check status hasn't changed
        if (currentAgreement.Status != previousStatus)
        {
            return false;
        }

        // Parse the configured behavior
        var behavior = ParseConfirmationTimeoutBehavior(_configuration.ConfirmationTimeoutBehavior);

        _logger.LogInformation("Processing expired confirmation for escrow {EscrowId}, behavior: {Behavior}",
            agreement.EscrowId, behavior);

        switch (behavior)
        {
            case ConfirmationTimeoutBehavior.AutoConfirm:
                return await HandleAutoConfirmAsync(
                    agreementStore, statusIndexStore, messageBus,
                    currentAgreement, etag, now, cancellationToken);

            case ConfirmationTimeoutBehavior.Dispute:
                return await HandleDisputeAsync(
                    agreementStore, statusIndexStore, messageBus,
                    currentAgreement, etag, now, cancellationToken);

            case ConfirmationTimeoutBehavior.Refund:
                return await HandleRefundAsync(
                    agreementStore, statusIndexStore, messageBus,
                    currentAgreement, etag, now, cancellationToken);

            default:
                _logger.LogWarning("Unknown timeout behavior: {Behavior}, defaulting to auto_confirm", behavior);
                return await HandleAutoConfirmAsync(
                    agreementStore, statusIndexStore, messageBus,
                    currentAgreement, etag, now, cancellationToken);
        }
    }

    /// <summary>
    /// Handles auto_confirm behavior: If services have confirmed, complete the escrow.
    /// Otherwise escalate to dispute.
    /// </summary>
    private async Task<bool> HandleAutoConfirmAsync(
        IQueryableStateStore<EscrowAgreementModel> agreementStore,
        IStateStore<StatusIndexEntry> statusIndexStore,
        IMessageBus messageBus,
        EscrowAgreementModel agreement,
        string? etag,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var allServicesConfirmed = agreement.ReleaseConfirmations?.All(c => c.ServiceConfirmed) ?? true;

        if (allServicesConfirmed)
        {
            _logger.LogInformation("Auto-confirming escrow {EscrowId} after timeout (services confirmed)",
                agreement.EscrowId);

            return await CompleteEscrowAsync(
                agreementStore, statusIndexStore, messageBus,
                agreement, etag, now, cancellationToken);
        }
        else
        {
            _logger.LogWarning("Cannot auto-confirm escrow {EscrowId} - services not confirmed, escalating to dispute",
                agreement.EscrowId);

            return await TransitionToDisputedAsync(
                agreementStore, statusIndexStore, messageBus,
                agreement, etag, now, cancellationToken);
        }
    }

    /// <summary>
    /// Handles dispute behavior: Transition to Disputed state.
    /// </summary>
    private async Task<bool> HandleDisputeAsync(
        IQueryableStateStore<EscrowAgreementModel> agreementStore,
        IStateStore<StatusIndexEntry> statusIndexStore,
        IMessageBus messageBus,
        EscrowAgreementModel agreement,
        string? etag,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Escalating escrow {EscrowId} to Disputed after timeout", agreement.EscrowId);

        return await TransitionToDisputedAsync(
            agreementStore, statusIndexStore, messageBus,
            agreement, etag, now, cancellationToken);
    }

    /// <summary>
    /// Handles refund behavior: Transition to Refunding/Refunded.
    /// </summary>
    private async Task<bool> HandleRefundAsync(
        IQueryableStateStore<EscrowAgreementModel> agreementStore,
        IStateStore<StatusIndexEntry> statusIndexStore,
        IMessageBus messageBus,
        EscrowAgreementModel agreement,
        string? etag,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Initiating refund for escrow {EscrowId} after timeout", agreement.EscrowId);

        var previousStatus = agreement.Status;
        var agreementKey = $"agreement:{agreement.EscrowId}";

        agreement.Status = EscrowStatus.Refunded;
        agreement.CompletedAt = now;
        agreement.Resolution = EscrowResolution.Refunded;
        agreement.ResolutionNotes = "Confirmation timeout expired - automatic refund";

        var saveResult = await agreementStore.TrySaveAsync(agreementKey, agreement, etag ?? string.Empty, cancellationToken);
        if (saveResult == null)
        {
            _logger.LogDebug("Concurrent modification on escrow {EscrowId} during refund transition", agreement.EscrowId);
            return false;
        }

        // Update status index
        await UpdateStatusIndexAsync(statusIndexStore, previousStatus, EscrowStatus.Refunded, agreement.EscrowId, agreement.ExpiresAt, now, cancellationToken);

        // Publish refunded event
        var refundEvent = new EscrowRefundedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            EscrowId = agreement.EscrowId,
            Depositors = agreement.Deposits?.Select(d => new DepositorInfo
            {
                PartyId = d.PartyId,
                PartyType = d.PartyType,
                AssetSummary = EscrowService.GenerateAssetSummary(d.Assets?.Assets)
            }).ToList() ?? new List<DepositorInfo>(),
            Reason = "Confirmation timeout expired",
            Resolution = EscrowResolution.Refunded,
            CompletedAt = now
        };
        await messageBus.TryPublishAsync(EscrowTopics.EscrowRefunded, refundEvent, cancellationToken);

        return true;
    }

    /// <summary>
    /// Completes an escrow (Released or Refunded) based on its current status.
    /// </summary>
    private async Task<bool> CompleteEscrowAsync(
        IQueryableStateStore<EscrowAgreementModel> agreementStore,
        IStateStore<StatusIndexEntry> statusIndexStore,
        IMessageBus messageBus,
        EscrowAgreementModel agreement,
        string? etag,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var previousStatus = agreement.Status;
        var agreementKey = $"agreement:{agreement.EscrowId}";
        EscrowStatus targetStatus;
        EscrowResolution resolution;

        if (previousStatus == EscrowStatus.Releasing)
        {
            targetStatus = EscrowStatus.Released;
            resolution = EscrowResolution.Released;
        }
        else if (previousStatus == EscrowStatus.Refunding)
        {
            targetStatus = EscrowStatus.Refunded;
            resolution = EscrowResolution.Refunded;
        }
        else
        {
            _logger.LogWarning("Cannot complete escrow {EscrowId} from status {Status}", agreement.EscrowId, previousStatus);
            return false;
        }

        agreement.Status = targetStatus;
        agreement.CompletedAt = now;
        agreement.Resolution = resolution;
        agreement.ResolutionNotes = "Confirmation timeout expired - auto-confirmed";

        var saveResult = await agreementStore.TrySaveAsync(agreementKey, agreement, etag ?? string.Empty, cancellationToken);
        if (saveResult == null)
        {
            _logger.LogDebug("Concurrent modification on escrow {EscrowId} during completion", agreement.EscrowId);
            return false;
        }

        // Update status index
        await UpdateStatusIndexAsync(statusIndexStore, previousStatus, targetStatus, agreement.EscrowId, agreement.ExpiresAt, now, cancellationToken);

        // Publish appropriate event
        if (targetStatus == EscrowStatus.Released)
        {
            var releaseEvent = new EscrowReleasedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                EscrowId = agreement.EscrowId,
                Recipients = agreement.ReleaseAllocations?.Select(a => new RecipientInfo
                {
                    PartyId = a.RecipientPartyId,
                    PartyType = a.RecipientPartyType,
                    AssetSummary = EscrowService.GenerateAssetSummary(a.Assets)
                }).ToList() ?? new List<RecipientInfo>(),
                Resolution = EscrowResolution.Released,
                CompletedAt = now
            };
            await messageBus.TryPublishAsync(EscrowTopics.EscrowReleased, releaseEvent, cancellationToken);
        }
        else
        {
            var refundEvent = new EscrowRefundedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                EscrowId = agreement.EscrowId,
                Depositors = agreement.Deposits?.Select(d => new DepositorInfo
                {
                    PartyId = d.PartyId,
                    PartyType = d.PartyType,
                    AssetSummary = EscrowService.GenerateAssetSummary(d.Assets?.Assets)
                }).ToList() ?? new List<DepositorInfo>(),
                Reason = "Confirmation timeout expired - auto-confirmed",
                Resolution = EscrowResolution.Refunded,
                CompletedAt = now
            };
            await messageBus.TryPublishAsync(EscrowTopics.EscrowRefunded, refundEvent, cancellationToken);
        }

        return true;
    }

    /// <summary>
    /// Transitions an escrow to Disputed state.
    /// </summary>
    private async Task<bool> TransitionToDisputedAsync(
        IQueryableStateStore<EscrowAgreementModel> agreementStore,
        IStateStore<StatusIndexEntry> statusIndexStore,
        IMessageBus messageBus,
        EscrowAgreementModel agreement,
        string? etag,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var previousStatus = agreement.Status;
        var agreementKey = $"agreement:{agreement.EscrowId}";

        agreement.Status = EscrowStatus.Disputed;

        var saveResult = await agreementStore.TrySaveAsync(agreementKey, agreement, etag ?? string.Empty, cancellationToken);
        if (saveResult == null)
        {
            _logger.LogDebug("Concurrent modification on escrow {EscrowId} during dispute transition", agreement.EscrowId);
            return false;
        }

        // Update status index
        await UpdateStatusIndexAsync(statusIndexStore, previousStatus, EscrowStatus.Disputed, agreement.EscrowId, agreement.ExpiresAt, now, cancellationToken);

        // Publish dispute event
        var disputeEvent = new EscrowDisputedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            EscrowId = agreement.EscrowId,
            DisputedBy = Guid.Empty, // System-initiated dispute
            DisputedByType = EntityType.System,
            Reason = "Confirmation timeout expired",
            DisputedAt = now
        };
        await messageBus.TryPublishAsync(EscrowTopics.EscrowDisputed, disputeEvent, cancellationToken);

        return true;
    }

    /// <summary>
    /// Updates the status index when transitioning between states.
    /// </summary>
    private static async Task UpdateStatusIndexAsync(
        IStateStore<StatusIndexEntry> statusIndexStore,
        EscrowStatus oldStatus,
        EscrowStatus newStatus,
        Guid escrowId,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var oldStatusKey = $"status:{oldStatus}:{escrowId}";
        await statusIndexStore.DeleteAsync(oldStatusKey, cancellationToken);

        var newStatusKey = $"status:{newStatus}:{escrowId}";
        var statusEntry = new StatusIndexEntry
        {
            EscrowId = escrowId,
            Status = newStatus,
            ExpiresAt = expiresAt,
            AddedAt = now
        };
        await statusIndexStore.SaveAsync(newStatusKey, statusEntry, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Parses the configuration string to ConfirmationTimeoutBehavior enum.
    /// </summary>
    private static ConfirmationTimeoutBehavior ParseConfirmationTimeoutBehavior(string value)
    {
        return value?.ToLowerInvariant() switch
        {
            "auto_confirm" => ConfirmationTimeoutBehavior.AutoConfirm,
            "dispute" => ConfirmationTimeoutBehavior.Dispute,
            "refund" => ConfirmationTimeoutBehavior.Refund,
            _ => ConfirmationTimeoutBehavior.AutoConfirm
        };
    }
}
