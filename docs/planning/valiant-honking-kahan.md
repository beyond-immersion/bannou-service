# Behavior Types Specification Planning

## Executive Summary

This document catalogs all behavior types needed for the Bannou behavior system, synthesized from THE DREAM vision, gap analysis, ABML/GOAP documentation, and current implementation. The goal is to comprehensively identify behavior types before specifying their shapes.

---

## Current State Analysis

### What's Already Built
- **ABML 2.0**: Complete YAML DSL (585+ tests) with expression VM, parallel channels, composition
- **GOAP Planner**: A* search with world state, urgency-based parameters (complete)
- **Bytecode Compiler**: Local runtime for client-side execution (226 tests)
- **Cognition Pipeline**: 5-stage perception-to-intention flow (all handlers implemented)
- **Intent Merger**: Multi-model output coordination with urgency weighting
- **Streaming Composition**: Continuation points and extensions (complete)

### What's Not Built Yet
- Event Brain actor type (orchestration layer)
- Client-side Stride integration
- Hot-reload/dynamic behavior updates
- Model distribution service
- Dedicated memory service with embeddings

---

## Behavior Type Taxonomy

I've identified **5 major categories** containing **19 distinct behavior types**:

### Category 1: ABML Document Types (Schema-Level)

These are the `metadata.type` values in ABML documents - the foundational authoring primitives:

| Type | Description | Execution Context | Key Capabilities |
|------|-------------|-------------------|------------------|
| **behavior** | NPC autonomous decision-making | Cloud (Character Agent) + Client (Local Runtime) | GOAP annotations, triggers, reactive patterns |
| **cutscene** | Choreographed multi-character sequences | Cloud (Event Brain) + Client (Cinematic Runtime) | Multi-channel, sync points, continuation points |
| **dialogue** | Branching conversations | Cloud (Character Agent) | Player choices, conditional branches, state tracking |
| **cognition** | Perception processing pipelines | Cloud (Character Agent) | 5-stage flow, memory, significance assessment |
| **timeline** | Time-based parallel sequences | Client (Cinematic Runtime) | Pure choreography, no game-specific semantics |
| **dialplan** | Call routing/IVR flows | Cloud (Voice Service) | Sequential routing, DTMF handling |

### Category 2: Execution Layer Behaviors

These represent WHERE behaviors execute and their latency/capability profiles:

| Layer | Latency | Location | Purpose | Behavior Styles |
|-------|---------|----------|---------|-----------------|
| **Event Brain** | 100-500ms | Cloud | Cinematic orchestration, dramatic AI | Cutscenes, dramatic opportunity detection |
| **Character Agent** | 50-200ms | Cloud | Tactical decisions, personality-driven | GOAP planning, dialogue, social behaviors |
| **Local Runtime** | <1ms | Client | Frame-by-frame combat, movement | Compiled bytecode, deterministic |
| **Cinematic Runtime** | Async | Client | Pause/resume cinematics | Continuation points, streaming extensions |

### Category 3: Behavioral Model Types (Concurrent Channels)

These are the 4 fixed concurrent channels for character behavior output:

| Channel | Slot Range | Controls | Example Outputs |
|---------|------------|----------|-----------------|
| **Combat** | 0-1, 13 | Attack/defend/abilities | `attack`, `block`, `use_ability_3` |
| **Movement** | 2-3, 10-12 | Navigation/steering | `walk_to(x,y,z)`, `strafe_left`, `dodge` |
| **Interaction** | 4-5, 14 | Object/NPC interaction | `pick_up`, `talk_to`, `use_lever` |
| **Idle** | 8-9 | Ambient/waiting | `idle_fidget`, `look_around`, `yawn` |

### Category 4: Behavioral Category Stack

These are semantic categories for behavior stacking and priority resolution:

| Category | Priority | Description | Examples |
|----------|----------|-------------|----------|
| **base** | Lowest | Core behavioral templates | `humanoid-base`, `creature-base` |
| **cultural** | Low | Culture-specific overlays | `european-medieval`, `eastern-fantasy` |
| **professional** | Medium | Profession behaviors | `blacksmith-work`, `guard-patrol` |
| **personal** | Medium-High | Character-specific quirks | `afraid-of-spiders`, `loves-gossip` |
| **situational** | High | Context-reactive | `combat-mode`, `negotiation-mode` |
| **ambient** | Varies | Background activities | `breathing`, `blinking`, `weight-shifting` |

### Category 5: Agent/Orchestration Types

These are the higher-level agent constructs that use behaviors:

