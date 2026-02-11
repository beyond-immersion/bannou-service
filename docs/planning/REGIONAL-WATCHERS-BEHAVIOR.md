# Regional Watchers and Storyline Orchestration

> **Status**: Design / Partial Implementation
> **Priority**: High
> **Last Updated**: 2026-02-05
> **Related**:
> - `docs/planning/SCENARIO_PLUGIN_ARCHITECTURE.md`
> - `docs/planning/QUEST_PLUGIN_ARCHITECTURE.md`
> - `docs/planning/STORYLINE_COMPOSER.md`
> - `docs/guides/STORY_SYSTEM.md`

## Overview

Regional Watchers are long-running **Event Brain actors** that monitor event streams and orchestrate narratives within their domain. In Arcadia, these are the **gods** - divine entities that oversee specific aspects of the game world.

This document describes:
1. **God Pattern** - Domain-specific watchers that spawn Event Agents and orchestrate storylines
2. **Direct Coordinator Pattern** - Simple Event Agents spawned for specific encounters
3. **Storyline Orchestration** - How watchers discover and trigger narrative scenarios

---

## Critical Architecture Clarification

### The Actor Does the Searching, Not the Plugins

A common misconception: "The Scenario plugin scans for characters that meet conditions."

**This is wrong.**

The correct architecture:

```
┌─────────────────────────────────────────────────────────────────┐
│                    REGIONAL WATCHER (Actor)                     │
│                                                                 │
│  The actor's BEHAVIOR defines what it finds "interesting":      │
│  • Receives events live in its region                           │
│  • "Navigates around" looking for characters/situations         │
│  • Decides what to investigate based on its preferences         │
│  • Queries plugins with specific characters/conditions          │
│                                                                 │
│  The actor is ACTIVE - it searches, evaluates, decides          │
└───────────────────────────┬─────────────────────────────────────┘
                            │ queries
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│                    SCENARIO PLUGIN (Passive)                    │
│                                                                 │
│  The plugin is a PASSIVE data store with matching APIs:         │
│  • "Here are scenarios I'm interested in" → returns list        │
│  • "Here's a character + world state" → returns matching        │
│  • "Test this scenario with these inputs" → returns outcome     │
│                                                                 │
│  The plugin does NOT scan, search, or make decisions            │
└─────────────────────────────────────────────────────────────────┘
```

**Why this matters:**
- The "soul" of the watcher is in its **behavior document**, not in plugin code
- Different watchers can have radically different search strategies using the same plugins
- Behavior documents are game-specific content; plugins are reusable infrastructure
- This keeps plugins simple and behaviors expressive

---

## Two Interaction Patterns with Scenario Plugin

### Pattern A: Scenarios-First (Guided Search)

The watcher asks: *"What scenarios interest me?"* then searches for matching characters.

```yaml
# Watcher behavior: Scenarios-First approach
flows:
  discover_narrative_opportunities:
    # 1. Get scenarios I care about
    - api_call:
        service: storyline  # or scenario, if separate
        endpoint: /scenario/list
        data:
          tags: "${my_preferred_tags}"  # [tragedy, coming_of_age, revenge]
          categories: [opportunity, consequence]
        result: available_scenarios

    # 2. For each scenario, understand what characters I need
    - foreach:
        collection: "${available_scenarios}"
        as: scenario
        do:
          # Extract character requirements from scenario
          - set:
              search_criteria: "${scenario.conditions.characterRequirements}"

          # Search my region for matching characters
          - call: search_region_for_candidates
            with:
              criteria: "${search_criteria}"
              limit: 10

          # If found candidates, evaluate further
          - cond:
              - when: "${candidates.length > 0}"
                then:
                  - call: evaluate_scenario_fit
                    with:
                      scenario: "${scenario}"
                      candidates: "${candidates}"

  search_region_for_candidates:
    # Actor does the searching via its own perception/queries
    - api_call:
        service: character
        endpoint: /character/query
        data:
          realmId: "${my_realm}"
          locationIds: "${my_region_locations}"
          filters: "${criteria}"
        result: candidates
```

**When to use**: When the watcher has specific narrative preferences and wants to find characters to fit those stories.

### Pattern B: Characters-First (Opportunistic Discovery)

The watcher asks: *"I found interesting characters - what scenarios fit them?"*

