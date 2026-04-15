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
| Public Types | ~42 (7 classes, 2 records, 4 interfaces, 3 enums, 1 static, 13 command classes, ~12 event args / records) |
| Public Methods | ~65 |
| Dependencies | sprite-theory |
| Deterministic | Yes for orchestration and assembly; bridge-dependent for pixel output |
| Allocation-Free Hot Paths | None (captures allocate FrameCapture, events allocate args) |

---

## Data Structures

### SpriteComposer (Orchestrator)

**Kind**: Class
**Thread Safety**: Single-threaded (UI thread); capture progress events must be marshaled to UI thread by consumer

| Field | Type | Purpose |
|-------|------|---------|
| `Project` | `SpriteProject?` | Current project. Null between NewProject/LoadProject and after project close. |
| `Bridge` | `ISpriteComposerBridge?` | Engine bridge. Null in headless mode. |
| `ModelHandle` | `IModelHandle?` | Currently loaded model. Null between model operations. |
| `EquipmentManager` | `EquipmentManager` | Attachment tracking |
| `AnimationBrowser` | `AnimationBrowser` | Available/filtered/selected animations |
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
| `ModelChanged` | `ModelChangedEventArgs` | After SetModelCommand executes or undoes |
| `EquipmentAttached` | `EquipmentEventArgs` | After AttachEquipmentCommand executes |
| `EquipmentDetached` | `EquipmentEventArgs` | After DetachEquipmentCommand executes |
| `CaptureStarted` | `CaptureStartedEventArgs` | CaptureSession transitions Idle → Running |
| `CaptureProgress` | `CaptureProgressEventArgs` | Throttled to 100ms during Running |
| `CaptureCompleted` | `CaptureCompletedEventArgs` | CaptureSession transitions to Completed |
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

**Kind**: Interface
**Thread Safety**: Implementation-defined (composer calls on a single thread; bridge may internally async-dispatch)

| Method | Signature | Purpose |
|--------|-----------|---------|
| `LoadModelAsync` | `(string path, CancellationToken) → Task<IModelHandle>` | Load 3D model into engine scene |
| `DisposeModel` | `(IModelHandle) → void` | Unload and free engine resources (idempotent) |
| `GetModelBounds` | `(IModelHandle) → BoundingBox` | Axis-aligned bounding box in world units |
| `SetModelScale` | `(IModelHandle, float) → void` | Apply uniform scale |
| `AttachEquipmentAsync` | `(IModelHandle, EquipmentSlot, CancellationToken) → Task<IEquipmentHandle>` | Parent mesh to skeleton bone |
| `DetachEquipment` | `(IModelHandle, IEquipmentHandle) → void` | Remove attachment |
| `SetMaterialOverride` | `(IModelHandle, string slot, string? path) → void` | Override material; null restores original |
| `GetAvailableAnimations` | `(IModelHandle) → IReadOnlyList<AnimationInfo>` | Enumerate animations on skeleton |
| `SetAnimation` | `(IModelHandle, string name) → void` | Set active clip (scrubbable, no auto-playback) |
| `SetAnimationTime` | `(IModelHandle, float normalizedTime) → void` | Seek clip; pose updates synchronously |
| `ConfigureCamera` | `(OrthographicParameters, (int, int) frameSize) → void` | Apply capture camera parameters |
| `CaptureFrameAsync` | `(string anim, string angle, int frameIndex, float normalizedTime, bool captureDepth, CancellationToken) → Task<FrameCapture>` | Render + readback |
| `SetPreviewCamera` | `(float yaw, float pitch, float distance) → void` | Position orbit preview camera |
| `SetPreviewAnimationPlayback` | `(bool playing, float normalizedSpeed) → void` | Start/stop real-time preview playback |
| `Dispose` | `() → void` | Release all engine resources |

### IModelHandle / IEquipmentHandle / IAnimationHandle

**Kind**: Marker interfaces

```
IModelHandle       // Opaque — bridge-defined concrete type
IEquipmentHandle   // Opaque — bridge-defined concrete type
IAnimationHandle   // Opaque — bridge-defined concrete type (optional, used when bridges expose animations as resources)
```

No members. Exist for type safety at the composer/bridge boundary.

### SpriteProject

**Kind**: Record (immutable; modifications via commands produce new instances)

| Field | Type | Purpose |
|-------|------|---------|
| `Name` | `string` | Project identifier (often matches variant name) |
| `Variant` | `CharacterVariant` | Model path, equipment list, material overrides, scale, `PivotOverride` (from sprite-theory) |
| `Rigs` | `IReadOnlyList<CameraRig>` | Camera rigs to capture with (from sprite-theory) |
| `SelectedAnimationNames` | `IReadOnlyList<string>` | Explicit capture-list (stored sorted alphabetically for deterministic iteration) |
| `AnimationConfigs` | `IReadOnlyDictionary<string, AnimationConfig>` | Per-animation overrides keyed by animation name (from sprite-theory) |
| `DefaultAnimationConfig` | `AnimationConfig` | Defaults when no per-animation override exists |
| `ExportOptions` | `ExportOptions` | Output paths, filenames, encoder |
| `NormalMapOptions` | `NormalMapOptions?` | Null = use sprite-theory defaults |
| `CustomProperties` | `IReadOnlyDictionary<string, string>?` | Game-specific opaque metadata (propagates to SpriteSheet) |
| `SchemaVersion` | `string` | "1.0" (project file format version) |

**Pivot policy**: Per-variant pivot override lives on `CharacterVariant.PivotOverride` (sprite-theory), not on `SpriteProject`. `BuildSpriteFrames` resolves per-frame pivot as `project.Variant.PivotOverride ?? PivotComputer.ComputeFromBounds(bounds, cameraParams)` with `PivotComputer.DefaultHumanoidPivot` as the terminal fallback.

### ExportOptions

**Kind**: Record

| Field | Type | Default | Purpose |
|-------|------|---------|---------|
| `OutputDirectory` | `string` | required | Target directory for atlas + JSON files |
| `AtlasFilenamePattern` | `string` | `"{variant}_{rig}_{atlas}.png"` | Filename with placeholders |
| `NormalMapFilenamePattern` | `string` | `"{variant}_{rig}_{atlas}_normal.png"` | Normal map filename pattern |
| `MetadataFilenamePattern` | `string` | `"{variant}_{rig}.json"` | Metadata filename pattern |
| `RigsToExport` | `IReadOnlyList<string>?` | `null` | Rig name filter; null = all rigs |
| `AtlasEncoder` | `IAtlasEncoder` | required | Consumer-supplied PNG/other encoder |

### IAtlasEncoder

**Kind**: Interface

| Method | Signature | Purpose |
|--------|-----------|---------|
| `EncodeRgba` | `(byte[] rgba, int width, int height) → byte[]` | Encode raw RGBA bytes into final file bytes |

### CaptureSession

**Kind**: Class
**Thread Safety**: Single writer (the session itself); `Progress`, `State`, `CapturedFrames.Count`, `Errors.Count` safely readable from other threads

| Field | Type | Purpose |
|-------|------|---------|
| `Project` | `SpriteProject` | Immutable snapshot at session start |
| `Bridge` | `ISpriteComposerBridge` | Required for session (non-null) |
| `ModelHandle` | `IModelHandle` | Required (captured at session start) |
| `State` | `CaptureState` | Lifecycle state |
| `Progress` | `CaptureProgress` | Live progress snapshot |
| `CapturedFrames` | `List<FrameCapture>` | Accumulated captures (grown in orchestration loop) |
| `Errors` | `List<CaptureError>` | Per-frame errors recorded during capture |
| `IsPauseRequested` | `bool` | Cooperative pause flag |
| `pauseSignal` | `ManualResetEventSlim` | Internal resume coordination |

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
| `TotalFrames` | `int` | Expected total from CaptureManifest.Compute |
| `CapturedFrames` | `int` | Count of successful captures |
| `FailedFrames` | `int` | Count of per-frame errors |
| `CurrentRig` | `string` | Current rig name |
| `CurrentAnimation` | `string` | Current animation name |
| `CurrentAngle` | `string` | Current angle name |
| `CurrentFrameIndex` | `int` | Frame within the animation |
| `ElapsedMs` | `long` | Wall-clock time since session start |
| `EstimatedRemainingMs` | `long` | Linear extrapolation |

### CaptureResult

**Kind**: Record (immutable output)

| Field | Type | Purpose |
|-------|------|---------|
| `CapturedFrames` | `IReadOnlyList<FrameCapture>` | All successfully-captured frames (from sprite-theory) |
| `PerRigOutputs` | `IReadOnlyList<RigCaptureOutput>` | Per-rig assembly results |
| `Errors` | `IReadOnlyList<CaptureError>` | Per-frame errors |
| `Manifest` | `CaptureManifest` | Precomputed manifest (from sprite-theory) |

