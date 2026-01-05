using BeyondImmersion.BannouService.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace BeyondImmersion.BannouService.GameSession;

/// <summary>
/// Plugin wrapper for GameSession service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class GameSessionServicePlugin : StandardServicePlugin<IGameSessionService>
{
    public override string PluginName => "game-session";
    public override string DisplayName => "GameSession Service";

    /// <summary>
    /// Registers additional services required by the GameSession plugin.
    /// </summary>
    /// <param name="services">The service collection to register services with.</param>
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        // Register the startup service to initialize subscription caches on startup
        services.AddHostedService<GameSessionStartupService>();
    }
}
