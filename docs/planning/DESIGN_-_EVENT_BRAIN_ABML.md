# Event Brain Actor ABML Behavior Schema Design

> **Status**: DESIGN DOCUMENT
> **Created**: 2026-01-09
> **Related**: [ACTOR_BEHAVIORS.md](ACTOR_BEHAVIORS.md), [DESIGN_-_QUERY_OPTIONS_API.md](DESIGN_-_QUERY_OPTIONS_API.md), [THE_DREAM.md](THE_DREAM.md)

This document defines the ABML behavior schema for Event Brain actors - the "fight coordinators" that orchestrate multi-character combat encounters.

---

## 1. Overview

### 1.1 What is an Event Brain?

An Event Brain is an **actor that orchestrates multi-character events**. Unlike character agents that control individual NPCs, Event Brains:

- Have no physical presence in the game world
- Subscribe to regional events to detect "interesting" situations
- Query character actors for their available options
- Compose choreographed sequences that create dramatic moments
- Send choreography instructions to participants

### 1.2 Key Insight: Event Brain is Just an Actor

Event Brain runs on the **same ActorRunner infrastructure** as character agents. The only differences are:
- No `CharacterId` (not a character-based actor)
- Subscribes to region-level events instead of character-level perceptions
- Emits choreography instructions instead of movement/action intents

### 1.3 Fight Coordinator as Primary Use Case

The primary Event Brain application is **fight coordination**:
- Detects when combat becomes "interesting" (multiple participants, dramatic tension)
- Queries each combatant for their combat options
- Considers backstory, personality, and dramatic goals
- Composes a choreographed sequence that serves the narrative

---

## 2. ABML Behavior Schema

### 2.1 Metadata Block

```yaml
version: "2.0"

metadata:
  id: "fight-coordinator-regional"
  type: "event_brain"           # New metadata type for Event Brain behaviors
  description: "Orchestrates combat encounters in a region"
  tags:
    - event-brain
    - combat
    - coordinator

# Event Brain configuration
config:
  # What makes an encounter interesting enough to coordinate?
  interestingness_threshold: 0.6

  # How often to evaluate encounters (seconds)
  evaluation_interval: 2.0

  # Maximum participants to coordinate at once
  max_participants: 8

  # Choreography style preferences
  dramatic_style: "cinematic"  # cinematic, realistic, chaotic
```

### 2.2 Context Block - Regional Data Access

Event Brains access regional mapping data and encounter state:

```yaml
context:
  variables:
    # ==========================================================================
    # REGIONAL DATA (from lib-mapping subscriptions)
    # ==========================================================================

    # All characters in the region
    region_characters:
      source: "${mapping.region.characters}"
      type: array
      description: "Characters currently in this region"

    # Characters in active combat
    combatants:
      source: "${mapping.region.characters.filter(c => c.in_combat)}"
      type: array

    # Props that could be used in combat
    combat_props:
      source: "${mapping.region.props.filter(p => p.tags.includes('weapon') || p.tags.includes('destructible'))}"
      type: array

    # Environmental features
    environment:
      source: "${mapping.region.environment}"
      type: object
      description: "Environmental data (lighting, weather, terrain features)"

    # ==========================================================================
    # ENCOUNTER STATE (maintained by Event Brain)
    # ==========================================================================

    # Active coordinated encounters
    active_encounters:
      source: "${state.memories.active_encounters ?? []}"
      type: array

    # Recent choreography outcomes (for learning)
    recent_outcomes:
      source: "${state.memories.choreography_outcomes ?? []}"
      type: array

    # Cooldowns per character (avoid over-choreographing same characters)
    character_cooldowns:
      source: "${state.memories.character_cooldowns ?? {}}"
      type: object

    # ==========================================================================
    # CACHED PARTICIPANT DATA
    # ==========================================================================

    # Cached options from last query (per participant)
    participant_options:
      source: "${state.memories.participant_options ?? {}}"
      type: object

    # Cached backstory relevant to current encounter
    participant_backstory:
      source: "${state.memories.participant_backstory ?? {}}"
      type: object
```

### 2.3 Event Subscriptions

Event Brains subscribe to region-level events:

```yaml
events:
  # Combat-related events
  - pattern: "combat.started"
    handler: evaluate_new_combat

  - pattern: "combat.participant_joined"
    handler: evaluate_expanding_combat

  - pattern: "combat.dramatic_moment"
    handler: choreograph_climax

  - pattern: "combat.nearly_fatal"
    handler: consider_death_choreography

  # Regional tick for periodic evaluation
  - pattern: "region.tick"
    handler: periodic_evaluation

  # Player involvement changes priority
  - pattern: "region.player_entered_combat"
    handler: elevate_encounter_priority

  # Choreography completion
  - pattern: "choreography.completed"
    handler: record_outcome

  - pattern: "choreography.interrupted"
    handler: handle_interruption
```

### 2.4 Core Flows

#### 2.4.1 Combat Evaluation Flow

```yaml
flows:
  # ===========================================================================
  # ENTRY POINT: Evaluate new combat for coordination
  # ===========================================================================

  evaluate_new_combat:
    description: "Decide whether to coordinate a new combat encounter"
    actions:
      # Extract event data
      - set:
          variable: encounter_id
          value: "${event.encounter_id}"
      - set:
          variable: initial_combatants
          value: "${event.combatants}"

      # Skip if already coordinating this encounter
      - cond:
          - when: "${active_encounters.any(e => e.id == encounter_id)}"
            then:
              - return: {}

      # Skip if too few participants
      - cond:
          - when: "${initial_combatants.length < 2}"
            then:
              - return: {}

      # Calculate interestingness score
      - call: { flow: calculate_interestingness, args: { combatants: "${initial_combatants}" } }

      # Decide whether to coordinate
      - cond:
          - when: "${interestingness_score >= config.interestingness_threshold}"
            then:
              - call: { flow: begin_coordination, args: { encounter_id: "${encounter_id}", combatants: "${initial_combatants}" } }

  # ===========================================================================
  # INTERESTINGNESS CALCULATION
  # ===========================================================================

  calculate_interestingness:
    description: "Score how interesting/dramatic this combat is"
    params:
      combatants: array
    actions:
      - set:
          variable: score
          value: 0.0

      # More participants = more interesting (diminishing returns)
      - set:
          variable: participant_score
          value: "${min(params.combatants.length / config.max_participants, 1.0) * 0.3}"

      # Player involvement = much more interesting
      - set:
          variable: player_involved
          value: "${params.combatants.any(c => c.is_player)}"
      - set:
          variable: player_score
          value: "${player_involved ? 0.4 : 0.0}"

      # Named NPCs = more interesting than generic
      - set:
          variable: named_count
          value: "${params.combatants.filter(c => c.is_named).length}"
      - set:
          variable: named_score
          value: "${min(named_count / 4, 1.0) * 0.15}"

      # Backstory conflicts = interesting
      - call: { flow: find_backstory_conflicts, args: { combatants: "${params.combatants}" } }
      - set:
          variable: conflict_score
          value: "${backstory_conflicts.length > 0 ? 0.15 : 0.0}"

      # Sum up
      - set:
          variable: interestingness_score
          value: "${participant_score + player_score + named_score + conflict_score}"
```

#### 2.4.2 Coordination Flow

