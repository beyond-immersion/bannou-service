# Voxel Builder SDK Implementation Map

> **SDK**: voxel-builder
> **Location**: `sdks/voxel-builder/`
> **Layer**: Composer
> **Domain**: Voxel
> **Deep Dive**: [docs/sdks/VOXEL-BUILDER.md](../VOXEL-BUILDER.md)
> **Status**: Aspirational — pseudo-code represents intended behavior, not verified implementation

---

| Field | Value |
|-------|-------|
| SDK | voxel-builder |
| Layer | Composer |
| Public Types | ~24 (8 classes, 3 structs, 4 interfaces, 2 enums, 1 static, 6 operation classes) |
| Public Methods | ~50 |
| Dependencies | voxel-core |
| Deterministic | Yes (same operation sequence → same result) |
| Allocation-Free Hot Paths | None (operations snapshot affected voxels) |

---

## Data Structures

### VoxelBuilder (Orchestrator)

**Kind**: Class
**Thread Safety**: Single-threaded (UI thread)

| Field | Type | Purpose |
|-------|------|---------|
| `Grid` | `VoxelGrid` | Current grid being edited |
| `localStack` | `OperationStack` | Local user's undo/redo (sourceId="local") |
| `externalStacks` | `Dictionary<string, OperationStack>` | Per-source undo histories (created on first use) |
| `Bridge` | `IVoxelBuilderBridge?` | Engine rendering (null for headless) |
| `Storage` | `IVoxelStorageClient?` | Persistence integration (optional) |
| `Options` | `VoxelBuilderOptions` | Configuration |
| `ActivePaletteIndex` | `byte` | Selected palette entry for painting |
| `ActiveBrush` | `BrushShape` | Current brush shape and size |
| `Selection` | `VoxelRegion?` | Current region selection |
| `Clipboard` | `VoxelClipboard?` | Copied region data |
| `OnOperationApplied` | `event<OperationAppliedEventArgs>` | Fires for ALL operations (local + external) |

### OperationAppliedEventArgs

**Kind**: Class

| Field | Type | Purpose |
|-------|------|---------|
| `Operation` | `IVoxelOperation` | The operation that was applied |
| `SerializedBytes` | `byte[]` | Pre-serialized operation for network broadcast |
| `SourceId` | `string` | Who created the operation |
| `AffectedChunks` | `IReadOnlySet<ChunkCoord>` | Chunks modified (for bridge + persistence) |
| `IsUndo` | `bool` | True if this was an undo, not a forward apply |

### OperationStack

**Kind**: Class
**Thread Safety**: Single-threaded

| Field | Type | Purpose |
|-------|------|---------|
| `undoDeque` | `BoundedDeque<IVoxelOperation>` | LIFO with bottom-eviction when MaxDepth exceeded |
| `redoStack` | `Stack<IVoxelOperation>` | Standard LIFO (redo never exceeds undo depth) |
| `SourceId` | `string` | Who owns this stack ("local", "generator", "player-2") |
| `MaxDepth` | `int` | Max undo history (drops oldest when exceeded) |
| `IsModified` | `bool` | Any operations since last save/reset |

### IVoxelOperation

**Kind**: Interface

| Field | Type | Purpose |
|-------|------|---------|
| `Execute(grid)` | `void` | Apply the operation, capturing before-state |
| `Undo(grid)` | `void` | Restore before-state |
| `Serialize()` | `byte[]` | Convert to bytes (excludes before-state snapshot) |
| `AffectedRegion` | `VoxelBounds` | Bounding box of changed voxels |
| `SourceId` | `string` | Who created this operation |
| `OperationType` | `VoxelOperationType` | Type discriminator for deserialization |
| `Description` | `string` | Human-readable name |

### OperationSerializer

**Kind**: Static class

| Field | Type | Purpose |
|-------|------|---------|
| `Serialize(op)` | `byte[]` | Type discriminator byte + type-specific payload |
| `Deserialize(bytes)` | `IVoxelOperation` | Read discriminator, dispatch to type-specific deserializer |

### VoxelOperationType

**Kind**: Enum (byte)

| Value | Operation |
|-------|-----------|
| 0 | Place |
| 1 | Erase |
| 2 | Fill |
| 3 | Brush |
| 4 | Box |
| 5 | Mirror |
| 6 | Rotate |
| 7 | CopyPaste |
| 8 | Replace |
| 9 | Compound |
| 10 | GridPatch |

### GridPatchOperation

**Kind**: Class implements IVoxelOperation

| Field | Type | Purpose |
|-------|------|---------|
| `delta` | `byte[]` | VoxelDelta (chunk-level binary diff from voxel-core) |
| `beforeChunks` | `Dictionary<ChunkCoord, byte[]>` | Serialized pre-patch chunk states for undo |
| `AffectedRegion` | `VoxelBounds` | Union of all affected chunk bounds |
| `SourceId` | `string` | Typically "generator" |
| `OperationType` | `GridPatch (10)` | Serialization discriminator |

