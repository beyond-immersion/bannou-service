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
    /// Uses optimistic concurrency (ETag) to prevent concurrent state transitions.
    /// </summary>
    public async Task<(StatusCodes, VerifyConditionResponse?)> VerifyConditionAsync(
        VerifyConditionRequest body,
        CancellationToken cancellationToken = default)
    {
        {
            var agreementKey = GetAgreementKey(body.EscrowId);

            for (var attempt = 0; attempt < _configuration.MaxConcurrencyRetries; attempt++)
            {
                var (agreementModel, etag) = await AgreementStore.GetWithETagAsync(agreementKey, cancellationToken);

                if (agreementModel == null)
                {
                    return (StatusCodes.NotFound, null);
                }

                if (agreementModel.BoundContractId == null)
                {
                    return (StatusCodes.BadRequest, null);
                }

                var validStates = new HashSet<EscrowStatus>
                {
                    EscrowStatus.PendingCondition,
                    EscrowStatus.ValidationFailed
                };

                if (!validStates.Contains(agreementModel.Status))
                {
                    return (StatusCodes.BadRequest, null);
                }

                var now = DateTimeOffset.UtcNow;
                var conditionMet = body.ConditionMet;

                agreementModel.LastValidatedAt = now;

                EscrowStatus newStatus;
                var previousStatus = agreementModel.Status;
                var triggered = false;

                if (conditionMet)
                {
                    newStatus = EscrowStatus.Finalizing;
                    agreementModel.ValidationFailures = null;
                    triggered = true;
                }
                else
                {
                    newStatus = EscrowStatus.ValidationFailed;

                    agreementModel.ValidationFailures ??= new List<ValidationFailureModel>();
                    agreementModel.ValidationFailures.Add(new ValidationFailureModel
                    {
                        DetectedAt = now,
                        AssetType = AssetType.Custom,
                        AssetDescription = "Contract condition",
                        FailureType = ValidationFailureType.AssetMissing,
                        AffectedPartyId = body.VerifierId,
                        AffectedPartyType = body.VerifierType
                    });
                }

                agreementModel.Status = newStatus;

                var saveResult = await AgreementStore.TrySaveAsync(agreementKey, agreementModel, etag ?? string.Empty, cancellationToken);
                if (saveResult == null)
                {
                    _logger.LogDebug("Concurrent modification during condition verification for escrow {EscrowId}, retrying (attempt {Attempt})",
                        body.EscrowId, attempt + 1);
                    continue;
                }

                // Agreement saved successfully - update secondary stores
                var validationKey = GetValidationKey(body.EscrowId);
                var validationTracking = await ValidationStore.GetAsync(validationKey, cancellationToken)
                    ?? new ValidationTrackingEntry { EscrowId = body.EscrowId };

                validationTracking.LastValidatedAt = now;
                if (conditionMet)
                {
                    validationTracking.FailedValidationCount = 0;
                }
                else
                {
                    validationTracking.FailedValidationCount++;
                }
                await ValidationStore.SaveAsync(validationKey, validationTracking, cancellationToken: cancellationToken);

                if (!conditionMet)
                {
                    var failedEvent = new EscrowValidationFailedEvent
                    {
                        EventId = Guid.NewGuid(),
                        Timestamp = now,
                        EscrowId = body.EscrowId,
                        Failures = new List<ValidationFailureInfo>
                        {
                            new ValidationFailureInfo
                            {
                                AssetType = AssetType.Custom,
                                FailureType = ValidationFailureType.AssetMissing,
                                AffectedPartyId = body.VerifierId,
                                Details = body.VerificationData?.ToString()
                            }
                        },
                        DetectedAt = now
                    };
                    await _messageBus.TryPublishAsync(EscrowTopics.EscrowValidationFailed, failedEvent, cancellationToken);
                }

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
                    await StatusIndexStore.SaveAsync(newStatusKey, statusEntry, cancellationToken: cancellationToken);
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

                return (StatusCodes.OK, new VerifyConditionResponse
                {
                    Escrow = MapToApiModel(agreementModel),
                    Triggered = triggered
                });
            }

            _logger.LogWarning("Failed to verify condition for escrow {EscrowId} after {MaxRetries} attempts due to concurrent modifications",
                body.EscrowId, _configuration.MaxConcurrencyRetries);
            return (StatusCodes.Conflict, null);
        }
    }

    /// <summary>
    /// Validates that escrowed assets are still present and correct.
    /// Uses optimistic concurrency (ETag) to prevent concurrent state transitions.
    /// </summary>
    public async Task<(StatusCodes, ValidateEscrowResponse?)> ValidateEscrowAsync(
        ValidateEscrowRequest body,
        CancellationToken cancellationToken = default)
    {
        {
            var agreementKey = GetAgreementKey(body.EscrowId);

            for (var attempt = 0; attempt < _configuration.MaxConcurrencyRetries; attempt++)
            {
                var (agreementModel, etag) = await AgreementStore.GetWithETagAsync(agreementKey, cancellationToken);

                if (agreementModel == null)
                {
                    return (StatusCodes.NotFound, null);
                }

                if (IsTerminalState(agreementModel.Status))
                {
                    return (StatusCodes.BadRequest, null);
                }

                var now = DateTimeOffset.UtcNow;
                var failures = new List<ValidationFailure>();

                // Validate deposits (placeholder - real impl would check with currency/inventory services)

                agreementModel.LastValidatedAt = now;

                var isValid = failures.Count == 0;
                var previousStatus = agreementModel.Status;

                if (!isValid)
                {
                    agreementModel.ValidationFailures = failures
                        .Select(f => new ValidationFailureModel
                        {
                            DetectedAt = f.DetectedAt,
                            AssetType = f.AssetType,
                            AssetDescription = f.AssetDescription,
                            FailureType = f.FailureType,
                            AffectedPartyId = f.AffectedPartyId,
                            AffectedPartyType = f.AffectedPartyType
                        })
                        .ToList();

                    if (agreementModel.Status == EscrowStatus.PendingCondition)
                    {
                        agreementModel.Status = EscrowStatus.ValidationFailed;
                    }
                }
                else
                {
                    agreementModel.ValidationFailures = null;
                }

                var saveResult = await AgreementStore.TrySaveAsync(agreementKey, agreementModel, etag ?? string.Empty, cancellationToken);
                if (saveResult == null)
                {
                    _logger.LogDebug("Concurrent modification during validation for escrow {EscrowId}, retrying (attempt {Attempt})",
                        body.EscrowId, attempt + 1);
                    continue;
                }

                // Agreement saved successfully - update secondary stores
                var validationKey = GetValidationKey(body.EscrowId);
                var validationTracking = await ValidationStore.GetAsync(validationKey, cancellationToken)
                    ?? new ValidationTrackingEntry { EscrowId = body.EscrowId };

                validationTracking.LastValidatedAt = now;
                if (!isValid)
                {
                    validationTracking.FailedValidationCount++;
                }
                await ValidationStore.SaveAsync(validationKey, validationTracking, cancellationToken: cancellationToken);

                if (!isValid)
                {
                    if (previousStatus != agreementModel.Status)
                    {
                        var oldStatusKey = $"{GetStatusIndexKey(previousStatus)}:{body.EscrowId}";
                        await StatusIndexStore.DeleteAsync(oldStatusKey, cancellationToken);

                        var newStatusKey = $"{GetStatusIndexKey(EscrowStatus.ValidationFailed)}:{body.EscrowId}";
                        var statusEntry = new StatusIndexEntry
                        {
                            EscrowId = body.EscrowId,
                            Status = EscrowStatus.ValidationFailed,
                            ExpiresAt = agreementModel.ExpiresAt,
                            AddedAt = now
                        };
                        await StatusIndexStore.SaveAsync(newStatusKey, statusEntry, cancellationToken: cancellationToken);
                    }

                    var failedEvent = new EscrowValidationFailedEvent
                    {
                        EventId = Guid.NewGuid(),
                        Timestamp = now,
                        EscrowId = body.EscrowId,
                        Failures = failures.Select(f => new ValidationFailureInfo
                        {
                            AssetType = f.AssetType,
                            FailureType = f.FailureType,
                            AffectedPartyId = f.AffectedPartyId,
                            Details = null
                        }).ToList(),
                        DetectedAt = now
                    };
                    await _messageBus.TryPublishAsync(EscrowTopics.EscrowValidationFailed, failedEvent, cancellationToken);
                }

                _logger.LogInformation("Validation completed for escrow {EscrowId}: valid={IsValid}",
                    body.EscrowId, isValid);

                return (StatusCodes.OK, new ValidateEscrowResponse
                {
                    Valid = isValid,
                    Escrow = MapToApiModel(agreementModel),
                    Failures = failures
                });
            }

            _logger.LogWarning("Failed to validate escrow {EscrowId} after {MaxRetries} attempts due to concurrent modifications",
                body.EscrowId, _configuration.MaxConcurrencyRetries);
            return (StatusCodes.Conflict, null);
        }
    }

    /// <summary>
    /// Reaffirms an escrow after validation failure.
    /// Uses optimistic concurrency (ETag) to prevent lost reaffirmations from concurrent modifications.
    /// </summary>
    public async Task<(StatusCodes, ReaffirmResponse?)> ReaffirmAsync(
        ReaffirmRequest body,
        CancellationToken cancellationToken = default)
    {
        {
            var agreementKey = GetAgreementKey(body.EscrowId);

            for (var attempt = 0; attempt < _configuration.MaxConcurrencyRetries; attempt++)
            {
                var (agreementModel, etag) = await AgreementStore.GetWithETagAsync(agreementKey, cancellationToken);

                if (agreementModel == null)
                {
                    return (StatusCodes.NotFound, null);
                }

                if (agreementModel.Status != EscrowStatus.ValidationFailed)
                {
                    return (StatusCodes.BadRequest, null);
                }

                var party = agreementModel.Parties?.FirstOrDefault(p =>
                    p.PartyId == body.PartyId && p.PartyType == body.PartyType);

                if (party == null)
                {
                    return (StatusCodes.NotFound, null);
                }

                var now = DateTimeOffset.UtcNow;

                agreementModel.Consents ??= new List<EscrowConsentModel>();
                agreementModel.Consents.Add(new EscrowConsentModel
                {
                    PartyId = body.PartyId,
                    PartyType = body.PartyType,
                    ConsentType = EscrowConsentType.Reaffirm,
                    ConsentedAt = now
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
                    newStatus = EscrowStatus.PendingCondition;
                    agreementModel.ValidationFailures = null;
                }
                else
                {
                    newStatus = EscrowStatus.ValidationFailed;
                }

                agreementModel.Status = newStatus;

                var saveResult = await AgreementStore.TrySaveAsync(agreementKey, agreementModel, etag ?? string.Empty, cancellationToken);
                if (saveResult == null)
                {
                    _logger.LogDebug("Concurrent modification during reaffirmation for escrow {EscrowId}, retrying (attempt {Attempt})",
                        body.EscrowId, attempt + 1);
                    continue;
                }

                // Agreement saved successfully - update secondary stores
                if (allReaffirmed)
                {
                    var validationKey = GetValidationKey(body.EscrowId);
                    var validationTracking = await ValidationStore.GetAsync(validationKey, cancellationToken);
                    if (validationTracking != null)
                    {
                        validationTracking.FailedValidationCount = 0;
                        await ValidationStore.SaveAsync(validationKey, validationTracking, cancellationToken: cancellationToken);
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
                    await StatusIndexStore.SaveAsync(newStatusKey, statusEntry, cancellationToken: cancellationToken);
                }

                _logger.LogInformation("Reaffirmation recorded for escrow {EscrowId} by party {PartyId}",
                    body.EscrowId, body.PartyId);

                return (StatusCodes.OK, new ReaffirmResponse
                {
                    Escrow = MapToApiModel(agreementModel),
                    AllReaffirmed = allReaffirmed
                });
            }

            _logger.LogWarning("Failed to reaffirm escrow {EscrowId} after {MaxRetries} attempts due to concurrent modifications",
                body.EscrowId, _configuration.MaxConcurrencyRetries);
            return (StatusCodes.Conflict, null);
        }
    }
}
