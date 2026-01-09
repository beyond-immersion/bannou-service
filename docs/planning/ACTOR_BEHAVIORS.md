# Actor Behaviors: From Character Creation to Compiled Bytecode

> **Status**: Planning Document
> **Last Updated**: 2026-01-08
> **Related**: [ABML Guide](../guides/ABML.md), [GOAP Guide](../guides/GOAP.md), [Actor System](../guides/ACTOR_SYSTEM.md)

This document describes the complete flow from game server character creation through the cognition pipeline to compiled behavior bytecode execution.

---

## Table of Contents

1. [Overview](#overview)
2. [The Character Creation Flow](#the-character-creation-flow)
3. [NPC Actor Behaviors](#npc-actor-behaviors)
4. [The Cognition Pipeline](#the-cognition-pipeline)
5. [Event Actor Behaviors](#event-actor-behaviors)
6. [Behavior Compilation](#behavior-compilation)
7. [Implementation Examples](#implementation-examples)

---

## Overview

### The Big Picture

```
Game Server                    Bannou Services                      Game Runtime
    │                               │                                    │
    │  1. Create Character          │                                    │
    ├──────────────────────────────►│                                    │
    │                               │                                    │
    │                    ┌──────────┴──────────┐                         │
    │                    │  Character Actor    │                         │
    │                    │  - Stores backstory │                         │
    │                    │  - Manages state    │                         │
    │                    │  - Selects behavior │                         │
    │                    └──────────┬──────────┘                         │
    │                               │                                    │
    │  2. Get Behavior Bytecode     │                                    │
    ├──────────────────────────────►│                                    │
    │                               │                                    │
    │                    ┌──────────┴──────────┐                         │
    │                    │  Behavior Service   │                         │
    │                    │  - Compiles ABML    │                         │
    │                    │  - Returns bytecode │                         │
    │                    └──────────┬──────────┘                         │
    │                               │                                    │
    │  3. Compiled Behavior         │                                    │
    │◄──────────────────────────────┤                                    │
    │                               │                                    │
    │  4. Execute Bytecode          │                                    │
    ├───────────────────────────────┼───────────────────────────────────►│
    │                               │                                    │
    │                               │  5. Perception Events              │
    │                               │◄───────────────────────────────────┤
    │                               │                                    │
    │                    ┌──────────┴──────────┐                         │
    │                    │  Cognition Pipeline │                         │
    │                    │  - Process events   │                         │
    │                    │  - Update emotions  │                         │
    │                    │  - Store memories   │                         │
    │                    │  - Adjust goals     │                         │
    │                    └──────────┬──────────┘                         │
    │                               │                                    │
    │                               │  6. Behavior Updates               │
    │                               ├───────────────────────────────────►│
    │                               │                                    │
```

### Key Components

| Component | Responsibility | Location |
|-----------|---------------|----------|
| Character Actor | Stores character data, manages emotional state, selects behaviors | lib-character |
| Behavior Service | Compiles ABML to bytecode, validates behaviors | lib-behavior |
| Cognition Pipeline | Processes perception → emotions → memory → goals | Within character actor |
| Event Actor | Orchestrates multi-character events using regional data | lib-character (event actors) |
| Mapping Channel | Caches character/prop locations by region | lib-mapping |

---

## The Character Creation Flow

When the game server needs to create a new NPC character:

### Step 1: Character Creation Request

```yaml
# Game server sends character creation request
POST /character/create
{
  "template_id": "guard_city",
  "spawn_location": {
    "region_id": "stormwind_market",
    "position": { "x": 100.5, "y": 0.0, "z": 45.2 }
  },
  "customization": {
    "name": "Marcus",
    "personality_traits": ["dutiful", "cautious", "protective"],
    "backstory_seed": "veteran_soldier"
  }
}
```

### Step 2: Character Actor Activation

The Character Actor (virtual actor) activates and:

1. **Generates/retrieves backstory** from lib-character storage
2. **Initializes emotional state** with baseline values
3. **Loads personality configuration** affecting behavior weights
4. **Selects appropriate behavior stack** based on role and context

```csharp
// Character Actor initialization (conceptual)
public async Task OnActivateAsync()
{
    // Load or generate character data
    _characterData = await _characterStore.GetOrCreateAsync(CharacterId, template);

    // Initialize cognition state
    _emotionalState = new EmotionalState(_characterData.PersonalityTraits);
    _memoryStore = new MemoryStore(CharacterId);
    _goalManager = new GoalManager(_characterData.BaseGoals);

    // Select behavior layers
    _behaviorStack = await SelectBehaviorStackAsync();
}
```

### Step 3: Behavior Selection

The character actor selects behaviors based on:

```yaml
# Behavior stack selection logic
behavior_stack:
  # Layer 1: Foundation (always active)
  base: "shared/humanoid-base"

  # Layer 2: Role-specific (based on character template)
  role: "${character.template.role_behavior}"  # e.g., "guard-patrol"

  # Layer 3: Situational (activated by conditions)
  situational:
    - behavior: "shared/combat-stance"
      condition: "${active_threats.length > 0}"
    - behavior: "shared/conversation-mode"
      condition: "${in_dialogue}"
    - behavior: "shared/rest-mode"
      condition: "${energy < 0.2 && !active_threats}"
```

### Step 4: Behavior Compilation

The Behavior Service compiles the selected ABML stack to bytecode:

```yaml
# Compilation request
POST /behavior/compile
{
  "character_id": "chr_marcus_001",
  "behavior_stack": [
    "shared/humanoid-base",
    "guard-patrol",
    "shared/combat-stance"
  ],
  "context": {
    "personality": { "aggression": 0.3, "caution": 0.7 },
    "equipment": { "has_shield": true, "weapon_type": "sword" },
    "role": "city_guard"
  }
}
```

Response contains compiled bytecode for the game runtime:

```yaml
# Compilation response
{
  "bytecode_version": "2.0",
  "bytecode": "<base64-encoded compiled behavior>",
  "expression_vm": {
    "registers": 32,
    "constants": [...],
    "functions": [...]
  },
  "active_flows": ["ambient_idle", "breathing", "blinking"],
  "event_handlers": {
    "perception.loud_noise": "react_to_loud_noise",
    "social.greeted": "acknowledge_greeting",
    "combat.damaged": "react_to_damage"
  }
}
```

---

## NPC Actor Behaviors

### Behavior Layer Architecture

NPC behaviors are composed in layers, with higher layers able to override lower ones:

```
┌─────────────────────────────────────────────────────────┐
│  Layer 3: Situational Overlays                          │
│  (combat-stance, conversation-mode, fleeing)            │
│  - Activated by specific conditions                     │
│  - High urgency intents override lower layers           │
├─────────────────────────────────────────────────────────┤
│  Layer 2: Role-Specific Behaviors                       │
│  (guard-patrol, merchant-shop, innkeeper)               │
│  - Character's primary occupation/purpose               │
│  - Contains role-specific flows and goals               │
├─────────────────────────────────────────────────────────┤
│  Layer 1: Humanoid Base                                 │
│  (humanoid-base)                                        │
│  - Universal behaviors all humanoids share              │
│  - Breathing, blinking, fatigue, damage reactions       │
└─────────────────────────────────────────────────────────┘
```

### Intent Resolution

When multiple layers emit intents on the same channel, resolution uses urgency:

```yaml
# Example: Combat layer wants attention, base layer has ambient glance
# From combat-stance (Layer 3):
- emit_intent:
    channel: attention
    action: combat_awareness
    primary_target: "${current_target}"
    urgency: 0.9  # High urgency

# From humanoid-base (Layer 1):
- emit_intent:
    channel: attention
    action: ambient_glance
    frequency: low
    urgency: 0.1  # Low urgency

# Resolution: Combat awareness wins (0.9 > 0.1)
```

### Complete NPC Actor Behavior Example

Here's a complete example of a **City Guard** NPC behavior that integrates with the cognition pipeline:

```yaml
# =============================================================================
# City Guard Behavior
# Role-specific behavior for city guard NPCs.
# Integrates with cognition pipeline for perception-driven responses.
# =============================================================================

version: "2.0"

metadata:
  id: guard-city
  type: behavior
  description: "City guard patrol and response behaviors"
  tags:
    - role
    - guard
    - city

imports:
  - path: "shared/humanoid-base"
    as: base
  - path: "shared/combat-stance"
    as: combat

# -----------------------------------------------------------------------------
# CONTEXT - Bindings to character actor state
# -----------------------------------------------------------------------------
context:
  variables:
    # From character actor cognition state
    emotional_state:
      source: "${agent.cognition.emotional_state}"
      type: object
    current_mood:
      source: "${agent.cognition.emotional_state.dominant_emotion}"
      type: string
    stress_level:
      source: "${agent.cognition.emotional_state.stress}"
      type: float

    # Memory access
    recent_memories:
      source: "${agent.cognition.memory.recent(10)}"
      type: array
    threat_memories:
      source: "${agent.cognition.memory.query('threat', 24h)}"
      type: array

    # Goals from cognition
    active_goals:
      source: "${agent.cognition.goals.active}"
      type: array
    primary_goal:
      source: "${agent.cognition.goals.highest_priority}"
      type: object

    # Combat preferences (influenced by personality + experience)
    combat_style:
      source: "${agent.cognition.combat_preferences.style}"
      type: string
      default: "balanced"
    aggression_modifier:
      source: "${agent.cognition.combat_preferences.aggression}"
      type: float
      default: 0.5

    # Perception
    visible_characters:
      source: "${agent.perception.characters}"
      type: array
    suspicious_characters:
      source: "${agent.perception.filter(c => c.suspicion > 0.3)}"
      type: array
    active_threats:
      source: "${agent.combat.active_threats}"
      type: array

    # Patrol data
    patrol_route:
      source: "${agent.config.patrol_route}"
      type: array
    current_waypoint:
      source: "${agent.state.current_waypoint}"
      type: int
      default: 0
    patrol_paused:
      source: "${agent.state.patrol_paused}"
      type: bool
      default: false

    # Duty status
    on_duty:
      source: "${agent.schedule.is_duty_time}"
      type: bool
    shift_end_soon:
      source: "${agent.schedule.minutes_until_shift_end < 30}"
      type: bool

# -----------------------------------------------------------------------------
# GOAP GOALS - What the guard wants to achieve
# -----------------------------------------------------------------------------
goals:
  maintain_order:
    description: "Keep the peace in patrol area"
    priority: 70
    conditions:
      area_peaceful: true
      no_active_crimes: true

  complete_patrol:
    description: "Complete current patrol circuit"
    priority: 50
    conditions:
      patrol_complete: true

  investigate_suspicious:
    description: "Check out suspicious activity"
    priority: 75
    conditions:
      suspicion_resolved: true

  respond_to_crime:
    description: "Stop active criminal activity"
    priority: 90
    conditions:
      crime_stopped: true

  protect_citizens:
    description: "Keep civilians safe from harm"
    priority: 95
    conditions:
      citizens_safe: true

# -----------------------------------------------------------------------------
# EVENT HANDLERS - Perception events trigger cognition updates
# -----------------------------------------------------------------------------
events:
  # Perception events update emotional state and memory
  - pattern: "perception.character_spotted"
    handler: process_character_sighting

  - pattern: "perception.suspicious_activity"
    handler: evaluate_suspicion

  - pattern: "perception.crime_witnessed"
    handler: respond_to_crime

  - pattern: "perception.citizen_distress"
    handler: check_citizen_welfare

  - pattern: "perception.threat_detected"
    handler: assess_threat

  # Social events
  - pattern: "social.addressed_by_citizen"
    handler: citizen_interaction

  - pattern: "social.addressed_by_authority"
    handler: authority_interaction

  # Duty events
  - pattern: "schedule.shift_ending"
    handler: prepare_shift_change

# -----------------------------------------------------------------------------
# FLOWS
# -----------------------------------------------------------------------------
flows:
  # ===========================================================================
  # MAIN PATROL LOOP
  # ===========================================================================

  patrol_duty:
    description: "Main patrol loop during duty hours"
    triggers:
      - condition: "${on_duty && !patrol_paused && active_threats.length == 0}"

    # GOAP annotations for planning
    goap:
      preconditions:
        on_duty: true
        patrol_paused: false
        in_combat: false
      effects:
        patrol_progress: "+1"
      cost: 1

    actions:
      # Emit patrol stance based on stress level
      - cond:
          - when: "${stress_level > 0.6}"
            then:
              - emit_intent:
                  channel: stance
                  action: alert
                  urgency: 0.5
          - else:
              - emit_intent:
                  channel: stance
                  action: relaxed_alert
                  urgency: 0.3

      # Move to next waypoint
      - emit_intent:
          channel: movement
          action: walk_to
          target: "${patrol_route[current_waypoint]}"
          style: patrol
          urgency: 0.4

      # Ambient awareness - scan surroundings
      - emit_intent:
          channel: attention
          action: scan_area
          pattern: patrol_sweep
          urgency: 0.35

      # Occasional greetings to citizens (mood-dependent)
      - cond:
          - when: "${current_mood == 'content' && random() < 0.3}"
            then:
              - call: { flow: greet_passerby }

      # Wait at waypoint briefly
      - wait: { duration: "2.0 + random() * 3.0" }

      # Advance to next waypoint
      - set:
          variable: current_waypoint
          value: "${(current_waypoint + 1) % patrol_route.length}"

      # Continue patrol
      - goto: { flow: patrol_duty }

  # ===========================================================================
  # PERCEPTION EVENT HANDLERS
  # ===========================================================================

  process_character_sighting:
    description: "Process seeing a character - update cognition"
    actions:
      - set:
          variable: spotted_character
          value: "${event.character}"

      # Check memory for this character
      - set:
          variable: character_memory
          value: "${agent.cognition.memory.query_entity(spotted_character.id)}"

      # Update emotional response based on memory
      - cond:
          - when: "${character_memory.has_threat_flag}"
            then:
              # Known troublemaker - increase alertness
              - call: { flow: cognition_increase_stress, args: { amount: 0.2 } }
              - call: { flow: cognition_store_memory, args: {
                  type: "sighting",
                  entity: "${spotted_character.id}",
                  context: "spotted_known_threat",
                  emotional_weight: 0.6
                }}
              - goto: { flow: watch_suspicious_character }

          - when: "${character_memory.is_known_citizen}"
            then:
              # Familiar face - slight positive
              - call: { flow: cognition_adjust_emotion, args: {
                  emotion: "comfort",
                  delta: 0.05
                }}

          - when: "${spotted_character.appearance.armed && !spotted_character.is_guard}"
            then:
              # Armed non-guard - note for observation
              - call: { flow: cognition_store_memory, args: {
                  type: "observation",
                  entity: "${spotted_character.id}",
                  context: "armed_civilian",
                  emotional_weight: 0.2
                }}

  evaluate_suspicion:
    description: "Evaluate suspicious activity"
    actions:
      - set:
          variable: activity
          value: "${event.activity}"

      # Store in memory
      - call: { flow: cognition_store_memory, args: {
          type: "suspicion",
          location: "${activity.location}",
          description: "${activity.description}",
          entities: "${activity.involved_entities}",
          emotional_weight: 0.4
        }}

      # Increase stress/alertness
      - call: { flow: cognition_increase_stress, args: { amount: 0.15 } }

      # Adjust goals - investigation becomes priority
      - call: { flow: cognition_boost_goal, args: {
          goal: "investigate_suspicious",
          boost: 20
        }}

      # Pause patrol to investigate
      - set:
          variable: patrol_paused
          value: true

      - goto: { flow: investigate_activity }

  assess_threat:
    description: "Assess detected threat and update cognition"
    actions:
      - set:
          variable: threat
          value: "${event.threat}"

      # Major cognition update for threats
      - call: { flow: cognition_store_memory, args: {
          type: "threat",
          entity: "${threat.source}",
          threat_level: "${threat.level}",
          context: "${threat.context}",
          emotional_weight: 0.8
        }}

      # Significant stress increase
      - call: { flow: cognition_increase_stress, args: { amount: 0.4 } }

      # Emotional shift to alertness/fear based on threat level
      - cond:
          - when: "${threat.level > 0.7}"
            then:
              - call: { flow: cognition_adjust_emotion, args: {
                  emotion: "fear",
                  delta: 0.3
                }}
          - else:
              - call: { flow: cognition_adjust_emotion, args: {
                  emotion: "alertness",
                  delta: 0.4
                }}

      # Update combat preferences based on threat
      - call: { flow: cognition_update_combat_preferences, args: {
          threat_type: "${threat.type}",
          threat_level: "${threat.level}"
        }}

      # Activate combat layer
      - layer_activate: { behavior: "combat" }

  # ===========================================================================
  # COGNITION PIPELINE FLOWS
  # ===========================================================================

  cognition_store_memory:
    description: "Store a new memory with emotional context"
    params:
      type: string
      entity: string?
      location: position?
      description: string?
      context: string
      emotional_weight: float
      entities: array?
      threat_level: float?
    actions:
      - emit_intent:
          channel: cognition
          action: store_memory
          memory:
            type: "${params.type}"
            timestamp: "${world.time.now}"
            location: "${params.location ?? agent.position}"
            entity_id: "${params.entity}"
            entities: "${params.entities}"
            description: "${params.description}"
            context: "${params.context}"
            threat_level: "${params.threat_level}"
            emotional_weight: "${params.emotional_weight}"
            emotional_state_snapshot: "${emotional_state}"
          urgency: 0.8

  cognition_increase_stress:
    description: "Increase stress level with decay consideration"
    params:
      amount: float
    actions:
      - emit_intent:
          channel: cognition
          action: modify_emotion
          emotion: stress
          operation: add
          value: "${params.amount}"
          max: 1.0
          urgency: 0.7

  cognition_adjust_emotion:
    description: "Adjust an emotional dimension"
    params:
      emotion: string
      delta: float
    actions:
      - emit_intent:
          channel: cognition
          action: modify_emotion
          emotion: "${params.emotion}"
          operation: add
          value: "${params.delta}"
          urgency: 0.6

  cognition_boost_goal:
    description: "Temporarily boost a goal's priority"
    params:
      goal: string
      boost: int
    actions:
      - emit_intent:
          channel: cognition
          action: modify_goal
          goal_id: "${params.goal}"
          priority_delta: "${params.boost}"
          duration: 300  # 5 minutes
          urgency: 0.75

  cognition_update_combat_preferences:
    description: "Update combat preferences based on threat assessment"
    params:
      threat_type: string
      threat_level: float
    actions:
      # Adjust aggression based on threat level and personality
      - set:
          variable: base_aggression
          value: "${agent.personality.aggression}"

      - cond:
          # High threat + low base aggression = more defensive
          - when: "${threat_level > 0.7 && base_aggression < 0.4}"
            then:
              - emit_intent:
                  channel: cognition
                  action: set_combat_preference
                  style: defensive
                  aggression: "${base_aggression * 0.8}"
                  urgency: 0.7

          # High threat + high base aggression = aggressive response
          - when: "${threat_level > 0.7 && base_aggression >= 0.6}"
            then:
              - emit_intent:
                  channel: cognition
                  action: set_combat_preference
                  style: aggressive
                  aggression: "${min(base_aggression * 1.2, 1.0)}"
                  urgency: 0.7

          # Moderate threat = balanced
          - else:
              - emit_intent:
                  channel: cognition
                  action: set_combat_preference
                  style: balanced
                  aggression: "${base_aggression}"
                  urgency: 0.6

  # ===========================================================================
  # INVESTIGATION FLOWS
  # ===========================================================================

  investigate_activity:
    description: "Investigate suspicious activity"
    goap:
      preconditions:
        suspicious_activity_detected: true
      effects:
        suspicion_resolved: true
      cost: 3

    actions:
      # Approach carefully
      - emit_intent:
          channel: movement
          action: approach
          target: "${activity.location}"
          style: cautious
          urgency: 0.6

      - emit_intent:
          channel: stance
          action: alert
          urgency: 0.55

      # Observe
      - emit_intent:
          channel: attention
          action: focus
          target: "${activity.location}"
          urgency: 0.7

      - wait: { duration: 3.0 }

      # Evaluate what we see
      - cond:
          - when: "${activity.resolved}"
            then:
              # False alarm - reduce stress, resume patrol
              - call: { flow: cognition_increase_stress, args: { amount: -0.1 } }
              - set:
                  variable: patrol_paused
                  value: false
              - goto: { flow: patrol_duty }

          - when: "${activity.is_crime}"
            then:
              - goto: { flow: respond_to_crime }

          - else:
              # Still suspicious - continue observation
              - call: { flow: question_individuals }

  watch_suspicious_character:
    description: "Keep eye on known troublemaker"
    actions:
      - emit_intent:
          channel: attention
          action: track
          target: "${spotted_character.id}"
          covert: true
          urgency: 0.6

      # Don't stare - periodic glances
      - wait: { duration: "5.0 + random() * 5.0" }

      # Check if they're still visible and behaving
      - cond:
          - when: "${!spotted_character.visible}"
            then:
              # Lost sight - note in memory
              - call: { flow: cognition_store_memory, args: {
                  type: "observation",
                  entity: "${spotted_character.id}",
                  context: "lost_visual_on_suspect",
                  emotional_weight: 0.2
                }}
              - return: {}

          - when: "${spotted_character.doing_something_suspicious}"
            then:
              - goto: { flow: confront_suspect }

          - else:
              # Continue watching
              - goto: { flow: watch_suspicious_character }

  # ===========================================================================
  # RESPONSE FLOWS
  # ===========================================================================

  respond_to_crime:
    description: "Active response to witnessed crime"
    goap:
      preconditions:
        crime_detected: true
      effects:
        crime_stopped: true
      cost: 5

    actions:
      # Store memory of the crime
      - call: { flow: cognition_store_memory, args: {
          type: "crime",
          entity: "${event.perpetrator}",
          context: "${event.crime_type}",
          location: "${event.location}",
          emotional_weight: 0.7
        }}

      # Significant stress from witnessing crime
      - call: { flow: cognition_increase_stress, args: { amount: 0.3 } }

      # Verbal challenge
      - emit_intent:
          channel: speech
          action: shout
          text: "Halt! City Guard!"
          urgency: 0.85

      # Move to intercept
      - emit_intent:
          channel: movement
          action: sprint_to
          target: "${event.perpetrator}"
          urgency: 0.8

      # Draw weapon
      - emit_intent:
          channel: combat
          action: ready_weapon
          urgency: 0.75

      # Assess perpetrator response
      - wait: { duration: 1.5 }

      - cond:
          - when: "${event.perpetrator.fleeing}"
            then:
              - goto: { flow: pursue_suspect }

          - when: "${event.perpetrator.surrendering}"
            then:
              - goto: { flow: arrest_suspect }

          - when: "${event.perpetrator.hostile}"
            then:
              - layer_activate: { behavior: "combat" }

          - else:
              - goto: { flow: confront_suspect }

  # ===========================================================================
  # INTERACTION FLOWS
  # ===========================================================================

  citizen_interaction:
    description: "Handle being addressed by a citizen"
    actions:
      # Pause patrol
      - set:
          variable: patrol_paused
          value: true

      # Face the citizen
      - emit_intent:
          channel: attention
          action: look_at
          target: "${event.source.id}"
          urgency: 0.5

      # Response based on mood and stress
      - cond:
          - when: "${stress_level > 0.6}"
            then:
              # Stressed - curt response
              - emit_intent:
                  channel: expression
                  action: stern
                  urgency: 0.4
              - emit_intent:
                  channel: speech
                  action: speak
                  text: "What is it? Make it quick."
                  tone: curt
                  urgency: 0.5

          - when: "${current_mood == 'content'}"
            then:
              # Good mood - friendly
              - emit_intent:
                  channel: expression
                  action: slight_smile
                  urgency: 0.3
              - emit_intent:
                  channel: speech
                  action: speak
                  text: "How can I help you, citizen?"
                  tone: friendly
                  urgency: 0.5

          - else:
              # Neutral
              - emit_intent:
                  channel: expression
                  action: neutral
                  urgency: 0.25
              - emit_intent:
                  channel: speech
                  action: speak
                  text: "Yes?"
                  tone: professional
                  urgency: 0.5

      # Transition to dialogue system
      - emit_intent:
          channel: dialogue
          action: begin_conversation
          partner: "${event.source.id}"
          urgency: 0.6

  greet_passerby:
    description: "Casual greeting to passing citizen"
    actions:
      # Find a nearby citizen to greet
      - set:
          variable: citizen
          value: "${visible_characters.filter(c => c.is_civilian && c.distance < 5)[0]}"

      - cond:
          - when: "${citizen != null}"
            then:
              # Brief acknowledgment
              - emit_intent:
                  channel: attention
                  action: glance
                  target: "${citizen.id}"
                  urgency: 0.25

              - emit_intent:
                  channel: expression
                  action: nod
                  urgency: 0.2

              # Maybe a word
              - cond:
                  - when: "${random() < 0.5}"
                    then:
                      - emit_intent:
                          channel: speech
                          action: speak
                          text: "${pick(['Morning.', 'Citizen.', 'Stay safe.'])}"
                          tone: brief
                          urgency: 0.2

      - return: {}

  # ===========================================================================
  # UTILITY FLOWS
  # ===========================================================================

  confront_suspect:
    description: "Verbal confrontation with suspect"
    actions:
      - emit_intent:
          channel: stance
          action: authoritative
          urgency: 0.6

      - emit_intent:
          channel: attention
          action: lock_on
          target: "${spotted_character.id}"
          urgency: 0.7

      - emit_intent:
          channel: speech
          action: speak
          text: "You there. Hold it. I have some questions."
          tone: commanding
          urgency: 0.65

      - wait: { duration: 2.0 }

      # Continue based on response...
      - return: {}

  pursue_suspect:
    description: "Chase fleeing suspect"
    actions:
      - emit_intent:
          channel: speech
          action: shout
          text: "Stop! You won't get far!"
          urgency: 0.7

      - emit_intent:
          channel: movement
          action: sprint
          target: "${event.perpetrator}"
          urgency: 0.85

      # Store memory of pursuit
      - call: { flow: cognition_store_memory, args: {
          type: "pursuit",
          entity: "${event.perpetrator.id}",
          context: "suspect_fled",
          emotional_weight: 0.5
        }}

      # Pursuit logic continues...
      - return: {}

  arrest_suspect:
    description: "Arrest surrendering suspect"
    actions:
      - emit_intent:
          channel: stance
          action: authoritative
          urgency: 0.6

      - emit_intent:
          channel: speech
          action: speak
          text: "On the ground. Hands behind your back. Now."
          tone: commanding
          urgency: 0.7

      # Arrest procedure...
      - return: {}

  question_individuals:
    description: "Question people about suspicious activity"
    actions:
      # Find nearby witnesses
      - set:
          variable: witnesses
          value: "${visible_characters.filter(c => c.distance < 10 && c.is_civilian)}"

      - cond:
          - when: "${witnesses.length > 0}"
            then:
              - emit_intent:
                  channel: attention
                  action: look_at
                  target: "${witnesses[0].id}"
                  urgency: 0.5

              - emit_intent:
                  channel: speech
                  action: speak
                  text: "Excuse me. Did you see what happened here?"
                  tone: professional
                  urgency: 0.55

      - return: {}

  prepare_shift_change:
    description: "Wind down patrol as shift ends"
    actions:
      # Slight mood lift - shift ending
      - call: { flow: cognition_adjust_emotion, args: {
          emotion: "relief",
          delta: 0.15
        }}

      # Head toward guardhouse
      - emit_intent:
          channel: movement
          action: walk_to
          target: "${agent.config.guardhouse_location}"
          style: casual
          urgency: 0.4

      - emit_intent:
          channel: expression
          action: relax
          urgency: 0.3

      - return: {}
```

---

## The Cognition Pipeline

The cognition pipeline processes perception events and updates character state to influence behavior selection.

### Pipeline Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           COGNITION PIPELINE                                 │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐    ┌───────────┐ │
│  │  Perception  │───►│   Emotion    │───►│   Memory     │───►│   Goals   │ │
│  │   Events     │    │   Update     │    │   Storage    │    │  Update   │ │
│  └──────────────┘    └──────────────┘    └──────────────┘    └───────────┘ │
│         │                   │                   │                   │       │
│         │                   ▼                   ▼                   ▼       │
│         │            ┌──────────────┐    ┌──────────────┐    ┌───────────┐ │
│         │            │  Emotional   │    │   Memory     │    │   Goal    │ │
│         │            │    State     │    │    Store     │    │  Manager  │ │
│         │            │              │    │              │    │           │ │
│         │            │ - stress     │    │ - recent[]   │    │ - active  │ │
│         │            │ - mood       │    │ - threats[]  │    │ - boosts  │ │
│         │            │ - alertness  │    │ - entities{} │    │ - weights │ │
│         │            └──────────────┘    └──────────────┘    └───────────┘ │
│         │                   │                   │                   │       │
│         │                   └───────────────────┴───────────────────┘       │
│         │                                       │                           │
│         │                                       ▼                           │
│         │                              ┌──────────────────┐                 │
│         │                              │ Combat Preference│                 │
│         │                              │     Update       │                 │
│         │                              │                  │                 │
│         │                              │ - style          │                 │
│         │                              │ - aggression     │                 │
│         │                              │ - tactics        │                 │
│         │                              └──────────────────┘                 │
│         │                                       │                           │
│         │                                       ▼                           │
│         │                              ┌──────────────────┐                 │
│         └─────────────────────────────►│ Behavior Select  │                 │
│                                        │                  │                 │
│                                        │ - layer weights  │                 │
│                                        │ - flow triggers  │                 │
│                                        │ - urgency calc   │                 │
│                                        └──────────────────┘                 │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Emotional State Model

```yaml
# Emotional state structure (stored in character actor)
emotional_state:
  # Primary emotions (0.0 to 1.0)
  dimensions:
    stress: 0.35          # Current stress level
    alertness: 0.5        # Vigilance level
    fear: 0.0             # Fear response
    anger: 0.0            # Anger/frustration
    joy: 0.3              # Happiness
    sadness: 0.0          # Grief/melancholy
    comfort: 0.4          # Feeling of safety
    curiosity: 0.2        # Interest in surroundings

  # Derived values
  dominant_emotion: "comfort"  # Highest dimension
  mood: "content"             # Aggregated mood label

  # Decay rates (per second)
  decay:
    stress: 0.01
    fear: 0.02
    anger: 0.015
    alertness: 0.005

  # Personality baseline (emotions return to these)
  baseline:
    stress: 0.2
    alertness: 0.4
    comfort: 0.5
```

### Memory Storage

```yaml
# Memory store structure
memory_store:
  # Recent memories (circular buffer)
  recent:
    capacity: 50
    entries:
      - id: "mem_001"
        type: "sighting"
        timestamp: "2024-01-08T14:30:00Z"
        entity_id: "chr_suspicious_guy"
        context: "spotted_known_threat"
        location: { x: 100, y: 0, z: 45 }
        emotional_weight: 0.6
        emotional_snapshot:
          stress: 0.4
          alertness: 0.7

  # Indexed by entity for quick lookup
  entity_memories:
    "chr_suspicious_guy":
      threat_flag: true
      last_seen: "2024-01-08T14:30:00Z"
      encounter_count: 3
      memories: ["mem_001", "mem_023", "mem_045"]

  # Indexed by type for queries
  type_index:
    threat: ["mem_001", "mem_015"]
    observation: ["mem_002", "mem_003"]
    crime: ["mem_015"]

  # Location-based spatial index
  spatial_index:
    # Chunked by region for efficient queries
    "stormwind_market": ["mem_001", "mem_002", "mem_003"]
```

### Goal Priority System

```yaml
# Goal manager structure
goal_manager:
  # Base priorities from character template
  base_priorities:
    maintain_order: 70
    complete_patrol: 50
    investigate_suspicious: 75
    respond_to_crime: 90
    protect_citizens: 95

  # Active priority boosts (temporary)
  boosts:
    - goal: "investigate_suspicious"
      amount: 20
      expires: "2024-01-08T14:35:00Z"
      reason: "suspicious_activity_detected"

  # Calculated active priorities
  active:
    - id: "protect_citizens"
      priority: 95
      conditions_met: true
    - id: "investigate_suspicious"
      priority: 95  # 75 + 20 boost
      conditions_met: false
    - id: "respond_to_crime"
      priority: 90
      conditions_met: false

  # Highest priority goal
  highest_priority:
    id: "protect_citizens"
    priority: 95
```

### Combat Preference Update

Combat preferences are influenced by:
1. **Personality traits** - Base aggression, caution levels
2. **Current emotional state** - Fear reduces aggression, anger increases it
3. **Memory of threats** - Past encounters influence approach
4. **Current health/resources** - Low health = more defensive

```yaml
# Combat preference calculation
combat_preferences:
  # Base from personality
  personality_base:
    aggression: 0.4
    caution: 0.6

  # Current modifiers
  modifiers:
    - source: "stress"
      effect: "aggression * 0.9"  # High stress = slightly less aggressive
    - source: "fear"
      effect: "aggression * 0.7"  # Fear significantly reduces aggression
    - source: "anger"
      effect: "aggression * 1.3"  # Anger increases aggression
    - source: "threat_memory"
      effect: "caution * 1.2"     # Known threats increase caution

  # Calculated values
  calculated:
    style: "balanced"    # aggressive | defensive | balanced
    aggression: 0.4      # Final aggression value
    preferred_range: "medium"
    tactics:
      - "watch_for_opening"
      - "maintain_distance"
```

---

## Event Actor Behaviors

Event actors orchestrate multi-character events using regional mapping data.

### Event Actor Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           EVENT ACTOR                                        │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  Inputs:                                                                     │
│  ┌──────────────────┐  ┌──────────────────┐  ┌──────────────────┐           │
│  │  Regional Events │  │  Mapping Data    │  │  Character Actors │          │
│  │  (subscribed)    │  │  (cached)        │  │  (queried)        │          │
│  │                  │  │                  │  │                   │          │
│  │ - time_tick      │  │ - characters[]   │  │ - get_behavior()  │          │
│  │ - player_entered │  │ - props[]        │  │ - get_state()     │          │
│  │ - state_changed  │  │ - locations[]    │  │ - get_goals()     │          │
│  └────────┬─────────┘  └────────┬─────────┘  └─────────┬─────────┘          │
│           │                     │                      │                    │
│           └─────────────────────┼──────────────────────┘                    │
│                                 │                                           │
│                                 ▼                                           │
│                    ┌────────────────────────┐                               │
│                    │   Event Composition    │                               │
│                    │                        │                               │
│                    │ - Select participants  │                               │
│                    │ - Choose props         │                               │
│                    │ - Design choreography  │                               │
│                    │ - Calculate timing     │                               │
│                    └───────────┬────────────┘                               │
│                                │                                            │
│                                ▼                                            │
│                    ┌────────────────────────┐                               │
│                    │   Event Execution      │                               │
│                    │                        │                               │
│                    │ - Send to participants │                               │
│                    │ - Coordinate timing    │                               │
│                    │ - Handle interrupts    │                               │
│                    └────────────────────────┘                               │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Quicktime Event Actor Example

```yaml
# =============================================================================
# Market Event Composer
# Event actor that creates dynamic quicktime events in market regions.
# Uses mapping data to find participants and props for event choreography.
# =============================================================================

version: "2.0"

metadata:
  id: market-event-composer
  type: event-actor-behavior
  description: "Composes and orchestrates quicktime events in market areas"
  tags:
    - event-actor
    - quicktime
    - market

# -----------------------------------------------------------------------------
# CONTEXT - Regional and cached data
# -----------------------------------------------------------------------------
context:
  variables:
    # Regional mapping data (cached from mapping channel)
    region_characters:
      source: "${mapping.region.characters}"
      type: array
      description: "All characters currently in this region"

    region_props:
      source: "${mapping.region.props}"
      type: array
      description: "Interactive props in this region"

    region_locations:
      source: "${mapping.region.points_of_interest}"
      type: array
      description: "Named locations (stalls, corners, etc.)"

    # Player data
    players_in_region:
      source: "${mapping.region.players}"
      type: array

    # Event state
    active_events:
      source: "${actor.state.active_events}"
      type: array
      default: []

    event_cooldowns:
      source: "${actor.state.cooldowns}"
      type: object
      default: {}

    # Configuration
    max_concurrent_events:
      source: "${actor.config.max_concurrent_events}"
      type: int
      default: 3

    event_check_interval:
      source: "${actor.config.event_check_interval}"
      type: float
      default: 10.0

# -----------------------------------------------------------------------------
# EVENT SUBSCRIPTIONS
# -----------------------------------------------------------------------------
events:
  - pattern: "region.time_tick"
    handler: periodic_event_check

  - pattern: "region.player_entered"
    handler: player_entered_region

  - pattern: "region.conflict_brewing"
    handler: potential_conflict_event

  - pattern: "region.transaction_completed"
    handler: post_transaction_event

  - pattern: "character.notable_action"
    handler: reactive_event_opportunity

# -----------------------------------------------------------------------------
# FLOWS
# -----------------------------------------------------------------------------
flows:
  # ===========================================================================
  # PERIODIC EVENT CHECK
  # ===========================================================================

  periodic_event_check:
    description: "Regular check for event opportunities"
    actions:
      # Don't spawn too many events
      - cond:
          - when: "${active_events.length >= max_concurrent_events}"
            then:
              - return: {}

      # Find eligible characters for events
      - set:
          variable: eligible_npcs
          value: "${region_characters.filter(c =>
            !c.is_player &&
            !c.in_combat &&
            !c.in_event &&
            c.idle_duration > 30
          )}"

      - cond:
          - when: "${eligible_npcs.length < 2}"
            then:
              - return: {}  # Need at least 2 NPCs for interesting events

      # Check what props are available
      - set:
          variable: available_props
          value: "${region_props.filter(p => !p.in_use)}"

      # Score potential event types
      - call: { flow: score_event_opportunities }

      # Select highest scoring event type
      - cond:
          - when: "${best_event_score > 0.5}"
            then:
              - call: { flow: compose_event, args: { event_type: "${best_event_type}" } }

  score_event_opportunities:
    description: "Score different event types based on current conditions"
    actions:
      - set:
          variable: event_scores
          value: {}

      # Score merchant interaction event
      - set:
          variable: merchants
          value: "${eligible_npcs.filter(c => c.role == 'merchant')}"
      - set:
          variable: potential_buyers
          value: "${eligible_npcs.filter(c => c.role != 'merchant' && c.needs_item)}"

      - cond:
          - when: "${merchants.length > 0 && potential_buyers.length > 0}"
            then:
              - set:
                  variable: event_scores.merchant_transaction
                  value: 0.7

      # Score casual conversation event
      - set:
          variable: acquainted_pairs
          value: "${find_acquainted_pairs(eligible_npcs)}"

      - cond:
          - when: "${acquainted_pairs.length > 0}"
            then:
              - set:
                  variable: event_scores.casual_conversation
                  value: 0.5

      # Score busker/performance event (needs performer + audience)
      - set:
          variable: performers
          value: "${eligible_npcs.filter(c => c.can_perform)}"

      - cond:
          - when: "${performers.length > 0 && eligible_npcs.length >= 3}"
            then:
              - set:
                  variable: event_scores.street_performance
                  value: 0.6

      # Score argument/conflict event (needs characters with tension)
      - set:
          variable: tense_pairs
          value: "${find_tense_pairs(eligible_npcs)}"

      - cond:
          - when: "${tense_pairs.length > 0}"
            then:
              - set:
                  variable: event_scores.brewing_argument
                  value: 0.65

      # Score environmental reaction (needs weather/time trigger)
      - cond:
          - when: "${world.weather.just_changed || world.time.just_changed_period}"
            then:
              - set:
                  variable: event_scores.environmental_reaction
                  value: 0.55

      # Find best scoring event
      - set:
          variable: best_event_type
          value: "${get_highest_key(event_scores)}"
      - set:
          variable: best_event_score
          value: "${event_scores[best_event_type] ?? 0}"

  # ===========================================================================
  # EVENT COMPOSITION
  # ===========================================================================

  compose_event:
    description: "Compose a specific event type with participants"
    params:
      event_type: string
    actions:
      - cond:
          - when: "${params.event_type == 'merchant_transaction'}"
            then:
              - call: { flow: compose_merchant_transaction }

          - when: "${params.event_type == 'casual_conversation'}"
            then:
              - call: { flow: compose_casual_conversation }

          - when: "${params.event_type == 'street_performance'}"
            then:
              - call: { flow: compose_street_performance }

          - when: "${params.event_type == 'brewing_argument'}"
            then:
              - call: { flow: compose_brewing_argument }

          - when: "${params.event_type == 'environmental_reaction'}"
            then:
              - call: { flow: compose_environmental_reaction }

  compose_merchant_transaction:
    description: "Compose a merchant transaction quicktime event"
    actions:
      # Select participants
      - set:
          variable: merchant
          value: "${pick(merchants)}"
      - set:
          variable: buyer
          value: "${pick(potential_buyers)}"

      # Query character actors for current state
      - call: { flow: query_character_state, args: { character_id: "${merchant.id}" } }
      - set:
          variable: merchant_state
          value: "${query_result}"

      - call: { flow: query_character_state, args: { character_id: "${buyer.id}" } }
      - set:
          variable: buyer_state
          value: "${query_result}"

      # Find relevant prop (merchant's stall/wares)
      - set:
          variable: merchant_stall
          value: "${available_props.filter(p => p.owner == merchant.id)[0]}"

      # Check if this is a good match
      - cond:
          - when: "${!is_compatible(merchant_state.mood, buyer_state.mood)}"
            then:
              # Moods don't work well together - skip or adjust
              - return: {}

      # Compose the event choreography
      - set:
          variable: event_choreography
          value:
            id: "${generate_event_id()}"
            type: "merchant_transaction"
            participants:
              - id: "${merchant.id}"
                role: "seller"
              - id: "${buyer.id}"
                role: "buyer"
            props:
              - id: "${merchant_stall.id}"
                role: "transaction_point"
            duration_estimate: 45  # seconds
            phases:
              - name: "approach"
                duration: 8
                actions:
                  buyer:
                    - { action: "walk_to", target: "${merchant_stall.position}" }
                    - { action: "look_at", target: "${merchant.id}" }
                  merchant:
                    - { action: "notice", target: "${buyer.id}" }
                    - { action: "greeting_gesture" }

              - name: "browsing"
                duration: 15
                actions:
                  buyer:
                    - { action: "examine_wares", target: "${merchant_stall.id}" }
                    - { action: "pick_up_item", item: "${select_item(merchant_stall)}" }
                  merchant:
                    - { action: "watch", target: "${buyer.id}" }
                    - { action: "describe_item", style: "${merchant_state.sales_style}" }

              - name: "negotiation"
                duration: 12
                dialogue:
                  template: "merchant_haggle"
                  buyer_personality: "${buyer_state.personality}"
                  merchant_personality: "${merchant_state.personality}"

              - name: "transaction"
                duration: 5
                actions:
                  buyer:
                    - { action: "pay", amount: "${negotiated_price}" }
                    - { action: "receive_item" }
                  merchant:
                    - { action: "accept_payment" }
                    - { action: "farewell_gesture" }

              - name: "departure"
                duration: 5
                actions:
                  buyer:
                    - { action: "express_satisfaction", style: "${buyer_state.mood}" }
                    - { action: "walk_away" }
                  merchant:
                    - { action: "return_to_idle" }

      # Register the event
      - call: { flow: register_and_dispatch_event, args: { event: "${event_choreography}" } }

  compose_brewing_argument:
    description: "Compose an argument event between tense characters"
    actions:
      # Select the most tense pair
      - set:
          variable: pair
          value: "${tense_pairs.sort((a,b) => b.tension - a.tension)[0]}"

      - set:
          variable: char_a
          value: "${pair.character_a}"
      - set:
          variable: char_b
          value: "${pair.character_b}"

      # Query both characters
      - call: { flow: query_character_state, args: { character_id: "${char_a.id}" } }
      - set:
          variable: state_a
          value: "${query_result}"

      - call: { flow: query_character_state, args: { character_id: "${char_b.id}" } }
      - set:
          variable: state_b
          value: "${query_result}"

      # Determine argument intensity based on personalities and emotions
      - set:
          variable: intensity
          value: "${calculate_argument_intensity(state_a, state_b, pair.tension)}"

      # Find nearby bystanders who might react
      - set:
          variable: bystanders
          value: "${eligible_npcs.filter(c =>
            c.id != char_a.id &&
            c.id != char_b.id &&
            distance(c.position, pair.location) < 15
          ).slice(0, 4)}"

      # Find a suitable location (open space preferred)
      - set:
          variable: event_location
          value: "${find_open_space_near(pair.location)}"

      # Compose choreography
      - set:
          variable: event_choreography
          value:
            id: "${generate_event_id()}"
            type: "brewing_argument"
            participants:
              - id: "${char_a.id}"
                role: "aggressor"
                emotional_state: "${state_a.emotional_state}"
              - id: "${char_b.id}"
                role: "defender"
                emotional_state: "${state_b.emotional_state}"
            bystanders: "${bystanders.map(b => b.id)}"
            location: "${event_location}"
            intensity: "${intensity}"
            duration_estimate: "${30 + intensity * 60}"  # 30-90 seconds

            phases:
              - name: "tension_build"
                duration: 10
                actions:
                  aggressor:
                    - { action: "approach_confrontationally", target: "${char_b.id}" }
                    - { action: "aggressive_stance" }
                  defender:
                    - { action: "notice_approach", target: "${char_a.id}" }
                    - { action: "defensive_stance" }
                  bystanders:
                    - { action: "notice", target: "${event_location}" }

              - name: "verbal_exchange"
                duration: "${15 + intensity * 20}"
                dialogue:
                  template: "heated_argument"
                  topic: "${pair.tension_source}"
                  intensity: "${intensity}"
                  personalities:
                    aggressor: "${state_a.personality}"
                    defender: "${state_b.personality}"
                bystanders:
                  - { action: "gather_watch", distance: 8 }
                  - { action: "whisper_to_neighbor", if: "${bystanders.length > 1}" }

              - name: "escalation_point"
                duration: 5
                # This is where player might intervene
                player_opportunity:
                  type: "intervention"
                  window: 3  # seconds
                  options:
                    - "calm_down"
                    - "take_side_a"
                    - "take_side_b"
                    - "ignore"
                actions:
                  aggressor:
                    - { action: "threatening_gesture", if: "${intensity > 0.6}" }
                  defender:
                    - { action: "back_away", if: "${state_b.personality.conflict_avoidance > 0.5}" }
                    - { action: "stand_ground", else: true }

              - name: "resolution"
                duration: 10
                # Resolution depends on intensity and any player intervention
                branches:
                  peaceful:
                    condition: "${intensity < 0.4 || player_calmed}"
                    actions:
                      both:
                        - { action: "back_down" }
                        - { action: "grumble" }
                        - { action: "walk_away_separately" }

                  standoff:
                    condition: "${intensity >= 0.4 && intensity < 0.7}"
                    actions:
                      aggressor:
                        - { action: "final_warning" }
                        - { action: "turn_and_leave" }
                      defender:
                        - { action: "relieved_exhale" }

                  fight:
                    condition: "${intensity >= 0.7 && !player_intervened}"
                    actions:
                      # Transition to combat
                      both:
                        - { action: "initiate_combat" }
                      bystanders:
                        - { action: "scatter", if: "${!is_guard}" }
                        - { action: "intervene", if: "${is_guard}" }

            # Post-event effects
            aftermath:
              - type: "memory"
                targets: ["${char_a.id}", "${char_b.id}"]
                memory_type: "conflict"
                emotional_weight: "${intensity * 0.5}"

              - type: "relationship_change"
                between: ["${char_a.id}", "${char_b.id}"]
                delta: "${-0.2 * intensity}"

              - type: "bystander_memory"
                targets: "${bystanders}"
                memory_type: "witnessed_conflict"
                emotional_weight: 0.2

      - call: { flow: register_and_dispatch_event, args: { event: "${event_choreography}" } }

  compose_street_performance:
    description: "Compose a street performance event"
    actions:
      # Select performer
      - set:
          variable: performer
          value: "${pick(performers)}"

      # Query performer state
      - call: { flow: query_character_state, args: { character_id: "${performer.id}" } }
      - set:
          variable: performer_state
          value: "${query_result}"

      # Find performance prop (instrument, stage area, etc.)
      - set:
          variable: performance_spot
          value: "${region_locations.filter(l => l.type == 'performance_area')[0] ??
                    region_locations.filter(l => l.type == 'open_space')[0]}"

      - set:
          variable: instrument
          value: "${available_props.filter(p => p.type == 'instrument' && p.owner == performer.id)[0]}"

      # Select potential audience
      - set:
          variable: audience
          value: "${eligible_npcs.filter(c =>
            c.id != performer.id &&
            distance(c.position, performance_spot.position) < 30
          ).slice(0, 8)}"

      # Compose choreography
      - set:
          variable: event_choreography
          value:
            id: "${generate_event_id()}"
            type: "street_performance"
            participants:
              - id: "${performer.id}"
                role: "performer"
            audience: "${audience.map(a => ({ id: a.id, initial_interest: calculate_interest(a, performer_state) }))}"
            location: "${performance_spot.position}"
            prop: "${instrument?.id}"
            duration_estimate: 120  # 2 minutes

            phases:
              - name: "setup"
                duration: 10
                actions:
                  performer:
                    - { action: "walk_to", target: "${performance_spot.position}" }
                    - { action: "prepare_performance" }
                    - { action: "ready_instrument", if: "${instrument != null}" }

              - name: "attract_attention"
                duration: 8
                actions:
                  performer:
                    - { action: "attention_call" }
                    - { action: "begin_warmup" }
                  audience:
                    # Each audience member may or may not engage
                    - { action: "notice", probability: 0.7 }
                    - { action: "turn_to_watch", if: "noticed && interest > 0.3" }

              - name: "performance"
                duration: 80
                actions:
                  performer:
                    - { action: "perform", skill: "${performer_state.performance_skill}" }
                    - { action: "engage_audience", style: "${performer_state.personality.charisma}" }
                  audience:
                    # Dynamic audience behavior
                    - { action: "gather_closer", if: "interest > 0.5" }
                    - { action: "clap_along", if: "interest > 0.7" }
                    - { action: "lose_interest_leave", if: "interest < 0.2" }
                    - { action: "toss_coin", if: "interest > 0.6 && has_money" }

                # Player opportunity
                player_opportunity:
                  type: "participation"
                  options:
                    - "watch"
                    - "tip_performer"
                    - "request_song"
                    - "heckle"

              - name: "finale"
                duration: 15
                actions:
                  performer:
                    - { action: "build_to_finale" }
                    - { action: "dramatic_finish" }
                    - { action: "bow" }
                  audience:
                    - { action: "applaud", enthusiasm: "based_on_interest" }

              - name: "wind_down"
                duration: 7
                actions:
                  performer:
                    - { action: "collect_tips" }
                    - { action: "thank_audience" }
                  audience:
                    - { action: "disperse_gradually" }

            aftermath:
              - type: "memory"
                targets: "${audience.filter(a => a.watched).map(a => a.id)}"
                memory_type: "entertainment"
                emotional_weight: 0.3
                context: "watched_performance"

              - type: "mood_boost"
                targets: "${audience.filter(a => a.interest > 0.5).map(a => a.id)}"
                emotion: "joy"
                delta: 0.1

              - type: "performer_income"
                target: "${performer.id}"
                amount: "${calculate_tips(audience)}"

      - call: { flow: register_and_dispatch_event, args: { event: "${event_choreography}" } }

  # ===========================================================================
  # HELPER FLOWS
  # ===========================================================================

  query_character_state:
    description: "Query a character actor for current state"
    params:
      character_id: string
    actions:
      # Send query to character actor
      - emit_intent:
          channel: actor_query
          action: get_character_state
          target_actor: "${params.character_id}"
          fields:
            - emotional_state
            - personality
            - current_goals
            - current_behavior
            - relationships
            - inventory_summary
          urgency: 0.8

      # Wait for response
      - wait_for:
          event: "actor_query.response"
          match: "event.request_id == last_request_id"
          timeout: 2.0

      - set:
          variable: query_result
          value: "${event.data}"

  register_and_dispatch_event:
    description: "Register event and send to participants"
    params:
      event: object
    actions:
      # Add to active events
      - set:
          variable: active_events
          value: "${[...active_events, params.event]}"

      # Set cooldown for this event type
      - set:
          variable: event_cooldowns["${params.event.type}"]
          value: "${world.time.now + 60}"  # 1 minute cooldown

      # Dispatch to all participants
      - foreach:
          items: "${params.event.participants}"
          as: participant
          do:
            - emit_intent:
                channel: event_dispatch
                action: join_event
                target_actor: "${participant.id}"
                event_id: "${params.event.id}"
                role: "${participant.role}"
                choreography: "${params.event.phases}"
                urgency: 0.85

      # Notify bystanders if applicable
      - cond:
          - when: "${params.event.bystanders?.length > 0}"
            then:
              - foreach:
                  items: "${params.event.bystanders}"
                  as: bystander
                  do:
                    - emit_intent:
                        channel: event_dispatch
                        action: notice_event
                        target_actor: "${bystander}"
                        event_id: "${params.event.id}"
                        event_type: "${params.event.type}"
                        location: "${params.event.location}"
                        urgency: 0.4

      # Start event timer
      - emit_intent:
          channel: timer
          action: set
          timer_id: "event_${params.event.id}"
          duration: "${params.event.duration_estimate}"
          on_complete: "handle_event_completion"
          urgency: 0.7

  handle_event_completion:
    description: "Handle when an event completes or times out"
    actions:
      - set:
          variable: completed_event
          value: "${active_events.find(e => e.id == event.event_id)}"

      # Remove from active events
      - set:
          variable: active_events
          value: "${active_events.filter(e => e.id != event.event_id)}"

      # Apply aftermath effects
      - cond:
          - when: "${completed_event?.aftermath}"
            then:
              - foreach:
                  items: "${completed_event.aftermath}"
                  as: effect
                  do:
                    - call: { flow: apply_aftermath_effect, args: { effect: "${effect}" } }

  apply_aftermath_effect:
    description: "Apply post-event effects to characters"
    params:
      effect: object
    actions:
      - cond:
          - when: "${params.effect.type == 'memory'}"
            then:
              - foreach:
                  items: "${params.effect.targets}"
                  as: target_id
                  do:
                    - emit_intent:
                        channel: event_dispatch
                        action: store_memory
                        target_actor: "${target_id}"
                        memory:
                          type: "${params.effect.memory_type}"
                          context: "${params.effect.context}"
                          emotional_weight: "${params.effect.emotional_weight}"
                        urgency: 0.6

          - when: "${params.effect.type == 'mood_boost'}"
            then:
              - foreach:
                  items: "${params.effect.targets}"
                  as: target_id
                  do:
                    - emit_intent:
                        channel: event_dispatch
                        action: adjust_emotion
                        target_actor: "${target_id}"
                        emotion: "${params.effect.emotion}"
                        delta: "${params.effect.delta}"
                        urgency: 0.5

          - when: "${params.effect.type == 'relationship_change'}"
            then:
              - emit_intent:
                  channel: relationship
                  action: modify
                  between: "${params.effect.between}"
                  delta: "${params.effect.delta}"
                  urgency: 0.6

  # ===========================================================================
  # REACTIVE EVENT HANDLERS
  # ===========================================================================

  player_entered_region:
    description: "React to player entering the region"
    actions:
      # Increase event probability when players are present
      - set:
          variable: player_present_boost
          value: 1.3

      # Maybe trigger a welcome event
      - cond:
          - when: "${random() < 0.2 && eligible_npcs.length > 0}"
            then:
              # Find an NPC who might notice the player
              - set:
                  variable: greeter
                  value: "${eligible_npcs.filter(c =>
                    c.personality.friendliness > 0.5 &&
                    distance(c.position, event.player.position) < 20
                  )[0]}"

              - cond:
                  - when: "${greeter != null}"
                    then:
                      # Nudge greeter to acknowledge player
                      - emit_intent:
                          channel: event_dispatch
                          action: subtle_notice
                          target_actor: "${greeter.id}"
                          notice_target: "${event.player.id}"
                          response_type: "friendly_acknowledgment"
                          urgency: 0.3

  potential_conflict_event:
    description: "Handle brewing conflict detected in region"
    actions:
      # This is high priority - conflicts are interesting
      - cond:
          - when: "${active_events.filter(e => e.type == 'brewing_argument').length == 0}"
            then:
              # No current argument events - this is an opportunity
              - set:
                  variable: tense_pairs
                  value: "[{ character_a: event.character_a, character_b: event.character_b, tension: event.tension_level, tension_source: event.reason, location: event.location }]"

              - call: { flow: compose_brewing_argument }

  reactive_event_opportunity:
    description: "React to notable NPC action that could spawn events"
    actions:
      # Someone did something interesting - can we make an event from it?
      - set:
          variable: actor
          value: "${event.actor}"
      - set:
          variable: action
          value: "${event.action}"

      - cond:
          # Merchant made a big sale - celebration opportunity
          - when: "${action.type == 'big_sale' && random() < 0.3}"
            then:
              # Find nearby NPCs to react
              - set:
                  variable: witnesses
                  value: "${region_characters.filter(c =>
                    distance(c.position, actor.position) < 10
                  )}"

              # Simple reactive moment
              - foreach:
                  items: "${witnesses.slice(0, 3)}"
                  as: witness
                  do:
                    - emit_intent:
                        channel: event_dispatch
                        action: react_to_moment
                        target_actor: "${witness.id}"
                        reaction: "impressed_glance"
                        target: "${actor.id}"
                        urgency: 0.3

          # Guard caught a thief - crowd gathering opportunity
          - when: "${action.type == 'apprehension' && random() < 0.5}"
            then:
              - call: { flow: compose_crowd_gathering, args: {
                  center: "${actor.position}",
                  reason: "guard_activity"
                }}
```

---

## Behavior Compilation

### Compilation Process

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        ABML COMPILATION PIPELINE                             │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  1. PARSE                                                                    │
│     ABML YAML ──► AST (Abstract Syntax Tree)                                │
│     - Validate schema structure                                              │
│     - Resolve imports                                                        │
│     - Build flow graph                                                       │
│                                                                              │
│  2. ANALYZE                                                                  │
│     AST ──► Annotated AST                                                   │
│     - Type inference                                                         │
│     - Variable scope analysis                                                │
│     - Dead code detection                                                    │
│     - GOAP goal/action validation                                            │
│                                                                              │
│  3. OPTIMIZE                                                                 │
│     Annotated AST ──► Optimized AST                                         │
│     - Constant folding                                                       │
│     - Common subexpression elimination                                       │
│     - Inline small flows                                                     │
│                                                                              │
│  4. GENERATE                                                                 │
│     Optimized AST ──► Bytecode                                              │
│     - Expression compilation to register VM                                  │
│     - Flow compilation to state machine                                      │
│     - Intent serialization                                                   │
│                                                                              │
│  5. PACKAGE                                                                  │
│     Bytecode ──► Behavior Package                                           │
│     - Metadata embedding                                                     │
│     - Constant pool                                                          │
│     - Debug symbols (optional)                                               │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Bytecode Format

```yaml
# Compiled behavior package structure
behavior_package:
  header:
    magic: "ABML"
    version: 2
    flags: 0x01  # Debug symbols included

  metadata:
    id: "guard-city"
    source_hash: "sha256:abc123..."
    compiled_at: "2024-01-08T10:00:00Z"

  constant_pool:
    strings:
      0: "idle"
      1: "patrol_duty"
      2: "Halt! City Guard!"
    floats:
      0: 0.5
      1: 0.85
    objects:
      # Serialized static objects

  expression_vm:
    registers: 32
    code:
      # Register-based bytecode for expressions
      # Example: ${stress_level > 0.6}
      # LOAD_VAR r0, "stress_level"
      # LOAD_CONST r1, #float:0
      # CMP_GT r2, r0, r1

  flow_machine:
    states:
      0: { name: "ambient_idle", ... }
      1: { name: "patrol_duty", ... }
    transitions:
      - from: 0, to: 1, condition: [...bytecode...]

  intent_templates:
    # Pre-serialized intent structures
    0: { channel: "stance", action: "alert", ... }

  event_handlers:
    "perception.loud_noise": { flow: 5, ... }

  goap_annotations:
    # GOAP preconditions/effects for planning
```

### Runtime Execution

The game server executes compiled behaviors:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        BEHAVIOR RUNTIME EXECUTION                            │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  TICK LOOP (per frame or fixed timestep):                                   │
│                                                                              │
│  1. CHECK TRIGGERS                                                           │
│     - Evaluate trigger conditions for all flows                             │
│     - Add newly triggered flows to active set                               │
│                                                                              │
│  2. PROCESS EVENTS                                                           │
│     - Match queued events against handlers                                  │
│     - Execute matching handler flows                                        │
│                                                                              │
│  3. EXECUTE ACTIVE FLOWS                                                     │
│     - Advance each flow's state machine                                     │
│     - Execute current action node                                           │
│     - Collect emitted intents                                               │
│                                                                              │
│  4. RESOLVE INTENTS                                                          │
│     - Group intents by channel                                              │
│     - Select highest urgency per channel                                    │
│     - Apply to character animation/movement/speech systems                  │
│                                                                              │
│  5. UPDATE STATE                                                             │
│     - Apply variable changes                                                │
│     - Update timers                                                          │
│     - Sync with character actor (if connected)                              │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Implementation Examples

### Example: Creating a New NPC Type

To create a new NPC type (e.g., "Innkeeper"):

**Step 1: Create Character Template**

```yaml
# examples/characters/innkeeper.yml
version: "2.0"

metadata:
  id: innkeeper-template
  type: character
  description: "Template for innkeeper NPCs"

character:
  role: innkeeper

  personality:
    friendliness: 0.8
    patience: 0.7
    greed: 0.4
    gossip_tendency: 0.6

  base_goals:
    - id: serve_customers
      priority: 70
    - id: maintain_inn
      priority: 60
    - id: gather_gossip
      priority: 40

  combat_preferences:
    style: defensive
    aggression: 0.2
    flee_threshold: 0.4
```

**Step 2: Create Role Behavior**

```yaml
# examples/behaviors/innkeeper-service.abml.yml
version: "2.0"

metadata:
  id: innkeeper-service
  type: behavior

imports:
  - path: "shared/humanoid-base"
    as: base

context:
  variables:
    customers_waiting:
      source: "${agent.perception.filter(c => c.wants_service)}"
      type: array
    inn_state:
      source: "${agent.workplace.state}"
      type: object
    # ... more context

goals:
  serve_all_customers:
    priority: 75
    conditions:
      customers_waiting: "== 0"

  # ... more goals

flows:
  tend_bar:
    description: "Main bar-tending loop"
    triggers:
      - condition: "${customers_waiting.length > 0}"
    actions:
      # ... implementation

  # ... more flows
```

**Step 3: Register and Compile**

```csharp
// In character actor activation
var behaviorStack = new[]
{
    "shared/humanoid-base",
    "innkeeper-service",
    "shared/combat-stance"  // Situational
};

var compiledBehavior = await _behaviorService.CompileAsync(
    characterId,
    behaviorStack,
    characterContext);

// Send to game server
await _gameClient.SetCharacterBehaviorAsync(characterId, compiledBehavior);
```

---

## Next Steps

1. **Implement Character Actor cognition storage** - Emotional state, memory store, goal manager
2. **Implement Event Actor regional subscriptions** - Mapping channel integration
3. **Build behavior compiler** - ABML → bytecode pipeline
4. **Create more shared behaviors** - conversation-mode, fleeing, resting
5. **Develop event templates** - Reusable event choreography patterns

---

## Related Documentation

- [ABML Guide](../guides/ABML.md) - Complete language specification
- [GOAP Guide](../guides/GOAP.md) - Planning system integration
- [Actor System Guide](../guides/ACTOR_SYSTEM.md) - Actor architecture
- [THE_DREAM](THE_DREAM.md) - Long-term vision
- [Gap Analysis](THE_DREAM_GAP_ANALYSIS.md) - Current implementation status
