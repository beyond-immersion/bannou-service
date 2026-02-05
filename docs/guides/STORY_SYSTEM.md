# Bannou Story System

This document provides a comprehensive overview of Bannou's narrative generation system, including the theoretical foundations, plugin architecture, and integration patterns for both traditional quest-hub gameplay and emergent AI-driven storytelling.

## Overview

The Bannou story system consists of three interconnected layers that work together to generate meaningful, character-driven narratives:

| Layer | Location | Purpose |
|-------|----------|---------|
| Story Templates (SDK) | `sdks/storyline-theory/` | Abstract narrative patterns from formal storytelling theory |
| Scenarios (Plugin) | `plugins/lib-scenario/` | Concrete game-world implementations of story patterns |
| Storyline Instances | Runtime state | Active narratives bound to specific characters |

The system is designed around a key insight: **the same scenario definitions power both simple deterministic games and complex emergent storytelling**. The triggering mechanism differs, not the content.

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

AI-driven storytelling where Regional Watchers actively seek and orchestrate narrative opportunities.

```
┌─────────────────────────────────────────────────────────────────┐
│                   REGIONAL WATCHER (Actor)                       │
│  "I like tragedy. I favor underdogs. Let me find a story..."    │
│                                                                  │
│  Preferences: favored_story_types, favored_outcomes, disfavored │
└───────────────────────────┬─────────────────────────────────────┘
                            │ browses / tests / triggers
                            ▼
┌───────────────────────────────────────────────────────────────────┐
│                      SCENARIO PLUGIN                               │
│                                                                    │
│  /scenario/watcher/scan     → Find characters with narrative potential │
│  /scenario/find-available   → Get matching scenarios               │
│  /scenario/test             → Dry-run to see what would happen     │
│  /scenario/evaluate-fit     → Score narrative potential            │
│  /scenario/trigger          → Execute chosen scenario              │
│                                                                    │
└───────────────────────────┬───────────────────────────────────────┘
                            │
                            ▼
┌───────────────────────────────────────────────────────────────────┐
│                     STORYLINE PLUGIN                               │
│                                                                    │
│  /storyline/compose         → Generate StorylinePlan from archives │
│  /storyline/instantiate     → Spawn entities and actors           │
│  /storyline/discover        → Find narrative opportunities        │
│                                                                    │
└───────────────────────────────────────────────────────────────────┘
```

**Regional Watcher Behavior** (ABML):
```yaml
metadata:
  type: event_brain
  domain: tragedy_and_redemption
  realm_ids: ["realm-omega"]

context:
  preferences:
    favored_story_types: [tragedy, survival, coming_of_age]
    favored_outcomes: [character_growth, relationship_formation]
    disfavored: [easy_victory, deus_ex_machina]

flows:
  periodic_scan:
    - interval: "PT1H"  # Every hour
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
                    dry_run: true

                - if: test_result.dramatically_interesting
                  then:
                    - trigger_scenario:
                        scenario: "{scenario}"
                        orchestrator: "{self}"
```

**What Regional Watchers Do**:
1. **Scan** for characters with narrative potential
2. **Browse** available scenarios that could apply
3. **Evaluate** narrative fit against their preferences
4. **Test** scenarios via dry-run (see what would happen)
5. **Select** the most dramatically interesting option
6. **Trigger** the chosen scenario, becoming the orchestrator

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

Regional Watchers are the **creative agents** that tie all components together.

```
┌─────────────────────────────────────────────────────────────────┐
│                   REGIONAL WATCHER (Actor)                       │
│  "God of Vengeance" behavior document                           │
│                                                                  │
│  Subscriptions:                                                  │
│    - character.died (high significance deaths)                  │
│    - resource.compressed (archives available)                   │
│    - quest.completed (storyline progression)                    │
└───────────────────────────┬─────────────────────────────────────┘
                            │
         ┌──────────────────┼──────────────────┐
         ▼                  ▼                  ▼
┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐
│ lib-resource    │ │ lib-storyline   │ │ lib-scenario    │
│ (archives)      │ │ (composition)   │ │ (triggers)      │
│                 │ │                 │ │                 │
│ • Get archive   │ │ • Compose plan  │ │ • Find available│
│ • Snapshot live │ │ • Instantiate   │ │ • Test dry-run  │
│ • Extract data  │ │ • Track active  │ │ • Trigger       │
└─────────────────┘ └─────────────────┘ └─────────────────┘
         │                  │                  │
         └──────────────────┼──────────────────┘
                            ▼
┌─────────────────────────────────────────────────────────────────┐
│                      DECISION FLOW                               │
│                                                                  │
│  1. Detect opportunity (significant death event)                │
│  2. Fetch archive from lib-resource                             │
│  3. Call /storyline/compose with goal: "revenge"                │
│  4. Evaluate plan confidence (> 0.7?)                           │
│  5. If worthy: /storyline/instantiate                           │
│  6. Monitor progress, adjust as needed                          │
└─────────────────────────────────────────────────────────────────┘
```

**Watcher Workflow** (ABML):
```yaml
flows:
  process_death:
    - when: "perception:type == 'character_died'"
      then:
        - call: evaluate_vengeance_potential

  evaluate_vengeance_potential:
    - set: { temp.archive_id: "perception:archive_id" }
    - call_service:
        service: storyline
        endpoint: /storyline/compose
        body:
          seed_sources: ["{{temp.archive_id}}"]
          goal: revenge
          constraints:
            max_new_entities: 3
          dry_run: true

    - when: "response.plan.confidence > 0.7"
      then:
        # God's willful decision - not automated
        - set: { memories.pending_vengeance: "response.plan_id" }
        - emit: { channel: "intention", value: "will_review_vengeance_opportunity" }
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

**Basic Pattern**:
```yaml
metadata:
  type: event_brain
  domain: your_domain
  realm_ids: ["target_realm"]

context:
  preferences:
    favored_story_types: [...]
    favored_outcomes: [...]
    disfavored: [...]

  subscriptions:
    - topic: "relevant.event"

flows:
  process_event:
    - when: "perception:type == 'relevant_event'"
      then:
        - call: evaluate_opportunity

  evaluate_opportunity:
    - call_service:
        service: storyline
        endpoint: /storyline/compose
        body:
          seed_sources: ["{{archive_id}}"]
          goal: appropriate_goal
          dry_run: true

    - when: "response.plan.confidence > 0.7"
      then:
        - call: decide_enactment

  decide_enactment:
    # Watcher's willful decision
    - set: { memories.pending_opportunity: "{{plan_id}}" }
    # Later, if conditions remain favorable:
    - call_service:
        service: storyline
        endpoint: /storyline/instantiate
```

**Watcher Preference Categories**:
| Preference | Effect |
|------------|--------|
| `favored_story_types` | Higher scores for matching scenarios |
| `favored_outcomes` | Prefer scenarios leading to these |
| `disfavored` | Avoid scenarios with these elements |
| `domain` | What types of events this watcher cares about |

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