### VoxelClipboard

**Kind**: Class

| Field | Type | Purpose |
|-------|------|---------|
| `Voxels` | `Dictionary<VoxelCoord, Voxel>` | Relative-coordinate voxel data |
| `Bounds` | `VoxelBounds` | Bounding box of copied region |
| `PaletteSnapshot` | `PaletteEntry[]` | Palette entries used by copied voxels |

### BrushShape

**Kind**: Record

| Field | Type | Purpose |
|-------|------|---------|
| `Type` | `BrushType` | Sphere, Cube, Cylinder |
| `Radius` | `int` | Brush radius in voxels |

---

## Dependencies

| Dependency | Type | Usage |
|------------|------|-------|
| voxel-core | SDK (project ref) | VoxelGrid, VoxelChunk, Voxel, Palette, MeshData, VoxelCoord, VoxelSerializer, VoxelDelta, IMesher |
| SharpGLTF.Core | NuGet (MIT) | glTF/GLB parsing for GltfImporter (terrain overlay mesh input) |
| System.Text.Json | BCL | BlockBench .bbmodel JSON parsing |

---

## API Index

#### VoxelBuilder

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| CreateEmpty | `(VoxelBounds, VoxelBuilderOptions?) → VoxelBuilder` | Yes | Allocating | Factory: new grid |
| LoadGrid | `(VoxelGrid, VoxelBuilderOptions?) → VoxelBuilder` | Yes | Allocating | Factory: existing grid |
| ExecuteOperation | `(IVoxelOperation) → void` | Yes | Minimal | Run + push to local stack + fire event |
| ApplyExternalOperation | `(IVoxelOperation, string sourceId) → void` | Yes | Minimal | Run + push to external stack + fire event |
| Undo | `() → bool` | Yes | Minimal | Pop local undo, push redo |
| Redo | `() → bool` | Yes | Minimal | Pop local redo, push undo |
| UndoExternal | `(string sourceId) → bool` | Yes | Minimal | Pop external source's undo |
| RedoExternal | `(string sourceId) → bool` | Yes | Minimal | Pop external source's redo |
| Place | `(VoxelCoord, byte paletteIndex) → void` | Yes | Minimal | Shortcut for PlaceOperation |
| Erase | `(VoxelCoord) → void` | Yes | Minimal | Shortcut for EraseOperation |
| BrushPaint | `(VoxelCoord center) → void` | Yes | Allocating | Paint with active brush |
| BrushErase | `(VoxelCoord center) → void` | Yes | Allocating | Erase with active brush |
| Fill | `(VoxelCoord origin, byte paletteIndex, VoxelBounds? limit) → void` | Yes | Allocating | Flood fill |
| BoxFill | `(VoxelBounds region, byte paletteIndex) → void` | Yes | Allocating | Fill box region |
| BoxErase | `(VoxelBounds region) → void` | Yes | Allocating | Erase box region |
| Mirror | `(Axis axis) → void` | Yes | Allocating | Mirror entire grid |
| Rotate90 | `(Axis axis) → void` | Yes | Allocating | Rotate 90° around axis |
| Replace | `(byte fromIndex, byte toIndex) → void` | Yes | Allocating | Replace palette index globally |
| Select | `(VoxelBounds region) → void` | Yes | Free | Set selection |
| ClearSelection | `() → void` | Yes | Free | Clear selection |
| Copy | `() → void` | Yes | Allocating | Copy selection to clipboard |
| Paste | `(VoxelCoord offset) → void` | Yes | Allocating | Paste clipboard at offset |
| BeginCompound | `(string description) → void` | Yes | Minimal | Start grouping operations |
| EndCompound | `() → void` | Yes | Minimal | Finalize compound operation |
| DiffToOperation | `(VoxelGrid before, VoxelGrid after, string sourceId) → GridPatchOperation` | Yes | Allocating | Compute delta, snapshot before-chunks |
| SetBridge | `(IVoxelBuilderBridge) → void` | N/A | Free | Attach engine bridge |
| MarkSaved | `() → void` | N/A | Free | Reset IsModified flag |

#### OperationSerializer

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| Serialize | `(IVoxelOperation) → byte[]` | Yes | Allocating | Type discriminator + payload, no snapshot |
| Deserialize | `(byte[]) → IVoxelOperation` | Yes | Allocating | Read discriminator, dispatch to type deserializer |

#### OperationStack

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| Execute | `(IVoxelOperation, VoxelGrid) → void` | Yes | Minimal | Execute + push undo |
| Undo | `(VoxelGrid) → IVoxelOperation?` | Yes | Free | Pop + undo + push redo |
| Redo | `(VoxelGrid) → IVoxelOperation?` | Yes | Free | Pop + execute + push undo |
| Clear | `() → void` | Yes | Free | Reset both stacks |
| CanUndo | `() → bool` | Yes | Free | Check undo stack |
| CanRedo | `() → bool` | Yes | Free | Check redo stack |

