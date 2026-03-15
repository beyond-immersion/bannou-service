using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Item;

/// <summary>
/// Plugin wrapper for Item service enabling plugin-based discovery and lifecycle management.
/// Registers ItemInstanceEventBatcher and its flush worker for batch lifecycle event publishing.
/// </summary>
public class ItemServicePlugin : StandardServicePlugin<IItemService>
{
    public override string PluginName => "item";
    public override string DisplayName => "Item Service";

    /// <summary>
    /// Registers Item-specific DI services: instance lifecycle event batcher and its worker.
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        // Register instance lifecycle event batcher as Singleton.
        // ItemService (Scoped) injects it to call Add* synchronously.
        services.AddSingleton<ItemInstanceEventBatcher>();

        // Single worker flushes all three batchers (created, modified, destroyed) per cycle.
        services.AddSingleton<IHostedService>(sp =>
        {
            var batcher = sp.GetRequiredService<ItemInstanceEventBatcher>();
            var config = sp.GetRequiredService<ItemServiceConfiguration>();
            return new EventBatcherWorker(
                batcher.AllFlushables,
                sp,
                sp.GetRequiredService<ILogger<EventBatcherWorker>>(),
                sp.GetRequiredService<ITelemetryProvider>(),
                config.InstanceEventBatchIntervalSeconds,
                config.InstanceEventBatchStartupDelaySeconds,
                "item",
                "InstanceEventBatcher");
        });
    }
}
