using BeyondImmersion.BannouService.Plugins;

namespace BeyondImmersion.BannouService.Divine;

/// <summary>
/// Plugin wrapper for Divine service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class DivineServicePlugin : StandardServicePlugin<IDivineService>
{
    public override string PluginName => "divine";
    public override string DisplayName => "Divine Service";
}
