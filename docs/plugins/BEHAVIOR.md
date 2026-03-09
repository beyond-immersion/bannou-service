# Behavior Plugin Deep Dive

> **Plugin**: lib-behavior
> **Schema**: schemas/behavior-api.yaml
> **Version**: 3.0.0
> **Layer**: GameFeatures
> **State Store**: behavior-statestore (Redis), agent-memories (Redis, shared with lib-actor)
> **Short**: ABML compiler (YAML to bytecode), A*-based GOAP planner, and 5-stage cognition pipeline

---

## Overview

ABML (Arcadia Behavior Markup Language) compiler and GOAP (Goal-Oriented Action Planning) runtime (L4 GameFeatures) for NPC behavior management. Provides three core subsystems: a multi-phase ABML compiler producing portable stack-based bytecode, an A*-based GOAP planner for action sequence generation from world state and goals, and a 5-stage cognition pipeline for NPC perception and intention formation. Compiled bytecode is interpreted by both the server-side ActorRunner (L2) and client SDKs. Supports streaming composition, variant-based model caching with fallback chains, and behavior bundling through the Asset service.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Redis persistence for behavior metadata, bundle membership, GOAP metadata (via `BehaviorBundleManager`) |
| lib-messaging (`IMessageBus`) | Publishing behavior lifecycle events, compilation failure events, GOAP plan events; error event publishing |
| lib-asset (`IAssetClient`) | Storing and retrieving compiled bytecode via pre-signed URLs (soft L3 dependency — resolved via `GetService<T>()` with graceful degradation) |
| `IHttpClientFactory` | HTTP client for asset upload/download operations |
| `ITelemetryProvider` | Span instrumentation for async methods |
| behavior-compiler SDK (`DocumentParser`) | YAML-to-AST parsing of ABML documents (from `sdks/behavior-compiler/`) |
| behavior-compiler SDK (`BehaviorCompiler`) | Multi-phase ABML-to-bytecode compilation pipeline (from `sdks/behavior-compiler/`) |
| behavior-compiler SDK (`GoapPlanner`) | A* search for GOAP planning (from `sdks/behavior-compiler/`) |
| behavior-compiler SDK (Runtime types) | `BehaviorModel`, `BehaviorModelInterpreter`, `BehaviorModelType`, `IntentChannel`, `BehaviorOpcode` |
| bannou-service (Cognition types) | `CognitionConstants`, `IMemoryStore`, `IActionHandler`, cognition pipeline infrastructure |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-actor | Consumes shared behavior compiler types (from bannou-service) for NPC brain execution; uses GOAP planner for action selection. Does NOT use `IBehaviorClient` — compiler types are shared code, not service-to-service calls |
| lib-puppetmaster | Subscribes to `behavior.updated` events for hot-reload of runtime-loaded behaviors; implements `IBehaviorDocumentProvider` which loads behaviors via lib-asset |

---

## State Storage

**Stores**: 1 state store (Redis) + 1 cross-service store

