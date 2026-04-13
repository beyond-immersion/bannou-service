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
| Public Types | ~24 (6 records, 4 static classes, 3 enums, 3 structs, 2 interfaces, 6 supporting records) |
| Public Methods | ~15 |
| Dependencies | None (pure .NET BCL) |
| Deterministic | Yes (pure) |
| Allocation-Free Hot Paths | CaptureAngle construction, BoundingBox operations, Color operations |

---

## Data Structures

### CameraRig

**Kind**: Record (immutable)
**Thread Safety**: Immutable

| Field | Type | Default | Purpose |
|-------|------|---------|---------|
| `Name` | `string` | required | Rig identifier |
| `Projection` | `ProjectionType` | `Orthographic` | Camera projection mode |
| `Angles` | `IReadOnlyList<CaptureAngle>` | required | All angles (captured + mirror) |
| `FrameSize` | `(int Width, int Height)` | required | Per-frame pixel dimensions |
| `Padding` | `int` | `2` | Pixels between atlas frames |
| `BackgroundColor` | `Color` | `(0,0,0,0)` | Render clear color |
| `IncludeNormalMap` | `bool` | `false` | Generate depth→normal atlas |
| `IncludeShadow` | `bool` | `false` | Shadow pass (future) |
| `TrimTransparent` | `bool` | `false` | Trim transparent borders |

**Computed properties**:
- `CapturedAngles` → angles where `!CanMirror` (the ones that need rendering)
- `MirrorAngles` → angles where `CanMirror` (generated from captures)
- `CapturedAngleCount` → `CapturedAngles.Count`
- `TotalAngleCount` → `Angles.Count` (captured + mirrors)

### CaptureAngle

**Kind**: Record (immutable)
**Thread Safety**: Immutable

| Field | Type | Default | Purpose |
|-------|------|---------|---------|
| `Name` | `string` | required | Angle identifier ("right", "N", "NE") |
| `Yaw` | `float` | required | Degrees rotation around Y (0 = north) |
| `Pitch` | `float` | required | Degrees from horizontal (negative = looking down) |
| `CanMirror` | `bool` | `false` | If true, generates a mirror target from this capture |
| `MirrorTargetName` | `string?` | `null` | Name of the generated mirror angle |
| `MirrorAxis` | `MirrorAxis` | `Horizontal` | Flip axis for mirror generation |

### AtlasOptions

**Kind**: Record (immutable)
**Thread Safety**: Immutable

| Field | Type | Default | Purpose |
|-------|------|---------|---------|
| `MaxWidth` | `int` | `4096` | Maximum atlas width in pixels |
| `MaxHeight` | `int` | `4096` | Maximum atlas height in pixels |
| `Padding` | `int` | `2` | Pixels between frames (overrides rig if set) |
| `PowerOfTwo` | `bool` | `true` | Round atlas dimensions to next power of two |
| `GroupByAnimation` | `bool` | `true` | Keep frames from same animation in rows |

### AnimationConfig

**Kind**: Record (immutable)
**Thread Safety**: Immutable

| Field | Type | Default | Purpose |
|-------|------|---------|---------|
| `FrameCount` | `int` | `8` | Number of frames to capture |
| `SpeedMultiplier` | `float` | `1.0` | Animation playback speed override |
| `TrimStart` | `float` | `0.0` | Normalized start time (skip beginning) |
| `TrimEnd` | `float` | `1.0` | Normalized end time (skip ending) |
| `LoopMode` | `LoopMode` | `None` | Loop mode for playback metadata |

### NormalMapOptions

**Kind**: Record (immutable)
**Thread Safety**: Immutable

| Field | Type | Default | Purpose |
|-------|------|---------|---------|
| `Strength` | `float` | `1.0` | Normal map intensity multiplier |
| `BlurRadius` | `int` | `0` | Gaussian blur on depth before conversion (0 = none) |

