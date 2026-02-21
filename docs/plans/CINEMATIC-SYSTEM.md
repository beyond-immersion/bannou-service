# Cinematic & Scenario System - Architectural Plan

> **Status**: Draft
> **Created**: 2026-02-17
> **Revised**: 2026-02-17
> **Author**: Claude (analysis) + Lysander (direction)
> **Category**: Multi-SDK + Plugin plan
> **Prior Art**: `docs/planning/CINEMATIC-RESEARCH-ANALYSIS.md` (research cards), `docs/planning/CINEMATIC-THEORY-RESEARCH.md` (raw research)
> **Related**: `sdks/scene-composer/` (spatial stage), `sdks/behavior-compiler/` (ABML compilation), `sdks/storyline-theory/`, `sdks/storyline-storyteller/`, `plugins/lib-storyline/`

---

## Executive Summary

The cinematic system fills the last architectural gap in Bannou: **cinematic composition**. The runtime already exists (~3,800 lines: CinematicInterpreter, CinematicRunner, CutsceneSession, InputWindowManager). What's missing is the authoring and composition pipeline that produces the ABML documents the runtime executes.

**The approach: hand-authoring first, procedural generation later.** Designers author cinematic scenarios in a timeline tool with tracks, sync barriers, and QTE windows. Procedural generation (GOAP-driven auto-composition from academic cinematography research) is a future additive layer that produces the exact same scenario format -- the runtime and plugin don't know or care about the source.

**This also establishes a new cross-domain pattern: the scenario registry.** Both storyline and cinematic systems share the concept of hand-authored scenario templates with trigger conditions and passive discovery. Extracting the pattern into standalone SDKs creates consistency, enables client-side authoring tools, and clarifies a critical design boundary: **plugins are passive registries, god-actors are the intelligence layer.**

---

## The Three Principles

### 1. Plugins Are Passive Registries

Scenario plugins (lib-storyline, lib-cinematic) are **content registries with mechanical condition matching**. They store scenarios, evaluate trigger conditions against provided context, track active instances, and manage distributed safety (locks, cooldowns, idempotency).

Plugins **do not** rank, recommend, or judge which scenario is "best" for a given situation. When a god-actor calls `find-available` with a context snapshot, the plugin returns **everything whose trigger conditions are mechanically satisfied**. The god-actor decides which one to trigger.

This means:
- `find-available` is a **filter**, not a ranker
- Fit scoring, personality weighting, and narrative appropriateness live in **actor behaviors** (ABML expressions) or **storyteller SDKs** (compositional intelligence), never in the plugin
- The plugin's condition evaluator is a predicate engine: conditions met / not met, with actual values for diagnostics

### 2. God-Actors Provide Judgment

The decision of which scenario to trigger -- and when, and for whom -- belongs to the actor/puppetmaster/gardener layer. A divine regional watcher might:

1. Perceive an encounter opportunity (two characters with history, in a dramatic location)
2. Call `find-available` with the encounter context
3. Get back 5 matching cinematic scenarios
4. Evaluate them using its own GOAP planning (narrative arc fit, personality alignment, recent encounter history, dramatic pacing)
5. Call `trigger` on the selected one

The plugin never goes looking for characters or situations. It is **asked** and it **answers**.

### 3. Format Agnosticism

