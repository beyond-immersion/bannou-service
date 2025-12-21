#
# Map Service Architecture - Spatial Data Management for Arcadia (Updated to Tenets)

> **Status**: Draft refresh aligned to Bannou TENETS  
> **Last Updated**: 2025-12-20  
> **Goal**: Provide a schema-first, Dapr-first plugin blueprint that exposes map data (heightmaps, tilemaps, overlays, metadata objects) to clients and services with strong permissions, events, and multi-instance safety.

## Tenet Alignment Snapshot
- **Schema-first (Tenet 1)**: All APIs, events, and configuration live in OpenAPI (`schemas/map-api.yaml`, `schemas/map-events.yaml`, `x-service-configuration`). Generated files untouched. POST-only for WebSocket paths; GET only for browser endpoints if explicitly needed.
- **Dapr-first (Tenet 2)**: State via Dapr state stores (`map-statestore`, `mysql-map-statestore`), pub/sub via `bannou-pubsub`. No direct RabbitMQ queues (prior per-map queues are replaced by Dapr topics + aggregation). Generated service clients for cross-service calls.
- **Event-driven (Tenet 3)**: Map/layer lifecycle events emitted (`map.created`, `map.placed`, `map.layer.updated`, `map.metadata.updated`, etc.) with optional `x-lifecycle` for map entities. Child/parent propagation uses events, not ad-hoc calls.
- **Multi-instance safety (Tenet 4)**: No required in-memory state; use `IDistributedLockProvider` for checkouts; caches are `ConcurrentDictionary` only. ETag concurrency on commits.
- **Permissions (Tenet 10)**: `x-permissions` on every endpoint with roles/states (e.g., `admin`, `developer`, `user`, region ownership states). No anonymous WebSocket access unless explicitly documented.
- **Naming (Tenet 15)**: Status codes use `BeyondImmersion.BannouService.StatusCodes`; models/events follow `{Action}Request`, `{Entity}Response`, `{Entity}{Action}Event`; topics `{entity}.{action}`.

## Core Concepts

- **Maps as abstract spatial assets**: Represents terrain, tilemaps, overlays (radiation, mana, vegetation), relational graphs, or logical maps (relationship graphs). Maps may be unplaced or placed in a hierarchy.
- **Layers**: Each map can hold multiple typed layers with independent persistence and update cadence. Layer metadata defines format (`texture`, `sparse_dict`, `tree`, `metadata_objects`), dimensions, channel layout, persistence (`durable`, `cache`), TTLs, and authority model.
- **Metadata objects**: Arbitrary objects attached to cells/regions (rocks, trees with harvest/destruction state, buildings, chests, POIs). Use the polymorphic ID+Type pattern (Tenet 13) plus optional location linkage.
- **Hierarchy**: Parent/child links with placement metadata. Child maps know parent placement; parents maintain child placement records. Placement events emitted when linking/unlinking.
- **Authority**: Region AI (or equivalent controller) owns structural/ownership layers; other services request changes through APIs with role/state checks. Layer-level authority settings drive checkout rules.

## Data Model Sketch (for schema-first)
- `MapDefinition` (entity) with `mapId`, `mapType`, `dimensions`, `parentMapId` (optional), `parentPlacement`, `layers`.
- `LayerDefinition` with `layerId`, `format` (`texture`, `sparse_dict`, `tree`, `metadata_objects`), `persistence` (`durable|cache`), `ttlSeconds`, `authority`, `encoding` (for textures: channels RGBA meaning, tile size).
- `MetadataObject` with `objectId`, `objectType`, `location` (cell or bounds), `state` blob, `links` (to location service IDs via ID+Type, optional realm/location IDs).
- State stores: `map-statestore` for mutable layer data/metadata, `mysql-map-statestore` for durable/queryable data (per deployment).
- Keys: `map-{id}`, `map:{id}:layer:{layerId}`, `map:{id}:metadata:{objectId}`, indexes like `map:{id}:metadata-index:{cellKey}` (Tenet 15 naming).

