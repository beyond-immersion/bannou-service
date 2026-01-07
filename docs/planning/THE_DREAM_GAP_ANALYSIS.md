# THE DREAM - Gap Analysis v2

> **Status**: ANALYSIS DOCUMENT (Revised)
> **Created**: 2025-12-28
> **Revised**: 2026-01-07 (Updated for Streaming Composition completion)
> **Related**: [THE_DREAM.md](./THE_DREAM.md), [ABML_LOCAL_RUNTIME.md](./ONGOING_-_ABML_LOCAL_RUNTIME.md), [BEHAVIOR_PLUGIN_V2](./ONGOING_-_BEHAVIOR_PLUGIN_V2.md), [ACTORS_PLUGIN_V3](./UPCOMING_-_ACTORS_PLUGIN_V3.md)

This document analyzes the gap between THE_DREAM's vision and our current ABML implementation. Unlike the original gap analysis (written before ABML existed), this revision is grounded in what we've actually built and the architectural decisions we've made.

---

## 1. Executive Summary

**Key Insight**: THE_DREAM's original technical requirements assumed a service-to-service composition model that doesn't match what we've built or what we actually need. The real composition challenge is **streaming runtime extension** - the ability for a game server to receive a complete compiled behavior/cinematic and then seamlessly extend it mid-execution.

**What We Have**:
- Complete ABML parser, AST, expression evaluator (585 tests passing)
- Tree-walking `DocumentExecutor` for cloud-side interpretation
- **Complete bytecode compiler and interpreter** for client-side execution (226+ tests)
- Intent channel architecture for multi-model coordination (79+ tests)

**What THE_DREAM Actually Needs**:

| Need | Original Assumption | Actual Requirement |
|------|---------------------|-------------------|
| Event Brain ↔ Agent communication | Service-to-service calls | ✓ Correct - HTTP/mesh queries work |
| Character behavior execution | Service calls | ✗ Wrong - needs local bytecode |
| Cinematic extension | Import/include | ✗ Wrong - needs streaming append |
| Multi-character coordination | Complex arbitration | ✓ Solved - Intent channels with urgency |

**The Real Gaps** (in priority order):
1. ~~**Streaming execution model**~~ - ✅ COMPLETE (continuation points, CinematicInterpreter with pause/resume)
2. ~~**Bytecode compilation**~~ - ✅ COMPLETE (see ABML_LOCAL_RUNTIME.md Phase 1-2)
3. ~~**Behavior distribution**~~ - ✅ COMPLETE (lib-asset handles this; behavior is an asset type)
4. **Event Brain actor type** - the orchestrator itself (see ACTORS_PLUGIN_V3.md)

---

## 2. Composition Models Clarified

### 2.1 Three Types of "Composition"

The original gap analysis conflated different types of composition. Let's be precise:

#### Type A: Static Composition (Design-Time Import)

```yaml
# A behavior imports reusable snippets at parse time
imports:
  - "./shared/basic-attacks.abml"
  - "./shared/defensive-moves.abml"

flows:
  combat_decision:
    - call: basic_sword_attack  # From imported document
```

**When resolved**: Parse time (before compilation)
**Result**: Single merged AST → single compiled bytecode
**Use cases**: Reusable behavior libraries, shared constants, DRY authoring

**Status**: Not yet implemented, but straightforward - it's just AST merging before compilation.

#### Type B: Dynamic Composition (Runtime Lookup)

```yaml
# Event Brain queries character agents at runtime
flows:
  generate_options:
    - query_character_agent:
        character_id: "${participant.id}"
        query: "combat_options"
        context: "${combat_context}"
        result_variable: agent_options
```

**When resolved**: Runtime (during execution)
**Result**: Service call returns data, execution continues
**Use cases**: Event Brain querying Character Agents, Map Service affordance queries

**Status**: This is what THE_DREAM originally described. It works for cloud-side execution where latency is acceptable. We need the `query_character_agent` action type.

#### Type C: Streaming Composition (Runtime Extension)

```
Timeline:
0s     Game Server receives Cinematic A (compiled bytecode)
0-10s  Executes Cinematic A
8s     Event Brain decides to extend based on player action
8.5s   Game Server receives Extension B (compiled bytecode)
10s    Cinematic A completes, seamlessly transitions to B
10-18s Executes Extension B
```

