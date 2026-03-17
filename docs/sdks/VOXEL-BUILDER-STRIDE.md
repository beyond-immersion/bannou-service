# Voxel Builder Stride Bridge SDK Deep Dive

> **SDK**: voxel-builder-stride (not yet created)
> **Location**: `sdks/voxel-builder-stride/` (planned)
> **Layer**: Bridge
> **Domain**: Voxel
> **Dependencies**: voxel-builder (-> voxel-core), Stride.Engine, Stride.Graphics
> **Status**: Aspirational — no code exists.
> **Short**: Stride engine bridge for voxel rendering — interleaved vertex structs, byte-color zero-copy, per-chunk MeshDraw with optional GPU compute meshing

---

## Overview

voxel-builder-stride is the **primary engine bridge** for the voxel domain. Stride is the first-priority engine, so this bridge is optimized for zero-copy paths wherever possible. The key advantage: Stride's `Color` struct is 4 bytes RGBA (byte-based), matching our `MeshData.Colors` exactly — no conversion needed for vertex colors. Stride's coordinate system (right-handed, Y-up, CCW front-face) also matches voxel-core exactly — no winding flips or axis swaps.

The bridge packs voxel-core's separate-array MeshData into Stride's interleaved vertex structs (`VertexPositionNormalTexture` or a custom `VertexPositionNormalColorTexture` with byte Color), uploads to GPU buffers via `Buffer.Vertex.New()`, and manages per-chunk `MeshInstance` entities in the scene.

Follows the same pattern as scene-composer-stride: thin translation layer converting engine-agnostic SDK output to engine-native rendering.

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
| `StrideVoxelBuilderBridge` | Class | IVoxelBuilderBridge implementation. Manages per-chunk mesh entities. |
| `StrideChunkRenderer` | Class | Per-chunk Entity with ModelComponent. Packs MeshData into interleaved vertex buffers. |
| `StrideVoxelInput` | Class | Mouse ray -> VoxelCoord picking via camera projection + DDA. |
| `StridePaletteAtlas` | Class | Generates Stride Texture from Palette entries for material rendering. |
| `StrideVoxelMaterial` | Class | Material setup: palette atlas texture, optional PBR from MaterialType/Roughness. |
| `StrideTypeConverter` | Static class | SDK types <-> Stride types. Color is zero-copy. Vertices pack into interleaved structs. |

---

## Data Model

### StrideVoxelBuilderBridge

```
StrideVoxelBuilderBridge : IVoxelBuilderBridge
├── rootEntity: Entity                 // Parent entity for all chunk meshes
├── chunkRenderers: Dictionary<ChunkCoord, StrideChunkRenderer>
├── mesher: IMesher                    // Current meshing strategy
├── paletteAtlas: StridePaletteAtlas   // Current palette texture + material
├── voxelScale: float                  // World units per voxel
├── grid: VoxelGrid?                   // Reference to current grid
└── graphicsDevice: GraphicsDevice     // Stride GPU device for buffer creation
```

### StrideChunkRenderer

```
StrideChunkRenderer
├── entity: Entity                     // Stride scene entity
├── modelComponent: ModelComponent     // Model with mesh + material
├── mesh: Mesh                         // Generated mesh
├── vertexBuffer: Buffer               // GPU vertex buffer (interleaved)
├── indexBuffer: Buffer?               // GPU index buffer (32-bit)
└── chunkCoord: ChunkCoord             // Which chunk this renders
```

### Vertex Struct

The bridge defines a custom interleaved vertex struct that includes position, normal, UV, and color:

```
StrideVoxelVertex (36 bytes, StructLayout Sequential Pack=4)
├── Position: Vector3       // 12 bytes (float3)
├── Normal: Vector3         // 12 bytes (float3)
├── TexCoord: Vector2       // 8 bytes (float2)
└── Color: Color            // 4 bytes (byte RGBA) — zero-copy from MeshData.Colors
```

This is more efficient than using `VertexPositionNormalTexture` (32 bytes, no color) and a separate color buffer. The custom vertex declaration registers all four attributes for the shader.

When `MeshingOptions.CollisionMode` produced the MeshData (no UVs, no colors, no AO), the bridge falls back to a simpler vertex layout:

```
StrideVoxelCollisionVertex (24 bytes)
├── Position: Vector3       // 12 bytes
└── Normal: Vector3         // 12 bytes
```

