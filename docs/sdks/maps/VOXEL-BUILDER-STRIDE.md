# Voxel Builder Stride Bridge SDK Implementation Map

> **SDK**: voxel-builder-stride
> **Location**: `sdks/voxel-builder-stride/`
> **Layer**: Bridge
> **Domain**: Voxel
> **Deep Dive**: [docs/sdks/VOXEL-BUILDER-STRIDE.md](../VOXEL-BUILDER-STRIDE.md)
> **Status**: Aspirational — pseudo-code represents intended behavior, not verified implementation. Open questions noted inline.

---

| Field | Value |
|-------|-------|
| SDK | voxel-builder-stride |
| Layer | Bridge |
| Public Types | 5 (3 classes, 1 static class, 1 static utility) |
| Public Methods | ~12 |
| Dependencies | voxel-builder (→ voxel-core), Stride.Engine 4.3, Stride.Rendering, Stride.Physics |
| Deterministic | No (rendering varies by GPU/platform) |
| Allocation-Free Hot Paths | None (GPU buffer upload always allocates) |

---

## Data Structures

### StrideVoxelBuilderBridge

**Kind**: Class implements IVoxelBuilderBridge, IDisposable
**Thread Safety**: Main thread only (Stride scene tree constraint)

| Field | Type | Purpose |
|-------|------|---------|
| `scene` | `Scene` | Stride scene to add chunk entities to |
| `camera` | `CameraComponent` | For mouse picking (Viewport.Unproject) |
| `graphicsDevice` | `GraphicsDevice` | For buffer and material creation |
| `commandListProvider` | `Func<CommandList>` | Provides current-frame CommandList for SetData |
| `chunkRenderers` | `Dictionary<ChunkCoord, StrideChunkRenderer>` | Active chunk mesh entities |
| `mesher` | `IMesher` | Current meshing strategy |
| `sharedMaterial` | `Material` | Vertex-color material (created once) |
| `voxelScale` | `float` | World units per voxel |
| `grid` | `VoxelGrid?` | Reference to current grid |
| `rootEntity` | `Entity` | Parent entity for all chunk meshes |

### StrideChunkRenderer

**Kind**: Internal class, IDisposable
**Thread Safety**: Main thread only

| Field | Type | Purpose |
|-------|------|---------|
| `entity` | `Entity` | Stride scene entity |
| `modelComponent` | `ModelComponent` | Model with mesh + material |
| `vertexBuffer` | `Buffer<VertexPositionNormalColor>` | GPU vertex buffer |
| `indexBuffer` | `Buffer<uint>` | GPU index buffer (32-bit) |
| `meshDraw` | `MeshDraw` | Draw call descriptor |
| `chunkCoord` | `ChunkCoord` | Which chunk this renders |
| `vertexCount` | `int` | Current buffer capacity (for resize detection) |

---

## Dependencies

| Dependency | Type | Usage |
|------------|------|-------|
| voxel-builder | SDK (project ref) | IVoxelBuilderBridge, VoxelBuilder, MeshData |
| voxel-core | SDK (transitive) | VoxelGrid, VoxelChunk, Palette, VoxelCoord, ChunkCoord, IMesher, MeshingOptions, VoxelRay |
| Stride.Engine | NuGet 4.3 | Entity, Scene, CameraComponent, ModelComponent, TransformComponent, SyncScript |
| Stride.Rendering | NuGet 4.3 | Material, MaterialDescriptor, Model, Mesh, MeshDraw |
| Stride.Graphics | NuGet 4.3 (transitive) | GraphicsDevice, Buffer, VertexPositionNormalColor, CommandList, PrimitiveType |
| Stride.Physics | NuGet 4.3 | Simulation.Raycast (future: collision), StaticColliderComponent |

---

## API Index

#### StrideVoxelBuilderBridge

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| Constructor | `(Scene, CameraComponent, GraphicsDevice, Func<CommandList>, IMesher?)` | N/A | Minimal | Creates root entity + shared material |
| OnGridLoaded | `(VoxelGrid) → void` | No | Allocating | Full mesh rebuild, all chunks |
| OnChunksModified | `(IReadOnlySet<ChunkCoord>) → void` | No | Allocating | Incremental update, dirty chunks + neighbors |
| OnPaletteChanged | `(Palette) → void` | No | Allocating | Re-interleave all chunks (colors changed) |
| ScreenToVoxel | `((float,float)) → VoxelCoord?` | No | Free | Viewport.Unproject + DDA |
| SetMesher | `(IMesher) → void` | N/A | Free | Swap meshing strategy |
| Dispose | `() → void` | N/A | Free | Cleanup all entities + GPU buffers |

