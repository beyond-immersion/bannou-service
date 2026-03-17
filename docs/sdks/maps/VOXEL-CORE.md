# Voxel Core SDK Implementation Map

> **SDK**: voxel-core
> **Location**: `sdks/voxel-core/`
> **Layer**: Theory
> **Domain**: Voxel
> **Deep Dive**: [docs/sdks/VOXEL-CORE.md](../VOXEL-CORE.md)
> **Status**: Aspirational — pseudo-code represents intended behavior, not verified implementation

---

| Field | Value |
|-------|-------|
| SDK | voxel-core |
| Layer | Theory |
| Public Types | ~20 (7 classes, 4 structs, 3 interfaces, 3 enums, 3 records) |
| Public Methods | ~40 |
| Dependencies | K4os.Compression.LZ4 (MIT) |
| Deterministic | Yes (pure) |
| Allocation-Free Hot Paths | GetVoxel, SetVoxel, VoxelCoord arithmetic, VoxelRay stepping |

---

## Data Structures

### VoxelGrid

**Kind**: Class
**Thread Safety**: Concurrent-read / Single-writer

| Field | Type | Purpose |
|-------|------|---------|
| `Bounds` | `VoxelBounds` | Grid dimensions (may exceed populated area) |
| `Palette` | `Palette` | Shared 256-entry color/material palette |
| `Metadata` | `GridMetadata` | Name, author, tags, voxel scale |
| `chunks` | `Dictionary<ChunkCoord, VoxelChunk>` | Sparse chunk storage (internal) |
| `VoxelCount` | `int` | Cached count of non-empty voxels |

### VoxelChunk

**Kind**: Class
**Thread Safety**: Single-writer (owned by grid)

| Field | Type | Purpose |
|-------|------|---------|
| `paletteIndices` | `byte[4096]` | 16^3 palette indices, flat array |
| `flags` | `byte[4096]` | 16^3 VoxelFlags, flat array |
| `NonEmptyCount` | `int` | Cached non-empty voxel count |
| `IsDirty` | `bool` | Modified since last serialization |

**Flat index formula**: `x + z * 16 + y * 256` (XZY order) where x, y, z are 0-15 local coordinates. Horizontal XZ slices are contiguous — optimal for greedy mesher floor/ceiling scans and column-wise terrain voxelization.

### Voxel

**Kind**: Readonly struct (2 bytes)
**Thread Safety**: Immutable

| Field | Type | Purpose |
|-------|------|---------|
| `PaletteIndex` | `byte` | 0 = empty, 1-255 = palette entry |
| `Flags` | `VoxelFlags` | Per-voxel property flags |

### VoxelCoord

**Kind**: Readonly struct (12 bytes)
**Thread Safety**: Immutable

| Field | Type | Purpose |
|-------|------|---------|
| `X` | `int` | Voxel X position |
| `Y` | `int` | Voxel Y position |
| `Z` | `int` | Voxel Z position |

### ChunkCoord

**Kind**: Readonly struct (12 bytes)
**Thread Safety**: Immutable

| Field | Type | Purpose |
|-------|------|---------|
| `X` | `int` | Chunk X position (voxel X / 16) |
| `Y` | `int` | Chunk Y position (voxel Y / 16) |
| `Z` | `int` | Chunk Z position (voxel Z / 16) |

### VoxelBounds

**Kind**: Readonly struct
**Thread Safety**: Immutable

| Field | Type | Purpose |
|-------|------|---------|
| `Min` | `VoxelCoord` | Minimum corner (inclusive) |
| `Max` | `VoxelCoord` | Maximum corner (inclusive) |

### Palette

**Kind**: Class
**Thread Safety**: Single-writer

| Field | Type | Purpose |
|-------|------|---------|
| `entries` | `PaletteEntry[256]` | Fixed 256-entry array |
| `usedCount` | `int` | Next available index |
| `entryIndex` | `Dictionary<(Color,MaterialType,float), byte>` | Full (Color, MaterialType, Roughness) → index reverse lookup |

### MeshData

**Kind**: Class
**Thread Safety**: Immutable after construction

| Field | Type | Purpose |
|-------|------|---------|
| `Vertices` | `float[]` | XYZ triplets (right-handed, Y-up) |
| `Normals` | `float[]` | XYZ triplets |
| `UVs` | `float[]` | UV pairs (palette atlas coords, texel-centered: `(i%16+0.5)/16`) |
| `Indices` | `int[]` | Triangle indices (CCW winding = front face) |
| `Colors` | `byte[]` | RGBA per-vertex, 4 bytes each (from palette) |
| `AmbientOcclusion` | `float[]?` | Per-vertex AO 0.0-1.0 (null if AO disabled) |
| `VertexCount` | `int` | Number of vertices |
| `TriangleCount` | `int` | Number of triangles |

### MeshingOptions

**Kind**: Record
**Thread Safety**: Immutable

| Field | Type | Purpose |
|-------|------|---------|
| `VoxelScale` | `float` | World units per voxel (default 0.25) |
| `AmbientOcclusion` | `bool` | Compute per-vertex AO (default true) |
| `CollisionMode` | `bool` | Skip UVs, colors, AO — output nulls for optional arrays (default false) |

**Default instance**: `MeshingOptions.Default` = scale 0.25, AO on, collision off.

### VoxelizationOptions

**Kind**: Record
**Thread Safety**: Immutable

| Field | Type | Purpose |
|-------|------|---------|
| `VoxelScale` | `float` | World units per voxel (default 0.25) |
| `FillMode` | `VoxelFillMode` | Surface (shell only) or Solid (filled interior) |
| `FrozenBorderWidth` | `int` | Voxels from grid edge to mark as Frozen (default 1) |
| `DefaultPaletteIndex` | `byte` | Fallback material when source has no color data |

