# ABML - Arcadia Behavior Markup Language

> **Version**: 2.0
> **Status**: Implemented (414 tests passing)
> **Location**: `bannou-service/Abml/`
> **Related**: [GOAP Guide](./GOAP.md)

ABML is a YAML-based domain-specific language for authoring event-driven, stateful sequences of actions. It powers NPC behaviors, dialogue systems, cutscenes, and agent cognition in Arcadia.

---

## Table of Contents

1. [Overview](#1-overview)
2. [Document Structure](#2-document-structure)
3. [Document Composition](#3-document-composition)
4. [Type System](#4-type-system)
5. [Expression Language](#5-expression-language)
6. [Control Flow](#6-control-flow)
7. [Channels and Parallelism](#7-channels-and-parallelism)
8. [Actions](#8-actions)
9. [Flows](#9-flows)
10. [GOAP Integration](#10-goap-integration)
11. [Events](#11-events)
12. [Context and Variables](#12-context-and-variables)
13. [Error Handling](#13-error-handling)
14. [Runtime Architecture](#14-runtime-architecture)
15. [Examples](#15-examples)
- [Appendix A: Bannou Implementation Requirements](#appendix-a-bannou-implementation-requirements)
- [Appendix B: Grammar Summary](#appendix-b-grammar-summary)
- [Appendix C: Potential Improvements](#appendix-c-potential-improvements)

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

## 3. Document Composition

ABML documents can import and reference other documents, enabling modular behavior libraries and code reuse.

### 3.1 Document Imports

```yaml
imports:
  # Schema imports - type validation only
  - schema: "economy-api.yaml"
    types: [ShopStatus, OrderDetails]

  # Document imports - reusable flows
  - file: "common/utilities.yml"
    as: utils

  - file: "./sibling.yml"
    as: sibling

  - file: "../shared/helpers.yml"
    as: helpers
```

**Import Types**:

| Type | Purpose | Syntax |
|------|---------|--------|
| Schema | Type validation from OpenAPI | `schema: "file.yaml"` with `types: [...]` |
| Document | Reusable flows from another ABML file | `file: "path.yml"` with `as: alias` |

### 3.2 Namespaced Flow References

Imported flows are accessed via their alias:

```yaml
imports:
  - file: "common/combat.yml"
    as: combat

flows:
  engage_enemy:
    actions:
      # Call an imported flow
      - call: { flow: combat.attack_sequence }

      # Goto an imported flow
      - goto: { flow: combat.retreat }
```

### 3.3 Relative Path Resolution

Import paths support relative resolution:

| Path | Resolution |
|------|------------|
| `utilities.yml` | Same directory as importing document |
| `./sibling.yml` | Explicit same directory |
| `../shared/file.yml` | Parent directory, then into `shared/` |
| `subdir/nested.yml` | Subdirectory relative to importing document |

### 3.4 Context-Relative Flow Resolution

When executing a flow from an imported document, flow references resolve relative to **that document's imports**, not the root document:

```yaml
# main.yml
imports:
  - file: "libs/ai.yml"
    as: ai

flows:
  start:
    - call: { flow: ai.process }  # Calls ai.yml's process flow
```

```yaml
# libs/ai.yml
imports:
  - file: "./helpers.yml"       # Relative to libs/
    as: helpers

flows:
  process:
    actions:
      - call: { flow: helpers.validate }  # Resolves to libs/helpers.yml
```

This enables truly modular libraries - imported documents don't need to know about the root document's structure.

### 3.5 Circular Import Detection

The document loader detects and rejects circular imports:

```yaml
# a.yml imports b.yml
# b.yml imports a.yml
# → CircularImportException at load time
```

### 3.6 Schema-Only Imports

Imports with only `schema:` (no `file:`) are used for type validation and are skipped during document loading:

```yaml
imports:
  # This import is for type validation only - no document loaded
  - schema: "character-api.yaml"
    types: [EmoteType, AnimationState]
```

### 3.7 Variable Scope Across Imports

When calling imported flows, variables follow standard scoping rules:

- **Existing variables propagate**: If the caller has a variable `x`, and the called flow modifies `x`, the change is visible to the caller
- **New variables stay local**: If the called flow creates a new variable `y`, it's not visible after the call returns

```yaml
# Caller sets 'mode' before calling
- set: { variable: mode, value: "aggressive" }
- call: { flow: ai.combat }
# If ai.combat modifies 'mode', the change persists here
```

---

## 4. Type System

### 4.1 Primitive Types

| Type | Description | Literal Examples |
|------|-------------|------------------|
| `bool` | Boolean | `true`, `false` |
| `int` | Integer | `42`, `-7`, `0` |
| `float` | Floating point | `3.14`, `-0.5`, `1.0` |
| `string` | Text | `"hello"`, `'world'` |
| `null` | Null value | `null` |
| `duration` | Time duration | `1s`, `500ms`, `2.5m` |

### 4.2 Collection Types

```yaml
# List type
items:
  type: list<Recipe>

# Map type
inventory:
  type: map<string, int>
```

### 4.3 Schema Imports

Types can be imported from OpenAPI schemas:

```yaml
imports:
  - schema: "economy-api.yaml"
    types: [ShopStatus, OrderDetails, Currency]
  - schema: "character-api.yaml"
    types: [EmoteType, AnimationState]
```

Imported types are validated against the schema at compile time.

### 4.4 Inline Type Definitions

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

### 4.5 Dynamic Types

When flexibility is needed:

```yaml
context:
  variables:
    custom_data:
      type: any                    # Fully dynamic

    extension_data:
      type: ExtensionData          # Alias for map<string, any>
```

### 4.6 Null Safety

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

## 5. Expression Language

### 5.1 Expression Syntax

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

### 5.2 Built-in Functions

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

### 5.3 Template Syntax (Text Interpolation)

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

### 5.4 Expression vs Template

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

## 6. Control Flow

### 6.1 Sequences

Actions execute in order by default:

```yaml
actions:
  - action_one: { ... }
  - action_two: { ... }
  - action_three: { ... }
```

### 6.2 Conditionals

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

### 6.3 Loops

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

### 6.4 Flow Control

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

## 7. Channels and Parallelism

### 7.1 Channel Definition

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

### 7.2 Sync Points

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

### 7.3 Cooperative Scheduling

Channels execute cooperatively on a single thread with deterministic interleaving:

```
Tick 1:  camera[0] -> actors[0] -> effects[0]
Tick 2:  camera[1] -> actors[1] -> effects[1]
Tick 3:  camera[2] -> actors[WAIT] -> effects[2]  (actors waiting)
Tick 4:  camera[3] -> effects[3]                  (actors still waiting)
Tick 5:  camera[EMIT] -> actors[2] -> effects[4]  (actors wake)
```

### 7.4 Deadlock Detection

The scheduler detects when all active channels are waiting for signals that will never arrive:

```yaml
# DEADLOCK - detected at runtime
channels:
  a:
    - wait_for: @b.signal  # A waits for B
  b:
    - wait_for: @a.signal  # B waits for A
```

### 7.5 Channel Scope Isolation

Each channel has its own scope (child of document scope). Channels can:
- Read from document scope
- Write locally without affecting other channels
- Communicate via sync points, not shared variables

### 7.6 Branching Between Channels

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

## 8. Actions

### 8.1 Action Syntax

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

### 8.2 Control Actions (Built-in)

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

### 8.3 Variable Actions

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

### 8.4 Domain Actions (Handler-Provided)

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

### 8.5 Handler Contract

Actions follow this contract with handlers:

| Execution Mode | ABML Syntax | Handler Behavior |
|----------------|-------------|------------------|
| Fire-and-forget | `- action: {...}` | Execute, return immediately |
| Await completion | `- action: {..., await: completion}` | Execute, signal when done |

The handler decides HOW to execute (events, RPC, direct calls). ABML only expresses WHAT should happen.

---

## 9. Flows

### 9.1 Flow Definition

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

### 9.2 Flow-Level Error Handling

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

### 9.3 Triggers

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

## 10. GOAP Integration

> **Full Documentation**: For comprehensive GOAP documentation including the A* planning algorithm, cognition integration, and best practices, see the [GOAP Guide](./GOAP.md).

GOAP (Goal-Oriented Action Planning) metadata are **optional annotations** on ABML flows. This allows:
- Same ABML documents to work without GOAP (cutscenes, dialogues)
- GOAP-aware systems to extract planning metadata
- Single source of truth for behaviors and their GOAP properties

### 10.1 Goal Definitions

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

### 10.2 GOAP Annotations on Flows

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

### 10.3 GOAP Condition Syntax

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

### 10.4 Effect Syntax

```
effect := value | delta
delta  := ("+" | "-") number

Examples:
"-0.8"      -> Subtract 0.8 from current value
"+5"        -> Add 5 to current value
"0.5"       -> Set to 0.5 (absolute)
"tavern"    -> Set to "tavern" (string)
```

### 10.5 GOAP is External

ABML only stores GOAP metadata. Actual planning (A* search, plan generation) happens in an external service:

1. **ABML Parser** extracts GOAP annotations
2. **Planning Service** builds action graph, runs planner
3. **Executor** receives plan, executes ABML flows in order

---

## 11. Events

### 11.1 Event Subscriptions

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

### 11.2 Event Publishing

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

## 12. Context and Variables

### 12.1 Context Definition

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

### 12.2 Variable Scopes

| Scope | Lifetime | Access |
|-------|----------|--------|
| `local` | Current flow execution | `${variable}` |
| `document` | Document lifetime | `${document.variable}` |
| `entity` | Entity lifetime | `${entity.property}` |
| `world` | Persistent world state | `${world.property}` |

### 12.3 Scope Behavior

| Operation | Behavior |
|-----------|----------|
| `SetValue` | Search up chain, update if found, else create locally |
| `SetLocalValue` | Always create/update in current scope (shadows parent) |
| `SetGlobalValue` | Always write to root scope |
| `GetValue` | Search up chain, return first match or null |

### 12.4 Loop Variable Isolation

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

## 13. Error Handling

### 13.1 Three-Level Error Chain

Errors are handled through a hierarchical chain:

```
Action-level on_error -> Flow-level on_error -> Document-level on_error
```

### 13.2 Error Handler Syntax

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

### 13.3 The `_error_handled` Pattern

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

### 13.4 Error Context Variable

When an error occurs, `_error` is set with error details:

```yaml
on_error:
  - log: { message: "Error in ${_error.flow}: ${_error.message}" }
  - log: { message: "Failed action: ${_error.action}" }
```

---

## 14. Runtime Architecture

### 14.1 Execution Model

ABML uses a hybrid execution model:

- **Control flow**: Tree-walking interpretation (simple, debuggable)
- **Expressions**: Register-based bytecode VM (fast, cached)

This optimizes for:
- I/O dominance (service calls >> expression evaluation)
- Hot reload capability (re-parse YAML, rebuild AST)
- Debuggability (step through nodes)

### 14.2 Register-Based VM

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

### 14.3 Instruction Set Summary

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

### 14.4 Compiled Expression Structure

```csharp
public sealed class CompiledExpression
{
    public required Instruction[] Code { get; init; }
    public required object[] Constants { get; init; }
    public required int RegisterCount { get; init; }
    public required string SourceText { get; init; }
}
```

### 14.5 File Structure

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
│   ├── ExpressionLexer.cs
│   ├── DocumentParser.cs
│   ├── DocumentLoader.cs         # Import resolution and composition
│   └── IDocumentResolver.cs      # Resolution interface + FileSystemDocumentResolver
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

## 15. Examples

### 15.1 NPC Behavior

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

### 15.2 Dialogue

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

### 15.3 Cutscene with QTE

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

### 15.4 Complete Behavior with All Features

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
# Document structure
document      := version metadata? imports? context? events? goals? flows? channels? on_error?
version       := "version:" STRING
metadata      := "metadata:" metadata_def
metadata_def  := { id: STRING, type: DOC_TYPE, description?: STRING,
                   tags?: STRING[], deterministic?: BOOL }
DOC_TYPE      := "behavior" | "dialogue" | "cutscene" | "dialplan" | "timeline"

# Imports
imports       := "imports:" import_spec+
import_spec   := { schema: STRING, types: STRING[] }
               | { file: STRING, as: STRING }

# Context
context       := "context:" { variables?: var_defs, requirements?: req_defs, services?: svc_defs }
var_defs      := { VAR_NAME: var_def }+
var_def       := { type: TYPE, default?: VALUE, source?: EXPR, computed?: EXPR }

# Events
events        := "events:" event_spec+
event_spec    := { pattern: STRING, handler: STRING, condition?: EXPR }

# Goals (GOAP)
goals         := "goals:" { GOAL_NAME: goal_def }+
goal_def      := { priority: NUMBER, conditions: { KEY: CONDITION }+ }

# Flows
flows         := "flows:" { FLOW_NAME: flow_def }+
flow_def      := { triggers?: trigger+, goap?: goap_def, actions: action+, on_error?: action+ }
trigger       := { event?: STRING, condition?: EXPR, time_range?: STRING, schedule?: STRING }
goap_def      := { preconditions: { KEY: CONDITION }+, effects: { KEY: EFFECT }+, cost: NUMBER }

# Channels
channels      := "channels:" { CHANNEL_NAME: action+ }+

# Actions
action        := control_action | domain_action
control_action := cond | for_each | repeat | goto | call | return | branch | emit | wait_for | set | log
cond          := "cond:" ( when_clause+ else_clause? )
when_clause   := { when: EXPR, then: action+ }
else_clause   := { else: action+ }
for_each      := "for_each:" { variable: STRING, collection: EXPR, do: action+ }
repeat        := "repeat:" { times: NUMBER, do: action+ }
goto          := "goto:" { flow: STRING, args?: { KEY: VALUE }+ }
call          := "call:" { flow: STRING }
return        := "return:" { value?: VALUE }
branch        := "branch:" STRING
emit          := "emit:" STRING
wait_for      := "wait_for:" ( STRING | wait_spec )
wait_spec     := { signals: STRING[], mode: "all_of" | "any_of", timeout?: DURATION }
set           := "set:" { variable: STRING, value: VALUE }
domain_action := ACTION_NAME ":" ( VALUE | { PARAM: VALUE }+ )

# Expressions
EXPR          := "${" expression "}"
expression    := ternary
ternary       := or ( "?" expression ":" expression )?
or            := and ( "||" and )*
and           := equality ( "&&" equality )*
equality      := comparison ( ( "==" | "!=" ) comparison )*
comparison    := addition ( ( "<" | "<=" | ">" | ">=" ) addition )*
addition      := multiplication ( ( "+" | "-" ) multiplication )*
multiplication := unary ( ( "*" | "/" | "%" ) unary )*
unary         := ( "!" | "-" )? postfix
postfix       := primary ( "." IDENT | "?." IDENT | "[" expression "]" | "(" args? ")" )*
primary       := NUMBER | STRING | BOOL | NULL | IDENT | "(" expression ")"

# Templates (Liquid-style)
template      := "{{" template_expr ( "|" filter )* "}}"
filter        := IDENT ( ":" VALUE )?

# Primitives
STRING        := quoted string
NUMBER        := integer or float
BOOL          := "true" | "false"
NULL          := "null"
DURATION      := NUMBER ("ms" | "s" | "m" | "h")
CONDITION     := OPERATOR VALUE    # e.g., "> 0.5", "== true"
EFFECT        := VALUE | DELTA     # e.g., "0.5", "-0.3", "+10"
OPERATOR      := ">" | ">=" | "<" | "<=" | "==" | "!="
DELTA         := ("+" | "-") NUMBER
```

---

## Appendix C: Design Decisions and Intentional Limitations

This section documents intentional design decisions that may appear as "limitations" but are deliberate choices with clear rationale. These are NOT bugs or TODOs.

### C.1 Context Variable Initialization for Imported Documents (Tree-Walking Only)

**Scope**: This applies only to the tree-walking `DocumentExecutor` (cloud-side orchestration). The bytecode compilation path (`DocumentMerger` → `BehaviorCompiler`) already handles imported context variables correctly by merging them with namespace prefixes.

**Behavior**: In tree-walking execution, context variables (defined in the `context:` section) are only initialized from the root document. When calling flows from imported documents, those documents' context variables are not automatically initialized.

**Why This Is Intentional**: The design prioritizes explicit parameter passing over implicit initialization. When you call an imported flow, it receives the caller's scope - any variables you set before the call are visible to the called flow. This is simple, predictable, and matches how most imported utility libraries are used.

**What Would Be Needed to Change This**:

In `CallHandler.ExecuteAsync`, after switching `CurrentDocument`, initialize the imported document's context variables:

```csharp
// After: context.CurrentDocument = resolvedDocument;

if (resolvedDocument.Document.Context?.Variables != null)
{
    foreach (var (name, definition) in resolvedDocument.Document.Context.Variables)
    {
        // Only initialize if NOT already in parent scope (preserve passed parameters)
        if (!callScope.TryGetValue(name, out _) && definition.Default != null)
        {
            callScope.SetValue(name, definition.Default);
        }
    }
}
```

This is approximately 10-15 lines of code with no architectural changes required.

**Design Decisions Required**:

| Question | Recommended Answer |
|----------|-------------------|
| Name collision (caller passed same variable name)? | Preserve caller's value |
| When to initialize? | Every call (predictable, isolated) |
| `source:` expressions? | Skip for now or evaluate lazily |

**Why It Hasn't Been Done**:

1. **Semantic ambiguity**: It's not immediately obvious what should happen with name collisions
2. **Explicit alternative exists**: You can set variables before calling:
   ```yaml
   - set: { variable: config_mode, value: "advanced" }
   - call: { flow: utils.setup }
   ```
3. **Use case unclear**: Most imported documents are utility libraries with pure functions that don't need their own default state

**Implementation Status**: This can be added at any time if a legitimate use case emerges. The infrastructure supports it; it's simply a matter of policy.

---

## Appendix D: Dialogue Resolution System

The dialogue system provides runtime text resolution with localization and conditional overrides.

### D.1 Three-Step Resolution Pipeline

```
1. Check external file for matching condition override (priority-sorted)
2. Check external file for localization (with fallback chain)
3. Fall back to inline default text (always exists, required in ABML)
```

**Key design**: Inline text is REQUIRED in every `speak:` action. External files are optional and provide localization + conditional overrides.

### D.2 External Dialogue File Format

```yaml
# dialogue/merchant/greet.yaml
localizations:
  en: "Welcome to my shop!"
  es: "¡Bienvenido a mi tienda!"
  ja: "いらっしゃいませ！"

overrides:
  - condition: "${player.reputation > 50}"
    text: "Ah, my favorite customer!"
    priority: 10
  - condition: "${time.hour >= 20}"
    text: "We're closing soon, but come in!"
    priority: 5
    locale: en  # Optional: locale-restricted override
```

### D.3 Code Locations

| Component | Location |
|-----------|----------|
| `IDialogueResolver` | `bannou-service/Behavior/IDialogueResolver.cs` |
| `ILocalizationProvider` | `bannou-service/Behavior/ILocalizationProvider.cs` |
| `IExternalDialogueLoader` | `bannou-service/Behavior/IExternalDialogueLoader.cs` |
| `DialogueResolver` | `lib-behavior/Dialogue/DialogueResolver.cs` |
| `ExternalDialogueLoader` | `lib-behavior/Dialogue/ExternalDialogueLoader.cs` |
| `FileLocalizationProvider` | `lib-behavior/Dialogue/FileLocalizationProvider.cs` |

### D.4 Future Extension: Database-Backed Dialogue Sources

The dialogue system supports two separate localization systems:

1. **Per-dialogue companion documents** (`IExternalDialogueLoader`)
   - Reference-based: `dialogue/merchant/greet` → YAML file
   - Conditional logic supported (overrides with conditions)
   - Currently file-only

2. **Global string tables** (`ILocalizationProvider`)
   - Key-based: `ui.menu.start` → localized string
   - Plugin architecture with `ILocalizationSource`
   - Ready for database source today

**Intended priority chain for dialogue text**:
1. Database (highest priority, almost never exists)
2. Companion document (YAML file, may exist)
3. Inline text (always exists, required in ABML)

**To add database support for per-dialogue**:

The `IExternalDialogueLoader` interface is abstract enough:
```csharp
Task<ExternalDialogueFile?> LoadAsync(string reference, CancellationToken ct);
```

Create `DatabaseDialogueLoader : IExternalDialogueLoader`, then aggregate:

```csharp
// Future: AggregateDialogueLoader.cs
public class AggregateDialogueLoader : IExternalDialogueLoader
{
    private readonly IReadOnlyList<IDialogueSource> _sources; // Priority-ordered

    public async Task<ExternalDialogueFile?> LoadAsync(string reference, CancellationToken ct)
    {
        foreach (var source in _sources)
        {
            var result = await source.LoadAsync(reference, ct);
            if (result != null) return result;
        }
        return null;
    }
}
```

**What to add when needed**:
- `IDialogueSource` interface (mirrors `ILocalizationSource`)
- `AggregateDialogueLoader` implementation
- `DatabaseDialogueSource` implementation
- Priority property on sources

This can be added without changing `DialogueResolver` - it only knows about `IExternalDialogueLoader`, not implementation details.

---

*This document is the authoritative reference for ABML.*
