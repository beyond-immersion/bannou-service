using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Escrow;

/// <summary>
/// Validation operations for escrow management.
/// </summary>
public partial class EscrowService
{
    /// <summary>
    /// Verifies a condition for a condition-bound escrow.
    /// </summary>
    public async Task<(StatusCodes, VerifyConditionResponse?)> VerifyConditionAsync(
        VerifyConditionRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var agreementKey = GetAgreementKey(body.EscrowId);
            var agreementModel = await AgreementStore.GetAsync(agreementKey, cancellationToken);

            if (agreementModel == null)
            {
                return (StatusCodes.Status404NotFound, new VerifyConditionResponse
                {
                    Success = false,
                    Error = $"Escrow {body.EscrowId} not found"
                });
            }

            if (agreementModel.BoundContractId == null)
            {
                return (StatusCodes.Status400BadRequest, new VerifyConditionResponse
                {
                    Success = false,
                    Error = "Escrow does not have a bound contract for condition verification"
                });
            }

            var validStates = new HashSet<EscrowStatus>
            {
                EscrowStatus.Pending_condition,
                EscrowStatus.Validation_failed
            };

            if (!validStates.Contains(agreementModel.Status))
            {
                return (StatusCodes.Status400BadRequest, new VerifyConditionResponse
                {
                    Success = false,
                    Error = $"Escrow is in {agreementModel.Status} state and cannot verify conditions"
                });
            }

            var now = DateTimeOffset.UtcNow;
            var conditionMet = body.ConditionMet;

            var validationKey = GetValidationKey(body.EscrowId);
            var validationTracking = await ValidationStore.GetAsync(validationKey, cancellationToken)
                ?? new ValidationTrackingEntry { EscrowId = body.EscrowId };

            validationTracking.LastValidatedAt = now;
            agreementModel.LastValidatedAt = now;

            EscrowStatus newStatus;
            var previousStatus = agreementModel.Status;

            if (conditionMet)
            {
                newStatus = EscrowStatus.Finalizing;
                validationTracking.FailedValidationCount = 0;
                agreementModel.ValidationFailures = null;
            }
            else
            {
                validationTracking.FailedValidationCount++;
                newStatus = EscrowStatus.Validation_failed;

                agreementModel.ValidationFailures ??= new List<ValidationFailureModel>();
                agreementModel.ValidationFailures.Add(new ValidationFailureModel
                {
                    DetectedAt = now,
                    AssetType = AssetType.Custom,
                    AssetDescription = "Contract condition",
                    FailureType = ValidationFailureType.Asset_missing,
                    AffectedPartyId = body.VerifiedBy ?? Guid.Empty,
                    AffectedPartyType = body.VerifiedByType ?? "system",
                    Details = body.FailureDetails
                });

                var failedEvent = new EscrowValidationFailedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = now,
                    EscrowId = body.EscrowId,
                    Failures = new List<ValidationFailureInfo>
                    {
                        new ValidationFailureInfo
                        {
                            AssetType = "contract_condition",
                            FailureType = "condition_not_met",
                            AffectedPartyId = body.VerifiedBy ?? Guid.Empty,
                            Details = body.FailureDetails
                        }
                    },
                    DetectedAt = now
                };
                await _messageBus.TryPublishAsync(EscrowTopics.EscrowValidationFailed, failedEvent, cancellationToken);
            }

            agreementModel.Status = newStatus;
            await AgreementStore.SaveAsync(agreementKey, agreementModel, cancellationToken);
            await ValidationStore.SaveAsync(validationKey, validationTracking, cancellationToken);

            if (previousStatus != newStatus)
            {
                var oldStatusKey = $"{GetStatusIndexKey(previousStatus)}:{body.EscrowId}";
                await StatusIndexStore.DeleteAsync(oldStatusKey, cancellationToken);

                var newStatusKey = $"{GetStatusIndexKey(newStatus)}:{body.EscrowId}";
                var statusEntry = new StatusIndexEntry
                {
                    EscrowId = body.EscrowId,
                    Status = newStatus,
                    ExpiresAt = agreementModel.ExpiresAt,
                    AddedAt = now
                };
                await StatusIndexStore.SaveAsync(newStatusKey, statusEntry, cancellationToken);
            }

