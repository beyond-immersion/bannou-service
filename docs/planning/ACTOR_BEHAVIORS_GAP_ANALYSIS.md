# Actor Behaviors Gap Analysis

> **Status**: Gap Analysis for ACTOR_BEHAVIORS.md Implementation
> **Date**: 2026-01-08
> **Peer Reviewed**: 2026-01-09
> **Revised**: 2026-01-09 (Updated for lib-character-personality/history completion)
> **Revised**: 2026-01-09 (Event Brain Communication Architecture analysis - see §6)
> **Revised**: 2026-01-09 (StateSync integration tests, T23 fixes, removed unused endpoints)
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
- **Character personality traits**: **100% complete** (lib-character-personality with 8 axes + experience evolution)
- **Combat preferences**: **100% complete** (lib-character-personality with 6 preference fields + combat experience evolution)
- **Character history/backstory**: **100% complete** (lib-character-history with 9 backstory element types + event participation)
- **ABML Personality Provider**: **100% complete** (`${personality.*}` paths in ActorRunner)
- **ABML Combat Preferences Provider**: **100% complete** (`${combat.*}` paths in ActorRunner)

- **ABML BackstoryProvider**: **100% complete** (`${backstory.*}` paths in ActorRunner)
- **Character Agent Query API**: **100% complete** (`/actor/query-options` generalized endpoint)
- **Event Brain ABML design**: **100% complete** (see DESIGN_-_EVENT_BRAIN_ABML.md)

**2026-01-09 Update**: Major progress since last review. The `lib-character-personality` and `lib-character-history` services are fully implemented with expanded capabilities beyond the original design. PersonalityProvider, CombatPreferencesProvider, and BackstoryProvider are all wired into ActorRunner, providing ABML access to character traits, combat preferences, and backstory.

**Remaining gaps for Event Brain / Fight Coordinators (Implementation only - design is complete)**:
1. **ABML Action Handlers** - `query_options`, `query_actor_state`, `emit_perception`, `schedule_event` action handlers (agents actively working on this)
2. **Choreography Integration** - Wire choreography perception handler to CutsceneCoordinator (agents actively working on this)
3. **Actor Encounter State** - Internal encounter tracking (`_currentEncounterId`, `_encounterRole`, `_encounterPriority`) + StartEncounter/EndEncounter endpoints
4. **Regional Watcher** - Auto-spawns Event Actors based on interestingness (larger separate task, not blocking)

**Implementation Fixes Required (2026-01-09 Review)**:
1. ⚠️ **TENET 5 Violation** - CharacterPersonalityService uses anonymous event objects; need typed events in schema
2. ⚠️ **Pattern Violation** - EmitPerceptionHandler publishes to `actor.{actorId}.perceptions`; should use existing `character.{characterId}.perceptions` channel or RPC
3. ⚠️ **Local Type** - PerceptionEvent defined locally in EmitPerceptionHandler.cs; should be in schema
4. ⚠️ **Redundant Subscription** - `_actorPerceptionSubscription` in ActorRunner should be removed

**Alignment with THE_DREAM**: THE_DREAM_GAP_ANALYSIS estimates ~97% overall completion. The remaining gaps are ABML action handler implementation (not new architecture) plus fixing identified violations. Event Brain Communication Architecture now fully designed (see §6).

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

### 1.6 Character Personality (`plugins/lib-character-personality/`)

| Feature | Status | Notes |
|---------|--------|-------|
| Personality traits (8 axes) | ✅ Complete | OPENNESS, CONSCIENTIOUSNESS, EXTRAVERSION, AGREEABLENESS, NEUROTICISM, HONESTY, AGGRESSION, LOYALTY |
| Combat preferences | ✅ Complete | style, preferredRange, groupRole, riskTolerance, retreatThreshold, protectAllies |
| Personality evolution | ✅ Complete | 9 experience types (TRAUMA, BETRAYAL, LOSS, VICTORY, FRIENDSHIP, etc.) |
| Combat preference evolution | ✅ Complete | 10 combat experience types (DECISIVE_VICTORY, NEAR_DEATH, ALLY_SAVED, etc.) |
| Batch personality retrieval | ✅ Complete | Efficient region loading |

### 1.7 Character History (`plugins/lib-character-history/`)

| Feature | Status | Notes |
|---------|--------|-------|
| Backstory elements | ✅ Complete | 9 types: ORIGIN, OCCUPATION, TRAINING, TRAUMA, ACHIEVEMENT, SECRET, GOAL, FEAR, BELIEF |
| Event participation | ✅ Complete | Historical event tracking with roles (LEADER, COMBATANT, VICTIM, WITNESS, etc.) |
| History summarization | ✅ Complete | Compression for character summaries |
| Dual-index storage | ✅ Complete | Efficient query by character or event |

### 1.8 ABML Variable Providers (`plugins/lib-actor/Runtime/`)

