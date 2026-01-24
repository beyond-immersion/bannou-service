# Mapping Plugin Deep Dive

> **Plugin**: lib-mapping
> **Schema**: schemas/mapping-api.yaml
> **Version**: 1.0.0
> **State Stores**: mapping-statestore (Redis)

---

## Overview

Spatial data management service for Arcadia game worlds. Handles authority-based channel ownership for exclusive write access, high-throughput ingest via dynamic RabbitMQ subscriptions, 3D spatial indexing with configurable cell sizes, affordance queries with multi-factor scoring, design-time authoring workflows (checkout/commit/release), and map definition templates. Uses a deterministic channel ID scheme (SHA-256 of region+kind) to ensure one authority per region/kind combination. Features per-kind TTL policies (durable for terrain/navigation/ownership, ephemeral for combat/visual effects), large payload offloading to lib-asset, event aggregation buffering to coalesce rapid object changes, configurable non-authority handling modes (reject_silent, reject_and_alert, accept_and_alert), and three takeover policies for authority succession (preserve_and_diff, require_consume, reset). Does NOT perform client-facing rendering or physics -- it is purely a spatial data store that game servers and NPC brains publish to and query from.

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

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `map:channel:{channelId}` | `ChannelRecord` | Channel configuration (region, kind, non-authority handling, takeover mode, alert config) |
| `map:authority:{channelId}` | `AuthorityRecord` | Current authority grant (token, app-id, expiry, RequiresConsumeBeforePublish flag) |
| `map:object:{regionId}:{objectId}` | `MapObject` | Individual map objects with position/bounds/data/version (TTL per kind) |
| `map:index:{regionId}:{kind}:{cellX}_{cellY}_{cellZ}` | `List<Guid>` | Spatial cell index - object IDs within a 3D grid cell |
| `map:type-index:{regionId}:{objectType}` | `List<Guid>` | Type index - object IDs grouped by publisher-defined type |
| `map:region-index:{regionId}:{kind}` | `List<Guid>` | Region index - all object IDs in a region+kind (for full-region queries) |
| `map:checkout:{regionId}:{kind}` | `CheckoutRecord` | Authoring checkout lock (editor ID, authority token, expiry) |
| `map:version:{channelId}` | `LongWrapper` | Monotonic version counter per channel |
| `map:affordance-cache:{regionId}:{type}:{boundsHash}` | `CachedAffordanceResult` | Cached affordance query results with timestamp |
| `map:definition:{definitionId}` | `DefinitionRecord` | Map definition templates (name, layers, default bounds, metadata) |
| `map:definition-index` | `DefinitionIndexEntry` | Index of all definition IDs for listing |

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
| `InlinePayloadMaxBytes` | `MAPPING_INLINE_PAYLOAD_MAX_BYTES` | `65536` | Threshold for offloading to lib-asset (64KB) |
| `MaxCheckoutDurationSeconds` | `MAPPING_MAX_CHECKOUT_DURATION_SECONDS` | `1800` | Maximum authoring lock duration (30 min) |
| `DefaultLayerCacheTtlSeconds` | `MAPPING_DEFAULT_LAYER_CACHE_TTL_SECONDS` | `3600` | Default TTL for ephemeral layer data (1 hour) |
| `EventAggregationWindowMs` | `MAPPING_EVENT_AGGREGATION_WINDOW_MS` | `100` | Buffering window for MapObjectsChangedEvent (0 = disabled) |
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
| `MappingServiceConfiguration` | Singleton | All 25 config properties |
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

2. **Spatial index garbage collection on channel reset**: `ClearChannelDataAsync` deletes objects and the region index but notes "Spatial and type indexes are per-object and will be orphaned. They'll be cleaned up on next access or could be garbage collected." No GC is implemented.

3. **MapSnapshotEvent not published**: The events schema defines `MapSnapshotEvent` and its topic `map.{regionId}.{kind}.snapshot`, but `RequestSnapshot` only returns data to the caller -- it does not broadcast a snapshot event.

4. **MapSnapshotRequestedEvent not consumed**: The schema defines `MapSnapshotRequestedEvent` for consumer-initiated snapshot requests, but the service does not subscribe to it.

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

## Tenet Violations (Fix Immediately)

