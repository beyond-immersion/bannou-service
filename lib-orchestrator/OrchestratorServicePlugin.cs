using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Services;
using LibOrchestrator;
using LibOrchestrator.Backends;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Orchestrator;

/// <summary>
/// Plugin wrapper for Orchestrator service enabling plugin-based discovery and lifecycle management.
/// Bridges existing IDaprService implementation with the new Plugin system.
/// </summary>
public class OrchestratorServicePlugin : BaseBannouPlugin
{
    public override string PluginName => "orchestrator";
    public override string DisplayName => "Orchestrator Service";

    private IOrchestratorService? _service;
    private IServiceProvider? _serviceProvider;

    /// <summary>
    /// Validate that this plugin should be loaded based on environment configuration.
    /// </summary>
    protected override bool OnValidatePlugin()
    {
        var enabled = Environment.GetEnvironmentVariable("ORCHESTRATOR_SERVICE_ENABLED")?.ToLower();
        Logger?.LogDebug("Orchestrator service enabled check: {EnabledValue}", enabled);
        return enabled == "true";
    }

    /// <summary>
    /// Configure services for dependency injection - mimics existing [DaprService] registration.
    /// </summary>
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

        // NOTE: OrchestratorServiceConfiguration registration is handled by the centralized PluginLoader
        // based on the [ServiceConfiguration] attribute. The managers read env vars directly
        // to avoid DI lifetime conflicts.

        // Register orchestrator helper classes as Singletons to maintain persistent connections
        // These manage Redis and RabbitMQ connections that should persist across requests
        // Note: These read connection strings directly from environment variables
        services.AddSingleton<IOrchestratorRedisManager, OrchestratorRedisManager>();
        services.AddSingleton<IOrchestratorEventManager, OrchestratorEventManager>();
        services.AddSingleton<IServiceHealthMonitor, ServiceHealthMonitor>();
        services.AddSingleton<ISmartRestartManager, SmartRestartManager>();
        services.AddSingleton<IBackendDetector, BackendDetector>();

        // Register the service implementation (existing pattern from [DaprService] attribute)
        services.AddScoped<IOrchestratorService, OrchestratorService>();
        services.AddScoped<OrchestratorService>();

        Logger?.LogInformation("Orchestrator service dependencies configured");
    }

    /// <summary>
    /// Configure application pipeline - handles controller registration.
    /// </summary>
    public override void ConfigureApplication(WebApplication app)
    {
        if (!OnValidatePlugin())
        {
            Logger?.LogInformation("Orchestrator service disabled, skipping application configuration");
            return;
        }

        Logger?.LogInformation("Configuring Orchestrator service application pipeline");

        // The generated OrchestratorController should already be discovered via standard ASP.NET Core controller discovery
        // since we're not excluding the assembly like we did with IDaprController approach

        // Store service provider for lifecycle management
        _serviceProvider = app.Services;

        Logger?.LogInformation("Orchestrator service application pipeline configured");
    }

    /// <summary>
    /// Start the service - initializes infrastructure connections and calls existing IDaprService lifecycle if present.
    /// </summary>
    protected override async Task<bool> OnStartAsync()
    {
        if (!OnValidatePlugin()) return true;

        Logger?.LogInformation("Starting Orchestrator service");

        try
        {
            // Initialize Redis and RabbitMQ connections (Singleton services, no scope needed)
            var redisManager = _serviceProvider?.GetService<IOrchestratorRedisManager>();
            var eventManager = _serviceProvider?.GetService<IOrchestratorEventManager>();

            if (redisManager != null)
            {
                Logger?.LogInformation("Initializing Redis connection for orchestrator...");
                var redisInitialized = await redisManager.InitializeAsync();
                if (!redisInitialized)
                {
                    Logger?.LogWarning("Redis connection initialization failed - health checks will report unhealthy");
                }
            }

            if (eventManager != null)
            {
                Logger?.LogInformation("Initializing RabbitMQ connection for orchestrator...");
                var rabbitInitialized = await eventManager.InitializeAsync();
                if (!rabbitInitialized)
                {
                    Logger?.LogWarning("RabbitMQ connection initialization failed - health checks will report unhealthy");
                }
            }

            // Get service instance from DI container with proper scope handling
            // Note: CreateScope() is required for Scoped services to avoid "Cannot resolve scoped service from root provider" error
            using var scope = _serviceProvider?.CreateScope();
            _service = scope?.ServiceProvider.GetService<IOrchestratorService>();

            if (_service == null)
            {
                Logger?.LogError("Failed to resolve IOrchestratorService from DI container");
                return false;
            }

            // Call existing IDaprService.OnStartAsync if the service implements it
            if (_service is IDaprService daprService)
            {
                Logger?.LogDebug("Calling IDaprService.OnStartAsync for Orchestrator service");
                await daprService.OnStartAsync(CancellationToken.None);
            }

            Logger?.LogInformation("Orchestrator service started successfully");
            return true;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to start Orchestrator service");
            return false;
        }
    }

    /// <summary>
    /// Running phase - calls existing IDaprService lifecycle if present.
    /// </summary>
    protected override async Task OnRunningAsync()
    {
        if (!OnValidatePlugin() || _service == null) return;

        Logger?.LogDebug("Orchestrator service running");

        try
        {
            // Call existing IDaprService.OnRunningAsync if the service implements it
            if (_service is IDaprService daprService)
            {
                Logger?.LogDebug("Calling IDaprService.OnRunningAsync for Orchestrator service");
                await daprService.OnRunningAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Exception during Orchestrator service running phase");
        }
    }

    /// <summary>
    /// Shutdown the service - calls existing IDaprService lifecycle if present.
    /// </summary>
    protected override async Task OnShutdownAsync()
    {
        if (!OnValidatePlugin() || _service == null) return;

        Logger?.LogInformation("Shutting down Orchestrator service");

        try
        {
            // Call existing IDaprService.OnShutdownAsync if the service implements it
            if (_service is IDaprService daprService)
            {
                Logger?.LogDebug("Calling IDaprService.OnShutdownAsync for Orchestrator service");
                await daprService.OnShutdownAsync();
            }

            Logger?.LogInformation("Orchestrator service shutdown complete");
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Exception during Orchestrator service shutdown");
        }
    }
}
