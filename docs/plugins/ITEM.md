# Item Plugin Deep Dive

> **Plugin**: lib-item
> **Schema**: schemas/item-api.yaml
> **Version**: 1.0.0
> **State Stores**: item-template-store (MySQL), item-template-cache (Redis), item-instance-store (MySQL), item-instance-cache (Redis)

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

**Stores**: 4 state stores (2 persistent MySQL + 2 cache Redis)

| Store | Backend | Purpose | TTL |
|-------|---------|---------|-----|
| `item-template-store` | MySQL | Template definitions (queryable) | N/A |
| `item-template-cache` | Redis | Template hot cache (read-through) | 3600s (configurable) |
| `item-instance-store` | MySQL | Instance data (realm-partitioned) | N/A |
| `item-instance-cache` | Redis | Instance hot cache (active gameplay) | 900s (configurable) |

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

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<ItemService>` | Scoped | Structured logging |
| `ItemServiceConfiguration` | Singleton | All 9 config properties |
| `IStateStoreFactory` | Singleton | Access to 4 state stores |
| `IMessageBus` | Scoped | Event publishing and error events |

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

- **CreateItemInstance** (`/item/instance/create`): Validates template is active and not deprecated. Quantity enforcement: Unique→1, Discrete→floor(value) capped at MaxStackSize, Continuous→as-is. Populates instance cache. Updates container index and template index (optimistic concurrency). Publishes `item-instance.created`.
- **GetItemInstance** (`/item/instance/get`): Cache read-through pattern. Returns instance with template reference.
- **ModifyItemInstance** (`/item/instance/modify`): Updates durability (delta), customStats, customName, instanceMetadata. Invalidates instance cache. Publishes `item-instance.modified`.
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

## Tenet Violations (Fix Immediately)

### 1. IMPLEMENTATION TENETS (T25): String-for-Guid Fields in Internal Models

**File**: `plugins/lib-item/ItemService.cs`, lines 1131-1191

Both `ItemTemplateModel` and `ItemInstanceModel` store all GUID fields as `string` types:
- `ItemTemplateModel.TemplateId` (line 1131) - should be `Guid`
- `ItemTemplateModel.AvailableRealms` (line 1156) - `List<string>?` should be `List<Guid>?`
- `ItemTemplateModel.MigrationTargetId` (line 1165) - `string?` should be `Guid?`
- `ItemInstanceModel.InstanceId` (line 1175) - should be `Guid`
- `ItemInstanceModel.TemplateId` (line 1176) - should be `Guid`
- `ItemInstanceModel.ContainerId` (line 1177) - should be `Guid`
- `ItemInstanceModel.RealmId` (line 1178) - should be `Guid`
- `ItemInstanceModel.BoundToId` (line 1185) - `string?` should be `Guid?`
- `ItemInstanceModel.OriginId` (line 1191) - `string?` should be `Guid?`

**Fix**: Change all string GUID fields to proper `Guid`/`Guid?` types.

### 2. IMPLEMENTATION TENETS (T25): `.ToString()` Populating Internal Models

**File**: `plugins/lib-item/ItemService.cs`, lines 91, 116, 130, 289, 352, 442-445, 456, 617

Enum and Guid values are converted to strings when creating/updating models. Per T25, enums and Guids should be assigned directly.

**Fix**: Remove `.ToString()` calls; assign typed values directly once models use proper types.

### 3. IMPLEMENTATION TENETS (T25): `Guid.Parse()` in Business Logic

**File**: `plugins/lib-item/ItemService.cs`, lines 567-569, 642-644, 703-705, 717, 1055, 1080, 1089, 1099-1101, 1109, 1115

Fragile `Guid.Parse()` calls scattered through mapping and event publishing. Direct consequence of string-typed model fields.

**Fix**: Remove all `Guid.Parse` calls once models use `Guid` types.

### 4. FOUNDATION TENETS (T6): Constructor Missing Null Checks

**File**: `plugins/lib-item/ItemService.cs`, lines 49-61

Constructor assigns all five dependencies directly without null checks. Per T6, must use `?? throw new ArgumentNullException(nameof(...))`.

**Fix**: Add null-check pattern to all constructor parameter assignments.

### 5. IMPLEMENTATION TENETS (T7): Missing ApiException Catch Distinction

**File**: `plugins/lib-item/ItemService.cs`, all 13 try-catch blocks

Every method catches only `Exception` generically. Per T7, must catch `ApiException` specifically first (log as Warning, propagate status code).

**Fix**: Add `catch (ApiException ex)` before each `catch (Exception ex)`.

### 6. IMPLEMENTATION TENETS (T25): Enum.Parse in Business Logic

**File**: `plugins/lib-item/ItemService.cs`, lines 99, 103, 112

- Line 99: `Enum.Parse<ItemRarity>(_configuration.DefaultRarity, ignoreCase: true)`
- Line 103: `Enum.Parse<WeightPrecision>(_configuration.DefaultWeightPrecision, ignoreCase: true)`
- Line 112: `Enum.Parse<SoulboundType>(_configuration.DefaultSoulboundType, ignoreCase: true)`

Per T25, parse only at boundaries. These are in core business logic.

**Fix**: Parse config values once in constructor into typed fields, or change schema to use enum types.

### 7. IMPLEMENTATION TENETS (T21): Configuration String Types Force Runtime Parsing

**File**: `schemas/item-configuration.yaml`, lines 19, 44, 49

Properties `defaultRarity`, `defaultWeightPrecision`, and `defaultSoulboundType` are typed as `string` instead of enum types, forcing runtime `Enum.Parse` in business logic.

**Fix**: Change schema types to reference enum definitions, or parse once at startup.

### 8. QUALITY TENETS (T22): Pragma Warning Suppression for Unused Field

**File**: `plugins/lib-item/ItemService.cs`, lines 30-33

```csharp
#pragma warning disable IDE0052
private readonly IServiceNavigator _navigator;
#pragma warning restore IDE0052
```

Per T22, "retained for future use" is not an allowed suppression exception.

**Fix**: Remove the field, import, and constructor parameter. Add back when needed.

### 9. ~~IMPLEMENTATION TENETS (T9): TOCTOU Race in Template Code Uniqueness~~ FIXED

**File**: `plugins/lib-item/ItemService.cs`

~~`CreateItemTemplateAsync` checks code uniqueness via `GetAsync` then writes. In multi-instance deployment, two instances could simultaneously pass the check and both create templates with the same code.~~

**Fixed**: Code index key is now claimed atomically using `GetWithETagAsync` + `TrySaveAsync` before saving the template. If the claim fails (another instance created between read and write), returns `Conflict`.

### 10. QUALITY TENETS (T10): Missing/Wrong-Level Operation Entry Logging

**File**: `plugins/lib-item/ItemService.cs`

Only 2 of 13 endpoint methods have entry logs, and those are at `Information` level instead of `Debug`. The remaining 11 methods lack entry logs entirely.

**Fix**: Add `_logger.LogDebug(...)` entry logging to all endpoint methods.

### 11. QUALITY TENETS (T16): Duplicate Assembly Attributes

**File**: `plugins/lib-item/ItemService.cs` lines 13-14; `plugins/lib-item/AssemblyInfo.cs` lines 5-6

Duplicate `[assembly: InternalsVisibleTo]` declarations.

**Fix**: Remove duplicates from `ItemService.cs`.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

None identified.

### Intentional Quirks (Documented Behavior)

1. **Template immutable fields**: Code, gameId, quantityModel, scope, and soulboundType are set at creation and cannot be changed. This prevents breaking existing instances that depend on these properties.

2. **Cache TTL asymmetry**: Templates cached for 1 hour (infrequent changes), instances for 15 minutes (active gameplay). Reflects different access patterns.

3. **Quantity flooring for Discrete**: When creating discrete instances, the quantity is `Math.Floor()`'d to the nearest integer. A request for 5.7 arrows creates 5.

4. **MaxInstancesPerQuery is a hard cap**: `ListItemsByContainer` enforces the limit as truncation, not pagination. If a container has 1001 items and the limit is 1000, the last item is silently excluded.

5. **Bind event enrichment fallback**: When binding an item, if the template cannot be loaded (data inconsistency), the event's `TemplateCode` field is set to `missing:{templateId}` rather than failing the operation.

6. **Optimistic concurrency doesn't fail requests**: If all retries for list operations (index updates) are exhausted, the operation logs a warning but the main create/destroy still succeeds. The index may be temporarily inconsistent.

7. **Config defaults applied at creation**: `DefaultRarity`, `DefaultMaxStackSize`, etc. are applied when the template doesn't specify values. Once created, these are stored on the template and don't change if config changes.

### Design Considerations (Requires Planning)

1. **List index N+1 loading**: `ListItemsByContainer` and `ListItemsByTemplate` load each instance individually from the state store. With large containers or popular templates, this generates many calls.

2. **No template deletion**: Templates can only be deprecated, never deleted. This preserves instance integrity but means the template store grows monotonically.

3. **JSON-stored complex fields**: Stats, effects, requirements, display, and metadata are stored as serialized JSON strings. No schema validation is performed on these fields - they're opaque to the item service.

4. **Container index not validated**: The item service trusts the `containerId` provided during creation. It does not validate that the container exists in the inventory service.

5. **No event consumption**: The item service is purely a publisher. It doesn't react to external events (e.g., container deletion). The inventory service is responsible for calling `DestroyItemInstance` when needed.

6. **Update doesn't track changedFields**: Unlike other services that track which fields changed, `UpdateItemTemplateAsync` applies all provided changes without changedFields list in the event. Consumers can't tell which fields were actually modified.

7. **Destroy bypasses destroyable check with "admin" reason**: Line 680 - if `body.Reason == "admin"`, the template's `Destroyable` flag is ignored. This allows admin-level destruction of indestructible items.

8. **BatchGetItemInstances is sequential**: Lines 834-845 fetch each instance one by one in a foreach loop rather than parallel fetching. Could be slow for large batches.

9. **Empty container/template index not cleaned up**: After `RemoveFromListAsync`, if the list becomes empty, it remains as an empty JSON array `[]` in the store rather than being deleted.

10. **ListItemsByContainer doesn't support pagination**: Unlike `ListItemsByTemplate` which uses Offset/Limit from the request, `ListItemsByContainer` just returns up to `MaxInstancesPerQuery` items with no offset support. Large containers lose items silently.

11. **ListItemsByTemplate filters AFTER fetching all instances**: Lines 793-801 fetch all instances then filter by RealmId in memory. For templates with many instances, this fetches far more data than needed.

12. **Bind doesn't enforce SoulboundType**: `BindItemInstanceAsync` binds any item regardless of its template's `SoulboundType`. The soulbound type is metadata for game logic, not enforced by the service.

13. **Deprecate is idempotent (no conflict)**: Unlike other services that return Conflict if already deprecated, `DeprecateItemTemplateAsync` will re-deprecate with a new timestamp, overwriting the original deprecation timestamp.

14. **CreateInstance validates IsActive but not IsDeprecated**: Line 413 checks `!template.IsActive` but not `template.IsDeprecated`. A deprecated but still-active template can continue spawning new instances.