#### GltfImporter

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| Import | `(byte[] data) → MeshData` | Yes | Allocating | .glb → MeshData via SharpGLTF |
| Import | `(Stream stream) → MeshData` | Yes | Allocating | Streaming variant |

#### BlockBenchImporter

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| Import | `(byte[] data) → VoxelGrid` | Yes | Allocating | .bbmodel → VoxelGrid |
| Import | `(Stream stream) → VoxelGrid` | Yes | Allocating | Streaming variant |

#### MagicaVoxelImporter

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| Import | `(byte[] data) → VoxelGrid` | Yes | Allocating | .vox → VoxelGrid |
| Import | `(Stream stream) → VoxelGrid` | Yes | Allocating | Streaming variant |
| ImportMulti | `(byte[] data) → IReadOnlyList<(VoxelGrid, Transform)>` | Yes | Allocating | Multi-model .vox with scene graph |

#### BlockBenchExporter

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| Export | `(VoxelGrid) → byte[]` | Yes | Allocating | VoxelGrid → .bbmodel JSON |

#### MagicaVoxelExporter

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| Export | `(VoxelGrid) → byte[]` | Yes | Allocating | VoxelGrid → .vox RIFF |

#### MeshExporter

| Method | Signature Summary | Deterministic | Allocation | Notes |
|--------|-------------------|---------------|------------|-------|
| ExportGlb | `(VoxelGrid, IMesher?) → byte[]` | Yes | Allocating | VoxelGrid → .glb binary |
| ExportGltf | `(VoxelGrid, IMesher?) → string` | Yes | Allocating | VoxelGrid → .gltf JSON |

---

## Methods

### VoxelBuilder.ExecuteOperation
`(operation: IVoxelOperation) → void`

// Local operation — pushes to local stack
SET operation.SourceId ← "local"
IF compoundActive
  ACCUMULATE operation to compoundOperations list
  operation.Execute(Grid)
  RETURN
localStack.Execute(operation, Grid)
// Serialize for broadcast
COMPUTE serializedBytes ← OperationSerializer.Serialize(operation)
COMPUTE dirtyChunks ← chunks overlapping operation.AffectedRegion
// Fire event for networking layer
FIRE OnOperationApplied(operation, serializedBytes, "local", dirtyChunks, isUndo: false)
// Notify bridge of dirty chunks
IF Bridge is not null
  Bridge.OnChunksModified(dirtyChunks)

---

### VoxelBuilder.ApplyExternalOperation
`(operation: IVoxelOperation, sourceId: string) → void`

// External operation — pushes to source-specific stack
SET operation.SourceId ← sourceId
// Get or create the external stack for this source
IF NOT externalStacks.ContainsKey(sourceId)
  CREATE externalStacks[sourceId] ← new OperationStack(sourceId, Options.MaxUndoDepth)
externalStacks[sourceId].Execute(operation, Grid)
COMPUTE dirtyChunks ← chunks overlapping operation.AffectedRegion
// Fire event (subscribers may re-broadcast for relay or persistence)
COMPUTE serializedBytes ← OperationSerializer.Serialize(operation)
FIRE OnOperationApplied(operation, serializedBytes, sourceId, dirtyChunks, isUndo: false)
// Notify bridge — remote edits become visible immediately
IF Bridge is not null
  Bridge.OnChunksModified(dirtyChunks)

---

### VoxelBuilder.Undo
`() → bool`

// Undo local operation only
IF NOT localStack.CanUndo()
  RETURN false
COMPUTE operation ← localStack.Undo(Grid)
COMPUTE dirtyChunks ← chunks overlapping operation.AffectedRegion
COMPUTE serializedBytes ← OperationSerializer.Serialize(operation)
FIRE OnOperationApplied(operation, serializedBytes, "local", dirtyChunks, isUndo: true)
IF Bridge is not null
  Bridge.OnChunksModified(dirtyChunks)
RETURN true

---

### VoxelBuilder.Redo
`() → bool`

IF NOT localStack.CanRedo()
  RETURN false
COMPUTE operation ← localStack.Redo(Grid)
COMPUTE dirtyChunks ← chunks overlapping operation.AffectedRegion
COMPUTE serializedBytes ← OperationSerializer.Serialize(operation)
FIRE OnOperationApplied(operation, serializedBytes, "local", dirtyChunks, isUndo: false)
IF Bridge is not null
  Bridge.OnChunksModified(dirtyChunks)
RETURN true

---

### VoxelBuilder.UndoExternal
`(sourceId: string) → bool`

IF NOT externalStacks.ContainsKey(sourceId)
  RETURN false
