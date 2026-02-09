# Dungeon as Actor: Living Dungeons in the Actor System

> **Status**: Proposal
> **Priority**: Medium-High
> **Related**: [SEED-AND-GARDENER.md](SEED-AND-GARDENER.md), [PLAYER-VISION.md](../reference/PLAYER-VISION.md), [VISION.md](../reference/VISION.md), [SERVICE-HIERARCHY.md](../reference/SERVICE-HIERARCHY.md)
> **External**: `arcadia-kb/04 - Game Systems/Dungeon System.md`, `arcadia-kb/04 - Game Systems/Dungeon System - Nageki-Inspired Enhancements.md`

## Executive Summary

This document proposes implementing **dungeons as a new actor category** within Bannou's actor system, backed by the **Seed service** for progressive growth and capability tracking. Rather than being passive containers for content, dungeons become autonomous entities that:

- **Perceive** intrusions, combat, deaths, and resource extraction within their domain
- **Cognize** threats, opportunities, and memorable events through a simplified cognition pipeline
- **Act** by spawning monsters, activating traps, managing resources, and coordinating defenses -- with actions gated by the dungeon's **seed capability manifest**
- **Grow** progressively through accumulated experience, tracked by a `dungeon_core` seed with growth domains (`mana_reserves`, `genetic_library.*`, `trap_complexity.*`, `domain_expansion.*`)
- **Bond** with a character or monster via the **Contract service**, with the bonded entity receiving a `dungeon_master` seed to track their growth in that role

The dungeon system introduces **two new seed types**: `dungeon_core` (the dungeon's own progressive growth) and `dungeon_master` (the bonded entity's growth in the mastery role). Together with the existing `guardian` seed type (player spirit growth), these demonstrate that seeds track **growth in roles**, not growth in entities -- the same entity can hold multiple seeds for different roles it plays.

The bonding pattern mirrors the player-character relationship: just as a player (account with `guardian` seed) bonds asymmetrically with an autonomous character -- able to disengage, deriving mutual benefit while engaged -- a character bonds asymmetrically with an autonomous dungeon. Contracts formalize the terms at each layer.

---

## Part 1: Why Dungeons as Actors (Backed by Seeds)

### Current Architecture Gap

The dungeon system described in arcadia-kb defines dungeons as living, conscious entities with:
- A **dungeon core** (newborn spirit that serves as the seed of consciousness)
- A **dungeon master** (creature that bonds with the core, gains power, defends it)
- **Monsters** as mana generators (pneuma echoes, not true creatures)
- **Domain expansion** through extension cores
- **Memory accumulation** from significant events

The Seed service (L2 Game Foundation) provides the perfect progressive growth substrate for dungeon cores, while the Actor service (L2) provides autonomous behavior execution. Together they model the complete dungeon lifecycle:

| Dungeon Concept | Service Mapping |
|-----------------|----------------|
| Dungeon core consciousness | **Actor** (Event Brain) -- autonomous cognition pipeline |
| Dungeon core growth | **Seed** -- `dungeon_core` seed type with growth domains |
| Dungeon master's growth in role | **Seed** -- `dungeon_master` seed type (created on bonding) |
| Dungeon perception (sensing intrusions) | **Actor** -- event subscriptions + perception queue |
| Dungeon decision-making | **Actor** -- ABML behavior document + cognition pipeline |
| Dungeon actions (spawn, trap, etc.) | **Actor** -- capabilities gated by **Seed** capability manifest |
| Dungeon-master bond | **Contract** (game-level terms) -- triggers `dungeon_master` seed creation |
| Progressive capability unlocks | **Seed** -- capability rules map growth domains to dungeon actions |
| Memory accumulation | **Actor** state + Memory store (memories contribute **Seed** growth) |

### The Asymmetric Bond Pattern

The dungeon-master relationship follows the same structural pattern as the player-character relationship:

```
PLAYER-CHARACTER BOND                    CHARACTER-DUNGEON BOND
─────────────────────                    ──────────────────────

Account (with guardian seed)             Character (with dungeon_master seed)
    │                                        │
    │ possesses (asymmetric)                 │ bonds with (asymmetric)
    │ can "go away"                          │ can "go away"
    │ mutual benefit while engaged           │ mutual benefit while engaged
    │                                        │
    ▼                                        ▼
Character (autonomous NPC brain)         Dungeon (autonomous Actor brain)
    always running                           always running
    acts independently when                  acts independently when
    player is absent                         master is absent
```

