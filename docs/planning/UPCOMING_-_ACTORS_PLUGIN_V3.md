# Actor Plugin V3 - Distributed Behavior Execution

> **Status**: IN PROGRESS (Phase 1-2 complete, Phase 3 starting)
> **Created**: 2026-01-01
> **Updated**: 2026-01-02 (Phase 1-2 behavior execution integration complete)
> **Related Documents**:
> - [BEHAVIOR_PLUGIN_V2.md](./ONGOING_-_BEHAVIOR_PLUGIN_V2.md) - Behavior compilation and GOAP planning
> - [ABML_LOCAL_RUNTIME.md](./ONGOING_-_ABML_LOCAL_RUNTIME.md) - Bytecode compilation and client execution
> - [THE_DREAM_GAP_ANALYSIS.md](./THE_DREAM_GAP_ANALYSIS.md) - Cinematic streaming composition
> - [ABML Guide](../guides/ABML.md) - Behavior language specification
> - lib-orchestrator - Node spawning infrastructure
> - lib-asset - Behavior storage (compiled bytecode)

---

## 1. Overview

### 1.1 What Is An Actor?

An **Actor** is a long-running task that executes a behavior (ABML document) in a loop until:
- The behavior signals completion (self-terminate)
- The control plane stops it (external terminate)

Actors are **not** request-response entities. They are autonomous processes that:
- Run continuously on pool nodes
- Execute behaviors defined in ABML
- Can be anything: CRON jobs, world administrators, NPC brains

### 1.1.1 The Actor's Role: Flavor, Not Foundation

**Critical insight**: Actors are **strictly optional**. Characters have massive, self-sufficient behavior stacks that handle every situation - opening fridges, hunting rabbits, combat, social interactions, daily routines. Without ANY actor, a character is fully functional. They just don't CHANGE or GROW.

**What actors provide:**
- **Growth**: Characters learn, evolve, develop new preferences
- **Spontaneity**: Unexpected reactions based on accumulated experiences
- **Personality**: Feelings, moods, and memories that color behavior
- **Realism**: Characters that feel alive because they change over time

**What actors do NOT provide:**
- Core functionality (behavior stack handles this)
- Moment-to-moment decision making (bytecode handles this)
- Required infrastructure (everything works without actors)

**Primary actor output: STATE UPDATES** (frequent)
- Feelings: "You're upset now", "You're scared"
- Goals: "You want to eat RABBIT specifically, not just food"
- Memories: "Remember that person betrayed you"

**Secondary actor output: BEHAVIOR CHANGES** (infrequent)
- "You learned a new combat technique" → add behavior
- "Your style has evolved" → swap behavior variant
- This is growth over time, not moment-to-moment control

### 1.2 Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           BANNOU NETWORK                                     │
│                                                                              │
│  ┌─────────────────────────────────────────────────────────────────────────┐│
│  │                    CONTROL PLANE (lib-actor in bannou-service)          ││
│  │  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐                      ││
│  │  │   Actor     │  │   Actor     │  │    Pool     │                      ││
│  │  │  Registry   │  │  Templates  │  │   Manager   │                      ││
│  │  └─────────────┘  └─────────────┘  └─────────────┘                      ││
│  └─────────────────────────────────────────────────────────────────────────┘│
│                                                                              │
│         lib-mesh (app-ids, routing)  +  lib-messaging (events)               │
│                              │                                               │
│    ┌─────────────────────────┼─────────────────────────┐                    │
│    │                         │                         │                    │
│    ▼                         ▼                         ▼                    │
│  ┌─────────────┐       ┌─────────────┐           ┌─────────────┐           │
│  │ Actor Pool  │       │ Actor Pool  │           │ Game Server │           │
│  │   Node A    │       │   Node B    │           │  (Stride)   │           │
│  │ app-id: xyz │       │ app-id: abc │           │ app-id: gs1 │           │
│  │             │       │             │           │             │           │
│  │ ┌─────────┐ │       │ ┌─────────┐ │           │ Runs:       │           │
│  │ │ Actor 1 │ │       │ │ Actor 4 │ │           │ - Physics   │           │
│  │ └─────────┘ │       │ └─────────┘ │           │ - Cinematics│           │
│  │ ┌─────────┐ │       │ ┌─────────┐ │           │ - Client    │           │
│  │ │ Actor 2 │ │       │ │ Actor 5 │ │           │   sync      │           │
│  │ └─────────┘ │       │ └─────────┘ │           │             │           │
│  └─────────────┘       └─────────────┘           └─────────────┘           │
│                                                                              │
│  All nodes are peers: same capabilities (lib-mesh, lib-messaging)           │
└─────────────────────────────────────────────────────────────────────────────┘
```

**Key insight**: Actor Pool Nodes and Game Servers are **peers** on the Bannou network. Both have unique app-ids, can send/receive events, and make mesh API calls. The difference is their responsibility:
- **Actor Pool Nodes**: Run actor behavior loops (cognition, decision-making)
- **Game Servers**: Run game simulation (physics, client sync, cinematic playback)

### 1.3 Key Concepts

| Concept | Description |
|---------|-------------|
| **Actor** | A running instance executing a behavior loop |
| **Actor Template** | Category definition: behavior ref + config + auto-spawn rules |
| **Pool Node** | Worker process running actor loops (spawned via lib-orchestrator) |
| **Control Plane** | Central coordinator in bannou-service (registry, templates, pool management) |
| **Behavior** | ABML document that defines what the actor does each tick |

### 1.4 Relationship to Other Services

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                            BANNOU SERVICES                                   │
│                                                                              │
│  ┌─────────────┐   ┌─────────────┐   ┌─────────────┐   ┌─────────────┐     │
│  │  Character  │   │  Behavior   │   │    Asset    │   │    Actor    │     │
│  │   Service   │   │   Service   │   │   Service   │   │   Service   │     │
│  └──────┬──────┘   └──────┬──────┘   └──────┬──────┘   └──────┬──────┘     │
│         │                 │                 │                 │             │
│    character data    compiles ABML     stores compiled    lifecycle mgmt   │
│    (stats, inventory)  GOAP planning   bytecode bundles   pool management  │
└─────────┼─────────────────┼─────────────────┼─────────────────┼─────────────┘
          │                 │                 │                 │
          │                 │                 │                 │
          ▼                 ▼                 ▼                 ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                         ACTOR POOL NODES                                     │
│                                                                              │
│  NPC Brain Actors:                    Event Actors:                         │
│  - Subscribe to character events      - Subscribe to regional events        │
│  - Evaluate cognition (tree-walking)  - Produce cinematics + extensions     │
│  - Update character intents/goals     - Coordinate multi-character scenes   │
│  - Trigger behavior changes           - Send events to Game Servers         │
└─────────────────────────────────────────────────────────────────────────────┘
          │                                      │
          │ behavior changes                     │ cinematics + extensions
          │ intent updates                       │ (via lib-messaging events)
          ▼                                      ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                          GAME SERVERS (Stride)                               │
│                                                                              │
│  - Fetches behavior bundles from Asset Service (pre-signed URLs)            │
│  - Runs BehaviorModelInterpreter for NPC combat/movement                    │
│  - Runs CinematicInterpreter for cutscenes (with continuation points)       │
│  - Publishes perception events for characters it manages                    │
│  - Syncs state to connected clients                                         │
│  - Sends pre-signed bundle URLs to clients                                  │
└─────────────────────────────────────────────────────────────────────────────┘
          │
          │ pre-signed URLs, state updates (UDP)
          ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                          GAME CLIENTS                                        │
│                                                                              │
│  - Downloads bundles from object storage (Minio/S3)                         │
│  - Runs local BehaviorModelInterpreter for animations                       │
│  - Runs local CinematicInterpreter (state dictated by server)               │
│  - Sends player inputs to Game Server                                       │
│  - NEVER talks directly to Bannou services                                  │
└─────────────────────────────────────────────────────────────────────────────┘
```

**Key boundaries:**
- **Character Service**: Pure data storage for character info (stats, inventory, relationships).
- **Behavior Service**: Compiles ABML → bytecode, provides GOAP planning. Does NOT store bytecode.
- **Asset Service**: Stores compiled behavior bytecode as assets. Game Servers fetch via pre-signed URLs.
- **Actor Service**: Lifecycle management for actors. Control plane only - actual execution on pool nodes.
- **Game Server**: Runs game simulation, executes bytecode, publishes perception events, coordinates clients.

**Critical note on dependencies:**
```
Game Client ──depends on──► Game Server ──depends on──► Bannou Services
                                                              │
                                              ┌───────────────┴───────────────┐
                                              │                               │
                                         REQUIRED                        OPTIONAL
                                    ┌─────────────────┐            ┌─────────────────┐
                                    │ Character       │            │ Actor           │
                                    │ Behavior        │            │ Service         │
                                    │ Asset           │            │                 │
                                    │ (core data &    │            │ (flavor only -  │
                                    │ compiled        │            │ growth, change, │
                                    │ behaviors)      │            │ personality)    │
                                    └─────────────────┘            └─────────────────┘
```

**Nothing depends on Actor Service.** Characters are fully functional with just their behavior stacks. Actors add growth and personality, but the game works without them.

---

## 2. Behavior Execution

### 2.1 Hybrid Execution Model

Actors support **two execution modes** that serve different purposes:

| Mode | Executor | Use Case | Performance | Hot Reload |
|------|----------|----------|-------------|------------|
| **Tree-walking** | `DocumentExecutor` | Cognition, decision-making | 10-50μs/node | ✅ Yes |
| **Bytecode** | `BehaviorModelInterpreter` | Combat actions, skills | <0.5ms total | ❌ No (recompile) |

