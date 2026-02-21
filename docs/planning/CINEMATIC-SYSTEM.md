# The Cinematic System: From Combat Dream to Choreographic Reality

> **Type**: Architectural planning document
> **Priority**: High (last remaining architectural gap)
> **Related**: [VISION-PROGRESS.md](VISION-PROGRESS.md) (R9), [ACTOR-BOUND-ENTITIES.md](ACTOR-BOUND-ENTITIES.md), [WHY-ARE-THERE-NO-SKILL-MAGIC-OR-COMBAT-PLUGINS.md](../faqs/WHY-ARE-THERE-NO-SKILL-MAGIC-OR-COMBAT-PLUGINS.md)
> **Inspiration**: *Final Fantasy XVI* (cinematic combat), *Tales of Destiny* (living weapon choreography), *Nier: Automata* (2B/9S combat as character expression)
> **Services**: lib-cinematic (new L4), CinematicTheory (new SDK), CinematicStoryteller (new SDK)

---

## Executive Summary

The Bannou architecture has exactly one remaining structural gap: **cinematic composition**. Every other system -- from the lowest infrastructure primitive to the highest orchestration layer -- exists as either an implemented plugin, a fully specified design, or both. The cinematic system is the only place where the architecture says "and then this happens" without having defined *how* it happens.

The gap is narrow but critical. The *runtime* for cinematics already exists: `CinematicInterpreter` handles streaming composition with continuation points, `CutsceneCoordinator` manages multi-entity session state, and `IClientCutsceneHandler` provides the client integration surface. What's missing is the *compositional layer* -- the system that takes an encounter context (environment, participants, capabilities, dramatic requirements) and produces the choreographic ABML document that the runtime then executes.

This follows the established Theory/Storyteller/Plugin pattern exactly:

| Domain | Theory SDK | Storyteller SDK | Plugin | Status |
|--------|-----------|-----------------|--------|--------|
| **Music** | MusicTheory | MusicStoryteller | lib-music (L4) | Implemented |
| **Narrative** | StorylineTheory | StorylineStoryteller | lib-storyline (L4) | Implemented |
| **Choreography** | CinematicTheory | CinematicStoryteller | lib-cinematic (L4) | **Gap** |

This document captures the full picture: what exists, what's missing, and how the pieces fit together.

---

## Part 1: What Already Exists

The existing cinematic infrastructure is substantial. The gap is not "we need a cinematic system" -- it's "we need the composition layer that feeds the cinematic system we already have."

### 1.1 The Runtime Stack (Complete)

Three layers of execution infrastructure are implemented and tested:

```
CinematicRunner (lib-behavior)
    │  High-level lifecycle: start, evaluate, complete, abort
    │  Entity control management (ControlGateManager)
    │  State machine: Idle → Running → WaitingForExtension → Completed
    │
    ├──► CinematicInterpreter (behavior-compiler SDK)
    │      Streaming composition with continuation points
    │      Extension injection (RegisterExtension / InjectExtension)
    │      Timeout-based default flow fallback
    │      Input/output state management
    │
    └──► CutsceneSession + CutsceneCoordinator (lib-behavior)
           Multi-entity session coordination
           Sync point management (ISyncPointManager)
           Input window management (IInputWindowManager)
           Active session registry with entity lookup
```

**CinematicInterpreter** is the core. It wraps `BehaviorModelInterpreter` to add:

- **Continuation points**: Named pause locations in ABML bytecode where external extensions can be injected. Each point has a timeout and a default flow offset -- if no extension arrives before the timeout, the interpreter falls through to the default choreography.
- **Streaming composition**: Extensions can be registered ahead of time or injected mid-execution. The interpreter evaluates through base behavior, pauses at continuation points, executes injected extensions, then resumes.
- **Graceful degradation**: If an extension is late, the default flow executes. The cinematic never blocks indefinitely.

**CutsceneSession** coordinates the multi-entity aspects:

- **Sync points**: Barriers where multiple entities must arrive before the sequence continues. Thread-safe with timeout support and completed/timed-out events.
- **Input windows**: QTE/choice moments where a player can influence the choreography. Configurable timeout, behavior default resolver, optional sync point emission on completion.
- **Session lifecycle**: Create, report sync, submit input, complete/abort. Clean disposal of all resources.

**CinematicRunner** ties it together:

- **Entity control**: Acquires control of entities via `ControlGateManager` at start, returns control on complete/abort. Entities participating in a cinematic are "owned" by the runner.
- **State sync**: Propagates cinematic state through `IStateSync` for distributed coordination.
- **Events**: `CinematicStarted`, `CinematicCompleted`, `ControlReturned` for external consumers.

### 1.2 The Client Integration Surface (Complete)

`IClientCutsceneHandler` defines what the game client implements to render cinematics:

```csharp
public interface IClientCutsceneHandler
{
    // Lifecycle
    Task OnCutsceneStartAsync(sessionId, cinematicId, controlledEntities, ct);
    Task OnCutsceneEndAsync(sessionId, wasAborted, ct);

    // Choreographic actions (character/entity)
    Task ExecuteActionAsync(entityId, action, parameters, ct);
    // Actions: walk_to, attack, look_at, play_animation, emote, speak

    // Camera direction
    Task ExecuteCameraActionAsync(action, parameters, ct);
    // Actions: move_to, track, shake, zoom, cut_to

    // Multi-entity coordination
    Task OnSyncPointReachedAsync(syncPointId, entityId, ct);
    Task OnSyncPointReleasedAsync(syncPointId, ct);
}
```

The client SDK is action-type-agnostic -- `ExecuteActionAsync` takes a string action name and parameter dictionary. This means the composition layer can define new action types without changing the client interface. The client interprets action names and maps them to animations, particle effects, camera movements, or whatever the game engine provides.

### 1.3 The ABML Action System (Complete)

The behavior compiler already handles choreographic actions through the `DomainAction` node type:

```csharp
public sealed record DomainAction(
    string Name,                                    // "animate", "speak", "move_to", etc.
    IReadOnlyDictionary<string, object?> Parameters, // Action-specific parameters
    IReadOnlyList<ActionNode>? OnError = null        // Error handling
) : ActionNode, IHasOnError;
```

Domain actions are the generic catch-all in the ABML action system. They pass through to the runtime as-is -- the compiler doesn't need to understand what "animate" or "camera_shake" means. This is by design: the compiler handles control flow, variables, and conditions; the runtime handles domain-specific action execution.

This means **ABML already has full choreographic authoring capability**. A behavior document can describe a fight sequence today:

```yaml
actions:
  - animate:
      entity: ${attacker}
      animation: sword_overhead_strike
      duration: 1.2
  - sync: strike_contact
  - animate:
      entity: ${defender}
      animation: block_high
      blend: 0.3
  - camera_action:
      action: shake
      intensity: 0.4
      duration: 0.5
```

What's missing is the system that *generates* these documents from encounter context.

### 1.4 The Extension Points (Complete)

The progressive agency connection is already architecturally sound:

- **Agency service** (L4) manages `${spirit.domain.combat.fidelity}` -- a float representing the guardian spirit's earned understanding of combat
- **CinematicInterpreter** supports continuation points where extensions can be injected
- **InputWindowManager** creates QTE/choice windows with configurable timeouts and behavior defaults

The intended flow: the composition layer reads the spirit's combat fidelity, and based on that value, determines how many continuation points in the choreography become interactive QTE windows vs. playing out automatically. Same choreography, different interaction density. A spirit with 0.1 fidelity watches a cinematic fight. A spirit with 0.9 fidelity directs every strike.

---

## Part 2: What's Missing

### 2.1 CinematicTheory SDK (Pure Computation)

**Analogous to**: MusicTheory (harmony, melody, pitch, MIDI-JSON output)

CinematicTheory is the formal grammar of choreographic composition. Where MusicTheory understands scales, chord progressions, and voice leading, CinematicTheory understands:

**Dramatic Grammar**:
- Beat structure (setup, escalation, climax, resolution within a sequence)
- Tension curves (how intensity builds and releases across a choreographic phrase)
- Dramatic timing (pauses, rushes, rhythmic variation in action sequences)
- Emotional arc mapping (how a fight tells a story -- aggression, desperation, triumph)

**Spatial Reasoning**:
- Affordance evaluation: given a location's spatial data (from Mapping), what choreographic moves are possible? A narrow bridge fight differs from an open field fight.
- Positioning logic: where entities should be relative to each other for a given action (flanking, surrounding, facing, retreating)
- Environmental interaction: what objects in the space can be used choreographically (tables to flip, walls to bounce off, elevation to exploit)

**Capability Matching**:
- Species behavioral archetypes (from Ethology): a wolf pack fights differently than a dragon
- Character combat preferences (from `${combat.*}` variables): an aggressive character initiates; a cautious one counters
- Equipment and item capabilities (from `${character.*}` variables): a character with a polearm has different choreographic options than one with dual daggers
- Special abilities: magic users, divine blessings, status effects that modify capability sets

**Camera Direction**:
- Shot composition rules (wide for establishing, close for emotional, tracking for movement)
- Cut rhythm (how often to change camera angle, matched to action intensity)
- Focus management (who/what the camera emphasizes at each beat)
- Spatial clarity (the audience must always understand where entities are relative to each other)

**Output Format**: ABML documents with DomainAction nodes -- the same format the existing runtime already consumes. CinematicTheory produces data structures; the caller (CinematicStoryteller or lib-cinematic) serializes them into ABML.

### 2.2 CinematicStoryteller SDK (GOAP-Driven Auto-Composition)

**Analogous to**: MusicStoryteller (narrative templates, emotional state planning)

CinematicStoryteller uses GOAP planning to compose cinematics from encounter context. Where MusicStoryteller plans emotional trajectories and selects composition templates, CinematicStoryteller:

**World State for Planning**:
- Participants (who is involved, their capabilities, their relationships)
- Environment (spatial affordances, lighting, weather, time of day)
- Dramatic context (is this a duel of honor, a surprise ambush, a desperate last stand?)
- Player agency level (`${spirit.domain.combat.fidelity}` for QTE density decisions)
- Encounter stakes (how important is this fight in the narrative arc?)

**Goals**:
- Produce a choreographic sequence that is dramatically satisfying
- Respect participant capabilities (don't choreograph a dodge for a character that can't dodge)
- Include appropriate QTE density for the current player agency level
- Manage tension curve (not every beat is peak intensity)
- End in a way that supports the narrative outcome (victory, defeat, escape, interruption)

**Actions (in GOAP terms)**:
- `compose_exchange` - Create an attack/defense/counter beat between two participants
- `insert_environmental_interaction` - Use a spatial affordance in the choreography
- `add_dramatic_pause` - Insert a tension-building beat (stare-down, weapon readying)
- `inject_qte_window` - Convert a choreographic beat into a player interaction moment
- `add_camera_emphasis` - Direct the camera for dramatic effect at a key moment
- `compose_group_action` - Coordinate multiple participants in a single beat (flanking, formation)
- `trigger_continuation_point` - Mark a spot where the sequence can be extended based on runtime events

**Output**: Complete ABML cinematic documents, ready for `CinematicInterpreter` execution. The plan determines the sequence; CinematicTheory's grammar rules ensure each beat is spatially and dramatically valid.

### 2.3 lib-cinematic Plugin (L4, Thin API Wrapper)

**Analogous to**: lib-music (wraps MusicTheory+MusicStoryteller, exposes HTTP API, caches results)

lib-cinematic is a thin orchestration layer following the established plugin pattern:

| Endpoint | What It Does | Called By |
|----------|-------------|-----------|
| `/cinematic/compose` | Participants + environment + constraints -> choreographic sequence | Event Brain actors via ABML |
| `/cinematic/extend` | Existing sequence + continuation point + new context -> extension | Event Brain actors mid-encounter |
| `/cinematic/resolve-input` | QTE definition + player input -> outcome + branch selection | Gardener (routing player influence) |
| `/cinematic/list-active` | Query active cinematic instances by realm/location | Puppetmaster, admin tools |
| `/cinematic/abort` | Force-end an active cinematic | Admin, disconnect handling |

**What lib-cinematic owns**:
- Active cinematic instance tracking (which cinematics are running, where, with whom)
- Composition caching (deterministic when seeded, like lib-music)
- Agency-gated QTE density computation (reads `${spirit.domain.combat.fidelity}` from Agency)
- Variable provider for Actor: `${cinematic.*}` namespace (is entity in cinematic, current beat, role)

**What lib-cinematic does NOT own**:
- Runtime execution (that's CinematicInterpreter + CinematicRunner in lib-behavior)
- Session coordination (that's CutsceneSession + CutsceneCoordinator in lib-behavior)
- Client rendering (that's IClientCutsceneHandler in the game client)
- Behavior decisions about *when* to initiate combat (that's Actor + GOAP)

**Why it's L4 and not L2**: lib-cinematic needs data from L4 services (character-personality for combat preferences, character-encounter for grudge-driven choreographic emphasis, ethology for species behavioral archetypes). It also needs L3's Asset service for behavior document storage via `IBehaviorDocumentProvider`. The runtime (in lib-behavior at L2/L4) handles execution; the composition layer needs the full context stack.

---

## Part 3: The Theory/Storyteller/Plugin Pattern

This is the third instantiation of a proven architectural pattern:

```
┌─────────────────────────────────────────────────────────────────┐
│ Theory SDK (pure computation, no service dependencies)          │
│   Formal grammar, rules, primitives                            │
│   Deterministic: same inputs -> same output                    │
│   Testable in isolation (unit tests only)                      │
├─────────────────────────────────────────────────────────────────┤
│ Storyteller SDK (GOAP-driven composition, no service deps)      │
│   World state -> goal -> action plan -> output                 │
│   Uses Theory for validation and grammar rules                 │
│   Deterministic when seeded                                    │
├─────────────────────────────────────────────────────────────────┤
│ Plugin (thin API wrapper, full service dependencies)            │
│   HTTP endpoints for ABML-callable composition                 │
│   Variable provider for Actor (${domain.*} namespace)          │
│   Caching, instance tracking, cleanup                          │
│   Reads context from other services via generated clients      │
└─────────────────────────────────────────────────────────────────┘
```

### Pattern Comparison

| Aspect | Music | Storyline | Cinematic |
|--------|-------|-----------|-----------|
| **Theory SDK** | Harmony, melody, pitch, voicing, MIDI-JSON | Narrative kernels, emotional arcs, spectrums | Dramatic grammar, spatial reasoning, capability matching, camera direction |
| **Theory inputs** | Key, mode, style, tempo, instrumentation | Archive bundle, genre spectrum, kernel scores | Participants, environment, capabilities, dramatic context |
| **Storyteller SDK** | Emotional state planning, template selection | Arc planning, phase sequencing, entity casting | Exchange composition, tension curve, QTE placement |
| **Storyteller output** | MIDI-JSON composition | Narrative plan with phases and actions | ABML cinematic document with DomainActions |
| **Plugin** | lib-music (L4) | lib-storyline (L4) | lib-cinematic (L4) |
| **Plugin endpoints** | `/music/compose`, `/music/progression` | `/storyline/plan`, `/storyline/generate` | `/cinematic/compose`, `/cinematic/extend`, `/cinematic/resolve-input` |
| **Variable provider** | N/A (music is ambient, not per-entity) | N/A (storyline is per-scenario, not per-entity) | `${cinematic.*}` (is entity in cinematic, role, current beat) |
| **Called by** | Any ABML behavior (mood-setting) | Regional Watcher actors (scenario orchestration) | Event Brain actors (combat/encounter initiation) |

### The Architectural Property

All three instantiations share a critical property: **the SDK is independently referenceable**. Just as `MusicTheory` can be used by any project (game client, testing tool, offline composition tool) without importing Bannou service infrastructure, `CinematicTheory` must be usable by:

- Game clients (for client-side choreography prediction/smoothing)
- Authoring tools (for cinematic design and preview)
- Unit tests (for grammar and composition validation)
- The behavior-compiler SDK (for compile-time cinematic validation)

This is why it's an SDK, not part of the plugin.

---

## Part 4: The Combat Dream, Realized

VISION.md describes the Combat Dream:

```
Event Brain (Actor) ──queries──► Mapping Service (affordances)
        │                         "What's in the environment?"
        │
        ├──queries──► Character Agents (capabilities)
        │                 "What can this character do? What would they choose?"
        │
        ├──composes──► Cinematic Interpreter (streaming composition)
        │                 Base cinematic + continuation-point extensions
        │
        └──coordinates──► Three-Version Temporal Desync
                          (canonical past, participant present, spectator projection)
```

With the cinematic system in place, this becomes concrete:

### Step 1: Event Brain Initiates Encounter

An Event Brain actor (launched by Puppetmaster as a Regional Watcher) detects an encounter opportunity -- two hostile characters in proximity, a dramatic scenario condition met, a player stumbling into a dangerous area. The Event Brain's ABML behavior decides to initiate combat.

### Step 2: Composition Request

The Event Brain calls `/cinematic/compose` via ABML `service_call` action:

- **Participants**: character IDs from the encounter context
- **Environment**: location ID (lib-cinematic queries Mapping for spatial affordances)
- **Dramatic context**: encounter type, narrative stakes (from the Event Brain's scenario data)
- **Constraints**: duration range, intensity level, allowed action types

### Step 3: CinematicStoryteller Plans

lib-cinematic feeds the context into CinematicStoryteller:

1. CinematicStoryteller queries participant capabilities (combat preferences, species archetypes, equipment)
2. CinematicStoryteller queries environment affordances (spatial data, interactive objects)
3. GOAP planner composes a sequence of dramatic beats (exchanges, pauses, environmental interactions)
4. CinematicTheory validates each beat against spatial/capability constraints
5. QTE continuation points are inserted based on player agency level
6. Output: ABML cinematic document

### Step 4: Runtime Execution

The ABML document is loaded into `CinematicInterpreter` via `CinematicRunner`:

1. Runner acquires control of participating entities
2. Interpreter evaluates through the choreography, emitting DomainActions
3. At continuation points, the interpreter pauses for extensions (QTE inputs, runtime adaptations)
4. `CutsceneSession` coordinates sync points across entities
5. `IClientCutsceneHandler` receives actions and renders them on the game client

### Step 5: Player Interaction (Agency-Gated)

For QTE continuation points, the flow branches based on spirit fidelity:

- **Low fidelity** (new spirit): continuation point times out, default flow executes. The fight plays out as a cinematic the player watches.
- **Medium fidelity**: some continuation points become input windows (dodge left/right, attack/defend). The player influences key moments.
- **High fidelity**: most continuation points are interactive. The player directs the choreography beat-by-beat, approaching the "full martial choreography" end of the PLAYER-VISION.md gradient.

The progressive agency connection is the same choreographic computation producing the same fight sequence -- the difference is how many moments the player gets to influence vs. how many play out automatically.

### Step 6: Three-Version Temporal Desync

This is the most speculative part of the Combat Dream and likely the last piece to implement:

- **Canonical past**: The "true" sequence as it happened (stored as a cinematic recording)
- **Participant present**: What participants experience in real-time (interactive, responsive)
- **Spectator projection**: What observers see (smoothed, dramatically enhanced)

The temporal desync is primarily a client-side rendering concern, not a service-layer concern. The server produces one authoritative choreographic sequence; the client renders it differently based on the viewer's relationship to the event. This means lib-cinematic doesn't need to "own" temporal desync -- it produces the canonical sequence, and the client SDK handles perspective rendering.

---

## Part 5: Connection to Progressive Agency

The cinematic system is the most direct implementation of the PLAYER-VISION.md combat UX gradient:

| Spirit Fidelity | Combat Experience | Cinematic Interaction |
|----------------|------------------|----------------------|
| **0.0 - 0.2** | New spirit, no combat understanding | Pure cinematic -- watch the fight unfold |
| **0.2 - 0.4** | Basic awareness | Directional intent (approach/retreat/hold) at key moments |
| **0.4 - 0.6** | Stance perception | Defensive/aggressive posture choices, dodge windows |
| **0.6 - 0.8** | Timing mastery | Parry/strike timing windows, combo direction |
| **0.8 - 1.0** | Full martial choreography | Beat-by-beat direction, stance selection, combo composition |

This is not a discrete unlock system. The fidelity value is a continuous float that determines:

1. **QTE density**: How many continuation points become interactive
2. **QTE complexity**: Simple binary choices vs. multi-option timing windows
3. **QTE timing**: Generous windows for lower fidelity, tight windows for higher
4. **Default quality**: Even at low fidelity, the "automatic" choreography should be good -- the spirit is watching a real fight, just without the ability to influence it

The key insight: **the composition layer does the same amount of work regardless of fidelity**. It always plans a full choreographic sequence with continuation points. The fidelity value only determines how many of those points become player-interactive vs. auto-resolving. This means the system degrades gracefully -- removing player agency doesn't simplify the choreography, it just changes who controls it (the system's behavior defaults vs. the player's input).

---

## Part 6: Existing Infrastructure Inventory

### In behavior-compiler SDK (`sdks/behavior-compiler/`)

| Component | Purpose | Key API |
|-----------|---------|---------|
| `CinematicInterpreter` | Streaming composition runtime | `RegisterExtension()`, `InjectExtension()`, `Evaluate()` |
| `ContinuationPoint` | Named pause location in bytecode | `NameHash`, `TimeoutMs`, `DefaultFlowOffset`, `BytecodeOffset` |
| `ContinuationPointTable` | Indexed continuation point collection | `TryGetByHash()`, `TryGetByName()`, `Serialize()` |
| `CinematicEvaluationResult` | Evaluation step output | `Status` (Completed/WaitingForExtension/ExtensionExecuted) |
| `DomainAction` | Generic choreographic action node | `Name`, `Parameters`, `OnError` |

### In bannou-service (`bannou-service/Behavior/`)

| Component | Purpose | Key API |
|-----------|---------|---------|
| `ISyncPointManager` | Cross-entity synchronization barriers | `RegisterSyncPoint()`, `ReportReachedAsync()`, `WaitForAllAsync()` |
| `SyncPointStatus` | Barrier state tracking | `State`, `RequiredParticipants`, `ReachedParticipants`, `Timeout` |

### In lib-behavior (`plugins/lib-behavior/`)

| Component | Purpose | Key API |
|-----------|---------|---------|
| `CinematicRunner` | High-level lifecycle controller | `StartAsync()`, `Evaluate()`, `CompleteAsync()`, `AbortAsync()` |
| `CutsceneSession` | Multi-entity session coordination | `ReportSyncReachedAsync()`, `CreateInputWindowAsync()`, `SubmitInputAsync()` |
| `CutsceneCoordinator` | Active session registry | `CreateSessionAsync()`, `GetSession()`, `EndSessionAsync()` |
| `InputWindowManager` | QTE/choice window management | `CreateAsync()`, `SubmitAsync()`, `Close()` |

### In client SDK (`sdks/client/Behavior/`)

| Component | Purpose | Key API |
|-----------|---------|---------|
| `IClientCutsceneHandler` | Client-side rendering callbacks | `ExecuteActionAsync()`, `ExecuteCameraActionAsync()`, sync/lifecycle events |

---

## Part 7: Implementation Priority

### Phase 1: CinematicTheory SDK

Pure computation, zero service dependencies. Build the formal grammar:

1. **Dramatic beat primitives** (exchange, pause, environmental interaction, group action)
2. **Spatial reasoning** (affordance evaluation, positioning, environmental interaction selection)
3. **Capability matching** (what choreographic moves a participant can perform given their traits)
4. **Camera direction rules** (shot composition, cut rhythm, focus management)
5. **ABML document generation** (produce valid DomainAction sequences from beat compositions)

Deliverable: `sdks/cinematic-theory/` with unit tests. No Bannou service dependencies.

### Phase 2: CinematicStoryteller SDK

GOAP-driven composition, depends on CinematicTheory only:

1. **World state definition** (participants, environment, dramatic context, agency level)
2. **GOAP actions** (compose_exchange, insert_environmental_interaction, inject_qte_window, etc.)
3. **Tension curve management** (planning dramatic arc across the sequence)
4. **QTE density computation** (fidelity-based continuation point activation)
5. **Deterministic seeded output** (same seed + same context = same choreography)

Deliverable: `sdks/cinematic-storyteller/` with unit tests. Depends on `cinematic-theory` only.

### Phase 3: lib-cinematic Plugin

Thin API wrapper, full service integration:

1. **Schema definition** (`schemas/cinematic-api.yaml`) following standard patterns
2. **Service implementation** with generated clients for context retrieval
3. **Variable provider** (`${cinematic.*}` namespace for Actor)
4. **Active instance tracking** (which cinematics are running, per-realm/location)
5. **Composition caching** (Redis, deterministic-seeded results)
6. **IBehaviorDocumentProvider implementation** (so actors can load cinematic documents at runtime)

Deliverable: `plugins/lib-cinematic/` following standard plugin structure.

### Phase 4: Integration

Wire cinematic composition into the existing behavior execution stack:

1. **Event Brain integration**: ABML `service_call` actions to `/cinematic/compose` and `/cinematic/extend`
2. **Gardener integration**: Route player QTE inputs through `/cinematic/resolve-input`
3. **Puppetmaster integration**: Regional watchers can orchestrate encounter cinematics
4. **End-to-end test**: Event Brain detects encounter -> composes cinematic -> executes with QTE -> player influences outcome

---

## Part 8: What This Completes

With the cinematic system in place, the Combat Dream becomes implementable. More importantly, the entire compositional architecture is complete:

| Autonomous Domain | Decision Layer | Composition Layer | Execution Layer | Client Layer |
|------------------|---------------|-------------------|----------------|-------------|
| **NPC Cognition** | Actor GOAP (DocumentExecutor) | Variable providers + ABML | BehaviorModelInterpreter (bytecode) | Game client |
| **Music** | ABML behavior (mood selection) | MusicStoryteller (GOAP) | MusicTheory (computation) | Audio engine |
| **Narrative** | Regional Watcher (scenario selection) | StorylineStoryteller (GOAP) | StorylineTheory (kernels) | Quest/dialogue UI |
| **Combat/Choreography** | Event Brain (encounter detection) | CinematicStoryteller (GOAP) | CinematicTheory -> CinematicInterpreter | IClientCutsceneHandler |

Every domain follows the same pattern: **behavioral decision -> GOAP-driven composition -> formal grammar execution -> client rendering**. ABML is the universal authoring language across all of them.

The remaining work in Bannou -- implementing Divine, wiring up Transit/Environment variable providers, building Organization GOAP, fixing the Affix T29 violation -- is all "implement what's designed." The cinematic system is the last place where the design itself is incomplete.

After this, there are no more architectural gaps. Only implementation.

---

*This document captures the architectural analysis for the cinematic composition system. For the resolved Combat Dream discussion, see [VISION-PROGRESS.md R9](VISION-PROGRESS.md). For why combat is not a plugin, see [WHY-ARE-THERE-NO-SKILL-MAGIC-OR-COMBAT-PLUGINS.md](../faqs/WHY-ARE-THERE-NO-SKILL-MAGIC-OR-COMBAT-PLUGINS.md).*
