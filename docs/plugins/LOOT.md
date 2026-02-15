# Loot Plugin Deep Dive

> **Plugin**: lib-loot (not yet created)
> **Schema**: `schemas/loot-api.yaml` (not yet created)
> **Version**: N/A (Pre-Implementation)
> **State Store**: loot-tables (MySQL), loot-table-cache (Redis), loot-contexts (Redis), loot-pity-counters (Redis), loot-history (MySQL), loot-lock (Redis) — all planned
> **Layer**: L4 GameFeatures
> **Status**: Aspirational — no schema, no generated code, no service implementation exists.

---

## Overview

Loot table management and generation service (L4 GameFeatures) for weighted drop determination, contextual modifier application, and group distribution orchestration. A thin orchestration layer (like Quest over Contract, Escrow over Currency/Item, Divine over Currency/Seed/Collection) that composes existing Bannou primitives to deliver loot acquisition mechanics.

**Composability**: Loot table definitions and generation rules are owned here. Item creation is lib-item (L2) -- loot generates item instances with metadata. Item placement is lib-inventory (L2) -- loot places generated items into containers. Modifier generation on drops is lib-affix (L4, soft) -- loot requests affix sets for items that should be randomly modified. Currency drops are lib-currency (L2) -- loot credits wallets directly. NPC looting behavior is Actor (L2) via the Variable Provider Factory pattern -- NPCs perceive lootable sources and decide whether to loot based on GOAP planning. Divine intervention in loot outcomes is lib-divine (L4, soft) -- gods can manipulate drop probabilities through narrative events without lib-loot knowing.

**The foundational distinction**: lib-loot manages WHAT can drop from sources (table definitions, weighted entries, probability curves) and orchestrates the HOW of generation (rolling, context application, instantiation). WHO triggers loot generation is external -- combat systems report kills, quest systems grant rewards, world events produce spoils, chest interactions trigger rolls. lib-loot is a pure generation engine: give it a table ID and a context, and it returns concrete items.

**Critical architectural insight**: Loot is not a reward -- it is a *consequence*. In Arcadia's living world, loot emerges from the simulation, not from a designer's reward spreadsheet. When a pneuma echo is destroyed in a dungeon, the logos seed disperses and the physical manifestation fragments into recoverable materials -- that is loot. When a divine actor decides a character deserves recognition, the divine blessing might manifest as a rare drop from the next monster they kill -- that is loot. When a merchant NPC's caravan is raided by bandits, the scattered goods become lootable -- that is loot. lib-loot provides the probabilistic generation engine; the simulation provides the meaning.

**The dual-table model**: Loot tables come in two flavors that serve different purposes. **Static tables** are designer-authored definitions seeded at deployment time -- the baseline drop rates for monster species, chest tiers, quest reward pools. **Dynamic tables** are runtime-constructed by actors and systems -- a divine actor composing a custom reward table for a blessed character, a dungeon core adjusting its treasure rooms based on intruder capabilities, an NPC merchant deciding what to stock by "looting" a supplier's catalog. Both models use the same LootTable structure; the distinction is in who creates them and when.

**Three generation tiers**: Not all loot requires the same computational effort. **Tier 1 (Lightweight)** generates item template references only -- "this source can drop iron ore, leather scraps, or wolf fangs." No instances created; the caller decides when and how to instantiate. Used for previews, bestiary entries, tooltip generation, and NPC GOAP evaluation at scale. **Tier 2 (Standard)** generates concrete item instances placed into a target container -- the normal loot drop flow. **Tier 3 (Enriched)** generates item instances with full affix sets, custom metadata, and quality modifiers -- the "rare drop" flow that coordinates with lib-affix for modifier generation. The tier is per-entry in the table, not per-table -- a single table can have Tier 1 common drops and Tier 3 legendary drops.

**NPC interaction with loot**: At 100K concurrent NPCs, loot generation must be efficient. NPCs interact with loot in three ways: as **sources** (NPC death triggers loot generation from their species table), as **claimants** (NPC GOAP evaluates whether to loot a nearby source based on need, greed, and personality), and as **evaluators** (NPC GOAP uses Tier 1 preview to assess whether a source is worth the effort). The Variable Provider Factory exposes `${loot.*}` variables for all three roles.

**The pity system as divine mechanism**: lib-loot tracks per-entity failure counters for configurable "pity" entries -- items guaranteed to drop after N failed rolls. But the pity system is deliberately minimal at the loot layer. In Arcadia, extended bad luck is a *narrative opportunity*: a divine actor observing a character's mounting frustration can intervene by temporarily modifying the character's loot context (boosting weight modifiers through a blessing), making the next drop feel earned rather than mechanically guaranteed. lib-loot's pity counter is the fallback for games without divine actors; Arcadia's gods make it nearly redundant by turning bad luck into story.

**Zero game-specific content**: lib-loot is a generic loot generation service. Arcadia's pneuma-based drop metaphysics, PoE-style deterministic crafting drops, Diablo-style legendary showers, or a simple "kill monster, get gold" system are all equally valid configurations. Table structures, entry weights, context modifiers, distribution modes, and pity thresholds are all opaque configuration defined per game at deployment time through table seeding.

**Current status**: Pre-implementation. No schema, no code. This deep dive is an architectural specification based on a Path of Exile item system complexity benchmark, the [Item & Economy Plugin Landscape](../plans/ITEM-ECONOMY-PLUGINS.md), and the broader architectural patterns established by lib-divine, lib-dungeon, lib-affix, and lib-market. Internal-only, never internet-facing.

---

## Why Not lib-item? (Architectural Rationale)

The question arises: should loot generation be an extension of lib-item? Items are what drop, after all.

**The answer is no -- loot is orchestration over items, not an item concern.**

| Concern | lib-item (L2) | lib-loot (L4) |
|---------|---------------|---------------|
| **What it stores** | Item templates, instances, quantities, binding, container references | Loot table definitions, weighted entry pools, context modifiers, pity counters, distribution rules |
| **What it knows** | "This item template exists with these properties" | "This monster drops items from this weighted pool, modified by these contextual factors, distributed with this fairness policy" |
| **Generation rules** | None (creates whatever instances callers request) | Weighted random selection, nested sub-tables, guaranteed drops, quantity curves, context-sensitive pool modification |
| **Consumers** | Every service that touches items | Combat systems, quest rewards, world events, treasure interactions, NPC looting behavior, divine intervention |
| **Scale concern** | Individual CRUD operations | Batch generation (dungeon clear produces 50+ drops in one tick), preview computation for 100K NPC evaluations |

lib-item is L2 (GameFoundation) because every game needs items. lib-loot is L4 (GameFeatures) because many games handle loot generation client-side or through game-server logic. A simple mobile game might call `/item/instance/create` directly; only games with complex loot tables, distribution mechanics, and NPC-driven economies need lib-loot.

**What they share is output, not logic**:
- lib-loot's generation engine produces `CreateItemInstanceRequest` payloads
- lib-loot calls lib-item to instantiate the generated items
- lib-item doesn't know or care that the item came from a loot roll

This is analogous to how lib-status and lib-license both use lib-item/lib-inventory for storage but own entirely different domain semantics.

---

## The Pneuma Echo Drop Model (Arcadia Game Integration)

> **This section describes Arcadia-specific lore.** lib-loot is game-agnostic; these mappings are applied through configuration, ABML behaviors, and narrative design -- not code.

In Arcadia's metaphysics, most dungeon creatures are pneuma echoes -- not truly alive. They are logos memory seeds given temporary physical form through dungeon mana. When a pneuma echo is destroyed, the pneuma body disperses and the logos seed fragments. What remains are:

- **Material fragments**: Physical residue from the pneuma manifestation. A fire elemental leaves behind charite crystite (crystallized fire pneuma). A skeletal warrior leaves bone meal and rusted iron. These are the "common drops" -- determined by species template and quality tier.

- **Logos impressions**: Faint copies of the logos seed's encoded knowledge. A scholarly skeleton might drop a fragment of the text it was manifesting. A guardian golem drops a piece of the defense pattern it embodied. These are the "uncommon drops" -- determined by the echo's specific logos composition, which varies per spawn.

