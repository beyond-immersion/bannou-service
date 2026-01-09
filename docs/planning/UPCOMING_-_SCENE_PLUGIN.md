# Scene Plugin Planning Document

> **Status**: Planning Phase
> **Created**: 2025-01-09
> **Author**: Design collaboration with Claude
> **Target**: lib-scene plugin for hierarchical scene composition

---

## Table of Contents

1. [Executive Summary](#executive-summary)
2. [Problem Statement](#problem-statement)
3. [Analysis of Existing Systems](#analysis-of-existing-systems)
4. [Why a Separate Scene Service](#why-a-separate-scene-service)
5. [Core Design Decisions](#core-design-decisions)
6. [Data Model Specification](#data-model-specification)
7. [API Design](#api-design)
8. [Event System](#event-system)
9. [Integration Strategies](#integration-strategies)
10. [Use Cases and Examples](#use-cases-and-examples)
11. [Future Considerations](#future-considerations)
12. [Implementation Roadmap](#implementation-roadmap)

---

## Executive Summary

The Scene Plugin (lib-scene) provides hierarchical composition management for game environments, buildings, interiors, and other spatial structures. Unlike the Mapping Service which handles flat spatial objects with world coordinates, the Scene Service manages **transform hierarchies** where child nodes have positions relative to their parents.

### Key Characteristics

- **Hierarchical composition**: Unlimited nesting of nodes with parent-child relationships
- **Relative transforms**: Position, rotation, and scale relative to parent node
- **Asset integration**: First-class references to lib-asset bundles, assets, and variants
- **Stateless architecture**: Scene content stored entirely in lib-asset; service is a computation/validation layer
- **Event-driven instantiation**: Scenes instantiate via events, consumers (mapping, actors, game servers) react independently
- **Single-author editing**: Checkout/commit model prevents conflicts without real-time collaboration complexity

### What This Enables

1. **Building/interior design**: Taverns, dungeons, houses with nested rooms and furniture
2. **Prefab systems**: Reusable scene templates for procedural generation
3. **Editor integration**: Scene editors with proper transform hierarchy support
4. **Cross-service coordination**: Single instantiation event triggers mapping, actors, and game servers
5. **Procedural generation foundation**: Generators create SceneNode hierarchies programmatically

---

## Problem Statement

### The Gap in Current Architecture

Bannou currently excels at:
- **Spatial awareness** (Mapping Service): "What objects are near location X?"
- **Asset storage** (Asset Service): "Store and retrieve versioned binary assets"
- **Location hierarchy** (Location Service): "What regions contain what sub-regions?"
- **Actor behaviors** (Actor/Behavior Services): "How should NPCs behave?"

However, there is no service for:
- **Composition management**: "What objects make up this building?"
- **Transform hierarchies**: "Where is this chair relative to this room?"
- **Scene instantiation**: "Spawn this entire tavern at world position (100, 0, 200)"

### Concrete Use Cases Requiring This Service

1. **Player Housing**: Players design homes with rooms, furniture, decorations. Each piece has a position relative to the room, not the world.

2. **Dungeon Design**: Level designers create dungeon rooms as scenes. Rooms are instantiated and connected procedurally.

3. **NPC Environments**: A tavern scene includes bartender spawn points, ambient sounds, entrance markers. Instantiating the tavern spawns all associated content.

4. **Procedural Buildings**: A "building generator" service creates SceneNode hierarchies based on rules, then instantiates them.

5. **Cutscene Stages**: Dramatic scenes require camera positions, actor marks, lighting setups - all relative to a scene origin.

### Why Mapping Cannot Solve This

The Mapping Service was deliberately designed as a "bag of spatial data" with:
- Flat object structure (no parent-child relationships)
- World-space coordinates only (no relative positioning)
- Schema-less data payloads (no enforced structure)
- High-throughput streaming focus (100+ updates per event)
- Channel-based authority (per region+kind, not per composition)

Attempting to add hierarchical composition to Mapping would:
- Break its flat data model assumptions
- Complicate its high-performance streaming path
- Confuse its authority model
- Violate separation of concerns

---

## Analysis of Existing Systems

### Mapping Service Deep Dive

The Mapping Service (`lib-mapping`) is Bannou's spatial intelligence backbone. Understanding its design philosophy explains why scene composition requires a separate service.

#### Core Concepts

**Channels**: The atomic unit of authority
- `Channel = RegionId + MapKind`
- One authority publisher per channel
- Subscription unit for consumers

**Map Kinds**: 12 differentiated data categories
```
Static (rarely change):     terrain, static_geometry, navigation
Semi-static (edit changes): resources, spawn_points, points_of_interest
Dynamic (runtime):          dynamic_objects, hazards, weather_effects, ownership
Ephemeral (short-lived):    combat_effects (30s TTL), visual_effects (60s TTL)
```

**Map Objects**: Schema-less containers
```yaml
MapObject:
  objectId: uuid
  regionId: uuid
  kind: MapKind
  objectType: string        # Publisher-defined (e.g., "building", "npc")
  position: Position3D      # Optional, for point objects
  bounds: Bounds            # Optional, for area objects
  data: object              # additionalProperties: true - anything
  version: int64
```

#### What Mapping Does Well

- **Spatial queries**: Point queries, bounds queries, type queries
- **Affordance system**: "Where can I ambush?" with generators, tests, scoring
- **High-throughput streaming**: Two publishing paths (RPC and event ingest)
- **Authority management**: Token-based exclusive publishing rights
- **TTL-based cleanup**: Ephemeral data auto-expires
- **Snapshot/subscribe pattern**: Cold-start synchronization

#### What Mapping Cannot Do

- **No object containment**: Objects cannot contain other objects
- **No nesting**: All objects in a region/kind are at the same level
- **No relative positioning**: All coordinates are world-space
- **No atomic multi-object operations**: Each object published individually
- **No scene lifecycle**: No "scene created" or "scene destroyed" events
- **No reusable templates**: MapDefinition is region-level only
- **No rotation/scale in schema**: Must be stored in `data` field

#### Mapping's Design Philosophy

Mapping prioritizes:
- **Simplicity**: One object, one set of coordinates
- **Flexibility**: Any game can define payloads via `data` field
- **Performance**: Simple pub/sub, no transaction overhead
- **Authority clarity**: One authority per channel

At the cost of:
- No built-in grouping
- No composition support
- No hierarchy within region/kind

This is an **intentional trade-off**, not an oversight.

### Asset Service Deep Dive

The Asset Service (`lib-asset`) provides content-addressed storage with versioning. Scene Service will store scene definitions as assets.

#### Core Architecture

**Pre-signed URL pattern**: All binary transfers bypass WebSocket
- Client uploads directly to MinIO/S3
- Client downloads directly from pre-signed URLs
- Service handles metadata and orchestration only

**Content-addressed IDs**: `{type-prefix}-{hash-prefix}`
- Deterministic: Same content = same ID
- Example: `model-abc123`, `texture-xyz789`

**Processing pipeline**:
```
Upload Request → Pre-signed URL → Direct Upload → Complete Notification
                                                         ↓
                                              Processing (texture/model/audio)
                                                         ↓
                                              Asset Ready Event
```

#### Bundle System

The `.bannou` bundle format groups related assets:
```
manifest.json    # Metadata for all contained assets
index.bin        # 48-byte entries (ID hash, offset, sizes, flags)
[LZ4 compressed assets...]
```

Bundle creation:
```yaml
CreateBundleRequest:
  bundleId: uuid
  assetIds: uuid[]      # Assets to include
  version: string       # Semantic version
  compression: lz4|lzma|none
  metadata: object
```

#### How Other Services Reference Assets

The `BehaviorDocumentCache` pattern shows the standard approach:
```csharp
// 1. Extract asset ID from URI (e.g., "asset://behaviors/npc-brain-v1")
var assetId = ExtractAssetId(behaviorRef);

// 2. Fetch metadata + download URL from asset service
var assetInfo = await assetClient.GetAssetAsync(
    new GetAssetRequest { AssetId = assetId }, ct);

// 3. Download content from pre-signed URL (direct HTTP, not through service)
var content = await httpClient.GetStringAsync(assetInfo.DownloadUrl, ct);

// 4. Parse and cache
var document = parser.Parse(content);
cache[behaviorRef] = document;
```

Scene Service will follow this same pattern for loading scene definitions.

### Existing Hierarchical Patterns

Bannou already implements several hierarchical patterns in other services.

#### Location Service (Tree Hierarchy)

The Location Service manages a proper tree structure:
```yaml
LocationResponse:
  locationId: uuid
  realmId: uuid
  parentLocationId: uuid|null   # Tree parent
  depth: integer                # Level in tree (0 = root)
  locationType: enum            # CONTINENT, REGION, CITY, BUILDING, ROOM, etc.
  code: string                  # Unique within realm
  metadata: object
```

Key characteristics:
- Unlimited depth via `parentLocationId`
- Explicit `depth` field for efficient queries
- Soft deletion via `isDeprecated` flag
- Query by realm + optional parent filter

**Relevance to Scene Service**: Location Service handles world geography (realms, regions, cities). Scene Service handles **interior composition** (rooms within buildings, furniture within rooms). A scene might be **placed at** a location, but the scene's internal hierarchy is separate from the location hierarchy.

#### Relationship Service (Polymorphic Graph)

The Relationship Service connects entities of different types:
```yaml
RelationshipResponse:
  entity1Id: uuid
  entity1Type: EntityType       # CHARACTER, NPC, ITEM, LOCATION, etc.
  entity2Id: uuid
  entity2Type: EntityType
  relationshipTypeId: uuid
  metadata: object
```

Key characteristics:
- Entity-agnostic via discriminator enum
- Bidirectional queryable
- Type-specific metadata
- Composite uniqueness key

**Relevance to Scene Service**: Scene nodes could theoretically be modeled as entities with parent-child relationships. However, this would be awkward because:
- Transforms don't fit the relationship model well
- Scene hierarchy is self-contained, not cross-entity
- Performance would suffer from relationship queries vs. document loading

#### Actor Service (Template-Instance Pattern)

The Actor Service uses templates for NPC creation:
```yaml
ActorTemplate:
  templateId: uuid
  category: string              # "npc-brain", "world-admin"
  behaviorRef: string           # "asset://behaviors/npc-brain-v1"
  autoSpawn: AutoSpawnConfig
  configuration: object
```

Key characteristics:
- Template defines defaults
- Instance inherits from template
- Override configuration at instance level
- Pattern-based auto-spawning

**Relevance to Scene Service**: Scenes are similar to templates. A scene definition (template) can be instantiated multiple times in the world with different positions and property overrides.

---

## Why a Separate Scene Service

### Fundamental Differences from Mapping

| Concern | Mapping Service | Scene Service |
|---------|-----------------|---------------|
| **Primary question** | "What's at location X?" | "What composes structure Y?" |
| **Data structure** | Flat collection | Tree hierarchy |
| **Coordinates** | World-space absolute | Parent-relative local |
| **Schema** | Schema-less (`data: object`) | Schema-enforced (node types) |
| **Storage** | Redis state store | lib-asset (versioned files) |
| **Authority model** | Per-channel (regionId+kind) | Per-scene (checkout/commit) |
| **Update frequency** | High-throughput streaming | Occasional commits |
| **Primary consumers** | Game servers (runtime) | Editors, generators (design-time) |
| **Lifecycle events** | Object changes (continuous) | Instantiation (discrete) |

### Why Not Extend Mapping?

Adding scene composition to Mapping would require:

1. **Parent-child relationships**: New `parentObjectId` field, breaking flat assumption
2. **Relative coordinates**: Parallel coordinate systems, complicating all queries
3. **Transform computation**: Hierarchy walking for world positions
4. **Atomic operations**: Transaction support for multi-object creates
5. **Checkout/commit**: New authority model alongside channel authority
6. **Versioning**: Asset-style versioning alongside streaming updates

This would effectively create two services in one codebase with different:
- Data models
- Authority models
- Update patterns
- Query patterns
- Storage backends

**Cleaner to separate them** and have scene instantiation publish to mapping.

### The Complementary Relationship

```
Scene Service                          Mapping Service
(Design-time composition)              (Runtime spatial awareness)
         │                                      │
         │  scene.instantiated event           │
         │  with pre-computed world transforms │
         ├─────────────────────────────────────►
         │                                      │
         │                                      ▼
         │                             Flat map objects
         │                             with world coords
         │                                      │
         │                                      │
    ┌────┴────┐                           ┌────┴────┐
    │ Editors │                           │ Game    │
    │ Generators│                         │ Servers │
    └─────────┘                           └─────────┘
```

**Scene Service owns composition**. When instantiated:
1. Computes world transforms for all nodes
2. Publishes `scene.instantiated` event with flattened data
3. Mapping Service receives flat objects with absolute coordinates
4. Other consumers (actors, game servers) also react to the event

---

## Core Design Decisions

Based on analysis and discussion, these decisions guide the implementation:

### Decision 1: Storage Architecture

**Choice**: lib-asset only (scene service is stateless)

**Rationale**:
- Scenes are versioned documents, like behaviors or other content
- lib-asset already provides versioning, bundling, CDN distribution
- No need for separate Redis state store
- Scene service becomes a computation/validation layer over lib-asset
- Simplifies deployment (no state to manage)

**Implementation**:
- Scenes stored as YAML or JSON files in lib-asset
- Scene service validates structure on load
- Scene service computes transforms on request
- All queries ultimately fetch from lib-asset

### Decision 2: Editing Model

**Choice**: Single author with checkout/commit (like mapping authoring)

**Rationale**:
- Real-time collaboration requires OT/CRDT, massive complexity
- Single author is simpler, covers 90% of use cases
- Checkout/commit pattern already proven in mapping authoring
- Conflict resolution is trivial (lock prevents conflicts)

**Implementation**:
- `checkout`: Acquire exclusive edit lock, get current scene
- `commit`: Save changes, release lock, create new version
- `release`: Abandon changes, release lock
- Configurable lock timeout (default: 30 minutes)

### Decision 3: Instantiation Pattern

**Choice**: Event-driven (consumers react independently)

**Rationale**:
- Decouples scene service from consumers
- Mapping, actors, game servers evolve independently
- New consumers can subscribe without scene service changes
- Follows Bannou's event-driven architecture

**Implementation**:
- `POST /scene/instantiate` triggers `scene.instantiated` event
- Event contains pre-computed world transforms
- Mapping service publishes spatial nodes as map objects
- Actor service spawns NPCs at spawn points
- Game servers load models, sounds, etc.

### Decision 4: Rotation Representation

**Choice**: Both quaternion (internal) and Euler angles (editing)

**Rationale**:
- Quaternion: Mathematically robust, no gimbal lock, correct for interpolation
- Euler angles: Human-readable, intuitive for manual editing
- Editors display Euler, convert to quaternion on save
- Runtime uses quaternion for all computations

**Implementation**:
```yaml
Transform:
  position: Vector3
  rotation: Quaternion          # Canonical representation
  rotationEuler: EulerAngles    # Computed for editing convenience
  scale: Vector3
```

Conversion utilities provided by scene service.

---

## Data Model Specification

### Core Types

#### Vector3

Three-dimensional vector for position and scale.

```yaml
Vector3:
  type: object
  required: [x, y, z]
  properties:
    x:
      type: number
      format: double
      description: X-axis component (typically East-West)
    y:
      type: number
      format: double
      description: Y-axis component (typically Up-Down / elevation)
    z:
      type: number
      format: double
      description: Z-axis component (typically North-South)
```

#### Quaternion

Four-dimensional rotation representation. Preferred for internal storage and computation.

```yaml
Quaternion:
  type: object
  required: [x, y, z, w]
  properties:
    x:
      type: number
      format: double
      description: X component of quaternion
    y:
      type: number
      format: double
      description: Y component of quaternion
    z:
      type: number
      format: double
      description: Z component of quaternion
    w:
      type: number
      format: double
      description: W component (scalar) of quaternion
  description: |
    Unit quaternion representing rotation. Must be normalized (x^2 + y^2 + z^2 + w^2 = 1).
    Identity rotation: (0, 0, 0, 1).
```

#### EulerAngles

Human-readable rotation representation. Used for editing interfaces.

```yaml
EulerAngles:
  type: object
  required: [pitch, yaw, roll]
  properties:
    pitch:
      type: number
      format: double
      description: Rotation around X-axis in degrees (-180 to 180)
    yaw:
      type: number
      format: double
      description: Rotation around Y-axis in degrees (-180 to 180)
    roll:
      type: number
      format: double
      description: Rotation around Z-axis in degrees (-180 to 180)
  description: |
    Euler angle rotation in degrees. Applied in YXZ order (yaw, then pitch, then roll).
    Warning: Subject to gimbal lock at extreme pitch values.
```

#### Transform

Complete local transform relative to parent node.

```yaml
Transform:
  type: object
  required: [position, rotation, scale]
  properties:
    position:
      $ref: '#/components/schemas/Vector3'
      description: Position relative to parent node origin
    rotation:
      $ref: '#/components/schemas/Quaternion'
      description: Rotation relative to parent node orientation (canonical)
    rotationEuler:
      $ref: '#/components/schemas/EulerAngles'
      description: |
        Euler angle representation of rotation. Computed from quaternion.
        Provided for editing convenience; quaternion is authoritative.
    scale:
      $ref: '#/components/schemas/Vector3'
      description: Scale relative to parent node scale
  description: |
    Local transform relative to parent. For root nodes, this is relative to scene origin.
    World transform computed by multiplying parent chain.
```

#### AssetReference

Reference to an asset in lib-asset, with optional bundle and variant.

```yaml
AssetReference:
  type: object
  required: [assetId]
  properties:
    bundleId:
      type: string
      format: uuid
      nullable: true
      description: |
        Optional bundle containing the asset. If specified, asset is loaded from bundle.
        If null, asset is loaded standalone from lib-asset.
    assetId:
      type: string
      format: uuid
      description: Primary asset identifier in lib-asset
    variantId:
      type: string
      nullable: true
      description: |
        Variant identifier within the asset. Interpretation depends on asset type:
        - Model: mesh variant ("damaged", "pristine")
        - Character: appearance variant ("male_elf", "female_dwarf")
        - Material: texture variant ("summer", "winter")
        Client/game engine resolves variant at load time.
  description: |
    Reference to an asset in lib-asset. The scene stores the reference;
    actual asset loading happens in clients/game servers.
```

### Node Types

The `NodeType` enum defines all supported node categories. Each type has specific properties.

```yaml
NodeType:
  type: string
  enum:
    - model
    - marker
    - camera
    - light
    - sound
    - trigger
    - spawn_point
    - waypoint
    - group
    - bounds
    - particle_system
    - decal
    - reflection_probe
    - custom
  description: |
    Discriminator for scene node types. Each type has specific properties
    and behaviors at runtime.
```

#### Node Type Descriptions

**model**: 3D mesh with optional materials
- Primary use: Visual geometry (buildings, furniture, props)
- Requires asset reference
- Properties: mesh path, material overrides, shadow settings, LOD

**marker**: Invisible reference point
- Primary use: Named positions for scripting, camera targets, interest points
- No visual representation
- Properties: marker type, tags, optional editor gizmo

**camera**: Camera rig for cinematics or gameplay
- Primary use: Cutscene cameras, security cameras, preview cameras
- Properties: projection, FOV, clipping planes, look-at target (RefID)

**light**: Light source
- Primary use: Scene illumination
- Properties: light type, color, intensity, range, shadows

**sound**: Audio emitter
- Primary use: Ambient sounds, music zones, sound effects
- Requires asset reference (audio asset)
- Properties: volume, spatialization, distance falloff, looping

**trigger**: Collision trigger volume
- Primary use: Enter/exit detection, interaction zones
- Properties: shape, size, trigger vs collider, collision layer

**spawn_point**: Entity spawn location
- Primary use: NPC spawns, player spawns, item spawns
- Properties: spawn type, tags, max occupancy, cooldown

**waypoint**: Navigation/patrol point
- Primary use: NPC patrol routes, quest markers
- Properties: waypoint type, connections (RefIDs), wait time

**group**: Container node with no visual
- Primary use: Organizing related nodes, logical grouping
- No properties beyond transform
- Children inherit group transform

**bounds**: Bounding volume for spatial queries
- Primary use: Room bounds, zone definitions, culling hints
- Properties: shape (box, sphere), visualization settings

**particle_system**: VFX emitter
- Primary use: Environmental effects (dust, smoke, sparkles)
- Requires asset reference (particle definition)
- Properties: emission rate, lifetime, simulation space

**decal**: Projected texture
- Primary use: Ground details, blood splatter, damage marks
- Requires asset reference (decal material)
- Properties: projection size, fade distance

**reflection_probe**: Environment reflection capture
- Primary use: Reflective surfaces, PBR rendering
- Properties: capture mode, resolution, bounds

**custom**: Extension point for game-specific nodes
- Primary use: Types not covered by standard set
- Properties: customType string, arbitrary properties object

### SceneNode

The fundamental hierarchical unit.

```yaml
SceneNode:
  type: object
  required: [nodeId, refId, name, nodeType, transform, children]
  properties:
    nodeId:
      type: string
      format: uuid
      description: Globally unique identifier for this node instance
    refId:
      type: string
      pattern: '^[a-z][a-z0-9_]*$'
      description: |
        Scene-local reference identifier. Used for cross-references within scene.
        Must be unique within scene. Lowercase with underscores only.
        Examples: "main_entrance", "bartender_spawn", "ambient_light_1"
    parentNodeId:
      type: string
      format: uuid
      nullable: true
      description: Parent node ID. Null for root node.
    name:
      type: string
      description: Human-readable display name for editors
    nodeType:
      $ref: '#/components/schemas/NodeType'
      description: Node type discriminator
    transform:
      $ref: '#/components/schemas/Transform'
      description: Local transform relative to parent
    asset:
      $ref: '#/components/schemas/AssetReference'
      nullable: true
      description: Optional asset binding. Required for model, sound, particle_system, decal.
    properties:
      type: object
      additionalProperties: true
      description: |
        Type-specific properties. Schema depends on nodeType.
        See NodeType-specific property schemas.
    children:
      type: array
      items:
        $ref: '#/components/schemas/SceneNode'
      description: Child nodes. Empty array for leaf nodes.
    enabled:
      type: boolean
      default: true
      description: Whether node is active. Disabled nodes excluded from instantiation.
    sortOrder:
      type: integer
      default: 0
      description: Sibling ordering for deterministic iteration
    tags:
      type: array
      items:
        type: string
      description: Searchable tags for filtering and queries
```

### Type-Specific Properties

Each node type has a defined properties schema. These are stored in the `properties` field.

#### ModelProperties

```yaml
ModelProperties:
  type: object
  properties:
    meshPath:
      type: string
      description: Path to mesh within asset bundle (e.g., "meshes/table.fbx")
    materialOverrides:
      type: array
      items:
        $ref: '#/components/schemas/MaterialOverride'
      description: Material slot overrides
    castShadows:
      type: boolean
      default: true
      description: Whether model casts shadows
    receiveShadows:
      type: boolean
      default: true
      description: Whether model receives shadows
    lodBias:
      type: number
      format: double
      default: 1.0
      description: LOD selection bias (higher = prefer higher detail)
    lightmapScale:
      type: number
      format: double
      default: 1.0
      description: Lightmap resolution scale
    layer:
      type: string
      description: Rendering layer name

MaterialOverride:
  type: object
  properties:
    slotIndex:
      type: integer
      description: Material slot index to override
    materialAssetId:
      type: string
      format: uuid
      description: Replacement material asset ID
```

#### MarkerProperties

```yaml
MarkerProperties:
  type: object
  properties:
    markerType:
      type: string
      description: |
        Semantic marker type. Common values:
        - "entrance": Entry point to area
        - "exit": Exit point from area
        - "interest_point": Point of interest for AI/quests
        - "camera_target": Target for camera look-at
        - "spawn_anchor": Anchor point for spawn calculations
        - "measurement": Distance/size reference point
    visualGizmo:
      type: string
      nullable: true
      description: Editor gizmo type for visualization (arrow, sphere, icon)
    gizmoColor:
      type: string
      pattern: '^#[0-9A-Fa-f]{6}$'
      description: Hex color for editor gizmo
    gizmoScale:
      type: number
      format: double
      default: 1.0
      description: Scale factor for editor gizmo
```

#### CameraProperties

```yaml
CameraProperties:
  type: object
  properties:
    projection:
      type: string
      enum: [perspective, orthographic]
      default: perspective
      description: Camera projection type
    fieldOfView:
      type: number
      format: double
      default: 60.0
      description: Vertical field of view in degrees (perspective only)
    orthographicSize:
      type: number
      format: double
      description: Half-height of view in units (orthographic only)
    nearClip:
      type: number
      format: double
      default: 0.1
      description: Near clipping plane distance
    farClip:
      type: number
      format: double
      default: 1000.0
      description: Far clipping plane distance
    aspectRatio:
      type: number
      format: double
      nullable: true
      description: Fixed aspect ratio. Null = inherit from viewport.
    lookAtRef:
      type: string
      nullable: true
      description: RefID of target node for look-at constraint
    depth:
      type: integer
      default: 0
      description: Rendering order (higher = rendered later)
```

#### LightProperties

```yaml
LightProperties:
  type: object
  properties:
    lightType:
      type: string
      enum: [directional, point, spot, area]
      description: Light source type
    color:
      $ref: '#/components/schemas/Color'
      description: Light color
    intensity:
      type: number
      format: double
      default: 1.0
      description: Light intensity multiplier
    range:
      type: number
      format: double
      description: Attenuation range for point/spot lights
    spotAngle:
      type: number
      format: double
      description: Outer cone angle in degrees (spot lights only)
    innerSpotAngle:
      type: number
      format: double
      description: Inner cone angle in degrees (spot lights only)
    shadowType:
      type: string
      enum: [none, hard, soft]
      default: none
      description: Shadow casting mode
    shadowStrength:
      type: number
      format: double
      default: 1.0
      description: Shadow darkness (0 = transparent, 1 = opaque)
    shadowResolution:
      type: string
      enum: [low, medium, high, very_high]
      default: medium
      description: Shadow map resolution
    cookieAssetId:
      type: string
      format: uuid
      nullable: true
      description: Light cookie texture asset ID

Color:
  type: object
  required: [r, g, b]
  properties:
    r:
      type: number
      format: double
      minimum: 0
      maximum: 1
      description: Red component (0-1)
    g:
      type: number
      format: double
      minimum: 0
      maximum: 1
      description: Green component (0-1)
    b:
      type: number
      format: double
      minimum: 0
      maximum: 1
      description: Blue component (0-1)
    a:
      type: number
      format: double
      minimum: 0
      maximum: 1
      default: 1.0
      description: Alpha component (0-1)
```

#### SoundProperties

```yaml
SoundProperties:
  type: object
  properties:
    volume:
      type: number
      format: double
      minimum: 0
      maximum: 1
      default: 1.0
      description: Playback volume (0-1)
    pitch:
      type: number
      format: double
      minimum: 0.1
      maximum: 3.0
      default: 1.0
      description: Playback pitch multiplier
    spatialBlend:
      type: number
      format: double
      minimum: 0
      maximum: 1
      default: 1.0
      description: 2D/3D blend (0 = 2D, 1 = fully 3D spatialized)
    minDistance:
      type: number
      format: double
      default: 1.0
      description: Distance at which volume starts attenuating
    maxDistance:
      type: number
      format: double
      default: 50.0
      description: Distance at which volume reaches zero
    rolloffMode:
      type: string
      enum: [linear, logarithmic, custom]
      default: logarithmic
      description: Volume attenuation curve
    loop:
      type: boolean
      default: false
      description: Whether sound loops
    playOnAwake:
      type: boolean
      default: true
      description: Whether sound plays immediately on instantiation
    priority:
      type: integer
      minimum: 0
      maximum: 255
      default: 128
      description: Audio source priority (lower = more important)
```

#### TriggerProperties

```yaml
TriggerProperties:
  type: object
  properties:
    shape:
      type: string
      enum: [box, sphere, capsule]
      default: box
      description: Trigger collider shape
    size:
      $ref: '#/components/schemas/Vector3'
      description: Size for box shape (width, height, depth)
    radius:
      type: number
      format: double
      description: Radius for sphere/capsule shapes
    height:
      type: number
      format: double
      description: Height for capsule shape
    isTrigger:
      type: boolean
      default: true
      description: True = trigger (non-blocking), False = collider (blocking)
    layer:
      type: string
      description: Collision layer name
    interactionTags:
      type: array
      items:
        type: string
      description: Tags that can interact with this trigger
    enterEvent:
      type: string
      nullable: true
      description: Event topic to publish on enter
    exitEvent:
      type: string
      nullable: true
      description: Event topic to publish on exit
```

#### SpawnPointProperties

```yaml
SpawnPointProperties:
  type: object
  properties:
    spawnType:
      type: string
      description: |
        Entity type to spawn. Common values:
        - "player": Player spawn point
        - "npc": Generic NPC spawn
        - "monster": Enemy spawn
        - "item": Item/loot spawn
        - "vehicle": Vehicle spawn
    spawnTags:
      type: array
      items:
        type: string
      description: |
        Filtering tags for spawn selection. Examples:
        ["bartender", "merchant"] - spawn bartenders or merchants
        ["guard", "elite"] - spawn elite guards
    maxOccupancy:
      type: integer
      minimum: 1
      default: 1
      description: Maximum entities that can spawn at this point
    cooldownSeconds:
      type: number
      format: double
      default: 0
      description: Minimum time between spawns
    spawnRadius:
      type: number
      format: double
      default: 0
      description: Random offset radius from point center
    facing:
      type: string
      enum: [fixed, random, toward_center, away_from_center]
      default: fixed
      description: How spawned entities should face
    facingTarget:
      type: string
      nullable: true
      description: RefID of node to face toward (if facing = toward_target)
```

#### WaypointProperties

```yaml
WaypointProperties:
  type: object
  properties:
    waypointType:
      type: string
      description: |
        Waypoint purpose. Common values:
        - "patrol": Patrol route waypoint
        - "guard_post": Stationary guard position
        - "quest": Quest objective marker
        - "navigation": General navigation hint
    connections:
      type: array
      items:
        type: string
      description: RefIDs of connected waypoints (for pathing)
    waitTimeSeconds:
      type: number
      format: double
      default: 0
      description: How long to pause at this waypoint
    arrivalRadius:
      type: number
      format: double
      default: 0.5
      description: Distance at which waypoint is considered "reached"
    priority:
      type: integer
      default: 0
      description: Selection priority when multiple paths available
```

### Scene Document

The complete scene structure stored in lib-asset.

```yaml
Scene:
  type: object
  required: [sceneId, name, version, realmId, root]
  properties:
    $schema:
      type: string
      const: "bannou://schemas/scene/v1"
      description: Schema identifier for validation
    sceneId:
      type: string
      format: uuid
      description: Unique scene identifier
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
    realmId:
      type: string
      description: Realm for asset scoping and permissions
    metadata:
      type: object
      additionalProperties: true
      properties:
        author:
          type: string
          description: Creator identifier
        tags:
          type: array
          items:
            type: string
          description: Searchable scene tags
        boundingBox:
          type: object
          properties:
            min:
              $ref: '#/components/schemas/Vector3'
            max:
              $ref: '#/components/schemas/Vector3'
          description: Pre-computed scene bounds in local space
        thumbnail:
          type: string
          format: uuid
          description: Thumbnail image asset ID
      description: Scene-level metadata
    root:
      $ref: '#/components/schemas/SceneNode'
      description: Root node of scene hierarchy (transform = identity)
    createdAt:
      type: string
      format: date-time
      description: Creation timestamp
    updatedAt:
      type: string
      format: date-time
      description: Last modification timestamp
```

### Flattened Node (For Instantiation)

When scenes are instantiated, hierarchy is flattened with world transforms.

```yaml
FlattenedNode:
  type: object
  required: [nodeId, refId, nodeType, worldTransform]
  properties:
    nodeId:
      type: string
      format: uuid
      description: Original node ID
    refId:
      type: string
      description: Original RefID
    parentNodeId:
      type: string
      format: uuid
      nullable: true
      description: Parent node ID (preserved for reference)
    name:
      type: string
      description: Node name
    nodeType:
      $ref: '#/components/schemas/NodeType'
    localTransform:
      $ref: '#/components/schemas/Transform'
      description: Original local transform (for reference)
    worldTransform:
      $ref: '#/components/schemas/Transform'
      description: Computed world-space transform
    worldPosition:
      $ref: '#/components/schemas/Vector3'
      description: Convenience field for world position
    asset:
      $ref: '#/components/schemas/AssetReference'
      nullable: true
    properties:
      type: object
      additionalProperties: true
    enabled:
      type: boolean
    tags:
      type: array
      items:
        type: string
```

---

## API Design

### Endpoint Overview

| Endpoint | Purpose | Notes |
|----------|---------|-------|
| `POST /scene/create` | Create new scene | Returns assetId |
| `POST /scene/get` | Retrieve scene by asset ID | Version optional |
| `POST /scene/get-by-name` | Retrieve scene by realm + name | Convenience |
| `POST /scene/checkout` | Acquire edit lock | Returns authority token |
| `POST /scene/commit` | Save changes, release lock | Creates new version |
| `POST /scene/release` | Abandon changes, release lock | No version created |
| `POST /scene/compute-world-transforms` | Flatten hierarchy | For preview/validation |
| `POST /scene/instantiate` | Spawn scene in world | Triggers events |
| `POST /scene/destroy` | Remove scene instance | Triggers events |
| `POST /scene/query-nodes` | Find nodes by criteria | Filter by type/tags/ref |
| `POST /scene/validate` | Validate scene structure | Pre-commit validation |

### Endpoint Specifications

#### Create Scene

```yaml
/scene/create:
  post:
    operationId: createScene
    x-permissions: [scene.create]
    requestBody:
      content:
        application/json:
          schema:
            type: object
            required: [name, realmId]
            properties:
              name:
                type: string
                description: Scene name (unique within realm)
              realmId:
                type: string
                description: Realm for scoping
              description:
                type: string
                nullable: true
              rootNode:
                $ref: '#/components/schemas/SceneNode'
                nullable: true
                description: Optional initial root node. If null, empty group created.
              metadata:
                type: object
                additionalProperties: true
    responses:
      '200':
        description: Scene created
        content:
          application/json:
            schema:
              type: object
              properties:
                assetId:
                  type: string
                  format: uuid
                  description: Asset ID for the new scene
                scene:
                  $ref: '#/components/schemas/Scene'
                version:
                  type: string
                  description: Initial version (1.0.0)
```

#### Get Scene

```yaml
/scene/get:
  post:
    operationId: getScene
    x-permissions: [scene.read]
    requestBody:
      content:
        application/json:
          schema:
            type: object
            required: [assetId]
            properties:
              assetId:
                type: string
                format: uuid
                description: Scene asset ID
              version:
                type: string
                nullable: true
                description: Specific version. Null = latest.
    responses:
      '200':
        description: Scene retrieved
        content:
          application/json:
            schema:
              type: object
              properties:
                scene:
                  $ref: '#/components/schemas/Scene'
                assetId:
                  type: string
                  format: uuid
                version:
                  type: string
```

#### Checkout Scene

```yaml
/scene/checkout:
  post:
    operationId: checkoutScene
    x-permissions: [scene.edit]
    requestBody:
      content:
        application/json:
          schema:
            type: object
            required: [assetId]
            properties:
              assetId:
                type: string
                format: uuid
                description: Scene asset ID to edit
    responses:
      '200':
        description: Scene checked out
        content:
          application/json:
            schema:
              type: object
              properties:
                authorityToken:
                  type: string
                  description: Opaque token for commit/release
                scene:
                  $ref: '#/components/schemas/Scene'
                lockedUntil:
                  type: string
                  format: date-time
                  description: Lock expiration timestamp
      '409':
        description: Scene already locked by another user
```

#### Commit Scene

```yaml
/scene/commit:
  post:
    operationId: commitScene
    x-permissions: [scene.edit]
    requestBody:
      content:
        application/json:
          schema:
            type: object
            required: [authorityToken, scene]
            properties:
              authorityToken:
                type: string
                description: Token from checkout
              scene:
                $ref: '#/components/schemas/Scene'
                description: Modified scene
              message:
                type: string
                description: Commit message (for version history)
              bumpType:
                type: string
                enum: [patch, minor, major]
                default: patch
                description: Version bump type
    responses:
      '200':
        description: Scene committed
        content:
          application/json:
            schema:
              type: object
              properties:
                assetId:
                  type: string
                  format: uuid
                previousVersion:
                  type: string
                newVersion:
                  type: string
      '401':
        description: Invalid or expired authority token
      '400':
        description: Validation errors in scene
```

#### Instantiate Scene

```yaml
/scene/instantiate:
  post:
    operationId: instantiateScene
    x-permissions: [scene.instantiate]
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
                description: Scene asset ID
              version:
                type: string
                nullable: true
                description: Specific version. Null = latest.
              instanceId:
                type: string
                format: uuid
                description: Caller-provided unique instance ID
              regionId:
                type: string
                format: uuid
                description: Target region for mapping objects
              worldTransform:
                $ref: '#/components/schemas/Transform'
                description: World-space transform for scene origin
              propertyOverrides:
                type: array
                items:
                  type: object
                  properties:
                    refId:
                      type: string
                      description: RefID of node to override
                    properties:
                      type: object
                      additionalProperties: true
                      description: Property overrides
                description: Per-node property overrides
    responses:
      '200':
        description: Scene instantiation triggered
        content:
          application/json:
            schema:
              type: object
              properties:
                instanceId:
                  type: string
                  format: uuid
                nodeCount:
                  type: integer
                  description: Number of nodes instantiated
```

#### Compute World Transforms

```yaml
/scene/compute-world-transforms:
  post:
    operationId: computeWorldTransforms
    x-permissions: [scene.read]
    requestBody:
      content:
        application/json:
          schema:
            type: object
            required: [assetId]
            properties:
              assetId:
                type: string
                format: uuid
              version:
                type: string
                nullable: true
              originTransform:
                $ref: '#/components/schemas/Transform'
                nullable: true
                description: Optional world origin. Null = identity.
              nodeTypes:
                type: array
                items:
                  $ref: '#/components/schemas/NodeType'
                nullable: true
                description: Filter to specific node types
    responses:
      '200':
        description: World transforms computed
        content:
          application/json:
            schema:
              type: object
              properties:
                nodes:
                  type: array
                  items:
                    $ref: '#/components/schemas/FlattenedNode'
                boundingBox:
                  type: object
                  properties:
                    min:
                      $ref: '#/components/schemas/Vector3'
                    max:
                      $ref: '#/components/schemas/Vector3'
                  description: World-space bounding box of all nodes
```

#### Query Nodes

```yaml
/scene/query-nodes:
  post:
    operationId: queryNodes
    x-permissions: [scene.read]
    requestBody:
      content:
        application/json:
          schema:
            type: object
            required: [assetId]
            properties:
              assetId:
                type: string
                format: uuid
              version:
                type: string
                nullable: true
              nodeTypes:
                type: array
                items:
                  $ref: '#/components/schemas/NodeType'
                nullable: true
                description: Filter by node types
              tags:
                type: array
                items:
                  type: string
                nullable: true
                description: Filter by tags (OR logic)
              refIdPattern:
                type: string
                nullable: true
                description: Regex pattern for RefID matching
              includeChildren:
                type: boolean
                default: false
                description: Include full subtrees of matching nodes
    responses:
      '200':
        description: Matching nodes
        content:
          application/json:
            schema:
              type: object
              properties:
                nodes:
                  type: array
                  items:
                    type: object
                    properties:
                      node:
                        $ref: '#/components/schemas/SceneNode'
                      path:
                        type: string
                        description: Node path from root (e.g., "root/building/room1")
                      depth:
                        type: integer
                        description: Depth in hierarchy
```

---

## Event System

### Service-to-Service Events

These events are published on the internal message bus for service coordination.

#### Scene Instantiated

Published when a scene is instantiated in the game world.

```yaml
SceneInstantiatedEvent:
  topic: scene.instantiated
  type: object
  required: [instanceId, sceneAssetId, sceneVersion, regionId, worldTransform, flattenedNodes]
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
      description: Version of scene that was instantiated
    sceneName:
      type: string
      description: Scene name (for logging/debugging)
    regionId:
      type: string
      format: uuid
      description: Target region for spatial objects
    worldTransform:
      $ref: '#/components/schemas/Transform'
      description: World-space origin transform
    flattenedNodes:
      type: array
      items:
        $ref: '#/components/schemas/FlattenedNode'
      description: All nodes with pre-computed world transforms
    propertyOverrides:
      type: array
      items:
        type: object
        properties:
          refId:
            type: string
          properties:
            type: object
      description: Applied property overrides
    instantiatedAt:
      type: string
      format: date-time
    instantiatedBy:
      type: string
      description: Service or user that triggered instantiation
```

#### Scene Destroyed

Published when a scene instance is removed.

```yaml
SceneDestroyedEvent:
  topic: scene.destroyed
  type: object
  required: [instanceId, regionId]
  properties:
    instanceId:
      type: string
      format: uuid
      description: Instance being destroyed
    sceneAssetId:
      type: string
      format: uuid
      description: Source scene asset ID
    regionId:
      type: string
      format: uuid
      description: Region where instance existed
    nodeIds:
      type: array
      items:
        type: string
        format: uuid
      description: All node IDs that were part of this instance
    destroyedAt:
      type: string
      format: date-time
    destroyedBy:
      type: string
      description: Service or user that triggered destruction
```

#### Scene Checked Out

Published when a scene is locked for editing.

```yaml
SceneCheckedOutEvent:
  topic: scene.checked_out
  type: object
  required: [sceneAssetId, lockedBy, lockedUntil]
  properties:
    sceneAssetId:
      type: string
      format: uuid
    sceneName:
      type: string
    lockedBy:
      type: string
      description: User or service that acquired lock
    lockedUntil:
      type: string
      format: date-time
    checkedOutAt:
      type: string
      format: date-time
```

#### Scene Committed

Published when a scene edit is saved.

```yaml
SceneCommittedEvent:
  topic: scene.committed
  type: object
  required: [sceneAssetId, previousVersion, newVersion]
  properties:
    sceneAssetId:
      type: string
      format: uuid
    sceneName:
      type: string
    previousVersion:
      type: string
    newVersion:
      type: string
    message:
      type: string
      description: Commit message
    committedBy:
      type: string
    committedAt:
      type: string
      format: date-time
    changeSummary:
      type: object
      properties:
        nodesAdded:
          type: integer
        nodesModified:
          type: integer
        nodesRemoved:
          type: integer
```

### Event Consumer Reactions

#### Mapping Service Consumer

Reacts to `scene.instantiated`:
1. Filters nodes to spatial types (spawn_point, marker, trigger, waypoint)
2. Creates MapObject for each with:
   - `objectId`: Derived from node ID + instance ID
   - `objectType`: Maps from nodeType (e.g., "scene_spawn_point")
   - `position`: From worldTransform
   - `data`: Node properties + scene metadata
3. Publishes to appropriate channel (regionId + MapKind)

Reacts to `scene.destroyed`:
1. Queries objects with matching instance ID
2. Deletes all matching objects
3. Publishes MapObjectsChangedEvent

#### Actor Service Consumer

Reacts to `scene.instantiated`:
1. Filters nodes to spawn_point type
2. For each spawn point with `spawnType: "npc"`:
   - Looks up actor template by spawn tags
   - Creates actor instance at spawn location
   - Registers actor with behavior system
3. Associates actors with scene instance ID

Reacts to `scene.destroyed`:
1. Finds actors associated with instance ID
2. Gracefully stops actor behaviors
3. Removes actor instances

#### Game Server Consumer

Reacts to `scene.instantiated`:
1. Receives full node list with assets
2. Queues asset loading for models, sounds, particles
3. Creates runtime objects at world positions
4. Activates sounds with `playOnAwake: true`
5. Registers triggers for collision detection

Reacts to `scene.destroyed`:
1. Unloads assets associated with instance
2. Removes runtime objects
3. Deactivates sounds and effects

---

## Integration Strategies

### With lib-asset

Scene Service uses lib-asset as its storage backend:

```csharp
// Creating a scene
public async Task<(StatusCodes, CreateSceneResponse?)> CreateSceneAsync(
    CreateSceneRequest request, CancellationToken ct)
{
    // 1. Build scene document
    var scene = new Scene
    {
        SceneId = Guid.NewGuid(),
        Name = request.Name,
        Version = "1.0.0",
        RealmId = request.RealmId,
        Root = request.RootNode ?? CreateEmptyRoot(),
        CreatedAt = DateTime.UtcNow
    };

    // 2. Serialize to YAML
    var yaml = SceneSerializer.Serialize(scene);
    var content = Encoding.UTF8.GetBytes(yaml);

    // 3. Upload to lib-asset
    var uploadRequest = await _assetClient.RequestUploadAsync(new UploadRequestRequest
    {
        Filename = $"{scene.Name}.scene.yaml",
        ContentType = "application/x-yaml",
        SizeBytes = content.Length,
        AssetType = "scene",
        Realm = request.RealmId,
        Tags = ["scene", ..request.Metadata?.Tags ?? []]
    }, ct);

    // 4. Upload content to pre-signed URL
    await _httpClient.PutAsync(uploadRequest.UploadUrl,
        new ByteArrayContent(content), ct);

    // 5. Complete upload
    await _assetClient.CompleteUploadAsync(new CompleteUploadRequest
    {
        UploadId = uploadRequest.UploadId
    }, ct);

    return (StatusCodes.Status200OK, new CreateSceneResponse
    {
        AssetId = uploadRequest.AssetId,
        Scene = scene,
        Version = "1.0.0"
    });
}
```

### With Mapping Service

The instantiation bridge between scene and mapping:

```csharp
// In MappingService event consumer
public async Task HandleSceneInstantiatedAsync(
    SceneInstantiatedEvent evt, CancellationToken ct)
{
    // Filter to spatial node types
    var spatialTypes = new[] {
        NodeType.SpawnPoint, NodeType.Marker,
        NodeType.Trigger, NodeType.Waypoint
    };

    var spatialNodes = evt.FlattenedNodes
        .Where(n => spatialTypes.Contains(n.NodeType))
        .ToList();

    // Create map objects
    var mapObjects = spatialNodes.Select(node => new MapPayload
    {
        ObjectId = DeriveObjectId(evt.InstanceId, node.NodeId),
        ObjectType = $"scene_{node.NodeType.ToString().ToLower()}",
        Position = new Position3D
        {
            X = node.WorldPosition.X,
            Y = node.WorldPosition.Y,
            Z = node.WorldPosition.Z
        },
        Data = new Dictionary<string, object>
        {
            ["sceneInstanceId"] = evt.InstanceId,
            ["sceneAssetId"] = evt.SceneAssetId,
            ["refId"] = node.RefId,
            ["nodeType"] = node.NodeType.ToString(),
            ["properties"] = node.Properties
        }
    });

    // Publish to mapping channel
    await PublishMapUpdateAsync(new PublishMapUpdateRequest
    {
        RegionId = evt.RegionId,
        Kind = MapKind.DynamicObjects,
        Objects = mapObjects.ToList()
    }, ct);
}
```

### With Actor Service

Spawning NPCs from scene spawn points:

```csharp
// In ActorService event consumer
public async Task HandleSceneInstantiatedAsync(
    SceneInstantiatedEvent evt, CancellationToken ct)
{
    var spawnPoints = evt.FlattenedNodes
        .Where(n => n.NodeType == NodeType.SpawnPoint)
        .Where(n => n.Properties.TryGetValue("spawnType", out var type)
                    && type?.ToString() == "npc")
        .ToList();

    foreach (var spawn in spawnPoints)
    {
        var tags = spawn.Properties.GetValueOrDefault("spawnTags") as List<string>
                   ?? new List<string>();

        // Find matching actor template
        var template = await FindTemplateByTagsAsync(tags, ct);
        if (template == null) continue;

        // Create actor instance
        var actorId = $"scene-{evt.InstanceId}-{spawn.RefId}";
        await CreateActorInstanceAsync(new CreateActorInstanceRequest
        {
            ActorId = actorId,
            TemplateId = template.TemplateId,
            InitialPosition = spawn.WorldPosition,
            InitialRotation = spawn.WorldTransform.Rotation,
            Metadata = new Dictionary<string, object>
            {
                ["sceneInstanceId"] = evt.InstanceId,
                ["spawnPointRefId"] = spawn.RefId
            }
        }, ct);
    }
}
```

---

## Use Cases and Examples

### Use Case 1: Tavern Interior

A level designer creates a tavern interior scene for use in multiple locations.

```yaml
$schema: "bannou://schemas/scene/v1"
sceneId: "tavern-cozy-interior-001"
name: "Cozy Tavern Interior"
version: "1.2.0"
realmId: "arcadia"

metadata:
  author: "level_design_team"
  tags: ["interior", "tavern", "social", "cozy"]
  boundingBox:
    min: { x: -8, y: 0, z: -8 }
    max: { x: 8, y: 4, z: 8 }

root:
  nodeId: "root"
  refId: "tavern_root"
  name: "Tavern Root"
  nodeType: group
  transform:
    position: { x: 0, y: 0, z: 0 }
    rotation: { x: 0, y: 0, z: 0, w: 1 }
    scale: { x: 1, y: 1, z: 1 }
  children:
    # Building shell
    - nodeId: "shell"
      refId: "building"
      name: "Tavern Building"
      nodeType: model
      transform:
        position: { x: 0, y: 0, z: 0 }
        rotation: { x: 0, y: 0, z: 0, w: 1 }
        scale: { x: 1, y: 1, z: 1 }
      asset:
        bundleId: "env-tavern-bundle"
        assetId: "model-tavern-shell"
        variantId: "weathered"
      properties:
        castShadows: true
        receiveShadows: true
      children:
        # Main entrance
        - nodeId: "entrance-main"
          refId: "main_entrance"
          name: "Main Entrance"
          nodeType: marker
          transform:
            position: { x: 0, y: 0, z: 7 }
            rotation: { x: 0, y: 0, z: 0, w: 1 }
            scale: { x: 1, y: 1, z: 1 }
          properties:
            markerType: "entrance"
            tags: ["main", "public"]
          children: []

        # Bar area
        - nodeId: "bar-area"
          refId: "bar"
          name: "Bar Area"
          nodeType: group
          transform:
            position: { x: -4, y: 0, z: -2 }
            rotation: { x: 0, y: 0.707, z: 0, w: 0.707 }  # 90 degrees
            rotationEuler: { pitch: 0, yaw: 90, roll: 0 }
            scale: { x: 1, y: 1, z: 1 }
          children:
            # Bar counter model
            - nodeId: "bar-counter"
              refId: "counter"
              name: "Bar Counter"
              nodeType: model
              transform:
                position: { x: 0, y: 0, z: 0 }
                rotation: { x: 0, y: 0, z: 0, w: 1 }
                scale: { x: 1, y: 1, z: 1 }
              asset:
                bundleId: "env-tavern-bundle"
                assetId: "model-bar-counter"
              properties:
                castShadows: true
              children: []

            # Bartender spawn
            - nodeId: "spawn-bartender"
              refId: "bartender_spawn"
              name: "Bartender Spawn"
              nodeType: spawn_point
              transform:
                position: { x: 0, y: 0, z: -0.5 }
                rotation: { x: 0, y: 0, z: 0, w: 1 }
                scale: { x: 1, y: 1, z: 1 }
              properties:
                spawnType: "npc"
                spawnTags: ["bartender", "merchant", "friendly"]
                maxOccupancy: 1
              children: []

            # Ambient bar sounds
            - nodeId: "sound-bar"
              refId: "bar_ambience"
              name: "Bar Ambient Sound"
              nodeType: sound
              transform:
                position: { x: 0, y: 1.5, z: 0 }
                rotation: { x: 0, y: 0, z: 0, w: 1 }
                scale: { x: 1, y: 1, z: 1 }
              asset:
                assetId: "audio-tavern-bar-ambience"
              properties:
                volume: 0.4
                spatialBlend: 0.8
                minDistance: 1
                maxDistance: 8
                loop: true
                playOnAwake: true
              children: []

        # Seating area
        - nodeId: "seating"
          refId: "seating_area"
          name: "Seating Area"
          nodeType: group
          transform:
            position: { x: 3, y: 0, z: 0 }
            rotation: { x: 0, y: 0, z: 0, w: 1 }
            scale: { x: 1, y: 1, z: 1 }
          children:
            # Tables (repeating pattern)
            - nodeId: "table-1"
              refId: "table_1"
              name: "Table 1"
              nodeType: model
              transform:
                position: { x: 0, y: 0, z: -2 }
                rotation: { x: 0, y: 0, z: 0, w: 1 }
                scale: { x: 1, y: 1, z: 1 }
              asset:
                bundleId: "env-tavern-bundle"
                assetId: "model-tavern-table"
                variantId: "round_small"
              properties:
                castShadows: true
              children:
                # Patron spawn at table
                - nodeId: "spawn-patron-1"
                  refId: "patron_spawn_1"
                  name: "Patron Spawn 1"
                  nodeType: spawn_point
                  transform:
                    position: { x: 0.6, y: 0, z: 0 }
                    rotation: { x: 0, y: -0.707, z: 0, w: 0.707 }  # Face table
                    scale: { x: 1, y: 1, z: 1 }
                  properties:
                    spawnType: "npc"
                    spawnTags: ["patron", "civilian"]
                    maxOccupancy: 1
                  children: []

        # Fireplace with effects
        - nodeId: "fireplace"
          refId: "fireplace"
          name: "Fireplace"
          nodeType: group
          transform:
            position: { x: 0, y: 0, z: -6 }
            rotation: { x: 0, y: 0, z: 0, w: 1 }
            scale: { x: 1, y: 1, z: 1 }
          children:
            - nodeId: "fireplace-model"
              refId: "fireplace_structure"
              name: "Fireplace Structure"
              nodeType: model
              asset:
                bundleId: "env-tavern-bundle"
                assetId: "model-fireplace"
              transform:
                position: { x: 0, y: 0, z: 0 }
                rotation: { x: 0, y: 0, z: 0, w: 1 }
                scale: { x: 1, y: 1, z: 1 }
              children: []

            - nodeId: "fire-light"
              refId: "fire_light"
              name: "Fire Light"
              nodeType: light
              transform:
                position: { x: 0, y: 0.5, z: 0.3 }
                rotation: { x: 0, y: 0, z: 0, w: 1 }
                scale: { x: 1, y: 1, z: 1 }
              properties:
                lightType: "point"
                color: { r: 1.0, g: 0.6, b: 0.2 }
                intensity: 2.0
                range: 6.0
                shadowType: "soft"
              children: []

            - nodeId: "fire-particles"
              refId: "fire_vfx"
              name: "Fire Particles"
              nodeType: particle_system
              transform:
                position: { x: 0, y: 0.3, z: 0.2 }
                rotation: { x: 0, y: 0, z: 0, w: 1 }
                scale: { x: 1, y: 1, z: 1 }
              asset:
                assetId: "vfx-campfire"
              properties:
                emissionRate: 50
                simulationSpace: "world"
              children: []

            - nodeId: "fire-sound"
              refId: "fire_crackle"
              name: "Fire Crackle Sound"
              nodeType: sound
              transform:
                position: { x: 0, y: 0.5, z: 0 }
                rotation: { x: 0, y: 0, z: 0, w: 1 }
                scale: { x: 1, y: 1, z: 1 }
              asset:
                assetId: "audio-fire-crackle"
              properties:
                volume: 0.3
                spatialBlend: 1.0
                minDistance: 0.5
                maxDistance: 5.0
                loop: true
                playOnAwake: true
              children: []
```

### Use Case 2: Dungeon Room Template

A procedural generator uses room templates to construct dungeons.

```yaml
$schema: "bannou://schemas/scene/v1"
sceneId: "dungeon-room-treasure"
name: "Treasure Room Template"
version: "1.0.0"
realmId: "arcadia"

metadata:
  tags: ["dungeon", "room", "treasure", "template"]
  roomType: "treasure"
  difficulty: "medium"
  connections:
    north: true
    south: true
    east: false
    west: false

root:
  nodeId: "root"
  refId: "room_root"
  name: "Treasure Room"
  nodeType: group
  transform:
    position: { x: 0, y: 0, z: 0 }
    rotation: { x: 0, y: 0, z: 0, w: 1 }
    scale: { x: 1, y: 1, z: 1 }
  children:
    # Room geometry
    - nodeId: "geometry"
      refId: "room_geometry"
      name: "Room Geometry"
      nodeType: model
      transform:
        position: { x: 0, y: 0, z: 0 }
        rotation: { x: 0, y: 0, z: 0, w: 1 }
        scale: { x: 1, y: 1, z: 1 }
      asset:
        bundleId: "dungeon-tileset"
        assetId: "model-room-10x10"
        variantId: "stone_worn"
      children: []

    # Connection markers (for dungeon generator)
    - nodeId: "conn-north"
      refId: "connection_north"
      name: "North Connection"
      nodeType: marker
      transform:
        position: { x: 0, y: 0, z: 5 }
        rotation: { x: 0, y: 0, z: 0, w: 1 }
        scale: { x: 1, y: 1, z: 1 }
      properties:
        markerType: "room_connection"
        tags: ["north", "door"]
      children: []

    - nodeId: "conn-south"
      refId: "connection_south"
      name: "South Connection"
      nodeType: marker
      transform:
        position: { x: 0, y: 0, z: -5 }
        rotation: { x: 0, y: 1, z: 0, w: 0 }  # Face south
        scale: { x: 1, y: 1, z: 1 }
      properties:
        markerType: "room_connection"
        tags: ["south", "door"]
      children: []

    # Treasure chest
    - nodeId: "chest"
      refId: "treasure_chest"
      name: "Treasure Chest"
      nodeType: group
      transform:
        position: { x: 0, y: 0, z: 0 }
        rotation: { x: 0, y: 0, z: 0, w: 1 }
        scale: { x: 1, y: 1, z: 1 }
      children:
        - nodeId: "chest-model"
          refId: "chest_model"
          nodeType: model
          name: "Chest Model"
          transform:
            position: { x: 0, y: 0, z: 0 }
            rotation: { x: 0, y: 0, z: 0, w: 1 }
            scale: { x: 1, y: 1, z: 1 }
          asset:
            bundleId: "dungeon-props"
            assetId: "model-chest-ornate"
          children: []

        - nodeId: "chest-trigger"
          refId: "chest_interaction"
          name: "Chest Interaction Trigger"
          nodeType: trigger
          transform:
            position: { x: 0, y: 0.5, z: 0 }
            rotation: { x: 0, y: 0, z: 0, w: 1 }
            scale: { x: 1, y: 1, z: 1 }
          properties:
            shape: "box"
            size: { x: 1.5, y: 1, z: 1.5 }
            isTrigger: true
            interactionTags: ["player"]
            enterEvent: "dungeon.chest.approached"
          children: []

    # Guardian spawn
    - nodeId: "spawn-guardian"
      refId: "guardian_spawn"
      name: "Guardian Spawn"
      nodeType: spawn_point
      transform:
        position: { x: -3, y: 0, z: 0 }
        rotation: { x: 0, y: 0.707, z: 0, w: 0.707 }  # Face center
        scale: { x: 1, y: 1, z: 1 }
      properties:
        spawnType: "monster"
        spawnTags: ["guardian", "elite", "undead"]
        maxOccupancy: 1
      children: []

    # Ambient lighting
    - nodeId: "torch-1"
      refId: "torch_1"
      name: "Wall Torch 1"
      nodeType: light
      transform:
        position: { x: 4, y: 2, z: 0 }
        rotation: { x: 0, y: 0, z: 0, w: 1 }
        scale: { x: 1, y: 1, z: 1 }
      properties:
        lightType: "point"
        color: { r: 1.0, g: 0.7, b: 0.4 }
        intensity: 1.5
        range: 5.0
        shadowType: "soft"
      children: []
```

### Use Case 3: Procedural Building Generation

A building generator creates scenes programmatically:

```csharp
public async Task<Scene> GenerateBuildingAsync(
    BuildingParameters parameters, CancellationToken ct)
{
    var scene = new Scene
    {
        SceneId = Guid.NewGuid(),
        Name = $"Generated Building {DateTime.UtcNow:yyyyMMddHHmmss}",
        Version = "1.0.0",
        RealmId = parameters.RealmId,
        Root = new SceneNode
        {
            NodeId = Guid.NewGuid(),
            RefId = "building_root",
            Name = "Building Root",
            NodeType = NodeType.Group,
            Transform = Transform.Identity,
            Children = new List<SceneNode>()
        }
    };

    // Generate floors
    for (int floor = 0; floor < parameters.FloorCount; floor++)
    {
        var floorNode = GenerateFloor(floor, parameters);
        scene.Root.Children.Add(floorNode);
    }

    // Add entrance markers
    var entrances = GenerateEntrances(parameters);
    scene.Root.Children.AddRange(entrances);

    // Add ambient sounds
    var ambientSound = new SceneNode
    {
        NodeId = Guid.NewGuid(),
        RefId = "building_ambience",
        Name = "Building Ambience",
        NodeType = NodeType.Sound,
        Transform = new Transform
        {
            Position = new Vector3(0, parameters.FloorCount * 3 / 2, 0),
            Rotation = Quaternion.Identity,
            Scale = Vector3.One
        },
        Asset = new AssetReference
        {
            AssetId = parameters.AmbienceAssetId
        },
        Properties = new Dictionary<string, object>
        {
            ["volume"] = 0.3,
            ["spatialBlend"] = 0.5,
            ["maxDistance"] = parameters.Width * 2,
            ["loop"] = true,
            ["playOnAwake"] = true
        }
    };
    scene.Root.Children.Add(ambientSound);

    return scene;
}

private SceneNode GenerateFloor(int floorIndex, BuildingParameters parameters)
{
    var floorHeight = 3.0f;
    var floorNode = new SceneNode
    {
        NodeId = Guid.NewGuid(),
        RefId = $"floor_{floorIndex}",
        Name = $"Floor {floorIndex}",
        NodeType = NodeType.Group,
        Transform = new Transform
        {
            Position = new Vector3(0, floorIndex * floorHeight, 0),
            Rotation = Quaternion.Identity,
            Scale = Vector3.One
        },
        Children = new List<SceneNode>()
    };

    // Add floor geometry
    floorNode.Children.Add(new SceneNode
    {
        NodeId = Guid.NewGuid(),
        RefId = $"floor_{floorIndex}_geometry",
        Name = $"Floor {floorIndex} Geometry",
        NodeType = NodeType.Model,
        Transform = Transform.Identity,
        Asset = new AssetReference
        {
            BundleId = parameters.BuildingBundleId,
            AssetId = parameters.FloorModelId,
            VariantId = floorIndex == 0 ? "ground_floor" : "upper_floor"
        }
    });

    // Generate rooms on this floor
    var rooms = GenerateRooms(floorIndex, parameters);
    floorNode.Children.AddRange(rooms);

    return floorNode;
}
```

---

## Future Considerations

### Scene Templates with Variables

Scenes could support variable substitution for procedural generation:

```yaml
# Template definition
variables:
  - name: building_style
    type: string
    enum: [rustic, elegant, ruined]
    default: rustic
  - name: npc_faction
    type: string
    default: neutral

root:
  children:
    - nodeType: model
      asset:
        assetId: "model-building-shell"
        variantId: "${building_style}"  # Variable substitution
    - nodeType: spawn_point
      properties:
        spawnTags: ["${npc_faction}", "guard"]  # Variable in array
```

### Scene Inheritance

Scenes could inherit from parent scenes:

```yaml
extends: "scene://base-tavern-interior"
overrides:
  - refId: "bartender_spawn"
    properties:
      spawnTags: ["bartender", "dwarf"]  # Override NPC type
  - refId: "building"
    asset:
      variantId: "dwarven"  # Override building style
additions:
  - parentRefId: "seating_area"
    node:
      # Add new table
```

### Multi-Scene Composition

Large environments could compose multiple scenes:

```yaml
compositeScene:
  name: "Village Center"
  scenes:
    - sceneRef: "scene://tavern-exterior"
      transform:
        position: { x: 0, y: 0, z: 0 }
    - sceneRef: "scene://blacksmith-exterior"
      transform:
        position: { x: 30, y: 0, z: 0 }
    - sceneRef: "scene://market-stalls"
      transform:
        position: { x: 15, y: 0, z: 20 }
```

### Prefab System

Frequently reused node subtrees could be prefabs:

```yaml
# Prefab definition
prefabId: "prefab-tavern-table-set"
root:
  nodeType: group
  children:
    - nodeType: model
      refId: "table"
      asset: { assetId: "model-table" }
    - nodeType: model
      refId: "chair_1"
      transform: { position: { x: 0.6, y: 0, z: 0 } }
      asset: { assetId: "model-chair" }
    - nodeType: model
      refId: "chair_2"
      transform: { position: { x: -0.6, y: 0, z: 0 } }
      asset: { assetId: "model-chair" }

# Usage in scene
- nodeType: prefab_instance
  prefabId: "prefab-tavern-table-set"
  transform: { position: { x: 3, y: 0, z: -2 } }
  overrides:
    - refId: "table"
      asset: { variantId: "round" }
```

### LOD and Streaming

Large scenes could support level-of-detail and streaming:

```yaml
metadata:
  streaming:
    enabled: true
    chunkSize: 32  # Units per streaming chunk
    lodLevels:
      - distance: 50
        simplification: 0.5
      - distance: 100
        simplification: 0.1
      - distance: 200
        cullChildren: true  # Only show bounding box
```

### Collaboration Features

If real-time collaboration is needed in the future:

- Operational transform for concurrent edits
- Per-node locking for coarse-grained conflicts
- Change history with user attribution
- Merge/conflict resolution UI

---

## Implementation Roadmap

### Phase 1: Core Service

1. **Create schema files**
   - `schemas/scene-api.yaml` - API endpoints
   - `schemas/scene-events.yaml` - Service events
   - `schemas/scene-client-events.yaml` - Client push events
   - `schemas/scene-configuration.yaml` - Service configuration

2. **Generate service scaffolding**
   - Run `scripts/generate-all-services.sh`
   - Creates `lib-scene/` plugin structure

3. **Implement core operations**
   - Create scene (serialize to asset)
   - Get scene (deserialize from asset)
   - Checkout/commit (locking via Redis)
   - Basic validation

### Phase 2: Transform System

1. **Implement transform utilities**
   - Quaternion ↔ Euler conversion
   - World transform computation (hierarchy walking)
   - Bounding box calculation

2. **Implement flatten operation**
   - Walk hierarchy
   - Multiply transforms
   - Generate FlattenedNode list

### Phase 3: Instantiation

1. **Implement instantiate endpoint**
   - Load scene
   - Compute world transforms
   - Publish scene.instantiated event

2. **Implement event consumers**
   - Mapping service consumer
   - Actor service consumer

3. **Implement destroy endpoint**
   - Publish scene.destroyed event
   - Consumers clean up

### Phase 4: Query and Validation

1. **Implement query-nodes**
   - Filter by type, tags, refId pattern
   - Return matching nodes with paths

2. **Implement validation**
   - RefID uniqueness
   - Asset reference validation
   - Transform sanity checks
   - Required properties per node type

### Phase 5: Editor Integration

1. **Add client events**
   - Scene update notifications
   - Lock status changes

2. **Implement editor-friendly endpoints**
   - Node CRUD operations
   - Subtree copy/paste
   - Undo/redo support (client-side)

---

## Appendix: Comparison with Game Engine Concepts

| Game Engine Concept | Scene Service Equivalent |
|---------------------|-------------------------|
| Unity GameObject | SceneNode |
| Unity Transform | Transform (position, rotation, scale) |
| Unity Prefab | Future: Prefab system |
| Unity Scene | Scene document |
| Unreal Actor | SceneNode |
| Unreal Component | Properties + asset reference |
| Unreal Level | Scene document |
| Godot Node | SceneNode |
| Godot Resource | Asset in lib-asset |

The Scene Service provides the **data representation** that game engines consume. It does not replicate engine functionality (physics, rendering) - it provides the scene graph that engines instantiate.

---

## Document History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 0.1 | 2025-01-09 | Initial design collaboration | Initial draft from design session |
