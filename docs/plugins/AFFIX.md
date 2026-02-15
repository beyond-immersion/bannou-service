# Affix Plugin Deep Dive

> **Plugin**: lib-affix (not yet created)
> **Schema**: `schemas/affix-api.yaml` (not yet created)
> **Version**: N/A (Pre-Implementation)
> **State Store**: affix-definitions (MySQL), affix-implicit-mappings (MySQL), affix-pool-cache (Redis), affix-definition-cache (Redis), affix-lock (Redis) — all planned
> **Layer**: L4 GameFeatures
> **Status**: Aspirational — no schema, no generated code, no service implementation exists.
> **Planning**: [Economy System Guide](../guides/ECONOMY-SYSTEM.md)

---

## Overview

Item modifier definition and generation service (L4 GameFeatures) for affix definitions, weighted random generation, validated application primitives, and stat computation. A structured layer above lib-item's opaque `instanceMetadata` that gives meaning to item modifiers: typed definitions with tiers, mod groups for exclusivity, spawn weights for probabilistic generation, and stat grants for computed item power. Any system that needs to answer "what modifiers can this item have?" or "what is this item worth?" queries lib-affix. Game-agnostic (PoE-style prefix/suffix tiers, Diablo-style legendary affixes, or simple "quality stars" are all valid configurations). Internal-only, never internet-facing.

---

## Why Not lib-item? (Architectural Rationale)

The question arises: should affixes be an extension to lib-item rather than a separate service? After all, affixes ARE item properties.

**The answer is no -- affixes are domain semantics layered on generic storage.**

| Concern | lib-item (L2) | lib-affix (L4) |
|---------|---------------|----------------|
| **What it stores** | Item templates, instances, quantities, binding, container references | Affix definitions, tier structures, mod groups, generation rules, influence types |
| **What it knows** | "This item has metadata" | "This item has a T3 fire resistance suffix from the Elemental Defense mod group" |
| **Modification rules** | Anyone can write to `instanceMetadata` | Mod group exclusivity, slot limits by rarity, influence requirements, state flags |
| **Computation** | None (opaque blobs) | Stat aggregation from base + implicits + explicits + quality |
| **Generation** | Creates instances with provided metadata | Weighted random selection from context-filtered pools |
| **Consumers** | Every service that touches items | Crafting, loot, market, NPC evaluation, equipment display |

lib-item is L2 (GameFoundation) because every game needs items. lib-affix is L4 (GameFeatures) because many games don't need complex modifier systems. A simple mobile game with predefined item variants needs lib-item; only games with randomized loot, crafting, or enchantment systems need lib-affix.

**What they share is storage, not semantics**:
- lib-item provides `ItemInstance.instanceMetadata` as an opaque JSON blob
- lib-affix writes structured data to that blob following the affix metadata convention
- lib-item doesn't validate, interpret, or compute anything about affix data
- lib-affix doesn't persist its own instance-level data -- it reads and writes through lib-item

This is analogous to how lib-status uses lib-item/lib-inventory for storage but owns the domain semantics of status effects. The storage primitive is L2; the domain interpretation is L4.

**The foundational distinction**: lib-affix manages WHAT modifiers exist (definitions, tiers, rules, valid pools) and provides validated primitives for applying/removing them. HOW those primitives are orchestrated into gameplay-meaningful operations (crafting recipes, currency effects, multi-step enchanting processes) is lib-craft's domain. WHO creates affixed items at scale (loot tables, vendor stock, quest rewards) is lib-loot's domain. This separation means lib-affix has three independent consumer categories: generators (lib-loot) call pool generation and batch set creation; orchestrators (lib-craft) call validated application/removal primitives within workflow sessions; readers (lib-market, NPC GOAP, UI) call queries and stat computation.

**The affix metadata convention**: lib-affix defines a versioned JSON schema for `ItemInstance.instanceMetadata.affixes` that all consumers read without importing lib-affix. This convention enables lib-craft and lib-loot to read affix data from items directly, and lib-market to index affix properties for search -- without coupling those services to lib-affix at the code level. The convention is a documented data format, not an API dependency.

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

**NPC item evaluation**: The `${affix.*}` variable provider feeds NPC GOAP decisions about item quality: "This sword has weak fire logos" or "This ring already carries three stable inscriptions." NPC crafting decisions (what to inscribe, what to improve) are driven by lib-craft's `${craft.*}` variable provider, which consults lib-affix data as part of its workflow. See [CRAFT.md](CRAFT.md) for the enchanting workflow integration.

---

## Core Concepts

### Affix Definitions (The Template Layer)

An affix definition is the template that describes a modifier. It defines what the modifier does (stat grants), where it can appear (valid item classes, slot type), how it's generated (spawn weight, tags), and how it relates to other modifiers (mod group, tier).

```
AffixDefinition:
  definitionId: Guid
  gameServiceId: Guid          # Scoped per game service (like ItemTemplate)
  code: string                 # Unique within game service (e.g., "increased_life_t1")

  # Classification
  slotType: string             # "implicit", "prefix", "suffix", "enchant", or custom
  modGroup: string             # Exclusivity group (e.g., "IncreasedLife")
  tier: int                    # 1 = best, higher = weaker (within mod group)
  category: string             # Broad classification (e.g., "defense", "offense")
  tags: [string]               # Generation tags for pool filtering

  # Requirements
  requiredItemLevel: int       # Minimum item level to spawn
  requiredInfluences: [string] # Required influence types (empty = no requirement)
  validItemClasses: [string]   # Item template categories this can appear on

  # Stat Grants (supports hybrid mods -- multiple grants per definition)
  statGrants:
    - statCode: string         # "maximum_life", "fire_resistance", etc.
      minValue: decimal
      maxValue: decimal

  # Generation
  spawnWeight: int             # Base weight for weighted random selection (1000 typical)
  spawnTagModifiers: object    # Per-tag weight multipliers (e.g., {"amulet": 0, "ring": 1500})

  # Display
  displayName: string          # "of the Godslayer" (suffix), "Blazing" (prefix)
  displayOrder: int            # Sorting priority within slot type

  # Metadata
  isActive: bool
  isDeprecated: bool
```

