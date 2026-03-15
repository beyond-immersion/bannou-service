# Item Plugin Deep Dive

> **Plugin**: lib-item
> **Schema**: schemas/item-api.yaml
> **Version**: 1.0.0
> **Layer**: GameFoundation
> **State Store**: item-template-store (MySQL), item-template-cache (Redis), item-instance-store (MySQL), item-instance-cache (Redis), item-lock (Redis)
> **Implementation Map**: [docs/maps/ITEM.md](../maps/ITEM.md)
> **Short**: Dual-model items -- templates (definitions) and instances (occurrences) with quantity models and binding

---

## Overview

Dual-model item management (L2 GameFoundation) with templates (definitions/prototypes) and instances (individual occurrences). Templates define item properties (code, game scope, quantity model, stats, effects, rarity); instances represent actual items in the game world with quantity, durability, custom stats, and binding state. Supports multiple quantity models (discrete stacks, continuous weights, unique items). Designed to pair with lib-inventory for container placement management.

---

## Itemize Anything: Conceptual Model

The Item service implements the **"Itemize Anything"** pattern ([#280](https://github.com/beyond-immersion/bannou-service/issues/280)), enabling arbitrary game concepts to be stored and managed as items. This creates a unified abstraction for:

- Traditional items (weapons, armor, consumables)
- Licenses and skills ([#281](https://github.com/beyond-immersion/bannou-service/issues/281))
- Status effects and buffs ([#282](https://github.com/beyond-immersion/bannou-service/issues/282)) — lib-status implemented
- Memberships and subscriptions ([#284](https://github.com/beyond-immersion/bannou-service/issues/284))
- Collectibles and achievements ([#286](https://github.com/beyond-immersion/bannou-service/issues/286))

### Items as Contract Wrappers

The key insight is that items can **delegate behavior to contracts**. An item template's `useBehaviorContractTemplateId` field points to a Contract template that defines what happens when the item is used:

```
┌─────────────────────────────────────────────────────────────────────────┐
│ Item → Contract Delegation │
├─────────────────────────────────────────────────────────────────────────┤
│ │
│ ItemTemplate ContractTemplate │
│ ┌─────────────────────────┐ ┌─────────────────────────┐ │
│ │ code: "quest_scroll" │ │ code: "ITEM_USE_QUEST" │ │
│ │ useBehaviorContract ────┼──────────→│ milestones: │ │
│ │ TemplateId: "..." │ │ - code: "use" │ │
│ └─────────────────────────┘ │ onComplete: │ │
│ │ - /quest/start │ │
│ /item/use │ - /item/destroy │ │
│ │ └─────────────────────────┘ │
│ ├── Create contract instance │
│ ├── Complete "use" milestone ──→ Prebound APIs execute │
│ └── Consume item on success │
│ │
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
| **Session** | On first `/item/use-step` (implemented) | When all milestones complete | Multi-step crafting recipe |
| **Lifecycle** | At item creation (by orchestrator) | When contract terminates | Buff/debuff, license, subscription |

**Ephemeral contracts** (current `/item/use`):
```
User clicks "Use Potion" → Item service creates contract → Contract executes
→ Character healed → Item consumed → Contract disposed
```

**Session contracts** (`/item/use-step`):
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
│ Orchestrator Pattern (lib-status example) │
├─────────────────────────────────────────────────────────────────────────┤
│ │
│ lib-status (L4 Orchestrator) │
│ │ │
│ ├── 1. Create Contract instance (poison_debuff template) │
│ │ ├── 30s duration milestone │
│ │ └── onComplete: /character/damage, /status/remove │
│ │ │
│ ├── 2. Create Item instance (poison status item) │
│ │ ├── contractInstanceId = contract from step 1 │
│ │ ├── contractBindingType = lifecycle │
│ │ └── containerId = character's status inventory │
│ │ │
│ └── 3. React to contract.terminated events │
│ └── Destroy the bound item │
│ │
│ Item Service (L2) - Stores the item, knows nothing about poison │
│ Contract Service (L1) - Manages lifecycle, executes prebound APIs │
│ │
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

## DI Listener Dispatch: IItemInstanceDestructionListener

Item is the **dispatcher** side of the `IItemInstanceDestructionListener` DI Listener pattern (defined in `bannou-service/Providers/`). When `DestroyItemInstanceAsync` destroys an instance, it must call all registered `IItemInstanceDestructionListener` implementations to notify L4 services that own per-item data (e.g., lib-affix owns modifier instances keyed by itemInstanceId).

This follows the **High-Frequency Instance Lifecycle Exception** to (FOUNDATION TENETS): item instances are created and destroyed at loot/combat/trading frequency across 100K NPCs. Using lib-resource for per-instance cleanup would be prohibitively expensive. Using event subscriptions would violate (no subscribing to `*.deleted` for dependent data cleanup). The DI Listener pattern provides in-process, zero-overhead notification.

```
DestroyItemInstanceAsync
 │
 ├── 1. Validate instance exists, check Destroyable flag
 ├── 2. Remove from container/template indexes
 ├── 3. Delete from store, invalidate cache
 ├── 4. Publish item.instance.destroyed event (broadcast)
 └── 5. Dispatch to IItemInstanceDestructionListener implementations
 ├── lib-affix: deletes affix instances from own state store
 ├── (future): lib-socket, lib-enchantment, etc.
 └── Graceful degradation: log warning on listener failure, don't fail the destroy
```

**Implementation requirements:**
- Discover listeners via `IEnumerable<IItemInstanceDestructionListener>` constructor injection
- Dispatch AFTER the destroy succeeds and event is published (listeners are optimization, not rollback)
- Each listener failure is isolated — one failing doesn't prevent others or fail the destroy
- Listeners must write to distributed state (Redis/MySQL) for multi-node consistency per SERVICE-HIERARCHY distributed safety rules

**Current status:** The interface is architecturally specified but not yet implemented in code. Neither `IItemInstanceDestructionListener` in `bannou-service/Providers/` nor the dispatch logic in `DestroyItemInstanceAsync` exist yet.
<!-- AUDIT:NEEDS_IMPLEMENTATION:2026-03-04:https://github.com/beyond-immersion/bannou-service/issues/490 -->

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-inventory | Uses `IItemClient` for template lookups, instance creation/modification/destruction |
| lib-collection | Uses `IItemClient` for entry instances (follows "items in inventories" pattern) |
| lib-quest | Uses `IItemClient` for ITEM_OWNED prerequisite checks and reward granting |
| lib-license | Uses item instances as license nodes on progression boards ([#281](https://github.com/beyond-immersion/bannou-service/issues/281)) |
| lib-status | Uses item instances as status effects in per-entity containers ([#282](https://github.com/beyond-immersion/bannou-service/issues/282)) |
| lib-escrow | Planned: item-backed exchanges ([#153](https://github.com/beyond-immersion/bannou-service/issues/153)), `IItemClient` not yet integrated |
| lib-affix | Planned (L4): per-item modifier data via `IItemInstanceDestructionListener` for cleanup — plugin not yet created |

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
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

## Visual Aid

```
Dual-Model Architecture
=========================

 ItemTemplate (Definition) ItemInstance (Occurrence)
 ┌──────────────────────────┐ ┌──────────────────────────┐
 │ TemplateId (immutable) │ ┌───→│ InstanceId │
 │ Code (immutable) │ │ │ TemplateId ──────────────┘
 │ GameId (immutable) │ │ │ ContainerId │
 │ QuantityModel (immutable)│ │ │ RealmId │
 │ Scope (immutable) │ │ │ Quantity │
 │ SoulboundType (immutable)│ │ │ SlotIndex / SlotX,Y │
 ├──────────────────────────┤ │ │ Rotated │
 │ Name (mutable) │ │ │ CurrentDurability │
 │ Description │ │ │ BoundToId (character) │
 │ Category, Subcategory │ │ │ CustomStats (JSON) │
 │ Tags, Rarity │ │ │ CustomName │
 │ Weight, Volume │ │ │ OriginType (loot/craft/…)│
 │ GridWidth/Height │ │ │ OriginId │
 │ MaxStackSize │ │ └──────────────────────────┘
 │ Stats, Effects (JSON) │ │
 │ Requirements (JSON) │ │ One template → Many instances
 │ Display (JSON) │ │
 └──────────────────────────┘ │
 │ │
 └───────────────────────┘


Cache Read-Through Pattern
============================

 GetTemplateWithCacheAsync(templateId)
 │
 ├── Try Redis cache (item-template-cache)
 │ └── Hit? → Return cached model
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
 │ ├── GetWithETagAsync(key) → (json, etag)
 │ ├── Deserialize → list, Add(value)
 │ ├── TrySaveAsync(key, serialized, etag)
 │ │ ├── Success (etag returned) → done
 │ │ └── Conflict (null) → retry
 │ └── Next attempt
 │
 └── All retries exhausted → log warning (operation succeeds anyway)


Soulbound Types
=================

 none → Item freely tradeable
 on_pickup → Binds when first acquired (instance creation)
 on_equip → Binds when equipped (external trigger)
 on_use → Binds when consumed/used (external trigger)


Contract Binding Patterns
===========================

 EPHEMERAL (current /item/use):
 ┌─────────────────────────────────────────────────────────┐
 │ /item/use │
 │ │ │
 │ ├── Create contract instance │
 │ ├── Complete milestone ──→ Prebound APIs │
 │ ├── Consume item │
 │ └── Contract disposed (ephemeral) │
 │ │
 │ contractInstanceId: NOT stored on item │
 │ Use case: Consumables, one-shot effects │
 └─────────────────────────────────────────────────────────┘

 SESSION (/item/use-step):
 ┌─────────────────────────────────────────────────────────┐
 │ /item/use-step (step 1) │
 │ ├── Create contract, store on item │
 │ └── Complete milestone 1 │
 │ │
 │ /item/use-step (step 2) │
 │ └── Complete milestone 2 │
 │ │
 │ /item/use-step (step N - final) │
 │ ├── Complete final milestone │
 │ ├── Consume item │
 │ └── Clear contractInstanceId │
 │ │
 │ contractInstanceId: stored during session │
 │ contractBindingType: "session" │
 │ Use case: Multi-step crafting, ritual spells │
 └─────────────────────────────────────────────────────────┘

 LIFECYCLE (orchestrator-managed):
 ┌─────────────────────────────────────────────────────────┐
 │ lib-status (orchestrator): │
 │ ├── Create contract (30s poison timer) │
 │ └── Create item with contractInstanceId │
 │ │
 │ Contract executes (ticks, duration): │
 │ └── Prebound APIs: damage per tick │
 │ │
 │ Contract expires: │
 │ ├── Orchestrator receives contract.terminated │
 │ └── Orchestrator destroys item │
 │ │
 │ contractInstanceId: stored at creation │
 │ contractBindingType: "lifecycle" │
 │ Use case: Buffs/debuffs, licenses, subscriptions │
 └─────────────────────────────────────────────────────────┘
```

---

## Stubs & Unimplemented Features

1. **Deprecation without cascade**: Deprecating a template doesn't automatically migrate, disable, or destroy existing instances. Admin must manage instances separately. Related: [#489](https://github.com/beyond-immersion/bannou-service/issues/489) (template migration) is a sub-question of this design.
<!-- AUDIT:NEEDS_DESIGN:2026-02-25:https://github.com/beyond-immersion/bannou-service/issues/486 -->
2. **`IItemInstanceDestructionListener` dispatch**: `DestroyItemInstanceAsync` does not yet dispatch to registered listeners for L4 per-item data cleanup. The interface and dispatch logic need to be implemented. See DI Listener Dispatch section above.
<!-- AUDIT:NEEDS_IMPLEMENTATION:2026-03-04:https://github.com/beyond-immersion/bannou-service/issues/490 -->

---

## Potential Extensions

1. **Template migration** ([#489](https://github.com/beyond-immersion/bannou-service/issues/489)): When deprecating with `migrationTargetId`, automatically upgrade instances to the new template. This is a sub-question of [#486](https://github.com/beyond-immersion/bannou-service/issues/486) (deprecation cascade) — the two issues share open questions about quantity model mismatches, contract-bound instances, and sync vs async execution.
<!-- AUDIT:NEEDS_DESIGN:2026-02-25:https://github.com/beyond-immersion/bannou-service/issues/489 -->
2. **Affix system**: Random or crafted modifiers applied to instances (prefixes/suffixes). See lib-affix for the L4 modifier service.
<!-- AUDIT:NEEDS_DESIGN:2026-02-25:https://github.com/beyond-immersion/bannou-service/issues/490 -->
3. **Durability repair**: Endpoint to restore durability with configurable repair costs.
<!-- AUDIT:NEEDS_DESIGN:2026-02-25:https://github.com/beyond-immersion/bannou-service/issues/491 -->
4. **Item Decay/Expiration** ([#407](https://github.com/beyond-immersion/bannou-service/issues/407)): Time-based item lifecycle (template-level decay config, instance `expiresAt`, background worker for expiration). Dependency for lib-status ([#417](https://github.com/beyond-immersion/bannou-service/issues/417)) — native item expiration would allow simple timed buffs without full Contract lifecycle overhead.
<!-- AUDIT:NEEDS_DESIGN:2026-02-25:https://github.com/beyond-immersion/bannou-service/issues/407 -->
5. **Item Sockets** ([#430](https://github.com/beyond-immersion/bannou-service/issues/430)): Future L4 plugin (lib-socket) for socket, linking, and gem placement systems on item instances.
<!-- AUDIT:NEEDS_DESIGN:2026-02-25:https://github.com/beyond-immersion/bannou-service/issues/430 -->
6. **Batch item destruction** ([#559](https://github.com/beyond-immersion/bannou-service/issues/559)): No batch destroy endpoint exists. For high-frequency scenarios (lib-resource CASCADE cleanup, inventory wipe, character death) calling destroy per-item is expensive. Given that `IItemInstanceDestructionListener` acknowledges item destruction is high-frequency across 100K NPCs, batch support in the destroy path should be considered.
<!-- AUDIT:NEEDS_DESIGN:2026-03-04:https://github.com/beyond-immersion/bannou-service/issues/559 -->

---

## Type Field Classification

| Field | Category | Type | Rationale |
|-------|----------|------|-----------|
| `userType` (UseItem) | A (Entity Reference) | `EntityType` enum | Entity using the item. All valid values are first-class Bannou entities. |
| `targetType` (UseItem) | A (Entity Reference) | `EntityType` enum (nullable) | Target entity for item use. All valid values are first-class Bannou entities when present. |
| `code` (template) | B (Content Code) | Opaque string | Game-configurable item template identifier, unique within game service. Extensible without schema changes (e.g., `iron_sword`, `health_potion`, `quest_scroll`). |
| `category` | C (System State) | `ItemCategory` enum | Finite item classification: `weapon`, `armor`, `consumable`, `material`, `quest`, `currency`, `container`, `decoration`, `tool`, `mount`, `pet`, `recipe`, `key`, `misc`. |
| `quantityModel` | C (System State) | `QuantityModel` enum | Finite system-owned tracking modes: `discrete` (stackable integers), `continuous` (decimal weights), `unique` (quantity forced to 1). Immutable after creation. |
| `rarity` | C (System State) | `ItemRarity` enum | Finite rarity tiers: `common`, `uncommon`, `rare`, `epic`, `legendary`. |
| `soulboundType` | C (System State) | `SoulboundType` enum | Finite binding modes: `none`, `on_pickup`, `on_equip`, `on_use`. |
| `scope` | C (System State) | `ItemScope` enum | Finite realm availability modes: `global`, `realm_specific`. Consistent with `CurrencyScope`. |
| `originType` | C (System State) | `ItemOriginType` enum | Finite creation source classification: `loot`, `quest`, `craft`, `vendor`, `trade`, `gift`, `admin`, `system`, `migration`, `other`. |
| `useBehavior` | C (System State) | `ItemUseBehavior` enum | Finite use consumption modes: `disabled`, `destroy_on_success`, `destroy_always`. |
| `reason` (UnbindEvent) | C (System State) | `UnbindReason` enum | Finite unbinding modes for admin/system operations. |

---

## Known Quirks & Caveats

### Bugs

1. ~~**Bind/unbind event `templateCode` uses sentinel value**~~: **FIXED** (2026-03-15) - `templateCode` is now nullable on both `ItemInstanceBoundEvent` and `ItemInstanceUnboundEvent` schemas. When the template cannot be loaded (data inconsistency), `TemplateCode` is set to `null` instead of the sentinel string `missing:{templateId}`. Per Implementation Tenets (No Sentinel Values).

### Intentional Quirks

1. **Quantity flooring for Discrete**: When creating discrete instances, the quantity is `Math.Floor()`'d to the nearest integer. A request for 5.7 arrows creates 5.

2. **Optimistic concurrency doesn't fail requests**: If all retries for list operations (index updates) are exhausted, the operation logs a warning but the main create/destroy still succeeds. The index may be temporarily inconsistent.

3. **Bind doesn't enforce SoulboundType**: `BindItemInstanceAsync` binds any item regardless of its template's `SoulboundType`. The soulbound type is metadata for game logic, not enforced by the service.

4. **No template deletion**: Templates can only be deprecated, never deleted. This is standard Category B behavior per IMPLEMENTATION TENETS deprecation lifecycle — templates whose instances outlive them are terminal-deprecation-only. Preserves instance referential integrity; use deprecation with `migrationTargetId` for upgrade paths.

5. **JSON-stored complex fields are opaque pass-through**: Stats, effects, requirements, display, and metadata on templates (plus customStats and instanceMetadata on instances) are stored as serialized JSON strings with no schema validation. This is intentional compliance — all schema descriptions explicitly state "Opaque to Bannou; no plugin reads keys by convention." These are client-side display data and game-specific implementation data per tenets's two legitimate uses.

6. **Container index not validated**: The item service trusts the `containerId` provided during creation without validating that the container exists in the inventory service. This is intentional: Item is a storage primitive and callers (Inventory, Collection, Status, License) are responsible for validating container references before calling Item's API. Adding `IInventoryClient` validation would create a circular L2 dependency (Inventory→Item and Item→Inventory). Related: [#164](https://github.com/beyond-immersion/bannou-service/issues/164) discusses making items temporarily "containerless" during drop operations.

7. **No event consumption**: The item service is purely a publisher (`x-event-subscriptions: []`). It doesn't react to external events (e.g., container deletion). Same-layer services (Inventory, Collection, Status, License) call Item's API directly per FOUNDATION TENETS (— same-layer direct API calls, not events). Cleanup coordination goes through lib-resource per FOUNDATION TENETS. Related: [#164](https://github.com/beyond-immersion/bannou-service/issues/164) explores event-driven drop handling as one design option.

8. **Admin override for indestructible items**: `DestroyItemInstanceAsync` bypasses the template's `Destroyable` flag when `body.Reason == DestroyReason.Admin`. This is an intentional admin safety valve — `DestroyReason.Admin` is a schema-defined enum value. Admins need the ability to remove any item regardless of template constraints (e.g., bugged indestructible items, account cleanup, data migration).

### Design Considerations

1. **Warning: `instanceMetadata` is opaque pass-through** ([#308](https://github.com/beyond-immersion/bannou-service/issues/308)): The `instanceMetadata` field on item instances uses `additionalProperties: true` and is opaque to Bannou. No plugin reads specific keys from this field by convention — verified by code audit (2026-02-26). lib-affix stores modifier data in its own state store per tenets (see AFFIX.md). The systemic `additionalProperties: true` pattern is tracked in #308 for potential migration to typed schemas. New code MUST NOT introduce convention-based metadata key reading.
<!-- AUDIT:NEEDS_DESIGN:2026-02-26:https://github.com/beyond-immersion/bannou-service/issues/308 -->

---

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above. Items here are managed by the `/audit-plugin` workflow.

### Active
- **`IItemInstanceDestructionListener` dispatch** (2026-03-04): Architecturally specified in SERVICE-HIERARCHY.md and AFFIX.md but not yet implemented. Added to Stubs section and DI Listener Dispatch section. Tracked via [#490](https://github.com/beyond-immersion/bannou-service/issues/490).
- **Batch item destruction** (2026-03-04): No batch destroy endpoint for high-frequency scenarios. Tracked via [#559](https://github.com/beyond-immersion/bannou-service/issues/559). See Potential Extensions #6.

### Completed
- **Batch lifecycle events for ItemInstance** (2026-03-15): Switched ItemInstance x-lifecycle to `batch: true`. Individual lifecycle events replaced with `ItemInstanceBatchCreatedEvent`, `BatchModifiedEvent`, `BatchDestroyedEvent`. Uses shared `EventBatcher<T>`/`DeduplicatingEventBatcher<K,T>` helpers. See `docs/planning/BATCH-LIFECYCLE-EVENTS.md`.

### Related (Cross-Service)
- **[#153](https://github.com/beyond-immersion/bannou-service/issues/153)**: Escrow Asset Transfer Integration Broken - Affects lib-escrow's ability to use `IItemClient` for item-backed exchanges.
- **[#164](https://github.com/beyond-immersion/bannou-service/issues/164)**: Item Removal/Drop Behavior - Owned by lib-inventory, but affects lib-item's container index and event patterns. See Intentional Quirks #6 and #7.
- **[#308](https://github.com/beyond-immersion/bannou-service/issues/308)**: Replace `additionalProperties:true` metadata pattern with typed schemas - Affects `instanceMetadata` field. See Design Considerations #1.
- **[#407](https://github.com/beyond-immersion/bannou-service/issues/407)**: Item Decay/Expiration System - Time-based item lifecycle. Dependency for lib-status ([#417](https://github.com/beyond-immersion/bannou-service/issues/417)). See Potential Extensions #4.
- **[#430](https://github.com/beyond-immersion/bannou-service/issues/430)**: lib-socket - Item socket, linking, and gem placement system. See Potential Extensions #5.
- **[#486](https://github.com/beyond-immersion/bannou-service/issues/486)**: Deprecation cascade behavior - Design question for what happens to instances. See Stubs #1.
- **[#482](https://github.com/beyond-immersion/bannou-service/issues/482)**: Item category indexing for inventory query optimization - Owned by lib-inventory, but may require lib-item to provide batch template lookups or category-indexed queries to resolve N+1 pattern at scale.
- **[#489](https://github.com/beyond-immersion/bannou-service/issues/489)**: Template migration on deprecation - Sub-question of #486. See Potential Extensions #1.
