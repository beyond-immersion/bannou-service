using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.Services;
using Dapr;
using Dapr.Client;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Permissions;

/// <summary>
/// Permissions service - authoritative source for all permission mappings and session capabilities.
/// Uses Dapr state store for atomic operations and Redis-backed data structures.
/// </summary>
[DaprService("permissions", typeof(IPermissionsService), lifetime: ServiceLifetime.Singleton)]
public class PermissionsService : IPermissionsService
{
    private readonly ILogger<PermissionsService> _logger;
    private readonly PermissionsServiceConfiguration _configuration;
    private readonly DaprClient _daprClient;

    // Dapr state store name
    private const string STATE_STORE = "permissions-store";

    // State key patterns
    private const string ACTIVE_SESSIONS_KEY = "active_sessions";
    private const string REGISTERED_SERVICES_KEY = "registered_services"; // List of all services that have registered permissions
    private const string SERVICE_REGISTERED_KEY = "service-registered:{0}"; // Individual service registration marker (race-condition safe)
    private const string SESSION_STATES_KEY = "session:{0}:states";
    private const string SESSION_PERMISSIONS_KEY = "session:{0}:permissions";
    private const string PERMISSION_MATRIX_KEY = "permissions:{0}:{1}:{2}"; // service:state:role
    private const string PERMISSION_VERSION_KEY = "permission_versions";
    private const string SERVICE_LOCK_KEY = "lock:service-registration:{0}";

    // Cache for compiled permissions (in-memory cache with Dapr state backing)
    private readonly ConcurrentDictionary<string, CapabilityResponse> _sessionCapabilityCache;

    public PermissionsService(
        ILogger<PermissionsService> logger,
        PermissionsServiceConfiguration configuration,
        DaprClient daprClient)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _daprClient = daprClient ?? throw new ArgumentNullException(nameof(daprClient));
        _sessionCapabilityCache = new ConcurrentDictionary<string, CapabilityResponse>();

