# Sprite Theory SDK Deep Dive

> **SDK**: sprite-theory
> **Location**: `sdks/sprite-theory/`
> **Layer**: Theory
> **Domain**: Sprite
> **Dependencies**: `BeyondImmersion.Bannou.Core` (for the shared `Vector3` primitive). No external NuGet dependencies.
> **Status**: Implemented — all types and methods from implementation map complete. 100 unit tests passing.
> **Implementation Map**: [docs/sdks/maps/SPRITE-THEORY.md](maps/SPRITE-THEORY.md)
> **Planning Document**: [docs/planning/SPRITE-COMPOSER-SDK.md](../planning/SPRITE-COMPOSER-SDK.md)
> **Consumers**: sprite-composer, sprite-composer-stride, future SpriteBatcher
> **Short**: Camera math, atlas packing, mirror optimization, normal maps, and sprite sheet JSON metadata

---

## Overview

sprite-theory is the theory-layer SDK for the sprite domain. It provides pure-computation primitives for defining camera rigs, computing orthographic projections, sampling animation frames, packing frames into atlas layouts (MaxRects bin-packing), generating mirror metadata from source angles, converting depth buffers to tangent-space normal maps (Sobel filtering), and serializing sprite sheet metadata to a canonical JSON format.

It follows the same pattern as music-theory, storyline-theory, and voxel-core: pure computation, zero service dependencies, deterministic, usable on both client and server. Any code that needs to define capture configurations, compute atlas layouts, generate mirror metadata, produce normal maps from depth data, or read/write sprite sheet metadata depends on this SDK.

**The theory-layer role**: sprite-theory does NOT render anything — it has no engine dependency and no pixel capture capability. It computes WHERE frames go in an atlas, WHICH angles produce mirrors, HOW depth data converts to normals, and WHAT the metadata format looks like. The actual rendering, model loading, and frame capture happen in the engine bridge (sprite-composer-stride). sprite-theory produces the mathematics and data structures that the bridge and orchestrator consume.

---

## Established Patterns and Prior Art

### MaxRects Bin Packing

**Pattern**: Pack variable-sized rectangles into a fixed-size bin with minimal wasted space.
**Source**: Jukka Jylänki, "A Thousand Ways to Pack the Bin — A Practical Approach to Two-Dimensional Rectangle Bin Packing" (2010). The definitive survey of 2D bin-packing algorithms for game development. The MaxRects-BSSF (Best Short Side Fit) variant provides near-optimal results with O(n²) time complexity in the number of free rectangles.
**Alternatives considered**:
- **Shelf packing** (simple row-by-row): Faster but wastes significant vertical space when frame sizes vary. Not suitable when TrimTransparent produces variable-height frames.
- **Guillotine**: Splits free space with full-width or full-height cuts. Simpler to implement but produces worse packing ratios than MaxRects for rectangular sprite frames.
- **Skyline**: Good for fixed-height rows but suboptimal when frames have padding and variable trim bounds.

**Why MaxRects-BSSF**: Industry standard for sprite atlas packing. Used by TexturePacker, libGDX, Unity's Sprite Packer, and Godot's atlas import. Height-descending sort as the primary heuristic is validated across decades of production use. For uniform-sized frames (the common case in sprite capture), MaxRects degenerates to near-perfect grid packing.

### Sobel Operator for Normal Map Generation

**Pattern**: Estimate surface gradients from a depth image using a 3×3 convolution kernel to produce tangent-space normal maps.
**Source**: Irwin Sobel and Gary Feldman, "A 3×3 Isotropic Gradient Operator for Image Processing" (presented at the Stanford AI Project, 1968). The Sobel operator is the standard first-derivative approximation for discrete images. Used universally in edge detection, normal map generation, and image processing.
**Why Sobel over alternatives**:
- **Central difference** (2-tap): Cheaper but noisier — no Gaussian smoothing component.
- **Scharr** (optimized 3×3): Better rotational symmetry but negligible improvement for sprite-sized frames (128×128).
- **Prewitt** (unweighted 3×3): Less noise suppression than Sobel's center-weighted kernel.

**Dead Cells precedent**: Motion Twin used depth-buffer-derived normal maps on pre-rendered sprites to enable 2D dynamic lighting — the same technique this SDK implements. The normal map atlas shares the color atlas's layout, enabling same-UV sampling in the game's lighting shader.

### Orthographic Projection for Sprite Capture

**Pattern**: Use orthographic (parallel) projection to render 3D models as 2D sprites with no perspective foreshortening.
**Source**: Standard computer graphics — orthographic projection preserves parallel lines and relative sizes regardless of depth, producing sprites that tile and animate consistently.
**Why orthographic, not perspective**: Perspective projection introduces foreshortening — characters closer to the camera appear larger than those farther away. For sprites used in 2D gameplay, this distortion would cause inconsistent sizing between frames of the same animation (arms reaching toward camera would grow). Orthographic projection eliminates this entirely. Both Hades (Supergiant Games) and Dead Cells (Motion Twin) use orthographic capture for this reason.

---

## Consumers

| Consumer | Type | Usage |
|----------|------|-------|
| sprite-composer | SDK | Uses CameraRig, AtlasPacker, MirrorOptimizer, SpriteSheetSerializer, DepthToNormal, AtlasAssembler, and all metadata types for capture orchestration and export |
| sprite-composer-stride | Bridge | Uses OrthographicSetup to configure Stride cameras, CaptureAngle for positioning, FrameCapture as the pixel data container |
| Future SpriteBatcher | Tool | Uses CameraRigPresets for batch definitions, AtlasPacker for layout, SpriteSheetSerializer for output |
| Game runtime | Consumer | Reads SpriteSheet JSON metadata to drive sprite animation, mirror flips, and normal map sampling |
| CI/CD pipeline | Tool | Uses SpriteSheetSerializer for validation, CaptureManifest for expected output verification |

