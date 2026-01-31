# Dungeon as Actor: Living Dungeons in the Actor System

> **Status**: Proposal
> **Priority**: Medium-High
> **Related**: `docs/planning/REGIONAL_WATCHERS_BEHAVIOR.md`, `docs/planning/ACTOR_DATA_ACCESS_PATTERNS.md`, `docs/guides/ACTOR_SYSTEM.md`
> **External**: `arcadia-kb/04 - Game Systems/Dungeon System.md`, `arcadia-kb/04 - Game Systems/Dungeon System - Nageki-Inspired Enhancements.md`

## Executive Summary

This document proposes implementing **dungeons as a new actor category** within Bannou's actor system. Rather than being passive containers for content, dungeons become autonomous entities that:

- **Perceive** intrusions, combat, deaths, and resource extraction within their domain
- **Cognize** threats, opportunities, and memorable events through a simplified cognition pipeline
- **Act** by spawning monsters, activating traps, managing resources, and coordinating defenses
- **Bond** with a character (player or NPC) or monster to serve as their avatar - the "dungeon master"

This synergizes with the existing Regional Watcher (god) pattern while adding a unique **bonded partnership** mechanic where the dungeon actor and a character agent work in symbiosis.

---

## Part 1: Why Dungeons as Actors

### Current Architecture Gap

The dungeon system described in arcadia-kb defines dungeons as living, conscious entities with:
- A **dungeon core** (newborn spirit that serves as the seed)
- A **dungeon master** (creature that bonds with the core, gains power, defends it)
- **Monsters** as mana generators (pneuma echoes, not true creatures)
- **Domain expansion** through extension cores
- **Memory accumulation** from significant events

However, there's no technical implementation for the dungeon's **autonomous behavior**. The actor system provides the perfect substrate:

| Dungeon Concept | Actor System Mapping |
|-----------------|---------------------|
| Dungeon core consciousness | Dungeon Actor (Event Brain) |
| Dungeon perception (sensing intrusions) | Event subscriptions + Perception queue |
| Dungeon decision-making | ABML behavior document + Cognition pipeline |
| Dungeon actions (spawn monsters, activate traps) | Capabilities + Service calls |
| Dungeon master bond | Actor-to-Actor partnership (new pattern) |
| Memory accumulation | Actor state + Memory store |

### The God Pattern Parallel

Regional Watchers (gods) demonstrate that long-running Event Actors can:
- Subscribe to domain-specific events
- Make intelligent decisions about when to act
- Spawn Event Agents for specific situations
- Execute game server APIs

Dungeons follow the same pattern but with a key addition: **a bonded character partner**.

---

## Part 2: Dungeon Actor Architecture

### Actor Type Definition

```yaml
# Actor template for a dungeon core
metadata:
  id: dungeon-core-template
  type: event_brain
  category: dungeon_core
  description: "Living dungeon consciousness that manages domain, inhabitants, and defenses"

config:
  domain: "dungeon"

  # Cognition template: simplified creature-like processing
  cognition_template: "creature_base"

  # Dungeon-specific configuration
  dungeon:
    core_location: "${dungeon.core_room_id}"
    domain_radius: 500  # meters from core
    extension_cores: []  # additional influence bubbles

  # Event subscriptions
  subscriptions:
    - "dungeon.${dungeon_id}.intrusion"      # Someone entered
    - "dungeon.${dungeon_id}.combat.*"       # Combat within domain
    - "dungeon.${dungeon_id}.death.*"        # Deaths within domain
    - "dungeon.${dungeon_id}.loot.*"         # Treasure taken
    - "dungeon.${dungeon_id}.trap.*"         # Trap triggered/disarmed
    - "dungeon.${dungeon_id}.structure.*"    # Structural damage
    - "dungeon.${dungeon_id}.inhabitant.*"   # Monster spawned/killed

  # Capabilities
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
```

### Dungeon State Structure

