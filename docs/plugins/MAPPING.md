# Mapping Plugin Deep Dive

> **Plugin**: lib-mapping
> **Schema**: schemas/mapping-api.yaml
> **Version**: 1.0.0
> **State Stores**: mapping-statestore (Redis)

---

## Overview

Spatial data management service (L4 GameFeatures) for Arcadia game worlds. Provides authority-based channel ownership for exclusive write access to spatial regions, high-throughput ingest via dynamic RabbitMQ subscriptions, 3D spatial indexing with affordance queries, and design-time authoring workflows (checkout/commit/release). Purely a spatial data store -- does not perform rendering or physics. Game servers and NPC brains publish spatial data to and query from it.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Redis persistence for channels, authority records, map objects, spatial/type/region indexes, definitions, checkouts, affordance cache, version counters |
| lib-messaging (`IMessageBus`) | Publishing channel lifecycle, map update, objects changed, authority, and warning events |
| lib-messaging (`IMessageSubscriber`) | Dynamic per-channel subscriptions to `map.ingest.{channelId}` topics |
| lib-asset (`IAssetClient` via mesh) | Large payload storage when snapshot/publish data exceeds `InlinePayloadMaxBytes` |
| `IHttpClientFactory` | Uploading binary data to presigned URLs returned by lib-asset |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| (none currently) | No other plugins reference `IMappingClient` or subscribe to mapping events within the plugin layer. Game servers and NPC actors consume events externally. |

---

## State Storage

**Stores**: 1 state store (mapping-statestore, Redis, prefix: `mapping`)

| Key Pattern | Data Type | Purpose | TTL |
|-------------|-----------|---------|-----|
| `map:channel:{channelId}` | `ChannelRecord` | Channel configuration (region, kind, non-authority handling, takeover mode, alert config) | None |
| `map:authority:{channelId}` | `AuthorityRecord` | Current authority grant (token, app-id, expiry, RequiresConsumeBeforePublish flag) | None |
| `map:object:{regionId}:{objectId}` | `MapObject` | Individual map objects with position/bounds/data/version | Per-kind |
| `map:index:{regionId}:{kind}:{cellX}_{cellY}_{cellZ}` | `List<Guid>` | Spatial cell index - object IDs within a 3D grid cell | None |
| `map:type-index:{regionId}:{objectType}` | `List<Guid>` | Type index - object IDs grouped by publisher-defined type | None |
| `map:region-index:{regionId}:{kind}` | `List<Guid>` | Region index - all object IDs in a region+kind (for full-region queries) | None |
| `map:checkout:{regionId}:{kind}` | `CheckoutRecord` | Authoring checkout lock (editor ID, authority token, expiry) | None |
| `map:version:{channelId}` | `LongWrapper` | Monotonic version counter per channel | None |
| `map:affordance-cache:{regionId}:{type}:{boundsHash}` | `CachedAffordanceResult` | Cached affordance query results with timestamp | None (app-level) |
| `map:definition:{definitionId}` | `DefinitionRecord` | Map definition templates (name, layers, default bounds, metadata) | None |
| `map:definition-index` | `DefinitionIndexEntry` | Index of all definition IDs for listing | None |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `mapping.channel.created` | `MappingChannelCreatedEvent` | Channel created via CreateChannel |
| `mapping.authority.granted` | `MappingAuthorityGrantedEvent` | Authority granted on CreateChannel |
| `mapping.authority.released` | `MappingAuthorityReleasedEvent` | Authority explicitly released |
| `mapping.authority.expired` | `MappingAuthorityExpiredEvent` | Authority expiry detected during validation (fire-and-forget) |
| `map.{regionId}.{kind}.updated` | `MapUpdatedEvent` | Map data published (RPC publish or ingest) |
| `map.{regionId}.{kind}.objects.changed` | `MapObjectsChangedEvent` | Objects created/updated/deleted (subject to event aggregation window) |
| `map.warnings.unauthorized_publish` | `MapUnauthorizedPublishWarning` | Non-authority publish attempt in reject_and_alert or accept_and_alert mode |