### FOUNDATION TENETS - T6: Constructor Null Checks Missing

**File**: `plugins/lib-mapping/MappingService.cs`, lines 99-106
**Issue**: The constructor assigns all dependencies directly without null-guard checks. Per T6 (Service Implementation Pattern), constructor dependencies MUST have explicit null checks with `ArgumentNullException`.

```csharp
// Current (no null checks):
_messageBus = messageBus;
_messageSubscriber = messageSubscriber;
_stateStoreFactory = stateStoreFactory;
_logger = logger;
_configuration = configuration;
_assetClient = assetClient;
_httpClientFactory = httpClientFactory;
_affordanceScorer = affordanceScorer;
```

**Fix**: Add `?? throw new ArgumentNullException(nameof(...))` to each assignment, e.g.:
```csharp
_messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
```

---

### IMPLEMENTATION TENETS - T7: Missing ApiException Catch Blocks

**File**: `plugins/lib-mapping/MappingService.cs`, all public methods
**Issue**: Every try-catch block catches only `Exception` and returns `InternalServerError`. Per T7 (Error Handling), services MUST distinguish between `ApiException` (expected downstream failures, log as Warning, propagate status) and `Exception` (unexpected, log as Error, emit error event).

**Affected methods**: `CreateChannelAsync`, `ReleaseAuthorityAsync`, `AuthorityHeartbeatAsync`, `PublishMapUpdateAsync`, `PublishObjectChangesAsync`, `RequestSnapshotAsync`, `QueryPointAsync`, `QueryBoundsAsync`, `QueryObjectsByTypeAsync`, `QueryAffordanceAsync`, `CheckoutForAuthoringAsync`, `CommitAuthoringAsync`, `ReleaseAuthoringAsync`, `CreateDefinitionAsync`, `GetDefinitionAsync`, `ListDefinitionsAsync`, `UpdateDefinitionAsync`, `DeleteDefinitionAsync`.

**Fix**: Add a `catch (ApiException ex)` block before the generic `catch (Exception ex)` in each method:
```csharp
catch (ApiException ex)
{
    _logger.LogWarning(ex, "Service call failed with status {Status}", ex.StatusCode);
    return ((StatusCodes)ex.StatusCode, null);
}
```

---

### IMPLEMENTATION TENETS - T9: Non-Atomic Index Operations Without Distributed Locks

**File**: `plugins/lib-mapping/MappingService.cs`, lines 1859-1981
**Issue**: All index operations (`UpdateSpatialIndexAsync`, `UpdateTypeIndexAsync`, `AddToRegionIndexAsync`, and their removal counterparts) perform non-atomic read-modify-write on shared state without distributed locks. The service does not inject `IDistributedLockProvider` at all. Per T9 (Multi-Instance Safety), operations requiring cross-instance consistency MUST use `IDistributedLockProvider`.

While the authority model (single writer per channel) mitigates this in the normal case, the `accept_and_alert` mode explicitly allows concurrent writes from unauthorized publishers. Additionally, `IncrementVersionAsync` (line 2085) has the same non-atomic read-increment-write pattern.

**Fix**: Inject `IDistributedLockProvider` and wrap index mutations and version increments with distributed locks when operating in `accept_and_alert` mode, or use ETags for optimistic concurrency on index writes.

---

### IMPLEMENTATION TENETS - T21: Hardcoded Tunables in AffordanceScorer

**File**: `plugins/lib-mapping/Helpers/AffordanceScorer.cs`, lines 33-93
**Issue**: The AffordanceScorer contains numerous hardcoded magic numbers that are tunables requiring configuration properties per T21 (Configuration-First):

| Magic Number | Line | Purpose |
|---|---|---|
| `0.5` | 33, 147 | Base score for affordance candidates |
| `0.3` | 42 | Cover rating weight multiplier |
| `100.0` | 50 | Elevation divisor |
| `0.3` | 50 | Max elevation bonus |
| `0.05` | 58 | Sightlines weight multiplier |
| `0.2` | 58 | Max sightlines bonus |
| `1.2, 1.1, 1.0, 0.9, 0.8` | 71-76 | Actor size modifiers |
| `0.2` | 83 | Stealth rating multiplier |
| `0.1` | 197 | Preference match bonus |

