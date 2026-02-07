# Behavior Plugin Deep Dive

> **Plugin**: lib-behavior
> **Schema**: schemas/behavior-api.yaml
> **Version**: 3.0.0
> **State Stores**: behavior-statestore (Redis)

---

## Overview

ABML (Arcadia Behavior Markup Language) compiler and GOAP (Goal-Oriented Action Planning) runtime for NPC behavior management. The plugin provides three core subsystems: (1) a multi-phase ABML compiler pipeline (YAML parse, semantic analysis, variable registration, flow compilation, bytecode emission) that produces stack-based bytecode for a custom instruction set with 50+ opcodes across 7 categories, (2) an A* GOAP planner with urgency-tiered search parameters (low/medium/high) producing action sequences from world state and goal conditions, and (3) a 5-stage cognition pipeline (attention filtering, significance assessment, memory formation, goal impact evaluation, intention formation) with keyword-based memory retrieval. Supports streaming composition via continuation points and extension attachment, variant-based model caching with fallback chains, and behavior bundling through the asset service. The compiler outputs portable bytecode interpreted by both server and client SDKs.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Redis persistence for behavior metadata, bundle membership, GOAP metadata |
| lib-messaging (`IMessageBus`) | Publishing behavior lifecycle events, compilation failure events, GOAP plan events, cinematic extension events; error event publishing |
| lib-asset (`IAssetClient`) | Storing and retrieving compiled bytecode via pre-signed URLs |
| `IHttpClientFactory` | HTTP client for asset upload operations |
| bannou-service (ABML Parser: `DocumentParser`) | YAML-to-AST parsing of ABML documents |
| bannou-service (ABML Documents) | `AbmlDocument`, `Flow`, `ActionNode` and subtypes for AST representation |
| bannou-service (Runtime types) | `BehaviorOpcode`, `BehaviorModel`, `BehaviorModelInterpreter`, `BehaviorModelType`, `IntentChannel` |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-actor | Consumes compiled behavior bytecode for NPC brain execution; uses GOAP planner for action selection |
| lib-character | References behavior metadata for character enrichment |

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
| `goap-metadata:{planId}` | behavior | GOAP metadata JSON | Generated plan metadata for debugging |
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
| `behavior-bundle.created` | `BehaviorBundleCreatedEvent` | Bundle created (lifecycle) |
| `behavior-bundle.updated` | `BehaviorBundleUpdatedEvent` | Bundle updated (lifecycle) |
| `behavior-bundle.deleted` | `BehaviorBundleDeletedEvent` | Bundle deleted (lifecycle) |
| `behavior.compilation-failed` | `BehaviorCompilationFailedEvent` | ABML compilation fails (monitoring/alerting) |
| `behavior.goap.plan-generated` | `GoapPlanGeneratedEvent` | GOAP planner generates new plan |
| `character.cinematic_extension` | `CinematicExtensionAvailableEvent` | Cinematic extension available for injection at continuation point |

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
| `IStateStoreFactory` | Singleton | Access to behavior-statestore |
| `IMessageBus` | Scoped | Event publishing and error events |
| `IAssetClient` | Scoped | Asset service integration for bytecode storage |
| `IHttpClientFactory` | Scoped | HTTP client for asset upload operations |
| `IEventConsumer` | Scoped | Event consumer registration (lifecycle) |
| `IGoapPlanner` (via `GoapPlanner`) | Per-use (stateless) | A* search for GOAP planning |
| `BehaviorCompiler` | Per-use (stateless) | ABML-to-bytecode compilation pipeline |
| `SemanticAnalyzer` | Per-use (internal to compiler) | Pre-compilation validation pass |
| `IBehaviorBundleManager` | Scoped | Bundle creation and management |
| `BehaviorModelCache` | Singleton (in-memory) | Per-character interpreter caching with fallback chains |
| `IMemoryStore` (via `ActorLocalMemoryStore`) | Scoped | Keyword-based memory storage and retrieval (uses `agent-memories` store owned by Actor) |
| `CognitionConstants` | Static | Configurable cognition pipeline thresholds (initialized from config) |

Service lifetime is **Scoped** (per-request). `BehaviorModelCache` is a singleton in-memory cache using `ConcurrentDictionary`. `CognitionConstants` is static but initialized from `BehaviorServiceConfiguration` once at service startup.

---

## API Endpoints (Implementation Notes)

### ABML Operations (2 endpoints)

