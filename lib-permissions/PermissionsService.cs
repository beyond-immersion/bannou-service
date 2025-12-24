using BeyondImmersion.BannouService;
using BeyondImmersion.BannouService.Attributes;
using BeyondImmersion.BannouService.ClientEvents;
using BeyondImmersion.BannouService.Configuration;
using BeyondImmersion.BannouService.Events;
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
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace BeyondImmersion.BannouService.Permissions;

/// <summary>
/// Permissions service - authoritative source for all permission mappings and session capabilities.
/// Uses Dapr state store for atomic operations and Redis-backed data structures.
/// </summary>
[DaprService("permissions", typeof(IPermissionsService), lifetime: ServiceLifetime.Singleton)]
public partial class PermissionsService : IPermissionsService
{
    private readonly ILogger<PermissionsService> _logger;
    private readonly PermissionsServiceConfiguration _configuration;
    private readonly DaprClient _daprClient;
    private readonly IDistributedLockProvider _lockProvider;
    private readonly IErrorEventEmitter _errorEventEmitter;
    private readonly IClientEventPublisher _clientEventPublisher;
    private static readonly string[] ROLE_ORDER = new[] { "anonymous", "user", "developer", "admin" };

    // Dapr state store name
    private const string STATE_STORE = "permissions-store";

    // State key patterns
    private const string ACTIVE_SESSIONS_KEY = "active_sessions";
    private const string ACTIVE_CONNECTIONS_KEY = "active_connections"; // Tracks sessions with active WebSocket connections (exchange exists)
    private const string REGISTERED_SERVICES_KEY = "registered_services"; // List of all services that have registered permissions
    private const string SERVICE_REGISTERED_KEY = "service-registered:{0}"; // Individual service registration marker (race-condition safe)
    private const string SESSION_STATES_KEY = "session:{0}:states";
    private const string SESSION_PERMISSIONS_KEY = "session:{0}:permissions";
    private const string PERMISSION_MATRIX_KEY = "permissions:{0}:{1}:{2}"; // service:state:role
    private const string PERMISSION_VERSION_KEY = "permission_versions";
    private const string SERVICE_LOCK_KEY = "lock:service-registration:{0}";
    private const string PERMISSION_HASH_KEY = "permission_hash:{0}"; // Stores hash of service permission data for idempotent registration

    // Cache for compiled permissions (in-memory cache with Dapr state backing)
    private readonly ConcurrentDictionary<string, CapabilityResponse> _sessionCapabilityCache;

    public PermissionsService(
        ILogger<PermissionsService> logger,
        PermissionsServiceConfiguration configuration,
        DaprClient daprClient,
        IDistributedLockProvider lockProvider,
        IErrorEventEmitter errorEventEmitter,
        IClientEventPublisher clientEventPublisher,
        IEventConsumer eventConsumer)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _daprClient = daprClient ?? throw new ArgumentNullException(nameof(daprClient));
        _lockProvider = lockProvider ?? throw new ArgumentNullException(nameof(lockProvider));
        _errorEventEmitter = errorEventEmitter ?? throw new ArgumentNullException(nameof(errorEventEmitter));
        _clientEventPublisher = clientEventPublisher ?? throw new ArgumentNullException(nameof(clientEventPublisher));
        _sessionCapabilityCache = new ConcurrentDictionary<string, CapabilityResponse>();

        // Register event handlers via partial class (PermissionsServiceEvents.cs)
        RegisterEventConsumers(eventConsumer);

        _logger.LogInformation("Permissions service initialized with Dapr state store, session-specific client event publishing, and event handlers registered");
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
            _sessionCapabilityCache[body.SessionId] = response;

            _logger.LogInformation("Retrieved capabilities for session {SessionId} with {ServiceCount} services",
                body.SessionId, permissions.Count);

