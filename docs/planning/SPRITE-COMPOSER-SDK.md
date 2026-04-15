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
├── Camera/
│   ├── CameraRig.cs              # Rig definition: projection type, angle list, frame size
│   ├── CaptureAngle.cs           # Single angle: name, yaw, pitch, canMirror, mirrorTarget
│   ├── CameraRigPresets.cs       # Built-in rigs: SideViewBrawler(), TopDown8Dir(), TopDown4Dir()
│   ├── OrthographicSetup.cs      # Compute ortho camera params to fit character bounds at angle
│   └── ProjectionMath.cs         # Orthographic/perspective projection utilities
├── Animation/
│   ├── AnimationSampling.cs      # Frame timing modes: uniform, keyframe-aligned, adaptive
│   ├── FrameSequence.cs          # Ordered list of frame timestamps for one animation
│   ├── AnimationConfig.cs        # Per-animation capture config: frame count, speed, loop clip
│   └── MotionAnalysis.cs         # Analyze animation to suggest optimal frame counts (future)
├── Atlas/
│   ├── AtlasPacker.cs            # MaxRects bin-packing algorithm
│   ├── AtlasLayout.cs            # Result: frame positions, atlas dimensions, packing efficiency
│   ├── AtlasOptions.cs           # Max atlas size, padding, power-of-two, per-animation rows
│   └── MultiAtlasStrategy.cs     # Overflow handling when frames exceed single atlas size
├── Mirror/
│   ├── MirrorOptimizer.cs        # Compute which angles are mirrors, generate flip metadata
│   └── MirrorInfo.cs             # Per-frame mirror data: source angle, flip axis
├── Metadata/
│   ├── SpriteSheet.cs            # Complete output: atlas layout + frames + animations + mirrors
│   ├── SpriteFrame.cs            # Per-frame: rect in atlas, pivot point, duration, trimmed bounds
│   ├── SpriteAnimation.cs        # Per-animation: name, frame indices, loop mode, total duration
│   ├── CharacterVariant.cs       # Model + equipment list + palette — input definition
│   └── CaptureManifest.cs        # Full capture job: variant + rigs + animations → expected outputs
├── NormalMap/
│   ├── DepthToNormal.cs          # Convert depth buffer pixels to normal map (Sobel or central diff)
│   └── NormalMapOptions.cs       # Strength, blur, format (RGB tangent-space)
├── Export/
│   ├── SpriteSheetSerializer.cs  # SpriteSheet ↔ JSON (the canonical metadata format)
│   ├── IPixelSource.cs           # Abstraction: raw RGBA pixel data + dimensions (engine-agnostic)
│   ├── IDepthSource.cs           # Abstraction: depth buffer data + dimensions (engine-agnostic)
│   ├── AtlasAssembler.cs         # Compose captured frames into atlas PNG using pixel sources
│   └── PngWriter.cs              # Minimal PNG encoder (or use System.Drawing.Common / ImageSharp)
└── sprite-theory.csproj
```

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
├── CanMirror: bool                 # If true, a mirrored version is generated instead of captured
├── MirrorTargetName: string?       # Name of the angle this mirrors (e.g., "NE" mirrors to "NW")
└── MirrorAxis: MirrorAxis          # Horizontal (most common) | Vertical
```

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
├── Name: string                       # Variant identifier (e.g., "warrior_plate_sword")
├── ModelPath: string                  # Path to base character FBX
├── Equipment: IReadOnlyList<EquipmentSlot>  # Attached equipment
│   ├── EquipmentSlot
│   │   ├── SlotName: string           # "head", "chest", "weapon_r", "shield_l"
│   │   ├── MeshPath: string           # Path to equipment FBX/mesh
│   │   └── BoneName: string           # Skeleton bone to attach to
├── MaterialOverrides: Dictionary<string, string>?  # Palette/material swaps
└── Scale: float                       # Model scale factor (default: 1.0)
```

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

1. Which angles are mirrors of other angles (defined by `CaptureAngle.CanMirror`)
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
    "modelPath": "Characters/Warrior_Base.fbx",
    "equipment": [
      { "slot": "chest", "meshPath": "Equipment/Plate_Chest.fbx", "bone": "Spine2" },
      { "slot": "weapon_r", "meshPath": "Weapons/Sword_01.fbx", "bone": "RightHand" }
    ]
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
ISpriteComposerBridge

  // ── Model Management ──
  LoadModelAsync(path: string, ct) → IModelHandle
      Load a 3D model (FBX) into the engine scene. Returns an opaque handle.

  DisposeModel(handle: IModelHandle) → void
      Unload and clean up engine resources for a model.

  GetModelBounds(handle: IModelHandle) → BoundingBox
      Get the axis-aligned bounding box of the model in its current pose.

  // ── Equipment ──
  AttachEquipmentAsync(model: IModelHandle, boneName: string, equipmentPath: string, ct) → IEquipmentHandle
      Load equipment mesh and parent it to the specified skeleton bone.

  DetachEquipment(handle: IEquipmentHandle) → void
      Remove attached equipment from the model.

  SetMaterialOverride(model: IModelHandle, materialSlot: string, materialPath: string) → void
      Override a material/palette on the model or equipment.

  // ── Animation ──
  GetAvailableAnimations(model: IModelHandle) → IReadOnlyList<AnimationInfo>
      Enumerate all animation clips available on the model's skeleton.
      Returns: name, duration, frame count, loop flag.

  SetAnimation(model: IModelHandle, animationName: string) → void
      Set the active animation clip on the model.

  SetAnimationTime(model: IModelHandle, normalizedTime: float) → void
      Seek the animation to a specific point (0.0 = start, 1.0 = end).
      The model's pose updates to reflect this time.

  // ── Camera ──
  ConfigureCamera(angle: CaptureAngle, bounds: BoundingBox, frameSize: (int, int)) → void
      Position and orient the capture camera to frame the model at the given angle.
      Uses OrthographicSetup from sprite-theory to compute the projection.

  // ── Capture ──
  CaptureFrameAsync(ct) → FrameCapture
      Render the current scene to an off-screen target and return the pixel data.
      Returns RGBA pixels, depth buffer (if available), and dimensions.

  // ── Preview (interactive 3D) ──
  SetPreviewCamera(yaw: float, pitch: float, distance: float) → void
      Position an interactive orbit camera for 3D model preview.

  SetPreviewAnimationPlaying(playing: bool) → void
      Start/stop real-time animation playback in the preview.

  // ── Lifecycle ──
  Dispose() → void
      Clean up all engine resources.
```

