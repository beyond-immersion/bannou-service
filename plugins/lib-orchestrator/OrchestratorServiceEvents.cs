using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Orchestrator;

/// <summary>
/// Partial class for OrchestratorService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
public partial class OrchestratorService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        eventConsumer.RegisterHandler<IOrchestratorService, ServiceHeartbeatEvent>(
            "bannou-service-heartbeats",
            async (svc, evt) => await ((OrchestratorService)svc).HandleServiceHeartbeatAsync(evt));

    }

    /// <summary>
    /// Handles service heartbeat events from RabbitMQ.
    /// Routes heartbeats to the OrchestratorEventManager which notifies subscribers
    /// (ServiceHealthMonitor) to update routing tables and deployment status.
    /// </summary>
    /// <param name="evt">The service heartbeat event data.</param>
    public async Task HandleServiceHeartbeatAsync(ServiceHeartbeatEvent evt)
    {
        _logger.LogDebug(
            "Received heartbeat from {AppId} (ServiceId: {ServiceId}, Status: {Status})",
            evt.AppId, evt.ServiceId, evt.Status);

        // Route to event manager which raises HeartbeatReceived event
        // ServiceHealthMonitor subscribes to this for deployment validation
        _eventManager.ReceiveHeartbeat(evt);

        await Task.CompletedTask;
    }
}
