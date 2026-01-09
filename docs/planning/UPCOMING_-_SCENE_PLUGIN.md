# Scene Plugin

> **Status**: PLANNING
> **Created**: 2026-01-09
> **Related**: [BANNOU_DESIGN.md](../BANNOU_DESIGN.md), [THE_DREAM.md](./THE_DREAM.md), [Mapping API](../../schemas/mapping-api.yaml)

## 1. Overview

The Scene Plugin provides hierarchical composition storage for game worlds. It defines **what exists and where** within a scene - not what anything does at runtime.

**Core Responsibility**: Store and retrieve hierarchical scene documents with nodes representing meshes, markers, volumes, emitters, and references to other scenes.

**Not Responsible For**:
- Computing world transforms (consumers apply transforms themselves)
- Determining affordances (runtime geometry concern)
- Pushing data to other services (event-driven decoupling)
- Interpreting node behavior (consumers decide what nodes mean)

---

## 2. Architectural Position

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              DATA FLOW                                       │
│                                                                              │
│  Scene Service                                                               │
│  (Composition Storage)                                                       │
│         │                                                                    │
│         │  "I store hierarchical definitions"                                │
│         │  "I don't know where, how big, or which way up scenes are placed" │
│         │                                                                    │
│         ▼                                                                    │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │                     scene.instantiated event                         │    │
│  │  "Someone placed scene X at world position Y with transform Z"       │    │
│  │  (Contains: sceneId, instanceId, worldTransform, regionId)           │    │
│  └──────────────────────────────┬──────────────────────────────────────┘    │
│                                 │                                            │
│         ┌───────────────────────┼───────────────────────────────────┐       │
│         ▼                       ▼                                   ▼       │
│  ┌─────────────┐       ┌─────────────────┐              ┌─────────────────┐ │
│  │   Mapping   │       │      Actor      │              │   Game Server   │ │
│  │   Service   │       │     Service     │              │                 │ │
│  │             │       │                 │              │                 │ │
│  │ Fetches     │       │ Finds spawn     │              │ Already has     │ │
│  │ scene,      │       │ markers,        │              │ geometry,       │ │
│  │ extracts    │       │ spawns NPCs     │              │ computes        │ │
│  │ spatial     │       │ at transformed  │              │ affordances     │ │
│  │ objects     │       │ positions       │              │ if needed       │ │
│  └─────────────┘       └─────────────────┘              └─────────────────┘ │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
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

### Event-Driven Decoupling

Scene Service publishes events. Consumers react independently:

1. Game server instantiates a scene in its world
2. Game server calls `/scene/instantiate` to declare this
3. Scene Service publishes `scene.instantiated` event
4. Mapping Service (if listening) fetches scene, extracts what matters to it
5. Actor Service (if listening) finds spawn markers, spawns NPCs
6. Other consumers react as needed

Scene Service has no knowledge of what consumers do with the data.

---

## 3. Partitioning

All scenes are partitioned by `gameId` and `sceneType` for efficient querying and optional validation.

### GameId

```yaml
gameId:
  type: string
  default: "00000000-0000-0000-0000-000000000000"
  description: |
    Game service identifier. Used for partitioning and validation rule lookup.
    Not validated against game-service registry - treated as opaque partition key.
```

### SceneType

```yaml
SceneType:
  type: string
  enum:
    - unknown      # Unclassified
    - region       # Large world area (continent, zone)
    - city         # Settlement, town, village
    - district     # Subsection of a city
    - lot          # Plot of land (housing, farm)
    - building     # Structure (house, shop, dungeon entrance)
    - room         # Interior space within a building
    - dungeon      # Underground/instanced adventure area
    - arena        # Dedicated combat or challenge space
    - vehicle      # Ship, mech, cart (movable container)
    - prefab       # Reusable composition (furniture set, decoration cluster)
    - cutscene     # Cinematic scene
    - other        # Intentionally uncategorized
```

### Per-Partition Validation

Each `gameId` + `sceneType` combination can have registered validation rules:

```json
{
  "gameId": "arcadia-online",
  "sceneType": "building",
  "rules": [
    {
      "ruleId": "building-entrance",
      "description": "Buildings must have at least one entrance marker",
      "severity": "error",
      "requireTag": { "nodeType": "marker", "tag": "entrance", "minCount": 1 }
    }
  ]
}
```

---

## 4. Data Model

### Scene Document

```yaml
Scene:
  type: object
  required: [sceneId, gameId, sceneType, name, version, root]
  properties:
    $schema:
      type: string
      const: "bannou://schemas/scene/v1"
      description: Schema identifier for validation
    sceneId:
      type: string
      format: uuid
      description: Unique scene identifier
    gameId:
      type: string
      default: "00000000-0000-0000-0000-000000000000"
      description: Game service identifier for partitioning
    sceneType:
      $ref: '#/components/schemas/SceneType'
      default: unknown
      description: Scene classification for querying and validation
    name:
      type: string
      description: Human-readable scene name
    description:
      type: string
      nullable: true
      description: Optional scene description
    version:
      type: string
      pattern: '^\d+\.\d+\.\d+$'
      description: Semantic version (MAJOR.MINOR.PATCH)
    root:
      $ref: '#/components/schemas/SceneNode'
      description: Root node of scene hierarchy
    tags:
      type: array
      items:
        type: string
      default: []
      description: Searchable tags for filtering
    metadata:
      type: object
      additionalProperties: true
      nullable: true
      description: Scene-level metadata (author, thumbnail, editor preferences)
    createdAt:
      type: string
      format: date-time
    updatedAt:
      type: string
      format: date-time
```

### Node Types

```yaml
NodeType:
  type: string
  enum:
    - group           # Container with no visual (organizational)
    - mesh            # 3D geometry reference
    - marker          # Named point in space (no visual)
    - volume          # 3D region definition (box, sphere, etc.)
    - emitter         # Point that emits something (sound, particles)
    - reference       # Reference to another scene (nested composition)
    - custom          # Extension point for game-specific types
  description: |
    Structural node type. Indicates what kind of data the node contains,
    not how it will be used at runtime. Consumers interpret nodes according
    to their own needs via tags and annotations.
```

### Scene Node

```yaml
SceneNode:
  type: object
  required: [nodeId, refId, name, nodeType, localTransform]
  properties:
    nodeId:
      type: string
      format: uuid
      description: Globally unique node identifier
    refId:
      type: string
      pattern: '^[a-z][a-z0-9_]*$'
      description: |
        Scene-local reference identifier. Unique within scene.
        Examples: "main_door", "center_table", "npc_position_1"
    parentNodeId:
      type: string
      format: uuid
      nullable: true
      description: Parent node ID. Null for root node.
    name:
      type: string
      description: Human-readable display name
    nodeType:
      $ref: '#/components/schemas/NodeType'
    localTransform:
      $ref: '#/components/schemas/Transform'
      description: Transform relative to parent
    asset:
      $ref: '#/components/schemas/AssetReference'
      nullable: true
      description: Optional asset binding (mesh, sound, particle asset)
    children:
      type: array
      items:
        $ref: '#/components/schemas/SceneNode'
      default: []
    enabled:
      type: boolean
      default: true
      description: Whether node is active in the scene definition
    sortOrder:
      type: integer
      default: 0
      description: Sibling ordering for deterministic iteration
    tags:
      type: array
      items:
        type: string
      default: []
      description: Arbitrary tags for consumer filtering
    annotations:
      type: object
      additionalProperties: true
      nullable: true
      description: |
        Consumer-specific data. Stored and returned without interpretation.
        Structure is entirely consumer-defined.
```

### Supporting Types

