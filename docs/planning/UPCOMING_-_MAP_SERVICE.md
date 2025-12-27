# Map Service Architecture - Spatial Data Management for Arcadia

**Status**: PLANNING
**Priority**: Medium
**Complexity**: High
**Estimated Duration**: 6-8 weeks
**Dependencies**: Location Service (conceptual), Asset Service (for large blob storage)
**Last Updated**: 2025-12-27

---

## Executive Summary

The Map Service manages spatial data (heightmaps, tilemaps, overlays, metadata objects) for Arcadia's game worlds. It provides a unified API for creating, querying, and modifying map layers with support for concurrent editing, hierarchical map composition, and real-time delta broadcasting.

### Key Capabilities

1. **Multi-Layer Maps**: Each map supports multiple typed layers (terrain, ownership, hazards, metadata objects)
2. **Hierarchical Composition**: Parent/child map relationships with placement metadata
3. **Concurrent Editing**: Checkout/commit model with distributed locking and ETag concurrency
4. **Real-Time Updates**: Event-driven delta broadcasting with configurable aggregation
5. **Metadata Objects**: Arbitrary objects (rocks, trees, buildings, POIs) attached to map locations
6. **Schema Registry**: Extensible object type validation for metadata objects

---

## Tenet Compliance

| Tenet | Requirement | Implementation |
|-------|-------------|----------------|
| **Tenet 1** (Schema-First) | All APIs in OpenAPI YAML | `schemas/map-api.yaml`, `schemas/map-events.yaml` |
| **Tenet 4** (Infrastructure Libs) | No direct Redis/RabbitMQ | lib-state, lib-messaging, lib-mesh only |
| **Tenet 5** (Event-Driven) | All state changes emit events | Typed events via `IMessageBus` |
| **Tenet 8** (Return Pattern) | `(StatusCodes, Response?)` tuples | All service methods |
| **Tenet 9** (Multi-Instance) | No required in-memory state | `IDistributedLockProvider` for checkouts |
| **Tenet 13** (X-Permissions) | All endpoints declare permissions | Role + state requirements |
| **Tenet 14** (Polymorphic) | Entity ID + Type pattern | Metadata object location references |
| **Tenet 16** (Naming) | Standard conventions | `{Entity}{Action}Event`, `{entity}.{action}` topics |

### Exception Requiring Approval

> **EXCEPTION: Browser-Facing GET Endpoints (Tenet 15)**
>
> If map visualization endpoints are needed for browser debugging/admin tools, they would require GET methods with path parameters. This violates Tenet 1 (POST-only for WebSocket routing).
>
> **Proposed Resolution**: Collaborate with Website plugin - Map Service provides visualization payloads via POST API; Website handles browser delivery with caching. No GET endpoints in Map Service itself.
>
> **Status**: Requires explicit approval if GET endpoints are later deemed necessary.

---

## Core Concepts

### Maps as Abstract Spatial Assets

Maps represent any spatially-organized data:
- **Terrain**: Heightmaps, biome data, resource deposits
- **Overlays**: Radiation zones, mana fields, weather effects, vegetation density
- **Ownership**: Territory control, property boundaries
- **Logical Maps**: Relationship graphs, trade routes, navigation meshes

Maps may be **unplaced** (templates/definitions) or **placed** within a parent map hierarchy.

### Layers

Each map contains multiple typed layers with independent:
- **Format**: How data is stored (`texture`, `sparse_dict`, `tree`, `metadata_objects`)
- **Persistence**: `durable` (survives restarts) or `cache` (ephemeral with TTL)
- **Authority**: Which service/role can modify the layer
- **Update Cadence**: How frequently changes are broadcast

### Metadata Objects

Arbitrary objects attached to map cells/regions:
- Trees with harvest state, rocks with destruction progress
- Buildings, chests, spawn points, quest markers
- Each object has a `type` validated against a registered schema
- Objects can link to Location Service entities via ID+Type pattern (Tenet 14)

### Hierarchy

Maps form parent/child relationships:
- Child maps know their parent and placement coordinates
- Parents track child placement records
- Placement changes emit events for downstream consumers
- Enables world → continent → region → zone composition

### Authority Model

