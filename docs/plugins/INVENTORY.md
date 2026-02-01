# Inventory Plugin Deep Dive

> **Plugin**: lib-inventory
> **Schema**: schemas/inventory-api.yaml
> **Version**: 1.0.0
> **State Stores**: inventory-container-store (MySQL), inventory-container-cache (Redis), inventory-lock (Redis)

---

## Overview

Container and item placement management for games. Handles container lifecycle (CRUD), item movement between containers, stacking operations (split/merge), and inventory queries. Does NOT handle item definitions or instances directly - delegates to lib-item for all item-level operations. Features distributed lock-protected modifications, Redis cache with MySQL backing, multiple constraint models (slot-only, weight-only, grid, volumetric, unlimited), category restrictions, nesting depth limits, and graceful degradation when the item service is unavailable. Designed as the placement layer that orchestrates lib-item.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | MySQL+Redis persistence for containers, cache, and indexes |
| lib-state (`IDistributedLockProvider`) | Container-level locks for concurrent modification safety |
| lib-messaging (`IMessageBus`) | Publishing inventory lifecycle events; error event publishing |
| lib-item (`IItemClient`) | Item template lookups, instance CRUD, container contents listing |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-escrow | Comment in code references inventory for asset custody; actual integration is placeholder |

**Note**: The escrow service currently has only a placeholder comment referencing inventory for deposit validation. No actual `IInventoryClient` usage exists in any service as of this review.

---

## State Storage

**Stores**: 3 state stores

| Store | Backend | Purpose |
|-------|---------|---------|
| `inventory-container-store` | MySQL | Container definitions (persistent) |
| `inventory-container-cache` | Redis | Container read-through cache (TTL: 300s) |
| `inventory-lock` | Redis | Distributed locks for concurrent modifications |

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `cont:{containerId}` | `ContainerModel` | Container definition and state |
| `cont-owner:{ownerType}:{ownerId}` | `List<string>` (JSON) | Container IDs for an owner |
| `cont-type:{containerType}` | `List<string>` (JSON) | Container IDs of a type |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `inventory-container.created` | `InventoryContainerCreatedEvent` | Container created |
| `inventory-container.updated` | `InventoryContainerUpdatedEvent` | Container properties changed |
| `inventory-container.deleted` | `InventoryContainerDeletedEvent` | Container deleted |
| `inventory-container.full` | `InventoryContainerFullEvent` | Container reached capacity |
| `inventory-item.placed` | `InventoryItemPlacedEvent` | Item added to container |
| `inventory-item.removed` | `InventoryItemRemovedEvent` | Item removed from container |
| `inventory-item.moved` | `InventoryItemMovedEvent` | Item moved between containers/slots |
| `inventory-item.transferred` | `InventoryItemTransferredEvent` | Item transferred between owners |
| `inventory-item.split` | `InventoryItemSplitEvent` | Stack split into two |
| `inventory-item.stacked` | `InventoryItemStackedEvent` | Stacks merged together |

### Consumed Events