```csharp
public class DungeonActorState
{
    // Core vitals
    public float CoreIntegrity { get; set; }        // 0-1, health of core
    public float ManaReserves { get; set; }         // Current mana pool
    public float ManaGenerationRate { get; set; }   // Per-tick generation

    // Domain
    public Guid CoreRoomId { get; set; }
    public List<Guid> ExtensionCoreIds { get; set; }
    public float DomainRadius { get; set; }

    // Population
    public Dictionary<string, int> InhabitantCounts { get; set; }  // By species
    public Dictionary<string, float> GeneticLibrary { get; set; }  // Logos completion %
    public int SoulSlots { get; set; }              // Max souls for intelligent monsters
    public int SoulSlotsUsed { get; set; }

    // Defenses
    public Dictionary<Guid, TrapState> Traps { get; set; }
    public Dictionary<Guid, float> RoomHazardLevels { get; set; }

    // Bonding
    public Guid? BondedMasterId { get; set; }       // Character ID of dungeon master
    public float BondStrength { get; set; }         // 0-1, strength of bond
    public BondType BondType { get; set; }          // Priest, Paladin, or Corrupted

    // Feelings (creature-level emotions)
    public Dictionary<string, float> Feelings { get; set; }  // hunger, threat, satisfaction

    // Memories (accumulated from events)
    public List<DungeonMemory> Memories { get; set; }
}

public class DungeonMemory
{
    public Guid EventId { get; set; }
    public string EventType { get; set; }           // "epic_battle", "betrayal", "first_clear"
    public float Significance { get; set; }         // 0-1
    public DateTimeOffset Timestamp { get; set; }
    public Dictionary<string, object> Content { get; set; }  // Event-specific data
    public bool HasManifested { get; set; }         // Has this become a painting/item?
}
```

### Perception Processing

Dungeons use a simplified cognition pipeline (`creature_base`):

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
│   3. INTENTION FORMATION                                                │
│   ──────────────────────                                                │
│   • Evaluate threats → spawn defenders or activate traps               │
│   • Evaluate opportunities → absorb corpses, claim resources           │
│   • Evaluate memories → store significant events                       │
│   • Communicate to bonded master if present                            │
│                                                                         │
│   SKIPPED STAGES (unlike humanoid_base):                               │
│   • No significance assessment (dungeon cares about everything)        │
│   • No goal impact evaluation (goals are fixed: survive, grow)         │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Part 3: The Dungeon Master Bond

### Bond Formation

The bond between dungeon core and dungeon master is a **contract-based relationship** managed through the Contract service:

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
    entity_type: character
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

milestones:
  - name: "initial_bond"
    description: "Core and master successfully bond"

  - name: "bond_strengthening"
    description: "Bond strength increases through cooperation"
    criteria: "bond_strength >= 0.5"

  - name: "full_symbiosis"
    description: "Complete trust and power sharing"
    criteria: "bond_strength >= 0.9"

enforcement: consequence_based
```

### Bond Types

| Bond Type | Dungeon Master Role | Core-Master Relationship | Death Behavior |
|-----------|--------------------|-----------------------|----------------|
| **Priest** | Non-combatant, focuses on creation and management | Core provides mana, master provides direction | Bond progress preserved through death |
| **Paladin** | Combatant, receives direct combat power from core | Core channels combat abilities through master | Bond progress resets on master death |
| **Corrupted** | Forced bond (monster transformed or possessed) | Core dominates, master is avatar | Core dies if avatar destroyed |

### Actor Partnership Pattern

This introduces a new actor pattern: **Actor-to-Actor Partnership**.

```csharp
/// <summary>
/// Manages the bidirectional relationship between dungeon core and dungeon master actors.
/// </summary>
public interface IDungeonBondManager
{
    /// <summary>
    /// Attempt to form a bond between a dungeon core and a character.
    /// </summary>
    Task<BondResult> AttemptBondAsync(
        Guid dungeonActorId,
        Guid characterId,
        BondType bondType,
        CancellationToken ct);

    /// <summary>
    /// Send a perception from dungeon to master (shared awareness).
    /// </summary>
    Task SendPerceptionToMasterAsync(
        Guid dungeonActorId,
        Perception perception,
        CancellationToken ct);

    /// <summary>
    /// Receive a command from master to dungeon (direction).
    /// </summary>
    Task<CommandResult> ReceiveCommandFromMasterAsync(
        Guid dungeonActorId,
        DungeonCommand command,
        CancellationToken ct);

