# Storyline Composer: Seeded Narrative Generation from Compressed Archives

> **Status**: Planning
> **Priority**: High
> **Related**: `docs/planning/COMPRESSION-GAMEPLAY-PATTERNS.md`, `docs/planning/COMPRESSION-CHARTS.md`, `docs/planning/REGIONAL_WATCHERS_BEHAVIOR.md`, `docs/planning/COMPOSER-LAYER.md`, `docs/plugins/RESOURCE.md`
> **SDK Precedent**: `sdks/music-storyteller/`, `sdks/music-theory/`, `docs/plugins/MUSIC.md`
> **Services**: Proposed `lib-storyline` (or `lib-seed`), depends on `lib-resource`, `lib-character`, `lib-realm`, `lib-scene`, `lib-quest`, `lib-actor` clients as needed
>
> **⚠️ AUTHORITY HIERARCHY**:
> - **YAML schemas** (`schemas/storyline/*.yaml`) are authoritative for narrative theory
> - **This document** describes architecture and integration patterns
> - **If this document conflicts with YAML schemas, the YAML is correct**
> - **Gap analysis**: See `schemas/storyline/GAP_ANALYSIS.md` for implementation status

## Summary

Compressed archives already exist and are rich enough to seed emergent content. The missing piece is a dedicated service that turns those archives into actionable storyline plans and optional instantiations, while keeping willful behavior inside Regional Watchers (gods) and event actors.

This document defines a **Storyline Composer** service that:
1. Accepts seed sources (archives, live snapshots, multi-archive bundles).
2. Produces a **StorylinePlan** (deterministic, inspectable, replayable).
3. Optionally instantiates the plan into characters, quests, scenes, and actors.

The gods decide when to call it and which plans to enact. The composer does not decide if a storyline should happen, only how it could happen given constraints.

## Goals

1. Provide stable endpoints for actor behaviors to use compressed data as seed inputs.
2. Produce a plan that can be inspected, moderated, or rejected before spawning.
3. Keep system boundaries clean: behavior and intent remain in Regional Watchers.
4. Support both deceased archives and living snapshots without deletion.
5. Enable multi-archive compositions for cross-character storylines.

## Non-Goals

1. The composer is not a god, actor, or event brain.
2. The composer does not own event subscriptions or behavioral loops.
3. The composer does not decide whether a plan should be enacted.
4. The composer does not replace manual quest authoring.

## Layering and Ownership

`lib-storyline` fits into the **Application / Thin Orchestration** layer per the service hierarchy:

```
BEHAVIORAL INTELLIGENCE LAYER (Actor | Behavior | ABML | GOAP)
        │ queries / drives        ← Regional Watchers call /storyline/compose
        ▼
APPLICATION / THIN ORCHESTRATION (lib-quest | lib-trade | lib-market | lib-storyline)
        │ creates / manages       ← Storyline composer lives here
        ▼
CUSTODY LAYER (lib-escrow)
        │ delegates logic to
        ▼
LOGIC LAYER (lib-contract)         ← Milestone-based storyline progression
        │ operates on
        ▼
ASSET LAYER (lib-currency | lib-item | lib-inventory)
        │ + memory layer
        ▼
MEMORY LAYER (lib-character-encounter)  ← Storyline seeds from encounter history
        │ references / scoped by
        ▼
ENTITY FOUNDATION LAYER (Character | Personality | History | Relationship)
        │ persisted / routed by   ← Compressed archives stored here
        ▼
INFRASTRUCTURE LAYER (State | Messaging | Mesh | Connect)
```

Following the "Quest Service as Thin Orchestration" pattern, the storyline composer doesn't reinvent the wheel:

| Storyline Feature | Handled By |
|-------------------|------------|
| "Ghost haunts the mine" | lib-actor `spawn` + ABML behavior document |
| "Quest to avenge blacksmith" | lib-quest `createQuest` with generated objectives |
| "Apprentice remembers master" | lib-character-encounter `recordEncounter` |
| "Milestone: find the killer" | lib-contract milestone progression |
| "Reward: family heirloom" | lib-item `createInstance` + lib-inventory |
| "NPC personality consistency" | lib-character-personality (preserved from archive) |

**The storyline service primarily needs to:**
1. Extract narrative elements from compressed archives
2. Plan storyline structure using GOAP/templates
3. Output intents that other services execute
4. Track active storyline instances
5. Support continuation points for lazy phase evaluation

It must not be depended on by foundation services.

## SDK Layering Pattern (From MusicTheory / MusicStoryteller)

The music stack demonstrates a clean split:
1. **SDKs** hold deterministic, reusable composition logic.
2. **Plugin** provides schemas + caching + service lifecycle.
3. **Clients** can use the SDKs directly for authoring.

For storylines, the same pattern could apply:

1. **StorylineTheory SDK**: Pure data and mechanics (story graph primitives, link rules, plausibility scoring, seed extraction helpers).
2. **StorylineStoryteller SDK**: Planning layer (goal-directed sequencing, narrative templates, constraint solving).
3. **Storyline Composer Service (plugin)**: Wraps SDKs with schemas, caching, persistence, and instantiation into game entities.

This keeps the "brains" shareable with clients and tools, while the plugin handles server-side state and spawning.

**Schema interop**: The music service exposes SDK types directly via `x-sdk-type` (for example, `MidiJson`, `PitchClass`). Storyline types like `StorylinePlan`, `StorylineNode`, or `StorylineLink` can follow the same pattern so clients and server share identical models without duplication.

## Architectural Precedent from MusicStoryteller

The `music-storyteller` SDK provides a proven architecture pattern for narrative-driven generation. However, **the music SDK's dimensions do not apply to literary narrative**. Musical perception dimensions (brightness, warmth, energy, valence) describe how music *sounds*, not how stories *mean*.

### Music → Storyline Mapping (Pattern Only, Not Content)

| Music Concept | Storyline Equivalent | Purpose |
|--------------|---------------------|---------|
| `EmotionalState` | `NarrativeState` (10 Life Value Spectrums) | Target dramatic state for story beat |
| `CompositionRequest` | `StorylineRequest` | High-level input specifying goals and constraints |
| `NarrativeTemplate` | `StoryArcTemplate` | Structural blueprint with phases |
| `NarrativePhase` | `StoryPhase` | Sequential progression points with target states |
| `GOAPPlanner` with A* search | Same | Action sequencing toward goal states |
| `ActionLibrary` | `StoryActionLibrary` | Atomic story modifications |
| `Plan` with action sequence | `StorylinePlan` | Ordered steps to reach narrative goal |

### NarrativeState (Authoritative Definition)

