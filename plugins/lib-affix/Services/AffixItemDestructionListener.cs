using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Affix;

/// <summary>
/// Singleton listener for item instance destruction notifications from lib-item (L2).
/// Cleans up affix instance data, cache entries, and reverse indexes when items are destroyed.
/// Registered as Singleton per DI listener pattern (FOUNDATION TENETS high-frequency exception).
/// </summary>
/// <remarks>
/// This listener writes to distributed state (MySQL + Redis), making it safe for single-node
/// dispatch. The OrphanReconciliationWorker provides the durability guarantee for any missed
/// notifications (node partitioning, listener exceptions, deployment gaps).
/// </remarks>
public class AffixItemDestructionListener : IItemInstanceDestructionListener
{
    private readonly IServiceProvider _serviceProvider;
    private readonly AffixInstanceEventBatcher _instanceEventBatcher;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly ILogger<AffixItemDestructionListener> _logger;

    /// <summary>
    /// Initializes the destruction listener with DI dependencies.
    /// </summary>
    /// <param name="serviceProvider">Service provider for creating scoped state store access.</param>
    /// <param name="instanceEventBatcher">Batcher for destruction lifecycle events.</param>
    /// <param name="telemetryProvider">Telemetry provider for span instrumentation.</param>
    /// <param name="logger">Logger instance.</param>
    public AffixItemDestructionListener(
        IServiceProvider serviceProvider,
        AffixInstanceEventBatcher instanceEventBatcher,
        ITelemetryProvider telemetryProvider,
        ILogger<AffixItemDestructionListener> logger)
    {
        _serviceProvider = serviceProvider;
        _instanceEventBatcher = instanceEventBatcher;
        _telemetryProvider = telemetryProvider;
        _logger = logger;
    }

    /// <summary>
    /// Handles item instance destruction by cleaning up all affix data for the destroyed item.
    /// </summary>
    /// <param name="notification">Details of the destroyed item instance.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task OnItemInstanceDestroyedAsync(ItemInstanceDestructionNotification notification, CancellationToken ct)
    {
        using var activity = _telemetryProvider.StartActivity(
            "bannou.affix", "AffixItemDestructionListener.OnItemInstanceDestroyed");

        using var scope = _serviceProvider.CreateScope();
        var stateStoreFactory = scope.ServiceProvider.GetRequiredService<IStateStoreFactory>();

        // Acquire all stores once per scope (per FOUNDATION TENETS background service store access)
        var instanceStore = stateStoreFactory.GetStore<AffixInstanceModel>(StateStoreDefinitions.AffixInstances);
        var instanceStringStore = stateStoreFactory.GetStore<string>(StateStoreDefinitions.AffixInstances);
        var instanceCache = stateStoreFactory.GetStore<AffixInstanceModel>(StateStoreDefinitions.AffixInstanceCache);
        var statsCache = stateStoreFactory.GetStore<ComputedStatsModel>(StateStoreDefinitions.AffixInstanceCache);

        var instanceKey = AffixService.BuildInstanceKey(notification.InstanceId);
        var instance = await instanceStore.GetAsync(instanceKey, ct);

        if (instance == null)
        {
            _logger.LogDebug("No affix instance found for destroyed item {InstanceId}, skipping cleanup", notification.InstanceId);
            return;
        }

        _logger.LogDebug("Cleaning up affix instance for destroyed item {InstanceId}", notification.InstanceId);

        // Clean reverse index entries for all slots referencing definitions
        foreach (var slot in instance.AllSlots())
        {
            await instanceStringStore.RemoveFromStringListAsync(
                AffixService.BuildInstancesByDefinitionKey(slot.DefinitionId),
                notification.InstanceId.ToString(),
                3,
                _logger,
                ct);
        }

        // Delete instance from persistent store and caches
        await instanceStore.DeleteAsync(instanceKey, ct);
        await instanceCache.DeleteAsync(AffixService.BuildInstanceCacheKey(notification.InstanceId), ct);
        await statsCache.DeleteAsync(AffixService.BuildStatsCacheKey(notification.InstanceId), ct);

        // Feed batch lifecycle destruction event
        _instanceEventBatcher.AddDestroyed(new Events.AffixInstanceBatchDestroyedEntry
        {
            ItemInstanceId = notification.InstanceId,
            GameServiceId = instance.GameServiceId,
            EffectiveRarity = instance.EffectiveRarity,
            ItemLevel = instance.ItemLevel,
            Quality = instance.Quality,
            CreatedAt = instance.CreatedAt,
            UpdatedAt = notification.DestroyedAt
        });

        _logger.LogInformation("Cleaned up affix instance for destroyed item {InstanceId}", notification.InstanceId);
    }
}
