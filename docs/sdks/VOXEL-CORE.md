# Voxel Core SDK Deep Dive

> **SDK**: voxel-core (not yet created)
> **Location**: `sdks/voxel-core/` (planned)
> **Layer**: Theory
> **Domain**: Voxel
> **Dependencies**: K4os.Compression.LZ4 (MIT) — already used by asset-bundler
> **Status**: Aspirational — no code exists.
> **Implementation Map**: [docs/sdks/maps/VOXEL-CORE.md](maps/VOXEL-CORE.md)
> **Short**: Sparse chunk-based voxel grid primitives with meshing, voxelization, serialization (.bvox), and math — the foundation for all voxel SDKs

---

## Overview

voxel-core is the theory-layer SDK for the voxel domain. It provides the fundamental data structures (VoxelGrid, VoxelChunk, Voxel, Palette), spatial math (coordinates, raycasting, bounds), bidirectional conversion between voxels and meshes (meshing and voxelization), serialization (the .bvox binary format with LZ4 compression), and delta encoding for incremental persistence.

It follows the same pattern as music-theory and storyline-theory: pure computation, zero service dependencies, deterministic when applicable, usable on both client and server. Any code that needs to create, read, modify, serialize, mesh, or voxelize data depends on this SDK.

**Two conversion directions**: Meshing converts voxels→mesh (for rendering). Voxelization converts mesh→voxels or heightmap→voxels (for editing). Both are core data conversions that higher SDKs and plugins build on. The terrain overlay workflow (voxelize terrain → edit → bake back to mesh) depends on both directions living in voxel-core.

---

## Established Patterns and Prior Art

Every major design decision traces to a proven system in the voxel ecosystem. This section documents the lineage so that reviewers and future implementors understand *why* each choice was made — not just what it is.

### Sparse Chunk Grid

**Pattern**: Subdivide the world into fixed-size chunks; store only non-empty chunks in a dictionary.
**Source**: Minecraft (2011). Minecraft uses 16x16x16 "sections" within 16x256x16 "chunks."
**Alternatives considered**:
- **Sparse Voxel Octree (SVO)** — used by NVIDIA for GPU voxelization, Atomontage Engine. Better for variable-resolution data and GPU ray tracing. Rejected: more complex to implement, edit, and serialize, with poor cache locality for interactive editing (tree traversal vs. flat array indexing). Our use case is discrete palette-indexed voxels, not continuous density fields.
- **OpenVDB** — DreamWorks' Academy Award-winning hierarchical sparse volume format. Industry standard for film VFX. Rejected: C++ library designed for continuous density fields, enormous overkill for discrete game voxels.

**Why this works**: The chunk dictionary is O(1) lookup, cache-friendly within chunks (flat array), and naturally supports sparse data (empty regions cost zero memory). Battle-proven across dozens of shipped voxel games.

### 16^3 Chunk Size

**Pattern**: Each chunk is exactly 16x16x16 voxels.
**Source**: Industry convergence — Minecraft sections, MagicaVoxel internal blocks, most voxel engines.
**Why 16**: Power of two enables compiler optimization (`/ 16` → `>> 4`, `% 16` → `& 0xF`). Small enough that editing one voxel only remeshes ~4K voxels (the dirty chunk), large enough that dictionary overhead is manageable (a 256^3 grid = 4,096 chunks, not 16M entries). 32^3 has fewer chunks but 8x the remesh cost; 8^3 has 8x more chunks with 8x less remesh cost. 16^3 is the established sweet spot for interactive editing + server-side generation.

### Palette-Based Coloring

**Pattern**: Each voxel stores a 1-byte index into a shared 256-entry palette, not an inline color.
**Source**: MagicaVoxel. Its `.vox` format uses exactly this model — 256 RGBA entries, index 0 = empty.
**Alternatives considered**:
- **Per-voxel RGB** (Teardown uses 24-bit color per voxel): 3x the memory, dramatically worse compression (RLE exploits repeated values — palette indices repeat frequently, raw RGB almost never does).
- **Bit-packed local palettes** (Minecraft post-1.13): Each section has its own palette with variable-bit indices. More memory-efficient for sparse material sets but significantly more complex to implement and edit.

**Why palette**: Matches MagicaVoxel exactly (artist tool familiarity), compresses well (few unique byte values = excellent RLE ratios), and the material mapping (Diffuse/Metal/Glass/Emit/Cloud) maps directly to MagicaVoxel's MATL chunk types. 255 usable entries is sufficient for voxel art — MagicaVoxel artists routinely create complex scenes within this limit.

