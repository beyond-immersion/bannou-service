# Inventory Implementation Map

> **Plugin**: lib-inventory
> **Schema**: schemas/inventory-api.yaml
> **Layer**: GameFoundation
> **Deep Dive**: [docs/plugins/INVENTORY.md](../plugins/INVENTORY.md)

---

| Field | Value |
|-------|-------|
| Plugin | lib-inventory |
| Layer | L2 GameFoundation |
| Endpoints | 16 |
| State Stores | inventory-container-store (MySQL), inventory-container-cache (Redis), inventory-lock (Redis) |
| Events Published | 10 (inventory.container.created, .updated, .deleted, .full, inventory.item.placed, .removed, .moved, .transferred, .split, .stacked) |
| Events Consumed | 5 (self-subscriptions for cache invalidation) |
| Client Events | 3 (inventory.item_changed, inventory.container_full, inventory.item_transferred) |
| Background Services | 0 |

---

## State

**Store**: `inventory-container-store` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `cont:{containerId}` | `ContainerModel` | Container definition and runtime state (slots, weight, volume counters) |
| `cont-owner:{ownerType}:{ownerId}` | `string` (JSON list of container IDs) | Owner-to-containers index for listing |
| `cont-type:{containerType}` | `string` (JSON list of container IDs) | Type-to-containers index |

**Store**: `inventory-container-cache` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `cont:{containerId}` | `ContainerModel` | Read-through cache with TTL (ContainerCacheTtlSeconds, default 300s) |

**Store**: `inventory-lock` (Backend: Redis)

Used by `IDistributedLockProvider` for container modification locks. Key patterns are container GUIDs passed to `LockAsync`.

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (IStateStoreFactory) | L0 | Hard | MySQL container store, Redis cache, string store for indexes |
| lib-state (IDistributedLockProvider) | L0 | Hard | Container modification locks and index list locks |
| lib-messaging (IMessageBus) | L0 | Hard | Publishing 10 inventory event topics |
| lib-telemetry (ITelemetryProvider) | L0 | Hard | Span instrumentation on async helpers |
| lib-connect (IEntitySessionRegistry) | L1 | Hard | Push client events to sessions observing container owners |
| lib-item (IItemClient) | L2 | Hard | Item instance CRUD, template lookups, container contents listing |

**DI Provider interface**: Implements `IVariableProviderFactory` as `InventoryProviderFactory` — exposes `${inventory.*}` ABML variables to Actor (L2) via `IEnumerable<IVariableProviderFactory>` discovery.

**Internal cache**: `IInventoryDataCache` (Singleton) — in-process ConcurrentDictionary for actor variable provider data. Loads via self-calling `IInventoryClient` + `IItemClient` through DI scope. Invalidated by self-event subscriptions over RabbitMQ for multi-node correctness.

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `inventory.container.created` | `InventoryContainerCreatedEvent` | CreateContainer |
| `inventory.container.updated` | `InventoryContainerUpdatedEvent` | UpdateContainer |
| `inventory.container.deleted` | `InventoryContainerDeletedEvent` | DeleteContainer |
| `inventory.container.full` | `InventoryContainerFullEvent` | AddItemToContainer (when capacity limit reached) |
| `inventory.item.placed` | `InventoryItemPlacedEvent` | AddItemToContainer |
| `inventory.item.removed` | `InventoryItemRemovedEvent` | RemoveItemFromContainer |
| `inventory.item.moved` | `InventoryItemMovedEvent` | MoveItem (both same-container and cross-container) |
| `inventory.item.transferred` | `InventoryItemTransferredEvent` | TransferItem |
| `inventory.item.split` | `InventoryItemSplitEvent` | SplitStack |
| `inventory.item.stacked` | `InventoryItemStackedEvent` | MergeStacks |

---

## Events Consumed

Self-subscriptions only — no external event consumption. Five of the plugin's own events are subscribed to for `IInventoryDataCache` invalidation (multi-node in-process cache requires RabbitMQ fan-out).