### Consumed Events (Dynamic)

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `map.ingest.{channelId}` | `MapIngestEvent` | Dynamic subscription per-channel; created when authority granted, disposed when released |

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `AuthorityTimeoutSeconds` | `MAPPING_AUTHORITY_TIMEOUT_SECONDS` | `60` | Time before authority expires without heartbeat |
| `AuthorityGracePeriodSeconds` | `MAPPING_AUTHORITY_GRACE_PERIOD_SECONDS` | `30` | Grace period warning threshold for heartbeat response |
| `DefaultSpatialCellSize` | `MAPPING_SPATIAL_CELL_SIZE` | `64.0` | Spatial index cell size in world units |
| `MaxObjectsPerQuery` | `MAPPING_MAX_OBJECTS_PER_QUERY` | `5000` | Maximum objects in a single query response |
| `MaxPayloadsPerPublish` | `MAPPING_MAX_PAYLOADS_PER_PUBLISH` | `100` | Maximum payloads in a single publish/ingest event |
| `AffordanceCacheTimeoutSeconds` | `MAPPING_AFFORDANCE_CACHE_TIMEOUT_SECONDS` | `60` | TTL for cached affordance results |
| `MaxAffordanceCandidates` | `MAPPING_MAX_AFFORDANCE_CANDIDATES` | `1000` | Maximum candidates evaluated in affordance queries |
| `AffordanceExclusionToleranceUnits` | `MAPPING_AFFORDANCE_EXCLUSION_TOLERANCE_UNITS` | `1.0` | Distance tolerance in world units for position exclusion matching |
| `InlinePayloadMaxBytes` | `MAPPING_INLINE_PAYLOAD_MAX_BYTES` | `65536` | Threshold for offloading to lib-asset (64KB) |
| `MaxCheckoutDurationSeconds` | `MAPPING_MAX_CHECKOUT_DURATION_SECONDS` | `1800` | Maximum authoring lock duration (30 min) |
| `DefaultLayerCacheTtlSeconds` | `MAPPING_DEFAULT_LAYER_CACHE_TTL_SECONDS` | `3600` | Default TTL for ephemeral layer data (1 hour) |
| `EventAggregationWindowMs` | `MAPPING_EVENT_AGGREGATION_WINDOW_MS` | `100` | Buffering window for MapObjectsChangedEvent (0 = disabled) |
| `MaxBufferFlushRetries` | `MAPPING_MAX_BUFFER_FLUSH_RETRIES` | `3` | Maximum retry attempts for flushing spatial change buffers before discarding |
| `TtlTerrain` | `MAPPING_TTL_TERRAIN` | `-1` | Terrain TTL (-1 = durable, no expiry) |
| `TtlStaticGeometry` | `MAPPING_TTL_STATIC_GEOMETRY` | `-1` | Static geometry TTL (durable) |
| `TtlNavigation` | `MAPPING_TTL_NAVIGATION` | `-1` | Navigation TTL (durable) |
| `TtlResources` | `MAPPING_TTL_RESOURCES` | `3600` | Resources TTL (1 hour) |
| `TtlSpawnPoints` | `MAPPING_TTL_SPAWN_POINTS` | `3600` | Spawn points TTL (1 hour) |
| `TtlPointsOfInterest` | `MAPPING_TTL_POINTS_OF_INTEREST` | `3600` | Points of interest TTL (1 hour) |
| `TtlDynamicObjects` | `MAPPING_TTL_DYNAMIC_OBJECTS` | `3600` | Dynamic objects TTL (1 hour) |
| `TtlHazards` | `MAPPING_TTL_HAZARDS` | `300` | Hazards TTL (5 min, short-lived) |
| `TtlWeatherEffects` | `MAPPING_TTL_WEATHER_EFFECTS` | `600` | Weather effects TTL (10 min) |
| `TtlOwnership` | `MAPPING_TTL_OWNERSHIP` | `-1` | Ownership TTL (durable) |
| `TtlCombatEffects` | `MAPPING_TTL_COMBAT_EFFECTS` | `30` | Combat effects TTL (30s, very ephemeral) |
| `TtlVisualEffects` | `MAPPING_TTL_VISUAL_EFFECTS` | `60` | Visual effects TTL (60s, ephemeral) |
| `MaxSpatialQueryResults` | `MAPPING_MAX_SPATIAL_QUERY_RESULTS` | `5` | Maximum distinct object types in warning summaries |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<MappingService>` | Scoped | Structured logging |
| `MappingServiceConfiguration` | Singleton | All 26 config properties |
| `IStateStoreFactory` | Singleton | Redis state store access |
| `IMessageBus` | Scoped | Event publishing |
| `IMessageSubscriber` | Scoped | Dynamic per-channel ingest subscriptions |
| `IAssetClient` | Scoped | Large payload upload to asset storage |
| `IHttpClientFactory` | Singleton | HTTP client for presigned URL uploads |
| `IAffordanceScorer` | Scoped | Affordance scoring logic (extracted helper) |
| `IEventConsumer` | Scoped | Event consumer registration (unused -- dynamic model) |

Service lifetime is **Scoped** (per-request). Static `ConcurrentDictionary` fields track cross-request state for ingest subscriptions and event aggregation buffers.

---

## API Endpoints (Implementation Notes)

### Authority Management (3 endpoints)

- **CreateChannel** (`/mapping/create-channel`): Generates deterministic channel ID via SHA-256(regionId:kind). Checks for existing channel -- if authority is still active, returns 409 Conflict. If authority has expired, applies takeover mode: `reset` clears all channel data, `require_consume` sets a flag blocking publishes until RequestSnapshot is called, `preserve_and_diff` (default) preserves existing data. Creates ChannelRecord, AuthorityRecord, and version counter. Processes optional initial snapshot payloads. Subscribes to dynamic ingest topic `map.ingest.{channelId}`. Publishes `mapping.channel.created` and `mapping.authority.granted` events. Returns AuthorityGrant with token, ingest topic, and expiry.

- **ReleaseAuthority** (`/mapping/release-authority`): Parses and validates authority token against stored AuthorityRecord. Deletes authority record. Disposes dynamic ingest subscription from `IngestSubscriptions` ConcurrentDictionary. Publishes `mapping.authority.released` event with channel details.

- **AuthorityHeartbeat** (`/mapping/authority-heartbeat`): Validates token and checks expiry. If authority has already expired, returns 401. Extends expiry by `AuthorityTimeoutSeconds` from now. Returns warning string if remaining time is below `AuthorityGracePeriodSeconds` to signal the client should increase heartbeat frequency.

### Runtime Publishing (3 endpoints)

- **PublishMapUpdate** (`/mapping/publish`): Checks payload size against `InlinePayloadMaxBytes` (returns 400 if exceeded). Validates authority token. On invalid authority, delegates to NonAuthorityHandling (reject_silent returns 401 silently, reject_and_alert publishes warning then 401, accept_and_alert publishes warning but processes the payload). Processes single payload via ProcessPayloadsAsync -- creates MapObject, updates spatial/type indexes with per-kind TTL. Publishes `map.{regionId}.{kind}.updated` event.

- **PublishObjectChanges** (`/mapping/publish-objects`): Validates authority. Processes batch of ObjectChange items (created/updated/deleted). Created: validates objectType, saves object, adds to region/spatial/type indexes. Updated: loads existing, cleans old indexes, updates fields (upserts if not found). Deleted: removes from all indexes, deletes object. Reports accepted/rejected counts. Increments channel version. Publishes `map.{regionId}.{kind}.objects.changed` via event aggregation buffer.

- **RequestSnapshot** (`/mapping/request-snapshot`): Gathers objects across specified kinds (or all kinds). Uses region index for unbounded queries or spatial index for bounded queries. Computes max version across all relevant channels. If authority token provided, clears `RequiresConsumeBeforePublish` flag (for `require_consume` takeover mode). If response exceeds `InlinePayloadMaxBytes`, uploads to lib-asset via presigned URL and returns `payloadRef` instead. Falls back to inline response if asset upload fails.

### Spatial Queries (4 endpoints)

- **QueryPoint** (`/mapping/query/point`): Creates bounding box from position and radius (defaults to `DefaultSpatialCellSize`). Queries spatial index cells within bounds. Post-filters by Euclidean distance for point objects, BoundsContainsPoint or BoundsIntersectsRadius for area objects. Returns matching objects across specified kinds.

- **QueryBounds** (`/mapping/query/bounds`): Iterates cells within the requested bounds. Loads objects from spatial indexes, deduplicates via HashSet. Verifies each object is actually within bounds (BoundsContainsPoint or BoundsIntersect). Enforces `maxObjects` limit with truncation flag.

- **QueryObjectsByType** (`/mapping/query/objects-by-type`): Uses type index (`map:type-index:{regionId}:{objectType}`) to look up object IDs. Loads each object individually. Applies optional bounds filter. Enforces `maxObjects` limit with truncation flag.

- **QueryAffordance** (`/mapping/query/affordance`): Checks affordance cache first (unless `Freshness=Fresh`). Determines relevant map kinds via `IAffordanceScorer.GetKindsForAffordanceType()` (e.g., Ambush searches static_geometry, dynamic_objects, navigation). Gathers candidates up to `MaxAffordanceCandidates`. Filters excluded positions (within 1.0 unit tolerance). Scores each candidate via `IAffordanceScorer.ScoreAffordance()` considering object data properties (cover_rating, elevation, sightlines), actor capabilities (size modifier, stealth rating), and custom affordance definitions (requires/prefers/excludes). Filters by `minScore`, sorts descending, takes `maxResults`. Returns scored locations with extracted features and query metadata (timing, cache hit, objects evaluated). Caches result if freshness allows.

### Authoring Workflow (3 endpoints)

- **CheckoutForAuthoring** (`/mapping/authoring/checkout`): Checks for existing checkout lock on region+kind. If lock exists and not expired, returns 409 Conflict. If expired, allows takeover. Creates CheckoutRecord with `MaxCheckoutDurationSeconds` expiry. Generates authority token for use in commit. Does NOT create a runtime channel -- authoring is design-time only.

- **CommitAuthoring** (`/mapping/authoring/commit`): Validates authority token against stored CheckoutRecord. Increments version counter for the deterministic channel ID. Deletes the checkout lock. Returns new version number.

- **ReleaseAuthoring** (`/mapping/authoring/release`): Validates authority token. Deletes checkout lock without incrementing version. Used to abandon changes.

### Map Definitions (5 endpoints)

- **CreateDefinition** (`/mapping/definition/create`): Scans existing definition index for duplicate names (case-insensitive). Creates DefinitionRecord with layers, default bounds, and metadata. Adds ID to definition index. Returns MapDefinition response.

- **GetDefinition** (`/mapping/definition/get`): Simple key lookup by definition ID. Returns 404 if not found.

- **ListDefinitions** (`/mapping/definition/list`): Loads all definitions from index. Applies optional name filter (case-insensitive Contains). Sorts by name, applies offset/limit pagination. Returns total count.

- **UpdateDefinition** (`/mapping/definition/update`): Loads existing record (404 if not found). Updates only provided fields (name, description, layers, defaultBounds, metadata). Sets UpdatedAt timestamp.

- **DeleteDefinition** (`/mapping/definition/delete`): Loads and deletes definition record. Removes from definition index.

---

## Visual Aid

```
Authority Lifecycle
=====================

  Game Server (Authority)              Mapping Service                   Consumers
       │                                    │                              │
       ├── CreateChannel ──────────────────►│                              │
       │   (regionId, kind, takeover)       │                              │
       │                                    ├── Generate channelId          │
       │                                    │   (SHA-256 of region:kind)    │
       │                                    ├── Store ChannelRecord         │
       │                                    ├── Store AuthorityRecord       │
       │                                    ├── Subscribe(map.ingest.{id})  │
       │◄── AuthorityGrant ────────────────│                              │
       │   (token, ingestTopic, expiry)     ├── Publish authority.granted ──►
       │                                    │                              │
       │                                    │                              │
       │  ┌─── Heartbeat Loop ────┐        │                              │
       │  │ Every ~30s:           │        │                              │
       ├──┤ AuthorityHeartbeat ───┼───────►│                              │
       │  │ (channelId, token)    │        ├── Extend ExpiresAt            │
       │◄─┤ {valid, newExpiry}    │────────│                              │
       │  └───────────────────────┘        │                              │
       │                                    │                              │
       │── Publish to ingest topic ────────►│                              │
       │   (via RabbitMQ, high-throughput)  ├── Validate authority          │
       │                                    ├── Process payloads            │
       │                                    ├── Update spatial indexes      │
       │                                    ├── Publish map.*.updated ──────►
       │                                    ├── Publish map.*.objects ──────►
       │                                    │                              │
       │── ReleaseAuthority ───────────────►│                              │
       │   (channelId, token)               ├── Delete AuthorityRecord     │
       │                                    ├── Dispose subscription        │
       │◄── {released: true} ──────────────│── Publish authority.released ►│
       │                                    │                              │