**Key design decisions**:

1. **Slot types are opaque strings**, not enums. "prefix", "suffix", "implicit", "enchant" are conventions -- games define their own slot types. A game might use "rune", "inscription", "blessing" as slot types.

2. **Mod groups enforce exclusivity**: Only one affix from a given mod group can appear on an item. All tiers within a group compete for the same "slot" -- you can't have T1 life AND T3 life on the same item.

3. **Stat grants are a list** (not a single stat): Hybrid mods grant multiple stats from one affix. "Subterranean" prefix grants +Life AND +Mana. One prefix slot, two stat lines. This is fundamental to interesting modifier design.

4. **Spawn tag modifiers** allow per-context weight overrides. A mod might have base weight 1000 but weight 0 on amulets (can't appear there) and weight 1500 on rings (more likely there). This replaces a rigid "valid item classes" list with a weighted probability system.

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

Within a mod group, tiers represent power levels. T1 is the best, T12+ the weakest. Tiers are gated by item level:

```
Mod Group: "IncreasedLife"
  T1: +110-119 Life, requires iLvl 86, weight 200
  T2: +100-109 Life, requires iLvl 82, weight 250
  T3: +90-99  Life, requires iLvl 74, weight 400
  T4: +80-89  Life, requires iLvl 64, weight 800
  ...
```

When generating an affix for an iLvl 75 item, T1 and T2 are excluded (item level too low). T3 through T4+ are valid, and T3 enters the weighted pool at weight 400 while T4 enters at weight 800 (lower tiers are more likely).

**Item level** is stored on the item instance (in `instanceMetadata.affixes.itemLevel`), not the template. It's set at instance creation time (typically from the source's level: monster level for drops, zone level for chests, recipe output level for crafting). lib-affix reads it; it doesn't set it.

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

Effective rarity is tracked in the affix metadata on the item instance. `ItemTemplate.rarity` remains a base/display property indicating the template's intended starting rarity; effective rarity can differ on the instance.

### Item States

Item states are boolean flags stored in the affix metadata that gate what operations are valid. lib-affix **reads and validates** these flags during apply/remove operations; lib-craft (or other orchestrators) **set** these flags as part of crafting workflows.

| State | Effect on lib-affix | Set By |
|-------|--------------------|--------|
| `isCorrupted` | Rejects apply/remove/reroll operations | lib-craft (corruption recipe) |
| `isMirrored` | Rejects all modification operations | lib-craft (mirror recipe) |
| `isFractured` | Per-affix: rejects removal of this specific affix | lib-craft or source system (drop/synthesis) |
| `isSplit` | No direct effect on affix operations (gates further splits) | lib-craft (split recipe) |
| `isSynthesized` | Allows multiple implicit slots | Source system (synthesis workflow) |
| `isIdentified` | Controls whether affixes are visible in queries | lib-craft (identification recipe) or creation |

**lib-affix validates but does not set these states.** The flags live in the affix metadata convention, readable by any service without importing lib-affix. The `ApplyAffix` and `RemoveAffix` primitives check these flags and reject operations on corrupted/mirrored items or fractured affixes. Setting states is a crafting concern -- see [CRAFT.md](CRAFT.md).

### Influences and Exclusive Pools

Influences are properties on items that unlock exclusive affix pools:

```
Item: "Shaper-influenced Vaal Regalia" (body armour)
  influences: ["shaper"]
  -> unlocks Shaper-exclusive affixes (e.g., "Nearby enemies have -9% fire resistance")
  -> these affixes have requiredInfluences: ["shaper"]
  -> normal affixes remain available alongside exclusive ones
```

An item can have multiple influences (e.g., dual-influenced items), unlocking affixes from multiple exclusive pools. Influence types are opaque strings defined per game.

### The Affix Metadata Convention

lib-affix writes structured JSON to `ItemInstance.instanceMetadata` under the `affixes` key. This schema is versioned and documented so that lib-craft, lib-loot, lib-market, and game clients can read affix data without importing lib-affix:

```json
{
  "affixes": {
    "version": 1,
    "effectiveRarity": "rare",
    "itemLevel": 75,
    "implicitSlots": [
      {
        "definitionId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
        "definitionCode": "ruby_fire_res",
        "modGroup": "NaturalFireResistance",
        "rolledValues": [28],
        "isFractured": false
      }
    ],
    "prefixSlots": [
      {
        "definitionId": "...",
        "definitionCode": "increased_life_t3",
        "modGroup": "IncreasedLife",
        "rolledValues": [95],
        "isFractured": false
      }
    ],
    "suffixSlots": [
      {
        "definitionId": "...",
        "definitionCode": "fire_resistance_t2",
        "modGroup": "FireResistance",
        "rolledValues": [38],
        "isFractured": true
      }
    ],
    "enchantSlots": [],
    "influences": ["shaper"],
    "states": {
      "isCorrupted": false,
      "isMirrored": false,
      "isSplit": false,
      "isIdentified": true,
      "isSynthesized": false
    },
    "quality": 20,
    "computedStats": null
  }
}
```

