# Voxel Builder Godot Bridge SDK Implementation Map

> **SDK**: voxel-builder-godot
> **Location**: `sdks/voxel-builder-godot/`
> **Layer**: Bridge
> **Domain**: Voxel
> **Deep Dive**: [docs/sdks/VOXEL-BUILDER-GODOT.md](../VOXEL-BUILDER-GODOT.md)
> **Status**: Aspirational — pseudo-code represents intended behavior, not verified implementation

---

| Field | Value |
|-------|-------|
| SDK | voxel-builder-godot |
| Layer | Bridge |
| Public Types | 5 (3 classes, 1 static class, 1 class inheriting from interface) |
| Public Methods | ~15 |
| Dependencies | voxel-builder (→ voxel-core), Godot.NET 4.x |
| Deterministic | No (rendering varies by GPU/platform) |
| Allocation-Free Hot Paths | ScreenToVoxel inner DDA loop (delegates to VoxelRay in voxel-core) |

---

## Data Structures

### GodotVoxelBuilderBridge

**Kind**: Class implements IVoxelBuilderBridge
**Thread Safety**: Main thread only (Godot scene tree constraint)

| Field | Type | Purpose |
|-------|------|---------|
| `rootNode` | `Node3D` | Parent scene node for all chunk meshes |
| `chunkRenderers` | `Dictionary<ChunkCoord, GodotChunkRenderer>` | Active chunk mesh nodes |
| `mesher` | `IMesher` | Current meshing strategy |
| `paletteAtlas` | `GodotPaletteAtlas` | Current palette texture + material |
| `voxelScale` | `float` | World units per voxel |
| `grid` | `VoxelGrid?` | Reference to current grid |

### GodotChunkRenderer

**Kind**: Class
**Thread Safety**: Main thread only

| Field | Type | Purpose |
|-------|------|---------|
| `meshInstance` | `MeshInstance3D` | Godot scene node |
| `arrayMesh` | `ArrayMesh` | Generated mesh |
| `chunkCoord` | `ChunkCoord` | Which chunk this renders |
| `collisionShape` | `StaticBody3D?` | Optional collision (null if disabled) |

### GodotPaletteAtlas

**Kind**: Class
**Thread Safety**: Main thread only

| Field | Type | Purpose |
|-------|------|---------|
| `texture` | `ImageTexture` | 16x16 atlas (256 palette entries) |
| `material` | `StandardMaterial3D` | Shared material using atlas |

### GodotTypeConverter

**Kind**: Static class

No fields — pure conversion methods.

---

## Dependencies

| Dependency | Type | Usage |
|------------|------|-------|
| voxel-builder | SDK (project ref) | IVoxelBuilderBridge, VoxelBuilder, operations |
| voxel-core | SDK (transitive) | VoxelGrid, VoxelChunk, MeshData, VoxelCoord, ChunkCoord, IMesher, VoxelRay |
| GodotSharp | NuGet (Godot 4.x) | Node3D, MeshInstance3D, ArrayMesh, StandardMaterial3D, Vector3, Vector3I |

---

## API Index

#### GodotVoxelBuilderBridge

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| Constructor | `(Node3D rootNode, IMesher? mesher)` | N/A | Minimal | |
| OnGridLoaded | `(VoxelGrid) → void` | No | Allocating | Full mesh rebuild |
| OnChunksModified | `(IReadOnlySet<ChunkCoord>) → void` | No | Allocating | Incremental update |
| OnPaletteChanged | `(Palette) → void` | No | Allocating | Atlas rebuild |
| ScreenToVoxel | `((float,float) screenPos) → VoxelCoord?` | No | Free | Mouse picking |
| SetMesher | `(IMesher) → void` | N/A | Free | Swap meshing strategy |
| Dispose | `() → void` | N/A | Free | Cleanup all nodes |

#### GodotChunkRenderer

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| Create | `(ChunkCoord, VoxelChunk, neighbors, IMesher, Palette, float scale, Material) → GodotChunkRenderer` | No | Allocating | Factory |
| UpdateMesh | `(VoxelChunk, neighbors, IMesher, Palette, float scale) → void` | No | Allocating | Re-mesh dirty chunk |
| Dispose | `() → void` | N/A | Free | Remove node from tree |

#### GodotPaletteAtlas

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| Create | `(Palette) → GodotPaletteAtlas` | No | Allocating | Factory |
| Rebuild | `(Palette) → void` | No | Allocating | Regenerate texture |