| Store | Backend | Purpose | TTL | Owner |
|-------|---------|---------|-----|-------|
| `behavior-statestore` | Redis | Behavior metadata, bundle membership, GOAP metadata | N/A | lib-behavior |
| `agent-memories` | Redis | Memory entries for cognition pipeline | N/A | lib-actor (used by lib-behavior's `ActorLocalMemoryStore`) |

| Key Pattern | Store | Data Type | Purpose |
|-------------|-------|-----------|---------|
| `behavior-metadata:{behaviorId}` | behavior | Behavior metadata JSON | Compiled behavior definition metadata |
| `bundle-membership:{bundleId}` | behavior | Bundle membership JSON | Bundle-to-behavior association index |
| `goap-metadata:{behaviorId}` | behavior | GOAP metadata JSON | Cached GOAP goals/actions from compiled behavior (used by `GenerateGoapPlanAsync`) |
| `memory:{entityId}:{memoryId}` | agent-memories | Memory JSON | Individual memory entries per agent |
| `memory-index:{entityId}` | agent-memories | List of memory IDs | Memory index for per-entity retrieval |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `behavior.created` | `BehaviorCreatedEvent` | New behavior compiled and stored (lifecycle) |
| `behavior.updated` | `BehaviorUpdatedEvent` | Behavior recompiled/updated (lifecycle) |
| `behavior.deleted` | `BehaviorDeletedEvent` | Behavior deleted/invalidated (lifecycle) |
| `behavior.bundle.created` | `BehaviorBundleCreatedEvent` | Bundle created (lifecycle) — published by `BehaviorBundleManager.AddToBundleAsync` when first behavior added to a new bundle |
| `behavior.bundle.updated` | `BehaviorBundleUpdatedEvent` | Bundle updated (lifecycle) — published by `AddToBundleAsync`, `CreateAssetBundleAsync`, `RemoveBehaviorAsync` on membership changes |
| `behavior.bundle.deleted` | `BehaviorBundleDeletedEvent` | Bundle deleted (lifecycle) — published by `RemoveBehaviorAsync` when last behavior removed from bundle |
| `behavior.compilation-failed` | `BehaviorCompilationFailedEvent` | ABML compilation fails (monitoring/alerting) |
| `behavior.goap-plan-generated` | `GoapPlanGeneratedEvent` | GOAP planner generates new plan |
| `behavior.cinematic-extension` | `CinematicExtensionAvailableEvent` | Cinematic extension available for injection at continuation point - **schema-defined but not yet published by code** |

### Consumed Events

This plugin does not consume external events (confirmed by `x-event-subscriptions: []` in schema).

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `LowUrgencyThreshold` | `BEHAVIOR_LOW_URGENCY_THRESHOLD` | `0.3` | Urgency below which planning uses low-urgency (full deliberation) |
| `HighUrgencyThreshold` | `BEHAVIOR_HIGH_URGENCY_THRESHOLD` | `0.7` | Urgency above which planning uses high-urgency (immediate reaction) |
| `LowUrgencyMaxPlanDepth` | `BEHAVIOR_LOW_URGENCY_MAX_PLAN_DEPTH` | `10` | Max A* search depth at low urgency |
| `LowUrgencyPlanTimeoutMs` | `BEHAVIOR_LOW_URGENCY_PLAN_TIMEOUT_MS` | `100` | Planning timeout at low urgency |
| `LowUrgencyMaxPlanNodes` | `BEHAVIOR_LOW_URGENCY_MAX_PLAN_NODES` | `1000` | Max nodes expanded at low urgency |
| `MediumUrgencyMaxPlanDepth` | `BEHAVIOR_MEDIUM_URGENCY_MAX_PLAN_DEPTH` | `6` | Max A* search depth at medium urgency |
| `MediumUrgencyPlanTimeoutMs` | `BEHAVIOR_MEDIUM_URGENCY_PLAN_TIMEOUT_MS` | `50` | Planning timeout at medium urgency |
| `MediumUrgencyMaxPlanNodes` | `BEHAVIOR_MEDIUM_URGENCY_MAX_PLAN_NODES` | `500` | Max nodes expanded at medium urgency |
| `HighUrgencyMaxPlanDepth` | `BEHAVIOR_HIGH_URGENCY_MAX_PLAN_DEPTH` | `3` | Max A* search depth at high urgency |
| `HighUrgencyPlanTimeoutMs` | `BEHAVIOR_HIGH_URGENCY_PLAN_TIMEOUT_MS` | `20` | Planning timeout at high urgency |
| `HighUrgencyMaxPlanNodes` | `BEHAVIOR_HIGH_URGENCY_MAX_PLAN_NODES` | `200` | Max nodes expanded at high urgency |
| `DefaultThreatWeight` | `BEHAVIOR_DEFAULT_THREAT_WEIGHT` | `10.0` | Attention priority multiplier for threats |
| `DefaultNoveltyWeight` | `BEHAVIOR_DEFAULT_NOVELTY_WEIGHT` | `5.0` | Attention priority multiplier for novel perceptions |
| `DefaultSocialWeight` | `BEHAVIOR_DEFAULT_SOCIAL_WEIGHT` | `3.0` | Attention priority multiplier for social perceptions |
| `DefaultRoutineWeight` | `BEHAVIOR_DEFAULT_ROUTINE_WEIGHT` | `1.0` | Attention priority multiplier for routine perceptions |
| `DefaultThreatFastTrackThreshold` | `BEHAVIOR_DEFAULT_THREAT_FAST_TRACK_THRESHOLD` | `0.8` | Urgency threshold for bypassing cognition pipeline |
| `DefaultEmotionalWeight` | `BEHAVIOR_DEFAULT_EMOTIONAL_WEIGHT` | `0.4` | Significance scoring: emotional impact weight |
| `DefaultGoalRelevanceWeight` | `BEHAVIOR_DEFAULT_GOAL_RELEVANCE_WEIGHT` | `0.4` | Significance scoring: goal relevance weight |
| `DefaultRelationshipWeight` | `BEHAVIOR_DEFAULT_RELATIONSHIP_WEIGHT` | `0.2` | Significance scoring: relationship factor weight |
| `DefaultMemoryLimit` | `BEHAVIOR_DEFAULT_MEMORY_LIMIT` | `100` | Maximum memory entries per actor |
| `MemoryStoreMaxRetries` | `BEHAVIOR_MEMORY_STORE_MAX_RETRIES` | `3` | Max retries for memory store operations |
| `MemoryMinimumRelevanceThreshold` | `BEHAVIOR_MEMORY_MINIMUM_RELEVANCE_THRESHOLD` | `0.1` | Minimum relevance score for memory retrieval |
| `DefaultStorageThreshold` | `BEHAVIOR_DEFAULT_STORAGE_THRESHOLD` | `0.7` | Significance score threshold for storing memories |
| `MemoryCategoryMatchWeight` | `BEHAVIOR_MEMORY_CATEGORY_MATCH_WEIGHT` | `0.3` | Memory relevance: category match weight |
| `MemoryContentOverlapWeight` | `BEHAVIOR_MEMORY_CONTENT_OVERLAP_WEIGHT` | `0.4` | Memory relevance: content keyword overlap weight |
| `MemoryMetadataOverlapWeight` | `BEHAVIOR_MEMORY_METADATA_OVERLAP_WEIGHT` | `0.2` | Memory relevance: metadata key overlap weight |
| `MemoryRecencyBonusWeight` | `BEHAVIOR_MEMORY_RECENCY_BONUS_WEIGHT` | `0.1` | Memory relevance: recency bonus (< 1 hour) |
| `MemorySignificanceBonusWeight` | `BEHAVIOR_MEMORY_SIGNIFICANCE_BONUS_WEIGHT` | `0.1` | Memory relevance: significance bonus weight |
| `CompilerMaxConstants` | `BEHAVIOR_COMPILER_MAX_CONSTANTS` | `256` | Maximum constants in behavior constant pool |
| `CompilerMaxStrings` | `BEHAVIOR_COMPILER_MAX_STRINGS` | `65536` | Maximum strings in behavior string table |
| `BundleMembershipKeyPrefix` | `BEHAVIOR_BUNDLE_MEMBERSHIP_KEY_PREFIX` | `bundle-membership:` | Key prefix for bundle membership entries |
| `BehaviorMetadataKeyPrefix` | `BEHAVIOR_METADATA_KEY_PREFIX` | `behavior-metadata:` | Key prefix for behavior metadata entries |
| `GoapMetadataKeyPrefix` | `BEHAVIOR_GOAP_METADATA_KEY_PREFIX` | `goap-metadata:` | Key prefix for GOAP metadata entries |
| `MemoryKeyPrefix` | `BEHAVIOR_MEMORY_KEY_PREFIX` | `memory:` | Key prefix for memory entries |
| `MemoryIndexKeyPrefix` | `BEHAVIOR_MEMORY_INDEX_KEY_PREFIX` | `memory-index:` | Key prefix for memory index entries |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<BehaviorService>` | Scoped | Structured logging |
| `BehaviorServiceConfiguration` | Singleton | All 33 config properties (urgency, attention, memory, compiler) |
| `IStateStoreFactory` | Singleton | Access to behavior-statestore (used by BehaviorBundleManager and ActorLocalMemoryStore) |
| `IMessageBus` | Scoped | Event publishing and error events |
| `IServiceProvider` | Singleton | Service provider for resolving optional L3 dependencies (IAssetClient) at method scope |
| `IHttpClientFactory` | Singleton | HTTP client for asset upload operations |
| `ITelemetryProvider` | Singleton | Span instrumentation for async methods (T30 compliance) |
| `IEventConsumer` | Scoped | Event consumer registration (no subscriptions - handler call is a no-op) |
| `IGoapPlanner` (via `GoapPlanner`) | Singleton | A* search for GOAP planning (thread-safe, stateless) |
| `BehaviorCompiler` | Singleton | ABML-to-bytecode compilation pipeline (thread-safe, stateless) |
| `IDocumentMerger` (via `DocumentMerger`) | Singleton | ABML document composition — merges LoadedDocument trees into flat AbmlDocuments for bytecode compilation (stateless, thread-safe) |
| `SemanticAnalyzer` | Per-use (internal to compiler) | Pre-compilation validation pass (uses `IResourceTemplateRegistry` when available) |
| `IBehaviorBundleManager` | Scoped | Bundle creation, membership tracking, and GOAP metadata caching |
| `BehaviorModelCache` | Not DI-registered | Per-character interpreter caching with variant fallback chains (ConcurrentDictionary). Used by `BehaviorEvaluatorBase` subclasses but NOT registered in `BehaviorServicePlugin.ConfigureServices()` — must be manually instantiated |
| `IMemoryStore` (via `ActorLocalMemoryStore`) | Singleton | Keyword-based memory storage and retrieval (uses `agent-memories` store) |
| `CognitionConstants` | Static | Configurable cognition pipeline thresholds (initialized from config at startup) |
| `IBehaviorModelInterpreterFactory` | Singleton | Runtime execution of compiled behavior models |
| `IArchetypeRegistry` | Singleton | Pre-loaded behavior archetype definitions |
| `IIntentEmitterRegistry` | Singleton | Core intent emitters for action/locomotion/attention/stance/vocalization channels |
| `IControlGateRegistry` (via `ControlGateManager`) | Singleton | Per-entity control tracking for behavior gating |
| `IEntityResolver` | Singleton | Cutscene semantic name resolution |
| `ICutsceneCoordinator` (via `CutsceneCoordinator`) | Singleton | Multi-participant cutscene session coordination (in-memory session state via ConcurrentDictionary) |
| `IActionHandler` (6 cognition handlers) | Singleton | FilterAttention, QueryMemory, AssessSignificance, StoreMemory, EvaluateGoalImpact, TriggerGoapReplan |
| `IExternalDialogueLoader` | Singleton | YAML-based dialogue file loading with caching |
| `IDialogueResolver` | Singleton | Three-step dialogue resolution pipeline |
| `ILocalizationProvider` (via `FileLocalizationProvider`) | Singleton | String table lookups for localization |
| `ICognitionTemplateRegistry` | Singleton | Base cognition templates (humanoid, creature, object) with embedded defaults |
| `ICognitionBuilder` | Singleton | Pipeline construction from cognition templates with overrides |

BehaviorService itself is **Scoped** (per-request). Most helper services are **Singleton** for shared, thread-safe access. `CognitionConstants` is static but initialized from `BehaviorServiceConfiguration` once at service startup. `IMemoryStore` is registered as Singleton despite using `IStateStoreFactory` (which creates per-call store instances internally).

---

## API Endpoints (Implementation Notes)

### ABML Operations (2 endpoints)

- **CompileAbmlBehavior** (`/compile`): Accepts ABML YAML string with optional compilation options (`enableOptimizations`, `cacheCompiledResult`, `strictValidation`, `culturalAdaptations`, `goapIntegration`). Invokes the multi-phase compiler pipeline: YAML parsing via `DocumentParser`, semantic analysis (when `strictValidation` is true), flow compilation with action compiler registry, bytecode emission with label patching. On success with caching enabled: stores bytecode as asset via lib-asset (soft L3 dependency), records metadata in `BehaviorBundleManager`, extracts and caches GOAP metadata from document, publishes `behavior.created` or `behavior.updated` lifecycle event. On failure: publishes `behavior.compilation-failed` event (note: `ContentHash` field in event schema is defined but never populated by the code). Returns compiled bytecode size, behavior ID (SHA256 hash of bytecode), and compilation time.

- **ValidateAbml** (`/validate`): Validates ABML YAML by running the full compilation pipeline (including bytecode emission) then discarding the bytecode and returning only the error/success status. Despite the "validate-only" intent, calls `_compiler.CompileYaml()` which performs the same multi-phase pipeline as `/compile`. Returns validation result with `isValid` flag and error list (undefined flows, empty conditionals, invalid continuation points, type mismatches). Semantic warnings (unreachable code, unused flows, etc.) are propagated via `result.Warnings` when `StrictMode=true` enables semantic analysis. When `StrictMode=false` (default), semantic analysis is skipped entirely and no warnings are produced. Does not modify state or publish events.

### Cache Operations (2 endpoints)

- **GetCachedBehavior** (`/cache/get`): Retrieves previously compiled behavior bytecode by behavior ID. Resolves `IAssetClient` via soft L3 dependency pattern (returns `ServiceUnavailable` if Asset service not loaded). Requests asset from lib-asset, downloads bytecode from pre-signed URL, returns compiled model with bytecode (base64). Falls back to providing download URL if bytecode download fails.

- **InvalidateCachedBehavior** (`/cache/invalidate`): Invalidates cached behavior by ID. Removes behavior metadata and GOAP metadata from state store via `BehaviorBundleManager`. Deletes the asset from lib-asset (if available). Publishes `behavior.deleted` lifecycle event. Note: does NOT invalidate in-memory `BehaviorModelCache` entries — the cache is not referenced by `BehaviorService`; it is only used by `BehaviorEvaluatorBase` subclasses.

### GOAP Operations (2 endpoints)

- **GenerateGoapPlan** (`/goap/plan`): Requires `behaviorId` to retrieve pre-cached GOAP metadata from compiled behaviors. Accepts a goal definition and current world state. Actions come from cached GOAP metadata (extracted during compilation), not from the request. Uses optional planning options (max depth/timeout/max nodes) or `PlanningOptions.Default` when not specified. Invokes `GoapPlanner.PlanAsync` with A* search. On success: returns ordered action sequence, total cost, nodes expanded, planning time in ms. On failure (no plan found): returns OK with failure reason and zeroed stats. Publishes `behavior.goap.plan-generated` event on success only.

- **ValidateGoapPlan** (`/goap/validate-plan`): Validates an existing plan against updated world state. Accepts the plan (goal, action sequence), current action index, current world state, and optional active goals list for priority checking. Checks: plan completion, goal already satisfied, current action preconditions still valid, higher-priority goals now actionable. Returns `PlanValidationResult` with validity flag, `ReplanReason` (None, PreconditionInvalidated, ActionFailed, BetterGoalAvailable, PlanCompleted, GoalAlreadySatisfied, SuboptimalPlan), `ValidationSuggestion` (Continue, Replan, Abort), invalidated action index, and optional better goal reference.

---

## GOAP Implementation Notes

### Canonical GOAP vs Bannou Implementation

The GOAP planner follows the architecture from [Jeff Orkin's original F.E.A.R. implementation](https://www.gamedeveloper.com/design/building-the-ai-of-f-e-a-r-with-goal-oriented-action-planning) with deliberate enhancements for richer NPC behaviors:

| Aspect | Canonical GOAP (F.E.A.R.) | Bannou Implementation |
|--------|--------------------------|----------------------|
| **World State** | 64-bit bit field (boolean atoms only) | `ImmutableDictionary<string, object>` (numeric, boolean, string) |
| **Conditions** | Boolean equality only | Full comparison operators (`==`, `!=`, `>`, `>=`, `<`, `<=`) |
| **Effects** | Set boolean flags | Set, Add (+delta), Subtract (-delta) |
| **Search Direction** | Regressive (backward from goal) | Forward (from current state) |
| **Heuristic** | Count of unsatisfied conditions | Sum of condition distances (numeric-aware) |

**Why the enhancements matter**: Boolean-only state forces awkward decomposition (e.g., `energy_low`, `energy_medium`, `energy_high` flags instead of `energy >= 50`). Numeric state with delta effects enables natural expressions like "resting adds 20 energy" without manual flag management.

### Forward vs Regressive Search Tradeoff

Jeff Orkin's paper recommends **regressive (backward) search** for efficiency:

> "A regressive search is more efficient and intuitive. Searching backwards will start at the goal, and find actions that will satisfy the goal."

**Why regressive search works for classic GOAP:**
- Goal: `hasWeapon == true`
- Find actions whose effects set `hasWeapon = true` → direct lookup
- New subgoals: that action's preconditions
- Simple 1:1 mapping between effects and subgoals

**Why regressive search is impractical for Bannou's implementation:**

With numeric effects and inequality conditions, reversing the search is non-trivial:

```
Goal: energy >= 10
Current: energy = 3
Action "Rest": effect energy += 5

Forward search: Apply Rest → energy = 8 → Apply Rest → energy = 13 → Goal satisfied

Regressive search would need to:
  1. Goal: energy >= 10
  2. Find actions affecting energy → Rest (energy += 5)
  3. Work backward: Before Rest, need energy >= 5 (10 - 5)
  4. Still unsatisfied (3 < 5), recurse...
  5. Need another Rest → Before that, need energy >= 0
  6. Now current state satisfies
```

This requires tracking **partial satisfaction** through accumulated deltas, which essentially simulates forward search with extra complexity. The richer expression language trades regressive-search-friendliness for expressiveness.

**Practical impact**: For typical NPC behavior (10-20 actions per actor), forward search with bounded depth (3-10), node limits (200-1000), and timeouts (20-100ms) performs adequately. The urgency-tiered parameters ensure reactive behaviors under time pressure.

---

## Visual Aid

```
ABML Compilation Pipeline
============================

  YAML Source (ABML)
       |
       v
  [Phase 0: DocumentParser]
       |  Produces AbmlDocument AST
       |  (metadata, context, flows, goals)
       v
  [Phase 1: SemanticAnalyzer]
       |  Validates: undefined flows, empty conditionals,
       |  invalid continuation points, unreachable code
       |  Produces: errors (blocking) + warnings (info)
       v
  [Phase 2: AnalyzeDocument]
       |  Registers input variables from context block
       |  Each variable -> index in input table
       |  Default values stored in model builder
       v
  [Phase 3: CompileFlows]
       |  ActionCompilerRegistry dispatches per action type
       |  Main flow compiled first, then remaining flows
       |  Emits bytecode via BytecodeEmitter
       |  Records label offsets, patches forward jumps
       v
  [Phase 4: Finalize]
       |  Emits HALT opcode
       |  Finalizes continuation points
       |  Attaches debug info (if enabled)
       |  Builds: ConstantPool + StringTable + Bytecode
       v
  CompilationResult
       |  Success: byte[] bytecode
       |  Failure: List<CompilationError>


Bytecode Instruction Set (BehaviorOpcode)
==========================================

  0x00-0x0F: Stack Operations
    PushConst, PushInput, PushLocal, StoreLocal,
    Pop, Dup, Swap, PushString

  0x10-0x1F: Arithmetic
    Add, Sub, Mul, Div, Mod, Neg

  0x20-0x2F: Comparison
    Eq, Ne, Lt, Le, Gt, Ge

  0x30-0x3F: Logical
    And, Or, Not

  0x40-0x4F: Control Flow
    Jmp, JmpIf, JmpUnless, Call, Ret, Halt, SwitchJmp

  0x50-0x5F: Output
    SetOutput, EmitIntent (action/locomotion/attention/stance/vocalization)

  0x60-0x6F: Special/Math
    Rand, RandInt, Lerp, Clamp, Abs, Floor, Ceil, Min, Max

  0x70-0x7F: Streaming Composition
    ContinuationPoint, ExtensionAvailable, YieldToExtension

  0xF0-0xFE: Debug
    Breakpoint, Trace

  0xFF: Reserved (Nop)


GOAP A* Planning
==================

  PlanAsync(currentState, goal, actions, options)
       |
       +--> Goal already satisfied? --> GoapPlan.Empty()
       |
       +--> No actions available? --> null
       |
       v
  [A* Search Loop]
       |
       |  openSet: PriorityQueue<PlanNode, float> (by FCost)
       |  closedSet: HashSet<int> (state hashes)
       |
       |  while openSet not empty:
       |    |
       |    +--> Check: cancellation, timeout, node limit
       |    |
       |    +--> Dequeue lowest-FCost node
       |    |
       |    +--> Goal check: SatisfiesGoal() --> goalNode found
       |    |
       |    +--> Add to closedSet (skip if duplicate hash)
       |    |
       |    +--> Depth limit check
       |    |
       |    +--> For each available action:
       |         |  IsApplicable(currentState)?
       |         |  Apply effects -> newState
       |         |  Skip if newStateHash in closedSet
       |         |  gCost = parent.GCost + action.Cost
       |         |  hCost = newState.DistanceToGoal(goal) * weight
       |         |  Enqueue new PlanNode
       |
       v
  ReconstructPlan(goalNode)
       |  Walk Parent chain from goal -> start
       |  Reverse to get action order
       |  GoapPlan(goal, actions, totalCost, stats)


Urgency-Tiered Planning Parameters
=====================================

  Urgency [0.0 .... 0.3 .... 0.7 .... 1.0]
           |  LOW    |  MEDIUM  |   HIGH  |
           |         |          |         |
  Depth:   |   10    |    6     |    3    |
  Timeout: |  100ms  |   50ms   |   20ms  |
  Nodes:   | 1000    |  500     |  200    |


Cognition Pipeline (5 Stages)
================================

  Perceptions (from environment/sensors)
       |
       v
  [Stage 1: Attention Filter]
       |  Priority = urgency * categoryWeight
       |  Budget-limited selection
       |  Threat fast-track (urgency > 0.8 -> skip to Stage 5)
       v
  [Stage 2: Significance Assessment]
       |  Score = emotional*0.4 + goalRelevance*0.4 + relationship*0.2
       |  Above StorageThreshold (0.7)? -> Stage 3
       v
  [Stage 3: Memory Formation]
       |  StoreExperienceAsync() via IMemoryStore
       |  Keyword-based retrieval for related memories
       |  Per-entity memory limit (100 default)
       v
  [Stage 4: Goal Impact Evaluation]
       |  Check if perceptions affect current goals
       |  Determine replanning urgency
       v
  [Stage 5: Intention Formation]
       |  GOAP replanning if goals affected
       |  Fast-tracked threats arrive here directly
       |  Output: action intents on channels


BehaviorModelCache (Variant Fallback)
========================================

  GetInterpreter(characterId, type, variant="sword-and-shield")
       |
       +--> Check ConcurrentDictionary cache
       |    Key: (characterId, type, variant)
       |    Hit? -> return cached interpreter
       |
       +--> FindBestModel(type, variant)
       |    1. Try exact variant: "sword-and-shield"
       |    2. Fallback chain: ["sword-and-shield", "one-handed", "default"]
       |    3. Try each until model found
       |
       +--> Create BehaviorModelInterpreter(model)
       |    Cache with resolved variant
       |
       +--> Return interpreter (per-character, NOT thread-safe)


Streaming Composition (Continuation Points)
=============================================

  Base Behavior Execution:
       |
  [ContinuationPoint "phase_2"]  <-- Extension attachment point
       |                              timeout: 5000ms
       |
       +--> Extension attached?
       |    YES: [YieldToExtension] -> Execute extension bytecode
       |    NO:  Wait until timeout
       |         -> [Jmp to defaultFlow]
       |
  [DefaultFlow: "phase_2_default"]
       |
  (Continue execution...)

  Extension Published via:
       CinematicExtensionAvailableEvent {
         characterId, cinematicId,
         continuationPointName: "phase_2",
         extensionBytecode: <base64>,
         expiresAtEpochMs
       }


Memory Relevance Scoring (Keyword-Based)
==========================================

  FindRelevantAsync(entityId, perceptions, limit)
       |
       v
  For each memory x each perception:
       |
       +--> CategoryMatch:   0.3 * (category == perception.category ? 1 : 0)
       |
       +--> ContentOverlap:  0.4 * (shared words / max word count)
       |
       +--> MetadataOverlap: 0.2 * (shared keys / max key count)
       |
       +--> RecencyBonus:    0.1 * max(0, 1 - hours_since_creation)
       |                     (only for memories < 1 hour old)
       |
       +--> SignificanceBonus: 0.1 * memory.Significance
       |
       v
  Total = sum of above components
  Filter: Total >= MinimumRelevanceThreshold (0.1)
  Sort: descending by relevance
  Take: limit
```

---

## Stubs & Unimplemented Features

1. ~~**Bundle management partial**~~: **FIXED** (2026-03-08) - `BehaviorBundleManager` is fully implemented with 9 methods covering behavior recording, bundle membership tracking, asset bundle creation via lib-asset (L3 soft dependency), GOAP metadata caching, and all 3 bundle lifecycle event publications (created/updated/deleted). What remains unimplemented: no dedicated HTTP endpoints for bundle querying/listing, no bundle versioning, and no metabundle (merged super-bundle) support. These are tracked as Potential Extensions, not stubs.

2. **Cinematic extension delivery**: The `CinematicExtensionAvailableEvent` schema and event model are defined but no code in lib-behavior actually publishes this event. The event model exists in generated code and `CinematicInterpreterTests.cs` references it, but the publishing path and the actual extension attachment to a running interpreter (matching `continuationPointName` to an active `ContinuationPoint` opcode) are not yet implemented.
<!-- AUDIT:NEEDS_DESIGN:2026-03-08:https://github.com/beyond-immersion/bannou-service/issues/603 -->

3. **Embedding-based memory store**: `IMemoryStore` interface is designed for swappable implementations. Only `ActorLocalMemoryStore` (keyword-based) exists. The embedding-based implementation for semantic similarity matching is documented as a future migration path in BEHAVIOR-SYSTEM.md section 7.5.
<!-- AUDIT:NEEDS_DESIGN:2026-03-08:https://github.com/beyond-immersion/bannou-service/issues/606 -->

4. **GOAP plan persistence**: GOAP metadata (goals/actions) is stored per behavior in state (`goap-metadata:{behaviorId}` prefix) and used internally by `GenerateGoapPlanAsync`, but there is no external retrieval endpoint or plan result history query. The `GoapPlanGeneratedEvent` provides the analytics trail for generated plans.
<!-- AUDIT:NEEDS_DESIGN:2026-03-08:https://github.com/beyond-immersion/bannou-service/issues/608 -->

5. ~~**Compiler optimizations not wired**~~: **FIXED** (2026-03-08) - `BytecodeOptimizer` (3 peephole passes: push-pop elimination, duplicate constant optimization, constant folding) was fully implemented in `Codegen/BytecodeOptimizer.cs` but never called. Wired into `CompilationContext.Finalize()` — when `EnableOptimizations` is true, optimizer runs on emitted bytecode before model building. The `CompilationOptions.Release` preset now produces optimized bytecode as intended.

6. ~~**Bundle lifecycle events not published**~~: **FIXED** (2026-03-08) - `BehaviorBundleManager` DOES publish all 3 bundle lifecycle events via generated `PublishBehaviorBundle*Async` extension methods: created in `AddToBundleAsync` (line 167), updated in `AddToBundleAsync`/`CreateAssetBundleAsync`/`RemoveBehaviorAsync`, deleted in `RemoveBehaviorAsync` when bundle becomes empty (line 360). The previous documentation was factually incorrect.

7. ~~**Behavior Stack system (not DI-registered)**~~: **FIXED** (2026-03-08) - Registered `IIntentStackMerger`, `IBehaviorStackRegistry`, and `ISituationalTriggerManager` as Singletons in `BehaviorServicePlugin.ConfigureServices()`. The behavior stack subsystem (multi-layer intent composition with category-based priority merging) is now discoverable by the Actor runtime via DI.

8. ~~**Cutscene Coordination system (not DI-registered)**~~: **FIXED** (2026-03-08) - Registered `ICutsceneCoordinator` as Singleton in `BehaviorServicePlugin.ConfigureServices()` with `ILoggerFactory` and `ITelemetryProvider` injection. `SyncPointManager` and `InputWindowManager` are per-session (created internally by `CutsceneSession`) and do not need DI registration. Note: coordinator uses in-memory `ConcurrentDictionary` for session state — Design Consideration #4 tracks the future requirement for Redis-backed distributed state when cinematics become active.

9. ~~**Document Merger (not DI-registered)**~~: **FIXED** (2026-03-08) - Extracted `IDocumentMerger` interface, registered `DocumentMerger` as Singleton in `BehaviorServicePlugin.ConfigureServices()`. The class is stateless and thread-safe, making it suitable for Singleton lifetime. Consumers can now inject `IDocumentMerger` for compile-time behavior composition.

---

## Potential Extensions

1. **Additional optimizer passes**: The `BytecodeOptimizer` currently implements 3 peephole passes (push-pop elimination, duplicate constant, constant folding). Additional passes could include dead-code elimination, branch simplification, and jump threading. The compiler architecture (multi-phase with separate emitter) supports adding passes to `BytecodeOptimizer.Optimize()`.
<!-- AUDIT:NEEDS_DESIGN:2026-03-08:https://github.com/beyond-immersion/bannou-service/issues/617 -->

2. **Hot-reload**: Use `BehaviorModelCache.Invalidate()` combined with `behavior.updated` event consumption to automatically refresh cached interpreters when behaviors are recompiled, enabling live behavior iteration without service restart.
<!-- AUDIT:NEEDS_DESIGN:2026-03-08:https://github.com/beyond-immersion/bannou-service/issues/618 -->

3. **Plan visualization endpoint**: Expose the stored GOAP metadata (from `goap-metadata:` state entries) as a queryable endpoint for debugging tools to visualize A* search trees and plan sequences.
<!-- AUDIT:NEEDS_DESIGN:2026-03-08:https://github.com/beyond-immersion/bannou-service/issues/619 -->

4. **Embedding memory store**: Implement `IMemoryStore` using vector embeddings for semantic similarity search. The interface contract (FindRelevantAsync, StoreExperienceAsync) is stable and the `ActorLocalMemoryStore` can be replaced without cognition pipeline changes. *(See also Stub #3 — same gap tracked there.)*
<!-- AUDIT:NEEDS_DESIGN:2026-03-08:https://github.com/beyond-immersion/bannou-service/issues/606 -->

5. **Parallel plan evaluation**: For NPCs with multiple active goals, run A* searches concurrently using the thread-safe `GoapPlanner` (all state is method-local). Currently goals are evaluated sequentially in validation.
<!-- AUDIT:NEEDS_DESIGN:2026-03-08:https://github.com/beyond-immersion/bannou-service/issues/620 -->

6. **Compilation caching by content hash**: Use the `contentHash` field from `BehaviorCompilationFailedEvent` to implement deduplication - skip recompilation of identical ABML content that has already been compiled successfully.
<!-- AUDIT:NEEDS_DESIGN:2026-03-08:https://github.com/beyond-immersion/bannou-service/issues/622 -->

7. **Bytecode version contract** (Issue #158): Formal versioning for bytecode format to ensure server-compiled bytecode and client SDK interpreters stay compatible across deployments. Header infrastructure exists (`BehaviorModelHeader` with magic bytes and `CurrentVersion = 1`), but the interpreter performs no version validation on load. Design decisions needed: backward compatibility policy, graceful degradation on mismatch, integration with hot-reload.
<!-- AUDIT:NEEDS_DESIGN:2026-03-08:https://github.com/beyond-immersion/bannou-service/issues/158 -->

8. **GOAP WorldState external data** (Issue #148): `GoapWorldStateProvider` pattern for injecting external service data into GOAP planning world state, enabling richer NPC decision-making.
<!-- AUDIT:NEEDS_DESIGN:2026-03-08:https://github.com/beyond-immersion/bannou-service/issues/148 -->

9. **ABML template inheritance** (Issue #384): `extends` and `abstract` keywords for behavior documents, enabling hierarchical behavior composition without copy-paste.
<!-- AUDIT:NEEDS_DESIGN:2026-03-08:https://github.com/beyond-immersion/bannou-service/issues/384 -->

10. **ABML economic action handlers** (Issue #428): Purpose-built ABML actions for economic operations (currency transfer, item creation, escrow management) following the same template-based pattern as `emit_event`.
<!-- AUDIT:NEEDS_DESIGN:2026-03-08:https://github.com/beyond-immersion/bannou-service/issues/428 -->

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

1. ~~**T16 violation: Bundle event topics use Pattern B (forbidden)**~~: **FIXED** — Changed 3 bundle topics in `behavior-events.yaml` from Pattern B (`behavior-bundle.{action}`) to Pattern C (`behavior.bundle.{action}`). Regeneration needed to update generated publisher.

### Intentional Quirks

1. **WorldState hash collisions in A* closed set**: The closed set uses `int` hash codes (`GetHashCode()`) for visited-state tracking rather than full state equality. With 32-bit hashes and potentially thousands of world states explored, hash collisions can cause two failure modes:
   - **False positive (skip valid state)**: If state A and state B hash to the same value, and A is explored first, B will be incorrectly skipped as "already visited" even though it's a different state with different goal distance.
   - **Missed optimization**: A shorter path to the same logical state might be skipped.

   The `ComputeHashCode` implementation orders keys before hashing for determinism, but collision probability follows the birthday problem: with ~77,000 unique states, there's a 50% chance of at least one collision. In practice, the node limits (200-1000) keep exploration well below this threshold, and the timeout bounds provide a safety net. This is a standard performance tradeoff in game AI planners—full state equality comparison on every closed-set check would be prohibitively expensive.

2. **Constant pool pre-seeds 0, 1, -1**: `ConstantPoolBuilder.AddCommonConstants()` pre-allocates indices 0-2 for common numeric literals. User constants start at index 3. The pre-seeded constants count against the `CompilerMaxConstants` limit (256 default), so users actually have 253 available slots.

3. **Two Redis stores for behavior data**: lib-behavior uses two separate state stores: `behavior-statestore` for behavior metadata, bundle membership, and GOAP metadata; and `agent-memories` for cognition pipeline memory storage (via `ActorLocalMemoryStore`). This separation ensures high-volume memory writes from many concurrent actors don't contend with behavior compilation metadata operations.

4. **No TTL on memory entries**: Memory entries in state have no expiration. Eviction is handled by the per-entity memory limit (default 100): when a new memory is stored and the index exceeds capacity, the oldest entries are automatically trimmed and their state store records deleted.

5. **GOAP action cost cannot be negative**: `GoapAction` constructor throws `ArgumentOutOfRangeException` for negative costs. This prevents modeling "rewarding" actions (negative cost = preferred) which some GOAP implementations allow for bonus objectives.

6. **Semantic analyzer reuse is fragile**: The `SemanticAnalyzer` clears its internal state (`_errors`, `_definedFlows`, etc.) at the start of each `Analyze()` call. This is safe for sequential use but the instance should not be shared across concurrent compilations despite appearing stateless from the outside.

7. **WorldState immutability creates GC pressure**: Every `SetNumeric`, `SetBoolean`, `SetString`, and `ApplyEffects` call creates a new `ImmutableDictionary` and thus a new `WorldState` instance. During A* search with many node expansions (up to 1000 at low urgency), this generates significant short-lived object allocations.

8. **Forward search over regressive search**: The GOAP planner uses forward A* search (from current state toward goal) rather than the regressive search recommended by Jeff Orkin's original F.E.A.R. implementation. This is a deliberate tradeoff: regressive search is more efficient for boolean-only world state (direct precondition→effect matching), but Bannou's numeric state with delta effects (`energy += 5`) and inequality conditions (`energy >= 10`) makes regressive search impractical without essentially simulating forward search. See "GOAP Implementation Notes" section for detailed analysis.

9. **'in' operator requires array literal with max 16 elements**: The `in` operator in ABML expressions only supports static array literals (`x in ['a', 'b', 'c']`), not dynamic arrays. Additionally, the array is limited to 16 elements maximum (line 244 of StackExpressionCompiler.cs). Arrays with more elements produce a compiler error suggesting pre-computed boolean flags. The expansion emits short-circuit OR chains.

10. **Array literals unsupported outside 'in' operator**: Standalone array literals in bytecode (`var arr = [1, 2, 3]`) are not supported. The compiler emits an error: "Array literals are only supported with the 'in' operator in bytecode." Dynamic collections require cloud-side execution.

11. **ClearAsync deletes memories sequentially**: Clearing an entity's memories iterates through each memory ID and issues individual delete calls (lines 234-237 of ActorLocalMemoryStore.cs). An entity with 100 memories generates 101 state store operations (100 deletes + 1 index delete).

12. **Unreachable code in BinaryOperator switch**: The `BinaryOperator.In` case at line 207 of StackExpressionCompiler.cs is dead code - the `in` operator is handled by `CompileInOperator` at lines 181-184 before the switch is reached. The throw statement can never execute. Kept as defensive code for future refactoring safety.

13. **BehaviorModelCache.GetInterpreter race condition on cold cache**: At lines 142-158 of BehaviorModelCache.cs, two concurrent threads calling `GetInterpreter` for the same character/type/variant can both miss the cache check (line 144), both create separate `BehaviorModelInterpreter` instances (line 155), and both write to the cache via direct assignment. The last writer wins and the earlier caller's interpreter is evicted from the cache while potentially still in use. In practice benign because actors run single-threaded behavior loops, so concurrent access to the same character's interpreter doesn't occur.

14. **IAssetClient uses soft L3 dependency pattern**: BehaviorService and BehaviorBundleManager resolve `IAssetClient` via `IServiceProvider.GetService<IAssetClient>()` with graceful degradation (null check + reduced functionality) per SERVICE-HIERARCHY.md L3 soft dependency rules. When Asset service is not loaded, cache operations return `ServiceUnavailable` and bundle creation skips asset upload. Compilation and GOAP planning work independently of Asset availability.

15. **ControlGateManager uses in-memory-only state (intentional)**: `ControlGateManager` (Singleton) stores entity control gates in a `ConcurrentDictionary` with no distributed backing. In multi-instance deployments, control state is node-local — an entity in a cinematic on Node A appears as "Behavior" (default) on Node B. This is intentional: the cinematic coordination system (`CinematicRunner`, `BehaviorOutputMask`) is not wired into any API endpoint, so node-local state has no observable consequences. When cinematics are exposed via API endpoints, the gate store must be backed by Redis via lib-state — the conversion is straightforward since `IControlGateRegistry` already abstracts the storage.

16. **Generic service call actions are forbidden**: The SemanticAnalyzer blocks `service_call`, `api_call`, `http_call`, `mesh_call`, and `invoke_service` domain actions at compile time with `SemanticErrorKind.ForbiddenDomainAction`. The IntentEmitterRegistry also rejects these at runtime as defense-in-depth. All service interactions must use purpose-built actions (e.g., `load_snapshot`, `actor_command`, `spawn_watcher`). See issue #296.

17. **Memory index unconditional save fallback after retry exhaustion**: `AddToMemoryIndexAsync` and `RemoveFromMemoryIndexAsync` use ETag-based optimistic concurrency with 3 retries (configurable via `MemoryStoreMaxRetries`). If all retries fail, they fall back to an unconditional save (re-read, modify, save without ETag). This creates a TOCTOU window where concurrent updates between the re-read and save could be lost. This is an intentional safety valve: actor behavior loops are single-threaded (see Quirk #13), making concurrent writes to the same entity's memory index extremely unlikely. In the rare case of contention (e.g., multi-node failover), losing one concurrent index entry is less harmful than silently dropping the memory entirely. The same fallback pattern exists in `RemoveFromMemoryIndexAsync` (lines 387-396). Orphaned memory records (in the store but not the index) have no functional impact — they are unreachable but bounded by the memory limit eviction cycle.

### Design Considerations (Requires Planning)

1. ~~**Memory store loads up to DefaultMemoryLimit memories for relevance scoring**~~: **FIXED** (2026-03-08) - The original concern stated "older memories beyond the limit are never scored for relevance." This was misleading: eviction in `AddToMemoryIndexAsync` permanently deletes memories beyond `DefaultMemoryLimit` (both from the index and from the state store), so those "older memories" don't exist — they've been evicted (see Intentional Quirk #4). `FindRelevantAsync` actually loads ALL stored memories because the index is always capped at `DefaultMemoryLimit`. The real observation is that `DefaultMemoryLimit` serves as both storage cap and retrieval window — if someone wants to store 500 memories but only score the top 200 for performance, they cannot (both are the same config). This coupling is acceptable for the MVP keyword-based store; a future implementation could introduce a separate `MemoryRelevanceScanLimit` config property if needed.

2. ~~**No plan cost upper bound**~~: **FIXED** (2026-03-08) - Added `MaxCostBound` property to `PlanningOptions` (nullable float, default null = no limit) and corresponding cost bound pruning in `GoapPlanner.PlanAsync()`. Nodes whose accumulated gCost exceeds the bound are skipped during A* expansion. Added `maxCostBound` to `GoapPlanningOptions` in the API schema and wired through `BehaviorService.GenerateGoapPlanAsync()`. Backward compatible — existing callers with no `maxCostBound` see no behavior change.

3. **ValidateAbml runs full compilation pipeline**: The `/validate` endpoint calls `_compiler.CompileYaml()` which executes the entire pipeline including flow compilation and bytecode emission. The generated bytecode is discarded. This wastes CPU for validation-only requests - could use a validation-specific compiler path that stops after semantic analysis.
<!-- AUDIT:NEEDS_DESIGN:2026-03-08:https://github.com/beyond-immersion/bannou-service/issues/624 -->

4. ~~**ControlGateManager uses in-memory-only state**~~: **FIXED** (2026-03-08) - Reclassified as Intentional Quirk #15. Investigation confirmed the in-memory state is deliberately chosen: the cinematic coordination system has no API endpoints, so node-local state has no observable consequences. The migration path (Redis-backed `IControlGateRegistry`) is clear and straightforward when cinematics are exposed via API.

5. ~~**VmConfig hardcoded limits not configurable**~~: **FIXED** (2026-03-08) - Investigation found no T21 violation. Of the 5 listed constants: MaxRegisters (256) is a bytecode format constraint (byte-indexed register file, architectural maximum like "bits per byte" — exempt per IMPLEMENTATION TENETS mathematical constants exception); MaxJumpOffset (65535) is a 16-bit instruction encoding constraint (same exemption); MaxInstructions, MaxFunctionArgs, and MaxNestingDepth are dead code (defined in VmConfig but never referenced by any code). DefaultCacheSize (10000, used by behavior-expressions SDK) is in the exempt standalone SDK layer per FOUNDATION TENETS schema-first exceptions. MaxConstants and MaxStrings are already configurable via BehaviorServiceConfiguration.

6. **GOAP planner returns null silently for multiple failure modes**: `PlanAsync` returns `null` without indicating cause when: (a) no actions available, (b) timeout exceeded, (c) cancellation requested, (d) node limit reached without finding goal. Callers cannot distinguish between "no valid plan exists" and "ran out of resources."
<!-- AUDIT:NEEDS_DESIGN:2026-03-08:https://github.com/beyond-immersion/bannou-service/issues/625 -->

7. ~~**Memory index update forces save after retry exhaustion**~~: **FIXED** (2026-03-09) - Reclassified as Intentional Quirk #17. Investigation confirmed the unconditional save fallback is an intentional safety valve: actor behavior loops are single-threaded (Quirk #13), making concurrent writes to the same entity's memory index extremely unlikely. In the rare case of contention (multi-node failover), losing one concurrent index entry is less harmful than silently dropping the memory entirely. Orphaned memory records have no functional impact (unreachable but bounded by memory limit eviction).

8. **GOAP failure response discards actual search effort**: In `GenerateGoapPlanAsync`, when `PlanAsync` returns null (timeout, node limit, no path), the response hardcodes `PlanningTimeMs = 0` and `NodesExpanded = 0`. The actual time spent searching and nodes expanded before failure are lost because these statistics are only available on the `GoapPlan` object which is null on failure. Callers cannot distinguish "instant failure (no actions)" from "searched 1000 nodes for 100ms and gave up." *(Same root cause as DC #6 — tracked together.)*
<!-- AUDIT:NEEDS_DESIGN:2026-03-08:https://github.com/beyond-immersion/bannou-service/issues/625 -->

---

## Domain Actions Reference

### Event-to-Character Communication

Two purpose-built ABML actions enable Event Brain actors to communicate with Character Brain actors:

#### actor_command (fire-and-forget)

Sends a command to a Character Brain actor via perception injection. The command is delivered as a perception of type `command:{commandName}`.

```yaml
- actor_command:
    target: ${attacker.actor_id}    # Required - expression evaluating to actor ID
    command: engage_target          # Required - command name (identifier)
    urgency: 0.8                    # Optional - default 0.7
    params:                         # Optional - command parameters
      target_id: ${defender.character_id}
      strategy: aggressive
```

**Parameters:**
- `target` (required): Expression evaluating to the target actor's ID
- `command` (required): Command name (must be a valid identifier: alphanumeric + underscore)
- `urgency` (optional): Perception urgency, 0.0-1.0 (default: 0.7)
- `params` (optional): Dictionary of parameters passed with the command

**Semantic Validation:** The compiler validates that `target` and `command` are present, and that `command` is a valid identifier.

**Character Brain Reception:** Character Brain behaviors can handle commands via flows named `on_command_{command_name}`:

```yaml
flows:
  on_command_engage_target:
    actions:
      - set:
          variable: current_target
          value: ${perception.params.target_id}
      - goto: { flow: combat_engage }
```

#### actor_query (request-response)

Queries a Character Brain actor for its current state/options and stores the result in a variable.

```yaml
- actor_query:
    target: ${defender.actor_id}    # Required - expression evaluating to actor ID
    query: combat_readiness         # Required - query type
    into: defender_status           # Required - variable to store result
    timeout: 1000                   # Optional - default 1000ms
```

**Parameters:**
- `target` (required): Expression evaluating to the target actor's ID
- `query` (required): Query type (combat, dialogue, exploration, or custom)
- `into` (required): Variable name to store the result (must be a valid identifier)
- `timeout` (optional): Query timeout in milliseconds (default: 1000)

**Semantic Validation:** The compiler validates that `target`, `query`, and `into` are present, and that `into` is a valid identifier.

**Result:** The query returns a list of `ActorOption` objects with:
- `ActionId`: Action identifier
- `Preference`: Preference score (0.0-1.0)
- `Available`: Whether the action is currently available
- `Risk`: Risk assessment (optional)
- `CooldownMs`: Cooldown remaining (optional)
- `Requirements`: List of requirements
- `Tags`: Action tags

**Example Usage:**

```yaml
flows:
  coordinate_attack:
    actions:
      # Query defender's readiness
      - actor_query:
          target: ${defender.actor_id}
          query: combat_readiness
          into: defender_status

      # Conditional based on query result
      - cond:
          - when: ${defender_status[0].preference < 0.3}
            then:
              # Defender is vulnerable, press the attack
              - actor_command:
                  target: ${attacker.actor_id}
                  command: engage_aggressive
                  params:
                    target_id: ${defender.character_id}
          - else:
              # Defender is ready, use caution
              - actor_command:
                  target: ${attacker.actor_id}
                  command: engage_defensive
                  params:
                    target_id: ${defender.character_id}
```

### Resource Loading

#### load_snapshot

Loads a resource snapshot and registers it as a variable provider for expression evaluation. Enables Event Brain actors to access character data (personality, history, encounters) via standard ABML expression syntax.

**Handler Location:** `plugins/lib-puppetmaster/Handlers/LoadSnapshotHandler.cs`

```yaml
- load_snapshot:
    name: candidate              # Required - provider name for expressions
    resource_type: character     # Required - resource type to load
    resource_id: ${target_id}    # Required - expression evaluating to GUID
    filter:                      # Optional - limit to specific source types
      - character-personality
      - character-history
```

**Parameters:**
- `name` (required): Provider name used in expression access (e.g., "candidate" enables `${candidate.personality.aggression}`)
- `resource_type` (required): Type of resource to load (e.g., "character")
- `resource_id` (required): Expression evaluating to the resource's GUID
- `filter` (optional): List of source types to include in the snapshot (e.g., `["character-personality", "character-history"]`)

**After loading, access via expressions:**
```yaml
- cond:
    - when: ${candidate.personality.aggression > 0.7}
      then:
        - log: "High aggression detected"
    - when: ${candidate.history.participations | length > 5}
      then:
        - log: "Experienced character"
```

**Implementation Notes:**
- Provider is registered in **root scope** (document-wide access)
- Uses `ResourceSnapshotCache` in lib-puppetmaster for TTL-based caching (5 minute default)
- If snapshot cannot be loaded, registers an empty provider (graceful degradation - returns null for all paths)
- The `resource_id` expression is evaluated at runtime, enabling dynamic resource loading
- The handler is provided by lib-puppetmaster (L4) and discovered via `GetServices<IActionHandler>()`

**Example - Event Brain loading both participants:**
```yaml
flows:
  main:
    actions:
      # Load attacker data
      - load_snapshot:
          name: attacker
          resource_type: character
          resource_id: ${attacker_id}
          filter:
            - character-personality
            - character-encounter

      # Load defender data
      - load_snapshot:
          name: defender
          resource_type: character
          resource_id: ${defender_id}
          filter:
            - character-personality
            - character-history

      # Use loaded data for decision making
      - cond:
          - when: ${attacker.personality.aggression > defender.personality.courage}
            then:
              - actor_command:
                  target: ${defender.actor_id}
                  command: intimidated
                  urgency: 0.8
```

#### resource_templates metadata (automatic filter defaults)

The `resource_templates` metadata field declares which resource snapshot types a behavior document uses. When specified, it provides automatic filtering optimization for `load_snapshot` actions that don't specify an explicit filter.

**Document metadata declaration:**
```yaml
abml: "2.0"
meta:
  id: combat-coordinator
  type: behavior
  description: Coordinates combat between NPCs
  resource_templates:
    - character-personality
    - character-history
    - character-encounter
```

**Behavior:**
- When `load_snapshot` has an explicit `filter` parameter, that filter is used (explicit takes priority)
- When `load_snapshot` has no filter (or empty filter), the declared `resource_templates` are used as the default
- When neither explicit filter nor `resource_templates` are declared, no filtering occurs (all snapshot entries loaded)

**Compile-time validation:**
- Template names must be lowercase with hyphens (regex: `^[a-z][a-z0-9]*(-[a-z0-9]+)*$`)
- Examples: `character-personality`, `realm-lore`, `quest-state`
- Invalid formats produce compilation errors (e.g., `CharacterPersonality`, `character_personality`)
- Duplicate template names produce warnings

**Example - no explicit filter uses declared templates:**
```yaml
abml: "2.0"
meta:
  id: encounter-evaluator
  resource_templates:
    - character-personality
    - character-encounter

flows:
  main:
    actions:
      # No filter specified - uses resource_templates as default
      - load_snapshot:
          name: candidate
          resource_type: character
          resource_id: ${target_id}
      # Equivalent to:
      # - load_snapshot:
      #     name: candidate
      #     resource_type: character
      #     resource_id: ${target_id}
      #     filter:
      #       - character-personality
      #       - character-encounter
```

**Template validation (Issue #294, implemented):** The `IResourceTemplateRegistry` is now fully integrated. The `SemanticAnalyzer` validates that declared template names exist in the registry via `HasTemplate()`, and validates expression paths (e.g., `${candidate.personality.aggression}`) against template schemas via `GetByNamespace()` + `ValidatePath()`. Unregistered templates produce warnings (not errors) since templates may be registered at runtime by plugins that aren't loaded during compilation.

#### prefetch_snapshots (batch cache warmup)

Batch-loads multiple resource snapshots into cache before iteration. Use before `foreach` loops to convert N sequential API calls into 1 batch call + N cache hits.

**Handler Location:** `plugins/lib-puppetmaster/Handlers/PrefetchSnapshotsHandler.cs`

```yaml
- prefetch_snapshots:
    resource_type: character
    resource_ids: ${participants | map('id')}  # Expression → List<Guid>
    filter:                                     # Optional
      - character-personality
      - character-history

- foreach:
    variable: candidate
    collection: ${participants}
    do:
      - load_snapshot:        # Cache hit - instant
          name: char
          resource_type: character
          resource_id: ${candidate.id}
```

**Parameters:**
- `resource_type` (required): Resource type (e.g., "character")
- `resource_ids` (required): Expression evaluating to a list of resource GUIDs
- `filter` (optional): List of source types to include in snapshots

**Behavior:**
- Prefetches all snapshots in parallel with bounded concurrency (max 5 concurrent)
- Logs success count but does not fail if some snapshots are missing
- Empty `resource_ids` list is a no-op (logs skip, returns Continue)
- Uses same cache as `load_snapshot` (5 minute TTL)

**Example - Prefetch before iterating raid participants:**
```yaml
flows:
  evaluate_raid:
    actions:
      # Prefetch all participant data upfront
      - prefetch_snapshots:
          resource_type: character
          resource_ids: ${raid.participants | map('character_id')}
          filter:
            - character-personality

      # Now iterate - all load_snapshot calls hit cache
      - foreach:
          variable: p
          collection: ${raid.participants}
          do:
            - load_snapshot:
                name: participant
                resource_type: character
                resource_id: ${p.character_id}
                filter:
                  - character-personality
            - cond:
                - when: ${participant.personality.aggression > 0.8}
                  then:
                    - actor_command:
                        target: ${p.actor_id}
                        command: lead_charge
```

### Watcher Management

Three purpose-built ABML actions enable Event Brain actors to manage regional watchers via the Puppetmaster service.

#### spawn_watcher

Spawns a new regional watcher for the specified realm.

**Handler Location:** `plugins/lib-puppetmaster/Handlers/SpawnWatcherHandler.cs`

```yaml
- spawn_watcher:
    watcher_type: regional           # Required - watcher type string
    realm_id: ${event.realmId}       # Required - realm GUID expression
    behavior_id: watcher-regional    # Optional - behavior document to use
    into: spawned_watcher_id         # Optional - variable to store watcher ID
```

**Parameters:**
- `watcher_type` (required): Type of watcher to spawn (e.g., "regional", "thematic", "dungeon")
- `realm_id` (required): Expression evaluating to the realm GUID
- `behavior_id` (optional): Behavior document ID for the watcher to execute
- `into` (optional): Variable name to store the created watcher's ID

**Example:**
```yaml
flows:
  on_realm_activated:
    actions:
      - spawn_watcher:
          watcher_type: regional
          realm_id: "${event.realmId}"
          into: regional_watcher_id
      - log: { message: "Started regional watcher: ${regional_watcher_id}" }
```

#### stop_watcher

Stops a running regional watcher.

**Handler Location:** `plugins/lib-puppetmaster/Handlers/StopWatcherHandler.cs`

```yaml
- stop_watcher:
    watcher_id: ${watcher_to_stop}   # Required - watcher GUID expression
```

**Parameters:**
- `watcher_id` (required): Expression evaluating to the watcher GUID to stop

**Example:**
```yaml
flows:
  handle_stop_watcher:
    actions:
      - log: { message: "Stopping watcher: ${event.watcherId}" }
      - stop_watcher:
          watcher_id: "${event.watcherId}"
```

#### list_watchers

Queries active watchers with optional filtering and stores results in a variable.

**Handler Location:** `plugins/lib-puppetmaster/Handlers/ListWatchersHandler.cs`

```yaml
- list_watchers:
    into: active_watchers            # Required - variable to store results
    realm_id: ${realm_id}            # Optional - filter by realm
    watcher_type: regional           # Optional - filter by type
```

**Parameters:**
- `into` (required): Variable name to store the list of watcher info objects
- `realm_id` (optional): Expression evaluating to realm GUID filter
- `watcher_type` (optional): Watcher type string filter

**Result:** The `into` variable receives a list of `WatcherInfo` objects with properties:
- `watcherId`: Watcher GUID
- `realmId`: Realm GUID
- `watcherType`: Type string
- `startedAt`: Start timestamp
- `behaviorRef`: Behavior document reference (nullable)
- `actorId`: Actor instance ID (nullable)

**Example:**
```yaml
flows:
  stop_realm_watchers:
    actions:
      - list_watchers:
          into: realm_watchers
          realm_id: "${event.realmId}"
      - foreach:
          variable: watcher
          collection: "${realm_watchers}"
          do:
            - stop_watcher:
                watcher_id: "${watcher.watcherId}"
            - log: { message: "Stopped watcher: ${watcher.watcherId}" }
```

### Event Publishing

#### emit_event (template-based event publishing)

Publishes a typed event using a registered template. Template owners (L3/L4 plugins) register `EventTemplate` instances with `IEventTemplateRegistry` on startup, and behavior authors reference templates by name with flat parameters.

**Handler Location:** `bannou-service/Abml/Execution/Handlers/EmitEventHandler.cs`

```yaml
- emit_event:
    template: encounter_resolved        # Required - registered template name
    encounterId: ${encounter.id}        # Template-specific parameters
    outcome: ${outcome}
    durationSeconds: ${duration}
```

**Parameters:**
- `template` (required): Name of a registered event template
- All other parameters are passed to the template for substitution (uses `{{paramName}}` placeholders)

**How Templates Work:**

Templates are registered by the plugin that owns the event type:

```csharp
// In CharacterEncounterServicePlugin.OnRunningAsync:
_eventTemplateRegistry.Register(new EventTemplate(
    Name: "encounter_resolved",
    Topic: "encounter.resolved",
    EventType: typeof(EncounterResolvedEvent),
    PayloadTemplate: @"{
        ""encounterId"": ""{{encounterId}}"",
        ""outcome"": ""{{outcome}}"",
        ""durationSeconds"": {{durationSeconds}}
    }",
    Description: "Encounter completed with outcome"
));
```

**Why Templates Instead of Generic `publish_event`:**

Per issue #296, generic service call actions (`service_call`, `api_call`, `publish_event`) are forbidden in ABML because:
1. They bypass security boundaries (behaviors could publish arbitrary events)
2. They create implicit coupling (ABML documents know infrastructure topics)
3. They prevent compile-time validation (no schema checking)

Template-based publishing ensures:
- Only pre-approved events can be published
- Parameter validation happens at template registration
- Topic ownership is enforced by the plugin system
- Behaviors remain declarative and infrastructure-agnostic

**Example - Combat encounter publishing outcome:**

```yaml
flows:
  resolve_combat:
    actions:
      # ... combat resolution logic ...

      - emit_event:
          template: encounter_resolved
          encounterId: ${encounter.id}
          outcome: ${winner == attacker ? 'attacker_victory' : 'defender_victory'}
          durationSeconds: ${(now - encounter.started_at) / 1000}