            return (StatusCodes.OK, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting capabilities for session {SessionId}", body.SessionId);
            await PublishErrorEventAsync(
                "GetCapabilities",
                "dependency_failure",
                ex.Message,
                dependency: "dapr-state",
                details: new { body.SessionId });
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
            var allowedEndpoints = BannouJson.Deserialize<List<string>>(jsonElement.GetRawText());
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
            await PublishErrorEventAsync(
                "ValidateApiAccess",
                "dependency_failure",
                ex.Message,
                dependency: "dapr-state",
                details: new { body.SessionId, body.ServiceId, body.Method });
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
            _logger.LogDebug("Registering service permissions for {ServiceId} version {Version}",
                body.ServiceId, body.Version);

            // IDEMPOTENT CHECK: Compute hash of incoming data and compare to stored hash
            // If the hash matches AND service is already registered, skip entirely
            var newHash = ComputePermissionDataHash(body);
            var hashKey = string.Format(PERMISSION_HASH_KEY, body.ServiceId);
            var storedHash = await _daprClient.GetStateAsync<string>(STATE_STORE, hashKey, cancellationToken: cancellationToken);

            // Also check if service is in registered_services (might be cleared independently of hash)
            var registeredServices = await _daprClient.GetStateAsync<HashSet<string>>(
                STATE_STORE, REGISTERED_SERVICES_KEY, cancellationToken: cancellationToken) ?? new HashSet<string>();
            var isServiceAlreadyRegistered = registeredServices.Contains(body.ServiceId);

            if (storedHash != null && storedHash == newHash && isServiceAlreadyRegistered)
            {
                _logger.LogDebug("Service {ServiceId} registration skipped - permission data unchanged and service already registered (hash: {Hash})",
                    body.ServiceId, newHash[..8] + "...");
                return (StatusCodes.OK, new RegistrationResponse
                {
                    ServiceId = body.ServiceId,
                    Success = true,
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

            // Use Dapr state transactions for atomic operations
            var transactionRequests = new List<StateTransactionRequest>();

            // Update permission matrix atomically
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
                        var existingEndpoints = await _daprClient.GetStateAsync<HashSet<string>>(
                            STATE_STORE, matrixKey, cancellationToken: cancellationToken) ?? new HashSet<string>();

                        foreach (var method in methods)
                        {
                            existingEndpoints.Add(method);
                        }

                        transactionRequests.Add(new StateTransactionRequest(
                            matrixKey, BannouJson.SerializeToUtf8Bytes(existingEndpoints), StateOperationType.Upsert));
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
                BannouJson.SerializeToUtf8Bytes(body.Version),
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
                RegisteredAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() // Store as Unix timestamp to avoid Dapr serialization bugs
            };
            await _daprClient.SaveStateAsync(STATE_STORE, serviceRegisteredKey, registrationInfo, cancellationToken: cancellationToken);
            _logger.LogInformation("Stored individual registration marker for {ServiceId} at key {Key}", body.ServiceId, serviceRegisteredKey);

            // Update the centralized registered_services list (enables "list all registered services" queries)
            // Uses Redis-based distributed lock to prevent race conditions when multiple services register concurrently
            // NO FALLBACK - if lock fails after retries, registration fails. We don't mask failures with alternative paths.
            // RETRY IS NOT A FALLBACK - retrying lock acquisition is the correct approach for handling lock contention.
            const string LOCK_STORE = "permissions-store"; // Use Redis state store for distributed locking
            const string LOCK_RESOURCE = "registered_services_lock";
            var lockOwnerId = $"{body.ServiceId}-{Guid.NewGuid():N}";

            // Acquire distributed lock with retry to handle concurrent registration contention
            // Lock contention is expected when multiple services register simultaneously (e.g., during tests or startup)
            const int maxRetries = 10;
            const int baseDelayMs = 100;
            ILockResponse? serviceLock = null;

            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    serviceLock = await _lockProvider.LockAsync(
                        LOCK_STORE,
                        LOCK_RESOURCE,
                        lockOwnerId,
                        expiryInSeconds: 30,  // Lock expires after 30 seconds if not released
                        cancellationToken: cancellationToken);

                    if (serviceLock.Success)
                    {
                        if (attempt > 0)
                        {
                            _logger.LogDebug("Acquired lock for {ServiceId} on attempt {Attempt}", body.ServiceId, attempt + 1);
                        }
                        break;
                    }

                    // Lock is held by another process - wait with exponential backoff and jitter
                    var delayMs = baseDelayMs * (1 << Math.Min(attempt, 5)) + Random.Shared.Next(0, 50);
                    _logger.LogDebug("Lock contention for {ServiceId}, attempt {Attempt}/{MaxRetries}, waiting {DelayMs}ms",
                        body.ServiceId, attempt + 1, maxRetries, delayMs);
                    await Task.Delay(delayMs, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Exception on lock attempt {Attempt} for {ServiceId}", attempt + 1, body.ServiceId);
                    if (attempt == maxRetries - 1)
                    {
                        _logger.LogError(ex, "Exception while acquiring distributed lock for {ServiceId} after {MaxRetries} attempts. " +
                            "Lock store: {LockStore}, Resource: {LockResource}, Owner: {LockOwner}",
                            body.ServiceId, maxRetries, LOCK_STORE, LOCK_RESOURCE, lockOwnerId);
                        return (StatusCodes.InternalServerError, new RegistrationResponse
                        {
                            ServiceId = body.ServiceId,
                            Success = false,
                            Message = $"Exception while acquiring lock after {maxRetries} attempts: {ex.Message}"
                        });
                    }
                    // Brief delay before retry on exception
                    await Task.Delay(baseDelayMs, cancellationToken);
                }
            }

            // Null check for safety - serviceLock should never be null after the loop
            // but we handle it explicitly to satisfy null safety requirements
            if (serviceLock == null)
            {
                _logger.LogError("Lock response was null for {ServiceId} - this should never happen", body.ServiceId);
                return (StatusCodes.InternalServerError, new RegistrationResponse
                {
                    ServiceId = body.ServiceId,
                    Success = false,
                    Message = "Lock acquisition returned null response"
                });
            }

            await using (serviceLock)
            {
                if (!serviceLock.Success)
                {
                    _logger.LogError("Failed to acquire distributed lock for {ServiceId} after {MaxRetries} attempts - " +
                        "cannot safely update registered services. This indicates persistent lock contention or Redis issues.",
                        body.ServiceId, maxRetries);
                    return (StatusCodes.InternalServerError, new RegistrationResponse
                    {
                        ServiceId = body.ServiceId,
                        Success = false,
                        Message = $"Failed to acquire distributed lock after {maxRetries} attempts - persistent lock contention"
                    });
                }

                _logger.LogInformation("Acquired lock for {ServiceId} to update registered services", body.ServiceId);

                // Now we have exclusive access - safe to read-modify-write
                var lockedServices = await _daprClient.GetStateAsync<HashSet<string>>(
                    STATE_STORE, REGISTERED_SERVICES_KEY, cancellationToken: cancellationToken) ?? new HashSet<string>();

                _logger.LogInformation("Current registered services (locked): {Services}",
                    string.Join(", ", lockedServices));

                if (!lockedServices.Contains(body.ServiceId))
                {
                    lockedServices.Add(body.ServiceId);
                    await _daprClient.SaveStateAsync(STATE_STORE, REGISTERED_SERVICES_KEY, lockedServices, cancellationToken: cancellationToken);
                    _logger.LogInformation("Successfully added {ServiceId} to registered services list. Now: {Services}",
                        body.ServiceId, string.Join(", ", lockedServices));
                }
                else
                {
                    _logger.LogInformation("Service {ServiceId} already in registered services list", body.ServiceId);
                }

                // Get all active sessions and recompile (for state tracking)
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

                // Note: RecompileSessionPermissionsAsync already handles publishing via PublishCapabilityUpdateAsync,
                // which checks activeConnections before publishing to avoid RabbitMQ crashes

                // Store the new hash for idempotent registration detection
                await _daprClient.SaveStateAsync(STATE_STORE, hashKey, newHash, cancellationToken: cancellationToken);
                _logger.LogDebug("Stored permission hash for {ServiceId}: {Hash}",
                    body.ServiceId, newHash[..8] + "...");

                return (StatusCodes.OK, new RegistrationResponse
                {
                    ServiceId = body.ServiceId,
                    Success = true,
                    Message = $"Registered {body.Permissions?.Count ?? 0} permission rules, recompiled {recompiledCount} sessions"
                });
            } // End of await using (serviceLock)
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error registering service permissions for {ServiceId}", body.ServiceId);
            await PublishErrorEventAsync(
                "RegisterServicePermissions",
                "dependency_failure",
                ex.Message,
                dependency: "dapr-state",
                details: new { body.ServiceId });
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
                new StateTransactionRequest(statesKey, BannouJson.SerializeToUtf8Bytes(sessionStates), StateOperationType.Upsert),
                new StateTransactionRequest(ACTIVE_SESSIONS_KEY, BannouJson.SerializeToUtf8Bytes(activeSessions), StateOperationType.Upsert)
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
            await PublishErrorEventAsync(
                "UpdateSessionState",
                "dependency_failure",
                ex.Message,
                dependency: "dapr-state",
                details: new { body.SessionId, body.ServiceId, body.NewState });
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
                new StateTransactionRequest(statesKey, BannouJson.SerializeToUtf8Bytes(sessionStates), StateOperationType.Upsert),
                new StateTransactionRequest(ACTIVE_SESSIONS_KEY, BannouJson.SerializeToUtf8Bytes(activeSessions), StateOperationType.Upsert)
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
            await PublishErrorEventAsync(
                "UpdateSessionRole",
                "dependency_failure",
                ex.Message,
                dependency: "dapr-state",
                details: new { body.SessionId, body.NewRole });
            return (StatusCodes.InternalServerError, null);
        }
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
        try
        {
            var statesKey = string.Format(SESSION_STATES_KEY, body.SessionId);

            // Get current session states
            var sessionStates = await _daprClient.GetStateAsync<Dictionary<string, string>>(
                STATE_STORE, statesKey, cancellationToken: cancellationToken);

            if (sessionStates == null || !sessionStates.ContainsKey(body.ServiceId))
            {
                _logger.LogInformation("No state to clear for session {SessionId}, service {ServiceId}",
                    body.SessionId, body.ServiceId);

                return (StatusCodes.OK, new SessionUpdateResponse
                {
                    SessionId = body.SessionId,
                    Success = true,
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
                    _logger.LogInformation(
                        "State '{CurrentState}' for session {SessionId}, service {ServiceId} does not match filter {States}",
                        currentState, body.SessionId, body.ServiceId, string.Join(", ", body.States));

                    return (StatusCodes.OK, new SessionUpdateResponse
                    {
                        SessionId = body.SessionId,
                        Success = true,
                        PermissionsChanged = false,
                        Message = $"Current state '{currentState}' does not match filter; not cleared"
                    });
                }
            }

            _logger.LogInformation("Clearing state '{CurrentState}' for session {SessionId}, service {ServiceId}",
                currentState, body.SessionId, body.ServiceId);

            // Remove the state
            sessionStates.Remove(body.ServiceId);

            // Atomic update
            var transactionRequests = new List<StateTransactionRequest>
            {
                new StateTransactionRequest(statesKey, BannouJson.SerializeToUtf8Bytes(sessionStates), StateOperationType.Upsert)
            };

            await _daprClient.ExecuteStateTransactionAsync(STATE_STORE, transactionRequests, cancellationToken: cancellationToken);

            // Recompile session permissions
            await RecompileSessionPermissionsAsync(body.SessionId, sessionStates, "session_state_cleared");

            return (StatusCodes.OK, new SessionUpdateResponse
            {
                SessionId = body.SessionId,
                Success = true,
                PermissionsChanged = true,
                Message = $"Cleared state '{currentState}' for service {body.ServiceId}"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing session state for {SessionId}", body.SessionId);
            await PublishErrorEventAsync(
                "ClearSessionState",
                "dependency_failure",
                ex.Message,
                dependency: "dapr-state",
                details: new { body.SessionId, body.ServiceId });
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting session info for {SessionId}", body.SessionId);
            await PublishErrorEventAsync(
                "GetSessionInfo",
                "dependency_failure",
                ex.Message,
                dependency: "dapr-state",
                details: new { body.SessionId });
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
    ///
    /// Permission state key matching:
    /// - "default" state permissions are always included for the session's role
    /// - For each session state (e.g., game-session=in_game), checks all registered services
    /// - State key construction matches registration logic:
    ///   - Same service (stateServiceId == serviceId): stateKey = stateValue (e.g., "in_game")
    ///   - Cross-service (stateServiceId != serviceId): stateKey = "{stateServiceId}:{stateValue}" (e.g., "game-session:in_game")
    /// </summary>
    /// <param name="sessionId">Session ID to recompile permissions for.</param>
    /// <param name="sessionStates">Session states dictionary (avoids re-reading from Dapr).</param>
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
            _logger.LogInformation("Recompiling permissions for session {SessionId}, role: {Role}, reason: {Reason}",
                sessionId, role, reason);

            // Compile permissions for each service
            var compiledPermissions = new Dictionary<string, HashSet<string>>();

            // First, get all registered services and check "default" state permissions for the role
            var registeredServices = await _daprClient.GetStateAsync<HashSet<string>>(
                STATE_STORE, REGISTERED_SERVICES_KEY) ?? new HashSet<string>();

            _logger.LogInformation("Found {Count} registered services: {Services}",
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
                        var endpoints = await _daprClient.GetStateAsync<HashSet<string>>(STATE_STORE, matrixKey);

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
                newPermissionData[kvp.Key] = kvp.Value.ToList();
            }
            newPermissionData["version"] = newVersion;
            newPermissionData["generated_at"] = DateTimeOffset.UtcNow.ToString();

            await _daprClient.SaveStateAsync(STATE_STORE, permissionsKey, newPermissionData);

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
                dependency: "dapr-state",
                details: new { sessionId, reason });
        }
    }

    /// <summary>
    /// Publish compiled capabilities directly to the session via session-specific RabbitMQ channel.
    /// Connect service receives this via ClientEventRabbitMQSubscriber, generates client-salted GUIDs,
    /// and sends CapabilityManifestEvent to the client. No API callback required.
    /// CRITICAL: Only publishes to sessions in activeConnections to avoid RabbitMQ exchange not_found crashes,
    /// unless skipActiveConnectionsCheck is true (used when called from HandleSessionConnectedAsync where we
    /// just added the session to activeConnections and want to avoid Dapr read-after-write consistency issues).
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
                var activeConnections = await _daprClient.GetStateAsync<HashSet<string>>(
                    STATE_STORE, ACTIVE_CONNECTIONS_KEY) ?? new HashSet<string>();

                if (!activeConnections.Contains(sessionId))
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
                Event_name = SessionCapabilitiesEventEvent_name.Permissions_session_capabilities,
                Event_id = Guid.NewGuid(),
                Timestamp = DateTimeOffset.UtcNow,
                Session_id = sessionId,
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
        }
    }