This plugin does not consume external events.

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `DefaultMaxNestingDepth` | `INVENTORY_DEFAULT_MAX_NESTING_DEPTH` | `3` | Maximum container nesting depth |
| `DefaultWeightContribution` | `INVENTORY_DEFAULT_WEIGHT_CONTRIBUTION` | `self_plus_contents` | How weight propagates to parent |
| `ContainerCacheTtlSeconds` | `INVENTORY_CONTAINER_CACHE_TTL_SECONDS` | `300` | Redis cache TTL (5 min) |
| `LockTimeoutSeconds` | `INVENTORY_LOCK_TIMEOUT_SECONDS` | `30` | Container-level distributed lock expiry |
| `ListLockTimeoutSeconds` | `INVENTORY_LIST_LOCK_TIMEOUT_SECONDS` | `15` | Owner/type index list lock expiry |
| `EnableLazyContainerCreation` | `INVENTORY_ENABLE_LAZY_CONTAINER_CREATION` | `true` | Allow get-or-create pattern |
| `DefaultMaxSlots` | `INVENTORY_DEFAULT_MAX_SLOTS` | `20` | Default slot count for new containers |
| `DefaultMaxWeight` | `INVENTORY_DEFAULT_MAX_WEIGHT` | `100.0` | Default weight capacity |
| `MaxCountQueryLimit` | `INVENTORY_MAX_COUNT_QUERY_LIMIT` | `10000` | Max items to scan during count |
| `QueryPageSize` | `INVENTORY_QUERY_PAGE_SIZE` | `200` | Items per page for queries |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<InventoryService>` | Scoped | Structured logging |
| `InventoryServiceConfiguration` | Singleton | All 10 config properties |
| `IStateStoreFactory` | Singleton | MySQL+Redis state store access |
| `IDistributedLockProvider` | Singleton | Container-level distributed locks |
| `IMessageBus` | Scoped | Event publishing and error events |
| `IItemClient` | Scoped | Item service client for template/instance operations |

Service lifetime is **Scoped** (per-request). No background services. No helper classes - all logic is in the main service.

---

## API Endpoints (Implementation Notes)

### Container Operations (6 endpoints)

- **CreateContainer** (`/inventory/container/create`): Validates capacity parameters per constraint model (rejects <= 0 for slots, weight, volume, grid dimensions). Checks nesting depth against parent's `MaxNestingDepth` (if specified). Resolves weight contribution from config default when `WeightContribution.None`. Saves to MySQL, updates Redis cache. Updates owner and type indexes with separate list locks. Publishes `inventory-container.created`.

- **GetContainer** (`/inventory/container/get`): Cache read-through pattern (Redis -> MySQL -> populate cache). If `includeContents=true`, calls `IItemClient.ListItemsByContainerAsync()`. Graceful fallback if item service unavailable (returns container with empty contents list, logs warning).

- **GetOrCreateContainer** (`/inventory/container/get-or-create`): Requires `EnableLazyContainerCreation=true`. Searches owner's containers by iterating the owner index list. Creates via `CreateContainerAsync` if not found. Idempotent acquisition pattern.

- **ListContainers** (`/inventory/container/list`): Lists containers from owner index. Filters by container type, realm, equipment slot status. No pagination support.

- **UpdateContainer** (`/inventory/container/update`): Acquires distributed lock before modification. Updates: maxSlots, maxWeight, gridDimensions, maxVolume, categories, tags, metadata. Publishes `inventory-container.updated`.

- **DeleteContainer** (`/inventory/container/delete`): Returns `ServiceUnavailable` if item service unreachable (prevents orphaning items). Three strategies for item handling: `destroy` (calls DestroyItemInstance per item), `transfer` (moves items to target container), `error` (returns 400 if not empty). Cleans up indexes and cache. Publishes `inventory-container.deleted`.

### Inventory Operations (6 endpoints)

- **AddItemToContainer** (`/inventory/add`): Acquires lock. Gets item instance and template from lib-item. Validates category constraints (allowed/forbidden lists, case-insensitive). Checks capacity per constraint model. Increments UsedSlots, ContentsWeight, CurrentVolume. Updates item's ContainerId via lib-item. Publishes `inventory-item.placed`. Emits `inventory-container.full` if capacity reached.

- **RemoveItemFromContainer** (`/inventory/remove`): Gets item's current container via lib-item. Acquires lock. Decrements counters. Weight/volume decrement fails gracefully if template lookup fails (warning logged). Publishes `inventory-item.removed`. Does NOT clear item's ContainerId in lib-item (item still references the container).

- **MoveItem** (`/inventory/move`): Same-container returns OK immediately (no weight changes, no events). Different-container: validates destination constraints, internally calls RemoveItemFromContainer + AddItemToContainer. Publishes `inventory-item.moved`.

- **TransferItem** (`/inventory/transfer`): Checks item is tradeable (`template.Tradeable`) and not bound (`item.BoundToId`). Acquires lock on source container. Calls MoveItem internally. Publishes `inventory-item.transferred` with source/target owner info.

- **SplitStack** (`/inventory/split`): Validates quantity < original (not <=). Validates not `QuantityModel.Unique`. Acquires container lock. Updates original quantity via `QuantityDelta = -body.Quantity`. Creates new instance via lib-item. Increments container UsedSlots. Publishes `inventory-item.split`. On failure to create new item, attempts to restore original quantity.

- **MergeStacks** (`/inventory/merge`): Validates templates match. Gets template for MaxStackSize. Calculates overflow (source keeps excess). Acquires lock on source container only. Updates target quantity first, then destroys or reduces source. Decrements container UsedSlots on full merge. Publishes `inventory-item.stacked`.

### Query Operations (4 endpoints)

- **QueryItems** (`/inventory/query`): Lists all owner's containers via ListContainersAsync, fetches items from each via lib-item. Client-side filtering by template/category/tags after fetch. Pagination via offset/limit applied after all items collected.

- **CountItems** (`/inventory/count`): Paginates through QueryItems results up to `MaxCountQueryLimit`. Returns total quantity (sum) and stack count.

- **HasItems** (`/inventory/has`): Takes array of template+quantity requirements. Calls CountItems for each. Returns per-item satisfaction boolean and overall `hasAll`.

- **FindSpace** (`/inventory/find-space`): Gets template for constraint checks. Filters containers by category constraints. Checks capacity via constraint model. If `preferStackable`, seeks existing stacks with room (quantity < MaxStackSize). Returns candidate containers with fit quantities.

---

## Visual Aid

```
Constraint Models
==================

  slot_only:
    ├── UsedSlots < MaxSlots → can add
    └── Example: 20-slot backpack

  weight_only:
    ├── ContentsWeight + item.Weight * qty ≤ MaxWeight → can add
    └── Example: 100 kg weight limit

  slot_and_weight:
    ├── Both slot AND weight constraints checked
    └── Example: 20-slot, 50kg satchel

  grid:
    ├── Approximated by slot count (true grid not implemented)
    └── Example: 8x6 grid inventory (48 slots)

  volumetric:
    ├── CurrentVolume + item.Volume * qty ≤ MaxVolume → can add
    └── Example: 50L chest

  unlimited:
    ├── No constraints checked
    └── Example: quest item bag, admin storage