**When resolved**: Runtime (during execution)
**Result**: Execution state extended without restart
**Use cases**: THE_DREAM's "extend before time runs out", dynamic dramatic acts

**Status**: ✅ **COMPLETE**. `CinematicInterpreter` supports pause at continuation points, extension injection, and timeout-based fallback to default flows.

### 2.2 Why Streaming Composition Matters

THE_DREAM's core promise is:

> "The game engine is always capable of completing the cinematic with what it was initially given. Event Agent *enriches* but isn't required for completion."

This requires:
1. Game server receives **complete, executable** cinematic
2. Game server **can** execute without further server contact
3. Event Brain **may** send extensions that enhance the experience
4. Extensions **must not** break the ongoing execution

This is fundamentally different from:
- Import (design-time, not runtime)
- Service calls (synchronous, blocking, requires connectivity)

### 2.3 Streaming Composition Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    STREAMING COMPOSITION MODEL                          │
│                                                                         │
│  ┌─────────────────┐                     ┌─────────────────┐           │
│  │   Event Brain   │                     │   Game Server   │           │
│  │    (Cloud)      │                     │    (Client)     │           │
│  └────────┬────────┘                     └────────┬────────┘           │
│           │                                       │                     │
│           │  1. Initial Cinematic (complete)      │                     │
│           │  ─────────────────────────────────► │                     │
│           │     [Header][Schema][Bytecode A]      │  ← Can execute      │
│           │                                       │    independently    │
│           │  2. Execution begins                  │                     │
│           │                                 ──────┼──────► Playing A    │
│           │                                       │                     │
│           │  3. Extension (optional)              │                     │
│           │  ─────────────────────────────────► │                     │
│           │     [Extension Header][Bytecode B]   │  ← Attaches to A    │
│           │     [Attach Point: "act_2_start"]    │                     │
│           │                                       │                     │
│           │  4. A completes, B continues          │                     │
│           │                                 ──────┼──────► Playing B    │
│           │                                       │                     │
└─────────────────────────────────────────────────────────────────────────┘
```

**Key Properties**:
- Initial delivery is **complete** - game server doesn't need to wait
- Extensions are **additive** - don't modify what's already executing
- Attach points are **named** - extensions declare where they connect
- Missing extensions are **fine** - original completes gracefully

---

## 3. Current State Assessment

### 3.1 What We've Built (Complete)

| Component | Location | Status | Tests |
|-----------|----------|--------|-------|
| ABML Parser | `bannou-service/Abml/Documents/` | ✅ Complete | Extensive |
| Document AST | `bannou-service/Abml/Documents/` | ✅ Complete | Extensive |
| Expression Lexer | `bannou-service/Abml/Expressions/` | ✅ Complete | 100+ tests |
| Expression Parser | `bannou-service/Abml/Expressions/` | ✅ Complete | 100+ tests |
| Expression Evaluator | `bannou-service/Abml/Expressions/` | ✅ Complete | 100+ tests |
| Document Executor | `bannou-service/Abml/Execution/` | ✅ Complete | Extensive |
| Variable Scopes | `bannou-service/Abml/Runtime/` | ✅ Complete | Tested |
| Error Handling | `bannou-service/Abml/Execution/` | ✅ Complete | `_error_handled` |
| Built-in Actions | Various | ✅ Core set | `cond`, `set`, `goto`, etc. |
| **Bytecode Compiler** | `lib-behavior/Compiler/` | ✅ Complete | 226 tests |
| **Bytecode Interpreter** | `sdk-sources/Behavior/Runtime/` | ✅ Complete | 61 tests |
| **Intent System** | `sdk-sources/Behavior/Intent/` | ✅ Complete | 79 tests |
| **Cinematic Interpreter** | `lib-behavior/Runtime/` | ✅ Complete | 13 pause/resume tests |
| **NPC Brain Integration** | `lib-actor/Runtime/` | ✅ Bannou-side | Perception/state wiring |
| **Game Transport (UDP)** | `Bannou.SDK/GameTransport/` | ✅ Complete | LiteNetLib integration |
| **Server SDK** | `Bannou.SDK/` | ✅ Complete | Full DI, mesh, all clients |

**Total**: 900+ tests passing (585 ABML + 226 compiler + 140 SDK)

### 3.1.1 SDK Enhancements (2026-01-05)

The `Bannou.SDK` now provides comprehensive game server integration:

- **Generated Service Clients**: Type-safe clients for all 18+ Bannou services
- **Mesh Service Routing**: Redis-based service discovery and YARP proxy integration
- **Game Transport (UDP)**: LiteNetLib-based server/client transport with MessagePack serialization
  - `LiteNetLibServerTransport` / `LiteNetLibClientTransport`
  - Message types: `PlayerInputMessage`, `ArenaStateSnapshot`, `ArenaStateDelta`, `OpportunityDataMessage`
  - Stride integration sketches for game server and client
- **WebSocket Integration**: `BannouClient` for Connect service events
- **Internal Connection Mode**: Service-to-service without JWT (service token or network trust auth)
- **Behavior Runtime**: Full ABML bytecode interpreter included for game server execution

### 3.2 Remaining Design-to-Implementation Gaps

| Component | Document | Status |
|-----------|----------|--------|
| ~~Behavior Distribution~~ | ~~LOCAL_RUNTIME §5.4~~ | ✅ lib-asset handles this |
| Multi-Channel Cutscenes | BEHAVIOR_PLUGIN §1.4 | Partially designed |
| Streaming Composition | LOCAL_RUNTIME §3.6 | Designed (critical gap) |

### 3.3 What's Not Yet Designed

| Component | Needed For | Priority |
|-----------|------------|----------|
| Streaming extension format | Cinematic extension | **Critical** |
| Attach point mechanism | Extension targeting | **Critical** |
| Extension delivery protocol | Game server updates | High |
| Event Brain actor schema | Orchestration | High |
| Character Agent query API | Option generation | High |
| Static import resolution | DRY authoring | Medium |

---

## 4. THE_DREAM Requirements Mapped to Architecture

### 4.1 Event Brain Orchestration

**THE_DREAM Says**: Event Brain watches combat, generates options, presents QTEs, emits choreography.

**Architecture**:
- **Runs in**: Cloud (Character Agent layer or above)
- **Executes via**: Tree-walking `DocumentExecutor` (not bytecode)
- **Communicates via**: lib-messaging events, mesh service calls
- **Produces**: Compiled cinematics/extensions for game servers

**Gap**: Event Brain actor type schema. Not a composition gap.

### 4.2 Character Agent Co-Pilot

**THE_DREAM Says**: Character agent knows capabilities, provides QTE defaults, computes options.

**Architecture**:
- **Runs in**: Cloud (Character Agent layer)
- **Has access to**: Character's behavior models (bytecode)
- **Responds to**: Event Brain queries
- **Uses models for**: "What would I do?" evaluation

**Gap**: Query API endpoint. The agent can already evaluate models locally.

### 4.3 Seamless Combat Escalation

**THE_DREAM Says**: Basic combat → Event Agent spawns → Cinematic treatment → Returns to basic.

**Architecture**:
- **Basic combat**: Bytecode models on game server, per-frame evaluation
- **Escalation**: Regional Watcher detects, Event Brain spawns
- **Cinematic**: Event Brain sends compiled cinematic to game server
- **Resolution**: Cinematic completes, game server returns to basic

**Gap**: The transition mechanics. How does game server know to pause basic evaluation during cinematic?

**Solution Sketch**:
```yaml
# Cinematic bytecode includes:
header:
  takes_control_of: ["character-123", "character-456"]
  control_mode: exclusive  # Suspends basic behavior
  on_complete: resume_basic