**Fix**: Define these as configuration properties in `schemas/mapping-configuration.yaml` (e.g., `AffordanceBaseScore`, `AffordanceCoverWeight`, `AffordanceElevationDivisor`, etc.) and read them from `MappingServiceConfiguration`.

---

### IMPLEMENTATION TENETS - T21: Hardcoded Exclusion Distance Tolerance

**File**: `plugins/lib-mapping/MappingService.cs`, lines 1072-1074
**Issue**: The position exclusion tolerance `1.0` is a hardcoded tunable:
```csharp
Math.Abs(p.X - candidate.Position.X) < 1.0 &&
Math.Abs(p.Y - candidate.Position.Y) < 1.0 &&
Math.Abs(p.Z - candidate.Position.Z) < 1.0
```

Per T21, any tunable value (limits, timeouts, thresholds) MUST be a configuration property.

**Fix**: Add `AffordanceExclusionToleranceUnits` to the configuration schema and replace the literal `1.0` with `_configuration.AffordanceExclusionToleranceUnits`.

---

### IMPLEMENTATION TENETS - T25: String Properties in Internal POCOs

**File**: `plugins/lib-mapping/MappingService.cs`, lines 2686-2715
**Issue**: Several internal record classes use `string` for fields that should use stronger types per T25 (Internal Model Type Safety):

- `AuthorityRecord.AuthorityAppId` (line 2687): This represents an application identifier. If there is an enum for app-ids, it should be used; otherwise, this is acceptable as a string since app-ids are arbitrary external identifiers.
- `CheckoutRecord.EditorId` (line 2714): Same consideration -- if editor IDs are GUIDs in the API schema, this should be `Guid`.

**Minor concern**: The `= string.Empty` default initializers on these properties are acceptable as they are property initializers for serialization compatibility (not `?? string.Empty` coercion), but review whether these fields should be nullable instead of defaulting to empty string, which could hide uninitialized state.

---

### QUALITY TENETS - T19: Missing XML Documentation on Internal Records

**File**: `plugins/lib-mapping/MappingService.cs`, lines 2670-2732
**Issue**: The internal record classes (`ChannelRecord`, `AuthorityRecord`, `DefinitionRecord`, `DefinitionIndexEntry`, `CheckoutRecord`, `CachedAffordanceResult`) and their properties lack XML documentation comments. Per T19 (XML Documentation), all public and internal types and their members should have `<summary>` tags. Only `LongWrapper` and `EventAggregationBuffer` have documentation.

**Fix**: Add `<summary>` tags to each internal class and their properties.

---

### QUALITY TENETS - T19: Missing XML Documentation on Private Helper Methods

**File**: `plugins/lib-mapping/MappingService.cs`
**Issue**: Multiple private/internal helper methods lack XML documentation:
- `ProcessAuthorizedObjectChangesAsync` (line 607)
- `HandleNonAuthorityObjectChangesAsync` (line 666)
- `PublishUnauthorizedObjectChangesWarningAsync` (line 698)
- `HandleNonAuthorityPublishAsync` (line 1631)
- `ProcessPayloadsAsync` (line 1666)
- `ProcessObjectChangeAsync` (line 1713)
- `ProcessObjectChangeWithIndexCleanupAsync` (line 1719)
- `UpdateSpatialIndexAsync` (line 1859)
- `UpdateSpatialIndexForBoundsAsync` (line 1874)
- `UpdateTypeIndexAsync` (line 1892)
- `AddToRegionIndexAsync` (line 1908)
- `RemoveFromRegionIndexAsync` (line 1922)
- `RemoveFromSpatialIndexAsync` (line 1936)
- `RemoveFromSpatialIndexForBoundsAsync` (line 1951)
- `RemoveFromTypeIndexAsync` (line 1969)
- `UploadLargePayloadToAssetAsync` (line 1985)
- `ClearRequiresConsumeForAuthorityAsync` (line 2035)
- `ClearChannelDataAsync` (line 2054)
- `IncrementVersionAsync` (line 2085)
- `QueryObjectsInRegionAsync` (line 2097)
- `QueryObjectsInBoundsAsync` (line 2124)
- All `Bounds*` geometry helpers (lines 2176-2203)
- Affordance caching helpers (lines 2209-2248)
- Event publishing helpers (lines 2254-2378)
- Ingest helpers (lines 2381-2651)