## API Design (conceptual; must be codified in schemas)
- **All WebSocket/Connect-facing endpoints are POST** with request bodies; GET allowed only for browser/website delivery (per Tenet 14) and kept out of WebSocket exposure.
- Example POST endpoints (request/response models follow Tenet naming):
  - `/maps/create` → `CreateMapRequest` → `MapResponse`
  - `/maps/get` → `GetMapRequest` (mapId) → `MapResponse`
  - `/maps/list` with filters (realm, type, parent)
  - `/maps/place-child` (mapId, parentMapId, placement) → emits `map.placed`
  - `/maps/remove-child`
  - `/maps/layers/read` (mapId, layerId, bounds?, format `raw|image|json`, version?) → `LayerReadResponse`
  - `/maps/layers/write` (mapId, layerId, payload) with checkout token
  - `/maps/layers/checkout` / `/maps/layers/commit` (per-layer scope; includes ETag/version)
  - `/maps/metadata/add|update|remove|list` (uses ID+Type, location references)
  - `/maps/query` (point/region/path; layers to sample; optional aggregation)
  - `/maps/subscribe` (event types, frequency, bounds filter) → Connect-managed event routing
- **Permissions**: `x-permissions` aligned to ownership states. Examples:
  - Ownership writes require `role: developer|admin` plus state `region:owns_area`.
  - Cache-layer writes (e.g., combat hazards) allow `role: developer` with `game-session:in_game`.
  - Reads can be `role: user` with optional state gating (e.g., must be in region).
- **Return pattern**: `(StatusCodes, Response?)` everywhere; null payload for errors. Controllers generated; manual service adheres to Tenet 6.

