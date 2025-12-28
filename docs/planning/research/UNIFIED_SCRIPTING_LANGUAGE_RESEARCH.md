# Unified Scripting Language Research
## ABML Evolution: From NPC Behaviors to Universal Event-Driven Scripting

> **Created**: 2024-12-28
> **Status**: INITIAL RESEARCH COMPLETE - Ready for Design Phase
> **Related**: ACTORS_ISSUES.md, arcadia-kb ABML docs, behavior service design
> **External Research**: See `DSL_STANDARDS_RESEARCH.md` for industry standards analysis

---

## Executive Summary

This document consolidates research for designing a unified YAML-based scripting language that can handle multiple seemingly-different domains which share fundamental patterns:

| Domain | Description | Common Pattern |
|--------|-------------|----------------|
| **NPC Behaviors** | Autonomous character decision-making | Event → State Check → Actions → State Change |
| **Dialogue Systems** | Branching conversations with choices | Event → State Check → Output → Branch |
| **Cutscene/Timelines** | Choreographed sequences of actions | Sequential Events → Parallel Actions → Sync Points |
| **Dialplans (Telephony)** | Call routing and IVR flows | Event → State Check → Actions → Route |
| **Agent Cognition** | Perception → Memory → Reasoning → Action | Sensory Event → Memory Query → Decision → Action |

**Core Insight**: All these domains are fundamentally **event-driven state machines with sequenced actions and conditional branching**.

---

## Part 1: Existing Research Analysis

### 1.1 ABML (Arcadia Behavior Markup Language) - From arcadia-kb

**Source**: `/home/lysander/repos/arcadia-kb/06 - Technical Architecture/NPC-Behavior-ABML-Specification.md`

ABML is a YAML-based DSL designed for NPC behaviors with these core features:

#### Core Structure
```yaml
version: "1.0.0"
metadata:
  id: "behavior_id"
  category: "profession"
  priority: 60

context:
  variables:
    energy: "${npc.stats.energy}"
  requirements:
    has_forge_access: true
  services:
    - name: "crafting_service"
      required: true

behaviors:
  morning_routine:
    triggers:
      - time_range: "06:00-09:00"
      - condition: "${context.energy > 0.7}"
    actions:
      - wake_up:
          animation: "stretch_and_yawn"
          duration: 3
      - service_call:
          service: "economy_service"
          method: "get_shop_status"
          result_variable: "shop_status"
      - cond:
          - when: "${shop_status.needs_restocking}"
            then:
              - goto: { behavior: "restock_materials" }
```

#### Key ABML Features
1. **Variable Interpolation**: `${npc.stats.energy}` syntax
2. **Service Integration**: Direct microservice calls with result capture
3. **Conditional Logic**: `cond:` blocks with `when:`/`then:`/`else:`
4. **Flow Control**: `goto:`, `for_each:` loops
5. **Stackable Extensions**: Modular behavior additions
6. **Error Handling**: Service unavailable fallbacks

### 1.2 GOAP Integration Pattern

**Source**: `/home/lysander/repos/arcadia-kb/06 - Technical Architecture/NPC-Behavior-GOAP-Planning-Integration.md`

GOAP adds intelligent planning on top of ABML:

```yaml
goals:
  meet_basic_needs:
    priority: 100
    conditions:
      energy: ">= 0.3"
      hunger: "<= 0.7"

behaviors:
  eat_meal:
    goap_action:
      preconditions:
        npc_hunger: "> 0.6"
        current_funds: "> 5"
      effects:
        npc_hunger: "-0.8"
        current_funds: "-5"
      cost: 2
    actions:
      - purchase_food: ...
      - consume_food: ...
```

**Key Pattern**: Each behavior becomes a GOAP action with:
- **Preconditions**: Required world state
- **Effects**: State changes after execution
- **Cost**: Action selection weight

### 1.3 Actor Plugin Boundary

**Source**: `ACTORS_ISSUES.md`

Critical architectural separation:

| Actor Plugin (Infrastructure) | Behavior Plugin (Logic) |
|-------------------------------|-------------------------|
| Actor lifecycle management | ABML YAML parsing/compilation |
| State persistence (hot/cold) | Behavior stack merging |
| Event routing | Context variable interpolation |
| Node placement/scaling | GOAP planning & action sequencing |
| Personal event channels | Perception interpretation |
| Service invocation access | Memory significance & consolidation |
| Turn-based processing | Attention allocation |

**Actor plugin routes events. Behavior plugin interprets them.**

---

## Part 2: Use Case Analysis

### 2.1 NPC Behaviors (ABML Core Use Case)

**Already well-designed in arcadia-kb.** Key characteristics:
- Autonomous execution based on world state
- Goal-driven action selection (GOAP)
- Cultural adaptation layers
- Memory and relationship tracking
- Service integration for effects

**Example Flow**:
```
Perception Event → Attention Filter → Memory Check → Goal Evaluation →
Action Planning (GOAP) → Action Execution → State Update → Memory Storage
```

### 2.2 Dialogue Systems

**Traditional dialogue systems (Yarn Spinner, Ink) features**:
- Branching narrative flow
- Variable tracking and conditions
- Character-specific dialogue
- Localization support
- Rich text markup

