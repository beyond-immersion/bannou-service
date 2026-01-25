using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.Events;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Text.Json;

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
        // Service registration - services publish their API permissions on startup
        eventConsumer.RegisterHandler<IPermissionService, ServiceRegistrationEvent>(
            "permission.service-registered",
            async (svc, evt) => await ((PermissionService)svc).HandleServiceRegistrationAsync(evt));

        // Session state changes from services (e.g., game-session state transitions)
        eventConsumer.RegisterHandler<IPermissionService, SessionStateChangeEvent>(
            "permission.session-state-changed",
            async (svc, evt) => await ((PermissionService)svc).HandleSessionStateChangeAsync(evt));

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

    #region Service Registration Handler

    /// <summary>
    /// Handles service registration events from other services.
    /// Builds permission matrix from endpoint data and registers with the permission system.
    /// This is CRITICAL for the permission system to function - without this handler,
    /// no services will have their permissions registered.
    /// </summary>
    /// <param name="evt">The service registration event containing endpoints and permissions.</param>
    public async Task HandleServiceRegistrationAsync(ServiceRegistrationEvent evt)
    {
        try
        {
            _logger.LogDebug("Processing service registration for {ServiceName} version {Version} with {EndpointCount} endpoints",
                evt.ServiceName, evt.Version, evt.Endpoints?.Count ?? 0);

            if (evt.Endpoints == null || evt.Endpoints.Count == 0)
            {
                _logger.LogWarning("Service {ServiceName} has no endpoints to register", evt.ServiceName);
                return;
            }

            // Build State -> Role -> Methods mapping from endpoint permissions
            var permissionMatrix = new Dictionary<string, StatePermissions>();

            foreach (var endpoint in evt.Endpoints)
            {
                var path = endpoint.Path;
                var method = endpoint.Method.ToString().ToUpperInvariant();
                var methodSignature = $"{method}:{path}";

                // Process each permission requirement for this endpoint
                foreach (var permission in endpoint.Permissions ?? new List<PermissionRequirement>())
                {
                    var role = permission.Role ?? "user";
                    var requiredStates = permission.RequiredStates ?? new Dictionary<string, string>();

                    // Extract all required states for this permission
                    var stateKeys = new List<string>();
                    foreach (var stateEntry in requiredStates)
                    {
                        var stateServiceId = stateEntry.Key;
                        var requiredState = stateEntry.Value;

                        // Create state key: either just the state name, or service:state if from another service
                        var stateKey = stateServiceId == evt.ServiceName ? requiredState : $"{stateServiceId}:{requiredState}";
                        if (!string.IsNullOrEmpty(stateKey))
                        {
                            stateKeys.Add(stateKey);
                        }
                    }

                    // If no specific states required, use "default" state key
                    // This matches the generated BuildPermissionMatrix() behavior which uses "default"
                    // when permission.RequiredStates.Count == 0
                    if (stateKeys.Count == 0)
                    {
                        stateKeys.Add("default");
                    }

                    // Add method to each required state/role combination
                    foreach (var stateKey in stateKeys)
                    {
                        if (!permissionMatrix.ContainsKey(stateKey))
                        {
                            permissionMatrix[stateKey] = new StatePermissions();
                        }

                        if (!permissionMatrix[stateKey].ContainsKey(role))
                        {
                            permissionMatrix[stateKey][role] = new Collection<string>();
                        }

                        // Add the method if not already present
                        if (!permissionMatrix[stateKey][role].Contains(methodSignature))
                        {
                            permissionMatrix[stateKey][role].Add(methodSignature);
                        }
                    }
                }
            }

            // Create the ServicePermissionMatrix
            var servicePermissionMatrix = new ServicePermissionMatrix
            {
                ServiceId = evt.ServiceName,
                Version = evt.Version ?? "",
                Permissions = permissionMatrix
            };

            _logger.LogDebug("Built permission matrix for {ServiceName}: {StateCount} states, {MethodCount} total methods",
                evt.ServiceName, permissionMatrix.Count,
                permissionMatrix.Values.SelectMany(sp => sp.Values).SelectMany(methods => methods).Count());

            // Register the permissions
            var result = await RegisterServicePermissionsAsync(servicePermissionMatrix);

            if (result.Item1 == StatusCodes.OK)
            {
                _logger.LogDebug("Successfully registered permissions for service {ServiceName}", evt.ServiceName);
            }
            else
            {
                _logger.LogError("Failed to register permissions for service {ServiceName}: {StatusCode}",
                    evt.ServiceName, result.Item1);
                await PublishErrorEventAsync("HandleServiceRegistration", "registration_failed", $"Failed to register permissions: {result.Item1}", details: new { evt.ServiceName, StatusCode = result.Item1 });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling service registration event for {ServiceName}", evt.ServiceName);
            await PublishErrorEventAsync("HandleServiceRegistration", ex.GetType().Name, ex.Message, details: new { evt.ServiceName });
        }
    }

    #endregion

    #region Session State Change Handler

    /// <summary>
    /// Handles session state change events from other services.
    /// Triggers permission recompilation when a session's state changes.
    /// </summary>
    /// <param name="evt">The session state change event.</param>
    public async Task HandleSessionStateChangeAsync(SessionStateChangeEvent evt)
    {
        try
        {
            _logger.LogDebug("Received session state change event for {SessionId}: {ServiceId} → {NewState}",
                evt.SessionId, evt.ServiceId, evt.NewState);

            if (evt.SessionId == Guid.Empty || string.IsNullOrEmpty(evt.ServiceId) || string.IsNullOrEmpty(evt.NewState))
            {
                _logger.LogWarning("Invalid session state change event - missing required fields");
                return;
            }

            var stateUpdate = new SessionStateUpdate
            {
                SessionId = evt.SessionId,
                ServiceId = evt.ServiceId,
                NewState = evt.NewState,
                PreviousState = evt.PreviousState
            };

            await UpdateSessionStateAsync(stateUpdate);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling session state change event for {SessionId}", evt.SessionId);
            await PublishErrorEventAsync("HandleSessionStateChange", ex.GetType().Name, ex.Message, details: new { evt.SessionId, evt.ServiceId });
        }
    }

    #endregion

    #region Session Updated Handler (from Auth service)

    /// <summary>
    /// Handles session updated events from the Auth service.
    /// Updates role and authorization states to trigger permission recompilation.
    /// </summary>
    /// <param name="evt">The session updated event.</param>
    public async Task HandleSessionUpdatedAsync(SessionUpdatedEvent evt)
    {
        try
        {
            _logger.LogDebug("Processing session.updated event for SessionId: {SessionId}, Reason: {Reason}, Roles: [{Roles}], Authorizations: [{Authorizations}]",
                evt.SessionId,
                evt.Reason,
                string.Join(", ", evt.Roles ?? new List<string>()),
                string.Join(", ", evt.Authorizations ?? new List<string>()));

            // Determine the highest role from the roles array
            // Priority: admin > developer > user
            var role = DetermineHighestRoleFromEvent(evt.Roles);

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

    /// <summary>
    /// Determines the highest priority role from a list of roles.
    /// Priority: admin > developer > user > anonymous
    /// </summary>
    private static string DetermineHighestRoleFromEvent(IEnumerable<string>? roles)
    {
        if (roles == null || !roles.Any())
        {
            return "user"; // Default role
        }

        // Check for highest priority roles first
        if (roles.Contains("admin", StringComparer.OrdinalIgnoreCase))
        {
            return "admin";
        }

        if (roles.Contains("developer", StringComparer.OrdinalIgnoreCase))
        {
            return "developer";
        }

        if (roles.Contains("user", StringComparer.OrdinalIgnoreCase))
        {
            return "user";
        }

        // If no recognized role, return the first one or default to user
        return roles.FirstOrDefault() ?? "user";
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
