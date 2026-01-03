# Case Study: Monster Rancher Combat Arena Demo

> **Status**: DESIGN IN PROGRESS
> **Created**: 2025-12-30
> **Purpose**: ABML/AST demonstration through simplified arena combat
> **Engine**: Stride
> **Assets**: Synty (all visual assets)
> **Related**: [ABML Guide](../guides/ABML.md), [BEHAVIOR_PLUGIN_V2.md](./UPCOMING_-_BEHAVIOR_PLUGIN_V2.md), [ACTORS_PLUGIN_V3.md](./UPCOMING_-_ACTORS_PLUGIN_V3.md)

---

## 1. Overview

### 1.1 Purpose

This demo serves as a **practical case study** for ABML and the behavior/actor systems. By implementing a simplified Monster Rancher-style combat arena, we demonstrate:

1. **ABML Behavior Trees** - Two combatants with AST-defined decision-making
2. **Local Runtime Execution** - Frame-by-frame combat decisions via compiled bytecode
3. **Fight Coordinator Actor** - Cloud-side agent creating "opportunity" events
4. **Streaming Composition** - Enrichment cinematics injected into ongoing combat
5. **Bannou Integration** - Full service stack (behavior, actors, events)

### 1.2 Why Monster Rancher?

Monster Rancher combat is intentionally simple:
- **1D Movement**: Forward, backward, dodge left/right (locked-on rotation)
- **Limited Actions**: 2 attacks + 1 distance-based ability at any time
- **Clear State**: Health, stamina, mana - all easily trackable
- **Deterministic Rules**: Distance determines available abilities

This simplicity lets us focus on **ABML patterns and architecture** rather than combat system complexity.

### 1.3 Dual-Brain Architecture & Player Control

This demo showcases the **dual-brain system** that powers all Arcadia characters:

```
┌─────────────────────────────────────────────────────────────────────────┐
│                        DUAL-BRAIN ARCHITECTURE                           │
│                                                                          │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │                    CLOUD BRAIN (Character Agent)                 │    │
│  │                         100-500ms latency                        │    │
│  │                                                                  │    │
│  │  • Personality, emotions, memories                               │    │
│  │  • Combat strategy and preferences                               │    │
│  │  • Relationship with player (trust, loyalty, mood)               │    │
│  │  • Generates/updates behavior ASTs                               │    │
│  │  • Decides WHAT the character WANTS to do                        │    │
│  └──────────────────────────┬──────────────────────────────────────┘    │
│                             │                                            │
│                    AST Distribution                                      │
│                             │                                            │
│                             ▼                                            │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │                    LOCAL BRAIN (Game Client)                     │    │
│  │                           <1ms latency                           │    │
│  │                                                                  │    │
│  │  • Executes compiled behavior ASTs                               │    │
│  │  • No independent decision-making                                │    │
│  │  • Responds to player input (when allowed)                       │    │
│  │  • Character "will" comes from cloud brain                       │    │
│  └──────────────────────────┬──────────────────────────────────────┘    │
│                             │                                            │
│                    Player Input                                          │
│                             │                                            │
│                             ▼                                            │
│  ┌─────────────────────────────────────────────────────────────────┐    │
│  │                    PLAYER CONTROL LAYER                          │    │
│  │                                                                  │    │
│  │  Player commands filtered through character's willingness:       │    │
│  │  • High trust: Character follows commands readily                │    │
│  │  • Low trust: Character may resist, delay, or refuse             │    │
│  │  • Panic/rage: Character may act autonomously                    │    │
│  │                                                                  │    │
│  │  This creates the authentic Monster Rancher feel where your      │    │
│  │  monster doesn't always do what you want - but here it's REAL.   │    │
│  └─────────────────────────────────────────────────────────────────┘    │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

**Key Insight**: Monster Rancher *pretended* your monster had a will of its own through RNG and hidden stats. In this system, the monster *actually* has motivations, emotions, and a relationship with you that determines cooperation. The cloud brain is genuinely deciding whether to follow your commands based on how you've treated it.

### 1.4 The "Fight Coordinator" Concept

Beyond basic combat, a **Fight Coordinator** watches the battle and creates **opportunities** - moments where combatants can trigger context-aware cinematic sequences:

- Environmental interactions (shoot down a chandelier, kick a boulder)
- Dramatic exchanges based on fight progression (comeback mechanic)
- Character-specific signature moves (based on traits/equipment)

The coordinator is an **Event Agent** (actor) that:
1. Observes both combatants via event tap
2. Analyzes environmental and combat context
3. Injects opportunity events that combatants can act on
4. Orchestrates cinematic exchanges when opportunities are taken

---

## 2. Combat System Design

### 2.1 Arena Layout

```
    ╔════════════════════════════════════════════════════════════╗
    ║                       ARENA (20m x 20m)                     ║
    ║                                                             ║
    ║      [Ice Zone]                              [Sand Zone]    ║
    ║        ~~~~                                    ::::         ║
    ║        ~~~~         🪨 Boulder                 ::::         ║
    ║                       (HP: 50)                              ║
    ║                                                             ║
    ║     👹                                              👺      ║
    ║  Monster A                                      Monster B   ║
    ║  (Facing →)                                    (← Facing)   ║
    ║                                                             ║
    ║                        🪨 Boulder                           ║
    ║      [Sand Zone]        (HP: 50)         [Ice Zone]        ║
    ║        ::::                                 ~~~~            ║
    ║        ::::                                 ~~~~            ║
    ╚════════════════════════════════════════════════════════════╝

