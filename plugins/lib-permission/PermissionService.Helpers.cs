using BeyondImmersion.Bannou.Core;
using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
using BeyondImmersion.BannouService.Services;
using BeyondImmersion.BannouService.State;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Permission;

// =============================================================================
// PermissionService — Private & Internal Helper Methods
// =============================================================================
//
// This partial class file is the designated home for all private and internal
// helper methods used by PermissionService. This separation exists to
// support the IMPLEMENTATION TENETS telemetry span rules:
//
//   - PRIMARY FILE (PermissionService.cs):
//     Contains ONLY public interface method implementations (the methods
//     declared in IPermissionService). These methods MUST NOT call
//     ITelemetryProvider.StartActivity because the generated controller
//     already wraps each endpoint with a telemetry span. Adding a span
//     in the service method would double-instrument the endpoint.
//
//   - THIS FILE (PermissionService.Helpers.cs):
//     Contains all private/internal helper methods, core logic extracted
//     from endpoints, event publishing helpers, query builders, mapping
//     functions, and any other non-public methods. Every async method in
//     this file MUST call ITelemetryProvider.StartActivity to ensure
//     sub-operations are properly instrumented.
//
// Structural tests enforce both rules:
//   - Services_PrimaryFile_DoesNotCallStartActivity
//   - Services_HelperFiles_HaveStartActivityWhenAsync
//
// WHAT GOES HERE:
//   - Private async helper methods (with StartActivity spans)
//   - Private sync helper methods (query builders, mappers, validators)
//   - Internal static key builders (already in primary file by convention,
//     but may be moved here if the primary file is large)
//   - Event publishing helper methods
//   - Any extracted "core" logic (e.g., CreateAccountCoreAsync)
//
// WHAT STAYS IN THE PRIMARY FILE:
//   - Public interface method implementations (/// <inheritdoc/> methods)
//   - Constructor and field declarations
//   - Constants and key prefix definitions
//
// See: docs/reference/tenets/IMPLEMENTATION-BEHAVIOR.md (T30)
// See: docs/reference/HELPERS-AND-COMMON-PATTERNS.md
// =============================================================================

