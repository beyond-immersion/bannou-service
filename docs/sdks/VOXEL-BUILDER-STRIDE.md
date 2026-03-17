# Voxel Builder Stride Bridge SDK Deep Dive

> **SDK**: voxel-builder-stride (not yet created)
> **Location**: `sdks/voxel-builder-stride/` (planned)
> **Layer**: Bridge
> **Domain**: Voxel
> **Dependencies**: voxel-builder (-> voxel-core), Stride.Engine 4.3, Stride.Rendering, Stride.Physics
> **Status**: Aspirational — no code exists.
> **Implementation Map**: [docs/sdks/maps/VOXEL-BUILDER-STRIDE.md](maps/VOXEL-BUILDER-STRIDE.md)
> **Short**: Stride engine bridge for voxel rendering — built-in VertexPositionNormalColor, byte-color zero-copy, per-chunk Entity with MeshDraw, SetData buffer updates

---

## Overview

voxel-builder-stride is the **primary engine bridge** for the voxel domain. Stride is the first-priority engine, so this bridge is optimized for zero-copy paths wherever possible. The key advantage: Stride's `Color` struct is 4 bytes RGBA (byte-based), matching our `MeshData.Colors` exactly — no conversion needed for vertex colors. Stride's coordinate system (right-handed, Y-up, CCW front-face) also matches voxel-core exactly — no winding flips or axis swaps.

The bridge uses Stride's **built-in `VertexPositionNormalColor`** (28 bytes: Vector3 + Vector3 + Color) — no custom vertex struct needed. Per-vertex color is the correct rendering model for blocky voxels: each face gets a flat color from the palette. UVs and palette atlas textures are a future refinement for when per-face texture detail is desired, but are not needed for correct palette-colored voxel rendering.

Follows the same pattern as scene-composer-stride: thin translation layer converting engine-agnostic SDK output to engine-native rendering. Constructor takes `Scene`, `CameraComponent`, `GraphicsDevice`, and an optional `CommandList` provider for buffer uploads.

---

## Consumers

| Consumer | Type | Usage |
|----------|------|-------|
| Game client (Stride) | Application | Renders voxel content in-game (terrain overlays, dungeons, housing, NPC buildings) |
| Voxel editor tool (Stride) | Application | Interactive voxel editing for content authoring |
| Content preview (Stride) | Tool | Preview imported/generated voxel models |

---

## Public API Surface

| Type | Kind | Purpose |
|------|------|---------|
| `StrideVoxelBuilderBridge` | Class | IVoxelBuilderBridge implementation. Manages per-chunk Entity lifecycle and mouse picking (`ScreenToVoxel` via `Viewport.Unproject` + DDA). |
| `StrideChunkRenderer` | Internal class | Per-chunk Entity with ModelComponent. Interleaves MeshData → `VertexPositionNormalColor[]` → GPU Buffer. Handles buffer resize on Update. |
| `StrideVoxelMaterial` | Static class | Creates a vertex-color material at runtime via `ComputeVertexStreamColor` + `MaterialDiffuseMapFeature` + `Material.New()`. |
| `StrideTypeConverter` | Static class | SDK types ↔ Stride types. Color is zero-copy. Follows scene-composer-stride's using-alias pattern. |

**Removed from original design**: `StridePaletteAtlas` — not needed when using `VertexPositionNormalColor` (vertex colors carry palette data directly, no texture atlas). The atlas approach becomes relevant when/if we add a UV-based rendering path for texture detail.

---

## Data Model

### StrideVoxelBuilderBridge

```
StrideVoxelBuilderBridge : IVoxelBuilderBridge
├── scene: Scene                       // Stride scene (from constructor, same as scene-composer-stride)
├── camera: CameraComponent            // For picking (from constructor)
├── graphicsDevice: GraphicsDevice     // For buffer creation
├── commandListProvider: Func<CommandList>  // Provides CommandList for SetData uploads
├── chunkRenderers: Dictionary<ChunkCoord, StrideChunkRenderer>
├── mesher: IMesher                    // Current meshing strategy
├── sharedMaterial: Material           // Vertex-color material (created once by StrideVoxelMaterial)
├── voxelScale: float                  // World units per voxel
├── grid: VoxelGrid?                   // Reference to current grid
└── rootEntity: Entity                 // Parent entity for all chunk meshes
```

**CommandList access pattern**: Stride's `Buffer.SetData()` requires a `CommandList`, which is only available during the game update/render loop (via `Game.GraphicsContext.CommandList`). The bridge constructor takes a `Func<CommandList>` provider rather than a static CommandList reference, since the CommandList may change between frames. In a typical SyncScript, this is `() => Game.GraphicsContext.CommandList`. In tests or headless mode, a mock provider can be used.