IF NOT externalStacks[sourceId].CanUndo()
  RETURN false
COMPUTE operation ← externalStacks[sourceId].Undo(Grid)
COMPUTE dirtyChunks ← chunks overlapping operation.AffectedRegion
COMPUTE serializedBytes ← OperationSerializer.Serialize(operation)
FIRE OnOperationApplied(operation, serializedBytes, sourceId, dirtyChunks, isUndo: true)
IF Bridge is not null
  Bridge.OnChunksModified(dirtyChunks)
RETURN true

---

### VoxelBuilder.DiffToOperation
`(before: VoxelGrid, after: VoxelGrid, sourceId: string) → GridPatchOperation`

// Compute VoxelDelta between the two grids (uses voxel-core's VoxelDelta.Compute)
COMPUTE delta ← VoxelDelta.Compute(before, after)
// Snapshot the before-state of all affected chunks (for undo)
COMPUTE affectedCoords ← chunks that differ between before and after
CREATE beforeChunks ← new Dictionary<ChunkCoord, byte[]>()
FOREACH coord in affectedCoords
  COMPUTE chunk ← before.GetChunk(coord)
  IF chunk is not null
    SET beforeChunks[coord] ← VoxelSerializer.SerializeChunk(chunk)
  ELSE
    SET beforeChunks[coord] ← null  // Chunk didn't exist before (was added by generator)
// Compute affected region as union of all affected chunk bounds
COMPUTE region ← union of (coord * 16, coord * 16 + 15) for all affectedCoords
RETURN CREATE GridPatchOperation(delta, beforeChunks, region, sourceId)

---

### GridPatchOperation.Execute
`(grid: VoxelGrid) → void`

// Capture before-state if not already captured (receiver-side: DiffToOperation pre-captures for sender)
IF beforeChunks is empty
  COMPUTE affectedCoords ← chunks that will be modified by delta
  FOREACH coord in affectedCoords
    COMPUTE chunk ← grid.GetChunk(coord)
    IF chunk is not null
      SET beforeChunks[coord] ← VoxelSerializer.SerializeChunk(chunk)
    ELSE
      SET beforeChunks[coord] ← null
// Apply the delta
VoxelDelta.Apply(grid, delta)

---

### GridPatchOperation.Undo
`(grid: VoxelGrid) → void`

// Restore each affected chunk to its pre-patch state
FOREACH (coord, serializedChunk) in beforeChunks
  IF serializedChunk is null
    // Chunk didn't exist before — remove it
    grid.chunks.Remove(coord)
  ELSE
    // Restore the pre-patch chunk
    COMPUTE chunk ← VoxelSerializer.DeserializeChunk(serializedChunk)
    SET grid.chunks[coord] ← chunk
// Recalculate VoxelCount
grid.VoxelCount ← SUM of all chunk.NonEmptyCount

---

### OperationSerializer.Serialize
`(operation: IVoxelOperation) → byte[]`

CREATE buffer as MemoryStream
EMIT operation.OperationType as byte                 // Type discriminator
EMIT operation.SourceId as length-prefixed UTF-8
// Type-specific payload (no before-state snapshot — receiver recomputes)
IF operation is PlaceOperation
  EMIT coord (3x int32) + paletteIndex (byte)
ELSE IF operation is EraseOperation
  EMIT coord (3x int32)
ELSE IF operation is BrushOperation
  EMIT center (3x int32) + brushType (byte) + radius (int32) + paletteIndex (byte) + erase (bool)
ELSE IF operation is FillOperation
  EMIT origin (3x int32) + paletteIndex (byte) + limit bounds (6x int32)
ELSE IF operation is BoxOperation
  EMIT bounds (6x int32) + paletteIndex (byte) + erase (bool)
ELSE IF operation is CompoundOperation
  EMIT operation count (int32)
  FOREACH sub-operation:
    EMIT OperationSerializer.Serialize(subOp) as length-prefixed bytes
ELSE IF operation is GridPatchOperation
  EMIT delta.Length (int32) + delta bytes
  // beforeChunks NOT serialized — receiver recomputes from its own grid state
// ... (remaining types follow same pattern)
RETURN buffer.ToArray()

---

### OperationSerializer.Deserialize
`(data: byte[]) → IVoxelOperation`

CREATE reader from data
COMPUTE type ← read byte as VoxelOperationType
COMPUTE sourceId ← read length-prefixed UTF-8
// Dispatch to type-specific deserializer
IF type == Place
  COMPUTE coord ← read 3x int32 as VoxelCoord
  COMPUTE paletteIndex ← read byte
  RETURN CREATE PlaceOperation(coord, paletteIndex, sourceId)
ELSE IF type == Brush
  COMPUTE center ← read 3x int32 as VoxelCoord
  COMPUTE brushType ← read byte as BrushType
  COMPUTE radius ← read int32
  COMPUTE paletteIndex ← read byte
  COMPUTE erase ← read bool
  RETURN CREATE BrushOperation(center, BrushShape(brushType, radius), paletteIndex, erase, sourceId)