Distance Zones (from each monster's perspective):
├── Short Range ──┤ (<2m)    : Close abilities available
├── Medium Range ─┤ (2m-5m)  : Medium abilities available
├── Long Range ───┤ (>5m)    : Long abilities available (costs mana)
```

### 2.2 Movement System

Movement is **locked-on** - monsters always face each other and move relative to their opponent:

| Input | Effect | Speed |
|-------|--------|-------|
| **Forward** | Move toward opponent | 3 m/s |
| **Backward** | Move away from opponent | 2.5 m/s |
| **Dodge Left** | Strafe left (rotate around opponent) | 4 m/s |
| **Dodge Right** | Strafe right (rotate around opponent) | 4 m/s |

**Terrain Effects:**

| Surface | Movement Effect | Combat Effect |
|---------|-----------------|---------------|
| Normal | 100% speed | Standard damage |
| Ice | 150% speed, 50% acceleration | +20% damage received |
| Sand | 70% speed | -10% damage dealt |

### 2.3 Combat Actions

#### Basic Attacks

| Attack | Stamina Cost | Damage | Recovery | Notes |
|--------|--------------|--------|----------|-------|
| **Weak Attack** | 10 | 8-12 | 0.3s | Fast, interruptible |
| **Heavy Attack** | 20 | 18-25 | 0.8s | Slow, can't cancel |

#### Distance-Based Abilities (Example - Troll)

Each monster has unique abilities for each range. Below are **example** abilities for the Troll:

| Range | Ability | Stamina | Mana | Damage | Special |
|-------|---------|---------|------|--------|---------|
| **Short** (<2m) | Grab/Throw | 30 | 0 | 15 | Repositions enemy |
| **Medium** (2-5m) | Charge Attack | 30 | 0 | 20 | Closes distance, knockdown |
| **Long** (>5m) | Rock Throw | 30 | 30 | 25 | Limited uses (mana) |

*Note: The Elemental Golem would have different abilities (e.g., Crystal Spike at short range, Energy Pulse at medium, Energy Beam at long). Abilities are character-specific and defined in each monster's behavior schema.*

### 2.4 Resource System

```
┌─────────────────────────────────────────────────────────────────────┐
│                         RESOURCE BARS                                │
├─────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  HEALTH:  ████████████████████████████████████████ 100/100          │
│           (No regeneration - depletes on damage)                     │
│                                                                      │
│  STAMINA: ████████████████████████████████████████ 100/100          │
│           Regenerates: 10/s (recovery) or 5/s (active)              │
│           Recovery mode: See Recovery Mode Rules below               │
│                                                                      │
│  MANA:    ████████████████████████████████████████ 100/100          │
│           No regeneration - 3 long-range attacks max                │
│           Cost: 30 per long-range ability                           │
│                                                                      │
└─────────────────────────────────────────────────────────────────────┘
```

#### Recovery Mode Rules

Recovery mode grants **10/s** stamina regeneration (vs 5/s in active state). Recovery mode activates after **3+ seconds** of no stamina-consuming actions:

| Action | Breaks Recovery Mode |
|--------|---------------------|
| Attacking (weak/heavy) | Yes |
| Using abilities | Yes |
| Dodging (left/right strafe) | Yes |
| Taking damage | Yes |
| Moving forward/backward | **No** |
| Standing idle | **No** |

**Design rationale**: This allows tactical spacing and repositioning during stamina recovery while preventing stamina regeneration during aggressive play or evasive maneuvering.

**Visual Feedback for Recovery Mode:**
- Character flashes white/yellow briefly when entering recovery mode
- Stamina bar gains a **yellow border** while in recovery mode
- **"+++"** indicator appears at the end of the stamina bar
- Optional: Small icon above character head (yellow stamina bar with +++) for spectators

### 2.5 Environmental Objects

#### Boulders

| Property | Value |
|----------|-------|
| Health | 50 HP |
| Shrapnel Radius | 3m |
| Shrapnel Damage | 15-20 |
| Cover Bonus | Blocks ranged abilities |
| Hide Zone | 1.5m radius behind boulder (relative to enemy) |

Boulders **cannot be targeted directly** - they take incidental damage from abilities that hit them. When destroyed, shrapnel damages anyone in radius.

**Boulder Interactions:**

| Interaction | Trigger | Effect |
|-------------|---------|--------|
| **Cover** | Stand behind boulder (enemy line-of-sight blocked) | Ranged abilities blocked |
| **Kick** (Opportunity) | Near weakened boulder (<25 HP) + enemy in blast zone | Offensive cinematic |
| **Hide** (Opportunity) | Low health (<30%) + near intact boulder | Defensive cinematic - brief invulnerability |

The "hide" opportunity is a **defensive opportunity** offered to a losing combatant when near a boulder - it triggers a brief cinematic where the character dives behind cover, catching their breath. This creates a dramatic "cornered but not defeated" moment.

---

## 3. ABML Behavior Models

> **STATUS**: UPDATE AFTER RUNTIME/CLIENT IMPLEMENTATION MORE COMPLETE
>
> This section contains placeholder schemas and example ABML. The specific input/output variables, flow structure, and player integration details will be refined once the local runtime interpreter and client-side behavior execution are further along.

### 3.1 Behavior Type Structure

Each monster has a **combat behavior type** with a single **variant** (for this demo):

```
Monster Combat Behaviors
└── combat (type)
    └── basic (variant)    ← Single variant for demo simplicity
```

In a full game, variants would represent different fighting styles (aggressive, defensive, tactical).

### 3.2 Input Schema

The behavior model receives these inputs each evaluation frame:

```yaml
# schemas/monster-combat-inputs.yaml
inputs:
  # Own state
  health: float           # 0-100
  stamina: float          # 0-100
  mana: float             # 0-100
  position_x: float       # Arena X coordinate
  position_y: float       # Arena Y coordinate

  # Enemy state
  enemy_distance: float   # Distance to opponent
  enemy_health: float     # Enemy HP (0-100)
  enemy_stamina: float    # Enemy stamina
  enemy_attacking: bool   # Enemy in attack animation
  enemy_recovering: bool  # Enemy in recovery state

  # Combat state
  in_attack: bool         # Currently attacking
  attack_frame: int       # Current attack animation frame
  in_recovery: bool       # In stamina recovery mode
  recovery_timer: float   # Time since last action

  # Environment
  on_ice: bool            # Standing on ice surface
  on_sand: bool           # Standing on sand surface
  near_boulder: bool      # Boulder within 2m
  boulder_between: bool   # Boulder blocks line to enemy

  # Distance zones (derived, but passed for convenience)
  in_short_range: bool    # <2m
  in_medium_range: bool   # 2-5m
  in_long_range: bool     # >5m

  # Opportunity state (from Fight Coordinator)
  opportunity_available: bool     # Coordinator offering opportunity
  opportunity_type: string        # "environmental", "dramatic", "signature"
  opportunity_target: string      # Object/action identifier
  opportunity_window_ms: int      # Time remaining to act
```

### 3.3 Output Schema

The behavior model produces action intents:

```yaml
# schemas/monster-combat-outputs.yaml
outputs:
  # Primary intent
  action: string          # "move_forward", "weak_attack", "ability", etc.
  action_priority: float  # 0-1, urgency for intent merging

  # Movement (if action is movement)
  move_direction: string  # "forward", "backward", "left", "right"

  # Attack (if action is attack)
  attack_type: string     # "weak", "heavy"

  # Ability (if action is ability)
  ability_type: string    # "short", "medium", "long"

  # Opportunity response
  take_opportunity: bool  # Accept current opportunity?
```

### 3.4 Basic Combat ABML

```yaml
# behaviors/monster-combat-basic.yml
version: "2.0"
metadata:
  id: monster_combat_basic
  type: behavior
  description: Basic monster combat behavior for arena demo

context:
  variables:
    aggression:
      type: float
      default: 0.5       # 0 = defensive, 1 = aggressive
    risk_tolerance:
      type: float
      default: 0.5       # Willingness to trade hits

flows:
  # Entry point - called each frame
  evaluate:
    actions:
      # First: Check for opportunities from Fight Coordinator
      - cond:
          - when: "${opportunity_available && opportunity_window_ms > 100}"
            then:
              - call: { flow: evaluate_opportunity }

      # Second: React to enemy attacks
      - cond:
          - when: "${enemy_attacking && stamina >= 10}"
            then:
              - call: { flow: defensive_response }

      # Third: Evaluate offensive options
      - cond:
          - when: "${stamina >= 30 && enemy_recovering}"
            then:
              - call: { flow: punish_recovery }

      # Fourth: Normal combat flow
      - call: { flow: standard_combat }

  evaluate_opportunity:
    actions:
      # Evaluate if opportunity is worth taking
      - cond:
          - when: "${opportunity_type == 'environmental' && health > 30}"
            then:
              # Environmental opportunities (boulder, etc) when healthy
              - set: { variable: take_opportunity, value: true }
              - return: {}
          - when: "${opportunity_type == 'dramatic' && health < enemy_health}"
            then:
              # Dramatic comeback opportunities when losing
              - set: { variable: take_opportunity, value: true }
              - return: {}
          - else:
              # Decline opportunity, continue normal combat
              - set: { variable: take_opportunity, value: false }

  defensive_response:
    actions:
      - cond:
          - when: "${in_short_range && stamina >= 20}"
            then:
              # Dodge away from close attacks
              - set: { variable: action, value: "move" }
              - set: { variable: move_direction, value: "backward" }
              - set: { variable: action_priority, value: 0.9 }
          - when: "${stamina >= 10}"
            then:
              # Sidestep ranged/medium attacks
              - set: { variable: action, value: "move" }
              - set: { variable: move_direction, value: "${random() > 0.5 ? 'left' : 'right'}" }
              - set: { variable: action_priority, value: 0.85 }
          - else:
              # Out of stamina - just take the hit
              - log: { message: "No stamina for defense" }

  punish_recovery:
    actions:
      - cond:
          - when: "${in_short_range && stamina >= 20}"
            then:
              # Close range: heavy attack
              - set: { variable: action, value: "attack" }
              - set: { variable: attack_type, value: "heavy" }
              - set: { variable: action_priority, value: 0.95 }
          - when: "${in_medium_range && stamina >= 30}"
            then:
              # Medium range: charge attack
              - set: { variable: action, value: "ability" }
              - set: { variable: ability_type, value: "medium" }
              - set: { variable: action_priority, value: 0.9 }
          - else:
              # Close distance first
              - set: { variable: action, value: "move" }
              - set: { variable: move_direction, value: "forward" }
              - set: { variable: action_priority, value: 0.7 }

  standard_combat:
    actions:
      # Resource-aware combat decisions
      - cond:
          # Low stamina - recover
          - when: "${stamina < 20 && !enemy_attacking}"
            then:
              - call: { flow: recovery_mode }

          # Short range combat
          - when: "${in_short_range}"
            then:
              - call: { flow: short_range_combat }

          # Medium range combat
          - when: "${in_medium_range}"
            then:
              - call: { flow: medium_range_combat }

          # Long range combat
          - when: "${in_long_range}"
            then:
              - call: { flow: long_range_combat }

  recovery_mode:
    actions:
      # Back off and recover stamina
      - cond:
          - when: "${enemy_distance < 5}"
            then:
              - set: { variable: action, value: "move" }
              - set: { variable: move_direction, value: "backward" }
              - set: { variable: action_priority, value: 0.6 }
          - else:
              # Far enough, just wait
              - set: { variable: action, value: "idle" }
              - set: { variable: action_priority, value: 0.3 }

  short_range_combat:
    actions:
      - cond:
          # Use grab if available
          - when: "${stamina >= 30 && random() < aggression}"
            then:
              - set: { variable: action, value: "ability" }
              - set: { variable: ability_type, value: "short" }
              - set: { variable: action_priority, value: 0.8 }

          # Quick attacks
          - when: "${stamina >= 10}"
            then:
              - set: { variable: action, value: "attack" }
              - set: { variable: attack_type, value: "weak" }
              - set: { variable: action_priority, value: 0.7 }

          # Back off if no stamina
          - else:
              - set: { variable: action, value: "move" }
              - set: { variable: move_direction, value: "backward" }
              - set: { variable: action_priority, value: 0.5 }

  medium_range_combat:
    actions:
      - cond:
          # Aggressive: close in
          - when: "${aggression > 0.6 && stamina >= 30}"
            then:
              - set: { variable: action, value: "ability" }
              - set: { variable: ability_type, value: "medium" }
              - set: { variable: action_priority, value: 0.75 }

          # Defensive: poke and retreat
          - when: "${stamina >= 10}"
            then:
              - set: { variable: action, value: "move" }
              - set: { variable: move_direction, value: "forward" }
              - set: { variable: action_priority, value: 0.6 }

          - else:
              - set: { variable: action, value: "idle" }

  long_range_combat:
    actions:
      - cond:
          # Use ranged blast if have mana
          - when: "${mana >= 30 && enemy_health > 25 && !boulder_between}"
            then:
              - set: { variable: action, value: "ability" }
              - set: { variable: ability_type, value: "long" }
              - set: { variable: action_priority, value: 0.7 }

          # Otherwise close distance
          - else:
              - set: { variable: action, value: "move" }
              - set: { variable: move_direction, value: "forward" }
              - set: { variable: action_priority, value: 0.5 }
```

### 3.5 Behavior Compilation Flow

```
┌───────────────────────────────────────────────────────────────────────┐
│                    ABML → BYTECODE COMPILATION                         │
│                                                                        │
│  monster-combat-basic.yml                                              │
│          │                                                             │
│          ▼                                                             │
│  ┌────────────────┐                                                   │
│  │ DocumentParser │  Parse YAML → AST (AbmlDocument)                  │
│  └────────┬───────┘                                                   │
│           │                                                            │
│           ▼                                                            │
│  ┌────────────────┐                                                   │
│  │BehaviorCompiler│  AST → Bytecode (BehaviorModel)                   │
│  │                │  - Flatten control flow                           │
│  │                │  - Compile expressions                            │
│  │                │  - Generate opcode sequence                       │
│  └────────┬───────┘                                                   │
│           │                                                            │
│           ▼                                                            │
│  ┌────────────────────────────────────────────────────────────────┐   │
│  │ BehaviorModel (Binary)                                          │   │
│  │ • Header: "ABML", version, flags, checksum                      │   │
│  │ • Input Schema: 20 variables (health, stamina, etc.)            │   │
│  │ • Output Schema: 5 variables (action, priority, etc.)           │   │
│  │ • Constant Pool: strings, numbers                               │   │
│  │ • Bytecode: ~500 bytes for this behavior                        │   │
│  └────────────────────────────────────────────────────────────────┘   │
│                                                                        │
│  Distribution: Push to game clients via WebSocket                      │
│                                                                        │
└───────────────────────────────────────────────────────────────────────┘
```

---

## 4. Fight Coordinator (Event Agent)

### 4.1 Concept

The Fight Coordinator is an **actor** that watches the combat and creates **opportunities** - contextual moments where a combatant can trigger something special.

```
┌───────────────────────────────────────────────────────────────────────┐
│                      FIGHT COORDINATOR ACTOR                           │
│                                                                        │
│  Inputs:                              Outputs:                         │
│  ├── Monster A events (event tap)    ├── Opportunity events           │
│  ├── Monster B events (event tap)    │   (pushed to combatants)       │
│  ├── Arena state                     └── Cinematic triggers           │
│  └── Environment objects                 (when opportunity taken)      │
│                                                                        │
│  ┌─────────────────────────────────────────────────────────────────┐  │
│  │                     ANALYSIS PIPELINE                            │  │
│  │                                                                  │  │
│  │  Combat Events ──► Context ──► Opportunity ──► Validation ──►   │  │
│  │                    Analysis    Detection       & Timing          │  │
│  │                                                                  │  │
│  │  Analyzes:                        Detects:                       │  │
│  │  • Relative positioning           • Environmental setups         │  │
│  │  • Resource states                • Comeback moments             │  │
│  │  • Fight tempo/pacing             • Dramatic timing              │  │
│  │  • Previous opportunities         • Signature move windows       │  │
│  └─────────────────────────────────────────────────────────────────┘  │
│                                                                        │
└───────────────────────────────────────────────────────────────────────┘
```

### 4.2 Opportunity Types

| Type | Category | Trigger Condition | Example |
|------|----------|-------------------|---------|
| **Environmental (Offensive)** | Offensive | Near weakened destructible + enemy in blast radius | "Kick boulder at enemy" |
| **Environmental (Defensive)** | Defensive | Low health + near intact cover | "Dive behind boulder" |
| **Dramatic** | Comeback | Health disparity (losing by 40+ HP) + still viable | "Comeback rally" |
| **Signature** | Character | Character-specific conditions met | "Troll's ground pound" |
| **Finisher** | Offensive | Enemy low health (<15) + close range + stamina | "Cinematic KO" |

**Opportunity Categories:**
- **Offensive**: Requires attacking/damaging enemy
- **Defensive**: Provides protection/recovery
- **Comeback**: Turns the tide when losing

### 4.3 Actor Schema

```yaml
# schemas/actor-types/fight-coordinator.yaml
openapi: 3.0.3
info:
  title: Fight Coordinator Actor
  version: 1.0.0
  description: Watches combat and generates opportunity events

x-actor-type:
  name: fight-coordinator
  idle_timeout_seconds: 300
  mailbox_capacity: 50

  state:
    $ref: '#/components/schemas/FightCoordinatorState'

  subscriptions:
    # Tap into both combatants' event streams
    - topic: character.combat.${participant_a_id}
      schema:
        $ref: '#/components/schemas/CombatEvent'
    - topic: character.combat.${participant_b_id}
      schema:
        $ref: '#/components/schemas/CombatEvent'

paths:
  /initialize:
    post:
      operationId: HandleInitialize
      x-actor-message: initialize
      description: Start coordinating a fight
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/InitializeFightRequest'
      responses:
        '200':
          description: Fight initialized

  /combat-event:
    post:
      operationId: HandleCombatEvent
      x-actor-message: combat-event
      description: Process combat event from a participant
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CombatEvent'
      responses:
        '200':
          description: Event processed

  /opportunity-response:
    post:
      operationId: HandleOpportunityResponse
      x-actor-message: opportunity-response
      description: Participant responded to an opportunity
      requestBody:
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/OpportunityResponse'
      responses:
        '200':
          description: Response processed

components:
  schemas:
    FightCoordinatorState:
      type: object
      properties:
        fight_id:
          type: string
        participant_a_id:
          type: string
        participant_b_id:
          type: string
        start_time:
          type: string
          format: date-time
        fight_duration_seconds:
          type: number
        opportunities_offered:
          type: integer
        opportunities_taken:
          type: integer
        last_opportunity_at:
          type: string
          format: date-time
        cooldown_remaining_ms:
          type: integer
          description: Minimum time between opportunities
        arena_state:
          $ref: '#/components/schemas/ArenaState'
        combat_history:
          type: array
          items:
            $ref: '#/components/schemas/CombatSnapshot'

    ArenaState:
      type: object
      properties:
        boulders:
          type: array
          items:
            $ref: '#/components/schemas/BoulderState'
        terrain_zones:
          type: array
          items:
            $ref: '#/components/schemas/TerrainZone'

    BoulderState:
      type: object
      properties:
        id:
          type: string
        position_x:
          type: number
        position_y:
          type: number
        health:
          type: integer
        destroyed:
          type: boolean

    CombatEvent:
      type: object
      required: [event_type, participant_id, timestamp]
      properties:
        event_type:
          type: string
          enum: [position_update, attack_started, attack_hit, ability_used, damage_taken, resource_changed]
        participant_id:
          type: string
        timestamp:
          type: string
          format: date-time
        data:
          type: object
          additionalProperties: true

    InitializeFightRequest:
      type: object
      required: [fight_id, participant_a_id, participant_b_id, arena_config]
      properties:
        fight_id:
          type: string
        participant_a_id:
          type: string
        participant_b_id:
          type: string
        arena_config:
          $ref: '#/components/schemas/ArenaState'

    OpportunityResponse:
      type: object
      required: [opportunity_id, participant_id, accepted]
      properties:
        opportunity_id:
          type: string
        participant_id:
          type: string
        accepted:
          type: boolean
```

### 4.4 Opportunity Detection Logic

The Fight Coordinator runs analysis on each combat tick:

```yaml
# behaviors/fight-coordinator-analysis.yml
version: "2.0"
metadata:
  id: fight_coordinator_analysis
  type: behavior
  description: Fight analysis for opportunity detection

context:
  variables:
    opportunity_cooldown_ms:
      type: int
      default: 5000        # 5 second minimum between opportunities
    min_fight_duration_ms:
      type: int
      default: 10000       # Don't offer opportunities in first 10 seconds

flows:
  analyze_combat:
    actions:
      # Check cooldown
      - cond:
          - when: "${cooldown_remaining_ms > 0}"
            then:
              - return: {}

      # Check minimum fight duration
      - cond:
          - when: "${fight_duration_ms < min_fight_duration_ms}"
            then:
              - return: {}

      # Run opportunity detectors
      - call: { flow: detect_environmental_offensive }
      - call: { flow: detect_environmental_defensive }
      - call: { flow: detect_dramatic }
      - call: { flow: detect_finisher }

  detect_environmental_offensive:
    actions:
      # Check for offensive boulder setups (kick weakened boulder at enemy)
      - for_each:
          variable: boulder
          collection: "${arena_state.boulders}"
          do:
            - cond:
                - when: "${!boulder.destroyed && boulder.health < 25}"
                  then:
                    # Weakened boulder - check if kick setup exists
                    - call:
                        flow: check_boulder_kick_opportunity
                        args:
                          boulder_id: "${boulder.id}"
                          boulder_pos: "${boulder.position}"

  check_boulder_kick_opportunity:
    actions:
      # Is one combatant near the boulder and enemy in blast zone?
      - cond:
          - when: "${distance(participant_a.position, args.boulder_pos) < 2.0}"
            then:
              - cond:
                  - when: "${distance(participant_b.position, args.boulder_pos) < 3.5}"
                    then:
                      # A can kick boulder at B
                      - call:
                          flow: offer_opportunity
                          args:
                            type: "environmental_offensive"
                            category: "offensive"
                            target_participant: "${participant_a.id}"
                            action: "kick_boulder"
                            target_object: "${args.boulder_id}"
                            window_ms: 2000

  detect_environmental_defensive:
    actions:
      # Check for defensive boulder setups (dive behind cover when losing)
      - for_each:
          variable: boulder
          collection: "${arena_state.boulders}"
          do:
            - cond:
                - when: "${!boulder.destroyed}"
                  then:
                    # Intact boulder - check if hide setup exists
                    - call:
                        flow: check_boulder_hide_opportunity
                        args:
                          boulder_id: "${boulder.id}"
                          boulder_pos: "${boulder.position}"

  check_boulder_hide_opportunity:
    actions:
      # Is a losing combatant near an intact boulder?
      - cond:
          # Participant A is low health and near boulder
          - when: "${participant_a.health < 30 && distance(participant_a.position, args.boulder_pos) < 2.5}"
            then:
              - call:
                  flow: offer_opportunity
                  args:
                    type: "environmental_defensive"
                    category: "defensive"
                    target_participant: "${participant_a.id}"
                    action: "hide_behind_boulder"
                    target_object: "${args.boulder_id}"
                    window_ms: 1500

          # Participant B is low health and near boulder
          - when: "${participant_b.health < 30 && distance(participant_b.position, args.boulder_pos) < 2.5}"
            then:
              - call:
                  flow: offer_opportunity
                  args:
                    type: "environmental_defensive"
                    category: "defensive"
                    target_participant: "${participant_b.id}"
                    action: "hide_behind_boulder"
                    target_object: "${args.boulder_id}"
                    window_ms: 1500

  detect_dramatic:
    actions:
      # Comeback opportunity: losing badly but still in it
      - set:
          variable: health_diff
          value: "${participant_a.health - participant_b.health}"

      - cond:
          # A is losing by 40+ HP but still has >20 HP
          - when: "${health_diff < -40 && participant_a.health > 20}"
            then:
              - call:
                  flow: offer_opportunity
                  args:
                    type: "dramatic"
                    target_participant: "${participant_a.id}"
                    action: "comeback_rally"
                    window_ms: 3000

          # B is losing by 40+ HP but still has >20 HP
          - when: "${health_diff > 40 && participant_b.health > 20}"
            then:
              - call:
                  flow: offer_opportunity
                  args:
                    type: "dramatic"
                    target_participant: "${participant_b.id}"
                    action: "comeback_rally"
                    window_ms: 3000

  detect_finisher:
    actions:
      # Check for finishing blow setup
      - cond:
          - when: "${participant_b.health < 15 && participant_a.stamina > 30}"
            then:
              - cond:
                  - when: "${distance(participant_a.position, participant_b.position) < 3}"
                    then:
                      - call:
                          flow: offer_opportunity
                          args:
                            type: "finisher"
                            target_participant: "${participant_a.id}"
                            action: "cinematic_finisher"
                            window_ms: 1500

  offer_opportunity:
    actions:
      # Publish opportunity to target combatant
      - publish:
          topic: "combat.opportunity.${args.target_participant}"
          payload:
            opportunity_id: "${generate_id()}"
            type: "${args.type}"
            action: "${args.action}"
            target_object: "${args.target_object}"
            window_ms: "${args.window_ms}"
            offered_at: "${now()}"

      # Update coordinator state
      - set: { variable: cooldown_remaining_ms, value: "${opportunity_cooldown_ms}" }
      - increment: { variable: opportunities_offered, by: 1 }
```

### 4.5 Cinematic Injection (Streaming Composition)

When an opportunity is taken, the Fight Coordinator uses **streaming composition** to inject a cinematic:

```yaml
# cinematics/boulder-kick.yml
version: "2.0"
metadata:
  id: boulder_kick_cinematic
  type: cinematic
  takes_control_of: ["${attacker_id}", "${defender_id}"]
  duration_seconds: 3.5

channels:
  camera:
    - move_to: { position: "${boulder.position}", offset: [2, 3, 0], duration: 0.3s }
    - emit: camera_ready
    - track: { subject: "${attacker_id}", duration: 1s }
    - wait_for: @attacker.kick_impact
    - shake: { intensity: 0.4, duration: 0.3s }
    - track: { subject: "${defender_id}", duration: 1s }

  attacker:
    - wait_for: @camera.camera_ready
    - move_to: { position: "${boulder.position}", speed: fast }
    - animate: { animation: "powerful_kick", target: "${boulder.id}" }
    - emit: kick_impact
    - animate: { animation: "kick_followthrough" }

  boulder:
    - wait_for: @attacker.kick_impact
    - physics_impulse: { direction: "${to_defender}", force: 500 }
    - wait: { duration: 0.4s }
    - destroy: { effect: "shatter" }
    - emit: destroyed

  defender:
    - wait_for: @boulder.destroyed
    - apply_damage: { amount: "${shrapnel_damage}", type: "impact" }
    - animate: { animation: "hit_stagger" }
    - emit: complete

  audio:
    - wait_for: @attacker.kick_impact
    - sfx: { sound: "boulder_crack" }
    - wait_for: @boulder.destroyed
    - sfx: { sound: "explosion_rock" }
```

#### Defensive Cinematic: Hide Behind Boulder

```yaml
# cinematics/boulder-hide.yml
version: "2.0"
metadata:
  id: boulder_hide_cinematic
  type: cinematic
  takes_control_of: ["${defender_id}"]
  duration_seconds: 2.0
  grants_invulnerability: true  # Defender can't be damaged during this

channels:
  camera:
    - move_to: { position: "${defender.position}", offset: [3, 2, 2], duration: 0.2s }
    - emit: camera_ready
    - track: { subject: "${defender_id}", duration: 1.5s }
    - shake: { intensity: 0.1, duration: 0.3s }  # Impact of diving

  defender:
    - wait_for: @camera.camera_ready
    - animate: { animation: "desperate_dive", target: "${boulder.id}" }
    - emit: dive_complete
    - move_to: { position: "${boulder.cover_position}", speed: fast }
    - animate: { animation: "catch_breath", duration: 0.8s }
    - emit: complete

  effects:
    - wait_for: @defender.dive_complete
    - particle: { effect: "dust_cloud", position: "${defender.position}" }

  audio:
    - wait_for: @defender.dive_complete
    - sfx: { sound: "body_impact_ground" }
    - sfx: { sound: "heavy_breathing", delay: 0.5s }

  # Optional: Attacker reaction (if they were attacking)
  attacker:
    - wait_for: @defender.dive_complete
    - cond:
        - when: "${attacker.was_attacking}"
          then:
            - animate: { animation: "attack_whiff" }
            - emit: missed
```

---

## 5. System Integration

### 5.1 Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              BANNOU SERVICES                                 │
│                                                                              │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐             │
│  │ Behavior Plugin │  │  Actors Plugin  │  │ Connect Plugin  │             │
│  │                 │  │                 │  │                 │             │
│  │ • ABML Parser   │  │ • Fight         │  │ • WebSocket     │             │
│  │ • Compiler      │  │   Coordinator   │  │   Gateway       │             │
│  │ • Distribution  │  │ • Actor Runtime │  │ • Event Routing │             │
│  └────────┬────────┘  └────────┬────────┘  └────────┬────────┘             │
│           │                    │                    │                       │
│           └────────────────────┼────────────────────┘                       │
│                                │                                            │
│                         lib-messaging (RabbitMQ)                            │
│                                │                                            │
└────────────────────────────────┼────────────────────────────────────────────┘
                                 │
                           WebSocket
                                 │
┌────────────────────────────────┼────────────────────────────────────────────┐
│                                ▼                                            │
│                        GAME CLIENT (Stride)                                 │
│                                                                              │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                     Behavior Layer                                    │   │
│  │                                                                       │   │
│  │  ┌───────────────┐  ┌───────────────┐  ┌───────────────┐            │   │
│  │  │ BehaviorModel │  │ BehaviorModel │  │   Intent      │            │   │
│  │  │   Cache       │  │  Interpreter  │  │   System      │            │   │
│  │  └───────────────┘  └───────────────┘  └───────────────┘            │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                 │                                           │
│                                 ▼                                           │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                     Game Systems Layer                                │   │
│  │                                                                       │   │
│  │  ┌───────────────┐  ┌───────────────┐  ┌───────────────┐            │   │
│  │  │   Combat      │  │   Physics     │  │   Animation   │            │   │
│  │  │   System      │  │   System      │  │   System      │            │   │
│  │  └───────────────┘  └───────────────┘  └───────────────┘            │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                              │
└──────────────────────────────────────────────────────────────────────────────┘
```

### 5.2 Event Flow

```
┌──────────────────────────────────────────────────────────────────────────────┐
│                           FIGHT EVENT FLOW                                    │
│                                                                               │
│  1. FIGHT START                                                              │
│  ───────────────                                                             │
│  Game Client ──► Connect ──► Actors Plugin                                   │
│                              │                                               │
│                              ▼                                               │
│                     [Spawn Fight Coordinator Actor]                          │
│                              │                                               │
│                              ▼                                               │
│                     [Subscribe to combatant events]                          │
│                                                                               │
│  2. COMBAT LOOP (60fps on client)                                            │
│  ────────────────────────────────                                            │
│                                                                               │
│  ┌─────────────────────────────────────────────────────────────────────────┐│
│  │ Each Frame:                                                              ││
│  │                                                                          ││
│  │   Monster A                           Monster B                          ││
│  │   ┌───────────────────────┐           ┌───────────────────────┐         ││
│  │   │ 1. Read game state    │           │ 1. Read game state    │         ││
│  │   │ 2. BehaviorInterpreter│           │ 2. BehaviorInterpreter│         ││
│  │   │ 3. Output intent      │           │ 3. Output intent      │         ││
│  │   │ 4. Execute action     │           │ 4. Execute action     │         ││
│  │   └──────────┬────────────┘           └──────────┬────────────┘         ││
│  │              │                                   │                       ││
│  │              ▼                                   ▼                       ││
│  │   [Combat events published to Bannou]                                    ││
│  └─────────────────────────────────────────────────────────────────────────┘│
│                                                                               │
│  3. COORDINATOR ANALYSIS (cloud, ~100-500ms latency)                         │
│  ────────────────────────────────────────────────────                        │
│                                                                               │
│  Fight Coordinator ◄── receives combat events                                │
│         │                                                                    │
│         ▼                                                                    │
│  [Analyze context, detect opportunities]                                     │
│         │                                                                    │
│         ▼                                                                    │
│  [Publish opportunity event] ──► Combatant                                   │
│                                                                               │
│  4. OPPORTUNITY RESPONSE                                                     │
│  ───────────────────────                                                     │
│                                                                               │
│  Combatant behavior evaluates:                                               │
│  • opportunity_available = true                                              │
│  • Sets take_opportunity = true/false                                        │
│         │                                                                    │
│         ▼                                                                    │
│  If accepted ──► Coordinator triggers cinematic                              │
│         │                                                                    │
│         ▼                                                                    │
│  [Cinematic streamed to client via Connect]                                  │
│                                                                               │
│  5. CINEMATIC EXECUTION                                                      │
│  ──────────────────────                                                      │
│                                                                               │
│  Game Client receives cinematic (BehaviorModel with channels)                │
│         │                                                                    │
│         ▼                                                                    │
│  [Pause basic behavior evaluation]                                           │
│         │                                                                    │
│         ▼                                                                    │
│  [Execute cinematic channels: camera, attacker, defender, audio]             │
│         │                                                                    │
│         ▼                                                                    │
│  [Resume basic behavior evaluation]                                          │
│                                                                               │
└──────────────────────────────────────────────────────────────────────────────┘
```

### 5.3 State Synchronization

| State Type | Authority | Sync Method |
|------------|-----------|-------------|
| Monster Position | Client | Periodic broadcast |
| Monster Resources (HP, stamina, mana) | Client | Event on change |
| Arena Objects (boulders) | Server | Authoritative state |
| Opportunities | Server (Coordinator) | Push events |
| Cinematics | Server | Streaming composition |

### 5.4 Debug Event System

For development and demonstration purposes, a **debug event stream** provides real-time insight into ABML evaluation, coordinator analysis, and opportunity detection.

#### Architecture

```
┌───────────────────────────────────────────────────────────────────────┐
│                        DEBUG EVENT FLOW                                │
│                                                                        │
│  ┌─────────────────┐                                                  │
│  │ Fight           │  Publishes to: debug.fight.{fight_id}            │
│  │ Coordinator     │  ├── coordinator.analysis                        │
│  │                 │  ├── coordinator.opportunity_detected            │
│  │                 │  └── coordinator.opportunity_response            │
│  └────────┬────────┘                                                  │
│           │                                                            │
│           ▼                                                            │
│  ┌─────────────────┐                                                  │
│  │ Connect Service │  Routes debug events to developer clients        │
│  │ (WebSocket)     │  ONLY if session has developer credentials       │
│  └────────┬────────┘                                                  │
│           │                                                            │
│           ▼                                                            │
│  ┌─────────────────────────────────────────────────────────────────┐  │
│  │                    GAME CLIENT (Stride)                          │  │
│  │                                                                   │  │
│  │  ┌─────────────────┐    ┌─────────────────────────────────────┐  │  │
│  │  │ Debug Event     │    │ In-Game Debug Logger                 │  │  │
│  │  │ Receiver        │───▶│ • Scrolling event log               │  │  │
│  │  │                 │    │ • Behavior evaluation tree view     │  │  │
│  │  └─────────────────┘    │ • Opportunity status panel          │  │  │
│  │                          │ • Coordinator state inspector       │  │  │
│  │                          └─────────────────────────────────────┘  │  │
│  └───────────────────────────────────────────────────────────────────┘  │
│                                                                        │
└───────────────────────────────────────────────────────────────────────┘
```

#### Debug Event Types

```yaml
# Debug events published by Fight Coordinator
debug_events:
  coordinator.analysis:
    description: Periodic analysis tick results
    payload:
      fight_id: string
      tick_number: int
      participant_a_state:
        position: [float, float]
        health: float
        stamina: float
        current_action: string
      participant_b_state:
        position: [float, float]
        health: float
        stamina: float
        current_action: string
      opportunities_evaluated: int
      cooldown_remaining_ms: int

  coordinator.opportunity_detected:
    description: Opportunity condition met
    payload:
      opportunity_id: string
      type: string           # environmental, dramatic, signature, finisher
      category: string       # offensive, defensive, comeback
      target_participant: string
      trigger_conditions:    # What conditions triggered this
        - condition: string
          value: any
      window_ms: int
      confidence: float      # 0-1, how "good" the opportunity is

  coordinator.opportunity_response:
    description: Participant responded to opportunity
    payload:
      opportunity_id: string
      participant_id: string
      accepted: bool
      response_time_ms: int
      cinematic_triggered: string  # null if declined

  behavior.evaluation:
    description: Per-frame behavior evaluation result
    payload:
      monster_id: string
      frame_number: int
      inputs_snapshot:       # Key inputs that influenced decision
        health: float
        stamina: float
        enemy_distance: float
        enemy_attacking: bool
      flows_executed: [string]  # Which flows were called
      output:
        action: string
        priority: float
      evaluation_time_us: int   # Microseconds
```

#### Developer Credential Gating

Debug events are **only routed to clients with developer credentials**:

```csharp
// In Connect service - debug event routing
public async Task RouteDebugEventAsync(DebugEvent evt, CancellationToken ct)
{
    // Get all clients subscribed to this fight's debug channel
    var subscribers = await _clientEventPublisher.GetSubscribersAsync(
        $"debug.fight.{evt.FightId}", ct);

    foreach (var clientId in subscribers)
    {
        var session = await _sessionManager.GetSessionAsync(clientId, ct);

        // Only route to developer sessions
        if (session?.HasPermission("debug:fight_events") == true)
        {
            await _clientEventPublisher.PublishToClientAsync(
                clientId,
                "debug.fight_event",
                evt,
                ct);
        }
    }
}
```

#### In-Game Debug UI Components

| Component | Purpose | Data Source |
|-----------|---------|-------------|
| **Event Log** | Scrolling timeline of all debug events | All debug events |
| **Behavior Tree View** | Visual representation of current evaluation path | `behavior.evaluation` |
| **Opportunity Panel** | Active/recent opportunities with acceptance status | `coordinator.opportunity_*` |
| **State Inspector** | Live view of coordinator internal state | `coordinator.analysis` |
| **Performance Metrics** | Evaluation times, event latencies | Aggregated from all |

This debug system allows developers to:
1. Understand why a monster made a specific decision
2. See what opportunities are being detected and why
3. Tune behavior parameters in real-time
4. Profile ABML evaluation performance
5. Demonstrate the system's intelligence to stakeholders

---

## 6. Visual Assets (Synty)

### 6.1 Recommended Packs

| Asset Need | Synty Pack | Notes |
|------------|------------|-------|
| **Arena Environment** | [POLYGON Fantasy Kingdom](https://syntystore.com/products/polygon-fantasy-kingdom-pack) | Arena structures, pillars |
| **Terrain/Surfaces** | [POLYGON Nature](https://syntystore.com/products/polygon-nature-pack) | Ice, sand textures |
| **Boulders/Rocks** | [POLYGON Nature](https://syntystore.com/products/polygon-nature-pack) | Destructible objects |
| **Monsters** | [POLYGON Fantasy Rivals](https://syntystore.com/products/polygon-fantasy-rivals-pack) | 20 unique creatures with big rig |
| **Backup Monsters** | [POLYGON Dungeon Pack](https://syntystore.com/products/polygon-dungeon-pack) | Rock Golem, skeletons |
| **Effects** | [POLYGON Particle FX](https://syntystore.com/products/polygon-particle-fx-pack) | Attack effects, explosions |

### 6.2 Monster Selection

Based on [POLYGON Fantasy Rivals](https://syntystore.com/products/polygon-fantasy-rivals-pack) availability, here are recommended pairings that showcase different combat styles:

#### Recommended Demo Pairing: **Troll vs Elemental Golem**

| Monster | Type | Fighting Style | Signature Opportunity |
|---------|------|----------------|----------------------|
| **Troll** | Organic Brute | Aggressive, high damage, slower | "Ground Pound" - AOE slam |
| **Elemental Golem** | Magic Construct | Defensive, ranged focus, sturdy | "Energy Burst" - Charged blast |

**Why this pairing works:**
- Visual contrast (organic vs crystalline/magical)
- Playstyle contrast (aggro melee vs defensive ranged)
- Both use "big rig" from Fantasy Rivals (large monsters)
- Clear silhouettes for readability

#### Alternative Pairings

| Pairing | Theme | Notes |
|---------|-------|-------|
| **Red Demon vs Forest Guardian** | Evil vs Nature | Good/evil narrative |
| **Mechanical Golem vs Big Ork** | Construct vs Organic | Tech vs brute force |
| **Barbarian Giant vs Medusa** | Strength vs Magic | Range differential |

#### Monster Stat Differentiation

| Stat | Troll | Elemental Golem |
|------|-------|-----------------|
| **Base Health** | 100 | 100 |
| **Base Stamina** | 100 | 100 |
| **Base Mana** | 100 | 100 |
| **Move Speed** | 2.5 m/s | 3.0 m/s |
| **Weak Attack Damage** | 10-14 | 6-10 |
| **Heavy Attack Damage** | 22-28 | 14-20 |
| **Aggression (default)** | 0.7 | 0.4 |
| **Risk Tolerance (default)** | 0.6 | 0.3 |

The stat differences create emergent behavior: Troll closes distance and trades hits, Golem maintains range and pokes.

### 6.3 Animation Requirements

```
Monster Requirements:
├── Idle animation
├── Walk forward/backward
├── Strafe left/right
├── Weak attack (2-3 variants)
├── Heavy attack (1-2 variants)
├── Ability animations (3 types)
│   ├── Short-range (grab/throw)
│   ├── Medium-range (charge)
│   └── Long-range (projectile)
├── Hit reaction (light)
├── Hit reaction (heavy/stagger)
├── Knockdown
├── Get up from knockdown
├── Death
└── Signature move (per monster)

Environment Requirements:
├── Arena floor (modular tiles)
├── Arena walls/barriers
├── Boulder (intact)
├── Boulder (damaged states)
├── Boulder (destroyed/rubble)
├── Ice surface overlay
├── Sand surface overlay
└── Particle effects (dust, ice shards, magic)
```

---

## 7. Implementation Roadmap

### Phase 1: Basic Arena Combat

- [ ] Arena scene setup in Stride
- [ ] Monster controller (movement, facing)
- [ ] Basic attack system (weak/heavy)
- [ ] Resource system (HP, stamina, mana)
- [ ] Distance-based ability availability

### Phase 2: ABML Integration

- [ ] Create monster combat input/output schemas
- [ ] Write basic combat ABML behavior
- [ ] Compile to BehaviorModel
- [ ] Implement BehaviorModelInterpreter in client
- [ ] Connect behavior output to game actions

### Phase 3: Fight Coordinator

- [ ] Define Fight Coordinator actor schema
- [ ] Implement coordinator analysis logic
- [ ] Opportunity detection (environmental, dramatic)
- [ ] Event tap for combatant streams
- [ ] Opportunity event publishing

### Phase 4: Cinematic Integration

- [ ] Design cinematic ABML documents
- [ ] Implement streaming composition in client
- [ ] Cinematic camera system
- [ ] Control handoff (pause/resume behaviors)

### Phase 5: Polish

- [ ] Visual effects (Synty particles)
- [ ] Audio integration
- [ ] UI for health/stamina/mana
- [ ] Multiple arena configurations
- [ ] Multiple monster types

---

## 8. Open Questions

### Design Questions

1. **Monster Personality Variation**: Should different monsters have different `aggression`/`risk_tolerance` defaults, or should that be equipment/trait-based?

2. **Opportunity Frequency**: How often should opportunities appear? Current design: 5s cooldown minimum. Is that too frequent or too rare?

3. **Cinematic Duration**: Boulder kick is 3.5s. What's the ideal length for keeping pace while being dramatic?

4. **Spectator Mode**: Should there be a spectator view that shows the Fight Coordinator's analysis in real-time?

### Technical Questions

1. **Latency Tolerance**: What's acceptable latency for opportunity events? Currently assuming ~200-500ms is fine for enrichment.

2. **Behavior Hot-Reload**: Can we update ABML behaviors mid-fight for testing?

3. **Determinism**: Should combat be replay-able given same inputs and random seeds?

---

## 9. Future Enhancements

This section documents planned enhancements that are **not part of the initial demo** but are designed to be addable without architectural changes. The initial demo focuses on ABML/AST showcase; these features add combat depth.

### 9.1 Soulslike Guard/Deflection System

A skill-based defensive system inspired by Sekiro and Dark Souls, adding timing-based defensive options.

#### System Overview

```
┌─────────────────────────────────────────────────────────────────────────┐
│                     DEFENSIVE ACTION HIERARCHY                           │
│                                                                          │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐         │
│  │    DEFLECT      │  │     BLOCK       │  │     DODGE       │         │
│  │   (Perfect)     │  │   (Held)        │  │   (Sidestep)    │         │
│  ├─────────────────┤  ├─────────────────┤  ├─────────────────┤         │
│  │ Timing: ~100ms  │  │ Timing: Hold    │  │ Timing: Any     │         │
│  │ Damage: 0%      │  │ Damage: 50%     │  │ Damage: 0%      │         │
│  │ Stamina: 10     │  │ Stamina: 10+5/s │  │ Stamina: 15     │         │
│  │ Recovery: None  │  │ Recovery: 0.3s  │  │ Recovery: 0.4s  │         │
│  │ Reward: High    │  │ Reward: Low     │  │ Reward: Medium  │         │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘         │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

#### Blocking Mechanics

| Property | Value | Notes |
|----------|-------|-------|
| **Initiation Time** | 0.3 seconds | "Heavy" feel, commitment required |
| **Release Time** | 0.3 seconds | Can't instantly attack after blocking |
| **Activation Cost** | 10 stamina | One-time cost when block starts |
| **Hold Cost** | 5 stamina/second | Continuous drain while blocking |
| **Hit Cost** | 5 stamina/hit | Additional drain per blocked attack |
| **Damage Reduction** | 50% | Half damage taken while blocking |
| **Stagger on Break** | Yes | If stamina depletes while blocking |

**Block State Machine:**
```
NEUTRAL ──[Block pressed]──► BLOCK_STARTUP (0.3s)
                                    │
                                    ▼
                              BLOCKING (active)
                                    │
                    ┌───────────────┴───────────────┐
                    │                               │
           [Block released]              [Stamina depleted]
                    │                               │
                    ▼                               ▼
           BLOCK_RELEASE (0.3s)              STAGGERED (1.0s)
                    │
                    ▼
                NEUTRAL
```

#### Deflection Mechanics

| Property | Value | Notes |
|----------|-------|-------|
| **Window** | 100ms | Near-perfect timing required |
| **Cost** | 10 stamina | Charged whether successful or not |
| **Damage on Success** | 0% | Complete negation |
| **Cooldown** | 0.3 seconds | Prevents button mashing |
| **Input Type** | Button press | NOT hold - press during attack contact |
| **Failed Deflect** | Becomes block | Auto-transitions to block state |
| **Visual Feedback** | Spark effect | Clear indicator of success |
| **Audio Feedback** | Distinct "clang" | Different from normal block sound |

**Deflection Input Rules:**
```
┌─────────────────────────────────────────────────────────────────────────┐
│                      DEFLECTION INPUT HANDLING                           │
│                                                                          │
│  Input: [Deflect button press]                                          │
│                                                                          │
│  IF time_since_last_deflect_input < 300ms:                              │
│      → IGNORE (anti-spam)                                               │
│                                                                          │
│  IF currently_in_deflect_window:                                        │
│      → IGNORE (already attempting)                                       │
│                                                                          │
│  ELSE:                                                                   │
│      → Consume 10 stamina                                                │
│      → Open deflect window (100ms)                                       │
│      → IF enemy_attack_contacts during window:                          │
│          → SUCCESS: Negate damage, play deflect effects                 │
│      → ELSE:                                                             │
│          → MISS: Transition to BLOCK_STARTUP                             │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

#### Undeflectable Attacks (Red Flash)

Certain attacks **cannot be deflected** and must be dodged:

| Attack Type | Visual Indicator | Deflectable | Blockable |
|-------------|------------------|-------------|-----------|
| Normal Attack | None | Yes | Yes (50%) |
| Heavy Attack | None | Yes | Yes (50%) |
| Ability | **Red flash** (0.5s) | **No** | Yes (50%) |
| Signature Move | **Red flash** (0.5s) | **No** | **No** |

**Red Flash Timing:**
- Flash appears 0.5 seconds before attack lands
- Gives player time to recognize and dodge instead of deflect
- All three abilities (short/medium/long range) trigger red flash
- Signature opportunity moves also trigger red flash

#### ABML Behavior Integration

The deflection system adds new inputs to the behavior model:

```yaml
# Additional inputs for Soulslike combat
inputs:
  # Defensive state
  can_block: bool           # Not in block cooldown
  can_deflect: bool         # Deflect cooldown expired
  in_block: bool            # Currently blocking
  block_stamina_drain: float  # Current stamina drain rate

  # Enemy attack analysis
  enemy_attack_deflectable: bool   # Can this attack be deflected?
  enemy_attack_timing_ms: int      # Time until attack lands
  enemy_attack_is_heavy: bool      # Is this a red-flash attack?

outputs:
  # Defensive actions
  defensive_action: string    # "block", "deflect", "dodge", "none"
```

#### Stamina Economy Comparison

| Defensive Action | Base Cost | Success Benefit | Failure Cost |
|-----------------|-----------|-----------------|--------------|
| **Deflect** | 10 | No damage, no recovery | 10 + block transition |
| **Block** | 10 + 5/s + 5/hit | 50% damage | Potential stagger |
| **Dodge** | 15 | No damage | Position changed |

### 9.2 DDR-Style Action Prompts (Legend of Dragoon)

For certain abilities, a rhythm-based prompt system determines attack effectiveness.

#### Concept

When executing specific abilities, a **DDR-style prompt sequence** appears:
- Player must hit button prompts in time with the animation
- More successful hits = more projectiles / higher damage multiplier
- Failure doesn't cancel the attack, just reduces effectiveness

```
┌─────────────────────────────────────────────────────────────────────────┐
│                      DDR PROMPT EXAMPLE                                  │
│                                                                          │
│  Ability: "Volley" (Long-range, shoots multiple projectiles)            │
│                                                                          │
│  Animation plays, prompts appear in sequence:                           │
│                                                                          │
│      [●]────────────────────[●]────────────────────[●]───────►          │
│       ↑                      ↑                      ↑                    │
│    Hit 1 (100ms)         Hit 2 (100ms)         Hit 3 (100ms)           │
│                                                                          │
│  Results:                                                                │
│  • 0 hits: 1 projectile  (base damage)                                  │
│  • 1 hit:  2 projectiles (133% damage)                                  │
│  • 2 hits: 3 projectiles (166% damage)                                  │
│  • 3 hits: 4 projectiles (200% damage)                                  │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

#### Integration with ABML

The DDR prompt is part of the **ability's AST** - it's defined in the ability animation, not hardcoded:

```yaml
# Ability definition with DDR prompts
abilities:
  volley:
    type: long_range
    base_projectiles: 1
    animation: "volley_throw"
    ddr_sequence:
      prompts:
        - timing_ms: 200
          window_ms: 100
          bonus_projectiles: 1
        - timing_ms: 500
          window_ms: 100
          bonus_projectiles: 1
        - timing_ms: 800
          window_ms: 100
          bonus_projectiles: 1
      on_perfect: # All hits
        damage_multiplier: 2.0
        effect: "volley_perfect_vfx"
```

#### Which Abilities Get DDR Prompts

| Ability | DDR Prompts | Scaling |
|---------|-------------|---------|
| Weak Attack | No | Fixed damage |
| Heavy Attack | No | Fixed damage |
| Short-Range Ability | No | Fixed damage |
| Medium-Range Ability | Optional (2 prompts) | 100%/133%/166% |
| Long-Range Ability | Yes (3 prompts) | 100%/133%/166%/200% |
| Signature Move | Yes (4-5 prompts) | Scales dramatically |

### 9.3 Implementation Notes

These systems are designed to be **additive** - they layer on top of the base combat without requiring architectural changes:

| System | ABML Changes | Client Changes | Server Changes |
|--------|--------------|----------------|----------------|
| **Block/Deflect** | New inputs/outputs | State machine, timing | None (client authority) |
| **Red Flash** | Ability metadata | Visual indicator | None |
| **DDR Prompts** | Ability sequences | Prompt UI, input handler | None |

**Why these are deferred:**
1. They add significant complexity to the base combat
2. The initial demo focuses on ABML/AST architecture showcase
3. These are "combat depth" features, not "AI behavior" features
4. They can be added incrementally without redesign

**When to add them:**
1. After the base demo is fully functional
2. When expanding to player-controlled combat
3. When showcasing advanced AST features (DDR sequences)

---

*Document created: 2025-12-30*
*Last updated: 2025-12-30*
*This is a case study design document for demonstrating ABML/behavior systems.*
