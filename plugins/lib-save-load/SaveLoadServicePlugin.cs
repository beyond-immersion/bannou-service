using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.SaveLoad.Processing;
using BeyondImmersion.BannouService.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.SaveLoad;

/// <summary>
/// Plugin wrapper for SaveLoad service enabling plugin-based discovery and lifecycle management.
/// Bridges existing IBannouService implementation with the new Plugin system.
/// </summary>
public class SaveLoadServicePlugin : BaseBannouPlugin
{
    public override string PluginName => "save-load";
    public override string DisplayName => "SaveLoad Service";

    private ISaveLoadService? _service;
    private IServiceProvider? _serviceProvider;

    /// <summary>
    /// Configure services for dependency injection - mimics existing [BannouService] registration.
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        Logger?.LogDebug("Configuring service dependencies");

        // Service registration is now handled centrally by PluginLoader based on [BannouService] attributes
        // No need to register ISaveLoadService and SaveLoadService here

        // Configuration registration is now handled centrally by PluginLoader based on [ServiceConfiguration] attributes
        // No need to register SaveLoadServiceConfiguration here

        // Register HttpClient factory for downloading save data from pre-signed URLs
        services.AddHttpClient();

        // Register background workers
        services.AddHostedService<SaveUploadWorker>();
        services.AddHostedService<CleanupService>();

        // The generated clients should already be registered by AddAllBannouServiceClients()

        Logger?.LogDebug("Service dependencies configured");
    }

    /// <summary>
    /// Configure application pipeline - handles controller registration.
    /// </summary>
    public override void ConfigureApplication(WebApplication app)
    {
        Logger?.LogInformation("Configuring SaveLoad service application pipeline");

        // The generated SaveLoadController should already be discovered via standard ASP.NET Core controller discovery

        // Store service provider for lifecycle management
        _serviceProvider = app.Services;

        Logger?.LogInformation("SaveLoad service application pipeline configured");
    }

    /// <summary>
    /// Start the service - calls existing IBannouService lifecycle if present.
    /// </summary>
    protected override async Task<bool> OnStartAsync()
    {
        Logger?.LogInformation("Starting SaveLoad service");

        try
        {
            // Get service instance from DI container with proper scope handling
            // Note: CreateScope() is required for Scoped services to avoid "Cannot resolve scoped service from root provider" error
            using var scope = _serviceProvider?.CreateScope();
            _service = scope?.ServiceProvider.GetService<ISaveLoadService>();

            if (_service == null)
            {
                Logger?.LogError("Failed to resolve ISaveLoadService from DI container");
                return false;
            }

            // Call existing IBannouService.OnStartAsync if the service implements it
            if (_service is IBannouService bannouService)
            {
                Logger?.LogDebug("Calling IBannouService.OnStartAsync for SaveLoad service");
                await bannouService.OnStartAsync(CancellationToken.None);
            }

            Logger?.LogInformation("SaveLoad service started successfully");
            return true;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to start SaveLoad service");
            return false;
        }
    }

    /// <summary>
    /// Running phase - calls existing IBannouService lifecycle if present.
    /// </summary>
    protected override async Task OnRunningAsync()
    {
        if (_service == null) return;

        Logger?.LogDebug("SaveLoad service running");

        try
        {
            // Call existing IBannouService.OnRunningAsync if the service implements it
            if (_service is IBannouService bannouService)
            {
                Logger?.LogDebug("Calling IBannouService.OnRunningAsync for SaveLoad service");
                await bannouService.OnRunningAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Exception during SaveLoad service running phase");
        }
    }

    /// <summary>
    /// Shutdown the service - calls existing IBannouService lifecycle if present.
    /// </summary>
    protected override async Task OnShutdownAsync()
    {
        if (_service == null) return;

        Logger?.LogInformation("Shutting down SaveLoad service");

        try
        {
            // Call existing IBannouService.OnShutdownAsync if the service implements it
            if (_service is IBannouService bannouService)
            {
                Logger?.LogDebug("Calling IBannouService.OnShutdownAsync for SaveLoad service");
                await bannouService.OnShutdownAsync();
            }

            Logger?.LogInformation("SaveLoad service shutdown complete");
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Exception during SaveLoad service shutdown");
        }
    }
}
