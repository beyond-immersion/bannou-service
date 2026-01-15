using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Connect.Protocol;
using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.Logging;
using System.Text;

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

        // Service error events from any service
        // Forward to all connected admin clients for real-time monitoring
        eventConsumer.RegisterHandler<IConnectService, ServiceErrorEvent>(
            "service.error",
            async (svc, evt) => await ((ConnectService)svc).HandleServiceErrorAsync(evt));
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

    /// <summary>
    /// Handles service.error events from any service.
    /// Forwards the error event to all connected admin WebSocket clients for real-time monitoring.
    /// </summary>
    /// <param name="evt">The service error event.</param>
    public async Task HandleServiceErrorAsync(ServiceErrorEvent evt)
    {
        var adminCount = _connectionManager.GetAdminConnectionCount();
        if (adminCount == 0)
        {
            _logger.LogDebug(
                "Service error event received from {ServiceId}/{Operation} but no admin clients connected",
                evt.ServiceId, evt.Operation);
            return;
        }

        _logger.LogInformation(
            "Forwarding service error from {ServiceId}/{Operation} ({ErrorType}: {Message}) to {AdminCount} admin client(s)",
            evt.ServiceId, evt.Operation, evt.ErrorType, evt.Message, adminCount);

        try
        {
            // Create a client event payload for admin notification
            var adminNotification = new
            {
                type = "service_error",
                eventId = evt.EventId,
                timestamp = evt.Timestamp,
                serviceId = evt.ServiceId,
                appId = evt.AppId,
                operation = evt.Operation,
                errorType = evt.ErrorType,
                message = evt.Message,
                severity = evt.Severity.ToString().ToLowerInvariant(),
                dependency = evt.Dependency,
                endpoint = evt.Endpoint,
                correlationId = evt.CorrelationId
            };

            // Use BannouJson for consistent serialization (IMPLEMENTATION TENETS)
            var payloadJson = BannouJson.Serialize(adminNotification);
            var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);

            // Create binary message with Event flag for push notification
            // Use a well-known GUID for "system/admin-notification" channel
            var adminNotificationGuid = GuidGenerator.GenerateServiceGuid(
                "system",
                "admin-notification",
                _serverSalt);

            var message = new BinaryMessage(
                flags: MessageFlags.Event, // This is a push notification, not a request
                channel: 0, // System channel
                sequenceNumber: 0, // Events don't need sequence tracking
                serviceGuid: adminNotificationGuid,
                messageId: (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                payload: payloadBytes);

            var sentCount = await _connectionManager.SendToAdminsAsync(message);
            _logger.LogDebug("Sent service error notification to {SentCount}/{AdminCount} admin clients",
                sentCount, adminCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to forward service error event to admin clients");
        }
    }
}