```yaml
Vector3:
  type: object
  required: [x, y, z]
  properties:
    x: { type: number, format: double }
    y: { type: number, format: double }
    z: { type: number, format: double }

Quaternion:
  type: object
  required: [x, y, z, w]
  properties:
    x: { type: number, format: double }
    y: { type: number, format: double }
    z: { type: number, format: double }
    w: { type: number, format: double }

Transform:
  type: object
  required: [position, rotation, scale]
  properties:
    position:
      $ref: '#/components/schemas/Vector3'
    rotation:
      $ref: '#/components/schemas/Quaternion'
    scale:
      $ref: '#/components/schemas/Vector3'

AssetReference:
  type: object
  required: [assetId]
  properties:
    bundleId:
      type: string
      format: uuid
      nullable: true
      description: Optional bundle containing the asset
    assetId:
      type: string
      format: uuid
      description: Asset identifier in lib-asset
    variantId:
      type: string
      nullable: true
      description: Variant identifier (consumer interprets)

VolumeShape:
  type: string
  enum: [box, sphere, capsule, cylinder]
  description: Shape of a volume node
```

### Annotations Pattern

Scene nodes carry annotations - arbitrary data that consumers interpret. Scene Service stores and retrieves without validation.

**Namespacing convention** (consumer responsibility, not enforced):

```yaml
annotations:
  render:
    castShadows: true
    lodBias: 1.0
  physics:
    collisionShape: "convex"
    mass: 0
  audio:
    occlusionFactor: 0.8
  editor:
    locked: false
    color: "#FF5500"
  arcadia:  # Game-specific namespace
    interactionType: "sit"
```

---

## 5. API Design

### Scene CRUD

```yaml
/scene/create:
  post:
    operationId: createScene
    x-permissions: [scene.create]
    summary: Create a new scene document

/scene/get:
  post:
    operationId: getScene
    x-permissions: [scene.read]
    summary: Retrieve scene by ID
    # Optional: resolveReferences to embed referenced scenes

/scene/list:
  post:
    operationId: listScenes
    x-permissions: [scene.read]
    summary: List scenes with filtering
    # Filters: gameId, sceneType, sceneTypes[], tags[], nameContains

/scene/update:
  post:
    operationId: updateScene
    x-permissions: [scene.edit]
    summary: Update scene document

/scene/delete:
  post:
    operationId: deleteScene
    x-permissions: [scene.delete]
    summary: Delete scene
```

### Instantiation

```yaml
/scene/instantiate:
  post:
    operationId: instantiateScene
    x-permissions: [scene.instantiate]
    summary: Declare that a scene instance was created in the game world
    description: |
      Records a scene instantiation and publishes an event. This is a
      NOTIFICATION endpoint - the caller has already instantiated the scene.

      1. Validates scene exists and is accessible
      2. Records instance metadata
      3. Publishes scene.instantiated event

      Consumers react to the event independently.
    requestBody:
      content:
        application/json:
          schema:
            type: object
            required: [assetId, instanceId, regionId, worldTransform]
            properties:
              assetId:
                type: string
                format: uuid
                description: Scene asset ID that was instantiated
              version:
                type: string
                nullable: true
                description: Specific version (null = latest)
              instanceId:
                type: string
                format: uuid
                description: Caller-provided unique instance ID
              regionId:
                type: string
                format: uuid
                description: Region where scene was placed
              worldTransform:
                $ref: '#/components/schemas/Transform'
                description: World-space transform for scene origin
              metadata:
                type: object
                additionalProperties: true
                nullable: true
                description: Caller-provided metadata passed to event

/scene/destroy-instance:
  post:
    operationId: destroyInstance
    x-permissions: [scene.instantiate]
    summary: Declare that a scene instance was removed
```

### Versioning and Checkout

```yaml
/scene/checkout:
  post:
    operationId: checkoutScene
    x-permissions: [scene.edit]
    summary: Lock scene for editing

/scene/commit:
  post:
    operationId: commitScene
    x-permissions: [scene.edit]
    summary: Save changes and release lock

/scene/discard:
  post:
    operationId: discardCheckout
    x-permissions: [scene.edit]
    summary: Release lock without saving

/scene/history:
  post:
    operationId: getSceneHistory
    x-permissions: [scene.read]
    summary: List version history
```

### Validation