        _logger.LogInformation("Permissions service initialized with Dapr state store");
    }

    /// <summary>
    /// Get compiled capabilities for a session from Dapr state store with in-memory caching.
    /// </summary>
    public async Task<(StatusCodes, CapabilityResponse?)> GetCapabilitiesAsync(
        CapabilityRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Getting capabilities for session {SessionId}", body.SessionId);

            // Check in-memory cache first
            if (_sessionCapabilityCache.TryGetValue(body.SessionId, out var cachedResponse))
            {
                _logger.LogDebug("Returning cached capabilities for session {SessionId}", body.SessionId);
                return (StatusCodes.OK, cachedResponse);
            }

            // Get compiled permissions from Dapr state store
            var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, body.SessionId);
            var permissionsData = await _daprClient.GetStateAsync<Dictionary<string, object>>(
                STATE_STORE, permissionsKey, cancellationToken: cancellationToken);

            if (permissionsData == null || permissionsData.Count == 0)
            {
                _logger.LogWarning("No permissions found for session {SessionId}", body.SessionId);
                return (StatusCodes.NotFound, null);
            }

            // Parse permissions data
            var permissions = new Dictionary<string, ICollection<string>>();
            var version = 0;
            var generatedAt = DateTimeOffset.UtcNow;

            foreach (var item in permissionsData)
            {
                if (item.Value == null)
                    continue;

                if (item.Key == "version")
                {
                    int.TryParse(item.Value.ToString(), out version);
                }
                else if (item.Key == "generated_at")
                {
                    DateTimeOffset.TryParse(item.Value.ToString(), out generatedAt);
                }
                else
                {
                    // Parse JSON array of endpoints
                    var jsonElement = (JsonElement)item.Value;
                    var endpoints = JsonSerializer.Deserialize<List<string>>(jsonElement.GetRawText());
                    if (endpoints != null)
                    {
                        permissions[item.Key] = endpoints;
                    }
                }
            }

            var response = new CapabilityResponse
            {
                SessionId = body.SessionId,
                Permissions = permissions,
                GeneratedAt = generatedAt
            };

            // Cache the response for future requests
            _sessionCapabilityCache[body.SessionId] = response;

            _logger.LogInformation("Retrieved capabilities for session {SessionId} with {ServiceCount} services",
                body.SessionId, permissions.Count);

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting capabilities for session {SessionId}", body.SessionId);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Fast O(1) validation using Dapr state lookup for specific API access.
    /// </summary>
    public async Task<(StatusCodes, ValidationResponse?)> ValidateApiAccessAsync(
        ValidationRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Validating API access for session {SessionId}, service {ServiceId}, method {Method}",
                body.SessionId, body.ServiceId, body.Method);

            // Get session permissions from Dapr state store
            var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, body.SessionId);
            var permissionsData = await _daprClient.GetStateAsync<Dictionary<string, object>>(
                STATE_STORE, permissionsKey, cancellationToken: cancellationToken);

            if (permissionsData == null || !permissionsData.ContainsKey(body.ServiceId))
            {
                _logger.LogDebug("No permissions found for session {SessionId} service {ServiceId}",
                    body.SessionId, body.ServiceId);
                return (StatusCodes.OK, new ValidationResponse
                {
                    Allowed = false,
                    SessionId = body.SessionId
                });
            }

            // Parse allowed endpoints
            var jsonElement = (JsonElement)permissionsData[body.ServiceId];
            var allowedEndpoints = JsonSerializer.Deserialize<List<string>>(jsonElement.GetRawText());
            var allowed = allowedEndpoints?.Contains(body.Method) ?? false;

            _logger.LogDebug("API access validation result for session {SessionId}: {Allowed}",
                body.SessionId, allowed);

            return (StatusCodes.OK, new ValidationResponse
            {
                Allowed = allowed,
                SessionId = body.SessionId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating API access for session {SessionId}", body.SessionId);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Register service permission matrix and trigger recompilation for all active sessions.
    /// Uses Dapr atomic transactions to prevent race conditions.
    /// </summary>
    public async Task<(StatusCodes, RegistrationResponse?)> RegisterServicePermissionsAsync(
        ServicePermissionMatrix body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Registering service permissions for {ServiceId} version {Version}",
                body.ServiceId, body.Version);

            // Use Dapr state transactions for atomic operations
            var transactionRequests = new List<StateTransactionRequest>();

            // Update permission matrix atomically
            if (body.Permissions != null)
            {
                _logger.LogInformation("Processing {PermissionCount} permission states for {ServiceId}",
                    body.Permissions.Count, body.ServiceId);

                foreach (var stateEntry in body.Permissions)
                {
                    var stateName = stateEntry.Key;
                    var statePermissions = stateEntry.Value;

                    foreach (var roleEntry in statePermissions)
                    {
                        var roleName = roleEntry.Key;
                        var methods = roleEntry.Value;

                        var matrixKey = string.Format(PERMISSION_MATRIX_KEY,
                            body.ServiceId,
                            stateName,
                            roleName);

                        _logger.LogInformation("Storing {MethodCount} methods at key: {MatrixKey}",
                            methods.Count, matrixKey);

                        // Get existing endpoints and add new ones
                        var existingEndpoints = await _daprClient.GetStateAsync<HashSet<string>>(
                            STATE_STORE, matrixKey, cancellationToken: cancellationToken) ?? new HashSet<string>();

                        foreach (var method in methods)
                        {
                            existingEndpoints.Add(method);
                        }

                        transactionRequests.Add(new StateTransactionRequest(
                            matrixKey, JsonSerializer.SerializeToUtf8Bytes(existingEndpoints), StateOperationType.Upsert));
                    }
                }
            }
            else
            {
                _logger.LogWarning("No permissions to register for {ServiceId}", body.ServiceId);
            }

            // Update service version
            transactionRequests.Add(new StateTransactionRequest(
                $"{PERMISSION_VERSION_KEY}:{body.ServiceId}",
                JsonSerializer.SerializeToUtf8Bytes(body.Version),
                StateOperationType.Upsert));

            // Execute atomic transaction
            await _daprClient.ExecuteStateTransactionAsync(STATE_STORE, transactionRequests, cancellationToken: cancellationToken);

            // Track this service using individual key pattern (race-condition safe)
            // Each service has its own key, eliminating the need to modify a shared list
            var serviceRegisteredKey = string.Format(SERVICE_REGISTERED_KEY, body.ServiceId);
            var registrationInfo = new
            {
                ServiceId = body.ServiceId,
                Version = body.Version,
                RegisteredAt = DateTimeOffset.UtcNow
            };
            await _daprClient.SaveStateAsync(STATE_STORE, serviceRegisteredKey, registrationInfo, cancellationToken: cancellationToken);
            _logger.LogInformation("Stored individual registration marker for {ServiceId} at key {Key}", body.ServiceId, serviceRegisteredKey);

            // Also update the centralized registered_services list (enables "list all registered services" queries)
            // Uses Dapr distributed lock to prevent race conditions when multiple services register concurrently
            const string LOCK_STORE = "lockstore";
            const string LOCK_RESOURCE = "registered_services_lock";
            var lockOwnerId = $"{body.ServiceId}-{Guid.NewGuid():N}";

            try
            {
                // Acquire distributed lock to serialize updates to the shared registered_services list
                // Note: Dapr distributed lock API is alpha, suppress the warning
#pragma warning disable DAPR_DISTRIBUTEDLOCK
                await using var serviceLock = await _daprClient.Lock(
                    LOCK_STORE,
                    LOCK_RESOURCE,
                    lockOwnerId,
                    expiryInSeconds: 30,  // Lock expires after 30 seconds if not released
                    cancellationToken: cancellationToken);
#pragma warning restore DAPR_DISTRIBUTEDLOCK

                if (serviceLock.Success)
                {
                    _logger.LogInformation("Acquired lock for {ServiceId} to update registered services", body.ServiceId);

                    // Now we have exclusive access - safe to read-modify-write
                    var registeredServices = await _daprClient.GetStateAsync<HashSet<string>>(
                        STATE_STORE, REGISTERED_SERVICES_KEY, cancellationToken: cancellationToken) ?? new HashSet<string>();

                    _logger.LogInformation("Current registered services (locked): {Services}",
                        string.Join(", ", registeredServices));

                    if (!registeredServices.Contains(body.ServiceId))
                    {
                        registeredServices.Add(body.ServiceId);
                        await _daprClient.SaveStateAsync(STATE_STORE, REGISTERED_SERVICES_KEY, registeredServices, cancellationToken: cancellationToken);
                        _logger.LogInformation("Successfully added {ServiceId} to registered services list. Now: {Services}",
                            body.ServiceId, string.Join(", ", registeredServices));
                    }
                    else
                    {
                        _logger.LogInformation("Service {ServiceId} already in registered services list", body.ServiceId);
                    }
                }
                else
                {
                    // Failed to acquire lock - this shouldn't happen often, log and continue
                    // The service is still registered via the individual marker, so this isn't critical
                    _logger.LogWarning("Failed to acquire lock for {ServiceId}, registered_services list may not include this service immediately", body.ServiceId);
                }
            }
            catch (Exception lockEx)
            {
                // Lock acquisition or state update failed
                _logger.LogWarning(lockEx, "Error during registered services update for {ServiceId}, service is registered but may not appear in service list immediately", body.ServiceId);
            }

            // Get all active sessions and recompile
            var activeSessions = await _daprClient.GetStateAsync<HashSet<string>>(
                STATE_STORE, ACTIVE_SESSIONS_KEY, cancellationToken: cancellationToken) ?? new HashSet<string>();

            // Recompile permissions for all active sessions
            var recompiledCount = 0;
            foreach (var sessionId in activeSessions)
            {
                await RecompileSessionPermissionsAsync(sessionId, "service_registered");
                recompiledCount++;
            }

            _logger.LogInformation("Service {ServiceId} registered successfully, recompiled {Count} sessions",
                body.ServiceId, recompiledCount);

            // Publish capability update event so Connect service can push updates to connected clients
            try
            {
                await _daprClient.PublishEventAsync(
                    "bannou-pubsub",
                    "permissions.capabilities-updated",
                    new
                    {
                        eventId = Guid.NewGuid().ToString(),
                        timestamp = DateTimeOffset.UtcNow,
                        serviceId = body.ServiceId,
                        affectedSessions = activeSessions.ToList(),
                        reason = "service_registered"
                    },
                    cancellationToken);
                _logger.LogDebug("Published capabilities-updated event for service {ServiceId}", body.ServiceId);
            }
            catch (Exception pubEx)
            {
                // Don't fail registration if event publishing fails - Connect will get updates on next client request
                _logger.LogWarning(pubEx, "Failed to publish capabilities-updated event for {ServiceId}", body.ServiceId);
            }

            return (StatusCodes.OK, new RegistrationResponse
            {
                ServiceId = body.ServiceId,
                Success = true,
                Message = $"Registered {body.Permissions?.Count ?? 0} permission rules, recompiled {recompiledCount} sessions"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering service permissions for {ServiceId}", body.ServiceId);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Update session state for specific service and recompile permissions.
    /// Uses Dapr atomic transactions for consistency.
    /// </summary>
    public async Task<(StatusCodes, SessionUpdateResponse?)> UpdateSessionStateAsync(
        SessionStateUpdate body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Updating session {SessionId} state for service {ServiceId}: {OldState} → {NewState}",
                body.SessionId, body.ServiceId, body.PreviousState, body.NewState);

            var statesKey = string.Format(SESSION_STATES_KEY, body.SessionId);
            var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, body.SessionId);

            // Get current session states
            var sessionStates = await _daprClient.GetStateAsync<Dictionary<string, string>>(
                STATE_STORE, statesKey, cancellationToken: cancellationToken) ?? new Dictionary<string, string>();

            // Get current permissions data for version increment
            var permissionsData = await _daprClient.GetStateAsync<Dictionary<string, object>>(
                STATE_STORE, permissionsKey, cancellationToken: cancellationToken) ?? new Dictionary<string, object>();

            // Update session state
            sessionStates[body.ServiceId] = body.NewState;

            // Increment version
            var currentVersion = 0;
            if (permissionsData.ContainsKey("version"))
            {
                int.TryParse(permissionsData["version"]?.ToString(), out currentVersion);
            }
            var newVersion = currentVersion + 1;

            // Get active sessions
            var activeSessions = await _daprClient.GetStateAsync<HashSet<string>>(
                STATE_STORE, ACTIVE_SESSIONS_KEY, cancellationToken: cancellationToken) ?? new HashSet<string>();
            activeSessions.Add(body.SessionId);

            // Atomic transaction to update session state, version, and active sessions
            var transactionRequests = new List<StateTransactionRequest>
            {
                new StateTransactionRequest(statesKey, JsonSerializer.SerializeToUtf8Bytes(sessionStates), StateOperationType.Upsert),
                new StateTransactionRequest(ACTIVE_SESSIONS_KEY, JsonSerializer.SerializeToUtf8Bytes(activeSessions), StateOperationType.Upsert)
            };

            await _daprClient.ExecuteStateTransactionAsync(STATE_STORE, transactionRequests, cancellationToken: cancellationToken);

            // Recompile session permissions using the states we already have
            // (avoids read-after-write consistency issues by not re-reading from Dapr)
            await RecompileSessionPermissionsAsync(body.SessionId, sessionStates, "session_state_changed");

            return (StatusCodes.OK, new SessionUpdateResponse
            {
                SessionId = body.SessionId,
                Success = true,
                Message = $"Updated {body.ServiceId} state to {body.NewState}, version {newVersion}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating session state for {SessionId}", body.SessionId);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Update session role and recompile all service permissions.
    /// </summary>
    public async Task<(StatusCodes, SessionUpdateResponse?)> UpdateSessionRoleAsync(
        SessionRoleUpdate body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Updating session {SessionId} role: {OldRole} → {NewRole}",
                body.SessionId, body.PreviousRole, body.NewRole);

            var statesKey = string.Format(SESSION_STATES_KEY, body.SessionId);

            // Get current session states
            var sessionStates = await _daprClient.GetStateAsync<Dictionary<string, string>>(
                STATE_STORE, statesKey, cancellationToken: cancellationToken) ?? new Dictionary<string, string>();

            // Update role
            sessionStates["role"] = body.NewRole;

            // Get active sessions
            var activeSessions = await _daprClient.GetStateAsync<HashSet<string>>(
                STATE_STORE, ACTIVE_SESSIONS_KEY, cancellationToken: cancellationToken) ?? new HashSet<string>();
            activeSessions.Add(body.SessionId);

            // Atomic update
            var transactionRequests = new List<StateTransactionRequest>
            {
                new StateTransactionRequest(statesKey, JsonSerializer.SerializeToUtf8Bytes(sessionStates), StateOperationType.Upsert),
                new StateTransactionRequest(ACTIVE_SESSIONS_KEY, JsonSerializer.SerializeToUtf8Bytes(activeSessions), StateOperationType.Upsert)
            };

            await _daprClient.ExecuteStateTransactionAsync(STATE_STORE, transactionRequests, cancellationToken: cancellationToken);

            // Recompile all permissions for this session using the states we already have
            // (avoids read-after-write consistency issues by not re-reading from Dapr)
            await RecompileSessionPermissionsAsync(body.SessionId, sessionStates, "role_changed");

            return (StatusCodes.OK, new SessionUpdateResponse
            {
                SessionId = body.SessionId,
                Success = true,
                Message = $"Updated role to {body.NewRole}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating session role for {SessionId}", body.SessionId);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Get complete session information including states, role, and compiled permissions.
    /// </summary>
    public async Task<(StatusCodes, SessionInfo?)> GetSessionInfoAsync(
        SessionInfoRequest body,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting session info for {SessionId}", body.SessionId);

            var statesKey = string.Format(SESSION_STATES_KEY, body.SessionId);
            var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, body.SessionId);

            // Get session states and permissions concurrently
            var statesTask = _daprClient.GetStateAsync<Dictionary<string, string>>(
                STATE_STORE, statesKey, cancellationToken: cancellationToken);
            var permissionsTask = _daprClient.GetStateAsync<Dictionary<string, object>>(
                STATE_STORE, permissionsKey, cancellationToken: cancellationToken);

            await Task.WhenAll(statesTask, permissionsTask);

            var states = statesTask.Result ?? new Dictionary<string, string>();
            var permissionsData = permissionsTask.Result ?? new Dictionary<string, object>();

            if (states.Count == 0)
            {
                _logger.LogWarning("No session info found for {SessionId}", body.SessionId);
                return (StatusCodes.NotFound, null);
            }

            // Parse permissions data
            var permissions = new Dictionary<string, ICollection<string>>();
            var version = 0;

            foreach (var item in permissionsData)
            {
                if (item.Value == null)
                    continue;

                if (item.Key == "version")
                {
                    int.TryParse(item.Value.ToString(), out version);
                }
                else if (item.Key != "generated_at")
                {
                    var jsonElement = (JsonElement)item.Value;
                    var endpoints = JsonSerializer.Deserialize<List<string>>(jsonElement.GetRawText());
                    if (endpoints != null)
                    {
                        permissions[item.Key] = endpoints;
                    }
                }
            }

            var sessionInfo = new SessionInfo
            {
                SessionId = body.SessionId,
                States = states,
                Role = states.GetValueOrDefault("role", "user"),
                Permissions = permissions,
                Version = version,
                LastUpdated = DateTimeOffset.UtcNow
            };

            return (StatusCodes.OK, sessionInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting session info for {SessionId}", body.SessionId);
            return (StatusCodes.InternalServerError, null);
        }
    }

    /// <summary>
    /// Recompile permissions for a session and publish update to Connect service.
    /// Reads session states from Dapr (used when states are not already in memory).
    /// </summary>
    private async Task RecompileSessionPermissionsAsync(string sessionId, string reason)
    {
        // Get session states from Dapr
        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
        var sessionStates = await _daprClient.GetStateAsync<Dictionary<string, string>>(
            STATE_STORE, statesKey);

        if (sessionStates == null || sessionStates.Count == 0)
        {
            _logger.LogDebug("No session states found for {SessionId}, skipping recompilation", sessionId);
            return;
        }

        await RecompileSessionPermissionsAsync(sessionId, sessionStates, reason);
    }

    /// <summary>
    /// Recompile permissions for a session using provided session states.
    /// This overload avoids read-after-write consistency issues by using states already in memory.
    /// </summary>
    private async Task RecompileSessionPermissionsAsync(string sessionId, Dictionary<string, string> sessionStates, string reason)
    {
        try
        {
            if (sessionStates == null || sessionStates.Count == 0)
            {
                _logger.LogDebug("No session states provided for {SessionId}, skipping recompilation", sessionId);
                return;
            }

            var role = sessionStates.GetValueOrDefault("role", "user");
            _logger.LogInformation("Recompiling permissions for session {SessionId}, role: {Role}, reason: {Reason}",
                sessionId, role, reason);

            // Compile permissions for each service
            var compiledPermissions = new Dictionary<string, List<string>>();

            // First, get all registered services and check "default" state permissions for the role
            var registeredServices = await _daprClient.GetStateAsync<HashSet<string>>(
                STATE_STORE, REGISTERED_SERVICES_KEY) ?? new HashSet<string>();

            _logger.LogInformation("Found {Count} registered services: {Services}",
                registeredServices.Count, string.Join(", ", registeredServices));

            foreach (var serviceId in registeredServices)
            {
                // Check for "authenticated" state permissions (endpoints with no required states)
                // Note: The event handler defaults to "authenticated" state when no states are required
                var authenticatedMatrixKey = string.Format(PERMISSION_MATRIX_KEY, serviceId, "authenticated", role);
                var defaultEndpoints = await _daprClient.GetStateAsync<HashSet<string>>(STATE_STORE, authenticatedMatrixKey);

                _logger.LogDebug("Looking up {Key}, found {Count} endpoints",
                    authenticatedMatrixKey, defaultEndpoints?.Count ?? 0);

                if (defaultEndpoints != null && defaultEndpoints.Count > 0)
                {
                    if (!compiledPermissions.ContainsKey(serviceId))
                    {
                        compiledPermissions[serviceId] = new List<string>();
                    }
                    compiledPermissions[serviceId].AddRange(defaultEndpoints);
                    _logger.LogInformation("Added {Count} endpoints for service {ServiceId} to session {SessionId}",
                        defaultEndpoints.Count, serviceId, sessionId);
                }
            }

            // Then, also check service-specific states that the session has
            foreach (var serviceState in sessionStates.Where(s => s.Key != "role"))
            {
                var serviceId = serviceState.Key;
                var state = serviceState.Value;

                // Skip "default" state as we already checked it above
                if (state == "default")
                    continue;

                var matrixKey = string.Format(PERMISSION_MATRIX_KEY, serviceId, state, role);
                var endpoints = await _daprClient.GetStateAsync<HashSet<string>>(STATE_STORE, matrixKey);

                if (endpoints != null && endpoints.Count > 0)
                {
                    if (!compiledPermissions.ContainsKey(serviceId))
                    {
                        compiledPermissions[serviceId] = new List<string>();
                    }
                    // Add unique endpoints (avoid duplicates)
                    foreach (var endpoint in endpoints)
                    {
                        if (!compiledPermissions[serviceId].Contains(endpoint))
                        {
                            compiledPermissions[serviceId].Add(endpoint);
                        }
                    }
                }
            }

            // Store compiled permissions with version increment
            var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionId);
            var existingPermissions = await _daprClient.GetStateAsync<Dictionary<string, object>>(
                STATE_STORE, permissionsKey) ?? new Dictionary<string, object>();

            var currentVersion = 0;
            if (existingPermissions.ContainsKey("version"))
            {
                int.TryParse(existingPermissions["version"]?.ToString(), out currentVersion);
            }

            var newVersion = currentVersion + 1;
            var newPermissionData = new Dictionary<string, object>();
            foreach (var kvp in compiledPermissions)
            {
                newPermissionData[kvp.Key] = kvp.Value;
            }
            newPermissionData["version"] = newVersion;
            newPermissionData["generated_at"] = DateTimeOffset.UtcNow.ToString();

            await _daprClient.SaveStateAsync(STATE_STORE, permissionsKey, newPermissionData);

            // Clear in-memory cache after write
            _sessionCapabilityCache.TryRemove(sessionId, out _);

            // Publish capability update to Connect service
            await PublishCapabilityUpdateAsync(sessionId, compiledPermissions, reason);

            _logger.LogInformation("Recompiled permissions for session {SessionId}: {ServiceCount} services, version {Version}",
                sessionId, compiledPermissions.Count, newVersion);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recompiling permissions for session {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// Publish capability update to specific Connect service instance.
    /// </summary>
    private async Task PublishCapabilityUpdateAsync(string sessionId, Dictionary<string, List<string>> permissions, string reason)
    {
        try
        {
            var capabilityUpdate = new
            {
                SessionId = sessionId,
                Version = DateTimeOffset.UtcNow.Ticks, // Simple version using timestamp
                UpdateType = "full", // Always send full for now
                FullCapabilities = permissions.ToDictionary(
                    kvp => kvp.Key,
                    kvp => (ICollection<string>)kvp.Value
                ),
                GeneratedAt = DateTimeOffset.UtcNow,
                ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                Reason = reason
            };

            // Publish to session-specific Connect channel
            var connectChannel = $"CONNECT_{sessionId}";
            await _daprClient.PublishEventAsync("bannou-pubsub", connectChannel, capabilityUpdate);

            _logger.LogDebug("Published capability update to {Channel} for session {SessionId}",
                connectChannel, sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing capability update for session {SessionId}", sessionId);
        }
    }
}
