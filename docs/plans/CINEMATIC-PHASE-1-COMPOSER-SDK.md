# Phase 1: ABML Bridge Proof + cinematic-composer SDK

> **Status**: Draft
> **Parent Plan**: [CINEMATIC-SYSTEM.md](CINEMATIC-SYSTEM.md)
> **Prerequisites**: Phase 0 complete (storyline-scenario SDK establishes the pattern)
> **Estimated Scope**: Bridge validation + new SDK

---

## Goal

Two sub-goals in sequence:

1. **Prove the runtime bridge** by hand-authoring a multi-channel ABML document with sync points, QTE branching, and cross-channel correlation, then compiling and executing it through the existing runtime stack. This validates that CinematicInterpreter + CutsceneSession + InputWindowManager can handle what the SDK will produce.

2. **Build the cinematic-composer SDK** with the authoring format, validation, and ABML export. Narrowed scope for this phase: data model, timeline, actions, QTE, AbmlExporter, and basic validation only.

---

## Sub-Phase 1A: Bridge Validation (CRITICAL -- Do This First)

### Why This Comes First

The entire cinematic system depends on the AbmlExporter producing multi-channel ABML that compiles and executes correctly on the existing runtime. If the runtime has gaps (branch node handling, cross-channel QTE correlation, sync point emission from QTE outcomes), we need to find them **before** building an SDK that generates ABML targeting those features.

### What To Validate

Hand-author a minimal ABML YAML document that exercises:

1. **Multiple channels**: At least 3 (attacker, defender, camera)
2. **Sync barrier**: Attacker emits a sync point, defender waits for it
3. **QTE window**: Defender gets a directional QTE choice after the sync
4. **Cross-channel branching**: QTE outcome selects branches in BOTH the defender channel and the camera channel simultaneously
5. **Continuation point**: An extension injection point where new content can be streamed in

### The Test Scenario

A minimal "throw and dodge" encounter:

```
Channel: attacker
  - animate: wind_up_throw (1.0s)
  - emit_sync: throw_release
  - animate: throw (0.5s)

Channel: defender
  - animate: brace (1.0s)
  - wait_for: throw_release
  - continuation_point: dodge_choice (timeout: 0.4s, default: dodge_left)
    - branch dodge_left:
        - animate: roll_left (0.4s)
    - branch dodge_right:
        - animate: roll_right (0.4s)

Channel: camera
  - camera: wide_shot (1.0s)
  - wait_for: throw_release
  - [must branch in sync with defender's QTE outcome]
    - branch dodge_left:
        - camera: low_angle_left (0.7s)
    - branch dodge_right:
        - camera: low_angle_right (0.7s)
```

### Validation Steps

1. **Write the ABML YAML** by hand (not SDK-generated). This is the target format the AbmlExporter must produce.

2. **Compile via behavior-compiler**: Feed the YAML through the ABML parser and compiler. Does it produce a valid `BehaviorModel`? If not, what ABML constructs are missing or malformed?

3. **Execute via CinematicInterpreter**: Create a CinematicInterpreter with the compiled model. Step through evaluation. Does it:
   - Process channels independently?
   - Pause at the sync barrier until all channels report?
   - Pause at the continuation point waiting for extension/input?
   - Branch correctly when input arrives?

4. **Execute via CutsceneSession**: Create a CutsceneSession with the compiled model. Does it:
   - Track sync point progress across participants?
   - Create an input window for the QTE?
   - Route the QTE result to the correct branch?
   - Correlate the branch across channels (defender + camera branch together)?

5. **Document gaps**: If any step fails, document exactly what's missing. These become pre-requisite fixes before building the SDK.

### Expected Outcomes

| Outcome | What It Means | Next Step |
|---------|--------------|-----------|
| Full stack works | Runtime handles everything the SDK needs to produce | Proceed to Sub-Phase 1B |
| Sync points work, QTE works, cross-channel branching fails | Runtime needs a cross-channel branch correlation mechanism | Fix in behavior-compiler/lib-behavior before Sub-Phase 1B |
| Branch nodes don't support QTE-driven selection | CinematicInterpreter needs QTE input → branch mapping | Fix in behavior-compiler before Sub-Phase 1B |
| Multi-channel ABML doesn't compile | Behavior-compiler needs multi-channel support | Fix in behavior-compiler before Sub-Phase 1B |

### Deliverable