| Agent Type | Scope | Lifecycle | Primary Function |
|------------|-------|-----------|------------------|
| **Character Agent Co-Pilot** | Per-character | Always-on | Pre-compute QTE defaults, personality decisions |
| **Event Brain** | Regional/Scene | Spawned on dramatic detection | Orchestrate cinematics, coordinate NPCs |
| **Fight Coordinator** | Per-combat | Combat duration | Detect opportunities, inject cinematics |
| **World Watcher** | World/Region | Persistent | Detect dramatic situations, spawn event brains |
| **VIP Agent** | VIP NPCs | Always-on | High-priority event detection for important chars |

---

## Detailed Type Descriptions

### 1. BEHAVIOR (ABML Document Type)

**Purpose**: Define NPC autonomous decision-making logic that can execute both in cloud and on client.

**Where It Fits**:
- Authored as ABML YAML with optional GOAP annotations
- Compiled to bytecode for client-side Local Runtime
- Interpreted tree-walking for cloud Character Agent

**How It's Used**:
- GOAP planner extracts goals/actions at compile time
- Triggered by events, conditions, or time schedules
- Outputs merged via Intent Merger
- Continuation points allow cloud to inject extensions

**Capabilities Needed**:
- Expression evaluation (conditions, state access)
- GOAP preconditions/effects/cost
- Multiple trigger types (event, condition, time)
- Flow control (cond, for_each, repeat, goto, call)
- Handler invocations for domain actions
- Error handling chain

**Open Questions**:
- Should behaviors support "personality parameters" that modify execution?
- How do we handle behavior inheritance (base → cultural → personal)?

---

### 2. CUTSCENE (ABML Document Type)

**Purpose**: Choreographed multi-character sequences with precise timing and synchronization.

**Where It Fits**:
- Authored with `channels` structure (parallel tracks)
- Executed by Event Brain (cloud) or Cinematic Runtime (client)
- Can be extended mid-execution via continuation points

**How It's Used**:
- Event Brain spawns cutscene on dramatic opportunity
- Multiple channels run concurrently (camera, hero, villain, environment)
- Sync points coordinate timing across channels
- Extensions injected at continuation points for streaming composition

**Capabilities Needed**:
- Multi-channel parallel execution
- Named sync points (emit/wait_for)
- Continuation point markers
- Camera choreography actions
- Animation blending
- Audio cue synchronization

**Open Questions**:
- How do we handle cutscene interruption (player action during cutscene)?
- Should cutscenes have "importance" levels for interrupt priority?
- What's the handoff mechanism between cutscene control and player control?

---

### 3. DIALOGUE (ABML Document Type)

**Purpose**: Branching conversations with player choices and NPC responses.

**Where It Fits**:
- Authored with `flows` for dialogue trees
- Executed by Character Agent (cloud)
- UI rendered by client based on choice events

**How It's Used**:
- Player approaches NPC → dialogue trigger
- Flows present choices based on conditions
- Choices affect world state, relationships
- Can embed mini-cutscenes or reactions

**Capabilities Needed**:
- Choice presentation (text + conditions for availability)
- Response branching
- Relationship/reputation tracking
- Emotional state reflection
- Memory integration (recall past conversations)
- Interruption by external events

**Open Questions**:
- How do dialogues integrate with the cognition pipeline?
- Should NPCs remember specific dialogue choices across sessions?
- How do we handle multi-party dialogues (3+ participants)?

---

### 4. COGNITION (ABML Document Type)

**Purpose**: Define how NPCs process perceptions into intentions.

**Where It Fits**:
- Specialized flow type for the 5-stage cognition pipeline
- Executed by Character Agent (cloud)
- Outputs affect goal priorities and trigger GOAP replanning

**How It's Used**:
- Perception events enter pipeline
- Filter → Memory Query → Significance → Storage → Intention
- Significant perceptions become memories
- Goal impacts trigger behavior replanning

**Capabilities Needed**:
- Attention budget management
- Memory retrieval (relevance scoring)
- Significance assessment
- Goal impact evaluation
- Replan triggering
- Emotional state updates

**Open Questions**:
- Should cognition flows be character-specific or shared?
- How do we handle "instinctive" bypasses (threat fast-track)?
- What's the interface between cognition output and GOAP?

---

### 5. TIMELINE (ABML Document Type)

**Purpose**: Generic parallel sequences without game-specific semantics.

**Where It Fits**:
- Authored with `channels` structure
- Lower-level than cutscene (no dramatic context)
- Used for technical choreography

**How It's Used**:
- Orchestrate multiple parallel action streams
- Sync points for coordination
- No implicit game semantics

**Capabilities Needed**:
- Multi-channel execution
- Sync point synchronization
- Handler-agnostic actions
- Timing control

**Open Questions**:
- Is timeline distinct enough from cutscene to warrant separate type?
- Should timelines support continuation points?

