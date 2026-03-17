# Voxel Builder SDK Deep Dive

> **SDK**: voxel-builder (not yet created)
> **Location**: `sdks/voxel-builder/` (planned)
> **Layer**: Composer
> **Domain**: Voxel
> **Dependencies**: voxel-core (grid, meshing, serialization), SharpGLTF.Core (MIT, glTF import)
> **Status**: Aspirational — no code exists.
> **Implementation Map**: [docs/sdks/maps/VOXEL-BUILDER.md](maps/VOXEL-BUILDER.md)
> **Consumers**: voxel-builder-stride, voxel-builder-godot, voxel-builder-unity (bridges), lib-procedural (content pipeline), Gardener/housing (shared building), lib-dungeon (dungeon editing)
> **Short**: Interactive voxel authoring with serializable operations, multi-source undo, assisted/shared building, artist tool import/export (BlockBench, MagicaVoxel, glTF), and engine bridge abstraction

---

## Overview

voxel-builder is the composer-layer SDK for the voxel domain. It provides the interactive editing layer on top of voxel-core: undoable operations (place, erase, fill, brush, box, mirror, rotate, copy/paste, replace), format import/export (BlockBench .bbmodel, MagicaVoxel .vox, glTF .glb, mesh), and the engine bridge abstraction (`IVoxelBuilderBridge`) that engine-specific SDKs implement.

It follows the same pattern as scene-composer: engine-agnostic core with a command-based undo/redo stack, validation, and pluggable engine bridges. The key difference is that voxel editing operates on a single grid (not a hierarchical document), so spatial composition of voxel objects happens at the Scene level — SceneComposer places voxel nodes in scene documents, VoxelBuilder edits the voxel data within each node.

**Operations are serializable and source-tagged.** Every operation can be converted to bytes for network transmission and carries a source identifier (local user, generator, remote editor). This enables two composition patterns from the same mechanism: **assisted building** (generator output flows into the builder as operations that compose with existing content) and **shared building** (multiple editors apply operations to the same grid with per-source undo). The grid is shared state; undo stacks are per-source.

---

## Consumers

| Consumer | Type | Usage |
|----------|------|-------|
| voxel-builder-stride | Bridge | Implements IVoxelBuilderBridge for Stride rendering and input (primary engine) |
| voxel-builder-godot | Bridge | Implements IVoxelBuilderBridge for Godot 4.x rendering and input |
| voxel-builder-unity | Bridge | Implements IVoxelBuilderBridge for Unity rendering and input |
| lib-procedural | Plugin | Content pipeline: import artist models (.bbmodel, .vox), export to .bvox or .glb. Terrain overlay workflow (voxelize → edit → bake). |
| Assisted building | Composition | Generator produces CompoundOperation relative to current grid state. Player refines. Generator output is undoable as a unit. |
| Shared building | Composition | Multiple editors on the same grid via serialized operations broadcast through lib-messaging. Per-source undo. Last-write-wins for same-voxel conflicts. |
| Terrain overlay | Composition | Work zone editing: voxelized terrain → player/NPC edits → bake back to mesh. Frozen borders maintain seam with surrounding terrain. |
| Player housing | Composition | Gardener garden type + VoxelBuilder = interactive building and sculpting within housing. |
| Player/NPC sculpting | Composition | Decorative object creation (statues, carvings, pictures). Small grids (16^3 to 64^3), serialized to .bvox for item metadata or Asset service storage. |
| Dungeon masters | Composition | Pattern A full-garden dungeon editing via VoxelBuilder |
| Content tools | Tool | Standalone voxel editor application using SDK + engine bridge |

---

## Public API Surface

### Core

| Type | Kind | Purpose |
|------|------|---------|
| `VoxelBuilder` | Class | Main orchestrator. Holds grid, per-source operation stacks, bridge reference. Entry point for all editing. |
| `IVoxelBuilderBridge` | Interface | Engine rendering contract. Implemented per-engine (Stride, Godot, Unity). |
| `IVoxelStorageClient` | Interface | Optional persistence integration (Save-Load, Asset service). |
| `VoxelBuilderOptions` | Record | Configuration: max undo depth, auto-mesh on edit, palette behavior, frozen enforcement. |

### Operations