```yaml
# Watcher behavior: Characters-First approach
flows:
  periodic_scan:
    # 1. Look for "interesting" characters based on my criteria
    - call: find_interesting_characters

    # 2. For each interesting character, query for matching scenarios
    - foreach:
        collection: "${interesting_characters}"
        as: character
        do:
          # Ask scenario plugin: what fits this character?
          - api_call:
              service: storyline
              endpoint: /scenario/find-matching
              data:
                characterId: "${character.id}"
                worldState:
                  realmId: "${my_realm}"
                  activeEvents: "${current_events}"
                  seasonOrTime: "${world_time}"
                preferredTags: "${my_preferred_tags}"
              result: matching_scenarios

          # Evaluate and maybe trigger
          - cond:
              - when: "${matching_scenarios.length > 0}"
                then:
                  - call: evaluate_and_maybe_trigger
                    with:
                      character: "${character}"
                      scenarios: "${matching_scenarios}"

  find_interesting_characters:
    # Actor-specific logic for "interesting"
    # This is where the behavior's personality shines
    - api_call:
        service: character
        endpoint: /character/query
        data:
          realmId: "${my_realm}"
          filters:
            # Example: tragedy-loving watcher looks for vulnerable characters
            hasUnresolvedTrauma: false  # Not already traumatized
            hasFamily: true             # Has someone to lose
            age:
              min: 8
              max: 25                   # Young enough for coming-of-age
        result: potential_characters

    # Score by "narrative potential" (actor's own judgment)
    - foreach:
        collection: "${potential_characters}"
        as: char
        do:
          - set:
              "char.narrative_score": >
                ${calculate_narrative_potential(char, my_preferences)}

    # Return top candidates
    - set:
        interesting_characters: >
          ${potential_characters.filter(c => c.narrative_score > 0.6).slice(0, 5)}
```

**When to use**: When the watcher is more exploratory, looking for opportunities in whoever is around.

### Pattern C: Event-Triggered (Reactive)

The watcher asks: *"This just happened - any scenarios triggered?"*

```yaml
# Watcher behavior: React to events
subscriptions:
  - "character.{my_realm}.died"
  - "character.{my_realm}.relationship.formed"
  - "combat.{my_realm}.significant"
  - "location.{my_realm}.discovered"

flows:
  on_event:
    # Event arrived - check for scenario triggers
    - api_call:
        service: storyline
        endpoint: /scenario/check-triggers
        data:
          eventType: "${event.type}"
          eventData: "${event}"
          realmId: "${my_realm}"
          locationId: "${event.location}"
        result: triggered_scenarios

    # Auto-trigger scenarios that match
    - foreach:
        collection: "${triggered_scenarios}"
        as: scenario
        do:
          - cond:
              - when: "${should_i_trigger(scenario, event)}"
                then:
                  - call: trigger_scenario
                    with:
                      scenario: "${scenario}"
                      triggeringEvent: "${event}"
```

**When to use**: For consequence scenarios that naturally follow from events (death → inheritance scenario, betrayal → revenge scenario).

---

## Scenario Plugin API (Passive Interface)

Based on the above patterns, the Scenario plugin needs these APIs:

```yaml
# What the Scenario plugin provides (passive queries only)

/scenario/list:
  summary: List scenario definitions with filtering
  description: |
    Returns scenarios matching criteria. Does NOT scan for characters.
    Watcher uses this to know what scenarios exist.
  input:
    tags: [string]           # Filter by tags
    categories: [string]     # Filter by category
    storyTemplates: [string] # Filter by story template code
  output:
    scenarios: [ScenarioSummary]

/scenario/find-matching:
  summary: Find scenarios that match given character + world state
  description: |
    Given a specific character and world state, returns scenarios
    whose conditions are satisfied. The CALLER provides the character,
    not the plugin searching for characters.
  input:
    characterId: uuid
    worldState:
      realmId: uuid
      locationId: uuid?
      activeEvents: [string]?
    preferredTags: [string]?
  output:
    matchingScenarios: [ScenarioMatch]
      # Each includes: scenario, matchScore, satisfiedConditions

/scenario/check-triggers:
  summary: Check if an event triggers any scenarios
  description: |
    Given an event that just occurred, returns scenarios that
    should trigger as a consequence.
  input:
    eventType: string
    eventData: object
    realmId: uuid
    locationId: uuid?
  output:
    triggeredScenarios: [ScenarioTrigger]

/scenario/test:
  summary: Dry-run a scenario with specific inputs
  description: |
    Simulates triggering a scenario without executing it.
    Returns predicted outcomes, mutations, quest hooks.
    Watcher uses this to evaluate narrative potential.
  input:
    scenarioCode: string
    primaryCharacterId: uuid
    additionalContext: object?
  output:
    predictedOutcome: object
    mutations: [Mutation]
    questHooks: [QuestHook]
    narrativeScore: float

/scenario/trigger:
  summary: Actually trigger a scenario
  description: |
    Executes the scenario, applying mutations and spawning quests.
    Watcher calls this after deciding to proceed.
  input:
    scenarioCode: string
    primaryCharacterId: uuid
    orchestratorId: uuid?  # The watcher that triggered
    additionalContext: object?
  output:
    scenarioInstanceId: uuid
    appliedMutations: [Mutation]
    spawnedQuests: [uuid]
```

