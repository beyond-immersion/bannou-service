using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Item;

/// <summary>
/// Static dispatch utility for invoking <see cref="IItemInstanceDestructionListener"/> callbacks.
/// Called by ItemService after item instance deletion to notify co-located L4 consumers.
/// </summary>
/// <remarks>
/// Dispatch pattern: early-returns if no listeners, wraps each listener call in try-catch
/// with warning log, and never rethrows. Per FOUNDATION TENETS (High-Frequency Instance
/// Lifecycle Exception): listeners write to distributed state; orphan reconciliation workers
/// provide the durability guarantee.
/// </remarks>
internal static class ItemInstanceDestructionDispatcher
{
    /// <summary>
    /// Dispatches destruction notifications to all registered listeners.
    /// </summary>
    /// <param name="listeners">Registered destruction listeners.</param>
    /// <param name="notification">Destruction notification to dispatch.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    /// <param name="logger">Logger for warning on listener failure.</param>
    /// <param name="ct">Cancellation token.</param>
    internal static async Task DispatchInstanceDestroyedAsync(
        IReadOnlyList<IItemInstanceDestructionListener> listeners,
        ItemInstanceDestructionNotification notification,
        ITelemetryProvider telemetryProvider,
        ILogger logger,
        CancellationToken ct)
    {
        if (listeners.Count == 0) return;

        using var activity = telemetryProvider.StartActivity(
            "bannou.item", "ItemInstanceDestructionDispatcher.DispatchInstanceDestroyed");

        foreach (var listener in listeners)
        {
            try
            {
                await listener.OnItemInstanceDestroyedAsync(notification, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Item destruction listener {ListenerType} failed for instance {InstanceId}",
                    listener.GetType().Name, notification.InstanceId);
            }
        }
    }
}
