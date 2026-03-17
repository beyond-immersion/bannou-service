# Voxel Builder Unity Bridge SDK Deep Dive

> **SDK**: voxel-builder-unity (not yet created)
> **Location**: `sdks/voxel-builder-unity/` (planned)
> **Layer**: Bridge
> **Domain**: Voxel
> **Dependencies**: voxel-builder (-> voxel-core), Unity Engine (2021.3+)
> **Status**: Aspirational — no code exists.
> **Short**: Unity engine bridge for voxel rendering — Color32 zero-copy, FlipWindingOrder for left-handed, per-chunk Mesh with 32-bit indices

---

## Overview

voxel-builder-unity is the Unity engine bridge for the voxel domain. Unity is the third-priority engine (after Stride and Godot), but its bridge has two notable properties: Unity's `Color32` struct is 4 bytes RGBA (byte-based) — matching our `MeshData.Colors` exactly (zero-copy, same as Stride). However, Unity uses a **left-handed** coordinate system, so the bridge must call `MeshUtility.FlipWindingOrder` on all MeshData before converting to Unity meshes.

Unity's `Mesh` API uses separate attribute arrays (like Godot, unlike Stride's interleaved structs), making the conversion from MeshData straightforward. The main Unity-specific concern is the **65,535 vertex limit per sub-mesh** when using 16-bit index buffers — the bridge opts into 32-bit indices (`IndexFormat.UInt32`) by default.

---

## Consumers

| Consumer | Type | Usage |
|----------|------|-------|
| Game client (Unity) | Application | Renders voxel content in-game |
| Voxel editor tool (Unity) | Application | Interactive voxel editing |
| Content preview (Unity) | Tool | Preview imported/generated voxel models |

---

## Public API Surface

| Type | Kind | Purpose |
|------|------|---------|
| `UnityVoxelBuilderBridge` | Class | IVoxelBuilderBridge implementation. Manages per-chunk GameObjects with MeshFilter/MeshRenderer. |
| `UnityChunkRenderer` | Class | Per-chunk GameObject. Converts MeshData → Unity Mesh with FlipWindingOrder. |
| `UnityVoxelInput` | Class | Mouse ray → VoxelCoord picking via Camera.ScreenPointToRay + DDA. |
| `UnityPaletteAtlas` | Class | Generates Texture2D from Palette entries for material rendering. |
| `UnityTypeConverter` | Static class | SDK types ↔ Unity types. Color32 is zero-copy. Vector3 is direct float mapping. |

---

## Data Model

### UnityVoxelBuilderBridge

```
UnityVoxelBuilderBridge : IVoxelBuilderBridge
├── rootTransform: Transform           // Parent transform for all chunk GameObjects
├── chunkRenderers: Dictionary<ChunkCoord, UnityChunkRenderer>
├── mesher: IMesher                    // Current meshing strategy
├── paletteAtlas: UnityPaletteAtlas    // Current palette texture + material
├── voxelScale: float                  // World units per voxel
├── grid: VoxelGrid?                   // Reference to current grid
└── chunkMaterial: Material            // Shared material instance
```

### UnityChunkRenderer

```
UnityChunkRenderer
├── gameObject: GameObject             // Unity scene object
├── meshFilter: MeshFilter             // Holds the mesh
├── meshRenderer: MeshRenderer         // Renders with shared material
├── mesh: Mesh                         // Generated Unity mesh (32-bit indices)
├── collider: MeshCollider?            // Optional physics collision
└── chunkCoord: ChunkCoord             // Which chunk this renders
```

---

## Computation Pipeline

### Grid Load