Cache Architecture
====================

  Request Handler
       │
       ├── GetContainerWithCacheAsync(containerId)
       │    │
       │    ├── TryGetContainerFromCacheAsync (Redis)
       │    │    └── Hit? Return cached
       │    │
       │    ├── Miss → GetAsync (MySQL)
       │    │
       │    └── Populate cache → UpdateContainerCacheAsync
       │
       └── SaveContainerWithCacheAsync(model)
            ├── SaveAsync (MySQL)
            └── UpdateContainerCacheAsync (Redis, TTL=300s)


Lock Acquisition Pattern
==========================

  Container modification operations acquire distributed locks:
  - add-item-{uuid}
  - remove-item-{uuid}
  - update-container-{uuid}
  - delete-container-{uuid}
  - transfer-item-{uuid}
  - split-stack-{uuid}
  - merge-stack-{uuid}

  Index list operations use shorter timeout:
  - AddToListAsync / RemoveFromListAsync
  - ListLockTimeoutSeconds (15s) vs LockTimeoutSeconds (30s)


Stack Operations
==================

  SplitStack(instanceId, quantity=8)
  (Original stack: 20 potions)
       │
       ├── Validate: quantity < original.Quantity
       ├── Validate: not QuantityModel.Unique
       │
       ├── ModifyItemInstance(original, QuantityDelta: -8)
       ├── CreateItemInstance(template, quantity: 8)
       ├── container.UsedSlots++
       │
       └── Result: 12 potions (original) + 8 potions (new stack)

  MergeStacks(source, target)
  (Source: 8 potions, Target: 12 potions, MaxStack: 15)
       │
       ├── Validate: same template
       ├── Combined = 12 + 8 = 20
       ├── Overflow = 20 - 15 = 5
       │
       ├── ModifyItemInstance(target, QuantityDelta: +10) [15 - 5 = add 10]
       │                      (actually adds quantityToAdd = 15 - 12 = 3)
       ├── ModifyItemInstance(source, QuantityDelta: -3) [remainder = 5]
       │
       └── Result: target=15 (full), source=5 (remainder)