```yaml
/scene/validate:
  post:
    operationId: validateScene
    x-permissions: [scene.read]
    summary: Validate scene structure and game-specific rules

/scene/register-validation-rules:
  post:
    operationId: registerValidationRules
    x-permissions: [scene.admin]
    summary: Register validation rules for gameId+sceneType
```

---

## 6. Event System

### Lifecycle Events (via x-lifecycle)

| Event | Description |
|-------|-------------|
| `scene.created` | Scene document created |
| `scene.updated` | Scene document modified |
| `scene.deleted` | Scene document deleted |

### Operational Events

```yaml
SceneInstantiatedEvent:
  topic: scene.instantiated
  properties:
    instanceId:
      type: string
      format: uuid
      description: Unique instance identifier
    sceneAssetId:
      type: string
      format: uuid
      description: Source scene asset ID
    sceneVersion:
      type: string
      description: Version instantiated
    sceneName:
      type: string
      description: Scene name (for logging)
    gameId:
      type: string
      description: Game ID from scene
    sceneType:
      $ref: '#/components/schemas/SceneType'
      description: Scene type from scene
    regionId:
      type: string
      format: uuid
      description: Region where placed
    worldTransform:
      $ref: '#/components/schemas/Transform'
      description: World-space transform (consumers must apply)
    metadata:
      type: object
      additionalProperties: true
      nullable: true
      description: Caller-provided metadata
    instantiatedAt:
      type: string
      format: date-time
    instantiatedBy:
      type: string
      nullable: true
      description: App-id of instantiator

SceneDestroyedEvent:
  topic: scene.destroyed
  properties:
    instanceId:
      type: string
      format: uuid
    sceneAssetId:
      type: string
      format: uuid
    regionId:
      type: string
      format: uuid
    destroyedAt:
      type: string
      format: date-time
    destroyedBy:
      type: string
      nullable: true
    metadata:
      type: object
      additionalProperties: true
      nullable: true

SceneCheckedOutEvent:
  topic: scene.checked_out
  properties:
    sceneId:
      type: string
      format: uuid
    checkedOutBy:
      type: string
    expiresAt:
      type: string
      format: date-time

SceneCommittedEvent:
  topic: scene.committed
  properties:
    sceneId:
      type: string
      format: uuid
    version:
      type: string
    committedBy:
      type: string
```

---

## 7. Use Cases

### Tavern Interior

```yaml
$schema: "bannou://schemas/scene/v1"
sceneId: "550e8400-e29b-41d4-a716-446655440001"
gameId: "arcadia-online"
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

    # Building shell
    - nodeId: "00000000-0000-0000-0000-000000000002"
      refId: "building"
      name: "Tavern Building"
      nodeType: mesh
      localTransform:
        position: { x: 0, y: 0, z: 0 }
        rotation: { x: 0, y: 0, z: 0, w: 1 }
        scale: { x: 1, y: 1, z: 1 }
      asset:
        bundleId: "550e8400-e29b-41d4-a716-446655440100"
        assetId: "550e8400-e29b-41d4-a716-446655440101"
        variantId: "weathered"
      tags: ["structure", "static"]
      annotations:
        render:
          castShadows: true
      children:

        # Entrance marker
        - nodeId: "00000000-0000-0000-0000-000000000003"
          refId: "main_entrance"
          name: "Main Entrance"
          nodeType: marker
          localTransform:
            position: { x: 0, y: 0, z: 7 }
            rotation: { x: 0, y: 0, z: 0, w: 1 }
            scale: { x: 1, y: 1, z: 1 }
          tags: ["entrance", "public"]
          children: []

        # Bar area group
        - nodeId: "00000000-0000-0000-0000-000000000010"
          refId: "bar_area"
          name: "Bar Area"
          nodeType: group
          localTransform:
            position: { x: -4, y: 0, z: -2 }
            rotation: { x: 0, y: 0.707, z: 0, w: 0.707 }
            scale: { x: 1, y: 1, z: 1 }
          children:

            # Bar counter
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
              children: []

            # Bartender position
            - nodeId: "00000000-0000-0000-0000-000000000012"
              refId: "behind_counter"
              name: "Behind Counter Position"
              nodeType: marker
              localTransform:
                position: { x: 0, y: 0, z: -0.5 }
                rotation: { x: 0, y: 0, z: 0, w: 1 }
                scale: { x: 1, y: 1, z: 1 }
              tags: ["position", "npc"]
              annotations:
                arcadia:
                  suggestedRole: "bartender"
              children: []

            # Ambient sound
            - nodeId: "00000000-0000-0000-0000-000000000013"
              refId: "bar_ambience"
              name: "Bar Ambient Sound"
              nodeType: emitter
              localTransform:
                position: { x: 0, y: 1.5, z: 0 }
                rotation: { x: 0, y: 0, z: 0, w: 1 }
                scale: { x: 1, y: 1, z: 1 }
              asset:
                assetId: "550e8400-e29b-41d4-a716-446655440201"
              tags: ["audio", "ambient"]
              annotations:
                audio:
                  volume: 0.4
                  loop: true
              children: []
```