**How it maps to ABML**:
```yaml
# Dialogue as behavior
dialogue:
  blacksmith_greeting:
    triggers:
      - event: "player_initiates_conversation"
      - condition: "${relationship.player >= 0}"

    flow:
      - speak:
          character: "${npc.id}"
          text: "Welcome to my forge, ${player.name}!"
          emotion: "${calculate_greeting_emotion()}"

      - choice:
          prompt: "What do you need?"
          options:
            - label: "I need a sword forged"
              condition: "${player.has_materials('iron', 5)}"
              then:
                - goto: { dialogue: "commission_sword" }
            - label: "Just browsing"
              then:
                - speak:
                    text: "Take your time."
                - goto: { dialogue: "end_conversation" }
            - label: "[Ask about the dragon attack]"
              condition: "${world.events.dragon_attack_recent}"
              then:
                - goto: { dialogue: "discuss_dragon" }
```

**Key Insight**: Dialogue is just a specialized behavior where:
- Actions = speech/animation
- Triggers = conversation initiation
- State = relationship/knowledge/world events
- Branching = player choice + NPC personality

### 2.3 Cutscene/Timeline Systems

**Traditional cutscene features**:
- Choreographed camera movements
- Character positioning and animation
- Synchronized audio/music
- Parallel action tracks
- Trigger points and skip logic

**How it maps to ABML**:
```yaml
cutscene:
  dragon_arrival:
    metadata:
      duration: 45  # seconds
      skippable: true
      save_point: true

    timeline:
      # Parallel tracks that sync at markers
      tracks:
        camera:
          - at: 0
            action: pan_to:
              target: "village_gate"
              duration: 3
          - at: 3
            action: shake:
              intensity: 0.5
              duration: 2

        audio:
          - at: 0
            action: play_music:
              track: "ominous_approach"
              fade_in: 2
          - at: 3
            action: play_sfx:
              sound: "dragon_roar"

        characters:
          - at: 2
            action: animate:
              target: "mayor"
              animation: "look_up_alarmed"
          - at: 3
            parallel:
              - animate:
                  target: "crowd_npcs"
                  animation: "scatter_panic"
              - spawn:
                  entity: "dragon"
                  location: "sky_entrance"

      markers:
        - name: "dragon_visible"
          at: 3
          trigger_behaviors:
            - behavior: "initiate_dragon_protocol"
              target: "mayor"
```

**Key Insight**: Cutscenes are behaviors with:
- Time-based triggers instead of event-based
- Parallel execution tracks
- Synchronization markers
- Actors as targets rather than self

### 2.4 Dialplans (Telephony)

**Traditional dialplan features (FreeSWITCH/Asterisk/SignalWire)**:
- Call routing based on caller/callee
- IVR menu systems
- DTMF input handling
- Recording, conferencing
- External API integration

**SWML (SignalWire Markup Language) was an inspiration for ABML!**

**How it maps to ABML**:
```yaml
dialplan:
  inbound_call:
    triggers:
      - event: "call.received"
      - condition: "${call.direction == 'inbound'}"

    actions:
      - answer:
          timeout: 30

      - play:
          url: "welcome_message.wav"

      - gather:
          input: "dtmf"
          timeout: 10
          result_variable: "user_input"

      - cond:
          - when: "${user_input == '1'}"
            then:
              - connect:
                  destination: "sales_queue"
          - when: "${user_input == '2'}"
            then:
              - connect:
                  destination: "support_queue"
          - when: "${user_input == '0'}"
            then:
              - connect:
                  destination: "operator"
          - else:
              - play:
                  text: "Invalid option. Goodbye."
              - hangup

      - on_error:
          - play:
              text: "We're experiencing difficulties."
          - hangup
```

**Key Insight**: Dialplans are behaviors where:
- Events = call signaling (ring, answer, hangup)
- Actions = media operations (play, record, gather)
- State = call state, DTMF input, caller info
- Branching = user input + business logic

### 2.5 Agent Cognition Pipeline

**The meta-use-case that ties everything together:**

```yaml
# Agent cognition as behavior orchestration
cognition:
  process_perception:
    triggers:
      - event: "perception.received"

    pipeline:
      # Stage 1: Attention filtering
      - filter_attention:
          input: "${perception.raw_data}"
          attention_budget: "${agent.attention_remaining}"
          result_variable: "filtered_perceptions"

      # Stage 2: Memory integration
      - query_memories:
          service: "memory_service"
          method: "find_relevant"
          parameters:
            context: "${filtered_perceptions}"
            limit: 10
          result_variable: "relevant_memories"

      # Stage 3: Significance assessment
      - for_each:
          variable: "perception"
          collection: "${filtered_perceptions}"
          do:
            - assess_significance:
                perception: "${perception}"
                memories: "${relevant_memories}"
                relationships: "${agent.relationships}"
                result_variable: "significance_scores"

      # Stage 4: Memory storage
      - cond:
          - when: "${significance_scores.max > 0.7}"
            then:
              - store_memory:
                  service: "memory_service"
                  type: "experience"
                  data: "${filtered_perceptions}"
                  significance: "${significance_scores}"

      # Stage 5: Intention formation
      - update_intentions:
          perceptions: "${filtered_perceptions}"
          memories: "${relevant_memories}"
          current_goals: "${agent.goals}"
          result_variable: "new_intentions"

      # Stage 6: Trigger behavior reevaluation
      - cond:
          - when: "${new_intentions.requires_replan}"
            then:
              - trigger_goap_replan:
                  goals: "${agent.goals}"
                  world_state: "${world.current_state}"
```

