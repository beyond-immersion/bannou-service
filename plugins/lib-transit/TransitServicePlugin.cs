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
        services.AddSingleton<ITransitConnectionGraphCache, TransitConnectionGraphCache>();
        services.AddSingleton<ITransitRouteCalculator, TransitRouteCalculator>();

        // Background workers
        services.AddHostedService<SeasonalConnectionWorker>();
        services.AddHostedService<JourneyArchivalWorker>();

        // Variable provider for ${transit.*} ABML expressions
        services.AddSingleton<IVariableProviderFactory, TransitVariableProviderFactory>();
    }
}
