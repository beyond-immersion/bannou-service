# Sprite Theory SDK Implementation Map

> **SDK**: sprite-theory
> **Location**: `sdks/sprite-theory/`
> **Layer**: Theory
> **Domain**: Sprite
> **Deep Dive**: [docs/sdks/SPRITE-THEORY.md](../SPRITE-THEORY.md)
> **Status**: Aspirational — pseudo-code represents intended behavior, not verified implementation

---

| Field | Value |
|-------|-------|
| SDK | sprite-theory |
| Layer | Theory |
| Public Types | 40 (21 records, 10 static classes, 3 enums, 4 structs, 2 interfaces) |
| Public Methods | 16 |
| Dependencies | None (pure .NET BCL: System.Text.Json, System.Numerics) |
| Deterministic | Yes (all operations are pure) |
| Allocation-Free Hot Paths | CaptureAngle construction, BoundingBox operations, Color operations, Vector2/Rectangle operations |

---

## Data Structures

### CameraRig

**Kind**: Record (immutable)
**Thread Safety**: Immutable

| Field | Type | Default | Purpose |
|-------|------|---------|---------|
| `Name` | `string` | required | Rig identifier ("SideView-Brawler", "TopDown-8Dir") |
| `Projection` | `ProjectionType` | `Orthographic` | Camera projection mode |
| `Angles` | `IReadOnlyList<CaptureAngle>` | required | Angles to render — every angle IS captured |
| `FrameSize` | `(int Width, int Height)` | required | Per-frame pixel dimensions |
| `Padding` | `int` | `2` | Pixels between frames in atlas |
| `BackgroundColor` | `Color` | `Color.Transparent` | Render clear color |
| `IncludeNormalMap` | `bool` | `false` | Also generate depth→normal atlas |
| `TrimTransparent` | `bool` | `false` | Trim transparent borders per frame |

**Invariant**: Every angle in `Angles` is rendered. Mirror targets are NOT in this list.

### CaptureAngle

**Kind**: Record (immutable)
**Thread Safety**: Immutable

| Field | Type | Default | Purpose |
|-------|------|---------|---------|
| `Name` | `string` | required | Angle identifier ("right", "N", "NE") |
| `Yaw` | `float` | required | Degrees rotation around Y (0 = north/forward) |
| `Pitch` | `float` | required | Degrees from horizontal (negative = looking down) |
| `ProducesMirror` | `bool` | `false` | If true, MirrorOptimizer generates a flipped counterpart |
| `MirrorTargetName` | `string?` | `null` | Name for the generated mirror angle (e.g., "NW") |
| `MirrorAxis` | `MirrorAxis` | `Horizontal` | Flip axis for the generated mirror |

**Invariant**: This angle is ALWAYS captured. `ProducesMirror` is additive metadata, not a skip flag.

### OrthographicParameters

**Kind**: Record (immutable, output of OrthographicSetup.Compute)
**Thread Safety**: Immutable

| Field | Type | Purpose |
|-------|------|---------|
| `Position` | `(float X, float Y, float Z)` | Camera world position |
| `Direction` | `(float X, float Y, float Z)` | Camera forward direction (normalized) |
| `Up` | `(float X, float Y, float Z)` | Camera up vector (normalized) |
| `OrthoWidth` | `float` | Orthographic viewport width in world units |
| `OrthoHeight` | `float` | Orthographic viewport height in world units |
| `NearPlane` | `float` | Near clip distance |
| `FarPlane` | `float` | Far clip distance |

### PivotComputer

**Kind**: Static class (no instance state)
**Thread Safety**: Thread-safe (pure)

| Member | Type | Purpose |
|--------|------|---------|
| `DefaultHumanoidPivot` | `static readonly Vector2` | Terminal fallback pivot `(0.5, 0.85)` — center-X, 85% from top, for upright humanoids |
| `ComputeFromBounds` | static method | Derives a pivot by projecting the bottom-center of a bounding box onto the camera frame plane, clamped to `[0, 1]` |

### MultiAtlasStrategy

**Kind**: Static class (no instance state)
**Thread Safety**: Thread-safe (pure, though callers mutate the free rectangle list passed in)

| Member | Type | Purpose |
|--------|------|---------|
| `OpenNextAtlas` | static method | Validates frame-fits-in-max then resets the free rectangle list to represent a fresh atlas; returns the new atlas index and the single full-atlas free rect for placement |

### BoundingBox

**Kind**: Struct (value type, 24 bytes)
**Thread Safety**: Immutable

| Field | Type | Purpose |
|-------|------|---------|
| `Min` | `(float X, float Y, float Z)` | Minimum corner |
| `Max` | `(float X, float Y, float Z)` | Maximum corner |

**Computed**: `Center` → midpoint, `Extents` → half-size, `Size` → full size.

### FrameSequence

**Kind**: Record (immutable, output of AnimationSampling)
**Thread Safety**: Immutable

| Field | Type | Purpose |
|-------|------|---------|
| `Timestamps` | `IReadOnlyList<float>` | Normalized times (0.0–1.0) for each frame |
| `Duration` | `float` | Effective animation duration in seconds |
| `FrameCount` | `int` | Number of frames |

### AnimationConfig

**Kind**: Record (immutable)
**Thread Safety**: Immutable

| Field | Type | Default | Purpose |
|-------|------|---------|---------|
| `FrameCount` | `int` | `8` | Number of frames to capture |
| `SpeedMultiplier` | `float` | `1.0` | Playback speed override |
| `TrimStart` | `float` | `0.0` | Normalized start time (skip beginning) |
| `TrimEnd` | `float` | `1.0` | Normalized end time (skip ending) |
| `LoopMode` | `LoopMode` | `None` | Loop mode for output metadata |

### AnimationInfo

**Kind**: Record (immutable, provided by bridge)
**Thread Safety**: Immutable

| Field | Type | Purpose |
|-------|------|---------|
| `Name` | `string` | Animation clip name |
| `Duration` | `float` | Total duration in seconds |
| `FrameCount` | `int` | Source frame count (from FBX/animation data) |
| `IsLooping` | `bool` | Whether the source clip loops |

