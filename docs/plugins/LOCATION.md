# Location Plugin Deep Dive

> **Plugin**: lib-location
> **Schema**: schemas/location-api.yaml
> **Version**: 1.0.0
> **Layer**: GameFoundation
> **State Store**: location-statestore (MySQL)

---

## Overview

Hierarchical location management (L2 GameFoundation) for the Arcadia game world. Manages physical places (cities, regions, buildings, rooms, landmarks) within realms as a tree structure with depth tracking. Each location belongs to exactly one realm and optionally has a parent location. Supports deprecation, circular reference prevention, cascading depth updates, code-based lookups, and bulk seeding with two-pass parent resolution.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | MySQL persistence for locations, code indexes, realm indexes, parent indexes |
| lib-state (`IDistributedLockProvider`) | Distributed locks for concurrent index modifications |
| lib-messaging (`IMessageBus`) | Publishing location lifecycle events; error event publishing |
| lib-realm (`IRealmClient`) | Realm existence validation on creation; realm code resolution during seed |
| lib-resource (`IResourceClient`) | Reference tracking checks before deletion; cleanup coordination |
| lib-contract (`IContractClient`) | Territory clause type registration during plugin startup (hard dependency — fail-fast per FOUNDATION TENETS) |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-realm | Calls `ListRootLocationsAsync`, `GetLocationDescendantsAsync`, `TransferLocationToRealmAsync`, `SetLocationParentAsync` via `ILocationClient` during realm merge |
| lib-character-encounter | Stores `LocationId` as optional encounter context (stores reference but does not call `ILocationClient`) |

**Contract Integration**: Location registers a `territory_constraint` clause type with Contract during plugin startup. Contract service will call `/location/validate-territory` via `IServiceNavigator` when evaluating territory constraint clauses. This enables Contract to validate territorial boundaries without a direct dependency on Location (SERVICE_HIERARCHY compliant: L2 Location registers with L1 Contract).

---

### Type Field Classification

| Field | Category | Type | Rationale |
|-------|----------|------|-----------|
| `LocationType` | C (System State) | Service-specific enum (`CONTINENT`, `REGION`, `CITY`, `DISTRICT`, `BUILDING`, `ROOM`, `LANDMARK`, `OTHER`) | Finite set of structural categories defining the role of a location in the containment hierarchy. System-owned; adding new location types requires schema changes because they affect hierarchy semantics |
| `BoundsPrecision` | C (System State) | Service-specific enum (`exact`, `approximate`, `none`) | Finite set of system-owned precision levels for spatial bounds data quality |
| `CoordinateMode` | C (System State) | Service-specific enum (`inherit`, `local`, `portal`) | Finite set of system-owned modes describing how a location's coordinate system relates to its parent |
| `TerritoryMode` | C (System State) | Service-specific enum (`exclusive`, `inclusive`) | Finite set of two system-owned validation modes for territory constraint checking |
| `entityType` (entity presence) | B (Content Code) | Opaque string | Entity type for presence tracking (e.g., "character", "actor", "npc", "player"). Described as opaque string in the schema; game-configurable to support arbitrary entity type classifications without schema changes |

---

## State Storage

**Stores**:
- `location-statestore` (via `StateStoreDefinitions.Location`) - MySQL backend (persistent)
- `location-cache` (via `StateStoreDefinitions.LocationCache`) - Redis backend (cache)
- `location-lock` (via `StateStoreDefinitions.LocationLock`) - Redis backend (distributed locks)
- `location-entity-presence` (via `StateStoreDefinitions.LocationEntityPresence`) - Redis backend (ephemeral entity-to-location bindings with TTL)
- `location-entity-set` (via `StateStoreDefinitions.LocationEntitySet`) - Redis backend (Sets tracking which entities are at each location)