#### StrideChunkRenderer

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| Create | `(ChunkCoord, MeshData, GraphicsDevice, CommandList, Material, float scale) → StrideChunkRenderer` | No | Allocating | Factory: interleave + upload + entity |
| Update | `(MeshData, CommandList) → void` | No | Allocating | Re-interleave + SetData (or recreate if size changed) |
| Dispose | `() → void` | N/A | Free | Remove entity, dispose GPU buffers |

#### StrideVoxelMaterial

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| Create | `(GraphicsDevice) → Material` | No | Allocating | **OPEN QUESTION**: exact MaterialDescriptor for vertex colors |

#### StrideTypeConverter

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| ToStride | `(SdkColor) → StrideColor` | Yes | Free | Zero-copy byte → byte |
| ToStride | `(VoxelCoord, float scale) → Vector3` | Yes | Free | Int → float with scale |

---

## Methods

### StrideVoxelBuilderBridge.Constructor
`(scene: Scene, camera: CameraComponent, graphicsDevice: GraphicsDevice, commandListProvider: Func<CommandList>, mesher: IMesher?)`

VALIDATE scene is not null
VALIDATE camera is not null
VALIDATE graphicsDevice is not null
VALIDATE commandListProvider is not null
SET this.scene ← scene
SET this.camera ← camera
SET this.graphicsDevice ← graphicsDevice
SET this.commandListProvider ← commandListProvider
SET this.mesher ← mesher ?? new GreedyMesher()
// Create shared vertex-color material (OPEN QUESTION: exact API)
SET sharedMaterial ← StrideVoxelMaterial.Create(graphicsDevice)
// Create root entity as parent for all chunks
SET rootEntity ← new Entity("VoxelRoot")
scene.Entities.Add(rootEntity)

---

### StrideVoxelBuilderBridge.OnGridLoaded
`(grid: VoxelGrid) → void`

// Dispose all existing chunk renderers
FOREACH renderer in chunkRenderers.Values
  renderer.Dispose()
chunkRenderers.Clear()

SET this.grid ← grid
SET voxelScale ← grid.Metadata.VoxelScale

COMPUTE commandList ← commandListProvider()
COMPUTE options ← new MeshingOptions(VoxelScale: voxelScale)
COMPUTE neighbors ← new VoxelChunk?[6]

FOREACH (coord, chunk) in grid.EnumerateChunks()
  // Gather 6 neighbor chunks for boundary face culling
  neighbors[0] ← grid.GetChunk(new ChunkCoord(coord.X + 1, coord.Y, coord.Z))
  neighbors[1] ← grid.GetChunk(new ChunkCoord(coord.X - 1, coord.Y, coord.Z))
  neighbors[2] ← grid.GetChunk(new ChunkCoord(coord.X, coord.Y + 1, coord.Z))
  neighbors[3] ← grid.GetChunk(new ChunkCoord(coord.X, coord.Y - 1, coord.Z))
  neighbors[4] ← grid.GetChunk(new ChunkCoord(coord.X, coord.Y, coord.Z + 1))
  neighbors[5] ← grid.GetChunk(new ChunkCoord(coord.X, coord.Y, coord.Z - 1))
  COMPUTE meshData ← mesher.Mesh(chunk, neighbors, grid.Palette, options)
  IF meshData.VertexCount == 0: CONTINUE
  CREATE renderer ← StrideChunkRenderer.Create(
    coord, meshData, graphicsDevice, commandList, sharedMaterial, voxelScale)
  rootEntity.AddChild(renderer.entity)
  SET chunkRenderers[coord] ← renderer

---

### StrideVoxelBuilderBridge.OnChunksModified
`(dirtyCoords: IReadOnlySet<ChunkCoord>) → void`

COMPUTE commandList ← commandListProvider()
COMPUTE options ← new MeshingOptions(VoxelScale: voxelScale)
COMPUTE neighbors ← new VoxelChunk?[6]

// Collect all coords to update (dirty + their 6 neighbors for boundary face changes)
CREATE updateSet ← new HashSet<ChunkCoord>(dirtyCoords)
FOREACH coord in dirtyCoords
  FOREACH direction in 6 axis offsets
    updateSet.Add(coord + direction)