## Schema Draft (conceptual, for `/schemas/map-api.yaml` and `/schemas/map-events.yaml`)
```yaml
openapi: 3.0.1
info:
  title: Map Service API
  version: 0.1.0
paths:
  /maps/create:
    post:
      x-permissions: [{ role: developer, states: {} }]
      requestBody:
        required: true
        content:
          application/json:
            schema: { $ref: '#/components/schemas/CreateMapRequest' }
      responses:
        '200': { content: { application/json: { schema: { $ref: '#/components/schemas/MapResponse' }}}}

  /maps/get:
    post:
      x-permissions: [{ role: user, states: {} }]
      requestBody: { required: true, content: { application/json: { schema: { $ref: '#/components/schemas/GetMapRequest' }}}}
      responses:
        '200': { content: { application/json: { schema: { $ref: '#/components/schemas/MapResponse' }}}}

  /maps/place-child:
    post:
      x-permissions: [{ role: developer, states: { region: owns_area } }]
      requestBody: { required: true, content: { application/json: { schema: { $ref: '#/components/schemas/PlaceChildMapRequest' }}}}
      responses:
        '200': { content: { application/json: { schema: { $ref: '#/components/schemas/MapResponse' }}}}

  /maps/layers/checkout:
    post:
      x-permissions: [{ role: developer, states: { region: owns_area } }]
      requestBody: { required: true, content: { application/json: { schema: { $ref: '#/components/schemas/LayerCheckoutRequest' }}}}
      responses:
        '200': { content: { application/json: { schema: { $ref: '#/components/schemas/LayerCheckoutResponse' }}}}

  /maps/layers/commit:
    post:
      x-permissions: [{ role: developer, states: { region: owns_area } }]
      requestBody: { required: true, content: { application/json: { schema: { $ref: '#/components/schemas/LayerCommitRequest' }}}}
      responses:
        '200': { content: { application/json: { schema: { $ref: '#/components/schemas/LayerCommitResponse' }}}}

  /maps/layers/read:
    post:
      x-permissions: [{ role: user, states: {} }]
      requestBody: { required: true, content: { application/json: { schema: { $ref: '#/components/schemas/LayerReadRequest' }}}}
      responses:
        '200': { content: { application/json: { schema: { $ref: '#/components/schemas/LayerReadResponse' }}}}

  /maps/metadata/add:
    post:
      x-permissions: [{ role: developer, states: {} }]
      requestBody: { required: true, content: { application/json: { schema: { $ref: '#/components/schemas/UpsertMetadataObjectRequest' }}}}
      responses:
        '200': { content: { application/json: { schema: { $ref: '#/components/schemas/MetadataObjectResponse' }}}}

  /maps/metadata/remove:
    post:
      x-permissions: [{ role: developer, states: {} }]
      requestBody: { required: true, content: { application/json: { schema: { $ref: '#/components/schemas/RemoveMetadataObjectRequest' }}}}
      responses:
        '200': { content: { application/json: { schema: { $ref: '#/components/schemas/MetadataObjectResponse' }}}}

  /maps/query:
    post:
      x-permissions: [{ role: user, states: {} }]
      requestBody: { required: true, content: { application/json: { schema: { $ref: '#/components/schemas/MapQueryRequest' }}}}
      responses:
        '200': { content: { application/json: { schema: { $ref: '#/components/schemas/MapQueryResponse' }}}}

  /maps/subscribe:
    post:
      x-permissions: [{ role: user, states: {} }]
      requestBody: { required: true, content: { application/json: { schema: { $ref: '#/components/schemas/SubscribeRequest' }}}}
      responses:
        '200': { content: { application/json: { schema: { $ref: '#/components/schemas/SubscribeResponse' }}}}

  # Schema registry for metadata object types
  /maps/object-types/register:
    post:
      x-permissions: [{ role: developer, states: {} }]
      requestBody: { required: true, content: { application/json: { schema: { $ref: '#/components/schemas/RegisterObjectTypeRequest' }}}}
      responses:
        '200': { content: { application/json: { schema: { $ref: '#/components/schemas/ObjectTypeResponse' }}}}

  /maps/object-types/assign-to-map:
    post:
      x-permissions: [{ role: developer, states: {} }]
      requestBody: { required: true, content: { application/json: { schema: { $ref: '#/components/schemas/AssignObjectTypeRequest' }}}}
      responses:
        '200': { content: { application/json: { schema: { $ref: '#/components/schemas/MapResponse' }}}}

components:
  schemas:
    CreateMapRequest:
      type: object
      required: [mapId, mapType, dimensions, layers]
      properties:
        mapId: { type: string, format: uuid }
        mapType: { type: string, enum: [texture, sparse_dict, tree, logical] }
        dimensions:
          type: array
          minItems: 2
          maxItems: 3
          items: { type: integer, minimum: 1 }
        layers:
          type: array
          items: { $ref: '#/components/schemas/LayerDefinition' }
        parentMapId: { type: string, format: uuid, nullable: true }
        parentPlacement: { $ref: '#/components/schemas/Placement', nullable: true }

    LayerDefinition:
      type: object
      required: [layerId, format, persistence]
      properties:
        layerId: { type: string }
        format: { type: string, enum: [texture, sparse_dict, tree, metadata_objects] }
        channelDescriptor: { type: array, items: { type: string }, nullable: true } # for texture
        persistence: { type: string, enum: [durable, cache] }
        ttlSeconds: { type: integer, format: int32, nullable: true }
        authority: { type: string }
        encoding: { type: string, nullable: true }

    UpsertMetadataObjectRequest:
      type: object
      required: [mapId, layerId, objectType, state]
      properties:
        mapId: { type: string, format: uuid }
        layerId: { type: string }
        objectId: { type: string, format: uuid, nullable: true } # optional; auto-generate if null
        objectType: { type: string } # validated against registered object type schema
        state: { type: object, additionalProperties: true }
        locationRef:
          type: object
          nullable: true
          properties:
            locationId: { type: string, format: uuid }
            locationType: { type: string }
        bounds: { $ref: '#/components/schemas/Bounds', nullable: true }

    RegisterObjectTypeRequest:
      type: object
      required: [objectType, schema]
      properties:
        objectType: { type: string }
        schema: { type: object, additionalProperties: true } # validation spec for state payload

    AssignObjectTypeRequest:
      type: object
      required: [mapId, objectType]
      properties:
        mapId: { type: string, format: uuid }
        objectType: { type: string }

    # ...additional request/response models follow the same pattern
```

