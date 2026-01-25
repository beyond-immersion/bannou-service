# Permission Plugin Deep Dive

> **Plugin**: lib-permission
> **Schema**: schemas/permission-api.yaml
> **Version**: 3.0.0
> **State Store**: permission-statestore (Redis)

---

## Overview

Redis-backed RBAC permission system for WebSocket services. Manages per-session capability manifests compiled from a multi-dimensional permission matrix (service x state x role -> allowed endpoints). Services register their permission matrices on startup; the Permission service recompiles affected session capabilities whenever roles, states, or registrations change and pushes updates to connected clients via `IClientEventPublisher`. Features idempotent registration (SHA-256 hash comparison), distributed locks for concurrent registration safety, and in-memory caching (`ConcurrentDictionary`) for compiled session capabilities.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Redis persistence for session states, permissions, matrix data, indexes |
| lib-state (`IDistributedLockProvider`) | Distributed locks for concurrent service registration |
| lib-messaging (`IMessageBus`) | Error event publishing |
| lib-messaging (`IEventConsumer`) | 5 event subscriptions (session lifecycle, state changes, registrations) |
| lib-messaging (`IClientEventPublisher`) | Push capability updates to WebSocket sessions |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-connect | Validates API access for WebSocket messages; receives capability updates for connected sessions |
| All services (via generated permission registration) | Register their x-permissions matrix on startup via `ServiceRegistrationEvent` |

---

## State Storage

