using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Escrow;

/// <summary>
/// Completion operations for escrow management.
/// </summary>
public partial class EscrowService
{
    /// <summary>
    /// Releases escrowed assets to recipients.
    /// </summary>
    public async Task<(StatusCodes, ReleaseResponse?)> ReleaseAsync(
        ReleaseRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var agreementKey = GetAgreementKey(body.EscrowId);
            var agreementModel = await AgreementStore.GetAsync(agreementKey, cancellationToken);

            if (agreementModel == null)
            {
                return (StatusCodes.Status404NotFound, new ReleaseResponse
                {
                    Success = false,
                    Error = $"Escrow {body.EscrowId} not found"
                });
            }

            var validReleaseStates = new HashSet<EscrowStatus>
            {
                EscrowStatus.Finalizing,
                EscrowStatus.Releasing
            };

            if (!validReleaseStates.Contains(agreementModel.Status))
            {
                return (StatusCodes.Status400BadRequest, new ReleaseResponse
                {
                    Success = false,
                    Error = $"Escrow is in {agreementModel.Status} state and cannot be released"
                });
            }

            var now = DateTimeOffset.UtcNow;
            var previousStatus = agreementModel.Status;

            var transferResults = new List<TransferResult>();

            // Process release allocations
            if (agreementModel.ReleaseAllocations != null)
            {
                foreach (var allocation in agreementModel.ReleaseAllocations)
                {
                    var assetsToTransfer = allocation.Assets ?? new List<EscrowAssetModel>();
                    transferResults.Add(new TransferResult
                    {
                        RecipientPartyId = allocation.RecipientPartyId,
                        RecipientPartyType = allocation.RecipientPartyType,
                        Transferred = assetsToTransfer.Select(MapAssetToApiModel).ToList(),
                        Success = true
                    });
                }
            }

            agreementModel.Status = EscrowStatus.Released;
            agreementModel.Resolution = EscrowResolution.Released;
            agreementModel.CompletedAt = now;
            agreementModel.ResolutionNotes = body.Notes;

            await AgreementStore.SaveAsync(agreementKey, agreementModel, cancellationToken);

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
            await StatusIndexStore.SaveAsync(newStatusKey, statusEntry, cancellationToken);

            foreach (var party in agreementModel.Parties ?? new List<EscrowPartyModel>())
            {
                var partyKey = GetPartyPendingKey(party.PartyId, party.PartyType);
                var existingCount = await PartyPendingStore.GetAsync(partyKey, cancellationToken);
                if (existingCount != null && existingCount.PendingCount > 0)
                {
                    existingCount.PendingCount--;
                    existingCount.LastUpdated = now;
                    await PartyPendingStore.SaveAsync(partyKey, existingCount, cancellationToken);
                }
            }

            var releaseEvent = new EscrowReleasedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                EscrowId = body.EscrowId,
                Recipients = transferResults.Select(tr => new RecipientInfo
                {
                    PartyId = tr.RecipientPartyId,
                    PartyType = tr.RecipientPartyType,
                    AssetSummary = GenerateAssetSummary(tr.Transferred?.Select(a => new EscrowAssetModel
                    {
                        AssetType = a.AssetType,
                        CurrencyCode = a.CurrencyCode,
                        CurrencyAmount = a.CurrencyAmount,
                        ItemName = a.ItemName,
                        ItemTemplateName = a.ItemTemplateName,
                        ItemQuantity = a.ItemQuantity,
                        ContractTemplateCode = a.ContractTemplateCode,
                        CustomAssetType = a.CustomAssetType
                    }))
                }).ToList(),
                Resolution = "released",
                CompletedAt = now
            };
            await _messageBus.TryPublishAsync(EscrowTopics.EscrowReleased, releaseEvent, cancellationToken);

            _logger.LogInformation("Escrow {EscrowId} released with {TransferCount} transfers",
                body.EscrowId, transferResults.Count);