ELSE IF type == Compound
  COMPUTE count ← read int32
  CREATE subOps ← new List<IVoxelOperation>()
  ITERATE count times
    COMPUTE subLength ← read int32
    COMPUTE subBytes ← read subLength bytes
    ACCUMULATE OperationSerializer.Deserialize(subBytes) to subOps
  RETURN CREATE CompoundOperation(sourceId, subOps)
ELSE IF type == GridPatch
  COMPUTE deltaLength ← read int32
  COMPUTE delta ← read deltaLength bytes
  RETURN CREATE GridPatchOperation(delta, beforeChunks: empty, region: computed from delta, sourceId)
  // beforeChunks populated during Execute — receiver captures its own before-state
// ... (remaining types follow same pattern)

---

### VoxelBuilder.Place
`(coord: VoxelCoord, paletteIndex: byte) → void`

CREATE op ← new PlaceOperation(coord, paletteIndex)
ExecuteOperation(op)

---

### VoxelBuilder.BrushPaint
`(center: VoxelCoord) → void`

CREATE op ← new BrushOperation(center, ActiveBrush, ActivePaletteIndex, erase: false)
ExecuteOperation(op)

---

### VoxelBuilder.Fill
`(origin: VoxelCoord, paletteIndex: byte, limit: VoxelBounds?) → void`

COMPUTE bounds ← limit ?? Grid.Bounds
CREATE op ← new FillOperation(origin, paletteIndex, bounds)
ExecuteOperation(op)

---

### VoxelBuilder.BeginCompound
`(description: string) → void`

VALIDATE NOT compoundActive                          → InvalidOperationException
SET compoundActive ← true
SET compoundDescription ← description
CREATE compoundOperations ← new List<IVoxelOperation>()

---

### VoxelBuilder.EndCompound
`() → void`

VALIDATE compoundActive                              → InvalidOperationException
SET compoundActive ← false
IF compoundOperations.Count == 0
  RETURN                                             // No-op compound
CREATE compound ← new CompoundOperation(compoundDescription, compoundOperations)
// Already executed individually during BeginCompound/EndCompound span
// Push to local undo stack without re-executing
localStack.PushWithoutExecute(compound)
// Fire event with the full compound for broadcast
COMPUTE serializedBytes ← OperationSerializer.Serialize(compound)
COMPUTE dirtyChunks ← chunks overlapping compound.AffectedRegion
FIRE OnOperationApplied(compound, serializedBytes, "local", dirtyChunks, isUndo: false)

---

### VoxelBuilder.Copy
`() → void`

VALIDATE Selection is not null                       → InvalidOperationException("No selection")
CREATE clipboard ← new VoxelClipboard()
SET clipboard.Bounds ← Selection.Bounds
FOREACH coord in Selection.Bounds
  COMPUTE voxel ← Grid.GetVoxel(coord)
  IF voxel is not Empty
    // Store as relative coordinate from selection min
    COMPUTE relative ← coord - Selection.Bounds.Min
    SET clipboard.Voxels[relative] ← voxel
// Snapshot palette entries referenced by copied voxels
COMPUTE usedIndices ← distinct PaletteIndex values in clipboard.Voxels
FOREACH index in usedIndices
  SET clipboard.PaletteSnapshot[index] ← Grid.Palette.Get(index)
SET Clipboard ← clipboard

---

### VoxelBuilder.Paste
`(offset: VoxelCoord) → void`

VALIDATE Clipboard is not null                       → InvalidOperationException("Nothing to paste")
CREATE op ← new CopyPasteOperation(Clipboard, offset, Grid.Palette)
ExecuteOperation(op)

---

### OperationStack.Execute
`(operation: IVoxelOperation, grid: VoxelGrid) → void`

operation.Execute(grid)
undoStack.Push(operation)
redoStack.Clear()                                    // Redo history invalidated by new action
// Enforce max depth
WHILE undoStack.Count > MaxDepth
  // Drop oldest (bottom of stack — requires dequeue from bottom)
  undoStack.RemoveBottom()
SET IsModified ← true

---

### OperationStack.Undo
`(grid: VoxelGrid) → IVoxelOperation?`

IF undoStack.Count == 0
  RETURN null
COMPUTE op ← undoStack.Pop()
op.Undo(grid)
redoStack.Push(op)
RETURN op

---

### PlaceOperation.Execute
`(grid: VoxelGrid) → void`

// Check frozen flag — skip if frozen and enforcement enabled
COMPUTE existing ← grid.GetVoxel(Coord)
IF existing.Flags has VoxelFlags.Frozen AND options.EnforceFrozen
  SET skipped ← true
  RETURN