Events (`map-events.yaml`):
```yaml
MapCreatedEvent:
  type: object
  required: [eventId, timestamp, mapId, mapType]
  properties:
    eventId: { type: string, format: uuid }
    timestamp: { type: string, format: date-time }
    mapId: { type: string, format: uuid }
    mapType: { type: string }

MapPlacedEvent:
  type: object
  required: [eventId, timestamp, mapId, parentMapId, placement]
  properties:
    eventId: { type: string, format: uuid }
    timestamp: { type: string, format: date-time }
    mapId: { type: string, format: uuid }
    parentMapId: { type: string, format: uuid }
    placement: { $ref: '#/components/schemas/Placement' }

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
    payloadRef: { type: string, nullable: true } # e.g., blob reference if large

MapDeltaBroadcastEvent:  # non-critical delta broadcasts from any service
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
    aggregationWindowSeconds: { type: integer, format: int32, default: 5 } # default cadence
    mayDrop: { type: boolean, default: true } # explicitly non-critical; may be ignored if overloaded

MapMetadataUpdatedEvent:
  type: object
  required: [eventId, timestamp, mapId, layerId, objectId, objectType]
  properties:
    eventId: { type: string, format: uuid }
    timestamp: { type: string, format: date-time }
    mapId: { type: string, format: uuid }
    layerId: { type: string }
    objectId: { type: string, format: uuid }
    objectType: { type: string }
    state: { type: object, additionalProperties: true }
```

## Event Model (Dapr pub/sub)
- Topics (`bannou-pubsub`):
  - `map.created`, `map.updated`, `map.deleted`
  - `map.placed`, `map.unplaced`, `map.child-linked`, `map.child-unlinked`
  - `map.layer.created|updated|deleted`
  - `map.metadata.created|updated|deleted`
  - Aggregated child summaries: `map.child-summary` (full-state style; includes version)
- Use `x-lifecycle` where applicable to auto-generate created/updated/deleted events for `MapDefinition` and `LayerDefinition`.
- Fixed-frequency emitters implemented via service timers + Dapr pub/sub, not per-map RabbitMQ queues. If per-map queueing is needed, propose Dapr topic partitioning (e.g., `map.layer.updated.{mapId}`) with rate limiting in service code. Cadence should default to “wide” (seconds+ aggregation—**default 5s**) leaning to eventual consistency; high-frequency writes are fine because reads catch up via aggregation and are allowed to drop/reject individual events.
- Add a **common, non-critical delta event** pattern (akin to service API mapping broadcasts) that any service can emit: “here are my map deltas.” Map service aggregates/merges and may silently drop if overloaded; critical updates must go through APIs with checkout/commit.
- Upward/downward propagation uses events; consumers decide whether to aggregate or ignore. Avoid direct service-to-service HTTP (Tenet 2).

## Concurrency and Checkout
- **Distributed locks**: `IDistributedLockProvider` keyed by `map:{id}:layer:{layerId}` for checkout; optional region lock for multi-layer batch edits.
- **ETag/versioned writes**: `GetStateEntryAsync` / `TrySaveStateAsync` per layer chunk or metadata blob. Reject stale commits with `StatusCodes.Conflict`.
- **Scoping**: Checkout scopes can be full-layer or bounds. Responses include token + ETag + expiry seconds.
- **Authority model**: Layers declare authority; Region AI exclusivity enforced by permissions + server-side checks (no implicit permanent checkout in-memory).

## Layer Formats and Serialization
- **Texture layers**: 2D arrays with channel descriptor (`channels: [R,G,B,A]` each 0-255). Support binary payloads and optional PNG encoding for import/export (documented in schema `format` enum). Chunked reads/writes by bounds.
- **Sparse dictionary layers**: Key/value for sparse spaces (e.g., star maps, trade routes). Keys follow `{x}:{y}` or domain-specific keys; values typed per layer schema.
- **Tree layers**: Quad/Oct/Custom trees for hierarchical ownership or density. Store serialized nodes with versioning; queries expose summaries.
- **Metadata objects**: Attach to cells/regions; store object state JSON with `objectType` enum; link to Location service via `locationId`/`locationType` (ID+Type).
- **Metadata object types/schemas**: Object type is not a closed enum; define a schema registry API to create/update object-type schemas (validation rules, required fields). Other plugins (e.g., vegetation) register their object schemas and assign them to maps for validation (both API and event ingestion). Object IDs may be provided or auto-generated GUIDs. Location references are optional and should not gate non-geographic map types.