**Convention rules**:
1. The `affixes` key is always present on affix-managed items (null on items without affixes)
2. `version` enables forward migration when the schema evolves
3. `definitionCode` is denormalized for read efficiency (avoids definition lookups for display)
4. `modGroup` is denormalized for validation efficiency (avoids definition lookups for exclusivity checks)
5. `computedStats` is null until explicitly computed via the `/affix/compute-stats` endpoint; consumers must not assume it's current (cache TTL applies)
6. Slot arrays may have gaps (e.g., a magic item with one prefix has `prefixSlots.length == 1`, not an array padded with nulls)

---

## Dependencies (What This Plugin Relies On)

### Hard Dependencies (constructor injection -- crash if missing)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Affix definitions (MySQL), implicit mappings (MySQL), pool cache (Redis), definition cache (Redis), distributed locks (Redis) |
| lib-state (`IDistributedLockProvider`) | Distributed locks for definition mutations and item affix modification |
| lib-messaging (`IMessageBus`) | Publishing affix lifecycle events, application events, state change events, error events |
| lib-item (`IItemClient`) | Reading item instances for affix operations, writing affix metadata via `ModifyItemInstanceAsync`, template lookups for item class resolution (L2) |
| lib-game-service (`IGameServiceClient`) | Validating game service existence for definition scoping (L2) |

### Soft Dependencies (runtime resolution via `IServiceProvider` -- graceful degradation)

| Dependency | Usage | Behavior When Missing |
|------------|-------|-----------------------|
| lib-inventory (`IInventoryClient`) | Equipment slot queries for variable provider (determining which items are equipped) | Variable provider returns empty results; stat computation still works per-item |
| lib-analytics (`IAnalyticsClient`) | Publishing affix generation statistics for economy monitoring | Statistics not collected; generation works normally |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-craft (L4, future) | Calls affix application, removal, rerolling, and pool generation endpoints for crafting workflows |
| lib-loot (L4, future) | Calls pool generation and batch application endpoints for loot drop creation |
| lib-market (L4, future) | Reads affix metadata convention on items for search filtering; may call stat computation for price estimation |
| lib-status (L4) | Reads `states.isCorrupted` from affix metadata to determine if corruption-based statuses apply |
| NPC Actor runtime (L2, via Variable Provider) | `${affix.*}` variables for item evaluation in GOAP economic/combat decisions |

---

## State Storage

### Affix Definition Store
**Store**: `affix-definitions` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `def:{definitionId}` | `AffixDefinitionModel` | Primary lookup by definition ID |
| `def-code:{gameServiceId}:{code}` | `AffixDefinitionModel` | Code-uniqueness lookup within game service |

Paginated queries by gameServiceId + optional filters (slotType, modGroup, category, tags, tier, influence) use `IJsonQueryableStateStore<AffixDefinitionModel>.JsonQueryPagedAsync()`.

### Implicit Mapping Store
**Store**: `affix-implicit-mappings` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `impl:{mappingId}` | `ImplicitMappingModel` | Maps item template code to implicit affix definitions with roll ranges |
| `impl-tpl:{gameServiceId}:{itemTemplateCode}` | `ImplicitMappingModel` | Lookup implicits for an item template code |

