using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BeyondImmersion.BannouService.Permission;

/// <summary>
/// Plugin wrapper for Permission service enabling plugin-based discovery and lifecycle management.
/// Registers IPermissionRegistry in DI so other services can push their permission matrices
/// directly during startup instead of publishing events.
/// </summary>
public class PermissionServicePlugin : StandardServicePlugin<IPermissionService>
{
    public override string PluginName => "permission";
    public override string DisplayName => "Permission Service";

    /// <summary>
    /// Registers Permission-specific DI services.
    /// IPermissionRegistry is backed by the PermissionService singleton,
    /// enabling push-based permission registration from all services.
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        // Register IPermissionRegistry backed by the PermissionService singleton.
        // Services resolve this during startup to push their permission matrices.
        services.AddSingleton<IPermissionRegistry>(sp =>
            (IPermissionRegistry)sp.GetRequiredService<IPermissionService>());
    }
}
