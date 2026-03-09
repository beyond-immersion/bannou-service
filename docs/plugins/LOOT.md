# Loot Plugin Deep Dive

> **Plugin**: lib-loot (not yet created)
> **Schema**: `schemas/loot-api.yaml` (not yet created)
> **Version**: N/A (Pre-Implementation)
> **State Store**: loot-tables (MySQL), loot-table-cache (Redis), loot-contexts (Redis), loot-pity-counters (Redis), loot-history (MySQL), loot-lock (Redis) — all planned
> **Layer**: GameFeatures
> **Status**: Aspirational — no schema, no generated code, no service implementation exists.
> **Short**: Loot table management with weighted drops, contextual modifiers, and pity thresholds

---

## Overview

Loot table management and generation service (L4 GameFeatures) for weighted drop determination, contextual modifier application, and group distribution orchestration. A thin orchestration layer (like Quest over Contract, Escrow over Currency/Item, Divine over Currency/Seed/Collection) that composes existing Bannou primitives to deliver loot acquisition mechanics. Game-agnostic: table structures, entry weights, context modifiers, distribution modes, and pity thresholds are all opaque configuration defined per game at deployment time through table seeding. Internal-only, never internet-facing.

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

lib-loot manages WHAT can drop from sources and HOW drops are determined and distributed. It does not own what items ARE (lib-item), where items GO (lib-inventory), or what items COST (lib-currency). lib-loot is a pure generation engine: given a source context, it resolves weighted tables, applies contextual modifiers, generates drop lists, and orchestrates distribution — then delegates to L2 services for actual item creation and placement.

Loot is not a reward — it is a *consequence*. Rewards are intentional outcomes designed by quest makers and event planners (lib-quest, lib-contract). Loot is the emergent result of interacting with the game world: killing a creature, opening a chest, harvesting a node, completing a dungeon room. This distinction matters because lib-quest already handles "give the player items for completing objectives" via prebound API execution on contract milestones. lib-loot handles "determine what this wolf was carrying when it died" — a fundamentally different operation involving weighted randomness, contextual modification, and distribution fairness.

Loot tables come in two flavors: **static tables** are authored by designers and seeded at deployment (the wolf_alpha always has the same potential drops, modified by context). **Dynamic tables** are generated at runtime by higher-layer services — a dungeon's loot table might be procedurally constructed based on the dungeon's personality, level, and current state via lib-dungeon. Both flavors go through the same generation pipeline; the difference is authorship, not mechanics.

Three generation tiers serve different performance needs: **Tier 1 (Lightweight)** resolves a single table with no context — pure weighted random, suitable for simple drops and bulk NPC evaluation. **Tier 2 (Standard)** resolves tables with full context evaluation, sub-table recursion, affix generation, and pity tracking — the default for player-facing loot events. **Tier 3 (Enriched)** adds distribution orchestration, multi-claimant fairness, and contract-based need/greed flows — used for group content and high-value drops.

At 100K concurrent NPCs, many are interacting with the loot system simultaneously — as sources (creatures that drop loot), claimants (NPCs that loot containers), and evaluators (NPCs that assess whether loot is worth pursuing). Tier 1 generation is optimized for this scale: cached table resolution, no context evaluation, batch-friendly. Only player-facing interactions use Tier 2/3.

lib-loot tracks per-entity failure counters for configurable "pity" thresholds — after N failed rolls for items above a rarity threshold, the next roll gets boosted weight. This is deliberately minimal: lib-loot provides the counter and boost mechanism, but the actual pity curves and thresholds are configuration (not code), allowing game designers to tune the feel without service changes.

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
  rollMode: RollMode           # Independent | Sequential | PickUnique
  guaranteedEntries: [Guid]    # Entry IDs that always drop (bypass rolling)

  # Entries
  entries: [LootEntry]         # The weighted pool (see below)

  # Context Requirements
  requiredContextKeys: [string] # Context keys that MUST be provided for generation

  # Metadata
  isActive: bool
  isDeprecated: bool
  deprecatedAt: DateTimeOffset?
  deprecationReason: string?
  description: string