---

## Public API Surface

### Camera Subsystem

| Type | Kind | Purpose |
|------|------|---------|
| `CameraRig` | Record | Rig definition: projection type, capture angles, frame size, padding, options |
| `CaptureAngle` | Record | Single capture angle: name, yaw, pitch, optional mirror production metadata |
| `CameraRigPresets` | Static class | Built-in rig factories: `SideViewBrawler()`, `TopDown8Dir()`, `TopDown4Dir()` |
| `OrthographicSetup` | Static class | Compute orthographic camera parameters to fit a bounding box at a given angle |
| `OrthographicParameters` | Record | Output of OrthographicSetup: camera position, direction, up, ortho dimensions, clip planes |
| `PivotComputer` | Static class | Auto-compute sprite pivot from bounding box + orthographic camera (feet projection), and project arbitrary world points onto the frame plane via `ProjectWorldPointToFrame` (e.g., skeleton bone anchors) |
| `ProjectionType` | Enum | `Orthographic`, `Perspective` |
| `BoundingBox` | Struct | Axis-aligned 3D bounding box (min/max `Vector3`) for model bounds |

### Animation Subsystem

| Type | Kind | Purpose |
|------|------|---------|
| `AnimationSampling` | Static class | Generate frame timestamp sequences from animation duration and config |
| `FrameSequence` | Record | Ordered list of normalized frame timestamps (0.0–1.0) for one animation capture |
| `AnimationConfig` | Record | Per-animation capture settings: frame count, speed multiplier, trim start/end, loop mode |
| `AnimationInfo` | Record | Animation metadata reported by the bridge: name, duration, frame count, is looping |
| `LoopMode` | Enum | `None`, `Loop`, `PingPong` |

### Atlas Subsystem

| Type | Kind | Purpose |
|------|------|---------|
| `AtlasPacker` | Static class | MaxRects-BSSF bin-packing: frames → atlas layout. Delegates multi-atlas overflow to `MultiAtlasStrategy`. |
| `MultiAtlasStrategy` | Static class | Overflow handling when frames exceed single atlas size: validates fit, opens next atlas, returns placement rect |
| `AtlasLayout` | Record | Packing result: per-frame placements, per-atlas dimensions, packing efficiency, atlas count |
| `AtlasOptions` | Record | Configuration: max atlas size, padding, power-of-two rounding, group-by-animation |
| `PackedFrame` | Record | Single frame's placement: atlas index, x, y, original width, original height |

### Mirror Subsystem

| Type | Kind | Purpose |
|------|------|---------|
| `MirrorOptimizer` | Static class | Analyze CameraRig angles, compute mirror relationships, generate mirror SpriteFrame entries |
| `MirrorInfo` | Record | One mirror relationship: source angle name, target angle name, flip axis |
| `MirrorAxis` | Enum | `Horizontal`, `Vertical` |

### Metadata Subsystem

| Type | Kind | Purpose |
|------|------|---------|
| `SpriteSheet` | Record | Complete output metadata: variant, rig, atlases, frames (real + mirror), animations |
| `SpriteFrame` | Record | Per-frame: atlas position, pivot, duration, angle, animation, mirror source if applicable |
| `SpriteAnimation` | Record | Per-animation: name, loop mode, total duration, per-angle frame index lookup |
| `AtlasInfo` | Record | Per-atlas image: index, filename, width, height |
| `CharacterVariant` | Record | Capture input definition: model `AssetReference`, equipment list, material-override `AssetReference` map, scale, optional pivot override, optional anchor-bone name |
| `EquipmentSlot` | Record | Single equipment attachment: slot name, mesh `AssetReference`, skeleton bone name |
| `AssetReference` | Record | Engine-agnostic asset identifier (`BundleId`, `AssetId`, optional `VariantId`) used for model, equipment mesh, and material references |
| `CaptureManifest` | Record | Pre-capture estimation: expected frame counts, atlas sizes, capture time per rig |
| `RigManifest` | Record | Per-rig estimation within a CaptureManifest |
| `AnimationEvent` | Record | Optional per-frame event marker: frame index, event type string, event data string |

### Normal Map Subsystem

| Type | Kind | Purpose |
|------|------|---------|
| `DepthToNormal` | Static class | Sobel 3×3 convolution: depth buffer float[] → tangent-space normal map RGBA byte[] |
| `NormalMapOptions` | Record | Configuration: strength multiplier, blur radius |

### Export Subsystem

| Type | Kind | Purpose |
|------|------|---------|
| `SpriteSheetSerializer` | Static class | SpriteSheet ↔ JSON serialization using System.Text.Json |
| `IPixelSource` | Interface | Engine-agnostic abstraction for raw RGBA pixel data + dimensions |
| `IDepthSource` | Interface | Engine-agnostic abstraction for depth buffer float data + dimensions |
| `AtlasAssembler` | Static class | Compose captured FrameCapture pixel data into atlas RGBA byte arrays using AtlasLayout |

### Shared Types

| Type | Kind | Purpose |
|------|------|---------|
| `Color` | Struct (4 bytes) | RGBA color, byte-valued (0–255 per channel) |
| `Vector2` | Struct (8 bytes) | 2D float vector for pivot points |
| `Vector3` | Struct (12 bytes) | 3D float vector imported from `BeyondImmersion.Bannou.Core.Math`. Used by `BoundingBox`, `OrthographicParameters`, `PivotComputer.ProjectWorldPointToFrame`, and the sprite-composer bridge contract. |
| `Rectangle` | Struct (16 bytes) | Integer rectangle (x, y, width, height) for atlas positions |
| `FrameCapture` | Record | Bridge-produced capture data: RGBA pixels, optional depth, dimensions, source metadata |

---

## Data Model

### CameraRig

