using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Orchestrator;
using Dapr.Client;
using Microsoft.Extensions.Logging;

namespace LibOrchestrator;

/// <summary>
/// Dapr-based event manager for orchestrator events.
/// </summary>
public class OrchestratorEventManager : IOrchestratorEventManager
{
    private readonly ILogger<OrchestratorEventManager> _logger;
    private readonly DaprClient _daprClient;

    public event Action<ServiceHeartbeatEvent>? HeartbeatReceived;

    private const string PUBSUB_NAME = "bannou-pubsub";
    private const string HEARTBEAT_TOPIC = "bannou-service-heartbeats";
    private const string FULL_MAPPINGS_TOPIC = "bannou-full-service-mappings";
    private const string RESTART_TOPIC = "bannou-service-restart";
    private const string DEPLOYMENT_TOPIC = "bannou-deployment-events";

    public OrchestratorEventManager(
        ILogger<OrchestratorEventManager> logger,
        DaprClient daprClient)
    {
        _logger = logger;
        _daprClient = daprClient;
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
        await _daprClient.PublishEventAsync(PUBSUB_NAME, RESTART_TOPIC, restartEvent);
        _logger.LogInformation("Published service restart event for {Service}", restartEvent.ServiceName);
    }

    public async Task PublishDeploymentEventAsync(DeploymentEvent deploymentEvent)
    {
        await _daprClient.PublishEventAsync(PUBSUB_NAME, DEPLOYMENT_TOPIC, deploymentEvent);
        _logger.LogInformation("Published deployment event: {Action} ({DeploymentId})", deploymentEvent.Action, deploymentEvent.DeploymentId);
    }

    public async Task PublishFullMappingsAsync(FullServiceMappingsEvent mappingsEvent)
    {
        await _daprClient.PublishEventAsync(PUBSUB_NAME, FULL_MAPPINGS_TOPIC, mappingsEvent);
        _logger.LogInformation(
            "Published full service mappings v{Version} with {Count} services",
            mappingsEvent.Version,
            mappingsEvent.TotalServices);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public void Dispose() { }
}