```
OnGridLoaded(grid)
  → Store grid reference, extract voxel scale from metadata
  → Generate palette atlas Texture2D from grid.Palette (16x16, FilterMode.Point)
  → Create shared Material with palette atlas
  → For each non-empty chunk in grid:
     CREATE UnityChunkRenderer:
       → Mesh chunk via current mesher → MeshData
       → Flip winding: MeshUtility.FlipWindingOrder(meshData) — LEFT-HANDED CORRECTION
       → Apply AO into vertex colors (multiply RGB by AO value, same as Stride)
       → Convert to Unity Mesh:
          mesh.indexFormat = IndexFormat.UInt32      // 32-bit indices, no 65K limit
          mesh.SetVertices(ConvertVertices(meshData))
          mesh.SetNormals(ConvertNormals(meshData))
          mesh.SetUVs(0, ConvertUVs(meshData))
          mesh.SetColors32(ConvertColors32(meshData))  // ZERO-COPY byte[] → Color32[]
          mesh.SetTriangles(meshData.Indices, 0)
          mesh.RecalculateBounds()
       → Create GameObject with MeshFilter + MeshRenderer
       → Position at chunk world offset
       → Parent under rootTransform
```

### Left-Handed Correction

Unity uses a left-handed coordinate system (Z points away from camera, opposite to Stride/Godot). The correction:

1. **FlipWindingOrder**: Swap each triangle (A, B, C) → (A, C, B) and negate normals. This is done via `MeshUtility.FlipWindingOrder(meshData)` from voxel-core — a single call before conversion.

No axis swap is needed — Unity is Y-up like voxel-core. Only the winding order differs.

### Color32 Zero-Copy

Unity's `Color32` is a struct of 4 bytes (R, G, B, A) — identical memory layout to 4 consecutive bytes in our `MeshData.Colors`. The conversion:

```
For each vertex i:
  color32[i] = new Color32(Colors[i*4], Colors[i*4+1], Colors[i*4+2], Colors[i*4+3])
```

The `Color32` constructor takes 4 bytes directly. No float conversion. Same zero-copy advantage as Stride.

### AO Application

