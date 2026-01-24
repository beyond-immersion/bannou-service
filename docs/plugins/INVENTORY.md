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
| `LockTimeoutSeconds` | `INVENTORY_LOCK_TIMEOUT_SECONDS` | `30` | Distributed lock expiry |
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

## Tenet Violations (Fix Immediately)

### 1. IMPLEMENTATION TENETS (T21): Hardcoded Index Lock Timeout

**File**: `plugins/lib-inventory/InventoryService.cs`, lines 1912 and 1938

The `AddToListAsync` and `RemoveFromListAsync` methods use a hardcoded `15` second lock timeout instead of a configuration property. Per T21, any tunable value (limits, timeouts, thresholds) MUST be a configuration property.

**Fix**: Add a `ListLockTimeoutSeconds` property to `schemas/inventory-configuration.yaml` with default `15`, regenerate, and replace the hardcoded `15` with `_configuration.ListLockTimeoutSeconds`.

### 2. IMPLEMENTATION TENETS (T25/T21): DefaultWeightContribution Config Is String Requiring Enum.Parse in Business Logic

**File**: `schemas/inventory-configuration.yaml`, line 16; `plugins/lib-inventory/InventoryService.cs`, line 127

The `DefaultWeightContribution` configuration property is defined as `type: string` in the schema. This forces `Enum.TryParse<WeightContribution>` in business logic (line 127). Per T25, Enum.Parse belongs only at system boundaries (deserialization, external input), not in service business logic. The configuration system IS a boundary, but the parse occurs per-request inside CreateContainerAsync rather than once at startup.

**Fix**: Parse the config value once in the constructor and store it as a `WeightContribution` typed field, or change the schema property to reference the WeightContribution enum type so the generated config class uses the enum directly.

### 3. QUALITY TENETS (T10): Validation Failures Logged at Warning Instead of Debug

**File**: `plugins/lib-inventory/InventoryService.cs`, lines 75, 80, 85, 90, 95, 111, 119, 276, 503, 657, 667, 677, 907, 1001, 1007, 1096, 1117, 1245, 528

T10 specifies that "Expected Outcomes" (resource not found, validation failures) should be logged at Debug level. The service logs input validation failures at Warning level. Per T7, these are 400-class responses representing expected user input issues, not security events or unexpected failures.

**Fix**: Change `LogWarning` to `LogDebug` for all input validation failures and expected business rule violations that return 400 BadRequest.

### 4. CLAUDE.md: `?? string.Empty` Without Justification Comment

**File**: `plugins/lib-inventory/InventoryService.cs`, line 1335

Uses `containerEtag ?? string.Empty` without the required explanatory comment. Per CLAUDE.md rules, `?? string.Empty` requires either (1) a comment explaining the coalesce can never execute (compiler satisfaction), or (2) defensive coding for external service with error logging.

**Fix**: Either validate the ETag is non-null with a throw or log, or add a justifying comment explaining why empty string is safe for `TrySaveAsync` in this context.

### 5. QUALITY TENETS (T16): Duplicate Assembly Attributes

**File**: `plugins/lib-inventory/InventoryService.cs`, lines 14-15; `plugins/lib-inventory/AssemblyInfo.cs`, lines 5-6

The `[assembly: InternalsVisibleTo]` attributes are declared in both files redundantly.

**Fix**: Remove the duplicate declarations and the `using System.Runtime.CompilerServices;` import from `InventoryService.cs` (keep them only in `AssemblyInfo.cs`).

### 6. IMPLEMENTATION TENETS (T9): TransferItem Lacks Distributed Lock

**File**: `plugins/lib-inventory/InventoryService.cs`, lines 965-1071

The `TransferItemAsync` method reads source and target containers and validates tradeable/bound state without acquiring a distributed lock. A concurrent modification could change the item state between validation and the delegated `MoveItemAsync` call.

**Fix**: Acquire a distributed lock on the source container before performing tradeable/bound validation.

### 7. IMPLEMENTATION TENETS (T9): DeleteContainer Lacks Distributed Lock

**File**: `plugins/lib-inventory/InventoryService.cs`, lines 467-587

The `DeleteContainerAsync` method performs significant state modifications (item destruction/transfer, index cleanup, container deletion, cache invalidation) without acquiring a distributed lock. Concurrent operations could add items to a container being deleted.

**Fix**: Acquire a distributed lock on the container ID before performing deletion operations.

### 8. IMPLEMENTATION TENETS (T9): SplitStack Does Not Update Container UsedSlots

**File**: `plugins/lib-inventory/InventoryService.cs`, lines 1074-1207

After a successful split, a new item instance is created in the same container but the container's `UsedSlots` counter is never incremented. No distributed lock is acquired on the container. This leaves capacity tracking inconsistent.

