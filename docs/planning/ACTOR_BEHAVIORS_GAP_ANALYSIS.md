# Actor Behaviors Gap Analysis

> **Status**: Gap Analysis for ACTOR_BEHAVIORS.md Implementation
> **Date**: 2026-01-08
> **Peer Reviewed**: 2026-01-09
> **Related**: [ACTOR_BEHAVIORS.md](ACTOR_BEHAVIORS.md), [THE_DREAM.md](THE_DREAM.md), [THE_DREAM_GAP_ANALYSIS.md](THE_DREAM_GAP_ANALYSIS.md)

This document identifies gaps between the design in ACTOR_BEHAVIORS.md and current implementation.

**Note on ACTOR_BEHAVIORS.md**: The example YAML in ACTOR_BEHAVIORS.md includes both *implemented features* and *aspirational syntax*. Some constructs (e.g., `${agent.cognition.emotional_state}`, `layer_activate`, `emit_intent: channel: cognition`) are design proposals that require both design and implementation work, not just implementation.

---

## Executive Summary

The foundation is **significantly more complete than expected**. The core architecture exists:
- Single-actor cognition pipeline: **100% complete** (all 6 handlers implemented)
- ABML compilation and bytecode VM: **100% complete** (226+ compiler tests, 61 interpreter tests)
- Mapping infrastructure: **100% complete** (spatial queries, affordances, authority)
- Actor state management (feelings, goals, memories): **100% complete**
- Runtime BehaviorStack evaluation: **100% complete** (lib-behavior/Stack/)
- Cutscene coordination: **100% complete** (CutsceneCoordinator, SyncPointManager, InputWindowManager)

**Key gaps are small but critical**:
1. Character personality/backstory schema (schema change)
2. `CompileBehaviorStackAsync` service endpoint (currently returns NotImplemented)
3. Event actor architecture (THE_DREAM "Event Brain" - Phase 5)
4. Character data loading in ActorRunner startup

**Alignment with THE_DREAM**: These gaps correspond to THE_DREAM_GAP_ANALYSIS Phase 5 (Event Brain) remaining work. THE_DREAM_GAP_ANALYSIS estimates ~90% overall completion.

---

## 1. What's Fully Implemented

### 1.1 ActorRunner (`plugins/lib-actor/Runtime/ActorRunner.cs`)

| Feature | Status | Notes |
|---------|--------|-------|
| Bounded perception queue | ✅ Complete | DropOldest behavior, configurable size |
| Behavior loop execution | ✅ Complete | Configurable tick interval |
| CharacterId linking | ✅ Complete | Optional, extracted from actor ID |
| Perception subscription | ✅ Complete | Subscribes to `character.{characterId}.perceptions` |
| State update publishing | ✅ Complete | CharacterStateUpdateEvent to game server |
| ABML document execution | ✅ Complete | Lazy-load, hot-reload, cache invalidation |
| State persistence | ✅ Complete | Configurable auto-save interval |
| Graceful shutdown | ✅ Complete | Waits for loop completion |

### 1.2 ActorState (`plugins/lib-actor/Runtime/ActorState.cs`)

| Feature | Status | Notes |
|---------|--------|-------|
| Feelings storage | ✅ Complete | Dictionary<string, double>, 0.0-1.0 |
| Goal management | ✅ Complete | Primary + secondary goals with parameters |
| Memory storage | ✅ Complete | Key-value with optional expiration |
| Working memory | ✅ Complete | Frame-level temporary data |
| Pending change tracking | ✅ Complete | For publishing state updates |
| Expired memory cleanup | ✅ Complete | Called each tick |

### 1.3 CognitionConstants (`plugins/lib-behavior/Cognition/CognitionTypes.cs`)

| Feature | Status | Notes |
|---------|--------|-------|
| Urgency thresholds | ✅ Complete | Low/Medium/High bands configurable |
| Planning parameters | ✅ Complete | MaxDepth, Timeout, MaxNodes per urgency |
| Attention weights | ✅ Complete | Threat/Novelty/Social/Routine weights |
| Significance weights | ✅ Complete | Emotional/GoalRelevance/Relationship |
| Memory relevance scoring | ✅ Complete | Category/Content/Metadata/Recency/Significance |
| Threat fast-track | ✅ Complete | Bypass cognition for high-urgency threats |

