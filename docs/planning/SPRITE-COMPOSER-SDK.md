# Sprite Composer SDK: 3D-to-2D Sprite Sheet Pipeline

> **Type**: Design
> **Status**: Active
> **Created**: 2026-04-13
> **Last Updated**: 2026-04-13
> **North Stars**: #4 (Ship Games Fast), #5 (Emergent Over Authored)
> **Related SDKs**: scene-composer (composer pattern reference), voxel-builder (extended composer patterns), cinematic-theory (Theory/Composer domain design precedent)
> **Related Repos**: defenders-kb (primary consumer), stride-demo (Stride animation patterns)
> **Inspiration**: *Hades* / *Hades II* (Supergiant Games — 3D models rendered to isometric sprites), *Dead Cells* (Motion Twin — 3D models rendered to side-view sprites), *Clair Obscur: Expedition 33* (genre-blending visual fidelity)
> **Primary Consumer**: Defenders of Ba'gata — a fantasy castle-defense action-RPG brawler requiring dual-orientation sprite sheets (side-view brawler + 55° top-down) from Synty 3D character models

## Summary

Designs a new creative SDK domain — **Sprite** — that captures 3D models as 2D sprite sheet assets for games that use pre-rendered sprites at runtime. The SDK family follows the established Theory/Composer pattern: `sprite-theory` provides pure-computation primitives (camera mathematics, atlas packing, animation sampling, mirror optimization), `sprite-composer` provides the engine-agnostic interactive editor core (capture sessions, project management, preview, undo/redo), and `sprite-composer-stride` implements the Stride engine bridge (FBX model loading, skeletal animation, render-to-texture frame capture, depth buffer extraction for normal maps).

The immediate consumer is **Defenders of Ba'gata**, which needs every playable character, troop, enemy, and boss rendered as sprite sheets from two orientations — side-view for brawler/attack phases and 55° top-down for defense and boss arena phases — with all animations captured in both. Equipment variants (Synty modular armor/weapons) multiply the combinatorial space. Without purpose-built tooling, this is the single most labor-intensive aspect of the game. With the Sprite Composer, it becomes a configuration-driven capture session.

No implementation exists yet.

---

## Part 1: The Problem

### Why 3D-to-2D?

Defenders is a 2D game at runtime — sprites on screen, tile-based levels, classic brawler and top-down perspectives. But the visual source material is 3D: Synty's POLYGON character models with modular equipment, skeletal animation rigs, and hundreds of animation clips. The game needs the *visual quality* of 3D (consistent lighting, smooth animation, equipment combinations) without the *runtime cost* of 3D (mobile GPU budgets, draw call limits, shader complexity on Android/iOS).

This is the Hades technique. Supergiant Games built their characters as 3D models, rendered them from an isometric camera, and output sprite sheets consumed by a 2D runtime. Dead Cells did the same from a side perspective. The 3D models exist as an authoring source; the sprites are the shipped product.

### The Dual Orientation Problem

Defenders weaves three gameplay modes through a single campaign:

| Mode | Perspective | Camera | Sprite Requirement |
|------|------------|--------|-------------------|
| **Castle Defense** | Top-down | ~55° from horizontal | 8-directional sprites (N, NE, E, SE, S, SW, W, NW) |
| **Brawler/Attack** | Side-scrolling | 0° (dead level) | Left/Right sprites (capture right, mirror left) |
| **Boss Arena** | Top-down | ~55° from horizontal | 8-directional sprites (same as defense) |

Every character — playable heroes, troops, enemies, bosses — needs a complete set of animation sprites in **both** orientations. A character with 20 animations at 8 frames each requires:

- **Side-view**: 1 direction × 20 animations × 8 frames = **160 frames**
- **Top-down**: 5 directions × 20 animations × 8 frames = **800 frames** (mirror optimization provides the other 3 directions)
- **Total per character variant**: **960 frames**

### The Equipment Combinatorics

Synty models use a modular equipment system: a base character mesh with a skeleton, and separate equipment meshes (helmets, chest armor, gauntlets, boots, weapons, shields) that attach to skeleton bones. A single character with 5 armor sets and 3 weapon types is 15 visual variants — 15 × 960 = **14,400 frames** for one hero.

Multiply across the full roster (heroes, troops, enemies, bosses) and the sprite production volume becomes untenable without automation. The Sprite Composer doesn't just make this easier — it makes it *possible*.

### What Exists Today

| Asset | Status | Location |
|-------|--------|----------|
| Synty FBX character models | Available | NAS mount at `/mnt/SyntyAssets` (unmounted currently) |
| Synty animation clips | Available | Included with Synty packs (FBX format) |
| Custom equipment/props | To be created | Will follow Synty skeleton conventions |
| Stride animation integration | Proven | stride-demo `MonsterAnimationController` — clip loading, blending, state machine |
| Stride asset loading | Proven | stride-demo `ContentManagerScript` — FBX/bundle loading via `StrideBannouAssetLoader` |
| Sprite sheets | Do not exist | **This SDK produces them** |
| Animation pipeline specs | Stubbed | defenders-kb `20-animation/` — all documents are empty stubs |

---

## Part 2: Architectural Decision — Theory/Composer Pattern

### Why This Pattern

The Sprite domain follows the established creative SDK pattern for the same reasons Music, Storyline, and Cinematic do: clean separation of pure computation from engine-specific concerns, independent referenceability of each layer, and the ability to swap engine bridges without touching core logic.

### Domain Matrix (Updated)

| Domain | Theory | Storyteller | Composer | Status |
|--------|--------|-------------|----------|--------|
| **Music** | `music-theory` | `music-storyteller` | *planned* | Theory + Storyteller complete |
| **Storyline** | `storyline-theory` | `storyline-storyteller` | `storyline-composer` *planned* | Theory + Storyteller complete |
| **Cinematic** | `cinematic-theory` *planned* | `cinematic-storyteller` *planned* | `cinematic-composer` *planned* | All in design |
| **Scene** | — | — | `scene-composer` | Composer complete (reference impl) |
| **Voxel** | `voxel-core` | `voxel-generator` | `voxel-builder` | Design complete, no code |
| **Sprite** | `sprite-theory` **NEW** | *SpriteBatcher, future* | `sprite-composer` **NEW** | **Starting now** |

### Why No Storyteller Layer (Yet)

Music and Storyline have Storytellers because they procedurally generate *novel content* from goals and context via GOAP planning. The Sprite domain captures *recordings* of existing 3D content — there's no "goal-driven sprite generation." The mapping:

| Layer | Music | Storyline | Sprite |
|-------|-------|-----------|--------|
| **Theory** | Harmony, pitch, scales | Narrative grammar, arc classification | Camera math, atlas packing, frame sampling |
| **Storyteller** | GOAP emotional composition | GOAP narrative planning | *SpriteBatcher* (future — configuration-driven batch automation, not GOAP) |
| **Composer** | *planned* (workbench) | *planned* (scenario editor) | `sprite-composer` (capture editor) |

The SpriteBatcher would be the domain's "Storyteller equivalent" — not GOAP-driven, but automation-driven: take a character definition (model + equipment list + animation list + camera rigs) and produce all sprite sheets without user interaction. Documented as a future phase.

### Pattern Comparison

| Aspect | Scene (reference) | Voxel | Sprite |
|--------|-------------------|-------|--------|
| **Core concept** | Hierarchical scene graph editing | Discrete voxel grid editing | 3D-to-2D frame capture |
| **Data flow** | SDK data → engine for display | SDK data → engine for display | Engine renders → SDK captures pixels |
| **Bridge direction** | Push (data → engine) | Push (mesh → engine) | **Pull** (rendered frames ← engine) |
| **Bridge contract** | `ISceneComposerBridge` | `IVoxelBuilderBridge` | `ISpriteComposerBridge` |
| **Primary operations** | Create/delete/move nodes | Place/erase/fill voxels | Load model, play animation, capture frame |
| **Undo/redo scope** | Scene modifications | Grid modifications | Configuration changes (rig, animation params) |
| **Output** | Scene document (JSON) | Voxel grid (.bvox) | Sprite atlas (PNG) + metadata (JSON) |
| **Persistence** | lib-scene checkout/commit | lib-save-load deltas | Local project files |
| **Headless mode** | N/A | Null bridge for server-side | **Null bridge for batch processing** |

The key architectural novelty: **the bridge pulls rendered data FROM the engine instead of pushing data TO the engine for display.** The bridge must support render-to-texture capture, depth buffer extraction, and animation time control — capabilities not needed by scene-composer or voxel-builder bridges.

---

## Part 3: sprite-theory (Pure Computation)

### Overview

sprite-theory is the theory-layer SDK for the sprite domain. It provides pure-computation primitives for camera projection, animation frame sampling, atlas layout, mirror optimization, and the sprite sheet metadata format. Zero dependencies. Deterministic. Independently referenceable by any .NET project.

**Analogous to**: MusicTheory (harmony and pitch primitives for music composition), voxel-core (grid and math primitives for voxel editing).

### Package Identity

| Property | Value |
|----------|-------|
| Directory | `sdks/sprite-theory/` |
| PackageId | `BeyondImmersion.Bannou.SpriteTheory` |
| RootNamespace | `BeyondImmersion.Bannou.SpriteTheory` |
| AssemblyName | `BeyondImmersion.Bannou.SpriteTheory` |
| Target | `net8.0` |
| Dependencies | None |

### Project Structure

```
sdks/sprite-theory/
├── AssetReference.cs             # Engine-agnostic asset identifier (BundleId, AssetId, VariantId?)
├── BoundingBox.cs                # Axis-aligned 3D bounds (Vector3 min/max)
├── Color.cs, Rectangle.cs, Vector2.cs
├── Camera/
│   ├── CameraRig.cs              # Rig definition: projection type, angle list, frame size
│   ├── CaptureAngle.cs           # Single angle: name, yaw, pitch, ProducesMirror metadata
│   ├── CameraRigPresets.cs       # Built-in rigs: SideViewBrawler(), TopDown8Dir(), TopDown4Dir()
│   ├── OrthographicSetup.cs      # Compute ortho camera params to fit character bounds at angle
│   ├── OrthographicParameters.cs # Output of OrthographicSetup (Vector3 position/direction/up)
│   └── PivotComputer.cs          # ProjectWorldPointToFrame (primitive) + ComputeFromBounds (wrapper)
├── Animation/
│   ├── AnimationSampling.cs      # Frame timing: uniform + config-based
│   ├── FrameSequence.cs          # Ordered list of frame timestamps for one animation
│   ├── AnimationConfig.cs        # Per-animation capture config: frame count, speed, trim
│   └── AnimationInfo.cs          # Bridge-reported animation metadata
├── Atlas/
│   ├── AtlasPacker.cs            # MaxRects-BSSF bin-packing algorithm
│   ├── AtlasLayout.cs            # Result: frame positions, atlas dimensions, packing efficiency
│   ├── AtlasOptions.cs           # Max atlas size, padding, power-of-two, per-animation rows
│   ├── PackedFrame.cs            # Single frame placement (atlas index + X/Y/W/H)
│   └── MultiAtlasStrategy.cs     # Overflow handling when frames exceed single atlas size
├── Mirror/
│   ├── MirrorOptimizer.cs        # Compute which angles are mirrors, generate flip metadata
│   └── MirrorInfo.cs             # Per-frame mirror data: source angle, flip axis
├── Metadata/
│   ├── SpriteSheet.cs            # Complete output: atlases + frames + animations + mirrors
│   ├── SpriteFrame.cs            # Per-frame: rect in atlas, pivot point, duration, trimmed bounds
│   ├── SpriteAnimation.cs        # Per-animation: name, AngleFrameMap, loop mode, duration
│   ├── AtlasInfo.cs              # Per-atlas image (index, filename, width, height)
│   ├── AnimationEvent.cs         # Optional per-frame event (hit, sound, effect)
│   ├── CharacterVariant.cs       # Model (AssetReference) + equipment + material overrides + pivot override + AnchorBoneName
│   ├── EquipmentSlot.cs          # Slot name + Mesh (AssetReference) + bone name
│   ├── FrameCapture.cs           # Bridge-produced: pixels + depth + variant/rig/angle/animation identity
│   ├── CaptureManifest.cs        # Full capture job: variant + rigs + animations → expected outputs
│   └── RigManifest.cs            # Per-rig breakdown within CaptureManifest
├── NormalMap/
│   ├── DepthToNormal.cs          # Convert depth buffer pixels to normal map (Sobel)
│   └── NormalMapOptions.cs       # Strength, blur radius
├── Export/
│   ├── SpriteSheetSerializer.cs  # SpriteSheet ↔ JSON (the canonical metadata format)
│   ├── IPixelSource.cs           # Abstraction: raw RGBA pixel data + dimensions (engine-agnostic)
│   ├── IDepthSource.cs           # Abstraction: depth buffer data + dimensions (engine-agnostic)
│   └── AtlasAssembler.cs         # Compose captured frames into atlas RGBA byte arrays
└── sprite-theory.csproj
```

**Removed from initial structure** (see § Part 10):
- `Camera/ProjectionMath.cs` — folded into `OrthographicSetup.Compute` (Decision 10.4).
- `Animation/MotionAnalysis.cs` — deferred (Decision 10.5); was already plan-tagged "future".
- `Export/PngWriter.cs` — PNG encoding is the consumer's responsibility via `IAtlasEncoder` (Decision 10.2).

### Core Types

#### CameraRig and CaptureAngle

