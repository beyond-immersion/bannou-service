# Voxel Generator SDK Implementation Map

> **SDK**: voxel-generator
> **Location**: `sdks/voxel-generator/`
> **Layer**: Storyteller
> **Domain**: Voxel
> **Deep Dive**: [docs/sdks/VOXEL-GENERATOR.md](../VOXEL-GENERATOR.md)
> **Status**: Aspirational — pseudo-code represents intended behavior, not verified implementation

---

| Field | Value |
|-------|-------|
| SDK | voxel-generator |
| Layer | Storyteller |
| Public Types | ~15 (5 generator classes, 1 interface, 1 registry, 8 parameter/data types) |
| Public Methods | ~15 |
| Dependencies | voxel-core |
| Deterministic | Yes (seeded — same params + seed = identical output) |
| Allocation-Free Hot Paths | Noise evaluation (per-voxel inner loop) |

---

## Data Structures

### IVoxelGenerator

**Kind**: Interface

| Field | Type | Purpose |
|-------|------|---------|
| `GeneratorId` | `string` | Unique ID for content-addressed caching |
| `Generate(params, seed)` | `VoxelGrid` | Deterministic generation |
| `Validate(params)` | `ValidationResult` | Parameter validation before generation |

### GeneratorRegistry

**Kind**: Class
**Thread Safety**: Concurrent-read after initialization

| Field | Type | Purpose |
|-------|------|---------|
| `generators` | `Dictionary<string, IVoxelGenerator>` | Registered generators by ID |

### WfcTileset

**Kind**: Class

| Field | Type | Purpose |
|-------|------|---------|
| `Tiles` | `IReadOnlyList<WfcTile>` | Available tiles |
| `SocketTypes` | `IReadOnlySet<string>` | All socket type IDs |
| `Weights` | `Dictionary<int, float>?` | Per-tile frequency weights |
| `DefaultWeight` | `float` | Weight for unspecified tiles (default 1.0) |

### WfcTile

**Kind**: Record

| Field | Type | Purpose |
|-------|------|---------|
| `Id` | `int` | Tile index within tileset |
| `Grid` | `VoxelGrid` | 16x16x16 voxel content |
| `Sockets` | `FaceSockets` | Socket IDs for 6 faces (+X, -X, +Y, -Y, +Z, -Z) |
| `Rotations` | `RotationSet` | Valid 90° Y-axis rotations (default: all 4) |
| `Tags` | `IReadOnlySet<string>` | Classification tags |

### FaceSockets

**Kind**: Readonly struct

| Field | Type | Purpose |
|-------|------|---------|
| `PosX` | `string` | Socket ID for +X face |
| `NegX` | `string` | Socket ID for -X face |
| `PosY` | `string` | Socket ID for +Y face |
| `NegY` | `string` | Socket ID for -Y face |
| `PosZ` | `string` | Socket ID for +Z face |
| `NegZ` | `string` | Socket ID for -Z face |

### LSystemGrammar

**Kind**: Record

| Field | Type | Purpose |
|-------|------|---------|
| `Axiom` | `string` | Starting symbol(s) |
| `Rules` | `IReadOnlyList<LSystemRule>` | Production rules |
| `Iterations` | `int` | Number of derivation steps |
| `Angle` | `float` | Branch angle in degrees |
| `LengthScale` | `float` | Voxels per segment |
| `RadiusScale` | `float` | Segment radius in voxels |
| `PaletteIndex` | `byte` | Material to draw with |

### LSystemRule

**Kind**: Record

| Field | Type | Purpose |
|-------|------|---------|
| `Symbol` | `char` | Symbol to replace |
| `Replacement` | `string` | Replacement string |
| `Probability` | `float` | Stochastic rule probability (1.0 = always, <1.0 = random selection) |

### StampPlacement

**Kind**: Record

| Field | Type | Purpose |
|-------|------|---------|
| `Template` | `VoxelGrid` | Template to stamp |
| `Position` | `VoxelCoord` | World position to stamp at |
| `Rotation` | `int` | Y-axis rotation: 0, 90, 180, 270 degrees |
| `PaletteMapping` | `Dictionary<byte, byte>?` | Source → destination palette index mapping |

