# Item Plugin Deep Dive

> **Plugin**: lib-item
> **Schema**: schemas/item-api.yaml
> **Version**: 1.0.0
> **State Stores**: item-template-store (MySQL), item-template-cache (Redis), item-instance-store (MySQL), item-instance-cache (Redis), item-lock (Redis)

---

## Overview

Dual-model item management with templates (definitions/prototypes) and instances (individual occurrences). Templates define immutable properties (code, game scope, quantity model, soulbound type) and mutable properties (name, description, stats, effects, rarity). Instances represent actual items in the game world with quantity, slot placement, durability, custom stats, and binding state. Features Redis read-through caching with configurable TTLs, optimistic concurrency for distributed list operations, and multiple quantity models (discrete stacks, continuous weights, unique items). Designed to pair with lib-inventory for container management.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | MySQL persistence + Redis caching for templates and instances |
| lib-messaging (`IMessageBus`) | Publishing item lifecycle events; error event publishing |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-inventory | Uses `IItemClient` for template lookups, instance creation/modification/destruction |
| lib-escrow | References item instances for asset exchange operations |

---

## State Storage

**Stores**: 5 state stores (2 persistent MySQL + 2 cache Redis + 1 lock Redis)

| Store | Backend | Purpose | TTL |
|-------|---------|---------|-----|
| `item-template-store` | MySQL | Template definitions (queryable) | N/A |
| `item-template-cache` | Redis | Template hot cache (read-through) | 3600s (configurable) |
| `item-instance-store` | MySQL | Instance data (realm-partitioned) | N/A |
| `item-instance-cache` | Redis | Instance hot cache (active gameplay) | 900s (configurable) |
| `item-lock` | Redis | Distributed locks for item modifications | N/A (lock timeout 30s) |

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `tpl:{templateId}` | `ItemTemplateModel` | Template definition |
| `tpl-code:{gameId}:{code}` | `string` | Code+game → template ID index |
| `tpl-game:{gameId}` | `List<string>` | All template IDs for a game |
| `all-templates` | `List<string>` | Global index of all template IDs |
| `inst:{instanceId}` | `ItemInstanceModel` | Instance data |
| `inst-container:{containerId}` | `List<string>` | Instance IDs in container |
| `inst-template:{templateId}` | `List<string>` | Instance IDs of a template |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `item-template.created` | `ItemTemplateCreatedEvent` | Template created |
| `item-template.updated` | `ItemTemplateUpdatedEvent` | Template fields changed |
| `item-template.deprecated` | `ItemTemplateDeprecatedEvent` | Template deprecated |
| `item-instance.created` | `ItemInstanceCreatedEvent` | Instance created from template |
| `item-instance.modified` | `ItemInstanceModifiedEvent` | Instance durability/stats/name changed |
| `item-instance.destroyed` | `ItemInstanceDestroyedEvent` | Instance permanently deleted |
| `item-instance.bound` | `ItemInstanceBoundEvent` | Instance bound to character |

### Consumed Events

