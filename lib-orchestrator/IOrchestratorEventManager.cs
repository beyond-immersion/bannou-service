using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Orchestrator;
using ServiceMappingAction = BeyondImmersion.BannouService.Events.ServiceMappingEventAction;

namespace LibOrchestrator;

/// <summary>
/// Orchestrator event contract using Dapr pub/sub for all publishes and receives.
/// </summary>
public interface IOrchestratorEventManager : IAsyncDisposable, IDisposable
{
    /// <summary>Raised when a heartbeat event is received from pub/sub.</summary>
    event Action<ServiceHeartbeatEvent>? HeartbeatReceived;

    /// <summary>Raised when a service mapping event is received from pub/sub.</summary>
    event Action<ServiceMappingEvent>? ServiceMappingReceived;

    /// <summary>Handle an incoming heartbeat (invoked by Dapr Topic handler).</summary>
    void ReceiveHeartbeat(ServiceHeartbeatEvent heartbeat);

    /// <summary>Handle an incoming service mapping (invoked by Dapr Topic handler).</summary>
    void ReceiveServiceMapping(ServiceMappingEvent mappingEvent);

    /// <summary>Publish a service restart event via Dapr pub/sub.</summary>
    Task PublishServiceRestartEventAsync(ServiceRestartEvent restartEvent);

    /// <summary>Publish a deployment event via Dapr pub/sub.</summary>
    Task PublishDeploymentEventAsync(DeploymentEvent deploymentEvent);
}
