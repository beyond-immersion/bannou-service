using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.RealmHistory;

/// <summary>
/// Plugin wrapper for RealmHistory service enabling plugin-based discovery and lifecycle management.
/// Bridges existing IBannouService implementation with the new Plugin system.
/// </summary>
public class RealmHistoryServicePlugin : BaseBannouPlugin
{
    public override string PluginName => "realm-history";
    public override string DisplayName => "RealmHistory Service";

    private IRealmHistoryService? _service;
    private IServiceProvider? _serviceProvider;

    /// <summary>
    /// Configure services for dependency injection - mimics existing [BannouService] registration.
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        Logger?.LogDebug("Configuring service dependencies");

        // Service registration is now handled centrally by PluginLoader based on [BannouService] attributes
        // No need to register IRealmHistoryService and RealmHistoryService here

        // Configuration registration is now handled centrally by PluginLoader based on [ServiceConfiguration] attributes
        // No need to register RealmHistoryServiceConfiguration here

        // Add any service-specific dependencies
        // The generated clients should already be registered by AddAllBannouServiceClients()

        Logger?.LogDebug("Service dependencies configured");
    }

    /// <summary>
    /// Configure application pipeline - handles controller registration.
    /// </summary>
    public override void ConfigureApplication(WebApplication app)
    {
        Logger?.LogInformation("Configuring RealmHistory service application pipeline");

        // The generated RealmHistoryController should already be discovered via standard ASP.NET Core controller discovery

        // Store service provider for lifecycle management
        _serviceProvider = app.Services;

        Logger?.LogInformation("RealmHistory service application pipeline configured");
    }

    /// <summary>
    /// Start the service - calls existing IBannouService lifecycle if present.
    /// </summary>
    protected override async Task<bool> OnStartAsync()
    {
        Logger?.LogInformation("Starting RealmHistory service");

        try
        {
            // Get service instance from DI container with proper scope handling
            // Note: CreateScope() is required for Scoped services to avoid "Cannot resolve scoped service from root provider" error
            using var scope = _serviceProvider?.CreateScope();
            _service = scope?.ServiceProvider.GetService<IRealmHistoryService>();

            if (_service == null)
            {
                Logger?.LogError("Failed to resolve IRealmHistoryService from DI container");
                return false;
            }

            // Call existing IBannouService.OnStartAsync if the service implements it
            if (_service is IBannouService bannouService)
            {
                Logger?.LogDebug("Calling IBannouService.OnStartAsync for RealmHistory service");
                await bannouService.OnStartAsync(CancellationToken.None);
            }

            Logger?.LogInformation("RealmHistory service started successfully");
            return true;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to start RealmHistory service");
            return false;
        }
    }

    /// <summary>
    /// Running phase - calls existing IBannouService lifecycle if present.
    /// Also registers cleanup callbacks with lib-resource (must happen after all plugins are started).
    /// </summary>
    protected override async Task OnRunningAsync()
    {
        if (_service == null) return;

        Logger?.LogDebug("RealmHistory service running");

        var serviceProvider = _serviceProvider ?? throw new InvalidOperationException("ServiceProvider not available during OnRunningAsync");

        try
        {
            // Call existing IBannouService.OnRunningAsync if the service implements it
            if (_service is IBannouService bannouService)
            {
                Logger?.LogDebug("Calling IBannouService.OnRunningAsync for RealmHistory service");
                await bannouService.OnRunningAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Exception during RealmHistory service running phase");
        }

        // Register cleanup callbacks with lib-resource for realm reference tracking.
        // This MUST happen in OnRunningAsync (not OnStartAsync) because OnRunningAsync runs
        // AFTER all plugins have completed StartAsync, ensuring lib-resource is available.
        // Registering during OnStartAsync would be unsafe because plugin load order isn't guaranteed.
        try
        {
            using var scope = serviceProvider.CreateScope();
            var resourceClient = scope.ServiceProvider.GetService<IResourceClient>();
            if (resourceClient != null)
            {
                var success = await RealmHistoryService.RegisterResourceCleanupCallbacksAsync(resourceClient, CancellationToken.None);
                if (success)
                {
                    Logger?.LogInformation("Registered realm cleanup callbacks with lib-resource");
                }
                else
                {
                    Logger?.LogWarning("Failed to register some cleanup callbacks with lib-resource");
                }

                // Register compression callback (generated from x-compression-callback)
                if (await RealmHistoryCompressionCallbacks.RegisterAsync(resourceClient, CancellationToken.None))
                {
                    Logger?.LogInformation("Registered realm-history compression callback with lib-resource");
                }
                else
                {
                    Logger?.LogWarning("Failed to register realm-history compression callback with lib-resource");
                }
            }
            else
            {
                Logger?.LogDebug("IResourceClient not available - cleanup callbacks not registered (lib-resource may not be enabled)");
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Failed to register cleanup callbacks with lib-resource");
        }
    }

    /// <summary>
    /// Shutdown the service - calls existing IBannouService lifecycle if present.
    /// </summary>
    protected override async Task OnShutdownAsync()
    {
        if (_service == null) return;

        Logger?.LogInformation("Shutting down RealmHistory service");

        try
        {
            // Call existing IBannouService.OnShutdownAsync if the service implements it
            if (_service is IBannouService bannouService)
            {
                Logger?.LogDebug("Calling IBannouService.OnShutdownAsync for RealmHistory service");
                await bannouService.OnShutdownAsync();
            }

            Logger?.LogInformation("RealmHistory service shutdown complete");
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Exception during RealmHistory service shutdown");
        }
    }
}
