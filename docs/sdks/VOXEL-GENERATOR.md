# Voxel Generator SDK Deep Dive

> **SDK**: voxel-generator (not yet created)
> **Location**: `sdks/voxel-generator/` (planned)
> **Layer**: Storyteller
> **Domain**: Voxel
> **Dependencies**: voxel-core (grid, palette, math, serialization)
> **Status**: Aspirational — no code exists.
> **Consumers**: lib-procedural (server-side generation), lib-dungeon (chamber growth), divine actors (environmental marks), content tools (batch generation)
> **Short**: Deterministic procedural voxel generation — WFC, L-systems, noise, primitives, template stamping — with content-addressed caching via seed

---

## Overview

voxel-generator is the storyteller-layer SDK for the voxel domain. It provides deterministic procedural generation algorithms that produce VoxelGrid output from parameters and seeds: Wave Function Collapse for pattern-tiled structures, L-systems for branching organic shapes, Perlin/simplex noise for terrain, geometric primitives for building blocks, and template stamping for composing prefab pieces.

It follows the same pattern as music-storyteller and storyline-storyteller: deterministic seeded generation where `same parameters + same seed = identical output`, enabling content-addressed caching (`SHA256(generatorId + params + seed) → cached result`). The key difference is that voxel-generator produces spatial geometry rather than temporal sequences (music) or narrative structures (storyline).

All generators implement the `IVoxelGenerator` interface and produce VoxelGrid output compatible with voxel-core's meshing and serialization pipeline. The SDK has no service dependencies — lib-procedural wraps it as an HTTP API with Asset service caching.

---

## Consumers

| Consumer | Type | Usage |
|----------|------|-------|
| lib-procedural | Plugin | "voxel" template type — runs generators in-process, caches output in Asset service |
| lib-dungeon | Plugin (indirect) | `domain_expansion` actions route through lib-procedural to WFC/noise generators |
| Divine actors | ABML (indirect) | Regional watcher environmental marks via lib-procedural service calls |
| NPC builders | ABML (indirect) | Building construction goals resolved by lib-procedural voxel generation |
| Content tools | Tool | Batch generation of voxel structures during realm creation / seeding |
| voxel-builder | SDK | TemplateStamper consumes pre-built templates from VoxelBuilder import |

---

## Public API Surface

### Core Interface

| Type | Kind | Purpose |
|------|------|---------|
| `IVoxelGenerator` | Interface | Generator contract: parameters + seed → VoxelGrid. All generators implement this. |
| `VoxelGeneratorParameters` | Record | Base parameter type: bounds, palette reference, generator-specific options. |
| `GeneratorRegistry` | Class | Registry of available generators by ID. Used by lib-procedural for dispatch. |

### Generators

| Type | Kind | Purpose |
|------|------|---------|
| `PrimitiveGenerator` | Class | Box, sphere, cylinder, arch, stairs, ramp. Building blocks for NPC construction and dungeon features. |
| `NoiseGenerator` | Class | Perlin/simplex noise for terrain heightmaps, cave density, erosion. Produces grids that compose with HeightmapVoxelizer output from voxel-core. |
| `WaveFunctionCollapse` | Class | Pattern-tiled generation from adjacency-constrained tilesets. Dungeons, city blocks, floor patterns. |
| `LSystemGenerator` | Class | Branching structures via L-system grammars. Tree roots, coral, crystalline growths, corridors. |
| `TemplateStamper` | Class | Compose scenes from pre-built VoxelGrid templates at positions with rotation/variation. |

### Support Types

| Type | Kind | Purpose |
|------|------|---------|
| `NoiseParameters` | Record | Noise type (Perlin/Simplex), octaves, frequency, amplitude, threshold. |
| `WfcTileset` | Class | Collection of tile VoxelGrids with adjacency rules per face. |
| `WfcTile` | Record | One tile: VoxelGrid (16^3 chunk) + 6 face socket IDs for adjacency matching. |
| `WfcConstraint` | Record | Boundary constraint: must-connect coordinates for integration with existing structures. |
| `LSystemRule` | Record | Production rule: symbol → replacement string. |
| `LSystemGrammar` | Class | Axiom + rules + iteration count + angle/length parameters. |
| `StampPlacement` | Record | Template reference + position + rotation (0/90/180/270) + palette mapping. |
| `PrimitiveType` | Enum | Box, Sphere, Cylinder, Arch, Stairs, Ramp, Wall, Column. |

---

## Data Model

### IVoxelGenerator

```
IVoxelGenerator
├── GeneratorId: string           // Unique ID for content-addressed caching
├── Generate(params, seed) → VoxelGrid  // Deterministic generation
└── Validate(params) → bool       // Parameter validation before generation
```

**Content-addressed caching**: `SHA256(generatorId + Serialize(params) + seed)` produces a cache key. If the cached .bvox exists in Asset service, skip generation entirely. This is the same pattern lib-procedural uses for Houdini output.