A working hand-authored ABML document that exercises the full runtime stack, plus documentation of any gaps found and fixes applied. This document becomes the reference for what AbmlExporter must produce.

---

## Sub-Phase 1B: cinematic-composer SDK

### Narrowed Scope

Phase 1 builds the minimum needed to define, validate, and export cinematic scenarios. Matching (finding scenarios for encounter contexts) and YAML serialization for authoring tools are deferred.

**In scope**:
- Scenario data model (participants, triggers, timeline, metadata)
- Timeline model (tracks, clips, sync barriers, continuation points, branches)
- Action definitions (animate, camera, environmental, QTE)
- AbmlExporter (scenario → multi-channel ABML)
- TimelineValidator (structural validation)

**Deferred to Phase 2 or later**:
- `Matching/` (ScenarioMatcher, SlotBinder, EncounterContext) -- the plugin will need this, but it's not needed to prove the authoring format
- `Serialization/ScenarioSerializer.cs` (YAML round-trip) -- authoring tool infrastructure, not bridge-proving

### SDK Structure

```
sdks/cinematic-composer/
|-- cinematic-composer.csproj
|
|-- Scenarios/
|   |-- CinematicScenario.cs         # Root: participants, triggers, timeline, metadata
|   |-- ParticipantSlot.cs           # Named slot with capability requirements
|   |-- CapabilityRequirement.cs     # Required tags, states, stats, props
|   |-- TriggerCondition.cs          # Distance, state, affordance, environmental conditions
|   `-- ScenarioMetadata.cs          # Priority, cooldown, exclusivity tags, classification tags
|
|-- Timeline/
|   |-- CinematicTimeline.cs         # Container for all tracks + segments
|   |-- Track.cs                     # Named track bound to a participant slot or "camera"
|   |-- TrackClip.cs                 # Action on a track: start time, duration, parameters
|   |-- SyncBarrier.cs               # Named barrier: emitting track + waiting tracks
|   |-- ContinuationPoint.cs         # Extension/QTE point: timeout, default flow
|   `-- Branch.cs                    # Conditional branch: outcome -> segment reference
|
|-- Actions/
|   |-- ActionDefinition.cs          # Base: action code, parameters dictionary
|   |-- AnimateAction.cs             # Character animation: clip name, blend, speed
|   |-- CameraAction.cs             # Camera: shot type, target, transition
|   |-- EnvironmentalAction.cs       # Prop interaction: grab, throw, interact (references scene nodes)
|   `-- QteAction.cs                 # QTE trigger: type, options, timeout, default, branches
|
|-- Validation/
|   `-- TimelineValidator.cs         # Sync points reachable, timing sane, slots covered, branches valid
|
`-- Export/
    `-- AbmlExporter.cs              # Scenario -> multi-channel ABML YAML string
```

**Dependencies**: `sdks/scene-composer/` (for Vector3, Transform spatial primitives)

### Key Types

#### CinematicScenario (Root)

```csharp
public sealed class CinematicScenario
{
    /// <summary>Unique scenario code for registry lookup.</summary>
    public required string Code { get; init; }

    /// <summary>Human-readable name.</summary>
    public required string Name { get; init; }

    /// <summary>Optional description.</summary>
    public string? Description { get; init; }

    /// <summary>Named participant slots with capability requirements.</summary>
    public required IReadOnlyList<ParticipantSlot> Participants { get; init; }

    /// <summary>Conditions that must be true to trigger this scenario.</summary>
    public required IReadOnlyList<TriggerCondition> Triggers { get; init; }

    /// <summary>The temporal content: tracks, clips, sync barriers, QTEs.</summary>
    public required CinematicTimeline Timeline { get; init; }

    /// <summary>Registry metadata: priority, cooldown, tags.</summary>
    public ScenarioMetadata? Metadata { get; init; }
}
```

#### CinematicTimeline

```csharp
public sealed class CinematicTimeline
{
    /// <summary>Tracks keyed by participant slot name (or "camera").</summary>
    public required IReadOnlyDictionary<string, Track> Tracks { get; init; }

    /// <summary>Named branch segments triggered by QTE outcomes.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<TrackClip>>>? Segments { get; init; }
}
```

#### AbmlExporter

The critical class. Transforms a `CinematicScenario` into a multi-channel ABML YAML string that the behavior-compiler can compile.

