using BeyondImmersion.BannouService.Plugins;

namespace BeyondImmersion.BannouService.Resource;

/// <summary>
/// Plugin wrapper for Resource service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class ResourceServicePlugin : StandardServicePlugin<IResourceService>
{
    public override string PluginName => "resource";
    public override string DisplayName => "Resource Service";
}