### OrthographicParameters

**Kind**: Record (immutable, output of OrthographicSetup)
**Thread Safety**: Immutable

| Field | Type | Purpose |
|-------|------|---------|
| `Position` | `(float X, float Y, float Z)` | Camera world position |
| `Direction` | `(float X, float Y, float Z)` | Camera forward direction (normalized) |
| `Up` | `(float X, float Y, float Z)` | Camera up vector (normalized) |
| `OrthoWidth` | `float` | Orthographic viewport width |
| `OrthoHeight` | `float` | Orthographic viewport height |
| `NearPlane` | `float` | Near clip distance |
| `FarPlane` | `float` | Far clip distance |

### BoundingBox

**Kind**: Struct (value type)
**Thread Safety**: Immutable

| Field | Type | Purpose |
|-------|------|---------|
| `Min` | `(float X, float Y, float Z)` | Minimum corner |
| `Max` | `(float X, float Y, float Z)` | Maximum corner |

**Computed properties**:
- `Center` → `((Min.X + Max.X) / 2, ...)`
- `Extents` → `((Max.X - Min.X) / 2, ...)`
- `Size` → `(Max.X - Min.X, ...)`

### FrameCapture

**Kind**: Record (immutable after creation by bridge)
**Thread Safety**: Immutable

| Field | Type | Purpose |
|-------|------|---------|
| `PixelData` | `byte[]` | RGBA pixels (4 bytes/pixel, row-major) |
| `DepthData` | `float[]?` | Depth buffer (0.0–1.0), null if not captured |
| `Width` | `int` | Frame width in pixels |
| `Height` | `int` | Frame height in pixels |
| `AngleName` | `string` | Source angle |
| `AnimationName` | `string` | Source animation |
| `FrameIndex` | `int` | Frame number in animation |
| `NormalizedTime` | `float` | Animation time when captured |

### Color

**Kind**: Struct (4 bytes, value type)
**Thread Safety**: Immutable

| Field | Type | Purpose |
|-------|------|---------|
| `R` | `byte` | Red (0–255) |
| `G` | `byte` | Green (0–255) |
| `B` | `byte` | Blue (0–255) |
| `A` | `byte` | Alpha (0–255) |

**Static factory**: `Color.Transparent` → `(0, 0, 0, 0)`, `Color.White` → `(255, 255, 255, 255)`

---

## Dependencies

| Dependency | Type | Usage |
|------------|------|-------|
| System.Text.Json | BCL | SpriteSheet JSON serialization |
| System.Numerics | BCL | MathF for trigonometry in OrthographicSetup |

**No external NuGet dependencies.** Pure .NET BCL only.

---

## API Index

### CameraRigPresets

| Method | Signature | Deterministic | Notes |
|--------|-----------|---------------|-------|
| `SideViewBrawler` | `(int frameWidth = 128, int frameHeight = 128) → CameraRig` | Yes | 1 capture (right), 1 mirror (left) |
| `TopDown8Dir` | `(float pitchDegrees = -55f, int frameWidth = 96, int frameHeight = 96) → CameraRig` | Yes | 5 captures (N,NE,E,SE,S), 3 mirrors (NW,W,SW) |
| `TopDown4Dir` | `(float pitchDegrees = -55f, int frameWidth = 96, int frameHeight = 96) → CameraRig` | Yes | 3 captures (N,E,S), 1 mirror (W) |

### OrthographicSetup

| Method | Signature | Deterministic | Notes |
|--------|-----------|---------------|-------|
| `Compute` | `(CaptureAngle, BoundingBox, (int,int) frameSize) → OrthographicParameters` | Yes | Pure trigonometry |

### AnimationSampling

| Method | Signature | Deterministic | Notes |
|--------|-----------|---------------|-------|
| `GenerateUniform` | `(float duration, int frameCount) → FrameSequence` | Yes | Evenly spaced frames |
| `GenerateFromConfig` | `(AnimationInfo, AnimationConfig) → FrameSequence` | Yes | Applies trim and speed |

