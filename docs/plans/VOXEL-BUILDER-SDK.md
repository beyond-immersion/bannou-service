# Voxel Builder SDK - Aspirational Design

> **Status**: Concept / Aspirational
> **Created**: 2026-02-14
> **Author**: Claude (research) + Lysander (direction)
> **Category**: SDK (pure computation library, not a plugin)
> **Location**: `sdks/voxel-builder/` (core), `sdks/voxel-builder-godot/` (engine bridge)

## Executive Summary

An engine-agnostic voxel authoring, generation, and serialization SDK for Bannou. Follows the established SDK pattern (like MusicTheory, SceneComposer, AssetBundler): pure computation with no service dependencies, usable on both client and server, with engine-specific bridge implementations for rendering.

**Native format support**: BlockBench (`.bbmodel`, JSON, MIT) and MagicaVoxel (`.vox`, RIFF binary, free tool). These are the two dominant voxel/block modeling tools in the indie space.

**Not a plugin**: The voxel-builder SDK is consumed by plugins and client tools. It is the computation engine; services provide persistence, distribution, and orchestration.

---

## Why This SDK Exists

### The Content Generation Gap (Geometric Layer)

VISION.md describes a content flywheel that generates both **narrative** content (via Storyline) and **physical** content (via Procedural/Houdini). The Houdini pipeline excels at complex geometry -- terrain, organic shapes, architectural facades -- but is heavyweight (5-30s cold start, license costs, GPU-optional-but-nice).

Voxel building fills a different niche: **discrete, constructive, low-latency geometry** that can be authored interactively by players, NPCs, and procedural systems alike. Where Houdini generates a mountain, the voxel builder lets a dungeon core reshape a corridor, an NPC builder stack a wall brick by brick, or a player sculpt a piece of furniture.

### The Consumers

