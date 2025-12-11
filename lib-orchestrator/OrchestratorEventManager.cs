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
    public event Action<ServiceMappingEvent>? ServiceMappingReceived;

    private const string PUBSUB_NAME = "bannou-pubsub";
    private const string HEARTBEAT_TOPIC = "bannou-service-heartbeats";
    private const string MAPPINGS_TOPIC = "bannou-service-mappings";
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

    public void ReceiveServiceMapping(ServiceMappingEvent mappingEvent)
    {
        try
        {
            ServiceMappingReceived?.Invoke(mappingEvent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling service mapping event");
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

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public void Dispose() { }
}
