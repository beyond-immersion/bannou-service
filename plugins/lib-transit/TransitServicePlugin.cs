using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace BeyondImmersion.BannouService.Transit;

/// <summary>
/// Plugin wrapper for Transit service enabling plugin-based discovery and lifecycle management.
/// Registers helper services, background workers, and variable provider factory.
/// </summary>
public class TransitServicePlugin : StandardServicePlugin<ITransitService>
{
    /// <inheritdoc />
    public override string PluginName => "transit";

    /// <inheritdoc />
    public override string DisplayName => "Transit Service";

    /// <summary>
    /// Registers transit helper services, background workers, and DI providers.
    /// </summary>
    /// <param name="services">The service collection to register services with.</param>
    public override void ConfigureServices(IServiceCollection services)
    {
        // Helper services (singleton for shared state across scoped service instances)
        // TODO: Uncomment when TransitConnectionGraphCache is created (Phase 3)
        // services.AddSingleton<ITransitConnectionGraphCache, TransitConnectionGraphCache>();
        // TODO: Uncomment when TransitRouteCalculator is created (Phase 4)
        // services.AddSingleton<ITransitRouteCalculator, TransitRouteCalculator>();

        // Background workers
        // TODO: Uncomment when SeasonalConnectionWorker is created (Phase 8)
        // services.AddHostedService<SeasonalConnectionWorker>();
        // TODO: Uncomment when JourneyArchivalWorker is created (Phase 8)
        // services.AddHostedService<JourneyArchivalWorker>();

        // Variable provider for ${transit.*} ABML expressions
        // TODO: Uncomment when TransitVariableProviderFactory is created (Phase 9)
        // services.AddSingleton<IVariableProviderFactory, TransitVariableProviderFactory>();
    }
}
