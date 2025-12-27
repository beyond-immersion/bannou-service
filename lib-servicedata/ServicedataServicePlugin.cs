using BeyondImmersion.BannouService.Plugins;

namespace BeyondImmersion.BannouService.Servicedata;

/// <summary>
/// Plugin wrapper for Servicedata service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class ServicedataServicePlugin : StandardServicePlugin<IServicedataService>
{
    public override string PluginName => "servicedata";
    public override string DisplayName => "Servicedata Service";
}
