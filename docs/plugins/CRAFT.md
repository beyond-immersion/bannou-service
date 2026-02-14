# Craft Plugin Deep Dive

> **Plugin**: lib-craft
> **Schema**: schemas/craft-api.yaml
> **Version**: 1.0.0
> **State Stores**: craft-recipe-store (MySQL), craft-recipe-cache (Redis), craft-session-store (MySQL), craft-proficiency-store (MySQL), craft-station-registry (MySQL), craft-discovery-store (MySQL), craft-lock (Redis)
> **Status**: Pre-implementation (architectural specification)
> **Planning**: [ITEM-ECONOMY-PLUGINS.md](../plans/ITEM-ECONOMY-PLUGINS.md)

---

## Overview

Recipe-based crafting orchestration service (L4 GameFeatures) for production workflows, item modification, and skill-gated crafting execution. A thin orchestration layer that composes existing Bannou primitives: lib-item for storage, lib-inventory for material consumption and output placement, lib-contract for multi-step session state machines, lib-currency for costs, and lib-affix for modifier operations on existing items. Any system that needs to answer "can this entity craft this recipe?" or "what happens when step 3 completes?" queries lib-craft.

**Composability**: Recipe definitions and crafting session management are owned here. Item storage is lib-item (L2). Container placement is lib-inventory (L2). Session state machines are lib-contract (L1). Currency costs are lib-currency (L2). Modifier definitions and application primitives are lib-affix (L4, soft). Loot generation that creates pre-crafted items at scale is lib-loot (L4, future). NPC crafting decisions via GOAP use lib-craft's Variable Provider Factory.

**The foundational distinction**: lib-craft manages HOW items are created and transformed -- recipes, steps, materials, skill requirements, station constraints, quality formulas, discovery. WHAT modifiers exist and their generation rules is lib-affix's domain. WHO triggers crafting (players, NPCs, automated systems) is the caller's concern. This means lib-craft has two primary consumer patterns: production consumers (NPC blacksmiths, player crafters, automated factories) that create new items from materials, and modification consumers (NPC enchanters, player crafters using currency items) that transform existing items using lib-affix primitives.

**Two recipe paradigms**:

| Paradigm | What Happens | Example | Delegates To |
|----------|-------------|---------|-------------|
| **Production** | Inputs consumed, outputs created | Smelt ore into ingots, forge sword from steel | lib-item (create), lib-inventory (consume/place) |
| **Modification** | Existing item transformed in place | Reroll affixes, add enchantment, corrupt | lib-affix (apply/remove/reroll/state), lib-item (metadata write) |
| **Extraction** | Existing item destroyed, components recovered | Salvage weapon for materials, disenchant for reagents | lib-item (destroy), lib-inventory (place outputs) |

**Zero game-specific content**: lib-craft is a generic recipe execution engine. Arcadia's 37+ authentic crafting processes (smelting, tanning, weaving, alchemy, enchanting), PoE-style currency modification (chaos orbs, exalted orbs, fossils), a simple mobile game's "combine 3 items" mechanic, or an idle game's automated production chains are all equally valid recipe configurations. Recipe types, proficiency domains, station types, tool categories, and quality formulas are all opaque strings defined per game at deployment time through recipe seeding.

**Current status**: Pre-implementation. No schema, no code. This deep dive is an architectural specification based on the Arcadia crafting vision (37+ authentic processes, NPC-driven economy, progressive player agency) and the [Item & Economy Plugin Landscape](../plans/ITEM-ECONOMY-PLUGINS.md). Internal-only, never internet-facing.

---

## Why Not lib-affix? (Architectural Rationale)

The question arises: why not combine crafting operations with the modifier system?

**The answer is: lib-affix is a data layer; lib-craft is a workflow layer.**

| Concern | lib-affix (L4) | lib-craft (L4) |
|---------|---------------|----------------|
| **Core entity** | AffixDefinition (modifier templates) | RecipeDefinition (workflow templates) |
| **What it knows** | "T3 fire resistance suffix exists with weight 400 and requires iLvl 74" | "Enchanting recipe 'inscribe_fire' requires an enchanting table, fire reagents, and Enchanting proficiency 5" |
| **Operations** | Apply/remove validated modifier, generate pool, roll values | Execute recipe steps, consume materials, check proficiency, determine quality, produce outputs |
| **State managed** | Definitions, implicit mappings, pool caches | Sessions, proficiency, discovery, station registry |
| **Session concept** | None (stateless operations) | Multi-step Contract-backed sessions with milestone progression |
| **Skill concept** | None | Proficiency domains with experience and leveling |
| **Physical world** | None | Station locations, tool requirements, environmental factors |

lib-affix answers "what can go on this item?" lib-craft answers "what does this entity need to do to put it there?"

**The independence test**: lib-loot generates items with modifiers using lib-affix directly -- no crafting session, no proficiency check, no station. NPCs evaluate item worth using lib-affix's variable provider -- no crafting context needed. lib-market indexes items by modifier properties via lib-affix -- no crafting involvement. These consumers use affix data without any concept of crafting workflows. Conversely, production crafting (smelting, cooking, weaving) uses lib-craft without any lib-affix involvement -- the outputs are items created from templates, not items with randomized modifiers.

---

## The Logos Crafting Model (Arcadia Game Integration)

> **This section describes Arcadia-specific lore**. lib-craft is game-agnostic; these mappings are applied through recipe configuration, ABML behaviors, and narrative design -- not code.

In Arcadia, crafting is the practical application of logos manipulation. Every crafting discipline works with the same metaphysical substrate (pneuma flow through logos patterns) but through different techniques and traditions.

**Physical crafting** (blacksmithing, alchemy, cooking) works with the logos patterns inherent in materials. A blacksmith doesn't just heat and hammer steel -- they coax the steel's logos into a new arrangement. The quality of the result depends on how well the crafter reads and guides the material's natural logos resonance. Master craftsmen can feel when the steel "wants" to become a blade vs. a shield.

**Enchanting** (a subset of modification crafting) is deliberate logos inscription. An enchanter prepares an item's logos structure (creating attunement channels), then flows specific pneuma patterns through those channels to inscribe new properties. This maps directly to lib-craft calling lib-affix's primitives -- the recipe defines the process, lib-affix validates and applies the modifier.

