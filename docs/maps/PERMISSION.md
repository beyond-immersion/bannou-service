# Permission Implementation Map

> **Plugin**: lib-permission
> **Schema**: schemas/permission-api.yaml
> **Layer**: AppFoundation
> **Deep Dive**: [docs/plugins/PERMISSION.md](../plugins/PERMISSION.md)

---

## Summary

| Field | Value |
|-------|-------|
| Plugin | lib-permission |
| Layer | L1 AppFoundation |
| Endpoints | 8 |
| State Stores | permission (Redis), permission-lock (Redis) |
| Events Published | 1 (permission.capability-update) |
| Events Consumed | 1 (session.updated) |
| Client Events | 1 (SessionCapabilitiesEvent) |
| Background Services | 0 |

---

## State

**Store**: `permission` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `active_sessions` | Set of string | All sessions that have registered state (superset of connected) |
| `active_connections` | Set of string | Sessions with active WebSocket connections (safe to push to) |
| `registered_services` | Set of string | All service IDs that have registered permission matrices |
| `service-registered:{serviceId}` | `ServiceRegistrationInfo` | Per-service registration marker with timestamp |
| `session:{sessionId}:states` | `Dictionary<string, string>` | Per-session state map (role key + per-service states) |
| `session:{sessionId}:permissions` | `Dictionary<string, object>` | Compiled permission map (service → endpoint list + version/generated_at metadata) |
| `permissions:{serviceId}:{state}:{role}` | `HashSet<string>` | Permission matrix entries (allowed endpoints for combination) |
| `permission_versions:{serviceId}` | `Dictionary<string, string>` | Per-service version tracking |
| `permission_hash:{serviceId}` | `string` | SHA-256 hash for idempotent registration detection |
| `service-states:{serviceId}` | `HashSet<string>` | State names registered by each service |

Session keys (`session:*`) use TTL from `SessionDataTtlSeconds` (default 600s), refreshed by heartbeats via `ISessionActivityListener`.

**Store**: `permission-lock` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `{sessionId}` | Lock | Distributed lock for session state/role mutations |

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (`IStateStoreFactory`) | L0 | Hard | All Redis state — sessions, permissions, matrix, indexes, set operations |
| lib-state (`IDistributedLockProvider`) | L0 | Hard | Session mutation locks (UpdateSessionState, UpdateSessionRole) |
| lib-messaging (`IMessageBus`) | L0 | Hard | Error event publishing via TryPublishErrorAsync |
| lib-messaging (`IEventConsumer`) | L0 | Hard | Subscription to session.updated from Auth |
| lib-messaging (`IClientEventPublisher`) | L0 | Hard | Push SessionCapabilitiesEvent to session channels |
| lib-telemetry (`ITelemetryProvider`) | L0 | Hard | Span instrumentation on all async helpers |

No service client dependencies. Permission is a pure receiver — callers invoke it, it invokes no other services.

**DI interfaces implemented:**

| Interface | Class | Purpose |
|-----------|-------|---------|
| `IPermissionRegistry` | `PermissionService` (Singleton) | Push-based permission matrix registration at startup (all plugins → Permission) |
| `ISessionActivityListener` | `PermissionSessionActivityListener` (Singleton) | Session lifecycle + heartbeat TTL refresh (Connect → Permission in-process) |

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `permission.capability-update` | `PermissionCapabilityUpdate` | Published on every session permission recompilation (service registration, state change, role change, connect, reconnect) |

---

## Events Consumed

| Topic | Handler | Action |
|-------|---------|--------|
| `session.updated` | `HandleSessionUpdatedAsync` | Extracts highest-priority role → UpdateSessionRole; iterates authorizations → UpdateSessionState per authorization |

Session connected/disconnected/reconnected/heartbeat received via `ISessionActivityListener` DI dispatch (not event subscriptions), since Connect and Permission are both L1 (always co-located).

---

## DI Services

| Service | Role |
|---------|------|
| `ILogger<PermissionService>` | Structured logging |
| `PermissionServiceConfiguration` | RoleHierarchy, TTL, lock timeout, max recompilations |
| `IStateStoreFactory` | Redis state access (acquired inline per-method, not cached in constructor) |
| `IDistributedLockProvider` | Distributed locks for session mutation |
| `IMessageBus` | Service event publishing (permission.capability-update) + error events |
| `IClientEventPublisher` | Session-specific capability push |
| `IEventConsumer` | Event handler registration (session.updated) |
| `ITelemetryProvider` | Trace span creation |
| `PermissionSessionActivityListener` | DI listener for session lifecycle and heartbeat TTL |

---

## Method Index

