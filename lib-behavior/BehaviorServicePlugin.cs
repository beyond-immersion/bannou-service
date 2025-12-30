using BeyondImmersion.Bannou.Behavior.Compiler;
using BeyondImmersion.Bannou.Behavior.Goap;
using BeyondImmersion.BannouService.Asset;
using BeyondImmersion.BannouService.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace BeyondImmersion.BannouService.Behavior;

/// <summary>
/// Plugin wrapper for Behavior service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class BehaviorServicePlugin : StandardServicePlugin<IBehaviorService>
{
    /// <inheritdoc/>
    public override string PluginName => "behavior";

    /// <inheritdoc/>
    public override string DisplayName => "Behavior Service";

    /// <summary>
    /// Configures additional services for the Behavior plugin.
    /// Registers the GOAP planner and ABML compiler as singletons (thread-safe, stateless).
    /// </summary>
    /// <param name="services">The service collection.</param>
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        // Register GOAP planner as singleton - it's thread-safe and stateless
        services.AddSingleton<IGoapPlanner, GoapPlanner>();

        // Register ABML behavior compiler as singleton - it's thread-safe and stateless
        services.AddSingleton<BehaviorCompiler>();

        // Register asset client for storing compiled behavior models
        // Uses mesh client for service-to-service calls
        services.AddScoped<IAssetClient, AssetClient>();

        // Register bundle manager for efficient behavior grouping and storage
        services.AddScoped<BehaviorBundleManager>();
    }
}