            if (newStatus == EscrowStatus.Finalizing)
            {
                var finalizingEvent = new EscrowFinalizingEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = now,
                    EscrowId = body.EscrowId,
                    BoundContractId = agreementModel.BoundContractId,
                    FinalizerCount = 0,
                    StartedAt = now
                };
                await _messageBus.TryPublishAsync(EscrowTopics.EscrowFinalizing, finalizingEvent, cancellationToken);
            }

            _logger.LogInformation("Condition verified for escrow {EscrowId}: met={ConditionMet}, status={Status}",
                body.EscrowId, conditionMet, newStatus);

            return (StatusCodes.Status200OK, new VerifyConditionResponse
            {
                Success = true,
                Escrow = MapToApiModel(agreementModel),
                ConditionMet = conditionMet,
                NewStatus = newStatus
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify condition for escrow {EscrowId}", body.EscrowId);
            await EmitErrorAsync("VerifyCondition", ex.Message, new { body.EscrowId }, cancellationToken);
            return (StatusCodes.Status500InternalServerError, new VerifyConditionResponse
            {
                Success = false,
                Error = "An unexpected error occurred while verifying the condition"
            });
        }
    }

    /// <summary>
    /// Validates that escrowed assets are still present and correct.
    /// </summary>
    public async Task<(StatusCodes, ValidateEscrowResponse?)> ValidateEscrowAsync(
        ValidateEscrowRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var agreementKey = GetAgreementKey(body.EscrowId);
            var agreementModel = await AgreementStore.GetAsync(agreementKey, cancellationToken);

            if (agreementModel == null)
            {
                return (StatusCodes.Status404NotFound, new ValidateEscrowResponse
                {
                    Valid = false,
                    Error = $"Escrow {body.EscrowId} not found"
                });
            }

            if (IsTerminalState(agreementModel.Status))
            {
                return (StatusCodes.Status400BadRequest, new ValidateEscrowResponse
                {
                    Valid = false,
                    Error = $"Escrow is in terminal state {agreementModel.Status}"
                });
            }

            var now = DateTimeOffset.UtcNow;
            var failures = new List<ValidationFailure>();

            // Validate deposits (placeholder - real impl would check with currency/inventory services)

            var validationKey = GetValidationKey(body.EscrowId);
            var validationTracking = await ValidationStore.GetAsync(validationKey, cancellationToken)
                ?? new ValidationTrackingEntry { EscrowId = body.EscrowId };

            validationTracking.LastValidatedAt = now;
            agreementModel.LastValidatedAt = now;

            var isValid = failures.Count == 0;

            if (!isValid)
            {
                validationTracking.FailedValidationCount++;
                agreementModel.ValidationFailures = failures
                    .Select(f => new ValidationFailureModel
                    {
                        DetectedAt = f.DetectedAt,
                        AssetType = Enum.TryParse<AssetType>(f.AssetType, out var at) ? at : AssetType.Custom,
                        AssetDescription = f.AssetDescription,
                        FailureType = f.FailureType,
                        AffectedPartyId = f.AffectedPartyId,
                        AffectedPartyType = f.AffectedPartyType,
                        Details = f.Details
                    })
                    .ToList();

                if (agreementModel.Status == EscrowStatus.Pending_condition)
                {
                    var previousStatus = agreementModel.Status;
                    agreementModel.Status = EscrowStatus.Validation_failed;

                    var oldStatusKey = $"{GetStatusIndexKey(previousStatus)}:{body.EscrowId}";
                    await StatusIndexStore.DeleteAsync(oldStatusKey, cancellationToken);

                    var newStatusKey = $"{GetStatusIndexKey(EscrowStatus.Validation_failed)}:{body.EscrowId}";
                    var statusEntry = new StatusIndexEntry
                    {
                        EscrowId = body.EscrowId,
                        Status = EscrowStatus.Validation_failed,
                        ExpiresAt = agreementModel.ExpiresAt,
                        AddedAt = now
                    };
                    await StatusIndexStore.SaveAsync(newStatusKey, statusEntry, cancellationToken);
                }

                var failedEvent = new EscrowValidationFailedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = now,
                    EscrowId = body.EscrowId,
                    Failures = failures.Select(f => new ValidationFailureInfo
                    {
                        AssetType = f.AssetType,
                        FailureType = f.FailureType.ToString(),
                        AffectedPartyId = f.AffectedPartyId,
                        Details = f.Details
                    }).ToList(),
                    DetectedAt = now
                };
                await _messageBus.TryPublishAsync(EscrowTopics.EscrowValidationFailed, failedEvent, cancellationToken);
            }
            else
            {
                agreementModel.ValidationFailures = null;
            }

            await AgreementStore.SaveAsync(agreementKey, agreementModel, cancellationToken);
            await ValidationStore.SaveAsync(validationKey, validationTracking, cancellationToken);

            _logger.LogInformation("Validation completed for escrow {EscrowId}: valid={IsValid}",
                body.EscrowId, isValid);

            return (StatusCodes.Status200OK, new ValidateEscrowResponse
            {
                Valid = isValid,
                Escrow = MapToApiModel(agreementModel),
                Failures = failures.Count > 0 ? failures : null
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate escrow {EscrowId}", body.EscrowId);
            await EmitErrorAsync("ValidateEscrow", ex.Message, new { body.EscrowId }, cancellationToken);
            return (StatusCodes.Status500InternalServerError, new ValidateEscrowResponse
            {
                Valid = false,
                Error = "An unexpected error occurred while validating the escrow"
            });
        }
    }

    /// <summary>
    /// Reaffirms an escrow after validation failure.
    /// </summary>
    public async Task<(StatusCodes, ReaffirmResponse?)> ReaffirmAsync(
        ReaffirmRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var agreementKey = GetAgreementKey(body.EscrowId);
            var agreementModel = await AgreementStore.GetAsync(agreementKey, cancellationToken);

            if (agreementModel == null)
            {
                return (StatusCodes.Status404NotFound, new ReaffirmResponse
                {
                    Success = false,
                    Error = $"Escrow {body.EscrowId} not found"
                });
            }

            if (agreementModel.Status != EscrowStatus.Validation_failed)
            {
                return (StatusCodes.Status400BadRequest, new ReaffirmResponse
                {
                    Success = false,
                    Error = $"Escrow is in {agreementModel.Status} state, not validation_failed"
                });
            }

            var party = agreementModel.Parties?.FirstOrDefault(p =>
                p.PartyId == body.PartyId && p.PartyType == body.PartyType);

            if (party == null)
            {
                return (StatusCodes.Status404NotFound, new ReaffirmResponse
                {
                    Success = false,
                    Error = "Party not found in this escrow"
                });
            }

            var now = DateTimeOffset.UtcNow;

            agreementModel.Consents ??= new List<EscrowConsentModel>();
            agreementModel.Consents.Add(new EscrowConsentModel
            {
                PartyId = body.PartyId,
                PartyType = body.PartyType,
                ConsentType = EscrowConsentType.Reaffirm,
                ConsentedAt = now,
                Notes = body.Notes
            });

            var affectedPartyIds = agreementModel.ValidationFailures?
                .Select(f => f.AffectedPartyId)
                .Distinct()
                .ToHashSet() ?? new HashSet<Guid>();

            var reaffirmations = agreementModel.Consents
                .Where(c => c.ConsentType == EscrowConsentType.Reaffirm)
                .Select(c => c.PartyId)
                .ToHashSet();

            var allReaffirmed = affectedPartyIds.All(id => reaffirmations.Contains(id));

            EscrowStatus newStatus;
            var previousStatus = agreementModel.Status;

            if (allReaffirmed)
            {
                newStatus = EscrowStatus.Pending_condition;
                agreementModel.ValidationFailures = null;

                var validationKey = GetValidationKey(body.EscrowId);
                var validationTracking = await ValidationStore.GetAsync(validationKey, cancellationToken);
                if (validationTracking != null)
                {
                    validationTracking.FailedValidationCount = 0;
                    await ValidationStore.SaveAsync(validationKey, validationTracking, cancellationToken);
                }

                var reaffirmedEvent = new EscrowValidationReaffirmedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = now,
                    EscrowId = body.EscrowId,
                    ReaffirmedBy = body.PartyId,
                    ReaffirmedByType = body.PartyType,
                    AllReaffirmed = true,
                    ReaffirmedAt = now
                };
                await _messageBus.TryPublishAsync(EscrowTopics.EscrowValidationReaffirmed, reaffirmedEvent, cancellationToken);
            }
            else
            {
                newStatus = EscrowStatus.Validation_failed;
            }

            agreementModel.Status = newStatus;
            await AgreementStore.SaveAsync(agreementKey, agreementModel, cancellationToken);

            if (previousStatus != newStatus)
            {
                var oldStatusKey = $"{GetStatusIndexKey(previousStatus)}:{body.EscrowId}";
                await StatusIndexStore.DeleteAsync(oldStatusKey, cancellationToken);

                var newStatusKey = $"{GetStatusIndexKey(newStatus)}:{body.EscrowId}";
                var statusEntry = new StatusIndexEntry
                {
                    EscrowId = body.EscrowId,
                    Status = newStatus,
                    ExpiresAt = agreementModel.ExpiresAt,
                    AddedAt = now
                };
                await StatusIndexStore.SaveAsync(newStatusKey, statusEntry, cancellationToken);
            }

            var remainingReaffirmations = affectedPartyIds.Except(reaffirmations).ToList();

            _logger.LogInformation("Reaffirmation recorded for escrow {EscrowId} by party {PartyId}",
                body.EscrowId, body.PartyId);

            return (StatusCodes.Status200OK, new ReaffirmResponse
            {
                Success = true,
                Escrow = MapToApiModel(agreementModel),
                AllPartiesReaffirmed = allReaffirmed,
                RemainingReaffirmations = remainingReaffirmations
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reaffirm escrow {EscrowId}", body.EscrowId);
            await EmitErrorAsync("Reaffirm", ex.Message, new { body.EscrowId }, cancellationToken);
            return (StatusCodes.Status500InternalServerError, new ReaffirmResponse
            {
                Success = false,
                Error = "An unexpected error occurred while processing the reaffirmation"
            });
        }
    }
}
