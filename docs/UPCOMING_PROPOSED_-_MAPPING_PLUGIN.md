# Map Service Architecture - Spatial Data Management for Arcadia

> **Status**: Draft
> **Last Updated**: 2025-11-27
> **Related Systems**: [[Microservices Architecture for Game Systems]], [[Dynamic World-Driven Systems]], [[Revolutionary Engagement Combat System]], [[Dungeon System]]
> **Tags**: #technical-architecture #map-service #spatial-data #bannou #world-simulation

## Overview

The Map Service provides **abstract spatial data management** for Arcadia's world simulation. Rather than representing just "game world chunks," maps are flexible data structures that can represent any spatial or relational information - from regional terrain to dungeon interiors to star charts to logical entity relationships.

Maps are the foundational layer through which services coordinate world state, territorial ownership, environmental effects, and resource distribution. The Map Service handles concurrency control, event-driven updates, multi-layer data persistence, and hierarchical nesting of spatial information.

## Core Architecture Principles

### Maps as Abstract Data Structures

Maps are **not limited to geographic representations**. The Map Service supports multiple map types optimized for different use cases:

#### Texture/Color Maps (Most Common)
**Structure**: 2D 8-bit arrays with multiple channels
- **Format**: RGBA channels (4 x 0-255 integer values per cell)
- **Advantages**:
  - Compatible with standard image processing tools and libraries
  - Import/export in widely understood formats
  - GPU acceleration possible for certain operations
  - Texture/color functionality shortcuts for visualization and processing
- **Channel Usage**:
  - RGB channels: Arbitrary data (terrain type, ownership ID, resource density, etc.)
  - Alpha channel: Optional modifier/opacity, or simply a fourth data channel
- **Use Cases**: Regional terrain, ownership maps, mana density layers, resource distribution, weather patterns

#### Dictionary/Relational Maps
**Structure**: Key-value mappings (object ID → set of related object IDs)
- **Format**: Sparse data structure for empty-space-dominant domains
- **Advantages**:
  - Efficient for sparsely populated spaces
  - Natural representation for relational data
  - Low memory overhead for mostly-empty regions
- **Use Cases**: Star maps, sea charts, trade route networks, social relationship maps

#### Tree-Based Maps
**Structure**: Hierarchical node structures
- **Format**: Parent-child relationships with arbitrary depth
- **Advantages**:
  - Efficient spatial queries (quadtrees, octrees)
  - Natural representation for hierarchical ownership
  - Good for variable-density data
- **Use Cases**: Administrative hierarchies, nested territorial claims, organizational structures

### Multi-Layer Architecture

Each map resource can contain **multiple data layers** with independent characteristics:

```yaml
map_resource:
  id: "region-omega-central"
  type: "texture_map"
  dimensions: [1024, 1024]

  layers:
    terrain:
      persistence: "permanent"
      storage: "durable"
      update_frequency: "rare"

    ownership:
      persistence: "permanent"
      storage: "durable"
      update_frequency: "moderate"
      authority: "region_ai_exclusive"

    mana_density:
      persistence: "ephemeral"
      storage: "cache"
      update_frequency: "high"
      recalculation: "periodic"

    weather_effects:
      persistence: "ephemeral"
      storage: "cache"
      update_frequency: "continuous"
      ttl: "4_hours"  # One in-game day

    combat_hazards:
      persistence: "ephemeral"
      storage: "cache"
      update_frequency: "event_driven"
      ttl: "variable"
```

#### Persistence Categories

**Permanent/Durable Layers**:
- Stored in persistent storage (MySQL, Azure Blob, AWS S3, etc.)
- Survives service restarts and deployments
- Examples: terrain, ownership, permanent structures, geographic features

**Ephemeral/Cache Layers**:
- Stored in cache systems (Redis, in-memory)
- Recalculated or regenerated as needed
- Examples: current mana density, weather effects, temporary combat hazards, real-time environmental conditions

**Configurable Per Layer**: Each layer's persistence strategy is determined at map creation and can vary based on deployment environment (Azure vs AWS vs local development).

## Hierarchical Nesting and Bidirectional Linking

### Nested Map Structure

Maps can contain references to child maps and maintain awareness of their parent placement:

```yaml
# Parent map contains object references
region_map:
  id: "region-omega-central"
  child_objects:
    - object_id: "town-riverside"
      map_reference: "town-riverside-interior"
      placement: { x: 512, y: 384, bounds: [64, 64] }

    - object_id: "dungeon-ancient-mines"
      map_reference: "dungeon-ancient-mines-interior"
      placement: { x: 128, y: 896, bounds: [32, 32] }

# Child map knows its placement context
dungeon_map:
  id: "dungeon-ancient-mines-interior"
  parent_map: "region-omega-central"
  parent_placement: { x: 128, y: 896 }
  world_coordinates: { realm: "omega", region: "central", local: [128, 896] }
```

### Map Existence vs Placement

**Critical Distinction**: Maps can exist as pure data structures without being "placed" in the game world.

- **Unplaced Maps**: Exist in storage, can be modified, but have no world coordinates
- **Placed Maps**: Linked into the map hierarchy with defined placement in a parent map

**Example - Dungeon Creation Flow**:
1. Player/system creates dungeon interior map (exists but unplaced)
2. Dungeon map is designed, populated with rooms and features
3. Entrance placement requires negotiation with parent map owner
4. Upon successful negotiation, child map is linked into parent map hierarchy
5. Dungeon now "exists" in the game world with accessible entrance

### Event Propagation

Events flow bidirectionally through the map hierarchy:

#### Upward Propagation (Child → Parent)
- Resource changes in child maps aggregate into bulk events
- Parent maps receive summarized change notifications
- Parent can choose to track aggregated data or disregard
- Enables region-level awareness of distributed changes

```yaml
# Child dungeon emits resource event
child_event:
  source: "dungeon-ancient-mines-interior"
  type: "resource_change"
  details:
    mana_generated: +150
    monsters_spawned: 3

# Parent region receives aggregated event
parent_event:
  source: "dungeon-ancient-mines-interior"
  type: "child_resource_summary"
  aggregation_period: "1_hour"
  details:
    net_mana_change: +1200
    population_change: +15
```

#### Downward Propagation (Parent → Child)
- Environmental changes affecting child regions
- Time progression and weather updates
- Administrative changes (ownership transfers, policy updates)

## Concurrency Control and Authority

### The Checkout Model

For large-scale modifications, the Map Service implements a **checkout system** to prevent race conditions:

#### Standard Checkout Flow
1. **Request Checkout**: Service requests exclusive write access to specific layer(s)
2. **Checkout Granted**: Map Service grants checkout with timeout
3. **Modifications Made**: Service applies changes to checked-out layers
4. **Commit Changes**: Service commits modifications
5. **Release Checkout**: Exclusive access released

#### Authority-Based Access

Different entities have **permanent or elevated authority** over specific layers:

```yaml
authority_model:
  region_ai:
    - layer: "ownership"
      access: "exclusive_permanent"
    - layer: "terrain"
      access: "exclusive_permanent"
    - layer: "build_restrictions"
      access: "exclusive_permanent"

  dungeon_cores:
    - layer: "mana_density"
      access: "write_within_domain"
    - layer: "monster_population"
      access: "write_within_domain"

  combat_service:
    - layer: "combat_hazards"
      access: "write_temporary"
      scope: "active_engagement_area"
```

### Region AI as Permanent Authority

**Region AIs effectively always have their regional maps checked out** for ownership and structural layers. Other entities don't checkout these layers directly but instead:

1. **Request changes through Region AI APIs** (not Map APIs directly)
2. **Negotiate terms** (cost, permissions, restrictions)
3. **Region AI applies approved changes** on their behalf

This ensures:
- Consistent enforcement of build restrictions (riverbeds, protected areas)
- Relationship-based cost modifiers for expansion
- Centralized conflict resolution for overlapping claims
- Enforcement of regional policies and restrictions

### Negotiation vs Direct Checkout

| Operation | Method | Authority |
|-----------|--------|-----------|
| Read any layer | Direct Map API | Any authorized service |
| Write cache layers | Direct Map API with checkout | Layer-specific permissions |
| Modify ownership | Region AI negotiation | Region AI exclusive |
| Place child maps | Region AI negotiation | Region AI exclusive |
| Expand domains | Region AI negotiation | Region AI exclusive |

## Event-Driven Updates via RabbitMQ

### Per-Map Event Queues