---

## Computation Pipeline

### Grid Load

```
OnGridLoaded(grid)
  → Store grid reference, extract voxel scale from metadata
  → Generate palette atlas texture from grid.Palette
  → Create StrideVoxelMaterial (palette atlas + PBR parameters from MaterialType)
  → For each non-empty chunk in grid:
     CREATE StrideChunkRenderer:
       → Mesh chunk via current mesher → MeshData
       → Pack MeshData into StrideVoxelVertex[] (interleaved)
       → Upload: Buffer.Vertex.New(graphicsDevice, vertices)
       → Upload: Buffer.Index.New(graphicsDevice, indices) (32-bit)
       → Create Mesh with MeshDraw (PrimitiveType.TriangleList)
       → Create Entity with ModelComponent, position at chunk world offset
       → Add as child of rootEntity
```

### Interleaving (MeshData → StrideVoxelVertex[])

```
For each vertex i in MeshData:
  vertex.Position = new Vector3(Vertices[i*3], Vertices[i*3+1], Vertices[i*3+2])
  vertex.Normal = new Vector3(Normals[i*3], Normals[i*3+1], Normals[i*3+2])
  IF UVs is not null:
    vertex.TexCoord = new Vector2(UVs[i*2], UVs[i*2+1])
  IF Colors is not null:
    // ZERO-COPY: MeshData.Colors byte[] maps directly to Stride's Color (4 bytes RGBA)
    vertex.Color = new Color(Colors[i*4], Colors[i*4+1], Colors[i*4+2], Colors[i*4+3])
```

The color assignment is the cheapest possible — Stride's `Color` constructor takes 4 bytes, and our MeshData stores 4 bytes per vertex color. No float conversion.

### AO Application

Stride does not have a built-in per-vertex AO channel like Godot's ARRAY_CUSTOM0. AO is applied by **modulating the vertex color** during interleaving:

```
IF AmbientOcclusion is not null:
  ao = AmbientOcclusion[i]
  vertex.Color = new Color(
    (byte)(Colors[i*4] * ao),
    (byte)(Colors[i*4+1] * ao),
    (byte)(Colors[i*4+2] * ao),
    Colors[i*4+3])  // Alpha unchanged
```

This bakes AO into the vertex color. The material shader uses `VertexColorUseAsAlbedo` to multiply vertex color into the albedo. The result is visually identical to a separate AO channel — concave corners darken, exposed faces stay bright.

### Incremental Update

```
OnChunksModified(dirtyCoords)
  → For each dirty ChunkCoord:
     IF chunk is now empty AND renderer exists:
       Dispose renderer (remove Entity, release GPU buffers)
     ELSE IF chunk is non-empty AND renderer exists:
       Re-mesh chunk → re-pack into vertex array → update GPU buffer in-place
       (Stride supports buffer mapping for sub-resource updates)
     ELSE IF chunk is non-empty AND no renderer:
       CREATE new StrideChunkRenderer
  → Also update neighbor chunk renderers (boundary faces may have changed)
```

### Mouse Picking

```
ScreenToVoxel(screenPos)
  → Get CameraComponent from scene
  → Unproject screen position to world ray via camera's ViewProjection matrix
  → Convert world ray to grid-local space (rootEntity.Transform inverse)
  → Divide by voxelScale to get voxel-space ray
  → DDA raycast through grid via VoxelRay.Cast
  → Return hit coordinate or null
```

---

## Determinism Contract

No determinism contract — rendering varies by GPU/platform. SDK types (MeshData, VoxelCoord) are deterministic; only GPU-specific rendering output is non-deterministic.

---

## Performance Targets

| Operation | Target | Context | Notes |
|-----------|--------|---------|-------|
| Single chunk mesh update | < 2 ms | Client | Re-mesh + interleave + GPU upload |
| Full grid load (100 chunks) | < 200 ms | Client | Initial mesh generation |
| Vertex interleaving (1 chunk) | < 0.5 ms | Client | MeshData → StrideVoxelVertex[] |
| Color packing | ~0 | Client | Zero-copy byte[] → Color constructor |
| Mouse picking | < 50 us | Client | Camera unproject + DDA |
| Palette atlas rebuild | < 1 ms | Client | 16x16 texture creation |

### Future: GPU Compute Meshing

