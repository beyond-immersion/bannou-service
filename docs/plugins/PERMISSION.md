# Permission Plugin Deep Dive

> **Plugin**: lib-permission
> **Schema**: schemas/permission-api.yaml
> **Version**: 3.0.0
> **State Store**: permission-statestore (Redis)

---

## Overview

Redis-backed RBAC permission system for WebSocket services. Manages per-session capability manifests compiled from a multi-dimensional permission matrix (service x state x role -> allowed endpoints). Services register their permission matrices on startup; the Permission service recompiles affected session capabilities whenever roles, states, or registrations change and pushes updates to connected clients via `IClientEventPublisher`. Features idempotent registration (SHA-256 hash comparison), atomic Redis set operations (`SADD`/`SREM`/`SISMEMBER`) for multi-instance-safe session tracking, and in-memory caching (`ConcurrentDictionary`) for compiled session capabilities.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Redis persistence for session states, permissions, matrix data, indexes; `ICacheableStateStore<string>` for atomic set operations on session/service tracking sets |
| lib-messaging (`IMessageBus`) | Error event publishing |
| lib-messaging (`IEventConsumer`) | 5 event subscriptions (session lifecycle, state changes, registrations) |
| lib-messaging (`IClientEventPublisher`) | Push capability updates to WebSocket sessions |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-connect | Validates API access for WebSocket messages; receives capability updates for connected sessions |
| lib-game-session | Calls `IPermissionClient.UpdateSessionStateAsync` to set `in_game` state when players join/leave |
| lib-matchmaking | Calls `IPermissionClient.UpdateSessionStateAsync` to set `in_match` state during matchmaking |
| lib-voice | Calls `IPermissionClient.UpdateSessionStateAsync` to set voice call states (`ringing`, `in_call`) |
| All services (via generated permission registration) | Register their x-permissions matrix on startup via `ServiceRegistrationEvent` |

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

No service-specific configuration properties. The service uses only `ForceServiceId` from `IServiceConfiguration`.

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
  5. Recompiles ALL active sessions
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
                ├── Atomic SADD: Add serviceId to registered_services set
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

1. **Permission TTL**: Auto-expire in-memory cached permissions after configurable period, forcing Redis refresh on next access. Safety net for lost RabbitMQ events. Default disabled (0). See [#198](https://github.com/beyond-immersion/bannou-service/issues/198) for implementation plan — all design questions resolved.
<!-- AUDIT:READY:2026-02-08:https://github.com/beyond-immersion/bannou-service/issues/198 -->
2. ~~**Fine-grained caching**~~: **CLOSED** — Per-service caching provides no invalidation benefit (all triggers operate at session level) and would increase memory ~40x. Per-session is the correct granularity. Additionally, Connect's local event-driven cache is the actual hot path, making Permission's in-memory cache secondary. See [#209](https://github.com/beyond-immersion/bannou-service/issues/209).
<!-- AUDIT:CLOSED:2026-02-08:https://github.com/beyond-immersion/bannou-service/issues/209 -->
3. ~~**Permission delegation**~~: **CLOSED** — The existing state-based permission system (`UpdateSessionState` with arbitrary states registered via x-permissions) already handles all proposed delegation use cases. Adding a parallel delegation mechanism would create unnecessary complexity. See [#234](https://github.com/beyond-immersion/bannou-service/issues/234).
<!-- AUDIT:CLOSED:2026-02-08:https://github.com/beyond-immersion/bannou-service/issues/234 -->
4. ~~**Audit trail**~~: **CLOSED (mis-scoped)** — Connect owns the hot-path validation (local cache, not Permission API). Permission changes are already event-driven via `SessionCapabilitiesEvent`. Denial auditing is a Connect concern. Storage/retention/queries are Analytics (L4) concerns. See [#235](https://github.com/beyond-immersion/bannou-service/issues/235).
<!-- AUDIT:CLOSED:2026-02-08:https://github.com/beyond-immersion/bannou-service/issues/235 -->

---

## Known Quirks & Caveats

### Bugs

None identified.

### Intentional Quirks

1. **activeConnections guards publishing**: Capability updates are only published to sessions in the `active_connections` set. Publishing to sessions without WebSocket connections causes RabbitMQ `exchange not_found` errors that crash the channel.

2. **ValidateApiAccess never uses cache**: Unlike `GetCapabilities` which uses the in-memory cache, `ValidateApiAccess` always reads from Redis. This ensures validation uses the latest compiled permissions at the cost of latency.

3. **No cache invalidation on session disconnect**: When a session disconnects, it's removed from `active_connections` but the in-memory cache entry remains until the session is garbage-collected or a new recompilation overwrites it.

4. **Static ROLE_ORDER array**: The role hierarchy is hardcoded as `["anonymous", "user", "developer", "admin"]`. Adding new roles requires code changes, not configuration.

### Design Considerations

1. **Full session recompilation on service registration**: Sequential recompilation of all active sessions during service registration. At 100K sessions, a hot-deployed service change triggers ~300s sequential recompilation. Solution: parallel recompilation with configurable `MaxConcurrentRecompilations` (semaphore-bounded `Task.WhenAll`). All design questions resolved — see [#236](https://github.com/beyond-immersion/bannou-service/issues/236) for implementation plan.
<!-- AUDIT:READY:2026-02-08:https://github.com/beyond-immersion/bannou-service/issues/236 -->

2. ~~**Permission matrix stored as individual keys**~~: **CLOSED** — 480 Redis keys with ~320 reads per recompilation (~3ms) is well within Redis capacity (100K+ ops/sec). The current simple key strategy is correct. Not on the hot message path (Connect validates locally). See [#237](https://github.com/beyond-immersion/bannou-service/issues/237).
<!-- AUDIT:CLOSED:2026-02-08:https://github.com/beyond-immersion/bannou-service/issues/237 -->

3. ~~**Read-modify-write on session sets**~~: **FIXED** - All three tracking sets (`activeConnections`, `activeSessions`, `registered_services`) now use `ICacheableStateStore<string>` atomic set operations (`SADD`/`SREM`/`SISMEMBER`/`SMEMBERS`), eliminating the read-modify-write race condition. The distributed lock and its 3 config properties were removed entirely.
<!-- AUDIT:FIXED:2026-02-08:https://github.com/beyond-immersion/bannou-service/issues/248 -->

---

## Work Tracking

None tracked.