> **AUTHORITATIVE SOURCE**: `schemas/storyline/narrative-state.yaml`
>
> Based on Story Grid's Life Value Spectrums from the Four Core Framework (Shawn Coyne, 2015-2020).
> Grounded in Maslow's hierarchy of needs - each spectrum maps to a fundamental human need.

```csharp
public sealed class NarrativeState
{
    // Each spectrum is a three-point scale:
    //   +1.0 = Positive pole (life, love, justice, etc.)
    //    0.0 = Negative pole (death, hate, injustice, etc.)
    //   -1.0 = Negation of negation (fate WORSE than negative - damnation, tyranny, etc.)

    // Survival Domain (Maslow Level 1)
    public double? LifeDeath { get; set; }           // Action, Thriller, Horror

    // Safety Domain (Maslow Level 2)
    public double? HonorDishonor { get; set; }       // War
    public double? JusticeInjustice { get; set; }    // Crime
    public double? FreedomSubjugation { get; set; }  // Western, Society

    // Connection Domain (Maslow Level 3)
    public double? LoveHate { get; set; }            // Love

    // Esteem Domain (Maslow Level 4)
    public double? RespectShame { get; set; }        // Performance
    public double? PowerImpotence { get; set; }      // Society
    public double? SuccessFailure { get; set; }      // Status

    // Self-Actualization Domain (Maslow Level 5)
    public double? AltruismSelfishness { get; set; } // Morality
    public double? WisdomIgnorance { get; set; }     // Worldview

    // Which spectrum is primary (determined by genre)
    public SpectrumType PrimarySpectrum { get; set; }

    // Distance for GOAP heuristics (only compares active spectrums)
    public double DistanceTo(NarrativeState target)
    {
        double sum = 0;
        int count = 0;
        if (LifeDeath.HasValue && target.LifeDeath.HasValue)
        {
            sum += Math.Abs(LifeDeath.Value - target.LifeDeath.Value);
            count++;
        }
        // ... etc for each spectrum
        return count > 0 ? sum / count : 0;
    }
}
```

### Genre → Primary Spectrum Mapping

Each content genre has a PRIMARY spectrum (the main stakes). Secondary spectrums are unrestricted - any spectrum can be secondary via genre layering.

| Genre | Primary Spectrum | Core Event |
|-------|------------------|------------|
| Action | LifeDeath | Hero at Mercy of Villain |
| Thriller | LifeDeath | Hero at Mercy of Villain |
| Horror | LifeDeath | Victim at Mercy of Monster |
| War | HonorDishonor | Big Battle |
| Crime | JusticeInjustice | Exposure of Criminal |
| Western | FreedomSubjugation | Big Showdown |
| Love | LoveHate | Proof of Love |
| Performance | RespectShame | Big Performance |
| Society | PowerImpotence | Revolution |
| Status | SuccessFailure | Big Choice |
| Morality | AltruismSelfishness | Big Choice |
| Worldview | WisdomIgnorance | Cognitive Growth |

### Reagan Arc Integration

Reagan's 6 emotional arcs describe the *shape* of how the PRIMARY spectrum changes over time:

| Arc | Shape | Description |
|-----|-------|-------------|
| Rags to Riches | ↗ | Primary spectrum rises steadily |
| Tragedy | ↘ | Primary spectrum falls steadily |
| Man in Hole | ↘↗ | Falls then rises (most common) |
| Icarus | ↗↘ | Rises then falls |
| Cinderella | ↗↘↗ | Rise, fall, rise again |
| Oedipus | ↘↗↘ | Fall, rise, fall again (complex tragedy) |

Example: Crime genre + Man in Hole arc = JusticeInjustice starts neutral (0.5) → falls as criminal seems to escape (0.2) → rises as detective closes in (0.9)
```

### GOAP Planning for Storylines

The behavior service uses urgency-tiered GOAP planning with A* search. The same pattern applies to storyline planning with different time horizons:

| Urgency | Storyline Context | Max Depth | Timeout | Nodes |
|---------|------------------|-----------|---------|-------|
| **Low** (< 0.3) | Background plots, long-term arcs | 15 | 500ms | 5000 |
| **Medium** (0.3-0.7) | Active questlines, session arcs | 10 | 100ms | 1000 |
| **High** (> 0.7) | Immediate reactions, triggered events | 5 | 50ms | 200 |

**NarrativeState for GOAP** (replaces WorldState for story planning):
```csharp
// Crime story: Justice spectrum is primary
var currentState = new NarrativeState
{
    PrimarySpectrum = SpectrumType.JusticeInjustice,
    JusticeInjustice = 0.3,  // Criminal is winning
    LifeDeath = 0.6,         // Secondary: detective's life at risk
};

var goalState = new NarrativeState
{
    JusticeInjustice = 0.9,  // Criminal exposed
    LifeDeath = 0.8,         // Detective survives
};
```

**GOAPGoal using NarrativeState**:
```csharp
// "Expose the criminal" story beat
var goal = GOAPGoal.FromNarrativeState(goalState)
    .WithMinimumProgress(SpectrumType.JusticeInjustice, 0.6)
    .WithPriority(0.8);
```

### Story Action Library

> **AUTHORITATIVE SOURCE**: `schemas/storyline/story-grid-genres.yaml` (Five Commandments), `schemas/storyline/propp-functions.yaml` (31 functions)

Story actions are derived from two sources:
1. **Story Grid's Five Commandments** - Scene-level action patterns
2. **Propp's 31 Functions** - Event-level narrative primitives

**Five Commandments → GOAP Action Pattern**:
```
Inciting Incident → Initial world state perturbation
Turning Point     → Complication requiring re-planning
Crisis            → Decision point requiring character choice
Climax            → Action execution
Resolution        → New world state
```

**Propp Functions as Actions** (from `propp-functions.yaml`):

| Phase | Key Functions | Effect on Primary Spectrum |
|-------|---------------|---------------------------|
| Preparation | Interdiction, Violation | Establishes stakes |
| Complication | Villainy/Lack, Mediation | Primary spectrum falls |
| Donor | Testing, Reaction, Acquisition | Enables comeback |
| Quest | Struggle/Task, Victory | Climactic reversal |
| Return | Pursuit, Rescue | Secondary tension |
| Recognition | Recognition, Punishment, Wedding | Resolution |

**Action Example** (using NCP as hub vocabulary):
```yaml
actions:
  villainy:
    propp_symbol: "A"
    ncp_functions: [Temptation, Threat, Uncontrolled]
    preconditions:
      primary_spectrum: { max: 0.7 }  # Not already in crisis
    effects:
      primary_spectrum_delta: -0.3    # Falls toward negative pole
    cost: 2.0
    typical_position: [0.08, 0.12]    # ~10% into story