            return (StatusCodes.Status200OK, new ReleaseResponse
            {
                Success = true,
                Escrow = MapToApiModel(agreementModel),
                TransferResults = transferResults
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to release escrow {EscrowId}", body.EscrowId);
            await EmitErrorAsync("Release", ex.Message, new { body.EscrowId }, cancellationToken);
            return (StatusCodes.Status500InternalServerError, new ReleaseResponse
            {
                Success = false,
                Error = "An unexpected error occurred while releasing the escrow"
            });
        }
    }

    /// <summary>
    /// Refunds escrowed assets to depositors.
    /// </summary>
    public async Task<(StatusCodes, RefundResponse?)> RefundAsync(
        RefundRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var agreementKey = GetAgreementKey(body.EscrowId);
            var agreementModel = await AgreementStore.GetAsync(agreementKey, cancellationToken);

            if (agreementModel == null)
            {
                return (StatusCodes.Status404NotFound, new RefundResponse
                {
                    Success = false,
                    Error = $"Escrow {body.EscrowId} not found"
                });
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
                return (StatusCodes.Status400BadRequest, new RefundResponse
                {
                    Success = false,
                    Error = $"Escrow is in {agreementModel.Status} state and cannot be refunded"
                });
            }

            var now = DateTimeOffset.UtcNow;
            var previousStatus = agreementModel.Status;

            var refundResults = new List<TransferResult>();
            foreach (var deposit in agreementModel.Deposits ?? new List<EscrowDepositModel>())
            {
                refundResults.Add(new TransferResult
                {
                    RecipientPartyId = deposit.PartyId,
                    RecipientPartyType = deposit.PartyType,
                    Transferred = deposit.Assets.Assets?.Select(MapAssetToApiModel).ToList()
                        ?? new List<EscrowAsset>(),
                    Success = true
                });
            }

            agreementModel.Status = EscrowStatus.Refunded;
            agreementModel.Resolution = EscrowResolution.Refunded;
            agreementModel.CompletedAt = now;
            agreementModel.ResolutionNotes = body.Reason;

            await AgreementStore.SaveAsync(agreementKey, agreementModel, cancellationToken);

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
            await StatusIndexStore.SaveAsync(newStatusKey, statusEntry, cancellationToken);

            foreach (var party in agreementModel.Parties ?? new List<EscrowPartyModel>())
            {
                var partyKey = GetPartyPendingKey(party.PartyId, party.PartyType);
                var existingCount = await PartyPendingStore.GetAsync(partyKey, cancellationToken);
                if (existingCount != null && existingCount.PendingCount > 0)
                {
                    existingCount.PendingCount--;
                    existingCount.LastUpdated = now;
                    await PartyPendingStore.SaveAsync(partyKey, existingCount, cancellationToken);
                }
            }

            var refundEvent = new EscrowRefundedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                EscrowId = body.EscrowId,
                Depositors = refundResults.Select(rr => new DepositorInfo
                {
                    PartyId = rr.RecipientPartyId,
                    PartyType = rr.RecipientPartyType,
                    AssetSummary = GenerateAssetSummary(rr.Transferred?.Select(a => new EscrowAssetModel
                    {
                        AssetType = a.AssetType,
                        CurrencyCode = a.CurrencyCode,
                        CurrencyAmount = a.CurrencyAmount,
                        ItemName = a.ItemName,
                        ItemTemplateName = a.ItemTemplateName,
                        ItemQuantity = a.ItemQuantity,
                        ContractTemplateCode = a.ContractTemplateCode,
                        CustomAssetType = a.CustomAssetType
                    }))
                }).ToList(),
                Reason = body.Reason,
                Resolution = "refunded",
                CompletedAt = now
            };
            await _messageBus.TryPublishAsync(EscrowTopics.EscrowRefunded, refundEvent, cancellationToken);

            _logger.LogInformation("Escrow {EscrowId} refunded with {RefundCount} refunds",
                body.EscrowId, refundResults.Count);