**Key Insight**: Cognition is the orchestrator that:
- Receives all sensory events
- Filters through attention
- Queries and updates memory
- Forms intentions/goals
- Triggers behavior replanning

---

## Part 3: Common Patterns Identified

### 3.1 Fundamental Building Blocks

All use cases share these primitives:

| Primitive | Description | Examples |
|-----------|-------------|----------|
| **Event** | Something that triggers processing | perception, call_received, player_choice, timer |
| **Condition** | State check that gates execution | `${energy > 0.5}`, `${call.from == 'VIP'}` |
| **Action** | Something that happens | speak, play, animate, service_call |
| **State Change** | Modification to world/agent state | memory update, relationship change |
| **Branch** | Conditional flow control | cond/when/then/else |
| **Sequence** | Ordered action execution | action lists |
| **Parallel** | Concurrent action execution | timeline tracks |
| **Loop** | Repeated execution | for_each |
| **Goto/Call** | Flow transfer | goto behavior, call subroutine |

### 3.2 Execution Models

| Model | Description | Use Cases |
|-------|-------------|-----------|
| **Reactive** | Respond to events as they occur | Dialplans, dialogue triggers |
| **Proactive** | Goal-driven autonomous action | NPC behaviors with GOAP |
| **Scripted** | Time-driven choreography | Cutscenes, tutorials |
| **Hybrid** | Mix of above | Agent cognition pipeline |

### 3.3 State Scopes

| Scope | Lifetime | Examples |
|-------|----------|----------|
| **Local** | Single execution | loop variables, result_variable |
| **Entity** | Entity lifetime | NPC stats, call state |
| **Session** | Session duration | conversation context, call session |
| **Persistent** | Permanent | memories, relationships, world state |

### 3.4 Service Integration Pattern

All domains need to call external services:

```yaml
# Universal service call pattern
- service_call:
    service: "service_name"
    method: "method_name"
    parameters:
      param1: "${variable}"
      param2: "literal"
    result_variable: "result"
    timeout: 5000
    on_error:
      - fallback_action
```

---

## Part 4: Proposed Unified Language Architecture

### 4.1 Core Language: ABML v2.0

Building on existing ABML design with extensions for all use cases:

```yaml
# ABML v2.0 Document Structure
version: "2.0.0"

metadata:
  id: "unique_identifier"
  type: "behavior" | "dialogue" | "cutscene" | "dialplan" | "cognition"
  priority: 50

context:
  # Variable definitions with type hints
  variables:
    energy:
      source: "${entity.stats.energy}"
      type: float
    caller_vip:
      source: "${call.from in vip_list}"
      type: boolean

  # Required world state
  requirements:
    service_available: true

  # Service dependencies
  services:
    - name: "economy_service"
      required: true

# Event subscriptions
events:
  - pattern: "perception.*"
    handler: process_perception
  - pattern: "call.received"
    handler: handle_incoming_call

# Goal definitions (for GOAP-enabled behaviors)
goals:
  goal_name:
    priority: 100
    conditions:
      state_key: ">= value"

# Behavior/flow definitions
flows:
  flow_name:
    triggers:
      - event: "event_pattern"
      - condition: "${expression}"
      - time_range: "HH:MM-HH:MM"
      - schedule: "0 9 * * MON"  # cron syntax

    # Optional GOAP action definition
    goap:
      preconditions:
        state: "value"
      effects:
        state: "new_value"
      cost: 5

    # Action sequence
    actions:
      - action_type:
          param: value

# Timeline definitions (for cutscenes)
timelines:
  timeline_name:
    duration: 45
    tracks:
      track_name:
        - at: 0
          action: action_type
          params: {}

# Error handling
errors:
  service_unavailable:
    - fallback_action
```

### 4.2 Expression Language

Unified expression syntax for conditions and interpolation:

```yaml
# Variable access
${entity.property}
${entity.nested.property}
${array[0]}
${map["key"]}

# Operators
${a + b}              # Arithmetic
${a == b}             # Comparison
${a && b}             # Logical
${a in collection}    # Membership
${a ?? default}       # Null coalescing

# Function calls
${length(array)}
${format("{0} items", count)}
${random(1, 10)}
${now()}

# Ternary
${condition ? value_if_true : value_if_false}
```

### 4.3 Action Categories

| Category | Actions | Primary Use Case |
|----------|---------|------------------|
| **Control** | cond, for_each, goto, call, return, parallel, wait | All |
| **Entity** | animate, move, look_at, spawn, despawn | NPC, Cutscene |
| **Speech** | speak, narrate, choice, listen | Dialogue |
| **Audio** | play, stop, fade, record | Cutscene, Dialplan |
| **Camera** | pan, zoom, shake, cut | Cutscene |
| **Telephony** | answer, hangup, transfer, gather, conference | Dialplan |
| **Memory** | remember, forget, query_memory | Cognition |
| **Service** | service_call, publish_event | All |
| **State** | set, increment, clear | All |

