// =============================================================================
// Currency Service Events
// Event consumer registration and handlers for cache invalidation.
// =============================================================================

using BeyondImmersion.BannouService.Currency.Caching;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace BeyondImmersion.BannouService.Currency;

/// <summary>
/// Partial class for CurrencyService event handling.
/// Contains event consumer registration and handler implementations for cache invalidation.
/// </summary>
public partial class CurrencyService : IBannouService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor via IBannouService interface.
    /// </summary>
    void IBannouService.RegisterEventConsumers(IEventConsumer eventConsumer)
    {
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
