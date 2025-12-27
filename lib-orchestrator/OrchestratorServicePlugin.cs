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
        // Get current app-id, defaulting to the constant if not set
        var currentAppId = Environment.GetEnvironmentVariable(AppConstants.ENV_BANNOU_APP_ID);
        if (string.IsNullOrEmpty(currentAppId))
        {
            currentAppId = AppConstants.DEFAULT_APP_NAME;
        }

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
        var stateManager = ServiceProvider?.GetService<IOrchestratorStateManager>();

        if (stateManager != null)
        {
            Logger?.LogInformation("Initializing state stores for orchestrator via lib-state...");
            var stateInitialized = await stateManager.InitializeAsync();
            if (!stateInitialized)
            {
                Logger?.LogWarning("State store initialization failed - health checks will report unhealthy");
            }
        }

        // Call base to resolve service and call IBannouService.OnStartAsync
        return await base.OnStartAsync();
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
