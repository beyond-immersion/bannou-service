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

## Tenet Violations (Audit)

### Category: FOUNDATION

1. **Service Implementation Pattern (T6)** - PermissionService.cs:62-67 - Missing null-check guard clauses on constructor dependencies
   - What's wrong: The constructor assigns dependencies without `?? throw new ArgumentNullException(nameof(...))` guards. `logger`, `configuration`, `stateStoreFactory`, `messageBus`, `lockProvider`, `clientEventPublisher` are all assigned directly. Only `eventConsumer` gets validated (implicitly, via `RegisterEventConsumers` usage). T6 requires explicit null checks on every injected dependency.
   - Fix: Add `?? throw new ArgumentNullException(nameof(param))` to each assignment (e.g., `_logger = logger ?? throw new ArgumentNullException(nameof(logger));`)

### Category: IMPLEMENTATION

2. **Error Handling (T7)** - PermissionService.cs:118-196 (and all public methods) - Missing ApiException catch blocks
   - What's wrong: Every try-catch block catches only `Exception`, never distinguishing `ApiException` from unexpected exceptions. T7 requires catching `ApiException` specifically to log as warning and propagate the status code, then catching generic `Exception` for unexpected failures.
   - Fix: Add `catch (ApiException ex) { _logger.LogWarning(...); return ((StatusCodes)ex.StatusCode, null); }` before each `catch (Exception ex)` block.

3. **Multi-Instance Safety (T9)** - PermissionService.cs:768-769 - Using `.Result` on awaited Tasks
   - What's wrong: After `await Task.WhenAll(statesTask, permissionsTask)`, the code accesses `statesTask.Result` and `permissionsTask.Result`. T23 explicitly forbids `.Result` usage. After `Task.WhenAll`, results should be accessed via `await`.
   - Fix: Replace `statesTask.Result` with `await statesTask` and `permissionsTask.Result` with `await permissionsTask`.

4. **Multi-Instance Safety (T9)** - PermissionService.cs:1273-1278 and 1284-1290 - Read-modify-write on shared sets without distributed lock
   - What's wrong: `activeConnections` and `activeSessions` HashSets are read, modified, and saved back without a distributed lock. Multiple instances could read the same set simultaneously, add different sessions, and overwrite each other's additions.
   - Fix: Use `IDistributedLockProvider` to protect these read-modify-write sequences, or use atomic state store operations that support set-add.

5. **Multi-Instance Safety (T9)** - PermissionService.cs:546-552 and 599-606 - Read-modify-write on activeSessions without distributed lock
   - What's wrong: In `UpdateSessionStateAsync` and `UpdateSessionRoleAsync`, the `activeSessions` HashSet is read, a session is added, and saved back without any distributed lock. Concurrent updates from multiple instances will lose data.
   - Fix: Use distributed lock or atomic set-add operations.

6. **Multi-Instance Safety (T9)** - PermissionService.cs:1334-1339 and 1347-1352 - Read-modify-write on activeConnections/activeSessions without distributed lock in disconnect handler
   - What's wrong: Same pattern as above - reading, removing, and saving shared sets without distributed locking. Concurrent disconnections could lose updates.
   - Fix: Use distributed lock or atomic set-remove operations.

7. **Configuration-First (T21)** - PermissionService.cs:1106-1107 - Hardcoded state and role arrays
   - What's wrong: `var states = new[] { "authenticated", "default", "lobby", "in_game" };` and `var roles = new[] { "user", "admin", "anonymous" };` are hardcoded arrays used for endpoint counting in `GetRegisteredServicesAsync`. These are tunables that should come from configuration. Additionally, the `roles` array is missing "developer" and does not match ROLE_ORDER.
   - Fix: Either use the full ROLE_ORDER for roles and derive states from the actual registered permission matrix data (querying registered services for their known states), or define these in configuration.

8. **Configuration-First (T21)** - PermissionService.cs:36 - Hardcoded role hierarchy
   - What's wrong: `ROLE_ORDER = new[] { "anonymous", "user", "developer", "admin" }` is a hardcoded tunable. Adding or reordering roles requires code changes.
   - Fix: Define the role hierarchy in the configuration schema and read from `_configuration.RoleOrder` or similar. Alternatively, if the hierarchy is truly fixed by design, document why configuration is inappropriate.

9. **Async Method Pattern (T23)** - PermissionService.cs:768-769 - `.Result` property access on Task
   - What's wrong: `statesTask.Result` and `permissionsTask.Result` use the `.Result` property which is explicitly forbidden by T23. Even after `await Task.WhenAll`, the canonical pattern is to `await` each task.
   - Fix: Use `var states = await statesTask ?? new Dictionary<string, string>();` and `var permissionsData = await permissionsTask ?? new Dictionary<string, object>();`.

