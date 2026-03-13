using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BeyondImmersion.BannouService.Permission;

/// <summary>
/// Plugin wrapper for Permission service enabling plugin-based discovery and lifecycle management.
/// Registers IPermissionRegistry and ISessionActivityListener in DI.
/// </summary>
public class PermissionServicePlugin : StandardServicePlugin<IPermissionService>
{
    public override string PluginName => "permission";
    public override string DisplayName => "Permission Service";

    /// <summary>
    /// Registers Permission-specific DI services.
    /// IPermissionRegistry is backed by the PermissionService singleton,
    /// enabling push-based permission registration from all services.
    /// ISessionActivityListener is registered for heartbeat-driven TTL refresh
    /// and session lifecycle handling via Connect's DI listener dispatch.
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        // Register IPermissionRegistry backed by the PermissionService singleton.
        // Services resolve this during startup to push their permission matrices.
        services.AddSingleton<IPermissionRegistry>(sp =>
            (IPermissionRegistry)sp.GetRequiredService<IPermissionService>());

        // Register RegistrationEventBatcher as Singleton + IHostedService.
        // Shared instance: PermissionService injects it to call Add(),
        // the host starts it as a BackgroundService for periodic flush.
        services.AddSingleton<RegistrationEventBatcher>();
        services.AddSingleton<IHostedService>(sp =>
            sp.GetRequiredService<RegistrationEventBatcher>());

    }
}
