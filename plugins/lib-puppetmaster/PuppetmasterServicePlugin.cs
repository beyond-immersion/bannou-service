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
    // All helper services auto-registered via [BannouHelperService]:
    // - BehaviorDocumentCache (DependencyMode.Both → concrete + IBehaviorDocumentCache)
    // - LoadSnapshotHandler, PrefetchSnapshotsHandler, SpawnWatcherHandler, StopWatcherHandler,
    //   ListWatchersHandler, WatchHandler, UnwatchHandler (DependencyMode.Both → concrete + IActionHandler)
    // - WatchRegistry, ResourceEventMapping (DependencyMode.Concrete)
}