#### GodotTypeConverter

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| ToVector3 | `(VoxelCoord, float scale) → Vector3` | Yes | Free | Coord → world position |
| ToVector3I | `(VoxelCoord) → Vector3I` | Yes | Free | Direct mapping |
| ToVoxelCoord | `(Vector3I) → VoxelCoord` | Yes | Free | Direct mapping |
| ToGodotColor | `(Color) → Godot.Color` | Yes | Free | Byte → float |
| ToSdkColor | `(Godot.Color) → Color` | Yes | Free | Float → byte |

---

## Methods

### GodotVoxelBuilderBridge.OnGridLoaded
`(grid: VoxelGrid) → void`

// Dispose all existing chunk renderers
FOREACH renderer in chunkRenderers.Values
  renderer.Dispose()
chunkRenderers.Clear()

SET this.grid ← grid
SET voxelScale ← grid.Metadata.VoxelScale ?? 0.25f

// Rebuild palette atlas
IF paletteAtlas is not null
  paletteAtlas.Rebuild(grid.Palette)
ELSE
  SET paletteAtlas ← GodotPaletteAtlas.Create(grid.Palette)

// Create mesh nodes for all non-empty chunks
FOREACH (coord, chunk) in grid.EnumerateChunks()
  COMPUTE neighbors ← getNeighborChunks(grid, coord)
  CREATE renderer ← GodotChunkRenderer.Create(coord, chunk, neighbors, mesher, grid.Palette, voxelScale, paletteAtlas.material)
  // Position node at chunk world position
  renderer.meshInstance.Position ← GodotTypeConverter.ToVector3(
    CREATE VoxelCoord(coord.X * 16, coord.Y * 16, coord.Z * 16), voxelScale)
  rootNode.AddChild(renderer.meshInstance)
  SET chunkRenderers[coord] ← renderer

---

### GodotVoxelBuilderBridge.OnChunksModified
`(dirtyCoords: IReadOnlySet<ChunkCoord>) → void`

FOREACH coord in dirtyCoords
  COMPUTE chunk ← grid.GetChunk(coord)

  IF chunk is null OR chunk.IsEmpty()
    // Chunk became empty — remove renderer
    IF chunkRenderers.TryGetValue(coord, out renderer)
      renderer.Dispose()
      chunkRenderers.Remove(coord)

  ELSE IF chunkRenderers.TryGetValue(coord, out renderer)
    // Chunk exists and has renderer — update mesh
    COMPUTE neighbors ← getNeighborChunks(grid, coord)
    renderer.UpdateMesh(chunk, neighbors, mesher, grid.Palette, voxelScale)

  ELSE
    // Chunk exists but no renderer — create new (first voxel placed in empty chunk)
    COMPUTE neighbors ← getNeighborChunks(grid, coord)
    CREATE renderer ← GodotChunkRenderer.Create(coord, chunk, neighbors, mesher, grid.Palette, voxelScale, paletteAtlas.material)
    renderer.meshInstance.Position ← GodotTypeConverter.ToVector3(
      CREATE VoxelCoord(coord.X * 16, coord.Y * 16, coord.Z * 16), voxelScale)
    rootNode.AddChild(renderer.meshInstance)
    SET chunkRenderers[coord] ← renderer

  // Also update neighbor chunk renderers (boundary faces may have changed)
  FOREACH neighborCoord in 6-connected(coord)
    IF chunkRenderers.TryGetValue(neighborCoord, out neighborRenderer)
      COMPUTE neighborChunk ← grid.GetChunk(neighborCoord)
      IF neighborChunk is not null
        COMPUTE neighborNeighbors ← getNeighborChunks(grid, neighborCoord)
        neighborRenderer.UpdateMesh(neighborChunk, neighborNeighbors, mesher, grid.Palette, voxelScale)

---

### GodotVoxelBuilderBridge.OnPaletteChanged
`(palette: Palette) → void`

paletteAtlas.Rebuild(palette)
// All chunk renderers share the material, so atlas update propagates automatically
// However, UV coordinates in meshes reference palette indices — if palette reindexed,
// all meshes need rebuild. If only colors changed (same indices), no mesh rebuild needed.
// For simplicity: assume palette color changes only (no reindexing) — atlas rebuild suffices.