10. **Async Method Pattern (T23)** - PermissionService.cs:1054 and PermissionServiceEvents.cs:153,159,198,286,361,398 - Discarded async calls without await
    - What's wrong: `_ = PublishErrorEventAsync(...)` discards the Task without awaiting it. While `TryPublishErrorAsync` is safe to call, the discarded Task pattern means exceptions inside `PublishErrorEventAsync` (which wraps `TryPublishErrorAsync`) could be silently lost and the method signature implies it should be awaited.
    - Fix: Use `await PublishErrorEventAsync(...)` consistently (as is done in other catch blocks in the same file, e.g., lines 189, 244, 499, etc.).

11. **Internal Model Type Safety (T25)** - PermissionService.cs:355-360 - Anonymous type used for state store persistence
    - What's wrong: `var registrationInfo = new { ServiceId = body.ServiceId, Version = body.Version, RegisteredAtUnix = ... }` stores an anonymous object in the state store. Anonymous types have no stable serialization contract and cannot be deserialized back to a typed model. This violates T25's requirement for proper types in internal models.
    - Fix: Define a typed `ServiceRegistrationInfo` POCO class with proper typed fields and use that instead of an anonymous type.

12. **Internal Model Type Safety (T25)** - PermissionService.cs:1128 - Empty string initialization for version
    - What's wrong: `var version = "";` uses an empty string default that obscures whether version data was found or not. While minor, this suggests a nullable string (`string?`) would be more appropriate.
    - Fix: Use `string? version = null;` and handle the null case explicitly.

### Category: QUALITY

13. **Logging Standards (T10)** - PermissionService.cs:120 - Operation entry logged at Information instead of Debug
    - What's wrong: `_logger.LogInformation("Getting capabilities for session {SessionId}", body.SessionId)` logs an operation entry at Information level. T10 specifies operation entry should be Debug.
    - Fix: Change to `_logger.LogDebug(...)`.

14. **Logging Standards (T10)** - PermissionService.cs:181-182 - Operation completion logged at Information instead of Debug
    - What's wrong: `_logger.LogInformation("Retrieved capabilities for session {SessionId}...")` is routine operation completion that should be Debug, not Information. Information is reserved for significant state changes.
    - Fix: Change to `_logger.LogDebug(...)`.

15. **Logging Standards (T10)** - PermissionService.cs:519 - Operation entry logged at Information instead of Debug
    - What's wrong: `_logger.LogInformation("Updating session {SessionId} state...")` is an operation entry that should be Debug per T10.
    - Fix: Change to `_logger.LogDebug(...)`.

16. **Logging Standards (T10)** - PermissionService.cs:586-587 - Operation entry logged at Information instead of Debug
    - What's wrong: `_logger.LogInformation("Updating session {SessionId} role...")` is an operation entry that should be Debug per T10.
    - Fix: Change to `_logger.LogDebug(...)`.

17. **Logging Standards (T10)** - PermissionService.cs:651,665,683,701,714 - Expected outcomes logged at Information instead of Debug
    - What's wrong: In `ClearSessionStateAsync`, "No states to clear", "Clearing all states", "No state to clear for service", "State does not match filter", and "Clearing state" are all expected operational outcomes that should be Debug, not Information.
    - Fix: Change these to `_logger.LogDebug(...)`.

18. **Logging Standards (T10)** - PermissionService.cs:876-877 - Recompilation entry logged at Information instead of Debug
    - What's wrong: `_logger.LogInformation("Recompiling permissions for session...")` is an internal operation entry. Given this is called for EVERY active session during service registration, it generates excessive log noise at Information level.
    - Fix: Change to `_logger.LogDebug(...)`.

19. **Logging Standards (T10)** - PermissionService.cs:886-887 - Diagnostic data logged at Information instead of Debug
    - What's wrong: `_logger.LogInformation("Found {Count} registered services: {Services}...")` logs diagnostic data during recompilation. This fires for every session recompilation and is diagnostic, not a significant state change.
    - Fix: Change to `_logger.LogDebug(...)`.

20. **Logging Standards (T10)** - PermissionService.cs:447-448 - Diagnostic data logged at Information in locked section
    - What's wrong: `_logger.LogInformation("Current registered services (locked): {Services}"...)` is diagnostic/debugging information, not a significant state change.
    - Fix: Change to `_logger.LogDebug(...)`.

21. **Logging Standards (T10)** - PermissionService.cs:363 - Implementation detail logged at Information
    - What's wrong: `_logger.LogInformation("Stored individual registration marker for {ServiceId} at key {Key}"...)` is an implementation detail about key storage, not a business-significant state change.
    - Fix: Change to `_logger.LogDebug(...)`.