Unity does not have custom per-vertex attribute channels in its standard render pipeline (URP/HDRP have some support via shader graph, but it's not universal). AO is baked into vertex colors during conversion, same approach as the Stride bridge:

```
IF AmbientOcclusion is not null:
  For each vertex i:
    ao = AmbientOcclusion[i]
    color32[i] = new Color32(
      (byte)(Colors[i*4] * ao),
      (byte)(Colors[i*4+1] * ao),
      (byte)(Colors[i*4+2] * ao),
      Colors[i*4+3])
```

### Incremental Update

```
OnChunksModified(dirtyCoords)
  → For each dirty ChunkCoord:
     IF chunk is now empty AND renderer exists:
       Destroy GameObject, remove from dictionary
     ELSE IF chunk is non-empty AND renderer exists:
       mesh.Clear()
       Re-mesh → FlipWindingOrder → repopulate mesh arrays → RecalculateBounds
     ELSE IF chunk is non-empty AND no renderer:
       CREATE new UnityChunkRenderer
  → Update neighbor chunk renderers (boundary faces)
```

### Mouse Picking

```
ScreenToVoxel(screenPos)
  → Camera.main.ScreenPointToRay(screenPos) → world ray
  → Transform ray to grid-local space (rootTransform.InverseTransformPoint/Direction)
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
| Single chunk mesh update | < 3 ms | Client | Re-mesh + flip + Mesh.Set* + RecalculateBounds |
| Full grid load (100 chunks) | < 300 ms | Client | Initial mesh generation |
| FlipWindingOrder | < 0.1 ms | Client | Index swap + normal negate per chunk |
| Color32 packing | ~0 | Client | Zero-copy byte[] → Color32 constructor |
| Mouse picking | < 50 us | Client | Camera ray + DDA |

### Unity-Specific Performance Notes

- **Mesh.SetVertices/SetNormals/SetUVs**: Unity's Mesh API accepts `NativeArray<T>` for burst-compatible zero-copy uploads in newer versions (2021.3+). The bridge should use `NativeArray` paths where available to avoid managed array copies.
- **Mesh.MarkDynamic()**: For chunks that are frequently modified (active editing), call `MarkDynamic()` on the Mesh to hint the GPU driver to use a dynamic buffer. Static chunks (frozen borders, completed builds) should not be marked dynamic.
- **Combine meshes for distant chunks**: Unity's `Mesh.CombineMeshes` can merge multiple chunk meshes into a single draw call for distant LOD. This reduces draw call overhead for large grids at the cost of per-chunk update granularity.

---

## Engine-Specific Patterns

### Type Conversion

| SDK Type | Unity Type | Conversion | Cost |
|----------|-----------|------------|------|
| `VoxelCoord` | `Vector3Int` | Direct (x, y, z) | Zero-copy |
| `Color` (SDK, 4 bytes) | `Color32` (4 bytes) | Constructor: `new Color32(r, g, b, a)` | Zero-copy |
| `MeshData.Vertices` (float[]) | `Vector3[]` | Triplet → Vector3 | O(n) |
| `MeshData.Normals` (float[]) | `Vector3[]` | Triplet → Vector3 (already negated by FlipWindingOrder) | O(n) |
| `MeshData.UVs` (float[]) | `Vector2[]` | Pair → Vector2 | O(n) |
| `MeshData.Colors` (byte[]) | `Color32[]` | 4 bytes → Color32 | Zero-copy |
| `MeshData.Indices` (int[]) | `int[]` | Direct (after FlipWindingOrder swap) | Zero-copy |

### Render Pipeline Compatibility

| Pipeline | Material Approach | AO Handling |
|----------|------------------|-------------|
| Built-in | Standard shader with vertex colors + palette atlas | Baked into vertex colors |
| URP | Lit shader with vertex color node in Shader Graph | Baked into vertex colors, or custom vertex attribute via Shader Graph |
| HDRP | Lit shader with vertex color node in Shader Graph | Custom vertex attribute possible via Shader Graph |

For URP/HDRP, a more sophisticated approach is possible: AO as a separate `TEXCOORD1` channel (second UV set), read by a Shader Graph that multiplies AO into the final lighting. This preserves pure material colors and allows dynamic AO scaling. But the baked-into-color approach works universally across all three pipelines.

---

## Known Quirks & Caveats

#### Intentional Quirks (Documented Behavior)

- **FlipWindingOrder on every mesh.** Every MeshData from voxel-core arrives in CCW (right-handed) winding. The Unity bridge calls `MeshUtility.FlipWindingOrder` before conversion, producing CW winding for Unity's left-handed system. This is a per-chunk O(n) operation on triangle count (~0.1ms per chunk) — negligible compared to meshing (~1-5ms).

- **32-bit indices by default.** Unity's default 16-bit index buffer limits meshes to 65,535 vertices. A fully detailed CulledMesh chunk can approach this limit. The bridge sets `mesh.indexFormat = IndexFormat.UInt32` unconditionally to avoid silent vertex count bugs. The memory cost is negligible (2 extra bytes per index for typically 2-10K indices per chunk).

- **AO baked into vertex colors.** Same approach as Stride bridge — AO multiplied into RGB during Color32 packing. See Stride bridge deep dive for rationale.

#### Design Considerations (Requires Planning)

- **NativeArray for zero-copy mesh upload**: Unity 2021.3+ supports `Mesh.SetVertexBufferData(NativeArray<T>)` for burst-compatible zero-copy GPU uploads. Implementing this requires unsafe code and NativeArray allocation. Worth benchmarking against the simpler `SetVertices(Vector3[])` path — the managed array path may be fast enough for chunk-sized meshes.

- **Physics collision**: If `MeshCollider` is attached, it needs a separate collision mesh. The bridge would mesh the chunk a second time with `MeshingOptions.CollisionMode=true` (cheaper — no UVs, colors, AO) and assign to `MeshCollider.sharedMesh`. This doubles the mesh count per chunk but collision meshes are simpler.

---

## Open Questions

1. **Minimum Unity version**: Unity 2021.3 LTS is the assumed baseline for `IndexFormat.UInt32` and `NativeArray` mesh APIs. Supporting older Unity versions would require fallback paths (16-bit indices with mesh splitting for large chunks). Likely not worth supporting — 2021.3 LTS is the current long-term support version.

---

## Work Tracking

No work items yet — SDK is pre-implementation.