---

## Dependencies

| Dependency | Type | Usage |
|------------|------|-------|
| voxel-core | SDK (project ref) | VoxelGrid, Palette, VoxelCoord, VoxelBounds |

No external dependencies. Noise functions implemented from scratch for determinism control.

---

## API Index

#### GeneratorRegistry

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| Register | `(IVoxelGenerator) → void` | N/A | Free | Add generator to registry |
| Get | `(string id) → IVoxelGenerator?` | Yes | Free | Lookup by ID |
| GenerateAndCache | `(string id, params, seed) → (VoxelGrid, string cacheKey)` | Yes | Allocating | Generate + compute cache key |

#### PrimitiveGenerator

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| Generate | `(params, seed) → VoxelGrid` | Yes | Allocating | Produce primitive shape |

#### NoiseGenerator

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| Generate | `(params, seed) → VoxelGrid` | Yes | Allocating | Produce noise terrain |

#### WaveFunctionCollapse

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| Generate | `(params, seed) → VoxelGrid` | Yes | Allocating | Produce tiled structure |

#### LSystemGenerator

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| Generate | `(params, seed) → VoxelGrid` | Yes | Allocating | Produce branching structure |

#### TemplateStamper

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| Generate | `(params, seed) → VoxelGrid` | Yes | Allocating | Compose from template placements |

---

## Methods

### GeneratorRegistry.GenerateAndCache
`(id: string, params: VoxelGeneratorParameters, seed: int) → (VoxelGrid, string cacheKey)`

LOOKUP generator ← generators[id]
VALIDATE generator is not null                       → KeyNotFoundException
COMPUTE validation ← generator.Validate(params)
VALIDATE validation.IsValid                          → ArgumentException(validation.Errors)
// Compute content-addressed cache key
COMPUTE cacheKey ← SHA256(id + BannouJson.Serialize(params) + seed.ToString())
COMPUTE grid ← generator.Generate(params, seed)
RETURN (grid, cacheKey)

---

### PrimitiveGenerator.Generate
`(params: VoxelGeneratorParameters, seed: int) → VoxelGrid`

COMPUTE primParams ← params as PrimitiveParameters
CREATE grid ← new VoxelGrid(primParams.Bounds, primParams.Palette ?? defaultPalette)
COMPUTE paletteIndex ← primParams.PaletteIndex ?? 1

IF primParams.Type == Box
  FOREACH coord in VoxelBounds(primParams.Origin, primParams.Origin + primParams.Size)
    grid.SetVoxel(coord, CREATE Voxel(paletteIndex, VoxelFlags.None))

ELSE IF primParams.Type == Sphere
  COMPUTE center ← primParams.Origin + primParams.Size / 2
  COMPUTE radius ← min(primParams.Size.X, primParams.Size.Y, primParams.Size.Z) / 2
  FOREACH coord in bounding box of sphere
    IF VoxelCoord.Distance(coord, center) <= radius
      grid.SetVoxel(coord, CREATE Voxel(paletteIndex, VoxelFlags.None))

ELSE IF primParams.Type == Cylinder
  COMPUTE center ← primParams.Origin + (primParams.Size.X / 2, 0, primParams.Size.Z / 2)
  COMPUTE radius ← min(primParams.Size.X, primParams.Size.Z) / 2
  FOREACH coord in bounding box
    COMPUTE xzDist ← sqrt((coord.X - center.X)² + (coord.Z - center.Z)²)
    IF xzDist <= radius AND coord.Y >= primParams.Origin.Y AND coord.Y < primParams.Origin.Y + primParams.Size.Y
      grid.SetVoxel(coord, CREATE Voxel(paletteIndex, VoxelFlags.None))

