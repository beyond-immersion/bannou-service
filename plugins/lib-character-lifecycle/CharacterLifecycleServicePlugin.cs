using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Resource;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.CharacterLifecycle;

/// <summary>
/// Plugin wrapper for CharacterLifecycle service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class CharacterLifecycleServicePlugin : StandardServicePlugin<ICharacterLifecycleService>
{
    public override string PluginName => "character-lifecycle";
    public override string DisplayName => "CharacterLifecycle Service";

    protected override async Task OnRunningAsync()
    {
        await base.OnRunningAsync();

        using var scope = ServiceProvider!.CreateScope();
        var resourceClient = scope.ServiceProvider.GetRequiredService<IResourceClient>();

        if (await CharacterLifecycleService.RegisterResourceCleanupCallbacksAsync(resourceClient, CancellationToken.None))
            Logger?.LogInformation("Registered resource cleanup callbacks with lib-resource");
        else
            Logger?.LogWarning("Failed to register some resource cleanup callbacks");

        if (await CharacterLifecycleCompressionCallbacks.RegisterAsync(resourceClient, CancellationToken.None))
            Logger?.LogInformation("Registered compression callback with lib-resource");
        else
            Logger?.LogWarning("Failed to register compression callback");
    }
}
