# Logos Resonance Items: Memory-Forged Equipment with Experiential Prerequisites

> **Status**: Design
> **Created**: 2026-02-27
> **Author**: Lysander (design) + Claude (analysis)
> **Category**: Cross-cutting mechanic (behavioral + Affix extension)
> **Related Services**: Affix (L4), Loot (L4), Item (L2), Inventory (L2), Collection (L2), Seed (L2), Actor (L2), Puppetmaster (L4), Character-Encounter (L4), Character-Personality (L4), Character-History (L4), Quest (L2), Music (L4), Divine (L4)
> **Related Plans**: [MEMENTO-INVENTORIES.md](MEMENTO-INVENTORIES.md), [DEATH-AND-PLOT-ARMOR.md](DEATH-AND-PLOT-ARMOR.md), [COMPRESSION-GAMEPLAY-PATTERNS.md](COMPRESSION-GAMEPLAY-PATTERNS.md), [DUNGEON-EXTENSIONS-NOTES.md](DUNGEON-EXTENSIONS-NOTES.md)
> **Related Docs**: [ORCHESTRATION-PATTERNS.md](../reference/ORCHESTRATION-PATTERNS.md), [VISION.md](../reference/VISION.md), [PLAYER-VISION.md](../reference/PLAYER-VISION.md)
> **Related Deep Dives**: [AFFIX.md](../plugins/AFFIX.md), [LOOT.md](../plugins/LOOT.md), [ITEM.md](../plugins/ITEM.md), [SEED.md](../plugins/SEED.md)
> **Related Issues**: [#490](https://github.com/beyond-immersion/bannou-service/issues/490) (Affix system), [#308](https://github.com/beyond-immersion/bannou-service/issues/308) (additionalProperties cleanup)
> **Related arcadia-kb**: Underworld and Soul Currency System, Dungeon System, True Names

---

## Executive Summary

Traditional soulbinding is a binary lock: the item works for you, or it doesn't. Logos resonance items replace this with a **gradient of experiential affinity** -- the item is freely tradeable, always functional, but its full capabilities emerge only when the wielder's accumulated experiences resonate with the memories crystallized within it.

When a character fights deep into a dungeon, survives close calls, and defeats a boss under unique circumstances, the ambient logos and dispersing pneuma crystallize into an item that **is** those circumstances -- a weapon forged from lightning and mobility and desperation and the collective memories of everyone who tried before and failed. The specific affixes on this item have **activation prerequisites** tied to the experiences that formed it: lightning proficiency, mobility mastery, having survived near-death encounters, having explored the dungeon's depths. The character who earned it meets most or all of these prerequisites because the item literally formed from their actions. Another character could meet some, or eventually most, through their own experiences -- but meeting all of them simultaneously would be extraordinarily unlikely for anyone other than the originator.

This is not soulbinding. This is **logos recognition**. The item doesn't refuse to function for anyone. It simply resonates most completely with the wielder whose logos signatures match its own, because those signatures were forged in the same crucible.

This requires **zero new plugins**. It extends the Affix system's definition model with activation prerequisites (following the `IPrerequisiteProviderFactory` pattern already established by Quest), and the rest is ABML behavior authoring for god-actors and dungeon cores that evaluate fights and construct dynamic loot tables. The heaviest service-level change is a new `activationPrerequisites` field on `AffixDefinition` and a corresponding `IActivationPrerequisiteProviderFactory` interface.

---

## The Core Insight: Items as Crystallized Experience

### The Metaphysical Foundation

In Arcadia's logos/pneuma framework (documented in AFFIX.md's "Logos Inscription Model"):

- **Logos** are the information particles that define what things ARE
- **Pneuma** is organized logos with volatile properties -- the spiritual substance of reality
- When a powerful entity dies, its pneuma disperses but its logos patterns fragment and scatter
- When this dispersal happens in a location saturated with spiritual residue (combat mementos, death mementos, emotional mementos), the dying pneuma **crystallizes around nearby logos patterns**

The Loot deep dive already describes this as "resonance artifacts" -- items that didn't exist in the source's template but emerged from context. Logos resonance items are the highest expression of this principle: items whose logos patterns are woven not just from the dying entity's dispersal, but from the **entire tapestry of events** that led to that moment.

### Why This Is Different From Existing Loot

| Aspect | Standard Loot | Logos Resonance Item |
|--------|---------------|---------------------|
| **Source** | Loot table (static or dynamic) | Dynamic table constructed from combat context + memento accumulation |
| **Affixes** | Generated from weighted pools, gated by item level | Generated from fight circumstances, with activation prerequisites tied to wielder experience |
| **Uniqueness** | Statistically rare but reproducible | Genuinely unreproducible (specific circumstances can never recur exactly) |
| **Binding** | Soulbound types (none, on_pickup, on_equip, on_use) | No binding -- fully tradeable, gradient activation |
| **Value** | Rarity + stat rolls | Uniqueness + specific capabilities + prerequisite accessibility |
| **Content flywheel** | Item enters economy | Item carries encoded history; its prerequisites create new gameplay goals for acquirers |