Non-Authority Handling
========================

  Unauthorized Publisher         Mapping Service           Alert Consumers
       │                             │                         │
       ├── PublishMapUpdate ────────►│                         │
       │   (invalid/no token)        │                         │
       │                             ├── ValidateAuthority()   │
       │                             │   → FAILS               │
       │                             │                         │
       │                      ┌──────┼── Check channel.NonAuthorityHandling
       │                      │      │                         │
       │   reject_silent:     │      │                         │
       │◄─ 401 (no event) ───┤      │                         │
       │                      │      │                         │
       │   reject_and_alert:  │      │                         │
       │◄─ 401 ──────────────┤──────┼── Publish warning ─────►│
       │                      │      │                         │
       │   accept_and_alert:  │      │                         │
       │◄─ 200 (processed) ──┤──────┼── Publish warning ─────►│
       │                      └──────┤── Process payload       │
       │                             │                         │


Spatial Index & Query
=======================

  World Space (DefaultSpatialCellSize = 64 units)
  ┌────────┬────────┬────────┐
  │(0,0,0) │(1,0,0) │(2,0,0) │  Each cell: 64x64x64 units
  │  obj_A │  obj_B │        │
  ├────────┼────────┼────────┤
  │(0,1,0) │(1,1,0) │(2,1,0) │
  │        │obj_C   │  obj_D │
  │        │obj_E   │        │
  └────────┴────────┴────────┘

  QueryPoint(position=(80,70,0), radius=50):
    1. Compute bounds: min=(30,20,-50), max=(130,120,50)
    2. GetCellsForBounds → cells (0,0,0),(1,0,0),(2,0,0),(0,1,0),(1,1,0),(2,1,0)
    3. Load object IDs from each cell's spatial index
    4. Deduplicate (HashSet)
    5. Post-filter by Euclidean distance from center
    → Returns: obj_B, obj_C, obj_E (within radius)

  QueryBounds(bounds):
    1. GetCellsForBounds → overlapping cells
    2. Load objects from each cell
    3. Verify BoundsContainsPoint or BoundsIntersect
    → Returns: objects within bounds, up to maxObjects