### 4.4 Execution Engine Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    ABML Runtime Engine                       │
├─────────────────────────────────────────────────────────────┤
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐       │
│  │   Parser     │  │  Compiler    │  │   Executor   │       │
│  │  (YAML→AST)  │──│ (AST→Plan)   │──│  (Plan→Run)  │       │
│  └──────────────┘  └──────────────┘  └──────────────┘       │
│         │                │                  │                │
│         ▼                ▼                  ▼                │
│  ┌──────────────────────────────────────────────────────┐   │
│  │              Expression Evaluator                     │   │
│  │  (Variable resolution, operators, functions)          │   │
│  └──────────────────────────────────────────────────────┘   │
│                          │                                   │
├──────────────────────────┼───────────────────────────────────┤
│                          ▼                                   │
│  ┌──────────────────────────────────────────────────────┐   │
│  │              Action Handler Registry                  │   │
│  │  ┌────────┐ ┌────────┐ ┌────────┐ ┌────────┐        │   │
│  │  │Control │ │ Entity │ │ Speech │ │Service │ ...    │   │
│  │  └────────┘ └────────┘ └────────┘ └────────┘        │   │
│  └──────────────────────────────────────────────────────┘   │
│                          │                                   │
├──────────────────────────┼───────────────────────────────────┤
│                          ▼                                   │
│  ┌─────────────────┐  ┌─────────────────┐                   │
│  │  State Store    │  │  Service Mesh   │                   │
│  │  (lib-state)    │  │  (lib-mesh)     │                   │
│  └─────────────────┘  └─────────────────┘                   │
└─────────────────────────────────────────────────────────────┘
```

---

## Part 5: Open Questions & Decisions

### 5.1 Language Design Questions

#### 1. Expression Language Choice - DECIDED: Custom Parser

**Decision**: Build a custom expression parser using Parlot or hand-written recursive descent.

**Rationale**:
- Perfect syntax match to ABML design (`${var.path}` notation)
- Full control over error messages (designer-friendly)
- Domain-specific functions: `has_item("sword")`, `distance_to(target)`
- Integration with ABML concepts (service results, entity lookups)
- No syntax compromises or preprocessing layers

**Syntax Design**:
```yaml
# Conditions use ${} with dot-path access
condition: "${npc.stats.energy > 0.5 && relationship[player.id] >= 0.7}"

# Text uses {{ }} Liquid syntax (via Fluid for templates)
speak:
  text: "Hello {{ customer.name | capitalize }}!"

# Computed values in action parameters
action:
  damage: "${base_damage * (1 + skill_bonus)}"
```

**Implementation**: ~2-4 weeks, using Parlot parser combinator library.

---

#### 2. Type System - DECIDED: Structural with Dynamic Extensions

**Decision**: Structural typing with schema imports from OpenAPI, plus explicit `any`/`ExtensionData` escape hatches.

**Rationale**:
- Self-documenting behaviors
- Compile-time error detection
- No duplication - imports existing OpenAPI service schemas
- Dynamic buckets for flexibility where needed
- Aligns with Bannou's schema-first architecture

**Type Categories**:
| Category | Source | Example |
|----------|--------|---------|
| **Primitives** | Built-in | `int`, `float`, `bool`, `string` |
| **Service Types** | OpenAPI imports | `ShopStatus`, `CraftedItem` |
| **Behavior Types** | ABML schema definitions | `NpcMood`, `PatrolState` |
| **Collections** | Generic | `list<Recipe>`, `map<string, int>` |
| **Enums** | Inline or imported | `enum(happy, sad, angry)` |
| **Dynamic** | Explicit opt-in | `any`, `ExtensionData` (map<string, any>) |

**Schema Import Pattern**:
```yaml
imports:
  - schema: "economy-api.yaml"
    types: [ShopStatus, OrderDetails]
  - schema: "crafting-api.yaml"
    types: [Recipe, CraftedItem]

context:
  variables:
    shop_status:
      type: ShopStatus  # From imported schema
    custom_data:
      type: ExtensionData  # Dynamic bucket
```

**Validation Pipeline**: YAML Parse → Schema Resolution → Type Validation → Semantic Validation → Compiled Output

**Testing Strategy**: "Must work" behavior fixtures as regression tests, schema change detection tests.

**Null Safety**: Null propagation operators (`?.` and `??`):
```yaml
# Safe navigation - returns null if target is null
condition: "${target?.health < 0.5}"

# Null coalescing - provide default
damage: "${weapon?.damage ?? 10}"

# Chained
greeting: "${npc?.relationship[player.id]?.title ?? 'stranger'}"
```

---

#### 3. Compilation vs Interpretation - DECIDED: Hybrid (Interpret + Bytecode Expressions)

**Decision**: Tree-walking interpretation for control flow, bytecode compilation for expressions.

**Rationale**:
- Control flow is rarely the bottleneck (I/O dominates: service calls 1-10ms, Redis 0.5-2ms)
- Expressions execute frequently and benefit from compilation
- Hot-reload stays simple (just reload YAML, rebuild AST)
- Debugging remains straightforward (step through nodes)
- Can add full bytecode VM later if profiling shows need

**Architecture**:
```csharp
public class ABMLExecutor
{
    // Control flow: simple tree-walking interpretation
    public async Task ExecuteAsync(ActionNode node, ExecutionContext ctx)
    {
        switch (node)
        {
            case SequenceNode seq:
                foreach (var child in seq.Children)
                    await ExecuteAsync(child, ctx);
                break;
            case ConditionNode cond:
                if (_exprEvaluator.Evaluate<bool>(cond.Expression, ctx))
                    await ExecuteAsync(cond.ThenBranch, ctx);
                break;
            // ...
        }
    }

    // Expressions: compiled to bytecode, cached
    private readonly ExpressionCache _exprCache = new();
}

