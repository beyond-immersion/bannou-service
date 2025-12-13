using BeyondImmersion.BannouService.Auth;
using BeyondImmersion.BannouService.Events;
using Dapr;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace BeyondImmersion.BannouService.Permissions;

/// <summary>
/// Controller for handling Dapr pubsub events related to permissions.
/// This controller is separate from the generated PermissionsController to allow
/// Dapr subscription discovery while maintaining schema-first architecture.
/// </summary>
[ApiController]
[Route("[controller]")]
public class PermissionsEventsController : ControllerBase
{
    private readonly ILogger<PermissionsEventsController> _logger;
    private readonly IPermissionsService _permissionsService;

    public PermissionsEventsController(
        ILogger<PermissionsEventsController> logger,
        IPermissionsService permissionsService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _permissionsService = permissionsService ?? throw new ArgumentNullException(nameof(permissionsService));
    }

    /// <summary>
    /// Handle service registration events from other services.
    /// Called by Dapr when a message is published to "permissions.service-registered" topic.
    /// </summary>
    [Topic("bannou-pubsub", "permissions.service-registered")]
    [HttpPost("handle-service-registration")]
    public async Task<IActionResult> HandleServiceRegistrationAsync()
    {
        try
        {
            // Read and parse event using shared helper (handles both CloudEvents and raw formats)
            var actualEventData = await DaprEventHelper.ReadEventJsonAsync(Request);

            if (actualEventData == null || actualEventData.Value.ValueKind != JsonValueKind.Object)
            {
                _logger.LogError("Received empty or invalid service registration event");
                return BadRequest(new { error = "Invalid event data" });
            }

            // Parse the ServiceRegistrationEvent with explicit validation
            if (!actualEventData.Value.TryGetProperty("serviceId", out var serviceIdElement))
            {
                _logger.LogError("Missing 'serviceId' property in event data");
                return BadRequest(new { error = "Missing required property: serviceId" });
            }
            var serviceId = serviceIdElement.GetString();

            if (!actualEventData.Value.TryGetProperty("version", out var versionElement))
            {
                _logger.LogError("Missing 'version' property in event data");
                return BadRequest(new { error = "Missing required property: version" });
            }
            var version = versionElement.GetString();

            if (!actualEventData.Value.TryGetProperty("endpoints", out var endpoints))
            {
                _logger.LogError("Missing 'endpoints' property in event data");
                return BadRequest(new { error = "Missing required property: endpoints" });
            }

            _logger.LogInformation("Processing service registration for {ServiceId} version {Version} with {EndpointCount} endpoints",
                serviceId, version, endpoints.GetArrayLength());

            // Build State -> Role -> Methods mapping from endpoint permissions
            var permissionMatrix = new Dictionary<string, StatePermissions>();

            foreach (var endpointElement in endpoints.EnumerateArray())
            {
                var path = endpointElement.GetProperty("path").GetString();
                // Method can be either a string ("GET") or a number (0 = GET, 1 = POST, etc.)
                var methodElement = endpointElement.GetProperty("method");
                string? method;
                if (methodElement.ValueKind == JsonValueKind.Number)
                {
                    // Convert numeric method to string (HttpMethodTypes enum values)
                    var methodIndex = methodElement.GetInt32();
                    method = methodIndex switch
                    {
                        0 => "GET",
                        1 => "POST",
                        2 => "PUT",
                        3 => "DELETE",
                        4 => "HEAD",
                        5 => "PATCH",
                        6 => "OPTIONS",
                        _ => $"UNKNOWN_{methodIndex}"
                    };
                }
                else
                {
                    method = methodElement.GetString();
                }
                var permissions = endpointElement.GetProperty("permissions");

                var methodSignature = $"{method}:{path}";

                // Process each permission requirement for this endpoint
                foreach (var permissionElement in permissions.EnumerateArray())
                {
                    var role = permissionElement.GetProperty("role").GetString();
                    var requiredStates = permissionElement.GetProperty("requiredStates");

                    // Extract all required states for this permission
                    var stateKeys = new List<string>();
                    foreach (var stateProperty in requiredStates.EnumerateObject())
                    {
                        var stateServiceId = stateProperty.Name;
                        var requiredState = stateProperty.Value.GetString();

                        // Create state key: either just the state name, or service:state if from another service
                        var stateKey = stateServiceId == serviceId ? requiredState : $"{stateServiceId}:{requiredState}";
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

                        if (!permissionMatrix[stateKey].ContainsKey(role ?? "user"))
                        {
                            permissionMatrix[stateKey][role ?? "user"] = new Collection<string>();
                        }

                        // Add the method if not already present
                        if (!permissionMatrix[stateKey][role ?? "user"].Contains(methodSignature))
                        {
                            permissionMatrix[stateKey][role ?? "user"].Add(methodSignature);
                        }
                    }
                }
            }

            // Create the ServicePermissionMatrix
            var servicePermissionMatrix = new ServicePermissionMatrix
            {
                ServiceId = serviceId ?? "",
                Version = version ?? "",
                Permissions = permissionMatrix
            };

            _logger.LogInformation("Built permission matrix for {ServiceId}: {StateCount} states, {MethodCount} total methods",
                serviceId, permissionMatrix.Count,
                permissionMatrix.Values.SelectMany(sp => sp.Values).SelectMany(methods => methods).Count());

            // Register the permissions using the service
            var result = await _permissionsService.RegisterServicePermissionsAsync(servicePermissionMatrix);

            if (result.Item1 == StatusCodes.OK)
            {
                _logger.LogInformation("Successfully registered permissions for service {ServiceId}", serviceId);
                return Ok();
            }
            else
            {
                _logger.LogError("Failed to register permissions for service {ServiceId}: {StatusCode}",
                    serviceId, result.Item1);
                return StatusCode((int)result.Item1);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling service registration event. Exception: {ExceptionMessage}",
                ex.Message);
            return StatusCode(500, new { error = ex.Message, stackTrace = ex.StackTrace });
        }
    }