| Topic | Handler | Action |
|-------|---------|--------|
| `inventory.item.placed` | `HandleItemPlacedAsync` | Invalidate cache for evt.OwnerId |
| `inventory.item.removed` | `HandleItemRemovedAsync` | Invalidate cache for evt.OwnerId |
| `inventory.item.transferred` | `HandleItemTransferredAsync` | Invalidate cache for evt.SourceOwnerId and evt.TargetOwnerId |
| `inventory.container.created` | `HandleContainerCreatedAsync` | Invalidate cache for evt.OwnerId |
| `inventory.container.deleted` | `HandleContainerDeletedAsync` | Invalidate cache for evt.OwnerId |

Note: `inventory.item.moved`, `inventory.item.split`, and `inventory.item.stacked` are NOT subscribed — cache may be stale for these operations until TTL expiry.

---

## DI Services

| Service | Role |
|---------|------|
| `ILogger<InventoryService>` | Structured logging |
| `InventoryServiceConfiguration` | All 12 config properties |
| `IStateStoreFactory` | MySQL + Redis state store access (not stored as field — used in constructor only) |
| `IDistributedLockProvider` | Container and index list distributed locks |
| `IMessageBus` | Event publishing |
| `IItemClient` | Item service inter-service calls |
| `ITelemetryProvider` | Distributed tracing spans |
| `IEntitySessionRegistry` | Client event push to WebSocket sessions |
| `IInventoryDataCache` | In-process actor variable provider cache |
| `IEventConsumer` | Self-event subscription registration (not stored as field) |

---

## Method Index

| Method | Route | Roles | Mutates | Publishes |
|--------|-------|-------|---------|-----------|
| CreateContainer | POST /inventory/container/create | developer | container, owner-index, type-index | inventory.container.created |
| GetContainer | POST /inventory/container/get | user | cache (populate) | - |
| GetOrCreateContainer | POST /inventory/container/get-or-create | developer | container, indexes (if created) | inventory.container.created (if created) |
| ListContainers | POST /inventory/container/list | user | - | - |
| UpdateContainer | POST /inventory/container/update | developer | container | inventory.container.updated |
| DeleteContainer | POST /inventory/container/delete | admin | container, owner-index, type-index, cache | inventory.container.deleted |
| AddItemToContainer | POST /inventory/add | [] | container | inventory.item.placed, inventory.container.full |
| RemoveItemFromContainer | POST /inventory/remove | [] | container | inventory.item.removed |
| MoveItem | POST /inventory/move | user | container (cross-container) | inventory.item.moved (+.removed, .placed if cross-container) |
| TransferItem | POST /inventory/transfer | [] | container (via delegates) | inventory.item.transferred (+split, move events) |
| SplitStack | POST /inventory/split | user | container | inventory.item.split |
| MergeStacks | POST /inventory/merge | user | container | inventory.item.stacked |
| QueryItems | POST /inventory/query | user | - | - |
| CountItems | POST /inventory/count | user | - | - |
| HasItems | POST /inventory/has | user | - | - |
| FindSpace | POST /inventory/find-space | user | - | - |

---

## Methods

### CreateContainer
POST /inventory/container/create | Roles: [developer]

```
// Validate capacity parameters per constraint model
IF maxSlots/maxWeight/maxVolume/gridWidth/gridHeight <= 0  -> 400

IF parentContainerId specified
  READ containerStore:cont:{parentContainerId}             -> 400 if null
  // Check nesting depth against parent's MaxNestingDepth
  IF parent.NestingDepth + 1 > parent.MaxNestingDepth     -> 400

// Apply config defaults for null capacity fields
// WeightContribution.None -> config.DefaultWeightContribution

WRITE containerStore:cont:{containerId} <- ContainerModel from request
WRITE containerCache:cont:{containerId} <- ContainerModel (TTL=config.ContainerCacheTtlSeconds)

// Index updates with list locks (lock failure logs warning, non-fatal)
LOCK containerStore:cont-owner:{ownerType}:{ownerId} (ListLockTimeoutSeconds)
  READ containerStringStore:cont-owner:{ownerType}:{ownerId}
  WRITE containerStringStore:cont-owner:{ownerType}:{ownerId} <- add containerId to list

LOCK containerStore:cont-type:{containerType} (ListLockTimeoutSeconds)
  READ containerStringStore:cont-type:{containerType}
  WRITE containerStringStore:cont-type:{containerType} <- add containerId to list

PUBLISH inventory.container.created { containerId, ownerId, ownerType, containerType, constraintModel, capacity fields, createdAt }
RETURN (200, ContainerResponse)
```