### Meshing Algorithms

All three meshers implement well-established algorithms:

- **Culled meshing**: The simplest correct approach — emit faces only where neighbors are empty. Every voxel engine implements this as baseline. No formal publication; it's the obvious first algorithm.
- **Greedy meshing**: **Mikola Lysenko, "Meshing in a Minecraft Game" (0fps.net, 2012)**. The canonical reference for coplanar face merging. Scans each 2D slice per face direction, finds maximal rectangles of the same material, emits one quad per rectangle. Typically 5-20x face count reduction. Used by virtually every production voxel renderer.
- **Marching cubes**: **Lorensen & Cline, "Marching Cubes: A High Resolution 3D Surface Construction Algorithm" (SIGGRAPH 1987)**. The foundational smooth surface extraction algorithm. Uses precomputed lookup tables (256 cube configurations). Well-known, well-tested, patent expired 2005. Standard in medical imaging, terrain, and smooth voxel games.

### Mesh Voxelization

**Pattern**: Convert triangle meshes to voxel grids via 3D scan-conversion and solid fill.
**Source**: Well-studied in computer graphics — **Nooruddin & Turk, "Simplification and Repair of Polygonal Models Using Volumetric Techniques" (IEEE TVCG, 2003)** established the ray-parity approach. GPU-accelerated variants exist (Schwarz & Seidel, 2010) but CPU scan-conversion is sufficient for our bounded work-zone sizes.

Two distinct voxelization paths exist:
- **Heightmap voxelization**: Trivially fill columns below sampled height values. Used for terrain overlay creation.
- **Mesh voxelization**: 3D scan-convert each triangle into the voxel grid, then flood-fill to determine solid interior via ray-parity (cast rays along one axis, toggle inside/outside at each surface crossing). Used for arbitrary mesh-to-voxel conversion.

### DDA Raycasting

**Reference**: **Amanatides & Woo, "A Fast Voxel Traversal Algorithm for Ray Tracing" (Eurographics 1987)**. Steps through a uniform grid along a ray by comparing distances to the next boundary on each axis. This is how Minecraft does block picking, how every voxel engine converts mouse position to voxel coordinate. Integer arithmetic only — no floating-point in the hot loop.

### RLE + LZ4 Compression

Standard pipeline. RLE exploits spatial coherence (large regions of same material), LZ4 catches remaining patterns. LZ4 specifically chosen for decompression speed (multi-GB/s) — critical for streaming chunks. Same compression the asset-bundler already uses (`K4os.Compression.LZ4`, MIT licensed).

---

## Coordinate System Convention

voxel-core uses a **Y-up, right-handed** coordinate system with **Stride as the primary target engine**.

| Engine | Up Axis | Handedness | Matches voxel-core? | Bridge Conversion |
|--------|---------|------------|---------------------|-------------------|
| Stride | Y-up | Right-handed | Yes | Colors: zero-copy (byte[]→Color). Vertices: interleave into vertex structs. |
| Godot 4 | Y-up | Right-handed | Yes | Colors: `byte / 255f` to float. Vertices: separate arrays map directly. Winding may need verification. |
| Unity | Y-up | Left-handed | Partially | `FlipWindingOrder` + negate normals. Colors: zero-copy (byte[]→Color32). |
| Unreal | Z-up | Left-handed | No | Swap Y↔Z in positions/normals + `FlipWindingOrder`. Lowest priority; likely C++ .bvox consumer. |
| MagicaVoxel | Z-up | Right-handed | Partially | Swap Y↔Z on import only (voxel-builder handles this). |

### Engine Priority

Stride is the primary engine. When a format choice maps natively to one engine and requires mechanical conversion for others, prefer Stride's convention:
- **Colors as `byte[]`**: Stride's `Color` struct is 4 bytes RGBA — direct match. Godot and Unity bridges convert.
- **Interleaved vertex structs**: Stride requires interleaved `VertexPositionNormalTexture` — the bridge packs our separate arrays. Godot's `ArrayMesh` uses separate arrays natively (cheaper bridge).
- **Right-handed CCW winding**: Matches Stride and Godot. Unity and Unreal bridges call `FlipWindingOrder`.

### What This Means for Meshers

Triangle winding order determines which side of a face is "front." In right-handed systems, counter-clockwise winding is front-facing. In left-handed systems (Unity, Unreal), the same winding produces back-faces — the mesh appears inside-out.