## Integration Points
- **Location plugin**: Map placements reference `locationId`/`locationType`; events can emit `location.map.linked` if needed. Spatial queries can return nearest location references.
- **Combat service**: Reads terrain/mana/weather/hazard layers; writes ephemeral hazard overlays with TTL. Subscribes to bounded updates (`map.layer.updated.{mapId}` with filter).
- **World/Time services**: Broadcast time/weather/season events; Map service applies to relevant layers via subscriptions.
- **NPC/Agent services**: Queries for pathfinding/resource availability; subscribes to ownership/resource change events in defined bounds.
- **Real estate/Guardian**: Ownership changes mediated through Region AI endpoints; emits ownership change events for permissions updates.

## Performance and Scaling
- Shard maps by realm/region; each Map service instance can own a shard. Service-to-service routing via generated clients + `ServiceAppMappingResolver`.
- Read-heavy layers cached (`ConcurrentDictionary`) with TTL; authoritative state in Dapr stores. Cache invalidated on layer update events.
- Write batching and consolidation occur inside service before publishing events; output rate limits implemented in code (no direct queue tuning).
- Large layers support chunked operations (bounds) to avoid oversized payloads.

## Deployment and Configuration
- Service configuration via generated `MapServiceConfiguration` (schema `x-service-configuration`): state store names, max checkout duration, chunk sizes, cache TTLs, event emission frequencies, rate limits.
- Environment toggles follow existing patterns: `MAP_SERVICE_ENABLED=true` plus per-environment overrides.
- Browser endpoints (if needed for visualization/debug) must be isolated under `/website/maps/*` with GET + path params and **not** exposed to WebSocket clients (Tenet 14). Preferred model: collaboration with Website plugin—Map plugin provides visualization-ready payloads on request; Website handles public delivery, caching (Redis/proxy), and permissions.

## Known Deviations vs Old Draft
- Replaced direct RabbitMQ per-map queues with Dapr pub/sub + service-side rate limiting to satisfy Tenet 2. If RabbitMQ-specific behavior is required, it must be justified as a Connect-style exception and documented explicitly.
- All API shapes now POST-first and schema-driven; previous GET/path-param examples removed except for optional browser-only endpoints.
- Authority/checkout no longer implies permanent implicit locks; now explicit with distributed locks/ETags to maintain multi-instance safety.

## Open Decisions to Confirm
- Partitioning strategy for high-volume layer events: topic naming (`map.layer.updated.{mapId}`) vs aggregation cadence defaults. **Decision: go with wide aggregation cadence; non-critical events may be dropped—critical paths must use APIs.**
- Default layer formats per common use cases (terrain=texture, ownership=texture/dictionary, radiation=mixed?). **Decision: no defaults; callers must specify.**
- Maximum layer dimensions/chunk size defaults for Connect payload limits. **Decision: set a sanity max (e.g., ≤ Japan-scale at 1m/px) but tolerate larger if Connect gains streaming/multi-message support.**
- Metadata object schema: required fields and allowed `objectType` enum set; how tightly to couple to Location service IDs. **Decision: object types come from a schema registry API (not enum); IDs optional/autogenerated; Location references optional; validation per assigned schema.**
- Whether any browser-facing visualization endpoints are required now vs later, and their caching strategy. **Decision: collaborate with Website plugin; Map provides visualization payloads on request, Website handles public delivery/caching (Redis/proxy).**

---
*This document is part of the Arcadia knowledge base and should remain consistent with Bannou TENETS. Update the Last Updated date when finalized.*
