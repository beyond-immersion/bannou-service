using BeyondImmersion.BannouService.Plugins;

namespace BeyondImmersion.BannouService.Permission;

/// <summary>
/// Plugin wrapper for Permission service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class PermissionServicePlugin : StandardServicePlugin<IPermissionService>
{
    public override string PluginName => "permission";
    public override string DisplayName => "Permission Service";
}
