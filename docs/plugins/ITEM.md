# Item Plugin Deep Dive

> **Plugin**: lib-item
> **Schema**: schemas/item-api.yaml
> **Version**: 1.0.0
> **State Stores**: item-template-store (MySQL), item-template-cache (Redis), item-instance-store (MySQL), item-instance-cache (Redis), item-lock (Redis)

---

## Overview

Dual-model item management (L2 GameFoundation) with templates (definitions/prototypes) and instances (individual occurrences). Templates define item properties (code, game scope, quantity model, stats, effects, rarity); instances represent actual items in the game world with quantity, durability, custom stats, and binding state. Supports multiple quantity models (discrete stacks, continuous weights, unique items). Designed to pair with lib-inventory for container placement management.

---

## Itemize Anything: Conceptual Model

The Item service implements the **"Itemize Anything"** pattern ([#280](https://github.com/beyond-immersion/bannou-service/issues/280)), enabling arbitrary game concepts to be stored and managed as items. This creates a unified abstraction for:

- Traditional items (weapons, armor, consumables)
- Licenses and skills ([#281](https://github.com/beyond-immersion/bannou-service/issues/281))
- Status effects and buffs ([#282](https://github.com/beyond-immersion/bannou-service/issues/282))
- Memberships and subscriptions ([#284](https://github.com/beyond-immersion/bannou-service/issues/284))
- Collectibles and achievements ([#286](https://github.com/beyond-immersion/bannou-service/issues/286))

### Items as Contract Wrappers

The key insight is that items can **delegate behavior to contracts**. An item template's `useBehaviorContractTemplateId` field points to a Contract template that defines what happens when the item is used:

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         Item → Contract Delegation                       │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│   ItemTemplate                           ContractTemplate                │
│   ┌─────────────────────────┐           ┌─────────────────────────┐     │
│   │ code: "quest_scroll"    │           │ code: "ITEM_USE_QUEST"  │     │
│   │ useBehaviorContract ────┼──────────→│ milestones:             │     │
│   │   TemplateId: "..."     │           │   - code: "use"         │     │
│   └─────────────────────────┘           │     onComplete:         │     │
│                                         │       - /quest/start    │     │
│   /item/use                             │       - /item/destroy   │     │
│       │                                 └─────────────────────────┘     │
│       ├── Create contract instance                                      │
│       ├── Complete "use" milestone ──→ Prebound APIs execute            │
│       └── Consume item on success                                       │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

This means the Item service doesn't need to know anything about quests, spells, buffs, or any other domain. It simply:
1. Creates a contract instance from the template
2. Completes the designated milestone
3. Lets the Contract service execute the prebound APIs

### Three Contract Binding Patterns

Items support three patterns for contract relationships:

| Pattern | When Contract Created | When Cleared | Example |
|---------|----------------------|--------------|---------|
| **Ephemeral** | On `/item/use` | Immediately after use | Quest scroll, healing potion |
| **Session** | On first `/item/use-step` | When all milestones complete | Multi-step crafting recipe |
| **Lifecycle** | At item creation (by orchestrator) | When contract terminates | Buff/debuff, license, subscription |

**Ephemeral contracts** (current `/item/use`):
```
User clicks "Use Potion" → Item service creates contract → Contract executes
→ Character healed → Item consumed → Contract disposed
```

**Session contracts** (future `/item/use-step`):
```
User starts crafting → Item gets contractInstanceId stored → User completes
step 1, 2, 3 → Final step → Item consumed → Contract completed
```

**Lifecycle contracts** (orchestrated by lib-status, lib-license, etc.):
```
Player poisoned → lib-status creates contract + item together → Contract
has 30s timer → Timer expires → Contract terminates → Item destroyed
```

### Orchestrator Pattern

Higher-layer services (L3/L4) act as **thin orchestration layers** that coordinate between Items and Contracts:

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    Orchestrator Pattern (lib-status example)             │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│   lib-status (L4 Orchestrator)                                          │
│       │                                                                  │
│       ├── 1. Create Contract instance (poison_debuff template)          │
│       │       ├── 30s duration milestone                                │
│       │       └── onComplete: /character/damage, /status/remove         │
│       │                                                                  │
│       ├── 2. Create Item instance (poison status item)                  │
│       │       ├── contractInstanceId = contract from step 1             │
│       │       ├── contractBindingType = lifecycle                       │
│       │       └── containerId = character's status inventory            │
│       │                                                                  │
│       └── 3. React to contract.terminated events                        │
│               └── Destroy the bound item                                │
│                                                                          │
│   Item Service (L2) - Stores the item, knows nothing about poison       │
│   Contract Service (L1) - Manages lifecycle, executes prebound APIs     │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

The Item service provides storage and the `/item/use` execution path. It doesn't interpret what items "mean" - that's the orchestrator's job.

### Why This Matters

This architecture enables:

1. **Unified queries**: "Does character have the poison debuff?" → Query status inventory for item with code `poison_tier_1`
2. **Unified serialization**: Save/load systems just persist items - the contract handles behavior
3. **Extensibility**: New item behaviors = new contract templates, not new code
4. **Clean separation**: Item service (L2) knows storage; orchestrators (L4) know domain semantics

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | MySQL persistence + Redis caching for templates and instances |
| lib-messaging (`IMessageBus`) | Publishing item lifecycle events; error event publishing |
| lib-contract (`IContractClient`) | Execute item use behaviors via contract prebound APIs |

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
| `item-instance.unbound` | `ItemInstanceUnboundEvent` | Instance binding removed |
| `item.used` | `ItemUsedEvent` | Batched item use successes (deduped by templateId+userId) |
| `item.use-failed` | `ItemUseFailedEvent` | Batched item use failures (deduped by templateId+userId) |

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
| `UseEventDeduplicationWindowSeconds` | `ITEM_USE_EVENT_DEDUPLICATION_WINDOW_SECONDS` | `60` | Deduplication window for batched use events |
| `UseEventBatchMaxSize` | `ITEM_USE_EVENT_BATCH_MAX_SIZE` | `100` | Max records per batched use event |
| `UseMilestoneCode` | `ITEM_USE_MILESTONE_CODE` | `use` | Contract milestone code to complete on item use |
| `SystemPartyId` | `ITEM_SYSTEM_PARTY_ID` | *(computed)* | System party ID for use contracts (null = derive from gameId) |
| `SystemPartyType` | `ITEM_SYSTEM_PARTY_TYPE` | `system` | Entity type for system party in use contracts |
| `CanUseMilestoneCode` | `ITEM_CAN_USE_MILESTONE_CODE` | `validate` | Milestone code for CanUse validation contracts |
| `OnUseFailedMilestoneCode` | `ITEM_ON_USE_FAILED_MILESTONE_CODE` | `handle_failure` | Milestone code for OnUseFailed handler contracts |
| `UseStepLockTimeoutSeconds` | `ITEM_USE_STEP_LOCK_TIMEOUT_SECONDS` | `30` | Distributed lock timeout for UseItemStep operations |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<ItemService>` | Scoped | Structured logging |
| `ItemServiceConfiguration` | Singleton | All 18 config properties |
| `IStateStoreFactory` | Singleton | Access to 5 state stores (4 data + 1 lock) |
| `IMessageBus` | Scoped | Event publishing and error events |
| `IDistributedLockProvider` | Scoped | Distributed locks for container change operations |
| `IContractClient` | Scoped | Contract service for item use behavior execution (L1 hard dependency) |

Service lifetime is **Scoped** (per-request). No background services.

---

## API Endpoints (Implementation Notes)

### Template Operations (5 endpoints)

- **CreateItemTemplate** (`/item/template/create`): Validates code uniqueness per game via code index. Applies config defaults for rarity, weight precision, max stack size, soulbound type. Immutable fields set at creation: code, gameId, quantityModel, scope. Populates template cache after save. Updates game index and code index (optimistic concurrency with retries). Publishes `item-template.created`.
- **GetItemTemplate** (`/item/template/get`): Dual lookup via `ResolveTemplateAsync`: by templateId (direct) or by code+gameId (index lookup). Uses `GetTemplateWithCacheAsync` (cache → persistent store → populate cache).
- **ListItemTemplates** (`/item/template/list`): Loads game index, fetches each template. Filters: category, subcategory, tags, rarity, scope, realm, active status, search (name/description). Pagination via offset/limit.
- **UpdateItemTemplate** (`/item/template/update`): Updates mutable fields only. Invalidates template cache after save. Publishes `item-template.updated`.
- **DeprecateItemTemplate** (`/item/template/deprecate`): Marks template inactive. Optional `migrationTargetId` for upgrade paths. Existing instances remain valid. Invalidates cache. Publishes `item-template.deprecated`.

### Instance Operations (6 endpoints)

- **CreateItemInstance** (`/item/instance/create`): Validates template exists and is active (but not IsDeprecated - see quirk #9). Quantity enforcement: Unique→1, Discrete→floor(value) capped at MaxStackSize, Continuous→as-is. Populates instance cache. Updates container index and template index (optimistic concurrency). Publishes `item-instance.created`.
- **GetItemInstance** (`/item/instance/get`): Cache read-through pattern. Returns instance with template reference.
- **ModifyItemInstance** (`/item/instance/modify`): Updates durability (delta), quantityDelta, customStats, customName, instanceMetadata, container/slot position. Container changes use distributed lock via `item-lock` store to prevent race conditions on index updates. Non-container changes skip locking. Invalidates instance cache. Publishes `item-instance.modified`.
- **BindItemInstance** (`/item/instance/bind`): Binds instance to character ID. Checks `BindingAllowAdminOverride` for rebinding. Enriches event with template code (fallback: `missing:{templateId}` if template not found). Publishes `item-instance.bound`.
- **DestroyItemInstance** (`/item/instance/destroy`): Validates template's `Destroyable` flag unless reason="admin". Removes from container and template indexes. Invalidates cache. Publishes `item-instance.destroyed`.
- **UseItem** (`/item/use`): Executes item behavior via Contract service delegation. See detailed flow below.

### UseItem Execution Flow (Detailed)

The `/item/use` endpoint implements the **ephemeral contract pattern**:

```
UseItemAsync(instanceId, userId, userType, targetId?, targetType?, context?)
    │
    ├── 1. Load instance from cache/store
    │       └── 404 if not found
    │
    ├── 2. Load template from cache/store
    │       └── 500 if template missing (data inconsistency)
    │
    ├── 3. Validate template.useBehaviorContractTemplateId
    │       └── 400 if null (item not usable)
    │
    ├── 4. Compute system party ID
    │       ├── If ITEM_SYSTEM_PARTY_ID configured → use it
    │       └── Else → SHA256(game ID) → deterministic UUID v5
    │
    ├── 5. Create contract instance
    │       ├── Two parties: user (from request) + system (computed)
    │       ├── gameMetadata populated with:
    │       │   ├── itemInstanceId, itemTemplateId
    │       │   ├── userId, userType
    │       │   ├── targetId, targetType (if provided)
    │       │   └── merged context dict (for template value substitution)
    │       └── 400 if contract creation fails
    │
    ├── 6. Complete "use" milestone (code from ITEM_USE_MILESTONE_CODE)
    │       ├── Contract service executes onComplete prebound APIs
    │       │   ├── Template values substituted: {{contract.party.user.entityId}}, etc.
    │       │   └── APIs execute in batches (default 10 concurrent per batch)
    │       └── 400 if milestone fails
    │
    ├── 7. Consume item (on success only)
    │       ├── Quantity > 1 → Decrement by 1, publish item-instance.modified
    │       └── Quantity ≤ 1 → Destroy instance, publish item-instance.destroyed
    │
    ├── 8. Record for batched event publishing
    │       ├── Key: {templateId}:{userId}
    │       ├── Window: ITEM_USE_EVENT_DEDUPLICATION_WINDOW_SECONDS (60s)
    │       └── Batch size: ITEM_USE_EVENT_BATCH_MAX_SIZE (100)
    │
    └── 9. Return UseItemResponse
            ├── success: true/false
            ├── contractInstanceId: the ephemeral contract
            ├── consumed: whether item was consumed
            ├── remainingQuantity: null if destroyed, else new quantity
            └── failureReason: if success=false
```

**Key Design Points**:

1. **Deterministic system party**: The system party ID is derived from the game ID via SHA-256, ensuring the same game always gets the same system party across all instances. This enables contract templates to reference `{{contract.party.system.entityId}}` consistently.

2. **Ephemeral contract**: The contract instance is created and completed in a single request. The `contractInstanceId` in the response is informational - the contract is already complete.

3. **Batched events**: High-frequency item use (e.g., rapid potion drinking) doesn't flood the event bus. Events are deduplicated by user+template and published in batches.

4. **Context passthrough**: The `context` dict in the request is merged into `gameMetadata`, allowing callers to pass arbitrary data for template value substitution (e.g., `{{contract.gameMetadata.instanceData.targetLocation}}`).

**Related Configuration**:
- `ITEM_USE_MILESTONE_CODE`: Milestone to complete (default: "use")
- `ITEM_SYSTEM_PARTY_ID`: Override deterministic system party (default: computed)
- `ITEM_SYSTEM_PARTY_TYPE`: Entity type for system party (default: "system")

### Query Operations (3 endpoints)

- **ListItemsByContainer** (`/item/instance/list-by-container`): Loads container index, fetches each instance. Enforces `MaxInstancesPerQuery` as hard limit. Response includes `totalCount` (actual item count) and `wasTruncated` (true if capped).
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


Contract Binding Patterns
===========================

  EPHEMERAL (current /item/use):
  ┌─────────────────────────────────────────────────────────┐
  │  /item/use                                              │
  │      │                                                  │
  │      ├── Create contract instance                       │
  │      ├── Complete milestone ──→ Prebound APIs           │
  │      ├── Consume item                                   │
  │      └── Contract disposed (ephemeral)                  │
  │                                                         │
  │  contractInstanceId: NOT stored on item                 │
  │  Use case: Consumables, one-shot effects                │
  └─────────────────────────────────────────────────────────┘

  SESSION (future /item/use-step):
  ┌─────────────────────────────────────────────────────────┐
  │  /item/use-step (step 1)                                │
  │      ├── Create contract, store on item                 │
  │      └── Complete milestone 1                           │
  │                                                         │
  │  /item/use-step (step 2)                                │
  │      └── Complete milestone 2                           │
  │                                                         │
  │  /item/use-step (step N - final)                        │
  │      ├── Complete final milestone                       │
  │      ├── Consume item                                   │
  │      └── Clear contractInstanceId                       │
  │                                                         │
  │  contractInstanceId: stored during session              │
  │  contractBindingType: "session"                         │
  │  Use case: Multi-step crafting, ritual spells           │
  └─────────────────────────────────────────────────────────┘

  LIFECYCLE (orchestrator-managed):
  ┌─────────────────────────────────────────────────────────┐
  │  lib-status (orchestrator):                             │
  │      ├── Create contract (30s poison timer)             │
  │      └── Create item with contractInstanceId            │
  │                                                         │
  │  Contract executes (ticks, duration):                   │
  │      └── Prebound APIs: damage per tick                 │
  │                                                         │
  │  Contract expires:                                      │
  │      ├── Orchestrator receives contract.terminated      │
  │      └── Orchestrator destroys item                     │
  │                                                         │
  │  contractInstanceId: stored at creation                 │
  │  contractBindingType: "lifecycle"                       │
  │  Use case: Buffs/debuffs, licenses, subscriptions       │
  └─────────────────────────────────────────────────────────┘
```

---

## Stubs & Unimplemented Features

1. ~~**Unbound event not implemented**~~: **FIXED** (2026-01-31) - Added `/item/instance/unbind` endpoint with admin-only permission. Clears `BoundToId` and `BoundAt`, publishes `ItemInstanceUnboundEvent` with reason and previous character ID. Returns BadRequest if item is not bound.
2. **Deprecation without cascade**: Deprecating a template doesn't automatically migrate, disable, or destroy existing instances. Admin must manage instances separately.

---

## Potential Extensions

1. ~~**Unbind endpoint**~~: **FIXED** (2026-01-31) - Implemented as `/item/instance/unbind` with admin-only permission.
2. **Template migration**: When deprecating with `migrationTargetId`, automatically upgrade instances to the new template.
3. **Affix system**: Random or crafted modifiers applied to instances (prefixes/suffixes).
4. **Durability repair**: Endpoint to restore durability with configurable repair costs.

---

## Known Quirks & Caveats

### Bugs

No bugs identified.

### Intentional Quirks

1. **Quantity flooring for Discrete**: When creating discrete instances, the quantity is `Math.Floor()`'d to the nearest integer. A request for 5.7 arrows creates 5.

2. ~~**MaxInstancesPerQuery is a hard cap**~~: **FIXED** (2026-02-08) - `ListItemsByContainer` now reports `totalCount` as the actual item count and sets `wasTruncated = true` when the response is capped by `MaxInstancesPerQuery`. See [#310](https://github.com/beyond-immersion/bannou-service/issues/310).

3. **Bind event enrichment fallback**: When binding an item, if the template cannot be loaded (data inconsistency), the event's `TemplateCode` field is set to `missing:{templateId}` rather than failing the operation.

4. **Optimistic concurrency doesn't fail requests**: If all retries for list operations (index updates) are exhausted, the operation logs a warning but the main create/destroy still succeeds. The index may be temporarily inconsistent.

5. **Update doesn't track changedFields**: Unlike other services that track which fields changed, `UpdateItemTemplateAsync` applies all provided changes without changedFields list in the event. Consumers can't tell which fields were actually modified.

6. **ListItemsByContainer doesn't support pagination**: Unlike `ListItemsByTemplate` which uses Offset/Limit from the request, `ListItemsByContainer` just returns up to `MaxInstancesPerQuery` items with no offset support. The `wasTruncated` flag signals when items are capped, but callers cannot page through them.

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

- **2026-02-08**: Fixed `ListItemsByContainer` silent truncation ([#310](https://github.com/beyond-immersion/bannou-service/issues/310)). Added `wasTruncated` boolean to `ListItemsResponse` schema. `totalCount` now reports actual item count (not truncated count). Callers can detect when results are capped by `MaxInstancesPerQuery`.

- **2026-02-07**: Itemize Anything Extensions ([#330](https://github.com/beyond-immersion/bannou-service/issues/330)). Added CanUse validation (`canUseBehaviorContractTemplateId`, `canUseBehavior`), OnUseFailed handler (`onUseFailedBehaviorContractTemplateId`), multi-step workflows (`/item/use-step` endpoint with session contract bindings), and per-template use behavior configuration (`ItemUseBehavior` enum: disabled, destroy_on_success, destroy_always). New events: `item.use-step-completed`, `item.use-step-failed`. Three new config properties: `CanUseMilestoneCode`, `OnUseFailedMilestoneCode`, `UseStepLockTimeoutSeconds`.

- **2026-02-07**: Itemize Anything - Items as Executable Contracts ([#280](https://github.com/beyond-immersion/bannou-service/issues/280)). Added `useBehaviorContractTemplateId` to ItemTemplate for contract-delegated item behavior. New `/item/use` endpoint creates two-party contracts (user + deterministic system party), completes the "use" milestone triggering prebound APIs, and consumes items on success. Events are batched with deduplication window (configurable, default 60s) by templateId+userId key. Added IContractClient as L1 hard dependency per SERVICE-HIERARCHY. Five new config properties for use behavior tuning.

- **2026-01-31**: Unbind endpoint implementation - Added `/item/instance/unbind` endpoint with `UnbindItemInstanceAsync` method. Admin-only permission, clears binding state, and publishes `ItemInstanceUnboundEvent` with reason and previous character ID. Returns BadRequest (400) if item is not bound. Schema, generated code, and service implementation all updated.

- **2026-01-31**: N+1 bulk loading optimization - Added `GetInstancesBulkWithCacheAsync` helper that performs two-tier bulk loading (Redis cache → MySQL persistent store for misses → bulk cache population). Applied to `ListItemsByContainer`, `ListItemsByTemplate`, and `BatchGetItemInstances`. Maximum 2 database round-trips regardless of item count. See Issue #168 for `IStateStore` bulk operations.

### Related (Cross-Service)
- **[#164](https://github.com/beyond-immersion/bannou-service/issues/164)**: Item Removal/Drop Behavior - Owned by lib-inventory, but affects lib-item's container index and event patterns. See Design Considerations #4 and #5.
