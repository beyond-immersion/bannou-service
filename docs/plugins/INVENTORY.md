# Inventory Plugin Deep Dive

> **Plugin**: lib-inventory
> **Schema**: schemas/inventory-api.yaml
> **Version**: 1.0.0
> **State Stores**: inventory-container-store (MySQL+Redis), inventory-container-cache (Redis), inventory-lock (distributed)

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
| lib-item (`IItemClient` via `IServiceNavigator`) | Item template lookups, instance CRUD, container contents listing |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-escrow | Uses `IInventoryClient` for asset custody during exchanges |

---

## State Storage

**Stores**: 3 state stores

| Store | Backend | Purpose |
|-------|---------|---------|
| `inventory-container-store` | MySQL+Redis | Container data and index persistence |
| `inventory-container-cache` | Redis | Container read-through cache |
| `inventory-lock` | Distributed | Container-level modification locks |

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `cont:{containerId}` | `ContainerModel` | Container definition and state |
| `cont-owner:{ownerType}:{ownerId}` | `List<string>` | Container IDs for an owner |
| `cont-type:{containerType}` | `List<string>` | Container IDs of a type |

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
| `InventoryServiceConfiguration` | Singleton | All 9 config properties |
| `IStateStoreFactory` | Singleton | MySQL+Redis state store access |
| `IDistributedLockProvider` | Singleton | Container-level distributed locks |
| `IMessageBus` | Scoped | Event publishing and error events |
| `IServiceNavigator` | Scoped | Item service client access |

Service lifetime is **Scoped** (per-request). No background services.

---

## API Endpoints (Implementation Notes)

### Container Operations (6 endpoints)

- **CreateContainer** (`/inventory/container/create`): Validates capacity parameters per constraint model. Checks nesting depth against parent (if specified). Resolves weight contribution from config default. Saves to MySQL+Redis. Updates owner and type indexes. Publishes `inventory-container.created`.
- **GetContainer** (`/inventory/container/get`): Cache read-through pattern. If `includeContents=true`, calls `IItemClient.ListItemsByContainerAsync()`. Graceful fallback if item service unavailable (returns container with empty contents list, logs warning).
- **GetOrCreateContainer** (`/inventory/container/get-or-create`): Searches owner's containers by owner+type key. Creates if not found (only if `EnableLazyContainerCreation=true`). Idempotent acquisition pattern.
- **ListContainers** (`/inventory/container/list`): Lists containers from owner index. Filters by container type, realm, equipment slot status.
- **UpdateContainer** (`/inventory/container/update`): Acquires distributed lock before modification. Updates: maxSlots, maxWeight, gridDimensions, maxVolume, categories, tags, metadata. Publishes `inventory-container.updated`.
- **DeleteContainer** (`/inventory/container/delete`): Fetches items via lib-item. Three strategies for item handling: `destroy` (calls DestroyItemInstance per item), `transfer` (moves items to target container), `error` (returns 400 if not empty). Cleans up indexes and cache. Publishes `inventory-container.deleted`.

### Inventory Operations (6 endpoints)

- **AddItemToContainer** (`/inventory/add`): Acquires lock. Validates category constraints (allowed/forbidden lists). Checks capacity per constraint model (slots, weight, grid, volume). Increments counters. Publishes `inventory-item.placed`. Emits `inventory-container.full` if capacity reached.
- **RemoveItemFromContainer** (`/inventory/remove`): Gets item's current container via lib-item. Acquires lock. Decrements counters. Publishes `inventory-item.removed`.
- **MoveItem** (`/inventory/move`): Same-container returns OK immediately (no weight changes). Different-container: validates destination constraints, internally removes from source and adds to destination. Publishes `inventory-item.moved`.
- **TransferItem** (`/inventory/transfer`): Checks item is tradeable and not bound. Moves item, publishes `inventory-item.transferred` with source/target owner info. Supports partial quantity transfer.
- **SplitStack** (`/inventory/split`): Validates item is stackable (not Unique). Updates original quantity via lib-item. Creates new instance with split quantity. Publishes `inventory-item.split`.
- **MergeStacks** (`/inventory/merge`): Validates templates match. Respects MaxStackSize (overflow handling: source retains excess). Destroys source if fully merged. Publishes `inventory-item.stacked`.

