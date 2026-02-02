# Resource Plugin Deep Dive

> **Plugin**: lib-resource
> **Layer**: L1 (App Foundation)
> **Schema**: schemas/resource-api.yaml
> **Version**: 1.0.0
> **State Stores**: resource-refcounts (Redis), resource-cleanup (Redis), resource-grace (Redis)

---

## Overview

Resource reference tracking and lifecycle management for foundational resources. Enables foundational services (L2) to safely delete resources by tracking references from higher-layer consumers (L3/L4) without violating the service hierarchy. Higher-layer services publish reference events when they create/delete references to foundational resources. This service maintains the reference counts using Redis sets and coordinates cleanup callbacks when resources are deleted.

**Key Design Principle**: lib-resource (L1) uses opaque string identifiers for `resourceType` and `sourceType`. It does NOT enumerate or validate these against any service registry - that would create implicit coupling to higher layers. The strings are just identifiers that consumers self-report.

**Why L1**: Any layer can depend on L1. Resources being tracked are at L2 or higher, and their consumers are at L3/L4. By placing this service at L1, all layers can use it without hierarchy violations.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Get Redis state stores for reference sets, cleanup callbacks, and grace periods |
| lib-state (`ICacheableStateStore<T>`) | Set operations (`AddToSetAsync`, `RemoveFromSetAsync`, `GetSetAsync`, `SetCountAsync`, `DeleteSetAsync`) for atomic reference tracking |
| lib-state (`IDistributedLockProvider`) | Distributed locks during cleanup execution to prevent concurrent cleanup |
| lib-messaging (`IMessageBus`) | Publishing `resource.grace-period.started` and `resource.cleanup.callback-failed` events |
| lib-mesh (`IServiceNavigator`) | Executing cleanup callbacks via `ExecutePreboundApiBatchAsync` |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-character | Queries `/resource/check` for L4 references in `CheckCharacterReferencesAsync` |
| lib-actor | Publishes `resource.reference.registered/unregistered` in SpawnActorAsync/StopActorAsync; cleanup via `/actor/cleanup-by-character` |
| lib-character-encounter | Publishes reference events in RecordEncounterAsync/DeleteEncounterAsync; cleanup via `/character-encounter/delete-by-character` |
| lib-character-history | Publishes reference events for participations and backstory; cleanup via `/character-history/delete-all` |
| lib-character-personality | Publishes reference events for personality/combat prefs; cleanup via `/character-personality/cleanup-by-character` |
| lib-scene (planned) | Publishes reference events for scene-to-character references; registers cleanup callback |

---

## State Storage

**Stores**: 3 state stores (all Redis-backed)

| Store | Backend | Key Prefix | Purpose |
|-------|---------|------------|---------|
| `resource-refcounts` | Redis | `resource:ref` | Reference tracking via sets |
| `resource-cleanup` | Redis | `resource:cleanup` | Cleanup callback definitions |
| `resource-grace` | Redis | `resource:grace` | Grace period timestamps |

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `{resourceType}:{resourceId}:sources` | Set of `ResourceReferenceEntry` | All entities referencing this resource |
| `{resourceType}:{resourceId}:grace` | `GracePeriodRecord` | When refcount became zero |
| `callback:{resourceType}:{sourceType}` | `CleanupCallbackDefinition` | Cleanup endpoint for a source type |
| `callback-index:{resourceType}` | Set of `string` | Source types with registered callbacks (for enumeration without KEYS/SCAN) |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `resource.grace-period.started` | `ResourceGracePeriodStartedEvent` | Resource refcount reaches zero via `UnregisterReferenceAsync` |
| `resource.cleanup.callback-failed` | `ResourceCleanupCallbackFailedEvent` | Cleanup callback returns non-2xx during `ExecuteCleanupAsync` |

### Consumed Events

| Topic | Event Type | Handler |
|-------|-----------|---------|
| `resource.reference.registered` | `ResourceReferenceRegisteredEvent` | `HandleReferenceRegisteredAsync` - delegates to `RegisterReferenceAsync` |
| `resource.reference.unregistered` | `ResourceReferenceUnregisteredEvent` | `HandleReferenceUnregisteredAsync` - delegates to `UnregisterReferenceAsync` |

---

## Configuration