### AtlasOptions

**Kind**: Record (immutable)
**Thread Safety**: Immutable

| Field | Type | Default | Purpose |
|-------|------|---------|---------|
| `MaxWidth` | `int` | `4096` | Maximum atlas width in pixels |
| `MaxHeight` | `int` | `4096` | Maximum atlas height in pixels |
| `Padding` | `int` | `2` | Pixels between frames |
| `PowerOfTwo` | `bool` | `true` | Round atlas dimensions to next power of two |
| `GroupByAnimation` | `bool` | `true` | Visual row grouping hint (best-effort) |

### AtlasLayout

**Kind**: Record (immutable, output of AtlasPacker.Pack)
**Thread Safety**: Immutable

| Field | Type | Purpose |
|-------|------|---------|
| `Placements` | `IReadOnlyList<PackedFrame>` | Per-frame atlas positions |
| `AtlasWidths` | `IReadOnlyList<int>` | Width of each atlas image |
| `AtlasHeights` | `IReadOnlyList<int>` | Height of each atlas image |
| `AtlasCount` | `int` | Number of atlas images (1 unless overflow) |
| `Efficiency` | `float` | Ratio: total frame area / total atlas area |

### PackedFrame

**Kind**: Record (immutable)
**Thread Safety**: Immutable

| Field | Type | Purpose |
|-------|------|---------|
| `FrameIndex` | `int` | Original frame index from input |
| `AtlasIndex` | `int` | Which atlas this frame was placed in |
| `X` | `int` | Horizontal position in atlas |
| `Y` | `int` | Vertical position in atlas |
| `Width` | `int` | Frame width (without padding) |
| `Height` | `int` | Frame height (without padding) |

### NormalMapOptions

**Kind**: Record (immutable)
**Thread Safety**: Immutable

| Field | Type | Default | Purpose |
|-------|------|---------|---------|
| `Strength` | `float` | `1.0` | Normal map intensity multiplier |
| `BlurRadius` | `int` | `0` | Gaussian blur on depth before Sobel (0 = none) |

### SpriteSheet

**Kind**: Record (immutable, complete output)
**Thread Safety**: Immutable

| Field | Type | Purpose |
|-------|------|---------|
| `Version` | `string` | Schema version ("1.0") |
| `Generator` | `string` | "BeyondImmersion.Bannou.SpriteComposer" |
| `GeneratedAt` | `DateTimeOffset` | Capture timestamp |
| `Variant` | `CharacterVariant` | What was captured |
| `Rig` | `CameraRig` | Camera rig used |
| `Atlases` | `IReadOnlyList<AtlasInfo>` | Atlas image info (one or more) |
| `Animations` | `IReadOnlyList<SpriteAnimation>` | All animations with per-angle frame maps |
| `Frames` | `IReadOnlyList<SpriteFrame>` | All frames (captured + mirrors) |
| `CustomProperties` | `IReadOnlyDictionary<string, string>?` | Game-specific opaque metadata |

### AtlasInfo

**Kind**: Record (immutable)
**Thread Safety**: Immutable

| Field | Type | Purpose |
|-------|------|---------|
| `Index` | `int` | 0-based atlas index |
| `Filename` | `string` | Output filename for this atlas image |
| `Width` | `int` | Atlas width in pixels |
| `Height` | `int` | Atlas height in pixels |

### SpriteFrame

**Kind**: Record (immutable)
**Thread Safety**: Immutable

| Field | Type | Purpose |
|-------|------|---------|
| `Index` | `int` | Global frame index (unique) |
| `AtlasIndex` | `int` | Which atlas image |
| `AngleName` | `string` | Source angle or mirror target angle name |
| `AnimationName` | `string` | Animation name |
| `FrameInAnimation` | `int` | Frame number within animation (0-based) |
| `Rect` | `Rectangle` | Position and size in atlas |
| `TrimmedRect` | `Rectangle?` | Content bounds if TrimTransparent (null otherwise) |
| `Pivot` | `Vector2` | Pivot point (default: 0.5, 0.85) |
| `Duration` | `float` | Display duration in seconds |
| `IsMirror` | `bool` | True = flip of MirrorSourceIndex |
| `MirrorSourceIndex` | `int?` | Source frame index (set when IsMirror) |

### SpriteAnimation

**Kind**: Record (immutable)
**Thread Safety**: Immutable

| Field | Type | Purpose |
|-------|------|---------|
| `Name` | `string` | Animation name |
| `LoopMode` | `LoopMode` | None, Loop, PingPong |
| `TotalDuration` | `float` | Total duration in seconds |
| `AngleFrameMap` | `IReadOnlyDictionary<string, int[]>` | AngleName → ordered frame indices |
| `Events` | `IReadOnlyList<AnimationEvent>?` | Optional per-frame events |

### CharacterVariant

**Kind**: Record (immutable)
**Thread Safety**: Immutable

| Field | Type | Purpose |
|-------|------|---------|
| `Name` | `string` | Variant identifier ("warrior_plate_sword") |
| `ModelPath` | `string` | Path to base character model |
| `Equipment` | `IReadOnlyList<EquipmentSlot>` | Attached equipment |
| `MaterialOverrides` | `IReadOnlyDictionary<string, string>?` | Material/palette swaps |
| `Scale` | `float` | Model scale (default: 1.0) |
| `PivotOverride` | `Vector2?` | Per-variant pivot override (null = PivotComputer.ComputeFromBounds auto-compute) |

### EquipmentSlot

**Kind**: Record (immutable)
**Thread Safety**: Immutable

| Field | Type | Purpose |
|-------|------|---------|
| `SlotName` | `string` | Slot identifier ("head", "weapon_r") |
| `MeshPath` | `string` | Path to equipment mesh |
| `BoneName` | `string` | Skeleton bone to attach to |

### MirrorInfo

**Kind**: Record (immutable)
**Thread Safety**: Immutable