- **Resonance artifacts**: When an echo is destroyed in a location with strong leyline activity, or when a divine actor is attending the region, the dying pneuma can crystallize around nearby logos patterns and form something entirely new -- an item that didn't exist in the echo's template. These are the "rare drops" -- emergent from context, not predetermined.

| Loot Mechanic | Arcadia Equivalent | Metaphysical Basis |
|---------------|--------------------|--------------------|
| **Common drops** | Material fragments | Physical residue from pneuma dispersal |
| **Uncommon drops** | Logos impressions | Knowledge encoded in the echo's seed |
| **Rare drops** | Resonance artifacts | Emergent crystallization from ambient pneuma + logos |
| **Currency drops** | Mana condensation | Raw pneuma that didn't attach to logos, usable as currency |
| **Quality tier** | Echo fidelity | How complete the logos seed was -- higher fidelity echoes leave richer fragments |
| **Loot modifier (luck)** | Logos attunement | Character's affinity for perceiving and attracting logos patterns |
| **Pity system** | Divine attention | A watching god notices the character's persistent effort and intervenes |
| **Loot table** | Species logos template | The inherited pattern of what a creature type can produce when dispersed |
| **Context modifier** | Leyline influence | Local pneuma density, divine attention, seasonal factors |
| **Group distribution** | Logos resonance | Items "want" to go to the entity whose logos pattern most resonates with them |

**NPC looting integration**: NPCs don't think in terms of "loot tables." An NPC blacksmith perceives scattered materials from a defeated echo and evaluates: "Those iron fragments would be useful for my current project" (`${loot.nearby_source_has.iron}` + `${craft.current_recipe_needs.iron}` -> GOAP action: loot). A scavenger NPC has different priorities: "That resonance artifact looks valuable at the market" (`${loot.nearby_value_estimate}` + `${personality.greed}` -> GOAP action: claim). lib-loot provides the data; the NPC's Actor brain provides the motivation.

---

## Core Concepts

### Loot Table Structure

A loot table is a hierarchical definition describing what can be generated from a source. Tables are composable -- entries can reference other tables (sub-tables), enabling inheritance and layered complexity without duplicating definitions.

```
LootTable:
  tableId: Guid
  gameServiceId: Guid          # Scoped per game service
  code: string                 # Unique within game service (e.g., "wolf_alpha_drops")

  # Classification
  category: string             # Broad type (e.g., "creature", "chest", "quest", "world_event")
  tags: [string]               # Searchable tags for organization

  # Roll Configuration
  rollCount: RollRange         # How many times to roll on this table
  rollMode: RollMode           # independent | sequential | pick_unique
  guaranteedEntries: [Guid]    # Entry IDs that always drop (bypass rolling)

  # Entries
  entries: [LootEntry]         # The weighted pool (see below)

  # Context Requirements
  requiredContextKeys: [string] # Context keys that MUST be provided for generation

  # Metadata
  isActive: bool
  isDeprecated: bool
  description: string
```

**Key design decisions**:

1. **Roll count is a range** (`{min: 1, max: 3}`), not a fixed number. The actual roll count is determined at generation time, influenced by context modifiers (party size, luck, source tier). This enables "more kills = more rolls" and "bigger party = more drops" without separate tables.

2. **Roll modes control duplicate behavior**: `independent` allows the same entry to be selected multiple times (3 rolls might all produce iron ore). `sequential` treats the table as an ordered list (first roll picks from full pool, second from remaining, etc.). `pick_unique` ensures no entry is selected twice per generation event.

3. **Guaranteed entries bypass rolling entirely**: They always appear in the output regardless of weight calculations. Used for quest items that must drop, material fragments that always result from destruction, and currency amounts that accompany every kill.

4. **Sub-table references enable composition**: A "wolf_alpha_drops" table might contain entries for common materials plus a reference to "rare_enchanted_items" sub-table. The sub-table has its own weights and can be shared across many parent tables.

### Loot Entries

Each entry in a table represents one possible outcome:

```
LootEntry:
  entryId: Guid
  tableId: Guid                # Parent table

  # What drops
  entryType: EntryType         # item | currency | sub_table | nothing
  itemTemplateId: Guid?        # For item entries
  itemTemplateCode: string?    # Alternative: resolve by code at generation time
  currencyDefinitionId: Guid?  # For currency entries
  subTableId: Guid?            # For sub-table references

  # Probability
  weight: int                  # Base weight for weighted random selection (1000 typical)
  weightTagModifiers: object   # Per-context-tag weight multipliers
  dropChance: decimal?         # Optional flat probability override (0.0-1.0)

  # Quantity
  quantity: QuantityRange      # {min: 1, max: 5} -- rolled uniformly
  quantityCurve: string?       # Optional curve type: "linear", "bell", "exponential_decay"

  # Generation Tier
  generationTier: int          # 1=lightweight (ref only), 2=standard (instance), 3=enriched (instance+affixes)

  # Affix Generation (Tier 3 only)
  affixContext: AffixContext?   # itemLevel range, rarity, influences, weight modifiers
  affixSetOverride: [Guid]?    # Fixed affix definitions (unique/legendary items)

  # Item Overrides
  itemOverrides: object?       # Partial overrides applied to created instances (customStats, metadata, quality)

  # Pity Configuration
  pityEnabled: bool            # Track failure counter for this entry
  pityThreshold: int?          # Guaranteed drop after N failures (e.g., 50)
  pityCounterScope: string     # "entity" (per character), "realm", "global"

  # Requirements
  requiredItemLevel: int?      # Minimum source level to appear in pool
  requiredContextTags: [string] # Context tags that must be present

  # Display
  displayName: string?         # For preview/tooltip (e.g., "Enchanted Weapon")
  displayRarity: string?       # For preview coloring
  isHidden: bool               # Excluded from preview endpoints (secret drops)
```

**Key design decisions**:

1. **Entry types allow heterogeneous pools**: A single table can drop items, currency, sub-table rolls, or "nothing" (weighted empty outcome that dilutes the pool). This models real loot: kill a wolf, get wolf hide (item) + 5 gold (currency) + maybe a rare gem (sub-table) + maybe nothing extra (nothing entry with high weight).

2. **Weight tag modifiers enable context sensitivity**: An entry with `weightTagModifiers: {"boss": 3.0, "normal": 0.5}` becomes 3x more likely from bosses and half as likely from normal enemies. Tags are provided by the caller in the generation context, not hardcoded in the entry. This keeps tables generic while allowing contextual variation.

3. **Drop chance vs. weight**: Most entries use `weight` for weighted random selection within the pool. `dropChance` is an optional override for entries that should have a flat independent probability regardless of pool composition -- "1% chance to drop this legendary, completely independent of other rolls." When `dropChance` is set, the entry is rolled separately (as a Bernoulli trial) before the weighted pool selection.

4. **Quantity curves shape distribution**: `linear` gives uniform distribution between min and max. `bell` (normal distribution centered on midpoint) makes average quantities most common. `exponential_decay` makes minimum quantities most common with rare large drops. Games tune the "feel" of loot through curves without changing the actual min/max.

5. **Affix context for Tier 3 generation**: When an entry is Tier 3 (enriched), the `affixContext` tells lib-affix how to generate modifiers. The `itemLevel` range is rolled from the source's level; the `rarity` might be overridden for legendary drops. This decouples loot table design from affix system details -- the table author says "generate a rare weapon with level 60-70 affixes" and lib-affix handles the rest.

### Generation Context

Every loot generation request includes a context object describing the circumstances of the drop:

```
LootGenerationContext:
  # Source identification
  sourceId: Guid               # What produced the loot (monster, chest, quest, event)
  sourceType: string           # "creature", "chest", "quest_reward", "world_event", "divine_gift"
  sourceLevel: int             # Level of the source (determines valid entries)

  # Claimant identification
  claimantId: Guid?            # Who is claiming the loot (for pity tracking, distribution)
  claimantType: string?        # "character", "party", "npc"
  claimantLevel: int?          # Level of the claimant (for level-scaling)

  # Modifier tags
  contextTags: [string]        # Tags that modify weights (e.g., "boss", "dungeon", "blessed", "first_kill")

  # Numeric modifiers
  luckModifier: decimal        # Multiplicative luck factor (1.0 = normal, 2.0 = double luck)
  quantityModifier: decimal    # Multiplicative quantity factor (1.0 = normal)
  qualityModifier: decimal     # Multiplicative quality factor (affects Tier 3 affix generation)

  # Distribution
  distributionMode: string?    # "personal" | "need_greed" | "round_robin" | "free_for_all"
  partyMembers: [PartyMember]? # For group distribution (entityId, entityType, level, role)

  # Target
  targetContainerId: Guid?     # Where to place generated items (null = return without placing)
  targetWalletId: Guid?        # Where to credit currency drops (null = return without crediting)

  # Overrides
  overrideWeightModifiers: object? # Additional weight modifiers applied on top of entry defaults
  forceEntryIds: [Guid]?       # Force specific entries to drop (testing, divine intervention)
```

**Key design decisions**:

1. **Modifiers are multiplicative, not additive**: `luckModifier: 1.5` means all weights for rare/uncommon entries are multiplied by 1.5. This compounds cleanly with entry-level `weightTagModifiers`. A boss (`boss` tag: 3.0x weight) killed by a lucky character (`luckModifier: 1.5`) yields 4.5x weight on applicable entries.

2. **Context tags are the primary customization mechanism**: Instead of building specific parameters for every possible modifier (region, weather, time of day, divine attention, party composition), tags provide an open-ended vocabulary. The game defines what tags mean; lib-loot applies them mechanically to weight modifiers.

3. **Force entry IDs enable divine intervention**: A divine actor can call the generation endpoint with `forceEntryIds` containing a specific rare drop, making it appear as part of the normal loot roll. From the player's perspective, the item dropped naturally. From the system's perspective, a god made it happen. This is the "guarantee NEXT drop" mechanism noted in the planning document.

4. **Distribution mode is per-generation, not per-table**: The same table (e.g., "boss_drops") might use personal loot for a solo kill and need/greed for a party kill. The caller decides based on the current game context.

### Distribution Modes

When loot is generated for a group, the distribution mode determines who gets what:

| Mode | Behavior | Use Case |
|------|----------|----------|
| **personal** | Each party member gets an independent roll on the table. Items are placed directly in their container. No contention. | Modern MMO personal loot. Every player sees their own drops. Scales linearly with party size. |
| **need_greed** | One set of loot is generated. Each item is offered to all eligible party members. Members declare "need" (I want this for use) or "greed" (I want this for profit) or "pass". Need > Greed > Pass. Ties broken randomly. | Traditional MMO group loot. Creates social dynamics around loot distribution. |
| **round_robin** | One set of loot is generated. Items are assigned to party members in rotation order. Each member gets roughly equal count. | Fair distribution without decision overhead. Good for farming groups. |
| **free_for_all** | One set of loot is generated and placed in a shared container. First to claim gets it. | Competitive loot. Creates urgency and conflict. Used for PvP scenarios and NPC scavenging. |
| **leader_assign** | One set of loot is generated. Party leader receives all items and distributes manually. | Guild raids, managed groups. Trust-based distribution. |

**Distribution is orchestration, not generation**: lib-loot generates the items first, then distributes them according to the mode. The generation step is identical regardless of mode. Distribution only affects WHERE the generated items are placed and WHO receives them.

**NPC distribution**: NPCs participate in distribution like any other entity. An NPC party member can "need" on items their GOAP evaluates as useful and "greed" on items they'd sell. Free-for-all distribution creates emergent competition between NPCs and players -- a scavenger NPC might grab loot before a slow player reaches it.

### Nested Sub-Tables

Tables can reference other tables as entries, creating hierarchical loot structures:

```
Table: "wolf_alpha_drops" (rollCount: {min: 2, max: 4})
  Entry: wolf_pelt         weight: 800   (common material)
  Entry: wolf_fang         weight: 600   (common material)
  Entry: raw_meat           weight: 400   (food drop)
  Entry: 5-15 gold          weight: 1000  (currency, always likely)
  Entry: -> "enchanted_items_t3" (sub-table) weight: 50 (rare)
  Entry: -> "crafting_reagents_uncommon" (sub-table) weight: 200

Table: "enchanted_items_t3" (rollCount: {min: 1, max: 1})
  Entry: enchanted_ring     weight: 300   generationTier: 3
  Entry: enchanted_amulet   weight: 300   generationTier: 3
  Entry: enchanted_weapon   weight: 200   generationTier: 3
  Entry: enchanted_armor    weight: 200   generationTier: 3
```

**Maximum nesting depth** is configurable (`MaxSubTableDepth`, default: 5) to prevent infinite recursion. Circular references are detected at table creation/update time and rejected.

**Sub-table weight inheritance**: When a sub-table is selected, its own `rollCount` and `rollMode` govern how many items it produces. The parent table's roll already "spent" one roll selecting the sub-table; the sub-table then independently generates its results.

---

## The Content Flywheel Connection

Loot is a critical node in the content flywheel described in VISION.md. Generated loot creates play history that becomes future content:

```
Character kills creature         Character dies
    |                                |
    v                                v
lib-loot generates drops     lib-resource compresses archive
    |                                |
    v                                |
Items enter economy                  |
(trade, craft, sell, use)            |
    |                                v
    v                          Storyline reads archive:
Analytics tracks item flow     "Character wielded Moonfire Blade,
    |                           a resonance artifact from
    v                           the Whispering Caverns"
NPC GOAP decisions:                  |
"Iron is scarce, I should           v
 mine more" --> supply chains   Regional Watcher orchestrates:
"This sword is valuable,       "Spawn quest: recover the
 I should sell it" --> trade    legendary blade from the
                                character's ghost"
                                     |
                                     v
                                New loot table entry:
                                "Moonfire Blade" as a
                                specific drop from the
                                ghost encounter -- seeded
                                by the original loot event
```

**lib-loot's role in the flywheel**: Every generated item has an `originType` and `originId` on the `ItemInstance` (set by lib-loot). This provenance chain enables Storyline to trace items back to their creation event. A legendary sword that dropped from a dungeon boss, was traded between NPCs, ended up in a hero's hands, and was buried with them when they died carries its entire history as metadata. When that character's compressed archive seeds a future quest, the sword's story becomes part of the narrative.

**Dynamic table creation from archives**: When lib-resource compresses a character's life into an archive, the items they possessed become potential future drops. A regional watcher (divine actor) processing the archive can create a dynamic loot table for the ghost/undead/legacy encounter that will represent this character in the future world. The table's entries reference the character's actual possessions -- not generic "ghost drops" but THIS character's sword, THIS character's ring, THIS character's accumulated gold.

---

## Dependencies (What This Plugin Relies On)

### Hard Dependencies (constructor injection -- crash if missing)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Loot table definitions (MySQL), table cache (Redis), generation contexts (Redis), pity counters (Redis), loot history (MySQL), distributed locks (Redis) |
| lib-state (`IDistributedLockProvider`) | Distributed locks for table mutations and pity counter updates |
| lib-messaging (`IMessageBus`) | Publishing loot generation events, distribution events, pity events, error events |
| lib-item (`IItemClient`) | Item template lookups for entry validation, item instance creation for Tier 2/3 generation (L2) |
| lib-inventory (`IInventoryClient`) | Placing generated items into target containers, creating temporary loot containers (L2) |
| lib-game-service (`IGameServiceClient`) | Validating game service existence for table scoping (L2) |
| lib-resource (`IResourceClient`) | Reference tracking, cleanup callback registration (L1) |

### Soft Dependencies (runtime resolution via `IServiceProvider` -- graceful degradation)