`VoxelFillMode`: enum { Surface, Solid }.

---

## Dependencies

| Dependency | Type | Usage |
|------------|------|-------|
| K4os.Compression.LZ4 | NuGet (MIT) | LZ4 compression for .bvox serialization |
| System.Numerics | BCL | Vector math for mesh generation |
| System.Text.Json | BCL | GridMetadata JSON serialization |

---

## API Index

#### VoxelGrid

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| GetVoxel | `(VoxelCoord) → Voxel` | Yes | Free | Returns Voxel.Empty for unpopulated |
| SetVoxel | `(VoxelCoord, Voxel) → void` | Yes | Free (chunk exists) / Allocating (new chunk) | Creates chunk on first write |
| GetChunk | `(ChunkCoord) → VoxelChunk?` | Yes | Free | Direct dictionary lookup |
| EnumerateChunks | `() → IEnumerable<(ChunkCoord, VoxelChunk)>` | Yes | Minimal | Enumerates non-empty chunks |
| GetDirtyChunks | `() → IReadOnlySet<ChunkCoord>` | Yes | Allocating | Returns set of chunks with IsDirty=true |
| ClearDirtyFlags | `() → void` | Yes | Free | Resets IsDirty on all chunks |
| IsEmpty | `(VoxelCoord) → bool` | Yes | Free | PaletteIndex == 0 check |
| Contains | `(VoxelCoord) → bool` | Yes | Free | Bounds check |

#### VoxelChunk

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| GetVoxel | `(int x, int y, int z) → Voxel` | Yes | Free | Local coordinates 0-15 |
| SetVoxel | `(int x, int y, int z, Voxel) → void` | Yes | Free | Updates NonEmptyCount and IsDirty |
| IsEmpty | `() → bool` | Yes | Free | NonEmptyCount == 0 |
| GetFlatIndex | `(int x, int y, int z) → int` | Yes | Free | x + y*16 + z*256 |

#### Palette

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| Get | `(byte index) → PaletteEntry` | Yes | Free | Array index |
| Set | `(byte index, PaletteEntry) → void` | Yes | Free | Array write |
| GetOrAddIndex | `(Color, MaterialType) → byte` | Yes | Free (exists) / Free (new) | Reverse lookup or allocate next index |
| Contains | `(Color) → bool` | Yes | Free | Dictionary lookup |

#### VoxelCoord

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| ToChunkCoord | `() → ChunkCoord` | Yes | Free | Floor-division by 16 (`x >> 4`) — correct for negatives |
| ToLocalCoord | `() → (int, int, int)` | Yes | Free | Floor-modulo 16 (`((x % 16) + 16) % 16`) — always 0-15 |
| operator + | `(VoxelCoord, VoxelCoord) → VoxelCoord` | Yes | Free | Component-wise add |
| operator - | `(VoxelCoord, VoxelCoord) → VoxelCoord` | Yes | Free | Component-wise subtract |
| Distance | `(VoxelCoord) → float` | Yes | Free | Euclidean distance |
| ManhattanDistance | `(VoxelCoord) → int` | Yes | Free | Taxi-cab distance |

#### VoxelRay

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| Create | `(VoxelCoord origin, (float,float,float) direction) → VoxelRay` | Yes | Free | Initialize DDA state |
| Step | `() → VoxelCoord` | Yes | Free | Advance one voxel along ray |
| Cast | `(VoxelGrid, int maxSteps) → (VoxelCoord, VoxelCoord)?` | Yes | Free | Returns (hit, face normal) or null |

#### VoxelBounds

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| Contains | `(VoxelCoord) → bool` | Yes | Free | Range check |
| Intersects | `(VoxelBounds) → bool` | Yes | Free | AABB overlap |
| Expand | `(VoxelCoord) → VoxelBounds` | Yes | Free | Expand to include point |
| Volume | `() → long` | Yes | Free | Width * Height * Depth |

#### CulledMesher

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| Mesh | `(VoxelChunk, neighbors, Palette, MeshingOptions) → MeshData` | Yes | Allocating | One MeshData per chunk |

#### GreedyMesher

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| Mesh | `(VoxelChunk, neighbors, Palette, MeshingOptions) → MeshData` | Yes | Allocating | Face-merged MeshData |

#### MarchingCubesMesher

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| Mesh | `(VoxelChunk, neighbors, Palette, MeshingOptions) → MeshData` | Yes | Allocating | Smooth surface MeshData |

#### MeshUtility

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| FlipWindingOrder | `(MeshData) → MeshData` | Yes | Allocating | Swap each triangle (A,B,C)→(A,C,B) for left-handed engines |
| MergeMeshData | `(IReadOnlyList<(MeshData, VoxelCoord offset)>) → MeshData` | Yes | Allocating | Merge multiple chunk meshes into one, offsetting vertices |

#### HeightmapVoxelizer

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| Voxelize | `(float[,] heights, byte[,] materials, Palette, VoxelizationOptions) → VoxelGrid` | Yes | Allocating | Column fill + frozen border |

#### MeshVoxelizer

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| Voxelize | `(MeshData source, Palette, VoxelizationOptions) → VoxelGrid` | Yes | Allocating | Scan-convert + ray-parity fill + frozen border |

#### VoxelSerializer

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| Serialize | `(VoxelGrid) → byte[]` | Yes | Allocating | Full .bvox output |
| Deserialize | `(byte[]) → VoxelGrid` | Yes | Allocating | Reconstruct from .bvox |
| SerializeChunk | `(VoxelChunk) → byte[]` | Yes | Allocating | Single chunk for delta |
| DeserializeChunk | `(byte[]) → VoxelChunk` | Yes | Allocating | Single chunk from delta |