```

Each action has preconditions (NarrativeState ranges), effects (spectrum deltas), and cost - enabling GOAP planning.

### Continuation Points for Ongoing Storylines (Lazy Phase Evaluation)

The behavior system supports `ContinuationPoint` opcodes for streaming composition - allowing base behaviors to pause and incorporate extensions. **This pattern is CRITICAL for storylines** because it enables **lazy phase evaluation**.

#### The Problem with Eager Generation

If we generate the entire storyline upfront:
```
Day 1: Player starts "Avenge the Blacksmith" quest
       → System generates all 5 phases including "confront the killer in the tavern"

Day 14: Player returns, ready for Phase 3
       → But the killer moved to a different city 3 days ago
       → The tavern burned down
       → A new alliance formed between killer and town guard
       → Generated phase is now nonsensical
```

**The world changes while players are offline.** Generating storylines too far ahead creates content that becomes invalid.

#### Lazy Phase Evaluation

Instead, only generate the **current phase + next trigger condition**:

```yaml
# Phase 1: Generated on storyline start
active_phase:
  name: "discovery"
  intents:
    - spawn: ghost_npc at death_location
    - spawn: apprentice at nearby_settlement
    - establish: apprentice.quest_hook = "master_not_at_rest"

  continuation:
    id: "after_discovery"
    trigger: "player.has_spoken_to_ghost"  # When this fires, generate Phase 2
    context_snapshot:
      killer_last_known_location: "goldvein_tavern"
      killer_sentiment_toward_player: null
      witness_npcs: ["bartender_jim", "miner_sal"]

# Phase 2: Generated ONLY when continuation triggers
# At this point, we re-query world state:
#   - Where is the killer NOW?
#   - Has player interacted with killer since Phase 1?
#   - What new information exists?
```

#### Benefits of Lazy Evaluation

1. **World consistency**: Next phase uses CURRENT world state, not stale snapshot
2. **Emergent twists**: If the killer died during player absence, storyline adapts
3. **Resource efficiency**: Don't plan phases that may never be reached
4. **Player agency respected**: Choices during Phase 1 actually affect Phase 2 generation
5. **Organic pacing**: Quest details incorporate recent events naturally

#### Implementation Pattern

```csharp
public class ContinuationPoint
{
    public string Id { get; set; }                    // "after_discovery"
    public string TriggerCondition { get; set; }      // ABML-style condition
    public Dictionary<string, object> ContextSnapshot { get; set; }  // Relevant state at pause
    public string[] RelevantArchiveIds { get; set; }  // Archives to re-read for next phase
    public DateTimeOffset CreatedAt { get; set; }     // For staleness tracking
}

// When continuation triggers:
public async Task<StorylinePhase> GenerateNextPhaseAsync(
    Guid storylineId,
    string continuationId,
    CancellationToken ct)
{
    var storyline = await _storyStore.GetAsync(storylineId, ct);
    var continuation = storyline.Continuations[continuationId];

    // Re-extract from archives with CURRENT world state overlay
    var freshWorldState = await _extractor.ExtractAsync(
        continuation.RelevantArchiveIds,
        includeCurrentWorldState: true,  // Key difference from initial generation
        ct);

    // What changed since the continuation was created?
    var delta = ComputeWorldStateDelta(continuation.ContextSnapshot, freshWorldState);

    // Generate next phase incorporating the delta
    return await _planner.PlanNextPhaseAsync(
        storyline,
        continuationId,
        freshWorldState,
        delta,  // "The killer moved", "Tavern burned", etc.
        ct);
}
```

#### Narrative Adaptation Examples

**Scenario: Killer moved during player absence**
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
    new_location: "ironhold_fortress"  # Where killer actually is NOW
```

**Scenario: Player befriended killer unknowingly**
```yaml
# Continuation context from Phase 1:
context_snapshot:
  killer_sentiment_toward_player: null  # Unknown at pause

# Fresh world state at Phase 2:
fresh_state:
  killer_sentiment_toward_player: 0.7  # Player helped killer in unrelated quest!

# Lazy Phase 2 incorporates this:
investigation_intents:
  - create_dilemma: "The man who helped you yesterday... is the murderer"
  - emotional_weight: BETRAYAL  # Much more impactful than if pre-planned
```

**Scenario: Killer died during absence**
```yaml
# Fresh world state shows killer is now in a compressed archive
fresh_state:
  killer_status: "dead"
  killer_archive_id: "char-xyz-archive-v3"
  killer_death_cause: "killed_by_bandit"

# Lazy Phase 2 transforms the storyline:
investigation_intents:
  - reveal: killer_already_dead via ghost_dialogue
  - new_theme: JUSTICE_DENIED → UNDERSTANDING
  - pivot: "Find out why the killer did what they did"
  - extract: killer_backstory from killer_archive  # Recursive archive use!
```

This creates storylines that feel **responsive and alive** - the quest the player left isn't frozen in amber, it evolved with the world.

## Integration with Actor Cognition Pipeline

The behavior service implements a 5-stage cognition pipeline that the storyline composer can leverage:

### Stage 1: Attention Filter → Story Relevance Filter
When scanning archives/snapshots, prioritize by narrative weight:
- **Threat weight** (10.0x) → Antagonists, dangers, unresolved conflicts
- **Novelty weight** (5.0x) → Unique traits, rare events, anomalies
- **Social weight** (3.0x) → Relationships, family trees, alliances
- **Routine weight** (1.0x) → Background details, flavor data

### Stage 2: Significance Assessment → Story Potential Scoring
Score each archive entry for narrative potential:
```
Score = emotional_impact × 0.4 + conflict_relevance × 0.4 + relationship_density × 0.2
```
Only entries above threshold (e.g., 0.7) become story seeds.

### Stage 3: Memory Formation → Story Element Extraction
Extract structured story elements from high-scoring entries:
- **Backstory elements** (origin, occupation, trauma, fears, goals)
- **Historical participations** (wars, disasters, political upheavals)
- **Encounter history** (grudges, alliances, memorable meetings)
- **Personality traits** (bipolar axes for character consistency)

### Stage 4: Goal Impact Evaluation → Narrative Arc Fitting
Map extracted elements to narrative templates:
- Does this character fit "tragic hero"?
- Does this relationship suggest "revenge arc"?
- Does this event sequence match "fall and redemption"?

### Stage 5: Intention Formation → Storyline Plan Generation
Produce concrete `StorylinePlan` with intents for each phase.

## Core Data Flow

1. Resource compression runs and stores an archive.
2. A god or event actor detects an opportunity.
3. The god calls the composer with archive id(s) and a goal.
4. The composer returns a StorylinePlan.
5. The god chooses to instantiate the plan or discard it.
6. Instantiation creates new entities and emits events.

## API Surface (Proposed)

