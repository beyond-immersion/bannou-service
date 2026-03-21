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
    private readonly IMessageBus _messageBus;
    private readonly IClientEventPublisher _clientEventPublisher;
    private readonly ITelemetryProvider _telemetryProvider;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly RegistrationEventBatcher _registrationBatcher;

    // Constructor-cached state stores (FOUNDATION TENETS)
    private readonly IStateStore<Dictionary<string, object>> _permissionsStore;
    private readonly IStateStore<Dictionary<string, string>> _sessionStatesStore;
    private readonly IStateStore<string> _stringStore;
    private readonly IStateStore<HashSet<string>> _hashSetStore;
    private readonly IStateStore<ServiceRegistrationInfo> _registrationStore;
    private readonly ICacheableStateStore<string> _cacheStore;

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
        IEventConsumer eventConsumer,
        RegistrationEventBatcher registrationBatcher)
    {
        _logger = logger;
        _configuration = configuration;
        _messageBus = messageBus;
        _clientEventPublisher = clientEventPublisher;
        _telemetryProvider = telemetryProvider;
        _lockProvider = lockProvider;
        _registrationBatcher = registrationBatcher;

        _permissionsStore = stateStoreFactory.GetStore<Dictionary<string, object>>(StateStoreDefinitions.Permission);
        _sessionStatesStore = stateStoreFactory.GetStore<Dictionary<string, string>>(StateStoreDefinitions.Permission);
        _stringStore = stateStoreFactory.GetStore<string>(StateStoreDefinitions.Permission);
        _hashSetStore = stateStoreFactory.GetStore<HashSet<string>>(StateStoreDefinitions.Permission);
        _registrationStore = stateStoreFactory.GetStore<ServiceRegistrationInfo>(StateStoreDefinitions.Permission);
        _cacheStore = stateStoreFactory.GetCacheableStore<string>(StateStoreDefinitions.Permission);

        RegisterEventConsumers(eventConsumer);

        _logger.LogDebug("PermissionService initialized");
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
    /// <param name="body">The capability request containing the session ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Status code and capability response with allowed endpoints.</returns>
    public async Task<(StatusCodes, CapabilityResponse?)> GetCapabilitiesAsync(
        CapabilityRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting capabilities for session {SessionId}", body.SessionId);

        var sessionIdStr = body.SessionId.ToString();
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionIdStr);
        var permissionsData = await _permissionsStore
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
    /// <param name="body">The validation request with session, service, and endpoint.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Status code and validation response indicating access allowed or denied.</returns>
    public async Task<(StatusCodes, ValidationResponse?)> ValidateApiAccessAsync(
        ValidationRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Validating API access for session {SessionId}, service {ServiceId}, endpoint {Endpoint}",
            body.SessionId, body.ServiceId, body.Endpoint);

        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, body.SessionId);
        var permissionsData = await _permissionsStore
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
    /// <param name="body">The permission matrix defining service states, roles, and allowed endpoints.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Status code and registration response.</returns>
    public async Task<(StatusCodes, RegistrationResponse?)> RegisterServicePermissionsAsync(
        ServicePermissionMatrix body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Registering service permissions for {ServiceId} version {Version}",
            body.ServiceId, body.Version);

        var newHash = ComputePermissionDataHash(body);
        var hashKey = string.Format(PERMISSION_HASH_KEY, body.ServiceId);
        var storedHash = await _stringStore
            .GetAsync(hashKey, cancellationToken);


        var isServiceAlreadyRegistered = await _cacheStore.SetContainsAsync<string>(REGISTERED_SERVICES_KEY, body.ServiceId, cancellationToken);

        if (storedHash != null && storedHash == newHash && isServiceAlreadyRegistered)
        {
            _logger.LogDebug("Service {ServiceId} registration skipped - permission data unchanged and service already registered (hash: {Hash})",
                body.ServiceId, newHash[..8]);
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

                    var existingEndpoints = await _hashSetStore.GetAsync(matrixKey, cancellationToken) ?? new HashSet<string>();

                    foreach (var method in methods)
                    {
                        existingEndpoints.Add(method);
                    }

                    await _hashSetStore.SaveAsync(matrixKey, existingEndpoints, cancellationToken: cancellationToken);
                }
            }

            var serviceStatesKey = string.Format(SERVICE_STATES_KEY, body.ServiceId);
            await _hashSetStore.SaveAsync(serviceStatesKey, new HashSet<string>(body.Permissions.Keys), cancellationToken: cancellationToken);
        }
        else
        {
            _logger.LogWarning("No permissions to register for {ServiceId}", body.ServiceId);
        }

        if (body.Version != null)
        {
            await _sessionStatesStore
                .SaveAsync($"{PERMISSION_VERSION_KEY}:{body.ServiceId}", new Dictionary<string, string> { ["version"] = body.Version }, cancellationToken: cancellationToken);
        }

        var serviceRegisteredKey = string.Format(SERVICE_REGISTERED_KEY, body.ServiceId);
        var registrationInfo = new ServiceRegistrationInfo
        {
            ServiceId = body.ServiceId,
            Version = body.Version,
            RegisteredAt = DateTimeOffset.UtcNow
        };
        await _registrationStore
            .SaveAsync(serviceRegisteredKey, registrationInfo, cancellationToken: cancellationToken);
        _logger.LogInformation("Stored individual registration marker for {ServiceId} at key {Key}", body.ServiceId, serviceRegisteredKey);

        var added = await _cacheStore.AddToSetAsync<string>(REGISTERED_SERVICES_KEY, body.ServiceId, cancellationToken: cancellationToken);
        _logger.LogInformation("Service {ServiceId} {Action} registered services list",
            body.ServiceId, added ? "added to" : "already in");

        var activeSessions = await _cacheStore.GetSetAsync<string>(ACTIVE_SESSIONS_KEY, cancellationToken);

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
                    await using var lockResponse = await _lockProvider.LockAsync(
                        StateStoreDefinitions.PermissionLock, sessionId, $"permission-register:{Guid.NewGuid()}", _configuration.SessionLockTimeoutSeconds, cancellationToken);

                    if (!lockResponse.Success)
                    {
                        _logger.LogDebug("Skipping recompilation for session {SessionId} - lock contention during service registration", sessionId);
                        return;
                    }

                    await RecompileSessionPermissionsAsync(sessionId, CapabilityUpdateReason.ServiceRegistered, cancellationToken);
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

        await _stringStore
            .SaveAsync(hashKey, newHash, cancellationToken: cancellationToken);
        _logger.LogDebug("Stored permission hash for {ServiceId}: {Hash}",
            body.ServiceId, newHash[..8]);

        _registrationBatcher.Add(body.ServiceId, body.Version);

        return (StatusCodes.OK, new RegistrationResponse());
    }

    /// <summary>
    /// Update session state for specific service and recompile permissions.
    /// Uses distributed lock to prevent lost updates from concurrent modifications.
    /// </summary>
    /// <param name="body">The state update containing session ID, service ID, and new state.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Status code and session update response with recompiled permissions if changed.</returns>
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

        var sessionStates = await _sessionStatesStore
            .GetAsync(statesKey, cancellationToken) ?? new Dictionary<string, string>();

        sessionStates[body.ServiceId] = body.NewState;

        // Atomic add to activeSessions

        await _cacheStore.AddToSetAsync<string>(ACTIVE_SESSIONS_KEY, sessionIdStr, cancellationToken: cancellationToken);

        await _sessionStatesStore
            .SaveAsync(statesKey, sessionStates, options: GetSessionDataStateOptions(), cancellationToken: cancellationToken);

        var (_, newPermissions) = await RecompileSessionPermissionsAsync(sessionIdStr, sessionStates, CapabilityUpdateReason.SessionStateChanged, cancellationToken: cancellationToken);

        return (StatusCodes.OK, new SessionUpdateResponse
        {
            NewPermissions = newPermissions
        });
    }

    /// <summary>
    /// Update session role and recompile all service permissions.
    /// Uses distributed lock to prevent lost updates from concurrent modifications.
    /// </summary>
    /// <param name="body">The role update containing session ID and new role.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Status code and session update response with recompiled permissions if changed.</returns>
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

        var sessionStates = await _sessionStatesStore
            .GetAsync(statesKey, cancellationToken) ?? new Dictionary<string, string>();

        sessionStates[SESSION_ROLE_KEY] = body.NewRole;

        // Atomic add to activeSessions

        await _cacheStore.AddToSetAsync<string>(ACTIVE_SESSIONS_KEY, sessionIdStr, cancellationToken: cancellationToken);

        await _sessionStatesStore
            .SaveAsync(statesKey, sessionStates, options: GetSessionDataStateOptions(), cancellationToken: cancellationToken);

        var (_, newPermissions) = await RecompileSessionPermissionsAsync(sessionIdStr, sessionStates, CapabilityUpdateReason.RoleChanged, cancellationToken: cancellationToken);

        return (StatusCodes.OK, new SessionUpdateResponse
        {
            NewPermissions = newPermissions
        });
    }

    /// <summary>
    /// Clear session state for a specific service and recompile permissions.
    /// Uses distributed lock to prevent lost updates from concurrent modifications.
    /// If states list is provided, only clears if current state matches one of the values.
    /// If states list is empty or not provided, clears the state unconditionally.
    /// </summary>
    /// <param name="body">The request containing session ID, service ID, and optional state filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Status code and session update response with recompiled permissions if changed.</returns>
    public async Task<(StatusCodes, SessionUpdateResponse?)> ClearSessionStateAsync(
        ClearSessionStateRequest body,
        CancellationToken cancellationToken = default)
    {
        var sessionIdStr = body.SessionId.ToString();
        var statesKey = string.Format(SESSION_STATES_KEY, body.SessionId);

        // Distributed lock to prevent lost updates from concurrent session modifications
        await using var lockResponse = await _lockProvider.LockAsync(
            StateStoreDefinitions.PermissionLock, sessionIdStr, $"permission-clear:{Guid.NewGuid()}", _configuration.SessionLockTimeoutSeconds, cancellationToken);

        if (!lockResponse.Success)
        {
            _logger.LogWarning("Failed to acquire lock for session {SessionId} state clear", body.SessionId);
            return (StatusCodes.Conflict, null);
        }

        var sessionStates = await _sessionStatesStore.GetAsync(statesKey, cancellationToken);

        if (sessionStates == null || sessionStates.Count == 0)
        {
            _logger.LogDebug("No states to clear for session {SessionId}", body.SessionId);

            return (StatusCodes.OK, new SessionUpdateResponse
            {
                NewPermissions = null
            });
        }

        // If serviceId is null, clear ALL states for the session
        if (string.IsNullOrEmpty(body.ServiceId))
        {
            _logger.LogDebug("Clearing all {StateCount} states for session {SessionId}",
                sessionStates.Count, body.SessionId);

            sessionStates.Clear();
            await _sessionStatesStore.SaveAsync(statesKey, sessionStates, options: GetSessionDataStateOptions(), cancellationToken: cancellationToken);
            var (_, perms) = await RecompileSessionPermissionsAsync(sessionIdStr, sessionStates, CapabilityUpdateReason.SessionStateChanged, cancellationToken: cancellationToken);

            return (StatusCodes.OK, new SessionUpdateResponse
            {
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
                NewPermissions = null
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
                    NewPermissions = null
                });
            }
        }

        _logger.LogDebug("Clearing state '{CurrentState}' for session {SessionId}, service {ServiceId}",
            currentState, body.SessionId, body.ServiceId);

        sessionStates.Remove(body.ServiceId);

        await _sessionStatesStore.SaveAsync(statesKey, sessionStates, options: GetSessionDataStateOptions(), cancellationToken: cancellationToken);

        var (_, newPerms) = await RecompileSessionPermissionsAsync(sessionIdStr, sessionStates, CapabilityUpdateReason.SessionStateChanged, cancellationToken: cancellationToken);

        return (StatusCodes.OK, new SessionUpdateResponse
        {
            NewPermissions = newPerms
        });
    }

    /// <summary>
    /// Get complete session information including states, role, and compiled permissions.
    /// </summary>
    /// <param name="body">The request containing the session ID to query.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Status code and session info with states, role, and compiled permissions.</returns>
    public async Task<(StatusCodes, SessionInfo?)> GetSessionInfoAsync(
        SessionInfoRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting session info for {SessionId}", body.SessionId);

        var statesKey = string.Format(SESSION_STATES_KEY, body.SessionId);
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, body.SessionId);

        var statesTask = _sessionStatesStore
            .GetAsync(statesKey, cancellationToken);
        var permissionsTask = _permissionsStore
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
    /// Get list of all registered services with their registration information.
    /// </summary>
    /// <param name="body">The list services request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Status code and response containing registered services with endpoint counts.</returns>
    public async Task<(StatusCodes, RegisteredServicesResponse?)> GetRegisteredServicesAsync(ListServicesRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting list of registered services");


        var registeredServiceIds = await _cacheStore.GetSetAsync<string>(REGISTERED_SERVICES_KEY, cancellationToken);

        _logger.LogDebug("Found {Count} registered services: {Services}",
            registeredServiceIds.Count, string.Join(", ", registeredServiceIds));

        var services = new List<RegisteredServiceInfo>();



        foreach (var serviceId in registeredServiceIds)
        {
            var serviceRegisteredKey = string.Format(SERVICE_REGISTERED_KEY, serviceId);
            var registrationData = await _registrationStore.GetAsync(serviceRegisteredKey, cancellationToken);

            var uniqueEndpoints = new HashSet<string>();

            var serviceStatesKey = string.Format(SERVICE_STATES_KEY, serviceId);
            var registeredStates = await _hashSetStore.GetAsync(serviceStatesKey, cancellationToken);
            var states = registeredStates ?? new HashSet<string> { "default" };
            var roles = _configuration.RoleHierarchy;

            foreach (var state in states)
            {
                foreach (var role in roles)
                {
                    var matrixKey = string.Format(PERMISSION_MATRIX_KEY, serviceId, state, role);
                    var endpoints = await _hashSetStore.GetAsync(matrixKey, cancellationToken);

                    if (endpoints != null)
                    {
                        foreach (var endpoint in endpoints)
                        {
                            uniqueEndpoints.Add(endpoint);
                        }
                    }
                }
            }

            services.Add(new RegisteredServiceInfo
            {
                ServiceId = serviceId,
                ServiceName = serviceId,
                Version = registrationData?.Version ?? "unknown",
                RegisteredAt = registrationData?.RegisteredAt,
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
