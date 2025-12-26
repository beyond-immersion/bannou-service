using BeyondImmersion.BannouService.Configuration;
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
    private MeshServiceConfiguration? _cachedConfig;

    public override void ConfigureServices(IServiceCollection services)
    {
        Logger?.LogDebug("Configuring mesh service dependencies");

        // Get configuration to check for local routing mode
        // Cache the provider to avoid multiple builds and ensure consistent config
        var tempProvider = services.BuildServiceProvider();
        _cachedConfig = tempProvider.GetService<MeshServiceConfiguration>();
        var config = _cachedConfig;
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
        // Uses IMeshRedisManager directly (NOT IMeshClient) to avoid circular dependency:
        // - All generated clients (AccountsClient, etc.) need IMeshInvocationClient
        // - If MeshInvocationClient needed IMeshClient, and MeshClient needs IMeshInvocationClient = deadlock
        services.AddSingleton<IMeshInvocationClient>(sp =>
        {
            var redisManager = sp.GetRequiredService<IMeshRedisManager>();
            var logger = sp.GetRequiredService<ILogger<MeshInvocationClient>>();
            return new MeshInvocationClient(redisManager, logger);
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
            else if (!_useLocalRouting)
            {
                // Register this instance as a mesh endpoint so other services can discover us
                await RegisterMeshEndpointAsync();
            }
        }

        // Call base to resolve service and call IBannouService.OnStartAsync
        return await base.OnStartAsync();
    }

    /// <summary>
    /// Registers this bannou instance as a mesh endpoint for service discovery.
    /// Other services (like http-tester) can then discover and route to this instance.
    /// </summary>
    private async Task RegisterMeshEndpointAsync()
    {
        if (_redisManager == null)
        {
            Logger?.LogWarning("Cannot register mesh endpoint - Redis manager not initialized");
            return;
        }

        // Get app configuration for app-id
        var appConfig = ServiceProvider?.GetService<AppConfiguration>();
        var appId = appConfig?.BannouAppId ?? "bannou";

        // Determine host and port from environment or defaults
        // In Docker Compose, the service name is the hostname
        var endpointHost = Environment.GetEnvironmentVariable("MESH_ENDPOINT_HOST")
            ?? appConfig?.BannouAppId
            ?? "bannou";
        var endpointPort = int.TryParse(
            Environment.GetEnvironmentVariable("MESH_ENDPOINT_PORT"), out var port)
            ? port : 80;

        // Generate a unique instance ID for this endpoint
        var instanceId = Guid.NewGuid();

        var endpoint = new MeshEndpoint
        {
            InstanceId = instanceId,
            AppId = appId,
            Host = endpointHost,
            Port = endpointPort,
            Status = EndpointStatus.Healthy,
            Services = new List<string> { "mesh" }, // Could expand to list all enabled services
            LoadPercent = 0,
            CurrentConnections = 0,
            RegisteredAt = DateTimeOffset.UtcNow,
            LastSeen = DateTimeOffset.UtcNow
        };

        var registered = await _redisManager.RegisterEndpointAsync(endpoint, 90);
        if (registered)
        {
            Logger?.LogInformation(
                "Registered mesh endpoint: {AppId} at {Host}:{Port} (instance {InstanceId})",
                appId, endpointHost, endpointPort, instanceId);
        }
        else
        {
            Logger?.LogWarning("Failed to register mesh endpoint for {AppId}", appId);
        }
    }
}
