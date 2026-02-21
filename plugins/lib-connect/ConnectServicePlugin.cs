using BeyondImmersion.BannouService.Connect.Helpers;
using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Connect;

/// <summary>
/// Plugin wrapper for Connect service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class ConnectServicePlugin : StandardServicePlugin<IConnectService>
{
    public override string PluginName => "connect";
    public override string DisplayName => "Connect Service";

    public override void ConfigureServices(IServiceCollection services)
    {
        Logger?.LogDebug("Configuring service dependencies");

        // Register BannouSessionManager for distributed session state management
        // Uses lib-state (connect-statestore) for state storage
        services.AddSingleton<ISessionManager, BannouSessionManager>();
        Logger?.LogDebug("Registered BannouSessionManager for session state management");

        // Register entity session registry for entity-to-session mapping
        // Must be Singleton because ConnectService is Singleton and cannot consume scoped services
        services.AddSingleton<IEntitySessionRegistry, EntitySessionRegistry>();
        Logger?.LogDebug("Registered EntitySessionRegistry for entity-to-session mapping");

        // Register helper services for improved testability
        // Must be Singleton because ConnectService is Singleton and cannot consume scoped services
        services.AddSingleton<ICapabilityManifestBuilder, CapabilityManifestBuilder>();
        Logger?.LogDebug("Registered CapabilityManifestBuilder");

        Logger?.LogDebug("Service dependencies configured");
    }
}
