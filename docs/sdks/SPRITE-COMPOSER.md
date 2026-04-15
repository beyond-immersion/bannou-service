# Sprite Composer SDK Deep Dive

> **SDK**: sprite-composer (not yet created)
> **Location**: `sdks/sprite-composer/` (planned)
> **Layer**: Composer
> **Domain**: Sprite
> **Dependencies**: sprite-theory
> **Status**: Aspirational — no code exists.
> **Implementation Map**: [docs/sdks/maps/SPRITE-COMPOSER.md](maps/SPRITE-COMPOSER.md)
> **Planning Document**: [docs/planning/SPRITE-COMPOSER-SDK.md](../planning/SPRITE-COMPOSER-SDK.md)
> **Consumers**: sprite-composer-stride (primary bridge), future sprite-composer-godot / sprite-composer-unity bridges, future SpriteBatcher automation tool, defenders-sprite-composer Stride project
> **Short**: Engine-agnostic orchestrator for 3D-to-2D sprite capture — projects, capture sessions, equipment/animation configuration, preview, and atlas export

---

## Overview

sprite-composer is the composer-layer SDK for the sprite domain. It sits between sprite-theory (pure computation) and engine-specific bridges (sprite-composer-stride, etc.), orchestrating the full 3D-to-2D sprite capture workflow: model loading, equipment attachment, animation discovery and configuration, camera rig management, frame capture, atlas assembly, preview playback, and project persistence.

It follows the composer pattern established by scene-composer and voxel-builder: an engine-agnostic core with a command-based undo/redo stack, events for consumer integration, headless-mode support, and a pluggable engine bridge (`ISpriteComposerBridge`). The critical architectural distinction — and the reason sprite-composer is its own SDK rather than a slight variant of an existing composer — is the **bridge direction**. Scene-composer and voxel-builder push data TO the engine for display. Sprite-composer pulls rendered data FROM the engine. Every capture operation is a round-trip: configure the camera, set animation time, render to off-screen target, read pixels back.

**The sprite-composer boundary is "where does the engine stop."** Anything that requires rendering, skeletal animation evaluation, or GPU access lives in the bridge. Anything that can be reasoned about without rendering — project configuration, capture planning, atlas assembly from captured pixel data, metadata export, preview frame selection — lives in sprite-composer itself. The result is an SDK that can fully validate projects, compute capture manifests, and assemble finished atlases offline, with the bridge as a pure "render N frames at these parameters" service.

---

## Consumers

| Consumer | Type | Usage |
|----------|------|-------|
| sprite-composer-stride | Bridge | Implements ISpriteComposerBridge using Stride 4.3 rendering, animation, and content systems (primary engine for Defenders) |
| sprite-composer-godot | Bridge (planned) | Potential future Godot 4.x implementation for studios standardizing on Godot |
| sprite-composer-unity | Bridge (planned) | Potential future Unity implementation |
| Future SpriteBatcher | Tool | Reads SpriteProject files, drives CaptureSession through a headless-or-GPU bridge, exports atlases without UI — the automation layer for production sprite batches |
| defenders-sprite-composer | Stride project | Hosts the interactive sprite composer UI as a Stride game-as-editor application (Phase 4 of the planning document) |
| Content pipelines / CI/CD | Tool | Uses headless mode for project validation, CaptureManifest computation, and JSON metadata verification |

---

## Public API Surface

### Orchestration

| Type | Kind | Purpose |
|------|------|---------|
| `SpriteComposer` | Class | Main entry point. Holds current project, command stack, bridge reference, and event surface. |
| `ISpriteComposerBridge` | Interface | Engine rendering contract. Implemented per-engine (Stride, Godot, Unity). |
| `SpriteComposerOptions` | Record | Configuration: max undo depth, default frame capture timeout, preview refresh rate, per-operation telemetry hooks. |

### Bridge Handles

| Type | Kind | Purpose |
|------|------|---------|
| `IModelHandle` | Interface | Opaque handle to a loaded 3D model. Implemented per-engine (StrideModelHandle wraps a Stride Entity). |
| `IEquipmentHandle` | Interface | Opaque handle to attached equipment. Bridge-specific concrete type. |
| `IAnimationHandle` | Interface | Opaque handle to an available animation clip. Primarily used when the bridge exposes animations as first-class resources. |

### Capture

| Type | Kind | Purpose |
|------|------|---------|
| `CaptureSession` | Class | Orchestrates the full capture workflow: iterates rigs × angles × animations × frames, coordinates with the bridge, assembles output. |
| `CaptureProgress` | Record | Reported during capture: current rig/angle/animation/frame, percent complete, elapsed ms, estimated remaining ms. |
| `CaptureResult` | Record | Complete capture output: list of `FrameCapture` entries (from sprite-theory), atlas byte arrays, SpriteSheet metadata, per-rig manifests. |
| `CaptureError` | Record | Per-frame capture failure: rig, angle, animation, frame index, exception. |
| `CaptureState` | Enum | `Idle`, `Running`, `Paused`, `Completed`, `Cancelled`, `Failed`. |

### Configuration

| Type | Kind | Purpose |
|------|------|---------|
| `AnimationBrowser` | Class | Queries the bridge for available animations, filters by tag/pattern, exposes per-animation configuration. |
| `EquipmentManager` | Class | Tracks current equipment attachments, validates bone compatibility via the bridge, executes attach/detach through the bridge. |
| `AnimationConfigMap` | Class | Per-animation `AnimationConfig` lookup with defaults fallback. |
| `AnimationFilter` | Record | Optional filter: name prefix/suffix/contains, tag list, limit count. |

### Preview

| Type | Kind | Purpose |
|------|------|---------|
| `SpritePreview` | Class | In-memory playback of already-captured frames: current animation, current angle, current frame, timing. |
| `PreviewController` | Class | Play/pause/stop/step, speed multiplier, angle switching, animation switching. |
| `PreviewState` | Enum | `Stopped`, `Playing`, `Paused`. |

### Project

