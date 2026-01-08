using BeyondImmersion.Bannou.Behavior.Cognition;
using BeyondImmersion.Bannou.Behavior.Compiler;
using BeyondImmersion.Bannou.Behavior.Coordination;
using BeyondImmersion.Bannou.Behavior.Dialogue;
using BeyondImmersion.Bannou.Behavior.Goap;
using BeyondImmersion.Bannou.Behavior.Handlers;
using BeyondImmersion.BannouService.Abml.Execution;
using BeyondImmersion.BannouService.Asset;
using BeyondImmersion.BannouService.Behavior.Archetypes;
using BeyondImmersion.BannouService.Behavior.Control;
using BeyondImmersion.BannouService.Behavior.Handlers;
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

        // Register archetype registry - singleton with pre-loaded archetypes
        // ArchetypeRegistry constructor registers all standard archetypes automatically
        services.AddSingleton<IArchetypeRegistry, ArchetypeRegistry>();

        // Register intent emitter registry - singleton with core emitters
        services.AddSingleton<IIntentEmitterRegistry>(sp =>
            IntentEmitterRegistry.CreateWithCoreEmitters());

        // Register control gate registry - singleton for per-entity control tracking
        services.AddSingleton<IControlGateRegistry, ControlGateManager>();

        // Register cognition pipeline handlers
        // These handle domain actions for the 5-stage cognition pipeline
        services.AddSingleton<IActionHandler, FilterAttentionHandler>();
        services.AddSingleton<IActionHandler, QueryMemoryHandler>();
        services.AddSingleton<IActionHandler, AssessSignificanceHandler>();
        services.AddSingleton<IActionHandler, StoreMemoryHandler>();
        services.AddSingleton<IActionHandler, EvaluateGoalImpactHandler>();
        services.AddSingleton<IActionHandler, TriggerGoapReplanHandler>();

        // Register dialogue & localization layer (Layer 6)
        // External dialogue loader for YAML-based dialogue files
        services.AddSingleton<IExternalDialogueLoader>(sp =>
        {
            var loader = new ExternalDialogueLoader(new ExternalDialogueLoaderOptions
            {
                EnableCaching = true,
                LogFileLoads = false
            });
            return loader;
        });

        // Dialogue resolver with three-step resolution pipeline
        services.AddSingleton<IDialogueResolver, DialogueResolver>();

        // Localization provider for string table lookups
        services.AddSingleton<ILocalizationProvider, FileLocalizationProvider>();

        // Register cognition layering system (Layer 7)
        // Template registry for base cognition templates (humanoid, creature, object)
        services.AddSingleton<ICognitionTemplateRegistry>(sp =>
        {
            var registry = new CognitionTemplateRegistry(
                logger: sp.GetService<Microsoft.Extensions.Logging.ILogger<CognitionTemplateRegistry>>(),
                loadEmbeddedDefaults: true);
            return registry;
        });

        // Cognition builder for constructing pipelines from templates with overrides
        services.AddSingleton<ICognitionBuilder>(sp =>
        {
            var registry = sp.GetRequiredService<ICognitionTemplateRegistry>();
            var handlerRegistry = sp.GetService<IActionHandlerRegistry>();
            return new CognitionBuilder(
                registry,
                handlerRegistry,
                sp.GetService<Microsoft.Extensions.Logging.ILogger<CognitionBuilder>>());
        });
    }
}
