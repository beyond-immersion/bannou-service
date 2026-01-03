using BeyondImmersion.Bannou.Behavior.Cognition;
using BeyondImmersion.Bannou.Behavior.Compiler;
using BeyondImmersion.Bannou.Behavior.Goap;
using BeyondImmersion.Bannou.Behavior.Handlers;
using BeyondImmersion.BannouService.Abml.Execution;
using BeyondImmersion.BannouService.Asset;
using BeyondImmersion.BannouService.Behavior.Runtime;
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
    /// Registers the GOAP planner, ABML compiler, cognition pipeline, and handlers.
    /// </summary>
    /// <param name="services">The service collection.</param>
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        // Register GOAP planner as singleton - it's thread-safe and stateless
        services.AddSingleton<IGoapPlanner, GoapPlanner>();

        // Register ABML behavior compiler as singleton - it's thread-safe and stateless
        services.AddSingleton<BehaviorCompiler>();

        // Register behavior model interpreter factory for runtime execution
        services.AddSingleton<IBehaviorModelInterpreterFactory, BehaviorModelInterpreterFactory>();

        // Register asset client for storing compiled behavior models
        // Uses mesh client for service-to-service calls
        services.AddScoped<IAssetClient, AssetClient>();

        // Register bundle manager for efficient behavior grouping and storage
        services.AddScoped<IBehaviorBundleManager, BehaviorBundleManager>();

        // Register memory store for cognition pipeline (actor-local MVP)
        services.AddSingleton<IMemoryStore, ActorLocalMemoryStore>();

        // Register cognition pipeline handlers
        // These handle domain actions for the 5-stage cognition pipeline
        services.AddSingleton<IActionHandler, FilterAttentionHandler>();
        services.AddSingleton<IActionHandler, QueryMemoryHandler>();
        services.AddSingleton<IActionHandler, AssessSignificanceHandler>();
        services.AddSingleton<IActionHandler, StoreMemoryHandler>();
        services.AddSingleton<IActionHandler, EvaluateGoalImpactHandler>();
        services.AddSingleton<IActionHandler, TriggerGoapReplanHandler>();
    }
}