- **CompileAbmlBehavior** (`/compile`): Accepts ABML YAML string with optional compilation options (debug info, optimizations, skip semantic analysis, model ID, max constants/strings). Invokes the multi-phase compiler pipeline: YAML parsing via `DocumentParser`, semantic analysis (unless skipped), variable registration from context block, flow compilation with action compiler registry, bytecode emission with label patching. On success: returns compiled bytecode (base64), model metadata (inputs, outputs, continuation points, debug line map), and bytecode size. On failure: returns error list with messages and optional line numbers. Publishes `behavior.compilation-failed` event with content hash for deduplication on compilation errors. Stores compiled behavior metadata in state store. For successful compilation: publishes `behavior.created` or `behavior.updated` lifecycle event.

- **ValidateAbml** (`/validate`): Validates ABML YAML by running the full compilation pipeline (including bytecode emission) then discarding the bytecode and returning only the error/success status. Despite the "validate-only" intent, calls `_compiler.CompileYaml()` which performs the same multi-phase pipeline as `/compile`. Returns validation result with `isValid` flag and error list (undefined flows, empty conditionals, invalid continuation points, type mismatches). Semantic warnings (unreachable code, unused flows, etc.) are propagated via `result.Warnings` when `StrictMode=true` enables semantic analysis. When `StrictMode=false` (default), semantic analysis is skipped entirely and no warnings are produced. Does not modify state or publish events.

### Cache Operations (2 endpoints)

- **GetCachedBehavior** (`/cache/get`): Retrieves previously compiled behavior metadata and bytecode by behavior ID. Looks up behavior metadata from state store using configured key prefix. If found, retrieves the compiled bytecode asset via mesh invocation to the asset service. Returns the full compiled model with metadata, bytecode (base64), and bundle membership information.

- **InvalidateCachedBehavior** (`/cache/invalidate`): Invalidates cached behavior by ID. Removes the behavior metadata entry from the state store. Invalidates any in-memory `BehaviorModelCache` entries referencing this behavior. Publishes `behavior.deleted` lifecycle event. Returns confirmation of invalidation.

### GOAP Operations (2 endpoints)

- **GenerateGoapPlan** (`/goap/plan`): Accepts a goal definition (ID, name, priority, conditions), current world state (key-value pairs), available actions (ID, name, preconditions, effects, cost), and optional planning options (urgency for automatic parameter selection, or explicit max depth/timeout/max nodes/heuristic weight). Converts request data to runtime types via `GoapMetadataConverter`. If urgency is provided, maps to tiered `PlanningOptions` via `UrgencyBasedPlanningOptions.FromUrgency()` (low < 0.3: depth 10, 100ms, 1000 nodes; medium 0.3-0.7: depth 6, 50ms, 500 nodes; high > 0.7: depth 3, 20ms, 200 nodes). Invokes `GoapPlanner.PlanAsync` with A* search. On success: returns ordered action sequence, total cost, nodes expanded, planning time in ms, initial state, expected final state. On failure (no plan found): returns null with planning diagnostics. Publishes `behavior.goap.plan-generated` event with plan metadata. Stores GOAP metadata in state store for debugging.

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

1. **Bundle management partial**: `IBehaviorBundleManager` interface is defined and injected but the full bundle lifecycle (creation from multiple behaviors, versioning, metabundles) routes through the asset service. The local `BehaviorBundleManager` handles membership tracking in state but asset upload/download for bundles is delegated to lib-asset.

2. **Cinematic extension delivery**: The `CinematicExtensionAvailableEvent` is published but the actual extension attachment to a running interpreter (matching `continuationPointName` to an active `ContinuationPoint` opcode) is handled by the actor runtime, not the behavior service itself.

3. **Embedding-based memory store**: `IMemoryStore` interface is designed for swappable implementations. Only `ActorLocalMemoryStore` (keyword-based) exists. The embedding-based implementation for semantic similarity matching is documented as a future migration path in ACTOR-SYSTEM.md section 7.3.

4. **GOAP plan persistence**: Plans are stored in state for debugging (`goap-metadata:` prefix) but there is no retrieval endpoint or plan history query. The `GoapPlanGeneratedEvent` provides the analytics trail instead.

5. **Compiler optimizations**: `CompilationOptions.EnableOptimizations` flag exists and `CompilationOptions.Release` preset enables it, but no optimization passes are currently implemented in the compiler pipeline. The flag is a placeholder for future dead-code elimination, constant folding, etc.

---

## Potential Extensions

1. **Optimizer passes**: Implement constant folding, dead-code elimination, and branch simplification for the `EnableOptimizations` flag. The compiler architecture (multi-phase with separate emitter) supports inserting optimization passes between flow compilation and finalization.

2. **Hot-reload**: Use `BehaviorModelCache.Invalidate()` combined with `behavior.updated` event consumption to automatically refresh cached interpreters when behaviors are recompiled, enabling live behavior iteration without service restart.

