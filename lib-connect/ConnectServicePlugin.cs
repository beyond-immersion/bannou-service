using BeyondImmersion.BannouService.Connect.ClientEvents;
using BeyondImmersion.BannouService.Plugins;
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

    [Obsolete]
    public override void ConfigureServices(IServiceCollection services)
    {
        Logger?.LogDebug("Configuring service dependencies");

        // Register named HttpClient for mesh proxying (Tenet 4: use IHttpClientFactory)
        services.AddHttpClient(ConnectService.HttpClientName, client =>
        {
            // Set timeout to 120 seconds to ensure Connect service doesn't hang indefinitely
            // This should be longer than client timeouts (60s) but shorter than infinite
            client.Timeout = TimeSpan.FromSeconds(120);
        });
        Logger?.LogDebug("Registered named HttpClient '{ClientName}' with 120s timeout", ConnectService.HttpClientName);

        // Register BannouSessionManager for distributed session state management
        // Uses lib-state (connect-statestore) for state storage
        services.AddSingleton<ISessionManager, BannouSessionManager>();
        Logger?.LogDebug("Registered BannouSessionManager for session state management");

        // Register client event queue manager for disconnection handling
        // Uses lib-state to queue events during client reconnection window
        // MUST be Singleton because it's used from RabbitMQ consumer callbacks (outside request scope)
        services.AddSingleton<ClientEventQueueManager>();
        Logger?.LogDebug("Registered ClientEventQueueManager for event queuing");

        Logger?.LogDebug("Service dependencies configured");
    }
}
