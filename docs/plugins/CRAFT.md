# Craft Plugin Deep Dive

> **Plugin**: lib-craft (not yet created)
> **Schema**: `schemas/craft-api.yaml` (not yet created)
> **Version**: N/A (Pre-Implementation)
> **State Store**: craft-recipe-store (MySQL), craft-recipe-cache (Redis), craft-session-store (MySQL), craft-station-registry (MySQL), craft-discovery-store (MySQL), craft-lock (Redis) — all planned
> **Layer**: GameFeatures
> **Status**: Aspirational — no schema, no generated code, no service implementation exists.
> **Implementation Map**: [docs/maps/CRAFT.md](../maps/CRAFT.md)
> **Planning**: [Economy System Guide](../guides/ECONOMY-SYSTEM.md)
> **Short**: Recipe-based crafting orchestration composing Item, Inventory, Contract, Currency, and Affix

---

## Overview

Recipe-based crafting orchestration service (L4 GameFeatures) for production workflows, item modification, and skill-gated crafting execution. A thin orchestration layer that composes existing Bannou primitives: lib-item for storage, lib-inventory for material consumption and output placement, lib-contract for multi-step session state machines, lib-currency for costs, and lib-affix for modifier operations on existing items. Game-agnostic: recipe types, proficiency domains, station types, tool categories, and quality formulas are all opaque strings defined per game at deployment time through recipe seeding. Internal-only, never internet-facing.

---

## Why Not lib-affix? (Architectural Rationale)

lib-craft manages HOW items are created and transformed -- recipes, steps, materials, skill requirements, station constraints, quality formulas, discovery. WHAT modifiers exist and their generation rules is lib-affix's domain. WHO triggers crafting (players, NPCs, automated systems) is the caller's concern. This means lib-craft has two primary consumer patterns: production consumers (NPC blacksmiths, player crafters, automated factories) that create new items from materials, and modification consumers (NPC enchanters, player crafters using currency items) that transform existing items using lib-affix primitives.

The question arises: why not combine crafting operations with the modifier system?

**The answer is: lib-affix is a data layer; lib-craft is a workflow layer.**

| Concern | lib-affix (L4) | lib-craft (L4) |
|---------|---------------|----------------|
| **Core entity** | AffixDefinition (modifier templates) | RecipeDefinition (workflow templates) |
| **What it knows** | "fire resistance suffix exists with weight 400 and requires iLvl 74" | "Enchanting recipe 'inscribe_fire' requires an enchanting table, fire reagents, and Enchanting proficiency 5" |
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

**Three recipe paradigms**:

| Paradigm | What Happens | Example | Delegates To |
|----------|-------------|---------|-------------|
| **Production** | Inputs consumed, outputs created | Smelt ore into ingots, forge sword from steel | lib-item (create), lib-inventory (consume/place) |
| **Modification** | Existing item transformed in place | Reroll affixes, add enchantment, corrupt | lib-affix (apply/remove/reroll/state), lib-item (metadata write) |
| **Extraction** | Existing item destroyed, components recovered | Salvage weapon for materials, disenchant for reagents | lib-item (destroy), lib-inventory (place outputs) |

### Recipe Definitions (The Template Layer)

A recipe definition is the template that describes a crafting workflow. It defines what the recipe produces, what it consumes, what steps are required, and what constraints apply.

