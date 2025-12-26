using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.Events;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

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
    [Obsolete]
    private readonly IConnectService _connectService;

    [Obsolete]
    public ConnectEventsController(
        ILogger<ConnectEventsController> logger,
        IConnectService connectService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _connectService = connectService ?? throw new ArgumentNullException(nameof(connectService));
    }

    // NOTE: Capability updates use session-specific RabbitMQ channels via IClientEventPublisher.
    // Permissions service publishes SessionCapabilitiesEvent (with actual permissions data) to
    // CONNECT_SESSION_{sessionId} topics. Connect's ClientEventRabbitMQSubscriber receives these
    // and calls TryHandleInternalEventAsync, which processes capabilities via ProcessCapabilitiesAsync.
    // NO API calls from Connect to Permissions - all capability data flows via events.

    /// <summary>
    /// Handle session invalidation events from the Auth service.
    /// Called by Dapr when sessions are invalidated (logout, account deletion, security revocation).
    /// Disconnects affected WebSocket clients.
    /// </summary>
    [Topic("bannou-pubsub", "session.invalidated")]
    [HttpPost("handle-session-invalidated")]
    [Obsolete]
    public async Task<IActionResult> HandleSessionInvalidatedAsync()
    {
        try
        {
            // Read and parse event using typed model (handles both CloudEvents and raw formats)
            var evt = await DaprEventHelper.ReadEventAsync<SessionInvalidatedEvent>(Request);

            if (evt == null)
            {
                _logger.LogWarning("Failed to parse SessionInvalidatedEvent from request body");
                return Ok(); // Don't fail - just ignore empty/invalid events
            }

            var sessionIds = evt.SessionIds?.ToList() ?? new List<string>();
            var reason = evt.Reason.ToString();
            var disconnectClients = evt.DisconnectClients;

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