```

### 4.4 Dynamic Cinematic Extension

**THE_DREAM Says**: Event Agent enriches but isn't required; game engine can complete with initial plan.

**Architecture**:
- **Initial delivery**: Complete cinematic with default ending
- **Optional extension**: Additional acts if Event Brain generates them
- **Attach mechanism**: Named points where extensions can connect

**Gap**: **This is the streaming composition gap**. Detailed in Section 5.

### 4.5 Environmental Affordance Integration

**THE_DREAM Says**: Query Map Service for throwables, climbables, hazards.

**Architecture**:
- **Event Brain queries**: Map Service at orchestration time
- **Generates options**: Based on what's available
- **Compiles into cinematic**: Specific object references baked into bytecode

**Gap**: Map Service affordance API. Not a composition gap.

---

## 5. The Streaming Composition Gap (Critical)

### 5.1 Problem Statement

A game server receives a compiled cinematic:

```
[Cinematic: "Dramatic Duel" - 20 seconds]
  Act 1: Opening clash (0-5s)
  Act 2: Exchange of blows (5-15s)
  Act 3: Resolution (15-20s) ← Default: dramatic stalemate
```

At second 12, Event Brain decides player's actions warrant an extended ending:

```
[Extension: "Dramatic Finish"]
  Attaches to: "before_act_3"
  New Act 3: Environmental kill using nearby chandelier (15-25s)
