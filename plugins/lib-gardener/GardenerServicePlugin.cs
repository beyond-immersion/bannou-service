using BeyondImmersion.BannouService.Plugins;

namespace BeyondImmersion.BannouService.Gardener;

/// <summary>
/// Plugin wrapper for Gardener service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class GardenerServicePlugin : StandardServicePlugin<IGardenerService>
{
    public override string PluginName => "gardener";
    public override string DisplayName => "Gardener Service";
}