The stride-marching-cubes reference implementation demonstrates GPU compute meshing via `ComputeEffectShader`. This is a future optimization path where the mesher runs entirely on the GPU:

```
VoxelChunk data → StructuredBuffer → Compute Shader (meshing) → VertexBuffer (output)
```

This bypasses CPU meshing and vertex interleaving entirely. The bridge would upload raw chunk data to a `RWStructuredBuffer`, dispatch a compute shader that implements CulledMesher/GreedyMesher in SDSL, and bind the output buffer directly as the mesh's vertex buffer. No CPU→GPU vertex data transfer.

This is architecturally compatible with our design (the bridge chooses between CPU mesher + upload vs. GPU compute mesher), but implementation is deferred until the CPU path proves to be a bottleneck.

---

## Engine-Specific Patterns

### Buffer Creation

```csharp
// Vertex buffer from interleaved struct array
var vertexBuffer = Buffer.Vertex.New(graphicsDevice, vertices, GraphicsResourceUsage.Default);
var vertexBinding = new VertexBufferBinding(vertexBuffer, StrideVoxelVertex.Layout, vertices.Length);

// Index buffer (32-bit)
var indexBuffer = Buffer.Index.New(graphicsDevice, indices);
var indexBinding = new IndexBufferBinding(indexBuffer, is32Bit: true, count: indices.Length);

// Assemble MeshDraw
var meshDraw = new MeshDraw
{
    PrimitiveType = PrimitiveType.TriangleList,
    DrawCount = indices.Length,
    VertexBuffers = new[] { vertexBinding },
    IndexBuffer = indexBinding
};
```

### Vertex Declaration

```csharp
public static readonly VertexDeclaration Layout = new VertexDeclaration(
    VertexElement.Position<Vector3>(),
    VertexElement.Normal<Vector3>(),
    VertexElement.TextureCoordinate<Vector2>(),
    VertexElement.Color<Color>()
);
```

### Material Setup

```csharp
// Palette atlas material with PBR support
var material = Material.New(graphicsDevice, new MaterialDescriptor
{
    Attributes =
    {
        Diffuse = new MaterialDiffuseMapFeature(new ComputeTextureColor(paletteTexture)),
        DiffuseModel = new MaterialDiffuseLambertModelFeature(),
        MicroSurface = new MaterialGlossinessMapFeature(/* from PaletteEntry.Roughness */),
    }
});
// Enable vertex color blending
material.Passes[0].Parameters.Set(MaterialKeys.HasVertexColor, true);
```

---

## Known Quirks & Caveats

#### Intentional Quirks (Documented Behavior)

- **AO baked into vertex colors.** Unlike Godot (which has ARRAY_CUSTOM channels), Stride's standard material pipeline doesn't expose custom per-vertex attributes without a custom shader. AO is multiplied into the RGB vertex color during interleaving. This is visually correct but means you can't separate AO from material color after baking. If a future use case needs separate AO (e.g., dynamic time-of-day AO scaling), a custom SDSL shader with an extra vertex attribute would be needed.

- **Custom vertex struct, not standard VertexPositionNormalTexture.** We define `StrideVoxelVertex` (36 bytes) instead of using Stride's built-in `VertexPositionNormalTexture` (32 bytes, no color). This is because voxel rendering needs per-vertex color for palette visualization. The custom struct is registered with Stride's vertex declaration system and works with standard shaders that read `COLOR0`.

#### Design Considerations (Requires Planning)

- **GPU buffer update strategy**: When a single chunk is modified, should the bridge: (a) recreate the entire GPU buffer, (b) map and update the sub-region, or (c) double-buffer (write to staging, copy to GPU)? Option (a) is simplest and fine for small chunk meshes. Option (b) is optimal for frequent edits. Start with (a), profile, switch to (b) if chunk updates cause frame drops.

---

## Open Questions

1. **MaterialType→PBR mapping**: Stride's material system is fully PBR. The PaletteEntry's MaterialType (Diffuse/Metal/Glass/Emit/Cloud) and Roughness map naturally to Stride's material attributes, but the exact mapping needs validation. Metal should set metallic=1.0 + the palette color as specular. Glass needs transparency. Emit needs emissive contribution. This is material authoring work, not SDK architecture.

---

## Work Tracking

No work items yet — SDK is pre-implementation.
