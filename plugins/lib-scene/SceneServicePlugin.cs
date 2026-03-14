using BeyondImmersion.BannouService.Plugins;

namespace BeyondImmersion.BannouService.Scene;

/// <summary>
/// Plugin wrapper for Scene service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class SceneServicePlugin : StandardServicePlugin<ISceneService>
{
    public override string PluginName => "scene";
    public override string DisplayName => "Scene Service";
}