Both relationships share key properties:
- **Asymmetric agency**: One party has broader context (player sees UX, character sees dungeon's rooms), the other has deeper local knowledge (character knows its body, dungeon knows its domain)
- **Graceful absence**: The autonomous entity continues functioning when its partner disengages
- **Progressive depth**: The relationship deepens with shared experience (guardian seed grows, dungeon_master seed grows)
- **Contractual terms**: Governed by agreements with consequences (implicit for player-character, explicit Contract for character-dungeon)

### The God Pattern Parallel

Regional Watchers (gods) demonstrate that long-running Event Actors can:
- Subscribe to domain-specific events
- Make intelligent decisions about when to act
- Spawn Event Agents for specific situations
- Execute game server APIs

Dungeons follow the same pattern but with a key addition: **a bonded partner** whose growth in the mastery role is tracked by its own seed.

### Why Seed + Actor (Not Just Actor)

Without lib-seed, the dungeon actor would need to maintain its own ad-hoc growth tracking, capability gating, and relationship quality measurement. This duplicates patterns already solved by lib-seed:

| Concern | Without Seed (Ad-Hoc) | With Seed (Generic) |
|---------|----------------------|---------------------|
| Dungeon growth tracking | Custom `DungeonActorState` fields | `dungeon_core` seed growth domains |
| Dungeon capability gating | Hardcoded thresholds in ABML | Seed capability rules with configurable formulas |
| Dungeon phase progression | Custom logic per dungeon | Seed phase definitions (Dormant/Stirring/Awakened/Ancient) |
| Master's role growth | Custom bond strength field | `dungeon_master` seed growth domains |
| Prior mastery experience | Lost on bond dissolution | Archived `dungeon_master` seed retains growth |
| Cross-entity reuse | One-off implementation | Same primitive used by guardians, factions, lineages |

The actor handles what changes tick-to-tick (feelings, combat state, room hazards). The seeds handle what accumulates over time (dungeon growth, mastery growth). Clean separation of volatile vs. progressive state.

---

## Part 2: Dungeon Actor Architecture

### Two Seed Types

The dungeon system introduces two seed types:

#### dungeon_core (The Dungeon's Growth)

```
SeedTypeDefinition
├── SeedTypeCode: "dungeon_core"
├── DisplayName: "Dungeon Core"
├── Description: "Progressive growth entity for living dungeons"
├── MaxPerOwner: 1          -- one seed per dungeon actor
├── AllowedOwnerTypes: ["actor"]
│     -- only actors (dungeon cores) own this seed type
├── GrowthPhases (ordered):
│   ├── Dormant    (MinTotalGrowth: 0.0)     -- newly formed, minimal capability
│   ├── Stirring   (MinTotalGrowth: 10.0)    -- basic spawning and traps
│   ├── Awakened   (MinTotalGrowth: 50.0)    -- layout manipulation, complex traps
│   └── Ancient    (MinTotalGrowth: 200.0)   -- full capability, memory manifestation
├── BondCardinality: 0       -- no seed-to-seed bonds (Contract handles the relationship)
└── CapabilityRules:
    ├── spawn_monster.basic     (domain: genetic_library, threshold: 1.0, formula: linear)
    ├── spawn_monster.enhanced  (domain: genetic_library, threshold: 5.0, formula: logarithmic)
    ├── spawn_monster.alpha     (domain: genetic_library, threshold: 20.0, formula: step)
    ├── activate_trap.basic     (domain: trap_complexity, threshold: 1.0, formula: linear)
    ├── activate_trap.complex   (domain: trap_complexity, threshold: 10.0, formula: logarithmic)
    ├── seal_passage             (domain: domain_expansion, threshold: 5.0, formula: step)
    ├── shift_layout             (domain: domain_expansion, threshold: 15.0, formula: step)
    ├── emit_miasma              (domain: mana_reserves, threshold: 3.0, formula: linear)
    ├── spawn_event_agent        (domain: mana_reserves, threshold: 10.0, formula: step)
    ├── evolve_inhabitant        (domain: genetic_library, threshold: 10.0, formula: logarithmic)
    └── manifest_memory          (domain: memory_depth, threshold: 8.0, formula: step)
```

#### dungeon_master (The Master's Role Growth)

```
SeedTypeDefinition
├── SeedTypeCode: "dungeon_master"
├── DisplayName: "Dungeon Master"
├── Description: "Growth in the dungeon mastery role -- command, channeling, coordination"
├── MaxPerOwner: 1          -- one mastery seed per entity (can only master one dungeon)
├── AllowedOwnerTypes: ["character", "actor"]
│     -- "character" for willing bonds (Priest/Paladin)
│     -- "actor" for monster avatars (Corrupted) since monsters are actor-managed
├── GrowthPhases (ordered):
│   ├── Bonded       (MinTotalGrowth: 0.0)    -- freshly bonded, basic communication
│   ├── Attuned      (MinTotalGrowth: 5.0)    -- clear communication, basic commands
│   ├── Symbiotic    (MinTotalGrowth: 25.0)   -- rich command vocabulary, power channeling
│   └── Transcendent (MinTotalGrowth: 100.0)  -- near-perfect coordination, shared consciousness
├── BondCardinality: 0       -- no seed-to-seed bonds (Contract handles the relationship)
└── CapabilityRules:
    ├── perception.basic        (domain: perception, threshold: 0.0, formula: linear)
    │     -- can sense dungeon's emotional state from moment of bonding
    ├── perception.tactical      (domain: perception, threshold: 5.0, formula: logarithmic)
    │     -- can perceive intruder details, room states
    ├── command.basic            (domain: command, threshold: 2.0, formula: linear)
    │     -- simple directives: "spawn defenders", "activate trap"
    ├── command.tactical         (domain: command, threshold: 10.0, formula: logarithmic)
    │     -- complex directives: multi-wave defense, ambush coordination
    ├── channeling.basic         (domain: channeling, threshold: 3.0, formula: linear)
    │     -- receive mana/power from core
    ├── channeling.combat        (domain: channeling, threshold: 15.0, formula: logarithmic)
    │     -- channel core's combat abilities in battle
    └── coordination.event_brain (domain: coordination, threshold: 20.0, formula: step)
          -- direct the dungeon's event coordinators personally
```

### Growth Domains

#### Dungeon Core Growth Domains

```
# dungeon_core seed growth domains
mana_reserves              → accumulated mana capacity
mana_reserves.ambient      → passive mana from leyline proximity
mana_reserves.harvested    → mana from monster deaths within domain

genetic_library            → aggregate logos completion across species
genetic_library.wolf       → wolf species logos completion (0.0 to unbounded)
genetic_library.golem      → golem species logos completion
genetic_library.dragon     → dragon species logos completion (very expensive)

trap_complexity            → trap design sophistication
trap_complexity.mechanical → physical trap mechanisms
trap_complexity.magical    → magical trap complexity
trap_complexity.puzzle     → puzzle/logic trap complexity

domain_expansion           → spatial control capability
domain_expansion.radius    → domain boundary extent
domain_expansion.rooms     → number of controllable rooms
domain_expansion.extension → extension core management

memory_depth               → memory accumulation and significance
memory_depth.capture       → memory capture sensitivity
memory_depth.manifestation → manifestation quality and variety
```

#### Dungeon Master Growth Domains

```
# dungeon_master seed growth domains
perception                 → shared awareness with the dungeon core
perception.emotional       → sensing the dungeon's feelings/state
perception.spatial         → seeing through the dungeon's "eyes" (room awareness)
perception.tactical        → understanding intruder capabilities and intentions

command                    → directing the dungeon's actions
command.spawning           → directing creature deployment
command.traps              → directing trap activation and placement
command.layout             → directing passage sealing and structural changes
command.strategy           → multi-stage tactical coordination

channeling                 → mana/power flow between core and master
channeling.passive         → passive strength bonus from bond
channeling.combat          → active combat ability channeling
channeling.regeneration    → healing/restoration from core's mana

coordination               → directing the dungeon's autonomous systems
coordination.defenders     → coordinating monster formations
coordination.encounters    → directing event brain coordinators
coordination.manifestation → guiding memory manifestation choices
```

#### Growth Contributions

| Event | Core Seed Domain | Master Seed Domain | Source |
|-------|-----------------|-------------------|--------|
| Monster killed within domain | `mana_reserves.harvested` +0.1 | -- | Actor (dungeon cognition) |
| Logos absorbed from death | `genetic_library.{species}` +varies | -- | Actor (dungeon cognition) |
| Trap successfully triggers | `trap_complexity.*` +0.2 | `command.traps` +0.1 (if master directed) | Actor (dungeon cognition) |
| Domain boundary expanded | `domain_expansion.*` +1.0 | -- | Actor (domain management) |
| Master command executed | -- | `command.*` +0.1 | Actor (bond communication) |
| Master perceives dungeon state | -- | `perception.*` +0.05 | Actor (bond communication) |
| Master channels power | -- | `channeling.*` +0.2 | Actor (bond communication) |
| Significant memory stored | `memory_depth.capture` +0.5 | `perception.emotional` +0.1 (if master present) | Actor (memory system) |
| Memory manifested | `memory_depth.manifestation` +1.0 | `coordination.manifestation` +0.5 (if master guided) | Actor (memory system) |
| Adventurers defeated | `mana_reserves` +0.5, `genetic_library` +0.1 | `command.strategy` +0.3 (if master directed defense) | Analytics (milestone) |

### Actor Type Definition

```yaml
# Actor template for a dungeon core
metadata:
  id: dungeon-core-template
  type: event_brain
  category: dungeon_core
  description: "Living dungeon consciousness backed by a dungeon_core seed for progressive growth"

config:
  domain: "dungeon"

  # Cognition template: simplified creature-like processing
  cognition_template: "creature_base"

  # Dungeon-specific configuration
  dungeon:
    core_location: "${dungeon.core_room_id}"
    domain_radius: 500  # meters from core
    extension_cores: []  # additional influence bubbles
    seed_type_code: "dungeon_core"  # lib-seed type for this actor

  # Event subscriptions
  subscriptions:
    - "dungeon.${dungeon_id}.intrusion"      # Someone entered
    - "dungeon.${dungeon_id}.combat.*"       # Combat within domain
    - "dungeon.${dungeon_id}.death.*"        # Deaths within domain
    - "dungeon.${dungeon_id}.loot.*"         # Treasure taken
    - "dungeon.${dungeon_id}.trap.*"         # Trap triggered/disarmed
    - "dungeon.${dungeon_id}.structure.*"    # Structural damage
    - "dungeon.${dungeon_id}.inhabitant.*"   # Monster spawned/killed

  # Capabilities (availability gated by dungeon_core seed capability manifest)
  capabilities:
    - spawn_monster           # Create pneuma echoes from genetic library
    - activate_trap           # Trigger trap systems
    - seal_passage            # Block/unblock passages
    - shift_layout            # Minor structural changes
    - distribute_treasure     # Place loot in containers
    - emit_miasma             # Increase/decrease ambient mana density
    - spawn_event_agent       # Create encounter coordinators
    - communicate_master      # Send perceptions to bonded dungeon master
    - evolve_inhabitant       # Upgrade monster echoes
    - manifest_memory         # Crystallize memories into physical form
```

### Dungeon Actor State (Volatile Only)

With growth, capabilities, and role tracking handled by seeds (dungeon_core + dungeon_master), the dungeon actor state is reduced to **volatile, tick-to-tick operational data**:

```csharp
/// <summary>
/// Volatile actor state for dungeon core cognition. Progressive growth
/// lives in the dungeon_core seed; master role growth lives in the
/// dungeon_master seed; this tracks only what changes per cognitive tick.
/// </summary>
public class DungeonActorState
{
    // Seed reference
    public Guid SeedId { get; set; }               // Reference to this dungeon's dungeon_core seed

    // Core vitals (volatile -- current values, not historical growth)
    public float CoreIntegrity { get; set; }        // 0-1, structural health of core
    public float CurrentMana { get; set; }          // Current mana pool (volatile balance)
    public float ManaGenerationRate { get; set; }   // Per-tick generation (derived from seed growth)

    // Domain (spatial, managed by Mapping service)
    public Guid CoreRoomId { get; set; }
    public List<Guid> ExtensionCoreIds { get; set; }

    // Population (current inhabitants, not historical growth)
    public Dictionary<string, int> InhabitantCounts { get; set; }  // By species
    public int SoulSlotsUsed { get; set; }

    // Defenses (current trap states, not trap capability)
    public Dictionary<Guid, TrapState> ActiveTraps { get; set; }
    public Dictionary<Guid, float> RoomHazardLevels { get; set; }

    // Bond reference (volatile -- Contract is the durable record)
    public Guid? BondContractId { get; set; }       // Active contract with master, if any
    public Guid? BondedMasterEntityId { get; set; } // Character or monster actor ID
    public string? BondedMasterType { get; set; }   // "character" or "actor" (monster)

    // Feelings (creature-level emotions, volatile per tick)
    public Dictionary<string, float> Feelings { get; set; }  // hunger, threat, satisfaction

    // Memories (accumulated from events -- actor-local, contribute to seed growth)
    public List<DungeonMemory> Memories { get; set; }

    // Current intruders (volatile tracking for cognition pipeline)
    public List<IntruderTracking> ActiveIntruders { get; set; }
}
```

### Key Distinction: Volatile vs. Progressive

```
┌─────────────────────────────────────────────────────────────────────────┐
│                      DUNGEON STATE ARCHITECTURE                         │
│                                                                         │
│   ┌──────────────────────┐  ┌─────────────────────┐  ┌──────────────┐ │
│   │  ACTOR STATE         │  │  DUNGEON_CORE SEED  │  │ DUNGEON_     │ │
│   │  (Volatile, Redis)   │  │  (Progressive, MySQL)│  │ MASTER SEED │ │
│   ├──────────────────────┤  ├─────────────────────┤  │ (Progressive)│ │
│   │ CoreIntegrity        │  │ Growth Domains:     │  ├──────────────┤ │
│   │ CurrentMana          │  │  mana_reserves 47.3 │  │ Growth:      │ │
│   │ ManaGenerationRate◄──│──│  genetic_lib.* 3.2  │  │  perception  │ │
│   │ InhabitantCounts     │  │  trap_complex. 8.1  │  │    12.3      │ │
│   │ ActiveTraps          │  │  domain_exp.  12.0  │  │  command 8.7 │ │
│   │ RoomHazardLevels     │  │  memory_depth 15.2  │  │  channeling  │ │
│   │ Feelings             │  │                     │  │    5.1       │ │
│   │ Memories ────────────│─►│ Capabilities:       │  │  coord. 3.2  │ │
│   │ ActiveIntruders      │  │  spawn_monster ✓    │  │              │ │
│   │ BondContractId       │  │  shift_layout ✓     │  │ Capabilities:│ │
│   │ BondedMasterEntityId │  │  manifest_mem ✓     │  │  perception  │ │
│   │ BondedMasterType     │  │  spawn_alpha ✗      │  │   .tactical ✓│ │
│   └──────────────────────┘  │                     │  │  command     │ │
│                              │ Phase: Awakened     │  │   .tactical ✓│ │
│       CONTRACT               │ Metadata:           │  │  channeling  │ │
│   ┌──────────────────────┐  │  personality: martial│  │   .combat ✓ │ │
│   │ Bond Type: Paladin   │  │                     │  │              │ │
│   │ Death Clause: ...    │  └─────────────────────┘  │ Phase:       │ │
│   │ Power Sharing: ...   │           ▲                │  Symbiotic   │ │
│   │ Milestones: ...      │           │ growth         │              │ │
│   └──────────────────────┘    events contribute      │ Owner:       │ │
│                                                       │  character   │ │
│                                                       └──────────────┘ │
└─────────────────────────────────────────────────────────────────────────┘
```

### Perception Processing

Dungeons use a simplified cognition pipeline (`creature_base`). Capability checks query the dungeon_core seed manifest; communication with the master is gated by the master's dungeon_master seed capabilities:

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    DUNGEON COGNITION PIPELINE                           │
│                                                                         │
│   1. FILTER ATTENTION                                                   │
│   ───────────────────                                                   │
│   • Priority: intrusion (10), combat (8), death (7), loot (5)          │
│   • Threat fast-track: urgency > 0.8 bypasses to action                │
│   • Attention budget: 50 perceptions per tick                          │
│                                                                         │
│   2. MEMORY QUERY (simplified)                                          │
│   ────────────────────────────                                          │
│   • Query: "Have I seen these intruders before?"                       │
│   • Query: "What happened last time in this room?"                     │
│   • Entity-indexed lookup for adventurer history                       │
│                                                                         │
│   3. CAPABILITY CHECK (via dungeon_core seed manifest)                  │
│   ────────────────────────────────────────────────────                  │
│   • Query seed capability manifest for available actions               │
│   • Higher seed fidelity → better execution quality                    │
│   • Unlocked capabilities gate which intentions can form               │
│                                                                         │
│   4. MASTER COMMUNICATION CHECK (via dungeon_master seed manifest)      │
│   ────────────────────────────────────────────────────────────────      │
│   • If bonded: check master's perception.* capabilities                │
│   • Higher perception fidelity → richer information shared             │
│   • If master has command.tactical: accept complex directives          │
│   • If no bond or Corrupted bond: skip (dungeon acts autonomously)     │
│                                                                         │
│   5. INTENTION FORMATION                                                │
│   ──────────────────────                                                │
│   • Apply master commands (if any, and if command capability allows)    │
│   • Evaluate threats → spawn defenders or activate traps               │
│   • Evaluate opportunities → absorb corpses, claim resources           │
│   • Evaluate memories → store significant events                       │
│   • Publish seed.growth.contributed for significant actions            │
│                                                                         │
│   SKIPPED STAGES (unlike humanoid_base):                               │
│   • No significance assessment (dungeon cares about everything)        │
│   • No goal impact evaluation (goals are fixed: survive, grow)         │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Part 3: The Dungeon Master Bond

### Contract-Driven Bonding

The dungeon-master bond is managed entirely by the **Contract service**. When a bond forms, the Contract triggers creation of a `dungeon_master` seed for the bonded entity. The two seeds (dungeon_core and dungeon_master) are not bonded to each other via the seed bond system -- they grow independently in parallel, connected by the Contract.

```
┌────────────────────────────────────────────────────────────────────────┐
│                    CONTRACT-DRIVEN BOND ARCHITECTURE                   │
│                                                                        │
│   DUNGEON CORE                           DUNGEON MASTER               │
│   ────────────                           ──────────────               │
│   Actor (event_brain)                    Character or Monster          │
│   dungeon_core seed                      dungeon_master seed           │
│   (owner: actor)                         (owner: character or actor)   │
│        │                                       │                       │
│        │           ┌───────────────┐           │                       │
│        └───────────│   CONTRACT    │───────────┘                       │
│                    │               │                                   │
│                    │ Template:     │                                   │
│                    │  dungeon-     │                                   │
│                    │  master-bond  │                                   │
│                    │               │                                   │
│                    │ Bond Type:    │                                   │
│                    │  Priest |     │                                   │
│                    │  Paladin |    │                                   │
│                    │  Corrupted    │                                   │
│                    │               │                                   │
│                    │ Terms:        │                                   │
│                    │  death clause │                                   │
│                    │  power sharing│                                   │
│                    │               │                                   │
│                    │ Milestones:   │                                   │
│                    │  initial_bond │  ← triggered by contract creation │
│                    │  attuned      │  ← triggered by master seed phase │
│                    │  symbiotic    │  ← triggered by master seed phase │
│                    │  transcendent │  ← triggered by master seed phase │
│                    └───────────────┘                                   │
│                                                                        │
│   Seeds grow independently:                                            │
│   • dungeon_core grows from dungeon events (mana, genetics, traps)    │
│   • dungeon_master grows from role fulfillment (commands, perception)  │
│   • No seed-to-seed bond needed -- Contract is the relationship       │
│   • The dungeon can outgrow its master (or vice versa)                │
│                                                                        │
└────────────────────────────────────────────────────────────────────────┘
```

### Bond Formation Flow

```yaml
flow: character_bond_formation
description: "Character discovers dungeon core and negotiates bond"

steps:
  # 1. Core offers bond
  - core_discovery:
      dungeon_actor: "${dungeon_actor_id}"
      character: "${character_id}"
      action: present_bond_terms

  # 2. Character (player or NPC) reviews terms
  - review_terms:
      bond_type: "${chosen_bond_type}"  # Priest, Paladin, or Corrupted
      power_sharing: "Dungeon master gains strength from core; core gains protection"
      death_clause: "Per bond type"

  # 3. If accepted: Create Contract
  - create_contract:
      call: /contract/create
      template: "dungeon-master-bond"
      parties:
        dungeon_core: { entityId: "${dungeon_actor_id}", entityType: "actor" }
        dungeon_master: { entityId: "${character_id}", entityType: "character" }
      terms:
        bond_type: "${chosen_bond_type}"

  # 4. Contract creation triggers (via prebound API): create dungeon_master seed
  - create_master_seed:
      call: /seed/create
      params:
        ownerId: "${character_id}"
        ownerType: "character"
        seedTypeCode: "dungeon_master"
        gameServiceId: "${game_service_id}"
        displayName: "Mastery of ${dungeon_name}"

  # 5. Update dungeon actor state with bond reference
  - update_actor:
      bondContractId: "${contract_id}"
      bondedMasterEntityId: "${character_id}"
      bondedMasterType: "character"

  # 6. Publish events
  - publish:
      topic: "dungeon.${dungeon_id}.bond.formed"
      payload: { master_id: "${character_id}", bond_type: "${chosen_bond_type}", master_seed_id: "${seed_id}" }
```

### Bond Types

| Bond Type | Master Entity | Core-Master Relationship | Death Behavior | Master Seed Effect |
|-----------|--------------|-------------------------|----------------|--------------------|
| **Priest** | Character (willing) | Core provides mana, master provides direction | Bond and master seed growth preserved through master death | Full growth tracking; seed archived on bond dissolution but retains growth |
| **Paladin** | Character (willing) | Core channels combat abilities through master | Master seed growth halved on death; bond can be re-formed | Full growth tracking; channeling.combat domains grow faster |
| **Corrupted** | Monster (dominated) | Core dominates, master is avatar | Core dies if avatar destroyed | Minimal seed -- tracks growth but monster has limited agency to direct it |

### Communication Flow

Communication fidelity is determined by the master's `dungeon_master` seed capabilities (perception, command, channeling domains):

```
┌──────────────────────────────────────────────────────────────────────┐
│                    DUNGEON-MASTER COMMUNICATION                       │
│                                                                       │
│   DUNGEON CORE ACTOR                    DUNGEON MASTER (Character)   │
│   ──────────────────                    ──────────────────────────   │
│                                                                       │
│   Perceives: Intrusion detected         Receives: "I sense intruders"│
│        ↓                                (fidelity: perception.tactical│
│   Evaluates: 4 adventurers, level 6      determines detail level)    │
│        ↓                                      ↓                       │
│   Options:                              Decides: "Send wolves first" │
│   - Spawn defenders                     (only if command.spawning    │
│   - Activate traps                       capability is unlocked)     │
│   - Seal passages                             ↓                       │
│        ↓                                Commands: spawn_monster       │
│                                         (publishes seed.growth to    │
│   Receives command from master           command.spawning +0.1)      │
│        ↓                                      ↓                       │
│   Executes: Spawn wolf pack             Observes: Wolves engaging    │
│   (publishes seed.growth to                   ↓                       │
│    genetic_library.wolf +0.05)          Adjusts: "Seal west passage" │
│        ↓                                (only if command.layout      │
│   Combat unfolds...                      capability is unlocked)     │
│        ↓                                      ↓                       │
│   Perceives: Wolves killed              Feels: Core's pain/loss      │
│   Absorbs: Logos from adventurer death  (perception.emotional grows) │
│   Stores: Memory of this battle                                      │
│   (publishes seed.growth to             Gains: Power from absorption │
│    mana_reserves +0.5,                  (channeling.passive grows)   │
│    memory_depth.capture +0.5)                                        │
└──────────────────────────────────────────────────────────────────────┘
```

### Contract Template

```yaml
# Contract template for dungeon-master bond
template_id: dungeon-master-bond
template_name: "Dungeon Core Bond"
template_type: actor_partnership

parties:
  - role: "dungeon_core"
    entity_type: actor
    required: true
  - role: "dungeon_master"
    entity_type: character  # or actor, for monster bonds
    required: true

terms:
  bond_type:
    type: enum
    values: ["priest", "paladin", "corrupted"]
    description: "Priest = non-combatant focus, Paladin = combatant focus, Corrupted = forced bond"

  power_sharing:
    description: "Dungeon master gains strength from core; core gains protection"

  death_clause:
    description: "If core dies, master typically dies. If master dies, core can select new master."

# Prebound APIs: executed on milestone transitions
prebound:
  on_contract_created:
    - call: /seed/create  # Create dungeon_master seed for the bonded entity
      params:
        ownerType: "${dungeon_master.entity_type}"
        seedTypeCode: "dungeon_master"

milestones:
  - name: "initial_bond"
    description: "Core and master successfully bond"
    # Triggered by contract creation

  - name: "attuned"
    description: "Master reaches Attuned phase in dungeon_master seed"
    # Triggered by seed.phase.changed event for the master's seed

  - name: "symbiotic"
    description: "Master reaches Symbiotic phase"
    # Triggered by seed.phase.changed event

  - name: "transcendent"
    description: "Master reaches Transcendent phase -- near-perfect coordination"
    # Triggered by seed.phase.changed event

enforcement: consequence_based
```

---

## Part 4: Dungeon Capabilities

Capabilities are gated by the dungeon_core seed capability manifest. The dungeon actor's cognition pipeline queries `seed/capability/get-manifest` and only forms intentions for unlocked capabilities. Capability fidelity (0.0-1.0) affects execution quality.

### Monster Spawning

```yaml
capability: spawn_monster
seed_capability_code: spawn_monster.basic  # Must be unlocked in dungeon_core seed
description: "Create a pneuma echo from the dungeon's genetic library"

parameters:
  species_id: string          # From genetic library
  room_id: Guid               # Where to spawn
  quality_level: float        # 0-1, logos investment
  count: int                  # Number to spawn

preconditions:
  - "${actor.current_mana} >= ${spawn_cost}"
  - "${seed.growth.genetic_library[species_id]} > 0"
  - "${room_id} in ${dungeon.domain}"

effects:
  - deduct: "${spawn_cost} from ${actor.current_mana}"
  - create: "pneuma_echo entities in ${room_id}"
  - increment: "${actor.inhabitant_counts[species_id]}"
  - publish: "seed.growth.contributed" to genetic_library.{species_id} +0.05
  - emit: "dungeon.*.inhabitant.spawned"

quality_scaling:
  # Quality tiers gated by seed capability fidelity and unlock level
  minimal:  { cost_multiplier: 0.5, stats_multiplier: 0.7, requires: spawn_monster.basic }
  standard: { cost_multiplier: 1.0, stats_multiplier: 1.0, requires: spawn_monster.basic }
  enhanced: { cost_multiplier: 2.0, stats_multiplier: 1.3, requires: spawn_monster.enhanced }
  maximum:  { cost_multiplier: 5.0, stats_multiplier: 1.6, requires: spawn_monster.enhanced }
  alpha:    { cost_multiplier: 10.0, stats_multiplier: 2.0, requires: spawn_monster.alpha }
```

### Trap Activation

```yaml
capability: activate_trap
seed_capability_code: activate_trap.basic  # Must be unlocked in dungeon_core seed
description: "Trigger a trap system in a room"

parameters:
  trap_id: Guid
  target_ids: List<Guid>      # Optional: specific targets

preconditions:
  - "${actor.active_traps[trap_id].state} == 'ready'"
  - "${actor.active_traps[trap_id].mana_charge} > 0"

effects:
  - set: "${actor.active_traps[trap_id].state} = 'triggered'"
  - deduct: "mana from trap charge"
  - publish: "seed.growth.contributed" to trap_complexity.* +0.2
  - emit: "dungeon.*.trap.triggered"
  - call: "/game-server/apply-trap-effect"
```

### Memory Manifestation

```yaml
capability: manifest_memory
seed_capability_code: manifest_memory  # Must be unlocked in dungeon_core seed
description: "Crystallize an accumulated memory into physical form"

parameters:
  memory_id: Guid
  manifestation_type: enum    # painting, data_crystal, memory_item, environmental
  room_id: Guid               # Where to manifest

preconditions:
  - "${actor.memories[memory_id].significance} >= 0.5"
  - "${actor.memories[memory_id].has_manifested} == false"
  - "${actor.current_mana} >= ${manifestation_cost}"

effects:
  - set: "${actor.memories[memory_id].has_manifested} = true"
  - call: "/item/create-from-template" if memory_item
  - call: "/scene/add-decoration" if painting or environmental
  - publish: "seed.growth.contributed" to memory_depth.manifestation +1.0
  - emit: "dungeon.*.memory.manifested"

# Manifestation quality scales with dungeon_core seed capability fidelity:
# Low fidelity (0.1-0.3): crude, blurry manifestations
# Medium fidelity (0.3-0.7): clear but simple manifestations
# High fidelity (0.7-1.0): vivid, detailed, potentially interactive manifestations
```

### Spawn Event Coordinator

```yaml
capability: spawn_event_agent
seed_capability_code: spawn_event_agent  # Must be unlocked in dungeon_core seed
description: "Create an encounter coordinator for complex situations"

parameters:
  template_id: string         # e.g., "dungeon-defense-coordinator"
  context: object             # Situation-specific data

templates:
  dungeon-defense-coordinator:
    description: "Coordinates multi-wave defense against adventurers"
    receives: intruder_ids, room_sequence, available_forces

  ambush-coordinator:
    description: "Sets up and executes an ambush"
    receives: target_ids, ambush_room, ambush_forces

  boss-encounter-coordinator:
    description: "Manages boss fight with phases and mechanics"
    receives: boss_id, arena_room, phase_definitions

  memory-harvest-coordinator:
    description: "Manages absorption of significant deaths"
    receives: deceased_ids, logos_to_extract
```

---

## Part 5: Integration with Existing Services

### Service Dependencies

| Service | Layer | Dungeon Actor Usage |
|---------|-------|-------------------|
| **Seed Service** | L2 | `dungeon_core` and `dungeon_master` seed types, growth domains, capability manifests |
| **Actor Service** | L2 | Actor lifecycle, template management, pool nodes, cognition pipeline |
| **Character Service** | L2 | Dungeon master data, adventurer info |
| **Character Encounter** | L4 | Track adventurer visits, build history |
| **Contract Service** | L1 | Dungeon-master bond management (terms, milestones, prebound seed creation) |
| **Currency Service** | L2 | Dungeon treasury (mana as currency, if using wallets) |
| **Inventory Service** | L2 | Trap/treasure container management |
| **Item Service** | L2 | Memory item creation, loot generation |
| **Mapping Service** | L4 | Domain boundaries, room connectivity |
| **Scene Service** | L4 | Memory paintings, environmental decorations |
| **Analytics Service** | L4 | Event significance scoring, milestone-triggered seed growth |

### Variable Providers

Variable providers expose seed data and actor state to ABML behavior documents via the Variable Provider Factory pattern.

```csharp
// DungeonCoreSeedVariableProvider - exposes dungeon_core seed data to ABML
services.AddSingleton<IVariableProviderFactory, DungeonCoreSeedVariableProviderFactory>();

// Provides (from dungeon_core seed growth domains):
// ${seed.growth.mana_reserves}
// ${seed.growth.genetic_library}
// ${seed.growth.genetic_library.wolf}
// ${seed.growth.trap_complexity}
// ${seed.growth.domain_expansion}
// ${seed.growth.memory_depth}
//
// Provides (from dungeon_core seed capability manifest):
// ${seed.capability.spawn_monster.basic}          -- bool, unlocked?
// ${seed.capability.spawn_monster.basic.fidelity} -- float, 0.0-1.0
// ${seed.capability.manifest_memory}              -- bool, unlocked?
//
// Provides (from dungeon_core seed entity):
// ${seed.phase}              -- "Dormant", "Stirring", "Awakened", "Ancient"
// ${seed.total_growth}       -- aggregate growth across all domains
```

```csharp
// DungeonMasterSeedVariableProvider - exposes dungeon_master seed data to ABML
// Only active when a bond exists (master seed exists and is Active)
services.AddSingleton<IVariableProviderFactory, DungeonMasterSeedVariableProviderFactory>();

// Provides (from dungeon_master seed):
// ${master.seed.phase}                  -- "Bonded", "Attuned", "Symbiotic", "Transcendent"
// ${master.seed.capability.perception.tactical}  -- bool
// ${master.seed.capability.command.tactical}      -- bool
// ${master.seed.capability.channeling.combat}     -- bool
// ${master.seed.capability.perception.tactical.fidelity} -- float
```

```csharp
// DungeonActorVariableProvider - exposes volatile actor state to ABML
services.AddSingleton<IVariableProviderFactory, DungeonActorVariableProviderFactory>();

// Provides (from DungeonActorState):
// ${dungeon.core_integrity}
// ${dungeon.current_mana}
// ${dungeon.mana_generation_rate}
// ${dungeon.inhabitant_count}
// ${dungeon.active_intruders}
// ${dungeon.feelings.hunger}
// ${dungeon.feelings.threat}
// ${dungeon.feelings.satisfaction}
// ${dungeon.has_master}         -- bool, derived from BondContractId != null
```

```csharp
// DungeonMasterCharacterVariableProvider - exposes master character data to ABML
// Only active when bonded to a character (not for Corrupted monster bonds)
services.AddSingleton<IVariableProviderFactory, DungeonMasterCharacterVariableProviderFactory>();

// Provides (when bonded to a character):
// ${master.health}
// ${master.mana}
// ${master.location}
// ${master.in_combat}
// ${master.commands_pending}
```

### Event Topics

```yaml
# Dungeon publishes
dungeon.{dungeon_id}.state_updated:
  description: "Dungeon vitals changed"
  payload: { core_integrity, current_mana, threat_level }

dungeon.{dungeon_id}.inhabitant.spawned:
  description: "Monster created"
  payload: { species_id, room_id, count }

dungeon.{dungeon_id}.memory.captured:
  description: "Significant event stored"
  payload: { event_type, significance, participants }

dungeon.{dungeon_id}.memory.manifested:
  description: "Memory became physical"
  payload: { memory_id, manifestation_type, room_id }

dungeon.{dungeon_id}.bond.formed:
  description: "New dungeon master bonded"
  payload: { master_id, master_type, bond_type, contract_id, master_seed_id }

# Dungeon publishes to Seed (growth contributions for BOTH seeds)
seed.growth.contributed:
  description: "Dungeon events contribute growth to dungeon_core and/or dungeon_master seeds"
  payload: { seed_id, domain, amount, source: "dungeon-actor", source_event_id }
  # Note: dungeon actor publishes growth to BOTH seeds where applicable
  # e.g., a successful master command grows command.* on the master seed
  #       AND may grow mana_reserves on the core seed (if it resulted in mana gain)

# Dungeon subscribes
character.{character_id}.entered_dungeon:
  description: "Character crossed dungeon boundary"

character.{character_id}.exited_dungeon:
  description: "Character left dungeon"

combat.{dungeon_id}.*:
  description: "Combat events within dungeon domain"

# Dungeon subscribes (from Seed)
seed.phase.changed:
  description: "Seed growth phase transition"
  # For dungeon_core: triggers behavior mode changes (e.g., Stirring enables traps)
  # For dungeon_master: triggers contract milestone transitions

seed.capability.updated:
  description: "Seed capability manifest recomputed"
  # Triggers dungeon to re-evaluate available actions (core or master capabilities changed)
```

---

## Part 6: Spawning and Corruption Mechanics

### Monster as Dungeon Master

When no suitable character is available, the dungeon can **spawn or corrupt a monster** to serve as its avatar. The monster receives a `dungeon_master` seed (owner: actor, since monsters are actor-managed), but with limited agency to grow it deliberately -- growth happens passively through the bond:

```yaml
flow: attempt_monster_bond
description: "Dungeon creates or corrupts a monster to serve as master"

steps:
  # Option 1: Spawn a new high-quality monster
  - cond:
      - when: "${dungeon.current_mana} >= ${avatar_spawn_cost}"
        when: "${seed.capability.spawn_monster.enhanced}"  # Requires enhanced spawning
        then:
          - spawn_monster:
              species_id: "${strongest_species_by_seed_growth}"
              quality_level: 1.0  # Maximum quality
              room_id: "${dungeon.core_room_id}"
              designate_as: "avatar_candidate"
          - create_contract:
              template: "dungeon-master-bond"
              bond_type: "corrupted"
              # Prebound API creates dungeon_master seed for the monster

  # Option 2: Corrupt existing powerful monster
  - cond:
      - when: "${dungeon.inhabitants.any(x => x.level >= 5)}"
        then:
          - select: "${dungeon.inhabitants.max_by(level)}"
          - corrupt:
              target_id: "${selected.id}"
              corruption_intensity: 0.8
          - create_contract:
              template: "dungeon-master-bond"
              bond_type: "corrupted"
```

### Corruption Process

```csharp
public class CorruptionResult
{
    public Guid OriginalMonsterId { get; set; }
    public Guid CorruptedMonsterId { get; set; }

    // Corruption effects
    public float IntelligenceBoost { get; set; }    // Gains sapience
    public float PowerMultiplier { get; set; }       // Stat increases
    public List<string> NewAbilities { get; set; }   // Core-granted powers
    public string NewAppearance { get; set; }        // Visual corruption

    // Bond effects
    public bool IsFullyControlled { get; set; }      // True = avatar, false = partner
    public float AutonomyLevel { get; set; }         // How much independent will remains

    // Seed reference
    public Guid MasterSeedId { get; set; }           // dungeon_master seed created for monster
    public Guid ContractId { get; set; }             // Contract governing the bond
}
```

### Contract with NPCs/Players (Willing Bond)

For character dungeon masters (not corrupted monsters), the full bond flow applies:

```yaml
# Player discovers dungeon core, chooses to bond
event: core_discovery
  - core offers bond (displays contract terms)
  - player reviews: power gains, restrictions, death clause
  - player accepts or rejects
  - if accepted:
      1. Contract.create(dungeon-master-bond, parties=[core, character])
      2. Contract prebound API creates dungeon_master seed for character
      3. bond_formed event published
      4. player character gains dungeon master abilities
         (gated by dungeon_master seed capabilities, starting at Bonded phase)
      5. player's NPC brain actor now receives dungeon perceptions
         (fidelity gated by master's perception.* capability)
```

---

## Part 7: Memory and Dungeon Personality

### Memory Capture Integration

The dungeon actor integrates with the **Memory Accumulation System** from arcadia-kb. Significant memories contribute seed growth to the dungeon_core's `memory_depth.*` domains, and if the master is present, to the master's `perception.emotional` domain:

```yaml
flow: evaluate_memory_capture
description: "Determine if an event should be stored as a dungeon memory"

steps:
  - receive: event from perception queue

  - calculate_significance:
      base: "${event.base_significance}"
      modifiers:
        - death_involved: +0.3
        - high_mana_expenditure: +0.2
        - emotional_intensity: +0.2
        - first_occurrence: +0.2
        - involves_known_adventurers: +0.1

  - cond:
      - when: "${significance} >= 0.5"
        then:
          - store_memory:
              event_id: "${event.id}"
              event_type: "${event.type}"
              significance: "${significance}"
              content:
                participants: "${event.participants}"
                location: "${event.room_id}"
                outcome: "${event.outcome}"
                emotions: "${event.emotional_context}"
          # Contribute growth to dungeon_core seed
          - publish_seed_growth:
              seed_id: "${dungeon.seed_id}"
              domain: "memory_depth.capture"
              amount: "${significance * 0.5}"
              source: "dungeon-actor"
          # If master is present, contribute to master's perception growth
          - cond:
              - when: "${dungeon.has_master} AND ${master.seed.capability.perception.emotional}"
                then:
                  - publish_seed_growth:
                      seed_id: "${master.seed_id}"
                      domain: "perception.emotional"
                      amount: "${significance * 0.1}"
                      source: "dungeon-actor"

  - cond:
      - when: "${significance} >= 0.8"
        then:
          - queue_manifestation:
              memory_id: "${stored_memory.id}"
              priority: "${significance}"
              # Manifestation only proceeds if dungeon_core seed manifest_memory is unlocked
              precondition: "${seed.capability.manifest_memory}"
```

### Dungeon Personality from Formation

The dungeon's personality (from its formative event) is stored in the dungeon_core seed's metadata, since it's a permanent characteristic:

```yaml
# Stored in dungeon_core seed metadata at creation time
personality_type: "martial" | "memorial" | "festive" | "scholarly"

personality_effects:
  martial_dungeon:
    memory_preference: ["combat", "valor", "skill_display"]
    manifestation_style: "epic_battle_scenes"
    spawn_preference: ["warrior_types", "predators"]
    trap_style: "direct_combat"
    # Seed growth bonus: genetic_library.* domains grow 20% faster

  memorial_dungeon:
    memory_preference: ["death", "grief", "sacrifice"]
    manifestation_style: "somber_portraits"
    spawn_preference: ["undead", "spirits", "guardians"]
    trap_style: "environmental_hazards"
    # Seed growth bonus: memory_depth.* domains grow 20% faster

  festive_dungeon:
    memory_preference: ["joy", "celebration", "togetherness"]
    manifestation_style: "bright_murals"
    spawn_preference: ["abundant_creatures", "treasure_guardians"]
    trap_style: "puzzles_and_mazes"
    # Seed growth bonus: mana_reserves.* domains grow 20% faster

  scholarly_dungeon:
    memory_preference: ["discovery", "learning", "puzzle_solving"]
    manifestation_style: "diagrams_and_books"
    spawn_preference: ["constructs", "magical_creatures"]
    trap_style: "logic_puzzles"
    # Seed growth bonus: trap_complexity.* domains grow 20% faster
```

### Dungeon Phase and Personality Expression

The dungeon_core seed growth phase influences how strongly personality manifests:

| Phase | Behavior | Personality Expression |
|-------|----------|----------------------|
| **Dormant** (0.0) | Reactive only -- responds to intrusions with basic instinct | Personality barely visible; all dungeons feel similar |
| **Stirring** (10.0) | Proactive spawning and basic trap usage | Personality preferences begin to emerge in spawn/trap choices |
| **Awakened** (50.0) | Layout manipulation, complex tactics, memory capture | Strong personality expression in manifestation style and strategy |
| **Ancient** (200.0) | Memory manifestation, event coordination, full master synergy | Full personality -- dungeon feels like a unique living entity |

---

## Part 8: Implementation Roadmap

### Phase 0: Seed Foundation (Prerequisite)

This phase requires **lib-seed to be implemented first** (see [SEED-AND-GARDENER.md](SEED-AND-GARDENER.md)).

1. **Register `dungeon_core` Seed Type**
   - Define growth phases (Dormant/Stirring/Awakened/Ancient)
   - Define capability rules mapping growth domains to dungeon actions
   - Configure BondCardinality: 0 (Contract handles bonds, not seed bond system)
   - Configure AllowedOwnerTypes: ["actor"]

2. **Register `dungeon_master` Seed Type**
   - Define growth phases (Bonded/Attuned/Symbiotic/Transcendent)
   - Define capability rules for perception, command, channeling, coordination
   - Configure BondCardinality: 0
   - Configure AllowedOwnerTypes: ["character", "actor"]
   - Configure MaxPerOwner: 1 (one mastery bond at a time)

3. **Create Contract Template**
   - Dungeon-master bond contract with Priest/Paladin/Corrupted variants
   - Prebound API for dungeon_master seed creation on contract formation
   - Milestone definitions linked to master seed phase transitions

### Phase 1: Core Infrastructure (Actor + Seed Integration)

4. **Create Dungeon Actor Template**
   - Define `dungeon_core` actor category with `event_brain` type
   - Implement `DungeonActorState` (volatile state only)
   - Create basic ABML behavior document with seed capability checks
   - Actor creation also creates a `dungeon_core` seed (SeedId stored in actor state)

5. **Implement Seed-Backed Variable Providers**
   - `DungeonCoreSeedVariableProviderFactory` -- exposes dungeon_core seed data
   - `DungeonActorVariableProviderFactory` -- exposes volatile actor state
   - Cache seed data with appropriate TTL (revalidate on `seed.capability.updated`)

6. **Create Dungeon Event Subscriptions**
   - Subscribe to domain-relevant events
   - Implement perception queue processing
   - On significant events, publish `seed.growth.contributed` to appropriate domains

### Phase 2: Dungeon Master Bond (Contract + Master Seed)

7. **Implement Bond Formation**
   - Contract creation flow (character or monster)
   - Prebound API triggers `dungeon_master` seed creation
   - Actor state update with bond reference
   - Bond event publication

8. **Implement Bond Communication**
   - `DungeonMasterSeedVariableProviderFactory` -- active when bonded
   - `DungeonMasterCharacterVariableProviderFactory` -- character data
   - Communication fidelity gated by master's perception/command capabilities
   - Growth contributions to both seeds on cooperative actions

9. **Implement Monster Corruption**
   - Corruption process
   - Contract with "corrupted" bond type
   - Monster receives `dungeon_master` seed with limited agency

### Phase 3: Capabilities (Seed-Gated Actions)

10. **Implement Spawn Capabilities**
    - Monster spawning with quality tiers gated by dungeon_core seed capabilities
    - Growth contributions to `genetic_library.*` domains on spawn/absorption
    - Master's `command.spawning` grows when directing spawns

11. **Implement Trap Capabilities**
    - Trap activation gated by `activate_trap.*` dungeon_core seed capabilities
    - Growth contributions on successful trap triggers
    - Master's `command.traps` grows when directing trap activation

12. **Implement Event Coordinators**
    - Defense coordinator (gated by `spawn_event_agent` capability)
    - Master can direct coordinators when `coordination.encounters` is unlocked

### Phase 4: Memory System (Emergence)

13. **Implement Memory Capture with Seed Growth**
    - Significance scoring
    - Memory storage in actor state
    - Growth to dungeon_core `memory_depth.capture` and master `perception.emotional`

14. **Implement Memory Manifestation**
    - Gated by `manifest_memory` dungeon_core seed capability
    - Manifestation quality scales with capability fidelity
    - Master's `coordination.manifestation` grows when guiding choices

15. **Dungeon Personality Effects**
    - Personality stored in dungeon_core seed metadata
    - Personality-based growth bonuses (20% faster in thematic domains)

---

## Part 9: Design Considerations

### Multi-Instance Safety

Dungeons follow the same patterns as other actors, with seeds providing durable state:

- **Actor state**: One dungeon actor per dungeon instance, Redis-backed, ETag-based concurrency
- **dungeon_core seed**: MySQL-backed (durable, queryable), updated via growth events
- **dungeon_master seed**: MySQL-backed, updated via growth events, one per bonded entity
- **Capability manifests**: Redis-cached in lib-seed, recomputed on growth threshold crossings
- **Contract**: MySQL-backed, managed by lib-contract
- Pool distribution for horizontal scaling (same as NPC brains)

### Scaling Considerations

For 1000 active dungeons:
- 1000 dungeon actors across pool nodes
- 1000 `dungeon_core` seeds in lib-seed (MySQL, rarely queried in bulk)
- ~800 `dungeon_master` seeds (not all dungeons have masters at all times)
- ~1800 cached capability manifests in Redis (frequently read, rarely recomputed)
- Each actor subscribes to ~10 event topics (fanout)
- ~10 events/second per dungeon = 10,000 events/second total
- Seed growth contributions debounced (configurable, default 5000ms)
- Horizontal scaling via pool nodes (same as NPC brains)

### Emergent Gameplay from the Seed Model

The two-seed architecture enables gameplay that wouldn't be possible with a monolithic design:

- **Experienced masters are valuable**: A character with an archived `dungeon_master` seed from a previous bond has proven mastery. New dungeons can sense this -- the archived seed is evidence of prior experience. The character starts any new bond at higher capability because their `dungeon_master` seed retains its growth.
- **Surviving monster masters**: A monster that served as dungeon master and survives the dungeon's destruction retains its `dungeon_master` seed -- it's now an unusually capable monster with dungeon-mastery experience, a natural story hook or potential quest boss.
- **Asymmetric growth tension**: The dungeon can outgrow its master (Ancient dungeon with Bonded master -- powerful core, clumsy partner). Or the master can outgrow the dungeon (Transcendent master of a Stirring dungeon -- expert manager of a weak core). Both create interesting gameplay dynamics and story opportunities.
- **Guardian seed cross-pollination**: A player whose character serves as dungeon master accumulates combat, strategy, and perception experience that feeds their guardian seed's growth domains. The dungeon mastery experience enriches the player's overall spirit progression.

### Testing Strategy

1. **Unit Tests**: Dungeon actor state transitions, capability-gated intention formation, memory significance scoring
2. **Unit Tests (Seed)**: `dungeon_core` and `dungeon_master` seed type registration, growth domain accumulation, capability manifest computation
3. **Integration Tests**: Contract-driven bond formation with prebound seed creation, growth contribution pipeline, master seed phase → contract milestone transitions
4. **Edge Tests**: Full dungeon lifecycle via WebSocket -- creation, growth, bonding, capability unlock, manifestation

---

## Part 10: Open Questions

### Resolved by Seed + Contract Architecture

- [x] **Dungeon dormancy**: dungeon_core seed status handles Active → Dormant. Actor can be resumed from dormant seed state. Master seed can be independently Dormant (master left) while core seed remains Active.
- [x] **Dungeon master UI**: The master's `dungeon_master` seed capability manifest drives what they can perceive and command. Early bonds (Bonded phase) show crude impressions. Mature bonds (Transcendent phase) show rich tactical information. Same pattern as guardian spirit UX manifest.
- [x] **Progressive capability gating**: Seed capability rules with configurable formulas gate all dungeon actions (dungeon_core seed) and all master abilities (dungeon_master seed).
- [x] **Bond strength tracking**: Not a single float -- it's the aggregate of the master's `dungeon_master` seed growth domains. Richer representation than a scalar value.

### Still Open

- [ ] Should dungeons have their own currency wallet (Currency service), or use `current_mana` as a virtual resource in actor state? The dungeon_core seed tracks `mana_reserves` growth (long-term capacity), but the volatile balance needs a home. Currency wallet would enable NPC economic participation; virtual resource is simpler.
- [ ] How do aberrant dungeons (cross-realm gates) affect the actor and seed model? Does the seed's GameServiceId constrain this, or can dungeon seeds exist across game services?
- [ ] Can multiple dungeon cores exist in a mega-dungeon, and how do their actors/seeds coordinate? Each core has its own `dungeon_core` seed, but they'd need some coordination mechanism. Shared territory Contracts?
- [ ] How do officially-sanctioned dungeons interact with political/economic systems? The dungeon_core seed's growth phases (Dormant → Ancient) could map to political recognition tiers.
- [ ] Should the `dungeon_master` seed contribute growth to the player's `guardian` seed? If a player character is a dungeon master, the combat/strategy experience should plausibly feed the guardian seed's domains. Recommendation: configurable multiplier per seed type pair, defaulting to 0.0, enabled for dungeon_master → guardian as a specific configuration.
- [ ] What is the growth contribution model for personality-biased domains? A flat 20% bonus, a configurable multiplier in seed metadata, or a separate personality growth formula?
- [ ] Should the `dungeon_master` seed be archived or deleted when a bond dissolves? Archival preserves experience for future bonds (gameplay-rich); deletion means every new bond starts fresh (simpler but less interesting).

---

## Conclusion

Implementing dungeons as actors **backed by two seed types** transforms them from passive content containers into **living, conscious entities with progressive capabilities and meaningful partnerships**:

1. **Perceive** their domain through event subscriptions (Actor)
2. **Think** using a cognition pipeline with capability-gated intentions (Actor + dungeon_core seed manifest)
3. **Grow** through accumulated experience across growth domains (dungeon_core seed)
4. **Unlock** progressively richer capabilities as growth crosses thresholds (dungeon_core seed)
5. **Act** through seed-gated capabilities -- spawning, traps, manifestations (Actor + dungeon_core seed)
6. **Bond** with characters or monsters via Contracts that create `dungeon_master` seeds (Contract + dungeon_master seed)
7. **Deepen partnerships** as the master grows in their role, unlocking richer communication and coordination (dungeon_master seed)
8. **Remember** through the memory accumulation system, contributing to both seeds' growth (Actor → seeds)

The asymmetric bond pattern -- player↔character, character↔dungeon -- is the same structural relationship at two layers, connected by Contracts and tracked by role-specific seeds. Seeds track growth in roles. Contracts formalize terms between entities. Actors provide autonomous behavior. Each service does what it's best at.

---

*This document is part of the Bannou planning documentation. It depends on [SEED-AND-GARDENER.md](SEED-AND-GARDENER.md) for the Seed service design.*