### 1.4 lib-mapping (Complete Spatial Infrastructure)

| Feature | Status | Notes |
|---------|--------|-------|
| Regional channel tracking | ✅ Complete | Region + Kind channels |
| 3D spatial indexing | ✅ Complete | Cell-based grid (64 unit default) |
| High-throughput ingest | ✅ Complete | Fire-and-forget bulk updates |
| Point/Bounds queries | ✅ Complete | Find objects at/in area |
| Affordance queries | ✅ Complete | Semantic queries (ambush, shelter, etc.) |
| Authority management | ✅ Complete | Heartbeat, takeover modes |
| Event subscriptions | ✅ Complete | `map.{regionId}.{kind}.*` topics |

### 1.5 ABML Compilation (`plugins/lib-behavior/`)

| Feature | Status | Notes |
|---------|--------|-------|
| YAML parser | ✅ Complete | Full ABML v2 syntax |
| Semantic analyzer | ✅ Complete | Type inference, scope analysis |
| Bytecode emitter | ✅ Complete | Stack-based VM, 42 opcodes |
| Expression VM | ✅ Complete | Zero-allocation per-frame evaluation |
| 5-channel intent system | ✅ Complete | action, locomotion, attention, stance, vocalization |
| GOAP planner | ✅ Complete | A* with urgency-based constraints |

---

## 2. Partially Implemented / Clarifications Needed

### 2.1 Behavior Stack Compilation Service Endpoint

**Status**: Runtime class complete, service endpoint returns NotImplemented

**Clarification**: There are TWO distinct components:
1. **Runtime BehaviorStack class** (`lib-behavior/Stack/BehaviorStack.cs`, 387 lines) - **COMPLETE**
   - Layer management (add/remove/get with thread-safety)
   - Priority-based ordering (categories then priority)
   - Active layer filtering
   - `EvaluateAsync()` with per-channel intent merging

2. **Service endpoint** (`BehaviorService.CompileBehaviorStackAsync`) - **NotImplemented**
   - Line 481: `return (StatusCodes.NotImplemented, null);`
   - Also: `ResolveContextVariablesAsync` at line 677 returns NotImplemented

```csharp
// From http-tester/Tests/BehaviorTestHandler.cs:600
var response = await behaviorClient.CompileBehaviorStackAsync(request);
// Currently returns 501 NotImplemented
```

**Gap**: The service endpoint that compiles multiple ABML sources into a merged behavior stack is not implemented. The runtime stack evaluation IS complete.

**Required Work**:
- Implement `CompileBehaviorStackAsync` to accept multiple ABML sources with priorities
- Use existing `BehaviorCompiler` for individual layers
- Use existing `IntentStackMerger` patterns for merging strategy
- Wire to existing `BehaviorStack` runtime class

**Effort**: ~2-3 days (mostly integration, not new architecture)

### 2.2 Cognition Pipeline Handlers

**Status**: Implemented, integration verification needed

All 6 cognition handlers are fully implemented in `lib-behavior/Handlers/`:
- `FilterAttentionHandler.cs` (146 lines) - Stage 1: Attention filtering with budget constraints
- `QueryMemoryHandler.cs` (150+ lines) - Stage 2: Memory retrieval by relevance
- `AssessSignificanceHandler.cs` (150+ lines) - Stage 3: Weighted significance scoring
- `StoreMemoryHandler.cs` (140+ lines) - Stage 4: Memory storage with filtering
- `EvaluateGoalImpactHandler.cs` (150+ lines) - Stage 5: Goal impact assessment
- `TriggerGoapReplanHandler.cs` (150+ lines) - Stage 6: GOAP replanning trigger

**Architecture**: These are ABML action handlers using the standard `IActionHandler` interface. They're invoked when ABML documents contain actions like `- filter_attention: ...`.

**Gap**: What's unclear is whether:
1. DocumentExecutorFactory has all handlers registered
2. ActorRunner's behavior documents actually invoke the cognition pipeline
3. The full 6-stage flow has been tested end-to-end

