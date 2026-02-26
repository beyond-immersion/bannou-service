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

/// <summary>
/// Permission service - authoritative source for all permission mappings and session capabilities.
/// Uses lib-state for atomic operations and Redis-backed data structures.
/// </summary>
[BannouService("permission", typeof(IPermissionService), lifetime: ServiceLifetime.Singleton, layer: ServiceLayer.AppFoundation)]
public partial class PermissionService : IPermissionService, IPermissionRegistry
{
    private readonly ILogger<PermissionService> _logger;
    private readonly PermissionServiceConfiguration _configuration;
    private readonly IStateStoreFactory _stateStoreFactory;
    private readonly IMessageBus _messageBus;
    private readonly IClientEventPublisher _clientEventPublisher;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly IDistributedLockProvider _lockProvider;

    // State key patterns
    private const string ACTIVE_SESSIONS_KEY = "active_sessions";
    private const string ACTIVE_CONNECTIONS_KEY = "active_connections";
    private const string REGISTERED_SERVICES_KEY = "registered_services";
    private const string SERVICE_REGISTERED_KEY = "service-registered:{0}";
    private const string SESSION_STATES_KEY = "session:{0}:states";
    private const string SESSION_PERMISSIONS_KEY = "session:{0}:permissions";
    private const string PERMISSION_MATRIX_KEY = "permissions:{0}:{1}:{2}";
    private const string PERMISSION_VERSION_KEY = "permission_versions";
    private const string PERMISSION_HASH_KEY = "permission_hash:{0}";
    private const string SERVICE_STATES_KEY = "service-states:{0}";

    /// <summary>
    /// Key used to store the session role in the session states dictionary.
    /// </summary>
    private const string SESSION_ROLE_KEY = "role";

    public PermissionService(
        ILogger<PermissionService> logger,
        PermissionServiceConfiguration configuration,
        IStateStoreFactory stateStoreFactory,
        IMessageBus messageBus,
        IClientEventPublisher clientEventPublisher,
        ITelemetryProvider telemetryProvider,
        IDistributedLockProvider lockProvider,
        IEventConsumer eventConsumer)
    {
        _logger = logger;
        _configuration = configuration;
        _stateStoreFactory = stateStoreFactory;
        _messageBus = messageBus;
        _clientEventPublisher = clientEventPublisher;
        _telemetryProvider = telemetryProvider;
        _lockProvider = lockProvider;

        // Register event handlers via partial class (PermissionServiceEvents.cs)
        RegisterEventConsumers(eventConsumer);

        _logger.LogInformation("Permission service initialized with native state store, session-specific client event publishing, and event handlers registered");
    }

    /// <summary>
    /// Computes a deterministic hash of permission data for idempotent registration.
    /// The hash is computed from a sorted, canonical representation of the permission matrix.
    /// </summary>
    private static string ComputePermissionDataHash(ServicePermissionMatrix body)
    {
        var builder = new StringBuilder();
        builder.Append($"v:{body.Version};");

        if (body.Permissions != null)
        {
            foreach (var stateEntry in body.Permissions.OrderBy(s => s.Key))
            {
                builder.Append($"s:{stateEntry.Key}[");

                foreach (var roleEntry in stateEntry.Value.OrderBy(r => r.Key))
                {
                    builder.Append($"r:{roleEntry.Key}(");
                    builder.Append(string.Join(",", roleEntry.Value.OrderBy(e => e)));
                    builder.Append(')');
                }

                builder.Append(']');
            }
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Get compiled capabilities for a session from Redis.
    /// </summary>
    public async Task<(StatusCodes, CapabilityResponse?)> GetCapabilitiesAsync(
        CapabilityRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting capabilities for session {SessionId}", body.SessionId);

        var sessionIdStr = body.SessionId.ToString();
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionIdStr);
        var permissionsData = await _stateStoreFactory.GetStore<Dictionary<string, object>>(StateStoreDefinitions.Permission)
            .GetAsync(permissionsKey, cancellationToken);

        if (permissionsData == null || permissionsData.Count == 0)
        {
            _logger.LogDebug("No permissions found for session {SessionId}", body.SessionId);
            return (StatusCodes.NotFound, null);
        }

        var permissions = new Dictionary<string, ICollection<string>>();
        var generatedAt = DateTimeOffset.UtcNow;

        foreach (var item in permissionsData)
        {
            if (item.Value == null)
                continue;

            if (item.Key == "generated_at")
            {
                DateTimeOffset.TryParse(item.Value.ToString(), out generatedAt);
            }
            else if (item.Key != "version")
            {
                var jsonElement = (JsonElement)item.Value;
                var endpoints = BannouJson.Deserialize<List<string>>(jsonElement.GetRawText());
                if (endpoints != null)
                {
                    permissions[item.Key] = endpoints;
                }
            }
        }

        var response = new CapabilityResponse
        {
            Permissions = permissions,
            GeneratedAt = generatedAt
        };

        _logger.LogDebug("Retrieved capabilities for session {SessionId} with {ServiceCount} services",
            body.SessionId, permissions.Count);

        return (StatusCodes.OK, response);
    }

    /// <summary>
    /// Fast O(1) validation using lib-state lookup for specific API access.
    /// </summary>
    public async Task<(StatusCodes, ValidationResponse?)> ValidateApiAccessAsync(
        ValidationRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Validating API access for session {SessionId}, service {ServiceId}, endpoint {Endpoint}",
            body.SessionId, body.ServiceId, body.Endpoint);

        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, body.SessionId);
        var permissionsData = await _stateStoreFactory.GetStore<Dictionary<string, object>>(StateStoreDefinitions.Permission)
            .GetAsync(permissionsKey, cancellationToken);

