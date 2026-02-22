# Permission Plugin Deep Dive

> **Plugin**: lib-permission
> **Schema**: schemas/permission-api.yaml
> **Version**: 3.0.0
> **Layer**: AppFoundation
> **State Store**: permission-statestore (Redis)

---

## Overview

Redis-backed RBAC permission system (L1 AppFoundation) for WebSocket services. Manages per-session capability manifests compiled from a multi-dimensional permission matrix (service x state x role -> allowed endpoints). Services register their permission matrices on startup; the Permission service recompiles affected session capabilities whenever roles, states, or registrations change and pushes updates to connected clients via the Connect service's per-session RabbitMQ queues.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Redis persistence for session states, permissions, matrix data, indexes; `ICacheableStateStore<string>` for atomic set operations on session/service tracking sets |
| lib-state (`IDistributedLockProvider`) | Distributed locks for session state and role update operations (prevents lost updates from concurrent modifications) |
| lib-messaging (`IMessageBus`) | Error event publishing |
| lib-messaging (`IEventConsumer`) | 1 event subscription (`session.updated` from Auth service) |
| lib-messaging (`IClientEventPublisher`) | Push capability updates to WebSocket sessions |
| lib-telemetry (`ITelemetryProvider`) | Trace spans on all async helper methods and event handlers (T30 compliance) |

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

