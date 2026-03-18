using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Contract;
using BeyondImmersion.BannouService.Currency;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Item;
using BeyondImmersion.BannouService.Services;
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
        var agreementKey = BuildAgreementKey(body.EscrowId);

        // Manual retry loop: mutation includes contract/status validation with early BadRequest returns (cannot use UpdateWithRetryAsync)
        for (var attempt = 0; attempt < _configuration.MaxConcurrencyRetries; attempt++)
        {
            var (agreementModel, etag) = await _agreementStore.GetWithETagAsync(agreementKey, cancellationToken);

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

            // GetWithETagAsync returns non-null etag for existing records;
            // coalesce satisfies compiler's nullable analysis (will never execute)
            var saveResult = await _agreementStore.TrySaveAsync(agreementKey, agreementModel, etag ?? string.Empty, cancellationToken: cancellationToken);
            if (saveResult == null)
            {
                _logger.LogDebug("Concurrent modification during condition verification for escrow {EscrowId}, retrying (attempt {Attempt})",
                    body.EscrowId, attempt + 1);
                continue;
            }

            // Agreement saved successfully - update secondary stores
            var validationKey = BuildValidationKey(body.EscrowId);
            var validationTracking = await _validationStore.GetAsync(validationKey, cancellationToken)
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
            await _validationStore.SaveAsync(validationKey, validationTracking, cancellationToken: cancellationToken);

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
                await _messageBus.PublishEscrowValidationFailedAsync(failedEvent, cancellationToken);
            }

            if (previousStatus != newStatus)
            {
                var oldStatusKey = $"{BuildStatusIndexKey(previousStatus)}:{body.EscrowId}";
                await _statusIndexStore.DeleteAsync(oldStatusKey, cancellationToken);

                var newStatusKey = $"{BuildStatusIndexKey(newStatus)}:{body.EscrowId}";
                var statusEntry = new StatusIndexEntry
                {
                    EscrowId = body.EscrowId,
                    Status = newStatus,
                    ExpiresAt = agreementModel.ExpiresAt,
                    AddedAt = now
                };
                await _statusIndexStore.SaveAsync(newStatusKey, statusEntry, cancellationToken: cancellationToken);
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
                await _messageBus.PublishEscrowFinalizingAsync(finalizingEvent, cancellationToken);
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

    /// <summary>
    /// Validates that escrowed assets are still present and correct.
    /// Uses optimistic concurrency (ETag) to prevent concurrent state transitions.
    /// </summary>
    public async Task<(StatusCodes, ValidateEscrowResponse?)> ValidateEscrowAsync(
        ValidateEscrowRequest body,
        CancellationToken cancellationToken = default)
    {
        var agreementKey = BuildAgreementKey(body.EscrowId);

        // Manual retry loop: mutation includes terminal state validation with early BadRequest return (cannot use UpdateWithRetryAsync)
        for (var attempt = 0; attempt < _configuration.MaxConcurrencyRetries; attempt++)
        {
            var (agreementModel, etag) = await _agreementStore.GetWithETagAsync(agreementKey, cancellationToken);

            if (agreementModel == null)
            {
                return (StatusCodes.NotFound, null);
            }

            if (IsTerminalState(agreementModel.Status))
            {
                return (StatusCodes.BadRequest, null);
            }

            var now = DateTimeOffset.UtcNow;
            var failures = await ValidateDepositsAsync(agreementModel, cancellationToken);

            agreementModel.LastValidatedAt = now;

            var isValid = failures.Count == 0;
            var previousStatus = agreementModel.Status;

            // States eligible for validation failure transition (all funded states except Releasing)
            var validationFailureEligible = new HashSet<EscrowStatus>
            {
                EscrowStatus.Funded,
                EscrowStatus.PendingConsent,
                EscrowStatus.PendingCondition,
                EscrowStatus.Finalizing
            };

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

                if (validationFailureEligible.Contains(agreementModel.Status))
                {
                    agreementModel.PreFailureStatus = agreementModel.Status;
                    agreementModel.Status = EscrowStatus.ValidationFailed;
                }
            }
            else
            {
                agreementModel.ValidationFailures = null;
            }

            // GetWithETagAsync returns non-null etag for existing records;
            // coalesce satisfies compiler's nullable analysis (will never execute)
            var saveResult = await _agreementStore.TrySaveAsync(agreementKey, agreementModel, etag ?? string.Empty, cancellationToken: cancellationToken);
            if (saveResult == null)
            {
                _logger.LogDebug("Concurrent modification during validation for escrow {EscrowId}, retrying (attempt {Attempt})",
                    body.EscrowId, attempt + 1);
                continue;
            }

            // Agreement saved successfully - update secondary stores
            var validationKey = BuildValidationKey(body.EscrowId);
            var validationTracking = await _validationStore.GetAsync(validationKey, cancellationToken)
                ?? new ValidationTrackingEntry { EscrowId = body.EscrowId };

            validationTracking.LastValidatedAt = now;
            if (!isValid)
            {
                validationTracking.FailedValidationCount++;
            }
            await _validationStore.SaveAsync(validationKey, validationTracking, cancellationToken: cancellationToken);

            if (!isValid)
            {
                if (previousStatus != agreementModel.Status)
                {
                    var oldStatusKey = $"{BuildStatusIndexKey(previousStatus)}:{body.EscrowId}";
                    await _statusIndexStore.DeleteAsync(oldStatusKey, cancellationToken);

                    var newStatusKey = $"{BuildStatusIndexKey(EscrowStatus.ValidationFailed)}:{body.EscrowId}";
                    var statusEntry = new StatusIndexEntry
                    {
                        EscrowId = body.EscrowId,
                        Status = EscrowStatus.ValidationFailed,
                        ExpiresAt = agreementModel.ExpiresAt,
                        AddedAt = now
                    };
                    await _statusIndexStore.SaveAsync(newStatusKey, statusEntry, cancellationToken: cancellationToken);
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
                await _messageBus.PublishEscrowValidationFailedAsync(failedEvent, cancellationToken);
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

    /// <summary>
    /// Reaffirms an escrow after validation failure.
    /// Uses optimistic concurrency (ETag) to prevent lost reaffirmations from concurrent modifications.
    /// </summary>
    public async Task<(StatusCodes, ReaffirmResponse?)> ReaffirmAsync(
        ReaffirmRequest body,
        CancellationToken cancellationToken = default)
    {
        var agreementKey = BuildAgreementKey(body.EscrowId);

        // Manual retry loop: mutation includes status/party validation with early returns (cannot use UpdateWithRetryAsync)
        for (var attempt = 0; attempt < _configuration.MaxConcurrencyRetries; attempt++)
        {
            var (agreementModel, etag) = await _agreementStore.GetWithETagAsync(agreementKey, cancellationToken);

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
                // Restore to pre-failure status (defaults to PendingCondition for backwards compatibility)
                newStatus = agreementModel.PreFailureStatus ?? EscrowStatus.PendingCondition;
                agreementModel.ValidationFailures = null;
                agreementModel.PreFailureStatus = null;
            }
            else
            {
                newStatus = EscrowStatus.ValidationFailed;
            }

            agreementModel.Status = newStatus;

            // GetWithETagAsync returns non-null etag for existing records;
            // coalesce satisfies compiler's nullable analysis (will never execute)
            var saveResult = await _agreementStore.TrySaveAsync(agreementKey, agreementModel, etag ?? string.Empty, cancellationToken: cancellationToken);
            if (saveResult == null)
            {
                _logger.LogDebug("Concurrent modification during reaffirmation for escrow {EscrowId}, retrying (attempt {Attempt})",
                    body.EscrowId, attempt + 1);
                continue;
            }

            // Agreement saved successfully - update secondary stores
            if (allReaffirmed)
            {
                var validationKey = BuildValidationKey(body.EscrowId);
                var validationTracking = await _validationStore.GetAsync(validationKey, cancellationToken);
                if (validationTracking != null)
                {
                    validationTracking.FailedValidationCount = 0;
                    await _validationStore.SaveAsync(validationKey, validationTracking, cancellationToken: cancellationToken);
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
                await _messageBus.PublishEscrowValidationReaffirmedAsync(reaffirmedEvent, cancellationToken);
            }

            if (previousStatus != newStatus)
            {
                var oldStatusKey = $"{BuildStatusIndexKey(previousStatus)}:{body.EscrowId}";
                await _statusIndexStore.DeleteAsync(oldStatusKey, cancellationToken);

                var newStatusKey = $"{BuildStatusIndexKey(newStatus)}:{body.EscrowId}";
                var statusEntry = new StatusIndexEntry
                {
                    EscrowId = body.EscrowId,
                    Status = newStatus,
                    ExpiresAt = agreementModel.ExpiresAt,
                    AddedAt = now
                };
                await _statusIndexStore.SaveAsync(newStatusKey, statusEntry, cancellationToken: cancellationToken);
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

    /// <summary>
    /// Validates all deposited assets by calling downstream services.
    /// Service unavailability is NOT a validation failure — assets are skipped and retried next cycle.
    /// Only confirmed asset discrepancies (404, terminal status) produce failures.
    /// </summary>
    private async Task<List<ValidationFailure>> ValidateDepositsAsync(
        EscrowAgreementModel agreement, CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.escrow", "EscrowService.ValidateDepositsAsync");
        var failures = new List<ValidationFailure>();
        var now = DateTimeOffset.UtcNow;

        if (agreement.Deposits == null || agreement.Deposits.Count == 0)
        {
            return failures;
        }

        foreach (var deposit in agreement.Deposits)
        {
            if (deposit.Assets?.Assets == null)
            {
                continue;
            }

            foreach (var asset in deposit.Assets.Assets)
            {
                try
                {
                    var failure = asset.AssetType switch
                    {
                        AssetType.Currency => await ValidateCurrencyAssetAsync(deposit, asset, agreement, cancellationToken),
                        AssetType.Item or AssetType.ItemStack => await ValidateItemAssetAsync(deposit, asset, cancellationToken),
                        AssetType.Contract => await ValidateContractAssetAsync(deposit, asset, cancellationToken),
                        AssetType.Custom => await ValidateCustomAssetAsync(asset, cancellationToken),
                        _ => null
                    };

                    if (failure != null)
                    {
                        failures.Add(failure);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Service unavailability is NOT validation failure — skip and retry next cycle
                    _logger.LogWarning(ex,
                        "Skipping validation for {AssetType} asset in escrow {EscrowId} deposit {DepositId} due to service error",
                        asset.AssetType, agreement.EscrowId, deposit.DepositId);
                }
            }
        }

        return failures;
    }

    /// <summary>
    /// Validates a currency deposit by verifying the wallet and balance exist.
    /// </summary>
    private async Task<ValidationFailure?> ValidateCurrencyAssetAsync(
        EscrowDepositModel deposit, EscrowAssetModel asset, EscrowAgreementModel agreement,
        CancellationToken cancellationToken)
    {
        var party = agreement.Parties?.FirstOrDefault(p => p.PartyId == deposit.PartyId && p.PartyType == deposit.PartyType);
        if (party?.WalletId == null || asset.CurrencyDefinitionId == null)
        {
            return null; // Insufficient data to validate — skip
        }

        try
        {
            await _currencyClient.GetBalanceAsync(
                new GetBalanceRequest
                {
                    WalletId = party.WalletId.Value,
                    CurrencyDefinitionId = asset.CurrencyDefinitionId.Value
                }, cancellationToken);

            return null; // Balance exists — passes validation
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            return new ValidationFailure
            {
                DetectedAt = DateTimeOffset.UtcNow,
                AssetType = AssetType.Currency,
                AssetDescription = $"{asset.CurrencyAmount} {asset.CurrencyCode ?? "currency"}",
                FailureType = ValidationFailureType.AssetMissing,
                AffectedPartyId = deposit.PartyId,
                AffectedPartyType = deposit.PartyType
            };
        }
    }

    /// <summary>
    /// Validates an item or item-stack deposit by verifying the item instance exists.
    /// </summary>
    private async Task<ValidationFailure?> ValidateItemAssetAsync(
        EscrowDepositModel deposit, EscrowAssetModel asset, CancellationToken cancellationToken)
    {
        if (asset.ItemInstanceId == null)
        {
            return null; // Insufficient data to validate — skip
        }

        try
        {
            await _itemClient.GetItemInstanceAsync(
                new GetItemInstanceRequest { InstanceId = asset.ItemInstanceId.Value },
                cancellationToken);

            return null; // Item exists — passes validation
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            var description = asset.AssetType == AssetType.ItemStack
                ? $"{asset.ItemQuantity}x {asset.ItemTemplateName ?? "items"}"
                : asset.ItemName ?? "item";

            return new ValidationFailure
            {
                DetectedAt = DateTimeOffset.UtcNow,
                AssetType = asset.AssetType,
                AssetDescription = description,
                FailureType = ValidationFailureType.AssetMissing,
                AffectedPartyId = deposit.PartyId,
                AffectedPartyType = deposit.PartyType
            };
        }
    }

    /// <summary>
    /// Validates a contract deposit by verifying the contract instance exists and is not in a terminal state.
    /// </summary>
    private async Task<ValidationFailure?> ValidateContractAssetAsync(
        EscrowDepositModel deposit, EscrowAssetModel asset, CancellationToken cancellationToken)
    {
        if (asset.ContractInstanceId == null)
        {
            return null; // Insufficient data to validate — skip
        }

        try
        {
            var contractInstance = await _contractClient.GetContractInstanceAsync(
                new GetContractInstanceRequest { ContractId = asset.ContractInstanceId.Value },
                cancellationToken);

            // Fulfilled, Terminated, and Expired are terminal states indicating the contract
            // is no longer available for escrow custody
            if (contractInstance.Status is ContractStatus.Fulfilled
                or ContractStatus.Terminated
                or ContractStatus.Expired)
            {
                return new ValidationFailure
                {
                    DetectedAt = DateTimeOffset.UtcNow,
                    AssetType = AssetType.Contract,
                    AssetDescription = asset.ContractTemplateCode ?? "contract",
                    FailureType = ValidationFailureType.AssetMutated,
                    AffectedPartyId = deposit.PartyId,
                    AffectedPartyType = deposit.PartyType,
                    Details = $"Contract status: {contractInstance.Status}"
                };
            }

            return null; // Contract exists and is not terminal — passes validation
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            return new ValidationFailure
            {
                DetectedAt = DateTimeOffset.UtcNow,
                AssetType = AssetType.Contract,
                AssetDescription = asset.ContractTemplateCode ?? "contract",
                FailureType = ValidationFailureType.AssetMissing,
                AffectedPartyId = deposit.PartyId,
                AffectedPartyType = deposit.PartyType
            };
        }
    }

    /// <summary>
    /// Validates a custom asset deposit by calling the registered handler's ValidateEndpoint via mesh.
    /// If no handler is registered for the asset type, the asset is skipped (not a failure).
    /// Service unavailability is NOT a validation failure — skipped and retried next cycle.
    /// </summary>
    private async Task<ValidationFailure?> ValidateCustomAssetAsync(
        EscrowAssetModel asset, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(asset.CustomAssetType))
        {
            return null;
        }

        var handlerKey = BuildHandlerKey(asset.CustomAssetType);
        var handler = await _handlerStore.GetAsync(handlerKey, cancellationToken);

        if (handler == null)
        {
            _logger.LogWarning("No handler registered for custom asset type {AssetType}, skipping validation",
                asset.CustomAssetType);
            return null;
        }

        try
        {
            var response = await _meshInvocationClient.InvokeMethodAsync<CustomHandlerValidateRequest, CustomHandlerValidateResponse>(
                handler.PluginId,
                handler.ValidateEndpoint,
                new CustomHandlerValidateRequest
                {
                    EscrowId = asset.SourceOwnerId,
                    CustomAssetType = asset.CustomAssetType,
                    CustomAssetId = asset.CustomAssetId,
                    CustomAssetData = asset.CustomAssetData
                },
                cancellationToken);

            if (!response.Valid)
            {
                return new ValidationFailure
                {
                    DetectedAt = DateTimeOffset.UtcNow,
                    AssetType = AssetType.Custom,
                    AssetDescription = asset.CustomAssetType,
                    FailureType = response.FailureType ?? ValidationFailureType.AssetMissing,
                    AffectedPartyId = Guid.Empty,
                    AffectedPartyType = EntityType.Character,
                    Details = response.Details
                };
            }

            return null;
        }
        catch (MeshInvocationException ex)
        {
            // Service unavailability is NOT validation failure — skip and retry next cycle
            _logger.LogWarning(ex,
                "Custom handler {PluginId} unavailable for {AssetType} validation, skipping",
                handler.PluginId, asset.CustomAssetType);
            return null;
        }
    }
}
