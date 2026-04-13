# Sprite Theory SDK Deep Dive

> **SDK**: sprite-theory (not yet created)
> **Location**: `sdks/sprite-theory/` (planned)
> **Layer**: Theory
> **Domain**: Sprite
> **Dependencies**: None (pure .NET, zero external dependencies)
> **Status**: Aspirational — no code exists.
> **Implementation Map**: [docs/sdks/maps/SPRITE-THEORY.md](maps/SPRITE-THEORY.md)
> **Planning Document**: [docs/planning/SPRITE-COMPOSER-SDK.md](../planning/SPRITE-COMPOSER-SDK.md)
> **Consumers**: sprite-composer (capture orchestration), sprite-composer-stride (Stride bridge), future SpriteBatcher (batch automation)
> **Short**: Camera mathematics, atlas packing, animation frame sampling, mirror optimization, normal map generation, and the sprite sheet JSON metadata format — the foundation for all sprite SDKs

---

## Overview

sprite-theory is the theory-layer SDK for the sprite domain. It provides pure-computation primitives for camera rig definitions, orthographic projection setup, animation frame sampling, atlas bin-packing (MaxRects), mirror optimization, depth-to-normal-map conversion, and the canonical sprite sheet metadata format (JSON + PNG).

It follows the same pattern as music-theory, storyline-theory, and voxel-core: pure computation, zero service dependencies, deterministic, usable on both client and server. Any code that needs to define camera rigs, compute atlas layouts, generate mirror metadata, convert depth buffers to normal maps, or serialize/deserialize sprite sheet metadata depends on this SDK.

**The theory-layer role**: sprite-theory does NOT render anything — it has no engine dependency. It computes WHERE frames go in an atlas, WHICH angles can be mirrored, HOW depth data converts to normals, and WHAT the metadata format looks like. The actual rendering, model loading, and frame capture happen in the bridge (sprite-composer-stride). The theory layer produces the mathematics and data structures that the bridge and orchestrator consume.

---

## Consumers

| Consumer | Type | Usage |
|----------|------|-------|
| sprite-composer | SDK | Uses CameraRig, AtlasPacker, MirrorOptimizer, SpriteSheetSerializer, DepthToNormal, and all metadata types for capture orchestration and export |
| sprite-composer-stride | Bridge | Uses OrthographicSetup to configure Stride cameras, CaptureAngle for positioning, FrameCapture as the pixel data container |
| Future SpriteBatcher | Tool | Uses CameraRigPresets for batch definitions, AtlasPacker for layout, SpriteSheetSerializer for output |
| Game runtime | Consumer | Reads SpriteSheet JSON metadata to drive sprite animation, mirror flips, and normal map sampling |
| CI/CD pipeline | Tool | Uses SpriteSheetSerializer for validation, CaptureManifest for expected output verification |

---

## Public API Surface

### Camera Subsystem

| Type | Kind | Purpose |
|------|------|---------|
| `CameraRig` | Record | Rig definition: projection type, angle list, frame size, padding, options |
| `CaptureAngle` | Record | Single angle: name, yaw, pitch, canMirror, mirrorTarget, mirrorAxis |
| `CameraRigPresets` | Static class | Built-in rig factories: `SideViewBrawler()`, `TopDown8Dir()`, `TopDown4Dir()`, `Custom()` |
| `OrthographicSetup` | Static class | Compute orthographic camera parameters to fit a bounding box at a given angle |
| `ProjectionType` | Enum | Orthographic, Perspective |
| `BoundingBox` | Struct | Axis-aligned 3D bounding box (min/max Vector3) for model bounds |

### Animation Subsystem

| Type | Kind | Purpose |
|------|------|---------|
| `AnimationSampling` | Static class | Generate frame sequences from animation duration and config |
| `FrameSequence` | Record | Ordered list of frame timestamps (normalized 0.0–1.0) for one animation |
| `AnimationConfig` | Record | Per-animation capture settings: frame count, speed multiplier, trim start/end |
| `AnimationInfo` | Record | Animation metadata from bridge: name, duration, frame count, is looping |
| `LoopMode` | Enum | None, Loop, PingPong |

