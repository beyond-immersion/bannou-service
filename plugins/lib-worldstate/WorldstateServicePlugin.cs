using BeyondImmersion.BannouService.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace BeyondImmersion.BannouService.Worldstate;

/// <summary>
/// Plugin wrapper for Worldstate service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class WorldstateServicePlugin : StandardServicePlugin<IWorldstateService>
{
    public override string PluginName => "worldstate";
    public override string DisplayName => "Worldstate Service";

    /// <summary>
    /// Registers worldstate helper services for dependency injection.
    /// </summary>
    /// <param name="services">The service collection to register services with.</param>
    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IWorldstateTimeCalculator, WorldstateTimeCalculator>();
        services.AddHostedService<WorldstateClockWorkerService>();
    }
}