This plugin does not consume external events.

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `DefaultMaxStackSize` | `ITEM_DEFAULT_MAX_STACK_SIZE` | `99` | Default stack cap when not specified in template |
| `DefaultWeightPrecision` | `ITEM_DEFAULT_WEIGHT_PRECISION` | `decimal_2` | Weight decimal precision (integer, decimal_1/2/3) |
| `DefaultRarity` | `ITEM_DEFAULT_RARITY` | `common` | Default rarity for new templates |
| `DefaultSoulboundType` | `ITEM_DEFAULT_SOULBOUND_TYPE` | `none` | Default binding behavior |
| `TemplateCacheTtlSeconds` | `ITEM_TEMPLATE_CACHE_TTL_SECONDS` | `3600` | Template cache lifetime (1 hour) |
| `InstanceCacheTtlSeconds` | `ITEM_INSTANCE_CACHE_TTL_SECONDS` | `900` | Instance cache lifetime (15 min) |
| `MaxInstancesPerQuery` | `ITEM_MAX_INSTANCES_PER_QUERY` | `1000` | Safety limit for list operations |
| `BindingAllowAdminOverride` | `ITEM_BINDING_ALLOW_ADMIN_OVERRIDE` | `true` | Allow rebinding soulbound items |
| `ListOperationMaxRetries` | `ITEM_LIST_OPERATION_MAX_RETRIES` | `3` | Optimistic concurrency retry budget |
| `LockTimeoutSeconds` | `ITEM_LOCK_TIMEOUT_SECONDS` | `30` | Distributed lock timeout for item modifications |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<ItemService>` | Scoped | Structured logging |
| `ItemServiceConfiguration` | Singleton | All 10 config properties |
| `IStateStoreFactory` | Singleton | Access to 5 state stores (4 data + 1 lock) |
| `IMessageBus` | Scoped | Event publishing and error events |
| `IDistributedLockProvider` | Scoped | Distributed locks for container change operations |

Service lifetime is **Scoped** (per-request). No background services.

---

## API Endpoints (Implementation Notes)

### Template Operations (5 endpoints)

- **CreateItemTemplate** (`/item/template/create`): Validates code uniqueness per game via code index. Applies config defaults for rarity, weight precision, max stack size, soulbound type. Immutable fields set at creation: code, gameId, quantityModel, scope. Populates template cache after save. Updates game index and code index (optimistic concurrency with retries). Publishes `item-template.created`.
- **GetItemTemplate** (`/item/template/get`): Dual lookup via `ResolveTemplateAsync`: by templateId (direct) or by code+gameId (index lookup). Uses `GetTemplateWithCacheAsync` (cache → persistent store → populate cache).
- **ListItemTemplates** (`/item/template/list`): Loads game index, fetches each template. Filters: category, subcategory, tags, rarity, scope, realm, active status, search (name/description). Pagination via offset/limit.
- **UpdateItemTemplate** (`/item/template/update`): Updates mutable fields only. Invalidates template cache after save. Publishes `item-template.updated`.
- **DeprecateItemTemplate** (`/item/template/deprecate`): Marks template inactive. Optional `migrationTargetId` for upgrade paths. Existing instances remain valid. Invalidates cache. Publishes `item-template.deprecated`.

### Instance Operations (5 endpoints)

- **CreateItemInstance** (`/item/instance/create`): Validates template exists and is active (but not IsDeprecated - see quirk #9). Quantity enforcement: Unique→1, Discrete→floor(value) capped at MaxStackSize, Continuous→as-is. Populates instance cache. Updates container index and template index (optimistic concurrency). Publishes `item-instance.created`.
- **GetItemInstance** (`/item/instance/get`): Cache read-through pattern. Returns instance with template reference.
- **ModifyItemInstance** (`/item/instance/modify`): Updates durability (delta), quantityDelta, customStats, customName, instanceMetadata, container/slot position. Container changes use distributed lock via `item-lock` store to prevent race conditions on index updates. Non-container changes skip locking. Invalidates instance cache. Publishes `item-instance.modified`.
- **BindItemInstance** (`/item/instance/bind`): Binds instance to character ID. Checks `BindingAllowAdminOverride` for rebinding. Enriches event with template code (fallback: `missing:{templateId}` if template not found). Publishes `item-instance.bound`.
- **DestroyItemInstance** (`/item/instance/destroy`): Validates template's `Destroyable` flag unless reason="admin". Removes from container and template indexes. Invalidates cache. Publishes `item-instance.destroyed`.

### Query Operations (3 endpoints)

- **ListItemsByContainer** (`/item/instance/list-by-container`): Loads container index, fetches each instance. Enforces `MaxInstancesPerQuery` as hard limit (not pagination - excess silently truncated).
- **ListItemsByTemplate** (`/item/instance/list-by-template`): Loads template index with optional realm filter. Pagination support.
- **BatchGetItemInstances** (`/item/instance/batch-get`): Bulk retrieval by instance IDs. Returns found items and `notFound` ID list separately.

---

## Visual Aid

```
Dual-Model Architecture
=========================

  ItemTemplate (Definition)             ItemInstance (Occurrence)
  ┌──────────────────────────┐         ┌──────────────────────────┐
  │ TemplateId (immutable)   │    ┌───→│ InstanceId               │
  │ Code (immutable)         │    │    │ TemplateId ──────────────┘
  │ GameId (immutable)       │    │    │ ContainerId              │
  │ QuantityModel (immutable)│    │    │ RealmId                  │
  │ Scope (immutable)        │    │    │ Quantity                 │
  │ SoulboundType (immutable)│    │    │ SlotIndex / SlotX,Y      │
  ├──────────────────────────┤    │    │ Rotated                  │
  │ Name (mutable)           │    │    │ CurrentDurability        │
  │ Description              │    │    │ BoundToId (character)    │
  │ Category, Subcategory    │    │    │ CustomStats (JSON)       │
  │ Tags, Rarity             │    │    │ CustomName               │
  │ Weight, Volume           │    │    │ OriginType (loot/craft/…)│
  │ GridWidth/Height         │    │    │ OriginId                 │
  │ MaxStackSize             │    │    └──────────────────────────┘
  │ Stats, Effects (JSON)    │    │
  │ Requirements (JSON)      │    │    One template → Many instances
  │ Display (JSON)           │    │
  └──────────────────────────┘    │
           │                       │
           └───────────────────────┘


