using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Resource;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.RealmHistory;

/// <summary>
/// Plugin wrapper for Realm History service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class RealmHistoryServicePlugin : StandardServicePlugin<IRealmHistoryService>
{
    public override string PluginName => "realm-history";
    public override string DisplayName => "Realm History Service";

    /// <inheritdoc />
    protected override async Task OnRunningAsync()
    {
        await base.OnRunningAsync();

        var serviceProvider = ServiceProvider
            ?? throw new InvalidOperationException("ServiceProvider not available during OnRunningAsync");

        // Register cleanup callbacks with lib-resource for realm reference tracking.
        // IResourceClient is L1 infrastructure - must be available (fail-fast per TENETS).
        using var scope = serviceProvider.CreateScope();
        var resourceClient = scope.ServiceProvider.GetRequiredService<IResourceClient>();

        var success = await RealmHistoryService.RegisterResourceCleanupCallbacksAsync(resourceClient, CancellationToken.None);
        if (success)
        {
            Logger?.LogInformation("Registered realm cleanup callbacks with lib-resource");
        }
        else
        {
            Logger?.LogWarning("Failed to register some cleanup callbacks with lib-resource");
        }

        // Register compression callback (generated from x-compression-callback)
        if (await RealmHistoryCompressionCallbacks.RegisterAsync(resourceClient, CancellationToken.None))
        {
            Logger?.LogInformation("Registered realm-history compression callback with lib-resource");
        }
        else
        {
            Logger?.LogWarning("Failed to register realm-history compression callback with lib-resource");
        }
    }
}