### AtlasPacker

| Method | Signature | Deterministic | Notes |
|--------|-----------|---------------|-------|
| `Pack` | `(IReadOnlyList<(int w, int h, int index)>, AtlasOptions) → AtlasLayout` | Yes | MaxRects bin-packing |

### MirrorOptimizer

| Method | Signature | Deterministic | Notes |
|--------|-----------|---------------|-------|
| `ComputeMirrors` | `(CameraRig) → IReadOnlyList<MirrorInfo>` | Yes | Single pass over angles |
| `GenerateMirrorFrames` | `(IReadOnlyList<SpriteFrame> captured, IReadOnlyList<MirrorInfo>) → IReadOnlyList<SpriteFrame>` | Yes | Create mirror frame entries |

### DepthToNormal

| Method | Signature | Deterministic | Notes |
|--------|-----------|---------------|-------|
| `Generate` | `(float[] depth, int width, int height, NormalMapOptions) → byte[]` | Yes | Sobel 3×3 convolution |

### AtlasAssembler

| Method | Signature | Deterministic | Notes |
|--------|-----------|---------------|-------|
| `Assemble` | `(IReadOnlyList<FrameCapture>, AtlasLayout, Color bg) → byte[][]` | Yes | Pixel blit per frame |

### SpriteSheetSerializer

| Method | Signature | Deterministic | Notes |
|--------|-----------|---------------|-------|
| `Serialize` | `(SpriteSheet) → string` | Yes | JSON via System.Text.Json |
| `Deserialize` | `(string json) → SpriteSheet` | Yes | JSON parsing |

### CaptureManifest

| Method | Signature | Deterministic | Notes |
|--------|-----------|---------------|-------|
| `Compute` | `(CharacterVariant, IReadOnlyList<CameraRig>, IReadOnlyList<(AnimationInfo,AnimationConfig)>) → CaptureManifest` | Yes | Arithmetic |

---

## Methods

### CameraRigPresets.SideViewBrawler
`(frameWidth: int = 128, frameHeight: int = 128) → CameraRig`

CREATE angles ← [
  CaptureAngle(Name: "right", Yaw: 90, Pitch: 0, CanMirror: true, MirrorTargetName: "left", MirrorAxis: Horizontal)
]
RETURN CameraRig(
  Name: "SideView-Brawler",
  Projection: Orthographic,
  Angles: angles,
  FrameSize: (frameWidth, frameHeight),
  Padding: 2,
  BackgroundColor: Color.Transparent)

---

### CameraRigPresets.TopDown8Dir
`(pitchDegrees: float = -55f, frameWidth: int = 96, frameHeight: int = 96) → CameraRig`

CREATE angles ← [
  CaptureAngle(Name: "N",  Yaw: 0,   Pitch: pitchDegrees, CanMirror: false),
  CaptureAngle(Name: "NE", Yaw: 45,  Pitch: pitchDegrees, CanMirror: true, MirrorTargetName: "NW", MirrorAxis: Horizontal),
  CaptureAngle(Name: "E",  Yaw: 90,  Pitch: pitchDegrees, CanMirror: true, MirrorTargetName: "W",  MirrorAxis: Horizontal),
  CaptureAngle(Name: "SE", Yaw: 135, Pitch: pitchDegrees, CanMirror: true, MirrorTargetName: "SW", MirrorAxis: Horizontal),
  CaptureAngle(Name: "S",  Yaw: 180, Pitch: pitchDegrees, CanMirror: false)
]
RETURN CameraRig(
  Name: "TopDown-8Dir",
  Projection: Orthographic,
  Angles: angles,
  FrameSize: (frameWidth, frameHeight),
  Padding: 2,
  BackgroundColor: Color.Transparent)

---