22. **Logging Standards (T10)** - PermissionService.cs:1237-1238 and 1329-1330 - Operation entry logged at Information instead of Debug
    - What's wrong: `_logger.LogInformation("Handling session connected...")` and `_logger.LogInformation("Handling session disconnected...")` are operation entries that should be Debug per T10.
    - Fix: Change to `_logger.LogDebug(...)`.

23. **Logging Standards (T10)** - PermissionServiceEvents.cs:61-62 - Operation entry logged at Information instead of Debug
    - What's wrong: `_logger.LogInformation("Processing service registration for {ServiceName}...")` is an operation entry.
    - Fix: Change to `_logger.LogDebug(...)`.

24. **Logging Standards (T10)** - PermissionServiceEvents.cs:176, 215-218 - Operation entry logged at Information instead of Debug
    - What's wrong: `_logger.LogInformation("Received session state change event...")` and `_logger.LogInformation("Processing session.updated event...")` are operation entries.
    - Fix: Change to `_logger.LogDebug(...)`.

25. **Logging Standards (T10)** - PermissionServiceEvents.cs:334-338, 374-377 - Operation entry logged at Information instead of Debug
    - What's wrong: `_logger.LogInformation("Processing session.connected...")` and `_logger.LogInformation("Processing session.disconnected...")` are operation entries.
    - Fix: Change to `_logger.LogDebug(...)`.

26. **Logging Standards (T10)** - PermissionService.cs:137 - Expected outcome (not found) logged at Warning instead of Debug
    - What's wrong: `_logger.LogWarning("No permissions found for session {SessionId}"...)` - a session with no compiled permissions is an expected scenario (e.g., freshly connected session before recompilation). T10 says expected outcomes should be Debug.
    - Fix: Change to `_logger.LogDebug(...)`.

27. **Logging Standards (T10)** - PermissionService.cs:773 - Expected outcome (not found) logged at Warning instead of Debug
    - What's wrong: `_logger.LogWarning("No session info found for {SessionId}"...)` - querying a session that has no states is an expected outcome, not a warning-worthy condition.
    - Fix: Change to `_logger.LogDebug(...)`.

28. **Naming Conventions (T16)** - PermissionService.cs:36 - Static field uses SCREAMING_CASE instead of PascalCase
    - What's wrong: `ROLE_ORDER` uses SCREAMING_SNAKE_CASE. C# conventions (and T16) specify PascalCase for static readonly fields.
    - Fix: Rename to `RoleOrder` or `RoleHierarchy`.

29. **Naming Conventions (T16)** - PermissionService.cs:39-48 - String constants use SCREAMING_CASE instead of PascalCase
    - What's wrong: `ACTIVE_SESSIONS_KEY`, `ACTIVE_CONNECTIONS_KEY`, `REGISTERED_SERVICES_KEY`, `SERVICE_REGISTERED_KEY`, `SESSION_STATES_KEY`, `SESSION_PERMISSIONS_KEY`, `PERMISSION_MATRIX_KEY`, `PERMISSION_VERSION_KEY`, `SERVICE_LOCK_KEY`, `PERMISSION_HASH_KEY` all use SCREAMING_SNAKE_CASE.
    - Fix: Rename to PascalCase (e.g., `ActiveSessionsKey`, `ActiveConnectionsKey`, etc.).

30. **Naming Conventions (T16)** - PermissionService.cs:369 - Local constant uses SCREAMING_CASE
    - What's wrong: `const string LOCK_RESOURCE = "registered_services_lock"` uses SCREAMING_SNAKE_CASE for a local constant.
    - Fix: Rename to `LockResource` (PascalCase).

31. **XML Documentation (T19)** - PermissionService.cs:1058-1063 - Private helper method missing XML documentation
    - What's wrong: `PublishErrorEventAsync` is missing a `<summary>` tag and `<param>` documentation for its parameters.
    - Fix: Add XML documentation with summary and param tags.

32. **XML Documentation (T19)** - PermissionService.cs:1386 - Private helper method `DetermineHighestPriorityRole` missing `<param>` and `<returns>` tags
    - What's wrong: The method has a `<summary>` but lacks `<param name="roles">` and `<returns>` documentation.
    - Fix: Add `<param>` and `<returns>` tags.

33. **XML Documentation (T19)** - PermissionServiceEvents.cs:294 - Private helper method `DetermineHighestRoleFromEvent` missing `<param>` and `<returns>` tags
    - What's wrong: Same as above - has `<summary>` but lacks `<param>` and `<returns>` documentation.
    - Fix: Add `<param>` and `<returns>` tags.