### Dungeon Room Template

```yaml
$schema: "bannou://schemas/scene/v1"
sceneId: "550e8400-e29b-41d4-a716-446655440002"
gameId: "arcadia-online"
sceneType: dungeon
name: "Treasure Room Template"
version: "1.0.0"
tags: ["dungeon", "room", "template"]

metadata:
  generator:
    roomType: "treasure"
    minConnections: 1
    maxConnections: 2

root:
  nodeId: "10000000-0000-0000-0000-000000000001"
  refId: "room_root"
  name: "Treasure Room"
  nodeType: group
  localTransform:
    position: { x: 0, y: 0, z: 0 }
    rotation: { x: 0, y: 0, z: 0, w: 1 }
    scale: { x: 1, y: 1, z: 1 }
  children:

    # Room geometry
    - nodeId: "10000000-0000-0000-0000-000000000002"
      refId: "room_mesh"
      name: "Room Geometry"
      nodeType: mesh
      localTransform:
        position: { x: 0, y: 0, z: 0 }
        rotation: { x: 0, y: 0, z: 0, w: 1 }
        scale: { x: 1, y: 1, z: 1 }
      asset:
        bundleId: "550e8400-e29b-41d4-a716-446655440500"
        assetId: "550e8400-e29b-41d4-a716-446655440501"
      tags: ["structure"]
      children: []

    # Room bounds
    - nodeId: "10000000-0000-0000-0000-000000000003"
      refId: "room_bounds"
      name: "Room Bounds"
      nodeType: volume
      localTransform:
        position: { x: 0, y: 2, z: 0 }
        rotation: { x: 0, y: 0, z: 0, w: 1 }
        scale: { x: 1, y: 1, z: 1 }
      tags: ["bounds", "room"]
      annotations:
        volume:
          shape: "box"
          width: 10
          height: 4
          depth: 10
      children: []

    # Connection markers for procedural generation
    - nodeId: "10000000-0000-0000-0000-000000000010"
      refId: "connection_north"
      name: "North Connection"
      nodeType: marker
      localTransform:
        position: { x: 0, y: 0, z: 5 }
        rotation: { x: 0, y: 0, z: 0, w: 1 }
        scale: { x: 1, y: 1, z: 1 }
      tags: ["connection", "door"]
      annotations:
        connection:
          direction: "north"
          width: 2.0
      children: []

    - nodeId: "10000000-0000-0000-0000-000000000011"
      refId: "connection_south"
      name: "South Connection"
      nodeType: marker
      localTransform:
        position: { x: 0, y: 0, z: -5 }
        rotation: { x: 0, y: 1, z: 0, w: 0 }
        scale: { x: 1, y: 1, z: 1 }
      tags: ["connection", "door"]
      annotations:
        connection:
          direction: "south"
          width: 2.0
      children: []

    # Treasure chest
    - nodeId: "10000000-0000-0000-0000-000000000020"
      refId: "chest"
      name: "Treasure Chest"
      nodeType: mesh
      localTransform:
        position: { x: 0, y: 0, z: 0 }
        rotation: { x: 0, y: 0, z: 0, w: 1 }
        scale: { x: 1, y: 1, z: 1 }
      asset:
        bundleId: "550e8400-e29b-41d4-a716-446655440500"
        assetId: "550e8400-e29b-41d4-a716-446655440502"
      tags: ["prop", "interactive"]
      annotations:
        interaction:
          type: "container"
      children: []

    # Guardian position
    - nodeId: "10000000-0000-0000-0000-000000000030"
      refId: "guardian_position"
      name: "Guardian Position"
      nodeType: marker
      localTransform:
        position: { x: -3, y: 0, z: 0 }
        rotation: { x: 0, y: 0.707, z: 0, w: 0.707 }
        scale: { x: 1, y: 1, z: 1 }
      tags: ["position", "npc", "hostile"]
      children: []
```

