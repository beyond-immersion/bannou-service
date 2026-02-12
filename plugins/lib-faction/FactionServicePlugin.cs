using BeyondImmersion.BannouService.Plugins;

namespace BeyondImmersion.BannouService.Faction;

/// <summary>
/// Plugin wrapper for Faction service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class FactionServicePlugin : StandardServicePlugin<IFactionService>
{
    public override string PluginName => "faction";
    public override string DisplayName => "Faction Service";
}