The primary configuration type. Defines how a character is captured from one orientation. **Every angle in the `Angles` list is captured (rendered).** Mirror targets are NOT in this list — they are computed externally by MirrorOptimizer.

```
CameraRig
├── Name: string                        // "SideView-Brawler", "TopDown-8Dir", custom
├── Projection: ProjectionType          // Orthographic (standard) or Perspective (rare)
├── Angles: IReadOnlyList<CaptureAngle> // ALL of these are rendered — NO mirror targets here
├── FrameSize: (int Width, int Height)  // Per-frame pixel dimensions (e.g., 128×128)
├── Padding: int                        // Pixels between frames in atlas (default: 2)
├── BackgroundColor: Color              // Render clear color (default: Color.Transparent)
├── IncludeNormalMap: bool              // Also generate normal map atlas from depth buffer
└── TrimTransparent: bool              // Trim transparent borders per frame (default: false)
```

**Key invariant**: `Angles` contains ONLY angles that will be rendered by the bridge. If the rig has 5 angles, the bridge renders 5 times per animation frame. Mirror targets (like "NW" generated from "NE") exist only in MirrorOptimizer's output and in the final SpriteSheet metadata — never in the rig definition.

**Immutability**: CameraRig is a record — value equality and immutability by default. Changes produce new instances. This supports undo/redo in the composer (commands store before/after rig snapshots).

### CaptureAngle

A single angle that the bridge will render from. Every CaptureAngle in a rig is captured. Optionally declares that it also produces a mirror.

```
CaptureAngle
├── Name: string               // "right", "N", "NE", "SE", etc.
├── Yaw: float                 // Degrees rotation around Y axis (0 = north/forward)
├── Pitch: float               // Degrees from horizontal (0 = level, negative = looking down)
├── ProducesMirror: bool       // If true, MirrorOptimizer will generate a flipped counterpart
├── MirrorTargetName: string?  // Name for the generated mirror (e.g., "NE" produces "NW")
└── MirrorAxis: MirrorAxis     // Horizontal (default) or Vertical flip for the mirror
```

**ProducesMirror semantics (CRITICAL)**: This boolean means "this angle IS captured AND ALSO generates a mirror." It does NOT mean "this angle is a mirror" or "this angle is not captured." Every CaptureAngle is always captured. `ProducesMirror` is purely additive metadata that MirrorOptimizer reads.

**Example — TopDown8Dir**:
- N (Yaw=0°, ProducesMirror=false) → captured, no mirror
- NE (Yaw=45°, ProducesMirror=true, MirrorTargetName="NW") → captured AND produces "NW" mirror
- E (Yaw=90°, ProducesMirror=true, MirrorTargetName="W") → captured AND produces "W" mirror
- SE (Yaw=135°, ProducesMirror=true, MirrorTargetName="SW") → captured AND produces "SW" mirror
- S (Yaw=180°, ProducesMirror=false) → captured, no mirror

Result: 5 angles captured, 3 mirrors generated by MirrorOptimizer = 8 total directions.

**Example — SideViewBrawler**:
- right (Yaw=90°, ProducesMirror=true, MirrorTargetName="left") → captured AND produces "left" mirror

Result: 1 angle captured, 1 mirror generated = 2 total directions.

### SpriteSheet (Output Metadata)

The complete output of a capture session. Contains both real captured frames and mirror frame entries. Serializes to the canonical JSON format via SpriteSheetSerializer.

```
SpriteSheet
├── Version: string                            // Schema version ("1.0")
├── Generator: string                          // "BeyondImmersion.Bannou.SpriteComposer"
├── GeneratedAt: DateTimeOffset                // Capture timestamp
├── Variant: CharacterVariant                  // What was captured (model + equipment)
├── Rig: CameraRig                             // Camera rig used (source angles only)
├── Atlases: IReadOnlyList<AtlasInfo>          // Atlas image files (one or more if overflow)
├── Animations: IReadOnlyList<SpriteAnimation> // All captured animations with per-angle frame maps
├── Frames: IReadOnlyList<SpriteFrame>         // ALL frames — captured first, then mirrors appended
└── CustomProperties: Dictionary<string, string>? // Game-specific opaque metadata (not read by SDK)
```

**CustomProperties**: Optional game-specific metadata embedded in the output JSON. Not read, validated, or interpreted by any SDK component. Example uses: game build version string, character class tag, content pipeline batch ID. Null when not provided.

**Atlases**: When frames overflow a single atlas (exceeding AtlasOptions.MaxWidth × MaxHeight), AtlasPacker produces multiple atlas images. Each AtlasInfo has an `Index` (0-based), `Filename`, `Width`, and `Height`. Each SpriteFrame references its atlas by `AtlasIndex`.

### SpriteFrame

Per-frame metadata. Frames with `IsMirror = true` have no unique atlas pixels — they reference a source frame that the game engine must flip horizontally at render time.

```
SpriteFrame
├── Index: int                  // Global frame index (0-based, unique across all frames)
├── AtlasIndex: int             // Which atlas image (0 for single-atlas, >0 for overflow)
├── AngleName: string           // Angle name — source angle for real frames, mirror target for mirrors
├── AnimationName: string       // Animation name ("idle", "attack_light", etc.)
├── FrameInAnimation: int       // Frame number within the animation (0-based)
├── Rect: Rectangle             // Position and size in atlas (x, y, width, height)
├── TrimmedRect: Rectangle?     // Content bounds if TrimTransparent was enabled (null otherwise)
├── Pivot: Vector2              // Pivot point normalized to frame (0,0 = top-left; default: 0.5, 0.85)
├── Duration: float             // Frame display duration in seconds
├── IsMirror: bool              // True = horizontal flip of MirrorSourceIndex frame
└── MirrorSourceIndex: int?     // Source frame index (only set when IsMirror = true)
```

**Frame ordering**: Captured frames come first (indices 0 through N-1), mirror frames are appended after (indices N through N+M-1). This ordering is deterministic and stable.

