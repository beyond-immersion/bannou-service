// =============================================================================
// Currency Service Events
// Event consumer registration and handlers for cache invalidation and
// account deletion cleanup.
// =============================================================================

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Currency.Caching;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Messaging;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using static BeyondImmersion.BannouService.Currency.CurrencyKeys;

namespace BeyondImmersion.BannouService.Currency;

/// <summary>
/// Partial class for CurrencyService event handling.
/// Contains event consumer registration and handler implementations for cache invalidation
/// and account deletion cleanup per FOUNDATION TENETS (Account Deletion Cleanup Obligation).
/// Note: Non-account entity cleanup (characters, guilds) uses lib-resource (x-references), not event subscription.
/// </summary>
public partial class CurrencyService : IBannouService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor via IBannouService interface.
    /// </summary>
    void IBannouService.RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        // Account deletion cleanup per FOUNDATION TENETS (Account Deletion Cleanup Obligation).
        // Account-owned wallets must be deleted when the owning account is deleted.
        eventConsumer.RegisterHandler<ICurrencyService, AccountDeletedEvent>(
            "account.deleted",
            async (svc, evt) => await ((CurrencyService)svc).HandleAccountDeletedAsync(evt));

        // Self-subscribe to balance-changing events for cache invalidation.
        // When currency is credited, debited, or transferred, invalidate the cache so
        // running actors get fresh data on their next behavior tick.
        eventConsumer.RegisterHandler<ICurrencyService, CurrencyCreditedEvent>(
            "currency.credited",
            async (svc, evt) => await ((CurrencyService)svc).HandleCurrencyCreditedAsync(evt));

        eventConsumer.RegisterHandler<ICurrencyService, CurrencyDebitedEvent>(
            "currency.debited",
            async (svc, evt) => await ((CurrencyService)svc).HandleCurrencyDebitedAsync(evt));

        eventConsumer.RegisterHandler<ICurrencyService, CurrencyTransferredEvent>(
            "currency.transferred",
            async (svc, evt) => await ((CurrencyService)svc).HandleCurrencyTransferredAsync(evt));
    }

    /// <summary>
    /// Handles account.deleted events by cleaning up all account-owned wallets and their
    /// associated data (balances, holds, transactions, indexes, caches).
    /// Per FOUNDATION TENETS: Account deletion is always CASCADE — data has no owner and must be removed.
    /// </summary>
    internal async Task HandleAccountDeletedAsync(AccountDeletedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.currency", "CurrencyService.HandleAccountDeleted");
        _logger.LogInformation("Handling account.deleted for account {AccountId}", evt.AccountId);

        try
        {
            await CleanupWalletsForAccountAsync(evt.AccountId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clean up wallets for account {AccountId}", evt.AccountId);
            await _messageBus.TryPublishErrorAsync(
                "currency",
                "CleanupWalletsForAccount",
                "cleanup_failed",
                ex.Message,
                dependency: null,
                endpoint: "account.deleted",
                details: $"accountId={evt.AccountId}",
                stack: ex.StackTrace);
        }
    }

    /// <summary>
    /// Cleans up all wallets for a given account by deleting wallet records, balances, holds,
    /// transactions, and all associated indexes and caches. Per-wallet failures are logged as
    /// warnings and do not abort the overall cleanup. Infrastructure exceptions propagate to caller.
    /// </summary>
    /// <param name="accountId">The account whose wallets should be cleaned up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal async Task<int> CleanupWalletsForAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.currency", "CurrencyService.CleanupWalletsForAccount");

        var wallets = await _walletQueryStore.QueryAsync(
            w => w.OwnerId == accountId && w.OwnerType == EntityType.Account,
            cancellationToken: cancellationToken);

        if (wallets.Count == 0)
        {
            _logger.LogDebug("No wallets found for account {AccountId}, skipping cleanup", accountId);
            return 0;
        }

        var deletedCount = 0;
        foreach (var wallet in wallets)
        {
            try
            {
                // Delete all balances for this wallet
                var balanceListJson = await _balanceStringStore.GetAsync(
                    $"{BALANCE_WALLET_INDEX}{wallet.WalletId}", cancellationToken);
                if (!string.IsNullOrEmpty(balanceListJson))
                {
                    var currencyDefIds = BannouJson.Deserialize<List<string>>(balanceListJson);
                    if (currencyDefIds != null)
                    {
                        foreach (var defId in currencyDefIds)
                        {
                            var balKey = $"{BALANCE_PREFIX}{wallet.WalletId}:{defId}";
                            await _balanceStore.DeleteAsync(balKey, cancellationToken);
                            await _balanceCacheStore.DeleteAsync(balKey, cancellationToken);

                            // Remove wallet from the currency's reverse index
                            await RemoveFromListAsync(
                                _balanceStringStore,
                                $"{BALANCE_CURRENCY_INDEX}{defId}",
                                wallet.WalletId.ToString(),
                                cancellationToken);

                            // Delete all holds for this wallet+currency
                            var holdListJson = await _holdStringStore.GetAsync(
                                $"{HOLD_WALLET_INDEX}{wallet.WalletId}:{defId}", cancellationToken);
                            if (!string.IsNullOrEmpty(holdListJson))
                            {
                                var holdIds = BannouJson.Deserialize<List<string>>(holdListJson);
                                if (holdIds != null)
                                {
                                    foreach (var holdId in holdIds)
                                    {
                                        await _holdStore.DeleteAsync($"{HOLD_PREFIX}{holdId}", cancellationToken);
                                        await _holdCacheStore.DeleteAsync($"{HOLD_PREFIX}{holdId}", cancellationToken);
                                    }
                                }
                                await _holdStringStore.DeleteAsync(
                                    $"{HOLD_WALLET_INDEX}{wallet.WalletId}:{defId}", cancellationToken);
                            }
                        }
                    }
                    await _balanceStringStore.DeleteAsync(
                        $"{BALANCE_WALLET_INDEX}{wallet.WalletId}", cancellationToken);
                }

                // Delete all transactions for this wallet
                var txListJson = await _transactionStringStore.GetAsync(
                    $"{TX_WALLET_INDEX}{wallet.WalletId}", cancellationToken);
                if (!string.IsNullOrEmpty(txListJson))
                {
                    var txIds = BannouJson.Deserialize<List<string>>(txListJson);
                    if (txIds != null)
                    {
                        foreach (var txId in txIds)
                        {
                            await _transactionStore.DeleteAsync($"{TX_PREFIX}{txId}", cancellationToken);
                        }
                    }
                    await _transactionStringStore.DeleteAsync(
                        $"{TX_WALLET_INDEX}{wallet.WalletId}", cancellationToken);
                }

                // Delete the wallet-owner index key
                var ownerKey = BuildOwnerKey(wallet.OwnerId, wallet.OwnerType, wallet.RealmId);
                await _walletStringStore.DeleteAsync($"{WALLET_OWNER_INDEX}{ownerKey}", cancellationToken);

                // Delete the wallet record itself
                await _walletStore.DeleteAsync($"{WALLET_PREFIX}{wallet.WalletId}", cancellationToken);

                // Invalidate currency cache for this owner
                _currencyCache.Invalidate(wallet.OwnerId);

                deletedCount++;
                _logger.LogDebug(
                    "Deleted wallet {WalletId} for account {AccountId}",
                    wallet.WalletId, accountId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to delete wallet {WalletId} for account {AccountId}, continuing with remaining wallets",
                    wallet.WalletId, accountId);
            }
        }

        _logger.LogInformation(
            "Cleaned up {DeletedCount}/{TotalCount} wallets for account {AccountId}",
            deletedCount, wallets.Count, accountId);

        return deletedCount;
    }

    /// <summary>
    /// Handles currency.credited events.
    /// Invalidates the currency cache for the affected character.
    /// </summary>
    private async Task HandleCurrencyCreditedAsync(CurrencyCreditedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.currency", "CurrencyService.HandleCurrencyCreditedAsync");
        _logger.LogDebug(
            "Received currency.credited event for owner {OwnerId}, invalidating cache",
            evt.OwnerId);

        _currencyCache.Invalidate(evt.OwnerId);

        // Yield to honor async contract per IMPLEMENTATION TENETS
        await Task.Yield();
    }

    /// <summary>
    /// Handles currency.debited events.
    /// Invalidates the currency cache for the affected character.
    /// </summary>
    private async Task HandleCurrencyDebitedAsync(CurrencyDebitedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.currency", "CurrencyService.HandleCurrencyDebitedAsync");
        _logger.LogDebug(
            "Received currency.debited event for owner {OwnerId}, invalidating cache",
            evt.OwnerId);

        _currencyCache.Invalidate(evt.OwnerId);

        // Yield to honor async contract per IMPLEMENTATION TENETS
        await Task.Yield();
    }

    /// <summary>
    /// Handles currency.transferred events.
    /// Invalidates the currency cache for both source and target owners.
    /// </summary>
    private async Task HandleCurrencyTransferredAsync(CurrencyTransferredEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.currency", "CurrencyService.HandleCurrencyTransferredAsync");
        _logger.LogDebug(
            "Received currency.transferred event from {SourceOwnerId} to {TargetOwnerId}, invalidating caches",
            evt.SourceOwnerId, evt.TargetOwnerId);

        _currencyCache.Invalidate(evt.SourceOwnerId);
        _currencyCache.Invalidate(evt.TargetOwnerId);

        // Yield to honor async contract per IMPLEMENTATION TENETS
        await Task.Yield();
    }
}
