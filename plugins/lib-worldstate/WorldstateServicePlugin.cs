using BeyondImmersion.BannouService.Plugins;

namespace BeyondImmersion.BannouService.Worldstate;

/// <summary>
/// Plugin wrapper for Worldstate service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class WorldstateServicePlugin : StandardServicePlugin<IWorldstateService>
{
    public override string PluginName => "worldstate";
    public override string DisplayName => "Worldstate Service";
}