**Why both?**
- **Tree-walking** is debuggable and hot-reloadable - critical for iterating on NPC cognition
- **Bytecode** is fast and allocation-free - critical for 60fps combat decisions

**Actor execution typically uses tree-walking for the main behavior loop** (cognition handlers, GOAP evaluation, intent updates). When the actor decides "use aggressive_sword_combo", it tells the Game Server to switch to that compiled behavior for the character.

### 2.2 Execution Infrastructure

```
bannou-service/Abml/Execution/
├── DocumentExecutor.cs      # Tree-walking interpreter (cognition)
├── ExecutionContext.cs      # Runtime state
├── IActionHandler.cs        # Action handler interface
└── Handlers/                # Built-in action handlers

lib-behavior/
├── Handlers/                # Cognition action handlers
│   ├── FilterAttentionHandler.cs
│   ├── AssessSignificanceHandler.cs
│   ├── QueryMemoryHandler.cs
│   ├── StoreMemoryHandler.cs
│   ├── EvaluateGoalImpactHandler.cs
│   └── TriggerGoapReplanHandler.cs
├── Goap/
│   └── GoapPlanner.cs       # A* planner
├── Compiler/
│   └── BehaviorCompiler.cs  # ABML → bytecode
└── Runtime/                 # (Phase 0: move from sdk-sources)
    └── BehaviorModelInterpreter.cs  # Bytecode VM

sdk-sources/Behavior/Runtime/
├── BehaviorModelInterpreter.cs  # Client-side VM (copied from lib-behavior)
├── CinematicInterpreter.cs      # Streaming composition with continuation points
└── Intent/                      # Intent channels for multi-behavior coordination
```

### 2.3 Actor Execution Flow

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                      ACTOR EXECUTION (on Pool Node)                          │
│                                                                              │
│  1. Actor spawns, loads behavior YAML from lib-asset                        │
│  2. Parser converts YAML → AbmlDocument (AST)                               │
│  3. Each tick:                                                               │
│     a. Drain perception queue into scope variables                          │
│     b. DocumentExecutor.ExecuteAsync() - tree-walking                       │
│     c. Cognition handlers evaluate (filter_attention, assess_significance)  │
│     d. GOAP planner may generate new plan                                   │
│     e. Actor emits STATE UPDATES (primary) or behavior changes (secondary)  │
│  4. Periodic state save to lib-state                                        │
│  5. Repeat until termination                                                │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    │ PRIMARY: State updates (feelings, goals, memories)
                                    │ SECONDARY: Behavior composition changes (rare)
                                    │ (lib-messaging events)
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                      GAME SERVER (Stride)                                    │
│                                                                              │
│  CHARACTER'S MASSIVE BEHAVIOR STACK (already running, self-sufficient):     │
│  ┌─────────────────────────────────────────────────────────────────────────┐│
│  │  Covers: combat, movement, social, survival, daily routines, etc.       ││
│  │  Reads inputs: perceptions, player input, actor state                   ││
│  │                                                                         ││
│  │  Actor state flows IN as input variables:                               ││
│  │  • feelings.upset = 0.8 → behavior responds accordingly                 ││
│  │  • goals.food_preference = "rabbit" → hunts rabbit, not generic food   ││
│  │  • memories.betrayed_by[X] = true → reacts differently to X            ││
│  └─────────────────────────────────────────────────────────────────────────┘│
│                                                                              │
│  BehaviorModelInterpreter.Evaluate() each frame                             │
│  → EmitIntent to IntentChannels → character acts                           │
│  → Publishes perception events back to character's event stream             │
│                                                                              │
│  WITHOUT ACTOR: Behavior stack still works, just doesn't change over time   │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 2.4 State Updates vs Behavior Changes

| Output Type | Frequency | What It Does | Example |
|-------------|-----------|--------------|---------|
| **State Update** | Every few ticks | Updates input variables read by behavior stack | `feelings.upset = 0.8` |
| **Goal Update** | When goals change | Updates goal-related inputs | `goals.primary = "find_shelter"` |
| **Memory Update** | When significant events occur | Stores/retrieves memories affecting behavior | `memories.add("betrayed_by", entityId)` |
| **Behavior Change** | Rare (learning/growth) | Modifies composed behavior stack | "Learned new combat technique" |

The behavior stack's bytecode reads these state inputs and responds accordingly. The actor doesn't control the character directly - it influences how the already-running behavior stack behaves.

### 2.5 State → Behavior → IntentChannels

How actor state updates eventually become character actions:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  ACTOR (Pool Node)                                                          │
│  Cognition produces: feelings, goals, memories                              │
└────────────────────────────────┬────────────────────────────────────────────┘
                                 │
                                 │ emit_state_update event
                                 ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│  GAME SERVER - Character's Behavior Stack                                   │
│                                                                              │
│  INPUT SLOTS (read by bytecode):                                            │
│  ├── perceptions: [enemy_nearby, low_health, ally_in_danger]               │
│  ├── player_input: [move_forward, attack_pressed] (if player-controlled)   │
│  └── actor_state: { feelings.angry=0.9, goals.target=X, memories... }      │
│                                                                              │
│  BYTECODE EVALUATION (massive composed behavior):                           │
│  ├── if angry > 0.7 AND enemy_nearby → choose aggressive_combo             │
│  ├── if fearful > 0.8 AND low_health → choose flee_behavior                │
│  ├── if goal.target exists → prioritize that target                        │
│  └── ... thousands of composed decision branches ...                        │
│                                                                              │
│  EMITINTENT OPCODES produce:                                                │
│  ├── Action: "heavy_slash", urgency=0.9                                    │
│  ├── Locomotion: toward_enemy, urgency=0.7                                 │
│  ├── Stance: "aggressive", urgency=0.8                                     │
│  └── Attention: focus_on_enemy, urgency=0.9                                │
└────────────────────────────────┬────────────────────────────────────────────┘
                                 │
                                 │ IntentMerger combines multiple behaviors
                                 ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│  CHARACTER ACTIONS                                                           │
│  Merged intents drive animation, movement, combat, attention                │
└─────────────────────────────────────────────────────────────────────────────┘
```

**Key insight**: The actor never emits IntentChannels directly. It emits STATE, which the behavior stack reads, which then emits IntentChannels. This keeps the layers clean:
- Actor: "Why" (feelings, goals, memories)
- Behavior Stack: "What" (which actions to take)
- IntentChannels: "How" (animation, movement execution)

### 2.6 The Behavior IS the Orchestrator

There is no separate "cognition orchestrator" component. The behavior document itself orchestrates the cognition pipeline:

```yaml
# Example NPC brain behavior
name: npc-brain
version: 1.0

context:
  variables:
    perceptions: { type: array, default: [] }
    current_goal: { type: object }
    current_plan: { type: object }

flows:
  main:
    - call: process_tick

  process_tick:
    # Stage 1: Filter attention
    - filter_attention:
        input: "${perceptions}"
        attention_budget: 100
        max_perceptions: 10
        priority_weights:
          threat: 10.0
          novelty: 5.0
          social: 3.0
        result_variable: "filtered"
        fast_track_variable: "urgent"

    # Handle urgent threats immediately
    - cond:
        if: "${len(urgent) > 0}"
        then:
          - call: handle_urgent_threat
            args: { threat: "${urgent[0]}" }

    # Stage 2: Query memory for context
    - query_memory:
        query_type: "episodic"
        context: "${filtered}"
        result_variable: "memories"

    # Stage 3: Assess significance
    - assess_significance:
        perceptions: "${filtered}"
        memories: "${memories}"
        result_variable: "significance_scores"

    # Stage 4: Store significant memories
    - for_each:
        items: "${filtered}"
        as: "perception"
        do:
          - store_memory:
              perception: "${perception}"
              significance: "${significance_scores[perception.id]}"
              threshold: 0.7

    # Stage 5: Evaluate goal impact
    - evaluate_goal_impact:
        perceptions: "${filtered}"
        current_goal: "${current_goal}"
        result_variable: "goal_impact"

    # Replan if needed
    - cond:
        if: "${goal_impact.requires_replan}"
        then:
          - trigger_goap_replan:
              urgency: "${goal_impact.urgency}"
              affected_goals: "${goal_impact.affected_goals}"
              result_variable: "new_plan"
          - set: current_plan = "${new_plan}"

    # Execute current plan step
    - call: execute_plan_step