### The Soulbinding Problem This Solves

Traditional soulbinding exists because designers want powerful items to be earned, not bought. But soulbinding creates dead-end items: once bound, they exit the economy permanently. This is anti-flywheel -- it removes content rather than generating it.

Logos resonance items solve the same problem (powerful items tied to earning) without the dead-end:

- The item IS earned -- its activation prerequisites are met by the earner
- The item IS tradeable -- someone else might want it for the prerequisites they CAN meet
- The item IS a content generator -- acquiring one creates new gameplay goals ("I need to fight lightning creatures to activate the third affix")
- The item IS a story -- its embedded memories and prerequisites encode real history

---

## Mechanism: How a Logos Resonance Item Is Born

### The Formative Event

A logos resonance item is created when a **formative event** occurs -- a moment of sufficient significance that ambient spiritual energy crystallizes into a new physical artifact. The evaluation and creation is performed by a god-actor (regional watcher or dungeon core actor) in their ABML behavior, not by any service's hardcoded logic.

Formative events include:

| Event Type | Evaluator | Significance Factors |
|-----------|-----------|---------------------|
| **Boss kill** | Dungeon core actor or regional watcher | Boss difficulty, combat duration, combat style diversity, near-death count, predecessor failures |
| **Climactic battle** | Regional watcher (Ares) | Participant count, stakes (territory, lives, political outcome), tactical creativity |
| **Masterwork creation** | Regional watcher or craft overseer | Material rarity, technique difficulty, location spiritual density, craftsman's lifetime mastery |
| **Divine manifestation** | The manifesting god | Divinity expenditure, purpose significance, follower devotion |
| **Dungeon awakening** | Dungeon core actor | Seed phase transition, accumulated memories, formative conditions |
| **Profound sacrifice** | Regional watcher (Moira/Thanatos) | What was given up, for whom, fulfillment state, witness impact |

Not every boss kill produces a logos resonance item. The god-actor evaluates significance and decides. Most kills produce standard loot (Tier 2/3 via normal loot tables). A logos resonance item is the exception -- reserved for moments that genuinely crystallize something worth remembering.

### The Evaluation Phase

When a god-actor determines that a formative event merits a logos resonance item, it performs a multi-service query to build the item's identity. This is an ABML behavior, not compiled code:

```yaml
# God-actor evaluating a boss kill for logos resonance item creation
evaluate_boss_kill_for_resonance:
  when:
    condition: |
      ${event.type} == 'boss_killed'
      AND ${event.significance_score} > 0.8
      AND ${event.near_death_count} > 0
    actions:
      # Gather the killer's combat identity
      - query:
          service: character
          endpoint: /character/get
          params:
            character_id: ${event.killer_character_id}
          result_var: killer

      # What weapon type does this character favor?
      - query:
          service: inventory
          endpoint: /inventory/container/list-items
          params:
            container_id: ${killer.equipment_container_id}
          result_var: equipped_items
      - compute:
          primary_weapon_category: |
            first(filter(${equipped_items},
              'item.template.category == "weapon"')).template.subcategory

      # What combat style defined this fight?
      - query:
          service: character-encounter
          endpoint: /character-encounter/encounter/list
          params:
            character_id: ${event.killer_character_id}
            location_id: ${event.location_id}
          result_var: dungeon_encounters

      # What predecessor memories exist in this dungeon?
      - query:
          service: inventory
          endpoint: /inventory/container/list-items
          params:
            container_id: ${event.location.memento_container_id}
            template_code: DEATH_MEMENTO
          result_var: predecessor_deaths

      # What personality drove this character's decisions?
      - query:
          service: character-personality
          endpoint: /character-personality/get
          params:
            character_id: ${event.killer_character_id}
          result_var: killer_personality

      # Proceed to item construction
      - goto: construct_resonance_item
```

### The Construction Phase

The god-actor constructs a **dynamic loot table** for this specific moment, then triggers generation:

```yaml
construct_resonance_item:
  actions:
    # Build the influence set from fight circumstances
    - compute:
        fight_influences:
          - "${event.primary_damage_type}_affinity"      # e.g., "lightning_affinity"
          - "${event.primary_tactic}_mastery"             # e.g., "high_mobility_mastery"
          - "dungeon_depth_${event.dungeon_depth}"        # e.g., "dungeon_depth_7"
          - "boss_slayer_${event.boss_template_code}"     # e.g., "boss_slayer_storm_wyrm"
        predecessor_influences:
          - for_each: ${predecessor_deaths}
            as: death
            compute: "predecessor_echo_${death.custom_stats.species_code}"

    # Determine item template based on character's weapon preference
    - compute:
        resonance_template_code: "crystallized_memory_${primary_weapon_category}"

    # Create dynamic loot table via lib-loot
    - service_call:
        service: loot
        endpoint: /loot/table/create
        params:
          code: "resonance_${event.event_id}"
          category: "resonance_artifact"
          gameServiceId: ${event.game_service_id}
          rollCount: { min: 1, max: 1 }
          rollMode: independent
          entries:
            - entryType: item
              itemTemplateCode: ${resonance_template_code}
              weight: 1000
              generationTier: 3
              affixContext:
                itemLevel: ${event.boss_level}
                rarity: "unique"
                influences: ${fight_influences} + ${predecessor_influences}
                weightModifiers:
                  "${event.primary_damage_type}": 5.0
                  "${event.primary_tactic}": 3.0
              itemOverrides:
                customStats:
                  origin_event_id: ${event.event_id}
                  origin_location_id: ${event.location_id}
                  origin_character_id: ${event.killer_character_id}
                  origin_character_name: ${killer.name}
                  predecessor_count: ${length(predecessor_deaths)}
                  fight_duration_seconds: ${event.duration_seconds}
                  near_death_count: ${event.near_death_count}
                originType: "resonance"
        result_var: resonance_table

    # Generate the item
    - service_call:
        service: loot
        endpoint: /loot/generate
        params:
          tableId: ${resonance_table.tableId}
          sourceId: ${event.boss_entity_id}
          sourceType: "resonance_crystallization"
          sourceLevel: ${event.boss_level}
          claimantId: ${event.killer_character_id}
          claimantType: "character"
          contextTags:
            - "resonance_artifact"
            - "boss_kill"
            - "${event.primary_damage_type}"
            - "${event.primary_tactic}"
          qualityModifier: ${1.0 + (event.near_death_count * 0.2)}
          targetContainerId: ${event.loot_container_id}
        result_var: generated_loot
```

---

## Affix Activation Prerequisites: The Key Extension

### Current State

The Affix deep dive documents `requiredInfluences` on affix definitions -- these gate which affixes CAN APPEAR on an item based on what influences the item carries. An item with `influences: ["shaper"]` can roll shaper-exclusive affixes. This is an **item-side** requirement evaluated at generation time.

### The Extension: Character-Side Activation

Logos resonance items need a second dimension: **activation prerequisites** evaluated against the **wielder** at runtime. An affix is always present on the item (visible, inspectable), but its stat grants only contribute when the wielder meets its activation prerequisites.

```
AffixDefinition (extended):
  # Existing fields...
  requiredInfluences: [string]          # Item-side: must be on item for affix to generate

  # NEW: Character-side activation
  activationPrerequisites: [ActivationPrerequisite]?  # Null = always active (standard affixes)
```

```
ActivationPrerequisite:
  prerequisiteType: string           # Provider namespace (e.g., "collection", "encounter", "personality")
  prerequisiteCode: string           # Specific check (e.g., "lightning_mastery", "fought_storm_wyrm")
  parameters: object?                # Additional context (e.g., { "threshold": 0.7 })
  displayHint: string?               # Player-readable hint (e.g., "Requires affinity with lightning")
  fidelityContribution: decimal      # How much this prerequisite contributes to affix fidelity (0.0-1.0)
```

### The IActivationPrerequisiteProviderFactory Pattern

This follows the exact pattern established by Quest's `IPrerequisiteProviderFactory` (documented in SERVICE-HIERARCHY.md):

```csharp
// bannou-service/Providers/IActivationPrerequisiteProviderFactory.cs
namespace BeyondImmersion.BannouService.Providers;

/// <summary>
/// Factory for checking whether a character meets an affix activation prerequisite.
/// Higher-layer services implement this to provide their domain data to Affix (L4).
/// Follows the same DI inversion pattern as IPrerequisiteProviderFactory (Quest)
/// and IVariableProviderFactory (Actor).
/// </summary>
public interface IActivationPrerequisiteProviderFactory
{
    /// <summary>The prerequisite namespace (e.g., "collection", "encounter", "personality")</summary>
    string ProviderName { get; }

    /// <summary>Check if a character meets the specified prerequisite</summary>
    Task<ActivationPrerequisiteResult> CheckAsync(
        Guid characterId,
        string prerequisiteCode,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken ct);
}

public record ActivationPrerequisiteResult(
    bool Satisfied,
    decimal Fidelity,       // 0.0-1.0: how strongly the prerequisite is met
    string? ProgressHint    // Optional hint: "3 of 5 lightning creatures defeated"
);
```

### Provider Implementations

| Provider | Namespace | Service | Example Prerequisites |
|----------|-----------|---------|----------------------|
| `CollectionActivationProvider` | `collection` | Collection (L2) | "Has unlocked 'Lightning Mastery' entry", "Has 'Storm Wyrm Slayer' trophy" |
| `EncounterActivationProvider` | `encounter` | Character-Encounter (L4) | "Has fought entity type 'storm_wyrm'", "Has survived 3+ near-death encounters" |
| `PersonalityActivationProvider` | `personality` | Character-Personality (L4) | "Has aggression > 0.7", "Has courage > 0.5" |
| `SeedActivationProvider` | `seed` | Seed (L2) | "Guardian spirit combat.lightning fidelity > 0.5" |
| `QuestActivationProvider` | `quest` | Quest (L2) | "Has completed quest 'Depths of the Storm Spire'" |
| `HistoryActivationProvider` | `history` | Character-History (L4) | "Has participated in event type 'dungeon_clear'" |

