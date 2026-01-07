# Mapping Plugin - Spatial Data Management for Arcadia

> **Status**: PLANNING
> **Priority**: Medium
> **Complexity**: High
> **Dependencies**: lib-state, lib-messaging, lib-asset (for large blob storage), Actor Plugin (for consumption)
> **Last Updated**: 2026-01-07
> **Related Documents**:
> - [ACTORS_PLUGIN_V3.md](./UPCOMING_-_ACTORS_PLUGIN_V3.md) - Actor consumption patterns
> - [BEHAVIOR_PLUGIN_V2.md](./ONGOING_-_BEHAVIOR_PLUGIN_V2.md) - ABML behaviors that consume map data
> - [THE_DREAM.md](./THE_DREAM.md) - Vision document, affordance queries
> - [BANNOU_DESIGN.md](../BANNOU_DESIGN.md) - Overall architecture

---

## 1. Executive Summary

The Mapping Plugin (`lib-mapping`) manages spatial data for Arcadia's game worlds. It enables:

1. **Game servers** (authority) to publish live spatial data via lib-messaging AND RPC
2. **Event actors** to subscribe to region-wide map data for orchestrating events
3. **NPC actors** to receive perception-filtered spatial context
4. **Design tools** to author and edit map templates
5. **Event Brain** to query affordances for procedural scene orchestration

### Core Design Principles

1. **Schema-less Payloads**: Map objects are "bags of data" - `additionalProperties: true` everywhere. Only publisher and consumer care about contents; lib-mapping passes them through unchanged.

2. **Bidirectional Event Flow**: Authority doesn't just call RPCs - it publishes events TO map topics via lib-messaging. This enables efficient bulk processing, queue pooling, and internal distribution.

3. **Authority Registration**: Authority is established during map/channel creation. While you're authority, your publishes are authoritative. Non-authority publishes generate warnings.

4. **No MVP Compromises**: This design is complete from day one. No "scale later" shortcuts.

