using BeyondImmersion.BannouService.Plugins;

namespace BeyondImmersion.BannouService.Environment;

/// <summary>
/// Plugin wrapper for Environment service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class EnvironmentServicePlugin : StandardServicePlugin<IEnvironmentService>
{
    public override string PluginName => "environment";
    public override string DisplayName => "Environment Service";
}