**voxel-core always generates right-handed (CCW) winding.** The SDK provides `MeshUtility.FlipWindingOrder(MeshData)` for bridges targeting left-handed engines. The flip is trivial: for each triangle (A, B, C), swap to (A, C, B) and negate normals.

---

## Consumers

| Consumer | Type | Usage |
|----------|------|-------|
| voxel-builder | SDK | Operations, import/export, orchestrator — all built on VoxelGrid |
| voxel-generator | SDK | All generators produce VoxelGrid output, use Palette and VoxelCoord |
| voxel-builder-stride | Bridge | Renders MeshData, packs into Stride vertex structs, byte[] colors are zero-copy |
| voxel-builder-godot | Bridge | Renders MeshData, converts byte→float colors, uses VoxelCoord for picking |
| lib-procedural | Plugin | Server-side voxel generation and terrain voxelization, serializes to .bvox |
| lib-save-load | Plugin | Delta serialization of modified chunks for voxel save data |
| lib-dungeon | Plugin (indirect) | Terrain overlay for dungeon chamber editing, voxelize → edit → bake |
| Gardener / Housing | Composition | Work zone terrain editing and player sculpting, via lib-procedural |
| Content pipeline | Tool | .bvox serialization for asset bundling |

---

## Public API Surface

### Grid Subsystem

| Type | Kind | Purpose |
|------|------|---------|
| `VoxelGrid` | Class | Sparse 3D voxel grid subdivided into 16x16x16 chunks. Primary data structure. |
| `VoxelChunk` | Class | 16x16x16 fixed-size chunk. Flat byte array, XZY memory order. |
| `Voxel` | Struct (2 bytes) | Individual voxel: palette index (1 byte) + flags (1 byte). Value type. |
| `VoxelFlags` | Enum (byte) | Per-voxel flags: Hidden, Damaged, Emissive, Transparent, Frozen. Bits 5-7 reserved. |
| `Palette` | Class | 256-entry color/material palette shared by all voxels in a grid. |
| `PaletteEntry` | Struct | Color (RGBA) + MaterialType + Roughness for one palette slot. |
| `MaterialType` | Enum (byte) | Diffuse, Metal, Glass, Emit, Cloud. Maps to MagicaVoxel materials. |
| `GridMetadata` | Class | Grid-level properties: name, author, creation date, tags, voxel scale. |

### Math Subsystem

| Type | Kind | Purpose |
|------|------|---------|
| `VoxelCoord` | Struct | Integer 3D coordinate (x, y, z). Floor-division for negatives. |
| `ChunkCoord` | Struct | Chunk-level coordinate. Derived from VoxelCoord by floor-division by 16. |
| `VoxelBounds` | Struct | Integer axis-aligned bounding box. Min/Max VoxelCoord. |
| `VoxelRay` | Struct | Integer raycast through voxel grid using DDA algorithm. |
| `Color` | Struct | RGBA color (4 bytes). Used by Palette. |

### Meshing Subsystem

| Type | Kind | Purpose |
|------|------|---------|
| `IMesher` | Interface | Meshing algorithm contract: `(VoxelChunk, neighbors, Palette, MeshingOptions) → MeshData`. |
| `CulledMesher` | Class | Per-face culling. Fast, correct, blocky aesthetic. Default. |
| `GreedyMesher` | Class | Coplanar face merging. 5-20x fewer faces. Production quality. |
| `MarchingCubesMesher` | Class | Smooth surface extraction from density. Terrain/organic shapes. |
| `MeshingOptions` | Record | Meshing configuration: voxel scale, ambient occlusion toggle, collision mode. |
| `MeshData` | Class | Engine-agnostic mesh output: vertices, normals, UVs, indices, colors, AO. |
| `MeshUtility` | Static class | Post-processing: `FlipWindingOrder` for left-handed engines, `MergeMeshData` for multi-chunk assembly. |

### Voxelization Subsystem

| Type | Kind | Purpose |
|------|------|---------|
| `HeightmapVoxelizer` | Static class | `float[,] heightmap → VoxelGrid`. Fill columns below height values with material sampling. |
| `MeshVoxelizer` | Static class | `MeshData → VoxelGrid`. 3D scan-conversion + ray-parity solid fill. |
| `VoxelizationOptions` | Record | Voxelization configuration: voxel scale, fill mode (surface/solid), material mapping, frozen border width. |

### Serialization Subsystem

| Type | Kind | Purpose |
|------|------|---------|
| `VoxelSerializer` | Static class | Serialize/deserialize VoxelGrid to/from .bvox binary format. |
| `VoxelCompression` | Static class | RLE + LZ4 compression pipeline for sparse grids. |
| `VoxelDelta` | Static class | Binary delta encoding between two grid states (chunk-level). |

