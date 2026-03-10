# Affix Plugin Deep Dive

> **Plugin**: lib-affix (not yet created)
> **Schema**: `schemas/affix-api.yaml` (not yet created)
> **Version**: N/A (Pre-Implementation)
> **State Store**: affix-definitions (MySQL), affix-implicit-mappings (MySQL), affix-instances (MySQL), affix-definition-cache (Redis), affix-instance-cache (Redis), affix-pool-cache (Redis), affix-lock (Redis) — all planned
> **Layer**: L4 GameFeatures
> **Status**: Aspirational — no schema, no generated code, no service implementation exists.
> **Planning**: [Economy System Guide](../guides/ECONOMY-SYSTEM.md)
> **Implementation Map**: [docs/maps/AFFIX.md](../maps/AFFIX.md)
> **Short**: Item modifier definition and procedural generation for equipment customization

---

## Overview

Item modifier definition, instance management, and stat computation service (L4 GameFeatures). Owns two layers of data: **definitions** (modifier templates with tiers, mod groups, spawn weights, stat grants) and **instances** (per-item applied modifier state stored in Affix's own state store). Any system that needs to answer "what modifiers does this item have?" or "what is this item worth?" queries lib-affix's typed API. Game-agnostic (PoE-style prefix/suffix tiers, Diablo-style legendary affixes, or simple "quality stars" are all valid configurations). Internal-only, never internet-facing.

---

## Why Own Instance State? (Architectural Rationale)

The question arises: why not store affix data in `ItemInstance.instanceMetadata` and let services read it directly?

**Because that is a violation.** Storing structured affix data in lib-item's freeform metadata field and having lib-craft, lib-loot, and lib-market read it by convention (knowing key names and expected types without schema enforcement) is the exact anti-pattern that FOUNDATION TENETS (No Metadata Bag Contracts) forbids. If lib-affix renames a metadata key, every consuming service breaks silently -- no compiler error, no schema validation failure, no test failure.

**The correct pattern**: lib-affix owns its domain data in its own typed schema and state stores, and exposes it through its own API. Other services query lib-affix, not metadata blobs from lib-item responses.

| Concern | lib-item (L2) | lib-affix (L4) |
|---------|---------------|----------------|
| **What it stores** | Item templates, instances, quantities, binding, container references | Affix definitions AND per-item applied affix state |
| **What it knows** | "This item exists with this template" | "This item has a fire resistance suffix from the Elemental Defense mod group" |
| **Instance data** | Item properties (quantity, durability, binding) | Applied modifiers (slots, rolled values, states, influences, quality) |
| **Modification rules** | None for affix data | Mod group exclusivity, slot limits by rarity, influence requirements, state flags |
| **Computation** | None | Stat aggregation from base + implicits + explicits + quality + sockets |
| **Consumers** | Every service that touches items | Crafting, loot, market, NPC evaluation, equipment display |

lib-item is L2 (GameFoundation) because every game needs items. lib-affix is L4 (GameFeatures) because many games don't need complex modifier systems. A simple mobile game with predefined item variants needs lib-item; only games with randomized loot, crafting, or enchantment systems need lib-affix.

**What they share is identity, not storage**: lib-affix uses the `itemInstanceId` from lib-item as its foreign key. When lib-affix stores affix data for an item, the key is the item instance ID. This means:
- lib-item owns the item lifecycle (create, destroy)
- lib-affix owns the modifier lifecycle (initialize, apply, remove, reroll)
- lib-affix implements `IItemInstanceDestructionListener` to clean up its own state when items are destroyed (DI Listener pattern, not event subscription — high-frequency exception)
- Other services call lib-affix's API for modifier data, not lib-item's metadata

This follows the same ownership pattern as other L4 services: lib-character-personality owns personality data keyed by characterId, lib-character-encounter owns encounter data keyed by characterId. lib-affix owns affix data keyed by itemInstanceId.

---

## The Logos Inscription Model (Arcadia Game Integration)

> **This section describes Arcadia-specific lore**. lib-affix is game-agnostic; these mappings are applied through configuration, ABML behaviors, and narrative design -- not code.

In Arcadia's metaphysics, all matter exists in dual form: physical substance and logos pattern. The logos pattern is the metaphysical signature that defines what something IS in the language of reality. A sword's logos pattern encodes "sharp", "balanced", "steel-forged"; a ring's pattern encodes "circular", "gold", "eternal".

Enchantment -- the technology of Arcadia -- is the art of manipulating logos patterns through controlled pneuma flow. An enchanter doesn't "add fire damage to a sword." They inscribe fire-aspected logos into the sword's pattern, causing the physical form to manifest heat. The quality of the inscription determines how stably the logos integrates with the existing pattern.

| Affix Mechanic | Arcadia Equivalent | Metaphysical Basis |
|----------------|--------------------|--------------------|
| **Implicit modifier** | Natural logos | The meaning the material inherently carries -- ruby naturally resonates with fire, mithril with magic resistance |
| **Prefix/suffix** | Inscribed logos | Patterns deliberately applied by an enchanter through pneuma manipulation |
| **Enchant slot** | Attunement channel | A prepared pathway in the item's logos structure for specific pneuma flow |
| **Affix tier** | Inscription depth | How deeply the logos pattern is inscribed -- deeper inscriptions are more powerful but require more skill |
| **Mod group** | Logos domain | Patterns of the same fundamental meaning -- you cannot inscribe two competing fire patterns simultaneously |
| **Spawn weight** | Logos compatibility | How naturally certain patterns form in certain materials -- fire inscriptions flow easily into ruby, poorly into sapphire |
| **Generation tag** | Elemental resonance | Material properties that attract or repel specific logos patterns |
| **Influence type** | Leyline attunement | The item has been exposed to a specific pneuma source, enabling logos patterns that only form under that resonance |
| **Quality** | Material purity | How well the physical form conducts pneuma -- purer materials sustain deeper inscriptions |
| **Corruption** | Logos destabilization | The pattern becomes chaotic, potentially gaining power but losing the ability to be further modified |
| **Fracturing** | Logos crystallization | A pattern becomes permanently inscribed -- no force can alter it, but it anchors the item's identity |
| **Synthesis** | Pure logos construction | Creating meaning without material basis -- items woven from logos alone carry implicits impossible in physical materials |
| **Mirroring** | Perfect logos duplication | Copying an item's complete logos signature -- the copy is perfect but fixed, a snapshot of the original |
| **Sockets** | Resonance cavities | Prepared voids in the item's logos structure that accept compatible logos catalysts (gems, runes) |

**NPC item evaluation**: The `${affix.*}` variable provider feeds NPC GOAP decisions about item quality: "This sword has weak fire logos" or "This ring already carries three stable inscriptions." NPC crafting decisions (what to inscribe, what to improve) are driven by lib-craft's `${craft.*}` variable provider, which consults lib-affix data as part of its workflow. See [CRAFT.md](CRAFT.md) for the enchanting workflow integration.

---

## Core Concepts

### Affix Definitions (The Template Layer)

An affix definition is the template that describes a modifier. It defines what the modifier does (stat grants), where it can appear (valid item classes, slot type), how it's generated (spawn weight, tags), and how it relates to other modifiers (mod group, tier).

```
AffixDefinition:
 definitionId: Guid
 gameServiceId: Guid # Scoped per game service (like ItemTemplate)
 code: string # Unique within game service (e.g., "increased_life_t1")

 # Classification
 slotType: string # "implicit", "prefix", "suffix", "enchant", or custom
 modGroup: string # Exclusivity group (e.g., "IncreasedLife")
 tier: int # 1 = best, higher = weaker (within mod group)
 category: string # Broad classification (e.g., "defense", "offense")
 tags: [string] # Generation tags for pool filtering

 # Requirements
 requiredItemLevel: int # Minimum item level to spawn
 requiredInfluences: [string] # Required influence types (empty = no requirement)
 validItemClasses: [string] # Item template categories this can appear on

 # Stat Grants (supports hybrid mods -- multiple grants per definition)
 statGrants:
 - statCode: string # "maximum_life", "fire_resistance", etc.
 minValue: decimal
 maxValue: decimal

 # Generation
 spawnWeight: int # Base weight for weighted random selection (1000 typical)
 spawnTagModifiers: # Per-tag weight multipliers (typed array, not freeform object)
 - tag: string # e.g., "amulet", "ring"
 weightMultiplier: int # e.g., 0 (can't appear), 1500 (more likely)

 # Display
 displayName: string # "of the Godslayer" (suffix), "Blazing" (prefix)
 displayOrder: int # Sorting priority within slot type

 # Deprecation (Category B -- deprecate-only, no delete, no undeprecate)
 isDeprecated: bool
 deprecatedAt: DateTimeOffset?
 deprecationReason: string?
```

**Key design decisions**:

1. **Slot types are opaque strings**, not enums. "prefix", "suffix", "implicit", "enchant" are conventions -- games define their own slot types. A game might use "rune", "inscription", "blessing" as slot types.

2. **Mod groups enforce exclusivity**: Only one affix from a given mod group can appear on an item. All tiers within a group compete for the same "slot" -- you can't have life AND life on the same item.

3. **Stat grants are a list** (not a single stat): Hybrid mods grant multiple stats from one affix. "Subterranean" prefix grants +Life AND +Mana. One prefix slot, two stat lines. This is fundamental to interesting modifier design.

4. **Spawn tag modifiers** allow per-context weight overrides. A mod might have base weight 1000 but weight 0 on amulets (can't appear there) and weight 1500 on rings (more likely there). This replaces a rigid "valid item classes" list with a weighted probability system. Modeled as a typed array of `{ tag, weightMultiplier }` objects (not freeform `object`/`additionalProperties: true`).

5. **Affix definitions are Category B deprecation** (per IMPLEMENTATION TENETS). Definitions are templates where applied affix instances permanently reference the definition ID. Definitions must remain readable forever. No delete endpoint, no undeprecate endpoint. Deprecated definitions cannot be applied to new items (see ApplyAffix deprecation guard below). The `isActive` field is redundant with deprecation and is NOT included -- deprecation IS the mechanism for marking a definition as inactive.

6. **Effective rarity is an opaque string** (Category B per tenets decision tree -- game designers define new rarity tiers at deployment time). Default conventions are "normal", "magic", "rare", "unique", but games may define "common/uncommon/rare/legendary" or any other rarity scheme. Not an enum.

### Affix Instance Data (The Instance Layer)

Each item managed by lib-affix has a corresponding record in Affix's own instance store. This record is the authoritative source for "what modifiers does this item have?" -- not lib-item's metadata, not a convention, not a cross-service assumption.

```
AffixInstanceModel:
 itemInstanceId: Guid # Foreign key to lib-item (NOT a Guid owned by Affix)
 gameServiceId: Guid
 effectiveRarity: string # Opaque string (Category B per tenets -- game-configurable rarity codes; defaults: "normal", "magic", "rare", "unique")
 itemLevel: int # Set at creation time (from source level)

 implicitSlots: [AffixSlotModel]
 prefixSlots: [AffixSlotModel]
 suffixSlots: [AffixSlotModel]
 enchantSlots: [AffixSlotModel]

 influences: [string] # Leyline attunement types unlocking exclusive pools
 states: AffixStatesModel # Boolean flags gating valid operations
 quality: int # 0-30, affects stat computation

AffixSlotModel:
 definitionId: Guid
 definitionCode: string # Denormalized for display efficiency
 modGroup: string # Denormalized for exclusivity validation efficiency
 rolledValues: [decimal] # One per stat grant in the definition
 isFractured: bool # Permanently locked -- cannot be removed

AffixStatesModel:
 isCorrupted: bool
 isMirrored: bool
 isSplit: bool
 isIdentified: bool
 isSynthesized: bool
```

**Lifecycle**:
- **Created** by `InitializeItemAffixes` (called by lib-loot after creating the item) or by `ApplyAffix` on an item that has no affix instance yet
- **Modified** by `ApplyAffix`, `RemoveAffix`, `RerollValues`, and state-change operations
- **Destroyed** when lib-affix's `IItemInstanceDestructionListener` receives in-process cleanup notification from lib-item (high-frequency exception; orphan reconciliation worker as durability guarantee)

**Why this matters for compliance**: Every service that needs affix data calls lib-affix's API. lib-craft calls `/affix/apply` to add modifiers during crafting. lib-loot calls `/affix/generate/set` then `/affix/initialize` to create affixed items. lib-market subscribes to `affix.modifier.applied` events to maintain its trade index. No service parses metadata blobs. No service knows affix key names by convention. If Affix restructures its internal model, no other service breaks.

### Affix Slots and Limits

Each item has a fixed number of slots per slot type, determined by its **effective rarity**:

| Effective Rarity | Implicit | Prefix | Suffix | Total Explicit |
|-----------------|----------|--------|--------|---------------|
| normal | Template-defined | 0 | 0 | 0 |
| magic | Template-defined | 0-1 | 0-1 | 1-2 |
| rare | Template-defined | 1-3 | 1-3 | 3-6 |
| unique | Template-defined | Fixed | Fixed | Predetermined |

**Slot limits are configurable per game** via `RaritySlotLimits` definitions. The table above is the PoE-inspired default. A game might define "common/uncommon/rare/legendary" with different slot counts, or skip rarity entirely and use a flat affix cap.

### Mod Groups and Exclusivity

Mod groups prevent conflicting modifiers. Every affix definition belongs to exactly one mod group (identified by string). Rules:

1. **At most one affix per mod group** on any item (across all tiers)
2. **Mod group check happens during application and generation** -- invalid combinations are rejected
3. **Fractured affixes count** toward mod group occupancy -- even though they can't be removed, they still block new affixes from the same group

### Tiers and Item Level Gating

Within a mod group, tiers represent power levels. is the best,+ the weakest. Tiers are gated by item level:

```
Mod Group: "IncreasedLife"
 +110-119 Life, requires iLvl 86, weight 200
 +100-109 Life, requires iLvl 82, weight 250
 +90-99 Life, requires iLvl 74, weight 400
 +80-89 Life, requires iLvl 64, weight 800
 ...
```

When generating an affix for an iLvl 75 item, and are excluded (item level too low). through+ are valid, and enters the weighted pool at weight 400 while enters at weight 800 (lower tiers are more likely).

**Item level** is stored on the affix instance (in `AffixInstanceModel.itemLevel`), set at instance creation time (typically from the source's level: monster level for drops, zone level for chests, recipe output level for crafting).

### Weighted Random Generation

The core algorithm for affix generation:

```
GenerateAffix(itemClass, itemLevel, slotType, existingAffixes, influences, weightModifiers):
 1. Load all definitions matching (gameServiceId, slotType, validItemClasses includes itemClass)
 2. Filter: requiredItemLevel <= itemLevel
 3. Filter: requiredInfluences subset of influences (or empty)
 4. Filter: modGroup NOT IN existing affix mod groups
 5. For each remaining definition:
 a. Start with base spawnWeight
 b. Apply spawnTagModifiers for the item class
 c. Apply external weightModifiers (passed by caller)
 d. If weight <= 0, exclude
 6. Weighted random selection from the remaining pool
 7. Roll values: for each statGrant, random uniform between minValue and maxValue
 8. Return (definitionId, rolledValues)
```

**Performance at scale**: For 100K NPCs making economic decisions, affix pool generation must be fast. The pool computation (steps 1-5) is definition-level data that doesn't change per item instance. Pre-computed pools are cached in Redis by `{gameServiceId}:{itemClass}:{slotType}:{ilvlBucket}:{influenceSet}`, with TTL measured in hours. Only the final weighted selection and value rolling are per-instance operations.

### Effective Rarity

Rarity is an **affix concern**, not a base item concern. An item starts as "normal" (no explicit affixes) and its effective rarity changes as affixes are applied:

- Normal -> Magic: first explicit affix applied (via crafting/loot)
- Magic -> Rare: enough affixes applied to exceed magic limits (via crafting/loot)
- Any -> Unique: item is created or transformed into a unique variant

Effective rarity is tracked in the affix instance record. `ItemTemplate.rarity` remains a base/display property indicating the template's intended starting rarity; effective rarity can differ on the instance.

### Item States

Item states are boolean flags stored in the affix instance that gate what operations are valid. lib-affix **reads and validates** these flags during apply/remove operations; lib-craft (or other orchestrators) **set** these flags via lib-affix's state-change API.

| State | Effect on lib-affix | Set By |
|-------|--------------------|--------|
| `isCorrupted` | Rejects apply/remove/reroll operations | lib-craft (corruption recipe) via `/affix/state/set` |
| `isMirrored` | Rejects all modification operations | lib-craft (mirror recipe) via `/affix/state/set` |
| `isFractured` | Per-affix: rejects removal of this specific affix | lib-craft or source system via `/affix/state/set` |
| `isSplit` | No direct effect on affix operations (gates further splits) | lib-craft (split recipe) via `/affix/state/set` |
| `isSynthesized` | Allows multiple implicit slots | Source system (synthesis workflow) via `/affix/state/set` |
| `isIdentified` | Controls whether affixes are visible in queries | lib-craft (identification recipe) or creation |

**lib-affix validates AND stores these states.** The flags live in the affix instance record, accessible through lib-affix's API. No service reads state flags from lib-item's metadata.

### Influences and Exclusive Pools

Influences are properties on items that unlock exclusive affix pools:

```
Item: "Shaper-influenced Vaal Regalia" (body armour)
 influences: ["shaper"]
 -> unlocks Shaper-exclusive affixes (e.g., "Nearby enemies have -9% fire resistance")
 -> these affixes have requiredInfluences: ["shaper"]
 -> normal affixes remain available alongside exclusive ones
```

An item can have multiple influences (e.g., dual-influenced items), unlocking affixes from multiple exclusive pools. Influence types are opaque strings defined per game. Influences are stored on the affix instance and modified via `/affix/influence/set`.

### Socket Integration

Sockets extend the affix system by allowing items to hold removable modifier sources (gems, runes). Socket mechanics reuse Inventory's existing container nesting:

**How sockets work**:
1. `ItemTemplate` declares `maxSocketCount` (0 = no sockets) and optionally `socketableCategories` (item categories that fit in sockets, e.g., `["gem", "rune"]`)
2. When socketing: Inventory creates a child container (type: `"socket"`) under the item's parent equipment container. The gem item is placed in the socket container.
3. When computing equipment stats, lib-affix reads socket containers for equipped items (via `IInventoryClient`), loads each socketed gem's affix instance from its own store, and includes gem stat grants in the computation.
4. Unsocketing: gem removed from socket container, socket container destroyed. Equipment stats recomputed on next query.

**Socket stat flow**:
```
Equipped item affix stats + socketed gem affix stats = total item contribution
```

Sockets are purely a stat computation concern for lib-affix. The physical containment (gem in socket container in equipment container) is lib-inventory's domain. The socket validation (can this gem go in this socket?) can use lib-item's template category checks or optionally a Contract-backed validation flow for more complex rules.

**Why not Collection for sockets**: Sockets are mutable (gems are inserted and removed frequently), have no growth semantics, and their data (which gem is in which socket of which item) is spatial inventory state. Collection is designed for one-way accumulation. Inventory already handles nested containment.

---

## Dependents

| Dependent | Relationship |
|-----------|-------------|
| lib-craft (L4, future) | Calls `/affix/apply`, `/affix/remove`, `/affix/reroll-values`, `/affix/state/set` for crafting modification workflows |
| lib-loot (L4, future) | Calls `/affix/generate/set`, `/affix/generate/batch`, `/affix/initialize` for loot drop creation with affixes. Soft dependency -- falls back to unaffixed items if lib-affix unavailable |
| lib-market (L4, future) | Calls `/affix/item/get`, `/affix/item/compute-stats`, `/affix/item/estimate-value` for search indexing and price estimation. Subscribes to `affix.modifier.applied` and `affix.modifier.removed` events for trade index maintenance |
| NPC Actor runtime (L2, via Variable Provider) | `${affix.*}` variables for item evaluation in GOAP economic/combat decisions |

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `DefinitionCacheTtlSeconds` | `AFFIX_DEFINITION_CACHE_TTL_SECONDS` | `3600` | Definition cache TTL (1 hour) |
| `InstanceCacheTtlSeconds` | `AFFIX_INSTANCE_CACHE_TTL_SECONDS` | `300` | Affix instance cache TTL (5 min) |
| `PoolCacheTtlSeconds` | `AFFIX_POOL_CACHE_TTL_SECONDS` | `3600` | Pre-computed pool cache TTL (1 hour) |
| `ComputedStatsCacheTtlSeconds` | `AFFIX_COMPUTED_STATS_CACHE_TTL_SECONDS` | `60` | Cached computed item stats TTL (1 min -- affixes change frequently) |
| `EquipmentStatsCacheTtlSeconds` | `AFFIX_EQUIPMENT_STATS_CACHE_TTL_SECONDS` | `30` | Cached aggregate equipment stats TTL (30s -- equip/unequip is frequent) |
| `ItemLevelBucketSize` | `AFFIX_ITEM_LEVEL_BUCKET_SIZE` | `10` | Item level range per cache bucket |
| `MaxItemLevel` | `AFFIX_MAX_ITEM_LEVEL` | `100` | Maximum supported item level (determines bucket count) |
| `DefaultSpawnWeight` | `AFFIX_DEFAULT_SPAWN_WEIGHT` | `1000` | Default spawn weight for new definitions |
| `DefaultMaxPrefixes` | `AFFIX_DEFAULT_MAX_PREFIXES` | `3` | Default max prefix slots for rare rarity |
| `DefaultMaxSuffixes` | `AFFIX_DEFAULT_MAX_SUFFIXES` | `3` | Default max suffix slots for rare rarity |
| `GenerationEventDeduplicationWindowSeconds` | `AFFIX_GENERATION_EVENT_DEDUPLICATION_WINDOW_SECONDS` | `60` | Deduplication window for batched generation events |
| `GenerationEventBatchMaxSize` | `AFFIX_GENERATION_EVENT_BATCH_MAX_SIZE` | `100` | Max records per batched generation event |
| `LockTimeoutSeconds` | `AFFIX_LOCK_TIMEOUT_SECONDS` | `30` | Distributed lock timeout |
| `MaxDefinitionsPerGameService` | `AFFIX_MAX_DEFINITIONS_PER_GAME_SERVICE` | `5000` | Safety limit for definition count per game service |
| `MaxAffixesPerItem` | `AFFIX_MAX_AFFIXES_PER_ITEM` | `12` | Hard cap on total affixes per item (across all slot types) |
| `IncludeSocketStatsInEquipment` | `AFFIX_INCLUDE_SOCKET_STATS_IN_EQUIPMENT` | `true` | Whether equipment stat computation includes socketed gem contributions |
| `OrphanReconciliationIntervalMinutes` | `AFFIX_ORPHAN_RECONCILIATION_INTERVAL_MINUTES` | `60` | Interval for background orphan instance cleanup worker |
| `OrphanReconciliationBatchSize` | `AFFIX_ORPHAN_RECONCILIATION_BATCH_SIZE` | `500` | Items checked per reconciliation cycle |

---

## Visual Aid

```
Affix Service Architecture (Own-Instance-Store Model)
======================================================

 DEFINITION LAYER (owned by lib-affix, MySQL + Redis cache)
 +-----------------------------------------------------------------+
 | AffixDefinition ImplicitMapping |
 | +--------------------+ +---------------------------+ |
 | | code: "life_t1" | | itemTemplateCode: | |
 | | slotType: "prefix" | | "body_armour_regalia" | |
 | | modGroup: | | implicits: | |
 | | "IncreasedLife" | | - ruby_fire_res | |
 | | statGrants: | | - mithril_magic_res | |
 | | life: 110-119 | +---------------------------+ |
 | | spawnWeight: 200 | |
 | +--------------------+ |
 +-----------------------------------------------------------------+
 |
 | GenerateAffixSet / ApplyAffix
 v
 INSTANCE LAYER (owned by lib-affix, MySQL + Redis cache)
 +-----------------------------------------------------------------+
 | AffixInstanceModel (keyed by itemInstanceId) |
 | +-----------------------------------------------------------+ |
 | | itemInstanceId: 3fa8... | |
 | | effectiveRarity: "rare" | |
 | | itemLevel: 75 | |
 | | prefixSlots: | |
 | | [0] IncreasedLife, rolledValues: [95] | |
 | | [1] AddedPhysDmg, rolledValues: [12, 22] | |
 | | [2] IncreasedES, rolledValues: [72] | |
 | | suffixSlots: | |
 | | [0] FireResistance, rolledValues: [38] [FRACTURED] | |
 | | [1] AttackSpeed, rolledValues: [6] | |
 | | [2] CritChance, rolledValues: [22] | |
 | | implicitSlots: | |
 | | [0] ruby_fire_res, rolledValues: [28] | |
 | | states: {corrupted: false, mirrored: false, ...} | |
 | | quality: 20 | |
 | +-----------------------------------------------------------+ |
 +-----------------------------------------------------------------+
 |
 | Typed API (no metadata convention)
 v
 +------------------+ +------------------+ +------------------+
 | lib-craft (L4) | | lib-loot (L4) | | lib-market (L4) |
 | Calls /affix/ | | Calls /affix/ | | Calls /affix/ |
 | apply, remove, | | generate/set, | | item/get, |
 | reroll, state/set| | initialize | | compute-stats, |
 +------------------+ +------------------+ | estimate-value |
 +------------------+


Loot Drop Creation Flow (Orchestrated by lib-loot)
====================================================

 lib-loot generates a rare drop:
 |
 +-- 1. /affix/generate/set
 | (itemClass, iLvl, rarity, influences)
 | Returns: AffixSetData (slots with definitions + rolled values)
 |
 +-- 2. /item/instance/create
 | (templateId, containerId, ...)
 | Returns: itemInstanceId
 |
 +-- 3. /affix/initialize
 | (itemInstanceId, affixSetData)
 | Creates AffixInstanceModel in lib-affix's store
 | Publishes affix.instance.initialized
 |
 +-- Item now exists in lib-item; affixes exist in lib-affix
 Any service needing affix data calls lib-affix's API


Equipment Stat Computation Flow
=================================

 /affix/equipment/compute (entityId, entityType)
 |
 +-- 1. IInventoryClient.ListContainers
 | (ownerType, ownerId, filter: isEquipmentSlot=true)
 | Returns: equipment containers
 |
 +-- 2. For each equipped item:
 | |
 | +-- Load AffixInstanceModel from own store
 | |
 | +-- Compute: base template stats
 | | + implicit rolled values
 | | + prefix rolled values
 | | + suffix rolled values
 | | + enchant rolled values
 | | + quality modifier
 | |
 | +-- If sockets (IncludeSocketStatsInEquipment):
 | IInventoryClient.GetContainer(itemContainer, children)
 | For each socket child container:
 | Load socketed gem's AffixInstanceModel
 | Add gem stat grants to item total
 |
 +-- 3. Aggregate per-stat totals across all equipment
 |
 +-- 4. Cache in Redis (TTL: EquipmentStatsCacheTtlSeconds)
 |
 +-- Return EquipmentStatsModel:
 { "maximum_life": 245, "fire_resistance": 66,
 "attack_speed": 6, "crit_chance": 22, ... }
 + per-item breakdown for UI
```

---

## Stubs & Unimplemented Features

**Everything is unimplemented.** This is a pre-implementation architectural specification. No schema, no generated code, no service implementation exists. The following phases are planned:
<!-- AUDIT:NEEDS_DESIGN:2026-03-08:https://github.com/beyond-immersion/bannou-service/issues/490 -->

### Phase 1: Definition Infrastructure
- Create affix-api.yaml schema with all endpoints
- Create affix-events.yaml schema
- Create affix-configuration.yaml schema
- Generate service code
- Implement definition CRUD (create, get, list, update, deprecate, seed, list-mod-groups)
- Implement implicit mapping CRUD (create, get, seed, roll)
- Implement pool cache warming and invalidation

### Phase 2: Instance Store and Core Operations
- Implement `affix-instances` MySQL store with dual-key pattern
- Implement `affix-instance-cache` Redis store
- Implement `InitializeItemAffixes` for batch affix instance creation
- Implement `GetAffixInstance` with cache read-through
- Implement `IItemInstanceDestructionListener` for in-process instance cleanup (high-frequency exception; register as `Singleton` in plugin startup)
- Implement `ApplyAffix` with full validation (mod groups, slot limits, item states)
- Implement `RemoveAffix` with state flag checks
- Implement `RerollValues` for value re-randomization
- Implement `SetItemState` for state flag mutations
- Implement effective rarity tracking and transitions

### Phase 3: Generation Engine
- Implement `GenerateAffixPool` with cached pool lookup and in-memory filtering
- Implement `GenerateAffixSet` for complete affix set generation
- Implement `BatchGenerateAffixSets` for loot generation at scale
- Performance testing at 100K NPC item evaluation scale

### Phase 4: Query, Computation, and Variable Provider
- Implement `GetItemAffixes` with definition enrichment
- Implement `ComputeItemStats` for per-item stat aggregation
- Implement `ComputeEquipmentStats` for entity-level equipment stat aggregation
- Implement `CompareItems` for NPC GOAP equipment evaluation
- Implement `EstimateItemValue` for NPC economic decisions
- Implement variable provider factory (`AffixItemEvaluationProviderFactory`)

### Phase 5: Socket Integration
- Coordinate with lib-socket (#430) for socket configuration state and lib-item for `maxSocketCount` on ItemTemplate
- Coordinate with lib-inventory for socket child container creation
- Extend `ComputeItemStats` to include socketed gem contributions
- Extend `ComputeEquipmentStats` for socket-aware equipment totals

### Phase 6: Events, Cleanup, and Background Workers
- Implement all event publishing (lifecycle, application, state change, generation batch)
- Implement event handlers for item template lifecycle events
- Declare `x-references` in `affix-api.yaml` targeting `game-service` with cleanup endpoint `/affix/cleanup-by-game-service` for lib-resource integration
- Implement resource cleanup endpoint (`/affix/cleanup-by-game-service`)
- Implement orphan reconciliation background worker (periodic scan + batch item existence check)
- Wire analytics integration (soft dependency)
- Integration testing with lib-item, lib-inventory

---

## Potential Extensions

1. **Rarity slot limit definitions as configurable entities**: Instead of hardcoded slot counts per rarity, expose a `RaritySlotLimit` definition API that games use to define their own rarity tiers with custom slot counts. A game could define "Legendary" rarity with 4 prefixes and 4 suffixes, or "Mythic" with unlimited slots but exponentially increasing generation cost.
<!-- AUDIT:NEEDS_DESIGN:2026-03-08:https://github.com/beyond-immersion/bannou-service/issues/588 -->

2. **Affix trade index (materialized view)**: A denormalized MySQL store that indexes `(gameServiceId, statCode, statValue, itemInstanceId)` for fast trade-site-style queries ("find all items with +90 life and +30 fire res"). Built asynchronously from `affix.modifier.applied` and `affix.modifier.removed` events. Used by lib-market for search.
<!-- AUDIT:NEEDS_DESIGN:2026-03-08:https://github.com/beyond-immersion/bannou-service/issues/589 -->

3. **Unique item definitions**: A `UniqueItemDefinition` entity that maps an item template code to a fixed set of affix definitions with predetermined roll ranges. When a unique item drops, its affixes come from the unique definition (not weighted random). The unique definition is owned by lib-affix, not lib-item.

4. **Client events**: `affix-client-events.yaml` for pushing real-time affix change notifications to connected WebSocket clients (affix applied, item identified, item corrupted).

5. **Synthesis implicit tables**: A mapping from combinations of sacrificed item affix properties to possible synthesis implicit outcomes. This is a complex system (PoE's synthesis was notoriously opaque) and may warrant its own endpoint set.

6. **IEquipmentStatsContributor DI interface**: If Status (L4) needs to include equipment stats in its unified effects query, add `IEquipmentStatsContributor` in `bannou-service/Providers/`. Affix implements it; Status discovers via `IEnumerable<IEquipmentStatsContributor>`. This keeps Status decoupled from Affix while enabling unified effect + equipment stat queries. Both are L4 so the DI inversion is purely for extensibility, not hierarchy compliance.

7. **Equipment stats as seed input**: Equipment power score could feed into guardian spirit seed growth. A character with consistently good equipment contributes "equipment mastery" growth to its guardian spirit's seed. This would use the existing Collection→Seed growth pipeline: Affix publishes equipment milestone events, a collection type tracks milestones, Seed grows.

8. **Affix-aware item decay**: Items with specific affix states (corrupted, fractured) could decay faster or slower. When lib-item implements #407 (Item Decay/Expiration), lib-affix could register decay rate modifiers based on affix state. This would use a DI provider interface (`IItemDecayModifier`) that lib-item discovers.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

None. Plugin is aspirational -- no code exists to have bugs.

### Intentional Quirks (Documented Behavior)

1. **Affix slot types are opaque strings, not enums**: "prefix", "suffix", "implicit", "enchant" are conventions, not enforced values. Games define their own slot types. lib-affix validates slot type consistency (an affix must be applied to a slot matching its `slotType`) but doesn't restrict which string values are valid. Slot limit configuration references slot types by string.

2. **Mod groups are per-game-service, not global**: Two games can have mod groups with the same name ("IncreasedLife") that are entirely independent. Mod group uniqueness is scoped to game service + item instance.

3. **Affix instance is keyed by itemInstanceId, not by its own GUID**: lib-affix does not generate its own primary key for affix instances. The item instance ID IS the key. This means one item can have at most one affix instance record. If an item needs a "clean slate" (all affixes removed, new set applied), the existing record is updated, not replaced.

4. **Computed stats are cached, not stored**: `ComputeItemStats` and `ComputeEquipmentStats` results are cached in Redis with short TTL. They're derived data that changes when definitions change (e.g., a definition tier value update should be reflected immediately) or when equipment changes. Consumers must not treat cached stats as authoritative for critical decisions -- they should call the compute endpoints for fresh values when precision matters.

5. **Effective rarity is advisory, not enforced by lib-item**: The `effectiveRarity` field in the affix instance is maintained by lib-affix during apply/remove operations. lib-item doesn't know about it and doesn't enforce it.

6. **Pool cache uses item level bucketing**: To keep the number of cached pools manageable, item levels are bucketed (e.g., levels 71-80 share a pool). The cached pool contains all definitions valid for the bucket's upper bound. When generating for a specific level (e.g., 75), the cached pool is filtered in-memory to exclude definitions requiring a higher level. This means the cache is slightly larger than necessary but the in-memory filter is fast.

7. **Implicit mappings reference item template CODES, not IDs**: Implicit mappings use `itemTemplateCode` (the human-readable code string) rather than the template's GUID. This is deliberate: item templates may be recreated (deprecated + replaced) with different IDs but the same code. The implicit mapping should follow the code, not the ID.

8. **Fractured affixes block their mod group permanently**: A fractured affix cannot be removed, and its mod group is permanently occupied. If a player fractures a low-tier life mod, they can never have a higher-tier life mod on that item. This is intentional (fracturing is a powerful but irreversible commitment) and should be communicated clearly to players.

9. **Batch generation does not guarantee uniqueness**: `BatchGenerateAffixSets` generates each item independently. Two items in the same batch might get identical affix sets if the RNG produces the same sequence. Statistically unlikely but not prevented.

10. **Generation events are batched, not per-item**: To prevent event flooding during mass loot generation (dungeon clear, batch NPC crafting), generation events are deduped by source and published in batches. Individual `affix.modifier.applied` events still fire for each application through the application endpoints.

11. **Instance cleanup is DI-listener-driven, not synchronous**: When lib-item destroys an item, lib-affix's `IItemInstanceDestructionListener` implementation receives an in-process notification on the same node. There is a brief window where other nodes may still see cached affix data for a destroyed item. The affix instance cache TTL (5 minutes) means stale entries expire naturally. The orphan reconciliation background worker provides the durability guarantee for any missed notifications (see Quirk #17).

12. **Equipment and effect stats are computed independently**: lib-affix computes equipment stats (base + affixes + sockets + quality). lib-status computes temporary effects (buffs, debuffs, blessings) and seed-derived capabilities. Consumers query both services independently. If unified queries are later needed, the `IEquipmentStatsContributor` DI pattern (Potential Extension #6) provides a clean integration path without coupling the services.

13. **Applied affixes are snapshots, not live references**: If a definition's stat grant ranges change (e.g., life changed from 90-99 to 85-94), existing items with that affix retain their original rolled values. There is no automatic re-computation or migration. lib-affix treats applied affixes as snapshots at application time.

14. **Implicit mappings are forward-only**: When implicit mappings change, newly created items get the new values, but existing items are unaffected. There is no mechanism to "re-roll" existing items' implicits unless the caller explicitly calls the application endpoints.

15. **Influence application is caller-driven**: lib-affix tracks influences on items and gates affix pools by influence requirements, but the mechanism for giving an item an influence is via `/affix/influence/set`. The trigger is game-specific (in PoE, influences come from Shaper drops; in Arcadia, they might come from leyline exposure); lib-affix provides the storage and validation.

16. **Equipment detection depends on inventory container flags**: The `AffixItemEvaluationProviderFactory` queries lib-inventory for equipment-type containers via `IInventoryClient.ListContainersAsync(ownerType, ownerId, isEquipmentSlot: true)`. Which container types count as equipment slots is game-specific, determined by how containers are created during game setup.

17. **Orphan reconciliation provides durability guarantee**: The `IItemInstanceDestructionListener` provides guaranteed in-process delivery on the node that processes the deletion, but if deletion occurs on a node where lib-affix is not loaded (unusual but possible in partitioned deployments), or if the listener throws, affix instances become orphaned. The orphan reconciliation background worker periodically scans affix instances, batch-checks item existence via `IItemClient`, and deletes orphaned records. This is the durability guarantee required by FOUNDATION TENETS (High-Frequency Instance Lifecycle Exception). Configuration: `OrphanReconciliationIntervalMinutes` (default: 60), `OrphanReconciliationBatchSize` (default: 500).

18. **Socket detection uses container type convention**: Equipment stat computation identifies socket child containers by the `"socket"` container type string in lib-inventory. This is a shared naming convention between lib-affix and the socket creation flow (orchestrated by the caller). If lib-inventory changes container type naming, socket detection breaks -- mitigated by both sides agreeing on the convention string.

---

## Work Tracking

### Active
- [#490](https://github.com/beyond-immersion/bannou-service/issues/490) — Full implementation of lib-affix (all 6 phases). Open questions include item level sourcing, equipment query semantics, pool cache invalidation strategy, and socket integration prerequisites.
- [#588](https://github.com/beyond-immersion/bannou-service/issues/588) — Decide scope for rarity slot limit definitions: static configuration vs dynamic API entities.
- [#589](https://github.com/beyond-immersion/bannou-service/issues/589) — Affix trade index ownership: lib-affix vs lib-market.
- [#607](https://github.com/beyond-immersion/bannou-service/issues/607) — Design: quality-to-affix tier mapping contract between lib-craft and lib-affix.

### Related / Dependencies
- [#430](https://github.com/beyond-immersion/bannou-service/issues/430) — lib-socket: Phase 5 (Socket Integration) is blocked until lib-socket's state ownership and container conventions are designed.
- [#559](https://github.com/beyond-immersion/bannou-service/issues/559) — Item batch instance destruction: if implemented, `IItemInstanceDestructionListener` may need a batch overload for efficient multi-item cleanup.

### Completed
- (2026-03-08) Audit: linked Stubs & Unimplemented Features section to #490.
- (2026-03-08) Audit: created #589 for trade index ownership design question (Potential Extension #2).
- (2026-03-08) Audit: reclassified all 7 Design Considerations as Intentional Quirks #12-#18 — all were already-decided behaviors, not open questions requiring planning.
- (2026-03-08) Review: added #607 to Active tracking (cross-service design question with lib-craft). Added #430 and #559 as Related/Dependencies. Added `x-references` declaration to Phase 6. Updated Phase 5 to reference lib-socket (#430) coordination.

*Plugin is in pre-implementation phase. See [Economy System Guide](../guides/ECONOMY-SYSTEM.md) for the cross-cutting economy architecture.*
