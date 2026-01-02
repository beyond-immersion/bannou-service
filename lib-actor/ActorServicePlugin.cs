using BeyondImmersion.Bannou.Behavior.Handlers;
using BeyondImmersion.BannouService.Actor.Caching;
using BeyondImmersion.BannouService.Actor.Execution;
using BeyondImmersion.BannouService.Actor.Runtime;
using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
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

        // Register behavior execution infrastructure
        services.AddSingleton<IBehaviorDocumentCache, BehaviorDocumentCache>();
        services.AddSingleton<IDocumentExecutorFactory, DocumentExecutorFactory>();

        // Register actor runtime components as singletons (shared across service instances)
        services.AddSingleton<IActorRegistry, ActorRegistry>();
        services.AddSingleton<IActorRunnerFactory, ActorRunnerFactory>();

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
            using var scope = _serviceProvider?.CreateScope();
            _service = scope?.ServiceProvider.GetService<IActorService>();

            if (_service == null)
            {
                Logger?.LogError("Failed to resolve IActorService from DI container");
                return false;
            }

            // Call existing IBannouService.OnStartAsync if the service implements it
            if (_service is IBannouService bannouService)
            {
                Logger?.LogDebug("Calling IBannouService.OnStartAsync for Actor service");
                await bannouService.OnStartAsync(CancellationToken.None);
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
            if (_serviceProvider != null)
            {
                var registry = _serviceProvider.GetService<IActorRegistry>();
                if (registry != null)
                {
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
