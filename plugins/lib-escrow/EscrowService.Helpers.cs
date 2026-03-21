using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Contract;
using BeyondImmersion.BannouService.Currency;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Inventory;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.Messaging;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Escrow;

// =============================================================================
// EscrowService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by EscrowService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (EscrowService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in IEscrowService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (EscrowService.Helpers.cs):
//     Contains all private/internal helper methods, core logic extracted
//     from endpoints, event publishing helpers, query builders, mapping
//     functions, and any other non-public methods. Every async method in
//     this file MUST call ITelemetryProvider.StartActivity to ensure
//     sub-operations are properly instrumented.
//
// Structural tests enforce both rules:
//   - Services_PrimaryFile_DoesNotCallStartActivity
//   - Services_HelperFiles_HaveStartActivityWhenAsync
//
// WHAT GOES HERE:
//   - Private async helper methods (with StartActivity spans)
//   - Private sync helper methods (query builders, mappers, validators)
//   - Internal static key builders (already in primary file by convention,
//     but may be moved here if the primary file is large)
//   - Event publishing helper methods
//   - Any extracted "core" logic (e.g., CreateAccountCoreAsync)
//
// WHAT STAYS IN THE PRIMARY FILE:
//   - Public interface method implementations (/// <inheritdoc/> methods)
//   - Constructor and field declarations
//   - Constants and key prefix definitions
//
// See: docs/reference/tenets/IMPLEMENTATION-BEHAVIOR.md (T30)
// See: docs/reference/HELPERS-AND-COMMON-PATTERNS.md
// =============================================================================

