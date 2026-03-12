using BeyondImmersion.BannouService.Plugins;

namespace BeyondImmersion.BannouService.Broadcast;

/// <summary>
/// Plugin wrapper for Broadcast service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class BroadcastServicePlugin : StandardServicePlugin<IBroadcastService>
{
    public override string PluginName => "broadcast";
    public override string DisplayName => "Broadcast Service";
}
