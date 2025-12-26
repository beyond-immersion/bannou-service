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
    /// CRITICAL: Orchestrator can ONLY run on the "bannou" app-id.
    /// </summary>
    protected override bool OnValidatePlugin()
    {
        var enabled = Environment.GetEnvironmentVariable("ORCHESTRATOR_SERVICE_ENABLED")?.ToLower();
        Logger?.LogDebug("Orchestrator service enabled check: {EnabledValue}", enabled);

        if (enabled != "true")
        {
            return false;
        }

        // CRITICAL: Enforce that orchestrator ONLY runs on "bannou" app-id
        var currentAppId = Environment.GetEnvironmentVariable("BANNOU_APP_ID") ?? AppConstants.DEFAULT_APP_NAME;
        if (currentAppId != AppConstants.DEFAULT_APP_NAME)
        {
            Logger?.LogCritical(
                "FATAL: Orchestrator service can ONLY run on '{DefaultAppId}' app-id, not '{CurrentAppId}'. " +
                "The orchestrator is the control plane that manages all other services.",
                AppConstants.DEFAULT_APP_NAME, currentAppId);

            throw new InvalidOperationException(
                $"Orchestrator service can ONLY run on '{AppConstants.DEFAULT_APP_NAME}' app-id, not '{currentAppId}'.");
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