FOREACH coord in updateSet
  COMPUTE chunk ← grid.GetChunk(coord)

  IF chunk is null OR chunk.IsEmpty
    // Chunk became empty — remove renderer
    IF chunkRenderers.TryGetValue(coord, out renderer)
      rootEntity.RemoveChild(renderer.entity) // correct? or scene.Entities.Remove?
      renderer.Dispose()
      chunkRenderers.Remove(coord)

  ELSE IF chunkRenderers.TryGetValue(coord, out renderer)
    // Chunk exists and has renderer — update mesh
    GatherNeighbors(coord, neighbors)
    COMPUTE meshData ← mesher.Mesh(chunk, neighbors, grid.Palette, options)
    IF meshData.VertexCount == 0
      rootEntity.RemoveChild(renderer.entity)
      renderer.Dispose()
      chunkRenderers.Remove(coord)
    ELSE
      renderer.Update(meshData, commandList)

  ELSE
    // Chunk exists but no renderer — create new
    GatherNeighbors(coord, neighbors)
    COMPUTE meshData ← mesher.Mesh(chunk, neighbors, grid.Palette, options)
    IF meshData.VertexCount > 0
      CREATE renderer ← StrideChunkRenderer.Create(
        coord, meshData, graphicsDevice, commandList, sharedMaterial, voxelScale)
      rootEntity.AddChild(renderer.entity)
      SET chunkRenderers[coord] ← renderer

---

### StrideVoxelBuilderBridge.OnPaletteChanged
`(palette: Palette) → void`

// Palette colors changed — must re-interleave ALL chunks because vertex colors are baked
// (No separate palette texture to update — colors live in vertex data)
COMPUTE commandList ← commandListProvider()
COMPUTE options ← new MeshingOptions(VoxelScale: voxelScale)
COMPUTE neighbors ← new VoxelChunk?[6]

FOREACH (coord, renderer) in chunkRenderers
  COMPUTE chunk ← grid.GetChunk(coord)
  IF chunk is null: CONTINUE
  GatherNeighbors(coord, neighbors)
  COMPUTE meshData ← mesher.Mesh(chunk, neighbors, grid.Palette, options)
  IF meshData.VertexCount > 0
    renderer.Update(meshData, commandList)

---

### StrideVoxelBuilderBridge.ScreenToVoxel
`(screenPos: (float, float)) → VoxelCoord?`

IF grid is null: RETURN null

// Create viewport from back buffer dimensions
COMPUTE viewport ← new Viewport(0, 0,
  graphicsDevice.Presenter.BackBuffer.Width,
  graphicsDevice.Presenter.BackBuffer.Height)

// Unproject screen position to near/far world points
camera.Update()  // ensure ViewProjectionMatrix is current
COMPUTE nearPoint ← viewport.Unproject(
  new Vector3(screenPos.Item1, screenPos.Item2, 0),
  camera.ProjectionMatrix, camera.ViewMatrix, Matrix.Identity)
COMPUTE farPoint ← viewport.Unproject(
  new Vector3(screenPos.Item1, screenPos.Item2, 1),
  camera.ProjectionMatrix, camera.ViewMatrix, Matrix.Identity)

COMPUTE rayOrigin ← nearPoint
COMPUTE rayDirection ← Vector3.Normalize(farPoint - nearPoint)

// Transform ray to grid-local space
COMPUTE worldToLocal ← rootEntity.Transform.WorldMatrix inverse
COMPUTE localOrigin ← Vector3.TransformCoordinate(rayOrigin, worldToLocal)
COMPUTE localDirection ← Vector3.TransformNormal(rayDirection, worldToLocal)

// Convert to voxel-space (divide by voxelScale)
COMPUTE voxelOriginX ← localOrigin.X / voxelScale
COMPUTE voxelOriginY ← localOrigin.Y / voxelScale
COMPUTE voxelOriginZ ← localOrigin.Z / voxelScale

// DDA raycast through grid (delegates to voxel-core's VoxelRay)
CREATE ray ← VoxelRay.Create(
  new VoxelCoord((int)voxelOriginX, (int)voxelOriginY, (int)voxelOriginZ),
  localDirection.X, localDirection.Y, localDirection.Z)
COMPUTE hit ← ray.Cast(grid, maxSteps: 200)

IF hit is null: RETURN null
RETURN hit.Value.Hit