Affordance Scoring
====================

  QueryAffordance(type=Ambush, regionId, bounds, minScore=0.3)
       │
       ├── GetKindsForAffordanceType(Ambush)
       │   → [static_geometry, dynamic_objects, navigation]
       │
       ├── Gather candidates from spatial indexes
       │   (up to MaxAffordanceCandidates=1000)
       │
       ├── For each candidate:
       │   ├── Base score: 0.5
       │   ├── cover_rating property? → score += cr * 0.3
       │   ├── sightlines property?   → score += min(sl * 0.05, 0.2)
       │   ├── Actor size modifier:
       │   │   Tiny=1.2, Small=1.1, Medium=1.0, Large=0.9, Huge=0.8
       │   ├── Stealth rating?        → score *= (1.0 + stealth * 0.2)
       │   └── Clamp to [0.0, 1.0]
       │
       ├── Filter: score >= minScore (0.3)
       ├── Sort descending by score
       ├── Take maxResults
       │
       └── Return AffordanceLocation[] with:
           - position/bounds
           - score (0.0-1.0)
           - features (cover_rating, sightlines, concealment)
           - objectIds


Event Aggregation Buffer
==========================

  Rapid Object Changes              EventAggregationBuffer           Message Bus
       │                                    │                           │
  t=0  ├── change_1 ──────────────────────►│ pendingChanges=[1]        │
  t=20 ├── change_2 ──────────────────────►│ pendingChanges=[1,2]      │
  t=50 ├── change_3 ──────────────────────►│ pendingChanges=[1,2,3]    │
       │                                    │                           │
  t=100│               ┌── Timer fires ────┤                           │
       │               │ (windowMs=100)     │                           │
       │               │                    ├── Flush all pending ──────►
       │               │                    │   MapObjectsChangedEvent   │
       │               │                    │   (3 changes, latest ver) │
       │               └────────────────────┤                           │
       │                                    ├── Remove buffer from dict │
       │                                    ├── Dispose timer           │
       │                                    │                           │

  Result: 3 rapid changes → 1 coalesced event (vs 3 separate events)


