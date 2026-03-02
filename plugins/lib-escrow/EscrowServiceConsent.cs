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
    /// Uses optimistic concurrency (ETag) to prevent lost consents from concurrent modifications.
    /// Token marking is deferred until after agreement save to prevent token consumption on retry.
    /// </summary>
    public async Task<(StatusCodes, ConsentResponse?)> RecordConsentAsync(
        ConsentRequest body,
        CancellationToken cancellationToken = default)
    {
        var agreementKey = GetAgreementKey(body.EscrowId);

        for (var attempt = 0; attempt < _configuration.MaxConcurrencyRetries; attempt++)
        {
            var (agreementModel, etag) = await AgreementStore.GetWithETagAsync(agreementKey, cancellationToken);

            if (agreementModel == null)
            {
                return (StatusCodes.NotFound, null);
            }

            var validConsentStates = new HashSet<EscrowStatus>
                {
                    EscrowStatus.Funded,
                    EscrowStatus.PendingConsent,
                    EscrowStatus.PendingCondition
                };

            if (!validConsentStates.Contains(agreementModel.Status))
            {
                return (StatusCodes.BadRequest, null);
            }

            if (agreementModel.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                return (StatusCodes.BadRequest, null);
            }

            var party = agreementModel.Parties?.FirstOrDefault(p =>
                p.PartyId == body.PartyId && p.PartyType == body.PartyType);

            if (party == null)
            {
                return (StatusCodes.NotFound, null);
            }

            // Validate release token if in full_consent mode (read-only validation, marking deferred)
            if (agreementModel.TrustMode == EscrowTrustMode.FullConsent &&
                body.ConsentType == EscrowConsentType.Release)
            {
                if (string.IsNullOrEmpty(body.ReleaseToken))
                {
                    return (StatusCodes.BadRequest, null);
                }

                if (party.ReleaseTokenUsed)
                {
                    return (StatusCodes.BadRequest, null);
                }

                var tokenHash = HashToken(body.ReleaseToken);
                var tokenKey = GetTokenKey(tokenHash);
                var tokenRecord = await TokenStore.GetAsync(tokenKey, cancellationToken);

                if (tokenRecord == null ||
                    tokenRecord.EscrowId != body.EscrowId ||
                    tokenRecord.PartyId != body.PartyId ||
                    tokenRecord.TokenType != TokenType.Release)
                {
                    return (StatusCodes.Unauthorized, null);
                }

                if (tokenRecord.Used)
                {
                    return (StatusCodes.BadRequest, null);
                }
            }

            var now = DateTimeOffset.UtcNow;

            var existingConsent = agreementModel.Consents?.FirstOrDefault(c =>
                c.PartyId == body.PartyId &&
                c.PartyType == body.PartyType &&
                c.ConsentType == body.ConsentType);

            if (existingConsent != null)
            {
                return (StatusCodes.BadRequest, null);
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
            var triggered = false;

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
                            ? EscrowStatus.PendingCondition
                            : EscrowStatus.Finalizing;
                        triggered = true;
                    }
                    else if (previousStatus == EscrowStatus.Funded)
                    {
                        newStatus = EscrowStatus.PendingConsent;
                    }
                    break;

                case EscrowConsentType.Refund:
                    if (party.ConsentRequired)
                    {
                        newStatus = EscrowStatus.Refunding;
                        triggered = true;

                        // Set confirmation deadline for non-immediate refund modes
                        if (agreementModel.RefundMode != RefundMode.Immediate)
                        {
                            agreementModel.ConfirmationDeadline = now.AddSeconds(_configuration.ConfirmationTimeoutSeconds);
                        }
                    }
                    break;

                case EscrowConsentType.Dispute:
                    newStatus = EscrowStatus.Disputed;
                    triggered = true;
                    break;
            }

            agreementModel.Status = newStatus;

            // GetWithETagAsync returns non-null etag for existing records;
            // coalesce satisfies compiler's nullable analysis (will never execute)
            var saveResult = await AgreementStore.TrySaveAsync(agreementKey, agreementModel, etag ?? string.Empty, cancellationToken);
            if (saveResult == null)
            {
                _logger.LogDebug("Concurrent modification during consent for escrow {EscrowId}, retrying (attempt {Attempt})",
                    body.EscrowId, attempt + 1);
                continue;
            }

            // Agreement saved successfully - now perform secondary operations
            // Mark release token as used (deferred to after agreement save for atomicity)
            if (agreementModel.TrustMode == EscrowTrustMode.FullConsent &&
                body.ConsentType == EscrowConsentType.Release &&
                !string.IsNullOrEmpty(body.ReleaseToken))
            {
                var tokenHash = HashToken(body.ReleaseToken);
                var tokenKey = GetTokenKey(tokenHash);
                var tokenRecord = await TokenStore.GetAsync(tokenKey, cancellationToken);
                if (tokenRecord != null)
                {
                    tokenRecord.Used = true;
                    tokenRecord.UsedAt = now;
                    await TokenStore.SaveAsync(tokenKey, tokenRecord, cancellationToken: cancellationToken);
                }
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

            // Publish consent event
            var consentEvent = new EscrowConsentReceivedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                EscrowId = body.EscrowId,
                PartyId = body.PartyId,
                PartyType = body.PartyType,
                ConsentType = body.ConsentType,
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
            else if (newStatus == EscrowStatus.Refunding && previousStatus != EscrowStatus.Refunding)
            {
                var refundingEvent = new EscrowRefundingEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = now,
                    EscrowId = body.EscrowId,
                    RefundMode = agreementModel.RefundMode,
                    Deposits = (agreementModel.Deposits ?? new List<EscrowDepositModel>())
                        .Select(MapDepositToApiModel).ToList(),
                    Reason = body.Notes ?? "Party initiated refund"
                };
                await _messageBus.TryPublishAsync(EscrowTopics.EscrowRefunding, refundingEvent, cancellationToken);
            }

            _logger.LogInformation(
                "Consent {ConsentType} recorded for escrow {EscrowId} by party {PartyId}, new status: {Status}",
                body.ConsentType, body.EscrowId, body.PartyId, newStatus);

            return (StatusCodes.OK, new ConsentResponse
            {
                Escrow = MapToApiModel(agreementModel),
                ConsentRecorded = true,
                Triggered = triggered,
                NewStatus = newStatus
            });
        }

        _logger.LogWarning("Failed to record consent for escrow {EscrowId} after {MaxRetries} attempts due to concurrent modifications",
            body.EscrowId, _configuration.MaxConcurrencyRetries);
        return (StatusCodes.Conflict, null);
    }

    /// <summary>
    /// Gets the consent status for an escrow.
    /// </summary>
    public async Task<(StatusCodes, GetConsentStatusResponse?)> GetConsentStatusAsync(
        GetConsentStatusRequest body,
        CancellationToken cancellationToken = default)
    {
        var agreementKey = GetAgreementKey(body.EscrowId);
        var agreementModel = await AgreementStore.GetAsync(agreementKey, cancellationToken);

        if (agreementModel == null)
        {
            return (StatusCodes.NotFound, null);
        }

        var partyStatuses = new List<PartyConsentStatus>();
        foreach (var party in agreementModel.Parties ?? new List<EscrowPartyModel>())
        {
            if (!party.ConsentRequired) continue;

            var consent = agreementModel.Consents?.FirstOrDefault(c =>
                c.PartyId == party.PartyId &&
                c.PartyType == party.PartyType &&
                c.ConsentType == EscrowConsentType.Release);

            partyStatuses.Add(new PartyConsentStatus
            {
                PartyId = party.PartyId,
                PartyType = party.PartyType,
                DisplayName = party.DisplayName,
                ConsentGiven = consent != null,
                ConsentType = consent?.ConsentType,
                ConsentedAt = consent?.ConsentedAt
            });
        }

        var releaseConsents = agreementModel.Consents?
            .Count(c => c.ConsentType == EscrowConsentType.Release) ?? 0;

        var requiredConsents = agreementModel.RequiredConsentsForRelease;
        if (requiredConsents == -1)
        {
            requiredConsents = agreementModel.Parties?
                .Count(p => p.ConsentRequired) ?? 0;
        }

        var hasRefundConsent = agreementModel.Consents?
            .Any(c => c.ConsentType == EscrowConsentType.Refund) ?? false;

        return (StatusCodes.OK, new GetConsentStatusResponse
        {
            PartiesRequiringConsent = partyStatuses,
            ConsentsReceived = releaseConsents,
            ConsentsRequired = requiredConsents,
            CanRelease = releaseConsents >= requiredConsents,
            CanRefund = hasRefundConsent
        });
    }
}