Affix discovers these providers via `IEnumerable<IActivationPrerequisiteProviderFactory>` injection -- the same DI collection pattern used everywhere in Bannou. Missing providers degrade gracefully: if Character-Encounter isn't loaded, encounter prerequisites evaluate to `Satisfied = false, Fidelity = 0.0`.

### Fidelity Model: Partial Activation

When a wielder meets SOME but not all activation prerequisites, the affix is **partially active**. This follows the fidelity model established by Seed capabilities:

```
Affix stat contribution = base_stat_value * activation_fidelity

activation_fidelity = sum(met_prerequisite.fidelityContribution * met_prerequisite.fidelity)
                      / sum(all_prerequisite.fidelityContribution)
```

Example: A "Stormcaller's Fury" affix grants +50 lightning damage and has three activation prerequisites:

| Prerequisite | Fidelity Contribution | Character A (earner) | Character B (buyer) |
|---|---|---|---|
| `collection:lightning_mastery` | 0.4 | Met (fidelity 1.0) | Met (fidelity 0.6) |
| `encounter:fought_storm_wyrm` | 0.3 | Met (fidelity 1.0) | Not met (fidelity 0.0) |
| `personality:courage > 0.6` | 0.3 | Met (fidelity 0.9) | Met (fidelity 0.8) |

- Character A: `(0.4 * 1.0 + 0.3 * 1.0 + 0.3 * 0.9) / 1.0 = 0.97` --> +48.5 lightning damage
- Character B: `(0.4 * 0.6 + 0.3 * 0.0 + 0.3 * 0.8) / 1.0 = 0.48` --> +24.0 lightning damage

The item works for both. It works dramatically better for the character who earned it.

### Stat Computation Extension

The Affix deep dive already documents `ComputeItemStats` and `ComputeEquipmentStats` endpoints. The activation fidelity check adds one query step:

```
ComputeItemStats (extended):
  1. Load affix instance for item (existing)
  2. For each affix slot:
     a. If activationPrerequisites is null → full stat contribution (existing behavior)
     b. If activationPrerequisites is non-null → query providers for wielder ID
        → compute activation_fidelity → scale stat contribution
  3. Sum base stats + scaled affix stats + quality modifier (existing)
```

The wielder ID is provided as a parameter to the stat computation endpoint. This enables "preview" computations: "What would this item's stats be if Character X wielded it?"

---

## The Predecessor Memory Layer

### Accumulated Failure Enriches Success

When multiple characters attempt and fail a challenge, their failures leave memento items at the location (per MEMENTO-INVENTORIES.md). These mementos are consumed by the formative event evaluation -- not literally consumed (they remain in the memento inventory), but **read** by the god-actor to enrich the resulting item.

```
Attempt 1: Character A (warrior) dies at room 5 to fire traps
  → DEATH_MEMENTO at room 5:
    species: human, cause: fire_trap, personality_aggression: 0.8,
    aspiration: "prove martial supremacy", fulfillment: 0.3

Attempt 2: Character B (mage) dies at boss room to melee overwhelm
  → DEATH_MEMENTO at boss room:
    species: elf, cause: boss_melee, personality_curiosity: 0.9,
    aspiration: "master the arcane", fulfillment: 0.5

Attempt 3: Character C defeats the boss using lightning + mobility
  → God-actor reads ALL mementos in the dungeon's rooms
  → The resonance item gains ADDITIONAL affixes from predecessor memories:

    "Echo of the Fallen Warrior" (from Character A's memento):
      statGrants: [{ fire_resistance: +30-40 }]
      activationPrerequisites:
        - encounter: "survived_fire_trap" (Character C did survive them)
        - personality: "aggression > 0.5" (resonates with A's warrior spirit)
      displayHint: "A warrior's defiance against flame lingers in this blade"

    "Mage's Final Lesson" (from Character B's memento):
      statGrants: [{ melee_defense: +20-25 }]
      activationPrerequisites:
        - personality: "curiosity > 0.4" (resonates with B's scholarly nature)
      displayHint: "The last insight of a scholar who underestimated close combat"
```

The god-actor constructs these predecessor affixes from memento custom stats. The affix definitions are created dynamically (registered via `/affix/definition/create` with codes like `resonance_{eventId}_echo_{mementoIndex}`) and applied during Tier 3 generation.

### The Consequence for Dungeons

Dungeon cores already have memory inventories (DUNGEON.md). When a dungeon core actor evaluates a boss kill for resonance item creation, it has access to:

1. **Its own memory inventory**: Every notable event the dungeon has experienced
2. **Room memento inventories**: Deaths and combat at each room
3. **The boss's logos template**: What the boss WAS (species, capabilities, personality)
4. **The mana released**: How much pneuma energy was available for crystallization

A dungeon that has been challenged many times produces richer resonance items than a fresh one. This is the content flywheel applied to individual items -- **the world literally rewards persistence with richer prizes**.

### Memento Consumption vs. Reference

An important distinction: the formative event evaluation **reads** mementos but does not **consume** them. The mementos remain in the location's memento inventory for future interactions (necromancers, mediums, historians, etc.). The resonance item's custom stats contain references to the source mementos (`origin_memento_ids`) for provenance tracking, but the mementos themselves persist.

The exception: the formative event's OWN memento (the boss death, the climactic battle) may optionally be consumed during crystallization, representing the spiritual energy being drawn into the item rather than dissipating into the environment. This is a god-actor behavioral decision, not a rule.

---

## Trade Dynamics: Why This Creates a Living Economy

### Value Asymmetry

A logos resonance item has different value to different characters:

| Character Profile | Prerequisites Met | Effective Power | Trade Value Assessment |
|---|---|---|---|
| **Earner** (fought the dungeon) | 9/10 | ~95% | "This is my item. I earned it. It's worth keeping." |
| **Similar build** (lightning + mobility) | 5/10 | ~50% | "I meet half. If I clear that dungeon myself, I'd meet 7/10. Worth the investment." |
| **Collector** | 2/10 | ~20% | "The history alone is worth something. Unique items attract attention." |
| **Incompatible build** | 1/10 | ~10% | "Beautiful item, wrong build. Not worth my gold." |

This creates **genuine price discovery** rather than flat market pricing. Two identical-looking items with different prerequisite sets have different values to different buyers. NPC merchants (via lib-market) evaluate resonance items differently based on their GOAP assessment of local demand -- a merchant in a lightning-mage district values a lightning resonance item higher than one in a warrior quarter.

### The Acquisition Goal Loop

When a character acquires a resonance item through trade and sees unmet prerequisites, those prerequisites become **gameplay goals**:

- "I need to fight a storm wyrm to activate the third affix" → seeks out storm wyrm encounters
- "I need courage > 0.6" → engages in dangerous situations (personality evolution from Character-Personality)
- "I need the 'Lightning Mastery' collection entry" → pursues lightning-related content

The item creates its own demand for content. This is a micro-flywheel: **acquiring an item generates engagement goals, which generate gameplay, which generates more mementos and archives, which feed the macro content flywheel**.

### NPC Trade Intelligence

NPC merchants and traders evaluate resonance items through their GOAP planners using affix variable data:

```yaml
# NPC merchant evaluating a resonance item for purchase/pricing
evaluate_resonance_item:
  when:
    condition: |
      ${offered_item.origin_type} == 'resonance'
      AND ${self.role} == 'merchant'
    actions:
      - query:
          service: affix
          endpoint: /affix/item/get
          params:
            item_instance_id: ${offered_item.instance_id}
          result_var: affix_data

      # Count prerequisites that local characters might meet
      - compute:
          local_demand_score: |
            ${affix_data.activation_prerequisites}
            | filter(p => p.type IN ['collection', 'quest'])
            | count(p => is_common_in_region(p.code, ${self.location}))
          rarity_premium: |
            1.0 + (${affix_data.predecessor_count} * 0.1)
          base_value: ${affix_data.item_value_estimate}
          final_price: ${base_value * rarity_premium * (0.5 + local_demand_score * 0.5)}
```

---

## Service Integration Map

### Which Services Handle What

```
                        God-Actor ABML Behavior
                        (evaluation + construction)
                                |
                    +-----------+-----------+
                    |                       |
                    v                       v
            Loot (L4)                 Affix (L4)
            Dynamic table             Affix definitions
            creation +                with activation
            Tier 3 generation         prerequisites
                    |                       |
                    v                       v
              Item (L2)              Activation prerequisite
              Template/instance      providers (DI pattern)
              storage                       |
                    |               +-------+-------+
                    v               |       |       |
            Inventory (L2)    Collection  Encounter  Personality
            Container          (L2)      (L4)       (L4)
            placement                |       |       |
                                   Seed    Quest   History
                                   (L2)    (L2)    (L4)
```

### Service-Level Changes Required