### Query Operations (4 endpoints)

- **QueryItems** (`/inventory/query`): Lists all owner's containers, fetches items from each, filters by template/category/tags. Client-side filtering after fetch. Pagination via offset/limit.
- **CountItems** (`/inventory/count`): Paginates through QueryItems results up to `MaxCountQueryLimit`. Returns total quantity and stack count for a template.
- **HasItems** (`/inventory/has`): Takes array of template+quantity requirements. Calls CountItems for each. Returns per-item satisfaction boolean.
- **FindSpace** (`/inventory/find-space`): Filters containers by category constraints. Checks capacity via constraint model. If `preferStackable`, seeks existing stacks with room. Returns candidate containers with fit quantities.

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


Distributed Lock Pattern
==========================

  AddItemToContainer(containerId, instanceId)
       │
       ├── Acquire lock: "add-item-{uuid}" on containerId
       │    ├── Success → proceed
       │    └── Timeout → return 409 Conflict
       │
       ├── Load container (cache read-through)
       ├── Validate category constraints
       ├── Check capacity (constraint model)
       ├── Update counters (UsedSlots++, ContentsWeight+=, etc.)
       ├── Save container (write-through: MySQL + Redis cache)
       │
       └── Release lock (via using statement)


Stack Operations
==================

  SplitStack(instanceId, quantity=8)
  (Original stack: 20 potions)
       │
       ├── Validate: quantity < original.Quantity
       ├── Validate: not QuantityModel.Unique
       │
       ├── ModifyItemInstance(original, quantity: 12)  [via lib-item]
       ├── CreateItemInstance(template, quantity: 8)   [via lib-item]
       │
       └── Result: 12 potions (original) + 8 potions (new stack)

  MergeStacks(source, target)
  (Source: 8 potions, Target: 12 potions, MaxStack: 15)
       │
       ├── Validate: same template
       ├── Combined = 12 + 8 = 20
       ├── Overflow = 20 - 15 = 5
       │
       ├── ModifyItemInstance(target, quantity: 15)  [capped]
       ├── ModifyItemInstance(source, quantity: 5)   [remainder]
       │
       └── Result: target=15 (full), source=5 (remainder)


Cache & Lock Architecture
============================

  ┌─────────────────────────────────────────────────────────┐
  │                    Request Handler                       │
  │                         │                                │
  │    ┌────────────────────┼────────────────────────┐       │
  │    │              Lock Provider                   │       │
  │    │   Acquire("inventory-lock", containerId)    │       │
  │    └────────────────────┼────────────────────────┘       │
  │                         │                                │
  │    ┌────────────────────┼────────────────────────┐       │
  │    │         Cache Layer (Redis, 300s TTL)       │       │
  │    │   TryGetFromCache → hit? return             │       │
  │    └────────────────────┼────────────────────────┘       │
  │                         │ miss                           │
  │    ┌────────────────────┼────────────────────────┐       │
  │    │     Persistent Store (MySQL+Redis)          │       │
  │    │   GetAsync → populate cache → return        │       │
  │    └────────────────────┼────────────────────────┘       │
  │                         │                                │
  │    ┌────────────────────┼────────────────────────┐       │
  │    │         Item Service (via mesh)             │       │
  │    │   ListItems, CreateInstance, ModifyInstance  │       │
  │    │   DestroyInstance (graceful on failure)      │       │
  │    └─────────────────────────────────────────────┘       │
  └─────────────────────────────────────────────────────────┘


Container Deletion Strategies
===============================

  DeleteContainer(containerId, itemStrategy)
       │
       ├── itemStrategy = "destroy"
       │    └── For each item: DestroyItemInstanceAsync()
       │
       ├── itemStrategy = "transfer"
       │    └── For each item: MoveItem(targetContainerId)
       │
       └── itemStrategy = "error"
            └── Items exist? → return 400 BadRequest