| Key Pattern | Data Type | Store | Purpose |
|-------------|-----------|-------|---------|
| `location:{locationId}` | `LocationModel` | MySQL | Individual location definition (persistent) |
| `location:{locationId}` | `LocationModel` | Redis (cache) | Location cache (TTL-based) |
| `code-index:{realmId}:{CODE}` | `string` | MySQL | Code → location ID (unique per realm) |
| `realm-index:{realmId}` | `List<Guid>` | MySQL | All location IDs in a realm |
| `parent-index:{realmId}:{parentId}` | `List<Guid>` | MySQL | Child location IDs for a parent |
| `root-locations:{realmId}` | `List<Guid>` | MySQL | Location IDs with no parent in a realm |
| `entity-location:{entityType}:{entityId}` | `EntityPresenceModel` | Redis (presence) | Entity-to-location binding with TTL |
| `location-entities:{locationId}` | `Set<string>` | Redis (entity-set) | Set of `{entityType}:{entityId}` members at a location |
| `location-entities:__index__` | `Set<string>` | Redis (entity-set) | Index of location IDs with active entity sets (for cleanup worker discovery) |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `location.created` | `LocationCreatedEvent` | New location created |
| `location.updated` | `LocationUpdatedEvent` | Location fields changed (includes `ChangedFields`) |
| `location.deleted` | `LocationDeletedEvent` | Location hard-deleted |
| `location.entity-arrived` | `LocationEntityArrivedEvent` | Entity presence reported at a new location (not on TTL refresh) |
| `location.entity-departed` | `LocationEntityDepartedEvent` | Entity presence cleared or moved to a different location |

### Consumed Events