| Type | Kind | Purpose |
|------|------|---------|
| `IVoxelOperation` | Interface | Undoable, serializable operation contract: Execute, Undo, Serialize, affected region, source ID. |
| `OperationStack` | Class | Per-source undo/redo stack with configurable depth. |
| `OperationSerializer` | Static class | Serialize/deserialize any IVoxelOperation to/from bytes. Type-discriminated binary format. |
| `PlaceOperation` | Class | Place single voxel at coordinate with palette index. |
| `EraseOperation` | Class | Remove single voxel (set to Voxel.Empty). |
| `FillOperation` | Class | Flood fill within bounds — connected same-palette or empty regions. |
| `BrushOperation` | Class | Paint voxels within a sphere/cube/cylinder brush shape. |
| `BoxOperation` | Class | Fill or erase an axis-aligned box region. |
| `MirrorOperation` | Class | Mirror grid content across an axis (X, Y, or Z). |
| `RotateOperation` | Class | 90-degree rotation around an axis. |
| `CopyPasteOperation` | Class | Copy region to clipboard, paste at offset with optional rotation. |
| `ReplaceOperation` | Class | Replace all voxels of palette index A with palette index B. |
| `CompoundOperation` | Class | Groups multiple operations into one atomic undo unit. Used for brush strokes and multi-step edits. |
| `GridPatchOperation` | Class | Applies a VoxelDelta as a single atomic operation. Used for generator output in assisted building — far more compact than wrapping N individual PlaceOperations. |

### Import/Export

| Type | Kind | Purpose |
|------|------|---------|
| `IVoxelImporter` | Interface | Format import contract: bytes/stream → VoxelGrid. |
| `IVoxelExporter` | Interface | Format export contract: VoxelGrid → bytes/stream. |
| `BlockBenchImporter` | Class | .bbmodel (JSON) → VoxelGrid. Parses elements, UV/texture → palette. |
| `BlockBenchExporter` | Class | VoxelGrid → .bbmodel. Greedy-meshed regions → elements. |
| `MagicaVoxelImporter` | Class | .vox (RIFF binary) → VoxelGrid. Parses XYZI, RGBA, MATL chunks. |
| `MagicaVoxelExporter` | Class | VoxelGrid → .vox. Grid → XYZI + RGBA + MATL chunks. |
| `GltfImporter` | Class | .glb/.gltf → MeshData via SharpGLTF (MIT). For terrain overlay voxelization input. |
| `MeshExporter` | Class | VoxelGrid → .glb/.gltf via mesher. For asset pipeline export and terrain bake. |
| `RawVoxelImporter` | Class | Raw byte array → VoxelGrid. For procedural output consumption. |
| `RawVoxelExporter` | Class | VoxelGrid → raw bytes. For Save-Load and Asset service integration. |

---

## Data Model

### VoxelBuilder (Orchestrator)

```
VoxelBuilder
├── Grid: VoxelGrid                   // The grid being edited (from voxel-core)
├── localStack: OperationStack        // Local user's undo/redo
├── externalStacks: Dictionary<string, OperationStack>  // Per-source undo histories
├── Bridge: IVoxelBuilderBridge?      // Engine rendering (null for headless/server)
├── Storage: IVoxelStorageClient?     // Persistence (null for local-only)
├── Options: VoxelBuilderOptions
├── ActivePaletteIndex: byte          // Currently selected palette entry for painting
├── ActiveBrush: BrushShape           // Current brush shape and size
├── Selection: VoxelRegion?           // Current region selection (for copy/paste)
├── Clipboard: VoxelClipboard?        // Copied region data
└── OnOperationApplied: event<OperationAppliedEventArgs>  // Fires for ALL operations (local + external)
```

**OnOperationApplied** is the hook that enables networking. The plugin layer (Gardener + lib-messaging) subscribes:
- When a local operation fires → plugin serializes it and broadcasts via IMessageBus
- When a remote operation arrives via subscription → plugin calls `ApplyExternalOperation`
- lib-save-load captures the delta for persistence

The event args include the serialized operation bytes, the source ID, and the affected chunk coordinates — everything the subscriber needs to broadcast without re-serializing.

### OperationStack

```
OperationStack
├── undoDeque: BoundedDeque<IVoxelOperation>  // LIFO with bottom-eviction when MaxDepth exceeded
├── redoStack: Stack<IVoxelOperation>         // Standard LIFO (redo never exceeds undo depth)
├── SourceId: string                          // Who owns this stack ("local", "generator", "player-2")
├── MaxDepth: int                             // Maximum undo history
└── IsModified: bool                          // Any operations since last save
```