While T19 formally applies to public APIs, comprehensive XML documentation on internal methods improves maintainability for a service of this complexity.

---

### QUALITY TENETS - T19: Missing XML Documentation on AffordanceScorer Private Methods

**File**: `plugins/lib-mapping/Helpers/AffordanceScorer.cs`, lines 145-248
**Issue**: The `ScoreCustomAffordance` method (line 145) has XML documentation, but `TryGetJsonDouble` (line 221) and `TryGetJsonInt` (line 237) have it. This is consistent. No violation here for private methods -- keeping for completeness of audit.

---

### IMPLEMENTATION TENETS - T21: Configuration Property `MaxSpatialQueryResults` Misused

**File**: `plugins/lib-mapping/MappingService.cs`, line 712
**Issue**: The configuration property `MaxSpatialQueryResults` (documented as "Maximum distinct object types in warning summaries") is used as a `.Take()` limit on object types in warning payloads. The name suggests spatial query results, not warning summary truncation. This is potentially a naming issue or dead/misused configuration.

**Fix**: Rename to `MaxWarningObjectTypeSummary` in the configuration schema to accurately reflect its purpose, or use a dedicated config property.

---

### IMPLEMENTATION TENETS - T7: Missing Error Event in HandleIngestEventAsync

**File**: `plugins/lib-mapping/MappingService.cs`, lines 2502-2505
**Issue**: The catch block in `HandleIngestEventAsync` logs the error but does NOT call `TryPublishErrorAsync`. Per T7 (Error Handling), unexpected exceptions in service logic MUST emit error events for monitoring:

```csharp
catch (Exception ex)
{
    _logger.LogError(ex, "Error handling ingest event for channel {ChannelId}", channelId);
    // Missing: await _messageBus.TryPublishErrorAsync(...)
}
```

**Fix**: Add `TryPublishErrorAsync` call after the `LogError`.

---

### IMPLEMENTATION TENETS - T23: Fire-and-Forget Task Discard

**File**: `plugins/lib-mapping/MappingService.cs`, line 1609
**Issue**: The authority expired event publish uses fire-and-forget discard pattern:
```csharp
_ = _messageBus.TryPublishAsync(AUTHORITY_EXPIRED_TOPIC, new MappingAuthorityExpiredEvent { ... });
```

Per T23 (Async Method Pattern), discarding tasks with `_ =` without awaiting means exceptions are silently lost and the operation timing is unpredictable. While this is documented as intentional ("fire-and-forget for monitoring"), it violates the principle that Task-returning calls should be awaited.