```
CameraRig
├── Name: string                    # "SideView-Brawler", "TopDown-55deg", custom
├── Projection: ProjectionType      # Orthographic | Perspective
├── Angles: IReadOnlyList<CaptureAngle>
├── FrameSize: (int Width, int Height)  # Per-frame pixel dimensions (e.g., 128×128)
├── Padding: int                    # Pixels between frames in atlas (default: 2)
├── BackgroundColor: Color          # Render clear color (default: transparent)
├── IncludeNormalMap: bool          # Also capture depth → normal map atlas
├── IncludeShadow: bool             # Render shadow pass (future)
└── TrimTransparent: bool           # Trim transparent borders per frame (reduce atlas size)

CaptureAngle
├── Name: string                    # "right", "N", "NE", "SE", etc.
├── Yaw: float                      # Degrees rotation around Y axis (0 = north/forward)
├── Pitch: float                    # Degrees from horizontal (0 = level, -55 = looking down 55°)
├── ProducesMirror: bool            # If true, additively produces a flipped counterpart (this angle IS still rendered)
├── MirrorTargetName: string?       # Name of the derived mirror (e.g., "NE" produces "NW")
└── MirrorAxis: MirrorAxis          # Horizontal (most common) | Vertical
```

**`ProducesMirror` is additive, not a skip flag** (Decision 10.1): every angle in `rig.Angles` is rendered by the bridge. When `ProducesMirror = true`, `MirrorOptimizer` additionally produces a derived `SpriteFrame` pointing back at the rendered source with a flipped pivot. Mirror target angles (`NW`, `left`, etc.) never appear in `rig.Angles`.

#### Camera Rig Presets

```csharp
public static class CameraRigPresets
{
    /// <summary>
    /// Dead Cells / brawler style: side-on camera, capture right-facing only.
    /// Mirror provides left-facing. 1 capture per animation.
    /// </summary>
    public static CameraRig SideViewBrawler(int frameWidth = 128, int frameHeight = 128);

    /// <summary>
    /// Hades / Defenders style: 55° top-down, 8-directional.
    /// 5 captures (N, NE, E, SE, S), mirrors provide NW, W, SW.
    /// </summary>
    public static CameraRig TopDown8Dir(
        float pitchDegrees = -55f,
        int frameWidth = 96,
        int frameHeight = 96);

    /// <summary>
    /// 4-directional top-down for simpler use cases.
    /// 3 captures (N, E, S), mirror provides W.
    /// </summary>
    public static CameraRig TopDown4Dir(
        float pitchDegrees = -55f,
        int frameWidth = 96,
        int frameHeight = 96);
}
```

#### SpriteSheet (Output Metadata)

```
SpriteSheet
├── Version: string                            # Schema version (e.g., "1.0")
├── Generator: string                          # "BeyondImmersion.Bannou.SpriteComposer"
├── GeneratedAt: DateTimeOffset
├── Variant: CharacterVariant                  # What was captured (model, equipment)
├── Rig: CameraRig                             # Camera rig used
├── AtlasWidth: int                            # Atlas image width in pixels
├── AtlasHeight: int                           # Atlas image height in pixels
├── Animations: IReadOnlyList<SpriteAnimation> # All captured animations
├── Frames: IReadOnlyList<SpriteFrame>         # All frames (referenced by index from animations)
├── Mirrors: IReadOnlyList<MirrorInfo>         # Mirror generation metadata
├── NormalMapAtlas: string?                    # Filename of normal map atlas (if generated)
└── CustomProperties: Dictionary<string, string>  # Game-specific metadata

SpriteFrame
├── Index: int                         # Global frame index
├── AngleName: string                  # Which capture angle ("right", "NE", etc.)
├── AnimationName: string              # Which animation ("idle", "attack_light", etc.)
├── FrameInAnimation: int              # Frame number within the animation (0-based)
├── Rect: Rectangle                    # Position and size in atlas (x, y, width, height)
├── TrimmedRect: Rectangle?            # Actual content rect if trimming enabled
├── Pivot: Vector2                     # Pivot point relative to frame (0,0 = top-left, 0.5,0.5 = center)
├── Duration: float                    # Frame display duration in seconds
├── IsMirror: bool                     # True if this frame is a horizontal flip of another
└── MirrorSourceIndex: int?            # Index of the source frame (if IsMirror)

SpriteAnimation
├── Name: string                       # Animation name (matches source clip name)
├── LoopMode: LoopMode                 # None, Loop, PingPong
├── TotalDuration: float               # Total animation duration in seconds
├── FrameIndices: IReadOnlyList<int>   # Ordered frame indices per angle per direction
├── AngleFrameMap: Dictionary<string, int[]>  # AngleName → frame indices for that angle
└── Events: IReadOnlyList<AnimationEvent>?    # Optional: hit frames, sound cues, etc.
```

#### CharacterVariant (Input Definition)

```
CharacterVariant
├── Name: string                                                       # Variant identifier (e.g., "warrior_plate_sword")
├── Model: AssetReference                                              # Bridge-resolved reference to base character model
├── Equipment: IReadOnlyList<EquipmentSlot>                            # Attached equipment
│   ├── EquipmentSlot
│   │   ├── SlotName: string                                           # "head", "chest", "weapon_r", "shield_l"
│   │   ├── Mesh: AssetReference                                       # Bridge-resolved reference to equipment mesh
│   │   └── BoneName: string                                           # Skeleton bone to attach to
├── MaterialOverrides: IReadOnlyDictionary<string, AssetReference>?    # Palette/material swaps (slot → material ref)
├── Scale: float                                                       # Model scale factor (default: 1.0)
├── PivotOverride: Vector2?                                            # Per-variant pivot override (highest priority)
└── AnchorBoneName: string?                                            # Optional skeleton-bone anchor (bridge-resolved via TryGetBonePosition)
```

**`AssetReference`** (`BundleId`, `AssetId`, `VariantId?`) is the engine-agnostic asset identifier shared with scene-composer's asset-loading convention. The bridge resolves each reference to an engine-specific handle at load time (Stride `Model`, Godot `PackedScene`, Unity prefab). For raw-FBX workflows, bridges supporting filesystem asset sources register loose files as single-asset bundles — the exact convention is the bridge's concern.

**Pivot resolution order** (applied by the capture session):

1. `PivotOverride` — used verbatim if set. Highest priority.
2. `AnchorBoneName` — if set AND the bridge advertises `SupportsSkeletonIntrospection` AND the bone exists, the bridge's `TryGetBonePosition` returns the bone's world position, projected via `PivotComputer.ProjectWorldPointToFrame`.
3. `PivotComputer.ComputeFromBounds` — feet-on-ground fallback from bounding box.
4. `PivotComputer.DefaultHumanoidPivot` — terminal fallback when camera basis is degenerate.

Use `AnchorBoneName` for subjects whose bounding-box minimum is not a sensible anchor (flowing cloth extending bounds below the feet, weapons extending bounds outward, asymmetric rigs). Use `PivotOverride` for subjects where even a bone anchor is unreliable.

### Atlas Packing

The `AtlasPacker` uses the **MaxRects** bin-packing algorithm (optimal for variable-size rectangles):

1. All frame captures are sized according to `CameraRig.FrameSize`
2. If `TrimTransparent` is enabled, each frame's transparent borders are trimmed — the actual content rect varies per frame
3. MaxRects places frames into the atlas, respecting `Padding` between frames
4. If frames exceed `AtlasOptions.MaxSize` (default: 4096×4096), `MultiAtlasStrategy` splits across multiple atlas images
5. For readability, `AtlasOptions.GroupByAnimation` (default: true) keeps frames from the same animation in rows

**Determinism contract**: Same inputs → same atlas layout. Frame ordering is deterministic (sorted by animation name, then angle name, then frame index). MaxRects tie-breaking uses frame index as the stable sort key.

### Mirror Optimization

`MirrorOptimizer` analyzes a `CameraRig` and computes:

1. Which angles produce mirrors (defined by `CaptureAngle.ProducesMirror = true`; see Decision 10.1)
2. For each mirrored angle, the source angle and flip axis
3. The atlas includes mirror frame entries with `IsMirror = true` and `MirrorSourceIndex` pointing to the actual captured frame

**The optimization math for Defenders**:

| Rig | Total Directions | Captured | Mirrored | Savings |
|-----|-----------------|----------|----------|---------|
| SideView-Brawler | 2 (L, R) | 1 (R) | 1 (L) | 50% |
| TopDown-55deg | 8 (N, NE, E, SE, S, SW, W, NW) | 5 (N, NE, E, SE, S) | 3 (NW, W, SW) | 37.5% |

Combined: for a character with 20 animations × 8 frames, instead of capturing 10 directions × 160 = **1,600 frames**, we capture 6 directions × 160 = **960 frames**. The other 640 are metadata-only mirrors pointing to their source frames. The game engine flips horizontally at render time using the mirror data.

### Normal Map Generation

When `CameraRig.IncludeNormalMap` is true, the capture pipeline also extracts the depth buffer alongside the color buffer for each frame. `DepthToNormal` converts depth data to tangent-space normal maps using a Sobel filter:

1. For each pixel, sample depth at neighboring pixels (3×3 kernel)
2. Compute horizontal and vertical gradients
3. Cross-product → surface normal
4. Encode to RGB (R = X, G = Y, B = Z, mapped from [-1,1] to [0,255])

Normal maps enable 2D dynamic lighting on sprites at runtime — the technique Dead Cells used to make their pre-rendered sprites react to in-game light sources. The normal map atlas uses the same layout as the color atlas (same frame positions, same packing) so the game can sample both with the same UV coordinates.

### JSON Metadata Schema

The canonical output format. Engine-agnostic, parseable by any language:

```json
{
  "version": "1.0",
  "generator": "BeyondImmersion.Bannou.SpriteComposer",
  "generatedAt": "2026-04-13T14:30:00Z",
  "variant": {
    "name": "warrior_plate_sword",
    "model": { "bundleId": "characters.synty", "assetId": "Warrior_Base" },
    "equipment": [
      { "slotName": "chest", "mesh": { "bundleId": "equipment.synty", "assetId": "Plate_Chest" }, "boneName": "Spine2" },
      { "slotName": "weapon_r", "mesh": { "bundleId": "weapons.synty", "assetId": "Sword_01" }, "boneName": "RightHand" }
    ],
    "anchorBoneName": null
  },
  "rig": {
    "name": "TopDown-55deg",
    "projection": "Orthographic",
    "frameSize": { "width": 96, "height": 96 },
    "padding": 2,
    "angles": [
      { "name": "N", "yaw": 0, "pitch": -55, "producesMirror": false },
      { "name": "NE", "yaw": 45, "pitch": -55, "producesMirror": true, "mirrorTargetName": "NW" },
      { "name": "E", "yaw": 90, "pitch": -55, "producesMirror": true, "mirrorTargetName": "W" },
      { "name": "SE", "yaw": 135, "pitch": -55, "producesMirror": true, "mirrorTargetName": "SW" },
      { "name": "S", "yaw": 180, "pitch": -55, "producesMirror": false }
    ]
  },
  "atlasWidth": 2048,
  "atlasHeight": 1536,
  "normalMapAtlas": "warrior_plate_sword_topdown_normals.png",
  "animations": [
    {
      "name": "idle",
      "loopMode": "Loop",
      "totalDuration": 1.0,
      "angleFrameMap": {
        "N":  [0, 1, 2, 3, 4, 5, 6, 7],
        "NE": [8, 9, 10, 11, 12, 13, 14, 15],
        "E":  [16, 17, 18, 19, 20, 21, 22, 23],
        "SE": [24, 25, 26, 27, 28, 29, 30, 31],
        "S":  [32, 33, 34, 35, 36, 37, 38, 39],
        "NW": [40, 41, 42, 43, 44, 45, 46, 47],
        "W":  [48, 49, 50, 51, 52, 53, 54, 55],
        "SW": [56, 57, 58, 59, 60, 61, 62, 63]
      }
    }
  ],
  "frames": [
    { "index": 0, "angle": "N", "animation": "idle", "frameInAnimation": 0,
      "rect": { "x": 0, "y": 0, "width": 96, "height": 96 },
      "pivot": { "x": 0.5, "y": 0.85 }, "duration": 0.125,
      "isMirror": false },
    { "index": 40, "angle": "NW", "animation": "idle", "frameInAnimation": 0,
      "rect": { "x": 0, "y": 0, "width": 96, "height": 96 },
      "pivot": { "x": 0.5, "y": 0.85 }, "duration": 0.125,
      "isMirror": true, "mirrorSourceIndex": 8 }
  ]
}
```

### Performance Targets

| Operation | Target | Notes |
|-----------|--------|-------|
| OrthographicSetup computation | < 1 μs | Pure math: bounds → ortho matrix |
| FrameSequence generation (uniform, 20 frames) | < 1 μs | Division arithmetic |
| AtlasPacker (1000 frames, 128×128) | < 10 ms | MaxRects with pre-sorted input |
| MirrorOptimizer (8-dir rig) | < 1 μs | Single pass over angles list |
| DepthToNormal (128×128 frame) | < 1 ms | Sobel 3×3 convolution |
| AtlasAssembler (1000 frames → 4096×4096) | < 100 ms | Pixel blit operations |
| SpriteSheet serialization to JSON | < 5 ms | System.Text.Json |
| SpriteSheet deserialization from JSON | < 5 ms | System.Text.Json |

### Determinism Contract

All operations are deterministic. Same inputs produce identical outputs:
- Same `CameraRig` + same `AtlasOptions` → same `AtlasLayout`
- Same pixel data + same `DepthToNormal` options → same normal map
- Same `SpriteSheet` → same JSON serialization (properties ordered consistently)

---

## Part 4: sprite-composer (Engine-Agnostic Core)

### Overview

