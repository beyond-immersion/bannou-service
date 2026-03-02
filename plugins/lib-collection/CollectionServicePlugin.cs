using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Resource;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Collection;

/// <summary>
/// Plugin wrapper for Collection service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class CollectionServicePlugin : StandardServicePlugin<ICollectionService>
{
    public override string PluginName => "collection";
    public override string DisplayName => "Collection Service";

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
