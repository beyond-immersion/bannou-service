using BeyondImmersion.BannouService.Achievement.Providers;
using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Achievement;

/// <summary>
/// Plugin wrapper for Achievement service enabling plugin-based discovery and lifecycle management.
/// Bridges existing IBannouService implementation with the new Plugin system.
/// </summary>
public class AchievementServicePlugin : BaseBannouPlugin
{
    public override string PluginName => "achievement";
    public override string DisplayName => "Achievement Service";

    private IAchievementService? _service;
    private IServiceProvider? _serviceProvider;

    /// <summary>
    /// Configure services for dependency injection - mimics existing [BannouService] registration.
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        Logger?.LogDebug("Configuring service dependencies");

        // Service registration is now handled centrally by PluginLoader based on [BannouService] attributes
        // No need to register IAchievementService and AchievementService here

        // Configuration registration is now handled centrally by PluginLoader based on [ServiceConfiguration] attributes
        // No need to register AchievementServiceConfiguration here

        // Add any service-specific dependencies
        // The generated clients should already be registered by AddAllBannouServiceClients()

        // Prerequisite provider for Quest's dynamic prerequisite validation
        services.AddSingleton<IPrerequisiteProviderFactory, AchievementPrerequisiteProviderFactory>();

        // Background service for periodic rarity percentage recalculation
        services.AddHostedService<RarityCalculationService>();

        Logger?.LogDebug("Service dependencies configured");
    }

    /// <summary>
    /// Configure application pipeline - handles controller registration.
    /// </summary>
    public override void ConfigureApplication(WebApplication app)
    {
        Logger?.LogInformation("Configuring Achievement service application pipeline");

        // The generated AchievementController should already be discovered via standard ASP.NET Core controller discovery

        // Store service provider for lifecycle management
        _serviceProvider = app.Services;

        Logger?.LogInformation("Achievement service application pipeline configured");
    }

    /// <summary>
    /// Start the service - calls existing IBannouService lifecycle if present.
    /// </summary>
    protected override async Task<bool> OnStartAsync()
    {
        Logger?.LogInformation("Starting Achievement service");

        try
        {
            // Get service instance from DI container with proper scope handling
            // Note: CreateScope() is required for Scoped services to avoid "Cannot resolve scoped service from root provider" error
            using var scope = _serviceProvider?.CreateScope();
            _service = scope?.ServiceProvider.GetService<IAchievementService>();

            if (_service == null)
            {
                Logger?.LogError("Failed to resolve IAchievementService from DI container");
                return false;
            }

            // Call existing IBannouService.OnStartAsync if the service implements it
            if (_service is IBannouService bannouService)
            {
                Logger?.LogDebug("Calling IBannouService.OnStartAsync for Achievement service");
                await bannouService.OnStartAsync(CancellationToken.None);
            }

            Logger?.LogInformation("Achievement service started successfully");
            return true;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to start Achievement service");
            return false;
        }
    }

    /// <summary>
    /// Running phase - calls existing IBannouService lifecycle if present.
    /// </summary>
    protected override async Task OnRunningAsync()
    {
        if (_service == null) return;

        Logger?.LogDebug("Achievement service running");

        var serviceProvider = _serviceProvider ?? throw new InvalidOperationException("ServiceProvider not available during OnRunningAsync");

        try
        {
            // Call existing IBannouService.OnRunningAsync if the service implements it
            if (_service is IBannouService bannouService)
            {
                Logger?.LogDebug("Calling IBannouService.OnRunningAsync for Achievement service");
                await bannouService.OnRunningAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Exception during Achievement service running phase");
        }

        // Register cleanup callbacks with lib-resource for character reference tracking.
        // IResourceClient is L1 infrastructure - must be available (fail-fast per FOUNDATION TENETS).
        using var scope = serviceProvider.CreateScope();
        var resourceClient = scope.ServiceProvider.GetRequiredService<IResourceClient>();

        var success = await AchievementService.RegisterResourceCleanupCallbacksAsync(resourceClient, CancellationToken.None);
        if (success)
        {
            Logger?.LogInformation("Registered character cleanup callbacks with lib-resource");
        }
        else
        {
            Logger?.LogWarning("Failed to register some cleanup callbacks with lib-resource");
        }
    }

    /// <summary>
    /// Shutdown the service - calls existing IBannouService lifecycle if present.
    /// </summary>
    protected override async Task OnShutdownAsync()
    {
        if (_service == null) return;

        Logger?.LogInformation("Shutting down Achievement service");

        try
        {
            // Call existing IBannouService.OnShutdownAsync if the service implements it
            if (_service is IBannouService bannouService)
            {
                Logger?.LogDebug("Calling IBannouService.OnShutdownAsync for Achievement service");
                await bannouService.OnShutdownAsync();
            }

            Logger?.LogInformation("Achievement service shutdown complete");
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Exception during Achievement service shutdown");
        }
    }
}