### RigCaptureOutput

**Kind**: Record

| Field | Type | Purpose |
|-------|------|---------|
| `Rig` | `CameraRig` | Source rig (from sprite-theory) |
| `AtlasImages` | `IReadOnlyList<byte[]>` | Raw RGBA bytes per atlas image |
| `NormalAtlases` | `IReadOnlyList<byte[]>?` | Raw RGBA normal maps if rig.IncludeNormalMap; null otherwise |
| `SpriteSheet` | `SpriteSheet` | Complete metadata (from sprite-theory) |

### CaptureError

**Kind**: Record

| Field | Type | Purpose |
|-------|------|---------|
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
| Running | ExecuteAsync actively iterating |
| Paused | Session paused cooperatively; resume signal awaited |
| Completed | All frames attempted; CapturedFrames finalized |
| Cancelled | Caller cancelled via CancellationToken |
| Failed | Too many per-frame errors exceeded tolerance; terminated early |

### AnimationBrowser

**Kind**: Class
**Thread Safety**: Single-threaded

| Field | Type | Purpose |
|-------|------|---------|
| `AvailableAnimations` | `IReadOnlyList<AnimationInfo>` | All animations reported by bridge |
| `Filter` | `AnimationFilter` | Active filter criteria |
| `FilteredAnimations` | `IReadOnlyList<AnimationInfo>` | AvailableAnimations after filter |
| `SelectedAnimationNames` | `IReadOnlySet<string>` | Currently selected for capture |

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

| Field | Type | Purpose |
|-------|------|---------|
| `Attachments` | `Dictionary<string, (EquipmentSlot Slot, IEquipmentHandle Handle)>` | Keyed by slot name |

### SpritePreview

**Kind**: Class

| Field | Type | Purpose |
|-------|------|---------|
| `SpriteSheet` | `SpriteSheet` | Captured metadata (from sprite-theory) |
| `Atlases` | `IReadOnlyList<byte[]>` | Raw RGBA atlas pixel data |
| `CurrentAnimation` | `string` | Active animation name |
| `CurrentAngle` | `string` | Active angle name |
| `CurrentFrameIndex` | `int` | Frame within the animation (0-based within AngleFrameMap[angle]) |
| `PlaybackSpeed` | `float` | Speed multiplier (1.0 = real-time) |
| `State` | `PreviewState` | Stopped / Playing / Paused |
| `TargetFps` | `float` | Playback rate when State == Playing |

### PreviewController

**Kind**: Class (wraps SpritePreview with transport controls)

| Field | Type | Purpose |
|-------|------|---------|
| `Preview` | `SpritePreview` | Underlying preview state |
| `playbackTimer` | `Timer?` | Internal frame advance timer when Playing |

### PreviewState

**Kind**: Enum

| Value | Meaning |
|-------|---------|
| Stopped | Frame advance halted; CurrentFrameIndex = 0 |
| Playing | Timer is advancing frames at TargetFps × PlaybackSpeed |
| Paused | Frame advance halted; CurrentFrameIndex preserved |

### IEditorCommand