---

### GetContainer
POST /inventory/container/get | Roles: [user]

```
// Cache read-through: Redis -> MySQL -> populate cache
READ containerCache:cont:{containerId}
IF cache miss
  READ containerStore:cont:{containerId}                   -> 404 if null
  WRITE containerCache:cont:{containerId} <- model (TTL)

IF includeContents
  CALL IItemClient.ListItemsByContainerAsync(containerId)
  // ApiException caught -> empty items list (not a failure)

RETURN (200, ContainerWithContentsResponse { container, items })
```

---

### GetOrCreateContainer
POST /inventory/container/get-or-create | Roles: [developer]

```
IF !config.EnableLazyContainerCreation                     -> 400

// Search owner's existing containers
READ containerStringStore:cont-owner:{ownerType}:{ownerId}
FOREACH containerId in index list
  READ containerStore:cont:{containerId}
  IF container.ContainerType == body.ContainerType
    RETURN (200, ContainerResponse)  // existing found

// No match found — delegate to full creation
// (all CreateContainer operations apply)
RETURN CreateContainerAsync(mapped request)
```

---

### ListContainers
POST /inventory/container/list | Roles: [user]

```
// Reads MySQL directly — bypasses Redis cache
READ containerStringStore:cont-owner:{ownerType}:{ownerId}
FOREACH containerId in index list
  READ containerStore:cont:{containerId}
  // Skip null entries silently
  // Filter: containerType match, equipmentSlot exclusion, realmId match

RETURN (200, ListContainersResponse { containers, totalCount })
```

---

### UpdateContainer
POST /inventory/container/update | Roles: [developer]

```
// Validation before lock
IF maxSlots/maxWeight/maxVolume/gridWidth/gridHeight <= 0  -> 400

LOCK inventoryLock:{containerId} (LockTimeoutSeconds)      -> 409 if fails
  READ containerCache/containerStore:cont:{containerId}    -> 404 if null

  // Patch semantics: only non-null request fields applied
  // MaxSlots, MaxWeight, GridWidth, GridHeight, MaxVolume,
  // AllowedCategories, ForbiddenCategories, AllowedTags, Tags, Metadata

  WRITE containerStore:cont:{containerId} <- updated model
  WRITE containerCache:cont:{containerId} <- updated model (TTL)

  PUBLISH inventory.container.updated { full container state snapshot }

RETURN (200, ContainerResponse)
```

---

### DeleteContainer
POST /inventory/container/delete | Roles: [admin]

```
LOCK inventoryLock:{containerId} (DeleteLockTimeoutSeconds) -> 409 if fails
  READ containerCache/containerStore:cont:{containerId}     -> 404 if null

  // Get container items — abort if item service unreachable
  CALL IItemClient.ListItemsByContainerAsync(containerId)   -> 503 on ApiException

  IF itemHandling == Error AND items not empty              -> 400
  IF itemHandling == Transfer AND no transferToContainerId  -> 400

  IF itemHandling == Destroy
    FOREACH item in items
      CALL IItemClient.DestroyItemInstanceAsync(instanceId, Destroyed)
      // Individual failures logged as warning, loop continues

  IF itemHandling == Transfer
    FOREACH item in items
      // Delegates to MoveItemAsync (acquires own locks)
      CALL self.MoveItemAsync(instanceId, transferToContainerId)

  // Remove from indexes (list lock, failure non-fatal)
  LOCK containerStore:cont-owner:{ownerType}:{ownerId} (ListLockTimeoutSeconds)
    READ+WRITE containerStringStore owner index <- remove containerId
  LOCK containerStore:cont-type:{containerType} (ListLockTimeoutSeconds)
    READ+WRITE containerStringStore type index <- remove containerId

  DELETE containerStore:cont:{containerId}
  DELETE containerCache:cont:{containerId}  // non-fatal if fails

  PUBLISH inventory.container.deleted { full container state at deletion }

RETURN (200, DeleteContainerResponse { itemsHandled })
```

---

### AddItemToContainer
POST /inventory/add | Roles: []