```csharp
public static class AbmlExporter
{
    /// <summary>
    /// Exports a cinematic scenario to multi-channel ABML YAML.
    /// The output is compilable by the behavior-compiler into a BehaviorModel
    /// executable by CinematicInterpreter.
    /// </summary>
    /// <param name="scenario">The scenario to export.</param>
    /// <param name="bindings">Concrete entity bindings for participant slots.</param>
    /// <returns>ABML YAML string.</returns>
    public static string Export(
        CinematicScenario scenario,
        IReadOnlyDictionary<string, Guid> bindings);
}
```

The export logic must:
1. Map each track to an ABML channel named by participant slot
2. Map `TrackClip` instances to ABML `domain_action` nodes
3. Map `SyncBarrier` to ABML `emit_sync` + `wait_for` constructs
4. Map `ContinuationPoint` to ABML continuation point constructs
5. Map `QteAction` + `Branch` to ABML branch nodes with QTE-driven selection
6. Map segments to ABML branch target sequences
7. Handle cross-channel branch correlation (the hard part from Sub-Phase 1A)

### The Authoring Format

What a designer composes (YAML representation of `CinematicScenario`):

```yaml
scenario: enraged_throw_dodge
name: "Enraged Throw and Dodge"
description: "Enraged character throws nearby heavy props at nimble target"

participants:
  - slot: thrower
    requires:
      tags: [MEGA_STRENGTH]
      state: enraged
      props_nearby: {type: heavy_throwable, min: 1}
  - slot: dodger
    requires:
      tags: [nimble]
      min_distance_from: {slot: thrower, distance: 5.0}

triggers:
  - type: state_check
    participant: thrower
    condition: "state == 'enraged'"
  - type: distance_check
    participants: [thrower, dodger]
    min_distance: 5.0
  - type: affordance_check
    participant: thrower
    affordance: heavy_throwable
    min_count: 1

timeline:
  tracks:
    thrower:
      - action: animate
        params: {clip: move_to_nearest_prop, speed: 1.0}
        start: 0.0
        duration: 1.5
      - action: environmental
        params: {interaction: grab_prop, type: heavy_throwable}
        start: 1.5
        duration: 0.8
      - action: animate
        params: {clip: wind_up_throw}
        start: 2.3
        duration: 0.6
      - action: animate
        params: {clip: throw_prop}
        start: 2.9
        duration: 0.5
        emit_sync: throw_release

    dodger:
      - action: animate
        params: {clip: react_alarmed}
        start: 0.0
        duration: 2.0
      - action: qte
        await_sync: throw_release
        qte_type: directional
        options: [dodge_left, dodge_right, dodge_back]
        timeout: 0.4
        default: dodge_left
        branches:
          dodge_left: segment_dodge_left
          dodge_right: segment_dodge_right
          dodge_back: segment_dodge_back

    camera:
      - action: camera
        params: {shot: wide_establishing, targets: [thrower, dodger]}
        start: 0.0
        duration: 1.5
      - action: camera
        params: {shot: close_on_hands, target: thrower}
        start: 1.5
        duration: 1.4
      - action: camera
        await_sync: throw_release
        params: {shot: tracking, target: prop}
        duration: 0.5
        branches:
          dodge_left: segment_camera_dodge_left
          dodge_right: segment_camera_dodge_right
          dodge_back: segment_camera_dodge_back

  segments:
    segment_dodge_left:
      - action: animate
        params: {clip: roll_left}
        duration: 0.4
      - action: animate
        params: {clip: recover_stance}
        duration: 0.3

    segment_dodge_right:
      - action: animate
        params: {clip: roll_right}
        duration: 0.4
      - action: animate
        params: {clip: recover_stance}
        duration: 0.3

    segment_dodge_back:
      - action: animate
        params: {clip: backflip}
        duration: 0.5
      - action: animate
        params: {clip: distance_taunt}
        duration: 0.4

    segment_camera_dodge_left:
      - action: camera
        params: {shot: low_angle_sweep_left}
        duration: 0.7

    segment_camera_dodge_right:
      - action: camera
        params: {shot: low_angle_sweep_right}
        duration: 0.7

    segment_camera_dodge_back:
      - action: camera
        params: {shot: dramatic_zoom_out}
        duration: 0.9

metadata:
  priority: 50
  cooldown_seconds: 120
  tags: [combat, environmental, qte]
  exclusivity_tags: [combat_cinematic]
```

---

## Implementation Steps

### Step 1: Bridge Validation (Sub-Phase 1A)