### Compose

`POST /storyline/compose`

Request:
`seed_sources`: list of archive ids and optional live snapshot ids.
`goal`: high-level intent from the caller, for example `resurrection`, `revenge`, `legacy`, `mystery`, `peace`.
`constraints`: realm id, location id, allowed entity types, max new entities.
`fidelity`: per-source fidelity targets, for example `ghost: 0.6`.
`dry_run`: default true.

Response:
`plan`: StorylinePlan.
`analysis`: reasons, conflicts, and the evidence pulled from sources.

### Instantiate

`POST /storyline/instantiate`

Request:
`plan_id`.
`execution_mode`: `plan_only`, `spawn_entities`, `spawn_entities_and_actor`.
`owner_actor_id`: optional, for traceability.

Response:
`result`: ids of created entities and spawned actors.

### Discover Opportunities (Optional)

`POST /storyline/discover`

Request:
`realm_id`.
`filters`: archive types, time windows, significance thresholds.
`limit`.

Response:
`candidates`: ranked archive ids with reasons.

## Data Model (Proposed)

**StorylinePlan**.
`plan_id`: deterministic id for replay and moderation.
`seed_sources`: list of archive ids and snapshot ids used.
`themes`: tags like `revenge`, `loss`, `legacy`, `mystery`.
`entities_to_spawn`: characters, quests, scenes, items, actors.
`links`: relationships to create between entities.
`locations`: target realm and location anchors.
`risks`: conflicts or missing data that reduce confidence.
`confidence`: 0-1 score for plan viability.
`created_at`: timestamp for TTL and auditing.

**StorylineEntitySpawn**.
`entity_type`: character, quest, scene, item, actor.
`template_id`: optional actor or quest template.
`seeded_from`: archive id(s).
`properties`: parameter bag for downstream services.

**StorylineLink**.
`type`: parent_child, enemy_of, guardian_of, bonded_to.
`source_entity_ref`.
`target_entity_ref`.
`evidence`: which archive fields justify the link.

## Integration with Regional Watchers

Regional Watchers keep the willful logic:
1. Subscribe to domain events.
2. Decide whether a scenario should occur.
3. Call `/storyline/compose`.
4. Evaluate the plan and decide to instantiate.
5. Spawn a coordinator event agent if long-running orchestration is required.

This keeps the composer deterministic and testable while the gods remain creative and selective.

## Live Compression Inputs

For living characters, the composer should accept **live snapshots** instead of destructive compression. This is now implemented as lib-resource's ephemeral snapshot system (see `COMPRESSION-GAMEPLAY-PATTERNS.md` for the gameplay vision and `docs/plugins/RESOURCE.md` for the API).

### Live Snapshot Architecture

**STATUS**: Schema added to `schemas/resource-api.yaml` and `schemas/resource-events.yaml`.
Implementation pending in `lib-resource/ResourceService.cs`.

A **LiveSnapshotRequest** triggers non-destructive data gathering using the same compression callbacks but without deletion:

```
POST /resource/snapshot/execute
{
  "resourceType": "character",
  "resourceId": "abc-123",
  "snapshotType": "storyline_seed",  // Distinguishes from archival
  "ttlSeconds": 3600  // Snapshots are ephemeral (default 1 hour, max 24 hours)
}
```

**Response**:
```json
{
  "resourceType": "character",
  "resourceId": "abc-123",
  "success": true,
  "snapshotId": "snap-xyz-456",
  "expiresAt": "2026-02-03T15:00:00Z",
  "callbackResults": [...],
  "snapshotDurationMs": 127
}
```

This produces a `ResourceSnapshot` (like `ResourceArchive` but in Redis with TTL via `resource-snapshots` store) that the storyline composer can consume without affecting the living entity.

**Key Differences from compress/execute**:
| Aspect | compress/execute | snapshot/execute |
|--------|------------------|------------------|
| Storage | MySQL (permanent) | Redis (ephemeral) |
| Deletion | Optional (`deleteSourceData`) | Never |
| TTL | None (permanent) | Configurable (1h default, 24h max) |
| Event | `resource.compressed` | `resource.snapshot.created` |
| Use Case | Death/archival | Living entity capture |

**ABML Integration** (for actor behaviors):
```yaml
# Actor behavior can request a snapshot of another character
- service_call:
    service: resource
    method: /resource/snapshot/execute
    parameters:
      resourceType: character
      resourceId: "${target_character_id}"
      snapshotType: storyline_seed
      ttlSeconds: 1800
    result_variable: snapshot_result
```

## Compressed Archive Data Richness

Understanding what's IN the archives reveals the storyline potential:

### Character Archive Entries (Priority-Ordered)

| Source Type | Priority | Data Available | Storyline Uses |
|-------------|----------|---------------|----------------|
| `character-base` | 0 | name, species, realm, death cause, family tree refs | Identity, setting, cause of death hooks |
| `character-personality` | 10 | Bipolar trait axes (-1 to +1): confrontational↔pacifist, trusting↔suspicious, etc. | Behavior consistency, conflict generation |
| `character-history` | 20 | Historical events with role/significance, backstory elements (origin, occupation, trauma, fears, goals) | Quest seeds, NPC motivations, world connections |
| `character-encounter` | 30 | Memorable interactions (weighted sentiment, has-met tracking, location context) | Grudges, alliances, dialogue triggers, quest hooks |

### What Makes This Different from Hand-Authored Content

Traditional game narrative:
```
Designer writes: "Quest: Avenge the Blacksmith"
- Fixed NPC with scripted backstory
- Predetermined villain
- Static reward
```

Archive-seeded narrative:
```
Composer extracts from actual play:
- Character who died: blacksmith with 3.2 years played
- Personality: confrontational (0.7), loyal (0.9), risk-taking (0.6)
- History: participated in "The Scarlet Uprising" as rebel leader
- Encounters: 47 positive interactions with town NPCs, 3 bitter rivalries
- Death: killed by entity with grudge from encounter #23
- Family: two living children (player characters), spouse compressed 6 months ago

Generated storyline:
- Ghost seeks resolution for ACTUAL unfinished business
- Revenge target is the REAL killer from the encounter
- Quest involves the ACTUAL children who remember their parent
- Resolution options informed by personality (confrontational → violent option weighted higher)
```

**The storyline is unique to this game world because it's seeded from actual play history.**

## Why This Enables True Emergence

### The Fundamental Insight

Traditional games have two modes:
1. **Scripted content**: Rich but finite, authored in advance
2. **Procedural generation**: Infinite but shallow, no emotional weight

Archive-seeded storylines create a third mode:
3. **Emergent narrative**: Infinite AND meaningful, because it references actual history