Layer-level authority controls write access:
- Region AI owns structural/ownership layers
- Combat service can write ephemeral hazard overlays
- Enforced via x-permissions + server-side validation
- Checkout model prevents concurrent conflicting writes

---

## Data Models

### MapDefinition

**State Store**: `map-statestore` via `IStateStore<MapDefinition>`
**Key Pattern**: `map:{mapId}`

```yaml
MapDefinition:
  type: object
  required: [mapId, mapType, dimensions, layers, createdAt]
  properties:
    mapId:
      type: string
      format: uuid
    mapType:
      type: string
      enum: [texture, sparse_dict, tree, logical]
    dimensions:
      type: array
      minItems: 2
      maxItems: 3
      items: { type: integer, minimum: 1 }
      description: "[width, height] or [width, height, depth]"
    layers:
      type: array
      items: { $ref: '#/components/schemas/LayerDefinition' }
    parentMapId:
      type: string
      format: uuid
      nullable: true
    parentPlacement:
      $ref: '#/components/schemas/Placement'
      nullable: true
    allowedObjectTypes:
      type: array
      items: { type: string }
      description: Object types allowed in this map's metadata layers
    createdAt:
      type: string
      format: date-time
    createdBy:
      type: string
      format: uuid
```

### LayerDefinition

```yaml
LayerDefinition:
  type: object
  required: [layerId, format, persistence]
  properties:
    layerId:
      type: string
      pattern: '^[a-z0-9-]+$'
    format:
      type: string
      enum: [texture, sparse_dict, tree, metadata_objects]
    channelDescriptor:
      type: array
      items: { type: string }
      nullable: true
      description: "For texture format: channel meanings [R, G, B, A]"
    persistence:
      type: string
      enum: [durable, cache]
    ttlSeconds:
      type: integer
      nullable: true
      description: For cache persistence, how long before expiry
    authority:
      type: string
      description: Service or role that owns write access
    encoding:
      type: string
      nullable: true
      description: Optional encoding hints (e.g., 'png', 'lz4')
    version:
      type: integer
      format: int64
      default: 0
```

### Placement

```yaml
Placement:
  type: object
  required: [x, y]
  properties:
    x: { type: integer }
    y: { type: integer }
    z: { type: integer, nullable: true }
    rotation: { type: number, nullable: true }
    scale: { type: number, default: 1.0 }
```

### MetadataObject

**Key Pattern**: `map:{mapId}:metadata:{objectId}`
**Index Pattern**: `map:{mapId}:metadata-index:{cellKey}` (for spatial lookups)

```yaml
MetadataObject:
  type: object
  required: [objectId, objectType, state, createdAt]
  properties:
    objectId:
      type: string
      format: uuid
    objectType:
      type: string
      description: Must be registered in schema registry
    state:
      type: object
      additionalProperties: true
      description: Object-specific state, validated against type schema
    bounds:
      $ref: '#/components/schemas/Bounds'
      nullable: true
    locationRef:
      type: object
      nullable: true
      description: Optional link to Location service entity (Tenet 14)
      properties:
        locationId: { type: string, format: uuid }
        locationType: { type: string }
    createdAt:
      type: string
      format: date-time
    updatedAt:
      type: string
      format: date-time
```

### Bounds

```yaml
Bounds:
  type: object
  required: [minX, minY, maxX, maxY]
  properties:
    minX: { type: integer }
    minY: { type: integer }
    maxX: { type: integer }
    maxY: { type: integer }
    minZ: { type: integer, nullable: true }
    maxZ: { type: integer, nullable: true }
```

### ObjectTypeSchema

**Key Pattern**: `map:object-type:{objectType}`

```yaml
ObjectTypeSchema:
  type: object
  required: [objectType, schema, createdAt]
  properties:
    objectType:
      type: string
      pattern: '^[a-z0-9-]+$'
    schema:
      type: object
      additionalProperties: true
      description: JSON Schema for validating object state
    requiredFields:
      type: array
      items: { type: string }
    createdAt:
      type: string
      format: date-time
    createdBy:
      type: string
      format: uuid
```

---

## API Endpoints

All endpoints follow Tenet 1 (POST-only, schema-first) and Tenet 13 (x-permissions).

### Map Management