| Service | Change Type | Description |
|---------|------------|-------------|
| **Affix (L4)** | Schema extension | Add `activationPrerequisites` field to `AffixDefinition` model |
| **Affix (L4)** | New interface | `IActivationPrerequisiteProviderFactory` in `bannou-service/Providers/` |
| **Affix (L4)** | Stat computation | Extend `ComputeItemStats`/`ComputeEquipmentStats` to accept wielder ID and evaluate activation fidelity |
| **Affix (L4)** | New endpoint | `/affix/item/compute-activation` -- preview activation fidelity for a character against an item's affixes |
| **Collection (L2)** | New provider | `CollectionActivationProvider` implements `IActivationPrerequisiteProviderFactory` |
| **Character-Encounter (L4)** | New provider | `EncounterActivationProvider` implements `IActivationPrerequisiteProviderFactory` |
| **Character-Personality (L4)** | New provider | `PersonalityActivationProvider` implements `IActivationPrerequisiteProviderFactory` |
| **Seed (L2)** | New provider | `SeedActivationProvider` implements `IActivationPrerequisiteProviderFactory` |
| **Quest (L2)** | New provider | `QuestActivationProvider` implements `IActivationPrerequisiteProviderFactory` |
| **Character-History (L4)** | New provider | `HistoryActivationProvider` implements `IActivationPrerequisiteProviderFactory` |
| **Loot (L4)** | No code change | Dynamic table creation already supported; god-actors author the behavior |
| **Item (L2)** | No code change | `originType: "resonance"` is an opaque string; `customStats` carries embedded memento references |

### What Is NOT Changed

- **No new plugins**: Everything composes from existing services
- **No schema changes to Item**: `originType` is already an opaque string; `customStats` already supports arbitrary data
- **No schema changes to Loot**: Dynamic tables already supported; `affixContext.influences` already accepts arbitrary strings
- **No changes to code generation scripts**: All extensions are manual service code and schema additions within existing plugins
- **No hierarchy violations**: All DI providers follow the established inversion pattern; lower-layer services define interfaces, higher-layer services implement them

---

## Hierarchy Compliance

### The Affix Prerequisite Direction

Affix is L4. The activation prerequisite providers span multiple layers:

| Provider | Layer | Affix's Dependency Direction | Compliance |
|----------|-------|------------------------------|------------|
| Collection | L2 | L4 depends on L2 | Allowed (downward) |
| Quest | L2 | L4 depends on L2 | Allowed (downward) |
| Seed | L2 | L4 depends on L2 | Allowed (downward) |
| Character-Encounter | L4 | L4 depends on L4 (soft) | Allowed (graceful degradation) |
| Character-Personality | L4 | L4 depends on L4 (soft) | Allowed (graceful degradation) |
| Character-History | L4 | L4 depends on L4 (soft) | Allowed (graceful degradation) |

The `IActivationPrerequisiteProviderFactory` interface lives in `bannou-service/Providers/` (shared code, no layer). Affix depends on the interface (allowed -- shared code). L2/L4 services implement the interface (allowed -- they depend on shared code). Affix discovers providers via `IEnumerable<IActivationPrerequisiteProviderFactory>` DI injection. Missing providers degrade gracefully (prerequisite evaluates as unmet, fidelity = 0).

This is the identical pattern to `IVariableProviderFactory` (Actor L2 discovering L4 providers) and `IPrerequisiteProviderFactory` (Quest L2 discovering L4 providers). No new architectural patterns introduced.

---

## Connection to the Content Flywheel

### The Item-Level Flywheel

Logos resonance items create a tertiary flywheel loop running alongside the primary (narrative) and secondary (spiritual) flywheels:

```
Primary Flywheel (Narrative):
  Character Dies → Archive → Storyline → Quest → Player Experiences → Loop

Secondary Flywheel (Spiritual, from MEMENTO-INVENTORIES):
  Character Dies → Memento at Location → Spiritual Ecology → Encounters → Loop

Tertiary Flywheel (Equipment):
  Formative Event → Resonance Item Created
      → Item enters economy with experiential prerequisites
      → Acquirer pursues prerequisites (new gameplay goals)
      → Gameplay generates encounters, mementos, archives
      → Archives feed narrative flywheel
      → Encounters feed spiritual flywheel
      → Both feed FUTURE resonance item creation
      → Richer items with more predecessor memories
      → Loop accelerates
```

### Year 1 vs Year 5

| Metric | Year 1 | Year 5 |
|--------|--------|--------|
| Average predecessor memories per resonance item | 0-2 | 5-20 |
| Average activation prerequisites per item | 3-5 | 8-15 |
| Unique affix definitions in circulation | ~100 | ~50,000+ |
| Trade market depth for resonance items | Thin (few exist) | Deep (items have complex, varied prerequisite profiles) |
| NPC ability to evaluate resonance items | Basic (few providers active) | Rich (full L4 stack provides detailed evaluation) |

A Year 5 resonance sword from a dungeon that's been challenged by hundreds of characters carries the spiritual weight of all those attempts. Its prerequisites reference encounters, personalities, and experiences that span years of simulation. It is literally more valuable because the world is older. **The items improve as the world ages, just like everything else in the content flywheel.**

---

## Detailed Affix Examples

### "Stormcaller's Edge" -- Lightning Boss Kill Resonance Weapon

A character who uses lightning magic and high mobility to defeat a Storm Wyrm boss at dungeon depth 7, after two predecessors died trying:

**Implicit Affixes** (from boss logos -- always active):
- Storm Wyrm Essence: +15-20 lightning damage (the boss's inherent nature)
- Deep Earth Resonance: +8% mana regeneration (the dungeon depth's ambient logos)

**Explicit Affixes** (from fight circumstances -- activation prerequisites):

| Affix | Stats | Prerequisites | Earner Meets? |
|-------|-------|--------------|---------------|
| Thundercaller's Precision | +25-30 lightning damage, +5% crit | `collection:lightning_mastery` (0.4), `personality:focus > 0.5` (0.3), `encounter:killed_storm_wyrm` (0.3) | Yes (all three -- used lightning, focused fighting, killed the boss) |
| Windrunner's Grace | +15% movement speed, +10 evasion | `seed:combat.mobility > 3.0` (0.5), `encounter:survived_near_death_3+` (0.5) | Yes (high mobility fighting, survived 3 near-death moments) |
| Fallen Warrior's Defiance | +30 fire resistance | `encounter:survived_fire_trap` (0.5), `personality:aggression > 0.5` (0.5) | Partial (survived the traps, but aggression is 0.4 → 80% fidelity on that prerequisite) |
| Scholar's Insight | +20 melee defense, +10 spell power | `personality:curiosity > 0.4` (0.6), `quest:explored_storm_spire_library` (0.4) | Partial (curiosity is high, but never explored the library → 60% fidelity) |

**Total for earner**: ~93% effective power (meets almost everything)
**Total for a lightning mage buyer**: ~55% (meets lightning prereqs, misses dungeon-specific ones)
**Total for a warrior buyer**: ~25% (meets aggression/courage, misses lightning/mobility)

### "Heartwood Aegis" -- Masterwork Crafting Resonance Shield

A master carpenter who crafts a shield from a 200-year-old tree at a location where a beloved NPC woodworker died protecting apprentices:

**Implicit Affixes**:
- Ancient Heartwood: +40 durability, +5% all elemental resistance (material logos)
- Sacred Grove Blessing: +10 vitality regeneration (location's consecrated status)

**Explicit Affixes**:

| Affix | Stats | Prerequisites |
|-------|-------|--------------|
| Master's Patience | +15% block chance | `seed:craft.woodworking > 5.0` (0.5), `personality:patience > 0.6` (0.5) |
| Protector's Resolve | +25 armor when allies nearby | `encounter:protected_ally_from_death` (0.4), `personality:mercy > 0.5` (0.3), `collection:mentor_legacy` (0.3) |
| Memory of Sacrifice | +50% healing received when below 30% health | `history:witnessed_heroic_sacrifice` (0.6), `personality:selflessness > 0.4` (0.4) |

This shield resonates most with someone who is a patient woodworker, has protected others, and has witnessed sacrifice -- because it was forged from those exact experiences.

---

## Client-Side Presentation

### Item Inspection UI

When a player inspects a logos resonance item, the client renders:

```
╔══════════════════════════════════════════════════╗
║  STORMCALLER'S EDGE                              ║
║  Unique Crystal Longsword                        ║
║  "Forged in the death-throes of a Storm Wyrm,    ║
║   this blade remembers the fight that made it."  ║
╠══════════════════════════════════════════════════╣
║  Storm Wyrm Essence          +18 Lightning Dmg   ║
║  Deep Earth Resonance        +8% Mana Regen      ║
╠══════════════════════════════════════════════════╣
║  ◆ Thundercaller's Precision  [███████████░] 97% ║
║    +29 Lightning Dmg, +5% Crit                   ║
║    ○ Lightning Mastery          ✓ Achieved        ║
║    ○ Focused Mind               ✓ Achieved        ║
║    ○ Storm Wyrm Slayer          ✓ Achieved        ║
║                                                   ║
║  ◆ Windrunner's Grace         [██████████░░] 85% ║
║    +13% Move Speed, +9 Evasion                   ║
║    ○ Mobility Mastery           ✓ Achieved        ║
║    ○ Survived 3+ Near-Deaths    ◐ Partial (2/3)  ║
║                                                   ║
║  ◇ Fallen Warrior's Defiance  [██████░░░░░░] 48% ║
║    +14 Fire Resistance                           ║
║    ○ Survived Fire Traps        ✓ Achieved        ║
║    ○ Warrior's Spirit           ✗ Not Met         ║
║    "A warrior's defiance against flame lingers    ║
║     in this blade"                                ║
║                                                   ║
║  ◇ Scholar's Insight           [████░░░░░░░] 35% ║
║    +7 Melee Defense, +4 Spell Power              ║
║    ○ Curious Mind               ✓ Achieved        ║
║    ○ Storm Spire Library        ✗ Not Met         ║
║    "The last insight of a scholar who             ║
║     underestimated close combat"                  ║
╠══════════════════════════════════════════════════╣
║  MEMORIES WITHIN:                                 ║
║  ◈ The Storm Wyrm's Fall (your victory)          ║
║  ◈ Echo of Aldric (warrior, died room 5)         ║
║  ◈ Echo of Thessaly (mage, died at the wyrm)    ║
║                                                   ║
║  Origin: Storm Spire, Depth 7                    ║
║  Crystallized: Day 147, Year 3                   ║
║  Predecessors who fell: 2                        ║
╚══════════════════════════════════════════════════╝
```

### Progressive Revelation

The amount of detail visible to the player depends on their guardian spirit's **agency progression** (Agency service L4):

| Agency Level | What's Visible |
|---|---|
| Minimal | Item name, implicit stats, "this item has hidden potential" |
| Low | Affix names and total stat values, activation bars without details |
| Medium | Prerequisites listed with met/unmet status, progress hints |
| High | Full fidelity percentages, specific prerequisite requirements, memory stories |
| Mastery | Exact numerical thresholds, optimal build paths to maximize activation |

This follows the UX-as-progression principle from PLAYER-VISION.md: the spirit doesn't understand what it's looking at until it has enough context.

---

## Implementation Sequence

This design can be implemented incrementally:

### Phase 1: Activation Prerequisites on Affix

1. Add `activationPrerequisites` to Affix schema (nullable -- existing affixes unaffected)
2. Define `IActivationPrerequisiteProviderFactory` in `bannou-service/Providers/`
3. Extend `ComputeItemStats` to accept optional `wielderId` parameter
4. When `wielderId` is provided and affix has `activationPrerequisites`, evaluate fidelity
5. Implement Collection and Seed prerequisite providers (L2 -- always available)

**Result**: Any affix can have activation prerequisites. Standard affixes remain unchanged.

### Phase 2: L4 Prerequisite Providers

6. Implement Character-Encounter prerequisite provider
7. Implement Character-Personality prerequisite provider
8. Implement Character-History prerequisite provider
9. Implement Quest prerequisite provider

**Result**: Rich prerequisite vocabulary available for affix definitions.

### Phase 3: Resonance Item Templates

10. Create "crystallized memory" item templates per weapon/armor category
11. Create affix definitions for common resonance patterns (lightning, fire, mobility, defense, etc.)
12. Define seeded affix definitions with activation prerequisites for standard combat styles

**Result**: The building blocks exist for resonance item creation.

### Phase 4: God-Actor Behaviors

13. Author ABML behaviors for dungeon core actors evaluating boss kills
14. Author ABML behaviors for regional watchers evaluating climactic events
15. Integrate with memento inventory queries for predecessor memory reading

**Result**: God-actors autonomously create logos resonance items from formative events.

### Phase 5: Economy Integration

16. Extend Market's valuation to consider activation prerequisite accessibility
17. NPC GOAP integration for evaluating resonance item trade value
18. Client UI for inspecting activation prerequisites and fidelity

**Result**: Resonance items participate fully in the living economy.

---

## Open Questions

1. **Affix definition lifecycle for dynamic resonance affixes**: When a god-actor creates a unique affix definition for a resonance item, should that definition be permanently stored (growing the definition space indefinitely) or should it be tagged as "ephemeral" (only the instance data matters after generation)? The definition is needed for display names and stat descriptions, but 50,000+ unique definitions after 5 years is a storage consideration.

2. **Activation fidelity caching**: Should computed activation fidelity be cached per character-per-item (like affix stat caching), or computed on every equipment stat query? Characters' prerequisites change slowly (personality evolution, new encounters), so aggressive caching seems safe. The Affix deep dive already defines `ComputedStatsCacheTtlSeconds` (default 60s) which could extend to include activation fidelity.

3. **Predecessor memory depth**: How many predecessor mementos should a god-actor read when constructing resonance affixes? Reading every memento in a deeply-explored dungeon could produce items with 20+ affixes. The `MaxAffixesPerItem` cap (default 12) provides a natural limit, but the god-actor's ABML behavior should select the most significant predecessors rather than all of them.

4. **Cross-realm resonance**: If a character in Realm A acquires a resonance item from Realm B, do the activation prerequisites reference Realm B experiences? The Seed system's cross-pollination model suggests yes -- experience crosses boundaries. But encounter and history records are realm-scoped. The answer may be that activation prerequisites reference character-level data (personality, seed, collection) more heavily than realm-scoped data (specific encounters, specific locations).

5. **Resonance item evolution**: Should a resonance item's activation prerequisites ever change after creation? The current design says no -- the item is a snapshot of the moment it was created. But an interesting extension would be items that "learn" from their wielder, gradually shifting prerequisites to match their current owner's experiences. This veers into living weapon territory (ACTOR-BOUND-ENTITIES.md) and might be better served by the sentient weapon pattern (weapon gets its own Seed and Actor at higher growth phases).

---

*This document describes a design that composes from existing Bannou primitives. The only service-level extension is activation prerequisites on Affix definitions, following established DI inversion patterns. Everything else is ABML behavior authoring and dynamic loot table construction -- content, not code.*