            return (StatusCodes.Status200OK, new RefundResponse
            {
                Success = true,
                Escrow = MapToApiModel(agreementModel),
                RefundResults = refundResults
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refund escrow {EscrowId}", body.EscrowId);
            await EmitErrorAsync("Refund", ex.Message, new { body.EscrowId }, cancellationToken);
            return (StatusCodes.Status500InternalServerError, new RefundResponse
            {
                Success = false,
                Error = "An unexpected error occurred while refunding the escrow"
            });
        }
    }

    /// <summary>
    /// Cancels an escrow that hasn't been fully funded.
    /// </summary>
    public async Task<(StatusCodes, CancelResponse?)> CancelAsync(
        CancelRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var agreementKey = GetAgreementKey(body.EscrowId);
            var agreementModel = await AgreementStore.GetAsync(agreementKey, cancellationToken);

            if (agreementModel == null)
            {
                return (StatusCodes.Status404NotFound, new CancelResponse
                {
                    Success = false,
                    Error = $"Escrow {body.EscrowId} not found"
                });
            }

            var validCancelStates = new HashSet<EscrowStatus>
            {
                EscrowStatus.Pending_deposits,
                EscrowStatus.Partially_funded
            };

            if (!validCancelStates.Contains(agreementModel.Status))
            {
                return (StatusCodes.Status400BadRequest, new CancelResponse
                {
                    Success = false,
                    Error = $"Escrow is in {agreementModel.Status} state and cannot be cancelled"
                });
            }

            var now = DateTimeOffset.UtcNow;
            var previousStatus = agreementModel.Status;

            var refundResults = new List<TransferResult>();
            foreach (var deposit in agreementModel.Deposits ?? new List<EscrowDepositModel>())
            {
                refundResults.Add(new TransferResult
                {
                    RecipientPartyId = deposit.PartyId,
                    RecipientPartyType = deposit.PartyType,
                    Transferred = deposit.Assets.Assets?.Select(MapAssetToApiModel).ToList()
                        ?? new List<EscrowAsset>(),
                    Success = true
                });
            }

            agreementModel.Status = EscrowStatus.Cancelled;
            agreementModel.Resolution = EscrowResolution.Cancelled_refunded;
            agreementModel.CompletedAt = now;
            agreementModel.ResolutionNotes = body.Reason;

            await AgreementStore.SaveAsync(agreementKey, agreementModel, cancellationToken);

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
            await StatusIndexStore.SaveAsync(newStatusKey, statusEntry, cancellationToken);

            foreach (var party in agreementModel.Parties ?? new List<EscrowPartyModel>())
            {
                var partyKey = GetPartyPendingKey(party.PartyId, party.PartyType);
                var existingCount = await PartyPendingStore.GetAsync(partyKey, cancellationToken);
                if (existingCount != null && existingCount.PendingCount > 0)
                {
                    existingCount.PendingCount--;
                    existingCount.LastUpdated = now;
                    await PartyPendingStore.SaveAsync(partyKey, existingCount, cancellationToken);
                }
            }

            var cancelEvent = new EscrowCancelledEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                EscrowId = body.EscrowId,
                CancelledBy = body.CancelledBy,
                CancelledByType = body.CancelledByType,
                Reason = body.Reason,
                DepositsRefunded = refundResults.Count,
                CancelledAt = now
            };
            await _messageBus.TryPublishAsync(EscrowTopics.EscrowCancelled, cancelEvent, cancellationToken);

            _logger.LogInformation("Escrow {EscrowId} cancelled, {RefundCount} deposits refunded",
                body.EscrowId, refundResults.Count);

