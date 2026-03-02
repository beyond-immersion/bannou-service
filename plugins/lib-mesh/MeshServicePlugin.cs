using BeyondImmersion.Bannou.Core;
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

    private IMeshStateManager? _stateManager;

    public override void ConfigureServices(IServiceCollection services)
    {
        Logger?.LogDebug("Configuring mesh service dependencies");

        // Register IMeshInstanceIdentifier - the canonical source of this node's identity.
        // Priority: MESH_INSTANCE_ID env var > --force-service-id CLI > random GUID.
        services.AddSingleton<IMeshInstanceIdentifier>(sp =>
        {
            var meshConfig = sp.GetRequiredService<MeshServiceConfiguration>();
            if (meshConfig.InstanceId.HasValue)
            {
                return new DefaultMeshInstanceIdentifier(meshConfig.InstanceId.Value);
            }

            var appConfig = sp.GetRequiredService<IServiceConfiguration>();
            return new DefaultMeshInstanceIdentifier(appConfig.ForceServiceId);
        });

        // Register IMeshStateManager via factory delegate that checks config at resolution time.
        // Avoids BuildServiceProvider anti-pattern - config is resolved when the singleton is
        // first requested, not during ConfigureServices. Follows established Actor/Asset plugin pattern.
        services.AddSingleton<IMeshStateManager>(sp =>
        {
            var config = sp.GetRequiredService<MeshServiceConfiguration>();
            if (config.UseLocalRouting)
            {
                sp.GetRequiredService<ILogger<MeshServicePlugin>>().LogWarning(
                    "Mesh using LOCAL ROUTING mode. All service calls will route locally (no state store)!");

                return new LocalMeshStateManager(
                    config,
                    sp.GetRequiredService<ILogger<LocalMeshStateManager>>(),
                    sp.GetRequiredService<IMeshInstanceIdentifier>());
            }

            return new MeshStateManager(
                sp.GetRequiredService<IStateStoreFactory>(),
                sp.GetRequiredService<ILogger<MeshStateManager>>(),
                sp.GetRequiredService<ITelemetryProvider>());
        });

        // Register active health checking background service
        services.AddHostedService<MeshHealthCheckService>();

        // Register the mesh invocation client for service-to-service calls
        // Uses IMeshStateManager directly (NOT IMeshClient) to avoid circular dependency:
        // - All generated clients (AccountClient, etc.) need IMeshInvocationClient
        // - If MeshInvocationClient needed IMeshClient, and MeshClient needs IMeshInvocationClient = deadlock
        // NullTelemetryProvider is registered by default; lib-telemetry overrides it when enabled
        // Distributed circuit breaker uses IStateStoreFactory + IMessageBus/IMessageSubscriber per IMPLEMENTATION TENETS
        services.AddSingleton<IMeshInvocationClient>(sp =>
        {
            var stateManager = sp.GetRequiredService<IMeshStateManager>();
            var stateStoreFactory = sp.GetRequiredService<IStateStoreFactory>();
            var messageBus = sp.GetRequiredService<IMessageBus>();
            var messageSubscriber = sp.GetRequiredService<IMessageSubscriber>();
            var configuration = sp.GetRequiredService<MeshServiceConfiguration>();
            var logger = sp.GetRequiredService<ILogger<MeshInvocationClient>>();
            var telemetryProvider = sp.GetRequiredService<ITelemetryProvider>();
            var instanceIdentifier = sp.GetRequiredService<IMeshInstanceIdentifier>();

            return new MeshInvocationClient(
                stateManager,
                stateStoreFactory,
                messageBus,
                messageSubscriber,
                configuration,
                logger,
                telemetryProvider,
                instanceIdentifier);
        });

        Logger?.LogDebug("Service dependencies configured");
    }

    protected override async Task<bool> OnStartAsync()
    {
        var serviceProvider = ServiceProvider ?? throw new InvalidOperationException("ServiceProvider not available during OnStartAsync");
        var meshConfig = serviceProvider.GetRequiredService<MeshServiceConfiguration>();

        Logger?.LogInformation("Starting Mesh service{Mode}", meshConfig.UseLocalRouting ? " (local routing)" : "");

        _stateManager = serviceProvider.GetRequiredService<IMeshStateManager>();

        if (!await _stateManager.InitializeAsync(CancellationToken.None))
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
        if (_stateManager == null)
        {
            Logger?.LogWarning("Cannot register mesh endpoint - state manager not initialized");
            return;
        }

        var serviceProvider = ServiceProvider ?? throw new InvalidOperationException("ServiceProvider not available during RegisterMeshEndpointAsync");

        // Get app configuration for app-id
        var appConfig = serviceProvider.GetRequiredService<AppConfiguration>();
        var meshConfig = serviceProvider.GetRequiredService<MeshServiceConfiguration>();
        var appId = appConfig.EffectiveAppId;

        // Endpoint host defaults to app-id for Docker Compose compatibility (hostname = service name)
        var endpointHost = meshConfig.EndpointHost ?? appConfig.EffectiveAppId;
        var endpointPort = meshConfig.EndpointPort > 0 ? meshConfig.EndpointPort : 80;

        // Use the registered IMeshInstanceIdentifier for consistent instance identification
        var instanceIdentifier = serviceProvider.GetRequiredService<IMeshInstanceIdentifier>();
        var instanceId = instanceIdentifier.InstanceId;

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

        var registered = await _stateManager.RegisterEndpointAsync(endpoint, meshConfig.EndpointTtlSeconds);
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
