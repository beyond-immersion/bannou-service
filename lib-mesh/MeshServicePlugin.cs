using BeyondImmersion.BannouService;
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
        // - All generated clients (AccountClient, etc.) need IMeshInvocationClient
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
        Logger?.LogInformation("Starting Mesh service{Mode}", _useLocalRouting ? " (local routing)" : "");

        var serviceProvider = ServiceProvider ?? throw new InvalidOperationException("ServiceProvider not available during OnStartAsync");
        _redisManager = serviceProvider.GetRequiredService<IMeshRedisManager>();

        if (!await _redisManager.InitializeAsync(CancellationToken.None))
        {
            Logger?.LogError("Mesh initialization failed");
            return false;
        }

        await RegisterMeshEndpointAsync();

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

        var serviceProvider = ServiceProvider ?? throw new InvalidOperationException("ServiceProvider not available during RegisterMeshEndpointAsync");

        // Get app configuration for app-id
        var appConfig = serviceProvider.GetRequiredService<AppConfiguration>();
        var meshConfig = _cachedConfig ?? serviceProvider.GetRequiredService<MeshServiceConfiguration>();
        var appId = appConfig.EffectiveAppId;

        // Endpoint host defaults to app-id for Docker Compose compatibility (hostname = service name)
        var endpointHost = meshConfig.EndpointHost ?? appConfig.EffectiveAppId;
        var endpointPort = meshConfig.EndpointPort > 0 ? meshConfig.EndpointPort : 80;

        // Use the shared Program.ServiceGUID for consistent instance identification
        // This ensures mesh endpoint and heartbeat use the same instance ID
        var instanceId = Guid.Parse(Program.ServiceGUID);

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