```

**Requirements**:
1. Game server already executing original cinematic
2. Extension arrives before Act 3 starts
3. Execution seamlessly transitions to extended version
4. If extension arrives late, original Act 3 plays (graceful degradation)

### 5.2 Current Executor Limitations

The `DocumentExecutor` is a tree-walker that:
- Takes an `AbmlDocument` (parsed YAML)
- Executes `Flow` by `Flow`
- Cannot accept new flows mid-execution

For streaming composition, we need:
- Execution state that can be extended
- Named attach points that extensions reference
- Late-binding of "what comes next"

### 5.3 Proposed Solution: Continuation Points

**Concept**: Cinematics declare **continuation points** - named locations where execution may continue with additional content or fall through to default.

**Theoretical Foundation**: This pattern maps directly to **algebraic effects with handlers** from programming language theory (Plotkin & Pretnar, 2009). A continuation point is essentially a typed effect operation that yields control to a handler. The handler can provide an extension (resume with new content) or let it timeout (resume with default). The key innovation is **async delivery with deadline** - the handler doesn't block waiting for input but sets a timeout after which the default continuation is used. See THE_DREAM.md §12.2 for detailed theoretical background.

```yaml
# Original cinematic
version: "2.0"
metadata:
  id: "dramatic_duel"
  type: "cinematic"

flows:
  main:
    - parallel:
        camera: { flow: camera_track }
        hero: { flow: hero_actions }
        villain: { flow: villain_actions }

    - continuation_point:
        name: "before_resolution"
        timeout: 2s                    # Wait up to 2s for extension
        default_flow: default_ending   # If no extension arrives

  default_ending:
    - parallel:
        camera: { flow: stalemate_camera }
        hero: { flow: hero_backs_off }
        villain: { flow: villain_retreats }
```

**Extension format**:

```yaml
# Extension (sent separately)
version: "2.0"
metadata:
  id: "dramatic_finish_chandelier"
  type: "cinematic_extension"
  extends: "dramatic_duel"
  attach_point: "before_resolution"

flows:
  main:  # Replaces default_ending
    - parallel:
        camera: { flow: chandelier_focus }
        hero: { flow: hero_uses_chandelier }
        villain: { flow: villain_crushed }
```

### 5.4 Bytecode Format Extension

The bytecode format needs:

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    EXTENDED BYTECODE FORMAT                             │
├─────────────────────────────────────────────────────────────────────────┤
│  HEADER (extended)                                                      │
│  ├── ... existing fields ...                                           │
│  ├── Continuation Point Count: uint16                                  │
│  └── Is Extension: bool                                                │
├─────────────────────────────────────────────────────────────────────────┤
│  CONTINUATION POINTS TABLE (if count > 0)                               │
│  └── For each continuation point:                                      │
│      ├── Name Hash: uint32                                             │
│      ├── Timeout Ms: uint32                                            │
│      ├── Default Flow Offset: uint32                                   │
│      └── Extended Flow Offset: uint32 (0 = not yet set)                │
├─────────────────────────────────────────────────────────────────────────┤
│  EXTENSION HEADER (if Is Extension = true)                              │
│  ├── Parent Model ID: GUID                                             │
│  ├── Attach Point Name Hash: uint32                                    │
│  └── Replacement Flow Offset: uint32                                   │
├─────────────────────────────────────────────────────────────────────────┤
│  ... rest of bytecode ...                                               │
└─────────────────────────────────────────────────────────────────────────┘
```