The key realization: **compression archives are not just for cleanup - they are distilled narrative essence**. Every dead character is a potential story seed. Every compressed location is a lost place that could be rediscovered. Every archived relationship is an echo that could resonate into new content.

### The Network Effect

As the world accumulates archives:
- Year 1: 1,000 compressed characters → 1,000 potential story seeds
- Year 3: 50,000 compressed characters → exponential cross-archive potential
- Year 5: 500,000 archives → virtually unlimited unique storylines

Each new death, relationship, or historical event adds to the seed pool. The storyline composer doesn't run out of content - it gains MORE material as the world ages.

### Player-Driven Content Without Player Authorship

Players don't need to write stories. They generate story potential through play:
- A grudge formed in PvP becomes a generational feud
- A guild's rise and fall becomes a lost kingdom's lore
- A player's kindness to NPCs becomes their ghost's comfort in death
- A betrayal between players becomes a cautionary tale told by bards

**The players are the authors without knowing it.**

## Encounter Memory Integration

The character-encounter system provides rich seed data via:

### Encounter Types (6 Built-In + Custom)

| Type | Storyline Potential |
|------|-------------------|
| `first_meeting` | "We've met before..." dialogue triggers |
| `conflict` | Grudge/rivalry questlines |
| `cooperation` | Alliance formation, debt/favor tracking |
| `trade` | Economic relationships, merchant connections |
| `emotional` | Deep bonds, betrayal impact multiplier |
| `significant` | Major life events, turning points |

### Weighted Sentiment Aggregation

Each character pair has aggregated sentiment across encounters:
```
sentiment = Σ(encounter.sentiment × decay(time) × significance) / count
```

This enables natural relationship evolution into storylines:
- Sentiment trending negative → potential conflict arc
- Positive sentiment with sudden negative event → betrayal storyline
- Long-term neutral with recent positive → emerging friendship

### Per-Character and Per-Pair Limits

Encounter pruning (configurable limits per character and per pair) ensures that only the MOST memorable interactions survive compression. This is feature, not bug - the composer works with concentrated narrative essence, not noise.

## Data Access Patterns

The storyline composer follows the established hybrid data access patterns:

| Data Type | Access Pattern | Rationale |
|-----------|----------------|-----------|
| **Compressed archives** | API call via lib-resource | Authoritative source, infrequent access |
| **Live snapshots** | API call via lib-resource | Must be fresh, ephemeral |
| **Character traits** | Variable Provider (cached) | Read during planning, 5-min TTL |
| **Encounter history** | Variable Provider (cached) | Read for relationship extraction, 5-min TTL |
| **Current world state** | API with batching | Consistency-critical for continuation |
| **Active storyline state** | Shared store (storyline-state) | High-frequency, owned by composer |

### StorylineVariableProvider (Proposed)

ABML behaviors can access active storyline state via a new Variable Provider:

```yaml
# In NPC behavior - check if they're part of an active storyline
condition: "${storyline.is_participant}"
action: "prioritize_storyline_goal"

# In Regional Watcher behavior - check storyline health
condition: "${storyline.active_count} > 10 and ${storyline.completion_rate} < 0.3"
action: "reduce_storyline_spawning"  # Too many stalled storylines
```

```csharp
public class StorylineVariableProvider : IVariableProvider
{
    public string Prefix => "storyline";

    public async Task<object?> GetValueAsync(string path, string entityId, CancellationToken ct)
    {
        return path switch
        {
            "is_participant" => await IsParticipantAsync(entityId, ct),
            "active_storylines" => await GetActiveStorylinesAsync(entityId, ct),
            "current_phase" => await GetCurrentPhaseAsync(entityId, ct),
            "pending_continuations" => await GetPendingContinuationsAsync(entityId, ct),
            _ => null
        };
    }
}
```

## Behavior Integration via QueryOptions

The actor service provides `/actor/query-options` which returns evaluated ABML options from actor state with preference scores. The storyline composer can use this to:

1. **Query a Regional Watcher's current interests**: "What goals does the God of Death care about right now?"
2. **Check actor compatibility with generated storylines**: "Would this NPC accept this quest role?"
3. **Get preference-weighted action selections**: "Given this situation, what would this character likely do?"

Freshness levels:
- `Cached`: Immediate, use for bulk filtering
- `Fresh`: Inject perception + wait one tick, use for critical decisions
- `Stale`: Any value, use when staleness is acceptable

## ABML Behavior Integration

Generated storylines can include ABML behavior fragments that actors execute:

```yaml
# Generated behavior fragment for "guardian ghost" role
metadata:
  type: generated_storyline_behavior
  storyline_id: "revenge-of-blacksmith-abc123"
  role: ghost_guardian

context:
  inputs:
    living_children: string[]  # Injected from storyline
    killer_entity_id: string   # From archive
    unfinished_business: string  # Extracted from backstory

flows:
  main:
    - select:
        - when: "perception:type == 'child_nearby'"
          then:
            - emit: { channel: "vocalization", value: "protective_warning" }
            - set: { feelings.protectiveness: 1.0 }

        - when: "perception:entity_id == killer_entity_id"
          then:
            - emit: { channel: "action", value: "manifest_threateningly" }
            - call: "seek_vengeance"

  seek_vengeance:
    - emit: { channel: "attention", value: "fixate_on_killer" }
    - set: { goals.primary: "confront_killer" }
```

This allows the storyline composer to output not just entity definitions but behavioral blueprints that make NPCs act consistently with their archived history.

## Example Flow

1. Character dies and is compressed.
2. God of Death detects a significant death event.
3. God calls `/storyline/compose` with the archive id and goal `tombguard`.
4. Composer outputs a plan that spawns a guardian actor and a quest chain.
5. God calls `/storyline/instantiate` to enact it.

## Authoritative Sources

> **YAML schemas in `schemas/storyline/` are the single source of truth for narrative theory.**
> This document describes architecture and integration patterns.
> For research-validated data, see the YAML files directly.

| Schema | Content |
|--------|---------|
| `narrative-state.yaml` | 10 Life Value spectrums, genre mappings, Reagan arc integration |
| `save-the-cat-beats.yaml` | 16 beats with percentage timing, dual strategies (bs2/fiction) |
| `emotional-arcs.yaml` | 6 Reagan arcs with SVD methodology, classification algorithms |
| `story-grid-genres.yaml` | 12 genres, Five Commandments, Four Core Framework, Five-Leaf Clover |
| `propp-functions.yaml` | 31 functions with variants, three-act structure, generation algorithm |
| `GAP_ANALYSIS.md` | Gap inventory, NCP integration strategy, implementation phases |

## Remaining Work

> **See `schemas/storyline/GAP_ANALYSIS.md` for detailed gap analysis and implementation phases.**