### Atlas Subsystem

| Type | Kind | Purpose |
|------|------|---------|
| `AtlasPacker` | Static class | MaxRects bin-packing: frames → atlas layout |
| `AtlasLayout` | Record | Result: frame positions, atlas dimensions, packing efficiency ratio |
| `AtlasOptions` | Record | Configuration: max atlas size, padding, power-of-two, group-by-animation |
| `MultiAtlasStrategy` | Static class | Overflow handling: split frames across multiple atlas images |
| `PackedFrame` | Record | Single frame's position in the atlas: rect, atlas index |

### Mirror Subsystem

| Type | Kind | Purpose |
|------|------|---------|
| `MirrorOptimizer` | Static class | Analyze CameraRig, compute which angles mirror, generate metadata |
| `MirrorInfo` | Record | Per-frame mirror data: source angle, target angle, flip axis |
| `MirrorAxis` | Enum | Horizontal, Vertical |

### Metadata Subsystem

| Type | Kind | Purpose |
|------|------|---------|
| `SpriteSheet` | Record | Complete output: variant, rig, atlas layout, frames, animations, mirrors |
| `SpriteFrame` | Record | Per-frame: rect in atlas, pivot, duration, angle, animation, mirror info |
| `SpriteAnimation` | Record | Per-animation: name, loop mode, duration, frame indices per angle |
| `CharacterVariant` | Record | Input definition: model path, equipment list, material overrides, scale |
| `EquipmentSlot` | Record | Equipment slot: slot name, mesh path, bone name |
| `CaptureManifest` | Record | Full capture job: variant + rigs + animations → expected frame count and atlas sizes |
| `AnimationEvent` | Record | Optional per-frame event marker: hit frame, sound cue, effect trigger |

### Normal Map Subsystem

| Type | Kind | Purpose |
|------|------|---------|
| `DepthToNormal` | Static class | Sobel-filter conversion of depth buffer to tangent-space normal map |
| `NormalMapOptions` | Record | Configuration: strength multiplier, blur radius, output format |

### Export Subsystem

| Type | Kind | Purpose |
|------|------|---------|
| `SpriteSheetSerializer` | Static class | SpriteSheet ↔ JSON serialization (the canonical metadata format) |
| `IPixelSource` | Interface | Engine-agnostic abstraction: raw RGBA pixel data + dimensions |
| `IDepthSource` | Interface | Engine-agnostic abstraction: depth buffer float data + dimensions |
| `AtlasAssembler` | Static class | Compose captured frames into atlas image from pixel sources |

### Shared Types

| Type | Kind | Purpose |
|------|------|---------|
| `Color` | Struct (4 bytes) | RGBA color, byte-valued (0–255 per channel) |
| `Vector2` | Struct | 2D float vector (pivot points, UV coordinates) |
| `Rectangle` | Struct | Integer rectangle (x, y, width, height) for atlas frame positions |
| `FrameCapture` | Record | Single captured frame: RGBA pixels, depth data (optional), dimensions |

---

## Data Model

### CameraRig

The primary configuration type. Defines how a character is captured from one orientation.

```
CameraRig
├── Name: string                        // "SideView-Brawler", "TopDown-55deg", custom
├── Projection: ProjectionType          // Orthographic (standard) or Perspective (rare)
├── Angles: IReadOnlyList<CaptureAngle> // All angles including mirrors
├── FrameSize: (int Width, int Height)  // Per-frame pixel dimensions (e.g., 128×128)
├── Padding: int                        // Pixels between frames in atlas (default: 2)
├── BackgroundColor: Color              // Render clear color (default: transparent RGBA 0,0,0,0)
├── IncludeNormalMap: bool              // Also generate normal map atlas from depth buffer
├── IncludeShadow: bool                 // Include shadow pass (future — not implemented)
└── TrimTransparent: bool              // Trim transparent borders per frame (default: false)
```

**Immutability**: CameraRig is a record — value equality and immutability by default. Changes produce new instances. This supports undo/redo in the composer (command stores before/after rig snapshots).