    private Task PublishErrorEventAsync(
        string operation,
        string errorType,
        string message,
        string? dependency = null,
        object? details = null)
    {
        return _errorEventEmitter.TryPublishAsync(
            serviceId: "permissions",
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
        try
        {
            _logger.LogDebug("Getting list of registered services");

            // Get all registered service IDs
            var registeredServiceIds = await _daprClient.GetStateAsync<HashSet<string>>(
                STATE_STORE, REGISTERED_SERVICES_KEY, cancellationToken: cancellationToken) ?? new HashSet<string>();

            _logger.LogDebug("Found {Count} registered services: {Services}",
                registeredServiceIds.Count, string.Join(", ", registeredServiceIds));

            var services = new List<RegisteredServiceInfo>();

            foreach (var serviceId in registeredServiceIds)
            {
                // Get individual registration info for this service
                var serviceRegisteredKey = string.Format(SERVICE_REGISTERED_KEY, serviceId);
                var registrationData = await _daprClient.GetStateAsync<Dictionary<string, object>>(
                    STATE_STORE, serviceRegisteredKey, cancellationToken: cancellationToken);

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
                        var endpoints = await _daprClient.GetStateAsync<HashSet<string>>(
                            STATE_STORE, matrixKey, cancellationToken: cancellationToken);

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

                // Parse registration data
                var version = "";
                var registeredAt = DateTimeOffset.UtcNow;

                if (registrationData != null)
                {
                    if (registrationData.TryGetValue("Version", out var versionObj) && versionObj != null)
                    {
                        version = versionObj.ToString() ?? "";
                    }
                    // Read Unix timestamp (new format)
                    if (registrationData.TryGetValue("RegisteredAtUnix", out var registeredAtUnixObj) && registeredAtUnixObj != null)
                    {
                        long registeredAtUnix = 0;
                        if (registeredAtUnixObj is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Number)
                        {
                            registeredAtUnix = jsonElement.GetInt64();
                        }
                        else if (long.TryParse(registeredAtUnixObj.ToString(), out var parsedLong))
                        {
                            registeredAtUnix = parsedLong;
                        }
                        if (registeredAtUnix > 0)
                        {
                            registeredAt = DateTimeOffset.FromUnixTimeSeconds(registeredAtUnix);
                        }
                    }
                    // Fallback to old DateTimeOffset format for backward compatibility
                    else if (registrationData.TryGetValue("RegisteredAt", out var registeredAtObj) && registeredAtObj != null)
                    {
                        if (registeredAtObj is JsonElement jsonElement)
                        {
                            DateTimeOffset.TryParse(jsonElement.GetString(), out registeredAt);
                        }
                        else
                        {
                            DateTimeOffset.TryParse(registeredAtObj.ToString(), out registeredAt);
                        }
                    }
                }

                services.Add(new RegisteredServiceInfo
                {
                    ServiceId = serviceId,
                    ServiceName = serviceId, // Use serviceId as name if not stored separately
                    Version = version,
                    RegisteredAt = registeredAt,
                    EndpointCount = endpointCount
                });
            }

            _logger.LogInformation("Returning {Count} registered services", services.Count);

            return (StatusCodes.OK, new RegisteredServicesResponse
            {
                Services = services,
                Timestamp = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting registered services");
            await PublishErrorEventAsync(
                "GetRegisteredServices",
                "dependency_failure",
                ex.Message,
                dependency: "dapr-state",
                details: null);
            return (StatusCodes.InternalServerError, null);
        }
    }

    #region Permission Registration

    /// <summary>
    /// Registers this service's API permissions with the Permissions service on startup.
    /// Even though Permissions service publishes to itself, this follows the same pattern
    /// as other services for consistency.
    /// </summary>
    public async Task RegisterServicePermissionsAsync()
    {
        _logger.LogInformation("Registering Permissions service permissions...");
        await PermissionsPermissionRegistration.RegisterViaEventAsync(_daprClient, _logger);
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
    /// <param name="authorizations">Authorization states from JWT (e.g., ["arcadia:authorized"]).</param>
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
            _logger.LogInformation("Handling session connected: {SessionId} for account {AccountId} with {RoleCount} roles and {AuthCount} authorizations",
                sessionId, accountId, roles?.Count ?? 0, authorizations?.Count ?? 0);

            // Build session states dictionary with role and authorizations
            // This is the format expected by RecompileSessionPermissionsAsync
            var statesKey = string.Format(SESSION_STATES_KEY, sessionId);
            var sessionStates = await _daprClient.GetStateAsync<Dictionary<string, string>>(
                STATE_STORE, statesKey, cancellationToken: cancellationToken) ?? new Dictionary<string, string>();

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

            // Store session states atomically
            await _daprClient.SaveStateAsync(STATE_STORE, statesKey, sessionStates, cancellationToken: cancellationToken);

            // Add to activeConnections (sessions with WebSocket connections where exchange exists)
            var activeConnections = await _daprClient.GetStateAsync<HashSet<string>>(
                STATE_STORE, ACTIVE_CONNECTIONS_KEY, cancellationToken: cancellationToken) ?? new HashSet<string>();

            if (!activeConnections.Contains(sessionId))
            {
                activeConnections.Add(sessionId);
                await _daprClient.SaveStateAsync(STATE_STORE, ACTIVE_CONNECTIONS_KEY, activeConnections, cancellationToken: cancellationToken);
                _logger.LogDebug("Added session {SessionId} to active connections. Total: {Count}",
                    sessionId, activeConnections.Count);
            }

            // Also ensure session is in activeSessions for state tracking
            var activeSessions = await _daprClient.GetStateAsync<HashSet<string>>(
                STATE_STORE, ACTIVE_SESSIONS_KEY, cancellationToken: cancellationToken) ?? new HashSet<string>();

            if (!activeSessions.Contains(sessionId))
            {
                activeSessions.Add(sessionId);
                await _daprClient.SaveStateAsync(STATE_STORE, ACTIVE_SESSIONS_KEY, activeSessions, cancellationToken: cancellationToken);
            }

            // Compile and publish initial capabilities for this session using the states we just built
            // This overload avoids read-after-write consistency issues
            // RecompileSessionPermissionsAsync calls PublishCapabilityUpdateAsync which sends
            // SessionCapabilitiesEvent with actual permissions data to Connect
            // CRITICAL: skipActiveConnectionsCheck=true because we JUST added sessionId to activeConnections above
            // and Dapr state store has eventual consistency - re-reading might not show the session yet
            await RecompileSessionPermissionsAsync(sessionId, sessionStates, "session_connected", skipActiveConnectionsCheck: true);

            return (StatusCodes.OK, new SessionUpdateResponse
            {
                Success = true,
                SessionId = sessionId,
                Message = "Session connection registered and capabilities published"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle session connected for {SessionId}", sessionId);
            return (StatusCodes.InternalServerError, new SessionUpdateResponse
            {
                Success = false,
                SessionId = sessionId,
                Message = $"Error: {ex.Message}"
            });
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
            _logger.LogInformation("Handling session disconnected: {SessionId}, Reconnectable: {Reconnectable}",
                sessionId, reconnectable);

            // Remove from activeConnections (no longer has WebSocket/exchange)
            var activeConnections = await _daprClient.GetStateAsync<HashSet<string>>(
                STATE_STORE, ACTIVE_CONNECTIONS_KEY, cancellationToken: cancellationToken) ?? new HashSet<string>();

            if (activeConnections.Contains(sessionId))
            {
                activeConnections.Remove(sessionId);
                await _daprClient.SaveStateAsync(STATE_STORE, ACTIVE_CONNECTIONS_KEY, activeConnections, cancellationToken: cancellationToken);
                _logger.LogDebug("Removed session {SessionId} from active connections. Remaining: {Count}",
                    sessionId, activeConnections.Count);
            }

            // If not reconnectable, also remove from activeSessions and clear session state
            if (!reconnectable)
            {
                var activeSessions = await _daprClient.GetStateAsync<HashSet<string>>(
                    STATE_STORE, ACTIVE_SESSIONS_KEY, cancellationToken: cancellationToken) ?? new HashSet<string>();

                if (activeSessions.Contains(sessionId))
                {
                    activeSessions.Remove(sessionId);
                    await _daprClient.SaveStateAsync(STATE_STORE, ACTIVE_SESSIONS_KEY, activeSessions, cancellationToken: cancellationToken);
                }

                // Clear session state and permissions cache
                await ClearSessionStateAsync(new ClearSessionStateRequest { SessionId = sessionId }, cancellationToken);
                _sessionCapabilityCache.TryRemove(sessionId, out _);

                _logger.LogDebug("Cleared state for non-reconnectable session {SessionId}", sessionId);
            }

            return (StatusCodes.OK, new SessionUpdateResponse
            {
                Success = true,
                SessionId = sessionId,
                Message = reconnectable
                    ? "Session connection removed (reconnectable)"
                    : "Session connection removed and state cleared"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle session disconnected for {SessionId}", sessionId);
            return (StatusCodes.InternalServerError, new SessionUpdateResponse
            {
                Success = false,
                SessionId = sessionId,
                Message = $"Error: {ex.Message}"
            });
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Determines the highest priority role from a collection of roles.
    /// Priority: admin > user > anonymous
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
