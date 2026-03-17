using BeyondImmersion.BannouService.Plugins;

namespace BeyondImmersion.BannouService.Arbitration;

/// <summary>
/// Plugin wrapper for Arbitration service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class ArbitrationServicePlugin : StandardServicePlugin<IArbitrationService>
{
    public override string PluginName => "arbitration";
    public override string DisplayName => "Arbitration Service";
}