### WfcTileset

```
WfcTileset
├── Tiles: IReadOnlyList<WfcTile>     // Available tiles
├── SocketTypes: IReadOnlySet<string> // All socket type IDs
├── Weights: Dictionary<int, float>?  // Per-tile frequency weights (optional)
└── DefaultWeight: float              // Weight for tiles without explicit weight
```

### WfcTile

```
WfcTile
├── Id: int                    // Tile index within tileset
├── Grid: VoxelGrid            // 16x16x16 voxel content (one chunk)
├── Sockets: FaceSockets       // Socket IDs for each face (+X, -X, +Y, -Y, +Z, -Z)
├── Rotations: RotationSet     // Which 90° rotations are valid (default: all 4 Y-axis)
└── Tags: IReadOnlySet<string> // Classification tags (corridor, room, junction, dead-end, staircase)
```

### LSystemGrammar

```
LSystemGrammar
├── Axiom: string                       // Starting symbol(s)
├── Rules: IReadOnlyList<LSystemRule>   // Production rules
├── Iterations: int                     // Number of derivation steps
├── Angle: float                        // Branch angle in degrees
├── LengthScale: float                  // Length per segment in voxels
├── RadiusScale: float                  // Radius per segment in voxels
└── PaletteIndex: byte                  // What material to draw with
```

---

## Computation Pipeline

### PrimitiveGenerator

```
Parameters (type, dimensions, palette index, seed)
  → Create empty VoxelGrid with bounds
  → Switch on PrimitiveType:
     Box:      fill axis-aligned region
     Sphere:   fill voxels within radius (distance check from center)
     Cylinder: fill voxels within radius on XZ, full height on Y
     Arch:     half-cylinder with hollow interior
     Stairs:   stepped fill with configurable rise/run
     Ramp:     diagonal fill from floor to height
     Wall:     thin box (1 voxel thick) at specified orientation
     Column:   thin cylinder (1-2 voxel radius)
  → Return VoxelGrid
```

### NoiseGenerator

```
Parameters (noise type, dimensions, octaves, frequency, amplitude, threshold, seed)
  → Initialize noise function with seed
  → For each (x, y, z) in bounds:
     value ← 0
     For each octave:
       value += amplitude * noise(x * frequency, y * frequency, z * frequency)
       frequency *= lacunarity
       amplitude *= persistence
     If value > threshold:
       SET voxel at (x, y, z) to palette index
  → Return VoxelGrid
```

### WaveFunctionCollapse

```
Parameters (tileset, output dimensions in tiles, boundary constraints, seed, personality weights)
  → Initialize wave: each cell = all possible tiles (superposition)
  → Apply boundary constraints (must-connect cells get forced sockets)
  → Apply personality weights (martial → prefer arenas, scholarly → prefer libraries)
  → WHILE uncollapsed cells exist:
     Select cell with minimum entropy (fewest possibilities)
     Collapse: randomly select one tile (weighted by frequency + personality)
     PROPAGATE constraints to neighbors:
       For each neighbor:
         Remove tiles whose sockets don't match the collapsed tile's face
         If neighbor reduced to 0 possibilities → BACKTRACK
     BACKTRACK if contradiction:
       Undo last collapse, remove that tile from possibilities, try next
  → For each collapsed cell:
     Stamp tile's VoxelGrid at cell position (with rotation)
  → Return composed VoxelGrid
```

### LSystemGenerator

```
Parameters (grammar, seed, palette index)
  → Derive string: apply production rules for N iterations
     axiom → rule application → derived string
  → Interpret derived string as turtle graphics in 3D:
     F = move forward, draw voxels along segment
     + = rotate right by angle
     - = rotate left by angle
     ^ = pitch up
     v = pitch down
     [ = push position/direction to stack
     ] = pop position/direction from stack
     Each segment → cylinder of voxels at current position/direction
  → Return VoxelGrid
```

### TemplateStamper

```
Parameters (placements: list of (template, position, rotation, palette mapping), seed)
  → Create empty VoxelGrid with bounds encompassing all placements
  → For each placement:
     Load template VoxelGrid
     Apply rotation (0/90/180/270 around Y axis)
     Apply palette mapping (source index → destination index)
     For each non-empty voxel in template:
       Compute world position = template local + placement position
       SET voxel in output grid
     // Later placements overwrite earlier ones at collision points
  → Return VoxelGrid
```

---

## Determinism Contract

**All generators are deterministic**: `same parameters + same seed = identical VoxelGrid output`.

This is enforced by:
- Using seeded random number generators (not `Random.Shared`)
- No floating-point platform sensitivity (integer voxel coordinates)
- Deterministic iteration order (sorted collections where order matters)
- No thread-local state

**Content-addressed caching**: The determinism contract enables `SHA256(generatorId + Serialize(params) + seed)` as a cache key. If a cached .bvox exists, the generation can be skipped entirely.

