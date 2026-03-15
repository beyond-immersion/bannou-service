using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Resource;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Species;

/// <summary>
/// Plugin wrapper for Species service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class SpeciesServicePlugin : StandardServicePlugin<ISpeciesService>
{
    public override string PluginName => "species";
    public override string DisplayName => "Species Service";

    /// <inheritdoc />
    protected override async Task OnRunningAsync()
    {
        await base.OnRunningAsync();

        var serviceProvider = ServiceProvider
            ?? throw new InvalidOperationException("ServiceProvider not available during OnRunningAsync");
        using var scope = serviceProvider.CreateScope();
        var resourceClient = scope.ServiceProvider.GetRequiredService<IResourceClient>();

        var success = await SpeciesService.RegisterResourceMigrateCallbacksAsync(resourceClient, CancellationToken.None);
        if (success)
        {
            Logger?.LogInformation("Registered species migrate callbacks with lib-resource");
        }
        else
        {
            Logger?.LogWarning("Failed to register some species migrate callbacks with lib-resource");
        }
    }
}