#### VoxelDelta

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| Compute | `(VoxelGrid old, VoxelGrid new) → byte[]` | Yes | Allocating | Delta between two states |
| Apply | `(VoxelGrid base, byte[] delta) → void` | Yes | Free/Allocating | Patch grid in-place |

---

## Methods

### VoxelGrid.GetVoxel
`(coord: VoxelCoord) → Voxel`

VALIDATE coord within Bounds                         → return Voxel.Empty if outside
// Floor-division for negative coordinate correctness
COMPUTE chunkCoord ← (coord.X >> 4, coord.Y >> 4, coord.Z >> 4)
LOOKUP chunk ← chunks[chunkCoord]
IF chunk is null
  RETURN Voxel.Empty
// Floor-modulo: always produces 0-15
COMPUTE lx ← ((coord.X % 16) + 16) % 16
COMPUTE ly ← ((coord.Y % 16) + 16) % 16
COMPUTE lz ← ((coord.Z % 16) + 16) % 16
RETURN chunk.GetVoxel(lx, ly, lz)

---

### VoxelGrid.SetVoxel
`(coord: VoxelCoord, voxel: Voxel) → void`

VALIDATE coord within Bounds                         → ArgumentOutOfRangeException
// Floor-division for negative coordinate correctness
COMPUTE chunkCoord ← (coord.X >> 4, coord.Y >> 4, coord.Z >> 4)
LOOKUP chunk ← chunks[chunkCoord]
IF chunk is null AND voxel is not Empty
  CREATE chunk as new VoxelChunk()
  SET chunks[chunkCoord] ← chunk
ELSE IF chunk is null AND voxel is Empty
  RETURN                                             // No-op: setting empty in empty chunk
COMPUTE lx ← ((coord.X % 16) + 16) % 16
COMPUTE ly ← ((coord.Y % 16) + 16) % 16
COMPUTE lz ← ((coord.Z % 16) + 16) % 16
COMPUTE oldVoxel ← chunk.GetVoxel(lx, ly, lz)
chunk.SetVoxel(lx, ly, lz, voxel)
// Update grid-level voxel count
IF oldVoxel is Empty AND voxel is not Empty
  VoxelCount += 1
ELSE IF oldVoxel is not Empty AND voxel is Empty
  VoxelCount -= 1
// Remove chunk if it became empty
IF chunk.IsEmpty()
  chunks.Remove(chunkCoord)

---

### VoxelChunk.GetVoxel
`(x: int, y: int, z: int) → Voxel`

COMPUTE index ← x + z * 16 + y * 256
RETURN CREATE Voxel(paletteIndices[index], (VoxelFlags)flags[index])

---

### VoxelChunk.SetVoxel
`(x: int, y: int, z: int, voxel: Voxel) → void`

COMPUTE index ← x + z * 16 + y * 256
COMPUTE wasEmpty ← paletteIndices[index] == 0
SET paletteIndices[index] ← voxel.PaletteIndex
SET flags[index] ← (byte)voxel.Flags
COMPUTE isNowEmpty ← voxel.PaletteIndex == 0
IF wasEmpty AND NOT isNowEmpty
  NonEmptyCount += 1
ELSE IF NOT wasEmpty AND isNowEmpty
  NonEmptyCount -= 1
SET IsDirty ← true

---

### Palette.GetOrAddIndex
`(color: Color, material: MaterialType, roughness: float) → byte`

// Full tuple lookup — same color with different material/roughness gets separate index
COMPUTE key ← (color, material, roughness)
IF entryIndex.TryGetValue(key, out existingIndex)
  RETURN existingIndex
VALIDATE usedCount < 255                             → InvalidOperationException("Palette full")
SET usedCount += 1
SET entries[usedCount] ← CREATE PaletteEntry(color, material, roughness)
SET entryIndex[key] ← (byte)usedCount
RETURN (byte)usedCount

---

### VoxelRay.Cast
`(grid: VoxelGrid, maxSteps: int) → (VoxelCoord hit, VoxelCoord faceNormal)?`

// DDA (Digital Differential Analyzer) algorithm
// Steps through voxels along ray in integer increments
COMPUTE stepX, stepY, stepZ ← sign of direction components (1 or -1)
COMPUTE tMaxX, tMaxY, tMaxZ ← distance to next voxel boundary per axis
COMPUTE tDeltaX, tDeltaY, tDeltaZ ← distance between voxel boundaries per axis
SET current ← origin
SET lastFace ← (0, 0, 0)
ITERATE steps FROM 0 TO maxSteps
  IF grid.Contains(current) AND NOT grid.IsEmpty(current)
    RETURN (current, lastFace)
  // Step to nearest voxel boundary
  IF tMaxX < tMaxY AND tMaxX < tMaxZ
    SET current.X += stepX
    SET tMaxX += tDeltaX
    SET lastFace ← (-stepX, 0, 0)
  ELSE IF tMaxY < tMaxZ
    SET current.Y += stepY
    SET tMaxY += tDeltaY
    SET lastFace ← (0, -stepY, 0)
  ELSE
    SET current.Z += stepZ
    SET tMaxZ += tDeltaZ
    SET lastFace ← (0, 0, -stepZ)
RETURN null                                          // No hit within maxSteps

---

### VoxelSerializer.Serialize
`(grid: VoxelGrid) → byte[]`

CREATE buffer as MemoryStream
// Header (20 bytes — checksum placeholder filled at end)
EMIT "BVOX" magic bytes
EMIT grid format version as uint16
EMIT flags as uint16 (Compressed | HasMetadata | HasPalette)
EMIT grid.chunks.Count as uint32
EMIT grid.VoxelCount as uint32
SET checksumOffset ← buffer.Position
EMIT 0 as uint32                                     // Placeholder for checksum
SET payloadStart ← buffer.Position