```
RecipeDefinition:
 recipeId: Guid
 gameServiceId: Guid
 code: string # Unique within game service (e.g., "smelt_iron_ingot")

 # Classification
 recipeType: string # "production", "modification", "extraction"
 domain: string # Proficiency domain (e.g., "blacksmithing", "enchanting", "alchemy")
 category: string # Broad classification (e.g., "weapons", "potions", "enchantments")
 tags: [string] # For filtering and discovery (e.g., ["fire", "melee", "basic"])

 # Inputs (materials consumed on completion)
 inputs:
 - itemTemplateCode: string # Required material
 quantity: decimal # Amount consumed
 qualityMinimum: decimal? # Minimum quality required (null = any)
 consumeOnStep: string? # Which step consumes this (null = final step)

 # Currency costs
 currencyCosts:
 - currencyCode: string
 amount: decimal

 # For modification recipes: target item requirements
 targetRequirements:
 validItemClasses: [string]? # Item template categories the recipe can target
 minimumItemLevel: int?
 requiredItemStates: # Typed predicates replacing object? (compliance)
 - field: ItemStateField # Service-specific enum: IsIdentified, IsCorrupted, IsMirrored, IsFractured
 expected: bool # Required state value
 requiredAffixSlotType: string? # For "add specific affix type" recipes

 # Affix operation (modification recipes only, requires lib-affix)
 affixOperation: AffixOperationType? # Service-specific enum (Category C):
 # ApplyRandom, RemoveRandom, RerollAll,
 # RerollValues, Corrupt, Fracture, ApplySpecific
 affixOperationConfig: # Typed config per operation (compliance)
 weightModifiers: Dictionary<string, decimal>? # For ApplyRandom: tag → weight multiplier
 slotType: string? # For ApplyRandom/ApplySpecific: target slot type
 guaranteedAffixCode: string? # For ApplySpecific: exact affix code to apply

 # Steps (ordered)
 steps:
 - code: string # "prepare", "heat", "hammer", "quench"
 name: string
 durationSeconds: int? # Real-time duration (null = instant)
 stationType: string? # Required station type (null = no station needed)
 toolCategory: string? # Required tool category (null = no tool needed)
 skillCheck: bool # Whether this step has a quality-affecting skill check
 eligibleRoles: [string]? # Which participant roles can advance this step (null = primary only)

 # Outputs (production recipes)
 outputs:
 - itemTemplateCode: string
 quantity: decimal
 qualityInfluence: decimal # How much recipe quality affects this output (0-1)

 # Extraction outputs (extraction recipes)
 extractionOutputs:
 - itemTemplateCode: string
 baseQuantity: decimal
 quantityVariance: decimal # +/- variance on quantity
 probability: decimal # Chance of this output (0-1)

 # Proficiency requirements
 proficiencyRequirements:
 - domain: string # Must match a proficiency domain
 minimumLevel: int

 # Quality formula weights (must sum to 1.0 — validated at recipe seed time)
 # Default weights are in CraftServiceConfiguration with x-constraint-group sum-equals enforcement
 qualityWeights:
 materialQuality: decimal # How much input quality affects output (0-1)
 proficiencyLevel: decimal # How much skill affects output (0-1)
 toolQuality: decimal # How much tool quality affects output (0-1)

 # Experience granted on completion
 experienceGrants:
 - domain: string
 baseExperience: decimal
 firstCraftBonus: decimal? # Extra XP for first successful craft of this recipe

 # Discovery
 isDiscoverable: bool # Can be discovered through experimentation
 discoveryHints: [string]? # Hints for discovery system (e.g., ["combine_fire_reagent_with_weapon"])
 prerequisiteRecipeCodes: [string]? # Must know these recipes before this one can be discovered

 # Deprecation (Category B — templates persist forever, no delete, no undeprecate)
 isDeprecated: bool
 deprecatedAt: DateTimeOffset?
 deprecationReason: string?
```

**Key design decisions**:

1. **Recipe types are opaque strings**, not enums. "production", "modification", "extraction" are conventions. A game might add "transmutation", "ritual", "assembly" as types with custom handling.

2. **Affix operations use a typed enum** (`AffixOperationType`): Modification recipes declare which lib-affix operation to invoke using a Category C service-specific enum, with typed configuration parameters (`AffixOperationConfig`). This replaces the previous `object?` approach that violated.

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
 | Milestones = recipe steps
 | onComplete (final step):
 | - /craft/internal/complete-session
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
 | Create output items via lib-item
 | Place in entity's inventory via lib-inventory
 | Quality affects: stat rolls, durability, rarity
 |
 +-- If modification:
 | Call lib-affix operation on target item
 | Quality affects: tier selection, value rolls
 |
 +-- If extraction:
 | Destroy target item via lib-item
 | Create extracted outputs (probabilistic)
 | Place in inventory
 |
 +-- Grant proficiency experience
 +-- Publish crafting events
