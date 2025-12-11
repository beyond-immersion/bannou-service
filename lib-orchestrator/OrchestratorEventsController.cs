using BeyondImmersion.BannouService.Events;
using Dapr;
using LibOrchestrator;
using Microsoft.AspNetCore.Mvc;

namespace BeyondImmersion.BannouService.Controllers;

/// <summary>
/// Dapr pub/sub handlers for orchestrator events (heartbeats, service mappings).
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

    [Topic("bannou-pubsub", "bannou-service-heartbeats")]
    [HttpPost("heartbeats")]
    public IActionResult HandleHeartbeat([FromBody] ServiceHeartbeatEvent heartbeat)
    {
        _eventManager.ReceiveHeartbeat(heartbeat);
        return Ok();
    }

    [Topic("bannou-pubsub", "bannou-service-mappings")]
    [HttpPost("service-mappings")]
    public IActionResult HandleServiceMapping([FromBody] ServiceMappingEvent mappingEvent)
    {
        _eventManager.ReceiveServiceMapping(mappingEvent);
        return Ok();
    }
}