| Phase | Gap | Deliverable | Status |
|-------|-----|-------------|--------|
| 1 | NarrativeState | `narrative-state.yaml` | ✅ Complete |
| 2 | Cross-Framework Mapping | NCP function mappings | 🔄 In Progress |
| 3 | Story Actions | `story-actions.yaml` | ⏳ Pending |
| 4 | Narrative Templates | `narrative-templates.yaml` | ⏳ Pending |
| 5 | Archive Extraction | `archive-extraction.yaml` | ⏳ Pending |
| 6 | Compatibility Matrix | `compatibility-matrix.yaml` | ⏳ Pending |

**Service Implementation**:
1. Schema-defined `lib-storyline` service with endpoints above
2. StorylinePlan data model for inspection and replay
3. Live compression snapshots for living entities
4. Archive indexing for discovery across a realm
5. Intent-to-ABML compiler for generated behavior fragments

## Detailed SDK Architecture (Proposed)

### storyline-theory SDK

Pure data structures and mechanics, no service dependencies. **Loads data from YAML schemas.**

```
sdks/storyline-theory/
├── State/
│   ├── NarrativeState.cs         # 10 Life Value spectrums (from narrative-state.yaml)
│   ├── SpectrumType.cs           # Enum of spectrum types
│   └── StoryProgress.cs          # Arc completion tracking
├── Arcs/
│   ├── EmotionalArc.cs           # Reagan's 6 arcs (from emotional-arcs.yaml)
│   └── ArcClassifier.cs          # SVD-based arc classification
├── Genre/
│   ├── StoryGridGenre.cs         # 12 content genres (from story-grid-genres.yaml)
│   ├── FiveLeafClover.cs         # Meta-genre classification
│   └── FourCoreFramework.cs      # Need, Value, Emotion, Event
├── Structure/
│   ├── SaveTheCatBeats.cs        # 16 beats with timing (from save-the-cat-beats.yaml)
│   ├── ProppFunctions.cs         # 31 functions (from propp-functions.yaml)
│   └── FiveCommandments.cs       # Scene-level structure
├── Scoring/
│   ├── GenreComplianceScorer.cs  # Obligatory scenes, conventions
│   ├── PacingSatisfactionScorer.cs # Beat timing validation
│   └── FidelityScorer.cs         # How true to source material?
└── Output/
    ├── StorylinePlan.cs          # Complete plan output
    └── StorylineJson.cs          # Serialization
```

### storyline-storyteller SDK

Planning and template system. **Uses NCP (Narrative Context Protocol) as hub vocabulary for cross-framework mapping.**

```
sdks/storyline-storyteller/
├── Templates/
│   ├── NarrativeTemplate.cs      # Base template using genre + arc combination
│   ├── TemplateRegistry.cs       # Genre → Arc → Template selection
│   └── PhaseDefinition.cs        # Beat positions + spectrum targets
├── Actions/
│   ├── IStoryAction.cs           # Action interface with preconditions/effects
│   ├── ProppActionLibrary.cs     # 31 Propp functions as GOAP actions
│   ├── CommandmentActions.cs     # Five Commandments → action patterns
│   └── NCPFunctionActions.cs     # NCP 145 functions as action vocabulary
├── Planning/
│   ├── StoryPlanner.cs           # A* search in NarrativeState space
│   ├── GOAPGoal.cs               # Target NarrativeState
│   ├── UrgencyTiers.cs           # Low/Medium/High planning parameters
│   └── Replanner.cs              # Mid-story adaptation for lazy phase evaluation
├── Mapping/
│   ├── NCPHub.cs                 # Hub-and-spoke framework mapping
│   ├── ProppToNCP.cs             # Propp 31 functions → NCP functions
│   ├── STCToNCP.cs               # Save the Cat beats → NCP functions
│   └── StoryGridToNCP.cs         # Story Grid scenes → NCP functions
├── Extraction/
│   ├── ArchiveExtractor.cs       # ResourceArchive → NarrativeState
│   ├── SnapshotExtractor.cs      # Live snapshot → NarrativeState
│   └── EncounterAnalyzer.cs      # Extract relationship sentiment
└── Storyteller.cs                # Main orchestrator
```

### Storyteller Orchestrator Pattern

Following the music SDK's `Storyteller.Compose()` pattern, but using NarrativeState (10 Life Value spectrums) instead of WorldState:

```csharp
public sealed class Storyteller
{
    private readonly ProppActionLibrary _actions;
    private readonly StoryPlanner _planner;
    private readonly TemplateRegistry _templates;
    private readonly ArchiveExtractor _archiveExtractor;

    public StorylineResult Compose(StorylineRequest request)
    {
        // 1. Determine genre and primary spectrum
        var genre = request.Genre ?? InferGenreFromArchives(request.SeedSources);
        var primarySpectrum = GenreMapping.GetPrimarySpectrum(genre);

        // 2. Extract initial NarrativeState from archives
        var currentState = _archiveExtractor.ExtractState(request.SeedSources, primarySpectrum);

        // 3. Select Reagan arc based on genre affinity
        var arc = request.ArcType ?? GenreMapping.GetRecommendedArc(genre);

        // 4. Get template: Genre + Arc = phase definitions with spectrum targets
        var template = _templates.GetTemplate(genre, arc);

        // 5. Process each phase using lazy evaluation
        var sections = new List<StorylineSection>();

        foreach (var phase in template.Phases)
        {
            // Target state comes from arc shape applied to primary spectrum
            var goalState = phase.GetTargetState(primarySpectrum, arc);

            // GOAP plan finds actions to reach target
            var plan = _planner.Plan(currentState, goalState, request.Urgency);

            sections.Add(new StorylineSection
            {
                Phase = phase,
                Plan = plan,
                SpectrumDelta = goalState.GetPrimaryValue() - currentState.GetPrimaryValue()
            });

            // Apply effects for next phase
            currentState = plan.ResultingState;
        }

        return new StorylineResult
        {
            Genre = genre,
            Arc = arc,
            PrimarySpectrum = primarySpectrum,
            Sections = sections,
            Confidence = CalculateConfidence(sections)
        };
    }
}
```

## Regional Watcher Integration Pattern

Based on `REGIONAL_WATCHERS_BEHAVIOR.md`, here's how gods use the composer:

```yaml
# Regional Watcher behavior for God of Vengeance
metadata:
  type: event_brain
  domain: death_and_vengeance
  realm_ids: ["realm-omega", "realm-arcadia"]

context:
  subscriptions:
    - topic: "character.died"
    - topic: "resource.compressed"

flows:
  process_death:
    - when: "perception:type == 'character_died'"
      then:
        # Check if death was violent and unjust
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
            realm_id: "{{agent.realm_id}}"
          dry_run: true
    - when: "response.plan.confidence > 0.7"
      then:
        - call: consider_enactment

  consider_enactment:
    # God's willful decision - not automated
    - set: { memories.pending_vengeance: "response.plan_id" }
    - emit: { channel: "intention", value: "will_review_vengeance_opportunity" }
    # Later, if conditions remain favorable, call /storyline/instantiate
```

