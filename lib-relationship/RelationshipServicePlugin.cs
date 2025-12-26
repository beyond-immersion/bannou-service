using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Relationship;

/// <summary>
/// Plugin wrapper for Relationship service enabling plugin-based discovery and lifecycle management.
/// Bridges existing IBannouService implementation with the new Plugin system.
/// </summary>
public class RelationshipServicePlugin : BaseBannouPlugin
{
    public override string PluginName => "relationship";
    public override string DisplayName => "Relationship Service";

    private IRelationshipService? _service;
    private IServiceProvider? _serviceProvider;

    /// <summary>
    /// Configure services for dependency injection - mimics existing [BannouService] registration.
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        Logger?.LogDebug("Configuring service dependencies");

        // Service registration is now handled centrally by PluginLoader based on [BannouService] attributes
        // Configuration registration is now handled centrally by PluginLoader based on [ServiceConfiguration] attributes
        // The generated clients should already be registered by AddAllBannouServiceClients()

        Logger?.LogDebug("Service dependencies configured");
    }

    /// <summary>
    /// Configure application pipeline - handles controller registration.
    /// </summary>
    public override void ConfigureApplication(WebApplication app)
    {
        Logger?.LogDebug("Configuring application pipeline");

        // The generated RelationshipController should already be discovered via standard ASP.NET Core controller discovery
        // Store service provider for lifecycle management
        _serviceProvider = app.Services;

        Logger?.LogDebug("Application pipeline configured");
    }

    /// <summary>
    /// Start the service - calls existing IBannouService lifecycle if present.
    /// </summary>
    protected override async Task<bool> OnStartAsync()
    {
        Logger?.LogInformation("Starting service");

        try
        {
            using var scope = _serviceProvider?.CreateScope();
            _service = scope?.ServiceProvider.GetService<IRelationshipService>();

            if (_service == null)
            {
                Logger?.LogError("Failed to resolve IRelationshipService from DI container");
                return false;
            }

            if (_service is IBannouService bannouService)
            {
                Logger?.LogDebug("Calling IBannouService.OnStartAsync for Relationship service");
                await bannouService.OnStartAsync(CancellationToken.None);
            }

            Logger?.LogInformation("Service started");
            return true;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to start service");
            return false;
        }
    }

    /// <summary>
    /// Running phase - calls existing IBannouService lifecycle if present.
    /// </summary>
    protected override async Task OnRunningAsync()
    {
        if (_service == null) return;

        Logger?.LogDebug("Service running");

        try
        {
            if (_service is IBannouService bannouService)
            {
                Logger?.LogDebug("Calling IBannouService.OnRunningAsync for Relationship service");
                await bannouService.OnRunningAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Exception during running phase");
        }
    }

    /// <summary>
    /// Shutdown the service - calls existing IBannouService lifecycle if present.
    /// </summary>
    protected override async Task OnShutdownAsync()
    {
        if (_service == null) return;

        Logger?.LogInformation("Shutting down service");

        try
        {
            if (_service is IBannouService bannouService)
            {
                Logger?.LogDebug("Calling IBannouService.OnShutdownAsync for Relationship service");
                await bannouService.OnShutdownAsync();
            }

            Logger?.LogInformation("Service shutdown complete");
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Exception during shutdown");
        }
    }
}