**Required Work**:
- Verify handler registration in DocumentExecutorFactory
- Create example ABML behavior that exercises the cognition pipeline
- Add integration tests for perception → emotion → memory → goal flow

**Effort**: ~1-2 days (verification + example ABML, not new code)

### 2.3 Intent Emission Routing

**Status**: 5 channels exist and are architecturally correct

The 5 existing channels are:
- `action` - Physical actions (includes combat, dialogue initiation)
- `locomotion` - Movement intent (walk, run, jump)
- `attention` - Where to look/focus
- `stance` - Body posture
- `vocalization` - Sounds/speech

**Clarification on ACTOR_BEHAVIORS.md Example**:

The ACTOR_BEHAVIORS.md example uses `channel: cognition` for actions like `store_memory` and `modify_emotion`. This is **aspirational syntax that is architecturally incorrect**.

**Why "cognition channel" is wrong**:
- Intent channels output to the game engine (animation, movement, sound)
- Cognition produces *internal state changes* (feelings, memories, goals)
- Cognition influences *which* intents are emitted, but cognition itself doesn't emit on a channel
- `store_memory` and `modify_emotion` should modify ActorState directly, not emit channel intents

**Correct architecture**:
- Cognition handlers modify ActorState via scope bindings (already implemented)
- Combat actions use `channel: action` with action discriminators (e.g., `action_type: attack`)
- Dialogue actions use `channel: action` with action discriminators (e.g., `action_type: speak`)

**NOT a gap**: The 5-channel system is sufficient. Combat and dialogue belong in the `action` channel.

**Gap**: ABML examples showing combat/dialogue via the action channel are missing (documentation gap, not code gap).

---

## 3. Not Implemented (Gaps)

### 3.1 Character Personality/Backstory Schema

**Gap**: `schemas/character-api.yaml` only has basic identity fields.

**Current CharacterResponse**:
```yaml
characterId, name, realmId, speciesId, birthDate, deathDate, status, createdAt, updatedAt
```

**Missing from ACTOR_BEHAVIORS.md design**:
```yaml
personality:
  friendliness: 0.8
  patience: 0.7
  aggression: 0.3
  caution: 0.6
  ...

backstory:
  template: "veteran_soldier"
  seed: "..."

appearance:
  height: 1.8
  build: "muscular"
  ...

baseGoals:
  - id: "maintain_order"
    priority: 70

combatPreferences:
  style: "defensive"
  aggression: 0.3
```

**Required Work**:
1. Update `schemas/character-api.yaml` with personality, backstory, combat preferences
2. Regenerate services: `scripts/generate-all-services.sh`
3. Update CharacterService to store/retrieve extended data
4. Update ActorRunner to load character data on startup

**Effort**: ~3-4 days (schema + service + actor integration)

### 3.2 Event Actor Architecture (THE_DREAM "Event Brain")

**Gap**: No EventActor class for multi-character orchestration.

**Alignment with THE_DREAM**: This is THE_DREAM's "Event Brain" concept from Phase 5. THE_DREAM_GAP_ANALYSIS identifies this as remaining work with:
- Event Brain Actor Schema
- Character Agent Query API (`/agent/query-combat-options`)
- Regional Watcher (spawns Event Actors based on interestingness)

**Current State**:
- ActorRunner is single-actor focused
- CutsceneCoordinator exists in lib-behavior and IS complete (sessions, sync points, input windows)
- ControlGateManager exists for cinematic handoff
- No regional event subscription pattern (Regional Watcher)

**ACTOR_BEHAVIORS.md and THE_DREAM describe event actors that**:
- Subscribe to mapping channel updates (Regional Watcher pattern)
- Query character actors for current state (Character Agent Query API)
- Compose multi-character choreography (uses existing CutsceneCoordinator)
- Handle player intervention opportunities (uses existing InputWindowManager)
- Apply aftermath effects (uses existing event publishing)