### StrideChunkRenderer

```
StrideChunkRenderer
├── entity: Entity                     // Stride scene entity
├── modelComponent: ModelComponent     // Model with mesh + material
├── vertexBuffer: Buffer<VertexPositionNormalColor>  // GPU vertex buffer
├── indexBuffer: Buffer<uint>          // GPU index buffer (32-bit)
├── meshDraw: MeshDraw                 // Describes the draw call
└── chunkCoord: ChunkCoord             // Which chunk this renders
```

**Buffer lifecycle**: Vertex and index buffers are GPU resources that must be disposed when a chunk is removed or updated. On update, new buffers are created and old ones disposed (full re-upload pattern per Stride conventions — `SetData()` on existing buffers is the standard approach for chunk-sized meshes).

---

## Established Patterns (from scene-composer-stride)

The existing scene-composer-stride bridge establishes patterns we follow:

| Pattern | scene-composer-stride | voxel-builder-stride |
|---------|----------------------|---------------------|
| Constructor params | `Scene`, `CameraComponent`, `GraphicsDevice` | Same + `Func<CommandList>` |
| Type disambiguation | `using SdkColor = ...; using StrideColor = ...;` aliases | Same pattern |
| Entity management | `ConcurrentDictionary<Guid, Entity>` | `Dictionary<ChunkCoord, StrideChunkRenderer>` |
| Asset loading | Async via `IAssetLoader` | Not needed (mesh generated in-process) |
| Transform application | `entity.Transform.Position/Rotation/Scale` | Chunk position only (no rotation/scale per chunk) |
| Scene hierarchy | `parentEntity.AddChild(entity)` | `rootEntity.AddChild(chunkEntity)` |

**Key difference**: Scene-composer loads pre-authored models via asset pipeline. Voxel-builder generates mesh geometry from voxel data in-process. There is no async asset loading — the mesh is computed from the VoxelChunk and uploaded to GPU buffers immediately.

---

## Computation Pipeline

### Grid Load

```
OnGridLoaded(grid)
  → Store grid reference, extract voxel scale from metadata
  → Create shared vertex-color material via StrideVoxelMaterial.Create(graphicsDevice)
  → For each non-empty chunk in grid:
     CREATE StrideChunkRenderer:
       → Mesh chunk via current mesher → MeshData
       → Interleave MeshData into VertexPositionNormalColor[] (bake AO into color)
       → Create vertex buffer: Buffer.New<VertexPositionNormalColor>(graphicsDevice, vertices, BufferFlags.ShaderResource)
       → Upload: vertexBuffer.SetData(commandList, vertices)
       → Create index buffer: Buffer.New<uint>(graphicsDevice, indices, BufferFlags.ShaderResource)
       → Upload: indexBuffer.SetData(commandList, indices)
       → Create MeshDraw (PrimitiveType.TriangleList, VertexPositionNormalColor.Layout)
       → Create Mesh → Model → ModelComponent
       → Assign sharedMaterial to Model.Materials
       → Create Entity, set Transform.Position to chunk world offset
       → rootEntity.AddChild(entity)
```

### Interleaving (MeshData → VertexPositionNormalColor[])

```
CREATE vertices = new VertexPositionNormalColor[meshData.VertexCount]
For each vertex i in MeshData:
  var position = new Vector3(Vertices[i*3], Vertices[i*3+1], Vertices[i*3+2])
  var normal = new Vector3(Normals[i*3], Normals[i*3+1], Normals[i*3+2])

  // Color: zero-copy byte → Stride Color (4-byte RGBA constructor)
  byte r = Colors[i*4], g = Colors[i*4+1], b = Colors[i*4+2], a = Colors[i*4+3]

  // Bake AO into vertex color if present
  IF AmbientOcclusion is not null:
    float ao = AmbientOcclusion[i]
    r = (byte)(r * ao)
    g = (byte)(g * ao)
    b = (byte)(b * ao)

  vertices[i] = new VertexPositionNormalColor(position, normal, new Color(r, g, b, a))
```

Using built-in `VertexPositionNormalColor` (28 bytes) means no custom `IVertex` implementation, no custom `VertexDeclaration`, and compatibility with all standard Stride shaders that read `COLOR0`.

### Incremental Update

```
OnChunksModified(dirtyCoords)
  → For each dirty ChunkCoord:
     IF chunk is now empty AND renderer exists:
       Dispose renderer (remove Entity from scene, dispose GPU buffers)
     ELSE IF chunk is non-empty AND renderer exists:
       Re-mesh chunk → interleave → vertexBuffer.SetData(commandList, newVertices)
       Update meshDraw.DrawCount if vertex/index count changed
       (If count increased beyond buffer capacity: dispose old buffers, create new ones)
     ELSE IF chunk is non-empty AND no renderer:
       CREATE new StrideChunkRenderer (same as Grid Load path)
  → Also update neighbor chunk renderers (boundary faces may have changed)
```

