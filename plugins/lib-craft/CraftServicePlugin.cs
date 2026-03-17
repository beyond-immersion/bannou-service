using BeyondImmersion.BannouService.Plugins;

namespace BeyondImmersion.BannouService.Craft;

/// <summary>
/// Plugin wrapper for Craft service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class CraftServicePlugin : StandardServicePlugin<ICraftService>
{
    public override string PluginName => "craft";
    public override string DisplayName => "Craft Service";
}
