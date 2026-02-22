using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Permission;

/// <summary>
/// Partial class for PermissionService event handling.
/// Contains event consumer registration and handler implementations for all pub/sub events.
/// </summary>
public partial class PermissionService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        // Session updates from Auth service (role/authorization changes)
        eventConsumer.RegisterHandler<IPermissionService, SessionUpdatedEvent>(
            "session.updated",
            async (svc, evt) => await ((PermissionService)svc).HandleSessionUpdatedAsync(evt));

        // Session connected - triggers initial capability delivery
        eventConsumer.RegisterHandler<IPermissionService, SessionConnectedEvent>(
            "session.connected",
            async (svc, evt) => await ((PermissionService)svc).HandleSessionConnectedEventAsync(evt));

        // Session disconnected - removes from activeConnections
        eventConsumer.RegisterHandler<IPermissionService, SessionDisconnectedEvent>(
            "session.disconnected",
            async (svc, evt) => await ((PermissionService)svc).HandleSessionDisconnectedEventAsync(evt));
    }

    #region Session Updated Handler (from Auth service)

    /// <summary>
    /// Handles session updated events from the Auth service.
    /// Updates role and authorization states to trigger permission recompilation.
    /// </summary>
    /// <param name="evt">The session updated event.</param>
    public async Task HandleSessionUpdatedAsync(SessionUpdatedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.permission", "PermissionService.HandleSessionUpdated");

        try
        {
            _logger.LogDebug("Processing session.updated event for SessionId: {SessionId}, Reason: {Reason}, Roles: [{Roles}], Authorizations: [{Authorizations}]",
                evt.SessionId,
                evt.Reason,
                string.Join(", ", evt.Roles ?? new List<string>()),
                string.Join(", ", evt.Authorizations ?? new List<string>()));

            // Determine the highest role from the roles array using configured hierarchy
            var role = DetermineHighestPriorityRole(evt.Roles);

            // Update session role
            var roleUpdate = new SessionRoleUpdate
            {
                SessionId = evt.SessionId,
                NewRole = role
            };

            var roleResult = await UpdateSessionRoleAsync(roleUpdate);
            if (roleResult.Item1 != StatusCodes.OK)
            {
                _logger.LogWarning("Failed to update session role for {SessionId}: {StatusCode}",
                    evt.SessionId, roleResult.Item1);
            }
            else
            {
                _logger.LogDebug("Updated session role to '{Role}' for {SessionId}",
                    role, evt.SessionId);
            }

            // Update authorization states
            // Authorization strings are in format "{stubName}:{state}" (e.g., "my-game:authorized")
            foreach (var auth in evt.Authorizations ?? new List<string>())
            {
                var parts = auth.Split(':');
                if (parts.Length == 2)
                {
                    var serviceId = parts[0];  // stubName serves as serviceId for authorization
                    var state = parts[1];

                    var stateUpdate = new SessionStateUpdate
                    {
                        SessionId = evt.SessionId,
                        ServiceId = serviceId,
                        NewState = state
                    };

                    var stateResult = await UpdateSessionStateAsync(stateUpdate);
                    if (stateResult.Item1 != StatusCodes.OK)
                    {
                        _logger.LogWarning("Failed to update session state for {SessionId}, service {ServiceId}: {StatusCode}",
                            evt.SessionId, serviceId, stateResult.Item1);
                    }
                    else
                    {
                        _logger.LogDebug("Updated session state to '{State}' for service '{ServiceId}' on session {SessionId}",
                            state, serviceId, evt.SessionId);
                    }
                }
                else
                {
                    _logger.LogWarning("Invalid authorization format: '{Authorization}', expected 'stubName:state'",
                        auth);
                }
            }

            _logger.LogDebug("Successfully processed session.updated for SessionId: {SessionId}",
                evt.SessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process session.updated event for {SessionId}", evt.SessionId);
            await PublishErrorEventAsync("HandleSessionUpdated", ex.GetType().Name, ex.Message, details: new { evt.SessionId });
        }
    }

    #endregion

    #region Session Connected/Disconnected Handlers

    /// <summary>
    /// Handles session.connected events from Connect service.
    /// Adds session to activeConnections and triggers initial capability delivery.
    /// </summary>
    /// <param name="evt">The session connected event.</param>
    public async Task HandleSessionConnectedEventAsync(SessionConnectedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.permission", "PermissionService.HandleSessionConnectedEvent");

        try
        {
            _logger.LogDebug("Processing session.connected for SessionId: {SessionId}, AccountId: {AccountId}, Roles: {RoleCount}, Authorizations: {AuthCount}",
                evt.SessionId,
                evt.AccountId,
                evt.Roles?.Count ?? 0,
                evt.Authorizations?.Count ?? 0);

            // Delegate to the service method that handles the business logic
            var result = await HandleSessionConnectedAsync(
                evt.SessionId.ToString(),
                evt.AccountId.ToString(),
                evt.Roles,
                evt.Authorizations);

            if (result.Item1 != StatusCodes.OK)
            {
                _logger.LogWarning("Failed to handle session connected for {SessionId}: {StatusCode}",
                    evt.SessionId, result.Item1);
            }
            else
            {
                _logger.LogDebug("Successfully processed session.connected for SessionId: {SessionId}",
                    evt.SessionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process session.connected event for {SessionId}", evt.SessionId);
            await PublishErrorEventAsync("HandleSessionConnectedEvent", ex.GetType().Name, ex.Message, details: new { evt.SessionId });
        }
    }

    /// <summary>
    /// Handles session.disconnected events from Connect service.
    /// Removes session from activeConnections to prevent publishing to non-existent exchanges.
    /// </summary>
    /// <param name="evt">The session disconnected event.</param>
    public async Task HandleSessionDisconnectedEventAsync(SessionDisconnectedEvent evt)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.permission", "PermissionService.HandleSessionDisconnectedEvent");

        try
        {
            _logger.LogDebug("Processing session.disconnected for SessionId: {SessionId}, Reason: {Reason}, Reconnectable: {Reconnectable}",
                evt.SessionId,
                evt.Reason,
                evt.Reconnectable);

            // Delegate to the service method that handles the business logic
            var result = await HandleSessionDisconnectedAsync(
                evt.SessionId.ToString(),
                evt.Reconnectable);

            if (result.Item1 != StatusCodes.OK)
            {
                _logger.LogWarning("Failed to handle session disconnected for {SessionId}: {StatusCode}",
                    evt.SessionId, result.Item1);
            }
            else
            {
                _logger.LogDebug("Successfully processed session.disconnected for SessionId: {SessionId}",
                    evt.SessionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process session.disconnected event for {SessionId}", evt.SessionId);
            await PublishErrorEventAsync("HandleSessionDisconnectedEvent", ex.GetType().Name, ex.Message, details: new { evt.SessionId });
        }
    }

    #endregion
}