public class ExpressionEvaluator
{
    public T Evaluate<T>(string expr, ExecutionContext ctx)
    {
        var compiled = _cache.GetOrAdd(expr, e => Compile(e));
        return compiled.Execute<T>(ctx);
    }
}
```

**Performance Expectations**:
- Control flow: ~10-50μs per node (acceptable given I/O dominance)
- Expressions: ~1-5μs per evaluation (bytecode VM)
- Upgrade path: Full bytecode VM if profiling reveals bottlenecks

---

#### 4. GOAP Integration Level - DECIDED: Hybrid (Annotations + External Planning)

**Decision**: GOAP metadata as optional annotations in ABML, planning logic in Behavior service.

**Rationale**:
- ABML stays clean for simpler use cases (cutscenes, dialogues, dialplans)
- Client applications can consume ABML without GOAP complexity
- Single source of truth - behavior and its GOAP metadata together
- Planning service can evolve independently (swap algorithms, optimize)
- Matches Actor/Behavior plugin boundary from ACTORS_ISSUES.md
- Separation of concerns: ABML = "what to do", Behavior service = "when to do it"

**ABML Syntax** (annotations are optional):
```yaml
behaviors:
  craft_sword:
    # Optional GOAP metadata - ignored by non-planning consumers
    goap:
      preconditions:
        forge_lit: true
        materials.iron: ">= 5"
      effects:
        inventory.swords: "+1"
        materials.iron: "-5"
      cost: 10

    # Core behavior - always present
    actions:
      - service_call: crafting.forge_item
      - update_inventory: { swords: "+1" }
```

**Architecture Split**:
| Component | Responsibility |
|-----------|---------------|
| ABML Compiler | Validates GOAP annotations, compiles behaviors |
| ABML Runtime | Executes action sequences |
| Behavior Service | Extracts GOAP metadata, runs A* planner, orchestrates execution |
| Client Apps | Can use ABML directly for cutscenes/dialogues (ignore GOAP) |

**Use Case Support**:
- **NPC Behaviors**: Full GOAP planning via Behavior service
- **Cutscenes/Timelines**: Direct ABML execution, no GOAP needed
- **Dialogues**: Direct ABML execution with branching
- **Dialplans**: Direct ABML execution for call routing
- **Game Editor**: Load ABML without Behavior service dependency

#### 5. Parallel Execution Model - PROPOSED: Multi-Channel with Named Sync Points

**Leading Proposal**: Multi-channel architecture with named emit/wait_for sync points, QTE branching, and document composition.

**Why This Model**: Addresses multiple identified needs simultaneously:
- Cutscene choreography (camera + actors + audio in parallel)
- Cross-timeline dependencies (wait for camera before hero acts)
- QTE branching (success/failure paths that reconverge)
- Document composition (build complex sequences from reusable pieces)
- Visual editor compatibility (horizontal tracks, vertical sync lines)
- Debuggability (named sync points are explicit, deadlocks detectable)

**Core Concepts**:
- **Channel**: A named sequence of steps that executes independently
- **Emit**: Declares a named sync point that other channels can wait for
- **Wait_for**: Blocks until specified sync points are reached
- **Branch**: Transfers control to another channel (possibly in another document)
- **QTE**: Special step type with success/failure branching based on player input

**Rationale**:
- Captures the "multiple independent things happening simultaneously" pattern
- Named sync points are explicit, debuggable, and visualizable
- Maps naturally to visual timeline editors (horizontal tracks, vertical sync lines)
- QTE branching is first-class, not bolted on
- Document composition allows building complex cinematics from smaller pieces

**Core Syntax**:
```yaml
# Multi-channel cutscene example
channels:
  camera:
    - fade_in: { duration: 1s }
    - move_to: { shot: wide_throne_room, duration: 2s }
    - emit: establishing_complete          # Named sync point
    - wait_for: @hero.at_mark              # Wait for another channel
    - crane_up: { reveal: boss, duration: 3s }
    - emit: boss_revealed

  hero:
    - wait_for: @camera.establishing_complete    # Cross-channel dependency
    - walk_to: { mark: hero_mark_1, speed: cautious }
    - emit: at_mark
    - wait_for: @camera.boss_revealed
    - emote: { expression: shock, duration: 1s }
    - speak: "You... it was you all along!"

  audio:
    - play: { track: ambient_throne_room, fade_in: 2s }
    - wait_for: @camera.boss_revealed
    - crossfade_to: { track: boss_theme, duration: 1s }
```

**Multi-Condition Waits**:
```yaml
# Barrier synchronization - wait for multiple channels
timeline_c:
  - wait_for: [a_ready, b_ready]     # All must complete (barrier)
  - combined_action

# Race condition - first one wins
timeline_c:
  - wait_for:
      any_of: [player_input, timeout_reached]
  - handle_result

# With timeout fallback
timeline_c:
  - wait_for:
      all_of: [a_ready, b_ready]
      timeout: 5s
      on_timeout: emergency_fallback
```

**QuickTime Events with Branching**:
```yaml
channels:
  qte_handler:
    - wait_for: @boss.attack_starting
    - qte:
        type: button_prompt
        prompt: "Press X to dodge!"
        button: X
        window: 1.5s
        on_success:
          branch: dodge_success_channel
        on_failure:
          branch: dodge_failure_channel

  dodge_success_channel:
    - hero.dodge_roll: { direction: left }
    - boss.attack_miss: true
    - emit: qte_resolved
    - branch: continue_fight

  dodge_failure_channel:
    - hero.take_hit: { animation: stagger_back }
    - apply_damage: { amount: 50, type: dark }
    - emit: qte_resolved
    - branch: continue_fight

  continue_fight:
    - wait_for: @qte_handler.qte_resolved
    - ... # Both branches converge here
