// =============================================================================
// Currency Transaction Listener Interface
// Enables in-process notification when wallet balances are mutated.
// Currency (L2) discovers listeners via DI; co-located L2 services implement them
// for targeted dispatch of credit/debit/transfer/autogain/escrow events.
// =============================================================================

using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Currency;

namespace BeyondImmersion.BannouService.Providers;

/// <summary>
/// Listener interface for receiving currency wallet mutation notifications via DI.
/// </summary>
/// <remarks>
/// <para>
/// This interface enables guaranteed in-process delivery of wallet mutation events
/// to co-located L2 consumers without requiring them to subscribe to the full
/// broadcast event firehose (<c>currency.credited</c>, <c>currency.debited</c>,
/// <c>currency.transferred</c>, <c>currency.autogain.calculated</c>):
/// </para>
/// <list type="bullet">
///   <item>Currency (L2) discovers this interface via <c>IEnumerable&lt;ICurrencyTransactionListener&gt;</c></item>
///   <item>L2 services implement the listener and register as Singleton</item>
///   <item>Currency calls listeners AFTER the balance is saved and broadcast events are published</item>
///   <item>Listener failures are logged as warnings and never affect core currency logic or other listeners</item>
/// </list>
/// <para>
/// <b>Why DI instead of events?</b> The canonical consumer is Genesis (L2), which must
/// route wallet mutations to genesis-managed entities as inputs to the seed growth
/// pipeline. Both Currency and Genesis are L2 (guaranteed co-located, guaranteed
/// available). Genesis's listener does microsecond-fast in-memory filtering against
/// a <c>ConcurrentDictionary&lt;walletId, WalletMapping&gt;</c> — unsuitable for the
/// event bus given autogain processing volumes (tens of thousands of wallets per tick).
/// </para>
/// <para>
/// <strong>DISTRIBUTED DEPLOYMENT NOTE</strong>: Listeners are LOCAL-ONLY fan-out.
/// Only listeners co-located on the same node where the wallet mutation occurs are
/// called. In a multi-node deployment, nodes that do not process the credit/debit/transfer
/// request will NOT have their listeners invoked.
/// </para>
/// <para>
/// This is safe when the listener's reaction writes to distributed state (Redis, MySQL)
/// because all nodes read from the same distributed store. The Genesis implementation
/// buffers credits in a per-node accumulator and flushes them to the distributed Seed
/// service via a background worker — the eventual write-through to distributed state
/// satisfies multi-node safety, even though the in-memory accumulator is per-node.
/// </para>
/// <para>
/// The canonical Currency broadcast events (<c>currency.credited</c>, <c>currency.debited</c>,
/// <c>currency.transferred</c>) are still published via <c>IMessageBus</c> for any
/// distributed consumer that needs cross-node delivery. The listener is an optimization
/// for co-located services, not a replacement for distributed event subscriptions.
/// </para>
/// <para>
/// <b>Example Implementation</b>:
/// </para>
/// <code>
/// public class GenesisCurrencyTransactionListener : ICurrencyTransactionListener
/// {
///     private readonly GenesisGrowthState _state;
///
///     public Task OnCurrencyCreditedAsync(CurrencyTransactionNotification n, CancellationToken ct)
///     {
///         if (!_state.WalletMap.TryGetValue(n.WalletId, out var mapping))
///             return Task.CompletedTask;  // Not a genesis-managed wallet (microseconds)
///         _state.BufferGrowth(mapping.EntityId, mapping.WalletCode(n.WalletId), n.Amount, GrowthDirection.Credit);
///         return Task.CompletedTask;
///     }
///     // ...
/// }
/// // DI registration: services.AddSingleton&lt;ICurrencyTransactionListener, GenesisCurrencyTransactionListener&gt;();
/// </code>
/// </remarks>
public interface ICurrencyTransactionListener
{
    /// <summary>
    /// Called after a currency credit is successfully applied to a wallet balance.
    /// </summary>
    /// <param name="notification">Details of the credit including wallet, owner, amount, and new balance.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// Fired from: <c>CreditCurrencyAsync</c>, <c>TransferCurrencyAsync</c> (target side),
    /// <c>EscrowReleaseAsync</c>, <c>EscrowRefundAsync</c>, and autogain processing
    /// (both lazy and task-based). Implementations should handle their own errors
    /// internally. Exceptions thrown from this method are caught by Currency and logged
    /// as warnings — they never affect the credit operation, other listeners, or the caller.
    /// </remarks>
    Task OnCurrencyCreditedAsync(CurrencyTransactionNotification notification, CancellationToken ct);

    /// <summary>
    /// Called after a currency debit is successfully applied to a wallet balance.
    /// </summary>
    /// <param name="notification">Details of the debit including wallet, owner, amount, and new balance.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// Fired from: <c>DebitCurrencyAsync</c>, <c>TransferCurrencyAsync</c> (source side),
    /// <c>EscrowDepositAsync</c>. Implementations should handle their own errors
    /// internally. Exceptions thrown from this method are caught by Currency and logged
    /// as warnings — they never affect the debit operation, other listeners, or the caller.
    /// </remarks>
    Task OnCurrencyDebitedAsync(CurrencyTransactionNotification notification, CancellationToken ct);
}

/// <summary>
/// Notification data for a currency wallet mutation, delivered via DI to currency transaction listeners.
/// </summary>
/// <param name="WalletId">The wallet whose balance changed.</param>
/// <param name="OwnerId">Entity that owns the wallet.</param>
/// <param name="OwnerType">Owner entity type discriminator.</param>
/// <param name="CurrencyDefinitionId">Currency definition the balance is denominated in.</param>
/// <param name="CurrencyCode">Currency code for consumer filtering (e.g., "mana", "weapon_experience").</param>
/// <param name="Amount">
/// Absolute magnitude of the mutation (always positive). The direction (credit vs debit)
/// is communicated by which method the listener is invoked on.
/// </param>
/// <param name="NewBalance">Resulting wallet balance after the mutation is applied.</param>
/// <param name="TransactionType">Classification of the transaction (autogain, quest reward, trade, etc.).</param>
/// <param name="OccurredAt">When the mutation occurred (UTC).</param>
public record CurrencyTransactionNotification(
    Guid WalletId,
    Guid OwnerId,
    EntityType OwnerType,
    Guid CurrencyDefinitionId,
    string CurrencyCode,
    double Amount,
    double NewBalance,
    TransactionType TransactionType,
    DateTimeOffset OccurredAt);
