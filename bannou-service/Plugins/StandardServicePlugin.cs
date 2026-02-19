using BeyondImmersion.BannouService.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Plugins;

/// <summary>
/// Generic base class for standard service plugins that eliminates lifecycle boilerplate.
/// Most service plugins only need to specify PluginName/DisplayName and optionally
/// override ConfigureServices to add extra DI registrations.
/// </summary>
/// <typeparam name="TService">The service interface type (e.g., ICharacterService)</typeparam>
public abstract class StandardServicePlugin<TService> : BaseBannouPlugin
    where TService : class
{
    /// <summary>
    /// The resolved service instance, available after OnStartAsync completes.
    /// </summary>
    protected TService? Service { get; private set; }

    /// <summary>
    /// The service provider, available after ConfigureApplication completes.
    /// </summary>
    protected IServiceProvider? ServiceProvider { get; private set; }

    /// <summary>
    /// Configure application pipeline - stores service provider for lifecycle management.
    /// Override to add custom pipeline configuration, but call base.ConfigureApplication first.
    /// </summary>
    public override void ConfigureApplication(WebApplication app)
    {
        Logger?.LogDebug("Configuring application pipeline");

        // Store service provider for lifecycle management
        ServiceProvider = app.Services;

        Logger?.LogDebug("Application pipeline configured");
    }

    /// <summary>
    /// Start the service - resolves service from DI and calls IBannouService.OnStartAsync if implemented.
    /// Override to add custom startup logic (e.g., Redis initialization), but call base.OnStartAsync.
    /// </summary>
    protected override async Task<bool> OnStartAsync()
    {
        Logger?.LogInformation("Starting service");

        try
        {
            // Get service instance from DI container with proper scope handling
            // Note: CreateScope() is required for Scoped services to avoid
            // "Cannot resolve scoped service from root provider" error
            var serviceProvider = ServiceProvider ?? throw new InvalidOperationException("ServiceProvider not available during OnStartAsync");
            using var scope = serviceProvider.CreateScope();
            Service = scope.ServiceProvider.GetRequiredService<TService>();

            // Call existing IBannouService.OnStartAsync if the service implements it
            if (Service is IBannouService bannouService)
            {
                Logger?.LogDebug("Calling IBannouService.OnStartAsync for {PluginName} service", PluginName);
                await bannouService.OnStartAsync(CancellationToken.None);
            }

            Logger?.LogInformation("Service started");
            return true;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to start service");
            throw;
        }
    }

    /// <summary>
    /// Running phase - calls IBannouService.OnRunningAsync if the service implements it.
    /// Override to add custom running logic, but call base.OnRunningAsync.
    /// </summary>
    protected override async Task OnRunningAsync()
    {
        if (Service == null) return;

        Logger?.LogDebug("Service running");

        try
        {
            // Call existing IBannouService.OnRunningAsync if the service implements it
            if (Service is IBannouService bannouService)
            {
                Logger?.LogDebug("Calling IBannouService.OnRunningAsync for {PluginName} service", PluginName);
                await bannouService.OnRunningAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Exception during running phase");
        }
    }

    /// <summary>
    /// Shutdown the service - calls IBannouService.OnShutdownAsync if the service implements it.
    /// Override to add custom shutdown logic, but call base.OnShutdownAsync.
    /// </summary>
    protected override async Task OnShutdownAsync()
    {
        if (Service == null) return;

        Logger?.LogInformation("Shutting down service");

        try
        {
            // Call existing IBannouService.OnShutdownAsync if the service implements it
            if (Service is IBannouService bannouService)
            {
                Logger?.LogDebug("Calling IBannouService.OnShutdownAsync for {PluginName} service", PluginName);
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
