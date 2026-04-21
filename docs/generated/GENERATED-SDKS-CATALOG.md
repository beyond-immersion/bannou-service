# Generated SDK Catalog

> **Source**: `docs/sdks/*.md`
> **Do not edit manually** - regenerate with `make generate-docs`

Pure-computation SDK deep dives covering Bannou's creative and infrastructure libraries.

## Sprite

### Sprite Theory SDK Deep Dive {#sprite-theory}

**Layer**: Theory | **Status**: Implemented — all types and methods from implementation map complete. 100 unit tests passing. | **Dependencies**: `BeyondImmersion.Bannou.Core` (for the shared `Vector3` primitive). No external NuGet dependencies. | **Consumers**: sprite-composer, sprite-composer-stride, future SpriteBatcher | [Implementation Map](../sdks/maps/SPRITE-THEORY.md) | [Full Deep Dive](../sdks/SPRITE-THEORY.md)

*Camera math, atlas packing, mirror optimization, normal maps, and sprite sheet JSON metadata*

sprite-theory is the theory-layer SDK for the sprite domain. It provides pure-computation primitives for defining camera rigs, computing orthographic projections, sampling animation frames, packing frames into atlas layouts (MaxRects bin-packing), generating mirror metadata from source angles, converting depth buffers to tangent-space normal maps (Sobel filtering), and serializing sprite sheet metadata to a canonical JSON format.

It follows the same pattern as music-theory, storyline-theory, and voxel-core: pure computation, zero service dependencies, deterministic, usable on both client and server. Any code that needs to define capture configurations, compute atlas layouts, generate mirror metadata, produce normal maps from depth data, or read/write sprite sheet metadata depends on this SDK.

**The theory-layer role**: sprite-theory does NOT render anything — it has no engine dependency and no pixel capture capability. It computes WHERE frames go in an atlas, WHICH angles produce mirrors, HOW depth data converts to normals, and WHAT the metadata format looks like. The actual rendering, model loading, and frame capture happen in the engine bridge (sprite-composer-stride). sprite-theory produces the mathematics and data structures that the bridge and orchestrator consume.

### Sprite Composer SDK Deep Dive {#sprite-composer}

**Layer**: Composer | **Status**: Aspirational — no code exists. | **Dependencies**: `sprite-theory` (transitively brings in `BeyondImmersion.Bannou.Core` for the shared `Vector3` primitive). | **Consumers**: sprite-composer-stride (primary bridge), future sprite-composer-godot / sprite-composer-unity bridges, future SpriteBatcher automation tool, defenders-sprite-composer Stride project | [Implementation Map](../sdks/maps/SPRITE-COMPOSER.md) | [Full Deep Dive](../sdks/SPRITE-COMPOSER.md)

*Engine-agnostic orchestrator for 3D-to-2D sprite capture — projects, capture sessions, equipment/animation configuration, preview, and atlas export*

sprite-composer is the composer-layer SDK for the sprite domain. It sits between sprite-theory (pure computation) and engine-specific bridges (sprite-composer-stride, etc.), orchestrating the full 3D-to-2D sprite capture workflow: model loading, equipment attachment, animation discovery and configuration, camera rig management, frame capture, atlas assembly, preview playback, and project persistence.

It follows the composer pattern established by scene-composer and voxel-builder: an engine-agnostic core with a command-based undo/redo stack, events for consumer integration, headless-mode support, and a pluggable engine bridge (`ISpriteComposerBridge`). The critical architectural distinction — and the reason sprite-composer is its own SDK rather than a slight variant of an existing composer — is the **bridge direction**. Scene-composer and voxel-builder push data TO the engine for display. Sprite-composer pulls rendered data FROM the engine. Every capture operation is a round-trip: configure the camera, set animation time, render to off-screen target, read pixels back.

**The sprite-composer boundary is "where does the engine stop."** Anything that requires rendering, skeletal animation evaluation, or GPU access lives in the bridge. Anything that can be reasoned about without rendering — project configuration, capture planning, atlas assembly from captured pixel data, metadata export, preview frame selection — lives in sprite-composer itself. The result is an SDK that can fully validate projects, compute capture manifests, and assemble finished atlases offline, with the bridge as a pure "render N frames at these parameters" service.

## Voxel

### Voxel Core SDK Deep Dive {#voxel-core}

**Layer**: Theory | **Status**: Aspirational — no code exists. | **Dependencies**: K4os.Compression.LZ4 (MIT) — already used by asset-bundler | [Implementation Map](../sdks/maps/VOXEL-CORE.md) | [Full Deep Dive](../sdks/VOXEL-CORE.md)