### CaptureAngle

Individual angle within a rig. Angles that have `CanMirror = true` are NOT captured — their frames are generated as horizontal flips of their source angle.

```
CaptureAngle
├── Name: string               // "right", "N", "NE", "SE", etc.
├── Yaw: float                 // Degrees rotation around Y axis (0 = north/forward)
├── Pitch: float               // Degrees from horizontal (0 = level, negative = looking down)
├── CanMirror: bool            // If true, this angle is NOT captured — it's a mirror
├── MirrorTargetName: string?  // Name of the generated mirror angle (e.g., "NE" → "NW")
└── MirrorAxis: MirrorAxis     // Horizontal (flip X) or Vertical (flip Y)
```

**Mirror semantics**: When `CanMirror = true`, the CaptureAngle represents a CAPTURED angle whose output is used as the source for a mirror. The `MirrorTargetName` is the name of the mirror that will be generated FROM this capture. For example, if angle "NE" (Yaw=45°) has `CanMirror=true` and `MirrorTargetName="NW"`, then:
- "NE" IS captured (5 render passes for 8-dir)
- "NW" is NOT captured — its frames are metadata entries pointing to "NE" frames with horizontal flip

### SpriteSheet (Output Metadata)

The complete output of a capture session. Serializes to the canonical JSON format.

```
SpriteSheet
├── Version: string                            // Schema version ("1.0")
├── Generator: string                          // "BeyondImmersion.Bannou.SpriteComposer"
├── GeneratedAt: DateTimeOffset                // Capture timestamp
├── Variant: CharacterVariant                  // What was captured
├── Rig: CameraRig                             // Camera rig used
├── Atlases: IReadOnlyList<AtlasInfo>          // Atlas image filenames and dimensions
├── Animations: IReadOnlyList<SpriteAnimation> // All captured animations
├── Frames: IReadOnlyList<SpriteFrame>         // All frames (real + mirror)
└── CustomProperties: Dictionary<string, string> // Game-specific metadata
```

**AtlasInfo**: When frames overflow a single atlas (MaxSize exceeded), multiple atlas images are generated. Each `AtlasInfo` has an `Index`, `Filename`, `Width`, and `Height`. Each `SpriteFrame` references its atlas by index.

### SpriteFrame

Per-frame metadata. Frames with `IsMirror = true` do not have atlas pixels — the game engine flips the source frame at render time.

```
SpriteFrame
├── Index: int                  // Global frame index (0-based)
├── AtlasIndex: int             // Which atlas image this frame is in (0 for single-atlas)
├── AngleName: string           // Capture angle name ("right", "NE", etc.)
├── AnimationName: string       // Animation name ("idle", "attack_light", etc.)
├── FrameInAnimation: int       // Frame number within the animation (0-based)
├── Rect: Rectangle             // Position and size in atlas (x, y, width, height)
├── TrimmedRect: Rectangle?     // Actual content rect if TrimTransparent was enabled
├── Pivot: Vector2              // Pivot point (0,0 = top-left; 0.5,0.85 = center-bottom default)
├── Duration: float             // Frame display duration in seconds
├── IsMirror: bool              // True = horizontal flip of another frame (no atlas pixels)
└── MirrorSourceIndex: int?     // Index of source frame (only when IsMirror = true)
```

### SpriteAnimation

Groups frames by animation name, with per-angle frame index lookup.

```
SpriteAnimation
├── Name: string                                 // Animation name (matches source clip)
├── LoopMode: LoopMode                           // None, Loop, PingPong
├── TotalDuration: float                         // Total animation duration in seconds
├── AngleFrameMap: Dictionary<string, int[]>     // AngleName → frame indices in order
└── Events: IReadOnlyList<AnimationEvent>?       // Optional hit frames, sound cues
```

**AngleFrameMap**: The primary lookup structure for game runtime. To play "idle" facing "NE", read `animation.AngleFrameMap["NE"]` → array of frame indices → look up each `SpriteFrame` by index for rect, duration, mirror info.

### CharacterVariant

Input definition describing what to capture. Serializable as part of the SpriteSheet metadata and as part of SpriteProject files.