**Fix**: Either await the call (preferred - TryPublishAsync is already safe and won't throw), or document this as an intentional exception with a comment explaining why fire-and-forget is acceptable here.

---

### QUALITY TENETS - T10: LogWarning Used for Non-Authority Rejection in accept_and_alert

**File**: `plugins/lib-mapping/MappingService.cs`, lines 2519, 2524
**Issue**: Non-authority ingest handling logs at `LogWarning` level for both `reject_and_alert` and `accept_and_alert` modes:
```csharp
_logger.LogWarning("Rejecting non-authority ingest with alert for channel {ChannelId}", channel.ChannelId);
_logger.LogWarning("Accepting non-authority ingest with alert for channel {ChannelId}", channel.ChannelId);
```

Per T10 guidelines, warnings are for expected failures (auth failures, permission denials). Non-authority publishing in `accept_and_alert` mode is a configured, expected behavior -- `LogInformation` would be more appropriate for the accept case since it represents a business decision, not a failure.

**Fix**: Change the `accept_and_alert` log to `LogInformation` since it represents a configured business decision (the system is intentionally accepting the publish).

---

### FOUNDATION TENETS - T6: IStateStoreFactory Used Inline Instead of Pre-Built Stores

**File**: `plugins/lib-mapping/MappingService.cs`, throughout
**Issue**: The service calls `_stateStoreFactory.GetStore<T>(StateStoreDefinitions.Mapping)` on every operation (50+ occurrences) instead of creating typed state stores in the constructor as recommended by T6 (Service Implementation Pattern). The typical pattern is:
```csharp
_stateStore = stateStoreFactory.GetStore<Model>(StateStoreDefinitions.ServiceName);
```

While `GetStore<T>` may return cached instances internally, the pattern of calling it inline with different type parameters on every operation adds noise and makes it harder to understand the state access patterns.

**Fix**: Create pre-built store fields in the constructor for the commonly used types:
```csharp
private readonly IStateStore<ChannelRecord> _channelStore;
private readonly IStateStore<AuthorityRecord> _authorityStore;
private readonly IStateStore<MapObject> _objectStore;
private readonly IStateStore<List<Guid>> _indexStore;
// etc.
```

---

### IMPLEMENTATION TENETS - T5: Event Kind Field Uses .ToString() Instead of Enum String

**File**: `plugins/lib-mapping/MappingService.cs`, lines 2262, 2263, 2281, 2339, 2363, 2601, 429, 355, 722, 1615
**Issue**: When publishing events, the `Kind` field is populated using `channel.Kind.ToString()`. The event schema defines `Kind` as a string field for cross-language compatibility, so the `.ToString()` conversion is necessary at the event boundary. However, multiple event models also use `.ToString()` on `NonAuthorityHandling` enums (line 2263) -- this is the correct pattern per T25 (convert at event boundary only).

**Verdict**: This is acceptable per T25's "Event Model Conversions" section. No violation.

---

### IMPLEMENTATION TENETS - T24: EventAggregationBuffer Timer Callback Uses try/finally Without using

**File**: `plugins/lib-mapping/MappingService.cs`, lines 2789-2799
**Issue**: The `OnTimerElapsed` callback uses `Task.Run` with a try/finally block. The `finally` block calls `Dispose()` on the buffer itself. Per T24, this pattern is acceptable because the disposal scope extends beyond the creating method (class-owned resource with class's own `Dispose()`). However, the `Task.Run` fire-and-forget means if the flush fails, the error is silently swallowed.

**Verdict**: The try/finally pattern is acceptable here (class-owned resource). The silent error swallowing is documented in Known Quirks #4.

---

## Known Quirks & Caveats

### Bugs (Fix When Discovered)

1. **Version counter race condition**: `IncrementVersionAsync` performs a non-atomic read-increment-write on the version counter. Two concurrent publishes could read the same version and both write version+1, producing duplicate version numbers. This is mitigated by the authority model (single writer per channel) but could occur during `accept_and_alert` mode where unauthorized publishes are processed concurrently with authorized ones.

2. **Orphaned spatial/type indexes on channel reset**: When `ClearChannelDataAsync` runs (reset takeover mode), it deletes objects and the region index but leaves spatial and type indexes pointing to deleted objects. Subsequent queries will attempt to load deleted objects (returning null, filtered out) but the index entries persist indefinitely.

3. **Index operations are not atomic**: The spatial, type, and region index operations perform read-modify-write on Redis lists without atomicity. Two concurrent requests adding objects to the same index cell could both read the same list, both add their object, and save—the second save overwrites the first, losing an object. This is mitigated by the authority model (single writer per channel) but could occur during `accept_and_alert` mode.

### Intentional Quirks (Documented Behavior)

1. **Static ConcurrentDictionary for subscriptions**: `IngestSubscriptions` and `EventAggregationBuffers` are `static readonly` fields. This means subscription state survives across scoped service instances within the same process. This is intentional -- subscriptions must outlive individual HTTP requests.

2. **Deterministic channel IDs**: Channel IDs are SHA-256(regionId:kind) truncated to 16 bytes as a GUID. This means the same region+kind always produces the same channel ID, enabling idempotent channel creation. Two different region+kind combinations producing the same hash (collision) is astronomically unlikely.

3. **Authority token contains expiry but expiry is NOT checked from token**: The token embeds channelId and expiresAt, but `ValidateAuthorityAsync` only uses the token's channelId for basic validation. Actual expiry is checked against `AuthorityRecord.ExpiresAt` which is updated by heartbeats. This is intentional -- heartbeats extend authority without re-issuing tokens.

4. **Event aggregation buffer is fire-and-forget**: The timer callback uses `Task.Run` to flush changes and cannot propagate exceptions. If the flush fails (e.g., message bus unavailable), the changes are silently lost. The buffer is removed from the dictionary after flush regardless of success.

5. **Update-as-upsert semantics**: `ObjectAction.Updated` for a non-existent object is treated as a create (upsert). This prevents data loss when object creation events are missed but means "update only if exists" semantics cannot be expressed.

6. **Authoring and runtime channels are independent**: Authoring checkouts (`map:checkout:*`) and runtime authority (`map:authority:*`) are separate concepts. An authoring checkout does not prevent runtime publishing to the same region+kind, and vice versa. They share the same underlying object storage.

7. **Heartbeat returns warning but does NOT fail**: When remaining authority time is below `AuthorityGracePeriodSeconds`, the heartbeat still succeeds (returns `valid=true`) but includes a warning string. The caller is expected to react to the warning by increasing heartbeat frequency.

### Design Considerations (Requires Planning)

1. **N+1 query pattern**: All spatial queries (point, bounds, type, affordance) load objects individually from Redis. A region with thousands of objects in a single bounds query generates thousands of Redis GET operations. Pipelining or MGET would be a significant performance improvement.

2. **No index compaction**: Spatial and type indexes grow as objects are added but entries are only removed on explicit delete. Long-running channels accumulate index bloat from updated objects that moved cells (the old cell entry is cleaned up, but the list itself is never compacted of null entries after object deletion leaves gaps).

3. **Event aggregation window is one-shot**: The buffer timer fires once after `EventAggregationWindowMs` and then the buffer is disposed. A sustained stream of changes creates a new buffer per window, rather than a persistent sliding window. This means bursty workloads aggregate well, but steady streams create many short-lived buffers.

4. **Affordance cache key is bounds-exact**: The cache key uses the exact min/max coordinates serialized as strings. Two queries with slightly different bounds (e.g., floating point rounding) will miss the cache. There is no spatial quantization of the cache key.

5. **Definition index is a single key**: All definition IDs are stored in one `List<Guid>`. With hundreds of definitions, this key becomes large and every CRUD operation requires loading/saving the entire list. A Redis SET or secondary index would scale better.

6. **Per-kind TTL conventions**: Durable kinds (terrain, static_geometry, navigation, ownership) use `-1` meaning no TTL. Ephemeral kinds (combat_effects at 30s, visual_effects at 60s) auto-expire from Redis. If an ephemeral object's spatial index entry outlives the object, queries will find the index entry but the object load returns null (safely filtered out, but wastes a round-trip).

7. **MaxAffordanceCandidates applies per-kind, not total**: In `QueryAffordanceAsync` (lines 1052-1058), `MaxAffordanceCandidates` is passed to each kind query individually, then results are combined with `AddRange`. With 3 kinds and a limit of 1000, you could evaluate up to 3000 candidates total, not 1000.

8. **Cached affordance metadata is overwritten**: When returning a cached affordance result (lines 1033-1040), the `QueryMetadata` is replaced with placeholder values (`KindsSearched = []`, `ObjectsEvaluated = 0`, `CandidatesGenerated = 0`). The original search statistics from the cached response are lost—only `CacheHit = true` is accurate.

9. **Objects without position get origin coordinates**: In `QueryAffordanceAsync` (line 1086), when a candidate object has `null` Position but passes the score threshold, the response uses `new Position3D { X = 0, Y = 0, Z = 0 }` as a default. This could place affordance locations at the world origin unexpectedly.

10. **Custom affordance exclusions check property existence, not value**: In `AffordanceScorer.ScoreCustomAffordance` (lines 206-212), the exclusions logic checks if a property EXISTS on the object data, regardless of its value. Configuring `excludes: { is_occupied: true }` will exclude objects with `is_occupied: false` too. This is intentional (simple implementation) but could be surprising.

11. **Unknown affordance types search all map kinds**: In `AffordanceScorer.GetKindsForAffordanceType` (lines 24-26), the `Custom` type and the fallback case (`_`) both return ALL `MapKind` values. An unknown affordance type triggers the most expensive possible query, searching every kind.

12. **ExtractFeatures returns null for single-feature results**: In `AffordanceScorer.ExtractFeatures` (line 139), the method always adds `objectType` to the features dictionary, then returns `null` if `features.Count <= 1`. When no relevant data properties are found, the result is always null (only objectType exists, count == 1).