**Buffer update strategy**: `SetData()` for full re-upload is the standard Stride pattern. Buffer mapping for partial updates exists but is rarely used — the GPU round-trip overhead makes full re-upload often faster for typical chunk sizes (~500-5000 vertices). If vertex count changes, the buffer must be recreated (Stride buffers are fixed-size at creation).

### Mouse Picking

```
ScreenToVoxel(screenPos)
  → Get Viewport from graphicsDevice.Presenter.BackBuffer dimensions
  → Unproject near point: Viewport.Unproject(
      new Vector3(screenX, screenY, 0),
      camera.ProjectionMatrix, camera.ViewMatrix, Matrix.Identity)
  → Unproject far point: same with Z=1
  → Compute ray: origin=nearPoint, direction=Normalize(farPoint-nearPoint)
  → Transform ray to grid-local space via rootEntity.Transform inverse
  → Divide by voxelScale to get voxel-space ray
  → DDA raycast through grid via VoxelRay.Cast
  → Return hit coordinate or null
```

---

## Determinism Contract

No determinism contract — rendering varies by GPU/platform.

---

## Performance Targets

| Operation | Target | Context | Notes |
|-----------|--------|---------|-------|
| Single chunk mesh update | < 2 ms | Client | Re-mesh + interleave + SetData upload |
| Full grid load (100 chunks) | < 200 ms | Client | Initial mesh generation |
| Vertex interleaving (1 chunk, ~2000 verts) | < 0.3 ms | Client | Array fill, zero-copy color |
| Color packing | ~0 | Client | Zero-copy byte[] → Color(r,g,b,a) constructor |
| Mouse picking | < 50 us | Client | Viewport.Unproject + DDA |

### Future: GPU Compute Meshing

The stride-marching-cubes reference project demonstrates GPU compute meshing via `ComputeEffectShader` with `RWStructuredBuffer` output bound directly as the vertex buffer. This bypasses CPU meshing and interleaving entirely. Architecturally compatible with our design (the bridge chooses CPU vs GPU path), but deferred until the CPU path proves to be a bottleneck.

---

## Engine-Specific Patterns

### Buffer Creation and Upload

```csharp
// Vertex buffer from interleaved struct array (built-in type, no custom vertex needed)
var vertexBuffer = Buffer.New<VertexPositionNormalColor>(
    graphicsDevice, vertexCount,
    BufferFlags.ShaderResource);
vertexBuffer.SetData(commandList, vertices);

// Index buffer (32-bit uint)
var indexBuffer = Buffer.New<uint>(
    graphicsDevice, indexCount,
    BufferFlags.ShaderResource);
indexBuffer.SetData(commandList, indices);

// Assemble MeshDraw
var meshDraw = new MeshDraw
{
    PrimitiveType = PrimitiveType.TriangleList,
    DrawCount = indexCount,
    VertexBuffers = new[] {
        new VertexBufferBinding(vertexBuffer, VertexPositionNormalColor.Layout, vertexCount)
    },
    IndexBuffer = new IndexBufferBinding(indexBuffer, is32Bit: true, indexCount)
};
```

### Entity Assembly

```csharp
// Create mesh → model → entity
var mesh = new Mesh { Draw = meshDraw, MaterialIndex = 0 };
var model = new Model { Meshes = { mesh } };
model.Materials.Add(sharedMaterial);

var entity = new Entity($"Chunk_{coord.X}_{coord.Y}_{coord.Z}");
entity.GetOrCreate<ModelComponent>().Model = model;
entity.Transform.Position = new Vector3(
    coord.X * 16 * voxelScale,
    coord.Y * 16 * voxelScale,
    coord.Z * 16 * voxelScale);
rootEntity.AddChild(entity);
```

### Type Converter (using-alias pattern from scene-composer-stride)

```csharp
using SdkColor = BeyondImmersion.Bannou.VoxelCore.Grid.Color;
using StrideColor = Stride.Core.Mathematics.Color;
using StrideVec3 = Stride.Core.Mathematics.Vector3;
using VoxelCoord = BeyondImmersion.Bannou.VoxelCore.Math.VoxelCoord;

// SDK Color → Stride Color (zero-copy: both are 4 bytes RGBA)
public static StrideColor ToStride(this SdkColor c) => new(c.R, c.G, c.B, c.A);

// SDK VoxelCoord → Stride Vector3 (with scale)
public static StrideVec3 ToStride(this VoxelCoord c, float scale) =>
    new(c.X * scale, c.Y * scale, c.Z * scale);
```

---

## Known Quirks & Caveats

