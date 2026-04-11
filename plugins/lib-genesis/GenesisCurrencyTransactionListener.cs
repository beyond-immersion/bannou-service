using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Genesis;

/// <summary>
/// Listens for currency wallet mutations and routes them to genesis entities for growth processing.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the microsecond-fast filtering layer of the Genesis runtime pipeline.</b> Every currency
/// credit, debit, transfer, autogain, and escrow operation in the system dispatches through this
/// listener. The overwhelming majority of those transactions are for non-genesis wallets and must be
/// discarded with zero overhead beyond a single <c>ConcurrentDictionary</c> lookup.
/// </para>
/// <para>
/// Matched mutations are buffered into <see cref="GenesisGrowthState"/>'s growth accumulator and then
/// processed asynchronously by <see cref="Services.GenesisGrowthFlushWorkerService"/>. This listener
/// does NO state store access, NO distributed locking, NO network I/O — just map lookup and bag append.
/// </para>
/// <para>
/// <b>Distributed safety:</b> The wallet map is per-node. When a currency mutation fires on Node A,
/// only Node A's listener runs. If the mutated wallet belongs to a genesis entity created on Node B,
/// Node A learns about the mapping via the broadcast <c>genesis.entity.created</c> event (handled
/// in <see cref="GenesisService.HandleGenesisEntityCreatedAsync"/>). Until that broadcast event arrives,
/// the listener misses the wallet and the growth is lost — but Currency and Genesis are both L2 and
/// guaranteed co-located in current deployment modes, so this window is zero in practice. If a future
/// deployment mode separates Currency and Genesis across nodes, this design would need to be revisited
/// with a distributed wallet map.
/// </para>
/// <para>
/// <b>Lifetime:</b> Registered as Singleton via <see cref="BannouHelperServiceAttribute"/>. Must be a
/// separate class from the scoped <see cref="GenesisService"/> because Currency's listener collection
/// is resolved from the singleton root provider.
/// </para>
/// </remarks>
[BannouHelperService("genesis-currency-transaction", typeof(IGenesisService), typeof(ICurrencyTransactionListener), lifetime: ServiceLifetime.Singleton)]
public class GenesisCurrencyTransactionListener : ICurrencyTransactionListener
{
    private readonly GenesisGrowthState _state;
    private readonly ILogger<GenesisCurrencyTransactionListener> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GenesisCurrencyTransactionListener"/> class.
    /// </summary>
    /// <param name="state">Shared in-memory state for the Genesis runtime pipeline.</param>
    /// <param name="logger">Logger instance.</param>
    public GenesisCurrencyTransactionListener(
        GenesisGrowthState state,
        ILogger<GenesisCurrencyTransactionListener> logger)
    {
        _state = state;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task OnCurrencyCreditedAsync(CurrencyTransactionNotification notification, CancellationToken ct)
    {
        // Fast path: in-memory map lookup. The overwhelming majority of wallets are non-genesis.
        if (!_state.WalletMap.TryGetValue(notification.WalletId, out var mapping))
            return Task.CompletedTask;

        _state.BufferGrowth(
            mapping.EntityId,
            new GrowthBufferEntry(
                WalletCode: mapping.WalletCode,
                Amount: notification.Amount,
                Direction: GrowthDirection.Credit));

        _logger.LogDebug(
            "Buffered credit for genesis entity {EntityId}: wallet {WalletCode}, amount {Amount}",
            mapping.EntityId, mapping.WalletCode, notification.Amount);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task OnCurrencyDebitedAsync(CurrencyTransactionNotification notification, CancellationToken ct)
    {
        if (!_state.WalletMap.TryGetValue(notification.WalletId, out var mapping))
            return Task.CompletedTask;

        _state.BufferGrowth(
            mapping.EntityId,
            new GrowthBufferEntry(
                WalletCode: mapping.WalletCode,
                Amount: notification.Amount,
                Direction: GrowthDirection.Debit));

        _logger.LogDebug(
            "Buffered debit for genesis entity {EntityId}: wallet {WalletCode}, amount {Amount}",
            mapping.EntityId, mapping.WalletCode, notification.Amount);

        return Task.CompletedTask;
    }
}