        if (permissionsData == null || !permissionsData.ContainsKey(body.ServiceId))
        {
            _logger.LogDebug("No permissions found for session {SessionId} service {ServiceId}",
                body.SessionId, body.ServiceId);
            return (StatusCodes.OK, new ValidationResponse
            {
                Allowed = false,
                Reason = $"No permissions registered for service {body.ServiceId}"
            });
        }

        var jsonElement = (JsonElement)permissionsData[body.ServiceId];
        var allowedEndpoints = BannouJson.Deserialize<List<string>>(jsonElement.GetRawText());
        var allowed = allowedEndpoints?.Contains(body.Endpoint) ?? false;

        _logger.LogDebug("API access validation result for session {SessionId}: {Allowed}",
            body.SessionId, allowed);

        return (StatusCodes.OK, new ValidationResponse
        {
            Allowed = allowed,
            Reason = allowed ? null : $"Endpoint {body.Endpoint} not in allowed list for service {body.ServiceId}"
        });
    }

    /// <summary>
    /// Register service permission matrix and trigger recompilation for all active sessions.
    /// Uses ICacheableStateStore atomic set operations for multi-instance safety.
    /// </summary>
    public async Task<(StatusCodes, RegistrationResponse?)> RegisterServicePermissionsAsync(
        ServicePermissionMatrix body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Registering service permissions for {ServiceId} version {Version}",
            body.ServiceId, body.Version);

        var newHash = ComputePermissionDataHash(body);
        var hashKey = string.Format(PERMISSION_HASH_KEY, body.ServiceId);
        var storedHash = await _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Permission)
            .GetAsync(hashKey, cancellationToken);

        var cacheStore = _stateStoreFactory.GetCacheableStore<string>(StateStoreDefinitions.Permission);
        var isServiceAlreadyRegistered = await cacheStore.SetContainsAsync<string>(REGISTERED_SERVICES_KEY, body.ServiceId, cancellationToken);

        if (storedHash != null && storedHash == newHash && isServiceAlreadyRegistered)
        {
            _logger.LogDebug("Service {ServiceId} registration skipped - permission data unchanged and service already registered (hash: {Hash})",
                body.ServiceId, newHash[..8] + "...");
            return (StatusCodes.OK, new RegistrationResponse());
        }

        if (storedHash == null)
        {
            _logger.LogInformation("Service {ServiceId} first-time registration (no stored hash), proceeding",
                body.ServiceId);
        }
        else if (storedHash != newHash)
        {
            _logger.LogInformation("Service {ServiceId} permission data has changed (hash mismatch), proceeding with registration",
                body.ServiceId);
        }
        else if (!isServiceAlreadyRegistered)
        {
            _logger.LogInformation("Service {ServiceId} hash matches but not in registered_services, proceeding to ensure consistency",
                body.ServiceId);
        }

        var hashSetStore = _stateStoreFactory.GetStore<HashSet<string>>(StateStoreDefinitions.Permission);
        if (body.Permissions != null)
        {
            _logger.LogDebug("Processing {PermissionCount} permission states for {ServiceId}",
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

                    var existingEndpoints = await hashSetStore.GetAsync(matrixKey, cancellationToken) ?? new HashSet<string>();

                    foreach (var method in methods)
                    {
                        existingEndpoints.Add(method);
                    }

                    await hashSetStore.SaveAsync(matrixKey, existingEndpoints, cancellationToken: cancellationToken);
                }
            }

            var serviceStatesKey = string.Format(SERVICE_STATES_KEY, body.ServiceId);
            await hashSetStore.SaveAsync(serviceStatesKey, new HashSet<string>(body.Permissions.Keys), cancellationToken: cancellationToken);
        }
        else
        {
            _logger.LogWarning("No permissions to register for {ServiceId}", body.ServiceId);
        }

        var versionValue = body.Version ?? "unknown";
        await _stateStoreFactory.GetStore<Dictionary<string, string>>(StateStoreDefinitions.Permission)
            .SaveAsync($"{PERMISSION_VERSION_KEY}:{body.ServiceId}", new Dictionary<string, string> { ["version"] = versionValue }, cancellationToken: cancellationToken);

        var serviceRegisteredKey = string.Format(SERVICE_REGISTERED_KEY, body.ServiceId);
        var registrationInfo = new ServiceRegistrationInfo
        {
            ServiceId = body.ServiceId,
            Version = versionValue,
            RegisteredAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        await _stateStoreFactory.GetStore<ServiceRegistrationInfo>(StateStoreDefinitions.Permission)
            .SaveAsync(serviceRegisteredKey, registrationInfo, cancellationToken: cancellationToken);
        _logger.LogInformation("Stored individual registration marker for {ServiceId} at key {Key}", body.ServiceId, serviceRegisteredKey);

        var added = await cacheStore.AddToSetAsync<string>(REGISTERED_SERVICES_KEY, body.ServiceId, cancellationToken: cancellationToken);
        _logger.LogInformation("Service {ServiceId} {Action} registered services list",
            body.ServiceId, added ? "added to" : "already in");

        var activeSessions = await cacheStore.GetSetAsync<string>(ACTIVE_SESSIONS_KEY, cancellationToken);

        var recompiledCount = 0;
        var stopwatch = Stopwatch.StartNew();

        if (activeSessions.Count > 0)
        {
            using var semaphore = new SemaphoreSlim(_configuration.MaxConcurrentRecompilations);
            var tasks = activeSessions.Select(async sessionId =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    await RecompileSessionPermissionsAsync(sessionId, "service_registered", cancellationToken);
                    Interlocked.Increment(ref recompiledCount);
                }
                finally
                {
                    semaphore.Release();
                }
            });
            await Task.WhenAll(tasks);
        }

        stopwatch.Stop();

        _logger.LogInformation(
            "Service {ServiceId} registered successfully, recompiled {Count} sessions in {ElapsedMs}ms (concurrency: {Concurrency})",
            body.ServiceId, recompiledCount, stopwatch.ElapsedMilliseconds, _configuration.MaxConcurrentRecompilations);

        await _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Permission)
            .SaveAsync(hashKey, newHash, cancellationToken: cancellationToken);
        _logger.LogDebug("Stored permission hash for {ServiceId}: {Hash}",
            body.ServiceId, newHash[..8] + "...");

        return (StatusCodes.OK, new RegistrationResponse());
    }

    /// <summary>
    /// Update session state for specific service and recompile permissions.
    /// Uses distributed lock to prevent lost updates from concurrent modifications.
    /// </summary>
    public async Task<(StatusCodes, SessionUpdateResponse?)> UpdateSessionStateAsync(
        SessionStateUpdate body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Updating session {SessionId} state for service {ServiceId}: {OldState} -> {NewState}",
            body.SessionId, body.ServiceId, body.PreviousState, body.NewState);

        var sessionIdStr = body.SessionId.ToString();
        var statesKey = string.Format(SESSION_STATES_KEY, sessionIdStr);

        // Distributed lock to prevent lost updates from concurrent session modifications
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.PermissionLock, sessionIdStr, $"permission-state:{Guid.NewGuid()}", _configuration.SessionLockTimeoutSeconds, cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogWarning("Failed to acquire lock for session {SessionId} state update", body.SessionId);
            return (StatusCodes.Conflict, null);
        }

        var sessionStates = await _stateStoreFactory.GetStore<Dictionary<string, string>>(StateStoreDefinitions.Permission)
            .GetAsync(statesKey, cancellationToken) ?? new Dictionary<string, string>();

        sessionStates[body.ServiceId] = body.NewState;

        // Atomic add to activeSessions
        var cacheStore = _stateStoreFactory.GetCacheableStore<string>(StateStoreDefinitions.Permission);
        await cacheStore.AddToSetAsync<string>(ACTIVE_SESSIONS_KEY, sessionIdStr, cancellationToken: cancellationToken);

        await _stateStoreFactory.GetStore<Dictionary<string, string>>(StateStoreDefinitions.Permission)
            .SaveAsync(statesKey, sessionStates, options: GetSessionDataStateOptions(), cancellationToken: cancellationToken);

        var (permissionsChanged, newPermissions) = await RecompileSessionPermissionsAsync(sessionIdStr, sessionStates, "session_state_changed", cancellationToken: cancellationToken);

        return (StatusCodes.OK, new SessionUpdateResponse
        {
            PermissionsChanged = permissionsChanged,
            NewPermissions = newPermissions
        });
    }

    /// <summary>
    /// Update session role and recompile all service permissions.
    /// Uses distributed lock to prevent lost updates from concurrent modifications.
    /// </summary>
    public async Task<(StatusCodes, SessionUpdateResponse?)> UpdateSessionRoleAsync(
        SessionRoleUpdate body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Updating session {SessionId} role: {OldRole} -> {NewRole}",
            body.SessionId, body.PreviousRole, body.NewRole);

        var sessionIdStr = body.SessionId.ToString();
        var statesKey = string.Format(SESSION_STATES_KEY, sessionIdStr);

        // Distributed lock to prevent lost updates from concurrent session modifications
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.PermissionLock, sessionIdStr, $"permission-role:{Guid.NewGuid()}", _configuration.SessionLockTimeoutSeconds, cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogWarning("Failed to acquire lock for session {SessionId} role update", body.SessionId);
            return (StatusCodes.Conflict, null);
        }

        var sessionStates = await _stateStoreFactory.GetStore<Dictionary<string, string>>(StateStoreDefinitions.Permission)
            .GetAsync(statesKey, cancellationToken) ?? new Dictionary<string, string>();

        sessionStates[SESSION_ROLE_KEY] = body.NewRole;

        // Atomic add to activeSessions
        var cacheStore = _stateStoreFactory.GetCacheableStore<string>(StateStoreDefinitions.Permission);
        await cacheStore.AddToSetAsync<string>(ACTIVE_SESSIONS_KEY, sessionIdStr, cancellationToken: cancellationToken);

        await _stateStoreFactory.GetStore<Dictionary<string, string>>(StateStoreDefinitions.Permission)
            .SaveAsync(statesKey, sessionStates, options: GetSessionDataStateOptions(), cancellationToken: cancellationToken);

        var (permissionsChanged, newPermissions) = await RecompileSessionPermissionsAsync(sessionIdStr, sessionStates, "role_changed", cancellationToken: cancellationToken);

        return (StatusCodes.OK, new SessionUpdateResponse
        {
            PermissionsChanged = permissionsChanged,
            NewPermissions = newPermissions
        });
    }

    /// <summary>
    /// Clear session state for a specific service and recompile permissions.
    /// If states list is provided, only clears if current state matches one of the values.
    /// If states list is empty or not provided, clears the state unconditionally.
    /// </summary>
    public async Task<(StatusCodes, SessionUpdateResponse?)> ClearSessionStateAsync(
        ClearSessionStateRequest body,
        CancellationToken cancellationToken = default)
    {
        var statesKey = string.Format(SESSION_STATES_KEY, body.SessionId);

        var statesStore = _stateStoreFactory.GetStore<Dictionary<string, string>>(StateStoreDefinitions.Permission);
        var sessionStates = await statesStore.GetAsync(statesKey, cancellationToken);

        if (sessionStates == null || sessionStates.Count == 0)
        {
            _logger.LogDebug("No states to clear for session {SessionId}", body.SessionId);

            return (StatusCodes.OK, new SessionUpdateResponse
            {
                PermissionsChanged = false
            });
        }

        var sessionIdStr = body.SessionId.ToString();

        // If serviceId is null, clear ALL states for the session
        if (string.IsNullOrEmpty(body.ServiceId))
        {
            _logger.LogDebug("Clearing all {StateCount} states for session {SessionId}",
                sessionStates.Count, body.SessionId);

            sessionStates.Clear();
            await statesStore.SaveAsync(statesKey, sessionStates, options: GetSessionDataStateOptions(), cancellationToken: cancellationToken);
            var (changed, perms) = await RecompileSessionPermissionsAsync(sessionIdStr, sessionStates, "session_state_cleared_all", cancellationToken: cancellationToken);

            return (StatusCodes.OK, new SessionUpdateResponse
            {
                PermissionsChanged = changed,
                NewPermissions = perms
            });
        }

        // Clear specific service state
        if (!sessionStates.ContainsKey(body.ServiceId))
        {
            _logger.LogDebug("No state to clear for session {SessionId}, service {ServiceId}",
                body.SessionId, body.ServiceId);

            return (StatusCodes.OK, new SessionUpdateResponse
            {
                PermissionsChanged = false
            });
        }

        var currentState = sessionStates[body.ServiceId];

        // If states list is provided and non-empty, check if current state matches
        if (body.States != null && body.States.Count > 0)
        {
            if (!body.States.Contains(currentState))
            {
                _logger.LogDebug(
                    "State '{CurrentState}' for session {SessionId}, service {ServiceId} does not match filter {States}",
                    currentState, body.SessionId, body.ServiceId, string.Join(", ", body.States));

                return (StatusCodes.OK, new SessionUpdateResponse
                {
                    PermissionsChanged = false
                });
            }
        }

        _logger.LogDebug("Clearing state '{CurrentState}' for session {SessionId}, service {ServiceId}",
            currentState, body.SessionId, body.ServiceId);

        sessionStates.Remove(body.ServiceId);

        await statesStore.SaveAsync(statesKey, sessionStates, options: GetSessionDataStateOptions(), cancellationToken: cancellationToken);

        var (permChanged, newPerms) = await RecompileSessionPermissionsAsync(sessionIdStr, sessionStates, "session_state_cleared", cancellationToken: cancellationToken);

        return (StatusCodes.OK, new SessionUpdateResponse
        {
            PermissionsChanged = permChanged,
            NewPermissions = newPerms
        });
    }

    /// <summary>
    /// Get complete session information including states, role, and compiled permissions.
    /// </summary>
    public async Task<(StatusCodes, SessionInfo?)> GetSessionInfoAsync(
        SessionInfoRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting session info for {SessionId}", body.SessionId);

        var statesKey = string.Format(SESSION_STATES_KEY, body.SessionId);
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, body.SessionId);

        var statesTask = _stateStoreFactory.GetStore<Dictionary<string, string>>(StateStoreDefinitions.Permission)
            .GetAsync(statesKey, cancellationToken);
        var permissionsTask = _stateStoreFactory.GetStore<Dictionary<string, object>>(StateStoreDefinitions.Permission)
            .GetAsync(permissionsKey, cancellationToken);

        await Task.WhenAll(statesTask, permissionsTask);

        var states = await statesTask ?? new Dictionary<string, string>();
        var permissionsData = await permissionsTask ?? new Dictionary<string, object>();

        if (states.Count == 0)
        {
            _logger.LogDebug("No session info found for {SessionId}", body.SessionId);
            return (StatusCodes.NotFound, null);
        }

        var permissions = new Dictionary<string, ICollection<string>>();
        var version = 0;
        var lastUpdated = DateTimeOffset.UtcNow;

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
                DateTimeOffset.TryParse(item.Value.ToString(), out lastUpdated);
            }
            else
            {
                var jsonElement = (JsonElement)item.Value;
                var endpoints = BannouJson.Deserialize<List<string>>(jsonElement.GetRawText());
                if (endpoints != null)
                {
                    permissions[item.Key] = endpoints;
                }
            }
        }

        var defaultRole = _configuration.RoleHierarchy[0];

        var sessionInfo = new SessionInfo
        {
            States = states,
            Role = states.GetValueOrDefault(SESSION_ROLE_KEY, defaultRole),
            Permissions = permissions,
            Version = version,
            LastUpdated = lastUpdated
        };

        return (StatusCodes.OK, sessionInfo);
    }

    /// <summary>
    /// Recompile permissions for a session and publish update to Connect service.
    /// Reads session states from state store (used when states are not already in memory).
    /// </summary>
    private async Task<(bool PermissionsChanged, IDictionary<string, ICollection<string>>? NewPermissions)> RecompileSessionPermissionsAsync(
        string sessionId, string reason, CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.permission", "PermissionService.RecompileSessionPermissions");

        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
        var sessionStates = await _stateStoreFactory.GetStore<Dictionary<string, string>>(StateStoreDefinitions.Permission)
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
    /// <param name="reason">Reason for recompilation (for logging).</param>
    /// <param name="skipActiveConnectionsCheck">Skip activeConnections check in PublishCapabilityUpdateAsync (used when session just added).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task<(bool PermissionsChanged, IDictionary<string, ICollection<string>>? NewPermissions)> RecompileSessionPermissionsAsync(
        string sessionId,
        Dictionary<string, string> sessionStates,
        string reason,
        bool skipActiveConnectionsCheck = false,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.permission", "PermissionService.RecompileSessionPermissionsWithStates");

        try
        {
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

            var cacheStore = _stateStoreFactory.GetCacheableStore<string>(StateStoreDefinitions.Permission);
            var hashSetStore = _stateStoreFactory.GetStore<HashSet<string>>(StateStoreDefinitions.Permission);
            var registeredServices = await cacheStore.GetSetAsync<string>(REGISTERED_SERVICES_KEY, cancellationToken);

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
                        var endpoints = await hashSetStore.GetAsync(matrixKey, cancellationToken);

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
            var permissionsStore = _stateStoreFactory.GetStore<Dictionary<string, object>>(StateStoreDefinitions.Permission);
            var existingPermissions = await permissionsStore.GetAsync(permissionsKey, cancellationToken) ?? new Dictionary<string, object>();

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

            await permissionsStore.SaveAsync(permissionsKey, newPermissionData, options: GetSessionDataStateOptions(), cancellationToken: cancellationToken);

            await PublishCapabilityUpdateAsync(
                sessionId,
                compiledPermissions.ToDictionary(k => k.Key, v => (IEnumerable<string>)v.Value.ToList()),
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
            _logger.LogError(ex, "Error recompiling permissions for session {SessionId}", sessionId);
            await PublishErrorEventAsync(
                "RecompileSessionPermissions",
                "dependency_failure",
                ex.Message,
                dependency: "state",
                details: new { sessionId, reason });
            return (false, null);
        }
    }

    /// <summary>
    /// Publish compiled capabilities directly to the session via session-specific RabbitMQ channel.
    /// Connect service receives this via ClientEventRabbitMQSubscriber, generates client-salted GUIDs,
    /// and sends CapabilityManifestEvent to the client.
    /// Only publishes to sessions in activeConnections to avoid RabbitMQ exchange not_found crashes,
    /// unless skipActiveConnectionsCheck is true.
    /// </summary>
    private async Task PublishCapabilityUpdateAsync(
        string sessionId,
        Dictionary<string, IEnumerable<string>> permissions,
        string reason,
        bool skipActiveConnectionsCheck = false,
        CancellationToken cancellationToken = default)
    {
        using var activity = _telemetryProvider.StartActivity("bannou.permission", "PermissionService.PublishCapabilityUpdate");

        try
        {
            if (!skipActiveConnectionsCheck)
            {
                var cacheStore = _stateStoreFactory.GetCacheableStore<string>(StateStoreDefinitions.Permission);
                var isConnected = await cacheStore.SetContainsAsync<string>(ACTIVE_CONNECTIONS_KEY, sessionId, cancellationToken);

                if (!isConnected)
                {
                    _logger.LogDebug("Skipping capability publish for session {SessionId} - not in activeConnections (reason: {Reason})",
                        sessionId, reason);
                    return;
                }
            }

            var permissionsDict = permissions.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToList() as ICollection<string>);

            var capabilitiesEvent = new SessionCapabilitiesEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SessionId = Guid.Parse(sessionId),
                Permissions = permissionsDict,
                Reason = reason
            };

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
            _logger.LogError(ex, "Error publishing capabilities for session {SessionId}", sessionId);
            await PublishErrorEventAsync("PublishCapabilities", ex.GetType().Name, ex.Message, dependency: "messaging", details: new { SessionId = sessionId });
        }
    }

    private async Task PublishErrorEventAsync(
        string operation,
        string errorType,
        string message,
        string? dependency = null,
        object? details = null)
    {
        await _messageBus.TryPublishErrorAsync(
            serviceName: "permission",
            operation: operation,
            errorType: errorType,
            message: message,
            dependency: dependency,
            details: details);
    }

    /// <summary>
    /// Get list of all registered services with their registration information.
    /// </summary>
    public async Task<(StatusCodes, RegisteredServicesResponse?)> GetRegisteredServicesAsync(ListServicesRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting list of registered services");

        var cacheStore = _stateStoreFactory.GetCacheableStore<string>(StateStoreDefinitions.Permission);
        var registeredServiceIds = await cacheStore.GetSetAsync<string>(REGISTERED_SERVICES_KEY, cancellationToken);

        _logger.LogDebug("Found {Count} registered services: {Services}",
            registeredServiceIds.Count, string.Join(", ", registeredServiceIds));

        var services = new List<RegisteredServiceInfo>();
        var registrationStore = _stateStoreFactory.GetStore<ServiceRegistrationInfo>(StateStoreDefinitions.Permission);
        var hashSetStore = _stateStoreFactory.GetStore<HashSet<string>>(StateStoreDefinitions.Permission);

        foreach (var serviceId in registeredServiceIds)
        {
            var serviceRegisteredKey = string.Format(SERVICE_REGISTERED_KEY, serviceId);
            var registrationData = await registrationStore.GetAsync(serviceRegisteredKey, cancellationToken);

            var uniqueEndpoints = new HashSet<string>();

            var serviceStatesKey = string.Format(SERVICE_STATES_KEY, serviceId);
            var registeredStates = await hashSetStore.GetAsync(serviceStatesKey, cancellationToken);
            var states = registeredStates ?? new HashSet<string> { "default" };
            var roles = _configuration.RoleHierarchy;

            foreach (var state in states)
            {
                foreach (var role in roles)
                {
                    var matrixKey = string.Format(PERMISSION_MATRIX_KEY, serviceId, state, role);
                    var endpoints = await hashSetStore.GetAsync(matrixKey, cancellationToken);

                    if (endpoints != null)
                    {
                        foreach (var endpoint in endpoints)
                        {
                            uniqueEndpoints.Add(endpoint);
                        }
                    }
                }
            }

            var version = registrationData?.Version ?? "unknown";
            var registeredAt = registrationData?.RegisteredAtUnix > 0
                ? DateTimeOffset.FromUnixTimeSeconds(registrationData.RegisteredAtUnix)
                : DateTimeOffset.UtcNow;

            services.Add(new RegisteredServiceInfo
            {
                ServiceId = serviceId,
                ServiceName = serviceId,
                Version = version,
                RegisteredAt = registeredAt,
                EndpointCount = uniqueEndpoints.Count
            });
        }

        _logger.LogDebug("Returning {Count} registered services", services.Count);

        return (StatusCodes.OK, new RegisteredServicesResponse
        {
            Services = services,
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    #region Permission Registration

    /// <summary>
    /// IPermissionRegistry implementation: receives permission matrices from other services
    /// via direct DI call (push-based). Converts generic dictionary types to the generated
    /// ServicePermissionMatrix model and delegates to the existing API registration method.
    /// </summary>
    async Task IPermissionRegistry.RegisterServiceAsync(
        string serviceId,
        string version,
        Dictionary<string, IDictionary<string, ICollection<string>>> permissionMatrix)
    {
        var permissions = new Dictionary<string, StatePermissions>();
        foreach (var (stateKey, roleMap) in permissionMatrix)
        {
            var statePermissions = new StatePermissions();
            foreach (var (role, methods) in roleMap)
            {
                statePermissions[role] = new System.Collections.ObjectModel.Collection<string>(methods.ToList());
            }
            permissions[stateKey] = statePermissions;
        }

        var body = new ServicePermissionMatrix
        {
            ServiceId = serviceId,
            Version = version,
            Permissions = permissions
        };

        var (status, _) = await RegisterServicePermissionsAsync(body);
        if (status != StatusCodes.OK)
        {
            throw new InvalidOperationException(
                $"Permission registration failed for {serviceId}: status {status}");
        }
    }

    #endregion

    #region Session Connection Tracking

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
        var statesStore = _stateStoreFactory.GetStore<Dictionary<string, string>>(StateStoreDefinitions.Permission);
        var sessionStates = await statesStore.GetAsync(statesKey, cancellationToken) ?? new Dictionary<string, string>();

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

        await statesStore.SaveAsync(statesKey, sessionStates, options: GetSessionDataStateOptions(), cancellationToken: cancellationToken);

        var cacheStore = _stateStoreFactory.GetCacheableStore<string>(StateStoreDefinitions.Permission);
        var addedToConnections = await cacheStore.AddToSetAsync<string>(ACTIVE_CONNECTIONS_KEY, sessionId, cancellationToken: cancellationToken);
        if (addedToConnections)
        {
            var connectionCount = await cacheStore.SetCountAsync(ACTIVE_CONNECTIONS_KEY, cancellationToken);
            _logger.LogDebug("Added session {SessionId} to active connections. Total: {Count}",
                sessionId, connectionCount);
        }

        await cacheStore.AddToSetAsync<string>(ACTIVE_SESSIONS_KEY, sessionId, cancellationToken: cancellationToken);

        // skipActiveConnectionsCheck=true because we JUST added sessionId to activeConnections above
        await RecompileSessionPermissionsAsync(sessionId, sessionStates, "session_connected", skipActiveConnectionsCheck: true, cancellationToken: cancellationToken);

        return (StatusCodes.OK, new SessionUpdateResponse
        {
            PermissionsChanged = true
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

        var cacheStore = _stateStoreFactory.GetCacheableStore<string>(StateStoreDefinitions.Permission);
        var removed = await cacheStore.RemoveFromSetAsync<string>(ACTIVE_CONNECTIONS_KEY, sessionId, cancellationToken);
        if (removed)
        {
            var remainingCount = await cacheStore.SetCountAsync(ACTIVE_CONNECTIONS_KEY, cancellationToken);
            _logger.LogDebug("Removed session {SessionId} from active connections. Remaining: {Count}",
                sessionId, remainingCount);
        }

        if (!reconnectable)
        {
            await cacheStore.RemoveFromSetAsync<string>(ACTIVE_SESSIONS_KEY, sessionId, cancellationToken);

            await ClearSessionStateAsync(new ClearSessionStateRequest { SessionId = Guid.Parse(sessionId) }, cancellationToken);

            _logger.LogDebug("Cleared state for non-reconnectable session {SessionId}", sessionId);
        }

        return (StatusCodes.OK, new SessionUpdateResponse
        {
            PermissionsChanged = !reconnectable
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
        var cacheStore = _stateStoreFactory.GetCacheableStore<string>(StateStoreDefinitions.Permission);
        await cacheStore.AddToSetAsync<string>(ACTIVE_CONNECTIONS_KEY, sessionId, cancellationToken: cancellationToken);

        // Recompile from existing Redis state (preserved during reconnection window)
        // skipActiveConnectionsCheck=true because we JUST re-added sessionId to activeConnections above
        await RecompileSessionPermissionsAsync(sessionId, "session_reconnected", cancellationToken);

        _logger.LogDebug("Reconnection recompilation complete for session {SessionId}", sessionId);
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates StateOptions with Redis TTL for session data if configured.
    /// Returns null when SessionDataTtlSeconds is 0 (disabled).
    /// </summary>
    private StateOptions? GetSessionDataStateOptions()
    {
        if (_configuration.SessionDataTtlSeconds <= 0)
            return null;

        return new StateOptions { Ttl = _configuration.SessionDataTtlSeconds };
    }

    /// <summary>
    /// Determines the highest priority role from a collection of roles
    /// using the configured role hierarchy.
    /// </summary>
    private string DetermineHighestPriorityRole(ICollection<string>? roles)
    {
        var defaultRole = _configuration.RoleHierarchy[0];

        if (roles == null || roles.Count == 0)
        {
            return defaultRole;
        }

        // Walk the hierarchy from highest to lowest, return the first match
        for (var i = _configuration.RoleHierarchy.Length - 1; i >= 0; i--)
        {
            var hierarchyRole = _configuration.RoleHierarchy[i];
            if (roles.Any(r => r.Equals(hierarchyRole, StringComparison.OrdinalIgnoreCase)))
            {
                return hierarchyRole;
            }
        }

        // If roles exist but none are in the hierarchy, use the first one
        return roles.FirstOrDefault() ?? defaultRole;
    }

    #endregion
}