**The 37+ authentic processes** mirror real-world techniques because Arcadia's physics are grounded in thermodynamic reality enhanced by pneuma. Smelting works like real smelting (heat + flux + ore -> metal + slag) with the addition that pneuma-aware smelters can influence trace element distribution. Alchemy follows real chemistry principles with pneuma catalysis adding impossible reactions. Each process has distinct steps, timing, and environmental requirements that map to recipe step definitions.

**NPC crafting as economic engine**: NPC artisans are the backbone of Arcadia's economy. A blacksmith NPC doesn't just sell swords -- they acquire materials from miners and merchants, maintain their forge, and produce items based on demand, skill, and personality preferences. Their crafting seed (via lib-seed) tracks mastery across specializations. The `${craft.*}` variable provider feeds GOAP decisions: "I have materials for 3 swords and 1 shield. Swords sell better but I'm more skilled at shields. My personality favors quality over quantity."

---

## Core Concepts

### Recipe Definitions (The Template Layer)

A recipe definition is the template that describes a crafting workflow. It defines what the recipe produces, what it consumes, what steps are required, and what constraints apply.

```
RecipeDefinition:
  recipeId: Guid
  gameServiceId: Guid
  code: string                    # Unique within game service (e.g., "smelt_iron_ingot")

  # Classification
  recipeType: string              # "production", "modification", "extraction"
  domain: string                  # Proficiency domain (e.g., "blacksmithing", "enchanting", "alchemy")
  category: string                # Broad classification (e.g., "weapons", "potions", "enchantments")
  tags: [string]                  # For filtering and discovery (e.g., ["fire", "melee", "basic"])

  # Inputs (materials consumed on completion)
  inputs:
    - itemTemplateCode: string    # Required material
      quantity: decimal           # Amount consumed
      qualityMinimum: decimal?    # Minimum quality required (null = any)
      consumeOnStep: string?      # Which step consumes this (null = final step)

  # Currency costs
  currencyCosts:
    - currencyCode: string
      amount: decimal

  # For modification recipes: target item requirements
  targetRequirements:
    validItemClasses: [string]?   # Item template categories the recipe can target
    minimumItemLevel: int?
    requiredStates: object?       # e.g., {"isIdentified": true, "isCorrupted": false}
    requiredAffixSlotType: string? # For "add specific affix type" recipes

  # Affix operation (modification recipes only, requires lib-affix)
  affixOperation: string?         # "apply_random", "remove_random", "reroll_all",
                                  # "reroll_values", "corrupt", "fracture", "apply_specific"
  affixOperationParams: object?   # Operation-specific params (weight modifiers, guaranteed mod code, etc.)

  # Steps (ordered)
  steps:
    - code: string                # "prepare", "heat", "hammer", "quench"
      name: string
      durationSeconds: int?       # Real-time duration (null = instant)
      stationType: string?        # Required station type (null = no station needed)
      toolCategory: string?       # Required tool category (null = no tool needed)
      skillCheck: bool            # Whether this step has a quality-affecting skill check

  # Outputs (production recipes)
  outputs:
    - itemTemplateCode: string
      quantity: decimal
      qualityInfluence: decimal   # How much recipe quality affects this output (0-1)

  # Extraction outputs (extraction recipes)
  extractionOutputs:
    - itemTemplateCode: string
      baseQuantity: decimal
      quantityVariance: decimal   # +/- variance on quantity
      probability: decimal        # Chance of this output (0-1)

  # Proficiency requirements
  proficiencyRequirements:
    - domain: string              # Must match a proficiency domain
      minimumLevel: int

  # Quality formula weights (all sum to 1.0)
  qualityWeights:
    materialQuality: decimal      # How much input quality affects output (0-1)
    proficiencyLevel: decimal     # How much skill affects output (0-1)
    toolQuality: decimal          # How much tool quality affects output (0-1)

  # Experience granted on completion
  experienceGrants:
    - domain: string
      baseExperience: decimal
      firstCraftBonus: decimal?   # Extra XP for first successful craft of this recipe

  # Discovery
  isDiscoverable: bool            # Can be discovered through experimentation
  discoveryHints: [string]?       # Hints for discovery system (e.g., ["combine_fire_reagent_with_weapon"])
  prerequisiteRecipeCodes: [string]? # Must know these recipes before this one can be discovered

  # Metadata
  isActive: bool
  isDeprecated: bool
```

**Key design decisions**:

1. **Recipe types are opaque strings**, not enums. "production", "modification", "extraction" are conventions. A game might add "transmutation", "ritual", "assembly" as types with custom handling.

2. **Affix operations are specified declaratively**: Modification recipes declare which lib-affix operation to invoke and with what parameters. lib-craft translates this into the appropriate lib-affix API calls. The recipe doesn't contain affix logic -- it references an operation by name.

3. **Steps are ordered milestones** that map directly to Contract milestones. Starting a crafting session creates a Contract instance whose milestones mirror the recipe's steps.

4. **Quality is a computed outcome**, not a property of the recipe. Quality weights define how much each factor (materials, skill, tools) contributes to the final quality score (0-1 normalized). The quality score influences output properties (stat rolls, durability, visual fidelity).

5. **Proficiency domains are opaque strings**. "blacksmithing", "enchanting", "alchemy" are Arcadia conventions. A game might use "crafting" as a single domain, or "melee_weapons" + "ranged_weapons" + "armor" for fine-grained specialization.

### Crafting Sessions (Contract Integration)

A crafting session is a live execution of a recipe by an entity. Sessions are backed by Contract instances, providing the state machine, milestone progression, and prebound API execution that lib-craft needs.

```
Crafting Session Flow:

  /craft/session/start
      |
      +-- Validate: entity has required proficiency
      +-- Validate: entity has required materials in inventory
      +-- Validate: station available (if required)
      +-- Validate: tool available (if required)
      +-- Lock materials (authorization hold on currency, reservation on items)
      |
      +-- Create Contract instance from recipe's step structure
      |     Milestones = recipe steps
      |     onComplete (final step):
      |       - /craft/internal/complete-session
      |
      +-- Return sessionId (= contractInstanceId)

  /craft/session/advance (per step)
      |
      +-- Validate: step prerequisites met (previous steps complete)
      +-- Validate: station still available, tool still held
      +-- If step has skillCheck: roll quality factor for this step
      +-- If step has durationSeconds: wait (or skip with timer)
      +-- If step has consumeOnStep material: consume it now
      |
      +-- Complete Contract milestone for this step
      +-- Return step result (quality contribution, remaining steps)

  /craft/internal/complete-session (prebound API, called by Contract)
      |
      +-- Consume remaining materials
      +-- Compute final quality from accumulated step quality + weights
      |
      +-- If production:
      |     Create output items via lib-item
      |     Place in entity's inventory via lib-inventory
      |     Quality affects: stat rolls, durability, rarity
      |
      +-- If modification:
      |     Call lib-affix operation on target item
      |     Quality affects: tier selection, value rolls
      |
      +-- If extraction:
      |     Destroy target item via lib-item
      |     Create extracted outputs (probabilistic)
      |     Place in inventory
      |
      +-- Grant proficiency experience
      +-- Publish crafting events
```