// Bounds section (12 bytes — binary for fast access without parsing JSON metadata)
EMIT grid.Bounds.Min (3x int16)
EMIT grid.Bounds.Max (3x int16)

// Palette section
EMIT grid.Palette.usedCount as uint16
FOREACH entry in grid.Palette.entries[1..usedCount]
  EMIT entry.Color as 4 bytes (RGBA)
  EMIT entry.Material as 1 byte
  EMIT entry.Roughness as float32

// Metadata section
COMPUTE metadataJson ← JsonSerialize(grid.Metadata)
EMIT metadataJson.Length as uint32
EMIT metadataJson as UTF-8 bytes

// Chunk table + data
COMPUTE sortedChunks ← grid.EnumerateChunks() sorted by ChunkCoord
CREATE chunkTable as List<(ChunkCoord, offset, length)>
CREATE chunkDataBuffer as MemoryStream
FOREACH (coord, chunk) in sortedChunks
  // Dual RLE streams: palette indices and flags encoded separately, then combined
  COMPUTE rleIndices ← VoxelCompression.RleEncode(chunk.paletteIndices)
  COMPUTE rleFlags ← VoxelCompression.RleEncode(chunk.flags)
  // Concatenate with length prefix so decoder knows where to split
  CREATE combined ← [rleIndices.Length as uint16] + rleIndices + rleFlags
  COMPUTE compressed ← LZ4.Compress(combined)
  SET offset ← chunkDataBuffer.Position
  EMIT compressed to chunkDataBuffer
  ACCUMULATE (coord, offset, compressed.Length, chunk.NonEmptyCount) to chunkTable

// Write chunk table (14 bytes per entry)
FOREACH entry in chunkTable
  EMIT entry.coord (3x int16) + entry.offset (uint32) + entry.length (uint16) + entry.nonEmptyCount (uint16)

// Write chunk data
EMIT chunkDataBuffer contents

// Compute and write checksum over all payload bytes (everything after header)
COMPUTE payload ← buffer bytes from payloadStart to end
COMPUTE checksum ← xxHash32(payload)
SET buffer.Position ← checksumOffset
EMIT checksum as uint32

RETURN buffer.ToArray()

---

### VoxelSerializer.Deserialize
`(data: byte[]) → VoxelGrid`

CREATE reader from data
// Header (20 bytes)
VALIDATE magic == "BVOX"                             → FormatException
COMPUTE version ← read uint16
COMPUTE flags ← read uint16
COMPUTE chunkCount ← read uint32
COMPUTE voxelCount ← read uint32
COMPUTE storedChecksum ← read uint32

// Verify checksum over all payload bytes (everything after header)
COMPUTE payload ← data bytes from offset 20 to end
COMPUTE computedChecksum ← xxHash32(payload)
VALIDATE storedChecksum == computedChecksum           → FormatException("Checksum mismatch")

// Bounds (12 bytes)
COMPUTE boundsMin ← read 3x int16 as VoxelCoord
COMPUTE boundsMax ← read 3x int16 as VoxelCoord
COMPUTE bounds ← VoxelBounds(boundsMin, boundsMax)

// Palette
COMPUTE entryCount ← read uint16
CREATE palette as new Palette()
ITERATE i FROM 1 TO entryCount
  COMPUTE color ← read 4 bytes as Color
  COMPUTE material ← read 1 byte as MaterialType
  COMPUTE roughness ← read float32
  palette.Set(i, CREATE PaletteEntry(color, material, roughness))

// Metadata
COMPUTE metaLength ← read uint32
COMPUTE metadataJson ← read metaLength bytes as UTF-8
COMPUTE metadata ← JsonDeserialize<GridMetadata>(metadataJson)

// Chunk table (14 bytes per entry)
CREATE chunkEntries as (ChunkCoord, uint32 offset, uint16 length, uint16 nonEmptyCount)[chunkCount]
ITERATE i FROM 0 TO chunkCount - 1
  chunkEntries[i] ← read (3x int16, uint32, uint16, uint16)

// Chunk data
CREATE grid as new VoxelGrid(bounds, palette, metadata)
FOREACH entry in chunkEntries
  COMPUTE compressedData ← read entry.length bytes at entry.offset
  COMPUTE combined ← LZ4.Decompress(compressedData)
  // Split dual RLE streams using length prefix
  COMPUTE rleIndicesLength ← read uint16 from combined[0..2]
  COMPUTE rleIndices ← combined[2 .. 2 + rleIndicesLength]
  COMPUTE rleFlags ← combined[2 + rleIndicesLength ..]
  COMPUTE paletteIndices ← VoxelCompression.RleDecode(rleIndices)
  COMPUTE flags ← VoxelCompression.RleDecode(rleFlags)
  CREATE chunk from (paletteIndices, flags)
  SET grid.chunks[entry.coord] ← chunk

RETURN grid

---

### VoxelDelta.Compute
`(oldGrid: VoxelGrid, newGrid: VoxelGrid) → byte[]`

CREATE buffer as MemoryStream
// Find added chunks (in new, not in old)
COMPUTE addedCoords ← newGrid.chunks.Keys EXCEPT oldGrid.chunks.Keys
// Find removed chunks (in old, not in new)
COMPUTE removedCoords ← oldGrid.chunks.Keys EXCEPT newGrid.chunks.Keys
// Find modified chunks (in both, dirty in new)
COMPUTE modifiedCoords ← newGrid.GetDirtyChunks() INTERSECT oldGrid.chunks.Keys

EMIT addedCoords.Count as uint32
FOREACH coord in addedCoords
  EMIT coord (3x int16)
  EMIT VoxelSerializer.SerializeChunk(newGrid.GetChunk(coord))

EMIT removedCoords.Count as uint32
FOREACH coord in removedCoords
  EMIT coord (3x int16)