```yaml
  # ===========================================================================
  # BEGIN COORDINATION
  # ===========================================================================

  begin_coordination:
    description: "Start coordinating an encounter"
    params:
      encounter_id: string
      combatants: array
    actions:
      # Register encounter
      - set:
          variable: encounter
          value:
            id: "${params.encounter_id}"
            participants: "${params.combatants.map(c => c.id)}"
            started_at: "${now()}"
            phase: "gathering"
            dramatic_arc: "rising"

      - state_update:
          path: "memories.active_encounters"
          operation: "append"
          value: "${encounter}"

      # Query options from all participants
      - call: { flow: gather_participant_options, args: { participant_ids: "${encounter.participants}" } }

      # Query backstory for dramatic consideration
      - call: { flow: gather_participant_backstory, args: { participant_ids: "${encounter.participants}" } }

      # Begin choreography planning
      - call: { flow: plan_choreography, args: { encounter_id: "${params.encounter_id}" } }

  # ===========================================================================
  # GATHER PARTICIPANT OPTIONS
  # ===========================================================================

  gather_participant_options:
    description: "Query each participant for their combat options"
    params:
      participant_ids: array
    actions:
      - foreach:
          items: "${params.participant_ids}"
          as: "participant_id"
          do:
            # Query actor for combat options
            - query_options:
                actor_id: "${participant_id}"
                query_type: "combat"
                freshness: "cached"
                max_age_ms: 3000
                context:
                  combat_state: "engaged"
                  opponent_ids: "${params.participant_ids.filter(id => id != participant_id)}"
                  environment_tags: "${environment.tags}"
                result_variable: "options_result"

            # Store in participant options cache
            - state_update:
                path: "memories.participant_options.${participant_id}"
                value: "${options_result}"

  # ===========================================================================
  # GATHER BACKSTORY FOR DRAMATIC CONSIDERATION
  # ===========================================================================

  gather_participant_backstory:
    description: "Get relevant backstory for each participant"
    params:
      participant_ids: array
    actions:
      - foreach:
          items: "${params.participant_ids}"
          as: "participant_id"
          do:
            # Query actor state for backstory (if character-based)
            - query_actor_state:
                actor_id: "${participant_id}"
                paths:
                  - "backstory"
                  - "personality"
                  - "combat_preferences"
                result_variable: "actor_data"

            # Store relevant backstory
            - cond:
                - when: "${actor_data.backstory != null}"
                  then:
                    - state_update:
                        path: "memories.participant_backstory.${participant_id}"
                        value:
                          fears: "${actor_data.backstory.fear}"
                          trauma: "${actor_data.backstory.trauma}"
                          training: "${actor_data.backstory.training}"
                          goals: "${actor_data.backstory.goal}"
                          personality: "${actor_data.personality}"
                          combat_style: "${actor_data.combat_preferences.style}"
```

#### 2.4.3 Choreography Planning Flow

```yaml
  # ===========================================================================
  # CHOREOGRAPHY PLANNING
  # ===========================================================================

  plan_choreography:
    description: "Plan a choreographed combat sequence"
    params:
      encounter_id: string
    actions:
      - set:
          variable: encounter
          value: "${active_encounters.find(e => e.id == params.encounter_id)}"

      # Get all participant data
      - set:
          variable: all_options
          value: "${encounter.participants.map(id => ({ id: id, options: participant_options[id] }))}"
      - set:
          variable: all_backstory
          value: "${encounter.participants.map(id => ({ id: id, backstory: participant_backstory[id] }))}"

      # Find dramatic opportunities
      - call: { flow: find_dramatic_opportunities }

      # Select choreography style based on dramatic arc
      - call: { flow: select_choreography_style }

      # Build choreography plan
      - call: { flow: build_choreography_plan }

      # Emit choreography to participants
      - call: { flow: emit_choreography }

  # ===========================================================================
  # FIND DRAMATIC OPPORTUNITIES
  # ===========================================================================

  find_dramatic_opportunities:
    description: "Identify dramatically interesting option combinations"
    actions:
      - set:
          variable: opportunities
          value: []

      # Look for backstory-driven opportunities
      - foreach:
          items: "${all_backstory}"
          as: "participant"
          do:
            # Fear-based opportunities
            - cond:
                - when: "${participant.backstory.fears != null}"
                  then:
                    - set:
                        variable: fear_opportunity
                        value:
                          type: "fear_trigger"
                          participant: "${participant.id}"
                          fear: "${participant.backstory.fears}"
                          dramatic_value: 0.8
                    - append:
                        to: "opportunities"
                        value: "${fear_opportunity}"

            # Trauma-based opportunities
            - cond:
                - when: "${participant.backstory.trauma != null}"
                  then:
                    - set:
                        variable: trauma_opportunity
                        value:
                          type: "trauma_echo"
                          participant: "${participant.id}"
                          trauma: "${participant.backstory.trauma}"
                          dramatic_value: 0.9
                    - append:
                        to: "opportunities"
                        value: "${trauma_opportunity}"

      # Look for complementary options (combo potential)
      - call: { flow: find_combo_opportunities }

      # Look for environmental interactions
      - call: { flow: find_environmental_opportunities }

  find_combo_opportunities:
    description: "Find options that could combine dramatically"
    actions:
      - foreach:
          items: "${all_options}"
          as: "attacker"
          do:
            - foreach:
                items: "${all_options.filter(o => o.id != attacker.id)}"
                as: "defender"
                do:
                  # Look for attack/counter combinations
                  - set:
                      variable: attacker_aggressive
                      value: "${attacker.options.options.filter(o => o.tags && o.tags.includes('offensive') && o.available)}"
                  - set:
                      variable: defender_defensive
                      value: "${defender.options.options.filter(o => o.tags && o.tags.includes('defensive') && o.available)}"

                  - cond:
                      - when: "${attacker_aggressive.length > 0 && defender_defensive.length > 0}"
                        then:
                          - set:
                              variable: combo
                              value:
                                type: "attack_counter"
                                attacker: "${attacker.id}"
                                attack: "${attacker_aggressive[0]}"
                                defender: "${defender.id}"
                                counter: "${defender_defensive[0]}"
                                dramatic_value: 0.7
                          - append:
                              to: "opportunities"
                              value: "${combo}"

  find_environmental_opportunities:
    description: "Find ways to use environment dramatically"
    actions:
      - foreach:
          items: "${combat_props}"
          as: "prop"
          do:
            # Find participants near this prop
            - set:
                variable: nearby_participants
                value: "${encounter.participants.filter(id => distance(mapping.region.characters.find(c => c.id == id).position, prop.position) < 5.0)}"

            - cond:
                - when: "${nearby_participants.length > 0 && prop.tags.includes('destructible')}"
                  then:
                    - set:
                        variable: env_opportunity
                        value:
                          type: "environmental_destruction"
                          prop: "${prop.id}"
                          nearby: "${nearby_participants}"
                          dramatic_value: 0.75
                    - append:
                        to: "opportunities"
                        value: "${env_opportunity}"
```

