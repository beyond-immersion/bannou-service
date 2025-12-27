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

    public override void ConfigureServices(IServiceCollection services)
    {
        // Add IHttpContextAccessor so GameSessionService can read the X-Bannou-Session-Id header
        // This header is set by Connect service when routing WebSocket requests
        services.AddHttpContextAccessor();
    }
}