**BoundedDeque**: Push to top (newest), pop from top (undo), evict from bottom (oldest) when count exceeds MaxDepth. Implemented as a circular buffer or `LinkedList<T>`. Standard `Stack<T>` cannot evict from the bottom — this is a common voxel editor implementation detail.

Each source gets its own stack. Local undo reverts local operations. Generator undo reverts the last generated CompoundOperation. Remote editor operations are tracked but not locally undoable (the remote editor owns their undo). The `externalStacks` dictionary is keyed by source ID — sources are created on first operation from that source.

### IVoxelOperation

```
IVoxelOperation
├── Execute(grid: VoxelGrid): void           // Apply the operation, capturing before-state
├── Undo(grid: VoxelGrid): void              // Restore before-state
├── Serialize(): byte[]                      // Convert to bytes for network/storage
├── AffectedRegion: VoxelBounds              // Bounding box of changed voxels
├── SourceId: string                         // Who created this operation
├── Description: string                      // Human-readable operation name
└── OperationType: VoxelOperationType        // Type discriminator for deserialization
```

**Serialization format**: Each operation type has a known binary layout. `OperationSerializer.Deserialize(byte[])` reads the `VoxelOperationType` discriminator byte, then dispatches to the correct type-specific deserializer. The serialized form includes:
- 1 byte: operation type discriminator
- N bytes: type-specific payload (coordinates, palette indices, region bounds, etc.)
- The before-state snapshot is NOT serialized — it's recomputed on the receiver by reading the grid state before applying

This means: the receiver applies the operation to its copy of the grid, and the operation captures the receiver's local before-state for undo. The sender's before-state and receiver's before-state are identical as long as the grids are synchronized — which they are if all prior operations were applied in the same order.

Each operation captures the before-state of affected voxels on Execute for Undo. For example, `PlaceOperation` stores the previous voxel at the target coordinate. `BrushOperation` stores a snapshot of all voxels within the brush volume.

**Frozen voxel enforcement**: Before any operation modifies a voxel, it checks the target coordinate's `VoxelFlags.Frozen` bit. If the voxel is frozen and `VoxelBuilderOptions.EnforceFrozen` is true (the default), the operation silently skips that coordinate. This means a brush stroke across a frozen boundary only modifies the non-frozen voxels — the frozen border remains intact, maintaining seamless alignment with surrounding terrain. Generators and voxelizers bypass this check because they construct grids via `VoxelGrid.SetVoxel` directly, not through the operation system.

### GridPatchOperation

The bridge between voxel-generator output (VoxelGrid) and the operation system. Wraps a VoxelDelta as a single atomic undoable operation.

```
GridPatchOperation : IVoxelOperation
├── delta: byte[]                     // VoxelDelta (chunk-level binary diff)
├── beforeChunks: Dictionary<ChunkCoord, byte[]>  // Serialized pre-patch chunk states (for undo)
├── AffectedRegion: VoxelBounds       // Union of all affected chunk bounds
├── SourceId: string                  // Typically "generator"
├── OperationType: GridPatch (10)     // Discriminator for serialization
├── Execute(grid): apply VoxelDelta.Apply(grid, delta)
└── Undo(grid): restore beforeChunks for each affected ChunkCoord
```

**Why not CompoundOperation with N PlaceOperations**: A 50K-voxel dungeon wing as individual PlaceOperations: ~700KB serialized. As a GridPatchOperation wrapping a VoxelDelta: ~5-50KB serialized (chunk-level XOR diff with RLE + LZ4). The delta format is already defined in voxel-core — GridPatchOperation simply wraps it as an undoable operation.

**Produced by**: `VoxelBuilder.DiffToOperation(before, after, sourceId)` — computes VoxelDelta between two grids, snapshots affected chunks for undo, returns GridPatchOperation.

### VoxelClipboard

```
VoxelClipboard
├── Voxels: Dictionary<VoxelCoord, Voxel>   // Relative-coordinate voxel data
├── Bounds: VoxelBounds                      // Bounding box of copied region
└── PaletteSnapshot: PaletteEntry[]          // Palette entries used by copied voxels
```

### BrushShape