### CameraRigPresets.TopDown4Dir
`(pitchDegrees: float = -55f, frameWidth: int = 96, frameHeight: int = 96) → CameraRig`

CREATE angles ← [
  CaptureAngle(Name: "N", Yaw: 0,   Pitch: pitchDegrees, CanMirror: false),
  CaptureAngle(Name: "E", Yaw: 90,  Pitch: pitchDegrees, CanMirror: true, MirrorTargetName: "W", MirrorAxis: Horizontal),
  CaptureAngle(Name: "S", Yaw: 180, Pitch: pitchDegrees, CanMirror: false)
]
RETURN CameraRig(
  Name: "TopDown-4Dir",
  Projection: Orthographic,
  Angles: angles,
  FrameSize: (frameWidth, frameHeight),
  Padding: 2,
  BackgroundColor: Color.Transparent)

---

### OrthographicSetup.Compute
`(angle: CaptureAngle, bounds: BoundingBox, frameSize: (int Width, int Height)) → OrthographicParameters`

// Step 1: Camera direction from yaw/pitch angles
COMPUTE yawRad ← angle.Yaw * MathF.PI / 180f
COMPUTE pitchRad ← angle.Pitch * MathF.PI / 180f
COMPUTE dirX ← MathF.Sin(yawRad) * MathF.Cos(pitchRad)
COMPUTE dirY ← MathF.Sin(pitchRad)
COMPUTE dirZ ← MathF.Cos(yawRad) * MathF.Cos(pitchRad)
COMPUTE direction ← normalize(dirX, dirY, dirZ)

// Step 2: Camera position — back along direction from bounds center
COMPUTE center ← bounds.Center
COMPUTE halfDiag ← length(bounds.Extents)
COMPUTE distance ← halfDiag * 2.5f    // generous distance to ensure full visibility
COMPUTE position ← center - direction * distance

// Step 3: Up vector (handle near-vertical pitch)
IF MathF.Abs(angle.Pitch) > 89f
  COMPUTE up ← (0, 0, -MathF.Sign(angle.Pitch))
ELSE
  COMPUTE up ← (0, 1, 0)

// Step 4: Right and corrected up from cross products
COMPUTE right ← normalize(cross(direction, up))
COMPUTE correctedUp ← cross(right, direction)

// Step 5: Project all 8 bounding box corners onto the camera's view plane
COMPUTE corners[8] ← all combinations of (Min.X|Max.X, Min.Y|Max.Y, Min.Z|Max.Z)
SET minU ← +INF, maxU ← -INF, minV ← +INF, maxV ← -INF
FOREACH corner in corners
  COMPUTE relative ← corner - position
  COMPUTE u ← dot(relative, right)
  COMPUTE v ← dot(relative, correctedUp)
  minU ← min(minU, u); maxU ← max(maxU, u)
  minV ← min(minV, v); maxV ← max(maxV, v)

// Step 6: Compute ortho dimensions with safety margin
COMPUTE orthoWidth ← (maxU - minU) * 1.1f
COMPUTE orthoHeight ← (maxV - minV) * 1.1f

// Step 7: Adjust for frame aspect ratio
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

### AnimationSampling.GenerateUniform
`(duration: float, frameCount: int) → FrameSequence`

VALIDATE frameCount > 0                              → ArgumentException
VALIDATE duration > 0                                → ArgumentException

CREATE timestamps ← new float[frameCount]
COMPUTE interval ← 1.0f / frameCount
FOREACH i FROM 0 TO frameCount - 1
  // Center of each frame's time window (avoids exact 0.0 and 1.0 boundaries)
  timestamps[i] ← i * interval + interval * 0.5f

RETURN FrameSequence(timestamps, duration, frameCount)

---

### AnimationSampling.GenerateFromConfig
`(info: AnimationInfo, config: AnimationConfig) → FrameSequence`

