using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Resource;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Relationship;

/// <summary>
/// Plugin wrapper for Relationship service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class RelationshipServicePlugin : StandardServicePlugin<IRelationshipService>
{
    public override string PluginName => "relationship";
    public override string DisplayName => "Relationship Service";

    /// <summary>
    /// Running phase - registers cleanup callbacks with lib-resource for character and realm
    /// reference tracking. Must happen after all plugins are started so lib-resource (L1) is available.
    /// </summary>
    protected override async Task OnRunningAsync()
    {
        await base.OnRunningAsync();

        var serviceProvider = ServiceProvider ?? throw new InvalidOperationException("ServiceProvider not available during OnRunningAsync");

        // Register cleanup callbacks with lib-resource for character and realm reference tracking.
        // IResourceClient is L1 infrastructure - must be available (fail-fast per TENETS).
        using var scope = serviceProvider.CreateScope();
        var resourceClient = scope.ServiceProvider.GetRequiredService<IResourceClient>();

        var success = await RelationshipService.RegisterResourceCleanupCallbacksAsync(resourceClient, CancellationToken.None);
        if (success)
        {
            Logger?.LogInformation("Registered character and realm cleanup callbacks with lib-resource");
        }
        else
        {
            Logger?.LogWarning("Failed to register some cleanup callbacks with lib-resource");
        }
    }
}