| Consumer | Use Case | Layer |
|----------|----------|-------|
| **Dungeon cores** | Chamber growth, layout shifting, trap placement, memory manifestation as environmental modification | lib-dungeon (L4) via lib-procedural (L4) |
| **NPC builders** | Building construction driven by ABML behavior plans | Actor (L2) via lib-procedural (L4) |
| **Player housing** | Interactive voxel construction within housing gardens (see [Housing as Garden](#housing-as-garden-not-a-plugin) below) | Gardener (L4) + Scene (L4) + Save-Load (L4) |
| **Player crafting** | Voxel-based object creation (furniture, tools, decorations) | lib-craft (L4, planned -- see [CRAFT.md](../plugins/CRAFT.md)) |
| **Dungeon masters** | Interactive dungeon editing (Pattern A full-garden experience) | lib-dungeon (L4) via Gardener |
| **World seeding** | Bulk generation of voxel-based structures during realm creation | Orchestrator-driven workflows |
| **Content pipeline** | Import from artist tools (BlockBench, MagicaVoxel), export to engine-native mesh | Build-time tooling |

### What Houdini Can't Do (Efficiently)

| Houdini Excels At | Voxel Builder Excels At |
|-------------------|------------------------|
| Organic shapes (terrain, vegetation, erosion) | Discrete structures (walls, rooms, buildings) |
| Complex parametric geometry | Interactive real-time editing |
| Artist-authored HDAs with exposed controls | Programmatic construction (NPC behaviors, procedural rules) |
| Batch generation (PDG parallelization) | Sub-millisecond single-voxel operations |
| High-poly output (LODs, subdivision) | Low-poly / stylized aesthetic (voxel art) |
| Requires worker containers + licensing | Zero dependencies, runs anywhere |

**They compose, not compete.** A Houdini HDA generates a terrain heightmap; the voxel builder fills structures onto that terrain. A dungeon core's `domain_expansion` HDA generates the chamber shell; the voxel builder details the interior. An NPC builder's plan references HDA facades for the exterior; the interior is voxel-built room by room.

---

## Architecture

### The SDK Hierarchy

Following the established pattern where `scene-composer` has an engine-agnostic core and engine-specific bridges:

```
sdks/voxel-builder/                     # Engine-agnostic core (this SDK)
    Pure computation: grids, operations, meshing, import/export, generation
    No service dependencies, no engine dependencies
    Runs on server (lib-procedural worker, save-load serialization)
    Runs on client (in-game voxel editor, content pipeline tools)

sdks/voxel-builder-godot/               # Godot 4.x engine bridge
    Renders voxel grids using Godot's ArrayMesh
    Input handling for voxel editing tools
    Type conversion: SDK (double) ↔ Godot (float)
    Follows the ISceneComposerBridge pattern from scene-composer-godot

(future) sdks/voxel-builder-stride/     # Stride engine bridge
(future) sdks/voxel-builder-unity/      # Unity engine bridge
```

### Core SDK Structure

```
sdks/voxel-builder/
├── Abstractions/
│   ├── IVoxelBuilder.cs              # Main orchestrator interface
│   ├── IVoxelBuilderBridge.cs        # Engine rendering bridge
│   ├── IVoxelStorageClient.cs        # Persistence integration (optional)
│   ├── IVoxelImporter.cs             # Format import interface
│   ├── IVoxelExporter.cs             # Format export interface
│   └── IMesher.cs                    # Meshing algorithm interface
│
├── Grid/
│   ├── VoxelGrid.cs                  # Core sparse 3D voxel grid
│   ├── VoxelChunk.cs                 # 16x16x16 chunk subdivision
│   ├── Voxel.cs                      # Individual voxel: palette index + flags
│   ├── Palette.cs                    # Color/material palette (max 256 entries)
│   ├── VoxelRegion.cs                # AABB region selection within a grid
│   └── GridMetadata.cs               # Grid-level properties (dimensions, origin, scale)
│
├── Operations/
│   ├── IVoxelOperation.cs            # Undoable operation interface
│   ├── OperationStack.cs             # Undo/redo stack (like CommandStack in SceneComposer)
│   ├── PlaceOperation.cs             # Place single voxel
│   ├── EraseOperation.cs             # Remove single voxel
│   ├── FillOperation.cs              # Flood fill within bounds
│   ├── BrushOperation.cs             # Sphere/cube/cylinder brush paint
│   ├── BoxOperation.cs               # Axis-aligned box fill/erase
│   ├── MirrorOperation.cs            # Mirror across axis
│   ├── RotateOperation.cs            # 90-degree rotations
│   ├── CopyPasteOperation.cs         # Region copy, paste at offset
│   ├── ReplaceOperation.cs           # Replace palette index A with B
│   └── CompoundOperation.cs          # Groups multiple operations for atomic undo
│
├── Import/
│   ├── BlockBenchImporter.cs         # .bbmodel (JSON) → VoxelGrid
│   ├── MagicaVoxelImporter.cs        # .vox (RIFF binary) → VoxelGrid
│   └── RawVoxelImporter.cs           # Raw byte array → VoxelGrid (for procedural output)
│
├── Export/
│   ├── BlockBenchExporter.cs         # VoxelGrid → .bbmodel
│   ├── MagicaVoxelExporter.cs        # VoxelGrid → .vox
│   ├── MeshExporter.cs               # VoxelGrid → .glb/.gltf (meshed)
│   └── RawVoxelExporter.cs           # VoxelGrid → raw bytes (for save-load, asset storage)
│
├── Meshing/
│   ├── GreedyMesher.cs               # Greedy meshing (fewer faces, good for solid structures)
│   ├── CulledMesher.cs               # Simple face-culled meshing (fast, correct)
│   ├── MarchingCubesMesher.cs        # Smooth surface extraction (terrain-like)
│   └── MeshData.cs                   # Engine-agnostic mesh output (vertices, normals, UVs, indices)
│
├── Generation/
│   ├── IVoxelGenerator.cs            # Generator interface (deterministic: params + seed → grid)
│   ├── NoiseGenerator.cs             # Perlin/simplex noise for terrain
│   ├── PrimitiveGenerator.cs         # Box, sphere, cylinder, arch, stairs
│   ├── WaveFunctionCollapse.cs       # WFC for pattern-based generation
│   ├── LSystemGenerator.cs           # L-system for branching structures (trees, roots, corridors)
│   └── TemplateStamper.cs            # Stamp pre-built templates into a grid with rotation/variation
│
├── Serialization/
│   ├── VoxelSerializer.cs            # .bvox format: Bannou voxel binary (chunk-based, LZ4)
│   ├── VoxelCompression.cs           # Run-length + LZ4 for sparse grids
│   └── VoxelDelta.cs                 # Binary delta encoding for incremental save
│
├── Math/
│   ├── VoxelCoord.cs                 # Integer 3D coordinate
│   ├── ChunkCoord.cs                 # Chunk-level coordinate
│   ├── VoxelRay.cs                   # Integer raycast through voxel grid (DDA algorithm)
│   └── VoxelBounds.cs                # Integer AABB
│
└── VoxelBuilder.cs                   # Main orchestrator (like SceneComposer.cs)
```

### Engine Bridge (Godot Example)

```
sdks/voxel-builder-godot/
├── GodotVoxelBuilderBridge.cs        # IVoxelBuilderBridge implementation
├── GodotVoxelRenderer.cs             # ArrayMesh generation from MeshData
├── GodotChunkRenderer.cs             # Per-chunk mesh nodes for LOD/culling
├── GodotVoxelInput.cs                # Mouse ray → voxel coordinate picking
├── GodotTypeConverter.cs             # SDK (double/int) ↔ Godot (float/Vector3I)
└── Bannou.Godot.VoxelBuilder.csproj
```

---

## Core Data Model

### VoxelGrid: Sparse Chunk-Based Storage

```csharp
/// <summary>
/// A sparse 3D voxel grid subdivided into 16x16x16 chunks.
/// Only non-empty chunks are allocated. Thread-safe for concurrent read,
/// single-writer for mutations (enforced by operation system).
/// </summary>
public class VoxelGrid
{
    /// <summary>Grid dimensions in voxels (may exceed actual populated area).</summary>
    public VoxelBounds Bounds { get; }

    /// <summary>Palette shared by all voxels in this grid (max 256 entries).</summary>
    public Palette Palette { get; }

    /// <summary>Metadata: name, author, creation date, tags.</summary>
    public GridMetadata Metadata { get; }

    /// <summary>Get voxel at coordinate. Returns Voxel.Empty for unpopulated.</summary>
    public Voxel GetVoxel(VoxelCoord coord);

    /// <summary>Set voxel at coordinate. Creates chunk on first write.</summary>
    public void SetVoxel(VoxelCoord coord, Voxel voxel);

    /// <summary>Enumerate all non-empty chunks.</summary>
    public IEnumerable<(ChunkCoord Coord, VoxelChunk Chunk)> EnumerateChunks();

    /// <summary>Count of non-empty voxels across all chunks.</summary>
    public int VoxelCount { get; }
}
```

### Voxel: Compact Per-Cell Data

```csharp
/// <summary>
/// A single voxel. 2 bytes: palette index (1 byte) + flags (1 byte).
/// Palette index 0 = empty (air). Flags encode per-voxel properties.
/// </summary>
public readonly struct Voxel
{
    /// <summary>Index into the grid's palette. 0 = empty.</summary>
    public byte PaletteIndex { get; }

    /// <summary>Per-voxel flags: visibility, damage state, etc.</summary>
    public VoxelFlags Flags { get; }

    /// <summary>The empty voxel (palette index 0, no flags).</summary>
    public static readonly Voxel Empty = new(0, VoxelFlags.None);
}

[Flags]
public enum VoxelFlags : byte
{
    None = 0,
    Hidden = 1,         // Voxel exists but is not rendered (structural)
    Damaged = 2,        // Visual damage state (cracks, weathering)
    Emissive = 4,       // Voxel emits light (palette color as light color)
    Transparent = 8,    // Voxel is semi-transparent (glass, water)
    // Bits 4-7 reserved for game-specific flags
}
```

### Palette: Material Definitions

```csharp
/// <summary>
/// 256-entry color/material palette shared by all voxels in a grid.
/// Index 0 is always empty (air). Compatible with MagicaVoxel's palette model.
/// </summary>
public class Palette
{
    /// <summary>Get palette entry by index.</summary>
    public PaletteEntry this[byte index] { get; set; }

    /// <summary>Find or create a palette entry for the given color.</summary>
    public byte GetOrAddIndex(Color color, MaterialType material = MaterialType.Diffuse);
}

public readonly struct PaletteEntry
{
    public Color Color { get; }            // RGBA
    public MaterialType Material { get; }  // Diffuse, Metal, Glass, Emit, etc.
    public float Roughness { get; }        // 0.0 (smooth) to 1.0 (rough)
}

public enum MaterialType : byte
{
    Diffuse,    // Standard opaque
    Metal,      // Metallic reflection
    Glass,      // Transparent + refraction
    Emit,       // Light-emitting
    Cloud,      // Volumetric scatter
    // Maps directly to MagicaVoxel material types
}
```

---

## Format Support

### BlockBench (.bbmodel) - MIT Licensed

BlockBench is the dominant free voxel/block modeling tool. Its `.bbmodel` format is JSON-based:

```json
{
    "meta": { "format_version": "4.10", "model_format": "free" },
    "resolution": { "width": 16, "height": 16 },
    "elements": [
        {
            "name": "cube",
            "from": [0, 0, 0],
            "to": [16, 16, 16],
            "faces": {
                "north": { "uv": [0, 0, 16, 16], "texture": 0 },
                ...
            }
        }
    ],
    "outliner": [ ... ],   // Hierarchy groups
    "textures": [ ... ],   // Embedded or referenced
    "animations": [ ... ]  // Optional keyframe data
}
```

**Import**: Elements (cuboids) → voxel grid. Each element maps to a filled region. Per-face UV + texture → palette entry assignment. Outliner hierarchy preserved as grid metadata tags.

**Export**: Greedy-meshed regions → elements. Palette entries → texture atlas. Grid metadata → outliner groups. Animations not supported in initial version (static models only).

**Why BlockBench**: MIT-licensed editor, massive community (Minecraft modding), JSON format is trivially parseable, supports both block models (Minecraft-style) and free-form (generic voxel). Its `free` model format is the most flexible.

### MagicaVoxel (.vox) - Free Tool

MagicaVoxel is the dominant voxel art tool. Its `.vox` format is RIFF-based binary:

```
"VOX " (magic)
150     (version)
MAIN chunk
├── SIZE chunk (grid dimensions)
├── XYZI chunk (voxel coordinates + palette indices)
├── RGBA chunk (256-color palette)
├── MATL chunk (per-palette material properties)
├── nTRN chunk (transform nodes - scene graph)
├── nGRP chunk (group nodes)
└── nSHP chunk (shape nodes with model references)
```

**Import**: XYZI voxel positions → sparse grid population. RGBA palette → Palette. MATL materials → PaletteEntry material types. Scene graph (nTRN/nGRP/nSHP) → multiple grids with relative transforms.

**Export**: Sparse grid → XYZI chunk. Palette → RGBA + MATL chunks. Single grid per model (scene graph export for multi-grid support).

**Why MagicaVoxel**: Free, beloved by voxel artists, binary format is compact, materials map cleanly to our PaletteEntry model. The RIFF structure is well-documented by the community.

### Bannou Voxel Binary (.bvox) - Internal Format

The SDK's native serialization format, optimized for Bannou's storage and streaming needs:

```
Header (16 bytes):
    Magic: "BVOX" (4 bytes)
    Version: uint16
    Flags: uint16 (compressed, has-metadata, has-palette)
    ChunkCount: uint32
    VoxelCount: uint32

Palette Section (variable):
    EntryCount: uint16
    Entries: PaletteEntry[] (color + material + roughness per entry)

Metadata Section (variable, optional):
    JSON-encoded GridMetadata

Chunk Table (8 bytes per chunk):
    ChunkCoord (3x int16) + DataOffset (uint32) + DataLength (uint16)

Chunk Data (variable per chunk):
    Run-length encoded palette indices within 16x16x16 volume
    LZ4 compressed if flag set

    Compression pipeline: sparse grid → RLE within chunks → LZ4 per chunk
    Expected compression ratio: 10:1 to 50:1 for typical voxel structures
```

**Why a custom format**: .bbmodel is JSON (verbose, slow to parse). .vox is compact but non-chunked (can't stream or delta-update). .bvox is chunk-aligned (matching the VoxelGrid's internal structure), enabling:
- Streaming: load visible chunks first, fill in distant chunks later
- Delta saves: only re-serialize modified chunks (feeds into Save-Load's delta system)
- Server efficiency: lib-procedural workers serialize directly to .bvox without format conversion

---

## Meshing Pipeline

The SDK provides multiple meshing strategies, selected based on use case:

### CulledMesher (Default)

Simple per-face culling: for each non-empty voxel, emit faces only where neighbors are empty. Fast, correct, produces the classic "blocky" look.

**Use case**: Real-time editing preview, simple voxel aesthetics.
**Performance**: O(n) where n = non-empty voxels. ~1ms for a 32x32x32 region.

### GreedyMesher (Production)

Merges coplanar adjacent faces of the same palette index into larger quads. Dramatically reduces face count (typically 5-20x reduction over culled meshing).

**Use case**: Final rendering, asset export, collision mesh generation.
**Performance**: O(n) but higher constant factor. ~5ms for a 32x32x32 region.

### MarchingCubesMesher (Terrain)

Smooth surface extraction treating palette values as density. Produces organic-looking surfaces from voxel data.

**Use case**: Terrain blending, cave surfaces, organic structures. The dungeon core's `emit_miasma` capability could modify density values for gradual environmental transformation.
**Performance**: O(n) per chunk. ~10ms for a 32x32x32 region at standard resolution.

### Output: MeshData

All meshers produce the same engine-agnostic output:

```csharp
/// <summary>
/// Engine-agnostic mesh output. Each engine bridge converts this
/// to its native mesh format (ArrayMesh for Godot, Mesh for Stride, etc.).
/// </summary>
public class MeshData
{
    public float[] Vertices { get; }   // xyz triplets
    public float[] Normals { get; }    // xyz triplets
    public float[] UVs { get; }        // uv pairs (palette atlas coordinates)
    public int[] Indices { get; }      // triangle indices
    public float[] Colors { get; }     // rgba per-vertex colors (from palette)
}
```

---

## Generation Subsystem

The generation subsystem produces voxel grids deterministically from parameters and seeds, enabling the same content-addressed caching that lib-procedural uses for Houdini output.

### Determinism Contract

```csharp
/// <summary>
/// All generators implement this interface and MUST be deterministic:
/// same parameters + same seed = identical VoxelGrid output.
/// This enables content-addressed caching: SHA256(generatorId + params + seed) → cached result.
/// </summary>
public interface IVoxelGenerator
{
    /// <summary>Unique identifier for this generator type.</summary>
    string GeneratorId { get; }

    /// <summary>Generate a voxel grid from parameters.</summary>
    VoxelGrid Generate(VoxelGeneratorParameters parameters, int seed);
}
```

### Built-In Generators

| Generator | Output | Use Case |
|-----------|--------|----------|
| **PrimitiveGenerator** | Box, sphere, cylinder, arch, stairs, ramp | Building blocks for NPC construction, dungeon features |
| **NoiseGenerator** | Terrain heightmap, cave density, erosion | Natural surfaces, dungeon cave systems |
| **WaveFunctionCollapse** | Pattern-tiled regions | Dungeon floor patterns, wall textures, city block layouts |
| **LSystemGenerator** | Branching structures | Tree roots penetrating dungeon walls, coral, crystalline growths |
| **TemplateStamper** | Composed multi-template scenes | Stamp a "door" template into a wall, place "window" templates, compose room layouts from prefab pieces |

### WFC for Dungeon Generation

Wave Function Collapse is particularly relevant to the dungeon system. Given a tileset of voxel chunks (corridor, room, junction, dead-end, staircase), WFC can generate connected dungeon layouts that respect adjacency constraints:

```
Input: Tileset of 16x16x16 voxel chunks with adjacency rules
       + boundary constraints (must connect to existing corridors)
       + personality weights (martial dungeons prefer arenas, scholarly prefer libraries)
       + seed for determinism

Output: Connected voxel grid representing a new dungeon wing

Consumers: lib-dungeon (domain_expansion), lib-procedural (as lightweight alternative to Houdini)
```

The tileset itself can be authored in BlockBench or MagicaVoxel, imported via the SDK, and stored in the Asset service as `.bvox` bundles.

---

## Integration Points

### Save-Load (lib-save-load, L4)

Voxel grids are a natural fit for Save-Load's delta save system:

```
VoxelGrid → VoxelSerializer.Serialize() → .bvox bytes → Save-Load save slot

On incremental save:
    Modified chunks identified by dirty flags
    Only dirty chunks re-serialized
    VoxelDelta produces binary diff
    Save-Load stores delta via JSON Patch (or binary patch extension)

On load:
    Base .bvox loaded from Asset service (via Save-Load's MinIO integration)
    Deltas applied in sequence
    VoxelGrid reconstructed
```

The chunk-aligned .bvox format means delta saves are naturally chunk-scoped: a single modified voxel re-serializes one 16x16x16 chunk (~4KB compressed), not the entire grid.

### Asset Service (lib-asset, L3)

Voxel assets stored in MinIO via the Asset service, bundled in `.bannou` format:

| Asset Type | Content | Storage Pattern |
|------------|---------|----------------|
| **Voxel model** | Single .bvox file | `voxel/{gameServiceId}/{assetId}.bvox` |
| **Tileset** | Collection of .bvox chunks + adjacency rules JSON | `.bannou` bundle containing multiple .bvox + manifest |
| **Imported model** | Original .bbmodel or .vox + converted .bvox | Both formats stored; .bvox is the runtime format |
| **Generated output** | Deterministically generated .bvox | Content-addressed cache key: SHA256(generator + params + seed) |

Pre-signed URLs enable direct client download of voxel assets without routing through the WebSocket gateway (following the established Asset service pattern for binary data).

### Procedural Service (lib-procedural, L4)

Two integration paths, composable:

**Path A: Voxel SDK as Procedural Worker Alternative**

For simpler generation that doesn't need Houdini's power, the voxel SDK's generators can run directly in lib-procedural's process:

```
lib-procedural receives generation request
    |
    +--> Template type = "houdini" → route to Houdini worker (existing path)
    +--> Template type = "voxel"  → execute VoxelGenerator in-process (new path)
            |
            +-- PrimitiveGenerator: "Generate a 20x10x20 stone building"
            +-- WFC Generator: "Generate a connected dungeon wing from tileset X"
            +-- NoiseGenerator: "Generate terrain chunk at coordinates Y"
            +-- TemplateStamper: "Stamp door/window/roof templates onto building shell"
            |
            VoxelGrid → MeshExporter.ExportGlb() or VoxelSerializer → .bvox
            → upload to Asset service
            → return asset reference
```

This bypasses the Houdini worker entirely -- no container, no license, sub-second generation. The trade-off is less sophisticated geometry (voxel-resolution, not smooth parametric).

**Path B: Houdini Generates Parameters, SDK Builds Voxels**

Houdini generates high-level parameters (heightmap, density field, structural skeleton) that the voxel SDK interprets:

```
Houdini HDA → outputs heightmap.exr + structure_skeleton.json
    |
    VoxelBuilder.NoiseGenerator → interprets heightmap as terrain voxels
    VoxelBuilder.TemplateStamper → places structures at skeleton positions
    VoxelBuilder.WFC → fills interiors from tileset
    |
    Combined VoxelGrid → .bvox → Asset service
```

This gets the best of both worlds: Houdini's sophisticated generation logic producing the macro structure, the voxel SDK filling in the discrete detail.

### Dungeon Integration (lib-dungeon, L4)

The dungeon-as-actor system is the primary game-side consumer:

| Dungeon Action | Voxel SDK Usage |
|----------------|----------------|
| **domain_expansion** | WFC or Houdini-seeded generation of new chambers |
| **shift_layout** | Structural voxel operations (seal passage = fill region, open passage = erase region) |
| **manifest_memory** | Paint memory-themed voxels into walls/floors (murals, crystalline growths, scorch marks) |
| **emit_miasma** | Modify voxel density values for MarchingCubes rendering of atmospheric effects |
| **activate_trap** | Toggle voxel regions (retractable walls, collapsing floors, revealed pits) |
| **spawn_event_agent** | Place encounter-specific voxel modifications (arena barriers, escape routes) |

The dungeon core's `dungeon_core` seed growth domains map to voxel capabilities:
- `domain_expansion.rooms` threshold → unlocks WFC room generation
- `trap_complexity.mechanical` threshold → unlocks voxel-toggling trap mechanisms
- `memory_depth.manifestation` threshold → unlocks environmental memory painting

### Puppetmaster / Regional Watchers

Regional watchers (divine actors) can trigger voxel modifications via Procedural:

- War god scorches terrain: NoiseGenerator with damage parameters → char/rubble voxels
- Forest god grows vegetation: LSystemGenerator → organic voxel structures
- Commerce god builds trade posts: TemplateStamper with building templates

These modifications are deterministic (same event seed → same modification), persisted via Save-Load, and spatially registered via Mapping.

### Scene Service (lib-scene, L4)

Voxel objects can be represented as scene nodes:

```yaml
# Scene node referencing a voxel asset
- id: "dungeon-chamber-042"
  type: "voxel"                    # New node type for voxel data
  asset:
    type: "bvox"
    assetId: "asset-uuid-here"
  transform:
    position: [100, -20, 50]
    rotation: [0, 0, 0, 1]
    scale: [1, 1, 1]
  annotations:
    voxel.gridScale: 0.25          # Each voxel = 0.25 world units
    voxel.mesher: "greedy"         # Meshing strategy for this node
    voxel.collisionMesher: "culled" # Separate collision mesh strategy
```

The SceneComposer SDK's `ISceneComposerBridge` would need a voxel-aware extension point, or the voxel bridge registers its own node type handler.

### Mapping Service (lib-mapping, L4)

Meshed voxel geometry provides spatial data:

```
VoxelGrid → GreedyMesher → collision mesh → Mapping spatial index
    |
    Mapping can answer: "What affordances exist within 5m?"
    Voxel data adds: "Is this wall destructible?", "Can this floor collapse?",
                     "Is there a hidden passage behind this wall?"
```

Voxel metadata (palette index → material type) informs affordance queries. A `Glass` voxel is breakable; a `Metal` voxel is not. This feeds into NPC GOAP planning ("Can I break through this wall?" depends on voxel material).

### Housing as Garden (Not a Plugin)

Player housing does not require a dedicated `lib-housing` plugin. It composes entirely from existing primitives -- the same pattern as the void/discovery experience in PLAYER-VISION.md, and structurally identical to the dungeon Pattern A garden.

| Housing Concern | Solved By |
|----------------|-----------|
| "A conceptual space the player inhabits" | **Gardener** (garden type: `housing`) |
| "Capabilities emerge from growth" | **Seed** (housing seed type -- phases unlock rooms, decorations, NPC servants, visitor capacity) |
| "Physical layout stored persistently" | **Scene** (node tree of housing objects, including `voxel` node types) + **Save-Load** (versioned persistence with delta saves) |
| "Rendered to the client on demand" | **Scene Composer SDK** + **Voxel Builder SDK** (engine bridges render the scene graph and voxel nodes respectively) |
| "A god tends the space" | **Divine/Puppetmaster** (gardener god-actor manages the housing experience per [BEHAVIORAL-BOOTSTRAP.md](../guides/BEHAVIORAL-BOOTSTRAP.md)) |
| "Items placed in the space" | **Inventory** (housing container) + **Item** (furniture/decorations as item instances) |
| "Visitors can enter" | **Game Session** (housing visit = game session) |
| "Entity events route to player" | **Entity Session Registry** in Connect (L1), per the [Gardener deep dive](../plugins/GARDENER.md) |
| "Furniture crafted by players" | **lib-craft** (L4, planned -- recipe-based production sessions, see [CRAFT.md](../plugins/CRAFT.md)) |

The flow mirrors the void experience exactly:

1. Player selects housing seed from the void
2. Gardener creates `housing` garden instance
3. Gardener god-actor begins tending (spawns NPC servants, manages visitors, routes events)
4. Scene data loaded → SceneComposer renders the node tree, VoxelBuilder renders voxel nodes
5. Player interacts: placing items goes through Inventory/Item, voxel editing goes through the SDK's operation system
6. Seed grows as player engages → unlocks capabilities (bigger space, more decoration slots, advanced building tools)
7. Save-Load persists the scene + voxel delta saves; Asset stores the base voxel data in MinIO

**Why this works without a plugin**: The garden IS the housing instance. The seed IS the capability system. Scene IS the layout. Save-Load IS the persistence. The gardener god-actor IS the experience manager. There is no remaining concern that needs a dedicated service -- only authored content (housing seed type definitions, gardener behavior documents, item templates for furniture, crafting recipes for decoration production).

**Voxel builder's role**: Players voxel-build within their housing garden. The SDK provides the editing operations (brush, fill, place, mirror); the Godot bridge renders the result; Save-Load persists the modified chunks; the gardener god-actor gates what operations are allowed based on the housing seed's capability manifest.

### Crafting Integration (lib-craft, L4)

lib-craft ([CRAFT.md](../plugins/CRAFT.md)) is a planned L4 recipe-based crafting orchestration service. It composes lib-item (L2), lib-inventory (L2), lib-contract (L1), and lib-currency (L2) to provide production, modification, and extraction workflows with seed-backed proficiency, station/tool constraints, and quality formulas.

Voxel-builder integrates with crafting in two ways:

1. **Crafting produces voxel-placeable items**: A crafting recipe's output is an Item instance (furniture, decoration, structural element). That item can be placed into a housing Inventory container and rendered as a scene node -- potentially with voxel geometry attached as the visual representation.

2. **Voxel authoring as a crafting input**: A player sculpts a voxel object using the SDK's editing tools. The completed voxel grid is serialized to .bvox and stored as an Asset. A crafting recipe references this asset as a "custom design" input, producing a placeable Item instance with the player's voxel creation as its visual. This enables player-authored furniture, decorations, and structural elements flowing through the standard item economy.

NPC crafters also participate: their ABML behavior plans include crafting goals (`${craft.best_recipe}`, `${craft.materials_for.*}`), and the crafted items populate the game world's economy. An NPC carpenter building a chair uses TemplateStamper or PrimitiveGenerator for the voxel geometry, then a crafting session to produce the Item instance.

---

## Licensing & Dependencies

### External Format Libraries

| Component | License | Purpose |
|-----------|---------|---------|
| **BlockBench format** | MIT (tool itself) | `.bbmodel` is undocumented but JSON -- we write our own parser |
| **MagicaVoxel format** | Community-documented RIFF | `.vox` spec is community-maintained -- we write our own parser |
| **K4os.Compression.LZ4** | MIT | LZ4 compression for .bvox serialization (already used by AssetBundler) |
| **No external mesh libraries** | N/A | All meshing algorithms implemented from scratch (greedy, marching cubes, WFC) |

**Critical**: No GPL dependencies. Both format parsers are written from scratch -- we don't use BlockBench's source code or MagicaVoxel's SDK (neither exists as a library). We parse the file formats directly, which is clean-room implementation.

### The "No External Mesh Library" Decision

The SDK implements its own meshing algorithms rather than depending on libraries like `MagicaCSG` or similar. Rationale:
1. Voxel meshing algorithms are well-documented and straightforward to implement
2. Zero dependency risk -- no license surprises, no abandoned packages
3. Full control over output format (our MeshData matches our engine bridge pattern)
4. The meshing hot path must be allocation-free for server-side generation at scale

---

## Performance Targets

| Operation | Target | Notes |
|-----------|--------|-------|
| Single voxel place/erase | < 1 microsecond | Direct array write within chunk |
| Brush operation (r=5 sphere) | < 100 microseconds | ~500 voxels modified |
| Culled mesh rebuild (one chunk) | < 1 ms | 16x16x16 = 4096 potential voxels |
| Greedy mesh rebuild (one chunk) | < 5 ms | Face merging pass |
| WFC generation (8x8x8 chunks) | < 500 ms | ~32K voxel positions, constraint propagation |
| Full grid serialization (256x256x256) | < 50 ms | RLE + LZ4, typical 100KB-2MB output |
| .bbmodel import (complex model) | < 100 ms | JSON parse + element → voxel conversion |
| .vox import (256x256x256) | < 50 ms | Binary parse, direct palette mapping |

Server-side generation (lib-procedural path) targets are higher because there's no frame budget:

| Server Operation | Target | Notes |
|-----------------|--------|-------|
| WFC dungeon wing (32x8x32 chunks) | < 5 seconds | Full connected dungeon section |
| Noise terrain chunk (64x64x64) | < 100 ms | Single terrain LOD0 |
| Template-stamped building | < 200 ms | Shell + interior composition |
| Batch mesh export (100 chunks → .glb) | < 2 seconds | Asset pipeline use case |

---

## Implementation Phases

### Phase 0: Core Data Structures

- VoxelGrid, VoxelChunk, Voxel, Palette, VoxelCoord
- Basic operations: place, erase, fill
- .bvox serialization with LZ4 compression
- CulledMesher for basic rendering

**Deliverable**: Can create, modify, serialize, and mesh voxel grids. No import/export, no generation.

### Phase 1: Import/Export

- BlockBench importer (.bbmodel → VoxelGrid)
- MagicaVoxel importer (.vox → VoxelGrid)
- BlockBench exporter (VoxelGrid → .bbmodel)
- MagicaVoxel exporter (VoxelGrid → .vox)
- Mesh exporter (VoxelGrid → .glb via GreedyMesher)

**Deliverable**: Round-trip with artist tools. Content pipeline functional.

### Phase 2: Operations & Editing

- Full operation set (brush, box, mirror, rotate, copy/paste, replace)
- OperationStack with undo/redo
- CompoundOperation for atomic multi-step edits
- IVoxelBuilder orchestrator
- IVoxelBuilderBridge abstraction

**Deliverable**: Complete editing SDK. Client teams can build voxel editors.

### Phase 3: Godot Engine Bridge

- GodotVoxelBuilderBridge implementation
- Per-chunk ArrayMesh rendering with dirty-chunk updates
- Mouse ray → voxel coordinate picking
- Palette-based texture atlas generation
- LOD integration (distant chunks use lower meshing resolution)

**Deliverable**: In-engine voxel editing in Godot.

### Phase 4: Generation Subsystem

- PrimitiveGenerator (box, sphere, arch, stairs)
- NoiseGenerator (Perlin/simplex)
- TemplateStamper (template placement with rotation/variation)
- WaveFunctionCollapse (pattern-tiled generation with adjacency constraints)
- LSystemGenerator (branching structures)

**Deliverable**: Procedural voxel generation. lib-procedural can use voxel generators as lightweight Houdini alternatives.

### Phase 5: Service Integration

- IVoxelStorageClient implementation for Save-Load integration
- Delta serialization (chunk-level dirty tracking → binary delta)
- Asset service integration (upload .bvox to MinIO, bundle in .bannou)
- lib-procedural "voxel" template type registration
- Content-addressed caching for generated grids

**Deliverable**: Full server-side integration. Voxel data persists, caches, and distributes through Bannou infrastructure.

### Phase 6: Advanced Meshing & Polish

- MarchingCubesMesher for smooth terrain
- Mesh LOD generation (multi-resolution chunks)
- Ambient occlusion baking (per-vertex AO from voxel neighbor analysis)
- Palette-aware UV atlasing for textured output

**Deliverable**: Production-quality visual output.

---

## Open Questions

1. **Grid scale**: What world-unit size should one voxel represent? 0.25m (Minecraft-ish), 0.1m (detailed interiors), or configurable per grid? Likely configurable via `GridMetadata.VoxelScale`, with Arcadia defaulting to 0.25m.

2. **Max grid size**: MagicaVoxel caps at 256x256x256. Our sparse chunk system has no theoretical limit, but practical limits for serialization/meshing exist. Recommend 512x512x512 soft cap (2048 potential chunks, ~32K chunks at full density).

3. **Animation support**: BlockBench supports keyframe animation. MagicaVoxel supports frame-based animation. Should the SDK support animated voxel models? Likely deferred -- static models are the priority for dungeon/building use cases.

4. **Multi-grid composition**: Should the SDK support composing multiple VoxelGrids at different transforms (like MagicaVoxel's scene graph)? Likely yes for TemplateStamper workflows, but the scene service may handle multi-grid composition at a higher level.

5. **Texture atlas strategy**: Greedy meshing produces quads that need UV coordinates. Should the palette generate a texture atlas automatically, or should this be a separate export step? The MagicaVoxel approach (per-vertex color, no texture) is simpler; BlockBench approach (per-face texture UVs) is more flexible.

6. **Collision mesh strategy**: Should the SDK generate separate collision meshes (culled, no material, simplified), or should consumers mesh twice with different strategies? Recommend the SDK supports a "collision" meshing pass as a standard output alongside the visual mesh.

---

## Relationship to Existing SDKs

| Existing SDK | Relationship to Voxel Builder |
|-------------|------------------------------|
| **SceneComposer** | Parallel pattern. SceneComposer edits hierarchical scenes with mesh/marker/volume nodes. VoxelBuilder edits voxel grids. A SceneComposer scene can reference voxel assets as "voxel" node types. Both use the engine bridge pattern. |
| **AssetBundler** | Consumer. .bvox files are bundled into .bannou archives by the AssetBundler. VoxelBuilder's RawVoxelExporter produces bytes that AssetBundler packages. |
| **AssetLoader** | Consumer. Engine bridges use AssetLoader to fetch .bvox data from pre-signed URLs, then VoxelSerializer.Deserialize() reconstructs the VoxelGrid. |
| **BehaviorCompiler** | Indirect. ABML behavior expressions can reference voxel state (`${dungeon.room_type}`, `${dungeon.wall_material}`). The VoxelBuilder doesn't know about ABML, but lib-dungeon's variable provider factories expose voxel grid queries to the behavior system. |
| **MusicTheory / StorylineTheory** | Structural parallel. All are pure computation SDKs: deterministic, no service dependencies, usable on both client and server. VoxelBuilder is to spatial content what MusicTheory is to audio content and StorylineTheory is to narrative content. |

---

## The North Star Test

Per VISION.md, every system should serve one or more of the five north stars:

| North Star | How Voxel Builder Serves It |
|------------|---------------------------|
| **Living Game Worlds** | Dungeons physically reshape themselves. NPC builders construct visible structures. Regional watcher events leave physical marks on the world. The world changes geometrically, not just narratively. |
| **The Content Flywheel** | Voxel structures become generative input. A destroyed building's voxel data seeds rubble generation. A dungeon's layout history generates maze variations. Physical content feeds back into the flywheel alongside narrative content. |
| **100,000+ Concurrent AI NPCs** | Lightweight voxel operations (place/erase) can run server-side at scale. NPC builders don't need Houdini workers -- the SDK generates directly in-process. |
| **Ship Games Fast** | Import from BlockBench/MagicaVoxel means artists use familiar tools. The SDK handles the engine integration. New game studios get voxel support without building meshing/serialization from scratch. |
| **Emergent Over Authored** | WFC, L-systems, and noise generators produce emergent geometry from rules, not hand-placed content. Dungeon chambers, buildings, and terrain features emerge from simulation parameters. |

---

*This document captures the voxel builder SDK vision as of its writing date. It is an aspirational design document, not an implementation specification. For implementation, individual phases would produce their own detailed specs. See [PROCEDURAL.md](../plugins/PROCEDURAL.md) for the Houdini generation service that composes with this SDK, and [DUNGEON.md](../plugins/DUNGEON.md) for the primary game-side consumer.*