```

**Why Contract integration matters**: Contracts provide durable state machines. If the server crashes mid-craft, the Contract persists the session state. Steps can have real-time durations (a forging step takes 30 seconds of in-game time). Prebound APIs on the final milestone handle the actual item creation/modification, ensuring atomicity.

### Multi-Entity Sessions (Collaborative Crafting)

Crafting sessions support multiple participants via Contract's multi-party model. This enables master-apprentice training, cooperative crafting, and NPC skill propagation through social relationships.

**Participant model**: `StartSession` accepts an optional `participants` list. Each entry has an `entityId`, `entityType`, and `role` (opaque string — "master", "apprentice", "assistant" are conventions). The session creator is always the primary participant. All participants must consent via Contract's multi-party consent flow before the session begins.

**Per-step role eligibility**: Recipe steps gain an `eligibleRoles` field (nullable string list). When null, only the primary participant can advance the step. When specified, any participant whose role is in the list can advance it. This enables recipes where the master handles skill-check steps while the apprentice handles preparation steps.

```
steps:
 - {code: "prepare_materials", eligibleRoles: ["master", "apprentice"], skillCheck: false}
 - {code: "heat_metal", eligibleRoles: ["master"], skillCheck: true, stationType: "forge"}
 - {code: "hammer_blade", eligibleRoles: ["master"], skillCheck: true, toolCategory: "hammer"}
 - {code: "quench", eligibleRoles: ["master", "apprentice"], skillCheck: false}
```

**Quality**: Multi-entity sessions gain a `collaborationBonus` quality factor. When multiple participants contribute skill-check steps, the collaboration bonus adds a configurable quality multiplier (`CollaborationBonusMultiplier` in configuration, default: 0.05 per additional participant).

**Experience distribution**: All participants receive experience on session completion, weighted by role. The primary participant (master) receives full `baseExperience`. Other participants receive `baseExperience * apprenticeBonusMultiplier` (`ApprenticeBonusMultiplier` in configuration, default: 0.5). This creates a natural incentive for NPCs to train apprentices — the master still gets full XP while the apprentice gains half.

**NPC social integration**: NPC crafting GOAP decisions include "assist master at forge" as an action when an NPC's `${craft.proficiency.<domain>}` is below the recipe's requirement but a master NPC is crafting nearby. The apprentice NPC joins the session via `StartSession` with the master's session ID. This enables emergent skill propagation through NPC social networks — blacksmith guilds naturally train new members.

**Single-entity sessions remain the default**: When no `participants` list is provided, sessions work exactly as before with zero overhead. The multi-entity path is purely additive.

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
 requiredItemStates:
 - {field: IsCorrupted, expected: false}
 - {field: IsMirrored, expected: false}
 affixOperation: RerollAll
 affixOperationConfig: {}
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
 requiredItemStates:
 - {field: IsCorrupted, expected: false}
 - {field: IsIdentified, expected: true}
 requiredAffixSlotType: "suffix"
 affixOperation: ApplyRandom
 affixOperationConfig:
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
 novice (0-99 growth) -> Can craft basic recipes
 apprentice (100-499) -> Unlocks intermediate recipes, quality bonus +10%
 journeyman (500-1499) -> Unlocks advanced recipes, quality bonus +25%
 expert (1500-3999) -> Unlocks expert recipes, quality bonus +40%
 master (4000+) -> Unlocks master recipes, quality bonus +50%, special techniques
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
 stationType: string # "forge", "enchanting_table", "alchemy_bench", "loom"
 locationId: Guid? # Physical location (null = portable/anywhere)
 ownerId: Guid? # Entity that owns/maintains this station (null = unowned)
 ownerType: EntityType? # Must be non-null when ownerId is set; null when unowned
 quality: decimal # Station quality (0-1), affects crafting output
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
${craft.can_craft.<recipe_code>} -- Can this NPC craft this recipe? (proficiency + materials + station)
${craft.proficiency.<domain>} -- Proficiency level in a domain
${craft.materials_for.<recipe_code>} -- Does the NPC have materials for this recipe?
${craft.station_nearby.<type>} -- Is a station of this type nearby?
${craft.best_recipe} -- Highest-value recipe the NPC can currently craft
${craft.queue_depth} -- How many items queued for crafting
${craft.last_craft_quality} -- Quality of last completed craft
```

NPC crafting decisions are driven by GOAP:
- **Goal**: "Earn money" -> Action: "Craft iron swords" (has materials, sword sells well)
- **Goal**: "Improve skill" -> Action: "Practice enchanting" (has cheap materials, gains XP)
- **Goal**: "Fill shop inventory" -> Action: "Craft what's low in stock"
- **Goal**: "Fulfill order" -> Action: "Craft specific commissioned item"

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-loot (L4, future) | May invoke production recipes for "crafted loot" drops (items that appear crafted rather than dropped) |
| lib-market (L4, future) | Reads crafting cost data for price estimation heuristics |
| NPC Actor runtime (L2, via Variable Provider) | `${craft.*}` variables for crafting GOAP decisions |