### Pool Cache
**Store**: `affix-pool-cache` (Backend: Redis, prefix: `affix:pool`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `pool:{gameServiceId}:{itemClass}:{slotType}:{ilvlBucket}` | `CachedAffixPool` | Pre-computed affix pool with weights for fast generation |
| `pool-inf:{gameServiceId}:{itemClass}:{slotType}:{ilvlBucket}:{influenceKey}` | `CachedAffixPool` | Influence-specific pool extension |

**TTL**: `PoolCacheTtlSeconds` (default: 3600, 1 hour). Invalidated on definition create/update/deprecate.

**Item level bucketing**: Item levels are bucketed into ranges (e.g., 1-10, 11-20, ..., 81-90, 91-100) to limit the number of cache entries. Within a bucket, the pool contains all definitions valid for the bucket's upper bound. Generation with a specific item level filters the cached pool at selection time (fast in-memory filter).

### Definition Cache
**Store**: `affix-definition-cache` (Backend: Redis, prefix: `affix:def`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `def:{definitionId}` | `AffixDefinitionModel` | Definition hot cache (read-through from MySQL) |
| `def-group:{gameServiceId}:{modGroup}` | `List<AffixDefinitionModel>` | All definitions in a mod group (for tier listing, exclusivity validation) |

**TTL**: `DefinitionCacheTtlSeconds` (default: 3600, 1 hour).

### Distributed Locks
**Store**: `affix-lock` (Backend: Redis, prefix: `affix:lock`)

| Key Pattern | Purpose |
|-------------|---------|
| `def:{definitionId}` | Definition mutation lock |
| `item:{itemInstanceId}` | Item affix modification lock (prevents concurrent affix operations on the same item) |
| `pool-rebuild:{gameServiceId}` | Pool cache rebuild lock (singleton rebuild on invalidation) |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `affix-definition.created` | `AffixDefinitionCreatedEvent` | Affix definition created (lifecycle) |
| `affix-definition.updated` | `AffixDefinitionUpdatedEvent` | Affix definition updated (lifecycle); includes `ChangedFields` |
| `affix-definition.deprecated` | `AffixDefinitionDeprecatedEvent` | Affix definition deprecated (lifecycle) |
| `affix.applied` | `AffixAppliedEvent` | Affix added to an item instance |
| `affix.removed` | `AffixRemovedEvent` | Affix removed from an item instance |
| `affix.rerolled` | `AffixRerolledEvent` | Affix values rerolled (same definition, new rolled values) |
| `affix.generated` | `AffixBatchGeneratedEvent` | Batch event for generated affix sets (deduped by source, configurable window) |
| `item-rarity.changed` | `ItemRarityChangedEvent` | Effective rarity changed (normal -> magic -> rare) |

### Consumed Events

| Topic | Handler | Action |
|-------|---------|--------|
| `item-template.created` | `HandleItemTemplateCreated` | Check if the new template has implicit mappings defined; warm pool cache for the template's category |
| `item-template.deprecated` | `HandleItemTemplateDeprecated` | Invalidate pool cache entries referencing the deprecated template's category |

### Resource Cleanup (T28)

| Target Resource | Source Type | On Delete | Cleanup Endpoint |
|----------------|-------------|-----------|-----------------|
| game-service | affix | CASCADE | `/affix/cleanup-by-game-service` |

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `DefinitionCacheTtlSeconds` | `AFFIX_DEFINITION_CACHE_TTL_SECONDS` | `3600` | Definition cache TTL (1 hour) |
| `PoolCacheTtlSeconds` | `AFFIX_POOL_CACHE_TTL_SECONDS` | `3600` | Pre-computed pool cache TTL (1 hour) |
| `ItemLevelBucketSize` | `AFFIX_ITEM_LEVEL_BUCKET_SIZE` | `10` | Item level range per cache bucket |
| `MaxItemLevel` | `AFFIX_MAX_ITEM_LEVEL` | `100` | Maximum supported item level (determines bucket count) |
| `DefaultSpawnWeight` | `AFFIX_DEFAULT_SPAWN_WEIGHT` | `1000` | Default spawn weight for new definitions |
| `DefaultMaxPrefixes` | `AFFIX_DEFAULT_MAX_PREFIXES` | `3` | Default max prefix slots for rare rarity |
| `DefaultMaxSuffixes` | `AFFIX_DEFAULT_MAX_SUFFIXES` | `3` | Default max suffix slots for rare rarity |
| `GenerationEventDeduplicationWindowSeconds` | `AFFIX_GENERATION_EVENT_DEDUPLICATION_WINDOW_SECONDS` | `60` | Deduplication window for batched generation events |
| `GenerationEventBatchMaxSize` | `AFFIX_GENERATION_EVENT_BATCH_MAX_SIZE` | `100` | Max records per batched generation event |
| `LockTimeoutSeconds` | `AFFIX_LOCK_TIMEOUT_SECONDS` | `30` | Distributed lock timeout |
| `ComputedStatsCacheTtlSeconds` | `AFFIX_COMPUTED_STATS_CACHE_TTL_SECONDS` | `60` | TTL for cached computed item stats (short -- affixes change frequently) |
| `MaxDefinitionsPerGameService` | `AFFIX_MAX_DEFINITIONS_PER_GAME_SERVICE` | `5000` | Safety limit for definition count per game service |
| `MaxAffixesPerItem` | `AFFIX_MAX_AFFIXES_PER_ITEM` | `12` | Hard cap on total affixes per item (across all slot types) |

---

## DI Services & Helpers

| Service | Role |
|---------|------|
| `ILogger<AffixService>` | Structured logging |
| `AffixServiceConfiguration` | Typed configuration access |
| `IStateStoreFactory` | State store access (creates 5 stores) |
| `IMessageBus` | Event publishing |
| `IDistributedLockProvider` | Distributed lock acquisition (L0) |
| `IItemClient` | Item instance read/write, template lookups (L2 hard) |
| `IGameServiceClient` | Game service existence validation (L2 hard) |
| `IServiceProvider` | Runtime resolution of soft L4 dependencies |

### Variable Provider Factories

| Factory | Namespace | Data Source | Registration |
|---------|-----------|-------------|--------------|
| `AffixItemEvaluationProviderFactory` | `${affix.*}` | Reads equipped items (via `IInventoryClient` soft), parses affix metadata, computes aggregate power scores and modifier presence | `IVariableProviderFactory` (DI singleton) |

**Variables exposed**:

| Variable | Type | Description |
|----------|------|-------------|
| `${affix.equipped_power_score}` | float | Aggregate power score from all equipped item affixes (normalized 0-1) |
| `${affix.best_rarity}` | string | Highest effective rarity among equipped items |
| `${affix.has_modifier.<tag>}` | bool | Whether any equipped item has an affix with the given generation tag |
| `${affix.weakest_slot}` | string | Equipment slot with the lowest affix power score (guides upgrade decisions) |
| `${affix.item_value_estimate}` | float | Estimated trade value based on affix quality (for NPC economic GOAP) |
| `${affix.has_open_slots}` | bool | Whether any equipped item has open affix slots |

---

## API Endpoints (Implementation Notes)

### Definition Management (7 endpoints)

All endpoints require `developer` role.

- **CreateDefinition** (`/affix/definition/create`): Validates game service existence. Validates code uniqueness per game service. Enforces `MaxDefinitionsPerGameService`. Validates `statGrants` has at least one entry. Saves to MySQL with dual keys (ID + code). Populates definition cache. Invalidates pool cache for affected item classes. Publishes `affix-definition.created`.

- **GetDefinition** (`/affix/definition/get`): Cache read-through (Redis -> MySQL -> populate cache). Supports lookup by definitionId or by gameServiceId + code.

- **ListDefinitions** (`/affix/definition/list`): Paged JSON query with required gameServiceId filter. Optional filters: slotType, modGroup, category, tags (any match), tier range, requiredInfluence, isActive. Sorted by modGroup then tier ascending.

- **UpdateDefinition** (`/affix/definition/update`): Acquires distributed lock. Partial update -- only non-null fields applied. **Cannot change**: code, gameServiceId, slotType, modGroup (these are identity-level properties). Invalidates definition and pool caches. Publishes `affix-definition.updated` with `changedFields`.

- **DeprecateDefinition** (`/affix/definition/deprecate`): Marks inactive. Optional `migrationTargetCode` for directing future generation to a replacement. Existing items with this affix are unaffected. Invalidates caches. Publishes `affix-definition.deprecated`.

- **SeedDefinitions** (`/affix/definition/seed`): Bulk creation, skipping definitions whose code already exists (idempotent). Validates game service once. Returns created/skipped counts. Invalidates pool cache once at end.

- **ListModGroups** (`/affix/definition/list-mod-groups`): Returns distinct mod group codes for a game service with definition counts per group. Useful for crafting UI and NPC decision-making.

### Implicit Mapping Management (4 endpoints)

All endpoints require `developer` role.

- **CreateImplicitMapping** (`/affix/implicit/create`): Maps an item template code to a list of implicit affix definition IDs with optional roll range overrides. Validates all referenced definitions exist and have `slotType: "implicit"`. Saves to MySQL.

- **GetImplicitMapping** (`/affix/implicit/get`): Lookup by gameServiceId + itemTemplateCode.

- **SeedImplicitMappings** (`/affix/implicit/seed`): Bulk creation for implicit mappings. Skips existing. Returns created/skipped counts.

- **RollImplicits** (`/affix/implicit/roll`): Takes gameServiceId + itemTemplateCode, looks up mapping, rolls values for each implicit definition according to stat grant ranges. Returns the rolled implicit slot data ready to be written to affix metadata. Does NOT modify any item -- the caller (lib-loot, lib-craft) writes the result.

### Affix Application (3 endpoints)

All endpoints require `developer` role. These are **validated primitives** -- they enforce mod group rules, slot limits, and item state checks. Higher-level operations that compose these primitives (reroll-all, fracture, corruption, identification) belong in lib-craft. See [CRAFT.md](CRAFT.md).

- **ApplyAffix** (`/affix/apply`): The core modification operation. Acquires item lock. Loads item, reads current affix metadata. Validates: not corrupted, not mirrored, slot type has capacity, mod group not occupied (unless replacing), item class valid for definition, item level sufficient. Rolls values from definition stat grants. Writes updated metadata via `IItemClient.ModifyItemInstanceAsync`. Updates effective rarity if needed. Publishes `affix.applied`, optionally `item-rarity.changed`.

- **RemoveAffix** (`/affix/remove`): Acquires item lock. Validates: not corrupted, not mirrored, target affix not fractured. Removes affix from the appropriate slot array. Recalculates effective rarity. Writes updated metadata. Publishes `affix.removed`, optionally `item-rarity.changed`.

- **RerollValues** (`/affix/reroll-values`): Acquires item lock. Validates: not corrupted, not mirrored. Re-rolls values for a specific affix on the item (same definition, new random values within stat grant ranges). Writes updated metadata. Publishes `affix.rerolled`.

### Generation (3 endpoints)

All endpoints require `developer` role.

- **GenerateAffixPool** (`/affix/generate/pool`): Returns the valid affix pool for a given context (gameServiceId, itemClass, itemLevel, slotType, existingModGroups, influences, externalWeightModifiers). Uses cached pool with in-memory filtering. Response includes each definition's effective weight and stat grant ranges. Used by crafting UI for "preview possible outcomes" and by lib-craft for pool-based operations.

- **GenerateAffixSet** (`/affix/generate/set`): Generates a complete affix set for a new item. Takes: gameServiceId, itemClass, itemLevel, targetRarity, influences, externalWeightModifiers. Rolls appropriate number of prefixes and suffixes for the rarity. Also rolls implicits from the implicit mapping. Returns the complete affix metadata JSON ready to be written. Does NOT modify any item -- the caller writes the result.

- **BatchGenerateAffixSets** (`/affix/generate/batch`): Generates affix sets for multiple items in one call. Used by lib-loot for batch loot generation. Each item in the batch specifies its own context (class, level, rarity, influences). Records batch generation events for analytics (deduped by source).

### Query (4 endpoints)

- **GetItemAffixes** (`/affix/item/get`): Loads item instance, parses affix metadata, enriches with full definition data (display names, tier info, mod group). Returns structured affix information for display.

- **ComputeItemStats** (`/affix/item/compute-stats`): Loads item instance and template. Computes aggregate stats: base stats (from template) + implicit values + explicit affix values + quality modifier. Returns a map of stat codes to computed values. Optionally caches the result (TTL: `ComputedStatsCacheTtlSeconds`).

- **CompareItems** (`/affix/item/compare`): Takes two item instance IDs. Loads both, computes stats for each, returns a diff showing which stats are higher/lower/equal. Used by NPC GOAP for equipment upgrade decisions.

- **EstimateItemValue** (`/affix/item/estimate-value`): Heuristic value estimation based on affix quality: tier levels, rolled value percentiles (how close to max roll), rarity, influence count, mod group synergies. Returns a normalized score (0-1) and a suggested currency value. Used by NPC GOAP for economic decisions and by lib-market for price guidance.

### Cleanup (1 endpoint)

- **CleanupByGameService** (`/affix/cleanup-by-game-service`): Deletes all definitions, implicit mappings, and cached pools for a game service. Called by lib-resource on game service deletion.

---

## Visual Aid

```
+-----------------------------------------------------------------------+
|                      Affix Service Architecture                        |
|                                                                        |
|   DEFINITION LAYER (owned by lib-affix, MySQL + Redis cache)           |
|   +------------------------------------------------------------------+|
|   |  AffixDefinition                                                  ||
|   |  +--------------------+  +--------------------+                   ||
|   |  | code: "life_t1"    |  | code: "fire_res_t2"|                   ||
|   |  | slotType: "prefix" |  | slotType: "suffix"  |                   ||
|   |  | modGroup:          |  | modGroup:           |                   ||
|   |  |   "IncreasedLife"  |  |   "FireResistance"  |                   ||
|   |  | tier: 1            |  | tier: 2              |                   ||
|   |  | statGrants:        |  | statGrants:          |                   ||
|   |  |   life: 110-119    |  |   fire_res: 34-41    |                   ||
|   |  | spawnWeight: 200   |  | spawnWeight: 1000    |                   ||
|   |  | reqItemLevel: 86   |  | reqItemLevel: 48     |                   ||
|   |  +--------------------+  +--------------------+                   ||
|   +------------------------------------------------------------------+|
|            |                                                           |
|            | GenerateAffixPool / GenerateAffixSet                      |
|            v                                                           |
|   GENERATION ENGINE                                                    |
|   +------------------------------------------------------------------+|
|   |  1. Load cached pool (Redis, keyed by class+level+slot)          ||
|   |  2. Filter: mod group exclusivity, item level, influences        ||
|   |  3. Apply external weight modifiers (passed by caller)           ||
|   |  4. Weighted random selection                                    ||
|   |  5. Roll values within stat grant ranges                         ||
|   |  6. Return ready-to-write affix data                             ||
|   +------------------------------------------------------------------+|
|            |                                                           |
|            | ApplyAffix / item metadata write                          |
|            v                                                           |
|   ITEM STORAGE (owned by lib-item L2, written by lib-affix)           |
|   +------------------------------------------------------------------+|
|   |  ItemInstance.instanceMetadata.affixes                            ||
|   |  {                                                                ||
|   |    "version": 1,                                                  ||
|   |    "effectiveRarity": "rare",                                     ||
|   |    "itemLevel": 75,                                               ||
|   |    "prefixSlots": [{...}, {...}, {...}],                          ||
|   |    "suffixSlots": [{...}, {...}],                                 ||
|   |    "implicitSlots": [{...}],                                      ||
|   |    "influences": ["shaper"],                                      ||
|   |    "states": { "isCorrupted": false, ... },                       ||
|   |    "quality": 20                                                  ||
|   |  }                                                                ||
|   +------------------------------------------------------------------+|
|            |                                                           |
|            | Read by consumers (documented convention, no import)       |
|            v                                                           |
|   +-------------------+  +-------------------+  +-------------------+ |
|   | lib-craft (L4)    |  | lib-loot (L4)     |  | lib-market (L4)   | |
|   | Transforms affixes|  | Creates affixed   |  | Indexes affixes   | |
|   | via lib-affix API |  | items in bulk     |  | for search/trade  | |
|   +-------------------+  +-------------------+  +-------------------+ |
|            |                                                           |
|            | Variable Provider Factory                                 |
|            v                                                           |
|   NPC GOAP (via Actor L2)                                             |
|   +------------------------------------------------------------------+|
|   |  ${affix.equipped_power_score}  -- "My gear is weak"             ||
|   |  ${affix.has_modifier.fire}     -- "I lack fire resistance"      ||
|   |  ${affix.item_value_estimate}   -- "This drop is valuable"       ||
|   |  ${affix.has_open_slots}        -- "This item could be stronger" ||
|   +------------------------------------------------------------------+|
+-----------------------------------------------------------------------+


Affix Generation Pipeline (Detail)
====================================

  GenerateAffixSet(itemClass="body_armour", iLvl=80, rarity="rare",
                    influences=["shaper"])
       |
       +-- RollImplicits("body_armour_astral_plate")
       |      |
       |      +-- Lookup implicit mapping
       |      +-- Roll values: [+12% all elemental resistance]
       |
       +-- GeneratePrefixes(count=3)
       |      |
       |      +-- Pool: all valid prefixes for body_armour, iLvl 80, shaper
       |      |
       |      +-- Roll 1: "IncreasedLife" T3 (weight 400) -> +95 life
       |      +-- Roll 2: "IncreasedES" T4 (weight 600) -> +72 energy shield
       |      +-- Roll 3: "ShaperNearbyRes" T1 (weight 50) -> -9% fire res
       |      |            (shaper-exclusive, very rare)
       |
       +-- GenerateSuffixes(count=3)
       |      |
       |      +-- Pool: all valid suffixes for body_armour, iLvl 80, shaper
       |      |
       |      +-- Roll 1: "FireResistance" T2 -> +38%
       |      +-- Roll 2: "AttackSpeed" T5 -> +6%
       |      +-- Roll 3: "CritChance" T3 -> +22%
       |
       +-- Assemble metadata JSON
       |
       +-- Return (ready to write to ItemInstance.instanceMetadata)


Mod Group Exclusivity Visualization
======================================

  Item: Rare Ring (iLvl 80, no influences)

  Prefix Slots (max 3):
  +--------------------+--------------------+--------------------+
  | Slot 1             | Slot 2             | Slot 3             |
  | IncreasedLife T3   | AddedPhysDmg T4    | (empty)            |
  | +95 life           | +12-22 phys        |                    |
  +--------------------+--------------------+--------------------+

  Suffix Slots (max 3):
  +--------------------+--------------------+--------------------+
  | Slot 1             | Slot 2             | Slot 3             |
  | FireResistance T2  | ColdResistance T3  | AttackSpeed T5     |
  | +38% fire res      | +29% cold res      | +6% attack speed   |
  | [FRACTURED]        |                    |                    |
  +--------------------+--------------------+--------------------+

  Blocked mod groups: {IncreasedLife, AddedPhysDmg, FireResistance,
                       ColdResistance, AttackSpeed}

  -> Cannot add: ANY other IncreasedLife tier (T1, T2, T4...)
  -> Cannot add: ANY other FireResistance tier
  -> CAN add: LightningResistance (different mod group)
  -> CAN add: IncreasedMana (different mod group)
  -> BUT: no open prefix slots (3/3 used)
  -> SO: can only add via suffix to slot 3... wait, slot 3 is taken
  -> RESULT: item is full (6/6 affixes). Must remove one to add another.
  -> EXCEPT: FireResistance T2 is FRACTURED -- cannot be removed.
```

---

## Stubs & Unimplemented Features

**Everything is unimplemented.** This is a pre-implementation architectural specification. No schema, no generated code, no service implementation exists. The following phases are planned:

### Phase 0: Foundation Prerequisites
- **#407 (Item Decay/Expiration)**: Not a direct blocker, but lib-status needs it and lib-affix consumers (crafting) benefit from item TTL support
- **Affix metadata convention documentation**: Publish the JSON schema for `instanceMetadata.affixes` so other services can begin designing against it

### Phase 1: Definition Infrastructure
- Create affix-api.yaml schema with all endpoints
- Create affix-events.yaml schema
- Create affix-configuration.yaml schema
- Generate service code
- Implement definition CRUD (create, get, list, update, deprecate, seed, list-mod-groups)
- Implement implicit mapping CRUD (create, get, seed)
- Implement pool cache warming and invalidation

### Phase 2: Generation Engine
- Implement `GenerateAffixPool` with cached pool lookup and in-memory filtering
- Implement `RollImplicits` for implicit value generation
- Implement `GenerateAffixSet` for complete affix set generation
- Implement `BatchGenerateAffixSets` for loot generation at scale
- Performance testing at 100K NPC item evaluation scale

### Phase 3: Affix Application
- Implement `ApplyAffix` with full validation (mod groups, slot limits, item states)
- Implement `RemoveAffix` with state flag checks
- Implement `RerollValues` for value re-randomization
- Implement effective rarity tracking and transitions

### Phase 4: Query and Computation
- Implement `GetItemAffixes` with definition enrichment
- Implement `ComputeItemStats` for aggregate stat computation
- Implement `CompareItems` for NPC GOAP equipment evaluation
- Implement `EstimateItemValue` for NPC economic decisions
- Implement variable provider factory (`AffixItemEvaluationProviderFactory`)

### Phase 5: Integration and Events
- Implement event handlers for item template lifecycle events
- Implement batched generation event publishing with deduplication
- Implement resource cleanup endpoint
- Wire analytics integration (soft dependency)
- Integration testing with lib-item

---

## Potential Extensions

1. **Rarity slot limit definitions as configurable entities**: Instead of hardcoded slot counts per rarity, expose a `RaritySlotLimit` definition API that games use to define their own rarity tiers with custom slot counts. A game could define "Legendary" rarity with 4 prefixes and 4 suffixes, or "Mythic" with unlimited slots but exponentially increasing generation cost.

2. **Affix trade index (materialized view)**: A denormalized MySQL store that indexes `(gameServiceId, statCode, statValue, itemInstanceId)` for fast trade-site-style queries ("find all items with +90 life and +30 fire res"). Built asynchronously from `affix.applied` and `affix.removed` events. Used by lib-market for search.

3. **Unique item definitions**: A `UniqueItemDefinition` entity that maps an item template code to a fixed set of affix definitions with predetermined roll ranges. When a unique item drops, its affixes come from the unique definition (not weighted random). The unique definition is owned by lib-affix, not lib-item.

4. **Client events**: `affix-client-events.yaml` for pushing real-time affix change notifications to connected WebSocket clients (affix applied, item identified, item corrupted).

5. **Synthesis implicit tables**: A mapping from combinations of sacrificed item affix properties to possible synthesis implicit outcomes. This is a complex system (PoE's synthesis was notoriously opaque) and may warrant its own endpoint set or even a sub-service.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

None. Plugin is aspirational — no code exists to have bugs.

### Intentional Quirks (Documented Behavior)

1. **Affix slot types are opaque strings, not enums**: "prefix", "suffix", "implicit", "enchant" are conventions, not enforced values. Games define their own slot types. lib-affix validates slot type consistency (an affix must be applied to a slot matching its `slotType`) but doesn't restrict which string values are valid. Slot limit configuration references slot types by string.

2. **Mod groups are per-game-service, not global**: Two games can have mod groups with the same name ("IncreasedLife") that are entirely independent. Mod group uniqueness is scoped to game service + item instance.

3. **Affix metadata is written via lib-item's ModifyItemInstance**: lib-affix does not have its own instance-level store. All affix data lives in `ItemInstance.instanceMetadata.affixes`. This means lib-item is the authoritative store for affix instance data. lib-affix only owns definitions (templates) and pre-computed pools.

4. **Computed stats are cached, not stored**: `ComputeItemStats` results are cached in Redis with short TTL. They're not written to the item's metadata because they're derived data that changes when definitions change (e.g., a definition tier value update should be reflected immediately). Consumers must not treat cached stats as authoritative -- they should call `ComputeItemStats` for fresh values when precision matters.

5. **Effective rarity is advisory, not enforced by lib-item**: The `effectiveRarity` field in affix metadata is maintained by lib-affix during apply/remove operations. lib-item doesn't know about it and doesn't enforce it. If a caller writes affix metadata directly (bypassing lib-affix), the rarity might not match the actual affix count.

6. **Pool cache uses item level bucketing**: To keep the number of cached pools manageable, item levels are bucketed (e.g., levels 71-80 share a pool). The cached pool contains all definitions valid for the bucket's upper bound. When generating for a specific level (e.g., 75), the cached pool is filtered in-memory to exclude definitions requiring a higher level. This means the cache is slightly larger than necessary (includes some definitions that will be filtered) but the in-memory filter is fast.

7. **Implicit mappings reference item template CODES, not IDs**: Implicit mappings use `itemTemplateCode` (the human-readable code string) rather than the template's GUID. This is deliberate: item templates may be recreated (deprecated + replaced) with different IDs but the same code. The implicit mapping should follow the code, not the ID.

8. **Fractured affixes block their mod group permanently**: A fractured affix cannot be removed, and its mod group is permanently occupied. If a player fractures a low-tier life mod, they can never have a higher-tier life mod on that item. This is intentional (fracturing is a powerful but irreversible commitment) and should be communicated clearly to players.

9. **Batch generation does not guarantee uniqueness**: `BatchGenerateAffixSets` generates each item independently. Two items in the same batch might get identical affix sets if the RNG produces the same sequence. This is statistically unlikely but not prevented. Consumers requiring unique results must deduplicate.

10. **Generation events are batched, not per-item**: To prevent event flooding during mass loot generation (dungeon clear, batch NPC crafting), generation events are deduped by source and published in batches. Individual `affix.applied` events still fire for each application through the application endpoints.

### Design Considerations (Requires Planning)

1. **Cross-service stat computation authority**: lib-affix can compute per-item stats, but aggregate "character total stats" requires knowing equipment slots (lib-inventory), active buffs (lib-status), seed capabilities (lib-seed), and more. The variable provider does a simplified version for GOAP. A proper equipment stat service (lib-equipment) would be the authoritative aggregation point. This is a future architectural decision.

2. **Affix data migration on definition changes**: If a definition's stat grant ranges change (e.g., T3 life changed from 90-99 to 85-94), existing items with that affix retain their old rolled values. There is no automatic re-computation or migration. lib-affix treats applied affixes as snapshots at application time. A "recompute" endpoint could be added for mass migration, but this is a significant operation (touching every item with that affix).

3. **Interaction with lib-item's customStats**: `instanceMetadata.affixes` is the convention for lib-affix data. But lib-item also has `customStats`. Games may use both (affix data in metadata, other custom data in customStats) or merge them. The convention should document how to avoid conflicts.

4. **Implicit mapping hot-reload**: When implicit mappings change (e.g., a base type's natural fire resistance range is adjusted), newly created items get the new values, but existing items are unaffected. There is no mechanism to "re-roll" existing items' implicits unless the caller explicitly calls the application endpoints.

5. **Influence application mechanism**: lib-affix tracks influences on items and gates affix pools by influence requirements, but the mechanism for GIVING an item an influence is external. In PoE, influences come from specific content (Shaper items drop from Shaper). In Arcadia, they might come from leyline exposure. lib-craft or loot systems write influence data to the affix metadata; lib-affix reads it during generation and application. The trigger is game-specific.

6. **Interaction with lib-save-load**: When saving game state, items carry their full affix metadata in `instanceMetadata`. lib-save-load persists this naturally. On load, no lib-affix interaction is needed (the data is self-contained). However, if affix definitions have changed between save and load (definition deprecation, tier rebalancing), the loaded items may reference outdated definitions. `ComputeItemStats` handles this gracefully (reads current definitions), but `GetItemAffixes` enrichment may show stale display names.

7. **Variable provider equipment detection**: The `AffixItemEvaluationProviderFactory` needs to know which items are "equipped" for a character. This requires querying lib-inventory for equipment-type containers and their contents. If lib-inventory is unavailable (soft dependency), the variable provider returns empty. The definition of "equipped" is game-specific (which container types count as equipment slots).

8. **Affix metadata convention and T29 compliance**: The current design stores affix instance data in `ItemInstance.instanceMetadata.affixes` and documents a convention for other services (lib-craft, lib-loot, lib-market) to read it by key name. This is the exact pattern FOUNDATION TENETS (T29: No Metadata Bag Contracts) forbids: "documenting convention as the interface contract between two services -- if it's not in a schema, it doesn't exist." The alternative is for lib-affix to own its own instance-level state store (e.g., `affix-instances` in MySQL, keyed by `itemInstanceId`) where it persists applied affixes in its own typed schema. Other services would query lib-affix's API rather than parsing metadata blobs from lib-item responses. This would be a significant architectural change from the current design and needs resolution before implementation begins. The "items in inventories" pattern used by lib-status and lib-collection (which use actual Item/Inventory primitives, not metadata bags) may offer a compliant alternative model.

---

## Work Tracking

*No active work items. Plugin is in pre-implementation phase. See [Economy System Guide](../guides/ECONOMY-SYSTEM.md) for the cross-cutting economy architecture.*