| Field | Type | Purpose |
|-------|------|---------|
| `SourceAngleName` | `string` | Captured angle that produces this mirror |
| `TargetAngleName` | `string` | Generated mirror angle name |
| `FlipAxis` | `MirrorAxis` | Horizontal or Vertical |

### AnimationEvent

**Kind**: Record (immutable)
**Thread Safety**: Immutable

| Field | Type | Purpose |
|-------|------|---------|
| `FrameIndex` | `int` | Which frame this event occurs on |
| `EventType` | `string` | Event category ("hit", "sound", "effect") |
| `EventData` | `string` | Event-specific payload |

### CaptureManifest

**Kind**: Record (immutable, output of CaptureManifest.Compute)
**Thread Safety**: Immutable

| Field | Type | Purpose |
|-------|------|---------|
| `Variant` | `CharacterVariant` | What will be captured |
| `Rigs` | `IReadOnlyList<RigManifest>` | Per-rig breakdown |
| `TotalCapturedFrames` | `int` | Frames requiring render passes |
| `TotalMirrorFrames` | `int` | Frames generated from mirrors |
| `TotalFrames` | `int` | Captured + mirror |
| `EstimatedCaptureTimeMs` | `int` | ~50ms per captured frame |
| `AnimationCount` | `int` | Number of animations |

### RigManifest

**Kind**: Record (immutable)
**Thread Safety**: Immutable

| Field | Type | Purpose |
|-------|------|---------|
| `RigName` | `string` | Camera rig name |
| `CapturedFrames` | `int` | Frames rendered for this rig |
| `MirrorFrames` | `int` | Mirror frames for this rig |
| `AngleCount` | `int` | Number of rendered angles |
| `MirrorCount` | `int` | Number of generated mirrors |

### FrameCapture

**Kind**: Record (immutable, created by bridge)
**Thread Safety**: Immutable

| Field | Type | Purpose |
|-------|------|---------|
| `PixelData` | `byte[]` | RGBA pixels (4 bytes/pixel, row-major) |
| `DepthData` | `float[]?` | Depth 0.0–1.0 (null if not captured) |
| `Width` | `int` | Frame width |
| `Height` | `int` | Frame height |
| `AngleName` | `string` | Source angle |
| `AnimationName` | `string` | Source animation |
| `FrameIndex` | `int` | Frame number in animation |
| `NormalizedTime` | `float` | Animation time when captured |

### Enums

| Enum | Values | Purpose |
|------|--------|---------|
| `ProjectionType` | `Orthographic`, `Perspective` | Camera projection mode |
| `LoopMode` | `None`, `Loop`, `PingPong` | Animation loop behavior |
| `MirrorAxis` | `Horizontal`, `Vertical` | Flip direction for mirrors |

### Shared Value Types

| Type | Size | Fields | Purpose |
|------|------|--------|---------|
| `Color` | 4 bytes | R, G, B, A (byte each) | RGBA color. Statics: `Transparent`, `White`, `Black` |
| `Vector2` | 8 bytes | X, Y (float each) | 2D point for pivots |
| `Rectangle` | 16 bytes | X, Y, Width, Height (int each) | Atlas frame position |

### Interfaces

| Interface | Methods | Purpose |
|-----------|---------|---------|
| `IPixelSource` | `byte[] GetPixels()`, `int Width`, `int Height` | Engine-agnostic RGBA pixel data |
| `IDepthSource` | `float[] GetDepth()`, `int Width`, `int Height` | Engine-agnostic depth buffer data |

---

## Dependencies

| Dependency | Type | Usage |
|------------|------|-------|
| `System.Text.Json` | BCL | SpriteSheet JSON serialization |
| `System.Numerics` | BCL | `MathF` for trigonometry in OrthographicSetup |

**No external NuGet dependencies.** Pure .NET BCL only.

---

## API Index

### CameraRigPresets

| Method | Signature | Det. | Allocation | Notes |
|--------|-----------|:----:|:----------:|-------|
| `SideViewBrawler` | `(int frameWidth = 128, int frameHeight = 128) → CameraRig` | Yes | Allocating | 1 angle + produces mirror (left) |
| `TopDown8Dir` | `(float pitch = -55f, int frameWidth = 96, int frameHeight = 96) → CameraRig` | Yes | Allocating | 5 angles, 3 produce mirrors = 8 dirs |
| `TopDown4Dir` | `(float pitch = -55f, int frameWidth = 96, int frameHeight = 96) → CameraRig` | Yes | Allocating | 3 angles, 1 produces mirror = 4 dirs |

### OrthographicSetup

| Method | Signature | Det. | Allocation | Notes |
|--------|-----------|:----:|:----------:|-------|
| `Compute` | `(CaptureAngle, BoundingBox, (int,int) frameSize) → OrthographicParameters` | Yes | Minimal | Returns one record |

### PivotComputer

| Method | Signature | Det. | Allocation | Notes |
|--------|-----------|:----:|:----------:|-------|
| `ComputeFromBounds` | `(BoundingBox, OrthographicParameters) → Vector2` | Yes | Free | Pure math; falls back to `DefaultHumanoidPivot` when camera basis is degenerate or ortho dimensions are zero |

### AnimationSampling

| Method | Signature | Det. | Allocation | Notes |
|--------|-----------|:----:|:----------:|-------|
| `GenerateUniform` | `(float duration, int frameCount) → FrameSequence` | Yes | Minimal | One float[] + record |
| `GenerateFromConfig` | `(AnimationInfo, AnimationConfig) → FrameSequence` | Yes | Minimal | One float[] + record |

### AtlasPacker

| Method | Signature | Det. | Allocation | Notes |
|--------|-----------|:----:|:----------:|-------|
| `Pack` | `(IReadOnlyList<(int w, int h, int index)>, AtlasOptions) → AtlasLayout` | Yes | Allocating | Free rect lists, placement list. Delegates overflow to `MultiAtlasStrategy.OpenNextAtlas`. |

### MultiAtlasStrategy

