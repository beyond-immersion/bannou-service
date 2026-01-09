# Query Options API Design

> **Status**: DESIGN DOCUMENT
> **Created**: 2026-01-09
> **Related**: [ACTOR_BEHAVIORS_GAP_ANALYSIS.md](ACTOR_BEHAVIORS_GAP_ANALYSIS.md), [THE_DREAM_GAP_ANALYSIS.md](THE_DREAM_GAP_ANALYSIS.md)

This document defines the design for the generalized `/actor/query-options` endpoint that enables Event Brain actors to query other actors for their available options.

---

## 1. Design Philosophy

### 1.1 Core Principle: Actors Self-Describe

Options are **not computed by the service endpoint**. Instead:
1. Actors maintain their available options in their state ("the bag")
2. The query endpoint reads from the actor's state
3. Actors can optionally receive context for context-sensitive recomputation

This keeps actors generic - any actor type can expose options by maintaining them in state.

### 1.2 Requester-Determines-Freshness Pattern

Following the established pattern from `lib-mapping`'s `AffordanceFreshness`:
- Caller specifies desired freshness level
- System returns cached data or triggers recomputation as needed
- Response includes metadata about actual freshness

### 1.3 First-Class ABML Support

Options become a first-class ABML concept:
- Standardized `options` block in ABML documents
- Standardized option schema (actionId, preference, risk, available, requirements)
- Runtime knows how to evaluate and cache options

---

## 2. API Design

### 2.1 Endpoint

```
POST /actor/query-options
```

### 2.2 Request Schema

```yaml
QueryOptionsRequest:
  type: object
  additionalProperties: false
  description: |
    Query an actor for its available options. Options are maintained by the actor
    in its state.memories.{queryType}_options and returned based on requested freshness.
  required:
    - actorId
    - queryType
  properties:
    actorId:
      type: string
      description: ID of the actor to query
    queryType:
      $ref: '#/components/schemas/OptionsQueryType'
      description: Type of options to query
    freshness:
      $ref: '#/components/schemas/OptionsFreshness'
      description: |
        Requested freshness level. Defaults to 'cached'.
        - fresh: Inject context and wait for actor to recompute
        - cached: Return cached options if within maxAgeMs
        - stale_ok: Return whatever is cached, even if expired
    maxAgeMs:
      type: integer
      minimum: 0
      maximum: 60000
      nullable: true
      description: |
        Maximum age of cached options in milliseconds (for 'cached' freshness).
        Defaults to 5000ms. If cached options are older, behavior depends on
        freshness level.
    context:
      $ref: '#/components/schemas/OptionsQueryContext'
      nullable: true
      description: |
        Optional context for the query. When provided with freshness='fresh',
        this context is injected as a perception to the actor, triggering
        context-sensitive option recomputation.
```

### 2.3 Response Schema

```yaml
QueryOptionsResponse:
  type: object
  additionalProperties: false
  description: Response containing the actor's available options
  required:
    - actorId
    - queryType
    - options
    - computedAt
  properties:
    actorId:
      type: string
      description: ID of the queried actor
    queryType:
      $ref: '#/components/schemas/OptionsQueryType'
      description: Type of options returned
    options:
      type: array
      description: Available options for the queried type
      items:
        $ref: '#/components/schemas/ActorOption'
    computedAt:
      type: string
      format: date-time
      description: When these options were last computed by the actor
    ageMs:
      type: integer
      description: Age of options in milliseconds (now - computedAt)
    characterContext:
      $ref: '#/components/schemas/CharacterOptionContext'
      nullable: true
      description: |
        Character-specific context that influenced these options.
        Only present for character-based actors.
```

### 2.4 Supporting Schemas