## Type Field Classification

| Field | Category | Type | Rationale |
|-------|----------|------|-----------|
| `recipeType` | B (Content Code) | Opaque string | Game-configurable recipe paradigms ("production", "modification", "extraction", or custom types like "transmutation", "ritual"); extensible without schema changes |
| `domain` | B (Content Code) | Opaque string | Proficiency domain codes ("blacksmithing", "enchanting", "alchemy"); game-defined at deployment time |
| `category` | B (Content Code) | Opaque string | Broad recipe classification ("weapons", "potions", "enchantments"); game-defined for filtering |
| `stationType` | B (Content Code) | Opaque string | Station type codes ("forge", "enchanting_table", "alchemy_bench", "loom"); game-configurable |
| `toolCategory` | B (Content Code) | Opaque string | Tool category codes ("hammer", "inscription_tools"); game-configurable |
| `affixOperation` | C (System State) | `AffixOperationType` enum | Finite set of lib-affix API operations (ApplyRandom, RemoveRandom, RerollAll, RerollValues, Corrupt, Fracture, ApplySpecific); not game-configurable |
| `itemStateField` (on requiredItemStates) | C (System State) | `ItemStateField` enum | Finite set of item state predicates (IsIdentified, IsCorrupted, IsMirrored, IsFractured); system-owned |
| `ownerType` (on StationDefinition) | A (Entity Reference) | `EntityType` enum (nullable) | Station owners are first-class Bannou entities (characters, guilds, etc.); null when unowned |
| `entityType` (on session/proficiency keys) | A (Entity Reference) | `EntityType` enum | Crafting sessions and proficiency are polymorphically owned by Bannou entities |

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
| `ExperienceScalingFactor` | `CRAFT_EXPERIENCE_SCALING_FACTOR` | `1.0` | Global experience multiplier |
| `ApprenticeBonusMultiplier` | `CRAFT_APPRENTICE_BONUS_MULTIPLIER` | `0.5` | XP multiplier for non-primary participants in multi-entity sessions |
| `CollaborationBonusMultiplier` | `CRAFT_COLLABORATION_BONUS_MULTIPLIER` | `0.05` | Quality bonus per additional participant in multi-entity sessions |

**Constraint group**: The three `DefaultQuality*Weight` properties use `x-constraint-group` with `sum-equals: 1.0` to enforce that the default quality weights collectively equal 1.0 at startup. If an operator overrides one weight without adjusting the others, the service fails fast with a descriptive error. Per-recipe `qualityWeights` are validated at the API level when recipes are seeded, not via configuration constraint groups.

---

## Visual Aid

Recipe definitions and crafting session management are owned here. Item storage is lib-item (L2). Container placement is lib-inventory (L2). Session state machines are lib-contract (L1). Currency costs are lib-currency (L2). Modifier definitions and application primitives are lib-affix (L4, soft). Loot generation that creates pre-crafted items at scale is lib-loot (L4, future). NPC crafting decisions via GOAP use lib-craft's Variable Provider Factory.

