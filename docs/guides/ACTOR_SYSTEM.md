# Actor System - Bringing Worlds to Life

> **Version**: 1.1
> **Status**: Implemented (Phases 0-5), Character Data Layer Complete
> **Location**: `plugins/lib-actor/`, `plugins/lib-behavior/`, `plugins/lib-character-personality/`, `plugins/lib-character-history/`
> **Related**: [ABML Guide](./ABML.md), [GOAP Guide](./GOAP.md), [Mapping System Guide](./MAPPING_SYSTEM.md)

The Actor System provides the cognitive layer for Arcadia's living world. Actors are long-running processes that give characters personality, make regions feel alive, and orchestrate dramatic moments. They are the invisible directors that turn static game worlds into dynamic, responsive experiences.

---

## Table of Contents

1. [Overview](#1-overview)
2. [The Key Insight: Flavor, Not Foundation](#2-the-key-insight-flavor-not-foundation)
3. [Actor Types](#3-actor-types)
4. [NPC Brain Actors](#4-npc-brain-actors)
5. [Character Personality and Backstory](#5-character-personality-and-backstory)
6. [Event Actors](#6-event-actors)
   - [6.6 Future Extension: ABML encounter_instruction Handler](#66-future-extension-abml-encounter_instruction-handler)
7. [Behavior Integration](#7-behavior-integration)
8. [Perception and Cognition](#8-perception-and-cognition)
9. [Cutscene and QTE Orchestration](#9-cutscene-and-qte-orchestration)
10. [Infrastructure Integration](#10-infrastructure-integration)
11. [State Management](#11-state-management)
12. [Scaling and Distribution](#12-scaling-and-distribution)
13. [API Reference](#13-api-reference)
14. [Game Server Integration](#14-game-server-integration-stride-side)
- [Appendix A: Actor Categories](#appendix-a-actor-categories)
- [Appendix B: Perception Event Types](#appendix-b-perception-event-types)

---

## 1. Overview

### 1.1 What Is An Actor?

An **Actor** is a long-running task that executes a behavior (ABML document) in a loop until:
- The behavior signals completion (self-terminate)
- The control plane stops it (external terminate)

Actors are **not** request-response entities. They are autonomous processes that:
- Run continuously on pool nodes
- Execute behaviors defined in ABML
- Subscribe to events and react over time
- Emit state updates that influence character behaviors

### 1.2 The Two Actor Paradigms

| Actor Type | Scope | Primary Function | Data Flow |
|------------|-------|------------------|-----------|
| **NPC Brain Actor** | Single character | Growth, personality, feelings | Consumes perceptions → Emits state updates |
| **Event Actor** | Region/situation | Orchestration, drama | Queries spatial data → Emits cinematics/events |

### 1.3 Architecture Overview

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
│  │             │       │             │           │             │           │
│  │ ┌─────────┐ │       │ ┌─────────┐ │           │ Runs:       │           │
│  │ │NPC Brain│ │       │ │ Event   │ │           │ - Physics   │           │
│  │ │ Actors  │ │       │ │ Actors  │ │           │ - Bytecode  │           │
│  │ └─────────┘ │       │ └─────────┘ │           │ - Cinematics│           │
│  └─────────────┘       └─────────────┘           └─────────────┘           │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 2. The Key Insight: Flavor, Not Foundation

### 2.1 Actors Are Strictly Optional

Characters have massive, self-sufficient **behavior stacks** that handle every situation:
- Opening doors, hunting prey, combat maneuvers
- Social interactions, daily routines, survival instincts
- All moment-to-moment decision-making

**Without ANY actor, a character is fully functional.** They just don't CHANGE or GROW.

### 2.2 What Actors Provide

| Actors Provide | Actors Do NOT Provide |
|----------------|----------------------|
| **Growth** - Characters learn and evolve | Core functionality (behavior stack handles this) |
| **Spontaneity** - Unexpected reactions | Moment-to-moment decisions (bytecode handles this) |
| **Personality** - Feelings, moods, memories | Required infrastructure (everything works without actors) |
| **Realism** - Characters that feel alive | Frame-by-frame combat choices |

### 2.3 The Dependency Graph

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

### 2.4 Actor Output Types

| Output Type | Frequency | Effect |
|-------------|-----------|--------|
| **State Update** | Every few ticks | Updates feelings/mood variables read by behavior stack |
| **Goal Update** | When goals change | Updates goal-related inputs for GOAP |
| **Memory Update** | On significant events | Stores/retrieves memories affecting behavior |
| **Behavior Change** | Rare (learning/growth) | Modifies the composed behavior stack |

The actor never controls the character directly - it influences how the already-running behavior stack behaves.

---

## 3. Actor Types

### 3.1 NPC Brain Actors

**Purpose**: Character growth, personality expression, memory integration

**Scope**: One actor per character that needs growth/personality

**Lifecycle**:
- Created when character needs cognitive processing
- Runs continuously while character is active
- Persists state across sessions

**Primary Outputs**:
- Feelings: "You're upset now", "You're scared"
- Goals: "You want RABBIT specifically, not just food"
- Memories: "Remember that person betrayed you"

### 3.2 Event Actors

**Purpose**: Orchestrate dramatic moments, regional events, cinematics

**Scope**: One actor per event/situation

**Lifecycle**:
- Spawned when "interestingness" threshold is crossed
- Lives for duration of the event
- Terminates when event concludes

**Primary Outputs**:
- Cinematics sent to game servers
- QTE option presentations
- Environmental effects coordination
- Multi-character scene orchestration

### 3.3 Administrative Actors

**Purpose**: Background tasks, world maintenance, scheduled jobs

**Examples**:
- Daily cleanup tasks
- Economic simulation
- Weather pattern generation
- NPC population management

---

## 4. NPC Brain Actors

### 4.1 The Character Co-Pilot Pattern

In Arcadia, players don't control characters directly - they **possess** them as "guardian spirits". The character has their own agent (NPC brain) that:
- Is always running, always perceiving, always computing
- Has intimate knowledge of capabilities, state, and preferences
- Makes decisions when the player doesn't (or can't respond fast enough)
- Maintains personality and behavioral patterns

When a player connects, they take **priority** over the agent's decisions, but the agent doesn't stop - it watches, computes, and waits.

### 4.2 Perception Flow

Perceptions flow **directly** from Game Server to Actor via event subscription:

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

**Key point**: The control plane does NOT route perceptions. Events flow directly via lib-messaging, scaling horizontally with pool nodes.

### 4.3 The Cognition Pipeline

Each tick, the NPC brain processes perceptions through a cognition pipeline:

```yaml
flows:
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
```

### 4.4 State Updates Flow to Behavior Stack

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

**Key insight**: The actor never emits IntentChannels directly. It emits STATE, which the behavior stack reads, which then emits IntentChannels.
- **Actor**: "Why" (feelings, goals, memories)
- **Behavior Stack**: "What" (which actions to take)
- **IntentChannels**: "How" (animation, movement execution)

### 4.5 Personality Through Failure

When players miss QTE windows, the character agent's pre-computed answer executes. This creates emergent personality expression:

| Character Type | QTE Timeout Behavior |
|----------------|---------------------|
| Aggressive | Attack anyway |
| Cautious | Defensive stance |
| Loyal | Protect ally first |
| Panicked | Random flailing |

The character's nature shows through even when (especially when) the player fails to respond.

---

## 5. Character Personality and Backstory

### 5.1 The Character Data Layer

Characters in Arcadia have persistent personality traits, combat preferences, and backstory that inform their behavior. This data is stored in dedicated services and made available to actors through ABML variable providers.

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    CHARACTER DATA LAYER                                      │
│                                                                              │
│  ┌─────────────────────────────────────────────────────────────────────────┐│
│  │                 lib-character-personality                               ││
│  │  ┌────────────────────────┐  ┌────────────────────────────────────────┐ ││
│  │  │ Personality Traits     │  │ Combat Preferences                      │ ││
│  │  │ - OPENNESS            │  │ - style (aggressive/defensive/tactical) │ ││
│  │  │ - CONSCIENTIOUSNESS   │  │ - preferredRange (close/mid/long)       │ ││
│  │  │ - EXTRAVERSION        │  │ - groupRole (leader/support/striker)    │ ││
│  │  │ - AGREEABLENESS       │  │ - riskTolerance (0.0-1.0)              │ ││
│  │  │ - NEUROTICISM         │  │ - retreatThreshold (0.0-1.0)           │ ││
│  │  │ - HONESTY             │  │ - protectAllies (boolean)               │ ││
│  │  │ - AGGRESSION          │  └────────────────────────────────────────┘ ││
│  │  │ - LOYALTY             │                                             ││
│  │  └────────────────────────┘  Experience Evolution (probabilistic)      ││
│  └─────────────────────────────────────────────────────────────────────────┘│
│                                                                              │
│  ┌─────────────────────────────────────────────────────────────────────────┐│
│  │                 lib-character-history                                   ││
│  │  ┌────────────────────────┐  ┌────────────────────────────────────────┐ ││
│  │  │ Backstory Elements     │  │ Event Participation                     │ ││
│  │  │ - ORIGIN              │  │ - Historical event tracking             │ ││
│  │  │ - OCCUPATION          │  │ - Roles: LEADER, COMBATANT, VICTIM,    │ ││
│  │  │ - TRAINING            │  │          WITNESS, BENEFICIARY, etc.     │ ││
│  │  │ - TRAUMA              │  │ - Dual-indexed for efficient queries    │ ││
│  │  │ - ACHIEVEMENT         │  └────────────────────────────────────────┘ ││
│  │  │ - SECRET              │                                             ││
│  │  │ - GOAL                │  History Summarization (for prompts)       ││
│  │  │ - FEAR                │                                             ││
│  │  │ - BELIEF              │                                             ││
│  │  └────────────────────────┘                                             ││
│  └─────────────────────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────────────────────┘
```

### 5.2 ABML Variable Providers

Character data is exposed to ABML behaviors through three variable providers:

| Provider | Namespace | Example Paths |
|----------|-----------|---------------|
| **PersonalityProvider** | `${personality.*}` | `${personality.openness}`, `${personality.traits.AGGRESSION}` |
| **CombatPreferencesProvider** | `${combat.*}` | `${combat.style}`, `${combat.riskTolerance}`, `${combat.protectAllies}` |
| **BackstoryProvider** | `${backstory.*}` | `${backstory.origin}`, `${backstory.fear.value}`, `${backstory.elements.TRAUMA}` |

### 5.3 Using Character Data in ABML

Character personality and backstory inform behavior decisions:

```yaml
flows:
  evaluate_combat_approach:
    # Consider personality traits
    - cond:
        if: "${personality.aggression > 0.7 && combat.riskTolerance > 0.6}"
        then:
          - set: combat_approach = "aggressive"
          - emit_intent:
              channel: stance
              stance: "aggressive"
              urgency: 0.9
        else:
          - set: combat_approach = "cautious"

    # Consider backstory when facing fire
    - cond:
        if: "${backstory.fear.key == 'FIRE' && environment.has_fire}"
        then:
          - modify_emotion:
              emotion: fear
              delta: 0.3
          - set: avoid_fire_tactics = true

    # Protect allies based on combat preferences
    - cond:
        if: "${combat.protectAllies && ally_in_danger}"
        then:
          - call: protect_ally_behavior
            priority: high
```

### 5.4 Personality Evolution

Characters evolve over time through experiences. The `RecordExperience` API triggers probabilistic trait changes:

```csharp
// After a traumatic combat experience
await _characterPersonalityClient.RecordExperienceAsync(new RecordExperienceRequest
{
    CharacterId = characterId,
    ExperienceType = ExperienceType.TRAUMA,
    Intensity = 0.8f  // Severe trauma
});

// Base 15% chance × intensity = 12% chance of trait modification
// If triggered: NEUROTICISM +0.05, AGREEABLENESS -0.03
```

**Experience types and their effects:**

| Experience | Potential Effects |
|------------|-------------------|
| `TRAUMA` | ↑ Neuroticism, ↓ Agreeableness |
| `BETRAYAL` | ↓ Honesty, ↓ Agreeableness |
| `VICTORY` | ↑ Confidence (custom trait) |
| `FRIENDSHIP` | ↑ Extraversion, ↑ Agreeableness |
| `NEAR_DEATH` | ↓ Risk tolerance, ↑ Retreat threshold |
| `ALLY_SAVED` | ↑ Protect allies tendency |

### 5.5 Combat Preference Evolution

Combat experiences also shape preferences:

```csharp
// After barely surviving a fight
await _characterPersonalityClient.RecordCombatExperienceAsync(new RecordCombatExperienceRequest
{
    CharacterId = characterId,
    CombatExperienceType = CombatExperienceType.NEAR_DEATH,
    Intensity = 0.9f
});

// May result in: riskTolerance ↓, retreatThreshold ↑
```

### 5.6 Character Data Loading in ActorRunner

When an actor starts for a character, the ActorRunner automatically loads personality, combat preferences, and backstory:

```csharp
// From ActorRunner.cs - character data loading
if (CharacterId.HasValue)
{
    // Load and cache personality traits
    var personality = await _personalityCache.GetOrLoadAsync(CharacterId.Value, ct);
    scope.RegisterProvider(new PersonalityProvider(personality));

    // Load and cache combat preferences
    var combatPrefs = await _personalityCache.GetCombatPreferencesOrLoadAsync(CharacterId.Value, ct);
    scope.RegisterProvider(new CombatPreferencesProvider(combatPrefs));

    // Load and cache backstory
    var backstory = await _personalityCache.GetBackstoryOrLoadAsync(CharacterId.Value, ct);
    scope.RegisterProvider(new BackstoryProvider(backstory));
}
```

Data is cached with a 5-minute TTL and stale-if-error fallback for resilience.

### 5.7 Character Agent Query API

Event Actors can query character agents for available options using the generalized `/actor/query-options` endpoint:

```yaml
# Event Brain querying combat options from a character
- query_options:
    actor_id: "${participant.actorId}"
    query_type: combat
    freshness: fresh  # Get current options, not cached
    context:
      combat_state: "${combat.state}"
      opponent_ids: "${combat.opponents}"
      environment_tags: "${environment.affordances}"
    result_variable: "character_options"

# Use options in choreography decision
- for_each:
    items: "${character_options.options}"
    as: "option"
    do:
      - cond:
          if: "${option.confidence > 0.7}"
          then:
            - add_to_option_pool: "${option}"
```

**Freshness levels:**
- `fresh` - Force re-evaluation of options (for critical decisions)
- `cached` - Accept recently cached options (configurable max age)
- `stale_ok` - Accept any cached value (for low-priority queries)

---

## 6. Event Actors

### 6.1 The Invisible Director

Event Actors are **drama coordinators** for any significant situation. They don't fight or act directly - they **script** the action in real-time.

### 6.2 Event Actor Hierarchy

```
┌─────────────────────────────────────────────────────────────────┐
│              REGIONAL WATCHER / WORLD EVENTS                     │
│                                                                  │
│  Monitors: proximity, antagonism, dramatic potential             │
│  Spawns: Event Actors when interestingness thresholds crossed    │
│                                                                  │
│  Always-on Event Actors for VIPs:                                │
│  - Kings, god avatars, elder dragons                             │
│  - At sufficient power level, EVERYTHING is an event             │
└──────────────────────────────┬───────────────────────────────────┘
                               │
            spawns             │              spawns
       ┌───────────────────────┼───────────────────────────┐
       ▼                       ▼                           ▼
┌─────────────┐      ┌─────────────────┐        ┌─────────────────┐
│  FESTIVAL   │      │    DISASTER     │        │  CONFRONTATION  │
│  DAY EVENT  │      │     EVENT       │        │     EVENT       │
│             │      │                 │        │                 │
│ The day     │      │ Earthquake,     │        │ Two rivals      │
│ itself is   │      │ dragon attack,  │        │ finally meet    │
│ an event    │      │ magical storm   │        │                 │
└──────┬──────┘      └────────┬────────┘        └────────┬────────┘
       │                      │                          │
       │ spawns sub-events    │                          │
       ▼                      ▼                          ▼
┌─────────────┐      ┌─────────────────┐        ┌─────────────────┐
│ Market      │      │ Building        │        │ Cinematic       │
│ Brawl       │      │ Collapse        │        │ Exchange        │
│ Event       │      │ Event           │        │ (Fight)         │
└─────────────┘      └─────────────────┘        └─────────────────┘
```

### 6.3 Interestingness Triggers

Event Actors spawn when the Regional Watcher detects:

| Trigger | Description |
|---------|-------------|
| **Power level proximity** | Matched combatants (interesting fight potential) |
| **Antagonism score** | Relationship system indicates hatred/rivalry |
| **Environmental drama** | Near hazards, in public, at significant location |
| **Story flags** | Characters with narrative significance |
| **Player involvement** | Human players make things interesting by default |
| **VIP presence** | Some characters ALWAYS have an Event Actor |

### 6.4 Event Brain Responsibilities

| Responsibility | Implementation |
|----------------|----------------|
| Discover environment | Query Map Service for affordances in combat bounds |
| Track combat state | Authoritative state machine: setup → exchange → resolution |
| Generate options | Build valid option sets from capabilities × opportunities |
| Present choices | Send QTE prompts to participants with timing windows |
| Resolve outcomes | Evaluate choices, apply effects, update state |
| Choreograph result | Emit ABML channel instructions for animation/camera/audio |
| Handle interruptions | Detect and integrate crisis moments |

### 6.5 Combat Without Event Actors

Fights happen constantly without Event Actors - and that's fine. Normal combat:
- Handled entirely by game engine/server
- Direct bytecode evaluation each frame
- Character agent provides defaults
- No Bannou hops required

**Event Actors ENHANCE already-good combat when the situation warrants it.**

### 6.6 Future Extension: ABML encounter_instruction Handler

When Event Brains orchestrate encounters (cinematics, QTEs, quest events), they send instructions to participating actors. Currently, these instructions would be handled with hardcoded switch statements in actor code.

**Future enhancement**: An ABML `encounter_instruction` handler that maps instructions to actions data-driven per actor type:

```yaml
# In actor ABML definition
handlers:
  encounter_instruction:
    type: instruction_mapper
    mappings:
      move_to_position:
        action: navigate
        params:
          destination: "{{instruction.position}}"
          speed: "{{instruction.speed | default: 'walk'}}"
      deliver_line:
        action: speak
        params:
          dialogue_id: "{{instruction.line_id}}"
          emotion: "{{instruction.emotion}}"
      play_animation:
        action: animate
        params:
          animation: "{{instruction.animation_id}}"
      wait_for_cue:
        action: idle_until
        params:
          signal: "{{instruction.signal_name}}"
```

**Benefits**:
- Different actor types interpret instructions differently (guard's `deliver_line` includes authoritative stance, merchant's includes nervous gestures)
- Matches the `query_options` ABML pattern for consistency
- Extensible without code changes

**Recommended staged implementation**:
1. **Phase 1**: Direct handling in actor code (understand instruction patterns first)
2. **Phase 2**: Extract to ABML handler once patterns crystallize from real usage

**Priority**: MEDIUM - Implement after encounter system is functional with hardcoded handling. Extract patterns to ABML when we understand which instruction types are common.

### 6.7 Event Brain Design Decisions

These architectural decisions guide Event Brain implementation:

**Why Event Brain Runs as a Standard Actor**

Event Brains use the same `ActorRunner` infrastructure as character agents, not a separate runtime class. This provides:
- Same execution model reduces complexity
- Event Brain behaviors use standard ABML syntax
- State persistence, hot reload, and cache invalidation work automatically
- Only difference is perception subscriptions (region-level vs character-level)

**Why Choreography Uses Perception Events**

Event Brains emit choreography as perception events to participants, not direct RPC:
- Consistent with existing pub/sub architecture
- Character agents can accept, reject, or modify choreography
- Supports async execution with natural timeout handling
- Works with existing sync point infrastructure (CutsceneCoordinator)

**Why Options Are Self-Described by Actors**

The `/actor/query-options` endpoint reads from actor state rather than computing options:
- Actors are self-describing - they know their own capabilities
- Options can depend on arbitrary actor state (mood, memories, goals)
- Keeps the query endpoint thin and generic
- Same pattern works for any actor type (characters, event brains, NPCs)

**Why Requester Determines Freshness**

The caller specifies desired freshness level (fresh, cached, stale_ok):
- Consistent with lib-mapping's AffordanceFreshness pattern
- Event Brain knows urgency better than the system
- Enables optimization: stale_ok for batch queries, fresh for critical decisions

### 6.8 Event Brain ABML Actions Reference

Event Brains use specialized ABML actions for coordination. These are available in addition to standard ABML actions.

#### Coordination Actions (lib-actor)

| Action | Parameters | Description |
|--------|------------|-------------|
| `query_options` | `actor_id`, `query_type`, `freshness?`, `max_age_ms?`, `context?`, `result_variable?` | Query another actor's available options via RPC |
| `query_actor_state` | `actor_id`, `paths?`, `result_variable?` | Query another actor's state from local registry |
| `emit_perception` | `target_character`, `perception_type`, `urgency?`, `source_id?`, `data?` | Send choreography instruction to a character |
| `schedule_event` | `delay_ms`, `event_type`, `target_character?`, `data?` | Schedule a delayed event |
| `state_update` | `path`, `operation`, `value` | Update working memory (set/append/increment/decrement) |
| `set_encounter_phase` | `phase`, `result_variable?` | Transition the encounter to a new phase |
| `end_encounter` | `result_variable?` | End the current encounter |

#### Cognition Actions (lib-behavior)

These are primarily for Character Agents but can be used by Event Brains for advanced orchestration:

| Action | Parameters | Description |
|--------|------------|-------------|
| `filter_attention` | `input`, `attention_budget?`, `priority_weights?` | Filter perceptions by attention budget |
| `query_memory` | `perceptions`, `entity_id`, `limit?` | Query memory store for relevant memories |
| `assess_significance` | `perception`, `memories?`, `personality?`, `weights?` | Score perception significance |
| `store_memory` | `entity_id`, `perception`, `significance?` | Store significant perception as memory |
| `evaluate_goal_impact` | `perceptions`, `current_goals`, `current_plan?` | Evaluate perception impact on goals |
| `trigger_goap_replan` | `goals`, `urgency?`, `world_state?` | Trigger GOAP replanning |

#### Example: Complete Event Brain Behavior

```yaml
# Arena Fight Coordinator - queries options, emits choreography, manages phases
flows:
  monitor_combat:
    # Query both fighters' options
    - query_options:
        actor_id: "${fighter_a}"
        query_type: "combat"
        freshness: "cached"
        max_age_ms: 2000
        result_variable: "fighter_a_options"

    # Detect dramatic opportunity
    - if:
        condition: "${detect_opportunity(fighter_a_options)}"
        then:
          # Send choreography instruction
          - emit_perception:
              target_character: "${fighter_a}"
              perception_type: "choreography_instruction"
              urgency: 0.95
              data:
                instruction_type: "execute_sequence"
                sequence_id: "dramatic_clash"

          # Transition phase
          - set_encounter_phase:
              phase: "cinematic"

  cleanup:
    - end_encounter: {}
```

See `examples/event-brains/` for complete examples:
- `arena-fight-coordinator.abml.yml` - Direct Coordinator pattern
- `god-of-monsters.abml.yml` - God/Regional Watcher pattern

---

## 7. Behavior Integration

### 7.1 The Behavior Stack

Characters have layered behavior stacks:

```
Character Behavior Stack
├── Base Layer (species/type fundamentals)
├── Cultural Layer (faction, background)
├── Professional Layer (class, occupation)
├── Personal Layer (individual quirks)
└── Situational Layer (current context overrides)
```

Each layer produces IntentEmissions. The stack merges them using archetype-defined strategies.

### 7.2 Behavior Types and Variants

```
Character Behaviors
├── combat (type)
│   ├── sword-and-shield (variant)
│   ├── dual-wield (variant)
│   └── unarmed (variant)
├── movement (type)
│   ├── standard (variant)
│   └── mounted (variant)
└── interaction (type)
    └── default (variant)
```

### 7.3 Intent Channels

When multiple behavior types are active simultaneously, they output to Intent Channels with urgency values:

| Channel | Purpose | Merge Strategy |
|---------|---------|----------------|
| Locomotion | Movement decisions | Highest urgency wins |
| Action | Combat/interaction actions | Highest urgency wins |
| Attention | Focus targets | Blended by weight |
| Stance | Body positioning | Highest urgency wins |
| Expression | Facial animations | Blended |
| Vocalization | Speech/sounds | Priority queue |

### 7.4 Actor State → Behavior Input

Actors update state variables that behaviors read:

```csharp
// Actor emits
await _messageBus.PublishAsync($"character.{characterId}.state", new StateUpdateEvent
{
    Feelings = new() { ["angry"] = 0.9, ["fearful"] = 0.2 },
    Goals = new() { Primary = "defeat_rival", Target = rivalId },
    Modifiers = new() { ["combat_style"] = "aggressive" }
});

// Behavior stack reads (in bytecode)
// if (state.feelings.angry > 0.7 && perceptions.enemy_nearby)
//     emit_intent(action, "aggressive_combo", urgency=0.9);
```

---

## 7. Perception and Cognition

### 7.1 Perception Event Format

```yaml
CharacterPerceptionEvent:
  type: object
  properties:
    characterId:
      type: string
      format: uuid
    perceptionType:
      type: string
      enum: [visual, auditory, tactile, olfactory, proprioceptive]
    sourceId:
      type: string
    sourceType:
      type: string
      enum: [character, npc, object, environment]
    data:
      type: object
      additionalProperties: true
    urgency:
      type: number
      minimum: 0
      maximum: 1
    timestamp:
      type: string
      format: date-time
```

### 7.2 Cognition Handlers

The behavior plugin provides six cognition handlers:

| Handler | Purpose |
|---------|---------|
| `filter_attention` | Prioritize perceptions within attention budget |
| `assess_significance` | Score perceptions for memory storage |
| `query_memory` | Retrieve relevant memories for context |
| `store_memory` | Persist significant experiences |
| `evaluate_goal_impact` | Determine if perceptions affect goals |
| `trigger_goap_replan` | Request GOAP planner to generate new plan |

### 7.3 Memory System (MVP)

Current implementation uses keyword-based relevance matching:

| Factor | Weight | Description |
|--------|--------|-------------|
| Category match | 0.3 | Memory category matches perception category |
| Content overlap | 0.4 | Shared keywords between perception and memory |
| Metadata overlap | 0.2 | Shared keys in metadata |
| Recency bonus | 0.1 | Memories < 1 hour old get boost |
| Significance bonus | 0.1 | Higher significance memories score higher |

**When Keyword Matching is Sufficient:**
- Game-defined perception categories ("threat", "social", "routine")
- Entity-based relationships (entity IDs in metadata)
- Structured events (combat encounters, dialogue exchanges)
- NPCs writing their own memories (consistent terminology)
- No player-generated content requiring fuzzy matching

**When to Consider Embedding Migration:**
- Semantic similarity needed: "The merchant cheated me" ↔ "I was swindled at the market"
- Cross-language or cross-cultural concept matching
- Player-generated content (names, descriptions)
- Large memory stores (1000+ memories per entity) where keyword matching degrades
- Narrative-driven games where emotional/thematic connections matter

**Migration Path:**
1. `IMemoryStore` interface is already designed for swappable implementations
2. Create `EmbeddingMemoryStore` implementing the same interface
3. Configure via `BehaviorServiceConfiguration` which implementation to use
4. No changes needed to cognition pipeline or handlers

**Trade-offs:**

| Aspect | Keyword (MVP) | Embedding (Future) |
|--------|---------------|-------------------|
| Latency | Fast (in-memory) | Slower (external service) |
| Accuracy | Exact matches only | Semantic similarity |
| Infrastructure | None | LLM/embedding service |
| Cost | Free | Per-query cost |
| Debugging | Transparent | Black box |

### 7.4 Cognition Constants

All magic numbers are centralized in `CognitionConstants`:

| Constant | Value | Purpose |
|----------|-------|---------|
| LowUrgencyThreshold | 0.3 | Full deliberation below this |
| HighUrgencyThreshold | 0.7 | Immediate reaction at/above |
| DefaultThreatWeight | 10.0 | Priority for threat perceptions |
| DefaultThreatFastTrackThreshold | 0.8 | Bypasses normal pipeline |

### 7.5 Cognition Templates

The cognition system uses **templates** that define which handlers run in what order. Three embedded templates are provided:

| Template | Use Case | Stages |
|----------|----------|--------|
| `humanoid_base` | Humanoid NPCs | All 5 stages (filter → memory_query → significance → storage → intention) |
| `creature_base` | Animals/creatures | Simpler (skips significance, lower attention budget, faster reactions) |
| `object_base` | Interactive objects | Minimal (just filter + intention for traps, doors, etc.) |

**Template differences**:

```
humanoid_base:                    creature_base:                  object_base:
├── filter (budget=100)           ├── filter (budget=50)          ├── filter (budget=10)
├── memory_query (max=20)         ├── memory_query (max=5)        └── intention
├── significance                  └── intention                       (no memory/significance)
├── storage
└── intention
    ├── goal_impact
    └── goap_replan
```

Creatures skip significance assessment - they react instinctively. Objects skip memory entirely - they're stateless responders.

### 7.6 Building Cognition Pipelines

The `CognitionBuilder` constructs pipelines from templates with optional character-specific overrides:

```csharp
// Get the template registry (DI-injected)
ICognitionTemplateRegistry registry = ...;
ICognitionBuilder builder = new CognitionBuilder(registry, handlerRegistry, logger);

// Build a standard humanoid pipeline
var pipeline = builder.Build("humanoid_base");

// Build with character-specific overrides
var overrides = new CognitionOverrides
{
    Overrides =
    [
        // Make this character less reactive to threats (e.g., battle-hardened veteran)
        new ParameterOverride
        {
            Stage = "filter",
            HandlerId = "attention_filter",
            Parameters = new Dictionary<string, object>
            {
                ["threat_fast_track_threshold"] = 0.95f  // Only extreme threats fast-track
            }
        },
        // Disable memory storage for a mindless zombie
        new DisableHandlerOverride
        {
            Stage = "storage",
            HandlerId = "store_memory"
        }
    ]
};

var customPipeline = builder.Build("humanoid_base", overrides);
```

**Override types**:
- `ParameterOverride` - Modify handler parameters (most common)
- `DisableHandlerOverride` - Disable a handler entirely or conditionally
- `AddHandlerOverride` - Insert a custom handler at a specific position
- `ReplaceHandlerOverride` - Swap one handler for another
- `ReorderHandlerOverride` - Change handler execution order

### 7.7 Invoking the Cognition Pipeline

Actors invoke cognition through `ICognitionPipeline.ProcessAsync()`:

```csharp
// In the actor tick loop
var result = await _cognitionPipeline.ProcessAsync(
    perception,
    new CognitionContext
    {
        AgentId = _actorId,
        AbmlContext = _executionContext,
        HandlerRegistry = _handlerRegistry
    },
    cancellationToken);

// Handle the result
if (result.Success)
{
    if (result.RequiresReplan)
    {
        // GOAP replanning was triggered with urgency level
        await HandleReplanAsync(result.ReplanUrgency);
    }

    // Process filtered perceptions
    foreach (var perception in result.ProcessedPerceptions)
    {
        // These passed all cognition stages
    }
}
```

**Batch processing** for efficiency when multiple perceptions arrive:

```csharp
var result = await _cognitionPipeline.ProcessBatchAsync(
    perceptions,  // IReadOnlyList<object>
    context,
    cancellationToken);
```

### 7.8 Cognition Code Locations

| Component | Location |
|-----------|----------|
| `CognitionPipeline` | `lib-behavior/Cognition/CognitionBuilder.cs:448-515` |
| `CognitionBuilder` | `lib-behavior/Cognition/CognitionBuilder.cs` |
| `CognitionTemplateRegistry` | `lib-behavior/Cognition/CognitionTemplateRegistry.cs` |
| `CognitionTypes` | `lib-behavior/Cognition/CognitionTypes.cs` |
| Cognition Handlers | `lib-behavior/Handlers/` |

### 7.9 Future Extension: Character-Level Cognition Overrides from YAML

The cognition system currently supports:
- Parsing YAML cognition **templates** (`CognitionTemplateRegistry.ParseYaml()`)
- Building pipelines with **programmatic** overrides (`CognitionBuilder.Build()`)

A future enhancement would parse character-definition YAML directly into `CognitionOverrides`:

```yaml
# Character definition with cognition override
character:
  id: paranoid_guard
  cognition:
    base: humanoid_base
    overrides:
      filter:
        - attention_filter: { max_perceptions: 5, threat_fast_track_threshold: 0.6 }
      significance:
        - assess_significance: { weights: { threat: 1.5 } }
```

**What exists now**:
- `CognitionTemplateRegistry.ParseYaml()` parses full templates
- `CognitionBuilder.Build(templateId, overrides)` accepts programmatic overrides

**What to add when needed**:
1. `CognitionOverridesDto` for YAML parsing
2. `CharacterDefinitionLoader` or extend entity archetype registry
3. Wire into entity creation pipeline

**Priority**: LOW - The cognition infrastructure works today with programmatic overrides. YAML parsing becomes valuable when characters are defined in data files rather than code.

### 7.10 Emotional State Model

Actors maintain emotional state that influences behavior selection. The model uses 8 dimensions with decay and personality baselines:

```yaml
emotional_state:
  # Primary dimensions (0.0 to 1.0)
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
  mood: "content"              # Aggregated mood label

  # Decay rates (per second) - emotions return toward baseline
  decay:
    stress: 0.01
    fear: 0.02
    anger: 0.015
    alertness: 0.005

  # Personality baseline (emotions return to these over time)
  baseline:
    stress: 0.2
    alertness: 0.4
    comfort: 0.5
```

**Design decisions**:
- **8 dimensions** cover the emotional range needed for NPC behavior without overwhelming complexity
- **Decay rates** prevent permanent emotional states (a guard doesn't stay angry forever)
- **Personality baselines** make characters feel consistent (an anxious character has higher stress baseline)
- **Dominant emotion** simplifies behavior selection (check one value, not eight)

### 7.11 Memory Storage Structure

Memory storage uses multiple indices for efficient queries:

```yaml
memory_store:
  # Recent memories (circular buffer, oldest dropped when full)
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
        emotional_snapshot: { stress: 0.4, alertness: 0.7 }

  # Entity index: Quick lookup by entity ID
  entity_memories:
    "chr_suspicious_guy":
      threat_flag: true
      last_seen: "2024-01-08T14:30:00Z"
      encounter_count: 3
      memories: ["mem_001", "mem_023", "mem_045"]

  # Type index: Query by memory category
  type_index:
    threat: ["mem_001", "mem_015"]
    observation: ["mem_002", "mem_003"]
    crime: ["mem_015"]

  # Spatial index: Location-based queries (chunked by region)
  spatial_index:
    "stormwind_market": ["mem_001", "mem_002", "mem_003"]
```

**Index usage**:
- **entity_memories**: "What do I know about this person?" - O(1) lookup
- **type_index**: "What threats have I seen?" - O(1) by category
- **spatial_index**: "What happened near here?" - O(1) by region
- **recent**: Time-ordered fallback when indices don't match

### 7.12 Goal Priority System

Goals have base priorities from character templates, with temporary boosts from perceptions:

```yaml
goal_manager:
  # Base priorities from character template
  base_priorities:
    maintain_order: 70
    complete_patrol: 50
    investigate_suspicious: 75
    respond_to_crime: 90
    protect_citizens: 95

  # Active boosts (temporary priority increases)
  boosts:
    - goal: "investigate_suspicious"
      amount: 20
      expires: "2024-01-08T14:35:00Z"
      reason: "suspicious_activity_detected"

  # Calculated active priorities (base + boosts)
  active:
    - id: "protect_citizens"
      priority: 95
      conditions_met: true
    - id: "investigate_suspicious"
      priority: 95              # 75 + 20 boost
      conditions_met: false
    - id: "respond_to_crime"
      priority: 90
      conditions_met: false

  # Highest actionable goal (highest priority with conditions_met)
  current_goal:
    id: "protect_citizens"
    priority: 95
```

**Design decisions**:
- **Boosts expire** - a perception that increased "investigate" priority fades over time
- **Conditions tracked separately** - high priority doesn't mean actionable (respond_to_crime needs a crime)
- **Current goal is derived** - highest priority goal where conditions_met is true

### 7.13 Combat Preference Modifiers

Combat preferences are modified by emotional state and memories:

| Source | Effect | Example |
|--------|--------|---------|
| Stress | aggression × 0.9 | High stress slightly reduces aggression |
| Fear | aggression × 0.7 | Fear significantly reduces aggression |
| Anger | aggression × 1.3 | Anger increases aggression |
| Threat memory | caution × 1.2 | Known threats increase caution |
| Low health | caution × 1.4 | Self-preservation instinct |

**Calculation flow**:
```
personality_base (aggression: 0.4, caution: 0.6)
    ↓ apply emotional modifiers
    ↓ apply memory modifiers
    ↓ apply health modifiers
calculated (style: "balanced", aggression: 0.4, preferred_range: "medium")
```

**Output determines**:
- **style**: aggressive | defensive | balanced - which behavior flows are preferred
- **preferred_range**: close | medium | long - positioning preferences
- **tactics**: list of tactical hints for behavior selection

---

## 8. Cutscene and QTE Orchestration

### 8.1 The Coordination Layer

The behavior plugin provides coordination infrastructure for multi-participant cinematics:

```
lib-behavior/Coordination/
├── CutsceneCoordinator.cs    # Session management
├── CutsceneSession.cs        # Individual session state
├── SyncPointManager.cs       # Channel synchronization
└── InputWindowManager.cs     # QTE timing windows
```

### 8.2 Cutscene Sessions

```csharp
var session = await _coordinator.CreateSessionAsync(
    sessionId: "fight-scene-123",
    cinematicId: "dramatic_duel",
    participants: [heroId, villainId],
    options: new CutsceneSessionOptions
    {
        DefaultTimeout = TimeSpan.FromSeconds(5),
        AllowExtensions = true
    });
```

### 8.3 Multi-Channel Execution

Cutscenes use parallel channels with sync points:

```yaml
channels:
  camera:
    - fade_in: { duration: 1s }
    - move_to: { shot: wide_throne_room }
    - emit: establishing_complete
    - wait_for: @hero.at_mark
    - crane_up: { reveal: boss }

  hero:
    - wait_for: @camera.establishing_complete
    - walk_to: { mark: hero_mark_1, speed: cautious }
    - emit: at_mark
    - speak: "Your reign ends today!"

  audio:
    - play: { track: ambient_throne_room }
    - wait_for: @camera.establishing_complete
    - crossfade_to: { track: boss_theme }
```

### 8.4 QTE Input Windows

The `InputWindowManager` handles timed input collection:

```csharp
var window = await _inputManager.CreateWindowAsync(
    sessionId: sessionId,
    participantId: heroId,
    options: ["dodge_left", "dodge_right", "parry", "counter"],
    defaultOption: "parry",  // Character agent's choice
    timeout: TimeSpan.FromSeconds(2));

// Window executes default if player doesn't respond
var choice = await window.GetResultAsync(ct);
```

### 8.5 Streaming Composition

Cinematics can be extended mid-execution:

```
Timeline:
0s     Game Server receives Cinematic A (compiled bytecode)
0-10s  Executes Cinematic A
8s     Event Brain decides to extend based on player action
8.5s   Game Server receives Extension B
10s    Cinematic A hits continuation point, seamlessly transitions to B
10-18s Executes Extension B
```

Key properties:
- Initial delivery is **complete** - game server can execute independently
- Extensions are **additive** - don't modify what's executing
- Missing extensions are **fine** - original completes gracefully

### 8.6 Control Handoff and State Sync

When cinematics complete and return control to behavior, state synchronization follows a clear architectural boundary:

**Server (Bannou) Responsibility:**
- Communicate the **target state** (position, health, stance, etc.)
- Signal the **handoff style** to the game client
- Update the `EntityStateRegistry` for behavior evaluation

**Client (Game Engine) Responsibility:**
- Receive the target state from the registry
- Apply the appropriate transition based on handoff style
- Render animations and visual interpolation

```csharp
// Server-side: StateSync writes target state
await _stateSync.SyncStateAsync(
    entityId,
    finalCinematicState,  // Target state from cinematic
    new ControlHandoff
    {
        Style = HandoffStyle.Blend,  // Signal to client: interpolate smoothly
        SyncState = true
    },
    ct);
```

**HandoffStyle Semantics:**

| Style | Server Action | Client Action |
|-------|---------------|---------------|
| `Instant` | Write target state | Snap immediately to target |
| `Blend` | Write target state | Smoothly interpolate to target |
| `Explicit` | Write target state | Handoff already handled externally |

**Why server-side blending would be wrong:**
- Server doesn't render or know current visual state
- Frame-rate interpolation requires client-side timing
- Animation systems are engine-specific (Stride, Unity, etc.)

**Game Server Integration (Stride-side):**
```csharp
// Subscribe to state updates from EntityStateRegistry
_stateRegistry.StateUpdated += (sender, args) =>
{
    var entity = GetEntity(args.EntityId);

    // Apply based on handoff style (from cinematic metadata)
    if (args.HandoffStyle == HandoffStyle.Blend)
    {
        // Start smooth interpolation over ~0.5s
        entity.StartStateBlend(args.NewState, duration: 0.5f);
    }
    else
    {
        // Snap immediately
        entity.ApplyState(args.NewState);
    }
};
```

---

## 9. Infrastructure Integration

### 9.1 The Four Pillars

The actor system builds on four critical services:

```
                    ┌─────────────────────────────────────┐
                    │         ACTOR SYSTEM                │
                    └──────────────┬──────────────────────┘
                                   │
           ┌───────────────────────┼───────────────────────┐
           │                       │                       │
           ▼                       ▼                       ▼
┌─────────────────┐    ┌─────────────────┐    ┌─────────────────┐
│   lib-mapping   │    │  lib-behavior   │    │   lib-asset     │
│ (Spatial Query) │    │ (GOAP + ABML)   │    │ (Distribution)  │
└─────────────────┘    └─────────────────┘    └─────────────────┘
                                │
                    ┌───────────┴───────────┐
                    │   lib-orchestrator    │
                    │  (Pool Management)    │
                    └───────────────────────┘
```

### 9.2 lib-mapping Integration

Event actors use the mapping system for spatial awareness:

```yaml
# Event actor queries affordances
- service_call:
    service: mapping
    method: query-affordance
    parameters:
      region_id: "${region.id}"
      affordance_type: "ambush"
      bounds: "${combat_bounds}"
    result_variable: "nearby_affordances"

# Generate options based on environment
- for_each:
    items: "${nearby_affordances.locations}"
    as: "location"
    do:
      - call: { flow: generate_environmental_option }
```

### 9.3 lib-behavior Integration

Actors execute ABML documents via the behavior service:

- **Tree-walking** `DocumentExecutor` for cloud-side cognition
- **Bytecode** `BehaviorModelInterpreter` for game server execution
- **GOAP** planner for goal-directed action selection
- **Cognition handlers** for perception processing

### 9.4 Behavior Storage and Distribution

Behaviors follow the **Asset Service pattern** - the same architecture used for textures, models, and other game assets. Large behavior files are never transferred directly through the system; instead, presigned URLs provide direct access to MinIO/S3 storage.

#### Compilation and Storage Flow

```
┌─────────────────────────────────────────────────────────────────┐
│ 1. COMPILE                                                      │
│    POST /behavior/compile                                       │
│    Body: { yaml: "behavior_version: 1\nname: npc-brain\n..." }  │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│ 2. BEHAVIOR SERVICE                                             │
│    DocumentParser → SemanticAnalyzer → BytecodeEmitter          │
│    Output: Binary bytecode (.bbm format)                        │
│    BehaviorId: Deterministic hash of content                    │
└─────────────────────────────────────────────────────────────────┘
                              │
          ┌───────────────────┼───────────────────┐
          ▼                   ▼                   ▼
┌──────────────────┐ ┌──────────────────┐ ┌──────────────────┐
│ ASSET SERVICE    │ │ STATE STORE      │ │ MESSAGE BUS      │
│                  │ │                  │ │                  │
│ Presigned upload │ │ behavior-        │ │ behavior.created │
│ to MinIO/S3      │ │ metadata:{id}    │ │ behavior.updated │
│                  │ │                  │ │                  │
│ Key: behaviors/  │ │ - behaviorId     │ │                  │
│ {id}.bbm         │ │ - assetId        │ │                  │
│                  │ │ - name, category │ │                  │
│                  │ │ - bytecodeSize   │ │                  │
└──────────────────┘ └──────────────────┘ └──────────────────┘
```

#### Retrieval Flow

```
┌─────────────────────────────────────────────────────────────────┐
│ Game Server / Actor                                             │
│ POST /behavior/cache/get { behaviorId: "abc123..." }            │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│ BEHAVIOR SERVICE                                                │
│ 1. Lookup metadata in state store                               │
│ 2. Request presigned download URL from Asset Service            │
│ 3. Return URL (or inline bytecode for small behaviors)          │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│ RESPONSE                                                        │
│ {                                                               │
│   "behaviorId": "abc123...",                                    │
│   "downloadUrl": "https://minio:9000/...?X-Amz-Signature=...",  │
│   "bytecodeSize": 4096,                                         │
│   "expiresAt": "2026-01-08T21:15:00Z"                           │
│ }                                                               │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│ Game Server downloads directly from MinIO (bypasses services)   │
│ Caches locally via RemoteAssetCache<T> with CRC verification    │
└─────────────────────────────────────────────────────────────────┘
```

#### Bundle Grouping

Related behaviors can be grouped into bundles for efficient bulk download:

```csharp
// Compile with bundle assignment
POST /behavior/compile
{
  "yaml": "...",
  "bundleId": "merchant-behaviors-v1"
}

// Later: download entire bundle
POST /behavior/bundle/get
{
  "bundleId": "merchant-behaviors-v1"
}
// Returns list of behaviorIds + combined download URL
```

#### Key Implementation Files

| File | Purpose |
|------|---------|
| `lib-behavior/BehaviorService.cs:414-471` | Asset Service upload integration |
| `lib-behavior/BehaviorBundleManager.cs` | Bundle grouping and management |
| `lib-actor/Caching/BehaviorDocumentCache.cs` | YAML document caching for actors |
| `Bannou.Client.SDK/Cache/RemoteAssetCache.cs` | Client-side caching with CRC |

#### Actor Template Reference

```yaml
category: npc-brain
behaviorRef: "asset://behaviors/npc-brain-v1"
autoSaveIntervalSeconds: 30
```

The `behaviorRef` format `asset://behaviors/{behaviorId}` is resolved by the actor runtime to fetch the compiled bytecode via the flow above.

### 9.5 lib-orchestrator Integration

The orchestrator spawns actor pool nodes on demand:

```yaml
# Request pool expansion
POST /orchestrator/spawn
{
  "template": "actor-pool",
  "count": 5,
  "configuration": {
    "plugins": ["actor"],
    "capacity": 1000
  }
}
```

---

## 10. State Management

### 10.1 Actor State

```csharp
public class ActorState
{
    public string ActorId { get; set; }
    public string TemplateId { get; set; }
    public string Category { get; set; }

    // Behavior execution state
    public Dictionary<string, object?> Variables { get; set; }
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

### 10.2 Auto-Save Configuration

```yaml
# Per-template configuration
category: npc-brain
behaviorRef: "asset://behaviors/npc-brain-v1"
autoSaveIntervalSeconds: 30  # Override default

# Or disable for transient actors
category: daily-cleanup
behaviorRef: "asset://behaviors/cleanup-v1"
autoSaveEnabled: false
```

### 10.3 State Recovery

When a pool node restarts or an actor migrates:
1. Control plane detects actor needs recovery
2. Loads last saved state from lib-state
3. Spawns actor on new node with restored state
4. Behavior resumes from saved position

---

## 11. Scaling and Distribution

### 11.1 Pool Node Architecture

Actor pool nodes are **peers** on the Bannou network:
- Unique app-ids per node
- Can send/receive events via lib-messaging
- Can make mesh API calls via lib-mesh

### 11.2 Horizontal Scaling

For 10,000 NPCs × 10 events/second = 100,000 events/second:
- Direct subscription scales horizontally with pool nodes
- Control plane only handles lifecycle (spawn, stop, migrate)
- No bottleneck in event routing

### 11.3 Actor Distribution

```yaml
# Pool node capacity
MAX_ACTORS_PER_NODE: 1000

# Distribution strategy
actor_assignment:
  strategy: locality  # Prefer co-location with related actors
  fallback: round_robin
  migration_threshold: 0.8  # Migrate when node at 80% capacity
```

### 11.4 Event Tap Pattern

Event actors can "tap" specific characters to receive their events:

```
                    RabbitMQ Fanout Exchange
                    (routing_id = character-123)
                              │
              ┌───────────────┼───────────────┐
              │               │               │
              ▼               ▼               ▼
       ┌─────────────┐ ┌─────────────┐ ┌─────────────┐
       │  Character  │ │   Player    │ │   Event     │
       │   Agent     │ │   Client    │ │   Actor     │
       │  (NPC side) │ │  (if any)   │ │   (tap)     │
       └─────────────┘ └─────────────┘ └─────────────┘
```

---

## 12. API Reference

### 12.1 Actor Templates

| Endpoint | Description |
|----------|-------------|
| `POST /actor/template/create` | Create actor template |
| `POST /actor/template/get` | Get template by ID or category |
| `POST /actor/template/list` | List all templates |
| `POST /actor/template/update` | Update template |
| `POST /actor/template/delete` | Delete template |

### 12.2 Actor Instances

| Endpoint | Description |
|----------|-------------|
| `POST /actor/spawn` | Spawn new actor from template |
| `POST /actor/get` | Get actor (instantiate-on-access if template allows) |
| `POST /actor/stop` | Stop running actor |
| `POST /actor/list` | List actors with filters |
| `POST /actor/send-message` | Send message to actor |

### 12.3 Messaging Topics

**Control Plane → Pool Node:**
```
actor.node.{poolAppId}.spawn    -> SpawnActorCommand
actor.node.{poolAppId}.stop     -> StopActorCommand
actor.node.{poolAppId}.message  -> SendMessageCommand
```

**Pool Node → Control Plane:**
```
actor.pool-node.heartbeat       -> PoolNodeHeartbeatEvent
actor.instance.status-changed   -> ActorStatusChangedEvent
actor.instance.completed        -> ActorCompletedEvent
```

**Perception Events:**
```
character.{characterId}.perception  -> CharacterPerceptionEvent
character.{characterId}.state       -> StateUpdateEvent
```

---

## 13. Game Server Integration (Stride-side)

The actor system requires game server support to complete the perception-cognition-action loop.

### 13.1 Data Flow

```
┌────────────────────────────────────────────────────────────────┐
│                    STRIDE GAME SERVER                          │
│                                                                │
│  Character experiences something (sees enemy, finds item)      │
│                            │                                   │
│                            │ BROADCAST (fire and forget)       │
│                            │ Topic: character.{id}.perceptions │
│                            ▼                                   │
└────────────────────────────┼───────────────────────────────────┘
                             │
                             │ lib-messaging (RabbitMQ fanout)
                             │
┌────────────────────────────┼───────────────────────────────────┐
│                    NPC BRAIN ACTOR                             │
│                                                                │
│  Subscribed to perceptions → cognition pipeline → state update │
│                            │                                   │
│                            │ lib-mesh invocation               │
│                            │ Endpoint: character/state-update  │
│                            ▼                                   │
└────────────────────────────┼───────────────────────────────────┘
                             │
┌────────────────────────────┼───────────────────────────────────┐
│                    STRIDE GAME SERVER                          │
│                                                                │
│  Apply state updates to behavior stack inputs                  │
│  - feelings.angry = 0.8                                        │
│  - goals.target = entityX                                      │
│  - memories.betrayed_by = [X]                                  │
│                                                                │
│  BehaviorModelInterpreter reads these and adjusts behavior     │
└────────────────────────────────────────────────────────────────┘
```

### 13.2 Game Server Requirements

The game server must implement:

| Requirement | Description |
|-------------|-------------|
| **Publish perceptions** | Emit `CharacterPerceptionEvent` to `character.{characterId}.perceptions` fanout when character sees/hears/senses something |
| **Handle state updates** | Implement `character/state-update` endpoint for lib-mesh invocations |
| **Apply to behavior inputs** | Write received state (feelings, goals, memories) to behavior stack input slots |
| **Lizard brain fallback** | Characters function autonomously when no actor is connected |

### 13.3 Publishing Perceptions

When a character experiences something significant:

```csharp
// Fire-and-forget broadcast - no required subscribers
await _messageBus.PublishAsync(
    $"character.{characterId}.perceptions",
    new CharacterPerceptionEvent
    {
        CharacterId = characterId,
        PerceptionType = PerceptionType.Visual,
        SourceId = enemyId,
        SourceType = SourceType.Character,
        Data = new { distance = 10.5, threat_level = 0.8 },
        Urgency = 0.9f,
        Timestamp = DateTimeOffset.UtcNow
    });
```

**When to publish perceptions**:
- Entity enters perception range
- Combat events (damage taken, ally hurt)
- Environmental changes (fire started, door opened)
- Social events (conversation started, gift received)
- Inventory changes (item found, item stolen)

### 13.4 Handling State Updates

The game server receives state updates via lib-mesh:

```csharp
// Endpoint: character/state-update
public async Task<(StatusCodes, StateUpdateResponse?)> HandleStateUpdateAsync(
    CharacterStateUpdateEvent update,
    CancellationToken ct)
{
    var character = await GetCharacterAsync(update.CharacterId, ct);

    // Apply feelings to behavior input slots
    foreach (var (feeling, intensity) in update.Feelings)
    {
        character.BehaviorStack.SetInput($"feelings.{feeling}", intensity);
    }

    // Apply goals
    if (update.Goals?.Target != null)
    {
        character.BehaviorStack.SetInput("goals.target", update.Goals.Target);
    }

    // Apply memories (affect future decisions)
    foreach (var memory in update.NewMemories)
    {
        character.MemoryStore.Add(memory);
    }

    return (StatusCodes.Ok, new StateUpdateResponse { Applied = true });
}
```

### 13.5 Lizard Brain Fallback

Characters must function autonomously when no NPC brain actor is connected:

```csharp
// In behavior evaluation
var hasActorUpdates = character.LastStateUpdateTime > TimeSpan.FromSeconds(5);

if (!hasActorUpdates)
{
    // Fall back to default behavior stack without actor enrichment
    // Character still functions, just doesn't grow/evolve
    return EvaluateDefaultBehavior(character, perceptions);
}

// Use actor-enriched behavior with feelings, memories, goals
return EvaluateEnrichedBehavior(character, perceptions, actorState);
```

**Fallback guarantees**:
- Characters respond to immediate threats
- Basic pathfinding and navigation works
- Combat actions execute based on local state
- Social interactions use default personality

---

## Appendix A: Actor Categories

### Standard Categories

| Category | Purpose | Auto-Spawn | Persistence |
|----------|---------|------------|-------------|
| `npc-brain` | Character cognition | On character load | Full state |
| `event-combat` | Combat orchestration | On trigger | Session only |
| `event-regional` | Regional events | On trigger | Session only |
| `world-admin` | World maintenance | Singleton | Metrics only |
| `scheduled-task` | CRON-like jobs | On schedule | None |

### Category Configuration

```yaml
# Template definition
category: npc-brain
behaviorRef: "asset://behaviors/npc-brain-v1"
autoSpawnPattern: "character-{characterId}"
autoSpawnEnabled: true
autoSaveIntervalSeconds: 30
maxInstancesPerPool: 500
```

---

## Appendix B: Perception Event Types

### Visual Perceptions

| Type | Data Fields |
|------|-------------|
| `entity_spotted` | entityId, entityType, position, distance |
| `entity_lost` | entityId, lastKnownPosition |
| `movement_detected` | direction, speed, estimatedDistance |
| `gesture_seen` | gestureType, performer, target |

### Auditory Perceptions

| Type | Data Fields |
|------|-------------|
| `speech_heard` | speakerId, content, volume, direction |
| `sound_detected` | soundType, intensity, direction, source |
| `combat_noise` | combatType, participants, distance |

### Tactile Perceptions

| Type | Data Fields |
|------|-------------|
| `damage_received` | damageType, amount, source, hitLocation |
| `contact_made` | contactType, otherEntity, force |
| `environment_change` | changeType, position, intensity |

### Proprioceptive Perceptions

| Type | Data Fields |
|------|-------------|
| `stamina_change` | previousValue, currentValue, delta |
| `health_change` | previousValue, currentValue, delta |
| `status_effect` | effectType, source, duration |

---

*This document describes the Actor System as implemented in lib-actor and lib-behavior. For implementation details, see the source code and schema definitions.*