---

### 6. EVENT BRAIN (Agent Type)

**Purpose**: Cloud-side orchestration agent that detects dramatic opportunities and spawns/coordinates cinematics.

**Where It Fits**:
- Spawned by World Watcher or Fight Coordinator
- Runs in cloud with full game state access
- Coordinates multiple Character Agents
- Manages cinematic injection

**How It's Used**:
- Detects dramatic opportunities (comeback, finishing blow, environmental)
- Generates cutscene extensions
- Publishes to Character Agents via continuation points
- Manages multi-character coordination

**Capabilities Needed**:
- Game state monitoring
- Opportunity detection (rules + heuristics)
- Cutscene generation/selection
- Multi-agent coordination
- Extension publishing
- Priority arbitration between concurrent events

**Open Questions**:
- What's the schema for Event Brain actor type?
- How do Event Brains coordinate with each other (avoid conflicting cinematics)?
- What triggers Event Brain spawning vs. destruction?

---

### 7. CHARACTER AGENT CO-PILOT (Agent Type)

**Purpose**: Always-running agent per NPC that knows the character's capabilities, personality, and preferences.

**Where It Fits**:
- Persistent per-character cloud agent
- Receives perception events
- Pre-computes QTE defaults
- Personality through failure decisions

**How It's Used**:
- When QTE appears, co-pilot has already computed "what character would do"
- If player doesn't respond, character's default executes
- Provides personality consistency
- Advises on dialogue choices

**Capabilities Needed**:
- Character knowledge (skills, abilities, preferences)
- Emotional state tracking
- Relationship awareness
- QTE default computation
- Personality modeling
- Decision explanation (for debugging)

**Open Questions**:
- How does co-pilot interact with GOAP planner?
- Should co-pilot decisions be visible to player (hints)?
- What's the latency budget for QTE default computation?

---

### 8. FIGHT COORDINATOR (Agent Type)

**Purpose**: Combat-scoped agent that watches fights and generates cinematic opportunities.

**Where It Fits**:
- Spawned when combat begins
- Destroyed when combat ends
- Monitors all combatants
- Interfaces with Event Brain

**How It's Used**:
- Watches health, positioning, action patterns
- Detects opportunity windows (stunned enemy, environmental)
- Generates opportunity events
- Coordinates with Event Brain for cinematics

**Capabilities Needed**:
- Combat state monitoring
- Opportunity detection rules
- Environmental awareness
- Timing coordination
- Cinematic triggering
- Multi-combatant tracking

**Open Questions**:
- How often should opportunities appear?
- Should coordinator learn player preferences?
- What's the opportunity-to-cinematic pipeline?

---

### 9. COMBAT BEHAVIOR (Model Type)

**Purpose**: Frame-by-frame combat decision making executed on client.

**Where It Fits**:
- Compiled bytecode running in Local Runtime
- Outputs to Combat intent channel (slots 0-1, 13)
- Merged with other model outputs

**How It's Used**:
- Every frame, evaluates combat state
- Outputs action intent + urgency
- Merged with movement, interaction intents
- Highest urgency wins

**Capabilities Needed**:
- Fast evaluation (<1ms)
- State access (health, stamina, distances)
- Action selection
- Combo awareness
- Opportunity response
- Urgency calculation

**Open Questions**:
- How do we handle action canceling?
- What's the interface with animation system?
- How do cooldowns integrate?

---

### 10. MOVEMENT BEHAVIOR (Model Type)

**Purpose**: Navigation and steering decisions executed on client.

**Where It Fits**:
- Compiled bytecode in Local Runtime
- Outputs to Movement intent channel (slots 2-3, 10-12)
- Interfaces with pathfinding

**How It's Used**:
- Decides movement goals
- Outputs locomotion intent + target position
- Urgency affects animation blend
- Coordinates with combat (strafing, dodging)

**Capabilities Needed**:
- Position awareness
- Target tracking
- Obstacle awareness
- Speed selection
- Direction control
- Dodge/evasion support

**Open Questions**:
- How does this integrate with navmesh pathfinding?
- Should steering be separate from locomotion type?
- What about vertical movement (jumping, climbing)?

---

### 11. INTERACTION BEHAVIOR (Model Type)

**Purpose**: Object and NPC interaction decisions.

**Where It Fits**:
- Compiled bytecode in Local Runtime
- Outputs to Interaction intent channel (slots 4-5, 14)
- Triggers interaction handlers

**How It's Used**:
- Detects interaction opportunities
- Outputs interaction intent + target
- Coordinates with other intents
- Triggers dialogue, item pickup, etc.

