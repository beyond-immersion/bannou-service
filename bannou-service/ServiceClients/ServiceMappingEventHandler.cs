using Dapr;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace BeyondImmersion.BannouService.ServiceClients;

/// <summary>
/// Handles service mapping events from RabbitMQ via Dapr pub/sub.
/// Updates the dynamic service-to-app-id mapping based on service discovery events.
/// </summary>
[ApiController]
[Route("api/events/service-mapping")]
public class ServiceMappingEventHandler : ControllerBase
{
    private readonly IServiceAppMappingResolver _mappingResolver;
    private readonly IServiceMappingEventDispatcher _eventDispatcher;
    private readonly ILogger<ServiceMappingEventHandler> _logger;

    /// <inheritdoc/>
    public ServiceMappingEventHandler(
        IServiceAppMappingResolver mappingResolver,
        IServiceMappingEventDispatcher eventDispatcher,
        ILogger<ServiceMappingEventHandler> logger)
    {
        _mappingResolver = mappingResolver;
        _eventDispatcher = eventDispatcher;
        _logger = logger;
    }

    /// <summary>
    /// Handles service mapping events from the bannou-service-mappings topic.
    /// These events update the dynamic routing table for distributed services.
    /// </summary>
    [Topic("bannou-pubsub", "bannou-service-mappings")]
    [HttpPost("handle")]
    public async Task<IActionResult> HandleServiceMappingEventAsync([FromBody] ServiceMappingEvent eventData)
    {
        try
        {
            _logger.LogDebug("Received service mapping event {EventId}: {Action} {ServiceName} -> {AppId}",
                eventData.EventId, eventData.Action, eventData.ServiceName, eventData.AppId);

            // First, update the core mapping resolver
            switch (eventData.Action?.ToLowerInvariant())
            {
                case "register":
                case "update":
                    _mappingResolver.UpdateServiceMapping(eventData.ServiceName, eventData.AppId);
                    break;

                case "unregister":
                    _mappingResolver.RemoveServiceMapping(eventData.ServiceName);
                    break;

                default:
                    _logger.LogWarning("Unknown service mapping action: {Action}", eventData.Action);
                    return BadRequest($"Unknown action: {eventData.Action}");
            }

            // Then, dispatch to custom handlers
            await _eventDispatcher.DispatchEventAsync(eventData);

            _logger.LogInformation("Successfully processed service mapping event {EventId}: {Action} {ServiceName}",
                eventData.EventId, eventData.Action, eventData.ServiceName);

            return Ok(new { status = "processed", eventId = eventData.EventId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process service mapping event {EventId}",
                eventData.EventId);
            return StatusCode(500, new { error = "Internal server error", eventId = eventData.EventId });
        }
    }

    /// <summary>
    /// Health check endpoint for service mapping event handling.
    /// </summary>
    [HttpGet("health")]
    public IActionResult GetHealth()
    {
        var mappings = _mappingResolver.GetAllMappings();
        return Ok(new
        {
            status = "healthy",
            mappingCount = mappings.Count,
            mappings = mappings
        });
    }
}

/// <summary>
/// Service mapping event data model.
/// Represents changes in service-to-app-id mappings for distributed deployment.
/// </summary>
public class ServiceMappingEvent
{
    /// <summary>
    /// Unique identifier for this event.
    /// </summary>
    public string EventId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// When the event occurred.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Name of the service (e.g., "accounts", "character-agent").
    /// </summary>
    public string ServiceName { get; set; } = "";

    /// <summary>
    /// Dapr app-id where the service is running.
    /// </summary>
    public string AppId { get; set; } = "";

    /// <summary>
    /// Type of mapping change: register, update, unregister.
    /// </summary>
    public string Action { get; set; } = "";

    /// <summary>
    /// Additional service metadata (optional).
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }
}
