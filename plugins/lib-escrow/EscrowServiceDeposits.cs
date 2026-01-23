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
    /// </summary>
    public async Task<(StatusCodes, DepositResponse?)> DepositAsync(
        DepositRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check idempotency
            var idempotencyKey = GetIdempotencyKey(body.IdempotencyKey);
            var existingRecord = await IdempotencyStore.GetAsync(idempotencyKey, cancellationToken);
            if (existingRecord != null)
            {
                _logger.LogInformation("Idempotent deposit request {IdempotencyKey} already processed", body.IdempotencyKey);
                if (existingRecord.Result is DepositResponse cachedResponse)
                {
                    return (StatusCodes.Status200OK, cachedResponse);
                }
            }

            var agreementKey = GetAgreementKey(body.EscrowId);
            var agreementModel = await AgreementStore.GetAsync(agreementKey, cancellationToken);

            if (agreementModel == null)
            {
                return (StatusCodes.Status404NotFound, new DepositResponse
                {
                    Success = false,
                    Error = $"Escrow {body.EscrowId} not found"
                });
            }

            if (agreementModel.Status != EscrowStatus.Pending_deposits &&
                agreementModel.Status != EscrowStatus.Partially_funded)
            {
                return (StatusCodes.Status400BadRequest, new DepositResponse
                {
                    Success = false,
                    Error = $"Escrow is in {agreementModel.Status} state and cannot accept deposits"
                });
            }

            if (agreementModel.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                return (StatusCodes.Status400BadRequest, new DepositResponse
                {
                    Success = false,
                    Error = "Escrow has expired"
                });
            }

            var party = agreementModel.Parties?.FirstOrDefault(p =>
                p.PartyId == body.PartyId && p.PartyType == body.PartyType);

            if (party == null)
            {
                return (StatusCodes.Status404NotFound, new DepositResponse
                {
                    Success = false,
                    Error = "Party not found in this escrow"
                });
            }

            // Validate deposit token if in full_consent mode
            if (agreementModel.TrustMode == EscrowTrustMode.Full_consent)
            {
                if (string.IsNullOrEmpty(body.DepositToken))
                {
                    return (StatusCodes.Status400BadRequest, new DepositResponse
                    {
                        Success = false,
                        Error = "Deposit token is required in full_consent mode"
                    });
                }

                if (party.DepositTokenUsed)
                {
                    return (StatusCodes.Status400BadRequest, new DepositResponse
                    {
                        Success = false,
                        Error = "Deposit token has already been used"
                    });
                }

                var tokenHash = HashToken(body.DepositToken);
                var tokenKey = GetTokenKey(tokenHash);
                var tokenRecord = await TokenStore.GetAsync(tokenKey, cancellationToken);

                if (tokenRecord == null ||
                    tokenRecord.EscrowId != body.EscrowId ||
                    tokenRecord.PartyId != body.PartyId ||
                    tokenRecord.TokenType != TokenType.Deposit)
                {
                    return (StatusCodes.Status401Unauthorized, new DepositResponse
                    {
                        Success = false,
                        Error = "Invalid deposit token"
                    });
                }

                if (tokenRecord.Used)
                {
                    return (StatusCodes.Status400BadRequest, new DepositResponse
                    {
                        Success = false,
                        Error = "Deposit token has already been used"
                    });
                }

                tokenRecord.Used = true;
                tokenRecord.UsedAt = DateTimeOffset.UtcNow;
                await TokenStore.SaveAsync(tokenKey, tokenRecord, cancellationToken);
            }

            var now = DateTimeOffset.UtcNow;
            var depositId = Guid.NewGuid();
            var bundleId = Guid.NewGuid();

            var assetModels = body.Assets?.Select(MapAssetInputToModel).ToList()
                ?? new List<EscrowAssetModel>();

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
                    Description = body.BundleDescription,
                    EstimatedValue = body.EstimatedValue
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

            if (allRequiredFulfilled)
            {
                newStatus = EscrowStatus.Funded;
                agreementModel.Status = newStatus;
                agreementModel.FundedAt = now;
            }
            else
            {
                newStatus = EscrowStatus.Partially_funded;
                agreementModel.Status = newStatus;
            }

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

            var response = new DepositResponse
            {
                Success = true,
                Escrow = MapToApiModel(agreementModel),
                Deposit = MapDepositToApiModel(depositModel),
                NewStatus = newStatus,
                RemainingDeposits = agreementModel.ExpectedDeposits?
                    .Where(ed => !ed.Fulfilled && !ed.Optional)
                    .Select(ed => new PartyToken
                    {
                        PartyId = ed.PartyId,
                        PartyType = ed.PartyType
                    })
                    .ToList()
            };

            var idempotencyRecord = new IdempotencyRecord
            {
                Key = body.IdempotencyKey,
                EscrowId = body.EscrowId,
                Operation = "Deposit",
                CreatedAt = now,
                ExpiresAt = now.AddHours(24),
                Result = response
            };
            await IdempotencyStore.SaveAsync(idempotencyKey, idempotencyRecord, cancellationToken);

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
                FullyFunded = newStatus == EscrowStatus.Funded,
                DepositedAt = now
            };
            await _messageBus.TryPublishAsync(EscrowTopics.EscrowDepositReceived, depositEvent, cancellationToken);

            if (newStatus == EscrowStatus.Funded)
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

            return (StatusCodes.Status200OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process deposit for escrow {EscrowId}", body.EscrowId);
            await EmitErrorAsync("Deposit", ex.Message, new { body.EscrowId, body.PartyId }, cancellationToken);
            return (StatusCodes.Status500InternalServerError, new DepositResponse
            {
                Success = false,
                Error = "An unexpected error occurred while processing the deposit"
            });
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
                return (StatusCodes.Status404NotFound, new ValidateDepositResponse
                {
                    Valid = false,
                    Errors = new List<string> { $"Escrow {body.EscrowId} not found" }
                });
            }

            if (agreementModel.Status != EscrowStatus.Pending_deposits &&
                agreementModel.Status != EscrowStatus.Partially_funded)
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

            return (StatusCodes.Status200OK, new ValidateDepositResponse
            {
                Valid = validationErrors.Count == 0,
                Errors = validationErrors.Count > 0 ? validationErrors : null,
                Warnings = warnings.Count > 0 ? warnings : null
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate deposit for escrow {EscrowId}", body.EscrowId);
            await EmitErrorAsync("ValidateDeposit", ex.Message, new { body.EscrowId }, cancellationToken);
            return (StatusCodes.Status500InternalServerError, new ValidateDepositResponse
            {
                Valid = false,
                Errors = new List<string> { "An unexpected error occurred during validation" }
            });
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
                return (StatusCodes.Status404NotFound, new GetDepositStatusResponse
                {
                    Found = false,
                    Error = $"Escrow {body.EscrowId} not found"
                });
            }

            var expectedDeposit = agreementModel.ExpectedDeposits?.FirstOrDefault(ed =>
                ed.PartyId == body.PartyId && ed.PartyType == body.PartyType);

            var actualDeposits = agreementModel.Deposits?
                .Where(d => d.PartyId == body.PartyId && d.PartyType == body.PartyType)
                .ToList() ?? new List<EscrowDepositModel>();

            var hasDeposited = actualDeposits.Count > 0;
            var isRequired = expectedDeposit != null && !expectedDeposit.Optional;

            return (StatusCodes.Status200OK, new GetDepositStatusResponse
            {
                Found = true,
                HasDeposited = hasDeposited,
                IsRequired = isRequired,
                IsFulfilled = expectedDeposit?.Fulfilled ?? false,
                DepositDeadline = expectedDeposit?.DepositDeadline,
                ExpectedAssets = expectedDeposit?.ExpectedAssets?
                    .Select(MapAssetToApiModel)
                    .ToList(),
                ActualDeposits = actualDeposits
                    .Select(MapDepositToApiModel)
                    .ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get deposit status for escrow {EscrowId}", body.EscrowId);
            await EmitErrorAsync("GetDepositStatus", ex.Message, new { body.EscrowId }, cancellationToken);
            return (StatusCodes.Status500InternalServerError, new GetDepositStatusResponse
            {
                Found = false,
                Error = "An unexpected error occurred while retrieving deposit status"
            });
        }
    }
}
