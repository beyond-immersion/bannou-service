# Permission Plugin Deep Dive

> **Plugin**: lib-permission
> **Schema**: schemas/permission-api.yaml
> **Version**: 3.0.0
> **Layer**: AppFoundation
> **State Store**: permission-statestore (Redis)
> **Implementation Map**: [docs/maps/PERMISSION.md](../maps/PERMISSION.md)

---

## Overview

Redis-backed RBAC permission system (L1 AppFoundation) for WebSocket services. Manages per-session capability manifests compiled from a multi-dimensional permission matrix (service x state x role -> allowed endpoints). Services register their permission matrices on startup; the Permission service recompiles affected session capabilities whenever roles, states, or registrations change and pushes updates to connected clients via the Connect service's per-session RabbitMQ queues.

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-connect | Receives `SessionCapabilitiesEvent` via RabbitMQ for capability manifest updates; dispatches session lifecycle and heartbeat events to `ISessionActivityListener` implementations |
| lib-game-session | Calls `IPermissionClient.UpdateSessionStateAsync` to set `in_game` state when players join/leave |
| lib-matchmaking | Calls `IPermissionClient.UpdateSessionStateAsync` to set `in_match` state during matchmaking |
| lib-voice | Calls `IPermissionClient.UpdateSessionStateAsync` to set voice call states (`ringing`, `in_call`) |
| lib-chat | Calls `IPermissionClient.UpdateSessionStateAsync` for chat room state management |
| All services (via generated permission registration) | Register their x-permissions matrix on startup via `IPermissionRegistry` DI interface |

---

### Type Field Classification

| Field | Category | Type | Rationale |
|-------|----------|------|-----------|
| `role` / `newRole` (on session state, role updates, session info) | B (Content Code) | Opaque string | Role values (`"anonymous"`, `"user"`, `"developer"`, `"admin"`) are configurable via `PERMISSION_ROLE_HIERARCHY` env var, not a fixed enum. New roles can be added without schema changes. |
| `state` / `newState` (on session state updates, permission matrix keys) | B (Content Code) | Opaque string | State values (`"default"`, `"authenticated"`, `"in_game"`, `"lobby"`, `"in_room"`, `"in_queue"`, etc.) are registered dynamically by each service's permission matrix. Extensible without schema changes. |
| `serviceId` (on permission matrices, session states, validation) | B (Content Code) | Opaque string | Service identifiers are registered dynamically by each plugin at startup; the set of services is deployment-dependent. |
| `CapabilityUpdateType` | C (System State) | Service-specific enum | Finite update delivery modes (`full`, `delta`) for capability push events. |
| `CapabilityUpdateReason` | C (System State) | Service-specific enum | Finite reasons for capability recompilation (`session_created`, `session_state_changed`, `role_changed`, `service_registered`, `manual_refresh`). |

---

## Configuration

| Property | Type | Default | Range | Env Var | Description |
|----------|------|---------|-------|---------|-------------|
| `MaxConcurrentRecompilations` | int | 50 | 1-500 | `PERMISSION_MAX_CONCURRENT_RECOMPILATIONS` | Bounds parallel session recompilations during service registration |
| `SessionDataTtlSeconds` | int | 600 | 0-604800 | `PERMISSION_SESSION_DATA_TTL_SECONDS` | Redis TTL for session data keys. With heartbeat-driven TTL refresh (~30s via `ISessionActivityListener`), active sessions continuously extend their TTL. Dead sessions expire naturally when heartbeats stop. Default 600 (10 min, ~20 heartbeat intervals of headroom). 0 disables |
| `RoleHierarchy` | string[] | `["anonymous", "user", "developer", "admin"]` | - | `PERMISSION_ROLE_HIERARCHY` | Ordered role hierarchy from lowest to highest privilege (comma-separated in env var) |
| `SessionLockTimeoutSeconds` | int | 10 | 1-60 | `PERMISSION_SESSION_LOCK_TIMEOUT_SECONDS` | Distributed lock expiry for session state/role update operations. Prevents lost updates from concurrent modifications |

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

### Service Registration Observability Event (GH#461)

Permission already publishes `permission.capability-update` via `IMessageBus` on every session recompilation (containing sessionId, version, capabilities, reason) — this serves as the recompilation observability event. GH#461 originally proposed two events, but `permission.recompiled` is redundant with the existing `permission.capability-update`. The remaining proposal is a `permission.service_registered` event for Analytics aggregation of service startup patterns and registration frequency. This would be fire-and-forget with no functional dependency. Low priority — no functional gap exists.

---

## Known Quirks & Caveats

### Bugs

None active.

### Intentional Quirks

1. **activeConnections guards publishing**: Capability updates are only published to sessions in the `active_connections` set. Publishing to sessions without WebSocket connections causes RabbitMQ `exchange not_found` errors that crash the channel.

### Design Considerations

None active. Previous considerations were either fixed (parallel recompilation via `SemaphoreSlim`, atomic set operations via `ICacheableStateStore`) or closed as non-issues (individual key strategy is correct at current scale).

---

## Work Tracking

No active work items.