ELSE IF primParams.Type == Arch
  // Half-cylinder (top half only) with hollow interior
  COMPUTE center ← primParams.Origin + (primParams.Size.X / 2, 0, primParams.Size.Z / 2)
  COMPUTE outerRadius ← min(primParams.Size.X, primParams.Size.Z) / 2
  COMPUTE innerRadius ← outerRadius - primParams.WallThickness ?? 1
  FOREACH coord in bounding box
    COMPUTE xzDist ← sqrt((coord.X - center.X)² + (coord.Z - center.Z)²)
    COMPUTE heightRatio ← (float)coord.Y / primParams.Size.Y
    IF xzDist <= outerRadius * (1 - heightRatio) AND xzDist >= innerRadius * (1 - heightRatio)
      grid.SetVoxel(coord, CREATE Voxel(paletteIndex, VoxelFlags.None))

ELSE IF primParams.Type == Stairs
  COMPUTE stepCount ← primParams.Size.Y
  COMPUTE stepDepth ← primParams.Size.Z / stepCount
  ITERATE step FROM 0 TO stepCount - 1
    COMPUTE y ← step
    COMPUTE zStart ← step * stepDepth
    COMPUTE zEnd ← zStart + stepDepth
    FOREACH coord where Y == y AND Z in [zStart, zEnd) AND X in origin.X range
      grid.SetVoxel(coord, CREATE Voxel(paletteIndex, VoxelFlags.None))

ELSE IF primParams.Type == Ramp
  FOREACH coord in bounding box
    COMPUTE expectedY ← (float)(coord.Z - primParams.Origin.Z) / primParams.Size.Z * primParams.Size.Y
    IF coord.Y <= expectedY
      grid.SetVoxel(coord, CREATE Voxel(paletteIndex, VoxelFlags.None))

RETURN grid

---

### NoiseGenerator.Generate
`(params: VoxelGeneratorParameters, seed: int) → VoxelGrid`

COMPUTE noiseParams ← params as NoiseParameters
CREATE grid ← new VoxelGrid(noiseParams.Bounds, noiseParams.Palette ?? defaultPalette)
CREATE rng ← new SeededRandom(seed)
// Initialize permutation table from seed for deterministic noise
CREATE permutation ← generatePermutationTable(rng)

FOREACH coord in noiseParams.Bounds
  COMPUTE value ← 0.0
  COMPUTE freq ← noiseParams.Frequency
  COMPUTE amp ← noiseParams.Amplitude
  // Octave stacking for multi-scale detail
  ITERATE octave FROM 0 TO noiseParams.Octaves - 1
    IF noiseParams.NoiseType == Perlin
      value += amp * perlinNoise3D(coord.X * freq, coord.Y * freq, coord.Z * freq, permutation)
    ELSE  // Simplex
      value += amp * simplexNoise3D(coord.X * freq, coord.Y * freq, coord.Z * freq, permutation)
    freq *= noiseParams.Lacunarity ?? 2.0
    amp *= noiseParams.Persistence ?? 0.5

  // Threshold: above = solid, below = empty
  IF value > noiseParams.Threshold
    // Optional: map value range to different palette indices for material variation
    COMPUTE paletteIndex ← noiseParams.PaletteIndex ?? 1
    IF noiseParams.GradientPalette is not null
      COMPUTE normalized ← (value - noiseParams.Threshold) / (1.0 - noiseParams.Threshold)
      COMPUTE gradientIndex ← floor(normalized * noiseParams.GradientPalette.Length)
      paletteIndex ← noiseParams.GradientPalette[clamp(gradientIndex)]
    grid.SetVoxel(coord, CREATE Voxel(paletteIndex, VoxelFlags.None))

RETURN grid

---

### WaveFunctionCollapse.Generate
`(params: VoxelGeneratorParameters, seed: int) → VoxelGrid`

COMPUTE wfcParams ← params as WfcParameters
CREATE rng ← new SeededRandom(seed)

// Expand tileset with rotations
CREATE expandedTiles ← new List<ExpandedTile>()
FOREACH tile in wfcParams.Tileset.Tiles
  FOREACH rotation in tile.Rotations
    CREATE expanded ← new ExpandedTile(tile, rotation, rotatedSockets(tile.Sockets, rotation))
    ACCUMULATE expanded to expandedTiles

// Build adjacency lookup: for each face direction, which tiles can be neighbors?
CREATE adjacency ← buildAdjacencyLookup(expandedTiles)
// adjacency[tileId][faceDirection] = set of compatible tile IDs

