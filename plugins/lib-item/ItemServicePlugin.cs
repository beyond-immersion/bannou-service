using BeyondImmersion.BannouService.Plugins;

namespace BeyondImmersion.BannouService.Item;

/// <summary>
/// Plugin wrapper for Item service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class ItemServicePlugin : StandardServicePlugin<IItemService>
{
    public override string PluginName => "item";
    public override string DisplayName => "Item Service";
}