// Snapshot before-state for undo
SET previousVoxel ← existing
grid.SetVoxel(Coord, CREATE Voxel(PaletteIndex, VoxelFlags.None))

---

### PlaceOperation.Undo
`(grid: VoxelGrid) → void`

grid.SetVoxel(Coord, previousVoxel)

---

### BrushOperation.Execute
`(grid: VoxelGrid) → void`

// Compute affected coordinates within brush volume
COMPUTE bounds ← VoxelBounds(Center - Radius, Center + Radius)
CREATE snapshot ← new Dictionary<VoxelCoord, Voxel>()
FOREACH coord in bounds
  IF NOT grid.Contains(coord)
    CONTINUE
  // Test if coord is within brush shape
  COMPUTE inBrush ← false
  IF Brush.Type == Sphere
    inBrush ← VoxelCoord.Distance(coord, Center) <= Brush.Radius
  ELSE IF Brush.Type == Cube
    inBrush ← true                                  // All coords in bounds are in cube
  ELSE IF Brush.Type == Cylinder
    COMPUTE xzDist ← sqrt((coord.X - Center.X)² + (coord.Z - Center.Z)²)
    inBrush ← xzDist <= Brush.Radius
  IF inBrush
    COMPUTE existing ← grid.GetVoxel(coord)
    // Skip frozen voxels — brush stroke flows around frozen border
    IF existing.Flags has VoxelFlags.Frozen AND options.EnforceFrozen
      CONTINUE
    SET snapshot[coord] ← existing
    IF Erase
      grid.SetVoxel(coord, Voxel.Empty)
    ELSE
      grid.SetVoxel(coord, CREATE Voxel(PaletteIndex, VoxelFlags.None))
SET beforeSnapshot ← snapshot

---

### BrushOperation.Undo
`(grid: VoxelGrid) → void`

FOREACH (coord, voxel) in beforeSnapshot
  grid.SetVoxel(coord, voxel)

---

### FillOperation.Execute
`(grid: VoxelGrid) → void`

// BFS flood fill from origin, bounded by Limit
COMPUTE targetIndex ← grid.GetVoxel(Origin).PaletteIndex
IF targetIndex == FillPaletteIndex
  RETURN                                             // No-op: filling with same color
CREATE snapshot ← new Dictionary<VoxelCoord, Voxel>()
CREATE queue ← new Queue<VoxelCoord>()
CREATE visited ← new HashSet<VoxelCoord>()
queue.Enqueue(Origin)
visited.Add(Origin)
WHILE queue is not empty
  COMPUTE coord ← queue.Dequeue()
  COMPUTE voxel ← grid.GetVoxel(coord)
  IF voxel.PaletteIndex != targetIndex
    CONTINUE
  // Skip frozen voxels — flood fill flows around frozen border
  IF voxel.Flags has VoxelFlags.Frozen AND options.EnforceFrozen
    CONTINUE
  SET snapshot[coord] ← voxel
  grid.SetVoxel(coord, CREATE Voxel(FillPaletteIndex, voxel.Flags))
  // Enqueue 6-connected neighbors within bounds
  FOREACH neighbor in 6-connected(coord)
    IF Limit.Contains(neighbor) AND NOT visited.Contains(neighbor)
      visited.Add(neighbor)
      queue.Enqueue(neighbor)
SET beforeSnapshot ← snapshot

---

### FillOperation.Undo
`(grid: VoxelGrid) → void`

FOREACH (coord, voxel) in beforeSnapshot
  grid.SetVoxel(coord, voxel)

---

### MirrorOperation.Execute
`(grid: VoxelGrid) → void`

// Snapshot entire grid (mirror is global)
CREATE snapshot ← new Dictionary<VoxelCoord, Voxel>()
FOREACH (chunkCoord, chunk) in grid.EnumerateChunks()
  FOREACH local (x, y, z) in 0..15
    COMPUTE worldCoord ← chunkCoord * 16 + (x, y, z)
    COMPUTE voxel ← chunk.GetVoxel(x, y, z)
    IF voxel is not Empty
      SET snapshot[worldCoord] ← voxel
// Clear grid
FOREACH coord in snapshot.Keys
  grid.SetVoxel(coord, Voxel.Empty)
// Write mirrored
COMPUTE center ← (grid.Bounds.Min + grid.Bounds.Max) / 2
FOREACH (coord, voxel) in snapshot
  COMPUTE mirrored ← coord
  IF Axis == X: mirrored.X ← center.X * 2 - coord.X
  IF Axis == Y: mirrored.Y ← center.Y * 2 - coord.Y
  IF Axis == Z: mirrored.Z ← center.Z * 2 - coord.Z
  grid.SetVoxel(mirrored, voxel)
SET beforeSnapshot ← snapshot

---

### CompoundOperation.Execute
`(grid: VoxelGrid) → void`