// Initialize wave: each output cell starts with all tiles possible
COMPUTE outputDims ← wfcParams.OutputDimensions  // (width, height, depth) in tiles
CREATE wave ← new int[outputDims.X, outputDims.Y, outputDims.Z][]
FOREACH cell in all output cells
  wave[cell] ← all expanded tile IDs (full superposition)

// Apply boundary constraints
FOREACH constraint in wfcParams.Constraints
  // Force specific sockets at boundary cells
  COMPUTE cellCoord ← constraint.CellCoord
  wave[cellCoord] ← filter wave[cellCoord] to tiles matching constraint socket requirements

// Apply personality weights to tile selection probabilities
CREATE weights ← computeWeights(expandedTiles, wfcParams.PersonalityWeights, wfcParams.Tileset)

// Main WFC loop
CREATE collapseHistory ← new Stack<(cellCoord, previousWave)>()  // For backtracking
SET backtrackCount ← 0
SET maxBacktracks ← wfcParams.MaxBacktracks ?? 1000

WHILE any cell has more than 1 possibility
  // Select cell with minimum entropy (fewest remaining possibilities)
  COMPUTE minCell ← cell with min(wave[cell].Length) where Length > 1
  IF minCell is null
    BREAK                                            // All cells collapsed or impossible

  // Save state for backtracking
  PUSH (minCell, copy of wave[minCell]) to collapseHistory

  // Collapse: randomly select one tile, weighted by personality + frequency
  COMPUTE selectedTile ← weightedRandomSelect(wave[minCell], weights, rng)
  SET wave[minCell] ← [selectedTile]

  // Propagate constraints
  CREATE propagateQueue ← new Queue<cellCoord>()
  propagateQueue.Enqueue(minCell)
  SET contradiction ← false

  WHILE propagateQueue is not empty AND NOT contradiction
    COMPUTE current ← propagateQueue.Dequeue()
    FOREACH direction in 6 face directions
      COMPUTE neighbor ← current + direction offset
      IF neighbor is out of bounds: CONTINUE
      // Filter neighbor's possibilities to those compatible with current's collapsed tile(s)
      COMPUTE allowedNeighborTiles ← union of adjacency[t][direction] for each t in wave[current]
      COMPUTE previousCount ← wave[neighbor].Length
      SET wave[neighbor] ← intersect(wave[neighbor], allowedNeighborTiles)
      IF wave[neighbor].Length == 0
        SET contradiction ← true
        BREAK
      IF wave[neighbor].Length < previousCount
        propagateQueue.Enqueue(neighbor)              // Propagation changed this cell; continue

  IF contradiction
    SET backtrackCount += 1
    IF backtrackCount > maxBacktracks
      // Return partial result — let caller handle incomplete generation
      BREAK
    // Backtrack: restore previous state, remove failed tile from possibilities
    COMPUTE (failedCell, previousPossibilities) ← collapseHistory.Pop()
    // Restore wave to pre-collapse state
    SET wave[failedCell] ← previousPossibilities.Remove(selectedTile)
    // Also need to restore propagated changes — simplification: restart from last good state
    // (More sophisticated: maintain arc-consistent state per checkpoint)
    CONTINUE

// Build output VoxelGrid from collapsed wave
COMPUTE voxelDims ← outputDims * 16  // Each tile is 16x16x16
CREATE grid ← new VoxelGrid(VoxelBounds(0, voxelDims - 1))
FOREACH cell in all output cells
  IF wave[cell].Length != 1: CONTINUE                // Uncollapsed (partial result)
  COMPUTE tile ← expandedTiles[wave[cell][0]]
  COMPUTE worldOffset ← cell * 16
  // Stamp tile's VoxelGrid at position, applying rotation
  FOREACH (localCoord, voxel) in tile.Grid voxels
    COMPUTE rotatedLocal ← applyRotation(localCoord, tile.Rotation)
    grid.SetVoxel(worldOffset + rotatedLocal, voxel)

RETURN grid

---