---

## Data Model

### VoxelGrid

The primary data structure. A sparse 3D grid where only non-empty chunks are allocated. Internally a `Dictionary<ChunkCoord, VoxelChunk>`.

```
VoxelGrid
├── Bounds: VoxelBounds          // Grid dimensions (may exceed populated area)
├── Palette: Palette             // Shared 256-entry color/material palette
├── Metadata: GridMetadata       // Name, author, tags, voxel scale
├── chunks: Dictionary<ChunkCoord, VoxelChunk>  // Sparse chunk storage
└── VoxelCount: int              // Cached count of non-empty voxels
```

**Thread safety**: Concurrent read, single-writer. The operation system in voxel-builder enforces single-writer; voxel-core does not enforce this internally.

### VoxelChunk

Fixed 16x16x16 volume. Stored as flat byte arrays in **XZY memory order** — horizontal XZ slices are contiguous, optimizing the greedy mesher's most common scan direction (floor/ceiling faces) and column-wise terrain voxelization.

```
VoxelChunk (8,192 bytes)
├── paletteIndices: byte[4096]   // 16*16*16 palette indices (0 = empty)
├── flags: byte[4096]            // 16*16*16 VoxelFlags
├── NonEmptyCount: int           // Cached count for quick emptiness check
└── IsDirty: bool                // Modified since last serialization
```

**Addressing**: Local coordinate `(x, y, z)` → flat index `x + z * 16 + y * 256`. This is XZY order — X varies fastest (within a row), Z varies next (within a horizontal slice), Y varies slowest (between slices). Matches Minecraft's section layout. Each horizontal XZ slice (all voxels at a given Y level) occupies a contiguous 256-byte region.

### Voxel

2-byte value type. Designed for zero-allocation pass-by-value in hot paths.

```
Voxel (2 bytes)
├── PaletteIndex: byte    // 0 = empty (air), 1-255 = palette entry
└── Flags: VoxelFlags     // Per-voxel property flags
```

### VoxelFlags

```
VoxelFlags : byte
├── None        = 0       // Default
├── Hidden      = 1       // Exists but not rendered (structural support)
├── Damaged     = 2       // Visual damage state (cracks, weathering)
├── Emissive    = 4       // Emits light (palette color as light color)
├── Transparent = 8       // Semi-transparent (glass, water)
├── Frozen      = 16      // Boundary-locked — voxel-builder rejects edits to frozen voxels
└── bits 5-7 reserved     // 3 bits for game-specific flags
```

**Frozen flag**: Used by the terrain overlay system to lock boundary voxels that must remain aligned with surrounding non-voxel terrain. VoxelGrid.SetVoxel does NOT enforce the frozen flag — it's a marker. Enforcement lives in voxel-builder's operation system, which checks the flag before allowing edits. This lets generators and the voxelizer write to frozen coordinates during initial setup while preventing player/NPC edits afterward.

### Palette

256-entry array. Index 0 is always empty (air). Compatible with MagicaVoxel's palette model.

```
Palette
├── entries: PaletteEntry[256]
├── usedCount: int               // Next available index
└── entryIndex: Dictionary<(Color, MaterialType, Roughness), byte>  // Full entry → index reverse lookup
```

**Reverse lookup key**: The full `(Color, MaterialType, Roughness)` tuple, not just color. Two entries with the same RGB but different materials (Metal vs. Diffuse) or different roughness are distinct palette entries. This is critical for terrain voxelization where visually similar terrain types need distinct materials, and for TemplateStamper palette merging.

### MeshData

Engine-agnostic mesh output. Each engine bridge converts this to its native format.

```
MeshData
├── Vertices: float[]     // xyz triplets (right-handed, Y-up)
├── Normals: float[]      // xyz triplets
├── UVs: float[]?         // uv pairs (palette atlas coordinates — null in CollisionMode)
├── Indices: int[]        // Triangle indices (CCW winding = front face)
├── Colors: byte[]?       // RGBA per-vertex, 4 bytes each (null in CollisionMode)
├── AmbientOcclusion: float[]?  // Per-vertex AO values 0.0-1.0 (null if AO disabled or CollisionMode)
├── VertexCount: int
└── TriangleCount: int
```

**Colors as bytes, not floats**: Palette colors are naturally byte-valued (0-255 RGBA). Storing as `byte[]` is 4x more compact than `float[]` and maps directly to Stride's `Color` struct (4-byte RGBA) with zero conversion. Godot bridges convert `byte / 255f` to float. This optimizes for the primary engine.

