using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Escrow;

/// <summary>
/// Partial class for EscrowService event handling.
/// Contains event consumer registration and handler implementations.
/// Handles account deletion cleanup and contract lifecycle events for contract-bound escrows.
/// </summary>
public partial class EscrowService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<IEscrowService, AccountDeletedEvent>(
            "account.deleted",
            async (svc, evt) => await ((EscrowService)svc).HandleAccountDeletedAsync(evt));

        eventConsumer.RegisterHandler<IEscrowService, ContractFulfilledEvent>(
            "contract.fulfilled",
            async (svc, evt) => await ((EscrowService)svc).HandleContractFulfilledAsync(evt));

        eventConsumer.RegisterHandler<IEscrowService, ContractTerminatedEvent>(
            "contract.terminated",
            async (svc, evt) => await ((EscrowService)svc).HandleContractTerminatedAsync(evt));
    }

    /// <summary>
    /// Handles account.deleted events by cleaning up all escrow data for the deleted account.
    /// Finds all agreements where the account is a party and removes associated data.
    /// Per FOUNDATION TENETS (Account Deletion Cleanup Obligation).
    /// </summary>
    /// <param name="evt">The account deleted event data.</param>
    public async Task HandleAccountDeletedAsync(AccountDeletedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.escrow", "EscrowService.HandleAccountDeleted");
        _logger.LogInformation("Handling account.deleted for account {AccountId}", evt.AccountId);
        try
        {
            await CleanupEscrowsForAccountAsync(evt.AccountId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clean up escrow data for account {AccountId}", evt.AccountId);
            await _messageBus.TryPublishErrorAsync(
                "escrow",
                "CleanupEscrowsForAccount",
                ex.GetType().Name,
                ex.Message,
                endpoint: "account.deleted",
                details: $"accountId={evt.AccountId}",
                stack: ex.StackTrace);
        }
    }

    /// <summary>
    /// Cleans up all escrow data for a deleted account. Queries for agreements where
    /// the account is a party, then removes agreement records, associated tokens,
    /// status index entries, party pending counts, idempotency records, and validation
    /// tracking entries. Per-agreement failures are logged as warnings and do not abort
    /// the overall cleanup. Multi-node idempotent — state store deletes are naturally
    /// idempotent so "not found" is treated as success.
    /// </summary>
    /// <param name="accountId">The deleted account ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal async Task CleanupEscrowsForAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.escrow", "EscrowService.CleanupEscrowsForAccount");

        var agreements = await _agreementStore.QueryAsync(
            a => a.Parties != null && a.Parties.Any(p => p.PartyId == accountId && p.PartyType == EntityType.Account),
            cancellationToken: cancellationToken);

        if (agreements.Count == 0)
        {
            _logger.LogDebug("No escrow agreements found for account {AccountId}, skipping cleanup", accountId);
            return;
        }

        var successCount = 0;
        var failureCount = 0;

        foreach (var agreement in agreements)
        {
            try
            {
                await CleanupSingleAgreementAsync(agreement, cancellationToken);
                successCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clean up escrow {EscrowId} for account {AccountId}, continuing",
                    agreement.EscrowId, accountId);
                failureCount++;
            }
        }

        _logger.LogInformation(
            "Escrow account cleanup complete for {AccountId}: {Success} succeeded, {Failed} failed",
            accountId, successCount, failureCount);
    }

    /// <summary>
    /// Cleans up a single escrow agreement and all associated data (tokens, indexes, counts, validation).
    /// </summary>
    private async Task CleanupSingleAgreementAsync(EscrowAgreementModel agreement, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.escrow", "EscrowService.CleanupSingleAgreement");

        var escrowId = agreement.EscrowId;

        // Delete token hashes for all parties
        if (agreement.Parties != null)
        {
            foreach (var party in agreement.Parties)
            {
                if (!string.IsNullOrEmpty(party.DepositToken))
                {
                    var depositTokenHash = HashToken(party.DepositToken);
                    await _tokenStore.DeleteAsync(BuildTokenKey(depositTokenHash), cancellationToken: cancellationToken);
                }

                if (!string.IsNullOrEmpty(party.ReleaseToken))
                {
                    var releaseTokenHash = HashToken(party.ReleaseToken);
                    await _tokenStore.DeleteAsync(BuildTokenKey(releaseTokenHash), cancellationToken: cancellationToken);
                }

                // Decrement party pending count
                await DecrementPartyPendingCountAsync(party.PartyId, party.PartyType, cancellationToken);
            }
        }

        // Delete status index entry
        var statusKey = $"{BuildStatusIndexKey(agreement.Status)}:{escrowId}";
        await _statusIndexStore.DeleteAsync(statusKey, cancellationToken: cancellationToken);

        // Delete validation tracking
        await _validationStore.DeleteAsync(BuildValidationKey(escrowId), cancellationToken: cancellationToken);

        // Delete the agreement record
        await _agreementStore.DeleteAsync(BuildAgreementKey(escrowId), cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Handles contract fulfilled events by transitioning bound escrows to Finalizing.
    /// Uses optimistic concurrency (ETag) to prevent concurrent state transitions.
    /// </summary>
    /// <param name="evt">The contract fulfilled event.</param>
    internal async Task HandleContractFulfilledAsync(ContractFulfilledEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.escrow", "EscrowService.HandleContractFulfilledAsync");
        try
        {
            var escrows = await _agreementStore.QueryAsync(
                m => m.BoundContractId == evt.ContractId);

            if (escrows.Count == 0)
            {
                _logger.LogDebug("No escrow bound to fulfilled contract {ContractId}", evt.ContractId);
                return;
            }

            foreach (var escrow in escrows)
            {
                await TransitionToFinalizingForContractAsync(escrow.EscrowId, evt.ContractId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle contract.fulfilled for contract {ContractId}", evt.ContractId);
            await EmitErrorAsync("HandleContractFulfilled", ex.Message, new { evt.ContractId });
        }
    }

    /// <summary>
    /// Handles contract terminated events by refunding bound escrows.
    /// Uses optimistic concurrency (ETag) to prevent concurrent state transitions.
    /// </summary>
    /// <param name="evt">The contract terminated event.</param>
    internal async Task HandleContractTerminatedAsync(ContractTerminatedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.escrow", "EscrowService.HandleContractTerminatedAsync");
        try
        {
            var escrows = await _agreementStore.QueryAsync(
                m => m.BoundContractId == evt.ContractId);

            if (escrows.Count == 0)
            {
                _logger.LogDebug("No escrow bound to terminated contract {ContractId}", evt.ContractId);
                return;
            }

            foreach (var escrow in escrows)
            {
                await RefundForContractTerminationAsync(escrow.EscrowId, evt.ContractId, evt.Reason);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle contract.terminated for contract {ContractId}", evt.ContractId);
            await EmitErrorAsync("HandleContractTerminated", ex.Message, new { evt.ContractId });
        }
    }

    /// <summary>
    /// Transitions an escrow to Finalizing state when its bound contract is fulfilled.
    /// Only valid from Pending_condition state.
    /// </summary>
    private async Task TransitionToFinalizingForContractAsync(Guid escrowId, Guid contractId)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.escrow", "EscrowService.TransitionToFinalizingForContractAsync");
        var agreementKey = BuildAgreementKey(escrowId);

        // Manual retry loop: mutation includes terminal/status state validation with early returns (cannot use UpdateWithRetryAsync)
        for (var attempt = 0; attempt < _configuration.MaxConcurrencyRetries; attempt++)
        {
            var (agreementModel, etag) = await _agreementStore.GetWithETagAsync(agreementKey);

            if (agreementModel == null)
            {
                _logger.LogWarning("Escrow {EscrowId} not found when handling contract.fulfilled for {ContractId}",
                    escrowId, contractId);
                return;
            }

            if (IsTerminalState(agreementModel.Status))
            {
                _logger.LogDebug("Escrow {EscrowId} already in terminal state {Status}, ignoring contract.fulfilled",
                    escrowId, agreementModel.Status);
                return;
            }

            if (agreementModel.Status != EscrowStatus.PendingCondition)
            {
                _logger.LogDebug("Escrow {EscrowId} in state {Status}, not Pending_condition; ignoring contract.fulfilled",
                    escrowId, agreementModel.Status);
                return;
            }

            var now = DateTimeOffset.UtcNow;
            var previousStatus = agreementModel.Status;

            agreementModel.Status = EscrowStatus.Finalizing;
            agreementModel.LastValidatedAt = now;
            agreementModel.ValidationFailures = null;

            // GetWithETagAsync returns non-null etag for existing records;
            // coalesce satisfies compiler's nullable analysis (will never execute)
            var saveResult = await _agreementStore.TrySaveAsync(agreementKey, agreementModel, etag ?? string.Empty);
            if (saveResult == null)
            {
                _logger.LogDebug("Concurrent modification during contract.fulfilled handling for escrow {EscrowId}, retrying (attempt {Attempt})",
                    escrowId, attempt + 1);
                continue;
            }

            // Update status index
            var oldStatusKey = $"{BuildStatusIndexKey(previousStatus)}:{escrowId}";
            await _statusIndexStore.DeleteAsync(oldStatusKey);

            var newStatusKey = $"{BuildStatusIndexKey(EscrowStatus.Finalizing)}:{escrowId}";
            var statusEntry = new StatusIndexEntry
            {
                EscrowId = escrowId,
                Status = EscrowStatus.Finalizing,
                ExpiresAt = agreementModel.ExpiresAt,
                AddedAt = now
            };
            await _statusIndexStore.SaveAsync(newStatusKey, statusEntry);

            // Reset validation tracking
            var validationKey = BuildValidationKey(escrowId);
            var validationTracking = await _validationStore.GetAsync(validationKey);
            if (validationTracking != null)
            {
                validationTracking.FailedValidationCount = 0;
                validationTracking.LastValidatedAt = now;
                await _validationStore.SaveAsync(validationKey, validationTracking);
            }

            // Publish finalizing event
            var finalizingEvent = new EscrowFinalizingEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                EscrowId = escrowId,
                BoundContractId = contractId,
                FinalizerCount = 0,
                StartedAt = now
            };
            await _messageBus.PublishEscrowFinalizingAsync(finalizingEvent);

            _logger.LogInformation("Escrow {EscrowId} transitioned to Finalizing on contract.fulfilled for contract {ContractId}",
                escrowId, contractId);
            return;
        }

        _logger.LogWarning("Failed to transition escrow {EscrowId} to Finalizing after {MaxRetries} attempts for contract {ContractId}",
            escrowId, _configuration.MaxConcurrencyRetries, contractId);
    }

    /// <summary>
    /// Refunds an escrow when its bound contract is terminated.
    /// Valid from Pending_condition, Validation_failed, or Pending_consent states.
    /// Transitions directly to Refunded terminal state.
    /// </summary>
    private async Task RefundForContractTerminationAsync(Guid escrowId, Guid contractId, string? reason)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.escrow", "EscrowService.RefundForContractTerminationAsync");
        var agreementKey = BuildAgreementKey(escrowId);

        // Manual retry loop: mutation includes terminal/status state validation with early returns (cannot use UpdateWithRetryAsync)
        for (var attempt = 0; attempt < _configuration.MaxConcurrencyRetries; attempt++)
        {
            var (agreementModel, etag) = await _agreementStore.GetWithETagAsync(agreementKey);

            if (agreementModel == null)
            {
                _logger.LogWarning("Escrow {EscrowId} not found when handling contract.terminated for {ContractId}",
                    escrowId, contractId);
                return;
            }

            if (IsTerminalState(agreementModel.Status))
            {
                _logger.LogDebug("Escrow {EscrowId} already in terminal state {Status}, ignoring contract.terminated",
                    escrowId, agreementModel.Status);
                return;
            }

            // Only refund from states where a contract termination is meaningful
            var validStatesForContractRefund = new HashSet<EscrowStatus>
            {
                EscrowStatus.PendingCondition,
                EscrowStatus.ValidationFailed,
                EscrowStatus.PendingConsent
            };

            if (!validStatesForContractRefund.Contains(agreementModel.Status))
            {
                _logger.LogDebug("Escrow {EscrowId} in state {Status}, not valid for contract termination refund",
                    escrowId, agreementModel.Status);
                return;
            }

            var now = DateTimeOffset.UtcNow;
            var previousStatus = agreementModel.Status;
            var refundReason = reason ?? "Bound contract terminated";

            agreementModel.Status = EscrowStatus.Refunded;
            agreementModel.Resolution = EscrowResolution.Refunded;
            agreementModel.CompletedAt = now;
            agreementModel.ResolutionNotes = refundReason;

            // GetWithETagAsync returns non-null etag for existing records;
            // coalesce satisfies compiler's nullable analysis (will never execute)
            var saveResult = await _agreementStore.TrySaveAsync(agreementKey, agreementModel, etag ?? string.Empty);
            if (saveResult == null)
            {
                _logger.LogDebug("Concurrent modification during contract.terminated handling for escrow {EscrowId}, retrying (attempt {Attempt})",
                    escrowId, attempt + 1);
                continue;
            }

            // Update status index
            var oldStatusKey = $"{BuildStatusIndexKey(previousStatus)}:{escrowId}";
            await _statusIndexStore.DeleteAsync(oldStatusKey);

            var newStatusKey = $"{BuildStatusIndexKey(EscrowStatus.Refunded)}:{escrowId}";
            var statusEntry = new StatusIndexEntry
            {
                EscrowId = escrowId,
                Status = EscrowStatus.Refunded,
                ExpiresAt = agreementModel.ExpiresAt,
                AddedAt = now
            };
            await _statusIndexStore.SaveAsync(newStatusKey, statusEntry);

            // Decrement pending counts
            foreach (var party in agreementModel.Parties ?? new List<EscrowPartyModel>())
            {
                await DecrementPartyPendingCountAsync(party.PartyId, party.PartyType);
            }

            // Publish refund event
            var refundEvent = new EscrowRefundedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                EscrowId = escrowId,
                Depositors = (agreementModel.Deposits ?? new List<EscrowDepositModel>())
                    .Select(d => new DepositorInfo
                    {
                        PartyId = d.PartyId,
                        PartyType = d.PartyType,
                        AssetSummary = GenerateAssetSummary(d.Assets?.Assets)
                    }).ToList(),
                Reason = refundReason,
                Resolution = EscrowResolution.Refunded,
                CompletedAt = now
            };
            await _messageBus.PublishEscrowRefundedAsync(refundEvent);

            _logger.LogInformation("Escrow {EscrowId} refunded on contract.terminated for contract {ContractId}",
                escrowId, contractId);
            return;
        }

        _logger.LogWarning("Failed to refund escrow {EscrowId} after {MaxRetries} attempts for contract {ContractId}",
            escrowId, _configuration.MaxConcurrencyRetries, contractId);
    }
}