This plugin does not consume external events.

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `MaxAncestorDepth` | `LOCATION_MAX_ANCESTOR_DEPTH` | `20` | Maximum depth for ancestor chain traversal |
| `DefaultDescendantMaxDepth` | `LOCATION_DEFAULT_DESCENDANT_MAX_DEPTH` | `10` | Default max depth when listing descendants |
| `MaxDescendantDepth` | `LOCATION_MAX_DESCENDANT_DEPTH` | `20` | Safety limit for descendant/circular reference checks |
| `CacheTtlSeconds` | `LOCATION_CACHE_TTL_SECONDS` | `3600` | TTL for location cache entries (locations change infrequently) |
| `IndexLockTimeoutSeconds` | `LOCATION_INDEX_LOCK_TIMEOUT_SECONDS` | `5` | Timeout for acquiring distributed locks on index operations |
| `EntityPresenceTtlSeconds` | `LOCATION_ENTITY_PRESENCE_TTL_SECONDS` | `30` | Default TTL for entity presence entries (reporters must refresh within this window) |
| `EntityPresenceCleanupIntervalSeconds` | `LOCATION_ENTITY_PRESENCE_CLEANUP_INTERVAL_SECONDS` | `60` | Interval between background cleanup cycles for expired set members |
| `EntityPresenceCleanupStartupDelaySeconds` | `LOCATION_ENTITY_PRESENCE_CLEANUP_STARTUP_DELAY_SECONDS` | `15` | Delay before entity presence cleanup worker starts processing |
| `MaxEntitiesPerLocationQuery` | `LOCATION_MAX_ENTITIES_PER_LOCATION_QUERY` | `100` | Maximum entities returned by list-entities-at-location (pagination cap) |
| `ContextCacheTtlSeconds` | `LOCATION_CONTEXT_CACHE_TTL_SECONDS` | `10` | TTL for location context cache entries used by variable provider |
| `ContextNearbyPoisLimit` | `LOCATION_CONTEXT_NEARBY_POIS_LIMIT` | `50` | Maximum nearby POIs to include in location context data |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<LocationService>` | Scoped | Structured logging |
| `LocationServiceConfiguration` | Singleton | All 11 config properties |
| `IStateStoreFactory` | Singleton | State store access |
| `IDistributedLockProvider` | Singleton | Distributed locks for index concurrency |
| `IMessageBus` | Scoped | Event publishing and error events |
| `IRealmClient` | Scoped | Realm validation |
| `IResourceClient` | Scoped | Reference tracking checks before deletion |
| `ITelemetryProvider` | Singleton | Distributed tracing spans for async helpers |
| `ILocationDataCache` | Singleton | ConcurrentDictionary cache for location context provider data |

Service lifetime is **Scoped** (per-request).

**Variable Provider**: `LocationContextProviderFactory` implements `IVariableProviderFactory` providing the `${location.*}` namespace to Actor (L2) via the Variable Provider Factory pattern. Variables: `zone` (location code), `name`, `region` (nearest REGION ancestor code), `type` (LocationType), `depth`, `realm` (realm code), `nearby_pois` (sibling location codes), `entity_count` (entities at current location). Data loaded via `ILocationDataCache` which caches pre-resolved context per character with configurable TTL.

**Background Services**:
- `EntityPresenceCleanupWorker` — Periodically cleans up stale members from location-entities sets. When an entity's presence TTL expires, the entity-location key is removed by Redis automatically, but the entity remains in the location's entity set. This worker evicts those stale set members using the `location-entities:__index__` index set for discovery.

---

## API Endpoints (Implementation Notes)

### Read Operations (11 endpoints)

- **GetLocation** (`/location/get`): Direct lookup by location ID. Returns full location data with parent reference and depth.
- **GetLocationByCode** (`/location/get-by-code`): Code index lookup using `{realmId}:{CODE}` composite key. Codes are unique per realm.
- **ListLocations** (`/location/list`): Loads from realm index, filters by `locationType`, `includeDeprecated`. In-memory pagination (page/pageSize, default 20).
- **ListLocationsByRealm** (`/location/list-by-realm`): Loads all location IDs from realm index, bulk-loads via `GetBulkAsync`. Filters by type and deprecation.
- **ListLocationsByParent** (`/location/list-by-parent`): Loads parent's child index. Validates parent exists first (404 if missing). Filters by type and deprecation.
- **ListRootLocations** (`/location/list-root`): Loads `root-locations:{realmId}` index. Returns top-level locations with no parent.
- **GetLocationAncestors** (`/location/get-ancestors`): Walks parent chain iteratively. Safety limit via `MaxAncestorDepth` config (default 20) to prevent infinite loops from corrupted data.
- **GetLocationDescendants** (`/location/get-descendants`): Recursive traversal via `CollectDescendantsAsync`. Safety limit of 20 depth levels. Optional `maxDepth` parameter.
- **ValidateTerritory** (`/location/validate-territory`): Territory constraint checking designed for Contract service's clause handler system. Builds location + ancestor hierarchy set, checks for overlap with territory location IDs. Supports two modes: `exclusive` (location must NOT overlap territory) and `inclusive` (location MUST be within territory). Defaults to `Exclusive` mode when `TerritoryMode` is not specified. The `territory_constraint` clause type is registered with Contract during plugin startup via `LocationServicePlugin.OnRunningAsync()`.
- **LocationExists** (`/location/exists`): Quick existence check. Returns `Exists` boolean and `IsActive` (not deprecated) flag.
- **QueryLocationsByPosition** (`/location/query/by-position`): Spatial query. Given a Position3D and realmId, returns all locations whose BoundingBox3D bounds contain that position. Only considers locations with non-null bounds and `boundsPrecision != none`. Results ordered by depth descending (most specific first). Supports optional `maxDepth` filter and pagination. Loads all realm locations, filters in-memory via AABB containment check.

### Write Operations (9 endpoints)

- **CreateLocation** (`/location/create`): Validates realm exists via `IRealmClient`. Normalizes code to uppercase. Checks code uniqueness within realm. If parent specified: validates parent exists, checks same realm, sets depth=parent.depth+1. Accepts optional spatial fields (bounds, boundsPrecision, coordinateMode, localOrigin) — defaults to `BoundsPrecision.None` and `CoordinateMode.Inherit`. Updates all indexes (code, realm, parent/root). Publishes `location.created`.
- **UpdateLocation** (`/location/update`): Partial update for name, description, locationType, bounds, boundsPrecision, coordinateMode, localOrigin, metadata. Tracks `changedFields`. Does not allow parent or code changes (use separate endpoints). Publishes `location.updated`.
- **SetLocationParent** (`/location/set-parent`): Circular reference detection via `IsDescendantOfAsync` (max 20 depth). Validates new parent is in same realm. Updates old parent's child index, new parent's child index, root-locations index. Cascading depth update for all descendants via `UpdateDescendantDepthsAsync`. Publishes update event.
- **RemoveLocationParent** (`/location/remove-parent`): Makes location a root (depth=0). Updates parent index and root-locations index. Cascading depth update for descendants.
- **DeleteLocation** (`/location/delete`): Requires no child locations (Conflict if children exist). Checks external references via `IResourceClient` - if references exist, executes cleanup callbacks before proceeding (returns Conflict if cleanup fails). Removes from all indexes. Publishes `location.deleted`. Does NOT require deprecation first (unlike species/relationship types).
- **DeprecateLocation** (`/location/deprecate`): Sets `IsDeprecated=true` with timestamp and reason. Location remains queryable. Publishes update event.
- **UndeprecateLocation** (`/location/undeprecate`): Restores deprecated location. Returns BadRequest if not deprecated.
- **TransferLocationToRealm** (`/location/transfer-realm`): Moves a location to a different realm. Validates target realm exists and is active via `IRealmClient`. Checks code uniqueness in target realm (returns 409 Conflict on collision). Removes from all source realm indexes (code, realm, parent, root), clears parent (becomes root, depth 0), saves with new realm ID, adds to target realm indexes. Idempotent (no-op if already in target realm). Publishes `location.updated` with changed fields: `realmId`, `parentLocationId`, `depth`. Invalidates cache. Used by Realm merge for tree migration — caller is responsible for re-parenting descendants after transfer.
- **SeedLocations** (`/location/seed`): Two-pass algorithm. Pass 1: Creates all locations without parent relationships, resolves realm codes via `IRealmClient`. Spatial fields (bounds, boundsPrecision, coordinateMode, localOrigin) are passed through when present. Pass 2: Sets parent relationships by resolving parent codes from pass 1 results. Supports `updateExisting` (spatial fields updated when provided). Returns created/updated/skipped/errors.

### Entity Presence Operations (4 endpoints)

- **ReportEntityPosition** (`/location/report-entity-position`): Reports an entity's current location. Validates location exists, saves ephemeral presence with configurable TTL (`EntityPresenceTtlSeconds`, default 30s). Detects location changes via caller-hint (`previousLocationId`) fast path or atomic GETSET fallback. On actual location change: updates entity sets (removes from old location set, adds to new), maintains index set, publishes `location.entity-arrived` and `location.entity-departed` events. TTL refreshes are silent (no events).
- **GetEntityLocation** (`/location/get-entity-location`): Returns current location for an entity by type+ID. Reads from presence store; returns null `LocationId` if TTL expired or never reported.
- **ListEntitiesAtLocation** (`/location/list-entities-at-location`): Lists entities at a specific location. Reads from Redis Set, optionally filters by `entityType`, paginates with `offset`/`limit` (capped by `MaxEntitiesPerLocationQuery`, default 100). Hydrates each member from presence store, skipping stale entries. Returns `EntityPresenceEntry` objects with entity details and `reportedAt` timestamps.
- **ClearEntityPosition** (`/location/clear-entity-position`): Explicitly removes an entity's presence. Deletes presence key, removes from location's entity set, publishes `location.entity-departed`. Returns null `PreviousLocationId` if entity had no active presence.

---

## Visual Aid

```
Location Hierarchy (Example)
==============================

  Realm: "Elara"

  CONTINENT_VASTORIA (root, depth=0)
    ├── REGION_NORTHERN_HIGHLANDS (depth=1)
    │    ├── CITY_FROSTHOLD (depth=2)
    │    │    ├── DISTRICT_MARKET (depth=3)
    │    │    │    ├── BUILDING_TAVERN (depth=4)
    │    │    │    │    └── ROOM_CELLAR (depth=5)
    │    │    │    └── BUILDING_SMITHY (depth=4)
    │    │    └── DISTRICT_CASTLE (depth=3)
    │    └── LANDMARK_CRYSTAL_LAKE (depth=2)
    └── REGION_SOUTHERN_PLAINS (depth=1)