**Key insight**: All these APIs take **explicit inputs**. The plugin never scans or searches on its own.

---

## Should Scenario Be Part of Storyline?

Given how passive the Scenario plugin is, it might make sense to merge it into Storyline:

**Arguments for separate:**
- Scenarios can be used without Storyline (Simple Mode - game server triggers directly)
- Separation of concerns: Scenario = definitions, Storyline = composition
- Different teams might own different parts

**Arguments for merged:**
- Very thin API surface for Scenario alone
- Storyline already needs all Scenario data
- Reduces service-to-service calls

**Recommendation**: Start with **Scenario as part of Storyline plugin**, with clear internal separation. Can split later if needed. The Storyline plugin provides:
- Scenario definition storage and queries
- Storyline composition from scenarios
- Archive mining and GOAP planning

---

## Pattern 1: Gods as Regional Watchers

### Core Concept

In Arcadia, "gods" are the managers of the game world. Each god has:
- **A domain of responsibility** (death, forest, monsters, war, commerce, etc.)
- **Event stream subscriptions** matching their domain
- **Narrative preferences** (what kinds of stories they like to tell)
- **Capabilities** specific to their role
- **Realms of influence** where they operate

Gods are **long-running Event Brain actors** that:
- Start lazily when a realm they influence becomes active
- Listen to event streams for domain-relevant events
- Search for narrative opportunities using Patterns A/B/C above
- Spawn Event Agents for specific situations
- Trigger scenarios and compose storylines

### Example: God of Tragedy (Storyline-Focused)

