using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Escrow;

/// <summary>
/// Completion operations for escrow management.
/// Handles release, refund, cancel, dispute, and resolve operations.
/// </summary>
public partial class EscrowService
{
    /// <summary>
    /// Releases escrowed assets to recipients.
    /// Uses optimistic concurrency (ETag) to prevent concurrent state transitions.
    /// Release behavior depends on ReleaseMode:
    /// - Immediate/ServiceOnly: Transitions directly to Released
    /// - PartyRequired/ServiceAndParty: Transitions to Releasing with confirmation deadline
    /// </summary>
    public async Task<(StatusCodes, ReleaseResponse?)> ReleaseAsync(
        ReleaseRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var agreementKey = GetAgreementKey(body.EscrowId);

            for (var attempt = 0; attempt < _configuration.MaxConcurrencyRetries; attempt++)
            {
                var (agreementModel, etag) = await AgreementStore.GetWithETagAsync(agreementKey, cancellationToken);

                if (agreementModel == null)
                {
                    return (StatusCodes.NotFound, null);
                }

                var validReleaseStates = new HashSet<EscrowStatus>
                {
                    EscrowStatus.Finalizing,
                    EscrowStatus.Releasing
                };

                if (!validReleaseStates.Contains(agreementModel.Status))
                {
                    return (StatusCodes.BadRequest, null);
                }

                var now = DateTimeOffset.UtcNow;
                var previousStatus = agreementModel.Status;

                // Check if release mode requires party confirmations
                var requiresPartyConfirmation = agreementModel.ReleaseMode == ReleaseMode.PartyRequired ||
                                                agreementModel.ReleaseMode == ReleaseMode.ServiceAndParty;

                // If coming from Finalizing and party confirmation required, transition to Releasing
                if (previousStatus == EscrowStatus.Finalizing && requiresPartyConfirmation)
                {
                    agreementModel.Status = EscrowStatus.Releasing;
                    agreementModel.ConfirmationDeadline = now.AddSeconds(_configuration.ConfirmationTimeoutSeconds);

                    // Initialize release confirmations for all recipient parties
                    agreementModel.ReleaseConfirmations = agreementModel.ReleaseAllocations?
                        .Select(a => new ReleaseConfirmationModel
                        {
                            PartyId = a.RecipientPartyId,
                            PartyType = a.RecipientPartyType,
                            ServiceConfirmed = agreementModel.ReleaseMode == ReleaseMode.PartyRequired, // Auto-confirm service for PartyRequired
                            PartyConfirmed = false,
                            ServiceConfirmedAt = agreementModel.ReleaseMode == ReleaseMode.PartyRequired ? now : null
                        }).ToList() ?? new List<ReleaseConfirmationModel>();

                    var initiationSaveResult = await AgreementStore.TrySaveAsync(agreementKey, agreementModel, etag ?? string.Empty, cancellationToken);
                    if (initiationSaveResult == null)
                    {
                        _logger.LogDebug("Concurrent modification during release initiation for escrow {EscrowId}, retrying (attempt {Attempt})",
                            body.EscrowId, attempt + 1);
                        continue;
                    }

                    // Update status index
                    var initiationOldStatusKey = $"{GetStatusIndexKey(previousStatus)}:{body.EscrowId}";
                    await StatusIndexStore.DeleteAsync(initiationOldStatusKey, cancellationToken);

                    var initiationNewStatusKey = $"{GetStatusIndexKey(EscrowStatus.Releasing)}:{body.EscrowId}";
                    var initiationStatusEntry = new StatusIndexEntry
                    {
                        EscrowId = body.EscrowId,
                        Status = EscrowStatus.Releasing,
                        ExpiresAt = agreementModel.ExpiresAt,
                        AddedAt = now
                    };
                    await StatusIndexStore.SaveAsync(initiationNewStatusKey, initiationStatusEntry, cancellationToken: cancellationToken);

                    // Publish releasing event with allocation details
                    var releasingEvent = new EscrowReleasingEvent
                    {
                        EventId = Guid.NewGuid(),
                        Timestamp = now,
                        EscrowId = body.EscrowId,
                        ReleaseMode = agreementModel.ReleaseMode,
                        ConfirmationDeadline = agreementModel.ConfirmationDeadline,
                        BoundContractId = agreementModel.BoundContractId,
                        Allocations = (agreementModel.ReleaseAllocations ?? new List<ReleaseAllocationModel>())
                            .Select(a => new ReleaseAllocationWithConfirmation
                            {
                                RecipientPartyId = a.RecipientPartyId,
                                RecipientPartyType = a.RecipientPartyType,
                                Assets = (a.Assets ?? new List<EscrowAssetModel>()).Select(MapAssetToApiModel).ToList(),
                                DestinationWalletId = a.DestinationWalletId,
                                DestinationContainerId = a.DestinationContainerId
                            }).ToList()
                    };
                    await _messageBus.TryPublishAsync(EscrowTopics.EscrowReleasing, releasingEvent, cancellationToken);

                    _logger.LogInformation("Escrow {EscrowId} transitioning to Releasing, awaiting {Count} party confirmations, deadline: {Deadline}",
                        body.EscrowId, agreementModel.ReleaseConfirmations.Count, agreementModel.ConfirmationDeadline);

                    return (StatusCodes.OK, new ReleaseResponse
                    {
                        Escrow = MapToApiModel(agreementModel),
                        FinalizerResults = new List<FinalizerResult>(),
                        Releases = new List<ReleaseResult>() // No releases yet - awaiting confirmations
                    });
                }

                // Direct release (Immediate/ServiceOnly mode, or already in Releasing with all confirmations)
                // Process release allocations
                var releases = new List<ReleaseResult>();
                if (agreementModel.ReleaseAllocations != null)
                {
                    foreach (var allocation in agreementModel.ReleaseAllocations)
                    {
                        var bundle = new EscrowAssetBundle
                        {
                            BundleId = Guid.NewGuid(),
                            Assets = allocation.Assets?.Select(MapAssetToApiModel).ToList()
                                ?? new List<EscrowAsset>()
                        };

                        releases.Add(new ReleaseResult
                        {
                            RecipientPartyId = allocation.RecipientPartyId,
                            Assets = bundle,
                            Success = true
                        });
                    }
                }

                agreementModel.Status = EscrowStatus.Released;
                agreementModel.Resolution = EscrowResolution.Released;
                agreementModel.CompletedAt = now;
                agreementModel.ResolutionNotes = body.Notes;

                var saveResult = await AgreementStore.TrySaveAsync(agreementKey, agreementModel, etag ?? string.Empty, cancellationToken);
                if (saveResult == null)
                {
                    _logger.LogDebug("Concurrent modification during release for escrow {EscrowId}, retrying (attempt {Attempt})",
                        body.EscrowId, attempt + 1);
                    continue;
                }

                // Agreement saved successfully - update secondary stores
                var oldStatusKey = $"{GetStatusIndexKey(previousStatus)}:{body.EscrowId}";
                await StatusIndexStore.DeleteAsync(oldStatusKey, cancellationToken);

                var newStatusKey = $"{GetStatusIndexKey(EscrowStatus.Released)}:{body.EscrowId}";
                var statusEntry = new StatusIndexEntry
                {
                    EscrowId = body.EscrowId,
                    Status = EscrowStatus.Released,
                    ExpiresAt = agreementModel.ExpiresAt,
                    AddedAt = now
                };
                await StatusIndexStore.SaveAsync(newStatusKey, statusEntry, cancellationToken: cancellationToken);

                // Decrement pending counts for all parties
                foreach (var party in agreementModel.Parties ?? new List<EscrowPartyModel>())
                {
                    await DecrementPartyPendingCountAsync(party.PartyId, party.PartyType, cancellationToken);
                }

                // Publish release event
                var releaseEvent = new EscrowReleasedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = now,
                    EscrowId = body.EscrowId,
                    // releases are built from ReleaseAllocations on same model; FirstOrDefault
                    // always finds the match. The null-coalesce satisfies the compiler but will never execute.
                    Recipients = releases.Select(r => new RecipientInfo
                    {
                        PartyId = r.RecipientPartyId,
                        PartyType = agreementModel.ReleaseAllocations?
                            .FirstOrDefault(a => a.RecipientPartyId == r.RecipientPartyId)?.RecipientPartyType ?? default,
                        AssetSummary = GenerateAssetSummary(
                            agreementModel.ReleaseAllocations?
                                .FirstOrDefault(a => a.RecipientPartyId == r.RecipientPartyId)?.Assets)
                    }).ToList(),
                    Resolution = EscrowResolution.Released,
                    CompletedAt = now
                };
                await _messageBus.TryPublishAsync(EscrowTopics.EscrowReleased, releaseEvent, cancellationToken);

                _logger.LogInformation("Escrow {EscrowId} released with {ReleaseCount} transfers",
                    body.EscrowId, releases.Count);

                return (StatusCodes.OK, new ReleaseResponse
                {
                    Escrow = MapToApiModel(agreementModel),
                    FinalizerResults = new List<FinalizerResult>(),
                    Releases = releases
                });
            }