**Capabilities Needed**:
- Interactable detection
- Priority selection
- Availability checking
- Context appropriateness
- Action coordination

**Open Questions**:
- How do interaction behaviors know what's interactable?
- What's the priority between combat and interaction?
- How do we handle multi-step interactions?

---

### 12. IDLE BEHAVIOR (Model Type)

**Purpose**: Ambient, low-priority behaviors for naturalistic character appearance.

**Where It Fits**:
- Compiled bytecode in Local Runtime
- Outputs to Idle/Vocalization channels (slots 8-9)
- Lowest priority, fills gaps

**How It's Used**:
- Runs when no higher-priority action
- Provides breathing, fidgeting, looking around
- Personality-influenced idle variations
- Vocalizations (sighs, hums)

**Capabilities Needed**:
- Activity detection (when to idle)
- Variety selection
- Personality influence
- Context awareness
- Timing variation

**Open Questions**:
- How many idle variations per character type?
- Should idles blend or switch discretely?
- How does idle interact with attention system?

---

### 13-14. PROFESSIONAL/SITUATIONAL BEHAVIORS (Category Stack)

**Purpose**: Layer-able behavior modifications that stack on base behaviors.

**Where They Fit**:
- Multiple behaviors can be active simultaneously
- Priority determines conflict resolution
- Higher layers override lower

**How They're Used**:
- Base behavior provides fundamentals
- Professional adds job-specific actions
- Situational activates on context change
- Stack resolves conflicts by priority

**Capabilities Needed**:
- Stack management
- Priority resolution
- Partial override (not all behaviors need full replacement)
- Context-triggered activation
- Clean deactivation

**Open Questions**:
- How do we specify partial overrides?
- What's the merge semantics for conflicting outputs?
- Should stacks be character-defined or dynamically computed?

---

### 15. OPPORTUNITY BEHAVIORS (Arena-Specific)

Based on Monster Arena case study, these are specialized behavior patterns:

| Type | Trigger | Example |
|------|---------|---------|
| **Environmental Offensive** | Near object + enemy vulnerable | Kick boulder at stunned enemy |
| **Environmental Defensive** | Low health + cover available | Dive behind pillar |
| **Dramatic** | Comeback threshold | Rally when near death |
| **Signature** | Meter full + enemy in range | Character's signature move |
| **Finisher** | Enemy at KO threshold | Cinematic finishing blow |

**Capabilities Needed**:
- Opportunity detection predicates
- Cooldowns/frequency limits
- Dramatic timing (not too frequent)
- Smooth handoff to cinematic
- Opportunity decline (player doesn't take it)

**Open Questions**:
- Are opportunities behaviors or events?
- How do we balance opportunity frequency?
- Should AI opponents use opportunities differently?

---

## Capability Matrix

| Behavior Type | GOAP | Multi-Channel | Continuation | Memory | Compilation |
|---------------|------|---------------|--------------|--------|-------------|
| behavior | Yes | No | Yes | Via cognition | Yes (bytecode) |
| cutscene | No | Yes | Yes | No | Partial |
| dialogue | Maybe | No | Yes | Yes | No (tree-walk) |
| cognition | No | No | No | Yes | No (tree-walk) |
| timeline | No | Yes | Optional | No | Partial |
| Combat Model | No | N/A | No | No | Yes (bytecode) |
| Movement Model | No | N/A | No | No | Yes (bytecode) |
| Interaction Model | No | N/A | No | No | Yes (bytecode) |
| Idle Model | No | N/A | No | No | Yes (bytecode) |

---

## Open Questions Summary

### Architecture Questions
1. What's the schema for Event Brain actor type?
2. How do Event Brains coordinate to avoid conflicts?
3. What triggers Character Agent vs Event Brain behaviors?
4. How does the behavior stack priority resolution work?

### Execution Questions
5. How do cutscenes hand control back to player?
6. What's the QTE default computation latency budget?
7. How do we handle behavior interruption mid-execution?
8. Should timeline be separate from cutscene?

### Integration Questions
9. How do dialogues integrate with cognition pipeline?
10. How do compiled behaviors access entity state on client?
11. What's the memory service interface for cognition?
12. How do opportunity behaviors connect to cinematics?

### Design Questions
13. How do we handle personality parameters?
14. Should behaviors support inheritance?
15. How many idle variations are needed?
16. What's the opportunity frequency tuning mechanism?

---

## Next Steps

1. **User Clarification**: Answer open questions to nail down concepts
2. **Concept Pass**: Detailed concept definition for each behavior type
3. **Spec Development**: Full specification documents building on ABML/GOAP
4. **Integration Design**: How types interact and compose
5. **Implementation Roadmap**: Priority ordering for development