// Apply trim: only sample within [TrimStart, TrimEnd] range
COMPUTE effectiveDuration ← info.Duration * (config.TrimEnd - config.TrimStart) / config.SpeedMultiplier
COMPUTE interval ← (config.TrimEnd - config.TrimStart) / config.FrameCount

CREATE timestamps ← new float[config.FrameCount]
FOREACH i FROM 0 TO config.FrameCount - 1
  timestamps[i] ← config.TrimStart + i * interval + interval * 0.5f

RETURN FrameSequence(timestamps, effectiveDuration, config.FrameCount)

---

### AtlasPacker.Pack
`(frames: IReadOnlyList<(int Width, int Height, int Index)>, options: AtlasOptions) → AtlasLayout`

// Sort by height descending (MaxRects standard heuristic), then by index for stability
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

  // Best Short Side Fit: find free rect that minimizes leftover short side
  SET bestRect ← null, bestShortSide ← int.MaxValue, bestLongSide ← int.MaxValue
  FOREACH rect in freeRects
    IF pw <= rect.Width AND ph <= rect.Height
      COMPUTE shortSide ← min(rect.Width - pw, rect.Height - ph)
      COMPUTE longSide ← max(rect.Width - pw, rect.Height - ph)
      IF shortSide < bestShortSide OR (shortSide == bestShortSide AND longSide < bestLongSide)
        bestRect ← rect; bestShortSide ← shortSide; bestLongSide ← longSide

  IF bestRect is null
    // Overflow: start new atlas
    currentAtlas += 1
    freeRects.Clear()
    freeRects.Add(Rectangle(0, 0, options.MaxWidth, options.MaxHeight))
    // Re-attempt placement (same frame, new atlas)
    // ... (repeat BSSF search on fresh freeRects — guaranteed to succeed for any frame ≤ MaxSize)

  // Place frame
  COMPUTE placement ← PackedFrame(frame.Index, currentAtlas, bestRect.X, bestRect.Y, frame.Width, frame.Height)
  allPlacements.Add(placement)

  // MaxRects split: subdivide free space around placed frame
  COMPUTE placed ← Rectangle(bestRect.X, bestRect.Y, pw, ph)
  CREATE newFreeRects ← new List<Rectangle>()
  FOREACH rect in freeRects
    IF NOT rect.Intersects(placed)
      newFreeRects.Add(rect)
      CONTINUE
    // Split into up to 4 new free rects (left, right, top, bottom of placed)
    IF placed.X > rect.X    // Left remainder
      newFreeRects.Add(Rectangle(rect.X, rect.Y, placed.X - rect.X, rect.Height))
    IF placed.Right < rect.Right    // Right remainder
      newFreeRects.Add(Rectangle(placed.Right, rect.Y, rect.Right - placed.Right, rect.Height))
    IF placed.Y > rect.Y    // Top remainder
      newFreeRects.Add(Rectangle(rect.X, rect.Y, rect.Width, placed.Y - rect.Y))
    IF placed.Bottom < rect.Bottom    // Bottom remainder
      newFreeRects.Add(Rectangle(rect.X, placed.Bottom, rect.Width, rect.Bottom - placed.Bottom))

  // Prune: remove free rects fully contained within other free rects
  freeRects ← PruneFreeRects(newFreeRects)

// Compute final atlas dimensions
FOREACH atlasIndex FROM 0 TO currentAtlas
  COMPUTE maxX ← max of (p.X + p.Width) for placements in this atlas
  COMPUTE maxY ← max of (p.Y + p.Height) for placements in this atlas
  IF options.PowerOfTwo
    maxX ← nextPowerOfTwo(maxX)
    maxY ← nextPowerOfTwo(maxY)

COMPUTE totalFrameArea ← SUM(frame.Width * frame.Height for all frames)
COMPUTE totalAtlasArea ← SUM(atlasWidth * atlasHeight for all atlases)
COMPUTE efficiency ← (float)totalFrameArea / totalAtlasArea