---

### GodotVoxelBuilderBridge.ScreenToVoxel
`(screenPos: (float, float)) → VoxelCoord?`

// Get camera from viewport
COMPUTE camera ← rootNode.GetViewport().GetCamera3D()
IF camera is null
  RETURN null

// Project screen position to world ray
COMPUTE rayOrigin ← camera.ProjectRayOrigin(new Vector2(screenPos.Item1, screenPos.Item2))
COMPUTE rayDirection ← camera.ProjectRayNormal(new Vector2(screenPos.Item1, screenPos.Item2))

// Transform world ray to grid-local space
COMPUTE localOrigin ← rootNode.ToLocal(rayOrigin)
COMPUTE localDirection ← (rootNode.ToLocal(rayOrigin + rayDirection) - localOrigin).Normalized()

// Convert to voxel space (divide by voxel scale)
COMPUTE voxelOrigin ← localOrigin / voxelScale
COMPUTE voxelDirection ← (localDirection.X, localDirection.Y, localDirection.Z)

// DDA raycast through grid (delegates to voxel-core's VoxelRay)
COMPUTE sdkOrigin ← CREATE VoxelCoord(round(voxelOrigin.X), round(voxelOrigin.Y), round(voxelOrigin.Z))
CREATE ray ← VoxelRay.Create(sdkOrigin, voxelDirection)
COMPUTE hit ← ray.Cast(grid, maxSteps: 200)

IF hit is null
  RETURN null
RETURN hit.Value.HitCoord

---

### GodotVoxelBuilderBridge.Dispose
`() → void`

FOREACH renderer in chunkRenderers.Values
  renderer.Dispose()
chunkRenderers.Clear()
IF paletteAtlas is not null
  paletteAtlas.material?.Dispose()
  paletteAtlas.texture?.Dispose()

---

### GodotChunkRenderer.Create
`(coord: ChunkCoord, chunk: VoxelChunk, neighbors: ChunkNeighbors, mesher: IMesher, palette: Palette, scale: float, material: StandardMaterial3D) → GodotChunkRenderer`

CREATE renderer ← new GodotChunkRenderer()
SET renderer.chunkCoord ← coord

// Mesh the chunk
COMPUTE meshData ← mesher.Mesh(chunk, neighbors, palette, scale)

// Convert MeshData to Godot ArrayMesh
CREATE arrayMesh ← new ArrayMesh()
IF meshData.VertexCount > 0
  CREATE arrays ← new Godot.Collections.Array()
  arrays.Resize((int)Mesh.ArrayType.Max)

  // Convert float[] triplets to Vector3[]
  COMPUTE vertices ← new Vector3[meshData.VertexCount]
  ITERATE i FROM 0 TO meshData.VertexCount - 1
    vertices[i] ← new Vector3(meshData.Vertices[i*3], meshData.Vertices[i*3+1], meshData.Vertices[i*3+2])

  COMPUTE normals ← new Vector3[meshData.VertexCount]
  ITERATE i FROM 0 TO meshData.VertexCount - 1
    normals[i] ← new Vector3(meshData.Normals[i*3], meshData.Normals[i*3+1], meshData.Normals[i*3+2])

  COMPUTE uvs ← new Vector2[meshData.VertexCount]
  ITERATE i FROM 0 TO meshData.VertexCount - 1
    uvs[i] ← new Vector2(meshData.UVs[i*2], meshData.UVs[i*2+1])

  // Convert byte[] colors to Godot float-based Color (Stride-first: bytes are native for Stride)
  COMPUTE colors ← new Godot.Color[meshData.VertexCount]
  IF meshData.Colors is not null
    ITERATE i FROM 0 TO meshData.VertexCount - 1
      colors[i] ← new Godot.Color(
        meshData.Colors[i*4] / 255f, meshData.Colors[i*4+1] / 255f,
        meshData.Colors[i*4+2] / 255f, meshData.Colors[i*4+3] / 255f)

  SET arrays[(int)Mesh.ArrayType.Vertex] ← vertices
  SET arrays[(int)Mesh.ArrayType.Normal] ← normals
  IF meshData.UVs is not null
    SET arrays[(int)Mesh.ArrayType.TexUV] ← uvs
  IF meshData.Colors is not null
    SET arrays[(int)Mesh.ArrayType.Color] ← colors
  SET arrays[(int)Mesh.ArrayType.Index] ← meshData.Indices

  // Store AO in ARRAY_CUSTOM0 as RGBA8_UNORM (1 byte per vertex, normalized)
  IF meshData.AmbientOcclusion is not null
    COMPUTE aoBytes ← new byte[meshData.VertexCount * 4]
    ITERATE i FROM 0 TO meshData.VertexCount - 1
      COMPUTE aoByte ← (byte)(meshData.AmbientOcclusion[i] * 255)
      aoBytes[i * 4] ← aoByte     // R channel = AO value
      aoBytes[i * 4 + 1] ← 0     // G unused
      aoBytes[i * 4 + 2] ← 0     // B unused
      aoBytes[i * 4 + 3] ← 255   // A = 1.0
    SET arrays[(int)Mesh.ArrayType.Custom0] ← aoBytes
    // Set custom format flag on the mesh
    SET customFormat ← Mesh.ArrayCustomFormat.RgbaFloat  // RGBA8_UNORM

  arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays)
  arrayMesh.SurfaceSetMaterial(0, material)