// Operations were already executed individually during BeginCompound/EndCompound
// This is only called on Redo
FOREACH op in Operations
  op.Execute(grid)

---

### CompoundOperation.Undo
`(grid: VoxelGrid) → void`

// Undo sub-operations in reverse order
FOREACH op in subOperations.Reverse()
  op.Undo(grid)

---

### BlockBenchImporter.Import
`(data: byte[]) → VoxelGrid`

COMPUTE json ← UTF-8 decode data
COMPUTE doc ← JsonDocument.Parse(json)
// Extract resolution
COMPUTE resolution ← doc.root["resolution"] → (width, height)
// Extract textures → palette mapping
CREATE palette ← new Palette()
FOREACH texture in doc.root["textures"]
  // BlockBench textures are base64 PNG or file references
  // Sample dominant colors → palette entries
  COMPUTE dominantColor ← extract from texture data
  palette.GetOrAddIndex(dominantColor)
// Extract elements → voxel regions
CREATE grid ← new VoxelGrid(computed bounds, palette)
FOREACH element in doc.root["elements"]
  COMPUTE from ← element["from"] as (float, float, float)
  COMPUTE to ← element["to"] as (float, float, float)
  // Convert element cuboid to voxel coordinates
  COMPUTE voxelFrom ← floor(from) as VoxelCoord
  COMPUTE voxelTo ← ceil(to) as VoxelCoord
  // Determine palette index from face textures
  COMPUTE paletteIndex ← resolve from element.faces[*].texture
  // Fill voxel region
  FOREACH coord in VoxelBounds(voxelFrom, voxelTo)
    grid.SetVoxel(coord, CREATE Voxel(paletteIndex, VoxelFlags.None))
// Extract outliner hierarchy → metadata tags
COMPUTE tags ← extract group names from doc.root["outliner"]
SET grid.Metadata.Tags ← tags
RETURN grid

---

### MagicaVoxelImporter.Import
`(data: byte[]) → VoxelGrid`

CREATE reader ← new BinaryReader(data, littleEndian)
// Validate header
COMPUTE magic ← read 4 chars
VALIDATE magic == "VOX "                             → FormatException
COMPUTE version ← read int32
// Parse MAIN chunk
COMPUTE mainId ← read 4 chars
VALIDATE mainId == "MAIN"
COMPUTE mainContentSize ← read int32
COMPUTE mainChildrenSize ← read int32
// Parse child chunks
CREATE palette ← new Palette()
CREATE grid ← null
WHILE more data in MAIN children
  COMPUTE chunkId ← read 4 chars
  COMPUTE contentSize ← read int32
  COMPUTE childrenSize ← read int32
  IF chunkId == "SIZE"
    COMPUTE sizeX ← read int32
    COMPUTE sizeY ← read int32
    COMPUTE sizeZ ← read int32
    CREATE grid ← new VoxelGrid(VoxelBounds(0, sizeX-1, 0, sizeZ-1, 0, sizeY-1))
    // Note: MagicaVoxel uses Z-up, Bannou uses Y-up → swap Y/Z
  ELSE IF chunkId == "XYZI"
    COMPUTE numVoxels ← read int32
    ITERATE numVoxels times
      COMPUTE x ← read byte
      COMPUTE y ← read byte
      COMPUTE z ← read byte
      COMPUTE colorIndex ← read byte
      // MagicaVoxel palette is 1-indexed; our palette is also 1-indexed (0 = empty)
      // Swap Y/Z for coordinate system conversion
      grid.SetVoxel(CREATE VoxelCoord(x, z, y), CREATE Voxel(colorIndex, VoxelFlags.None))
  ELSE IF chunkId == "RGBA"
    ITERATE 256 times (index 0-255)
      COMPUTE r ← read byte, g ← read byte, b ← read byte, a ← read byte
      IF i > 0  // Index 0 is unused in MagicaVoxel
        palette.Set(i, CREATE PaletteEntry(Color(r,g,b,a), MaterialType.Diffuse, 0.5f))
  ELSE IF chunkId == "MATL"
    COMPUTE matId ← read int32
    COMPUTE properties ← read dictionary
    // Map MagicaVoxel material types to our MaterialType enum
    COMPUTE matType ← properties["_type"] match
      "_diffuse" → MaterialType.Diffuse
      "_metal" → MaterialType.Metal
      "_glass" → MaterialType.Glass
      "_emit" → MaterialType.Emit
      "_blend" → MaterialType.Cloud
    COMPUTE roughness ← properties.GetOrDefault("_rough", 0.5f)
    palette.Set(matId, palette.Get(matId) with { Material = matType, Roughness = roughness })
  ELSE
    // Skip unknown chunks (nTRN, nGRP, nSHP for scene graph — handled by ImportMulti)
    skip contentSize + childrenSize bytes
SET grid.Palette ← palette
RETURN grid

---

### GltfImporter.Import
`(data: byte[]) → MeshData`

