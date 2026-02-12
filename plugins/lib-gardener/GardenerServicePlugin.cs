using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace BeyondImmersion.BannouService.Gardener;

/// <summary>
/// Plugin wrapper for Gardener service enabling plugin-based discovery and lifecycle management.
/// Registers background workers and DI listener implementations.
/// </summary>
/// <remarks>
/// <para>
/// <b>IMPLEMENTATION TENETS - DI Listener:</b> Registers GardenerService as an
/// ISeedEvolutionListener for local-only fan-out of seed growth/phase notifications.
/// Reactions write to distributed state (Redis), ensuring other nodes see updates.
/// </para>
/// </remarks>
public class GardenerServicePlugin : StandardServicePlugin<IGardenerService>
{
    /// <inheritdoc />
    public override string PluginName => "gardener";

    /// <inheritdoc />
    public override string DisplayName => "Gardener Service";

    /// <summary>
    /// Configures services for the Gardener plugin including background workers
    /// and DI listener registrations.
    /// </summary>
    /// <param name="services">The service collection to register services into.</param>
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        // Register ISeedEvolutionListener as Singleton for seed growth/phase notifications.
        // Must be a separate class (not GardenerService) because ISeedEvolutionListener
        // is consumed by BackgroundService workers (Singleton context), while GardenerService
        // is Scoped. Follows SeedCollectionUnlockListener pattern.
        // per IMPLEMENTATION TENETS - DI Listener pattern
        services.AddSingleton<ISeedEvolutionListener, GardenerSeedEvolutionListener>();

        // Register background workers as hosted services
        services.AddHostedService<GardenerVoidOrchestratorWorker>();
        services.AddHostedService<GardenerScenarioLifecycleWorker>();
    }
}