```

---

## Stubs & Unimplemented Features

1. **Grid constraint approximation**: Grid containers use slot count as proxy for space tracking. True grid collision detection (tracking occupied cells, item rotation via SlotX/SlotY/Rotated) is not implemented.
<!-- AUDIT:NEEDS_DESIGN:2026-01-31:https://github.com/beyond-immersion/bannou-service/issues/196 -->

2. **Nested container weight propagation**: The `WeightContribution` enum exists and is stored, but parent container ContentsWeight is not automatically updated when child container contents change. Only the immediate container's counters are updated.
<!-- AUDIT:NEEDS_DESIGN:2026-02-01:https://github.com/beyond-immersion/bannou-service/issues/226 -->

3. **Equipment slot specialization**: `IsEquipmentSlot` and `EquipmentSlotName` fields exist on containers but no special equipment-only validation logic (e.g., only allow weapons in weapon slot) is implemented.
<!-- AUDIT:NEEDS_DESIGN:2026-02-01:https://github.com/beyond-immersion/bannou-service/issues/226 -->

4. **RemoveItem does not clear item's ContainerId**: When RemoveItemFromContainer is called, the item's ContainerId field in lib-item is not cleared. The item still references the container it was removed from until AddItemToContainer places it elsewhere. See [#164](https://github.com/beyond-immersion/bannou-service/issues/164) for the design discussion on configurable drop behavior.

5. **Partial quantity transfer not implemented**: TransferItemAsync accepts `quantity` parameter but always transfers `body.Quantity ?? item.Quantity` (full item if null). Partial quantity transfer would require splitting before moving.

---

## Potential Extensions

1. **True grid collision**: Track occupied cells in grid containers. Validate item GridWidth/GridHeight fits at proposed SlotX/SlotY with optional rotation. Would require a separate grid state store per container.

2. **Weight propagation events**: Subscribe to `inventory-item.placed` and `inventory-item.removed` to update parent container weight recursively up the nesting chain.

3. **Container templates**: Define reusable container configurations (slot count, constraints, categories) that can be instantiated, similar to item templates.

4. **Item category indexing**: Build per-category secondary indexes within containers for optimized query filtering. Currently queries fetch all items and filter client-side.

5. **Batch operations**: Add BatchAddItems, BatchRemoveItems, BatchTransfer to reduce cross-service call overhead. Would need careful lock ordering for multi-container operations.

6. **Item event consumption**: Subscribe to `item.destroyed`, `item.bound`, `item.modified` events from lib-item to keep container counters synchronized when items are modified directly via lib-item (bypassing inventory).

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

1. ~~**RemoveItem does not decrement counters if lock fails on source**~~: **NOT A BUG** (2026-01-31) - Code review confirms lock is acquired BEFORE any counter modifications (line 852-857), so lock failure results in clean rejection with no state changes. Moved to Intentional Quirks as documentation of safe behavior.

2. ~~**MergeStacks only locks source container**~~: **FIXED** (2026-01-30) - MergeStacks now acquires locks on both containers when items are in different containers, using deterministic ordering (smaller GUID first) to prevent deadlocks.

### Intentional Quirks (Documented Behavior)

1. **Lock acquired before any modifications**: All mutating operations (remove, add, update, delete, split, merge, transfer) acquire the distributed lock BEFORE reading or modifying container state. If lock acquisition fails, the operation returns `Conflict` with no state changes - clean rejection, no partial processing.

2. **List index locking separate from container lock**: Owner/type indexes use `ListLockTimeoutSeconds` (default 15s, shorter than container locks). Index lock failure is non-fatal - a warning is logged but the main operation succeeds. This means index state can drift from container state if locks fail.

2. **Nesting depth validation uses parent's limit**: When creating a nested container, the depth check uses `parent.MaxNestingDepth`. The new container's own `MaxNestingDepth` only limits its future children.

3. **Missing parent returns BadRequest not NotFound**: If `ParentContainerId` is specified but the parent doesn't exist, returns `StatusCodes.BadRequest` rather than NotFound.

4. **Container deletion with "destroy" continues on item failure**: When deleting with `ItemHandling.Destroy`, individual item destruction failures are logged but don't stop the loop. The container is deleted even if some items couldn't be destroyed. This can orphan items.

5. **Merge stack source destruction failure non-fatal**: When stacks are fully merged, failure to destroy the source item logs a warning but the merge is still considered successful. The source item may remain with zero quantity in lib-item.

6. **WeightContribution.None treated as "use default"**: If `WeightContribution.None` is explicitly specified in a create request, it's replaced with `DefaultWeightContribution` from config. There's no way to explicitly set "no weight propagation" unless the default is changed.

7. **DeleteContainer returns ServiceUnavailable on item service failure**: This is intentional to prevent orphaning items, but callers must handle this gracefully.

### Design Considerations (Requires Planning)

1. **N+1 query pattern**: `QueryItems` and `GetContainer(includeContents)` load items individually from the item service per container. Large inventories with many containers generate many cross-service calls.

2. **No batch item operations**: Split, merge, and transfer operate on single items. Batch versions would require careful lock ordering to prevent deadlocks across multiple containers.

3. **Category constraints are client-side only**: Allowed/forbidden category lists are checked in the inventory service, but lib-item doesn't enforce them. An item could be placed directly via lib-item without category validation, bypassing inventory.

4. **Container deletion item handling is serial**: When deleting with "destroy" strategy, each item is destroyed individually with separate lib-item calls. A container with many items could take significant time under lock.

5. **No event consumption**: Inventory doesn't listen for item events (destroy, bind, modify). If an item is destroyed directly via lib-item (bypassing inventory), the container's UsedSlots/ContentsWeight counters become stale.

6. **Lock timeout not configurable per-operation**: All container operations use the same `LockTimeoutSeconds`. Quick operations (update metadata) and slow operations (delete with destroy) share the same timeout.

7. **QueryItems pagination is inefficient**: All items are fetched from all containers, then offset/limit is applied in memory. For owners with many containers and items, this is O(n) memory even for the first page.

8. **No escrow integration**: lib-escrow has a placeholder comment mentioning inventory but no actual integration. Asset custody for items in escrow is not implemented.

---

## Work Tracking

### Active
- **[#164](https://github.com/beyond-immersion/bannou-service/issues/164)**: Item Removal/Drop Behavior - Design and implementation of configurable drop behavior for removed items. Current `RemoveItemFromContainer` leaves items in limbo (container counters updated but item's ContainerId unchanged). Issue tracks adding per-container drop configuration, a `/inventory/drop` endpoint, and location-owned ground containers.

### Completed
- **2026-01-31**: Audit confirmed "RemoveItem counter decrement on lock fail" is not a bug - lock is acquired before any modifications. Moved to Intentional Quirks.
- **2026-01-30**: Fixed MergeStacks race condition - now locks both containers with deterministic ordering