```
CALL IItemClient.GetItemInstanceAsync(instanceId)           -> mapped status on ApiException
CALL IItemClient.GetItemTemplateAsync(item.templateId)      -> 500 on ApiException

LOCK inventoryLock:{containerId} (LockTimeoutSeconds)       -> 409 if fails
  READ containerCache/containerStore:cont:{containerId}     -> 404 if null

  // Category constraint check (case-insensitive)
  IF container.AllowedCategories and item category not in list  -> 400
  IF container.ForbiddenCategories and item category in list    -> 400

  // Capacity constraint check per constraint model
  // (slots, weight, volume, grid approximated by slots)
  IF constraints violated                                   -> 400

  // Update container counters: UsedSlots++, ContentsWeight, CurrentVolume
  CALL IItemClient.ModifyItemInstanceAsync(instanceId, newContainerId, slotIndex, slotX, slotY)
                                                            -> 500 on ApiException

  WRITE containerStore:cont:{containerId} <- updated model
  WRITE containerCache:cont:{containerId} <- updated model (TTL)

  PUBLISH inventory.item.placed { instanceId, templateId, containerId, ownerId, ownerType, quantity, slot data }
  PUSH inventory.item_changed { changeType: Placed, containerId, instanceId, templateId, quantity }

  // Check if capacity limit reached after add
  IF container full
    PUBLISH inventory.container.full { containerId, ownerId, ownerType, containerType, constraintType }
    PUSH inventory.container_full { containerId, constraintType }

RETURN (200, AddItemResponse { slotIndex, slotX, slotY })
```

---

### RemoveItemFromContainer
POST /inventory/remove | Roles: []

```
CALL IItemClient.GetItemInstanceAsync(instanceId)           -> mapped status on ApiException
IF item not in container (!containerId.HasValue)            -> 400

LOCK inventoryLock:{item.containerId} (LockTimeoutSeconds)  -> 409 if fails
  READ containerCache/containerStore:cont:{containerId}     -> 404 if null

  // Get template for weight/volume update
  CALL IItemClient.GetItemTemplateAsync(item.templateId)
  // ApiException -> continue without weight/volume update (silent inconsistency)

  // Decrement container counters: UsedSlots--, ContentsWeight, CurrentVolume
  CALL IItemClient.ModifyItemInstanceAsync(instanceId, clearContainerId: true)
  // ApiException -> warning logged, not fatal

  WRITE containerStore:cont:{containerId} <- updated model
  WRITE containerCache:cont:{containerId} <- updated model (TTL)

  PUBLISH inventory.item.removed { instanceId, templateId, containerId, ownerId, ownerType }
  PUSH inventory.item_changed { changeType: Removed, containerId, instanceId, templateId }

RETURN (200, RemoveItemResponse { previousContainerId })
```

---

### MoveItem
POST /inventory/move | Roles: [user]

```
CALL IItemClient.GetItemInstanceAsync(instanceId)           -> mapped status on ApiException
IF item not in container                                    -> 400

IF sourceContainerId == targetContainerId
  // Same-container: update slot position only
  LOCK inventoryLock:{targetContainerId} (LockTimeoutSeconds) -> 409 if fails
    CALL IItemClient.ModifyItemInstanceAsync(instanceId, newSlotIndex, newSlotX, newSlotY)
                                                            -> mapped status on ApiException
    READ containerCache/containerStore:cont:{containerId}   // for client event routing

  PUBLISH inventory.item.moved { instanceId, templateId, sourceContainerId, targetContainerId, slot data }
  PUSH inventory.item_changed { changeType: Moved }

ELSE
  // Cross-container: validate target, then remove+add
  READ containerCache/containerStore:cont:{targetContainerId} -> 404 if null
  CALL IItemClient.GetItemTemplateAsync(item.templateId)     // for constraint check

  // Suppress intermediate client events
  _suppressClientEvents = true
  CALL self.RemoveItemFromContainerAsync(instanceId)         -> propagate status on failure
  CALL self.AddItemToContainerAsync(instanceId, targetContainerId, slot data)
  // If Add fails after Remove succeeds: item orphaned (logged as error)
  _suppressClientEvents = false

  PUBLISH inventory.item.moved { instanceId, templateId, sourceContainerId, targetContainerId }
  PUSH inventory.item_changed { changeType: Moved }

RETURN (200, MoveItemResponse { sourceContainerId, slotIndex, slotX, slotY })
```