```yaml
/maps/create:
  post:
    operationId: createMap
    summary: Create a new map definition
    x-permissions:
      - role: developer
    requestBody:
      schema: { $ref: '#/components/schemas/CreateMapRequest' }
    responses:
      '200': { schema: { $ref: '#/components/schemas/MapResponse' } }
      '400': { description: Invalid layer configuration }
      '409': { description: Map ID already exists }

/maps/get:
  post:
    operationId: getMap
    summary: Get map definition by ID
    x-permissions:
      - role: user
    requestBody:
      schema: { $ref: '#/components/schemas/GetMapRequest' }

/maps/list:
  post:
    operationId: listMaps
    summary: List maps with optional filters
    x-permissions:
      - role: user
    requestBody:
      schema: { $ref: '#/components/schemas/ListMapsRequest' }
      description: Filter by parentMapId, mapType, bounds

/maps/delete:
  post:
    operationId: deleteMap
    summary: Delete a map and all its layers
    x-permissions:
      - role: admin
```

### Hierarchy Management

```yaml
/maps/place-child:
  post:
    operationId: placeChildMap
    summary: Place a child map within a parent
    x-permissions:
      - role: developer
        states:
          region: owns_area
    requestBody:
      schema: { $ref: '#/components/schemas/PlaceChildMapRequest' }
    responses:
      '200': { description: Emits map.placed event }
      '404': { description: Parent or child map not found }
      '409': { description: Child already placed elsewhere }

/maps/remove-child:
  post:
    operationId: removeChildMap
    summary: Remove a child map from its parent
    x-permissions:
      - role: developer
        states:
          region: owns_area
```

### Layer Operations

```yaml
/maps/layers/read:
  post:
    operationId: readLayer
    summary: Read layer data, optionally within bounds
    x-permissions:
      - role: user
    requestBody:
      schema:
        type: object
        required: [mapId, layerId]
        properties:
          mapId: { type: string, format: uuid }
          layerId: { type: string }
          bounds: { $ref: '#/components/schemas/Bounds', nullable: true }
          format: { type: string, enum: [raw, image, json], default: raw }
          version: { type: integer, format: int64, nullable: true }

/maps/layers/checkout:
  post:
    operationId: checkoutLayer
    summary: Acquire exclusive write lock on layer
    x-permissions:
      - role: developer
        states:
          region: owns_area
    requestBody:
      schema:
        type: object
        required: [mapId, layerId]
        properties:
          mapId: { type: string, format: uuid }
          layerId: { type: string }
          bounds: { $ref: '#/components/schemas/Bounds', nullable: true }
          durationSeconds: { type: integer, default: 300, maximum: 3600 }
    responses:
      '200':
        schema:
          type: object
          properties:
            checkoutToken: { type: string }
            etag: { type: string }
            expiresAt: { type: string, format: date-time }
            currentVersion: { type: integer, format: int64 }
      '409': { description: Layer already checked out }

/maps/layers/commit:
  post:
    operationId: commitLayer
    summary: Commit layer changes and release lock
    x-permissions:
      - role: developer
        states:
          region: owns_area
    requestBody:
      schema:
        type: object
        required: [mapId, layerId, checkoutToken, etag, payload]
        properties:
          mapId: { type: string, format: uuid }
          layerId: { type: string }
          checkoutToken: { type: string }
          etag: { type: string }
          payload: { type: string, format: byte, description: Base64-encoded layer data }
          bounds: { $ref: '#/components/schemas/Bounds', nullable: true }
    responses:
      '200':
        schema:
          type: object
          properties:
            newVersion: { type: integer, format: int64 }
            newEtag: { type: string }
      '409': { description: ETag mismatch - concurrent modification }
      '410': { description: Checkout expired }

/maps/layers/release:
  post:
    operationId: releaseCheckout
    summary: Release checkout without committing
    x-permissions:
      - role: developer
```

### Metadata Objects

