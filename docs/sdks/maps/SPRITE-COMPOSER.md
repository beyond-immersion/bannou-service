# Sprite Composer SDK Implementation Map

> **SDK**: sprite-composer
> **Location**: `sdks/sprite-composer/`
> **Layer**: Composer
> **Domain**: Sprite
> **Deep Dive**: [docs/sdks/SPRITE-COMPOSER.md](../SPRITE-COMPOSER.md)
> **Status**: Aspirational — pseudo-code represents intended behavior, not verified implementation

---

| Field | Value |
|-------|-------|
| SDK | sprite-composer |
| Layer | Composer |
| Public Types | ~46 (9 classes, 4 records, 4 interfaces, 3 enums, 2 static classes, 16 command classes, ~14 event args / records) |
| Public Methods | ~80 |
| Dependencies | `sprite-theory` (transitively: `BeyondImmersion.Bannou.Core` for `Vector3`) |
| Deterministic | Yes for orchestration and assembly; bridge-dependent for pixel output |
| Allocation-Free Hot Paths | None (captures allocate `FrameCapture`, events allocate args) |

---

## Data Structures

### SpriteComposer (Orchestrator)

**Kind**: Class
**Thread Safety**: Single-threaded (UI thread); capture progress events must be marshaled to UI thread by consumer

| Field | Type | Purpose |
|-------|------|---------|
| `Project` | `SpriteProject?` | Current project. Null between NewProject/LoadProject and after project close. |
| `Bridge` | `ISpriteComposerBridge?` | Engine bridge. Null in headless mode. |
| `ActiveVariantName` | `string?` | Name of the currently-previewed variant. The editor tracks one "focused" variant whose model is loaded for 3D preview. Null when no variant is active. |
| `ModelHandle` | `IModelHandle?` | Model for `ActiveVariantName`. Null between variant switches. |
| `EquipmentManager` | `EquipmentManager` | Attachment tracking scoped to the active variant. |
| `AnimationBrowser` | `AnimationBrowser` | Available/filtered/selected animations for the active variant's model. |
| `Commands` | `CommandStack` | Undo/redo stack for project configuration |
| `Preview` | `SpritePreview?` | Non-null only after a capture completes |
| `ActiveCaptureSession` | `CaptureSession?` | Non-null during Running / Paused state |
| `IsDirty` | `bool` | Any unsaved project changes |
| `Options` | `SpriteComposerOptions` | Configuration |

**Events**:

| Event | Args | When |
|-------|------|------|
| `ProjectLoaded` | `ProjectEventArgs` | After New/Load succeeds |
| `ProjectSaved` | `ProjectEventArgs` | After Save succeeds |
| `DirtyStateChanged` | `DirtyStateEventArgs` | When IsDirty flips |
| `ActiveVariantChanged` | `ActiveVariantEventArgs` | After `SetActiveVariantAsync` completes |
| `VariantAdded` / `VariantRemoved` | `VariantEventArgs` | After AddVariantCommand / RemoveVariantCommand executes or undoes |
| `AnimationSetAdded` / `AnimationSetRemoved` | `AnimationSetEventArgs` | After AddAnimationSetCommand / RemoveAnimationSetCommand executes or undoes |
| `EquipmentAttached` / `EquipmentDetached` | `EquipmentEventArgs` | After AttachEquipmentCommand / DetachEquipmentCommand executes (includes `variantName`) |
| `ModelChanged` | `ModelChangedEventArgs` | After SetVariantModelCommand executes or undoes for the active variant |
| `CaptureStarted` | `CaptureStartedEventArgs` | CaptureSession transitions Idle → Running |
| `CaptureProgress` | `CaptureProgressEventArgs` | Throttled to 100ms during Running |
| `CaptureCompleted` | `CaptureCompletedEventArgs` | CaptureSession transitions to Completed |
| `ExportCompleted` | `ExportCompletedEventArgs` | Fired after each `(variantName, rigName)` atlas + JSON pair is written |
| `AllExportsCompleted` | `EventArgs` | Fired after every group in the `CaptureResult` has been exported |
| `CaptureError` | `CaptureErrorEventArgs` | Per-frame error recorded |
| `UndoRedoStateChanged` | `EventArgs` | Command stack changes |

### SpriteComposerOptions

**Kind**: Record

| Field | Type | Default | Purpose |
|-------|------|---------|---------|
| `MaxUndoDepth` | `int` | `100` | Command stack capacity |
| `CaptureProgressIntervalMs` | `int` | `100` | Throttle window for CaptureProgress events |
| `DefaultFrameTimeoutMs` | `int` | `5000` | Per-frame capture timeout (cancellation fires if bridge hangs) |
| `PreviewDefaultFps` | `float` | `12` | Default playback rate for preview (frames per second) |

### ISpriteComposerBridge

**Kind**: Interface (implements `IAsyncDisposable`)
**Thread Safety**: Implementation-defined (composer calls on a single thread; bridge may internally async-dispatch)

**Capability flags**:

| Member | Type | Purpose |
|--------|------|---------|
| `SupportsDepthCapture` | `bool` | True → bridge can populate `FrameCapture.DepthData`. False → composer skips normal-map atlas generation and logs a one-shot warning. |
| `SupportsSkeletonIntrospection` | `bool` | True → bridge can resolve bone names to world positions via `TryGetBonePosition`. False → composer falls back to `ComputeFromBounds` when `AnchorBoneName` is set. |

**Model / equipment / animation / camera methods**:

| Method | Signature | Purpose |
|--------|-----------|---------|
| `LoadModelAsync` | `(AssetReference, CancellationToken) → Task<IModelHandle>` | Resolve asset reference to an engine handle and instantiate in the scene |
| `DisposeModel` | `(IModelHandle) → void` | Unload and free engine resources (idempotent) |
| `GetModelBounds` | `(IModelHandle) → BoundingBox` | Axis-aligned bounding box in world units (Vector3 min/max from core) |
| `SetModelScale` | `(IModelHandle, float) → void` | Apply uniform scale |
| `TryGetBonePosition` | `(IModelHandle, string boneName) → Vector3?` | Resolve bone's current world position (null when unsupported or missing). Conditional on `SupportsSkeletonIntrospection`. |
| `AttachEquipmentAsync` | `(IModelHandle, EquipmentSlot, CancellationToken) → Task<IEquipmentHandle>` | Resolve `slot.Mesh` (AssetReference), parent to `slot.BoneName` |
| `DetachEquipment` | `(IModelHandle, IEquipmentHandle) → void` | Remove attachment |
| `SetMaterialOverride` | `(IModelHandle, string slot, AssetReference? material) → void` | Override material; null restores original |
| `GetAvailableAnimations` | `(IModelHandle) → IReadOnlyList<AnimationInfo>` | Enumerate animations on skeleton |
| `SetAnimation` | `(IModelHandle, string name) → void` | Set active clip (scrubbable, no auto-playback) |
| `SetAnimationTime` | `(IModelHandle, float normalizedTime) → void` | Seek clip; pose updates synchronously; does NOT advance game loop |
| `ConfigureCamera` | `(OrthographicParameters, (int Width, int Height) frameSize) → void` | Apply composer-computed camera basis. Bridge never re-computes — the composer passes already-resolved `OrthographicParameters`. |

**Lifecycle hooks** (default to no-ops on `SpriteComposerBridgeBase`):

| Method | Signature | Purpose |
|--------|-----------|---------|
| `BeginRigAsync` | `(CameraRig, CancellationToken) → Task` | Called once per rig at the boundary of a rig's capture pass |
| `EndRigAsync` | `(CameraRig, CancellationToken) → Task` | Symmetric partner of `BeginRigAsync` |
| `BeginAngleAsync` | `(CaptureAngle, CancellationToken) → Task` | Called once per angle within a rig |
| `EndAngleAsync` | `(CaptureAngle, CancellationToken) → Task` | Symmetric partner of `BeginAngleAsync` |

**Frame capture** (identity-stamped):

| Method | Signature | Purpose |
|--------|-----------|---------|
| `CaptureFrameAsync` | `(string variantName, string rigName, string angleName, string animationName, int frameIndex, float normalizedTime, bool captureDepth, CancellationToken) → Task<FrameCapture>` | Render + readback. Returned `FrameCapture` is stamped with the full 5-tuple identity. `captureDepth` is composer-resolved as `rig.IncludeNormalMap AND SupportsDepthCapture`. |

**Preview + lifecycle**:

| Method | Signature | Purpose |
|--------|-----------|---------|
| `SetPreviewCamera` | `(float yaw, float pitch, float distance) → void` | Position orbit preview camera |
| `SetPreviewAnimationPlayback` | `(bool playing, float normalizedSpeed) → void` | Start/stop real-time preview playback |
| `DisposeAsync` | `() → ValueTask` | Release all engine resources |

### IModelHandle / IEquipmentHandle / IAnimationHandle

**Kind**: Marker interfaces

```
IModelHandle       // Opaque — bridge-defined concrete type
IEquipmentHandle   // Opaque — bridge-defined concrete type
IAnimationHandle   // Opaque — bridge-defined concrete type (optional)
```

No members. Exist for type safety at the composer/bridge boundary.

### SpriteProject

**Kind**: Record (immutable; modifications via commands produce new instances)

Multi-variant container. A single project holds every variant that shares rigs and an animation vocabulary. See the deep dive's SpriteProject section for the design rationale.

| Field | Type | Purpose |
|-------|------|---------|
| `Name` | `string` | Project identifier ("Heroes", "Troops", "Enemies", "Bosses") |
| `Rigs` | `IReadOnlyList<CameraRig>` | Camera rigs shared by every variant |
| `AnimationSets` | `IReadOnlyDictionary<string, AnimationSet>` | Named reusable animation configurations |
| `Variants` | `IReadOnlyList<VariantBinding>` | One entry per character variant |
| `ExportOptions` | `ExportOptions` | Output paths, filenames, encoder |
| `NormalMapOptions` | `NormalMapOptions?` | Null = use sprite-theory defaults |
| `CustomProperties` | `IReadOnlyDictionary<string, string>?` | Game-specific opaque metadata (propagates to every SpriteSheet output) |
| `SchemaVersion` | `string` | "1.0" (project file format version) |

**Pivot policy**: Per-variant pivot state lives on `VariantBinding.Variant.PivotOverride` and `VariantBinding.Variant.AnchorBoneName` (both sprite-theory `CharacterVariant` fields). The capture session resolves the pivot per angle; `BuildSpriteFrames` stamps the resolved value onto each `SpriteFrame`. See CaptureSession.BuildPerGroupOutputs below.

### AnimationSet

**Kind**: Record (immutable)

Named reusable animation configuration. Multiple `VariantBindings` bind to the same set by name to share a single configuration.

| Field | Type | Purpose |
|-------|------|---------|
| `Name` | `string` | Set identifier ("humanoid-combat", "ranged-attacker", "quadruped", "boss-multi-phase") |
| `SelectedAnimationNames` | `IReadOnlyList<string>` | Bridge-reported animation names to capture (persisted sorted alphabetically for determinism) |
| `AnimationConfigs` | `IReadOnlyDictionary<string, AnimationConfig>` | Per-animation overrides within the set, keyed by animation name |
| `DefaultAnimationConfig` | `AnimationConfig` | Defaults when no per-animation override exists |

### VariantBinding

**Kind**: Record (immutable)

One entry per character variant. References an `AnimationSet` by name and optionally overrides or excludes specific animations for this variant only.

| Field | Type | Purpose |
|-------|------|---------|
| `Variant` | `CharacterVariant` | sprite-theory record: `Model` (AssetReference), `Equipment`, `MaterialOverrides`, `Scale`, `PivotOverride`, `AnchorBoneName` |
| `AnimationSetName` | `string` | Must exist in `project.AnimationSets` — fails validation if missing |
| `AnimationOverrides` | `IReadOnlyDictionary<string, AnimationConfig>?` | Per-variant overrides on specific animations within the bound set. Null = inherit the set's configs unchanged. |
| `ExcludedAnimations` | `IReadOnlyList<string>?` | Animations in the set this variant should skip. Null = include every animation in the set. |

**Effective config resolution** for a `(variantBinding, animName)` tuple:

```
variantBinding.AnimationOverrides?.GetValueOrDefault(animName)
 ?? project.AnimationSets[variantBinding.AnimationSetName]
       .AnimationConfigs.GetValueOrDefault(
           animName,
           project.AnimationSets[variantBinding.AnimationSetName].DefaultAnimationConfig)
```

**Effective animation list resolution** for a `variantBinding`:

```
project.AnimationSets[variantBinding.AnimationSetName]
    .SelectedAnimationNames
    .Except(variantBinding.ExcludedAnimations ?? [])
```

### ExportOptions

**Kind**: Record

| Field | Type | Default | Purpose |
|-------|------|---------|---------|
| `OutputDirectory` | `string` | required | Target directory for atlas + JSON files |
| `AtlasFilenamePattern` | `string` | `"{variant}_{rig}_{atlas}.png"` | Filename template with `{variant}`, `{rig}`, `{atlas}` placeholders |
| `NormalMapFilenamePattern` | `string` | `"{variant}_{rig}_{atlas}_normal.png"` | Normal map filename template |
| `MetadataFilenamePattern` | `string` | `"{variant}_{rig}.json"` | Metadata filename template |
| `RigsToExport` | `IReadOnlyList<string>?` | `null` | Rig name filter; null = all rigs |
| `AtlasEncoder` | `IAtlasEncoder` | required | Consumer-supplied PNG/other encoder |

**Pattern validation**: `ExportPipeline.ExportAsync` validates at export start that `AtlasFilenamePattern`, `NormalMapFilenamePattern`, and `MetadataFilenamePattern` all contain `{variant}` and `{rig}` placeholders. A multi-variant project whose patterns are missing either placeholder would overwrite its own outputs — the validation catches this immediately and throws `InvalidOperationException` with a diagnostic message.

### IAtlasEncoder

**Kind**: Interface

| Method | Signature | Purpose |
|--------|-----------|---------|
| `EncodeRgba` | `(byte[] rgba, int width, int height) → byte[]` | Encode raw RGBA bytes into final file bytes (typically PNG) |

### CaptureSession

**Kind**: Class
**Thread Safety**: Single writer (the session itself); `Progress`, `State`, `CapturedFrames.Count`, `Errors.Count` safely readable from other threads

| Field | Type | Purpose |
|-------|------|---------|
| `Project` | `SpriteProject` | Immutable snapshot at session start |
| `Bridge` | `ISpriteComposerBridge` | Required for session (non-null) |
| `State` | `CaptureState` | Lifecycle state |
| `Progress` | `CaptureProgress` | Live progress snapshot |
| `CapturedFrames` | `List<(FrameCapture Capture, Vector2 Pivot)>` | Accumulated captures, each paired with its per-angle resolved pivot |
| `Errors` | `List<CaptureError>` | Per-frame errors recorded during capture |
| `IsPauseRequested` | `bool` | Cooperative pause flag |
| `resumeSignal` | `TaskCompletionSource<bool>` | Internal resume coordination (replaced on each Pause/Resume cycle) |

**Events**:

| Event | Args | When |
|-------|------|------|
| `StateChanged` | `CaptureStateEventArgs` | Idle/Running/Paused/Completed/Cancelled/Failed transitions |
| `Progress` | `CaptureProgressEventArgs` | Throttled progress updates |
| `FrameCaptured` | `FrameCapturedEventArgs` | Per-frame (unthrottled, optional — for telemetry) |
| `FrameFailed` | `CaptureErrorEventArgs` | Per-frame error |

### CaptureProgress

**Kind**: Record (snapshot — new instance per progress event)

| Field | Type | Purpose |
|-------|------|---------|
| `TotalFrames` | `int` | Expected total from `CaptureManifest.Compute` across all variants |
| `CapturedFrames` | `int` | Count of successful captures |
| `FailedFrames` | `int` | Count of per-frame errors |
| `CurrentVariant` | `string` | Current variant name (the outermost loop variable) |
| `CurrentRig` | `string` | Current rig name |
| `CurrentAnimation` | `string` | Current animation name |
| `CurrentAngle` | `string` | Current angle name |
| `CurrentFrameIndex` | `int` | Frame within the current animation |
| `ElapsedMs` | `long` | Wall-clock time since session start |
| `EstimatedRemainingMs` | `long` | Linear extrapolation |

### CaptureResult

**Kind**: Record (immutable output)

| Field | Type | Purpose |
|-------|------|---------|
| `CapturedFrames` | `IReadOnlyList<(FrameCapture Capture, Vector2 Pivot)>` | All successfully-captured frames with their resolved pivots |
| `PerGroupOutputs` | `IReadOnlyList<GroupCaptureOutput>` | One entry per `(VariantName, RigName)` grouping — the assembly-phase outputs |
| `Errors` | `IReadOnlyList<CaptureError>` | Per-frame errors |
| `Manifest` | `CaptureManifest` | Precomputed manifest (aggregated across every variant) |

### GroupCaptureOutput

**Kind**: Record

One output per `(VariantName, RigName)` grouping. A multi-variant project with 4 variants × 2 rigs produces 8 `GroupCaptureOutput` entries.

| Field | Type | Purpose |
|-------|------|---------|
| `VariantName` | `string` | Variant this output belongs to |
| `Rig` | `CameraRig` | Source rig (from sprite-theory) |
| `AtlasImages` | `IReadOnlyList<byte[]>` | Raw RGBA bytes per atlas image (1+ when multi-atlas overflow) |
| `NormalAtlases` | `IReadOnlyList<byte[]>?` | Raw RGBA normal maps when `rig.IncludeNormalMap AND bridge.SupportsDepthCapture`; null otherwise |
| `SpriteSheet` | `SpriteSheet` | Complete metadata (from sprite-theory) |

### CaptureError

**Kind**: Record

| Field | Type | Purpose |
|-------|------|---------|
| `VariantName` | `string` | Variant that was active |
| `RigName` | `string` | Rig that was active |
| `AngleName` | `string` | Angle that was active |
| `AnimationName` | `string` | Animation that was active |
| `FrameIndex` | `int` | Frame within the animation |
| `NormalizedTime` | `float` | Animation time when capture failed |
| `Exception` | `Exception` | Underlying bridge exception |
| `Timestamp` | `DateTimeOffset` | When the error occurred |

### CaptureState

**Kind**: Enum

| Value | Meaning |
|-------|---------|
| Idle | Session created, not yet started |
| Running | `ExecuteAsync` actively iterating |
| Paused | Session paused cooperatively; resume signal awaited |
| Completed | All frames attempted; `CapturedFrames` finalized |
| Cancelled | Caller cancelled via `CancellationToken` |
| Failed | Too many per-frame errors exceeded tolerance; terminated early |

### AnimationBrowser

**Kind**: Class
**Thread Safety**: Single-threaded

The browser mirrors the effective animation list of the **active variant** — it reflects `project.AnimationSets[activeBinding.AnimationSetName].SelectedAnimationNames.Except(activeBinding.ExcludedAnimations)` as the "current selection" for UI purposes.

| Field | Type | Purpose |
|-------|------|---------|
| `AvailableAnimations` | `IReadOnlyList<AnimationInfo>` | All animations the bridge reports for the active variant's model |
| `Filter` | `AnimationFilter` | Active filter criteria |
| `FilteredAnimations` | `IReadOnlyList<AnimationInfo>` | `AvailableAnimations` after filter |
| `EffectiveSelection` | `IReadOnlySet<string>` | Effective selection for the active variant (computed, not mutable via this class) |

### AnimationFilter

**Kind**: Record

| Field | Type | Purpose |
|-------|------|---------|
| `NameContains` | `string?` | Substring match (case-insensitive) |
| `NamePrefix` | `string?` | Prefix match |
| `NameSuffix` | `string?` | Suffix match |
| `MinDuration` | `float?` | Minimum animation duration in seconds |
| `MaxDuration` | `float?` | Maximum animation duration in seconds |
| `LoopingOnly` | `bool?` | true = only looping, false = only non-looping, null = both |
| `MaxResults` | `int?` | Cap result count |

### EquipmentManager

**Kind**: Class

Scoped to the active variant. When the active variant changes, `Attachments` is fully cleared and rebuilt from the new binding's `Variant.Equipment` list.

| Field | Type | Purpose |
|-------|------|---------|
| `Attachments` | `Dictionary<string, (EquipmentSlot Slot, IEquipmentHandle Handle)>` | Keyed by slot name, non-null only for the active variant |

### SpritePreview

**Kind**: Class

Points at a single `(VariantName, RigName)` group from the capture result.

| Field | Type | Purpose |
|-------|------|---------|
| `VariantName` | `string` | Which variant this preview is playing |
| `SpriteSheet` | `SpriteSheet` | Captured metadata (from sprite-theory) |
| `Atlases` | `IReadOnlyList<byte[]>` | Raw RGBA atlas pixel data |
| `CurrentAnimation` | `string` | Active animation name |
| `CurrentAngle` | `string` | Active angle name |
| `CurrentFrameIndex` | `int` | Frame within the animation (0-based within `AngleFrameMap[angle]`) |
| `PlaybackSpeed` | `float` | Speed multiplier (1.0 = real-time) |
| `State` | `PreviewState` | Stopped / Playing / Paused |
| `TargetFps` | `float` | Playback rate when `State == Playing` |

### PreviewController

**Kind**: Class (wraps `SpritePreview` with transport controls)

| Field | Type | Purpose |
|-------|------|---------|
| `Preview` | `SpritePreview` | Underlying preview state |
| `playbackTimer` | `Timer?` | Internal frame advance timer when Playing |

### PreviewState

**Kind**: Enum

| Value | Meaning |
|-------|---------|
| Stopped | Frame advance halted; `CurrentFrameIndex = 0` |
| Playing | Timer is advancing frames at `TargetFps × PlaybackSpeed` |
| Paused | Frame advance halted; `CurrentFrameIndex` preserved |

### IEditorCommand

