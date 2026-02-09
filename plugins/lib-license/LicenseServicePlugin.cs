using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.License;

/// <summary>
/// Plugin wrapper for License service enabling plugin-based discovery and lifecycle management.
/// Bridges existing IBannouService implementation with the new Plugin system.
/// </summary>
public class LicenseServicePlugin : BaseBannouPlugin
{
    public override string PluginName => "license";
    public override string DisplayName => "License Board Service";

    private ILicenseService? _service;
    private IServiceProvider? _serviceProvider;
    private IServiceScope? _scope;

    /// <summary>
    /// Configure services for dependency injection - mimics existing [BannouService] registration.
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        Logger?.LogDebug("Configuring service dependencies");

        // Service registration is now handled centrally by PluginLoader based on [BannouService] attributes
        // No need to register ILicenseService and LicenseService here

        // Configuration registration is now handled centrally by PluginLoader based on [ServiceConfiguration] attributes
        // No need to register LicenseServiceConfiguration here

        // Add any service-specific dependencies
        // The generated clients should already be registered by AddAllBannouServiceClients()

        Logger?.LogDebug("Service dependencies configured");
    }

    /// <summary>
    /// Configure application pipeline - handles controller registration.
    /// </summary>
    public override void ConfigureApplication(WebApplication app)
    {
        Logger?.LogInformation("Configuring License service application pipeline");

        // The generated LicenseController should already be discovered via standard ASP.NET Core controller discovery

        // Store service provider for lifecycle management
        _serviceProvider = app.Services;

        Logger?.LogInformation("License service application pipeline configured");
    }

    /// <summary>
    /// Start the service - calls existing IBannouService lifecycle if present.
    /// </summary>
    protected override async Task<bool> OnStartAsync()
    {
        Logger?.LogInformation("Starting License service");

        try
        {
            // Get service instance from DI container with proper scope handling
            // Note: CreateScope() is required for Scoped services to avoid "Cannot resolve scoped service from root provider" error
            // Scope stored as field (not using var) because _service is used in OnRunningAsync/OnShutdownAsync
            _scope = _serviceProvider?.CreateScope();
            _service = _scope?.ServiceProvider.GetService<ILicenseService>();

            if (_service == null)
            {
                Logger?.LogError("Failed to resolve ILicenseService from DI container");
                return false;
            }

            // Call existing IBannouService.OnStartAsync if the service implements it
            if (_service is IBannouService bannouService)
            {
                Logger?.LogDebug("Calling IBannouService.OnStartAsync for License service");
                await bannouService.OnStartAsync(CancellationToken.None);
            }

            Logger?.LogInformation("License service started successfully");
            return true;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to start License service");
            return false;
        }
    }

    /// <summary>
    /// Running phase - calls existing IBannouService lifecycle if present.
    /// </summary>
    protected override async Task OnRunningAsync()
    {
        if (_service == null) return;

        Logger?.LogDebug("License service running");

        var serviceProvider = _serviceProvider ?? throw new InvalidOperationException("ServiceProvider not available during OnRunningAsync");

        try
        {
            // Call existing IBannouService.OnRunningAsync if the service implements it
            if (_service is IBannouService bannouService)
            {
                Logger?.LogDebug("Calling IBannouService.OnRunningAsync for License service");
                await bannouService.OnRunningAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Exception during License service running phase");
        }

        // Register cleanup callbacks with lib-resource for character reference tracking.
        // IResourceClient is L1 infrastructure - must be available (fail-fast per TENETS).
        using var scope = serviceProvider.CreateScope();
        var resourceClient = scope.ServiceProvider.GetRequiredService<IResourceClient>();

        var success = await LicenseService.RegisterResourceCleanupCallbacksAsync(resourceClient, CancellationToken.None);
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

        Logger?.LogInformation("Shutting down License service");

        try
        {
            // Call existing IBannouService.OnShutdownAsync if the service implements it
            if (_service is IBannouService bannouService)
            {
                Logger?.LogDebug("Calling IBannouService.OnShutdownAsync for License service");
                await bannouService.OnShutdownAsync();
            }

            Logger?.LogInformation("License service shutdown complete");
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Exception during License service shutdown");
        }
        finally
        {
            _scope?.Dispose();
            _scope = null;
        }
    }
}