```
CharacterVariant
├── Name: string                                    // "warrior_plate_sword"
├── ModelPath: string                               // Path to base character model
├── Equipment: IReadOnlyList<EquipmentSlot>         // Attached equipment pieces
├── MaterialOverrides: Dictionary<string, string>?  // Material/palette swaps
└── Scale: float                                    // Model scale factor (default: 1.0)
```

### FrameCapture

Engine-agnostic container for captured pixel data. Created by the bridge, consumed by sprite-theory's assembler and normal map generator.

```
FrameCapture
├── PixelData: byte[]      // RGBA pixels, 4 bytes per pixel, row-major
├── DepthData: float[]?    // Depth buffer values (0.0–1.0), null if not captured
├── Width: int             // Frame width in pixels
├── Height: int            // Frame height in pixels
├── AngleName: string      // Which angle this was captured from
├── AnimationName: string  // Which animation was playing
├── FrameIndex: int        // Frame number within the animation
└── NormalizedTime: float  // Animation time (0.0–1.0) when captured
```

---

## Computation Pipeline

### Orthographic Camera Setup

```
OrthographicSetup.Compute(angle: CaptureAngle, bounds: BoundingBox, frameSize: (int, int))
  → OrthographicParameters

  // 1. Compute camera position from angle
  COMPUTE yawRad ← angle.Yaw * PI / 180
  COMPUTE pitchRad ← angle.Pitch * PI / 180
  COMPUTE direction ← (
    sin(yawRad) * cos(pitchRad),
    sin(pitchRad),
    cos(yawRad) * cos(pitchRad)
  )
  COMPUTE center ← bounds.Center
  COMPUTE radius ← bounds.Extents.Length    // half-diagonal of bounding box
  COMPUTE distance ← radius * 2.0          // ensure model is fully visible
  COMPUTE position ← center - direction * distance

  // 2. Compute orthographic projection size to fit the model
  // Project bounding box corners onto the camera's view plane
  COMPUTE up ← (0, 1, 0) if |pitch| < 89° else (0, 0, -sign(pitch))
  COMPUTE right ← normalize(cross(direction, up))
  COMPUTE correctedUp ← cross(right, direction)

  COMPUTE minU, maxU, minV, maxV ← project all 8 bbox corners onto (right, correctedUp) plane
  COMPUTE orthoWidth ← (maxU - minU) * 1.1   // 10% padding for safety
  COMPUTE orthoHeight ← (maxV - minV) * 1.1

  // 3. Adjust for frame aspect ratio
  COMPUTE frameAspect ← frameSize.Width / frameSize.Height
  COMPUTE orthoAspect ← orthoWidth / orthoHeight
  IF frameAspect > orthoAspect
    orthoWidth ← orthoHeight * frameAspect
  ELSE
    orthoHeight ← orthoWidth / frameAspect

  RETURN OrthographicParameters(position, direction, correctedUp, orthoWidth, orthoHeight, near: 0.01, far: distance * 3)
```

### Animation Frame Sampling

```
AnimationSampling.GenerateUniform(duration: float, frameCount: int) → FrameSequence

  // Uniform spacing: frames evenly distributed across animation duration
  CREATE timestamps ← new float[frameCount]
  COMPUTE interval ← 1.0 / frameCount    // normalized interval
  FOREACH i FROM 0 TO frameCount - 1
    timestamps[i] ← i * interval + interval * 0.5   // center of each frame's time window
  RETURN FrameSequence(timestamps, duration)
```

### Atlas Packing (MaxRects)

