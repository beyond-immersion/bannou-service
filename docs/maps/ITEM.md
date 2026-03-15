# Item Implementation Map

> **Plugin**: lib-item
> **Schema**: schemas/item-api.yaml
> **Layer**: GameFoundation
> **Deep Dive**: [docs/plugins/ITEM.md](../plugins/ITEM.md)

---

## Summary

| Field | Value |
|-------|-------|
| Plugin | lib-item |
| Layer | L2 GameFoundation |
| Endpoints | 17 |
| State Stores | item-template-store (MySQL), item-template-cache (Redis), item-instance-store (MySQL), item-instance-cache (Redis), item-lock (Redis) |
| Events Published | 12 (item.template.created, item.template.updated, item.template.deleted, item.instance.batch-created, item.instance.batch-modified, item.instance.batch-destroyed, item.instance.bound, item.instance.unbound, item.used, item.use-failed, item.use-step-completed, item.use-step-failed) |
| Events Consumed | 0 |
| Client Events | 0 |
| Background Services | 1 (EventBatcherWorker) |

---

## State

**Store**: `item-template-store` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `tpl:{templateId}` | `ItemTemplateModel` | Template definition |
| `tpl-code:{gameId}:{code}` | `string` | Code+game uniqueness index (value = templateId) |
| `tpl-game:{gameId}` | `List<string>` (JSON) | All template IDs for a game service |
| `all-templates` | `List<string>` (JSON) | Global index of all template IDs |

**Store**: `item-template-cache` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `tpl:{templateId}` | `ItemTemplateModel` | Template hot cache (TTL: config.TemplateCacheTtlSeconds) |

**Store**: `item-instance-store` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `inst:{instanceId}` | `ItemInstanceModel` | Instance data |
| `inst-container:{containerId}` | `List<string>` (JSON) | Instance IDs in a container |
| `inst-template:{templateId}` | `List<string>` (JSON) | Instance IDs of a template |

**Store**: `item-instance-cache` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `inst:{instanceId}` | `ItemInstanceModel` | Instance hot cache (TTL: config.InstanceCacheTtlSeconds) |

**Store**: `item-lock` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `{instanceId}` | lock | Distributed lock for container-change modifications |
| `item-use-step:{instanceId}` | lock | Distributed lock for UseItemStep operations |

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (IStateStoreFactory) | L0 | Hard | 5 state stores for templates, instances, cache, locks |
| lib-state (IDistributedLockProvider) | L0 | Hard | Locks for container changes and UseItemStep |
| lib-messaging (IMessageBus) | L0 | Hard | Publishing all 12 event topics |
| lib-telemetry (ITelemetryProvider) | L0 | Hard | Span instrumentation for async helpers |
| lib-contract (IContractClient) | L1 | Hard | Contract creation, milestone completion, contract queries for item use flows |