### 5.5 Runtime Behavior

**Game Server Interpreter**:

```csharp
public sealed class CinematicInterpreter
{
    private readonly BehaviorModel _baseModel;
    private readonly Dictionary<uint, BehaviorModel> _extensions = new();

    /// <summary>
    /// Attach an extension to a continuation point.
    /// Can be called during execution.
    /// </summary>
    public void AttachExtension(BehaviorModel extension)
    {
        if (!extension.IsExtension)
            throw new ArgumentException("Not an extension model");

        _extensions[extension.AttachPointHash] = extension;
    }

    /// <summary>
    /// Called when execution reaches a continuation point.
    /// </summary>
    private void HandleContinuationPoint(ContinuationPoint cp)
    {
        // Check if extension has arrived
        if (_extensions.TryGetValue(cp.NameHash, out var extension))
        {
            // Extension available - use it
            _currentModel = extension;
            _instructionPointer = extension.MainFlowOffset;
            return;
        }

        // No extension yet - wait up to timeout
        var deadline = DateTime.UtcNow.AddMilliseconds(cp.TimeoutMs);

        // Set callback for when extension might arrive
        _pendingContinuation = new PendingContinuation
        {
            Point = cp,
            Deadline = deadline,
            OnTimeout = () => {
                // Use default flow
                _instructionPointer = cp.DefaultFlowOffset;
            }
        };
    }
}
```

### 5.6 Graceful Degradation

The streaming composition model ensures graceful degradation:

| Scenario | Behavior |
|----------|----------|
| Extension arrives early | Used immediately at continuation point |
| Extension arrives just in time | Used (within timeout window) |
| Extension arrives late | Ignored, default already playing |
| Extension never arrives | Default plays, cinematic completes normally |
| Network failure | Default plays, player sees complete experience |

**This is THE_DREAM's requirement**: "game engine can complete with initial plan."

---

## 6. Updated Gap List

### 6.1 Critical Gaps (Required for THE_DREAM)

| Gap | Description | Effort | Dependencies |
|-----|-------------|--------|--------------|
| ~~**Continuation Points**~~ | ~~ABML syntax for declaring extension points~~ | ~~Medium~~ | ✅ COMPLETE |
| ~~**Extension Format**~~ | ~~ABML syntax for extensions that attach to points~~ | ~~Medium~~ | ✅ COMPLETE |
| ~~**Bytecode Extensions**~~ | ~~Extended format for continuation/extension~~ | ~~Medium~~ | ✅ COMPLETE |
| ~~**Streaming Interpreter**~~ | ~~Runtime support for mid-execution attachment~~ | ~~High~~ | ✅ COMPLETE |
| **Extension Delivery** | Protocol for pushing extensions to game servers | Medium | Event schema complete, integration pending |

### 6.2 High Priority Gaps (Enable Core Functionality)

| Gap | Description | Effort | Dependencies |
|-----|-------------|--------|--------------|
| ~~**Bytecode Compiler**~~ | ~~AST → bytecode compilation~~ | ~~High~~ | ✅ COMPLETE |
| ~~**Bytecode Interpreter**~~ | ~~Stack-based VM for client execution~~ | ~~High~~ | ✅ COMPLETE |
| **Event Brain Actor Schema** | Actor type definition | Medium | Actor plugin (see ACTORS_V3) |
| **Character Agent Query API** | `/agent/query-combat-options` | Medium | Behavior plugin |
| **Control Handoff** | How cinematics take control from basic behavior | Medium | Intent system |

### 6.3 Medium Priority Gaps (Quality of Life)

| Gap | Description | Effort | Dependencies |
|-----|-------------|--------|--------------|
| **Static Imports** | Design-time ABML document composition | Medium | Parser changes |
| **Regional Watcher** | Area monitoring for event spawning | Medium | Actor plugin |
| **Affordance Query Actions** | `query_environment` ABML action | Medium | Map Service |
| **Dynamic Choice Action** | Runtime-generated QTE options | Medium | UI integration |

### 6.4 Lower Priority Gaps (Can Defer)

| Gap | Description | Notes |
|-----|-------------|-------|
| VIP Registry | Always-on Event Agents for important characters | Can use regular spawning initially |
| Crisis Interruption | Third-party joining mid-cinematic | Extension system handles this |
| Dramatic Pacing AI | "Beat templates" for authored feel | Content design problem |

