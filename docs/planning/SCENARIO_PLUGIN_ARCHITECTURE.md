# Scenario Plugin Architecture

> **Version**: 1.0
> **Last Updated**: 2026-02-05
> **Status**: Planning
> **Dependencies**: lib-character (L2), lib-realm (L2), lib-location (L2), lib-quest (L4), lib-storyline (L4)

## Executive Summary

The Scenario plugin is the **bridge between narrative theory and game mechanics**. If Story Templates (in the SDK) define the *kinds* of stories that can exist (tragedy, hero's journey, revenge arc), Scenarios define the *concrete implementations* of those stories in the game world - complete with triggering conditions, character archetypes, situational requirements, and resulting state changes.

**Core Principle**: Scenarios are reusable narrative building blocks. They can be triggered directly by game servers (traditional MMO style) or discovered and composed by Regional Watchers (emergent storytelling style). The same scenario definitions power both modes.

---

## The Three-Layer Narrative Stack

```
┌─────────────────────────────────────────────────────────────────┐
│                    STORY TEMPLATES (SDK)                        │
│  Abstract narrative patterns from formal storytelling theory    │
│  • Tragedy, Comedy, Hero's Journey, Revenge Arc                 │
│  • 10-spectrum narrative state model (Life/Death, Honor, etc.)  │
│  • Genre conventions, beat structures, emotional arcs           │
│  "A character experiences loss, seeks meaning, finds purpose"   │
└───────────────────────────┬─────────────────────────────────────┘
                            │ implemented by
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│                      SCENARIOS (Plugin)                         │
│  Concrete game-world implementations of story patterns          │
│  • Triggering conditions (character state, world state)         │
│  • Required participants (archetypes, relationships)            │
│  • Situational setup (locations, items, NPCs to spawn)          │
│  • State mutations (trauma, goals, relationships, memories)     │
│  "Boy encounters monster in woods, develops fear and nightmares"│
└───────────────────────────┬─────────────────────────────────────┘
                            │ instantiated as
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│                    STORYLINE INSTANCES                          │
│  Active narrative arcs bound to specific characters             │
│  • Concrete characters filling archetype roles                  │
│  • Specific locations, items, NPCs                              │
│  • Progress tracking via Quest system                           │
│  "Aldric (age 8) encountered a Shadowwolf in Darkwood Forest"   │
└─────────────────────────────────────────────────────────────────┘
```

---

## What Makes Scenarios Special

### Scenarios Are Not Quests

| Aspect | Quest | Scenario |
|--------|-------|----------|
| **Focus** | Player objectives and rewards | Narrative events and character development |
| **Trigger** | Explicit acceptance | Conditions met (may be involuntary) |
| **Outcome** | Success/failure with rewards | State changes to characters and world |
| **Awareness** | Player knows they're "on a quest" | Character may not realize significance |
| **Scope** | Single objective chain | May spawn multiple quests, or none |

A scenario might:
- Spawn zero quests (pure backstory event)
- Spawn one quest (traditional quest trigger)
- Spawn multiple quests (complex narrative arc)
- Affect multiple characters differently (each gets different quests from same scenario)

### Scenarios Bridge Two Execution Modes

**Mode 1: Direct Trigger (Simple Games)**
```
Game Server → Check conditions → Trigger scenario → Generate quest
```
Traditional MMO style. Game server periodically checks which scenarios are available for which characters, triggers them when conditions match. Deterministic, designer-controlled.

**Mode 2: Watcher Discovery (Emergent Storytelling)**
```
Regional Watcher → Browse scenarios → Test conditions → Evaluate narrative fit → Trigger scenario
```
Arcadia style. Regional Watcher actors have preferences ("I like tragedy", "I favor underdogs"). They actively search for scenario opportunities, evaluate narrative potential, and choose which to trigger based on their own goals.

**Same scenario definitions power both modes.** A simple game uses Mode 1; Arcadia uses Mode 2. The Scenario plugin doesn't care which mode is calling it.

---

## Scenario Definition Structure

### Core Schema

```yaml
ScenarioDefinition:
  # Identity
  code: "MONSTER_ENCOUNTER_CHILDHOOD_TRAUMA"
  name: "Childhood Monster Encounter"
  description: "A young character encounters a dangerous creature, developing lasting fear"

  # Story Template Reference (from SDK)
  storyTemplate:
    code: "ORIGIN_TRAUMA"
    category: backstory
    archetypes: [innocent, monster]

  # Triggering Conditions
  conditions:
    characterRequirements:
      - archetype: innocent
        alias: victim
        constraints:
          age: { min: 5, max: 12 }
          hasTrauma: false
          personality:
            bravery: { max: 0.3 }  # Not already brave

    worldRequirements:
      - type: location
        alias: dangerous_place
        constraints:
          dangerLevel: { min: 3 }
          hasMonsters: true
          nearSettlement: true  # Close enough to escape

      - type: entity
        alias: monster
        constraints:
          threatLevel: { min: 2, max: 4 }  # Scary but survivable
          isActive: true

    situationalRequirements:
      - type: proximity
        entities: [victim, dangerous_place]
        distance: { max: 100 }  # Victim near dangerous area

      - type: time
        constraints:
          isolated: true  # No protectors nearby

  # What happens when scenario triggers
  execution:
    phases:
      - phase: setup
        actions:
          - type: spawn_encounter
            location: "{dangerous_place}"
            entities: ["{victim}", "{monster}"]

      - phase: event
        actions:
          - type: combat_or_flee
            attacker: "{monster}"
            defender: "{victim}"
            outcomes:
              injured_escape:
                probability: 0.7
                mutations: [injury, fear, nightmare]
              uninjured_escape:
                probability: 0.2
                mutations: [fear]
              rescued:
                probability: 0.1
                mutations: [gratitude, protector_bond]
                spawns: [rescuer_npc]

      - phase: aftermath
        actions:
          - type: apply_mutations
          - type: record_memory
            participants: ["{victim}"]
            memoryType: traumatic_encounter
            significance: high

  # State changes applied to characters
  mutations:
    injury:
      target: "{victim}"
      changes:
        - type: health
          operation: reduce
          amount: { min: 20, max: 40 }
        - type: add_condition
          condition: "wounded"
          duration: "P7D"

    fear:
      target: "{victim}"
      changes:
        - type: add_backstory
          element: FEAR
          content: "Terrified of {monster.species} after childhood encounter"
        - type: personality_shift
          trait: bravery
          direction: decrease
          intensity: moderate

    nightmare:
      target: "{victim}"
      changes:
        - type: add_backstory
          element: TRAUMA
          content: "Has recurring nightmares of {monster.species} at the window"
        - type: add_behavior_trigger
          trigger: "sleep"
          chance: 0.3
          effect: "nightmare_event"
          duration: "P1Y"  # Nightmares for a year

    gratitude:
      target: "{victim}"
      changes:
        - type: add_relationship
          toEntity: "{rescuer}"
          relationshipType: "PROTECTOR"
          sentiment: highly_positive

    protector_bond:
      target: "{rescuer}"
      changes:
        - type: add_relationship
          toEntity: "{victim}"
          relationshipType: "WARD"
          sentiment: protective

  # Optional: Quests that can spawn from this scenario
  questHooks:
    - condition: "outcome == injured_escape AND victim.age > 16"
      questTemplate: "OVERCOME_CHILDHOOD_FEAR"
      delay: "P5Y"  # Quest available 5 years later
      description: "Return to face the monster that scarred you"

    - condition: "outcome == rescued"
      questTemplate: "REPAY_PROTECTOR"
      delay: "P1Y"
      description: "Find a way to thank your rescuer"

  # Metadata
  metadata:
    tags: [childhood, trauma, monster, fear, backstory]
    frequency: common  # How often this can occur
    cooldownPerCharacter: "P10Y"  # Once per character per 10 years
    cooldownGlobal: null  # No global cooldown
    exclusivity: []  # No conflicting scenarios
```

### Another Example: Caravan Survivor

```yaml
ScenarioDefinition:
  code: "CARAVAN_ATTACK_ORPHAN"
  name: "Caravan Attack Survivor"
  description: "Character survives a caravan attack, loses family, becomes orphan"

  storyTemplate:
    code: "ORIGIN_LOSS"
    category: backstory
    archetypes: [survivor, family, attackers]

  conditions:
    characterRequirements:
      - archetype: survivor
        alias: orphan_to_be
        constraints:
          age: { min: 6, max: 16 }
          hasFamily: true
          familySize: { min: 1 }

      - archetype: family
        alias: doomed_family
        constraints:
          relationTo: "{orphan_to_be}"
          relationTypes: [PARENT, SIBLING, GUARDIAN]

    worldRequirements:
      - type: event
        alias: caravan_journey
        constraints:
          type: travel
          participants: ["{orphan_to_be}", "{doomed_family}"]
          routeDanger: { min: 2 }

      - type: faction
        alias: attackers
        constraints:
          hostile: true
          strength: { min: 3 }

    situationalRequirements:
      - type: vulnerability
        target: caravan_journey
        description: "Caravan is in vulnerable position"

  execution:
    phases:
      - phase: attack
        actions:
          - type: combat_event
            attackers: "{attackers}"
            defenders: ["{orphan_to_be}", "{doomed_family}"]
            outcome_override: attackers_win  # This scenario requires family death

      - phase: survival
        actions:
          - type: survival_check
            survivor: "{orphan_to_be}"
            methods: [hidden, fled, spared, presumed_dead]
            outcomes:
              hidden:
                description: "Hid among the dead/cargo"
                mutations: [trauma_witnessed, survival_guilt]
              fled:
                description: "Ran and escaped"
                mutations: [trauma_abandonment, survival_instinct]
              spared:
                description: "Attackers showed unexpected mercy"
                mutations: [trauma_witnessed, mysterious_mercy]
              presumed_dead:
                description: "Left for dead but survived"
                mutations: [trauma_physical, near_death_experience]

      - phase: aftermath
        actions:
          - type: kill_characters
            targets: "{doomed_family}"
            cause: "caravan_attack"

          - type: find_guardian
            orphan: "{orphan_to_be}"
            searchOrder:
              - extended_family
              - family_friends
              - faction_members
              - orphanage
              - streets
            result_alias: new_guardian

          - type: relocate
            character: "{orphan_to_be}"
            destination: "{new_guardian.location}"

  mutations:
    trauma_witnessed:
      target: "{orphan_to_be}"
      changes:
        - type: add_backstory
          element: TRAUMA
          content: "Witnessed family murdered in caravan attack"
        - type: personality_shift
          trait: trust
          direction: decrease
          intensity: severe

    survival_guilt:
      target: "{orphan_to_be}"
      changes:
        - type: add_backstory
          element: GUILT
          content: "Survived by hiding while family died"
        - type: add_goal
          goal: "Prove worthy of survival"

    survival_instinct:
      target: "{orphan_to_be}"
      changes:
        - type: personality_shift
          trait: self_preservation
          direction: increase
          intensity: moderate
        - type: add_backstory
          element: SHAME
          content: "Ran instead of fighting"

    mysterious_mercy:
      target: "{orphan_to_be}"
      changes:
        - type: add_backstory
          element: MYSTERY
          content: "Attackers spared me for unknown reason"
        - type: add_goal
          goal: "Discover why I was spared"

  questHooks:
    - condition: "survival_method == hidden OR survival_method == fled"
      questTemplate: "AVENGE_FAMILY"
      delay: "P3Y"
      description: "Hunt down the attackers who killed your family"

    - condition: "survival_method == spared"
      questTemplate: "DISCOVER_MERCY_REASON"
      delay: "P1Y"
      description: "Find out why the attackers let you live"

    - condition: "new_guardian.type == streets"
      questTemplate: "STREET_SURVIVAL"
      delay: "P0D"  # Immediate
      description: "Survive on the streets as an orphan"
```

---

## Scenario Categories

### By Trigger Timing

| Category | When | Examples |
|----------|------|----------|
| **Origin** | Character creation / early life | Monster encounter, caravan attack, found by mentor |
| **Milestone** | Level/age thresholds | Coming of age, first kill, mastery achieved |
| **Opportunity** | World state conditions | Discovered dungeon, met faction leader, found artifact |
| **Consequence** | Result of actions | Betrayal revealed, debt called, enemy returns |
| **Random** | Chance encounters | Traveler on road, storm shelter, mysterious stranger |

### By Scope

| Scope | Affects | Quest Generation |
|-------|---------|------------------|
| **Personal** | Single character | 0-1 quests, backstory mutations |
| **Interpersonal** | 2-3 characters | Relationship quests, rivalry/alliance |
| **Local** | Community/location | Community quests, local reputation |
| **Regional** | Faction/realm area | Faction quests, political involvement |
| **World** | Multiple realms | Epic quests, world-changing events |

### By Narrative Function

| Function | Purpose | Examples |
|----------|---------|----------|
| **Setup** | Establish character hooks | Trauma, training, relationships |
| **Complication** | Add obstacles | Enemy appears, ally betrays, resource lost |
| **Escalation** | Raise stakes | Threat grows, deadline approaches, cost increases |
| **Resolution** | Conclude arcs | Confrontation, reconciliation, transformation |
| **Transition** | Bridge narratives | New chapter, location change, time skip |

---

## Two Execution Modes in Detail

### Mode 1: Direct Trigger (Game Server)

For simpler games or deterministic content:

```csharp
// Game server periodic check
public async Task CheckScenarioTriggers(Guid characterId)
{
    var character = await _characterClient.GetAsync(characterId);
    var availableScenarios = await _scenarioClient.FindAvailableAsync(new FindAvailableRequest
    {
        CharacterId = characterId,
        Categories = [ScenarioCategory.Milestone, ScenarioCategory.Opportunity],
        MaxResults = 10
    });

    foreach (var scenario in availableScenarios)
    {
        if (ShouldTrigger(scenario, character))
        {
            await _scenarioClient.TriggerAsync(new TriggerScenarioRequest
            {
                ScenarioCode = scenario.Code,
                PrimaryCharacterId = characterId,
                ExecutionMode = ExecutionMode.Immediate
            });
            break; // One scenario per check
        }
    }
}

private bool ShouldTrigger(ScenarioAvailability scenario, Character character)
{
    // Simple deterministic logic
    // e.g., "Level 10 characters always get the guild invitation scenario"
    return scenario.TriggerProbability >= 1.0 ||
           _random.NextDouble() < scenario.TriggerProbability;
}
```

**Use Cases**:
- Traditional MMO quest hubs
- Level-gated content unlocks
- Tutorial sequences
- Guaranteed story beats

### Mode 2: Watcher Discovery (Regional Watcher)

For emergent storytelling:

```yaml
# Regional Watcher behavior document (ABML)
watcher_behavior:
  name: "Darkwood Forest Watcher"
  preferences:
    favored_story_types: [tragedy, survival, coming_of_age]
    favored_outcomes: [character_growth, relationship_formation]
    disfavored: [easy_victory, deus_ex_machina]

  periodic_scan:
    interval: "PT1H"  # Every hour
    actions:
      - scan_for_candidates:
          location: "{my_region}"
          criteria:
            has_narrative_potential: true
            not_in_active_storyline: true

      - for_each: candidate
        do:
          - evaluate_scenarios:
              character: "{candidate}"
              limit: 5

          - for_each: scenario
            do:
              - score_narrative_fit:
                  scenario: "{scenario}"
                  character: "{candidate}"
                  my_preferences: "{preferences}"

              - if: narrative_score > 0.7
                then:
                  - test_scenario:
                      scenario: "{scenario}"
                      character: "{candidate}"
                      dry_run: true

                  - if: test_result.dramatically_interesting
                    then:
                      - trigger_scenario:
                          scenario: "{scenario}"
                          character: "{candidate}"
                          orchestrator: "{self}"
```

**What the Watcher Does**:
1. **Scans** for characters with narrative potential (interesting backstory hooks, relationship tensions, unfulfilled goals)
2. **Browses** available scenarios that could apply to those characters
3. **Evaluates** narrative fit against its own preferences
4. **Tests** scenarios via Storyline API (dry run to see what would happen)
5. **Selects** the most dramatically interesting option
6. **Triggers** the chosen scenario, becoming the orchestrator

**Watcher Preferences Create Variety**:
- A watcher favoring "tragedy" will trigger different scenarios than one favoring "comedy"
- A watcher favoring "underdogs" will seek out weak characters for hero's journey arcs
- Different regions can have different watchers with different tastes
- Players experience different narrative styles in different areas

---

## Integration Points

### With Storyline Service

Storyline uses Scenario as its **building blocks**:

```
Storyline Plan:
├── Phase 1: Setup (Scenario: MYSTERIOUS_STRANGER_ARRIVES)
├── Phase 2: Complication (Scenario: STRANGER_REVEALS_CONNECTION)
├── Phase 3: Quest (Quest: INVESTIGATE_STRANGER_PAST)
├── Phase 4: Escalation (Scenario: ENEMY_追_STRANGER)
└── Phase 5: Resolution (Scenario: CONFRONTATION_CHOICE)
```

Storyline doesn't invent narrative beats - it **composes scenarios** into arcs.

### With Quest Service

Scenarios can spawn quests via `questHooks`:

```
Scenario Triggered
    ↓
State Mutations Applied (backstory, personality, relationships)
    ↓
Quest Hooks Evaluated
    ↓
Matching Quests Created (via Quest service)
    ↓
Character receives quest(s) based on scenario outcome
```

### With Character Services

Scenarios read from and write to character data:

| Read From | Write To |
|-----------|----------|
| Character age, location | Backstory elements |
| Personality traits | Personality shifts |
| Existing relationships | New relationships |
| Backstory (to avoid conflicts) | Trauma, goals, fears |
| Current conditions | New conditions |

### With Regional Watchers

Watchers are the **active agents** that discover and trigger scenarios:

```
Watcher (Actor with behavior document)
    ↓ calls
Scenario Service (find available, test, trigger)
    ↓ creates
Storyline Instance (tracks the unfolding narrative)
    ↓ spawns
Quest Instances (player-facing objectives)
```

---

## API Design

### Scenario Definition Management

```yaml
/scenario/definition/create:
  summary: Create a new scenario definition

/scenario/definition/get:
  summary: Get scenario by ID or code

/scenario/definition/list:
  summary: List scenarios with filtering (category, tags, story template)

/scenario/definition/update:
  summary: Update scenario (non-structural changes only)

/scenario/definition/deprecate:
  summary: Mark scenario as deprecated
```

### Scenario Discovery

```yaml
/scenario/find-available:
  summary: Find scenarios available for a character
  description: |
    Evaluates all scenario conditions against character and world state.
    Returns scenarios that could currently trigger, with match scores.

/scenario/test:
  summary: Dry-run a scenario trigger
  description: |
    Simulates triggering a scenario without actually executing it.
    Returns predicted outcomes, state changes, and quest hooks.
    Used by Watchers to evaluate narrative potential.

/scenario/evaluate-fit:
  summary: Score how well a scenario fits a character
  description: |
    Considers character backstory, personality, current goals, and
    existing narrative arcs. Returns narrative fit score and reasoning.
```

### Scenario Execution

```yaml
/scenario/trigger:
  summary: Trigger a scenario for a character
  description: |
    Executes the scenario, applying state mutations and spawning quests.
    Can specify execution mode (immediate vs phased) and orchestrator.

/scenario/get-active:
  summary: Get currently executing scenarios for a character

/scenario/get-history:
  summary: Get scenario history for a character
```

### Watcher Support

```yaml
/scenario/watcher/scan:
  summary: Scan a region for scenario candidates
  description: |
    Returns characters in region with narrative potential scores.
    Used by Regional Watchers to find storytelling opportunities.

/scenario/watcher/browse:
  summary: Browse scenarios matching watcher preferences
  description: |
    Filters scenarios by story type, outcome preferences, etc.
    Returns scenarios sorted by preference match score.
```

---

## State Storage

```yaml
# state-stores.yaml additions
ScenarioDefinition:
  backend: mysql
  description: Scenario definitions (templates)

ScenarioInstance:
  backend: mysql
  description: Active/completed scenario executions

ScenarioHistory:
  backend: mysql
  description: Character scenario history (for cooldown tracking)

ScenarioCandidateCache:
  backend: redis
  description: Cached scenario availability per character
  ttl: 300  # 5 minutes
```

---

## Relationship to Character Creation

Arcadia's "organic character creation" is **just origin scenarios**:

```
Spirit lands on object (staff, hammer, shovel, etc.)
    ↓
Object determines initial archetype hints
    ↓
Tutorial location determines available origin scenarios
    ↓
Origin scenarios trigger based on player choices
    ↓
Character emerges from accumulated scenario outcomes
```

There's no "character creation screen". There are just **origin scenarios** that fire during the tutorial period, shaping who the character becomes.

**Example Flow**:
1. Spirit lands on **shovel** → hints toward laborer/farmer archetype
2. Tutorial starts in **mining village** → mining-related scenarios available
3. Player explores mine → **CAVE_DISCOVERY** scenario triggers (found rare ore)
4. Player returns ore → **HONEST_LABORER** scenario triggers (reputation boost)
5. Foreman notices → **APPRENTICESHIP_OFFER** scenario triggers
6. Character is now "miner's apprentice" - not because they chose it, but because scenarios shaped them

---

## Configuration

```yaml
# scenario-configuration.yaml
ScenarioServiceConfiguration:
  type: object
  x-service-configuration:
    envPrefix: SCENARIO
  properties:
    CandidateCacheTtlSeconds:
      type: integer
      default: 300
      description: TTL for scenario availability cache
      env: CANDIDATE_CACHE_TTL

    MaxConcurrentScenariosPerCharacter:
      type: integer
      default: 3
      description: Maximum scenarios a character can be in simultaneously
      env: MAX_CONCURRENT

    DefaultCooldownDays:
      type: integer
      default: 30
      description: Default cooldown between scenario triggers for same character
      env: DEFAULT_COOLDOWN_DAYS

    EnableWatcherIntegration:
      type: boolean
      default: true
      description: Allow Regional Watchers to trigger scenarios
      env: WATCHER_ENABLED

    DryRunTimeoutMs:
      type: integer
      default: 5000
      description: Timeout for scenario test/dry-run evaluation
      env: DRY_RUN_TIMEOUT
```

---

## Event Flow

### Published Events

```yaml
scenario.triggered:
  scenarioCode: string
  scenarioInstanceId: uuid
  primaryCharacterId: uuid
  orchestratorId: uuid?  # Watcher that triggered, if any
  phase: string

scenario.phase.completed:
  scenarioInstanceId: uuid
  phase: string
  outcome: string
  mutations: [...]

scenario.completed:
  scenarioInstanceId: uuid
  scenarioCode: string
  finalOutcome: string
  questsSpawned: [uuid]
  mutationsApplied: [...]

scenario.available:
  characterId: uuid
  scenarioCode: string
  matchScore: float
  # Published when new scenarios become available (for UI hints)
```

### Consumed Events

```yaml
character.created:
  # Check for origin scenarios

character.level.changed:
  # Check for milestone scenarios

character.location.entered:
  # Check for location-triggered scenarios

character.relationship.formed:
  # Check for relationship-triggered scenarios

quest.completed:
  # Check for consequence scenarios
```

---

## Implementation Phases

### Phase 1: Core Framework
- Scenario definition CRUD
- Basic condition evaluation (character requirements)
- Simple trigger execution
- State mutation application

### Phase 2: Quest Integration
- Quest hook evaluation and spawning
- Scenario → Quest flow
- History tracking and cooldowns

### Phase 3: Watcher Integration
- find-available API with scoring
- test/dry-run API
- Watcher-specific browse API
- Orchestrator tracking

### Phase 4: Advanced Features
- Multi-character scenarios
- Phased execution (scenarios that unfold over time)
- Branching outcomes based on player actions during scenario
- Scenario composition (scenarios that trigger other scenarios)

---

## The Big Picture

```
┌─────────────────────────────────────────────────────────────────┐
│                   REGIONAL WATCHER (Actor)                      │
│  "I like tragedy. I favor underdogs. Let me find a story..."   │
└───────────────────────────┬─────────────────────────────────────┘
                            │ browses/tests/triggers
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│                      SCENARIO (Plugin)                          │
│  Concrete implementations of story patterns                     │
│  Conditions, participants, mutations, quest hooks               │
└───────────────────────────┬─────────────────────────────────────┘
                            │ implements patterns from
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│                   STORY TEMPLATES (SDK)                         │
│  Abstract narrative theory: tragedy, hero's journey, etc.       │
│  10-spectrum model, beat structures, emotional arcs             │
└─────────────────────────────────────────────────────────────────┘

Scenario execution:
┌─────────────────────────────────────────────────────────────────┐
│  SCENARIO TRIGGERED                                             │
│      ↓                                                          │
│  STATE MUTATIONS (backstory, personality, relationships)        │
│      ↓                                                          │
│  QUEST HOOKS EVALUATED                                          │
│      ↓                                                          │
│  QUESTS SPAWNED (via Quest service)                             │
│      ↓                                                          │
│  STORYLINE ADVANCED (Storyline service notified)                │
└─────────────────────────────────────────────────────────────────┘
```

---

## Why This Enables Both Simple and Complex Games

**Simple Game** (traditional MMO):
- Define scenarios for each level range
- Game server triggers scenarios when conditions met
- Players experience curated, designer-controlled narrative
- Zero AI, zero emergent behavior, fully predictable

**Complex Game** (Arcadia):
- Same scenario definitions
- Regional Watchers actively seek narrative opportunities
- Watchers have preferences that create regional flavor
- Scenarios compose into multi-phase storylines
- Players experience emergent, unpredictable narrative

**The scenario definitions are identical.** Only the triggering mechanism differs.

---

## Open Questions

1. **Scenario Versioning**: How do we handle scenario definition updates when instances are in progress?

2. **Multi-Character Coordination**: For scenarios involving multiple characters, how do we ensure all participants are available and willing?

3. **Player Agency**: Can players refuse scenarios? How does that affect Watcher planning?

4. **Scenario Conflicts**: How do we prevent incompatible scenarios from triggering simultaneously?

5. **Difficulty Scaling**: Should scenarios have difficulty variants, or should conditions handle this?

---

## References

- [Quest Plugin Architecture](QUEST_PLUGIN_ARCHITECTURE.md)
- [Storyline Composer](STORYLINE_COMPOSER.md)
- [Compression as Seed Data](COMPRESSION_AS_SEED_DATA.md)
- [ABML/GOAP Expansion Opportunities](ABML_GOAP_EXPANSION_OPPORTUNITIES.md)
- [Actor Data Access Patterns](ACTOR_DATA_ACCESS_PATTERNS.md)

---

*This document defines the architectural vision for lib-scenario. The plugin enables both traditional quest-hub gameplay and emergent AI-driven storytelling using the same scenario definitions.*
