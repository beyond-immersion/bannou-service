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
| lib-state (`IStateStoreFactory`) | Redis state stores for reference sets, cleanup callbacks, and grace periods |
| lib-state (`ICacheableStateStore`) | Set operations for atomic reference tracking |
| lib-state (`IDistributedLockProvider`) | Distributed locks during cleanup execution |
| lib-messaging (`IMessageBus`) | Publishing grace period and cleanup failure events |
| lib-mesh (`IServiceNavigator`) | Executing cleanup callbacks via `ExecutePreboundApiBatchAsync` |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-character (planned) | Calls `/resource/check` before deletion; calls `/resource/cleanup/execute` to coordinate cascading cleanup |
| lib-actor (planned) | Publishes `resource.reference.registered` when creating actors that reference characters; registers cleanup callback |
| lib-character-encounter (planned) | Publishes reference events for character encounters; registers cleanup callback |
| lib-scene (planned) | Publishes reference events for scene-to-character references; registers cleanup callback |

---

## State Storage

**Stores**: 3 state stores (all Redis-backed)

| Store | Backend | Purpose |
|-------|---------|---------|
| `resource-refcounts` | Redis | Reference tracking via sets |
| `resource-cleanup` | Redis | Cleanup callback definitions |
| `resource-grace` | Redis | Grace period timestamps |

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `{resourceType}:{resourceId}:sources` | Set of `ResourceReferenceEntry` | All entities referencing this resource |
| `{resourceType}:{resourceId}:grace` | `GracePeriodRecord` | When refcount became zero |
| `callback:{resourceType}:{sourceType}` | `CleanupCallbackDefinition` | Cleanup endpoint for a source type |
| `callback-index:{resourceType}` | Set of `string` | Source types with registered callbacks |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `resource.grace-period.started` | `ResourceGracePeriodStartedEvent` | Resource refcount reaches zero |
| `resource.cleanup.callback-failed` | `ResourceCleanupCallbackFailedEvent` | Cleanup callback returns non-2xx |

### Consumed Events

| Topic | Event Type | Handler |
|-------|-----------|---------|
| `resource.reference.registered` | `ResourceReferenceRegisteredEvent` | `HandleReferenceRegisteredAsync` - adds to reference set |
| `resource.reference.unregistered` | `ResourceReferenceUnregisteredEvent` | `HandleReferenceUnregisteredAsync` - removes from reference set |

---

## Configuration

| Property | Env Var | Default | Description |
|----------|---------|---------|-------------|
| `DefaultGracePeriodSeconds` | `RESOURCE_DEFAULT_GRACE_PERIOD_SECONDS` | 604800 (7 days) | Grace period before cleanup eligible |
| `CleanupCallbackTimeoutSeconds` | `RESOURCE_CLEANUP_CALLBACK_TIMEOUT_SECONDS` | 30 | Timeout per cleanup callback |
| `MaxCallbackRetries` | `RESOURCE_MAX_CALLBACK_RETRIES` | 3 | Retries per callback on transient failure |
| `CleanupLockExpirySeconds` | `RESOURCE_CLEANUP_LOCK_EXPIRY_SECONDS` | 300 | Distributed lock timeout during cleanup |
| `DefaultCleanupPolicy` | `RESOURCE_DEFAULT_CLEANUP_POLICY` | BEST_EFFORT | Policy when not specified per-request |

---

## API Endpoints

### Reference Management

| Endpoint | Purpose |
|----------|---------|
| `POST /resource/register` | Register a reference (also done via events) |
| `POST /resource/unregister` | Unregister a reference (also done via events) |
| `POST /resource/check` | Check refcount and cleanup eligibility |
| `POST /resource/list` | List all references to a resource |

### Cleanup Management

| Endpoint | Purpose |
|----------|---------|
| `POST /resource/cleanup/define` | Register a cleanup callback for a resource type |
| `POST /resource/cleanup/execute` | Execute cleanup for a resource (with lock and validation) |

---

## Core Concepts

### Reference Tracking via Sets

References are stored in Redis sets using `ICacheableStateStore.AddToSetAsync/RemoveFromSetAsync`. The set membership IS the source of truth - there's no separate counter. Count is derived via `SetCountAsync` (Redis SCARD).

```
Key: character:{characterId}:sources
Members:
  - {SourceType: "actor", SourceId: "abc-123", RegisteredAt: "2024-01-01T..."}
  - {SourceType: "scene", SourceId: "def-456", RegisteredAt: "2024-01-02T..."}
```

### Grace Period

When a resource's refcount reaches zero, the service:
1. Records `lastZeroTimestamp` in the grace store
2. Publishes `resource.grace-period.started` event
3. Returns `gracePeriodEndsAt` in subsequent `/resource/check` responses

Cleanup is only eligible after the grace period passes. This prevents premature deletion during transient states (e.g., migration, reprocessing).

### Cleanup Execution Flow