EMIT modifiedCoords.Count as uint32
FOREACH coord in modifiedCoords
  EMIT coord (3x int16)
  // Binary diff of flat arrays (paletteIndices + flags)
  COMPUTE oldBytes ← oldGrid.GetChunk(coord) flat arrays
  COMPUTE newBytes ← newGrid.GetChunk(coord) flat arrays
  COMPUTE diff ← binary XOR diff with RLE of non-zero runs
  EMIT diff

RETURN buffer.ToArray()

---

### VoxelDelta.Apply
`(grid: VoxelGrid, delta: byte[]) → void`

CREATE reader from delta
// Apply added chunks
COMPUTE addedCount ← read uint32
ITERATE addedCount times
  COMPUTE coord ← read ChunkCoord
  COMPUTE chunk ← VoxelSerializer.DeserializeChunk(read chunk bytes)
  SET grid.chunks[coord] ← chunk

// Apply removed chunks
COMPUTE removedCount ← read uint32
ITERATE removedCount times
  COMPUTE coord ← read ChunkCoord
  grid.chunks.Remove(coord)

// Apply modified chunks
COMPUTE modifiedCount ← read uint32
ITERATE modifiedCount times
  COMPUTE coord ← read ChunkCoord
  COMPUTE diff ← read diff bytes
  LOOKUP chunk ← grid.chunks[coord]
  // Apply XOR diff to flat arrays
  ITERATE i FROM 0 TO diff.Length
    chunk.paletteIndices[i] XOR= diff.paletteData[i]
    chunk.flags[i] XOR= diff.flagData[i]
  chunk.IsDirty ← true

// Recalculate VoxelCount
grid.VoxelCount ← SUM of all chunk.NonEmptyCount

---

### HeightmapVoxelizer.Voxelize
`(heights: float[,], materials: byte[,], palette: Palette, options: VoxelizationOptions) → VoxelGrid`

// Heights is a 2D array indexed [x, z] with world-space height values
// Materials is a parallel 2D array [x, z] with palette indices per column
COMPUTE gridWidth ← heights.GetLength(0)
COMPUTE gridDepth ← heights.GetLength(1)
COMPUTE maxHeight ← ceiling(max value in heights / options.VoxelScale)
CREATE grid ← new VoxelGrid(VoxelBounds(0, 0, 0, gridWidth-1, maxHeight, gridDepth-1), palette)

FOREACH x FROM 0 TO gridWidth - 1
  FOREACH z FROM 0 TO gridDepth - 1
    COMPUTE worldHeight ← heights[x, z]
    COMPUTE voxelHeight ← floor(worldHeight / options.VoxelScale)
    COMPUTE paletteIndex ← materials[x, z]
    IF paletteIndex == 0
      paletteIndex ← options.DefaultPaletteIndex
    // Fill column from bottom to height
    FOREACH y FROM 0 TO voxelHeight
      COMPUTE flags ← VoxelFlags.None
      // Mark frozen border: within FrozenBorderWidth of grid edge
      IF x < options.FrozenBorderWidth OR x >= gridWidth - options.FrozenBorderWidth
         OR z < options.FrozenBorderWidth OR z >= gridDepth - options.FrozenBorderWidth
         OR y == voxelHeight  // Top surface of border columns is also frozen
        SET flags ← flags | VoxelFlags.Frozen
      grid.SetVoxel(CREATE VoxelCoord(x, y, z), CREATE Voxel(paletteIndex, flags))

RETURN grid

---

### MeshVoxelizer.Voxelize
`(source: MeshData, palette: Palette, options: VoxelizationOptions) → VoxelGrid`

// Phase 1: Determine grid bounds from source mesh extents
COMPUTE meshMin ← min of all vertices (component-wise)
COMPUTE meshMax ← max of all vertices (component-wise)
COMPUTE gridMin ← floor(meshMin / options.VoxelScale) as VoxelCoord
COMPUTE gridMax ← ceiling(meshMax / options.VoxelScale) as VoxelCoord
CREATE grid ← new VoxelGrid(VoxelBounds(gridMin, gridMax), palette)

// Phase 2: Surface rasterization — 3D scan-convert each triangle
FOREACH triangle (i0, i1, i2) in source.Indices (step 3)
  COMPUTE v0 ← source vertex at i0 / options.VoxelScale
  COMPUTE v1 ← source vertex at i1 / options.VoxelScale
  COMPUTE v2 ← source vertex at i2 / options.VoxelScale
  // Determine palette index from source vertex colors (if present)
  COMPUTE paletteIndex ← options.DefaultPaletteIndex
  IF source.Colors is not null
    COMPUTE avgColor ← average of colors at i0, i1, i2
    paletteIndex ← palette.GetOrAddIndex(avgColor, MaterialType.Diffuse, 0.5f)
  // 3D triangle rasterization: iterate voxels in triangle's bounding box
  COMPUTE triMin ← floor(min(v0, v1, v2)) as VoxelCoord
  COMPUTE triMax ← ceiling(max(v0, v1, v2)) as VoxelCoord
  FOREACH coord in VoxelBounds(triMin, triMax)
    // Test if voxel center intersects the triangle (within half-voxel distance)
    COMPUTE voxelCenter ← (coord.X + 0.5, coord.Y + 0.5, coord.Z + 0.5)
    IF pointTriangleDistance(voxelCenter, v0, v1, v2) < 0.866
      // 0.866 = sqrt(3)/2 — half-diagonal of unit cube
      grid.SetVoxel(coord, CREATE Voxel(paletteIndex, VoxelFlags.None))