**Key design decisions**:

1. **Opaque handles**: `IModelHandle` and `IEquipmentHandle` are marker interfaces. The bridge implementation defines the concrete type (e.g., `StrideModelHandle` wrapping a Stride `Entity`). The composer never touches engine types.

2. **Normalized animation time**: `SetAnimationTime(0.0 - 1.0)` instead of absolute seconds. This abstracts away animation duration — the composer computes normalized time from `FrameSequence` timestamps.

3. **Camera configuration uses sprite-theory**: The bridge receives a `CaptureAngle` and `BoundingBox` and uses `OrthographicSetup` to compute the exact camera matrix. The math lives in sprite-theory; the bridge applies it to the engine's camera.

4. **FrameCapture is engine-agnostic**: Raw RGBA byte array + optional depth float array + dimensions. No engine types cross the bridge boundary.

### Capture Session Pipeline

The `CaptureSession` orchestrates the full capture workflow:

```
CaptureSession.ExecuteAsync(project, bridge, progress, ct)

  FOR EACH rig IN project.Rigs:
    FOR EACH captureAngle IN rig.Angles:   // Every angle IS rendered; ProducesMirror is additive metadata
      bridge.ConfigureCamera(captureAngle, modelBounds, rig.FrameSize)

      FOR EACH animation IN project.Animations:
        config = project.GetAnimationConfig(animation.Name)
        frameSequence = AnimationSampling.GenerateFrames(animation, config)
        bridge.SetAnimation(model, animation.Name)

        FOR EACH frameTime IN frameSequence:
          bridge.SetAnimationTime(model, frameTime.NormalizedTime)
          frame = await bridge.CaptureFrameAsync(ct)

          IF rig.IncludeNormalMap AND frame.DepthData != null:
            normalFrame = DepthToNormal.Generate(frame.DepthData, normalOptions)

          capturedFrames.Add(frame)
          progress.Report(current++, total)

  // Assembly phase
  atlasLayout = AtlasPacker.Pack(capturedFrames, atlasOptions)
  mirrorInfo = MirrorOptimizer.GenerateMirrors(rig, capturedFrames)
  spriteSheet = SpriteSheet.Build(atlasLayout, mirrorInfo, project.Variant, rig)

  // Export
  ExportPipeline.Export(spriteSheet, capturedFrames, exportOptions)
```

