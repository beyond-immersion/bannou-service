# Bannou Story System

This document provides a comprehensive overview of Bannou's narrative generation system, including the theoretical foundations, plugin architecture, and integration patterns for both traditional quest-hub gameplay and emergent AI-driven storytelling.

> **Implementation Status**: The `lib-storyline` plugin exists with SDK integration for archives, spectrums, and arcs. The Quest and Scenario capabilities described here are planned extensions. See [Implementation Gaps](#implementation-gaps) for current state.

## Overview

The Bannou story system consists of three interconnected layers that work together to generate meaningful, character-driven narratives:

| Layer | Location | Purpose |
|-------|----------|---------|
| Story Templates (SDK) | `sdks/storyline-theory/` | Abstract narrative patterns from formal storytelling theory |
| Scenarios (Storyline Plugin) | `plugins/lib-storyline/` | Concrete game-world implementations of story patterns |
| Storyline Instances | Runtime state | Active narratives bound to specific characters |

The system is designed around a key insight: **the same scenario definitions power both simple deterministic games and complex emergent storytelling**. The triggering mechanism differs, not the content.

---

## Critical Architecture Principle

### The Plugin is PASSIVE - Actors Do the Searching

A common misconception: "The Storyline/Scenario plugin scans for characters that meet conditions."

**This is wrong.**

The correct architecture:

```
┌─────────────────────────────────────────────────────────────────┐
│                   REGIONAL WATCHER (Actor)                      │
│                                                                 │
│  ACTIVE: The actor's BEHAVIOR defines what it finds interesting │
│  • Receives events live in its region                          │
│  • "Navigates around" looking for characters/situations        │
│  • Queries plugins with specific characters/conditions         │
│  • Decides what to trigger based on its preferences            │
│                                                                 │
│  The BEHAVIOR is the soul - game-specific, expressive          │
└───────────────────────────┬─────────────────────────────────────┘
                            │ queries
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│                    STORYLINE PLUGIN (Passive)                   │
│                                                                 │
│  PASSIVE: Stores definitions, matches conditions, executes     │
│  • "What scenarios exist?" → returns list                      │
│  • "Does this character fit?" → returns matches                │
│  • "Test this scenario" → returns predicted outcome            │
│  • "Trigger this scenario" → applies mutations, spawns quests  │
│                                                                 │
│  Simple infrastructure - NO searching, NO decisions            │
└─────────────────────────────────────────────────────────────────┘
```

**Why this matters:**
- The "soul" of storyline discovery is in **behavior documents**, not plugin code
- Different watchers can use radically different search strategies
- Behaviors are game-specific content; the plugin is reusable infrastructure
- This keeps the plugin simple and behaviors expressive

See [Regional Watchers Behavior](../planning/REGIONAL_WATCHERS_BEHAVIOR.md) for active orchestration patterns.

---

## Theoretical Foundations

The story system synthesizes several influential narrative theories:

### Story Grid Four Core Framework (Coyne)

Shawn Coyne's Story Grid methodology provides the structural foundation for narrative state:

**Life Value Spectrums**: Every story operates on one or more value spectrums, each representing a fundamental human need (mapped to Maslow's hierarchy):

| Domain | Spectrum | Positive Pole | Negative Pole | Negation of Negation |
|--------|----------|---------------|---------------|---------------------|
| Survival (L1) | Life/Death | Life | Death | Damnation/Fate Worse Than Death |
| Safety (L2) | Honor/Dishonor | Honor | Dishonor | Treachery |
| Safety (L2) | Justice/Injustice | Justice | Injustice | Tyranny |
| Safety (L2) | Freedom/Subjugation | Freedom | Subjugation | Enslavement |
| Connection (L3) | Love/Hate | Love | Hate | Self-Loathing |
| Esteem (L4) | Respect/Shame | Respect | Shame | Humiliation |
| Esteem (L4) | Power/Impotence | Power | Impotence | Self-Destruction |
| Esteem (L4) | Success/Failure | Success | Failure | Selling Out |
| Self-Actualization (L5) | Altruism/Selfishness | Altruism | Selfishness | Self-Condemnation |
| Self-Actualization (L5) | Wisdom/Ignorance | Wisdom | Ignorance | Folly |

**Primary Spectrum**: Each content genre has ONE primary spectrum that defines the core stakes:
- **Action/Thriller/Horror**: Life/Death
- **War**: Honor/Dishonor
- **Crime**: Justice/Injustice
- **Love**: Love/Hate
- **Morality**: Altruism/Selfishness

### Reagan's Emotional Arcs (SVD Methodology)

Andrew Reagan's computational analysis of 40,000+ stories identified six fundamental emotional arc shapes that describe how the primary spectrum changes over time:

| Arc | Shape | Description | Example |
|-----|-------|-------------|---------|
| Rags to Riches | Rising | Steady improvement | Cinderella's rise |
| Tragedy | Falling | Steady decline | Oedipus |
| Man in Hole | Fall then Rise | Most common structure | Star Wars |
| Icarus | Rise then Fall | Hubris arc | Breaking Bad |
| Cinderella | Rise, Fall, Rise | Complex redemption | Pride and Prejudice |
| Oedipus | Fall, Rise, Fall | Complex tragedy | Hamlet |

### Propp's Morphology (31 Functions)

Vladimir Propp's analysis of Russian folktales provides atomic narrative building blocks. These 31 functions serve as GOAP actions for storyline planning:

| Phase | Key Functions | Narrative Effect |
|-------|---------------|------------------|
| Preparation | Interdiction, Violation | Establishes stakes |
| Complication | Villainy, Lack, Mediation | Primary spectrum falls |
| Donor | Testing, Acquisition | Enables comeback |
| Quest | Struggle, Victory | Climactic reversal |
| Return | Pursuit, Rescue | Secondary tension |
| Recognition | Recognition, Punishment, Wedding | Resolution |

### Save the Cat (Blake Snyder)

The 15-beat structure provides timing guidelines for narrative pacing:

| Beat | Percentage | Purpose |
|------|------------|---------|
| Opening Image | 0% | Establish starting state |
| Theme Stated | 5% | Plant thematic seed |
| Catalyst | 10% | Inciting incident |
| Break into Two | 25% | Commitment to adventure |
| Midpoint | 50% | False victory/defeat, stakes raise |
| All Is Lost | 75% | Darkest moment |
| Finale | 75-99% | Climactic sequence |
| Final Image | 100% | Show transformation |

---

## Architecture

### The Three-Layer Narrative Stack

```
                      AUTHORING / THEORY
┌─────────────────────────────────────────────────────────────────┐
│                   STORY TEMPLATES (SDK)                          │
│  Abstract narrative patterns from formal storytelling theory     │
│                                                                  │
│  • 10 Life Value Spectrums (NarrativeState)                     │
│  • 6 Reagan Emotional Arcs (arc shapes)                         │
│  • 31 Propp Functions (narrative actions)                       │
│  • 15 Save the Cat Beats (timing guidelines)                    │
│  • 12 Story Grid Genres (primary spectrum mapping)              │
│                                                                  │
│  "A character experiences loss, seeks meaning, finds purpose"   │
└───────────────────────────────┬─────────────────────────────────┘
                                │ implemented by
                                ▼
                        GAME DEFINITION
┌─────────────────────────────────────────────────────────────────┐
│                      SCENARIOS (Plugin)                          │
│  Concrete game-world implementations of story patterns           │
│                                                                  │
│  • Triggering conditions (character state, world state)         │
│  • Required participants (archetypes, relationships)            │
│  • Situational setup (locations, items, NPCs to spawn)          │
│  • State mutations (trauma, goals, relationships, memories)     │
│  • Quest hooks (spawn quests on scenario completion)            │
│                                                                  │
│  "Boy encounters monster in woods, develops fear and nightmares"│
└───────────────────────────────┬─────────────────────────────────┘
                                │ instantiated as
                                ▼
                         RUNTIME STATE
┌─────────────────────────────────────────────────────────────────┐
│                    STORYLINE INSTANCES                           │
│  Active narrative arcs bound to specific characters              │
│                                                                  │
│  • Concrete characters filling archetype roles                  │
│  • Specific locations, items, NPCs                              │
│  • Progress tracking via Quest system                           │
│  • Continuation points for lazy phase evaluation                │
│                                                                  │
│  "Aldric (age 8) encountered a Shadowwolf in Darkwood Forest"   │
└─────────────────────────────────────────────────────────────────┘
```

### Data Flow Between Components

```
┌──────────────────────────────────────────────────────────────────────┐
│                         SDK LAYER                                     │
│  ┌─────────────┐  ┌───────────────┐  ┌────────────────────────────┐ │
│  │ Storyline   │──│ GOAP Planner  │──│ Archive Extractor          │ │
│  │ Storyteller │  │ (A* Search)   │  │ (Compressed → NarrativeState)│ │
│  └─────────────┘  └───────────────┘  └────────────────────────────┘ │
│         │                                       │                     │
│         ▼                                       ▼                     │
│  ┌─────────────┐                    ┌────────────────────────────┐  │
│  │ Narrative   │                    │ NarrativeState             │  │
│  │ Templates   │                    │ (10 Life Value Spectrums)  │  │
│  └─────────────┘                    └────────────────────────────┘  │
└──────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌──────────────────────────────────────────────────────────────────────┐
│                       PLUGIN LAYER                                    │
│  ┌─────────────┐  ┌───────────────┐  ┌────────────────────────────┐ │
│  │ Storyline   │──│ Scenario      │──│ Quest                      │ │
│  │ Service     │  │ Service       │  │ Service                    │ │
│  └─────────────┘  └───────────────┘  └────────────────────────────┘ │
│         │                 │                       │                  │
│         ▼                 ▼                       ▼                  │
│  ┌─────────────┐  ┌───────────────┐  ┌────────────────────────────┐ │
│  │ Composes    │  │ Defines       │  │ Wraps                      │ │
│  │ scenarios   │  │ conditions &  │  │ Contract                   │ │
│  │ into arcs   │  │ mutations     │  │ for objectives             │ │
│  └─────────────┘  └───────────────┘  └────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌──────────────────────────────────────────────────────────────────────┐
│                    FOUNDATION LAYER                                   │
│                                                                       │
│  lib-contract   lib-currency   lib-inventory   lib-character         │
│  (FSM, consent) (rewards)      (items)         (state mutations)     │
│                                                                       │
└──────────────────────────────────────────────────────────────────────┘
```

---

## Two Execution Modes

The story system supports two fundamentally different execution modes using the **same scenario definitions**.

### Mode 1: Simple Mode (Direct Trigger)

Traditional MMO-style quest progression where the game server controls narrative flow.

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│   Game Server   │────▶│ Scenario Plugin │────▶│  Quest Plugin   │
│  (periodic check)│     │  (evaluate +    │     │ (create quest)  │
│                 │     │   trigger)      │     │                 │
└─────────────────┘     └─────────────────┘     └─────────────────┘
         │                                               │
         │ "Level 10? Trigger guild scenario"            │
         │                                               │
         ▼                                               ▼
   ┌───────────┐                                   ┌───────────┐
   │ Character │                                   │   Quest   │
   │   State   │                                   │   Log     │
   └───────────┘                                   └───────────┘
```

**Workflow**:
```csharp
// Game server periodic check
public async Task CheckScenarioTriggers(Guid characterId)
{
    var character = await _characterClient.GetAsync(characterId);
    var availableScenarios = await _scenarioClient.FindAvailableAsync(
        new FindAvailableRequest
        {
            CharacterId = characterId,
            Categories = [ScenarioCategory.Milestone, ScenarioCategory.Opportunity]
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
```

**Best for**:
- Traditional MMO quest hubs
- Level-gated content unlocks
- Tutorial sequences
- Guaranteed story beats
- Designer-controlled narrative

### Mode 2: Emergent Mode (Regional Watcher Discovery)

AI-driven storytelling where Regional Watchers **actively search** and orchestrate narrative opportunities. The key insight: **the actor does the searching, not the plugin**.

```
┌─────────────────────────────────────────────────────────────────┐
│                   REGIONAL WATCHER (Actor)                       │
│  "I like tragedy. I favor underdogs. Let me find a story..."    │
│                                                                  │
│  ACTIVE SEARCHING (via behavior document):                       │
│  • Subscribe to events in my region                              │
│  • Query character service for interesting candidates            │
│  • Score "tragic potential" using MY preferences                 │
│  • Decide which characters to investigate further                │
└───────────────────────────┬─────────────────────────────────────┘
                            │ queries with specific characters
                            ▼
┌───────────────────────────────────────────────────────────────────┐
│                     STORYLINE PLUGIN (Passive)                     │
│                                                                    │
│  /storyline/scenario/list        → "What scenarios interest me?"  │
│  /storyline/scenario/find-matching → "What fits THIS character?"  │
│  /storyline/scenario/test        → "What would happen if...?"     │
│  /storyline/scenario/trigger     → "Execute this scenario"        │
│  /storyline/compose              → "Plan from this archive"       │
│  /storyline/instantiate          → "Spawn the storyline"          │
│                                                                    │
│  NOTE: Plugin does NOT scan for characters - watcher provides them│
└───────────────────────────────────────────────────────────────────┘
```

**Regional Watcher Behavior** (ABML):
```yaml
metadata:
  type: event_brain
  domain: tragedy_and_redemption
  realm_ids: ["realm-omega"]

config:
  preferences:
    favored_story_templates: [FALL_FROM_GRACE, DOOMED_LOVE, LOSS_OF_INNOCENCE]
    favored_outcomes: [character_growth, meaningful_sacrifice]
    disfavored: [easy_victory, deus_ex_machina]

flows:
  periodic_scan:
    - schedule:
        interval: "PT1H"
        call: scan_for_tragedy_opportunities

  scan_for_tragedy_opportunities:
    # ACTOR searches for characters (not the plugin!)
    - api_call:
        service: character
        endpoint: /character/query
        data:
          realmId: "${my_realm}"
          filters:
            hasRelationships: true      # Something to lose
            notInActiveStoryline: true  # Not already in a story
            age: { min: 8, max: 30 }    # Prime tragedy candidates
        result: potential_victims

    # ACTOR scores by "tragic potential" using its own preferences
    - foreach:
        collection: "${potential_victims}"
        as: candidate
        do:
          - set:
              "candidate.tragic_score": >
                ${0.3 * candidate.relationships.positiveCount +
                  0.3 * candidate.backstory.goals.length +
                  0.2 * (candidate.age < 25 ? 1 : 0) +
                  0.2 * (candidate.hasLivingFamily ? 1 : 0)}

    # For top candidates, ask PLUGIN what scenarios fit
    - set:
        top_candidates: "${potential_victims.filter(c => c.tragic_score > 0.6).slice(0, 3)}"

    - foreach:
        collection: "${top_candidates}"
        as: candidate
        do:
          # Now query the plugin with a SPECIFIC character
          - api_call:
              service: storyline
              endpoint: /storyline/scenario/find-matching
              data:
                characterId: "${candidate.id}"
                preferredTemplates: "${preferences.favored_story_templates}"
              result: matching_scenarios

          - cond:
              - when: "${matching_scenarios.length > 0}"
                then:
                  - call: evaluate_and_trigger
                    with:
                      character: "${candidate}"
                      scenarios: "${matching_scenarios}"
```

**Three Watcher-to-Plugin Interaction Patterns**:

| Pattern | Watcher Asks | When Used |
|---------|--------------|-----------|
| **Scenarios-First** | "What scenarios interest me?" → searches for fitting characters | Watcher has narrative preferences, seeks characters to match |
| **Characters-First** | "I found interesting characters - what fits them?" | Watcher is exploratory, finds opportunities in whoever's around |
| **Event-Triggered** | "This event just happened - any consequences?" | Reactive (death → vengeance, betrayal → revenge) |

**The key insight**: The watcher's **behavior document** defines the search strategy. Different watchers can use completely different approaches with the same plugin APIs.

See [Regional Watchers Behavior](../planning/REGIONAL_WATCHERS_BEHAVIOR.md) for detailed patterns.

**Best for**:
- Emergent, unpredictable narrative
- Regional flavor through watcher preferences
- Multi-phase storylines
- Deep character development
- Living world experiences

### Mode Comparison

| Aspect | Simple Mode | Emergent Mode |
|--------|-------------|---------------|
| **Trigger** | Game server periodic check | Regional Watcher decision |
| **Control** | Designer-controlled | AI-curated |
| **Predictability** | Deterministic | Emergent |
| **Complexity** | Low (no AI needed) | High (behavior documents) |
| **Scenario Definitions** | **Same** | **Same** |
| **Player Experience** | Curated, reliable | Surprising, personal |

---

## Component Relationships

### How Storyline Composes Scenarios

Storylines are **sequences of scenarios** forming complete narrative arcs.

```
Storyline: "Revenge for Father's Death"
│
├── Phase 1: Scenario (WITNESS_PARENT_DEATH)
│   └── Mutations: trauma, grief, revenge_goal
│
├── Phase 2: Quest via questHook
│   └── Quest: "Investigate the Murder"
│
├── Phase 3: Scenario (DISCOVER_BETRAYAL)
│   └── Mutations: trust_broken, new_enemy
│
├── Phase 4: Quest via questHook
│   └── Quest: "Track the Assassin"
│
├── Phase 5: Scenario (CONFRONTATION)
│   └── Branches: forgive / avenge / justice
│
└── Resolution: Based on player choice
```

**Storyline Service Responsibilities**:
1. Extract narrative elements from compressed archives
2. Plan storyline structure using GOAP
3. Compose scenarios into coherent arcs
4. Track active storyline progress
5. Support continuation points for lazy evaluation

### How Scenarios Spawn Quests

Scenarios apply state mutations and optionally spawn quests via `questHooks`.

```
┌─────────────────────────────────────────────────────────────────┐
│                    SCENARIO TRIGGERED                            │
│                                                                  │
│  Code: MONSTER_ENCOUNTER_CHILDHOOD_TRAUMA                        │
│  Participants: victim (child), monster                          │
└───────────────────────────┬─────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│                   STATE MUTATIONS APPLIED                        │
│                                                                  │
│  • add_backstory: FEAR ("Terrified of wolves")                  │
│  • add_backstory: TRAUMA ("Nightmares of the attack")           │
│  • personality_shift: bravery -0.2                              │
│  • add_relationship: if rescued, bond with rescuer              │
└───────────────────────────┬─────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│                   QUEST HOOKS EVALUATED                          │
│                                                                  │
│  Hook 1: outcome == injured_escape AND victim.age > 16          │
│    → Quest: OVERCOME_CHILDHOOD_FEAR (delay: 5 years)            │
│    → "Return to face the monster that scarred you"              │
│                                                                  │
│  Hook 2: outcome == rescued                                      │
│    → Quest: REPAY_PROTECTOR (delay: 1 year)                     │
│    → "Find a way to thank your rescuer"                         │
└───────────────────────────┬─────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│                    QUESTS SPAWNED                                │
│                                                                  │
│  Quest created via lib-quest, which wraps lib-contract          │
│  Character receives quest(s) based on scenario outcome          │
│  Storyline service notified for arc progression                 │
└─────────────────────────────────────────────────────────────────┘
```

**Key Insight**: Scenarios can spawn zero, one, or many quests depending on conditions. Not every narrative event needs player-visible objectives.

### How Quests Wrap Contracts

Quests are a **thin orchestration layer** over lib-contract, adding game-specific semantics.

```
┌─────────────────────────────────────────────────────────────────┐
│                          QUEST (L4)                              │
│  Thin orchestration over Contract                                │
│                                                                  │
│  Quest Definition    = Contract Template + Quest Metadata        │
│  Active Quest        = Contract Instance                         │
│  Quest Giver         = Party (employer role)                     │
│  Questor            = Party (employee role)                      │
│  Objective          = Milestone                                  │
│  Objective Progress = Quest-specific tracking                    │
│  Reward Distribution = Prebound API execution                    │
│  Quest Failure      = Contract breach                            │
│  Quest Abandonment  = Contract termination                       │
└───────────────────────────┬─────────────────────────────────────┘
                            │ wraps
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│                        CONTRACT (L1)                             │
│  FSM + consent + milestones + prebound APIs                      │
│                                                                  │
│  • State machine for agreement lifecycle                        │
│  • Milestone tracking and deadlines                             │
│  • Prebound API execution for rewards:                          │
│    - serviceName: currency, endpoint: /credit                   │
│    - serviceName: inventory, endpoint: /add-item                │
│    - serviceName: character, endpoint: /grant-experience        │
│  • Consent and breach management                                │
└───────────────────────────┬─────────────────────────────────────┘
                            │ triggers
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│                  CURRENCY / INVENTORY (L2)                       │
│  Execute reward distribution                                     │
│  Publish transfer completion events                              │
└─────────────────────────────────────────────────────────────────┘
```

**Why This Pattern**:
- Quest doesn't reinvent state machines
- Contract handles all the hard FSM/consent logic
- Quest adds player-facing terminology and UI support
- Rewards are configured, not coded

### How Regional Watchers Orchestrate Everything

Regional Watchers are the **active agents** that tie all components together. The watcher's **behavior document** is the soul - it defines search strategies, preferences, and decision-making logic.

```
┌─────────────────────────────────────────────────────────────────┐
│                   REGIONAL WATCHER (Actor)                       │
│  "God of Tragedy" - ACTIVE agent with behavior document         │
│                                                                  │
│  ACTIVE RESPONSIBILITIES (defined in behavior):                  │
│    • Subscribe to events (deaths, relationships, prosperity)    │
│    • Search for characters via lib-character queries            │
│    • Score "tragic potential" using MY preferences              │
│    • Decide which opportunities to pursue                       │
│    • Track active storylines I'm orchestrating                  │
└───────────────────────────┬─────────────────────────────────────┘
                            │ queries (watcher provides the inputs)
         ┌──────────────────┼──────────────────┐
         ▼                  ▼                  ▼
┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐
│ lib-character   │ │ lib-storyline   │ │ lib-resource    │
│ (searching)     │ │ (PASSIVE)       │ │ (archives)      │
│                 │ │                 │ │                 │
│ • Query chars   │ │ • Match scenario│ │ • Get archive   │
│ • Get backstory │ │ • Test dry-run  │ │ • Snapshot live │
│ • Get relations │ │ • Trigger       │ │ • Extract data  │
│                 │ │ • Compose plan  │ │                 │
└─────────────────┘ └─────────────────┘ └─────────────────┘
         │                  │                  │
         └──────────────────┼──────────────────┘
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│                 WATCHER DECISION FLOW (in behavior)              │
│                                                                  │
│  1. Receive event OR periodic scan triggers                     │
│  2. WATCHER searches for characters (lib-character query)       │
│  3. WATCHER scores candidates using its own preferences         │
│  4. WATCHER asks plugin: "what scenarios fit THIS character?"   │
│  5. WATCHER evaluates: test dry-run, check narrative score      │
│  6. WATCHER decides: trigger or skip based on preferences       │
│  7. WATCHER tracks: monitor active storylines, advance phases   │
└─────────────────────────────────────────────────────────────────┘
```

**Key insight**: Plugins are **passive infrastructure**. Watchers are **active agents** whose behavior documents define game-specific search strategies and narrative preferences. We provide **base templates**; games provide **the soul**.

**Event-Triggered Watcher Flow** (ABML - reactive to deaths):
```yaml
flows:
  on_character_died:
    # Event-triggered pattern (Pattern C)
    - when: "perception:type == 'character_died'"
      then:
        # WATCHER decides if this death interests ME
        - cond:
            - when: "${event.character.hadLovedOnes || event.wasWitnessed}"
              then:
                - call: evaluate_vengeance_potential

  evaluate_vengeance_potential:
    # WATCHER fetches the archive
    - api_call:
        service: resource
        endpoint: /resource/archive/get
        data:
          resourceId: "${event.character.id}"
          resourceType: "character"
        result: archive

    # WATCHER asks plugin to compose (plugin is passive)
    - api_call:
        service: storyline
        endpoint: /storyline/compose
        data:
          archiveId: "${archive.id}"
          goals: ["revenge", "justice"]
          dryRun: true
        result: plan

    # WATCHER decides based on MY preferences
    - cond:
        - when: "${plan.confidence > 0.7 && plan.matchesMyPreferences}"
          then:
            # Track for later decision (watcher is willful, not automatic)
            - set:
                memories.pending_vengeance: "${plan.id}"
            - emit:
                channel: "intention"
                value: "considering_vengeance_storyline"
```

---

## Organic Character Creation

Arcadia's unique approach eliminates the traditional "character creation screen" in favor of **origin scenarios**.

### The Philosophy

Instead of choosing race, class, and backstory from menus, players discover who their character becomes through play.

```
┌─────────────────────────────────────────────────────────────────┐
│                     SPIRIT LANDS ON OBJECT                       │
│                                                                  │
│  Player spirit enters world, lands on an object:                │
│    • Staff      → hints toward mage archetype                   │
│    • Hammer     → hints toward smith/warrior archetype          │
│    • Shovel     → hints toward laborer/farmer archetype         │
│    • Quill      → hints toward scholar/scribe archetype         │
│                                                                  │
│  Object provides initial archetype HINTS, not determinism       │
└───────────────────────────┬─────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│                  TUTORIAL LOCATION DETERMINES                    │
│                   AVAILABLE ORIGIN SCENARIOS                     │
│                                                                  │
│  Mining Village:                                                 │
│    • CAVE_DISCOVERY, MINE_COLLAPSE, APPRENTICESHIP_OFFER        │
│                                                                  │
│  Forest Settlement:                                              │
│    • MONSTER_ENCOUNTER, RANGER_MENTOR, LOST_TRAVELER            │
│                                                                  │
│  Noble Estate:                                                   │
│    • SERVANT_LIFE, NOBLE_INTRIGUE, FORBIDDEN_FRIENDSHIP         │
└───────────────────────────┬─────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│                   ORIGIN SCENARIOS TRIGGER                       │
│                  BASED ON PLAYER CHOICES                         │
│                                                                  │
│  Example flow (Mining Village + Shovel):                        │
│                                                                  │
│  Day 1: Explores mine                                           │
│    → CAVE_DISCOVERY triggers (found rare ore)                   │
│    → Mutation: curiosity +0.2, geology_knowledge                │
│                                                                  │
│  Day 2: Returns ore to foreman                                  │
│    → HONEST_LABORER triggers (reputation boost)                 │
│    → Mutation: reputation.village_guard +0.3                    │
│                                                                  │
│  Day 3: Foreman notices diligence                               │
│    → APPRENTICESHIP_OFFER triggers                              │
│    → Mutation: occupation = "miner_apprentice"                  │
│                                                                  │
│  Character is now "miner's apprentice" - emerged from play      │
└─────────────────────────────────────────────────────────────────┘
```

### Origin Scenario Categories

| Category | Purpose | Examples |
|----------|---------|----------|
| **Background** | Establish social position | NOBLE_BIRTH, ORPHAN_STREETS, MERCHANT_FAMILY |
| **Formative** | Shape personality | CHILDHOOD_TRAUMA, MENTOR_BOND, FIRST_LOVE |
| **Skill** | Grant abilities | APPRENTICESHIP, MILITARY_TRAINING, SELF_TAUGHT |
| **Catalyst** | Start adventure | LOSS_OF_HOME, MYSTERIOUS_SUMMONS, DISCOVERED_POWER |

### Tutorial Period as Scenario Accumulation

```
Tutorial Period (First 7 Days)
│
├── Day 1: Land on object → Initial hints
│
├── Days 1-3: Exploration
│   └── Origin scenarios fire based on where player goes
│   └── Accumulate: backstory, personality, relationships
│
├── Days 4-6: Complications
│   └── More complex scenarios based on accumulated state
│   └── First quest hooks may trigger
│
└── Day 7: Tutorial Complete
    └── Character emerges from accumulated scenario outcomes
    └── No "creation screen" choices - all emerged from play
```

---

## Lazy Phase Evaluation

Storylines use **continuation points** to avoid pre-generating content that becomes stale.

### The Problem with Eager Generation

```
Day 1: Player starts "Avenge the Blacksmith" quest
       System generates all 5 phases including:
       "Phase 3: Confront the killer in the tavern"

Day 14: Player returns, ready for Phase 3
        BUT:
        • The killer moved to a different city 3 days ago
        • The tavern burned down 5 days ago
        • A new alliance formed between killer and town guard
        • Generated phase is now NONSENSICAL
```

**The world changes while players are offline.** Generating too far ahead creates invalid content.

### The Solution: Continuation Points

Only generate the **current phase** plus a **trigger condition** for the next phase.

```
┌─────────────────────────────────────────────────────────────────┐
│                      PHASE 1 (Generated)                         │
│                                                                  │
│  name: "discovery"                                               │
│  intents:                                                        │
│    - spawn: ghost_npc at death_location                         │
│    - spawn: apprentice at nearby_settlement                     │
│    - establish: apprentice.quest_hook = "master_not_at_rest"    │
│                                                                  │
│  continuation:                                                   │
│    id: "after_discovery"                                         │
│    trigger: "player.has_spoken_to_ghost"                        │
│    context_snapshot:                                             │
│      killer_last_known_location: "goldvein_tavern"              │
│      witness_npcs: ["bartender_jim", "miner_sal"]               │
└───────────────────────────┬─────────────────────────────────────┘
                            │
      [Player talks to ghost - trigger fires]
                            │
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│          PHASE 2 GENERATION (When Continuation Triggers)         │
│                                                                  │
│  1. Re-query world state:                                        │
│     - Where is the killer NOW?                                  │
│     - Has player interacted with killer since Phase 1?          │
│     - What new information exists?                              │
│                                                                  │
│  2. Compute delta from context_snapshot:                         │
│     - Killer moved from "goldvein_tavern" to "ironhold_fortress"│
│     - Tavern was destroyed by fire                              │
│     - Player unknowingly helped killer in separate quest        │
│                                                                  │
│  3. Generate Phase 2 incorporating delta:                        │
│     - Reveal killer fled via bartender (now homeless, camping)  │
│     - Plant clue pointing to new location                       │
│     - Add emotional weight: "Your friend is the murderer"       │
└─────────────────────────────────────────────────────────────────┘
```

### Narrative Adaptation Examples

**Killer Moved During Player Absence**:
```yaml
# Original Phase 2 would have said:
investigation_intents:
  - reveal: killer_location via bartender_dialogue
    location: "goldvein_tavern"

# Lazy Phase 2 detects delta and adapts:
investigation_intents:
  - reveal: killer_fled via bartender_dialogue
    context: "He left town three days ago, seemed scared"
  - plant_clue: killer_new_location via witness_encounter
    new_location: "ironhold_fortress"  # Where killer ACTUALLY is NOW
```

**Player Befriended Killer Unknowingly**:
```yaml
# Context snapshot from Phase 1:
context_snapshot:
  killer_sentiment_toward_player: null  # Unknown at pause

# Fresh world state at Phase 2:
fresh_state:
  killer_sentiment_toward_player: 0.7  # Player helped killer!

# Lazy Phase 2 incorporates this:
investigation_intents:
  - create_dilemma: "The man who helped you yesterday... is the murderer"
  - emotional_weight: BETRAYAL  # Much more impactful than pre-planned
```

**Killer Died During Absence**:
```yaml
# Fresh world state shows killer is now compressed
fresh_state:
  killer_status: "dead"
  killer_archive_id: "char-xyz-archive-v3"
  killer_death_cause: "killed_by_bandit"

# Lazy Phase 2 TRANSFORMS the storyline:
investigation_intents:
  - reveal: killer_already_dead via ghost_dialogue
  - new_theme: JUSTICE_DENIED → UNDERSTANDING
  - pivot: "Find out WHY the killer did what they did"
  - extract: killer_backstory from killer_archive  # Recursive archive use!
```

### Benefits of Lazy Evaluation

| Benefit | Description |
|---------|-------------|
| **World Consistency** | Next phase uses CURRENT world state, not stale snapshot |
| **Emergent Twists** | If killer died, storyline adapts organically |
| **Resource Efficiency** | Don't plan phases that may never be reached |
| **Player Agency Respected** | Choices during Phase 1 actually affect Phase 2 |
| **Organic Pacing** | Quest details incorporate recent events naturally |

---

## Compression as Narrative Seeds

Compressed archives of dead characters become the raw material for future storylines.

### Archive Contents and Storyline Uses

| Archive Layer | Data | Storyline Potential |
|---------------|------|---------------------|
| `character-base` | name, species, death_cause, family_tree | Identity, setting, death hooks |
| `character-personality` | bipolar trait axes | Behavior consistency, conflict |
| `character-history` | historical events, backstory elements | Quest seeds, NPC motivations |
| `character-encounter` | memorable interactions, sentiment | Grudges, alliances, dialogue |

### The Feedback Loop

```
┌─────────────────────────────────────────────────────────────────┐
│                      LIVING GAMEPLAY                             │
│                                                                  │
│  Characters play, form relationships, have adventures           │
│  Encounters recorded, history accumulated, personality shaped   │
└───────────────────────────┬─────────────────────────────────────┘
                            │ character dies
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│                    COMPRESSION (lib-resource)                    │
│                                                                  │
│  Death triggers compression pipeline:                           │
│  • Gather personality, history, encounters                      │
│  • Generate text summaries                                      │
│  • Store archive in MySQL                                       │
│  • Publish resource.compressed event                            │
└───────────────────────────┬─────────────────────────────────────┘
                            │ Regional Watcher detects
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│                  STORYLINE COMPOSITION                           │
│                                                                  │
│  Watcher calls /storyline/compose with archive:                 │
│  • Extract narrative state from archive                         │
│  • Match to story templates                                     │
│  • Generate plan with entities, quests, behaviors               │
│  • If confidence > threshold: instantiate                       │
└───────────────────────────┬─────────────────────────────────────┘
                            │ new entities enter world
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│                      NEW LIVING GAMEPLAY                         │
│                                                                  │
│  Ghost NPC haunts death location                                │
│  Apprentice offers revenge quest                                │
│  Killer becomes target of new storyline                         │
│  ...and these NEW characters will eventually die and compress   │
└─────────────────────────────────────────────────────────────────┘
```

### Example: The Blacksmith's Vengeance

**Archive Contents** (from actual play history):
```json
{
  "character-base": {
    "name": "Kira Ironheart",
    "species": "dwarf",
    "death_cause": "murdered",
    "death_location": "Goldvein Mine"
  },
  "character-personality": {
    "confrontational": 0.7,
    "loyal": 0.9,
    "vengeful": 0.8
  },
  "character-history": {
    "backstory": {
      "occupation": "master_blacksmith",
      "trauma": "lost_guild_to_betrayal",
      "goals": ["restore_guild_honor", "protect_apprentices"]
    }
  },
  "character-encounter": {
    "killer_id": "char-xyz",
    "killer_sentiment": -0.9,
    "encounter_type": "conflict"
  }
}
```

**Generated Storyline Plan**:
```yaml
plan_id: "revenge-kira-ironheart-abc123"
confidence: 0.87
template: "revenge_arc"

entities_to_spawn:
  - type: character
    role: ghost_npc
    name: "Spirit of Kira Ironheart"
    behavior_template: ghost_guardian
    bound_location: "Goldvein Mine"

  - type: character
    role: quest_giver
    name: "Apprentice Torval"
    relationship_to_deceased: apprentice

links:
  - type: guardian_of
    source: ghost_npc
    target: apprentice
    evidence: ["personality.loyal", "backstory.goals.protect_apprentices"]

  - type: enemy_of
    source: ghost_npc
    target: "char-xyz"  # The actual killer
    evidence: ["encounter.killer_sentiment"]

phases:
  - name: "discovery"
    intents:
      - spawn: ghost_npc at death_location
      - spawn: apprentice at nearby_settlement
      - establish: apprentice.quest_hook = "master_not_at_rest"

  - name: "investigation"
    continuation: after_player_speaks_to_ghost
    # Generated fresh with current world state
```

---

## Practical Guide

### When to Use Which Mode

| Situation | Recommended Mode | Rationale |
|-----------|------------------|-----------|
| Tutorial content | Simple | Guaranteed progression |
| Main story beats | Simple | Designer control |
| Side quests | Either | Balance predictability vs emergence |
| Character backstory | Emergent | Personal, meaningful |
| Regional flavor | Emergent | Watcher preferences create variety |
| Time-limited events | Simple | Reliable timing |
| Post-game content | Emergent | Infinite variation |

### Defining Scenarios

**Structure**:
```yaml
ScenarioDefinition:
  # Identity
  code: "SCENARIO_CODE"
  name: "Human Readable Name"
  description: "What happens in this scenario"

  # Story Template Reference
  storyTemplate:
    code: "TEMPLATE_CODE"      # From SDK
    category: backstory        # Origin, Milestone, Opportunity, etc.
    archetypes: [role1, role2] # Required participant types

  # Triggering Conditions
  conditions:
    characterRequirements:
      - archetype: protagonist
        alias: hero
        constraints:
          age: { min: 16 }
          hasTrauma: false

    worldRequirements:
      - type: location
        alias: danger_zone
        constraints:
          dangerLevel: { min: 3 }

    situationalRequirements:
      - type: proximity
        entities: [hero, danger_zone]
        distance: { max: 100 }

  # Execution phases
  execution:
    phases:
      - phase: setup
        actions:
          - type: spawn_encounter
            # ...

      - phase: event
        actions:
          - type: combat_or_flee
            outcomes:
              success:
                probability: 0.3
                mutations: [heroic_confidence]
              failure:
                probability: 0.7
                mutations: [trauma, fear]

  # State mutations by outcome
  mutations:
    trauma:
      target: "{hero}"
      changes:
        - type: add_backstory
          element: TRAUMA
          content: "Suffered defeat at {danger_zone}"
        - type: personality_shift
          trait: confidence
          direction: decrease

  # Quest spawning
  questHooks:
    - condition: "outcome == failure"
      questTemplate: "OVERCOME_FAILURE"
      delay: "P1Y"  # Available 1 year later
```

### Regional Watcher Behaviors

**Key Architecture**: Watchers are **active agents**. They search, score, and decide. Plugins are **passive infrastructure**. They store definitions and answer queries.

**Base Template** (games extend this):
```yaml
# templates/regional-watcher-base.yaml
metadata:
  id: regional-watcher-base
  type: event_brain
  abstract: true  # Can't be instantiated directly

config:
  subscriptions: []  # Override in concrete template
  preferences:
    favored_story_templates: []
    favored_outcomes: []
    disfavored: []

flows:
  # Common periodic scan pattern
  periodic_scan:
    - schedule:
        interval: "${config.scan_interval || 'PT30M'}"
        call: scan_for_opportunities

  # Abstract - games implement their own search logic
  scan_for_opportunities:
    - abstract: true

  # Common storyline tracking
  track_storyline:
    - set:
        active_storylines: "${active_storylines.concat([storyline])}"
```

**Game-Specific Implementation** (the "soul"):
```yaml
# arcadia/behaviors/god-of-tragedy.yaml
extends: regional-watcher-base

metadata:
  id: arcadia-god-of-tragedy
  description: "Moira, the Fate-Weaver"

config:
  scan_interval: "PT20M"
  subscriptions:
    - "character.arcadia.*.relationship.formed"
    - "character.arcadia.*.prosperity.gained"
  preferences:
    favored_story_templates: [FALL_FROM_GRACE, DOOMED_LOVE]
    disfavored: [easy_escape, consequence_free_choices]

flows:
  scan_for_opportunities:
    # GAME-SPECIFIC: What makes someone "tragically interesting"?
    - api_call:
        service: character
        endpoint: /character/query
        data:
          realmId: "arcadia"
          filters:
            hasPositiveRelationships: true
            recentAchievements: true
        result: candidates

    # GAME-SPECIFIC: Arcadia's happiness scoring formula
    - foreach:
        collection: "${candidates}"
        as: char
        do:
          - set:
              "char.happiness_score": >
                ${0.3 * char.relationships.positiveCount +
                  0.3 * char.recentAchievements.length +
                  0.4 * (char.hasLivingFamily ? 1 : 0)}
    # ... continue with scenario matching
```

**What We Provide vs What Games Provide**:

| We Provide (Templates) | Games Provide (Soul) |
|------------------------|----------------------|
| Base watcher structure | Search strategies |
| Common flows (scan, track) | Scoring formulas |
| Plugin query patterns | Narrative preferences |
| Storyline tracking | Domain-specific logic |

See [Regional Watchers Behavior](../planning/REGIONAL_WATCHERS_BEHAVIOR.md) for complete patterns.

---

## Decision Guide

```
Are you implementing narrative systems?
│
├─ Yes, need full emergent storytelling (Arcadia-style)
│  └─ Use Storyline SDK + Scenario Plugin + Quest Plugin
│     - Story templates for theory
│     - Scenarios for game-specific implementations
│     - Regional Watchers for orchestration
│     - Lazy phase evaluation for fresh content
│
├─ Yes, but traditional quest progression (MMO-style)
│  └─ Use Scenario Plugin + Quest Plugin (Simple Mode)
│     - Define scenarios with direct triggers
│     - Game server controls progression
│     - No AI needed, fully deterministic
│
├─ Yes, but just objective tracking
│  └─ Use Quest Plugin only
│     - Wraps Contract for objectives
│     - Event-driven progress updates
│     - Reward distribution via prebound APIs
│
└─ No, just managing agreements/contracts
   └─ Use Contract Plugin directly
      - FSM for state management
      - Milestone tracking
      - Prebound API execution
```

---

## References

### Planning Documents (Detailed Implementation)

- [Quest Plugin Architecture](../planning/QUEST_PLUGIN_ARCHITECTURE.md) - Quest system design
- [Scenario Plugin Architecture](../planning/SCENARIO_PLUGIN_ARCHITECTURE.md) - Scenario system design
- [Storyline Composer](../planning/STORYLINE_COMPOSER.md) - Archive-seeded narrative generation

### Research Sources

- Coyne, S. (2015-2020). *Story Grid*. Four Core Framework, Life Value Spectrums.
- Reagan, A. et al. (2016). *The emotional arcs of stories*. EPJ Data Science.
- Propp, V. (1928). *Morphology of the Folktale*. 31 narrative functions.
- Snyder, B. (2005). *Save the Cat!*. 15-beat structure.

### Related Guides

- [Music System Guide](MUSIC_SYSTEM.md) - Architecture precedent for SDK layering
- [Plugin Development](PLUGIN_DEVELOPMENT.md) - Creating Bannou plugins
- [Contract Plugin](../plugins/CONTRACT.md) - FSM and prebound API details

### SDK Documentation

- [storyline-theory SDK](../../sdks/storyline-theory/) - Narrative theory primitives
- [storyline-storyteller SDK](../../sdks/storyline-storyteller/) - Planning and composition

---

## Implementation Gaps

The `lib-storyline` plugin exists with foundational SDK integration. The following capabilities are **planned but not yet implemented**:

### Current State (lib-storyline)

| Capability | Status | Notes |
|------------|--------|-------|
| SDK integration (archives, spectrums, arcs) | ✅ Exists | Core narrative theory available |
| Archive mining | ✅ Exists | Can extract narrative state from compressed data |
| Basic storyline tracking | ✅ Exists | Can track active storylines |

### Planned Extensions

| Capability | Status | Planning Doc |
|------------|--------|--------------|
| Scenario definitions | 📋 Planned | [SCENARIO_PLUGIN_ARCHITECTURE.md](../planning/SCENARIO_PLUGIN_ARCHITECTURE.md) |
| Scenario condition matching | 📋 Planned | Passive APIs for watcher queries |
| Scenario triggering & mutations | 📋 Planned | State changes to characters |
| Quest spawning via hooks | 📋 Planned | Integration with lib-quest |
| Lazy phase evaluation | 📋 Planned | Continuation points |
| Storyline composition from archives | 📋 Planned | GOAP planning in narrative space |

### Quest Plugin (lib-quest)

| Capability | Status | Planning Doc |
|------------|--------|--------------|
| Quest definitions (contract templates) | 📋 Planned | [QUEST_PLUGIN_ARCHITECTURE.md](../planning/QUEST_PLUGIN_ARCHITECTURE.md) |
| Objective tracking | 📋 Planned | Event-driven progress updates |
| Quest log UI support | 📋 Planned | Player-facing views |
| Reward distribution | 📋 Planned | Via lib-contract prebound APIs |

### Next Steps

1. **Audit existing lib-storyline** against this guide to identify gaps
2. **Implement scenario APIs** as passive query endpoints
3. **Implement quest plugin** as thin wrapper over lib-contract
4. **Create watcher base templates** for regional watcher behaviors
5. **Test with simple watcher** (Monster God pattern) before complex (Tragedy God)

Run `/audit-plugin storyline` to begin gap analysis against these requirements.