```
BrushShape
├── Type: BrushType     // Sphere, Cube, Cylinder
└── Radius: int         // Brush radius in voxels
```

---

## Collaborative Editing Model

### Operations as the Universal Unit of Change

Every modification to a VoxelGrid — whether from local user input, a procedural generator, or a remote editor — flows through the same `IVoxelOperation` interface. Operations are:

1. **Executable**: Apply to a grid, capturing before-state for undo
2. **Undoable**: Restore the before-state
3. **Serializable**: Convert to bytes for network transmission or disk storage
4. **Source-tagged**: Know who created them, enabling per-source undo

This design means the SDK doesn't distinguish between "local edit" and "remote edit" at the grid level — both are just operations. The distinction matters only for undo scoping.

### Assisted Building Pattern

A procedural generator acts as a collaborator, producing operations relative to the current grid state:

```
Player places foundation voxels (local operations → localStack)
  → Player requests "generate walls" via lib-procedural
  → lib-procedural reads current grid state from lib-save-load (authoritative copy)
  → Generator (TemplateStamper, WFC, etc.) runs against current state → output VoxelGrid
  → VoxelBuilder.DiffToOperation(contextGrid, outputGrid, "generator") → GridPatchOperation
  → GridPatchOperation serialized, returned to client
  → Client calls builder.ApplyExternalOperation(patchOp, "generator")
  → Walls appear around the foundation
  → Player adjusts walls manually (local operations → localStack)
  → Player can undo the entire generated wing as one operation: builder.UndoExternal("generator")
  → Player can also undo their own manual edits independently: builder.Undo() (local stack only)
```

The generator produces a VoxelGrid, not operations. `DiffToOperation` computes the VoxelDelta between the context grid and the generated output, wrapping it as a `GridPatchOperation` — a single atomic operation containing a compact chunk-level binary diff (~5-50KB for a dungeon wing vs. ~700KB as 50K individual PlaceOperations). The delta captures only what changed. The patch can be undone as one unit without affecting the player's manual edits.

### Shared Building Pattern

Multiple editors operate on the same grid with real-time synchronization:

```
Player A and Player B connected to the same housing garden session
  → Both have VoxelBuilder instances pointing at the same authoritative grid (via lib-save-load)
  → Player A places a wall:
     → PlaceOperation executes locally → localStack
     → OnOperationApplied fires → plugin serializes, broadcasts via IMessageBus
     → Server applies to authoritative grid, assigns sequence number, broadcasts
     → Player B receives → plugin calls builder.ApplyExternalOperation(op, "player-A")
     → Player B sees wall appear (bridge notified via OnChunksModified)
  → Player B adds a window:
     → Same flow in reverse
  → Undo is per-source:
     → Player A's undo reverts Player A's last operation
     → Player B's operations are unaffected
```

**Conflict resolution**: Last-write-wins at the VoxelGrid level. `SetVoxel` overwrites unconditionally. The authoritative server (lib-save-load) determines operation ordering via sequence numbers. If Player A and Player B both edit voxel (5,3,2) simultaneously, the server's ordering determines which write persists. This is acceptable for a building game — the visual result is immediately visible to both editors, and the "loser" can simply re-apply their edit.

**Why this is simpler than general OT/CRDT**: Voxel operations commute for most cases. Placing at (5,3,2) and placing at (8,1,4) produce the same result regardless of order. The only conflict is same-voxel contention, and last-write-wins handles it cleanly. No Operational Transformation or CRDT required.

### What the SDK Owns vs. What the Plugin Layer Owns

| Concern | Owner | Mechanism |
|---------|-------|-----------|
| Operation execution + undo | VoxelBuilder (SDK) | Per-source OperationStacks |
| Operation serialization | OperationSerializer (SDK) | Binary type-discriminated format |
| External operation application | VoxelBuilder.ApplyExternalOperation (SDK) | Executes on grid, pushes to external stack |
| Operation broadcast event | VoxelBuilder.OnOperationApplied (SDK) | Event with serialized bytes + source + affected chunks |
| Network transport | Gardener + lib-messaging (Plugin) | IMessageBus publish/subscribe per garden session |
| Authoritative ordering | lib-save-load (Plugin) | Server applies sequentially, assigns sequence numbers |
| Session management | lib-game-session (Plugin) | Who's connected, permissions, join/leave |
| Grid conflict resolution | VoxelGrid.SetVoxel (SDK, inherent) | Last-write-wins (SetVoxel overwrites) |
| Persistence | lib-save-load (Plugin) | Delta saves from dirty chunks after each operation batch |
| Generator invocation | lib-procedural (Plugin) | Receives context, returns CompoundOperation |