SET renderer.arrayMesh ← arrayMesh

// Create scene node
CREATE meshInstance ← new MeshInstance3D()
meshInstance.Mesh ← arrayMesh
meshInstance.Name ← $"Chunk_{coord.X}_{coord.Y}_{coord.Z}"
SET renderer.meshInstance ← meshInstance

RETURN renderer

---

### GodotChunkRenderer.UpdateMesh
`(chunk: VoxelChunk, neighbors: ChunkNeighbors, mesher: IMesher, palette: Palette, scale: float) → void`

// Re-mesh
COMPUTE meshData ← mesher.Mesh(chunk, neighbors, palette, scale)

// Clear existing surfaces
arrayMesh.ClearSurfaces()

IF meshData.VertexCount > 0
  // Rebuild arrays (same conversion as Create)
  CREATE arrays ← buildGodotArrays(meshData)
  arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays)
  arrayMesh.SurfaceSetMaterial(0, meshInstance.GetActiveMaterial(0))

---

### GodotPaletteAtlas.Create
`(palette: Palette) → GodotPaletteAtlas`

CREATE atlas ← new GodotPaletteAtlas()

// Create 16x16 image (256 pixels, one per palette entry)
CREATE image ← Image.CreateEmpty(16, 16, false, Image.Format.Rgba8)
ITERATE index FROM 0 TO 255
  COMPUTE entry ← palette.Get((byte)index)
  COMPUTE x ← index % 16
  COMPUTE y ← index / 16
  image.SetPixel(x, y, GodotTypeConverter.ToGodotColor(entry.Color))

CREATE texture ← ImageTexture.CreateFromImage(image)

// Create shared material
CREATE material ← new StandardMaterial3D()
material.AlbedoTexture ← texture
material.TextureFilter ← BaseMaterial3D.TextureFilterEnum.Nearest  // Pixel-art style, no bilinear
material.VertexColorUseAsAlbedo ← true  // Blend vertex color with texture
material.ShadingMode ← BaseMaterial3D.ShadingModeEnum.PerPixel

SET atlas.texture ← texture
SET atlas.material ← material
RETURN atlas

---

### GodotPaletteAtlas.Rebuild
`(palette: Palette) → void`

// Update pixel data in existing image
CREATE image ← Image.CreateEmpty(16, 16, false, Image.Format.Rgba8)
ITERATE index FROM 0 TO 255
  COMPUTE entry ← palette.Get((byte)index)
  COMPUTE x ← index % 16
  COMPUTE y ← index / 16
  image.SetPixel(x, y, GodotTypeConverter.ToGodotColor(entry.Color))

texture.Update(image)
// Material references texture — auto-updates on next frame

---

### GodotTypeConverter.ToVector3
`(coord: VoxelCoord, scale: float) → Vector3`

RETURN new Vector3(coord.X * scale, coord.Y * scale, coord.Z * scale)

---

### GodotTypeConverter.ToGodotColor
`(color: Color) → Godot.Color`

RETURN new Godot.Color(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f)

---

## Algorithms

No non-trivial algorithms in this SDK. Meshing is delegated to voxel-core's IMesher implementations. Mouse picking is delegated to voxel-core's VoxelRay.Cast. This SDK is a translation and rendering layer.

---

## Serialization Formats

No custom serialization formats.