```

### 2.3 Cognition Implementation Notes

#### 2.3.1 MVP Memory Relevance (Keyword Matching)

The current memory store implementation uses **keyword-based relevance matching**, not semantic embeddings. This is an MVP approach with known limitations:

**How it works** (see `ActorLocalMemoryStore.ComputeRelevanceScore`):
- **Category match**: If memory category matches perception category (weight: 0.3)
- **Content overlap**: Ratio of shared keywords between perception and memory content (weight: 0.4)
- **Metadata overlap**: Ratio of shared keys between perception data and memory metadata (weight: 0.2)
- **Recency bonus**: Memories <1 hour old get a boost (weight: 0.1)
- **Significance bonus**: Higher significance memories score higher (weight: 0.1)

**Limitations:**
- No semantic understanding - "enemy" and "adversary" don't match unless both words appear
- Simple tokenization (split on punctuation, filter words <4 chars)
- Can produce false positives (word appears in unrelated context)
- Requires explicit keyword overlap for relevance

**Future migration path:**
The `IMemoryStore` interface is designed for future replacement with a dedicated Memory service using embeddings. The interface methods (`FindRelevantAsync`, `StoreExperienceAsync`) can work with semantic similarity without changing callers.

#### 2.3.2 Cognition Constants

All magic numbers in the cognition pipeline are centralized in `CognitionConstants` (see `lib-behavior/Cognition/CognitionTypes.cs`):

**Urgency Thresholds:**
- `LowUrgencyThreshold` (0.3): Below this → full deliberation
- `HighUrgencyThreshold` (0.7): At or above this → immediate reaction

**Planning Parameters (per urgency band):**
| Urgency | MaxDepth | TimeoutMs | MaxNodes |
|---------|----------|-----------|----------|
| Low (<0.3) | 10 | 100 | 1000 |
| Medium (0.3-0.7) | 6 | 50 | 500 |
| High (≥0.7) | 3 | 20 | 200 |

**Attention Weights:**
- `DefaultThreatWeight` (10.0): Priority for threat perceptions
- `DefaultNoveltyWeight` (5.0): Priority for novel perceptions
- `DefaultSocialWeight` (3.0): Priority for social perceptions
- `DefaultRoutineWeight` (1.0): Priority for routine perceptions
- `DefaultThreatFastTrackThreshold` (0.8): Urgency above this bypasses normal pipeline

**ThreatFastTrack default is `true`** - fight-or-flight is typical NPC behavior. Set to `false` for characters that should remain calm under pressure (strategists, leaders who need to form memories during threats).

**Memory Relevance Weights:**
- `MemoryCategoryMatchWeight` (0.3)
- `MemoryContentOverlapWeight` (0.4)
- `MemoryMetadataOverlapWeight` (0.2)
- `MemoryRecencyBonusWeight` (0.1)
- `MemorySignificanceBonusWeight` (0.1)
- `MemoryMinimumRelevanceThreshold` (0.1): Memories must score at least this to be returned

**Test coupling note:** Unit tests that verify threshold behavior reference these constants. If constant values change, test expectations must be updated to match.

### 2.4 Project Dependencies

Pool nodes need access to behavior execution infrastructure:

```
lib-actor (pool node)
├── References: bannou-service (IDocumentExecutor, action handlers)
├── References: lib-behavior (cognition handlers, IGoapPlanner)
├── References: lib-character (ICharacterClient for NPC data)
├── References: lib-messaging (IMessageBus for events)
└── References: lib-state (IStateStoreFactory for actor state)
```

**Direct DI vs API calls:**
- `IDocumentExecutor` - Direct DI (runs in-process on pool node)
- `IGoapPlanner` - Direct DI (avoid round-trip for every planning call)
- `ICharacterClient` - Via lib-mesh (character data lives elsewhere)
- `IBehaviorClient` - Via lib-mesh (for fetching behaviors from lib-assets)

---

## 3. Actor State Management

### 3.1 State Persistence

Actor state is persisted periodically to lib-state:

```csharp
public class ActorState
{
    public string ActorId { get; set; }
    public string TemplateId { get; set; }
    public string Category { get; set; }

    // Behavior execution state
    public Dictionary<string, object?> Variables { get; set; } = new();
    public string? CurrentFlowName { get; set; }
    public int CurrentActionIndex { get; set; }

    // GOAP state
    public object? CurrentGoal { get; set; }
    public object? CurrentPlan { get; set; }
    public int CurrentPlanStep { get; set; }

    // Metrics
    public long LoopIterations { get; set; }
    public DateTimeOffset LastSaveTime { get; set; }
    public DateTimeOffset StartedAt { get; set; }
}
```

### 3.2 Auto-Save Configuration

```yaml
# actor-configuration.yaml
x-service-configuration:
  properties:
    DefaultAutoSaveIntervalSeconds:
      type: integer
      env: ACTOR_DEFAULT_AUTOSAVE_INTERVAL_SECONDS
      default: 60
      description: Default interval for periodic state saves
```

Per-template override:
```yaml
# Template definition
category: npc-brain
behaviorRef: "asset://behaviors/npc-brain-v1"
autoSaveIntervalSeconds: 30  # Override default

# Or disable entirely for transient actors
category: daily-cleanup
behaviorRef: "asset://behaviors/cleanup-v1"
autoSaveEnabled: false  # No state persistence
```

### 3.3 State Recovery

When a pool node restarts or an actor migrates:
1. Control plane detects actor needs recovery
2. Loads last saved state from lib-state
3. Spawns actor on new node with restored state
4. Behavior resumes from saved position

---

## 4. Perception Flow (NPC Brains)

### 4.1 How Perceptions Reach Actors

Perceptions flow **directly** from Game Server to Actor via event subscription - NO control plane routing:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                          GAME SERVER                                         │
│                                                                              │
│  Character moves, sees enemy, takes damage, etc.                            │
│                            │                                                 │
│                            │ publish                                         │
│                            ▼                                                 │
│              topic: character.{characterId}.perception                       │
└────────────────────────────┼────────────────────────────────────────────────┘
                             │
                             │ lib-messaging (RabbitMQ)
                             │ DIRECT delivery to subscriber
                             │
                             ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                         ACTOR POOL NODE                                      │
│                                                                              │
│  ActorRunner for character-{characterId}                                    │
│  └── Subscribed to: character.{characterId}.perception                      │
│      └── On receive: enqueue for next tick                                  │
│                                                                              │
│  ┌─────────────────────────────────────────────────────────────────────────┐│
│  │ Actor Loop (process_tick)                                               ││
│  │                                                                         ││
│  │  1. Drain perception queue                                              ││
│  │  2. filter_attention → filtered perceptions                             ││
│  │  3. assess_significance → scored perceptions                            ││
│  │  4. evaluate_goal_impact → should we replan?                           ││
│  │  5. Update intents, emit behavior changes                               ││
│  └─────────────────────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────────────────────┘
```

**Key point**: The control plane does NOT route perceptions. When an actor spawns on a pool node:
1. ActorRunner subscribes to `character.{characterId}.perception`
2. Events flow directly via lib-messaging
3. Control plane only handles lifecycle (spawn, stop, migrate)

**Why this matters for scale**: 10,000 NPCs × 10 events/second = 100,000 events/second. Routing through control plane would be a bottleneck. Direct subscription scales horizontally with pool nodes.

### 4.2 Perception Event Format

```yaml
# character-events.yaml
CharacterPerceptionEvent:
  type: object
  required: [characterId, perceptionType, sourceId, timestamp]
  properties:
    characterId:
      type: string
      format: uuid
      description: The character receiving this perception
    perceptionType:
      type: string
      enum: [visual, auditory, tactile, olfactory, proprioceptive]
    sourceId:
      type: string
      description: ID of the entity causing this perception
    sourceType:
      type: string
      enum: [character, npc, object, environment]
    data:
      type: object
      additionalProperties: true
      description: Perception-specific data
    urgency:
      type: number
      minimum: 0
      maximum: 1
      description: How urgent this perception is (0-1)
    timestamp:
      type: string
      format: date-time
```

### 4.3 Actor Perception Queue

Each actor maintains a perception queue:

```csharp
public class ActorRunner
{
    private readonly Channel<CharacterPerceptionEvent> _perceptionQueue =
        Channel.CreateBounded<CharacterPerceptionEvent>(100);

    public void EnqueuePerception(CharacterPerceptionEvent perception)
    {
        // Non-blocking, drops if queue full
        _perceptionQueue.Writer.TryWrite(perception);
    }

    private async Task<List<Perception>> DrainPerceptionsAsync()
    {
        var perceptions = new List<Perception>();
        while (_perceptionQueue.Reader.TryRead(out var evt))
        {
            perceptions.Add(ConvertToPerception(evt));
        }
        return perceptions;
    }
}
```

---

## 5. Messaging Patterns

### 5.1 Control Plane to Pool Node

Commands sent via dedicated topics per pool node:

```
actor.node.{poolAppId}.spawn    -> SpawnActorCommand
actor.node.{poolAppId}.stop     -> StopActorCommand
actor.node.{poolAppId}.message  -> SendMessageCommand
```

### 5.2 Pool Node to Control Plane

Status updates and completions:

```
actor.pool-node.heartbeat       -> PoolNodeHeartbeatEvent
actor.instance.status-changed   -> ActorStatusChangedEvent
actor.instance.completed        -> ActorCompletedEvent
```

### 5.3 Direct Actor-to-Actor Messaging

If an actor knows another actor's pool node app-id:
```
actor.node.{poolAppId}.message  -> SendMessageCommand (with target actorId)
```

If using actor ID only (goes through control plane):
```
POST /actor/send-message
{
  "actorId": "npc-grok-123",
  "messageType": "greeting",
  "payload": { "from": "npc-merchant-456" }
}
```

**The direct route does NOT auto-spawn.** Only the control plane route triggers instantiate-on-access.

---

## 6. Schemas

### 6.1 actor-api.yaml

