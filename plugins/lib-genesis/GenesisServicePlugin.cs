using BeyondImmersion.BannouService.Plugins;

namespace BeyondImmersion.BannouService.Genesis;

/// <summary>
/// Plugin wrapper for Genesis service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class GenesisServicePlugin : StandardServicePlugin<IGenesisService>
{
    public override string PluginName => "genesis";
    public override string DisplayName => "Genesis Service";
}