/// <summary>
/// Private and internal helper methods for PermissionService.
/// </summary>
/// <remarks>
/// <para>
/// This partial class contains all non-public helper methods. Every async method
/// in this file MUST include a <c>using var activity = _telemetryProvider.StartActivity(...)</c>
/// span per IMPLEMENTATION TENETS (T30). The generated controller instruments the
/// public interface methods; this file instruments the sub-operations.
/// </para>
/// </remarks>
public partial class PermissionService
{
    /// <summary>
    /// Recompile permissions for a session and publish update to Connect service.
    /// Reads session states from state store (used when states are not already in memory).
    /// </summary>
    private async Task<(bool PermissionsChanged, IDictionary<string, ICollection<string>>? NewPermissions)> RecompileSessionPermissionsAsync(
        string sessionId, CapabilityUpdateReason reason, CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.permission", "PermissionService.RecompileSessionPermissions");

        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
        var sessionStates = await _sessionStatesStore
            .GetAsync(statesKey, cancellationToken);

        if (sessionStates == null || sessionStates.Count == 0)
        {
            _logger.LogDebug("No session states found for {SessionId}, skipping recompilation", sessionId);
            return (false, null);
        }

        return await RecompileSessionPermissionsAsync(sessionId, sessionStates, reason, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Recompile permissions for a session using provided session states.
    /// This overload avoids read-after-write consistency issues by using states already in memory.
    /// </summary>
    /// <param name="sessionId">Session ID to recompile permissions for.</param>
    /// <param name="sessionStates">Session states dictionary (avoids re-reading from state store).</param>
    /// <param name="reason">Reason for recompilation.</param>
    /// <param name="skipActiveConnectionsCheck">Skip activeConnections check in PublishCapabilityUpdateAsync (used when session just added).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task<(bool PermissionsChanged, IDictionary<string, ICollection<string>>? NewPermissions)> RecompileSessionPermissionsAsync(
        string sessionId,
        Dictionary<string, string> sessionStates,
        CapabilityUpdateReason reason,
        bool skipActiveConnectionsCheck = false,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.permission", "PermissionService.RecompileSessionPermissionsWithStates");

        if (sessionStates == null)
        {
            _logger.LogDebug("No session states provided for {SessionId}, skipping recompilation", sessionId);
            return (false, null);
        }

        var defaultRole = _configuration.RoleHierarchy[0];
        var role = sessionStates.GetValueOrDefault(SESSION_ROLE_KEY, defaultRole);
        _logger.LogDebug("Recompiling permissions for session {SessionId}, role: {Role}, reason: {Reason}",
            sessionId, role, reason);

        var compiledPermissions = new Dictionary<string, HashSet<string>>();

        try
        {
            var registeredServices = await _cacheStore.GetSetAsync<string>(REGISTERED_SERVICES_KEY, cancellationToken);

            _logger.LogDebug("Found {Count} registered services: {Services}",
                registeredServices.Count, string.Join(", ", registeredServices));

            foreach (var serviceId in registeredServices)
            {
                var relevantStates = new List<string> { "default" };
                foreach (var serviceState in sessionStates.Where(s => s.Key != SESSION_ROLE_KEY))
                {
                    var stateServiceId = serviceState.Key;
                    var stateValue = serviceState.Value;
                    if (stateValue == "default") continue;
                    relevantStates.Add(stateServiceId == serviceId ? stateValue : $"{stateServiceId}:{stateValue}");
                }

                foreach (var stateKey in relevantStates)
                {
                    var maxRoleByEndpoint = new Dictionary<string, int>();

                    foreach (var roleName in _configuration.RoleHierarchy)
                    {
                        var matrixKey = string.Format(PERMISSION_MATRIX_KEY, serviceId, stateKey, roleName);
                        var endpoints = await _hashSetStore.GetAsync(matrixKey, cancellationToken);

                        _logger.LogDebug("State-based lookup: service={ServiceId}, stateKey={StateKey}, role={Role}, key={Key}, found={Count}",
                            serviceId, stateKey, roleName, matrixKey, endpoints?.Count ?? 0);

                        if (endpoints == null) continue;

                        foreach (var endpoint in endpoints)
                        {
                            var priority = Array.IndexOf(_configuration.RoleHierarchy, roleName);
                            maxRoleByEndpoint[endpoint] = maxRoleByEndpoint.TryGetValue(endpoint, out var existing)
                                ? Math.Max(existing, priority)
                                : priority;
                        }
                    }

                    var sessionPriority = Array.IndexOf(_configuration.RoleHierarchy, role);
                    foreach (var kvp in maxRoleByEndpoint)
                    {
                        if (sessionPriority < kvp.Value)
                        {
                            continue;
                        }

                        if (!compiledPermissions.ContainsKey(serviceId))
                        {
                            compiledPermissions[serviceId] = new HashSet<string>();
                        }

                        compiledPermissions[serviceId].Add(kvp.Key);
                    }
                }
            }

            // Store compiled permissions with version increment
            var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionId);
            var existingPermissions = await _permissionsStore.GetAsync(permissionsKey, cancellationToken) ?? new Dictionary<string, object>();

            var currentVersion = 0;
            if (existingPermissions.ContainsKey("version"))
            {
                int.TryParse(existingPermissions["version"]?.ToString(), out currentVersion);
            }

            var newVersion = currentVersion + 1;
            var newPermissionData = new Dictionary<string, object>();
            foreach (var kvp in compiledPermissions)
            {
                newPermissionData[kvp.Key] = kvp.Value.ToList();
            }
            newPermissionData["version"] = newVersion;
            newPermissionData["generated_at"] = DateTimeOffset.UtcNow.ToString("o");

            await _permissionsStore.SaveAsync(permissionsKey, newPermissionData, options: GetSessionDataStateOptions(), cancellationToken: cancellationToken);

            await PublishCapabilityUpdateAsync(
                sessionId,
                compiledPermissions.ToDictionary(k => k.Key, v => (IEnumerable<string>)v.Value.ToList()),
                newVersion,
                reason,
                skipActiveConnectionsCheck,
                cancellationToken);

            _logger.LogInformation("Recompiled permissions for session {SessionId}: {ServiceCount} services, version {Version}",
                sessionId, compiledPermissions.Count, newVersion);

            var resultPermissions = compiledPermissions.ToDictionary(
                k => k.Key,
                v => (ICollection<string>)v.Value.ToList());

            return (true, resultPermissions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to recompile permissions for session {SessionId}", sessionId);
            await _messageBus.TryPublishErrorAsync(
                "permission",
                "RecompileSessionPermissions",
                ex.GetType().Name,
                ex.Message,
                "state",
                sessionId,
                ServiceErrorEventSeverity.Error,
                cancellationToken: cancellationToken);
            return (false, null);
        }
    }

    /// <summary>
    /// Publish compiled capabilities directly to the session via session-specific RabbitMQ channel.
    /// Connect service receives this via ClientEventRabbitMQSubscriber, generates client-salted GUIDs,
    /// and sends CapabilityManifestClientEvent to the client.
    /// Only publishes to sessions in activeConnections to avoid RabbitMQ exchange not_found crashes,
    /// unless skipActiveConnectionsCheck is true.
    /// </summary>
    private async Task PublishCapabilityUpdateAsync(
        string sessionId,
        Dictionary<string, IEnumerable<string>> permissions,
        int version,
        CapabilityUpdateReason reason,
        bool skipActiveConnectionsCheck = false,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.permission", "PermissionService.PublishCapabilityUpdate");

        var sessionGuid = Guid.Parse(sessionId);
        var now = DateTimeOffset.UtcNow;

        var permissionsDict = permissions.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.ToList() as ICollection<string>);

        // Publish service event (broadcast to any subscriber, regardless of connection state)
        await _messageBus.PublishPermissionCapabilityUpdateAsync(new PermissionCapabilityUpdate
        {
            SessionId = sessionGuid,
            Version = version,
            UpdateType = CapabilityUpdateType.Full,
            FullCapabilities = permissionsDict,
            GeneratedAt = now,
            Reason = reason
        }, cancellationToken);

        // Push client event to session channel (gated by active connection)
        if (!skipActiveConnectionsCheck)
        {

            var isConnected = await _cacheStore.SetContainsAsync<string>(ACTIVE_CONNECTIONS_KEY, sessionId, cancellationToken);

            if (!isConnected)
            {
                _logger.LogDebug("Skipping client capability push for session {SessionId} - not in activeConnections (reason: {Reason})",
                    sessionId, reason);
                return;
            }
        }

        var capabilitiesEvent = new SessionCapabilitiesEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            SessionId = sessionGuid,
            Permissions = permissionsDict,
            Reason = reason
        };