**Kind**: Interface (same contract as scene-composer's IEditorCommand — separate type, same semantics)

| Member | Signature | Purpose |
|--------|-----------|---------|
| `Description` | `string` | Human-readable name |
| `Execute` | `() → void` | Apply change |
| `Undo` | `() → void` | Reverse change |
| `CanMergeWith` | `(IEditorCommand other) → bool` | Merge eligibility |
| `TryMerge` | `(IEditorCommand other) → bool` | Merge into this command, returning success |

### CommandStack

**Kind**: Class (mirror of scene-composer's CommandStack — independent type, identical semantics)

| Field | Type | Purpose |
|-------|------|---------|
| `undoStack` | `List<IEditorCommand>` | Undo history (newest at end) |
| `redoStack` | `List<IEditorCommand>` | Redo history (newest at end) |
| `maxDepth` | `int` | Capacity (from SpriteComposerOptions) |
| `activeCompound` | `CompoundCommand?` | Non-null during BeginCompound scope |
| `compoundDepth` | `int` | Nesting count for nested compound calls |
| `lastCommandTime` | `DateTime` | For merge-window comparison |
| `mergeWindow` | `TimeSpan` | Default 500ms |

**Events**: `StateChanged`, `CommandExecuted`, `CommandUndone`, `CommandRedone`.

### CompoundCommand

**Kind**: Class implements IEditorCommand

| Field | Type | Purpose |
|-------|------|---------|
| `Description` | `string` | Group description |
| `commands` | `List<IEditorCommand>` | Child commands in execution order |

`CanMergeWith` always returns false. Undo iterates children in reverse.

---

## Dependencies

| Dependency | Type | Usage |
|------------|------|-------|
| sprite-theory | SDK (project ref) | CameraRig, CaptureAngle, OrthographicSetup, AnimationSampling, AtlasPacker, MirrorOptimizer, DepthToNormal, AtlasAssembler, SpriteSheetSerializer, all metadata types, FrameCapture |
| System.Text.Json | BCL | SpriteProject serialization |
| System.IO | BCL | Project file read/write |

**No external NuGet dependencies.** PNG encoding is delegated to consumer via `IAtlasEncoder`.

---

## API Index

#### SpriteComposer

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| Create | `(SpriteComposerOptions?) → SpriteComposer` | Yes | Allocating | Headless (no bridge) |
| SetBridge | `(ISpriteComposerBridge) → void` | N/A | Free | Wire bridge after construction |
| ClearBridge | `() → void` | N/A | Free | Detach bridge; disposes model + equipment first |
| NewProject | `(CharacterVariant, IReadOnlyList<CameraRig>) → void` | Yes | Allocating | Replace current project |
| LoadProjectAsync | `(string path, CancellationToken) → Task` | Yes | Allocating | Read JSON, deserialize, replace project |
| SaveProjectAsync | `(string path, CancellationToken) → Task` | Yes | Allocating | Serialize, write JSON |
| LoadModelAsync | `(string modelPath, CancellationToken) → Task` | Yes | Allocating | Bridge required |
| AttachEquipment | `(string slot, string meshPath, string bone) → void` | Yes | Allocating | Bridge required; pushes command |
| DetachEquipment | `(string slotName) → void` | Yes | Allocating | Bridge required; pushes command |
| AddRig | `(CameraRig) → void` | Yes | Allocating | Pushes AddRigCommand |
| RemoveRig | `(string rigName) → void` | Yes | Allocating | Pushes RemoveRigCommand |
| ModifyRig | `(string rigName, CameraRig newRig) → void` | Yes | Allocating | Pushes ModifyRigCommand |
| SetAnimationConfig | `(string animName, AnimationConfig) → void` | Yes | Allocating | Pushes SetAnimationConfigCommand |
| SelectAnimation | `(string animName) → void` | Yes | Allocating | Pushes SelectAnimationCommand |
| DeselectAnimation | `(string animName) → void` | Yes | Allocating | Pushes DeselectAnimationCommand |
| SetModel | `(string modelPath) → void` | Yes | Allocating | Pushes SetModelCommand; disposes previous model |
| SetScale | `(float scale) → void` | Yes | Allocating | Pushes SetScaleCommand |
| SetMaterialOverride | `(string slot, string? path) → void` | Yes | Allocating | Pushes SetMaterialOverrideCommand |
| Undo | `() → bool` | Yes | Minimal | Delegates to CommandStack |
| Redo | `() → bool` | Yes | Minimal | Delegates to CommandStack |
| ComputeCaptureManifest | `() → CaptureManifest` | Yes | Allocating | Works headless (no bridge needed) |
| StartCaptureAsync | `(CancellationToken) → Task<CaptureResult>` | Yes | Allocating | Bridge required; creates CaptureSession, runs ExecuteAsync |
| PauseCapture | `() → void` | N/A | Free | Requests cooperative pause |
| ResumeCapture | `() → void` | N/A | Free | Signals resume |
| CancelCapture | `() → void` | N/A | Free | Cancels active CancellationToken |
| ExportAsync | `(CaptureResult, CancellationToken) → Task` | Yes | Allocating | Delegates to ExportPipeline |
| Dispose | `() → void` | N/A | Free | Disposes bridge and internal state |

#### CaptureSession

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| Create | `(SpriteProject, ISpriteComposerBridge, IModelHandle, SpriteComposerOptions) → CaptureSession` | Yes | Allocating | Captured project snapshot |
| ExecuteAsync | `(IProgress<CaptureProgress>?, CancellationToken) → Task<CaptureResult>` | Yes | Allocating | Main capture loop |
| RequestPause | `() → void` | N/A | Free | Cooperative pause |
| Resume | `() → void` | N/A | Free | Clear pause flag + signal |

#### AnimationBrowser

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| RefreshAsync | `(IModelHandle, ISpriteComposerBridge, CancellationToken) → Task` | Yes | Allocating | Calls bridge.GetAvailableAnimations |
| SetFilter | `(AnimationFilter) → void` | Yes | Allocating | Rebuilds FilteredAnimations |
| Select | `(string name) → void` | Yes | Allocating | Add to SelectedAnimationNames |
| Deselect | `(string name) → void` | Yes | Allocating | Remove from SelectedAnimationNames |
| SelectAll | `() → void` | Yes | Allocating | Select all in FilteredAnimations |
| DeselectAll | `() → void` | Yes | Allocating | Clear selection |
| LookupAnimationInfo | `(string name) → AnimationInfo?` | Yes | Free | Find by name in AvailableAnimations |

#### EquipmentManager

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| Attach | `(string slotName, EquipmentSlot, IEquipmentHandle) → void` | Yes | Allocating | Add to Attachments |
| Detach | `(string slotName) → IEquipmentHandle?` | Yes | Free | Remove from Attachments, return former handle (caller must Dispose via bridge) |
| Get | `(string slotName) → (EquipmentSlot, IEquipmentHandle)?` | Yes | Free | Lookup |
| IsOccupied | `(string slotName) → bool` | Yes | Free | Check presence |

#### SpritePreview

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| Create | `(SpriteSheet, IReadOnlyList<byte[]> atlases) → SpritePreview` | Yes | Allocating | Initial state: first animation, first angle, frame 0 |
| SwitchAnimation | `(string animName) → void` | Yes | Allocating | Validates animation exists |
| SwitchAngle | `(string angleName) → void` | Yes | Allocating | Validates angle exists |
| StepForward | `() → void` | Yes | Free | Advance frame; wraps or stops per LoopMode |
| StepBackward | `() → void` | Yes | Free | Decrement frame |
| JumpToFrame | `(int index) → void` | Yes | Free | Direct index; validates |
| GetCurrentSpriteFrame | `() → SpriteFrame` | Yes | Free | Lookup via AngleFrameMap[CurrentAngle][CurrentFrameIndex] |

#### PreviewController

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| Play | `() → void` | N/A | Allocating | Start internal timer |
| Pause | `() → void` | N/A | Free | Stop timer, preserve frame |
| Stop | `() → void` | N/A | Free | Stop timer, reset to frame 0 |
| SetSpeed | `(float multiplier) → void` | Yes | Free | Update PlaybackSpeed; timer interval recomputed |

#### ProjectSerializer

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| Serialize | `(SpriteProject) → string` | Yes | Allocating | JSON via System.Text.Json |
| Deserialize | `(string json) → SpriteProject` | Yes | Allocating | Throws JsonException on invalid |

#### ExportPipeline

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| ExportAsync | `(CaptureResult, SpriteProject, CancellationToken) → Task` | Yes | Allocating | Writes atlas PNGs + JSON metadata |
| ResolveFilename | `(string pattern, string variant, string rig, int atlasIndex) → string` | Yes | Allocating | Pure substitution |

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
SET composer.IsDirty ← false
RETURN composer

---

### SpriteComposer.SetBridge
`(bridge: ISpriteComposerBridge) → void`

VALIDATE bridge is not null                          → ArgumentNullException
VALIDATE this.Bridge is null                         → InvalidOperationException("Bridge already attached; call ClearBridge first")
SET this.Bridge ← bridge

---

### SpriteComposer.ClearBridge
`() → void`

IF this.Bridge is null
  RETURN
// Dispose model + equipment through bridge before releasing
IF ModelHandle is not null
  FOREACH (slotName, (slot, handle)) in EquipmentManager.Attachments
    Bridge.DetachEquipment(ModelHandle, handle)
  EquipmentManager.Attachments.Clear()
  Bridge.DisposeModel(ModelHandle)
  SET ModelHandle ← null
Bridge.Dispose()
SET this.Bridge ← null

---

### SpriteComposer.NewProject
`(variant: CharacterVariant, rigs: IReadOnlyList<CameraRig>) → void`

// Clear existing state
CloseProject()
// Build empty project with defaults
SET Project ← new SpriteProject(
    Name: variant.Name,
    Variant: variant,                                  // variant.PivotOverride is the single source of truth
    Rigs: rigs,
    SelectedAnimationNames: new List<string>(),
    AnimationConfigs: new Dictionary<string, AnimationConfig>(),
    DefaultAnimationConfig: new AnimationConfig(),     // sprite-theory defaults
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
CloseProject()
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

### SpriteComposer.CloseProject (internal helper)
`() → void`

IF ActiveCaptureSession is not null AND ActiveCaptureSession.State IN [Running, Paused]
  CancelCapture()
SET Preview ← null
SET ActiveCaptureSession ← null
IF ModelHandle is not null AND Bridge is not null
  FOREACH (_, (slot, handle)) in EquipmentManager.Attachments
    Bridge.DetachEquipment(ModelHandle, handle)
  Bridge.DisposeModel(ModelHandle)
  SET ModelHandle ← null
EquipmentManager.Attachments.Clear()
SET AnimationBrowser ← new AnimationBrowser()     // fresh state
SET Project ← null

---

### SpriteComposer.LoadModelAsync
`(modelPath: string, ct: CancellationToken) → Task`

VALIDATE Bridge is not null                          → InvalidOperationException("Bridge required")
VALIDATE Project is not null                         → InvalidOperationException("No project loaded")
// Eagerly detach existing model + equipment
IF ModelHandle is not null
  FOREACH (_, (_, handle)) in EquipmentManager.Attachments
    Bridge.DetachEquipment(ModelHandle, handle)
  EquipmentManager.Attachments.Clear()
  Bridge.DisposeModel(ModelHandle)
// Load new model
SET ModelHandle ← await Bridge.LoadModelAsync(modelPath, ct)
Bridge.SetModelScale(ModelHandle, Project.Variant.Scale)
// Refresh animations from bridge
COMPUTE animations ← Bridge.GetAvailableAnimations(ModelHandle)
AnimationBrowser.SetAvailableAnimations(animations)
// Update project variant if path differs (not a command — this is a bridge-initiated sync)
IF Project.Variant.ModelPath != modelPath
  SET Project ← Project with Variant.ModelPath = modelPath
Fire ModelChanged(ModelHandle, modelPath)

---

### SpriteComposer.AttachEquipment
`(slotName: string, meshPath: string, boneName: string) → void`

VALIDATE Bridge is not null                          → InvalidOperationException
VALIDATE ModelHandle is not null                     → InvalidOperationException("Model required")
VALIDATE Project is not null                         → InvalidOperationException
COMPUTE slot ← new EquipmentSlot(slotName, meshPath, boneName)   // sprite-theory record
CREATE command ← new AttachEquipmentCommand(this, slot)
Commands.Execute(command)
Fire EquipmentAttached(slot)

---

### SpriteComposer.DetachEquipment
`(slotName: string) → void`

VALIDATE EquipmentManager.IsOccupied(slotName)       → InvalidOperationException("Slot not occupied")
CREATE command ← new DetachEquipmentCommand(this, slotName)
Commands.Execute(command)
Fire EquipmentDetached(slotName)

---

### SpriteComposer.AddRig / RemoveRig / ModifyRig
`(...) → void`

VALIDATE Project is not null                         → InvalidOperationException
CREATE command ← new {Add,Remove,Modify}RigCommand(this, args)
Commands.Execute(command)

---

### SpriteComposer.SetAnimationConfig
`(animName: string, config: AnimationConfig) → void`

VALIDATE Project is not null                         → InvalidOperationException
COMPUTE existing ← Project.AnimationConfigs.GetValueOrDefault(animName, Project.DefaultAnimationConfig)
IF existing equals config                            // No-op guard
  RETURN
CREATE command ← new SetAnimationConfigCommand(this, animName, existing, config)
Commands.Execute(command)

---

### SpriteComposer.SelectAnimation / DeselectAnimation
`(animName: string) → void`

VALIDATE AnimationBrowser.LookupAnimationInfo(animName) is not null   → ArgumentException
CREATE command ← new {Select,Deselect}AnimationCommand(this, animName)
Commands.Execute(command)

---

### SpriteComposer.SetModel
`(modelPath: string) → void`

VALIDATE Bridge is not null                          → InvalidOperationException
VALIDATE Project is not null                         → InvalidOperationException
IF Project.Variant.ModelPath == modelPath            // No-op
  RETURN
CREATE command ← new SetModelCommand(this, Project.Variant.ModelPath, modelPath)
Commands.Execute(command)

---

### SpriteComposer.SetScale
`(scale: float) → void`

VALIDATE scale > 0                                   → ArgumentException
IF Project.Variant.Scale equals scale                // No-op
  RETURN
CREATE command ← new SetScaleCommand(this, Project.Variant.Scale, scale)
Commands.Execute(command)

---

### SpriteComposer.SetMaterialOverride
`(materialSlot: string, path: string?) → void`

COMPUTE previous ← Project.Variant.MaterialOverrides?.GetValueOrDefault(materialSlot, null)
IF previous equals path                              // No-op
  RETURN
CREATE command ← new SetMaterialOverrideCommand(this, materialSlot, previous, path)
Commands.Execute(command)

---

### SpriteComposer.Undo / Redo
`() → bool`

RETURN Commands.Undo() / Commands.Redo()
// Command itself fires relevant events; CommandStack fires UndoRedoStateChanged

---

### SpriteComposer.ComputeCaptureManifest
`() → CaptureManifest`

VALIDATE Project is not null                         → InvalidOperationException
// Gather (AnimationInfo, AnimationConfig) pairs for each selected animation
CREATE pairs ← new List<(AnimationInfo, AnimationConfig)>()
FOREACH name in Project.SelectedAnimationNames (sorted)
  COMPUTE info ← AnimationBrowser.LookupAnimationInfo(name)
  IF info is null
    // Bridge hasn't reported this animation; skip in headless mode, error otherwise
    IF Bridge is not null                            → InvalidOperationException(name)
    CONTINUE
  COMPUTE config ← Project.AnimationConfigs.GetValueOrDefault(name, Project.DefaultAnimationConfig)
  ACCUMULATE (info, config) to pairs
RETURN CaptureManifest.Compute(Project.Variant, Project.Rigs, pairs)      // sprite-theory

---

### SpriteComposer.StartCaptureAsync
`(ct: CancellationToken) → Task<CaptureResult>`

VALIDATE Bridge is not null                          → InvalidOperationException
VALIDATE ModelHandle is not null                     → InvalidOperationException("Model required")
VALIDATE Project is not null                         → InvalidOperationException
VALIDATE ActiveCaptureSession is null OR ActiveCaptureSession.State IN [Completed, Cancelled, Failed]
                                                      → InvalidOperationException("Capture already running")
VALIDATE Project.SelectedAnimationNames.Count > 0    → InvalidOperationException("No animations selected")
VALIDATE Project.Rigs.Count > 0                      → InvalidOperationException("No rigs configured")

CREATE session ← CaptureSession.Create(Project, Bridge, ModelHandle, Options)
SET ActiveCaptureSession ← session
// Wire events to forward to composer subscribers
session.StateChanged += handler that fires CaptureStarted / CaptureCompleted
session.Progress += handler that fires CaptureProgress
session.FrameFailed += handler that fires CaptureError
Fire CaptureStarted(session, ComputeCaptureManifest())

COMPUTE result ← await session.ExecuteAsync(progress: internal dispatcher, ct)

// Build preview from first rig's output (consumer can switch via SwitchRigPreview if multi-rig)
IF result.PerRigOutputs.Count > 0
  COMPUTE firstRig ← result.PerRigOutputs[0]
  SET Preview ← SpritePreview.Create(firstRig.SpriteSheet, firstRig.AtlasImages)

Fire CaptureCompleted(result)
RETURN result

---

### SpriteComposer.PauseCapture / ResumeCapture / CancelCapture
`() → void`

IF ActiveCaptureSession is null
  RETURN
ActiveCaptureSession.RequestPause() / Resume() / (trigger CancellationToken via internal source)

---

### SpriteComposer.ExportAsync
`(captureResult: CaptureResult, ct: CancellationToken) → Task`

VALIDATE Project is not null                         → InvalidOperationException
await ExportPipeline.ExportAsync(captureResult, Project, ct)
Fire ExportCompleted(captureResult)

---

### CaptureSession.Create
`(project: SpriteProject, bridge: ISpriteComposerBridge, modelHandle: IModelHandle, options: SpriteComposerOptions) → CaptureSession`

CREATE session ← new CaptureSession()
SET session.Project ← project
SET session.Bridge ← bridge
SET session.ModelHandle ← modelHandle
SET session.Options ← options
SET session.State ← CaptureState.Idle
SET session.CapturedFrames ← new List<FrameCapture>()
SET session.Errors ← new List<CaptureError>()
SET session.pauseSignal ← new ManualResetEventSlim(initialState: true)
RETURN session

---

### CaptureSession.ExecuteAsync
`(progress: IProgress<CaptureProgress>?, ct: CancellationToken) → Task<CaptureResult>`

VALIDATE State == Idle                               → InvalidOperationException("Session already started or finished")

// Precompute manifest (for progress totals)
CREATE pairs ← new List<(AnimationInfo, AnimationConfig)>()
FOREACH name in Project.SelectedAnimationNames (sorted)
  COMPUTE info ← ResolveAnimationInfoFromBridge(name)
  COMPUTE config ← Project.AnimationConfigs.GetValueOrDefault(name, Project.DefaultAnimationConfig)
  ACCUMULATE (info, config) to pairs
COMPUTE manifest ← CaptureManifest.Compute(Project.Variant, Project.Rigs, pairs)   // sprite-theory

SET State ← CaptureState.Running
SET startTime ← UtcNow
Fire StateChanged(Running)

COMPUTE throttle ← TimeSpan.FromMilliseconds(Options.CaptureProgressIntervalMs)
COMPUTE lastReport ← DateTime.MinValue

// Orchestration loop
Try:
  FOREACH rig IN Project.Rigs
    // Filter by RigsToExport if set (not strictly needed — rigs not exported are still captured if present)
    IF ct.IsCancellationRequested
      SET State ← CaptureState.Cancelled
      BREAK

    COMPUTE modelBounds ← Bridge.GetModelBounds(ModelHandle)

    FOREACH angle IN rig.Angles
      // Wait on pause signal (cooperative)
      await pauseSignal.WaitAsync(ct)
      IF ct.IsCancellationRequested: BREAK

      COMPUTE orthoParams ← OrthographicSetup.Compute(angle, modelBounds, rig.FrameSize)   // sprite-theory
      Bridge.ConfigureCamera(orthoParams, rig.FrameSize)

      FOREACH animName IN Project.SelectedAnimationNames (sorted for determinism)
        await pauseSignal.WaitAsync(ct)
        IF ct.IsCancellationRequested: BREAK

        COMPUTE animInfo ← ResolveAnimationInfoFromBridge(animName)
        IF animInfo is null
          // Animation disappeared — record error and continue
          Errors.Add(new CaptureError(rig.Name, angle.Name, animName, -1, 0, new InvalidOperationException("Animation not available"), UtcNow))
          CONTINUE
        COMPUTE animConfig ← Project.AnimationConfigs.GetValueOrDefault(animName, Project.DefaultAnimationConfig)
        COMPUTE sequence ← AnimationSampling.GenerateFromConfig(animInfo, animConfig)   // sprite-theory
        Bridge.SetAnimation(ModelHandle, animName)

        ITERATE frameIndex FROM 0 TO sequence.FrameCount - 1
          await pauseSignal.WaitAsync(ct)
          IF ct.IsCancellationRequested: BREAK

          COMPUTE normalizedTime ← sequence.Timestamps[frameIndex]
          Bridge.SetAnimationTime(ModelHandle, normalizedTime)

          Try:
            COMPUTE capture ← await Bridge.CaptureFrameAsync(
                animName, angle.Name, frameIndex, normalizedTime,
                rig.IncludeNormalMap, ct)
            // Tag capture with rig identity so assembly phase can group (FrameCapture lacks rig field;
            // the composer internally tracks via parallel list or a (rig, capture) tuple — implementation detail)
            InternalTrack(capture, rig, angle, animName, frameIndex)
            CapturedFrames.Add(capture)
            Fire FrameCaptured(capture)
          Catch (OperationCanceledException):
            RETHROW
          Catch (Exception ex):
            CREATE err ← new CaptureError(rig.Name, angle.Name, animName, frameIndex, normalizedTime, ex, UtcNow)
            Errors.Add(err)
            Fire FrameFailed(err)

          // Throttled progress reporting
          IF progress is not null AND (UtcNow - lastReport) >= throttle
            COMPUTE snap ← BuildProgressSnapshot(manifest, rig, angle.Name, animName, frameIndex, startTime)
            progress.Report(snap)
            Fire Progress(snap)
            SET lastReport ← UtcNow

  IF ct.IsCancellationRequested
    SET State ← CaptureState.Cancelled
  ELSE
    // Assembly phase — run regardless of errors; per-rig outputs contain only successfully-captured frames
    COMPUTE perRigOutputs ← BuildPerRigOutputs(rig-grouped CapturedFrames, Project)
    SET State ← CaptureState.Completed

Catch (OperationCanceledException):
  SET State ← CaptureState.Cancelled

Catch (Exception ex):
  // Catastrophic failure — not a per-frame error
  Errors.Add(new CaptureError("(session)", "", "", -1, 0, ex, UtcNow))
  SET State ← CaptureState.Failed

Finally:
  Fire StateChanged(State)

RETURN new CaptureResult(
    CapturedFrames: CapturedFrames,
    PerRigOutputs: perRigOutputs ?? empty,
    Errors: Errors,
    Manifest: manifest)

---

### CaptureSession.BuildPerRigOutputs (internal)
`(capturesByRig: Dictionary<CameraRig, List<FrameCapture>>, project: SpriteProject) → IReadOnlyList<RigCaptureOutput>`

CREATE outputs ← new List<RigCaptureOutput>()
FOREACH rig IN project.Rigs
  COMPUTE rigFrames ← capturesByRig.GetValueOrDefault(rig, empty list)
  IF rigFrames.Count == 0
    CONTINUE                                         // Skip rigs with no successful captures

  // Atlas packing via sprite-theory
  COMPUTE packInputs ← rigFrames.Select((c, i) => (c.Width, c.Height, i)).ToList()
  COMPUTE atlasOptions ← new AtlasOptions(
      MaxWidth: 4096, MaxHeight: 4096,
      Padding: rig.Padding,
      PowerOfTwo: true,
      GroupByAnimation: true)
  COMPUTE atlasLayout ← AtlasPacker.Pack(packInputs, atlasOptions)     // sprite-theory

  // Resolve pivot once per rig (bounds + camera params are rig/angle-stable):
  //   1. variant.PivotOverride if explicitly set (subjects with non-humanoid proportions)
  //   2. else PivotComputer.ComputeFromBounds for auto-derivation from model geometry
  //   3. else PivotComputer.DefaultHumanoidPivot as terminal fallback (handled inside PivotComputer)
  COMPUTE modelBounds ← capturesByRig[rig].First().InternalModelBounds     // cached at capture time
  COMPUTE rigOrthoParams ← OrthographicSetup.Compute(rig.Angles[0], modelBounds, rig.FrameSize)   // sprite-theory
  COMPUTE resolvedPivot ← project.Variant.PivotOverride
                           ?? PivotComputer.ComputeFromBounds(modelBounds, rigOrthoParams)        // sprite-theory

  // Build captured SpriteFrames
  COMPUTE capturedSpriteFrames ← new List<SpriteFrame>()
  ITERATE i FROM 0 TO rigFrames.Count - 1
    COMPUTE c ← rigFrames[i]
    COMPUTE placement ← atlasLayout.Placements.First(p => p.FrameIndex == i)
    // Lookup duration from the animation's config
    COMPUTE animConfig ← project.AnimationConfigs.GetValueOrDefault(c.AnimationName, project.DefaultAnimationConfig)
    COMPUTE animInfo ← ResolveAnimationInfoFromBridge(c.AnimationName)
    COMPUTE sequence ← AnimationSampling.GenerateFromConfig(animInfo, animConfig)
    COMPUTE duration ← sequence.Duration / sequence.FrameCount
    COMPUTE frame ← new SpriteFrame(
        Index: i,
        AtlasIndex: placement.AtlasIndex,
        AngleName: c.AngleName,
        AnimationName: c.AnimationName,
        FrameInAnimation: c.FrameIndex,
        Rect: new Rectangle(placement.X, placement.Y, placement.Width, placement.Height),
        TrimmedRect: null,                           // Trimming not yet implemented
        Pivot: resolvedPivot,
        Duration: duration,
        IsMirror: false,
        MirrorSourceIndex: null)
    ACCUMULATE frame to capturedSpriteFrames

  // Mirror generation via sprite-theory
  COMPUTE mirrors ← MirrorOptimizer.ComputeMirrors(rig)                // sprite-theory
  COMPUTE mirrorFrames ← MirrorOptimizer.GenerateMirrorFrames(capturedSpriteFrames, mirrors)  // sprite-theory
  COMPUTE allFrames ← capturedSpriteFrames.Concat(mirrorFrames).ToList()

  // Per-atlas info
  COMPUTE atlasInfos ← new List<AtlasInfo>()
  ITERATE atlasIdx FROM 0 TO atlasLayout.AtlasCount - 1
    COMPUTE filename ← ExportPipeline.ResolveFilename(
        project.ExportOptions.AtlasFilenamePattern,
        project.Name, rig.Name, atlasIdx)
    atlasInfos.Add(new AtlasInfo(
        Index: atlasIdx,
        Filename: filename,
        Width: atlasLayout.AtlasWidths[atlasIdx],
        Height: atlasLayout.AtlasHeights[atlasIdx]))

  // Per-animation groupings
  COMPUTE animations ← BuildSpriteAnimations(allFrames, project)

  // Atlas assembly via sprite-theory
  COMPUTE atlasImages ← AtlasAssembler.Assemble(rigFrames, atlasLayout, rig.BackgroundColor)  // sprite-theory

  // Optional normal map atlases (share layout with color atlases)
  COMPUTE normalAtlases ← null
  IF rig.IncludeNormalMap
    normalAtlases ← BuildNormalAtlases(rigFrames, atlasLayout, project.NormalMapOptions ?? new NormalMapOptions())

  // Assemble SpriteSheet
  COMPUTE spriteSheet ← new SpriteSheet(
      Version: "1.0",
      Generator: "BeyondImmersion.Bannou.SpriteComposer",
      GeneratedAt: UtcNow,
      Variant: project.Variant,
      Rig: rig,
      Atlases: atlasInfos,
      Animations: animations,
      Frames: allFrames,
      CustomProperties: project.CustomProperties)

  outputs.Add(new RigCaptureOutput(rig, atlasImages, normalAtlases, spriteSheet))

RETURN outputs

---

### CaptureSession.BuildSpriteAnimations (internal)
`(frames: IReadOnlyList<SpriteFrame>, project: SpriteProject) → IReadOnlyList<SpriteAnimation>`

CREATE animations ← new List<SpriteAnimation>()
// Group frames by animation name
COMPUTE byAnim ← frames.GroupBy(f => f.AnimationName)
FOREACH group in byAnim (sorted by animation name for determinism)
  COMPUTE animName ← group.Key
  COMPUTE animConfig ← project.AnimationConfigs.GetValueOrDefault(animName, project.DefaultAnimationConfig)
  COMPUTE animInfo ← ResolveAnimationInfoFromBridge(animName)
  COMPUTE totalDuration ← animInfo.Duration * (animConfig.TrimEnd - animConfig.TrimStart) / animConfig.SpeedMultiplier

  // Build per-angle frame index lookup
  CREATE angleMap ← new Dictionary<string, int[]>()
  COMPUTE framesByAngle ← group.GroupBy(f => f.AngleName)
  FOREACH angleGroup in framesByAngle
    COMPUTE orderedIndices ← angleGroup.OrderBy(f => f.FrameInAnimation).Select(f => f.Index).ToArray()
    SET angleMap[angleGroup.Key] ← orderedIndices

  animations.Add(new SpriteAnimation(
      Name: animName,
      LoopMode: animConfig.LoopMode,
      TotalDuration: totalDuration,
      AngleFrameMap: angleMap,
      Events: null))                                 // Events not yet implemented — null per sprite-theory schema

RETURN animations

---

### CaptureSession.BuildNormalAtlases (internal)
`(rigFrames: IReadOnlyList<FrameCapture>, layout: AtlasLayout, options: NormalMapOptions) → IReadOnlyList<byte[]>`

// For each captured frame with depth data, compute normal map bytes
CREATE normalFrameCaptures ← new List<FrameCapture>()
FOREACH frame in rigFrames
  IF frame.DepthData is null
    // Substitute a neutral-blue (flat) normal for frames without depth
    CREATE neutral ← new byte[frame.Width * frame.Height * 4]
    ITERATE i FROM 0 TO neutral.Length / 4 - 1
      neutral[i*4+0] ← 128                           // X = 0
      neutral[i*4+1] ← 128                           // Y = 0
      neutral[i*4+2] ← 255                           // Z = 1
      neutral[i*4+3] ← 255
    ACCUMULATE new FrameCapture(neutral, null, frame.Width, frame.Height, frame.AngleName, frame.AnimationName, frame.FrameIndex, frame.NormalizedTime) to normalFrameCaptures
  ELSE
    COMPUTE normalBytes ← DepthToNormal.Generate(frame.DepthData, frame.Width, frame.Height, options)   // sprite-theory
    ACCUMULATE new FrameCapture(normalBytes, null, frame.Width, frame.Height, frame.AngleName, frame.AnimationName, frame.FrameIndex, frame.NormalizedTime) to normalFrameCaptures

// Assemble using the same layout as the color atlas (same placements)
RETURN AtlasAssembler.Assemble(normalFrameCaptures, layout, Color.Transparent)     // sprite-theory

---

### CaptureSession.RequestPause
`() → void`

IF State == Running
  SET IsPauseRequested ← true
  pauseSignal.Reset()                                // Block next WaitAsync
  SET State ← CaptureState.Paused
  Fire StateChanged(Paused)

---

### CaptureSession.Resume
`() → void`

IF State == Paused
  SET IsPauseRequested ← false
  pauseSignal.Set()                                  // Release pending WaitAsync
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
// Rebuild filtered list and prune selection
COMPUTE available ← new HashSet<string>(animations.Select(a => a.Name))
SET this.SelectedAnimationNames ← new SortedSet<string>(this.SelectedAnimationNames.Where(n => available.Contains(n)))
ApplyFilter()

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

### AnimationBrowser.Select / Deselect
`(name: string) → void`

VALIDATE LookupAnimationInfo(name) is not null       → ArgumentException("Unknown animation")
SET SelectedAnimationNames ← SelectedAnimationNames with name added/removed
// SortedSet preserves sort order automatically

---

### AnimationBrowser.LookupAnimationInfo
`(name: string) → AnimationInfo?`

RETURN AvailableAnimations.FirstOrDefault(a => a.Name == name)

---

### EquipmentManager.Attach / Detach / Get / IsOccupied
`(...) → void / (...)? / bool`

// Pure dictionary operations — no bridge calls
Attach: SET Attachments[slotName] ← (slot, handle)
Detach: SET handle ← Attachments[slotName].Handle; REMOVE Attachments[slotName]; RETURN handle
Get: RETURN Attachments.GetValueOrDefault(slotName)
IsOccupied: RETURN Attachments.ContainsKey(slotName)

---

### SpritePreview.Create
`(spriteSheet: SpriteSheet, atlases: IReadOnlyList<byte[]>) → SpritePreview`

VALIDATE spriteSheet.Animations.Count > 0            → ArgumentException("No animations")
COMPUTE firstAnim ← spriteSheet.Animations[0]
VALIDATE firstAnim.AngleFrameMap.Count > 0           → ArgumentException("Animation has no angles")
COMPUTE firstAngle ← firstAnim.AngleFrameMap.Keys.OrderBy(a => a).First()

CREATE preview ← new SpritePreview()
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
`(...) → void`

SwitchAnimation:
  VALIDATE SpriteSheet.Animations.Any(a => a.Name == name)   → ArgumentException
  SET CurrentAnimation ← name
  SET CurrentFrameIndex ← 0
  // If the new animation doesn't have CurrentAngle, fall back to first angle
  COMPUTE anim ← SpriteSheet.Animations.First(a => a.Name == name)
  IF NOT anim.AngleFrameMap.ContainsKey(CurrentAngle)
    SET CurrentAngle ← anim.AngleFrameMap.Keys.OrderBy(a => a).First()
  Fire AnimationChanged(name)
  Fire FrameChanged(0)

SwitchAngle:
  COMPUTE anim ← SpriteSheet.Animations.First(a => a.Name == CurrentAnimation)
  VALIDATE anim.AngleFrameMap.ContainsKey(name)    → ArgumentException
  SET CurrentAngle ← name
  SET CurrentFrameIndex ← 0
  Fire AngleChanged(name)
  Fire FrameChanged(0)

StepForward:
  COMPUTE anim ← SpriteSheet.Animations.First(a => a.Name == CurrentAnimation)
  COMPUTE indices ← anim.AngleFrameMap[CurrentAngle]
  COMPUTE next ← CurrentFrameIndex + 1
  IF next >= indices.Length
    IF anim.LoopMode == Loop: next ← 0
    ELSE IF anim.LoopMode == PingPong: (handled by PreviewController's direction flag — Preview itself just advances)
    ELSE: RETURN (stopped at last frame)
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
`(...) → void`

Play:
  IF Preview.State == Playing: RETURN
  SET Preview.State ← Playing
  COMPUTE intervalMs ← 1000 / (Preview.TargetFps * Preview.PlaybackSpeed)
  SET playbackTimer ← new Timer(callback: () => Preview.StepForward(), intervalMs)
  Fire StateChanged(Playing)

Pause:
  IF Preview.State != Playing: RETURN
  playbackTimer?.Dispose()
  SET playbackTimer ← null
  SET Preview.State ← Paused
  Fire StateChanged(Paused)

Stop:
  playbackTimer?.Dispose()
  SET playbackTimer ← null
  SET Preview.State ← Stopped
  Preview.JumpToFrame(0)
  Fire StateChanged(Stopped)

SetSpeed:
  VALIDATE multiplier > 0                            → ArgumentException
  SET Preview.PlaybackSpeed ← multiplier
  IF Preview.State == Playing
    // Restart timer with new interval
    Pause(); Play()

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
RETURN project

---

### ExportPipeline.ExportAsync
`(result: CaptureResult, project: SpriteProject, ct: CancellationToken) → Task`

VALIDATE project.ExportOptions.AtlasEncoder is not null          → InvalidOperationException("AtlasEncoder required")
VALIDATE Directory.Exists(project.ExportOptions.OutputDirectory) OR CanCreate
  → IF not exists: Directory.CreateDirectory(project.ExportOptions.OutputDirectory)

FOREACH rigOutput IN result.PerRigOutputs
  // Filter by RigsToExport if set
  IF project.ExportOptions.RigsToExport is not null AND rigOutput.Rig.Name not in project.ExportOptions.RigsToExport
    CONTINUE

  // Export atlas images
  ITERATE atlasIdx FROM 0 TO rigOutput.AtlasImages.Count - 1
    COMPUTE atlasBytes ← rigOutput.AtlasImages[atlasIdx]
    COMPUTE info ← rigOutput.SpriteSheet.Atlases[atlasIdx]
    COMPUTE encoded ← project.ExportOptions.AtlasEncoder.EncodeRgba(atlasBytes, info.Width, info.Height)
    COMPUTE fullPath ← Path.Combine(project.ExportOptions.OutputDirectory, info.Filename)
    await File.WriteAllBytesAsync(fullPath, encoded, ct)

  // Export normal maps (if present)
  IF rigOutput.NormalAtlases is not null
    ITERATE atlasIdx FROM 0 TO rigOutput.NormalAtlases.Count - 1
      COMPUTE normalBytes ← rigOutput.NormalAtlases[atlasIdx]
      COMPUTE info ← rigOutput.SpriteSheet.Atlases[atlasIdx]
      COMPUTE normalFilename ← ResolveFilename(
          project.ExportOptions.NormalMapFilenamePattern,
          project.Name, rigOutput.Rig.Name, atlasIdx)
      COMPUTE encoded ← project.ExportOptions.AtlasEncoder.EncodeRgba(normalBytes, info.Width, info.Height)
      COMPUTE fullPath ← Path.Combine(project.ExportOptions.OutputDirectory, normalFilename)
      await File.WriteAllBytesAsync(fullPath, encoded, ct)

  // Export metadata JSON
  COMPUTE metadataFilename ← ResolveFilename(
      project.ExportOptions.MetadataFilenamePattern,
      project.Name, rigOutput.Rig.Name, 0)
  COMPUTE metadataJson ← SpriteSheetSerializer.Serialize(rigOutput.SpriteSheet)    // sprite-theory
  COMPUTE fullPath ← Path.Combine(project.ExportOptions.OutputDirectory, metadataFilename)
  await File.WriteAllTextAsync(fullPath, metadataJson, ct)

---

### ExportPipeline.ResolveFilename
`(pattern: string, variant: string, rig: string, atlasIndex: int) → string`

COMPUTE result ← pattern
result ← result.Replace("{variant}", KebabCase(variant))
result ← result.Replace("{rig}", KebabCase(rig))
result ← result.Replace("{atlas}", atlasIndex.ToString())
RETURN result

FUNCTION KebabCase(s: string) → string:
  // Convert "TopDown-8Dir" → "topdown-8dir", "warrior_plate_sword" → "warrior-plate-sword"
  RETURN s.ToLowerInvariant().Replace('_', '-')

---

### CommandStack.Execute
`(command: IEditorCommand, allowMerge: bool = true) → void`

VALIDATE command is not null                         → ArgumentNullException
command.Execute()
// If inside compound, accumulate there
IF activeCompound is not null
  activeCompound.Add(command)
  Fire CommandExecuted(command, ExecutionType.Execute)
  RETURN
// Try merge with previous command within merge window
COMPUTE now ← UtcNow
IF allowMerge AND undoStack.Count > 0 AND (now - lastCommandTime) < mergeWindow
  COMPUTE last ← undoStack[undoStack.Count - 1]
  IF last.CanMergeWith(command) AND last.TryMerge(command)
    SET lastCommandTime ← now
    Fire CommandExecuted(command, ExecutionType.Merged)
    Fire StateChanged
    RETURN
// Push as new command
redoStack.Clear()
undoStack.Add(command)
SET lastCommandTime ← now
WHILE undoStack.Count > maxDepth
  undoStack.RemoveAt(0)                              // Evict oldest
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
  Fire CommandUndone(command)
  Fire StateChanged
  RETURN true

Redo:
  IF redoStack.Count == 0 OR activeCompound is not null: RETURN false
  COMPUTE command ← redoStack[redoStack.Count - 1]
  redoStack.RemoveAt(redoStack.Count - 1)
  command.Execute()
  undoStack.Add(command)
  Fire CommandRedone(command)
  Fire StateChanged
  RETURN true

---

### CommandStack.BeginCompound
`(description: string) → IDisposable`

VALIDATE description is not null                     → ArgumentNullException
INCREMENT compoundDepth
IF activeCompound is null
  SET activeCompound ← new CompoundCommand(description)
RETURN new CompoundScope(this)                       // Disposable that calls EndCompound on Dispose

---

### CommandStack.EndCompound (internal)
`() → void`

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

---

### CommandStack.BreakMerge / Clear
`() → void`

BreakMerge: SET lastCommandTime ← DateTime.MinValue
Clear: undoStack.Clear(); redoStack.Clear(); activeCompound ← null; compoundDepth ← 0; lastCommandTime ← DateTime.MinValue; Fire StateChanged

---

### AddRigCommand.Execute / Undo
`() → void`

AddRigCommand(composer, rig):
  Execute:
    VALIDATE composer.Project is not null
    SET composer.Project ← composer.Project with Rigs = Rigs + rig
    SET composer.IsDirty ← true
  Undo:
    SET composer.Project ← composer.Project with Rigs = Rigs without rig
    SET composer.IsDirty ← true
  CanMergeWith: RETURN false
  TryMerge: RETURN false

---

### RemoveRigCommand.Execute / Undo
`() → void`

RemoveRigCommand(composer, rigName):
  Execute:
    COMPUTE rig ← composer.Project.Rigs.First(r => r.Name == rigName)
    SET removedRig ← rig                             // Save for undo
    SET removedIndex ← composer.Project.Rigs.IndexOf(rig)
    SET composer.Project ← composer.Project with Rigs = Rigs without rig
    SET composer.IsDirty ← true
  Undo:
    SET composer.Project ← composer.Project with Rigs = Rigs.Insert(removedIndex, removedRig)
  CanMergeWith / TryMerge: RETURN false

---

### ModifyRigCommand.Execute / Undo
`() → void`

ModifyRigCommand(composer, rigName, oldRig, newRig):
  Execute:
    COMPUTE idx ← composer.Project.Rigs.IndexOf(oldRig-by-name)
    SET composer.Project ← composer.Project with Rigs = Rigs.SetItem(idx, newRig)
    SET composer.IsDirty ← true
  Undo:
    COMPUTE idx ← composer.Project.Rigs.IndexOf(newRig-by-name)
    SET composer.Project ← composer.Project with Rigs = Rigs.SetItem(idx, oldRig)
    SET composer.IsDirty ← true
  CanMergeWith(other): RETURN other is ModifyRigCommand AND other.rigName == this.rigName
  TryMerge(other): IF CanMergeWith: SET this.newRig ← other.newRig; RETURN true; ELSE: RETURN false

---

### SetAnimationConfigCommand.Execute / Undo
`() → void`

SetAnimationConfigCommand(composer, animName, oldConfig, newConfig):
  Execute:
    SET composer.Project ← composer.Project with AnimationConfigs[animName] = newConfig
    SET composer.IsDirty ← true
  Undo:
    IF oldConfig equals composer.Project.DefaultAnimationConfig AND was originally absent:
      SET composer.Project ← composer.Project with AnimationConfigs having animName removed
    ELSE:
      SET composer.Project ← composer.Project with AnimationConfigs[animName] = oldConfig
    SET composer.IsDirty ← true
  CanMergeWith(other): RETURN other is SetAnimationConfigCommand AND other.animName == this.animName
  TryMerge(other): SET this.newConfig ← other.newConfig; RETURN true

---

### SelectAnimationCommand / DeselectAnimationCommand
`() → void`

SelectAnimationCommand(composer, animName):
  Execute: SET composer.Project ← composer.Project with SelectedAnimationNames.Add(animName); composer.AnimationBrowser.Select(animName); SET IsDirty ← true
  Undo: SET composer.Project ← composer.Project with SelectedAnimationNames.Remove(animName); composer.AnimationBrowser.Deselect(animName)
  CanMergeWith / TryMerge: RETURN false

(DeselectAnimationCommand symmetric)

---

### AttachEquipmentCommand.Execute / Undo
`() → void`

AttachEquipmentCommand(composer, slot):
  Execute:
    // If slot already occupied, detach previous first
    IF composer.EquipmentManager.IsOccupied(slot.SlotName)
      COMPUTE previous ← composer.EquipmentManager.Get(slot.SlotName).Value
      composer.Bridge.DetachEquipment(composer.ModelHandle, previous.Handle)
      composer.EquipmentManager.Detach(slot.SlotName)
      SET previousSlot ← previous.Slot               // Save for undo
    // Attach new equipment
    COMPUTE handle ← await composer.Bridge.AttachEquipmentAsync(composer.ModelHandle, slot, default)
    composer.EquipmentManager.Attach(slot.SlotName, slot, handle)
    SET composer.Project ← composer.Project with Variant.Equipment updated
    SET composer.IsDirty ← true
    SET attachedHandle ← handle                      // Save for undo
  Undo:
    // Detach the attached equipment
    composer.Bridge.DetachEquipment(composer.ModelHandle, attachedHandle)
    composer.EquipmentManager.Detach(slot.SlotName)
    SET composer.Project ← composer.Project with Variant.Equipment without slot
    // If a previous attachment was there, re-attach it
    IF previousSlot is not null
      COMPUTE restoredHandle ← await composer.Bridge.AttachEquipmentAsync(composer.ModelHandle, previousSlot, default)
      composer.EquipmentManager.Attach(previousSlot.SlotName, previousSlot, restoredHandle)
      SET composer.Project ← composer.Project with Variant.Equipment + previousSlot
    SET composer.IsDirty ← true
  CanMergeWith / TryMerge: RETURN false

---

### DetachEquipmentCommand.Execute / Undo
`() → void`

DetachEquipmentCommand(composer, slotName):
  Execute:
    COMPUTE existing ← composer.EquipmentManager.Get(slotName).Value
    SET detachedSlot ← existing.Slot                 // Save for undo
    composer.Bridge.DetachEquipment(composer.ModelHandle, existing.Handle)
    composer.EquipmentManager.Detach(slotName)
    SET composer.Project ← composer.Project with Variant.Equipment without detachedSlot
    SET composer.IsDirty ← true
  Undo:
    COMPUTE restoredHandle ← await composer.Bridge.AttachEquipmentAsync(composer.ModelHandle, detachedSlot, default)
    composer.EquipmentManager.Attach(detachedSlot.SlotName, detachedSlot, restoredHandle)
    SET composer.Project ← composer.Project with Variant.Equipment + detachedSlot
    SET composer.IsDirty ← true
  CanMergeWith / TryMerge: RETURN false

---

### SetModelCommand.Execute / Undo
`() → void`

SetModelCommand(composer, oldPath, newPath):
  Execute:
    // Save equipment snapshot for undo (equipment is skeleton-specific; detach on model change)
    CREATE detachedSlots ← new List<EquipmentSlot>()
    FOREACH (slotName, (slot, handle)) in composer.EquipmentManager.Attachments
      composer.Bridge.DetachEquipment(composer.ModelHandle, handle)
      ACCUMULATE slot to detachedSlots
    composer.EquipmentManager.Attachments.Clear()
    SET previousEquipment ← detachedSlots

    composer.Bridge.DisposeModel(composer.ModelHandle)
    SET composer.ModelHandle ← await composer.Bridge.LoadModelAsync(newPath, default)
    composer.Bridge.SetModelScale(composer.ModelHandle, composer.Project.Variant.Scale)
    COMPUTE animations ← composer.Bridge.GetAvailableAnimations(composer.ModelHandle)
    composer.AnimationBrowser.SetAvailableAnimations(animations)
    SET composer.Project ← composer.Project with Variant.ModelPath = newPath, Variant.Equipment = []
    SET composer.IsDirty ← true
    Fire ModelChanged(newPath)
  Undo:
    composer.Bridge.DisposeModel(composer.ModelHandle)
    SET composer.ModelHandle ← await composer.Bridge.LoadModelAsync(oldPath, default)
    composer.Bridge.SetModelScale(composer.ModelHandle, composer.Project.Variant.Scale)
    COMPUTE animations ← composer.Bridge.GetAvailableAnimations(composer.ModelHandle)
    composer.AnimationBrowser.SetAvailableAnimations(animations)
    // Restore equipment
    FOREACH slot in previousEquipment
      COMPUTE handle ← await composer.Bridge.AttachEquipmentAsync(composer.ModelHandle, slot, default)
      composer.EquipmentManager.Attach(slot.SlotName, slot, handle)
    SET composer.Project ← composer.Project with Variant.ModelPath = oldPath, Variant.Equipment = previousEquipment
    SET composer.IsDirty ← true
    Fire ModelChanged(oldPath)
  CanMergeWith / TryMerge: RETURN false

---

### SetScaleCommand.Execute / Undo
`() → void`

SetScaleCommand(composer, oldScale, newScale):
  Execute:
    IF composer.ModelHandle is not null AND composer.Bridge is not null
      composer.Bridge.SetModelScale(composer.ModelHandle, newScale)
    SET composer.Project ← composer.Project with Variant.Scale = newScale
    SET composer.IsDirty ← true
  Undo:
    IF composer.ModelHandle is not null AND composer.Bridge is not null
      composer.Bridge.SetModelScale(composer.ModelHandle, oldScale)
    SET composer.Project ← composer.Project with Variant.Scale = oldScale
    SET composer.IsDirty ← true
  CanMergeWith(other): RETURN other is SetScaleCommand
  TryMerge(other): SET this.newScale ← other.newScale; RETURN true

---

### SetMaterialOverrideCommand.Execute / Undo
`() → void`

SetMaterialOverrideCommand(composer, materialSlot, oldPath, newPath):
  Execute:
    IF composer.ModelHandle is not null AND composer.Bridge is not null
      composer.Bridge.SetMaterialOverride(composer.ModelHandle, materialSlot, newPath)
    SET composer.Project ← composer.Project with Variant.MaterialOverrides[materialSlot] = newPath
    SET composer.IsDirty ← true
  Undo:
    IF composer.ModelHandle is not null AND composer.Bridge is not null
      composer.Bridge.SetMaterialOverride(composer.ModelHandle, materialSlot, oldPath)
    SET composer.Project ← composer.Project with Variant.MaterialOverrides[materialSlot] = oldPath
    SET composer.IsDirty ← true
  CanMergeWith(other): RETURN other is SetMaterialOverrideCommand AND other.materialSlot == this.materialSlot
  TryMerge(other): SET this.newPath ← other.newPath; RETURN true

---

## Algorithms

### Capture Orchestration Loop

**Purpose**: Iterate all combinations of (rig, angle, animation, frame) and capture a frame for each, with per-frame error isolation and cooperative pause/cancel.

**Complexity**: O(R × A × N × F) time where R = rigs, A = angles/rig, N = selected animations, F = frames/animation. Each iteration is bounded by the bridge's `CaptureFrameAsync` latency (typically 40–70 ms for Stride).

**Reference**: Standard nested-loop traversal with cooperative cancellation token. The per-frame try-catch pattern is the canonical IMPLEMENTATION TENETS per-item error isolation model.

INPUT: SpriteProject, ISpriteComposerBridge, IModelHandle, CancellationToken
OUTPUT: CaptureResult with captured frames, per-rig assembled outputs, and per-frame errors

```
State ← Running
FOREACH rig:
  modelBounds ← Bridge.GetModelBounds(ModelHandle)
  FOREACH angle:
    await pauseSignal.WaitAsync(ct)
    orthoParams ← OrthographicSetup.Compute(angle, modelBounds, rig.FrameSize)   // sprite-theory
    Bridge.ConfigureCamera(orthoParams, rig.FrameSize)
    FOREACH animName (sorted):
      sequence ← AnimationSampling.GenerateFromConfig(animInfo, animConfig)      // sprite-theory
      Bridge.SetAnimation(ModelHandle, animName)
      ITERATE frameIndex FROM 0 TO sequence.FrameCount - 1:
        await pauseSignal.WaitAsync(ct)
        Bridge.SetAnimationTime(ModelHandle, sequence.Timestamps[frameIndex])
        Try:
          capture ← await Bridge.CaptureFrameAsync(animName, angle.Name, frameIndex, timestamp, rig.IncludeNormalMap, ct)
          CapturedFrames.Add(capture)
        Catch (OperationCanceledException): RETHROW
        Catch (Exception): record CaptureError, continue
        ReportProgress (throttled to Options.CaptureProgressIntervalMs)

// Assembly phase per rig (deterministic, engine-independent)
FOREACH rig:
  group CapturedFrames by rig
  atlasLayout ← AtlasPacker.Pack(frame dimensions, AtlasOptions)                 // sprite-theory
  mirrors ← MirrorOptimizer.ComputeMirrors(rig)                                  // sprite-theory
  mirrorFrames ← MirrorOptimizer.GenerateMirrorFrames(capturedFrames, mirrors)   // sprite-theory
  atlasImages ← AtlasAssembler.Assemble(rigFrames, atlasLayout, rig.BackgroundColor)  // sprite-theory
  spriteSheet ← compose metadata from layout + frames + animations

State ← Completed
```

**Termination guarantee**: The outer loops have finite bounds (finite rigs, angles, animations, frame counts). The inner await points respect the CancellationToken. Even with frame errors, the loop always progresses — each iteration either captures a frame or records an error.

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
    // Merged — no new undo entry, top command updated in place
    lastCommandTime ← now
    RETURN
// Otherwise push as new entry
undoStack.Push(incoming)
lastCommandTime ← now
```

**Merge-safe command types**: `ModifyRigCommand`, `SetAnimationConfigCommand`, `SetScaleCommand`, `SetMaterialOverrideCommand`. **Non-merge command types**: `AddRigCommand`, `RemoveRigCommand`, `AttachEquipmentCommand`, `DetachEquipmentCommand`, `SetModelCommand`, `SelectAnimationCommand`, `DeselectAnimationCommand`.

---

## Serialization Formats

### Sprite Project (.spriteproj.json)

**Purpose**: Complete project configuration, version-controllable, shareable across team members and SpriteBatcher.
**Encoding**: UTF-8 JSON via System.Text.Json.
**Property naming**: camelCase (matches sprite-theory's `SpriteSheetSerializer` convention).
**Formatting**: Indented, null values omitted (`JsonIgnoreCondition.WhenWritingNull`).

Structure (expressed as JSON schema prose):

```json
{
  "name": "warrior",
  "variant": {
    "name": "warrior_plate_sword",
    "modelPath": "Characters/Warrior_Base.fbx",
    "equipment": [
      { "slotName": "chest", "meshPath": "Equipment/Plate_Chest.fbx", "boneName": "Spine2" },
      { "slotName": "weapon_r", "meshPath": "Weapons/Sword_01.fbx", "boneName": "RightHand" }
    ],
    "materialOverrides": null,
    "scale": 1.0
  },
  "rigs": [
    { "name": "TopDown-8Dir", "projection": "Orthographic", "angles": [...], "frameSize": {"width": 96, "height": 96}, "padding": 2, "backgroundColor": {...}, "includeNormalMap": false, "trimTransparent": false },
    { "name": "SideView-Brawler", ... }
  ],
  "selectedAnimationNames": ["attack_heavy", "attack_light", "death", "idle", "run", "walk"],
  "animationConfigs": {
    "attack_heavy": { "frameCount": 16, "speedMultiplier": 1.0, "trimStart": 0.0, "trimEnd": 1.0, "loopMode": "None" }
  },
  "defaultAnimationConfig": { "frameCount": 8, "speedMultiplier": 1.0, "trimStart": 0.0, "trimEnd": 1.0, "loopMode": "None" },
  "exportOptions": {
    "outputDirectory": "./sprites/warrior",
    "atlasFilenamePattern": "{variant}_{rig}_{atlas}.png",
    "normalMapFilenamePattern": "{variant}_{rig}_{atlas}_normal.png",
    "metadataFilenamePattern": "{variant}_{rig}.json",
    "rigsToExport": null
  },
  "normalMapOptions": null,
  "pivotOverride": null,
  "customProperties": null,
  "schemaVersion": "1.0"
}
```

**Note**: `exportOptions.atlasEncoder` is NOT serialized. It is a runtime-only field supplied by the hosting application (e.g., the Stride bridge registers its own encoder when the composer is created).

The format depends on types from sprite-theory (CameraRig, CaptureAngle, AnimationConfig, CharacterVariant, EquipmentSlot, Color, NormalMapOptions, Vector2) which serialize per their own records' property layouts.

**No custom binary format.** All serialization is JSON.

---