| Feature | Status | Notes |
|---------|--------|-------|
| PersonalityProvider | ✅ Complete | `${personality.openness}`, `${personality.traits.AGGRESSION}`, `${personality.version}` |
| CombatPreferencesProvider | ✅ Complete | `${combat.style}`, `${combat.riskTolerance}`, `${combat.protectAllies}`, etc. |
| PersonalityCache | ✅ Complete | TTL-based caching via ICharacterPersonalityClient |
| Provider registration | ✅ Complete | Providers registered in ActorRunner behavior execution scope |

---

## 2. Partially Implemented / Clarifications Needed

### ~~2.1 Behavior Stack Compilation Service Endpoint~~ RESOLVED

**Status**: ✅ REMOVED (2026-01-09)

The `CompileBehaviorStackAsync` and `ResolveContextVariablesAsync` endpoints were removed from the API entirely. They represented an inferior architecture that conflicted with the established ABML compilation pipeline.

**What existed**:
1. **Runtime BehaviorStack class** (`lib-behavior/Stack/BehaviorStack.cs`, 387 lines) - **COMPLETE and unchanged**
   - Layer management (add/remove/get with thread-safety)
   - Priority-based ordering (categories then priority)
   - Active layer filtering
   - `EvaluateAsync()` with per-channel intent merging

2. ~~**Service endpoints**~~ - **REMOVED**
   - `CompileBehaviorStackAsync` - removed from schema and service
   - `ResolveContextVariablesAsync` - removed from schema and service

**Resolution**: The existing ABML compilation pipeline (`/behavior/compile`) handles individual behaviors. Behavior stacking happens at runtime via the BehaviorStack class, not via a separate compilation endpoint. This is the correct architecture.

**Effort**: None (removal, not implementation)

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

**Status**: ✅ **COMPLETE** (2026-01-09)

The original gap (character personality and backstory storage) has been fully addressed through separate dedicated services:

| Original Gap | Resolution |
|--------------|------------|
| Personality traits | ✅ `lib-character-personality` - 8 trait axes with evolution |
| Combat preferences | ✅ `lib-character-personality` - 6 preference fields with combat experience evolution |
| Backstory | ✅ `lib-character-history` - 9 backstory element types |
| ABML personality access | ✅ `PersonalityProvider` - `${personality.*}` paths |
| ABML combat access | ✅ `CombatPreferencesProvider` - `${combat.*}` paths |
| Character data loading | ✅ `PersonalityCache` - TTL-cached loading via mesh client |

**Remaining sub-gap**: BackstoryProvider for ABML (see §3.5)

### 3.2 Event Actor Architecture (THE_DREAM "Event Brain")

**Gap**: No EventActor/EventBrain ABML behavior definition for multi-character orchestration.

**Alignment with THE_DREAM**: This is THE_DREAM's "Event Brain" concept from Phase 5. THE_DREAM_GAP_ANALYSIS identifies this as remaining work.

**Current State**:
- ActorRunner is complete and can run ANY actor type (no separate EventActorRunner needed)
- CutsceneCoordinator: ✅ Complete (sessions, sync points, input windows)
- ControlGateManager: ✅ Complete (cinematic handoff)
- PersonalityProvider: ✅ Complete (`${personality.*}` paths)
- CombatPreferencesProvider: ✅ Complete (`${combat.*}` paths)
- No Event Brain ABML behavior definition
- No Character Agent Query API

**What Event Brain needs**:
1. **ABML behavior definition** - The orchestration logic itself (what "Event Brain" does)
2. **Character Agent Query API** - Ask participants "what can you do?"
3. **BackstoryProvider** - Consider character history for choreography decisions

**Note**: Event Brain is an ABML behavior that uses existing infrastructure. The existing ActorRunner can execute it - no separate runtime class needed. The gap is the ABML definition and query API, not infrastructure.

**Required Work**:
1. Design Event Brain ABML behavior schema (orchestration logic)
2. Add Character Agent Query API endpoint (see §3.6)
3. Add BackstoryProvider to ActorRunner (see §3.5)
4. Create example event behaviors (fight orchestration, cutscene direction)

**Effort**: ~3-5 days (ABML authoring + endpoint + provider)

**Separate Task**: Regional Watcher for auto-spawning Event Actors based on interestingness. This is a larger architectural decision about when/how Event Brains spawn and is tracked separately.

### 3.3 Combat Preference Storage in ActorState

**Status**: ✅ **COMPLETE** (2026-01-09)

Combat preferences are now handled through the dedicated `lib-character-personality` service:

| Original Gap | Resolution |
|--------------|------------|
| Combat preferences storage | ✅ `lib-character-personality` stores style, preferredRange, groupRole, riskTolerance, retreatThreshold, protectAllies |
| Preference evolution | ✅ Combat experience evolution (DECISIVE_VICTORY, NEAR_DEATH, etc.) |
| ABML access | ✅ `CombatPreferencesProvider` provides `${combat.*}` paths |
| Caching | ✅ `PersonalityCache.GetCombatPreferencesOrLoadAsync()` with TTL |