### Village with Scene References

```yaml
$schema: "bannou://schemas/scene/v1"
sceneId: "550e8400-e29b-41d4-a716-446655440003"
gameId: "arcadia-online"
sceneType: city
name: "Village Square"
version: "1.0.0"
tags: ["exterior", "village", "social"]

root:
  nodeId: "20000000-0000-0000-0000-000000000001"
  refId: "village_root"
  name: "Village Square"
  nodeType: group
  localTransform:
    position: { x: 0, y: 0, z: 0 }
    rotation: { x: 0, y: 0, z: 0, w: 1 }
    scale: { x: 1, y: 1, z: 1 }
  children:

    # Ground
    - nodeId: "20000000-0000-0000-0000-000000000002"
      refId: "ground"
      name: "Village Ground"
      nodeType: mesh
      localTransform:
        position: { x: 0, y: 0, z: 0 }
        rotation: { x: 0, y: 0, z: 0, w: 1 }
        scale: { x: 1, y: 1, z: 1 }
      asset:
        assetId: "550e8400-e29b-41d4-a716-446655440601"
      tags: ["terrain"]
      children: []

    # Reference to tavern (not embedded, just referenced)
    - nodeId: "20000000-0000-0000-0000-000000000010"
      refId: "tavern_ref"
      name: "The Cozy Tavern"
      nodeType: reference
      localTransform:
        position: { x: 15, y: 0, z: 10 }
        rotation: { x: 0, y: 0.383, z: 0, w: 0.924 }
        scale: { x: 1, y: 1, z: 1 }
      tags: ["building", "tavern"]
      annotations:
        reference:
          sceneAssetId: "550e8400-e29b-41d4-a716-446655440001"
          version: "1.2.0"
      children: []

    # Reference to blacksmith
    - nodeId: "20000000-0000-0000-0000-000000000011"
      refId: "blacksmith_ref"
      name: "The Forge"
      nodeType: reference
      localTransform:
        position: { x: -20, y: 0, z: 5 }
        rotation: { x: 0, y: -0.707, z: 0, w: 0.707 }
        scale: { x: 0.9, y: 0.9, z: 0.9 }
      tags: ["building", "shop"]
      annotations:
        reference:
          sceneAssetId: "550e8400-e29b-41d4-a716-446655440004"
      children: []

    # Village well (direct mesh)
    - nodeId: "20000000-0000-0000-0000-000000000020"
      refId: "well"
      name: "Village Well"
      nodeType: mesh
      localTransform:
        position: { x: 0, y: 0, z: 0 }
        rotation: { x: 0, y: 0, z: 0, w: 1 }
        scale: { x: 1, y: 1, z: 1 }
      asset:
        assetId: "550e8400-e29b-41d4-a716-446655440602"
      tags: ["prop", "landmark"]
      children: []
```

---

## 8. Implementation Roadmap

### Phase 1: Core Schema and Storage

**Goal**: Scene documents can be created, stored, and retrieved.