Authoring Workflow
====================

  Level Designer                  Mapping Service
       │                               │
       ├── Checkout(regionId, kind) ───►│
       │                               ├── Check existing lock
       │                               │   ├── Active? → 409 Conflict
       │                               │   └── Expired or none? → proceed
       │                               ├── Store CheckoutRecord
       │◄── {authorityToken, expiry} ──│   (MaxCheckoutDuration=30min)
       │                               │
       │  (edit map data offline)      │
       │                               │
       ├── Commit(token) ─────────────►│
       │                               ├── Validate token
       │                               ├── Increment version
       │                               ├── Delete checkout lock
       │◄── {version} ────────────────│
       │                               │
       │  -- OR --                     │
       │                               │
       ├── Release(token) ────────────►│
       │                               ├── Validate token
       │                               ├── Delete lock (no version bump)
       │◄── {released: true} ─────────│
```

---

## Stubs & Unimplemented Features

1. **Event aggregation for MapUpdatedEvent**: Only `MapObjectsChangedEvent` uses the aggregation buffer. `MapUpdatedEvent` (from PublishMapUpdate) publishes immediately on every call. The code comment notes "payload-level coalescing is complex."
<!-- AUDIT:NEEDS_DESIGN:2026-01-31:https://github.com/beyond-immersion/bannou-service/issues/199 -->

2. ~~**Spatial index garbage collection on channel reset**~~: **FIXED** (2026-01-31) - `ClearChannelDataAsync` now properly cleans up all spatial and type indexes before deleting objects. For each object, it fetches the object data to get Position/Bounds/ObjectType and calls the existing cleanup methods (`RemoveFromSpatialIndexAsync`, `RemoveFromSpatialIndexForBoundsAsync`, `RemoveFromTypeIndexAsync`). This adds one extra Redis GET per object but ensures no orphaned index entries remain.

3. **MapSnapshotEvent not published**: The events schema defines `MapSnapshotEvent` and its topic `map.{regionId}.{kind}.snapshot`, but `RequestSnapshot` only returns data to the caller -- it does not broadcast a snapshot event.
<!-- AUDIT:NEEDS_DESIGN:2026-01-31:https://github.com/beyond-immersion/bannou-service/issues/208 -->

4. **MapSnapshotRequestedEvent not consumed**: The schema defines `MapSnapshotRequestedEvent` for consumer-initiated snapshot requests, but the service does not subscribe to it.
<!-- AUDIT:NEEDS_DESIGN:2026-02-01:https://github.com/beyond-immersion/bannou-service/issues/240 -->

5. **Large payload support for PublishMapUpdate**: The publish endpoint rejects payloads exceeding `InlinePayloadMaxBytes` (returns 400). The code comment notes "MVP: reject large payloads; full impl would use lib-asset." Only RequestSnapshot handles large-to-asset offloading.

6. **Custom affordance scoring is basic**: `ScoreCustomAffordance` checks requires (objectTypes + min thresholds), prefers (+0.1 per matching property), and excludes (returns 0.0). More complex scoring rules (weighted preferences, ranges, spatial relationships) are not implemented.

---

## Potential Extensions

1. **Distributed lock for channel operations**: Currently uses optimistic read-modify-write for authority and version counters. Under high concurrency, version increments could produce duplicates. A Redis INCR atomic operation or distributed lock would prevent this.

2. **Spatial index TTL alignment**: Map objects have per-kind TTLs, but their spatial/type/region index entries do not expire. Stale index entries accumulate until the object is explicitly deleted or the index is cleaned on access.

3. **Batch object loading**: Queries load objects one-by-one from Redis. A pipeline or MGET operation would significantly reduce round-trips for large result sets.

4. **Hierarchical spatial indexing**: The current flat grid works well for uniform distributions but poorly for sparse worlds. An octree or R-tree structure would improve query performance for uneven object density.

5. **Authority transfer without gap**: Currently releasing and re-acquiring authority has a gap where the channel is unowned. A direct transfer operation would maintain continuous authority coverage.

6. **Snapshot diffing for require_consume takeover**: The `require_consume` mode blocks publishing until RequestSnapshot is called, but does not provide a diff between old and new data. A proper diff would enable incremental state reconciliation.

---

## Known Quirks & Caveats

### Bugs

1. **Version counter race condition**: `IncrementVersionAsync` performs a non-atomic read-increment-write on the version counter. Two concurrent publishes could read the same version and both write version+1, producing duplicate version numbers. This is mitigated by the authority model (single writer per channel) but could occur during `accept_and_alert` mode where unauthorized publishes are processed concurrently with authorized ones.

2. ~~**Orphaned spatial/type indexes on channel reset**~~: **FIXED** (2026-01-31) - `ClearChannelDataAsync` now properly cleans up all spatial and type indexes before deleting objects, preventing orphaned index entries.

3. **Index operations are not atomic**: The spatial, type, and region index operations perform read-modify-write on Redis lists without atomicity. Two concurrent requests adding objects to the same index cell could both read the same list, both add their object, and save—the second save overwrites the first, losing an object. This is mitigated by the authority model (single writer per channel) but could occur during `accept_and_alert` mode.

### Intentional Quirks

1. **Authority token contains expiry but expiry is NOT checked from token**: The token embeds channelId and expiresAt, but `ValidateAuthorityAsync` only uses the token's channelId for basic validation. Actual expiry is checked against `AuthorityRecord.ExpiresAt` which is updated by heartbeats. This is intentional -- heartbeats extend authority without re-issuing tokens.

2. ~~**Event aggregation buffer is fire-and-forget**~~: **FIXED** (2026-02-08) - The buffer now retries with exponential backoff (100ms base, up to `MaxBufferFlushRetries` attempts) before discarding changes. Failures are logged at Error level and published via `TryPublishErrorAsync`. Buffer is only removed/disposed after success or max retries exhausted. See [#310](https://github.com/beyond-immersion/bannou-service/issues/310).

3. **Update-as-upsert semantics**: `ObjectAction.Updated` for a non-existent object is treated as a create (upsert). This prevents data loss when object creation events are missed but means "update only if exists" semantics cannot be expressed.

4. **MaxAffordanceCandidates applies per-kind, not total**: `MaxAffordanceCandidates` is passed to each kind query individually, then results are combined. With 3 kinds and a limit of 1000, you could evaluate up to 3000 candidates total, not 1000.

5. **Objects without position get origin coordinates**: When a candidate object has `null` Position but passes the score threshold, the response uses `new Position3D { X = 0, Y = 0, Z = 0 }` as a default. This could place affordance locations at the world origin unexpectedly.

6. **Custom affordance exclusions check property existence, not value**: The exclusions logic checks if a property EXISTS on the object data, regardless of its value. Configuring `excludes: { is_occupied: true }` will exclude objects with `is_occupied: false` too.

7. **Unknown affordance types search all map kinds**: The `Custom` type and the fallback case both return ALL `MapKind` values. An unknown affordance type triggers the most expensive possible query, searching every kind.

8. **Authority expiry event uses discard pattern**: Authority expired event uses `_ = _messageBus.TryPublishAsync(...)` which discards the returned Task. This is intentional: the event is for **monitoring only** (non-critical), and the calling code path doesn't need to await it. Note that `TryPublishAsync` still buffers and retries internally if RabbitMQ is unavailable - the event WILL be delivered eventually (see MESSAGING.md Quirk #1). The discard means the caller doesn't check the return value, but since this is a monitoring event, that's acceptable.

### Design Considerations

1. **N+1 query pattern**: All spatial queries (point, bounds, type, affordance) load objects individually from Redis. A region with thousands of objects in a single bounds query generates thousands of Redis GET operations. Pipelining or MGET would be a significant performance improvement.

2. **No index compaction**: Spatial and type indexes grow as objects are added but entries are only removed on explicit delete. Long-running channels accumulate index bloat from updated objects that moved cells (the old cell entry is cleaned up, but the list itself is never compacted of null entries after object deletion leaves gaps).

3. **Event aggregation window is one-shot**: The buffer timer fires once after `EventAggregationWindowMs` and then the buffer is disposed. A sustained stream of changes creates a new buffer per window, rather than a persistent sliding window. This means bursty workloads aggregate well, but steady streams create many short-lived buffers.

4. **Affordance cache key is bounds-exact**: The cache key uses the exact min/max coordinates serialized as strings. Two queries with slightly different bounds (e.g., floating point rounding) will miss the cache. There is no spatial quantization of the cache key.

5. **Definition index is a single key**: All definition IDs are stored in one `List<Guid>`. With hundreds of definitions, this key becomes large and every CRUD operation requires loading/saving the entire list. A Redis SET or secondary index would scale better.

6. **Per-kind TTL conventions**: Durable kinds (terrain, static_geometry, navigation, ownership) use `-1` meaning no TTL. Ephemeral kinds (combat_effects at 30s, visual_effects at 60s) auto-expire from Redis. If an ephemeral object's spatial index entry outlives the object, queries will find the index entry but the object load returns null (safely filtered out, but wastes a round-trip).

7. **ExtractFeatures returns null for single-feature results**: The method always adds `objectType` to the features dictionary, then returns `null` if `features.Count <= 1`. When no relevant data properties are found, the result is always null.

8. **Hardcoded affordance scoring magic numbers**: AffordanceScorer contains ~15 hardcoded scoring weights (base score 0.5, cover weight 0.3, elevation divisor 100.0, size modifiers 0.8-1.2, etc.). Should be configuration properties for game-specific tuning.

---

## Work Tracking

### Active Items

*No active audit items.*

### Pending Investigation

*No pending investigation items.*

### Completed

- **2026-02-08**: Fixed event aggregation buffer fire-and-forget ([#310](https://github.com/beyond-immersion/bannou-service/issues/310)). Buffer now retries flush with exponential backoff (configurable via `MaxBufferFlushRetries`, default 3). Only disposes after success or max retries exhausted. Failures reported via `TryPublishErrorAsync`.

- **2026-01-31**: Fixed spatial index garbage collection on channel reset. `ClearChannelDataAsync` now properly cleans up all spatial and type indexes before deleting objects by fetching each object's Position/Bounds/ObjectType and calling the existing cleanup methods.