            return (StatusCodes.Status200OK, new CancelResponse
            {
                Success = true,
                Escrow = MapToApiModel(agreementModel),
                RefundResults = refundResults
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel escrow {EscrowId}", body.EscrowId);
            await EmitErrorAsync("Cancel", ex.Message, new { body.EscrowId }, cancellationToken);
            return (StatusCodes.Status500InternalServerError, new CancelResponse
            {
                Success = false,
                Error = "An unexpected error occurred while cancelling the escrow"
            });
        }
    }

    /// <summary>
    /// Raises a dispute on an escrow.
    /// </summary>
    public async Task<(StatusCodes, DisputeResponse?)> DisputeAsync(
        DisputeRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var agreementKey = GetAgreementKey(body.EscrowId);
            var agreementModel = await AgreementStore.GetAsync(agreementKey, cancellationToken);

            if (agreementModel == null)
            {
                return (StatusCodes.Status404NotFound, new DisputeResponse
                {
                    Success = false,
                    Error = $"Escrow {body.EscrowId} not found"
                });
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
                return (StatusCodes.Status400BadRequest, new DisputeResponse
                {
                    Success = false,
                    Error = $"Escrow is in {agreementModel.Status} state and cannot be disputed"
                });
            }

            var disputerParty = agreementModel.Parties?.FirstOrDefault(p =>
                p.PartyId == body.DisputedBy && p.PartyType == body.DisputedByType);

            if (disputerParty == null)
            {
                return (StatusCodes.Status403Forbidden, new DisputeResponse
                {
                    Success = false,
                    Error = "Only parties to the escrow can raise disputes"
                });
            }

            var now = DateTimeOffset.UtcNow;
            var previousStatus = agreementModel.Status;

            agreementModel.Status = EscrowStatus.Disputed;

            agreementModel.Consents ??= new List<EscrowConsentModel>();
            agreementModel.Consents.Add(new EscrowConsentModel
            {
                PartyId = body.DisputedBy,
                PartyType = body.DisputedByType,
                ConsentType = EscrowConsentType.Dispute,
                ConsentedAt = now,
                Notes = body.Reason
            });

            await AgreementStore.SaveAsync(agreementKey, agreementModel, cancellationToken);

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
            await StatusIndexStore.SaveAsync(newStatusKey, statusEntry, cancellationToken);

            var disputeEvent = new EscrowDisputedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                EscrowId = body.EscrowId,
                DisputedBy = body.DisputedBy,
                DisputedByType = body.DisputedByType,
                Reason = body.Reason ?? "Dispute raised",
                DisputedAt = now
            };
            await _messageBus.TryPublishAsync(EscrowTopics.EscrowDisputed, disputeEvent, cancellationToken);

            _logger.LogInformation("Dispute raised on escrow {EscrowId} by {DisputedBy}",
                body.EscrowId, body.DisputedBy);

            var arbiter = agreementModel.Parties?.FirstOrDefault(p =>
                p.Role == EscrowPartyRole.Arbiter);

            return (StatusCodes.Status200OK, new DisputeResponse
            {
                Success = true,
                Escrow = MapToApiModel(agreementModel),
                ArbiterId = arbiter?.PartyId,
                ArbiterType = arbiter?.PartyType,
                Message = arbiter != null
                    ? "Dispute raised. Arbiter has been notified."
                    : "Dispute raised. No arbiter assigned."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dispute escrow {EscrowId}", body.EscrowId);
            await EmitErrorAsync("Dispute", ex.Message, new { body.EscrowId }, cancellationToken);
            return (StatusCodes.Status500InternalServerError, new DisputeResponse
            {
                Success = false,
                Error = "An unexpected error occurred while raising the dispute"
            });
        }
    }