```

**Document Composition**:
```yaml
# Large cinematics from smaller pieces
imports:
  - file: camera_library.abml
    as: camera
  - file: character_blocking.abml
    as: blocking

channels:
  main:
    - start_document:
        file: opening_pan.abml
        wait_for_complete: true

    - start_channels:
        - camera.dramatic_reveal
        - blocking.hero_entrance

    - wait_for: [@camera.reveal_complete, @blocking.hero_at_mark]

    - start_document:
        file: boss_fight_intro.abml
        pass_context:
          hero_health: "${hero.current_hp}"
```

**Document Types and Runtime Behavior**:
| Type | Deterministic | GOAP | Parallel Channels |
|------|--------------|------|-------------------|
| `behavior` | No (random ok) | Yes | Optional |
| `cutscene` | Yes (seeded) | No | Primary use |
| `dialogue` | Player-driven | No | Optional |
| `dialplan` | Event-driven | No | Some |
| `timeline` | Yes | No | Primary use |

**Visual Editor Mapping**:
```
Time →
┌────────────────────────────────────────────────────────┐
│ camera │▓▓fade_in▓▓│▓▓▓move_to▓▓▓│●│▓▓crane_up▓▓│     │
│        │           │             │e│            │     │
├────────┼───────────┼─────────────┼s┼────────────┼─────┤
│ hero   │           │ ●wait       │t│▓▓walk_to▓▓▓│●emit│
│        │           │             │a│            │     │
├────────┼───────────┼─────────────┼b┼────────────┼─────┤
│ audio  │▓▓▓ambient▓▓▓▓▓▓▓▓▓▓▓▓▓▓│l│●wait       │▓▓▓▓▓│
└────────┴───────────┴─────────────┴─┴────────────┴─────┘
Legend: ▓ = action, ● = sync point, vertical = dependency
```

**Runtime State Model**:
```csharp
class DocumentExecution
{
    Dictionary<string, ChannelState> Channels;
    HashSet<string> EmittedSyncPoints;
    Dictionary<string, List<WaitingChannel>> WaitRegistry;
    Stack<BranchContext> BranchStack;
    Dictionary<string, object> Context;
}

class ChannelState
{
    int CurrentStepIndex;
    ChannelStatus Status; // Running, WaitingForSync, Complete, Branched
    string WaitingFor;
}
```

**Deadlock Detection**: Runtime detects circular waits:
```
Channel A waits for @B.ready
Channel B waits for @A.ready
→ DEADLOCK detected
```

**GOAP Integration at This Level**:
- For NPC behaviors: GOAP chooses which ABML document to execute
- For cutscenes: No GOAP at runtime (deterministic)
- For editors: GOAP can validate sequences (check preconditions met)

---

### 5.2 Runtime Questions

1. **Execution Context**:
   - One executor per entity?
   - Pooled executors?
   - How to handle long-running flows (cutscenes)?

2. **Concurrency Model** - Partially addressed by Q5:
   - Parallel tracks sync via named emit/wait_for points
   - Service call timeouts configurable per-action
   - State updates gated by sync points prevent races

3. **Debugging and Tooling**:
   - How to debug running behaviors?
   - What telemetry to emit?
   - IDE support (VS Code extension)?

#### 6. Blocking/Waiting Semantics - DECIDED: Intent-Based, Handler-Interpreted

**Decision**: ABML expresses intent (do, wait, wait-with-timeout). Handlers interpret based on runtime context. No inherent timeouts unless authored. Language is transport-agnostic.

**Key Insight**: ABML is a language specification, not tied to any specific runtime. Questions like "how do actions reach the game client?" are implementation details for specific ABML consumers (Bannou behavior service, Unity editor, test harness, etc.).

**What ABML Expresses (Spec Level)**:

| Pattern | Syntax | Behavior |
|---------|--------|----------|
| Fire-and-forget | `- action: {...}` | Handler executes, ABML continues |
| Await completion | `- action: {..., await: completion}` | Block until handler signals done |
| Await input | `- choice: {...}` | Block until input received (indefinitely) |
| Await with timeout | `- qte: {..., timeout: 1.5s}` | Block with explicit failure path |
| Await sync point | `- wait_for: @channel.point` | Block until sync point emitted |

**What Handlers Decide (Implementation Level)**:
- Transport mechanism (events, RPC, direct API calls)
- How to communicate with game clients
- Connection management and reconnection
- System-level timeouts (if any)
- Batching optimizations for cutscenes

**Handler Contract**:
- Fire-and-forget: Execute action, return immediately
- Await completion: Execute action, signal completion when done
- ABML doesn't prescribe *how* the handler accomplishes this

**Design Principle**: A player can sit on a dialogue choice for hours. ABML doesn't care - that's valid. Timeouts are opt-in game design choices, not system defaults.

**Deferred to Implementation**: Bannou-specific decisions (lib-messaging events, batching strategies, etc.) are behavior plugin design questions, not ABML spec questions.

---

### 5.3 Remaining Integration Questions (Implementation-Level)

These are deferred to specific runtime implementations:

1. **Bannou Behavior Plugin**:
   - Event transport via lib-messaging
   - Batching strategy for cutscenes
   - Service mesh integration patterns

2. **Telephony Integration**:
   - SignalWire/FreeSWITCH adapter
   - Real-time audio stream handling
   - DTMF input mapping to ABML choices

3. **Memory System Integration**:
   - Cognition pipeline ↔ memory service interaction
   - Memory consolidation triggers
   - Semantic/episodic/procedural memory APIs

---

## Part 6: .NET Implementation Considerations

### 6.1 Recommended Libraries

| Purpose | Library | Notes |
|---------|---------|-------|
| YAML Parsing | YamlDotNet | Already in use, well-supported |
| Expression Parsing | Custom or Parlot | Parlot is fast parser combinator |
| Template Rendering | Fluid | Liquid-compatible, extensible |
| State Machine | Stateless | For execution state tracking |
| JSON Schema | NJsonSchema | For validation |

### 6.2 Service Architecture

```csharp
// Core interfaces
public interface IABMLParser
{
    ABMLDocument Parse(string yaml);
    ValidationResult Validate(ABMLDocument doc);
}