### Relationship to lib-scene's Checkout/Commit

Scene-composer uses checkout/commit for exclusive editing — one editor at a time, with version history. VoxelBuilder supports both concurrency models:

- **Exclusive editing** (content creation by artists): `IVoxelStorageClient` backed by lib-scene's checkout/commit. Single editor, version history, conflict-free.
- **Shared editing** (player housing, collaborative building): `IVoxelStorageClient` backed by lib-save-load's delta persistence. Multiple editors, real-time merge, last-write-wins.

The storage backend determines the concurrency model, not the builder. The builder's operation system works identically in both modes — the only difference is whether `OnOperationApplied` triggers a broadcast (shared) or just a local save (exclusive).

---

## Computation Pipeline

### Local Edit Flow

```
User action (place/brush/fill/etc.)
  → Create IVoxelOperation with parameters and sourceId="local"
  → operation.Execute(grid)
     → For each target coordinate:
        → Check VoxelFlags.Frozen — skip if frozen and EnforceFrozen is true
        → Snapshot affected voxel (for undo)
        → Modify VoxelGrid via SetVoxel
        → Mark affected chunks as dirty
  → Push to localStack (undo stack), clear local redo stack
  → Fire OnOperationApplied(operation, serializedBytes, affectedChunks)
  → Notify bridge: OnChunksModified(dirtyChunkCoords)
```

### External Operation Flow

```
External operation received (from generator, remote editor, or network)
  → builder.ApplyExternalOperation(operation, sourceId)
  → operation.Execute(grid)
     → Same frozen check, snapshot, modify, dirty logic
  → Push to externalStacks[sourceId] (creates stack on first use)
  → Fire OnOperationApplied(operation, serializedBytes, affectedChunks)
  → Notify bridge: OnChunksModified(dirtyChunkCoords)
```

### Undo/Redo Flow

```
Local undo:
  → Pop from localStack.undoStack
  → operation.Undo(grid) → restore snapshots, mark dirty
  → Push to localStack.redoStack
  → Fire OnOperationApplied (undo event)
  → Notify bridge

External undo (e.g., undo generator output):
  → builder.UndoExternal(sourceId)
  → Pop from externalStacks[sourceId].undoStack
  → operation.Undo(grid) → restore snapshots, mark dirty
  → Push to redo stack
  → Fire OnOperationApplied (undo event)
  → Notify bridge
```

### Import Flow

```
File bytes → IVoxelImporter.Import(bytes)
  → Parse format (JSON for .bbmodel, RIFF for .vox, glTF binary for .glb)
  → Map format elements to VoxelGrid:
     .bbmodel: cuboid elements → filled voxel regions, textures → palette
     .vox: XYZI positions → voxels, RGBA → palette, MATL → materials
     .glb: vertices + indices → MeshData (for MeshVoxelizer consumption)
  → Return VoxelGrid (or MeshData for .glb)

VoxelBuilder.LoadGrid(grid)
  → Replace current grid
  → Clear all operation stacks (local + external)
  → Notify bridge: full re-mesh
```

### Export Flow

```
VoxelBuilder.Grid → IVoxelExporter.Export(grid)
  → .bbmodel: greedy-mesh regions → cuboid elements, palette → texture atlas
  → .vox: voxels → XYZI chunk, palette → RGBA + MATL chunks
  → .glb: mesh via configurable mesher → glTF binary
  → Return bytes
```

---

## Determinism Contract

**Operations**: Deterministic. Same sequence of operations on the same initial grid produces the same result, regardless of source.

**Serialization round-trip**: Deterministic. `Serialize → Deserialize → Execute` produces the same grid modification as direct `Execute`, given the same grid state.

**Import**: Deterministic. Same file bytes → same VoxelGrid.

**Export**: Deterministic. Same VoxelGrid → same output bytes.

---

## Performance Targets