```yaml
metadata:
  id: god-of-tragedy
  type: event_brain
  description: "Seeks out opportunities for dramatic tragedy and loss"

config:
  domain: "tragedy"
  realms_of_influence: ["all"]  # Tragedy can strike anywhere

  # What events might signal tragedy opportunities
  subscriptions:
    - "character.*.died"           # Death is often tragic
    - "character.*.relationship.formed"  # Love creates vulnerability
    - "character.*.prosperity.gained"    # Success precedes fall
    - "combat.*.significant"       # Battle creates loss

  # Narrative preferences (what stories I like)
  preferences:
    favored_story_templates:
      - "FALL_FROM_GRACE"
      - "LOSS_OF_INNOCENCE"
      - "PYRRHIC_VICTORY"
      - "DOOMED_LOVE"
    favored_outcomes:
      - character_growth_through_suffering
      - meaningful_sacrifice
      - bittersweet_resolution
    disfavored:
      - easy_escape
      - deus_ex_machina
      - pointless_death

  # Capabilities
  capabilities:
    - trigger_scenario
    - compose_storyline
    - spawn_event_agent

# Variables updated by cognition
variables:
  watched_characters: []     # Characters I'm tracking for opportunities
  active_storylines: []      # Storylines I'm orchestrating
  recent_tragedies: []       # Avoid clustering tragedies

flows:
  main:
    # Periodic scan for opportunities (Pattern A)
    - schedule:
        interval: "PT30M"  # Every 30 minutes
        call: scan_for_tragedy_opportunities

    # React to events (Pattern C)
    - on_event:
        call: evaluate_event_for_tragedy

  scan_for_tragedy_opportunities:
    # Get scenarios I care about
    - api_call:
        service: storyline
        endpoint: /scenario/list
        data:
          storyTemplates: "${preferences.favored_story_templates}"
        result: tragedy_scenarios

    # For each, search for fitting characters
    - foreach:
        collection: "${tragedy_scenarios}"
        as: scenario
        do:
          - call: find_candidates_for_scenario
            with:
              scenario: "${scenario}"

  find_candidates_for_scenario:
    # Extract what kind of character this scenario needs
    - set:
        needs: "${scenario.conditions.characterRequirements[0]}"

    # Search my realms
    - api_call:
        service: character
        endpoint: /character/query
        data:
          realmIds: "${my_realms}"
          filters:
            # Tragedy needs someone with something to lose
            hasRelationships: true
            notInActiveStoryline: true
        result: potential_victims

    # Score each by tragic potential
    - foreach:
        collection: "${potential_victims}"
        as: candidate
        do:
          - set:
              "candidate.tragic_potential": >
                ${score_tragic_potential(candidate)}

    # Test top candidates against scenario
    - set:
        top_candidates: >
          ${potential_victims.sort(c => -c.tragic_potential).slice(0, 3)}

    - foreach:
        collection: "${top_candidates}"
        as: candidate
        do:
          - api_call:
              service: storyline
              endpoint: /scenario/test
              data:
                scenarioCode: "${scenario.code}"
                primaryCharacterId: "${candidate.id}"
              result: test_result

          - cond:
              - when: "${test_result.narrativeScore > 0.7}"
                then:
                  - call: consider_triggering
                    with:
                      scenario: "${scenario}"
                      character: "${candidate}"
                      testResult: "${test_result}"

  consider_triggering:
    # Final checks before triggering
    - cond:
        # Don't cluster tragedies
        - when: "${recently_had_tragedy_near(character.location)}"
          then:
            - log: "Skipping - recent tragedy nearby"
            - return

        # Character hasn't been targeted recently
        - when: "${character.id in recent_targets}"
          then:
            - log: "Skipping - character recently targeted"
            - return

    # All checks passed - trigger!
    - api_call:
        service: storyline
        endpoint: /scenario/trigger
        data:
          scenarioCode: "${scenario.code}"
          primaryCharacterId: "${character.id}"
          orchestratorId: "${self.actor_id}"
        result: instance

    # Track it
    - set:
        active_storylines: "${active_storylines.concat([instance])}"
        recent_targets: "${recent_targets.concat([character.id])}"

  evaluate_event_for_tragedy:
    # An event happened - does it create tragedy opportunity?
    - switch:
        value: "${event.type}"
        cases:
          "character.relationship.formed":
            # New love = potential doomed love scenario
            - call: check_doomed_love_opportunity
          "character.prosperity.gained":
            # Success = potential fall from grace
            - call: check_fall_from_grace_opportunity
          "character.died":
            # Death = potential survivor's guilt / vengeance
            - call: check_death_consequences

  # Domain-specific helper: score tragic potential
  score_tragic_potential:
    # Characters with more to lose are more tragic
    - set:
        score: 0.0

    # Has loving relationships?
    - cond:
        - when: "${character.relationships.filter(r => r.sentiment > 0.5).length > 0}"
          then:
            - set:
                score: "${score + 0.3}"

    # Has achieved something?
    - cond:
        - when: "${character.backstory.achievements.length > 0}"
          then:
            - set:
                score: "${score + 0.2}"

    # Is young (more life to lose)?
    - cond:
        - when: "${character.age < 30}"
          then:
            - set:
                score: "${score + 0.2}"

    # Has hopes/dreams?
    - cond:
        - when: "${character.backstory.goals.length > 0}"
          then:
            - set:
                score: "${score + 0.3}"

    - return: "${score}"
```

### Example: God of Monsters (Spawning-Focused)

```yaml
metadata:
  id: god-of-monsters
  type: event_brain
  description: "Controls monster spawning and monster-related events"

config:
  domain: "monsters"

  subscriptions:
    - "region.*.population_low"    # Monster population dropped
    - "player.*.entered_zone"      # Players entering zones
    - "monster.*.killed"           # Monster deaths
    - "ecology.*.imbalanced"       # Ecosystem changes

  capabilities:
    - spawn_monsters
    - create_monster_event
    - evolve_monster
    - trigger_scenario

  # Monster god also likes certain storylines
  preferences:
    favored_story_templates:
      - "MONSTER_ENCOUNTER_TRAUMA"
      - "BEAST_HUNT"
      - "ECOLOGICAL_DISASTER"

flows:
  handle_population_low:
    # Region needs more monsters - basic spawning
    - set:
        spawn_count: "${calculate_spawn_needs(event.region, event.current_population)}"

    - api_call:
        service: game-server
        endpoint: /monsters/spawn-batch
        data:
          region_id: "${event.region.id}"
          count: "${spawn_count}"
          tier: "${event.region.difficulty_tier}"

  handle_player_entered_zone:
    # Player entered - opportunity for monster encounter scenario?
    - cond:
        # Only for certain players (not high-level veterans)
        - when: "${event.player.level < 15 && !event.player.hasMonsterEncounterTrauma}"
          then:
            # Check if monster encounter scenario fits
            - api_call:
                service: storyline
                endpoint: /scenario/find-matching
                data:
                  characterId: "${event.player.characterId}"
                  worldState:
                    realmId: "${event.realm}"
                    locationId: "${event.zone.id}"
                  preferredTags: ["monster", "encounter", "fear"]
                result: matching_scenarios

            - cond:
                - when: "${matching_scenarios.length > 0}"
                  then:
                    # Roll for scenario trigger
                    - cond:
                        - when: "${random() < 0.1}"  # 10% chance
                          then:
                            - call: trigger_monster_encounter
                              with:
                                scenario: "${matching_scenarios[0]}"
                                character: "${event.player.characterId}"
                                zone: "${event.zone}"
```