public interface IABMLCompiler
{
    CompiledBehavior Compile(ABMLDocument doc);
    CompiledBehavior CompileStack(IEnumerable<ABMLDocument> docs);
}

public interface IABMLExecutor
{
    Task ExecuteAsync(CompiledBehavior behavior, ExecutionContext context);
    void RegisterActionHandler(string actionType, IActionHandler handler);
}

public interface IExpressionEvaluator
{
    object Evaluate(string expression, IVariableScope scope);
    bool EvaluateCondition(string expression, IVariableScope scope);
}
```

### 6.3 Action Handler Pattern

```csharp
public interface IActionHandler
{
    string ActionType { get; }
    Task<ActionResult> ExecuteAsync(
        ActionDefinition action,
        ExecutionContext context,
        CancellationToken ct);
}

// Example implementation
public class ServiceCallHandler : IActionHandler
{
    public string ActionType => "service_call";

    public async Task<ActionResult> ExecuteAsync(
        ActionDefinition action,
        ExecutionContext context,
        CancellationToken ct)
    {
        var serviceName = action.Parameters["service"].ToString();
        var methodName = action.Parameters["method"].ToString();
        var parameters = ResolveParameters(action.Parameters["parameters"], context);

        var result = await _mesh.InvokeAsync(serviceName, methodName, parameters, ct);

        if (action.Parameters.TryGetValue("result_variable", out var varName))
        {
            context.SetVariable(varName.ToString(), result);
        }

        return ActionResult.Success(result);
    }
}
```

---

## Part 7: Next Steps

### Immediate Actions

1. **Review DSL Standards Research** (awaiting agent completion)
   - Incorporate behavior tree patterns
   - Learn from Ink/Yarn Spinner dialogue approaches
   - Analyze SWML/dialplan patterns

2. **Prototype Expression Evaluator**
   - Define expression grammar
   - Build parser with Parlot or custom
   - Test with ABML samples

3. **Define Action Handler Interface**
   - Core control flow handlers
   - Service integration handler
   - Entity action handler (for game engine)

### Short-Term Goals

1. **ABML v2.0 Schema Definition**
   - Complete TypeSpec schema
   - Generate JSON Schema for validation
   - Create VS Code extension for syntax highlighting

2. **Parser Implementation**
   - YamlDotNet-based parser
   - Schema validation
   - Helpful error messages

3. **Minimal Executor**
   - Basic action execution
   - Variable scope management
   - Condition evaluation

### Medium-Term Goals

1. **GOAP Integration**
   - Planning service
   - Goal evaluation
   - Dynamic replanning

2. **Timeline Support**
   - Parallel track execution
   - Synchronization markers
   - Time-based triggers

3. **Telephony Integration**
   - SignalWire/SWML bridge
   - Call event handling
   - DTMF processing

---

## Appendix A: Example Unified Documents

### A.1 NPC Behavior (Classic ABML)
See arcadia-kb examples for comprehensive NPC behavior definitions.

### A.2 Dialogue Flow
```yaml
version: "2.0.0"
metadata:
  id: "merchant_haggle"
  type: "dialogue"

context:
  variables:
    player_gold: "${player.inventory.gold}"
    merchant_disposition: "${npc.relationships[player.id].disposition}"
    base_price: 100

flows:
  initiate_haggle:
    triggers:
      - event: "dialogue.haggle_requested"

    actions:
      - speak:
          character: "${npc.id}"
          text: "Ah, you want to negotiate? Very well..."

      - cond:
          - when: "${merchant_disposition > 0.7}"
            then:
              - speak:
                  text: "For you, my friend, I'll start at ${base_price * 0.9}."
              - set:
                  variable: "current_price"
                  value: "${base_price * 0.9}"
          - else:
              - speak:
                  text: "The price is ${base_price}. Take it or leave it."
              - set:
                  variable: "current_price"
                  value: "${base_price}"

      - goto: { flow: "haggle_loop" }

  haggle_loop:
    actions:
      - choice:
          prompt: "Your offer?"
          options:
            - label: "How about ${current_price * 0.8}?"
              then:
                - goto: { flow: "counter_offer", args: { offered: "${current_price * 0.8}" } }
            - label: "I'll pay ${current_price}"
              then:
                - goto: { flow: "accept_deal" }
            - label: "Never mind"
              then:
                - goto: { flow: "end_haggle" }