**Nullable optional arrays**: When `MeshingOptions.CollisionMode = true`, `UVs`, `Colors`, and `AmbientOcclusion` are all null. Only `Vertices`, `Normals`, and `Indices` are populated. Bridges check for null before accessing.

**UV texel centering**: For a 16x16 palette atlas, the UV for palette index `i` is:
- `U = (i % 16 + 0.5) / 16`
- `V = (i / 16 + 0.5) / 16`

The `+0.5` centers the sample point within the texel, preventing color bleeding at texel boundaries even with nearest-neighbor filtering.

**Ambient occlusion**: Per-vertex AO computed by counting solid neighbors at each vertex corner. Examines 3 edge-adjacent voxels plus the diagonal; 0 occluders = 1.0 (fully lit), 3 occluders = ~0.2 (deep shadow). The anisotropy fix flips the quad diagonal split when AO values differ across corners, preventing visual artifacts. This is standard Minecraft-style AO — dramatic visual depth for near-zero cost.

**Winding order**: Always counter-clockwise (right-handed convention, matching Stride). `MeshUtility.FlipWindingOrder(meshData)` swaps each triangle's second and third indices and negates normals.

---

## Computation Pipeline

### Grid Operations

```
VoxelCoord → ChunkCoord
  Floor-division: floor(x / 16), floor(y / 16), floor(z / 16)
  C# arithmetic right shift: x >> 4 (floors for negatives)

VoxelCoord → Local coord within chunk
  Floor-modulo: ((x % 16) + 16) % 16  (always produces 0-15, even for negative x)

Local (x, y, z) → Flat array index
  XZY order: x + z * 16 + y * 256
```

**Negative coordinate correctness**: C#'s `%` operator rounds toward zero, so `-1 % 16 = -1` (wrong — we need 15). The `((x % 16) + 16) % 16` pattern produces correct 0-15 results for all integers. Similarly, `-1 / 16 = 0` in C# (rounds toward zero), but we need `-1` (floor division). Arithmetic right shift `x >> 4` correctly floors for negatives in C#.

### Meshing Pipeline

```
VoxelGrid
  → Select chunks to mesh (visibility, LOD)
  → Per chunk: IMesher.Mesh(chunk, neighbors, palette, options)
     → CulledMesher:  for each non-empty voxel, emit faces where neighbor is empty + AO
     → GreedyMesher:  for each face direction, scan planes, merge coplanar same-palette+AO faces
     → MarchingCubes: for each cell, evaluate density, emit triangles from lookup table (no AO)
  → MeshData (vertices, normals, UVs, indices, colors, AO)
```

### Voxelization Pipeline

```
Heightmap voxelization:
  float[,] heightmap + materialMap + VoxelizationOptions
  → For each (x, z) column in grid bounds:
     sample height at (x, z) from heightmap
     sample material at (x, z) from materialMap → palette index
     fill voxels from y=0 to y=floor(height) with palette index
  → Mark border column voxels as Frozen (columns within FrozenBorderWidth of XZ grid edge)
  → Return VoxelGrid

Mesh voxelization:
  MeshData source + VoxelizationOptions
  → Phase 1: Surface rasterization
     For each triangle in source mesh:
       3D scan-convert triangle into voxel grid (mark surface voxels)
       Sample material from source vertex colors or UV → palette index
  → Phase 2: Solid fill (if options.FillMode == Solid)
     For each (x, z) column:
       Cast ray along Y axis through grid
       Count surface crossings — toggle inside/outside (ray-parity)
       Fill interior voxels as solid with nearest surface material
  → Mark border voxels as Frozen (options.FrozenBorderWidth)
  → Return VoxelGrid
```

### Serialization Pipeline

```
VoxelGrid → .bvox
  → Write header (magic, version, flags, counts, checksum placeholder)
  → Write bounds section (binary grid extents — fast spatial query without JSON parse)
  → Write palette section
  → Write metadata section (JSON)
  → For each non-empty chunk (sorted by ChunkCoord):
     → RLE encode paletteIndices[4096] → compressed stream 1
     → RLE encode flags[4096] → compressed stream 2
     → Concatenate streams, LZ4 compress the combined output
     → Write chunk table entry (coord + offset + length + nonEmptyCount)
     → Write compressed chunk data
  → Compute xxHash32 over all post-header bytes, write to checksum field

.bvox → VoxelGrid (reverse)
  → Read header, validate magic/version/checksum
  → Read bounds (binary grid extents)
  → Read palette
  → Read metadata
  → Read chunk table (coord + offset + length + nonEmptyCount per chunk)
  → For each chunk entry:
     → LZ4 decompress
     → Split into two RLE streams
     → RLE decode stream 1 → paletteIndices
     → RLE decode stream 2 → flags
     → Populate VoxelChunk
```