// Phase 3: Solid fill (if FillMode == Solid)
IF options.FillMode == Solid
  FOREACH x FROM gridMin.X TO gridMax.X
    FOREACH z FROM gridMin.Z TO gridMax.Z
      // Cast ray along Y axis, count surface crossings
      SET inside ← false
      SET lastSurfaceMaterial ← options.DefaultPaletteIndex
      FOREACH y FROM gridMin.Y TO gridMax.Y
        COMPUTE voxel ← grid.GetVoxel(CREATE VoxelCoord(x, y, z))
        IF voxel is not Empty
          // Surface crossing detected
          SET inside ← NOT inside
          SET lastSurfaceMaterial ← voxel.PaletteIndex
        ELSE IF inside
          // Interior voxel — fill with nearest surface material
          grid.SetVoxel(CREATE VoxelCoord(x, y, z),
            CREATE Voxel(lastSurfaceMaterial, VoxelFlags.None))

// Phase 4: Mark frozen border
FOREACH coord in all non-empty voxels in grid
  COMPUTE distToEdgeX ← min(coord.X - gridMin.X, gridMax.X - coord.X)
  COMPUTE distToEdgeZ ← min(coord.Z - gridMin.Z, gridMax.Z - coord.Z)
  COMPUTE distToEdgeY ← min(coord.Y - gridMin.Y, gridMax.Y - coord.Y)
  IF min(distToEdgeX, distToEdgeZ, distToEdgeY) < options.FrozenBorderWidth
    COMPUTE voxel ← grid.GetVoxel(coord)
    grid.SetVoxel(coord, CREATE Voxel(voxel.PaletteIndex, voxel.Flags | VoxelFlags.Frozen))

RETURN grid

---

### MeshUtility.FlipWindingOrder
`(source: MeshData) → MeshData`

// Swap each triangle's 2nd and 3rd indices: (A, B, C) → (A, C, B)
// This reverses the winding order from CCW (right-handed) to CW (left-handed)
CREATE flippedIndices ← new int[source.Indices.Length]
ITERATE i FROM 0 TO source.Indices.Length - 1 STEP 3
  SET flippedIndices[i]   ← source.Indices[i]       // A stays
  SET flippedIndices[i+1] ← source.Indices[i+2]     // C moves to position 2
  SET flippedIndices[i+2] ← source.Indices[i+1]     // B moves to position 3
// Also negate all normals (front/back flip)
CREATE flippedNormals ← new float[source.Normals.Length]
ITERATE i FROM 0 TO source.Normals.Length - 1
  SET flippedNormals[i] ← -source.Normals[i]
RETURN CREATE MeshData(
  source.Vertices, flippedNormals, source.UVs, flippedIndices,
  source.Colors, source.AmbientOcclusion,
  source.VertexCount, source.TriangleCount)

---

### MeshUtility.MergeMeshData
`(chunks: IReadOnlyList<(MeshData mesh, VoxelCoord offset)>) → MeshData`

// Merge multiple per-chunk meshes into a single MeshData, offsetting vertex positions
COMPUTE totalVertices ← SUM of all chunk.mesh.VertexCount
COMPUTE totalTriangles ← SUM of all chunk.mesh.TriangleCount
ALLOCATE merged arrays for totalVertices / totalTriangles

SET vertexBase ← 0
SET indexBase ← 0
FOREACH (mesh, offset) in chunks
  // Copy vertices with world-space offset applied
  ITERATE i FROM 0 TO mesh.VertexCount - 1
    SET mergedVertices[vertexBase + i].X ← mesh.Vertices[i*3]   + offset.X * scale
    SET mergedVertices[vertexBase + i].Y ← mesh.Vertices[i*3+1] + offset.Y * scale
    SET mergedVertices[vertexBase + i].Z ← mesh.Vertices[i*3+2] + offset.Z * scale
  // Copy normals, UVs, colors, AO directly (no offset needed)
  COPY mesh.Normals → mergedNormals at vertexBase
  COPY mesh.UVs → mergedUVs at vertexBase
  COPY mesh.Colors → mergedColors at vertexBase
  IF mesh.AmbientOcclusion is not null
    COPY mesh.AmbientOcclusion → mergedAO at vertexBase
  // Copy indices with vertex base offset
  ITERATE i FROM 0 TO mesh.Indices.Length - 1
    SET mergedIndices[indexBase + i] ← mesh.Indices[i] + vertexBase
  SET vertexBase += mesh.VertexCount
  SET indexBase += mesh.Indices.Length

RETURN CREATE MeshData(mergedVertices, mergedNormals, mergedUVs, mergedIndices,
  mergedColors, mergedAO, totalVertices, totalTriangles)

---

## Algorithms

### CulledMesher

**Purpose**: Generate mesh faces only where voxel faces are exposed (neighbor is empty).
**Complexity**: O(n) time where n = non-empty voxels, O(f) space where f = exposed faces.
**Reference**: Standard voxel face culling (used by Minecraft, MagicaVoxel viewer). AO algorithm from "Ambient Occlusion for Minecraft-like Worlds" (0fps.net, 2013).

INPUT: VoxelChunk, 6 neighbor chunks (for boundary faces), Palette, MeshingOptions
OUTPUT: MeshData