```

### A.3 Cutscene with Multi-Channel and QTE
```yaml
version: "2.0.0"
metadata:
  id: "boss_reveal_with_qte"
  type: "cutscene"
  deterministic: true

imports:
  - schema: "character-api.yaml"
    types: [EmoteType, DamageType]

channels:
  camera:
    - fade_in: { duration: 1s, from: black }
    - establishing_shot: { target: throne_room, duration: 3s }
    - emit: room_established
    - wait_for: @hero.entered_room
    - track: { subject: hero, style: following, duration: 5s }
    - wait_for: @boss.stands_up
    - dramatic_zoom: { target: boss_face, duration: 2s }
    - emit: boss_closeup_complete

  hero:
    - wait_for: @camera.room_established
    - enter_through_door: { door: main_entrance, style: cautious }
    - emit: entered_room
    - walk_to: { mark: confrontation_mark, duration: 4s }
    - wait_for: @camera.boss_closeup_complete
    - emote: { type: defiant, duration: 0.5s }
    - speak:
        line: "Your reign of terror ends today!"
        emotion: determined
    - emit: challenge_delivered

  boss:
    - seated_pose: { animation: imperious_wait }
    - wait_for: @hero.entered_room
    - subtle_reaction: { animation: slight_smile }
    - wait_for: @hero.challenge_delivered
    - stand_from_throne: { style: menacing, duration: 2s }
    - emit: stands_up
    - speak:
        line: "Fool. You have no idea what power you face."
        emotion: contemptuous
    - begin_attack_windup: { duration: 1s }
    - emit: attack_starting

  audio:
    - set_ambience: { track: ominous_throne_room, volume: 0.3 }
    - wait_for: @boss.stands_up
    - crossfade: { to: boss_theme, duration: 2s }
    - wait_for: @boss.attack_starting
    - play_sfx: { sound: energy_charging, spatial: boss }

  qte_handler:
    - wait_for: @boss.attack_starting
    - qte:
        type: reaction_prompt
        prompt_key: "qte.dodge"
        input: { button: X, window: 1.2s }
        on_success:
          branch: qte_success
        on_failure:
          branch: qte_failure

  qte_success:
    - hero.dodge: { direction: left, style: acrobatic }
    - boss.attack_miss: { stumble: slight }
    - emit: qte_resolved
    - branch: post_qte_continue

  qte_failure:
    - hero.take_hit: { animation: knocked_back, damage: { amount: 75, type: dark } }
    - boss.speak: { line: "Pathetic.", emotion: dismissive }
    - emit: qte_resolved
    - branch: post_qte_continue

  post_qte_continue:
    - wait_for: @qte_handler.qte_resolved
    - camera.reset_to: { shot: medium_two_shot }
    - emit: cutscene_complete
    - transition_to:
        file: boss_fight_gameplay.abml
        pass_context:
          hero_health: "${hero.current_hp}"
```

---

## Appendix B: Research Sources

### Internal Documentation
- `/home/lysander/repos/arcadia-kb/06 - Technical Architecture/NPC-Behavior-ABML-Specification.md`
- `/home/lysander/repos/arcadia-kb/06 - Technical Architecture/NPC-Behavior-GOAP-Planning-Integration.md`
- `/home/lysander/repos/arcadia-kb/07 - Implementation Guides/ABML-Implementation-Examples.md`
- `/home/lysander/repos/arcadia-kb/05 - NPC AI Design/Character-Behavior-Systems-Architecture.md`
- `/home/lysander/repos/arcadia-kb/05 - NPC AI Design/Distributed Agent Architecture.md`
- `/home/lysander/repos/bannou/docs/planning/ACTORS_ISSUES.md`

### External Research (Completed)
See `DSL_STANDARDS_RESEARCH.md` for comprehensive analysis including:

**Behavior Trees**:
- BehaviorTree.CPP XML format is the de facto standard (BTCPP_format="4")
- .NET libraries available: BehaviourTree (NuGet), EugenyN/BehaviorTrees (.NET 9.0)
- Key pattern: Sequence, Selector, Parallel composites with action/condition leaves

**Dialogue Systems**:
- Yarn Spinner: Node-based, screenplay format, `$var` syntax, `<<commands>>`
- Ink: Knot-based (`===`), diverts (`->`), compiles to JSON, more powerful logic
- Both support branching, variables, and game engine integration

**Telephony (SWML)**:
- SignalWire SWML directly inspired ABML design
- Section-based organization with named blocks
- `%{variable}` interpolation, `switch`/`case` routing
- AI agent integration as first-class method

**State Machines**:
- XState (JS): Actor-based, SCXML-compliant, visualization tools
- SCXML: W3C standard with parallel states, history, external communication
- Stateless (.NET): Best library - fluent API, hierarchical, async, DOT visualization

**AI Agent Frameworks**:
- Semantic Kernel (.NET): Multi-agent orchestration, plugin system
- LangGraph: Stateful workflows with graph abstraction
- Common patterns: observe-plan-act loops, handoff, multi-agent collaboration

**Recommended .NET Stack**:
| Purpose | Library |
|---------|---------|
| YAML Parsing | YamlDotNet 16.3.0 |
| Expressions | NCalc (cached, extensible) |
| Templates | Fluid (high-performance Liquid) |
| State Machine | Stateless 5.20.0 |
| AI Integration | Semantic Kernel |

---

*Document created as initial research synthesis. Will be updated with external research findings and refined through design iteration.*
