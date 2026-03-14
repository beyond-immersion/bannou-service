using BeyondImmersion.BannouService.Abml.Cognition.Handlers;
using BeyondImmersion.BannouService.Abml.Execution;
using BeyondImmersion.BannouService.Actor.Execution;
using BeyondImmersion.BannouService.Actor.Handlers;
using BeyondImmersion.BannouService.Actor.Pool;
using BeyondImmersion.BannouService.Actor.PoolNode;
using BeyondImmersion.BannouService.Actor.Providers;
using BeyondImmersion.BannouService.Actor.Runtime;
using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Resource;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Actor;

/// <summary>
/// Plugin wrapper for Actor service enabling plugin-based discovery and lifecycle management.
/// </summary>
/// <remarks>
/// <para>
/// <b>Registration:</b> ActorPoolManager is always registered (required by ActorService).
/// Additional components are registered based on deployment mode:
/// <list type="bullet">
/// <item>Pool node mode (ACTOR_POOL_NODE_ID set): Adds ActorPoolNodeWorker, HeartbeatEmitter</item>
/// <item>Control plane mode (non-bannou): Adds PoolHealthMonitor</item>
/// <item>Bannou mode: No additional components, actors run locally</item>
/// </list>
/// </para>
/// </remarks>
public class ActorServicePlugin : StandardServicePlugin<IActorService>
{
    /// <inheritdoc/>
    public override string PluginName => "actor";

    /// <inheritdoc/>
    public override string DisplayName => "Actor Service";

    /// <summary>
    /// Configure services for dependency injection.
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        // Register cognition handlers from bannou-service (used by DocumentExecutorFactory)
        services.AddSingleton<FilterAttentionHandler>();
        services.AddSingleton<AssessSignificanceHandler>();
        services.AddSingleton<QueryMemoryHandler>();
        services.AddSingleton<StoreMemoryHandler>();
        services.AddSingleton<EvaluateGoalImpactHandler>();
        services.AddSingleton<TriggerGoapReplanHandler>();

        // Register scheduled event manager for delayed event handling
        services.AddSingleton<IScheduledEventManager, ScheduledEventManager>();

        // Register behavior execution infrastructure
        // Note: Variable provider factories (personality, encounters, quests) are registered by their
        // owning L3/L4 plugins (lib-character-personality, lib-character-encounter, lib-quest) via
        // IVariableProviderFactory interface. Actor (L2) discovers them via DI collection injection.

        services.AddSingleton<BehaviorDocumentLoader>();
        services.AddSingleton<IBehaviorDocumentLoader>(sp => sp.GetRequiredService<BehaviorDocumentLoader>());

        // Register all pool-related services unconditionally
        // Each service checks its own configuration in ExecuteAsync/Start and becomes a no-op
        // if running in the wrong mode. This follows FOUNDATION TENETS - configuration access
        // via DI, not Environment.GetEnvironmentVariable during registration.
        services.AddSingleton<HeartbeatEmitter>();
        services.AddSingleton<ActorPoolNodeWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<ActorPoolNodeWorker>());
        services.AddHostedService<PoolHealthMonitor>();
    }

    /// <summary>
    /// Running phase - registers cleanup callbacks with lib-resource.
    /// </summary>
    protected override async Task OnRunningAsync()
    {
        await base.OnRunningAsync();

        // Register cleanup callbacks with lib-resource for character reference tracking.
        // IResourceClient is L1 infrastructure - must be available (fail-fast per TENETS).
        using var scope = ServiceProvider!.CreateScope();
        var resourceClient = scope.ServiceProvider.GetRequiredService<IResourceClient>();

        var success = await ActorService.RegisterResourceCleanupCallbacksAsync(resourceClient, CancellationToken.None);
        if (success)
        {
            Logger?.LogInformation("Registered character cleanup callbacks with lib-resource");
        }
        else
        {
            Logger?.LogWarning("Failed to register some cleanup callbacks with lib-resource");
        }
    }

    /// <summary>
    /// Shutdown the service - stops all running actors before standard shutdown.
    /// </summary>
    protected override async Task OnShutdownAsync()
    {
        Logger?.LogInformation("Shutting down Actor service");

        try
        {
            // Stop all running actors via the singleton registry (not scoped service)
            var registry = ServiceProvider!.GetRequiredService<IActorRegistry>();
            var actors = registry.GetAllRunners().ToList();
            Logger?.LogInformation("Stopping {ActorCount} running actors", actors.Count);

            foreach (var actor in actors)
            {
                try
                {
                    await actor.StopAsync(graceful: true);
                    registry.TryRemove(actor.ActorId, out _);
                    await actor.DisposeAsync();
                }
                catch (Exception ex)
                {
                    Logger?.LogWarning(ex, "Error stopping actor {ActorId} during shutdown", actor.ActorId);
                }
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Exception during Actor service actor cleanup");
        }

        await base.OnShutdownAsync();
    }
}
