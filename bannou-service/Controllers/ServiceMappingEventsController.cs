using BeyondImmersion.BannouService.ServiceClients;
using Dapr;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Controllers;

/// <summary>
/// Action types for service mapping events.
/// Defined locally to avoid circular dependency with lib-orchestrator.
/// Must match LibOrchestrator.ServiceMappingAction enum values.
/// </summary>
public enum ServiceMappingAction
{
    /// <inheritdoc/>
    Register = 0,
    /// <inheritdoc/>
    Update = 1,
    /// <inheritdoc/>
    Unregister = 2
}

/// <summary>
/// Event model for service mapping updates.
/// Defined locally to avoid circular dependency with lib-orchestrator.
/// Must match LibOrchestrator.ServiceMappingEvent structure.
/// </summary>
public class ServiceMappingEventModel
{
    /// <inheritdoc/>
    public string EventId { get; set; } = string.Empty;
    /// <inheritdoc/>
    public DateTime Timestamp { get; set; }
    /// <inheritdoc/>
    public string ServiceName { get; set; } = string.Empty;
    /// <inheritdoc/>
    public string AppId { get; set; } = string.Empty;
    /// <inheritdoc/>
    public ServiceMappingAction Action { get; set; }
}

/// <summary>
/// Handles service mapping events from orchestrator.
/// Updates the ServiceAppMappingResolver to enable dynamic service routing.
/// This controller subscribes to the bannou-service-mappings topic via Dapr pub/sub.
/// </summary>
[ApiController]
[Route("[controller]")]
public class ServiceMappingEventsController : ControllerBase
{
    private readonly IServiceAppMappingResolver _resolver;
    private readonly ILogger<ServiceMappingEventsController> _logger;

    // Debug counter to track received events
    private static int _eventsReceived = 0;
    private static DateTime _lastEventTime = DateTime.MinValue;

    /// <inheritdoc/>
    public ServiceMappingEventsController(
        IServiceAppMappingResolver resolver,
        ILogger<ServiceMappingEventsController> logger)
    {
        _resolver = resolver;
        _logger = logger;
    }

    /// <summary>
    /// Handles service mapping events from the orchestrator.
    /// Updates the local ServiceAppMappingResolver to route service calls to the correct app-id.
    /// </summary>
    [Topic("bannou-pubsub", "bannou-service-mappings")]
    [HttpPost("handle")]
    public IActionResult HandleServiceMappingEvent([FromBody] ServiceMappingEventModel eventData)
    {
        // Track all incoming events for debugging
        _eventsReceived++;
        _lastEventTime = DateTime.UtcNow;
        _logger.LogInformation("ServiceMappingEventsController.HandleServiceMappingEvent called (total: {Count})", _eventsReceived);

        if (eventData == null)
        {
            _logger.LogWarning("Received null service mapping event");
            return BadRequest("Event data is required");
        }

        if (string.IsNullOrWhiteSpace(eventData.ServiceName))
        {
            _logger.LogWarning("Received service mapping event with empty service name");
            return BadRequest("Service name is required");
        }

        _logger.LogInformation(
            "Processing service mapping event: {ServiceName} -> {AppId} ({Action})",
            eventData.ServiceName, eventData.AppId, eventData.Action);

        try
        {
            switch (eventData.Action)
            {
                case ServiceMappingAction.Register:
                case ServiceMappingAction.Update:
                    if (string.IsNullOrWhiteSpace(eventData.AppId))
                    {
                        _logger.LogWarning(
                            "Received {Action} event with empty app-id for service {ServiceName}",
                            eventData.Action, eventData.ServiceName);
                        return BadRequest("App-id is required for Register/Update actions");
                    }
                    _resolver.UpdateServiceMapping(eventData.ServiceName, eventData.AppId);
                    _logger.LogInformation(
                        "Updated service mapping: {ServiceName} -> {AppId}",
                        eventData.ServiceName, eventData.AppId);
                    break;

                case ServiceMappingAction.Unregister:
                    _resolver.RemoveServiceMapping(eventData.ServiceName);
                    _logger.LogInformation(
                        "Removed service mapping: {ServiceName} (reverted to default)",
                        eventData.ServiceName);
                    break;

                default:
                    _logger.LogWarning(
                        "Unknown service mapping action: {Action}",
                        eventData.Action);
                    return BadRequest($"Unknown action: {eventData.Action}");
            }

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing service mapping event for {ServiceName}", eventData.ServiceName);
            return StatusCode(500, "Internal error processing service mapping event");
        }
    }

    /// <summary>
    /// Debug endpoint to view current service mappings and event statistics.
    /// </summary>
    [HttpGet("mappings")]
    public IActionResult GetCurrentMappings()
    {
        var mappings = _resolver.GetAllMappings();
        return Ok(new
        {
            DefaultAppId = "bannou",
            CustomMappings = mappings,
            MappingCount = mappings.Count,
            EventsReceived = _eventsReceived,
            LastEventTime = _lastEventTime == DateTime.MinValue ? "never" : _lastEventTime.ToString("o")
        });
    }

    /// <summary>
    /// Simple health check endpoint to verify the controller is reachable.
    /// </summary>
    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new
        {
            Status = "healthy",
            Controller = "ServiceMappingEventsController",
            EventsReceived = _eventsReceived
        });
    }
}