| Type | Kind | Purpose |
|------|------|---------|
| `SpriteProject` | Record | Complete project definition: variant, rigs, per-animation configs, export options, normal map options, metadata. |
| `ProjectSerializer` | Static class | `SpriteProject` ↔ JSON serialization (mirrors sprite-theory's `SpriteSheetSerializer` conventions). |
| `RecentProjects` | Class | MRU project path list with simple JSON persistence (per-user, optional). |

### Export

| Type | Kind | Purpose |
|------|------|---------|
| `ExportPipeline` | Static class | Assembles `CaptureResult` into atlas byte arrays + `SpriteSheet` JSON using sprite-theory primitives. |
| `ExportOptions` | Record | Output directory, atlas filename pattern, normal map filename pattern, which rigs to export, PNG encoder hook. |
| `IAtlasEncoder` | Interface | Consumer-supplied PNG (or other format) encoder. The SDK produces raw RGBA bytes; the encoder converts to the final file bytes. |

### Commands

| Type | Kind | Purpose |
|------|------|---------|
| `IEditorCommand` | Interface | Scene-composer-compatible command contract (Description, Execute, Undo, CanMergeWith, TryMerge). Reused pattern, not reused type — local to sprite-composer to avoid scene-composer dependency. |
| `CommandStack` | Class | Undo/redo stack with merge window, compound commands, events. Same contract as scene-composer's `CommandStack`. |
| `CompoundCommand` | Class | Groups multiple commands into one atomic undo unit. |
| `AddRigCommand` | Class | Add a camera rig to the project. |
| `RemoveRigCommand` | Class | Remove a camera rig. |
| `ModifyRigCommand` | Class | Change rig parameters (frame size, padding, background, normal map / trim flags). |
| `SetAnimationConfigCommand` | Class | Change per-animation configuration (frame count, speed, trim, loop mode). |
| `SelectAnimationCommand` / `DeselectAnimationCommand` | Class | Toggle animation inclusion in `SelectedAnimationNames`. |
| `AttachEquipmentCommand` | Class | Add an equipment slot; calls through to bridge. |
| `DetachEquipmentCommand` | Class | Remove an equipment slot; calls through to bridge. |
| `SetModelCommand` | Class | Change the base character model; unloads the previous model handle via the bridge. |
| `SetScaleCommand` | Class | Change the variant scale. |
| `SetMaterialOverrideCommand` | Class | Add or change a material override entry on the variant. |

### Events

| Type | Kind | Purpose |
|------|------|---------|
| `ComposerEvents` | Class / aggregated | ProjectLoaded, ProjectSaved, DirtyStateChanged, CaptureStarted, CaptureProgress, CaptureCompleted, CaptureError, UndoRedoStateChanged, EquipmentAttached, EquipmentDetached, ModelChanged. |
| `PreviewEvents` | Class / aggregated | PreviewAnimationChanged, PreviewFrameChanged, PreviewAngleChanged, PreviewStateChanged. |

---

## Data Model

### SpriteComposer (Orchestrator)

```
SpriteComposer
├── Project: SpriteProject?             // Current project — null between New/Load
├── Bridge: ISpriteComposerBridge?      // Engine rendering (null in headless mode)
├── ModelHandle: IModelHandle?          // Currently loaded model (null between model operations)
├── EquipmentManager: EquipmentManager  // Attachment tracking
├── AnimationBrowser: AnimationBrowser  // Discovered animations from the bridge
├── Commands: CommandStack              // Undo/redo for project configuration
├── Preview: SpritePreview?             // Non-null only after a capture completes
├── ActiveCaptureSession: CaptureSession?  // Non-null during Running / Paused state
├── IsDirty: bool                       // Any unsaved project changes
├── Options: SpriteComposerOptions
└── (events)                            // ProjectLoaded, CaptureProgress, etc.
```

`SpriteComposer` owns the project, command stack, and bridge reference. It does not own the rendering — that belongs to the bridge. It does not own the animation runtime — also the bridge. It owns the configuration that describes *what* should be rendered and the orchestration that tells the bridge *when*.

**The bridge is mutable at runtime.** A headless-mode composer can have a bridge attached later (for example, loading a project on a server for validation, then attaching a Stride bridge to render a preview frame). The composer tracks bridge presence via null-check; operations that require rendering (capture, preview, equipment attachment) validate the bridge and throw `InvalidOperationException("Bridge required for this operation")` if null.

### ISpriteComposerBridge (Engine Contract)

The bridge contract is the single most important artifact in this SDK. It defines everything the composer delegates to the engine:

```
ISpriteComposerBridge

  // ── Model Management ──
  LoadModelAsync(modelPath: string, ct: CancellationToken) → IModelHandle
      Load a 3D model (FBX, compiled or raw) into the engine scene. Returns an opaque handle.

  DisposeModel(handle: IModelHandle) → void
      Unload and clean up engine resources for a model. Idempotent.

  GetModelBounds(handle: IModelHandle) → BoundingBox
      Axis-aligned bounding box in the model's rest pose, in world units.
      Used by OrthographicSetup.Compute to frame the camera.

  SetModelScale(handle: IModelHandle, scale: float) → void
      Apply a uniform scale to the loaded model. GetModelBounds returns scaled bounds afterward.

  // ── Equipment ──
  AttachEquipmentAsync(model: IModelHandle, slot: EquipmentSlot, ct) → IEquipmentHandle
      Load equipment mesh and parent it to the specified skeleton bone.
      Throws if the bone name doesn't exist on the model's skeleton.

  DetachEquipment(model: IModelHandle, handle: IEquipmentHandle) → void
      Remove attached equipment and release engine resources.

  SetMaterialOverride(model: IModelHandle, materialSlot: string, materialPath: string?) → void
      Override a material on the model or equipment. Null path restores the original.

  // ── Animation ──
  GetAvailableAnimations(model: IModelHandle) → IReadOnlyList<AnimationInfo>
      Enumerate all animation clips available on the model's skeleton.
      Returns sprite-theory's AnimationInfo records (name, duration, frame count, looping flag).

  SetAnimation(model: IModelHandle, animationName: string) → void
      Set the active animation clip. No automatic playback — the animation is now "scrubbable" via SetAnimationTime.

  SetAnimationTime(model: IModelHandle, normalizedTime: float) → void
      Seek the animation to a normalized point (0.0 = clip start, 1.0 = clip end).
      The model's pose updates synchronously. Does NOT advance time from the game loop.

  // ── Camera ──
  ConfigureCamera(parameters: OrthographicParameters, frameSize: (int Width, int Height)) → void
      Position, orient, and configure the capture camera using sprite-theory's computed parameters.

  // ── Frame Capture ──
  CaptureFrameAsync(
      animationName: string,
      angleName: string,
      frameIndex: int,
      normalizedTime: float,
      captureDepth: bool,
      ct: CancellationToken) → FrameCapture
      Render the current scene to an off-screen target and return RGBA pixel data.
      If captureDepth is true, also return normalized 0.0–1.0 depth data.
      The returned FrameCapture carries identity metadata so the composer can reassemble into an atlas.

  // ── Preview (interactive 3D) ──
  SetPreviewCamera(yaw: float, pitch: float, distance: float) → void
      Position an orbit camera around the model for interactive preview.
      Separate from the capture camera — the capture camera positions change per-angle per-frame;
      the preview camera stays put for user-driven orbit viewing.

  SetPreviewAnimationPlayback(playing: bool, normalizedSpeed: float) → void
      Start or stop real-time animation playback in the preview.
      Only used when the user is inspecting the live 3D model, not during capture.

  // ── Lifecycle ──
  Dispose() → void
      Release all engine resources: disposed models, disposed equipment, released render targets, camera reset.
```

**Key contract decisions**:

1. **Opaque handles** (`IModelHandle`, `IEquipmentHandle`, `IAnimationHandle`) are marker interfaces. The bridge defines concrete types (`StrideModelHandle`, etc.) that internally wrap engine entities. The composer never touches engine types.

2. **Normalized time on `SetAnimationTime`.** The composer computes the normalized timestamp from `FrameSequence.Timestamps` (already 0.0–1.0). The bridge maps this to engine-specific time units (Stride: `TimeSpan.FromSeconds(normalizedTime * clip.Duration.TotalSeconds)`).

3. **Camera configuration uses sprite-theory.** The composer calls `OrthographicSetup.Compute(captureAngle, modelBounds, frameSize)` and passes the resulting `OrthographicParameters` to the bridge. The math lives in sprite-theory; the bridge applies it to the engine's camera.

4. **FrameCapture is the sole output of rendering.** Raw RGBA byte array + optional depth float array + dimensions + identity metadata. No engine types cross the bridge boundary. `FrameCapture` is defined in sprite-theory (the composer doesn't redefine it).

5. **Separation of capture camera and preview camera.** During a capture session, the camera position changes per-angle per-frame. During preview, the user orbits freely. The bridge distinguishes these with separate methods (`ConfigureCamera` vs. `SetPreviewCamera`) so neither mode interferes with the other.

### SpriteProject

The complete configuration for a reproducible capture. Serializable, version-controllable, and the input format for the future SpriteBatcher.

```
SpriteProject
├── Name: string                                           // "warrior" (often matches variant name)
├── Variant: CharacterVariant                              // Model path, equipment, materials, scale (from sprite-theory)
├── Rigs: IReadOnlyList<CameraRig>                         // Camera rigs to capture with (from sprite-theory)
├── SelectedAnimationNames: IReadOnlyList<string>          // Explicit capture-list (sorted alphabetically for deterministic order)
├── AnimationConfigs: Dictionary<string, AnimationConfig>  // Per-animation overrides keyed by animation name
├── DefaultAnimationConfig: AnimationConfig                // Defaults for animations without explicit config
├── ExportOptions: ExportOptions                           // Output paths, filenames, which rigs to export
├── NormalMapOptions: NormalMapOptions?                    // Null = use sprite-theory defaults (Strength=1.0, BlurRadius=0)
├── PivotOverride: Vector2?                                // Null = use sprite-theory default (0.5, 0.85)
├── CustomProperties: Dictionary<string, string>?          // Game-specific opaque metadata (propagates to SpriteSheet output)
└── SchemaVersion: string                                  // "1.0" — project file format version
```

**Animation config resolution**: During capture, for each animation X in `SelectedAnimationNames`, the composer resolves `AnimationConfigs[X] ?? DefaultAnimationConfig`. This lets projects set one default (e.g., `FrameCount=8, LoopMode=Loop`) and override only specific animations that need different treatment (e.g., `attack_heavy` with `FrameCount=16` and `LoopMode=None`).

**Why project-level rigs, not global presets**: Each project can define its own rigs (typically by calling `CameraRigPresets.TopDown8Dir()` + `CameraRigPresets.SideViewBrawler()`), but custom rigs are fully supported. A project for a flying enemy might use a top-down-only rig; a wall-mounted trap might use a fixed-angle single-view rig.

### ExportOptions

```
ExportOptions
├── OutputDirectory: string                           // Absolute or relative path for atlas + JSON files
├── AtlasFilenamePattern: string                      // "{variant}_{rig}_{atlas}.png" with placeholders
├── NormalMapFilenamePattern: string                  // "{variant}_{rig}_{atlas}_normal.png"
├── MetadataFilenamePattern: string                   // "{variant}_{rig}.json"
├── RigsToExport: IReadOnlyList<string>?              // Null = export all; non-null = export only named rigs
└── AtlasEncoder: IAtlasEncoder                       // Consumer-supplied PNG/other encoder (see below)
```

**Filename placeholders** resolved at export:
- `{variant}` → `SpriteProject.Name` (kebab-cased)
- `{rig}` → `CameraRig.Name` (kebab-cased)
- `{atlas}` → atlas index (0, 1, 2… for multi-atlas output)
- `{ext}` → `.png` for atlas files, `.json` for metadata

Defaults:
- Atlas: `{variant}_{rig}_{atlas}.png`
- Normal map: `{variant}_{rig}_{atlas}_normal.png`
- Metadata: `{variant}_{rig}.json`

### IAtlasEncoder

sprite-theory produces raw RGBA bytes via `AtlasAssembler.Assemble`. sprite-composer does not depend on a PNG library — encoding is the consumer's responsibility. The composer delegates through `IAtlasEncoder`:

```
IAtlasEncoder
  EncodeRgba(rgba: byte[], width: int, height: int) → byte[]
      Encode raw RGBA pixel data into the final file bytes (typically PNG).
```

Reference implementations live outside sprite-composer:
- **ImageSharp** (Apache 2.0) in a thin adapter — most projects
- **Stride** has built-in PNG writing (the Stride bridge project supplies an encoder)
- **System.Drawing.Common** — Windows-only fallback

Keeping the encoder out of sprite-composer preserves the SDK's minimal dependency profile (sprite-theory only) and lets different engine bridges supply their preferred encoder without cross-pollination.

### CaptureSession

```
CaptureSession
├── Project: SpriteProject                            // What to capture (immutable for the session duration)
├── Bridge: ISpriteComposerBridge                     // Where to render (non-null required)
├── ModelHandle: IModelHandle                         // Currently loaded model (captured at session start)
├── State: CaptureState                               // Idle / Running / Paused / Completed / Cancelled / Failed
├── Progress: CaptureProgress                         // Live state (safely readable from other threads)
├── CapturedFrames: List<FrameCapture>                // Accumulated across all rigs/angles/animations/frames
├── Errors: List<CaptureError>                        // Per-frame errors collected for recovery
└── (events)                                          // Progress, Completed, Error
```

A `CaptureSession` is a one-shot object — each capture creates a new session. Pausing is cooperative: the session checks `IsPauseRequested` between frames and awaits a resume signal. Cancellation is via the standard `CancellationToken` passed to `ExecuteAsync`. After a session reaches `Completed`, `Cancelled`, or `Failed`, its `CapturedFrames` and `Errors` are final — the composer's `Preview` and `ExportPipeline` read from these directly.

**Per-frame error isolation**: If one frame capture throws (GPU readback failure, animation evaluation error), the session records a `CaptureError` entry and continues to the next frame. The atlas assembly phase then assembles only the successfully-captured frames. This mirrors the per-item error isolation pattern from IMPLEMENTATION TENETS for batch processing — one corrupt frame must not abort the whole 960-frame capture.

### CaptureProgress

```
CaptureProgress
├── TotalFrames: int                                  // Expected total from CaptureManifest.Compute
├── CapturedFrames: int                               // Count of successful captures so far
├── FailedFrames: int                                 // Count of per-frame errors recorded
├── CurrentRig: string                                // Current rig name (empty if between rigs)
├── CurrentAnimation: string                          // Current animation name
├── CurrentAngle: string                              // Current angle name
├── CurrentFrameIndex: int                            // Frame within the current animation
├── ElapsedMs: long                                   // Wall-clock time since session started
└── EstimatedRemainingMs: long                        // Linear extrapolation from elapsed / captured
```

Computed fresh each time the session fires `CaptureProgress` (throttled to once per 100ms to avoid UI flooding at ~50 FPS capture rates).

### AnimationBrowser

```
AnimationBrowser
├── AvailableAnimations: IReadOnlyList<AnimationInfo>   // Populated by RefreshAsync via bridge.GetAvailableAnimations
├── Filter: AnimationFilter                              // Optional tag/pattern filter
├── FilteredAnimations: IReadOnlyList<AnimationInfo>     // AvailableAnimations filtered through Filter
└── SelectedAnimationNames: IReadOnlySet<string>         // Which animations the user has chosen to include
```

**Explicit selection**: Not all animations on a Synty model need to be captured. A character might have 40 animations but the project only needs 20 of them. `SelectedAnimationNames` drives which animations `CaptureSession.ExecuteAsync` iterates over. The capture session only captures the selected set; `AnimationConfigMap` provides per-animation config for each selected animation.

### EquipmentManager

```
EquipmentManager
├── Attachments: Dictionary<string, (EquipmentSlot slot, IEquipmentHandle handle)>   // Keyed by slot name
└── (no events at this layer — SpriteComposer fires them after a command applies)
```

**Slot-based uniqueness**: Each slot name (e.g., "weapon_r", "helmet") can hold at most one attachment. Attaching a new mesh to an occupied slot detaches the previous handle first (this is an implementation detail of `AttachEquipmentCommand`, not a separate API).

### SpritePreview

```
SpritePreview
├── SpriteSheet: SpriteSheet                            // From a completed capture (sprite-theory type)
├── Atlases: IReadOnlyList<byte[]>                      // Raw RGBA atlas pixel data
├── CurrentAnimation: string                            // Active animation name
├── CurrentAngle: string                                // Active angle name
├── CurrentFrameIndex: int                              // Frame within the animation
├── PlaybackSpeed: float                                // Multiplier (1.0 = real-time, 0.5 = half-speed, 2.0 = 2×)
├── State: PreviewState                                 // Stopped / Playing / Paused
└── (events)                                            // AnimationChanged, FrameChanged, AngleChanged, StateChanged
```

Preview is a pure consumer of capture output — it reads from `SpriteSheet` and `Atlases` (both immutable once capture completes) and drives the frame advance via an internal timer or explicit `Step()` calls. The bridge is NOT involved in preview playback; preview renders directly from atlas pixels using the consuming UI's own image-display mechanism. `PreviewController` wraps `SpritePreview` with transport controls.

---

## Computation Pipeline

### Project Load / New

```
SpriteComposer.NewProject(variant, rigs)
  → Create empty SpriteProject with defaults
  → Clear CommandStack
  → Clear preview, equipment manager, animation browser
  → Mark IsDirty = false
  → Fire ProjectLoaded event

SpriteComposer.LoadProjectAsync(path)
  → ProjectSerializer.Deserialize(File.ReadAllText(path)) → SpriteProject
  → Same clearing + fire ProjectLoaded

SpriteComposer.SaveProjectAsync(path)
  → ProjectSerializer.Serialize(Project) → string
  → File.WriteAllTextAsync(path, json, ct)
  → Mark IsDirty = false
  → Fire ProjectSaved
```

### Model Load Flow

```
SpriteComposer.LoadModelAsync(modelPath)
  → Require bridge
  → If ModelHandle exists: bridge.DisposeModel(ModelHandle) + detach all equipment
  → ModelHandle ← await bridge.LoadModelAsync(modelPath)
  → Refresh AnimationBrowser: animations ← bridge.GetAvailableAnimations(ModelHandle)
  → Variant.ModelPath ← modelPath (via SetModelCommand if inside interactive flow)
  → Fire ModelChanged
```

### Equipment Attach Flow

```
SpriteComposer.AttachEquipment(slotName, meshPath, boneName)
  → Build EquipmentSlot record (sprite-theory type)
  → Execute new AttachEquipmentCommand(this, slot):
      If slot already occupied: detach previous first (as a compound sub-command)
      handle ← await bridge.AttachEquipmentAsync(ModelHandle, slot)
      EquipmentManager.Attachments[slotName] = (slot, handle)
      Variant.Equipment ← Variant.Equipment with slot added
      Fire EquipmentAttached
  → Push to CommandStack
```

### Capture Session Pipeline

```
CaptureSession.ExecuteAsync(progress: IProgress<CaptureProgress>, ct)
  // Precomputation
  animationsWithConfigs ← FOREACH name IN Project.SelectedAnimationNames:
      (AnimationBrowser.LookupAnimationInfo(name), Project.AnimationConfigs[name] ?? DefaultAnimationConfig)
  manifest ← CaptureManifest.Compute(variant, rigs, animationsWithConfigs)   // sprite-theory
  State ← Running
  Fire CaptureStarted(manifest)

  // Main iteration — nested loops over rigs × angles × animations × frames
  FOREACH rig IN Project.Rigs (filtered by ExportOptions.RigsToExport if set)
    modelBounds ← Bridge.GetModelBounds(ModelHandle)

    FOREACH angle IN rig.Angles                             // Every angle IS captured (no mirror targets)
      orthoParams ← OrthographicSetup.Compute(angle, modelBounds, rig.FrameSize)   // sprite-theory
      Bridge.ConfigureCamera(orthoParams, rig.FrameSize)

      FOREACH selectedAnimation IN Project.SelectedAnimationNames (sorted, deterministic)
        animInfo ← AnimationBrowser.LookupAnimationInfo(selectedAnimation)
        animConfig ← Project.AnimationConfigs.GetValueOrDefault(
                       selectedAnimation, Project.DefaultAnimationConfig)
        sequence ← AnimationSampling.GenerateFromConfig(animInfo, animConfig)   // sprite-theory
        Bridge.SetAnimation(ModelHandle, selectedAnimation)

        FOREACH (timestamp, frameIndex) IN sequence.Timestamps (enumerated):
          CheckPausedOrCancelled(ct)
          Bridge.SetAnimationTime(ModelHandle, timestamp)
          Try:
            capture ← await Bridge.CaptureFrameAsync(
                selectedAnimation, angle.Name, frameIndex, timestamp,
                rig.IncludeNormalMap, ct)
            CapturedFrames.Add(capture)
            Progress.CapturedFrames++
          Catch (Exception ex):
            Errors.Add(new CaptureError(ex, rig.Name, angle.Name, selectedAnimation, frameIndex))
            Progress.FailedFrames++
          ReportProgress (throttled to 100ms)

  // Assembly phase — for each rig, pack + mirror + serialize
  perRigOutputs ← new List<RigCaptureOutput>()
  FOREACH rig IN Project.Rigs
    rigFrames ← CapturedFrames.Where(c => c was captured under this rig)
    packInputs ← rigFrames.Select((c, i) => (c.Width, c.Height, i))
    atlasLayout ← AtlasPacker.Pack(packInputs, buildAtlasOptions(rig))      // sprite-theory
    mirrors ← MirrorOptimizer.ComputeMirrors(rig)                           // sprite-theory
    capturedSpriteFrames ← BuildSpriteFrames(rigFrames, atlasLayout, project.PivotOverride)
    mirrorSpriteFrames ← MirrorOptimizer.GenerateMirrorFrames(capturedSpriteFrames, mirrors)
    atlasImages ← AtlasAssembler.Assemble(rigFrames, atlasLayout, rig.BackgroundColor)
    atlasInfos ← BuildAtlasInfos(rig, atlasLayout, Project.ExportOptions)
    spriteAnimations ← BuildSpriteAnimations(capturedSpriteFrames + mirrorSpriteFrames)
    spriteSheet ← new SpriteSheet(
        Version: "1.0",
        Generator: "BeyondImmersion.Bannou.SpriteComposer",
        GeneratedAt: UtcNow,
        Variant: Project.Variant,
        Rig: rig,
        Atlases: atlasInfos,
        Animations: spriteAnimations,
        Frames: capturedSpriteFrames + mirrorSpriteFrames,
        CustomProperties: Project.CustomProperties)

    // Optional normal map atlases (share layout with color atlases)
    normalAtlases ← null
    IF rig.IncludeNormalMap:
      normalAtlases ← BuildNormalAtlases(rigFrames, atlasLayout, Project.NormalMapOptions)

    perRigOutputs.Add(RigCaptureOutput(rig, atlasImages, normalAtlases, spriteSheet))

  Result ← CaptureResult(CapturedFrames, perRigOutputs, Errors)
  State ← Completed (or Failed if Errors.Count exceeds threshold)
  Fire CaptureCompleted(Result)
```

**Frame count estimation** (same formula as sprite-theory's `CaptureManifest.Compute`):

```
totalCaptured = Σ (rig.Angles.Count × animation.FrameCount) for each rig × selectedAnimation
totalMirror   = Σ (rig.Angles.Count(a => a.ProducesMirror) × animation.FrameCount) for each rig × selectedAnimation
```

For a Defenders character (TopDown8Dir + SideViewBrawler, 20 selected animations at 8 frames each):
- TopDown8Dir: 5 angles × 20 × 8 = 800 captured + 3 mirrors × 20 × 8 = 480 mirror
- SideViewBrawler: 1 angle × 20 × 8 = 160 captured + 1 mirror × 20 × 8 = 160 mirror
- **Total**: 960 captured + 640 mirror = 1,600 total frames. At ~50 ms/frame: ~48 s per variant.

### Export Flow

```
ExportPipeline.ExportAsync(captureResult, project, ct)
  FOREACH rigOutput in captureResult.PerRigOutputs:
    IF project.ExportOptions.RigsToExport is not null AND rigOutput.Rig.Name not in RigsToExport:
      CONTINUE

    FOREACH (atlasIndex, atlasBytes) in rigOutput.AtlasImages:
      filename ← ResolveFilename(ExportOptions.AtlasFilenamePattern, project.Name, rigOutput.Rig.Name, atlasIndex)
      encoded ← project.ExportOptions.AtlasEncoder.EncodeRgba(atlasBytes, atlasWidth, atlasHeight)
      File.WriteAllBytesAsync(Path.Combine(ExportOptions.OutputDirectory, filename), encoded, ct)

    IF rigOutput.NormalAtlases is not null:
      FOREACH (atlasIndex, normalBytes) in rigOutput.NormalAtlases:
        filename ← ResolveFilename(ExportOptions.NormalMapFilenamePattern, ...)
        encoded ← AtlasEncoder.EncodeRgba(...)
        File.WriteAllBytesAsync(...)

    metadataJson ← SpriteSheetSerializer.Serialize(rigOutput.SpriteSheet)       // sprite-theory
    metadataFilename ← ResolveFilename(ExportOptions.MetadataFilenamePattern, project.Name, rigOutput.Rig.Name, 0)
    File.WriteAllTextAsync(Path.Combine(ExportOptions.OutputDirectory, metadataFilename), metadataJson, ct)

  Fire ExportCompleted
```

### Undo/Redo (Configuration Only)

```
User modifies project → SpriteComposer routes through CommandStack:
  command.Execute() (applies change, marks IsDirty, fires relevant event)
  CommandStack.Execute(command) pushes to undo stack, fires StateChanged

Undo → CommandStack pops undo stack → command.Undo() → pushes to redo stack
Redo → reverse
```

Undo scope is **exclusively project configuration**: rigs, animation configs, animation selection, equipment attachments, model path, scale, material overrides. Captures are NOT undoable — they are one-way (recapture if needed). Preview state is NOT undoable — it is transient playback state.

**Command merging** uses scene-composer's 500ms merge window semantics: consecutive commands of the same type on the same target within 500ms merge into one undo entry. This matters for slider-driven parameter tweaks (a user scrubbing a frame-count slider emits many `SetAnimationConfigCommand` events; merging produces a single undo entry with the final value rather than a hundred).

---

## Engine Bridge Pattern

### The Pull Direction

Scene-composer and voxel-builder **push data to the engine**. When the user adds a node, scene-composer calls `bridge.CreateEntity(nodeId, nodeType, transform, asset)` — the engine then owns that visual state. When the user places a voxel, voxel-builder calls `bridge.OnChunksModified(coords)` — the engine re-meshes the affected chunks.

Sprite-composer **pulls data from the engine**. The composer never displays anything itself. It tells the bridge "load this model," "attach this equipment," "set animation to idle at time 0.375," then requests "capture what you are currently rendering." The rendered result comes back as raw pixel data, which the composer then assembles into atlases — its only "output" artifacts are byte arrays and JSON, not engine state.

This direction inversion has one major consequence: the bridge must support **render-to-texture with CPU readback**. Scene-composer's bridge can render interactively to the window. Voxel-builder's bridge can render the edited chunks to the visible viewport. Sprite-composer's bridge must be able to render to an off-screen target AND copy those pixels back to CPU memory for every frame captured. In Stride, that is a `Texture.New2D(... TextureFlags.RenderTarget | TextureFlags.ShaderResource)` plus a `GraphicsResourceUsage.Staging` texture for the readback.

### Bridge Lifecycle

| Phase | Composer Action | Bridge Expected Behavior |
|-------|-----------------|--------------------------|
| Attach | `composer.SetBridge(bridge)` | Bridge is typically already instantiated with engine context (scene, graphics device). Composer just wires the reference. |
| Model load | `composer.LoadModelAsync(path)` | Bridge loads FBX/compiled asset into scene. Returns `IModelHandle`. Instantiates `AnimationComponent` and skeleton. |
| Animation enumeration | After load, composer refreshes AnimationBrowser | Bridge returns sprite-theory's `AnimationInfo` records from the model's skeleton / animation container. |
| Equipment ops | User attaches equipment | Bridge instantiates child entity parented via bone link (Stride: `ModelNodeLinkComponent`). |
| Capture configure | Per angle per frame | Bridge accepts `OrthographicParameters`, applies to camera transform, sets ortho projection matrix. |
| Capture frame | Per frame | Bridge sets animation time, forces animation evaluation, renders to off-screen target, reads back RGBA + optional depth, returns `FrameCapture`. |
| Preview | User interacts with 3D preview | Bridge switches to preview camera, enables animation playback, renders to interactive viewport. |
| Detach / Dispose | Composer shutdown or project close | Bridge disposes all models, equipment, render targets, staging textures, releases GPU resources. |

### What's Common vs. Engine-Specific

| Concern | Composer (engine-agnostic) | Bridge (engine-specific) |
|---------|---------------------------|--------------------------|
| Model path resolution | Passes path string verbatim | Interprets path — compiled asset, raw FBX, etc. |
| Skeleton structure | Never inspects | Loads skeleton, exposes bone names |
| Animation selection | Tracks selected animation names | Owns animation system, keyframe evaluation, pose blending |
| Frame timing math | `AnimationSampling.GenerateFromConfig` (sprite-theory) | Converts normalized time to engine time units |
| Camera parameters | `OrthographicSetup.Compute` (sprite-theory) | Maps `OrthographicParameters` to engine camera |
| Rendering | Never renders directly | Owns render pipeline, off-screen target, scene renderer |
| Pixel readback | Consumes `FrameCapture` | Implements GPU→CPU readback, normalizes depth to 0–1 |
| Atlas assembly | `AtlasAssembler.Assemble` (sprite-theory) | Not involved |
| PNG encoding | Delegates to `IAtlasEncoder` | May supply the encoder implementation |

---

## Headless Mode

When `Bridge` is null, the composer operates **headlessly**:

- **Works without a bridge**: Project management (new/load/save), command stack (undo/redo on configuration), `CaptureManifest.Compute` (sprite-theory needs only rig info and animation configs — no bridge required to predict frame counts).
- **Throws without a bridge**: `LoadModelAsync`, `AttachEquipmentAsync`, `CaptureSession.ExecuteAsync`, `SpritePreview` creation — anything requiring rendering or animation evaluation.

**Why headless matters**:

1. **Batch processing (future SpriteBatcher)**: Read project YAML/JSON → modify configurations programmatically (e.g., "change all projects' TopDown8Dir pitch from -55° to -60°") → export metadata → the bridge is only needed for actual rendering. Validation can happen in CI without engine dependencies.

2. **CI/CD integration**: Project file linting — does every rig reference a valid preset? Are all configured animations present in `SelectedAnimationNames`? Are export filename patterns valid? Do expected frame counts match a manifest file?

3. **Testing**: Unit tests for all composer logic (command stack, project serialization, equipment/animation management) can run without any engine dependency. Tests become simple `Assert` flows with a mock bridge (or no bridge at all, for configuration-only tests).

The bridge-present-vs-absent check is a simple null-guard at each bridge-dependent method; there is no separate "headless mode" class or parameter. The composer's operations partition cleanly into "needs bridge" and "doesn't."

---

## Determinism Contract

**Project configuration**: Serialization is deterministic — same `SpriteProject` → identical JSON via `ProjectSerializer.Serialize`. Same JSON → structurally equivalent `SpriteProject` via `Deserialize`. Round-trip is stable.

**Capture orchestration**: The capture iteration order is deterministic — rigs in declared order, angles in declared order, animations in alphabetical order from `SelectedAnimationNames`, frame timestamps from `AnimationSampling.GenerateFromConfig`. Given the same bridge state (model, equipment, animation data), two capture sessions produce the same sequence of `FrameCapture` calls in the same order.

**Capture output**: Not deterministic at the SDK level — the bridge's rendered pixel data depends on the engine's exact graphics configuration, shader versions, and GPU precision. Two Stride builds with different driver versions may produce bitwise-different pixel data even for identical camera/animation inputs. sprite-theory's deterministic layout and assembly still hold — given the same captured pixel data, the same atlas comes out.

**Atlas assembly**: Deterministic (via sprite-theory's `AtlasPacker.Pack` deterministic sort and `AtlasAssembler.Assemble` placement).

**JSON metadata**: Deterministic (via sprite-theory's `SpriteSheetSerializer` and property ordering).

The practical determinism statement: "Same project file + same bridge + same engine version → same atlases and same metadata." The bridge is a variable outside the SDK's control.

---

## Performance Targets

| Operation | Target | Context | Notes |
|-----------|--------|---------|-------|
| `NewProject` / `LoadProject` / `SaveProject` | < 50 ms | Editor | Non-capture project ops; JSON parse for a 1 MB project file |
| `CaptureSession` setup (first frame latency) | < 500 ms | Editor | Bridge.GetModelBounds + first ConfigureCamera + first SetAnimation + first CaptureFrameAsync |
| Per-frame capture (excluding bridge time) | < 2 ms | Editor | Composer's overhead: progress reporting, error bookkeeping, `FrameCapture` list append |
| Per-frame capture (including bridge — Stride) | 40–70 ms | Editor | Render + GPU readback; dominated by bridge |
| Full character variant (960 captured frames, Stride) | 40–70 s | Editor | 1,600 total frames with mirror optimization; ~60 ms × 960 |
| Atlas assembly (960 frames into 4096² atlas) | < 100 ms | Editor | sprite-theory's AtlasAssembler + AtlasPacker; row-by-row `Buffer.BlockCopy` |
| JSON metadata serialization (full SpriteSheet) | < 5 ms | Editor | System.Text.Json via sprite-theory's SpriteSheetSerializer |
| Project file save | < 50 ms | Editor | JSON serialize + disk write for typical project (~10–50 KB) |
| Undo / Redo of a project command | < 1 ms | Editor | In-memory state mutation, no bridge calls |
| Preview frame step | < 5 ms | Editor | Timer-driven frame index advance + event fire |

**Pipeline opportunity (future)**: The current model is sequential (capture frame N, read back, capture frame N+1). A future optimization could pipeline reads: start rendering frame N+1 while the staging texture for frame N is being read. This halves the effective per-frame cost but doubles GPU memory pressure (two render targets live simultaneously). Left as a design consideration — initial implementation should prioritize correctness and simple backpressure over throughput.

---

## Open Questions

### 1. Stride animation time-setting evaluation

The core technical risk: can Stride's `AnimationProcessor` / `AnimationUpdater` be forced to evaluate a single frame at a specific time without the game loop advancing? Stride's animation system normally ticks during `Game.Update`. For sprite capture we need deterministic, game-loop-independent pose evaluation.

**Options identified in the planning doc**:
- Manually invoke `AnimationProcessor.Update` after setting `PlayingAnimation.CurrentTime`.
- Run a single simulation step per frame.
- Skip Stride's animation processor entirely and directly compute skeleton poses from `AnimationClip` keyframes.

**Why it affects the composer**: The `ISpriteComposerBridge.SetAnimationTime(handle, normalizedTime)` contract guarantees that after the call returns, the model's pose reflects that exact normalized time. The bridge needs a way to guarantee this in Stride. If none of the options works, the bridge may need to briefly run the game loop per frame, which changes the threading model and makes batch capture much slower.

**Resolution**: This is a bridge-side concern that doesn't affect the composer's contract. The composer simply demands the guarantee; the bridge is responsible for fulfilling it. However, if no solution exists, it forces either a bridge-specific workaround (documented in sprite-composer-stride) or a composer-level accommodation (e.g., the composer could await multiple small delays between `SetAnimationTime` and `CaptureFrameAsync` to let the animation system settle — ugly, but a fallback).

### 2. Pivot auto-detection

Sprite-theory's default pivot is `(0.5, 0.85)` — center-x, 85%-from-top — chosen for standing humanoids. For non-humanoid subjects, this is wrong (flying enemies have no "feet"; bosses may have different proportions; quadrupeds stand lower).

**Options**:
- **Per-variant override in `SpriteProject`** (current design): `PivotOverride: Vector2?` on the project. Explicit but requires user action. Default `null` = use sprite-theory's `(0.5, 0.85)`.
- **Auto-compute from bounding box at capture time**: When `rig.AutoPivot = true`, the composer projects the model's bottom-most point onto the frame plane to derive a pivot. Works for most cases; fails for flying subjects (their "bottom" isn't meaningful).
- **Per-rig override**: Some rigs might want foot-anchoring (TopDown8Dir), others might want center-anchoring (top-down-flat for flying units). Add `DefaultPivot: Vector2` to `CameraRig`. Means extending sprite-theory, not just sprite-composer.

**Recommendation**: Start with per-variant override on `SpriteProject` as documented. Auto-computation is a future enhancement once the project format has at least one explicit-override data point to validate against.

### 3. Compound commands for continuous inputs

Slider-driven parameter tweaks (frame count, trim start, trim end, speed multiplier) emit many `SetAnimationConfigCommand` events per second. Scene-composer solves this with 500ms merge windows on commands of the same type + same target. sprite-composer should reuse this pattern. But some inputs are NOT sliders — setting a camera rig's name via a text input should still produce one undo entry per commit, not per keystroke.

**Decision**: Commands are auto-merging (`CanMergeWith` = true) by default for parameter commands (`ModifyRigCommand`, `SetAnimationConfigCommand`, `SetScaleCommand`), non-merging for structural commands (`AddRigCommand`, `RemoveRigCommand`, `AttachEquipmentCommand`, `DetachEquipmentCommand`, `SetModelCommand`). The UI layer can call `CommandStack.BreakMerge()` to force a new undo entry at explicit commit points (form submission, focus loss).

### 4. Project file format — single file vs. manifest directory

**Option A (single file)**: `warrior.spriteproj.json` contains everything (variant, rigs, configs, export options). Easy to version-control, email, git-diff.

**Option B (manifest directory)**: `warrior/project.json` + `warrior/rigs/topdown8.json` + `warrior/animations/idle.json` etc. Cleaner when editing individual rigs manually, but awkward to move/share.

**Recommendation**: Single file. Projects are rarely large enough to warrant the directory split (a typical project is 10–50 KB of JSON), and the single-file model matches sprite-theory's `SpriteSheet` serialization convention.

### 5. Cancellation behavior during capture

When the user cancels a running capture partway through:

- **Option A**: Discard all captured frames. The next capture starts fresh.
- **Option B**: Keep the completed rigs/animations, resume from where we stopped. Requires tracking "progress bookmarks" in `CaptureSession`.
- **Option C**: Keep the completed frames in memory (available via `CaptureResult`) but don't auto-resume. User decides to keep or discard.

**Recommendation**: Option C. The user gets useful work out of a partial capture (maybe the top-down rig finished and only the side-view was pending; they can re-run just SideView) and the session data is inspectable.

### 6. Runtime animation-clip registration

Some engines expose animation clips as first-class assets that need explicit registration on the `AnimationComponent`. Stride's `AnimationComponent.Animations["idle"] = clip` requires the clip to be loaded first. The bridge might want to pre-register all clips for the selected animations up-front (faster per-frame setup) or lazy-register (less memory, slower first-use per animation).

**Resolution**: Bridge's internal choice. The composer's contract (`SetAnimation(name)` and `SetAnimationTime(time)`) is indifferent. Document the expected behavior in the sprite-composer-stride deep dive.

---

## Known Quirks & Caveats

### Intentional Quirks (Documented Behavior)

- **The bridge is assumed single-threaded.** Capture sessions call bridge methods sequentially from a single thread. The bridge can internally use GPU async (Stride's `CommandList` is async) but must expose a synchronous-per-frame interface. If a bridge implementation needs to hop to the engine's main thread, it is the bridge's responsibility (Stride bridges typically marshal to the game loop thread).

- **Capture is uninterruptible between `SetAnimationTime` and `CaptureFrameAsync`.** Once the animation is scrubbed to a specific time, the composer assumes the next `CaptureFrameAsync` returns a frame at that exact pose. Pause/cancel is checked BEFORE `SetAnimationTime`, not during. This guarantees that if capture happens at all, it captures the right pose.

- **Undo does not affect captured frames.** Undoing a configuration change that happened DURING a capture (possible but unusual) does not alter the capture result. Captures reflect the project state at the moment of each frame's capture, not the project's current state. If the user wants to "undo a capture," they re-run the capture with the desired configuration.

- **Model disposal on `SetModelCommand`.** Changing the base model via `SetModelCommand` eagerly disposes the previous `IModelHandle` via the bridge. Any attached equipment is also detached (the new model's skeleton is different; attachment points are no longer valid). The command's `Undo` re-loads the previous model and re-attaches the previous equipment — which requires re-calling the bridge to reload. This makes `SetModelCommand.Undo` potentially slow (re-loads a model). Documented, not fixed.

- **`ExportOptions.AtlasEncoder` is mandatory.** The SDK produces raw RGBA bytes and refuses to write atlas files without a consumer-supplied encoder. The composer does not default to `System.Drawing.Common` or `ImageSharp` — that is a deliberate dependency discipline choice. Consumers must provide an encoder (most bridge packages bundle one).

- **`SelectedAnimationNames` iteration is alphabetical.** The capture order iterates through the selection deterministically (sorted alphabetically by name). Projects that need a specific capture order for some reason (unlikely) would need a separate `CaptureAnimationOrder` field — intentionally not included to keep the project format simple.

- **Preview plays from atlas pixels, not from a fresh bridge capture.** Preview is pure data consumption. If the user wants to see the 3D model interactively (orbiting, full resolution), that's the "Preview Camera" mode via `bridge.SetPreviewCamera` and `bridge.SetPreviewAnimationPlayback` — separate from the sprite preview, which plays captured frames.

- **`SetModelCommand.Undo` may produce a different `IModelHandle` than the original.** Disposing a handle releases GPU resources; re-loading the same asset produces a fresh handle with a different identity. Consumers that captured the pre-undo handle externally will find it invalid after undo. The composer handles this internally by refreshing `ModelHandle`, but external code (e.g., a UI that cached a thumbnail against the handle) must subscribe to `ModelChanged` events to refresh.

### Design Considerations (Requires Planning)

- **GPU memory for in-flight captures**: At 960 frames × (128² × 4 bytes) = ~63 MB held in `CapturedFrames` during a full capture. Not catastrophic, but worth tracking. If memory pressure becomes an issue, a future optimization streams frames directly to disk-backed atlases as they're captured, discarding them from memory. Trade-off: atlas packing requires all frame sizes known up-front, so streaming requires either fixed frame sizes (no trim) or a two-pass capture (first pass: capture all, measure bounds; second pass: pack and write). The current design assumes everything fits in memory, which is safe for typical character variants.

- **Animation clip discovery reliability**: Synty packs ship animations as separate FBX files with naming conventions like `Warrior@Idle.fbx`, `Warrior@Run.fbx`. Stride's Game Studio imports these as separate `AnimationClip` assets. The bridge's `GetAvailableAnimations` must enumerate all clips associated with a skeleton. If the bridge can't discover clips that aren't explicitly registered on the model's `AnimationComponent`, the user has to manually list animations in the project — which defeats the "browse available animations" UX.

- **Equipment visual consistency**: Equipment meshes must use the same skeleton as the base character (or a compatible subset). Synty ensures this within their packs but custom equipment may not. The bridge's `AttachEquipmentAsync` should validate bone name compatibility and report clear errors. A frustrating silent failure mode: equipment attaches but follows bones at the wrong positions because the bone names match but the rig topology doesn't. Runtime detection is probably impossible; this is a content-authoring discipline issue.

- **Character shadow capture**: A drop shadow significantly improves sprite readability at 55° top-down angles. Options: (a) render a shadow pass with a directional light and a ground plane, (b) render a silhouette at reduced opacity as a second layer, (c) let the game engine add shadows at render time (no capture needed). The planning document suggested `CameraRig.IncludeShadow` as a future flag. Deferred until the base capture pipeline is working — when added, produces a shadow atlas parallel to the normal map atlas.

- **Multi-atlas export naming**: With `{atlas}` in the filename pattern, multi-atlas outputs get `warrior_topdown_0.png`, `warrior_topdown_1.png`, etc. The game runtime needs to know how to find all atlases for a given sprite sheet. The `SpriteSheet.Atlases` metadata already carries the filenames, so runtime iterates that list. Works, but the consumer has to honor the metadata — if the consumer assumes a single atlas, they silently miss frames. The deep dive on the consuming side (game runtime) must document this.

- **Per-frame telemetry granularity**: `CaptureProgress` events are throttled to 100ms. On a fast GPU that captures at ~30ms/frame, multiple frame completions collapse into a single progress event. UI responsiveness is fine, but per-frame timing analysis for performance tuning requires a separate `CapturedFrame` event (unthrottled) that fires per frame. Not in the initial API surface; candidate for a debug/telemetry extension if capture performance analysis becomes important.

---

## Relationship to Scene-Composer

sprite-composer's command stack pattern is a deliberate adaptation of scene-composer's. The `IEditorCommand` interface, `CommandStack` with merge windows, and `CompoundCommand` pattern are nearly identical. The types are NOT reused across SDK boundaries (sprite-composer does not reference scene-composer), but the contracts and semantics match so developers moving between SDKs have no cognitive overhead.

Where the patterns diverge:
- **Scene-composer** has entity-level commands (`AddNodeCommand`, `MoveNodeCommand`, `DeleteNodeCommand`) operating on a hierarchical scene graph.
- **Sprite-composer** has project-level commands operating on a flat configuration record (rigs list, equipment list, animation config map).

The `CommandStack` implementation can be copied nearly verbatim from scene-composer's — merge-window logic, compound-command scope disposal, state-changed events — with only the command type names adapted. A future refactor could extract a shared `BeyondImmersion.Bannou.EditorCommands` SDK; not pursued now to keep each composer self-contained.

---

## Work Tracking

No work items yet — SDK is pre-implementation. This deep dive and its companion implementation map constitute the design specification.
