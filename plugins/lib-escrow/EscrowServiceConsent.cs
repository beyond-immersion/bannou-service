using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Escrow;

/// <summary>
/// Consent operations for escrow management.
/// </summary>
public partial class EscrowService
{
    /// <summary>
    /// Records a party's consent for an escrow action.
    /// </summary>
    public async Task<(StatusCodes, ConsentResponse?)> RecordConsentAsync(
        ConsentRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var agreementKey = GetAgreementKey(body.EscrowId);
            var agreementModel = await AgreementStore.GetAsync(agreementKey, cancellationToken);

            if (agreementModel == null)
            {
                return (StatusCodes.Status404NotFound, new ConsentResponse
                {
                    Success = false,
                    Error = $"Escrow {body.EscrowId} not found"
                });
            }

            var validConsentStates = new HashSet<EscrowStatus>
            {
                EscrowStatus.Funded,
                EscrowStatus.Pending_consent,
                EscrowStatus.Pending_condition
            };

            if (!validConsentStates.Contains(agreementModel.Status))
            {
                return (StatusCodes.Status400BadRequest, new ConsentResponse
                {
                    Success = false,
                    Error = $"Escrow is in {agreementModel.Status} state and cannot accept consent"
                });
            }

            if (agreementModel.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                return (StatusCodes.Status400BadRequest, new ConsentResponse
                {
                    Success = false,
                    Error = "Escrow has expired"
                });
            }

            var party = agreementModel.Parties?.FirstOrDefault(p =>
                p.PartyId == body.PartyId && p.PartyType == body.PartyType);

            if (party == null)
            {
                return (StatusCodes.Status404NotFound, new ConsentResponse
                {
                    Success = false,
                    Error = "Party not found in this escrow"
                });
            }

            // Validate release token if in full_consent mode
            if (agreementModel.TrustMode == EscrowTrustMode.Full_consent &&
                body.ConsentType == EscrowConsentType.Release)
            {
                if (string.IsNullOrEmpty(body.ReleaseToken))
                {
                    return (StatusCodes.Status400BadRequest, new ConsentResponse
                    {
                        Success = false,
                        Error = "Release token is required in full_consent mode"
                    });
                }

                if (party.ReleaseTokenUsed)
                {
                    return (StatusCodes.Status400BadRequest, new ConsentResponse
                    {
                        Success = false,
                        Error = "Release token has already been used"
                    });
                }

                var tokenHash = HashToken(body.ReleaseToken);
                var tokenKey = GetTokenKey(tokenHash);
                var tokenRecord = await TokenStore.GetAsync(tokenKey, cancellationToken);

                if (tokenRecord == null ||
                    tokenRecord.EscrowId != body.EscrowId ||
                    tokenRecord.PartyId != body.PartyId ||
                    tokenRecord.TokenType != TokenType.Release)
                {
                    return (StatusCodes.Status401Unauthorized, new ConsentResponse
                    {
                        Success = false,
                        Error = "Invalid release token"
                    });
                }

                if (tokenRecord.Used)
                {
                    return (StatusCodes.Status400BadRequest, new ConsentResponse
                    {
                        Success = false,
                        Error = "Release token has already been used"
                    });
                }

                tokenRecord.Used = true;
                tokenRecord.UsedAt = DateTimeOffset.UtcNow;
                await TokenStore.SaveAsync(tokenKey, tokenRecord, cancellationToken);
            }

            var now = DateTimeOffset.UtcNow;

            var existingConsent = agreementModel.Consents?.FirstOrDefault(c =>
                c.PartyId == body.PartyId &&
                c.PartyType == body.PartyType &&
                c.ConsentType == body.ConsentType);

            if (existingConsent != null)
            {
                return (StatusCodes.Status400BadRequest, new ConsentResponse
                {
                    Success = false,
                    Error = $"Party has already recorded {body.ConsentType} consent"
                });
            }

            var consentModel = new EscrowConsentModel
            {
                PartyId = body.PartyId,
                PartyType = body.PartyType,
                ConsentType = body.ConsentType,
                ConsentedAt = now,
                ReleaseTokenUsed = body.ReleaseToken,
                Notes = body.Notes
            };

            agreementModel.Consents ??= new List<EscrowConsentModel>();
            agreementModel.Consents.Add(consentModel);

            if (body.ConsentType == EscrowConsentType.Release)
            {
                party.ReleaseTokenUsed = true;
                party.ReleaseTokenUsedAt = now;
            }

            var previousStatus = agreementModel.Status;
            EscrowStatus newStatus = previousStatus;

            switch (body.ConsentType)
            {
                case EscrowConsentType.Release:
                    var releaseConsentCount = agreementModel.Consents
                        .Count(c => c.ConsentType == EscrowConsentType.Release);

                    var requiredConsents = agreementModel.RequiredConsentsForRelease;
                    if (requiredConsents == -1)
                    {
                        requiredConsents = agreementModel.Parties?
                            .Count(p => p.ConsentRequired) ?? 0;
                    }

                    if (releaseConsentCount >= requiredConsents)
                    {
                        newStatus = agreementModel.BoundContractId != null
                            ? EscrowStatus.Pending_condition
                            : EscrowStatus.Finalizing;
                    }
                    else if (previousStatus == EscrowStatus.Funded)
                    {
                        newStatus = EscrowStatus.Pending_consent;
                    }
                    break;

                case EscrowConsentType.Refund:
                    if (party.ConsentRequired)
                    {
                        newStatus = EscrowStatus.Refunding;
                    }
                    break;

                case EscrowConsentType.Dispute:
                    newStatus = EscrowStatus.Disputed;
                    break;
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

            // Publish consent event
            var consentEvent = new EscrowConsentReceivedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                EscrowId = body.EscrowId,
                PartyId = body.PartyId,
                PartyType = body.PartyType,
                ConsentType = body.ConsentType.ToString(),
                ConsentsReceived = agreementModel.Consents?.Count(c => c.ConsentType == EscrowConsentType.Release) ?? 0,
                ConsentsRequired = agreementModel.RequiredConsentsForRelease,
                ConsentedAt = now
            };
            await _messageBus.TryPublishAsync(EscrowTopics.EscrowConsentReceived, consentEvent, cancellationToken);

            if (newStatus == EscrowStatus.Finalizing && previousStatus != EscrowStatus.Finalizing)
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
            else if (newStatus == EscrowStatus.Disputed)
            {
                var disputeEvent = new EscrowDisputedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = now,
                    EscrowId = body.EscrowId,
                    DisputedBy = body.PartyId,
                    DisputedByType = body.PartyType,
                    Reason = body.Notes ?? "Party initiated dispute",
                    DisputedAt = now
                };
                await _messageBus.TryPublishAsync(EscrowTopics.EscrowDisputed, disputeEvent, cancellationToken);
            }