## Service Implementation Phases (Revised)

### Phase 1: SDK Foundations

1. Create `sdks/storyline-theory/` with core data structures
2. Create `sdks/storyline-storyteller/` with planning infrastructure
3. Port GOAP planner from music-storyteller (shared abstraction)
4. Define initial narrative templates (Revenge, Legacy, Mystery)
5. Unit tests for SDK components

### Phase 2: Archive Extraction

1. Implement `ArchiveExtractor` consuming `lib-resource` archives
2. Implement `BackstoryParser` for character-history data
3. Implement `EncounterAnalyzer` for character-encounter data
4. WorldState generation from extracted elements
5. Integration tests with real compressed archive formats

### Phase 3: Service Plugin

1. Define `storyline-api.yaml` schema
2. Generate `lib-storyline` plugin
3. Implement `/storyline/compose` endpoint wrapping SDK
4. Implement plan caching and deterministic replay
5. Add confidence scoring and risk analysis

### Phase 4: Instantiation

1. Implement `/storyline/instantiate` endpoint
2. Integration with `lib-character` for NPC spawning
3. Integration with `lib-actor` for behavior assignment
4. Integration with `lib-quest` (when available) for quest creation
5. Integration with `lib-scene` for location setup

### Phase 5: Live Snapshots

1. Add `/resource/snapshot/execute` to lib-resource
2. Implement `SnapshotExtractor` in storyline-storyteller SDK
3. Support mixed archive + snapshot compositions
4. TTL management for ephemeral snapshots

### Phase 6: Discovery and Indexing

1. Implement `/storyline/discover` endpoint
2. Archive indexing by realm, significance, recency
3. Cross-archive opportunity detection
4. Background service for proactive opportunity surfacing

### Phase 7: Regional Watcher Integration

1. ABML actions for storyline service invocation
2. Example watcher behaviors for common domains
3. Intent-to-ABML compiler for generated behaviors
4. Testing with actual Regional Watcher actors

## Technical Considerations

### Determinism and Reproducibility

Like the music composer, storyline generation must be deterministic when given the same inputs:

```csharp
// Deterministic plan ID from inputs
var planId = ComputeDeterministicId(
    seedSources,      // Archive/snapshot IDs
    goal,             // Requested goal type
    constraints,      // Realm, limits, etc.
    templateId        // If specified
);

// Same inputs always produce same plan
Assert.Equal(
    Compose(request1).PlanId,
    Compose(request1).PlanId
);
```

This enables:
- **Moderation**: Admins can review plans before enactment
- **Replay**: Regenerate identical plans for debugging
- **Caching**: Skip recomputation for duplicate requests
- **Testing**: Predictable outputs for unit tests

### Caching Strategy

Following the music service pattern:

```
Redis: storyline-cache
├── plan:{planId}                 # Computed StorylinePlan (TTL: 24h)
├── extraction:{archiveId}        # WorldState from archive (TTL: 1h)
└── template-match:{hash}         # Template selection results (TTL: 1h)

MySQL: storyline-plans
├── enacted plans                 # Permanent record of instantiated storylines
└── storyline analytics           # Success rates, player engagement
```

### Fidelity Scoring

How closely does the generated storyline match the source material?

| Factor | Weight | Measurement |
|--------|--------|-------------|
| Character consistency | 0.3 | Personality traits preserved |
| Relationship accuracy | 0.25 | Encounter history honored |
| Historical grounding | 0.2 | Backstory elements used |
| Thematic coherence | 0.15 | Theme matches character arc |
| Plausibility | 0.1 | World rules respected |

High fidelity (> 0.8) = Strong recommendation
Medium fidelity (0.5-0.8) = Viable with caveats
Low fidelity (< 0.5) = Creative liberty, warn caller

### Multi-Archive Composition

For storylines involving multiple archived entities:

```
POST /storyline/compose
{
  "seed_sources": [
    { "archive_id": "char-abc", "role": "protagonist" },
    { "archive_id": "char-def", "role": "antagonist" },
    { "archive_id": "realm-hist-xyz", "role": "context" }
  ],
  "goal": "generational_conflict",
  "constraints": {
    "require_shared_history": true,  # Must have common events
    "max_new_entities": 5
  }
}
```

The composer finds narrative threads connecting the archives:
- Shared historical participations
- Relationship chains (A knew B who fought C)
- Thematic parallels (both experienced loss)
- Geographical connections (same realm/location)

## Concrete Examples

### Example 1: The Blacksmith's Vengeance

**Trigger**: God of Vengeance detects high-significance death with unresolved conflict

**Archive Contents**:
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
    },
    "participations": ["guild_wars_of_212", "defense_of_goldvein"]
  },
  "character-encounter": {
    "killer_id": "char-xyz",
    "killer_sentiment": -0.9,
    "encounter_type": "conflict",
    "encounter_count": 3
  }
}
```

**Generated Plan**:
```yaml
plan_id: "revenge-kira-ironheart-abc123"
confidence: 0.87
template: "revenge_arc"
themes: [revenge, justice, legacy]

entities_to_spawn:
  - type: character
    role: ghost_npc
    seeded_from: char-abc
    properties:
      name: "Spirit of Kira Ironheart"
      behavior_template: ghost_guardian
      bound_location: "Goldvein Mine"

  - type: character
    role: quest_giver
    seeded_from: null  # New entity
    properties:
      name: "Apprentice Torval"
      relationship_to_deceased: apprentice
      motivation: "honor_master"

links:
  - type: guardian_of
    source: ghost_npc
    target: apprentice
    evidence: ["personality.loyal", "backstory.goals.protect_apprentices"]

  - type: enemy_of
    source: ghost_npc
    target: "char-xyz"  # Living character - the killer
    evidence: ["encounter.killer_sentiment"]

phases:
  - name: "discovery"
    intents:
      - spawn: ghost_npc at death_location
      - spawn: apprentice at nearby_settlement
      - establish: apprentice.quest_hook = "master_not_at_rest"

  - name: "investigation"
    intents:
      - reveal: murder_circumstances via ghost_dialogue
      - plant_clue: weapon_type at crime_scene
      - reveal: killer_identity_hint via encounter_memory

  - name: "confrontation"
    intents:
      - enable: ghost_manifestation near killer
      - provide: player_choice (forgive/avenge/deliver_to_justice)