```
AtlasPacker.Pack(frames: IReadOnlyList<(int width, int height)>, options: AtlasOptions)
  → AtlasLayout

  // Sort frames by height descending (best heuristic for MaxRects)
  COMPUTE sorted ← frames.OrderByDescending(f => f.height).ThenByDescending(f => f.width)

  // Initialize MaxRects with atlas dimensions
  CREATE freeRects ← [Rectangle(0, 0, options.MaxWidth, options.MaxHeight)]
  CREATE placements ← new List<PackedFrame>()
  SET currentAtlas ← 0

  FOREACH frame in sorted
    COMPUTE paddedWidth ← frame.width + options.Padding
    COMPUTE paddedHeight ← frame.height + options.Padding

    // Find best position using Best Short Side Fit heuristic
    COMPUTE bestRect ← null, bestShortSide ← MAX_INT, bestLongSide ← MAX_INT
    FOREACH rect in freeRects
      IF paddedWidth <= rect.Width AND paddedHeight <= rect.Height
        COMPUTE shortSide ← min(rect.Width - paddedWidth, rect.Height - paddedHeight)
        COMPUTE longSide ← max(rect.Width - paddedWidth, rect.Height - paddedHeight)
        IF shortSide < bestShortSide OR (shortSide == bestShortSide AND longSide < bestLongSide)
          bestRect ← rect; bestShortSide ← shortSide; bestLongSide ← longSide

    IF bestRect is null
      // Frame doesn't fit — start new atlas (MultiAtlasStrategy)
      currentAtlas += 1
      freeRects ← [Rectangle(0, 0, options.MaxWidth, options.MaxHeight)]
      // Retry placement in new atlas
      ...continue

    // Place frame at bestRect position
    placements.Add(PackedFrame(frame.index, currentAtlas, bestRect.X, bestRect.Y, frame.width, frame.height))

    // Split remaining free space (MaxRects subdivision)
    COMPUTE placed ← Rectangle(bestRect.X, bestRect.Y, paddedWidth, paddedHeight)
    SplitFreeRects(freeRects, placed)
    PruneFreeRects(freeRects)  // Remove rects fully contained by others

  // Compute actual atlas dimensions (smallest power-of-two that fits all placements)
  COMPUTE actualWidth ← options.PowerOfTwo ? nextPowerOfTwo(maxX) : maxX
  COMPUTE actualHeight ← options.PowerOfTwo ? nextPowerOfTwo(maxY) : maxY
  COMPUTE efficiency ← totalFrameArea / (actualWidth * actualHeight)

  RETURN AtlasLayout(placements, actualWidth, actualHeight, efficiency, currentAtlas + 1)
```

### Mirror Computation

```
MirrorOptimizer.GenerateMirrors(rig: CameraRig, capturedFrameCount: int)
  → IReadOnlyList<MirrorInfo>

  CREATE mirrors ← new List<MirrorInfo>()

  // Find all angles that have CanMirror = true
  FOREACH angle in rig.Angles WHERE angle.CanMirror
    // Find the corresponding mirror target angle
    COMPUTE targetName ← angle.MirrorTargetName
    // For each captured frame of this source angle, generate a mirror frame entry
    // (actual frame indices are assigned by the caller — we just declare the relationship)
    mirrors.Add(MirrorInfo(
      sourceAngleName: angle.Name,
      targetAngleName: targetName,
      flipAxis: angle.MirrorAxis
    ))

  RETURN mirrors
```

### Depth-to-Normal Conversion

```
DepthToNormal.Generate(depth: float[], width: int, height: int, options: NormalMapOptions)
  → byte[]   // RGBA normal map pixels

  CREATE normals ← new byte[width * height * 4]

  FOREACH y FROM 0 TO height - 1
    FOREACH x FROM 0 TO width - 1
      // Sobel 3×3 gradient estimation
      COMPUTE dzdx ← (
        sample(x+1, y-1) + 2*sample(x+1, y) + sample(x+1, y+1)
        - sample(x-1, y-1) - 2*sample(x-1, y) - sample(x-1, y+1)
      ) * options.Strength

      COMPUTE dzdy ← (
        sample(x-1, y+1) + 2*sample(x, y+1) + sample(x+1, y+1)
        - sample(x-1, y-1) - 2*sample(x, y-1) - sample(x+1, y-1)
      ) * options.Strength

      // Construct normal vector (tangent space: Z points outward)
      COMPUTE normal ← normalize(-dzdx, -dzdy, 1.0)

      // Encode to RGB: map [-1,1] → [0,255]
      COMPUTE i ← (y * width + x) * 4
      normals[i + 0] ← (byte)((normal.X * 0.5 + 0.5) * 255)   // R = X
      normals[i + 1] ← (byte)((normal.Y * 0.5 + 0.5) * 255)   // G = Y
      normals[i + 2] ← (byte)((normal.Z * 0.5 + 0.5) * 255)   // B = Z
      normals[i + 3] ← 255                                       // A = opaque

  RETURN normals

  // Helper: sample depth with clamped boundary
  FUNCTION sample(x, y) → float
    COMPUTE cx ← clamp(x, 0, width - 1)
    COMPUTE cy ← clamp(y, 0, height - 1)
    RETURN depth[cy * width + cx]
```