**Design Decision**: Combat preferences are persistent character data (survives actor restart), not transient actor state. This is more appropriate than storing in ActorState which is session-scoped.

### 3.4 Character Data Loading in Actor Startup

**Status**: ✅ **COMPLETE** (2026-01-09)

Character data loading is now implemented via the PersonalityCache pattern:

```csharp
// From ActorRunner.cs lines 541-555
if (CharacterId.HasValue)
{
    var personality = await _personalityCache.GetOrLoadAsync(CharacterId.Value, ct);
    scope.RegisterProvider(new PersonalityProvider(personality));

    var combatPrefs = await _personalityCache.GetCombatPreferencesOrLoadAsync(CharacterId.Value, ct);
    scope.RegisterProvider(new CombatPreferencesProvider(combatPrefs));
}
```

| Original Gap | Resolution |
|--------------|------------|
| Load character data on startup | ✅ PersonalityCache loads via mesh client |
| Initialize personality | ✅ PersonalityProvider registered in scope |
| Initialize combat preferences | ✅ CombatPreferencesProvider registered in scope |
| Handle missing data | ✅ Graceful degradation (providers use defaults) |
| Caching | ✅ 5-minute TTL with stale-if-error fallback |

### 3.5 ~~BackstoryProvider for ABML~~ ✅ COMPLETE

**Status**: ✅ **COMPLETE** (2026-01-09)

**Implementation**:
- `BackstoryProvider : IVariableProvider` created in `lib-actor/Runtime/BackstoryProvider.cs`
- `GetBackstoryOrLoadAsync` added to `IPersonalityCache` and `PersonalityCache`
- BackstoryProvider registered in ActorRunner execution scope for character-based actors
- Uses 5-minute TTL caching with stale-if-error fallback (same pattern as PersonalityProvider)

**Supported ABML Paths**:
- `${backstory.origin}` - First ORIGIN element's value
- `${backstory.origin.value}` - Element value
- `${backstory.origin.key}` - Element key
- `${backstory.origin.strength}` - Element strength (0.0-1.0)
- `${backstory.fear}` - First FEAR element's value
- `${backstory.elements}` - All backstory elements as collection
- `${backstory.elements.TRAUMA}` - All elements of type TRAUMA

**Example ABML Usage**:
```yaml
# Event Brain considering backstory for choreography
- cond:
    if: "${backstory.fear.key == 'FIRE'}"
    then:
      - exclude_option: "chandelier_fire_kill"
    else:
      - include_option: "chandelier_fire_kill"
```

### 3.6 ~~Character Agent Query API~~ ✅ COMPLETE

**Status**: ✅ **COMPLETE** (2026-01-09)

**Implementation**:
- Generalized `/actor/query-options` endpoint implemented
- Schema added to `schemas/actor-api.yaml`
- `QueryOptionsAsync` implemented in `ActorService.cs`
- Full design document: `docs/planning/DESIGN_-_QUERY_OPTIONS_API.md`

**Key Design Decisions**:
1. **Generalized Approach**: Single endpoint with `queryType` parameter (combat, dialogue, social, exploration, custom)
2. **Requester-Determines-Freshness**: Three levels (fresh, cached, stale_ok) following lib-mapping pattern
3. **Actor Self-Describes**: Options maintained by actors in `state.memories.{type}_options`, not computed by endpoint
4. **First-Class ABML Support**: `options` block proposed for ABML behaviors (future implementation)

**Endpoint**: `POST /actor/query-options`

**Request Schema**:
```yaml
QueryOptionsRequest:
  actorId: string           # Actor to query
  queryType: enum           # combat, dialogue, social, exploration, custom
  freshness: enum           # fresh, cached, stale_ok
  maxAgeMs: integer         # Max cache age for 'cached' freshness
  context:                  # Optional context for fresh queries
    combatState: string
    opponentIds: array
    allyIds: array
    environmentTags: array
    urgency: float
```

**Response Schema**:
```yaml
QueryOptionsResponse:
  actorId: string
  queryType: enum
  options: array            # ActorOption[]
  computedAt: datetime
  ageMs: integer
  characterContext:         # For character-based actors
    combatStyle: string
    riskTolerance: float
    protectAllies: boolean
```

---

## 4. Implementation Priority

### ~~Phase 1: Foundation (Week 1)~~ ✅ COMPLETE

| Task | Status | Resolution |
|------|--------|------------|
| ~~Add personality/backstory to character schema~~ | ✅ Complete | lib-character-personality, lib-character-history |
| ~~Add combat preferences to ActorState~~ | ✅ Complete | CombatPreferencesProvider |
| ~~Load character data in ActorRunner startup~~ | ✅ Complete | PersonalityCache pattern |

### ~~Phase 2: Cognition Wiring (Week 2)~~ ✅ COMPLETE

