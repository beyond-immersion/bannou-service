using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Resource;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Genesis;

/// <summary>
/// Plugin wrapper for Genesis service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class GenesisServicePlugin : StandardServicePlugin<IGenesisService>
{
    public override string PluginName => "genesis";
    public override string DisplayName => "Genesis Service";

    protected override async Task OnRunningAsync()
    {
        await base.OnRunningAsync();

        using var scope = ServiceProvider!.CreateScope();
        var resourceClient = scope.ServiceProvider.GetRequiredService<IResourceClient>();

        if (await GenesisService.RegisterResourceCleanupCallbacksAsync(resourceClient, CancellationToken.None))
            Logger?.LogInformation("Registered resource cleanup callbacks with lib-resource");
        else
            Logger?.LogWarning("Failed to register some resource cleanup callbacks");

        if (await GenesisCompressionCallbacks.RegisterAsync(resourceClient, CancellationToken.None))
            Logger?.LogInformation("Registered compression callback with lib-resource");
        else
            Logger?.LogWarning("Failed to register compression callback");
    }
}
