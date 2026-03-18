using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Contract;
using BeyondImmersion.BannouService.Currency;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Item;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Xml;

namespace BeyondImmersion.BannouService.Escrow;

/// <summary>
/// Background service that periodically validates deposited assets are still present
/// and in expected state by calling downstream services (Currency, Item, Contract).
/// Service unavailability is NOT validation failure — assets are skipped and retried next cycle.
/// Only confirmed discrepancies trigger the ValidationFailed state and reaffirmation flow.
/// </summary>
[BannouHelperService("escrow-validation", typeof(IEscrowService), typeof(IHostedService), lifetime: ServiceLifetime.Singleton)]
public class EscrowValidationService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EscrowValidationService> _logger;
    private readonly EscrowServiceConfiguration _configuration;
    private readonly ITelemetryProvider _telemetryProvider;

    /// <summary>
    /// Interval between validation checks, from configuration (ISO 8601 duration).
    /// </summary>
    private TimeSpan CheckInterval => ParseDuration(_configuration.ValidationCheckInterval);

    /// <summary>
    /// Maximum escrows to process per cycle, from configuration.
    /// </summary>
    private int BatchSize => _configuration.ValidationBatchSize;

    /// <summary>
    /// Statuses eligible for periodic validation (all funded states).
    /// </summary>
    private static readonly EscrowStatus[] ValidationEligibleStatuses = new[]
    {
        EscrowStatus.Funded,
        EscrowStatus.PendingConsent,
        EscrowStatus.PendingCondition,
        EscrowStatus.Finalizing,
        EscrowStatus.Releasing
    };

    /// <summary>
    /// Statuses that can transition to ValidationFailed (all funded states except Releasing).
    /// </summary>
    private static readonly HashSet<EscrowStatus> FailureTransitionEligible = new()
    {
        EscrowStatus.Funded,
        EscrowStatus.PendingConsent,
        EscrowStatus.PendingCondition,
        EscrowStatus.Finalizing
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="EscrowValidationService"/> class.
    /// </summary>
    public EscrowValidationService(
        IServiceProvider serviceProvider,
        ILogger<EscrowValidationService> logger,
        EscrowServiceConfiguration configuration,
        ITelemetryProvider telemetryProvider)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
        _telemetryProvider = telemetryProvider;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Escrow validation service starting, check interval: {Interval}",
            CheckInterval);

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(_configuration.ValidationStartupDelaySeconds), stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Escrow validation service cancelled during startup");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndProcessValidationsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during escrow validation check");
                await _serviceProvider.TryPublishWorkerErrorAsync(
                    "escrow", "ValidationCheck", ex, _logger, stoppingToken);
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Escrow validation service stopped");
    }

    /// <summary>
    /// Checks for escrows due for validation and processes them.
    /// </summary>
    private async Task CheckAndProcessValidationsAsync(CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.escrow", "EscrowValidationService.CheckAndProcessValidationsAsync");
        _logger.LogDebug("Checking for escrows due for validation");

        using var scope = _serviceProvider.CreateScope();
        var stateStoreFactory = scope.ServiceProvider.GetRequiredService<IStateStoreFactory>();
        var messageBus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
        var currencyClient = scope.ServiceProvider.GetRequiredService<ICurrencyClient>();
        var itemClient = scope.ServiceProvider.GetRequiredService<IItemClient>();
        var contractClient = scope.ServiceProvider.GetRequiredService<IContractClient>();
        var meshInvocationClient = scope.ServiceProvider.GetRequiredService<IMeshInvocationClient>();

        var agreementStore = stateStoreFactory.GetQueryableStore<EscrowAgreementModel>(StateStoreDefinitions.EscrowAgreements);
        var handlerStore = stateStoreFactory.GetQueryableStore<AssetHandlerModel>(StateStoreDefinitions.EscrowHandlerRegistry);
        var statusIndexStore = stateStoreFactory.GetStore<StatusIndexEntry>(StateStoreDefinitions.EscrowStatusIndex);
        var validationStore = stateStoreFactory.GetStore<ValidationTrackingEntry>(StateStoreDefinitions.EscrowActiveValidation);

        var now = DateTimeOffset.UtcNow;
        var processed = 0;

        foreach (var status in ValidationEligibleStatuses)
        {
            if (processed >= BatchSize) break;

            var agreements = await agreementStore.QueryAsync(
                a => a.Status == status,
                cancellationToken);

            if (agreements.Count == 0) continue;

            var toProcess = agreements.Take(BatchSize - processed);

            foreach (var agreement in toProcess)
            {
                if (processed >= BatchSize) break;

                try
                {
                    // Check if validation is due
                    var validationKey = EscrowService.BuildValidationKey(agreement.EscrowId);
                    var tracking = await validationStore.GetAsync(validationKey, cancellationToken);

                    if (tracking != null && tracking.NextValidationDue > now)
                    {
                        continue; // Not due yet
                    }

                    var wasProcessed = await ProcessSingleValidationAsync(
                        agreementStore, statusIndexStore, validationStore, handlerStore,
                        messageBus, currencyClient, itemClient, contractClient, meshInvocationClient,
                        agreement, now, cancellationToken);

                    if (wasProcessed) processed++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to validate escrow {EscrowId}, continuing",
                        agreement.EscrowId);
                }
            }
        }

        if (processed > 0)
        {
            _logger.LogInformation("Validated {Count} escrows", processed);
        }
        else
        {
            _logger.LogDebug("No escrows due for validation this cycle");
        }
    }

    /// <summary>
    /// Validates a single escrow's deposited assets.
    /// </summary>
    private async Task<bool> ProcessSingleValidationAsync(
        IQueryableStateStore<EscrowAgreementModel> agreementStore,
        IStateStore<StatusIndexEntry> statusIndexStore,
        IStateStore<ValidationTrackingEntry> validationStore,
        IQueryableStateStore<AssetHandlerModel> handlerStore,
        IMessageBus messageBus,
        ICurrencyClient currencyClient,
        IItemClient itemClient,
        IContractClient contractClient,
        IMeshInvocationClient meshInvocationClient,
        EscrowAgreementModel agreement,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.escrow", "EscrowValidationService.ProcessSingleValidationAsync");
        var agreementKey = EscrowService.BuildAgreementKey(agreement.EscrowId);

        var (currentAgreement, etag) = await agreementStore.GetWithETagAsync(agreementKey, cancellationToken);
        if (currentAgreement == null)
        {
            _logger.LogDebug("Escrow {EscrowId} no longer exists", agreement.EscrowId);
            return false;
        }

        // Re-check status hasn't changed
        if (!ValidationEligibleStatuses.Contains(currentAgreement.Status))
        {
            _logger.LogDebug("Escrow {EscrowId} no longer in validation-eligible state", agreement.EscrowId);
            return false;
        }

        // Validate deposited assets
        var failures = await ValidateAssetsAsync(
            currentAgreement, currencyClient, itemClient, contractClient, meshInvocationClient, handlerStore, cancellationToken);

        var previousStatus = currentAgreement.Status;
        var hasFailures = failures.Count > 0;

        if (hasFailures && FailureTransitionEligible.Contains(currentAgreement.Status))
        {
            currentAgreement.PreFailureStatus = currentAgreement.Status;
            currentAgreement.Status = EscrowStatus.ValidationFailed;
            currentAgreement.ValidationFailures = failures.Select(f => new ValidationFailureModel
            {
                DetectedAt = f.DetectedAt,
                AssetType = f.AssetType,
                AssetDescription = f.AssetDescription,
                FailureType = f.FailureType,
                AffectedPartyId = f.AffectedPartyId,
                AffectedPartyType = f.AffectedPartyType,
                Details = f.Details
            }).ToList();
        }

        currentAgreement.LastValidatedAt = now;

        // GetWithETagAsync returns non-null etag for existing records;
        // coalesce satisfies compiler's nullable analysis (will never execute)
        var saveResult = await agreementStore.TrySaveAsync(agreementKey, currentAgreement, etag ?? string.Empty, cancellationToken: cancellationToken);
        if (saveResult == null)
        {
            _logger.LogDebug("Concurrent modification on escrow {EscrowId} during validation", agreement.EscrowId);
            return false;
        }

        // Update validation tracking
        var validationKey = EscrowService.BuildValidationKey(agreement.EscrowId);
        var tracking = await validationStore.GetAsync(validationKey, cancellationToken)
            ?? new ValidationTrackingEntry { EscrowId = agreement.EscrowId };

        tracking.LastValidatedAt = now;
        tracking.NextValidationDue = now + CheckInterval;
        if (hasFailures)
        {
            tracking.FailedValidationCount++;
        }
        await validationStore.SaveAsync(validationKey, tracking, cancellationToken: cancellationToken);

        // Update status index if status changed
        if (previousStatus != currentAgreement.Status)
        {
            var oldStatusKey = $"{EscrowService.BuildStatusIndexKey(previousStatus)}:{agreement.EscrowId}";
            await statusIndexStore.DeleteAsync(oldStatusKey, cancellationToken);

            var newStatusKey = $"{EscrowService.BuildStatusIndexKey(currentAgreement.Status)}:{agreement.EscrowId}";
            var statusEntry = new StatusIndexEntry
            {
                EscrowId = agreement.EscrowId,
                Status = currentAgreement.Status,
                ExpiresAt = currentAgreement.ExpiresAt,
                AddedAt = now
            };
            await statusIndexStore.SaveAsync(newStatusKey, statusEntry, cancellationToken: cancellationToken);

            // Publish validation failed event
            var failedEvent = new EscrowValidationFailedEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = now,
                EscrowId = agreement.EscrowId,
                Failures = failures.Select(f => new ValidationFailureInfo
                {
                    AssetType = f.AssetType,
                    FailureType = f.FailureType,
                    AffectedPartyId = f.AffectedPartyId,
                    Details = f.Details?.ToString()
                }).ToList(),
                DetectedAt = now
            };
            await messageBus.PublishEscrowValidationFailedAsync(failedEvent, cancellationToken);

            _logger.LogInformation("Escrow {EscrowId} validation failed: {FailureCount} failures, transitioned from {Previous} to ValidationFailed",
                agreement.EscrowId, failures.Count, previousStatus);
        }
        else
        {
            _logger.LogDebug("Escrow {EscrowId} validation passed", agreement.EscrowId);
        }

        return true;
    }

    /// <summary>
    /// Validates all deposited assets by calling downstream services.
    /// Service unavailability is NOT a validation failure — assets are skipped.
    /// </summary>
    private async Task<List<ValidationFailure>> ValidateAssetsAsync(
        EscrowAgreementModel agreement,
        ICurrencyClient currencyClient,
        IItemClient itemClient,
        IContractClient contractClient,
        IMeshInvocationClient meshInvocationClient,
        IQueryableStateStore<AssetHandlerModel> handlerStore,
        CancellationToken cancellationToken)
    {
        var failures = new List<ValidationFailure>();

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
                        AssetType.Currency => await ValidateCurrencyAsync(deposit, asset, agreement, currencyClient, cancellationToken),
                        AssetType.Item or AssetType.ItemStack => await ValidateItemAsync(deposit, asset, itemClient, cancellationToken),
                        AssetType.Contract => await ValidateContractAsync(deposit, asset, contractClient, cancellationToken),
                        AssetType.Custom => await ValidateCustomAsync(asset, handlerStore, meshInvocationClient, cancellationToken),
                        _ => null
                    };

                    if (failure != null)
                    {
                        failures.Add(failure);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
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
    private static async Task<ValidationFailure?> ValidateCurrencyAsync(
        EscrowDepositModel deposit, EscrowAssetModel asset, EscrowAgreementModel agreement,
        ICurrencyClient currencyClient, CancellationToken cancellationToken)
    {
        var party = agreement.Parties?.FirstOrDefault(p => p.PartyId == deposit.PartyId && p.PartyType == deposit.PartyType);
        if (party?.WalletId == null || asset.CurrencyDefinitionId == null)
        {
            return null;
        }

        try
        {
            await currencyClient.GetBalanceAsync(
                new GetBalanceRequest
                {
                    WalletId = party.WalletId.Value,
                    CurrencyDefinitionId = asset.CurrencyDefinitionId.Value
                }, cancellationToken);

            return null;
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
    private static async Task<ValidationFailure?> ValidateItemAsync(
        EscrowDepositModel deposit, EscrowAssetModel asset,
        IItemClient itemClient, CancellationToken cancellationToken)
    {
        if (asset.ItemInstanceId == null)
        {
            return null;
        }

        try
        {
            await itemClient.GetItemInstanceAsync(
                new GetItemInstanceRequest { InstanceId = asset.ItemInstanceId.Value },
                cancellationToken);

            return null;
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
    /// Validates a contract deposit by verifying the contract instance exists and is not terminal.
    /// </summary>
    private static async Task<ValidationFailure?> ValidateContractAsync(
        EscrowDepositModel deposit, EscrowAssetModel asset,
        IContractClient contractClient, CancellationToken cancellationToken)
    {
        if (asset.ContractInstanceId == null)
        {
            return null;
        }

        try
        {
            var contractInstance = await contractClient.GetContractInstanceAsync(
                new GetContractInstanceRequest { ContractId = asset.ContractInstanceId.Value },
                cancellationToken);

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

            return null;
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
    /// </summary>
    private async Task<ValidationFailure?> ValidateCustomAsync(
        EscrowAssetModel asset,
        IQueryableStateStore<AssetHandlerModel> handlerStore,
        IMeshInvocationClient meshInvocationClient,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(asset.CustomAssetType))
        {
            return null;
        }

        var handlerKey = EscrowService.BuildHandlerKey(asset.CustomAssetType);
        var handler = await handlerStore.GetAsync(handlerKey, cancellationToken);

        if (handler == null)
        {
            _logger.LogWarning("No handler registered for custom asset type {AssetType}, skipping validation",
                asset.CustomAssetType);
            return null;
        }

        try
        {
            var response = await meshInvocationClient.InvokeMethodAsync<CustomHandlerValidateRequest, CustomHandlerValidateResponse>(
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
            _logger.LogWarning(ex,
                "Custom handler {PluginId} unavailable for {AssetType} validation, skipping",
                handler.PluginId, asset.CustomAssetType);
            return null;
        }
    }

    /// <summary>
    /// Parses an ISO 8601 duration string to a TimeSpan.
    /// </summary>
    private static TimeSpan ParseDuration(string isoDuration)
    {
        try
        {
            return XmlConvert.ToTimeSpan(isoDuration);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                $"Invalid ISO 8601 duration in configuration: '{isoDuration}'", ex);
        }
    }
}