```

**Key design decisions**:

1. **Roll count is a range** (`{min: 1, max: 3}`), not a fixed number. The actual roll count is determined at generation time, influenced by context modifiers (party size, luck, source tier). This enables "more kills = more rolls" and "bigger party = more drops" without separate tables.

2. **Roll modes control duplicate behavior**: `Independent` allows the same entry to be selected multiple times (3 rolls might all produce iron ore). `Sequential` treats the table as an ordered list (first roll picks from full pool, second from remaining, etc.). `PickUnique` ensures no entry is selected twice per generation event.

3. **Guaranteed entries bypass rolling entirely**: They always appear in the output regardless of weight calculations. Used for quest items that must drop, material fragments that always result from destruction, and currency amounts that accompany every kill.

4. **Sub-table references enable composition**: A "wolf_alpha_drops" table might contain entries for common materials plus a reference to "rare_enchanted_items" sub-table. The sub-table has its own weights and can be shared across many parent tables.

### Loot Entries

Each entry in a table represents one possible outcome:

```
LootEntry:
  entryId: Guid
  tableId: Guid                # Parent table

  # What drops
  entryType: EntryType         # Item | Currency | SubTable | Nothing
  itemTemplateId: Guid?        # For item entries
  itemTemplateCode: string?    # Alternative: resolve by code at generation time
  currencyDefinitionId: Guid?  # For currency entries
  subTableId: Guid?            # For sub-table references

  # Probability
  weight: int                  # Base weight for weighted random selection (1000 typical)
  weightTagModifiers: map<string, double>  # Per-context-tag weight multipliers
  dropChance: double?           # Optional flat probability override (0.0-1.0)

  # Quantity
  quantity: QuantityRange      # {min: 1, max: 5} -- rolled uniformly
  quantityCurve: QuantityCurve? # Optional curve type: Linear, Bell, ExponentialDecay

  # Generation Tier
  generationTier: GenerationTier  # Lightweight (ref only), Standard (instance), Enriched (instance+affixes)

  # Affix Generation (Tier 3 only)
  affixContext: AffixContext?   # itemLevel range, rarity, influences, weight modifiers
  affixSetOverride: [Guid]?    # Fixed affix definitions (unique/legendary items)

  # Item Overrides
  itemOverrides: LootItemOverrides?  # Partial overrides applied to created instances (customStats, quality)

  # Pity Configuration
  pityEnabled: bool            # Track failure counter for this entry
  pityThreshold: int?          # Guaranteed drop after N failures (e.g., 50)
  pityCounterScope: PityCounterScope  # Entity (per character), Realm, Global

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

4. **Quantity curves shape distribution**: `Linear` gives uniform distribution between min and max. `Bell` (normal distribution centered on midpoint) makes average quantities most common. `ExponentialDecay` makes minimum quantities most common with rare large drops. Games tune the "feel" of loot through curves without changing the actual min/max.

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
  luckModifier: double          # Multiplicative luck factor (1.0 = normal, 2.0 = double luck)
  quantityModifier: double     # Multiplicative quantity factor (1.0 = normal)
  qualityModifier: double      # Multiplicative quality factor (affects Tier 3 affix generation)

  # Distribution
  distributionMode: DistributionMode?  # Personal | NeedGreed | RoundRobin | FreeForAll | LeaderAssign
  partyMembers: [PartyMember]? # For group distribution (entityId, entityType, level, role)

  # Target
  targetContainerId: Guid?     # Where to place generated items (null = return without placing)
  targetWalletId: Guid?        # Where to credit currency drops (null = return without crediting)

  # Overrides
  overrideWeightModifiers: map<string, double>? # Additional weight modifiers applied on top of entry defaults
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
| **Personal** | Each party member gets an independent roll on the table. Items are placed directly in their container. No contention. | Modern MMO personal loot. Every player sees their own drops. Scales linearly with party size. |
| **NeedGreed** | One set of loot is generated. Each item is offered to all eligible party members. Members declare "Need" (I want this for use) or "Greed" (I want this for profit) or "Pass". Need > Greed > Pass. Ties broken randomly. | Traditional MMO group loot. Creates social dynamics around loot distribution. |
| **RoundRobin** | One set of loot is generated. Items are assigned to party members in rotation order. Each member gets roughly equal count. | Fair distribution without decision overhead. Good for farming groups. |
| **FreeForAll** | One set of loot is generated and placed in a shared container. First to claim gets it. | Competitive loot. Creates urgency and conflict. Used for PvP scenarios and NPC scavenging. |
| **LeaderAssign** | One set of loot is generated. Party leader receives all items and distributes manually. | Guild raids, managed groups. Trust-based distribution. |

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
  Entry: enchanted_ring     weight: 300   generationTier: Enriched
  Entry: enchanted_amulet   weight: 300   generationTier: Enriched
  Entry: enchanted_weapon   weight: 200   generationTier: Enriched
  Entry: enchanted_armor    weight: 200   generationTier: Enriched
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
| lib-currency (`ICurrencyClient`) | Currency entry drops: credit wallets for gold/material currency drops (L2) |
| lib-character (`ICharacterClient`) | Claimant validation for pity counter scoping and distribution eligibility (L2) |
| lib-seed (`ISeedClient`) | Context evaluation for situational modifiers: seed growth phase and capability levels influence weight modifiers and drop context (L2) |
| lib-contract (`IContractClient`) | Distribution orchestration for need/greed/auction flows: contract-based coordination for multi-party loot claiming (L1) |