#### 2.4.4 Choreography Emission

```yaml
  # ===========================================================================
  # EMIT CHOREOGRAPHY TO PARTICIPANTS
  # ===========================================================================

  emit_choreography:
    description: "Send choreography instructions to participants"
    actions:
      - foreach:
          items: "${choreography_plan.sequences}"
          as: "sequence"
          do:
            # Send choreography perception to participant
            - emit_perception:
                target_actor: "${sequence.participant_id}"
                perception_type: "choreography_instruction"
                data:
                  encounter_id: "${encounter.id}"
                  sequence_id: "${sequence.id}"
                  actions: "${sequence.actions}"
                  timing: "${sequence.timing}"
                  priority: "high"
                  can_interrupt: "${sequence.can_interrupt ?? false}"

      # Update encounter phase
      - state_update:
          path: "memories.active_encounters"
          operation: "update"
          filter: "e => e.id == encounter.id"
          value:
            phase: "executing"
            current_sequence: "${choreography_plan.sequences[0].id}"

      # Set up timeout for completion
      - schedule_event:
          delay_ms: "${choreography_plan.estimated_duration_ms}"
          event_type: "choreography.timeout"
          data:
            encounter_id: "${encounter.id}"
```

### 2.5 Backstory-Aware Choreography

The Event Brain can consider character backstory when planning:

```yaml
  # ===========================================================================
  # BACKSTORY-AWARE OPTION FILTERING
  # ===========================================================================

  filter_options_by_backstory:
    description: "Filter out options that conflict with character backstory"
    params:
      participant_id: string
      options: array
    actions:
      - set:
          variable: backstory
          value: "${participant_backstory[params.participant_id]}"
      - set:
          variable: filtered_options
          value: "${params.options}"

      # Exclude options that trigger fears (unless dramatically appropriate)
      - cond:
          - when: "${backstory.fears != null && backstory.fears.key == 'FIRE'}"
            then:
              # Character fears fire - exclude fire-based options from THEIR repertoire
              # But could include fire in OPPONENT's choreography as dramatic tension
              - set:
                  variable: filtered_options
                  value: "${filtered_options.filter(o => !o.tags || !o.tags.includes('fire'))}"

      # Prefer options that match training background
      - cond:
          - when: "${backstory.training != null}"
            then:
              - set:
                  variable: training_match
                  value: "${backstory.training.value.toLowerCase()}"
              - set:
                  variable: filtered_options
                  value: "${filtered_options.map(o => ({
                    ...o,
                    preference: o.tags && o.tags.some(t => t.toLowerCase().includes(training_match))
                      ? o.preference * 1.2
                      : o.preference
                  }))}"

      - return: { value: "${filtered_options}" }

  # ===========================================================================
  # DRAMATIC ARC INTEGRATION
  # ===========================================================================

  select_dramatic_opportunity:
    description: "Select best dramatic opportunity based on arc position"
    actions:
      - set:
          variable: arc_position
          value: "${encounter.dramatic_arc}"

      - cond:
          # Rising action - build tension
          - when: "${arc_position == 'rising'}"
            then:
              - set:
                  variable: preferred_types
                  value: ["attack_counter", "environmental_destruction"]
              - set:
                  variable: selected
                  value: "${opportunities.filter(o => preferred_types.includes(o.type)).sort((a,b) => b.dramatic_value - a.dramatic_value)[0]}"

          # Climax - maximum drama
          - when: "${arc_position == 'climax'}"
            then:
              - set:
                  variable: preferred_types
                  value: ["trauma_echo", "fear_trigger", "decisive_blow"]
              - set:
                  variable: selected
                  value: "${opportunities.filter(o => preferred_types.includes(o.type)).sort((a,b) => b.dramatic_value - a.dramatic_value)[0]}"

          # Falling action - resolution
          - when: "${arc_position == 'falling'}"
            then:
              - set:
                  variable: preferred_types
                  value: ["retreat", "surrender", "victory_pose"]
              - set:
                  variable: selected
                  value: "${opportunities.filter(o => preferred_types.includes(o.type))[0]}"
```

