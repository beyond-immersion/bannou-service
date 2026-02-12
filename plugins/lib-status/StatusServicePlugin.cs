using BeyondImmersion.BannouService.Plugins;

namespace BeyondImmersion.BannouService.Status;

/// <summary>
/// Plugin wrapper for Status service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class StatusServicePlugin : StandardServicePlugin<IStatusService>
{
    public override string PluginName => "status";
    public override string DisplayName => "Status Service";
}