| Task | Status | Resolution |
|------|--------|------------|
| ~~Verify cognition handler integration~~ | ✅ Complete | All 6 handlers registered |
| ~~Add cognition intent channel~~ | ✅ N/A | Not needed - cognition modifies state, not intents |
| ~~Test end-to-end cognition flow~~ | ✅ Complete | Cognition handlers tested |

**Note**: `CompileBehaviorStackAsync` and `ResolveContextVariablesAsync` endpoints were **removed** (2026-01-09) as they represented inferior design.

### ~~Phase 3: Event Brain Infrastructure~~ ✅ DESIGN COMPLETE

| Task | Effort | Status | Notes |
|------|--------|--------|-------|
| ~~Create BackstoryProvider for ABML~~ | 1-2 days | ✅ Complete | `lib-actor/Runtime/BackstoryProvider.cs` |
| ~~Design Character Agent Query API~~ | 2-3 days | ✅ Complete | `/actor/query-options` endpoint |
| ~~Create Event Brain ABML behavior schema~~ | 2-3 days | ✅ Complete | `DESIGN_-_EVENT_BRAIN_ABML.md` |
| Implement Event Brain ABML actions | 2-3 days | **Pending** | `query_options`, `emit_perception`, etc. |
| Create example event behaviors | 1-2 days | **Pending** | `fight-coordinator-regional` |

**Total Remaining Phase 3 Effort**: ~3-5 days (implementation only)

### Phase 4: Regional Watcher (Separate Task)

Regional Watcher is a larger architectural decision about when/how Event Brains spawn automatically based on "interestingness" thresholds. This is tracked separately and not blocking Phase 3 work.

| Task | Effort | Priority | Notes |
|------|--------|----------|-------|
| Define "interestingness" metrics | TBD | Medium | Design decision |
| Regional perception aggregation | TBD | Medium | Architecture decision |
| Auto-spawn threshold tuning | TBD | Medium | Requires playtesting |
| Integration with Orchestrator | TBD | Medium | Deployment consideration |

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

## 6. Event Brain Communication Architecture (2026-01-09)

This section documents the architectural analysis and design decisions for how Event Brains (choreography orchestrators) communicate with Character Actors.

### 6.1 Problem Statement