### Soft Dependencies (runtime resolution via `IServiceProvider` -- graceful degradation)

| Dependency | Usage | Behavior When Missing |
|------------|-------|-----------------------|
| lib-affix (`IAffixClient`) | Tier 3 generation: request affix set generation for enriched drops | Tier 3 entries fall back to Tier 2 (items created without affixes); warning logged. Template `customStats` still applied from `itemOverrides`. |
| lib-analytics (`IAnalyticsClient`) | Publishing loot generation statistics for economy monitoring and divine observation | Statistics not collected; generation works normally. |

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

### Type Field Classification

Every polymorphic "type" or "kind" field in the Loot domain falls into one of three categories:

| Field | Model(s) | Cat | Values / Source | Rationale |
|-------|----------|-----|-----------------|-----------|
| `entryType` | `LootEntry` | C | `Item`, `Currency`, `SubTable`, `Nothing` | Finite entry kinds that the generation engine switches on. Service-owned enum (`EntryType`). |
| `rollMode` | `LootTable` | C | `Independent`, `Sequential`, `PickUnique` | Finite pool-selection modes that govern duplicate behavior. Service-owned enum (`RollMode`). |
| `category` | `LootTable` | B | `"creature"`, `"chest"`, `"quest"`, `"world_event"`, ... | Broad table classification. Opaque string so games can invent new source categories without schema changes. |
| `sourceType` | `LootGenerationContext` | B | `"creature"`, `"chest"`, `"quest_reward"`, `"world_event"`, `"divine_gift"`, ... | What produced the loot. Opaque string; new source types are added per game without schema changes. |
| `claimantType` | `LootGenerationContext` | B | `"character"`, `"party"`, `"npc"`, ... | Who is claiming. Opaque string to allow future claimant kinds (guilds, dungeon cores, etc.) without schema changes. |
| `distributionMode` | `LootGenerationContext` | C | `Personal`, `NeedGreed`, `RoundRobin`, `FreeForAll`, `LeaderAssign` | Finite distribution strategies the service implements. Service-owned enum (`DistributionMode`). |
| `pityCounterScope` | `LootEntry` | C | `Entity`, `Realm`, `Global` | Finite scoping modes for pity counters. Service-owned enum (`PityCounterScope`). |
| `quantityCurve` | `LootEntry` | C | `Linear`, `Bell`, `ExponentialDecay` | Finite distribution curve shapes the generation engine implements. Service-owned enum (`QuantityCurve`). |
| `generationTier` | `LootEntry` | C | `Lightweight` (ref only), `Standard` (instance), `Enriched` (enriched + affixes) | Finite tier levels determining generation complexity. Service-owned enum (`GenerationTier`). |
| `displayRarity` | `LootEntry` | B | `"common"`, `"uncommon"`, `"rare"`, ... | Preview rarity label. Opaque string matching lib-item's rarity vocabulary; games define their own rarity tiers. |