```yaml
openapi: 3.0.3
info:
  title: Actor Service API
  version: 1.0.0
  description: Distributed actor management and execution

servers:
  - url: http://localhost:5012

paths:
  # === Actor Templates ===
  /actor/template/create:
    post:
      operationId: CreateActorTemplate
      summary: Create an actor template (category definition)
      x-permissions: [role:developer]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CreateActorTemplateRequest'
      responses:
        '200':
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ActorTemplateResponse'

  /actor/template/get:
    post:
      operationId: GetActorTemplate
      summary: Get an actor template by ID or category
      x-permissions: [role:user]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GetActorTemplateRequest'
      responses:
        '200':
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ActorTemplateResponse'

  /actor/template/list:
    post:
      operationId: ListActorTemplates
      summary: List all actor templates
      x-permissions: [role:user]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ListActorTemplatesRequest'
      responses:
        '200':
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ListActorTemplatesResponse'

  /actor/template/update:
    post:
      operationId: UpdateActorTemplate
      summary: Update an actor template
      x-permissions: [role:developer]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/UpdateActorTemplateRequest'
      responses:
        '200':
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ActorTemplateResponse'

  /actor/template/delete:
    post:
      operationId: DeleteActorTemplate
      summary: Delete an actor template
      x-permissions: [role:developer]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/DeleteActorTemplateRequest'
      responses:
        '200':
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/DeleteActorTemplateResponse'

  # === Actor Instances ===
  /actor/spawn:
    post:
      operationId: SpawnActor
      summary: Spawn a new actor from a template
      x-permissions: [role:developer]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/SpawnActorRequest'
      responses:
        '200':
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ActorInstanceResponse'

  /actor/get:
    post:
      operationId: GetActor
      summary: Get actor instance (instantiate-on-access if template allows)
      description: |
        If the actor exists, returns its current state.
        If the actor doesn't exist but a matching template has auto-spawn enabled,
        instantiates the actor and returns it.
      x-permissions: [role:user]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/GetActorRequest'
      responses:
        '200':
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ActorInstanceResponse'

  /actor/stop:
    post:
      operationId: StopActor
      summary: Stop a running actor
      x-permissions: [role:developer]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/StopActorRequest'
      responses:
        '200':
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/StopActorResponse'

  /actor/list:
    post:
      operationId: ListActors
      summary: List actors with optional filters
      x-permissions: [role:user]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ListActorsRequest'
      responses:
        '200':
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ListActorsResponse'

  /actor/send-message:
    post:
      operationId: SendActorMessage
      summary: Send a message to a running actor
      description: |
        Delivers a message to the actor's input queue.
        The behavior decides how to handle it.
        If actor doesn't exist and has auto-spawn template, spawns it first.
      x-permissions: [role:user]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/SendActorMessageRequest'
      responses:
        '200':
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/SendActorMessageResponse'

  # === Pool Management (admin) ===
  /actor/pool/list:
    post:
      operationId: ListPoolNodes
      summary: List all pool nodes
      x-permissions: [role:admin]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ListPoolNodesRequest'
      responses:
        '200':
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ListPoolNodesResponse'

  /actor/pool/spawn:
    post:
      operationId: SpawnPoolNode
      summary: Spawn a new pool node
      x-permissions: [role:admin]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/SpawnPoolNodeRequest'
      responses:
        '200':
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/PoolNodeResponse'

  /actor/pool/shutdown:
    post:
      operationId: ShutdownPoolNode
      summary: Gracefully shutdown a pool node
      x-permissions: [role:admin]
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ShutdownPoolNodeRequest'
      responses:
        '200':
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/ShutdownPoolNodeResponse'

components:
  schemas:
    # === Actor Templates ===
    CreateActorTemplateRequest:
      type: object
      required: [category, behaviorRef]
      properties:
        category:
          type: string
          description: Category identifier (e.g., "npc-brain", "world-admin", "cron-cleanup")
        behaviorRef:
          type: string
          description: Reference to behavior in lib-assets (e.g., "asset://behaviors/npc-brain-v1")
        configuration:
          type: object
          additionalProperties: true
          description: Default configuration passed to behavior execution
        autoSpawn:
          $ref: '#/components/schemas/AutoSpawnConfig'
        tickIntervalMs:
          type: integer
          default: 1000
          description: Milliseconds between behavior loop iterations
        autoSaveIntervalSeconds:
          type: integer
          default: 60
          description: Seconds between automatic state saves (0 to disable)
        maxInstancesPerNode:
          type: integer
          default: 100
          description: Maximum actors of this category per pool node

    AutoSpawnConfig:
      type: object
      description: Configuration for instantiate-on-access behavior
      properties:
        enabled:
          type: boolean
          default: false
          description: If true, accessing a non-existent actor creates it
        idPattern:
          type: string
          description: |
            Regex pattern for actor IDs that trigger auto-spawn.
            Examples: "npc-.*" matches "npc-grok", "npc-merchant-123"
        maxInstances:
          type: integer
          description: Maximum auto-spawned instances (0 = unlimited)

    ActorTemplateResponse:
      type: object
      properties:
        templateId:
          type: string
          format: uuid
          description: Unique template identifier
        category:
          type: string
          description: Category identifier
        behaviorRef:
          type: string
          description: Reference to behavior in lib-assets
        configuration:
          type: object
          additionalProperties: true
        autoSpawn:
          $ref: '#/components/schemas/AutoSpawnConfig'
        tickIntervalMs:
          type: integer
        autoSaveIntervalSeconds:
          type: integer
        createdAt:
          type: string
          format: date-time
        updatedAt:
          type: string
          format: date-time

    GetActorTemplateRequest:
      type: object
      properties:
        templateId:
          type: string
          format: uuid
          description: Template ID to retrieve
        category:
          type: string
          description: Or retrieve by category name

    ListActorTemplatesRequest:
      type: object
      properties:
        limit:
          type: integer
          default: 100
        offset:
          type: integer
          default: 0

    ListActorTemplatesResponse:
      type: object
      properties:
        templates:
          type: array
          items:
            $ref: '#/components/schemas/ActorTemplateResponse'
        total:
          type: integer

    UpdateActorTemplateRequest:
      type: object
      required: [templateId]
      properties:
        templateId:
          type: string
          format: uuid
        behaviorRef:
          type: string
          description: New behavior reference (triggers behavior.updated subscription)
        configuration:
          type: object
          additionalProperties: true
        autoSpawn:
          $ref: '#/components/schemas/AutoSpawnConfig'
        tickIntervalMs:
          type: integer
        autoSaveIntervalSeconds:
          type: integer

    DeleteActorTemplateRequest:
      type: object
      required: [templateId]
      properties:
        templateId:
          type: string
          format: uuid
        forceStopActors:
          type: boolean
          default: false
          description: If true, stops all running actors using this template

    DeleteActorTemplateResponse:
      type: object
      properties:
        deleted:
          type: boolean
        stoppedActorCount:
          type: integer

    # === Actor Instances ===
    SpawnActorRequest:
      type: object
      required: [templateId]
      properties:
        templateId:
          type: string
          format: uuid
          description: Template to instantiate from
        actorId:
          type: string
          description: Optional custom actor ID (auto-generated if not provided)
        configurationOverrides:
          type: object
          additionalProperties: true
          description: Override template defaults
        initialState:
          type: object
          additionalProperties: true
          description: Initial state passed to behavior
        characterId:
          type: string
          format: uuid
          description: Optional character ID for NPC brain actors

    GetActorRequest:
      type: object
      required: [actorId]
      properties:
        actorId:
          type: string
          description: Actor ID to retrieve

    ActorInstanceResponse:
      type: object
      properties:
        actorId:
          type: string
          description: Unique actor identifier
        templateId:
          type: string
          format: uuid
        category:
          type: string
        nodeId:
          type: string
          description: Pool node running this actor
        nodeAppId:
          type: string
          description: Pool node's app-id for direct messaging
        status:
          $ref: '#/components/schemas/ActorStatus'
        characterId:
          type: string
          format: uuid
          description: Associated character ID (for NPC brains)
        startedAt:
          type: string
          format: date-time
        lastHeartbeat:
          type: string
          format: date-time
        loopIterations:
          type: integer
          format: int64

    ActorStatus:
      type: string
      enum: [pending, starting, running, paused, stopping, stopped, error]
      description: Current actor lifecycle state

    StopActorRequest:
      type: object
      required: [actorId]
      properties:
        actorId:
          type: string
        graceful:
          type: boolean
          default: true
          description: If true, allows behavior to complete current iteration

    StopActorResponse:
      type: object
      properties:
        stopped:
          type: boolean
        finalStatus:
          $ref: '#/components/schemas/ActorStatus'

    ListActorsRequest:
      type: object
      properties:
        category:
          type: string
          description: Filter by category
        nodeId:
          type: string
          description: Filter by pool node
        status:
          $ref: '#/components/schemas/ActorStatus'
        characterId:
          type: string
          format: uuid
          description: Filter by associated character
        limit:
          type: integer
          default: 100
        offset:
          type: integer
          default: 0

    ListActorsResponse:
      type: object
      properties:
        actors:
          type: array
          items:
            $ref: '#/components/schemas/ActorInstanceResponse'
        total:
          type: integer

    SendActorMessageRequest:
      type: object
      required: [actorId, messageType]
      properties:
        actorId:
          type: string
        messageType:
          type: string
          description: Message type identifier (behavior decides how to handle)
        payload:
          type: object
          additionalProperties: true

    SendActorMessageResponse:
      type: object
      properties:
        queued:
          type: boolean
        wasSpawned:
          type: boolean
          description: True if actor was auto-spawned to receive this message

    # === Pool Nodes ===
    ListPoolNodesRequest:
      type: object
      properties:
        includeMetrics:
          type: boolean
          default: false

    ListPoolNodesResponse:
      type: object
      properties:
        nodes:
          type: array
          items:
            $ref: '#/components/schemas/PoolNodeResponse'

    SpawnPoolNodeRequest:
      type: object
      properties:
        capacity:
          type: integer
          default: 100
          description: Maximum actors this node can run

    PoolNodeResponse:
      type: object
      properties:
        nodeId:
          type: string
        appId:
          type: string
          description: Unique app-id for direct routing
        status:
          type: string
          enum: [starting, ready, draining, stopped]
        capacity:
          type: integer
        currentLoad:
          type: integer
        startedAt:
          type: string
          format: date-time
        lastHeartbeat:
          type: string
          format: date-time

    ShutdownPoolNodeRequest:
      type: object
      required: [nodeId]
      properties:
        nodeId:
          type: string
        graceful:
          type: boolean
          default: true
          description: If true, waits for actors to complete or migrate

    ShutdownPoolNodeResponse:
      type: object
      properties:
        status:
          type: string
        actorsMigrated:
          type: integer
        actorsStopped:
          type: integer
```

