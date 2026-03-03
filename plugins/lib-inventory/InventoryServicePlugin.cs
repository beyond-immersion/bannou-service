using BeyondImmersion.BannouService.Inventory.Caching;
using BeyondImmersion.BannouService.Inventory.Providers;
using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace BeyondImmersion.BannouService.Inventory;

/// <summary>
/// Plugin wrapper for Inventory service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class InventoryServicePlugin : StandardServicePlugin<IInventoryService>
{
    public override string PluginName => "inventory";
    public override string DisplayName => "Inventory Service";

    /// <summary>
    /// Configure services for dependency injection.
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        // Register inventory data cache and variable provider factory for ABML expressions
        services.AddSingleton<IInventoryDataCache, InventoryDataCache>();
        services.AddSingleton<IVariableProviderFactory, InventoryProviderFactory>();
    }
}