            _logger.LogWarning("Failed to release escrow {EscrowId} after {MaxRetries} attempts due to concurrent modifications",
                body.EscrowId, _configuration.MaxConcurrencyRetries);
            return (StatusCodes.Conflict, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to release escrow {EscrowId}", body.EscrowId);
            await EmitErrorAsync("Release", ex.Message, new { body.EscrowId }, cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Refunds escrowed assets to depositors.
    /// Uses optimistic concurrency (ETag) to prevent concurrent state transitions.
    /// </summary>
    public async Task<(StatusCodes, RefundResponse?)> RefundAsync(
        RefundRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var agreementKey = GetAgreementKey(body.EscrowId);

            for (var attempt = 0; attempt < _configuration.MaxConcurrencyRetries; attempt++)
            {
                var (agreementModel, etag) = await AgreementStore.GetWithETagAsync(agreementKey, cancellationToken);

                if (agreementModel == null)
                {
                    return (StatusCodes.NotFound, null);
                }

                var validRefundStates = new HashSet<EscrowStatus>
                {
                    EscrowStatus.Refunding,
                    EscrowStatus.ValidationFailed,
                    EscrowStatus.Disputed,
                    EscrowStatus.PartiallyFunded,
                    EscrowStatus.PendingDeposits
                };

                if (!validRefundStates.Contains(agreementModel.Status))
                {
                    return (StatusCodes.BadRequest, null);
                }

                var now = DateTimeOffset.UtcNow;
                var previousStatus = agreementModel.Status;

                // Build refund results from deposits
                var refunds = new List<RefundResult>();
                foreach (var deposit in agreementModel.Deposits ?? new List<EscrowDepositModel>())
                {
                    refunds.Add(new RefundResult
                    {
                        DepositorPartyId = deposit.PartyId,
                        Assets = MapAssetBundleToApiModel(deposit.Assets),
                        Success = true
                    });
                }

                agreementModel.Status = EscrowStatus.Refunded;
                agreementModel.Resolution = EscrowResolution.Refunded;
                agreementModel.CompletedAt = now;
                agreementModel.ResolutionNotes = body.Reason;

                var saveResult = await AgreementStore.TrySaveAsync(agreementKey, agreementModel, etag ?? string.Empty, cancellationToken);
                if (saveResult == null)
                {
                    _logger.LogDebug("Concurrent modification during refund for escrow {EscrowId}, retrying (attempt {Attempt})",
                        body.EscrowId, attempt + 1);
                    continue;
                }

                // Agreement saved successfully - update secondary stores
                var oldStatusKey = $"{GetStatusIndexKey(previousStatus)}:{body.EscrowId}";
                await StatusIndexStore.DeleteAsync(oldStatusKey, cancellationToken);

                var newStatusKey = $"{GetStatusIndexKey(EscrowStatus.Refunded)}:{body.EscrowId}";
                var statusEntry = new StatusIndexEntry
                {
                    EscrowId = body.EscrowId,
                    Status = EscrowStatus.Refunded,
                    ExpiresAt = agreementModel.ExpiresAt,
                    AddedAt = now
                };
                await StatusIndexStore.SaveAsync(newStatusKey, statusEntry, cancellationToken: cancellationToken);

                // Decrement pending counts
                foreach (var party in agreementModel.Parties ?? new List<EscrowPartyModel>())
                {
                    await DecrementPartyPendingCountAsync(party.PartyId, party.PartyType, cancellationToken);
                }

                // Publish refund event
                // refunds are built from Deposits on same model; FirstOrDefault always finds the match.
                // The null-coalesce satisfies the compiler but will never execute.
                var refundEvent = new EscrowRefundedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = now,
                    EscrowId = body.EscrowId,
                    Depositors = refunds.Select(r => new DepositorInfo
                    {
                        PartyId = r.DepositorPartyId,
                        PartyType = agreementModel.Deposits?
                            .FirstOrDefault(d => d.PartyId == r.DepositorPartyId)?.PartyType ?? default,
                        AssetSummary = GenerateAssetSummary(
                            agreementModel.Deposits?
                                .FirstOrDefault(d => d.PartyId == r.DepositorPartyId)?.Assets?.Assets)
                    }).ToList(),
                    Reason = body.Reason,
                    Resolution = EscrowResolution.Refunded,
                    CompletedAt = now
                };
                await _messageBus.TryPublishAsync(EscrowTopics.EscrowRefunded, refundEvent, cancellationToken);

                _logger.LogInformation("Escrow {EscrowId} refunded with {RefundCount} refunds",
                    body.EscrowId, refunds.Count);

                return (StatusCodes.OK, new RefundResponse
                {
                    Escrow = MapToApiModel(agreementModel),
                    Refunds = refunds
                });
            }

            _logger.LogWarning("Failed to refund escrow {EscrowId} after {MaxRetries} attempts due to concurrent modifications",
                body.EscrowId, _configuration.MaxConcurrencyRetries);
            return (StatusCodes.Conflict, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refund escrow {EscrowId}", body.EscrowId);
            await EmitErrorAsync("Refund", ex.Message, new { body.EscrowId }, cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Cancels an escrow that hasn't been fully funded.
    /// Uses optimistic concurrency (ETag) to prevent concurrent state transitions.
    /// </summary>
    public async Task<(StatusCodes, CancelResponse?)> CancelAsync(
        CancelRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var agreementKey = GetAgreementKey(body.EscrowId);

            for (var attempt = 0; attempt < _configuration.MaxConcurrencyRetries; attempt++)
            {
                var (agreementModel, etag) = await AgreementStore.GetWithETagAsync(agreementKey, cancellationToken);

                if (agreementModel == null)
                {
                    return (StatusCodes.NotFound, null);
                }

                var validCancelStates = new HashSet<EscrowStatus>
                {
                    EscrowStatus.PendingDeposits,
                    EscrowStatus.PartiallyFunded
                };

                if (!validCancelStates.Contains(agreementModel.Status))
                {
                    return (StatusCodes.BadRequest, null);
                }

                var now = DateTimeOffset.UtcNow;
                var previousStatus = agreementModel.Status;

                // Build refund results for any partial deposits
                var refunds = new List<RefundResult>();
                foreach (var deposit in agreementModel.Deposits ?? new List<EscrowDepositModel>())
                {
                    refunds.Add(new RefundResult
                    {
                        DepositorPartyId = deposit.PartyId,
                        Assets = MapAssetBundleToApiModel(deposit.Assets),
                        Success = true
                    });
                }

                agreementModel.Status = EscrowStatus.Cancelled;
                agreementModel.Resolution = EscrowResolution.CancelledRefunded;
                agreementModel.CompletedAt = now;
                agreementModel.ResolutionNotes = body.Reason;

                var saveResult = await AgreementStore.TrySaveAsync(agreementKey, agreementModel, etag ?? string.Empty, cancellationToken);
                if (saveResult == null)
                {
                    _logger.LogDebug("Concurrent modification during cancel for escrow {EscrowId}, retrying (attempt {Attempt})",
                        body.EscrowId, attempt + 1);
                    continue;
                }

                // Agreement saved successfully - update secondary stores
                var oldStatusKey = $"{GetStatusIndexKey(previousStatus)}:{body.EscrowId}";
                await StatusIndexStore.DeleteAsync(oldStatusKey, cancellationToken);

                var newStatusKey = $"{GetStatusIndexKey(EscrowStatus.Cancelled)}:{body.EscrowId}";
                var statusEntry = new StatusIndexEntry
                {
                    EscrowId = body.EscrowId,
                    Status = EscrowStatus.Cancelled,
                    ExpiresAt = agreementModel.ExpiresAt,
                    AddedAt = now
                };
                await StatusIndexStore.SaveAsync(newStatusKey, statusEntry, cancellationToken: cancellationToken);

                // Decrement pending counts
                foreach (var party in agreementModel.Parties ?? new List<EscrowPartyModel>())
                {
                    await DecrementPartyPendingCountAsync(party.PartyId, party.PartyType, cancellationToken);
                }

                // Publish cancel event
                var cancelEvent = new EscrowCancelledEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = now,
                    EscrowId = body.EscrowId,
                    Reason = body.Reason,
                    DepositsRefunded = refunds.Count,
                    CancelledAt = now
                };
                await _messageBus.TryPublishAsync(EscrowTopics.EscrowCancelled, cancelEvent, cancellationToken);

                _logger.LogInformation("Escrow {EscrowId} cancelled, {RefundCount} deposits refunded",
                    body.EscrowId, refunds.Count);

                return (StatusCodes.OK, new CancelResponse
                {
                    Escrow = MapToApiModel(agreementModel),
                    Refunds = refunds
                });
            }

            _logger.LogWarning("Failed to cancel escrow {EscrowId} after {MaxRetries} attempts due to concurrent modifications",
                body.EscrowId, _configuration.MaxConcurrencyRetries);
            return (StatusCodes.Conflict, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel escrow {EscrowId}", body.EscrowId);
            await EmitErrorAsync("Cancel", ex.Message, new { body.EscrowId }, cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Raises a dispute on an escrow.
    /// Uses optimistic concurrency (ETag) to prevent concurrent state transitions.
    /// </summary>
    public async Task<(StatusCodes, DisputeResponse?)> DisputeAsync(
        DisputeRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var agreementKey = GetAgreementKey(body.EscrowId);

            for (var attempt = 0; attempt < _configuration.MaxConcurrencyRetries; attempt++)
            {
                var (agreementModel, etag) = await AgreementStore.GetWithETagAsync(agreementKey, cancellationToken);

                if (agreementModel == null)
                {
                    return (StatusCodes.NotFound, null);
                }

                var validDisputeStates = new HashSet<EscrowStatus>
                {
                    EscrowStatus.Funded,
                    EscrowStatus.PendingConsent,
                    EscrowStatus.PendingCondition,
                    EscrowStatus.Finalizing
                };

                if (!validDisputeStates.Contains(agreementModel.Status))
                {
                    return (StatusCodes.BadRequest, null);
                }

                var disputerParty = agreementModel.Parties?.FirstOrDefault(p =>
                    p.PartyId == body.PartyId && p.PartyType == body.PartyType);

                if (disputerParty == null)
                {
                    return (StatusCodes.Forbidden, null);
                }

                var now = DateTimeOffset.UtcNow;
                var previousStatus = agreementModel.Status;

                agreementModel.Status = EscrowStatus.Disputed;

                agreementModel.Consents ??= new List<EscrowConsentModel>();
                agreementModel.Consents.Add(new EscrowConsentModel
                {
                    PartyId = body.PartyId,
                    PartyType = body.PartyType,
                    ConsentType = EscrowConsentType.Dispute,
                    ConsentedAt = now,
                    Notes = body.Reason
                });

                var saveResult = await AgreementStore.TrySaveAsync(agreementKey, agreementModel, etag ?? string.Empty, cancellationToken);
                if (saveResult == null)
                {
                    _logger.LogDebug("Concurrent modification during dispute for escrow {EscrowId}, retrying (attempt {Attempt})",
                        body.EscrowId, attempt + 1);
                    continue;
                }

                // Agreement saved successfully - update secondary stores
                var oldStatusKey = $"{GetStatusIndexKey(previousStatus)}:{body.EscrowId}";
                await StatusIndexStore.DeleteAsync(oldStatusKey, cancellationToken);

                var newStatusKey = $"{GetStatusIndexKey(EscrowStatus.Disputed)}:{body.EscrowId}";
                var statusEntry = new StatusIndexEntry
                {
                    EscrowId = body.EscrowId,
                    Status = EscrowStatus.Disputed,
                    ExpiresAt = agreementModel.ExpiresAt,
                    AddedAt = now
                };
                await StatusIndexStore.SaveAsync(newStatusKey, statusEntry, cancellationToken: cancellationToken);

                // Publish dispute event
                var disputeEvent = new EscrowDisputedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = now,
                    EscrowId = body.EscrowId,
                    DisputedBy = body.PartyId,
                    DisputedByType = body.PartyType,
                    Reason = body.Reason,
                    DisputedAt = now
                };
                await _messageBus.TryPublishAsync(EscrowTopics.EscrowDisputed, disputeEvent, cancellationToken);

                _logger.LogInformation("Dispute raised on escrow {EscrowId} by {PartyId}",
                    body.EscrowId, body.PartyId);

                return (StatusCodes.OK, new DisputeResponse
                {
                    Escrow = MapToApiModel(agreementModel)
                });
            }

            _logger.LogWarning("Failed to dispute escrow {EscrowId} after {MaxRetries} attempts due to concurrent modifications",
                body.EscrowId, _configuration.MaxConcurrencyRetries);
            return (StatusCodes.Conflict, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispute escrow {EscrowId}", body.EscrowId);
            await EmitErrorAsync("Dispute", ex.Message, new { body.EscrowId }, cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Resolves a disputed escrow (arbiter action).
    /// Uses optimistic concurrency (ETag) to prevent concurrent state transitions.
    /// </summary>
    public async Task<(StatusCodes, ResolveResponse?)> ResolveAsync(
        ResolveRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var agreementKey = GetAgreementKey(body.EscrowId);

            for (var attempt = 0; attempt < _configuration.MaxConcurrencyRetries; attempt++)
            {
                var (agreementModel, etag) = await AgreementStore.GetWithETagAsync(agreementKey, cancellationToken);

                if (agreementModel == null)
                {
                    return (StatusCodes.NotFound, null);
                }

                if (agreementModel.Status != EscrowStatus.Disputed)
                {
                    return (StatusCodes.BadRequest, null);
                }

                // Verify the arbiter is a party with arbiter role
                var arbiterParty = agreementModel.Parties?.FirstOrDefault(p =>
                    p.PartyId == body.ArbiterId && p.PartyType == body.ArbiterType);

                if (arbiterParty == null || arbiterParty.Role != EscrowPartyRole.Arbiter)
                {
                    return (StatusCodes.Forbidden, null);
                }

                var now = DateTimeOffset.UtcNow;
                var transfers = new List<TransferResult>();

                switch (body.Resolution)
                {
                    case EscrowResolution.Released:
                        agreementModel.Status = EscrowStatus.Released;
                        if (agreementModel.ReleaseAllocations != null)
                        {
                            foreach (var allocation in agreementModel.ReleaseAllocations)
                            {
                                transfers.Add(new TransferResult
                                {
                                    PartyId = allocation.RecipientPartyId,
                                    Assets = new EscrowAssetBundle
                                    {
                                        BundleId = Guid.NewGuid(),
                                        Assets = allocation.Assets?.Select(MapAssetToApiModel).ToList()
                                            ?? new List<EscrowAsset>()
                                    },
                                    Success = true
                                });
                            }
                        }
                        break;

                    case EscrowResolution.Refunded:
                        agreementModel.Status = EscrowStatus.Refunded;
                        foreach (var deposit in agreementModel.Deposits ?? new List<EscrowDepositModel>())
                        {
                            transfers.Add(new TransferResult
                            {
                                PartyId = deposit.PartyId,
                                Assets = MapAssetBundleToApiModel(deposit.Assets),
                                Success = true
                            });
                        }
                        break;

                    case EscrowResolution.Split:
                        agreementModel.Status = EscrowStatus.Released;
                        if (body.SplitAllocations != null)
                        {
                            foreach (var allocation in body.SplitAllocations)
                            {
                                var assetModels = allocation.Assets?.Select(MapAssetInputToModel).ToList()
                                    ?? new List<EscrowAssetModel>();

                                transfers.Add(new TransferResult
                                {
                                    PartyId = allocation.PartyId,
                                    Assets = new EscrowAssetBundle
                                    {
                                        BundleId = Guid.NewGuid(),
                                        Assets = assetModels.Select(MapAssetToApiModel).ToList()
                                    },
                                    Success = true
                                });
                            }
                        }
                        break;

                    default:
                        agreementModel.Status = EscrowStatus.Refunded;
                        break;
                }

                agreementModel.Resolution = body.Resolution;
                agreementModel.CompletedAt = now;
                agreementModel.ResolutionNotes = body.Notes;

                var saveResult = await AgreementStore.TrySaveAsync(agreementKey, agreementModel, etag ?? string.Empty, cancellationToken);
                if (saveResult == null)
                {
                    _logger.LogDebug("Concurrent modification during resolve for escrow {EscrowId}, retrying (attempt {Attempt})",
                        body.EscrowId, attempt + 1);
                    continue;
                }

                // Agreement saved successfully - update secondary stores
                var oldStatusKey = $"{GetStatusIndexKey(EscrowStatus.Disputed)}:{body.EscrowId}";
                await StatusIndexStore.DeleteAsync(oldStatusKey, cancellationToken);

                var newStatusKey = $"{GetStatusIndexKey(agreementModel.Status)}:{body.EscrowId}";
                var statusEntry = new StatusIndexEntry
                {
                    EscrowId = body.EscrowId,
                    Status = agreementModel.Status,
                    ExpiresAt = agreementModel.ExpiresAt,
                    AddedAt = now
                };
                await StatusIndexStore.SaveAsync(newStatusKey, statusEntry, cancellationToken: cancellationToken);

                // Decrement pending counts
                foreach (var party in agreementModel.Parties ?? new List<EscrowPartyModel>())
                {
                    await DecrementPartyPendingCountAsync(party.PartyId, party.PartyType, cancellationToken);
                }

                // Publish resolve event
                var resolveEvent = new EscrowResolvedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = now,
                    EscrowId = body.EscrowId,
                    ArbiterId = body.ArbiterId,
                    ArbiterType = body.ArbiterType,
                    Resolution = body.Resolution,
                    Notes = body.Notes,
                    ResolvedAt = now
                };
                await _messageBus.TryPublishAsync(EscrowTopics.EscrowResolved, resolveEvent, cancellationToken);

                _logger.LogInformation("Escrow {EscrowId} resolved with {Resolution}",
                    body.EscrowId, body.Resolution);

                return (StatusCodes.OK, new ResolveResponse
                {
                    Escrow = MapToApiModel(agreementModel),
                    Transfers = transfers
                });
            }

            _logger.LogWarning("Failed to resolve escrow {EscrowId} after {MaxRetries} attempts due to concurrent modifications",
                body.EscrowId, _configuration.MaxConcurrencyRetries);
            return (StatusCodes.Conflict, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve escrow {EscrowId}", body.EscrowId);
            await EmitErrorAsync("Resolve", ex.Message, new { body.EscrowId }, cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Confirms receipt of released assets by a party.
    /// Required when ReleaseMode is party_required or service_and_party.
    /// Uses optimistic concurrency (ETag) to prevent concurrent state transitions.
    /// </summary>
    public async Task<(StatusCodes, ConfirmReleaseResponse?)> ConfirmReleaseAsync(
        ConfirmReleaseRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var agreementKey = GetAgreementKey(body.EscrowId);

            for (var attempt = 0; attempt < _configuration.MaxConcurrencyRetries; attempt++)
            {
                var (agreementModel, etag) = await AgreementStore.GetWithETagAsync(agreementKey, cancellationToken);

                if (agreementModel == null)
                {
                    return (StatusCodes.NotFound, null);
                }

                if (agreementModel.Status != EscrowStatus.Releasing)
                {
                    _logger.LogWarning("Cannot confirm release for escrow {EscrowId}: status is {Status}, expected Releasing",
                        body.EscrowId, agreementModel.Status);
                    return (StatusCodes.Conflict, null);
                }

                // Validate release token
                var party = agreementModel.Parties?.FirstOrDefault(p => p.PartyId == body.PartyId);
                if (party == null)
                {
                    _logger.LogWarning("Party {PartyId} not found in escrow {EscrowId}",
                        body.PartyId, body.EscrowId);
                    return (StatusCodes.NotFound, null);
                }

                if (party.ReleaseToken != body.ReleaseToken)
                {
                    _logger.LogWarning("Invalid release token for party {PartyId} in escrow {EscrowId}",
                        body.PartyId, body.EscrowId);
                    return (StatusCodes.Forbidden, null);
                }

                // Find and update confirmation record
                var confirmation = agreementModel.ReleaseConfirmations?
                    .FirstOrDefault(c => c.PartyId == body.PartyId);
                if (confirmation == null)
                {
                    _logger.LogWarning("No confirmation record for party {PartyId} in escrow {EscrowId}",
                        body.PartyId, body.EscrowId);
                    return (StatusCodes.BadRequest, null);
                }

                if (confirmation.PartyConfirmed)
                {
                    // Already confirmed - return success idempotently
                    return (StatusCodes.OK, new ConfirmReleaseResponse
                    {
                        EscrowId = body.EscrowId,
                        Confirmed = true,
                        AllPartiesConfirmed = CheckAllConfirmationsComplete(agreementModel),
                        Status = agreementModel.Status
                    });
                }

                var now = DateTimeOffset.UtcNow;
                confirmation.PartyConfirmed = true;
                confirmation.PartyConfirmedAt = now;

                // Check if all required confirmations are complete
                var allConfirmed = CheckAllConfirmationsComplete(agreementModel);
                var previousStatus = agreementModel.Status;

                if (allConfirmed)
                {
                    agreementModel.Status = EscrowStatus.Released;
                    agreementModel.CompletedAt = now;
                    agreementModel.Resolution = EscrowResolution.Released;
                }

                var saveResult = await AgreementStore.TrySaveAsync(agreementKey, agreementModel, etag ?? string.Empty, cancellationToken);
                if (saveResult == null)
                {
                    _logger.LogDebug("Concurrent modification during confirm release for escrow {EscrowId}, retrying (attempt {Attempt})",
                        body.EscrowId, attempt + 1);
                    continue;
                }

                if (allConfirmed)
                {
                    // Update status index
                    var oldStatusKey = $"{GetStatusIndexKey(previousStatus)}:{body.EscrowId}";
                    await StatusIndexStore.DeleteAsync(oldStatusKey, cancellationToken);

                    var newStatusKey = $"{GetStatusIndexKey(EscrowStatus.Released)}:{body.EscrowId}";
                    var statusEntry = new StatusIndexEntry
                    {
                        EscrowId = body.EscrowId,
                        Status = EscrowStatus.Released,
                        ExpiresAt = agreementModel.ExpiresAt,
                        AddedAt = now
                    };
                    await StatusIndexStore.SaveAsync(newStatusKey, statusEntry, cancellationToken: cancellationToken);

                    // Decrement pending counts
                    foreach (var p in agreementModel.Parties ?? new List<EscrowPartyModel>())
                    {
                        await DecrementPartyPendingCountAsync(p.PartyId, p.PartyType, cancellationToken);
                    }

                    // Publish released event
                    await PublishReleasedEventAsync(agreementModel, now, cancellationToken);

                    _logger.LogInformation("Escrow {EscrowId} fully released after all party confirmations",
                        body.EscrowId);
                }
                else
                {
                    _logger.LogInformation("Party {PartyId} confirmed release for escrow {EscrowId}, awaiting other confirmations",
                        body.PartyId, body.EscrowId);
                }

                return (StatusCodes.OK, new ConfirmReleaseResponse
                {
                    EscrowId = body.EscrowId,
                    Confirmed = true,
                    AllPartiesConfirmed = allConfirmed,
                    Status = agreementModel.Status
                });
            }

            _logger.LogWarning("Failed to confirm release for escrow {EscrowId} after {MaxRetries} attempts",
                body.EscrowId, _configuration.MaxConcurrencyRetries);
            return (StatusCodes.Conflict, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to confirm release for escrow {EscrowId}", body.EscrowId);
            await EmitErrorAsync("ConfirmRelease", ex.Message, new { body.EscrowId, body.PartyId }, cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Confirms receipt of refunded assets by a party.
    /// Required when RefundMode is party_required.
    /// Uses optimistic concurrency (ETag) to prevent concurrent state transitions.
    /// </summary>
    public async Task<(StatusCodes, ConfirmRefundResponse?)> ConfirmRefundAsync(
        ConfirmRefundRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var agreementKey = GetAgreementKey(body.EscrowId);

            for (var attempt = 0; attempt < _configuration.MaxConcurrencyRetries; attempt++)
            {
                var (agreementModel, etag) = await AgreementStore.GetWithETagAsync(agreementKey, cancellationToken);

                if (agreementModel == null)
                {
                    return (StatusCodes.NotFound, null);
                }

                if (agreementModel.Status != EscrowStatus.Refunding)
                {
                    _logger.LogWarning("Cannot confirm refund for escrow {EscrowId}: status is {Status}, expected Refunding",
                        body.EscrowId, agreementModel.Status);
                    return (StatusCodes.Conflict, null);
                }

                // Verify party is a depositor
                var party = agreementModel.Parties?.FirstOrDefault(p => p.PartyId == body.PartyId);
                if (party == null)
                {
                    _logger.LogWarning("Party {PartyId} not found in escrow {EscrowId}",
                        body.PartyId, body.EscrowId);
                    return (StatusCodes.NotFound, null);
                }

                // Find and update confirmation record
                var confirmation = agreementModel.ReleaseConfirmations?
                    .FirstOrDefault(c => c.PartyId == body.PartyId);
                if (confirmation == null)
                {
                    _logger.LogWarning("No confirmation record for party {PartyId} in escrow {EscrowId}",
                        body.PartyId, body.EscrowId);
                    return (StatusCodes.BadRequest, null);
                }

                if (confirmation.PartyConfirmed)
                {
                    // Already confirmed - return success idempotently
                    return (StatusCodes.OK, new ConfirmRefundResponse
                    {
                        EscrowId = body.EscrowId,
                        Confirmed = true,
                        AllPartiesConfirmed = CheckAllRefundConfirmationsComplete(agreementModel),
                        Status = agreementModel.Status
                    });
                }

                var now = DateTimeOffset.UtcNow;
                confirmation.PartyConfirmed = true;
                confirmation.PartyConfirmedAt = now;

                // Check if all required confirmations are complete
                var allConfirmed = CheckAllRefundConfirmationsComplete(agreementModel);
                var previousStatus = agreementModel.Status;

                if (allConfirmed)
                {
                    agreementModel.Status = EscrowStatus.Refunded;
                    agreementModel.CompletedAt = now;
                    agreementModel.Resolution = EscrowResolution.Refunded;
                }

                var saveResult = await AgreementStore.TrySaveAsync(agreementKey, agreementModel, etag ?? string.Empty, cancellationToken);
                if (saveResult == null)
                {
                    _logger.LogDebug("Concurrent modification during confirm refund for escrow {EscrowId}, retrying (attempt {Attempt})",
                        body.EscrowId, attempt + 1);
                    continue;
                }

                if (allConfirmed)
                {
                    // Update status index
                    var oldStatusKey = $"{GetStatusIndexKey(previousStatus)}:{body.EscrowId}";
                    await StatusIndexStore.DeleteAsync(oldStatusKey, cancellationToken);

                    var newStatusKey = $"{GetStatusIndexKey(EscrowStatus.Refunded)}:{body.EscrowId}";
                    var statusEntry = new StatusIndexEntry
                    {
                        EscrowId = body.EscrowId,
                        Status = EscrowStatus.Refunded,
                        ExpiresAt = agreementModel.ExpiresAt,
                        AddedAt = now
                    };
                    await StatusIndexStore.SaveAsync(newStatusKey, statusEntry, cancellationToken: cancellationToken);

                    // Decrement pending counts
                    foreach (var p in agreementModel.Parties ?? new List<EscrowPartyModel>())
                    {
                        await DecrementPartyPendingCountAsync(p.PartyId, p.PartyType, cancellationToken);
                    }

                    // Publish refunded event
                    await PublishRefundedEventAsync(agreementModel, now, cancellationToken);

                    _logger.LogInformation("Escrow {EscrowId} fully refunded after all party confirmations",
                        body.EscrowId);
                }
                else
                {
                    _logger.LogInformation("Party {PartyId} confirmed refund for escrow {EscrowId}, awaiting other confirmations",
                        body.PartyId, body.EscrowId);
                }

                return (StatusCodes.OK, new ConfirmRefundResponse
                {
                    EscrowId = body.EscrowId,
                    Confirmed = true,
                    AllPartiesConfirmed = allConfirmed,
                    Status = agreementModel.Status
                });
            }

            _logger.LogWarning("Failed to confirm refund for escrow {EscrowId} after {MaxRetries} attempts",
                body.EscrowId, _configuration.MaxConcurrencyRetries);
            return (StatusCodes.Conflict, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to confirm refund for escrow {EscrowId}", body.EscrowId);
            await EmitErrorAsync("ConfirmRefund", ex.Message, new { body.EscrowId, body.PartyId }, cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #region Helper Methods for Confirmation

    /// <summary>
    /// Checks if all release confirmations are complete based on ReleaseMode.
    /// </summary>
    private static bool CheckAllConfirmationsComplete(EscrowAgreementModel agreement)
    {
        if (agreement.ReleaseConfirmations == null || !agreement.ReleaseConfirmations.Any())
        {
            return true;
        }

        return agreement.ReleaseMode switch
        {
            ReleaseMode.ServiceOnly => agreement.ReleaseConfirmations.All(c => c.ServiceConfirmed),
            ReleaseMode.PartyRequired => agreement.ReleaseConfirmations.All(c => c.PartyConfirmed),
            ReleaseMode.ServiceAndParty => agreement.ReleaseConfirmations.All(c => c.ServiceConfirmed && c.PartyConfirmed),
            _ => true // Immediate mode should not reach here
        };
    }

    /// <summary>
    /// Checks if all refund confirmations are complete based on RefundMode.
    /// </summary>
    private static bool CheckAllRefundConfirmationsComplete(EscrowAgreementModel agreement)
    {
        if (agreement.ReleaseConfirmations == null || !agreement.ReleaseConfirmations.Any())
        {
            return true;
        }

        return agreement.RefundMode switch
        {
            RefundMode.ServiceOnly => agreement.ReleaseConfirmations.All(c => c.ServiceConfirmed),
            RefundMode.PartyRequired => agreement.ReleaseConfirmations.All(c => c.PartyConfirmed),
            _ => true // Immediate mode should not reach here
        };
    }

    /// <summary>
    /// Publishes the EscrowReleasedEvent.
    /// </summary>
    private async Task PublishReleasedEventAsync(EscrowAgreementModel agreementModel, DateTimeOffset timestamp, CancellationToken cancellationToken)
    {
        var releaseEvent = new EscrowReleasedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = timestamp,
            EscrowId = agreementModel.EscrowId,
            Recipients = agreementModel.ReleaseAllocations?.Select(a => new RecipientInfo
            {
                PartyId = a.RecipientPartyId,
                PartyType = a.RecipientPartyType,
                AssetSummary = GenerateAssetSummary(a.Assets)
            }).ToList() ?? new List<RecipientInfo>(),
            Resolution = EscrowResolution.Released,
            CompletedAt = timestamp
        };
        await _messageBus.TryPublishAsync(EscrowTopics.EscrowReleased, releaseEvent, cancellationToken);
    }

    /// <summary>
    /// Publishes the EscrowRefundedEvent.
    /// </summary>
    private async Task PublishRefundedEventAsync(EscrowAgreementModel agreementModel, DateTimeOffset timestamp, CancellationToken cancellationToken)
    {
        var refundEvent = new EscrowRefundedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = timestamp,
            EscrowId = agreementModel.EscrowId,
            Depositors = agreementModel.Deposits?.Select(d => new DepositorInfo
            {
                PartyId = d.PartyId,
                PartyType = d.PartyType,
                AssetSummary = GenerateAssetSummary(d.Assets?.Assets)
            }).ToList() ?? new List<DepositorInfo>(),
            Resolution = EscrowResolution.Refunded,
            CompletedAt = timestamp
        };
        await _messageBus.TryPublishAsync(EscrowTopics.EscrowRefunded, refundEvent, cancellationToken);
    }

    #endregion
}