        try
        {
            var published = await _clientEventPublisher.PublishToSessionAsync(sessionId, capabilitiesEvent, cancellationToken);

            if (published)
            {
                _logger.LogDebug("Published capabilities to session {SessionId} ({ServiceCount} services, reason: {Reason})",
                    sessionId, permissions.Count, reason);
            }
            else
            {
                _logger.LogWarning("Failed to publish capabilities to session {SessionId}", sessionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception publishing capabilities to session {SessionId}", sessionId);
            await _messageBus.TryPublishErrorAsync(
                "permission",
                "PublishCapabilities",
                ex.GetType().Name,
                ex.Message,
                "messaging",
                sessionId,
                ServiceErrorEventSeverity.Error,
                cancellationToken: cancellationToken);
        }
    }
    /// <summary>
    /// Handles a session connection event from the Connect service.
    /// Adds the session to activeConnections and triggers initial capability delivery.
    /// Roles and authorizations are used to compile capabilities without API calls.
    /// </summary>
    /// <param name="sessionId">The session ID that connected.</param>
    /// <param name="accountId">The account ID owning the session.</param>
    /// <param name="roles">User roles from JWT (e.g., ["user", "admin"]).</param>
    /// <param name="authorizations">Authorization states from JWT (e.g., ["my-game:authorized"]).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Status code indicating success or failure.</returns>
    public async Task<(StatusCodes, SessionUpdateResponse?)> HandleSessionConnectedAsync(
        string sessionId,
        string accountId,
        ICollection<string>? roles,
        ICollection<string>? authorizations,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.permission", "PermissionService.HandleSessionConnected");

        _logger.LogDebug("Handling session connected: {SessionId} for account {AccountId} with {RoleCount} roles and {AuthCount} authorizations",
            sessionId, accountId, roles?.Count ?? 0, authorizations?.Count ?? 0);

        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
        var sessionStates = await _sessionStatesStore.GetAsync(statesKey, cancellationToken) ?? new Dictionary<string, string>();

        var role = DetermineHighestPriorityRole(roles);
        sessionStates[SESSION_ROLE_KEY] = role;
        _logger.LogDebug("Set role '{Role}' for session {SessionId}", role, sessionId);

        if (authorizations != null && authorizations.Count > 0)
        {
            foreach (var auth in authorizations)
            {
                var parts = auth.Split(':');
                if (parts.Length == 2)
                {
                    var authServiceId = parts[0];
                    var state = parts[1];
                    sessionStates[authServiceId] = state;
                    _logger.LogDebug("Set authorization state '{State}' for service '{ServiceId}' on session {SessionId}",
                        state, authServiceId, sessionId);
                }
            }
        }

        await _sessionStatesStore.SaveAsync(statesKey, sessionStates, options: GetSessionDataStateOptions(), cancellationToken: cancellationToken);


        var addedToConnections = await _cacheStore.AddToSetAsync<string>(ACTIVE_CONNECTIONS_KEY, sessionId, cancellationToken: cancellationToken);
        if (addedToConnections)
        {
            var connectionCount = await _cacheStore.SetCountAsync(ACTIVE_CONNECTIONS_KEY, cancellationToken);
            _logger.LogDebug("Added session {SessionId} to active connections. Total: {Count}",
                sessionId, connectionCount);
        }

        await _cacheStore.AddToSetAsync<string>(ACTIVE_SESSIONS_KEY, sessionId, cancellationToken: cancellationToken);

        // skipActiveConnectionsCheck=true because we JUST added sessionId to activeConnections above
        await RecompileSessionPermissionsAsync(sessionId, sessionStates, CapabilityUpdateReason.SessionCreated, skipActiveConnectionsCheck: true, cancellationToken: cancellationToken);

        return (StatusCodes.OK, new SessionUpdateResponse
        {
            NewPermissions = null
        });
    }

    /// <summary>
    /// Handles a session disconnection event from the Connect service.
    /// Removes the session from activeConnections to prevent publishing to non-existent exchanges.
    /// </summary>
    /// <param name="sessionId">The session ID that disconnected.</param>
    /// <param name="reconnectable">Whether the session can reconnect within the window.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Status code indicating success or failure.</returns>
    public async Task<(StatusCodes, SessionUpdateResponse?)> HandleSessionDisconnectedAsync(
        string sessionId,
        bool reconnectable,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.permission", "PermissionService.HandleSessionDisconnected");

        _logger.LogDebug("Handling session disconnected: {SessionId}, Reconnectable: {Reconnectable}",
            sessionId, reconnectable);


        var removed = await _cacheStore.RemoveFromSetAsync<string>(ACTIVE_CONNECTIONS_KEY, sessionId, cancellationToken);
        if (removed)
        {
            var remainingCount = await _cacheStore.SetCountAsync(ACTIVE_CONNECTIONS_KEY, cancellationToken);
            _logger.LogDebug("Removed session {SessionId} from active connections. Remaining: {Count}",
                sessionId, remainingCount);
        }

        if (!reconnectable)
        {
            await _cacheStore.RemoveFromSetAsync<string>(ACTIVE_SESSIONS_KEY, sessionId, cancellationToken);

            await ClearSessionStateAsync(new ClearSessionStateRequest { SessionId = Guid.Parse(sessionId) }, cancellationToken);

            _logger.LogDebug("Cleared state for non-reconnectable session {SessionId}", sessionId);
        }

        return (StatusCodes.OK, new SessionUpdateResponse
        {
            NewPermissions = null
        });
    }

    /// <summary>
    /// Handles a session reconnection within the reconnection window.
    /// Re-adds the session to active connections and recompiles permissions
    /// from existing Redis state (preserved during the reconnection window).
    /// </summary>
    /// <param name="sessionId">The reconnected session ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task RecompileForReconnectionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.permission", "PermissionService.RecompileForReconnection");

        _logger.LogDebug("Handling session reconnection: {SessionId}", sessionId);

        // Re-add session to active connections (may have been removed during disconnect)

        await _cacheStore.AddToSetAsync<string>(ACTIVE_CONNECTIONS_KEY, sessionId, cancellationToken: cancellationToken);

        // Recompile from existing Redis state (preserved during reconnection window)
        // skipActiveConnectionsCheck=true because we JUST re-added sessionId to activeConnections above
        await RecompileSessionPermissionsAsync(sessionId, CapabilityUpdateReason.SessionCreated, cancellationToken);

        _logger.LogDebug("Reconnection recompilation complete for session {SessionId}", sessionId);
    }
}