// Uses SharpGLTF (MIT) for glTF/GLB parsing — no from-scratch binary format parsing
COMPUTE model ← SharpGLTF.Schema2.ModelRoot.ReadGLB(new MemoryStream(data))
VALIDATE model.LogicalMeshes.Count > 0               → FormatException("No meshes in glTF")
COMPUTE gltfMesh ← model.LogicalMeshes[0]
COMPUTE primitive ← gltfMesh.Primitives[0]
// Extract vertex attributes
COMPUTE positions ← primitive.GetVertexAccessor("POSITION").AsVector3Array()
COMPUTE normals ← primitive.GetVertexAccessor("NORMAL")?.AsVector3Array()
COMPUTE indices ← primitive.GetIndices().ToArray()
// Extract vertex colors if present (for palette index assignment during voxelization)
COMPUTE colors ← null
IF primitive.GetVertexAccessor("COLOR_0") is not null
  COMPUTE colorVectors ← primitive.GetVertexAccessor("COLOR_0").AsVector4Array()
  CREATE colorBytes ← new byte[colorVectors.Count * 4]
  FOREACH i FROM 0 TO colorVectors.Count - 1
    colorBytes[i*4]   ← (byte)(colorVectors[i].X * 255)
    colorBytes[i*4+1] ← (byte)(colorVectors[i].Y * 255)
    colorBytes[i*4+2] ← (byte)(colorVectors[i].Z * 255)
    colorBytes[i*4+3] ← (byte)(colorVectors[i].W * 255)
  SET colors ← colorBytes
// Pack into MeshData
CREATE vertices ← new float[positions.Count * 3]
CREATE normalArr ← new float[positions.Count * 3]
FOREACH i FROM 0 TO positions.Count - 1
  vertices[i*3] ← positions[i].X
  vertices[i*3+1] ← positions[i].Y
  vertices[i*3+2] ← positions[i].Z
  IF normals is not null
    normalArr[i*3] ← normals[i].X
    normalArr[i*3+1] ← normals[i].Y
    normalArr[i*3+2] ← normals[i].Z
RETURN CREATE MeshData(vertices, normalArr, UVs: null, indices, colors,
  AmbientOcclusion: null, positions.Count, indices.Length / 3)

---

### MeshExporter.ExportGlb
`(grid: VoxelGrid, mesher: IMesher?) → byte[]`

COMPUTE activeMesher ← mesher ?? new GreedyMesher()
// Mesh all non-empty chunks
CREATE allMeshData ← new List<(MeshData, ChunkCoord)>()
FOREACH (coord, chunk) in grid.EnumerateChunks()
  // Get 6 neighbor chunks for boundary face culling
  COMPUTE neighbors ← grid.GetNeighborChunks(coord)
  COMPUTE meshData ← activeMesher.Mesh(chunk, neighbors, grid.Palette, grid.Metadata.VoxelScale)
  IF meshData.VertexCount > 0
    ACCUMULATE (meshData, coord) to allMeshData
// Merge all chunk meshes into one, offsetting vertices by chunk world position
CREATE merged ← MergeMeshData(allMeshData)
// Write glTF binary (.glb)
CREATE glb ← new GlbWriter()
glb.AddMesh(merged.Vertices, merged.Normals, merged.UVs, merged.Indices, merged.Colors)
// Generate palette texture atlas (16x16 PNG)
COMPUTE atlasBytes ← GeneratePaletteAtlasPng(grid.Palette)
glb.AddTexture(atlasBytes)
glb.SetMaterial(albedoTexture: atlas, vertexColors: true)
RETURN glb.ToBytes()

---

## Algorithms

### Flood Fill (BFS)

**Purpose**: Fill connected region of same palette index with new index.
**Complexity**: O(v) time where v = voxels in connected region, O(v) space for visited set.
**Reference**: Standard BFS flood fill.

INPUT: origin VoxelCoord, targetIndex (current palette at origin), fillIndex, bounds limit
OUTPUT: Set of modified voxel coordinates

CREATE queue, CREATE visited set
ENQUEUE origin, mark visited
WHILE queue not empty:
  coord ← DEQUEUE
  IF grid[coord].PaletteIndex != targetIndex: CONTINUE
  SET grid[coord] ← fillIndex
  FOR EACH 6-connected neighbor:
    IF within bounds AND not visited:
      mark visited, ENQUEUE

**Termination guarantee**: Bounded by VoxelBounds limit. Worst case visits every voxel in bounds.

---

## Serialization Formats

No custom formats defined by this SDK. voxel-builder uses:
- voxel-core's .bvox format for native serialization
- BlockBench .bbmodel (JSON) for artist tool interop
- MagicaVoxel .vox (RIFF binary) for artist tool interop
- glTF/GLB for mesh export

Import/export code lives in this SDK; the format definitions are documented in the deep dive.