```

### Example 2: The Lost Kingdom

**Trigger**: God of Memory detects cluster of related deaths (guild wipe)

**Archive Cluster**: 15 characters from same guild, compressed within 2-hour window

**Generated Plan**:
```yaml
plan_id: "legacy-ironforge-guild-def456"
confidence: 0.92
template: "legacy_arc"
themes: [loss, memory, rediscovery]

analysis:
  shared_elements:
    - All members of "Ironforge Vanguard" guild
    - All died in "Siege of Ashhold" historical event
    - Average sentiment toward each other: +0.7 (comrades)
    - Combined encounter count: 847 memorable interactions

  narrative_potential:
    - Guild hall location data available → lost_place theme
    - Leader had "restore guild honor" goal → unfinished_business
    - 3 members had "protect the weak" goals → guardian potential

entities_to_spawn:
  - type: location_marker
    role: "lost_guild_hall"
    properties:
      discoverable: true
      discovery_hint: "old_maps_in_tavern"

  - type: character
    role: "guild_leader_echo"
    seeded_from: highest_significance_member
    properties:
      manifestation: "memory_echo"  # Not full ghost, just imprint

  - type: item_template
    role: "guild_artifact"
    properties:
      triggers_memory_when_held: true
      memory_source: random_guild_member_archive
```

### Example 3: Cross-Archive Generational Saga

**Trigger**: Discovery endpoint identifies narrative opportunity spanning 3 generations

**Archives Involved**:
- Grandparent (compressed 2 years ago, "betrayed_by_brother")
- Parent (compressed 6 months ago, "never_learned_truth")
- Grandchild (living, snapshot shows "searching_for_family_history")

**Generated Plan**:
```yaml
plan_id: "generational-secret-ghi789"
confidence: 0.78
template: "mystery_arc"
themes: [family, secrets, reconciliation]

narrative_thread:
  discovery: "Grandchild researches family"
  hook: "Find grandparent's journal mentioning betrayal"
  investigation: "Track down great-uncle (the betrayer)"
  revelation: "Betrayer had reasons (protecting family from greater threat)"
  choice: "Condemn or forgive; affects family legacy"

cross_archive_connections:
  - grandparent.trauma.betrayed_by_brother → mystery_hook
  - parent.backstory.fears.family_shame → emotional_stakes
  - grandchild.goals.understand_heritage → player_motivation
```

## Vision: The Living World

When fully implemented, Bannou worlds become **living narratives**:

1. **Every death matters**: Compressed characters become story seeds
2. **History accumulates**: Older worlds have richer narrative potential
3. **Connections emerge**: Cross-archive analysis finds unexpected links
4. **Gods curate**: Regional Watchers select and shape which stories manifest
5. **Players participate**: Actions create future archives for future stories

This is not procedural generation. It's **emergent narrative archaeology** - unearthing the stories that players unknowingly wrote through play.

The storyline composer doesn't invent stories. It discovers the stories that already exist in the accumulated history of the world, and gives them new life.

---

## Architectural Decisions Summary

| Decision | Choice | Rationale |
|----------|--------|-----------|
| **NarrativeState Model** | 10 Life Value spectrums | Story Grid Four Core Framework (Coyne, 2015-2020); grounded in Maslow's hierarchy |
| **Temporal Dynamics** | Reagan's 6 emotional arcs | SVD-derived shapes (Reagan et al., 2016); applied to primary spectrum |
| **Cross-Framework Mapping** | NCP as hub vocabulary | 145 narrative functions enable Propp↔STC↔StoryGrid↔Reagan translation |
| **Service Layer** | L4 (Application / Thin Orchestration) | Alongside lib-quest; driven by Behavioral Intelligence Layer |
| **SDK Pattern** | Two-tier (storyline-theory, storyline-storyteller) | Theory loads YAML; Storyteller does planning |
| **Planning Approach** | GOAP with urgency-tiered parameters | A* search in NarrativeState space |
| **Phase Generation** | Lazy evaluation via continuation points | World changes while players offline; fresh state for each phase |
| **Archive Access** | API via lib-resource | Authoritative source; extract to NarrativeState |
| **Instantiation** | Delegates to existing services | lib-quest, lib-actor, lib-character-encounter, lib-contract |
| **Determinism** | Hash-based plan IDs from inputs | Enables caching, replay, moderation |

## Implementation Phases (Summary)

1. Phase 1. SDK Foundations (storyline-theory, storyline-storyteller)
2. Phase 2. Archive Extraction (ArchiveExtractor, BackstoryParser, EncounterAnalyzer)
3. Phase 3. Service Plugin (lib-storyline with /compose endpoint)
4. Phase 4. Instantiation (entity spawning, behavior assignment)
5. Phase 5. Live Snapshots (non-destructive compression for living entities)
6. Phase 6. Discovery and Indexing (proactive opportunity surfacing)
7. Phase 7. Regional Watcher Integration (god behavior patterns)
8. Phase 8. Continuation System (lazy phase evaluation, world delta detection)

---

## Related Documents

### Authoritative YAML Schemas (in `schemas/storyline/`)
- `narrative-state.yaml` - 10 Life Value spectrums, genre mappings, Reagan arc integration
- `save-the-cat-beats.yaml` - 16 beats with percentage timing, dual strategies
- `emotional-arcs.yaml` - 6 Reagan arcs with SVD methodology
- `story-grid-genres.yaml` - 12 genres, Five Commandments, Four Core Framework
- `propp-functions.yaml` - 31 functions with variants, generation algorithm
- `GAP_ANALYSIS.md` - Gap inventory, NCP integration strategy, implementation phases

### Research Summaries (in `docs/research/`)
- `FOUR-CORE-FRAMEWORK.md` - Story Grid's authoritative model
- `FIVE-LEAF-GENRE-CLOVER.md` - Complete genre classification
- `EMOTIONAL-ARCS-SVD-METHODOLOGY.md` - Reagan et al. sentiment analysis
- `NARRATIVE-CONTEXT-PROTOCOL.md` - NCP hub schema for cross-framework mapping
- `PROPPER-IMPLEMENTATION.md` - Propp generation algorithm details

### Architecture Documents
- `docs/plugins/ACTOR.md` - Actor deep dive (data access pattern selection)
- `docs/reference/SERVICE-HIERARCHY.md` - Variable Provider Factory pattern
- `docs/planning/COMPRESSION-GAMEPLAY-PATTERNS.md` - Archive structure as narrative seeds
- `docs/planning/REGIONAL_WATCHERS_BEHAVIOR.md` - God actor patterns
- `docs/plugins/RESOURCE.md` - Compression infrastructure
- `docs/plugins/MUSIC.md` - SDK layering precedent
- `sdks/music-storyteller/` - Reference implementation for GOAP
