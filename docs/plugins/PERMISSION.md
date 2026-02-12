# Permission Plugin Deep Dive

> **Plugin**: lib-permission
> **Schema**: schemas/permission-api.yaml
> **Version**: 3.0.0
> **State Store**: permission-statestore (Redis)

---

## Overview

Redis-backed RBAC permission system (L1 AppFoundation) for WebSocket services. Manages per-session capability manifests compiled from a multi-dimensional permission matrix (service x state x role -> allowed endpoints). Services register their permission matrices on startup; the Permission service recompiles affected session capabilities whenever roles, states, or registrations change and pushes updates to connected clients via the Connect service's per-session RabbitMQ queues.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Redis persistence for session states, permissions, matrix data, indexes; `ICacheableStateStore<string>` for atomic set operations on session/service tracking sets |
| lib-messaging (`IMessageBus`) | Error event publishing |
| lib-messaging (`IEventConsumer`) | 3 event subscriptions (session lifecycle) |
| lib-messaging (`IClientEventPublisher`) | Push capability updates to WebSocket sessions |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-connect | Validates API access for WebSocket messages; receives capability updates for connected sessions |
| lib-game-session | Calls `IPermissionClient.UpdateSessionStateAsync` to set `in_game` state when players join/leave |
| lib-matchmaking | Calls `IPermissionClient.UpdateSessionStateAsync` to set `in_match` state during matchmaking |
| lib-voice | Calls `IPermissionClient.UpdateSessionStateAsync` to set voice call states (`ringing`, `in_call`) |
| lib-chat | Calls `IPermissionClient.UpdateSessionStateAsync` for chat room state management |
| All services (via generated permission registration) | Register their x-permissions matrix on startup via `IPermissionRegistry` DI interface |

---

## State Storage