**Category key**: **A** = Entity Reference (`EntityType` enum), **B** = Content Code (opaque string, game-configurable), **C** = System State (service-owned enum, finite).

---

## Events

**Topic prefix**: `loot` (all topics use `loot.{entity}.{action}` pattern per QUALITY TENETS naming conventions)

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `loot.table.created` | `LootTableCreatedEvent` | Table definition created (x-lifecycle) |
| `loot.table.updated` | `LootTableUpdatedEvent` | Table definition updated (x-lifecycle); includes `ChangedFields`. Covers deprecation state changes (changedFields contains `isDeprecated`, `deprecatedAt`, `deprecationReason`) |
| `loot.generated` | `LootGeneratedEvent` | Loot generated from a table (batch: includes all rolled entries, created items, currency amounts) |
| `loot.distributed` | `LootDistributedEvent` | Generated loot distributed to recipients (includes distribution mode and per-recipient assignments) |
| `loot.claimed` | `LootClaimedEvent` | Individual item claimed from a free-for-all or need/greed context |
| `loot.expired` | `LootExpiredEvent` | Unclaimed loot context TTL expired; items destroyed |
| `loot.pity.triggered` | `LootPityTriggeredEvent` | Pity counter reached threshold; guaranteed drop issued |
| `loot.pity.reset` | `LootPityResetEvent` | Pity counter reset (entry dropped naturally before threshold) |

### Consumed Events