    /// <summary>
    /// Handle session state change events from other services.
    /// Called by Dapr when a message is published to "permissions.session-state-changed" topic.
    /// </summary>
    [Topic("bannou-pubsub", "permissions.session-state-changed")]
    [HttpPost("handle-session-state-change")]
    public async Task<IActionResult> HandleSessionStateChangeAsync()
    {
        try
        {
            // Read and parse event using shared helper (handles both CloudEvents and raw formats)
            var actualEventData = await DaprEventHelper.ReadEventJsonAsync(Request);

            if (actualEventData == null || actualEventData.Value.ValueKind != JsonValueKind.Object)
            {
                _logger.LogError("Received empty or invalid session state change event");
                return BadRequest(new { error = "Invalid event data" });
            }

            // Parse the event data with explicit validation
            if (!actualEventData.Value.TryGetProperty("sessionId", out var sessionIdElement))
            {
                _logger.LogError("Missing 'sessionId' property in event data");
                return BadRequest(new { error = "Missing required property: sessionId" });
            }
            var sessionId = sessionIdElement.GetString();

            if (!actualEventData.Value.TryGetProperty("serviceId", out var serviceIdElement))
            {
                _logger.LogError("Missing 'serviceId' property in event data");
                return BadRequest(new { error = "Missing required property: serviceId" });
            }
            var serviceId = serviceIdElement.GetString();

            if (!actualEventData.Value.TryGetProperty("newState", out var newStateElement))
            {
                _logger.LogError("Missing 'newState' property in event data");
                return BadRequest(new { error = "Missing required property: newState" });
            }
            var newState = newStateElement.GetString();

            var previousState = actualEventData.Value.TryGetProperty("previousState", out var prevProp) ?
                prevProp.GetString() : null;

            _logger.LogInformation("Received session state change event for {SessionId}: {ServiceId} → {NewState}",
                sessionId, serviceId, newState);

            if (sessionId == null || serviceId == null || newState == null)
            {
                _logger.LogWarning("Invalid session state change event - missing required fields");
                return BadRequest("Missing required fields");
            }

            var stateUpdate = new SessionStateUpdate
            {
                SessionId = sessionId,
                ServiceId = serviceId,
                NewState = newState,
                PreviousState = previousState
            };

            await _permissionsService.UpdateSessionStateAsync(stateUpdate);

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling session state change event. Exception: {ExceptionMessage}",
                ex.Message);
            return StatusCode(500, new { error = ex.Message, stackTrace = ex.StackTrace });
        }
    }

    /// <summary>
    /// Handle session updated events from the Auth service.
    /// Called by Dapr when a session's roles or authorizations change.
    /// Updates role and authorization states to trigger permission recompilation.
    /// </summary>
    [Topic("bannou-pubsub", "session.updated")]
    [HttpPost("handle-session-updated")]
    public async Task<IActionResult> HandleSessionUpdatedAsync()
    {
        try
        {
            // Read and parse event using shared helper (handles both CloudEvents and raw formats)
            var sessionUpdatedEvent = await DaprEventHelper.ReadEventAsync<SessionUpdatedEvent>(Request);

            if (sessionUpdatedEvent == null)
            {
                _logger.LogWarning("[PERM-EVENT] Failed to parse SessionUpdatedEvent from request body");
                return BadRequest("Invalid event data");
            }

            _logger.LogInformation("[PERM-EVENT] Processing session.updated event for SessionId: {SessionId}, Reason: {Reason}, Roles: [{Roles}], Authorizations: [{Authorizations}]",
                sessionUpdatedEvent.SessionId,
                sessionUpdatedEvent.Reason,
                string.Join(", ", sessionUpdatedEvent.Roles ?? new List<string>()),
                string.Join(", ", sessionUpdatedEvent.Authorizations ?? new List<string>()));

            // Determine the highest role from the roles array
            // Priority: admin > developer > user
            var role = DetermineHighestRole(sessionUpdatedEvent.Roles);

            // Update session role
            var roleUpdate = new SessionRoleUpdate
            {
                SessionId = sessionUpdatedEvent.SessionId,
                NewRole = role
            };

            var roleResult = await _permissionsService.UpdateSessionRoleAsync(roleUpdate);
            if (roleResult.Item1 != StatusCodes.OK)
            {
                _logger.LogWarning("[PERM-EVENT] Failed to update session role for {SessionId}: {StatusCode}",
                    sessionUpdatedEvent.SessionId, roleResult.Item1);
            }
            else
            {
                _logger.LogDebug("[PERM-EVENT] Updated session role to '{Role}' for {SessionId}",
                    role, sessionUpdatedEvent.SessionId);
            }

            // Update authorization states
            // Authorization strings are in format "{stubName}:{state}" (e.g., "arcadia:authorized")
            foreach (var auth in sessionUpdatedEvent.Authorizations ?? new List<string>())
            {
                var parts = auth.Split(':');
                if (parts.Length == 2)
                {
                    var serviceId = parts[0];  // stubName serves as serviceId for authorization
                    var state = parts[1];

                    var stateUpdate = new SessionStateUpdate
                    {
                        SessionId = sessionUpdatedEvent.SessionId,
                        ServiceId = serviceId,
                        NewState = state
                    };

                    var stateResult = await _permissionsService.UpdateSessionStateAsync(stateUpdate);
                    if (stateResult.Item1 != StatusCodes.OK)
                    {
                        _logger.LogWarning("[PERM-EVENT] Failed to update session state for {SessionId}, service {ServiceId}: {StatusCode}",
                            sessionUpdatedEvent.SessionId, serviceId, stateResult.Item1);
                    }
                    else
                    {
                        _logger.LogDebug("[PERM-EVENT] Updated session state to '{State}' for service '{ServiceId}' on session {SessionId}",
                            state, serviceId, sessionUpdatedEvent.SessionId);
                    }
                }
                else
                {
                    _logger.LogWarning("[PERM-EVENT] Invalid authorization format: '{Authorization}', expected 'stubName:state'",
                        auth);
                }
            }

            _logger.LogInformation("[PERM-EVENT] Successfully processed session.updated for SessionId: {SessionId}",
                sessionUpdatedEvent.SessionId);

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PERM-EVENT] Failed to process session.updated event");
            return StatusCode(500, "Internal server error processing event");
        }
    }

    /// <summary>
    /// Handle session connected events from the Connect service.
    /// Called by Dapr when a WebSocket connection is fully established.
    /// This event ensures the RabbitMQ exchange exists before publishing to session-specific topics.
    /// Adds session to activeConnections and triggers initial capability delivery.
    /// </summary>
    [Topic("bannou-pubsub", "session.connected")]
    [HttpPost("handle-session-connected")]
    public async Task<IActionResult> HandleSessionConnectedAsync()
    {
        try
        {
            // Read and parse event using shared helper (handles both CloudEvents and raw formats)
            var sessionConnectedEvent = await DaprEventHelper.ReadEventAsync<SessionConnectedEvent>(Request);

            if (sessionConnectedEvent == null)
            {
                _logger.LogWarning("[PERM-EVENT] Failed to parse SessionConnectedEvent from request body");
                return BadRequest("Invalid event data");
            }

            _logger.LogInformation("[PERM-EVENT] Processing session.connected for SessionId: {SessionId}, AccountId: {AccountId}, Roles: {RoleCount}, Authorizations: {AuthCount}",
                sessionConnectedEvent.SessionId,
                sessionConnectedEvent.AccountId,
                sessionConnectedEvent.Roles?.Count ?? 0,
                sessionConnectedEvent.Authorizations?.Count ?? 0);

            // Register the session as connected and trigger capability delivery
            // This ensures capabilities are only published after the RabbitMQ exchange exists
            // Roles and authorizations enable capability compilation without API calls from Connect
            var result = await _permissionsService.HandleSessionConnectedAsync(
                sessionConnectedEvent.SessionId,
                sessionConnectedEvent.AccountId,
                sessionConnectedEvent.Roles,
                sessionConnectedEvent.Authorizations);

            if (result.Item1 != StatusCodes.OK)
            {
                _logger.LogWarning("[PERM-EVENT] Failed to handle session connected for {SessionId}: {StatusCode}",
                    sessionConnectedEvent.SessionId, result.Item1);
                return StatusCode((int)result.Item1);
            }

            _logger.LogInformation("[PERM-EVENT] Successfully processed session.connected for SessionId: {SessionId}",
                sessionConnectedEvent.SessionId);

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PERM-EVENT] Failed to process session.connected event");
            return StatusCode(500, "Internal server error processing event");
        }
    }

    /// <summary>
    /// Handle session disconnected events from the Connect service.
    /// Called by Dapr when a WebSocket connection is closed.
    /// Removes session from activeConnections to prevent publishing to non-existent exchanges.
    /// </summary>
    [Topic("bannou-pubsub", "session.disconnected")]
    [HttpPost("handle-session-disconnected")]
    public async Task<IActionResult> HandleSessionDisconnectedAsync()
    {
        try
        {
            // Read and parse event using shared helper (handles both CloudEvents and raw formats)
            var sessionDisconnectedEvent = await DaprEventHelper.ReadEventAsync<SessionDisconnectedEvent>(Request);

            if (sessionDisconnectedEvent == null)
            {
                _logger.LogWarning("[PERM-EVENT] Failed to parse SessionDisconnectedEvent from request body");
                return BadRequest("Invalid event data");
            }

            _logger.LogInformation("[PERM-EVENT] Processing session.disconnected for SessionId: {SessionId}, Reason: {Reason}, Reconnectable: {Reconnectable}",
                sessionDisconnectedEvent.SessionId,
                sessionDisconnectedEvent.Reason,
                sessionDisconnectedEvent.Reconnectable);

            // Remove the session from active connections
            var result = await _permissionsService.HandleSessionDisconnectedAsync(
                sessionDisconnectedEvent.SessionId,
                sessionDisconnectedEvent.Reconnectable);

            if (result.Item1 != StatusCodes.OK)
            {
                _logger.LogWarning("[PERM-EVENT] Failed to handle session disconnected for {SessionId}: {StatusCode}",
                    sessionDisconnectedEvent.SessionId, result.Item1);
                return StatusCode((int)result.Item1);
            }

            _logger.LogInformation("[PERM-EVENT] Successfully processed session.disconnected for SessionId: {SessionId}",
                sessionDisconnectedEvent.SessionId);

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PERM-EVENT] Failed to process session.disconnected event");
            return StatusCode(500, "Internal server error processing event");
        }
    }

    /// <summary>
    /// Determines the highest priority role from a list of roles.
    /// Priority: admin > developer > user > anonymous
    /// </summary>
    private static string DetermineHighestRole(IEnumerable<string>? roles)
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
}