| Property | Env Var | Default | Used | Purpose |
|----------|---------|---------|------|---------|
| `DefaultGracePeriodSeconds` | `RESOURCE_DEFAULT_GRACE_PERIOD_SECONDS` | 604800 (7 days) | Yes | Grace period before cleanup eligible |
| `CleanupLockExpirySeconds` | `RESOURCE_CLEANUP_LOCK_EXPIRY_SECONDS` | 300 | Yes | Distributed lock timeout during cleanup |
| `DefaultCleanupPolicy` | `RESOURCE_DEFAULT_CLEANUP_POLICY` | BEST_EFFORT | Yes | Policy when not specified per-request |
| `CleanupCallbackTimeoutSeconds` | `RESOURCE_CLEANUP_CALLBACK_TIMEOUT_SECONDS` | 30 | Yes | Timeout for cleanup callback execution |

---

## DI Services & Helpers

| Service | Role |
|---------|------|
| `ILogger<ResourceService>` | Structured logging |
| `ResourceServiceConfiguration` | Typed configuration access |
| `IStateStoreFactory` | State store access for all three stores |
| `IDistributedLockProvider` | Acquiring cleanup locks |
| `IMessageBus` | Publishing events |
| `IServiceNavigator` | Executing cleanup callbacks |
| `IEventConsumer` | Registering event subscription handlers |

**Internal Types** (defined in ResourceService.cs):
| Type | Role |
|------|------|
| `ResourceReferenceEntry` | Set member for reference tracking; equality based on `SourceType` + `SourceId` |
| `GracePeriodRecord` | Records when refcount became zero |
| `CleanupCallbackDefinition` | Stores callback registration (service, endpoint, template, onDeleteAction) |

---

## API Endpoints (Implementation Notes)

### Reference Management

| Endpoint | Notes |
|----------|-------|
| `POST /resource/register` | Uses `AddToSetAsync` for atomic add; clears grace period if new reference added |
| `POST /resource/unregister` | Uses `RemoveFromSetAsync`; publishes `grace-period.started` event when refcount reaches zero |
| `POST /resource/check` | Derives refcount from `SetCountAsync`; computes `isCleanupEligible` from grace period timestamp |
| `POST /resource/list` | Returns all set members; supports `filterSourceType` and `limit` |

### Cleanup Management

| Endpoint | Notes |
|----------|-------|
| `POST /resource/cleanup/define` | Upserts callback definition with `onDeleteAction`; maintains `callback-index:{resourceType}` set for enumeration; `serviceName` defaults to `sourceType` if not specified |
| `POST /resource/cleanup/execute` | Full cleanup flow: RESTRICT check → pre-check → lock → re-validate → execute CASCADE/DETACH callbacks → clear state |

**OnDeleteAction Behavior** (per-callback, configured via `/resource/cleanup/define`):

| Action | Behavior |
|--------|----------|
| `CASCADE` (default) | Execute cleanup callback to delete dependent entities |
| `RESTRICT` | Block resource deletion if references of this sourceType exist |
| `DETACH` | Execute cleanup callback (consumer implements null-out/detach logic) |

**Cleanup Execution Flow**:
1. Get all callbacks and identify RESTRICT vs CASCADE/DETACH callbacks
2. **RESTRICT check**: If any active references have RESTRICT callbacks, return failure immediately with `"Blocked by RESTRICT policy from: {sourceTypes}"`
3. Pre-check refcount and grace period (without lock)
4. If blocked by non-RESTRICT reasons (unhandled refs, grace period), return early with reason
5. Acquire distributed lock on `cleanup:{resourceType}:{resourceId}`
6. Re-validate refcount under lock (race protection)
7. Execute only CASCADE and DETACH callbacks via `IServiceNavigator.ExecutePreboundApiBatchAsync` in parallel with configured timeout (`CleanupCallbackTimeoutSeconds`)
8. Per cleanup policy: abort or continue on failures
9. Delete grace period record and reference set
10. Release lock

---

