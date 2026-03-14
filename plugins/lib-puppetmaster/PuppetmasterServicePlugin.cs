using BeyondImmersion.BannouService.Abml.Execution;
using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Puppetmaster.Caching;
using BeyondImmersion.BannouService.Puppetmaster.Handlers;
using BeyondImmersion.BannouService.Puppetmaster.Watches;
using Microsoft.Extensions.DependencyInjection;

namespace BeyondImmersion.BannouService.Puppetmaster;

/// <summary>
/// Plugin wrapper for Puppetmaster service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class PuppetmasterServicePlugin : StandardServicePlugin<IPuppetmasterService>
{
    /// <inheritdoc />
    public override string PluginName => "puppetmaster";

    /// <inheritdoc />
    public override string DisplayName => "Puppetmaster Service";

    /// <summary>
    /// Configure services for dependency injection.
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        // Register the behavior document cache as singleton (in-memory caching)
        services.AddSingleton<BehaviorDocumentCache>();
        services.AddSingleton<IBehaviorDocumentCache>(sp => sp.GetRequiredService<BehaviorDocumentCache>());

        // Register load_snapshot handler for ABML execution
        // This enables Event Brain actors to load resource snapshots via load_snapshot: action
        // The handler is discovered by DocumentExecutorFactory via GetServices<IActionHandler>()
        services.AddSingleton<LoadSnapshotHandler>();
        services.AddSingleton<IActionHandler>(sp => sp.GetRequiredService<LoadSnapshotHandler>());

        // Register prefetch_snapshots handler for ABML execution
        // This enables Event Brain actors to batch-prefetch resource snapshots before iteration
        services.AddSingleton<PrefetchSnapshotsHandler>();
        services.AddSingleton<IActionHandler>(sp => sp.GetRequiredService<PrefetchSnapshotsHandler>());

        // Register watcher management handlers for ABML execution
        // These enable Event Brain actors to spawn, stop, and list regional watchers
        services.AddSingleton<SpawnWatcherHandler>();
        services.AddSingleton<IActionHandler>(sp => sp.GetRequiredService<SpawnWatcherHandler>());

        services.AddSingleton<StopWatcherHandler>();
        services.AddSingleton<IActionHandler>(sp => sp.GetRequiredService<StopWatcherHandler>());

        services.AddSingleton<ListWatchersHandler>();
        services.AddSingleton<IActionHandler>(sp => sp.GetRequiredService<ListWatchersHandler>());

        // Register watch registry for resource change subscriptions
        // This is an in-memory, ephemeral registry - watches are lost on restart
        services.AddSingleton<WatchRegistry>();

        // Register resource event mapping for lifecycle event → resource type mapping
        services.AddSingleton<ResourceEventMapping>();

        // Register watch/unwatch handlers for ABML execution
        // These enable Event Brain actors to subscribe to resource change notifications
        services.AddSingleton<WatchHandler>();
        services.AddSingleton<IActionHandler>(sp => sp.GetRequiredService<WatchHandler>());

        services.AddSingleton<UnwatchHandler>();
        services.AddSingleton<IActionHandler>(sp => sp.GetRequiredService<UnwatchHandler>());
    }
}