### Atlas Assembly

```
AtlasAssembler.Assemble(
  frames: IReadOnlyList<FrameCapture>,
  layout: AtlasLayout,
  backgroundColor: Color
) → byte[][]   // One RGBA pixel array per atlas

  CREATE atlases ← new byte[layout.AtlasCount][]
  FOREACH i FROM 0 TO layout.AtlasCount - 1
    atlases[i] ← new byte[layout.Width * layout.Height * 4]
    // Fill with background color
    FOREACH pixel in atlases[i] (step 4)
      SET backgroundColor RGBA

  // Blit each captured frame into its atlas position
  FOREACH placement in layout.Placements
    COMPUTE frame ← frames[placement.FrameIndex]
    COMPUTE atlas ← atlases[placement.AtlasIndex]
    // Copy rows from frame pixel data to atlas at (placement.X, placement.Y)
    FOREACH row FROM 0 TO frame.Height - 1
      COMPUTE srcOffset ← row * frame.Width * 4
      COMPUTE dstOffset ← ((placement.Y + row) * layout.Width + placement.X) * 4
      Array.Copy(frame.PixelData, srcOffset, atlas, dstOffset, frame.Width * 4)

  RETURN atlases
```

---

## Determinism Contract

All operations are deterministic. Same inputs produce identical outputs:

| Operation | Deterministic? | Notes |
|-----------|---------------|-------|
| OrthographicSetup.Compute | Yes | Pure trigonometry from angle + bounds |
| AnimationSampling.GenerateUniform | Yes | Division arithmetic |
| AtlasPacker.Pack | Yes | Deterministic sort + MaxRects with stable tie-breaking |
| MirrorOptimizer.GenerateMirrors | Yes | Single pass over angle list |
| DepthToNormal.Generate | Yes | Sobel convolution, per-pixel independent |
| AtlasAssembler.Assemble | Yes | Pixel copy at deterministic positions |
| SpriteSheetSerializer.Serialize | Yes | Ordered JSON properties via System.Text.Json |
| SpriteSheetSerializer.Deserialize | Yes | Standard JSON parsing |
| CaptureManifest.Compute | Yes | Arithmetic from rig + animation config |

---

## Performance Targets

| Operation | Target | Notes |
|-----------|--------|-------|
| OrthographicSetup.Compute | < 1 μs | Trigonometry + bounding box projection |
| AnimationSampling.GenerateUniform | < 1 μs | Division arithmetic |
| AtlasPacker.Pack (1000 frames, 128×128) | < 10 ms | MaxRects with height-sorted input |
| MirrorOptimizer.GenerateMirrors (8-dir rig) | < 1 μs | Single pass over ~8 angles |
| DepthToNormal.Generate (128×128 frame) | < 1 ms | 16K pixels × 9 samples per pixel |
| AtlasAssembler.Assemble (1000 frames → 4096×4096) | < 100 ms | Pixel blit (memcpy per row) |
| SpriteSheetSerializer.Serialize (1000 frames) | < 5 ms | System.Text.Json |
| SpriteSheetSerializer.Deserialize (1000 frames) | < 5 ms | System.Text.Json |
| CaptureManifest.Compute | < 1 μs | Arithmetic |

---

## Known Quirks & Caveats

#### Intentional Quirks (Documented Behavior)

