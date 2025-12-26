using BeyondImmersion.BannouService.Plugins;

namespace BeyondImmersion.BannouService.Permissions;

/// <summary>
/// Plugin wrapper for Permissions service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class PermissionsServicePlugin : StandardServicePlugin<IPermissionsService>
{
    public override string PluginName => "permissions";
    public override string DisplayName => "Permissions Service";

    // Note: IDistributedLockProvider is registered by lib-state's StateServicePlugin
}
