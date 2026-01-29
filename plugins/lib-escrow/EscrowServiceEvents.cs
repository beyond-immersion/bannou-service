using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Escrow;

/// <summary>
/// Partial class for EscrowService event handling.
/// Contains event consumer registration and handler implementations.
/// Handles contract lifecycle events for contract-bound escrows.
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
        eventConsumer.RegisterHandler<IEscrowService, ContractFulfilledEvent>(
            "contract.fulfilled",
            async (svc, evt) => await ((EscrowService)svc).HandleContractFulfilledAsync(evt));

        eventConsumer.RegisterHandler<IEscrowService, ContractTerminatedEvent>(
            "contract.terminated",
            async (svc, evt) => await ((EscrowService)svc).HandleContractTerminatedAsync(evt));
    }

    /// <summary>
    /// Handles contract fulfilled events by transitioning bound escrows to Finalizing.
    /// Uses optimistic concurrency (ETag) to prevent concurrent state transitions.
    /// </summary>
    /// <param name="evt">The contract fulfilled event.</param>
    internal async Task HandleContractFulfilledAsync(ContractFulfilledEvent evt)
    {
        try
        {
            var escrows = await AgreementStore.QueryAsync(
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
        try
        {
            var escrows = await AgreementStore.QueryAsync(
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
        var agreementKey = GetAgreementKey(escrowId);

        for (var attempt = 0; attempt < _configuration.MaxConcurrencyRetries; attempt++)
        {
            var (agreementModel, etag) = await AgreementStore.GetWithETagAsync(agreementKey);

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

            var saveResult = await AgreementStore.TrySaveAsync(agreementKey, agreementModel, etag ?? string.Empty);
            if (saveResult == null)
            {
                _logger.LogDebug("Concurrent modification during contract.fulfilled handling for escrow {EscrowId}, retrying (attempt {Attempt})",
                    escrowId, attempt + 1);
                continue;
            }

            // Update status index
            var oldStatusKey = $"{GetStatusIndexKey(previousStatus)}:{escrowId}";
            await StatusIndexStore.DeleteAsync(oldStatusKey);

            var newStatusKey = $"{GetStatusIndexKey(EscrowStatus.Finalizing)}:{escrowId}";
            var statusEntry = new StatusIndexEntry
            {
                EscrowId = escrowId,
                Status = EscrowStatus.Finalizing,
                ExpiresAt = agreementModel.ExpiresAt,
                AddedAt = now
            };
            await StatusIndexStore.SaveAsync(newStatusKey, statusEntry);

            // Reset validation tracking
            var validationKey = GetValidationKey(escrowId);
            var validationTracking = await ValidationStore.GetAsync(validationKey);
            if (validationTracking != null)
            {
                validationTracking.FailedValidationCount = 0;
                validationTracking.LastValidatedAt = now;
                await ValidationStore.SaveAsync(validationKey, validationTracking);
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
            await _messageBus.TryPublishAsync(EscrowTopics.EscrowFinalizing, finalizingEvent);

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
        var agreementKey = GetAgreementKey(escrowId);

        for (var attempt = 0; attempt < _configuration.MaxConcurrencyRetries; attempt++)
        {
            var (agreementModel, etag) = await AgreementStore.GetWithETagAsync(agreementKey);

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

            var saveResult = await AgreementStore.TrySaveAsync(agreementKey, agreementModel, etag ?? string.Empty);
            if (saveResult == null)
            {
                _logger.LogDebug("Concurrent modification during contract.terminated handling for escrow {EscrowId}, retrying (attempt {Attempt})",
                    escrowId, attempt + 1);
                continue;
            }

            // Update status index
            var oldStatusKey = $"{GetStatusIndexKey(previousStatus)}:{escrowId}";
            await StatusIndexStore.DeleteAsync(oldStatusKey);

            var newStatusKey = $"{GetStatusIndexKey(EscrowStatus.Refunded)}:{escrowId}";
            var statusEntry = new StatusIndexEntry
            {
                EscrowId = escrowId,
                Status = EscrowStatus.Refunded,
                ExpiresAt = agreementModel.ExpiresAt,
                AddedAt = now
            };
            await StatusIndexStore.SaveAsync(newStatusKey, statusEntry);

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
            await _messageBus.TryPublishAsync(EscrowTopics.EscrowRefunded, refundEvent);

            _logger.LogInformation("Escrow {EscrowId} refunded on contract.terminated for contract {ContractId}",
                escrowId, contractId);
            return;
        }

        _logger.LogWarning("Failed to refund escrow {EscrowId} after {MaxRetries} attempts for contract {ContractId}",
            escrowId, _configuration.MaxConcurrencyRetries, contractId);
    }
}
