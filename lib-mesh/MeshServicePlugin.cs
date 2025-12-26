using BeyondImmersion.BannouService.Mesh.Services;
using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Forwarder;

namespace BeyondImmersion.BannouService.Mesh;

/// <summary>
/// Plugin wrapper for Mesh service enabling plugin-based discovery and lifecycle management.
/// Bridges existing IDaprService implementation with the new Plugin system.
/// </summary>
public class MeshServicePlugin : BaseBannouPlugin
{
    public override string PluginName => "mesh";
    public override string DisplayName => "Mesh Service";

    [Obsolete]
    private IMeshService? _service;
    private IServiceProvider? _serviceProvider;
    private IMeshRedisManager? _redisManager;

    /// <summary>
    /// Configure services for dependency injection - mimics existing [DaprService] registration.
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        Logger?.LogDebug("Configuring service dependencies");

        // Service registration is now handled centrally by PluginLoader based on [DaprService] attributes
        // No need to register IMeshService and MeshService here

        // Configuration registration is now handled centrally by PluginLoader based on [ServiceConfiguration] attributes
        // No need to register MeshServiceConfiguration here

        // Register MeshRedisManager as Singleton (direct Redis connection, NOT Dapr)
        // This avoids circular dependencies since Mesh IS the service discovery layer
        services.AddSingleton<IMeshRedisManager, MeshRedisManager>();

        // Register YARP HTTP Forwarder for service invocation
        services.AddHttpForwarder();

        // Register the mesh invocation client for service-to-service calls
        // This replaces DaprClient.InvokeMethodAsync for inter-service communication
        services.AddSingleton<IMeshInvocationClient>(sp =>
        {
            var meshClient = sp.GetRequiredService<IMeshClient>();
            var forwarder = sp.GetRequiredService<IHttpForwarder>();
            var logger = sp.GetRequiredService<ILogger<MeshInvocationClient>>();
            var httpClientFactory = sp.GetService<IHttpClientFactory>();
            return new MeshInvocationClient(meshClient, forwarder, logger, httpClientFactory);
        });

        Logger?.LogDebug("Service dependencies configured");
    }

    /// <summary>
    /// Configure application pipeline - handles controller registration.
    /// </summary>
    public override void ConfigureApplication(WebApplication app)
    {
        Logger?.LogInformation("Configuring Mesh service application pipeline");

        // The generated MeshController should already be discovered via standard ASP.NET Core controller discovery
        // since we're not excluding the assembly like we did with IDaprController approach

        // Store service provider for lifecycle management
        _serviceProvider = app.Services;

        Logger?.LogInformation("Mesh service application pipeline configured");
    }

    /// <summary>
    /// Start the service - calls existing IDaprService lifecycle if present.
    /// </summary>
    [Obsolete]
    protected override async Task<bool> OnStartAsync()
    {
        Logger?.LogInformation("Starting Mesh service");

        try
        {
            // Initialize Redis connection first (Mesh uses direct Redis, not Dapr)
            _redisManager = _serviceProvider?.GetService<IMeshRedisManager>();
            if (_redisManager != null)
            {
                Logger?.LogInformation("Initializing Mesh Redis connection...");
                var redisConnected = await _redisManager.InitializeAsync(CancellationToken.None);
                if (!redisConnected)
                {
                    Logger?.LogWarning("Mesh Redis connection not established - service will operate in degraded mode");
                }
            }

            // Get service instance from DI container with proper scope handling
            // Note: CreateScope() is required for Scoped services to avoid "Cannot resolve scoped service from root provider" error
            using var scope = _serviceProvider?.CreateScope();
            _service = scope?.ServiceProvider.GetService<IMeshService>();

            if (_service == null)
            {
                Logger?.LogError("Failed to resolve IMeshService from DI container");
                return false;
            }

            // Call existing IDaprService.OnStartAsync if the service implements it
            if (_service is IDaprService daprService)
            {
                Logger?.LogDebug("Calling IDaprService.OnStartAsync for Mesh service");
                await daprService.OnStartAsync(CancellationToken.None);
            }

            Logger?.LogInformation("Mesh service started successfully");
            return true;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to start Mesh service");
            return false;
        }
    }

    /// <summary>
    /// Running phase - calls existing IDaprService lifecycle if present.
    /// </summary>
    [Obsolete]
    protected override async Task OnRunningAsync()
    {
        if (_service == null) return;

        Logger?.LogDebug("Mesh service running");

        try
        {
            // Call existing IDaprService.OnRunningAsync if the service implements it
            if (_service is IDaprService daprService)
            {
                Logger?.LogDebug("Calling IDaprService.OnRunningAsync for Mesh service");
                await daprService.OnRunningAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Exception during Mesh service running phase");
        }
    }

    /// <summary>
    /// Shutdown the service - calls existing IDaprService lifecycle if present.
    /// </summary>
    [Obsolete]
    protected override async Task OnShutdownAsync()
    {
        if (_service == null) return;

        Logger?.LogInformation("Shutting down Mesh service");

        try
        {
            // Call existing IDaprService.OnShutdownAsync if the service implements it
            if (_service is IDaprService daprService)
            {
                Logger?.LogDebug("Calling IDaprService.OnShutdownAsync for Mesh service");
                await daprService.OnShutdownAsync();
            }

            Logger?.LogInformation("Mesh service shutdown complete");
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Exception during Mesh service shutdown");
        }
    }
}