    /// <summary>
    /// Channel mana/power between core and master.
    /// </summary>
    Task ChannelPowerAsync(
        Guid dungeonActorId,
        Guid masterId,
        float amount,
        PowerFlowDirection direction,
        CancellationToken ct);
}
```

### Communication Flow

```
┌──────────────────────────────────────────────────────────────────────┐
│                    DUNGEON-MASTER COMMUNICATION                       │
│                                                                       │
│   DUNGEON CORE ACTOR                    DUNGEON MASTER (Character)   │
│   ──────────────────                    ──────────────────────────   │
│                                                                       │
│   Perceives: Intrusion detected         Receives: "I sense intruders"│
│        ↓                                      ↓                       │
│   Evaluates: 4 adventurers, level 6     Knows: Party composition     │
│        ↓                                      ↓                       │
│   Options:                              Decides: "Send wolves first" │
│   - Spawn defenders                           ↓                       │
│   - Activate traps                      Commands: spawn_monster       │
│   - Seal passages                             ↓                       │
│        ↓                                                              │
│   Receives command from master          Observes: Wolves engaging    │
│        ↓                                      ↓                       │
│   Executes: Spawn wolf pack             Adjusts: "Seal west passage" │
│        ↓                                      ↓                       │
│   Combat unfolds...                     Combat unfolds...            │
│        ↓                                      ↓                       │
│   Perceives: Wolves killed              Feels: Core's pain/loss      │
│   Absorbs: Logos from adventurer death  Gains: Power from absorption │
│   Stores: Memory of this battle         Shares: Memory of battle     │
│                                                                       │
└──────────────────────────────────────────────────────────────────────┘
```

---

## Part 4: Dungeon Capabilities

### Monster Spawning

```yaml
capability: spawn_monster
description: "Create a pneuma echo from the dungeon's genetic library"

parameters:
  species_id: string          # From genetic library
  room_id: Guid               # Where to spawn
  quality_level: float        # 0-1, logos investment
  count: int                  # Number to spawn

preconditions:
  - "${dungeon.mana_reserves} >= ${spawn_cost}"
  - "${dungeon.genetic_library[species_id]} > 0"
  - "${room_id} in ${dungeon.domain}"

effects:
  - deduct: "${spawn_cost} from ${dungeon.mana_reserves}"
  - create: "pneuma_echo entities in ${room_id}"
  - increment: "${dungeon.inhabitant_counts[species_id]}"
  - emit: "dungeon.*.inhabitant.spawned"

quality_scaling:
  # From Nageki-Inspired Enhancements: logos completion affects echo quality
  minimal: { cost_multiplier: 0.5, stats_multiplier: 0.7 }
  standard: { cost_multiplier: 1.0, stats_multiplier: 1.0 }
  enhanced: { cost_multiplier: 2.0, stats_multiplier: 1.3 }
  maximum: { cost_multiplier: 5.0, stats_multiplier: 1.6 }
  alpha: { cost_multiplier: 10.0, stats_multiplier: 2.0, requires: "logos_completion >= 1.0" }
```

### Trap Activation

```yaml
capability: activate_trap
description: "Trigger a trap system in a room"

parameters:
  trap_id: Guid
  target_ids: List<Guid>      # Optional: specific targets

preconditions:
  - "${traps[trap_id].state} == 'ready'"
  - "${traps[trap_id].mana_charge} > 0"

effects:
  - set: "${traps[trap_id].state} = 'triggered'"
  - deduct: "mana from trap charge"
  - emit: "dungeon.*.trap.triggered"
  - call: "/game-server/apply-trap-effect"
```

### Memory Manifestation

```yaml
capability: manifest_memory
description: "Crystallize an accumulated memory into physical form"

parameters:
  memory_id: Guid
  manifestation_type: enum    # painting, data_crystal, memory_item, environmental
  room_id: Guid               # Where to manifest

preconditions:
  - "${memories[memory_id].significance} >= 0.5"
  - "${memories[memory_id].has_manifested} == false"
  - "${dungeon.mana_reserves} >= ${manifestation_cost}"

effects:
  - set: "${memories[memory_id].has_manifested} = true"
  - call: "/item/create-from-template" if memory_item
  - call: "/scene/add-decoration" if painting or environmental
  - emit: "dungeon.*.memory.manifested"