---

### StrideChunkRenderer.Create
`(coord: ChunkCoord, meshData: MeshData, graphicsDevice: GraphicsDevice, commandList: CommandList, material: Material, voxelScale: float) → StrideChunkRenderer`

// Interleave MeshData into VertexPositionNormalColor[]
CREATE vertices ← new VertexPositionNormalColor[meshData.VertexCount]
ITERATE i FROM 0 TO meshData.VertexCount - 1
  COMPUTE position ← new Vector3(
    meshData.Vertices[i*3], meshData.Vertices[i*3+1], meshData.Vertices[i*3+2])
  COMPUTE normal ← new Vector3(
    meshData.Normals[i*3], meshData.Normals[i*3+1], meshData.Normals[i*3+2])

  // Color: zero-copy from MeshData.Colors (byte[]) to Stride Color (4 bytes RGBA)
  COMPUTE r ← meshData.Colors?[i*4] ?? 255
  COMPUTE g ← meshData.Colors?[i*4+1] ?? 255
  COMPUTE b ← meshData.Colors?[i*4+2] ?? 255
  COMPUTE a ← meshData.Colors?[i*4+3] ?? 255

  // Bake AO into vertex color
  IF meshData.AmbientOcclusion is not null
    COMPUTE ao ← meshData.AmbientOcclusion[i]
    r ← (byte)(r * ao)
    g ← (byte)(g * ao)
    b ← (byte)(b * ao)

  vertices[i] ← new VertexPositionNormalColor(position, normal, new Color(r, g, b, a))

// Convert indices to uint[] (MeshData.Indices is int[])
CREATE indices ← new uint[meshData.Indices.Length]
ITERATE i FROM 0 TO meshData.Indices.Length - 1
  indices[i] ← (uint)meshData.Indices[i]

// Create GPU buffers
COMPUTE vertexBuffer ← Buffer.New<VertexPositionNormalColor>(
  graphicsDevice, vertices.Length, BufferFlags.ShaderResource)
vertexBuffer.SetData(commandList, vertices)

COMPUTE indexBuffer ← Buffer.New<uint>(
  graphicsDevice, indices.Length, BufferFlags.ShaderResource)
indexBuffer.SetData(commandList, indices)

// Assemble MeshDraw
COMPUTE meshDraw ← new MeshDraw {
  PrimitiveType = PrimitiveType.TriangleList,
  DrawCount = indices.Length,
  VertexBuffers = new[] {
    new VertexBufferBinding(vertexBuffer, VertexPositionNormalColor.Layout, vertices.Length)
  },
  IndexBuffer = new IndexBufferBinding(indexBuffer, is32Bit: true, indices.Length)
}

// Create Mesh → Model → Entity
COMPUTE mesh ← new Mesh { Draw = meshDraw, MaterialIndex = 0 }
COMPUTE model ← new Model { Meshes = { mesh } }
model.Materials.Add(material)

CREATE entity ← new Entity($"Chunk_{coord.X}_{coord.Y}_{coord.Z}")
entity.GetOrCreate<ModelComponent>().Model ← model
entity.Transform.Position ← new Vector3(
  coord.X * 16 * voxelScale,
  coord.Y * 16 * voxelScale,
  coord.Z * 16 * voxelScale)

RETURN new StrideChunkRenderer(entity, vertexBuffer, indexBuffer, meshDraw, coord, vertices.Length)

---

### StrideChunkRenderer.Update
`(meshData: MeshData, commandList: CommandList) → void`

// Re-interleave (same logic as Create)
CREATE vertices ← InterleaveVertices(meshData) // same interleaving as Create
CREATE indices ← ConvertIndices(meshData)

// If vertex count fits in existing buffer, SetData in-place
IF vertices.Length <= vertexCount AND indices.Length <= indexBuffer.ElementCount
  vertexBuffer.SetData(commandList, vertices)
  indexBuffer.SetData(commandList, indices)
  meshDraw.DrawCount ← indices.Length
