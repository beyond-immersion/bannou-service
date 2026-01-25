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
                            .FirstOrDefault(a => a.RecipientPartyId == r.RecipientPartyId)?.RecipientPartyType ?? string.Empty,
                        AssetSummary = GenerateAssetSummary(
                            agreementModel.ReleaseAllocations?
                                .FirstOrDefault(a => a.RecipientPartyId == r.RecipientPartyId)?.Assets)
                    }).ToList(),
                    Resolution = "released",
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
                    EscrowStatus.Validation_failed,
                    EscrowStatus.Disputed,
                    EscrowStatus.Partially_funded,
                    EscrowStatus.Pending_deposits
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
                            .FirstOrDefault(d => d.PartyId == r.DepositorPartyId)?.PartyType ?? string.Empty,
                        AssetSummary = GenerateAssetSummary(
                            agreementModel.Deposits?
                                .FirstOrDefault(d => d.PartyId == r.DepositorPartyId)?.Assets?.Assets)
                    }).ToList(),
                    Reason = body.Reason,
                    Resolution = "refunded",
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
                    EscrowStatus.Pending_deposits,
                    EscrowStatus.Partially_funded
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
                agreementModel.Resolution = EscrowResolution.Cancelled_refunded;
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
                    EscrowStatus.Pending_consent,
                    EscrowStatus.Pending_condition,
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
                    Resolution = body.Resolution.ToString(),
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
}