RETURN AtlasLayout(allPlacements, atlasWidths[], atlasHeights[], efficiency, currentAtlas + 1)

---

### MirrorOptimizer.ComputeMirrors
`(rig: CameraRig) → IReadOnlyList<MirrorInfo>`

CREATE mirrors ← new List<MirrorInfo>()
FOREACH angle in rig.Angles WHERE angle.CanMirror AND angle.MirrorTargetName is not null
  mirrors.Add(MirrorInfo(
    SourceAngleName: angle.Name,
    TargetAngleName: angle.MirrorTargetName,
    FlipAxis: angle.MirrorAxis))
RETURN mirrors

---

### MirrorOptimizer.GenerateMirrorFrames
`(capturedFrames: IReadOnlyList<SpriteFrame>, mirrors: IReadOnlyList<MirrorInfo>) → IReadOnlyList<SpriteFrame>`

// For each mirror relationship, create mirror frame entries for all captured frames of the source angle
CREATE mirrorFrames ← new List<SpriteFrame>()
SET nextIndex ← capturedFrames.Count    // Mirror frame indices start after captured frames

FOREACH mirror in mirrors
  // Find all captured frames for the source angle
  COMPUTE sourceFrames ← capturedFrames.Where(f => f.AngleName == mirror.SourceAngleName)
  FOREACH source in sourceFrames
    mirrorFrames.Add(SpriteFrame(
      Index: nextIndex,
      AtlasIndex: source.AtlasIndex,
      AngleName: mirror.TargetAngleName,
      AnimationName: source.AnimationName,
      FrameInAnimation: source.FrameInAnimation,
      Rect: source.Rect,                    // Same atlas position — game engine flips at render
      TrimmedRect: source.TrimmedRect,
      Pivot: FlipPivot(source.Pivot, mirror.FlipAxis),
      Duration: source.Duration,
      IsMirror: true,
      MirrorSourceIndex: source.Index))
    nextIndex += 1

RETURN mirrorFrames

// Helper: flip pivot for horizontal mirror
FUNCTION FlipPivot(pivot: Vector2, axis: MirrorAxis) → Vector2
  IF axis == Horizontal
    RETURN Vector2(1.0f - pivot.X, pivot.Y)    // Flip X
  ELSE
    RETURN Vector2(pivot.X, 1.0f - pivot.Y)    // Flip Y

---

### DepthToNormal.Generate
`(depth: float[], width: int, height: int, options: NormalMapOptions) → byte[]`

VALIDATE depth.Length == width * height              → ArgumentException

// Optional: apply Gaussian blur to depth before conversion
IF options.BlurRadius > 0
  depth ← GaussianBlur(depth, width, height, options.BlurRadius)

CREATE normals ← new byte[width * height * 4]

FOREACH y FROM 0 TO height - 1
  FOREACH x FROM 0 TO width - 1
    // Sobel 3×3 horizontal gradient (dz/dx)
    COMPUTE dzdx ← (
      Sample(x+1, y-1) + 2 * Sample(x+1, y) + Sample(x+1, y+1)
      - Sample(x-1, y-1) - 2 * Sample(x-1, y) - Sample(x-1, y+1)
    ) * options.Strength

    // Sobel 3×3 vertical gradient (dz/dy)
    COMPUTE dzdy ← (
      Sample(x-1, y+1) + 2 * Sample(x, y+1) + Sample(x+1, y+1)
      - Sample(x-1, y-1) - 2 * Sample(x, y-1) - Sample(x+1, y-1)
    ) * options.Strength

    // Normal vector (tangent space: Z outward)
    COMPUTE len ← MathF.Sqrt(dzdx * dzdx + dzdy * dzdy + 1.0f)
    COMPUTE nx ← -dzdx / len
    COMPUTE ny ← -dzdy / len
    COMPUTE nz ← 1.0f / len

    // Encode to RGB [0,255]
    COMPUTE i ← (y * width + x) * 4
    normals[i + 0] ← (byte)((nx * 0.5f + 0.5f) * 255f)
    normals[i + 1] ← (byte)((ny * 0.5f + 0.5f) * 255f)
    normals[i + 2] ← (byte)((nz * 0.5f + 0.5f) * 255f)
    normals[i + 3] ← 255

