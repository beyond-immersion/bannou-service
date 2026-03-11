using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Resource;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Divine;

/// <summary>
/// Plugin wrapper for Divine service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class DivineServicePlugin : StandardServicePlugin<IDivineService>
{
    public override string PluginName => "divine";
    public override string DisplayName => "Divine Service";

    /// <inheritdoc />
    protected override async Task OnRunningAsync()
    {
        await base.OnRunningAsync();

        var serviceProvider = ServiceProvider
            ?? throw new InvalidOperationException("ServiceProvider not available during OnRunningAsync");
        using var scope = serviceProvider.CreateScope();
        var resourceClient = scope.ServiceProvider.GetRequiredService<IResourceClient>();

        var success = await DivineService.RegisterResourceCleanupCallbacksAsync(resourceClient, CancellationToken.None);
        if (success)
        {
            Logger?.LogInformation("Registered divine cleanup callbacks with lib-resource");
        }
        else
        {
            Logger?.LogWarning("Failed to register some divine cleanup callbacks with lib-resource");
        }
    }
}
