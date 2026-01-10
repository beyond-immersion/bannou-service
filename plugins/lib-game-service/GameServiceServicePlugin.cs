using BeyondImmersion.BannouService.Plugins;

namespace BeyondImmersion.BannouService.GameService;

/// <summary>
/// Plugin wrapper for Game Service service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class GameServiceServicePlugin : StandardServicePlugin<IGameServiceService>
{
    public override string PluginName => "game-service";
    public override string DisplayName => "Game Service";
}