When an Event Brain wants to orchestrate a choreographed sequence (cinematic, QTE, quest event, combat coordination), it needs to:
1. **Reserve** participants (mutual exclusion - can't be in two cinematics at once)
2. **Get consent** (actor agency - actors can refuse based on personality/state)
3. **Coordinate** the sequence (broadcast instructions)
4. **Release** participants (cleanup when encounter ends)

### 6.2 Rejected Approach: Separate Encounter Service + New Topics

**Initial proposal** (rejected as over-engineering):

```
Event Brain → Encounter Service → State Store (locks) → Actors (via new topics)
```

This approach proposed:
- New `lib-encounter` plugin for lifecycle management
- Distributed locking via state store for mutual exclusion
- New `encounter.{encounterId}.instructions` topic for broadcast
- Consent aggregation service

**Why this was rejected**:

| Proposed Feature | Why Unnecessary |
|------------------|-----------------|
| Distributed locking | Actor's internal state check IS the lock (atomic per-request) |
| Encounter Service plugin | Event Brain + actor internal state is sufficient |
| New encounter topic | Event Brain already knows character perception channels |
| Consent aggregation | Event Brain can track responses directly |

### 6.3 Correct Approach: Actor-Centric Encounters

**Key insight**: "Encounters are what make actors 'actors' instead of simply 'agents'."

The theatrical metaphor is deliberate. Actors don't just exist - they **perform** in directed sequences. Encounter participation belongs IN the actor, not bolted on via external service.

**Simplified architecture**:
```
Event Brain → RPC → Actor (which tracks its own encounter state internally)
```

### 6.4 Actor Encounter State

Actors maintain encounter state internally:

```csharp
// Internal actor state (not exposed via external service)
private Guid? _currentEncounterId;        // null if not in encounter
private string? _encounterRole;           // "guard_1", "merchant", etc.
private int _encounterPriority;           // For interruption decisions
private Dictionary<string, object>? _encounterState;  // Role-specific data
```

### 6.5 Communication Patterns

Event Brain uses **existing** communication patterns:

| Operation | Pattern | Rationale |
|-----------|---------|-----------|
| Start Encounter | RPC | Synchronous accept/reject response needed |
| Query Options | RPC | Already exists (`/actor/query-options`), synchronous |
| Instructions | RPC or Perception | Depends on sync needs (see §6.6) |
| End Encounter | RPC | Synchronous confirmation |

**No new topics or channels needed.** Event Brain already knows:
- Character perception channels (`character.{characterId}.perceptions`)
- Actor RPC endpoints (`/actor/query-options`, etc.)

### 6.6 Instructions: RPC vs Perception Channel

Two valid approaches for sending encounter instructions:

**Option A: RPC to Actor**
```csharp
// Event Brain sends instruction directly to actor
await _actorClient.SendInstructionAsync(actorId, new InstructionRequest
{
    EncounterId = encounterId,
    InstructionType = "move_to_position",
    Data = new { Position = new { X = 10, Y = 20 }, Speed = "walk" }
});
```

- Synchronous confirmation
- Event Brain knows exactly when actor received instruction
- Individual timeout/failure handling per-actor
- Good for "do this now and confirm" scenarios

**Option B: Perception Channel (existing)**
```csharp
// Event Brain publishes to character's perception topic
await _messageBus.PublishAsync($"character.{characterId}.perceptions", new CharacterPerceptionEvent
{
    PerceptionType = "encounter_instruction",
    SourceType = "encounter",
    SourceId = encounterId,
    Data = instruction
});
```

- Uses existing infrastructure completely
- Asynchronous (actor processes in next tick)
- Natural for "this is happening, react appropriately"
- Consistent with other perceptions

**Recommendation**: Hybrid approach:
- RPC for synchronous operations (StartEncounter, QueryOptions, EndEncounter)
- Perception channel for instructions (async is acceptable, actor reacts in next tick)

### 6.7 Mutual Exclusion Without Distributed Locks

The actor's `StartEncounter` check provides atomic mutual exclusion:

```csharp
public async Task<EncounterStartResponse> StartEncounterAsync(StartEncounterRequest request)
{
    // Mutual exclusion check (atomic per-request)
    if (_currentEncounterId.HasValue)
    {
        if (request.Priority > _encounterPriority)
        {
            // Higher priority can interrupt
            await ExitCurrentEncounterAsync("interrupted_by_higher_priority");
        }
        else
        {
            return new EncounterStartResponse
            {
                Accepted = false,
                Reason = $"already_in_encounter:{_currentEncounterId}"
            };
        }
    }

    // Evaluate against personality/configuration
    var evaluation = EvaluateEncounterProposal(request);
    if (!evaluation.Accepted)
        return evaluation;

    // Accept and set internal state
    _currentEncounterId = request.EncounterId;
    _encounterRole = request.Role;
    _encounterPriority = request.Priority;

    return new EncounterStartResponse { Accepted = true };
}
```

**Race condition handling**: If two Event Brains simultaneously try to claim the same actor, the first request processed wins, the second gets a rejection response. The Event Brain then adapts (substitute NPC, retry later, degrade encounter, cancel).

### 6.8 Actor Consent via Configuration

Actors can refuse encounters based on personality and configuration:

```yaml
# Actor configuration (part of personality or actor template)
encounter_preferences:
  encounters_enabled: true           # Master toggle
  allowed_types: [cinematic, quest_event, social]
  blocked_types: [tutorial]          # "I've done this already"
  min_interrupt_priority: 50         # Only interrupt if priority > current activity
  min_encounter_interval_seconds: 300  # Rate limiting
```

Example consent evaluation:
```csharp
private EncounterStartResponse EvaluateEncounterProposal(StartEncounterRequest request)
{
    // Check master toggle
    if (!_config.EncountersEnabled)
        return Reject("encounters_disabled");

    // Check type filter
    if (_config.BlockedTypes.Contains(request.EncounterType))
        return Reject($"type_blocked:{request.EncounterType}");

    // Check priority vs current activity
    if (request.Priority < _state.CurrentActivityPriority)
        return Reject($"busy:priority_{_state.CurrentActivityPriority}");

    // Rate limiting
    if (_state.LastEncounterTime.HasValue)
    {
        var elapsed = DateTimeOffset.UtcNow - _state.LastEncounterTime.Value;
        if (elapsed.TotalSeconds < _config.MinEncounterInterval)
            return Reject($"cooldown:{_config.MinEncounterInterval - elapsed.TotalSeconds}s");
    }

    return Accept();
}
```

### 6.9 ABML Handler for Encounter Instructions: Analysis

**Question**: Should there be an `encounter_instruction` ABML handler that maps instructions to actions?

**Without handler (hardcoded)**:
```csharp
switch (instruction.Type)
{
    case "move_to_position":
        await NavigateAsync(instruction.Position, instruction.Speed);
        break;
    case "deliver_line":
        await SpeakAsync(instruction.LineId, instruction.Emotion);
        break;
    // ... more cases
}
```

**With ABML handler (data-driven)**:
```yaml
handlers:
  encounter_instruction:
    type: instruction_mapper
    mappings:
      move_to_position:
        action: navigate
        params:
          destination: "{{instruction.position}}"
          speed: "{{instruction.speed | default: 'walk'}}"
      deliver_line:
        action: speak
        params:
          dialogue_id: "{{instruction.line_id}}"
          emotion: "{{instruction.emotion}}"
      play_animation:
        action: animate
        params:
          animation: "{{instruction.animation_id}}"
```

**Analysis**:

| Aspect | Without Handler | With ABML Handler |
|--------|-----------------|-------------------|
| Flexibility | Hardcoded in service | Data-driven, per-actor-type |
| Extension | Requires code changes | Add mappings in YAML |
| Consistency | Different from query_options | Matches query_options pattern |
| Complexity | Simpler initially | More abstraction upfront |
| Actor variation | Same for all actors | Guard interprets differently than merchant |

**Verdict**: The ABML handler approach is **NOT overkill** - it's the natural extension of the `query_options` pattern. Different actor types SHOULD interpret the same instruction differently:
- Guard's `deliver_line` includes authoritative stance
- Merchant's `deliver_line` includes nervous gestures
- Warrior's `take_damage` is stoic
- Scholar's `take_damage` is dramatic

**Recommendation**: Staged implementation:
1. **Phase 1**: Direct handling in actor code (understand instruction patterns first)
2. **Phase 2**: Extract to ABML `encounter_instruction` handler once patterns crystallize

### 6.10 Encounter Types and Consent Requirements

Different encounter types have different semantics:

| Type | Checkout Required | Consent Required | Can Interrupt |
|------|-------------------|------------------|---------------|
| `world_broadcast` | No | No | No (informational only) |
| `qte` | No | No | Instant reaction, no negotiation |
| `combat_cue` | No | No | Combat context only |
| `social_interaction` | Yes | Yes | Low priority activities |
| `cinematic` | Yes | Yes | Priority-based |
| `quest_event` | Yes | Yes | Priority-based |

This means the `StartEncounter` flow applies to cinematics/quests, but QTEs and combat cues can use perception channel directly.

### 6.11 Implementation Summary

**What's needed (minimal viable)**:

1. **Actor internal state**: `_currentEncounterId`, `_encounterRole`, `_encounterPriority`
2. **Actor endpoints**:
   - `StartEncounterAsync(request)` → Accept/Reject with reason
   - `EndEncounterAsync(request)` → Confirmation
   - Optionally: `SendInstructionAsync(request)` for RPC-style instructions
3. **Encounter evaluation logic**: Based on personality config
4. **Event Brain handling**: Track responses, adapt to rejections

**What's NOT needed**:
- New `lib-encounter` plugin
- New `encounter.{id}.instructions` topic
- Distributed locking via state store
- Consent aggregation service

**Alignment with THE_DREAM**: This actor-centric approach directly supports THE_DREAM's vision of autonomous agents that participate in encounters as a core capability, not puppets being controlled by external systems.

---

## 7. Testing Considerations

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

## 8. Summary Table

| Component | Design Status | Implementation Status | Gap Size |
|-----------|---------------|----------------------|----------|
| ActorRunner core | Complete | ✅ Complete | None |
| ActorState (feelings/goals/memory) | Complete | ✅ Complete | None |
| Perception subscription | Complete | ✅ Complete | None |
| State update publishing | Complete | ✅ Complete | None |
| ABML compilation | Complete | ✅ Complete (226+ compiler tests) | None |
| Bytecode interpreter | Complete | ✅ Complete (61 tests) | None |
| Cognition types/constants | Complete | ✅ Complete (CognitionTypes.cs 633 lines) | None |
| Cognition handlers (6 stages) | Complete | ✅ Complete | None |
| Runtime BehaviorStack | Complete | ✅ Complete (387 lines) | None |
| ~~BehaviorStack service endpoint~~ | ~~Complete~~ | ✅ REMOVED | None (inferior design) |
| Cutscene coordination | Complete | ✅ Complete | None |
| Control gates | Complete | ✅ Complete | None |
| **StateSync→EntityStateRegistry** | Complete | ✅ Complete (14 integration tests) | None |
| Intent channels (5) | Complete | ✅ Complete | None |
| **Character personality traits** | Complete | ✅ Complete (lib-character-personality) | None |
| **Combat preferences** | Complete | ✅ Complete (lib-character-personality) | None |
| **Character backstory storage** | Complete | ✅ Complete (lib-character-history) | None |
| **PersonalityProvider (ABML)** | Complete | ✅ Complete (`${personality.*}`) | None |
| **CombatPreferencesProvider (ABML)** | Complete | ✅ Complete (`${combat.*}`) | None |
| **BackstoryProvider (ABML)** | Complete | ✅ Complete (`${backstory.*}`) | None |
| **Character data loading** | Complete | ✅ Complete (PersonalityCache) | None |
| **Character Agent Query API** | Complete | ✅ Complete (`/actor/query-options`) | None |
| Event Brain ABML behavior | Complete | ✅ Design complete (see DESIGN_-_EVENT_BRAIN_ABML.md) | **Implementation** |
| **Event Brain Communication** | ✅ Design complete (§6) | Needs refactoring | **Small** |
| **Actor Encounter State** | ✅ Design complete (§6) | ❌ Not implemented | Small |
| Regional Watcher | Design needed | ❌ Not implemented | Large (separate) |
| Mapping infrastructure | Complete | ✅ Complete | None |

**Total estimated effort to close Phase 3 gaps**: ~4-6 days (Event Brain implementation + fixes)
**Regional Watcher (Phase 4)**: TBD (separate larger task)

**Implementation Fixes Identified (2026-01-09)**:
- ⚠️ **TENET 5 Violation**: CharacterPersonalityService uses anonymous event objects (need typed events in schema)
- ⚠️ **Pattern Violation**: EmitPerceptionHandler uses `actor.{actorId}.perceptions` (should use existing channels)
- ⚠️ **Local Type**: PerceptionEvent defined locally, not in schema

**Alignment with THE_DREAM**: Phase 3 design is complete. Remaining work is Event Brain ABML action handler implementation plus fixing identified violations. Estimate ~97% completion.

---

## 9. Recommended Next Steps

### ~~Previous Recommendations~~ ✅ COMPLETE
- ~~Update character schema with personality/backstory/combatPreferences fields~~ → lib-character-personality, lib-character-history
- ~~Add combat preferences to ActorState~~ → CombatPreferencesProvider
- ~~Wire character data loading in ActorRunner startup~~ → PersonalityCache pattern
- ~~Verify cognition handler registration~~ → All 6 handlers registered

### ~~Phase 3: Event Brain Infrastructure~~ ✅ DESIGN COMPLETE

**~~Parallel Track A - BackstoryProvider~~** ✅ COMPLETE (2026-01-09):
- ✅ `BackstoryProvider : IVariableProvider` created
- ✅ History loading added to PersonalityCache
- ✅ Registered in ActorRunner execution scope
- ✅ Supports `${backstory.*}` paths in ABML

**~~Parallel Track B - Character Agent Query API~~** ✅ COMPLETE (2026-01-09):
- ✅ Generalized `/actor/query-options` endpoint designed
- ✅ Schema added to `schemas/actor-api.yaml`
- ✅ `QueryOptionsAsync` implemented in ActorService
- ✅ Supports freshness levels (fresh, cached, stale_ok)
- ✅ Design document: `DESIGN_-_QUERY_OPTIONS_API.md`

**~~Event Brain ABML Design~~** ✅ COMPLETE (2026-01-09):
- ✅ Event Brain behavior schema designed
- ✅ Core actions defined (query_options, emit_perception, etc.)
- ✅ Choreography output format specified
- ✅ Integration with CutsceneCoordinator documented
- ✅ Design document: `DESIGN_-_EVENT_BRAIN_ABML.md`

### Phase 3 Remaining: Implementation (~3-5 days)

1. **ABML Action Handlers** (~2-3 days):
   - Implement `query_options` action handler
   - Implement `query_actor_state` action handler
   - Implement `emit_perception` action handler
   - Implement `schedule_event` action handler
   - Add Event Brain metadata type to ABML parser

2. **Choreography Integration** (~1-2 days):
   - Add choreography perception handler to character agents
   - Wire to CutsceneCoordinator for sync points
   - Create example `fight-coordinator-regional` behavior

### Phase 4: Regional Watcher (Separate Task)
Regional Watcher auto-spawning is a larger architectural decision tracked separately. It depends on:
- Defining "interestingness" metrics
- Regional perception aggregation patterns
- Integration with Orchestrator deployment

**The foundation is solid**. With Phase 3 complete, THE_DREAM reaches ~95% completion. Event Brain becomes functional using existing infrastructure. Regional Watcher is enhancement, not blocker.

---

## 10. Corrections Log

### Peer Review 2026-01-09

| Original Claim | Correction | Reason |
|----------------|------------|--------|
| "ABML compilation 85% complete" | 100% complete | Bytecode compiler has 226+ tests, interpreter has 61 tests |
| "Cognition handlers Partial" | Complete (verification needed) | All 6 handlers fully implemented in lib-behavior/Handlers/ |
| "Need cognition channel" | NOT needed | Cognition modifies internal state, doesn't emit output intents |
| "5-7 days for Event Actor" | 3-5 days | Most infrastructure exists (CutsceneCoordinator, SyncPointManager, InputWindowManager) |
| "3-4 weeks total" | 2-3 weeks | Many items marked "not implemented" are actually complete |
| Missing THE_DREAM alignment | Added | Event Actor = Event Brain Phase 5 |

**Key Architectural Insight**: The ACTOR_BEHAVIORS.md example uses some aspirational syntax (`${agent.cognition.emotional_state}`, `layer_activate`, `emit_intent: channel: cognition`) that doesn't exist. These are design proposals, not implementation gaps. The 5-channel intent system is architecturally correct as-is.

### Revision 2026-01-09 (lib-character-personality/history Review)

| Original Claim | Correction | Reason |
|----------------|------------|--------|
| "Character personality schema - Not in schema" | ✅ Complete | lib-character-personality provides 8 trait axes + experience evolution |
| "Combat preferences - Not in ActorState" | ✅ Complete | CombatPreferencesProvider via lib-character-personality |
| "Character data loading - Not in ActorRunner" | ✅ Complete | PersonalityCache pattern with TTL caching |
| "Need separate EventActorRunner" | NOT needed | ActorRunner can execute any actor type including Event Brain |
| "2-3 weeks total effort" | ~6-10 days | Foundation work complete, only Event Brain gaps remain |

**New Gaps Identified**:
1. **BackstoryProvider** - lib-character-history data not accessible from ABML (`${backstory.*}` paths missing)
2. **Character Agent Query API** - Event Brain has no way to ask participants "what can you do?"
3. **Event Brain ABML behavior** - Orchestration logic not yet defined

**Key Finding**: The implementation evolved beyond the original design in a positive way:
- Personality and combat preferences are now in dedicated services (better separation of concerns)
- Experience-based evolution enables personality growth over time
- Dual-index history storage enables efficient queries from both character and event perspectives
- The PersonalityCache pattern provides efficient caching with graceful degradation

**Alignment Update**: THE_DREAM completion estimate raised from ~90% to ~95% after Phase 3 completion.

### Revision 2026-01-09 (Event Brain Communication Architecture)

**Context**: Agent implemented `emit_perception` handler publishing to `actor.{actorId}.perceptions` and added second subscription to ActorRunner. This pattern was flagged for architectural review.

| Original Proposal | Correction | Reason |
|-------------------|------------|--------|
| New `lib-encounter` plugin | NOT needed | Event Brain + actor internal state is sufficient |
| Distributed locking via state store | NOT needed | Actor's internal state check IS the lock (atomic per-request) |
| New `encounter.{id}.instructions` topic | NOT needed | Use existing character perception channels or RPC |
| `actor.{actorId}.perceptions` topic | INCORRECT | Violates "tap" pattern; should use existing channels |
| Consent aggregation service | NOT needed | Event Brain can track responses directly |

**Key Architectural Insight**: "Encounters are what make actors 'actors' instead of simply 'agents'." Encounter participation belongs IN the actor, not bolted on via external service.

**Correct Architecture**:
1. Actor owns encounter state internally (`_currentEncounterId`, `_encounterRole`, `_encounterPriority`)
2. Event Brain uses existing RPC for synchronous operations (StartEncounter, EndEncounter, QueryOptions)
3. Instructions via existing perception channel (`character.{characterId}.perceptions`) or RPC
4. No new topics or services needed

**ABML Handler Analysis**: An `encounter_instruction` ABML handler is NOT overkill - it matches the `query_options` pattern and allows different actor types to interpret instructions differently. Recommended staged implementation: direct handling first, extract to ABML handler when patterns crystallize.

**Implementation Fixes Required**:
1. Remove `actor.{actorId}.perceptions` topic pattern from EmitPerceptionHandler
2. Remove `_actorPerceptionSubscription` from ActorRunner
3. Add encounter state fields to ActorRunner
4. Add StartEncounter/EndEncounter endpoints (or handle via perception events)
5. Fix TENET 5 violation in CharacterPersonalityService (anonymous event objects)
6. Move PerceptionEvent to schema if keeping any direct perception pattern

See §6 (Event Brain Communication Architecture) for full analysis.

### Revision 2026-01-09 (StateSync, T23, Endpoint Cleanup)

| Original Status | Correction | Reason |
|-----------------|------------|--------|
| StateSync "placeholder" | ✅ Fully implemented | StateSync now writes to EntityStateRegistry with proper events |
| EntityStateRegistry N/A | ✅ Complete | Thread-safe registry with StateUpdated events, source tracking |
| StateSync integration tests | ✅ 14 tests added | `lib-behavior.tests/Integration/StateSyncIntegrationTests.cs` |
| T23 violations (10+ methods) | ✅ All fixed | Converted to `await Task.Yield()` pattern per IMPLEMENTATION TENETS |
| `CompileBehaviorStackAsync` NotImplemented | ✅ REMOVED | Inferior design conflicting with ABML compilation pipeline |
| `ResolveContextVariablesAsync` NotImplemented | ✅ REMOVED | Inferior design conflicting with ABML compilation pipeline |

**StateSync Implementation Details**:
- CinematicController.ReturnEntityControlAsync calls StateSync.SyncStateAsync
- StateSync writes entity state to EntityStateRegistry with source="cinematic"
- EntityStateRegistry raises StateUpdated events for downstream consumers
- 14 integration tests cover StateSync→Registry, CinematicController→StateSync, and end-to-end flows

**T23 Fixes Applied**:
- `lib-actor/ActorServiceEvents.cs` - 3 handlers
- `lib-actor/Runtime/ActorRunner.cs` - 2 methods
- `lib-actor/PoolNode/ActorPoolNodeWorker.cs` - 1 method
- `lib-behavior/Coordination/InputWindowManager.cs` - 2 methods
- `lib-behavior/Coordination/SyncPointManager.cs` - 1 method
- `lib-behavior/Coordination/CutsceneCoordinator.cs` - 1 method
- `lib-behavior/Coordination/CutsceneSession.cs` - 4 methods
