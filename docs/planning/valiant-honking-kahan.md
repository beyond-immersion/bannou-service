# Behavior Types Specification Planning

> **Status**: ✅ ARCHITECTURAL DECISIONS COMPLETE - Ready for Implementation
> **Created**: 2026-01-06
> **Updated**: 2026-01-08 (All Sections A-G Resolved)
> **Related**: [THE_DREAM.md](./THE_DREAM.md), [ABML Guide](../guides/ABML.md), [ACTORS_PLUGIN_V3](./UPCOMING_-_ACTORS_PLUGIN_V3.md), [MONSTER_ARENA](./CASE_STUDY_-_MONSTER_ARENA.md)

## Executive Summary

This document catalogs all behavior types needed for the Bannou behavior system, synthesized from THE DREAM vision, gap analysis, ABML/GOAP documentation, and current implementation. The goal is to comprehensively identify behavior types before specifying their shapes.

**Critical Insight (2026-01-07)**: The original "fixed 4 channels vs dynamic channels" question conflated two distinct architectural concepts. This revision separates them and identifies the missing architectural layers.

---

## Part 1: Channel Architecture Clarification

### 1.1 The Two Channel Concepts

The original planning conflated two fundamentally different "channel" concepts:

| Concept | Where It Lives | What It Does | Lifetime | Example |
|---------|----------------|--------------|----------|---------|
| **ABML Channels** | `channels:` in YAML | Parallel execution tracks for choreography | Per-document (authoring-time) | camera, hero, villain, audio |
| **Intent Channels** | BehaviorModelInterpreter output slots | Merged behavior outputs for character control | Per-entity-archetype (runtime) | Combat, Movement, Interaction, Idle |

**ABML Channels** are how cutscenes/timelines organize parallel action streams. They're defined at authoring time and determine what can happen concurrently within a single ABML document.

**Intent Channels** are how the behavior runtime organizes entity outputs. They're the slots where compiled behaviors emit their results, which the Intent Merger combines based on urgency.

These concepts interact but are **not the same thing**.

### 1.2 The Missing Architectural Layers

The original plan assumed a direct path from behavior to output. Analysis reveals **four distinct layers**:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        BEHAVIOR EXECUTION LAYERS                             │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  Layer 1: ABML EXECUTION                                                     │
│  ┌─────────────────────────────────────────────────────────────────────────┐│
│  │  ABML Documents with channels: { camera: [...], hero: [...] }           ││
│  │  • Parallel track execution                                              ││
│  │  • Sync points (emit/wait_for)                                          ││
│  │  • Continuation points for streaming                                     ││
│  │  • Output: Handler invocations (abstract actions)                        ││
│  └─────────────────────────────────────────────────────────────────────────┘│
│                                    │                                         │
│                                    ▼                                         │
│  Layer 2: HANDLER MAPPING                                                    │
│  ┌─────────────────────────────────────────────────────────────────────────┐│
│  │  Translates abstract ABML actions → Entity-specific Intent outputs       ││
│  │  • "walk_to: { mark: center }" → Movement channel output                 ││
│  │  • "attack: { type: heavy }" → Combat channel output                     ││
│  │  • Entity-archetype aware (humanoid vs vehicle vs creature)              ││
│  │  • Output: Intent Channel emissions                                       ││
│  └─────────────────────────────────────────────────────────────────────────┘│
│                                    │                                         │
│                                    ▼                                         │
│  Layer 3: CONTROL GATING                                                     │
│  ┌─────────────────────────────────────────────────────────────────────────┐│
│  │  Determines what sources can write to Intent Channels                    ││
│  │  • Normal: Behavior stack outputs                                        ││
│  │  • Cinematic: Cutscene overrides, behavior stack masked                  ││
│  │  • Player: Player input layer (when allowed)                             ││
│  │  • Output: Gated Intent values per channel                               ││
│  └─────────────────────────────────────────────────────────────────────────┘│
│                                    │                                         │
│                                    ▼                                         │
│  Layer 4: INTENT MERGING                                                     │
│  ┌─────────────────────────────────────────────────────────────────────────┐│
│  │  Combines multiple behavior outputs per channel                          ││
│  │  • Urgency-based conflict resolution                                     ││
│  │  • Multiple behaviors can output to same slot                            ││
│  │  • Output: Final Intent values → Animation/Physics                       ││
│  └─────────────────────────────────────────────────────────────────────────┘│
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

**What was missing**: Layer 2 (Handler Mapping) and Layer 3 (Control Gating) were not explicitly designed.

### 1.3 Decisions Implicitly Made by THE DREAM

Analysis of THE DREAM and related documents reveals these architectural decisions are already locked in:

#### Decision 1: Behavior Stack Never Stops

From THE DREAM §2.3 (Character Agent Co-Pilot):
> "the agent doesn't stop - it watches, computes, and waits"
> "When the Event Brain presents a QTE to a player, the character agent has already computed its answer"

**Implication**: During cinematics, the behavior stack continues evaluating to compute QTE defaults. Intent Channel outputs are **gated/masked**, not suspended.

**Status**: ✅ CONFIRMED - architecture must support continuous evaluation with output gating.

#### Decision 2: Streaming Composition is Additive

From THE_DREAM_GAP_ANALYSIS §2.3:
> "Extensions are **additive** - don't modify what's already executing"

**Implication**: ABML channel structure is fixed at authoring time. Extensions add flows, not channels. No runtime channel creation.

**Status**: ✅ CONFIRMED - ABML channels are static per document.

#### Decision 3: Temporal Separation, Not Channel Separation

From THE DREAM §4.1-4.2 (Three-Version System):
> "Participants 'earn' slow-mo time by fast-forwarding through setup"

**Implication**: Spectators vs participants differ by **time position**, not by using different channels. All entities use the same channel model.

**Status**: ✅ CONFIRMED - channel model is universal; temporal desync is handled by game engine.

#### Decision 4: Entity Archetypes Have Different Needs

Implicit in MONSTER_ARENA and THE DREAM: vehicles, creatures, environmental objects exist alongside humanoids. The 4-channel humanoid model (Combat, Movement, Interaction, Idle) isn't universal.

**Implication**: Intent Channel configuration is per-entity-archetype, not global.

**Status**: ⚠️ NEEDS DESIGN - what are the archetype categories and their channel configurations?

### 1.4 The Reframed Question

The original question "fixed 4 channels vs dynamic channels" should be reframed as:

**"What's the relationship between ABML execution (channels) and behavior output (Intent Channels), and how do cinematics override the latter while the former keeps evaluating?"**

This decomposes into:
1. **Handler Mapping**: How do ABML actions translate to Intent Channel outputs?
2. **Control Gating**: How do cinematics suppress normal behavior while allowing continued evaluation?
3. **Archetype Configuration**: What Intent Channels exist for each entity type?
4. **Player Integration**: How does player input interact with the control layer?

---

## Part 2: Current State Analysis

### 2.1 What's Already Built

| Component | Status | Tests | Notes |
|-----------|--------|-------|-------|
| **ABML 2.0 Parser** | ✅ Complete | 585+ | YAML DSL with expression VM |
| **GOAP Planner** | ✅ Complete | Full | A* search with urgency parameters |
| **Bytecode Compiler** | ✅ Complete | 226 | Client-side local runtime |
| **Cognition Pipeline** | ✅ Complete | All handlers | 5-stage perception→intention flow |
| **Intent Merger** | ✅ Complete | 79 | Urgency-based multi-model merge |
| **Streaming Composition** | ✅ Complete | 13 | Continuation points and extensions |
| **CinematicInterpreter** | ✅ Complete | 13 | Pause/resume at continuation points |

### 2.2 What's Not Built Yet

| Component | Priority | Blocking |
|-----------|----------|----------|
| **Handler Mapping Layer** | Critical | Yes - connects ABML to entities |
| **Control Gating Layer** | Critical | Yes - enables cinematic override |
| **Entity Archetype System** | High | For non-humanoid entities |
| **Event Brain Actor Type** | High | For orchestration |
| **Client-side Stride Integration** | High | For actual gameplay |
| **Model Distribution Service** | Medium | Via lib-asset |
| **Dedicated Memory Service** | Medium | For embeddings-based memory |

---

## Part 3: Behavior Type Taxonomy

### 3.1 ABML Document Types (Schema-Level)

These are the `metadata.type` values in ABML documents:

| Type | Description | Execution Context | Key Capabilities |
|------|-------------|-------------------|------------------|
| **behavior** | NPC autonomous decision-making | Cloud + Client | GOAP, triggers, reactive patterns |
| **cutscene** | Choreographed multi-character sequences | Cloud + Client | Multi-channel, sync points, continuation |
| **dialogue** | Branching conversations | Cloud | Player choices, state tracking |
| **cognition** | Perception processing pipelines | Cloud | 5-stage flow, memory, significance |
| **timeline** | Time-based parallel sequences | Client | Pure choreography |
| **dialplan** | Call routing/IVR flows | Cloud (Voice) | Sequential routing, DTMF |

### 3.2 Execution Layers

| Layer | Latency | Location | Purpose | Executes |
|-------|---------|----------|---------|----------|
| **Event Brain** | 100-500ms | Cloud | Cinematic orchestration | Cutscenes, opportunity detection |
| **Character Agent** | 50-200ms | Cloud | Tactical decisions | GOAP, dialogue, cognition |
| **Local Runtime** | <1ms | Client | Frame-by-frame decisions | Compiled bytecode behaviors |
| **Cinematic Runtime** | Async | Client | Pause/resume cinematics | Continuation points |

### 3.3 Intent Channel Archetypes

> **DECISION (2026-01-07)**: Archetypes are **data-driven** - defined in YAML schema, entities reference by ID. This enables Event Agents to understand and modify any behavior-driven system trivially. The value isn't just behavior-driven control itself - it's that Event Agents can dynamically modify ANY of these at runtime, which is "over-engineered for single player but incredible for multiplayer/metaverse potential."

**Humanoid Archetype** (expanded 8-channel model):

| Channel | Purpose | Example Outputs |
|---------|---------|-----------------|
| **Combat** | Attack/defend/abilities | `attack`, `block`, `use_ability` |
| **Movement** | Navigation/steering | `walk_to`, `strafe`, `dodge` |
| **Interaction** | Object/NPC interaction | `pick_up`, `talk_to`, `use` |
| **Idle** | Ambient/waiting behaviors | `fidget`, `breathe`, `shift_weight` |
| **Expression** | Facial emotions | `smile`, `frown`, `surprise`, `fear` |
| **Attention** | Gaze/head tracking | `look_at`, `track_target`, `scan_area` |
| **Speech** | Vocalization/verbal | `grunt`, `shout`, `whisper`, `laugh` |
| **Stance** | Body posture/position | `crouch`, `stand_alert`, `relax`, `ready` |