```yaml
/maps/metadata/upsert:
  post:
    operationId: upsertMetadataObject
    summary: Create or update a metadata object
    x-permissions:
      - role: developer
    requestBody:
      schema: { $ref: '#/components/schemas/UpsertMetadataObjectRequest' }
    responses:
      '200': { schema: { $ref: '#/components/schemas/MetadataObjectResponse' } }
      '400': { description: State validation failed against object type schema }

/maps/metadata/remove:
  post:
    operationId: removeMetadataObject
    summary: Remove a metadata object
    x-permissions:
      - role: developer

/maps/metadata/list:
  post:
    operationId: listMetadataObjects
    summary: List metadata objects in bounds or by type
    x-permissions:
      - role: user
    requestBody:
      schema:
        type: object
        required: [mapId, layerId]
        properties:
          mapId: { type: string, format: uuid }
          layerId: { type: string }
          bounds: { $ref: '#/components/schemas/Bounds', nullable: true }
          objectType: { type: string, nullable: true }
          page: { type: integer, default: 1 }
          pageSize: { type: integer, default: 100, maximum: 500 }
```

### Object Type Registry

```yaml
/maps/object-types/register:
  post:
    operationId: registerObjectType
    summary: Register a new metadata object type with validation schema
    x-permissions:
      - role: developer
    requestBody:
      schema:
        type: object
        required: [objectType, schema]
        properties:
          objectType: { type: string, pattern: '^[a-z0-9-]+$' }
          schema: { type: object, additionalProperties: true }
          requiredFields: { type: array, items: { type: string } }

/maps/object-types/assign:
  post:
    operationId: assignObjectTypeToMap
    summary: Allow an object type to be used in a specific map
    x-permissions:
      - role: developer
    requestBody:
      schema:
        type: object
        required: [mapId, objectType]
        properties:
          mapId: { type: string, format: uuid }
          objectType: { type: string }

/maps/object-types/list:
  post:
    operationId: listObjectTypes
    summary: List registered object types
    x-permissions:
      - role: user
```

### Spatial Queries

```yaml
/maps/query:
  post:
    operationId: queryMap
    summary: Query map data at point, region, or along path
    x-permissions:
      - role: user
    requestBody:
      schema:
        type: object
        required: [mapId]
        properties:
          mapId: { type: string, format: uuid }
          queryType: { type: string, enum: [point, bounds, path] }
          point: { type: object, properties: { x: { type: integer }, y: { type: integer } } }
          bounds: { $ref: '#/components/schemas/Bounds' }
          path: { type: array, items: { type: object, properties: { x: { type: integer }, y: { type: integer } } } }
          layers: { type: array, items: { type: string }, description: Layer IDs to sample }
          includeMetadata: { type: boolean, default: false }
```

### Subscriptions

```yaml
/maps/subscribe:
  post:
    operationId: subscribeToMapEvents
    summary: Subscribe to map update events
    x-permissions:
      - role: user
    requestBody:
      schema:
        type: object
        required: [mapId]
        properties:
          mapId: { type: string, format: uuid }
          eventTypes: { type: array, items: { type: string }, description: Event types to receive }
          bounds: { $ref: '#/components/schemas/Bounds', nullable: true }
          aggregationSeconds: { type: integer, default: 5, minimum: 1, maximum: 60 }
    responses:
      '200':
        schema:
          type: object
          properties:
            subscriptionId: { type: string, format: uuid }
            message: { type: string }
```

---

## Events

All events are typed models published via `IMessageBus` (Tenet 4, Tenet 5).

### Lifecycle Events

Use `x-lifecycle` in `map-events.yaml` to auto-generate Created/Updated/Deleted events for `MapDefinition`.

```yaml
# map-events.yaml
x-lifecycle:
  Map:
    model:
      mapId: { type: string, format: uuid, primary: true, required: true }
      mapType: { type: string, required: true }
      parentMapId: { type: string, format: uuid }
    sensitive: []  # No sensitive fields
```

### Custom Events