#### Intentional Quirks (Documented Behavior)

- **Uses built-in `VertexPositionNormalColor`, not a custom vertex struct.** The earlier design proposed a custom 36-byte struct with UV. Research confirmed Stride has a built-in 28-byte `VertexPositionNormalColor` in `Stride.Graphics` that is sufficient for palette-colored voxel rendering. Per-vertex color IS the correct rendering model for blocky voxels. A palette atlas texture (requiring UVs) is a future refinement for per-face texture detail — not needed for correct initial rendering.

- **AO baked into vertex colors.** Stride's standard material pipeline doesn't expose custom per-vertex attributes without a custom SDSL shader. AO is multiplied into the RGB vertex color during interleaving. Visually correct — concave corners darken, exposed faces stay bright. If future use cases need separate AO (dynamic time-of-day scaling), a custom shader with an extra vertex attribute would be needed.

- **Full buffer re-upload on chunk update, not partial mapping.** `SetData()` re-uploads the entire vertex array. This is the standard Stride pattern and is fast enough for chunk-sized meshes (~500-5000 vertices). Buffer mapping for partial updates exists in Stride but is rarely used and adds complexity. If a chunk's vertex count changes, the buffer must be recreated anyway (Stride buffers are fixed-size at creation).

#### Design Considerations (Requires Planning)

- **Runtime material creation for vertex colors — RESOLVED**: Stride's `ComputeVertexStreamColor` class (in `Stride.Rendering.Materials.ComputeColors`) reads the `COLOR0` vertex attribute via `ColorVertexStreamDefinition`. Wrapping it in `MaterialDiffuseMapFeature` and compiling via `Material.New(GraphicsDevice, MaterialDescriptor)` produces a fully functional vertex-color material at runtime — no Game Studio assets or custom shaders needed. Stride's shader compiler generates the correct `ComputeColorFromStream.sdsl` shader that reads `streams.LocalColor` and saturates to [0,1]. The `ComputeVertexStreamColor` implements `IComputeColor`, so it can be plugged into any material feature slot (diffuse, emissive, etc.). Future MaterialType support (Metal=metallic, Glass=transparent, Emit=emissive) would use additional `ComputeVertexStreamColor` instances or computed values in the corresponding material feature slots.

- **Concave collision for voxel chunks**: `ConvexHullColliderShape` wraps the mesh in a convex hull — wrong for voxel terrain where the player should collide with the actual surface, not the bounding hull. Stride's Bepu Physics backend may support triangle mesh static colliders (`StaticMeshColliderShape` or similar), but this needs investigation. Alternative: DDA raycast for mouse picking (no physics collision needed for voxel selection), with physics collision deferred to a separate collider mesh if gameplay requires it.

- **net10.0 target framework**: Stride 4.3 requires net10.0 (with `-windows` TFM on Windows for GPU API access, plain net10.0 on other platforms for CI). The csproj must use conditional `TargetFramework` like scene-composer-stride. This means voxel-builder-stride has a different TFM from voxel-core (net8.0/net9.0) — the project reference works because net10.0 can reference net8.0/net9.0 assemblies.

---

## Open Questions

1. **Concave static collision**: Does Stride's Bepu Physics support `MeshColliderShape` or `TriangleMeshColliderShape` for static colliders from vertex/index data? If not, the alternative is no engine-level collision and purely SDK-side DDA raycasting for voxel selection. Gameplay collision (character walking on voxel terrain) would then need a heightmap-based collider or manual collision from the voxel grid data. Investigation path: `~/repos/stride/sources/engine/Stride.Physics/` for collider shape types.

2. **Buffer disposal timing**: When a chunk is updated (new mesh generated), the old GPU buffers must be disposed. Can buffers be disposed immediately after creating new ones, or must disposal wait until the GPU has finished rendering the previous frame? The march-cubes example disposes and recreates buffers without explicit synchronization — suggesting Stride handles deferred resource destruction internally. Needs verification against `~/repos/stride/sources/engine/Stride.Graphics/GraphicsResource.cs`.

3. **Multiple materials per chunk**: Voxel chunks with both opaque and transparent voxels (Glass MaterialType) may need separate draw calls with different materials (one opaque, one transparent with alpha blending). This is a future concern — initial implementation uses a single opaque material for all chunks.

## Resolved Questions

1. **Vertex-color material API — RESOLVED**: `ComputeVertexStreamColor` with `ColorVertexStreamDefinition(0)` wrapped in `MaterialDiffuseMapFeature`, compiled via `Material.New(GraphicsDevice, MaterialDescriptor)`. No custom shaders or Game Studio assets needed. See implementation map § StrideVoxelMaterial.Create for full pseudo-code.

---

## Work Tracking

No work items yet — SDK is pre-implementation.