**Pivot resolution**: Consumers assemble `SpriteFrame.Pivot` in this order — `CharacterVariant.PivotOverride` if non-null, otherwise `PivotComputer.ComputeFromBounds(bounds, orthographicParameters)` for a bounds-derived pivot, otherwise `PivotComputer.DefaultHumanoidPivot` `(0.5, 0.85)`. `PivotComputer` projects the bottom-center of the bounding box onto the camera's frame plane and clamps the result to `[0, 1]`.

### SpriteAnimation

Groups frames by animation name with per-angle frame index lookup for game runtime use.

```
SpriteAnimation
├── Name: string                                 // Animation name (matches source clip name)
├── LoopMode: LoopMode                           // None, Loop, PingPong
├── TotalDuration: float                         // Total animation duration in seconds
├── AngleFrameMap: Dictionary<string, int[]>     // AngleName → ordered frame indices
└── Events: IReadOnlyList<AnimationEvent>?       // Optional hit frames, sound cues (null if none)
```

**AngleFrameMap**: The primary runtime lookup. To play "idle" facing "NE", read `AngleFrameMap["NE"]` → array of frame indices → look up each SpriteFrame by index for rect, duration, and mirror info. Mirror angles (like "NW") are included in the map — they point to mirror SpriteFrame entries.

### CharacterVariant

Input definition describing what to capture. Serializable as part of SpriteSheet metadata and SpriteProject files.

```
CharacterVariant
├── Name: string                                                       // "warrior_plate_sword"
├── Model: AssetReference                                              // Reference to base character model
├── Equipment: IReadOnlyList<EquipmentSlot>                            // Attached equipment pieces
├── MaterialOverrides: IReadOnlyDictionary<string, AssetReference>?    // Material/palette swaps (null if none)
├── Scale: float                                                       // Model scale factor (default: 1.0)
├── PivotOverride: Vector2?                                            // Per-variant pivot override (highest priority)
└── AnchorBoneName: string?                                            // Optional skeleton-bone anchor (bridge-resolved)
```

**Pivot resolution order** (applied by consumers such as sprite-composer during capture):

1. **`PivotOverride`** — if non-null, used verbatim. Highest priority; bypasses every auto-computation path.
2. **`AnchorBoneName`** — if non-null AND the bridge advertises `SupportsSkeletonIntrospection` AND the bone exists, the bridge's `TryGetBonePosition` returns the bone's world position, which is projected onto the frame plane via `PivotComputer.ProjectWorldPointToFrame`. Use this for subjects whose bounding-box minimum is not a sensible anchor (cloth extending bounds below the feet, weapons extending bounds outward, asymmetric rigs).
3. **`PivotComputer.ComputeFromBounds`** — fallback for the common feet-on-ground case. Projects the bottom-center of the bounding box onto the frame plane.
4. **`PivotComputer.DefaultHumanoidPivot`** — terminal fallback `(0.5, 0.85)` when the camera basis is degenerate.

Use `PivotOverride` for subjects where even a bone anchor is unreliable. Use `AnchorBoneName` for the common case of flowing clothing or asymmetric gear — it avoids hand-tuning pivots per variant while still producing correct feet placement. Leave both null to accept the feet-from-bounds default.

### FrameCapture

Engine-agnostic container for captured pixel data. Created by the bridge after each frame render, consumed by AtlasAssembler and DepthToNormal.

```
FrameCapture
├── PixelData: byte[]      // RGBA pixels, 4 bytes per pixel, row-major top-to-bottom
├── DepthData: float[]?    // Depth values 0.0 (near) to 1.0 (far), null if not captured
├── Width: int             // Frame width in pixels
├── Height: int            // Frame height in pixels
├── VariantName: string    // Which character variant this capture is for
├── RigName: string        // Which CameraRig was active when this frame was captured
├── AngleName: string      // Which CaptureAngle this was rendered from
├── AnimationName: string  // Which animation was playing
├── FrameIndex: int        // Frame number within the animation (0-based)
└── NormalizedTime: float  // Animation time when captured (0.0–1.0)
```

**Identity invariant**: the tuple (`VariantName`, `RigName`, `AngleName`, `AnimationName`, `FrameIndex`) is unique within a single capture session's `CapturedFrames` list. Identity fields are ordered by nesting depth — variant contains rig contains angle contains animation contains frame — and appear after the pixel dimensions for readability. This is the positional identity every downstream consumer (atlas assembly, sprite-sheet grouping, mirror generation) relies on when captures come from multi-variant, multi-rig sessions.

**DepthData normalization**: The bridge MUST normalize GPU depth values to the 0.0–1.0 range (0.0 = near plane, 1.0 = far plane) before creating a FrameCapture. Different graphics APIs store depth differently (0–1 vs -1–1, reversed vs non-reversed). Normalization is the bridge's responsibility; sprite-theory always sees linear 0.0–1.0.

### AssetReference

Engine-agnostic asset identifier used across the sprite-composer family. Identifies a model, equipment mesh, or material without binding the capture pipeline to a specific engine's asset-handle type.

```
AssetReference
├── BundleId: string       // Asset bundle identifier (or filesystem-source bundle for loose FBX)
├── AssetId: string        // Asset identifier within the bundle
└── VariantId: string?     // Optional variant (LOD level, palette swap). Null = default variant.
```

**Bridge resolution**: The bridge translates an `AssetReference` to an engine-specific handle at load time (Stride `Model`, Godot `PackedScene`, Unity `GameObject` prefab). For raw-FBX workflows, bridges supporting filesystem asset sources register loose files as single-asset bundles — the exact convention is the bridge's concern, not sprite-theory's.

**JSON round-trip**: Default record serialization via `System.Text.Json` — no custom converter. Properties serialize as `bundleId`, `assetId`, and `variantId` in camelCase JSON, with `variantId` omitted when null (per `JsonIgnoreCondition.WhenWritingNull`).