Cache Read-Through Pattern
============================

  GetTemplateWithCacheAsync(templateId)
       │
       ├── Try Redis cache (item-template-cache)
       │    └── Hit? → Return cached model
       │
       ├── Miss → Load from MySQL (item-template-store)
       │
       └── Populate Redis cache with TTL (3600s)
            └── Return model


Quantity Models
================

  QuantityModel.Discrete (stackable integers):
    ├── Quantity floored to integer
    ├── Capped at MaxStackSize (default 99)
    └── Example: 50 arrows, 20 potions

  QuantityModel.Continuous (decimal weights):
    ├── Allows fractional quantities
    └── Example: 2.5 kg of ore, 0.3 liters of potion

  QuantityModel.Unique (single items):
    ├── Quantity forced to 1
    └── Example: named sword, quest item


Optimistic Concurrency for List Operations
=============================================

  AddToListAsync(key, value)
       │
       ├── for attempt = 0..ListOperationMaxRetries:
       │    ├── GetWithETagAsync(key) → (json, etag)
       │    ├── Deserialize → list, Add(value)
       │    ├── TrySaveAsync(key, serialized, etag)
       │    │    ├── Success (etag returned) → done
       │    │    └── Conflict (null) → retry
       │    └── Next attempt
       │
       └── All retries exhausted → log warning (operation succeeds anyway)


Soulbound Types
=================

  none       → Item freely tradeable
  on_pickup  → Binds when first acquired (instance creation)
  on_equip   → Binds when equipped (external trigger)
  on_use     → Binds when consumed/used (external trigger)