Session connected/disconnected events are now handled via `PermissionSessionActivityListener` (DI listener pattern, GH#392) instead of event subscriptions, since Connect and Permission are both L1 AppFoundation (always co-located).

### DI Listener: PermissionSessionActivityListener

| Callback | Trigger | Action |
|----------|---------|--------|
| `OnHeartbeatAsync` | Connect heartbeat (~30s) | O(1) Redis EXPIRE on session data keys (TTL refresh) |
| `OnConnectedAsync` | New session connection | Delegates to `HandleSessionConnectedAsync` (session setup + initial capability delivery) |
| `OnReconnectedAsync` | Session reconnection | Re-adds to `active_connections`, recompiles from preserved Redis state, refreshes TTL |
| `OnDisconnectedAsync` | Session disconnect | Delegates to `HandleSessionDisconnectedAsync`; aligns Redis TTL to reconnection window when reconnectable |

---

## Configuration

| Property | Type | Default | Range | Env Var | Description |
|----------|------|---------|-------|---------|-------------|
| `MaxConcurrentRecompilations` | int | 50 | 1-500 | `PERMISSION_MAX_CONCURRENT_RECOMPILATIONS` | Bounds parallel session recompilations during service registration |
| `SessionDataTtlSeconds` | int | 600 | 0-604800 | `PERMISSION_SESSION_DATA_TTL_SECONDS` | Redis TTL for session data keys. With heartbeat-driven TTL refresh (~30s via `ISessionActivityListener`), active sessions continuously extend their TTL. Dead sessions expire naturally when heartbeats stop. Default 600 (10 min, ~20 heartbeat intervals of headroom). 0 disables |
| `RoleHierarchy` | string[] | `["anonymous", "user", "developer", "admin"]` | - | `PERMISSION_ROLE_HIERARCHY` | Ordered role hierarchy from lowest to highest privilege (comma-separated in env var) |
| `SessionLockTimeoutSeconds` | int | 10 | 1-60 | `PERMISSION_SESSION_LOCK_TIMEOUT_SECONDS` | Distributed lock expiry for session state/role update operations. Prevents lost updates from concurrent modifications |

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
| `ITelemetryProvider` | Singleton | Trace span creation for all async helper methods and event handlers |
| `IDistributedLockProvider` | Singleton | Distributed locks for session state and role updates (prevents concurrent modification) |
| `IPermissionRegistry` | Singleton (via plugin) | Push-based permission registration interface (backed by PermissionService singleton, registered in PermissionServicePlugin) |
| `ISessionActivityListener` | Singleton (via plugin) | `PermissionSessionActivityListener` — receives session lifecycle events and heartbeats from Connect via DI listener dispatch. Handles TTL refresh, session connect/disconnect/reconnect delegation. Replaces `session.connected`/`session.disconnected` event subscriptions (GH#392) |

Service lifetime is **Singleton** (shared across all requests).

---

## API Endpoints (Implementation Notes)

### Permission Lookup (1 endpoint)

- **GetCapabilities** (`/permission/capabilities`): Returns compiled session permissions from Redis. Returns service -> endpoint list map with version. Diagnostic/admin endpoint — Connect receives capabilities via push events, not by polling this endpoint.

### Permission Validation (1 endpoint)

- **ValidateApiAccess** (`/permission/validate`): O(1) validation. Reads session permissions from Redis, checks if method exists in the service's allowed endpoints list. Returns `Allowed` boolean.

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

- **UpdateSessionState** (`/permission/update-session-state`): Acquires distributed lock on session before updating state for specific service in session's state map. Triggers recompilation using in-memory states (avoids read-after-write issues). Lock timeout configurable via `SessionLockTimeoutSeconds`.
- **UpdateSessionRole** (`/permission/update-session-role`): Acquires distributed lock on session before updating role in session's state map. Triggers full recompilation across all services. Lock timeout configurable via `SessionLockTimeoutSeconds`.
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

  UpdateSessionStateAsync / UpdateSessionRoleAsync
       │
       ├── Acquire distributed lock: session:{sessionId}
       │    └── Lock timeout: SessionLockTimeoutSeconds (default: 10s)
       │
       ├── Read-modify-write session states under lock
       │
       └── RecompileSessionPermissionsAsync(sessionId, states, reason)
            │
            ├── role = states["role"] (default: "anonymous")
            │
            ├── For each registered service:
            │    ├── Relevant states: ["default"] + session states
            │    │
            │    ├── For each relevant state:
            │    │    ├── Walk hierarchy from _configuration.RoleHierarchy
            │    │    │    └── Load: permissions:{service}:{state}:{role}
            │    │    │
            │    │    └── For each endpoint:
            │    │         Track maxRoleByEndpoint[endpoint] = highest role
            │    │
            │    └── Allow endpoints where session role >= max required role
            │
            ├── Store compiled permissions with version increment
            │
            └── PublishCapabilityUpdateAsync → IClientEventPublisher
                                               └── SessionCapabilitiesEvent to WebSocket


Role Priority Hierarchy (from _configuration.RoleHierarchy)
===============================================================

  Default: ["anonymous", "user", "developer", "admin"]
  Index:       0            1          2           3

  Configurable via PERMISSION_ROLE_HIERARCHY env var (comma-separated).
  DetermineHighestPriorityRole walks the hierarchy from highest to lowest,
  returning the first matching role from the session's role list.

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

1. ~~**Inconsistent default role between connection and update paths**~~: **FIXED** (2026-02-11, updated 2026-02-22) - Unified all default-role locations to return `"anonymous"` when roles are null/empty. The static `DetermineHighestRoleFromEvent` method was removed and replaced with config-driven `DetermineHighestPriorityRole` which walks `_configuration.RoleHierarchy` from highest to lowest. Sessions with no roles now consistently get anonymous-level permissions across all code paths.

2. ~~**Static ROLE_ORDER array**~~: **FIXED** (2026-02-11) - Replaced hardcoded `ROLE_ORDER` with `_configuration.RoleHierarchy` config property (default `["anonymous", "user", "developer", "admin"]`). Set via `PERMISSION_ROLE_HIERARCHY` env var as comma-separated string. T21 compliance fix.

### Intentional Quirks

1. **activeConnections guards publishing**: Capability updates are only published to sessions in the `active_connections` set. Publishing to sessions without WebSocket connections causes RabbitMQ `exchange not_found` errors that crash the channel.

2. ~~**ValidateApiAccess never uses cache**~~: **OBSOLETE** (2026-02-22) — In-memory capability cache removed entirely. Both `GetCapabilities` and `ValidateApiAccess` read directly from Redis. The cache was never used in any production hot path — Connect receives capabilities via push events, not by polling `GetCapabilities`.

3. ~~**No cache invalidation on session disconnect**~~: **FIXED** (2026-02-22, GH#392) — Implemented `ISessionActivityListener` DI listener pattern for heartbeat-driven Redis TTL refresh. In-memory capability cache removed entirely (see quirk #2). `SessionDataTtlSeconds` default reduced from 86400 (24h) to 600 (10 min). On disconnect with reconnection window, TTL is aligned to Connect's actual reconnection window duration.

4. ~~**Cross-instance cache staleness is TTL-bounded**~~: **OBSOLETE** (2026-02-22) — In-memory capability cache removed. No cross-instance staleness concern — all reads go directly to Redis.

5. ~~**GetRegisteredServices endpoint count is approximate**~~: **FIXED** (2026-02-11) - `GetRegisteredServicesAsync` now reads dynamically stored per-service state names from `service-states:{serviceId}` Redis keys instead of using a hardcoded array. States are saved during `RegisterServicePermissionsAsync` from the permission matrix keys. Previously used `["authenticated", "default", "lobby", "in_game"]` which included fake states and missed real ones (voice: `ringing`/`in_room`/`consent_pending`, matchmaking: `in_queue`/`match_pending`, chat: `in_room`).

### Design Considerations

None active. Previous considerations were either fixed (parallel recompilation via `SemaphoreSlim`, atomic set operations via `ICacheableStateStore`) or closed as non-issues (individual key strategy is correct at current scale).

---

## Work Tracking

### Completed
- **2026-02-11**: Fixed hardcoded states array in `GetRegisteredServicesAsync`. Now dynamically reads per-service states from Redis, stored during registration.
- **2026-02-11**: Issue #389. Replaced hardcoded `ROLE_ORDER` with `RoleHierarchy` config property. T21 compliance fix.
- **2026-02-22**: Production hardening audit. Schema, code, and documentation sweep:
  - **Schema (NRT)**: Fixed 14 NRT violations across response schemas — added `required` arrays and `nullable: true` where needed.
  - **Schema (T8)**: Removed filler properties from responses — `ServiceId` echo from RegistrationResponse, `SessionId` echoes from SessionUpdateResponse/CapabilityResponse/SessionInfo/ValidationResponse, `Message` from SessionUpdateResponse.
  - **Schema (T5)**: Moved `CapabilityUpdateType` and `CapabilityUpdateReason` inline enums from events schema to API schema; events schema now uses `$ref`.
  - **Schema (T5)**: Made `PermissionCapabilityUpdate` extend `BaseServiceEvent` via `allOf`.
  - **Schema (T21)**: Removed dead `metadata` field from `SessionStateUpdate`.
  - **Schema (validation)**: Added `minLength`, `minimum`, `minItems` validation keywords across schemas. Added `SessionLockTimeoutSeconds` config property.
  - **Code (T9)**: Added distributed locks (`IDistributedLockProvider`) to `UpdateSessionStateAsync` and `UpdateSessionRoleAsync` to prevent lost updates from concurrent modifications.
  - **Code (T21)**: Completed `RoleHierarchy` migration — removed static `DetermineHighestRoleFromEvent` from events partial, unified to config-driven `DetermineHighestPriorityRole`.
  - **Code (T30)**: Added telemetry spans (`ITelemetryProvider.StartActivity`) to all async helper methods and event handlers.
  - **Code (T7)**: Removed duplicate try-catch from `HandleSessionConnectedAsync`/`HandleSessionDisconnectedAsync` service methods (event handlers in `PermissionServiceEvents.cs` are the error boundary).
  - **Code (T26)**: Fixed sentinel values — `DateTimeOffset.UtcNow.ToString("o")` for ISO 8601 timestamps.
  - **Code (T13)**: Extracted magic string `"role"` to `SESSION_ROLE_KEY` constant.
  - **Code**: Moved `ServiceRegistrationInfo` internal class from `PermissionService.cs` to `PermissionServiceModels.cs`.
  - **Tests**: Updated 27 unit tests for new constructor signature and removed response properties. All passing.
  - ~~**Config**: Changed `PermissionCacheTtlSeconds` default from 0 to 300 (5 minutes).~~ **OBSOLETE** — `PermissionCacheTtlSeconds` removed entirely when in-memory cache was removed (2026-02-22).
- **2026-02-22**: GH#392 — Implemented `ISessionActivityListener` DI listener pattern for session lifecycle + heartbeat-driven TTL refresh:
  - **New**: `PermissionSessionActivityListener` — thin DI listener receiving heartbeats, connect/disconnect/reconnect notifications from Connect. Handles O(1) Redis EXPIRE for TTL refresh, delegates session lifecycle to PermissionService methods.
  - **New**: `RecompileForReconnectionAsync` on PermissionService — lightweight reconnection path that re-adds to `active_connections` and recompiles from preserved Redis state.
  - **Removed**: `session.connected` and `session.disconnected` event subscriptions from `PermissionServiceEvents.cs` (replaced by DI listener since Connect and Permission are both L1, always co-located). `session.updated` subscription retained (from Auth, separate service).
  - **Changed**: `SessionDataTtlSeconds` default from 86400 (24h) to 600 (10 min). With 30s heartbeats refreshing TTL, active sessions always stay alive; dead sessions expire in ~10 minutes.
  - **Connect**: Added `IEnumerable<ISessionActivityListener>` injection + dispatch at 4 sites (heartbeat, connected, reconnected, disconnected).
  - **Removed**: In-memory `ConcurrentDictionary<string, CapabilityResponse>` cache and `PermissionCacheTtlSeconds` config property. The cache served no production purpose — Connect receives capabilities via push events, `ValidateApiAccess` always read Redis, and `GetCapabilities` is a diagnostic endpoint with no hot-path callers.