ELSE
  // Buffer too small — dispose old, create new
  vertexBuffer.Dispose()
  indexBuffer.Dispose()
  SET vertexBuffer ← Buffer.New<VertexPositionNormalColor>(
    entity.Get<ModelComponent>().Model./* graphicsDevice access? */, vertices.Length, BufferFlags.ShaderResource)
  // NOTE: StrideChunkRenderer needs GraphicsDevice access for buffer recreation.
  // Either store as field or pass through Update parameter.
  vertexBuffer.SetData(commandList, vertices)
  indexBuffer ← Buffer.New<uint>(graphicsDevice, indices.Length, BufferFlags.ShaderResource)
  indexBuffer.SetData(commandList, indices)
  // Rebuild MeshDraw bindings
  meshDraw.VertexBuffers = new[] {
    new VertexBufferBinding(vertexBuffer, VertexPositionNormalColor.Layout, vertices.Length)
  }
  meshDraw.IndexBuffer ← new IndexBufferBinding(indexBuffer, is32Bit: true, indices.Length)
  meshDraw.DrawCount ← indices.Length
  SET vertexCount ← vertices.Length

---

### StrideChunkRenderer.Dispose
`() → void`

// Remove entity from scene (parent removes child)
entity.Scene?.Entities.Remove(entity)
// Dispose GPU resources
vertexBuffer?.Dispose()
indexBuffer?.Dispose()

---

### StrideVoxelMaterial.Create
`(graphicsDevice: GraphicsDevice) → Material`

// RESOLVED: Stride has ComputeVertexStreamColor in Stride.Rendering.Materials.ComputeColors
// that reads the COLOR0 vertex attribute via ColorVertexStreamDefinition.
// No custom shaders needed — Stride's shader compiler generates the correct code from the
// material descriptor. The shader (ComputeColorFromStream.sdsl) reads streams.LocalColor
// from the specified semantic and outputs it as diffuse albedo.

// 1. Create vertex stream color compute that reads COLOR0
CREATE vertexColorCompute ← new ComputeVertexStreamColor()
SET vertexColorCompute.Stream ← new ColorVertexStreamDefinition(0)  // index 0 = COLOR0

// 2. Create diffuse feature wrapping the vertex color compute
CREATE diffuseFeature ← new MaterialDiffuseMapFeature(vertexColorCompute)

// 3. Assemble material descriptor
CREATE descriptor ← new MaterialDescriptor()
descriptor.Attributes.Diffuse ← diffuseFeature
descriptor.Attributes.DiffuseModel ← new MaterialDiffuseLambertModelFeature()

// 4. Compile material
RETURN Material.New(graphicsDevice, descriptor)

---

## Algorithms

No non-trivial algorithms in this SDK. Meshing is delegated to voxel-core's IMesher implementations. Mouse picking delegates to voxel-core's VoxelRay.Cast. This SDK is a translation, buffer management, and entity lifecycle layer.

---

## Serialization Formats

No custom serialization formats.

---

## Open Questions Requiring Investigation

### Q1: Runtime vertex-color material creation — RESOLVED

**Answer**: `ComputeVertexStreamColor` (in `Stride.Rendering.Materials.ComputeColors`) reads the `COLOR0` vertex attribute via `ColorVertexStreamDefinition(0)`. Wrapping it in `MaterialDiffuseMapFeature` and compiling via `Material.New(GraphicsDevice, MaterialDescriptor)` produces a fully functional vertex-color material at runtime. No custom shaders or Game Studio assets needed. Stride's shader compiler generates `ComputeColorFromStream.sdsl` which reads `streams.LocalColor` and saturates to [0,1]. See § StrideVoxelMaterial.Create for full pseudo-code.

### Q2: Concave static collision from voxel mesh

**What we need**: Physics collision shape from triangle mesh (not convex hull) for voxel terrain chunks.

**Investigation paths**:
- Check if `StaticMeshColliderShape` or `TriangleMeshColliderShape` exists in Stride.Physics
- Look at Bepu Physics integration for mesh shapes
- Alternative: no engine collision, SDK-side DDA raycast only (sufficient for voxel picking, not for character physics)

**Where to look**: `~/repos/stride/sources/engine/Stride.Physics/` for collider shape types

### Q3: Buffer disposal synchronization

**What we need**: Confirmation that disposing a `Buffer<T>` while the GPU may still be rendering a frame that references it is safe (deferred destruction) or unsafe (must wait).

**Investigation paths**:
- Check Stride's `GraphicsResource.Dispose()` implementation
- Look for deferred resource destruction queue in the graphics backend
- The march-cubes example disposes and recreates buffers without explicit synchronization — suggests deferred destruction is handled internally

**Where to look**: `~/repos/stride/sources/engine/Stride.Graphics/` for `Buffer.cs` and `GraphicsResource.cs`
