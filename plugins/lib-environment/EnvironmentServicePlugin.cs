using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Resource;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Environment;

/// <summary>
/// Plugin wrapper for Environment service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class EnvironmentServicePlugin : StandardServicePlugin<IEnvironmentService>
{
    public override string PluginName => "environment";
    public override string DisplayName => "Environment Service";

    protected override async Task OnRunningAsync()
    {
        await base.OnRunningAsync();

        using var scope = ServiceProvider!.CreateScope();
        var resourceClient = scope.ServiceProvider.GetRequiredService<IResourceClient>();

        if (await EnvironmentService.RegisterResourceCleanupCallbacksAsync(resourceClient, CancellationToken.None))
            Logger?.LogInformation("Registered resource cleanup callbacks with lib-resource");
        else
            Logger?.LogWarning("Failed to register some resource cleanup callbacks");
    }
}
