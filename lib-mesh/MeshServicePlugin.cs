using BeyondImmersion.BannouService.Mesh.Services;
using BeyondImmersion.BannouService.Plugins;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Mesh;

/// <summary>
/// Plugin wrapper for Mesh service enabling plugin-based discovery and lifecycle management.
/// </summary>
public class MeshServicePlugin : StandardServicePlugin<IMeshService>
{
    public override string PluginName => "mesh";
    public override string DisplayName => "Mesh Service";

    private IMeshRedisManager? _redisManager;
    private bool _useLocalRouting;

    public override void ConfigureServices(IServiceCollection services)
    {
        Logger?.LogDebug("Configuring service dependencies");

        // Get configuration to check for local routing mode
        var config = services.BuildServiceProvider().GetService<MeshServiceConfiguration>();
        _useLocalRouting = config?.UseLocalRouting ?? false;

        if (_useLocalRouting)
        {
            Logger?.LogWarning(
                "Mesh using LOCAL ROUTING mode. All service calls will route locally (no Redis)!");

            // Register LocalMeshRedisManager (no Redis connection)
            services.AddSingleton<IMeshRedisManager, LocalMeshRedisManager>();
        }
        else
        {
            // Register MeshRedisManager as Singleton (direct Redis connection for service discovery)
            // This avoids circular dependencies since Mesh IS the service discovery layer
            services.AddSingleton<IMeshRedisManager, MeshRedisManager>();
        }

        // Register the mesh invocation client for service-to-service calls
        services.AddSingleton<IMeshInvocationClient>(sp =>
        {
            var meshClient = sp.GetRequiredService<IMeshClient>();
            var logger = sp.GetRequiredService<ILogger<MeshInvocationClient>>();
            return new MeshInvocationClient(meshClient, logger);
        });

        Logger?.LogDebug("Service dependencies configured");
    }

    protected override async Task<bool> OnStartAsync()
    {
        Logger?.LogInformation("Starting Mesh service");

        // Initialize Redis connection first (Mesh uses direct Redis for service discovery)
        // In local routing mode, this is a no-op that always succeeds
        _redisManager = ServiceProvider?.GetService<IMeshRedisManager>();
        if (_redisManager != null)
        {
            if (_useLocalRouting)
            {
                Logger?.LogInformation("Mesh using local routing mode (no Redis)");
            }
            else
            {
                Logger?.LogInformation("Initializing Mesh Redis connection...");
            }

            var redisConnected = await _redisManager.InitializeAsync(CancellationToken.None);
            if (!redisConnected)
            {
                Logger?.LogWarning("Mesh Redis connection not established - service will operate in degraded mode");
            }
        }

        // Call base to resolve service and call IBannouService.OnStartAsync
        return await base.OnStartAsync();
    }
}
