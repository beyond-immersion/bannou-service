using BeyondImmersion.BannouService.Achievement.Providers;
using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Resource;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Achievement;

/// <summary>
/// Plugin wrapper for Achievement service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class AchievementServicePlugin : StandardServicePlugin<IAchievementService>
{
    public override string PluginName => "achievement";
    public override string DisplayName => "Achievement Service";

    /// <summary>
    /// Configure services for dependency injection.
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        // Background service for periodic rarity percentage recalculation
        services.AddHostedService<RarityCalculationService>();
    }

    /// <summary>
    /// Running phase - registers cleanup callbacks with lib-resource.
    /// </summary>
    protected override async Task OnRunningAsync()
    {
        await base.OnRunningAsync();

        // Register cleanup callbacks with lib-resource for character reference tracking.
        // IResourceClient is L1 infrastructure - must be available (fail-fast per FOUNDATION TENETS).
        using var scope = ServiceProvider!.CreateScope();
        var resourceClient = scope.ServiceProvider.GetRequiredService<IResourceClient>();

        var success = await AchievementService.RegisterResourceCleanupCallbacksAsync(resourceClient, CancellationToken.None);
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
