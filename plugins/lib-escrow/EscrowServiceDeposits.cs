using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Escrow;

/// <summary>
/// Deposit operations for escrow management.
/// Handles depositing assets, validating deposits, and checking deposit status.
/// </summary>
public partial class EscrowService
{
    /// <summary>
    /// Deposits assets into an escrow.
    /// Uses optimistic concurrency (ETag) to prevent lost deposits from concurrent modifications.
    /// Token marking is deferred until after agreement save to prevent token consumption on retry.
    /// </summary>
    public async Task<(StatusCodes, DepositResponse?)> DepositAsync(
        DepositRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check idempotency (outside retry loop - read-only check)
            var idempotencyKey = GetIdempotencyKey(body.IdempotencyKey);
            var existingRecord = await IdempotencyStore.GetAsync(idempotencyKey, cancellationToken);
            if (existingRecord != null)
            {
                if (existingRecord.EscrowId != body.EscrowId || existingRecord.PartyId != body.PartyId)
                {
                    _logger.LogWarning("Idempotency key {IdempotencyKey} reused with different parameters (original escrow: {OriginalEscrowId}, new: {NewEscrowId})",
                        body.IdempotencyKey, existingRecord.EscrowId, body.EscrowId);
                    return (StatusCodes.BadRequest, null);
                }

                _logger.LogInformation("Idempotent deposit request {IdempotencyKey} already processed", body.IdempotencyKey);
                if (existingRecord.Result is DepositResponse cachedResponse)
                {
                    return (StatusCodes.OK, cachedResponse);
                }
            }

            var agreementKey = GetAgreementKey(body.EscrowId);

            for (var attempt = 0; attempt < _configuration.MaxConcurrencyRetries; attempt++)
            {
                var (agreementModel, etag) = await AgreementStore.GetWithETagAsync(agreementKey, cancellationToken);

                if (agreementModel == null)
                {
                    return (StatusCodes.NotFound, null);
                }

                if (agreementModel.Status != EscrowStatus.PendingDeposits &&
                    agreementModel.Status != EscrowStatus.PartiallyFunded)
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

                // Validate deposit token if in full_consent mode (read-only validation, marking deferred)
                if (agreementModel.TrustMode == EscrowTrustMode.FullConsent)
                {
                    if (string.IsNullOrEmpty(body.DepositToken))
                    {
                        return (StatusCodes.BadRequest, null);
                    }

                    if (party.DepositTokenUsed)
                    {
                        return (StatusCodes.BadRequest, null);
                    }

                    var tokenHash = HashToken(body.DepositToken);
                    var tokenKey = GetTokenKey(tokenHash);
                    var tokenRecord = await TokenStore.GetAsync(tokenKey, cancellationToken);

                    if (tokenRecord == null ||
                        tokenRecord.EscrowId != body.EscrowId ||
                        tokenRecord.PartyId != body.PartyId ||
                        tokenRecord.TokenType != TokenType.Deposit)
                    {
                        return (StatusCodes.Unauthorized, null);
                    }

                    if (tokenRecord.Used)
                    {
                        return (StatusCodes.BadRequest, null);
                    }
                }

                var now = DateTimeOffset.UtcNow;
                var depositId = Guid.NewGuid();
                var bundleId = Guid.NewGuid();

                var assetModels = body.Assets?.Assets?.Select(MapAssetInputToModel).ToList()
                    ?? new List<EscrowAssetModel>();

                // Validate MaxAssetsPerDeposit
                if (assetModels.Count > _configuration.MaxAssetsPerDeposit)
                {
                    _logger.LogWarning("Deposit rejected: asset count {Count} exceeds max {Max} for escrow {EscrowId}",
                        assetModels.Count, _configuration.MaxAssetsPerDeposit, body.EscrowId);
                    return (StatusCodes.BadRequest, null);
                }

                var depositModel = new EscrowDepositModel
                {
                    DepositId = depositId,
                    EscrowId = body.EscrowId,
                    PartyId = body.PartyId,
                    PartyType = body.PartyType,
                    Assets = new EscrowAssetBundleModel
                    {
                        BundleId = bundleId,
                        Assets = assetModels,
                        Description = body.Assets?.Description,
                        EstimatedValue = body.Assets?.EstimatedValue
                    },
                    DepositedAt = now,
                    DepositTokenUsed = body.DepositToken,
                    IdempotencyKey = body.IdempotencyKey
                };

                agreementModel.Deposits ??= new List<EscrowDepositModel>();
                agreementModel.Deposits.Add(depositModel);

                party.DepositTokenUsed = true;
                party.DepositTokenUsedAt = now;

                var expectedDeposit = agreementModel.ExpectedDeposits?.FirstOrDefault(ed =>
                    ed.PartyId == body.PartyId && ed.PartyType == body.PartyType);

                if (expectedDeposit != null)
                {
                    expectedDeposit.Fulfilled = true;
                }

                var allRequiredFulfilled = agreementModel.ExpectedDeposits?
                    .Where(ed => !ed.Optional)
                    .All(ed => ed.Fulfilled) ?? true;

                var previousStatus = agreementModel.Status;
                EscrowStatus newStatus;
                var fullyFunded = false;

                if (allRequiredFulfilled)
                {
                    newStatus = EscrowStatus.Funded;
                    agreementModel.Status = newStatus;
                    agreementModel.FundedAt = now;
                    fullyFunded = true;
                }
                else
                {
                    newStatus = EscrowStatus.PartiallyFunded;
                    agreementModel.Status = newStatus;
                }

                var saveResult = await AgreementStore.TrySaveAsync(agreementKey, agreementModel, etag ?? string.Empty, cancellationToken);
                if (saveResult == null)
                {
                    _logger.LogDebug("Concurrent modification during deposit for escrow {EscrowId}, retrying (attempt {Attempt})",
                        body.EscrowId, attempt + 1);
                    continue;
                }

                // Agreement saved successfully - now perform secondary operations
                // Mark deposit token as used (deferred to after agreement save for atomicity)
                if (agreementModel.TrustMode == EscrowTrustMode.FullConsent && !string.IsNullOrEmpty(body.DepositToken))
                {
                    var tokenHash = HashToken(body.DepositToken);
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

                // Build release tokens if fully funded
                var releaseTokens = new List<PartyToken>();
                if (fullyFunded)
                {
                    foreach (var p in agreementModel.Parties ?? new List<EscrowPartyModel>())
                    {
                        if (p.ReleaseToken != null)
                        {
                            releaseTokens.Add(new PartyToken
                            {
                                PartyId = p.PartyId,
                                PartyType = p.PartyType,
                                Token = p.ReleaseToken
                            });
                        }
                    }
                }

                var response = new DepositResponse
                {
                    Escrow = MapToApiModel(agreementModel),
                    Deposit = MapDepositToApiModel(depositModel),
                    FullyFunded = fullyFunded,
                    ReleaseTokens = releaseTokens
                };

                var idempotencyRecord = new IdempotencyRecord
                {
                    Key = body.IdempotencyKey,
                    EscrowId = body.EscrowId,
                    PartyId = body.PartyId,
                    Operation = "Deposit",
                    CreatedAt = now,
                    ExpiresAt = now.AddHours(_configuration.IdempotencyTtlHours),
                    Result = response
                };
                await IdempotencyStore.SaveAsync(idempotencyKey, idempotencyRecord, cancellationToken: cancellationToken);

                // Publish deposit received event
                var depositEvent = new EscrowDepositReceivedEvent
                {
                    EventId = Guid.NewGuid(),
                    Timestamp = now,
                    EscrowId = body.EscrowId,
                    PartyId = body.PartyId,
                    PartyType = body.PartyType,
                    DepositId = depositId,
                    AssetSummary = GenerateAssetSummary(assetModels),
                    DepositsReceived = agreementModel.Deposits?.Count ?? 0,
                    DepositsExpected = agreementModel.ExpectedDeposits?.Count ?? 0,
                    FullyFunded = fullyFunded,
                    DepositedAt = now
                };
                await _messageBus.TryPublishAsync(EscrowTopics.EscrowDepositReceived, depositEvent, cancellationToken);

                if (fullyFunded)
                {
                    var fundedEvent = new EscrowFundedEvent
                    {
                        EventId = Guid.NewGuid(),
                        Timestamp = now,
                        EscrowId = body.EscrowId,
                        TotalDeposits = agreementModel.Deposits?.Count ?? 0,
                        FundedAt = now
                    };
                    await _messageBus.TryPublishAsync(EscrowTopics.EscrowFunded, fundedEvent, cancellationToken);
                }

                _logger.LogInformation(
                    "Deposit {DepositId} received for escrow {EscrowId} from party {PartyId}, new status: {Status}",
                    depositId, body.EscrowId, body.PartyId, newStatus);

                return (StatusCodes.OK, response);
            }

            _logger.LogWarning("Failed to deposit for escrow {EscrowId} after {MaxRetries} attempts due to concurrent modifications",
                body.EscrowId, _configuration.MaxConcurrencyRetries);
            return (StatusCodes.Conflict, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process deposit for escrow {EscrowId}", body.EscrowId);
            await EmitErrorAsync("Deposit", ex.Message, new { body.EscrowId, body.PartyId }, cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Validates a pending deposit before actual execution.
    /// </summary>
    public async Task<(StatusCodes, ValidateDepositResponse?)> ValidateDepositAsync(
        ValidateDepositRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var validationErrors = new List<string>();
            var warnings = new List<string>();

            var agreementKey = GetAgreementKey(body.EscrowId);
            var agreementModel = await AgreementStore.GetAsync(agreementKey, cancellationToken);

            if (agreementModel == null)
            {
                return (StatusCodes.NotFound, null);
            }

            if (agreementModel.Status != EscrowStatus.PendingDeposits &&
                agreementModel.Status != EscrowStatus.PartiallyFunded)
            {
                validationErrors.Add($"Escrow is in {agreementModel.Status} state and cannot accept deposits");
            }

            if (agreementModel.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                validationErrors.Add("Escrow has expired");
            }

            var party = agreementModel.Parties?.FirstOrDefault(p =>
                p.PartyId == body.PartyId && p.PartyType == body.PartyType);

            if (party == null)
            {
                validationErrors.Add("Party not found in this escrow");
            }
            else if (party.DepositTokenUsed)
            {
                validationErrors.Add("Party has already made a deposit");
            }

            return (StatusCodes.OK, new ValidateDepositResponse
            {
                Valid = validationErrors.Count == 0,
                Errors = validationErrors.Count > 0 ? validationErrors : new List<string>(),
                Warnings = warnings.Count > 0 ? warnings : new List<string>()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate deposit for escrow {EscrowId}", body.EscrowId);
            await EmitErrorAsync("ValidateDeposit", ex.Message, new { body.EscrowId }, cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Gets the deposit status for a party in an escrow.
    /// </summary>
    public async Task<(StatusCodes, GetDepositStatusResponse?)> GetDepositStatusAsync(
        GetDepositStatusRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var agreementKey = GetAgreementKey(body.EscrowId);
            var agreementModel = await AgreementStore.GetAsync(agreementKey, cancellationToken);

            if (agreementModel == null)
            {
                return (StatusCodes.NotFound, null);
            }

            var expectedDeposit = agreementModel.ExpectedDeposits?.FirstOrDefault(ed =>
                ed.PartyId == body.PartyId && ed.PartyType == body.PartyType);

            var actualDeposits = agreementModel.Deposits?
                .Where(d => d.PartyId == body.PartyId && d.PartyType == body.PartyType)
                .ToList() ?? new List<EscrowDepositModel>();

            var party = agreementModel.Parties?.FirstOrDefault(p =>
                p.PartyId == body.PartyId && p.PartyType == body.PartyType);

            return (StatusCodes.OK, new GetDepositStatusResponse
            {
                ExpectedAssets = expectedDeposit?.ExpectedAssets?
                    .Select(MapAssetToApiModel)
                    .ToList() ?? new List<EscrowAsset>(),
                DepositedAssets = actualDeposits
                    .SelectMany(d => d.Assets?.Assets?.Select(MapAssetToApiModel) ?? Enumerable.Empty<EscrowAsset>())
                    .ToList(),
                Fulfilled = expectedDeposit?.Fulfilled ?? false,
                DepositToken = party?.DepositToken,
                DepositDeadline = expectedDeposit?.DepositDeadline
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get deposit status for escrow {EscrowId}", body.EscrowId);
            await EmitErrorAsync("GetDepositStatus", ex.Message, new { body.EscrowId }, cancellationToken);
            return (StatusCodes.InternalServerError, null);
        }
    }
}