### 6.2 actor-events.yaml

```yaml
openapi: 3.0.3
info:
  title: Actor Service Events
  version: 1.0.0
  x-event-subscriptions:
    - topic: actor.pool-node.heartbeat
      event: PoolNodeHeartbeatEvent
      handler: HandlePoolNodeHeartbeat
    - topic: actor.instance.status-changed
      event: ActorStatusChangedEvent
      handler: HandleActorStatusChanged
    - topic: actor.instance.completed
      event: ActorCompletedEvent
      handler: HandleActorCompleted
    - topic: behavior.updated
      event: BehaviorUpdatedEvent
      handler: HandleBehaviorUpdated

x-lifecycle:
  ActorTemplate:
    model:
      templateId: { type: string, format: uuid, primary: true, required: true }
      category: { type: string, required: true }
      behaviorRef: { type: string, required: true }
      createdAt: { type: string, format: date-time, required: true }
  ActorInstance:
    model:
      actorId: { type: string, primary: true, required: true }
      templateId: { type: string, format: uuid, required: true }
      nodeId: { type: string, required: true }
      status: { type: string, required: true }
      startedAt: { type: string, format: date-time, required: true }

components:
  schemas:
    PoolNodeHeartbeatEvent:
      type: object
      required: [nodeId, timestamp, currentLoad, capacity]
      properties:
        nodeId:
          type: string
        appId:
          type: string
        timestamp:
          type: string
          format: date-time
        currentLoad:
          type: integer
        capacity:
          type: integer
        actorSummary:
          type: object
          additionalProperties:
            type: string
          description: Map of actorId to status for sync

    ActorStatusChangedEvent:
      type: object
      required: [actorId, nodeId, previousStatus, newStatus, timestamp]
      properties:
        actorId:
          type: string
        nodeId:
          type: string
        previousStatus:
          type: string
        newStatus:
          type: string
        reason:
          type: string
        timestamp:
          type: string
          format: date-time

    ActorCompletedEvent:
      type: object
      required: [actorId, nodeId, exitReason, timestamp]
      properties:
        actorId:
          type: string
        nodeId:
          type: string
        exitReason:
          type: string
          enum: [behavior_complete, error, timeout, external_stop]
        exitMessage:
          type: string
        loopIterations:
          type: integer
          format: int64
        timestamp:
          type: string
          format: date-time

    # Internal commands (pool node receives these)
    SpawnActorCommand:
      type: object
      required: [actorId, templateId, behaviorRef]
      properties:
        actorId:
          type: string
        templateId:
          type: string
          format: uuid
        behaviorRef:
          type: string
        configuration:
          type: object
          additionalProperties: true
        initialState:
          type: object
          additionalProperties: true
        tickIntervalMs:
          type: integer
        autoSaveIntervalSeconds:
          type: integer
        characterId:
          type: string
          format: uuid

    StopActorCommand:
      type: object
      required: [actorId]
      properties:
        actorId:
          type: string
        graceful:
          type: boolean

    SendMessageCommand:
      type: object
      required: [actorId, messageType]
      properties:
        actorId:
          type: string
        messageType:
          type: string
        payload:
          type: object
          additionalProperties: true
```

### 6.3 actor-configuration.yaml

```yaml
openapi: 3.0.3
info:
  title: Actor Service Configuration
  version: 1.0.0

x-service-configuration:
  properties:
    PoolNodeImage:
      type: string
      env: ACTOR_POOL_NODE_IMAGE
      default: "bannou-actor-pool:latest"
      description: Docker image for pool nodes
    MinPoolNodes:
      type: integer
      env: ACTOR_MIN_POOL_NODES
      default: 1
      description: Minimum pool nodes to maintain
    MaxPoolNodes:
      type: integer
      env: ACTOR_MAX_POOL_NODES
      default: 10
      description: Maximum pool nodes allowed
    DefaultActorsPerNode:
      type: integer
      env: ACTOR_DEFAULT_ACTORS_PER_NODE
      default: 100
      description: Default capacity per pool node
    HeartbeatIntervalSeconds:
      type: integer
      env: ACTOR_HEARTBEAT_INTERVAL_SECONDS
      default: 10
      description: Pool node heartbeat frequency
    HeartbeatTimeoutSeconds:
      type: integer
      env: ACTOR_HEARTBEAT_TIMEOUT_SECONDS
      default: 30
      description: Mark node unhealthy after this many seconds without heartbeat
    DefaultTickIntervalMs:
      type: integer
      env: ACTOR_DEFAULT_TICK_INTERVAL_MS
      default: 1000
      description: Default behavior loop interval
    DefaultAutoSaveIntervalSeconds:
      type: integer
      env: ACTOR_DEFAULT_AUTOSAVE_INTERVAL_SECONDS
      default: 60
      description: Default interval for periodic state saves
    PerceptionQueueSize:
      type: integer
      env: ACTOR_PERCEPTION_QUEUE_SIZE
      default: 100
      description: Max perceptions queued per actor before dropping
```

---

## 7. Implementation

### 7.1 ActorRunner (Pool Node)

```csharp
/// <summary>
/// Executes a single actor's behavior loop.
/// Runs on pool nodes.
/// </summary>
public sealed class ActorRunner : IDisposable
{
    private readonly string _actorId;
    private readonly ActorTemplate _template;
    private readonly IDocumentExecutor _executor;
    private readonly IGoapPlanner _goapPlanner;
    private readonly IStateStore<ActorState> _stateStore;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts = new();

    // Input queues
    private readonly Channel<CharacterPerceptionEvent> _perceptionQueue;
    private readonly Channel<ActorMessage> _messageQueue;

    // Execution state
    private AbmlDocument? _behavior;
    private IVariableScope _scope;
    private ActorState _state;
    private DateTimeOffset _lastSave;
    private long _loopIterations;

    public async Task RunAsync(Action<string, string, object?> onCompleted)
    {
        try
        {
            // Load behavior from lib-assets
            _behavior = await LoadBehaviorAsync(_template.BehaviorRef);

            // Initialize or restore state
            _scope = CreateScope(_state.Variables);

            // Main loop
            while (!_cts.Token.IsCancellationRequested)
            {
                // Drain perceptions into scope
                var perceptions = await DrainPerceptionsAsync();
                _scope.SetValue("perceptions", perceptions);

                // Process messages
                await ProcessMessagesAsync();

                // Execute one behavior tick
                var result = await _executor.ExecuteAsync(
                    _behavior,
                    "process_tick",  // Or configurable start flow
                    _scope,
                    _cts.Token);

                _loopIterations++;

                // Check for completion signal
                if (result.ReturnValue is { } rv &&
                    rv is IDictionary<string, object?> dict &&
                    dict.TryGetValue("terminate", out var term) &&
                    term is true)
                {
                    _logger.LogInformation(
                        "Actor {ActorId} behavior signaled completion", _actorId);
                    onCompleted(_actorId, "behavior_complete", _state.Variables);
                    return;
                }

                // Periodic state save
                if (ShouldSaveState())
                {
                    await SaveStateAsync();
                }

                // Wait for next tick
                await Task.Delay(_template.TickIntervalMs, _cts.Token);
            }

            onCompleted(_actorId, "external_stop", _state.Variables);
        }
        catch (OperationCanceledException)
        {
            onCompleted(_actorId, "external_stop", _state.Variables);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Actor {ActorId} crashed", _actorId);
            onCompleted(_actorId, "error", _state.Variables);
        }
    }

    private bool ShouldSaveState()
    {
        if (_template.AutoSaveIntervalSeconds <= 0)
            return false;

        return DateTimeOffset.UtcNow - _lastSave >
            TimeSpan.FromSeconds(_template.AutoSaveIntervalSeconds);
    }
}
```

### 7.2 Behavior Cache Invalidation

When a behavior is updated, actors using it should refresh:

```csharp
// In ActorService (control plane)
public async Task HandleBehaviorUpdatedAsync(BehaviorUpdatedEvent evt)
{
    _logger.LogInformation(
        "Behavior {BehaviorId} updated, notifying affected actors",
        evt.BehaviorId);

    // Find templates using this behavior
    var affectedTemplates = await FindTemplatesByBehaviorRefAsync(evt.AssetId);

    foreach (var template in affectedTemplates)
    {
        // Find running actors using this template
        var actors = await FindActorsByTemplateAsync(template.TemplateId);

        foreach (var actor in actors)
        {
            // Send reload command to pool node
            await _messageBus.PublishAsync(
                $"actor.node.{actor.NodeAppId}.reload-behavior",
                new ReloadBehaviorCommand
                {
                    ActorId = actor.ActorId,
                    NewBehaviorRef = evt.AssetId
                });
        }
    }
}
```

---

## 8. Use Cases

### 8.1 NPC Brain

```yaml
category: npc-brain
behaviorRef: "asset://behaviors/npc-brain-v1"
tickIntervalMs: 100           # 10 ticks per second
autoSaveIntervalSeconds: 30   # Save state every 30s
autoSpawn:
  enabled: true
  idPattern: "npc-.*"         # Auto-spawn any npc-* actor

# The behavior handles:
# - Perception processing (filter_attention, assess_significance)
# - Memory management (store_memory, query_memory)
# - Goal evaluation (evaluate_goal_impact)
# - GOAP replanning (trigger_goap_replan)
# - STATE OUTPUT (primary): feelings, goals, memories → flow to character
# - BEHAVIOR OUTPUT (secondary, rare): learning, skill acquisition
```

**Remember**: The character's behavior stack in the game world is MASSIVE and self-sufficient. It already knows how to:
- Hunt, gather, cook, eat
- Fight, flee, defend, surrender
- Socialize, trade, negotiate
- Sleep, rest, wander