---

### TransferItem
POST /inventory/transfer | Roles: []

```
CALL IItemClient.GetItemInstanceAsync(instanceId)           -> mapped status on ApiException
CALL IItemClient.GetItemTemplateAsync(item.templateId)      -> 500 on ApiException

IF !template.Tradeable                                      -> 400
IF item.BoundToId has value                                 -> 400
IF item not in container                                    -> 400
IF body.Quantity > item.Quantity                            -> 400

// Load source and target containers from MySQL directly (bypasses cache)
READ containerStore:cont:{sourceContainerId}                -> 404 if null
READ containerStore:cont:{targetContainerId}                -> 404 if null

_suppressClientEvents = true

IF partial transfer (body.Quantity < item.Quantity)
  CALL self.SplitStackAsync(instanceId, quantity)           -> propagate status on failure
  // Use split result's NewInstanceId for move
  CALL self.MoveItemAsync(newInstanceId, targetContainerId) -> propagate status on failure
ELSE
  CALL self.MoveItemAsync(instanceId, targetContainerId)    -> propagate status on failure

_suppressClientEvents = false

PUBLISH inventory.item.transferred { instanceId, templateId, sourceContainerId, sourceOwnerId, sourceOwnerType, targetContainerId, targetOwnerId, targetOwnerType, quantityTransferred }
PUSH inventory.item_transferred to source owner { instanceId, templateId, sourceContainerId, targetContainerId, quantityTransferred }
IF targetOwnerId != sourceOwnerId
  PUSH inventory.item_transferred to target owner { same fields }

RETURN (200, TransferItemResponse { instanceId, sourceContainerId, quantityTransferred })
```

---

### SplitStack
POST /inventory/split | Roles: [user]

```
CALL IItemClient.GetItemInstanceAsync(instanceId)           -> mapped status on ApiException
IF item not in container                                    -> 400
IF item.Quantity <= body.Quantity                            -> 400
CALL IItemClient.GetItemTemplateAsync(item.templateId)      -> 500 on ApiException
IF template.QuantityModel == Unique                         -> 400

LOCK inventoryLock:{containerId} (LockTimeoutSeconds)       -> 409 if fails
  // Step 1: Reduce original quantity
  CALL IItemClient.ModifyItemInstanceAsync(instanceId, quantityDelta: -body.Quantity)
                                                            -> 500 on ApiException

  // Step 2: Create new instance with split quantity
  CALL IItemClient.CreateItemInstanceAsync(templateId, containerId, quantity, slotIndex, slotX, slotY)
  // On failure: rollback step 1 via ModifyItemInstance(+body.Quantity)
                                                            -> 500 on ApiException

  // Update container UsedSlots++
  READ containerCache/containerStore:cont:{containerId}
  IF container found
    container.UsedSlots += 1
    WRITE containerStore:cont:{containerId} <- updated model
    WRITE containerCache:cont:{containerId} <- updated model (TTL)

  PUBLISH inventory.item.split { originalInstanceId, newInstanceId, templateId, containerId, quantitySplit, originalRemaining }
  PUSH inventory.item_changed { changeType: Split, containerId, instanceId (new), templateId, quantity }

RETURN (200, SplitStackResponse { newInstanceId, originalQuantity, newQuantity })
```

---

### MergeStacks
POST /inventory/merge | Roles: [user]

