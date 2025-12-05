using BeyondImmersion.BannouService.Auth;
using Dapr;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Text.Json;

namespace BeyondImmersion.BannouService.Connect;

/// <summary>
/// Controller for handling Dapr pubsub events related to the Connect service.
/// This controller is separate from the generated ConnectController to allow
/// Dapr subscription discovery while maintaining schema-first architecture.
/// </summary>
[ApiController]
[Route("[controller]")]
public class ConnectEventsController : ControllerBase
{
    private readonly ILogger<ConnectEventsController> _logger;
    private readonly IConnectService _connectService;

    public ConnectEventsController(
        ILogger<ConnectEventsController> logger,
        IConnectService connectService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _connectService = connectService ?? throw new ArgumentNullException(nameof(connectService));
    }

    /// <summary>
    /// Handle capability update events from the Permissions service.
    /// Called by Dapr when permissions change and connected clients need updates.
    /// </summary>
    [Topic("bannou-pubsub", "permissions.capabilities-updated")]
    [HttpPost("handle-capabilities-updated")]
    public async Task<IActionResult> HandleCapabilitiesUpdatedAsync()
    {
        try
        {
            // Read and parse event using shared helper (handles both CloudEvents and raw formats)
            var actualEventData = await DaprEventHelper.ReadEventJsonAsync(Request);

            if (actualEventData == null)
            {
                _logger.LogWarning("Received empty or invalid capabilities-updated event");
                return Ok(); // Don't fail - just ignore empty/invalid events
            }

            // Extract affected sessions and service ID
            string? serviceId = null;
            var affectedSessions = new List<string>();

            if (actualEventData.Value.TryGetProperty("serviceId", out var serviceIdElement))
            {
                serviceId = serviceIdElement.GetString();
            }

            if (actualEventData.Value.TryGetProperty("affectedSessions", out var sessionsElement) &&
                sessionsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var sessionElement in sessionsElement.EnumerateArray())
                {
                    var sessionId = sessionElement.GetString();
                    if (!string.IsNullOrEmpty(sessionId))
                    {
                        affectedSessions.Add(sessionId);
                    }
                }
            }

            _logger.LogInformation(
                "Processing capabilities update for service {ServiceId}, {SessionCount} affected sessions",
                serviceId, affectedSessions.Count);

            // Cast to concrete service to call capability update method
            var connectService = _connectService as ConnectService;
            if (connectService == null)
            {
                _logger.LogWarning("Connect service implementation not available for capability updates");
                return Ok();
            }

            // Push updates to affected sessions, or all sessions if none specified
            if (affectedSessions.Count > 0)
            {
                foreach (var sessionId in affectedSessions)
                {
                    await connectService.PushCapabilityUpdateAsync(sessionId);
                }
            }
            else
            {
                // No specific sessions - push to all connected clients
                await connectService.PushCapabilityUpdateToAllAsync();
            }

            _logger.LogInformation("Capability updates pushed for service {ServiceId}", serviceId);
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling capabilities-updated event");
            return Ok(); // Don't fail Dapr retries - log and continue
        }
    }

    /// <summary>
    /// Handle session invalidation events from the Auth service.
    /// Called by Dapr when sessions are invalidated (logout, account deletion, security revocation).
    /// Disconnects affected WebSocket clients.
    /// </summary>
    [Topic("bannou-pubsub", "session.invalidated")]
    [HttpPost("handle-session-invalidated")]
    public async Task<IActionResult> HandleSessionInvalidatedAsync()
    {
        try
        {
            // Read and parse event using shared helper (handles both CloudEvents and raw formats)
            var actualEventData = await DaprEventHelper.ReadEventJsonAsync(Request);

            if (actualEventData == null)
            {
                _logger.LogWarning("Received empty or invalid session-invalidated event");
                return Ok(); // Don't fail - just ignore empty/invalid events
            }

            // Extract session IDs and reason
            var sessionIds = new List<string>();
            string? reason = null;
            bool disconnectClients = true;

            if (actualEventData.Value.TryGetProperty("sessionIds", out var sessionsElement) &&
                sessionsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var sessionElement in sessionsElement.EnumerateArray())
                {
                    var sessionId = sessionElement.GetString();
                    if (!string.IsNullOrEmpty(sessionId))
                    {
                        sessionIds.Add(sessionId);
                    }
                }
            }

            if (actualEventData.Value.TryGetProperty("reason", out var reasonElement))
            {
                reason = reasonElement.GetString();
            }

            if (actualEventData.Value.TryGetProperty("disconnectClients", out var disconnectElement))
            {
                disconnectClients = disconnectElement.GetBoolean();
            }

            _logger.LogInformation(
                "Processing session invalidation: {SessionCount} sessions, reason: {Reason}, disconnect: {Disconnect}",
                sessionIds.Count, reason, disconnectClients);

            if (!disconnectClients)
            {
                _logger.LogInformation("Session invalidation event received but disconnectClients=false, skipping");
                return Ok();
            }

            // Cast to concrete service to call disconnect method
            var connectService = _connectService as ConnectService;
            if (connectService == null)
            {
                _logger.LogWarning("Connect service implementation not available for session disconnection");
                return Ok();
            }

            // Disconnect affected sessions
            foreach (var sessionId in sessionIds)
            {
                await connectService.DisconnectSessionAsync(sessionId, reason ?? "session_invalidated");
            }

            _logger.LogInformation("Disconnected {SessionCount} sessions due to invalidation (reason: {Reason})",
                sessionIds.Count, reason);
            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling session-invalidated event");
            return Ok(); // Don't fail Dapr retries - log and continue
        }
    }
}
