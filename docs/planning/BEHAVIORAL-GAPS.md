# Behavioral Gaps: What the Behavior System Must Become

> **Type**: Gap Analysis
> **Status**: Active
> **Created**: 2026-03-10
> **Last Updated**: 2026-03-10
> **North Stars**: #1, #2, #3, #5
> **Related Plugins**: Behavior, Actor, Puppetmaster, Cinematic, Director, Agency, Broadcast
> **Source Documents**: VISION.md, PLAYER-VISION.md, VIDEO-DIRECTOR.md, COMPOSITIONAL-CINEMATICS.md, DEVELOPER-STREAMS.md, BEHAVIOR-COMPOSITION.md, CINEMATIC-SYSTEM.md, ABML-GOAP-OPPORTUNITIES.md, BEHAVIORAL-BOOTSTRAP.md, ACTOR-BOUND-ENTITIES.md

## Executive Summary

The Bannou behavior system — ABML compiler, GOAP planner, Actor runtime, and Puppetmaster orchestration — is the keystone of every north star. It powers NPC autonomy (#1), drives the content flywheel (#2), must scale to 100,000+ agents (#3), and enables all emergent systems (#5). The core infrastructure is **solid and implemented**: a multi-phase compiler producing 58+ opcodes, an A*-based GOAP planner, a 5-stage cognition pipeline, 13 variable provider namespaces, continuation point opcodes, and a distributed Actor pool architecture.

However, the planning documents (VIDEO-DIRECTOR, COMPOSITIONAL-CINEMATICS, DEVELOPER-STREAMS, BEHAVIOR-COMPOSITION) reveal that the behavior system must evolve from "NPC brain" to **universal orchestration substrate** — powering combat choreography, cinematic composition, music-driven video generation, developer stream directing, god-actor world orchestration, and the content flywheel. The distance between what exists and what is required is substantial.

This document catalogs every identified gap, organized by architectural category. Each gap cites the planning document(s) that require it, the current implementation state, and any existing GH issues.

---

## Table of Contents

1. [ABML Language Gaps](#1-abml-language-gaps)
2. [GOAP Planner Gaps](#2-goap-planner-gaps)
3. [Behavior Composition & Scale Gaps](#3-behavior-composition--scale-gaps)
4. [Variable Provider Gaps](#4-variable-provider-gaps)
5. [Cinematic System Gaps](#5-cinematic-system-gaps)
6. [Director & Orchestration Gaps](#6-director--orchestration-gaps)
7. [Video Director Gaps](#7-video-director-gaps)
8. [Developer Streams Gaps](#8-developer-streams-gaps)
9. [Actor Runtime Gaps](#9-actor-runtime-gaps)
10. [Puppetmaster & Watcher Gaps](#10-puppetmaster--watcher-gaps)
11. [Content Flywheel Integration Gaps](#11-content-flywheel-integration-gaps)
12. [Cross-Cutting Behavioral Gaps](#12-cross-cutting-behavioral-gaps)

---

## 1. ABML Language Gaps

### 1.1 Template Inheritance (`extends` / `abstract`)

**Required by**: BEHAVIORAL-BOOTSTRAP (god-actor skeletons), ACTOR-BOUND-ENTITIES (cognitive stage progression), DEVELOPER-STREAMS (directing behavior variants)

**Current state**: Not implemented. Every behavior document is standalone. No inheritance, no abstract flows, no override semantics.

**Impact**: Regional watchers, encounter coordinators, and dungeon cores all share common patterns (event subscription, periodic scanning, lifecycle management). Without inheritance, every game must rewrite these patterns from scratch. God behavior skeletons (Moira, Thanatos, Silvanus, Ares, Typhon, Hermes) cannot share a base template.

**GH Issue**: #384

### 1.2 Channel Signaling Not Compiled (`emit` / `wait_for` / `sync`)

**Required by**: COMPOSITIONAL-CINEMATICS (multi-character synchronization), BEHAVIOR-COMPOSITION (component linking), CINEMATIC-SYSTEM (CutsceneSession sync barriers)

**Current state**: Action nodes are defined in the AST (`EmitAction`, `WaitForAction`, `SyncAction`), but the SDK analysis confirms these are **not yet compiled to bytecode**. They are parsed but produce no opcodes.

**Impact**: Multi-character scene coordination requires channel synchronization. The anime-production paradigm (COMPOSITIONAL-CINEMATICS) relies on independent character performances synchronizing at "same-cel" moments via sync barriers. Without compiled channel ops, this synchronization must happen entirely outside ABML — defeating the purpose of declarative behavior composition.

**GH Issue**: None

### 1.3 Economic Action Handlers

**Required by**: VISION (NPC-driven economy), ABML-GOAP-OPPORTUNITIES (faction/economy simulation), VISION (Hermes/Commerce god-actor)

**Current state**: Not implemented. NPCs cannot interact with the economy from ABML. No `economy_credit`, `economy_debit`, `inventory_add`, `inventory_has`, or `market_query` handlers exist.

**Impact**: The economy must be NPC-driven (VISION: "the economy must be NPC-driven, not player-driven"). Without economic action handlers, NPCs cannot buy, sell, craft, or trade. The entire NPC economic substrate that player economies should layer on top of doesn't exist.

**GH Issue**: #428

### 1.4 Game Engine Signaling (`emit_impulse`)

**Required by**: CINEMATIC-SYSTEM (choreographic actions delivered to game engine), COMPOSITIONAL-CINEMATICS (behavior-to-renderer signaling), VIDEO-DIRECTOR (execution layer)

**Current state**: Not implemented. No mechanism for ABML behaviors to send immediate signals to the game engine. Domain actions exist but are runtime-delegated, not compiled to specific signaling opcodes.

**Impact**: Combat choreography, cinematic rendering, and any real-time visual feedback from ABML behaviors requires the game engine to receive action signals. Without `emit_impulse`, the entire execution layer of cinematics is disconnected from the renderer.

**GH Issue**: #408

### 1.5 Limited Collection/Array Support

**Required by**: BEHAVIOR-COMPOSITION (component selection from registries), DEVELOPER-STREAMS (feed list management), VIDEO-DIRECTOR (character casting from criteria lists)

**Current state**: `in` operator supports max 16 static literal elements only. No standalone array literals. No dynamic array construction, filtering, or mapping at the bytecode level (expression system has `| length`, `| map('field')` but these operate in the expression evaluator, not the stack VM).

**Impact**: Any ABML behavior that needs to work with dynamic lists (available components, active feeds, candidate characters) is limited to expression-level operations. Complex list processing must be delegated to service calls.

**GH Issue**: None

### 1.6 No String Operations in Bytecode VM

**Required by**: VIDEO-DIRECTOR (template name matching, theme selection), DEVELOPER-STREAMS (file path context inference, build output parsing)

**Current state**: The stack VM operates on doubles exclusively. String operations exist in the expression evaluator (`upper`, `lower`, `trim`, `split`, `join`, `format`) but not in the bytecode instruction set. `PushString` exists but primarily for output/logging.

**Impact**: Any behavior that needs to process string data (parse file paths, match patterns, format messages) must route through the expression evaluator, which adds overhead and limits composability with stack operations.

**GH Issue**: None

### 1.7 No Formal Error Recovery in ABML Flows

**Required by**: BEHAVIORAL-BOOTSTRAP (god-actor resilience), ACTOR-BOUND-ENTITIES (entity lifecycle management)

**Current state**: Document-level `on_error` flow exists. Action-level `on_error` blocks exist for domain actions. But there's no `try/catch` equivalent in ABML control flow, no error propagation model, and no retry semantics.

**Impact**: God-actors running for days/weeks must be resilient to transient failures (service calls failing, state inconsistencies, network partitions). Without structured error recovery, a single failed API call in a god-actor's scanning loop could halt orchestration.

**GH Issue**: None

---

## 2. GOAP Planner Gaps

### 2.1 Silent Failure (No Diagnostics)

**Required by**: All GOAP consumers (behavior, cinematic, narrative, music, video director)

**Current state**: `PlanAsync` returns `null` for multiple failure modes (timeout, node limit, no path, no actions). Callers cannot distinguish why planning failed. Response hardcodes `PlanningTimeMs = 0`, `NodesExpanded = 0` on failure — actual search statistics are discarded.

**Impact**: Debugging GOAP behavior across 100K agents is impossible without failure diagnostics. A god-actor failing to plan a narrative intervention provides zero information about why.

**GH Issues**: #575, #625

### 2.2 No Parallel Plan Evaluation

**Required by**: BEHAVIOR-COMPOSITION (multi-goal NPCs with competing priorities), VISION (NPCs pursuing multiple simultaneous aspirations)

**Current state**: GOAP evaluates one goal at a time. Multi-goal NPCs must plan sequentially, selecting the best plan from results.

**Impact**: NPCs with multiple active goals (eat, trade, socialize, fulfill obligations) require sequential planning. At 100-500ms cognitive ticks with 3-5 active goals, planning alone could consume the entire tick budget.

**GH Issue**: #620

### 2.3 No Plan Persistence or History

**Required by**: ABML-GOAP-OPPORTUNITIES (plan analysis for tutorials/quest generation), VIDEO-DIRECTOR (cinematic plan storage for deterministic replay)

**Current state**: GOAP metadata (goals, actions) is cached per behavior in Redis. But plan results — what the planner actually produced — are ephemeral. No query endpoint exists to retrieve past plans.

**Impact**: Tutorial adaptation (observing what plans NPCs generate), quest generation (inverse GOAP from interesting plans), and deterministic cinematic replay all require plan persistence.

**GH Issue**: #608

### 2.4 No Plan Visualization

**Required by**: ABML-GOAP-OPPORTUNITIES (developer tooling), debugging at scale

**Current state**: GOAP metadata is stored but no endpoint exposes it for visualization. Plan execution paths are invisible.

**Impact**: Behavior debugging for 100K agents requires visual tools. Without plan visualization, developers cannot understand why NPCs make specific decisions.

**GH Issue**: #619

### 2.5 No GOAP Domain Expansion

**Required by**: ABML-GOAP-OPPORTUNITIES (9 expansion domains), VISION (GOAP as universal planner)

**Current state**: GOAP works for NPC behavioral planning only. No domain-specific GOAP applications exist for:

| Domain | Status | Source Requirement |
|--------|--------|-------------------|
| Combat choreography | Not started | CINEMATIC-SYSTEM, COMPOSITIONAL-CINEMATICS |
| Narrative composition | Exists in Storyline SDK | VISION (content flywheel) |
| Music composition | Exists in MusicStoryteller SDK | VISION (music generation) |
| Adaptive tutorials | Not started | ABML-GOAP-OPPORTUNITIES |
| Procedural quest generation | Not started | ABML-GOAP-OPPORTUNITIES |
| Social dynamics | Not started | ABML-GOAP-OPPORTUNITIES |
| Faction/economy simulation | Not started | ABML-GOAP-OPPORTUNITIES |
| Dialogue evolution | Not started | ABML-GOAP-OPPORTUNITIES |
| Cinematic video composition | Not started | VIDEO-DIRECTOR |

**Impact**: GOAP being the "universal planner" (VISION Design Principle #6) requires domain-specific action sets, world state schemas, and goal definitions for each domain. The planner algorithm exists; the domain content does not.

**GH Issue**: None (individual domain tickets may exist)

---

## 3. Behavior Composition & Scale Gaps

### 3.1 No Component Registry

**Required by**: BEHAVIOR-COMPOSITION (core architecture), COMPOSITIONAL-CINEMATICS (fingerprinted components as "cels"), VIDEO-DIRECTOR (combat/social component selection)

**Current state**: No `IComponentRegistry` interface exists. No Redis-backed component metadata store. Behaviors are stored by content hash in the Asset service but cannot be discovered by type, tags, capabilities, spatial requirements, or similarity.

**Impact**: The entire compositional model — GOAP selecting from a library of reusable, typed, fingerprinted behavior components — cannot function without a registry. This is the foundation of BEHAVIOR-COMPOSITION.

**GH Issue**: None

### 3.2 No Plan Cache

**Required by**: BEHAVIOR-COMPOSITION (100K agent scale), VISION (100,000+ concurrent AI NPCs)

**Current state**: No `PlanCache` exists. No world state fingerprinting. No quantization for cache keying. Every agent plans independently, even when thousands share near-identical states and goals.

**Impact**: At 100K agents with 60% doing routine behaviors across ~100 archetypes, the difference is ~600 GOAP evaluations per cache window vs ~60,000. Without plan caching, the system cannot meet the 100K agent target for routine behaviors. This is the single most critical scale gap.

**GH Issue**: None

### 3.3 No Composite Assembly

**Required by**: BEHAVIOR-COMPOSITION (component linking), COMPOSITIONAL-CINEMATICS (independent "cel" composition), VIDEO-DIRECTOR (scene composition from components)

**Current state**: No `CompositeAssembler` exists. Continuation points exist in bytecode but are not used to link component chains at runtime. `DocumentMerger` exists for compile-time composition but isn't actively used.

**Impact**: Without composite assembly, behavior components cannot be strung together at runtime via continuation points. The anime-production paradigm (independent performances linked at composition seams) is architecturally blocked.

**GH Issue**: None

### 3.4 No Component Libraries (Combat, Routine, Narrative, Music)

**Required by**: BEHAVIOR-COMPOSITION (phases 4-5), COMPOSITIONAL-CINEMATICS (200 combat exchange components), VIDEO-DIRECTOR (component-driven choreography)

**Current state**: Zero behavior components exist in any registry. No combat exchanges, no routine behaviors (commute, work, meal, rest), no narrative beat components, no music phrase components.

**Impact**: Even if the registry and assembler were built, there is nothing to compose. The component libraries are pure content creation work — hundreds of hand-crafted ABML documents designed for composition. COMPOSITIONAL-CINEMATICS estimates needing 200+ combat components, 50+ dialogue components per register, and 30+ transition components for a single domain.

**GH Issue**: None

### 3.5 No Plan Fingerprinting

**Required by**: BEHAVIOR-COMPOSITION (cache keying), COMPOSITIONAL-CINEMATICS (deterministic composition), VIDEO-DIRECTOR (reproducible cinematics)

**Current state**: No `PlanFingerprint` class exists. No deterministic hashing for goals, quantized states, or action sets. Content hashing exists for individual behaviors (SHA256 of bytecode) but not for plans (sequences of component selections).

**Impact**: Plan caching requires deterministic fingerprinting. Deterministic cinematic replay requires fingerprinted composition. Both are blocked.

**GH Issue**: None

---

## 4. Variable Provider Gaps

### 4.1 Missing Cognitive Domain Providers (Planned L4)

**Required by**: VISION (NPC Intelligence Stack), PLAYER-VISION (social fabric)

| Provider | Namespace | Status | Source Requirement |
|----------|-----------|--------|-------------------|
| Disposition | `${disposition.*}` | Not implemented | VISION (emotional synthesis, aspiration drives) |
| Hearsay | `${hearsay.*}` | Not implemented | VISION (belief propagation, information asymmetry) |
| Lexicon | `${lexicon.*}` | Not implemented | VISION (structured vocabulary, concept ontology) |
| Ethology | `${ethology.*}` | Not implemented | CINEMATIC-SYSTEM (species archetypes for combat style) |

**Impact**: The NPC Intelligence Stack diagram in VISION shows 14 implemented + 3 planned cognitive providers. Without disposition, NPCs have no emotional state beyond personality traits. Without hearsay, no information propagation. Without lexicon, no structured vocabulary for NPC communication. The "social fabric" (VISION) cannot exist without these.

### 4.2 Missing Media/Directing Providers

**Required by**: DEVELOPER-STREAMS, VIDEO-DIRECTOR

| Provider | Namespace | Status | Source Requirement |
|----------|-----------|--------|-------------------|
| Stream | `${stream.*}` | Not implemented | DEVELOPER-STREAMS (feed state, activity, composition) |
| Camera | `${camera.*}` | Not implemented | DEVELOPER-STREAMS + VIDEO-DIRECTOR (visual feed properties) |
| Audio | `${audio.*}` | Not implemented | DEVELOPER-STREAMS + VIDEO-DIRECTOR (audio state) |
| Developer | `${developer.*}` | Not implemented | DEVELOPER-STREAMS (build, test, git, typing state) |
| Video | `${video.*}` | Not implemented | VIDEO-DIRECTOR (cinematic composition state) |
| Cinematic | `${cinematic.*}` | Not implemented | CINEMATIC-SYSTEM (active cinematic state) |

**Impact**: The director divine actor pattern (DEVELOPER-STREAMS) depends entirely on `${stream.*}`, `${camera.*}`, and `${developer.*}` variables to make directing decisions. Without these, ABML-authored directing behaviors have no data to evaluate.

### 4.3 Missing Entity-Specific Providers

**Required by**: ACTOR-BOUND-ENTITIES, VISION (system realms)

| Provider | Namespace | Status | Source Requirement |
|----------|-----------|--------|-------------------|
| Wielder | `${wielder.*}` | Not implemented | ACTOR-BOUND-ENTITIES (living weapon awareness) |
| Dungeon | `${dungeon.*}` | Not implemented | VISION (dungeon core cognition) |
| Garden | `${garden.*}` | Not implemented | BEHAVIORAL-BOOTSTRAP (gardener god state) |
| Spirit | `${spirit.*}` | Not implemented | PLAYER-VISION (guardian spirit agency fidelity) |
| Social | `${social.*}` | Not implemented | VISION (social cognition, ambient mood) |
| Battle | `${battle.*}` | Not implemented | DEVELOPER-STREAMS mention of in-game equivalents |

**Impact**: Every actor-bound entity type (living weapons, dungeons, guardian spirits) requires domain-specific variable providers to make decisions about their unique cognitive domain. Without `${dungeon.mana_reserves}`, dungeon cores cannot decide when to spawn creatures. Without `${spirit.domain.combat.fidelity}`, Agency cannot gate QTE density.

---

## 5. Cinematic System Gaps

### 5.1 CinematicTheory SDK Does Not Exist

**Required by**: CINEMATIC-SYSTEM, COMPOSITIONAL-CINEMATICS, VIDEO-DIRECTOR, VISION (combat dream)

**Current state**: No code exists. The SDK is planned to provide:
- Dramatic grammar (beat structure, tension curves, timing, emotional arcs)
- Spatial reasoning (affordance evaluation from Mapping data, positioning, environmental interaction)
- Capability matching (what choreographic moves a participant can perform from `${combat.*}`)
- Camera direction rules (Toric Space, DCCL idioms, film editing patterns, Murch Rule of Six)

**Impact**: This is the computational foundation for all procedural cinematics. Without dramatic grammar, there is no formal structure for combat choreography. Without spatial reasoning, choreography ignores the environment. Without camera direction, there is no procedural cinematography. The "Combat Dream" (VISION) depends entirely on this SDK.

**GH Issue**: None

### 5.2 CinematicStoryteller SDK Does Not Exist

**Required by**: CINEMATIC-SYSTEM, VIDEO-DIRECTOR, COMPOSITIONAL-CINEMATICS

**Current state**: No code exists. The SDK is planned as the GOAP-driven composition layer:
- World state: participants, environment, dramatic context, player agency level
- Actions: `compose_exchange`, `insert_environmental_interaction`, `add_dramatic_pause`, `inject_qte_window`, etc.
- Output: Complete ABML cinematic documents ready for CinematicInterpreter

**Impact**: Without the Storyteller, there is no automated way to compose combat encounters, dialogue scenes, or any procedural cinematic. The CinematicInterpreter runtime exists and works, but has nothing to interpret because nothing composes content for it.

**GH Issue**: None

### 5.3 lib-cinematic Plugin Does Not Exist

**Required by**: CINEMATIC-SYSTEM, VIDEO-DIRECTOR, VISION

**Current state**: No schema, no plugin, no endpoints. Planned endpoints:
- `/cinematic/compose` — Full composition pipeline
- `/cinematic/extend` — Continuation point extension
- `/cinematic/resolve-input` — QTE + player input resolution
- `/cinematic/list-active` — Active instance tracking
- `/cinematic/abort` — Stop active cinematic

**Impact**: Without the plugin, there is no API surface for triggering cinematic composition. Event brain actors, god-actors, and the Video Director have no service to call.

**GH Issue**: None

### 5.4 Cinematic Extension Delivery Pipeline Not Implemented

**Required by**: CINEMATIC-SYSTEM (streaming composition), BEHAVIOR-COMPOSITION (component linking), COMPOSITIONAL-CINEMATICS (continuation point seams)

**Current state**: Opcodes exist (`ContinuationPoint`, `ExtensionAvailable`, `YieldToExtension`). Schema exists (`CinematicExtensionAvailableEvent`). **No code publishes the event. No mechanism matches extensions to running interpreters.**

**Impact**: Continuation points are the architectural keystone of behavior composition (BEHAVIOR-COMPOSITION) and cinematic layering (COMPOSITIONAL-CINEMATICS). Without the delivery pipeline, all composition must happen at compile-time via DocumentMerger, not at runtime. This eliminates the dynamic QTE injection, player agency gating, and streaming composition that define the cinematic vision.

**GH Issues**: #573, #603

### 5.5 No Agency-Gated QTE Density

**Required by**: CINEMATIC-SYSTEM (progressive agency), PLAYER-VISION (fidelity gradient), VISION (combat dream)

**Current state**: Agency service not implemented. No mechanism reads `${spirit.domain.combat.fidelity}` to determine how many continuation points become interactive vs. auto-resolving.

**Impact**: The progressive agency model (PLAYER-VISION) requires the same choreographic computation to produce different player experiences based on spirit fidelity. Without Agency gating, all cinematics are either fully interactive or fully autonomous — no gradient.

**GH Issue**: None (Agency is pre-implementation)

---

## 6. Director & Orchestration Gaps

### 6.1 Director Plugin Not Implemented

**Required by**: DEVELOPER-STREAMS (human-in-the-loop override), VIDEO-DIRECTOR (live-world music videos), VISION (content management)

**Current state**: Pre-implementation. No schema, no plugin, no endpoints. Planned Observe/Steer/Drive tiers for human-in-the-loop event coordination.

**Impact**: Without Director, there is no mechanism for developers or game masters to override autonomous cinematic/directing decisions. Both DEVELOPER-STREAMS and VIDEO-DIRECTOR rely on Director for human override.

**GH Issues**: #597, #599, #609, #612

### 6.2 No Actor Pause/Resume for Drive Sessions

**Required by**: DEVELOPER-STREAMS (developer takes manual control), CINEMATIC-SYSTEM (entity control during choreography)

**Current state**: ControlGateManager exists for entity control acquisition/release during cinematics (in-memory-only, Quirk #15). But no API endpoint or mechanism exists for Director Drive sessions to pause/resume actor autonomy.

**Impact**: Drive-tier directing requires suspending an actor's autonomous behavior while the human operator controls it. Without pause/resume, the actor and the human fight over control.

**GH Issue**: #599

### 6.3 No Tap Mechanism

**Required by**: DEVELOPER-STREAMS (developer observes actor state), Director (Observe tier)

**Current state**: Not designed. No mechanism for external observers to "tap into" an actor's perception stream, intent output, or internal state without modifying the actor's behavior.

**Impact**: The Observe tier of Director requires non-invasive observation of actor cognition. Without a tap mechanism, observation requires logging (heavy, non-real-time) or direct state inspection (breaks encapsulation).

**GH Issues**: #597, #609

---

## 7. Video Director Gaps

The entire Video Director system is aspirational. Every component below is unbuilt.

### 7.1 VideoDirectorComposer SDK

**Required by**: VIDEO-DIRECTOR (core architecture)

Submodules needed:
- **MusicSceneMapper**: Section energy → scene type mapping, phrase → shot sequence timing
- **LifeStageDecomposer**: Character archive → temporal segment extraction
- **MatchCutAnalyzer**: Archive analysis for visual parallels across life stages
- **CastingEngine**: Theme + criteria → character selection with variable provider queries
- **RoleMappingEngine**: Music ensemble roles → cinematic roles, cross-domain prominence sync
- **BeatSyncScheduler**: Action-to-beat alignment constraints

### 7.2 Music-Cinematic Synchronization

**Required by**: VIDEO-DIRECTOR (sections 4-6)

Three temporal scales of sync:
- **Macro** (section ↔ scene): Hard sync at section boundaries
- **Meso** (phrase ↔ shot sequence): Cut opportunities at phrase boundaries
- **Micro** (beat ↔ action): Impact moments on strong beats, preparation on weak beats

**Current state**: No sync mechanism exists. MusicStoryteller and CinematicInterpreter operate independently.

### 7.3 lib-video-director Plugin

**Required by**: VIDEO-DIRECTOR

Endpoints: `/video-director/compose`, `/video-director/cast`, `/video-director/preview`, `/video-director/execute`, `/video-director/templates`, `/video-director/abort`

**Current state**: No schema, no plugin.

### 7.4 Role-Based Cinematic Composition

**Required by**: VIDEO-DIRECTOR, COMPOSITIONAL-CINEMATICS

Mapping from music ensemble roles (melody, bass, harmony, rhythm, ornament) to cinematic roles (protagonist, counterforce, ensemble, atmosphere, detail) with screen time budgets, camera distance rules, action density constraints, and dramatic freedom parameters.

**Current state**: Conceptualized in VIDEO-DIRECTOR. No implementation.

---

## 8. Developer Streams Gaps

The entire Developer Streams system is aspirational. All components below are unbuilt.

### 8.1 Screen Capture SDK

**Required by**: DEVELOPER-STREAMS (feed acquisition)

Platform-specific screen capture (FFmpeg-based or native APIs) producing independent RTMP feeds per screen/terminal.

### 8.2 Activity Detection Agent

**Required by**: DEVELOPER-STREAMS (structured event generation)

Hooks into terminal emulators, build systems, file watchers, and git to produce structured activity events published to the Bannou message bus.

### 8.3 Broadcast Multi-Input Compositor

**Required by**: DEVELOPER-STREAMS, VIDEO-DIRECTOR (live-world mode)

FFmpeg filter chain management for compositing multiple RTMP inputs into a single directed output. Compositor commands shared between in-game and developer stream directing.

### 8.4 Feed Manifest in Agency

**Required by**: DEVELOPER-STREAMS (active/standby/inactive feed management), COMPOSITIONAL-CINEMATICS (distributed scene sourcing)

Extension of the UX capability manifest pattern for media feed management.

### 8.5 Client-Side Commentary Agent

**Required by**: DEVELOPER-STREAMS (LLM-powered stream commentary)

Client-side LLM using activity events + Documentation entries to generate natural language commentary posted to Chat rooms.

---

## 9. Actor Runtime Gaps

### 9.1 No Actor Migration Between Pool Nodes

**Required by**: VISION (100K scale with dynamic rebalancing)

**Current state**: Once an actor is assigned to a pool node, it stays there until stopped. No mechanism to move a running actor to a different node without state loss.

**Impact**: Pool rebalancing, node maintenance, and scale-down all require actor migration. Without it, operational flexibility is severely limited.

**GH Issue**: #393

### 9.2 No Pool Node Capacity Validation

**Required by**: VISION (100K scale reliability)

**Current state**: Pool nodes report capacity via heartbeat. No validation that reported capacity matches actual resource usage. No load discrepancy detection.

**GH Issue**: #394

### 9.3 No Behavior Version Tracking or Rollback

**Required by**: BEHAVIORAL-BOOTSTRAP (god-actor behavior updates), BEHAVIOR-COMPOSITION (component versioning)

**Current state**: Behaviors are stored by content hash. No version history, no rollback mechanism, no way to track which version of a behavior an actor is running.

**Impact**: Deploying updated god-actor behaviors to production requires confidence that the new version works. Without version tracking and rollback, bad behavior deployments are irreversible.

**GH Issue**: #391

### 9.4 Memory Relevance Decay Not Designed

**Required by**: VISION (100K NPCs with bounded memory), ABML-GOAP-OPPORTUNITIES (memory system)

**Current state**: Memory eviction is by count (oldest entries trimmed at per-entity limit of 100). No time-based relevance decay, no significance-weighted retention, no forgetting curve.

**Impact**: A 100-memory cap with FIFO eviction means NPCs forget important old memories (first meeting with a lifelong friend) in favor of trivial recent ones (walked past a tree). Memory quality degrades as NPCs accumulate experiences.

**GH Issue**: #387

### 9.5 No Embedding-Based Memory Store

**Required by**: VISION (NPC intelligence), ABML-GOAP-OPPORTUNITIES (memory system)

**Current state**: `IMemoryStore` interface exists. `ActorLocalMemoryStore` implements keyword-based search. No vector embedding implementation.

**Impact**: Keyword-based memory search cannot handle semantic similarity ("find memories about betrayal" when the stored memory says "ally turned against me in battle"). Embedding-based search is required for narratively rich NPC memory.

**GH Issue**: #606

### 9.6 No Game Engine ↔ Actor WebSocket Transport

**Required by**: CINEMATIC-SYSTEM (real-time choreographic delivery), VISION (game engine integration)

**Current state**: Actor runtime communicates via RabbitMQ (pool mode) or in-memory (bannou mode). No WebSocket transport via Connect for real-time game engine integration.

**Impact**: Combat choreography requires sub-frame latency between behavior decisions and game engine rendering. RabbitMQ adds unacceptable latency for real-time choreographic control.

**GH Issue**: #409

---

## 10. Puppetmaster & Watcher Gaps

### 10.1 Watcher-Actor Integration Not Implemented

**Required by**: BEHAVIORAL-BOOTSTRAP (god-actors spawn and manage regional watchers), VISION (regional watcher pattern)

**Current state**: Puppetmaster has watcher management ABML actions (`spawn_watcher`, `stop_watcher`, `list_watchers`). But the actual integration — watchers spawning Actor instances, monitoring their health, restarting crashed actors — is not implemented.

**Impact**: The entire god-actor orchestration pattern (BEHAVIORAL-BOOTSTRAP) depends on watchers spawning and managing actors. Without this integration, regional watchers are behavioral documents with no runtime manifestation.

**GH Issues**: #383, #388

### 10.2 Distributed Watcher State

**Required by**: BEHAVIORAL-BOOTSTRAP (multi-node watcher coordination), VISION (100K scale)

**Current state**: Watcher registry is in-memory. No distributed state for multi-node coordination.

**Impact**: In production, multiple Puppetmaster instances need to coordinate which watchers are running, on which nodes, managing which regions. Without distributed state, watchers are single-node only.

**GH Issue**: #395

### 10.3 Behavior Cache Warm-Up on Startup

**Required by**: BEHAVIORAL-BOOTSTRAP (god-actors need behaviors immediately at startup)

**Current state**: No warm-up mechanism. Behaviors are loaded on first access, causing cold-start latency for the first actor of each type.

**GH Issue**: #399

### 10.4 ABML Document-Level Variant Selection

**Required by**: BEHAVIORAL-BOOTSTRAP (god-specific behavior variants), ACTOR-BOUND-ENTITIES (cognitive stage variants)

**Current state**: `BehaviorModelCache` supports variant fallback chains at the runtime level. But Puppetmaster's `DynamicBehaviorProvider` has no mechanism to select between behavior document variants based on actor metadata (species, personality, role, cognitive stage).

**Impact**: Different species should use different combat behaviors. Different gods should use different orchestration behaviors. Without document-level variant selection, the only way to differentiate is runtime branching within a single monolithic document — which is exactly what template inheritance (#384) and variant selection should eliminate.

**GH Issue**: #397

---

## 11. Content Flywheel Integration Gaps

### 11.1 Archive-to-Storyline Feedback Pipeline

**Required by**: VISION (content flywheel loop), BEHAVIORAL-BOOTSTRAP (god-actor archive evaluation)

**Current state**: Not implemented. The pipeline `Character Death → Resource compression → Storyline composition → Quest spawning → New player experiences → loop` is documented but no code connects these services.

**Impact**: This is the content flywheel. Without it, the game has finite hand-authored content. With it, content generation accelerates with world age (Year 1: ~1K story seeds, Year 5: ~500K). This is arguably the single most important systemic gap for Arcadia's core thesis.

**GH Issue**: #385

### 11.2 No God-Actor Behavior Documents

**Required by**: BEHAVIORAL-BOOTSTRAP (god-actor behaviors), VISION (regional watcher orchestration)

**Current state**: No ABML behavior documents exist for any deity (Moira/Fate, Thanatos/Death, Silvanus/Forest, Ares/War, Typhon/Monsters, Hermes/Commerce). No manager actors (puppetmaster-manager, gardener-manager) exist.

**Impact**: The behavioral bootstrap sequence (Phase 1-5) cannot execute without behavior documents. God-actors are "just actors with unusual behavior documents" — but those documents don't exist.

**GH Issue**: None

### 11.3 No Perception Filtering for God-Actors

**Required by**: BEHAVIORAL-BOOTSTRAP (gods perceive different events differently), VISION (aesthetic preferences)

**Current state**: The 5-stage cognition pipeline exists with attention filtering. But there is no mechanism for god-actors to subscribe to and filter specific event streams based on their domain (Ares perceives battles, Hermes perceives trades, Moira perceives deaths).

**Impact**: Without domain-specific perception filtering, god-actors would need to process every event in their region and filter in ABML — which doesn't scale. The attention filter needs domain-aware subscription so gods only receive events matching their interests.

**GH Issue**: None

### 11.4 Second Thoughts (Prospective Consequence Evaluation)

**Required by**: VISION (NPC intelligence depth), ABML-GOAP-OPPORTUNITIES

**Current state**: Not implemented. lib-obligation provides obligation costs as variable providers. But no mechanism exists for NPCs to prospectively evaluate "what would happen if I did X" — checking consequences against obligations, relationships, and faction norms before acting.

**Impact**: Without prospective evaluation, NPCs violate obligations and social norms without hesitation, then face consequences retroactively. "Second thoughts" adds believable decision-making depth.

**GH Issue**: #410

---

## 12. Cross-Cutting Behavioral Gaps

### 12.1 Hot-Reload Not Wired End-to-End

**Required by**: BEHAVIORAL-BOOTSTRAP (behavior updates without restart), production operations

**Current state**: `behavior.updated` events exist. Cache invalidation is possible. But the end-to-end pipeline (behavior source changes → recompilation → event publication → cache invalidation → actor picks up new behavior) is not connected.

**GH Issue**: #618

### 12.2 Content Hash Deduplication Not Implemented

**Required by**: BEHAVIOR-COMPOSITION (component identity), scale efficiency

**Current state**: Behaviors are hashed by content (SHA256) for identification. But compilation doesn't check if an identical behavior already exists before compiling. Duplicate compilations waste CPU and storage.

**GH Issue**: #622

### 12.3 Validate-Only Compiler Path Missing

**Required by**: Developer tooling, CI/CD validation

**Current state**: `ValidateAbml` runs the full compilation pipeline including bytecode emission, then discards the bytecode. A validation-only path would save significant CPU for syntax checking.

**GH Issue**: #624

### 12.4 ABML Example Files Reference Wrong Variable Namespace

**Required by**: Documentation accuracy, developer experience

**Current state**: Worldstate ABML examples reference wrong variable namespaces.

**GH Issue**: #568

### 12.5 Guardian Spirit Input Tracking Boundary

**Required by**: PLAYER-VISION (progressive agency), ACTOR-BOUND-ENTITIES (spirit-character interaction)

**Current state**: Not designed. The boundary between Actor (executing character brain) and Disposition (tracking spirit inputs) for progressive agency is undefined.

**GH Issue**: #621

### 12.6 Offline NPC Behavior When Actor Is Suspended

**Required by**: VISION (world continues when players offline), production operations

**Current state**: Not designed. When an actor is suspended (pool node scaling, maintenance), NPC behavior stops. No mechanism for "offline simulation" or retroactive behavior computation.

**GH Issue**: #614

---

## Priority Assessment

### Tier 1: Architecture-Critical (Block Multiple Planning Documents)

These gaps block the fundamental evolution from "NPC brain" to "universal orchestration substrate":

| Gap | Blocks |
|-----|--------|
| **3.2 Plan Cache** | 100K agent scale (North Star #3) |
| **5.4 Cinematic Extension Delivery** | All composition-based systems |
| **11.1 Archive-to-Storyline Pipeline** | Content flywheel (North Star #2) |
| **1.2 Channel Signaling Compilation** | Multi-character synchronization |
| **3.1 Component Registry** | All behavior composition |
| **10.1 Watcher-Actor Integration** | God-actor orchestration |

### Tier 2: Vision-Critical (Required for Core Experiences)

These gaps are required for specific vision elements to function:

| Gap | Blocks |
|-----|--------|
| **5.1 CinematicTheory SDK** | Combat dream, all procedural cinematics |
| **5.2 CinematicStoryteller SDK** | Automated choreography composition |
| **1.1 Template Inheritance** | God-actor skeletons, entity cognitive stages |
| **1.3 Economic Action Handlers** | NPC-driven economy |
| **2.1 GOAP Silent Failure** | Debugging at any scale |
| **4.1 Disposition/Hearsay/Lexicon** | Social fabric, NPC communication |
| **9.6 WebSocket Transport** | Real-time choreographic delivery |
| **11.2 God-Actor Behaviors** | Content flywheel activation |

### Tier 3: Scale & Quality (Required for Production)

| Gap | Blocks |
|-----|--------|
| **9.1 Actor Migration** | Operational flexibility at scale |
| **9.4 Memory Relevance Decay** | Memory quality over time |
| **10.2 Distributed Watcher State** | Multi-node watcher coordination |
| **3.3 Composite Assembly** | Runtime behavior linking |
| **12.1 Hot-Reload** | Production behavior updates |
| **2.2 Parallel Plan Evaluation** | Multi-goal NPC performance |

### Tier 4: Feature Extensions (Required for Specific Features)

| Gap | Blocks |
|-----|--------|
| **7.* Video Director (all)** | Music-driven cinematic generation |
| **8.* Developer Streams (all)** | Dev stream directing |
| **6.* Director Plugin** | Human-in-the-loop orchestration |
| **4.2 Media Providers** | Directing variable access |
| **4.3 Entity Providers** | Living weapons, dungeons, spirits |
| **11.4 Second Thoughts** | NPC decision depth |

---

## Relationship to Existing Issues

| GH Issue | Gap Reference | Status |
|----------|--------------|--------|
| #384 | 1.1 Template Inheritance | Open, design questions unresolved |
| #408 | 1.4 Game Engine Signaling | Open |
| #409 | 9.6 WebSocket Transport | Open |
| #410 | 11.4 Second Thoughts | Open |
| #428 | 1.3 Economic Action Handlers | Open |
| #383 | 10.1 Watcher-Actor Integration | Open |
| #385 | 11.1 Content Flywheel Pipeline | Open |
| #387 | 9.4 Memory Relevance Decay | Open |
| #388 | 10.1 Watcher-Actor Integration | Open |
| #391 | 9.3 Behavior Version Tracking | Open |
| #393 | 9.1 Actor Migration | Open |
| #394 | 9.2 Pool Node Capacity | Open |
| #395 | 10.2 Distributed Watcher State | Open |
| #397 | 10.4 Variant Selection | Open |
| #399 | 10.3 Cache Warm-Up | Open |
| #568 | 12.4 ABML Example Files | Open |
| #573 | 5.4 Extension Delivery | Open, enhancement |
| #575 | 2.1 GOAP Silent Failure | Open, enhancement |
| #603 | 5.4 Extension Delivery | Open |
| #606 | 9.5 Embedding Memory Store | Open |
| #608 | 2.3 Plan Persistence | Open |
| #614 | 12.6 Offline NPC Behavior | Open |
| #617 | Cross-cutting (optimizer) | Open |
| #618 | 12.1 Hot-Reload | Open |
| #619 | 2.4 Plan Visualization | Open |
| #620 | 2.2 Parallel Plan Evaluation | Open |
| #621 | 12.5 Spirit Input Boundary | Open |
| #622 | 12.2 Content Hash Dedup | Open |
| #624 | 12.3 Validate-Only Path | Open |
| #625 | 2.1 GOAP Statistics | Open |

**Gaps without existing issues** (new discoveries from planning document analysis):

- 1.2 Channel Signaling Compilation
- 1.5 Limited Collection/Array Support
- 1.6 No String Operations in Bytecode VM
- 1.7 No Formal Error Recovery
- 2.5 GOAP Domain Expansion
- 3.1 Component Registry
- 3.2 Plan Cache
- 3.3 Composite Assembly
- 3.4 Component Libraries
- 3.5 Plan Fingerprinting
- 4.1 Disposition/Hearsay/Lexicon Providers (partially tracked by unimplemented service status)
- 4.2 Media/Directing Providers
- 4.3 Entity-Specific Providers
- 5.1 CinematicTheory SDK
- 5.2 CinematicStoryteller SDK
- 5.3 lib-cinematic Plugin
- 5.5 Agency-Gated QTE Density
- 6.1 Director Plugin
- 7.* Video Director (all)
- 8.* Developer Streams (all)
- 11.2 God-Actor Behavior Documents
- 11.3 God-Actor Perception Filtering

---

## Conclusion

The behavior system's evolution can be understood as three concentric rings:

**Ring 1 (Core — Exists)**: ABML compiler, GOAP planner, Actor runtime, 5-stage cognition, 13 variable providers, continuation point opcodes, distributed pool architecture. This is solid. It powers NPC brains today.

**Ring 2 (Composition — Missing)**: Component registry, plan cache, composite assembly, cinematic extension delivery, channel signaling. This is the connective tissue that transforms individual behaviors into composable units. Without Ring 2, the system cannot scale to 100K agents (plan cache) or support procedural cinematics (composition). **This is the highest priority gap.**

**Ring 3 (Domains — Missing)**: CinematicTheory, CinematicStoryteller, VideoDirectorComposer, Director, economic action handlers, god-actor behaviors, social providers, media providers. This is the domain-specific content and SDKs that make the universal orchestration substrate actually orchestrate things. Ring 3 depends on Ring 2.

The planning documents reveal that Bannou's behavior system must become something no game engine has ever attempted: a **universal compositional substrate** where NPC cognition, combat choreography, cinematic direction, music-driven video generation, developer stream directing, and content flywheel orchestration are all expressions of the same underlying technology — ABML behaviors evaluated by GOAP, composed from fingerprinted components, executed on the Actor runtime.

The foundation is strong. The distance to the vision is significant but well-mapped.

---

*This document catalogs behavioral gaps identified through analysis of planning documents (VIDEO-DIRECTOR, COMPOSITIONAL-CINEMATICS, DEVELOPER-STREAMS, BEHAVIOR-COMPOSITION), vision documents (VISION, PLAYER-VISION), system architecture documents (CINEMATIC-SYSTEM, ABML-GOAP-OPPORTUNITIES, BEHAVIORAL-BOOTSTRAP, ACTOR-BOUND-ENTITIES), and open GitHub issues. For the current implementation state, see [BEHAVIOR.md](../plugins/BEHAVIOR.md) and [ACTOR.md](../plugins/ACTOR.md). For the compositional architecture, see [BEHAVIOR-COMPOSITION.md](BEHAVIOR-COMPOSITION.md).*