The NPC brain actor provides **flavor**, not function:

```yaml
# Example: Actor processes "saw enemy" perception
flows:
  process_enemy_sighting:
    - assess_significance:
        perception: "${enemy_sighting}"
        result_variable: significance

    - query_memory:
        query: "past_encounters_with"
        entity_id: "${enemy_sighting.entity_id}"
        result_variable: history

    # PRIMARY OUTPUT: State updates (this is what actors mostly do)
    - cond:
        - if: "${history.was_betrayed}"
          then:
            - emit_state_update:
                feelings: { angry: 0.9, fearful: 0.3 }
                goals: { avoid_or_confront: "${enemy_sighting.entity_id}" }
        - else:
            - emit_state_update:
                feelings: { alert: 0.6 }

    # SECONDARY OUTPUT: Behavior changes (rare - only for growth/learning)
    # - emit_behavior_change:
    #     added: ["learned_counter_technique_v2"]
    #     reason: "fought this enemy type 10 times"
```

The character's existing behavior stack reads these state inputs and responds:
- `feelings.angry = 0.9` → more aggressive combat choices
- `goals.avoid_or_confront` → decides whether to engage or flee
- All WITHOUT the actor needing to specify HOW to fight or flee

### 8.2 Realm Administrator

```yaml
category: realm-admin
behaviorRef: "asset://behaviors/realm-admin-v1"
tickIntervalMs: 60000         # Once per minute
autoSaveIntervalSeconds: 300  # Save every 5 minutes
autoSpawn:
  enabled: true
  idPattern: "realm-admin-.*"

# The behavior handles:
# - Check realm health metrics
# - Spawn/despawn NPCs based on player density
# - Trigger weather/time transitions
# - Clean up abandoned entities
```

### 8.3 CRON Job (Transient)

```yaml
category: daily-cleanup
behaviorRef: "asset://behaviors/daily-cleanup-v1"
tickIntervalMs: 3600000       # Once per hour
autoSaveIntervalSeconds: 0    # No state persistence (transient)

# The behavior handles:
# - Check if it's cleanup time
# - If yes: run cleanup tasks, then terminate
# - If no: continue loop
```

### 8.4 Event Actor (Cinematic Coordinator)

Event Actors are NOT character representatives - they're regional/event representatives that:
- Subscribe to regional event streams (not character perception streams)
- Coordinate multi-character scenes (cinematics, boss fights)
- Produce cinematics + extensions for Game Servers
- May represent: holiday events, regional battles, dungeon instances

```yaml
category: event-coordinator
behaviorRef: "asset://behaviors/event-coordinator-v1"
tickIntervalMs: 500           # 2 ticks per second (orchestration, not combat)
autoSaveIntervalSeconds: 60   # Save coordinator state
autoSpawn:
  enabled: true
  idPattern: "event-.*"       # Auto-spawn any event-* actor

# The behavior handles:
# - Monitor regional combat (who's fighting whom)
# - Detect "interesting" situations worth escalating
# - Compose cinematics (compile new ABML → store in lib-asset)
# - Send cinematic events to Game Servers
# - Track extension opportunities (continuation points)
# - Send extensions before deadlines
```

**Cinematic Flow:**

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                      EVENT ACTOR (on Pool Node)                              │
│                                                                              │
│  1. Detect interesting combat in region                                     │
│  2. Compose cinematic ABML dynamically                                      │
│  3. Request compilation: Behavior Plugin compiles → stores in lib-asset     │
│  4. Wait for asset storage confirmation                                     │
│  5. Emit event to Game Server: "start cinematic {asset_id} for [chars]"     │
│  6. Monitor cinematic progress (Game Server publishes status events)        │
│  7. Decide on extensions based on player actions                            │
│  8. Compose extension → compile → store → emit "attach extension {id}"      │
│  9. Extension must arrive before continuation point timeout (graceful       │
│     degradation: if late, Game Server uses default flow)                    │
└─────────────────────────────────────────────────────────────────────────────┘
                                    │
                                    │ lib-messaging events
                                    ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                      GAME SERVER                                             │
│                                                                              │
│  1. Receives "start cinematic" event                                        │
│  2. Fetches compiled bytecode from lib-asset                                │
│  3. Runs CinematicInterpreter with continuation point support               │
│  4. Publishes status events: "phase 1 complete", "waiting at cp_X"          │
│  5. Receives "attach extension" → registers extension with interpreter      │
│  6. When continuation point reached: use extension or default               │
│  7. Syncs all state to connected clients                                    │
└─────────────────────────────────────────────────────────────────────────────┘
```

**Extension Delivery Ordering Guarantee:**

The "start cinematic" and "attach extension" events MUST include the pre-signed URL for the compiled bytecode directly in the event payload. This provides:

1. **Guaranteed ordering**: Event Actor waits for lib-asset storage confirmation before emitting the event. The signed URL proves the asset is available.
2. **No extra queries**: Game Server has everything it needs in the event - no additional `/asset/get` call required.
3. **Race avoidance**: Without the signed URL in the event, Game Server might receive the event before the asset is actually accessible in storage.
4. **Faster execution**: Single event contains asset location - Game Server can immediately fetch without lookup latency.

```yaml
# Event schema includes signed URL
CinematicStartEvent:
  type: object
  required: [cinematicId, signedUrl, characterIds, timestamp]
  properties:
    cinematicId:
      type: string
      format: uuid
    signedUrl:
      type: string
      format: uri
      description: Pre-signed URL to fetch compiled cinematic bytecode
    characterIds:
      type: array
      items: { type: string, format: uuid }
    continuationPoints:
      type: array
      items: { type: string }
      description: Named continuation points where extensions can attach

CinematicExtensionEvent:
  type: object
  required: [cinematicId, continuationPoint, signedUrl, timestamp]
  properties:
    cinematicId:
      type: string
      format: uuid
    continuationPoint:
      type: string
      description: Which continuation point this extension attaches to
    signedUrl:
      type: string
      format: uri
      description: Pre-signed URL to fetch compiled extension bytecode
```

### 8.5 NPC Brain During Cinematics

When a character is pulled into a cinematic, its NPC brain actor doesn't suspend:

```yaml
# NPC brain behavior handles this naturally
flows:
  process_perceptions:
    - filter_attention:
        perceptions: "${_perceptions}"
        result_variable: filtered

    # Check for cinematic status
    - cond:
        - if: "${any(filtered, p => p.type == 'cinematic_started')}"
          then:
            - set: { var: in_cinematic, value: true }
            - set: { var: event_coordinator_id, value: "${find(filtered, p => p.type == 'cinematic_started').coordinator_id}" }

    # During cinematic: can still process perceptions and adjust future intent
    # The character's ACTIONS are controlled by the cinematic, not the NPC brain
    # But the NPC brain can prepare for what comes after
    - cond:
        - if: "${in_cinematic}"
          then:
            # Process cinematic events (QTE prompts, choices)
            - handle_cinematic_events:
                events: "${filter(filtered, p => p.source == event_coordinator_id)}"
          else:
            # Normal cognition flow
            - assess_significance: { ... }
```

---

## 9. Open Questions & Decisions

### 9.1 Resolved

1. **Hybrid execution model**:
   - **Tree-walking** (`DocumentExecutor`): Used for actor cognition loops (hot-reloadable, debuggable)
   - **Bytecode VM** (`BehaviorModelInterpreter`): Used for combat/skills on Game Server (fast, allocation-free)

   **Decision**: Actors run tree-walking for cognition. When an actor decides "use behavior X", it tells
   the Game Server, which runs bytecode for that character. This gives us:
   - Debuggability for NPC decision-making
   - Performance for 60fps combat
   - Clear separation of concerns

2. **Perception routing**: Direct subscription, NOT control plane routing.
   - When actor spawns → ActorRunner subscribes to `character.{characterId}.perception`
   - Events flow directly via lib-messaging (RabbitMQ)
   - Control plane ONLY handles lifecycle (spawn, stop, migrate)
   - **Why**: 10K NPCs × 10 events/sec = 100K events/sec. Control plane routing won't scale.

3. **Actor state persistence**: Periodic auto-save with configurable interval per template.
   - Default: 60 seconds
   - Can be disabled for transient actors (autoSaveIntervalSeconds: 0)
   - State includes behavior variables, current goal/plan, loop count

4. **Cognition orchestration**: The behavior document IS the orchestrator.
   - No separate component needed
   - Cognition handlers are ABML actions (filter_attention, etc.)
   - Behavior author controls the cognition flow via ABML

5. **Message ordering**: FIFO queue per actor.
   - Perceptions drain at start of each tick
   - Messages processed after perception drain

6. **Cross-actor communication**: Two paths.
   - Direct (by pool app-id): Fast, no auto-spawn
   - Via control plane (by actor ID): Slower, triggers auto-spawn
   - **Note**: Direct route does NOT create actors if they don't exist

7. **Actor recovery on failure**: Do NOT auto-restart actors.
   - Individual actor death → let normal triggers re-activate (e.g., perception events)
   - For NPC actors: next perception event for that character spawns a new actor
   - Entire pool node death → emit event, but individual actors come back via triggers
   - State is preserved in lib-state for recovery if needed

8. **Character-Actor binding**: 1:1 with characterId as actor key.
   - For NPC brain actors, characterId IS the actorId
   - "Hey character 10456, what're you up to?" → wakes/creates that actor
   - Can only ever be ONE actor per character (but only some are awake at any time)
   - Not every character has an actor, but each actor controls exactly one character

9. **Game Server relationship**: Game Servers are peers on the Bannou network.
   - Same capabilities as Actor Pool Nodes (lib-mesh, lib-messaging)
   - Unique app-ids, can send/receive events, make API calls
   - Difference is responsibility: Game Servers run simulation, actors run cognition
   - Actor → Game Server communication: lib-messaging events (fast, fire-and-forget)

10. **Event Actors vs NPC Brain Actors**: Same actor system, different responsibilities.
    - NPC Brain: Represents a character, subscribes to character perception stream
    - Event Actor: Represents a region/event, subscribes to regional event streams
    - Both use same ActorRunner infrastructure

11. **GOAP planning location**: Direct `IGoapPlanner` via DI.
    - Pool nodes have lib-behavior reference
    - No round-trip to Behavior service for planning

12. **Behavior distribution**: Uses lib-asset, NOT a dedicated BehaviorDistributionService.
    - Behavior Plugin compiles ABML → stores bytecode in lib-asset
    - Game Servers fetch via pre-signed URLs (standard asset fetch)
    - No special `/behavior/models/sync` endpoint needed
    - lib-asset already has `behavior` as recognized asset type

13. **Actor output paradigm**: State updates are PRIMARY, behavior changes are SECONDARY.
    - **Primary (frequent)**: Feelings, goals, memories → flow as input variables to behavior stack
    - **Secondary (rare)**: Behavior composition changes → only for learning/growth over time
    - The character's behavior stack is massive and self-sufficient
    - Actors add flavor (growth, spontaneity, personality), NOT core functionality
    - Characters work perfectly fine without actors - they just don't change

14. **Player-controlled characters**: Player inputs go directly to character, not through actor.
    - Player makes 95% of decisions via direct input
    - Character's behavior stack still runs alongside player input
    - Character can override player if urgency is high enough (trust/sync dependent)
    - This is game design, not behavior system architecture, but relevant context

### 9.2 Deployment Modes

Actors can run in multiple deployment configurations. This is **configurable per environment**:

| Mode | Description | Use Case |
|------|-------------|----------|
| **bannou** | All actors run on control plane (single process) | Local dev, small deployments |
| **pool-per-type** | Separate container per actor category | Production with category isolation |
| **shared-pool** | One additional container for ALL actor types | Medium deployments |
| **auto-scale** | Spawn containers on demand based on load | Large-scale production |

**Configuration** (actor-configuration.yaml):
```yaml
DeploymentMode:
  type: string
  env: ACTOR_DEPLOYMENT_MODE
  default: "bannou"
  enum: [bannou, pool-per-type, shared-pool, auto-scale]
  description: |
    - bannou: All actors on control plane (single instance)
    - pool-per-type: Separate container per category
    - shared-pool: One container for all pool types
    - auto-scale: Dynamic container spawning