3. **Plan visualization endpoint**: Expose the stored GOAP metadata (from `goap-metadata:` state entries) as a queryable endpoint for debugging tools to visualize A* search trees and plan sequences.

4. **Embedding memory store**: Implement `IMemoryStore` using vector embeddings for semantic similarity search. The interface contract (FindRelevantAsync, StoreExperienceAsync) is stable and the `ActorLocalMemoryStore` can be replaced without cognition pipeline changes.

5. **Parallel plan evaluation**: For NPCs with multiple active goals, run A* searches concurrently using the thread-safe `GoapPlanner` (all state is method-local). Currently goals are evaluated sequentially in validation.

6. **Compilation caching by content hash**: Use the `contentHash` field from `BehaviorCompilationFailedEvent` to implement deduplication - skip recompilation of identical ABML content that has already been compiled successfully.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

No bugs identified.

### Intentional Quirks

1. **WorldState hash collisions in A* closed set**: The closed set uses `int` hash codes (`GetHashCode()`) for visited-state tracking rather than full state equality. With 32-bit hashes and potentially thousands of world states explored, hash collisions can cause two failure modes:
   - **False positive (skip valid state)**: If state A and state B hash to the same value, and A is explored first, B will be incorrectly skipped as "already visited" even though it's a different state with different goal distance.
   - **Missed optimization**: A shorter path to the same logical state might be skipped.

   The `ComputeHashCode` implementation orders keys before hashing for determinism, but collision probability follows the birthday problem: with ~77,000 unique states, there's a 50% chance of at least one collision. In practice, the node limits (200-1000) keep exploration well below this threshold, and the timeout bounds provide a safety net. This is a standard performance tradeoff in game AI planners—full state equality comparison on every closed-set check would be prohibitively expensive.

2. **Constant pool pre-seeds 0, 1, -1**: `ConstantPoolBuilder.AddCommonConstants()` pre-allocates indices 0-2 for common numeric literals. User constants start at index 3. The pre-seeded constants count against the `CompilerMaxConstants` limit (256 default), so users actually have 253 available slots.

### Design Considerations (Requires Planning)

1. **Memory store loads up to DefaultMemoryLimit memories for relevance scoring**: `FindRelevantAsync` calls `GetAllAsync(entityId, _configuration.DefaultMemoryLimit, ct)` which fetches the last N entries from the index (via `TakeLast(limit)`). With default limit of 100 and 10 perceptions, this is 1000 relevance calculations per query. Older memories beyond the limit are never scored for relevance, even if they would be highly relevant - the limit acts as an implicit recency bias independent of the recency bonus in scoring.

2. **Two Redis stores for behavior data**: lib-behavior uses two separate state stores: `behavior-statestore` for behavior metadata, bundle membership, and GOAP metadata; and `agent-memories` for cognition pipeline memory storage (via `ActorLocalMemoryStore`). This separation ensures high-volume memory writes from many concurrent actors don't contend with behavior compilation metadata operations.

3. **No TTL on memory entries**: Memory entries in state have no expiration. Eviction is handled by the per-entity memory limit (default 100): when a new memory is stored and the index exceeds capacity, the oldest entries are automatically trimmed and their state store records deleted.

4. **GOAP action cost cannot be negative**: `GoapAction` constructor throws `ArgumentOutOfRangeException` for negative costs. This prevents modeling "rewarding" actions (negative cost = preferred) which some GOAP implementations allow for bonus objectives.

5. **Semantic analyzer reuse is fragile**: The `SemanticAnalyzer` clears its internal state (`_errors`, `_definedFlows`, etc.) at the start of each `Analyze()` call. This is safe for sequential use but the instance should not be shared across concurrent compilations despite appearing stateless from the outside.

6. **WorldState immutability creates GC pressure**: Every `SetNumeric`, `SetBoolean`, `SetString`, and `ApplyEffects` call creates a new `ImmutableDictionary` and thus a new `WorldState` instance. During A* search with many node expansions (up to 1000 at low urgency), this generates significant short-lived object allocations.

7. **No plan cost upper bound**: The A* planner has no mechanism to abandon search if the best found plan exceeds a cost threshold. It will explore all nodes up to the limit even if the cheapest partial plan already exceeds a practical budget.

8. **Forward search over regressive search**: The GOAP planner uses forward A* search (from current state toward goal) rather than the regressive search recommended by Jeff Orkin's original F.E.A.R. implementation. This is a deliberate tradeoff: regressive search is more efficient for boolean-only world state (direct precondition→effect matching), but Bannou's numeric state with delta effects (`energy += 5`) and inequality conditions (`energy >= 10`) makes regressive search impractical without essentially simulating forward search. See "GOAP Implementation Notes" section for detailed analysis.

