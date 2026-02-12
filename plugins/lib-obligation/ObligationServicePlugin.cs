using BeyondImmersion.BannouService.Obligation.Providers;
using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Resource;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Obligation;

/// <summary>
/// Plugin wrapper for Obligation service enabling plugin-based discovery and lifecycle management.
/// </summary>
/// <remarks>
/// Registers the ObligationProviderFactory for Actor (L2) to discover via DI,
/// and sets up lib-resource cleanup and compression callbacks during startup.
/// </remarks>
public class ObligationServicePlugin : BaseBannouPlugin
{
    public override string PluginName => "obligation";
    public override string DisplayName => "Obligation Service";

    private IServiceProvider? _serviceProvider;

    /// <summary>
    /// Configure services for dependency injection.
    /// Registers the obligation variable provider factory for Actor runtime discovery.
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        Logger?.LogDebug("Configuring obligation service dependencies");

        // Register obligation variable provider factory for Actor (L2) discovery via IEnumerable<IVariableProviderFactory>.
        // Enables ${obligations.*} paths in ABML behavior expressions without Actor depending on Obligation.
        services.AddSingleton<IVariableProviderFactory, ObligationProviderFactory>();

        Logger?.LogDebug("Obligation service dependencies configured");
    }

    /// <summary>
    /// Configure application pipeline.
    /// </summary>
    public override void ConfigureApplication(WebApplication app)
    {
        Logger?.LogInformation("Configuring Obligation service application pipeline");
        _serviceProvider = app.Services;
        Logger?.LogInformation("Obligation service application pipeline configured");
    }

    /// <summary>
    /// Start the service.
    /// </summary>
    protected override async Task<bool> OnStartAsync()
    {
        Logger?.LogInformation("Starting Obligation service");

        try
        {
            using var scope = _serviceProvider?.CreateScope();
            var service = scope?.ServiceProvider.GetService<IObligationService>();
            if (service == null)
            {
                Logger?.LogWarning("Obligation service not available from DI container");
                return false;
            }

            Logger?.LogInformation("Obligation service started");
            return true;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to start Obligation service");
            return false;
        }
    }

    /// <summary>
    /// Registers lib-resource cleanup and compression callbacks after the service is running.
    /// </summary>
    protected override async Task OnRunningAsync()
    {
        Logger?.LogDebug("Obligation service running");

        var serviceProvider = _serviceProvider ?? throw new InvalidOperationException(
            "ServiceProvider not available during OnRunningAsync");

        // Register cleanup callbacks with lib-resource for character reference tracking.
        // IResourceClient is L1 infrastructure - must be available (fail-fast per TENETS).
        using var scope = serviceProvider.CreateScope();
        var resourceClient = scope.ServiceProvider.GetRequiredService<IResourceClient>();

        var success = await ObligationService.RegisterResourceCleanupCallbacksAsync(
            resourceClient, CancellationToken.None);
        if (success)
        {
            Logger?.LogInformation("Registered character cleanup callbacks with lib-resource");
        }
        else
        {
            Logger?.LogWarning("Failed to register some cleanup callbacks with lib-resource");
        }

        // Register compression callback (generated from x-compression-callback)
        if (await ObligationCompressionCallbacks.RegisterAsync(resourceClient, CancellationToken.None))
        {
            Logger?.LogInformation("Registered obligation compression callback with lib-resource");
        }
        else
        {
            Logger?.LogWarning("Failed to register obligation compression callback with lib-resource");
        }
    }
}
