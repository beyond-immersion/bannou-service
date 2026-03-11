using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Worldstate.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Worldstate;

/// <summary>
/// Plugin wrapper for Worldstate service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class WorldstateServicePlugin : StandardServicePlugin<IWorldstateService>
{
    public override string PluginName => "worldstate";
    public override string DisplayName => "Worldstate Service";

    /// <summary>
    /// Registers worldstate helper services for dependency injection.
    /// </summary>
    /// <param name="services">The service collection to register services with.</param>
    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddHostedService<WorldstateClockWorkerService>();
    }

    /// <inheritdoc />
    protected override async Task OnRunningAsync()
    {
        await base.OnRunningAsync();

        var serviceProvider = ServiceProvider
            ?? throw new InvalidOperationException("ServiceProvider not available during OnRunningAsync");
        using var scope = serviceProvider.CreateScope();
        var resourceClient = scope.ServiceProvider.GetRequiredService<IResourceClient>();

        var success = await WorldstateService.RegisterResourceCleanupCallbacksAsync(resourceClient, CancellationToken.None);
        if (success)
        {
            Logger?.LogInformation("Registered worldstate cleanup callbacks with lib-resource");
        }
        else
        {
            Logger?.LogWarning("Failed to register some worldstate cleanup callbacks with lib-resource");
        }
    }
}
