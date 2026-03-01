using BeyondImmersion.BannouService.Plugins;

namespace BeyondImmersion.BannouService.Transit;

/// <summary>
/// Plugin wrapper for Transit service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class TransitServicePlugin : StandardServicePlugin<ITransitService>
{
    public override string PluginName => "transit";
    public override string DisplayName => "Transit Service";
}