RETURN normals

// Helper: sample depth with boundary clamping
FUNCTION Sample(x: int, y: int) → float
  RETURN depth[clamp(y, 0, height-1) * width + clamp(x, 0, width-1)]

---

### AtlasAssembler.Assemble
`(frames: IReadOnlyList<FrameCapture>, layout: AtlasLayout, backgroundColor: Color) → byte[][]`

CREATE atlases ← new byte[layout.AtlasCount][]

// Initialize each atlas with background color
FOREACH i FROM 0 TO layout.AtlasCount - 1
  COMPUTE atlasWidth ← layout.AtlasWidths[i]
  COMPUTE atlasHeight ← layout.AtlasHeights[i]
  atlases[i] ← new byte[atlasWidth * atlasHeight * 4]
  // Fill background
  FOREACH p FROM 0 TO atlasWidth * atlasHeight - 1
    atlases[i][p * 4 + 0] ← backgroundColor.R
    atlases[i][p * 4 + 1] ← backgroundColor.G
    atlases[i][p * 4 + 2] ← backgroundColor.B
    atlases[i][p * 4 + 3] ← backgroundColor.A

// Blit each captured frame into its atlas position
FOREACH placement in layout.Placements
  COMPUTE frame ← frames.First(f => f is at placement.FrameIndex)  // by index match
  COMPUTE atlas ← atlases[placement.AtlasIndex]
  COMPUTE atlasWidth ← layout.AtlasWidths[placement.AtlasIndex]

  // Row-by-row copy from frame pixels to atlas
  FOREACH row FROM 0 TO frame.Height - 1
    COMPUTE srcOffset ← row * frame.Width * 4
    COMPUTE dstOffset ← ((placement.Y + row) * atlasWidth + placement.X) * 4
    Buffer.BlockCopy(frame.PixelData, srcOffset, atlas, dstOffset, frame.Width * 4)

RETURN atlases

---

### SpriteSheetSerializer.Serialize
`(spriteSheet: SpriteSheet) → string`

// Serialize to JSON using System.Text.Json with camelCase naming and ordered properties
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

SET totalCapturedFrames ← 0
SET totalMirrorFrames ← 0
CREATE rigManifests ← new List<RigManifest>()

FOREACH rig in rigs
  COMPUTE capturedAngleCount ← rig.CapturedAngles.Count
  COMPUTE mirrorAngleCount ← rig.MirrorAngles.Count
  SET rigCaptured ← 0
  SET rigMirror ← 0

  FOREACH (info, config) in animations
    rigCaptured += capturedAngleCount * config.FrameCount
    rigMirror += mirrorAngleCount * config.FrameCount

  totalCapturedFrames += rigCaptured
  totalMirrorFrames += rigMirror
  rigManifests.Add(RigManifest(rig.Name, rigCaptured, rigMirror, capturedAngleCount, mirrorAngleCount))

COMPUTE estimatedCaptureTimeMs ← totalCapturedFrames * 50  // ~50ms per frame (render + readback)

RETURN CaptureManifest(
  Variant: variant,
  Rigs: rigManifests,
  TotalCapturedFrames: totalCapturedFrames,
  TotalMirrorFrames: totalMirrorFrames,
  TotalFrames: totalCapturedFrames + totalMirrorFrames,
  EstimatedCaptureTimeMs: estimatedCaptureTimeMs,
  AnimationCount: animations.Count)

---

*This document contains pseudo-code representing intended behavior. Verify against actual implementation once the SDK is built.*