Hand-authored and procedurally generated scenarios produce the **exact same document format**. The plugin, the runtime, and the client treat both identically. The source (designer's timeline tool vs. GOAP planner) is metadata, not a structural distinction.

This enables a natural development progression:
- **Now**: Hand-authored scenarios prove the format, the bridge, and the runtime
- **Later**: Procedural generation SDKs produce additional scenarios in the same format
- **Eventually**: The scenario library contains both, and the god-actors choose from the combined pool

---

## The Unified Scenario Pattern

Both storyline and cinematic systems follow the same structural pattern:

```
PER DOMAIN:

  {domain}-scenario SDK                  {domain}-theory SDK
  "Hand-author content"                  "Formal domain grammar"
  - Scenario definition format           - Pure computation
  - Trigger condition model              - Deterministic
  - Mechanical condition evaluation      - No service dependencies
  - Domain-specific content model
  - Validation                           {domain}-storyteller SDK
  - ABML export (cinematic) or           "Auto-compose scenarios"
    execution model (storyline)          - GOAP planning
  - Usable by client authoring tools     - Uses theory SDK
                                         - Produces SAME format as
          |                                hand-authored scenarios
          |  both produce same format              |
          |                                        |
     +----+----------------------------------------+
     |
  lib-{domain} plugin (L4, thin)
  - Scenario registry (CRUD, deprecation)
  - Passive discovery (find-available: mechanical filter)
  - Trigger execution with distributed safety
  - Active instance tracking
  - Persistent templates + ephemeral instances (flag, not separate paths)
  - Caching, cooldowns, idempotency, cleanup
  - Doesn't care about source
```

Applied to both domains:

| Component | Storyline | Cinematic |
|-----------|-----------|-----------|
| **Scenario SDK** | `storyline-scenario` (extract from plugin) | `cinematic-composer` (new) |
| **Content model** | Conditions, phases, mutations, quest hooks | Timeline tracks, clips, sync points, QTE windows, camera |
| **Theory SDK** | `storyline-theory` (exists) | `cinematic-theory` (future -- camera + movement) |
| **Storyteller SDK** | `storyline-storyteller` (exists, output format changes) | `cinematic-storyteller` (future) |
| **Plugin** | `lib-storyline` (exists, 15 endpoints) | `lib-cinematic` (new, ~8 endpoints) |

### What the Scenario SDKs Own

- **Typed data model**: Scenario definitions, trigger conditions, domain-specific content (phases/timeline)
- **Condition evaluator**: Binary predicate matching -- does the provided context satisfy the trigger conditions?
- **Structural validator**: Is the scenario internally consistent? (timing conflicts, unreachable sync points, etc.)
- **Serialization**: YAML/JSON round-trip for authoring tools
- **Export** (cinematic only): Scenario → multi-channel ABML document

### What the Scenario SDKs Do NOT Own

- **Fit scoring / ranking**: No `FitScorer` class. Mechanical condition counting at most (how many conditions matched), never semantic judgment (is this a "good" scenario for this character)
- **Persistence**: The plugin handles MySQL/Redis storage
- **Distributed safety**: The plugin handles locks, cooldowns, idempotency
- **Intelligence**: The actor system decides what to trigger

### Persistent vs Ephemeral Scenarios

Both plugins support two lifecycle modes via a simple flag:

| Mode | Source | Storage | Reusable | Example |
|------|--------|---------|----------|---------|
| **Persistent** | Hand-authored or curated procedural output | MySQL + Redis cache | Yes, versioned with ETag | "Enraged character throws props at nimble dodger" template |
| **Ephemeral** | One-shot procedural composition | Redis only, TTL-based | No, consumed once | Procedurally generated encounter for specific characters/context |

The plugin code path is identical. The flag determines storage backend and cleanup behavior.

---

## Resolved Design Decisions

### D1: Hand-Authoring First, Procedural Generation Later

The prior research analysis proposed implementing 5+ SIGGRAPH papers (EMOTE, Toric Space, DCCL, Facade, Resonator) as Phase 1. This is inverted. You don't need to understand the thematic implications of a rock being thrown to make a QTE for throwing and dodging one.

**Phase 1**: Hand-authored cinematic scenarios with a timeline/track authoring format. The trigger conditions encode what capabilities are needed; the timeline encodes what happens.

**Future**: Procedural generation SDKs that auto-compose scenarios using formal cinematography theory. These produce the same format as hand-authored scenarios. The plugin treats both identically.

### D2: Plugins Are Passive Registries (No Ranking)

The current storyline `evaluate-fit` endpoint computes weighted fit scores from trait matches, backstory alignment, relationship state, location context, and world-state bonuses. This scoring logic does not belong in the plugin or the scenario SDK.

**Mechanical condition matching**: Belongs in the scenario SDK. "Does this character have tag X? Is this character in location Y?"

**Semantic scoring**: Belongs in the god-actor's ABML behavior model or the storyteller SDK. "Would this be a good story for this character given their personality and recent experiences?"

The `evaluate-fit` endpoint either becomes a thin condition-match counter or is removed entirely in favor of `find-available` returning all mechanically matching scenarios.

### D3: Scene-Composer Provides the Stage, Cinematic-Composer Adds Time

The existing scene-composer SDK is purely spatial: scene graphs, transforms, nodes, affordances. It provides the 3D set. The cinematic-composer SDK adds the temporal dimension: tracks, clips, timing, sync points, QTE windows. Cinematic-composer depends on scene-composer for spatial primitives (Vector3, Transform) and references scene elements as the physical environment.

### D4: Storyline-Scenario SDK Extracted First for Consistency

lib-storyline already has scenario endpoints but the scenario format lives as serialized JSON blobs in `ScenarioDefinitionModel` (`TriggerConditionsJson`, `PhasesJson`, `MutationsJson`, `QuestHooksJson`). Extracting these into a typed SDK is a **design phase**, not just a code move -- it defines the type system for what was previously untyped JSON. The typed models established here become the pattern template for cinematic-composer.

### D5: No FitScorer in Scenario SDKs

The scenario SDKs provide:
- `ConditionEvaluator` -- binary predicate matching with diagnostics (met/not-met, actual vs expected values)
- `ScenarioMatcher` -- finds all scenarios whose conditions are satisfiable (the filter)

The scenario SDKs do NOT provide:
- `FitScorer` -- removed from the design entirely at the SDK level
- Any form of ranking, recommendation, or "best match" logic

If a caller needs to count how many conditions were satisfied (for rough prioritization), the `ConditionEvaluator` returns per-condition results that the caller can count. The SDK does not assign meaning to that count.

### D6: One Theory SDK with Two Domains, Two Test Suites

The research analysis proposed a massive CinematicTheory SDK (3-4x larger than MusicTheory). Instead: one SDK (`cinematic-theory`) with two clearly independent computational domains -- **camera** (Toric Space, DCCL modules, film idioms, Murch scoring) and **movement** (EMOTE Effort/Shape, Laban transforms). Each domain has its own test suite. They share zero internal dependencies.

### D7: The Research Remains Valuable -- It's the Future Roadmap

CINEMATIC-RESEARCH-ANALYSIS.md and the 9 research cards are the roadmap for the procedural generation tier. The academic foundations (EMOTE, Toric Space, DCCL, Facade, Resonator, Reagan arcs, Murch) become relevant when auto-composition is built. The analysis's design decisions (HFSM for camera, GOAP for choreography, three independent layers, greedy selection) remain correct for that future phase.

### D8: Storyline Recomposition (Future)

The storyline-storyteller SDK currently produces `StorylinePlan` objects (narrative arc plans with phases and actions). In the unified model, it should produce `ScenarioDefinition` objects -- the same format as hand-authored scenarios. This is a breaking change to the storyteller SDK's output contract, but there is no backwards compatibility concern (endpoints are unused).

This recomposition is future work, not part of the near-term phases. The immediate priority is extracting the scenario SDK (Phase 0) and building the cinematic system (Phases 1-3). The storyline recomposition can happen once the cinematic system validates the pattern.

---

## Component Overview

### storyline-scenario SDK (Phase 0 -- Extract from lib-storyline)

**Location**: `sdks/storyline-scenario/`
**Purpose**: Portable scenario definition format, typed condition model, and mechanical condition evaluation. Usable by client-side authoring tools without importing plugin dependencies.
**Dependencies**: None (pure data model + evaluation logic)
**Details**: See [Phase 0 Plan](CINEMATIC-PHASE-0-SCENARIO-SDK.md)

### cinematic-composer SDK (Phase 1 -- New)

**Location**: `sdks/cinematic-composer/`
**Purpose**: Timeline/track authoring format for cinematic scenarios. The temporal complement to scene-composer's spatial editing. Provides the data model, validation, and ABML export that client-side authoring tools and the runtime both use.
**Dependencies**: `sdks/scene-composer/` (for Vector3, Transform, and affordance types)
**Details**: See [Phase 1 Plan](CINEMATIC-PHASE-1-COMPOSER-SDK.md)

### lib-cinematic Plugin (Phase 2 -- New, L4 GameFeatures)

**Location**: `plugins/lib-cinematic/`
**Purpose**: Thin scenario registry + execution layer following the established plugin pattern.
**Dependencies**: L0/L1/L2 hard; L3/L4 soft with graceful degradation
**Details**: See [Phase 2 Plan](CINEMATIC-PHASE-2-PLUGIN.md)

### cinematic-theory SDK (Future)

**Location**: `sdks/cinematic-theory/`
**Purpose**: Formal grammar for procedural cinematography. Two independent computational domains (camera, movement) in one SDK.
**Dependencies**: None (pure computation, like MusicTheory)

**Domain A: Camera** -- Toric Space geometric solver, DCCL camera modules (apex, external, internal, track, follow), HFSM film idioms (duel, group fight, chase, ambush), Murch Rule of Six scoring, FEP editing patterns, Darshak rule extractions.

**Domain B: Movement** -- EMOTE Effort/Shape transformation pipeline, Laban four-axis parameterization, eight named efforts.

### cinematic-storyteller SDK (Future)

**Location**: `sdks/cinematic-storyteller/`
**Purpose**: GOAP-driven auto-composition. Takes encounter context and produces cinematic-composer scenarios procedurally.
**Dependencies**: `cinematic-theory` (camera + movement grammars), `cinematic-composer` (output format)

Key design decisions preserved from research analysis:
- HFSM for camera, GOAP for choreography
- Facade's greedy selection (one exchange ahead, not the entire fight)
- Resonator + Reagan unified (Reagan labels as vocabulary, Resonator parameters as generation mechanism)
- C.R.A. (Cue-Reaction-Action) as the atomic choreographic unit
- Three independent layers: Structure (what happens), Quality (how it looks), Presentation (how it's shown)

---

## Implementation Phases

| Phase | Name | Goal | Plan |
|-------|------|------|------|
| **0** | Scenario SDK Pattern | Extract storyline-scenario SDK, define the typed condition model, prove the pattern | [Phase 0](CINEMATIC-PHASE-0-SCENARIO-SDK.md) |
| **1** | ABML Bridge + Cinematic Composer | Prove the runtime bridge with hand-authored ABML, then build the authoring SDK | [Phase 1](CINEMATIC-PHASE-1-COMPOSER-SDK.md) |
| **2** | lib-cinematic Plugin | Schema-first plugin following the established pattern | [Phase 2](CINEMATIC-PHASE-2-PLUGIN.md) |
| **3** | Integration Wiring | Connect to actor/puppetmaster/gardener systems, progressive agency | [Phase 3](CINEMATIC-PHASE-3-INTEGRATION.md) |
| **4** | cinematic-theory SDK | Formal camera + movement grammar (future) | This document |
| **5** | cinematic-storyteller SDK | GOAP-driven procedural composition (future) | This document |
| **6** | Storyline Recomposition | Update storyline-storyteller output format to produce scenario definitions (future) | This document |

Phases 0-3 are near-term and have individual plan documents. Phases 4-6 are future work documented here as roadmap.

---

## Relationship to Existing Infrastructure

### Existing Runtime Stack (Unchanged)

| Component | Location | Lines | Role |
|-----------|----------|-------|------|
| CinematicInterpreter | `sdks/behavior-compiler/Runtime/` | ~400 | Streaming bytecode execution with continuation points |
| CinematicRunner | `plugins/lib-behavior/Runtime/` | ~515 | Entity control acquisition, lifecycle management |
| CutsceneCoordinator | `plugins/lib-behavior/Coordination/` | ~243 | Multi-session orchestration |
| CutsceneSession | `plugins/lib-behavior/Coordination/` | ~347 | Sync points + input windows |
| InputWindowManager | `plugins/lib-behavior/Coordination/` | ~502 | QTE/choice management with timeouts |
| ICutsceneCoordinator | `bannou-service/Behavior/` | Interface | Session creation, management, cleanup |
| IBehaviorDocumentProvider | `bannou-service/Providers/` | Interface | Priority-based behavior document loading (100=dynamic, 50=seeded, 0=fallback) |

The cinematic-composer SDK's `AbmlExporter` must produce multi-channel ABML that compiles to bytecode compatible with this stack. **This is the critical integration point validated in Phase 1.**

### Scene-Composer SDK (Spatial Stage)

Cinematic-composer depends on scene-composer for:
- `Vector3`, `Transform`, `Quaternion` -- spatial primitives
- `ComposerSceneNode.Affordances` -- what scene elements can be interacted with
- `ComposerSceneNode.MarkerType` -- CameraPoint, TriggerPoint, SpawnPoint
- `ComposerSceneNode.AttachmentPoints` -- where props attach to characters

### Behavior-Compiler SDK (Compilation)

Handles ABML YAML → AST → bytecode compilation. The cinematic-composer's `AbmlExporter` produces ABML YAML. The behavior-compiler compiles it. No modifications expected unless the exported ABML uses constructs the compiler doesn't yet support (validated in Phase 1).

### Storyline System (Pattern Source)

lib-storyline's scenario subsystem is the direct template for lib-cinematic:

| lib-storyline Pattern | lib-cinematic Equivalent |
|----------------------|--------------------------|
| `ScenarioDefinitionModel` (MySQL) | Cinematic scenario template (MySQL) |
| `find-available` (conditions vs character state) | `find-available` (conditions vs encounter context) |
| `trigger` (execute with lock + cooldown + idempotency) | `trigger` (bind entities, compile, execute) |
| `get-active` / `get-history` | `get-active` (running cinematics) |
| Cooldown markers (Redis TTL) | Cooldown markers (Redis TTL) |
| Exclusivity tags (mutual exclusion) | Exclusivity tags (mutual exclusion) |

---

## Event Brain → lib-cinematic → Runtime Flow

```
Event Brain (Actor, event_brain type)
    | detects encounter opportunity
    |
    |--- service_call: /cinematic/find-available
    |    {participants, location_id, capabilities, spatial_state}
    |
    |--- God-actor evaluates returned scenarios using GOAP
    |    (narrative arc fit, personality alignment, dramatic pacing)
    |
    |--- service_call: /cinematic/trigger
    |    {scenario_id, participant_bindings, location_id}
    |
    v
lib-cinematic (L4)
    | 1. Load scenario template from MySQL/Redis cache
    | 2. Bind concrete entities to participant slots
    | 3. Export scenario to multi-channel ABML via cinematic-composer SDK
    | 4. Compile ABML to bytecode via behavior-compiler
    | 5. Cache compiled BehaviorModel in Redis (deterministic seed)
    | 6. Register with IBehaviorDocumentProvider chain (priority 75)
    | 7. Return cinematic reference to caller
    |
    v
Event Brain receives cinematic reference
    | emit_perception to participants: "cinematic starting"
    |
    v
CinematicRunner (lib-behavior, on game server or actor pool)
    | 1. Load compiled BehaviorModel via IBehaviorDocumentProvider chain
    | 2. Create CinematicInterpreter with continuation points
    | 3. Acquire entity control via ControlGateManager
    | 4. Create CutsceneSession via CutsceneCoordinator
    | 5. Evaluate frame-by-frame
    |    - DomainActions → IClientCutsceneHandler
    |    - Sync points → SyncPointManager
    |    - Continuation points → InputWindowManager (if QTE-activated)
    | 6. On completion: return entity control, publish events
```

---

## Open Questions

1. **ABML multi-channel branching**: Does the behavior-compiler's existing branch node implementation support QTE-triggered branching across multiple channels (e.g., dodger branches to `segment_dodge_left` while camera branches to matching camera segment)? **Must be validated in Phase 1 before building the SDK.**

2. **Scene-composer dependency scope**: Should cinematic-composer depend on the full scene-composer SDK, or extract shared spatial types into a shared math package? Recommend: depend on scene-composer directly for now, extract if a third consumer appears.

3. **Scenario template versioning**: When a hand-authored scenario is updated, should running instances continue with the old version? lib-storyline's pattern (ETag-based optimistic concurrency on definitions, running instances keep their snapshot) is likely correct.

4. **Trigger evaluation frequency**: Event-driven (actor perceives opportunity, calls find-available) vs. periodic sweep. Event-driven matches the actor system's perception model and is more efficient. Recommend: event-driven only.

5. **Cross-channel branch correlation**: If QTE outcome in one channel must select the corresponding branch in another channel, does this require a new ABML construct or can it be expressed with existing sync points? Phase 1 bridge validation will answer this.

---

## Relationship to Prior Planning Documents

| Document | Status | Disposition |
|----------|--------|-------------|
| `docs/planning/CINEMATIC-RESEARCH-ANALYSIS.md` | Preserved (future roadmap) | Research cards and design decisions are the roadmap for Phases 4-5. Not near-term. |
| `docs/planning/CINEMATIC-THEORY-RESEARCH.md` | Preserved (reference) | Raw research compilation for future use. |

---

*This document is the master architectural plan for the cinematic and scenario system. For implementation details, see the individual phase plans. For the procedural generation research, see `docs/planning/CINEMATIC-RESEARCH-ANALYSIS.md`.*