sprite-composer is the composer-layer SDK for the sprite domain. It provides the interactive editor core: model/equipment configuration, camera rig management, animation browsing and configuration, capture session orchestration, atlas assembly, sprite preview with playback, project save/load, and command-based undo/redo. All engine-specific rendering, model loading, and frame capture operations are delegated to the `ISpriteComposerBridge`.

**Analogous to**: scene-composer (the reference composer implementation — bridge pattern, undo/redo, events), voxel-builder (headless mode, operation serialization, import/export).

### Package Identity

| Property | Value |
|----------|-------|
| Directory | `sdks/sprite-composer/` |
| PackageId | `BeyondImmersion.Bannou.SpriteComposer` |
| RootNamespace | `BeyondImmersion.Bannou.SpriteComposer` |
| AssemblyName | `BeyondImmersion.Bannou.SpriteComposer` |
| Target | `net8.0` |
| Dependencies | `sprite-theory` |

### Project Structure

```
sdks/sprite-composer/
├── Abstractions/
│   ├── ISpriteComposerBridge.cs      # Engine integration contract
│   ├── IModelHandle.cs               # Opaque handle to a loaded 3D model
│   └── IAnimationHandle.cs           # Opaque handle to an available animation
├── Capture/
│   ├── CaptureSession.cs             # Orchestrates full capture: model × rigs × animations × frames
│   ├── CaptureProgress.cs            # Progress reporting: current animation, angle, frame, % complete
│   ├── CaptureResult.cs              # Complete capture output: pixel data + metadata
│   └── FrameCapture.cs               # Single captured frame: RGBA pixels + depth + dimensions
├── Configuration/
│   ├── AnimationConfig.cs            # Per-animation settings: frame count, speed override, trim
│   ├── AnimationBrowser.cs           # Query available animations from the bridge, filter, configure
│   └── EquipmentManager.cs           # Manage equipment attachment slots
├── Preview/
│   ├── SpritePreview.cs              # Play back captured sprites with timing and angle selection
│   └── PreviewController.cs          # Play, pause, frame step, speed, angle switching
├── Project/
│   ├── SpriteProject.cs              # Complete project: variant definition + rig configs + animation configs
│   ├── ProjectSerializer.cs          # JSON save/load for projects
│   └── RecentProjects.cs             # MRU project list
├── Export/
│   ├── ExportPipeline.cs             # Assemble captures → atlas PNG + JSON metadata via sprite-theory
│   └── ExportOptions.cs              # Output directory, filename patterns, which rigs to export
├── Commands/
│   ├── IEditorCommand.cs             # Undo/redo command contract (from scene-composer pattern)
│   ├── CommandStack.cs               # Undo/redo stack with compound operations
│   ├── RigCommands.cs                # Add/remove/modify camera rig
│   ├── AnimationCommands.cs          # Change animation config (frame count, speed, etc.)
│   ├── EquipmentCommands.cs          # Attach/detach equipment
│   └── VariantCommands.cs            # Change model, scale, material
├── Events/
│   ├── ComposerEvents.cs             # ProjectLoaded, CaptureStarted, CaptureProgress, CaptureComplete
│   └── PreviewEvents.cs              # AnimationChanged, FrameChanged, AngleChanged
├── SpriteComposer.cs                 # Main orchestrator
└── sprite-composer.csproj
```

### The Bridge Contract

This is the critical design artifact — the boundary between engine-agnostic logic and engine-specific rendering:

```
ISpriteComposerBridge : IAsyncDisposable

  // ── Capability flags ──
  SupportsDepthCapture: bool
      True when the bridge can populate FrameCapture.DepthData. False → composer skips normal-map
      atlas generation even when rig.IncludeNormalMap = true (and logs a one-shot warning).

  SupportsSkeletonIntrospection: bool
      True when the bridge can resolve bone names to world positions via TryGetBonePosition.
      False → composer falls back to ComputeFromBounds when AnchorBoneName is set (and logs once).

  // ── Model lifecycle ──
  LoadModelAsync(model: AssetReference, ct) → IModelHandle
      Resolve the asset reference to an engine-specific handle and instantiate it in the scene.

  DisposeModel(handle: IModelHandle) → void
      Unload and clean up engine resources for a model.

  GetModelBounds(handle: IModelHandle) → BoundingBox
      Axis-aligned bounds in world space, in the model's current pose.

  SetModelScale(handle: IModelHandle, scale: float) → void
      Apply CharacterVariant.Scale before bounds are queried.

  // ── Skeleton introspection (conditional on SupportsSkeletonIntrospection) ──
  TryGetBonePosition(handle: IModelHandle, boneName: string) → Vector3?
      Return the named bone's current world position, or null when the bone is missing or
      the capability is unsupported. Used by pivot resolution when AnchorBoneName is set.

  // ── Equipment ──
  AttachEquipmentAsync(model: IModelHandle, slot: EquipmentSlot, ct) → IEquipmentHandle
      Resolve slot.Mesh (AssetReference), attach to slot.BoneName, return a handle.

  DetachEquipment(model: IModelHandle, handle: IEquipmentHandle) → void

  SetMaterialOverride(model: IModelHandle, materialSlot: string, material: AssetReference?) → void
      Override a material/palette. Null material restores the default.

  // ── Animation ──
  GetAvailableAnimations(handle: IModelHandle) → IReadOnlyList<AnimationInfo>
      Enumerate all animation clips available on the model's skeleton.
      Returns: name, duration, frame count, loop flag.

  SetAnimation(handle: IModelHandle, animationName: string) → void
      Set the active animation clip on the model.

  SetAnimationTime(handle: IModelHandle, normalizedTime: float) → void
      Seek the animation to a specific point (0.0 = start, 1.0 = end) and force-evaluate
      the skeleton pose without advancing the game loop.

  // ── Rig/Angle lifecycle (new) ──
  BeginRigAsync(rig: CameraRig, ct) → Task
  EndRigAsync(rig: CameraRig, ct) → Task
      Called once per rig at the boundaries of a rig's capture pass. Lets bridges allocate/
      release per-rig resources (render targets sized to rig.FrameSize, etc.).

  BeginAngleAsync(angle: CaptureAngle, ct) → Task
  EndAngleAsync(angle: CaptureAngle, ct) → Task
      Called once per angle within a rig. Lets bridges snapshot per-angle state.

  All four default to no-ops on SpriteComposerBridgeBase.

  // ── Camera ──
  ConfigureCamera(parameters: OrthographicParameters, frameSize: (int Width, int Height)) → void
      Apply the composer-computed camera basis to the engine's capture camera.
      The composer passes already-computed OrthographicParameters; the bridge does not re-compute.

  // ── Frame capture (variant + rig identity on the signature) ──
  CaptureFrameAsync(
      variantName: string,
      rigName: string,
      angleName: string,
      animationName: string,
      frameIndex: int,
      normalizedTime: float,
      captureDepth: bool,
      ct) → FrameCapture
      Render the current scene to an off-screen target and return the pixel data tagged with
      the full 5-tuple identity. captureDepth is composer-resolved as
      rig.IncludeNormalMap AND SupportsDepthCapture — the bridge may return DepthData=null
      if a frame-specific failure prevents depth capture, and this is non-fatal.

  // ── Preview (interactive 3D) ──
  SetPreviewCamera(yaw: float, pitch: float, distance: float) → void
  SetPreviewAnimationPlayback(playing: bool, normalizedSpeed: float) → void
```

**Key design decisions**:

1. **Opaque handles**: `IModelHandle` and `IEquipmentHandle` are marker interfaces. The bridge implementation defines the concrete type (e.g., `StrideModelHandle` wrapping a Stride `Entity`). The composer never touches engine types.

2. **AssetReference instead of string paths** (Decision 10.10): models, equipment meshes, and material overrides are referenced by `AssetReference(BundleId, AssetId, VariantId?)` — the same engine-agnostic identifier scene-composer uses. The bridge resolves references to engine-specific handles at load time; the composer never constructs paths.

3. **Normalized animation time**: `SetAnimationTime(0.0 - 1.0)` instead of absolute seconds. This abstracts away animation duration — the composer computes normalized time from `FrameSequence` timestamps.

4. **Pre-computed camera parameters** (Decision 10.11): the composer computes `OrthographicParameters` via `OrthographicSetup.Compute` and passes the result to the bridge. The bridge applies the parameters to the engine's camera; it never re-computes. This keeps the math in sprite-theory and makes the per-angle pivot (Decision 10.9) trivially correct — the same `OrthographicParameters` feeds both camera configuration and pivot projection.

5. **Capability flags with graceful degradation** (Decision 10.8): `SupportsDepthCapture` and `SupportsSkeletonIntrospection` advertise optional bridge features. The composer inspects them before requesting depth capture or bone lookups and degrades gracefully (no normal-map atlas, fall back to bounds-based pivot) with a single warning per capture session.

6. **Rig/Angle lifecycle hooks** (Decision 10.8): four async hooks (`BeginRigAsync`/`EndRigAsync`/`BeginAngleAsync`/`EndAngleAsync`) wrap the capture loop boundaries. Default no-op implementations mean bridges only override them when they need per-rig or per-angle setup (sized render targets, per-angle post-processing state). The composer calls them whether or not the bridge overrides them.

7. **FrameCapture carries the full identity** (Decision 10.7): the 5-tuple (`VariantName`, `RigName`, `AngleName`, `AnimationName`, `FrameIndex`) travels with every capture. Downstream assembly (atlas layout, mirror generation, sprite-sheet grouping by `(Variant, Rig)`) depends on this identity, and it's the bridge's responsibility to stamp it correctly via the `CaptureFrameAsync` parameters the composer supplies.

8. **FrameCapture is otherwise engine-agnostic**: Raw RGBA byte array + optional depth float array + dimensions + identity strings + normalized time. No engine types cross the bridge boundary.

### Project Management

A `SpriteProject` is a **multi-variant container** (Decision 10.12) — a single project holds every variant for a character family that shares rigs and animations. Defenders' 50–80 total variants (heroes + troop specs × tiers + enemies + bosses + NPCs) collapse to ~4 project files, one per class of content (heroes, troops, enemies, bosses), instead of 50–80 single-variant projects.

```
SpriteProject
├── Name: string                                                       # "Heroes", "Troops", "Enemies", "Bosses"
├── Rigs: IReadOnlyList<CameraRig>                                     # Camera rigs shared by all variants
├── AnimationSets: IReadOnlyDictionary<string, AnimationSet>           # Named reusable animation configurations
├── Variants: IReadOnlyList<VariantBinding>                            # One entry per character variant
├── ExportOptions: ExportOptions                                       # Output paths, filenames, IAtlasEncoder (§ Part 10)
├── NormalMapOptions: NormalMapOptions?                                # Normal map generation settings
├── CustomProperties: IReadOnlyDictionary<string, string>?             # Game-specific metadata to embed in output
└── SchemaVersion: string                                              # "1.0"

AnimationSet
├── Name: string                                                       # "humanoid-combat", "ranged-attacker", "quadruped"
├── SelectedAnimationNames: IReadOnlyList<string>                      # Which bridge-reported animations to capture
├── AnimationConfigs: IReadOnlyDictionary<string, AnimationConfig>     # Per-animation overrides within the set
└── DefaultAnimationConfig: AnimationConfig                            # Defaults for unconfigured animations

VariantBinding
├── Variant: CharacterVariant                                          # Model, equipment, pivot, anchor-bone (§ Part 3)
├── AnimationSetName: string                                           # Which AnimationSet this variant uses
├── AnimationOverrides: IReadOnlyDictionary<string, AnimationConfig>?  # Per-variant AnimationConfig overrides
└── ExcludedAnimations: IReadOnlyList<string>?                         # Animations in the set to skip for this variant
```

**How AnimationSets eliminate duplication**: All nine warrior-tier troops (warrior-bronze/iron/steel for the infantry line, etc.) share the same animation set. Instead of 9 `AnimationConfigs` dictionaries (one per variant), the project holds one `AnimationSet` named `humanoid-combat` and binds all 9 variants to it. When the set needs a tweak (add an animation, change a frame count), one edit affects every bound variant.

**How VariantBinding scopes the overrides**: A specific warrior variant might have an unusually long wind-up animation that needs different trim values than the shared default. `binding.AnimationOverrides["attack_heavy"]` overrides just that entry without forking the whole set. A boss that shares the general humanoid combat set but has no ranged attack sets `ExcludedAnimations = ["shoot_bow"]` without forking.

Projects serialize to JSON and can be shared across team members or stored in version control. This is the input format that SpriteBatcher (§ Part 6 and § Phase 3.5) consumes — a single `.spriteproj.json` file can drive every capture for a full class of content.

### Capture Session Pipeline

The `CaptureSession` orchestrates a per-variant capture across every rig × angle × animation × frame nesting:

```
CaptureSession.ExecuteAsync(project, bridge, progress, ct)

  capturedFrames = []

  FOREACH variantBinding IN project.Variants:
    variant = variantBinding.Variant
    set = project.AnimationSets[variantBinding.AnimationSetName]
    effectiveAnimations = set.SelectedAnimationNames.Except(variantBinding.ExcludedAnimations ?? [])
    effectiveConfigs = set.AnimationConfigs overlay variantBinding.AnimationOverrides

    handle = await bridge.LoadModelAsync(variant.Model, ct)
    bridge.SetModelScale(handle, variant.Scale)
    FOREACH slot IN variant.Equipment:
      bridge.AttachEquipmentAsync(handle, slot, ct)
    FOREACH (materialSlot, materialRef) IN variant.MaterialOverrides ?? {}:
      bridge.SetMaterialOverride(handle, materialSlot, materialRef)

    modelBounds = bridge.GetModelBounds(handle)

    FOREACH rig IN project.Rigs:
      await bridge.BeginRigAsync(rig, ct)

      FOREACH angle IN rig.Angles:                              # Every angle IS rendered (§ Decision 10.1)
        await bridge.BeginAngleAsync(angle, ct)

        // Per-angle OrthographicParameters (§ Decision 10.11)
        orthoParams = OrthographicSetup.Compute(angle, modelBounds, rig.FrameSize)

        // Per-angle pivot (§ Decision 10.9) — MUST be computed here, not per-rig
        pivot = ResolvePivot(variant, bridge, handle, modelBounds, orthoParams)

        bridge.ConfigureCamera(orthoParams, rig.FrameSize)

        FOREACH animName IN effectiveAnimations (sorted by name for determinism):
          animInfo = bridge.GetAvailableAnimations(handle).First(a => a.Name == animName)
          animConfig = effectiveConfigs.GetValueOrDefault(animName, set.DefaultAnimationConfig)
          sequence = AnimationSampling.GenerateFromConfig(animInfo, animConfig)
          bridge.SetAnimation(handle, animName)

          FOREACH (normalizedTime, frameIdx) IN sequence.Timestamps.Zip(0..):
            bridge.SetAnimationTime(handle, normalizedTime)
            capture = await bridge.CaptureFrameAsync(
              variantName: variant.Name,
              rigName: rig.Name,
              angleName: angle.Name,
              animationName: animName,
              frameIndex: frameIdx,
              normalizedTime: normalizedTime,
              captureDepth: rig.IncludeNormalMap AND bridge.SupportsDepthCapture,
              ct: ct)
            capturedFrames.Add((capture, pivot))
            progress.Report(current++, total)

        await bridge.EndAngleAsync(angle, ct)

      await bridge.EndRigAsync(rig, ct)

    FOREACH slot IN variant.Equipment:  bridge.DetachEquipment(handle, slot)
    bridge.DisposeModel(handle)

  // Assembly phase — one SpriteSheet per (variant, rig) grouping
  FOREACH (variantName, rigName) IN capturedFrames.GroupKeys:
    subset = capturedFrames.Where(f => f.VariantName == variantName AND f.RigName == rigName)
    atlasLayout = AtlasPacker.Pack(subset, atlasOptions)
    mirrorInfo = MirrorOptimizer.ComputeMirrors(rig)
    mirrorFrames = MirrorOptimizer.GenerateMirrorFrames(baseFrames, mirrorInfo)
    spriteSheet = SpriteSheet.Build(atlasLayout, mirrorFrames, variant, rig)
    await ExportPipeline.Export(spriteSheet, subset, exportOptions)

  progress.OnExportCompleted()


FUNCTION ResolvePivot(variant, bridge, handle, bounds, orthoParams) → Vector2:
  // § Part 3 CharacterVariant resolution order (§ Decision 10.9)
  IF variant.PivotOverride is not null:
    RETURN variant.PivotOverride.Value
  IF variant.AnchorBoneName is not null AND bridge.SupportsSkeletonIntrospection:
    bonePos = bridge.TryGetBonePosition(handle, variant.AnchorBoneName)
    IF bonePos is not null:
      RETURN PivotComputer.ProjectWorldPointToFrame(bonePos.Value, orthoParams)
  RETURN PivotComputer.ComputeFromBounds(bounds, orthoParams)
```

**Frame count estimation** (for progress reporting):

```
totalFrames = Σ_variants Σ_rigs (rig.Angles.Count × Σ_animations animationConfig.FrameCount)
```

For a Defenders heroes project with 4 variants × 2 rigs (side-view: 1 angle, top-down: 5 angles) × 20 animations × 8 frames each:

| Component | Captured frames |
|---|---|
| Per variant side-view | 1 × 20 × 8 = 160 |
| Per variant top-down | 5 × 20 × 8 = 800 |
| Per variant total | 960 |
| 4 variants × 960 | **3,840 frames** |
| At ~50ms per frame (render + readback) | ~3.2 minutes for the heroes project |

**Output grouping**: one `SpriteSheet` per `(VariantName, RigName)` — a heroes project with 4 variants × 2 rigs produces 8 sprite sheets (8 atlases + 8 JSON metadata files). `ExportOptions.AtlasFilenamePattern` defaults to `"{variant}_{rig}_{atlas}.png"` and must support both `{variant}` and `{rig}` placeholders.

### Command-Based Undo/Redo

Following the scene-composer pattern, all project modifications go through the command stack. Commands are partitioned by the scope they target — project-level (rigs, animation sets, variant list) vs variant-level (per-variant model/equipment/pivot/anchor):

**Project-level commands** (operate on the multi-variant `SpriteProject` container):

| Command | What It Does | Undo |
|---------|-------------|------|
| `AddRigCommand` | Add a camera rig to the project (affects every variant) | Remove the rig |
| `RemoveRigCommand` | Remove a camera rig | Re-add the rig at its original position |
| `ModifyRigCommand` | Change rig parameters (frame size, padding, etc.) | Restore previous parameters |
| `AddAnimationSetCommand` | Add a named `AnimationSet` to `project.AnimationSets` | Remove the set (fails if any variant binds to it) |
| `RemoveAnimationSetCommand` | Remove a named `AnimationSet` | Re-add the set (fails if any variant now binds to a deleted set) |
| `ModifyAnimationSetCommand` | Change an AnimationSet's selected animations or configs | Restore previous set state |
| `AddVariantCommand` | Add a `VariantBinding` to `project.Variants` | Remove the binding |
| `RemoveVariantCommand` | Remove a `VariantBinding` | Re-add the binding at its original position |

**Variant-level commands** (operate on a single `VariantBinding` or its `CharacterVariant`):

| Command | What It Does | Undo |
|---------|-------------|------|
| `SetVariantModelCommand(variantName, newModel)` | Change a variant's `Model: AssetReference` | Restore previous AssetReference; reload via bridge |
| `SetVariantScaleCommand(variantName, newScale)` | Change variant scale | Restore previous scale |
| `SetVariantPivotOverrideCommand(variantName, pivot?)` | Set or clear `PivotOverride` | Restore previous override state |
| `SetVariantAnchorBoneNameCommand(variantName, boneName?)` | Set or clear `AnchorBoneName` | Restore previous bone name |
| `AttachEquipmentCommand(variantName, slot)` | Add equipment to a variant's slot | Remove equipment, call bridge.Detach |
| `DetachEquipmentCommand(variantName, slotName)` | Remove equipment from a variant's slot | Re-attach, call bridge.Attach |
| `BindVariantToAnimationSetCommand(variantName, setName)` | Change which `AnimationSet` a variant binds to | Restore previous binding |
| `SetVariantAnimationOverrideCommand(variantName, animName, config?)` | Set or clear a per-variant override on a specific animation | Restore previous override |
| `SetVariantExcludedAnimationsCommand(variantName, names)` | Change the variant's exclusion list | Restore previous list |

