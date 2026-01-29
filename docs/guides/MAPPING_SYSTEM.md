# Mapping System - Spatial Intelligence for Game Worlds

> **Version**: 1.0
> **Status**: Implemented
> **Location**: `plugins/lib-mapping/`
> **Related**: [GOAP Guide](./GOAP.md), [ABML Guide](./ABML.md)

The Mapping System (`lib-mapping`) manages spatial data for game worlds. It enables game servers to publish live spatial data, event actors to orchestrate region-wide encounters, NPC actors to receive perception-filtered context, and behaviors to make spatially-aware decisions through affordance queries.

---

## Table of Contents

1. [Overview](#1-overview)
2. [Core Concepts](#2-core-concepts)
3. [Authority Model](#3-authority-model)
4. [Map Kinds](#4-map-kinds)
5. [Event Architecture](#5-event-architecture)
6. [Affordance System](#6-affordance-system)
7. [Actor Integration](#7-actor-integration)
8. [Processed Maps and Derivatives](#8-processed-maps-and-derivatives)
9. [Property Ownership](#9-property-ownership)
10. [ABML Integration](#10-abml-integration)
11. [API Reference](#11-api-reference)
12. [Best Practices](#12-best-practices)
- [Appendix A: Payload Examples](#appendix-a-payload-examples)
- [Appendix B: Well-Known Affordance Types](#appendix-b-well-known-affordance-types)

---

## 1. Overview

### 1.1 What is the Mapping System?

The Mapping System is the spatial intelligence backbone of Bannou-powered games. It answers the fundamental questions that make worlds feel alive:

- **Where can things happen?** - Not just "where is the boulder" but "where can an ambush occur"
- **What does this location afford?** - Cover, shelter, dramatic vistas, chokepoints
- **How does the world change?** - Real-time tracking of hazards, destruction, ownership
- **What does each actor perceive?** - Filtered spatial context based on perception

Unlike simple coordinate storage, the Mapping System enables **smart decisions** rather than scripted ones. An NPC doesn't just know "go to position X,Y" - it knows "there's cover nearby, a hazard to avoid, and multiple escape routes."

### 1.2 Design Principles

1. **Schema-less Payloads**: Map objects are "bags of data" - `additionalProperties: true` everywhere. Only publisher and consumer care about contents; lib-mapping passes them through unchanged. This allows game design to evolve without schema churn.

2. **Authority Registration**: Authority over spatial data is explicit. The game server owns live runtime data; design tools own static templates. Non-authority publishes generate configurable warnings.

3. **Two Consumer Patterns**: Event actors subscribe directly to map events for region-wide awareness. NPC actors receive filtered spatial context through perception events - they only know what their character can perceive.

4. **Affordance-First Queries**: Instead of "what's at position X," ask "where can I stage an ambush?" The system evaluates multiple map kinds and returns scored, actionable locations.

### 1.3 The Three Data Flows

```
AUTHORING FLOW (Design-time):
  Editor Tools ─────checkout/commit─────> lib-mapping ──> Stored templates
                    (with locking)

RUNTIME FLOW - RPC (Request/Response):
  Game Server ─────POST /mapping/*─────> lib-mapping ─────events────> Actors
               (validated publish)

RUNTIME FLOW - EVENT (High-throughput):
  Game Server ─────lib-messaging─────> lib-mapping ─────events────> Actors
               map.ingest.{channelId}
               (bulk/streaming)
```

---

## 2. Core Concepts

### 2.1 Channels

A **channel** represents a unique combination of region and kind:

```
Channel = Region + Kind
         550e8400-... + "static_geometry"
```

Channels are the unit of:
- **Authority** - One app-id owns each channel
- **Subscription** - Consumers subscribe to channel events
- **Publishing** - Updates target a specific channel

### 2.2 Regions

A **region** is a spatial partition of the world - typically a zone, instance, or area. Regions are identified by UUID:

```csharp
var regionId = Guid.Parse("550e8400-e29b-41d4-a716-446655440001");
```

Regions provide natural partitioning for:
- Event routing (consumers subscribe to regions they manage)
- Query scoping (spatial queries are region-bounded)
- Authority boundaries (different servers can own different regions)

### 2.3 Map Objects

Map objects are schema-less data containers with minimal envelope fields:

```json
{
  "objectType": "boulder_cluster",
  "position": { "x": 2048, "y": 15, "z": 1024 },
  "bounds": {
    "min": { "x": 2040, "y": 10, "z": 1016 },
    "max": { "x": 2056, "y": 25, "z": 1032 }
  },
  "data": {
    "provides_cover": true,
    "cover_rating": 0.8,
    "sightline_blockage": 0.6,
    "climbable": false
  }
}
```

**Envelope fields** (validated by lib-mapping):
- `objectType` - Publisher-defined type string for filtering
- `position` - Point location (for point objects)
- `bounds` - Bounding box (for area objects)

**Data field** - Anything the game needs. lib-mapping doesn't validate it.

### 2.4 Versions and Deltas

Each update carries a monotonic version number for ordering:

```yaml
MapUpdatedEvent:
  version: 12345      # Monotonic, for ordering
  deltaType: delta    # "delta" or "snapshot"
```

- **Delta**: Incremental change - apply on top of existing state
- **Snapshot**: Full state replacement - use for cold start or recovery

---

## 3. Authority Model

### 3.1 Authority Registration

Authority over a channel is established at creation:

```csharp
var (status, grant) = await _mappingService.CreateChannelAsync(new CreateChannelRequest
{
    RegionId = regionId,
    Kind = "dynamic_objects"
});

// grant.AuthorityToken - Prove authority on subsequent publishes
// grant.IngestTopic - Topic for high-throughput event publishing
// grant.ExpiresAt - Authority expiration (must heartbeat to maintain)
```

### 3.2 Authority Token

The `authorityToken` is opaque - don't parse it. Validity is determined by checking `AuthorityRecord.ExpiresAt` on the server side, not by decoding the token.

```csharp
// Correct: Use token in requests
await _mappingService.PublishMapUpdateAsync(new PublishMapUpdateRequest
{
    AuthorityToken = grant.AuthorityToken,
    RegionId = regionId,
    Kind = "dynamic_objects",
    Payload = objectData
});
```

### 3.3 Authority Heartbeat

Authorities must maintain heartbeat to keep their authority:

```csharp
// Call periodically (before ExpiresAt)
var (status, response) = await _mappingService.AuthorityHeartbeatAsync(
    new AuthorityHeartbeatRequest
    {
        ChannelId = channelId,
        AuthorityToken = authorityToken
    });

// response.ExpiresAt - Updated expiration
// response.Valid - Whether authority is still valid
```

If heartbeat is missed:
1. **Grace period (30 seconds)** - Allows recovery from transient failures
2. **Release** - Authority becomes unassigned
3. **Transfer** - If another app requested authority during grace period

### 3.4 Authority Takeover Modes

When a new authority takes over a channel, the mode determines data handling:

| Mode | Behavior | Use Case |
|------|----------|----------|
| `preserve_and_diff` | Keep existing data, new authority sends updates | Default - seamless handoff |
| `reset` | Clear all data, start fresh | New instance, clean slate |
| `require_consume` | New authority must RequestSnapshot first | Cautious handoff with verification |

### 3.5 Non-Authority Handling

Non-authority publish handling is configurable per-channel:

| Mode | Publish Accepted? | Warning Event? | Use Case |
|------|------------------|----------------|----------|
| `reject_and_alert` | No | Yes | Default - strict authority, monitor violations |
| `accept_and_alert` | Yes | Yes | Permissive mode during migration/debugging |
| `reject_silent` | No | No | High-volume channels where warnings would spam |

---

## 4. Map Kinds

Maps are differentiated by **kind** - the type of spatial data they contain:

### 4.1 Static Kinds (Rarely Changes)

| Kind | Description | Authority | Example Objects |
|------|-------------|-----------|-----------------|
| `terrain` | Heightmaps, biome types, base geography | Authoring tools | terrain_cell, biome_marker |
| `static_geometry` | Permanent structures | Authoring tools | boulder_cluster, building, cliff |
| `navigation` | Navmesh, pathfinding hints | Authoring tools | nav_chokepoint, pathable_area |

### 4.2 Semi-Static Kinds (Changes on Edit)

| Kind | Description | Authority | Example Objects |
|------|-------------|-----------|-----------------|
| `resources` | Harvestable nodes | Authoring tools | harvestable_ore, herb_spawn |
| `spawn_points` | NPC/creature spawn locations | Authoring tools | creature_spawn, patrol_route |
| `points_of_interest` | Quest markers, landmarks | Authoring tools | poi_landmark, fast_travel |

### 4.3 Dynamic Kinds (Runtime Changes)

| Kind | Description | Authority | TTL |
|------|-------------|-----------|-----|
| `dynamic_objects` | Movable/destructible objects | Game server | 1 hour |
| `hazards` | Damage zones (fire, radiation) | Game server | 5 min |
| `weather_effects` | Localized weather | Game server | Varies |
| `ownership` | Territory control | Game server | Durable |

### 4.4 Ephemeral Kinds (Short-Lived)

| Kind | Description | Authority | TTL |
|------|-------------|-----------|-----|
| `combat_effects` | AoE markers, ability zones | Game server | 30 sec |
| `visual_effects` | Particle triggers | Game server | 30 sec |

---

## 5. Event Architecture

### 5.1 Topic Structure

Events follow this topic pattern:
```
map.{regionId}.{kind}.{action}
```

Examples:
```
map.550e8400-e29b-41d4-a716-446655440001.terrain.updated
map.550e8400-e29b-41d4-a716-446655440001.hazards.created
map.550e8400-e29b-41d4-a716-446655440001.ownership.updated
```

### 5.2 Core Events

**MapUpdatedEvent** - Layer-level notification:
```yaml
MapUpdatedEvent:
  eventId: uuid
  timestamp: datetime
  regionId: uuid
  kind: string
  bounds: Bounds?          # Affected area (null = entire region)
  version: int64           # Monotonic for ordering
  deltaType: delta|snapshot
  sourceAppId: string      # Publisher identity
  payload: object          # Schema-less content
```

**MapObjectsChangedEvent** - Per-object delta list:
```yaml
MapObjectsChangedEvent:
  eventId: uuid
  timestamp: datetime
  regionId: uuid
  kind: string
  changes:
    - objectId: uuid
      action: created|updated|deleted
      objectType: string
      position: Position3D
      state: object        # Object data (for created/updated)
```

### 5.3 Event Aggregation

For high-frequency updates, events can be aggregated within a configurable window:

```yaml
# Configuration
EventAggregationWindowMs: 100  # Coalesce events within 100ms
```

When aggregation is enabled, multiple `MapObjectsChangedEvent` publishes within the window are combined into a single event, reducing consumer load for rapidly-changing data.

### 5.4 Ingest Topics

For high-throughput publishing, authorities can publish directly to ingest topics:

```
map.ingest.{channelId}
```

lib-mapping validates the authority token and broadcasts to consumer topics. This path is fire-and-forget with no acknowledgment, suitable for:
- Combat effects (high frequency)
- Position updates (streaming)
- Telemetry (bulk)

---

## 6. Affordance System

### 6.1 What Are Affordances?

Traditional spatial queries ask "what's here?" Affordance queries ask "where can I do X?"

The concept comes from James Gibson's ecological psychology: **affordances are action possibilities that the environment offers to an actor**. A boulder affords "cover" to a humanoid but not to a giant. A cliff affords "dramatic reveal" only if there's something to reveal below.

### 6.2 The Affordance Query Pipeline

```
┌──────────────┐   ┌──────────────┐   ┌──────────────┐   ┌─────────────┐
│  GENERATORS  │ → │    TESTS     │ → │   SCORING    │ → │  SELECTION  │
└──────────────┘   └──────────────┘   └──────────────┘   └─────────────┘

Create candidate     Evaluate each      Normalize and      Pick winner(s)
sample points        point's fitness    combine scores     from ranked
                                                           candidates
```

1. **Generators** create candidate points (object positions, grid samples, ring patterns)
2. **Tests** evaluate each point (cover rating, sightlines, pathability, hazards)
3. **Scoring** normalizes results to 0-1 and combines with weights
4. **Selection** returns ranked results

### 6.3 Well-Known Affordance Types

| Affordance | Description | Key Factors |
|------------|-------------|-------------|
| `ambush` | Surprise attack location | cover_rating, sightline_blockage, approach_routes |
| `shelter` | Protected resting location | roof_coverage, wind_protection, no_hazards |
| `vista` | Scenic overlook | elevation, visibility_distance, unobstructed_view |
| `choke_point` | Narrow defensive passage | width, alternate_routes, tactical_value |
| `gathering_spot` | Group assembly space | flat_terrain, size, accessibility |
| `dramatic_reveal` | Story moment location | elevation_change, vista_score, approach_suspense |
| `hidden_path` | Concealed route | vegetation_cover, terrain_concealment |
| `defensible_position` | Holdout location | cover_rating, elevation, escape_routes |

### 6.4 Actor Capabilities (Gibsonian Affordances)

The same location affords different actions to different actors:

```csharp
// Query for a MEDIUM humanoid NPC
var result = await _mappingService.QueryAffordanceAsync(new AffordanceQueryRequest
{
    RegionId = regionId,
    AffordanceType = "ambush",
    ActorCapabilities = new ActorCapabilities
    {
        Size = "medium",
        Height = 1.8
    }
});
// Result: Boulder clusters, dense vegetation

// Query for a LARGE ogre
var result = await _mappingService.QueryAffordanceAsync(new AffordanceQueryRequest
{
    RegionId = regionId,
    AffordanceType = "ambush",
    ActorCapabilities = new ActorCapabilities
    {
        Size = "large",
        Height = 3.5
    }
});
// Result: Only large structures, cliff overhangs (small boulders filtered out)
```

### 6.5 Custom Affordances

For novel situations, define custom criteria:

```json
{
  "regionId": "550e8400-...",
  "affordanceType": "custom",
  "customAffordance": {
    "description": "romantic_overlook",
    "requires": {
      "objectTypes": ["vista_point", "scenic_overlook"],
      "elevation": { "min": 50 },
      "hazards": false
    },
    "prefers": {
      "sunset_facing": true,
      "water_view": true
    },
    "excludes": {
      "creature_spawns_nearby": 100,
      "high_traffic": true
    }
  },
  "participantCount": 2
}
```

### 6.6 Freshness and Caching

Affordance queries can be expensive. Configure freshness per query:

| Level | Behavior | Latency | Use Case |
|-------|----------|---------|----------|
| `fresh` | Always query live | Highest | Combat starting NOW |
| `cached` | Use cache if < maxAgeSeconds | Medium | Planning phase |
| `aggressive_cache` | Return stale, refresh async | Lowest | Background planning |

---

## 7. Actor Integration

### 7.1 The Critical Distinction: NPC Actors vs Event Actors

| Aspect | NPC Brain Actor | Event Actor |
|--------|-----------------|-------------|
| **View** | Perception-limited (what character sees) | Region-wide (full spatial awareness) |
| **Data Source** | Perception events from game server | Direct map subscription |
| **Subscribes To** | `character.{characterId}.perception` | `map.{regionId}.{kind}.*` |
| **Purpose** | Character growth, feelings, goals | Orchestrate events, spawn encounters |
| **Example Use** | "I feel threatened by that enemy" | "Spawn ambush at boulder cluster at X,Y" |

### 7.2 Event Actor Pattern

Event actors manage regions and need spatial awareness for orchestration:

```csharp
public partial class RegionEventActor : RegionEventActorBase
{
    public override async Task OnActivateAsync(CancellationToken ct)
    {
        // Subscribe to map updates for this region
        await _subscriber.SubscribeDynamicAsync<MapObjectsChangedEvent>(
            $"map.{RegionId}.static_geometry.*",
            HandleStaticGeometryUpdate,
            cancellationToken: ct);

        // Request initial snapshot
        await _mappingClient.RequestSnapshotAsync(new RequestSnapshotRequest
        {
            RegionId = RegionId,
            Kinds = [MapKind.StaticGeometry, MapKind.Resources]
        }, ct);
    }

    public async Task<List<AffordanceLocation>> FindAmbushPointsAsync(CancellationToken ct)
    {
        // Use affordance query - lib-mapping does the heavy lifting
        var (status, response) = await _mappingClient.QueryAffordanceAsync(
            new AffordanceQueryRequest
            {
                RegionId = RegionId,
                AffordanceType = "ambush",
                MinScore = 0.7,
                MaxResults = 5
            }, ct);

        return response?.Locations ?? [];
    }
}
```

### 7.3 NPC Brain Actor Pattern

NPC actors receive spatial context through perception events - they don't subscribe to map topics directly:

```csharp
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
                .Where(o => o.ObjectType == "boulder_cluster")
                .ToList();

            if (nearbyBoulders.Any())
            {
                await UpdateGoalContextAsync("available_cover", nearbyBoulders, ct);
            }
        }
    }
}
```

### 7.4 Game Server Pattern

The game server is the authority for live spatial data:

```csharp
public class GameServerSpatialManager
{
    // RPC path for important state changes
    public async Task OnObjectDestroyedAsync(Guid objectId, Position3D position)
    {
        await _mappingClient.PublishObjectChangesAsync(new PublishObjectChangesRequest
        {
            RegionId = _currentRegionId,
            Kind = MapKind.DynamicObjects,
            AuthorityToken = _authorityToken,
            Changes = [new ObjectChange
            {
                ObjectId = objectId,
                Action = ObjectAction.Deleted,
                Position = position
            }]
        });
    }

    // Event path for high-frequency updates
    public async Task PublishCombatEffectAsync(CombatEffect effect)
    {
        await _messageBus.PublishAsync(_ingestTopic, new MapIngestEvent
        {
            AuthorityToken = _authorityToken,
            Payloads = [new MapPayload
            {
                ObjectType = "aoe_ability",
                Bounds = effect.Bounds,
                Data = effect.ToJsonNode()
            }]
        });
    }
}
```

---

## 8. Processed Maps and Derivatives

### 8.1 The Power of Derivative Maps

The Mapping System isn't just about storing raw spatial data - it's the foundation for **layered spatial intelligence**. Services can consume base map data, process it, and publish enriched derivative maps:

```
BASE MAPS (from game engine)        PROCESSED MAPS (from services)
───────────────────────────         ────────────────────────────────
terrain                      →      flow_paths (pedestrian traffic)
static_geometry              →      mana_density (magical hotspots)
spawn_points + navigation    →      danger_zones (threat assessment)
terrain + resources          →      economic_value (trade route scoring)
hazards + terrain            →      safe_corridors (evacuation routes)
```

### 8.2 Example: Flow Path Analysis

A pathfinding service consumes terrain and navigation data, runs crowd simulation, and publishes flow analysis:

```json
{
  "objectType": "flow_corridor",
  "bounds": { ... },
  "data": {
    "flow_type": "pedestrian",
    "average_throughput": 150,
    "peak_throughput": 400,
    "congestion_risk": 0.3,
    "alternate_routes": ["path_a", "path_b"],
    "bottleneck_positions": [
      { "x": 1000, "y": 0, "z": 500 }
    ]
  }
}
```

Event actors can now query "where are congestion points?" without understanding pathfinding algorithms.

### 8.3 Example: Threat Assessment Maps

A security analysis service combines spawn points, patrol routes, and historical combat data:

```json
{
  "objectType": "threat_zone",
  "bounds": { ... },
  "data": {
    "threat_level": "high",
    "threat_types": ["bandit", "wildlife"],
    "active_hours": { "start": 20, "end": 6 },
    "escape_difficulty": 0.7,
    "recommended_party_size": 3,
    "recent_incidents": 12
  }
}
```

NPC behaviors can receive "danger ahead" context without computing threat themselves.

### 8.4 Example: Economic Value Maps

A trade route analyzer combines resource locations, travel costs, and market data:

```json
{
  "objectType": "trade_opportunity",
  "position": { ... },
  "data": {
    "resource_type": "iron_ore",
    "estimated_profit": 150,
    "travel_risk": 0.2,
    "competition_level": "medium",
    "optimal_time_window": { "start": 8, "end": 18 }
  }
}
```

Merchant NPCs can make economically rational decisions without market modeling.

### 8.5 The Compounding Effect

Each derivative map enables more sophisticated derivatives:

```
Level 0: Raw spatial data
         terrain, geometry, spawn_points

Level 1: First-order processing
         flow_paths, threat_zones, resource_density

Level 2: Combined analysis
         safe_trade_routes (flow_paths + threat_zones)
         optimal_ambush_timing (flow_paths + threat_zones + time_of_day)

Level 3: Predictive modeling
         crowd_surge_prediction (flow_paths + events + time)
         territory_shift_forecast (ownership + threat + economics)
```

This layered approach enables **smart decisions** rather than **scripted decisions**. An NPC merchant doesn't follow a hardcoded route - it queries "safe profitable routes" and adapts when conditions change.

---

## 9. Property Ownership

### 9.1 Ownership as Spatial Data

Territory and property ownership is tracked as spatial map data:

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
    "protection_level": "full",
    "allowed_actions": {
      "guild_members": ["build", "harvest", "enter"],
      "allies": ["enter", "harvest"],
      "others": ["enter"]
    },
    "contested": false
  }
}
```

### 9.2 Ownership Events

When ownership changes, interested parties receive events:

```
map.{regionId}.ownership.updated
```

Event actors can:
- Trigger political events when territory changes hands
- Spawn defense encounters when territory is contested
- Update NPC faction relationships based on ownership

### 9.3 Affordance Integration

Ownership affects affordances:

```json
{
  "affordanceType": "custom",
  "customAffordance": {
    "requires": {
      "owner_type": "guild",
      "owner_id": "${actor.guild_id}"
    }
  }
}
```

A guild member querying for "safe rest" will only receive locations within owned territory.

---

## 10. ABML Integration

### 10.1 The Correct Pattern

ABML is the perfect *consumer* of affordance results, but the actual spatial processing happens in lib-mapping's native C# code:

```
ABML (Behavior Language)              lib-mapping (Native C#)
─────────────────────────             ────────────────────────
• Defines query parameters            • Generators create sample points
• Calls affordance service            • Tests evaluate each point
• Receives ranked results             • Scoring combines test results
• Makes decisions based on results    • Returns ranked candidates
• Orchestrates follow-up actions
```

### 10.2 Affordance Queries in ABML

```yaml
flows:
  plan_ambush_encounter:
    actions:
      # Query for ambush locations (lib-mapping does heavy lifting)
      - service_call:
          service: mapping
          method: query-affordance
          parameters:
            region_id: "${region.id}"
            affordance_type: "ambush"
            min_score: 0.7
            max_results: 5
            actor_capabilities:
              size: "medium"
              stealth_rating: 0.6
          result_variable: "ambush_locations"

      # Use pre-scored, pre-filtered results
      - cond:
          - when: "${length(ambush_locations.locations) == 0}"
            then:
              - call: { flow: roaming_encounter }
          - otherwise:
              - set:
                  variable: chosen_location
                  value: "${first(ambush_locations.locations)}"
              - call: { flow: spawn_ambush_at_location }
```

### 10.3 Spatial Context in NPC Behaviors

NPC behaviors receive spatial context through perception, not through direct queries:

```yaml
context:
  variables:
    spatial_context:
      source: "${perception.spatial_context}"

flows:
  evaluate_combat_options:
    actions:
      - cond:
          # Cover available? (from perception)
          - when: "${length(spatial_context.nearby_objects.where(type='boulder_cluster')) > 0}"
            then:
              - call: { flow: use_cover_tactics }

          # Hazard nearby? (from perception)
          - when: "${length(spatial_context.hazards_in_range) > 0}"
            then:
              - call: { flow: avoid_hazard_combat }
```

### 10.4 Anti-Pattern: Don't Process Affordances in ABML

ABML is not designed for numerical processing of hundreds of sample points:

```yaml
# ANTI-PATTERN: Don't do this
flows:
  plan_ambush_BAD:
    actions:
      # Don't query raw objects and try to score them in ABML
      - service_call:
          service: mapping
          method: query-objects-by-type
          parameters:
            object_type: "boulder_cluster"
          result_variable: "all_boulders"

      # Don't try to implement scoring logic in ABML
      - for_each:
          variable: boulder
          collection: "${all_boulders}"
          do:
            # This is algorithmic heavy lifting that belongs in C#
            - set:
                variable: score
                value: "${boulder.cover_rating * 0.3 + ...}"
```

Use affordance queries instead - let lib-mapping do the heavy lifting.

---

## 11. API Reference

### 11.1 Authority Management

| Endpoint | Description |
|----------|-------------|
| `POST /mapping/create-channel` | Create channel, become authority |
| `POST /mapping/release-authority` | Release authority over channel |
| `POST /mapping/authority-heartbeat` | Maintain authority |

### 11.2 Publishing

| Endpoint | Description |
|----------|-------------|
| `POST /mapping/publish` | Publish map layer update |
| `POST /mapping/publish-objects` | Publish object changes |

### 11.3 Queries

| Endpoint | Description |
|----------|-------------|
| `POST /mapping/query/point` | Query at specific point |
| `POST /mapping/query/bounds` | Query within bounds |
| `POST /mapping/query/objects-by-type` | Find objects by type |
| `POST /mapping/query/affordance` | Affordance query |
| `POST /mapping/request-snapshot` | Request full snapshot |

### 11.4 Authoring

| Endpoint | Description |
|----------|-------------|
| `POST /mapping/authoring/checkout` | Acquire edit lock |
| `POST /mapping/authoring/commit` | Commit changes |
| `POST /mapping/authoring/release` | Release lock |

### 11.5 Definitions

| Endpoint | Description |
|----------|-------------|
| `POST /mapping/definition/create` | Create map definition |
| `POST /mapping/definition/get` | Get definition by ID |
| `POST /mapping/definition/list` | List definitions |
| `POST /mapping/definition/update` | Update definition |
| `POST /mapping/definition/delete` | Delete definition |

---

## 12. Best Practices

### 12.1 Authority Management

- **Heartbeat regularly**: Don't let authority expire unexpectedly
- **Release explicitly**: When shutting down, release authority cleanly
- **Use appropriate takeover mode**: `preserve_and_diff` for seamless handoffs, `reset` for clean slates

### 12.2 Event Publishing

- **Use RPC for important changes**: Object destruction, ownership changes
- **Use ingest topics for high-frequency data**: Combat effects, position streaming
- **Batch updates when possible**: Up to 100 payloads per ingest event
- **Configure aggregation window** for rapidly-changing data

### 12.3 Affordance Queries

- **Use well-known types** when possible - they're optimized
- **Specify actor capabilities** for accurate results
- **Use appropriate freshness**: `fresh` for combat, `cached` for planning
- **Precompute common affordances** on actor activation

### 12.4 Consumer Patterns

- **Event actors**: Subscribe to specific regions and kinds you need
- **NPC actors**: Rely on perception events, don't subscribe to map topics
- **Request snapshots on cold start**: Don't assume empty state

### 12.5 Performance

- **Keep payloads reasonable**: Large blobs go through lib-asset
- **Use bounds in queries**: Don't query entire regions unnecessarily
- **Cache affordance results**: They can be expensive
- **Subscribe narrowly**: Avoid `map.*.*` wildcards

### 12.6 Known Limitations

For implementation details and known quirks, see [MAPPING.md](../plugins/MAPPING.md):

- **Event aggregation**: Fire-and-forget with potential silent loss under high load
- **Spatial index**: Stale entries accumulate indefinitely (no automatic cleanup)
- **Index operations**: Non-atomic; race conditions possible under concurrent updates
- **Snapshot events**: `MapSnapshotEvent` is defined but not currently published

---

## Appendix A: Payload Examples

### Terrain Cell
```json
{
  "objectType": "terrain_cell",
  "position": { "x": 1024, "y": 0, "z": 512 },
  "data": {
    "biome": "temperate_forest",
    "elevation": 127.5,
    "surface_material": "grass_dirt_mix",
    "traversability": {
      "foot": 1.0,
      "mount": 0.9,
      "vehicle": 0.3
    }
  }
}
```

### Static Geometry (Boulder Cluster)
```json
{
  "objectType": "boulder_cluster",
  "position": { "x": 2048, "y": 15, "z": 1024 },
  "bounds": {
    "min": { "x": 2040, "y": 10, "z": 1016 },
    "max": { "x": 2056, "y": 25, "z": 1032 }
  },
  "data": {
    "provides_cover": true,
    "cover_rating": 0.8,
    "sightline_blockage": 0.6,
    "climbable": false
  }
}
```

### Hazard (Fire Zone)
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
    "fuel_remaining_seconds": 45,
    "blocks_los": true
  }
}
```

### Navigation Hint (Chokepoint)
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
    "alternate_routes": [
      { "via": "ford_downstream", "cost_multiplier": 2.5 }
    ]
  }
}
```

---

## Appendix B: Well-Known Affordance Types

### ambush
**Purpose**: Find locations suitable for surprise attacks

**Generator**: Object positions (boulder_cluster, dense_vegetation, ruined_wall)

**Tests**:
- `has_cover` (weight: 0.30) - Cover rating from map object
- `clear_sightline` (weight: 0.25) - LOS to approach path
- `pathable` (weight: 0.15, required) - Can reach via navigation
- `no_hazards` (weight: 0.15, required) - No active hazards nearby
- `chokepoint_proximity` (weight: 0.15) - Near tactical chokepoints

### shelter
**Purpose**: Find protected resting locations

**Generator**: Object positions (structure, cave_entrance, overhang, dense_canopy)

**Tests**:
- `property_check: roof_coverage` (weight: 0.35)
- `no_hazards` (weight: 0.25, required)
- `escape_routes` (weight: 0.20)
- `property_check: wind_protection` (weight: 0.20)

### vista
**Purpose**: Find scenic overlooks or observation points

**Generator**: Object positions (vista_point, cliff_edge, tower, hilltop)

**Tests**:
- `elevation` (weight: 0.35) - Prefer higher positions
- `property_check: visibility_distance` (weight: 0.35)
- `pathable` (weight: 0.15, required)
- `no_hazards` (weight: 0.15)

### choke_point
**Purpose**: Find narrow defensive passages

**Generator**: Navigation hints (nav_chokepoint)

**Tests**:
- `property_check: width` (weight: 0.30) - Prefer narrower
- `property_check: tactical_value` (weight: 0.30)
- `escape_routes` (weight: 0.20)
- `pathable` (weight: 0.20, required)

### defensible_position
**Purpose**: Find locations to hold against attack

**Generator**: Object positions with tactical properties

**Tests**:
- `has_cover` (weight: 0.30)
- `elevation` (weight: 0.25) - Prefer high ground
- `escape_routes` (weight: 0.25)
- `property_check: supplies_nearby` (weight: 0.20)

---

*This document describes the Mapping System as implemented in lib-mapping. For implementation details, see the source code and schema definitions.*