**Deliverables**:
- Schema files: `scene-api.yaml`, `scene-events.yaml`, `scene-configuration.yaml`
- API: create, get, list, update, delete, validate
- Storage: Scene documents in lib-asset, index in lib-state
- Events: `scene.created`, `scene.updated`, `scene.deleted` (via `x-lifecycle`)

### Phase 2: Instantiation and Events

**Goal**: Game servers can declare scene instantiations.

**Deliverables**:
- API: instantiate, destroy-instance, list-instances
- Events: `scene.instantiated`, `scene.destroyed`
- Instance tracking in lib-state (minimal - for debugging)

### Phase 3: Versioning and Checkout

**Goal**: Multiple editors can work on scenes with conflict prevention.

**Deliverables**:
- API: checkout, commit, discard, history, get-version
- Locking via `IDistributedLockProvider` with TTL and heartbeat
- Events: `scene.checked_out`, `scene.committed`, `scene.lock_expired`

### Phase 4: Validation Framework

**Goal**: Per-game, per-scene-type validation rules.

**Deliverables**:
- API: register-validation-rules, get-validation-rules
- Structural validation (always): UUIDs, unique refIds, valid transforms, no cycles
- Game-specific validation (optional): Rules per `gameId` + `sceneType`

### Phase 5: Reference Resolution

**Goal**: Scenes can reference other scenes for composition.

**Deliverables**:
- `resolveReferences` option on get endpoint
- Cycle detection during validation
- Reference tracking for "find usages" queries

### Phase 6: Editor Integration

**Goal**: Design tools can efficiently work with scenes.

**Deliverables**:
- API: search, find-references, find-asset-usage, duplicate
- Client events for real-time collaboration

---

## 9. Storage Model

### lib-asset

Scene documents stored as YAML assets with:
- Content type: `application/x-bannou-scene+yaml`
- Versioning via lib-asset's standard versioning
- Compression for large scenes

### lib-state

Index for efficient queries:
- Key pattern: `scene:index:{gameId}:{sceneType}:{sceneId}`
- Secondary indices: by name, by tags
- Instance tracking: `scene:instance:{instanceId}`

### Validation Rules

Stored per partition:
- Key pattern: `scene:validation:{gameId}:{sceneType}`

---

## 10. What Scene Service Validates

| Validation | Enforced |
|------------|----------|
| `nodeId` is valid UUID | Yes |
| `refId` is unique within scene | Yes |
| `refId` matches pattern `^[a-z][a-z0-9_]*$` | Yes |
| `parentNodeId` references existing node | Yes |
| `localTransform` has valid components | Yes |
| `version` matches semver pattern | Yes |
| No circular parent references | Yes |
| `gameId` is valid | No (opaque string) |
| `sceneType` is valid enum | Yes |
| `annotations` content | No (any JSON) |
| `tags` values | No (any strings) |
| `asset` references exist | No (consumer's job) |
| Game-specific rules | Only if registered |

---

## 11. Consumer Responsibilities

Scene Service publishes events. Consumers decide what to do:

| Consumer | Responsibility |
|----------|----------------|
| Game Server | Load scene, instantiate geometry, call `/scene/instantiate` |
| Mapping Service | Listen to `scene.instantiated`, fetch scene, extract spatial objects |
| Actor Service | Listen to `scene.instantiated`, find spawn markers, spawn NPCs |
| Affordance Calculator | Listen to `map.updated`, analyze geometry, compute affordances |

**Transform Application**: Consumers MUST apply `worldTransform` (including scale and rotation) when computing actual node positions. Scene Service stores local transforms only.

**Affordances**: Computed from actual world-space geometry, not scene definitions. A table on the ceiling isn't cover.

---

## 12. Summary

The Scene Service is a **hierarchical composition storage system**:

1. **Partitioned**: All scenes have `gameId` and `sceneType`
2. **Structural validation**: UUIDs, transforms, hierarchy integrity
3. **Optional game-specific validation**: Rules per partition
4. **Event-driven**: Publishes lifecycle and operational events
5. **Annotation passthrough**: Stores consumer data without interpretation

**The Scene Service defines what exists. Consumers define what it means.**