```
+-----------------------------------------------------------------------+
| Craft Service Architecture |
| |
| RECIPE LAYER (owned by lib-craft, MySQL + Redis cache) |
| +------------------------------------------------------------------+|
| | RecipeDefinition ||
| | +------------------------+ +---------------------------+ ||
| | | code: "forge_iron_sw" | | code: "inscribe_fire" | ||
| | | type: "production" | | type: "modification" | ||
| | | domain: "blacksmithing"| | domain: "enchanting" | ||
| | | inputs: iron x3, | | inputs: fire_reagent x2, | ||
| | | leather x1 | | inscription_ink x1 | ||
| | | steps: heat, hammer, | | affixOperation: | ||
| | | quench, grip | | "apply_random" | ||
| | | output: iron_sword x1 | | affixParams: | ||
| | | proficiency: 3 | | {fire: 5.0, cold: 0.0} | ||
| | +------------------------+ +---------------------------+ ||
| +------------------------------------------------------------------+|
| | |
| | /craft/session/start |
| v |
| SESSION LAYER (Contract-backed state machine) |
| +------------------------------------------------------------------+|
| | 1. Validate proficiency, materials, station, tool ||
| | 2. Create Contract with milestones = recipe steps ||
| | 3. Lock materials (reservation / authorization hold) ||
| | 4. Advance through steps (with quality checks) ||
| | 5. Final milestone -> prebound API -> complete session ||
| +------------------------------------------------------------------+|
| | |
| +--------+--------+ |
| | | |
| v v |
| PRODUCTION MODIFICATION |
| +----------------+ +------------------------------------------+ |
| | Consume inputs | | Call lib-affix operation on target item | |
| | Create outputs | | (apply_random, reroll_all, remove, etc.) | |
| | via lib-item | | Quality affects tier selection & rolls | |
| | Place in inv | +------------------------------------------+ |
| +----------------+ |
| | |
| | Variable Provider Factory |
| v |
| NPC GOAP (via Actor L2) |
| +------------------------------------------------------------------+|
| | ${craft.can_craft.forge_iron_sw} -- "I can make swords" ||
| | ${craft.proficiency.blacksmithing} -- "I'm level 7" ||
| | ${craft.materials_for.forge_iron_sw} -- "I have materials" ||
| | ${craft.best_recipe} -- "Swords are most valuable"||
| +------------------------------------------------------------------+|
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
 | Contract Milestones:
 | [1] heat_metal (15s duration, skill check)
 | [2] hammer_blade (skill check, requires hammer tool)
 | [3] quench
 | [4] attach_grip (requires hammer tool)
 | onComplete: /craft/internal/complete-session
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
 | materialQuality = 0.65 (average of input qualities)
 | proficiencyFactor = 0.70 (level 7 of max 10 for this recipe)
 | toolQuality = 0.80 (good hammer)
 | stepBonuses = 0.06 (from skill checks)
 |
 | finalQuality = (0.65 * 0.3) + (0.70 * 0.5) + (0.80 * 0.2) + 0.06
 | = 0.195 + 0.350 + 0.160 + 0.06
 | = 0.765
 |
 | Consume 3 iron ingots + 1 leather strip
 | Create iron_sword (quality: 0.765)
 | Place in entity's inventory
 | Grant blacksmithing XP: 50
 |
 +-- Publish craft.session.completed
 +-- Return: {outputItems: [iron_sword], quality: 0.765}
```

---

## Stubs & Unimplemented Features

**Everything is unimplemented.** This is a pre-implementation architectural specification. No schema, no generated code, no service implementation exists. The following phases are planned:

### Phase 1: Recipe Infrastructure
- Create craft-api.yaml schema with all endpoints
- Create craft-service-events.yaml schema
- Create craft-configuration.yaml schema (use `x-constraint-group` with `sum-equals: 1.0` for DefaultQuality*Weight properties)
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

5. **Environmental factors**: Location-specific quality bonuses (mountain forges produce better metal, coastal alchemy produces better water-based potions). Environmental bonuses must be owned by lib-craft or a dedicated L4 service (e.g., Environment) via service-owned bindings, not read from Location's `additionalProperties` metadata bag (per Foundation Tenets, No Metadata Bag Contracts).

6. **Crafting events as spectacle**: Publish real-time step progress as client events so nearby players can watch an NPC craft. "The blacksmith raises the hammer, strikes the blade, sparks fly" -- driven by step completion events.

7. **Recipe chains**: Recipes that require outputs of other recipes as inputs, forming dependency trees. "Forge steel sword" requires "smelt steel ingot" which requires "refine iron ore". lib-craft could validate the full chain and present it as a crafting plan.

8. **Tool wear**: Tools lose durability during crafting. Maps to lib-item's durability system. High-quality tools last longer and produce better results.

9. **Apprenticeship**: Entity A mentors entity B, granting bonus XP when both craft together. Tracked as a relationship (lib-relationship) that lib-craft checks during experience calculation.

10. **Client events**: `craft-client-events.yaml` for pushing real-time session progress to connected WebSocket clients (step completed, quality rolling, item created).