Each map resource has its own RabbitMQ queue for receiving updates:

```yaml
queue_configuration:
  map_id: "region-omega-central"
  queue_name: "map.region-omega-central.updates"

  rate_limiting:
    max_events_per_second: 100
    burst_allowance: 500

  consolidation:
    enabled: true
    window: "500ms"
    strategy: "merge_compatible"
```

### Rate Limiting and Overload Prevention

**Queue-Based Rate Limiting**:
- Each map's queue inherently prevents update storms
- High-frequency updates queue rather than overwhelm
- Configurable rate limits per map based on expected activity

**Event Consolidation**:
- Multiple queued events can merge into equivalent larger changes
- Similar to merging git diffs
- Reduces processing overhead for rapidly-changing data

```yaml
# Before consolidation: 10 separate mana updates
events:
  - { type: "mana_update", cell: [10, 20], delta: +5 }
  - { type: "mana_update", cell: [10, 20], delta: +3 }
  - { type: "mana_update", cell: [10, 21], delta: +7 }
  # ... more events

# After consolidation: 2 aggregated updates
consolidated:
  - { type: "mana_update", cell: [10, 20], delta: +8 }
  - { type: "mana_update", cell: [10, 21], delta: +7 }
```

### Fixed-Frequency Output Events

**Guaranteed Non-Overload for Listeners**:
- Map change events emitted at **configured fixed intervals**
- Listeners cannot be overloaded regardless of internal update frequency
- Frequency determined at map creation based on use case

```yaml
output_configuration:
  map_id: "region-omega-central"

  event_channels:
    ownership_changes:
      frequency: "on_change"  # Immediate for critical changes
      max_rate: "1_per_second"

    mana_density_updates:
      frequency: "every_30_seconds"
      aggregation: "delta_summary"

    weather_updates:
      frequency: "every_5_minutes"

    resource_summaries:
      frequency: "every_hour"
      aggregation: "complete_snapshot"
```

## Integration with Game Services

### Combat Session Integration

The Combat Service uses Map APIs for environmental awareness during engagements:

```yaml
combat_map_integration:
  reads:
    - layer: "terrain"
      purpose: "ground stability, elevation, structural integrity"
    - layer: "mana_density"
      purpose: "environmental magical energy affects available abilities"
    - layer: "weather_effects"
      purpose: "visibility, movement modifiers"
    - layer: "hazards"
      purpose: "existing environmental dangers"

  writes:
    - layer: "combat_hazards"
      purpose: "temporary hazards from combat (fire, debris, magical effects)"
      persistence: "ephemeral"
      ttl: "combat_duration + cleanup_period"
```

### World State Service Integration

The World State Service coordinates time progression and global effects through maps:

```yaml
world_state_integration:
  broadcasts:
    - time_progression: "all_region_maps"
    - weather_changes: "affected_region_maps"
    - seasonal_effects: "all_region_maps"

  aggregates:
    - population_summaries: "from_all_child_maps"
    - resource_totals: "from_all_child_maps"
    - economic_activity: "from_all_child_maps"
```

### NPC Agent Integration

Character agents query map data for decision-making and pathfinding:

```yaml
npc_map_integration:
  queries:
    - "nearby_resources(location, radius)"
    - "path_to_destination(start, end, constraints)"
    - "local_mana_density(location)"
    - "ownership_at(location)"
    - "build_restrictions(location)"

  subscriptions:
    - "ownership_changes_in_area(area)"
    - "resource_availability_changes(resource_type, area)"
```

### Guardian Spirit / Real Estate Integration

Property ownership and territorial claims flow through the Map Service:

```yaml
real_estate_integration:
  ownership_layer:
    data_type: "entity_id_per_cell"
    authority: "region_ai_exclusive"

  operations:
    claim_property:
      method: "region_ai_negotiation"
      requirements: ["available", "not_restricted", "payment"]

    transfer_property:
      method: "region_ai_negotiation"
      requirements: ["owner_consent", "buyer_qualified"]

    expand_property:
      method: "region_ai_negotiation"
      requirements: ["adjacent_available", "expansion_cost"]
```

## Map Generation and Lifecycle

### Initial Generation

**Regional Maps**: Generated on game server/region creation
- Large-scale terrain and geographic features
- Initial ownership (unclaimed, NPC settlements, etc.)
- Build restriction zones (riverbeds, protected areas, etc.)