| Method | Route | Roles | Mutates | Publishes |
|--------|-------|-------|---------|-----------|
| GetCapabilities | POST /permission/capabilities | [] | - | - |
| ValidateApiAccess | POST /permission/validate | [] | - | - |
| RegisterServicePermissions | POST /permission/register-service | [] | matrix, hash, version, registration, service-states, session-permissions | permission.capability-update, PUSH session-capabilities |
| UpdateSessionState | POST /permission/update-session-state | [admin] | session-states, session-permissions | permission.capability-update, PUSH session-capabilities |
| UpdateSessionRole | POST /permission/update-session-role | [] | session-states, session-permissions | permission.capability-update, PUSH session-capabilities |
| ClearSessionState | POST /permission/clear-session-state | [] | session-states, session-permissions | permission.capability-update, PUSH session-capabilities |
| GetSessionInfo | POST /permission/get-session-info | [] | - | - |
| GetRegisteredServices | POST /permission/services/list | [admin] | - | - |

---

## Methods

### GetCapabilities
POST /permission/capabilities | Roles: []

```
READ permission:"session:{sessionId}:permissions"                  -> 404 if null or empty
FOREACH key in permissions (excluding "version", "generated_at" metadata keys)
  // Deserialize value as List<string> endpoint names
RETURN (200, CapabilityResponse { permissions, generatedAt })
```

### ValidateApiAccess
POST /permission/validate | Roles: []

```
READ permission:"session:{sessionId}:permissions"
IF permissions null OR serviceId not present
  RETURN (200, ValidationResponse { allowed: false, reason: "No permissions registered" })
// Deserialize endpoint list for requested service
IF endpoint in allowed list
  RETURN (200, ValidationResponse { allowed: true })
ELSE
  RETURN (200, ValidationResponse { allowed: false, reason: "Endpoint not in allowed list" })
```

// Always returns 200. Access denial is in the response body, not status codes.

### RegisterServicePermissions
POST /permission/register-service | Roles: []

```
// Compute SHA-256 hash of permission data
READ permission:"permission_hash:{serviceId}"
READ permission:registered_services CONTAINS serviceId
IF hash matches AND service already registered
  RETURN (200, RegistrationResponse {})                            // Idempotent skip

FOREACH state in body.permissions
  FOREACH role in state.permissions
    READ permission:"permissions:{serviceId}:{state}:{role}"       // Existing endpoints
    WRITE permission:"permissions:{serviceId}:{state}:{role}" <- merged endpoint set

WRITE permission:"service-states:{serviceId}" <- HashSet of state names
WRITE permission:"permission_versions:{serviceId}" <- { version }
WRITE permission:"service-registered:{serviceId}" <- ServiceRegistrationInfo { timestamp }
WRITE permission:registered_services ADD serviceId                 // Atomic SADD

// Fan-out recompilation to all active sessions
READ permission:active_sessions -> sessionIds
FOREACH sessionId in sessionIds (parallel, bounded by MaxConcurrentRecompilations)
  // see helper: RecompileSessionPermissions

WRITE permission:"permission_hash:{serviceId}" <- newHash         // After recompilation completes
RETURN (200, RegistrationResponse {})
```

### UpdateSessionState
POST /permission/update-session-state | Roles: [admin]

```
LOCK permission-lock:"{sessionId}" timeout=SessionLockTimeoutSeconds
                                                                   -> 409 if lock fails
  READ permission:"session:{sessionId}:states"                     // null -> new empty dict
  WRITE permission:active_sessions ADD sessionId
  // Set states[serviceId] = newState
  WRITE permission:"session:{sessionId}:states" <- updated [TTL: SessionDataTtlSeconds]
  // see helper: RecompileSessionPermissions (with in-memory states)
RETURN (200, SessionUpdateResponse { permissionsChanged, newPermissions })
```

### UpdateSessionRole
POST /permission/update-session-role | Roles: []

```
LOCK permission-lock:"{sessionId}" timeout=SessionLockTimeoutSeconds
                                                                   -> 409 if lock fails
  READ permission:"session:{sessionId}:states"                     // null -> new empty dict
  WRITE permission:active_sessions ADD sessionId
  // Set states["role"] = newRole
  WRITE permission:"session:{sessionId}:states" <- updated [TTL: SessionDataTtlSeconds]
  // see helper: RecompileSessionPermissions (with in-memory states)
RETURN (200, SessionUpdateResponse { permissionsChanged, newPermissions })
```

### ClearSessionState
POST /permission/clear-session-state | Roles: []

```
READ permission:"session:{sessionId}:states"
IF states null or empty
  RETURN (200, SessionUpdateResponse { permissionsChanged: false })

IF serviceId null or empty                                         // Clear ALL states
  WRITE permission:"session:{sessionId}:states" <- {} [TTL: SessionDataTtlSeconds]
  // see helper: RecompileSessionPermissions (with empty states)
ELSE
  IF serviceId not in states
    RETURN (200, SessionUpdateResponse { permissionsChanged: false })
  IF body.states filter provided and non-empty
    IF current state not in filter
      RETURN (200, SessionUpdateResponse { permissionsChanged: false })
  // Remove serviceId from states dict
  WRITE permission:"session:{sessionId}:states" <- updated [TTL: SessionDataTtlSeconds]
  // see helper: RecompileSessionPermissions (with updated states)

RETURN (200, SessionUpdateResponse { permissionsChanged, newPermissions })
```