| Dependency | Usage | Behavior When Missing |
|------------|-------|-----------------------|
| lib-affix (`IAffixClient`) | Tier 3 generation: request affix set generation for enriched drops | Tier 3 entries fall back to Tier 2 (items created without affixes); warning logged. Template `customStats`/`instanceMetadata` still applied from `itemOverrides`. |
| lib-currency (`ICurrencyClient`) | Currency entry drops: credit wallets for gold/material currency drops | Currency entries skipped; warning logged. Items still generated normally. |
| lib-analytics (`IAnalyticsClient`) | Publishing loot generation statistics for economy monitoring and divine observation | Statistics not collected; generation works normally. |
| lib-character (`ICharacterClient`) | Claimant validation for pity counter scoping and distribution eligibility | Pity counters use entityId directly without validation; distribution skips eligibility checks. |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| *(none yet)* | Loot is a new L4 service with no current consumers. Future dependents: combat systems (trigger loot generation on kills), quest systems (trigger loot generation for rewards), dungeon systems (treasure room generation, boss drops), world events (spoils generation), NPC Actor behaviors (looting GOAP via Variable Provider), divine actors (loot context manipulation via force/blessing mechanics) |

---

## State Storage

### Loot Table Store
**Store**: `loot-tables` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `table:{tableId}` | `LootTableModel` | Primary lookup by table ID. Stores definition with all entries inline (denormalized for generation performance). |
| `table-code:{gameServiceId}:{code}` | `LootTableModel` | Code-uniqueness lookup within game service scope |

Paginated queries by gameServiceId + optional filters (category, tags, status) use `IJsonQueryableStateStore<LootTableModel>.JsonQueryPagedAsync()`.

