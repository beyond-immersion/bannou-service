# Scene Service Guide

> **Status**: PRODUCTION
> **Version**: 1.0.0
> **Schema**: `schemas/scene-api.yaml`, `schemas/scene-events.yaml`, `schemas/scene-configuration.yaml`

---

## Overview

The Scene Service provides hierarchical composition storage for game worlds. It stores and retrieves scene documents with arbitrary node hierarchies representing meshes, markers, volumes, emitters, and references to other scenes.

**Core Responsibility**: Store and retrieve hierarchical scene documents.

**Not Responsible For**:
- Computing world transforms (consumers apply transforms themselves)
- Determining affordances (runtime geometry concern)
- Pushing data to other services (event-driven decoupling)
- Interpreting node behavior (consumers decide what nodes mean)

This is intentionally a "dumb data service" - it stores structured data and publishes events when that data changes. Consumers (Map Service, game engines, AI systems) decide how to interpret and use the data.

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

A tavern scene defines "bar counter is child of bar area, rotated 45°". Mapping stores "object at world coords (1547, 0, 892) is queryable for spatial operations."

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
| `MaxCheckoutExtensions` | 10 | Max heartbeat extensions (10 × 60min = 10hr max) |
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
  annotations:
    reference:
      sceneAssetId: "550e8400-e29b-41d4-a716-446655440001"
      version: "1.2.0"  # Optional - latest if omitted
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

---

## Configuration

Environment variables (prefix: `SCENE_`):

| Variable | Default | Description |
|----------|---------|-------------|
| `SCENE_ENABLED` | true | Enable/disable service |
| `SCENE_ASSET_BUCKET` | "scenes" | Storage bucket name |
| `SCENE_DEFAULT_CHECKOUT_TTL_MINUTES` | 60 | Lock duration |
| `SCENE_MAX_CHECKOUT_EXTENSIONS` | 10 | Max lock extensions |
| `SCENE_DEFAULT_MAX_REFERENCE_DEPTH` | 3 | Default resolution depth |
| `SCENE_MAX_REFERENCE_DEPTH_LIMIT` | 10 | Hard limit for resolution |
| `SCENE_DEFAULT_VERSION_RETENTION_COUNT` | 3 | Versions to retain |
| `SCENE_MAX_VERSION_RETENTION_COUNT` | 100 | Max retention limit |
| `SCENE_MAX_NODE_COUNT` | 10000 | Max nodes per scene |
| `SCENE_MAX_SCENE_SIZE_BYTES` | 10485760 | Max scene size (10MB) |
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

Map Service can optionally subscribe to `scene.instantiated` events:

1. Receive event with sceneId and worldTransform
2. Fetch scene via Scene Service GET endpoint
3. Extract spatial objects (markers, volumes, meshes)
4. Transform to world coordinates (apply worldTransform)
5. Index in spatial data structure

**Note**: This integration is optional and consumer-driven. Scene Service does not push to Map Service directly.

### Editor Tooling Integration

For scene editing tools:

1. **Checkout** scene before editing
2. **Heartbeat** every 30s to maintain lock
3. **Commit** changes when done
4. Use **find-references** before refactoring
5. Use **duplicate** for creating variants

---

## Future Considerations

The following are not currently implemented but may be added:

1. **Concurrent editing** - Currently single-editor only
2. **Advanced search** - Currently simple substring matching
3. **Version retrieval** - Can list history but not fetch specific old version
4. **Scene diffing** - Cannot compare versions
5. **Real-time collaboration events** - No WebSocket push for editor sync

These are intentionally deferred as they add complexity beyond core use cases.

---

## Related Documentation

- **Schema**: `schemas/scene-api.yaml`
- **Events**: `schemas/scene-events.yaml`
- **Configuration**: `schemas/scene-configuration.yaml`
- **Design Philosophy**: `docs/BANNOU_DESIGN.md` (monoservice architecture)
- **Testing**: `docs/operations/TESTING.md`