**Required Work**:
1. Create `EventActorRunner` class (or new EventActor ABML behavior type)
2. Implement Regional Watcher pattern for event spawning
3. Add `/agent/query-combat-options` endpoint to ActorService
4. Wire EventActor to existing CutsceneCoordinator, InputWindowManager
5. Create event choreography composition using existing SyncPointManager

**Effort**: ~3-5 days (most infrastructure exists, this is integration)

**Note**: THE_DREAM_GAP_ANALYSIS estimates this at "Small" effort for Event Brain actor schema, suggesting existing infrastructure is more reusable than originally thought.

### 3.3 Combat Preference Storage in ActorState

**Gap**: No storage for combat preferences that evolve during play.

**Current ActorState has**:
- `_feelings` - emotional dimensions
- `_goals` - primary/secondary goals
- `_memories` - key-value memories

**Missing**:
- `_combatPreferences` - style, aggression, tactics
- Methods: `GetCombatStyle()`, `SetCombatAggression()`, etc.

**Required Work**:
1. Add `CombatPreferences` class to ActorState
2. Add pending change tracking for combat preference updates
3. Add to CharacterStateUpdateEvent schema
4. Wire to behavior execution scope

**Effort**: ~1 day

### 3.4 Character Data Loading in Actor Startup

**Gap**: ActorRunner has CharacterId but doesn't load character data.

**Current flow**:
```
ActorRunner created with CharacterId
  → Sets up perception subscription
  → BUT: Doesn't load personality/backstory from lib-character
```

**Required flow**:
```
ActorRunner created with CharacterId
  → Load character data from lib-character
  → Initialize emotional state baseline from personality
  → Initialize goals from character's base goals
  → Initialize combat preferences from character config
  → Set up perception subscription
```

**Required Work**:
1. Add lib-character client to ActorRunner dependencies
2. Call character/get on startup to load data
3. Initialize ActorState with character baseline
4. Handle character data not found (graceful degradation)

**Effort**: ~1-2 days

---

## 4. Implementation Priority

### Phase 1: Foundation (Week 1)
| Task | Effort | Priority | Dependency |
|------|--------|----------|------------|
| Add personality/backstory to character schema | 2 days | High | None |
| Add combat preferences to ActorState | 1 day | High | None |
| Load character data in ActorRunner startup | 1 day | High | Character schema |

### Phase 2: Cognition Wiring (Week 2)
| Task | Effort | Priority | Dependency |
|------|--------|----------|------------|
| Verify cognition handler integration | 1 day | High | None |
| Add cognition intent channel | 1 day | High | None |
| Implement behavior stack compilation | 2 days | Medium | None |
| Test end-to-end cognition flow | 1 day | High | Above tasks |

### Phase 3: Event Actors (Week 3-4)
| Task | Effort | Priority | Dependency |
|------|--------|----------|------------|
| Create EventActorRunner class | 2 days | Medium | Phase 1-2 |
| Add mapping subscription capability | 2 days | Medium | EventActorRunner |
| Expose CutsceneCoordinator to actors | 1 day | Medium | EventActorRunner |
| Implement event choreography composition | 2 days | Medium | All above |

---

## 5. Architectural Constraints (From Existing Code)

### 5.1 Perception is Queue-Based, Not Event-Based
ActorRunner uses `Channel<PerceptionData>` with DropOldest behavior. Perceptions are drained each tick, not processed on arrival. This is **by design** for deterministic behavior.

### 5.2 State Changes are Batched
ActorState tracks pending changes. They're published after behavior execution, not during. This prevents partial state visibility.

### 5.3 Cognition Stages are ABML Handlers
The cognition pipeline stages (FilterAttention, AssessSignificance, etc.) are ABML action handlers, not runtime code. They're composable via behavior documents.

### 5.4 Pool Mode Uses Message-Based Commands
In distributed mode, spawn/stop commands route via message bus. This is async, not RPC. EventActors must account for this latency.

### 5.5 CharacterId is Optional
Actors don't require character linking. Event actors, system actors, etc. may not have a CharacterId. Code must handle null CharacterId gracefully.

---

## 6. Testing Considerations

### Existing Test Infrastructure
- `InjectPerception` endpoint allows direct perception injection
- ActorStateSnapshot accessible via GetActor
- http-tester has BehaviorTestHandler with stack compilation tests (currently failing NotImplemented)

