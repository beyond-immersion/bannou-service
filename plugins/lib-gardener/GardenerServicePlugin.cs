using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace BeyondImmersion.BannouService.Gardener;

/// <summary>
/// Plugin wrapper for Gardener service enabling plugin-based discovery and lifecycle management.
/// Registers background workers for void orchestration and scenario lifecycle management.
/// </summary>
public class GardenerServicePlugin : StandardServicePlugin<IGardenerService>
{
    /// <inheritdoc />
    public override string PluginName => "gardener";

    /// <inheritdoc />
    public override string DisplayName => "Gardener Service";

    /// <inheritdoc />
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        // Background workers for periodic processing
        services.AddHostedService<GardenerScenarioLifecycleWorker>();

        // Register ISeedEvolutionListener so Seed (L2) dispatches growth/phase/capability
        // notifications to Gardener (L4) via DI. This replaces broadcast event subscriptions
        // for seed.growth.updated and seed.phase.changed.
        services.AddSingleton<ISeedEvolutionListener, GardenerSeedEvolutionListener>();
    }
}