9. **'in' operator requires array literal with max 16 elements**: The `in` operator in ABML expressions only supports static array literals (`x in ['a', 'b', 'c']`), not dynamic arrays. Additionally, the array is limited to 16 elements maximum (line 244 of StackExpressionCompiler.cs). Arrays with more elements produce a compiler error suggesting pre-computed boolean flags. The expansion emits short-circuit OR chains.

10. **ValidateAbml runs full compilation pipeline**: The `/validate` endpoint calls `_compiler.CompileYaml()` which executes the entire pipeline including flow compilation and bytecode emission. The generated bytecode is discarded. This wastes CPU for validation-only requests - could use a validation-specific compiler path that stops after semantic analysis.

11. **Array literals unsupported outside 'in' operator**: Standalone array literals in bytecode (`var arr = [1, 2, 3]`) are not supported. The compiler emits an error: "Array literals are only supported with the 'in' operator in bytecode." Dynamic collections require cloud-side execution.

12. **VmConfig hardcoded limits not configurable**: Several VM limits are hardcoded in `VmConfig.cs` and not exposed via service configuration: MaxRegisters (256), MaxInstructions (65536), MaxJumpOffset (65535), MaxFunctionArgs (16), MaxNestingDepth (100). Only MaxConstants and MaxStrings are configurable.

13. **GOAP planner returns null silently for multiple failure modes**: `PlanAsync` returns `null` without indicating cause when: (a) no actions available, (b) timeout exceeded, (c) cancellation requested, (d) node limit reached without finding goal. Callers cannot distinguish between "no valid plan exists" and "ran out of resources."

14. **Memory index update forces save after retry exhaustion**: If ETag-based optimistic concurrency fails 3 times (MemoryStoreMaxRetries), the memory index update falls back to unconditional save (lines 289-299 of ActorLocalMemoryStore.cs), potentially losing concurrent updates.

15. **ClearAsync deletes memories sequentially**: Clearing an entity's memories iterates through each memory ID and issues individual delete calls (lines 234-237 of ActorLocalMemoryStore.cs). An entity with 100 memories generates 101 state store operations (100 deletes + 1 index delete).

16. **Unreachable code in BinaryOperator switch**: The `BinaryOperator.In` case at line 207 of StackExpressionCompiler.cs is dead code - the `in` operator is handled by `CompileInOperator` at lines 181-184 before the switch is reached. The throw statement can never execute. Kept as defensive code for future refactoring safety.

17. **BehaviorModelCache.GetInterpreter race condition on cold cache**: At lines 142-158 of BehaviorModelCache.cs, two concurrent threads calling `GetInterpreter` for the same character/type/variant can both miss the cache check (line 144), both create separate `BehaviorModelInterpreter` instances (line 155), and both write to the cache via direct assignment. The last writer wins and the earlier caller's interpreter is evicted from the cache while potentially still in use. In practice benign because actors run single-threaded behavior loops, so concurrent access to the same character's interpreter doesn't occur.

18. **GOAP failure response discards actual search effort**: At lines 864-865 of BehaviorService.cs, when `PlanAsync` returns null (timeout, node limit, no path), the response hardcodes `PlanningTimeMs = 0` and `NodesExpanded = 0`. The actual time spent searching and nodes expanded before failure are lost because these statistics are only available on the `GoapPlan` object which is null on failure. Callers cannot distinguish "instant failure (no actions)" from "searched 1000 nodes for 100ms and gave up."

19. **Generic service call actions are forbidden**: The SemanticAnalyzer blocks `service_call`, `api_call`, `http_call`, `mesh_call`, and `invoke_service` domain actions at compile time with `SemanticErrorKind.ForbiddenDomainAction`. The IntentEmitterRegistry also rejects these at runtime as defense-in-depth. All service interactions must use purpose-built actions (e.g., `load_snapshot`, `actor_command`, `spawn_watcher`). See issue #296.

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

**Future enhancements (Issue #294):** When `IResourceTemplateRegistry` is implemented, the compiler will additionally validate that declared template names exist in the registry and that expression paths (e.g., `${candidate.personality.aggression}`) match the template schemas.

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

- **2026-02-06**: Issues #291-#293, #297, #299 - Added Event Brain domain actions: `load_snapshot`, `prefetch_snapshots`, `resource_templates` metadata, `actor_command`, `actor_query`, `emit_event`. See Domain Actions Reference section.
- **2026-02-06**: Issue #296 - Forbidden generic `service_call`/`api_call` actions in ABML. See quirk #19.

### AUDIT Markers

None active.

### Implementation Gaps

No implementation gaps identified requiring AUDIT markers.