```

---

## Stubs & Unimplemented Features

1. **Grid constraint approximation**: Grid containers use slot count as proxy for space tracking. True grid collision detection (tracking occupied cells, item rotation) is not implemented.
2. **Nested container weight propagation simplified**: The `WeightContribution` enum exists but weight propagation to parent containers is simplified - parent weight is not automatically updated when child container contents change.
3. **Equipment slot specialization**: `IsEquipmentSlot` and `EquipmentSlotName` fields exist on containers but no special equipment-only validation logic is implemented.

---

## Potential Extensions

1. **True grid collision**: Track occupied cells in grid containers. Validate item GridWidth/GridHeight fits at proposed SlotX/SlotY with optional rotation.
2. **Weight propagation events**: Automatically update parent container weight when child contents change, with recursive propagation up the nesting chain.
3. **Container templates**: Define reusable container configurations (slot count, constraints, categories) that can be instantiated.
4. **Item category indexing**: Build per-category indexes within containers for optimized query filtering.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

No bugs identified.

### Intentional Quirks (Documented Behavior)

1. **Lock acquisition failure returns 409**: When the distributed lock cannot be acquired within `LockTimeoutSeconds`, the operation returns Conflict (not 500). The caller should retry.

2. **Same-container move is no-op**: Moving an item within the same container (different slot) returns OK immediately without modifying weight/volume counters. Only slot position changes.

3. **Item service graceful degradation**: Container read operations continue if lib-item is unavailable (`GetContainer` returns empty contents, logged as warning). `DeleteContainer` returns `ServiceUnavailable` (503) when items can't be fetched, preventing orphaned items.

4. **Stack merge overflow handling**: Merging stacks respects MaxStackSize. If combined exceeds max, target gets capped and source retains the remainder. Source is only destroyed if fully consumed.

5. **Container full event after add**: The `inventory-container.full` event is emitted after successfully adding an item that fills the container. Future add attempts will fail constraint checks.

6. **List index locking separate from container lock**: Owner/type indexes use `ListLockTimeoutSeconds` (default 15s, shorter than container locks). Index lock failure is non-fatal - a warning is logged but the main operation succeeds.

7. **TransferItem checks tradeable and binding**: Transfer validates that the item template's `Tradeable` flag is true AND the instance is not bound. Bound or non-tradeable items cannot be transferred between owners.

### Design Considerations (Requires Planning)

1. **N+1 query pattern**: `QueryItems` and `GetContainer(includeContents)` load items individually from the item service. Large containers generate many cross-service calls.

2. **No batch item operations**: Split, merge, and transfer operate on single items. Batch versions would require careful lock ordering to prevent deadlocks.

3. **Category constraints are client-side**: Allowed/forbidden category lists are checked in the inventory service, but the item service doesn't enforce them. An item could be placed directly via lib-item without category validation.

4. **Container deletion item handling is serial**: When deleting with "destroy" strategy, each item is destroyed individually with separate lib-item calls. A container with many items could take significant time under lock.

5. **No event consumption**: Inventory doesn't listen for item events (destroy, bind, modify). If an item is destroyed directly via lib-item (bypassing inventory), the container's UsedSlots/ContentsWeight counters become stale.

6. **Lock timeout not configurable per-operation**: All container operations use the same `LockTimeoutSeconds`. Quick operations (update metadata) and slow operations (delete with destroy) share the same timeout.

7. **Cache errors are non-fatal**: Cache lookup failures, write failures, and invalidation failures are logged but don't fail operations. Container operations succeed even if Redis cache is unavailable.

8. **Nesting depth validation uses parent's limit**: When creating a nested container, the depth check uses `parent.MaxNestingDepth`. The new container's own `MaxNestingDepth` only limits its future children.

9. **Missing parent returns BadRequest not NotFound**: If `ParentContainerId` is specified but the parent doesn't exist, returns `StatusCodes.BadRequest` rather than NotFound.

10. **Container deletion with "destroy" continues on item failure**: When deleting with `ItemHandling.Destroy`, individual item destruction failures are logged but don't stop the loop. The container is deleted even if some items couldn't be destroyed.

11. **Merge stack source destruction failure non-fatal**: When stacks are fully merged, failure to destroy the source item logs a warning but the merge is still considered successful. The source item may remain with zero quantity.