```
CALL IItemClient.GetItemInstanceAsync(sourceInstanceId)     -> mapped status on ApiException
CALL IItemClient.GetItemInstanceAsync(targetInstanceId)     -> mapped status on ApiException
IF source.TemplateId != target.TemplateId                   -> 400
IF either item not in container                             -> 400
CALL IItemClient.GetItemTemplateAsync(source.templateId)    -> 500 on ApiException

// Calculate merge amounts
// combined = source.Quantity + target.Quantity
// IF combined > template.MaxStackSize: partial merge with overflow

// Lock ordering: deterministic by GUID (smaller first) to prevent deadlock
IF same container
  LOCK inventoryLock:{containerId} (LockTimeoutSeconds)     -> 409 if fails
ELSE
  LOCK inventoryLock:{smallerGuid} (LockTimeoutSeconds)     -> 409 if fails
  LOCK inventoryLock:{largerGuid} (LockTimeoutSeconds)      -> 409 if fails

  // Step 1: Update target quantity
  CALL IItemClient.ModifyItemInstanceAsync(targetInstanceId, quantityDelta: +quantityToAdd)
                                                            -> 500 on ApiException

  IF full merge (no overflow)
    // Step 2: Destroy source
    CALL IItemClient.DestroyItemInstanceAsync(sourceInstanceId, Consumed)
    // Failure logged as warning, non-fatal

    // Decrement source container UsedSlots
    READ containerCache/containerStore:cont:{sourceContainerId}
    IF container found
      container.UsedSlots -= 1
      WRITE containerStore:cont:{sourceContainerId} <- updated model
      WRITE containerCache:cont:{sourceContainerId} <- updated model (TTL)
  ELSE
    // Partial merge: reduce source quantity
    CALL IItemClient.ModifyItemInstanceAsync(sourceInstanceId, quantityDelta: -quantityToAdd)
    // Failure logged as warning, non-fatal

  PUBLISH inventory.item.stacked { sourceInstanceId, targetInstanceId, templateId, containerId (target), quantityAdded, newTotalQuantity }
  PUSH inventory.item_changed { changeType: Stacked, containerId (target), instanceId (target), templateId, quantity (combined) }

RETURN (200, MergeStacksResponse { newQuantity, sourceDestroyed, overflowQuantity })
```

---

### QueryItems
POST /inventory/query | Roles: [user]

```
// Get all owner's containers
CALL self.ListContainersAsync(ownerId, ownerType)           -> propagate status on failure
// Additional filter: containerType, excludeEquipmentSlots

FOREACH container in containers
  CALL IItemClient.ListItemsByContainerAsync(containerId)
  // ApiException -> skip container silently

  FOREACH item in container items
    IF body.TemplateId specified AND item.TemplateId != body.TemplateId
      // skip
    IF body.Category or body.Tags specified
      CALL IItemClient.GetItemTemplateAsync(item.templateId)
      // ApiException -> skip item
      // Filter by category match, tag intersection

// In-memory pagination: results.Skip(offset).Take(limit)
RETURN (200, QueryItemsResponse { items, totalCount })
```

---

### CountItems
POST /inventory/count | Roles: [user]

```
// Paginated aggregation up to MaxCountQueryLimit
FOREACH page (pageSize = config.QueryPageSize)
  CALL self.QueryItemsAsync(ownerId, ownerType, templateId, offset, limit)
  // Accumulate items
  IF page.items.Count < pageSize OR total >= MaxCountQueryLimit
    // stop

// Sum quantities across all matching stacks
RETURN (200, CountItemsResponse { templateId, totalQuantity, stackCount })
```

---

### HasItems
POST /inventory/has | Roles: [user]

```
FOREACH requirement in body.Requirements
  CALL self.CountItemsAsync(ownerId, ownerType, requirement.templateId)
  // Null response -> available = 0
  satisfied = available >= requirement.Quantity

hasAll = all requirements satisfied
RETURN (200, HasItemsResponse { hasAll, results })
```

---

### FindSpace
POST /inventory/find-space | Roles: [user]

```
CALL IItemClient.GetItemTemplateAsync(body.TemplateId)      -> mapped status on ApiException

CALL self.ListContainersAsync(ownerId, ownerType)           -> propagate status on failure

FOREACH container in containers
  // Check category constraints (allowed/forbidden)
  // Check capacity via constraint model
  IF constraints not satisfied -> skip

  IF body.PreferStackable AND template.QuantityModel != Unique
    CALL IItemClient.ListItemsByContainerAsync(containerId)
    // ApiException -> skip stack check
    // Look for existing stacks of same templateId with room
    IF stack found with space
      candidate.ExistingStackInstanceId = stack.instanceId
      candidate.CanFitQuantity = min(body.Quantity, remaining stack space)

  // Add to candidates

RETURN (200, FindSpaceResponse { hasSpace, candidates })
```

---

## Background Services

No background services.