| Method | Signature | Det. | Allocation | Notes |
|--------|-----------|:----:|:----------:|-------|
| `OpenNextAtlas` | `(int currentAtlasIndex, List<Rectangle> freeRects, int maxW, int maxH, int pw, int ph, int frameIndex, int frameW, int frameH) → (int NewAtlasIndex, Rectangle Placement)` | Yes | Minimal | Mutates `freeRects` in place. Throws `InvalidOperationException` if padded frame exceeds max atlas dimensions. |

### MirrorOptimizer

| Method | Signature | Det. | Allocation | Notes |
|--------|-----------|:----:|:----------:|-------|
| `ComputeMirrors` | `(CameraRig) → IReadOnlyList<MirrorInfo>` | Yes | Minimal | Small list (~3 entries) |
| `GenerateMirrorFrames` | `(IReadOnlyList<SpriteFrame>, IReadOnlyList<MirrorInfo>) → IReadOnlyList<SpriteFrame>` | Yes | Allocating | One SpriteFrame per mirror frame |

### DepthToNormal

| Method | Signature | Det. | Allocation | Notes |
|--------|-----------|:----:|:----------:|-------|
| `Generate` | `(float[] depth, int w, int h, NormalMapOptions) → byte[]` | Yes | Allocating | Output byte[] (w×h×4) |

### AtlasAssembler

| Method | Signature | Det. | Allocation | Notes |
|--------|-----------|:----:|:----------:|-------|
| `Assemble` | `(IReadOnlyList<FrameCapture>, AtlasLayout, Color bg) → byte[][]` | Yes | Allocating | Atlas pixel arrays |

### SpriteSheetSerializer

| Method | Signature | Det. | Allocation | Notes |
|--------|-----------|:----:|:----------:|-------|
| `Serialize` | `(SpriteSheet) → string` | Yes | Allocating | JSON string |
| `Deserialize` | `(string json) → SpriteSheet` | Yes | Allocating | Full object graph |

### CaptureManifest

| Method | Signature | Det. | Allocation | Notes |
|--------|-----------|:----:|:----------:|-------|
| `Compute` | `(CharacterVariant, IReadOnlyList<CameraRig>, IReadOnlyList<(AnimationInfo,AnimationConfig)>) → CaptureManifest` | Yes | Minimal | Arithmetic + small lists |

---

## Methods

### CameraRigPresets.SideViewBrawler
`(frameWidth: int = 128, frameHeight: int = 128) → CameraRig`

CREATE angles ← [
  CaptureAngle(Name: "right", Yaw: 90, Pitch: 0,
    ProducesMirror: true, MirrorTargetName: "left", MirrorAxis: Horizontal)
]
RETURN CameraRig(
  Name: "SideView-Brawler",
  Projection: Orthographic,
  Angles: angles,
  FrameSize: (frameWidth, frameHeight),
  Padding: 2,
  BackgroundColor: Color.Transparent,
  IncludeNormalMap: false,
  TrimTransparent: false)

// Result: 1 rendered angle. MirrorOptimizer produces 1 mirror → 2 total directions.

---

### CameraRigPresets.TopDown8Dir
`(pitchDegrees: float = -55f, frameWidth: int = 96, frameHeight: int = 96) → CameraRig`

CREATE angles ← [
  CaptureAngle(Name: "N",  Yaw: 0,   Pitch: pitchDegrees, ProducesMirror: false),
  CaptureAngle(Name: "NE", Yaw: 45,  Pitch: pitchDegrees,
    ProducesMirror: true, MirrorTargetName: "NW", MirrorAxis: Horizontal),
  CaptureAngle(Name: "E",  Yaw: 90,  Pitch: pitchDegrees,
    ProducesMirror: true, MirrorTargetName: "W",  MirrorAxis: Horizontal),
  CaptureAngle(Name: "SE", Yaw: 135, Pitch: pitchDegrees,
    ProducesMirror: true, MirrorTargetName: "SW", MirrorAxis: Horizontal),
  CaptureAngle(Name: "S",  Yaw: 180, Pitch: pitchDegrees, ProducesMirror: false)
]
RETURN CameraRig(
  Name: "TopDown-8Dir",
  Projection: Orthographic,
  Angles: angles,
  FrameSize: (frameWidth, frameHeight),
  Padding: 2,
  BackgroundColor: Color.Transparent,
  IncludeNormalMap: false,
  TrimTransparent: false)

// Result: 5 rendered angles. MirrorOptimizer produces 3 mirrors → 8 total directions.

---

### CameraRigPresets.TopDown4Dir
`(pitchDegrees: float = -55f, frameWidth: int = 96, frameHeight: int = 96) → CameraRig`

CREATE angles ← [
  CaptureAngle(Name: "N", Yaw: 0,   Pitch: pitchDegrees, ProducesMirror: false),
  CaptureAngle(Name: "E", Yaw: 90,  Pitch: pitchDegrees,
    ProducesMirror: true, MirrorTargetName: "W", MirrorAxis: Horizontal),
  CaptureAngle(Name: "S", Yaw: 180, Pitch: pitchDegrees, ProducesMirror: false)
]
RETURN CameraRig(
  Name: "TopDown-4Dir",
  Projection: Orthographic,
  Angles: angles,
  FrameSize: (frameWidth, frameHeight),
  Padding: 2,
  BackgroundColor: Color.Transparent,
  IncludeNormalMap: false,
  TrimTransparent: false)

// Result: 3 rendered angles. MirrorOptimizer produces 1 mirror → 4 total directions.

---

### OrthographicSetup.Compute
`(angle: CaptureAngle, bounds: BoundingBox, frameSize: (int Width, int Height)) → OrthographicParameters`

// Step 1: Camera direction from yaw/pitch (spherical → Cartesian)
COMPUTE yawRad ← angle.Yaw * MathF.PI / 180f
COMPUTE pitchRad ← angle.Pitch * MathF.PI / 180f
COMPUTE dirX ← MathF.Sin(yawRad) * MathF.Cos(pitchRad)
COMPUTE dirY ← MathF.Sin(pitchRad)
COMPUTE dirZ ← MathF.Cos(yawRad) * MathF.Cos(pitchRad)
COMPUTE direction ← normalize(dirX, dirY, dirZ)