| Topic | Handler | Action |
|-------|---------|--------|
| `item-template.updated` | `HandleItemTemplateUpdatedAsync` | Check `changedFields` for deprecation state changes; if deprecated, scan tables for entries referencing the template and log warning. Do NOT automatically disable entries (the template's `migrationTargetId` may handle transition). |

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
| `DefaultGenerationTier` | `LOOT_DEFAULT_GENERATION_TIER` | `Standard` | Default generation tier for new entries (enum: Lightweight=1, Standard=2, Enriched=3) |
| `DefaultRollCount` | `LOOT_DEFAULT_ROLL_COUNT` | `1` | Default roll count (both min and max) when not specified |
| `DefaultRollMode` | `LOOT_DEFAULT_ROLL_MODE` | `Independent` | Default roll mode for new tables |
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
| `ICurrencyClient` | Currency entry credit operations (L2 hard) |
| `ICharacterClient` | Claimant validation for distribution eligibility (L2 hard) |
| `ISeedClient` | Context evaluation for situational modifiers (L2 hard) |
| `IContractClient` | Distribution orchestration for need/greed/auction flows (L1 hard) |
| `ITelemetryProvider` | Telemetry span creation for all async methods (L0) |
| `IEventConsumer` | Event handler registration for consumed events (L0) |
| `IServiceProvider` | Runtime resolution of soft L4 dependencies (Affix, Analytics) |

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

All endpoints require `developer` role. `x-permissions: [{ state: any, role: developer }]`

- **CreateTable** (`/loot/table/create`): Validates game service existence. Validates code uniqueness per game service. Enforces `MaxTablesPerGameService`. Validates entries: sub-table references exist and don't create cycles (BFS cycle detection), item template IDs or codes resolve, entry weights > 0. Saves to MySQL. Populates cache. Publishes `loot.table.created`.

- **GetTable** (`/loot/table/get`): Cache read-through (Redis -> MySQL -> populate cache). Supports lookup by tableId or by gameServiceId + code.

- **ListTables** (`/loot/table/list`): Paged JSON query with required gameServiceId filter. Optional filters: category, tags (any match), isActive, includeDeprecated (boolean, default: false). Returns table summaries (no entry details -- use GetTable for full definition).

- **UpdateTable** (`/loot/table/update`): Acquires distributed lock. Partial update. **Cannot change**: code, gameServiceId (identity-level). Entry additions/removals/modifications are part of the update payload. Re-validates cycle detection on sub-table changes. Invalidates cache. Publishes `loot.table.updated` with `changedFields`.

- **DeprecateTable** (`/loot/table/deprecate`): Marks deprecated with triple-field semantics: sets `isDeprecated: true`, records `deprecatedAt` timestamp, and stores the caller-provided `deprecationReason`. Idempotent -- returns OK if already deprecated (caller's intent is satisfied). Optional `migrationTargetCode` for directing future generation to a replacement table. Existing references from other tables (as sub-tables) are NOT automatically updated -- they continue referencing the deprecated table (which still functions but logs deprecation warnings during generation). Invalidates cache. Publishes `loot.table.updated` with changedFields containing deprecation fields.

- **SeedTables** (`/loot/table/seed`): Bulk creation, skipping tables whose code already exists (idempotent). Validates game service once. Validates all sub-table references within the batch (entries can reference other tables in the same batch). Returns created/skipped counts. Populates cache for all created tables.

- **PreviewTable** (`/loot/table/preview`): Returns a human-readable breakdown of a table's structure: entries with display names, effective weights at a specified source level, probability percentages, sub-table expansion (recursive), and pity thresholds. Does NOT generate anything. Used for game design tooling, bestiary tooltips, and NPC GOAP planning (Tier 1 evaluation).

### Generation (4 endpoints)

`x-permissions: []` (service-to-service only, not exposed to WebSocket clients)

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
    |       |   +-- Exclude: already-selected entries if rollMode=PickUnique
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
    |           +-- If entryType=Nothing: skip (dilution entry)
    |           +-- If entryType=SubTable: recursively generate from sub-table
    |           +-- If entryType=Item: generate item (see step 6)
    |           +-- If entryType=Currency: generate currency (see step 7)
    |           +-- Update pity counters for all eligible-but-not-selected entries
    |           +-- Reset pity counter for selected entry
    |
    +-- 6. Generate item entries
    |       +-- Roll quantity from entry.quantity range using quantityCurve
    |       +-- Apply context.quantityModifier
    |       |
    |       +-- If generationTier=Lightweight: return template reference only
    |       +-- If generationTier=Standard: call IItemClient.CreateItemInstanceAsync
    |       |   +-- Apply entry.itemOverrides (customStats, metadata)
    |       |   +-- Set originType="loot", originId=generationId
    |       |   +-- Place in targetContainerId if provided
    |       +-- If generationTier=Enriched: call IAffixClient (soft)
    |           +-- Generate affix set using entry.affixContext
    |           +-- If affixSetOverride: use fixed definitions instead
    |           +-- lib-affix stores affix data in its own state store, keyed by item instance ID (per FOUNDATION TENETS — no metadata bag contracts)
    |           +-- Fall back to Tier 2 if lib-affix unavailable
    |
    +-- 7. Generate currency entries
    |       +-- Roll quantity from entry.quantity range
    |       +-- Call ICurrencyClient.CreditAsync (hard L2 dep)
    |       +-- Failure is an unexpected error (ApiException catch per IMPLEMENTATION TENETS)
    |
    +-- 8. Distribution (if partyMembers provided)
    |       +-- Personal: duplicate generation per member (each gets independent rolls)
    |       +-- NeedGreed: create loot context, await declarations
    |       +-- RoundRobin: assign items to members in rotation
    |       +-- FreeForAll: create shared loot container with TTL
    |       +-- LeaderAssign: place all items in leader's container
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

`x-permissions: []` (service-to-service only, not exposed to WebSocket clients)

- **DeclareNeedGreed** (`/loot/distribution/declare`): For need/greed distribution. Takes contextId, itemInstanceId, declaration (`NeedGreedDeclaration` enum: `Need`, `Greed`, `Pass`). Validates context exists and hasn't expired. Validates declarer is in the party. Records declaration. When all party members have declared (or `NeedGreedTimeoutSeconds` expires), resolves winner and distributes. Publishes `loot.claimed` for each resolved item.

- **ClaimItem** (`/loot/distribution/claim`): For free-for-all distribution. Takes contextId, itemInstanceId. Acquires context lock. Validates context exists, item is unclaimed, claimant is eligible. Moves item from shared container to claimant's container. Publishes `loot.claimed`.

- **GetContext** (`/loot/distribution/context`): Returns active loot context details -- items available, declarations received, time remaining, distribution mode. Used by UI to display loot rolls and by NPC GOAP to evaluate claiming decisions.

### Query (3 endpoints)

`x-permissions: []` (service-to-service only, not exposed to WebSocket clients)

- **GetGenerationHistory** (`/loot/history/get`): Load generation record by generationId. Returns full details: table used, context, entries rolled, items created.

- **ListGenerationHistory** (`/loot/history/list`): Paged query by gameServiceId, optional sourceType, claimantId, tableId, timeRange. Used for analytics dashboards and divine actor observation patterns.

- **GetDropRates** (`/loot/rates/get`): Computed endpoint. Takes tableId + context (without generating). Returns effective drop rates for all entries given the context's modifiers -- useful for game design validation ("what are the actual odds of this legendary dropping from this boss with this party composition?").

### Cleanup (2 endpoints)

`x-permissions: []` (service-to-service only, called by lib-resource cleanup callbacks)

Resource-managed cleanup via lib-resource (per FOUNDATION TENETS):

- **CleanupByGameService** (`/loot/cleanup-by-game-service`): Deletes all tables, pity counters, active contexts, and history for a game service.

- **CleanupByRealm** (`/loot/cleanup-by-realm`): Deletes realm-scoped pity counters and active contexts. Tables are game-service-scoped (not realm-scoped) and are unaffected.

---

## Visual Aid

Loot table definitions and generation rules are owned here. Item creation is lib-item (L2). Container placement is lib-inventory (L2). Currency rewards are lib-currency (L2). Context evaluation queries lib-character (L2, hard) and lib-seed (L2, hard) for situational modifiers. Distribution orchestration coordinates with lib-contract (L1, hard) for need/greed/auction flows. Modifier generation is lib-affix (L4, soft). Environmental context is lib-environment (L4, soft). Divine intervention in loot outcomes is lib-divine (L4, soft) — a god might modify drop weights for followers, but lib-loot doesn't know or care that the context modifier came from a deity.

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
|  |  Seed ---------- context evaluation, situational modifiers     |     |
|  |  Contract ------ need/greed/auction distribution flows         |     |
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
       +-- Weighted pool (3 rolls, Independent mode):
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
       |      |   +-- lib-affix stores affixes in own state (keyed by instance ID)
       |      |
       |      +-- Roll 3: 234 -> wolf_pelt (Tier 2)
       |          +-- Quantity: range(1,2), linear -> 1
       |          +-- CreateItemInstance(wolf_pelt_template, qty:1)
       |
       +-- Distribution: Personal (solo kill)
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
       +-- Publish loot.pity.triggered
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

  Party of 3 kills boss -> Generate with distributionMode=NeedGreed
       |
       +-- Items generated: [enchanted_sword, healing_potion x3, 100 gold]
       |
       +-- Create loot context (TTL: 60s)
       |
       +-- All 3 members notified (via client events, future)
       |
       +-- Member A: DeclareNeedGreed(sword=Need, potion=Greed)
       +-- Member B: DeclareNeedGreed(sword=Need, potion=Need)
       +-- Member C: DeclareNeedGreed(sword=Pass, potion=Greed)
       |
       +-- [Timeout or all declared]
       |
       +-- Resolve:
       |   +-- sword: Need from A and B -> random between A and B -> A wins
       |   +-- potion: Need from B > Greed from A,C -> B gets potion
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
- Implement currency drop generation via lib-currency (hard L2 dep)

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

4. **Need/greed auto-resolves on timeout**: If not all party members declare within `NeedGreedTimeoutSeconds`, undeclared members are treated as "greed" for all remaining items. This prevents indefinite blocking by AFK party members.

5. **Tier 3 fallback is silent**: When lib-affix is unavailable, Tier 3 entries fall back to Tier 2 (items created without affixes) with a warning log. The generation result includes `affixed: false` for these items. Callers that require affixed items must check this flag. The fallback is silent to avoid failing the entire generation for a soft dependency.

6. **"Nothing" entries are valid and useful**: An entry with `entryType: Nothing` represents "no drop." It participates in weighted selection, diluting the pool. A table with 900 weight of Nothing and 100 weight of items produces items only 10% of the time. This is the standard mechanism for "empty rolls" without separate probability configuration.

7. **Guaranteed entries ignore context**: Entries in the `guaranteedEntries` list are always generated regardless of source level, context tags, or weight modifiers. They are not filtered or weighted -- they simply always appear. Pity counters are not affected by guaranteed drops (they track weighted pool performance, not guaranteed output).

8. **Generation history is append-only with pruning**: History records are never updated after creation. The `LootHistoryPruneService` deletes records older than `LootHistoryRetentionDays`. For long-term analytics, consumers should subscribe to `loot.generated` events and maintain their own aggregated data (this is lib-analytics' role).

9. **Distribution modes are advisory for personal loot**: In "personal" mode, each party member gets independent rolls on the table. The items are placed directly in each member's container. There is no contention and no "fairness" guarantee -- one member might get 3 rares while another gets only commons. This is intentional; personal loot removes social friction at the cost of perceived fairness.

### Design Considerations (Requires Planning)

1. **Generation at 100K NPC scale**: NPC death events at scale could trigger thousands of loot generations per second. Tier 1 preview is designed for this (no instance creation, no cross-service calls), but Tier 2/3 generation involves lib-item and lib-inventory calls per item. Batch generation helps but doesn't eliminate the cross-service overhead. Consider: should NPC death loot be Tier 1 only (generate references, defer instantiation until a claimant arrives)?

2. **Loot container lifecycle**: Free-for-all loot creates temporary inventory containers. Who owns these containers? How are they associated with a physical location in the game world? lib-loot creates the container but has no concept of spatial placement -- that's the caller's responsibility. Consider: should loot containers use a dedicated `ownerType: "loot"` and be placed via lib-mapping integration?

3. **Interaction with lib-dungeon**: Dungeon treasure rooms are loot generation events. The dungeon core actor decides what treasure to place based on its personality and growth phase. Should the dungeon create dynamic tables (using the general table creation API) or should lib-loot have a "dungeon-aware" generation mode? Recommend: dungeons create dynamic tables. lib-loot stays dungeon-agnostic.

4. **Affix generation performance**: Tier 3 generation calls lib-affix per item. For batch generation with many Tier 3 entries, this creates significant cross-service call volume. lib-affix's `BatchGenerateAffixSets` endpoint exists for this purpose, but lib-loot needs to batch its affix requests efficiently (collect all Tier 3 items, make one batch call, distribute results).

5. **Cross-service atomicity**: A generation event creates items (lib-item), places them (lib-inventory), credits currency (lib-currency), and generates affixes (lib-affix). If any step fails partway through, the generation is partially complete. Compensating actions (destroy orphaned items, reverse currency credits) are needed. Initial implementation should use a "log-and-reconcile" pattern: record intended operations, execute them, handle failures by publishing error events for manual reconciliation.

6. **Loot tables as economic levers**: Loot tables are the primary faucet for items entering the economy. Changes to table weights have significant economic impact. Consider: should table modifications be audited? Should there be a "staging" flow where table changes are previewed before activation? Should lib-analytics track generation rates for economy health monitoring?

7. **Variable provider performance**: The `${loot.nearby_source_count}` variable requires knowing about active loot contexts within an NPC's perception range. This is inherently spatial -- it needs lib-mapping data or a proximity query. Without mapping integration, the variable provider would need the caller to provide "nearby source IDs" in the variable context. Consider: should the variable provider cache active loot contexts per-region?

8. **Interaction with lib-save-load**: When saving game state, active loot contexts (unclaimed piles) should be persisted. On load, these contexts should be restored with updated TTLs. lib-save-load would need to serialize and restore the Redis-based context store. Consider: should active loot contexts be excluded from saves (loot is ephemeral) or included (preserving the world state)?

9. **T31 deprecation category classification**: Loot tables need a definitive Category A vs Category B classification (per IMPLEMENTATION TENETS deprecation lifecycle). Category A (definitions where instances don't persist independently) allows deprecate + undeprecate + delete. Category B (definitions where instances persist independently) allows deprecate only — no undeprecate, no delete. Active loot contexts reference tables briefly during generation, but generation results become independent items via lib-item. Determine which category applies; this affects whether undeprecate/delete endpoints exist.

10. **T8 filler assessment for generation responses**: The `LootGenerationResult` includes `generationTier` and `distributionMode`. Per IMPLEMENTATION TENETS, echoed request fields are forbidden in responses. However, these may represent computed/effective values rather than echoes (e.g., `generationTier` per entry varies within a single generation, `distributionMode` may be resolved from defaults). Determine whether these are request echoes (remove) or result-specific data (keep).

11. **lib-resource registration for item template references**: Loot table entries reference item templates by ID. When an item template is deleted via lib-resource cascading cleanup, should lib-loot register as a reference holder (via `x-references`) to participate in cleanup? Or is the consumed `item-template.updated` event sufficient? The current approach (event-based warning) doesn't prevent orphaned references — lib-resource integration would enforce CASCADE/RESTRICT/DETACH policies.

12. **Realm cleanup appropriateness**: The cleanup table lists `realm | loot | CASCADE` but loot tables are game-service-scoped, not realm-scoped. Only pity counters and active contexts might be realm-scoped. Determine whether realm cleanup is needed at all, or whether it should be limited to realm-scoped pity counter cleanup only.

13. **isActive vs isDeprecated redundancy**: The `LootTable` model has both `isActive: bool` and `isDeprecated: bool`. Per IMPLEMENTATION TENETS, deprecation state should be the authoritative lifecycle control. Determine whether `isActive` serves a distinct purpose (e.g., temporary deactivation without deprecation) or is redundant with deprecation. If distinct, document the semantic difference clearly.

14. **Validation constraint specifics**: The schema needs specific validation constraints (`minimum`, `maximum`, `minLength`, `maxLength`, `pattern`) on all fields per SCHEMA-RULES. Determine appropriate bounds for: weight (minimum 1?), rollCount ranges, pityThreshold, quantity ranges, code patterns, tag lengths, etc.

15. **PartyMember, RollRange, QuantityRange type definitions**: These inline types need full model definitions with proper field types and descriptions. `PartyMember` needs entityId (Guid), entityType, level (int), role (string?). `RollRange` and `QuantityRange` need min (int) and max (int) with appropriate validation constraints.

16. **AffixContext ownership**: The `AffixContext` type is used by lib-loot but describes data consumed by lib-affix. Determine whether this is a loot-owned type (with fields that map to lib-affix's API) or whether lib-affix should define this type and lib-loot should reference it via `$ref`. Per FOUNDATION TENETS, each service owns its own domain data.

17. **Variable provider registration in variable-providers.yaml**: The `${loot.*}` variable namespace should be registered in the shared `variable-providers.yaml` (or equivalent registration mechanism) so that the Actor runtime knows about it. Determine the registration mechanism and document it.

18. **claimantType: string vs EntityType enum**: The `claimantType` field is currently an opaque string (Category B in type classification). Per IMPLEMENTATION TENETS type safety rules, if the valid set is known and finite within the service's domain (character, party, npc), it may warrant a service-owned enum. However, the doc notes future claimant kinds (guilds, dungeon cores) — determine whether this warrants an enum with planned expansion or remains an opaque string.

19. **Event model naming after Pattern C topic conversion**: With topics now using Pattern C (`loot.table.created`, `loot.pity.triggered`), verify that event model class names follow the `{Entity}{Action}Event` naming convention per QUALITY TENETS. Current names like `LootTableCreatedEvent` and `LootPityTriggeredEvent` appear correct, but confirm all models align after the topic rename.

---

## Work Tracking

*No active work items. Plugin is in pre-implementation phase. See [Economy System Guide](../guides/ECONOMY-SYSTEM.md) for the cross-cutting economy architecture.*