```

---

## Work Tracking

### Completed

- **2026-03-08**: Fixed stale documentation for Stub #1 (Bundle management partial) and Stub #6 (Bundle lifecycle events not published). Both were factually incorrect — `BehaviorBundleManager` is fully implemented with event publishing. Updated Published Events table to remove incorrect "not yet published" annotations. Fixed GOAP topic typo (`behavior.goap.plan-generated` → `behavior.goap-plan-generated`). Added T16 bug for Pattern B topic naming in bundle events.
- **2026-03-08**: Wired `BytecodeOptimizer` into `CompilationContext.Finalize()` (Stub #5). The optimizer was fully implemented but never called — added conditional invocation when `EnableOptimizations` is true. Updated Potential Extension #1 to reflect that 3 peephole passes already exist.
- **2026-03-08**: Registered Behavior Stack subsystem in DI (Stub #7). Added `IIntentStackMerger`, `IBehaviorStackRegistry`, and `ISituationalTriggerManager` as Singletons in `BehaviorServicePlugin.ConfigureServices()`. The subsystem was complete but inactive due to missing DI registrations.
- **2026-03-08**: Registered `ICutsceneCoordinator` as Singleton in DI (Stub #8). The cutscene coordination subsystem was complete but undiscoverable due to missing DI registration. `SyncPointManager` and `InputWindowManager` are per-session and don't need DI registration.
- **2026-03-08**: Extracted `IDocumentMerger` interface and registered `DocumentMerger` as Singleton in DI (Stub #9). The document merger was complete but had no interface and no DI registration, making it undiscoverable for compile-time behavior composition.
- **2026-03-08**: Audit pass — added AUDIT:NEEDS_DESIGN markers to Potential Extensions #7-#10 (bytecode version contract, GOAP WorldState external data, ABML template inheritance, ABML economic action handlers). All reference existing open GitHub issues. Enriched PE #7 description with investigation findings: header infrastructure already exists (`BehaviorModelHeader` with magic bytes and `CurrentVersion = 1`) but interpreter performs no version validation.
- **2026-03-08**: Fixed misleading Design Consideration #1 (memory store relevance scoring). The original text implied "older memories beyond the limit are never scored" — investigation confirmed those memories don't exist (eviction permanently deletes them per Quirk #4). Reframed to accurately describe the storage/retrieval limit coupling.
- **2026-03-08**: Added `MaxCostBound` to GOAP planner (Design Consideration #2). Added nullable float property to `PlanningOptions`, cost bound pruning in `GoapPlanner.PlanAsync()`, `maxCostBound` field to `GoapPlanningOptions` API schema, and wired through `BehaviorService.GenerateGoapPlanAsync()`. Backward compatible.
- **2026-03-08**: Reclassified Design Consideration #4 (ControlGateManager in-memory state) as Intentional Quirk #15. Investigation confirmed the in-memory state is deliberate: cinematic coordination has no API endpoints, so node-local state has no observable consequences. Migration path (Redis-backed `IControlGateRegistry`) is clear for when cinematics are externally exposed.
- **2026-03-08**: Resolved Design Consideration #5 (VmConfig hardcoded limits). Investigation found no T21 violation: MaxRegisters and MaxJumpOffset are bytecode format architectural constraints (exempt as mathematical constants); MaxInstructions, MaxFunctionArgs, MaxNestingDepth are dead code (defined but never referenced); DefaultCacheSize is in the exempt SDK layer; MaxConstants and MaxStrings are already configurable.
- **2026-03-09**: Reclassified Design Consideration #7 (memory index unconditional save fallback) as Intentional Quirk #17. Investigation of `ActorLocalMemoryStore.AddToMemoryIndexAsync` (lines 271-352) confirmed the unconditional save fallback is an intentional safety valve: actor behavior loops are single-threaded (Quirk #13), making concurrent writes extremely unlikely; losing one concurrent index entry on fallback is less harmful than dropping the memory entirely.

### AUDIT Markers

- **Stub #2**: Cinematic extension delivery — NEEDS_DESIGN (#603)
- **Stub #3**: Embedding-based memory store — NEEDS_DESIGN (#606)
- **Stub #4**: GOAP plan persistence — NEEDS_DESIGN (#608)
- **PE #1**: Additional optimizer passes — NEEDS_DESIGN (#617)
- **PE #2**: Hot-reload — NEEDS_DESIGN (#618)
- **PE #3**: Plan visualization endpoint — NEEDS_DESIGN (#619)
- **PE #4**: Embedding memory store — NEEDS_DESIGN (#606)
- **PE #5**: Parallel plan evaluation — NEEDS_DESIGN (#620)
- **PE #6**: Compilation caching by content hash — NEEDS_DESIGN (#622)
- **PE #7**: Bytecode version contract — NEEDS_DESIGN (#158)
- **PE #8**: GOAP WorldState external data — NEEDS_DESIGN (#148)
- **PE #9**: ABML template inheritance — NEEDS_DESIGN (#384)
- **PE #10**: ABML economic action handlers — NEEDS_DESIGN (#428)
- **DC #3**: ValidateAbml runs full compilation pipeline — NEEDS_DESIGN (#624)
- **DC #6 + DC #8**: GOAP planner silent failure modes + discarded search effort — NEEDS_DESIGN (#625)

### Implementation Gaps

No implementation gaps identified requiring AUDIT markers.
