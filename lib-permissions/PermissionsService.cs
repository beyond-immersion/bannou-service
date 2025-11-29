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

            // Update service version
            transactionRequests.Add(new StateTransactionRequest(
                $"{PERMISSION_VERSION_KEY}:{body.ServiceId}",
                JsonSerializer.SerializeToUtf8Bytes(body.Version),
                StateOperationType.Upsert));

            // Execute atomic transaction
            await _daprClient.ExecuteStateTransactionAsync(STATE_STORE, transactionRequests, cancellationToken: cancellationToken);

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

            // Recompile session permissions
            await RecompileSessionPermissionsAsync(body.SessionId, "session_state_changed");

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

            // Recompile all permissions for this session
            await RecompileSessionPermissionsAsync(body.SessionId, "role_changed");

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
    /// Handle service registration events from RabbitMQ.
    /// Properly parses ServiceRegistrationEvent and converts endpoint permissions
    /// to ServicePermissionMatrix format (State → Role → Methods).
    /// </summary>
    [Topic("bannou-pubsub", "permissions.service-registered")]
    [HttpPost("handle-service-registration")]
    public async Task<IActionResult> HandleServiceRegistrationAsync([FromBody] object serviceEventObj)
    {
        try
        {
            _logger.LogDebug("Received service registration event: {EventData}", serviceEventObj);

            // Parse the ServiceRegistrationEvent
            var jsonElement = (JsonElement)serviceEventObj;
            var serviceId = jsonElement.GetProperty("serviceId").GetString();
            var version = jsonElement.GetProperty("version").GetString();
            var endpoints = jsonElement.GetProperty("endpoints");

            _logger.LogInformation("Processing service registration for {ServiceId} version {Version} with {EndpointCount} endpoints",
                serviceId, version, endpoints.GetArrayLength());

            // Build State → Role → Methods mapping from endpoint permissions
            var permissionMatrix = new Dictionary<string, StatePermissions>();

            foreach (var endpointElement in endpoints.EnumerateArray())
            {
                var path = endpointElement.GetProperty("path").GetString();
                var method = endpointElement.GetProperty("method").GetString();
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

                    // If no specific states required, default to "authenticated"
                    if (stateKeys.Count == 0)
                    {
                        stateKeys.Add("authenticated");
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
                            permissionMatrix[stateKey][role ?? "user"] = new System.Collections.ObjectModel.Collection<string>();
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

            // Register the permissions using the existing method
            var result = await RegisterServicePermissionsAsync(servicePermissionMatrix);

            if (result.Item1 == StatusCodes.OK)
            {
                _logger.LogInformation("Successfully registered permissions for service {ServiceId}", serviceId);
                return new OkResult();
            }
            else
            {
                _logger.LogError("Failed to register permissions for service {ServiceId}: {StatusCode}",
                    serviceId, result.Item1);
                return new StatusCodeResult((int)result.Item1);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling service registration event: {EventData}", serviceEventObj);
            return new StatusCodeResult(500);
        }
    }

    /// <summary>
    /// Handle session state change events from other services.
    /// </summary>
    [Topic("bannou-pubsub", "permissions.session-state-changed")]
    [HttpPost("handle-session-state-change")]
    public async Task<IActionResult> HandleSessionStateChangeAsync([FromBody] object stateEventObj)
    {
        try
        {
            // Parse the event data
            var jsonElement = (JsonElement)stateEventObj;
            var sessionId = jsonElement.GetProperty("sessionId").GetString();
            var serviceId = jsonElement.GetProperty("serviceId").GetString();
            var newState = jsonElement.GetProperty("newState").GetString();
            var previousState = jsonElement.TryGetProperty("previousState", out var prevProp) ?
                prevProp.GetString() : null;

            _logger.LogInformation("Received session state change event for {SessionId}: {ServiceId} → {NewState}",
                sessionId, serviceId, newState);

            if (sessionId == null || serviceId == null || newState == null)
                return new StatusCodeResult(500);

            var stateUpdate = new SessionStateUpdate
            {
                SessionId = sessionId,
                ServiceId = serviceId,
                NewState = newState,
                PreviousState = previousState
            };

            await UpdateSessionStateAsync(stateUpdate);

            return new OkResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling session state change event");
            return new StatusCodeResult(500);
        }
    }

    /// <summary>
    /// Recompile permissions for a session and publish update to Connect service.
    /// </summary>
    private async Task RecompileSessionPermissionsAsync(string sessionId, string reason)
    {
        try
        {
            // Get session states
            var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
            var sessionStates = await _daprClient.GetStateAsync<Dictionary<string, string>>(
                STATE_STORE, statesKey);

            if (sessionStates == null || sessionStates.Count == 0)
            {
                _logger.LogDebug("No session states found for {SessionId}, skipping recompilation", sessionId);
                return;
            }

            var role = sessionStates.GetValueOrDefault("role", "user");

            // Compile permissions for each service
            var compiledPermissions = new Dictionary<string, List<string>>();

            foreach (var serviceState in sessionStates.Where(s => s.Key != "role"))
            {
                var serviceId = serviceState.Key;
                var state = serviceState.Value;

                var matrixKey = string.Format(PERMISSION_MATRIX_KEY, serviceId, state, role);
                var endpoints = await _daprClient.GetStateAsync<HashSet<string>>(STATE_STORE, matrixKey);

                if (endpoints != null && endpoints.Count > 0)
                {
                    compiledPermissions[serviceId] = endpoints.ToList();
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

            var newPermissionData = new Dictionary<string, object>();
            foreach (var kvp in compiledPermissions)
            {
                newPermissionData[kvp.Key] = kvp.Value;
            }
            newPermissionData["version"] = currentVersion + 1;
            newPermissionData["generated_at"] = DateTimeOffset.UtcNow.ToString();

            await _daprClient.SaveStateAsync(STATE_STORE, permissionsKey, newPermissionData);

            // Clear in-memory cache
            _sessionCapabilityCache.TryRemove(sessionId, out _);

            // Publish capability update to Connect service
            await PublishCapabilityUpdateAsync(sessionId, compiledPermissions, reason);

            _logger.LogInformation("Recompiled permissions for session {SessionId}: {ServiceCount} services",
                sessionId, compiledPermissions.Count);
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