// Step 2: Camera position — back along direction from bounds center
COMPUTE center ← bounds.Center
COMPUTE halfDiag ← length(bounds.Extents)
COMPUTE distance ← halfDiag * 2.5f                  // 2.5× ensures no clipping at extreme angles
COMPUTE position ← center - direction * distance

// Step 3: Up vector (handle near-vertical pitch to avoid gimbal lock)
IF MathF.Abs(angle.Pitch) > 89f
  COMPUTE up ← (0, 0, -MathF.Sign(angle.Pitch))
ELSE
  COMPUTE up ← (0, 1, 0)

// Step 4: Orthonormal basis via cross products
COMPUTE right ← normalize(cross(direction, up))
COMPUTE correctedUp ← cross(right, direction)

// Step 5: Project all 8 bounding box corners onto the camera's view plane
COMPUTE corners[8] ← all (Min.X|Max.X, Min.Y|Max.Y, Min.Z|Max.Z) combinations
SET minU ← +INF, maxU ← -INF, minV ← +INF, maxV ← -INF
FOREACH corner in corners
  COMPUTE relative ← corner - position
  COMPUTE u ← dot(relative, right)
  COMPUTE v ← dot(relative, correctedUp)
  minU ← min(minU, u); maxU ← max(maxU, u)
  minV ← min(minV, v); maxV ← max(maxV, v)

// Step 6: Ortho dimensions with 10% safety margin
COMPUTE orthoWidth ← (maxU - minU) * 1.1f
COMPUTE orthoHeight ← (maxV - minV) * 1.1f

// Step 7: Match frame aspect ratio (expand the smaller dimension)
COMPUTE frameAspect ← (float)frameSize.Width / frameSize.Height
COMPUTE orthoAspect ← orthoWidth / orthoHeight
IF frameAspect > orthoAspect
  orthoWidth ← orthoHeight * frameAspect
ELSE
  orthoHeight ← orthoWidth / frameAspect

RETURN OrthographicParameters(
  Position: position,
  Direction: direction,
  Up: correctedUp,
  OrthoWidth: orthoWidth,
  OrthoHeight: orthoHeight,
  NearPlane: 0.01f,
  FarPlane: distance * 3f)

---

### PivotComputer.ComputeFromBounds
`(bounds: BoundingBox, camera: OrthographicParameters) → Vector2`

// Step 1: Feet world position = bottom-center of bounds (upright-humanoid convention)
COMPUTE feetX ← (bounds.Min.X + bounds.Max.X) * 0.5f
COMPUTE feetY ← bounds.Min.Y
COMPUTE feetZ ← (bounds.Min.Z + bounds.Max.Z) * 0.5f

// Step 2: Relative to camera
COMPUTE rel ← (feetX - camera.Position.X, feetY - camera.Position.Y, feetZ - camera.Position.Z)

// Step 3: Derive camera right axis from basis
COMPUTE right ← cross(camera.Direction, camera.Up)
COMPUTE rightLen ← length(right)
IF rightLen < 1e-10f
  // Degenerate basis (direction parallel to up) — no well-defined projection
  RETURN DefaultHumanoidPivot
COMPUTE right ← right / rightLen

// Step 4: Project onto (right, up) plane
COMPUTE u ← dot(rel, right)
COMPUTE v ← dot(rel, camera.Up)

// Step 5: Guard against invalid ortho dimensions
IF camera.OrthoWidth <= 0f OR camera.OrthoHeight <= 0f
  RETURN DefaultHumanoidPivot

// Step 6: Map to normalized frame coords
//   Pivot origin is top-left (Y grows downward), camera V is bottom-up, so flip Y.
COMPUTE pivotX ← 0.5f + u / camera.OrthoWidth
COMPUTE pivotY ← 0.5f - v / camera.OrthoHeight

// Step 7: Clamp to [0, 1] so downstream consumers never see out-of-frame pivots
RETURN Vector2(clamp01(pivotX), clamp01(pivotY))

---

### AnimationSampling.GenerateUniform
`(duration: float, frameCount: int) → FrameSequence`

VALIDATE frameCount > 0                              → ArgumentException
VALIDATE duration > 0                                → ArgumentException

CREATE timestamps ← new float[frameCount]
COMPUTE interval ← 1.0f / frameCount
FOREACH i FROM 0 TO frameCount - 1
  timestamps[i] ← i * interval + interval * 0.5f    // center of each time window
  // Example: 8 frames → [0.0625, 0.1875, 0.3125, ..., 0.9375]

RETURN FrameSequence(Timestamps: timestamps, Duration: duration, FrameCount: frameCount)

---

### AnimationSampling.GenerateFromConfig
`(info: AnimationInfo, config: AnimationConfig) → FrameSequence`

VALIDATE config.FrameCount > 0                       → ArgumentException
VALIDATE config.TrimEnd > config.TrimStart           → ArgumentException

COMPUTE effectiveRange ← config.TrimEnd - config.TrimStart
COMPUTE effectiveDuration ← info.Duration * effectiveRange / config.SpeedMultiplier
COMPUTE interval ← effectiveRange / config.FrameCount

CREATE timestamps ← new float[config.FrameCount]
FOREACH i FROM 0 TO config.FrameCount - 1
  timestamps[i] ← config.TrimStart + i * interval + interval * 0.5f

RETURN FrameSequence(
  Timestamps: timestamps,
  Duration: effectiveDuration,
  FrameCount: config.FrameCount)

---

### AtlasPacker.Pack
`(frames: IReadOnlyList<(int Width, int Height, int Index)>, options: AtlasOptions) → AtlasLayout`

// Sort by height descending, width descending, index ascending (stable deterministic order)
COMPUTE sorted ← frames.OrderByDescending(f => f.Height)
                       .ThenByDescending(f => f.Width)
                       .ThenBy(f => f.Index)
                       .ToList()

CREATE allPlacements ← new List<PackedFrame>()
SET currentAtlas ← 0
CREATE freeRects ← new List<Rectangle> { Rectangle(0, 0, options.MaxWidth, options.MaxHeight) }