- **Mirror angles are NOT captured — they are generated metadata.** When a CaptureAngle has `CanMirror = true`, sprite-composer skips rendering for the mirror target angle entirely. The mirror frames exist only as SpriteFrame entries with `IsMirror = true` and `MirrorSourceIndex` pointing to the actual captured frame. The game engine must flip horizontally at render time. This halves the capture work for side-view (50% savings) and reduces it by 37.5% for 8-directional top-down.

- **CanMirror on the SOURCE angle, not the target.** The CaptureAngle named "NE" is what gets RENDERED. Its `MirrorTargetName = "NW"` means "when generating the mirror set, create a 'NW' entry that flips 'NE'." This is counterintuitive (the mirror metadata lives on the source, not the target) but correct — the source is the one that needs to declare what it produces.

- **Atlas packing uses height-descending sort as the primary heuristic.** This is the standard MaxRects heuristic validated across decades of bin-packing literature. Frame ordering within the same height group is by frame index (stable). Alternative sort orders (area-descending, width-descending) are not exposed as options — the height-descending heuristic produces optimal or near-optimal results for the uniform-sized frames typical of sprite capture.

- **Normal maps are tangent-space, not object-space.** The Sobel-filter output assumes the camera is looking straight at the surface. This is correct for 2D sprites where the "surface" is always the sprite plane facing the camera. Object-space normals would require knowledge of the 3D geometry's actual orientation, which is lost once captured as a 2D image.

- **FrameCapture.DepthData uses 0.0–1.0 range (near to far).** The bridge must normalize the GPU depth buffer to this range before creating a FrameCapture. Different graphics APIs store depth differently (0–1 vs -1–1, reversed vs non-reversed). Normalization is the bridge's responsibility; sprite-theory always sees 0.0 = near, 1.0 = far.

- **Default pivot is (0.5, 0.85) — center-bottom biased.** This works for humanoid characters standing on the ground. The feet anchor point is at ~85% of the frame height from top. Non-humanoid characters (flying enemies, large bosses, quadrupeds) need custom pivots configured per CharacterVariant or per CameraRig.

#### Design Considerations (Requires Planning)

- **PNG encoding dependency**: AtlasAssembler produces raw RGBA pixel arrays. Converting to PNG requires either a library (SixLabors.ImageSharp, SkiaSharp) or a minimal custom PNG writer. The choice affects the "zero external dependencies" claim. Options: (a) add ImageSharp as the sole dependency (Apache 2.0, pure .NET, industry standard), (b) write a minimal PNG encoder (deflate + chunk headers — ~200 lines), (c) defer PNG encoding to the consumer (sprite-composer handles it). **Recommendation**: Option (c) — sprite-theory produces raw pixel arrays, the consumer writes PNG. This keeps the theory layer dependency-free.

- **Multi-atlas sprite sheets**: When frames overflow a single 4096×4096 atlas, MultiAtlasStrategy splits across multiple images. The JSON metadata supports this (each SpriteFrame has `AtlasIndex`), but game-side consumers must handle texture switching during animation playback. This is documented as a consumer concern, not an SDK concern.

- **Transparent trimming precision**: When `TrimTransparent = true`, frame content rects vary per frame (some frames have more transparency than others). The `TrimmedRect` on SpriteFrame captures the actual content bounds within the standard `Rect`. Game engines can use the full Rect for uniform sprite sizing or TrimmedRect for tighter collision bounds.

---

## Open Questions

1. **PNG encoding location**: Should sprite-theory include a PNG encoder, or should raw pixel arrays be the output? See Design Considerations above. Current recommendation: raw pixel arrays (dependency-free theory layer).

2. **Animation event format**: `AnimationEvent` markers (hit frames, sound cues) are optional in SpriteAnimation. The exact schema for these events depends on what the Defenders runtime needs. Current design: simple `(FrameIndex, EventType, EventData)` triple. May need refinement based on game integration.

3. **Pivot auto-detection**: Should OrthographicSetup compute a pivot point from the character's bounding box (feet position at the bottom center of the projected bounds)? This would provide reasonable defaults without manual configuration. **Recommendation**: Yes, compute auto-pivot as default with per-variant override.

---

## Work Tracking

No work items yet — SDK is pre-implementation.
