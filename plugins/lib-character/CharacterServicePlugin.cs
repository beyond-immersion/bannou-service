using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Resource;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Character;

/// <summary>
/// Plugin wrapper for Character service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class CharacterServicePlugin : StandardServicePlugin<ICharacterService>
{
    public override string PluginName => "character";
    public override string DisplayName => "Character Service";

    /// <summary>
    /// Running phase - registers compression callback with lib-resource.
    /// </summary>
    protected override async Task OnRunningAsync()
    {
        await base.OnRunningAsync();

        // Register compression callback with lib-resource (generated from x-compression-callback)
        if (ServiceProvider == null) return;

        try
        {
            using var scope = ServiceProvider.CreateScope();
            var resourceClient = scope.ServiceProvider.GetService<IResourceClient>();
            if (resourceClient != null)
            {
                if (await CharacterCompressionCallbacks.RegisterAsync(resourceClient, CancellationToken.None))
                {
                    Logger?.LogInformation("Registered character compression callback with lib-resource");
                }
                else
                {
                    Logger?.LogWarning("Failed to register character compression callback with lib-resource");
                }
            }
            else
            {
                Logger?.LogDebug("IResourceClient not available - compression callback not registered");
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Failed to register compression callback with lib-resource");
        }
    }
}