**Vehicle Archetype** (behavior-driven control for mounts, cars, ships):

| Channel | Purpose | Example Outputs |
|---------|---------|-----------------|
| **Throttle** | Speed control | `accelerate`, `brake`, `coast` |
| **Steering** | Direction control | `turn_left`, `turn_right`, `hold_course` |
| **Signals** | Communication | `horn`, `lights`, `indicators` |
| **Systems** | Vehicle subsystems | `weapons_arm`, `shields_raise`, `cargo_drop` |

Use cases: Horse that resists rider based on fear/loyalty, AI convoy navigation, ship autopilot with override.

**Creature Archetype** (simpler than humanoid, for animals/monsters):

| Channel | Purpose | Example Outputs |
|---------|---------|-----------------|
| **Locomotion** | Movement style | `walk`, `run`, `fly`, `swim`, `burrow` |
| **Action** | Primary behavior | `hunt`, `flee`, `forage`, `rest`, `attack` |
| **Social** | Pack/herd coordination | `follow_leader`, `signal_danger`, `call_pack` |
| **Alert** | Awareness state | `vigilant`, `relaxed`, `alarmed` |

**Key Insight - Pack Behaviors**: One behavior can control MULTIPLE entities (wolf pack, bird flock, convoy). The behavior targets an **entity group** rather than individual entity.

**Object Archetype** (doors, switches, traps, elevators, puzzles):

| Channel | Purpose | Example Outputs |
|---------|---------|-----------------|
| **State** | Primary state machine | `open`, `closed`, `locked`, `triggered` |
| **Timing** | Temporal control | `delay`, `cycle`, `hold` |
| **Feedback** | User indication | `indicate_ready`, `warn`, `confirm` |

Use case: Elevator logic as reusable behavior pattern. Event Agents can modify trap timing, door behavior, etc.

**Environmental Archetype** (weather, lighting, ambience):

| Channel | Purpose | Example Outputs |
|---------|---------|-----------------|
| **Intensity** | Effect strength | `light`, `moderate`, `heavy`, `extreme` |
| **Type** | Effect variant | `rain`, `snow`, `fog`, `clear` |
| **Direction** | Spatial orientation | `north_wind`, `overhead`, `ambient` |
| **Mood** | Dramatic tone | `ominous`, `peaceful`, `tense`, `celebratory` |

**Key Insight**: Weather as behavior means Event Agents can change weather patterns trivially - storm during dramatic moment, clear skies for resolution.

### 3.4 Behavioral Category Stack

Priority-based behavior layering:

| Category | Priority | Description | Examples |
|----------|----------|-------------|----------|
| **base** | Lowest | Core templates | `humanoid-base`, `creature-base` |
| **cultural** | Low | Culture overlays | `european-medieval`, `eastern-fantasy` |
| **professional** | Medium | Job behaviors | `blacksmith-work`, `guard-patrol` |
| **personal** | Medium-High | Character quirks | `afraid-of-spiders`, `loves-gossip` |
| **situational** | High | Context-reactive | `combat-mode`, `negotiation-mode` |
| **ambient** | Varies | Background | `breathing`, `blinking` |

### 3.5 Agent/Orchestration Types

| Agent Type | Scope | Lifecycle | Primary Function |
|------------|-------|-----------|------------------|
| **Character Agent** | Per-character | Always-on | QTE defaults, personality |
| **Event Brain** | Regional/Scene | Spawned | Orchestrate cinematics |
| **Fight Coordinator** | Per-combat | Combat duration | Detect opportunities |
| **World Watcher** | World/Region | Persistent | Spawn event brains |
| **VIP Agent** | VIP NPCs | Always-on | High-priority detection |

---

## Part 4: Detailed Type Specifications

### 4.1 BEHAVIOR (ABML Document Type)

**Purpose**: Define NPC autonomous decision-making logic.

**Execution Contexts**:
- Cloud (Character Agent): Tree-walking for cognition, GOAP planning
- Client (Local Runtime): Compiled bytecode for frame-by-frame combat/movement

**How It's Used**:
1. GOAP planner extracts goals/actions at compile time
2. Triggered by events, conditions, or time schedules
3. Outputs emitted to Intent Channels via Handler Mapping
4. Intent Merger combines outputs from multiple active behaviors
5. Continuation points allow cloud to inject extensions

**Handler Mapping for Behaviors**:
- `emit_intent: { channel: combat, action: attack, urgency: 0.9 }` → Combat channel
- `emit_intent: { channel: movement, direction: forward, urgency: 0.7 }` → Movement channel

**Capabilities**:
- Expression evaluation (conditions, state access)
- GOAP preconditions/effects/cost annotations
- Multiple trigger types (event, condition, time)
- Flow control (cond, for_each, repeat, goto, call)
- Handler invocations for domain actions
- Error handling chain

### 4.2 CUTSCENE (ABML Document Type)

**Purpose**: Choreographed multi-character sequences with precise timing.

**Execution Contexts**:
- Cloud (Event Brain): Generates and coordinates
- Client (Cinematic Runtime): Executes with continuation points

**How It's Used**:
1. Event Brain detects dramatic opportunity, selects/generates cutscene
2. Cutscene compiled and sent to game server
3. CinematicInterpreter executes, may pause at continuation points
4. Event Brain may send extensions before continuation timeout
5. On completion, control returns to normal behavior stack

**ABML Channel → Intent Channel Mapping**:
```yaml
channels:
  camera:
    - move_to: { position: overhead }  # → Camera system (not Intent Channel)
  hero:
    - walk_to: { mark: center }        # → hero's Movement Intent Channel
    - attack: { type: heavy }          # → hero's Combat Intent Channel
  villain:
    - block: { direction: front }      # → villain's Combat Intent Channel
```

**Key Insight**: Each ABML channel that references an entity maps to THAT entity's Intent Channels. The "camera" channel is special (goes to camera system, not an entity).

**Control Gating During Cutscene**:
- Cutscene declares `takes_control_of: [hero, villain]`
- Control Gating Layer masks normal behavior output for these entities
- Behavior stack continues evaluating (for QTE defaults)
- Cutscene outputs go directly to Intent Channels (bypass merger?)

### 4.3 DIALOGUE (ABML Document Type)

**Purpose**: Branching conversations with player choices.

**Execution Context**: Cloud (Character Agent)

