using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Seed.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace BeyondImmersion.BannouService.Seed;

/// <summary>
/// Plugin wrapper for Seed service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class SeedServicePlugin : StandardServicePlugin<ISeedService>
{
    public override string PluginName => "seed";
    public override string DisplayName => "Seed Service";

    /// <summary>
    /// Configure services for dependency injection.
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        // Background worker for applying growth decay to seed domains
        services.AddHostedService<SeedDecayWorkerService>();
    }
}