SetParent with Circular Reference Check
==========================================

  SetLocationParent(location=REGION_NORTHERN, newParent=CITY_FROSTHOLD)
       │
       ├── IsDescendantOfAsync(CITY_FROSTHOLD, REGION_NORTHERN, ...)
       │    Walk: CITY_FROSTHOLD → parent → REGION_NORTHERN (!)
       │    Result: TRUE (would create cycle)
       │
       └── Return: BadRequest (circular reference detected)


Cascading Depth Update
========================

  RemoveParent(CITY_FROSTHOLD)
       │
       ├── CITY_FROSTHOLD: depth 2 → 0 (becomes root)
       │
       └── UpdateDescendantDepthsAsync:
            ├── DISTRICT_MARKET: depth 3 → 1
            │    ├── BUILDING_TAVERN: depth 4 → 2
            │    │    └── ROOM_CELLAR: depth 5 → 3
            │    └── BUILDING_SMITHY: depth 4 → 2
            └── DISTRICT_CASTLE: depth 3 → 1


Seed Two-Pass Algorithm
=========================

  Input: [
    { code: "CITY_A", realmCode: "ELARA", parentCode: "REGION_X" },
    { code: "REGION_X", realmCode: "ELARA" }
  ]

  Pass 1 (Create without parents):
    ├── Resolve "ELARA" → realmId via IRealmClient
    ├── Create REGION_X (no parent)
    └── Create CITY_A (no parent yet)

  Pass 2 (Set parent relationships):
    └── SetParent(CITY_A, parent=REGION_X)


