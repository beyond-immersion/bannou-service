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

    public override void ConfigureServices(IServiceCollection services)
    {
        Logger?.LogDebug("Configuring service dependencies");

        // Register MeshRedisManager as Singleton (direct Redis connection for service discovery)
        // This avoids circular dependencies since Mesh IS the service discovery layer
        services.AddSingleton<IMeshRedisManager, MeshRedisManager>();

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
        _redisManager = ServiceProvider?.GetService<IMeshRedisManager>();
        if (_redisManager != null)
        {
            Logger?.LogInformation("Initializing Mesh Redis connection...");
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
