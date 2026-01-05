using BeyondImmersion.BannouService.Plugins;

namespace BeyondImmersion.BannouService.Service;

/// <summary>
/// Plugin wrapper for Service service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class ServiceServicePlugin : StandardServicePlugin<IServiceService>
{
    public override string PluginName => "service";
    public override string DisplayName => "Service Service";
}