```

---

## Stubs & Unimplemented Features

1. **Unbound event not implemented**: `item-instance.unbound` event type is defined in the event schema but no unbinding endpoint exists. Items can only be bound, not unbound (except via admin override rebinding).
2. **Deprecation without cascade**: Deprecating a template doesn't automatically migrate, disable, or destroy existing instances. Admin must manage instances separately.

---

## Potential Extensions

1. **Unbind endpoint**: Allow admin-level unbinding of soulbound items with event publishing.
2. **Template migration**: When deprecating with `migrationTargetId`, automatically upgrade instances to the new template.
3. **Affix system**: Random or crafted modifiers applied to instances (prefixes/suffixes).
4. **Durability repair**: Endpoint to restore durability with configurable repair costs.

---

## Known Quirks & Caveats

### Bugs

No bugs identified.

### Intentional Quirks

1. **Quantity flooring for Discrete**: When creating discrete instances, the quantity is `Math.Floor()`'d to the nearest integer. A request for 5.7 arrows creates 5.

2. **MaxInstancesPerQuery is a hard cap**: `ListItemsByContainer` enforces the limit as truncation, not pagination. If a container has 1001 items and the limit is 1000, the last item is silently excluded.

3. **Bind event enrichment fallback**: When binding an item, if the template cannot be loaded (data inconsistency), the event's `TemplateCode` field is set to `missing:{templateId}` rather than failing the operation.

4. **Optimistic concurrency doesn't fail requests**: If all retries for list operations (index updates) are exhausted, the operation logs a warning but the main create/destroy still succeeds. The index may be temporarily inconsistent.

5. **Update doesn't track changedFields**: Unlike other services that track which fields changed, `UpdateItemTemplateAsync` applies all provided changes without changedFields list in the event. Consumers can't tell which fields were actually modified.

6. **ListItemsByContainer doesn't support pagination**: Unlike `ListItemsByTemplate` which uses Offset/Limit from the request, `ListItemsByContainer` just returns up to `MaxInstancesPerQuery` items with no offset support. Large containers lose items silently.

7. **Bind doesn't enforce SoulboundType**: `BindItemInstanceAsync` binds any item regardless of its template's `SoulboundType`. The soulbound type is metadata for game logic, not enforced by the service.

8. **Deprecate is idempotent (no conflict)**: Unlike other services that return Conflict if already deprecated, `DeprecateItemTemplateAsync` will re-deprecate with a new timestamp, overwriting the original deprecation timestamp.

9. **CreateInstance validates IsActive but not IsDeprecated**: Checks `!template.IsActive` but not `template.IsDeprecated`. A deprecated but still-active template can continue spawning new instances.

### Design Considerations

1. ~~**List index N+1 loading**~~: **FIXED** (2026-01-31) - `ListItemsByContainer` and `ListItemsByTemplate` now use `GetInstancesBulkWithCacheAsync` for two-tier bulk loading (Redis cache first, then MySQL for misses). Cache population happens in bulk after persistent store fetch.

2. **No template deletion**: Templates can only be deprecated, never deleted. This preserves instance integrity but means the template store grows monotonically.

3. **JSON-stored complex fields**: Stats, effects, requirements, display, and metadata are stored as serialized JSON strings. No schema validation is performed on these fields - they're opaque to the item service.

4. **Container index not validated**: The item service trusts the `containerId` provided during creation. It does not validate that the container exists in the inventory service. Related: [#164](https://github.com/beyond-immersion/bannou-service/issues/164) discusses making items temporarily "containerless" during drop operations.

5. **No event consumption**: The item service is purely a publisher. It doesn't react to external events (e.g., container deletion). The inventory service is responsible for calling `DestroyItemInstance` when needed. Related: [#164](https://github.com/beyond-immersion/bannou-service/issues/164) explores event-driven drop handling as one design option.

6. **Destroy bypasses destroyable check with "admin" reason**: If `body.Reason == "admin"`, the template's `Destroyable` flag is ignored, allowing admin-level destruction of indestructible items.

7. ~~**BatchGetItemInstances is sequential**~~: **FIXED** (2026-01-31) - `BatchGetItemInstances` now uses `GetInstancesBulkWithCacheAsync` for two-tier bulk loading with a maximum of 2 database round-trips (cache check + persistent store for misses).

8. **Empty container/template index not cleaned up**: After `RemoveFromListAsync`, if the list becomes empty, it remains as an empty JSON array `[]` in the store rather than being deleted.

9. **ListItemsByTemplate filters AFTER fetching all instances**: All instances are fetched then filtered by RealmId in memory. For templates with many instances, this fetches far more data than needed.

---

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above. Items here are managed by the `/audit-plugin` workflow.

### Completed

- **2026-01-31**: N+1 bulk loading optimization - Added `GetInstancesBulkWithCacheAsync` helper that performs two-tier bulk loading (Redis cache → MySQL persistent store for misses → bulk cache population). Applied to `ListItemsByContainer`, `ListItemsByTemplate`, and `BatchGetItemInstances`. Maximum 2 database round-trips regardless of item count. See Issue #168 for `IStateStore` bulk operations.

### Related (Cross-Service)
- **[#164](https://github.com/beyond-immersion/bannou-service/issues/164)**: Item Removal/Drop Behavior - Owned by lib-inventory, but affects lib-item's container index and event patterns. See Design Considerations #4 and #5.