### Table Cache
**Store**: `loot-table-cache` (Backend: Redis, prefix: `loot:tbl`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `tbl:{tableId}` | `LootTableModel` | Table hot cache (read-through from MySQL) |
| `tbl-code:{gameServiceId}:{code}` | `string` | Code-to-ID index for fast code resolution |

**TTL**: `TableCacheTtlSeconds` (default: 3600, 1 hour). Invalidated on table create/update/deprecate.

### Generation Context Store
**Store**: `loot-contexts` (Backend: Redis, prefix: `loot:ctx`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `ctx:{contextId}` | `ActiveLootContextModel` | Active loot container awaiting claiming (free-for-all, need/greed). TTL-based expiry for unclaimed loot. |

**TTL**: `UnclaimedLootTtlSeconds` (default: 300, 5 minutes). After TTL, unclaimed loot containers are destroyed and items are lost.

### Pity Counter Store
**Store**: `loot-pity-counters` (Backend: Redis, prefix: `loot:pity`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `pity:{scope}:{entityId}:{entryId}` | `int` | Failure counter for pity-enabled entries. Incremented on generation where this entry was eligible but not selected. Reset to 0 when the entry drops. |

**TTL**: `PityCounterTtlSeconds` (default: 604800, 7 days). Counters expire if the entity hasn't triggered the table in the configured window. This prevents stale counters from accumulating indefinitely.

### Loot History Store
**Store**: `loot-history` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `hist:{generationId}` | `LootGenerationHistoryModel` | Record of a complete generation event: table used, context provided, entries rolled, items created, currency credited, distribution outcomes. Used for analytics, debugging, and divine actor observation. |

Paginated queries by gameServiceId, sourceType, claimantId, timeRange use `IJsonQueryableStateStore<LootGenerationHistoryModel>.JsonQueryPagedAsync()`.

### Distributed Locks
**Store**: `loot-lock` (Backend: Redis, prefix: `loot:lock`)

| Key Pattern | Purpose |
|-------------|---------|
| `table:{tableId}` | Table mutation lock (create, update, deprecate) |
| `pity:{scope}:{entityId}` | Pity counter update lock (prevents concurrent counter races) |
| `ctx:{contextId}` | Loot context claim lock (prevents double-claiming in need/greed) |
| `history-prune` | History pruning background worker singleton lock |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `loot-table.created` | `LootTableCreatedEvent` | Table definition created (lifecycle) |
| `loot-table.updated` | `LootTableUpdatedEvent` | Table definition updated (lifecycle); includes `ChangedFields` |
| `loot-table.deprecated` | `LootTableDeprecatedEvent` | Table definition deprecated (lifecycle) |
| `loot.generated` | `LootGeneratedEvent` | Loot generated from a table (batch: includes all rolled entries, created items, currency amounts) |
| `loot.distributed` | `LootDistributedEvent` | Generated loot distributed to recipients (includes distribution mode and per-recipient assignments) |
| `loot.claimed` | `LootClaimedEvent` | Individual item claimed from a free-for-all or need/greed context |
| `loot.expired` | `LootExpiredEvent` | Unclaimed loot context TTL expired; items destroyed |
| `loot.pity-triggered` | `LootPityTriggeredEvent` | Pity counter reached threshold; guaranteed drop issued |
| `loot.pity-reset` | `LootPityResetEvent` | Pity counter reset (entry dropped naturally before threshold) |

### Consumed Events

| Topic | Handler | Action |
|-------|---------|--------|
| `item-template.deprecated` | `HandleItemTemplateDeprecatedAsync` | Scan tables for entries referencing the deprecated template; log warning. Do NOT automatically disable entries (the template's `migrationTargetId` may handle transition). |

### Resource Cleanup (T28)

| Target Resource | Source Type | On Delete | Cleanup Endpoint |
|----------------|-------------|-----------|-----------------|
| game-service | loot | CASCADE | `/loot/cleanup-by-game-service` |
| realm | loot | CASCADE | `/loot/cleanup-by-realm` |

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `TableCacheTtlSeconds` | `LOOT_TABLE_CACHE_TTL_SECONDS` | `3600` | Table definition cache TTL (1 hour) |
| `MaxSubTableDepth` | `LOOT_MAX_SUB_TABLE_DEPTH` | `5` | Maximum nesting depth for sub-table references |
| `MaxEntriesPerTable` | `LOOT_MAX_ENTRIES_PER_TABLE` | `200` | Safety limit for entries per table |
| `MaxTablesPerGameService` | `LOOT_MAX_TABLES_PER_GAME_SERVICE` | `10000` | Safety limit for table count per game service |
| `DefaultWeight` | `LOOT_DEFAULT_WEIGHT` | `1000` | Default weight for new entries |
| `DefaultGenerationTier` | `LOOT_DEFAULT_GENERATION_TIER` | `2` | Default generation tier for new entries |
| `DefaultRollCount` | `LOOT_DEFAULT_ROLL_COUNT` | `1` | Default roll count (both min and max) when not specified |
| `DefaultRollMode` | `LOOT_DEFAULT_ROLL_MODE` | `independent` | Default roll mode for new tables |
| `UnclaimedLootTtlSeconds` | `LOOT_UNCLAIMED_LOOT_TTL_SECONDS` | `300` | TTL for unclaimed loot containers (5 min) |
| `PityCounterTtlSeconds` | `LOOT_PITY_COUNTER_TTL_SECONDS` | `604800` | TTL for pity failure counters (7 days) |
| `GenerationEventBatchMaxSize` | `LOOT_GENERATION_EVENT_BATCH_MAX_SIZE` | `100` | Max items per batched generation event |
| `LootHistoryRetentionDays` | `LOOT_HISTORY_RETENTION_DAYS` | `30` | Days of generation history retained before pruning |
| `HistoryPruneIntervalSeconds` | `LOOT_HISTORY_PRUNE_INTERVAL_SECONDS` | `3600` | How often the history pruning worker runs (1 hour) |
| `MaxConcurrentGenerations` | `LOOT_MAX_CONCURRENT_GENERATIONS` | `50` | Max concurrent batch generation requests (backpressure) |
| `LockTimeoutSeconds` | `LOOT_LOCK_TIMEOUT_SECONDS` | `30` | Distributed lock timeout |
| `NeedGreedTimeoutSeconds` | `LOOT_NEED_GREED_TIMEOUT_SECONDS` | `60` | Time window for need/greed declarations before auto-greed |

---

## DI Services & Helpers

| Service | Role |
|---------|------|
| `ILogger<LootService>` | Structured logging |
| `LootServiceConfiguration` | Typed configuration access (17 properties) |
| `IStateStoreFactory` | State store access (creates 6 stores) |
| `IMessageBus` | Event publishing |
| `IDistributedLockProvider` | Distributed lock acquisition (L0) |
| `IItemClient` | Item template validation, instance creation (L2 hard) |
| `IInventoryClient` | Container placement for generated items (L2 hard) |
| `IGameServiceClient` | Game service validation (L2 hard) |
| `IResourceClient` | Reference tracking, cleanup callbacks (L1 hard) |
| `IServiceProvider` | Runtime resolution of soft L4 dependencies (Affix, Currency, Analytics, Character) |

### Background Workers

| Worker | Purpose | Interval Config | Lock Key |
|--------|---------|-----------------|----------|
| `LootHistoryPruneService` | Prunes generation history records older than `LootHistoryRetentionDays` | `HistoryPruneIntervalSeconds` (3600s) | `loot:lock:history-prune` |

### Variable Provider Factories

| Factory | Namespace | Data Source | Registration |
|---------|-----------|-------------|--------------|
| `LootSourceVariableProviderFactory` | `${loot.*}` | Nearby lootable sources (active loot contexts within perception range), source value estimates, item presence checks | `IVariableProviderFactory` (DI singleton) |

**`${loot.*}` variables** (character-scoped, for NPC GOAP):

| Variable | Type | Description |
|----------|------|-------------|
| `${loot.nearby_source_count}` | int | Number of active loot contexts within perception range |
| `${loot.nearby_source_has.{tag}}` | bool | Whether any nearby source contains items with the given tag (e.g., "iron", "weapon", "food") |
| `${loot.nearby_value_estimate}` | float | Estimated total trade value of nearby unclaimed loot (normalized 0-1) |
| `${loot.nearest_source_distance}` | float | Distance to nearest loot source (for movement GOAP) |
| `${loot.personal_pending_count}` | int | Number of personal loot items awaiting claiming (from recent kills/events) |
| `${loot.pity_progress.{tableCode}}` | float | Progress toward pity threshold for a specific table (0.0-1.0; 1.0 = guaranteed next drop) |

---

## API Endpoints (Implementation Notes)

### Table Management (7 endpoints)

All endpoints require `developer` role.

- **CreateTable** (`/loot/table/create`): Validates game service existence. Validates code uniqueness per game service. Enforces `MaxTablesPerGameService`. Validates entries: sub-table references exist and don't create cycles (BFS cycle detection), item template IDs or codes resolve, entry weights > 0. Saves to MySQL. Populates cache. Publishes `loot-table.created`.

- **GetTable** (`/loot/table/get`): Cache read-through (Redis -> MySQL -> populate cache). Supports lookup by tableId or by gameServiceId + code.

- **ListTables** (`/loot/table/list`): Paged JSON query with required gameServiceId filter. Optional filters: category, tags (any match), isActive. Returns table summaries (no entry details -- use GetTable for full definition).

- **UpdateTable** (`/loot/table/update`): Acquires distributed lock. Partial update. **Cannot change**: code, gameServiceId (identity-level). Entry additions/removals/modifications are part of the update payload. Re-validates cycle detection on sub-table changes. Invalidates cache. Publishes `loot-table.updated` with `changedFields`.

- **DeprecateTable** (`/loot/table/deprecate`): Marks inactive. Optional `migrationTargetCode` for directing future generation to a replacement table. Existing references from other tables (as sub-tables) are NOT automatically updated -- they continue referencing the deprecated table (which still functions but logs deprecation warnings during generation). Invalidates cache. Publishes `loot-table.deprecated`.

- **SeedTables** (`/loot/table/seed`): Bulk creation, skipping tables whose code already exists (idempotent). Validates game service once. Validates all sub-table references within the batch (entries can reference other tables in the same batch). Returns created/skipped counts. Populates cache for all created tables.

- **PreviewTable** (`/loot/table/preview`): Returns a human-readable breakdown of a table's structure: entries with display names, effective weights at a specified source level, probability percentages, sub-table expansion (recursive), and pity thresholds. Does NOT generate anything. Used for game design tooling, bestiary tooltips, and NPC GOAP planning (Tier 1 evaluation).

### Generation (4 endpoints)

- **Generate** (`/loot/generate`): The core generation endpoint. Takes tableId (or code) + `LootGenerationContext`. Executes the full generation pipeline (see detailed flow below). Returns `LootGenerationResult` containing: generated items (with instance IDs if Tier 2/3), currency amounts, distribution assignments (if party), generation ID for history lookup. Publishes `loot.generated`.

- **GenerateBatch** (`/loot/generate/batch`): Generates loot from multiple tables in one call. Each entry in the batch specifies its own table + context. Used for dungeon clears (10+ kills in one event tick), world event spoils, and NPC death cascades. Enforces `MaxConcurrentGenerations` backpressure. Returns per-table results.

- **GeneratePreview** (`/loot/generate/preview`): Tier 1 generation only. Takes tableId + context, returns item template references and probability-weighted value estimates WITHOUT creating instances. Used for NPC GOAP evaluation ("is this source worth looting?"), UI previews, and tooltip population. Does not consume pity counters or publish events.

- **ForceGenerate** (`/loot/generate/force`): Generates specific entries from a table, bypassing weighted selection. Takes tableId + entryIds + context. Used by divine intervention systems and admin tools. Marks history record with `wasForced: true`. Publishes `loot.generated` with `forced: true` flag.

### Generate Execution Flow (Detailed)

```
GenerateAsync(tableId, context)
    |
    +-- 1. Resolve table (cache -> MySQL)
    |       +-- 404 if not found or deprecated (unless deprecated is allowed)
    |
    +-- 2. Determine roll count
    |       +-- Base: table.rollCount (random in range)
    |       +-- Modified by: context.quantityModifier
    |       +-- Floor: always at least 1
    |
    +-- 3. Process guaranteed entries
    |       +-- For each: generate item/currency regardless of rolling
    |       +-- Pity counters NOT affected by guaranteed entries
    |
    +-- 4. Process drop-chance entries (independent Bernoulli trials)
    |       +-- For each entry with dropChance set:
    |       |   +-- Roll [0.0, 1.0)
    |       |   +-- Modified by: context.luckModifier (effective = dropChance * luckModifier)
    |       |   +-- Capped at 1.0 (can't exceed 100%)
    |       |   +-- If passes: generate, reset pity if tracked
    |       |   +-- If fails: increment pity counter if tracked
    |       |
    +-- 5. Process weighted pool (N rolls per rollCount)
    |       +-- Build effective pool:
    |       |   +-- Filter: entry.requiredItemLevel <= context.sourceLevel
    |       |   +-- Filter: entry.requiredContextTags subset of context.contextTags
    |       |   +-- Exclude: already-selected entries if rollMode=pick_unique
    |       |   +-- For each remaining:
    |       |       +-- effectiveWeight = entry.weight
    |       |       +-- Apply weightTagModifiers for matching context tags
    |       |       +-- Apply context.overrideWeightModifiers
    |       |       +-- Apply context.luckModifier (for entries tagged "rare"/"legendary")
    |       |       +-- If effectiveWeight <= 0: exclude
    |       |
    |       +-- Check pity thresholds:
    |       |   +-- For each pity-enabled entry in pool:
    |       |       +-- Load counter from Redis
    |       |       +-- If counter >= pityThreshold: force-select this entry
    |       |
    |       +-- Weighted random selection from pool
    |       +-- For each selected entry:
    |           +-- If entryType=nothing: skip (dilution entry)
    |           +-- If entryType=sub_table: recursively generate from sub-table
    |           +-- If entryType=item: generate item (see step 6)
    |           +-- If entryType=currency: generate currency (see step 7)
    |           +-- Update pity counters for all eligible-but-not-selected entries
    |           +-- Reset pity counter for selected entry
    |
    +-- 6. Generate item entries
    |       +-- Roll quantity from entry.quantity range using quantityCurve
    |       +-- Apply context.quantityModifier
    |       |
    |       +-- If generationTier=1: return template reference only
    |       +-- If generationTier=2: call IItemClient.CreateItemInstanceAsync
    |       |   +-- Apply entry.itemOverrides (customStats, metadata)
    |       |   +-- Set originType="loot", originId=generationId
    |       |   +-- Place in targetContainerId if provided
    |       +-- If generationTier=3: call IAffixClient (soft)
    |           +-- Generate affix set using entry.affixContext
    |           +-- If affixSetOverride: use fixed definitions instead
    |           +-- Write affix metadata to item instance
    |           +-- Fall back to Tier 2 if lib-affix unavailable
    |
    +-- 7. Generate currency entries
    |       +-- Roll quantity from entry.quantity range
    |       +-- Call ICurrencyClient.CreditAsync (soft)
    |       +-- If lib-currency unavailable: include in result but mark as uncredited
    |
    +-- 8. Distribution (if partyMembers provided)
    |       +-- personal: duplicate generation per member (each gets independent rolls)
    |       +-- need_greed: create loot context, await declarations
    |       +-- round_robin: assign items to members in rotation
    |       +-- free_for_all: create shared loot container with TTL
    |       +-- leader_assign: place all items in leader's container
    |       +-- Publish loot.distributed
    |
    +-- 9. Record history
    |       +-- Save LootGenerationHistoryModel to MySQL
    |       +-- Include: tableId, context, all rolled entries, all created items/currency
    |
    +-- 10. Return LootGenerationResult
            +-- generationId
            +-- items: [{instanceId, templateId, templateCode, quantity, generationTier, affixed}]
            +-- currency: [{currencyId, amount, credited}]
            +-- distributionMode, assignments (if group)
            +-- pityTriggered: [entryIds] (if any pity thresholds were hit)
```

### Distribution (3 endpoints)

- **DeclareNeedGreed** (`/loot/distribution/declare`): For need/greed distribution. Takes contextId, itemInstanceId, declaration (need/greed/pass). Validates context exists and hasn't expired. Validates declarer is in the party. Records declaration. When all party members have declared (or `NeedGreedTimeoutSeconds` expires), resolves winner and distributes. Publishes `loot.claimed` for each resolved item.

- **ClaimItem** (`/loot/distribution/claim`): For free-for-all distribution. Takes contextId, itemInstanceId. Acquires context lock. Validates context exists, item is unclaimed, claimant is eligible. Moves item from shared container to claimant's container. Publishes `loot.claimed`.

- **GetContext** (`/loot/distribution/context`): Returns active loot context details -- items available, declarations received, time remaining, distribution mode. Used by UI to display loot rolls and by NPC GOAP to evaluate claiming decisions.

### Query (3 endpoints)

- **GetGenerationHistory** (`/loot/history/get`): Load generation record by generationId. Returns full details: table used, context, entries rolled, items created.

- **ListGenerationHistory** (`/loot/history/list`): Paged query by gameServiceId, optional sourceType, claimantId, tableId, timeRange. Used for analytics dashboards and divine actor observation patterns.

- **GetDropRates** (`/loot/rates/get`): Computed endpoint. Takes tableId + context (without generating). Returns effective drop rates for all entries given the context's modifiers -- useful for game design validation ("what are the actual odds of this legendary dropping from this boss with this party composition?").

### Cleanup (2 endpoints)

Resource-managed cleanup via lib-resource (per FOUNDATION TENETS):

- **CleanupByGameService** (`/loot/cleanup-by-game-service`): Deletes all tables, pity counters, active contexts, and history for a game service.

- **CleanupByRealm** (`/loot/cleanup-by-realm`): Deletes realm-scoped pity counters and active contexts. Tables are game-service-scoped (not realm-scoped) and are unaffected.

---

## Visual Aid

```
+-----------------------------------------------------------------------+
|                    Loot Service Composability                            |
+-----------------------------------------------------------------------+
|                                                                        |
|  lib-loot (L4) -- "What drops and how it's distributed"                |
|  +------------------+  +------------------+  +------------------+     |
|  | LootTable        |  | Generation       |  | Distribution     |     |
|  | (what can drop,  |  | (weighted rolls, |  | (personal,       |     |
|  |  weights, tiers) |  |  affix gen,      |  |  need/greed,     |     |
|  |                  |  |  context mods)   |  |  free-for-all)   |     |
|  +--------+---------+  +--------+---------+  +--------+---------+     |
|           |                      |                      |              |
|           +----------+-----------+----------+-----------+              |
|                      |                      |                          |
|                      v                      v                          |
|  +-------------------------------------------------------------+     |
|  | Hard Dependencies (L0/L1/L2 -- constructor injection)         |     |
|  |                                                                |     |
|  |  Item ---------- template lookups, instance creation,          |     |
|  |                  origin tracking (originType="loot")           |     |
|  |  Inventory ----- placing items in containers (target,          |     |
|  |                  shared loot container for free-for-all)       |     |
|  |  Currency ------ crediting wallets for currency drops          |     |
|  |  Character ----- claimant validation for distribution          |     |
|  |  Resource ------ cleanup coordination on entity deletion       |     |
|  +-------------------------------------------------------------+     |
|           |                                                            |
|           v  soft dependencies (L4)                                    |
|  +-------------------------------------------------------------+     |
|  | Optional Features (L4, graceful degradation)                  |     |
|  |                                                                |     |
|  |  Affix --------- Tier 3 enriched drops (modifier generation)   |     |
|  |  Analytics ------ generation statistics, divine observation    |     |
|  +-------------------------------------------------------------+     |
|                                                                        |
|  Variable Provider Factory                                             |
|  +-------------------------------------------------------------+     |
|  | ${loot.nearby_source_count}    "3 loot piles nearby"          |     |
|  | ${loot.nearby_source_has.iron} "One has iron fragments"       |     |
|  | ${loot.nearby_value_estimate}  "About 50g total value"        |     |
|  | ${loot.pity_progress.wolf_rare} "72% to guaranteed rare"      |     |
|  +-------------------------------------------------------------+     |
|                                                                        |
|  Background Worker                                                     |
|  +-------------------------------------------------------------+     |
|  | LootHistoryPruneService -- prunes old generation records       |     |
|  +-------------------------------------------------------------+     |
+-----------------------------------------------------------------------+


Loot Generation Pipeline
==========================

  Generate("wolf_alpha_drops", context={sourceLevel:45, luck:1.2, tags:["boss"]})
       |
       +-- Resolve table (cache hit)
       |
       +-- Roll count: range(2,4), modifier 1.0 -> rolled 3
       |
       +-- Guaranteed entries:
       |      +-- 5 gold (currency) -> credit wallet
       |
       +-- Drop-chance entries:
       |      +-- "legendary_fang" (0.01 chance * 1.2 luck = 0.012)
       |      +-- Roll: 0.847 -> MISS
       |      +-- Increment pity counter (now 34/50)
       |
       +-- Weighted pool (3 rolls, independent mode):
       |      +-- Pool:
       |      |   wolf_pelt:    800 * boss_tag(0.5) = 400
       |      |   wolf_fang:    600 * boss_tag(1.0) = 600
       |      |   raw_meat:     400 * boss_tag(0.3) = 120
       |      |   -> enchanted_items_t3: 50 * boss_tag(5.0) * luck(1.2) = 300
       |      |   nothing:      200 * boss_tag(0.5) = 100
       |      |   Total pool weight: 1520
       |      |
       |      +-- Roll 1: 891 -> wolf_fang (Tier 2)
       |      |   +-- Quantity: range(1,3), bell curve -> 2
       |      |   +-- CreateItemInstance(wolf_fang_template, qty:2)
       |      |
       |      +-- Roll 2: 1340 -> enchanted_items_t3 (sub-table!)
       |      |   +-- Sub-table rolls: 1
       |      |   +-- Pool: ring(300), amulet(300), weapon(200), armor(200)
       |      |   +-- Roll: 580 -> enchanted_weapon (Tier 3!)
       |      |   +-- CreateItemInstance(weapon_template)
       |      |   +-- Call IAffixClient.GenerateAffixSet(
       |      |   |     class="weapon", iLvl=45, rarity="rare",
       |      |   |     qualityModifier=1.2)
       |      |   +-- Write affixes to item instance
       |      |
       |      +-- Roll 3: 234 -> wolf_pelt (Tier 2)
       |          +-- Quantity: range(1,2), linear -> 1
       |          +-- CreateItemInstance(wolf_pelt_template, qty:1)
       |
       +-- Distribution: personal (solo kill)
       |   +-- All items placed in targetContainerId
       |
       +-- Record history
       |
       +-- Result:
            items: [wolf_fang x2, enchanted_weapon (rare, affixed), wolf_pelt x1]
            currency: [5 gold]
            pityTriggered: []
            generationId: "abc123..."


Pity System Flow
==================

  Generation attempt N=49 (pityThreshold=50):
       |
       +-- legendary_fang: dropChance=0.01, pity counter = 49
       +-- Roll: 0.956 -> MISS
       +-- Counter: 49 -> 50 (THRESHOLD REACHED)
       |
  Generation attempt N=50:
       |
       +-- legendary_fang: counter=50 >= threshold=50
       +-- FORCE SELECT (bypasses all weighted rolling)
       +-- Generate legendary_fang (Tier 3, enriched)
       +-- Reset counter to 0
       +-- Publish loot.pity-triggered
       |
       +-- NOTE: divine actor alternative
           +-- At counter=40, a watching god might:
           |   +-- Call ForceGenerate with context tag "divine_gift"
           |   +-- The legendary drops 10 attempts early
           |   +-- Pity counter resets (the entry dropped)
           |   +-- Player experiences: "The gods smiled upon me"
           |   +-- Pity system: "Counter reset, working as intended"
           +-- The divine intervention is invisible to lib-loot


Need/Greed Distribution
==========================

  Party of 3 kills boss -> Generate with distributionMode="need_greed"
       |
       +-- Items generated: [enchanted_sword, healing_potion x3, 100 gold]
       |
       +-- Create loot context (TTL: 60s)
       |
       +-- All 3 members notified (via client events, future)
       |
       +-- Member A: DeclareNeedGreed(sword=NEED, potion=GREED)
       +-- Member B: DeclareNeedGreed(sword=NEED, potion=NEED)
       +-- Member C: DeclareNeedGreed(sword=PASS, potion=GREED)
       |
       +-- [Timeout or all declared]
       |
       +-- Resolve:
       |   +-- sword: NEED from A and B -> random between A and B -> A wins
       |   +-- potion: NEED from B > GREED from A,C -> B gets potion
       |   +-- gold: split evenly (33g each, 1g lost to rounding)
       |
       +-- Move sword to A's inventory
       +-- Move potion to B's inventory
       +-- Credit wallets (33g each)
       +-- Publish loot.distributed with assignments
```

---

## Stubs & Unimplemented Features

**Everything is unimplemented.** This is a pre-implementation architectural specification. No schema, no generated code, no service implementation exists. The following phases are planned:

### Phase 0: Foundation Prerequisites

Before lib-loot implementation:
- lib-affix should exist for Tier 3 enriched generation (soft dependency -- Tier 1/2 work without it)
- lib-item #407 (item decay/expiration) is desirable for temporary loot containers but not blocking
- lib-escrow completion is desirable for secure loot custody in need/greed but not blocking (initial implementation uses plain inventory containers)

### Phase 1: Core Infrastructure (Table Definitions + Basic Generation)

- Create loot-api.yaml schema with all endpoints
- Create loot-events.yaml schema
- Create loot-configuration.yaml schema
- Generate service code
- Implement table definition CRUD (create, get, list, update, deprecate, seed, preview)
- Implement sub-table cycle detection (BFS at create/update time)
- Implement table cache warming and invalidation

### Phase 2: Generation Engine

- Implement `Generate` with full pipeline: roll count, guaranteed entries, drop-chance entries, weighted pool, quantity curves
- Implement sub-table recursive generation with depth limit
- Implement context modifier application (luck, quantity, quality, weight tag modifiers)
- Implement Tier 1 (preview/reference only) generation
- Implement Tier 2 (standard instance creation) via lib-item
- Implement `GeneratePreview` for NPC GOAP evaluation
- Implement `GenerateBatch` for multi-table batch generation
- Performance testing at 100K NPC evaluation scale (Tier 1)

### Phase 3: Enriched Generation (Tier 3)

- Implement Tier 3 generation with lib-affix integration (soft)
- Implement `affixContext` pass-through to `IAffixClient.GenerateAffixSet`
- Implement `affixSetOverride` for fixed-definition legendary/unique items
- Implement graceful fallback to Tier 2 when lib-affix unavailable
- Implement currency drop generation via lib-currency (soft)

### Phase 4: Distribution

- Implement personal loot (per-member independent generation)
- Implement free-for-all (shared container with TTL)
- Implement need/greed (declaration tracking, timeout, resolution)
- Implement round-robin (rotation assignment)
- Implement leader-assign (all to leader)
- Implement claim and declaration endpoints
- Implement loot context TTL expiry with item destruction

### Phase 5: Pity System

- Implement pity counter tracking (increment on miss, reset on hit)
- Implement pity threshold force-selection during weighted pool phase
- Implement `ForceGenerate` for divine intervention and admin tools
- Implement pity counter TTL expiry for stale counters
- Implement pity events (triggered, reset)

### Phase 6: History, Analytics, and Variable Provider

- Implement generation history recording
- Implement history query endpoints
- Implement `GetDropRates` computed endpoint
- Implement `LootHistoryPruneService` background worker
- Implement `LootSourceVariableProviderFactory` for `${loot.*}` NPC GOAP variables
- Register factory as `IVariableProviderFactory` singleton
- Wire analytics integration (soft dependency)

### Phase 7: Resource Cleanup & Integration

- Implement cleanup endpoints (by-game-service, by-realm)
- Register with lib-resource for cascading cleanup
- Integration testing with lib-item, lib-inventory, lib-affix, lib-currency
- Test NPC GOAP looting behavior with sample ABML behaviors

---

## Potential Extensions

1. **Seasonal/event loot modifiers**: Named modifier sets (like affix weight modifier sets) that apply global weight adjustments during events. A "Winter Festival" modifier set might boost festive item drop rates across all tables. Applied via context tags ("winter_festival" tag -> weight modifiers) without modifying table definitions.

2. **Loot quality tiers with visual feedback**: A configurable quality scale (e.g., "common/uncommon/rare/epic/legendary") that determines visual presentation (item beam color, sound effect, minimap icon) via client events. lib-loot assigns quality tier; the client renders accordingly. The quality tier is metadata on the generation result, not stored on the item.

3. **Smart loot (character-aware drops)**: Modify weighted pool based on claimant's equipment or class. A warrior character might see increased weapon/armor weights; a mage might see increased spell component weights. Implemented via `contextTags` -- the caller analyzes the character and provides appropriate tags ("class:warrior", "needs:weapon").

4. **Cumulative luck (exploration bonus)**: Track per-entity "exploration depth" -- how many generation events they've triggered from a specific source type without finding anything notable. This depth modifies luck over time, making longer sessions increasingly rewarding. Distinct from pity (which is per-entry); this is per-source-type luck accumulation.

5. **Loot logging for economy dashboards**: Real-time loot generation feed for game designers to monitor drop rates, economy health, and outlier detection. Built on the generation history store with real-time event streaming via RabbitMQ topic subscriptions.

6. **Cross-table exclusions**: Entries that are mutually exclusive across different tables in the same generation event. "If this boss dropped its unique weapon, don't also drop the generic weapon from the common table." Implemented as a cross-table exclusion list checked during batch generation.

7. **Deterministic seeded generation**: For replay, testing, and procedural content (dungeon treasure rooms), support a deterministic RNG seed in the context. Same seed + same context + same table = identical output. Enables Redis-cached generation results for frequently repeated scenarios.

8. **Client events**: `loot-client-events.yaml` for pushing real-time loot notifications to connected WebSocket clients -- item dropped (with quality tier for visual presentation), need/greed prompt, loot claimed, pity progress.

9. **Loot container types**: Rather than using generic inventory containers for free-for-all loot, register dedicated loot container types (corpse, chest, treasure pile) with lib-inventory. These containers could have special rules (auto-destroy on empty, proximity-gated access, visual representation).

10. **Divine intervention API**: A dedicated endpoint for divine actors to modify a character's loot context for a duration -- "for the next 5 loot events, this character's luck modifier is 3.0 and context includes tag 'blessed_by_hermes'." Stored in the pity counter store with TTL. lib-loot checks for active divine modifiers before generating.

11. **Merchant loot integration**: NPC merchants "loot" their suppliers -- calling GeneratePreview to evaluate a supplier's catalog, then Generate to "purchase" stock. This creates emergent supply chains: the mine produces ore (via loot tables), the merchant acquires it (via generation), the merchant sells it at market (via lib-market). The loot table IS the production model.

12. **Archive-seeded dynamic tables**: When a character archive is processed by Storyline, automatically create a dynamic loot table containing the character's notable possessions. This table is registered for the ghost/undead/legacy encounter spawned from the archive. The content flywheel turns: character's loot becomes future character's loot.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

*None. Plugin is pre-implementation — no code exists to contain bugs.*

### Intentional Quirks (Documented Behavior)

1. **Loot table entries are denormalized in the table model**: Entries are stored inline within the `LootTableModel`, not as separate entities with foreign keys. This is deliberate for generation performance -- loading a table for generation requires exactly one store read (or cache hit), not a table read plus N entry reads. The trade-off is that updating a single entry requires saving the entire table.

2. **Sub-table references use table IDs, not codes**: Unlike implicit mappings in lib-affix (which use template codes for stability across template recreation), sub-table references use table GUIDs. This means deprecating and replacing a sub-table requires updating all parent table references. This is deliberate -- tables are less frequently recreated than templates, and ID references enable faster cache resolution during generation.

3. **Pity counters are per-entity, not per-party**: In group content, each party member has their own pity counter. If Member A has 49/50 pity progress on a rare drop and Member B kills the boss, Member A's counter still increments. This means pity is individual progress, not shared group progress. Games wanting shared pity should use divine intervention (force-generate for the "unluckiest" party member).

4. **Currency drops are best-effort**: If lib-currency is unavailable (soft dependency), currency entries appear in the generation result with `credited: false`. The caller is responsible for retrying or crediting manually. This prevents currency drops from blocking item generation.

5. **Need/greed auto-resolves on timeout**: If not all party members declare within `NeedGreedTimeoutSeconds`, undeclared members are treated as "greed" for all remaining items. This prevents indefinite blocking by AFK party members.

6. **Tier 3 fallback is silent**: When lib-affix is unavailable, Tier 3 entries fall back to Tier 2 (items created without affixes) with a warning log. The generation result includes `affixed: false` for these items. Callers that require affixed items must check this flag. The fallback is silent to avoid failing the entire generation for a soft dependency.

7. **"Nothing" entries are valid and useful**: An entry with `entryType: nothing` represents "no drop." It participates in weighted selection, diluting the pool. A table with 900 weight of "nothing" and 100 weight of items produces items only 10% of the time. This is the standard mechanism for "empty rolls" without separate probability configuration.

8. **Guaranteed entries ignore context**: Entries in the `guaranteedEntries` list are always generated regardless of source level, context tags, or weight modifiers. They are not filtered or weighted -- they simply always appear. Pity counters are not affected by guaranteed drops (they track weighted pool performance, not guaranteed output).

9. **Generation history is append-only with pruning**: History records are never updated after creation. The `LootHistoryPruneService` deletes records older than `LootHistoryRetentionDays`. For long-term analytics, consumers should subscribe to `loot.generated` events and maintain their own aggregated data (this is lib-analytics' role).

10. **Distribution modes are advisory for personal loot**: In "personal" mode, each party member gets independent rolls on the table. The items are placed directly in each member's container. There is no contention and no "fairness" guarantee -- one member might get 3 rares while another gets only commons. This is intentional; personal loot removes social friction at the cost of perceived fairness.

### Design Considerations (Requires Planning)

1. **Generation at 100K NPC scale**: NPC death events at scale could trigger thousands of loot generations per second. Tier 1 preview is designed for this (no instance creation, no cross-service calls), but Tier 2/3 generation involves lib-item and lib-inventory calls per item. Batch generation helps but doesn't eliminate the cross-service overhead. Consider: should NPC death loot be Tier 1 only (generate references, defer instantiation until a claimant arrives)?

2. **Loot container lifecycle**: Free-for-all loot creates temporary inventory containers. Who owns these containers? How are they associated with a physical location in the game world? lib-loot creates the container but has no concept of spatial placement -- that's the caller's responsibility. Consider: should loot containers use a dedicated `ownerType: "loot"` and be placed via lib-mapping integration?

3. **Interaction with lib-dungeon**: Dungeon treasure rooms are loot generation events. The dungeon core actor decides what treasure to place based on its personality and growth phase. Should the dungeon create dynamic tables (using the general table creation API) or should lib-loot have a "dungeon-aware" generation mode? Recommend: dungeons create dynamic tables. lib-loot stays dungeon-agnostic.

4. **Affix generation performance**: Tier 3 generation calls lib-affix per item. For batch generation with many Tier 3 entries, this creates significant cross-service call volume. lib-affix's `BatchGenerateAffixSets` endpoint exists for this purpose, but lib-loot needs to batch its affix requests efficiently (collect all Tier 3 items, make one batch call, distribute results).

5. **Cross-service atomicity**: A generation event creates items (lib-item), places them (lib-inventory), credits currency (lib-currency), and generates affixes (lib-affix). If any step fails partway through, the generation is partially complete. Compensating actions (destroy orphaned items, reverse currency credits) are needed. Initial implementation should use a "log-and-reconcile" pattern: record intended operations, execute them, handle failures by publishing error events for manual reconciliation.

6. **Loot tables as economic levers**: Loot tables are the primary faucet for items entering the economy. Changes to table weights have significant economic impact. Consider: should table modifications be audited? Should there be a "staging" flow where table changes are previewed before activation? Should lib-analytics track generation rates for economy health monitoring?

7. **Variable provider performance**: The `${loot.nearby_source_count}` variable requires knowing about active loot contexts within an NPC's perception range. This is inherently spatial -- it needs lib-mapping data or a proximity query. Without mapping integration, the variable provider would need the caller to provide "nearby source IDs" in the variable context. Consider: should the variable provider cache active loot contexts per-region?

8. **Interaction with lib-save-load**: When saving game state, active loot contexts (unclaimed piles) should be persisted. On load, these contexts should be restored with updated TTLs. lib-save-load would need to serialize and restore the Redis-based context store. Consider: should active loot contexts be excluded from saves (loot is ephemeral) or included (preserving the world state)?

9. **L2 dependencies classified as soft violate SERVICE-HIERARCHY.md**: lib-currency (L2) and lib-character (L2) are listed as soft dependencies with graceful degradation. Per SERVICE-HIERARCHY.md, L4 services MUST use constructor injection (hard dependency) for all L0/L1/L2 dependencies — graceful degradation for guaranteed-available layers is explicitly forbidden because it hides deployment configuration errors. When implemented, both should be constructor-injected hard dependencies. The "currency drops are best-effort" design (Intentional Quirk #4) must be revisited: if lib-currency is guaranteed available, currency drops should never fail due to missing dependency. Similarly, claimant validation should always use lib-character directly rather than degrading gracefully.

---

## Work Tracking

*No active work items. Plugin is in pre-implementation phase. See [ITEM-ECONOMY-PLUGINS.md](../plans/ITEM-ECONOMY-PLUGINS.md) for the landscape survey and prioritization.*
