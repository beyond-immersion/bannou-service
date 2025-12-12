using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Orchestrator;

namespace LibOrchestrator;

/// <summary>
/// Orchestrator event contract using Dapr pub/sub for all publishes and receives.
/// </summary>
public interface IOrchestratorEventManager : IAsyncDisposable, IDisposable
{
    /// <summary>Raised when a heartbeat event is received from pub/sub.</summary>
    event Action<ServiceHeartbeatEvent>? HeartbeatReceived;

    /// <summary>Handle an incoming heartbeat (invoked by Dapr Topic handler).</summary>
    void ReceiveHeartbeat(ServiceHeartbeatEvent heartbeat);

    /// <summary>Publish a service restart event via Dapr pub/sub.</summary>
    Task PublishServiceRestartEventAsync(ServiceRestartEvent restartEvent);

    /// <summary>Publish a deployment event via Dapr pub/sub.</summary>
    Task PublishDeploymentEventAsync(DeploymentEvent deploymentEvent);

    /// <summary>
    /// Publish the full service mappings event to all bannou instances.
    /// This is Orchestrator's "heartbeat" - the authoritative source of truth.
    /// </summary>
    Task PublishFullMappingsAsync(FullServiceMappingsEvent mappingsEvent);
}