1. Hand-author the test ABML document described above
2. Compile via behavior-compiler
3. Execute via CinematicInterpreter + CutsceneSession
4. Document results and any gaps
5. Fix any runtime gaps before proceeding

### Step 2: Create SDK Project

1. Create `sdks/cinematic-composer/cinematic-composer.csproj`
2. Set namespace to `BeyondImmersion.Bannou.CinematicComposer`
3. Add project reference to `sdks/scene-composer/` for spatial types

### Step 3: Define Scenario Data Model

1. Define `CinematicScenario`, `ParticipantSlot`, `CapabilityRequirement`
2. Define `TriggerCondition` (distance, state, affordance, environmental)
3. Define `ScenarioMetadata` (priority, cooldown, exclusivity, tags)
4. Follow the pattern established in Phase 0's `storyline-scenario` SDK

### Step 4: Define Timeline Model

1. Define `CinematicTimeline`, `Track`, `TrackClip`
2. Define `SyncBarrier` (emit_sync name + which tracks wait)
3. Define `ContinuationPoint` (timeout, default flow)
4. Define `Branch` (outcome → segment reference)

### Step 5: Define Action Types

1. Define `ActionDefinition` base type with action code + parameters
2. Define `AnimateAction`, `CameraAction`, `EnvironmentalAction`, `QteAction`
3. `QteAction` includes: type (directional, timing, binary, multi-option), options, timeout, default, branch map

### Step 6: Implement TimelineValidator

1. Validate sync points: every `wait_for` references a `emit_sync` that exists
2. Validate timing: no negative durations, no overlapping clips on same track
3. Validate slots: every track references a declared participant slot (or "camera")
4. Validate branches: every branch outcome references an existing segment
5. Validate cross-channel correlation: if a QTE branches in one channel, correlated channels must have matching branches (or no branches, meaning they continue linearly)

### Step 7: Implement AbmlExporter

1. Based on the validated bridge format from Sub-Phase 1A
2. Map scenario structure to ABML YAML
3. Handle cross-channel branch correlation (the solution discovered in Sub-Phase 1A)
4. Produce output that compiles cleanly via behavior-compiler

### Step 8: Write Tests

1. **AbmlExporter integration test**: Define scenario → export → compile via behavior-compiler → verify valid BehaviorModel. This is the critical end-to-end test.
2. TimelineValidator unit tests (valid and invalid scenarios)
3. Data model round-trip tests (construct → serialize → deserialize → verify equal)
4. Edge cases: single-channel scenarios, no QTEs, no sync points, empty branches

### Step 9: Build Verification

1. `dotnet build sdks/cinematic-composer/cinematic-composer.csproj --no-restore` succeeds
2. `dotnet test` for SDK test project passes
3. Full `dotnet build` succeeds

---

## Acceptance Criteria

1. **Bridge validated**: A hand-authored multi-channel ABML document compiles and executes through the full runtime stack (CinematicInterpreter + CutsceneSession + InputWindowManager)
2. **Runtime gaps documented**: Any gaps found in Sub-Phase 1A are documented and fixed
3. `sdks/cinematic-composer/` exists with scenario, timeline, action, validation, and export types
4. AbmlExporter produces ABML that compiles to valid BehaviorModel via behavior-compiler
5. TimelineValidator catches structural errors (missing sync targets, orphan branches, invalid timing)
6. Integration test proves the full pipeline: scenario → export → compile → valid BehaviorModel
7. `dotnet build` succeeds for the full solution

---

## Risks

| Risk | Mitigation |
|------|------------|
| Cross-channel branching not supported by runtime | Sub-Phase 1A discovers this early. Possible solutions: shared branch variable in ABML, sync-point-based correlation, or new ABML construct. |
| AbmlExporter output doesn't compile | Integration test catches this immediately. The export format is derived from the hand-authored bridge document (Sub-Phase 1A), not guessed. |
| Scene-composer dependency too heavy | Cinematic-composer only uses Vector3/Transform. If scene-composer pulls in unwanted transitive dependencies, extract math types into a shared package. |
| Authoring format too rigid for future procedural generation | The data model is designed to represent the output of both hand-authoring and procedural generation. The storyteller SDK will construct `CinematicScenario` objects programmatically. |

---

*This is the Phase 1 implementation plan. For architectural context, see [CINEMATIC-SYSTEM.md](CINEMATIC-SYSTEM.md). For the pattern being followed, see [Phase 0](CINEMATIC-PHASE-0-SCENARIO-SDK.md).*
