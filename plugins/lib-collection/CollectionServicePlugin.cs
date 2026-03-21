using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Collection;

/// <summary>
/// Plugin wrapper for Collection service enabling plugin-based discovery and lifecycle management.
/// Registers CollectionInstanceEventBatcher and its flush worker for batch lifecycle event publishing.
/// </summary>
public class CollectionServicePlugin : StandardServicePlugin<ICollectionService>
{
    public override string PluginName => "collection";
    public override string DisplayName => "Collection Service";

    /// <summary>
    /// Registers Collection-specific DI services: instance lifecycle event batcher and its worker.
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        // CollectionInstanceEventBatcher auto-registered via [BannouHelperService] (Concrete mode)

        // Single worker flushes both batchers (created, destroyed) per cycle.
        services.AddSingleton<IHostedService>(sp =>
        {
            var batcher = sp.GetRequiredService<CollectionInstanceEventBatcher>();
            var config = sp.GetRequiredService<CollectionServiceConfiguration>();
            return new EventBatcherWorker(
                batcher.AllFlushables,
                sp,
                sp.GetRequiredService<ILogger<EventBatcherWorker>>(),
                sp.GetRequiredService<ITelemetryProvider>(),
                config.InstanceEventBatchIntervalSeconds,
                config.InstanceEventBatchStartupDelaySeconds,
                "collection",
                "InstanceEventBatcher");
        });
    }

    /// <summary>
    /// Registers resource cleanup callbacks with lib-resource after the service is running.
    /// </summary>
    protected override async Task OnRunningAsync()
    {
        await base.OnRunningAsync();

        var serviceProvider = ServiceProvider ?? throw new InvalidOperationException("ServiceProvider not available during OnRunningAsync");

        // Register cleanup callbacks with lib-resource for character reference tracking.
        // IResourceClient is L1 infrastructure - must be available (fail-fast per FOUNDATION TENETS).
        using var scope = serviceProvider.CreateScope();
        var resourceClient = scope.ServiceProvider.GetRequiredService<IResourceClient>();

        var success = await CollectionService.RegisterResourceCleanupCallbacksAsync(resourceClient, CancellationToken.None);
        if (success)
        {
            Logger?.LogInformation("Registered character cleanup callbacks with lib-resource");
        }
        else
        {
            Logger?.LogWarning("Failed to register some cleanup callbacks with lib-resource");
        }
    }
}
