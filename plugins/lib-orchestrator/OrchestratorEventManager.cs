using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Orchestrator;
using BeyondImmersion.BannouService.Providers;
using BeyondImmersion.BannouService.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace LibOrchestrator;

/// <summary>
/// Event manager for orchestrator events using native messaging infrastructure.
/// </summary>
[BannouHelperService("orchestrator-event", typeof(IOrchestratorService), typeof(IOrchestratorEventManager), lifetime: ServiceLifetime.Singleton)]
public class OrchestratorEventManager : IOrchestratorEventManager
{
    private readonly ILogger<OrchestratorEventManager> _logger;
    private readonly IMessageBus _messageBus;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly IEnumerable<IServiceMappingReceiver> _mappingReceivers;

    public event Action<ServiceHeartbeatEvent>? HeartbeatReceived;

    private const string HEARTBEAT_TOPIC = "bannou.service-heartbeat";
    private const string RESTART_TOPIC = "bannou.service-lifecycle";
    private const string DEPLOYMENT_TOPIC = "bannou.deployment-events";

    public OrchestratorEventManager(
        ILogger<OrchestratorEventManager> logger,
        IMessageBus messageBus,
        ITelemetryProvider telemetryProvider,
        IEnumerable<IServiceMappingReceiver> mappingReceivers)
    {
        _logger = logger;
        _messageBus = messageBus;
        ArgumentNullException.ThrowIfNull(telemetryProvider, nameof(telemetryProvider));
        _telemetryProvider = telemetryProvider;
        _mappingReceivers = mappingReceivers;
    }

    public void ReceiveHeartbeat(ServiceHeartbeatEvent heartbeat)
    {
        try
        {
            HeartbeatReceived?.Invoke(heartbeat);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling heartbeat event");
        }
    }

    public async Task PublishServiceRestartEventAsync(ServiceRestartEvent restartEvent)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorEventManager.PublishServiceRestartEventAsync");
        await _messageBus.PublishServiceRestartAsync(restartEvent);
        _logger.LogInformation("Published service restart event for {Service}", restartEvent.ServiceName);
    }

    public async Task PublishDeploymentEventAsync(DeploymentEvent deploymentEvent)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorEventManager.PublishDeploymentEventAsync");
        await _messageBus.PublishDeploymentAsync(deploymentEvent);
        _logger.LogInformation("Published deployment event: {Action} ({DeploymentId})", deploymentEvent.Action, deploymentEvent.DeploymentId);
    }

    public async Task PublishFullMappingsAsync(FullServiceMappingsEvent mappingsEvent)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.orchestrator", "OrchestratorEventManager.PublishFullMappingsAsync");

        // Push mappings via DI interface instead of publishing events directly.
        // Per FOUNDATION TENETS (Cross-Service Communication Discipline): Orchestrator (L3)
        // pushes into Mesh (L0) via DI interface. Mesh's implementation updates the local
        // resolver and broadcasts mesh.mappings.updated (L0→L0) for cross-node sync.
        var mappings = mappingsEvent.Mappings?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
            ?? new Dictionary<string, string>();
        var defaultAppId = mappingsEvent.DefaultAppId ?? "bannou";

        var receiverCount = 0;
        foreach (var receiver in _mappingReceivers)
        {
            try
            {
                await receiver.UpdateMappingsAsync(
                    mappings,
                    defaultAppId,
                    mappingsEvent.Version);
                receiverCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to push mappings to {ReceiverType}", receiver.GetType().Name);
            }
        }

        if (receiverCount == 0)
        {
            _logger.LogWarning(
                "No IServiceMappingReceiver implementations found — mappings v{Version} not applied. Is lib-mesh loaded?",
                mappingsEvent.Version);
        }
        else
        {
            _logger.LogInformation(
                "Pushed service mappings v{Version} with {Count} services to {ReceiverCount} receiver(s)",
                mappingsEvent.Version,
                mappingsEvent.TotalServices,
                receiverCount);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Task.CompletedTask;
    }
    public void Dispose() { }
}