```

### Spawn Event Coordinator

```yaml
capability: spawn_event_agent
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

| Service | Dungeon Actor Usage |
|---------|-------------------|
| **Actor Service** | Actor lifecycle, template management, pool nodes |
| **Character Service** | Dungeon master data, adventurer info |
| **Character Encounter** | Track adventurer visits, build history |
| **Contract Service** | Dungeon-master bond management |
| **Currency Service** | Dungeon treasury (mana as currency) |
| **Inventory Service** | Trap/treasure container management |
| **Item Service** | Memory item creation, loot generation |
| **Mapping Service** | Domain boundaries, room connectivity |
| **Scene Service** | Memory paintings, environmental decorations |
| **Analytics Service** | Event significance scoring for memory capture |

### Variable Providers (New)

```csharp
// DungeonVariableProvider - exposes dungeon state to ABML
services.AddSingleton<IVariableProvider, DungeonVariableProvider>();

// Provides:
// ${dungeon.core_integrity}
// ${dungeon.mana_reserves}
// ${dungeon.mana_generation_rate}
// ${dungeon.inhabitant_count}
// ${dungeon.genetic_library.*}
// ${dungeon.traps.*}
// ${dungeon.bond_strength}
// ${dungeon.master_id}
```

```csharp
// DungeonMasterVariableProvider - exposes master data to dungeon ABML
services.AddSingleton<IVariableProvider, DungeonMasterVariableProvider>();

// Provides:
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
  payload: { core_integrity, mana_reserves, threat_level }

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
  payload: { master_id, bond_type }

# Dungeon subscribes
character.{character_id}.entered_dungeon:
  description: "Character crossed dungeon boundary"

character.{character_id}.exited_dungeon:
  description: "Character left dungeon"

combat.{dungeon_id}.*:
  description: "Combat events within dungeon domain"
```

---

## Part 6: Spawning and Corruption Mechanics

### Monster as Dungeon Master

When no suitable character is available, the dungeon can **spawn or corrupt a monster** to serve as its avatar:

```yaml
flow: attempt_monster_bond
description: "Dungeon creates or corrupts a monster to serve as master"

steps:
  # Option 1: Spawn a new high-quality monster
  - cond:
      - when: "${dungeon.mana_reserves} >= ${avatar_spawn_cost}"
        then:
          - spawn_monster:
              species_id: "${dungeon.strongest_species}"
              quality_level: 1.0  # Maximum quality
              room_id: "${dungeon.core_room_id}"
              designate_as: "avatar_candidate"
          - form_bond:
              target: "${avatar_candidate.id}"
              bond_type: "corrupted"  # Monster bond is always corrupted type

  # Option 2: Corrupt existing powerful monster
  - cond:
      - when: "${dungeon.inhabitants.any(x => x.level >= 5)}"
        then:
          - select: "${dungeon.inhabitants.max_by(level)}"
          - corrupt:
              target_id: "${selected.id}"
              corruption_intensity: 0.8
          - form_bond:
              target: "${selected.id}"
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
}
```

### Contract with NPCs/Players

For character dungeon masters (not corrupted monsters), the bond uses the Contract service:

```yaml
# Player discovers dungeon core, chooses to bond
event: core_discovery
  - core offers bond (displays contract terms)
  - player reviews: power gains, restrictions, death clause
  - player accepts or rejects
  - if accepted: Contract.create(dungeon-master-bond, parties=[core, player])
  - bond_formed event triggers
  - player character gains dungeon master abilities
  - player's NPC brain actor now receives dungeon perceptions
```

---

## Part 7: Memory and Dungeon Personality

### Memory Capture Integration

The dungeon actor integrates with the **Memory Accumulation System** from arcadia-kb:

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

  - cond:
      - when: "${significance} >= 0.8"
        then:
          - queue_manifestation:
              memory_id: "${stored_memory.id}"
              priority: "${significance}"