| Operation | Target | Context | Notes |
|-----------|--------|---------|-------|
| PlaceOperation (single voxel) | < 1 μs | Client | Direct array write + snapshot |
| BrushOperation (r=5 sphere) | < 100 μs | Client | ~500 voxels snapshot + modify |
| BoxOperation (16^3) | < 50 μs | Client | Region fill, one chunk |
| FillOperation (flood fill) | < 5 ms | Client | BFS bounded by grid extent |
| Undo/Redo | Same as original op | Client | Snapshot restore |
| OperationSerializer.Serialize | < 10 μs | Both | Binary write, no snapshot in payload |
| OperationSerializer.Deserialize | < 10 μs | Both | Binary read + type dispatch |
| ApplyExternalOperation | Same as local op | Client | Execute + push to external stack |
| .bbmodel import (complex) | < 100 ms | Both | JSON parse + element → voxel |
| .vox import (256^3) | < 50 ms | Both | Binary parse, direct palette map |
| .glb import (10K triangles) | < 50 ms | Both | glTF parse → MeshData |
| .glb export (full grid) | < 200 ms | Server | Greedy mesh + glTF serialization |

---

## Format Support

### BlockBench (.bbmodel) — MIT Licensed Tool

**Direction**: Both

BlockBench is the dominant free voxel/block modeling tool. Its format is JSON:

```json
{
    "meta": { "format_version": "4.10", "model_format": "free" },
    "resolution": { "width": 16, "height": 16 },
    "elements": [
        { "name": "cube", "from": [0,0,0], "to": [16,16,16], "faces": {...} }
    ],
    "outliner": [...],
    "textures": [...]
}
```

**Import mapping**: Elements (cuboids) → filled voxel regions. Per-face UV + texture → palette entry (most common face color selected when per-face colors differ). Outliner hierarchy → GridMetadata tags.

**Export mapping**: Greedy-meshed regions → elements. Palette → texture atlas. Grid metadata → outliner groups. Animations not supported (static models only).

**Parser**: Written from scratch (clean-room). We parse the JSON format directly — no dependency on BlockBench source code.

### MagicaVoxel (.vox) — Free Tool, Community-Documented RIFF

**Direction**: Both

```
"VOX " (magic) | 150 (version)
MAIN chunk
├── SIZE (grid dimensions)
├── XYZI (voxel coords + palette indices)
├── RGBA (256-color palette)
├── MATL (per-palette material properties)
├── nTRN (transform nodes)
├── nGRP (group nodes)
└── nSHP (shape nodes)
```

**Import mapping**: XYZI positions → sparse grid (Y/Z swapped for coordinate system conversion). RGBA → Palette. MATL → PaletteEntry material types. Scene graph (nTRN/nGRP/nSHP) → multiple grids with transforms via `ImportMulti`.

**Export mapping**: Sparse grid → XYZI (Y/Z swapped back). Palette → RGBA + MATL. Single grid per model.

**Parser**: Written from scratch. The RIFF structure is well-documented by the community.

### glTF/GLB (.glb, .gltf) — Khronos Standard

**Direction**: Import only (export via MeshExporter)

**Purpose**: Terrain overlay voxelization input. When lib-procedural needs to voxelize a terrain mesh from lib-scene, the asset is typically stored as glTF. The `GltfImporter` parses it into `MeshData` for consumption by voxel-core's `MeshVoxelizer`.

**Import mapping**: glTF meshes → MeshData (vertices, normals, indices, vertex colors where present). Only the first mesh primitive is imported (voxelization targets a single surface, not a complex scene). Textures are sampled at vertex positions for palette color assignment.

---

## Engine Bridge Pattern

### IVoxelBuilderBridge

The bridge contract that engine-specific SDKs implement:

```
IVoxelBuilderBridge
├── OnGridLoaded(grid: VoxelGrid)                    // Full grid replacement
├── OnChunksModified(coords: IReadOnlySet<ChunkCoord>) // Incremental mesh update
├── OnPaletteChanged(palette: Palette)               // Palette texture atlas rebuild
├── ScreenToVoxel(screenPos: (float, float)) → VoxelCoord? // Mouse → voxel picking
├── SetMesher(mesher: IMesher)                       // Swap meshing strategy
└── Dispose()                                        // Cleanup engine resources
```

**Why bridge, not direct rendering**: The SDK is engine-agnostic. VoxelBuilder doesn't know about Stride's `MeshDraw`, Godot's `ArrayMesh`, or Unity's `Mesh`. The bridge translates between SDK types (MeshData, VoxelCoord) and engine types. Same pattern as scene-composer's `ISceneComposerBridge`.

