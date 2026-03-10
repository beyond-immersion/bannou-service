# Inventory Plugin Deep Dive

> **Plugin**: lib-inventory
> **Schema**: schemas/inventory-api.yaml
> **Version**: 1.0.0
> **Layer**: GameFoundation
> **State Store**: inventory-container-store (MySQL), inventory-container-cache (Redis), inventory-lock (Redis)
> **Implementation Map**: [docs/maps/INVENTORY.md](../maps/INVENTORY.md)
> **Short**: Container and item placement with constraint models (slot/weight/grid/volumetric/unlimited)

---

## Overview

Container and item placement management (L2 GameFoundation) for games. Handles container lifecycle (CRUD), item movement between containers, stacking operations (split/merge), and inventory queries. Does NOT handle item definitions or instances directly -- delegates to lib-item for all item-level operations. Supports multiple constraint models (slot-only, weight-only, grid, volumetric, unlimited), category restrictions, and nesting depth limits. Designed as the placement layer that orchestrates lib-item.

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-collection (L2) | Uses "items in inventories" pattern: creates inventory containers per owner for collection entries |
| lib-quest (L2) | Calls `HasItemsAsync()` for `ITEM_OWNED` prerequisite validation |
| lib-license (L4) | Uses inventory containers for grid-based progression boards (license nodes as item instances) |
| lib-status (L4) | Uses "items in inventories" pattern: status containers hold per-entity status effect items |