### Delta Pipeline

```
VoxelGrid (old) + VoxelGrid (new) → VoxelDelta
  → Identify chunks present in new but not old (added)
  → Identify chunks present in old but not new (removed)
  → Identify chunks present in both with IsDirty flag (modified)
  → For modified chunks: XOR diff of both flat arrays (indices + flags)
  → Serialize as: [added chunks full data] + [removed chunk coords] + [modified chunk diffs]

VoxelGrid (base) + VoxelDelta → VoxelGrid (patched)
  → Apply removals (delete chunks)
  → Apply additions (insert full chunk data)
  → Apply modifications (XOR patch both flat arrays)
```

---

## Determinism Contract

**Meshing**: Deterministic. Same VoxelGrid + same MeshingOptions → same MeshData. No floating-point platform sensitivity because all mesh coordinates are derived from integer voxel positions multiplied by scale.

**Voxelization**: Deterministic. Same input mesh/heightmap + same options → identical VoxelGrid. The 3D scan-conversion uses integer rasterization; the ray-parity fill casts along a fixed axis with deterministic iteration order.

**Serialization**: Deterministic. Same VoxelGrid → identical .bvox bytes. Chunk iteration order is deterministic (sorted by ChunkCoord). RLE and LZ4 are deterministic algorithms.

**Delta**: Deterministic. Same (old, new) pair → identical delta bytes.

**Grid operations** (Get/Set): Trivially deterministic — direct array access.

---

## Performance Targets

| Operation | Target | Context | Notes |
|-----------|--------|---------|-------|
| GetVoxel | < 100 ns | Both | Dictionary lookup + array index |
| SetVoxel | < 200 ns | Both | Dictionary lookup + array write + dirty flag |
| CulledMesher (one chunk) | < 1 ms | Client | 16x16x16 = 4,096 potential voxels |
| GreedyMesher (one chunk) | < 5 ms | Client | Face merging pass per direction |
| MarchingCubesMesher (one chunk) | < 10 ms | Client | Density evaluation + triangle lookup |
| HeightmapVoxelizer (64x64 region) | < 10 ms | Server | Column fill, one chunk height |
| MeshVoxelizer (10K triangles, 64^3) | < 100 ms | Server | Scan-convert + ray-parity fill |
| Serialize full grid (256^3) | < 50 ms | Server | Dual-stream RLE + LZ4, typical 100KB-2MB |
| Deserialize full grid (256^3) | < 50 ms | Server | LZ4 decompress + dual RLE decode |
| Delta computation | < 10 ms | Server | Dirty flag check + chunk-level XOR diff |
| Raycast (DDA, 100 steps) | < 10 us | Client | Integer arithmetic only |

---

## Format Support

### Bannou Voxel Binary (.bvox)

**License**: Bannou-defined format
**Direction**: Both (native format)

```
Header (20 bytes):
    Offset 0:  Magic         char[4]   "BVOX"
    Offset 4:  Version       uint16    Format version
    Offset 6:  Flags         uint16    Compressed | HasMetadata | HasPalette
    Offset 8:  ChunkCount    uint32    Number of non-empty chunks
    Offset 12: VoxelCount    uint32    Total non-empty voxels
    Offset 16: Checksum      uint32    xxHash32 of all bytes after the header

Bounds Section (12 bytes):
    Min: int16 x 3 (6 bytes)        Grid minimum corner (X, Y, Z)
    Max: int16 x 3 (6 bytes)        Grid maximum corner (X, Y, Z)

Palette Section (variable):
    EntryCount: uint16
    Entries[]:  Color (4 bytes) + MaterialType (1) + Roughness (4) = 9 bytes each

Metadata Section (variable, optional):
    Length: uint32
    Data:   UTF-8 JSON-encoded GridMetadata

Chunk Table (14 bytes per chunk):
    ChunkCoord: int16 x 3 (6 bytes)
    DataOffset: uint32 (relative to chunk data start)
    DataLength: uint16
    NonEmptyCount: uint16            Per-chunk voxel count (0-4096)

Chunk Data (variable per chunk):
    Two concatenated RLE streams (palette indices then flags), LZ4 compressed together
    Stream 1: RLE-encoded byte[4096] palette indices
    Stream 2: RLE-encoded byte[4096] flags
    Both streams concatenated, then LZ4 compressed as one block
    Expected compression ratio: 10:1 to 50:1 for typical structures
```

