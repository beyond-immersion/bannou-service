using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.Logging;

namespace BeyondImmersion.BannouService.Permission;

/// <summary>
/// Partial class for PermissionService event handling.
/// Contains event consumer registration and handler implementations for pub/sub events.
/// </summary>
/// <remarks>
/// <para>
/// <b>Note:</b> Session connected and disconnected events from Connect are now handled
/// via the <see cref="PermissionSessionActivityListener"/> DI listener pattern instead
/// of event subscriptions. This is more efficient since Connect and Permission are both
/// L1 AppFoundation (always co-located). Only the <c>session.updated</c> subscription
/// (from Auth service) remains here because Auth is a separate service that may publish
/// from any node.
/// </para>
/// </remarks>
public partial class PermissionService
{
    /// <summary>
    /// Registers event consumers for pub/sub events this service handles.
    /// Called from the main service constructor.
    /// </summary>
    /// <param name="eventConsumer">The event consumer for registering handlers.</param>
    protected void RegisterEventConsumers(IEventConsumer eventConsumer)
    {
        // Session updates from Auth service (role/authorization changes).
        // Retained as event subscription: Auth is a separate L1 service that may
        // publish from any node. DI listener only covers Connect-originated events.
        eventConsumer.RegisterHandler<IPermissionService, SessionUpdatedEvent>(
            "session.updated",
            async (svc, evt) => await ((PermissionService)svc).HandleSessionUpdatedAsync(evt));
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
}