**Store**: `permission-statestore` (Backend: Redis, Prefix: `permission`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `active_sessions` | Redis Set (atomic `SADD`/`SREM`) | All sessions that have ever connected |
| `active_connections` | Redis Set (atomic `SADD`/`SREM`) | Sessions with active WebSocket connections (safe to publish to) |
| `registered_services` | Redis Set (atomic `SADD`/`SREM`) | List of all services that have registered permissions |
| `service-registered:{serviceId}` | Registration object | Individual service registration marker with timestamp |
| `session:{sessionId}:states` | `Dictionary<string, string>` | Per-session state map (role, service states) |
| `session:{sessionId}:permissions` | `Dictionary<string, object>` | Compiled permission set (service -> endpoint list + version) |
| `permissions:{service}:{state}:{role}` | `HashSet<string>` | Permission matrix entries (allowed endpoints for combination) |
| `permission_versions:{serviceId}` | `Dictionary<string, string>` | Per-service version tracking (contains `version` key) |
| `permission_hash:{serviceId}` | `string` | SHA-256 hash for idempotent registration detection |
| `service-states:{serviceId}` | `HashSet<string>` | Set of state names registered by each service (for dynamic endpoint discovery) |

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
| `session.updated` | `SessionUpdatedEvent` | Role/authorization changes from Auth service |
| `session.connected` | `SessionConnectedEvent` | Adds to activeConnections, triggers initial capability delivery |
| `session.disconnected` | `SessionDisconnectedEvent` | Removes from activeConnections |

---

## Configuration

| Property | Type | Default | Range | Env Var | Description |
|----------|------|---------|-------|---------|-------------|
| `MaxConcurrentRecompilations` | int | 50 | 1-500 | `PERMISSION_MAX_CONCURRENT_RECOMPILATIONS` | Bounds parallel session recompilations during service registration |
| `PermissionCacheTtlSeconds` | int | 0 | 0-86400 | `PERMISSION_CACHE_TTL_SECONDS` | In-memory cache TTL in seconds. 0 disables (cache never expires). Recommended non-zero: 300 (5 min). Safety net against lost RabbitMQ events |
| `SessionDataTtlSeconds` | int | 86400 | 0-604800 | `PERMISSION_SESSION_DATA_TTL_SECONDS` | Redis TTL for session data keys. Handles orphaned session cleanup. 0 disables. Default 86400 (24h) |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<PermissionService>` | Singleton | Structured logging |
| `PermissionServiceConfiguration` | Singleton | Service configuration |
| `IStateStoreFactory` | Singleton | Redis state operations (`IStateStore<T>` + `ICacheableStateStore<string>`) |
| `IMessageBus` | Scoped (injected) | Error event publishing |
| `IClientEventPublisher` | Scoped (injected) | Session-specific capability push |
| `IEventConsumer` | Scoped (injected) | Event handler registration |
| `IPermissionRegistry` | Singleton (via plugin) | Push-based permission registration interface (backed by PermissionService singleton, registered in PermissionServicePlugin) |

Service lifetime is **Singleton** (shared across all requests).

---

## API Endpoints (Implementation Notes)

### Permission Lookup (1 endpoint)

- **GetCapabilities** (`/permission/capabilities`): Returns compiled session permissions. Checks in-memory `ConcurrentDictionary` cache first. Falls back to Redis state store. Caches result for future requests. Returns service -> endpoint list map with version.

### Permission Validation (1 endpoint)

- **ValidateApiAccess** (`/permission/validate`): O(1) validation. Reads session permissions from Redis, checks if method exists in the service's allowed endpoints list. Returns `Allowed` boolean. No cache (always reads latest).

### Service Management (2 endpoints)

- **RegisterServicePermissions** (`/permission/register-service`): Registration flow:
  1. Computes SHA-256 hash of permission data for idempotency
  2. Skips if hash unchanged AND service already registered (atomic `SISMEMBER` check)
  3. Stores matrix entries in Redis (service:state:role -> endpoints)
  4. Atomically adds service to registered_services set (`SADD` - multi-instance safe)
  5. Recompiles ALL active sessions in parallel (`SemaphoreSlim`-bounded `Task.WhenAll`, configurable via `MaxConcurrentRecompilations`)
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

  PluginLoader Startup → IPermissionRegistry.RegisterServiceAsync(serviceId, version, matrix)
       │
       ▼
  RegisterServicePermissionsAsync
       │
       ├── Hash unchanged? → Skip (idempotent)
       │
       ├── Store matrix: permissions:{service}:{state}:{role} → [endpoints]
       │
       ├── Atomic SADD: Add serviceId to registered_services set
       │
       └── For each active session:
            └── RecompileSessionPermissionsAsync


Session Permission Recompilation
==================================

  RecompileSessionPermissionsAsync(sessionId, states, reason)
       │
       ├── role = states["role"] (default: "anonymous")
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

None identified. Previous extensions were either implemented (Permission TTL → config properties) or rejected as unnecessary (fine-grained caching, permission delegation, audit trail).

---

## Known Quirks & Caveats

### Bugs

1. ~~**Inconsistent default role between connection and update paths**~~: **FIXED** (2026-02-11) - Unified all four default-role locations to return `"anonymous"` when roles are null/empty: `DetermineHighestRoleFromEvent` (two returns), `RecompileSessionPermissionsAsync` (`GetValueOrDefault`), and `GetSessionInfoAsync` (`GetValueOrDefault`). Sessions with no roles now consistently get anonymous-level permissions across all code paths.

### Intentional Quirks

1. **activeConnections guards publishing**: Capability updates are only published to sessions in the `active_connections` set. Publishing to sessions without WebSocket connections causes RabbitMQ `exchange not_found` errors that crash the channel.

2. **ValidateApiAccess never uses cache**: Unlike `GetCapabilities` which uses the in-memory cache, `ValidateApiAccess` always reads from Redis. This ensures validation uses the latest compiled permissions at the cost of latency.

3. **No cache invalidation on session disconnect**: When a session disconnects, it's removed from `active_connections` but the in-memory cache entry remains until the session is garbage-collected or a new recompilation overwrites it.

4. **Static ROLE_ORDER array**: The role hierarchy is hardcoded as `["anonymous", "user", "developer", "admin"]`. Adding new roles requires code changes, not configuration.

5. ~~**GetRegisteredServices endpoint count is approximate**~~: **FIXED** (2026-02-11) - `GetRegisteredServicesAsync` now reads dynamically stored per-service state names from `service-states:{serviceId}` Redis keys instead of using a hardcoded array. States are saved during `RegisterServicePermissionsAsync` from the permission matrix keys. Previously used `["authenticated", "default", "lobby", "in_game"]` which included fake states and missed real ones (voice: `ringing`/`in_room`/`consent_pending`, matchmaking: `in_queue`/`match_pending`, chat: `in_room`).

### Design Considerations

None active. Previous considerations were either fixed (parallel recompilation via `SemaphoreSlim`, atomic set operations via `ICacheableStateStore`) or closed as non-issues (individual key strategy is correct at current scale).

---

## Work Tracking

### Completed
- **2026-02-11**: Fixed hardcoded states array in `GetRegisteredServicesAsync`. Now dynamically reads per-service states from Redis, stored during registration.