FOREACH frame in sorted
  COMPUTE pw ← frame.Width + options.Padding
  COMPUTE ph ← frame.Height + options.Padding

  // Best Short Side Fit: minimize leftover short side
  SET bestRect ← null, bestShortSide ← int.MaxValue, bestLongSide ← int.MaxValue
  FOREACH rect in freeRects
    IF pw <= rect.Width AND ph <= rect.Height
      COMPUTE shortSide ← min(rect.Width - pw, rect.Height - ph)
      COMPUTE longSide ← max(rect.Width - pw, rect.Height - ph)
      IF shortSide < bestShortSide OR (shortSide == bestShortSide AND longSide < bestLongSide)
        bestRect ← rect; bestShortSide ← shortSide; bestLongSide ← longSide

  DECLARE best ← Rectangle
  IF bestRect is null
    // Multi-atlas overflow: delegate to MultiAtlasStrategy. Validates the frame fits
    // in a max-sized atlas, resets freeRects, and returns the placement rect (the
    // single free rect on a fresh atlas starts at origin).
    (currentAtlas, best) ← MultiAtlasStrategy.OpenNextAtlas(
      currentAtlas, freeRects, options.MaxWidth, options.MaxHeight,
      pw, ph, frame.Index, frame.Width, frame.Height)
  ELSE
    best ← bestRect.Value

  // Place frame at best position
  allPlacements.Add(PackedFrame(frame.Index, currentAtlas,
    best.X, best.Y, frame.Width, frame.Height))

  // MaxRects subdivision: split remaining free space around placed rectangle
  COMPUTE placed ← Rectangle(best.X, best.Y, pw, ph)
  CREATE newFreeRects ← new List<Rectangle>()
  FOREACH rect in freeRects
    IF NOT Intersects(rect, placed)
      newFreeRects.Add(rect)
      CONTINUE
    // Generate up to 4 remainder rectangles
    IF placed.X > rect.X
      newFreeRects.Add(Rectangle(rect.X, rect.Y, placed.X - rect.X, rect.Height))
    IF placed.X + pw < rect.X + rect.Width
      newFreeRects.Add(Rectangle(placed.X + pw, rect.Y, rect.X + rect.Width - placed.X - pw, rect.Height))
    IF placed.Y > rect.Y
      newFreeRects.Add(Rectangle(rect.X, rect.Y, rect.Width, placed.Y - rect.Y))
    IF placed.Y + ph < rect.Y + rect.Height
      newFreeRects.Add(Rectangle(rect.X, placed.Y + ph, rect.Width, rect.Y + rect.Height - placed.Y - ph))

  // Prune: remove free rects fully contained within another free rect
  freeRects ← PruneFreeRects(newFreeRects)

// Compute actual atlas dimensions per atlas
CREATE atlasWidths ← new int[currentAtlas + 1]
CREATE atlasHeights ← new int[currentAtlas + 1]
FOREACH atlasIdx FROM 0 TO currentAtlas
  COMPUTE maxX ← max of (p.X + p.Width) for placements where p.AtlasIndex == atlasIdx
  COMPUTE maxY ← max of (p.Y + p.Height) for placements where p.AtlasIndex == atlasIdx
  atlasWidths[atlasIdx] ← options.PowerOfTwo ? NextPowerOfTwo(maxX) : maxX
  atlasHeights[atlasIdx] ← options.PowerOfTwo ? NextPowerOfTwo(maxY) : maxY

COMPUTE totalFrameArea ← SUM(f.Width * f.Height for all frames)
COMPUTE totalAtlasArea ← SUM(atlasWidths[i] * atlasHeights[i] for all i)
COMPUTE efficiency ← (float)totalFrameArea / totalAtlasArea

RETURN AtlasLayout(allPlacements, atlasWidths, atlasHeights, currentAtlas + 1, efficiency)

---

### MultiAtlasStrategy.OpenNextAtlas
`(currentAtlasIndex: int, freeRects: List<Rectangle>, maxWidth: int, maxHeight: int, paddedFrameWidth: int, paddedFrameHeight: int, frameIndex: int, frameWidth: int, frameHeight: int) → (int NewAtlasIndex, Rectangle Placement)`

// Step 1: Validate the frame can fit in any atlas at the configured max dimensions.
// Done BEFORE mutating freeRects so callers with oversized frames fail fast.
VALIDATE paddedFrameWidth <= maxWidth AND paddedFrameHeight <= maxHeight
  → throw InvalidOperationException($"Frame (index={frameIndex}, {frameWidth}x{frameHeight}) exceeds maximum atlas dimensions ({maxWidth}x{maxHeight}).")

// Step 2: Reset free rectangle list to a single rect covering the new atlas
CREATE fullAtlas ← Rectangle(0, 0, maxWidth, maxHeight)
freeRects.Clear()
freeRects.Add(fullAtlas)

// Step 3: Return the new atlas index and the placement rect. The single free rect
// in a fresh atlas covers the whole atlas from origin — that IS the placement rect
// (caller does not need to re-run BSSF because there's only one free rect).
RETURN (currentAtlasIndex + 1, fullAtlas)

---

### MirrorOptimizer.ComputeMirrors
`(rig: CameraRig) → IReadOnlyList<MirrorInfo>`

CREATE mirrors ← new List<MirrorInfo>()
FOREACH angle in rig.Angles
  IF angle.ProducesMirror AND angle.MirrorTargetName is not null
    mirrors.Add(MirrorInfo(
      SourceAngleName: angle.Name,
      TargetAngleName: angle.MirrorTargetName,
      FlipAxis: angle.MirrorAxis))
RETURN mirrors

---

### MirrorOptimizer.GenerateMirrorFrames
`(capturedFrames: IReadOnlyList<SpriteFrame>, mirrors: IReadOnlyList<MirrorInfo>) → IReadOnlyList<SpriteFrame>`

CREATE mirrorFrames ← new List<SpriteFrame>()
SET nextIndex ← capturedFrames.Count