FOR EACH non-empty voxel at (x, y, z):
  paletteIndex ← chunk.paletteIndices[flatIndex]
  FOR EACH of 6 face directions (+X, -X, +Y, -Y, +Z, -Z):
    neighborCoord ← (x, y, z) + direction
    IF neighbor is empty (either in this chunk or neighbor chunk):
      // Emit a quad for this face
      COMPUTE 4 vertex positions from voxel position + face direction + options.VoxelScale
      COMPUTE normal from face direction

      IF NOT options.CollisionMode
        // UV: texel-centered palette atlas coordinates
        COMPUTE u ← (paletteIndex % 16 + 0.5) / 16.0
        COMPUTE v ← (paletteIndex / 16 + 0.5) / 16.0
        COMPUTE color ← Palette[paletteIndex].Color as 4 bytes

        IF options.AmbientOcclusion
          // Per-vertex AO: for each of 4 vertices on this face, count occluding neighbors
          FOR EACH vertex corner of the quad:
            // Sample 3 edge-adjacent voxels + 1 corner-diagonal voxel
            COMPUTE side1 ← is voxel at (corner + edge1) solid? (0 or 1)
            COMPUTE side2 ← is voxel at (corner + edge2) solid? (0 or 1)
            COMPUTE corner ← is voxel at (corner + diagonal) solid? (0 or 1)
            // AO formula: 3 occluders = darkest, 0 = fully lit
            IF side1 AND side2
              ao ← 0.0    // Both sides solid → corner is fully occluded (no diagonal check needed)
            ELSE
              ao ← 1.0 - (side1 + side2 + corner) / 3.0
            EMIT ao value for this vertex

      EMIT 4 vertices + 2 triangles (6 indices, CCW winding)
      // AO fix: when AO values differ across the quad diagonal, flip the quad split
      // to avoid visual artifacts (the "anisotropy fix")
      IF options.AmbientOcclusion
        IF ao[0] + ao[2] < ao[1] + ao[3]
          // Flip triangle split: (0,1,2)(2,3,0) → (1,2,3)(3,0,1)
          EMIT flipped triangle indices

// Collect all emitted geometry into MeshData arrays
RETURN CREATE MeshData(vertices, normals, uvs, indices, colors, ambientOcclusion)

---

### GreedyMesher

**Purpose**: Merge coplanar adjacent faces of the same palette index into larger quads.
**Complexity**: O(n) time per face direction (6 passes), O(s) space where s = slice area.
**Reference**: Mikola Lysenko, "Meshing in a Minecraft Game" (0fps.net, 2012).

INPUT: VoxelChunk, 6 neighbor chunks, Palette, MeshingOptions
OUTPUT: MeshData

// When AO is enabled, the mask must also encode per-vertex AO to prevent merging
// faces with different AO values (which would produce incorrect lighting).
// The mask key becomes: (paletteIndex, ao0, ao1, ao2, ao3) — only merge faces
// where both the material AND all 4 corner AO values match.

FOR EACH of 6 face directions:
  FOR EACH slice perpendicular to direction:
    // Build a 16x16 mask of exposed faces in this slice
    IF options.AmbientOcclusion
      CREATE mask[16][16] ← (paletteIndex, ao[4]) tuples (0 = no face)
    ELSE
      CREATE mask[16][16] ← paletteIndex only (0 = no face)

    FOR (u, v) in 0..15:
      IF voxel at (u, v) is non-empty AND neighbor in face direction is empty:
        mask[u][v].paletteIndex ← paletteIndex
        IF options.AmbientOcclusion
          // Compute 4-corner AO for this face (same algorithm as CulledMesher)
          mask[u][v].ao ← computeCornerAO(u, v, slice, direction, chunk, neighbors)
      ELSE:
        mask[u][v] ← 0

    // Greedy merge: find maximal rectangles of same mask key
    FOR (u, v) in 0..15:
      IF mask[u][v] != 0:
        key ← mask[u][v]  // (paletteIndex) or (paletteIndex + ao[4])
        // Extend width: how far right can we go with same key?
        width ← 1
        WHILE u + width < 16 AND mask[u + width][v] == key:
          width += 1
        // Extend height: how far down can we go maintaining full width?
        height ← 1
        WHILE v + height < 16:
          CHECK all mask[u..u+width][v + height] == key
          IF any differ: BREAK
          height += 1
        // Emit merged quad
        EMIT 4 vertices spanning (u, v) to (u+width, v+height) at slice depth

        IF NOT options.CollisionMode
          COMPUTE UV from paletteIndex (texel-centered atlas coordinates)
          COMPUTE color from Palette[paletteIndex]
          IF options.AmbientOcclusion
            // All merged faces share the same AO values (guaranteed by merge key)
            EMIT AO values from key.ao for 4 vertices
            // Apply anisotropy fix (same as CulledMesher)

        EMIT 2 triangles (CCW winding)
        // Clear mask for merged region
        FOR (mu, mv) in merged rectangle:
          mask[mu][mv] ← 0

RETURN CREATE MeshData(vertices, normals, uvs, indices, colors, ambientOcclusion)

// NOTE: AO-aware greedy meshing produces more quads than AO-unaware (faces with different
// AO can't merge), but still far fewer than CulledMesher. Typical ratio: 2-5x more quads
// than AO-unaware greedy, but still 3-10x fewer than culled. The visual quality improvement
// is dramatic — without AO-aware merging, merged quads have flat shading across concave corners.

---

### MarchingCubesMesher

**Purpose**: Extract smooth isosurface from voxel density field.
**Complexity**: O(n) time where n = cells in chunk, O(t) space where t = triangles.
**Reference**: Lorensen & Cline, "Marching Cubes" (SIGGRAPH 1987).

INPUT: VoxelChunk, 6 neighbor chunks, Palette, MeshingOptions
OUTPUT: MeshData

// Treat palette index as density (0 = below surface, >0 = above surface)
// AO is not applicable to marching cubes (smooth surfaces don't have voxel corners)
// MeshingOptions.AmbientOcclusion is ignored; AmbientOcclusion output is always null
// Threshold at 0.5 (any non-empty voxel is "solid")
FOR EACH cell (x, y, z) in chunk (15x15x15 cells, each spans 2x2x2 voxels):
  // Sample 8 corner densities
  COMPUTE cornerDensities[8] ← density at each corner of the cell
  // Build cube index from corner sign bits
  COMPUTE cubeIndex ← 8-bit mask (bit set if corner density > threshold)
  IF cubeIndex == 0 OR cubeIndex == 255:
    CONTINUE                                         // Entirely inside or outside
  // Lookup edge intersections from precomputed table
  COMPUTE edgeMask ← edgeTable[cubeIndex]
  // Interpolate vertex positions along intersected edges
  FOR EACH intersected edge:
    COMPUTE vertex position ← linear interpolate between edge endpoints
    COMPUTE normal ← gradient of density field at vertex
  // Lookup triangles from precomputed table
  COMPUTE triangles ← triTable[cubeIndex]
  FOR EACH triangle:
    EMIT 3 vertices + 1 triangle
    // Color from dominant palette index at cell center
    COMPUTE color from Palette[dominant paletteIndex in cell]