**How It's Used**:
1. Player approaches NPC → dialogue trigger
2. Flows present choices based on conditions
3. Player selects (or timeout → NPC's preferred choice)
4. Choices affect world state, relationships
5. Can embed mini-cutscenes or reactions

**Intent Channel Interaction**:
- During dialogue, entity Movement/Combat channels are typically idle/suppressed
- Idle channel may show "talking" animations
- Interaction channel handles the dialogue gestures

### 4.4 COGNITION (ABML Document Type)

**Purpose**: Define how NPCs process perceptions into intentions.

**Execution Context**: Cloud (Character Agent)

**How It's Used**:
1. Perception events enter pipeline
2. 5-stage flow: Filter → Memory Query → Significance → Storage → Intention
3. Significant perceptions become memories
4. Goal impacts trigger GOAP replanning
5. State updates emitted to game server

**No Direct Intent Channel Output**: Cognition affects **state variables** that behaviors read, not Intent Channels directly. The behavior stack (running on client) reads the updated state and adjusts its outputs.

### 4.5 TIMELINE (ABML Document Type)

**Purpose**: Generic parallel sequences without game semantics.

**Execution Context**: Client (Cinematic Runtime)

**How It's Used**:
- Pure choreography orchestration
- Multi-channel execution with sync points
- No dramatic context or opportunity awareness
- Technical foundation for cutscenes

**Relationship to Cutscene**: Timeline is the **substrate**; cutscene adds dramatic semantics (opportunities, extensions, QTE hooks).

---

## Part 5: Open Questions (Structured for Resolution)

### Section A: Entity Archetype System ✅ RESOLVED

> **Decisions recorded 2026-01-07** - See §3.3 for full archetype definitions.

#### A1. Humanoid Channels ✅

**Decision**: Expanded from 4 to **8 channels**:
- Combat, Movement, Interaction, Idle (original 4)
- Expression, Attention, Speech, Stance (new 4)

#### A2. Non-Humanoid Archetypes ✅

**Decision**: All four categories need behavior-driven control:
- **Vehicles**: Different channels (Throttle, Steering, Signals, Systems)
- **Creatures**: Simpler channels (Locomotion, Action, Social, Alert) + pack behaviors
- **Objects**: Simple channels (State, Timing, Feedback) - elevator as canonical example
- **Environmental**: Channels (Intensity, Type, Direction, Mood) - Event Agents can modify weather trivially

**Key Insight**: The value is Event Agent modification capability, not just behavior-driven control itself.

#### A3. Archetype Definition ✅

**Decision**: **Data-driven** - archetypes defined in YAML schema, entities reference by ID.

#### A4. Pack/Group Behaviors ✅

**Decision**: **Hierarchical Emit Model** - behavior emits to targets with hierarchical authority, recipients respond based on their own local behavior.

```yaml
# Pack alpha behavior (runs on alpha wolf, foreman, squad leader, etc.)
flows:
  coordinate_hunt:
    # Emit to everyone in group
    - emit_group: { target: all, intent: hunt, target_entity: "${rabbit.id}" }

    # Emit to role
    - emit_group: { target: role:flanker, intent: circle_left }

    # Emit to specific individual
    - emit_group: { target: member:beta_2, intent: hold_position }
```

**How it works**:
- Pack behavior doesn't distribute outputs directly
- Pack behavior EMITS to targets (all, role, or individual)
- Each recipient has their own local behavior that:
  1. Subscribes to group emits
  2. Filters based on their role/identity
  3. Responds according to their own behavior logic

**Benefits**:
- Pack behavior doesn't need to know individual capabilities
- Members can be added/removed without changing pack behavior
- Same pattern works for wolf packs, work crews, military squads, convoy vehicles
- Matches real-world authority patterns (foreman calling out to crews)

---

### Section B: Handler Mapping Layer ✅ RESOLVED

> **Decisions recorded 2026-01-07**

#### B1. Action Vocabulary ✅

**Decision**: **Layered** - Core actions shared across document types, type-specific extensions added per document type.

```yaml
# Core actions (work everywhere)
- walk_to: { target: "${pos}" }           # Basic movement
- attack: { type: heavy }                  # Basic combat
- look_at: { target: "${entity}" }         # Basic attention

# Behavior-specific extensions
- emit_intent: { channel: combat, action: attack, urgency: 0.9 }
- goap_action: { name: attack_enemy, preconditions: {...}, effects: {...} }

# Cutscene-specific extensions
- wait_for_arrival: {}                     # Block until movement complete
- hold: { duration: 2.0s }                 # Timed pause
- sync_with: { channel: camera }           # Cross-channel sync

# Dialogue-specific extensions
- present_choice: { options: [...] }
- emotion_shift: { from: neutral, to: angry }
```

**Benefits**: Clear what's available where, type-specific semantics explicit, no implicit behavioral differences.

#### B2. Entity Resolution ✅

**Decision**: **Explicit Binding with Semantic Aliases** - Scene provides entities by category, document defines semantic names.

```yaml
# Cutscene header
metadata:
  id: dramatic_confrontation
  type: cutscene

# Entity bindings - scene provides these at invocation
bindings:
  # Main actors
  hero: "${scene.participants[0]}"
  villain: "${scene.participants[1]}"

  # Props
  shiny_lamp: "${scene.props[0]}"
  broken_chair: "${scene.props[1]}"

  # Environmental
  thunder_sfx: "${scene.audio[0]}"
  rain_vfx: "${scene.effects[0]}"
  spotlight: "${scene.lights[0]}"

# Channels use semantic names throughout
channels:
  hero:
    - walk_to: { mark: center }
    - look_at: { target: villain }

  villain:
    - grab: { target: broken_chair }
    - throw_at: { target: hero }

  environment:
    - trigger: { target: thunder_sfx }
    - activate: { target: rain_vfx }
    - focus: { target: spotlight, on: hero }

  camera:
    - move_to: { position: overhead }
    - track: { target: shiny_lamp }
```

**Invocation by Event Agent**:
```yaml
- invoke_cutscene:
    id: dramatic_confrontation
    participants: ["entity-123", "entity-456"]
    props: ["lamp-789", "chair-012"]
    audio: ["thunder-sfx-id"]
    effects: ["rain-vfx-id"]
    lights: ["spotlight-id"]
```

**Benefits**: Clear what each index refers to, semantic names readable throughout, Event Agent provides all entities (single source of truth), extensible binding categories.

#### B3. Handler Registration ✅

**Decision**: **Plugin System** - Service plugins register their own handlers.

```csharp
// Each plugin registers its handlers
public class CombatPlugin : IBannouPlugin
{
    public void RegisterHandlers(IHandlerRegistry registry)
    {
        registry.Add<AttackHandler>();
        registry.Add<BlockHandler>();
        registry.Add<DodgeHandler>();
    }
}

public class CognitionPlugin : IBannouPlugin
{
    public void RegisterHandlers(IHandlerRegistry registry)
    {
        registry.Add<FilterAttentionHandler>();
        registry.Add<QueryMemoryHandler>();
    }
}
```

**Benefits**: Matches existing pattern (lib-behavior/Handlers/), scales well, keeps related handlers together, extensible for game-specific actions.

---

### Section C: Control Gating Layer ✅ RESOLVED

> **Decisions recorded 2026-01-07**

#### C1. Gating Granularity ✅

**Decision**: **Entity-level with explicit input points** - Cutscene takes full control of entity, but can explicitly request input at specific points.

- Normal behavior entirely disabled for duration
- Cutscene controls ALL channels (including Speech, Expression)
- Cutscene can include QTE points where player/behavior input is read
- Cutscene can include dialogue-style pause points (wait for input)
- More problems than benefits with partial channel control

```yaml
# Cutscene with input points
channels:
  hero:
    - walk_to: { mark: center }
    - qte_prompt:                    # Explicit input request
        type: attack_choice
        timeout: 2.0s
        default_from: behavior       # QTE default from behavior stack
    - attack: { type: "${qte_result}" }
```

#### C2. Behavior Stack During Override ✅

**Decision**: **Continue + discard, perception still active** - Behavior stack evaluates continuously, outputs discarded, but perception events still generated.

- Behavior keeps computing "what I would do" (for QTE defaults)
- Outputs discarded (not sent to Intent Channels)
- **Key caveat**: Perception events still generated - character is "aware" of surroundings
- Character can form memories, update emotional state during cutscene
- Just can't ACT on perceptions until control returns

**Why this matters**: Hero in dramatic cutscene can still "notice" an ambush being set up. When cutscene ends, behavior stack already knows about the threat.

#### C3. Control Return ✅

**Decision**: **State sync required, transition style configurable** - All transition styles available, game sets default, cutscene can override.

```yaml
# Cutscene can specify handoff style
metadata:
  id: finishing_blow
  type: cutscene
  handoff:
    style: blend          # instant | blend | explicit
    blend_duration: 0.3s  # if blend

# Or explicit handoff action
channels:
  hero:
    - finishing_attack: {}
    - return_control:           # Explicit handoff
        style: blend
        duration: 0.5s
        state_sync: true        # Push final state to behavior stack
```

**State sync guarantees**:
- Cutscene's final world state pushed to behavior stack
- Behavior stack re-evaluates with fresh state
- No "behavior thinks enemy is still alive" problems

#### C4. Player Input During Cinematic ✅

**Decision**: **QTE + per-cutscene interruptibility + skip with final positions**

| Feature | How It Works |
|---------|--------------|
| **QTE** | Cutscene defines QTE points, behavior provides defaults |
| **Interruptibility** | Per-cutscene flag: `interruptible: true/false` |
| **Skip** | Game decides if skip allowed; cutscene provides skip destinations |

**Skip Destinations** - Cutscene declares where entities end up if skipped:

```yaml
metadata:
  id: dramatic_chase
  type: cutscene
  skippable: easily          # easily | with_penalty | not_skippable

  # Final positions if skipped (based on branch/QTE outcomes)
  skip_destinations:
    default:
      hero: { position: [10, 0, 5], state: exhausted }
      villain: { position: [50, 0, 20], state: escaped }

    # If specific QTEs were completed before skip
    hero_caught_villain:
      hero: { position: [25, 0, 12], state: victorious }
      villain: { position: [25, 0, 12], state: captured }
```

**Future consideration**: Pre-cache skip destinations by dry-run simulation through branch options.

#### C5. Control Priority ✅

**Decision**: **Cinematic > Player > Opportunity > Behavior** with willingness override.

```
DEFAULT PRIORITY (highest to lowest):
1. Cinematic (forced, takes full control)
2. Player input (commands to character)
3. Opportunity (optional, becomes cinematic if accepted)
4. Behavior stack (autonomous AI)

EXCEPTION - Willingness Override:
- Behavior CAN override player if urgency high enough
- Character willfulness affects override threshold
- Player "rank" with character affects threshold
- Same Monster Rancher feel: willful monster disobeys low-rank trainer
```

**Opportunity flow**:
1. Fight Coordinator detects opportunity
2. Opportunity offered to player/behavior (NOT forced)
3. If accepted → becomes cinematic (full control)
4. If declined → normal behavior continues

**Skills/spells consideration**: Some abilities are "micro-cinematics" - brief moments of cinematic control for flashy attacks. Player inputs limited to what the micro-cinematic expects.

---

### Section D: Multi-Entity Coordination ✅ RESOLVED

> **Decisions recorded 2026-01-07**
>
> **Key Insight**: Multiplayer cutscenes are THE DREAM's goal. Not just player + NPC, but player + player, both with QTEs affecting each other. This is unprecedented in gaming.

#### D1. Sync Point Scope ✅

**Decision**: **Cross-entity sync** - Sync points are global within the cutscene, any channel can emit/wait.

```yaml
channels:
  hero:
    - charge_attack: {}
    - emit: hero_attack_lands      # Global signal

  villain:
    - wait_for: hero_attack_lands  # Villain waits for hero
    - stagger_back: {}

  camera:
    - wait_for: hero_attack_lands  # Camera also waits
    - shake: { intensity: 0.5 }
```

**Why cross-entity is required**: If a cutscene can wait for input from one entity (variable timing), it needs to sync other entities back up. Multiplayer cutscenes with Player A and Player B both having QTEs REQUIRE cross-entity coordination.

#### D2. Timing Authority ✅

**Decision**: **Server authoritative with input windows** - Server runs the master clock, creates windows where specific participants can input.

**Mental Model - Input Window Types**:

| Window Type | Visual | Who Can Input | Timeout |
|-------------|--------|---------------|---------|
| **Blue box** | Your turn | Current player | Configurable (or indefinite in single-player) |
| **Red box** | Their turn | Other player | Configurable timeout |
| **Gray box** | No input | Nobody | Fixed duration (e.g., 2s to read text) |

```yaml
channels:
  hero:
    - dialogue: { text: "Ready to fight?", speaker: hero }
    - input_window:                    # Blue box for hero's player
        type: choice
        target: hero
        timeout: 5.0s                  # Or null for indefinite
        options: ["Attack", "Defend", "Flee"]

  villain:
    - wait_for: hero_choice            # Red box for villain's player
    - dialogue: { text: "You chose ${hero_choice}..." }
    - input_window:                    # Now blue for villain
        type: choice
        target: villain
        timeout: 5.0s
```

**Server behavior**:
- Server is always authoritative on timing
- Server creates input windows, waits for input OR timeout
- Late inputs may be rejected or treated as timeout
- Single-player cutscenes can have indefinite input windows (no timeout)
- Props/characters being "held" may require timeouts to prevent blocking

#### D3. Entity Ownership ✅

**Decision**: **Distributed execution, server authority** - Server coordinates, clients execute locally, server adjudicates.

| Aspect | Who Handles |
|--------|-------------|
| **Master timeline** | Server |
| **Channel execution** | Each client runs their entity's channel locally |
| **Sync points** | Server coordinates (waits for all, then releases all) |
| **Input windows** | Server creates, accepts/rejects inputs |
| **QTE adjudication** | Server decides outcome (player input is "suggestion") |

**Multiplayer example**:
1. Server sends cutscene to both Player A (hero) and Player B (villain)
2. Both clients execute their channels locally
3. Hero channel reaches `emit: hero_attacks` → client notifies server
4. Server waits for villain to reach `wait_for: hero_attacks`
5. Server releases both to continue
6. Hero channel reaches QTE → server creates input window
7. Player A's input sent to server
8. Server decides: accept input, use timeout default, or use behavior default
9. Server broadcasts result, both clients continue

#### D4. Failure Handling ✅

**Decision**: **Graceful degradation by design** - Disconnection isn't a problem because the architecture is inherently resilient.

| Component | If It Disconnects | What Happens |
|-----------|-------------------|--------------|
| **NPC Brain (Actor)** | Flavor layer gone | Character continues on loaded behaviors (fully functional) |
| **Player** | Input source gone | Behavior provides defaults, server uses timeout/defaults |
| **Spectator** | Observer gone | Nothing changes for participants |

**Key insight**: The system is designed so nothing is strictly required:
- NPC brain pushes state updates and sometimes replaces behaviors, but character works fine without it
- Player input is a "suggestion" that's sometimes ignored anyway (willfulness, timing)
- Everything degrades gracefully because everything has defaults

**No special failure handling needed** - the architecture handles it.

#### D5. Spectator Sync (Temporal Desync) ✅

**Decision**: **Game engine handles temporal desync** - ABML is agnostic, participants experience time warp, spectators see real-time.

**How it works**:
- **Participants** (in the cutscene) experience temporal desync:
  - "Skip ahead" during setup/travel/transitions
  - "Slow down" during QTE action prompts
  - Net result: don't need split-second reaction times
- **Spectators** see server's real-time execution
  - No time warping
  - See the "actual" pace of events

**ABML's role**: None. Cutscene runs at one pace, game engine handles:
- Which clients are participants vs spectators
- Time dilation for participants
- Real-time playback for spectators

This is entirely a game engine concern, not a behavior system concern.

---

### Section E: Behavior Stacking ✅ RESOLVED

> **Decisions recorded 2026-01-07**

#### E1. Stack vs Merge ✅

**Decision**: **Merge with priority** - All layers contribute outputs, priority resolves conflicts per channel.

**Example**: Guard with `humanoid-base` + `guard-patrol` + `afraid-of-spiders`. Spider appears.

| Channel | Winner | Output |
|---------|--------|--------|
| Movement | `afraid-of-spiders` (high priority) | Flee from spider |
| Combat | `afraid-of-spiders` (high priority) | No attack (fear override) |
| Speech | `afraid-of-spiders` (contextual) | "I'm not going near that thing!" |
| Attention | `guard-patrol` (still watching) | Track the spider |

**Why not selective override**: Goes too far - would get speech unrelated to context. Merge with priority keeps outputs contextually coherent.

#### E2. Stack Configuration ✅

**Decision**: **Static first, Hybrid later** - Start with explicit behavior lists, add dynamic trait-based assembly as enhancement.

**Phase 1 (Static)**:
```yaml
character:
  id: guard_bob
  behaviors:
    - base: humanoid-base
    - cultural: medieval-european
    - professional: guard-patrol
    - personal: afraid-of-spiders
```

**Phase 2 (Hybrid - later)**:
```yaml
character:
  id: guard_bob
  base_stack:                     # Fixed core
    - humanoid-base
    - medieval-european
    - guard-patrol
  dynamic_traits:                 # Can change at runtime
    fears: [spiders]
    mood: grumpy
```

**Why this order**: Static is simpler, and the infrastructure for static supports hybrid (just adding another source of behaviors). NPC brain can author/replace behaviors anyway, so configuration is "just a detail."

#### E3. Situational Layer Triggers ✅

**Decision**: **GOAP + Event-triggered** - Both mechanisms, used for different situations.

| Trigger Type | When Used | Example |
|--------------|-----------|---------|
| **GOAP** | Planner decides mode switch needed | "To reach destination, enter vehicle" → `vehicle-control` |
| **Event** | Discrete moment of transition | `enemy_spotted` → `combat-mode` |

**Why behavior-internal doesn't work**: Different archetypes have different channels.
- Humanoid: Combat, Movement, Interaction, Idle, Expression, Attention, Speech, Stance
- Vehicle: Throttle, Steering, Signals, Systems

Humanoid behavior can't output to Throttle/Steering. When mounting a vehicle, you MUST switch to vehicle-control behavior (or mounted-humanoid hybrid). Behavior-internal is not viable.

#### E4. Layer Interaction with Cutscenes ✅

**Decision**: **All layers run, outputs masked, cutscene puppets directly. Optional per-channel behavior input.**

**Execution Model**:
```
┌─────────────────────────────────────────────────────────────────────────────┐
│  CUTSCENE CONTROL: Cutscene writes directly to Intent Channels              │
│                                                                              │
│  Cutscene Channel ──► Handler Mapping ──► Entity's Intent Channels          │
│                                                                              │
│  Entity's Behavior Stack:                                                    │
│  • RUNNING (not suspended)                                                   │
│  • Perceiving surroundings (awareness continues)                            │
│  • Updating emotional state (feelings persist)                              │
│  • Computing "what I would do" (QTE defaults available)                     │
│  • Outputs → DISCARDED (cutscene has sole control)                          │
└─────────────────────────────────────────────────────────────────────────────┘
```

**Key points**:
- Behavior doesn't need to "accept" commands - it's completely bypassed
- Same model for wolf, elevator, humanoid - cutscene puppets them all
- No conflict because behavior outputs are discarded
- Behavior not "suspended" - it's "running but muted"

**Optional behavior input** - Cutscene can let behavior control specific channels:
```yaml
metadata:
  id: tense_standoff
  type: cutscene
  behavior_input:
    expression: true    # Let behavior control facial expressions
    speech: true        # Let behavior control vocalizations
    # All other channels: cutscene controls
```

This is the EXCEPTION, not the rule. Default is "cutscene puppets everything."

---

### Section F: THE DREAM Integration ✅ RESOLVED

> **Decisions recorded 2026-01-07**

#### F1. QTE Default Pre-computation ✅

**Decision**: **Continuous (behavior) + Cutscene fallback (lizard brain)**

Since behaviors always run during cutscenes, QTE defaults are always available from the behavior stack. However, cutscenes can still define defaults for:
- "Play only" mode without behaviors (testing, demos)
- Fallback when no behavior exists for an entity
- "Lizard brain" option for dramatic effect

**Future enhancement - Speed dice roll**:
```yaml
qte_prompt:
  type: attack_choice
  timeout: 2.0s
  defaults:
    cutscene: block           # "Lizard brain" fallback
    behavior: true            # Use behavior's computed default
  speed_contest: true         # Roll: did behavior "react fast enough"?
```

If `speed_contest: true`, there's a dice roll based on character's reaction speed. Fast characters get their behavior's choice; slow characters fall back to cutscene default. Creates tension: "Will my character react in time?"

#### F2. Character Willingness (Monster Rancher Feel) ✅

**Decision**: **Multiple mechanisms, pre-computed to avoid negotiation delay**

| Mechanism | When Used | Example |
|-----------|-----------|---------|
| **Chance to obey** | Animals, low-intelligence creatures | Wolf has 60% chance to follow pack command |
| **Urgency threshold** | Both directions | "I'll definitely listen in emergency" OR "I'll definitely NOT listen in emergency" |
| **Explicit refusal** | High-intelligence humanoids | Pre-commit: "This is important, I'm doing A, no one stops me" |
| **Pre-routed alternative** | Confusion/status effects | "If action X comes in next second, do Y instead" (temporary dyslexia) |

**Key insight - Avoid negotiation delay**: Willingness decisions are **pre-computed**, not real-time negotiation. Character has already decided "if player tells me to attack, I'll refuse" BEFORE the command arrives. No back-and-forth during action.

**Implementation sketch**:
```yaml
# Character's willingness state (updated by cognition/emotions)
willingness:
  obey_threshold: 0.6         # Commands below this urgency: might disobey
  refuse_threshold: 0.3       # Commands below this: definitely refuse
  pre_routes:                 # Temporary overrides
    - trigger: attack         # If told to attack...
      response: defend        # ...defend instead
      duration: 5s            # ...for next 5 seconds
      reason: "too_scared"
  pre_commits:                # Locked-in decisions
    - action: protect_child
      override_all: true      # Nothing overrides this
      reason: "parental_instinct"
```

#### F3. Three-Version Temporal Desync ✅

**Decision**: **Hybrid - ABML marks time-gain spots, game engine executes desync**

ABML must mark:
1. **Time-gain spots** - Where participants can "earn" slow-mo time by skipping ahead
2. **QTE spots** - Where slow-mo is spent
3. **Skip destinations** - Positions/state if section is skipped

```yaml
channels:
  hero:
    - walk_to: { mark: arena_entrance }
    - time_gain_section:              # ABML marks this section
        skip_destination:
          position: [10, 0, 5]
          state: ready_to_fight
        duration: 3.0s                # How much time can be "earned"

    - dramatic_entrance: {}

    - qte_section:                    # Where earned time is spent
        slow_mo_budget: 3.0s          # Max slow-mo available
        prompts:
          - type: attack_choice
            real_time_window: 0.5s    # Actual window without slow-mo
            perceived_window: 2.0s    # Window with full slow-mo
```

**Game engine responsibilities**:
- Track time budget per participant
- Apply time dilation during QTE sections
- Handle skip-ahead during time-gain sections
- Spectators see real-time (no dilation)

**Without these markers**: QTEs happen in real-time (functional fallback, but not THE DREAM experience).

---

### Section G: Document Type Boundaries ✅ RESOLVED

> **Decisions recorded 2026-01-07**

#### G1. Timeline vs Cutscene ✅

**Decision**: **Merged - One type with optional drama features**

Timeline and cutscene are the same document type (`type: cutscene`), with drama features as optional flags:

```yaml
metadata:
  id: simple_sequence
  type: cutscene
  drama:
    opportunities: false    # No opportunity detection
    qte_enabled: false      # No QTE integration
    extensions: false       # No cloud streaming extensions
    # Default: all false = pure timeline
```

```yaml
metadata:
  id: dramatic_battle
  type: cutscene
  drama:
    opportunities: true     # Event Brain can detect opportunities
    qte_enabled: true       # QTE prompts integrated
    extensions: true        # Cloud can stream extensions
```

**Benefits**:
- Single compiler path, simpler maintenance
- Gradual adoption: start with `drama: false`, enable features as needed
- Same execution model, different feature sets

#### G2. Dialogue Integration ✅

**Decision**: **Both inline AND external - inline REQUIRED for defaults, external for localization**

**Inline required** - Every dialogue must have inline defaults:
```yaml
flows:
  greet_player:
    - dialogue:
        speaker: merchant
        text: "Welcome to my shop!"      # REQUIRED inline default
        external: dialogue/merchant/greet # OPTIONAL external reference
        choices:
          - label: "Show me your wares"   # REQUIRED inline defaults
            text: "Show me your wares"
            external: dialogue/merchant/greet/show_wares
          - label: "Just looking"
            text: "Just looking around."
            external: dialogue/merchant/greet/just_looking
```

**External for localization/overrides**:
```yaml
# dialogue/merchant/greet.yaml (external file)
localization:
  en: "Welcome to my shop!"
  es: "¡Bienvenido a mi tienda!"
  jp: "いらっしゃいませ！"
overrides:
  - condition: "${player.reputation} > 50"
    text: "Ah, my favorite customer!"
  - condition: "${time} > 20:00"
    text: "We're about to close, but come in!"
```

**Resolution order**:
1. Check external file for condition overrides
2. Check external file for localization
3. Fall back to inline default

**Benefits**:
- Documents are playable without external files (testing, prototyping)
- Localization is a layer, not a blocker
- Rich dialogue variations without cluttering behavior files

#### G3. Cognition Customization ✅

**Decision**: **Layered - Shared base + character-specific overrides**

```yaml
# cognition/base/humanoid.yaml - Shared base template
metadata:
  id: humanoid-cognition-base
  type: cognition

stages:
  filter:
    - check_perception_distance: { max: 50.0 }
    - check_line_of_sight: {}

  memory_query:
    - query_recent: { duration: 60s }
    - query_related: { entity_type: "${perception.source_type}" }

  significance:
    - calculate_threat: {}
    - calculate_interest: {}
    - apply_personality_modifiers: {}  # Uses character's personality values

  storage:
    - store_if_significant: { threshold: 0.3 }

  intention:
    - generate_response: {}
```

```yaml
# Character definition with cognition override
character:
  id: paranoid_guard
  cognition:
    base: humanoid-cognition-base
    overrides:
      filter:
        # Paranoid characters notice more
        - check_perception_distance: { max: 75.0 }  # Override: longer range
      significance:
        # Paranoid characters see more threats
        - calculate_threat: { multiplier: 1.5 }     # Override: higher threat
```

**How overrides work**:
- Base template defines standard pipeline stages
- Character can override specific handlers within stages
- Overrides merge with (not replace) base stages
- Can add new handlers, modify parameters, or disable handlers

**Benefits**:
- One base template for all humanoids (maintainable)
- Character personality expressed through cognition tweaks
- Not per-character files to manage (scales better)
- Enables archetypes: "paranoid", "oblivious", "perceptive" as override sets

---

## Part 6: Capability Matrix (Final)

> **Updated 2026-01-07** - Reflects all Section A-G decisions.

### 6.1 Document Type Capabilities

| Document Type | GOAP | Multi-Channel | Continuation | Memory | Compilation | Intent Output |
|---------------|------|---------------|--------------|--------|-------------|---------------|
| **behavior** | Yes | No | Yes | Via cognition | Bytecode | Via emit_intent |
| **cutscene** | No | Yes | Yes | No | Partial | Via channel→entity |
| **dialogue** | Maybe | No | Yes | Yes | Tree-walk | Minimal (gestures) |
| **cognition** | No | No | No | Yes | Tree-walk | None (state only) |

**Note**: `timeline` merged into `cutscene` with `drama: false` (see G1).

### 6.2 Entity Archetype Channel Matrix

| Archetype | Channels | Primary Use Cases |
|-----------|----------|-------------------|
| **Humanoid** | Combat, Movement, Interaction, Idle, Expression, Attention, Speech, Stance | Players, NPCs, humanoid creatures |
| **Vehicle** | Throttle, Steering, Signals, Systems | Mounts, cars, ships, mechs |
| **Creature** | Locomotion, Action, Social, Alert | Animals, monsters, familiars |
| **Object** | State, Timing, Feedback | Doors, traps, elevators, puzzles |
| **Environmental** | Intensity, Type, Direction, Mood | Weather, lighting, ambience |

### 6.3 Control Layer Priority Matrix

| Priority | Source | Scope | Interruptible By |
|----------|--------|-------|------------------|
| 1 (Highest) | **Cinematic** | Full entity | Nothing (except skip) |
| 2 | **Player Input** | Per-command | Behavior willingness override |
| 3 | **Opportunity** | Offered | Player/behavior decline |
| 4 (Lowest) | **Behavior Stack** | All channels | All above |

### 6.4 Execution Context Matrix

| Context | Location | Latency | Executes | State Access |
|---------|----------|---------|----------|--------------|
| **Event Brain** | Cloud | 100-500ms | Cutscene generation, opportunity detection | Full world state |
| **Character Agent** | Cloud | 50-200ms | GOAP planning, cognition, dialogue | Per-character + relations |
| **Local Runtime** | Client | <1ms | Compiled bytecode behaviors | Local entity state |
| **Cinematic Runtime** | Client | Async | Cutscene execution with continuation | Scene state |

### 6.5 Multiplayer Coordination Matrix

| Feature | Server Role | Client Role | Sync Model |
|---------|-------------|-------------|------------|
| **Master Timeline** | Authoritative | Follows | Server broadcasts position |
| **Channel Execution** | Coordinates | Executes locally | Event-driven sync |
| **Input Windows** | Creates/validates | Sends input | Request/response |
| **QTE Adjudication** | Decides outcome | Suggests | Server authoritative |
| **Temporal Desync** | Tracks budgets | Applies dilation | Per-participant |

---

## Part 7: Implementation Layers (Detailed)

> **Status**: All architectural questions resolved. Ready for implementation.

### Layer 1: Entity Archetype System

**Goal**: Data-driven archetype definitions with per-type Intent Channel configurations.

**Schema** (`schemas/archetype-definitions.yaml`):
```yaml
archetypes:
  humanoid:
    channels:
      - name: combat
        default_urgency: 0.5
        merge_strategy: priority
      - name: movement
        default_urgency: 0.5
        merge_strategy: blend
      - name: interaction
        default_urgency: 0.3
        merge_strategy: priority
      - name: idle
        default_urgency: 0.1
        merge_strategy: blend
      - name: expression
        default_urgency: 0.4
        merge_strategy: blend
      - name: attention
        default_urgency: 0.6
        merge_strategy: priority
      - name: speech
        default_urgency: 0.5
        merge_strategy: priority
      - name: stance
        default_urgency: 0.3
        merge_strategy: blend
```

**Implementation**:
1. `IArchetypeRegistry` - Load/query archetype definitions
2. `ArchetypeResolver` - Assign archetype to entity at spawn
3. `IntentChannelFactory` - Create channel instances per archetype

### Layer 2: Handler Mapping

**Goal**: Translate ABML actions to Intent Channel outputs via plugin-registered handlers.

**Handler Interface**:
```csharp
public interface IActionHandler
{
    string ActionName { get; }                    // e.g., "walk_to", "attack"
    IEnumerable<string> DocumentTypes { get; }   // e.g., ["behavior", "cutscene"]

    Task<IntentEmission[]> ExecuteAsync(
        ActionContext context,
        IReadOnlyDictionary<string, object> parameters);
}
```

**Entity Resolution** (for cutscene channels):
```csharp
public interface IEntityResolver
{
    Task<EntityReference> ResolveAsync(
        string bindingName,           // e.g., "hero"
        CutsceneBindings bindings,    // scene.participants, scene.props, etc.
        ExpressionContext context);
}
```

**Implementation**:
1. `IHandlerRegistry` - Plugin handler registration
2. `ActionRouter` - Match action to handler by name + document type
3. `EntityResolver` - Resolve semantic names to entity references
4. `IntentEmitter` - Route handler outputs to correct channels

### Layer 3: Control Gating

**Goal**: Determine what controls each entity's Intent Channels at any moment.

**Control States**:
```csharp
public enum ControlSource
{
    Behavior,     // Normal behavior stack output
    Player,       // Player input commands
    Cinematic,    // Cutscene puppeting
    Opportunity   // Offered (not yet accepted)
}

public interface IControlGate
{
    ControlSource CurrentSource { get; }
    bool AcceptsBehaviorOutput { get; }
    bool AcceptsPlayerInput { get; }

    Task TakeControlAsync(ControlSource source, ControlOptions options);
    Task ReturnControlAsync(ControlHandoff handoff);
}
```

**Handoff Protocol**:
```csharp
public record ControlHandoff(
    HandoffStyle Style,           // Instant, Blend, Explicit
    TimeSpan? BlendDuration,
    bool SyncState,               // Push final state to behavior stack
    EntityState? FinalState);
```

**Implementation**:
1. `ControlGateManager` - Per-entity control tracking
2. `CinematicController` - Cutscene ↔ ControlGate integration
3. `BehaviorOutputMask` - Discard behavior outputs when gated
4. `StateSync` - Push cutscene final state to behavior

### Layer 4: Behavior Stacking

**Goal**: Merge outputs from multiple behaviors with priority-based conflict resolution.

**Stack Configuration**:
```csharp
public interface IBehaviorStack
{
    void AddLayer(BehaviorLayer layer, int priority);
    void RemoveLayer(string layerId);

    // Called every frame
    IntentOutputs ComputeOutputs(EntityState state);
}

public record BehaviorLayer(
    string Id,
    IBehavior Behavior,
    BehaviorCategory Category,    // Base, Cultural, Professional, Personal, Situational
    int Priority);
```

**Merge Strategy**:
```csharp
public interface IIntentMerger
{
    // Per-channel merge based on archetype configuration
    IntentValue Merge(
        string channelName,
        IReadOnlyList<IntentContribution> contributions,
        MergeStrategy strategy);    // Priority, Blend, Override
}
```

**Implementation**:
1. `BehaviorStackManager` - Layer add/remove/reorder
2. `IntentMerger` - Per-channel conflict resolution (existing, enhance)
3. `SituationalTrigger` - GOAP/Event-driven layer activation
4. `StackSerializer` - Save/load character behavior configuration

### Layer 5: Multi-Entity Coordination

**Goal**: Cross-entity sync points with server-authoritative timing for multiplayer.

**Sync Protocol**:
```csharp
public interface ICutsceneCoordinator
{
    // Server-side: wait for all participants
    Task WaitForSyncPointAsync(string syncId, IEnumerable<EntityId> participants);

    // Server-side: create input window
    Task<InputWindowResult> CreateInputWindowAsync(
        EntityId target,
        InputWindowOptions options);

    // Client-side: report reaching sync point
    Task ReportSyncReachedAsync(string syncId, EntityId entity);

    // Client-side: submit input
    Task SubmitInputAsync(string windowId, EntityId entity, object input);
}
```

**Temporal Desync** (game engine integration):
```csharp
public interface ITemporalManager
{
    // Track time budget per participant
    void AddTimeBudget(EntityId participant, TimeSpan amount);
    void SpendTimeBudget(EntityId participant, TimeSpan amount);

    // Apply dilation
    float GetTimeDilation(EntityId entity);  // 0.1 = 10x slow-mo, 2.0 = 2x fast
}
```

**Implementation**:
1. `SyncPointManager` - Track sync point progress
2. `InputWindowManager` - Create/validate/timeout input windows
3. `CutsceneTimelineServer` - Server-side master timeline
4. `TemporalDesyncBridge` - ABML markers ↔ game engine time dilation

### Layer 6: Dialogue & Localization

**Goal**: Inline dialogue with external localization overlay.

**Resolution Pipeline**:
```csharp
public interface IDialogueResolver
{
    Task<ResolvedDialogue> ResolveAsync(
        InlineDialogue inline,       // Required defaults from ABML
        string? externalRef,         // Optional external reference
        LocalizationContext locale,
        ExpressionContext context);  // For condition evaluation
}
```

**Implementation**:
1. `DialogueResolver` - Three-step resolution (overrides → localization → inline)
2. `ExternalDialogueLoader` - Load external dialogue files
3. `LocalizationProvider` - Language-specific text lookup
4. `ConditionEvaluator` - Evaluate override conditions

### Layer 7: Cognition Layering

**Goal**: Shared base cognition with per-character overrides.

**Override System**:
```csharp
public interface ICognitionBuilder
{
    ICognitionPipeline Build(
        CognitionTemplate baseTemplate,
        CognitionOverrides? overrides);
}

public record CognitionOverrides(
    Dictionary<string, HandlerOverride[]> StageOverrides);

public record HandlerOverride(
    string HandlerName,
    Dictionary<string, object>? NewParameters,
    bool Disabled);
```

**Implementation**:
1. `CognitionTemplateRegistry` - Load base templates
2. `CognitionBuilder` - Merge base + overrides
3. `OverrideApplier` - Apply parameter overrides to handlers

---

## Part 8: Next Steps

> **Status**: ✅ All architectural questions resolved (Sections A-G complete)

### Phase 1: Foundation (Can Start Now)

| Task | Layer | Dependencies | Priority |
|------|-------|--------------|----------|
| Define `archetype-definitions.yaml` schema | 1 | None | High |
| Implement `IArchetypeRegistry` | 1 | Schema | High |
| Implement `IHandlerRegistry` plugin system | 2 | None | High |
| Define core action vocabulary | 2 | None | High |

---

### 🔴 PHASE 1 AUDIT (2026-01-08)

> **Reviewer**: Claude Opus 4.5 (high-perspective reviewer agent)
> **Status**: Phase 1 partially complete - integration gaps identified

#### Audit Summary

| Component | Status | Notes |
|-----------|--------|-------|
| `archetype-definitions.yaml` | ✅ DONE | All 5 archetypes defined, 7 humanoid channels |
| `IArchetypeRegistry` | ✅ DONE | Thread-safe implementation with tests (19 passing) |
| `IIntentEmitterRegistry` | ✅ DONE | Built as new system (15 core emitters, 15 tests passing) |
| Core action vocabulary | ✅ DONE | walk_to, run_to, attack, block, dodge, look_at, etc. |
| `IControlGate` | ✅ DONE | Full interface with priority system |
| `ControlGateManager` | ✅ DONE | Per-entity registry (13 tests passing) |
| **DomainActionHandler integration** | 🔴 **MISSING** | Critical - systems not connected |
| **IntentChannelFactory** | 🔴 **MISSING** | Required for entity spawning |
| **BehaviorOutputMask** | 🔴 **MISSING** | Required for control gating to work |
| **StateSync** | 🔴 **MISSING** | Required for cinematic → behavior handoff |

#### 🔴 CRITICAL: DomainActionHandler Integration

**Problem**: The new `IIntentEmitterRegistry` system is built but NOT CONNECTED to the existing ABML execution pipeline.

**Current architecture** (disconnected):
```
DocumentExecutor → IActionHandlerRegistry → DomainActionHandler (just logs, does nothing)
                                                    ↓
                                              [NO CONNECTION]
                                                    ↓
IntentEmitterRegistry → IIntentEmitter → IntentEmission (orphaned)
```

**Required architecture** (integrated):
```
DocumentExecutor → IActionHandlerRegistry → DomainActionHandler
                                                    ↓
                                           IIntentEmitterRegistry
                                                    ↓
                                              IIntentEmitter
                                                    ↓
                                            IntentEmission[]
```

**Fix required in `bannou-service/Abml/Execution/Handlers/DomainActionHandler.cs`**:

```csharp
public sealed class DomainActionHandler : IActionHandler
{
    private readonly IIntentEmitterRegistry _emitters;
    private readonly IArchetypeRegistry _archetypes;
    private readonly IControlGateRegistry _controlGates;
    private readonly Func<Guid>? _entityIdResolver;  // From execution context

    public DomainActionHandler(
        IIntentEmitterRegistry emitters,
        IArchetypeRegistry archetypes,
        IControlGateRegistry controlGates,
        Func<Guid>? entityIdResolver = null)
    {
        _emitters = emitters;
        _archetypes = archetypes;
        _controlGates = controlGates;
        _entityIdResolver = entityIdResolver;
    }

    public async ValueTask<ActionResult> ExecuteAsync(
        ActionNode action, ExecutionContext context, CancellationToken ct)
    {
        var domainAction = (DomainAction)action;
        var scope = context.CallStack.Current?.Scope ?? context.RootScope;

        // 1. Evaluate parameters
        var evaluatedParams = ValueEvaluator.EvaluateParameters(
            domainAction.Parameters, scope, context.Evaluator);

        // 2. Resolve entity and archetype from context
        var entityId = ResolveEntityId(scope);
        var archetype = ResolveArchetype(scope, entityId);

        // 3. Create emission context
        var emissionContext = new IntentEmissionContext
        {
            EntityId = entityId,
            Archetype = archetype,
            DocumentType = context.Document.Metadata?.Type ?? "behavior",
            Data = BuildContextData(scope)
        };

        // 4. Look up emitter
        var emitter = _emitters.GetEmitter(domainAction.Name, emissionContext);
        if (emitter == null)
        {
            // No emitter - log and continue (backward compatibility)
            context.Logs.Add(new LogEntry("domain",
                $"{domainAction.Name} (no emitter)", DateTime.UtcNow));
            return ActionResult.Continue;
        }

        // 5. Emit intents
        var emissions = await emitter.EmitAsync(
            evaluatedParams.ToDictionary(kv => kv.Key, kv => kv.Value ?? new object()),
            emissionContext, ct);

        // 6. Filter through control gate
        var gate = _controlGates.Get(entityId);
        var filteredEmissions = gate?.FilterEmissions(emissions, ControlSource.Behavior)
            ?? emissions;

        // 7. Store emissions in context for later processing
        StoreEmissionsInContext(scope, filteredEmissions);

        return ActionResult.Continue;
    }

    private Guid ResolveEntityId(IVariableScope scope)
    {
        // Try to get from scope (agent.id or entity.id)
        if (scope.TryGetValue("agent", out var agent) &&
            agent is IDictionary<string, object?> agentDict &&
            agentDict.TryGetValue("id", out var idObj))
        {
            if (idObj is Guid guid) return guid;
            if (idObj is string str && Guid.TryParse(str, out var parsed)) return parsed;
        }

        // Fallback to resolver or empty
        return _entityIdResolver?.Invoke() ?? Guid.Empty;
    }

    private ArchetypeDefinition ResolveArchetype(IVariableScope scope, Guid entityId)
    {
        // Try to get archetype from scope
        if (scope.TryGetValue("archetype", out var archetypeId) &&
            archetypeId is string id)
        {
            return _archetypes.GetArchetype(id) ?? _archetypes.GetDefaultArchetype();
        }

        return _archetypes.GetDefaultArchetype();
    }

    private static IReadOnlyDictionary<string, object> BuildContextData(IVariableScope scope)
    {
        var data = new Dictionary<string, object>();

        // Copy relevant scope variables to context data
        if (scope.TryGetValue("feelings", out var feelings) && feelings != null)
            data["feelings"] = feelings;
        if (scope.TryGetValue("goals", out var goals) && goals != null)
            data["goals"] = goals;
        if (scope.TryGetValue("perceptions", out var perceptions) && perceptions != null)
            data["perceptions"] = perceptions;

        return data;
    }

    private static void StoreEmissionsInContext(
        IVariableScope scope, IReadOnlyList<IntentEmission> emissions)
    {
        // Store in _intent_emissions for later processing by runtime
        var existing = scope.TryGetValue("_intent_emissions", out var e) && e is List<IntentEmission> list
            ? list
            : new List<IntentEmission>();

        existing.AddRange(emissions);
        scope.SetValue("_intent_emissions", existing);
    }
}
```

**Additional changes needed**:

1. **Update `ActionHandlerRegistry.CreateWithBuiltins()`** to accept dependencies:
   ```csharp
   public static ActionHandlerRegistry CreateWithBuiltins(
       IIntentEmitterRegistry? emitters = null,
       IArchetypeRegistry? archetypes = null,
       IControlGateRegistry? controlGates = null)
   ```

2. **Update `DocumentExecutor` constructor** to pass registries through.

3. **Update DI registration** in service startup to wire everything together.

---

#### 🔴 MISSING: IntentChannelFactory

**What it does**: Creates runtime channel instances for an entity based on its archetype.

**Where to add**: `lib-behavior/Archetypes/IntentChannelFactory.cs`

```csharp
public interface IIntentChannelFactory
{
    /// <summary>
    /// Creates intent channel instances for an entity based on archetype.
    /// </summary>
    IntentChannelSet CreateChannels(Guid entityId, ArchetypeDefinition archetype);
}

public sealed class IntentChannelSet
{
    public Guid EntityId { get; init; }
    public string ArchetypeId { get; init; }
    public IReadOnlyDictionary<string, IntentChannel> Channels { get; init; }

    public IntentChannel? GetChannel(string name);
    public void ApplyEmission(IntentEmission emission);
}

public sealed class IntentChannel
{
    public string Name { get; }
    public LogicalChannelDefinition Definition { get; }
    public IntentValue? CurrentValue { get; private set; }

    public void SetValue(IntentValue value);
    public void Clear();
}
```

---

#### 🔴 MISSING: BehaviorOutputMask

**What it does**: Discards behavior stack outputs when entity is under cinematic/player control.

**Where to add**: `lib-behavior/Control/BehaviorOutputMask.cs`

**Integration point**: Must hook into `IntentMerger` or wrap its output.

```csharp
public interface IBehaviorOutputMask
{
    /// <summary>
    /// Filters merged intent output based on control state.
    /// </summary>
    MergedIntent ApplyMask(Guid entityId, MergedIntent behaviorOutput);
}

public sealed class BehaviorOutputMask : IBehaviorOutputMask
{
    private readonly IControlGateRegistry _gates;

    public MergedIntent ApplyMask(Guid entityId, MergedIntent behaviorOutput)
    {
        var gate = _gates.Get(entityId);
        if (gate == null || gate.AcceptsBehaviorOutput)
        {
            return behaviorOutput; // No masking needed
        }

        // Mask channels not in allowed list
        return behaviorOutput.MaskChannels(gate.BehaviorInputChannels);
    }
}
```

**Required change to `MergedIntent`**: Add `MaskChannels()` method that zeros out non-allowed channels.

---

#### 🔴 MISSING: StateSync

**What it does**: Pushes cinematic final state back to behavior stack when control returns.

**Where to add**: `lib-behavior/Control/StateSync.cs`

```csharp
public interface IStateSync
{
    /// <summary>
    /// Synchronizes entity state from cinematic back to behavior.
    /// Called when cinematic returns control.
    /// </summary>
    Task SyncStateAsync(
        Guid entityId,
        EntityState finalCinematicState,
        ControlHandoff handoff,
        CancellationToken ct);
}
```

**Integration point**: Called from `ControlGate.ReturnControlAsync()` when `handoff.SyncState` is true.

---

#### 🔴 CRITICAL: Interface Architecture Problem

**Problem**: Interfaces are incorrectly split between `bannou-service` and `lib-behavior`, creating duplicate types and unnecessary complexity.

**Current (WRONG) Architecture**:
```
bannou-service/Behavior/
├── IArchetype.cs           → IArchetypeDefinition, IArchetypeRegistry (simple)
├── IIntentEmitter.cs       → IIntentEmitter, IIntentEmitterRegistry (simple)
├── IControlGate.cs         → IControlGate, IControlGateRegistry (simple)
└── IntentEmission, IntentEmissionContext (types)

lib-behavior/
├── Archetypes/IArchetypeRegistry.cs   → IArchetypeRegistry : CoreBehavior.IArchetypeRegistry (EXTENDS)
├── Handlers/IIntentEmitter.cs         → IIntentEmitter (DUPLICATE - doesn't extend!)
├── Control/IControlGate.cs            → IControlGate : CoreBehavior.IControlGate (EXTENDS)
└── IntentEmission, IntentEmissionContext (DUPLICATE TYPES with ToCore()/FromCore())
```

**Issues**:
| Problem | Impact |
|---------|--------|
| Duplicate types (`IntentEmission`) | Two types with same name, different fields, need conversion |
| Duplicate interfaces | `IIntentEmitter` in lib-behavior does NOT extend bannou-service one |
| Method hiding (`new` keyword) | Returns concrete types instead of interfaces |
| Only ONE implementation exists | No actual need for extended interfaces |

**Root Cause**: Over-engineering Dependency Inversion. The split IS needed (DomainActionHandler can't reference lib-behavior), but extended interfaces are NOT needed.

**Required Architecture**:
```
bannou-service/Behavior/
├── IArchetype.cs           → COMPLETE interfaces with ALL methods
├── IIntentEmitter.cs       → COMPLETE interfaces with ALL methods
├── IControlGate.cs         → COMPLETE interfaces with ALL methods
└── ALL types (IntentEmission, ControlOptions, etc.)

lib-behavior/
├── ArchetypeRegistry.cs    → IMPLEMENTS bannou-service.IArchetypeRegistry (no separate interface)
├── IntentEmitterRegistry.cs → IMPLEMENTS bannou-service.IIntentEmitterRegistry (no separate interface)
├── ControlGateManager.cs   → IMPLEMENTS bannou-service.IControlGateRegistry (no separate interface)
└── IntentEmissionExtensions.cs → Extension methods for TargetPosition, etc.
```

**Fix Step 1**: Make bannou-service interfaces COMPLETE

Update `bannou-service/Behavior/IArchetype.cs`:
```csharp
public interface IArchetypeRegistry
{
    IArchetypeDefinition? GetArchetype(string archetypeId);
    IArchetypeDefinition? GetArchetypeByHash(int hash);           // ADD
    bool HasArchetype(string archetypeId);
    IArchetypeDefinition GetDefaultArchetype();
    IReadOnlyCollection<string> GetArchetypeIds();                // ADD
    IReadOnlyCollection<IArchetypeDefinition> GetAllArchetypes(); // ADD
    void RegisterArchetype(IArchetypeDefinition archetype);       // ADD
}
```

Update `bannou-service/Behavior/IIntentEmitter.cs`:
```csharp
public interface IIntentEmitterRegistry
{
    void Register(IIntentEmitter emitter);                        // ADD
    IIntentEmitter? GetEmitter(string actionName, IntentEmissionContext context);
    bool HasEmitter(string actionName);
    IReadOnlyCollection<string> GetActionNames();                 // Change to IReadOnlyCollection
}
```

Update `bannou-service/Behavior/IControlGate.cs`:
```csharp
public interface IControlGate
{
    Guid EntityId { get; }
    ControlSource CurrentSource { get; }
    ControlOptions? CurrentOptions { get; }                       // ADD
    bool AcceptsBehaviorOutput { get; }
    bool AcceptsPlayerInput { get; }
    IReadOnlySet<string> BehaviorInputChannels { get; }
    Task<bool> TakeControlAsync(ControlOptions options);          // ADD
    Task ReturnControlAsync(ControlHandoff handoff);              // ADD
    IReadOnlyList<IntentEmission> FilterEmissions(IReadOnlyList<IntentEmission> emissions, ControlSource source);
    event EventHandler<ControlChangedEvent>? ControlChanged;      // ADD
}

public interface IControlGateRegistry
{
    IControlGate? Get(Guid entityId);
    IControlGate GetOrCreate(Guid entityId);
    bool Remove(Guid entityId);
    IReadOnlyCollection<Guid> GetCinematicControlledEntities();   // ADD
    IReadOnlyCollection<Guid> GetPlayerControlledEntities();      // ADD
}
```

**Fix Step 2**: Move ALL types to bannou-service

Move these to `bannou-service/Behavior/`:
- `ControlOptions` (currently only in lib-behavior)
- `ControlHandoff` (currently only in lib-behavior)
- `ControlChangedEvent` (currently only in lib-behavior)

**Fix Step 3**: DELETE lib-behavior interface files

Remove entirely:
- `lib-behavior/Archetypes/IArchetypeRegistry.cs`
- `lib-behavior/Handlers/IIntentEmitter.cs` (the INTERFACE part - keep emitter implementations)
- `lib-behavior/Control/IControlGate.cs`

**Fix Step 4**: Update implementations to use bannou-service interfaces directly

```csharp
// lib-behavior/Archetypes/ArchetypeRegistry.cs
using BeyondImmersion.BannouService.Behavior;

namespace BeyondImmersion.BannouService.Behavior.Archetypes;

public sealed class ArchetypeRegistry : IArchetypeRegistry  // Direct implementation
{
    // Implement ALL methods from bannou-service interface
    // ArchetypeDefinition implements IArchetypeDefinition
}
```

**Fix Step 5**: Use extension methods for lib-behavior-specific needs

For `TargetPosition` (which lib-behavior emitters need but bannou-service doesn't):

```csharp
// lib-behavior/Extensions/IntentEmissionExtensions.cs
namespace BeyondImmersion.BannouService.Behavior.Extensions;

public static class IntentEmissionExtensions
{
    public static Vector3? GetTargetPosition(this IntentEmission emission)
    {
        if (emission.Data?.TryGetValue("target_position", out var pos) == true
            && pos is Vector3 vec)
            return vec;
        return null;
    }

    public static IntentEmission WithTargetPosition(this IntentEmission emission, Vector3 pos)
    {
        var data = new Dictionary<string, object>(emission.Data ?? new Dictionary<string, object>())
        {
            ["target_position"] = pos
        };
        return emission with { Data = data };
    }
}
```

**Fix Step 6**: Update emitter implementations to use extension methods

```csharp
// lib-behavior/Handlers/CoreEmitters/MovementEmitters.cs
public override ValueTask<IReadOnlyList<IntentEmission>> EmitAsync(...)
{
    var emission = new IntentEmission(channel, "walk", urgency, targetEntity)
        .WithTargetPosition(targetPos);  // Use extension method

    return ValueTask.FromResult<IReadOnlyList<IntentEmission>>(new[] { emission });
}
```

---

#### Interface Fix Checklist

- [x] Update `bannou-service/Behavior/IArchetype.cs` with complete interface methods ✅ DONE 2026-01-08
- [x] Update `bannou-service/Behavior/IIntentEmitter.cs` with complete interface methods ✅ DONE 2026-01-08
- [x] Update `bannou-service/Behavior/IControlGate.cs` with complete interface methods ✅ DONE 2026-01-08
- [x] Move `ControlOptions`, `ControlHandoff`, `ControlChangedEvent` to bannou-service ✅ DONE 2026-01-08
- [x] Delete `lib-behavior/Archetypes/IArchetypeRegistry.cs` ✅ DONE 2026-01-08
- [x] Delete `lib-behavior/Handlers/IIntentEmitter.cs` (interface parts only) ✅ DONE 2026-01-08
- [x] Delete `lib-behavior/Control/IControlGate.cs` ✅ DONE 2026-01-08
- [x] Update `ArchetypeRegistry` to implement `BeyondImmersion.BannouService.Behavior.IArchetypeRegistry` ✅ DONE 2026-01-08
- [x] Update `IntentEmitterRegistry` to implement `BeyondImmersion.BannouService.Behavior.IIntentEmitterRegistry` ✅ DONE 2026-01-08
- [x] Update `ControlGateManager` to implement `BeyondImmersion.BannouService.Behavior.IControlGateRegistry` ✅ DONE 2026-01-08
- [x] Create `lib-behavior/Extensions/IntentEmissionExtensions.cs` for TargetPosition ✅ DONE 2026-01-08
- [x] Update all emitters to use `IntentEmission` from bannou-service + extension methods ✅ DONE 2026-01-08
- [x] Remove all `ToCore()`/`FromCore()` conversion methods ✅ DONE 2026-01-08
- [x] Verify build succeeds after changes ✅ DONE 2026-01-08 (0 errors, 0 warnings)

---

#### Integration Checklist ✅ COMPLETED 2026-01-08

- [x] Update `DomainActionHandler` with `IIntentEmitterRegistry` integration ✅ Already done (verified in code)
- [x] Update `ActionHandlerRegistry.CreateWithBuiltins()` to accept new dependencies ✅ Already done (verified in code)
- [x] Update DI registration in `BehaviorServicePlugin` to wire registries ✅ DONE 2026-01-08
- [x] Add `IntentChannelFactory` and `RuntimeChannelSet` classes ✅ DONE 2026-01-08
- [x] Add `BehaviorOutputMask` ✅ DONE 2026-01-08
- [x] Add `StateSync` ✅ DONE 2026-01-08
- [x] Add tests for all new integration points ✅ DONE 2026-01-08 (21 new tests)
- [x] Verify existing tests still pass ✅ DONE 2026-01-08 (533 total tests passing)

---

### Phase 2: Control Infrastructure ✅ COMPLETED 2026-01-08

| Task | Layer | Dependencies | Status |
|------|-------|--------------|--------|
| Implement `IControlGate` interface | 3 | Archetypes | ✅ DONE |
| Implement `ControlGateManager` | 3 | IControlGate | ✅ DONE |
| Integrate with `CinematicInterpreter` | 3 | ControlGate | ✅ DONE - `CinematicController` created |
| Implement `BehaviorOutputMask` | 3 | ControlGate | ✅ DONE |

**Phase 2 Components**:
- `lib-behavior/Control/ControlGate.cs` - Per-entity control gate
- `lib-behavior/Control/ControlGateManager.cs` - Entity control registry
- `lib-behavior/Control/BehaviorOutputMask.cs` - Filters behavior output during control override
- `lib-behavior/Control/StateSync.cs` - State synchronization on control return
- `lib-behavior/Runtime/CinematicController.cs` - High-level cinematic playback with control gating

**Tests**: 21 new CinematicController tests, 554 total lib-behavior tests passing

### Phase 3: Behavior Stack Enhancement

| Task | Layer | Dependencies | Priority |
|------|-------|--------------|----------|
| Implement `IBehaviorStack` interface | 4 | Archetypes | Medium |
| Implement priority-based merge | 4 | IBehaviorStack | Medium |
| Implement situational triggers | 4 | Stack, Events | Medium |

### Phase 4: Multiplayer Coordination

| Task | Layer | Dependencies | Priority |
|------|-------|--------------|----------|
| Implement `ICutsceneCoordinator` | 5 | Control layers | Medium |
| Implement `SyncPointManager` | 5 | Coordinator | Medium |
| Implement `InputWindowManager` | 5 | Coordinator | Medium |
| Define game engine temporal API | 5 | None | Low (future) |

---

### 🔴 PHASE 4 AUDIT (2026-01-08)

> **Reviewer**: Claude Opus 4.5 (high-perspective reviewer agent)
> **Status**: ✅ Phase 4 COMPLETE (with documented deferrals)

#### What Was Implemented

| Component | Location | Tests | Status |
|-----------|----------|-------|--------|
| `ICutsceneCoordinator` | `bannou-service/Behavior/ICutsceneCoordinator.cs` | 16 | ✅ Complete |
| `CutsceneCoordinator` | `lib-behavior/Coordination/CutsceneCoordinator.cs` | - | ✅ Complete |
| `ICutsceneSession` | `bannou-service/Behavior/ICutsceneCoordinator.cs` | 19 | ✅ Complete |
| `CutsceneSession` | `lib-behavior/Coordination/CutsceneSession.cs` | - | ✅ Complete |
| `ISyncPointManager` | `bannou-service/Behavior/ISyncPointManager.cs` | 14 | ✅ Complete |
| `SyncPointManager` | `lib-behavior/Coordination/SyncPointManager.cs` | - | ✅ Complete |
| `IInputWindowManager` | `bannou-service/Behavior/IInputWindowManager.cs` | 21 | ✅ Complete |
| `InputWindowManager` | `lib-behavior/Coordination/InputWindowManager.cs` | - | ✅ Complete |
| `ITemporalManager` | `bannou-service/Behavior/ITemporalManager.cs` | - | ✅ Interface only |

**Total Phase 4 tests**: 70 tests passing

#### What Was Deferred (and Why)

The following components from Layer 5 spec (lines 1411-1416) are being deferred:

| Component | Reason | Future Location |
|-----------|--------|-----------------|
| `CutsceneTimelineServer` | Game engine concern (per D5 decision) | Game engine plugin |
| `TemporalDesyncBridge` | Game engine concern (per D5 decision) | Game engine plugin |
| `ITemporalManager` **implementation** | Game engine concern (per D5 decision) | Game engine plugin |

**Rationale** (from Section D5 - Spectator Sync decision):

> "This is entirely a game engine concern, not a behavior system concern."

The ABML/behavior system:
- ✅ Marks time-gain spots via `time_gain_section:` action
- ✅ Marks QTE spots via `qte_section:` action
- ✅ Provides skip destinations via metadata
- ✅ Provides `ITemporalManager` interface as integration contract

The game engine:
- ❌ (deferred) Tracks time budget per participant
- ❌ (deferred) Applies time dilation during QTE sections
- ❌ (deferred) Handles skip-ahead during time-gain sections
- ❌ (deferred) Ensures spectators see real-time (no dilation)

#### Interface Added (2026-01-08)

Created `ITemporalManager` in `bannou-service/Behavior/ITemporalManager.cs`:
- Defines the contract for temporal desync
- Includes `NullTemporalManager` for when temporal desync is disabled
- Ready for game engine to implement when Stride integration happens

#### ABML ↔ Coordination Integration (Deferred)

The implementing agent proposed these integration tasks:
```
☐ Add NetworkSyncAction ABML action type
☐ Add QtePromptAction ABML action type
☐ Implement NetworkSyncHandler to connect ABML to SyncPointManager
☐ Implement QtePromptHandler to connect ABML to InputWindowManager
☐ Update CinematicController to use CutsceneSession for multiplayer
☐ Add tests for ABML ↔ Coordination integration
```

**Decision**: Deferred to future work. The coordination infrastructure is complete and tested.
The ABML action types can be added when we have a concrete game integration scenario.
Current `emit:` and `wait_for:` actions in ABML already support the sync point pattern
at the document level.

#### Ready for Phase 5

Phase 4 is complete with:
1. ✅ All core coordination interfaces implemented and tested
2. ✅ `ITemporalManager` interface defined for future game engine integration
3. ✅ Temporal implementation explicitly deferred (documented game engine concern)
4. ✅ ABML integration points identified but deferred (not blocking)

**Phase 5 can proceed.**

---

### Phase 5: Content Systems

| Task | Layer | Dependencies | Priority |
|------|-------|--------------|----------|
| Implement `IDialogueResolver` | 6 | Expression VM | Low |
| Implement localization overlay | 6 | DialogueResolver | Low |
| Implement `ICognitionBuilder` | 7 | Existing cognition | Low |
| Implement override system | 7 | CognitionBuilder | Low |

### Implementation Notes

**What already exists** (leverage, don't rebuild):
- `BehaviorModelInterpreter` - Tree-walking execution ✅
- `IntentMerger` - Urgency-based merge (79 tests) ✅
- `CinematicInterpreter` - Continuation points ✅
- `GoapPlanner` - A* search with preconditions ✅
- `BehaviorBytecodeCompiler` - Client-side compilation ✅
- Cognition 5-stage pipeline - All handlers ✅

**What needs building** (the gaps this document addresses):
- Handler Mapping layer (Layer 2)
- ~~Control Gating layer (Layer 3)~~ ✅ DONE 2026-01-08
- ~~Entity Archetype registry (Layer 1)~~ ✅ DONE 2026-01-08
- Multiplayer sync coordination (Layer 5)

---

## Appendix A: Decision Summary

| Section | Key Decision | Rationale |
|---------|--------------|-----------|
| A1 | 8 humanoid channels | Expression, Attention, Speech, Stance needed for drama |
| A2 | All archetypes behavior-driven | Event Agents can modify anything |
| A3 | Data-driven archetypes | YAML schema, entities reference by ID |
| A4 | Hierarchical emit for packs | Alpha emits, members respond |
| B1 | Layered action vocabulary | Core shared, type-specific extensions |
| B2 | Explicit binding with aliases | Cutscene defines semantic names |
| B3 | Plugin handler registration | Matches existing lib-behavior pattern |
| C1 | Entity-level control | Full takeover, explicit input points |
| C2 | Continue + discard | Behavior runs, outputs masked |
| C3 | Configurable handoff | Blend/instant/explicit, state sync |
| C4 | QTE + skip destinations | Per-cutscene interruptibility |
| C5 | Priority with willingness | Monster Rancher feel |
| D1 | Cross-entity sync | Multiplayer cutscenes are THE DREAM |
| D2 | Server authoritative | Input windows (blue/red/gray box) |
| D3 | Distributed execution | Server coordinates, clients execute |
| D4 | Graceful degradation | Architecture handles disconnects |
| D5 | Engine handles temporal | ABML agnostic to time dilation |
| E1 | Merge with priority | All layers contribute, priority resolves |
| E2 | Static first, hybrid later | Start simple, add dynamic later |
| E3 | GOAP + event triggers | Both mechanisms for mode switches |
| E4 | All layers run, masked | Cutscene puppets, optional behavior input |
| F1 | Behavior + cutscene fallback | Speed dice roll future enhancement |
| F2 | Pre-computed willingness | No negotiation delay |
| F3 | ABML marks, engine executes | Hybrid temporal desync |
| G1 | Merged timeline/cutscene | Optional drama features flag |
| G2 | Inline + external dialogue | Inline required, external for localization |
| G3 | Layered cognition | Shared base + character overrides |

---

## Appendix B: Glossary

| Term | Definition |
|------|------------|
| **ABML Channel** | Parallel execution track within a document (authoring-time) |
| **Intent Channel** | Behavior output slot for entity control (runtime) |
| **Control Gating** | Layer that determines what source controls each entity |
| **Handler Mapping** | Translation from ABML actions to Intent emissions |
| **Temporal Desync** | Time dilation for participants vs spectators |
| **Willingness** | Character's pre-computed obedience thresholds |
| **Input Window** | Server-created period where specific participant can input |
| **Skip Destination** | Where entities end up if cutscene is skipped |
