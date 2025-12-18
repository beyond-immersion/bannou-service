using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Species;

/// <summary>
/// Plugin wrapper for Species service enabling plugin-based discovery and lifecycle management.
/// Bridges existing IDaprService implementation with the new Plugin system.
/// </summary>
public class SpeciesServicePlugin : BaseBannouPlugin
{
    public override string PluginName => "species";
    public override string DisplayName => "Species Service";

    private ISpeciesService? _service;
    private IServiceProvider? _serviceProvider;

    /// <summary>
    /// Validate that this plugin should be loaded based on environment configuration.
    /// </summary>
    protected override bool OnValidatePlugin()
    {
        var enabled = Environment.GetEnvironmentVariable("SPECIES_SERVICE_ENABLED")?.ToLower();
        Logger?.LogDebug("Species service enabled check: {EnabledValue}", enabled);
        return enabled == "true";
    }

    /// <summary>
    /// Configure services for dependency injection - mimics existing [DaprService] registration.
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        if (!OnValidatePlugin())
        {
            Logger?.LogInformation("Species service disabled, skipping service registration");
            return;
        }

        Logger?.LogInformation("Configuring Species service dependencies");

        // Register the service implementation (existing pattern from [DaprService] attribute)
        services.AddScoped<ISpeciesService, SpeciesService>();
        services.AddScoped<SpeciesService>();

        // Register generated configuration class
        services.AddScoped<SpeciesServiceConfiguration>();

        // Add any service-specific dependencies
        // The generated clients should already be registered by AddAllBannouServiceClients()

        Logger?.LogInformation("Species service dependencies configured");
    }

    /// <summary>
    /// Configure application pipeline - handles controller registration.
    /// </summary>
    public override void ConfigureApplication(WebApplication app)
    {
        if (!OnValidatePlugin())
        {
            Logger?.LogInformation("Species service disabled, skipping application configuration");
            return;
        }

        Logger?.LogInformation("Configuring Species service application pipeline");

        // The generated SpeciesController should already be discovered via standard ASP.NET Core controller discovery
        // since we're not excluding the assembly like we did with IDaprController approach

        // Store service provider for lifecycle management
        _serviceProvider = app.Services;

        Logger?.LogInformation("Species service application pipeline configured");
    }

    /// <summary>
    /// Start the service - calls existing IDaprService lifecycle if present.
    /// </summary>
    protected override async Task<bool> OnStartAsync()
    {
        if (!OnValidatePlugin()) return true;

        Logger?.LogInformation("Starting Species service");

        try
        {
            // Get service instance from DI container with proper scope handling
            // Note: CreateScope() is required for Scoped services to avoid "Cannot resolve scoped service from root provider" error
            using var scope = _serviceProvider?.CreateScope();
            _service = scope?.ServiceProvider.GetService<ISpeciesService>();

            if (_service == null)
            {
                Logger?.LogError("Failed to resolve ISpeciesService from DI container");
                return false;
            }

            // Call existing IDaprService.OnStartAsync if the service implements it
            if (_service is IDaprService daprService)
            {
                Logger?.LogDebug("Calling IDaprService.OnStartAsync for Species service");
                await daprService.OnStartAsync(CancellationToken.None);
            }

            Logger?.LogInformation("Species service started successfully");
            return true;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to start Species service");
            return false;
        }
    }

    /// <summary>
    /// Running phase - calls existing IDaprService lifecycle if present.
    /// </summary>
    protected override async Task OnRunningAsync()
    {
        if (!OnValidatePlugin() || _service == null) return;

        Logger?.LogDebug("Species service running");

        try
        {
            // Call existing IDaprService.OnRunningAsync if the service implements it
            if (_service is IDaprService daprService)
            {
                Logger?.LogDebug("Calling IDaprService.OnRunningAsync for Species service");
                await daprService.OnRunningAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Exception during Species service running phase");
        }
    }

    /// <summary>
    /// Shutdown the service - calls existing IDaprService lifecycle if present.
    /// </summary>
    protected override async Task OnShutdownAsync()
    {
        if (!OnValidatePlugin() || _service == null) return;

        Logger?.LogInformation("Shutting down Species service");

        try
        {
            // Call existing IDaprService.OnShutdownAsync if the service implements it
            if (_service is IDaprService daprService)
            {
                Logger?.LogDebug("Calling IDaprService.OnShutdownAsync for Species service");
                await daprService.OnShutdownAsync();
            }

            Logger?.LogInformation("Species service shutdown complete");
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Exception during Species service shutdown");
        }
    }
}