---

## 3. Built-in Actions

Event Brain behaviors use these built-in actions:

### 3.1 query_options

Query an actor for available options (wraps `/actor/query-options`):

```yaml
- query_options:
    actor_id: string           # Actor to query
    query_type: string         # combat, dialogue, social, etc.
    freshness: string          # fresh, cached, stale_ok
    max_age_ms: integer        # Maximum cache age
    context: object            # Context for fresh queries
    result_variable: string    # Where to store result
```

### 3.2 query_actor_state

Query specific state paths from an actor:

```yaml
- query_actor_state:
    actor_id: string           # Actor to query
    paths: array               # State paths to retrieve
    result_variable: string    # Where to store result
```

### 3.3 emit_perception

Send a perception event to a specific actor:

```yaml
- emit_perception:
    target_actor: string       # Actor to receive perception
    perception_type: string    # Type of perception
    data: object               # Perception data
```

### 3.4 schedule_event

Schedule a future event:

```yaml
- schedule_event:
    delay_ms: integer          # Delay before event fires
    event_type: string         # Event type to emit
    data: object               # Event data
```

### 3.5 state_update

Update Event Brain's own state:

```yaml
- state_update:
    path: string               # State path (dot notation)
    operation: string          # set, append, update, remove
    value: any                 # Value to apply
    filter: string             # Filter for update operation (optional)
```

---

## 4. Choreography Output Format

Event Brain emits choreography as perceptions to participants:

### 4.1 Choreography Instruction Perception

```yaml
ChoreographyInstruction:
  type: object
  description: Instruction sent to a participant to execute choreographed action
  properties:
    encounter_id:
      type: string
      description: ID of the coordinated encounter
    sequence_id:
      type: string
      description: ID of this sequence within the choreography
    actions:
      type: array
      description: Actions to perform in order
      items:
        $ref: '#/components/schemas/ChoreographyAction'
    timing:
      $ref: '#/components/schemas/ChoreographyTiming'
    priority:
      type: string
      enum: [low, normal, high, override]
      description: How strongly to prefer this choreography
    can_interrupt:
      type: boolean
      description: Whether participant can interrupt if better opportunity arises

ChoreographyAction:
  type: object
  properties:
    action_id:
      type: string
      description: Action to perform (from participant's options)
    target_id:
      type: string
      nullable: true
      description: Target of action (if applicable)
    position:
      type: object
      nullable: true
      description: Position to move to (if applicable)
    duration_ms:
      type: integer
      description: Expected duration
    style:
      type: string
      nullable: true
      description: Style modifier (dramatic, quick, hesitant, etc.)

ChoreographyTiming:
  type: object
  properties:
    start_at:
      type: string
      enum: [immediate, after_previous, sync_point]
    sync_point_id:
      type: string
      nullable: true
      description: Sync point to wait for (if start_at=sync_point)
    max_wait_ms:
      type: integer
      description: Maximum time to wait for sync/previous
```

