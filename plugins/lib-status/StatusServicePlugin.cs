using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Resource;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Status;

/// <summary>
/// Plugin wrapper for Status service enabling plugin-based discovery and lifecycle management.
/// Registers DI services and cleanup callbacks on startup.
/// </summary>
public class StatusServicePlugin : StandardServicePlugin<IStatusService>
{
    /// <inheritdoc />
    public override string PluginName => "status";

    /// <inheritdoc />
    public override string DisplayName => "Status Service";

    /// <inheritdoc />
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        // Register seed evolution listener as singleton for DI discovery by Seed service (L2).
        // Seed discovers all ISeedEvolutionListener implementations via IEnumerable<ISeedEvolutionListener>.
        services.AddSingleton<ISeedEvolutionListener, StatusSeedEvolutionListener>();
    }

    /// <inheritdoc />
    protected override async Task OnRunningAsync()
    {
        await base.OnRunningAsync();

        // Register cleanup callbacks with lib-resource for entity types that can have statuses.
        // Uses generated x-references pattern from StatusReferenceTracking.cs.
        // IResourceClient is L1 infrastructure - must be available (fail-fast per FOUNDATION TENETS).
        var serviceProvider = ServiceProvider ?? throw new InvalidOperationException("ServiceProvider not available during OnRunningAsync");
        using var scope = serviceProvider.CreateScope();
        var resourceClient = scope.ServiceProvider.GetRequiredService<IResourceClient>();

        var success = await StatusService.RegisterResourceCleanupCallbacksAsync(resourceClient, CancellationToken.None);
        if (success)
        {
            Logger?.LogInformation("Registered status cleanup callbacks with lib-resource");
        }
        else
        {
            Logger?.LogWarning("Failed to register some status cleanup callbacks with lib-resource");
        }
    }
}