State Store Layout
===================

  ┌─ location store ─────────────────────────────────────────┐
  │                                                           │
  │  location:{locationId} → LocationModel                    │
  │    ├── RealmId (required, mutable via transfer-realm)      │
  │    ├── Code (uppercase, unique per realm)                 │
  │    ├── Name, Description                                  │
  │    ├── LocationType (CITY, REGION, BUILDING, ROOM, etc.)  │
  │    ├── ParentLocationId (nullable for roots)              │
  │    ├── Depth (0 for roots, auto-calculated)               │
  │    ├── IsDeprecated, DeprecatedAt, DeprecationReason      │
  │    ├── Bounds (nullable BoundingBox3D, AABB in meters)    │
  │    ├── BoundsPrecision (exact/approximate/none)           │
  │    ├── CoordinateMode (inherit/local/portal)              │
  │    └── LocalOrigin (nullable Position3D)                  │
  │                                                           │
  │  code-index:{realmId}:{CODE} → locationId                 │
  │  realm-index:{realmId} → [locationId, ...]                │
  │  parent-index:{realmId}:{parentId} → [childId, ...]       │
  │  root-locations:{realmId} → [locationId, ...]             │
  └───────────────────────────────────────────────────────────┘
```

---

## Stubs & Unimplemented Features

None. All endpoints and the `${location.*}` variable provider are fully implemented.

---

## Potential Extensions

None currently identified.

---

## Known Quirks & Caveats

### Bugs

1. ~~**T4 Violation: Graceful degradation for L1 dependency**~~: **FIXED** (2026-02-08) - Changed `GetService<IContractClient>()` to `GetRequiredService<IContractClient>()` and removed null-check early return. Added `ServiceProvider ?? throw` guard. Removed catch-all exception handlers — only 409 Conflict (idempotent) is caught. Contract (L1) is guaranteed available when Location (L2) runs; failures now crash startup as expected per FOUNDATION TENETS.

### Intentional Quirks

1. **Delete doesn't require deprecation**: Unlike species and relationship types, locations can be hard-deleted without first being deprecated. However, they cannot be deleted if they have child locations.

2. **Realm validation only at creation and transfer**: `CreateLocation` and `TransferLocationToRealm` validate realm existence via `IRealmClient`. Subsequent operations (update, set-parent) do not re-validate the realm.

3. **Seed update doesn't publish events**: When updating existing locations during seed with `updateExisting=true`, no `location.updated` event is published. The update uses direct `SaveAsync` bypassing the normal event publishing path.

4. **ListLocationsByParent returns NotFound for missing parent**: If the parent location doesn't exist, returns NotFound. Other list operations (ListByRealm, ListRoot) return empty lists for missing realms/indexes. Inconsistent behavior.

5. **Undeprecate returns BadRequest not Conflict**: Unlike `DeprecateLocation` which returns Conflict when already deprecated, `UndeprecateLocation` returns BadRequest when not deprecated. Inconsistent error status pattern.

6. **Delete blocks when lib-resource unavailable**: `DeleteLocation` returns `ServiceUnavailable` (503) if `IResourceClient` is not reachable when checking external references. This fail-closed behavior protects referential integrity but means location deletion depends on lib-resource availability.

7. **Delete executes cleanup callbacks**: When external references exist, `DeleteLocation` calls `IResourceClient.ExecuteCleanupAsync` with `CleanupPolicy.ALL_REQUIRED`. This executes CASCADE/DETACH callbacks registered by higher-layer services (L3/L4) before allowing deletion.

8. **TransferLocationToRealm clears parent**: When a location is transferred to a different realm, its parent is cleared and it becomes a root location (depth 0). This avoids cross-realm parent references. Callers (e.g., Realm merge) are responsible for re-parenting descendants after transfer using `SetLocationParent`.

9. **TransferLocationToRealm checks code uniqueness**: Returns 409 Conflict if the target realm already has a location with the same code. The caller must handle this (e.g., rename, skip, or abort the merge for that entity).

10. **Contract clause registration is in OnRunningAsync**: During plugin startup, Location registers the `territory_constraint` clause type with Contract via `OnRunningAsync()` rather than in the constructor. Registration is in `OnRunningAsync` because it requires making an API call (not just storing a reference), and `OnRunningAsync` is the appropriate lifecycle phase for startup API calls. Layer-based plugin loading ensures L1 Contract is already registered when L2 Location's `OnRunningAsync` fires. Uses `GetRequiredService` and fail-fast per FOUNDATION TENETS.

### Design Considerations

1. **Location-bound contracts** ([#274](https://github.com/beyond-immersion/bannou-service/issues/274)): Adding `boundContractIds` to the location model would enable territory-bound agreements with inheritance semantics (child locations inherit parent's effective contracts). This would allow querying "what contracts apply at this location?" by walking the ancestor chain, enabling game-layer territory rule enforcement without Contract (L1) depending on Location (L2). Design questions: should contracts be directly bound to locations or mediated by a separate binding table? What are the inheritance semantics (simple merge vs priority/override)? Is this a Location concern (L2) or a game rules concern (L4)?

2. **Ground containers** ([#164](https://github.com/beyond-immersion/bannou-service/issues/164), [#274](https://github.com/beyond-immersion/bannou-service/issues/274)): Location-owned "ground" containers for items dropped or lost in the game world. Would add a `groundContainerId` and `groundContainerCreationPolicy` (on-demand, explicit, inherit-parent, disabled) to the location model. Cross-cutting with lib-inventory — Inventory provides the container/item mechanics, Location provides the spatial anchor. Design questions: on-demand creation vs explicit seeding, TTL/capacity/cleanup policies per location type, hierarchy-aware drop behavior (walking up the tree to find nearest location with ground container).

---

## Work Tracking

- **2026-02-12**: Issue [#145](https://github.com/beyond-immersion/bannou-service/issues/145) - Implemented `LocationContextProviderFactory` (`IVariableProviderFactory`) providing `${location.*}` namespace to Actor behavior system. Includes `LocationDataCache` (ConcurrentDictionary with configurable TTL) and `LocationContextProvider` resolving zone, name, region, type, depth, realm, nearby POIs, and entity count.
- **2026-02-12**: Issue [#406](https://github.com/beyond-immersion/bannou-service/issues/406) - Added entity presence tracking: 4 new endpoints (report-entity-position, get-entity-location, list-entities-at-location, clear-entity-position), Redis-backed ephemeral storage with TTL, background cleanup worker, arrived/departed events. Prerequisite for #145.
- **2026-02-12**: Issue [#165](https://github.com/beyond-immersion/bannou-service/issues/165) - Added optional spatial coordinates (BoundingBox3D bounds, BoundsPrecision, CoordinateMode, Position3D localOrigin) to locations. Added `/location/query/by-position` endpoint for spatial-to-location lookup. Shared Position3D/BoundingBox3D types added to common-api.yaml.
- **T4 Violation: Graceful degradation for L1 dependency** (Bugs #1): COMPLETED (2026-02-08) - Changed to `GetRequiredService` with fail-fast behavior
