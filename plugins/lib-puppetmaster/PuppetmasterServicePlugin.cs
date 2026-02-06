using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Puppetmaster.Caching;
using BeyondImmersion.BannouService.Puppetmaster.Providers;
using BeyondImmersion.BannouService.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Puppetmaster;

/// <summary>
/// Plugin wrapper for Puppetmaster service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class PuppetmasterServicePlugin : BaseBannouPlugin
{
    /// <inheritdoc />
    public override string PluginName => "puppetmaster";

    /// <inheritdoc />
    public override string DisplayName => "Puppetmaster Service";

    private IPuppetmasterService? _service;
    private IServiceProvider? _serviceProvider;

    /// <summary>
    /// Configure services for dependency injection.
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        Logger?.LogDebug("Configuring Puppetmaster service dependencies");

        // Service registration is handled centrally by PluginLoader based on [BannouService] attributes
        // Configuration registration is handled centrally based on [ServiceConfiguration] attributes

        // Register the behavior document cache as singleton (in-memory caching)
        services.AddSingleton<BehaviorDocumentCache>();
        services.AddSingleton<IBehaviorDocumentCache>(sp => sp.GetRequiredService<BehaviorDocumentCache>());

        // Register the dynamic behavior provider (priority 100 = highest)
        // This enables lib-actor to discover and use our provider via DI
        services.AddSingleton<IBehaviorDocumentProvider, DynamicBehaviorProvider>();

        Logger?.LogDebug("Puppetmaster service dependencies configured");
    }

    /// <summary>
    /// Configure application pipeline.
    /// </summary>
    public override void ConfigureApplication(WebApplication app)
    {
        Logger?.LogInformation("Configuring Puppetmaster service application pipeline");

        // Store service provider for lifecycle management
        _serviceProvider = app.Services;

        Logger?.LogInformation("Puppetmaster service application pipeline configured");
    }

    /// <summary>
    /// Start the service.
    /// </summary>
    protected override async Task<bool> OnStartAsync()
    {
        Logger?.LogInformation("Starting Puppetmaster service");

        try
        {
            // Get service instance from DI container
            // Note: PuppetmasterService is Singleton, so no scope needed
            _service = _serviceProvider?.GetService<IPuppetmasterService>();

            if (_service == null)
            {
                Logger?.LogError("Failed to resolve IPuppetmasterService from DI container");
                return false;
            }

            // Call existing IBannouService.OnStartAsync if the service implements it
            if (_service is IBannouService bannouService)
            {
                Logger?.LogDebug("Calling IBannouService.OnStartAsync for Puppetmaster service");
                await bannouService.OnStartAsync(CancellationToken.None);
            }

            Logger?.LogInformation("Puppetmaster service started successfully");
            return true;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to start Puppetmaster service");
            return false;
        }
    }

    /// <summary>
    /// Running phase - calls existing IBannouService lifecycle if present.
    /// </summary>
    protected override async Task OnRunningAsync()
    {
        if (_service == null) return;

        Logger?.LogDebug("Puppetmaster service running");

        try
        {
            if (_service is IBannouService bannouService)
            {
                Logger?.LogDebug("Calling IBannouService.OnRunningAsync for Puppetmaster service");
                await bannouService.OnRunningAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Exception during Puppetmaster service running phase");
        }
    }

    /// <summary>
    /// Shutdown the service.
    /// </summary>
    protected override async Task OnShutdownAsync()
    {
        if (_service == null) return;

        Logger?.LogInformation("Shutting down Puppetmaster service");

        try
        {
            if (_service is IBannouService bannouService)
            {
                Logger?.LogDebug("Calling IBannouService.OnShutdownAsync for Puppetmaster service");
                await bannouService.OnShutdownAsync();
            }

            Logger?.LogInformation("Puppetmaster service shutdown complete");
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Exception during Puppetmaster service shutdown");
        }
    }
}