## Visual Aid

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         Resource State Store Layout                         │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  resource-refcounts (Redis)                                                 │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │ Key: {resourceType}:{resourceId}:sources                            │   │
│  │ Type: Redis Set                                                     │   │
│  │ Members: [                                                          │   │
│  │   { SourceType: "actor", SourceId: "abc-123", RegisteredAt: "..." },│   │
│  │   { SourceType: "scene", SourceId: "def-456", RegisteredAt: "..." } │   │
│  │ ]                                                                   │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│  resource-grace (Redis)                                                     │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │ Key: {resourceType}:{resourceId}:grace                              │   │
│  │ Type: JSON object                                                   │   │
│  │ Value: { ResourceType, ResourceId, ZeroTimestamp }                  │   │
│  │ Created: When refcount becomes zero                                 │   │
│  │ Deleted: When cleanup executes OR new reference registered          │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│  resource-cleanup (Redis)                                                   │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │ Key: callback:{resourceType}:{sourceType}                           │   │
│  │ Type: JSON object                                                   │   │
│  │ Value: { ServiceName, CallbackEndpoint, PayloadTemplate, ... }      │   │
│  ├─────────────────────────────────────────────────────────────────────┤   │
│  │ Key: callback-index:{resourceType}                                  │   │
│  │ Type: Redis Set (for enumeration without KEYS/SCAN)                 │   │
│  │ Members: [ "actor", "scene", "encounter" ]                          │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Stubs & Unimplemented Features

None currently.

---

## Potential Extensions

1. **Per-resource-type cleanup policies**: Currently `DefaultCleanupPolicy` applies globally; could add per-resource-type configuration via `DefineCleanupRequest`.

2. **Automatic cleanup scheduler**: Background service that periodically scans for resources past grace period and triggers cleanup (opt-in per resource type).

3. **Reference type metadata**: Allow consumers to attach metadata to references (e.g., reference strength, priority for cleanup ordering).

4. **Cleanup callback ordering**: Currently all callbacks execute in parallel; could add priority/ordering for sequential cleanup dependencies.

5. **Reference lifecycle hooks**: Pre-register/post-unregister hooks for validation or side effects.

---

## Integration Pattern

### For Higher-Layer Services (L3/L4)

1. **At startup (OnRunningAsync)**: Register cleanup callbacks via `/resource/cleanup/define`

   **CRITICAL**: Cleanup callback registration MUST happen in `OnRunningAsync`, NOT `OnStartAsync`.
   The `OnRunningAsync` lifecycle hook runs AFTER all plugins have completed their `StartAsync` phase,
   ensuring lib-resource is fully available. Registering during `OnStartAsync` is unsafe because
   plugin load order is not guaranteed beyond infrastructure plugins (lib-state, lib-messaging, lib-mesh).

   ```csharp
   // In YourServicePlugin.OnRunningAsync():
   var resourceClient = scope.ServiceProvider.GetService<IResourceClient>();
   if (resourceClient != null)
   {
       await YourService.RegisterResourceCleanupCallbacksAsync(resourceClient, ct);
   }
   ```

   The generated `RegisterResourceCleanupCallbacksAsync` method (from `x-references` schema extension) calls:
   ```csharp
   POST /resource/cleanup/define
   {
     "resourceType": "character",
     "sourceType": "actor",
     "serviceName": "actor",
     "callbackEndpoint": "/actor/cleanup-by-character",
     "payloadTemplate": "{\"characterId\": \"{{resourceId}}\"}",
     "onDeleteAction": "CASCADE"  // or "RESTRICT" / "DETACH"
   }
   ```

   **OnDeleteAction Options**:
   - `CASCADE` (default): Delete dependent entities when resource is deleted
   - `RESTRICT`: Block resource deletion while references of this type exist
   - `DETACH`: Nullify/detach references when resource is deleted (consumer implements)

2. **On entity creation with reference**: Publish event
   ```csharp
   await _messageBus.TryPublishAsync("resource.reference.registered",
       new ResourceReferenceRegisteredEvent
       {
           ResourceType = "character",
           ResourceId = characterId,
           SourceType = "actor",
           SourceId = actorId,
           Timestamp = DateTimeOffset.UtcNow
       }, ct);
   ```

3. **On entity deletion**: Publish event
   ```csharp
   await _messageBus.TryPublishAsync("resource.reference.unregistered",
       new ResourceReferenceUnregisteredEvent { ... }, ct);
   ```

4. **Implement cleanup endpoint**: Handle cascading deletion when called back

### For Foundational Services (L2)

1. **Before deletion**: Check references via `/resource/check`
2. **Execute cleanup**: Call `/resource/cleanup/execute`
3. **Proceed with deletion**: After cleanup succeeds

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

None.

### Intentional Quirks (Documented Behavior)