---

## Computation Pipeline

### Orthographic Camera Setup

Given a CaptureAngle and a model's BoundingBox, computes the exact orthographic camera parameters that frame the model completely with no clipping.

```
Input:  CaptureAngle (yaw, pitch) + BoundingBox (model bounds) + frame size
Output: OrthographicParameters (position, direction, up, ortho width/height, clip planes)

Steps:
1. Convert yaw/pitch to camera direction vector (spherical → Cartesian)
2. Position camera along -direction from bounding box center (distance = 2.5× half-diagonal)
3. Compute up vector (special case for near-vertical pitch > 89°)
4. Build orthonormal basis: right = cross(direction, up), correctedUp = cross(right, direction)
5. Project all 8 bounding box corners onto the camera's (right, correctedUp) plane
6. Compute ortho dimensions from projected min/max with 10% safety margin
7. Adjust for frame aspect ratio (expand width or height to match)
```

### Animation Frame Sampling

Generates a sequence of normalized timestamps for capturing animation frames.

```
Uniform sampling:
  Input:  animation duration, frame count
  Output: array of normalized times (0.0–1.0), each at the center of its time window

  Example: 8 frames → [0.0625, 0.1875, 0.3125, 0.4375, 0.5625, 0.6875, 0.8125, 0.9375]

Config-based sampling:
  Input:  AnimationInfo + AnimationConfig (with trim and speed)
  Output: array of normalized times within [TrimStart, TrimEnd], scaled by SpeedMultiplier
```

### Atlas Packing (MaxRects-BSSF)

Packs frame rectangles into one or more atlas images using the MaxRects algorithm with Best Short Side Fit heuristic.

```
Input:  list of (width, height, index) per frame + AtlasOptions
Output: AtlasLayout with per-frame placements and per-atlas dimensions

Steps:
1. Sort frames by height descending, then width descending, then index (stable)
2. Initialize free rectangle list with full atlas dimensions
3. For each frame: find the free rectangle with smallest short-side remainder (BSSF)
4. Place frame, subdivide remaining free space into up to 4 new rectangles
5. Prune fully-contained free rectangles
6. If no free rectangle fits: start a new atlas (multi-atlas overflow)
7. Compute actual dimensions (power-of-two if enabled) and packing efficiency
```

### Mirror Frame Generation

Two-phase process: extract mirror relationships from the rig, then generate SpriteFrame entries.

```
Phase 1 — ComputeMirrors:
  Input:  CameraRig
  Output: list of MirrorInfo (source angle name, target angle name, flip axis)
  
  Scan all angles where ProducesMirror = true.
  For each, create a MirrorInfo linking source to target.

Phase 2 — GenerateMirrorFrames:
  Input:  captured SpriteFrames + MirrorInfo list
  Output: mirror SpriteFrame entries (IsMirror=true, MirrorSourceIndex set)

  For each MirrorInfo, find all captured frames with matching source angle.
  Create a mirror SpriteFrame for each, with:
    - AngleName = mirror target name
    - Same Rect (no new atlas pixels — game engine flips at render time)
    - Flipped Pivot (horizontal: X becomes 1-X)
    - IsMirror = true, MirrorSourceIndex = source frame's index
```

### Pivot Computation

The `PivotComputer` static class provides two entry points:

- **`ProjectWorldPointToFrame(Vector3 worldPoint, OrthographicParameters camera)` — the general primitive.** Projects any world-space point onto the camera frame plane and returns a normalized pivot. Consumers call this directly when they have a specific anchor point (e.g., a skeleton bone position reported by the bridge for an `AnchorBoneName` override).
- **`ComputeFromBounds(BoundingBox bounds, OrthographicParameters camera)` — the feet-on-ground convenience.** A thin wrapper that computes the feet point `(Center.X, Min.Y, Center.Z)` from the bounds and delegates to `ProjectWorldPointToFrame`.

```
ProjectWorldPointToFrame:
  Input:  worldPoint: Vector3 + camera: OrthographicParameters (from OrthographicSetup.Compute)
  Output: Vector2 (normalized frame coords, (0,0) top-left, (1,1) bottom-right)

  Steps:
  1. Transform to camera space: rel = worldPoint - camera.Position
  2. Derive camera right axis: right = normalize(cross(camera.Direction, camera.Up))
     (If the cross product has near-zero squared length, the camera basis is degenerate — return DefaultHumanoidPivot)
  3. Project rel onto (right, up) plane:
       u = dot(rel, right)
       v = dot(rel, camera.Up)
  4. Guard against invalid ortho dimensions: if OrthoWidth ≤ 0 or OrthoHeight ≤ 0, return DefaultHumanoidPivot
  5. Normalize to frame coords:
       pivotX = 0.5 + u / orthoWidth
       pivotY = 0.5 - v / orthoHeight   // Y flipped because pivot origin is top-left
  6. Clamp both to [0, 1] to guarantee the pivot stays inside the frame
```

`ComputeFromBounds` adds a single step 0: compute `feet = Vector3(Center.X, Min.Y, Center.Z)` and pass it to `ProjectWorldPointToFrame`. Its behavior is unchanged from the pre-refactor implementation — all existing tests continue to pass against it.

**Why this matters for mixed-pitch rigs**: sprite-composer's capture loop MUST call the pivot-computation entry per angle, not once per rig. Angles at different pitches project feet to different frame-Y positions (side-view places feet near the frame bottom; top-down pulls them toward the frame center), and even angles at the same pitch but different yaws project to different frame-Y positions because the convex-hull-on-camera-plane of the 8 world-space bounding corners — and therefore `orthoHeight` — varies with yaw. Per-angle pivot computation is mandatory, not optional. This is enforced by the `PivotComputer` regression tests in `sprite-theory.tests/Camera/PivotComputerTests.cs`.