```yaml
MapPlacedEvent:
  type: object
  required: [eventId, timestamp, mapId, parentMapId, placement]
  properties:
    eventId: { type: string, format: uuid }
    timestamp: { type: string, format: date-time }
    mapId: { type: string, format: uuid }
    parentMapId: { type: string, format: uuid }
    placement: { $ref: '#/components/schemas/Placement' }

MapUnplacedEvent:
  type: object
  required: [eventId, timestamp, mapId, previousParentMapId]
  properties:
    eventId: { type: string, format: uuid }
    timestamp: { type: string, format: date-time }
    mapId: { type: string, format: uuid }
    previousParentMapId: { type: string, format: uuid }

MapLayerUpdatedEvent:
  type: object
  required: [eventId, timestamp, mapId, layerId, version]
  properties:
    eventId: { type: string, format: uuid }
    timestamp: { type: string, format: date-time }
    mapId: { type: string, format: uuid }
    layerId: { type: string }
    version: { type: integer, format: int64 }
    bounds: { $ref: '#/components/schemas/Bounds', nullable: true }
    deltaType: { type: string, enum: [delta, snapshot] }
    payloadRef: { type: string, nullable: true, description: Asset reference for large payloads }

MapMetadataUpdatedEvent:
  type: object
  required: [eventId, timestamp, mapId, layerId, objectId, objectType, action]
  properties:
    eventId: { type: string, format: uuid }
    timestamp: { type: string, format: date-time }
    mapId: { type: string, format: uuid }
    layerId: { type: string }
    objectId: { type: string, format: uuid }
    objectType: { type: string }
    action: { type: string, enum: [created, updated, deleted] }
    state: { type: object, additionalProperties: true, nullable: true }
```

### Delta Broadcasting

For high-frequency non-critical updates (e.g., combat hazards, weather effects):

```yaml
MapDeltaBroadcastEvent:
  type: object
  required: [eventId, timestamp, mapId, deltas]
  properties:
    eventId: { type: string, format: uuid }
    timestamp: { type: string, format: date-time }
    mapId: { type: string, format: uuid }
    deltas:
      type: array
      items:
        type: object
        properties:
          layerId: { type: string }
          bounds: { $ref: '#/components/schemas/Bounds', nullable: true }
          deltaPayload: { type: object, additionalProperties: true }
    mayDrop:
      type: boolean
      default: true
      description: Explicitly non-critical; consumers may ignore if overloaded
```

**Design Decision**: Delta broadcasts use wide aggregation (default 5 seconds) and are explicitly droppable. Critical updates MUST use the checkout/commit API flow.

### Topic Naming

| Topic | Description |
|-------|-------------|
| `map.created` | New map definition created |
| `map.updated` | Map definition modified |
| `map.deleted` | Map deleted |
| `map.placed` | Child map placed in parent |
| `map.unplaced` | Child map removed from parent |
| `map.layer.updated` | Layer data committed |
| `map.metadata.updated` | Metadata object changed |
| `map.delta.broadcast` | Non-critical delta aggregation |

---

## Infrastructure Lib Usage

### State Management (lib-state)

```csharp
public partial class MapService : IMapService
{
    private readonly IStateStore<MapDefinition> _mapStore;
    private readonly IStateStore<MetadataObject> _metadataStore;
    private readonly IStateStore<ObjectTypeSchema> _schemaStore;

    public MapService(IStateStoreFactory stateStoreFactory)
    {
        _mapStore = stateStoreFactory.Create<MapDefinition>("map");
        _metadataStore = stateStoreFactory.Create<MetadataObject>("map");
        _schemaStore = stateStoreFactory.Create<ObjectTypeSchema>("map");
    }

    public async Task<(StatusCodes, MapResponse?)> CreateMapAsync(
        CreateMapRequest body, CancellationToken ct)
    {
        var map = new MapDefinition
        {
            MapId = body.MapId,
            MapType = body.MapType,
            Dimensions = body.Dimensions,
            Layers = body.Layers,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _mapStore.SaveAsync($"map:{body.MapId}", map, cancellationToken: ct);

        await _messageBus.PublishAsync("map.created", new MapCreatedEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = DateTimeOffset.UtcNow,
            MapId = map.MapId,
            MapType = map.MapType.ToString()
        });

        return (StatusCodes.OK, MapResponse.FromDefinition(map));
    }
}
```

### Distributed Locking for Checkout (lib-state)