11. **Socket integration**: If lib-socket ([#430](https://github.com/beyond-immersion/bannou-service/issues/430)) is implemented, socket operations (recolor, relink, add/remove sockets, gem placement) would be orchestrated as modification recipes. Socket recoloring and relinking map to the modification recipe paradigm with `affixOperation`-style socket operation enums. lib-craft would call lib-socket's API at session completion, paralleling how it calls lib-affix for modifier operations.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

*No bugs — specification includes all required Category B elements (event, endpoint, clean-deprecated sweep). Both formerly listed items (missing event, missing endpoint) were resolved in the 2026-03-16 maintenance pass and are now captured in the implementation map.*

### Intentional Quirks (Documented Behavior)

1. **Recipe types are opaque strings, not enums**: "production", "modification", "extraction" are conventions. lib-craft validates that modification recipes have `affixOperation` specified and production recipes have `outputs` specified, but doesn't restrict which type strings are valid. Custom types require custom handling in the completion path.

2. **Proficiency domains are per-game-service, not global**: Two games can have proficiency domains with the same name that are entirely independent.

3. **Sessions are Contract-backed, not independently persisted**: The session store tracks crafting-specific metadata (quality accumulation, reserved materials) but the session's lifecycle state is owned by the Contract. If the Contract terminates unexpectedly, the session cleanup handler fires.

4. **Material reservation is advisory, not enforced by lib-item**: When a session starts, lib-craft records which items are reserved, but lib-item doesn't enforce reservation locks. If another operation destroys a reserved item, the session fails at the consumption step.

5. **Quality is deterministic given inputs**: The same entity with the same proficiency using the same materials at the same station with the same tool produces the same quality score. The randomness comes from skill check steps, which use a seeded PRNG for reproducibility in testing.

6. **Station exclusivity is optional**: By default, multiple entities can use the same station simultaneously. When `StationExclusivity` is enabled, stations are locked during sessions. This is a game design choice -- a busy forge might be realistic but frustrating.

7. **Modification recipes with lib-affix unavailable fail gracefully**: If lib-affix is not loaded and an entity attempts a modification recipe, the session start returns an error explaining that modification crafting is unavailable. Production and extraction recipes are unaffected.

8. **Discovery hints are opaque strings**: The discovery system matches entity-provided material combinations against recipe hint strings using a configurable matching algorithm. The default is exact-match on sorted material codes, but games can implement fuzzy matching via configuration.

9. **Proficiency seed type registration order**: lib-craft registers `proficiency:{domain}` seed types on startup for each domain it discovers in recipe definitions. This means seed type registration happens after recipe seeding — the seed types are derived from the recipe data, not predefined.

### Design Considerations (Requires Planning)

1. ~~**Cross-entity crafting**~~: **RESOLVED** (2026-03-16, #613) — Multi-entity sessions approved. Contract's multi-party model provides the mechanism. `StartSession` accepts an optional `participants` list (entityId + entityType + role). Recipe steps gain an `eligibleRoles` field controlling which participants can advance each step. Quality formula gains a `collaborationBonus` factor. XP distributed to all participants weighted by role (`apprenticeBonusMultiplier` in configuration). Single-entity sessions remain the default. See "Multi-Entity Sessions" in Core Concepts.

2. **Offline NPC crafting**: When an NPC's actor is running and they're crafting, step advancement happens through GOAP actions. But what about when the NPC's actor is suspended? Options: (a) sessions pause when actor suspends, (b) sessions auto-complete on a timer, (c) sessions are represented as Contract timer milestones that progress independently of the actor, (d) NPC crafting delegates to lib-workshop blueprints that reference lib-craft recipes — Workshop's lazy evaluation pattern handles "production continues when nobody is watching" natively via piecewise rate segments and background materialization, avoiding duplicate infrastructure. Option (d) would mean player crafting uses Contract-backed live sessions (interactive, skill checks) while NPC crafting uses Workshop (lazy, scalable to 100k+ artisans). Workshop already has a soft dependency on lib-craft for recipe-referenced blueprints.
<!-- AUDIT:NEEDS_DESIGN:2026-03-08:https://github.com/beyond-immersion/bannou-service/issues/614 -->

3. **Category B `instanceEntity` for RecipeDefinition**: RecipeDefinition's instance entity is CraftingSession — an ephemeral entity that completes/cancels/expires rather than persisting indefinitely. Per Implementation Tenets (ephemeral instance entities), the clean-deprecated sweep's `hasActiveInstancesAsync` delegate checks for active sessions referencing the recipe via a direct store query rather than a maintained reverse index. Sessions self-resolve within `SessionTimeoutSeconds`, so the count converges to zero naturally. CraftingSession must still be declared as an x-lifecycle entity in the events schema to satisfy B10a/B10b structural requirements.

---

## Work Tracking

*Pre-implementation phase. See [Economy System Guide](../guides/ECONOMY-SYSTEM.md) for the cross-cutting economy architecture.*

**Predecessor**: [#285](https://github.com/beyond-immersion/bannou-service/issues/285) (World Events and Crafting as Contract-Orchestrated Systems) — crafting portion superseded by this deep dive. World Events portion is a separate concern.

**Audit History**:
- **2026-03-16**: Resolved Design Consideration #1 (cross-entity crafting → [#613](https://github.com/beyond-immersion/bannou-service/issues/613)). Multi-entity sessions approved using Contract multi-party model. Added "Multi-Entity Sessions" subsection to Core Concepts, `eligibleRoles` to recipe steps, `ApprenticeBonusMultiplier` and `CollaborationBonusMultiplier` to configuration. Updated implementation map (StartSession, AdvanceSession, CompleteSession).
- **2026-03-16**: Maintenance pass 2 (`/maintain-plugin`). Implementation map migration: removed 5 operational sections (Dependencies, State Storage, Events, DI Services, API Endpoints) now covered by `docs/maps/CRAFT.md`. Deleted 2 FIXED strikethrough bugs (resolved in earlier same-day pass). Fixed broken Configuration table (constraint group paragraph was splitting the table). Kept Type Field Classification as Position B reference section. Kept Dependents and Configuration sections per template.
- **2026-03-16**: Audit pass (`/audit-plugin`). Marked Bugs #1-#2 as FIXED — documentation gaps (missing event, missing endpoint) were already corrected during the same-day maintain-plugin pass. Pre-implementation plugin: no further actionable gaps (all remaining items are planned implementation phases, potential extensions, or design considerations with issue tracking).
- **2026-03-16**: Maintenance pass (`/maintain-plugin`). Added 2 bugs: missing `craft.recipe.deleted` event (B11), missing `clean-deprecated` endpoint (B17-B19). Added Design Consideration #3: Category B `instanceEntity` for ephemeral CraftingSession — resolved by augmenting IMPLEMENTATION-BEHAVIOR.md T31 with ephemeral instance entity guidance. Deleted 5 resolved strikethrough Design Considerations (#1 quality-to-affix, #2 concurrent crafting, #3 real-time durations, #4 seed type registration, #6 material quality). Updated Extension #5 with T29 metadata bag clarification. Updated Recipe Management endpoint count (7→8).
- **2026-03-15**: Issue #607: Resolved quality-to-affix tier mapping — lib-craft owns the quality→affix-param translation (weightModifiers + valuePercentileTarget). lib-affix accepts generic parameters with no crafting knowledge. Quality curve configuration belongs in craft-configuration.yaml. Updated Design Consideration #1.
- **2026-03-08**: Pre-implementation audit (pass 4). Created issue for remaining design question: #7 (offline NPC crafting → [#614](https://github.com/beyond-immersion/bannou-service/issues/614)). All design considerations now have AUDIT markers.
- **2026-03-08**: Pre-implementation audit (pass 3). Created issues for remaining design questions: #5 (cross-entity crafting → [#613](https://github.com/beyond-immersion/bannou-service/issues/613)), #7 (offline NPC crafting → pending).
- **2026-03-08**: Pre-implementation audit (pass 2). Resolved Design Considerations #3 (real-time step durations → Contract timer milestones), #4 (proficiency seed type registration → implementation ordering note, moved to Intentional Quirks #9), #6 (material quality propagation → external dependency, lib-craft is consumer not producer). Two genuine design questions remain: #5 (cross-entity crafting) and #7 (offline NPC crafting).
- **2026-03-08**: Pre-implementation audit. Resolved Design Consideration #2 (concurrent crafting and item modification) — lib-affix's internal per-operation locking handles this; no lib-craft pre-check needed.
- **2026-03-06**: L4 audit pass. Fixed: Pattern B→C event topics (all 10), violations (`requiredStates: object?` → typed predicates, `affixOperationParams: object?` → typed config), `affixOperation` reclassified Category B→C (service-specific enum), Category B explicitly designated with deprecation guard on StartSession, `isActive` removed (use deprecation lifecycle), `ProficiencySource` local fallback removed (lib-seed guaranteed), `craft-proficiency-store` removed, `ownerType` typed to `EntityType?`, x-permissions added to all endpoint groups, location cleanup target added, `CraftStepCompletedEvent` → `CraftSessionStepCompletedEvent`. "Archive Shape" tenet added to FOUNDATION.md to prevent future agents from suggesting compression callbacks for game-mechanical operational state.