**Planned dependents**:
- lib-craft (L4): Material consumption from source inventories, output placement to destination inventories
- lib-escrow (L4): Asset custody for items in escrow (placeholder exists, integration tracked by [#153](https://github.com/beyond-immersion/bannou-service/issues/153))
- ABML action handlers: `inventory_add`/`inventory_has` handlers for NPC behavior ([#428](https://github.com/beyond-immersion/bannou-service/issues/428))

**Natural L4 consumers** (standard L4→L2 API callers, no special integration needed):
- lib-loot (L4): Places generated items into containers after drop resolution
- lib-trade (L4): Cargo management, supply shipment inventory operations
- lib-workshop (L4): Material consumption from source inventories, output placement to destination inventories
- lib-dungeon (L4): Loot containers in dungeon rooms, treasure placement

---

## Type Field Classification

| Field | Category | Type | Rationale |
|-------|----------|------|-----------|
| `ownerType` | **EXCEPTION** (Mixed Entity + Non-Entity) | `ContainerOwnerType` enum | Service-specific enum rather than shared `EntityType` because it includes non-entity roles: `character`, `account`, `location`, `vehicle`, `guild`, `escrow`, `mail`, `other`. The `escrow` and `mail` values represent system custody contexts, not first-class Bannou entities. |
| `containerType` | B (Content Code) | Opaque string | Game-configurable container classification (e.g., `inventory`, `bank`, `equipment_slot`, `loot_bag`, `mail_inbox`). Extensible without schema changes. |
| `constraintModel` | C (System State) | `ContainerConstraintModel` enum | Finite system-owned capacity modes: `slot_only`, `weight_only`, `slot_and_weight`, `grid`, `volumetric`, `unlimited`. |
| `weightContribution` | C (System State) | `WeightContribution` enum | Finite propagation modes: `none`, `self_only`, `self_plus_contents`. Controls how container weight propagates to parent. |
| `constraintType` (ContainerFullEvent) | C (System State) | `ConstraintLimitType` enum | Finite capacity constraint types: `slots`, `weight`, `volume`, `grid`. Identifies which constraint was reached. |

---

## Client Events (WebSocket Push)

Client events are pushed via `IEntitySessionRegistry.PublishToEntitySessionsAsync` using entity type `"inventory"` with the container owner's ID as the entity ID. Higher-layer services (e.g., Gardener) register `inventory → session` bindings so that connected clients receive real-time updates.

| Event Name | Event Type | Trigger |
|-----------|-----------|---------|
| `inventory.item_changed` | `InventoryItemChangedClientEvent` | Item placed, removed, moved, stacked, or split (uses `InventoryItemChangeType` discriminator) |
| `inventory.container_full` | `InventoryContainerFullClientEvent` | Container reached capacity limit |
| `inventory.item_transferred` | `InventoryItemTransferredClientEvent` | Item transferred between owners (both source and target owner sessions notified) |

**Composite operation suppression**: Cross-container moves (Remove+Add) and transfers (Split+Move) suppress intermediate client events from sub-operations and publish a single consolidated event. This prevents clients from receiving 3-6 intermediate updates for a single logical operation.

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `DefaultMaxNestingDepth` | `INVENTORY_DEFAULT_MAX_NESTING_DEPTH` | `3` | Maximum container nesting depth |
| `DefaultWeightContribution` | `INVENTORY_DEFAULT_WEIGHT_CONTRIBUTION` | `self_plus_contents` | How weight propagates to parent |
| `ContainerCacheTtlSeconds` | `INVENTORY_CONTAINER_CACHE_TTL_SECONDS` | `300` | Redis cache TTL (5 min) |
| `ProviderCacheTtlSeconds` | `INVENTORY_PROVIDER_CACHE_TTL_SECONDS` | `60` | In-memory variable provider cache TTL for actor behavior |
| `LockTimeoutSeconds` | `INVENTORY_LOCK_TIMEOUT_SECONDS` | `30` | Container-level distributed lock expiry |
| `DeleteLockTimeoutSeconds` | `INVENTORY_DELETE_LOCK_TIMEOUT_SECONDS` | `120` | Container deletion lock expiry (longer for serial item handling) |
| `ListLockTimeoutSeconds` | `INVENTORY_LIST_LOCK_TIMEOUT_SECONDS` | `15` | Owner/type index list lock expiry |
| `EnableLazyContainerCreation` | `INVENTORY_ENABLE_LAZY_CONTAINER_CREATION` | `true` | Allow get-or-create pattern |
| `DefaultMaxSlots` | `INVENTORY_DEFAULT_MAX_SLOTS` | `20` | Default slot count for new containers |
| `DefaultMaxWeight` | `INVENTORY_DEFAULT_MAX_WEIGHT` | `100.0` | Default weight capacity |
| `MaxCountQueryLimit` | `INVENTORY_MAX_COUNT_QUERY_LIMIT` | `10000` | Max items to scan during count |
| `QueryPageSize` | `INVENTORY_QUERY_PAGE_SIZE` | `200` | Items per page for queries |

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
 │ │
 │ ├── TryGetContainerFromCacheAsync (Redis)
 │ │ └── Hit? Return cached
 │ │
 │ ├── Miss → GetAsync (MySQL)
 │ │
 │ └── Populate cache → UpdateContainerCacheAsync
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
 │ (actually adds quantityToAdd = 15 - 12 = 3)
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

4. **No configurable drop behavior**: RemoveItem clears the item's ContainerId (setting it to null via lib-item), but there is no configurable "drop" system — removed items simply become uncontained. No per-container drop configuration, no `/inventory/drop` endpoint, and no location-owned ground containers exist. See [#164](https://github.com/beyond-immersion/bannou-service/issues/164) for the design discussion.
<!-- AUDIT:NEEDS_DESIGN:2026-02-01:https://github.com/beyond-immersion/bannou-service/issues/164 -->

---

## Potential Extensions

1. **True grid collision**: Track occupied cells in grid containers. Validate item GridWidth/GridHeight fits at proposed SlotX/SlotY with optional rotation. Would require a separate grid state store per container.
<!-- AUDIT:NEEDS_DESIGN:2026-02-25:https://github.com/beyond-immersion/bannou-service/issues/196 -->

2. **Weight propagation events**: Subscribe to `inventory.item.placed` and `inventory.item.removed` to update parent container weight recursively up the nesting chain.
<!-- AUDIT:NEEDS_DESIGN:2026-02-25:https://github.com/beyond-immersion/bannou-service/issues/226 -->

3. **Container templates**: Define reusable container configurations (slot count, constraints, categories) that can be instantiated, similar to item templates.
<!-- AUDIT:NEEDS_DESIGN:2026-02-25:https://github.com/beyond-immersion/bannou-service/issues/481 -->

4. **Item category indexing**: Build per-category secondary indexes within containers for optimized query filtering. Currently queries fetch all items and filter client-side.
<!-- AUDIT:NEEDS_DESIGN:2026-02-25:https://github.com/beyond-immersion/bannou-service/issues/482 -->

5. **Batch operations**: Add BatchAddItems, BatchRemoveItems, BatchTransfer to reduce cross-service call overhead. Would need careful lock ordering for multi-container operations.
<!-- AUDIT:NEEDS_DESIGN:2026-02-25:https://github.com/beyond-immersion/bannou-service/issues/483 -->

6. **Item event consumption**: Subscribe to `item.destroyed`, `item.bound`, `item.modified` events from lib-item to keep container counters synchronized when items are modified directly via lib-item (bypassing inventory). See also [#407](https://github.com/beyond-immersion/bannou-service/issues/407) (Item Decay) as a concrete need for consuming item lifecycle events.
<!-- AUDIT:NEEDS_DESIGN:2026-02-25:https://github.com/beyond-immersion/bannou-service/issues/484 -->

7. **Mail as remote inventory**: Implement a mailbox system using inventory containers with COD (cash-on-delivery) escrow integration. See [#283](https://github.com/beyond-immersion/bannou-service/issues/283).
<!-- AUDIT:NEEDS_DESIGN:2026-02-25:https://github.com/beyond-immersion/bannou-service/issues/283 -->

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

(None currently identified.)

### Intentional Quirks (Documented Behavior)

1. **Lock acquired before any modifications**: All mutating operations (remove, add, update, delete, split, merge, transfer) acquire the distributed lock BEFORE reading or modifying container state. If lock acquisition fails, the operation returns `Conflict` with no state changes - clean rejection, no partial processing.

2. **List index locking separate from container lock**: Owner/type indexes use `ListLockTimeoutSeconds` (default 15s, shorter than container locks). Index lock failure is non-fatal - a warning is logged but the main operation succeeds. This means index state can drift from container state if locks fail.

3. **Nesting depth validation uses parent's limit**: When creating a nested container, the depth check uses `parent.MaxNestingDepth`. The new container's own `MaxNestingDepth` only limits its future children.

4. **MergeStacks uses deterministic lock ordering**: When merging stacks across different containers, locks are acquired in GUID order (smaller GUID first) to prevent deadlocks. Both containers are locked for the duration of the merge. Operations may briefly conflict if another operation is locking in the opposite order.

5. **Partial transfer returns split item ID**: When `TransferItemAsync` is called with a quantity less than the item's total, the stack is split first, then the split portion is moved. The response `InstanceId` is the newly created split item, not the original. The original retains the remainder.

6. **Missing parent returns BadRequest not NotFound**: If `ParentContainerId` is specified but the parent doesn't exist, returns `StatusCodes.BadRequest` rather than NotFound.

7. **Container deletion with "destroy" continues on item failure**: When deleting with `ItemHandling.Destroy`, individual item destruction failures are logged but don't stop the loop. The container is deleted even if some items couldn't be destroyed. This can orphan items.

8. **Merge stack source destruction failure non-fatal**: When stacks are fully merged, failure to destroy the source item logs a warning but the merge is still considered successful. The source item may remain with zero quantity in lib-item.

9. **WeightContribution.None treated as "use default"**: If `WeightContribution.None` is explicitly specified in a create request, it's replaced with `DefaultWeightContribution` from config. There's no way to explicitly set "no weight propagation" unless the default is changed.

10. **DeleteContainer returns ServiceUnavailable on item service failure**: This is intentional to prevent orphaning items, but callers must handle this gracefully.

11. **MoveItem/TransferItem generate multiple events per operation**: Cross-container MoveItem internally calls RemoveItemFromContainer (publishes `inventory.item.removed`) then AddItemToContainer (publishes `inventory.item.placed`), then publishes `inventory.item.moved` — 3 events total. TransferItem adds a 4th (`inventory.item.transferred`). Same-container MoveItem publishes only `inventory.item.moved` (1 event). Consumers should handle idempotently and be aware of the event sequence.

12. **GetOrCreateContainer and ListContainers bypass Redis cache**: These methods read containers directly from MySQL via `containerStore.GetAsync()` instead of using `GetContainerWithCacheAsync()`. Only GetContainer, AddItemToContainer, RemoveItemFromContainer, and other single-container operations use the Redis cache read-through. For high-frequency GetOrCreateContainer calls (e.g., lazy creation pattern), this means every call hits MySQL.

13. **Category constraints enforced at inventory layer only**: AllowedCategories/ForbiddenCategories are validated by Inventory's `AddItemToContainerAsync` and `CheckContainerFitForItem`, but lib-item's `CreateItemInstanceAsync` and `ModifyItemInstanceAsync` do not enforce them. This is an intentional architectural boundary: Inventory is the placement/constraint layer, Item is the data layer. Item cannot depend on Inventory (circular dependency — Inventory already depends on Item). Services that need category enforcement must go through Inventory's `AddItemToContainer`; services that own their containers (Collection, Status, License) can safely call lib-item directly because they control both the container configuration and the items being placed.

### Design Considerations (Requires Planning)

1. **N+1 query pattern**: `QueryItems` and `GetContainer(includeContents)` load items individually from the item service per container. Large inventories with many containers generate many cross-service calls.
<!-- AUDIT:NEEDS_DESIGN:2026-02-25:https://github.com/beyond-immersion/bannou-service/issues/485 -->

2. **No batch item operations**: Split, merge, and transfer operate on single items. Batch versions would require careful lock ordering to prevent deadlocks across multiple containers.
<!-- AUDIT:NEEDS_DESIGN:2026-02-25:https://github.com/beyond-immersion/bannou-service/issues/483 -->

3. **Container deletion item handling is serial**: When deleting with "destroy" strategy, each item is destroyed individually with separate lib-item calls. A container with many items could take significant time under lock.
<!-- AUDIT:NEEDS_DESIGN:2026-02-25:https://github.com/beyond-immersion/bannou-service/issues/483 -->

4. **No event consumption**: Inventory doesn't listen for item events (destroy, bind, modify). If an item is destroyed directly via lib-item (bypassing inventory), the container's UsedSlots/ContentsWeight counters become stale.
<!-- AUDIT:NEEDS_DESIGN:2026-02-25:https://github.com/beyond-immersion/bannou-service/issues/484 -->

5. **QueryItems pagination is inefficient**: All items are fetched from all containers, then offset/limit is applied in memory. For owners with many containers and items, this is O(n) memory even for the first page.
<!-- AUDIT:NEEDS_DESIGN:2026-02-25:https://github.com/beyond-immersion/bannou-service/issues/485 -->

6. **No escrow integration**: lib-escrow has a placeholder comment mentioning inventory but no actual integration. Asset custody for items in escrow is not implemented. See [#153](https://github.com/beyond-immersion/bannou-service/issues/153).
<!-- AUDIT:NEEDS_DESIGN:2026-02-25:https://github.com/beyond-immersion/bannou-service/issues/153 -->

7. **Container orphan cleanup on owner deletion**: When an owner entity is deleted, its inventory containers must be cleaned up. For L2 owners (Character, Location), cleanup is handled via lib-resource (`x-references` with CASCADE policy). For **Account-owned containers**, Inventory MUST subscribe to `account.deleted` and clean up all account-owned containers per tenets's Account Deletion Cleanup Obligation. This is the established pattern — accounts are the one entity where event-based cleanup is mandatory because lib-resource cannot track account references (privacy constraint). **Not yet implemented** — see [#593](https://github.com/beyond-immersion/bannou-service/issues/593). Reference implementation: `lib-collection/CollectionServiceEvents.cs`.
<!-- AUDIT:TODO:2026-03-08:https://github.com/beyond-immersion/bannou-service/issues/593 -->

---

## Work Tracking

### Active
- **[#147](https://github.com/beyond-immersion/bannou-service/issues/147)**: Phase 2 variable providers (inventory portion implemented — `InventoryProviderFactory` + `IInventoryDataCache` registered; issue remains open for Currency and Relationship providers)
- **[#153](https://github.com/beyond-immersion/bannou-service/issues/153)**: Cross-cutting escrow asset transfer integration (inventory deposit/release)
- **[#164](https://github.com/beyond-immersion/bannou-service/issues/164)**: Item Removal/Drop Behavior - configurable drop behavior for removed items
- **[#196](https://github.com/beyond-immersion/bannou-service/issues/196)**: True grid collision detection for grid-based containers
- **[#226](https://github.com/beyond-immersion/bannou-service/issues/226)**: Weight propagation and equipment slot validation
- **[#283](https://github.com/beyond-immersion/bannou-service/issues/283)**: Mail as remote inventory with COD escrow
- **[#407](https://github.com/beyond-immersion/bannou-service/issues/407)**: Item decay/expiration system (drives item event consumption need)
- **[#428](https://github.com/beyond-immersion/bannou-service/issues/428)**: ABML economic action handlers (inventory_add/inventory_has)
- **[#481](https://github.com/beyond-immersion/bannou-service/issues/481)**: Container template/definition pattern for reusable container configs
- **[#482](https://github.com/beyond-immersion/bannou-service/issues/482)**: Item category indexing for inventory query optimization
- **[#484](https://github.com/beyond-immersion/bannou-service/issues/484)**: Item event consumption for container counter synchronization
- **[#485](https://github.com/beyond-immersion/bannou-service/issues/485)**: N+1 query pattern — batch item listing across containers

### Completed
- **2026-02-25**: Reclassified "Category constraints are client-side only" from Design Considerations to Intentional Quirks (#13) — intentional architectural boundary (Inventory is placement layer, Item is data layer, no circular dependency)
- **2026-02-24**: L3 hardening pass - NRT fix, filler removal from 7 responses, metadata disclaimers, event schema additionalProperties, x-lifecycle model completion, validation keywords, sentinel fix, telemetry spans, 24 new tests (93 total)
- **2026-02-24**: Closed #310 (silent failure patterns) - IItemClient now constructor-injected (hard dependency)
- **2026-02-24**: Closed #317 (quest ITEM_OWNED prerequisite) - QuestService calls HasItemsAsync()