            _logger.LogInformation(
                "Consent {ConsentType} recorded for escrow {EscrowId} by party {PartyId}, new status: {Status}",
                body.ConsentType, body.EscrowId, body.PartyId, newStatus);

            return (StatusCodes.Status200OK, new ConsentResponse
            {
                Success = true,
                Escrow = MapToApiModel(agreementModel),
                NewStatus = newStatus
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record consent for escrow {EscrowId}", body.EscrowId);
            await EmitErrorAsync("RecordConsent", ex.Message, new { body.EscrowId, body.PartyId }, cancellationToken);
            return (StatusCodes.Status500InternalServerError, new ConsentResponse
            {
                Success = false,
                Error = "An unexpected error occurred while recording consent"
            });
        }
    }

    /// <summary>
    /// Gets the consent status for an escrow.
    /// </summary>
    public async Task<(StatusCodes, GetConsentStatusResponse?)> GetConsentStatusAsync(
        GetConsentStatusRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var agreementKey = GetAgreementKey(body.EscrowId);
            var agreementModel = await AgreementStore.GetAsync(agreementKey, cancellationToken);

            if (agreementModel == null)
            {
                return (StatusCodes.Status404NotFound, new GetConsentStatusResponse
                {
                    Found = false,
                    Error = $"Escrow {body.EscrowId} not found"
                });
            }

            var partyStatuses = new List<PartyConsentStatus>();
            foreach (var party in agreementModel.Parties ?? new List<EscrowPartyModel>())
            {
                var consents = agreementModel.Consents?
                    .Where(c => c.PartyId == party.PartyId && c.PartyType == party.PartyType)
                    .ToList() ?? new List<EscrowConsentModel>();

                var hasReleaseConsent = consents.Any(c => c.ConsentType == EscrowConsentType.Release);

                var partyStatus = new PartyConsentStatus
                {
                    PartyId = party.PartyId,
                    PartyType = party.PartyType,
                    ConsentRequired = party.ConsentRequired,
                    HasConsented = hasReleaseConsent,
                    ConsentType = consents.FirstOrDefault()?.ConsentType,
                    ConsentedAt = consents.FirstOrDefault()?.ConsentedAt
                };
                partyStatuses.Add(partyStatus);
            }

            var releaseConsents = agreementModel.Consents?
                .Count(c => c.ConsentType == EscrowConsentType.Release) ?? 0;

            var requiredConsents = agreementModel.RequiredConsentsForRelease;
            if (requiredConsents == -1)
            {
                requiredConsents = agreementModel.Parties?
                    .Count(p => p.ConsentRequired) ?? 0;
            }

            var remainingConsents = Math.Max(0, requiredConsents - releaseConsents);

            return (StatusCodes.Status200OK, new GetConsentStatusResponse
            {
                Found = true,
                PartyStatuses = partyStatuses,
                TotalConsentsRequired = requiredConsents,
                TotalConsentsReceived = releaseConsents,
                RemainingConsentsNeeded = remainingConsents,
                IsReadyForRelease = remainingConsents == 0
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get consent status for escrow {EscrowId}", body.EscrowId);
            await EmitErrorAsync("GetConsentStatus", ex.Message, new { body.EscrowId }, cancellationToken);
            return (StatusCodes.Status500InternalServerError, new GetConsentStatusResponse
            {
                Found = false,
                Error = "An unexpected error occurred while retrieving consent status"
            });
        }
    }
}
