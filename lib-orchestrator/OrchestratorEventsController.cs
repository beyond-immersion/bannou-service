using BeyondImmersion.BannouService.Events;
using Dapr;
using LibOrchestrator;
using Microsoft.AspNetCore.Mvc;

namespace BeyondImmersion.BannouService.Controllers;

/// <summary>
/// Dapr pub/sub handlers for orchestrator events.
/// Receives service heartbeats from all bannou instances for health monitoring.
/// Note: Orchestrator PUBLISHES full service mappings, it does not consume them.
/// </summary>
[ApiController]
[Route("[controller]")]
public class OrchestratorEventsController : ControllerBase
{
    private readonly IOrchestratorEventManager _eventManager;

    public OrchestratorEventsController(IOrchestratorEventManager eventManager)
    {
        _eventManager = eventManager;
    }

    /// <summary>
    /// Receives service heartbeats from all bannou instances.
    /// Forwarded to ServiceHealthMonitor for aggregation and health tracking.
    /// </summary>
    [Topic("bannou-pubsub", "bannou-service-heartbeats")]
    [HttpPost("heartbeats")]
    public IActionResult HandleHeartbeat([FromBody] ServiceHeartbeatEvent heartbeat)
    {
        _eventManager.ReceiveHeartbeat(heartbeat);
        return Ok();
    }
}
