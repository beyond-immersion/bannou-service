using BeyondImmersion.BannouService.Plugins;

namespace BeyondImmersion.BannouService.Inventory;

/// <summary>
/// Plugin wrapper for Inventory service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class InventoryServicePlugin : StandardServicePlugin<IInventoryService>
{
    public override string PluginName => "inventory";
    public override string DisplayName => "Inventory Service";
}