**Noise functions**: The SDK includes its own noise implementation (not a library) to guarantee cross-platform determinism. Perlin and simplex noise use the same gradient tables and permutation arrays regardless of platform.

---

## Performance Targets

| Operation | Target | Context | Notes |
|-----------|--------|---------|-------|
| Primitive (16^3) | < 1 ms | Server | Direct region fill |
| Primitive (large building, 64x32x64) | < 10 ms | Server | Bounds iteration |
| Noise terrain chunk (64^3) | < 100 ms | Server | Per-voxel evaluation |
| WFC (8x8x8 tiles = 128^3 voxels) | < 500 ms | Server | Constraint propagation + backtracking |
| WFC dungeon wing (32x8x32 tiles) | < 5 s | Server | Large-scale connected dungeon section |
| L-system (5 iterations) | < 100 ms | Server | String derivation + turtle interpretation |
| Template stamping (20 templates) | < 200 ms | Server | Sequential copy operations |

Server-side targets are generous because there's no frame budget — these run in lib-procedural worker contexts.

---

## Generation Strategies

### Wave Function Collapse for Dungeons

WFC is the primary generation algorithm for structured spaces. Given a tileset of voxel chunks with adjacency rules, it generates connected layouts respecting constraints.

**Dungeon-specific features**:
- **Boundary constraints**: "This face must connect to an existing corridor" — forces compatibility with the dungeon's current layout
- **Personality weights**: Dungeon personality traits (`${dungeon.personality.*}`) bias tile selection. A martial dungeon prefers arenas and training rooms. A scholarly dungeon prefers libraries and observatories.
- **Tag filtering**: Tile tags (corridor, room, junction, dead-end, staircase) enable structural queries ("at least 2 rooms, at least 1 dead-end")

**Tileset authoring**: Tiles are authored in BlockBench or MagicaVoxel, imported via voxel-builder, and stored in Asset service as .bvox bundles with a JSON manifest containing adjacency rules.

### L-Systems for Organic Structures

L-systems generate branching organic structures that grow through voxel space. Used for:
- Tree roots penetrating dungeon walls
- Coral or crystalline growths
- Cave system corridors (with randomized angles)
- Vine/vegetation patterns on structures

**3D turtle interpretation**: Standard 2D L-system turtle graphics extended to 3D with pitch/yaw/roll. Each segment draws a cylinder of voxels at the current radius.

### Noise for Natural Terrain

Perlin/simplex noise generates natural-looking terrain surfaces, cave interiors, and erosion patterns. Combined with a threshold, noise produces the binary occupied/empty classification needed for voxel grids.

**Octave stacking**: Multiple noise layers at different frequencies produce multi-scale detail (continental shape + mountain ridges + rock detail).

---

## Known Quirks & Caveats

#### Design Considerations (Requires Planning)

- **WFC backtracking depth**: Worst-case WFC can backtrack exponentially. Need a max-backtrack limit that fails gracefully (return partial result + error flag) rather than running forever. The dungeon core should handle partial results by attempting a simpler layout.

- **WFC rotation symmetry**: Tiles may be rotationally symmetric (a straight corridor looks the same rotated 180°). The tileset should support marking symmetry classes to avoid redundant tile possibilities, reducing entropy and improving generation speed.

- **Noise function choice**: Simplex noise is technically patented (expired 2022 for the original patent, but OpenSimplex variants exist). Using OpenSimplex2 (public domain) is the safe choice.

- **L-system segment collision**: Turtle-drawn segments can self-intersect. For corridors this is a feature (loops); for tree roots it may produce visual artifacts. Consider optional collision detection that terminates branches on self-intersection.

- **Cross-generator composition**: A dungeon wing might use WFC for the room layout, noise for cave walls, L-systems for root intrusions, and primitives for structural columns. The composition order and conflict resolution (what happens when two generators write the same voxel?) needs a clear rule. Current design: later placements overwrite, same as TemplateStamper.

---

## Open Questions

1. **WFC solver implementation**: Simple backtracking, or arc consistency (AC-3/AC-4)? AC-3 is more efficient for large grids but more complex to implement. Start with simple backtracking + propagation, optimize later if needed.

2. **Noise implementation**: Write from scratch, or use a known public-domain implementation? Writing from scratch ensures determinism control. Recommend porting OpenSimplex2 (public domain, well-tested).

3. **Template storage format**: Should WFC tilesets and L-system grammars have their own serialization formats, or use JSON? JSON is simpler for authoring and debugging. Binary is more compact for large tilesets (100+ tiles with full 16^3 voxel data each).

4. **Progressive generation**: Should WFC support generating one "section" at a time (for streaming dungeon expansion), or always generate the full output? Progressive WFC is significantly more complex but better matches the dungeon core's incremental growth pattern.

---

## Work Tracking

No work items yet — SDK is pre-implementation.