    /// <summary>
    /// Resolves a disputed escrow (arbiter action).
    /// </summary>
    public async Task<(StatusCodes, ResolveResponse?)> ResolveAsync(
        ResolveRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var agreementKey = GetAgreementKey(body.EscrowId);
            var agreementModel = await AgreementStore.GetAsync(agreementKey, cancellationToken);

            if (agreementModel == null)
            {
                return (StatusCodes.Status404NotFound, new ResolveResponse
                {
                    Success = false,
                    Error = $"Escrow {body.EscrowId} not found"
                });
            }

            if (agreementModel.Status != EscrowStatus.Disputed)
            {
                return (StatusCodes.Status400BadRequest, new ResolveResponse
                {
                    Success = false,
                    Error = $"Escrow is in {agreementModel.Status} state, not disputed"
                });
            }

            var now = DateTimeOffset.UtcNow;
            var transferResults = new List<TransferResult>();

            switch (body.Resolution)
            {
                case EscrowResolution.Released:
                    agreementModel.Status = EscrowStatus.Released;
                    break;

                case EscrowResolution.Refunded:
                    foreach (var deposit in agreementModel.Deposits ?? new List<EscrowDepositModel>())
                    {
                        transferResults.Add(new TransferResult
                        {
                            RecipientPartyId = deposit.PartyId,
                            RecipientPartyType = deposit.PartyType,
                            Transferred = deposit.Assets.Assets?.Select(MapAssetToApiModel).ToList()
                                ?? new List<EscrowAsset>(),
                            Success = true
                        });
                    }
                    agreementModel.Status = EscrowStatus.Refunded;
                    break;

                case EscrowResolution.Split:
                    if (body.SplitAllocations != null)
                    {
                        foreach (var allocation in body.SplitAllocations)
                        {
                            transferResults.Add(new TransferResult
                            {
                                RecipientPartyId = allocation.RecipientPartyId,
                                RecipientPartyType = allocation.RecipientPartyType,
                                Transferred = allocation.Assets?.Select(a => MapAssetInputToModel(a))
                                    .Select(MapAssetToApiModel).ToList() ?? new List<EscrowAsset>(),
                                Success = true
                            });
                        }
                    }
                    agreementModel.Status = EscrowStatus.Released;
                    break;

                default:
                    agreementModel.Status = EscrowStatus.Refunded;
                    break;
            }

            agreementModel.Resolution = body.Resolution;
            agreementModel.CompletedAt = now;
            agreementModel.ResolutionNotes = body.Notes;

            await AgreementStore.SaveAsync(agreementKey, agreementModel, cancellationToken);

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
            await StatusIndexStore.SaveAsync(newStatusKey, statusEntry, cancellationToken);

            foreach (var party in agreementModel.Parties ?? new List<EscrowPartyModel>())
            {
                var partyKey = GetPartyPendingKey(party.PartyId, party.PartyType);
                var existingCount = await PartyPendingStore.GetAsync(partyKey, cancellationToken);
                if (existingCount != null && existingCount.PendingCount > 0)
                {
                    existingCount.PendingCount--;
                    existingCount.LastUpdated = now;
                    await PartyPendingStore.SaveAsync(partyKey, existingCount, cancellationToken);
                }
            }

            var resolveEvent = new EscrowResolvedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                EscrowId = body.EscrowId,
                ArbiterId = body.ResolvedBy,
                ArbiterType = body.ResolvedByType,
                Resolution = body.Resolution.ToString(),
                Notes = body.Notes,
                ResolvedAt = now
            };
            await _messageBus.TryPublishAsync(EscrowTopics.EscrowResolved, resolveEvent, cancellationToken);

            _logger.LogInformation("Escrow {EscrowId} resolved with {Resolution}",
                body.EscrowId, body.Resolution);

            return (StatusCodes.Status200OK, new ResolveResponse
            {
                Success = true,
                Escrow = MapToApiModel(agreementModel),
                TransferResults = transferResults
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve escrow {EscrowId}", body.EscrowId);
            await EmitErrorAsync("Resolve", ex.Message, new { body.EscrowId }, cancellationToken);
            return (StatusCodes.Status500InternalServerError, new ResolveResponse
            {
                Success = false,
                Error = "An unexpected error occurred while resolving the dispute"
            });
        }
    }
}