*Sparse chunk-based voxel grid primitives with meshing, voxelization, serialization (.bvox), and math — the foundation for all voxel SDKs*

voxel-core is the theory-layer SDK for the voxel domain. It provides the fundamental data structures (VoxelGrid, VoxelChunk, Voxel, Palette), spatial math (coordinates, raycasting, bounds), bidirectional conversion between voxels and meshes (meshing and voxelization), serialization (the .bvox binary format with LZ4 compression), and delta encoding for incremental persistence.

It follows the same pattern as music-theory and storyline-theory: pure computation, zero service dependencies, deterministic when applicable, usable on both client and server. Any code that needs to create, read, modify, serialize, mesh, or voxelize data depends on this SDK.

**Two conversion directions**: Meshing converts voxels→mesh (for rendering). Voxelization converts mesh→voxels or heightmap→voxels (for editing). Both are core data conversions that higher SDKs and plugins build on. The terrain overlay workflow (voxelize terrain → edit → bake back to mesh) depends on both directions living in voxel-core.

### Voxel Generator SDK Deep Dive {#voxel-generator}

**Layer**: Storyteller | **Status**: Aspirational — no code exists. | **Dependencies**: voxel-core (grid, palette, math, serialization) | **Consumers**: lib-procedural (server-side generation), lib-dungeon (chamber growth), divine actors (environmental marks), content tools (batch generation) | [Implementation Map](../sdks/maps/VOXEL-GENERATOR.md) | [Full Deep Dive](../sdks/VOXEL-GENERATOR.md)

*Deterministic procedural voxel generation — WFC, L-systems, noise, primitives, template stamping — with content-addressed caching via seed*

voxel-generator is the storyteller-layer SDK for the voxel domain. It provides deterministic procedural generation algorithms that produce VoxelGrid output from parameters and seeds: Wave Function Collapse for pattern-tiled structures, L-systems for branching organic shapes, Perlin/simplex noise for terrain, geometric primitives for building blocks, and template stamping for composing prefab pieces.

It follows the same pattern as music-storyteller and storyline-storyteller: deterministic seeded generation where `same parameters + same seed = identical output`, enabling content-addressed caching (`SHA256(generatorId + params + seed) → cached result`). The key difference is that voxel-generator produces spatial geometry rather than temporal sequences (music) or narrative structures (storyline).

All generators implement the `IVoxelGenerator` interface and produce VoxelGrid output compatible with voxel-core's meshing and serialization pipeline. The SDK has no service dependencies — lib-procedural wraps it as an HTTP API with Asset service caching.

### Voxel Builder SDK Deep Dive {#voxel-builder}

**Layer**: Composer | **Status**: Aspirational — no code exists. | **Dependencies**: voxel-core (grid, meshing, serialization), SharpGLTF.Core (MIT, glTF import) | **Consumers**: voxel-builder-stride, voxel-builder-godot, voxel-builder-unity (bridges), lib-procedural (content pipeline), Gardener/housing (shared building), lib-dungeon (dungeon editing) | [Implementation Map](../sdks/maps/VOXEL-BUILDER.md) | [Full Deep Dive](../sdks/VOXEL-BUILDER.md)

*Interactive voxel authoring with serializable operations, multi-source undo, assisted/shared building, artist tool import/export (BlockBench, MagicaVoxel, glTF), and engine bridge abstraction*

voxel-builder is the composer-layer SDK for the voxel domain. It provides the interactive editing layer on top of voxel-core: undoable operations (place, erase, fill, brush, box, mirror, rotate, copy/paste, replace), format import/export (BlockBench .bbmodel, MagicaVoxel .vox, glTF .glb, mesh), and the engine bridge abstraction (`IVoxelBuilderBridge`) that engine-specific SDKs implement.

It follows the same pattern as scene-composer: engine-agnostic core with a command-based undo/redo stack, validation, and pluggable engine bridges. The key difference is that voxel editing operates on a single grid (not a hierarchical document), so spatial composition of voxel objects happens at the Scene level — SceneComposer places voxel nodes in scene documents, VoxelBuilder edits the voxel data within each node.

**Operations are serializable and source-tagged.** Every operation can be converted to bytes for network transmission and carries a source identifier (local user, generator, remote editor). This enables two composition patterns from the same mechanism: **assisted building** (generator output flows into the builder as operations that compose with existing content) and **shared building** (multiple editors apply operations to the same grid with per-source undo). The grid is shared state; undo stacks are per-source.