```

**In "bannou" mode:**
- No separate pool nodes are spawned
- ActorRunner instances run directly in bannou-service
- No lib-orchestrator usage for pools
- Simplest possible configuration

### 9.3 Bytecode Interpreter Architecture

The bytecode interpreter exists in `sdk-sources/Behavior/Runtime/` but should be canonical in lib-behavior.

**Current state:**
```
sdk-sources/Behavior/Runtime/
├── BehaviorModelInterpreter.cs   # Full stack-based VM
├── BehaviorModel.cs               # Binary model format
├── BehaviorOpcode.cs              # Opcodes (duplicated in bannou-service)
├── StateSchema.cs                 # Input/output schema
└── ...

bannou-service/Abml/Bytecode/
└── BehaviorOpcode.cs              # Only opcodes (must sync with SDK)
```

**Target state:**
```
lib-behavior/Runtime/
├── BehaviorModelInterpreter.cs   # CANONICAL - stack-based VM
├── BehaviorModel.cs               # CANONICAL - binary model format
├── BehaviorOpcode.cs              # CANONICAL - opcode definitions
├── StateSchema.cs                 # CANONICAL - schema
├── IBehaviorModelInterpreter.cs   # DI-friendly interface
└── ...

sdk-sources/Behavior/Runtime/
├── (copied from lib-behavior on build, like Connect protocol)
└── ...

bannou-service/Abml/Bytecode/
└── (removed - use lib-behavior reference)
```

**Build process:**
1. lib-behavior contains canonical bytecode runtime
2. On SDK build, copy runtime files to sdk-sources/Behavior/Runtime/
3. Adjust namespaces during copy (like Connect protocol does)
4. Both server and client use identical bytecode format

**DI wrapper in lib-behavior:**
```csharp
public interface IBehaviorModelInterpreter
{
    void SetRandomSeed(int seed);
    void Evaluate(ReadOnlySpan<double> inputState, Span<double> outputState);
    BehaviorModel Model { get; }
}

public interface IBehaviorModelInterpreterFactory
{
    IBehaviorModelInterpreter Create(BehaviorModel model);
    IBehaviorModelInterpreter Create(byte[] bytecode);
}
```

**Actor usage:**
```csharp
// In ActorRunner
private readonly IBehaviorModelInterpreterFactory _interpreterFactory;
private IBehaviorModelInterpreter? _interpreter;

private async Task LoadBehaviorAsync(string behaviorRef)
{
    var bytecode = await _assetClient.GetAssetBytesAsync(behaviorRef);
    _interpreter = _interpreterFactory.Create(bytecode);
}

private void ExecuteTick(Span<double> input, Span<double> output)
{
    _interpreter.Evaluate(input, output);
}
```

### 9.4 State Update Transport Architecture (Phase 2)

State updates from actors to Game Servers can use two transport mechanisms:

#### 9.4.1 The Dual Transport Model

| Transport | Use Case | Pros | Cons |
|-----------|----------|------|------|
| **lib-messaging** | Default, bannou mode | Simple, works everywhere | Higher latency, more broker load |
| **Internal Connect** | Production scale | Zero-copy, low latency, broadcast | Requires additional Connect nodes |

**Configuration** (actor-configuration.yaml):
```yaml
StateUpdateTransport:
  type: string
  env: ACTOR_STATE_UPDATE_TRANSPORT
  default: "messaging"
  enum: [messaging, internal-connect]
  description: |
    - messaging: Use lib-messaging for state updates (default, works in bannou mode)
    - internal-connect: Use regional internal Connect nodes (production scale)

InternalConnectRegionTtlSeconds:
  type: integer
  env: ACTOR_INTERNAL_CONNECT_REGION_TTL_SECONDS
  default: 30
  description: |
    How long to keep connections to previous regions after character migration.
    Allows quick return without connection re-establishment overhead.
```

**Why lib-messaging as default**: In "bannou" mode (single process), there are no separate Connect nodes. Actors and Game Servers would connect to the same Connect instance as clients, which wouldn't work as designed and would be heavier than just using lib-messaging.

#### 9.4.2 Internal Connect Architecture (Production Scale)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    REGION: Arcadia Zone 5                                    │
│                                                                              │
│  ┌─────────────────────────────────────────────────────────────────────────┐│
│  │              INTERNAL CONNECT NODE (region-specific)                    ││
│  │                                                                         ││
│  │  Primary Game Server: gs-arcadia-z5 (GUID: abc123)                     ││
│  │  Inner Game Servers: gs-arcadia-z5-dungeon-01 (GUID: xyz789)           ││
│  │                      gs-arcadia-z5-instance-02 (GUID: ...)             ││
│  │                                                                         ││
│  │  Connected Actors:                                                      ││
│  │  ├── char-1001 (GUID: def456) ──► knows gs-arcadia-z5 GUID            ││
│  │  ├── char-1002 (GUID: ghi789)                                          ││
│  │  └── ... ~5000 actors for this region ...                              ││
│  │                                                                         ││
│  │  Routing:                                                               ││
│  │  ├── Actor → Game Server: GUID header, zero-copy forward               ││
│  │  └── Game Server → broadcast: ALL connected actors (no GUID needed)    ││
│  │                                                                         ││
│  │  Inner server broadcasts also go to whole region (how inner server     ││
│  │  APPEARS in outer server context)                                      ││
│  └─────────────────────────────────────────────────────────────────────────┘│
│                                                                              │
│  Perception flow (still uses lib-messaging - needs to be tappable):         │
│  Game Server ──publish──► character.{id}.perception ──subscribe──► Actor   │
│  Perception events include region_connect_guid for actor to learn routing  │
└─────────────────────────────────────────────────────────────────────────────┘
```

**Key design points:**

1. **One primary Game Server per region per Connect node** - inner/instance servers share the same node
2. **Actors learn Game Server GUIDs from perception events** - `region_connect_guid` field
3. **Connection TTL on region transitions** - actors keep old connections briefly for quick return
4. **Broadcast is region-wide** - inner server events go to all actors in region (how inner server appears externally)
5. **Topic routing deferred** - not needed initially, easy to add later if required

#### 9.4.3 State Update Schema

**IMPORTANT**: The state update schema MUST be in `bannou-service/` (not lib-actor) so it's available in:
- Service SDK (for Game Servers like Stride to consume)
- Actor plugin (for actors to produce)

```csharp
// bannou-service/Events/ActorStateUpdate.cs
namespace BeyondImmersion.BannouService.Events;

/// <summary>
/// State update from actor to Game Server.
/// Transport-agnostic: works over lib-messaging or internal Connect.
/// </summary>
public sealed class ActorStateUpdate
{
    /// <summary>Character this update applies to.</summary>
    public required Guid CharacterId { get; init; }

    /// <summary>Actor that produced this update.</summary>
    public required string ActorId { get; init; }

    /// <summary>Timestamp when actor produced this update.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Feelings/emotional state changes.</summary>
    public Dictionary<string, double>? Feelings { get; init; }

    /// <summary>Goal changes.</summary>
    public GoalUpdate? Goals { get; init; }

    /// <summary>Memory updates (add/remove/modify).</summary>
    public List<MemoryUpdate>? Memories { get; init; }

    /// <summary>Behavior composition changes (rare - learning/growth).</summary>
    public BehaviorCompositionChange? BehaviorChange { get; init; }
}

public sealed class GoalUpdate
{
    public string? PrimaryGoal { get; init; }
    public Dictionary<string, object>? GoalParameters { get; init; }
    public List<string>? SecondaryGoals { get; init; }
}

public sealed class MemoryUpdate
{
    public required string Operation { get; init; }  // "add", "remove", "modify"
    public required string MemoryKey { get; init; }
    public object? MemoryValue { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

public sealed class BehaviorCompositionChange
{
    public List<string>? Added { get; init; }
    public List<string>? Removed { get; init; }
    public string? Reason { get; init; }
}
```