**Notes:**
- Item is the dispatcher side of `IItemInstanceDestructionListener` (L2->L4 push). Not yet implemented in code (GH #490). When wired, `DestroyItemInstanceAsync` will dispatch to registered listeners after the destroy event is published.
- No L2 service client dependencies — one of the lowest-dependency L2 services.
- No events consumed (`x-event-subscriptions: []`). Same-layer callers (Inventory, Collection) use Item's API directly per FOUNDATION TENETS.

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `item.template.created` | `ItemTemplateCreatedEvent` | Template created |
| `item.template.updated` | `ItemTemplateUpdatedEvent` | Template fields changed (changedFields populated) or deprecated (changedFields for deprecation fields) |
| `item.template.deleted` | `ItemTemplateDeletedEvent` | Template permanently deleted by clean-deprecated sweep |
| `item.instance.batch-created` | `ItemInstanceBatchCreatedEvent` | Batch of instance creations (accumulated by ItemInstanceEventBatcher, flushed by EventBatcherWorker) |
| `item.instance.batch-modified` | `ItemInstanceBatchModifiedEvent` | Batch of instance modifications (accumulated, deduped by instanceId per window) |
| `item.instance.batch-destroyed` | `ItemInstanceBatchDestroyedEvent` | Batch of instance destructions (accumulated, flushed on interval or max batch size) |
| `item.instance.bound` | `ItemInstanceBoundEvent` | Instance bound to character |
| `item.instance.unbound` | `ItemInstanceUnboundEvent` | Instance binding removed |
| `item.used` | `ItemUsedEvent` | Batched use successes (deduped by templateId+userId within window) |
| `item.use-failed` | `ItemUseFailedEvent` | Batched use failures (deduped by templateId+userId within window) |
| `item.use-step-completed` | `ItemUseStepCompletedEvent` | Multi-step use milestone completed |
| `item.use-step-failed` | `ItemUseStepFailedEvent` | Multi-step use milestone failed |

---

## Events Consumed

This plugin does not consume external events.

---

## DI Services

| Service | Role |
|---------|------|
| `ILogger<ItemService>` | Structured logging |
| `ItemServiceConfiguration` | All 18 config properties (defaults cached at construction) |
| `IStateStoreFactory` | State store access (7 stores, constructor-cached as `_templateStore`, `_templateStringStore`, `_templateCacheStore`, `_instanceStore`, `_instanceStringStore`, `_instanceQueryableStore`, `_instanceCacheStore`) |
| `IMessageBus` | Event publishing |
| `IDistributedLockProvider` | Distributed locks for container changes and UseItemStep |
| `ITelemetryProvider` | Span instrumentation |
| `IContractClient` | Contract creation and milestone completion for item use flows |
| `ItemInstanceEventBatcher` | Singleton batcher for instance lifecycle batch events (created/modified/destroyed) |

---

## Method Index

| Method | Route | Source | Roles | Mutates | Publishes |
|--------|-------|--------|-------|---------|-----------|
| CreateItemTemplate | POST /item/template/create | generated | developer | template, code-index, game-index, all-index, cache | item.template.created |
| GetItemTemplate | POST /item/template/get | generated | user | - | - |
| ListItemTemplates | POST /item/template/list | generated | user | - | - |
| UpdateItemTemplate | POST /item/template/update | generated | developer | template, cache | item.template.updated |
| DeprecateItemTemplate | POST /item/template/deprecate | generated | admin | template, cache | item.template.updated |
| CreateItemInstance | POST /item/instance/create | generated | developer | instance, container-index, template-index, cache | item.instance.batch-created (deferred) |
| GetItemInstance | POST /item/instance/get | generated | user | - | - |
| ModifyItemInstance | POST /item/instance/modify | generated | developer | instance, container-index, cache | item.instance.batch-modified (deferred) |
| BindItemInstance | POST /item/instance/bind | generated | developer | instance, cache | item.instance.bound |
| UnbindItemInstance | POST /item/instance/unbind | generated | admin | instance, cache | item.instance.unbound |
| DestroyItemInstance | POST /item/instance/destroy | generated | developer | instance, container-index, template-index, cache | item.instance.batch-destroyed (deferred) |
| UseItem | POST /item/use | generated | user | instance, container-index, template-index, cache | item.used, item.use-failed, item.instance.batch-modified (deferred), item.instance.batch-destroyed (deferred) |
| UseItemStep | POST /item/use-step | generated | user | instance, container-index, template-index, cache | item.use-step-completed, item.use-step-failed, item.instance.batch-modified (deferred), item.instance.batch-destroyed (deferred) |
| ListItemsByContainer | POST /item/instance/list-by-container | generated | user | - | - |
| ListItemsByTemplate | POST /item/instance/list-by-template | generated | admin | - | - |
| BatchGetItemInstances | POST /item/instance/batch-get | generated | user | - | - |
| CleanDeprecatedItemTemplates | POST /item/template/clean-deprecated | generated | admin | template, code-index, game-index, all-index, cache, reverse-index | item.template.deleted |

---

## Methods

### CreateItemTemplate
POST /item/template/create | Roles: [developer]

```
READ template-store:"tpl-code:{gameId}:{code}" [with ETag]  -> 409 if non-empty (code already claimed)
ETAG-WRITE template-store:"tpl-code:{gameId}:{code}" <- templateId  -> 409 if concurrent claim
// Apply config defaults: rarity, weightPrecision, soulboundType when not in request
// Serialize stats, effects, requirements, display, metadata as JSON strings
WRITE template-store:"tpl:{templateId}" <- ItemTemplateModel from request
WRITE template-cache:"tpl:{templateId}" <- ItemTemplateModel (with TTL)
// AddToListAsync with optimistic concurrency retries (up to ListOperationMaxRetries)
ETAG-WRITE template-store:"tpl-game:{gameId}" <- append templateId to list
ETAG-WRITE template-store:"all-templates" <- append templateId to list
PUBLISH item.template.created { templateId, code, gameId, category, rarity, quantityModel, scope, ... }
RETURN (200, ItemTemplateResponse)
```

### GetItemTemplate
POST /item/template/get | Roles: [user]

```
// ResolveTemplateAsync: templateId direct lookup OR code+gameId index lookup
IF body.templateId is set
  READ template-cache:"tpl:{templateId}"
  IF cache miss
    READ template-store:"tpl:{templateId}"                   -> 404 if null
    WRITE template-cache:"tpl:{templateId}" <- model (with TTL)
ELSE
  READ template-store:"tpl-code:{gameId}:{code}"             -> 404 if empty
  // Then resolve by templateId via cache read-through as above
RETURN (200, ItemTemplateResponse)
```

### ListItemTemplates
POST /item/template/list | Roles: [user]

```
IF body.gameId is set
  READ template-store:"tpl-game:{gameId}" -> templateId list
ELSE
  READ template-store:"all-templates" -> templateId list
FOREACH templateId in list
  READ template via cache read-through (cache -> store -> populate cache)
// In-memory filters: includeInactive, includeDeprecated, category, subcategory,
//   rarity, scope, realmId, tags (all must match), search (name/description contains)
// Pagination: skip(offset), take(limit) applied after filtering
RETURN (200, ListItemTemplatesResponse { templates, totalCount })
```

### UpdateItemTemplate
POST /item/template/update | Roles: [developer]

```
READ template-store:"tpl:{templateId}"                       -> 404 if null
// Patch mutable fields only (code, gameId, quantityModel, scope are immutable)
// Each field applied only if non-null in request; track changed field names in changedFields list
WRITE template-store:"tpl:{templateId}" <- patched ItemTemplateModel
DELETE template-cache:"tpl:{templateId}"
PUBLISH item.template.updated { templateId, code, gameId, ..., changedFields }
RETURN (200, ItemTemplateResponse)
```

### DeprecateItemTemplate
POST /item/template/deprecate | Roles: [admin]

```
READ template-store:"tpl:{templateId}"                       -> 404 if null
IF template.IsDeprecated == true                             -> 200 (idempotent, per IMPLEMENTATION TENETS)
// Sets IsDeprecated=true, DeprecatedAt=now, DeprecationReason, MigrationTargetId
WRITE template-store:"tpl:{templateId}" <- updated ItemTemplateModel
DELETE template-cache:"tpl:{templateId}"
PUBLISH item.template.updated { templateId, changedFields: ["isDeprecated", "deprecatedAt", "deprecationReason", "migrationTargetId"] }
RETURN (200, ItemTemplateResponse)
```

### CreateItemInstance
POST /item/instance/create | Roles: [developer]

```
READ template via cache read-through                         -> 404 if null
IF template.IsActive == false                                -> 400
IF template.IsDeprecated == true                             -> 400
// Quantity adjustment: Unique -> 1, Discrete -> floor + clamp to MaxStackSize
// CurrentDurability defaults to template.MaxDurability if not provided
// ContractBindingType defaults to Lifecycle if contractInstanceId provided
WRITE instance-store:"inst:{instanceId}" <- ItemInstanceModel from request
WRITE instance-cache:"inst:{instanceId}" <- model (with TTL)
// AddToListAsync with optimistic concurrency retries
ETAG-WRITE instance-store:"inst-container:{containerId}" <- append instanceId
ETAG-WRITE instance-store:"inst-template:{templateId}" <- append instanceId
// Instance event deferred to batch (published by EventBatcherWorker as item.instance.batch-created)
RETURN (200, ItemInstanceResponse)
```

### GetItemInstance
POST /item/instance/get | Roles: [user]

```
READ instance-cache:"inst:{instanceId}"
IF cache miss
  READ instance-store:"inst:{instanceId}"                    -> 404 if null
  WRITE instance-cache:"inst:{instanceId}" <- model (with TTL)
RETURN (200, ItemInstanceResponse)
```

### ModifyItemInstance
POST /item/instance/modify | Roles: [developer]

```
IF body.newContainerId or body.clearContainerId
  LOCK item-lock:"{instanceId}" (timeout: config.LockTimeoutSeconds)  -> 409 if fails
    // Falls through to internal modify logic below
ELSE
  // No lock needed for non-container changes

// ModifyItemInstanceInternalAsync:
READ instance-store:"inst:{instanceId}"                      -> 404 if null
// Apply fields: durabilityDelta (floor 0, no ceiling), quantityDelta, customStats,
//   customName, instanceMetadata, container changes, slot position
WRITE instance-store:"inst:{instanceId}" <- modified ItemInstanceModel
IF container cleared or changed
  // RemoveFromListAsync: remove instanceId from old container index
  ETAG-WRITE instance-store:"inst-container:{oldContainerId}" <- remove instanceId
IF container changed to new
  ETAG-WRITE instance-store:"inst-container:{newContainerId}" <- append instanceId
DELETE instance-cache:"inst:{instanceId}"
// Instance event deferred to batch (published by EventBatcherWorker as item.instance.batch-modified)
RETURN (200, ItemInstanceResponse)
```

### BindItemInstance
POST /item/instance/bind | Roles: [developer]

```
READ instance-store:"inst:{instanceId}"                      -> 404 if null
IF instance.BoundToId is set AND config.BindingAllowAdminOverride == false  -> 409
// Set BoundToId = characterId, BoundAt = now
WRITE instance-store:"inst:{instanceId}" <- updated model
DELETE instance-cache:"inst:{instanceId}"
// Event enrichment: load template for code (null if template not found)
READ template via cache read-through
PUBLISH item.instance.bound { instanceId, templateId, templateCode (nullable), realmId, characterId, bindType }
RETURN (200, ItemInstanceResponse)
```

### UnbindItemInstance
POST /item/instance/unbind | Roles: [admin]

```
READ instance-store:"inst:{instanceId}"                      -> 404 if null
IF instance.BoundToId is null                                -> 400 (not bound)
// Capture previousCharacterId before clearing
// Set BoundToId = null, BoundAt = null
WRITE instance-store:"inst:{instanceId}" <- updated model
DELETE instance-cache:"inst:{instanceId}"
// Event enrichment: load template for code (null if template not found)
READ template via cache read-through
PUBLISH item.instance.unbound { instanceId, templateId, templateCode (nullable), realmId, previousCharacterId, reason }
RETURN (200, ItemInstanceResponse)
```

### DestroyItemInstance
POST /item/instance/destroy | Roles: [developer]

```
READ instance-store:"inst:{instanceId}"                      -> 404 if null
READ template via cache read-through
IF template is not null AND template.Destroyable == false AND body.reason != Admin  -> 400
// RemoveFromListAsync for indexes
IF instance.ContainerId is set
  ETAG-WRITE instance-store:"inst-container:{containerId}" <- remove instanceId
ETAG-WRITE instance-store:"inst-template:{templateId}" <- remove instanceId
DELETE instance-store:"inst:{instanceId}"
DELETE instance-cache:"inst:{instanceId}"
// Instance event deferred to batch (published by EventBatcherWorker as item.instance.batch-destroyed)
// TODO: dispatch to IItemInstanceDestructionListener implementations (GH #490)
RETURN (200, DestroyItemInstanceResponse { templateId })
```

### UseItem
POST /item/use | Roles: [user]

```
READ instance via cache read-through                         -> 404 if null
READ template via cache read-through                         -> 500 if null (data inconsistency)
IF template.ItemUseBehavior == Disabled                      -> 400
IF template.UseBehaviorContractTemplateId is null            -> 400

// Optional CanUse validation
IF template.CanUseBehaviorContractTemplateId is set
  CALL IContractClient.CreateContractInstanceAsync(canUse contract)
  CALL IContractClient.CompleteMilestoneAsync(config.CanUseMilestoneCode)
  IF failed AND config.CanUseBehavior == Block               -> 400

// Create main use contract
// System party ID: config.SystemPartyId ?? deterministic UUID from SHA-256("item-system-party:{gameId}")
CALL IContractClient.CreateContractInstanceAsync(use contract)  -> 400 if fails
CALL IContractClient.CompleteMilestoneAsync(config.UseMilestoneCode)

IF milestone failed
  IF template.ItemUseBehavior == DestroyAlways
    // ConsumeItemAsync: destroy or decrement (see below)
  IF template.OnUseFailedBehaviorContractTemplateId is set
    CALL IContractClient.CreateContractInstanceAsync(failure handler)
    CALL IContractClient.CompleteMilestoneAsync(config.OnUseFailedMilestoneCode)
  // RecordUseFailureAsync: add to failure batch
  PUBLISH item.use-failed { batchId, failures, totalCount }  // batched, deduped by templateId+userId
  RETURN (200, UseItemResponse { consumed, failureReason })

// Success path: consume item
IF template.ItemUseBehavior != Disabled
  // ConsumeItemAsync:
  IF instance.Quantity <= 1
    ETAG-WRITE instance-store:"inst-container:{containerId}" <- remove instanceId
    ETAG-WRITE instance-store:"inst-template:{templateId}" <- remove instanceId
    DELETE instance-store:"inst:{instanceId}"
    DELETE instance-cache:"inst:{instanceId}"
    // Deferred to batch: item.instance.batch-destroyed
  ELSE
    WRITE instance-store:"inst:{instanceId}" <- Quantity -= 1
    DELETE instance-cache:"inst:{instanceId}"
    // Deferred to batch: item.instance.batch-modified

// RecordUseSuccessAsync: add to success batch
PUBLISH item.used { batchId, uses, totalCount }  // batched, deduped by templateId+userId
RETURN (200, UseItemResponse { contractInstanceId, consumed, remainingQuantity })
```

### UseItemStep
POST /item/use-step | Roles: [user]

```
READ instance via cache read-through                         -> 404 if null
READ template via cache read-through                         -> 500 if null
IF template.ItemUseBehavior == Disabled                      -> 400
IF template.UseBehaviorContractTemplateId is null            -> 400

LOCK item-lock:"item-use-step:{instanceId}" (timeout: config.UseStepLockTimeoutSeconds)  -> 409 if fails
  READ instance-store:"inst:{instanceId}" [with ETag]        -> 404 if null

  IF instance.ContractInstanceId is null  // First step
    // Optional CanUse validation
    IF template.CanUseBehaviorContractTemplateId is set
      CALL IContractClient.CreateContractInstanceAsync(canUse contract)
      CALL IContractClient.CompleteMilestoneAsync(config.CanUseMilestoneCode)
      IF failed AND config.CanUseBehavior == Block           -> 400

    CALL IContractClient.CreateContractInstanceAsync(use contract)  -> 400 if fails
    ETAG-WRITE instance-store:"inst:{instanceId}" <- set ContractInstanceId, ContractBindingType=Session
    DELETE instance-cache:"inst:{instanceId}"

  // Complete requested milestone
  CALL IContractClient.CompleteMilestoneAsync(body.milestoneCode)
  IF milestone failed
    IF template.OnUseFailedBehaviorContractTemplateId is set
      CALL IContractClient.CreateContractInstanceAsync(failure handler)
      CALL IContractClient.CompleteMilestoneAsync(config.OnUseFailedMilestoneCode)
    PUBLISH item.use-step-failed { instanceId, templateId, milestoneCode, reason }
    RETURN (200, UseItemStepResponse { failureReason })

  // Check completion
  CALL IContractClient.GetContractInstanceAsync(contractInstanceId) -> remaining milestones

  IF isComplete
    IF instance.ContractBindingType == Session
      READ instance-store:"inst:{instanceId}" [with ETag]
      ETAG-WRITE instance-store:"inst:{instanceId}" <- clear ContractInstanceId, ContractBindingType
      DELETE instance-cache:"inst:{instanceId}"
    IF template.ItemUseBehavior != Disabled
      // ConsumeItemAsync: destroy or decrement (same as UseItem)

  PUBLISH item.use-step-completed { instanceId, contractInstanceId, milestoneCode, remainingMilestones, isComplete, consumed }
  RETURN (200, UseItemStepResponse { instanceId, contractInstanceId, completedMilestone, remainingMilestones, isComplete, consumed })
```

### ListItemsByContainer
POST /item/instance/list-by-container | Roles: [user]

```
READ instance-store:"inst-container:{containerId}" -> instanceId list
totalCount = ids.Count
effectiveLimit = min(body.limit, config.MaxInstancesPerQuery)
idsToFetch = ids.Skip(body.offset).Take(effectiveLimit)
wasTruncated = totalCount > body.offset + idsToFetch.Count
// Bulk cache read-through: cache hits from Redis, misses from MySQL, populate cache for misses
FOREACH instanceId in idsToFetch
  READ instance via bulk cache read-through
RETURN (200, ListItemsResponse { items, totalCount, wasTruncated })
```

### ListItemsByTemplate
POST /item/instance/list-by-template | Roles: [admin]

```
// Uses IQueryableStore<ItemInstanceModel> — pushes filtering to MySQL
QUERY instance-store WHERE TemplateId == body.templateId
  IF body.realmId is set: AND RealmId == body.realmId
// In-memory pagination: skip(offset), take(min(limit, MaxInstancesPerQuery))
RETURN (200, ListItemsResponse { items, totalCount })
```

### BatchGetItemInstances
POST /item/instance/batch-get | Roles: [user]

```
// Bulk cache read-through: try Redis first, MySQL for misses, populate cache
FOREACH instanceId in body.instanceIds
  READ instance via bulk cache read-through
// Separate found items from not-found IDs
RETURN (200, BatchGetItemInstancesResponse { items, notFound })
```

---

### CleanDeprecatedItemTemplates
POST /item/template/clean-deprecated | Roles: [admin]

```
// Load all templates via bulk get, filter deprecated in memory
templates = BULK_GET template-store (all keys via "all-templates" index)
deprecatedTemplates = templates.Where(t => t.IsDeprecated)
IF count == 0
  RETURN (200, CleanDeprecatedResponse { cleaned=0, remaining=0, errors=0, cleanedIds=[] })

result = DeprecationCleanupHelper.ExecuteCleanupSweepAsync(
  deprecatedTemplates,
  getEntityId: t => t.TemplateId,
  getDeprecatedAt: t => t.DeprecatedAt,
  hasInstancesAsync: (t, ct) =>
    _instanceStringStore.HasStringListEntriesAsync("inst-template:{templateId}", ct),
  deleteAndPublishAsync: (t, ct) =>
    DELETE template-store:"tpl:{templateId}"
    DELETE template-store:"tpl-code:{gameId}:{code}"
    ETAG-WRITE template-store:"tpl-game:{gameId}" <- remove templateId
    ETAG-WRITE template-store:"all-templates" <- remove templateId
    DELETE template-cache:"tpl:{templateId}"
    DELETE instance-store:"inst-template:{templateId}"  // defensive reverse index cleanup
    PUBLISH item.template.deleted (ItemTemplateDeletedEvent with all fields + deleteReason),
  gracePeriodDays: body.GracePeriodDays,
  dryRun: body.DryRun
)

RETURN (200, CleanDeprecatedResponse { cleaned, remaining, errors, cleanedIds })
```

---

## Background Services

### EventBatcherWorker
**Interval**: config.InstanceEventBatchIntervalSeconds (default 5s)
**Startup delay**: config.InstanceEventBatchStartupDelaySeconds (default 10s)
**Purpose**: Flushes accumulated instance lifecycle batch events

```
// Registered in ItemServicePlugin.ConfigureServices as AddHostedService
// Flushes ItemInstanceEventBatcher's three internal batchers on interval
FOREACH batcher in ItemInstanceEventBatcher.AllFlushables
  IF batcher has accumulated entries
    PUBLISH item.instance.batch-created { records[] }   // or batch-modified / batch-destroyed
```

**Dual batching architecture:**
- **Instance lifecycle events** (created/modified/destroyed): Accumulated by `ItemInstanceEventBatcher` (singleton), flushed by this worker on a timer. Max batch size: config.InstanceEventBatchMaxSize (default 500).
- **Item use events** (used/use-failed): Accumulated by static `ConcurrentDictionary` state in ItemService, flushed inline during endpoint processing when the deduplication window (config.UseEventDeduplicationWindowSeconds, default 60s) expires or batch reaches config.UseEventBatchMaxSize (default 100). No background worker — request-driven only.

---

## Non-Standard Implementation Patterns

No non-standard patterns (no manual routes, no controller-only endpoints, no custom lifecycle overrides).
