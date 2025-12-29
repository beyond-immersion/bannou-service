# ABML - Arcadia Behavior Markup Language

> **Version**: 2.0
> **Status**: Implemented (414 tests passing)
> **Location**: `bannou-service/Abml/`

ABML is a YAML-based domain-specific language for authoring event-driven, stateful sequences of actions. It powers NPC behaviors, dialogue systems, cutscenes, and agent cognition in Arcadia.

---

## Table of Contents

1. [Overview](#1-overview)
2. [Document Structure](#2-document-structure)
3. [Type System](#3-type-system)
4. [Expression Language](#4-expression-language)
5. [Control Flow](#5-control-flow)
6. [Channels and Parallelism](#6-channels-and-parallelism)
7. [Actions](#7-actions)
8. [Flows](#8-flows)
9. [GOAP Integration](#9-goap-integration)
10. [Events](#10-events)
11. [Context and Variables](#11-context-and-variables)
12. [Error Handling](#12-error-handling)
13. [Runtime Architecture](#13-runtime-architecture)
14. [Examples](#14-examples)

---

## 1. Overview

### 1.1 What is ABML?

ABML enables authoring of:

- **NPC Behaviors** - Autonomous character decision-making
- **Dialogue Systems** - Branching conversations with choices
- **Cutscenes/Timelines** - Choreographed multi-track sequences
- **Dialplans** - Call routing and IVR flows
- **Agent Cognition** - Perception, memory, action pipelines

### 1.2 Design Philosophy

1. **YAML-first**: Human-readable, version-control friendly
2. **Intent-based**: Express what should happen, not how
3. **Handler-agnostic**: Runtime interprets actions; ABML doesn't prescribe transport
4. **Composable**: Documents can import and reference other documents
5. **Type-safe**: Structural typing with schema imports
6. **Parallel-native**: Multi-channel execution with named synchronization points

### 1.3 What ABML Is Not

- **Not a programming language**: No arbitrary computation, loops are bounded
- **Not runtime-specific**: Doesn't assume Bannou, Unity, or any particular backend
- **Not a state machine definition**: Though it can express state-like patterns
- **Not GOAP**: GOAP metadata is optional annotations; planning happens externally

---

## 2. Document Structure

### 2.1 Top-Level Schema

```yaml
version: "2.0"

metadata:
  id: string              # Unique identifier
  type: document_type     # behavior | dialogue | cutscene | dialplan | timeline
  description: string?    # Human-readable description
  tags: string[]?         # Categorization tags
  deterministic: bool?    # Hint for runtime (default: false)

imports:
  - schema: string        # OpenAPI schema file
    types: string[]       # Types to import
  - file: string          # Another ABML document
    as: string            # Namespace alias

context:
  variables: { ... }      # Variable definitions with types
  requirements: { ... }   # Required world state to execute
  services: [ ... ]       # Service dependencies

events:
  - pattern: string       # Event pattern to subscribe to
    handler: string       # Flow/channel to invoke

goals:                    # Optional GOAP goal definitions
  goal_name: { ... }

flows:                    # Named action sequences (for behaviors/dialogues)
  flow_name: { ... }

channels:                 # Parallel execution tracks (for cutscenes/timelines)
  channel_name: [ ... ]

on_error: flow_name       # Document-level error handler
```

### 2.2 Document Types

| Type | Primary Structure | Use Case |
|------|------------------|----------|
| `behavior` | `flows` | NPC autonomous behaviors, reactive logic |
| `dialogue` | `flows` | Branching conversations, player choices |
| `cutscene` | `channels` | Choreographed sequences, cinematics |
| `dialplan` | `flows` | Call routing, IVR menus |
| `timeline` | `channels` | Generic parallel sequences |

The `type` field is a hint to runtimes and tooling. The actual structure (flows vs channels) determines execution model.

---

## 3. Type System

### 3.1 Primitive Types

| Type | Description | Literal Examples |
|------|-------------|------------------|
| `bool` | Boolean | `true`, `false` |
| `int` | Integer | `42`, `-7`, `0` |
| `float` | Floating point | `3.14`, `-0.5`, `1.0` |
| `string` | Text | `"hello"`, `'world'` |
| `null` | Null value | `null` |
| `duration` | Time duration | `1s`, `500ms`, `2.5m` |

### 3.2 Collection Types

```yaml
# List type
items:
  type: list<Recipe>

# Map type
inventory:
  type: map<string, int>
```

### 3.3 Schema Imports

Types can be imported from OpenAPI schemas:

```yaml
imports:
  - schema: "economy-api.yaml"
    types: [ShopStatus, OrderDetails, Currency]
  - schema: "character-api.yaml"
    types: [EmoteType, AnimationState]
```

Imported types are validated against the schema at compile time.

### 3.4 Inline Type Definitions

```yaml
context:
  variables:
    mood:
      type: enum(happy, sad, angry, neutral)

    patrol_state:
      type: object
      properties:
        current_waypoint: int
        direction: enum(forward, backward)
        paused: bool
```

### 3.5 Dynamic Types

When flexibility is needed:

```yaml
context:
  variables:
    custom_data:
      type: any                    # Fully dynamic

    extension_data:
      type: ExtensionData          # Alias for map<string, any>
```

### 3.6 Null Safety

ABML supports null propagation operators:

| Operator | Name | Behavior |
|----------|------|----------|
| `?.` | Safe navigation | Returns `null` if left side is null |
| `??` | Null coalescing | Returns right side if left is null |

```yaml
# Safe navigation
condition: "${target?.health < 0.5}"

# Null coalescing
damage: "${weapon?.damage ?? 10}"

# Chained
greeting: "${npc?.relationship[player.id]?.title ?? 'stranger'}"
```

---

## 4. Expression Language

### 4.1 Expression Syntax

Expressions are enclosed in `${}` and support:

```yaml
# Variable access
"${entity.property}"
"${entity.nested.property}"
"${array[0]}"
"${map['key']}"
"${map[variable_key]}"

# Arithmetic
"${a + b}"
"${price * quantity}"
"${health - damage}"
"${total / count}"
"${index % 2}"

# Comparison
"${a == b}"
"${a != b}"
"${a > b}"
"${a >= b}"
"${a < b}"
"${a <= b}"

# Logical
"${a && b}"
"${a || b}"
"${!condition}"

# Membership
"${item in collection}"
"${key in map}"

# Ternary
"${condition ? value_if_true : value_if_false}"

# Null operators
"${value ?? default}"
"${obj?.property}"
```

### 4.2 Built-in Functions

```yaml
# Collection functions
"${length(array)}"
"${contains(collection, item)}"
"${first(array)}"
"${last(array)}"
"${keys(map)}"
"${values(map)}"

# String functions
"${format('{0} has {1} gold', name, amount)}"
"${upper(text)}"
"${lower(text)}"
"${trim(text)}"
"${split(text, delimiter)}"
"${join(array, delimiter)}"

# Math functions
"${min(a, b)}"
"${max(a, b)}"
"${abs(value)}"
"${floor(value)}"
"${ceil(value)}"
"${round(value)}"
"${random()}"              # 0.0 to 1.0
"${random(min, max)}"      # Integer in range

# Time functions
"${now()}"                 # Current timestamp
"${duration(seconds)}"     # Create duration

# Type functions
"${type_of(value)}"        # Returns type name
"${is_null(value)}"
"${is_empty(collection)}"
```

### 4.3 Template Syntax (Text Interpolation)

For text output (dialogue, messages), use Liquid-style `{{}}` syntax:

```yaml
speak:
  text: "Hello {{ customer.name | capitalize }}!"

narrate:
  text: |
    {{ hero.name }} entered the {{ location.name }}.
    {% if location.is_dark %}
    It was pitch black inside.
    {% endif %}
```

Template filters: `capitalize`, `upcase`, `downcase`, `truncate`, `strip`, `default`, `date`

### 4.4 Expression vs Template

| Context | Syntax | Use |
|---------|--------|-----|
| Conditions | `"${...}"` | Boolean evaluation |
| Computed values | `"${...}"` | Numbers, references |
| Display text | `"{{ ... }}"` | User-visible strings |

```yaml
- cond:
    - when: "${player.gold >= item.price}"  # Expression
      then:
        - speak:
            text: "That'll be {{ item.price }} gold."  # Template
```

---

## 5. Control Flow

### 5.1 Sequences

Actions execute in order by default:

```yaml
actions:
  - action_one: { ... }
  - action_two: { ... }
  - action_three: { ... }
```

### 5.2 Conditionals

```yaml
- cond:
    - when: "${condition_a}"
      then:
        - action_if_a
    - when: "${condition_b}"
      then:
        - action_if_b
    - else:
        - action_otherwise
```

Multiple `when` clauses are evaluated in order; first match executes.

### 5.3 Loops

```yaml
# Iterate over collection
- for_each:
    variable: item
    collection: "${inventory.items}"
    do:
      - process_item: { item: "${item}" }

# Bounded iteration (not arbitrary while loops)
- repeat:
    times: 3
    do:
      - attempt_action: { ... }
```

### 5.4 Flow Control

```yaml
# Jump to another flow
- goto: { flow: "other_flow_name" }

# Jump with arguments
- goto:
    flow: "process_order"
    args:
      order_id: "${current_order.id}"
      priority: high

# Call and return (subroutine)
- call: { flow: "validate_input" }
- continue_after_validation: { ... }

# Early exit
- return: { value: "${result}" }

# Branch to channel (in channel-based documents)
- branch: channel_name
```

---

## 6. Channels and Parallelism

### 6.1 Channel Definition

Channels are named sequences that execute in parallel:

```yaml
channels:
  camera:
    - fade_in: { duration: 1s }
    - move_to: { shot: wide, duration: 2s }
    - emit: shot_ready

  hero:
    - wait_for: @camera.shot_ready
    - walk_to: { mark: center_stage }
    - emit: hero_ready

  audio:
    - play: { track: ambient, fade_in: 2s }
    - wait_for: @hero.hero_ready
    - crossfade: { to: dramatic_theme }
```

### 6.2 Sync Points

**Emit**: Declare a named sync point

```yaml
- emit: point_name
```

**Wait**: Block until sync point(s) reached

```yaml
# Wait for single point
- wait_for: @channel.point_name

# Wait for multiple (barrier - all must complete)
- wait_for:
    signals:
      - @channel_a.signal1
      - @channel_b.signal2
    mode: all_of

# Wait for any (race - first one wins)
- wait_for:
    signals:
      - @player_input
      - @timeout_signal
    mode: any_of

# Wait with timeout
- wait_for:
    all_of: [a_ready, b_ready]
    timeout: 5s
    on_timeout: handle_timeout_channel
```

### 6.3 Cooperative Scheduling

Channels execute cooperatively on a single thread with deterministic interleaving:

```
Tick 1:  camera[0] -> actors[0] -> effects[0]
Tick 2:  camera[1] -> actors[1] -> effects[1]
Tick 3:  camera[2] -> actors[WAIT] -> effects[2]  (actors waiting)
Tick 4:  camera[3] -> effects[3]                  (actors still waiting)
Tick 5:  camera[EMIT] -> actors[2] -> effects[4]  (actors wake)
```

### 6.4 Deadlock Detection

The scheduler detects when all active channels are waiting for signals that will never arrive:

```yaml
# DEADLOCK - detected at runtime
channels:
  a:
    - wait_for: @b.signal  # A waits for B
  b:
    - wait_for: @a.signal  # B waits for A
```

### 6.5 Channel Scope Isolation

Each channel has its own scope (child of document scope). Channels can:
- Read from document scope
- Write locally without affecting other channels
- Communicate via sync points, not shared variables

### 6.6 Branching Between Channels

```yaml
channels:
  main:
    - setup_scene
    - qte:
        prompt: "Press X!"
        timeout: 1.5s
        on_success:
          branch: success_path
        on_failure:
          branch: failure_path

  success_path:
    - hero.dodge: { style: acrobatic }
    - emit: qte_resolved
    - branch: continue_main

  failure_path:
    - hero.take_hit: { damage: 50 }
    - emit: qte_resolved
    - branch: continue_main
```

---

## 7. Actions

### 7.1 Action Syntax

```yaml
# Simple action
- action_name: { param: value }

# Action with multiple params
- action_name:
    param_one: value
    param_two: "${expression}"
    param_three:
      nested: data

# Fire-and-forget (default)
- animate: { target: hero, animation: wave }

# Await completion
- animate:
    target: hero
    animation: dramatic_death
    await: completion
```

### 7.2 Control Actions (Built-in)

| Action | Purpose |
|--------|---------|
| `cond` | Conditional branching |
| `for_each` | Collection iteration |
| `repeat` | Bounded repetition |
| `goto` | Flow transfer |
| `call` | Subroutine call |
| `return` | Early exit |
| `branch` | Channel transfer |
| `emit` | Sync point declaration |
| `wait_for` | Sync point wait |
| `set` | Variable assignment |
| `parallel` | Inline parallel block |
| `log` | Debug logging |

### 7.3 Variable Actions

```yaml
# Set variable
- set:
    variable: current_target
    value: "${nearest_enemy}"

# Increment/decrement
- increment: { variable: counter, by: 1 }
- decrement: { variable: attempts, by: 1 }

# Clear/unset
- clear: { variable: temp_data }
```

### 7.4 Domain Actions (Handler-Provided)

These are interpreted by the runtime handler:

#### Entity Actions
```yaml
- animate: { target: entity_id, animation: name, ... }
- move_to: { target: entity_id, destination: location, ... }
- look_at: { target: entity_id, look_target: other_entity, ... }
- spawn: { entity_type: type, location: loc, ... }
- despawn: { target: entity_id }
```

#### Speech Actions
```yaml
- speak:
    character: "${npc.id}"
    text: "Hello, {{ player.name }}!"
    emotion: friendly

- narrate:
    text: "The door creaked open slowly..."

- choice:
    prompt: "What do you say?"
    options:
      - label: "Option A"
        condition: "${can_choose_a}"
        then: [ ... ]
      - label: "Option B"
        then: [ ... ]
```

#### Camera Actions
```yaml
- camera.cut_to: { shot: shot_name }
- camera.pan: { target: location, duration: 2s }
- camera.zoom: { level: 1.5, duration: 1s }
- camera.shake: { intensity: 0.5, duration: 0.5s }
```

#### Audio Actions
```yaml
- audio.play: { track: music_name, fade_in: 2s }
- audio.stop: { track: music_name, fade_out: 1s }
- audio.sfx: { sound: sound_name, spatial: entity_id }
```

#### Service Actions
```yaml
- service_call:
    service: economy_service
    method: purchase_item
    parameters:
      item_id: "${selected_item.id}"
      quantity: 1
    result_variable: purchase_result
    on_error:
      - handle_purchase_error
```

### 7.5 Handler Contract

Actions follow this contract with handlers:

| Execution Mode | ABML Syntax | Handler Behavior |
|----------------|-------------|------------------|
| Fire-and-forget | `- action: {...}` | Execute, return immediately |
| Await completion | `- action: {..., await: completion}` | Execute, signal when done |

The handler decides HOW to execute (events, RPC, direct calls). ABML only expresses WHAT should happen.

---

## 8. Flows

### 8.1 Flow Definition

Flows are named, triggerable action sequences:

```yaml
flows:
  morning_routine:
    triggers:
      - time_range: "06:00-09:00"
      - condition: "${npc.energy > 0.5}"

    actions:
      - wake_up_animation
      - check_schedule
      - goto: { flow: "start_daily_tasks" }

  start_daily_tasks:
    actions:
      - cond:
          - when: "${npc.profession == 'blacksmith'}"
            then:
              - goto: { flow: "blacksmith_work" }
          - when: "${npc.profession == 'merchant'}"
            then:
              - goto: { flow: "merchant_work" }
```

### 8.2 Flow-Level Error Handling

```yaml
flows:
  risky_operation:
    on_error:
      - log: { message: "Operation failed: ${_error.message}" }
      - set: { variable: _error_handled, value: "${true}" }

    actions:
      - dangerous_action: { ... }
      - next_action: { ... }  # Continues if _error_handled is true
```

### 8.3 Triggers

```yaml
triggers:
  # Event trigger
  - event: "perception.player_nearby"

  # Condition (evaluated periodically or on state change)
  - condition: "${npc.hunger > 0.7}"

  # Time-based
  - time_range: "09:00-17:00"
  - schedule: "0 9 * * MON-FRI"  # Cron syntax

  # Combined (all must be true)
  - event: "shop.customer_entered"
    condition: "${shop.is_open}"
    time_range: "08:00-20:00"
```

---

## 9. GOAP Integration

GOAP (Goal-Oriented Action Planning) metadata are **optional annotations** on ABML flows. This allows:
- Same ABML documents to work without GOAP (cutscenes, dialogues)
- GOAP-aware systems to extract planning metadata
- Single source of truth for behaviors and their GOAP properties

### 9.1 Goal Definitions

```yaml
goals:
  stay_fed:
    priority: 100
    conditions:
      hunger: "<= 0.3"

  earn_money:
    priority: 50
    conditions:
      gold: ">= ${npc.daily_target}"
```

### 9.2 GOAP Annotations on Flows

```yaml
flows:
  eat_meal:
    goap:
      preconditions:
        hunger: "> 0.5"
        gold: ">= 5"
        location: "near_tavern"
      effects:
        hunger: "-0.7"    # Delta: subtract 0.7
        gold: "-5"        # Delta: subtract 5
      cost: 3

    actions:
      - go_to: { destination: tavern }
      - purchase: { item: meal }
      - consume: { item: meal }
```

### 9.3 GOAP Condition Syntax

```
condition := operator value
operator  := ">" | ">=" | "<" | "<=" | "==" | "!="
value     := number | boolean | string

Examples:
"> 0.6"     -> Greater than 0.6
">= 5"      -> Greater than or equal to 5
"== true"   -> Equals true
"!= 'idle'" -> Not equals "idle"
```

### 9.4 Effect Syntax

```
effect := value | delta
delta  := ("+" | "-") number

Examples:
"-0.8"      -> Subtract 0.8 from current value
"+5"        -> Add 5 to current value
"0.5"       -> Set to 0.5 (absolute)
"tavern"    -> Set to "tavern" (string)
```

### 9.5 GOAP is External

ABML only stores GOAP metadata. Actual planning (A* search, plan generation) happens in an external service:

1. **ABML Parser** extracts GOAP annotations
2. **Planning Service** builds action graph, runs planner
3. **Executor** receives plan, executes ABML flows in order

---

## 10. Events

### 10.1 Event Subscriptions

```yaml
events:
  - pattern: "perception.*"
    handler: process_perception

  - pattern: "dialogue.initiated"
    handler: start_conversation

  - pattern: "combat.damage_received"
    handler: react_to_damage
    condition: "${event.target == npc.id}"
```

### 10.2 Event Publishing

```yaml
# Reference a typed event (preferred)
- publish:
    event: NpcStateChangedEvent
    data:
      npc_id: "${npc.id}"
      new_state: "${current_state}"

# Topic with inline payload
- publish:
    topic: "npc.state_changed"
    payload:
      npc_id: "${npc.id}"
      new_state: "${current_state}"
```

---

## 11. Context and Variables

### 11.1 Context Definition

```yaml
context:
  variables:
    # Bound to external state
    energy:
      source: "${entity.stats.energy}"
      type: float

    # Local variable with default
    interaction_count:
      type: int
      default: 0

    # Computed variable
    is_tired:
      type: bool
      computed: "${energy < 0.3}"

  requirements:
    has_voice_lines: true
    is_adult_npc: true

  services:
    - name: economy_service
      required: true
    - name: relationship_service
      required: false
```

### 11.2 Variable Scopes

| Scope | Lifetime | Access |
|-------|----------|--------|
| `local` | Current flow execution | `${variable}` |
| `document` | Document lifetime | `${document.variable}` |
| `entity` | Entity lifetime | `${entity.property}` |
| `world` | Persistent world state | `${world.property}` |

### 11.3 Scope Behavior

| Operation | Behavior |
|-----------|----------|
| `SetValue` | Search up chain, update if found, else create locally |
| `SetLocalValue` | Always create/update in current scope (shadows parent) |
| `SetGlobalValue` | Always write to root scope |
| `GetValue` | Search up chain, return first match or null |

### 11.4 Loop Variable Isolation

Loop variables use local scope to prevent clobbering:

```yaml
# Outer 'i' is preserved after loop
- set: { variable: i, value: 100 }
- for_each:
    variable: i           # Shadows outer 'i'
    collection: "${items}"
    do:
      - log: "${i}"       # Logs item values
- log: "${i}"             # Still 100, not clobbered
```

---

## 12. Error Handling

### 12.1 Three-Level Error Chain

Errors are handled through a hierarchical chain:

```
Action-level on_error -> Flow-level on_error -> Document-level on_error
```

### 12.2 Error Handler Syntax

```yaml
version: "2.0"
metadata:
  id: error_handling_example

# Document-level error handler
on_error: handle_fatal_error

flows:
  start:
    # Flow-level error handler
    on_error:
      - log: { message: "Flow error: ${_error.message}", level: error }
      - set: { variable: _error_handled, value: "${true}" }

    actions:
      # Action-level error handling
      - query_service:
          target: economy
          on_error:
            - log: "Service unavailable, using cached data"
            - set: { variable: _error_handled, value: "${true}" }

  handle_fatal_error:
    actions:
      - log: { message: "Fatal: ${_error.message}", level: error }
```

### 12.3 The `_error_handled` Pattern

By default, error handling **stops** flow execution. To continue (try/catch semantics), explicitly set `_error_handled = true`:

| Scenario | `_error_handled` | Result |
|----------|------------------|--------|
| Default (not set) | N/A | Flow completes but **stops** |
| Set to `true` | `${true}` | Flow **continues** to next action |
| Set to `false` | `${false}` | Flow completes but **stops** |

**Graceful Degradation Pattern:**

```yaml
flows:
  start:
    actions:
      - query_environment:
          bounds: "${combat_bounds}"
          result_variable: affordances
      - for_each:
          variable: affordance
          collection: "${affordances}"
          do:
            - process: "${affordance}"
    on_error:
      - log: "Query failed, using cached data"
      - set: { variable: affordances, value: "${cached_affordances}" }
      - set: { variable: _error_handled, value: "${true}" }  # Continue!
```

### 12.4 Error Context Variable

When an error occurs, `_error` is set with error details:

```yaml
on_error:
  - log: { message: "Error in ${_error.flow}: ${_error.message}" }
  - log: { message: "Failed action: ${_error.action}" }
```

---

## 13. Runtime Architecture

### 13.1 Execution Model

ABML uses a hybrid execution model:

- **Control flow**: Tree-walking interpretation (simple, debuggable)
- **Expressions**: Register-based bytecode VM (fast, cached)

This optimizes for:
- I/O dominance (service calls >> expression evaluation)
- Hot reload capability (re-parse YAML, rebuild AST)
- Debuggability (step through nodes)

### 13.2 Register-Based VM

The expression VM uses a register-based architecture for efficient null-safety handling:

```
Expression: ${entity?.health < 0.3 ? 'critical' : 'stable'}

Bytecode:
  0: LoadVar R0, "entity"
  1: JumpIfNull R0, 8
  2: GetProp R1, R0, "health"
  3: LoadConst R2, 0.3
  4: Lt R3, R1, R2
  5: JumpIfFalse R3, 8
  6: LoadConst R0, "critical"
  7: Return R0
  8: LoadConst R0, "stable"
  9: Return R0
```

**Why register-based?**
- Clean null-safety patterns (no stack management)
- Value reuse without DUP/SWAP gymnastics
- Debuggable state (named registers vs anonymous stack)

### 13.3 Instruction Set Summary

| Category | OpCodes |
|----------|---------|
| Loads | `LoadConst`, `LoadVar`, `LoadNull`, `LoadTrue`, `LoadFalse` |
| Property | `GetProp`, `GetPropSafe`, `GetIndex`, `GetIndexSafe` |
| Arithmetic | `Add`, `Sub`, `Mul`, `Div`, `Mod`, `Neg` |
| Comparison | `Eq`, `Ne`, `Lt`, `Le`, `Gt`, `Ge` |
| Logical | `Not`, `And`, `Or` |
| Control | `Jump`, `JumpIfTrue`, `JumpIfFalse`, `JumpIfNull`, `JumpIfNotNull` |
| Functions | `Call`, `CallArgs` |
| Null | `Coalesce` |
| String | `In`, `Concat` |
| Result | `Return` |

### 13.4 Compiled Expression Structure

```csharp
public sealed class CompiledExpression
{
    public required Instruction[] Code { get; init; }
    public required object[] Constants { get; init; }
    public required int RegisterCount { get; init; }
    public required string SourceText { get; init; }
}
```

### 13.5 File Structure

```
bannou-service/Abml/
├── Compiler/
│   ├── ExpressionCompiler.cs
│   ├── OpCode.cs
│   ├── RegisterAllocator.cs
│   └── InstructionBuilder.cs
│
├── Runtime/
│   ├── ExpressionVm.cs
│   └── ExpressionCache.cs
│
├── Parser/
│   ├── ExpressionParser.cs
│   ├── DocumentParser.cs
│   └── ExpressionLexer.cs
│
├── Documents/
│   ├── AbmlDocument.cs
│   ├── Flow.cs
│   └── Actions/
│
├── Execution/
│   ├── DocumentExecutor.cs
│   ├── ExecutionContext.cs
│   ├── CallStack.cs
│   ├── Channel/
│   │   ├── ChannelScheduler.cs
│   │   └── ChannelState.cs
│   └── Handlers/
│       ├── SetHandler.cs
│       ├── CallHandler.cs
│       ├── GotoHandler.cs
│       ├── CondHandler.cs
│       ├── ForEachHandler.cs
│       ├── RepeatHandler.cs
│       ├── LogHandler.cs
│       ├── EmitHandler.cs
│       ├── WaitForHandler.cs
│       └── SyncHandler.cs
│
└── Expressions/
    ├── VariableScope.cs
    ├── ExpressionEvaluator.cs
    └── AbmlTypeCoercion.cs
```

---

## 14. Examples

### 14.1 NPC Behavior

```yaml
version: "2.0"

metadata:
  id: blacksmith_daily_routine
  type: behavior
  description: "Blacksmith NPC daily work routine"

imports:
  - schema: "crafting-api.yaml"
    types: [Recipe, CraftedItem]

context:
  variables:
    energy:
      source: "${entity.stats.energy}"
      type: float
    shop_open:
      source: "${entity.shop.is_open}"
      type: bool

goals:
  maintain_shop:
    priority: 80
    conditions:
      shop_stocked: true

  rest_when_tired:
    priority: 100
    conditions:
      energy: ">= 0.3"

flows:
  open_shop:
    triggers:
      - time_range: "08:00-09:00"
      - condition: "${!shop_open}"

    goap:
      preconditions:
        energy: "> 0.3"
      effects:
        shop_open: true
      cost: 1

    actions:
      - animate: { animation: unlock_door }
      - set: { variable: shop_open, value: true }
      - speak:
          text: "The forge is open for business!"
      - goto: { flow: "tend_shop" }

  tend_shop:
    triggers:
      - condition: "${shop_open && energy > 0.2}"

    actions:
      - cond:
          - when: "${shop.customers.length > 0}"
            then:
              - goto: { flow: "serve_customer" }
          - when: "${shop.needs_restock}"
            then:
              - goto: { flow: "craft_items" }
          - else:
              - idle: { animation: tend_forge, duration: 30s }
```

### 14.2 Dialogue

```yaml
version: "2.0"

metadata:
  id: merchant_haggle
  type: dialogue

context:
  variables:
    base_price:
      type: int
      default: 100
    current_offer:
      type: int
    player_charisma:
      source: "${player.stats.charisma}"
      type: int

flows:
  start_haggle:
    triggers:
      - event: "dialogue.haggle_requested"

    actions:
      - set:
          variable: current_offer
          value: "${base_price}"

      - speak:
          character: "${npc.id}"
          text: "So, you want to negotiate? The price is {{ current_offer }} gold."

      - choice:
          prompt: "Your response:"
          options:
            - label: "How about {{ current_offer * 0.7 | round }}?"
              then:
                - goto:
                    flow: counter_offer
                    args:
                      offered: "${floor(current_offer * 0.7)}"

            - label: "I'll pay full price."
              then:
                - goto: { flow: accept_deal }

            - label: "Never mind."
              then:
                - goto: { flow: end_haggle }

  counter_offer:
    actions:
      - cond:
          - when: "${args.offered >= base_price * 0.8}"
            then:
              - speak:
                  text: "Hmm... you drive a hard bargain. Fine, {{ args.offered }} gold."
              - set: { variable: current_offer, value: "${args.offered}" }
              - goto: { flow: accept_deal }

          - when: "${player_charisma > 15 && args.offered >= base_price * 0.6}"
            then:
              - speak:
                  text: "You're quite persuasive... {{ args.offered + 10 }} and we have a deal."
              - set: { variable: current_offer, value: "${args.offered + 10}" }
              - goto: { flow: accept_deal }

          - else:
              - speak:
                  text: "Ha! You insult me. {{ floor(current_offer * 0.9) }} is my final offer."
              - set: { variable: current_offer, value: "${floor(current_offer * 0.9)}" }
              - goto: { flow: start_haggle }
```

### 14.3 Cutscene with QTE

```yaml
version: "2.0"

metadata:
  id: boss_reveal
  type: cutscene
  deterministic: true

imports:
  - schema: "character-api.yaml"
    types: [EmoteType, DamageType]

channels:
  camera:
    - fade_in: { duration: 1s, from: black }
    - establishing_shot: { target: throne_room, duration: 3s }
    - emit: room_established
    - wait_for: @hero.entered
    - track: { subject: hero, style: following, duration: 4s }
    - wait_for: @boss.stands
    - dramatic_zoom: { target: boss, duration: 2s }
    - emit: boss_closeup

  hero:
    - wait_for: @camera.room_established
    - enter_through_door: { door: main_gate, style: cautious }
    - emit: entered
    - walk_to: { mark: confrontation_point, duration: 3s }
    - wait_for: @camera.boss_closeup
    - emote: { type: defiant }
    - speak:
        line: "Your reign ends today!"
        emotion: determined
    - emit: challenge_delivered

  boss:
    - seated_pose: { animation: imperious }
    - wait_for: @hero.entered
    - subtle_smirk: { duration: 1s }
    - wait_for: @hero.challenge_delivered
    - stand_from_throne: { style: menacing, duration: 2s }
    - emit: stands
    - speak:
        line: "Fool. You have no idea what you face."
        emotion: contemptuous
    - charge_attack: { duration: 1.5s }
    - emit: attack_starting

  audio:
    - ambience: { track: ominous_throne, volume: 0.3 }
    - wait_for: @boss.stands
    - crossfade: { to: boss_theme, duration: 2s }
    - wait_for: @boss.attack_starting
    - sfx: { sound: energy_charge, spatial: boss }

  qte_sequence:
    - wait_for: @boss.attack_starting
    - qte:
        type: reaction
        prompt_key: "qte.dodge"
        input: { button: X, window: 1.2s }
        on_success:
          branch: dodge_success
        on_failure:
          branch: dodge_failure

  dodge_success:
    - hero.dodge: { direction: left, style: acrobatic }
    - boss.attack_miss: { stumble: true }
    - camera.action_shot: { focus: hero }
    - audio.sfx: { sound: whoosh }
    - emit: qte_done
    - branch: post_qte

  dodge_failure:
    - hero.take_hit:
        animation: knocked_back
        damage: { amount: 75, type: dark }
    - camera.shake: { intensity: 0.6 }
    - audio.sfx: { sound: impact }
    - boss.speak:
        line: "Pathetic."
        emotion: dismissive
    - emit: qte_done
    - branch: post_qte

  post_qte:
    - wait_for: @qte_sequence.qte_done
    - camera.reset: { shot: medium_two_shot }
    - parallel:
        - hero.recover_stance: { duration: 1s }
        - boss.ready_stance: { duration: 1s }
    - emit: cutscene_complete
    - transition:
        to: boss_fight_gameplay.abml
        pass_context:
          hero_hp: "${hero.current_hp}"
```

### 14.4 Complete Behavior with All Features

```yaml
version: "2.0"
metadata:
  id: "blacksmith_morning_routine"

flows:
  start:
    actions:
      - set: { variable: energy, value: 100 }
      - call: { flow: check_inventory }
      - cond:
          - when: "${needs_supplies}"
            then:
              - call: { flow: go_to_market }
          - else:
            - call: { flow: open_shop }

  check_inventory:
    actions:
      - set: { variable: iron_count, value: "${entity.inventory.iron ?? 0}" }
      - set: { variable: needs_supplies, value: "${iron_count < 5}" }

  go_to_market:
    actions:
      - log: { message: "Heading to market for supplies" }

  open_shop:
    actions:
      - log: { message: "Opening shop for business" }

# Multi-channel cutscene
channels:
  camera:
    - wait_for: @actors.positions_set
    - log: { message: "Camera: Starting crane shot" }
    - emit: crane_complete

  actors:
    - log: { message: "Actors: Moving to marks" }
    - emit: positions_set
    - wait_for: @camera.crane_complete
    - log: { message: "Actors: Starting dialogue" }
```

---

## Appendix A: Bannou Implementation Requirements

When implementing ABML handlers within the Bannou service architecture, the following constraints from the [Tenets](../reference/TENETS.md) apply:

### A.1 Infrastructure Libs (Tenet 4)

All ABML action handlers MUST use infrastructure abstractions:

| Action Type | Required Implementation |
|-------------|------------------------|
| `service_call` | Use `IMeshInvocationClient` or generated clients via lib-mesh |
| `publish` | Use `IMessageBus.PublishAsync()` via lib-messaging |
| State access | Use `IStateStore<T>` via lib-state |

**Forbidden**: Direct Redis, MySQL, RabbitMQ, or HTTP client usage in handlers.

### A.2 Typed Events (Tenet 5)

Event publishing handlers MUST use typed event models:

```csharp
// CORRECT
await _messageBus.PublishAsync("npc.state_changed", new NpcStateChangedEvent
{
    NpcId = npcId,
    NewState = newState
});

// FORBIDDEN
await _messageBus.PublishAsync("npc.state_changed", new { npc_id = npcId });
```

### A.3 JSON Serialization (Tenet 20)

All serialization within Bannou handlers MUST use `BannouJson`:

```csharp
var compiled = BannouJson.Deserialize<CompiledDocument>(yamlJson);
var serialized = BannouJson.Serialize(executionState);
```

### A.4 Non-Bannou Implementations

ABML is designed to be handler-agnostic. Non-Bannou implementations (Unity editor, standalone tools, test harnesses) are not bound by these constraints.

---

## Appendix B: Grammar Summary

```
document      := version metadata? imports? context? events? goals? flows? channels?
version       := "version:" string
metadata      := "metadata:" { id, type, description?, tags?, deterministic? }
imports       := "imports:" [ import_spec+ ]
import_spec   := { schema: string, types: [string+] } | { file: string, as: string }
context       := "context:" { variables?, requirements?, services? }
flows         := "flows:" { flow_name: flow_def }+
flow_def      := { triggers?: trigger+, goap?: goap_def, actions: action+, on_error?: action+ }
channels      := "channels:" { channel_name: action+ }+
action        := control_action | domain_action
control_action := cond | for_each | repeat | goto | call | return | branch | emit | wait_for | set
domain_action := action_name ":" ( value | { param: value }+ )
expression    := "${" expr "}"
template      := "{{" template_expr "}}"
```

---

*This document is the authoritative reference for ABML.*
