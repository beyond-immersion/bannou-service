using BeyondImmersion.Bannou.Behavior.Handlers;
using BeyondImmersion.BannouService.Abml.Execution;
using BeyondImmersion.BannouService.Actor.Caching;
using BeyondImmersion.BannouService.Actor.Execution;
using BeyondImmersion.BannouService.Actor.Handlers;
using BeyondImmersion.BannouService.Actor.Pool;
using BeyondImmersion.BannouService.Actor.PoolNode;
using BeyondImmersion.BannouService.Actor.Runtime;
using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Resource;
using BeyondImmersion.BannouService.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Actor;

/// <summary>
/// Plugin wrapper for Actor service enabling plugin-based discovery and lifecycle management.
/// Bridges existing IBannouService implementation with the new Plugin system.
/// </summary>
public class ActorServicePlugin : BaseBannouPlugin
{
    /// <inheritdoc/>
    public override string PluginName => "actor";

    /// <inheritdoc/>
    public override string DisplayName => "Actor Service";

    private IActorService? _service;
    private IServiceProvider? _serviceProvider;

    /// <summary>
    /// Configure services for dependency injection - mimics existing [BannouService] registration.
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
    public override void ConfigureServices(IServiceCollection services)
    {
        Logger?.LogDebug("Configuring service dependencies");

        // Service registration is now handled centrally by PluginLoader based on [BannouService] attributes
        // No need to register IActorService and ActorService here

        // Configuration registration is now handled centrally by PluginLoader based on [ServiceConfiguration] attributes
        // No need to register ActorServiceConfiguration here

        // Register cognition handlers from lib-behavior (used by DocumentExecutorFactory)
        services.AddSingleton<FilterAttentionHandler>();
        services.AddSingleton<AssessSignificanceHandler>();
        services.AddSingleton<QueryMemoryHandler>();
        services.AddSingleton<StoreMemoryHandler>();
        services.AddSingleton<EvaluateGoalImpactHandler>();
        services.AddSingleton<TriggerGoapReplanHandler>();

        // Register Event Brain action handlers (used by DocumentExecutorFactory)
        services.AddSingleton<IActionHandler, QueryOptionsHandler>();
        services.AddSingleton<IActionHandler, QueryActorStateHandler>();
        services.AddSingleton<IActionHandler, EmitPerceptionHandler>();
        services.AddSingleton<IActionHandler, ScheduleEventHandler>();
        services.AddSingleton<IActionHandler, StateUpdateHandler>();
        services.AddSingleton<IActionHandler, SetEncounterPhaseHandler>();
        services.AddSingleton<IActionHandler, EndEncounterHandler>();

        // Register scheduled event manager for delayed event handling
        services.AddSingleton<IScheduledEventManager, ScheduledEventManager>();

        // Register behavior execution infrastructure
        services.AddSingleton<IBehaviorDocumentCache, BehaviorDocumentCache>();
        services.AddSingleton<IPersonalityCache, PersonalityCache>();
        services.AddSingleton<IEncounterCache, EncounterCache>();
        services.AddSingleton<IDocumentExecutorFactory, DocumentExecutorFactory>();

        // Register actor runtime components as singletons (shared across service instances)
        services.AddSingleton<IActorRegistry, ActorRegistry>();
        services.AddSingleton<IActorRunnerFactory, ActorRunnerFactory>();

        // Always register pool manager - required by ActorService constructor
        // In bannou mode it's available but pool operations will be local-only
        services.AddSingleton<IActorPoolManager, ActorPoolManager>();

        // Register all pool-related services unconditionally
        // Each service checks its own configuration in ExecuteAsync/Start and becomes a no-op
        // if running in the wrong mode. This follows FOUNDATION TENETS - configuration access
        // via DI, not Environment.GetEnvironmentVariable during registration.
        services.AddSingleton<HeartbeatEmitter>();
        services.AddSingleton<ActorPoolNodeWorker>();
        services.AddHostedService(sp => sp.GetRequiredService<ActorPoolNodeWorker>());
        services.AddHostedService<PoolHealthMonitor>();

        Logger?.LogDebug("Registered all actor pool components (mode determined at runtime via configuration)");

        Logger?.LogDebug("Service dependencies configured");
    }

    /// <summary>
    /// Configure application pipeline - handles controller registration.
    /// </summary>
    public override void ConfigureApplication(WebApplication app)
    {
        Logger?.LogInformation("Configuring Actor service application pipeline");

        // The generated ActorController should already be discovered via standard ASP.NET Core controller discovery

        // Store service provider for lifecycle management
        _serviceProvider = app.Services;

        Logger?.LogInformation("Actor service application pipeline configured");
    }

    /// <summary>
    /// Start the service - calls existing IBannouService lifecycle if present.
    /// </summary>
    protected override async Task<bool> OnStartAsync()
    {
        Logger?.LogInformation("Starting Actor service");

        try
        {
            // Get service instance from DI container with proper scope handling
            // Note: CreateScope() is required for Scoped services to avoid "Cannot resolve scoped service from root provider" error
            var serviceProvider = _serviceProvider ?? throw new InvalidOperationException("ServiceProvider not available during OnStartAsync");
            using var scope = serviceProvider.CreateScope();
            _service = scope.ServiceProvider.GetRequiredService<IActorService>();

            // Call existing IBannouService.OnStartAsync if the service implements it
            if (_service is IBannouService bannouService)
            {
                Logger?.LogDebug("Calling IBannouService.OnStartAsync for Actor service");
                await bannouService.OnStartAsync(CancellationToken.None);
            }

            // Register cleanup callbacks with lib-resource for character reference tracking
            // This enables cascading cleanup when characters are deleted
            try
            {
                var resourceClient = scope.ServiceProvider.GetService<IResourceClient>();
                if (resourceClient != null)
                {
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
                else
                {
                    Logger?.LogDebug("IResourceClient not available - cleanup callbacks not registered (lib-resource may not be enabled)");
                }
            }
            catch (Exception ex)
            {
                Logger?.LogWarning(ex, "Failed to register cleanup callbacks with lib-resource");
            }

            Logger?.LogInformation("Actor service started successfully");
            return true;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to start Actor service");
            return false;
        }
    }

    /// <summary>
    /// Running phase - calls existing IBannouService lifecycle if present.
    /// </summary>
    protected override async Task OnRunningAsync()
    {
        if (_service == null) return;

        Logger?.LogDebug("Actor service running");

        try
        {
            // Call existing IBannouService.OnRunningAsync if the service implements it
            if (_service is IBannouService bannouService)
            {
                Logger?.LogDebug("Calling IBannouService.OnRunningAsync for Actor service");
                await bannouService.OnRunningAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Exception during Actor service running phase");
        }
    }

    /// <summary>
    /// Shutdown the service - calls existing IBannouService lifecycle if present.
    /// </summary>
    protected override async Task OnShutdownAsync()
    {
        if (_service == null) return;

        Logger?.LogInformation("Shutting down Actor service");

        try
        {
            // Stop all running actors
            var serviceProvider = _serviceProvider ?? throw new InvalidOperationException("ServiceProvider not available during OnShutdownAsync");
            var registry = serviceProvider.GetRequiredService<IActorRegistry>();
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

            // Call existing IBannouService.OnShutdownAsync if the service implements it
            if (_service is IBannouService bannouService)
            {
                Logger?.LogDebug("Calling IBannouService.OnShutdownAsync for Actor service");
                await bannouService.OnShutdownAsync();
            }

            Logger?.LogInformation("Actor service shutdown complete");
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Exception during Actor service shutdown");
        }
    }
}