```

### Dungeon Personality from Formation

The dungeon's personality (from its formative event) affects:

```yaml
personality_effects:
  martial_dungeon:
    memory_preference: ["combat", "valor", "skill_display"]
    manifestation_style: "epic_battle_scenes"
    spawn_preference: ["warrior_types", "predators"]
    trap_style: "direct_combat"

  memorial_dungeon:
    memory_preference: ["death", "grief", "sacrifice"]
    manifestation_style: "somber_portraits"
    spawn_preference: ["undead", "spirits", "guardians"]
    trap_style: "environmental_hazards"

  festive_dungeon:
    memory_preference: ["joy", "celebration", "togetherness"]
    manifestation_style: "bright_murals"
    spawn_preference: ["abundant_creatures", "treasure_guardians"]
    trap_style: "puzzles_and_mazes"

  scholarly_dungeon:
    memory_preference: ["discovery", "learning", "puzzle_solving"]
    manifestation_style: "diagrams_and_books"
    spawn_preference: ["constructs", "magical_creatures"]
    trap_style: "logic_puzzles"
```

---

## Part 8: Implementation Roadmap

### Phase 1: Core Infrastructure (Foundation)

1. **Create Dungeon Actor Template**
   - Define `dungeon_core` actor category
   - Implement `DungeonActorState` class
   - Create basic ABML behavior document

2. **Implement DungeonVariableProvider**
   - Expose dungeon state to ABML
   - Cache with appropriate TTL

3. **Create Dungeon Event Subscriptions**
   - Subscribe to domain-relevant events
   - Implement perception queue processing

### Phase 2: Dungeon Master Bond (Partnership)

4. **Implement IDungeonBondManager**
   - Bond formation logic
   - Power channeling
   - Communication flow

5. **Create Contract Template**
   - Dungeon-master bond contract
   - Priest/Paladin/Corrupted variants

6. **Implement Monster Corruption**
   - Corruption process
   - Avatar spawning

### Phase 3: Capabilities (Actions)

7. **Implement Spawn Capabilities**
   - Monster spawning with quality scaling
   - Integration with genetic library

8. **Implement Trap Capabilities**
   - Trap activation
   - Trap recharging

9. **Implement Event Coordinators**
   - Defense coordinator
   - Ambush coordinator
   - Boss encounter coordinator

### Phase 4: Memory System (Emergence)

10. **Implement Memory Capture**
    - Significance scoring
    - Memory storage
    - Integration with Analytics service

11. **Implement Memory Manifestation**
    - Painting generation (Scene service)
    - Item generation (Item service)
    - Environmental effects

12. **Dungeon Personality Effects**
    - Formation-based personality
    - Preference modifiers

---

## Part 9: Design Considerations

### Multi-Instance Safety

Dungeons follow the same patterns as other actors:
- One dungeon actor per dungeon instance
- State persisted to actor-state store (Redis)
- ETag-based optimistic concurrency
- Pool distribution for horizontal scaling

### Scaling Considerations

For 1000 active dungeons:
- 1000 dungeon actors across pool nodes
- Each subscribes to ~10 event topics (fanout)
- ~10 events/second per dungeon = 10,000 events/second total
- Horizontal scaling via pool nodes (same as NPC brains)

### Testing Strategy

1. **Unit Tests**: Dungeon actor state transitions, capability execution
2. **Integration Tests**: Bond formation, event subscription, service calls
3. **Edge Tests**: Full dungeon lifecycle via WebSocket

---

## Part 10: Open Questions

- [ ] Should dungeons have their own currency wallet, or use mana as a virtual resource?
- [ ] How do aberrant dungeons (cross-realm gates) affect the actor model?
- [ ] Can multiple dungeon cores exist in a mega-dungeon, and how do their actors coordinate?
- [ ] How does dungeon dormancy (long periods without master) affect the actor lifecycle?
- [ ] Should dungeon masters have a special UI for directing their dungeon, or use natural language?
- [ ] How do officially-sanctioned dungeons interact with political/economic systems?

---

## Conclusion

Implementing dungeons as actors transforms them from passive content containers into **living, conscious entities** that:

1. **Perceive** their domain through event subscriptions
2. **Think** using a cognition pipeline (creature-level)
3. **Act** through capabilities (spawning, traps, manifestations)
4. **Partner** with characters through the bond system
5. **Remember** through the memory accumulation system

This creates emergent gameplay where each dungeon develops a unique personality, history, and relationship with its master - making dungeons feel truly alive in Arcadia's world.

---

*This document is part of the Bannou planning documentation.*
