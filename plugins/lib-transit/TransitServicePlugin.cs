using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Transit.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Transit;

/// <summary>
/// Plugin wrapper for Transit service enabling plugin-based discovery and lifecycle management.
/// Registers helper services, background workers, and variable provider factory.
/// </summary>
public class TransitServicePlugin : StandardServicePlugin<ITransitService>
{
    /// <inheritdoc />
    public override string PluginName => "transit";

    /// <inheritdoc />
    public override string DisplayName => "Transit Service";

    /// <summary>
    /// Registers transit helper services, background workers, and DI providers.
    /// </summary>
    /// <param name="services">The service collection to register services with.</param>
    public override void ConfigureServices(IServiceCollection services)
    {
        // Background workers
        services.AddHostedService<SeasonalConnectionWorker>();
        services.AddHostedService<JourneyArchivalWorker>();
    }

    /// <inheritdoc />
    protected override async Task OnRunningAsync()
    {
        await base.OnRunningAsync();

        var serviceProvider = ServiceProvider
            ?? throw new InvalidOperationException("ServiceProvider not available during OnRunningAsync");
        using var scope = serviceProvider.CreateScope();
        var resourceClient = scope.ServiceProvider.GetRequiredService<IResourceClient>();

        var success = await TransitService.RegisterResourceCleanupCallbacksAsync(resourceClient, CancellationToken.None);
        if (success)
        {
            Logger?.LogInformation("Registered transit cleanup callbacks with lib-resource");
        }
        else
        {
            Logger?.LogWarning("Failed to register some transit cleanup callbacks with lib-resource");
        }
    }
}