```csharp
public async Task<(StatusCodes, LayerCheckoutResponse?)> CheckoutLayerAsync(
    LayerCheckoutRequest body, CancellationToken ct)
{
    var lockResource = $"map:{body.MapId}:layer:{body.LayerId}";
    var lockOwner = Guid.NewGuid().ToString();
    var expirySeconds = Math.Min(body.DurationSeconds ?? 300, 3600);

    await using var lockResponse = await _lockProvider.LockAsync(
        resourceId: lockResource,
        lockOwner: lockOwner,
        expiryInSeconds: expirySeconds,
        cancellationToken: ct);

    if (!lockResponse.Success)
    {
        _logger.LogWarning("Layer checkout failed - already locked: {MapId}/{LayerId}",
            body.MapId, body.LayerId);
        return (StatusCodes.Conflict, null);
    }

    // Get current layer state for ETag
    var (layerData, etag) = await _layerStore.GetWithETagAsync(
        $"map:{body.MapId}:layer:{body.LayerId}", ct);

    var checkoutToken = Guid.NewGuid().ToString();
    await _checkoutStore.SaveAsync($"checkout:{checkoutToken}", new CheckoutRecord
    {
        MapId = body.MapId,
        LayerId = body.LayerId,
        LockOwner = lockOwner,
        ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expirySeconds)
    }, new StateOptions { Ttl = TimeSpan.FromSeconds(expirySeconds) }, ct);

    return (StatusCodes.OK, new LayerCheckoutResponse
    {
        CheckoutToken = checkoutToken,
        Etag = etag ?? string.Empty,
        ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expirySeconds),
        CurrentVersion = layerData?.Version ?? 0
    });
}
```

### ETag Concurrency for Commits

```csharp
public async Task<(StatusCodes, LayerCommitResponse?)> CommitLayerAsync(
    LayerCommitRequest body, CancellationToken ct)
{
    // Validate checkout token
    var checkout = await _checkoutStore.GetAsync($"checkout:{body.CheckoutToken}", ct);
    if (checkout == null || checkout.ExpiresAt < DateTimeOffset.UtcNow)
        return (StatusCodes.Gone, null);  // Checkout expired

    // Attempt optimistic write with ETag
    var layerData = new LayerData
    {
        Payload = body.Payload,
        Version = checkout.CurrentVersion + 1,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    var saved = await _layerStore.TrySaveAsync(
        $"map:{body.MapId}:layer:{body.LayerId}",
        layerData,
        body.Etag,
        ct);

    if (!saved)
    {
        _logger.LogWarning("Layer commit rejected - ETag mismatch: {MapId}/{LayerId}",
            body.MapId, body.LayerId);
        return (StatusCodes.Conflict, null);
    }

    // Clean up checkout
    await _checkoutStore.DeleteAsync($"checkout:{body.CheckoutToken}", ct);

    // Publish update event
    await _messageBus.PublishAsync("map.layer.updated", new MapLayerUpdatedEvent
    {
        EventId = Guid.NewGuid(),
        Timestamp = DateTimeOffset.UtcNow,
        MapId = body.MapId,
        LayerId = body.LayerId,
        Version = layerData.Version,
        Bounds = body.Bounds,
        DeltaType = "snapshot"
    });

    return (StatusCodes.OK, new LayerCommitResponse
    {
        NewVersion = layerData.Version,
        NewEtag = layerData.Version.ToString()
    });
}
```

### Event Publishing (lib-messaging)

```csharp
// Typed event publishing (Tenet 5 - no anonymous objects)
await _messageBus.PublishAsync("map.placed", new MapPlacedEvent
{
    EventId = Guid.NewGuid(),
    Timestamp = DateTimeOffset.UtcNow,
    MapId = childMapId,
    ParentMapId = parentMapId,
    Placement = placement
});
```

---

## Layer Formats

### Texture Layers

2D arrays with channel descriptor (e.g., `[R, G, B, A]` each 0-255).

- **Use Cases**: Heightmaps, terrain types, ownership colors, density maps
- **Encoding**: Raw binary or PNG for import/export
- **Chunked Operations**: Support bounded reads/writes for large layers

```yaml
# Example terrain layer
layerId: "terrain-height"
format: texture
channelDescriptor: ["height_high", "height_low", "terrain_type", "moisture"]
persistence: durable
authority: "region-ai"
```

### Sparse Dictionary Layers

Key/value storage for sparse spatial data.

- **Use Cases**: Star maps (only populated systems), trade routes, waypoints
- **Key Format**: `{x}:{y}` or domain-specific keys
- **Values**: Typed per layer schema

```yaml
# Example trade route layer
layerId: "trade-routes"
format: sparse_dict
persistence: durable
authority: "economy-service"
```