### God Lifecycle

```
Realm Activation
       │
       ▼
┌──────────────────────────────────────────────────────────────────┐
│  God Actor Started (if has influence in realm)                   │
│                                                                  │
│  1. Subscribe to domain event streams                            │
│  2. Load my narrative preferences                                │
│  3. Query available scenarios I care about (cache locally)       │
│  4. Enter main processing loop                                   │
└──────────────────────────────────────────────────────────────────┘
       │
       ▼
┌──────────────────────────────────────────────────────────────────┐
│  Event Processing Loop (long-running)                            │
│                                                                  │
│  REACTIVE: Receive domain events → evaluate for opportunities    │
│  PROACTIVE: Periodic scans → search for narrative candidates     │
│  ORCHESTRATE: Track active storylines → advance phases           │
└──────────────────────────────────────────────────────────────────┘
       │
       ▼
Realm Deactivation → God Actor Stopped (state persisted)
```

### Domain-Specific Filtering (Not Generic "Interestingness")

Previous designs emphasized generic "interestingness scoring." This is the wrong abstraction.

**Wrong approach:**
```yaml
# Too generic - what makes something "interesting"?
interestingness:
  power_proximity: 0.3
  antagonism: 0.4
  spawn_threshold: 0.7
```

**Right approach:**
```yaml
# Domain-specific - each god knows what THEY care about
god_of_tragedy:
  cares_about:
    - characters with something to lose
    - new relationships (vulnerability)
    - recent success (pride before fall)
  scores_by:
    - number of loving relationships
    - unfulfilled dreams/goals
    - youth (more life to lose)

god_of_war:
  cares_about:
    - battles (especially large scale)
    - duels (honor combat)
    - military movements
  scores_by:
    - combat skill of participants
    - stakes involved
    - historical significance

god_of_commerce:
  cares_about:
    - major transactions
    - market disruptions
    - trade route events
  scores_by:
    - economic impact
    - parties involved (powerful merchants)
    - rarity of goods
```

Each god's "interest filter" is defined in its **behavior document**, not in plugins.

---

## Pattern 2: Direct Coordinator (Simple Case)

### Core Concept

For many situations, no Regional Watcher/God is needed. The Event Agent is spawned directly with known parameters:

- **Known participants** from the start
- **Clear role** (coordinate this specific thing)
- **Simple lifecycle** (start → do job → end)
- **No spawning of sub-agents** needed

### Example: Monster Arena Fight Coordinator

See `docs/planning/CASE_STUDY_-_MONSTER_ARENA.md` for full details.

```yaml
metadata:
  id: arena-fight-coordinator
  type: event_brain
  description: "Coordinates a single arena fight between two participants"

# Spawned with known context - no guesswork
context:
  variables:
    fighter_a:
      source: "${spawn_context.fighter_a}"
    fighter_b:
      source: "${spawn_context.fighter_b}"
    arena_id:
      source: "${spawn_context.arena_id}"

flows:
  main:
    # Already know exactly what to do
    - call: initialize_fight
    - call: coordinate_rounds
    - call: declare_winner
    - call: cleanup
```

**Spawning is explicit:**
```csharp
// Game server code when arena fight starts
await _actorClient.StartActorAsync(new StartActorRequest
{
    TemplateId = "arena-fight-coordinator",
    ActorId = $"arena-fight-{fightId}",
    InitialContext = new Dictionary<string, object?>
    {
        ["fighter_a"] = fighterAId,
        ["fighter_b"] = fighterBId,
        ["arena_id"] = arenaId
    }
});
```

