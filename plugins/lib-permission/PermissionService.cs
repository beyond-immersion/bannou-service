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
using System.Collections.Concurrent;
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
    private static readonly string[] ROLE_ORDER = new[] { "anonymous", "user", "developer", "admin" };

    // State key patterns
    private const string ACTIVE_SESSIONS_KEY = "active_sessions";
    private const string ACTIVE_CONNECTIONS_KEY = "active_connections"; // Tracks sessions with active WebSocket connections (exchange exists)
    private const string REGISTERED_SERVICES_KEY = "registered_services"; // List of all services that have registered permissions
    private const string SERVICE_REGISTERED_KEY = "service-registered:{0}"; // Individual service registration marker (race-condition safe)
    private const string SESSION_STATES_KEY = "session:{0}:states";
    private const string SESSION_PERMISSIONS_KEY = "session:{0}:permissions";
    private const string PERMISSION_MATRIX_KEY = "permissions:{0}:{1}:{2}"; // service:state:role
    private const string PERMISSION_VERSION_KEY = "permission_versions";
    private const string PERMISSION_HASH_KEY = "permission_hash:{0}"; // Stores hash of service permission data for idempotent registration

    // Cache for compiled permissions (in-memory cache with lib-state backing)
    private readonly ConcurrentDictionary<string, CapabilityResponse> _sessionCapabilityCache;

    public PermissionService(
        ILogger<PermissionService> logger,
        PermissionServiceConfiguration configuration,
        IStateStoreFactory stateStoreFactory,
        IMessageBus messageBus,
        IClientEventPublisher clientEventPublisher,
        IEventConsumer eventConsumer)
    {
        _logger = logger;
        _configuration = configuration;
        _stateStoreFactory = stateStoreFactory;
        _messageBus = messageBus;
        _clientEventPublisher = clientEventPublisher;
        _sessionCapabilityCache = new ConcurrentDictionary<string, CapabilityResponse>();

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
            // Sort by state name for deterministic ordering
            foreach (var stateEntry in body.Permissions.OrderBy(s => s.Key))
            {
                builder.Append($"s:{stateEntry.Key}[");

                // Sort by role name
                foreach (var roleEntry in stateEntry.Value.OrderBy(r => r.Key))
                {
                    builder.Append($"r:{roleEntry.Key}(");

                    // Sort endpoints
                    builder.Append(string.Join(",", roleEntry.Value.OrderBy(e => e)));
                    builder.Append(')');
                }

                builder.Append(']');
            }
        }

        // Compute SHA256 hash
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Get compiled capabilities for a session from lib-state store with in-memory caching.
    /// </summary>
    public async Task<(StatusCodes, CapabilityResponse?)> GetCapabilitiesAsync(
        CapabilityRequest body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting capabilities for session {SessionId}", body.SessionId);

        // Check in-memory cache first
        var sessionIdStr = body.SessionId.ToString();
        if (_sessionCapabilityCache.TryGetValue(sessionIdStr, out var cachedResponse))
        {
            // Check in-memory cache TTL (0 = disabled, cache never expires)
            if (_configuration.PermissionCacheTtlSeconds > 0)
            {
                var age = DateTimeOffset.UtcNow - cachedResponse.GeneratedAt;
                if (age.TotalSeconds > _configuration.PermissionCacheTtlSeconds)
                {
                    _logger.LogDebug(
                        "Cached capabilities for session {SessionId} expired (age: {AgeSeconds}s, TTL: {TtlSeconds}s), refreshing from Redis",
                        body.SessionId, (int)age.TotalSeconds, _configuration.PermissionCacheTtlSeconds);
                    _sessionCapabilityCache.TryRemove(sessionIdStr, out _);
                    // Fall through to Redis read below
                }
                else
                {
                    _logger.LogDebug("Returning cached capabilities for session {SessionId}", body.SessionId);
                    return (StatusCodes.OK, cachedResponse);
                }
            }
            else
            {
                _logger.LogDebug("Returning cached capabilities for session {SessionId}", body.SessionId);
                return (StatusCodes.OK, cachedResponse);
            }
        }

        // Get compiled permissions from state store
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, sessionIdStr);
        var permissionsData = await _stateStoreFactory.GetStore<Dictionary<string, object>>(StateStoreDefinitions.Permission)
            .GetAsync(permissionsKey, cancellationToken);

        if (permissionsData == null || permissionsData.Count == 0)
        {
            _logger.LogDebug("No permissions found for session {SessionId}", body.SessionId);
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
                var endpoints = BannouJson.Deserialize<List<string>>(jsonElement.GetRawText());
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
        _sessionCapabilityCache[sessionIdStr] = response;

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

        // Get session permissions from state store
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
                SessionId = body.SessionId
            });
        }

        // Parse allowed endpoints
        var jsonElement = (JsonElement)permissionsData[body.ServiceId];
        var allowedEndpoints = BannouJson.Deserialize<List<string>>(jsonElement.GetRawText());
        var allowed = allowedEndpoints?.Contains(body.Endpoint) ?? false;

        _logger.LogDebug("API access validation result for session {SessionId}: {Allowed}",
            body.SessionId, allowed);

        return (StatusCodes.OK, new ValidationResponse
        {
            Allowed = allowed,
            SessionId = body.SessionId
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

        // IDEMPOTENT CHECK: Compute hash of incoming data and compare to stored hash
        // If the hash matches AND service is already registered, skip entirely
        var newHash = ComputePermissionDataHash(body);
        var hashKey = string.Format(PERMISSION_HASH_KEY, body.ServiceId);
        var storedHash = await _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Permission)
            .GetAsync(hashKey, cancellationToken);

        // Atomic membership check - no read-modify-write needed
        var cacheStore = _stateStoreFactory.GetCacheableStore<string>(StateStoreDefinitions.Permission);
        var isServiceAlreadyRegistered = await cacheStore.SetContainsAsync<string>(REGISTERED_SERVICES_KEY, body.ServiceId, cancellationToken);

        if (storedHash != null && storedHash == newHash && isServiceAlreadyRegistered)
        {
            _logger.LogDebug("Service {ServiceId} registration skipped - permission data unchanged and service already registered (hash: {Hash})",
                body.ServiceId, newHash[..8] + "...");
            return (StatusCodes.OK, new RegistrationResponse
            {
                ServiceId = body.ServiceId,
                Registered = true,
                AffectedSessions = 0,
                Message = "Permissions unchanged (idempotent)"
            });
        }

        // Log why we're proceeding
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

        // Update permission matrix - save each permission set
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

                    // Get existing endpoints and merge with new ones
                    var existingEndpoints = await hashSetStore.GetAsync(matrixKey, cancellationToken) ?? new HashSet<string>();

                    foreach (var method in methods)
                    {
                        existingEndpoints.Add(method);
                    }

                    await hashSetStore.SaveAsync(matrixKey, existingEndpoints, cancellationToken: cancellationToken);
                }
            }
        }
        else
        {
            _logger.LogWarning("No permissions to register for {ServiceId}", body.ServiceId);
        }

        // Update service version (wrap in object for state store compatibility)
        await _stateStoreFactory.GetStore<Dictionary<string, string>>(StateStoreDefinitions.Permission)
            .SaveAsync($"{PERMISSION_VERSION_KEY}:{body.ServiceId}", new Dictionary<string, string> { ["version"] = body.Version }, cancellationToken: cancellationToken);

        // Track this service using individual key pattern (race-condition safe)
        // Each service has its own key, eliminating the need to modify a shared list
        var serviceRegisteredKey = string.Format(SERVICE_REGISTERED_KEY, body.ServiceId);
        var registrationInfo = new ServiceRegistrationInfo
        {
            ServiceId = body.ServiceId,
            Version = body.Version,
            RegisteredAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };
        await _stateStoreFactory.GetStore<ServiceRegistrationInfo>(StateStoreDefinitions.Permission)
            .SaveAsync(serviceRegisteredKey, registrationInfo, cancellationToken: cancellationToken);
        _logger.LogInformation("Stored individual registration marker for {ServiceId} at key {Key}", body.ServiceId, serviceRegisteredKey);

        // Atomic add to registered_services set - no lock needed, SADD is inherently atomic
        var added = await cacheStore.AddToSetAsync<string>(REGISTERED_SERVICES_KEY, body.ServiceId, cancellationToken: cancellationToken);
        _logger.LogInformation("Service {ServiceId} {Action} registered services list",
            body.ServiceId, added ? "added to" : "already in");

        // Recompile permissions for all active sessions (parallel with configurable concurrency)
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
                    await RecompileSessionPermissionsAsync(sessionId, "service_registered");
                    Interlocked.Increment(ref recompiledCount);
                }
                finally
                {
                    semaphore.Release();
                }
            });
            // RecompileSessionPermissionsAsync handles its own exceptions (logged + error event published)
            // so Task.WhenAll won't throw -- individual session failures don't abort the batch
            await Task.WhenAll(tasks);
        }

        stopwatch.Stop();

        _logger.LogInformation(
            "Service {ServiceId} registered successfully, recompiled {Count} sessions in {ElapsedMs}ms (concurrency: {Concurrency})",
            body.ServiceId, recompiledCount, stopwatch.ElapsedMilliseconds, _configuration.MaxConcurrentRecompilations);

        // Store the new hash for idempotent registration detection
        await _stateStoreFactory.GetStore<string>(StateStoreDefinitions.Permission)
            .SaveAsync(hashKey, newHash, cancellationToken: cancellationToken);
        _logger.LogDebug("Stored permission hash for {ServiceId}: {Hash}",
            body.ServiceId, newHash[..8] + "...");

        return (StatusCodes.OK, new RegistrationResponse
        {
            ServiceId = body.ServiceId,
            Registered = true,
            AffectedSessions = recompiledCount,
            Message = $"Registered {body.Permissions?.Count ?? 0} permission rules, recompiled {recompiledCount} sessions"
        });
    }

    /// <summary>
    /// Update session state for specific service and recompile permissions.
    /// Uses lib-state atomic transactions for consistency.
    /// </summary>
    public async Task<(StatusCodes, SessionUpdateResponse?)> UpdateSessionStateAsync(
        SessionStateUpdate body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Updating session {SessionId} state for service {ServiceId}: {OldState} → {NewState}",
            body.SessionId, body.ServiceId, body.PreviousState, body.NewState);

        var statesKey = string.Format(SESSION_STATES_KEY, body.SessionId);
        var permissionsKey = string.Format(SESSION_PERMISSIONS_KEY, body.SessionId);

        // Get current session states
        var sessionStates = await _stateStoreFactory.GetStore<Dictionary<string, string>>(StateStoreDefinitions.Permission)
            .GetAsync(statesKey, cancellationToken) ?? new Dictionary<string, string>();

        // Get current permissions data for version increment
        var permissionsData = await _stateStoreFactory.GetStore<Dictionary<string, object>>(StateStoreDefinitions.Permission)
            .GetAsync(permissionsKey, cancellationToken) ?? new Dictionary<string, object>();

        // Update session state
        sessionStates[body.ServiceId] = body.NewState;

        // Increment version
        var currentVersion = 0;
        if (permissionsData.ContainsKey("version"))
        {
            int.TryParse(permissionsData["version"]?.ToString(), out currentVersion);
        }
        var newVersion = currentVersion + 1;

        // Atomic add to activeSessions - SADD is inherently safe for concurrent access
        var cacheStore = _stateStoreFactory.GetCacheableStore<string>(StateStoreDefinitions.Permission);
        await cacheStore.AddToSetAsync<string>(ACTIVE_SESSIONS_KEY, body.SessionId.ToString(), cancellationToken: cancellationToken);

        // Save session state (with Redis TTL for orphaned session cleanup)
        await _stateStoreFactory.GetStore<Dictionary<string, string>>(StateStoreDefinitions.Permission)
            .SaveAsync(statesKey, sessionStates, options: GetSessionDataStateOptions(), cancellationToken: cancellationToken);

        // Recompile session permissions using the states we already have
        // (avoids read-after-write consistency issues by not re-reading from state store)
        await RecompileSessionPermissionsAsync(body.SessionId.ToString(), sessionStates, "session_state_changed");

        return (StatusCodes.OK, new SessionUpdateResponse
        {
            SessionId = body.SessionId,
            Message = $"Updated {body.ServiceId} state to {body.NewState}, version {newVersion}"
        });
    }

    /// <summary>
    /// Update session role and recompile all service permissions.
    /// </summary>
    public async Task<(StatusCodes, SessionUpdateResponse?)> UpdateSessionRoleAsync(
        SessionRoleUpdate body,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Updating session {SessionId} role: {OldRole} → {NewRole}",
            body.SessionId, body.PreviousRole, body.NewRole);

        var statesKey = string.Format(SESSION_STATES_KEY, body.SessionId);

        // Get current session states
        var sessionStates = await _stateStoreFactory.GetStore<Dictionary<string, string>>(StateStoreDefinitions.Permission)
            .GetAsync(statesKey, cancellationToken) ?? new Dictionary<string, string>();

        // Update role
        sessionStates["role"] = body.NewRole;

        // Atomic add to activeSessions - SADD is inherently safe for concurrent access
        var cacheStore = _stateStoreFactory.GetCacheableStore<string>(StateStoreDefinitions.Permission);
        await cacheStore.AddToSetAsync<string>(ACTIVE_SESSIONS_KEY, body.SessionId.ToString(), cancellationToken: cancellationToken);

        // Save session states (with Redis TTL for orphaned session cleanup)
        await _stateStoreFactory.GetStore<Dictionary<string, string>>(StateStoreDefinitions.Permission)
            .SaveAsync(statesKey, sessionStates, options: GetSessionDataStateOptions(), cancellationToken: cancellationToken);

        // Recompile all permissions for this session using the states we already have
        // (avoids read-after-write consistency issues by not re-reading from state store)
        await RecompileSessionPermissionsAsync(body.SessionId.ToString(), sessionStates, "role_changed");

        return (StatusCodes.OK, new SessionUpdateResponse
        {
            SessionId = body.SessionId,
            Message = $"Updated role to {body.NewRole}"
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

        // Get current session states
        var statesStore = _stateStoreFactory.GetStore<Dictionary<string, string>>(StateStoreDefinitions.Permission);
        var sessionStates = await statesStore.GetAsync(statesKey, cancellationToken);

        // If no states exist, nothing to clear
        if (sessionStates == null || sessionStates.Count == 0)
        {
            _logger.LogDebug("No states to clear for session {SessionId}", body.SessionId);

            return (StatusCodes.OK, new SessionUpdateResponse
            {
                SessionId = body.SessionId,
                PermissionsChanged = false,
                Message = "No states were set for this session"
            });
        }

        // If serviceId is null, clear ALL states for the session
        if (string.IsNullOrEmpty(body.ServiceId))
        {
            var stateCount = sessionStates.Count;
            _logger.LogDebug("Clearing all {StateCount} states for session {SessionId}",
                stateCount, body.SessionId);

            sessionStates.Clear();
            await statesStore.SaveAsync(statesKey, sessionStates, options: GetSessionDataStateOptions(), cancellationToken: cancellationToken);
            await RecompileSessionPermissionsAsync(body.SessionId.ToString(), sessionStates, "session_state_cleared_all");

            return (StatusCodes.OK, new SessionUpdateResponse
            {
                SessionId = body.SessionId,
                PermissionsChanged = true,
                Message = $"Cleared all {stateCount} states for session"
            });
        }

        // Clear specific service state
        if (!sessionStates.ContainsKey(body.ServiceId))
        {
            _logger.LogDebug("No state to clear for session {SessionId}, service {ServiceId}",
                body.SessionId, body.ServiceId);

            return (StatusCodes.OK, new SessionUpdateResponse
            {
                SessionId = body.SessionId,
                PermissionsChanged = false,
                Message = $"No state was set for service {body.ServiceId}"
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
                    SessionId = body.SessionId,
                    PermissionsChanged = false,
                    Message = $"Current state '{currentState}' does not match filter; not cleared"
                });
            }
        }

        _logger.LogDebug("Clearing state '{CurrentState}' for session {SessionId}, service {ServiceId}",
            currentState, body.SessionId, body.ServiceId);

        // Remove the state
        sessionStates.Remove(body.ServiceId);

        // Save updated session states (with Redis TTL for orphaned session cleanup)
        await statesStore.SaveAsync(statesKey, sessionStates, options: GetSessionDataStateOptions(), cancellationToken: cancellationToken);

        // Recompile session permissions
        await RecompileSessionPermissionsAsync(body.SessionId.ToString(), sessionStates, "session_state_cleared");

        return (StatusCodes.OK, new SessionUpdateResponse
        {
            SessionId = body.SessionId,
            PermissionsChanged = true,
            Message = $"Cleared state '{currentState}' for service {body.ServiceId}"
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

        // Get session states and permissions concurrently
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
                var endpoints = BannouJson.Deserialize<List<string>>(jsonElement.GetRawText());
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

    /// <summary>
    /// Recompile permissions for a session and publish update to Connect service.
    /// Reads session states from state store (used when states are not already in memory).
    /// </summary>
    private async Task RecompileSessionPermissionsAsync(string sessionId, string reason)
    {
        // Get session states from state store
        var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
        var sessionStates = await _stateStoreFactory.GetStore<Dictionary<string, string>>(StateStoreDefinitions.Permission)
            .GetAsync(statesKey);

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
    ///
    /// Permission state key matching:
    /// - "default" state permissions are always included for the session's role
    /// - For each session state (e.g., game-session=in_game), checks all registered services
    /// - State key construction matches registration logic:
    ///   - Same service (stateServiceId == serviceId): stateKey = stateValue (e.g., "in_game")
    ///   - Cross-service (stateServiceId != serviceId): stateKey = "{stateServiceId}:{stateValue}" (e.g., "game-session:in_game")
    /// </summary>
    /// <param name="sessionId">Session ID to recompile permissions for.</param>
    /// <param name="sessionStates">Session states dictionary (avoids re-reading from state store).</param>
    /// <param name="reason">Reason for recompilation (for logging).</param>
    /// <param name="skipActiveConnectionsCheck">Skip activeConnections check in PublishCapabilityUpdateAsync (used when session just added).</param>
    private async Task RecompileSessionPermissionsAsync(
        string sessionId,
        Dictionary<string, string> sessionStates,
        string reason,
        bool skipActiveConnectionsCheck = false)
    {
        try
        {
            if (sessionStates == null || sessionStates.Count == 0)
            {
                _logger.LogDebug("No session states provided for {SessionId}, skipping recompilation", sessionId);
                return;
            }

            var role = sessionStates.GetValueOrDefault("role", "user");
            _logger.LogDebug("Recompiling permissions for session {SessionId}, role: {Role}, reason: {Reason}",
                sessionId, role, reason);

            // Compile permissions for each service
            var compiledPermissions = new Dictionary<string, HashSet<string>>();

            // First, get all registered services and check "default" state permissions for the role
            var cacheStore = _stateStoreFactory.GetCacheableStore<string>(StateStoreDefinitions.Permission);
            var hashSetStore = _stateStoreFactory.GetStore<HashSet<string>>(StateStoreDefinitions.Permission);
            var registeredServices = await cacheStore.GetSetAsync<string>(REGISTERED_SERVICES_KEY);

            _logger.LogDebug("Found {Count} registered services: {Services}",
                registeredServices.Count, string.Join(", ", registeredServices));

            foreach (var serviceId in registeredServices)
            {
                // Relevant state keys for this service
                var relevantStates = new List<string> { "default" };
                foreach (var serviceState in sessionStates.Where(s => s.Key != "role"))
                {
                    var stateServiceId = serviceState.Key;
                    var stateValue = serviceState.Value;
                    if (stateValue == "default") continue;
                    relevantStates.Add(stateServiceId == serviceId ? stateValue : $"{stateServiceId}:{stateValue}");
                }

                foreach (var stateKey in relevantStates)
                {
                    var maxRoleByEndpoint = new Dictionary<string, int>();

                    // Walk all roles to find the highest required role for each endpoint in this state
                    foreach (var roleName in ROLE_ORDER)
                    {
                        var matrixKey = string.Format(PERMISSION_MATRIX_KEY, serviceId, stateKey, roleName);
                        var endpoints = await hashSetStore.GetAsync(matrixKey);

                        _logger.LogDebug("State-based lookup: service={ServiceId}, stateKey={StateKey}, role={Role}, key={Key}, found={Count}",
                            serviceId, stateKey, roleName, matrixKey, endpoints?.Count ?? 0);

                        if (endpoints == null) continue;

                        foreach (var endpoint in endpoints)
                        {
                            var priority = Array.IndexOf(ROLE_ORDER, roleName);
                            maxRoleByEndpoint[endpoint] = maxRoleByEndpoint.TryGetValue(endpoint, out var existing)
                                ? Math.Max(existing, priority)
                                : priority;
                        }
                    }

                    // Allow endpoints where session role meets or exceeds highest required
                    var sessionPriority = Array.IndexOf(ROLE_ORDER, role);
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
            var existingPermissions = await permissionsStore.GetAsync(permissionsKey) ?? new Dictionary<string, object>();

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
            newPermissionData["generated_at"] = DateTimeOffset.UtcNow.ToString();

            await permissionsStore.SaveAsync(permissionsKey, newPermissionData, options: GetSessionDataStateOptions());

            // Clear in-memory cache after write
            _sessionCapabilityCache.TryRemove(sessionId, out _);

            // Publish capability update to Connect service
            await PublishCapabilityUpdateAsync(
                sessionId,
                compiledPermissions.ToDictionary(k => k.Key, v => (IEnumerable<string>)v.Value.ToList()),
                reason,
                skipActiveConnectionsCheck);

            _logger.LogInformation("Recompiled permissions for session {SessionId}: {ServiceCount} services, version {Version}",
                sessionId, compiledPermissions.Count, newVersion);
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
        }
    }

    /// <summary>
    /// Publish compiled capabilities directly to the session via session-specific RabbitMQ channel.
    /// Connect service receives this via ClientEventRabbitMQSubscriber, generates client-salted GUIDs,
    /// and sends CapabilityManifestEvent to the client. No API callback required.
    /// CRITICAL: Only publishes to sessions in activeConnections to avoid RabbitMQ exchange not_found crashes,
    /// unless skipActiveConnectionsCheck is true (used when called from HandleSessionConnectedAsync where we
    /// just added the session to activeConnections and want to avoid state store read-after-write consistency issues).
    /// </summary>
    private async Task PublishCapabilityUpdateAsync(
        string sessionId,
        Dictionary<string, IEnumerable<string>> permissions,
        string reason,
        bool skipActiveConnectionsCheck = false)
    {
        try
        {
            // CRITICAL: Only publish to sessions with active WebSocket connections
            // Publishing to sessions without connections causes RabbitMQ channel crash (exchange not_found)
            // Skip this check when called from HandleSessionConnectedAsync (we just added the session)
            if (!skipActiveConnectionsCheck)
            {
                var cacheStore = _stateStoreFactory.GetCacheableStore<string>(StateStoreDefinitions.Permission);
                var isConnected = await cacheStore.SetContainsAsync<string>(ACTIVE_CONNECTIONS_KEY, sessionId);

                if (!isConnected)
                {
                    _logger.LogDebug("Skipping capability publish for session {SessionId} - not in activeConnections (reason: {Reason})",
                        sessionId, reason);
                    return;
                }
            }

            // Convert permissions to the format expected by SessionCapabilitiesEvent
            var permissionsDict = permissions.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToList() as ICollection<string>);

            // Create SessionCapabilitiesEvent with actual permissions data
            var capabilitiesEvent = new SessionCapabilitiesEvent
            {
                EventId = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                SessionId = Guid.Parse(sessionId),
                Permissions = permissionsDict,
                Reason = reason
            };

            // Publish directly to session-specific channel
            var published = await _clientEventPublisher.PublishToSessionAsync(sessionId, capabilitiesEvent);

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
    /// Used by testers to poll for service readiness - services only register after they're ready to handle requests.
    /// </summary>
    public async Task<(StatusCodes, RegisteredServicesResponse?)> GetRegisteredServicesAsync(ListServicesRequest body, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Getting list of registered services");

        // Get all registered service IDs via atomic set read
        var cacheStore = _stateStoreFactory.GetCacheableStore<string>(StateStoreDefinitions.Permission);
        var registeredServiceIds = await cacheStore.GetSetAsync<string>(REGISTERED_SERVICES_KEY, cancellationToken);

        _logger.LogDebug("Found {Count} registered services: {Services}",
            registeredServiceIds.Count, string.Join(", ", registeredServiceIds));

        var services = new List<RegisteredServiceInfo>();
        var registrationStore = _stateStoreFactory.GetStore<ServiceRegistrationInfo>(StateStoreDefinitions.Permission);
        var hashSetStore = _stateStoreFactory.GetStore<HashSet<string>>(StateStoreDefinitions.Permission);

        foreach (var serviceId in registeredServiceIds)
        {
            // Get individual registration info for this service
            var serviceRegisteredKey = string.Format(SERVICE_REGISTERED_KEY, serviceId);
            var registrationData = await registrationStore.GetAsync(serviceRegisteredKey, cancellationToken);

            // Count endpoints for this service by scanning permission matrix keys
            // We look for all state/role combinations and sum unique endpoints
            var endpointCount = 0;
            var uniqueEndpoints = new HashSet<string>();

            // Check common states and roles
            var states = new[] { "authenticated", "default", "lobby", "in_game" };
            var roles = new[] { "user", "admin", "anonymous" };

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
            endpointCount = uniqueEndpoints.Count;

            // Extract registration data from typed model
            var version = registrationData?.Version ?? "";
            var registeredAt = registrationData?.RegisteredAtUnix > 0
                ? DateTimeOffset.FromUnixTimeSeconds(registrationData.RegisteredAtUnix)
                : DateTimeOffset.UtcNow;

            services.Add(new RegisteredServiceInfo
            {
                ServiceId = serviceId,
                ServiceName = serviceId, // Use serviceId as name if not stored separately
                Version = version,
                RegisteredAt = registeredAt,
                EndpointCount = endpointCount
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
    /// ServicePermissionMatrix model and delegates to the existing API registration method
    /// which handles idempotency, Redis storage, and session recompilation.
    /// </summary>
    async Task IPermissionRegistry.RegisterServiceAsync(
        string serviceId,
        string version,
        Dictionary<string, IDictionary<string, ICollection<string>>> permissionMatrix)
    {
        // Convert from generic dictionary types to generated ServicePermissionMatrix model types
        // BuildPermissionMatrix returns Dictionary<string, IDictionary<string, ICollection<string>>>
        // but ServicePermissionMatrix.Permissions is IDictionary<string, StatePermissions>
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
    /// CRITICAL: This method should only be called AFTER the RabbitMQ exchange exists.
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
        try
        {
            _logger.LogDebug("Handling session connected: {SessionId} for account {AccountId} with {RoleCount} roles and {AuthCount} authorizations",
                sessionId, accountId, roles?.Count ?? 0, authorizations?.Count ?? 0);

            // Build session states dictionary with role and authorizations
            // This is the format expected by RecompileSessionPermissionsAsync
            var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
            var statesStore = _stateStoreFactory.GetStore<Dictionary<string, string>>(StateStoreDefinitions.Permission);
            var sessionStates = await statesStore.GetAsync(statesKey, cancellationToken) ?? new Dictionary<string, string>();

            // Add role to session states
            var role = DetermineHighestPriorityRole(roles);
            sessionStates["role"] = role;
            _logger.LogDebug("Set role '{Role}' for session {SessionId}", role, sessionId);

            // Add authorization states (format: "{serviceId}:{state}" -> stored as serviceId=state)
            if (authorizations != null && authorizations.Count > 0)
            {
                foreach (var auth in authorizations)
                {
                    var parts = auth.Split(':');
                    if (parts.Length == 2)
                    {
                        var serviceId = parts[0];
                        var state = parts[1];
                        sessionStates[serviceId] = state;
                        _logger.LogDebug("Set authorization state '{State}' for service '{ServiceId}' on session {SessionId}",
                            state, serviceId, sessionId);
                    }
                }
            }

            // Store session states (with Redis TTL for orphaned session cleanup)
            await statesStore.SaveAsync(statesKey, sessionStates, options: GetSessionDataStateOptions(), cancellationToken: cancellationToken);

            // Atomic add to activeConnections and activeSessions - SADD is inherently safe for concurrent access
            var cacheStore = _stateStoreFactory.GetCacheableStore<string>(StateStoreDefinitions.Permission);
            var addedToConnections = await cacheStore.AddToSetAsync<string>(ACTIVE_CONNECTIONS_KEY, sessionId, cancellationToken: cancellationToken);
            if (addedToConnections)
            {
                var connectionCount = await cacheStore.SetCountAsync(ACTIVE_CONNECTIONS_KEY, cancellationToken);
                _logger.LogDebug("Added session {SessionId} to active connections. Total: {Count}",
                    sessionId, connectionCount);
            }

            await cacheStore.AddToSetAsync<string>(ACTIVE_SESSIONS_KEY, sessionId, cancellationToken: cancellationToken);

            // Compile and publish initial capabilities for this session using the states we just built
            // This overload avoids read-after-write consistency issues
            // RecompileSessionPermissionsAsync calls PublishCapabilityUpdateAsync which sends
            // SessionCapabilitiesEvent with actual permissions data to Connect
            // CRITICAL: skipActiveConnectionsCheck=true because we JUST added sessionId to activeConnections above
            // and state store has eventual consistency - re-reading might not show the session yet
            await RecompileSessionPermissionsAsync(sessionId, sessionStates, "session_connected", skipActiveConnectionsCheck: true);

            return (StatusCodes.OK, new SessionUpdateResponse
            {
                SessionId = Guid.Parse(sessionId),
                Message = "Session connection registered and capabilities published"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle session connected for {SessionId}", sessionId);
            await PublishErrorEventAsync("HandleSessionConnected", ex.GetType().Name, ex.Message, dependency: "state", details: new { SessionId = sessionId });
            return (StatusCodes.InternalServerError, null);
        }
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
        try
        {
            _logger.LogDebug("Handling session disconnected: {SessionId}, Reconnectable: {Reconnectable}",
                sessionId, reconnectable);

            // Atomic remove from activeConnections - SREM is inherently safe for concurrent access
            var cacheStore = _stateStoreFactory.GetCacheableStore<string>(StateStoreDefinitions.Permission);
            var removed = await cacheStore.RemoveFromSetAsync<string>(ACTIVE_CONNECTIONS_KEY, sessionId, cancellationToken);
            if (removed)
            {
                var remainingCount = await cacheStore.SetCountAsync(ACTIVE_CONNECTIONS_KEY, cancellationToken);
                _logger.LogDebug("Removed session {SessionId} from active connections. Remaining: {Count}",
                    sessionId, remainingCount);
            }

            // If not reconnectable, also remove from activeSessions and clear session state
            if (!reconnectable)
            {
                await cacheStore.RemoveFromSetAsync<string>(ACTIVE_SESSIONS_KEY, sessionId, cancellationToken);

                // Clear session state and permissions cache
                await ClearSessionStateAsync(new ClearSessionStateRequest { SessionId = Guid.Parse(sessionId) }, cancellationToken);
                _sessionCapabilityCache.TryRemove(sessionId, out _);

                _logger.LogDebug("Cleared state for non-reconnectable session {SessionId}", sessionId);
            }

            return (StatusCodes.OK, new SessionUpdateResponse
            {
                SessionId = Guid.Parse(sessionId),
                Message = reconnectable
                    ? "Session connection removed (reconnectable)"
                    : "Session connection removed and state cleared"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle session disconnected for {SessionId}", sessionId);
            await PublishErrorEventAsync("HandleSessionDisconnected", ex.GetType().Name, ex.Message, dependency: "state", details: new { SessionId = sessionId });
            return (StatusCodes.InternalServerError, null);
        }
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
    /// Determines the highest priority role from a collection of roles.
    /// Priority: admin > developer > user > anonymous
    /// </summary>
    private static string DetermineHighestPriorityRole(ICollection<string>? roles)
    {
        if (roles == null || roles.Count == 0)
        {
            return "anonymous";
        }

        // Check for admin role (highest priority)
        if (roles.Any(r => r.Equals("admin", StringComparison.OrdinalIgnoreCase)))
        {
            return "admin";
        }

        // Check for developer role
        if (roles.Any(r => r.Equals("developer", StringComparison.OrdinalIgnoreCase)))
        {
            return "developer";
        }

        // Check for user role
        if (roles.Any(r => r.Equals("user", StringComparison.OrdinalIgnoreCase)))
        {
            return "user";
        }

        // If roles exist but none are recognized, use the first one
        return roles.FirstOrDefault() ?? "anonymous";
    }

    #endregion
}

// ============================================================================
// Internal Data Models
// ============================================================================

/// <summary>
/// Internal storage model for service registration information.
/// Replaces anonymous type to ensure reliable serialization/deserialization.
/// </summary>
internal class ServiceRegistrationInfo
{
    public string ServiceId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public long RegisteredAtUnix { get; set; }
}