RETURN CREATE MeshData(vertices, normals, uvs, indices, colors)

---

### VoxelCompression.RleEncode / RleDecode

**Purpose**: Run-length encode sparse voxel data for compact serialization.
**Complexity**: O(n) time and space where n = array length (4096 for one chunk dimension).

INPUT (encode): byte[4096] (palette indices)
OUTPUT (encode): byte[] (RLE-encoded)

// Format: [count, value] pairs
// count = 1-255 for runs of same value
// When run > 255, emit multiple pairs
SET position ← 0
WHILE position < 4096:
  COMPUTE value ← input[position]
  COMPUTE runLength ← count consecutive equal values from position
  WHILE runLength > 0:
    COMPUTE emitCount ← min(runLength, 255)
    EMIT emitCount as byte
    EMIT value as byte
    runLength -= emitCount
  position += total run length

INPUT (decode): byte[] (RLE-encoded)
OUTPUT (decode): byte[4096]

SET position ← 0
WHILE more RLE data:
  COMPUTE count ← read byte
  COMPUTE value ← read byte
  ITERATE count times:
    output[position] ← value
    position += 1

---

### Mesh Voxelization (3D Scan-Conversion + Ray-Parity Fill)

**Purpose**: Convert triangle mesh to solid voxel representation.
**Complexity**: O(t * b) time where t = triangles, b = average triangle bounding box in voxels. Fill pass: O(w * d * h) where w/d/h = grid dimensions.
**Reference**: Nooruddin & Turk, "Simplification and Repair of Polygonal Models Using Volumetric Techniques" (IEEE TVCG, 2003). Ray-parity approach.

INPUT: MeshData (vertices, indices, colors), Palette, VoxelizationOptions
OUTPUT: VoxelGrid

// Phase 1: Surface rasterization
FOR EACH triangle (v0, v1, v2):
  Scale vertices to voxel space (divide by VoxelScale)
  Compute triangle AABB in voxel coordinates
  FOR EACH voxel in AABB:
    Compute distance from voxel center to triangle surface
    IF distance < half-diagonal of unit cube (sqrt(3)/2 ≈ 0.866):
      Mark voxel as surface (set palette index from vertex color or default)

// Phase 2: Solid interior fill (ray-parity method)
FOR EACH column (x, z):
  Cast ray along Y from bottom to top
  Count surface voxel crossings
  When crossing count is odd → inside; even → outside
  Fill interior voxels with nearest surface material

// Phase 3: Frozen border marking
FOR EACH non-empty voxel within FrozenBorderWidth of any grid edge:
  Set Frozen flag

**Why ray-parity along Y**: Terrain and building meshes are predominantly floor/ceiling surfaces. Casting along Y produces the most reliable inside/outside classification for these geometries. For meshes with significant vertical surfaces (walls), the same approach works because the floor/ceiling surfaces provide the parity boundaries.

**Edge case — open meshes**: If the source mesh isn't watertight (has holes), ray-parity produces incorrect interior classification. The FillMode.Surface option skips Phase 2 entirely, producing only the surface shell. For terrain voxelization, HeightmapVoxelizer is preferred over MeshVoxelizer because heightmaps are inherently watertight (every column has a defined height).

---

## Serialization Formats

### .bvox (Bannou Voxel Binary)

**Purpose**: Native voxel serialization optimized for streaming, delta saves, and server efficiency.
**Byte order**: Little-endian

| Offset | Size | Field | Type | Description |
|--------|------|-------|------|-------------|
| 0 | 4 | Magic | char[4] | "BVOX" |
| 4 | 2 | Version | uint16 | Format version (currently 1) |
| 6 | 2 | Flags | uint16 | Bit 0: Compressed, Bit 1: HasMetadata, Bit 2: HasPalette |
| 8 | 4 | ChunkCount | uint32 | Number of non-empty chunks |
| 12 | 4 | VoxelCount | uint32 | Total non-empty voxels |
| 16 | 4 | Checksum | uint32 | xxHash32 of all bytes after header (offset 20+) |
| 20 | 6 | BoundsMin | int16 x 3 | Grid minimum corner (X, Y, Z) |
| 26 | 6 | BoundsMax | int16 x 3 | Grid maximum corner (X, Y, Z) |
| 32 | 2 | PaletteEntryCount | uint16 | Number of used palette entries |
| 34 | 9*N | PaletteEntries | PaletteEntry[] | Color(4) + Material(1) + Roughness(4) each |
| var | 4 | MetadataLength | uint32 | JSON metadata byte length |
| var | M | Metadata | UTF-8 JSON | GridMetadata serialized |
| var | 14*C | ChunkTable | ChunkEntry[] | Coord(6) + Offset(4) + Length(2) + NonEmptyCount(2) each |
| var | var | ChunkData | byte[] | RLE+LZ4 compressed chunk data |

**Chunk data compression pipeline**: Two separate RLE streams (paletteIndices[4096] + flags[4096]), concatenated with uint16 length prefix on first stream, then LZ4 compressed together.
**Expected compression ratio**: 10:1 to 50:1 for typical voxel structures. Flags stream compresses near-perfectly (mostly zeros).