**Headless mode**: When `Bridge` is null, VoxelBuilder operates headlessly — all operations work, no rendering. Used for server-side generation, content pipeline, and authoritative game server processing.

**Bridge notification for all operation sources**: The bridge is notified via `OnChunksModified` for both local and external operations. When a remote editor's PlaceOperation is applied via `ApplyExternalOperation`, the bridge re-meshes the affected chunk and updates the rendering — the remote edit becomes visible immediately.

---

## Known Quirks & Caveats

#### Intentional Quirks (Documented Behavior)

- **Before-state snapshots are NOT serialized.** When an operation is serialized for network transmission, the snapshot (previous voxel values for undo) is excluded. The receiver recomputes the snapshot by reading its local grid state before applying the operation. This works because grids are synchronized — all prior operations were applied in the same order. It dramatically reduces serialized operation size (a BrushOperation touching 500 voxels would serialize 500 coordinate+voxel pairs for the snapshot; without snapshots, it serializes only the brush center, radius, and palette index).

- **External operations are not locally undoable by default.** Calling `builder.Undo()` only reverts local operations. To undo external operations, the caller must explicitly specify the source: `builder.UndoExternal("generator")`. This prevents a player from accidentally undoing another player's work in shared editing. The UX layer decides whether to expose external undo controls.

- **Last-write-wins is the only conflict resolution.** Two editors placing different voxels at the same coordinate: the last operation applied wins. No merge, no conflict notification. This is acceptable for voxel building because: (a) the visual result is immediately visible to both editors, (b) the "loser" can re-apply their edit, (c) same-voxel contention is rare in practice (editors naturally work in different areas).

#### Design Considerations (Requires Planning)

- **Copy/paste across grids**: CopyPasteOperation operates within a single grid. Cross-grid paste needs palette merging (source palette index 5 might differ from destination's). Palette.GetOrAddIndex with full (Color, MaterialType, Roughness) lookup handles the merging — remap each copied voxel's palette index through the destination palette.

- **Large brush performance**: BrushOperation at r=50 (500K+ voxels) may exceed the 100μs target. Consider spatial optimization (only iterate voxels within brush bounds, skip empty chunks). The snapshot cost also matters — snapshotting 500K voxels is expensive. May need chunk-level snapshots for large operations.

- **Flood fill containment**: FillOperation BFS could visit every voxel in a large empty grid. Must be bounded by the `limit` parameter (VoxelBounds). Without a limit, defaults to grid bounds — which could be 512^3 in the worst case. Consider a max-voxel-count safety limit as well.

- **Operation ordering in shared editing**: The authoritative server assigns sequence numbers to operations. If operations arrive out of order at a client (network reordering), they should be buffered and applied in sequence order. This is a plugin-layer concern (lib-save-load manages the sequence), but the SDK should tolerate operations being applied in any order — which it does, because SetVoxel is unconditional and the before-state snapshot is captured locally.

- **Generator context freshness**: When a generator produces a CompoundOperation against a grid snapshot, the player may have edited the grid between request and response. The generator's operations may partially overlap with the player's edits. This is fine — last-write-wins means the generator's output overwrites any conflicting player edits, and the player can undo the generated output and re-request. For a more sophisticated approach, the generator could diff its output against the current grid and skip unchanged voxels.

---

## Open Questions

1. **Brush stroke grouping**: Dragging a brush should produce one CompoundOperation per stroke (undo reverts the whole stroke), not individual operations per frame. This requires the bridge to signal stroke start/end via `BeginCompound("brush stroke")` / `EndCompound()`. The bridge knows when mouse-down and mouse-up occur — the SDK provides the grouping mechanism.

2. **Import palette conflict**: When importing a .bbmodel/.vox into a grid that already has palette entries, the importer merges palettes via `Palette.GetOrAddIndex` (find matching full entries, add new ones, remap indices). This is consistent with the TemplateStamper palette merge strategy in voxel-generator.

3. **Shared editing latency tolerance**: How much latency between operation broadcast and remote application is acceptable? For building (not combat), 100-200ms is fine. The plugin layer (lib-messaging) determines actual latency. The SDK has no latency awareness — it applies operations as fast as they arrive.

---

## Work Tracking

No work items yet — SDK is pre-implementation.
