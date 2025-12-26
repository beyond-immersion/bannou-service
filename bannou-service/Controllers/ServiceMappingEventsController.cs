using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Controllers;

/// <summary>
/// Handles full service mapping events from Orchestrator.
/// Atomically updates the ServiceAppMappingResolver with complete routing state.
/// This controller subscribes to the bannou-full-service-mappings topic via Bannou pub/sub.
/// </summary>
/// <remarks>
/// Architecture:
/// - Orchestrator is the single source of truth for service-to-app-id mappings
/// - Orchestrator publishes FullServiceMappingsEvent every 30 seconds AND on routing changes
/// - All bannou instances consume this event to atomically update their local routing tables
/// - Version checking prevents out-of-order event application
/// </remarks>
[ApiController]
[Route("[controller]")]
public class ServiceMappingEventsController : ControllerBase
{
    private readonly IServiceAppMappingResolver _resolver;
    private readonly ILogger<ServiceMappingEventsController> _logger;

    // Debug counter to track received events
    private static int _eventsReceived = 0;
    private static int _eventsApplied = 0;
    private static int _eventsRejected = 0;
    private static DateTimeOffset _lastEventTime = DateTimeOffset.MinValue;

    /// <inheritdoc/>
    public ServiceMappingEventsController(
        IServiceAppMappingResolver resolver,
        ILogger<ServiceMappingEventsController> logger)
    {
        _resolver = resolver;
        _logger = logger;
    }

    /// <summary>
    /// Handles full service mappings events from the Orchestrator.
    /// Atomically replaces all local service mappings with the complete state from Orchestrator.
    /// Implements version checking to reject out-of-order events.
    /// </summary>
    [Topic("bannou-pubsub", "bannou-full-service-mappings")]
    [HttpPost("handle")]
    public IActionResult HandleFullServiceMappings([FromBody] FullServiceMappingsEvent eventData)
    {
        // Track all incoming events for debugging
        _eventsReceived++;
        _lastEventTime = DateTimeOffset.UtcNow;

        if (eventData == null)
        {
            _logger.LogWarning("Received null full service mappings event");
            return BadRequest("Event data is required");
        }

        if (eventData.Mappings == null)
        {
            _logger.LogWarning("Received full service mappings event with null mappings dictionary");
            return BadRequest("Mappings dictionary is required");
        }

        _logger.LogDebug(
            "Received full service mappings v{Version} with {Count} services from {Source}",
            eventData.Version,
            eventData.TotalServices,
            eventData.SourceInstanceId);

        try
        {
            // Atomically replace all mappings (with version checking)
            var applied = _resolver.ReplaceAllMappings(
                (IReadOnlyDictionary<string, string>)eventData.Mappings,
                eventData.DefaultAppId ?? "bannou",
                eventData.Version);

            if (applied)
            {
                _eventsApplied++;
                _logger.LogInformation(
                    "Applied full service mappings v{Version}: {Count} services (total applied: {TotalApplied})",
                    eventData.Version, eventData.TotalServices, _eventsApplied);
            }
            else
            {
                _eventsRejected++;
                _logger.LogDebug(
                    "Rejected stale full service mappings v{Version} (current: v{CurrentVersion}, total rejected: {TotalRejected})",
                    eventData.Version, _resolver.CurrentVersion, _eventsRejected);
            }

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing full service mappings event v{Version}", eventData.Version);
            return StatusCode(500, "Internal error processing full service mappings event");
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
            CurrentVersion = _resolver.CurrentVersion,
            CustomMappings = mappings,
            MappingCount = mappings.Count,
            EventsReceived = _eventsReceived,
            EventsApplied = _eventsApplied,
            EventsRejected = _eventsRejected,
            LastEventTime = _lastEventTime == DateTimeOffset.MinValue ? "never" : _lastEventTime.ToString("o")
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
            CurrentVersion = _resolver.CurrentVersion,
            EventsReceived = _eventsReceived,
            EventsApplied = _eventsApplied
        });
    }
}