**Frame count estimation** (for progress reporting):

```
totalFrames = Σ (rig.CapturedAngleCount × animation.FrameCount) for each rig × animation
```

For a Defenders character with 2 rigs (side-view: 1 angle, top-down: 5 angles), 20 animations, 8 frames each:
- Side-view: 1 × 20 × 8 = 160 frames
- Top-down: 5 × 20 × 8 = 800 frames
- Total frames to capture: **960**
- At ~50ms per frame (render + readback): **~48 seconds** per character variant

### Project Management

A `SpriteProject` captures the complete configuration for reproducible captures:

```
SpriteProject
├── Name: string                                # "Warrior"
├── Variant: CharacterVariant                   # Model + equipment + PivotOverride (see § Part 3)
├── Rigs: IReadOnlyList<CameraRig>              # Camera rigs to capture with
├── AnimationConfigs: IReadOnlyDictionary<string, AnimationConfig>  # Per-animation overrides
├── DefaultAnimationConfig: AnimationConfig     # Defaults for unconfigured animations
├── ExportOptions: ExportOptions                # Output paths, filenames, IAtlasEncoder (see § Part 10)
├── NormalMapOptions: NormalMapOptions?         # Normal map generation settings
└── Metadata: IReadOnlyDictionary<string, string>  # Game-specific metadata to embed in output
```

Projects serialize to JSON and can be shared across team members or stored in version control. This is the input format that a future SpriteBatcher would consume.

### Command-Based Undo/Redo

Following the scene-composer pattern, all project modifications go through the command stack:

| Command | What It Does | Undo |
|---------|-------------|------|
| `AddRigCommand` | Add a camera rig to the project | Remove the rig |
| `RemoveRigCommand` | Remove a camera rig | Re-add the rig at its original position |
| `ModifyRigCommand` | Change rig parameters (frame size, padding, etc.) | Restore previous parameters |
| `SetAnimationConfigCommand` | Change per-animation settings | Restore previous config |
| `AttachEquipmentCommand` | Add equipment to a slot | Remove equipment, call bridge.Detach |
| `DetachEquipmentCommand` | Remove equipment from a slot | Re-attach, call bridge.Attach |
| `SetModelCommand` | Change the base character model | Reload previous model |
| `SetScaleCommand` | Change model scale | Restore previous scale |

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

### Phase 1: sprite-theory (Week 1 — Can Start Today)

Pure computation. Zero dependencies. Fully unit-testable.

**Deliverables**:
1. `CameraRig`, `CaptureAngle`, `CameraRigPresets` — rig definitions with Defenders-specific presets
2. `OrthographicSetup` — compute ortho camera parameters from character bounds + angle
3. `AnimationSampling`, `FrameSequence` — uniform and keyframe-aligned frame timing
4. `AtlasPacker` with MaxRects — bin-packing with configurable options
5. `MultiAtlasStrategy` — overflow handling when frames exceed a single atlas's MaxSize
6. `MirrorOptimizer` — compute which angles produce mirrors, generate derived frame metadata
7. `PivotComputer` — auto-derive pivots from bounds + camera (feet-on-ground projection)
8. `SpriteSheet`, `SpriteFrame`, `SpriteAnimation`, `CharacterVariant` — output metadata types
9. `SpriteSheetSerializer` — JSON serialization/deserialization of the metadata schema
10. `DepthToNormal` — Sobel-based normal map generation from depth data
11. `IPixelSource`, `IDepthSource` — engine-agnostic raw data abstractions
12. `AtlasAssembler` — compose captured frames into atlas RGBA byte arrays (PNG encoding is the consumer's responsibility — see § Part 10)
13. Comprehensive unit tests for all of the above

**Directory**: `sdks/sprite-theory/` + `sdks/sprite-theory.tests/`

**Status**: Phase 1 implemented on 2026-04-15 (87 unit tests, 0 warnings, 0 errors). See `docs/sdks/SPRITE-THEORY.md` for the deep dive and `docs/sdks/maps/SPRITE-THEORY.md` for the method-level map.

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

---

*This document captures the architectural design for the Sprite Composer SDK family. For the established SDK patterns it follows, see [SDK-OVERVIEW.md](../guides/SDK-OVERVIEW.md). For the Cinematic System design that established the Theory/Composer domain pattern, see [CINEMATIC-SYSTEM.md](CINEMATIC-SYSTEM.md). For the Defenders game requirements, see the defenders-kb repository.*
