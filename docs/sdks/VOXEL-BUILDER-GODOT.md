# Voxel Builder Godot Bridge SDK Deep Dive

> **SDK**: voxel-builder-godot (not yet created)
> **Location**: `sdks/voxel-builder-godot/` (planned)
> **Layer**: Bridge
> **Domain**: Voxel
> **Dependencies**: voxel-builder (→ voxel-core), Godot.NET (4.x)
> **Status**: Aspirational — no code exists.
> **Short**: Godot 4.x engine bridge for voxel rendering (per-chunk ArrayMesh), mouse-to-voxel picking, and palette atlas generation

---

## Overview

voxel-builder-godot is the Godot 4.x engine bridge for the voxel domain. It implements `IVoxelBuilderBridge` from voxel-builder, translating between SDK types (MeshData, VoxelCoord, Palette) and Godot types (ArrayMesh, Vector3I, StandardMaterial3D).

It follows the same pattern as scene-composer-godot: a thin translation layer that converts engine-agnostic SDK output to engine-native rendering. The bridge handles per-chunk mesh node management (creation, update, disposal), mouse ray → voxel coordinate picking (DDA raycast through the grid), and palette-based texture atlas generation for material rendering.

---

## Consumers

| Consumer | Type | Usage |
|----------|------|-------|
| Game client (Godot) | Application | Renders voxel content in-game (dungeons, housing, NPC buildings) |
| Voxel editor tool | Application | Interactive voxel editing tool built on VoxelBuilder + this bridge |
| Content preview | Tool | Preview imported/generated voxel models in Godot |

---

## Public API Surface

| Type | Kind | Purpose |
|------|------|---------|
| `GodotVoxelBuilderBridge` | Class | IVoxelBuilderBridge implementation. Manages chunk mesh nodes. |
| `GodotChunkRenderer` | Class | Per-chunk mesh node. Converts MeshData → ArrayMesh, manages as MeshInstance3D. |
| `GodotVoxelInput` | Class | Mouse ray → VoxelCoord picking via DDA raycast through grid. |
| `GodotPaletteAtlas` | Class | Generates a texture atlas from Palette entries for material rendering. |
| `GodotTypeConverter` | Static class | SDK types ↔ Godot types (VoxelCoord ↔ Vector3I, Color ↔ Godot.Color, etc.) |

---

## Data Model

### GodotVoxelBuilderBridge

```
GodotVoxelBuilderBridge : IVoxelBuilderBridge
├── rootNode: Node3D                    // Parent node for all chunk meshes
├── chunkRenderers: Dictionary<ChunkCoord, GodotChunkRenderer>
├── mesher: IMesher                     // Current meshing strategy
├── paletteAtlas: GodotPaletteAtlas     // Current palette texture
├── voxelScale: float                   // World units per voxel (from GridMetadata)
└── grid: VoxelGrid?                    // Reference to current grid
```

### GodotChunkRenderer

```
GodotChunkRenderer
├── meshInstance: MeshInstance3D   // Godot scene node
├── arrayMesh: ArrayMesh          // Generated mesh data
├── chunkCoord: ChunkCoord        // Which chunk this renders
└── material: StandardMaterial3D  // Shared material with palette atlas
```

### GodotPaletteAtlas

```
GodotPaletteAtlas
├── texture: ImageTexture         // 16x16 atlas texture (256 entries)
├── material: StandardMaterial3D  // Shared material using the atlas
└── palette: Palette              // Source palette reference
```

---

## Computation Pipeline

### Grid Load

```
OnGridLoaded(grid)
  → Store grid reference, extract voxel scale from metadata
  → Generate palette atlas texture from grid.Palette
  → Create shared material with atlas
  → For each non-empty chunk in grid:
     CREATE GodotChunkRenderer
       → Mesh chunk via current mesher → MeshData
       → Convert MeshData to ArrayMesh (vertices, normals, UVs, colors)
       → Create MeshInstance3D as child of rootNode
       → Position at chunkCoord * 16 * voxelScale
  → Store in chunkRenderers dictionary
```

### Incremental Update

```
OnChunksModified(dirtyCoords)
  → For each dirty ChunkCoord:
     IF chunk is now empty AND renderer exists:
       Dispose renderer, remove from dictionary
     ELSE IF chunk is non-empty AND renderer exists:
       Re-mesh chunk → update ArrayMesh in-place
     ELSE IF chunk is non-empty AND no renderer:
       CREATE new GodotChunkRenderer (new chunk created by edit)
```

### Mouse Picking

```
ScreenToVoxel(screenPos)
  → Get Camera3D from viewport
  → Project screen position to world ray (origin + direction)
  → Convert world ray to grid-local ray (account for rootNode transform + voxel scale)
  → DDA raycast through VoxelGrid:
     Step along ray in integer voxel increments
     At each step: check if voxel is non-empty
     If hit: return VoxelCoord + face normal (for placement adjacent to hit)
     If exceeded max distance (100 steps): return null
  → Convert VoxelCoord back to world position for visual feedback
```

### Palette Atlas Generation

```
OnPaletteChanged(palette)
  → Create 16x16 Image (256 pixels, one per palette entry)
  → For each entry in palette:
     Set pixel color at (index % 16, index / 16)
  → Create ImageTexture from Image
  → Update shared material's albedo texture
  → UV coordinates for mesh faces: (paletteIndex % 16 + 0.5) / 16, (paletteIndex / 16 + 0.5) / 16
```

