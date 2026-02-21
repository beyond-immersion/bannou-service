# Scene System Guide

> **Status**: PRODUCTION
> **Version**: 2.0.0
> **Scope**: Scene service, SceneComposer SDK, Asset integration, content authoring pipeline

---

## Overview

The Scene System is the content authoring pipeline for Bannou game worlds. It spans three layers:

1. **Storage** (lib-scene) — Hierarchical scene document persistence with versioning, locking, and validation
2. **Distribution** (lib-asset) — Binary asset storage and bundled delivery via pre-signed URLs
3. **Authoring** (SceneComposer SDK) — Engine-agnostic editing with engine-specific rendering bridges

**Core Principle**: Game engines are pure renderers. All composition happens through Bannou services and the SceneComposer SDK. Scenes exist identically at edit-time and runtime — no "baking" step, no editor-only concepts, no format conversion.

**Not Responsible For**:
- Computing world transforms (consumers apply transforms themselves)
- Determining affordances at runtime (geometry concern)
- Pushing data to other services (event-driven decoupling)
- Interpreting node behavior (consumers decide what nodes mean)
- Engine-specific rendering or physics (engine bridges handle this)

---

## Scene Composition Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                    SCENE COMPOSITION PIPELINE                           │
│                                                                         │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │ AUTHORING LAYER (Client SDK)                                      │  │
│  │                                                                    │  │
│  │  SceneComposer SDK (Engine-Agnostic Core)                         │  │
│  │  ├─ Scene hierarchy management                                    │  │
│  │  ├─ Transform operations (local/world)                            │  │
│  │  ├─ Selection state machine                                       │  │
│  │  ├─ Undo/redo command stack                                       │  │
│  │  ├─ Validation & constraint checking                              │  │
│  │  └─ Persistence coordination (checkout/heartbeat/commit)          │  │
│  │           │                                                        │  │
│  │           ▼                                                        │  │
│  │  ISceneComposerBridge (Engine Extension Interface)                 │  │
│  │  ├─ CreateEntity / DestroyEntity                                  │  │
│  │  ├─ UpdateTransform                                               │  │
│  │  ├─ SetAsset / ClearAsset                                         │  │
│  │  ├─ RenderGizmo / PickEntity                                      │  │
│  │  └─ FocusCamera / GetMouseRay                                     │  │
│  └───────────────────────────────────────────────────────────────────┘  │
│           │                              │                              │
│           ▼                              ▼                              │
│  ┌─────────────────┐          ┌─────────────────────┐                  │
│  │  STORAGE LAYER  │          │ DISTRIBUTION LAYER  │                  │
│  │  (lib-scene)    │          │ (lib-asset)         │                  │
│  │                 │          │                     │                  │
│  │  Scene YAML     │          │  .bannou bundles    │                  │
│  │  documents      │          │  Pre-signed URLs    │                  │
│  │  Version history│          │  LZ4 compression    │                  │
│  │  Checkout locks │          │  Processing pool    │                  │
│  └─────────────────┘          └─────────────────────┘                  │
│           │                              │                              │
│           ▼                              ▼                              │
│  ┌──────────────────────────────────────────────────────────────────┐  │
│  │ CONSUMERS                                                        │  │
│  │  ├─ Mapping Service: spatial indexing from scene.instantiated    │  │
│  │  ├─ Actor Service: NPC spawn from marker nodes                   │  │
│  │  ├─ Game Engine: geometry instantiation + rendering              │  │
│  │  └─ Save-Load: voxel persistence for modified scene data        │  │
│  └──────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────┘
```

### Why Three Layers?

| Layer | What It Owns | What It Does NOT Own |
|-------|-------------|---------------------|
| **lib-scene** (Storage) | Scene document structure, hierarchy, locking, validation | Binary asset data, rendering, spatial indexing |
| **lib-asset** (Distribution) | Binary files (textures, models, audio), bundles, processing | Scene structure, composition logic |
| **SceneComposer SDK** (Authoring) | Editing UX, transform math, undo/redo, selection | Rendering, persistence format, asset storage |

A scene document references assets by ID. The SceneComposer SDK fetches scene documents from lib-scene and loads referenced assets from lib-asset through engine-specific type loaders. The SDK mediates between the two services without either service knowing about the other.

---

## SceneComposer SDK

The SceneComposer SDK is an engine-agnostic NuGet package (`BeyondImmersion.Bannou.SceneComposer`) that provides all composition logic. Engine-specific packages (e.g., `BeyondImmersion.Bannou.SceneComposer.Stride`) implement the rendering bridge.

### Engine Bridge Pattern

The SDK defines an `ISceneComposerBridge` interface that each engine extension implements:

| Bridge Method | Purpose | Engine Responsibility |
|---------------|---------|----------------------|
| `CreateEntity` | Instantiate a renderable entity for a scene node | Create engine-native object (Entity, Node3D, Actor) |
| `DestroyEntity` | Remove entity when node is deleted | Destroy engine-native object |
| `UpdateTransform` | Apply world transform to entity | Set engine transform component |
| `SetEntityAsset` | Load and display asset on entity | Deserialize asset bytes into engine-native format |
| `RenderGizmo` | Draw transform manipulation handles | Use engine debug drawing API |
| `PickEntity` | Raycast to find entity under cursor | Use engine physics/picking system |
| `FocusCamera` | Orbit camera to focus on target | Control engine camera |

**Key principle**: The SDK owns data and logic; the bridge owns rendering and input. All composition state lives in the SDK — the bridge is a thin rendering adapter.

### Command Pattern (Undo/Redo)

All scene modifications go through a command stack:

| Command | What It Captures |
|---------|-----------------|
| `CreateNodeCommand` | Node data, parent ID, insert index |
| `DeleteNodeCommand` | Full node subtree, parent ID, index |
| `SetTransformCommand` | Node ID, old/new transform |
| `ReparentNodeCommand` | Node ID, old/new parent, old/new index |
| `BindAssetCommand` | Node ID, old/new asset reference |
| `BatchCommand` | List of sub-commands (for grouped operations) |

Transform drags are handled as sessions: the initial transform is captured on mouse-down, intermediate transforms update the entity without creating commands, and a single `SetTransformCommand` is created on mouse-up with the initial → final delta.

### Selection System

The SDK manages multi-selection state:

| Mode | Behavior | Input |
|------|----------|-------|
| `Replace` | Clear all, select one | Click |
| `Add` | Add to selection | Ctrl+Click |
| `Toggle` | Add if not selected, remove if selected | Ctrl+Click on selected |
| `Range` | Select range in hierarchy | Shift+Click |

### Gizmo Abstraction

Gizmo implementation is split between SDK and engine:

- **SDK owns**: State machine (Idle → Hovering → Dragging), axis picking math (ray-line/plane intersection), transform constraints (grid snap, axis lock, rotation snap), transform delta computation, configuration (colors, sizes, sensitivities)
- **Engine bridge owns**: Line/cone/circle drawing, mouse ray generation, input state

This ensures consistent gizmo behavior across engines while using each engine's optimized debug drawing.

### UI Approach: ViewModel, Not Abstract UI

The SDK does NOT provide an abstract UI framework. UI frameworks differ too much between engines (Unity UI/UIToolkit, Stride.UI, Godot Control nodes) to abstract meaningfully.

Instead, the SDK provides a `ViewModel` with observable data that engine-specific UI panels consume:

- Scene hierarchy (root node, children)
- Selected nodes
- Loaded bundles and available assets
- Editor state (mode, dirty flag, checkout state)
- Bindable commands (create, delete, undo, redo, save)

Each engine extension builds native UI panels that subscribe to the ViewModel. This gives engine teams full creative control over UX while the SDK owns the data.

---

## Architectural Position

```
┌─────────────────────────────────────────────────────────────────┐
│                         DATA FLOW                                │
│                                                                  │
│  Scene Service                                                   │
│  (Composition Storage)                                           │
│         │                                                        │
│         │  "I store hierarchical definitions"                    │
│         │  "I don't compute positions or interpret meaning"      │
│         │                                                        │
│         ▼                                                        │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │                 scene.instantiated event                 │    │
│  │  "Someone placed scene X at world position Y"            │    │
│  │  (Contains: sceneId, instanceId, worldTransform, regionId│    │
│  └──────────────────────────┬──────────────────────────────┘    │
│                             │                                    │
│         ┌───────────────────┼───────────────────────┐           │
│         ▼                   ▼                       ▼           │
│  ┌─────────────┐   ┌─────────────────┐   ┌─────────────────┐   │
│  │   Mapping   │   │      Actor      │   │   Game Server   │   │
│  │   Service   │   │     Service     │   │                 │   │
│  │             │   │                 │   │                 │   │
│  │ Fetches     │   │ Finds spawn     │   │ Instantiates    │   │
│  │ scene,      │   │ markers,        │   │ geometry,       │   │
│  │ indexes     │   │ spawns NPCs     │   │ computes        │   │
│  │ spatially   │   │ at positions    │   │ affordances     │   │
│  └─────────────┘   └─────────────────┘   └─────────────────┘   │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### Why Separate from Mapping?

| Concern | Scene Service | Mapping Service |
|---------|---------------|-----------------|
| Data structure | Hierarchical (parent/child) | Flat (spatial index) |
| Coordinates | Local to parent | World space |
| Purpose | Define composition | Enable spatial queries |
| Update frequency | Design-time authoring | Runtime updates |
| Storage model | Versioned documents | Live spatial data |

A tavern scene defines "bar counter is child of bar area, rotated 45 degrees". Mapping stores "object at world coords (1547, 0, 892) is queryable for spatial operations."

---

## Data Model

### Scene Document

```yaml
Scene:
  sceneId: UUID           # Unique identifier
  gameId: string          # Partition key (e.g., "my-game")
  sceneType: SceneType    # Classification (region, building, prefab, etc.)
  name: string            # Human-readable name
  description: string?    # Optional description
  version: string         # Semantic version (MAJOR.MINOR.PATCH)
  root: SceneNode         # Root node of hierarchy
  tags: string[]          # Searchable tags
  metadata: object?       # Consumer-specific opaque data
  createdAt: datetime
  updatedAt: datetime
```

### Scene Types

```yaml
SceneType:
  - unknown     # Unclassified
  - region      # Large world area (continent, zone)
  - city        # Settlement, town, village
  - district    # Subsection of a city
  - lot         # Plot of land (housing, farm)
  - building    # Structure (house, shop, dungeon entrance)
  - room        # Interior space within a building
  - dungeon     # Underground/instanced adventure area
  - arena       # Dedicated combat or challenge space
  - vehicle     # Ship, mech, cart (movable container)
  - prefab      # Reusable composition (furniture set, decoration cluster)
  - cutscene    # Cinematic scene
  - other       # Intentionally uncategorized
```

### Scene Node

```yaml
SceneNode:
  nodeId: UUID            # Globally unique node identifier
  refId: string           # Scene-local reference (e.g., "main_door", "npc_spawn_1")
  parentNodeId: UUID?     # Parent node (null for root)
  name: string            # Human-readable display name
  nodeType: NodeType      # group, mesh, marker, volume, emitter, reference, custom
  localTransform: Transform  # Position/rotation/scale relative to parent
  asset: AssetReference?  # Optional asset binding (mesh, sound, particle)
  children: SceneNode[]   # Child nodes
  enabled: boolean        # Whether node is active (default: true)
  sortOrder: integer      # Sibling ordering for deterministic iteration
  tags: string[]          # Arbitrary tags for consumer filtering
  annotations: object?    # Consumer-specific opaque data
```

### Node Types

| Type | Purpose | Typical Usage |
|------|---------|---------------|
| `group` | Container with no visual | Organizational grouping |
| `mesh` | 3D geometry reference | Buildings, terrain, props |
| `marker` | Named point in space | Spawn locations, door frames |
| `volume` | 3D region definition | Combat zones, loading triggers |
| `emitter` | Point that emits something | Sound sources, particle effects |
| `reference` | Reference to another scene | Nested composition |
| `custom` | Extension point | Game-specific nodes |

### Annotations Pattern

Nodes can carry arbitrary consumer data via `annotations`. Scene Service stores without interpretation:

```yaml
annotations:
  render:
    castShadows: true
    lodBias: 1.0
  physics:
    collisionShape: "convex"
    mass: 0
  game:  # Game-specific namespace
    interactionType: "sit"
    npcBehavior: "guard_patrol"
```

---

## API Endpoints

### Scene CRUD

| Endpoint | Permission | Purpose |
|----------|------------|---------|
| `POST /scene/create` | developer | Create new scene document |
| `POST /scene/get` | user | Retrieve scene by ID (with optional reference resolution) |
| `POST /scene/list` | user | List scenes with filtering and pagination |
| `POST /scene/update` | developer | Update scene (respects checkout locks) |
| `POST /scene/delete` | developer | Soft-delete scene (blocks if referenced) |
| `POST /scene/validate` | user | Validate scene structure |

### Checkout Workflow

| Endpoint | Permission | Purpose |
|----------|------------|---------|
| `POST /scene/checkout` | developer | Lock scene for editing (returns token + scene) |
| `POST /scene/commit` | developer | Save changes and release lock |
| `POST /scene/discard` | developer | Release lock without saving |
| `POST /scene/heartbeat` | developer | Extend checkout lock TTL |
| `POST /scene/history` | user | Get version history |

### Instantiation

| Endpoint | Permission | Purpose |
|----------|------------|---------|
| `POST /scene/instantiate` | developer | Declare scene placed in game world (publishes event) |
| `POST /scene/destroy-instance` | developer | Declare scene instance removed (publishes event) |

**Note**: Instantiation endpoints are **notification-only**. The caller has already placed the scene; these endpoints publish events so other services can react.

### Validation Rules

| Endpoint | Permission | Purpose |
|----------|------------|---------|
| `POST /scene/register-validation-rules` | developer | Register game-specific validation rules |
| `POST /scene/get-validation-rules` | user | Retrieve registered validation rules |

### Query & Discovery

| Endpoint | Permission | Purpose |
|----------|------------|---------|
| `POST /scene/search` | user | Full-text search across scenes |
| `POST /scene/find-references` | user | Find scenes that reference a target scene |
| `POST /scene/find-asset-usage` | user | Find scenes using a specific asset |
| `POST /scene/duplicate` | developer | Clone scene with new IDs |

---

## Event System

### Lifecycle Events

Published automatically for scene document changes:

| Topic | Description |
|-------|-------------|
| `scene.created` | New scene document created |
| `scene.updated` | Scene document modified |
| `scene.deleted` | Scene document deleted |

### Instantiation Events

Published when scenes are placed/removed from game world:

| Topic | Description |
|-------|-------------|
| `scene.instantiated` | Scene placed in game world |
| `scene.destroyed` | Scene instance removed |

**SceneInstantiatedEvent** contains:
- `instanceId`: Unique instance identifier (caller-provided)
- `sceneAssetId`: Source scene ID
- `sceneVersion`: Version instantiated
- `regionId`: Region where placed
- `worldTransform`: World-space transform for scene origin
- `metadata`: Caller-provided metadata

### Checkout Events

| Topic | Description |
|-------|-------------|
| `scene.checked_out` | Scene locked for editing |
| `scene.committed` | Checkout changes saved |
| `scene.checkout.discarded` | Lock released without saving |

> **Note**: `scene.checkout.expired` is defined in the schema but not currently published. Expired checkouts are detected lazily when another editor attempts to checkout the scene (enabling takeover of expired locks).

### Validation Events

| Topic | Description |
|-------|-------------|
| `scene.validation_rules.updated` | Game-specific rules registered/updated |

### Reference Events

> **Note**: `scene.reference.broken` is defined in the schema but not currently published. Reference integrity is checked on load, not actively monitored.

---

## Checkout/Lock System

The checkout system prevents concurrent editing conflicts:

1. **Checkout** - Acquire exclusive lock, receive token + scene content
2. **Edit** - Make changes locally
3. **Heartbeat** - Periodically extend lock (max 10 extensions)
4. **Commit** or **Discard** - Save changes and release, or release without saving

### Configuration

| Setting | Default | Description |
|---------|---------|-------------|
| `DefaultCheckoutTtlMinutes` | 60 | Lock duration |
| `MaxCheckoutExtensions` | 10 | Max heartbeat extensions (10 x 60min = 10hr max) |
| `CheckoutHeartbeatIntervalSeconds` | 30 | Recommended heartbeat interval |

### Expired Lock Handling

- Locks stored with TTL + 5 minute buffer in Redis
- Expired checkouts can be "taken over" by another editor
- No active background expiration monitoring (relies on TTL eviction)

---

## Reference Resolution

Scenes can reference other scenes via `reference` type nodes:

```yaml
- nodeId: "..."
  refId: "tavern_ref"
  nodeType: reference
  localTransform: { ... }
  referenceSceneId: "550e8400-e29b-41d4-a716-446655440001"
```

### Resolving References

Use `resolveReferences: true` on GetScene to embed referenced scenes:

```json
{
  "sceneId": "...",
  "resolveReferences": true,
  "maxReferenceDepth": 3
}
```

Response includes:
- `resolvedReferences`: Successfully resolved scenes
- `unresolvedReferences`: Failed resolutions (circular, missing, depth exceeded)
- `resolutionErrors`: Error descriptions

### Reference Tracking

- References are tracked bidirectionally
- Deletion is blocked if other scenes reference a scene
- `find-references` endpoint finds all scenes referencing a target

---

## Validation

### Structural Validation (Always Applied)

| Rule | Description |
|------|-------------|
| `valid-uuid` | sceneId and nodeId must be valid non-empty UUIDs |
| `valid-version` | Version must match MAJOR.MINOR.PATCH |
| `single-root` | Scene must have exactly one root node |
| `root-no-parent` | Root node must have null parentNodeId |
| `unique-refid` | refId must be unique within scene |
| `refid-pattern` | refId must match `^[a-z][a-z0-9_]*$` |
| `valid-transform` | Transform must have valid components |
| `node-count-limit` | Total nodes must not exceed MaxNodeCount |

### Game-Specific Validation (Optional)

Register custom rules per gameId + sceneType:

```json
{
  "gameId": "my-game",
  "sceneType": "building",
  "rules": [
    {
      "ruleId": "building-entrance",
      "description": "Buildings must have at least one entrance marker",
      "severity": "error",
      "ruleType": "require_tag",
      "config": {
        "nodeType": "marker",
        "tag": "entrance",
        "minCount": 1
      }
    }
  ]
}
```

Rule types:
- `require_tag`: Scene must have N nodes with tag
- `forbid_tag`: No nodes can have this tag
- `require_node_type`: Scene must have N nodes of type

### Validation at Composition vs. Commit Time

The SceneComposer SDK applies validation at two levels with different strictness:

| When | What | Behavior |
|------|------|----------|
| **Composition time** (SDK) | Structural rules, asset slot mismatches | Warnings — show UI indicator but allow |
| **Commit time** (lib-scene) | Required fields, structural integrity | Errors — block commit, require fix |
| **Game-specific rules** | Per-game constraints | Configurable severity: warn or block |

This tiered approach provides immediate feedback during editing while enforcing integrity at persistence boundaries.

---

## Configuration

Environment variables (prefix: `SCENE_`):

| Variable | Default | Description |
|----------|---------|-------------|
| `SCENE_ENABLED` | true | Enable/disable service |
| `SCENE_DEFAULT_CHECKOUT_TTL_MINUTES` | 60 | Lock duration |
| `SCENE_MAX_CHECKOUT_EXTENSIONS` | 10 | Max lock extensions |
| `SCENE_DEFAULT_MAX_REFERENCE_DEPTH` | 3 | Default resolution depth |
| `SCENE_MAX_REFERENCE_DEPTH_LIMIT` | 10 | Hard limit for resolution |
| `SCENE_MAX_VERSION_RETENTION_COUNT` | 100 | Max retention limit |
| `SCENE_MAX_NODE_COUNT` | 10000 | Max nodes per scene |
| `SCENE_MAX_TAGS_PER_SCENE` | 50 | Max scene tags |
| `SCENE_MAX_TAGS_PER_NODE` | 20 | Max node tags |
| `SCENE_MAX_LIST_RESULTS` | 200 | List pagination limit |
| `SCENE_MAX_SEARCH_RESULTS` | 100 | Search pagination limit |

---

## Example Scene Document

```yaml
$schema: "bannou://schemas/scene/v1"
sceneId: "550e8400-e29b-41d4-a716-446655440001"
gameId: "my-game"
sceneType: building
name: "Cozy Tavern Interior"
version: "1.2.0"
tags: ["interior", "tavern", "social"]

root:
  nodeId: "00000000-0000-0000-0000-000000000001"
  refId: "tavern_root"
  name: "Tavern Root"
  nodeType: group
  localTransform:
    position: { x: 0, y: 0, z: 0 }
    rotation: { x: 0, y: 0, z: 0, w: 1 }
    scale: { x: 1, y: 1, z: 1 }
  children:
    - nodeId: "00000000-0000-0000-0000-000000000002"
      refId: "main_entrance"
      name: "Main Entrance"
      nodeType: marker
      localTransform:
        position: { x: 0, y: 0, z: 7 }
        rotation: { x: 0, y: 0, z: 0, w: 1 }
        scale: { x: 1, y: 1, z: 1 }
      tags: ["entrance", "public"]

    - nodeId: "00000000-0000-0000-0000-000000000010"
      refId: "bar_area"
      name: "Bar Area"
      nodeType: group
      localTransform:
        position: { x: -4, y: 0, z: -2 }
        rotation: { x: 0, y: 0.707, z: 0, w: 0.707 }
        scale: { x: 1, y: 1, z: 1 }
      children:
        - nodeId: "00000000-0000-0000-0000-000000000011"
          refId: "counter"
          name: "Bar Counter"
          nodeType: mesh
          localTransform:
            position: { x: 0, y: 0, z: 0 }
            rotation: { x: 0, y: 0, z: 0, w: 1 }
            scale: { x: 1, y: 1, z: 1 }
          asset:
            bundleId: "550e8400-e29b-41d4-a716-446655440100"
            assetId: "550e8400-e29b-41d4-a716-446655440102"
          tags: ["furniture", "static"]

        - nodeId: "00000000-0000-0000-0000-000000000012"
          refId: "bartender_position"
          name: "Bartender Position"
          nodeType: marker
          localTransform:
            position: { x: 0, y: 0, z: -0.5 }
            rotation: { x: 0, y: 0, z: 0, w: 1 }
            scale: { x: 1, y: 1, z: 1 }
          tags: ["position", "npc"]
          annotations:
            game:
              suggestedRole: "bartender"
```

---

## Integration Patterns

### Game Engine Integration

1. **Load scene** via GET /scene/get
2. **Instantiate geometry** using asset references and transforms
3. **Notify Bannou** via POST /scene/instantiate
4. Other services react to `scene.instantiated` event

### Map Service Integration

Map Service subscribes to `scene.instantiated` events:

1. Receive event with sceneId and worldTransform
2. Fetch scene via Scene Service GET endpoint
3. Extract spatial objects (markers, volumes, meshes)
4. Transform to world coordinates (apply worldTransform)
5. Index in spatial data structure

**Note**: This integration is consumer-driven. Scene Service does not push to Map Service directly.

### Editor Tooling Integration

For scene editing tools using the SceneComposer SDK:

1. **Checkout** scene before editing
2. **Heartbeat** every 30s to maintain lock
3. **Edit** via SDK operations (create, move, bind assets, reparent)
4. **Commit** changes when done (SDK coordinates with lib-scene)
5. Use **find-references** before refactoring shared scenes
6. Use **duplicate** for creating variants

### Asset Service Integration

Scene nodes reference assets from lib-asset bundles. The integration flow:

1. **Author creates assets** (textures, models) and uploads via lib-asset
2. **Author bundles assets** into `.bannou` bundles for distribution
3. **Scene nodes reference assets** by bundleId + assetId
4. **SceneComposer SDK** loads bundles, resolves asset references, and provides bytes to engine-specific type loaders
5. **Engine bridge** deserializes bytes into engine-native types (Stride Model, Godot Mesh3D, Unity Mesh)

Asset loading is engine-specific because each engine has its own binary format for models, textures, and materials. The SDK provides the loading abstraction (`IAssetLoader<T>`); engine extensions provide the deserialization.

### Save-Load Integration (Voxel Persistence)

For scenes with voxel node types (player housing, dungeon interiors), modified voxel data persists through Save-Load:

1. **Base `.bvox` asset** stored in MinIO via lib-asset
2. **Player modifies voxels** using the Voxel Builder SDK
3. **Modified chunks** (16x16x16, LZ4 compressed) produce binary deltas
4. **Save-Load** stores chunk-scoped deltas via its binary delta system (BSDIFF/XDELTA)
5. **On load**: base + deltas reconstruct the current voxel state

This uses Save-Load's polymorphic ownership (`ownerType: seed`, `ownerId: housingSeedId`) for housing save slots.

---

## Design Decisions

### No New Plugin for Composition

The SceneComposer SDK handles client-side composition; lib-scene handles server-side persistence. There is no need for a `lib-composer` plugin because:

- **lib-scene already provides**: Storage, checkout/locking, version history, validation, search
- **The SDK provides**: Node operations, transforms, selection, undo/redo, asset binding
- **A new plugin would duplicate**: Validation logic, scene state management, checkout coordination

The decision is a hybrid: SDK package for client-side composition + schema extensions on lib-scene for server-side validation enhancements (attachment points, affordances). See the [Housing FAQ](../faqs/WHY-IS-THERE-NO-PLAYER-HOUSING-PLUGIN.md) for how player housing composes entirely from existing Scene + Seed + Gardener + Save-Load primitives.

### YAML Format Is the Runtime Format

Scenes are stored as YAML and used as YAML at runtime. There is no binary compilation or "baking" step:

- **Same format everywhere**: In the editor, in lib-scene, in the game client
- **Human-readable**: Scenes can be version-controlled, diffed, and edited by hand
- **No structure loss**: The hierarchy is preserved identically at all stages
- **Reference nodes load on demand**: Referenced scenes are fetched at runtime, not flattened at build time

This avoids the trap that cost Unity 13 years with Nested Prefabs: Unity's prefab concept only existed at editor-time; at runtime, data was "baked" into a flat representation, losing all hierarchy information. Bannou scenes are documents that exist identically everywhere.

### Lock-Based Editing (Not CRDT)

The Scene service uses exclusive checkout locks for concurrent editing prevention. This is sufficient because:

- **Team sizes of 2-20** work well with locks
- **Content is separable** — different scenes, different regions
- **Most edits are solo work** on specific prefabs
- **Lock contention is low** with good tooling (heartbeats, TTL expiry, takeover of expired locks)

CRDT/OT-based real-time collaboration adds significant complexity (6+ months of work), especially for hierarchical data where parent-child relationships make conflict resolution hard. The enhancement path is tracked in [Issue #441](https://github.com/beyond-immersion/bannou-service/issues/441) with a staged approach from enhanced locking to optimistic merge to hybrid real-time.

### Each Scene Is Its Own Document

Editing a scene never accidentally modifies another scene. Reference nodes point to other scenes, but:

- **Editing a referenced scene** requires opening THAT scene (separate checkout)
- **No "apply to prefab"** button that overwrites shared templates
- **No auto-propagation** — reference nodes always load the latest version of referenced scenes, but the referencing scene itself doesn't change
- **The document boundary is the editing boundary**

---

## Planned Extensions

### Scene Format Versioning

Scenes will adopt a **SchemaVer** versioning pattern (MODEL-REVISION-ADDITION) for forward-compatible evolution:

```yaml
$schema: "bannou://scene/v2.1.0"
schema_version: "2.1.0"
```

- **MODEL** (2.x.x): Breaking change to existing fields — requires migration
- **REVISION** (x.1.x): Change affecting existing data — may require migration
- **ADDITION** (x.x.1): New optional field — backward compatible

Migration is lazy on save: old-format scenes are read and migrated in memory, then saved as the latest format on commit. A migration chain of dedicated migrators handles each version step.

### Attachment Points & Affordances

Schema extensions planned for procedural generation support:

- **Attachment Points**: Predefined locations on nodes for attaching child objects (e.g., wall hooks, shelf positions), with `acceptsTags` filtering compatible assets
- **Affordances**: Structured tags describing what a node can do (walkable, sittable, interactive, destructible), enabling AI-driven placement decisions and NPC navigation
- **Asset Slots**: Placeholders defining acceptable asset categories for random or targeted swapping, enabling procedural variation within authored templates

These are schema additions to the SceneNode model, validated at commit time by lib-scene.

### Voxel Node Type

A `voxel` node type referencing `.bvox` assets from the Voxel Builder SDK. Voxel nodes carry annotations for grid scale, meshing strategy (greedy/culled/marching-cubes), and collision mesher. Key consumers: player housing (interactive voxel construction), dungeon cores (chamber reshaping), NPC builders (structure construction). See [SCENE.md Extension #8](../plugins/SCENE.md) for details.

---

## Related Documentation

- **Deep Dive**: [SCENE.md](../plugins/SCENE.md) — Service implementation details, state stores, quirks
- **Deep Dive**: [ASSET.md](../plugins/ASSET.md) — Asset storage, bundles, processing pipeline
- **Deep Dive**: [SAVE-LOAD.md](../plugins/SAVE-LOAD.md) — Delta persistence for voxel data
- **Guide**: [ASSET-SDK.md](ASSET-SDK.md) — Asset loading and bundle management patterns
- **Guide**: [SDK-OVERVIEW.md](SDK-OVERVIEW.md) — All Bannou SDKs with decision tree
- **FAQ**: [WHY-IS-THERE-NO-PLAYER-HOUSING-PLUGIN.md](../faqs/WHY-IS-THERE-NO-PLAYER-HOUSING-PLUGIN.md) — Housing as composed garden
- **FAQ**: [WHY-DOESNT-ASSET-ROUTE-THROUGH-WEBSOCKET.md](../faqs/WHY-DOESNT-ASSET-ROUTE-THROUGH-WEBSOCKET.md) — Pre-signed URL pattern
- **Schema**: `schemas/scene-api.yaml`, `schemas/scene-events.yaml`, `schemas/scene-configuration.yaml`
- **Issue**: [#159](https://github.com/beyond-immersion/bannou-service/issues/159) — Spatial coordinate system contract
- **Issue**: [#432](https://github.com/beyond-immersion/bannou-service/issues/432) — Player housing as composed garden behavior