**Bounds section**: Binary grid bounds available at fixed offset (20) without parsing JSON metadata. A streaming reader can know the spatial extent of the grid from the first 32 bytes of the file. The Metadata JSON also contains bounds (via GridMetadata), but binary is faster for programmatic access.

**Per-chunk NonEmptyCount**: Stored in the chunk table (2 bytes per chunk) so a reader can assess chunk density without decompressing chunk data. Enables: LOD decisions (skip sparse chunks at distance), streaming priority (load dense chunks first), pre-allocation of mesh buffers proportional to expected face count.

**Two RLE streams**: Palette indices and flags are RLE-encoded separately because they have different statistical properties — flags are mostly zeros (VoxelFlags.None), producing near-perfect RLE compression, while palette indices have more variety. Separate streams exploit these different distributions. The two RLE outputs are concatenated and LZ4-compressed together (LZ4 sees the boundary as just another pattern).

**Checksum**: xxHash32 of all bytes after the header (offset 20+, including the bounds section). Verified on deserialization. Uses `System.IO.Hashing.XxHash32` (BCL since .NET 7, no external dependency). Detects accidental corruption during storage or transfer.

**Why custom format**: .bbmodel is JSON (verbose, slow). .vox is compact but non-chunked (can't stream or delta-update). .bvox is chunk-aligned, enabling streaming (load visible chunks first), delta saves (re-serialize only dirty chunks), and server efficiency (direct serialization without format conversion).

---

## Engine Bridge Format Notes

Findings from Stride and Godot engine documentation that inform bridge design.

### Stride (Primary)

- **Vertex structs**: `VertexPositionNormalTexture` (32 bytes: float3 + float3 + float2) and `VertexPositionColorTexture` (24 bytes: float3 + Color + float2). Always interleaved — no separate attribute arrays.
- **Color**: `Stride.Core.Mathematics.Color` is 4 bytes RGBA. Our `byte[]` MeshData.Colors maps directly.
- **Mesh creation**: `Buffer.Vertex.New(GraphicsDevice, vertices)` → `VertexBufferBinding` → `MeshDraw` → `Mesh`. The bridge packs our separate float[] arrays into interleaved vertex structs.
- **Coordinate convention**: Right-handed, Y-up, CCW front-face. Exact match — no transforms needed.
- **GPU compute meshing**: The stride-marching-cubes reference implementation uses compute shaders for real-time meshing. Future optimization path for the Stride bridge — initial implementation uses CPU meshing + GPU buffer upload.

### Godot (Secondary)

- **ArrayMesh**: Accepts separate arrays: `PackedVector3Array` (vertices, normals), `PackedVector2Array` (UVs), `PackedColorArray` (colors), `PackedInt32Array` (indices). Our separate-array MeshData maps naturally.
- **Color**: `Godot.Color` is 4 floats (0.0-1.0). Bridge converts: `new Color(r / 255f, g / 255f, b / 255f, a / 255f)`.
- **AO storage**: Godot supports `ARRAY_CUSTOM0-3` with `RGBA8_UNORM` format (1 byte per vertex, normalized). Per-vertex AO maps to a custom channel — no need to bake AO into vertex colors.
- **Coordinate convention**: Right-handed, Y-up. Winding order convention needs verification during bridge implementation — `FlipWindingOrder` available if needed.

---

## Known Quirks & Caveats

#### Intentional Quirks (Documented Behavior)

- **Two bytes per voxel excludes per-voxel metadata.** The Voxel struct is deliberately 2 bytes (palette index + flags). There is no room for per-voxel game data (damage amounts, light levels, custom state). This is intentional — compact voxels enable fast meshing, serialization, and generation. Game-specific per-voxel data (e.g., dungeon wall damage tracking) should be stored externally by the consuming plugin in a `Dictionary<VoxelCoord, T>` or state store keyed by coordinate. The SDK provides the spatial structure; the plugin provides the semantic overlay.

- **256 palette entries is the hard limit.** This matches MagicaVoxel exactly and is sufficient for voxel art. Games needing more than 255 colors in a single grid use multiple grids (composed at the Scene level) or accept the palette constraint. In practice, even complex MagicaVoxel scenes rarely use more than 100 entries.

- **No per-face color.** Each voxel has one palette index applied to all 6 faces. BlockBench supports per-face textures; the importer in voxel-builder reduces per-face data to per-voxel by selecting the most common face color. For models that need per-face variation, the answer is more voxels at finer resolution, not richer per-voxel data.

- **Meshes are always right-handed CCW winding.** Left-handed engine bridges (Unity, Unreal) must call `MeshUtility.FlipWindingOrder()`. Generating one canonical winding and providing a flip utility is cleaner than parameterizing every mesher.

- **Frozen flag is advisory, not enforced by the grid.** `VoxelGrid.SetVoxel` does NOT check the Frozen flag — it writes regardless. Enforcement lives in voxel-builder's operation system, which checks the flag before allowing edits. This separation lets generators and the voxelizer write to frozen coordinates during initial setup while preventing player/NPC edits afterward.

- **Palette reverse lookup uses full (Color, MaterialType, Roughness) tuple.** Two entries with the same RGB but different material types get separate palette indices. This prevents terrain voxelization from merging materials that look similar but behave differently (e.g., stone vs. metal at the same gray color).

#### Design Considerations (Requires Planning)

- **Grid scale**: Configurable via `GridMetadata.VoxelScale`. Arcadia default: 0.25m (one voxel = 25cm cube, ~4x Minecraft resolution). Affects meshing output coordinates, Scene/Mapping integration, the visual granularity of terrain editing, and the size of sculpted decorative items. Scale choice propagates through the entire pipeline — changing it after content is authored means re-authoring.

- **Max grid size**: MagicaVoxel caps at 256^3. The sparse chunk system has no theoretical limit, but serialization, meshing, and memory have practical bounds. Recommend 512^3 soft cap (32,768 potential chunks at full density, typical sparse usage: 100-2,000 chunks). Work zone terrain overlays are bounded by the designated work area — typically 32^3 to 128^3 for a building site.

- **Collision mesh**: Consumers that need physics collision mesh twice — once with CulledMesher for collision (correct boundaries, `CollisionMode=true` skips UVs/colors/AO), once with GreedyMesher for rendering. `CollisionMode` reduces the collision pass cost significantly.

- **Terrain overlay boundary smoothness**: At the edge of a voxelized work zone, the voxel surface must seamlessly match the surrounding non-voxel terrain. The HeightmapVoxelizer's `FrozenBorderWidth` parameter creates a frozen border N voxels wide that matches the terrain exactly. MarchingCubesMesher at the boundary produces smooth transitions; CulledMesher/GreedyMesher produce blocky edges (acceptable if the voxel aesthetic is desired, but MarchingCubes is recommended for terrain overlays).

- **Mesh bake quality for permanent terrain**: When a completed work zone is baked from voxels back to mesh for long-term storage, GreedyMesher output is geometrically correct but visually blocky. MarchingCubesMesher produces smoother terrain but higher triangle counts. The bake mesher choice depends on the game's art direction — blocky games use GreedyMesher, naturalistic games use MarchingCubes. Neither includes mesh simplification (LOD reduction) — that's a content pipeline concern beyond the SDK.

---

## Open Questions

1. **Palette sharing across grids**: When TemplateStamper composes multiple templates into one grid, palette indices may conflict. Current solution: palette merging at composition time using the full `(Color, MaterialType, Roughness)` lookup. Tileset-level shared palettes can be added later if merge overhead proves problematic.

2. **LOD support**: No built-in level-of-detail. For large worlds, distant chunks render at lower resolution. The bridge selects mesher per chunk based on camera distance (GreedyMesher near, CulledMesher far). True LOD decimation (sampling every 2^N voxels) can be added to voxel-core later if per-chunk mesher selection proves insufficient.

3. **Voxelization material sampling**: When voxelizing a mesh, how are materials assigned to voxels? Options: (a) sample source mesh vertex colors at the voxel center, (b) sample from a separate material map texture, (c) assign a uniform material. The HeightmapVoxelizer accepts a parallel `byte[,] materialMap` (palette indices). The MeshVoxelizer samples from source vertex colors where available, falling back to a default palette index. Both approaches are simple and sufficient for the terrain overlay and sculpting use cases.

4. **Sculpted item storage size**: A 32^3 decorative sculpture serialized to .bvox: ~200 bytes to 2KB compressed. A 64^3 detailed piece: ~5-50KB. These fit comfortably in item metadata (opaque pass-through per T29) for small objects. Larger sculptures should reference an Asset service asset ID rather than embedding raw data. The threshold (inline vs. asset reference) is a plugin-level decision, not an SDK concern.

---

## Work Tracking

No work items yet — SDK is pre-implementation.
