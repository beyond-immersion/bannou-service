using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Currency;

/// <summary>
/// Static dispatch utility for invoking <see cref="ICurrencyTransactionListener"/> callbacks.
/// Shared by CurrencyService and CurrencyAutogainTaskService to avoid dispatch logic duplication.
/// </summary>
/// <remarks>
/// <para>
/// Each dispatch method: early-returns if no listeners, wraps each listener call in try-catch
/// with warning log, and never rethrows. The currency operation that triggered the dispatch
/// must not be affected by listener failures — listeners are an optimization, not a dependency.
/// </para>
/// <para>
/// This mirrors <c>SeedEvolutionDispatcher</c> in <c>lib-seed</c>. See the documentation on
/// <see cref="ICurrencyTransactionListener"/> for the distributed safety model.
/// </para>
/// </remarks>
internal static class CurrencyTransactionDispatcher
{
    /// <summary>
    /// Dispatches credit notifications to all registered listeners.
    /// </summary>
    /// <param name="listeners">Registered currency transaction listeners.</param>
    /// <param name="notification">Credit notification to dispatch.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    /// <param name="logger">Logger for warning on listener failure.</param>
    /// <param name="ct">Cancellation token.</param>
    internal static async Task DispatchCreditedAsync(
        IReadOnlyList<ICurrencyTransactionListener> listeners,
        CurrencyTransactionNotification notification,
        ITelemetryProvider telemetryProvider,
        ILogger logger,
        CancellationToken ct)
    {
        if (listeners.Count == 0) return;

        using var activity = telemetryProvider.StartActivity(
            "bannou.currency", "CurrencyTransactionDispatcher.DispatchCredited");

        foreach (var listener in listeners)
        {
            try
            {
                await listener.OnCurrencyCreditedAsync(notification, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Currency transaction listener {ListenerType} failed during OnCurrencyCreditedAsync for wallet {WalletId}",
                    listener.GetType().Name, notification.WalletId);
            }
        }
    }

    /// <summary>
    /// Dispatches debit notifications to all registered listeners.
    /// </summary>
    /// <param name="listeners">Registered currency transaction listeners.</param>
    /// <param name="notification">Debit notification to dispatch.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    /// <param name="logger">Logger for warning on listener failure.</param>
    /// <param name="ct">Cancellation token.</param>
    internal static async Task DispatchDebitedAsync(
        IReadOnlyList<ICurrencyTransactionListener> listeners,
        CurrencyTransactionNotification notification,
        ITelemetryProvider telemetryProvider,
        ILogger logger,
        CancellationToken ct)
    {
        if (listeners.Count == 0) return;

        using var activity = telemetryProvider.StartActivity(
            "bannou.currency", "CurrencyTransactionDispatcher.DispatchDebited");

        foreach (var listener in listeners)
        {
            try
            {
                await listener.OnCurrencyDebitedAsync(notification, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Currency transaction listener {ListenerType} failed during OnCurrencyDebitedAsync for wallet {WalletId}",
                    listener.GetType().Name, notification.WalletId);
            }
        }
    }
}