**Sub-Maps**: Generated on-demand as needed
- Town interiors created when towns are founded
- Dungeon interiors created when dungeons form
- Building interiors created when buildings are constructed

### Lazy Generation Pattern

```yaml
generation_pattern:
  region_creation:
    immediate:
      - terrain_layer: "complete"
      - ownership_layer: "initial_claims_only"
      - restriction_layer: "complete"
    deferred:
      - sub_maps: "generated_on_first_access"
      - detailed_resources: "generated_on_first_query"

  sub_map_creation:
    trigger: "entity_establishment"  # Town founded, dungeon formed, building constructed
    process:
      1. "create_map_structure"
      2. "generate_initial_content"
      3. "negotiate_placement_in_parent"
      4. "link_bidirectionally"
      5. "emit_creation_event"
```

## Technical Implementation Details

### Storage Strategy by Deployment

**Development (Local)**:
- Durable layers: SQLite or local file storage
- Cache layers: In-memory dictionary
- Event queues: Local RabbitMQ or in-process queue

**Production (Azure)**:
- Durable layers: Azure Blob Storage + Azure SQL
- Cache layers: Azure Cache for Redis
- Event queues: Azure Service Bus or RabbitMQ on AKS

**Production (AWS)**:
- Durable layers: S3 + RDS/Aurora
- Cache layers: ElastiCache (Redis)
- Event queues: Amazon MQ (RabbitMQ) or SQS

### Texture Map Serialization

For texture/color maps, standard image formats enable interoperability:

```yaml
serialization:
  format: "PNG"  # Lossless, widely supported
  channels:
    R: "primary_data_channel"
    G: "secondary_data_channel"
    B: "tertiary_data_channel"
    A: "modifier_or_fourth_channel"

  import_export:
    - "standard image libraries"
    - "GPU texture processing"
    - "external visualization tools"
    - "procedural generation pipelines"
```

### API Schema Pattern

```yaml
# schemas/map-api.yaml (conceptual)
paths:
  /maps/{mapId}:
    get:
      summary: "Get map metadata and available layers"

  /maps/{mapId}/layers/{layerId}:
    get:
      summary: "Read layer data (full or region)"
      parameters:
        - bounds: "optional region bounds"
        - format: "raw|image|json"

  /maps/{mapId}/layers/{layerId}/checkout:
    post:
      summary: "Request exclusive write access"
      parameters:
        - duration: "checkout timeout"
        - scope: "full|region bounds"

  /maps/{mapId}/layers/{layerId}/commit:
    post:
      summary: "Commit changes and release checkout"

  /maps/{mapId}/query:
    post:
      summary: "Spatial query across layers"
      parameters:
        - query_type: "point|region|path"
        - layers: "layers to query"

  /maps/{mapId}/subscribe:
    post:
      summary: "Subscribe to map change events"
      parameters:
        - event_types: "ownership|resources|all"
        - frequency: "immediate|batched"
```

## Performance Considerations

### Scalability Patterns

**Horizontal Scaling**:
- Map Service instances can be sharded by region
- Each instance manages a subset of map resources
- Cross-region queries route through service mesh

**Caching Strategy**:
- Frequently-read layers cached at edge
- Write-through caching for durable layers
- TTL-based expiration for ephemeral layers

**Query Optimization**:
- Spatial indexes for region-based queries
- Layer-specific query optimization
- Aggregation caching for common summaries

### Load Management

**Expected Load Patterns**:
- High read volume: NPC pathfinding, resource queries, ownership checks
- Moderate write volume: Ownership changes, resource updates
- Burst writes: Combat effects, weather changes, event-driven updates

**Mitigation Strategies**:
- Read replicas for query-heavy layers
- Write batching for high-frequency updates
- Event consolidation for burst scenarios

## Related Documents

- [[Microservices Architecture for Game Systems]] - Overall service architecture
- [[Dynamic World-Driven Systems]] - World state and territorial mechanics
- [[Revolutionary Engagement Combat System]] - Combat integration with Maps API
- [[Dungeon System]] - Dungeon-specific map integration details
- [[Distributed Agent Architecture]] - NPC agent map queries

---
*This document is part of the [[README|Arcadia Knowledge Base]]*