Fallback to `DefaultHumanoidPivot` (0.5, 0.85) when the camera basis is degenerate (direction parallel to up) or the ortho dimensions are zero.

### Depth-to-Normal Map Conversion

Converts per-frame depth buffer data to tangent-space normal map pixels using Sobel filtering.

```
Input:  depth float[] (0.0–1.0), frame dimensions, NormalMapOptions
Output: RGBA byte[] normal map (same dimensions)

Steps:
1. Optionally blur depth with Gaussian kernel (if BlurRadius > 0)
2. For each pixel, compute Sobel 3×3 gradients:
   - dz/dx = weighted horizontal difference (right column minus left column)
   - dz/dy = weighted vertical difference (bottom row minus top row)
3. Construct normal vector: (-dzdx × strength, -dzdy × strength, 1.0), normalized
4. Encode to RGB: map [-1,1] → [0,255] per component. A = 255 (opaque).
5. Boundary pixels use clamped sampling (repeat edge values)
```

### Atlas Assembly

Composites captured frame pixel data into atlas images at positions determined by AtlasPacker.

```
Input:  list of FrameCapture + AtlasLayout + background Color
Output: one RGBA byte[] per atlas image

Steps:
1. Allocate atlas pixel arrays filled with background color
2. For each placement in the layout, find the corresponding FrameCapture
3. Blit frame pixels into the atlas at (placement.X, placement.Y) via row-by-row memcpy
```

---

## Determinism Contract

All operations are deterministic. Same inputs → identical outputs.

| Operation | Deterministic | Mechanism |
|-----------|:---:|-----------|
| OrthographicSetup.Compute | Yes | Pure trigonometry from angle + bounds |
| AnimationSampling.GenerateUniform | Yes | Division arithmetic |
| AnimationSampling.GenerateFromConfig | Yes | Division arithmetic with trim/speed |
| AtlasPacker.Pack | Yes | Deterministic sort (height → width → index) + MaxRects-BSSF with stable tie-breaking |
| MirrorOptimizer.ComputeMirrors | Yes | Single iteration over angle list in order |
| MirrorOptimizer.GenerateMirrorFrames | Yes | Iteration over mirrors × source frames, both in deterministic order |
| PivotComputer.ProjectWorldPointToFrame | Yes | Pure linear algebra on world-point + camera basis |
| PivotComputer.ComputeFromBounds | Yes | Thin wrapper — computes feet point from bounds, delegates to ProjectWorldPointToFrame |
| MultiAtlasStrategy.OpenNextAtlas | Yes | Pure arithmetic + list reset (identical inputs produce identical outputs) |
| DepthToNormal.Generate | Yes | Per-pixel independent Sobel convolution |
| AtlasAssembler.Assemble | Yes | Pixel copy at deterministic atlas positions |
| SpriteSheetSerializer.Serialize | Yes | Ordered JSON properties via System.Text.Json |
| SpriteSheetSerializer.Deserialize | Yes | Standard JSON parsing |
| CaptureManifest.Compute | Yes | Arithmetic from rig angles × animation configs |

---

## Performance Targets

| Operation | Target | Context | Notes |
|-----------|--------|---------|-------|
| OrthographicSetup.Compute | < 1 μs | Both | Trigonometry + 8-corner projection |
| AnimationSampling.GenerateUniform | < 1 μs | Both | Division arithmetic |
| AnimationSampling.GenerateFromConfig | < 1 μs | Both | Division arithmetic with trim |
| AtlasPacker.Pack (1000 frames, 128×128) | < 10 ms | Both | MaxRects-BSSF, height-sorted input |
| MirrorOptimizer.ComputeMirrors (8-dir rig) | < 1 μs | Both | Single pass over 5 angles |
| MirrorOptimizer.GenerateMirrorFrames (3 mirrors × 160 frames) | < 1 ms | Both | 480 mirror frame entries |
| DepthToNormal.Generate (128×128 frame) | < 1 ms | Both | 16K pixels × 9 samples per pixel |
| AtlasAssembler.Assemble (960 frames → 4096² atlas) | < 100 ms | Both | Row-by-row memcpy per frame |
| SpriteSheetSerializer.Serialize (960 frames) | < 5 ms | Both | System.Text.Json |
| SpriteSheetSerializer.Deserialize (960 frames) | < 5 ms | Both | System.Text.Json |
| CaptureManifest.Compute | < 1 μs | Both | Arithmetic |

---

## Format Support

### Sprite Sheet Metadata (.json)

**Direction**: Both (read/write via SpriteSheetSerializer)

The canonical output format for sprite sheet metadata. Engine-agnostic JSON produced by `SpriteSheetSerializer.Serialize()` and consumed by any game runtime via `SpriteSheetSerializer.Deserialize()` or direct JSON parsing in any language.

**Key properties**: `version`, `generator`, `generatedAt`, `variant` (model + equipment), `rig` (camera config), `atlases` (image files), `animations` (per-angle frame maps), `frames` (real + mirror entries with atlas positions, pivots, durations).

See the planning document ([SPRITE-COMPOSER-SDK.md](../planning/SPRITE-COMPOSER-SDK.md) § JSON Metadata Schema) for a complete JSON example.

### Atlas Images (.png)