**Store**: `permission-statestore` (Backend: Redis, Prefix: `permission`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `active_sessions` | `HashSet<string>` | All sessions that have ever connected |
| `active_connections` | `HashSet<string>` | Sessions with active WebSocket connections (safe to publish to) |
| `registered_services` | `HashSet<string>` | List of all services that have registered permissions |
| `service-registered:{serviceId}` | Registration object | Individual service registration marker with timestamp |
| `session:{sessionId}:states` | `Dictionary<string, string>` | Per-session state map (role, service states) |
| `session:{sessionId}:permissions` | `Dictionary<string, object>` | Compiled permission set (service -> endpoint list + version) |
| `permissions:{service}:{state}:{role}` | `HashSet<string>` | Permission matrix entries (allowed endpoints for combination) |
| `permission_versions` | `Dictionary<string, string>` | Service version tracking |
| `permission_hash:{serviceId}` | `string` | SHA-256 hash for idempotent registration detection |

---

## Events

### Published Events

| Target | Mechanism | Trigger |
|--------|-----------|---------|
| Session-specific channel | `IClientEventPublisher.PublishToSessionAsync` | `SessionCapabilitiesEvent` pushed to connected clients on recompilation |

No traditional topic-based event publications. Capability updates go directly to session channels.

### Consumed Events

| Topic | Event Type | Handler |
|-------|-----------|---------|
| `permission.service-registered` | `ServiceRegistrationEvent` | Builds permission matrix from endpoint data, registers with system |
| `permission.session-state-changed` | `SessionStateChangeEvent` | Triggers recompilation when service state changes |
| `session.updated` | `SessionUpdatedEvent` | Role/authorization changes from Auth service |
| `session.connected` | `SessionConnectedEvent` | Adds to activeConnections, triggers initial capability delivery |
| `session.disconnected` | `SessionDisconnectedEvent` | Removes from activeConnections |

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `LockMaxRetries` | `PERMISSION_LOCK_MAX_RETRIES` | `10` | Maximum retries for acquiring distributed lock |
| `LockBaseDelayMs` | `PERMISSION_LOCK_BASE_DELAY_MS` | `100` | Base delay between lock retries (exponential backoff) |
| `LockExpirySeconds` | `PERMISSION_LOCK_EXPIRY_SECONDS` | `30` | Distributed lock expiration time |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<PermissionService>` | Singleton | Structured logging |
| `PermissionServiceConfiguration` | Singleton | Lock retry config |
| `IStateStoreFactory` | Singleton | Redis state operations |
| `IMessageBus` | Scoped (injected) | Error event publishing |
| `IDistributedLockProvider` | Singleton | Distributed lock acquisition |
| `IClientEventPublisher` | Scoped (injected) | Session-specific capability push |
| `IEventConsumer` | Scoped (injected) | Event handler registration |

Service lifetime is **Singleton** (shared across all requests).

---

## API Endpoints (Implementation Notes)

### Permission Lookup (1 endpoint)

- **GetCapabilities** (`/permission/capabilities`): Returns compiled session permissions. Checks in-memory `ConcurrentDictionary` cache first. Falls back to Redis state store. Caches result for future requests. Returns service -> endpoint list map with version.

### Permission Validation (1 endpoint)

- **ValidateApiAccess** (`/permission/validate`): O(1) validation. Reads session permissions from Redis, checks if method exists in the service's allowed endpoints list. Returns `Allowed` boolean. No cache (always reads latest).

### Service Management (2 endpoints)

- **RegisterServicePermissions** (`/permission/register-service`): Complex registration flow:
  1. Computes SHA-256 hash of permission data for idempotency
  2. Skips if hash unchanged AND service already registered
  3. Stores matrix entries in Redis (service:state:role -> endpoints)
  4. Acquires distributed lock with exponential backoff + jitter for registered_services list update
  5. Recompiles ALL active sessions outside lock scope (prevents lock timeout)
  6. Stores new hash for future idempotency checks

- **GetRegisteredServices** (`/permission/services/list`): Lists all registered services with their registration info. Used by test infrastructure to poll for service readiness.

### Session Management (4 endpoints)

- **UpdateSessionState** (`/permission/update-session-state`): Updates state for specific service in session's state map. Triggers recompilation using in-memory states (avoids read-after-write issues).
- **UpdateSessionRole** (`/permission/update-session-role`): Updates role in session's state map. Triggers full recompilation across all services.
- **ClearSessionState** (`/permission/clear-session-state`): Conditional or unconditional state removal. If `states` list provided, only clears if current state matches. If `serviceId` is null, clears ALL states.
- **GetSessionInfo** (`/permission/get-session-info`): Returns full session picture: states map, role, compiled permissions, version. Uses `Task.WhenAll` for concurrent state/permission reads.

---

## Visual Aid

```
Permission Compilation Flow
==============================

  Service Startup → publishes ServiceRegistrationEvent
       │
       ▼
  HandleServiceRegistrationAsync
       │
       ├──► Build State->Role->Methods matrix from x-permissions
       │
       └──► RegisterServicePermissionsAsync
                │
                ├── Hash unchanged? → Skip (idempotent)
                │
                ├── Store matrix: permissions:{service}:{state}:{role} → [endpoints]
                │
                ├── Lock: registered_services_lock (with retry/backoff)
                │    └── Add serviceId to registered_services set
                │
                └── For each active session:
                     └── RecompileSessionPermissionsAsync


Session Permission Recompilation
==================================

  RecompileSessionPermissionsAsync(sessionId, states, reason)
       │
       ├── role = states["role"] (default: "user")
       │
       ├── For each registered service:
       │    ├── Relevant states: ["default"] + session states
       │    │
       │    ├── For each relevant state:
       │    │    ├── Walk ROLE_ORDER: anonymous, user, developer, admin
       │    │    │    └── Load: permissions:{service}:{state}:{role}
       │    │    │
       │    │    └── For each endpoint:
       │    │         Track maxRoleByEndpoint[endpoint] = highest role
       │    │
       │    └── Allow endpoints where session role >= max required role
       │
       ├── Store compiled permissions with version increment
       │
       ├── Clear in-memory cache (ConcurrentDictionary)
       │
       └── PublishCapabilityUpdateAsync → IClientEventPublisher
                                          └── SessionCapabilitiesEvent to WebSocket


Role Priority Hierarchy
=========================

  ROLE_ORDER = ["anonymous", "user", "developer", "admin"]
  Index:          0            1          2           3

  Session role priority >= endpoint required role → ALLOWED

  Example: Session role = "developer" (priority 2)
    - Endpoint requires "user" (priority 1) → ALLOWED (2 >= 1)
    - Endpoint requires "admin" (priority 3) → DENIED (2 < 3)
```

---

## Stubs & Unimplemented Features

None. The service is feature-complete for its scope.

---

## Potential Extensions

1. **Permission TTL**: Auto-expire session permissions after configurable period, forcing recompilation on next access.
2. **Fine-grained caching**: Cache compiled permissions per-service instead of per-session for more granular invalidation.
3. **Permission delegation**: Allow services to grant temporary elevated permissions to specific sessions.
4. **Audit trail**: Log permission checks and changes for security auditing.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

None identified.

### Intentional Quirks (Documented Behavior)

1. **Singleton lifetime**: Unlike most services (Scoped), PermissionService is Singleton. The in-memory `ConcurrentDictionary` cache persists for the application lifetime, enabling fast capability lookups without Redis calls.

2. **Idempotent registration via SHA-256 hash**: Permission data is hashed using a canonical sorted representation. If the hash matches and the service is already registered, registration is skipped entirely. Prevents redundant recompilation on service restarts.

3. **Lock scope minimized**: The distributed lock for `registered_services` list is held only for the read-modify-write on the set. Session recompilation happens OUTSIDE the lock to prevent lock timeouts during concurrent registrations at startup.

4. **activeConnections guards publishing**: Capability updates are only published to sessions in the `active_connections` set. Publishing to sessions without WebSocket connections causes RabbitMQ `exchange not_found` errors that crash the channel.

5. **Cross-service state keys**: State keys use format `{serviceId}:{stateValue}` for cross-service states (e.g., `game-session:in_game`). Same-service states use just the state value (e.g., `in_game`). Matches registration logic in `HandleServiceRegistrationAsync`.

6. **Authorization strings format**: From Auth service's `SessionUpdatedEvent`, authorizations are in `{stubName}:{state}` format (e.g., `arcadia:authorized`). The stub name becomes the service ID for state tracking.

7. **ValidateApiAccess never uses cache**: Unlike `GetCapabilities` which uses the in-memory cache, `ValidateApiAccess` always reads from Redis. This ensures validation uses the latest compiled permissions at the cost of latency.

### Design Considerations (Requires Planning)

1. **Full session recompilation on service registration**: Every time a service registers, ALL active sessions are recompiled. With many concurrent sessions and frequent service restarts, this generates O(sessions * services) Redis operations.

2. **No cache invalidation on session disconnect**: When a session disconnects, it's removed from `active_connections` but the in-memory cache entry remains until the session is garbage-collected or a new recompilation overwrites it.

3. **Permission matrix stored as individual keys**: Each `service:state:role` combination is a separate Redis key. For 40 services with 3 states and 4 roles, this is 480 keys. Queries during recompilation read many keys per session.

4. **Exponential backoff jitter**: Lock retry uses `baseDelayMs * (1 << min(attempt, 5)) + random(0, 50)`. Maximum delay caps at 32x base (3200ms + jitter). 10 retries at worst case = ~6 seconds of waiting.

5. **Static ROLE_ORDER array**: The role hierarchy is hardcoded as `["anonymous", "user", "developer", "admin"]`. Adding new roles requires code changes, not configuration.

---

### False Positives Removed

- **T6 constructor null checks**: NRTs enabled - compile-time null safety eliminates need for runtime guards
- **T7 ApiException handling**: Permission service does not call external services via mesh/generated clients - only infrastructure libs
- **T19 private method XML docs**: T19 applies to public APIs only; private helpers do not require XML documentation
- **T21 hardcoded role hierarchy**: `ROLE_ORDER` is an intentional fixed hierarchy matching the authentication system design. Role priority is a security invariant, not a tunable configuration.
- **T16 SCREAMING_CASE constants**: Internal constants can use any consistent naming convention. This is a style preference, not a tenet requirement.

### Additional Design Considerations

6. **T9 (Multi-Instance Safety)**: Read-modify-write on `activeConnections`/`activeSessions` sets without distributed locks. Multiple instances could overwrite each other's additions/removals. Requires atomic set operations in lib-state or distributed lock refactoring.

7. **T25 (Anonymous type for registration)**: `registrationInfo` uses anonymous type which cannot be reliably deserialized. Should define a typed `ServiceRegistrationInfo` POCO class.