FOREACH mirror in mirrors
  COMPUTE sourceFrames ← capturedFrames.Where(f => f.AngleName == mirror.SourceAngleName)
                                        .OrderBy(f => f.Index)
  FOREACH source in sourceFrames
    mirrorFrames.Add(SpriteFrame(
      Index: nextIndex,
      AtlasIndex: source.AtlasIndex,
      AngleName: mirror.TargetAngleName,
      AnimationName: source.AnimationName,
      FrameInAnimation: source.FrameInAnimation,
      Rect: source.Rect,                           // same atlas position — game flips at render
      TrimmedRect: source.TrimmedRect,
      Pivot: FlipPivot(source.Pivot, mirror.FlipAxis),
      Duration: source.Duration,
      IsMirror: true,
      MirrorSourceIndex: source.Index))
    nextIndex += 1

RETURN mirrorFrames

FUNCTION FlipPivot(pivot: Vector2, axis: MirrorAxis) → Vector2
  IF axis == Horizontal
    RETURN Vector2(1.0f - pivot.X, pivot.Y)
  ELSE
    RETURN Vector2(pivot.X, 1.0f - pivot.Y)

---

### DepthToNormal.Generate
`(depth: float[], width: int, height: int, options: NormalMapOptions) → byte[]`

VALIDATE depth.Length == width * height              → ArgumentException

IF options.BlurRadius > 0
  depth ← GaussianBlur(depth, width, height, options.BlurRadius)

CREATE normals ← new byte[width * height * 4]

FOREACH y FROM 0 TO height - 1
  FOREACH x FROM 0 TO width - 1
    // Sobel 3×3 horizontal gradient
    COMPUTE dzdx ← (
      S(x+1,y-1) + 2*S(x+1,y) + S(x+1,y+1)
      - S(x-1,y-1) - 2*S(x-1,y) - S(x-1,y+1)
    ) * options.Strength

    // Sobel 3×3 vertical gradient
    COMPUTE dzdy ← (
      S(x-1,y+1) + 2*S(x,y+1) + S(x+1,y+1)
      - S(x-1,y-1) - 2*S(x,y-1) - S(x+1,y-1)
    ) * options.Strength

    // Normal vector (tangent space: Z outward from sprite plane)
    COMPUTE len ← MathF.Sqrt(dzdx*dzdx + dzdy*dzdy + 1.0f)
    COMPUTE nx ← -dzdx / len
    COMPUTE ny ← -dzdy / len
    COMPUTE nz ← 1.0f / len

    // Encode [-1,1] → [0,255]
    COMPUTE i ← (y * width + x) * 4
    normals[i+0] ← (byte)((nx * 0.5f + 0.5f) * 255f)
    normals[i+1] ← (byte)((ny * 0.5f + 0.5f) * 255f)
    normals[i+2] ← (byte)((nz * 0.5f + 0.5f) * 255f)
    normals[i+3] ← 255

RETURN normals

FUNCTION S(x: int, y: int) → float                  // sample with clamped boundary
  RETURN depth[clamp(y, 0, height-1) * width + clamp(x, 0, width-1)]

---

### AtlasAssembler.Assemble
`(frames: IReadOnlyList<FrameCapture>, layout: AtlasLayout, backgroundColor: Color) → byte[][]`

CREATE atlases ← new byte[layout.AtlasCount][]

FOREACH i FROM 0 TO layout.AtlasCount - 1
  COMPUTE w ← layout.AtlasWidths[i]
  COMPUTE h ← layout.AtlasHeights[i]
  atlases[i] ← new byte[w * h * 4]
  // Fill with background color
  FOREACH p FROM 0 TO w * h - 1
    atlases[i][p*4+0] ← backgroundColor.R
    atlases[i][p*4+1] ← backgroundColor.G
    atlases[i][p*4+2] ← backgroundColor.B
    atlases[i][p*4+3] ← backgroundColor.A

FOREACH placement in layout.Placements
  COMPUTE frame ← frames[placement.FrameIndex]
  COMPUTE atlas ← atlases[placement.AtlasIndex]
  COMPUTE atlasW ← layout.AtlasWidths[placement.AtlasIndex]
  FOREACH row FROM 0 TO frame.Height - 1
    COMPUTE src ← row * frame.Width * 4
    COMPUTE dst ← ((placement.Y + row) * atlasW + placement.X) * 4
    Buffer.BlockCopy(frame.PixelData, src, atlas, dst, frame.Width * 4)

RETURN atlases

---

### SpriteSheetSerializer.Serialize
`(spriteSheet: SpriteSheet) → string`

COMPUTE options ← new JsonSerializerOptions {
  PropertyNamingPolicy: JsonNamingPolicy.CamelCase,
  WriteIndented: true,
  DefaultIgnoreCondition: JsonIgnoreCondition.WhenWritingNull
}
RETURN JsonSerializer.Serialize(spriteSheet, options)

---

### SpriteSheetSerializer.Deserialize
`(json: string) → SpriteSheet`

COMPUTE options ← new JsonSerializerOptions {
  PropertyNamingPolicy: JsonNamingPolicy.CamelCase,
  PropertyNameCaseInsensitive: true
}
RETURN JsonSerializer.Deserialize<SpriteSheet>(json, options)
  ?? throw new JsonException("Failed to deserialize SpriteSheet")

---

### CaptureManifest.Compute
`(variant: CharacterVariant, rigs: IReadOnlyList<CameraRig>, animations: IReadOnlyList<(AnimationInfo Info, AnimationConfig Config)>) → CaptureManifest`

SET totalCaptured ← 0
SET totalMirror ← 0
CREATE rigManifests ← new List<RigManifest>()

FOREACH rig in rigs
  // Every angle in rig.Angles is captured
  COMPUTE angleCount ← rig.Angles.Count
  // Mirror count from angles with ProducesMirror = true
  COMPUTE mirrorCount ← rig.Angles.Count(a => a.ProducesMirror)
  SET rigCaptured ← 0
  SET rigMirror ← 0

  FOREACH (info, config) in animations
    rigCaptured += angleCount * config.FrameCount
    rigMirror += mirrorCount * config.FrameCount

  totalCaptured += rigCaptured
  totalMirror += rigMirror
  rigManifests.Add(RigManifest(rig.Name, rigCaptured, rigMirror, angleCount, mirrorCount))