```yaml
OptionsQueryType:
  type: string
  description: |
    Type of options to query. Actors maintain options in state.memories.{type}_options.
    Well-known types are defined; actors can also expose custom types.
  enum:
    - combat      # Combat actions (attack, defend, retreat, etc.)
    - dialogue    # Dialogue options (greet, threaten, negotiate, etc.)
    - exploration # Exploration options (investigate, climb, search, etc.)
    - social      # Social interactions (trade, persuade, intimidate, etc.)
    - custom      # Custom actor-defined options (use customType field)

OptionsFreshness:
  type: string
  description: Controls caching behavior for options queries
  enum:
    - fresh         # Inject context and wait for recomputation
    - cached        # Return cached if within maxAgeMs, else like 'fresh'
    - stale_ok      # Return cached regardless of age
  default: cached

ActorOption:
  type: object
  additionalProperties: true
  description: |
    A single option available to the actor. The standardized fields enable
    Event Brain to reason about options; additional fields allow actor-specific data.
  required:
    - actionId
    - preference
    - available
  properties:
    actionId:
      type: string
      description: |
        Unique identifier for this action within the option type.
        Examples: "sword_slash", "greet_friendly", "climb_wall"
    preference:
      type: number
      format: float
      minimum: 0
      maximum: 1
      description: |
        How much the actor prefers this option (0-1), based on personality,
        combat preferences, current state, etc. Higher = more preferred.
    risk:
      type: number
      format: float
      minimum: 0
      maximum: 1
      nullable: true
      description: Estimated risk of this action (0=safe, 1=very risky)
    available:
      type: boolean
      description: Whether this option is currently available (requirements met)
    requirements:
      type: array
      nullable: true
      description: Requirements that must be met for this option
      items:
        type: string
    cooldownMs:
      type: integer
      nullable: true
      description: Milliseconds until this option becomes available again (if on cooldown)
    tags:
      type: array
      nullable: true
      description: Tags for categorization (e.g., ["melee", "aggressive", "loud"])
      items:
        type: string

OptionsQueryContext:
  type: object
  additionalProperties: true
  description: |
    Context provided with a fresh query. Injected as a perception to the actor
    to trigger context-sensitive option recomputation.
  properties:
    combatState:
      type: string
      nullable: true
      description: Current combat state (approaching, engaged, retreating, etc.)
    opponentIds:
      type: array
      nullable: true
      description: IDs of opponents in the current encounter
      items:
        type: string
    allyIds:
      type: array
      nullable: true
      description: IDs of allies in the current encounter
      items:
        type: string
    environmentTags:
      type: array
      nullable: true
      description: Environment tags (indoor, elevated, destructibles, narrow, etc.)
      items:
        type: string
    urgency:
      type: number
      format: float
      minimum: 0
      maximum: 1
      nullable: true
      description: How urgent is this query (affects option prioritization)
    customContext:
      type: object
      additionalProperties: true
      nullable: true
      description: Actor-specific context data

CharacterOptionContext:
  type: object
  additionalProperties: false
  description: Character-specific context that influenced option computation
  properties:
    combatStyle:
      type: string
      nullable: true
      description: Character's combat style (aggressive, defensive, balanced, etc.)
    riskTolerance:
      type: number
      format: float
      minimum: 0
      maximum: 1
      nullable: true
      description: Character's risk tolerance (0=cautious, 1=reckless)
    protectAllies:
      type: boolean
      nullable: true
      description: Whether character prioritizes ally protection
    currentGoal:
      type: string
      nullable: true
      description: Character's current primary goal
    emotionalState:
      type: string
      nullable: true
      description: Character's current dominant emotion
```

---

## 3. ABML First-Class Options

### 3.1 Options Block in Behavior

Actors that want to be queryable define an `options` block:

```yaml
version: "2.0"
metadata:
  id: "character_agent_base"
  type: "character_agent"

# First-class options definition
options:
  combat:
    # Options are computed based on expressions
    - actionId: "sword_slash"
      preference: "${combat.style == 'aggressive' ? 0.9 : 0.6}"
      risk: 0.3
      available: "${equipment.has_sword}"
      requirements: ["has_sword"]
      tags: ["melee", "offensive"]

    - actionId: "shield_bash"
      preference: "${combat.style == 'defensive' ? 0.7 : 0.4}"
      risk: 0.2
      available: "${equipment.has_shield}"
      requirements: ["has_shield"]
      tags: ["melee", "defensive", "stun"]

    - actionId: "retreat"
      preference: "${1.0 - combat.riskTolerance}"
      risk: 0.0
      available: true
      tags: ["movement", "defensive"]

    - actionId: "protect_ally"
      preference: "${combat.protectAllies ? 0.95 : 0.3}"
      risk: 0.5
      available: "${allies_nearby > 0}"
      requirements: ["ally_in_range"]
      tags: ["defensive", "support"]

  dialogue:
    - actionId: "greet_friendly"
      preference: "${personality.extraversion * personality.agreeableness}"
      available: true
      tags: ["social", "friendly"]

    - actionId: "intimidate"
      preference: "${personality.aggression * (1.0 - personality.agreeableness)}"
      available: true
      tags: ["social", "hostile"]

flows:
  main:
    # Normal behavior loop
    - process_perceptions
    - update_options  # Auto-generated action that evaluates options block
    - make_decisions
    - emit_intent
```

### 3.2 Runtime Behavior

When the runtime processes an `options` block:

1. **On Load**: Parse options expressions, create evaluation templates
2. **On `update_options` Action**:
   - Evaluate all option expressions in current scope
   - Store results in `state.memories.{type}_options` with timestamp