### Voxel Builder Godot Bridge SDK Deep Dive {#voxel-builder-godot}

**Layer**: Bridge | **Status**: Aspirational — no code exists. | **Dependencies**: voxel-builder (→ voxel-core), Godot.NET (4.x) | [Implementation Map](../sdks/maps/VOXEL-BUILDER-GODOT.md) | [Full Deep Dive](../sdks/VOXEL-BUILDER-GODOT.md)

*Godot 4.x engine bridge for voxel rendering (per-chunk ArrayMesh), mouse-to-voxel picking, and palette atlas generation*

voxel-builder-godot is the Godot 4.x engine bridge for the voxel domain. It implements `IVoxelBuilderBridge` from voxel-builder, translating between SDK types (MeshData, VoxelCoord, Palette) and Godot types (ArrayMesh, Vector3I, StandardMaterial3D).

It follows the same pattern as scene-composer-godot: a thin translation layer that converts engine-agnostic SDK output to engine-native rendering. The bridge handles per-chunk mesh node management (creation, update, disposal), mouse ray → voxel coordinate picking (DDA raycast through the grid), and palette-based texture atlas generation for material rendering.

### Voxel Builder Stride Bridge SDK Deep Dive {#voxel-builder-stride}

**Layer**: Bridge | **Status**: Aspirational — no code exists. | **Dependencies**: voxel-builder (-> voxel-core), Stride.Engine 4.3, Stride.Rendering, Stride.Physics | [Implementation Map](../sdks/maps/VOXEL-BUILDER-STRIDE.md) | [Full Deep Dive](../sdks/VOXEL-BUILDER-STRIDE.md)

*Stride engine bridge for voxel rendering — built-in VertexPositionNormalColor, byte-color zero-copy, per-chunk Entity with MeshDraw, SetData buffer updates*

voxel-builder-stride is the **primary engine bridge** for the voxel domain. Stride is the first-priority engine, so this bridge is optimized for zero-copy paths wherever possible. The key advantage: Stride's `Color` struct is 4 bytes RGBA (byte-based), matching our `MeshData.Colors` exactly — no conversion needed for vertex colors. Stride's coordinate system (right-handed, Y-up, CCW front-face) also matches voxel-core exactly — no winding flips or axis swaps.

The bridge uses Stride's **built-in `VertexPositionNormalColor`** (28 bytes: Vector3 + Vector3 + Color) — no custom vertex struct needed. Per-vertex color is the correct rendering model for blocky voxels: each face gets a flat color from the palette. UVs and palette atlas textures are a future refinement for when per-face texture detail is desired, but are not needed for correct palette-colored voxel rendering.

Follows the same pattern as scene-composer-stride: thin translation layer converting engine-agnostic SDK output to engine-native rendering. Constructor takes `Scene`, `CameraComponent`, `GraphicsDevice`, and an optional `CommandList` provider for buffer uploads.

### Voxel Builder Unity Bridge SDK Deep Dive {#voxel-builder-unity}

**Layer**: Bridge | **Status**: Aspirational — no code exists. | **Dependencies**: voxel-builder (-> voxel-core), Unity Engine (2021.3+) | [Full Deep Dive](../sdks/VOXEL-BUILDER-UNITY.md)

*Unity engine bridge for voxel rendering — Color32 zero-copy, FlipWindingOrder for left-handed, per-chunk Mesh with 32-bit indices*

voxel-builder-unity is the Unity engine bridge for the voxel domain. Unity is the third-priority engine (after Stride and Godot), but its bridge has two notable properties: Unity's `Color32` struct is 4 bytes RGBA (byte-based) — matching our `MeshData.Colors` exactly (zero-copy, same as Stride). However, Unity uses a **left-handed** coordinate system, so the bridge must call `MeshUtility.FlipWindingOrder` on all MeshData before converting to Unity meshes.

Unity's `Mesh` API uses separate attribute arrays (like Godot, unlike Stride's interleaved structs), making the conversion from MeshData straightforward. The main Unity-specific concern is the **65,535 vertex limit per sub-mesh** when using 16-bit index buffers — the bridge opts into 32-bit indices (`IndexFormat.UInt32`) by default.

## Summary

- **SDKs in catalog**: 8
- **Domains**: Sprite, Voxel
- **Implementation maps**: 7

---

*This file is auto-generated. See [TENETS.md](../reference/TENETS.md) for architectural context.*