```
1. Foundational service calls /resource/check
   → Returns refcount, isCleanupEligible, blockers list

2. If refCount > 0: Reject with blockers list

3. If isCleanupEligible = false: Reject with gracePeriodEndsAt

4. Foundational service calls /resource/cleanup/execute:
   a. Acquire distributed lock on resource:{type}:{id}
   b. Re-validate refcount=0 under lock (via SetCountAsync)
   c. If refcount changed → return 409 Conflict
   d. Execute cleanup callbacks via IServiceNavigator.ExecutePreboundApiBatchAsync
   e. Per cleanup policy: abort or continue on failures
   f. Clear grace period and reference set
   g. Release lock

5. Foundational service proceeds with actual deletion
```

### Cleanup Policies

| Policy | Behavior |
|--------|----------|
| `BEST_EFFORT` | Proceed even if some callbacks fail |
| `ALL_REQUIRED` | Abort if any callback fails |

### Callback Registration

Services register cleanup callbacks at startup:

```json
POST /resource/cleanup/define
{
  "resourceType": "character",
  "sourceType": "actor",
  "serviceName": "actor",
  "callbackEndpoint": "/actor/cleanup-by-character",
  "payloadTemplate": "{\"characterId\": \"{{resourceId}}\"}"
}
```

When cleanup executes, the template is substituted with context values and the endpoint is called.

---

## Integration Pattern

### For Higher-Layer Services (L3/L4)

1. **At startup**: Register cleanup callbacks via `/resource/cleanup/define`

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
       new ResourceReferenceUnregisteredEvent
       {
           ResourceType = "character",
           ResourceId = characterId,
           SourceType = "actor",
           SourceId = actorId,
           Timestamp = DateTimeOffset.UtcNow
       }, ct);
   ```

4. **Implement cleanup endpoint**: Handle cascading deletion
   ```csharp
   public async Task<(StatusCodes, CleanupByCharacterResponse?)> CleanupByCharacterAsync(
       CleanupByCharacterRequest body, CancellationToken ct)
   {
       // Delete all actors referencing this character
       // (References already unregistered by lib-resource before this callback)
       ...
   }
   ```

### For Foundational Services (L2)

1. **Before deletion**: Check references
   ```csharp
   var (status, check) = await _resourceClient.CheckReferencesAsync(
       new CheckReferencesRequest
       {
           ResourceType = "character",
           ResourceId = characterId
       }, ct);

   if (check.RefCount > 0)
   {
       return (StatusCodes.Conflict, new DeleteResponse
       {
           Success = false,
           Blockers = check.Sources.Select(s => $"{s.SourceType}:{s.SourceId}").ToList()
       });
   }
   ```

2. **Execute cleanup**: Coordinate cascading deletion
   ```csharp
   var (status, result) = await _resourceClient.ExecuteCleanupAsync(
       new ExecuteCleanupRequest
       {
           ResourceType = "character",
           ResourceId = characterId,
           GracePeriodOverride = "PT0S" // Skip grace period if desired
       }, ct);

   if (!result.Success)
   {
       return (StatusCodes.Conflict, new DeleteResponse
       {
           Success = false,
           AbortReason = result.AbortReason
       });
   }
   ```

3. **Proceed with deletion**: After cleanup succeeds
   ```csharp
   await _characterStore.DeleteAsync(characterId, ct);
   await _messageBus.TryPublishAsync("character.deleted", new CharacterDeletedEvent {...}, ct);
   ```

---

## Implementation Notes

### Opaque String Identifiers

`resourceType` and `sourceType` are intentionally strings, not enums. This is per SCHEMA-RULES.md "When NOT to Create Enums" - lib-resource (L1) must not enumerate L2+ services or entity types, as that would create implicit coupling.

### Set-Based Reference Counting

The reference count is derived from set cardinality, not a separate counter. This avoids the need for Lua scripts to maintain atomicity across increment/decrement operations. The small race window between "remove + check count + set lastZero" is acceptable because cleanup execution always re-validates under distributed lock.

### Index Maintenance

Cleanup callbacks are indexed by resource type. When `DefineCleanupCallbackAsync` is called, it adds the source type to a callback index set, enabling efficient enumeration of all callbacks for a resource type.

---

## Work Tracking

### Completed
- [x] Schema files (api, events, configuration)
- [x] State store definitions
- [x] Service implementation with ICacheableStateStore
- [x] Event handlers for reference tracking
- [x] SERVICE_HIERARCHY.md updated

### Pending
- [ ] Phase 2: Schema extension (`x-references`) and code generator
- [ ] Phase 3: First consumer integration (Actor service)
- [ ] Phase 4: Migration of CharacterService to use lib-resource
- [ ] Unit tests for reference counting logic

---

## Related Documents

- [SERVICE_HIERARCHY.md](../reference/SERVICE_HIERARCHY.md) - Layer placement rationale
- [TENETS.md](../reference/TENETS.md) - Compliance requirements
- [SCHEMA-RULES.md](../reference/SCHEMA-RULES.md) - Why strings instead of enums