**Fix**: Acquire a lock on the container, increment `UsedSlots` after creating the new instance, and save the updated container state.

### 9. IMPLEMENTATION TENETS (T9): MergeStacks Container Update Lacks Distributed Lock

**File**: `plugins/lib-inventory/InventoryService.cs`, lines 1327-1340

The `MergeStacksAsync` method modifies container slot counts without acquiring a distributed lock. Uses `TrySaveAsync` with ETags but has no retry on conflict -- ETag failure leaves slot count permanently stale.

**Fix**: Acquire a distributed lock on the affected container before modifying its slot count, or add retry logic on ETag conflict.

### 10. QUALITY TENETS (T10): Missing Debug-Level Operation Entry Logging

**File**: `plugins/lib-inventory/InventoryService.cs`

T10 requires "Operation Entry (Debug): Log input parameters" for all operations. Most endpoint methods lack Debug-level entry logging. Only `CreateContainerAsync` has an entry log (line 69), but at Information level rather than Debug.

**Fix**: Add `_logger.LogDebug(...)` entry logging at the beginning of each endpoint method. Change line 69 from `LogInformation` to `LogDebug` for the entry log.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

None identified.

### Intentional Quirks (Documented Behavior)

1. **Lock acquisition failure returns 409**: When the distributed lock cannot be acquired within `LockTimeoutSeconds`, the operation returns Conflict (not 500). The caller should retry.

2. **Same-container move is no-op**: Moving an item within the same container (different slot) returns OK immediately without modifying weight/volume counters. Only slot position changes.

3. **Item service graceful degradation**: Container read operations continue if lib-item is unavailable (`GetContainer` returns empty contents, logged as warning). **FIXED**: `DeleteContainer` now returns `ServiceUnavailable` (503) when items can't be fetched, preventing orphaned items. Previously it would proceed with deletion assuming the container was empty.

4. **Stack merge overflow handling**: Merging stacks respects MaxStackSize. If combined exceeds max, target gets capped and source retains the remainder. Source is only destroyed if fully consumed.

5. **Container full event after add**: The `inventory-container.full` event is emitted after successfully adding an item that fills the container. Future add attempts will fail constraint checks.

6. **List index locking separate from container lock**: Owner/type indexes use their own 15-second locks (shorter than the 30-second container lock). Index lock failure is non-fatal - a warning is logged but the main operation succeeds.

7. **TransferItem checks tradeable and binding**: Transfer validates that the item template's `Tradeable` flag is true AND the instance is not bound. Bound or non-tradeable items cannot be transferred between owners.

### Design Considerations (Requires Planning)

1. **N+1 query pattern**: `QueryItems` and `GetContainer(includeContents)` load items individually from the item service. Large containers generate many cross-service calls.

2. **No batch item operations**: Split, merge, and transfer operate on single items. Batch versions would require careful lock ordering to prevent deadlocks.

3. **Category constraints are client-side**: Allowed/forbidden category lists are checked in the inventory service, but the item service doesn't enforce them. An item could be placed directly via lib-item without category validation.

4. **Container deletion item handling is serial**: When deleting with "destroy" strategy, each item is destroyed individually with separate lib-item calls. A container with many items could take significant time under lock.

5. **No event consumption**: Inventory doesn't listen for item events (destroy, bind, modify). If an item is destroyed directly via lib-item (bypassing inventory), the container's UsedSlots/ContentsWeight counters become stale.

6. **Lock timeout not configurable per-operation**: All operations use the same `LockTimeoutSeconds`. Quick operations (update metadata) and slow operations (delete with destroy) share the same timeout.

7. **Cache errors are non-fatal**: Lines 2017-2018, 2036-2037, 2053-2054 - cache lookup failures, write failures, and invalidation failures are all logged at debug/warning level but don't fail operations. Container operations succeed even if Redis cache is unavailable.

8. **Nesting depth validation uses parent's limit**: Line 116 - when creating a nested container, the depth check uses `parent.MaxNestingDepth`. The new container's own `MaxNestingDepth` only limits its future children, not its own creation.

9. **Missing parent returns BadRequest not NotFound**: Line 110-112 - if `ParentContainerId` is specified but the parent doesn't exist, returns `StatusCodes.BadRequest` rather than NotFound. Logged as warning.

10. **Container deletion with "destroy" continues on item failure**: Lines 518-522 - when deleting with `ItemHandling.Destroy`, individual item destruction failures are logged but don't stop the loop. The container is deleted even if some items couldn't be destroyed.

11. **Merge stack source destruction failure non-fatal**: Lines 1322-1325 - when stacks are fully merged, failure to destroy the source item logs a warning but the merge is still considered successful. The source item may remain with zero quantity.