**Direction**: Write only (sprite-theory produces raw RGBA byte[] via AtlasAssembler; PNG encoding is the consumer's responsibility)

One or more atlas images containing all captured frames packed via MaxRects-BSSF. Normal map atlases (when `IncludeNormalMap = true`) share the same layout as color atlases, enabling same-UV sampling.

No custom binary format. No import capability.

---

## Known Quirks & Caveats

#### Bugs (Fix Immediately)

None known.

#### Intentional Quirks (Documented Behavior)

- **CameraRig.Angles contains ONLY rendered angles, never mirror targets.** Mirror targets like "NW" or "left" do not exist in the rig definition. They are created by MirrorOptimizer from the `ProducesMirror`/`MirrorTargetName` metadata on source angles. This means `rig.Angles.Count` equals the number of render passes per frame, which is exactly what the bridge needs to iterate.

- **ProducesMirror is additive, not exclusive.** An angle with `ProducesMirror = true` IS rendered AND ALSO generates a mirror. It does NOT mean "this is a mirror" or "skip rendering this." This is a ratified deviation from the original planning document, which proposed a `CanMirror` skip-flag with mirror angles enumerated in `rig.Angles`. The ratified design keeps only source angles in `rig.Angles` and derives mirror metadata from `ProducesMirror`/`MirrorTargetName`; see `docs/planning/SPRITE-COMPOSER-SDK.md` § Resolved Decisions.

- **Mirror frames share atlas Rect with their source.** A mirror SpriteFrame points to the same atlas rectangle as its source. No duplicate pixels exist in the atlas — the game engine applies horizontal flip at render time using the `IsMirror` flag. This means the atlas size reflects only captured frames, not total directions.

- **Default pivot (0.5, 0.85) is the terminal fallback, not the primary computation.** Consumers should auto-derive pivots via `PivotComputer.ComputeFromBounds`, which projects the bottom-center of the bounding box onto the frame plane. The default applies only when (a) the per-variant `CharacterVariant.PivotOverride` is null AND the consumer has not invoked `PivotComputer`, or (b) `PivotComputer` detects a degenerate camera basis and falls back. For flying enemies, tall bosses, quadrupeds, and other subjects whose bottom-of-bounds is not their "feet", set `CharacterVariant.PivotOverride` explicitly to bypass auto-computation.

- **PNG encoding is not part of sprite-theory.** `AtlasAssembler.Assemble` returns raw RGBA `byte[][]`; the sprite-theory SDK does not encode PNG. This is a ratified design decision (see `docs/planning/SPRITE-COMPOSER-SDK.md` § Resolved Decisions): every target engine bridges — Stride, Godot, Unity — ships native PNG encoding in its graphics package (`Stride.Graphics.Image.Save`, `Godot.Image.save_png`, `UnityEngine.ImageConversion.EncodeToPNG`). Each bridge uses its engine-native encoder, which avoids adding an external dependency to sprite-theory and avoids the Six Labors commercial-license trap that applies to `SixLabors.ImageSharp` for consumers with revenue above $1M USD. The encoder is supplied to the composer layer via `IAtlasEncoder` in sprite-composer.

- **Normal maps are tangent-space, not object-space.** The Sobel-filter output assumes the camera is looking straight at the sprite plane. This is correct for 2D sprites where the "surface" always faces the camera. Object-space normals would require 3D geometry knowledge that is lost after 2D capture.

- **FrameCapture.DepthData uses normalized 0.0–1.0 range.** The bridge must normalize GPU depth to this range (0.0 = near, 1.0 = far) before creating FrameCapture. Different graphics APIs store depth differently (0–1, -1–1, reversed). Normalization is the bridge's responsibility.

- **AtlasPacker handles multi-atlas overflow internally.** When frames exceed a single atlas's MaxWidth × MaxHeight, the packer starts a new atlas transparently. There is no separate MultiAtlasStrategy class — the overflow logic is part of AtlasPacker.Pack. The AtlasLayout result contains the atlas count and per-atlas dimensions.

#### Design Considerations (Requires Planning)

- **Transparent trimming and uniform frame sizing.** When `TrimTransparent = true`, each frame's content rect varies (some frames have more transparency). SpriteFrame stores both `Rect` (full allocated space in atlas) and `TrimmedRect` (actual content bounds). Game engines can use `Rect` for uniform sprite sizing or `TrimmedRect` for tighter collision bounds. The atlas packer uses trimmed sizes to pack more efficiently but allocates full `FrameSize` space to maintain uniform grid alignment when `TrimTransparent = false`.

- **Shadow capture (future).** A drop shadow improves sprite readability, especially at the 55° top-down angle. Options include a shadow pass with directional light + ground plane, a silhouette with reduced opacity, or game-engine-side shadows. This is deferred until the base capture pipeline is working. When implemented, it would add an `IncludeShadow` boolean to CameraRig and produce a separate shadow atlas (like normal maps).

---

## Open Questions

1. **Animation event schema**: `AnimationEvent` markers (hit frames, sound cues) are optional in SpriteAnimation. The format is currently a simple `(FrameIndex: int, EventType: string, EventData: string)` triple. The exact schema depends on Defenders' runtime needs. This is sufficient for initial implementation; refinement can happen during game integration.

2. **GroupByAnimation atlas layout**: When `AtlasOptions.GroupByAnimation = true`, the packer should keep frames from the same animation in visual rows for human readability when inspecting atlas images. The current MaxRects algorithm doesn't enforce this — it optimizes for packing efficiency. A future enhancement could add row-constrained packing as an option, at the cost of slightly lower packing efficiency.

---

## Work Tracking

### Completed

- **2026-04-15**: Full implementation of sprite-theory SDK from implementation map.
  - 36 source files (1,897 lines) across 7 subsystems: Camera, Animation, Atlas, Mirror, Metadata, NormalMap, Export
  - 25 public types: 21 records, 4 structs, 3 enums, 2 interfaces
  - 14 public methods: all methods from implementation map implemented
  - 77 unit tests (1,477 lines) across 9 test files — all passing
  - Zero external NuGet dependencies (pure .NET BCL)
  - Targets net8.0 + net9.0, builds with 0 warnings (TreatWarningsAsErrors enabled)
  - Key algorithms: MaxRects-BSSF bin-packing (AtlasPacker), Sobel 3×3 normal map generation (DepthToNormal), 7-step orthographic camera setup (OrthographicSetup)
  - Defenders verification test confirms: TopDown8Dir + SideViewBrawler = 960 captured + 640 mirror = 1,600 total frames per variant
  - `sdks/bannou-sdks.sln` updated to reference `sprite-theory` and `sprite-theory.tests`.

- **2026-04-15**: Post-implementation audit remediation (Findings A, B, C, D, E, F from initial agent audit).
  - **MultiAtlasStrategy** extracted from `AtlasPacker` into its own static class (`Atlas/MultiAtlasStrategy.cs`). Matches the planning doc's project structure and Bannou's "one static class per concept" pattern. Pre-validates frame-fits-in-atlas-max before resetting free rects.
  - **PivotComputer** added (`Camera/PivotComputer.cs`) with `ComputeFromBounds(BoundingBox, OrthographicParameters) → Vector2`. Projects bottom-center of bounds onto the camera frame plane, clamped to `[0, 1]`. Exposes `DefaultHumanoidPivot` as the terminal fallback. 10 new tests in `PivotComputerTests.cs`.
  - **CharacterVariant.PivotOverride: Vector2?** field added per the planning doc's original recommendation (auto-compute with per-variant override).
  - `Dictionary<,>` fields on records changed to `IReadOnlyDictionary<,>` (`CharacterVariant.MaterialOverrides`, `SpriteSheet.CustomProperties`, `SpriteAnimation.AngleFrameMap`) to honor the documented immutability contract. STJ round-trips the interface natively in .NET 6+.
  - Ratified the `ProducesMirror` semantic redesign (vs the plan's `CanMirror` skip-flag) in the Intentional Quirks section and cross-referenced the planning doc's Resolved Decisions.
  - Ratified PNG-encoding-by-consumer (the `byte[][]` return from `AtlasAssembler`) in the Intentional Quirks section with the engine-native rationale and the Six Labors licensing rationale. The entry moved out of "Design Considerations (Requires Planning)" because it is now planned, not pending.
  - Total tests: 87 passing. Public types: 40 (+2 new static classes, +1 new `PivotOverride` field on existing record). Public methods: 16 (+2).

- **2026-04-19**: Sprite-composer remediation — schema changes landing in sprite-theory (findings F2, F3, R1, F11, I1/I8/I9 from the sprite-composer critical review).
  - **F2 — AtlasAssembler indexing**: `AtlasAssembler.Assemble` now indexes captures by `frames[placement.FrameIndex]` directly (list position), matching the implementation map's specification and throwing `ArgumentOutOfRangeException` on invalid layout input. The previous `Dictionary<int, FrameCapture>` keyed by `FrameCapture.FrameIndex` (an intra-animation index) collided the moment any two captures shared an intra-animation index — i.e., every realistic multi-animation session. Four new `AtlasAssemblerTests` cover multi-animation scenarios at the Defenders scale (3 animations × 3 angles × 2 frames = 18 captures).
  - **F3 — FrameCapture identity**: `FrameCapture` gains required `VariantName: string` and `RigName: string` fields positioned after dimensions to reflect identity nesting depth. The tuple (`VariantName`, `RigName`, `AngleName`, `AnimationName`, `FrameIndex`) is the unique identity of a capture within a session. The bridge's `CaptureFrameAsync` will take both fields in its parameter list (sprite-composer Phase 2).
  - **R1 — CharacterVariant anchor**: added optional `AnchorBoneName: string?`. Combined with the new `PivotComputer.ProjectWorldPointToFrame` primitive and the bridge's `TryGetBonePosition` (sprite-composer Phase 2), variants can anchor their pivot to a named skeleton bone — resolving the fragile bounds-min projection for characters with flowing cloth, asymmetric gear, or non-standard proportions. The four-step pivot resolution order is documented on `CharacterVariant` and enforced by the sprite-composer capture loop.
  - **F11 — PivotComputer refactor**: extracted `ProjectWorldPointToFrame(Vector3, OrthographicParameters) → Vector2` as the general primitive. `ComputeFromBounds` is now a thin wrapper that computes the feet point and delegates. Three new divergence tests prove per-angle pivot computation is required even for single-pitch presets (different yaws produce different `orthoHeight` values, which produce different `pivotY` values).
  - **I1, I8, I9 — AssetReference**: new `AssetReference(BundleId, AssetId, VariantId?)` record replaces raw `string` paths on `CharacterVariant.Model`, `CharacterVariant.MaterialOverrides` values, and `EquipmentSlot.Mesh`. Bridges resolve the reference to engine-specific handles at load time; for raw-FBX workflows, filesystem asset sources register loose files as single-asset bundles.
  - **Vector3 primitive**: introduced `BeyondImmersion.Bannou.Core.Math.Vector3` (float, 12-byte struct) as the shared 3D vector primitive for sprite-theory, the sprite-composer bridge contract, and any future SDK that needs graphics-precision 3D math. `BoundingBox.Min`/`Max`/`Center`/`Extents`/`Size`, `OrthographicParameters.Position`/`Direction`/`Up`, and `PivotComputer.ProjectWorldPointToFrame` all use it. The scene-composer SDK retains its own double-precision `SceneComposer.Math.Vector3` for large-world accuracy — the two types have different namespaces and do not conflict. 30 new Vector3 unit tests in `sdks/core.tests/Math/Vector3Tests.cs`.
  - Total tests: 100 passing (sprite-theory) + 30 passing (core Vector3) = 130 new-or-validated. Public types: 41 (+1 `AssetReference`). Public methods: 17 (+1 `ProjectWorldPointToFrame`).

### Pending

- Performance profiling against documented targets (< 10ms for 1000-frame atlas pack, < 1ms for normal map generation)
- `GroupByAnimation` atlas layout hint (MaxRects currently optimizes for efficiency, not visual row grouping)
- Consumer SDKs: sprite-composer (engine-agnostic orchestrator), sprite-composer-stride (Stride engine bridge)
