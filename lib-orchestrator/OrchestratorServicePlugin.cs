using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Services;
using LibOrchestrator;
using LibOrchestrator.Backends;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Orchestrator;

/// <summary>
/// Plugin wrapper for Orchestrator service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class OrchestratorServicePlugin : StandardServicePlugin<IOrchestratorService>
{
    public override string PluginName => "orchestrator";
    public override string DisplayName => "Orchestrator Service";

    /// <summary>
    /// Validate that this plugin should be loaded based on environment configuration.
    /// CRITICAL: Orchestrator can ONLY run on the default "bannou" app-id.
    /// Service enabling is handled automatically by the plugin system - this only validates
    /// that the orchestrator isn't being loaded on a non-control-plane instance.
    /// </summary>
    protected override bool OnValidatePlugin()
    {
        // Get current app-id from configuration
        var currentAppId = Program.Configuration.EffectiveAppId;

        // CRITICAL: Orchestrator can ONLY run on the default "bannou" app-id
        // It's the control plane that manages all other services
        if (!string.Equals(currentAppId, AppConstants.DEFAULT_APP_NAME, StringComparison.OrdinalIgnoreCase))
        {
            Logger?.LogWarning(
                "Orchestrator service skipped: can ONLY run on '{DefaultAppId}' app-id, not '{CurrentAppId}'. " +
                "The orchestrator is the control plane that manages all other services.",
                AppConstants.DEFAULT_APP_NAME, currentAppId);
            return false;
        }

        Logger?.LogInformation("Orchestrator service validated: running on '{AppId}' (control plane)", currentAppId);
        return true;
    }

    public override void ConfigureServices(IServiceCollection services)
    {
        if (!OnValidatePlugin())
        {
            Logger?.LogInformation("Orchestrator service disabled, skipping service registration");
            return;
        }

        Logger?.LogInformation("Configuring Orchestrator service dependencies");

        // Register HttpClientFactory for BackendDetector (Portainer/Kubernetes API calls)
        services.AddHttpClient();

        // Register orchestrator helper classes as Singletons to maintain persistent connections
        // Note: IOrchestratorStateManager uses IStateStoreFactory from lib-state (no direct Redis dependency)
        services.AddSingleton<IOrchestratorStateManager, OrchestratorStateManager>();
        services.AddSingleton<IOrchestratorEventManager, OrchestratorEventManager>();
        services.AddSingleton<IServiceHealthMonitor, ServiceHealthMonitor>();
        services.AddSingleton<ISmartRestartManager, SmartRestartManager>();
        services.AddSingleton<IBackendDetector, BackendDetector>();

        // Register the service implementation
        services.AddScoped<IOrchestratorService, OrchestratorService>();
        services.AddScoped<OrchestratorService>();

        Logger?.LogInformation("Orchestrator service dependencies configured");
    }

    protected override async Task<bool> OnStartAsync()
    {
        if (!OnValidatePlugin()) return true;

        Logger?.LogInformation("Starting Orchestrator service");

        // Initialize state manager (uses IStateStoreFactory from lib-state)
        var serviceProvider = ServiceProvider ?? throw new InvalidOperationException("ServiceProvider not available during OnStartAsync");
        var stateManager = serviceProvider.GetRequiredService<IOrchestratorStateManager>();

        Logger?.LogInformation("Initializing state stores for orchestrator via lib-state...");
        var stateInitialized = await stateManager.InitializeAsync();
        if (!stateInitialized)
        {
            Logger?.LogError("State store initialization failed - cannot start Orchestrator without state");
            return false;
        }

        // Initialize default routes for all loaded services
        // This ensures routing proxies (like OpenResty) have explicit routes from startup
        // rather than falling back to hardcoded defaults.
        await InitializeDefaultServiceRoutesAsync(stateManager);

        // Call base to resolve service and call IBannouService.OnStartAsync
        return await base.OnStartAsync();
    }

    /// <summary>
    /// Known routable services. Infrastructure services (state, messaging, mesh) are excluded
    /// as they must always be handled locally, never routed to external nodes.
    /// </summary>
    private static readonly string[] KnownRoutableServices =
    {
        "auth", "account", "connect", "website", "permission",
        "behavior", "character", "species", "realm", "location",
        "relationship", "relationship-type", "subscription",
        "game-session", "orchestrator", "documentation",
        "service", "voice", "asset", "actor"
    };

    /// <summary>
    /// Initialize default routes for all known routable services.
    /// Sets each service to route to the orchestrator's EffectiveAppId.
    /// This ensures routing proxies like OpenResty have explicit routes from startup.
    /// </summary>
    private async Task InitializeDefaultServiceRoutesAsync(IOrchestratorStateManager stateManager)
    {
        try
        {
            var defaultAppId = Program.Configuration.EffectiveAppId;
            var routesInitialized = 0;

            foreach (var serviceName in KnownRoutableServices)
            {
                // Write default routing for this service
                var defaultRouting = new ServiceRouting
                {
                    AppId = defaultAppId,
                    Host = defaultAppId,
                    Port = 80,
                    Status = "healthy",
                    LastUpdated = DateTimeOffset.UtcNow
                };

                await stateManager.WriteServiceRoutingAsync(serviceName, defaultRouting);
                routesInitialized++;

                Logger?.LogDebug("Initialized default route: {ServiceName} -> {AppId}", serviceName, defaultAppId);
            }

            Logger?.LogInformation(
                "Initialized {Count} default service routes to '{AppId}' on orchestrator startup",
                routesInitialized, defaultAppId);
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Failed to initialize default service routes - routing may fall back to proxy defaults");
        }
    }

    protected override async Task OnRunningAsync()
    {
        if (!OnValidatePlugin()) return;
        await base.OnRunningAsync();
    }

    protected override async Task OnShutdownAsync()
    {
        if (!OnValidatePlugin()) return;
        await base.OnShutdownAsync();
    }
}
