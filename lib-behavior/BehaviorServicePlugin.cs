using BeyondImmersion.BannouService.Plugins;

namespace BeyondImmersion.BannouService.Behavior;

/// <summary>
/// Plugin wrapper for Behavior service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class BehaviorServicePlugin : StandardServicePlugin<IBehaviorService>
{
    public override string PluginName => "behavior";
    public override string DisplayName => "Behavior Service";
}