RETURN CaptureManifest(
  Variant: variant,
  Rigs: rigManifests,
  TotalCapturedFrames: totalCaptured,
  TotalMirrorFrames: totalMirror,
  TotalFrames: totalCaptured + totalMirror,
  EstimatedCaptureTimeMs: totalCaptured * 50,
  AnimationCount: animations.Count)

// Verification for Defenders (TopDown8Dir + SideViewBrawler, 20 animations × 8 frames):
//   TopDown8Dir:  5 angles × 20 × 8 = 800 captured, 3 mirrors × 20 × 8 = 480 mirror
//   SideViewBrawler: 1 angle × 20 × 8 = 160 captured, 1 mirror × 20 × 8 = 160 mirror
//   Total: 960 captured + 640 mirror = 1600 total frames
//   Estimated capture time: 960 × 50ms = 48 seconds

---

## Algorithms

### MaxRects-BSSF (Best Short Side Fit)

**Purpose**: Pack variable-sized rectangles into fixed-size bins with minimal wasted space.
**Complexity**: O(n × r) time where n = frames and r = free rectangles (typically r ≈ n). O(r) space for free rectangle list.
**Reference**: Jukka Jylänki, "A Thousand Ways to Pack the Bin — A Practical Approach to Two-Dimensional Rectangle Bin Packing" (2010).

INPUT: Sorted list of (width, height, index) frames, AtlasOptions (max size, padding, power-of-two)
OUTPUT: AtlasLayout with per-frame placements and per-atlas dimensions

The algorithm maintains a list of "free rectangles" — regions of the atlas not yet occupied. For each frame to place:

1. **Select**: Find the free rectangle where the frame fits with the smallest remaining short side. Tie-break on long side. This is the BSSF heuristic — it minimizes wasted space along the tighter dimension.

2. **Place**: Position the frame at the free rectangle's origin.

3. **Split**: The placed frame divides the free rectangle into up to 4 new free rectangles (left, right, top, bottom remainders). Each remainder must have positive width and height.

4. **Prune**: Remove any free rectangle that is fully contained within another free rectangle. This prevents the free list from growing unboundedly with redundant entries.

5. **Overflow**: If no free rectangle fits the current frame, start a new atlas with a fresh free list covering the full atlas dimensions. The frame is guaranteed to fit in the new atlas (assuming frame ≤ MaxSize).

**Sort order**: Height descending, width descending, index ascending. Height-first is the standard heuristic — tall frames constrain vertical space earliest, giving the packer maximum horizontal flexibility for subsequent shorter frames. Index ascending provides deterministic tie-breaking.

**Power-of-two rounding**: After all frames are placed, actual atlas dimensions are rounded up to the next power of two if `PowerOfTwo = true`. This is required by some GPU texture formats and improves GPU memory alignment.

---

### Sobel 3×3 Normal Map Generation

**Purpose**: Estimate surface normal vectors from a depth image for tangent-space normal mapping.
**Complexity**: O(w × h) time, O(w × h) space for output normal map. Per-pixel independent — trivially parallelizable.
**Reference**: Irwin Sobel and Gary Feldman, "A 3×3 Isotropic Gradient Operator for Image Processing" (Stanford AI Project, 1968).

INPUT: float[] depth buffer (w × h, values 0.0–1.0), NormalMapOptions (strength, blur radius)
OUTPUT: byte[] RGBA normal map (w × h × 4, tangent-space encoded)

The Sobel operator estimates the horizontal and vertical gradients of the depth field at each pixel using a 3×3 weighted convolution:

```
Horizontal kernel (Gx):        Vertical kernel (Gy):
[-1  0  +1]                    [-1  -2  -1]
[-2  0  +2]                    [ 0   0   0]
[-1  0  +1]                    [+1  +2  +1]
```

For each pixel (x, y):
1. Apply Gx kernel to 3×3 neighborhood → dz/dx (horizontal gradient)
2. Apply Gy kernel to 3×3 neighborhood → dz/dy (vertical gradient)
3. Scale gradients by `options.Strength` (controls normal map intensity)
4. Construct normal: `normalize(-dzdx, -dzdy, 1.0)` — Z points outward from the sprite plane
5. Encode to RGB: map each component from [-1, 1] to [0, 255]. A = 255 (opaque).

**Boundary handling**: Pixels at image edges use clamped sampling — edge values are repeated rather than wrapped or zeroed. This prevents dark-border artifacts on normal maps.

**Optional blur**: When `BlurRadius > 0`, a Gaussian blur is applied to the depth buffer before Sobel convolution. This smooths high-frequency depth noise at the cost of reduced normal map sharpness. Useful for models with very fine geometric detail that produces noisy normals at sprite resolution.

---

## Serialization Formats

### Sprite Sheet Metadata (JSON)

**Purpose**: Canonical output format for sprite sheet metadata. Engine-agnostic, language-agnostic.
**Encoding**: UTF-8 JSON via System.Text.Json with camelCase property naming.

The JSON format is defined by the `SpriteSheet` record type. See the deep dive's Format Support section and the planning document ([SPRITE-COMPOSER-SDK.md](../../planning/SPRITE-COMPOSER-SDK.md) § JSON Metadata Schema) for a complete JSON example with all fields.

**Key design choices**:
- `camelCase` property names (System.Text.Json convention, matches JavaScript consumption)
- `null` values omitted (`JsonIgnoreCondition.WhenWritingNull`) — reduces file size for optional fields
- `writeIndented: true` — human-readable output for debugging; consumers should accept both indented and compact
- Mirror frames included inline in the `frames` array with `isMirror: true` — no separate mirrors collection

No custom binary format. All atlas images are standard PNG (produced by the consumer, not by sprite-theory).

---

*This document contains pseudo-code representing intended behavior. Verify against actual implementation once the SDK is built.*