1. **Opaque string identifiers for resourceType/sourceType**: This is deliberate per SCHEMA-RULES.md - lib-resource (L1) must not enumerate L2+ services or entity types, so these are plain strings with no validation.

2. **Set-based reference counting**: Reference count is derived from set cardinality (`SetCountAsync`), not a separate counter. This avoids Lua scripts for atomic increment/decrement. The small race window between operations is acceptable because cleanup always re-validates under distributed lock.

3. **Event handlers delegate to API methods**: `HandleReferenceRegisteredAsync` and `HandleReferenceUnregisteredAsync` simply construct requests and call the API methods. This ensures consistent logic but means event processing pays the full API path cost.

4. **Cleanup lock uses refcount store name**: The distributed lock is acquired with `storeName: StateStoreDefinitions.ResourceRefcounts` even though it's a logical lock, not a data lock. This is intentional - the lock protects the refcount state.

5. **Cleanup callbacks registered in OnRunningAsync**: Consumer plugins MUST register their cleanup callbacks in `OnRunningAsync`, not `OnStartAsync`. This is because `OnRunningAsync` runs after ALL plugins have completed their `StartAsync` phase, guaranteeing lib-resource is available. Plugin load order is not guaranteed beyond infrastructure plugins (L0), so registering during `OnStartAsync` could fail if lib-resource hasn't started yet. See ActorServicePlugin for the reference implementation.

### Design Considerations (Requires Planning)

1. **Callback index is never cleaned up**: When a cleanup callback is undefined/removed (no API for this exists), the source type remains in `callback-index:{resourceType}`. Not a bug since callbacks are typically permanent, but could accumulate stale entries.

---

## Work Tracking

### Completed
- [x] Schema files (api, events, configuration)
- [x] State store definitions
- [x] Service implementation with ICacheableStateStore
- [x] Event handlers for reference tracking wired up via IEventConsumer
- [x] SERVICE_HIERARCHY.md updated
- [x] Unit tests for reference counting logic (25 tests)
- [x] Bug fixes: ParseIsoDuration removed, CleanupCallbackTimeoutSeconds wired up, serviceName defaults to sourceType
- [x] Phase 2: Schema extension (`x-references`, `x-resource-lifecycle`) documented in SCHEMA-RULES.md
- [x] Phase 2: `scripts/generate-references.py` code generator created
- [x] Phase 2: Generator integrated into `scripts/generate-all-services.sh` pipeline
- [x] Phase 2: Example `x-references` added to actor-api.yaml, generates `ActorReferenceTracking.cs`
- [x] Phase 3: sourceId changed to opaque string (supports non-Guid IDs like Actor's `brain-{guid}` format)
- [x] Phase 3: Actor service wired up - `RegisterCharacterReferenceAsync` in SpawnActorAsync, `UnregisterCharacterReferenceAsync` in StopActorAsync
- [x] Phase 3: Cleanup endpoint `/actor/cleanup-by-character` added to actor-api.yaml and implemented
- [x] Phase 3: `RegisterResourceCleanupCallbacksAsync` wired up in ActorServicePlugin.OnRunningAsync
- [x] Phase 4: CharacterService queries lib-resource for L4 references in CheckCharacterReferencesAsync
- [x] Phase 4: character-encounter integrated - x-references, cleanup endpoint `/character-encounter/delete-by-character`
- [x] Phase 4: character-history integrated - x-references, cleanup via existing `/character-history/delete-all`
- [x] Phase 4: character-personality integrated - x-references, cleanup endpoint `/character-personality/cleanup-by-character`

- [x] OnDeleteAction enum wired up - per-callback deletion behavior (CASCADE/RESTRICT/DETACH)
- [x] idempotencyKey stub removed from RegisterReferenceRequest schema
- [x] Unit tests for OnDeleteAction functionality (30 total tests)

### Pending
None - lib-resource integration complete for all L4 character consumers.

---

## Related Documents

- [SERVICE_HIERARCHY.md](../reference/SERVICE_HIERARCHY.md) - Layer placement rationale
- [TENETS.md](../reference/TENETS.md) - Compliance requirements
- [SCHEMA-RULES.md](../reference/SCHEMA-RULES.md) - `x-references` and `x-resource-lifecycle` schema extensions
- [Planning Document](~/.claude/plans/typed-crunching-muffin.md) - Full implementation plan with phases