---

## Determinism Contract

No determinism contract — rendering output varies by Godot version, GPU, and platform. The SDK types (MeshData, VoxelCoord) are deterministic; only the Godot-specific rendering is non-deterministic.

---

## Performance Targets

| Operation | Target | Context | Notes |
|-----------|--------|---------|-------|
| Single chunk mesh update | < 2 ms | Client | Re-mesh + ArrayMesh upload |
| Full grid load (100 chunks) | < 200 ms | Client | Initial mesh generation |
| Mouse picking (DDA 100 steps) | < 50 μs | Client | Integer math, no GPU |
| Palette atlas rebuild | < 1 ms | Client | 16x16 texture creation |

**Frame budget awareness**: Interactive editing should update only dirty chunks. A brush stroke modifying 3 chunks = ~6ms mesh update, well within a 16ms frame budget (60 FPS).

---

## Engine-Specific Patterns

### Type Conversion

| SDK Type | Godot Type | Conversion | Cost |
|----------|-----------|------------|------|
| `VoxelCoord` | `Vector3I` | Direct (x, y, z) | Zero-copy |
| `Color` (SDK, 4 bytes) | `Godot.Color` (4 floats) | `byte / 255f` per component | O(n) |
| `MeshData.Vertices` (float[]) | `Vector3[]` | Triplet → Vector3 | O(n) |
| `MeshData.Normals` (float[]) | `Vector3[]` | Triplet → Vector3 | O(n) |
| `MeshData.UVs` (float[]) | `Vector2[]` | Pair → Vector2 | O(n) |
| `MeshData.Colors` (byte[]) | `Color[]` | 4 bytes → 4 floats: `new Color(r/255f, g/255f, b/255f, a/255f)` | O(n) |
| `MeshData.AmbientOcclusion` (float[]?) | `ARRAY_CUSTOM0` (RGBA8_UNORM) | `(byte)(ao * 255)` packed into custom channel | O(n) |
| `MeshData.Indices` (int[]) | `int[]` | Direct copy | Zero-copy |

**AO via ARRAY_CUSTOM0**: Godot 4.x supports custom vertex attribute channels (`ARRAY_CUSTOM0` through `ARRAY_CUSTOM3`) with configurable format. AO is stored in `ARRAY_CUSTOM0` using `ARRAY_CUSTOM_RGBA8_UNORM` format (1 byte per vertex, normalized 0-255). The shader samples this channel for per-vertex darkening. This avoids baking AO into vertex colors, keeping material colors pure.

**Color conversion direction**: voxel-core stores colors as `byte[]` (optimized for Stride, the primary engine). The Godot bridge converts byte→float. This is the intentional priority ordering — Stride gets zero-copy colors, Godot pays O(n) conversion.

### ArrayMesh Construction

```csharp
// Per-chunk mesh construction pattern
var arrays = new Godot.Collections.Array();
arrays.Resize((int)Mesh.ArrayType.Max);
arrays[(int)Mesh.ArrayType.Vertex] = vertices;
arrays[(int)Mesh.ArrayType.Normal] = normals;
arrays[(int)Mesh.ArrayType.TexUV] = uvs;
arrays[(int)Mesh.ArrayType.Color] = colors;
arrays[(int)Mesh.ArrayType.Index] = indices;
arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
```

### LOD Strategy

Distance-based chunk LOD is possible by swapping the mesher per chunk:
- **Near**: GreedyMesher (full detail)
- **Far**: CulledMesher (simpler, faster updates)
- **Very far**: Lower-resolution grid sampling (decimation before meshing)

LOD transitions managed by the bridge based on camera distance to chunk center.

---

## Known Quirks & Caveats

#### Design Considerations (Requires Planning)

- **Chunk node count**: A 512^3 grid could have up to 32,768 chunks. Each is a MeshInstance3D node. Godot handles thousands of nodes well, but 32K may need frustum culling and/or chunk merging for distant chunks.

- **ArrayMesh vs ImmediateGeometry3D**: ArrayMesh is the standard approach for pre-computed meshes. ImmediateGeometry3D could theoretically be faster for single-frame preview during brush drag, but ArrayMesh with in-place updates is likely sufficient.

- **Godot version coupling**: The bridge uses Godot.NET 4.x APIs. Godot 4.0 → 4.x API changes (particularly around Mesh and Material) may require version-specific code paths. Follow the same strategy as scene-composer-godot.

---

## Open Questions

1. **Collision mesh**: Should the bridge generate a separate collision shape per chunk (for physics picking and character collision), or is the DDA raycast sufficient? Physics collision requires StaticBody3D + CollisionShape3D per chunk, adding significant node overhead.

2. **Shader approach**: Should the palette atlas use a simple StandardMaterial3D with an albedo texture, or a custom shader that handles material types (Metal reflections, Glass transparency, Emit glow)? Custom shader is more faithful to the palette's MaterialType but adds complexity.

3. **Thread safety**: Should mesh generation happen on a background thread to avoid frame drops during large edits? Godot's scene tree is single-threaded, but ArrayMesh data can be prepared off-thread and applied on the main thread.

---

## Work Tracking

No work items yet — SDK is pre-implementation.