Undo/redo applies to **configuration**, not to captured frames. Capturing is a one-way operation (you can re-capture, but you don't "undo a capture").

### Events

```csharp
// Project lifecycle
event EventHandler<ProjectEventArgs> ProjectLoaded;
event EventHandler<ProjectEventArgs> ProjectSaved;
event EventHandler<DirtyStateEventArgs> DirtyStateChanged;

// Capture lifecycle
event EventHandler<CaptureStartedEventArgs> CaptureStarted;
event EventHandler<CaptureProgressEventArgs> CaptureProgress;    // Periodic updates
event EventHandler<CaptureCompleteEventArgs> CaptureCompleted;
event EventHandler<ExportCompletedEventArgs> ExportCompleted;    // After every (variant, rig) atlas + JSON written
event EventHandler<CaptureErrorEventArgs> CaptureError;

// Preview
event EventHandler<AnimationChangedEventArgs> PreviewAnimationChanged;
event EventHandler<FrameChangedEventArgs> PreviewFrameChanged;
event EventHandler<AngleChangedEventArgs> PreviewAngleChanged;

// Undo/redo
event EventHandler<UndoRedoStateEventArgs> UndoRedoStateChanged;
```

### Headless Mode

When `bridge` is null, the composer operates headlessly — project management, configuration, and metadata operations work, but no rendering or capture occurs. This enables:

- **Batch processing** (future SpriteBatcher): read project files, modify configurations programmatically, export metadata without engine rendering
- **CI/CD integration**: validate project files, compute expected frame counts, generate capture manifests
- **Testing**: unit test all composer logic without engine dependencies

---

## Part 5: sprite-composer-stride (Stride Engine Bridge)

### Overview

sprite-composer-stride is the Stride engine bridge for the sprite domain. It implements `ISpriteComposerBridge` using Stride's rendering, animation, and content systems. As the primary (and initially only) engine bridge, it is optimized for the Synty FBX workflow and Stride's specific APIs.

**Analogous to**: scene-composer-stride (entity management, asset loading, type conversion), voxel-builder-stride (render target management, buffer operations, constructor patterns).

### Package Identity

| Property | Value |
|----------|-------|
| Directory | `sdks/sprite-composer-stride/` |
| PackageId | `BeyondImmersion.Bannou.SpriteComposer.Stride` |
| RootNamespace | `BeyondImmersion.Bannou.SpriteComposer.Stride` |
| AssemblyName | `BeyondImmersion.Bannou.SpriteComposer.Stride` |
| Target | `net10.0` (conditional `-windows` TFM on Windows, plain on Linux for CI) |
| Dependencies | `sprite-composer` (→ `sprite-theory`), `Stride.Engine 4.3`, `Stride.Rendering`, `Stride.Animations` |

### Project Structure

```
sdks/sprite-composer-stride/
├── StrideSpriteComposerBridge.cs     # ISpriteComposerBridge implementation
├── StrideModelLoader.cs              # FBX → Entity with skeleton + AnimationComponent
├── StrideEquipmentAttacher.cs        # Equipment mesh → bone parenting
├── StrideAnimationController.cs      # AnimationComponent wrapper: play, seek, step
├── StrideCaptureCamera.cs            # Orthographic camera management, CaptureAngle → camera transform
├── StrideFrameCapturer.cs            # RenderTexture creation, render, pixel readback
├── StrideDepthCapturer.cs            # Depth buffer extraction from render target
├── StridePreviewCamera.cs            # Interactive orbit camera for 3D preview
├── StrideTypeConverter.cs            # SDK ↔ Stride type conversion (using-alias pattern)
├── Handles/
│   ├── StrideModelHandle.cs          # IModelHandle wrapping Entity + AnimationComponent
│   └── StrideEquipmentHandle.cs      # IEquipmentHandle wrapping child Entity
└── sprite-composer-stride.csproj
```

### Constructor Pattern

Following voxel-builder-stride and scene-composer-stride:

```csharp
public StrideSpriteComposerBridge(
    Scene scene,                          // Stride scene to manage entities in
    GraphicsDevice graphicsDevice,        // For render target creation
    Func<CommandList> commandListProvider, // For GPU readback operations
    IContentManager? contentManager = null // Optional: for loading compiled Stride assets
)
```

**Why `IContentManager`?** Stride compiles FBX models into its internal format during the build pipeline. At runtime, `ContentManager.Load<Model>("path")` loads the compiled asset. For the sprite composer, we need to load raw FBX files that haven't been compiled — this requires either Stride's import pipeline or a third-party FBX loader. The `contentManager` parameter supports the compiled-asset path (when working within a Stride Game Studio project), while raw FBX loading is handled by `StrideModelLoader` for the standalone tool case.

### FBX Model Loading

Synty FBX files contain:
- **Mesh data**: Vertices, normals, UVs, vertex colors
- **Skeleton**: Bone hierarchy with bind poses
- **Animation clips**: Keyframed bone transforms (often in separate FBX files)

Stride's content pipeline compiles FBX into `.sdmodel`, `.sdanim`, and `.sdtex` files. For the sprite composer:

**Option A: Stride Game Studio Project** (recommended for Defenders)
- Import FBX files into a Stride project via Game Studio
- The composer loads compiled assets via `ContentManager.Load<Model>()`
- Animations loaded via `ContentManager.Load<AnimationClip>()`
- Equipment meshes are separate models loaded the same way
- Bone attachment via `entity.Transform.Parent = parentEntity.Transform` with the target bone's transform

**Option B: Raw FBX Loading** (future, for standalone tool)
- Use Stride's `Assimp` import pipeline programmatically
- Convert to runtime `Model`, `AnimationClip`, `Skeleton` objects
- More complex but enables the standalone editor without Game Studio

For the initial implementation, **Option A is correct** — Defenders will have a Stride Game Studio project where Synty FBX assets are imported through the normal pipeline. The sprite composer loads from that project's compiled assets.

### Animation System Integration

From stride-demo's `MonsterAnimationController`, the proven Stride animation API:

```csharp
// Load animation clip (compiled from FBX)
var clip = Content.Load<AnimationClip>("Animations/warrior_idle");

// Add to AnimationComponent
var animComponent = entity.GetOrCreate<AnimationComponent>();
animComponent.Animations["idle"] = clip;

// Play
var playing = animComponent.Play("idle");
playing.RepeatMode = AnimationRepeatMode.PlayOnce;

// Seek to specific time (for frame capture)
playing.CurrentTime = TimeSpan.FromSeconds(normalizedTime * clip.Duration.TotalSeconds);

// For sprite capture: we need the animation to evaluate at this exact time
// without advancing — this requires setting the time and forcing an evaluation
```

**Critical detail for frame capture**: Stride's animation system normally advances time each frame during the game loop. For sprite capture, we need to **set an exact time and evaluate once** without the game loop advancing. The approach:

1. Set `playing.CurrentTime` to the desired frame time
2. Force the animation system to evaluate (update the entity transforms)
3. Render the scene to the capture target
4. Read back the pixel data

This may require calling Stride's animation evaluation manually or running a single simulation step. Investigation needed: does `AnimationUpdater.Update()` or `AnimationProcessor` provide a way to force-evaluate without game loop time advancement?

### Render-to-Texture Pipeline

The core of the bridge — rendering the scene to an off-screen target:

```
CaptureFrameAsync(ct) → FrameCapture

  1. Ensure render target exists at rig.FrameSize
     renderTexture = Texture.New2D(graphicsDevice, width, height,
         PixelFormat.R8G8B8A8_UNorm, TextureFlags.RenderTarget | TextureFlags.ShaderResource)

  2. Clear render target with background color (transparent)
     commandList.Clear(renderTexture, backgroundColor)

  3. Render scene to target
     // Option A: Use Stride's SceneRenderer pipeline directed at our render target
     // Option B: Use a custom RenderFrame with our camera + render target
     // This needs investigation — Stride's rendering pipeline is sophisticated

  4. Read pixels back from GPU
     var stagingTexture = Texture.New2D(graphicsDevice, width, height,
         PixelFormat.R8G8B8A8_UNorm, TextureFlags.None, usage: GraphicsResourceUsage.Staging)
     commandList.CopyResource(renderTexture, stagingTexture)
     var pixelData = stagingTexture.GetData<byte>(commandList)

  5. If depth capture enabled, repeat for depth buffer
     // Stride writes depth to the depth-stencil buffer
     // Reading depth back requires a depth-stencil texture with staging copy

  6. Return FrameCapture(pixelData, depthData, width, height)
```

**GPU readback latency**: Reading pixels from GPU to CPU is the slowest part of the pipeline. For batch capture (not interactive preview), we can pipeline: start rendering frame N+1 while reading back frame N. Expected per-frame cost: ~30-50ms (render) + ~10-20ms (readback) = ~40-70ms per frame. For 960 frames: **~40-70 seconds per character variant**.

### Equipment Bone Attachment

Synty's modular equipment system uses skeleton bone parenting:

```csharp
// Load equipment model
var swordModel = Content.Load<Model>("Equipment/Sword_01");

// Create equipment entity
var swordEntity = new Entity("weapon_r");
swordEntity.GetOrCreate<ModelComponent>().Model = swordModel;

// Find the bone transform on the character skeleton
// Stride exposes skeleton bones as child transforms when using ModelNodeLinkComponent
var nodeLink = new ModelNodeLinkComponent { NodeName = "RightHand" };
swordEntity.Add(nodeLink);

// Parent to character entity — the ModelNodeLinkComponent handles bone following
characterEntity.AddChild(swordEntity);
```

`ModelNodeLinkComponent` is Stride's mechanism for attaching entities to skeleton bones — it automatically updates the child entity's transform to follow the named bone during animation. This is exactly what the equipment attachment bridge method needs to use.

### Established Patterns (from scene-composer-stride / voxel-builder-stride)

| Pattern | Source | How We Apply It |
|---------|--------|----------------|
| Constructor params | `(Scene, CameraComponent, GraphicsDevice)` | Same + `Func<CommandList>` + optional `IContentManager` |
| Type disambiguation | `using SdkVec3 = ...; using StrideVec3 = ...;` aliases | Same pattern for BoundingBox, Vector3, Color |
| Entity management | `Dictionary<Guid, Entity>` | `StrideModelHandle` wraps Entity + its child equipment entities |
| Coordinate system | Stride = right-handed, Y-up, CCW front-face | Same as SDK math types — no conversion needed |
| Disposal | Entity removal from scene, GPU resource disposal | Render targets, staging textures, model entities |

---

## Part 6: Future — SpriteBatcher

The SpriteBatcher is the automation layer that removes the interactive editor from the loop. It is documented here for architectural completeness and to ensure the sprite-composer API surface supports it from day one, but it is **not in scope for the initial implementation**.

### Concept

SpriteBatcher takes a batch definition (YAML or JSON) and produces all sprite sheets without user interaction:

```yaml
# sprite-batch.yaml
output_directory: ./sprites/
characters:
  - name: warrior
    model: Characters/Warrior_Base
    variants:
      - name: plate_sword
        equipment:
          chest: Equipment/Plate_Chest
          weapon_r: Weapons/Sword_01
      - name: leather_bow
        equipment:
          chest: Equipment/Leather_Chest
          weapon_r: Weapons/Bow_01
    rigs:
      - preset: SideViewBrawler
        frame_size: [128, 128]
        normal_maps: true
      - preset: TopDown8Dir
        pitch: -55
        frame_size: [96, 96]
        normal_maps: true
    animations:
      - name: idle
        frames: 8
        loop: true
      - name: attack_light
        frames: 12
        loop: false
      - name: attack_heavy
        frames: 16
        loop: false
      # ... all animations
    default_animation:
      frames: 8
```

### Execution Model

```
SpriteBatcher reads batch definition
  → FOR EACH character × variant:
      → Create SpriteProject from batch definition
      → Initialize bridge (Stride headless or GPU-enabled)
      → Run CaptureSession.ExecuteAsync (same as interactive editor)
      → Export atlas + metadata
      → Dispose bridge resources
  → Report: characters processed, frames captured, atlas sizes, errors
```

### CI/CD Integration

```bash
# Generate all sprites from batch definition
dotnet run --project tools/SpriteBatcher -- \
    --input sprite-batch.yaml \
    --output ./assets/sprites/ \
    --parallel 2  # Two characters simultaneously (GPU memory permitting)
```

### What sprite-composer Must Support for This

The SpriteBatcher consumes sprite-composer's API surface. These design decisions in the composer enable batch automation:

1. **SpriteProject is serializable** (JSON) — batch definitions map to project files
2. **CaptureSession is independent** — doesn't require UI, just a bridge and a project
3. **Headless mode works** for metadata operations (validation, manifest generation)
4. **CameraRigPresets are code-accessible** — batch definitions reference presets by name
5. **ExportPipeline takes a directory** — batch processing writes to configurable paths
6. **Progress reporting uses events** — batch tool can log to console instead of UI

---

## Part 7: Implementation Priority

### Phase Summary

| Phase | Deliverable | Dependencies | Status |
|---|---|---|---|
| 1 | `sprite-theory` SDK (pure computation) | None | ✅ Complete (including Apr 19 remediation + F2 fix, 100 tests) |
| **1.5** | **Stride capture spike** (throwaway validation) | None | **2–3 days before Phase 2** |
| 2 | `sprite-composer` SDK (engine-agnostic core) | sprite-theory, Phase 1.5 validation | Pending |
| 3 | `sprite-composer-stride` bridge | sprite-composer | Pending |
| **3.5** | **SpriteBatcher CLI tool** | sprite-composer + any bridge | **Alongside / just after Phase 3** |
| 4 | `defenders-sprite-composer` Stride project (interactive UI) | All above | Pending |
| 5+ | asset-bundler sprite handler, asset-loader sprite integration | Phase 3 outputs | Future |

### Phase 1: sprite-theory (Complete)

Pure computation. Depends on `BeyondImmersion.Bannou.Core` (for `Vector3`). Fully unit-testable.

**Deliverables**:
1. `CameraRig`, `CaptureAngle`, `CameraRigPresets` — rig definitions with Defenders-specific presets
2. `OrthographicSetup` — compute ortho camera parameters from character bounds + angle
3. `AnimationSampling`, `FrameSequence` — uniform and keyframe-aligned frame timing
4. `AtlasPacker` with MaxRects-BSSF — bin-packing with configurable options
5. `MultiAtlasStrategy` — overflow handling when frames exceed a single atlas's MaxSize
6. `MirrorOptimizer` — compute which angles produce mirrors, generate derived frame metadata
7. `PivotComputer` — `ProjectWorldPointToFrame` (general primitive) + `ComputeFromBounds` (feet wrapper)
8. `SpriteSheet`, `SpriteFrame`, `SpriteAnimation`, `CharacterVariant`, `EquipmentSlot`, `AssetReference`, `FrameCapture` — output/input metadata types
9. `SpriteSheetSerializer` — JSON serialization/deserialization of the metadata schema
10. `DepthToNormal` — Sobel-based normal map generation from depth data
11. `IPixelSource`, `IDepthSource` — engine-agnostic raw data abstractions
12. `AtlasAssembler` — compose captured frames into atlas RGBA byte arrays (PNG encoding is the consumer's responsibility — Decision 10.2)

**Directory**: `sdks/sprite-theory/` + `sdks/sprite-theory.tests/`

**Status**: Phase 1 implemented 2026-04-15 (87 tests), with Apr 19 remediation landing F2 fix + AssetReference adoption + FrameCapture identity + AnchorBoneName + `ProjectWorldPointToFrame` + Vector3 migration. **100 unit tests**, 0 warnings, 0 errors. See `docs/sdks/SPRITE-THEORY.md` for the deep dive and `docs/sdks/maps/SPRITE-THEORY.md` for the method-level map.

### Phase 1.5: Stride Capture Spike (NEW — 2–3 days before Phase 2)

A **throwaway Stride project** that validates the three capture primitives the Phase 2 bridge contract depends on. The spike is discarded after validation; the findings feed into Phase 2 (and may require bridge-contract adjustments before the contract is frozen).

**Deliverables**:

1. **Animation time-set without game-loop advance**: Can `PlayingAnimation.CurrentTime = X` + force-evaluate produce a deterministic pose without the update loop advancing time? Investigate `AnimationProcessor.Update(gameTime)` and direct skeleton manipulation.
2. **Render-to-texture with CPU readback**: Render a scene to a `Texture` with `TextureFlags.RenderTarget | ShaderResource`, copy to a staging texture with `GraphicsResourceUsage.Staging`, `GetData<byte>()` back to CPU. Verify pixel data matches the visible rendering.
3. **Depth buffer readback**: Same as above but for depth. `PixelFormat.D32_Float` staging texture. Verify depth values are in a known normalized range or document how to normalize.

**Output**: a short write-up in `docs/planning/SPRITE-STRIDE-SPIKE-RESULTS.md` documenting what worked, what didn't, and any bridge-contract adjustments needed before Phase 2.

**Why before Phase 2**: Phase 2 freezes `ISpriteComposerBridge` including depth-capture support, render-target lifecycle, and animation time-set semantics. If any of those primitives turn out to require different ergonomics than the current contract assumes (e.g., depth readback requires an explicit separate render target, animation evaluation requires a full simulation step rather than an isolated force-evaluate), the contract needs to reflect that before sprite-composer builds on top of it. Discovering this in Phase 3 would invalidate Phase 2 work.

### Phase 2: sprite-composer (Week 2-3)

Engine-agnostic orchestration. Depends on sprite-theory.

**Deliverables**:
1. `ISpriteComposerBridge` — the complete bridge contract
2. `IModelHandle`, `IEquipmentHandle` — opaque handle interfaces
3. `CaptureSession` — full capture orchestration pipeline
4. `SpriteProject`, `ProjectSerializer` — project save/load
5. `EquipmentManager` — equipment slot management
6. `AnimationBrowser`, `AnimationConfig` — animation discovery and configuration
7. `ExportPipeline` — atlas assembly + JSON metadata export using sprite-theory
8. `CommandStack` and all command types — undo/redo for configuration changes
9. `SpritePreview`, `PreviewController` — captured sprite playback
10. All events (`ComposerEvents`, `PreviewEvents`)
11. `SpriteComposer` orchestrator — main entry point tying everything together
12. Unit tests (with mock bridge)

**Directory**: `sdks/sprite-composer/` + `sdks/sprite-composer.tests/`

### Phase 3: sprite-composer-stride (Week 3-4)

Stride engine bridge. Depends on sprite-composer and Stride 4.3.

**Deliverables**:
1. `StrideSpriteComposerBridge` — complete ISpriteComposerBridge implementation
2. `StrideModelLoader` — load compiled Stride models (FBX via Game Studio import)
3. `StrideEquipmentAttacher` — `ModelNodeLinkComponent` bone attachment
4. `StrideAnimationController` — clip loading, play, seek, time-set
5. `StrideCaptureCamera` — orthographic camera configuration from CaptureAngle
6. `StrideFrameCapturer` — render target creation, scene render, pixel readback
7. `StrideDepthCapturer` — depth buffer extraction for normal maps
8. `StridePreviewCamera` — interactive orbit camera
9. `StrideTypeConverter` — using-alias type conversion
10. Handle implementations (`StrideModelHandle`, `StrideEquipmentHandle`)
11. Integration test: load a Synty FBX, capture frames, verify output

**Directory**: `sdks/sprite-composer-stride/` + `sdks/sprite-composer-stride.tests/`

### Phase 3.5: SpriteBatcher CLI Tool (NEW — Alongside / Just After Phase 3)

A headless CLI tool that drives `sprite-composer` + any bridge to execute captures without UI. Consumer of the same `SpriteProject` files the interactive editor uses.

**Deliverables**:

1. `tools/sprite-batcher/` CLI project with a stable command-line surface (`--projects`, `--output`, `--parallel`, `--bridge`, `--asset-bundles`, `--filter-variants`, `--filter-rigs`, `--dry-run`, `--verbose`).
2. Per-project × per-variant capture orchestration reusing `sprite-composer`'s `CaptureSession` verbatim — no separate capture logic.
3. Per-item error isolation (one corrupt variant must not abort the full batch) with a summary report.
4. `--dry-run` mode that computes `CaptureManifest` for each project and prints expected totals without invoking the bridge.
5. Exit codes meaningful for CI (0 = all projects succeeded, non-zero = any failed).

**Full design**: see `docs/planning/SPRITE-BATCHER.md` for the complete CLI surface, parallelism constraints, error handling, and CI integration.

**Why a standalone phase, not bundled into Phase 2 or 3**: The batcher is a *consumer* of sprite-composer, not part of its core. It lands after the composer's capture contract is stable (Phase 2) and after at least one bridge implementation exists (Phase 3) so the CLI has something to call. A SpriteBatcher bundled into Phase 2 would force premature decisions about bridge bootstrapping; landed alongside Phase 3, it demonstrates that the same capture pipeline serves both the interactive editor and CI batch processing.

### Phase 4: Defenders Composer Project (Week 4+)

A standalone Stride project that hosts the sprite composer UI:

```
defenders-sprite-composer/
├── DefendersSpriteComposer.sln
├── DefendersSpriteComposer.Game/
│   ├── ComposerScript.cs             # Main script: initializes bridge, composer, UI
│   ├── UI/
│   │   ├── ModelBrowserPanel.cs      # Browse available character models
│   │   ├── EquipmentPanel.cs         # Attach/detach equipment
│   │   ├── AnimationPanel.cs         # Browse/configure animations
│   │   ├── RigPanel.cs              # Camera rig configuration
│   │   ├── CapturePanel.cs           # Start capture, show progress
│   │   ├── PreviewPanel.cs           # Sprite playback preview
│   │   └── ExportPanel.cs            # Export configuration
│   └── Assets/                       # Compiled Synty assets (imported via Game Studio)
└── DefendersSpriteComposer.Windows/  # Windows launcher
```

This follows the same pattern as a scene-composer project — a Stride game that exists solely as an editor tool. Content creators open it, import models, configure captures, and export sprite sheets.

---

## Part 8: Open Questions

### Resolved

1. **Side-view directions** → Left/Right only (capture right, mirror left). Dead Cells style.
2. **Top-down directions** → 8-directional at 55° pitch. 5 captures + 3 mirrors. Hades style.
3. **Output format** → Generic JSON + PNG. Engine-agnostic. sprite-theory defines the schema.
4. **Scope** → Editor first, SpriteBatcher documented as future (Phase 2 in project lifecycle).
5. **Normal maps** → Yes, via depth buffer capture. Dead Cells technique for 2D dynamic lighting.
6. **Pivot point determination** (2026-04-15) → Auto-compute from bounding box with per-variant override. Implemented via `PivotComputer.ComputeFromBounds` (sprite-theory) and `CharacterVariant.PivotOverride`. Default fallback remains `(0.5, 0.85)` as a terminal when the camera basis is degenerate. See § Part 10 Resolved Decisions.

### Requires Investigation

1. **Stride animation time-setting**: Can we set `PlayingAnimation.CurrentTime` and force-evaluate the skeleton without running the game loop? If not, what's the workaround? The `AnimationProcessor` updates during the scene update step — we may need to trigger a single update step per frame capture. **Investigation path**: `Stride.Animations.AnimationProcessor`, `AnimationUpdater`, `AnimationBlender`.

2. **Stride render-to-texture pipeline**: How to render a single camera to an off-screen target? Options: (a) custom `SceneRendererCollection` with our camera, (b) `RenderFrame` API, (c) low-level `RenderContext` manipulation. Scene-composer-stride doesn't render to texture (it renders interactively), so this is new territory. **Investigation path**: Stride samples for render-to-texture, `RenderFrame`, `SceneRenderer`.

3. **Stride depth buffer readback**: Is the depth-stencil buffer accessible for CPU readback after rendering? Some GPU APIs make this straightforward (OpenGL: `glReadPixels` with `GL_DEPTH_COMPONENT`), others require copying to a staging texture. **Investigation path**: Stride's `Texture` with `PixelFormat.D32_Float` or `D24_UNorm_S8_UInt`, staging copy support.

4. **FBX import without Game Studio**: For the standalone tool path (Option B), can Stride's Assimp pipeline be invoked programmatically at runtime? This would enable loading raw FBX files without pre-compiling through Game Studio. Not needed for Phase 3 (Defenders will use compiled assets), but affects the standalone tool story. **Investigation path**: `Stride.Importer.FBX`, `Stride.Assets.Models`.

5. **Large atlas handling**: When a character variant produces more frames than fit in a single 4096×4096 atlas, `MultiAtlasStrategy` splits across multiple PNG files. How does the game engine consumer handle multi-atlas sprite sheets? The JSON metadata supports it (each frame references its atlas file), but the game-side sprite renderer needs to handle texture switching. **Resolution**: game-specific concern, not SDK concern — document the multi-atlas format and let consumers handle it.

### Design Considerations

1. **GPU memory for batch processing**: Capturing 960 frames requires keeping the render target allocated but not all frames in GPU memory. The pipeline should capture → readback → dispose per frame (or small batch), not accumulate all frames in GPU memory.

2. **Animation clip discovery**: Synty often ships animations as separate FBX files (e.g., `Warrior@Idle.fbx`, `Warrior@Run.fbx`). Stride Game Studio imports these as separate `AnimationClip` assets. The bridge's `GetAvailableAnimations` needs to enumerate all clips associated with a skeleton, which may require scanning the project's asset directory rather than querying a single model's embedded clips.

3. **Equipment visual consistency**: Equipment meshes must use the same skeleton as the base character. Synty ensures this within their packs, but custom equipment must follow the same bone naming convention. The bridge should validate bone name compatibility when attaching equipment and report clear errors if bones don't match.

4. **Character shadow**: A drop shadow under the character improves sprite readability, especially at the 55° top-down angle. Options: (a) render a shadow pass with a directional light and a ground plane, (b) render a silhouette with reduced opacity, (c) game-engine-side shadow (not baked into sprite). **Recommendation**: make it an option on `CameraRig.IncludeShadow` with a separate shadow atlas (like normal maps).

---

## Part 9: Relationship to Existing Bannou SDKs

### What This SDK Is NOT

- **Not a Bannou plugin**: No lib-sprite service. Sprite sheets are offline-generated assets, not runtime-computed content.
- **Not a runtime system**: The game loads the output (PNG + JSON) at runtime. The SDK is a developer tool.
- **Not part of the content flywheel**: Sprites don't participate in GOAP, events, or autonomous NPC behavior. They're visual assets.

### What This SDK Shares

| Shared Element | How Sprite Uses It |
|----------------|-------------------|
| **SDK naming conventions** | `BeyondImmersion.Bannou.SpriteTheory`, kebab-case directories, unified versioning |
| **Bridge pattern** | `ISpriteComposerBridge` following `ISceneComposerBridge` and `IVoxelBuilderBridge` |
| **Command stack** | Same `IEditorCommand` / `CommandStack` pattern as scene-composer |
| **Stride bridge patterns** | Constructor params, using-alias type conversion, entity management |
| **Asset pipeline** | Output PNG + JSON could be bundled as `.bannou` assets via `asset-bundler` for runtime distribution |
| **SDK solution** | Added to `sdks/bannou-sdks.sln`, follows `SDK_VERSION` lockstep versioning |

### Future Integration Points

1. **asset-bundler**: Sprite atlas PNGs + JSON metadata could be packaged into `.bannou` bundles for distribution via the Asset service. The `asset-bundler` would need a sprite type handler.

2. **scene-composer**: Scene documents could reference sprite sheet assets for 2D entity placement in level editing.

3. **The Defenders game project**: The runtime sprite rendering system (not part of this SDK) would consume the JSON metadata to drive sprite animation, handle mirror flips, and sample normal maps for dynamic lighting.

---

## Part 10: Resolved Decisions

Decisions ratified during and after sprite-theory's Phase 1 implementation (2026-04-15). Each entry names the decision, what it supersedes in this planning document, and why. Downstream documents (deep dive, implementation map) reflect the resolved state.

### 10.1: CaptureAngle uses `ProducesMirror` (additive), not `CanMirror` (skip-flag)

**Supersedes**: The initial Part 3 CaptureAngle type listing showed `CanMirror: bool # If true, a mirrored version is generated instead of captured`, and the capture session pseudocode iterated `rig.Angles WHERE NOT angle.CanMirror`. Both are now incorrect.

**Ratified design**: `ProducesMirror: bool` on `CaptureAngle` is additive metadata. **Every `CaptureAngle` in `rig.Angles` is rendered by the bridge.** When `ProducesMirror = true`, `MirrorOptimizer` additionally produces a derived `SpriteFrame` with `IsMirror = true`, `MirrorSourceIndex` pointing to the source, a flipped pivot, and the same atlas rect (no new pixels). Mirror target angles ("NW", "left", etc.) never appear in `rig.Angles` — they exist only in the `SpriteSheet.Frames` output.

**Why**: Rigs expressing "what the bridge must render" is cleaner than rigs expressing "what the world conceptually contains with some entries marked skip-me." The additive design means rig enumeration = render count, with no filtering step. The preset factories (`SideViewBrawler`, `TopDown8Dir`, `TopDown4Dir`) produce 1, 5, and 3 angles respectively — all rendered — matching the "Captured" column of the Mirror Optimization table.

**Code location**: `sdks/sprite-theory/Camera/CaptureAngle.cs`, `sdks/sprite-theory/Mirror/MirrorOptimizer.cs`, `sdks/sprite-theory/Camera/CameraRigPresets.cs`.

### 10.2: PNG encoding is the consumer's responsibility, not sprite-theory's

**Supersedes**: The initial Part 3 project structure listed `PngWriter.cs # Minimal PNG encoder (or use System.Drawing.Common / ImageSharp)`. The Phase 1 Deliverables list did not include it. The plan's parenthetical "or use library" hinted that the author hadn't fully settled the question.

**Ratified design**: `AtlasAssembler.Assemble` returns raw RGBA `byte[][]` — one byte array per atlas. Sprite-theory does not ship a PNG encoder. The composer-layer introduces `IAtlasEncoder { byte[] EncodeRgba(byte[] rgba, int width, int height); }`, which engine bridges implement using their engine's native PNG encoder:

| Engine | Native API |
|--------|-----------|
| Stride | `Stride.Graphics.Image.Save(stream, ImageFileType.Png)` via `Texture.GetDataAsImage()` |
| Godot | `Godot.Image.save_png(path)` / `save_png_to_buffer()` |
| Unity | `UnityEngine.ImageConversion.EncodeToPNG()` / `Texture2D.EncodeToPNG()` |

**Why**: Every target engine bridges already ship a PNG encoder in their graphics package — the bridge pays zero additional dependency cost. Adding `SixLabors.ImageSharp` to sprite-theory (the other feasible path) creates a commercial licensing trap: ImageSharp's Apache 2.0 license is conditional on consumer annual revenue under $1M USD; above that threshold the Six Labors commercial license is required. This is incompatible with the "permissive licenses only" spirit of the Bannou licensing tenet and would propagate to every consumer of sprite-theory. A hand-rolled DEFLATE-based encoder in sprite-theory would duplicate functionality that every consumer already has, at the cost of maintaining compression code forever.

**Code location**: `sdks/sprite-theory/Export/AtlasAssembler.cs` (returns `byte[][]`). The `IAtlasEncoder` surface is specified in the sprite-composer deep dive and map (awaiting implementation).

### 10.3: `MultiAtlasStrategy` is a separate static class (as the plan's project structure showed)

**Supersedes**: An interim implementation inlined the overflow logic into `AtlasPacker.Pack`. The plan's Part 3 project structure listed `MultiAtlasStrategy.cs` as a distinct file.

**Ratified design**: `MultiAtlasStrategy.OpenNextAtlas` is a separate static class in `sdks/sprite-theory/Atlas/`. `AtlasPacker.Pack` delegates when `FindBestRect` returns null. `OpenNextAtlas` pre-validates that the padded frame can fit in a max-sized atlas (fails fast before mutating any state), clears the free rectangle list, and returns `(NewAtlasIndex, Placement)` — the single free rect on a fresh atlas covers the full atlas from origin, which is also the placement rect.

**Why**: Matches the plan's structure listing and Bannou's "one static class per concept" pattern (see `AnimationSampling`, `MirrorOptimizer`, `PivotComputer`, `DepthToNormal` — each a separate static class for one concept). Makes the overflow concept independently testable and discoverable.

**Code location**: `sdks/sprite-theory/Atlas/MultiAtlasStrategy.cs`.

### 10.4: Projection math stays inline in `OrthographicSetup` (no separate `ProjectionMath.cs`)

**Supersedes**: The initial Part 3 project structure listed `ProjectionMath.cs # Orthographic/perspective projection utilities`.

**Ratified design**: All projection math lives inside `OrthographicSetup.Compute` (steps 1–7). No separate `ProjectionMath.cs` exists.

**Why**: Every projection operation in sprite-theory is orthographic-specific and used by exactly one caller. The hypothetical shared utilities a `ProjectionMath` class might expose (perspective projection, generic spherical-to-Cartesian helpers) have no second consumer — perspective projection has no implementation and the spherical-to-Cartesian transform is bound to the camera's (yaw, pitch) input shape. Extracting thin wrappers over single-caller code would be speculative abstraction without a concrete use case. If perspective projection is added later (currently a `ProjectionType.Perspective` enum value with no implementation), `ProjectionMath` can be introduced then alongside a second caller.

**Code location**: `sdks/sprite-theory/Camera/OrthographicSetup.cs`.

### 10.5: `MotionAnalysis.cs` deferred (was already plan-tagged "future")

**Supersedes**: The initial Part 3 project structure listed `MotionAnalysis.cs # Analyze animation to suggest optimal frame counts (future)`.

**Ratified design**: Not in Phase 1. The plan already tagged this as future; the omission is plan-authorized, not a deviation. The project structure listing has been updated to remove the entry to avoid future audits flagging its absence.

**Code location**: N/A (not implemented).

### 10.6: Immutable collection interfaces on record fields

**Supersedes**: The initial type listings showed `Dictionary<string, string>?` on `CharacterVariant.MaterialOverrides`, `SpriteSheet.CustomProperties`, and `SpriteAnimation.AngleFrameMap`.

**Ratified design**: Those fields use `IReadOnlyDictionary<string, string>?` (or `IReadOnlyDictionary<string, int[]>` for `AngleFrameMap`). This matches the immutability contract the deep dive claims for SDK records and matches the existing pattern on sibling fields (`IReadOnlyList<EquipmentSlot> Equipment` on `CharacterVariant`, `IReadOnlyList<AtlasInfo> Atlases` on `SpriteSheet`).

**Why**: Record types are reference-immutable but `Dictionary<,>` contents are mutable. A consumer holding a returned `SpriteSheet` could corrupt shared state by mutating its `CustomProperties`. `IReadOnlyDictionary<,>` is the interface that matches the record's immutability guarantee. System.Text.Json round-trips `IReadOnlyDictionary<string, T>` natively in .NET 6+ via a `Dictionary<,>` backing.

**Code location**: `sdks/sprite-theory/Metadata/CharacterVariant.cs`, `sdks/sprite-theory/Metadata/SpriteSheet.cs`, `sdks/sprite-theory/Metadata/SpriteAnimation.cs`.

### 10.7: `FrameCapture` carries the full variant/rig identity (F2 + F3)

**Supersedes**: The initial `FrameCapture` record had four identity fields — `AngleName`, `AnimationName`, `FrameIndex`, `NormalizedTime` — insufficient to uniquely identify a capture across multi-variant or multi-rig sessions. The initial `AtlasAssembler.Assemble` implementation keyed captures by the Dictionary `<int, FrameCapture>[placement.FrameIndex]`, where `placement.FrameIndex` is an atlas-packer list position and `FrameCapture.FrameIndex` is an intra-animation index — different semantics. The two collided on every realistic multi-animation session because frame 0 of "idle" and frame 0 of "run" both have `FrameCapture.FrameIndex == 0`.

**Ratified design**:
- `FrameCapture` gains **required** `VariantName: string` and `RigName: string`, placed after the pixel dimensions to reflect identity nesting depth (variant ⊃ rig ⊃ angle ⊃ animation ⊃ frame).
- The tuple (`VariantName`, `RigName`, `AngleName`, `AnimationName`, `FrameIndex`) is the unique identity of a capture within a session's `CapturedFrames` list.
- `AtlasAssembler.Assemble` indexes captures by `frames[placement.FrameIndex]` directly (list position, matching the implementation map's spec), throwing `ArgumentOutOfRangeException` on invalid layout input.
- The bridge's `CaptureFrameAsync` signature takes `(variantName, rigName, angleName, animationName, frameIndex, normalizedTime, captureDepth, ct)` — the composer supplies every identity field, the bridge stamps them onto the returned `FrameCapture`.

**Why**: Downstream assembly groups captures by `(Variant, Rig)` to produce one `SpriteSheet` per grouping. Without variant and rig identity on each capture, that grouping is impossible — the multi-variant `SpriteProject` container (Decision 10.12) requires it structurally. The list-position indexing in `AtlasAssembler` is simpler and matches how `AtlasPacker.Pack` constructs placements: `frames.Select((f, i) => (f.Width, f.Height, i))` — the `i` is the list position, and the packer round-trips it. Four new regression tests in `AtlasAssemblerTests.cs` prove the fix.

**Code location**: `sdks/sprite-theory/Metadata/FrameCapture.cs`, `sdks/sprite-theory/Export/AtlasAssembler.cs`, `sdks/sprite-theory.tests/Export/AtlasAssemblerTests.cs`.

### 10.8: Bridge capability flags + rig/angle lifecycle hooks (I3, F6, F9)

**Supersedes**: The initial bridge contract had no capability negotiation (the composer couldn't tell whether a bridge supported depth capture or skeleton introspection) and no lifecycle hooks (every operation that might need per-rig or per-angle setup was the bridge's private concern, forcing every bridge to duplicate capture-time bookkeeping).

**Ratified design**:
- `ISpriteComposerBridge` gains two capability flags: `SupportsDepthCapture: bool` and `SupportsSkeletonIntrospection: bool`.
- `ISpriteComposerBridge` gains four async lifecycle hooks: `BeginRigAsync(CameraRig, ct)`, `EndRigAsync(CameraRig, ct)`, `BeginAngleAsync(CaptureAngle, ct)`, `EndAngleAsync(CaptureAngle, ct)`. Default no-op implementations on `SpriteComposerBridgeBase` mean bridges only override when they need per-rig or per-angle setup.
- The composer inspects capability flags before optional operations: if `SupportsDepthCapture = false`, skip normal-map atlas generation (log once per session). If `SupportsSkeletonIntrospection = false`, fall back to `ComputeFromBounds` when `AnchorBoneName` is set (log once per session).
- The composer calls lifecycle hooks unconditionally — bridges that don't need them inherit the no-op defaults.

**Why**: Capability negotiation lets the composer make correct decisions (don't request depth from a bridge that can't supply it; don't fail when a rig sets `IncludeNormalMap = true` and the bridge can't support it). Lifecycle hooks let bridges allocate sized render targets, snapshot per-angle state, or release GPU resources at the correct loop boundary without the composer needing to know. Default no-ops keep the contract minimal — simple bridges implement five methods plus the capture method, not fifteen.

**Code location**: `sdks/sprite-composer/Abstractions/ISpriteComposerBridge.cs` (awaiting Phase 2 implementation).

### 10.9: Per-angle pivot computation inside the capture loop (F11)

**Supersedes**: The initial capture pseudocode showed `resolvedPivot ← PivotComputer.ComputeFromBounds(modelBounds, rigOrthoParams)` computed **once per rig**, using `rig.Angles[0]`'s `OrthographicParameters`. This is wrong for every multi-angle rig — even when all angles share a pitch (the canonical `TopDown8Dir` / `SideViewBrawler` presets), the convex-hull-on-camera-plane of the 8 world-space bounding corners varies with yaw, which makes `orthoHeight` vary with yaw, which makes `pivotY = 0.5 - v/orthoHeight` vary with yaw. Computing once per rig and applying to all angles produces visibly wrong feet placement on every non-`rig.Angles[0]` angle.

**Ratified design**: pivot is resolved **per angle, inside the angle loop, during capture** — not once per rig during assembly. Each captured frame carries its own pivot alongside the pixel data. The capture loop calls `ResolvePivot(variant, bridge, handle, modelBounds, orthoParams)` (defined in the capture session pipeline above) for every angle of every rig of every variant, using the `OrthographicParameters` for that specific angle.

**Regression protection**: three new tests in `PivotComputerTests.cs` — `ComputeFromBounds_DifferentPitches_ProduceDifferentPivots`, `ComputeFromBounds_SameYawDifferentPitches_DivergeInY`, and (critically) `ComputeFromBounds_DifferentYawsSamePitch_AlsoDivergeInY` — prove that per-angle computation is required even for single-pitch rigs.

**Why**: Pivots in normalized frame coordinates depend on the projected bounding-box extents on the *specific angle's* camera plane. A per-rig shortcut is only correct for single-angle rigs. Since all practical Defenders rigs are multi-angle (side-view: 1 angle but mirror-producing, top-down: 5 angles), the shortcut would produce wrong output for every deployed rig.

**Code location**: `sdks/sprite-theory/Camera/PivotComputer.cs` (gained `ProjectWorldPointToFrame` primitive for this). `sdks/sprite-theory.tests/Camera/PivotComputerTests.cs` (the three divergence tests). Sprite-composer's capture-session pseudocode (§ Part 4 of this document).

### 10.10: `AssetReference` replaces raw string paths (I1, I8, I9)

**Supersedes**: Initial Part 3 types showed `CharacterVariant.ModelPath: string`, `EquipmentSlot.MeshPath: string`, `MaterialOverrides: IReadOnlyDictionary<string, string>?`, and the bridge's `LoadModelAsync(path: string)`, `AttachEquipmentAsync(path: string)`, `SetMaterialOverride(path: string)` — all raw string paths. This pattern is not portable across engines: Stride compiles FBX to `.sdmodel`, Godot uses `.pck` scenes, Unity uses AssetBundles or Addressables. A raw string is an engine-specific convention masquerading as an SDK type.

**Ratified design**: introduce `AssetReference(BundleId: string, AssetId: string, VariantId: string? = null)` as a shared engine-agnostic asset identifier. `CharacterVariant.Model`, `EquipmentSlot.Mesh`, and `MaterialOverrides` values all use `AssetReference`. The bridge's `LoadModelAsync`, `AttachEquipmentAsync`, and `SetMaterialOverride` all take `AssetReference` parameters. The bridge resolves each reference to an engine-specific handle at load time. For raw-FBX workflows, bridges supporting filesystem asset sources register loose files as single-asset bundles — the exact convention (`"file:///path/to/warrior.fbx"` as BundleId, or a filesystem-asset-source convention similar to scene-composer-stride's `FilesystemAssetSource`) is the bridge's concern.

**Why**: The same reason scene-composer uses asset references — `SpriteProject` files should be portable across engines. A project authored for Stride should load in a hypothetical Godot bridge without rewriting paths. AssetReference also naturally supports versioning (VariantId) and decouples authoring-time asset location from runtime asset location.

**Code location**: `sdks/sprite-theory/AssetReference.cs` (new file), `sdks/sprite-theory/Metadata/CharacterVariant.cs`, `sdks/sprite-theory/Metadata/EquipmentSlot.cs`. Bridge surface awaits Phase 2 implementation.

### 10.11: Camera parameters are computed by the composer, applied by the bridge

**Supersedes**: The initial bridge contract had `ConfigureCamera(angle: CaptureAngle, bounds: BoundingBox, frameSize: (int, int))` — the bridge received raw inputs and called `OrthographicSetup.Compute` itself. This forced every bridge to know about sprite-theory's computation and duplicated the path to `OrthographicParameters`.

**Ratified design**: the composer computes `orthoParams = OrthographicSetup.Compute(angle, modelBounds, rig.FrameSize)` once per angle and calls `bridge.ConfigureCamera(orthoParams, rig.FrameSize)`. The bridge applies the already-computed parameters to the engine's camera; it never re-computes. The pivot resolution path also uses these same `orthoParams`, so the camera configured in the engine and the pivot stamped onto captured frames are guaranteed to agree.

**Why**: Single computation point for camera parameters. Per-angle pivot (Decision 10.9) and per-angle camera configuration share the same `OrthographicParameters` instance — no drift possible. Bridges are simpler: they apply a camera matrix, they don't compute one.

**Code location**: the capture-session pseudocode (§ Part 4 of this document). Bridge contract awaits Phase 2 implementation.

### 10.12: Multi-variant `SpriteProject` container (R6)

**Supersedes**: The initial `SpriteProject` held a single `Variant: CharacterVariant` and a single `AnimationConfigs` dictionary. Defenders has 50–80 variants across heroes, troops (9 specs × 3 tiers = 27), enemies (~20), bosses (6), and NPCs (~10). One project per variant would produce 50–80 project files — unmaintainable.

**Ratified design**: `SpriteProject` becomes a multi-variant container:
- `Rigs: IReadOnlyList<CameraRig>` — rigs shared by every variant in the project.
- `AnimationSets: IReadOnlyDictionary<string, AnimationSet>` — named reusable animation configurations (`SelectedAnimationNames` + per-animation `AnimationConfig` map + default config). Multiple variants bind to a named set by name.
- `Variants: IReadOnlyList<VariantBinding>` — one entry per character variant. Each binding references an `AnimationSet` by name and optionally overrides individual animations or excludes specific animations for this variant.

Defenders collapses to ~4 projects: `Heroes.spriteproj.json`, `Troops.spriteproj.json`, `Enemies.spriteproj.json`, `Bosses.spriteproj.json`. The 27 troops share one `humanoid-combat` AnimationSet; specific variants override individual animations without forking the set.

The capture session loops `FOREACH variant IN project.Variants` as the outer loop and emits one `SpriteSheet` per `(VariantName, RigName)` — a 4-variant × 2-rig heroes project produces 8 sprite-sheet outputs. Filenames are templated via `ExportOptions.AtlasFilenamePattern` supporting both `{variant}` and `{rig}` placeholders (default: `"{variant}_{rig}_{atlas}.png"`).

**Why**: The combinatorial volume of Defenders content collapses to manageable file counts, duplication of shared animation configuration is eliminated, and variant-specific overrides stay local to the variant binding. This is the shape the batcher (Decision 10.13) consumes — one `--projects` arg invocation per class of content, not per variant.

**Code location**: `sdks/sprite-composer/Project/` (awaiting Phase 2 implementation) — `SpriteProject.cs`, `AnimationSet.cs`, `VariantBinding.cs`, `ProjectSerializer.cs`, `ExportOptions.cs`.

### 10.13: Phase 3.5 introduces SpriteBatcher CLI alongside Phase 3

**Supersedes**: The initial Part 6 described SpriteBatcher conceptually but left it as an indefinitely-future phase. Given the multi-variant project shape (Decision 10.12), the CLI batcher is now a near-term deliverable that consumes exactly the same `SpriteProject` files the interactive editor uses.

**Ratified design**: a standalone CLI project at `tools/sprite-batcher/` with a stable command-line surface (`--projects`, `--output`, `--parallel`, `--bridge`, `--asset-bundles`, `--filter-variants`, `--filter-rigs`, `--dry-run`, `--verbose`). Executes captures per-project × per-variant using `sprite-composer`'s `CaptureSession` verbatim — no separate capture logic. Per-item error isolation, summary report, meaningful exit codes, and a `--dry-run` mode that computes `CaptureManifest` totals without invoking the bridge. Phase position: 3.5 (between the Stride bridge and the interactive Defenders composer project).

**Full design**: `docs/planning/SPRITE-BATCHER.md`.

**Why**: Landing the batcher alongside Phase 3 demonstrates that the same capture pipeline serves the interactive editor and CI batch processing from day one. It also prevents Phase 2 from making bridge-bootstrapping decisions that only make sense for the interactive context.

**Code location**: `tools/sprite-batcher/` (awaiting Phase 3.5 implementation).

### 10.14: Phase 1.5 Stride capture spike precedes Phase 2

**Supersedes**: The initial Part 8 Open Questions section acknowledged three Stride primitives needed investigation — animation time-set without game-loop advance, render-to-texture with CPU readback, depth buffer readback — and hoped these would resolve during Phase 3. But Phase 2 freezes the bridge contract including depth-capture support, render-target lifecycle, and animation time-set semantics. If any of those primitives turn out to require different ergonomics than the current contract assumes, the contract needs to reflect that before sprite-composer builds on top of it.

**Ratified design**: a throwaway Stride project, discarded after the spike, validates the three primitives above. Output is a short write-up in `docs/planning/SPRITE-STRIDE-SPIKE-RESULTS.md` documenting what worked, what didn't, and any bridge-contract adjustments needed before Phase 2. Expected duration: 2–3 days. Phase position: 1.5 (before Phase 2 sprite-composer).

**Why**: Discovering a bridge-contract mismatch in Phase 3 would invalidate Phase 2 work — every Phase 2 unit test, every mock bridge, every capture-session assertion. A 2–3 day spike before freezing the contract is cheap insurance.

**Code location**: throwaway project (not versioned); result document at `docs/planning/SPRITE-STRIDE-SPIKE-RESULTS.md` (awaiting Phase 1.5 execution).

### 10.15: `AnchorBoneName` for skeleton-anchored pivots (R1)

**Supersedes**: The initial pivot resolution was bounds-first with `PivotOverride` as the only escape hatch. For subjects whose bounding-box minimum is not a sensible anchor (flowing cloth extending bounds below the feet, weapons extending bounds outward, asymmetric rigs), every variant needed a hand-tuned `PivotOverride` — fragile and one-off.

**Ratified design**: `CharacterVariant` gains optional `AnchorBoneName: string?`. The four-step pivot resolution order becomes:

1. `PivotOverride` — used verbatim if set.
2. `AnchorBoneName` — if set AND the bridge advertises `SupportsSkeletonIntrospection` AND the bone exists, the bridge's `TryGetBonePosition` returns the bone's world position, projected via `PivotComputer.ProjectWorldPointToFrame`.
3. `PivotComputer.ComputeFromBounds` — fallback.
4. `PivotComputer.DefaultHumanoidPivot` — terminal fallback.

The common case (flowing clothing, asymmetric gear) now has a variant-level fix that doesn't require hand-tuning frame coordinates. `PivotOverride` remains for subjects where even a bone anchor is unreliable (flying enemies with no root bone, purely abstract shapes).

**Why**: Defenders characters like Vu'ud (flowing sari extending bounds below the feet) need sensible pivot placement without per-frame hand-tuning. A skeleton-bone anchor naturally tracks the "visual feet" through every frame regardless of cloth physics.

**Code location**: `sdks/sprite-theory/Metadata/CharacterVariant.cs` (field added), `sdks/sprite-theory/Camera/PivotComputer.cs` (`ProjectWorldPointToFrame` primitive extracted), bridge `TryGetBonePosition` surface (awaiting Phase 2).

### 10.16: Shared `Vector3` primitive in core (supports I1/F11 and future SDKs)

**Supersedes**: sprite-theory's `BoundingBox` used `(float X, float Y, float Z)` tuples for min/max/center/extents/size. `OrthographicParameters` used the same tuple shape for position/direction/up. Scene-composer has its own `SceneComposer.Math.Vector3` (double-precision). There was no shared 3D-vector primitive.

**Ratified design**: introduce `BeyondImmersion.Bannou.Core.Math.Vector3` (float, 12-byte struct, exact-equality, full operator set: +, -, *, /, unary negation; `Dot`, `Cross`, `Lerp`, `Distance`, `Min`, `Max`; `Normalized`, `Length`, `LengthSquared`; `Zero`, `One`, `UnitX/Y/Z` statics; `Deconstruct`). sprite-theory replaces tuples with `Vector3` in `BoundingBox`, `OrthographicParameters`, and the new `PivotComputer.ProjectWorldPointToFrame`. Scene-composer retains its double-precision `SceneComposer.Math.Vector3` for large-world accuracy — the two types have different namespaces and do not conflict. The sprite-composer bridge contract's `TryGetBonePosition → Vector3?` uses the core primitive.

**Why**: Sprite-theory and the upcoming bridge both need 3D math. Having two different precisions across sibling SDKs (core `Vector3` float, scene-composer `Vector3` double) is correct — sprite-theory is a graphics-pipeline SDK where float matches Stride/Godot/Unity native types and avoids per-operation conversion, while scene-composer operates on large-world double-precision coordinates. The core SDK is the natural home for a cross-SDK float primitive because future SDKs (a hypothetical pixel-theory, physics-theory, etc.) would also want it.

**Code location**: `sdks/core/Math/Vector3.cs` (new file), `sdks/core.tests/Math/Vector3Tests.cs` (30 new tests), sprite-theory's `BoundingBox.cs` / `Camera/OrthographicParameters.cs` / `Camera/OrthographicSetup.cs` / `Camera/PivotComputer.cs`.

---

## Part 11: Defenders as First Consumer — Requirements from Locked Content

This section captures first-consumer input from Defenders of Ba'hara (the primary consumer named in the document header) as Phase 1 scope items identified against locked Defenders art / lighting / narrative content. These items emerge from decisions and reference documentation in the sibling `defenders-kb` repository (decisions D3, D73, D44, D91, D4, D133; art reference docs `19-art/STYLE-BIBLE.md`, `19-art/SYNTY-MAPPING.md`, `19-art/LIGHTING.md`). Items 11.1 and 11.2 are scope gaps the Bannou team must address during Phase 1 design-to-implementation — the current design (Parts 3–5 + Part 10 decisions) does not yet cover them. Items 11.3 and 11.4 are conceptually covered by existing design but listed as Phase 3 acceptance criteria for Defenders' consumption.

**Defenders-side reference**: `defenders-kb/00-meta/DECISIONS.md` D133 ("Defenders' sprite-composer Phase 1 scope — first-consumer requirements") captures these items from the Defenders side. The SYNTY-MAPPING pipeline specification Defenders relies on is at `defenders-kb/19-art/SYNTY-MAPPING.md` §1.

### 11.1: Per-act lighting / palette variant support (gap)

**Defenders requirement**: the same character variant must be captured under different lighting rigs across three acts of the campaign — daylight for Act 1, cool-shifted ambient for Act 2, desaturated for Act 3 (per the Silence-progression visual spec). `defenders-kb/19-art/LIGHTING.md` §1 estimates **4–5 lighting variants per character per act**. Per-character story-state variants (Ja'hana post-banishment empty-cummerbund, Vu'ud mana-gauge-active coloring, Gee progressive corruption) multiply this further.

**Gap in current design**: `SpriteProject` (Part 4, Decision 10.12) holds one `Variants` list per project. The pattern for capturing the *same* character variant under multiple distinct lighting rigs is not explicit. Evaluation options:

- **Option A — Multi-project per act**: three projects per character class (`Heroes-Act1.spriteproj.json`, `-Act2`, `-Act3`). Project count 3× current estimate.
- **Option B — Per-rig lighting variants within a project**: `CameraRig` gains a lighting-variant list; capture session iterates `variant × rig × lighting-variant × angle × animation × frame`. Project count unchanged; per-project frame count scales by lighting-variant count.
- **Option C — Runtime tinting / grading**: capture under neutral lighting; Stride's rendering applies per-act ambient at runtime. Capture count unchanged; visual precision trade-off.
- **Option D — Other mechanism**: Bannou's design choice.

Bannou picks; Defenders requires that one of A–D (or a documented alternative) exists in Phase 1 and that full-roster capture stays within the 48-minute nightly budget (Part 4 frame-count estimation table) after per-act lighting is applied.

**Acceptance**: any mechanism producing sprite sheets consumable by the Defenders runtime and covering per-act lighting progression within the capture budget.

### 11.2: Painterly post-pass hook (gap)

**Defenders requirement**: `defenders-kb/19-art/STYLE-BIBLE.md` §12 and `defenders-kb/19-art/SYNTY-MAPPING.md` §1 specify a "painterly post-pass (suppress soft-gradient artifacts)" as a pipeline step **after** sprite-composer bake. Synty models + standard ortho capture produce smooth-gradient sprites that don't match Defenders' Hades-adjacent hand-illustrated aesthetic; a post-pass applies painterly treatment (edge sharpening, tone quantization, stylistic filtering) to fit the visual target.

**Gap in current design**: sprite-composer does not include a post-processor extension point. `CaptureResult` → `ExportPipeline.Export` writes raw atlas bytes via `IAtlasEncoder` with no transformation stage between capture and encode.

- **Option A — Phase 1 scope extension**: add `IFramePostProcessor` (per-frame, operating on captured RGBA before atlas assembly) or `IAtlasPostProcessor` (per-atlas, operating on assembled atlas bytes before `IAtlasEncoder`). Default implementation is identity pass-through. Defenders supplies its painterly post-processor as a consumer.
- **Option B — Documented Defenders-side intercept path**: Bannou does not add a post-processor extension; Defenders intercepts between `CaptureResult.CapturedFrames` and invoking `ExportPipeline.Export`, running its post-pass and feeding transformed frames into `AtlasAssembler.Assemble` manually. Requires that this intercept path is documented as a supported pattern, not an anti-pattern.

Bannou picks A or B; Defenders requires one of them is supported.

**Acceptance**: either Phase 1 exposes a post-processor extension point, OR the Defenders-side intercept path is explicitly supported in the sprite-composer API surface and documented.

### 11.3: Synty FBX ingestion via Stride — confirmation

**Defenders requirement**: source is reskinned Synty FBX assets (per `defenders-kb/19-art/SYNTY-MAPPING.md` + D4 Synty library context) exported from Maya. `sprite-composer-stride` bridge's `LoadModelAsync` must resolve `AssetReference` through Stride's asset system cleanly for reskinned Synty FBX sources.

**Status in current design**: Part 5 (§ FBX Model Loading) describes Option A (Stride Game Studio Project) as "recommended for Defenders" with compiled assets via `ContentManager.Load<Model>()`. Decision 10.10 establishes `AssetReference(BundleId, AssetId, VariantId?)` as the engine-agnostic identifier. **Conceptually covered; requires validation at Phase 3 bridge implementation.**

**Defenders acceptance**: Phase 3 deliverable item 11 ("Integration test: load a Synty FBX, capture frames, verify output") covers this with a reskinned Synty source. No new SDK scope.

### 11.4: Normal map capture end-to-end — confirmation

**Defenders requirement**: Defenders uses 2D dynamic lighting on sprites (per `defenders-kb/19-art/STYLE-BIBLE.md` + LIGHTING). Normal maps enable pre-rendered sprites to react to in-game light sources (Dead Cells technique).

**Status in current design**: Part 3 § Normal Map Generation documents the depth-to-normal pipeline; Part 4 bridge contract has `SupportsDepthCapture` capability flag; Part 7 Phase 3 Deliverables include `StrideDepthCapturer`; Phase 1.5 Stride capture spike validates depth-buffer readback. **Conceptually covered and Phase-gated; requires Phase 3 validation.**

**Defenders acceptance**: `sprite-composer-stride` bridge advertises `SupportsDepthCapture = true` post-Phase-1.5, produces normal-map atlases loadable by Stride's rendering with matching UV coordinates against the color atlas. Phase 3 integration test covers this. No new SDK scope.

### Scope exclusions

Defenders does NOT request from Phase 1:

- **defenders-sprite-composer Stride UI project** — acknowledged as Phase 4 in Part 7 and in this document's introduction. Separate Defenders deliverable; Phase 4 scope is out of Phase 1.
- **Dialogue portrait pipeline** — per `defenders-kb/03-characters/CHARACTER-DESIGN.md` §10, dialogue portraits are hand-painted 2D illustrations (~80–200+ across all characters). Not sprite-composer scope.
- **sprite-composer-godot / -unity bridges** — Defenders uses Stride per D56; non-Stride bridges are not Defenders' concern.
- **Post-launch reskin variant workflow** (DLC outfits) — production-process concern, not SDK scope.

### Co-evolution commitment

When Phase 1 lands, Defenders becomes the validation consumer. Performance acceptance benchmark: full-roster capture (~4 projects × ~60 variants × 2 rigs × 20 animations × 8 frames = ~48 minutes per Part 4 estimation table). Additional Phase 1 requirements may emerge from first authoring sessions; appended as further Ds in `defenders-kb`.

Section reflects Defenders' first-consumer input as of D133 (2026-04-20). Updates follow new Defenders Ds extending or modifying Phase 1 requirements.

---

*This document captures the architectural design for the Sprite Composer SDK family. For the established SDK patterns it follows, see [SDK-OVERVIEW.md](../guides/SDK-OVERVIEW.md). For the Cinematic System design that established the Theory/Composer domain pattern, see [CINEMATIC-SYSTEM.md](CINEMATIC-SYSTEM.md). For the Defenders game requirements, see the defenders-kb repository.*