3. **On Query Perception**:
   - If `freshness=fresh`, inject context perception
   - Re-run `update_options` with context in scope
   - Return updated options

### 3.3 Automatic Option Maintenance

Actors with an `options` block automatically:
- Maintain `state.memories.{type}_options` for each option type
- Include `computedAt` timestamp
- Re-evaluate on each tick (or on-demand via context injection)

---

## 4. Implementation Plan

### 4.1 Phase 1: Schema & Endpoint (2-3 days)

1. Add schemas to `schemas/actor-api.yaml`
2. Regenerate services
3. Implement `QueryOptionsAsync` in ActorService
   - Read from actor's state.memories
   - Handle freshness levels
   - Context injection for fresh queries

### 4.2 Phase 2: ABML First-Class Support (2-3 days)

1. Add `options` block to ABML parser
2. Add `update_options` built-in action
3. Implement option expression evaluation
4. Add options to ActorState serialization

### 4.3 Phase 3: Integration & Testing (1-2 days)

1. Create example character agent with options
2. Create example Event Brain that queries options
3. Add http-tester tests for query-options endpoint
4. Document ABML options block in guides

---

## 5. Example Usage

### 5.1 Event Brain Querying Combat Options

```yaml
# Event Brain behavior
flows:
  choreograph_fight:
    # Query each participant for their options
    - foreach:
        items: "${participants}"
        as: "participant"
        do:
          - query_options:
              actorId: "${participant.actorId}"
              queryType: "combat"
              freshness: "cached"
              maxAgeMs: 3000
              context:
                combatState: "engaged"
                opponentIds: "${opponents}"
                environmentTags: "${environment.tags}"
              result_variable: "${participant.id}_options"

    # Analyze options to find dramatic possibilities
    - analyze_choreography:
        participant_options: "${all_participant_options}"
        dramatic_goals: "${current_dramatic_goals}"
        result_variable: "choreography_plan"

    # Emit the choreography
    - emit_choreography:
        plan: "${choreography_plan}"
```

### 5.2 Character Agent Maintaining Options

```yaml
# Character agent behavior with options
options:
  combat:
    - actionId: "power_strike"
      preference: "${personality.aggression * combat.riskTolerance}"
      risk: 0.6
      available: "${stamina >= 30}"
      requirements: ["stamina:30"]

    - actionId: "quick_jab"
      preference: "${0.5 + (personality.conscientiousness * 0.3)}"
      risk: 0.1
      available: true

    - actionId: "defensive_stance"
      preference: "${(1.0 - combat.riskTolerance) * 0.8}"
      risk: 0.0
      available: true

flows:
  main:
    - process_perceptions
    - update_emotional_state
    - update_options        # Automatically updates combat_options in state
    - evaluate_threats
    - select_action
    - emit_intent
```

---

## 6. Design Decisions

### 6.1 Why Read from State Instead of Computing?

**Considered**: Service computes options based on character data
**Decided**: Actor maintains options in state

**Rationale**:
- Actors are self-describing - they know their own capabilities
- Options can depend on arbitrary actor state (mood, memories, goals)
- Keeps the endpoint thin and generic
- Same pattern works for any actor type

### 6.2 Why Requester-Determines-Freshness?

**Considered**: System determines optimal freshness
**Decided**: Caller specifies freshness

**Rationale**:
- Consistent with lib-mapping AffordanceFreshness pattern
- Event Brain knows urgency better than the system
- Enables optimization (stale_ok for batch queries, fresh for critical decisions)

### 6.3 Why First-Class ABML Options Block?

**Considered**: Manual option maintenance in flows
**Decided**: First-class `options` block

**Rationale**:
- Enforces standardized option schema
- Reduces boilerplate in behavior files
- Makes options declarative and inspectable
- Enables tooling (option visualization, validation)

### 6.4 Why Generalized Query Types?

**Considered**: Separate endpoints per type (query-combat-options, query-dialogue-options)
**Decided**: Single endpoint with queryType parameter

**Rationale**:
- Less API surface area
- Easy to add new option types
- Consistent pattern for all queries
- Actors decide which types they support

---

## 7. Future Considerations

### 7.1 Option Negotiation

Event Brain could negotiate with actors:
- "Would you accept this choreography?"
- Actor responds with acceptance/modification

### 7.2 Option Dependencies

Options could declare dependencies on other actors' options:
- "I can do a combo attack IF ally does setup"
- Enables coordinated option selection

### 7.3 Option History

Track which options were chosen and outcomes:
- Informs preference evolution
- Enables learning from experience

---

*Document Status: DESIGN - Ready for implementation*