### LSystemGenerator.Generate
`(params: VoxelGeneratorParameters, seed: int) → VoxelGrid`

COMPUTE lParams ← params as LSystemParameters
CREATE rng ← new SeededRandom(seed)

// Phase 1: String derivation
SET current ← lParams.Grammar.Axiom
ITERATE iteration FROM 0 TO lParams.Grammar.Iterations - 1
  CREATE next ← new StringBuilder()
  FOREACH symbol in current
    // Find applicable rules (may be stochastic)
    COMPUTE applicableRules ← lParams.Grammar.Rules where Rule.Symbol == symbol
    IF applicableRules is empty
      next.Append(symbol)                            // No rule = identity
    ELSE IF applicableRules.Count == 1
      next.Append(applicableRules[0].Replacement)
    ELSE
      // Stochastic selection: roll against cumulative probability
      COMPUTE roll ← rng.NextFloat()
      COMPUTE cumulative ← 0.0
      FOREACH rule in applicableRules
        cumulative += rule.Probability
        IF roll <= cumulative
          next.Append(rule.Replacement)
          BREAK
  SET current ← next.ToString()

// Phase 2: Turtle interpretation in 3D voxel space
CREATE grid ← new VoxelGrid(estimateBounds(current, lParams.Grammar))
CREATE turtleStack ← new Stack<TurtleState>()
SET position ← (0.0, 0.0, 0.0)  // Float position for smooth direction
SET direction ← (0.0, 1.0, 0.0)  // Initially pointing up
SET right ← (1.0, 0.0, 0.0)
SET radius ← lParams.Grammar.RadiusScale

FOREACH symbol in current
  IF symbol == 'F'  // Draw forward
    COMPUTE segmentLength ← lParams.Grammar.LengthScale
    // Draw cylinder of voxels from position along direction
    ITERATE step FROM 0 TO segmentLength - 1
      COMPUTE center ← position + direction * step
      // Fill circle perpendicular to direction at this point
      FOREACH offset in circle(radius)
        COMPUTE voxelPos ← round(center + perpendicular offset) as VoxelCoord
        grid.SetVoxel(voxelPos, CREATE Voxel(lParams.Grammar.PaletteIndex, VoxelFlags.None))
    SET position ← position + direction * segmentLength

  ELSE IF symbol == 'f'  // Move forward without drawing
    SET position ← position + direction * lParams.Grammar.LengthScale

  ELSE IF symbol == '+'  // Yaw right
    SET direction ← rotateAroundAxis(direction, up, +lParams.Grammar.Angle)
    SET right ← rotateAroundAxis(right, up, +lParams.Grammar.Angle)

  ELSE IF symbol == '-'  // Yaw left
    SET direction ← rotateAroundAxis(direction, up, -lParams.Grammar.Angle)
    SET right ← rotateAroundAxis(right, up, -lParams.Grammar.Angle)

  ELSE IF symbol == '^'  // Pitch up
    SET direction ← rotateAroundAxis(direction, right, +lParams.Grammar.Angle)

  ELSE IF symbol == 'v'  // Pitch down
    SET direction ← rotateAroundAxis(direction, right, -lParams.Grammar.Angle)

  ELSE IF symbol == '['  // Push state
    PUSH (position, direction, right, radius) to turtleStack

  ELSE IF symbol == ']'  // Pop state
    POP (position, direction, right, radius) from turtleStack
    // Optional: reduce radius after branching for taper effect
    SET radius ← radius * 0.8

RETURN grid

---

### TemplateStamper.Generate
`(params: VoxelGeneratorParameters, seed: int) → VoxelGrid`

COMPUTE stampParams ← params as StampParameters
CREATE rng ← new SeededRandom(seed)

// Compute output bounds encompassing all placements
COMPUTE totalBounds ← union of all placement bounds (position + template size, accounting for rotation)
CREATE grid ← new VoxelGrid(totalBounds)