### New Tests Needed
1. **Character data loading**: Actor loads personality on startup
2. **Cognition flow**: Perception → emotion → memory → goal test
3. **Combat preference update**: Threat assessment affects combat style
4. **Behavior stack merging**: Multiple layers resolve correctly
5. **Event actor choreography**: Multi-character event executes

---

## 7. Summary Table

| Component | Design Status | Implementation Status | Gap Size |
|-----------|---------------|----------------------|----------|
| ActorRunner core | Complete | ✅ Complete | None |
| ActorState (feelings/goals/memory) | Complete | ✅ Complete | None |
| Perception subscription | Complete | ✅ Complete | None |
| State update publishing | Complete | ✅ Complete | None |
| ABML compilation | Complete | ✅ Complete (226+ compiler tests) | None |
| Bytecode interpreter | Complete | ✅ Complete (61 tests) | None |
| Cognition types/constants | Complete | ✅ Complete (CognitionTypes.cs 633 lines) | None |
| Cognition handlers (6 stages) | Complete | ✅ Complete (verification needed) | Small |
| Runtime BehaviorStack | Complete | ✅ Complete (387 lines) | None |
| BehaviorStack service endpoint | Complete | ❌ NotImplemented | Medium |
| Cutscene coordination | Complete | ✅ Complete | None |
| Control gates | Complete | ✅ Complete | None |
| Intent channels (5) | Complete | ✅ Complete | None |
| Character personality schema | Complete | ❌ Not in schema | Medium |
| Combat preferences | Complete | ❌ Not in ActorState | Small |
| Character data loading | Complete | ❌ Not in ActorRunner | Small |
| Event actor (Event Brain) | Complete | ❌ Not implemented | Medium |
| Regional Watcher | Complete | ❌ Not implemented | Medium |
| Mapping infrastructure | Complete | ✅ Complete | None |

**Total estimated effort to close gaps**: ~2-3 weeks

**Alignment with THE_DREAM**: THE_DREAM_GAP_ANALYSIS estimates ~90% completion. The remaining gaps here are THE_DREAM Phase 5 (Event Brain) work.

---

## 8. Recommended Next Steps

1. **Immediate**: Update character schema with personality/backstory/combatPreferences fields
2. **This week**:
   - Add combat preferences to ActorState
   - Wire character data loading in ActorRunner startup
   - Verify cognition handler registration in DocumentExecutorFactory
3. **Next week**:
   - Implement `CompileBehaviorStackAsync` service endpoint
   - Create example ABML behaviors that exercise cognition pipeline
4. **Following weeks**:
   - Event Actor / Event Brain architecture (THE_DREAM Phase 5)
   - Regional Watcher for event spawning
   - Character Agent Query API

**The foundation is solid**. THE_DREAM_GAP_ANALYSIS's ~90% completion estimate is accurate. Remaining gaps are integration and "filling in the boxes" work, not new architecture.

---

## 9. Corrections Log (Peer Review 2026-01-09)

| Original Claim | Correction | Reason |
|----------------|------------|--------|
| "ABML compilation 85% complete" | 100% complete | Bytecode compiler has 226+ tests, interpreter has 61 tests |
| "Cognition handlers Partial" | Complete (verification needed) | All 6 handlers fully implemented in lib-behavior/Handlers/ |
| "Need cognition channel" | NOT needed | Cognition modifies internal state, doesn't emit output intents |
| "5-7 days for Event Actor" | 3-5 days | Most infrastructure exists (CutsceneCoordinator, SyncPointManager, InputWindowManager) |
| "3-4 weeks total" | 2-3 weeks | Many items marked "not implemented" are actually complete |
| Missing THE_DREAM alignment | Added | Event Actor = Event Brain Phase 5 |

**Key Architectural Insight**: The ACTOR_BEHAVIORS.md example uses some aspirational syntax (`${agent.cognition.emotional_state}`, `layer_activate`, `emit_intent: channel: cognition`) that doesn't exist. These are design proposals, not implementation gaps. The 5-channel intent system is architecturally correct as-is.
