using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Orchestrator.Events;

[ApiController]
[Route("events/service-error")]
public class ServiceErrorEventsController : ControllerBase
{
    private readonly ILogger<ServiceErrorEventsController> _logger;

    public ServiceErrorEventsController(ILogger<ServiceErrorEventsController> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Listener for service.error events. Currently logs and stubs reaction paths.
    /// </summary>
    [HttpPost("handle")]
    [Topic("bannou-pubsub", "service.error")]
    public IActionResult HandleServiceError([FromBody] ServiceErrorEvent evt)
    {
        if (evt == null)
        {
            return BadRequest();
        }

        _logger.LogWarning(
            "Received service error event from {ServiceId} ({Dependency}) op={Operation} type={ErrorType} severity={Severity}",
            evt.ServiceId,
            evt.Dependency ?? "unknown",
            evt.Operation,
            evt.ErrorType,
            evt.Severity);

        switch (evt.Dependency?.ToLowerInvariant())
        {
            case "redis":
                // TODO: orchestrator response for redis issues (restart redis/notify)
                break;
            case "dapr-pubsub":
            case "pubsub":
                // TODO: orchestrator response for pubsub issues
                break;
            case "placement":
                // TODO: orchestrator response for placement failures
                break;
            default:
                // TODO: catch-all orchestrator handling
                break;
        }

        return Ok();
    }
}
