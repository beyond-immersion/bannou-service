using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Connect;

/// <summary>
/// Partial class for ConnectService event handling.
/// Contains event consumer registration and handler implementations.
/// </summary>
public partial class ConnectService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        // Session invalidation events from Auth service
        // When sessions are invalidated (logout, account deletion, security revocation),
        // we need to disconnect affected WebSocket clients
        eventConsumer.RegisterHandler<IConnectService, SessionInvalidatedEvent>(
            "session.invalidated",
            async (svc, evt) => await ((ConnectService)svc).HandleSessionInvalidatedAsync(evt));
    }

    /// <summary>
    /// Handles session.invalidated events from the Auth service.
    /// Disconnects affected WebSocket clients when sessions are invalidated.
    /// </summary>
    /// <param name="evt">The session invalidated event.</param>
    public async Task HandleSessionInvalidatedAsync(SessionInvalidatedEvent evt)
    {
        var sessionIds = evt.SessionIds?.ToList() ?? new List<string>();
        var reason = evt.Reason.ToString();
        var disconnectClients = evt.DisconnectClients;

        _logger.LogInformation(
            "Processing session invalidation: {SessionCount} sessions, reason: {Reason}, disconnect: {Disconnect}",
            sessionIds.Count, reason, disconnectClients);

        if (!disconnectClients)
        {
            _logger.LogInformation("Session invalidation event received but disconnectClients=false, skipping");
            return;
        }

        // Disconnect affected sessions
        foreach (var sessionId in sessionIds)
        {
            await DisconnectSessionAsync(sessionId, reason ?? "session_invalidated");
        }

        _logger.LogInformation("Disconnected {SessionCount} sessions due to invalidation (reason: {Reason})",
            sessionIds.Count, reason);
    }
}