---

## 7. What's NOT a Gap (Clarifications)

### 7.1 Service-to-Service Composition

**Original Gap Analysis Said**: Need complex service-to-service composition for Event Brain to query Character Agents.

**Actual Status**: This is just normal service calls. The `DocumentExecutor` can already make service calls via action handlers. We need the specific `query_character_agent` action, but the composition model is fine.

### 7.2 Complex Arbitration for Multi-Model Coordination

**Original Gap Analysis Said**: Need sophisticated AI to decide when combat overrides movement.

**Actual Status**: Solved by Intent Channels with urgency values. No arbitration AI needed - just compare floats. See LOCAL_RUNTIME §6.4.

### 7.3 Event Tap Pattern

**Original Gap Analysis Said**: Need special infrastructure for Event Agents to tap character event streams.

**Actual Status**: This is just lib-messaging subscriptions. No new infrastructure needed.

### 7.4 Orchestrator Spawning

**Original Gap Analysis Said**: Need detailed documentation on spawning Event Agent instances.

**Actual Status**: This is standard Orchestrator API usage. The gap is just documentation, not implementation.

---

## 8. Implementation Phases (Revised)

### Phase 1: Bytecode Foundation ✅ COMPLETE
**Goal**: Basic compilation and execution working

- [x] Bytecode format implementation (without continuation points)
- [x] AST → bytecode compiler (basic)
- [x] Stack-based interpreter (basic)
- [x] Round-trip test: ABML → bytecode → execution → same result as tree-walker

### Phase 2: Streaming Composition ✅ COMPLETE
**Goal**: Continuation points and extensions working

- [x] Continuation point ABML syntax (`continuation_point` action with name, timeout, default_flow)
- [x] Extension ABML syntax (extensions attach to named continuation points)
- [x] Extended bytecode format (`CONTINUATION_POINT` opcode with timeout and default flow offset)
- [x] Streaming interpreter (`CinematicInterpreter` with pause/resume, `EvaluateWithPause()`, `ResumeWithDefaultFlow()`, `ResumeWithExtension()`)
- [x] Tests: 13 tests covering extension arrives early/on-time/late/never, timeout, force resume, reset

### Phase 3: Distribution & Integration ✅ MOSTLY COMPLETE
**Goal**: Models flow to game servers, cinematics can be triggered

- [x] ~~Behavior Distribution Service~~ → lib-asset handles this (behavior is an asset type)
- [x] `/behavior/models/sync` API - via lib-asset sync
- [x] Server SDK with full mesh integration (`Bannou.SDK`)
- [x] Game Transport (UDP) with LiteNetLib for real-time state sync
- [x] Extension delivery event schema (`CinematicExtensionAvailableEvent` in behavior-events.yaml)
- [ ] Extension delivery integration (mesh routing + pub/sub fallback)
- [ ] Control handoff mechanism (cinematic takes control)
- [ ] Integration tests with mock game server

### Phase 4: NPC Brain Integration 🔄 IN PROGRESS (see ACTORS_PLUGIN_V3.md §5)
**Goal**: Character Agent ↔ Game Server perception/state flow

**Bannou-side (COMPLETE 2026-01-05):**
- [x] Perception subscription (`character.{characterId}.perceptions`) in ActorRunner
- [x] State update routing via lib-mesh (`PublishStateUpdateIfNeededAsync`)
- [x] `CharacterPerceptionEvent` schema with `sourceAppId` for routing
- [x] All 6 cognition handlers implemented and registered

**Stride-side (NEXT):**
- [ ] Publish perception events for managed characters
- [ ] Handle `character/state-update` endpoint
- [ ] Apply state updates to behavior input slots
- [ ] Implement lizard brain fallback

### Phase 5: Event Brain
**Goal**: Cloud-side cinematic orchestration working

- [ ] Event Brain actor schema (Event Actor in ACTORS_V3 terminology)
- [ ] Character Agent query API
- [ ] Event tap subscriptions (direct perception routing, not control plane)
- [ ] Option generation algorithm
- [ ] Choreography emission (produces cinematics)