**Why Contract integration matters**: Contracts provide durable state machines. If the server crashes mid-craft, the Contract persists the session state. Steps can have real-time durations (a forging step takes 30 seconds of in-game time). Prebound APIs on the final milestone handle the actual item creation/modification, ensuring atomicity.

### Recipe Types In Detail

#### Production Recipes

Transform input materials into output items. The economic backbone of NPC crafting.

```
Example: "Forge Iron Sword"
  recipeType: "production"
  domain: "blacksmithing"
  inputs:
    - {itemTemplateCode: "iron_ingot", quantity: 3}
    - {itemTemplateCode: "leather_strip", quantity: 1}
  currencyCosts: []
  steps:
    - {code: "heat_metal", stationType: "forge", durationSeconds: 15, skillCheck: true}
    - {code: "hammer_blade", stationType: "forge", toolCategory: "hammer", skillCheck: true}
    - {code: "quench", stationType: "forge", skillCheck: false}
    - {code: "attach_grip", toolCategory: "hammer", skillCheck: false}
  outputs:
    - {itemTemplateCode: "iron_sword", quantity: 1, qualityInfluence: 1.0}
  proficiencyRequirements:
    - {domain: "blacksmithing", minimumLevel: 3}
  qualityWeights:
    materialQuality: 0.3
    proficiencyLevel: 0.5
    toolQuality: 0.2
```

#### Modification Recipes

Transform an existing item using lib-affix operations. Maps to PoE-style currency effects, enchanting, and similar modification mechanics.

```
Example: "Chaos Reforge" (PoE's Chaos Orb equivalent)
  recipeType: "modification"
  domain: "enchanting"
  inputs:
    - {itemTemplateCode: "chaos_reagent", quantity: 1}
  targetRequirements:
    requiredStates: {"isCorrupted": false, "isMirrored": false}
  affixOperation: "reroll_all"
  affixOperationParams: {}
  steps:
    - {code: "apply", skillCheck: false}
  proficiencyRequirements: []

Example: "Inscribe Fire Logos" (Arcadia enchanting)
  recipeType: "modification"
  domain: "enchanting"
  inputs:
    - {itemTemplateCode: "fire_reagent", quantity: 2}
    - {itemTemplateCode: "inscription_ink", quantity: 1}
  targetRequirements:
    requiredStates: {"isCorrupted": false, "isIdentified": true}
    requiredAffixSlotType: "suffix"
  affixOperation: "apply_random"
  affixOperationParams:
    weightModifiers: {"fire": 5.0, "cold": 0.0, "lightning": 0.0}
    slotType: "suffix"
  steps:
    - {code: "prepare_channels", stationType: "enchanting_table", durationSeconds: 10, skillCheck: true}
    - {code: "flow_pneuma", stationType: "enchanting_table", toolCategory: "inscription_tools", skillCheck: true}
    - {code: "stabilize", stationType: "enchanting_table", skillCheck: true}
  proficiencyRequirements:
    - {domain: "enchanting", minimumLevel: 5}
  qualityWeights:
    materialQuality: 0.2
    proficiencyLevel: 0.6
    toolQuality: 0.2
```

For modification recipes, quality influences which tiers of affixes can roll and how close to max values the rolls land. Higher quality = higher probability of better tiers and better rolls within the tier's range.

#### Extraction Recipes

Destroy an item and recover components. Enables material recycling and the "salvage" economy.

```
Example: "Salvage Weapon"
  recipeType: "extraction"
  domain: "blacksmithing"
  inputs: []
  targetRequirements:
    validItemClasses: ["one_hand_sword", "two_hand_sword", "axe", "mace"]
  steps:
    - {code: "disassemble", stationType: "forge", toolCategory: "hammer", skillCheck: true}
  extractionOutputs:
    - {itemTemplateCode: "iron_ingot", baseQuantity: 1, quantityVariance: 1, probability: 1.0}
    - {itemTemplateCode: "leather_strip", baseQuantity: 0, quantityVariance: 1, probability: 0.5}
  proficiencyRequirements:
    - {domain: "blacksmithing", minimumLevel: 1}
```

### Proficiency (Seed Integration)

Crafting proficiency tracks an entity's skill in specific crafting domains. Rather than building a custom leveling system, lib-craft delegates proficiency to **lib-seed** (L2) -- crafting mastery is a seed that grows through practice.

Each crafting domain (blacksmithing, enchanting, alchemy, etc.) maps to a seed type. When an entity crafts, the experience grant from the recipe feeds growth into the corresponding seed. The seed's growth phases represent mastery tiers:

```
Seed Type: "proficiency:blacksmithing"
  Phases:
    novice     (0-99 growth)     -> Can craft basic recipes
    apprentice (100-499)         -> Unlocks intermediate recipes, quality bonus +10%
    journeyman (500-1499)        -> Unlocks advanced recipes, quality bonus +25%
    expert     (1500-3999)       -> Unlocks expert recipes, quality bonus +40%
    master     (4000+)           -> Unlocks master recipes, quality bonus +50%, special techniques
```

**Why lib-seed**: The seed system already provides polymorphic ownership, configurable growth phases, capability manifests, and growth event publishing. Reusing it avoids duplicating all that infrastructure. A blacksmith NPC's crafting seed is the same architectural pattern as any other seed -- it just uses `${craft.*}` variables instead of `${seed.*}`.

**Proficiency lookup**: When validating a crafting session, lib-craft queries the entity's seed for the relevant domain, reads its current phase, and compares against the recipe's proficiency requirements. The seed's growth state is also a factor in the quality formula.

### Stations and Tools

Stations are fixed locations where crafting occurs. Tools are items held by the crafter that affect outcome quality.

**Stations** are registered per game service with location associations:

```
StationDefinition:
  stationId: Guid
  gameServiceId: Guid
  stationType: string            # "forge", "enchanting_table", "alchemy_bench", "loom"
  locationId: Guid?              # Physical location (null = portable/anywhere)
  ownerId: Guid?                 # Entity that owns/maintains this station
  ownerType: string?
  quality: decimal               # Station quality (0-1), affects crafting output
  isActive: bool
```

**Tools** are item instances with a tool category. lib-craft reads the tool's quality from its item metadata. Tool quality is a factor in the quality formula.

**Station and tool validation** happens at session start. If a recipe requires `stationType: "forge"`, lib-craft checks that a forge station exists at the crafter's location (or within interaction range). If a recipe requires `toolCategory: "hammer"`, lib-craft checks that the crafter has a hammer-category item equipped or in their inventory.

### Quality System

Every crafting session produces a quality score (0-1 normalized) that influences output properties:

```
quality = (materialQuality * weights.materialQuality)
        + (proficiencyFactor * weights.proficiencyLevel)
        + (toolQuality * weights.toolQuality)
        + stepBonuses

where:
  materialQuality = average quality of input materials (from instanceMetadata)
  proficiencyFactor = normalized proficiency level (current / max for recipe tier)
  toolQuality = tool's quality value (from instanceMetadata, default 0.5)
  stepBonuses = accumulated quality contributions from skill-check steps
```

**Quality effects by recipe type**:

| Recipe Type | Quality Effect |
|-------------|---------------|
| Production | Output item quality metadata, stat rolls, durability |
| Modification | Affix tier weighting (higher quality = better tiers more likely), value roll percentile |
| Extraction | Output quantity multiplier, chance of bonus rare materials |

### Discovery

Entities can discover new recipes through experimentation. The discovery system manages what recipes are known per entity and how new ones are found.

**Known recipes** are tracked per entity (character or NPC). An entity can only execute recipes they know. Recipes can be learned through:

1. **Automatic**: Some recipes are known by default (basic recipes for a domain)
2. **Prerequisite**: Knowing recipe A unlocks recipe B
3. **Experimentation**: Combining specific items at a station with the intent to discover
4. **Teaching**: Another entity (NPC trainer, player mentor) teaches the recipe
5. **Scroll/item**: A recipe scroll item grants knowledge when used (via lib-item's contract delegation)

**Experimentation** is a special discovery mode where the entity provides materials and a station, and lib-craft checks if the combination matches any discoverable recipe's `discoveryHints`. Discovery attempts consume a subset of materials regardless of success.

### NPC Crafting (GOAP Integration)

NPCs use crafting as part of their autonomous economic behavior. The `${craft.*}` variable provider exposes crafting state to ABML behavior expressions:

```
${craft.can_craft.<recipe_code>}     -- Can this NPC craft this recipe? (proficiency + materials + station)
${craft.proficiency.<domain>}        -- Proficiency level in a domain
${craft.materials_for.<recipe_code>} -- Does the NPC have materials for this recipe?
${craft.station_nearby.<type>}       -- Is a station of this type nearby?
${craft.best_recipe}                 -- Highest-value recipe the NPC can currently craft
${craft.queue_depth}                 -- How many items queued for crafting
${craft.last_craft_quality}          -- Quality of last completed craft
```

NPC crafting decisions are driven by GOAP:
- **Goal**: "Earn money" -> Action: "Craft iron swords" (has materials, sword sells well)
- **Goal**: "Improve skill" -> Action: "Practice enchanting" (has cheap materials, gains XP)
- **Goal**: "Fill shop inventory" -> Action: "Craft what's low in stock"
- **Goal**: "Fulfill order" -> Action: "Craft specific commissioned item"

---

## Dependencies (What This Plugin Relies On)

### Hard Dependencies (constructor injection -- crash if missing)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Recipe definitions (MySQL), sessions (MySQL), proficiency (MySQL), station registry (MySQL), discovery (MySQL), recipe cache (Redis), distributed locks (Redis) |
| lib-state (`IDistributedLockProvider`) | Distributed locks for session operations and recipe mutations |
| lib-messaging (`IMessageBus`) | Publishing crafting lifecycle events, error events |
| lib-item (`IItemClient`) | Creating output items, destroying items for extraction, reading item metadata for material quality, tool quality (L2) |
| lib-inventory (`IInventoryClient`) | Consuming materials from containers, placing outputs in containers, checking tool availability (L2) |
| lib-contract (`IContractClient`) | Creating session contracts from recipe step structures, milestone progression (L1) |
| lib-currency (`ICurrencyClient`) | Deducting currency costs for recipes (L2) |
| lib-game-service (`IGameServiceClient`) | Validating game service existence for recipe scoping (L2) |

### Soft Dependencies (runtime resolution via `IServiceProvider` -- graceful degradation)

| Dependency | Usage | Behavior When Missing |
|------------|-------|-----------------------|
| lib-affix (`IAffixClient`) | Executing modification recipe affix operations (apply, remove, reroll, etc.) | Modification recipes return error; production and extraction recipes work normally |
| lib-seed (`ISeedClient`) | Reading/updating proficiency seeds for crafting skill tracking | Proficiency checks skip (all recipes available); experience not granted |
| lib-location (`ILocationClient`) | Resolving station locations for proximity checks | Station location checks skip (station type match only) |
| lib-analytics (`IAnalyticsClient`) | Publishing crafting statistics for economy monitoring | Statistics not collected; crafting works normally |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-loot (L4, future) | May invoke production recipes for "crafted loot" drops (items that appear crafted rather than dropped) |
| lib-market (L4, future) | Reads crafting cost data for price estimation heuristics |
| NPC Actor runtime (L2, via Variable Provider) | `${craft.*}` variables for crafting GOAP decisions |

---

## State Storage

### Recipe Definition Store
**Store**: `craft-recipe-store` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `recipe:{recipeId}` | `RecipeDefinitionModel` | Primary lookup by recipe ID |
| `recipe-code:{gameServiceId}:{code}` | `RecipeDefinitionModel` | Code-uniqueness lookup within game service |

Paginated queries by gameServiceId + optional filters (recipeType, domain, category, tags, proficiency domain, proficiency level range) use `IJsonQueryableStateStore<RecipeDefinitionModel>.JsonQueryPagedAsync()`.

### Recipe Definition Cache
**Store**: `craft-recipe-cache` (Backend: Redis, prefix: `craft:recipe`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `recipe:{recipeId}` | `RecipeDefinitionModel` | Recipe hot cache (read-through from MySQL) |
| `recipe-domain:{gameServiceId}:{domain}` | `List<RecipeDefinitionModel>` | All recipes in a domain (for proficiency-based filtering) |

**TTL**: `RecipeCacheTtlSeconds` (default: 3600, 1 hour).

### Crafting Session Store
**Store**: `craft-session-store` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `session:{sessionId}` | `CraftingSessionModel` | Active session state (sessionId = contractInstanceId) |
| `session-entity:{entityId}:{entityType}` | `List<string>` | Active session IDs for an entity |

Sessions are cleaned up on completion, cancellation, or expiration.

### Proficiency Store
**Store**: `craft-proficiency-store` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `prof:{entityId}:{entityType}:{domain}` | `ProficiencyModel` | Proficiency state per entity per domain |

**Note**: If lib-seed integration is active, proficiency data is delegated to seed state. This store serves as a fallback when lib-seed is unavailable.

### Station Registry
**Store**: `craft-station-registry` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `station:{stationId}` | `StationDefinitionModel` | Station definition and location |
| `station-loc:{locationId}` | `List<string>` | Station IDs at a location |
| `station-type:{gameServiceId}:{stationType}` | `List<string>` | Station IDs by type |

### Discovery Store
**Store**: `craft-discovery-store` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `known:{entityId}:{entityType}:{gameServiceId}` | `KnownRecipesModel` | Set of recipe codes known by this entity |
| `discovery-attempt:{entityId}:{hash}` | `DiscoveryAttemptModel` | Recent discovery attempts (for hint progression) |

### Distributed Locks
**Store**: `craft-lock` (Backend: Redis, prefix: `craft:lock`)

| Key Pattern | Purpose |
|-------------|---------|
| `session:{sessionId}` | Session step advancement lock (prevents concurrent step completion) |
| `recipe:{recipeId}` | Recipe definition mutation lock |
| `station:{stationId}` | Station occupancy lock (one session per station at a time, if configured) |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `craft-recipe.created` | `CraftRecipeCreatedEvent` | Recipe definition created (lifecycle) |
| `craft-recipe.updated` | `CraftRecipeUpdatedEvent` | Recipe definition updated (lifecycle) |
| `craft-recipe.deprecated` | `CraftRecipeDeprecatedEvent` | Recipe definition deprecated (lifecycle) |
| `craft-session.started` | `CraftSessionStartedEvent` | Crafting session started |
| `craft-session.step-completed` | `CraftStepCompletedEvent` | A recipe step completed (includes quality contribution) |
| `craft-session.completed` | `CraftSessionCompletedEvent` | Session completed successfully (includes output items, quality score) |
| `craft-session.failed` | `CraftSessionFailedEvent` | Session failed (material returned or consumed depending on step) |
| `craft-session.cancelled` | `CraftSessionCancelledEvent` | Session cancelled by entity (materials returned) |
| `craft-proficiency.gained` | `CraftProficiencyGainedEvent` | Entity gained crafting experience |
| `craft-proficiency.leveled` | `CraftProficiencyLeveledEvent` | Entity reached a new proficiency level |
| `craft-recipe.discovered` | `CraftRecipeDiscoveredEvent` | Entity discovered a new recipe |

### Consumed Events

| Topic | Handler | Action |
|-------|---------|--------|
| `contract.terminated` | `HandleContractTerminated` | Clean up crafting sessions whose backing contract has terminated (timeout, breach) |

### Resource Cleanup (T28)

| Target Resource | Source Type | On Delete | Cleanup Endpoint |
|----------------|-------------|-----------|-----------------|
| game-service | craft | CASCADE | `/craft/cleanup-by-game-service` |
| character | craft | CASCADE | `/craft/cleanup-by-entity` |

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `RecipeCacheTtlSeconds` | `CRAFT_RECIPE_CACHE_TTL_SECONDS` | `3600` | Recipe definition cache TTL (1 hour) |
| `MaxActiveSessionsPerEntity` | `CRAFT_MAX_ACTIVE_SESSIONS_PER_ENTITY` | `1` | Maximum concurrent crafting sessions per entity |
| `SessionTimeoutSeconds` | `CRAFT_SESSION_TIMEOUT_SECONDS` | `3600` | Maximum session duration before auto-cancellation (1 hour) |
| `DefaultQualityMaterialWeight` | `CRAFT_DEFAULT_QUALITY_MATERIAL_WEIGHT` | `0.3` | Default material quality weight when recipe doesn't specify |
| `DefaultQualityProficiencyWeight` | `CRAFT_DEFAULT_QUALITY_PROFICIENCY_WEIGHT` | `0.5` | Default proficiency quality weight |
| `DefaultQualityToolWeight` | `CRAFT_DEFAULT_QUALITY_TOOL_WEIGHT` | `0.2` | Default tool quality weight |
| `DiscoveryMaterialConsumptionRate` | `CRAFT_DISCOVERY_MATERIAL_CONSUMPTION_RATE` | `0.5` | Fraction of materials consumed on failed discovery attempt |
| `MaxRecipesPerGameService` | `CRAFT_MAX_RECIPES_PER_GAME_SERVICE` | `10000` | Safety limit for recipe count per game service |
| `LockTimeoutSeconds` | `CRAFT_LOCK_TIMEOUT_SECONDS` | `30` | Distributed lock timeout |
| `StationExclusivity` | `CRAFT_STATION_EXCLUSIVITY` | `false` | Whether stations are locked to one session at a time |
| `ProficiencySource` | `CRAFT_PROFICIENCY_SOURCE` | `seed` | Proficiency backing: "seed" (lib-seed) or "local" (internal store) |
| `MaxProficiencyLevel` | `CRAFT_MAX_PROFICIENCY_LEVEL` | `100` | Maximum proficiency level per domain |
| `ExperienceScalingFactor` | `CRAFT_EXPERIENCE_SCALING_FACTOR` | `1.0` | Global experience multiplier |

---

## DI Services & Helpers

| Service | Role |
|---------|------|
| `ILogger<CraftService>` | Structured logging |
| `CraftServiceConfiguration` | Typed configuration access |
| `IStateStoreFactory` | State store access (creates 7 stores) |
| `IMessageBus` | Event publishing |
| `IDistributedLockProvider` | Distributed lock acquisition (L0) |
| `IItemClient` | Item creation/destruction, metadata reads (L2 hard) |
| `IInventoryClient` | Material consumption, output placement (L2 hard) |
| `IContractClient` | Session contract creation and milestone progression (L1 hard) |
| `ICurrencyClient` | Currency cost deduction (L2 hard) |
| `IGameServiceClient` | Game service existence validation (L2 hard) |
| `IServiceProvider` | Runtime resolution of soft L4 dependencies |

### Variable Provider Factories

| Factory | Namespace | Data Source | Registration |
|---------|-----------|-------------|--------------|
| `CraftDecisionProviderFactory` | `${craft.*}` | Reads entity's proficiency (via seed or local), known recipes, nearby stations, inventory materials | `IVariableProviderFactory` (DI singleton) |

---

## API Endpoints (Implementation Notes)

### Recipe Management (7 endpoints)

All endpoints require `developer` role.

- **CreateRecipe** (`/craft/recipe/create`): Validates game service existence. Validates code uniqueness. Validates step structure (at least one step, valid step codes). Validates input references (item template codes exist). Validates output references. Enforces `MaxRecipesPerGameService`. Saves to MySQL. Populates cache. Publishes `craft-recipe.created`.

- **GetRecipe** (`/craft/recipe/get`): Cache read-through (Redis -> MySQL -> populate cache). Supports lookup by recipeId or by gameServiceId + code.

- **ListRecipes** (`/craft/recipe/list`): Paged JSON query with required gameServiceId filter. Optional filters: recipeType, domain, category, tags (any match), proficiency requirements range. Sorted by domain then category.

- **UpdateRecipe** (`/craft/recipe/update`): Acquires distributed lock. Partial update. **Cannot change**: code, gameServiceId, recipeType (identity-level). Invalidates caches. Publishes `craft-recipe.updated`.

- **DeprecateRecipe** (`/craft/recipe/deprecate`): Marks inactive. Active sessions using this recipe continue to completion. Invalidates caches. Publishes `craft-recipe.deprecated`.

- **SeedRecipes** (`/craft/recipe/seed`): Bulk creation, skipping existing codes (idempotent). Validates game service once. Returns created/skipped counts.

- **ListDomains** (`/craft/recipe/list-domains`): Returns distinct proficiency domains for a game service with recipe counts per domain.

### Session Management (5 endpoints)

- **StartSession** (`/craft/session/start`): The core session creation. Validates: entity knows the recipe, meets proficiency requirements, has required materials, has required currency, station available (if needed), tool available (if needed). For modification: validates target item meets requirements. Creates Contract instance with milestones matching recipe steps. Records material reservations. Returns sessionId + first step info.

- **AdvanceSession** (`/craft/session/advance`): Advances to the next step. Acquires session lock. Validates: step sequence correct, station still available, tool still held. If step has `skillCheck`: computes quality contribution for this step. If step has `consumeOnStep`: consumes specified materials now. Completes the Contract milestone for this step. If final step: triggers session completion via Contract prebound API. Returns step result.

- **CancelSession** (`/craft/session/cancel`): Cancels an active session. Returns unconsumed materials to entity's inventory. Releases currency holds. Terminates the backing Contract. Publishes `craft-session.cancelled`.

- **GetSession** (`/craft/session/get`): Returns current session state: recipe info, current step, accumulated quality, remaining steps, elapsed time.

- **ListSessions** (`/craft/session/list`): Lists active sessions for an entity. Returns summary info for each.

### Proficiency (4 endpoints)

- **GetProficiency** (`/craft/proficiency/get`): Returns proficiency state for an entity in a specific domain. If `ProficiencySource` is "seed", reads from lib-seed. Otherwise reads from local store.

- **ListProficiencies** (`/craft/proficiency/list`): Returns all proficiency domains and levels for an entity.

- **GrantExperience** (`/craft/proficiency/grant`): Manually grants experience (for quest rewards, training, etc.). Validates domain exists. If using seeds, records growth via lib-seed. Publishes `craft-proficiency.gained` and optionally `craft-proficiency.leveled`.

- **SetProficiency** (`/craft/proficiency/set`): Admin endpoint. Directly sets proficiency level. Requires `developer` role.

### Station Management (4 endpoints)

All endpoints require `developer` role.

- **RegisterStation** (`/craft/station/register`): Creates a station definition at a location. Validates location exists (if lib-location available). Updates location and type indexes.

- **GetStation** (`/craft/station/get`): Returns station definition.

- **ListStations** (`/craft/station/list`): List stations with filters: stationType, locationId, gameServiceId, isActive.

- **DeregisterStation** (`/craft/station/deregister`): Marks station inactive. Active sessions at this station continue. Future sessions cannot use it.

### Discovery (3 endpoints)

- **AttemptDiscovery** (`/craft/discovery/attempt`): Entity provides materials + station type. lib-craft hashes the combination and checks against discoverable recipes' hints. If match: recipe added to known set, materials partially consumed, publishes `craft-recipe.discovered`. If no match: materials partially consumed (configurable rate), returns failure with optional hint progression.

- **ListKnownRecipes** (`/craft/discovery/list-known`): Returns all recipe codes known by an entity for a game service. Includes proficiency eligibility (can they actually craft each one?).

- **TeachRecipe** (`/craft/discovery/teach`): Adds a recipe to an entity's known set. Used by NPC trainers, recipe scroll items, quest rewards.

### Query (3 endpoints)

- **CanCraft** (`/craft/query/can-craft`): Checks if an entity can craft a specific recipe. Returns detailed breakdown: proficiency met?, materials available?, station nearby?, tool available?, currency sufficient? Used by UI and NPC GOAP.

- **EstimateCraftQuality** (`/craft/query/estimate-quality`): Given entity + recipe + specific materials + tool, estimates the output quality score without executing. Used for UI preview and NPC decision-making.

- **GetRecipeOutputPreview** (`/craft/query/output-preview`): Returns what a recipe would produce at various quality levels. Used for UI tooltips.

### Cleanup (2 endpoints)

- **CleanupByGameService** (`/craft/cleanup-by-game-service`): Deletes all recipes, sessions, stations, and discovery data for a game service. Called by lib-resource on game service deletion.

- **CleanupByEntity** (`/craft/cleanup-by-entity`): Cancels active sessions, clears discovery data, clears proficiency data for an entity. Called by lib-resource on character deletion.

---

## Visual Aid

```
+-----------------------------------------------------------------------+
|                      Craft Service Architecture                        |
|                                                                        |
|   RECIPE LAYER (owned by lib-craft, MySQL + Redis cache)               |
|   +------------------------------------------------------------------+|
|   |  RecipeDefinition                                                 ||
|   |  +------------------------+  +---------------------------+        ||
|   |  | code: "forge_iron_sw"  |  | code: "inscribe_fire"     |        ||
|   |  | type: "production"     |  | type: "modification"      |        ||
|   |  | domain: "blacksmithing"|  | domain: "enchanting"       |        ||
|   |  | inputs: iron x3,       |  | inputs: fire_reagent x2,  |        ||
|   |  |   leather x1           |  |   inscription_ink x1      |        ||
|   |  | steps: heat, hammer,   |  | affixOperation:           |        ||
|   |  |   quench, grip         |  |   "apply_random"          |        ||
|   |  | output: iron_sword x1  |  | affixParams:              |        ||
|   |  | proficiency: 3         |  |   {fire: 5.0, cold: 0.0}  |        ||
|   |  +------------------------+  +---------------------------+        ||
|   +------------------------------------------------------------------+|
|            |                                                           |
|            | /craft/session/start                                      |
|            v                                                           |
|   SESSION LAYER (Contract-backed state machine)                        |
|   +------------------------------------------------------------------+|
|   |  1. Validate proficiency, materials, station, tool                ||
|   |  2. Create Contract with milestones = recipe steps                ||
|   |  3. Lock materials (reservation / authorization hold)             ||
|   |  4. Advance through steps (with quality checks)                   ||
|   |  5. Final milestone -> prebound API -> complete session           ||
|   +------------------------------------------------------------------+|
|            |                                                           |
|   +--------+--------+                                                  |
|   |                  |                                                  |
|   v                  v                                                  |
| PRODUCTION         MODIFICATION                                        |
| +----------------+ +------------------------------------------+        |
| | Consume inputs | | Call lib-affix operation on target item   |        |
| | Create outputs | | (apply_random, reroll_all, remove, etc.) |        |
| | via lib-item   | | Quality affects tier selection & rolls   |        |
| | Place in inv   | +------------------------------------------+        |
| +----------------+                                                     |
|            |                                                           |
|            | Variable Provider Factory                                 |
|            v                                                           |
|   NPC GOAP (via Actor L2)                                              |
|   +------------------------------------------------------------------+|
|   |  ${craft.can_craft.forge_iron_sw}  -- "I can make swords"        ||
|   |  ${craft.proficiency.blacksmithing} -- "I'm level 7"             ||
|   |  ${craft.materials_for.forge_iron_sw} -- "I have materials"      ||
|   |  ${craft.best_recipe}               -- "Swords are most valuable"||
|   +------------------------------------------------------------------+|
+-----------------------------------------------------------------------+


Crafting Session Lifecycle (Production)
=========================================

  StartSession("forge_iron_sword", entityId, inventoryId)
       |
       +-- Validate proficiency: blacksmithing >= 3? YES
       +-- Validate materials: 3 iron ingots + 1 leather strip? YES
       +-- Validate station: forge nearby? YES
       +-- Reserve materials (mark as locked)
       +-- Create Contract instance (4 milestones)
       |
       |   Contract Milestones:
       |   [1] heat_metal  (15s duration, skill check)
       |   [2] hammer_blade (skill check, requires hammer tool)
       |   [3] quench
       |   [4] attach_grip (requires hammer tool)
       |       onComplete: /craft/internal/complete-session
       |
       +-- Return sessionId, step 1 info

  AdvanceSession(sessionId, step: "heat_metal")
       |
       +-- Validate: station = forge? YES
       +-- Wait 15 seconds (or timer)
       +-- Skill check: roll quality contribution = 0.82
       +-- Complete Contract milestone 1
       +-- Return: {quality: 0.82, nextStep: "hammer_blade"}

  AdvanceSession(sessionId, step: "hammer_blade")
       |
       +-- Validate: tool = hammer? YES
       +-- Skill check: roll quality contribution = 0.71
       +-- Complete Contract milestone 2
       +-- Return: {quality: 0.76, nextStep: "quench"}

  AdvanceSession(sessionId, step: "quench")
       |
       +-- No skill check (fixed step)
       +-- Complete Contract milestone 3
       +-- Return: {quality: 0.76, nextStep: "attach_grip"}

  AdvanceSession(sessionId, step: "attach_grip")
       |
       +-- Validate: tool = hammer? YES
       +-- No skill check
       +-- Complete Contract milestone 4 (FINAL)
       +-- Contract executes prebound API: /craft/internal/complete-session
       |
       +-- Complete session:
       |     materialQuality = 0.65 (average of input qualities)
       |     proficiencyFactor = 0.70 (level 7 of max 10 for this recipe)
       |     toolQuality = 0.80 (good hammer)
       |     stepBonuses = 0.06 (from skill checks)
       |
       |     finalQuality = (0.65 * 0.3) + (0.70 * 0.5) + (0.80 * 0.2) + 0.06
       |                  = 0.195 + 0.350 + 0.160 + 0.06
       |                  = 0.765
       |
       |     Consume 3 iron ingots + 1 leather strip
       |     Create iron_sword (quality: 0.765)
       |     Place in entity's inventory
       |     Grant blacksmithing XP: 50
       |
       +-- Publish craft-session.completed
       +-- Return: {outputItems: [iron_sword], quality: 0.765}
```

---

## Stubs & Unimplemented Features

**Everything is unimplemented.** This is a pre-implementation architectural specification. No schema, no generated code, no service implementation exists. The following phases are planned:

### Phase 1: Recipe Infrastructure
- Create craft-api.yaml schema with all endpoints
- Create craft-events.yaml schema
- Create craft-configuration.yaml schema
- Generate service code
- Implement recipe CRUD (create, get, list, update, deprecate, seed, list-domains)
- Implement recipe cache warming and invalidation

### Phase 2: Session Management (Production)
- Implement session start with material validation and reservation
- Implement step advancement with Contract milestone progression
- Implement session completion with item creation and placement
- Implement session cancellation with material return
- Implement Contract event handler for session cleanup

### Phase 3: Modification & Extraction
- Implement modification recipe execution (lib-affix integration)
- Implement extraction recipe execution
- Implement quality-to-affix-tier mapping for modification recipes
- Test with simple modification recipes (single-step currency effects)
- Test with multi-step enchanting recipes

### Phase 4: Proficiency & Discovery
- Implement proficiency tracking (seed-backed and local fallback)
- Implement experience granting and level progression
- Implement recipe discovery system
- Implement known recipes tracking
- Implement teaching endpoint

### Phase 5: Stations, Tools & NPC Integration
- Implement station registry
- Implement station/tool validation in sessions
- Implement variable provider factory (`CraftDecisionProviderFactory`)
- Integration testing with Actor runtime
- Station occupancy locking (if configured)

### Phase 6: Quality & Events
- Implement quality formula computation
- Implement quality effects on output items
- Implement all event publishing
- Resource cleanup endpoints
- Integration testing with lib-affix, lib-seed, lib-inventory

---

## Potential Extensions

1. **Crafting queues**: Allow NPCs to queue multiple crafting sessions that execute sequentially. The current `MaxActiveSessionsPerEntity` limits concurrency, but a queue system would let NPCs plan their work schedule. Important for the "NPC artisan as economic actor" vision.

2. **Batch production**: A "craft N of recipe X" endpoint that optimizes material consumption and session overhead for repetitive production. Instead of N sessions with N contracts, a single batch session with one contract and a quantity multiplier.

3. **Crafting commissions**: An entity commissions another entity to craft something. The commissioner provides materials and payment; the crafter provides skill and station. Maps naturally to a Contract with two parties.

4. **Recipe experimentation with partial results**: Failed discovery attempts that are "close" to a valid recipe produce a degraded or unexpected output instead of nothing. Encourages experimentation.

5. **Environmental factors**: Location-specific quality bonuses (mountain forges produce better metal, coastal alchemy produces better water-based potions). Reads location metadata to apply quality modifiers.

6. **Crafting events as spectacle**: Publish real-time step progress as client events so nearby players can watch an NPC craft. "The blacksmith raises the hammer, strikes the blade, sparks fly" -- driven by step completion events.

7. **Recipe chains**: Recipes that require outputs of other recipes as inputs, forming dependency trees. "Forge steel sword" requires "smelt steel ingot" which requires "refine iron ore". lib-craft could validate the full chain and present it as a crafting plan.

8. **Tool wear**: Tools lose durability during crafting. Maps to lib-item's durability system. High-quality tools last longer and produce better results.

9. **Apprenticeship**: Entity A mentors entity B, granting bonus XP when both craft together. Tracked as a relationship (lib-relationship) that lib-craft checks during experience calculation.

10. **Client events**: `craft-client-events.yaml` for pushing real-time session progress to connected WebSocket clients (step completed, quality rolling, item created).

---

## Known Quirks & Caveats

### Intentional Quirks (Documented Behavior)

1. **Recipe types are opaque strings, not enums**: "production", "modification", "extraction" are conventions. lib-craft validates that modification recipes have `affixOperation` specified and production recipes have `outputs` specified, but doesn't restrict which type strings are valid. Custom types require custom handling in the completion path.

2. **Proficiency domains are per-game-service, not global**: Two games can have proficiency domains with the same name that are entirely independent.

3. **Sessions are Contract-backed, not independently persisted**: The session store tracks crafting-specific metadata (quality accumulation, reserved materials) but the session's lifecycle state is owned by the Contract. If the Contract terminates unexpectedly, the session cleanup handler fires.

4. **Material reservation is advisory, not enforced by lib-item**: When a session starts, lib-craft records which items are reserved, but lib-item doesn't enforce reservation locks. If another operation destroys a reserved item, the session fails at the consumption step.

5. **Quality is deterministic given inputs**: The same entity with the same proficiency using the same materials at the same station with the same tool produces the same quality score. The randomness comes from skill check steps, which use a seeded PRNG for reproducibility in testing.

6. **Station exclusivity is optional**: By default, multiple entities can use the same station simultaneously. When `StationExclusivity` is enabled, stations are locked during sessions. This is a game design choice -- a busy forge might be realistic but frustrating.

7. **Modification recipes with lib-affix unavailable fail gracefully**: If lib-affix is not loaded and an entity attempts a modification recipe, the session start returns an error explaining that modification crafting is unavailable. Production and extraction recipes are unaffected.

8. **Discovery hints are opaque strings**: The discovery system matches entity-provided material combinations against recipe hint strings using a configurable matching algorithm. The default is exact-match on sorted material codes, but games can implement fuzzy matching via configuration.

### Design Considerations (Requires Planning)

1. **Quality-to-affix mapping**: When a modification recipe produces a quality score, how does that map to lib-affix tier selection? A linear mapping (quality 0.8 = 80th percentile tier) is simple but may not match game feel. May need configurable quality curves per game.

2. **Concurrent crafting and item modification**: If entity A is crafting with a target item and entity B tries to modify the same item, who wins? The item lock in lib-affix prevents concurrent modification, but lib-craft should also check item lock state before starting a modification session.

3. **Real-time step durations**: Steps with `durationSeconds` need a timer mechanism. Options: (a) Contract timer milestones, (b) client-side timer with server validation, (c) background service polling. Contract timer milestones are the cleanest architectural fit.

4. **Proficiency seed type registration**: If using lib-seed for proficiency, who registers the seed types? lib-craft should register `proficiency:{domain}` seed types on startup for each domain it discovers in recipe definitions. This means seed type registration happens after recipe seeding.

5. **Cross-entity crafting**: Can entity A start a session and entity B advance a step? (e.g., an apprentice assists a master). The current model assumes single-entity sessions. Multi-entity crafting could use Contract's multi-party model.

6. **Material quality propagation**: How does material quality get set in the first place? Production recipe outputs carry quality from the session. But raw materials (mined ore, harvested herbs) need quality set at creation time by the gathering system (future lib-gathering or game-specific logic).

7. **Offline NPC crafting**: When an NPC's actor is running and they're crafting, step advancement happens through GOAP actions. But what about when the NPC's actor is suspended? Options: (a) sessions pause when actor suspends, (b) sessions auto-complete on a timer, (c) sessions are represented as Contract timer milestones that progress independently of the actor.

---

## Work Tracking

*No active work items. Plugin is in pre-implementation phase. See [ITEM-ECONOMY-PLUGINS.md](../plans/ITEM-ECONOMY-PLUGINS.md) for the landscape survey and prioritization.*