### Tree Layers

Hierarchical spatial structures (quadtrees, octrees).

- **Use Cases**: Ownership hierarchies, LOD management, density queries
- **Serialization**: Node-based with versioning
- **Queries**: Return subtree summaries

### Metadata Object Layers

Collection of positioned game objects.

- **Use Cases**: Resources (trees, rocks), buildings, NPCs, quest markers
- **Validation**: Objects validated against registered type schemas
- **Indexing**: Spatial index for bounded queries

---

## Concurrency Model

### Checkout/Commit Flow

```
┌─────────────┐     ┌─────────────┐     ┌─────────────┐     ┌─────────────┐
│  checkout   │────>│  edit data  │────>│   commit    │────>│   release   │
│  (get lock) │     │  (locally)  │     │ (with ETag) │     │   (auto)    │
└─────────────┘     └─────────────┘     └─────────────┘     └─────────────┘
       │                                       │
       │                                       ▼
       │                              ┌─────────────────┐
       │                              │ Conflict (409)  │
       │                              │ if ETag stale   │
       │                              └─────────────────┘
       ▼
┌─────────────────┐
│ Conflict (409)  │
│ if already      │
│ locked          │
└─────────────────┘
```

### Multi-Instance Safety (Tenet 9)

- **No in-memory authoritative state** - all state in lib-state stores
- **Distributed locks** via `IDistributedLockProvider` for checkouts
- **ETag concurrency** for optimistic writes
- **ConcurrentDictionary** only for local caches (with TTL invalidation)

---

## Configuration

```yaml
# map-configuration.yaml
x-service-configuration:
  properties:
    MaxCheckoutDurationSeconds:
      type: integer
      default: 3600
      env: MAP_MAX_CHECKOUT_DURATION_SECONDS

    DefaultAggregationSeconds:
      type: integer
      default: 5
      env: MAP_DEFAULT_AGGREGATION_SECONDS

    MaxLayerDimensionPixels:
      type: integer
      default: 100000
      env: MAP_MAX_LAYER_DIMENSION_PIXELS
      description: Maximum width/height for texture layers

    MaxChunkSizeBytes:
      type: integer
      default: 1048576
      env: MAP_MAX_CHUNK_SIZE_BYTES
      description: Maximum payload size for layer operations (1MB)

    LayerCacheTtlSeconds:
      type: integer
      default: 300
      env: MAP_LAYER_CACHE_TTL_SECONDS

    MaxMetadataObjectsPerQuery:
      type: integer
      default: 500
      env: MAP_MAX_METADATA_OBJECTS_PER_QUERY
```

---

## Integration Points

| Service | Integration | Direction |
|---------|-------------|-----------|
| **Location** | Metadata objects link via `locationId`/`locationType` | Map → Location |
| **Combat** | Reads terrain/hazard layers, writes ephemeral overlays | Bidirectional |
| **World/Time** | Broadcasts time/weather events | Time → Map (subscriber) |
| **NPC/Agent** | Queries for pathfinding, subscribes to ownership changes | NPC → Map |
| **Region AI** | Authority over structural layers | Region → Map |
| **Real Estate** | Ownership changes via Region AI | Estate → Region → Map |

---

## Implementation Roadmap

### Phase 1: Core Map CRUD (Week 1-2)

- Schema definitions in `schemas/map-api.yaml`
- MapDefinition and LayerDefinition models
- Create/Get/List/Delete map endpoints
- Basic layer read (without checkout)
- Unit tests for map operations

### Phase 2: Checkout/Commit (Week 3-4)

- Distributed locking integration
- ETag concurrency implementation
- Checkout/Commit/Release endpoints
- Layer write with bounds support
- Concurrency conflict handling tests

### Phase 3: Metadata Objects (Week 5-6)

- ObjectTypeSchema registry
- Metadata object CRUD
- Spatial indexing for bounded queries
- JSON Schema validation integration
- Metadata event publishing

### Phase 4: Hierarchy & Events (Week 7-8)

- Parent/child placement endpoints
- Hierarchy traversal queries
- Full event publishing (lifecycle + custom)
- Subscription management
- Delta broadcasting with aggregation
- Integration tests

---

*This document is the authoritative source for Map Service implementation. Updates require review and approval.*
