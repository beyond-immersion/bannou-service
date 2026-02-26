using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Xml;

namespace BeyondImmersion.BannouService.Escrow;

/// <summary>
/// Background service that checks for expired escrows and transitions them
/// to the Expired state with automatic refund of any deposits.
/// </summary>
public class EscrowExpirationService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EscrowExpirationService> _logger;
    private readonly EscrowServiceConfiguration _configuration;

    /// <summary>
    /// Interval between expiration checks, from configuration (ISO 8601 duration).
    /// </summary>
    private TimeSpan CheckInterval => ParseDuration(_configuration.ExpirationCheckInterval);

    /// <summary>
    /// Grace period after expiration before auto-refund (ISO 8601 duration).
    /// </summary>
    private TimeSpan GracePeriod => ParseDuration(_configuration.ExpirationGracePeriod);

    /// <summary>
    /// Maximum escrows to process per cycle, from configuration.
    /// </summary>
    private int BatchSize => _configuration.ExpirationBatchSize;

    /// <summary>
    /// Startup delay before first check to allow other services to start.
    /// </summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Statuses that can transition to Expired (non-terminal, pre-release states).
    /// </summary>
    private static readonly EscrowStatus[] ExpirableStatuses = new[]
    {
        EscrowStatus.PendingDeposits,
        EscrowStatus.PartiallyFunded,
        EscrowStatus.PendingConsent,
        EscrowStatus.PendingCondition
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="EscrowExpirationService"/> class.
    /// </summary>
    public EscrowExpirationService(
        IServiceProvider serviceProvider,
        ILogger<EscrowExpirationService> logger,
        EscrowServiceConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Escrow expiration service starting, check interval: {Interval}, grace period: {GracePeriod}",
            CheckInterval, GracePeriod);

        // Wait a bit before first check to allow other services to start
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Escrow expiration service cancelled during startup");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndProcessExpiredEscrowsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Expected during shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during escrow expiration check");
                try
                {
                    using var errorScope = _serviceProvider.CreateScope();
                    var messageBus = errorScope.ServiceProvider.GetRequiredService<IMessageBus>();
                    await messageBus.TryPublishErrorAsync(
                        "escrow",
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

        _logger.LogInformation("Escrow expiration service stopped");
    }

    /// <summary>
    /// Checks for expired escrows and processes them.
    /// </summary>
    private async Task CheckAndProcessExpiredEscrowsAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Checking for expired escrows");

        using var scope = _serviceProvider.CreateScope();
        var stateStoreFactory = scope.ServiceProvider.GetRequiredService<IStateStoreFactory>();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

        var agreementStore = stateStoreFactory.GetQueryableStore<EscrowAgreementModel>(StateStoreDefinitions.EscrowAgreements);
        var statusIndexStore = stateStoreFactory.GetStore<StatusIndexEntry>(StateStoreDefinitions.EscrowStatusIndex);
        var partyPendingStore = stateStoreFactory.GetStore<PartyPendingCount>(StateStoreDefinitions.EscrowPartyPending);

        var now = DateTimeOffset.UtcNow;
        var expirationCutoff = now - GracePeriod;
        var processed = 0;

        foreach (var status in ExpirableStatuses)
        {
            if (processed >= BatchSize) break;

            // Query escrows in this status with ExpiresAt past the cutoff (after grace period)
            var agreements = await agreementStore.QueryAsync(
                a => a.Status == status && a.ExpiresAt <= expirationCutoff,
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
                    partyPendingStore,
                    messageBus,
                    agreement,
                    now,
                    cancellationToken);

                if (wasProcessed) processed++;
            }
        }

        if (processed > 0)
        {
            _logger.LogInformation("Processed {Count} expired escrows", processed);
        }
        else
        {
            _logger.LogDebug("No expired escrows this cycle");
        }
    }

    /// <summary>
    /// Processes a single expired escrow.
    /// </summary>
    private async Task<bool> ProcessExpiredEscrowAsync(
        IQueryableStateStore<EscrowAgreementModel> agreementStore,
        IStateStore<StatusIndexEntry> statusIndexStore,
        IStateStore<PartyPendingCount> partyPendingStore,
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

        // Re-check status hasn't changed (might have been processed by another instance or user action)
        if (currentAgreement.Status != previousStatus)
        {
            _logger.LogDebug("Escrow {EscrowId} status changed from {Previous} to {Current}, skipping",
                agreement.EscrowId, previousStatus, currentAgreement.Status);
            return false;
        }

        // Re-check expiration (might have been extended)
        var expirationCutoff = now - GracePeriod;
        if (currentAgreement.ExpiresAt > expirationCutoff)
        {
            _logger.LogDebug("Escrow {EscrowId} expiration extended, skipping", agreement.EscrowId);
            return false;
        }

        _logger.LogInformation("Processing expired escrow {EscrowId}, previous status: {Status}",
            agreement.EscrowId, previousStatus);

        // Check if there are deposits to refund
        var hasDeposits = currentAgreement.Deposits != null && currentAgreement.Deposits.Count > 0;

        // Update escrow to Expired status
        currentAgreement.Status = EscrowStatus.Expired;
        currentAgreement.CompletedAt = now;
        currentAgreement.Resolution = hasDeposits ? EscrowResolution.ExpiredRefunded : EscrowResolution.ExpiredRefunded;
        currentAgreement.ResolutionNotes = hasDeposits
            ? "Escrow expired - deposits automatically refunded"
            : "Escrow expired - no deposits to refund";

        // GetWithETagAsync returns non-null etag for existing records;
        // coalesce satisfies compiler's nullable analysis (will never execute)
        var saveResult = await agreementStore.TrySaveAsync(agreementKey, currentAgreement, etag ?? string.Empty, cancellationToken);
        if (saveResult == null)
        {
            _logger.LogDebug("Concurrent modification on escrow {EscrowId} during expiration processing", agreement.EscrowId);
            return false;
        }

        // Update status index
        await UpdateStatusIndexAsync(statusIndexStore, previousStatus, EscrowStatus.Expired, agreement.EscrowId, currentAgreement.ExpiresAt, now, cancellationToken);

        // Decrement party pending counts for all parties
        if (currentAgreement.Parties != null)
        {
            foreach (var party in currentAgreement.Parties)
            {
                await DecrementPartyPendingCountAsync(partyPendingStore, party.PartyId, party.PartyType, cancellationToken);
            }
        }

        // Publish EscrowExpiredEvent
        var expiredEvent = new EscrowExpiredEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            EscrowId = agreement.EscrowId,
            Status = previousStatus,
            AutoRefunded = hasDeposits,
            ExpiredAt = now
        };
        await messageBus.TryPublishAsync(EscrowTopics.EscrowExpired, expiredEvent, cancellationToken);

        // If there were deposits, also publish refund event for downstream services
        if (hasDeposits)
        {
            var refundEvent = new EscrowRefundedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                EscrowId = agreement.EscrowId,
                Depositors = (currentAgreement.Deposits ?? new List<EscrowDepositModel>()).Select(d => new DepositorInfo
                {
                    PartyId = d.PartyId,
                    PartyType = d.PartyType,
                    AssetSummary = EscrowService.GenerateAssetSummary(d.Assets?.Assets)
                }).ToList(),
                Reason = "Escrow expired",
                Resolution = EscrowResolution.Refunded,
                CompletedAt = now
            };
            await messageBus.TryPublishAsync(EscrowTopics.EscrowRefunded, refundEvent, cancellationToken);

            _logger.LogInformation("Escrow {EscrowId} expired with {DepositCount} deposits auto-refunded",
                agreement.EscrowId, (currentAgreement.Deposits ?? new List<EscrowDepositModel>()).Count);
        }
        else
        {
            _logger.LogInformation("Escrow {EscrowId} expired with no deposits", agreement.EscrowId);
        }

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
    /// Decrements the pending escrow count for a party using optimistic concurrency.
    /// </summary>
    private async Task DecrementPartyPendingCountAsync(
        IStateStore<PartyPendingCount> partyPendingStore,
        Guid partyId,
        EntityType partyType,
        CancellationToken cancellationToken)
    {
        var partyKey = $"party:{partyType}:{partyId}";
        var now = DateTimeOffset.UtcNow;

        for (var attempt = 0; attempt < _configuration.MaxConcurrencyRetries; attempt++)
        {
            var (existing, etag) = await partyPendingStore.GetWithETagAsync(partyKey, cancellationToken);
            if (existing == null || existing.PendingCount <= 0)
            {
                return;
            }

            existing.PendingCount--;
            existing.LastUpdated = now;

            // GetWithETagAsync returns non-null etag for existing records;
            // coalesce satisfies compiler's nullable analysis (will never execute)
            var saveResult = await partyPendingStore.TrySaveAsync(partyKey, existing, etag ?? string.Empty, cancellationToken);
            if (saveResult != null)
            {
                return;
            }

            _logger.LogDebug("Concurrent modification on party pending count {PartyKey}, retrying decrement (attempt {Attempt})",
                partyKey, attempt + 1);
        }

        _logger.LogWarning("Failed to decrement party pending count for {PartyType}:{PartyId} after {MaxRetries} attempts",
            partyType, partyId, _configuration.MaxConcurrencyRetries);
    }

    /// <summary>
    /// Parses an ISO 8601 duration string to a TimeSpan.
    /// </summary>
    private static TimeSpan ParseDuration(string duration)
    {
        try
        {
            return XmlConvert.ToTimeSpan(duration);
        }
        catch
        {
            // Default to 1 minute if parsing fails
            return TimeSpan.FromMinutes(1);
        }
    }
}