**Kind**: Interface (same contract as scene-composer's `IEditorCommand` — separate type, same semantics)

| Member | Signature | Purpose |
|--------|-----------|---------|
| `Description` | `string` | Human-readable name |
| `Execute` | `() → void` | Apply change |
| `Undo` | `() → void` | Reverse change |
| `CanMergeWith` | `(IEditorCommand other) → bool` | Merge eligibility |
| `TryMerge` | `(IEditorCommand other) → bool` | Merge into this command, returning success |

### CommandStack

**Kind**: Class (mirror of scene-composer's `CommandStack` — independent type, identical semantics)

| Field | Type | Purpose |
|-------|------|---------|
| `undoStack` | `List<IEditorCommand>` | Undo history (newest at end) |
| `redoStack` | `List<IEditorCommand>` | Redo history (newest at end) |
| `maxDepth` | `int` | Capacity (from `SpriteComposerOptions`) |
| `activeCompound` | `CompoundCommand?` | Non-null during `BeginCompound` scope |
| `compoundDepth` | `int` | Nesting count for nested compound calls |
| `lastCommandTime` | `DateTime` | For merge-window comparison |
| `mergeWindow` | `TimeSpan` | Default 500ms |

**Events**: `StateChanged`, `CommandExecuted`, `CommandUndone`, `CommandRedone`.

### CompoundCommand

**Kind**: Class implements `IEditorCommand`

| Field | Type | Purpose |
|-------|------|---------|
| `Description` | `string` | Group description |
| `commands` | `List<IEditorCommand>` | Child commands in execution order |

`CanMergeWith` always returns false. `Undo` iterates children in reverse.

---

## Dependencies

| Dependency | Type | Usage |
|------------|------|-------|
| sprite-theory | SDK (project ref) | `CameraRig`, `CaptureAngle`, `OrthographicSetup`, `OrthographicParameters`, `AnimationSampling`, `AnimationConfig`, `AnimationInfo`, `AtlasPacker`, `AtlasLayout`, `AtlasOptions`, `AtlasAssembler`, `AtlasInfo`, `MirrorOptimizer`, `DepthToNormal`, `PivotComputer`, `SpriteSheetSerializer`, `SpriteSheet`, `SpriteFrame`, `SpriteAnimation`, `CharacterVariant`, `EquipmentSlot`, `AssetReference`, `FrameCapture`, `CaptureManifest`, `BoundingBox`, `Color`, `Rectangle`, `Vector2` |
| `BeyondImmersion.Bannou.Core` | SDK (transitive via sprite-theory) | `Vector3` — used by the bridge contract (`TryGetBonePosition`, via `BoundingBox` / `OrthographicParameters` on sprite-theory) |
| `System.Text.Json` | BCL | `SpriteProject` serialization |
| `System.IO` | BCL | Project file read/write |

**No external NuGet dependencies.** PNG encoding is delegated to the consumer via `IAtlasEncoder`.

---

## API Index

#### SpriteComposer

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| Create | `(SpriteComposerOptions?) → SpriteComposer` | Yes | Allocating | Headless (no bridge) |
| SetBridge | `(ISpriteComposerBridge) → void` | N/A | Free | Wire bridge after construction |
| ClearBridgeAsync | `() → ValueTask` | N/A | Free | Detach bridge; disposes active model + equipment first |
| NewProject | `(string name, IReadOnlyList<CameraRig> rigs) → void` | Yes | Allocating | Empty multi-variant project |
| LoadProjectAsync | `(string path, CancellationToken) → Task` | Yes | Allocating | Read JSON, deserialize, replace project |
| SaveProjectAsync | `(string path, CancellationToken) → Task` | Yes | Allocating | Serialize, write JSON |
| AddVariant | `(VariantBinding) → void` | Yes | Allocating | Pushes `AddVariantCommand` |
| RemoveVariant | `(string variantName) → void` | Yes | Allocating | Pushes `RemoveVariantCommand` |
| AddAnimationSet | `(AnimationSet) → void` | Yes | Allocating | Pushes `AddAnimationSetCommand` |
| RemoveAnimationSet | `(string setName) → void` | Yes | Allocating | Fails if any variant binds to the set |
| ModifyAnimationSet | `(string setName, AnimationSet newSet) → void` | Yes | Allocating | Pushes `ModifyAnimationSetCommand` |
| AddRig / RemoveRig / ModifyRig | `(...) → void` | Yes | Allocating | Project-level rig commands |
| SetActiveVariantAsync | `(string variantName, CancellationToken) → Task` | Yes | Allocating | Bridge required; loads the variant's model and equipment for 3D preview |
| SetVariantModel | `(string variantName, AssetReference model) → void` | Yes | Allocating | Pushes `SetVariantModelCommand` |
| SetVariantScale | `(string variantName, float scale) → void` | Yes | Allocating | Pushes `SetVariantScaleCommand` |
| SetVariantPivotOverride | `(string variantName, Vector2? pivot) → void` | Yes | Allocating | Pushes `SetVariantPivotOverrideCommand` |
| SetVariantAnchorBoneName | `(string variantName, string? boneName) → void` | Yes | Allocating | Pushes `SetVariantAnchorBoneNameCommand` |
| AttachEquipment | `(string variantName, string slot, AssetReference mesh, string bone) → void` | Yes | Allocating | Bridge call when variant is active; pushes command |
| DetachEquipment | `(string variantName, string slot) → void` | Yes | Allocating | Bridge call when variant is active; pushes command |
| SetMaterialOverride | `(string variantName, string slot, AssetReference? material) → void` | Yes | Allocating | Pushes `SetMaterialOverrideCommand` |
| BindVariantToAnimationSet | `(string variantName, string setName) → void` | Yes | Allocating | Pushes `BindVariantToAnimationSetCommand` |
| SetVariantAnimationOverride | `(string variantName, string animName, AnimationConfig?) → void` | Yes | Allocating | Pushes `SetVariantAnimationOverrideCommand` |
| SetVariantExcludedAnimations | `(string variantName, IReadOnlyList<string> excluded) → void` | Yes | Allocating | Pushes `SetVariantExcludedAnimationsCommand` |
| Undo | `() → bool` | Yes | Minimal | Delegates to `CommandStack` |
| Redo | `() → bool` | Yes | Minimal | Delegates to `CommandStack` |
| ComputeCaptureManifest | `() → CaptureManifest` | Yes | Allocating | Aggregated across every variant; works headless |
| StartCaptureAsync | `(CancellationToken) → Task<CaptureResult>` | Yes | Allocating | Bridge required; creates `CaptureSession`, runs `ExecuteAsync` |
| PauseCapture | `() → void` | N/A | Free | Requests cooperative pause |
| ResumeCapture | `() → void` | N/A | Free | Signals resume |
| CancelCapture | `() → void` | N/A | Free | Cancels active `CancellationToken` |
| ExportAsync | `(CaptureResult, CancellationToken) → Task` | Yes | Allocating | Delegates to `ExportPipeline` |
| DisposeAsync | `() → ValueTask` | N/A | Free | Disposes bridge and internal state |

#### CaptureSession

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| Create | `(SpriteProject, ISpriteComposerBridge, SpriteComposerOptions) → CaptureSession` | Yes | Allocating | Immutable project snapshot |
| ExecuteAsync | `(IProgress<CaptureProgress>?, CancellationToken) → Task<CaptureResult>` | Yes | Allocating | Per-variant outer loop |
| RequestPause | `() → void` | N/A | Free | Cooperative pause via `TaskCompletionSource` |
| Resume | `() → void` | N/A | Free | Signal the current resume TCS |

#### AnimationBrowser

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| RefreshAsync | `(IModelHandle, ISpriteComposerBridge, CancellationToken) → Task` | Yes | Allocating | Calls `bridge.GetAvailableAnimations` |
| SetFilter | `(AnimationFilter) → void` | Yes | Allocating | Rebuilds `FilteredAnimations` |
| UpdateEffectiveSelectionFor | `(VariantBinding, AnimationSet) → void` | Yes | Free | Recomputes `EffectiveSelection` from the set minus variant exclusions |
| LookupAnimationInfo | `(string name) → AnimationInfo?` | Yes | Free | Find by name in `AvailableAnimations` |

#### EquipmentManager

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| Attach | `(string slotName, EquipmentSlot, IEquipmentHandle) → void` | Yes | Allocating | Add to `Attachments` |
| Detach | `(string slotName) → IEquipmentHandle?` | Yes | Free | Remove from `Attachments`, return former handle |
| Get | `(string slotName) → (EquipmentSlot, IEquipmentHandle)?` | Yes | Free | Lookup |
| IsOccupied | `(string slotName) → bool` | Yes | Free | Check presence |
| Clear | `() → void` | Yes | Free | Empty map; caller is responsible for detaching via the bridge before calling |

#### SpritePreview

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| Create | `(string variantName, SpriteSheet, IReadOnlyList<byte[]> atlases) → SpritePreview` | Yes | Allocating | Initial state: first animation, first angle, frame 0 |
| SwitchAnimation | `(string animName) → void` | Yes | Allocating | Validates animation exists |
| SwitchAngle | `(string angleName) → void` | Yes | Allocating | Validates angle exists |
| StepForward | `() → void` | Yes | Free | Advance frame; wraps or stops per `LoopMode` |
| StepBackward | `() → void` | Yes | Free | Decrement frame |
| JumpToFrame | `(int index) → void` | Yes | Free | Direct index; validates |
| GetCurrentSpriteFrame | `() → SpriteFrame` | Yes | Free | Lookup via `AngleFrameMap[CurrentAngle][CurrentFrameIndex]` |

#### PreviewController

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| Play | `() → void` | N/A | Allocating | Start internal timer |
| Pause | `() → void` | N/A | Free | Stop timer, preserve frame |
| Stop | `() → void` | N/A | Free | Stop timer, reset to frame 0 |
| SetSpeed | `(float multiplier) → void` | Yes | Free | Update `PlaybackSpeed`; timer interval recomputed |

#### ProjectSerializer

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| Serialize | `(SpriteProject) → string` | Yes | Allocating | JSON via `System.Text.Json` |
| Deserialize | `(string json) → SpriteProject` | Yes | Allocating | Throws `JsonException` on invalid; validates `VariantBinding.AnimationSetName` references exist |

#### ExportPipeline

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| ExportAsync | `(CaptureResult, SpriteProject, CancellationToken) → Task` | Yes | Allocating | Writes atlas PNGs + JSON metadata per `(VariantName, RigName)` group |
| ResolveFilename | `(string pattern, string variant, string rig, int atlasIndex) → string` | Yes | Allocating | Pure substitution; throws when required placeholder is missing |
| ValidatePatterns | `(ExportOptions) → void` | Yes | Free | Ensures every pattern contains `{variant}` and `{rig}` placeholders |

#### CommandStack

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| Execute | `(IEditorCommand, bool allowMerge = true) → void` | Yes | Minimal | Run + push to undo |
| Undo | `() → bool` | Yes | Free | Pop, reverse, push to redo |
| Redo | `() → bool` | Yes | Free | Pop, re-execute, push to undo |
| BeginCompound | `(string description) → IDisposable` | Yes | Minimal | Scope that groups commands |
| Clear | `() → void` | Yes | Free | Reset both stacks |
| BreakMerge | `() → void` | N/A | Free | Force next command to not merge |

---

## Methods

### SpriteComposer.Create
`(options: SpriteComposerOptions?) → SpriteComposer`

COMPUTE effectiveOptions ← options ?? SpriteComposerOptions.Default
CREATE composer ← new SpriteComposer()
SET composer.Options ← effectiveOptions
SET composer.Commands ← new CommandStack(effectiveOptions.MaxUndoDepth)
SET composer.EquipmentManager ← new EquipmentManager()
SET composer.AnimationBrowser ← new AnimationBrowser()
SET composer.Project ← null
SET composer.Bridge ← null
SET composer.ActiveVariantName ← null
SET composer.ModelHandle ← null
SET composer.IsDirty ← false
RETURN composer

---

### SpriteComposer.SetBridge
`(bridge: ISpriteComposerBridge) → void`

VALIDATE bridge is not null                          → ArgumentNullException
VALIDATE this.Bridge is null                         → InvalidOperationException("Bridge already attached; call ClearBridgeAsync first")
SET this.Bridge ← bridge

---

### SpriteComposer.ClearBridgeAsync
`() → ValueTask`

IF this.Bridge is null
  RETURN
// Dispose active model + equipment through bridge before releasing
IF ModelHandle is not null
  FOREACH (slotName, (slot, handle)) in EquipmentManager.Attachments
    Bridge.DetachEquipment(ModelHandle, handle)
  EquipmentManager.Clear()
  Bridge.DisposeModel(ModelHandle)
  SET ModelHandle ← null
SET ActiveVariantName ← null
await Bridge.DisposeAsync()
SET this.Bridge ← null

---

### SpriteComposer.NewProject
`(name: string, rigs: IReadOnlyList<CameraRig>) → void`

// Clear existing state
await CloseProjectAsync()
// Build empty multi-variant project with defaults
SET Project ← new SpriteProject(
    Name: name,
    Rigs: rigs,
    AnimationSets: new Dictionary<string, AnimationSet>(),
    Variants: new List<VariantBinding>(),
    ExportOptions: ExportOptions.Default,
    NormalMapOptions: null,
    CustomProperties: null,
    SchemaVersion: "1.0")
SET IsDirty ← false
Commands.Clear()
Fire ProjectLoaded(Project)

---

### SpriteComposer.LoadProjectAsync
`(path: string, ct: CancellationToken) → Task`

VALIDATE File.Exists(path)                           → FileNotFoundException
COMPUTE json ← await File.ReadAllTextAsync(path, ct)
COMPUTE loadedProject ← ProjectSerializer.Deserialize(json)
await CloseProjectAsync()
SET Project ← loadedProject
SET IsDirty ← false
Commands.Clear()
Fire ProjectLoaded(Project)

---

### SpriteComposer.SaveProjectAsync
`(path: string, ct: CancellationToken) → Task`

VALIDATE Project is not null                         → InvalidOperationException("No project loaded")
COMPUTE json ← ProjectSerializer.Serialize(Project)
await File.WriteAllTextAsync(path, json, ct)
SET IsDirty ← false
Fire ProjectSaved(Project)

---

### SpriteComposer.CloseProjectAsync (internal helper)
`() → ValueTask`

IF ActiveCaptureSession is not null AND ActiveCaptureSession.State IN [Running, Paused]
  CancelCapture()
SET Preview ← null
SET ActiveCaptureSession ← null
IF ModelHandle is not null AND Bridge is not null
  FOREACH (_, (slot, handle)) in EquipmentManager.Attachments
    Bridge.DetachEquipment(ModelHandle, handle)
  Bridge.DisposeModel(ModelHandle)
  SET ModelHandle ← null
EquipmentManager.Clear()
SET AnimationBrowser ← new AnimationBrowser()
SET ActiveVariantName ← null
SET Project ← null

---

### SpriteComposer.SetActiveVariantAsync
`(variantName: string, ct: CancellationToken) → Task`

VALIDATE Bridge is not null                          → InvalidOperationException("Bridge required")
VALIDATE Project is not null                         → InvalidOperationException("No project loaded")
COMPUTE binding ← Project.Variants.FirstOrDefault(v => v.Variant.Name == variantName)
VALIDATE binding is not null                         → ArgumentException($"Variant '{variantName}' not found")

// Tear down the previous active variant
IF ModelHandle is not null
  FOREACH (_, (_, handle)) in EquipmentManager.Attachments
    Bridge.DetachEquipment(ModelHandle, handle)
  EquipmentManager.Clear()
  Bridge.DisposeModel(ModelHandle)
  SET ModelHandle ← null

// Load the new variant
SET ModelHandle ← await Bridge.LoadModelAsync(binding.Variant.Model, ct)
Bridge.SetModelScale(ModelHandle, binding.Variant.Scale)
FOREACH slot IN binding.Variant.Equipment
  COMPUTE handle ← await Bridge.AttachEquipmentAsync(ModelHandle, slot, ct)
  EquipmentManager.Attach(slot.SlotName, slot, handle)
FOREACH (materialSlot, materialRef) IN (binding.Variant.MaterialOverrides ?? {})
  Bridge.SetMaterialOverride(ModelHandle, materialSlot, materialRef)

// Refresh animations from bridge and update effective selection
COMPUTE animations ← Bridge.GetAvailableAnimations(ModelHandle)
AnimationBrowser.SetAvailableAnimations(animations)
COMPUTE set ← Project.AnimationSets[binding.AnimationSetName]
AnimationBrowser.UpdateEffectiveSelectionFor(binding, set)

SET ActiveVariantName ← variantName
Fire ActiveVariantChanged(variantName)

---

### SpriteComposer.AddVariant / RemoveVariant
`(...) → void`

VALIDATE Project is not null                         → InvalidOperationException
AddVariant(binding):
  VALIDATE binding.AnimationSetName in Project.AnimationSets   → ArgumentException("Unknown AnimationSet")
  VALIDATE Project.Variants.All(v => v.Variant.Name != binding.Variant.Name)   → ArgumentException("Duplicate variant name")
  CREATE command ← new AddVariantCommand(this, binding)
  Commands.Execute(command)
RemoveVariant(variantName):
  CREATE command ← new RemoveVariantCommand(this, variantName)
  Commands.Execute(command)

---

### SpriteComposer.AddAnimationSet / RemoveAnimationSet / ModifyAnimationSet
`(...) → void`

AddAnimationSet(set):
  VALIDATE Project.AnimationSets does not contain set.Name   → ArgumentException
  CREATE command ← new AddAnimationSetCommand(this, set)
  Commands.Execute(command)
RemoveAnimationSet(setName):
  VALIDATE Project.Variants.None(v => v.AnimationSetName == setName)   → InvalidOperationException("Set still bound")
  CREATE command ← new RemoveAnimationSetCommand(this, setName)
  Commands.Execute(command)
ModifyAnimationSet(setName, newSet):
  CREATE command ← new ModifyAnimationSetCommand(this, setName, oldSet, newSet)
  Commands.Execute(command)

---

### SpriteComposer.AddRig / RemoveRig / ModifyRig
`(...) → void`

VALIDATE Project is not null                         → InvalidOperationException
CREATE command ← new {Add,Remove,Modify}RigCommand(this, args)
Commands.Execute(command)

---

### SpriteComposer.SetVariantModel
`(variantName: string, model: AssetReference) → void`

VALIDATE Project is not null                         → InvalidOperationException
COMPUTE binding ← Project.Variants.First(v => v.Variant.Name == variantName)
IF binding.Variant.Model equals model                // No-op
  RETURN
CREATE command ← new SetVariantModelCommand(this, variantName, binding.Variant.Model, model)
Commands.Execute(command)

---

### SpriteComposer.SetVariantScale / SetVariantPivotOverride / SetVariantAnchorBoneName
`(...) → void`

SetVariantScale(variantName, scale):
  VALIDATE scale > 0                                 → ArgumentException
  COMPUTE binding ← Project.Variants.First(v => v.Variant.Name == variantName)
  IF binding.Variant.Scale equals scale: RETURN
  CREATE command ← new SetVariantScaleCommand(this, variantName, binding.Variant.Scale, scale)
  Commands.Execute(command)

SetVariantPivotOverride(variantName, pivot?):
  COMPUTE binding ← Project.Variants.First(v => v.Variant.Name == variantName)
  IF binding.Variant.PivotOverride equals pivot: RETURN
  CREATE command ← new SetVariantPivotOverrideCommand(this, variantName, binding.Variant.PivotOverride, pivot)
  Commands.Execute(command)

SetVariantAnchorBoneName(variantName, boneName?):
  COMPUTE binding ← Project.Variants.First(v => v.Variant.Name == variantName)
  IF binding.Variant.AnchorBoneName equals boneName: RETURN
  CREATE command ← new SetVariantAnchorBoneNameCommand(this, variantName, binding.Variant.AnchorBoneName, boneName)
  Commands.Execute(command)

---

### SpriteComposer.AttachEquipment / DetachEquipment
`(...) → void`

AttachEquipment(variantName, slotName, mesh: AssetReference, boneName):
  VALIDATE Project is not null                       → InvalidOperationException
  COMPUTE slot ← new EquipmentSlot(slotName, mesh, boneName)   // sprite-theory record
  CREATE command ← new AttachEquipmentCommand(this, variantName, slot)
  Commands.Execute(command)

DetachEquipment(variantName, slotName):
  COMPUTE binding ← Project.Variants.First(v => v.Variant.Name == variantName)
  VALIDATE binding.Variant.Equipment.Any(e => e.SlotName == slotName)   → InvalidOperationException("Slot not occupied")
  CREATE command ← new DetachEquipmentCommand(this, variantName, slotName)
  Commands.Execute(command)

---

### SpriteComposer.SetMaterialOverride
`(variantName: string, materialSlot: string, material: AssetReference?) → void`

COMPUTE binding ← Project.Variants.First(v => v.Variant.Name == variantName)
COMPUTE previous ← binding.Variant.MaterialOverrides?.GetValueOrDefault(materialSlot)
IF previous equals material: RETURN
CREATE command ← new SetMaterialOverrideCommand(this, variantName, materialSlot, previous, material)
Commands.Execute(command)

---

### SpriteComposer.BindVariantToAnimationSet / SetVariantAnimationOverride / SetVariantExcludedAnimations
`(...) → void`

BindVariantToAnimationSet(variantName, setName):
  VALIDATE Project.AnimationSets contains setName   → ArgumentException("Unknown AnimationSet")
  CREATE command ← new BindVariantToAnimationSetCommand(this, variantName, setName)
  Commands.Execute(command)

SetVariantAnimationOverride(variantName, animName, config?):
  CREATE command ← new SetVariantAnimationOverrideCommand(this, variantName, animName, config)
  Commands.Execute(command)

SetVariantExcludedAnimations(variantName, excluded):
  CREATE command ← new SetVariantExcludedAnimationsCommand(this, variantName, excluded)
  Commands.Execute(command)

---

### SpriteComposer.Undo / Redo
`() → bool`

RETURN Commands.Undo() / Commands.Redo()
// Commands fire relevant domain events; CommandStack fires UndoRedoStateChanged

---

### SpriteComposer.ComputeCaptureManifest
`() → CaptureManifest`

VALIDATE Project is not null                         → InvalidOperationException
// Aggregate across every variant × rig × effective animation
CREATE totalsPerVariant ← new List<CaptureManifest>()
FOREACH binding IN Project.Variants
  COMPUTE set ← Project.AnimationSets[binding.AnimationSetName]
  COMPUTE effectiveAnimations ← set.SelectedAnimationNames.Except(binding.ExcludedAnimations ?? [])
  CREATE pairs ← new List<(AnimationInfo, AnimationConfig)>()
  FOREACH name IN effectiveAnimations (sorted alphabetically)
    COMPUTE info ← AnimationBrowser.LookupAnimationInfo(name)                    // may be null in pure-headless
    COMPUTE config ← binding.AnimationOverrides?.GetValueOrDefault(name)
                  ?? set.AnimationConfigs.GetValueOrDefault(name, set.DefaultAnimationConfig)
    IF info is not null
      ACCUMULATE (info, config) to pairs
  COMPUTE variantManifest ← CaptureManifest.Compute(binding.Variant, Project.Rigs, pairs)   // sprite-theory
  ACCUMULATE variantManifest to totalsPerVariant
RETURN CaptureManifest.AggregateMultiVariant(totalsPerVariant)                   // Helper in sprite-theory (or inline summation)

---

### SpriteComposer.StartCaptureAsync
`(ct: CancellationToken) → Task<CaptureResult>`

VALIDATE Bridge is not null                          → InvalidOperationException
VALIDATE Project is not null                         → InvalidOperationException
VALIDATE ActiveCaptureSession is null OR ActiveCaptureSession.State IN [Completed, Cancelled, Failed]
                                                      → InvalidOperationException("Capture already running")
VALIDATE Project.Variants.Count > 0                  → InvalidOperationException("No variants configured")
VALIDATE Project.Rigs.Count > 0                      → InvalidOperationException("No rigs configured")
VALIDATE Project.AnimationSets.Count > 0             → InvalidOperationException("No animation sets configured")
VALIDATE every Variants[i].AnimationSetName exists in Project.AnimationSets   → InvalidOperationException

CREATE session ← CaptureSession.Create(Project, Bridge, Options)
SET ActiveCaptureSession ← session
// Wire events to forward to composer subscribers
session.StateChanged += handler that fires CaptureStarted / CaptureCompleted
session.Progress += handler that fires CaptureProgress
session.FrameFailed += handler that fires CaptureError
Fire CaptureStarted(session, ComputeCaptureManifest())

COMPUTE result ← await session.ExecuteAsync(progress: internal dispatcher, ct)

// Build preview from the first group's output (consumer can switch via SwitchPreviewGroup)
IF result.PerGroupOutputs.Count > 0
  COMPUTE firstGroup ← result.PerGroupOutputs[0]
  SET Preview ← SpritePreview.Create(firstGroup.VariantName, firstGroup.SpriteSheet, firstGroup.AtlasImages)

Fire CaptureCompleted(result)
RETURN result

---

### SpriteComposer.PauseCapture / ResumeCapture / CancelCapture
`() → void`

IF ActiveCaptureSession is null: RETURN
ActiveCaptureSession.RequestPause() / Resume() / (trigger CancellationToken via internal source)

---

### SpriteComposer.ExportAsync
`(captureResult: CaptureResult, ct: CancellationToken) → Task`

VALIDATE Project is not null                         → InvalidOperationException
await ExportPipeline.ExportAsync(captureResult, Project, ct)
Fire AllExportsCompleted()

---

### CaptureSession.Create
`(project: SpriteProject, bridge: ISpriteComposerBridge, options: SpriteComposerOptions) → CaptureSession`

CREATE session ← new CaptureSession()
SET session.Project ← project
SET session.Bridge ← bridge
SET session.Options ← options
SET session.State ← CaptureState.Idle
SET session.CapturedFrames ← new List<(FrameCapture, Vector2)>()
SET session.Errors ← new List<CaptureError>()
SET session.resumeSignal ← new TaskCompletionSource<bool>()
session.resumeSignal.TrySetResult(true)               // Initial state: unpaused
RETURN session

---

### CaptureSession.ExecuteAsync
`(progress: IProgress<CaptureProgress>?, ct: CancellationToken) → Task<CaptureResult>`

VALIDATE State == Idle                               → InvalidOperationException("Session already started or finished")

// Precompute aggregated manifest for progress totals
COMPUTE manifest ← ComputeAggregatedManifest(Project)
SET State ← CaptureState.Running
SET startTime ← UtcNow
Fire StateChanged(Running)

// One-shot capability warnings
CREATE warningsFired ← new HashSet<string>()

COMPUTE throttle ← TimeSpan.FromMilliseconds(Options.CaptureProgressIntervalMs)
COMPUTE lastReport ← DateTime.MinValue

Try:
  // ── Outer loop: per variant ──
  FOREACH binding IN Project.Variants
    IF ct.IsCancellationRequested: BREAK

    COMPUTE variant ← binding.Variant
    COMPUTE set ← Project.AnimationSets[binding.AnimationSetName]
    COMPUTE effectiveAnimations ← set.SelectedAnimationNames.Except(binding.ExcludedAnimations ?? []).OrderBy(n => n)

    // Load the variant's model + equipment
    COMPUTE handle ← await Bridge.LoadModelAsync(variant.Model, ct)
    Bridge.SetModelScale(handle, variant.Scale)
    CREATE attachedHandles ← new List<IEquipmentHandle>()
    FOREACH slot IN variant.Equipment
      COMPUTE eqHandle ← await Bridge.AttachEquipmentAsync(handle, slot, ct)
      ACCUMULATE eqHandle to attachedHandles
    FOREACH (materialSlot, materialRef) IN (variant.MaterialOverrides ?? {})
      Bridge.SetMaterialOverride(handle, materialSlot, materialRef)

    // Capability-contingent warnings (fired once per capture session)
    IF variant.AnchorBoneName is not null AND NOT Bridge.SupportsSkeletonIntrospection AND "skeleton" not in warningsFired
      LogWarning($"Variant '{variant.Name}' sets AnchorBoneName but bridge does not support skeleton introspection; falling back to ComputeFromBounds")
      warningsFired.Add("skeleton")

    COMPUTE modelBounds ← Bridge.GetModelBounds(handle)

    // ── Rig loop ──
    FOREACH rig IN Project.Rigs
      IF ct.IsCancellationRequested: BREAK
      await Bridge.BeginRigAsync(rig, ct)

      IF rig.IncludeNormalMap AND NOT Bridge.SupportsDepthCapture AND "depth" not in warningsFired
        LogWarning($"Rig '{rig.Name}' requests normal-map capture but bridge does not support depth; normal atlases will be skipped")
        warningsFired.Add("depth")

      // ── Angle loop ──
      FOREACH angle IN rig.Angles
        IF ct.IsCancellationRequested: BREAK
        await pauseSignal()
        await Bridge.BeginAngleAsync(angle, ct)

        COMPUTE orthoParams ← OrthographicSetup.Compute(angle, modelBounds, rig.FrameSize)   // sprite-theory
        COMPUTE pivot ← ResolvePivot(variant, Bridge, handle, modelBounds, orthoParams)
        Bridge.ConfigureCamera(orthoParams, rig.FrameSize)

        // ── Animation loop ──
        FOREACH animName IN effectiveAnimations
          await pauseSignal()
          IF ct.IsCancellationRequested: BREAK

          COMPUTE animInfo ← Bridge.GetAvailableAnimations(handle).FirstOrDefault(a => a.Name == animName)
          IF animInfo is null
            Errors.Add(new CaptureError(variant.Name, rig.Name, angle.Name, animName, -1, 0, new InvalidOperationException($"Animation '{animName}' not available"), UtcNow))
            CONTINUE

          COMPUTE animConfig ← binding.AnimationOverrides?.GetValueOrDefault(animName)
                            ?? set.AnimationConfigs.GetValueOrDefault(animName, set.DefaultAnimationConfig)
          COMPUTE sequence ← AnimationSampling.GenerateFromConfig(animInfo, animConfig)   // sprite-theory
          Bridge.SetAnimation(handle, animName)

          // ── Frame loop ──
          ITERATE frameIndex FROM 0 TO sequence.FrameCount - 1
            await pauseSignal()
            IF ct.IsCancellationRequested: BREAK

            COMPUTE normalizedTime ← sequence.Timestamps[frameIndex]
            Bridge.SetAnimationTime(handle, normalizedTime)

            Try:
              COMPUTE capture ← await Bridge.CaptureFrameAsync(
                  variantName: variant.Name,
                  rigName: rig.Name,
                  angleName: angle.Name,
                  animationName: animName,
                  frameIndex: frameIndex,
                  normalizedTime: normalizedTime,
                  captureDepth: rig.IncludeNormalMap AND Bridge.SupportsDepthCapture,
                  ct: ct)
              CapturedFrames.Add((capture, pivot))
              Fire FrameCaptured(capture)
            Catch (OperationCanceledException): RETHROW
            Catch (Exception ex):
              CREATE err ← new CaptureError(variant.Name, rig.Name, angle.Name, animName, frameIndex, normalizedTime, ex, UtcNow)
              Errors.Add(err)
              Fire FrameFailed(err)

            // Throttled progress reporting
            IF progress is not null AND (UtcNow - lastReport) >= throttle
              COMPUTE snap ← BuildProgressSnapshot(manifest, variant.Name, rig.Name, angle.Name, animName, frameIndex, startTime)
              progress.Report(snap)
              Fire Progress(snap)
              SET lastReport ← UtcNow

        await Bridge.EndAngleAsync(angle, ct)

      await Bridge.EndRigAsync(rig, ct)

    // Tear the variant down
    FOREACH eqHandle IN attachedHandles
      Bridge.DetachEquipment(handle, eqHandle)
    Bridge.DisposeModel(handle)

  IF ct.IsCancellationRequested
    SET State ← CaptureState.Cancelled
  ELSE
    // Assembly phase — one group per (variantName, rigName)
    COMPUTE perGroupOutputs ← BuildPerGroupOutputs(CapturedFrames, Project)
    SET State ← CaptureState.Completed

Catch (OperationCanceledException):
  SET State ← CaptureState.Cancelled

Catch (Exception ex):
  Errors.Add(new CaptureError("(session)", "", "", "", -1, 0, ex, UtcNow))
  SET State ← CaptureState.Failed

Finally:
  Fire StateChanged(State)

RETURN new CaptureResult(
    CapturedFrames: CapturedFrames,
    PerGroupOutputs: perGroupOutputs ?? empty,
    Errors: Errors,
    Manifest: manifest)


FUNCTION pauseSignal() → Task
  // Block until resumeSignal is completed (or await-propagate cancellation)
  RETURN resumeSignal.Task.WaitAsync(ct)


FUNCTION ResolvePivot(variant, bridge, handle, bounds, orthoParams) → Vector2
  // Four-step resolution order per CharacterVariant's pivot policy
  IF variant.PivotOverride is not null
    RETURN variant.PivotOverride.Value
  IF variant.AnchorBoneName is not null AND bridge.SupportsSkeletonIntrospection
    COMPUTE bonePos ← bridge.TryGetBonePosition(handle, variant.AnchorBoneName)
    IF bonePos is not null
      RETURN PivotComputer.ProjectWorldPointToFrame(bonePos.Value, orthoParams)   // sprite-theory
  RETURN PivotComputer.ComputeFromBounds(bounds, orthoParams)                      // sprite-theory — falls back to DefaultHumanoidPivot on degenerate camera

---

### CaptureSession.BuildPerGroupOutputs (internal)
`(capturedFrames: IReadOnlyList<(FrameCapture, Vector2)>, project: SpriteProject) → IReadOnlyList<GroupCaptureOutput>`

CREATE outputs ← new List<GroupCaptureOutput>()

// Group captures by (VariantName, RigName)
COMPUTE grouped ← capturedFrames.GroupBy(f => (f.Capture.VariantName, f.Capture.RigName))

FOREACH group IN grouped
  COMPUTE variantName ← group.Key.VariantName
  COMPUTE rigName ← group.Key.RigName
  COMPUTE binding ← project.Variants.First(v => v.Variant.Name == variantName)
  COMPUTE rig ← project.Rigs.First(r => r.Name == rigName)
  COMPUTE groupFrames ← group.ToList()

  // Atlas packing via sprite-theory (list position = FrameIndex, matches AtlasAssembler indexing)
  COMPUTE packInputs ← groupFrames.Select((f, i) => (f.Capture.Width, f.Capture.Height, i)).ToList()
  COMPUTE atlasOptions ← new AtlasOptions(
      MaxWidth: 4096, MaxHeight: 4096,
      Padding: rig.Padding,
      PowerOfTwo: true,
      GroupByAnimation: true)
  COMPUTE atlasLayout ← AtlasPacker.Pack(packInputs, atlasOptions)             // sprite-theory

  // Build captured SpriteFrames with per-frame pivots stamped from capturedFrames
  COMPUTE capturedSpriteFrames ← new List<SpriteFrame>()
  ITERATE i FROM 0 TO groupFrames.Count - 1
    COMPUTE (capture, pivot) ← groupFrames[i]
    COMPUTE placement ← atlasLayout.Placements.First(p => p.FrameIndex == i)
    COMPUTE animConfig ← ResolveEffectiveAnimationConfig(project, binding, capture.AnimationName)
    COMPUTE animInfo ← Bridge.GetAvailableAnimations(...).First(a => a.Name == capture.AnimationName)   // cached per session
    COMPUTE sequence ← AnimationSampling.GenerateFromConfig(animInfo, animConfig)
    COMPUTE duration ← sequence.Duration / sequence.FrameCount
    COMPUTE frame ← new SpriteFrame(
        Index: i,
        AtlasIndex: placement.AtlasIndex,
        AngleName: capture.AngleName,
        AnimationName: capture.AnimationName,
        FrameInAnimation: capture.FrameIndex,
        Rect: new Rectangle(placement.X, placement.Y, placement.Width, placement.Height),
        TrimmedRect: null,                                                        // Trimming not yet implemented
        Pivot: pivot,                                                             // Stamped from per-angle resolution
        Duration: duration,
        IsMirror: false,
        MirrorSourceIndex: null)
    ACCUMULATE frame to capturedSpriteFrames

  // Mirror generation via sprite-theory
  COMPUTE mirrors ← MirrorOptimizer.ComputeMirrors(rig)                         // sprite-theory
  COMPUTE mirrorFrames ← MirrorOptimizer.GenerateMirrorFrames(capturedSpriteFrames, mirrors)
  COMPUTE allFrames ← capturedSpriteFrames.Concat(mirrorFrames).ToList()

  // Per-atlas info with filename templating
  COMPUTE atlasInfos ← new List<AtlasInfo>()
  ITERATE atlasIdx FROM 0 TO atlasLayout.AtlasCount - 1
    COMPUTE filename ← ExportPipeline.ResolveFilename(
        project.ExportOptions.AtlasFilenamePattern, variantName, rig.Name, atlasIdx)
    atlasInfos.Add(new AtlasInfo(
        Index: atlasIdx,
        Filename: filename,
        Width: atlasLayout.AtlasWidths[atlasIdx],
        Height: atlasLayout.AtlasHeights[atlasIdx]))

  // Per-animation groupings
  COMPUTE animations ← BuildSpriteAnimations(allFrames, project, binding)

  // Atlas assembly via sprite-theory
  COMPUTE atlasImages ← AtlasAssembler.Assemble(
      groupFrames.Select(f => f.Capture).ToList(), atlasLayout, rig.BackgroundColor)

  // Optional normal map atlases
  COMPUTE normalAtlases ← null
  IF rig.IncludeNormalMap AND Bridge.SupportsDepthCapture
    normalAtlases ← BuildNormalAtlases(groupFrames.Select(f => f.Capture).ToList(), atlasLayout, project.NormalMapOptions ?? new NormalMapOptions())

  // Assemble SpriteSheet
  COMPUTE spriteSheet ← new SpriteSheet(
      Version: "1.0",
      Generator: "BeyondImmersion.Bannou.SpriteComposer",
      GeneratedAt: UtcNow,
      Variant: binding.Variant,
      Rig: rig,
      Atlases: atlasInfos,
      Animations: animations,
      Frames: allFrames,
      CustomProperties: project.CustomProperties)

  outputs.Add(new GroupCaptureOutput(variantName, rig, atlasImages, normalAtlases, spriteSheet))

RETURN outputs

---

### CaptureSession.BuildSpriteAnimations (internal)
`(frames: IReadOnlyList<SpriteFrame>, project: SpriteProject, binding: VariantBinding) → IReadOnlyList<SpriteAnimation>`

CREATE animations ← new List<SpriteAnimation>()
COMPUTE byAnim ← frames.GroupBy(f => f.AnimationName)
FOREACH group IN byAnim (sorted by animation name for determinism)
  COMPUTE animName ← group.Key
  COMPUTE animConfig ← ResolveEffectiveAnimationConfig(project, binding, animName)
  COMPUTE animInfo ← Bridge.GetAvailableAnimations(...).First(a => a.Name == animName)
  COMPUTE totalDuration ← animInfo.Duration * (animConfig.TrimEnd - animConfig.TrimStart) / animConfig.SpeedMultiplier

  // Build per-angle frame index lookup
  CREATE angleMap ← new Dictionary<string, int[]>()
  COMPUTE framesByAngle ← group.GroupBy(f => f.AngleName)
  FOREACH angleGroup IN framesByAngle
    COMPUTE orderedIndices ← angleGroup.OrderBy(f => f.FrameInAnimation).Select(f => f.Index).ToArray()
    SET angleMap[angleGroup.Key] ← orderedIndices

  animations.Add(new SpriteAnimation(
      Name: animName,
      LoopMode: animConfig.LoopMode,
      TotalDuration: totalDuration,
      AngleFrameMap: angleMap,
      Events: null))                                                              // Events not yet implemented

RETURN animations


FUNCTION ResolveEffectiveAnimationConfig(project, binding, animName) → AnimationConfig
  COMPUTE set ← project.AnimationSets[binding.AnimationSetName]
  RETURN binding.AnimationOverrides?.GetValueOrDefault(animName)
      ?? set.AnimationConfigs.GetValueOrDefault(animName, set.DefaultAnimationConfig)

---

### CaptureSession.BuildNormalAtlases (internal)
`(groupFrames: IReadOnlyList<FrameCapture>, layout: AtlasLayout, options: NormalMapOptions) → IReadOnlyList<byte[]>`

// For each captured frame with depth data, compute normal map bytes; fall back to flat blue normal
// for frames without depth (preserves atlas layout integrity).
CREATE normalFrameCaptures ← new List<FrameCapture>()
FOREACH frame IN groupFrames
  IF frame.DepthData is null
    CREATE neutral ← new byte[frame.Width * frame.Height * 4]
    ITERATE i FROM 0 TO neutral.Length / 4 - 1
      neutral[i*4+0] ← 128                                                        // X = 0
      neutral[i*4+1] ← 128                                                        // Y = 0
      neutral[i*4+2] ← 255                                                        // Z = 1
      neutral[i*4+3] ← 255
    ACCUMULATE new FrameCapture(neutral, null, frame.Width, frame.Height,
        frame.VariantName, frame.RigName, frame.AngleName, frame.AnimationName,
        frame.FrameIndex, frame.NormalizedTime) to normalFrameCaptures
  ELSE
    COMPUTE normalBytes ← DepthToNormal.Generate(frame.DepthData, frame.Width, frame.Height, options)   // sprite-theory
    ACCUMULATE new FrameCapture(normalBytes, null, frame.Width, frame.Height,
        frame.VariantName, frame.RigName, frame.AngleName, frame.AnimationName,
        frame.FrameIndex, frame.NormalizedTime) to normalFrameCaptures

// Assemble using the same layout as the color atlas (same placements)
RETURN AtlasAssembler.Assemble(normalFrameCaptures, layout, Color.Transparent)   // sprite-theory

---

### CaptureSession.RequestPause
`() → void`

IF State == Running
  SET IsPauseRequested ← true
  SET resumeSignal ← new TaskCompletionSource<bool>()                             // Block next pauseSignal() call
  SET State ← CaptureState.Paused
  Fire StateChanged(Paused)

---

### CaptureSession.Resume
`() → void`

IF State == Paused
  SET IsPauseRequested ← false
  resumeSignal.TrySetResult(true)                                                 // Release pending awaits
  SET State ← CaptureState.Running
  Fire StateChanged(Running)

---

### AnimationBrowser.RefreshAsync
`(model: IModelHandle, bridge: ISpriteComposerBridge, ct: CancellationToken) → Task`

COMPUTE animations ← bridge.GetAvailableAnimations(model)
SetAvailableAnimations(animations)

---

### AnimationBrowser.SetAvailableAnimations (internal)
`(animations: IReadOnlyList<AnimationInfo>) → void`

SET this.AvailableAnimations ← animations
ApplyFilter()

---

### AnimationBrowser.UpdateEffectiveSelectionFor
`(binding: VariantBinding, set: AnimationSet) → void`

COMPUTE effective ← set.SelectedAnimationNames.Except(binding.ExcludedAnimations ?? []).ToHashSet()
SET this.EffectiveSelection ← effective

---

### AnimationBrowser.SetFilter
`(filter: AnimationFilter) → void`

SET this.Filter ← filter
ApplyFilter()

---

### AnimationBrowser.ApplyFilter (internal)
`() → void`

COMPUTE filtered ← this.AvailableAnimations
IF Filter.NameContains is not null
  filtered ← filtered.Where(a => a.Name.Contains(Filter.NameContains, StringComparison.OrdinalIgnoreCase))
IF Filter.NamePrefix is not null
  filtered ← filtered.Where(a => a.Name.StartsWith(Filter.NamePrefix, StringComparison.OrdinalIgnoreCase))
IF Filter.NameSuffix is not null
  filtered ← filtered.Where(a => a.Name.EndsWith(Filter.NameSuffix, StringComparison.OrdinalIgnoreCase))
IF Filter.MinDuration is not null
  filtered ← filtered.Where(a => a.Duration >= Filter.MinDuration)
IF Filter.MaxDuration is not null
  filtered ← filtered.Where(a => a.Duration <= Filter.MaxDuration)
IF Filter.LoopingOnly is not null
  filtered ← filtered.Where(a => a.IsLooping == Filter.LoopingOnly)
IF Filter.MaxResults is not null
  filtered ← filtered.Take(Filter.MaxResults)
SET this.FilteredAnimations ← filtered.OrderBy(a => a.Name).ToList()

---

### AnimationBrowser.LookupAnimationInfo
`(name: string) → AnimationInfo?`

RETURN AvailableAnimations.FirstOrDefault(a => a.Name == name)

---

### EquipmentManager.Attach / Detach / Get / IsOccupied / Clear
`(...) → void / (...)? / bool`

Attach: SET Attachments[slotName] ← (slot, handle)
Detach: SET handle ← Attachments[slotName].Handle; REMOVE Attachments[slotName]; RETURN handle
Get: RETURN Attachments.GetValueOrDefault(slotName)
IsOccupied: RETURN Attachments.ContainsKey(slotName)
Clear: Attachments.Clear()

---

### SpritePreview.Create
`(variantName: string, spriteSheet: SpriteSheet, atlases: IReadOnlyList<byte[]>) → SpritePreview`

VALIDATE spriteSheet.Animations.Count > 0            → ArgumentException("No animations")
COMPUTE firstAnim ← spriteSheet.Animations[0]
VALIDATE firstAnim.AngleFrameMap.Count > 0           → ArgumentException("Animation has no angles")
COMPUTE firstAngle ← firstAnim.AngleFrameMap.Keys.OrderBy(a => a).First()

CREATE preview ← new SpritePreview()
SET preview.VariantName ← variantName
SET preview.SpriteSheet ← spriteSheet
SET preview.Atlases ← atlases
SET preview.CurrentAnimation ← firstAnim.Name
SET preview.CurrentAngle ← firstAngle
SET preview.CurrentFrameIndex ← 0
SET preview.PlaybackSpeed ← 1.0f
SET preview.State ← PreviewState.Stopped
SET preview.TargetFps ← 12.0f
RETURN preview

---

### SpritePreview.SwitchAnimation / SwitchAngle / StepForward / StepBackward / JumpToFrame

SwitchAnimation:
  VALIDATE SpriteSheet.Animations.Any(a => a.Name == name)   → ArgumentException
  SET CurrentAnimation ← name
  SET CurrentFrameIndex ← 0
  COMPUTE anim ← SpriteSheet.Animations.First(a => a.Name == name)
  IF NOT anim.AngleFrameMap.ContainsKey(CurrentAngle)
    SET CurrentAngle ← anim.AngleFrameMap.Keys.OrderBy(a => a).First()
  Fire AnimationChanged(name); Fire FrameChanged(0)

SwitchAngle:
  COMPUTE anim ← SpriteSheet.Animations.First(a => a.Name == CurrentAnimation)
  VALIDATE anim.AngleFrameMap.ContainsKey(name)    → ArgumentException
  SET CurrentAngle ← name; SET CurrentFrameIndex ← 0
  Fire AngleChanged(name); Fire FrameChanged(0)

StepForward:
  COMPUTE anim ← SpriteSheet.Animations.First(a => a.Name == CurrentAnimation)
  COMPUTE indices ← anim.AngleFrameMap[CurrentAngle]
  COMPUTE next ← CurrentFrameIndex + 1
  IF next >= indices.Length
    IF anim.LoopMode == Loop: next ← 0
    ELSE IF anim.LoopMode == PingPong: (PreviewController handles direction flag)
    ELSE: RETURN
  SET CurrentFrameIndex ← next
  Fire FrameChanged(next)

StepBackward: symmetric with min-clamp

JumpToFrame:
  VALIDATE index in bounds                           → ArgumentOutOfRangeException
  SET CurrentFrameIndex ← index
  Fire FrameChanged(index)

---

### SpritePreview.GetCurrentSpriteFrame
`() → SpriteFrame`

COMPUTE anim ← SpriteSheet.Animations.First(a => a.Name == CurrentAnimation)
COMPUTE indices ← anim.AngleFrameMap[CurrentAngle]
COMPUTE frameGlobalIndex ← indices[CurrentFrameIndex]
RETURN SpriteSheet.Frames[frameGlobalIndex]

---

### PreviewController.Play / Pause / Stop / SetSpeed

Play:
  IF Preview.State == Playing: RETURN
  SET Preview.State ← Playing
  COMPUTE intervalMs ← 1000 / (Preview.TargetFps * Preview.PlaybackSpeed)
  SET playbackTimer ← new Timer(callback: () => Preview.StepForward(), intervalMs)
  Fire StateChanged(Playing)

Pause:
  IF Preview.State != Playing: RETURN
  playbackTimer?.Dispose(); SET playbackTimer ← null
  SET Preview.State ← Paused
  Fire StateChanged(Paused)

Stop:
  playbackTimer?.Dispose(); SET playbackTimer ← null
  SET Preview.State ← Stopped
  Preview.JumpToFrame(0)
  Fire StateChanged(Stopped)

SetSpeed:
  VALIDATE multiplier > 0                            → ArgumentException
  SET Preview.PlaybackSpeed ← multiplier
  IF Preview.State == Playing: Pause(); Play()

---

### ProjectSerializer.Serialize
`(project: SpriteProject) → string`

COMPUTE options ← new JsonSerializerOptions {
  PropertyNamingPolicy: JsonNamingPolicy.CamelCase,
  WriteIndented: true,
  DefaultIgnoreCondition: JsonIgnoreCondition.WhenWritingNull
}
RETURN JsonSerializer.Serialize(project, options)

---

### ProjectSerializer.Deserialize
`(json: string) → SpriteProject`

COMPUTE options ← new JsonSerializerOptions {
  PropertyNamingPolicy: JsonNamingPolicy.CamelCase,
  PropertyNameCaseInsensitive: true
}
COMPUTE project ← JsonSerializer.Deserialize<SpriteProject>(json, options)
VALIDATE project is not null                         → JsonException("Failed to deserialize")
VALIDATE project.SchemaVersion == "1.0"              → NotSupportedException($"Unsupported schema version: {project.SchemaVersion}")
// Referential integrity check on VariantBinding.AnimationSetName
FOREACH binding IN project.Variants
  VALIDATE project.AnimationSets.ContainsKey(binding.AnimationSetName)
    → InvalidDataException($"Variant '{binding.Variant.Name}' binds to unknown AnimationSet '{binding.AnimationSetName}'")
RETURN project

---

### ExportPipeline.ExportAsync
`(result: CaptureResult, project: SpriteProject, ct: CancellationToken) → Task`

ValidatePatterns(project.ExportOptions)                                          // Fails fast if {variant}/{rig} missing
VALIDATE project.ExportOptions.AtlasEncoder is not null    → InvalidOperationException("AtlasEncoder required")
IF NOT Directory.Exists(project.ExportOptions.OutputDirectory)
  Directory.CreateDirectory(project.ExportOptions.OutputDirectory)

FOREACH groupOutput IN result.PerGroupOutputs
  IF project.ExportOptions.RigsToExport is not null AND groupOutput.Rig.Name not in project.ExportOptions.RigsToExport
    CONTINUE

  // Export atlas images
  ITERATE atlasIdx FROM 0 TO groupOutput.AtlasImages.Count - 1
    COMPUTE atlasBytes ← groupOutput.AtlasImages[atlasIdx]
    COMPUTE info ← groupOutput.SpriteSheet.Atlases[atlasIdx]
    COMPUTE encoded ← project.ExportOptions.AtlasEncoder.EncodeRgba(atlasBytes, info.Width, info.Height)
    COMPUTE fullPath ← Path.Combine(project.ExportOptions.OutputDirectory, info.Filename)
    await File.WriteAllBytesAsync(fullPath, encoded, ct)

  // Export normal maps (if present)
  IF groupOutput.NormalAtlases is not null
    ITERATE atlasIdx FROM 0 TO groupOutput.NormalAtlases.Count - 1
      COMPUTE normalBytes ← groupOutput.NormalAtlases[atlasIdx]
      COMPUTE info ← groupOutput.SpriteSheet.Atlases[atlasIdx]
      COMPUTE normalFilename ← ResolveFilename(
          project.ExportOptions.NormalMapFilenamePattern,
          groupOutput.VariantName, groupOutput.Rig.Name, atlasIdx)
      COMPUTE encoded ← project.ExportOptions.AtlasEncoder.EncodeRgba(normalBytes, info.Width, info.Height)
      COMPUTE fullPath ← Path.Combine(project.ExportOptions.OutputDirectory, normalFilename)
      await File.WriteAllBytesAsync(fullPath, encoded, ct)

  // Export metadata JSON
  COMPUTE metadataFilename ← ResolveFilename(
      project.ExportOptions.MetadataFilenamePattern,
      groupOutput.VariantName, groupOutput.Rig.Name, 0)
  COMPUTE metadataJson ← SpriteSheetSerializer.Serialize(groupOutput.SpriteSheet)   // sprite-theory
  COMPUTE fullPath ← Path.Combine(project.ExportOptions.OutputDirectory, metadataFilename)
  await File.WriteAllTextAsync(fullPath, metadataJson, ct)

  Fire ExportCompleted(groupOutput.VariantName, groupOutput.Rig.Name)

// AllExportsCompleted fires at the composer level (not this helper)

---

### ExportPipeline.ResolveFilename
`(pattern: string, variant: string, rig: string, atlasIndex: int) → string`

COMPUTE result ← pattern
result ← result.Replace("{variant}", KebabCase(variant))
result ← result.Replace("{rig}", KebabCase(rig))
result ← result.Replace("{atlas}", atlasIndex.ToString())
RETURN result

FUNCTION KebabCase(s: string) → string:
  RETURN s.ToLowerInvariant().Replace('_', '-')

---

### ExportPipeline.ValidatePatterns
`(options: ExportOptions) → void`

FUNCTION RequirePlaceholder(pattern, placeholder, patternName):
  IF NOT pattern.Contains(placeholder)
    THROW InvalidOperationException($"ExportOptions.{patternName} missing required placeholder '{placeholder}' — multi-variant export would overwrite files.")

RequirePlaceholder(options.AtlasFilenamePattern, "{variant}", "AtlasFilenamePattern")
RequirePlaceholder(options.AtlasFilenamePattern, "{rig}", "AtlasFilenamePattern")
RequirePlaceholder(options.NormalMapFilenamePattern, "{variant}", "NormalMapFilenamePattern")
RequirePlaceholder(options.NormalMapFilenamePattern, "{rig}", "NormalMapFilenamePattern")
RequirePlaceholder(options.MetadataFilenamePattern, "{variant}", "MetadataFilenamePattern")
RequirePlaceholder(options.MetadataFilenamePattern, "{rig}", "MetadataFilenamePattern")

---

### CommandStack.Execute
`(command: IEditorCommand, allowMerge: bool = true) → void`

VALIDATE command is not null                         → ArgumentNullException
command.Execute()
IF activeCompound is not null
  activeCompound.Add(command)
  Fire CommandExecuted(command, ExecutionType.Execute)
  RETURN
COMPUTE now ← UtcNow
IF allowMerge AND undoStack.Count > 0 AND (now - lastCommandTime) < mergeWindow
  COMPUTE last ← undoStack[undoStack.Count - 1]
  IF last.CanMergeWith(command) AND last.TryMerge(command)
    SET lastCommandTime ← now
    Fire CommandExecuted(command, ExecutionType.Merged)
    Fire StateChanged
    RETURN
redoStack.Clear()
undoStack.Add(command)
SET lastCommandTime ← now
WHILE undoStack.Count > maxDepth
  undoStack.RemoveAt(0)
Fire CommandExecuted(command, ExecutionType.Execute)
Fire StateChanged

---

### CommandStack.Undo / Redo
`() → bool`

Undo:
  IF undoStack.Count == 0 OR activeCompound is not null: RETURN false
  COMPUTE command ← undoStack[undoStack.Count - 1]
  undoStack.RemoveAt(undoStack.Count - 1)
  command.Undo()
  redoStack.Add(command)
  Fire CommandUndone(command); Fire StateChanged
  RETURN true

Redo:
  IF redoStack.Count == 0 OR activeCompound is not null: RETURN false
  COMPUTE command ← redoStack[redoStack.Count - 1]
  redoStack.RemoveAt(redoStack.Count - 1)
  command.Execute()
  undoStack.Add(command)
  Fire CommandRedone(command); Fire StateChanged
  RETURN true

---

### CommandStack.BeginCompound / EndCompound (internal) / BreakMerge / Clear

BeginCompound(description):
  VALIDATE description is not null
  INCREMENT compoundDepth
  IF activeCompound is null
    SET activeCompound ← new CompoundCommand(description)
  RETURN new CompoundScope(this)

EndCompound (called by CompoundScope.Dispose):
  IF compoundDepth == 0: RETURN
  DECREMENT compoundDepth
  IF compoundDepth == 0 AND activeCompound is not null
    IF activeCompound.Commands.Count > 0
      redoStack.Clear()
      undoStack.Add(activeCompound)
      WHILE undoStack.Count > maxDepth: undoStack.RemoveAt(0)
      SET lastCommandTime ← UtcNow
      Fire StateChanged
    SET activeCompound ← null

BreakMerge: SET lastCommandTime ← DateTime.MinValue
Clear: undoStack.Clear(); redoStack.Clear(); activeCompound ← null; compoundDepth ← 0; lastCommandTime ← DateTime.MinValue; Fire StateChanged

---

## Commands

All commands follow the same shape: `Execute` applies the change, `Undo` reverses it, `CanMergeWith`/`TryMerge` determine merge-window coalescence. Only the command-specific logic is shown.

### Project-level commands

**AddRigCommand** / **RemoveRigCommand** / **ModifyRigCommand**: Update `project.Rigs`. Merge only for `ModifyRigCommand` targeting the same rig name.

**AddAnimationSetCommand(composer, set)**:
```
Execute: SET project.AnimationSets[set.Name] ← set; IsDirty ← true; Fire AnimationSetAdded(set.Name)
Undo: REMOVE project.AnimationSets[set.Name]; IsDirty ← true; Fire AnimationSetRemoved(set.Name)
CanMergeWith / TryMerge: RETURN false
```

**RemoveAnimationSetCommand(composer, setName)**:
```
Execute:
  VALIDATE project.Variants.None(v => v.AnimationSetName == setName)   // Guard against orphaning variants
  SET removedSet ← project.AnimationSets[setName]                      // Save for undo
  REMOVE project.AnimationSets[setName]; IsDirty ← true; Fire AnimationSetRemoved(setName)
Undo: SET project.AnimationSets[setName] ← removedSet; IsDirty ← true; Fire AnimationSetAdded(setName)
CanMergeWith / TryMerge: RETURN false
```

**ModifyAnimationSetCommand(composer, setName, oldSet, newSet)**: Update one set in place. Merge-eligible against other `ModifyAnimationSetCommand` targeting same set name.

**AddVariantCommand(composer, binding)**:
```
Execute:
  VALIDATE binding.AnimationSetName in project.AnimationSets
  VALIDATE no existing variant has this name
  SET project.Variants ← project.Variants + binding; IsDirty ← true; Fire VariantAdded(binding)
Undo: REMOVE binding from project.Variants; IsDirty ← true; Fire VariantRemoved(binding.Variant.Name)
CanMergeWith / TryMerge: RETURN false
```

**RemoveVariantCommand(composer, variantName)**:
```
Execute:
  SET removedBinding ← project.Variants.First(v => v.Variant.Name == variantName)   // Save for undo
  SET removedIndex ← project.Variants.IndexOf(removedBinding)
  REMOVE binding from project.Variants; IsDirty ← true; Fire VariantRemoved(variantName)
  IF ActiveVariantName == variantName: CLEAR active state via ClearActiveVariant()
Undo: SET project.Variants ← Variants.Insert(removedIndex, removedBinding); IsDirty ← true; Fire VariantAdded(removedBinding)
CanMergeWith / TryMerge: RETURN false
```

### Variant-level commands

**SetVariantModelCommand(composer, variantName, oldModel: AssetReference, newModel: AssetReference)**:
```
Execute:
  COMPUTE binding ← locate by variantName
  SET project.Variants ← Variants with binding.Variant.Model = newModel; IsDirty ← true
  // Bridge work only when active:
  IF composer.ActiveVariantName == variantName AND composer.Bridge is not null
    composer.Bridge.DisposeModel(composer.ModelHandle)
    SET composer.ModelHandle ← await composer.Bridge.LoadModelAsync(newModel, default)
    composer.Bridge.SetModelScale(composer.ModelHandle, binding.Variant.Scale)
    composer.AnimationBrowser.SetAvailableAnimations(composer.Bridge.GetAvailableAnimations(composer.ModelHandle))
    Fire ModelChanged(variantName, newModel)
Undo: symmetric with oldModel
CanMergeWith / TryMerge: RETURN false
```

**SetVariantScaleCommand(composer, variantName, oldScale, newScale)**: Merge-eligible against same variant.

```
Execute:
  SET project.Variants ← Variants with binding.Variant.Scale = newScale; IsDirty ← true
  IF composer.ActiveVariantName == variantName AND composer.Bridge is not null
    composer.Bridge.SetModelScale(composer.ModelHandle, newScale)
Undo: symmetric with oldScale
```

**SetVariantPivotOverrideCommand(composer, variantName, oldPivot?, newPivot?)**: Merge-eligible against same variant.

```
Execute: SET binding.Variant.PivotOverride = newPivot; IsDirty ← true
Undo: SET binding.Variant.PivotOverride = oldPivot; IsDirty ← true
```

**SetVariantAnchorBoneNameCommand(composer, variantName, oldBone?, newBone?)**: Non-merging structural change.

```
Execute: SET binding.Variant.AnchorBoneName = newBone; IsDirty ← true
Undo: SET binding.Variant.AnchorBoneName = oldBone; IsDirty ← true
```

**AttachEquipmentCommand(composer, variantName, slot)**:
```
Execute:
  COMPUTE binding ← locate by variantName
  IF composer.ActiveVariantName == variantName AND composer.Bridge is not null
    IF composer.EquipmentManager.IsOccupied(slot.SlotName)
      SAVE previousSlot; bridge.DetachEquipment(...); EquipmentManager.Detach(...)
    SET handle ← await bridge.AttachEquipmentAsync(composer.ModelHandle, slot, default)
    composer.EquipmentManager.Attach(slot.SlotName, slot, handle)
  SET binding.Variant.Equipment ← Equipment with slot added (replacing existing by SlotName)
  IsDirty ← true; Fire EquipmentAttached(variantName, slot)
Undo: symmetric — detach the attached, re-attach previousSlot if any
CanMergeWith / TryMerge: RETURN false
```

**DetachEquipmentCommand(composer, variantName, slotName)**:
```
Execute:
  COMPUTE existing ← binding.Variant.Equipment.First(e => e.SlotName == slotName)
  SET detachedSlot ← existing
  IF composer.ActiveVariantName == variantName AND composer.Bridge is not null
    COMPUTE handle ← composer.EquipmentManager.Get(slotName).Handle
    composer.Bridge.DetachEquipment(composer.ModelHandle, handle)
    composer.EquipmentManager.Detach(slotName)
  SET binding.Variant.Equipment ← Equipment without detachedSlot
  IsDirty ← true; Fire EquipmentDetached(variantName, slotName)
Undo: symmetric — re-attach detachedSlot
CanMergeWith / TryMerge: RETURN false
```

**SetMaterialOverrideCommand(composer, variantName, materialSlot, oldMaterial: AssetReference?, newMaterial: AssetReference?)**: Merge-eligible against same (variantName, materialSlot).

```
Execute:
  SET binding.Variant.MaterialOverrides[materialSlot] ← newMaterial (add/update/remove)
  IF composer.ActiveVariantName == variantName AND composer.Bridge is not null
    composer.Bridge.SetMaterialOverride(composer.ModelHandle, materialSlot, newMaterial)
  IsDirty ← true
Undo: symmetric with oldMaterial
```

**BindVariantToAnimationSetCommand(composer, variantName, newSetName)**: Non-merging.

```
Execute: SAVE oldSetName; SET binding.AnimationSetName ← newSetName; IsDirty ← true
Undo: SET binding.AnimationSetName ← oldSetName
```

**SetVariantAnimationOverrideCommand(composer, variantName, animName, newConfig: AnimationConfig?)**: Merge-eligible against same (variantName, animName).

```
Execute:
  SAVE oldConfig ← binding.AnimationOverrides?.GetValueOrDefault(animName)
  SET binding.AnimationOverrides[animName] ← newConfig (add/update/remove-when-null)
  IsDirty ← true
Undo: restore oldConfig (either add back or remove if originally absent)
```

**SetVariantExcludedAnimationsCommand(composer, variantName, newExcluded)**: Merge-eligible against same variant.

```
Execute: SAVE oldExcluded ← binding.ExcludedAnimations; SET binding.ExcludedAnimations ← newExcluded; IsDirty ← true
Undo: SET binding.ExcludedAnimations ← oldExcluded
```

---

## Algorithms

### Capture Orchestration Loop

**Purpose**: Iterate every combination of (variant, rig, angle, animation, frame) and capture a frame for each, with per-frame error isolation and cooperative pause/cancel.

**Complexity**: O(V × R × A × N × F) time where V = variants, R = rigs, A = angles/rig, N = effective animations/variant, F = frames/animation. Each iteration is bounded by the bridge's `CaptureFrameAsync` latency (typically 40–70 ms for Stride).

**Reference**: Standard nested-loop traversal with cooperative cancellation token and `TaskCompletionSource`-based pause. The per-frame try-catch pattern is the canonical IMPLEMENTATION TENETS per-item error isolation model.

INPUT: SpriteProject, ISpriteComposerBridge, CancellationToken
OUTPUT: CaptureResult with captured frames (+ per-angle pivots), per-group assembled outputs, per-frame errors

```
State ← Running
FOREACH variant:
  handle ← bridge.LoadModelAsync(variant.Model)
  apply scale + equipment + material overrides
  modelBounds ← bridge.GetModelBounds(handle)
  FOREACH rig:
    await bridge.BeginRigAsync(rig)
    FOREACH angle:
      await pauseSignal()
      await bridge.BeginAngleAsync(angle)
      orthoParams ← OrthographicSetup.Compute(angle, modelBounds, rig.FrameSize)      // sprite-theory
      pivot ← ResolvePivot(variant, bridge, handle, modelBounds, orthoParams)
      bridge.ConfigureCamera(orthoParams, rig.FrameSize)
      FOREACH effective animName (sorted for determinism):
        sequence ← AnimationSampling.GenerateFromConfig(animInfo, effectiveConfig)    // sprite-theory
        bridge.SetAnimation(handle, animName)
        ITERATE frameIndex FROM 0 TO sequence.FrameCount - 1:
          await pauseSignal()
          bridge.SetAnimationTime(handle, sequence.Timestamps[frameIndex])
          Try:
            capture ← await bridge.CaptureFrameAsync(
                variantName, rigName, angleName, animName, frameIndex, timestamp,
                rig.IncludeNormalMap AND bridge.SupportsDepthCapture, ct)
            CapturedFrames.Add((capture, pivot))
          Catch (OperationCanceledException): RETHROW
          Catch (Exception): record CaptureError, continue
          ReportProgress (throttled to Options.CaptureProgressIntervalMs)
      await bridge.EndAngleAsync(angle)
    await bridge.EndRigAsync(rig)
  detach equipment + dispose model

// Assembly phase per (variantName, rigName) group (deterministic, engine-independent)
FOREACH (variantName, rigName) grouping:
  atlasLayout ← AtlasPacker.Pack(frame dimensions, AtlasOptions)                      // sprite-theory
  mirrors ← MirrorOptimizer.ComputeMirrors(rig)                                       // sprite-theory
  mirrorFrames ← MirrorOptimizer.GenerateMirrorFrames(capturedFrames, mirrors)        // sprite-theory
  atlasImages ← AtlasAssembler.Assemble(groupFrames, atlasLayout, rig.BackgroundColor) // sprite-theory
  spriteSheet ← compose metadata from layout + frames + per-angle pivots + animations

State ← Completed
```

**Termination guarantee**: The outer loops have finite bounds (finite variants, rigs, angles, animations, frame counts). The inner await points respect the CancellationToken. Even with frame errors, the loop always progresses — each iteration either captures a frame or records an error.

**Pause semantics**: `pauseSignal()` awaits the current `resumeSignal.Task`. `RequestPause` creates a fresh incomplete TCS; `Resume` calls `TrySetResult(true)` on the current TCS. Cancellation propagates through `Task.WaitAsync(ct)`.

**Per-angle pivot**: `ResolvePivot` is called once per angle per variant and the returned pivot is stored alongside each `FrameCapture` in `CapturedFrames`. This is structurally required — even single-pitch rigs have different `orthoHeight` per angle due to the convex hull of the bounding-box corners on the camera plane. See sprite-theory's PivotComputer tests for the regression evidence.

### Command Merge Window

**Purpose**: Collapse multiple same-type same-target commands emitted within a short time window (e.g., slider drag) into a single undoable entry.

**Complexity**: O(1) per command — single comparison with the top of the undo stack.

**Reference**: Standard undo-stack merging pattern from interactive editors (Adobe Photoshop, Figma, scene-composer).

INPUT: incoming IEditorCommand, current undo stack, last command timestamp
OUTPUT: either a new undo entry or a merged top-of-stack entry

```
IF undoStack not empty AND (now - lastCommandTime) < mergeWindow:
  top ← undoStack.Peek
  IF top.CanMergeWith(incoming) AND top.TryMerge(incoming):
    lastCommandTime ← now
    RETURN
undoStack.Push(incoming)
lastCommandTime ← now
```

**Merge-safe command types**: `ModifyRigCommand`, `ModifyAnimationSetCommand`, `SetVariantScaleCommand`, `SetVariantPivotOverrideCommand`, `SetMaterialOverrideCommand`, `SetVariantAnimationOverrideCommand`, `SetVariantExcludedAnimationsCommand`.

**Non-merge command types** (structural changes): `AddRigCommand`, `RemoveRigCommand`, `AddAnimationSetCommand`, `RemoveAnimationSetCommand`, `AddVariantCommand`, `RemoveVariantCommand`, `SetVariantModelCommand`, `SetVariantAnchorBoneNameCommand`, `AttachEquipmentCommand`, `DetachEquipmentCommand`, `BindVariantToAnimationSetCommand`.

---

## Serialization Formats

### Sprite Project (.spriteproj.json)

**Purpose**: Complete multi-variant project configuration, version-controllable, shareable across team members and SpriteBatcher.
**Encoding**: UTF-8 JSON via `System.Text.Json`.
**Property naming**: camelCase (matches sprite-theory's `SpriteSheetSerializer` convention).
**Formatting**: Indented, null values omitted (`JsonIgnoreCondition.WhenWritingNull`).

Structure (Defenders heroes project example, abridged):

```json
{
  "name": "Heroes",
  "rigs": [
    { "name": "TopDown-8Dir", "projection": "Orthographic",
      "angles": [ ... 5 entries: N, NE, E, SE, S ... ],
      "frameSize": {"width": 96, "height": 96}, "padding": 2,
      "backgroundColor": {"r": 0, "g": 0, "b": 0, "a": 0},
      "includeNormalMap": true, "trimTransparent": false },
    { "name": "SideView-Brawler", "projection": "Orthographic",
      "angles": [ { "name": "right", "yaw": 90, "pitch": 0,
                    "producesMirror": true, "mirrorTargetName": "left" } ],
      "frameSize": {"width": 128, "height": 128}, "padding": 2,
      "backgroundColor": {"r": 0, "g": 0, "b": 0, "a": 0},
      "includeNormalMap": true, "trimTransparent": false }
  ],
  "animationSets": {
    "humanoid-combat": {
      "name": "humanoid-combat",
      "selectedAnimationNames": [
        "attack_heavy", "attack_light", "block", "death", "dodge",
        "hit_reaction", "idle", "run", "walk" ],
      "animationConfigs": {
        "attack_heavy": { "frameCount": 16, "speedMultiplier": 1.0,
                          "trimStart": 0.0, "trimEnd": 1.0, "loopMode": "None" }
      },
      "defaultAnimationConfig": { "frameCount": 8, "speedMultiplier": 1.0,
                                   "trimStart": 0.0, "trimEnd": 1.0, "loopMode": "None" }
    }
  },
  "variants": [
    {
      "variant": {
        "name": "gee",
        "model": { "bundleId": "characters.synty", "assetId": "Gee_Hunter" },
        "equipment": [
          { "slotName": "weapon_r", "mesh": { "bundleId": "weapons.synty", "assetId": "Bow_Compound" }, "boneName": "RightHand" },
          { "slotName": "quiver", "mesh": { "bundleId": "weapons.synty", "assetId": "Quiver_Basic" }, "boneName": "Spine2" }
        ],
        "materialOverrides": null,
        "scale": 1.0,
        "pivotOverride": null,
        "anchorBoneName": "Hips"
      },
      "animationSetName": "humanoid-combat",
      "animationOverrides": null,
      "excludedAnimations": null
    },
    {
      "variant": {
        "name": "vu-ud",
        "model": { "bundleId": "characters.synty", "assetId": "VuUd_Monk" },
        "equipment": [],
        "materialOverrides": null,
        "scale": 1.0,
        "pivotOverride": null,
        "anchorBoneName": "Hips"
      },
      "animationSetName": "humanoid-combat",
      "animationOverrides": {
        "attack_heavy": { "frameCount": 20, "speedMultiplier": 0.8,
                          "trimStart": 0.1, "trimEnd": 0.9, "loopMode": "None" }
      },
      "excludedAnimations": null
    }
  ],
  "exportOptions": {
    "outputDirectory": "./assets/sprites/heroes",
    "atlasFilenamePattern": "{variant}_{rig}_{atlas}.png",
    "normalMapFilenamePattern": "{variant}_{rig}_{atlas}_normal.png",
    "metadataFilenamePattern": "{variant}_{rig}.json",
    "rigsToExport": null
  },
  "normalMapOptions": null,
  "customProperties": null,
  "schemaVersion": "1.0"
}
```

**Note**: `exportOptions.atlasEncoder` is NOT serialized. It is a runtime-only field supplied by the hosting application (e.g., the Stride bridge registers its own encoder when the composer is created).

**Referential integrity on deserialize**: every `variants[i].animationSetName` must exist as a key in `animationSets`. `ProjectSerializer.Deserialize` validates this and throws `InvalidDataException` on mismatch.

The format depends on types from sprite-theory (`CameraRig`, `CaptureAngle`, `AnimationConfig`, `CharacterVariant`, `EquipmentSlot`, `AssetReference`, `Color`, `NormalMapOptions`, `Vector2`) which serialize per their own records' property layouts.

**No custom binary format.** All serialization is JSON.

---