// Note: No distributed lock — unlike UpdateSessionState/UpdateSessionRole.

### GetSessionInfo
POST /permission/get-session-info | Roles: []

```
READ permission:"session:{sessionId}:states"        (parallel)
READ permission:"session:{sessionId}:permissions"    (parallel)
IF states null or empty                                            -> 404
// Role from states["role"], fallback to RoleHierarchy[0]
// Parse permissions map (skip "version", "generated_at" metadata keys)
RETURN (200, SessionInfo { role, states, permissions, version, lastUpdated })
```

### GetRegisteredServices
POST /permission/services/list | Roles: [admin]

```
READ permission:registered_services -> serviceIds
FOREACH serviceId in serviceIds
  READ permission:"service-registered:{serviceId}"
  READ permission:"service-states:{serviceId}" -> registeredStates
  // Count unique endpoints across all states x roles
  FOREACH state in registeredStates
    FOREACH role in RoleHierarchy
      READ permission:"permissions:{serviceId}:{state}:{role}"
RETURN (200, RegisteredServicesResponse { services, timestamp })
```

---

### Internal Helpers

#### RecompileSessionPermissions

Called from: RegisterServicePermissions, UpdateSessionState, UpdateSessionRole, ClearSessionState, HandleSessionConnected, RecompileForReconnection

Two overloads: one reads states from Redis, one accepts in-memory states dict.

```
// Determine role from states["role"], fallback to RoleHierarchy[0]
READ permission:registered_services -> registeredServices

FOREACH serviceId in registeredServices
  // relevantStates = ["default"] + session states
  // Cross-service states qualified as "{otherServiceId}:{stateValue}"
  FOREACH state in relevantStates
    FOREACH role in RoleHierarchy
      READ permission:"permissions:{serviceId}:{state}:{role}"
      // Track maxRoleByEndpoint[endpoint] = highest role index granting access
  // Allow endpoints where session role priority >= max required role

READ permission:"session:{sessionId}:permissions"                  // For version increment
WRITE permission:"session:{sessionId}:permissions" <- compiled [TTL: SessionDataTtlSeconds]
// see helper: PublishCapabilityUpdate
```

#### PublishCapabilityUpdate

Called from: RecompileSessionPermissions

```
// Publish service event (broadcast to all subscribers, regardless of connection state)
PUBLISH permission.capability-update { sessionId, version, updateType: Full, fullCapabilities, generatedAt, reason }

// Push client event to session channel (gated by active connection)
IF not skipActiveConnectionsCheck
  READ permission:active_connections CONTAINS sessionId
  IF not connected -> return                                       // Skip client push
PUSH SessionCapabilitiesEvent to sessionId { sessionId, permissions, reason, version }
```

---

### DI Listener: PermissionSessionActivityListener

#### OnConnectedAsync

Trigger: New session connection (Connect → ISessionActivityListener)

```
READ permission:"session:{sessionId}:states"                       // Existing or null
// Determine highest priority role from session roles via RoleHierarchy
// Parse authorizations (format "stubName:state")
WRITE permission:"session:{sessionId}:states" <- merged [TTL: SessionDataTtlSeconds]
WRITE permission:active_connections ADD sessionId
WRITE permission:active_sessions ADD sessionId
// see helper: RecompileSessionPermissions (skipActiveConnectionsCheck: true)
```

#### OnDisconnectedAsync

Trigger: Session disconnect (Connect → ISessionActivityListener)

```
WRITE permission:active_connections REMOVE sessionId
IF not reconnectable
  WRITE permission:active_sessions REMOVE sessionId
  // Delegates to ClearSessionStateAsync (clears all states, recompiles)
// If reconnectable: Redis EXPIRE aligned to Connect's reconnection window
```

#### OnReconnectedAsync

Trigger: Session reconnection (Connect → ISessionActivityListener)

```
WRITE permission:active_connections ADD sessionId
// see helper: RecompileSessionPermissions (reads states from Redis, refreshes TTL)
```

#### OnHeartbeatAsync

Trigger: Connect heartbeat (~30s interval)

```
// O(1) Redis EXPIRE — refresh TTL on session data keys (no data change)
// permission:"session:{sessionId}:states" TTL = SessionDataTtlSeconds
// permission:"session:{sessionId}:permissions" TTL = SessionDataTtlSeconds
```

---

### Event Handler: HandleSessionUpdatedAsync

Trigger: `session.updated` event from Auth service

```
// Determine highest priority role from evt.Roles via RoleHierarchy
CALL UpdateSessionRoleAsync({ sessionId, newRole })                // Acquires lock, recompiles
FOREACH authorization in evt.Authorizations
  // Parse "stubName:state" format
  CALL UpdateSessionStateAsync({ sessionId, serviceId, newState }) // Acquires lock per auth, recompiles
```

// Each call triggers a separate lock acquisition, recompilation, and capability push.

---

## Background Services

No background services.