/// <summary>
/// Private and internal helper methods for EscrowService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class EscrowService
{
    /// <summary>
    /// Executes asset transfers for a deposit by calling downstream services.
    /// Currency deposits are debited via ICurrencyClient.EscrowDepositAsync.
    /// Item/ItemStack deposits are transferred to the escrow container via IInventoryClient.TransferItemAsync.
    /// Contract deposits are locked via IContractClient.LockContractAsync.
    /// Custom deposits call the registered handler's DepositEndpoint via mesh.
    /// Returns null on success, or an error message on failure.
    /// </summary>
    internal async Task<string?> ExecuteDepositTransfersAsync(
        Guid escrowId,
        EscrowPartyModel party,
        List<EscrowAssetModel> assets,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.escrow", "EscrowService.ExecuteDepositTransfersAsync");

        foreach (var asset in assets)
        {
            try
            {
                switch (asset.AssetType)
                {
                    case AssetType.Currency:
                        if (party.WalletId == null || asset.CurrencyDefinitionId == null || asset.CurrencyAmount == null)
                        {
                            _logger.LogWarning("Skipping currency deposit transfer: missing wallet or currency data for escrow {EscrowId}", escrowId);
                            break;
                        }

                        await _currencyClient.EscrowDepositAsync(
                            new EscrowDepositRequest
                            {
                                WalletId = party.WalletId.Value,
                                CurrencyDefinitionId = asset.CurrencyDefinitionId.Value,
                                Amount = asset.CurrencyAmount.Value,
                                EscrowId = escrowId,
                                IdempotencyKey = $"escrow-deposit:{escrowId}:{idempotencyKey}:{asset.CurrencyDefinitionId}"
                            }, cancellationToken);

                        _logger.LogDebug("Currency deposit executed: {Amount} {Currency} from wallet {WalletId} for escrow {EscrowId}",
                            asset.CurrencyAmount, asset.CurrencyCode, party.WalletId, escrowId);
                        break;

                    case AssetType.Item:
                    case AssetType.ItemStack:
                        if (asset.ItemInstanceId == null || party.EscrowContainerId == null)
                        {
                            _logger.LogWarning("Skipping item deposit transfer: missing item or escrow container for escrow {EscrowId}", escrowId);
                            break;
                        }

                        await _inventoryClient.TransferItemAsync(
                            new TransferItemRequest
                            {
                                InstanceId = asset.ItemInstanceId.Value,
                                TargetContainerId = party.EscrowContainerId.Value,
                                Quantity = asset.ItemQuantity.HasValue ? (double)asset.ItemQuantity.Value : null
                            }, cancellationToken);

                        _logger.LogDebug("Item deposit transferred: {ItemId} to escrow container {ContainerId} for escrow {EscrowId}",
                            asset.ItemInstanceId, party.EscrowContainerId, escrowId);
                        break;

                    case AssetType.Contract:
                        if (asset.ContractInstanceId == null)
                        {
                            _logger.LogWarning("Skipping contract deposit: missing contract ID for escrow {EscrowId}", escrowId);
                            break;
                        }

                        await _contractClient.LockContractAsync(
                            new LockContractRequest
                            {
                                ContractInstanceId = asset.ContractInstanceId.Value,
                                GuardianId = escrowId,
                                GuardianType = "escrow"
                            }, cancellationToken);

                        _logger.LogDebug("Contract locked: {ContractId} by escrow {EscrowId}",
                            asset.ContractInstanceId, escrowId);
                        break;

                    case AssetType.Custom:
                        if (string.IsNullOrEmpty(asset.CustomAssetType))
                        {
                            break;
                        }

                        var handlerKey = BuildHandlerKey(asset.CustomAssetType);
                        var handler = await _handlerStore.GetAsync(handlerKey, cancellationToken);
                        if (handler != null)
                        {
                            await _meshInvocationClient.InvokeMethodAsync<CustomHandlerValidateRequest, CustomHandlerValidateResponse>(
                                handler.PluginId,
                                handler.DepositEndpoint,
                                new CustomHandlerValidateRequest
                                {
                                    EscrowId = escrowId,
                                    CustomAssetType = asset.CustomAssetType,
                                    CustomAssetId = asset.CustomAssetId,
                                    CustomAssetData = asset.CustomAssetData
                                }, cancellationToken);
                        }
                        break;
                }
            }
            catch (ApiException ex)
            {
                _logger.LogWarning(ex, "Asset transfer failed for {AssetType} in escrow {EscrowId}: HTTP {StatusCode}",
                    asset.AssetType, escrowId, ex.StatusCode);
                return $"Asset transfer failed for {asset.AssetType}: {ex.Message}";
            }
            catch (MeshInvocationException ex)
            {
                _logger.LogWarning(ex, "Custom handler deposit failed for {AssetType} in escrow {EscrowId}",
                    asset.AssetType, escrowId);
                return $"Custom handler deposit failed for {asset.CustomAssetType}: {ex.Message}";
            }
        }

        return null;
    }

    /// <summary>
    /// Executes release asset transfers AFTER the agreement has transitioned to Released.
    /// Credits recipients via Currency, transfers items to destination containers, unlocks contracts.
    /// Per-allocation, per-asset error isolation — transfer failures are logged but do NOT
    /// block the terminal transition. The FSM state is authoritative; the Disputed escalation
    /// path handles unresolved transfer failures.
    /// </summary>
    internal async Task ExecuteReleaseTransfersAsync(
        EscrowAgreementModel agreement,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.escrow", "EscrowService.ExecuteReleaseTransfersAsync");

        if (agreement.ReleaseAllocations == null || agreement.ReleaseAllocations.Count == 0)
        {
            return;
        }

        foreach (var allocation in agreement.ReleaseAllocations)
        {
            if (allocation.Assets == null)
            {
                continue;
            }

            foreach (var asset in allocation.Assets)
            {
                try
                {
                    switch (asset.AssetType)
                    {
                        case AssetType.Currency:
                            if (allocation.DestinationWalletId == null || asset.CurrencyDefinitionId == null || asset.CurrencyAmount == null)
                            {
                                break;
                            }

                            await _currencyClient.EscrowReleaseAsync(
                                new EscrowReleaseRequest
                                {
                                    WalletId = allocation.DestinationWalletId.Value,
                                    CurrencyDefinitionId = asset.CurrencyDefinitionId.Value,
                                    Amount = asset.CurrencyAmount.Value,
                                    EscrowId = agreement.EscrowId,
                                    IdempotencyKey = $"escrow-release:{agreement.EscrowId}:{allocation.RecipientPartyId}:{asset.CurrencyDefinitionId}"
                                }, cancellationToken);
                            break;

                        case AssetType.Item:
                        case AssetType.ItemStack:
                            if (asset.ItemInstanceId == null || allocation.DestinationContainerId == null)
                            {
                                break;
                            }

                            await _inventoryClient.TransferItemAsync(
                                new TransferItemRequest
                                {
                                    InstanceId = asset.ItemInstanceId.Value,
                                    TargetContainerId = allocation.DestinationContainerId.Value,
                                    Quantity = asset.ItemQuantity.HasValue ? (double)asset.ItemQuantity.Value : null
                                }, cancellationToken);
                            break;

                        case AssetType.Contract:
                            if (asset.ContractInstanceId == null)
                            {
                                break;
                            }

                            await _contractClient.UnlockContractAsync(
                                new UnlockContractRequest
                                {
                                    ContractInstanceId = asset.ContractInstanceId.Value,
                                    GuardianId = agreement.EscrowId,
                                    GuardianType = "escrow"
                                }, cancellationToken);
                            break;

                        case AssetType.Custom:
                            if (string.IsNullOrEmpty(asset.CustomAssetType))
                            {
                                break;
                            }

                            var handlerKey = BuildHandlerKey(asset.CustomAssetType);
                            var handler = await _handlerStore.GetAsync(handlerKey, cancellationToken);
                            if (handler != null)
                            {
                                await _meshInvocationClient.InvokeMethodAsync<CustomHandlerReleaseRequest, CustomHandlerReleaseResponse>(
                                    handler.PluginId,
                                    handler.ReleaseEndpoint,
                                    new CustomHandlerReleaseRequest
                                    {
                                        EscrowId = agreement.EscrowId,
                                        CustomAssetType = asset.CustomAssetType,
                                        CustomAssetId = asset.CustomAssetId,
                                        CustomAssetData = asset.CustomAssetData,
                                        RecipientPartyId = allocation.RecipientPartyId
                                    }, cancellationToken);
                            }
                            break;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex,
                        "Release transfer failed for {AssetType} to recipient {RecipientId} in escrow {EscrowId}, continuing",
                        asset.AssetType, allocation.RecipientPartyId, agreement.EscrowId);
                }
            }
        }
    }

    /// <summary>
    /// Executes refund asset transfers AFTER the agreement has transitioned to Refunded/Cancelled/Expired.
    /// Returns deposits to depositors via Currency, transfers items back to source containers, unlocks contracts.
    /// Per-deposit, per-asset error isolation — transfer failures are logged but do NOT
    /// block the terminal transition.
    /// </summary>
    internal async Task ExecuteRefundTransfersAsync(
        EscrowAgreementModel agreement,
        CancellationToken cancellationToken)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.escrow", "EscrowService.ExecuteRefundTransfersAsync");

        if (agreement.Deposits == null || agreement.Deposits.Count == 0)
        {
            return;
        }

        foreach (var deposit in agreement.Deposits)
        {
            if (deposit.Assets?.Assets == null)
            {
                continue;
            }

            var party = agreement.Parties?.FirstOrDefault(p => p.PartyId == deposit.PartyId && p.PartyType == deposit.PartyType);

            foreach (var asset in deposit.Assets.Assets)
            {
                try
                {
                    switch (asset.AssetType)
                    {
                        case AssetType.Currency:
                            if (party?.WalletId == null || asset.CurrencyDefinitionId == null || asset.CurrencyAmount == null)
                            {
                                break;
                            }

                            await _currencyClient.EscrowRefundAsync(
                                new EscrowRefundRequest
                                {
                                    WalletId = party.WalletId.Value,
                                    CurrencyDefinitionId = asset.CurrencyDefinitionId.Value,
                                    Amount = asset.CurrencyAmount.Value,
                                    EscrowId = agreement.EscrowId,
                                    IdempotencyKey = $"escrow-refund:{agreement.EscrowId}:{deposit.PartyId}:{asset.CurrencyDefinitionId}"
                                }, cancellationToken);
                            break;

                        case AssetType.Item:
                        case AssetType.ItemStack:
                            if (asset.ItemInstanceId == null || asset.SourceContainerId == null)
                            {
                                break;
                            }

                            await _inventoryClient.TransferItemAsync(
                                new TransferItemRequest
                                {
                                    InstanceId = asset.ItemInstanceId.Value,
                                    TargetContainerId = asset.SourceContainerId.Value,
                                    Quantity = asset.ItemQuantity.HasValue ? (double)asset.ItemQuantity.Value : null
                                }, cancellationToken);
                            break;

                        case AssetType.Contract:
                            if (asset.ContractInstanceId == null)
                            {
                                break;
                            }

                            await _contractClient.UnlockContractAsync(
                                new UnlockContractRequest
                                {
                                    ContractInstanceId = asset.ContractInstanceId.Value,
                                    GuardianId = agreement.EscrowId,
                                    GuardianType = "escrow"
                                }, cancellationToken);
                            break;

                        case AssetType.Custom:
                            if (string.IsNullOrEmpty(asset.CustomAssetType))
                            {
                                break;
                            }

                            var handlerKey = BuildHandlerKey(asset.CustomAssetType);
                            var handler = await _handlerStore.GetAsync(handlerKey, cancellationToken);
                            if (handler != null)
                            {
                                await _meshInvocationClient.InvokeMethodAsync<CustomHandlerRefundRequest, CustomHandlerRefundResponse>(
                                    handler.PluginId,
                                    handler.RefundEndpoint,
                                    new CustomHandlerRefundRequest
                                    {
                                        EscrowId = agreement.EscrowId,
                                        CustomAssetType = asset.CustomAssetType,
                                        CustomAssetId = asset.CustomAssetId,
                                        CustomAssetData = asset.CustomAssetData,
                                        DepositorPartyId = deposit.PartyId
                                    }, cancellationToken);
                            }
                            break;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex,
                        "Refund transfer failed for {AssetType} to depositor {DepositorId} in escrow {EscrowId}, continuing",
                        asset.AssetType, deposit.PartyId, agreement.EscrowId);
                }
            }
        }
    }
    /// <summary>
    /// Emits an error event for unexpected failures.
    /// </summary>
    /// <param name="operation">Operation name.</param>
    /// <param name="error">Error message.</param>
    /// <param name="context">Additional context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task EmitErrorAsync(string operation, string error, object? context = null, CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.escrow", "EscrowService.EmitErrorAsync");
        _logger.LogError("Escrow operation {Operation} failed: {Error}", operation, error);
        await _messageBus.TryPublishErrorAsync(
            "escrow",
            operation,
            error,
            error,
            details: context,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Atomically decrements the pending escrow count for a party using optimistic concurrency.
    /// Only decrements if current count is greater than zero.
    /// </summary>
    /// <param name="partyId">The party ID.</param>
    /// <param name="partyType">The party type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal async Task DecrementPartyPendingCountAsync(Guid partyId, EntityType partyType, CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.escrow", "EscrowService.DecrementPartyPendingCountAsync");
        var partyKey = BuildPartyPendingKey(partyId, partyType);
        var now = DateTimeOffset.UtcNow;

        for (var attempt = 0; attempt < _configuration.MaxConcurrencyRetries; attempt++)
        {
            var (existing, etag) = await _partyPendingStore.GetWithETagAsync(partyKey, cancellationToken);
            if (existing == null || existing.PendingCount <= 0)
            {
                return;
            }

            existing.PendingCount--;
            existing.LastUpdated = now;

            // GetWithETagAsync returns non-null etag for existing records;
            // coalesce satisfies compiler's nullable analysis (will never execute)
            var saveResult = await _partyPendingStore.TrySaveAsync(partyKey, existing, etag ?? string.Empty, cancellationToken: cancellationToken);
            if (saveResult != null)
            {
                return;
            }

            _logger.LogDebug("Concurrent modification on party pending count {PartyKey}, retrying decrement (attempt {Attempt})",
                partyKey, attempt + 1);
        }

        _logger.LogWarning("Failed to decrement party pending count for {PartyType}:{PartyId} after {MaxRetries} attempts",
            partyType, partyId, _configuration.MaxConcurrencyRetries);
    }

    /// <summary>
    /// Atomically increments the pending escrow count for a party using optimistic concurrency.
    /// </summary>
    /// <param name="partyId">The party ID.</param>
    /// <param name="partyType">The party type.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal async Task IncrementPartyPendingCountAsync(Guid partyId, EntityType partyType, CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.escrow", "EscrowService.IncrementPartyPendingCountAsync");
        var partyKey = BuildPartyPendingKey(partyId, partyType);
        var now = DateTimeOffset.UtcNow;

        for (var attempt = 0; attempt < _configuration.MaxConcurrencyRetries; attempt++)
        {
            var (existing, etag) = await _partyPendingStore.GetWithETagAsync(partyKey, cancellationToken);
            var newCount = new PartyPendingCount
            {
                PartyId = partyId,
                PartyType = partyType,
                PendingCount = (existing?.PendingCount ?? 0) + 1,
                LastUpdated = now
            };

            // etag is null when key doesn't exist yet; empty string signals
            // "create new" to TrySaveAsync (will never conflict on new entries)
            var saveResult = await _partyPendingStore.TrySaveAsync(partyKey, newCount, etag ?? string.Empty, cancellationToken: cancellationToken);
            if (saveResult != null)
            {
                return;
            }

            _logger.LogDebug("Concurrent modification on party pending count {PartyKey}, retrying increment (attempt {Attempt})",
                partyKey, attempt + 1);
        }

        _logger.LogWarning("Failed to increment party pending count for {PartyType}:{PartyId} after {MaxRetries} attempts",
            partyType, partyId, _configuration.MaxConcurrencyRetries);
    }
}