### 4.2 Example Choreography Emission

```yaml
# Event Brain emits to Attacker:
emit_perception:
  target_actor: "actor_guard_001"
  perception_type: "choreography_instruction"
  data:
    encounter_id: "enc_market_fight_001"
    sequence_id: "seq_001"
    actions:
      - action_id: "sword_slash"
        target_id: "actor_thief_001"
        duration_ms: 1500
        style: "dramatic"
    timing:
      start_at: "immediate"
      max_wait_ms: 500
    priority: "high"
    can_interrupt: false

# Event Brain emits to Defender:
emit_perception:
  target_actor: "actor_thief_001"
  perception_type: "choreography_instruction"
  data:
    encounter_id: "enc_market_fight_001"
    sequence_id: "seq_002"
    actions:
      - action_id: "dodge_roll"
        position: { x: 102.5, y: 0.0, z: 47.8 }
        duration_ms: 800
        style: "desperate"
      - action_id: "counter_slash"
        target_id: "actor_guard_001"
        duration_ms: 1200
        style: "quick"
    timing:
      start_at: "sync_point"
      sync_point_id: "attack_lands"
      max_wait_ms: 2000
    priority: "high"
    can_interrupt: true
```

---

## 5. Integration with Existing Infrastructure

### 5.1 Using CutsceneCoordinator

For complex choreography with sync points:

```yaml
- begin_cutscene:
    cutscene_id: "${encounter.id}_choreography"
    participants: "${encounter.participants}"
    sync_points:
      - id: "attack_lands"
        participants: ["${attacker.id}"]
      - id: "counter_complete"
        participants: ["${defender.id}"]
    timeout_ms: 10000
```

### 5.2 Using InputWindowManager

For player-involving choreography with QTE windows:

```yaml
- open_input_window:
    player_id: "${player.id}"
    window_type: "combat_qte"
    options:
      - action_id: "perfect_parry"
        key_hint: "F"
        window_ms: 300
      - action_id: "dodge"
        key_hint: "SPACE"
        window_ms: 500
    on_success: "perfect_parry_sequence"
    on_timeout: "hit_received_sequence"
```

### 5.3 Using lib-mapping

Event Brain subscribes to mapping channels for regional data:

```yaml
# ActorRunner config for Event Brain
actor_config:
  id: "event_brain_region_001"
  behavior: "fight-coordinator-regional"
  subscriptions:
    - channel: "map.region_001.characters"
    - channel: "map.region_001.props"
    - channel: "combat.region_001.*"
    - channel: "region.region_001.tick"
```

---

## 6. Implementation Plan

### 6.1 Phase 1: Core Actions (2-3 days)

1. Implement `query_options` action handler
2. Implement `query_actor_state` action handler
3. Implement `emit_perception` action handler
4. Implement `schedule_event` action handler
5. Add Event Brain metadata type to ABML parser

### 6.2 Phase 2: Choreography Emission (1-2 days)

1. Define ChoreographyInstruction schema
2. Add choreography perception handler to character agents
3. Wire to CutsceneCoordinator for sync points
4. Add choreography acceptance/rejection flow

### 6.3 Phase 3: Example Behaviors (1-2 days)

1. Create `fight-coordinator-regional` example
2. Create `combat-event-simple` minimal example
3. Add http-tester tests for Event Brain flow
4. Document Event Brain authoring patterns

---

## 7. Design Decisions

### 7.1 Why Event Brain as Actor?

**Considered**: Dedicated EventBrainRunner class
**Decided**: Use standard ActorRunner

**Rationale**:
- Same execution model reduces complexity
- Event Brain can use same ABML syntax
- State persistence, hot reload, etc. work automatically
- Only difference is perception subscriptions

### 7.2 Why Choreography as Perception?

**Considered**: Direct RPC to character agents
**Decided**: Emit perception events

**Rationale**:
- Consistent with existing architecture
- Character agents can accept/reject/modify
- Supports async execution
- Works with existing sync point infrastructure

### 7.3 Why Backstory Consideration?

**Considered**: Pure option-based choreography
**Decided**: Include backstory in planning

**Rationale**:
- Creates emergent narrative moments
- Character fears/trauma create tension
- Training backgrounds affect combat style
- Personality influences action selection

---

*Document Status: DESIGN - Ready for implementation*
