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
| Endpoints | 16 |
| State Stores | item-template-store (MySQL), item-template-cache (Redis), item-instance-store (MySQL), item-instance-cache (Redis), item-lock (Redis) |
| Events Published | 11 (item.template.created, item.template.updated, item.instance.created, item.instance.modified, item.instance.destroyed, item.instance.bound, item.instance.unbound, item.used, item.use-failed, item.use-step-completed, item.use-step-failed) |
| Events Consumed | 0 |
| Client Events | 0 |
| Background Services | 0 |

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
| lib-messaging (IMessageBus) | L0 | Hard | Publishing all 11 event topics |
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
| `item.template.updated` | `ItemTemplateUpdatedEvent` | Template fields changed or deprecated (changedFields for deprecation) |
| `item.instance.created` | `ItemInstanceCreatedEvent` | Instance created from template |
| `item.instance.modified` | `ItemInstanceModifiedEvent` | Instance durability/stats/name/container/quantity changed, or quantity decremented on use |
| `item.instance.destroyed` | `ItemInstanceDestroyedEvent` | Instance permanently deleted or consumed (last unit) |
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
| `ItemServiceConfiguration` | All 17 config properties (defaults cached at construction) |
| `IStateStoreFactory` | State store access (5 stores, acquired inline per method) |
| `IMessageBus` | Event publishing |
| `IDistributedLockProvider` | Distributed locks for container changes and UseItemStep |
| `ITelemetryProvider` | Span instrumentation |
| `IContractClient` | Contract creation and milestone completion for item use flows |

---

## Method Index

| Method | Route | Roles | Mutates | Publishes |
|--------|-------|-------|---------|-----------|
| CreateItemTemplate | POST /item/template/create | developer | template, code-index, game-index, all-index, cache | item.template.created |
| GetItemTemplate | POST /item/template/get | user | - | - |
| ListItemTemplates | POST /item/template/list | user | - | - |
| UpdateItemTemplate | POST /item/template/update | developer | template, cache | item.template.updated |
| DeprecateItemTemplate | POST /item/template/deprecate | admin | template, cache | item.template.updated |
| CreateItemInstance | POST /item/instance/create | developer | instance, container-index, template-index, cache | item.instance.created |
| GetItemInstance | POST /item/instance/get | user | - | - |
| ModifyItemInstance | POST /item/instance/modify | developer | instance, container-index, cache | item.instance.modified |
| BindItemInstance | POST /item/instance/bind | developer | instance, cache | item.instance.bound |
| UnbindItemInstance | POST /item/instance/unbind | admin | instance, cache | item.instance.unbound |
| DestroyItemInstance | POST /item/instance/destroy | developer | instance, container-index, template-index, cache | item.instance.destroyed |
| UseItem | POST /item/use | user | instance, container-index, template-index, cache | item.used, item.use-failed, item.instance.modified, item.instance.destroyed |
| UseItemStep | POST /item/use-step | user | instance, container-index, template-index, cache | item.use-step-completed, item.use-step-failed, item.instance.modified, item.instance.destroyed |
| ListItemsByContainer | POST /item/instance/list-by-container | user | - | - |
| ListItemsByTemplate | POST /item/instance/list-by-template | admin | - | - |
| BatchGetItemInstances | POST /item/instance/batch-get | user | - | - |

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
// Each field applied only if non-null in request
WRITE template-store:"tpl:{templateId}" <- patched ItemTemplateModel
DELETE template-cache:"tpl:{templateId}"
PUBLISH item.template.updated { templateId, code, gameId, ... }
RETURN (200, ItemTemplateResponse)
```

### DeprecateItemTemplate
POST /item/template/deprecate | Roles: [admin]

```
READ template-store:"tpl:{templateId}"                       -> 404 if null
// Always writes (does not skip if already deprecated)
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
PUBLISH item.instance.created { instanceId, templateId, containerId, realmId, quantity, ... }
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
PUBLISH item.instance.modified { instanceId, templateId, containerId, realmId, quantity, ... }
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
// Event enrichment: load template for code (fallback: "missing:{templateId}" if template not found)
READ template via cache read-through
PUBLISH item.instance.bound { instanceId, templateId, templateCode, realmId, characterId, bindType }
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
READ template via cache read-through  // for event enrichment
PUBLISH item.instance.unbound { instanceId, templateId, templateCode, realmId, previousCharacterId, reason }
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
PUBLISH item.instance.destroyed { instanceId, templateId, containerId, realmId, quantity, ... }
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
    PUBLISH item.instance.destroyed { ... }
  ELSE
    WRITE instance-store:"inst:{instanceId}" <- Quantity -= 1
    DELETE instance-cache:"inst:{instanceId}"
    PUBLISH item.instance.modified { ... }

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
// Truncate to config.MaxInstancesPerQuery; set wasTruncated if capped
// Bulk cache read-through: cache hits from Redis, misses from MySQL, populate cache for misses
FOREACH instanceId in list (truncated)
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

## Background Services

No background services.