#### 9.4.4 Connect Neighbor Routing Prerequisites

**Before implementing internal Connect transport**, ensure Connect's neighbor routing is tested:

**Required tests (http-tester):**
```
Tests/ConnectNeighborRoutingTests.cs
├── Service_Can_Route_To_Neighbor_By_GUID
├── Service_Receives_Arbitrary_Payload_Zero_Copy
├── Unknown_Neighbor_GUID_Returns_Error
├── Neighbor_Disconnects_Routing_Fails_Gracefully
└── Multiple_Services_Can_Route_To_Same_Neighbor
```

**Required tests (edge-tester):**
```
Tests/ConnectNeighborRoutingEdgeTests.cs
├── Client_Can_Route_To_Neighbor_By_GUID
├── Client_Receives_Arbitrary_Payload_Zero_Copy
├── Bidirectional_Neighbor_Communication
├── Neighbor_Routing_With_Binary_Payload
└── Connection_GUID_Stability_Across_Reconnect
```

**Implementation notes:**
- Data MUST be arbitrary (zero-copy means Connect doesn't validate payload)
- This is "unsafe" in production for clients - would need safeguards (not zero-copy anymore)
- For internal service-to-service, unsafe is acceptable
- Tests should verify identical behavior for service and client perspectives

**Implementation Status (don/actors branch):**
- ✅ `ConnectionState.PeerGuid` - Unique GUID per connection for peer identification
- ✅ `WebSocketConnectionManager._peerGuidToSessionId` - Peer registry for GUID→session lookup
- ✅ `RouteToClientAsync` updated - Routes using peerGuid with Client flag (0x20)
- ✅ `CapabilityManifestEvent.peerGuid` - Clients receive their peerGuid in capability manifest
- ✅ `SessionConnectedEvent.peerGuid` - Published to RabbitMQ for service awareness
- ✅ Edge tests in `edge-tester/Tests/PeerRoutingTestHandler.cs` - Full WebSocket peer routing tests
- ✅ HTTP tests in `http-tester/Tests/PeerRoutingTestHandler.cs` - Pain point documentation

**Pain Points Resolved (ConnectionMode Implementation - 2026-01-02):**

The Connect service now supports three connection modes via `CONNECT_CONNECTION_MODE` environment variable:

| Mode | Auth | Capability Manifest | Broadcast | Use Case |
|------|------|---------------------|-----------|----------|
| `external` (default) | JWT required | Full manifest sent | **BLOCKED** (returns code 40) | Player clients |
| `relayed` | JWT required | Full manifest sent | Allowed | P2P games through edge |
| `internal` | Configurable | Skipped | Allowed | Actors, game servers |

**Internal Mode Authentication (`CONNECT_INTERNAL_AUTH_MODE`):**
- `service-token` (default): Validates `X-Service-Token` header against `CONNECT_INTERNAL_SERVICE_TOKEN`
- `network-trust`: No authentication required (for isolated internal networks)

**Broadcast Routing:**
- Uses `AppConstants.BROADCAST_GUID` (`FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF`)
- Message must have Client flag (0x20) set
- External mode returns `ResponseCodes.BroadcastNotAllowed` (40)
- Relayed/Internal modes fan-out to all connected peers (excluding sender)

**Internal Mode Response:**
Instead of capability manifest, internal connections receive minimal JSON:
```json
{ "sessionId": "...", "peerGuid": "..." }
```

**Remaining Migration:**
Voice service uses `sessionId` for P2P - should migrate to `peerGuid` for consistency

**Voice Migration Note:**
`lib-voice/Services/P2PCoordinator.cs` uses `VoicePeer.SessionId` and `peerSessionId` fields.
These should migrate to `peerGuid` for consistency with Connect's peer routing:
- `VoicePeer.SessionId` → `VoicePeer.PeerGuid`
- `peerSessionId` → `peerGuid` in voice-client-events.yaml
- Direct WebRTC signaling still works - only the identifier changes

#### 9.4.5 Broadcast Extension (Phase 2)

Add broadcast capability to Connect for Game Server → all actors:

```csharp
// Connect protocol extension
public enum BroadcastScope : byte
{
    AllConnected = 0,     // All connections on this Connect node
    // Future: TopicSubscribers = 1  // Only connections subscribed to a topic
}

// Game Server sends broadcast (no recipient GUID needed)
// Connect forwards to ALL connected WebSocket clients
```

**Note**: Topic-based routing deferred. If needed later, actors would subscribe to topics like `region.arcadia.zone-5` and broadcasts could target specific topics. Simple broadcast to all connected is sufficient for Phase 2.

---

## 10. Implementation Phases

### Phase 0: Bytecode Runtime Migration (Pre-requisite)
- [ ] Move `sdk-sources/Behavior/Runtime/*` to `lib-behavior/Runtime/`
- [ ] Create `IBehaviorModelInterpreter` and `IBehaviorModelInterpreterFactory` interfaces
- [ ] Add DI registration in lib-behavior
- [ ] Update SDK build to copy from lib-behavior (like Connect protocol)
- [ ] Remove `bannou-service/Abml/Bytecode/BehaviorOpcode.cs` (use lib-behavior)
- [ ] Update any server-side bytecode references

### Phase 1: Core Infrastructure ✅ COMPLETE
- [x] Create `schemas/actor-api.yaml`
- [x] Create `schemas/actor-events.yaml`
- [x] Create `schemas/actor-configuration.yaml` (including DeploymentMode)
- [x] Generate service code (`scripts/generate-service.sh actor`)
- [x] Implement ActorService (control plane)
- [x] Implement actor template CRUD
- [x] Implement actor spawn/stop/list
- [x] Implement "bannou" deployment mode (actors on control plane)

### Phase 2: ActorRunner & State Update Transport ✅ COMPLETE
- [x] Implement ActorRunner (behavior loop using tree-walking for cognition)
- [x] Implement perception queue (FIFO, bounded)
- [x] Implement message queue
- [x] Implement periodic state save
- [x] Add characterId binding for NPC brain actors
- [x] Implement state update emission (lib-messaging transport)
- [x] Create `bannou-service/Events/ActorStateUpdate.cs` (transport-agnostic schema)
- [x] Test "bannou" mode end-to-end (124 unit tests passing)

**Implementation Notes (Phase 1-2):**
- `lib-actor/Runtime/ActorRunner.cs` - Behavior execution loop with perception queue, state persistence
- `lib-actor/Runtime/ActorRunnerFactory.cs` - Factory with DI for all dependencies
- `lib-actor/Caching/BehaviorDocumentCache.cs` - Caches parsed ABML from lib-asset
- `lib-actor/Caching/IBehaviorDocumentCache.cs` - Cache interface
- `lib-actor/Execution/DocumentExecutorFactory.cs` - Creates IDocumentExecutor with cognition handlers
- `lib-actor/Execution/IDocumentExecutorFactory.cs` - Factory interface
- `lib-state/StateServicePlugin.cs` - Added "actor-state" store mapping
- `lib-actor/ActorServicePlugin.cs` - Registered all cognition handlers and factories
- `lib-actor/ActorServiceEvents.cs` - Added behavior.updated cache invalidation

**Phase 2b: Internal Connect Transport (Production Scale)**
- [x] Verify/complete Connect neighbor routing (see §9.4.4 prerequisites)
- [x] Add http-tester neighbor routing tests
- [x] Add edge-tester neighbor routing tests
- [x] Add Connect broadcast capability (BroadcastScope.AllConnected) - via BROADCAST_GUID
- [x] Implement ConnectionMode (external/relayed/internal) configuration
- [x] Implement Internal mode authentication (service-token/network-trust)
- [x] Implement Internal mode minimal response (skip capability manifest)
- [ ] Add StateUpdateTransport configuration option (lib-actor)
- [ ] Implement internal Connect transport for state updates (lib-actor)
- [ ] Implement connection TTL for region transitions
- [ ] Test dual-transport switching (messaging ↔ internal-connect)

### Phase 3: Pool Node Deployment
- [ ] Add pool node container support
- [ ] Implement "shared-pool" deployment mode
- [ ] Implement "pool-per-type" deployment mode
- [ ] Pool node ↔ control plane messaging
- [ ] Heartbeat system

### Phase 4: Orchestration Integration
- [ ] Pool node spawning via lib-orchestrator
- [ ] Pool node health monitoring
- [ ] Node failure detection and event emission
- [ ] Implement "auto-scale" deployment mode

### Phase 5: NPC Brain Integration
- [ ] Perception event routing (character.*.perception → actor)
- [ ] Behavior cache invalidation (behavior.updated subscription)
- [ ] GOAP integration via IGoapPlanner
- [ ] Cognition handler integration

### Phase 6: Advanced Features
- [ ] Actor affinity (keep related actors on same node)
- [ ] Metrics and observability
- [ ] Horizontal scaling policies
- [ ] Actor migration on graceful node shutdown

---

*Last Updated: 2026-01-02*