### Key Insight: Three Data Flows

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        MAPPING DATA FLOWS                                    │
│                                                                              │
│  AUTHORING FLOW (Design-time):                                              │
│  ┌──────────┐    checkout/commit    ┌─────────────┐                         │
│  │  Editor  │ ──────────────────────▶│ lib-mapping │──▶ Stored templates    │
│  │  Tools   │    (with locking)      │  (Bannou)   │                        │
│  └──────────┘                        └─────────────┘                        │
│                                                                              │
│  RUNTIME FLOW - RPC (Request/Response):                                     │
│  ┌──────────┐   POST /mapping/*      ┌─────────────┐    events    ┌───────┐│
│  │  Game    │ ──────────────────────▶│ lib-mapping │─────────────▶│Actors ││
│  │  Server  │  (validated publish)   │  (Bannou)   │              │       ││
│  │ (Stride) │                        └─────────────┘              └───────┘│
│  └──────────┘                                                               │
│                                                                              │
│  RUNTIME FLOW - EVENT PUBLISHING (High-throughput):                         │
│  ┌──────────┐   lib-messaging        ┌─────────────┐    events    ┌───────┐│
│  │  Game    │ ───────────────────────▶│ lib-mapping │─────────────▶│Actors ││
│  │  Server  │  map.ingest.{regionId} │  (Bannou)   │              │       ││
│  │ (Stride) │  (bulk/streaming)      └─────────────┘              └───────┘│
│  └──────────┘                                                               │
│      │                                                                      │
│      │ AUTHORITY: Game server owns live spatial truth                       │
│      │ Authority established at map creation/checkout                       │
│      │ Non-authority publishes generate warnings                            │
└──────────────────────────────────────────────────────────────────────────────┘
```

### Why "lib-mapping" (not lib-map)?

Following the gerund naming convention used for other plugins where the base word is too common:
- `lib-messaging` (not lib-message) - "message" conflicts with common types
- `lib-testing` (not lib-test) - "test" conflicts with common types
- `lib-mapping` (not lib-map) - "map" conflicts with Dictionary/Map types everywhere

---

## 2. Architecture Overview

### 2.1 Component Relationships

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           BANNOU SERVICES                                    │
│                                                                              │
│  ┌─────────────┐   ┌─────────────┐   ┌─────────────┐   ┌─────────────┐     │
│  │   Mapping   │   │    Actor    │   │   Behavior  │   │    Asset    │     │
│  │   Service   │   │   Service   │   │   Service   │   │   Service   │     │
│  └──────┬──────┘   └──────┬──────┘   └──────┬──────┘   └──────┬──────┘     │
│         │                 │                 │                 │             │
│    map storage      actor lifecycle    ABML/GOAP         blob storage       │
│    + events         + routing          planning          large payloads     │
└─────────┼─────────────────┼─────────────────┼─────────────────┼─────────────┘
          │                 │                 │                 │
          │ lib-messaging   │                 │                 │
          │ (events)        │                 │                 │
          ▼                 ▼                 ▼                 ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                         EVENT ACTORS (Pool Nodes)                            │
│                                                                              │
│  Region Event Actor:                    NPC Brain Actor:                    │
│  - Subscribes to map.{regionId}.*       - Does NOT subscribe to map events  │
│  - Full region view (static geometry,   - Receives spatial data via         │
│    terrain, resources, etc.)              PERCEPTION events from game server│
│  - Orchestrates ambushes, encounters    - Perception-limited view only      │
│  - Spawns cinematics at locations       - Actor influences character state  │
└─────────────────────────────────────────────────────────────────────────────┘
          ▲                                       ▲
          │ map events                            │ perception events
          │                                       │
┌─────────────────────────────────────────────────────────────────────────────┐
│                          GAME SERVERS (Stride)                               │
│                                                                              │
│  AUTHORITY for live spatial data:                                           │
│  - Tracks what's at every location (objects, terrain state, hazards)        │
│  - PUBLISHES map updates when things change                                 │
│  - PUBLISHES perception events to characters                                │
│  - Runs behavior stacks that READ map context                               │
│                                                                              │
│  Map context available to behavior stack:                                   │
│  - Terrain type at position (grass, water, stone)                           │
│  - Nearby objects (boulders, trees, buildings)                              │
│  - Hazard zones (fire, radiation, deep water)                               │
│  - Navigation hints (pathable areas, chokepoints)                           │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 2.2 The Critical Distinction: NPC Actors vs Event Actors

| Aspect | NPC Brain Actor | Event Actor |
|--------|-----------------|-------------|
| **View** | Perception-limited (what character sees/hears) | Region-wide (full spatial awareness) |
| **Data Source** | Perception events from game server | Direct map subscription from lib-mapping |
| **Subscribes To** | `character.{characterId}.perception` | `map.{regionId}.{kind}.*` |
| **Purpose** | Character growth, feelings, goals | Orchestrate events, spawn encounters |
| **Spatial Knowledge** | Only what character perceives | Everything in managed region |
| **Example Use** | "I feel angry at that enemy" | "Spawn ambush at boulder cluster at coords X,Y" |

**Key Insight**: NPC actors don't need direct map subscriptions because the game server already:
1. Knows the full spatial context
2. Generates perception events that include relevant spatial info
3. Runs the behavior stack that reads map context directly

Event actors DO need direct map subscriptions because they:
1. Orchestrate multi-character scenes
2. Plan encounters based on terrain/geometry
3. Need to know "where are good ambush points" without a character perceiving them

---

## 3. Authority Model

### 3.1 Authority Registration

Authority over a map channel (region + kind combination) is established at creation or checkout:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        AUTHORITY LIFECYCLE                                   │
│                                                                              │
│  CREATION: Authority assigned to creator                                    │
│  ┌──────────────┐   POST /mapping/create-channel   ┌─────────────┐         │
│  │ Game Server  │ ─────────────────────────────────▶│ lib-mapping │         │
│  │  app-id: X   │   { regionId, kind, ... }        │             │         │
│  └──────────────┘                                   └──────┬──────┘         │
│                                                             │                │
│         ◄─────────────────────────────────────────────────┘                │
│         { channelId, authorityToken, ingestTopic }                          │
│         Authority: app-id X for region/kind until released                  │
│                                                                              │
│  CHECKOUT: Authority transferred to requester                               │
│  ┌──────────────┐   POST /mapping/checkout         ┌─────────────┐         │
│  │   Editor     │ ─────────────────────────────────▶│ lib-mapping │         │
│  │  app-id: Y   │   { regionId, kind, ... }        │             │         │
│  └──────────────┘                                   └──────┬──────┘         │
│                                                             │                │
│         ◄─────────────────────────────────────────────────┘                │
│         { checkoutId, authorityToken, ingestTopic }                         │
│         Authority: app-id Y for region/kind until commit/release            │
│                                                                              │
│  RELEASE: Authority returned/transferred                                    │
│  ┌──────────────┐   POST /mapping/release          ┌─────────────┐         │
│  │ Game Server  │ ─────────────────────────────────▶│ lib-mapping │         │
│  │  app-id: X   │   { channelId }                  │             │         │
│  └──────────────┘                                   └──────┬──────┘         │
│                                                             │                │
│         Channel authority is now UNASSIGNED (or reverts to previous)       │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 3.2 Authority Token

When you become authority, you receive an `authorityToken`:

```yaml
AuthorityGrant:
  type: object
  required: [channelId, authorityToken, ingestTopic, expiresAt]
  properties:
    channelId:
      type: string
      format: uuid
      description: The channel (region+kind) you have authority over
    authorityToken:
      type: string
      description: Opaque token proving your authority (include in publishes)
    ingestTopic:
      type: string
      description: Topic for direct lib-messaging publishes (map.ingest.{channelId})
    expiresAt:
      type: string
      format: date-time
      description: When authority expires (must refresh or release)
    regionId:
      type: string
      format: uuid
    kind:
      type: string
```

### 3.3 Publishing as Authority

Authority can publish via TWO mechanisms:

**Mechanism 1: RPC (Request/Response)**
- Traditional HTTP POST to `/mapping/publish`
- Includes `authorityToken` in request
- Validated, acknowledged, guaranteed delivery
- Use for: important state changes, low-frequency updates

**Mechanism 2: Event Publishing (lib-messaging)**
- Direct publish to `map.ingest.{channelId}` topic
- Includes `authorityToken` in event metadata
- High-throughput, fire-and-forget, queue pooling
- Use for: bulk updates, streaming telemetry, high-frequency changes

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    AUTHORITY PUBLISHING PATHS                                │
│                                                                              │
│  RPC Path (validated, acknowledged):                                        │
│  ┌──────────────┐   POST /mapping/publish    ┌─────────────┐               │
│  │ Game Server  │ ───────────────────────────▶│ lib-mapping │               │
│  │ (authority)  │   { authorityToken, ... }  │   Service   │               │
│  └──────────────┘                             └──────┬──────┘               │
│         │                                            │                       │
│         │◄───────────────────────────────────────────│ Response (ack/nack)  │
│         │                                            │                       │
│         │                                            ▼ Publishes to          │
│         │                                    map.{regionId}.{kind}.updated   │
│                                                                              │
│  Event Path (high-throughput, fire-and-forget):                             │
│  ┌──────────────┐   lib-messaging publish    ┌─────────────┐               │
│  │ Game Server  │ ───────────────────────────▶│ lib-mapping │               │
│  │ (authority)  │   map.ingest.{channelId}   │  Ingester   │               │
│  └──────────────┘   { authorityToken, ... }  └──────┬──────┘               │
│                                                      │                       │
│         No response (fire-and-forget)                │                       │
│                                                      ▼ Validates + broadcasts│
│                                              map.{regionId}.{kind}.updated   │
│                                                                              │
│  BOTH paths end up at the same output topic for consumers                   │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 3.4 Non-Authority Publishes

Non-authority publish handling is **configurable per-channel** to support different operational scenarios. The authority specifies the handling mode when creating or configuring a channel.

#### 3.4.1 Handling Mode Enum

```yaml
NonAuthorityHandlingMode:
  type: string
  description: How to handle publish attempts from non-authority sources
  enum:
    - reject_and_alert    # DEFAULT: Reject the publish, emit warning event
    - accept_and_alert    # Accept the publish but emit warning event (permissive mode)
    - reject_silent       # Reject without emitting warning (reduces noise)
  default: reject_and_alert
```

| Mode | Publish Accepted? | Warning Event? | Use Case |
|------|------------------|----------------|----------|
| `reject_and_alert` | No | Yes | **Default** - strict authority, monitor violations |
| `accept_and_alert` | Yes | Yes | Permissive mode during migration/debugging |
| `reject_silent` | No | No | High-volume channels where warnings would spam |

#### 3.4.2 Alert Configuration

When alerts are enabled, they can be routed to a specific topic:

```yaml
NonAuthorityAlertConfig:
  type: object
  properties:
    enabled:
      type: boolean
      default: true
      description: Whether to emit warning events for non-authority publishes
    alertTopic:
      type: string
      description: |
        Custom topic for warning events.
        Default: map.warnings.unauthorized_publish
        Example: ops.alerts.mapping.{regionId}
    includePayloadSummary:
      type: boolean
      default: false
      description: Include truncated payload in warning (for debugging)
```

#### 3.4.3 Channel-Level Configuration

Authority sets the handling mode at channel creation:

```yaml
CreateChannelRequest:
  type: object
  required: [regionId, kind]
  properties:
    # ... existing fields ...
    nonAuthorityHandling:
      $ref: '#/components/schemas/NonAuthorityHandlingMode'
      default: reject_and_alert
    alertConfig:
      $ref: '#/components/schemas/NonAuthorityAlertConfig'
```

#### 3.4.4 Handling Flow

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    NON-AUTHORITY PUBLISH HANDLING                            │
│                                                                              │
│  1. Publish arrives (RPC or Event path)                                     │
│  2. Authority token validation fails                                        │
│  3. Check channel's nonAuthorityHandling mode:                              │
│                                                                              │
│     ┌─────────────────────┐                                                 │
│     │ reject_and_alert    │──▶ Reject + Emit warning to alertTopic          │
│     ├─────────────────────┤                                                 │
│     │ accept_and_alert    │──▶ Accept + Emit warning to alertTopic          │
│     ├─────────────────────┤                                                 │
│     │ reject_silent       │──▶ Reject + Log only (no event)                 │
│     └─────────────────────┘                                                 │
│                                                                              │
│  RPC Response (for reject modes):                                           │
│     { status: WARNING,                                                       │
│       warning: "Not authority for channel",                                  │
│       currentAuthority: "app-id X",                                          │
│       accepted: false,                                                       │
│       handlingMode: "reject_and_alert" }                                     │
│                                                                              │
│  RPC Response (for accept mode):                                            │
│     { status: WARNING,                                                       │
│       warning: "Accepted from non-authority (permissive mode)",              │
│       currentAuthority: "app-id X",                                          │
│       accepted: true,                                                        │
│       handlingMode: "accept_and_alert" }                                     │
└─────────────────────────────────────────────────────────────────────────────┘
```

#### 3.4.5 Warning Event Schema

```yaml
MapUnauthorizedPublishWarning:
  type: object
  required: [timestamp, channelId, attemptedPublisher, currentAuthority, handlingMode]
  properties:
    timestamp:
      type: string
      format: date-time
    channelId:
      type: string
      format: uuid
    regionId:
      type: string
      format: uuid
    kind:
      type: string
    attemptedPublisher:
      type: string
      description: app-id that tried to publish
    currentAuthority:
      type: string
      description: app-id that currently has authority
    handlingMode:
      $ref: '#/components/schemas/NonAuthorityHandlingMode'
    publishAccepted:
      type: boolean
      description: Whether the publish was actually accepted
    payloadSummary:
      type: string
      description: Truncated payload (if includePayloadSummary enabled)
```

### 3.5 Authority Heartbeat

Authorities must maintain a heartbeat to keep their authority:

```yaml
# POST /mapping/authority-heartbeat
AuthorityHeartbeatRequest:
  type: object
  required: [channelId, authorityToken]
  properties:
    channelId:
      type: string
      format: uuid
    authorityToken:
      type: string
    # No other fields - just "I'm still here"

AuthorityHeartbeatResponse:
  type: object
  properties:
    valid:
      type: boolean
      description: Whether your authority is still valid
    expiresAt:
      type: string
      format: date-time
      description: Updated expiration time
    warning:
      type: string
      description: Optional warning (e.g., "authority expiring soon")
```

If heartbeat is missed, authority can be:
1. **Extended grace period** (30 seconds) - allows recovery from transient failures
2. **Released** - authority becomes unassigned
3. **Transferred** - if another app requested authority during grace period

---

## 4. Map Kinds (The Taxonomy)

Maps are differentiated by **kind** - the type of spatial data they contain. Each kind has different:
- Update frequency
- Storage format
- Consumer patterns
- Authority model

### 3.1 Map Kind Enumeration

```yaml
MapKind:
  type: string
  description: The category of spatial data this map contains
  enum:
    # === STATIC (rarely changes) ===
    - terrain           # Heightmaps, biome types, base geography
    - static_geometry   # Permanent structures (buildings, large rocks, cliffs)
    - navigation        # Navmesh data, pathfinding hints, chokepoints

    # === SEMI-STATIC (changes on authoring, not runtime) ===
    - resources         # Harvestable node positions (trees, ore, herbs)
    - spawn_points      # NPC/creature spawn locations
    - points_of_interest # Quest markers, landmarks, fast travel points

    # === DYNAMIC (changes at runtime) ===
    - dynamic_objects   # Movable/destructible objects (crates, doors, bridges)
    - hazards           # Damage zones (fire, radiation, poison gas)
    - weather_effects   # Localized weather (fog banks, rain cells)
    - ownership         # Territory control, claimed areas

    # === EPHEMERAL (short-lived, high-frequency) ===
    - combat_effects    # Temporary combat zones (AoE markers, ability zones)
    - visual_effects    # Particle effect triggers (explosions, magic)
```

### 3.2 Kind Properties

| Kind | Persistence | Authority | Update Freq | Typical Format |
|------|-------------|-----------|-------------|----------------|
| `terrain` | Durable | Authoring tools | Rarely | Texture (heightmap) |
| `static_geometry` | Durable | Authoring tools | Rarely | Metadata objects |
| `navigation` | Durable | Authoring tools | On bake | Tree (quadtree) |
| `resources` | Durable | Authoring tools | On edit | Metadata objects |
| `spawn_points` | Durable | Authoring tools | On edit | Metadata objects |
| `points_of_interest` | Durable | Authoring tools | On edit | Metadata objects |
| `dynamic_objects` | Cache (TTL) | Game server | Frequent | Metadata objects |
| `hazards` | Cache (TTL) | Game server | Frequent | Sparse dict |
| `weather_effects` | Cache (TTL) | Game server | Moderate | Sparse dict |
| `ownership` | Durable | Game server | On change | Sparse dict |
| `combat_effects` | Cache (short) | Game server | Very high | Sparse dict |
| `visual_effects` | Cache (short) | Game server | Very high | Sparse dict |

### 3.3 Subscription Patterns by Consumer

**Event Actors** typically subscribe to:
- `terrain` - Need to know geography for event planning
- `static_geometry` - Need to know buildings/rocks for positioning
- `resources` - May trigger events when resources depleted
- `ownership` - Ownership changes may trigger political events

**Event Actors** typically DON'T subscribe to:
- `combat_effects` - Too ephemeral, not useful for planning
- `visual_effects` - Purely cosmetic

**Game Servers** typically subscribe to:
- Cross-region `terrain` and `static_geometry` for distant LOD
- `ownership` for cross-region visibility of territory

**Design Tools** read/write all kinds during authoring sessions.

---

## 5. Topic Routing Pattern

### 5.1 Topic Structure

Events follow this topic pattern:
```
map.{regionId}.{kind}.{action}
```

Where:
- `regionId` - UUID of the region (natural partition key)
- `kind` - One of the MapKind values
- `action` - What happened: `updated`, `created`, `deleted`, `snapshot`

### 5.2 Topic Examples

```
# Terrain updated in region
map.550e8400-e29b-41d4-a716-446655440001.terrain.updated

# Static geometry snapshot (full dump)
map.550e8400-e29b-41d4-a716-446655440001.static_geometry.snapshot

# Hazard zone created
map.550e8400-e29b-41d4-a716-446655440001.hazards.created

# Ownership changed
map.550e8400-e29b-41d4-a716-446655440001.ownership.updated
```

### 5.3 Wildcard Subscriptions

Consumers can use RabbitMQ topic wildcards:

```
# All updates for a specific region (Event Actor pattern)
map.550e8400-e29b-41d4-a716-446655440001.*.*

# All static_geometry updates across all regions (rare, expensive)
map.*.static_geometry.*

# All snapshot events (for cold start/recovery)
map.*.*.snapshot
```

### 5.4 Subscription Efficiency

**Good patterns** (efficient):
```csharp
// Event actor subscribes to specific region + kinds it cares about
await _subscriber.SubscribeDynamicAsync<MapUpdatedEvent>(
    $"map.{regionId}.terrain.updated", handler);
await _subscriber.SubscribeDynamicAsync<MapUpdatedEvent>(
    $"map.{regionId}.static_geometry.updated", handler);
```

**Bad patterns** (expensive, avoid):
```csharp
// DON'T subscribe to all regions - scales poorly
await _subscriber.SubscribeDynamicAsync<MapUpdatedEvent>(
    "map.*.*.updated", handler); // AVOID
```

---

## 6. Schema-less Payloads ("Bags of Data")

### 6.1 Why Schema-less?

Map objects change constantly as game design evolves. Trying to maintain strict schemas for every object type would:
- Create endless schema churn
- Force regeneration for every game design change
- Couple lib-mapping to game-specific types it shouldn't know about

**The solution**: lib-mapping treats payloads as opaque JSON blobs. Only the publisher (game server) and consumer (actors) care about the structure. lib-mapping just:
- Stores the blob
- Broadcasts the blob
- Indexes by position/bounds for queries

### 6.2 Payload Structure

All payloads follow this pattern:

```yaml
MapPayload:
  type: object
  description: |
    Schema-less payload. Only envelope fields are validated.
    The 'data' field can contain ANYTHING the publisher wants.
  required: [objectType]
  properties:
    objectType:
      type: string
      description: Publisher-defined type (for filtering, not validation)
    position:
      $ref: '#/components/schemas/Position3D'
      description: Optional - for point objects
    bounds:
      $ref: '#/components/schemas/Bounds'
      description: Optional - for area objects
    data:
      type: object
      additionalProperties: true
      description: |
        ANYTHING. lib-mapping does not validate this.
        Only publisher and consumer care about structure.
```

### 6.3 Concrete Payload Examples

Below are EXAMPLES of payloads. These are NOT schemas - game design can change them freely:

#### Terrain Example
```json
{
  "objectType": "terrain_cell",
  "position": { "x": 1024, "y": 0, "z": 512 },
  "data": {
    "biome": "temperate_forest",
    "elevation": 127.5,
    "slope": 0.15,
    "moisture": 0.7,
    "surface_material": "grass_dirt_mix",
    "vegetation_density": 0.6,
    "traversability": {
      "foot": 1.0,
      "mount": 0.9,
      "vehicle": 0.3
    }
  }
}
```

#### Static Geometry Example (Boulder Cluster)
```json
{
  "objectType": "boulder_cluster",
  "position": { "x": 2048, "y": 15, "z": 1024 },
  "bounds": {
    "min": { "x": 2040, "y": 10, "z": 1016 },
    "max": { "x": 2056, "y": 25, "z": 1032 }
  },
  "data": {
    "boulder_count": 5,
    "provides_cover": true,
    "cover_rating": 0.8,
    "sightline_blockage": 0.6,
    "climbable": false,
    "destructible": false,
    "visual_prefab": "boulder_cluster_medium_01",
    "collision_mesh": "boulder_cluster_medium_01_col"
  }
}
```

#### Dynamic Object Example (Destructible Crate)
```json
{
  "objectType": "destructible_crate",
  "position": { "x": 500, "y": 2, "z": 300 },
  "data": {
    "health": 50,
    "max_health": 100,
    "damage_type_vulnerabilities": {
      "physical": 1.0,
      "fire": 1.5,
      "magic": 0.5
    },
    "loot_table_id": "crate_common_supplies",
    "interaction_prompt": "Break crate",
    "visual_state": "damaged",
    "physics_enabled": true
  }
}
```

#### Hazard Example (Fire Zone)
```json
{
  "objectType": "hazard_fire",
  "bounds": {
    "min": { "x": 800, "y": 0, "z": 400 },
    "max": { "x": 850, "y": 10, "z": 450 }
  },
  "data": {
    "damage_per_second": 15,
    "damage_type": "fire",
    "intensity": 0.8,
    "spread_rate": 0.1,
    "fuel_remaining_seconds": 45,
    "ignition_source": "spell_fireball",
    "igniter_character_id": "abc123-def456",
    "visual_intensity": "high",
    "smoke_column": true,
    "blocks_los": true
  }
}
```

#### Spawn Point Example
```json
{
  "objectType": "creature_spawn",
  "position": { "x": 3000, "y": 5, "z": 2000 },
  "data": {
    "creature_type": "wolf_pack",
    "spawn_count_min": 3,
    "spawn_count_max": 6,
    "respawn_delay_seconds": 300,
    "active_time_range": {
      "start_hour": 20,
      "end_hour": 6
    },
    "patrol_waypoints": [
      { "x": 3000, "y": 5, "z": 2000 },
      { "x": 3100, "y": 8, "z": 2100 },
      { "x": 2900, "y": 3, "z": 2050 }
    ],
    "aggression_level": "territorial",
    "level_range": { "min": 5, "max": 8 },
    "enabled": true
  }
}
```

#### Point of Interest Example
```json
{
  "objectType": "poi_landmark",
  "position": { "x": 5000, "y": 100, "z": 5000 },
  "data": {
    "name": "The Watchman's Tower",
    "description": "An ancient watchtower overlooking the valley",
    "discovery_radius": 50,
    "map_icon": "tower_ruins",
    "visibility_distance": 500,
    "associated_quests": ["quest_valley_mystery", "quest_old_kingdom"],
    "fast_travel_enabled": true,
    "fast_travel_unlock_condition": "discovered",
    "ambient_sounds": ["wind_howling", "creaking_wood"],
    "lore_entry_id": "lore_watchmans_tower"
  }
}
```

#### Ownership Zone Example
```json
{
  "objectType": "territory_claim",
  "bounds": {
    "min": { "x": 10000, "y": 0, "z": 10000 },
    "max": { "x": 11000, "y": 500, "z": 11000 }
  },
  "data": {
    "owner_type": "guild",
    "owner_id": "guild-12345",
    "owner_name": "The Iron Wolves",
    "claim_established_at": "2026-01-01T00:00:00Z",
    "claim_expires_at": "2026-02-01T00:00:00Z",
    "protection_level": "full",
    "tax_rate": 0.05,
    "allowed_actions": {
      "guild_members": ["build", "harvest", "enter"],
      "allies": ["enter", "harvest"],
      "others": ["enter"]
    },
    "contested": false,
    "siege_status": null
  }
}
```

#### Combat Effect Example
```json
{
  "objectType": "aoe_ability",
  "bounds": {
    "min": { "x": 1500, "y": 0, "z": 1500 },
    "max": { "x": 1520, "y": 20, "z": 1520 }
  },
  "data": {
    "ability_id": "blizzard",
    "caster_character_id": "char-789",
    "effect_type": "damage_over_time",
    "damage_per_tick": 25,
    "tick_interval_ms": 500,
    "remaining_duration_ms": 8000,
    "slow_percentage": 40,
    "visual_effect": "blizzard_aoe_large",
    "affects": ["enemies", "neutrals"],
    "created_at": "2026-01-07T12:34:56.789Z"
  }
}
```

#### Weather Effect Example
```json
{
  "objectType": "weather_cell",
  "bounds": {
    "min": { "x": 0, "y": 0, "z": 0 },
    "max": { "x": 2000, "y": 500, "z": 2000 }
  },
  "data": {
    "weather_type": "thunderstorm",
    "intensity": 0.7,
    "wind_direction": { "x": 0.8, "y": 0, "z": 0.6 },
    "wind_speed": 25,
    "precipitation_rate": "heavy",
    "visibility_reduction": 0.4,
    "lightning_frequency": "frequent",
    "movement_speed_modifier": -0.15,
    "fire_extinguish_chance": 0.8,
    "audio_preset": "storm_intense"
  }
}
```

#### Resource Node Example
```json
{
  "objectType": "harvestable_ore",
  "position": { "x": 4500, "y": -10, "z": 3200 },
  "data": {
    "resource_type": "iron_ore",
    "resource_quality": "rich",
    "current_yield": 150,
    "max_yield": 200,
    "respawn_time_hours": 24,
    "required_skill": "mining",
    "required_skill_level": 15,
    "required_tool": "pickaxe",
    "harvest_time_seconds": 5,
    "depleted": false,
    "last_harvested_at": null,
    "visual_state": "full"
  }
}
```

#### Navigation Hint Example
```json
{
  "objectType": "nav_chokepoint",
  "position": { "x": 6000, "y": 50, "z": 4000 },
  "bounds": {
    "min": { "x": 5990, "y": 45, "z": 3990 },
    "max": { "x": 6010, "y": 55, "z": 4010 }
  },
  "data": {
    "chokepoint_type": "bridge",
    "width": 3,
    "tactical_value": "high",
    "ambush_rating": 0.9,
    "can_be_blocked": true,
    "alternate_routes": [
      { "via": "ford_downstream", "cost_multiplier": 2.5 },
      { "via": "mountain_pass", "cost_multiplier": 4.0 }
    ],
    "control_value": 85
  }
}
```

---

## 7. Event Schemas

### 7.1 Core Events

```yaml
# mapping-events.yaml

MapUpdatedEvent:
  type: object
  description: |
    Published when map layer data changes.
    The 'payload' field is SCHEMA-LESS - only objectType is guaranteed.
  required: [eventId, timestamp, regionId, kind]
  properties:
    eventId:
      type: string
      format: uuid
    timestamp:
      type: string
      format: date-time
    regionId:
      type: string
      format: uuid
      description: Region containing this map data
    kind:
      $ref: '#/components/schemas/MapKind'
    bounds:
      $ref: '#/components/schemas/Bounds'
      description: Affected area (for partial updates, null = entire region)
    version:
      type: integer
      format: int64
      description: Monotonic version for ordering
    deltaType:
      type: string
      enum: [delta, snapshot]
      description: Whether this is incremental or full
    sourceAppId:
      type: string
      description: App-id of publisher (game server, editor, etc.)
    authorityToken:
      type: string
      description: Token proving publisher authority (validated by lib-mapping)
    payload:
      type: object
      additionalProperties: true
      description: |
        SCHEMA-LESS. Can contain anything.
        Only 'objectType' field is expected (for filtering).
        lib-mapping does not validate contents.
    payloadRef:
      type: string
      description: Asset reference for large payloads (lib-asset URL)

MapObjectsChangedEvent:
  type: object
  description: Published when metadata objects change in a map
  required: [eventId, timestamp, regionId, kind, changes]
  properties:
    eventId:
      type: string
      format: uuid
    timestamp:
      type: string
      format: date-time
    regionId:
      type: string
      format: uuid
    kind:
      $ref: '#/components/schemas/MapKind'
    changes:
      type: array
      items:
        type: object
        required: [objectId, action]
        properties:
          objectId:
            type: string
            format: uuid
          action:
            type: string
            enum: [created, updated, deleted]
          objectType:
            type: string
          position:
            $ref: '#/components/schemas/Position3D'
          state:
            type: object
            additionalProperties: true
            description: Object state (for created/updated)

MapSnapshotRequestedEvent:
  type: object
  description: Published when a consumer needs full snapshot (cold start)
  required: [eventId, timestamp, requesterId, regionId, kinds]
  properties:
    eventId:
      type: string
      format: uuid
    timestamp:
      type: string
      format: date-time
    requesterId:
      type: string
      description: App-id of requester
    regionId:
      type: string
      format: uuid
    kinds:
      type: array
      items:
        $ref: '#/components/schemas/MapKind'
      description: Which kinds to snapshot
```

### 5.2 Perception Events (Game Server → NPC Actors)

NPC actors receive spatial context through perception events, NOT direct map subscriptions:

```yaml
# Already defined in actor-events.yaml, but showing spatial aspects:

CharacterPerceptionEvent:
  type: object
  required: [characterId, perceptionType, timestamp]
  properties:
    characterId:
      type: string
      format: uuid
    perceptionType:
      type: string
      enum: [visual, auditory, tactile, olfactory, proprioceptive, spatial]
    timestamp:
      type: string
      format: date-time

    # Spatial context included in perception
    spatialContext:
      type: object
      description: Map-derived context relevant to this perception
      properties:
        terrainType:
          type: string
          description: What's underfoot (grass, stone, water, etc.)
        nearbyObjects:
          type: array
          items:
            type: object
            properties:
              objectId: { type: string, format: uuid }
              objectType: { type: string }
              distance: { type: number }
              direction: { type: string, enum: [north, south, east, west, above, below] }
        hazardsInRange:
          type: array
          items:
            type: object
            properties:
              hazardType: { type: string }
              distance: { type: number }
              severity: { type: number, minimum: 0, maximum: 1 }
        pathableDirections:
          type: array
          items:
            type: string
            enum: [north, south, east, west]
```

---

## 8. API Design

### 8.1 API Categories

The Mapping service provides three API categories:

1. **Authoring APIs** - For design tools, with checkout/commit locking
2. **Runtime APIs** - For game servers, direct publish without locking
3. **Query APIs** - For any consumer, read-only spatial queries

### 8.2 Runtime APIs (Game Server → Bannou)

These are the PRIMARY APIs for live game operation:

```yaml
# mapping-api.yaml

paths:
  /mapping/publish:
    post:
      operationId: PublishMapUpdate
      summary: Publish map data update (game server authority)
      description: |
        Game servers use this to push authoritative spatial updates.
        No checkout required - game server IS the authority.
        Publishes MapUpdatedEvent to relevant topic.
      tags: [Runtime]
      x-permissions:
        - role: game_server
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/PublishMapUpdateRequest'
      responses:
        '200':
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/PublishMapUpdateResponse'

  /mapping/publish-objects:
    post:
      operationId: PublishObjectChanges
      summary: Publish metadata object changes
      description: |
        Game servers use this to push object state changes.
        Efficiently batches multiple object changes into one event.
      tags: [Runtime]
      x-permissions:
        - role: game_server
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/PublishObjectChangesRequest'
      responses:
        '200':
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/PublishObjectChangesResponse'

  /mapping/request-snapshot:
    post:
      operationId: RequestSnapshot
      summary: Request full snapshot for cold start
      description: |
        Consumers use this when starting up to get initial state.
        Triggers MapSnapshotRequestedEvent, authoritative source responds.
      tags: [Runtime]
      x-permissions:
        - role: user
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/RequestSnapshotRequest'
      responses:
        '200':
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/RequestSnapshotResponse'

components:
  schemas:
    PublishMapUpdateRequest:
      type: object
      required: [regionId, kind, payload]
      properties:
        regionId:
          type: string
          format: uuid
        kind:
          $ref: '#/components/schemas/MapKind'
        bounds:
          $ref: '#/components/schemas/Bounds'
          description: Affected area (null = entire region)
        deltaType:
          type: string
          enum: [delta, snapshot]
          default: delta
        payload:
          type: object
          additionalProperties: true
          description: Kind-specific payload
        payloadAsset:
          type: string
          description: For large payloads, asset ID from lib-asset

    PublishObjectChangesRequest:
      type: object
      required: [regionId, kind, changes]
      properties:
        regionId:
          type: string
          format: uuid
        kind:
          $ref: '#/components/schemas/MapKind'
        changes:
          type: array
          maxItems: 100
          items:
            type: object
            required: [objectId, action]
            properties:
              objectId:
                type: string
                format: uuid
              action:
                type: string
                enum: [created, updated, deleted]
              objectType:
                type: string
              position:
                $ref: '#/components/schemas/Position3D'
              state:
                type: object
                additionalProperties: true
```

### 8.3 Query APIs (Read-Only)

```yaml
paths:
  /mapping/query/point:
    post:
      operationId: QueryPoint
      summary: Query map data at a specific point
      description: |
        Returns all map data at a point across requested kinds.
        Used by behavior stacks for contextual decisions.
      tags: [Query]
      x-permissions:
        - role: user
      requestBody:
        required: true
        content:
          application/json:
            schema:
              type: object
              required: [regionId, position]
              properties:
                regionId:
                  type: string
                  format: uuid
                position:
                  $ref: '#/components/schemas/Position3D'
                kinds:
                  type: array
                  items:
                    $ref: '#/components/schemas/MapKind'
                  description: Kinds to query (default all)
                radius:
                  type: number
                  description: Include objects within this radius

  /mapping/query/bounds:
    post:
      operationId: QueryBounds
      summary: Query map data within bounds
      description: |
        Returns map data within a bounding box.
        For event actors needing region overview.
      tags: [Query]
      x-permissions:
        - role: user
      requestBody:
        required: true
        content:
          application/json:
            schema:
              type: object
              required: [regionId, bounds]
              properties:
                regionId:
                  type: string
                  format: uuid
                bounds:
                  $ref: '#/components/schemas/Bounds'
                kinds:
                  type: array
                  items:
                    $ref: '#/components/schemas/MapKind'
                maxObjects:
                  type: integer
                  default: 500
                  maximum: 5000

  /mapping/query/objects-by-type:
    post:
      operationId: QueryObjectsByType
      summary: Find all objects of a type in region
      description: |
        Returns all objects matching a type filter.
        For event actors: "where are all the boulder clusters?"
      tags: [Query]
      x-permissions:
        - role: user
      requestBody:
        required: true
        content:
          application/json:
            schema:
              type: object
              required: [regionId, objectType]
              properties:
                regionId:
                  type: string
                  format: uuid
                objectType:
                  type: string
                bounds:
                  $ref: '#/components/schemas/Bounds'
                  description: Optional bounds filter
```

### 8.4 Authoring APIs (Design Tools)

The existing checkout/commit model from the original document applies here for design-time editing. These APIs are used by level editors, not game servers.

```yaml
paths:
  /mapping/authoring/checkout:
    post:
      operationId: CheckoutForAuthoring
      summary: Acquire exclusive edit lock for design-time editing
      description: |
        For level editors and design tools only.
        Game servers do NOT use this - they have implicit authority.
      tags: [Authoring]
      x-permissions:
        - role: developer
      # ... (checkout/commit pattern from original doc)

  /mapping/authoring/commit:
    post:
      operationId: CommitAuthoring
      summary: Commit design-time changes
      tags: [Authoring]
      x-permissions:
        - role: developer
      # ...
```

### 8.5 Authority Management APIs

```yaml
paths:
  /mapping/create-channel:
    post:
      operationId: CreateChannel
      summary: Create a new map channel and become its authority
      description: |
        Creates a new region+kind channel and grants authority to caller.
        Returns ingestTopic for high-throughput event publishing.
      tags: [Authority]
      x-permissions:
        - role: game_server
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/CreateChannelRequest'
      responses:
        '200':
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/AuthorityGrant'

  /mapping/release-authority:
    post:
      operationId: ReleaseAuthority
      summary: Release authority over a channel
      description: |
        Voluntarily releases authority. Channel becomes unassigned
        or reverts to previous authority.
      tags: [Authority]
      x-permissions:
        - role: game_server
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/ReleaseAuthorityRequest'

  /mapping/authority-heartbeat:
    post:
      operationId: AuthorityHeartbeat
      summary: Maintain authority over channel
      description: |
        Keep-alive for authority. Must be called periodically.
        Failure to heartbeat results in authority expiration.
      tags: [Authority]
      x-permissions:
        - role: game_server
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/AuthorityHeartbeatRequest'
      responses:
        '200':
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/AuthorityHeartbeatResponse'

components:
  schemas:
    CreateChannelRequest:
      type: object
      required: [regionId, kind]
      properties:
        regionId:
          type: string
          format: uuid
        kind:
          $ref: '#/components/schemas/MapKind'
        initialSnapshot:
          type: array
          items:
            type: object
            additionalProperties: true
          description: Optional initial data to populate channel

    ReleaseAuthorityRequest:
      type: object
      required: [channelId, authorityToken]
      properties:
        channelId:
          type: string
          format: uuid
        authorityToken:
          type: string
```

---

## 9. Affordance Query APIs (Event Brain Integration)

From [THE_DREAM.md](./THE_DREAM.md), Event Brain needs to ask spatial questions like "where are good places for an ambush?" or "find locations suitable for a dramatic reveal." These are **affordance queries** - they return locations based on what actions/scenes they afford.

### 9.1 Affordance Query Design

Affordance queries don't ask "what's here?" (that's a regular query). They ask "where can I do X?" The answer combines multiple map kinds and applies game-specific logic.

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        AFFORDANCE QUERY FLOW                                 │
│                                                                              │
│  Event Brain needs: "Where can I stage an ambush?"                          │
│                                                                              │
│  ┌──────────────┐   POST /mapping/query/affordance   ┌─────────────┐       │
│  │ Event Actor  │ ───────────────────────────────────▶│ lib-mapping │       │
│  │              │   { affordance: "ambush",          │             │       │
│  │              │     regionId, constraints }        └──────┬──────┘       │
│  └──────────────┘                                            │              │
│                                                              │              │
│                                              Query static_geometry          │
│                                              + terrain + navigation          │
│                                              + hazards                       │
│                                                              │              │
│                                                              ▼              │
│                                              ┌──────────────────────────┐   │
│                                              │ Apply affordance logic:  │   │
│                                              │ - cover_rating > 0.6     │   │
│                                              │ - sightline_blockage OK  │   │
│                                              │ - pathable approach      │   │
│                                              │ - no active hazards      │   │
│                                              │ - tactical_value > med   │   │
│                                              └───────────┬──────────────┘   │
│                                                          │                  │
│         ◄────────────────────────────────────────────────┘                  │
│         { locations: [ { position, score, features }, ... ] }               │
│                                                                              │
│  Event Brain can now spawn ambush at best location                          │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 9.2 Affordance Query API

```yaml
paths:
  /mapping/query/affordance:
    post:
      operationId: QueryAffordance
      summary: Find locations that afford a specific action or scene type
      description: |
        Affordance queries answer "where can I do X?" by combining
        multiple map kinds and applying game-specific logic.

        Used by Event Brain for procedural scene orchestration:
        - "Find ambush locations"
        - "Find dramatic reveal spots"
        - "Find sheltered rest areas"
        - "Find choke points for defense"
      tags: [Query]
      x-permissions:
        - role: user
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/AffordanceQueryRequest'
      responses:
        '200':
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/AffordanceQueryResponse'

components:
  schemas:
    AffordanceQueryRequest:
      type: object
      required: [regionId, affordanceType]
      properties:
        regionId:
          type: string
          format: uuid
        affordanceType:
          type: string
          description: |
            The type of affordance to search for.
            Well-known types: ambush, shelter, vista, choke_point,
            gathering_spot, dramatic_reveal, hidden_path, defensible_position
          enum:
            - ambush
            - shelter
            - vista
            - choke_point
            - gathering_spot
            - dramatic_reveal
            - hidden_path
            - defensible_position
            - custom
        customAffordance:
          type: object
          additionalProperties: true
          description: |
            For affordanceType=custom, define the criteria:
            { "requires": { "cover_rating": { "min": 0.5 }, "terrain": ["grass", "stone"] },
              "excludes": { "hazards": true, "contested": true } }
        bounds:
          $ref: '#/components/schemas/Bounds'
          description: Optional bounds to search within
        maxResults:
          type: integer
          default: 10
          maximum: 100
        minScore:
          type: number
          minimum: 0
          maximum: 1
          default: 0.5
          description: Minimum affordance score (0-1) to include in results
        participantCount:
          type: integer
          description: Expected number of participants (affects space requirements)
        excludePositions:
          type: array
          items:
            $ref: '#/components/schemas/Position3D'
          description: Positions to exclude (e.g., player's current location)
        freshness:
          $ref: '#/components/schemas/AffordanceFreshness'
          description: |
            How fresh the affordance data needs to be. Actors can configure
            default freshness levels, and behaviors can override per-query.
        maxAgeSeconds:
          type: integer
          minimum: 0
          maximum: 3600
          description: |
            For freshness=cached or aggressive_cache, maximum age of cached
            results in seconds. Ignored for freshness=fresh.

    AffordanceFreshness:
      type: string
      description: |
        Controls caching behavior for affordance queries. Actors can configure
        default freshness levels in their configuration, and individual queries
        can override the default.
      enum:
        - fresh              # Always query live data (highest latency, most accurate)
        - cached             # Use cache if within maxAgeSeconds (balanced)
        - aggressive_cache   # Return stale data immediately, refresh async (lowest latency)
      default: cached

    AffordanceQueryResponse:
      type: object
      properties:
        locations:
          type: array
          items:
            type: object
            properties:
              position:
                $ref: '#/components/schemas/Position3D'
              bounds:
                $ref: '#/components/schemas/Bounds'
                description: Area bounds if affordance spans an area
              score:
                type: number
                minimum: 0
                maximum: 1
                description: How well this location affords the requested action
              features:
                type: object
                additionalProperties: true
                description: |
                  What makes this location suitable:
                  { "cover_rating": 0.8, "sightlines": ["north", "east"],
                    "approach_routes": 2, "terrain": "rocky_outcrop" }
              objectIds:
                type: array
                items:
                  type: string
                  format: uuid
                description: Map objects that contribute to this affordance
        queryMetadata:
          type: object
          properties:
            kindsSearched:
              type: array
              items:
                type: string
            objectsEvaluated:
              type: integer
            searchDurationMs:
              type: integer
```

### 9.3 Well-Known Affordance Types

| Affordance | Description | Key Factors |
|------------|-------------|-------------|
| `ambush` | Good location for surprise attack | cover_rating, sightline_blockage, approach_routes, choke_proximity |
| `shelter` | Protected resting location | roof_coverage, wind_protection, no_hazards, warmth |
| `vista` | Scenic overlook or observation point | elevation, visibility_distance, unobstructed_view |
| `choke_point` | Narrow passage for defense | width, alternate_routes, tactical_value |
| `gathering_spot` | Space for group assembly | flat_terrain, size, accessibility |
| `dramatic_reveal` | Location for story moment | elevation_change, vista_score, approach_suspense |
| `hidden_path` | Concealed route | vegetation_cover, terrain_concealment, patrol_avoidance |
| `defensible_position` | Location to hold against attack | cover_rating, elevation, escape_routes, supplies_nearby |

### 9.4 Custom Affordance Example

Event Brain can define custom affordances for novel situations:

```json
{
  "regionId": "550e8400-e29b-41d4-a716-446655440001",
  "affordanceType": "custom",
  "customAffordance": {
    "description": "romantic_overlook",
    "requires": {
      "objectTypes": ["vista_point", "scenic_overlook"],
      "terrain": ["grass", "flowers"],
      "elevation": { "min": 50 },
      "visibility_distance": { "min": 200 },
      "hazards": false,
      "contested": false
    },
    "prefers": {
      "sunset_facing": true,
      "shelter_nearby": true,
      "water_view": true
    },
    "excludes": {
      "creature_spawns_nearby": 100,
      "high_traffic": true
    }
  },
  "maxResults": 5,
  "participantCount": 2
}
```

Response:
```json
{
  "locations": [
    {
      "position": { "x": 5000, "y": 150, "z": 4800 },
      "score": 0.92,
      "features": {
        "vista_type": "cliff_edge",
        "terrain": "grass_flowers",
        "sunset_facing": true,
        "water_view": "lake_panorama",
        "shelter_distance": 25,
        "privacy_rating": 0.85
      },
      "objectIds": ["poi-vista-001", "terrain-cliff-edge-42"]
    },
    {
      "position": { "x": 6200, "y": 80, "z": 5100 },
      "score": 0.78,
      "features": {
        "vista_type": "hilltop_meadow",
        "terrain": "wildflowers",
        "sunset_facing": true,
        "water_view": null,
        "shelter_distance": 150,
        "privacy_rating": 0.70
      },
      "objectIds": ["poi-meadow-003"]
    }
  ]
}
```

### 9.5 Affordance System Architecture

> **Research Note**: This architecture is based on industry standards including Unreal Engine's Environment Query System (EQS), CryEngine's Tactical Point System, and Utility AI scoring patterns documented in Game AI Pro.

The game industry has converged on a **composable pipeline architecture** for spatial queries. lib-mapping adopts this proven pattern:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    AFFORDANCE QUERY PIPELINE                                 │
│                                                                              │
│  ┌──────────────┐   ┌──────────────┐   ┌──────────────┐   ┌─────────────┐  │
│  │  GENERATORS  │ → │    TESTS     │ → │   SCORING    │ → │  SELECTION  │  │
│  └──────────────┘   └──────────────┘   └──────────────┘   └─────────────┘  │
│                                                                              │
│  Create candidate     Evaluate each      Normalize and      Pick winner(s)  │
│  sample points        point's fitness    combine scores     from ranked     │
│                                                             candidates      │
└─────────────────────────────────────────────────────────────────────────────┘
```

#### 9.5.1 Pipeline Components

| Component | Purpose | Implementation |
|-----------|---------|----------------|
| **Generators** | Create candidate points for evaluation | Object positions, grid samples, ring patterns, navmesh points |
| **Tests** | Evaluate each point against criteria | Distance, LOS, pathability, property checks, hazard detection |
| **Scoring** | Normalize results to 0-1 and combine with weights | Weighted sum, with required tests as filters |
| **Selection** | Choose final result(s) | Highest score, Top N, weighted random from top percentile |

#### 9.5.2 Generator Types

```yaml
AffordanceGenerator:
  type: string
  enum:
    - object_positions     # Use existing map objects as candidates
    - grid                 # Uniform grid across bounds
    - ring                 # Concentric rings around center
    - navmesh_samples      # Points on navigation mesh
    - custom_points        # Caller-provided positions
```

**Generator configuration examples:**
```yaml
# Object-based: Use boulder clusters as candidate ambush points
generator:
  type: object_positions
  objectTypes: ["boulder_cluster", "dense_vegetation", "ruined_wall"]

# Grid-based: Sample entire region uniformly
generator:
  type: grid
  spacing: 10           # meters between samples
  snapToNavmesh: true   # Only pathable locations

# Ring-based: Find positions at specific distance from player
generator:
  type: ring
  center: "${player.position}"
  minRadius: 20
  maxRadius: 50
  ringCount: 3
  pointsPerRing: 12
```

#### 9.5.3 Test Types

Tests evaluate candidates and produce scores (0.0 to 1.0):

```yaml
AffordanceTest:
  type: object
  required: [test, weight]
  properties:
    test:
      type: string
      description: Test type to apply
      enum:
        # Spatial tests
        - has_cover             # Cover rating from map object properties
        - clear_sightline       # LOS to target/direction
        - pathable              # Can reach via navigation
        - distance_to           # Distance scoring
        - elevation             # Height-based scoring

        # Property tests
        - property_check        # Evaluate map object property
        - no_hazards            # No active hazards in radius
        - terrain_type          # Match terrain types

        # Composite tests
        - chokepoint_proximity  # Near tactical chokepoints
        - escape_routes         # Multiple exit paths available
    weight:
      type: number
      minimum: 0
      maximum: 1
      description: Contribution to final score
    required:
      type: boolean
      default: false
      description: If true, candidate is rejected if test fails (score < threshold)
    threshold:
      type: number
      default: 0.0
      description: Minimum score for required tests
    parameters:
      type: object
      additionalProperties: true
      description: Test-specific parameters
```

#### 9.5.4 Well-Known Affordance Definitions

Each well-known affordance type is a **preset combination of generator, tests, and weights**:

```yaml
# Internal lib-mapping presets (not user-configurable, shown for documentation)
affordance_presets:
  ambush:
    generator:
      type: object_positions
      objectTypes: ["boulder_cluster", "dense_vegetation", "ruined_wall"]
    tests:
      - test: has_cover
        weight: 0.30
        parameters: { minRating: 0.5 }
      - test: clear_sightline
        weight: 0.25
        parameters: { direction: "approach_path" }
      - test: pathable
        weight: 0.15
        required: true
      - test: no_hazards
        weight: 0.15
        required: true
        parameters: { radius: 10 }
      - test: chokepoint_proximity
        weight: 0.15
        parameters: { maxDistance: 30 }

  shelter:
    generator:
      type: object_positions
      objectTypes: ["structure", "cave_entrance", "overhang", "dense_canopy"]
    tests:
      - test: property_check
        weight: 0.35
        parameters: { property: "roof_coverage", minValue: 0.7 }
      - test: no_hazards
        weight: 0.25
        required: true
      - test: escape_routes
        weight: 0.20
        parameters: { minRoutes: 1 }
      - test: property_check
        weight: 0.20
        parameters: { property: "wind_protection", minValue: 0.5 }

  vista:
    generator:
      type: object_positions
      objectTypes: ["vista_point", "cliff_edge", "tower", "hilltop"]
    tests:
      - test: elevation
        weight: 0.35
        parameters: { preferHigher: true }
      - test: property_check
        weight: 0.35
        parameters: { property: "visibility_distance", minValue: 100 }
      - test: pathable
        weight: 0.15
        required: true
      - test: no_hazards
        weight: 0.15
```

#### 9.5.5 Why This Architecture Works for Any Map Type

The affordance system is **generic** because:

1. **Schema-less object properties** - Tests query properties from map object payloads (`cover_rating`, `elevation`, etc.) without lib-mapping needing to understand game semantics

2. **Composable tests** - Simple building blocks combine into complex affordance definitions

3. **Weighted scoring** - Normalized 0-1 scores allow objective comparison across different test types

4. **Preset + Custom** - Well-known affordances provide convenience; custom affordances enable novel scenarios

### 9.6 Actor Capabilities (Gibsonian Affordances)

> **Gibson's Insight**: Affordances depend on BOTH the environment AND the actor's capabilities. A boulder affords "cover" to a humanoid but not to a giant. A cliff affords "dramatic reveal" only if there's something to reveal below.

To support true Gibsonian affordances, the query API accepts **actor capabilities** that modify how tests are evaluated:

```yaml
ActorCapabilities:
  type: object
  description: |
    Actor-specific capabilities that affect affordance evaluation.
    Same location may afford different actions to different actor types.
  properties:
    size:
      type: string
      enum: [tiny, small, medium, large, huge]
      default: medium
      description: |
        Affects cover requirements and passage width.
        - tiny/small: Can use small objects for cover
        - medium: Standard cover requirements
        - large/huge: Requires larger cover, may not fit in narrow passages
    height:
      type: number
      description: Actor height in meters (affects cover, sightlines)
    canClimb:
      type: boolean
      default: false
      description: Can reach elevated positions
    canSwim:
      type: boolean
      default: false
      description: Includes water-based positions
    canFly:
      type: boolean
      default: false
      description: Includes aerial positions
    perceptionRange:
      type: number
      description: Affects sightline distance requirements
    movementSpeed:
      type: number
      description: Affects escape route viability calculations
    stealthRating:
      type: number
      minimum: 0
      maximum: 1
      description: Affects ambush/hidden_path affordance scoring
```

**Updated AffordanceQueryRequest:**
```yaml
AffordanceQueryRequest:
  type: object
  required: [regionId, affordanceType]
  properties:
    # ... existing fields ...
    actorCapabilities:
      $ref: '#/components/schemas/ActorCapabilities'
      description: |
        Actor capabilities that affect affordance evaluation.
        Example: A "large" actor requires bigger cover objects.
```

**Example: Same Query, Different Results**

```json
// Query for a MEDIUM humanoid NPC
{
  "regionId": "...",
  "affordanceType": "ambush",
  "actorCapabilities": { "size": "medium", "height": 1.8 }
}
// Result: Boulder clusters, dense vegetation

// Query for a LARGE ogre
{
  "regionId": "...",
  "affordanceType": "ambush",
  "actorCapabilities": { "size": "large", "height": 3.5 }
}
// Result: Only large structures, cliff overhangs (small boulders filtered out)

// Query for a TINY fairy
{
  "regionId": "...",
  "affordanceType": "shelter",
  "actorCapabilities": { "size": "tiny", "canFly": true }
}
// Result: Includes flower canopies, small hollows, aerial perches
```

### 9.7 Affordance Caching and Freshness

Affordance queries can be expensive (evaluating hundreds of sample points), so lib-mapping supports caching with configurable freshness levels.

#### 9.7.1 Freshness Levels

| Level | Behavior | Latency | Use Case |
|-------|----------|---------|----------|
| `fresh` | Always query live | Highest | Combat starting NOW, need current positions |
| `cached` | Use cache if < maxAgeSeconds | Medium | Planning phase, 30-60 sec old data is acceptable |
| `aggressive_cache` | Return stale data, refresh async | Lowest | Background planning, staleness acceptable |

#### 9.7.2 Actor-Level Freshness Defaults

Actors can configure default freshness behavior. Individual queries can override the default:

```yaml
# Actor configuration (in actor schema or spawn parameters)
affordanceCacheConfig:
  defaultFreshness: cached
  defaultMaxAgeSeconds: 60
  precomputeAffordances:
    - ambush      # Pre-cache ambush locations on activation
    - shelter     # Pre-cache shelter locations on activation
  refreshOnMapUpdate: true   # Invalidate cache when map.*.updated received
```

#### 9.7.3 Query-Level Override

Behaviors can override the actor's default freshness per-query:

```yaml
# In ABML behavior
- service_call:
    service: mapping
    method: query-affordance
    parameters:
      affordance_type: "ambush"
      freshness: "fresh"        # Override: need live data NOW
      # maxAgeSeconds ignored when freshness=fresh
    result_variable: "ambush_points"
```

#### 9.7.4 Cache Invalidation

Caches are invalidated when:
1. **TTL expires** - maxAgeSeconds reached
2. **Map update received** - If actor subscribed to map.{regionId}.* and refreshOnMapUpdate=true
3. **Manual invalidation** - Actor explicitly clears cache (e.g., after major event)

For `aggressive_cache`, stale data is returned immediately AND a background refresh is triggered. The actor receives updated data on the next query.

#### 9.7.5 Precomputation

For frequently-used affordances, actors can precompute on activation:

```csharp
public override async Task OnActivateAsync(CancellationToken ct)
{
    // Precompute ambush and shelter locations
    await PrecomputeAffordancesAsync(["ambush", "shelter"], ct);

    // Subscribe to map updates to refresh cache
    await _subscriber.SubscribeDynamicAsync<MapUpdatedEvent>(
        $"map.{RegionId}.*.*",
        HandleMapUpdateAsync,
        cancellationToken: ct);
}

private async Task HandleMapUpdateAsync(MapUpdatedEvent evt, CancellationToken ct)
{
    // Invalidate cached affordances for affected kinds
    InvalidateAffordanceCache(evt.Kind);
}
```

### 9.8 Why ABML Cannot Be an Affordance Processor

> **Important Design Clarification**: ABML is the perfect *consumer* of affordance results, but it is NOT suitable as the affordance processor itself.

#### The Distinction

| Task | ABML Suitability | Why |
|------|------------------|-----|
| Define query parameters | ✅ Excellent | Expressions, variables, conditions |
| Call affordance service | ✅ Excellent | `service_call` action |
| Process results in behavior logic | ✅ Excellent | `for_each`, `cond`, flow control |
| **Evaluate 1000s of sample points** | ❌ Not suitable | Tree-walking interpreter, no spatial primitives |
| **Raycast/LOS calculations** | ❌ Not suitable | No built-in spatial operations |
| **Weighted score aggregation** | ❌ Awkward | Not designed for numerical processing |
| **Performance-critical iteration** | ❌ Not suitable | Not optimized for bulk computation |

#### ABML Design Philosophy

From [ABML.md](../guides/ABML.md):
- **"Intent-based"**: Express WHAT should happen, not HOW
- **"Handler-agnostic"**: Runtime interprets actions; ABML doesn't prescribe transport
- **"Not a programming language"**: No arbitrary computation

Affordance processing is **algorithmic heavy lifting** (iterating sample points, spatial calculations, score aggregation) that belongs in native C# code within lib-mapping.

#### The Correct Pattern

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    ABML + AFFORDANCE SYSTEM INTEGRATION                      │
│                                                                              │
│  ABML (Behavior Language)              lib-mapping (Native C#)              │
│  ─────────────────────────             ────────────────────────              │
│  • Defines query parameters            • Generators create sample points     │
│  • Calls affordance service            • Tests evaluate each point           │
│  • Receives ranked results             • Scoring combines test results       │
│  • Makes decisions based on results    • Returns ranked candidates           │
│  • Orchestrates follow-up actions                                            │
│                                                                              │
│  ┌──────────────┐   service_call    ┌─────────────┐   Native algorithm     │
│  │ ABML Behavior│ ─────────────────▶│ lib-mapping │ ─────────────────────▶  │
│  │              │                   │  Affordance │   Generator → Tests     │
│  │              │◀───────────────── │   Service   │ ←── → Scoring → Select  │
│  └──────────────┘   ranked results  └─────────────┘                         │
│         │                                                                    │
│         │ Use results in behavior logic                                      │
│         ▼                                                                    │
│  • spawn_encounter at best location                                          │
│  • set actor_destination                                                     │
│  • evaluate_alternatives                                                     │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 10. Actor Integration Patterns

### 10.1 Event Actor: Region Manager

Event actors manage regions and need spatial awareness for orchestration:

```csharp
/// <summary>
/// Event actor that manages a region and orchestrates encounters.
/// Subscribes to map updates for spatial awareness.
/// </summary>
public partial class RegionEventActor : RegionEventActorBase
{
    private readonly IMessageSubscriber _subscriber;
    private readonly IMappingClient _mappingClient;

    // Cached spatial data
    private Dictionary<Guid, MapObject> _staticGeometry = new();
    private Dictionary<Guid, MapObject> _resources = new();

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        // Subscribe to map updates for this region
        _staticGeometrySub = await _subscriber.SubscribeDynamicAsync<MapObjectsChangedEvent>(
            $"map.{RegionId}.static_geometry.*",
            HandleStaticGeometryUpdate,
            cancellationToken: ct);

        _resourceSub = await _subscriber.SubscribeDynamicAsync<MapObjectsChangedEvent>(
            $"map.{RegionId}.resources.*",
            HandleResourceUpdate,
            cancellationToken: ct);

        // Request initial snapshot
        await _mappingClient.RequestSnapshotAsync(new RequestSnapshotRequest
        {
            RegionId = RegionId,
            Kinds = [MapKind.StaticGeometry, MapKind.Resources]
        }, ct);
    }

    /// <summary>
    /// Find good ambush locations based on terrain and geometry.
    /// </summary>
    public async Task<List<Position3D>> FindAmbushPointsAsync(CancellationToken ct)
    {
        // Query for boulder clusters (good cover)
        var boulders = await _mappingClient.QueryObjectsByTypeAsync(new QueryObjectsByTypeRequest
        {
            RegionId = RegionId,
            ObjectType = "boulder-cluster"
        }, ct);

        // Filter for tactical value based on terrain
        return boulders.Objects
            .Where(b => HasGoodSightlines(b) && HasCoverAngles(b))
            .Select(b => b.Position)
            .ToList();
    }

    private async Task HandleStaticGeometryUpdate(MapObjectsChangedEvent evt, CancellationToken ct)
    {
        foreach (var change in evt.Changes)
        {
            switch (change.Action)
            {
                case "created":
                case "updated":
                    _staticGeometry[change.ObjectId] = MapObject.From(change);
                    break;
                case "deleted":
                    _staticGeometry.Remove(change.ObjectId);
                    break;
            }
        }

        // Recompute tactical points when geometry changes
        await RecomputeTacticalPointsAsync(ct);
    }
}
```

### 10.2 NPC Brain Actor: No Direct Map Subscription

NPC brain actors receive spatial context through perception events:

```csharp
/// <summary>
/// NPC brain actor - does NOT subscribe to map events directly.
/// Spatial context comes through perception events from game server.
/// </summary>
public partial class NpcBrainActor : NpcBrainActorBase
{
    public override async Task HandlePerceptionAsync(
        CharacterPerceptionEvent perception,
        CancellationToken ct)
    {
        // Spatial context is INCLUDED in perception from game server
        var spatialContext = perception.SpatialContext;

        if (spatialContext != null)
        {
            // "I'm standing on stone" - affects movement choices
            if (spatialContext.TerrainType == "stone")
            {
                await UpdateFeelingAsync("stability", 0.8, ct);
            }

            // "There's cover nearby" - affects combat choices
            var nearbyBoulders = spatialContext.NearbyObjects
                .Where(o => o.ObjectType == "boulder-cluster")
                .ToList();

            if (nearbyBoulders.Any())
            {
                await UpdateGoalContextAsync("available_cover", nearbyBoulders, ct);
            }

            // "Hazard ahead!" - affects navigation
            if (spatialContext.HazardsInRange.Any())
            {
                await UpdateFeelingAsync("caution", 0.9, ct);
            }
        }

        // Normal cognition pipeline with spatial-aware context
        await ProcessCognitionAsync(perception, ct);
    }
}
```

### 10.3 Game Server: Authority Publisher (RPC Path)

The game server publishes map updates when spatial state changes via RPC:

```csharp
/// <summary>
/// Game server publishes authoritative spatial updates.
/// </summary>
public class GameServerSpatialManager
{
    private readonly IMappingClient _mappingClient;

    /// <summary>
    /// Called when a destructible object is destroyed.
    /// </summary>
    public async Task OnObjectDestroyedAsync(Guid objectId, Position3D position)
    {
        await _mappingClient.PublishObjectChangesAsync(new PublishObjectChangesRequest
        {
            RegionId = _currentRegionId,
            Kind = MapKind.DynamicObjects,
            Changes =
            [
                new ObjectChange
                {
                    ObjectId = objectId,
                    Action = "deleted",
                    Position = position
                }
            ]
        });
    }

    /// <summary>
    /// Called when a hazard zone is created (e.g., fire spell).
    /// </summary>
    public async Task OnHazardCreatedAsync(Guid hazardId, Bounds area, string hazardType)
    {
        await _mappingClient.PublishMapUpdateAsync(new PublishMapUpdateRequest
        {
            RegionId = _currentRegionId,
            Kind = MapKind.Hazards,
            Bounds = area,
            DeltaType = "delta",
            Payload = new
            {
                HazardId = hazardId,
                Type = hazardType,
                Bounds = area,
                Duration = TimeSpan.FromSeconds(30)
            }
        });
    }

    /// <summary>
    /// Generate perception with spatial context for character.
    /// </summary>
    public CharacterPerceptionEvent CreatePerceptionWithSpatialContext(
        Guid characterId,
        Position3D position)
    {
        // Query local spatial state (game server has authoritative copy)
        var terrainType = _localTerrain.GetTerrainAt(position);
        var nearbyObjects = _localObjects.GetWithinRadius(position, 20f);
        var hazards = _localHazards.GetWithinRadius(position, 30f);

        return new CharacterPerceptionEvent
        {
            CharacterId = characterId,
            PerceptionType = "spatial",
            Timestamp = DateTimeOffset.UtcNow,
            SpatialContext = new SpatialContext
            {
                TerrainType = terrainType,
                NearbyObjects = nearbyObjects.Select(o => new NearbyObject
                {
                    ObjectId = o.Id,
                    ObjectType = o.Type,
                    Distance = o.DistanceTo(position),
                    Direction = o.DirectionFrom(position)
                }).ToList(),
                HazardsInRange = hazards.Select(h => new HazardInfo
                {
                    HazardType = h.Type,
                    Distance = h.DistanceTo(position),
                    Severity = h.Severity
                }).ToList()
            }
        };
    }
}
```

### 10.4 Game Server: Authority Publisher (Event Path)

For high-throughput scenarios, authority can publish directly via lib-messaging:

```csharp
/// <summary>
/// Game server publishes high-frequency updates via event path.
/// Use this for combat effects, position updates, etc.
/// </summary>
public class GameServerHighThroughputPublisher
{
    private readonly IMessageBus _messageBus;
    private readonly string _ingestTopic;
    private readonly string _authorityToken;

    public GameServerHighThroughputPublisher(
        IMessageBus messageBus,
        AuthorityGrant authority)
    {
        _messageBus = messageBus;
        _ingestTopic = authority.IngestTopic; // e.g., "map.ingest.{channelId}"
        _authorityToken = authority.AuthorityToken;
    }

    /// <summary>
    /// Publish combat effect (high frequency, fire-and-forget).
    /// </summary>
    public async Task PublishCombatEffectAsync(CombatEffect effect)
    {
        // Publish to ingest topic - lib-mapping validates and broadcasts
        await _messageBus.PublishAsync(_ingestTopic, new MapIngestEvent
        {
            AuthorityToken = _authorityToken,
            Timestamp = DateTimeOffset.UtcNow,
            Payloads =
            [
                new MapPayload
                {
                    ObjectType = "aoe_ability",
                    Bounds = effect.Bounds,
                    Data = BannouJson.SerializeToNode(new
                    {
                        ability_id = effect.AbilityId,
                        caster_character_id = effect.CasterId,
                        effect_type = effect.EffectType,
                        damage_per_tick = effect.DamagePerTick,
                        remaining_duration_ms = effect.RemainingDurationMs,
                        visual_effect = effect.VisualEffect
                    })
                }
            ]
        });
    }

    /// <summary>
    /// Batch multiple updates into single event (efficient for bulk).
    /// </summary>
    public async Task PublishBatchUpdatesAsync(IReadOnlyList<MapPayload> updates)
    {
        // Can batch up to 100 payloads per event
        foreach (var batch in updates.Chunk(100))
        {
            await _messageBus.PublishAsync(_ingestTopic, new MapIngestEvent
            {
                AuthorityToken = _authorityToken,
                Timestamp = DateTimeOffset.UtcNow,
                Payloads = batch.ToList()
            });
        }
    }
}
```

**Ingest Event Schema:**
```yaml
MapIngestEvent:
  type: object
  description: |
    Event published by authority to ingest topic.
    lib-mapping validates authority and broadcasts to consumers.
  required: [authorityToken, timestamp, payloads]
  properties:
    authorityToken:
      type: string
      description: Token proving authority over this channel
    timestamp:
      type: string
      format: date-time
    payloads:
      type: array
      maxItems: 100
      items:
        type: object
        additionalProperties: true
        description: Schema-less payloads (bags of data)
```

**Topic Patterns:**
| Topic | Direction | Purpose |
|-------|-----------|---------|
| `map.ingest.{channelId}` | Authority → lib-mapping | High-throughput ingest |
| `map.{regionId}.{kind}.updated` | lib-mapping → Consumers | Broadcast to subscribers |
| `map.{regionId}.{kind}.snapshot` | lib-mapping → Consumers | Full state snapshot |
| `map.warnings.unauthorized_publish` | lib-mapping → Monitoring | Non-authority attempts |

---

## 11. Data Flow Diagrams

### 11.1 Runtime Data Flow

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                      RUNTIME MAP DATA FLOW                                   │
│                                                                              │
│  1. Object destroyed in game                                                 │
│     ┌──────────────┐                                                        │
│     │ Game Server  │                                                        │
│     │   (Stride)   │                                                        │
│     └──────┬───────┘                                                        │
│            │                                                                 │
│            │ POST /mapping/publish-objects                                   │
│            │ { regionId, kind: "dynamic_objects", changes: [{deleted}] }    │
│            ▼                                                                 │
│     ┌──────────────┐                                                        │
│     │ lib-mapping  │                                                        │
│     │  (Bannou)    │                                                        │
│     └──────┬───────┘                                                        │
│            │                                                                 │
│            │ Stores update + Publishes event                                 │
│            │ Topic: map.{regionId}.dynamic_objects.updated                   │
│            │                                                                 │
│            ▼                                                                 │
│  ┌─────────────────────────────────────────────────────────────────────────┐│
│  │                         lib-messaging (RabbitMQ)                        ││
│  │  Exchange: bannou-events                                                ││
│  │  Routing key: map.{regionId}.dynamic_objects.updated                    ││
│  └───────────────────────────────────┬─────────────────────────────────────┘│
│                                      │                                       │
│            ┌─────────────────────────┼─────────────────────────┐            │
│            ▼                         ▼                         ▼            │
│  ┌──────────────────┐    ┌──────────────────┐    ┌──────────────────┐      │
│  │ Region Event     │    │ Other Game       │    │ Analytics        │      │
│  │ Actor            │    │ Server (cross-   │    │ Service          │      │
│  │ (Pool Node)      │    │ region LOD)      │    │ (logging)        │      │
│  │                  │    │                  │    │                  │      │
│  │ Updates spatial  │    │ Updates distant  │    │ Tracks object    │      │
│  │ cache for event  │    │ view rendering   │    │ lifecycle        │      │
│  │ orchestration    │    │                  │    │                  │      │
│  └──────────────────┘    └──────────────────┘    └──────────────────┘      │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 11.2 NPC Perception Data Flow

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    NPC SPATIAL PERCEPTION FLOW                               │
│                                                                              │
│  Game Server has authoritative spatial state                                │
│  ┌──────────────────────────────────────────────────────────────────────┐  │
│  │  LOCAL SPATIAL STATE (Game Server memory)                            │  │
│  │  - Terrain grid                                                       │  │
│  │  - Object positions                                                   │  │
│  │  - Hazard zones                                                       │  │
│  │  - Navigation mesh                                                    │  │
│  └────────────────────────────────┬─────────────────────────────────────┘  │
│                                   │                                         │
│                                   │ Character moves/acts                    │
│                                   ▼                                         │
│  ┌──────────────────────────────────────────────────────────────────────┐  │
│  │  PERCEPTION GENERATION                                                │  │
│  │  Game server creates perception with spatial context:                 │  │
│  │  - Terrain type at position                                           │  │
│  │  - Nearby objects (within perception radius)                          │  │
│  │  - Hazards in range                                                   │  │
│  │  - Pathable directions                                                │  │
│  └────────────────────────────────┬─────────────────────────────────────┘  │
│                                   │                                         │
│                                   │ Publish: character.{id}.perception      │
│                                   ▼                                         │
│  ┌──────────────────────────────────────────────────────────────────────┐  │
│  │  NPC BRAIN ACTOR (Pool Node)                                          │  │
│  │                                                                        │  │
│  │  Receives perception WITH spatial context already included.           │  │
│  │  Does NOT subscribe to map.* topics.                                  │  │
│  │                                                                        │  │
│  │  Uses spatial context in cognition:                                   │  │
│  │  - "Standing on stone" → stable footing feeling                       │  │
│  │  - "Boulder nearby" → cover awareness goal                            │  │
│  │  - "Fire hazard ahead" → caution feeling                              │  │
│  └──────────────────────────────────────────────────────────────────────┘  │
│                                                                              │
│  KEY INSIGHT: NPC actors don't need direct map subscriptions because        │
│  the game server already filters and includes relevant spatial context      │
│  in perception events. This is more efficient and maintains proper          │
│  perception-limited awareness.                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 12. Storage Model

### 12.1 State Store Keys

```
# Map definitions (templates)
map:definition:{mapId}                  → MapDefinition

# Channel metadata (region+kind)
map:channel:{channelId}                 → ChannelMetadata

# Authority records
map:authority:{channelId}               → AuthorityRecord
map:authority:by-app:{appId}            → Set<channelId>

# Layer data (actual spatial data)
map:layer:{regionId}:{kind}:{layerId}   → LayerData

# Metadata objects (schema-less blobs)
map:object:{regionId}:{objectId}        → JSON blob (additionalProperties: true)

# Spatial index (for queries)
map:index:{regionId}:{kind}:{cellKey}   → Set<objectId>

# Object type index (for affordance queries)
map:type-index:{regionId}:{objectType}  → Set<objectId>

# Authoring checkouts
map:checkout:{regionId}:{kind}          → CheckoutRecord
```

### 12.2 Cache vs Durable Storage

| Data Type | Storage | TTL | Notes |
|-----------|---------|-----|-------|
| Map definitions | Durable | None | Template definitions |
| Channel metadata | Durable | None | Region+kind channels |
| Authority records | Cache | 60 sec | Refreshed by heartbeat |
| Terrain layers | Durable | None | Rarely changes |
| Static geometry | Durable | None | Rarely changes |
| Dynamic objects | Cache | 1 hour | Game server is authority |
| Hazards | Cache | 5 min | Short-lived |
| Combat effects | Cache | 30 sec | Very short-lived |
| Ownership | Durable | None | Persistent |

---

## 13. ABML Integration

> **Key Insight**: ABML is the perfect *consumer* of affordance and spatial query results, but the actual spatial processing happens in lib-mapping's native C# code. See [Section 9.7](#97-why-abml-cannot-be-an-affordance-processor) for the rationale.

### 13.1 Affordance Queries in ABML (Recommended Pattern)

The **preferred pattern** for spatial decision-making in ABML is to call affordance queries and use the pre-scored, pre-filtered results:

```yaml
# Event actor behavior using affordance queries
version: "2.0"
metadata:
  id: region_event_orchestrator
  type: behavior

flows:
  plan_ambush_encounter:
    actions:
      # Query for ambush-suitable locations (lib-mapping does the heavy lifting)
      - service_call:
          service: mapping
          method: query-affordance
          parameters:
            region_id: "${region.id}"
            affordance_type: "ambush"
            min_score: 0.7
            max_results: 5
            participant_count: "${encounter.enemy_count}"
            actor_capabilities:
              size: "medium"
              stealth_rating: 0.6
          result_variable: "ambush_locations"

      # Check if we found suitable locations
      - cond:
          - when: "${length(ambush_locations.locations) == 0}"
            then:
              - log: { message: "No suitable ambush locations found, falling back" }
              - call: { flow: roaming_encounter }

          - otherwise:
              # Use the best location (already scored and ranked by lib-mapping)
              - set:
                  variable: chosen_location
                  value: "${first(ambush_locations.locations)}"
              - log:
                  message: "Spawning ambush at score ${chosen_location.score}"
              - call: { flow: spawn_ambush_at_location }

  spawn_ambush_at_location:
    actions:
      # Use the pre-evaluated features from the affordance result
      - cond:
          - when: "${chosen_location.features.cover_rating > 0.8}"
            then:
              # High cover = ranged ambush
              - call: { flow: spawn_ranged_ambush }
          - when: "${chosen_location.features.chokepoint_proximity < 10}"
            then:
              # Near chokepoint = blocking ambush
              - call: { flow: spawn_blocking_ambush }
          - otherwise:
              # Standard melee ambush
              - call: { flow: spawn_melee_ambush }

      # Spawn enemies at the location
      - for_each:
          variable: i
          collection: "${range(encounter.enemy_count)}"
          do:
            - service_call:
                service: actor
                method: spawn-character
                parameters:
                  position: "${chosen_location.position}"
                  template: "${encounter.enemy_template}"
                  behavior: "ambush_behavior"
```

### 13.2 Actor-Specific Affordance Queries

Different actor types query with their own capabilities:

```yaml
flows:
  find_shelter_for_actor:
    actions:
      # Query shelter locations appropriate for this actor's size
      - service_call:
          service: mapping
          method: query-affordance
          parameters:
            region_id: "${region.id}"
            affordance_type: "shelter"
            actor_capabilities:
              size: "${actor.size}"          # tiny/small/medium/large/huge
              can_fly: "${actor.can_fly}"
              can_swim: "${actor.can_swim}"
            bounds:
              min: "${actor.position.offset(-100, -100, -100)}"
              max: "${actor.position.offset(100, 100, 100)}"
          result_variable: "shelters"

      - cond:
          - when: "${length(shelters.locations) > 0}"
            then:
              - set:
                  variable: destination
                  value: "${first(shelters.locations).position}"
              - call: { flow: move_to_destination }
```

### 13.3 Custom Affordance Queries

For novel situations not covered by well-known affordance types:

```yaml
flows:
  find_dramatic_scene_location:
    actions:
      # Custom affordance: "romantic overlook for sunset scene"
      - service_call:
          service: mapping
          method: query-affordance
          parameters:
            region_id: "${region.id}"
            affordance_type: "custom"
            custom_affordance:
              description: "romantic_overlook"
              requires:
                object_types: ["vista_point", "scenic_overlook"]
                elevation: { min: 50 }
                visibility_distance: { min: 200 }
                hazards: false
              prefers:
                sunset_facing: true
                shelter_nearby: true
                water_view: true
              excludes:
                creature_spawns_nearby: 100
                high_traffic: true
            participant_count: 2
            max_results: 3
          result_variable: "scene_locations"

      - cond:
          - when: "${length(scene_locations.locations) > 0}"
            then:
              - set:
                  variable: scene
                  value: "${first(scene_locations.locations)}"
              - log:
                  message: "Found romantic overlook with features: ${scene.features}"
              - call: { flow: stage_romantic_scene }
```

### 13.4 Spatial Context in NPC Behaviors (Perception-Based)

NPC brain actors receive spatial context through perception events, NOT through direct map queries:

```yaml
# NPC brain behavior (spatial context comes FROM perception, not from map queries)
version: "2.0"
metadata:
  id: npc_combat_brain
  type: behavior

context:
  variables:
    spatial_context:
      source: "${perception.spatial_context}"
      type: SpatialContext

flows:
  evaluate_combat_options:
    actions:
      - cond:
          # Cover available? (from perception spatial context)
          - when: "${length(spatial_context.nearby_objects.where(type='boulder_cluster')) > 0}"
            then:
              - set:
                  variable: has_cover
                  value: "${true}"
              - call: { flow: use_cover_tactics }

          # Standing in water? (disadvantaged)
          - when: "${spatial_context.terrain_type == 'water'}"
            then:
              - call: { flow: disadvantaged_combat }

          # Hazard nearby? (be cautious)
          - when: "${length(spatial_context.hazards_in_range) > 0}"
            then:
              - set:
                  variable: nearest_hazard
                  value: "${first(spatial_context.hazards_in_range)}"
              - call: { flow: avoid_hazard_combat }

          - otherwise:
              - call: { flow: standard_combat }

  use_cover_tactics:
    actions:
      # Find the nearest cover object from perception context
      - set:
          variable: cover_object
          value: "${first(spatial_context.nearby_objects.where(type='boulder_cluster'))}"
      - log:
          message: "Moving to cover at distance ${cover_object.distance}"
      # Request movement toward cover
      - publish:
          event: CharacterMoveRequestEvent
          data:
            character_id: "${actor.id}"
            destination: "${cover_object.position}"
            reason: "seeking_cover"
```

### 13.5 Anti-Pattern: Don't Process Affordances in ABML

The following pattern is **discouraged** - it puts affordance processing logic in ABML where it doesn't belong:

```yaml
# ❌ ANTI-PATTERN: Processing affordances in ABML
flows:
  plan_ambush_BAD:
    actions:
      # Fetching raw objects and evaluating them in ABML = WRONG
      - service_call:
          service: mapping
          method: query-objects-by-type
          parameters:
            region_id: "${region.id}"
            object_type: "boulder_cluster"
          result_variable: "boulders"

      # Manually filtering in ABML = INEFFICIENT, WRONG LAYER
      - set: { variable: best_score, value: 0 }
      - set: { variable: best_location, value: "${null}" }
      - for_each:
          variable: boulder
          collection: "${boulders.objects}"
          do:
            # This kind of scoring belongs in lib-mapping, not ABML!
            - set:
                variable: score
                value: "${boulder.data.cover_rating * 0.3 + ...}"
            - cond:
                - when: "${score > best_score}"
                  then:
                    - set: { variable: best_score, value: "${score}" }
                    - set: { variable: best_location, value: "${boulder}" }
```

**Why this is wrong:**
- ABML's tree-walking interpreter is not optimized for numerical iteration
- Scoring logic becomes duplicated across behaviors
- Can't leverage native spatial operations (raycast, pathfinding)
- Violates separation of concerns

**Use the affordance query API instead** - let lib-mapping do the heavy lifting, and ABML focuses on decision-making with the results.

---

## 14. Configuration

```yaml
# mapping-configuration.yaml
x-service-configuration:
  properties:
    # Storage settings
    DefaultLayerCacheTtlSeconds:
      type: integer
      default: 3600
      env: MAPPING_DEFAULT_LAYER_CACHE_TTL_SECONDS
      description: Default TTL for cached layer data

    MaxObjectsPerQuery:
      type: integer
      default: 5000
      env: MAPPING_MAX_OBJECTS_PER_QUERY
      description: Maximum objects returned in a single query

    # Publishing settings
    MaxChangesPerPublish:
      type: integer
      default: 100
      env: MAPPING_MAX_CHANGES_PER_PUBLISH
      description: Maximum object changes per publish call

    EventAggregationWindowMs:
      type: integer
      default: 100
      env: MAPPING_EVENT_AGGREGATION_WINDOW_MS
      description: Window for batching rapid updates into single event

    # Authoring settings
    MaxCheckoutDurationSeconds:
      type: integer
      default: 3600
      env: MAPPING_MAX_CHECKOUT_DURATION_SECONDS
      description: Maximum duration for authoring checkout

    # Large payload settings
    InlinePayloadMaxBytes:
      type: integer
      default: 65536
      env: MAPPING_INLINE_PAYLOAD_MAX_BYTES
      description: Payloads larger than this use lib-asset reference

    # Authority settings
    AuthorityHeartbeatIntervalSeconds:
      type: integer
      default: 30
      env: MAPPING_AUTHORITY_HEARTBEAT_INTERVAL_SECONDS
      description: How often authorities must heartbeat

    AuthorityExpirationSeconds:
      type: integer
      default: 60
      env: MAPPING_AUTHORITY_EXPIRATION_SECONDS
      description: Time until authority expires without heartbeat

    AuthorityGracePeriodSeconds:
      type: integer
      default: 30
      env: MAPPING_AUTHORITY_GRACE_PERIOD_SECONDS
      description: Grace period after missed heartbeat before authority released

    # Non-authority handling
    RejectNonAuthorityPublishes:
      type: boolean
      default: true
      env: MAPPING_REJECT_NON_AUTHORITY_PUBLISHES
      description: Whether to reject (true) or warn and accept (false) non-authority publishes
```

---

## 15. Tenet Compliance

| Tenet Category | Requirement | Implementation |
|----------------|-------------|----------------|
| **FOUNDATION** | Schema-First | `schemas/mapping-api.yaml`, `schemas/mapping-events.yaml` |
| **FOUNDATION** | Infrastructure Libs | Uses lib-state, lib-messaging, lib-mesh |
| **FOUNDATION** | Event-Driven | All changes emit typed events |
| **IMPLEMENTATION** | Return Pattern | All methods return `(StatusCodes, Response?)` |
| **IMPLEMENTATION** | Multi-Instance | No in-memory authoritative state |
| **IMPLEMENTATION** | Polymorphic | Entity ID + Type for object references |
| **QUALITY** | Logging | Structured logging with message templates |
| **QUALITY** | Testing | Unit + HTTP integration + actor consumption tests |

---

## 16. Implementation Phases

Implementation follows a complete, non-incremental approach. All phases are required for the full feature set.

### Phase 1: Schema & Core Infrastructure
- [ ] Schema definitions (`mapping-api.yaml`, `mapping-events.yaml`)
- [ ] Channel and authority state store setup
- [ ] Authority registration APIs (create-channel, release, heartbeat)
- [ ] Ingest topic subscription (lib-mapping listens for authority publishes)
- [ ] Unit tests for authority lifecycle

### Phase 2: Publishing (Both Paths)
- [ ] RPC publish API (POST /mapping/publish)
- [ ] Event ingest handler (map.ingest.{channelId} subscription)
- [ ] Authority token validation
- [ ] Non-authority warning/rejection
- [ ] Event emission to consumer topics (map.{regionId}.{kind}.*)
- [ ] Unit tests for both publish paths

### Phase 3: Query APIs
- [ ] Point query implementation
- [ ] Bounds query implementation
- [ ] Object-by-type query
- [ ] Spatial indexing (by position and by object type)
- [ ] HTTP integration tests

### Phase 4: Affordance Queries
- [ ] Well-known affordance implementations (ambush, shelter, vista, etc.)
- [ ] Custom affordance query support
- [ ] Multi-kind query aggregation
- [ ] Scoring and ranking algorithm
- [ ] Integration tests with sample data

### Phase 5: Actor Integration
- [ ] Event actor subscription patterns
- [ ] Snapshot request/response flow
- [ ] ABML spatial query actions
- [ ] ABML affordance query actions
- [ ] Actor consumption tests

### Phase 6: Authoring APIs
- [ ] Checkout/commit for design tools (with authority transfer)
- [ ] Map definition CRUD
- [ ] Layer format support (texture, sparse_dict, metadata_objects)

### Phase 7: Advanced Features
- [ ] Large payload handling (lib-asset integration)
- [ ] Event aggregation for high-frequency updates
- [ ] Cross-region queries

---

## Appendix A: Comparison with Original Document

| Aspect | Original Doc | This Doc |
|--------|--------------|----------|
| Architecture | "Map Service" (ambiguous) | Clear separation: Authoring vs Runtime (RPC + Event) flows |
| Game Server Role | "Region AI owns layers" | Game server is explicit authority with token-based validation |
| Publishing Model | RPC only | Dual-path: RPC (validated) + Event (high-throughput lib-messaging) |
| Authority Model | Not addressed | Full authority lifecycle with heartbeat, tokens, warnings |
| Payload Schema | Typed schemas | Schema-less "bags of data" (additionalProperties: true) |
| Actor Integration | Not addressed | Detailed NPC vs Event actor patterns |
| Topic Routing | Generic | `map.{regionId}.{kind}.{action}` + `map.ingest.{channelId}` patterns |
| Perception Flow | Not addressed | NPC actors receive spatial via perception events |
| Map Kinds | "layers" (format-focused) | Clear taxonomy with concrete payload examples |
| Affordance Queries | Not addressed | Event Brain integration for spatial scene orchestration |
| Naming | lib-map (noun) | lib-mapping (gerund, avoids conflicts) |

---

## Appendix B: Key Schema Decisions

### Why Schema-less Payloads?

Map objects change constantly as game design evolves. Maintaining typed schemas would:
- Create endless schema churn
- Couple lib-mapping to game-specific knowledge
- Require regeneration for every design change

**Solution**: lib-mapping treats payload.data as opaque. Only `objectType` is indexed.

### Why Authority Tokens?

Without authority validation:
- Any app could overwrite spatial data
- Race conditions between game servers
- No audit trail of who changed what

**Solution**: Authority tokens establish single source of truth per channel.

### Why Dual Publishing Paths?

Different scenarios need different guarantees:
- RPC: Validation, acknowledgment, guaranteed delivery (important changes)
- Event: High-throughput, batching, fire-and-forget (combat effects, streaming)

**Solution**: Both paths, same output topic for consumers.

---

*Document created: 2026-01-07*
*Last updated: 2026-01-07*
*This document supersedes UPCOMING_-_MAP_SERVICE.md*
