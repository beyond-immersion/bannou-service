using BeyondImmersion.BannouService.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace BeyondImmersion.BannouService.Matchmaking;

/// <summary>
/// Plugin wrapper for Matchmaking service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class MatchmakingServicePlugin : StandardServicePlugin<IMatchmakingService>
{
    public override string PluginName => "matchmaking";
    public override string DisplayName => "Matchmaking Service";

    /// <summary>
    /// Configure services for dependency injection.
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        // Register background service for interval-based match processing
        services.AddHostedService<MatchmakingBackgroundService>();
    }
}