### Phase 6: Full Integration
**Goal**: THE_DREAM works end-to-end

- [ ] Regional Watcher (spawns Event Agents)
- [ ] Map Service affordance queries
- [ ] Dynamic QTE presentation
- [ ] End-to-end test: basic combat → escalation → cinematic → resolution

---

## 9. Key Architectural Decisions

### 9.1 Streaming vs Import for Runtime Extension

**Decision**: Runtime extension uses **streaming composition** (continuation points + extensions), not imports.

**Rationale**:
- Imports are design-time, can't handle runtime decisions
- Service calls add latency, break offline capability
- Streaming allows "complete initial delivery" + "optional enhancement"

### 9.2 Continuation Point Timeout

**Decision**: Continuation points have a **configurable timeout** (default 2s).

**Rationale**:
- Can't wait forever (would freeze cinematic)
- Can't wait zero (would miss close-call extensions)
- Designer-configurable allows tuning per-situation

### 9.3 Extension Replaces Default (No Merge)

**Decision**: Extensions **replace** the default continuation, they don't merge.

**Rationale**:
- Merging is complex (what if both want to move camera?)
- Extensions are designed knowing the context
- Simpler implementation, clearer authoring model

### 9.4 Cloud-Side Event Brain, Client-Side Execution

**Decision**: Event Brain runs in cloud, produces cinematics; game server executes them locally.

**Rationale**:
- Event Brain needs Map Service access, character knowledge, dramatic AI
- Execution needs <1ms latency for smooth playback
- This matches the LOCAL_RUNTIME layer model

---

## 10. Success Criteria (Updated)

### Technical Criteria

| Metric | Target | Notes |
|--------|--------|-------|
| Extension attachment latency | <50ms | From receive to execution transition |
| Graceful degradation rate | 100% | Every cinematic must be completable without extension |
| Continuation point overhead | <1ms | Don't slow down normal execution |
| Extension delivery latency | <500ms | From Event Brain decision to game server receive |

### Experience Criteria

- Extensions feel seamless (no visible "loading" or "switching")
- Players never see incomplete cinematics
- Network issues result in simpler (not broken) experiences
- Event Brain enrichment is noticeable when present

### Development Criteria

- Continuation points are easy to author
- Extensions can be tested independently
- Debug tools show continuation point state
- Clear error messages when extensions don't match

---

## 11. Conclusion

The original gap analysis was written when ABML was theoretical. Now that we have a working implementation, the gaps are clearer:

1. **The big gap is streaming composition** - not imports, not service calls, but runtime extension of executing behaviors
2. **The Intent Channel system solved multi-model coordination** - no complex arbitration needed
3. **Most "service integration" gaps are just normal service calls** - the architecture supports them
4. **The bytecode format needs continuation point support** - this is a format extension, not a new system

THE_DREAM is achievable with:
- Continuation points in ABML/bytecode
- Streaming interpreter that accepts extensions
- Event Brain that produces cinematics + extensions
- Distribution protocol that delivers them to game servers

**Novelty Note**: Research confirms this combination is genuinely novel. No existing system combines graceful degradation + precise choreography + runtime extension + async delivery with timeout. The closest academic concepts are algebraic effects (theoretical) and dynamic behavior trees (synchronous). The closest industry implementations are Left 4 Dead's AI Director (macro-level) and procedural cinematics like AC Odyssey (pre-generated). See THE_DREAM.md §12 for detailed prior art analysis.

The path is clear. The composition model is defined. Let's build it.

---

*Document Status: ANALYSIS (Revised) - Supersedes original gap analysis*

## Related Documents

- [THE_DREAM.md](./THE_DREAM.md) - Vision document
- [ONGOING_-_ABML_LOCAL_RUNTIME.md](./ONGOING_-_ABML_LOCAL_RUNTIME.md) - Local bytecode execution
- [ONGOING_-_BEHAVIOR_PLUGIN_V2.md](./ONGOING_-_BEHAVIOR_PLUGIN_V2.md) - Behavior runtime
- [UPCOMING_-_ACTORS_PLUGIN_V3.md](./UPCOMING_-_ACTORS_PLUGIN_V3.md) - Actor infrastructure (authoritative)
- [ABML Guide](../guides/ABML.md) - ABML language specification