// Sort placements by priority (if specified) or maintain order
// Later placements overwrite earlier ones at collision points
FOREACH placement in stampParams.Placements
  COMPUTE template ← placement.Template
  FOREACH (coord, voxel) in template non-empty voxels
    // Apply rotation (0, 90, 180, 270 degrees around Y axis)
    COMPUTE rotated ← applyYRotation(coord, placement.Rotation, template.Bounds)
    // Apply palette mapping if specified
    COMPUTE mappedIndex ← voxel.PaletteIndex
    IF placement.PaletteMapping is not null AND placement.PaletteMapping.ContainsKey(voxel.PaletteIndex)
      mappedIndex ← placement.PaletteMapping[voxel.PaletteIndex]
    // Apply position offset
    COMPUTE worldCoord ← rotated + placement.Position
    grid.SetVoxel(worldCoord, CREATE Voxel(mappedIndex, voxel.Flags))

  // Optional: apply random variation based on seed
  IF stampParams.VariationChance > 0
    FOREACH coord in template bounds at world position
      IF rng.NextFloat() < stampParams.VariationChance
        // Randomly remove, shift, or recolor voxels for organic variation
        COMPUTE voxel ← grid.GetVoxel(coord + placement.Position)
        IF voxel is not Empty AND rng.NextFloat() < 0.3
          grid.SetVoxel(coord + placement.Position, Voxel.Empty)  // Random removal

RETURN grid

---

## Algorithms

### Wave Function Collapse (Constraint Propagation + Backtracking)

**Purpose**: Generate connected tiled structures from adjacency-constrained tilesets.
**Complexity**: O(n * t * log(n)) average time where n = output cells, t = tile count. Worst case exponential with backtracking.
**Reference**: Maxim Gumin, "WaveFunctionCollapse" (2016); adapted from Merrell's "Model Synthesis" (2007).

The algorithm maintains a "wave" where each output cell tracks which tiles are still possible. Each step collapses the minimum-entropy cell, then propagates the constraint implications to neighbors. Contradictions trigger backtracking.

**Key adaptation for dungeons**: Boundary constraints allow WFC to generate new wings that connect seamlessly to existing dungeon layout. Personality weights bias tile selection toward thematically appropriate choices without hard-excluding any tile.

### Perlin Noise (3D)

**Purpose**: Generate coherent 3D noise values for terrain and density fields.
**Complexity**: O(1) per evaluation point, O(n) total for n voxels.
**Reference**: Ken Perlin, "Improving Noise" (SIGGRAPH 2002).

Implemented from scratch using a seeded permutation table. The gradient table and hash function are deterministic given the seed. Octave stacking with configurable lacunarity and persistence produces multi-scale detail.

### L-System String Derivation

**Purpose**: Produce complex branching patterns from simple rewriting rules.
**Complexity**: O(m^i) where m = max rule expansion length, i = iterations. Typically bounded by iteration count (3-7).
**Reference**: Prusinkiewicz & Lindenmayer, "The Algorithmic Beauty of Plants" (1990).

Stochastic rules enable natural variation: multiple rules for the same symbol with different probabilities, selected deterministically from the seeded RNG.

---

## Serialization Formats

No custom formats defined by this SDK.

WFC tilesets are stored as .bvox bundles (one .bvox per tile) with a JSON manifest containing adjacency rules and metadata. The manifest format:

```json
{
  "tiles": [
    {
      "id": 0,
      "asset": "tile_corridor_straight.bvox",
      "sockets": { "posX": "open", "negX": "open", "posY": "floor", "negY": "ceiling", "posZ": "wall", "negZ": "wall" },
      "rotations": [0, 90, 180, 270],
      "tags": ["corridor"],
      "weight": 1.0
    }
  ],
  "socketCompatibility": {
    "open": ["open"],
    "wall": ["wall"],
    "door": ["open", "door"],
    "floor": ["floor"],
    "ceiling": ["ceiling"]
  }
}
```

L-system grammars are stored as JSON:

```json
{
  "axiom": "F",
  "rules": [
    { "symbol": "F", "replacement": "FF+[+F-F-F]-[-F+F+F]", "probability": 1.0 }
  ],
  "iterations": 4,
  "angle": 25.0,
  "lengthScale": 3.0,
  "radiusScale": 1.0,
  "paletteIndex": 1
}
```