### When to Use Each Pattern

| Scenario | Pattern | Why |
|----------|---------|-----|
| Monster spawning | God Pattern | Continuous responsibility, realm-wide |
| Arena fight | Direct Coordinator | Known participants, clear lifecycle |
| Death events | God Pattern | Domain-wide monitoring needed |
| Cutscene | Direct Coordinator | Triggered by game, known participants |
| Quest boss encounter | Either | Could be god-triggered or game-triggered |
| World event (festival) | God Pattern | Scheduled, spawns many sub-events |
| Duel between players | Direct Coordinator | Explicit trigger, two participants |
| Storyline orchestration | God Pattern | Proactive search, multi-phase tracking |
| Scenario consequence | Either | Could be god-reactive or game-triggered |

---

## Storyline Orchestration Flow

### How a Watcher Composes a Storyline

When a watcher finds a good scenario opportunity, it may compose a multi-phase storyline:

```yaml
flows:
  compose_storyline_from_scenario:
    # 1. Trigger the initial scenario
    - api_call:
        service: storyline
        endpoint: /scenario/trigger
        data:
          scenarioCode: "${scenario.code}"
          primaryCharacterId: "${character.id}"
          orchestratorId: "${self.actor_id}"
        result: initial_instance

    # 2. Create a storyline tracking this character's arc
    - api_call:
        service: storyline
        endpoint: /storyline/create
        data:
          characterId: "${character.id}"
          storyTemplateCode: "${scenario.storyTemplate}"
          initialScenarioId: "${initial_instance.id}"
          orchestratorId: "${self.actor_id}"
        result: storyline

    # 3. Track this storyline
    - set:
        active_storylines: "${active_storylines.concat([storyline])}"

    # 4. Subscribe to storyline events
    - subscribe:
        topic: "storyline.${storyline.id}.phase.completed"
        handler: on_storyline_phase_completed

  on_storyline_phase_completed:
    # A phase completed - decide next phase
    - api_call:
        service: storyline
        endpoint: /storyline/get-next-phase-options
        data:
          storylineId: "${event.storylineId}"
        result: options

    # Evaluate options against my preferences
    - set:
        best_option: "${select_best_option(options, my_preferences)}"

    # Could be a scenario or a quest
    - switch:
        value: "${best_option.type}"
        cases:
          "scenario":
            - api_call:
                service: storyline
                endpoint: /storyline/advance-with-scenario
                data:
                  storylineId: "${event.storylineId}"
                  scenarioCode: "${best_option.scenarioCode}"
          "quest":
            - api_call:
                service: storyline
                endpoint: /storyline/advance-with-quest
                data:
                  storylineId: "${event.storylineId}"
                  questDefinitionCode: "${best_option.questCode}"
          "wait":
            # Let the storyline pause until conditions change
            - log: "Storyline ${event.storylineId} waiting for conditions"
```

### The Compression Feedback Loop

Watchers can also mine compressed archives for storyline seeds:

```yaml
flows:
  mine_archives_for_stories:
    # Periodically check for compressed characters with story potential
    - api_call:
        service: resource
        endpoint: /resource/query-archives
        data:
          resourceType: "character"
          realmId: "${my_realm}"
          hasNarrativePotential: true
          limit: 10
        result: archives

    - foreach:
        collection: "${archives}"
        as: archive
        do:
          # Ask storyline service to compose from this archive
          - api_call:
              service: storyline
              endpoint: /storyline/compose-from-archive
              data:
                archiveId: "${archive.id}"
                goals: "${my_preferred_outcomes}"
                excludeTemplates: "${recently_used_templates}"
              result: composition

          # If good composition found, consider instantiating
          - cond:
              - when: "${composition.narrativeScore > 0.7}"
                then:
                  - call: evaluate_archive_storyline
                    with:
                      archive: "${archive}"
                      composition: "${composition}"

  evaluate_archive_storyline:
    # Does this dead character's story fit current world state?
    # Can we find living characters to participate?

    - api_call:
        service: storyline
        endpoint: /storyline/find-participants
        data:
          compositionId: "${composition.id}"
          realmId: "${my_realm}"
          requiredRoles: "${composition.requiredRoles}"
        result: potential_participants

    - cond:
        - when: "${potential_participants.allRolesFilled}"
          then:
            # We can instantiate this storyline!
            - api_call:
                service: storyline
                endpoint: /storyline/instantiate
                data:
                  compositionId: "${composition.id}"
                  participants: "${potential_participants.assignments}"
                  orchestratorId: "${self.actor_id}"
```

---

## Implementation Considerations

### God Actor Startup

Gods start when realms they influence become active:

```csharp
// When realm activates
public async Task OnRealmActivatedAsync(Guid realmId)
{
    // Find gods with influence in this realm
    var gods = await _godRegistry.GetGodsForRealmAsync(realmId);

    foreach (var god in gods)
    {
        // Start god actor if not already running
        await _actorService.EnsureActorRunningAsync(
            templateId: god.TemplateId,
            actorId: $"god-{god.Id}-realm-{realmId}");
    }
}
```

### Event Subscriptions

Gods subscribe to domain-specific topics:

```csharp
// In god actor initialization
await _messageSubscriber.SubscribeAsync<CharacterDiedEvent>(
    $"character.{realmId}.died",
    async (evt, ct) => await OnCharacterDiedAsync(evt, ct));
```

### Capability Execution

Gods execute capabilities via existing infrastructure:

```csharp
// Trigger scenario
await _storylineClient.TriggerScenarioAsync(new TriggerScenarioRequest
{
    ScenarioCode = scenarioCode,
    PrimaryCharacterId = characterId,
    OrchestratorId = actorId
});

// Query characters
await _characterClient.QueryAsync(new CharacterQueryRequest
{
    RealmId = realmId,
    Filters = filters
});

// Spawn event agent
await _actorService.StartActorAsync(eventAgentTemplate, context);
```

---

## Arcadia God Registry (Design)

```yaml
# Configuration for Arcadia's pantheon
gods:
  moira:  # Fate
    id: god-of-fate
    template: god-tragedy-watcher
    domains: [tragedy, fate, destiny, loss]
    realms: [all]
    preferences:
      favored_templates: [FALL_FROM_GRACE, DOOMED_LOVE, PYRRHIC_VICTORY]

  thanatos:  # Death
    id: god-of-death
    template: god-death-watcher
    domains: [death, undeath, afterlife]
    realms: [underworld, graveyards, battlefields, temples_of_death]
    preferences:
      favored_templates: [GHOST_VENGEANCE, RESURRECTION_COST, LAST_WORDS]

  silvanus:  # Forest
    id: god-of-forest
    template: god-forest-watcher
    domains: [forest, nature, wildlife, growth]
    realms: [ancient_forest, druid_groves, wilderness]
    preferences:
      favored_templates: [BEAST_BOND, ECOLOGICAL_BALANCE, NATURE_CORRUPTION]

  ares:  # War
    id: god-of-war
    template: god-war-watcher
    domains: [war, combat, military, conquest]
    realms: [battlefields, military_camps, arenas, war_torn_regions]
    preferences:
      favored_templates: [HERO_FORGED, BITTER_RIVALRY, PYRRHIC_VICTORY]

  typhon:  # Monsters
    id: god-of-monsters
    template: god-monster-watcher
    domains: [monsters, beasts, spawning]
    realms: [all]
    preferences:
      favored_templates: [MONSTER_ENCOUNTER_TRAUMA, BEAST_HUNT, TAMING]

  hermes:  # Commerce
    id: god-of-commerce
    template: god-commerce-watcher
    domains: [trade, commerce, travel, messages]
    realms: [markets, trade_routes, cities]
    preferences:
      favored_templates: [RAGS_TO_RICHES, MERCHANT_RIVALRY, TRADE_ROUTE_DANGER]
```

---

## Relationship to Existing Infrastructure

| Existing Component | Role in Watcher Pattern |
|-------------------|-------------------------|
| `ActorRunner` | Runs god actors (long-running Event Brains) |
| `IMessageBus` | Gods subscribe to domain event streams |
| Event Brain handlers | Gods use `emit_perception`, `schedule_event`, etc. |
| `ActorService.StartActorAsync` | Gods spawn Event Agents |
| lib-mesh | Gods call service APIs (character, storyline, etc.) |
| Storyline plugin | Gods query scenarios, trigger storylines |
| lib-resource | Gods access compressed archives |

**No new infrastructure needed** - gods are Event Brain actors using existing capabilities with storyline integration.

---

## Behavior Templates vs Game-Specific Behaviors

### What We Provide (Templates)

Generic patterns that games can customize:

```yaml
# templates/regional-watcher-base.yaml
# Base template with common infrastructure
metadata:
  id: regional-watcher-base
  type: event_brain
  abstract: true  # Can't be instantiated directly

config:
  # Common subscriptions pattern
  subscriptions: []  # Override in concrete template

  # Common preferences structure
  preferences:
    favored_story_templates: []
    favored_outcomes: []
    disfavored: []

flows:
  # Common scan loop
  periodic_scan:
    - schedule:
        interval: "${config.scan_interval || 'PT30M'}"
        call: scan_for_opportunities

  # Abstract - must be implemented
  scan_for_opportunities:
    - abstract: true

  # Common storyline tracking
  track_storyline:
    - set:
        active_storylines: "${active_storylines.concat([storyline])}"

  untrack_storyline:
    - set:
        active_storylines: "${active_storylines.filter(s => s.id != storyline.id)}"
```

### What Games Provide (Implementations)

Game-specific behaviors with actual preferences and logic:

```yaml
# arcadia/behaviors/god-of-tragedy.yaml
# Arcadia-specific tragedy watcher
extends: regional-watcher-base

metadata:
  id: arcadia-god-of-tragedy
  description: "Moira, the Fate-Weaver of Arcadia"

config:
  scan_interval: "PT20M"

  subscriptions:
    - "character.arcadia.*.relationship.formed"
    - "character.arcadia.*.prosperity.gained"
    - "character.arcadia.*.achievement.unlocked"

  preferences:
    favored_story_templates:
      - "FALL_FROM_GRACE"
      - "DOOMED_LOVE"
      - "LOSS_OF_INNOCENCE"
    favored_outcomes:
      - character_growth_through_suffering
      - meaningful_sacrifice
    disfavored:
      - easy_escape
      - consequence_free_choices

flows:
  scan_for_opportunities:
    # Arcadia-specific: look for characters at peak happiness
    # (because tragedy hits hardest when you have something to lose)
    - call: find_happy_characters
    - call: evaluate_for_downfall

  find_happy_characters:
    # Arcadia-specific happiness scoring
    - api_call:
        service: character
        endpoint: /character/query
        data:
          realmId: "arcadia"
          filters:
            hasPositiveRelationships: true
            recentAchievements: true
            notInActiveStoryline: true
        result: candidates

    # Arcadia-specific scoring logic
    - foreach:
        collection: "${candidates}"
        as: char
        do:
          - set:
              "char.happiness_score": >
                ${0.3 * char.relationships.positiveCount +
                  0.3 * char.recentAchievements.length +
                  0.2 * (char.wealth > realm.averageWealth ? 1 : 0) +
                  0.2 * (char.hasLivingFamily ? 1 : 0)}
```

**Key insight**: We provide the **skeleton** and **patterns**. Games provide the **soul** - the specific preferences, scoring logic, and narrative taste that make their world unique.

---

## Next Steps

1. **Merge Scenario into Storyline** - Keep the APIs but as part of one plugin
2. **Define Variable Providers** for watcher behaviors to access storyline state
3. **Create base templates** for regional watchers
4. **Implement storyline orchestration flows** in Storyline plugin
5. **Test with simple watcher** (Monster God) before complex (Tragedy God)

---

## Open Questions

1. **God persistence** - Do gods maintain state between realm sessions? (Probably yes - tracked storylines, recent targets)

2. **Multi-realm gods** - Can one god instance serve multiple realms, or one per realm? (Probably one per realm for isolation)

3. **God cooperation** - How do gods coordinate (e.g., death + tragedy during battle)? (Event-based? Explicit negotiation?)

4. **Player interaction** - Can players interact with gods directly (prayers, offerings)? (Probably via game mechanics that generate events gods subscribe to)

5. **Watcher creation** - Can players become watchers? (Dungeon masters are a form of this)

---

## References

- [Scenario Plugin Architecture](SCENARIO_PLUGIN_ARCHITECTURE.md)
- [Quest Plugin Architecture](QUEST_PLUGIN_ARCHITECTURE.md)
- [Storyline Composer](STORYLINE_COMPOSER.md)
- [Story System Guide](../guides/STORY_SYSTEM.md)
- [Dungeon as Actor](DUNGEON_AS_ACTOR.md) - Related watcher pattern
- [Actor Deep Dive](../plugins/ACTOR.md) - Data access pattern selection
- [SERVICE-HIERARCHY.md](../reference/SERVICE-HIERARCHY.md) - Variable Provider Factory pattern
