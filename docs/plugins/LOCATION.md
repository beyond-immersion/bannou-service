# Location Plugin Deep Dive

> **Plugin**: lib-location
> **Schema**: schemas/location-api.yaml
> **Version**: 1.0.0
> **State Store**: location-statestore (MySQL)

---

## Overview

Hierarchical location management for the Arcadia game world. Manages physical places (cities, regions, buildings, rooms, landmarks) within realms as a tree structure with depth tracking. Each location belongs to exactly one realm and optionally has a parent location. Supports deprecation (soft-delete), circular reference prevention during parent reassignment, cascading depth updates, code-based lookups (uppercase-normalized per realm), and bulk seeding with two-pass parent resolution. Features Redis read-through caching with configurable TTL for frequently-accessed locations.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | MySQL persistence for locations, code indexes, realm indexes, parent indexes |
| lib-state (`IDistributedLockProvider`) | Distributed locks for concurrent index modifications |
| lib-messaging (`IMessageBus`) | Publishing location lifecycle events; error event publishing |
| lib-messaging (`IEventConsumer`) | Event handler registration (partial class pattern, no handlers) |
| lib-realm (`IRealmClient`) | Realm existence validation on creation; realm code resolution during seed |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-character-encounter | Stores `LocationId` as optional encounter context (stores reference but does not call `ILocationClient`) |

**Note**: No services currently call `ILocationClient`. Location references in other services (character-encounter, mapping) are stored as Guid foreign keys without runtime validation. This is intentional - locations are reference data seeded at startup, not runtime-validated entities.

---

## State Storage

**Stores**:
- `location-statestore` (via `StateStoreDefinitions.Location`) - MySQL backend (persistent)
- `location-cache` (via `StateStoreDefinitions.LocationCache`) - Redis backend (cache)
- `location-lock` (via `StateStoreDefinitions.LocationLock`) - Redis backend (distributed locks)

| Key Pattern | Data Type | Store | Purpose |
|-------------|-----------|-------|---------|
| `location:{locationId}` | `LocationModel` | MySQL | Individual location definition (persistent) |
| `location:{locationId}` | `LocationModel` | Redis | Location cache (TTL-based) |
| `code-index:{realmId}:{CODE}` | `string` | MySQL | Code → location ID (unique per realm) |
| `realm-index:{realmId}` | `List<Guid>` | MySQL | All location IDs in a realm |
| `parent-index:{realmId}:{parentId}` | `List<Guid>` | MySQL | Child location IDs for a parent |
| `root-locations:{realmId}` | `List<Guid>` | MySQL | Location IDs with no parent in a realm |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `location.created` | `LocationCreatedEvent` | New location created |
| `location.updated` | `LocationUpdatedEvent` | Location fields changed (includes `ChangedFields`) |
| `location.deleted` | `LocationDeletedEvent` | Location hard-deleted |

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

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<LocationService>` | Scoped | Structured logging |
| `LocationServiceConfiguration` | Singleton | All 5 config properties |
| `IStateStoreFactory` | Singleton | State store access |
| `IDistributedLockProvider` | Singleton | Distributed locks for index concurrency |
| `IMessageBus` | Scoped | Event publishing and error events |
| `IEventConsumer` | Scoped | Event handler registration |
| `IRealmClient` | Scoped | Realm validation |

Service lifetime is **Scoped** (per-request). No background services.

---

## API Endpoints (Implementation Notes)

### Read Operations (9 endpoints)

- **GetLocation** (`/location/get`): Direct lookup by location ID. Returns full location data with parent reference and depth.
- **GetLocationByCode** (`/location/get-by-code`): Code index lookup using `{realmId}:{CODE}` composite key. Codes are unique per realm.
- **ListLocations** (`/location/list`): Loads from realm index, filters by `locationType`, `includeDeprecated`. In-memory pagination (page/pageSize, default 50).
- **ListLocationsByRealm** (`/location/list-by-realm`): Loads all location IDs from realm index, bulk-loads individually (N+1 pattern). Filters by type and deprecation.
- **ListLocationsByParent** (`/location/list-by-parent`): Loads parent's child index. Validates parent exists first (404 if missing). Filters by type and deprecation.
- **ListRootLocations** (`/location/list-root`): Loads `root-locations:{realmId}` index. Returns top-level locations with no parent.
- **GetLocationAncestors** (`/location/get-ancestors`): Walks parent chain iteratively. Safety limit via `MaxAncestorDepth` config (default 20) to prevent infinite loops from corrupted data.
- **GetLocationDescendants** (`/location/get-descendants`): Recursive traversal via `CollectDescendantsAsync`. Safety limit of 20 depth levels. Optional `maxDepth` parameter.
- **LocationExists** (`/location/exists`): Quick existence check. Returns `Exists` boolean and `IsActive` (not deprecated) flag.

### Write Operations (8 endpoints)

- **CreateLocation** (`/location/create`): Validates realm exists via `IRealmClient`. Normalizes code to uppercase. Checks code uniqueness within realm. If parent specified: validates parent exists, checks same realm, sets depth=parent.depth+1. Updates all indexes (code, realm, parent/root). Publishes `location.created`.
- **UpdateLocation** (`/location/update`): Partial update for name, description, locationType, metadata. Tracks `changedFields`. Does not allow parent or code changes (use separate endpoints). Publishes `location.updated`.
- **SetLocationParent** (`/location/set-parent`): Circular reference detection via `IsDescendantOfAsync` (max 20 depth). Validates new parent is in same realm. Updates old parent's child index, new parent's child index, root-locations index. Cascading depth update for all descendants via `UpdateDescendantDepthsAsync`. Publishes update event.
- **RemoveLocationParent** (`/location/remove-parent`): Makes location a root (depth=0). Updates parent index and root-locations index. Cascading depth update for descendants.
- **DeleteLocation** (`/location/delete`): Requires no child locations (Conflict if children exist). Removes from all indexes. Publishes `location.deleted`. Does NOT require deprecation first (unlike species/relationship-type).
- **DeprecateLocation** (`/location/deprecate`): Sets `IsDeprecated=true` with timestamp and reason. Location remains queryable. Publishes update event.
- **UndeprecateLocation** (`/location/undeprecate`): Restores deprecated location. Returns BadRequest if not deprecated.
- **SeedLocations** (`/location/seed`): Two-pass algorithm. Pass 1: Creates all locations without parent relationships, resolves realm codes via `IRealmClient`. Pass 2: Sets parent relationships by resolving parent codes from pass 1 results. Supports `updateExisting`. Returns created/updated/skipped/errors.

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
  │    ├── RealmId (required, immutable)                      │
  │    ├── Code (uppercase, unique per realm)                 │
  │    ├── Name, Description                                  │
  │    ├── LocationType (CITY, REGION, BUILDING, ROOM, etc.)  │
  │    ├── ParentLocationId (nullable for roots)              │
  │    ├── Depth (0 for roots, auto-calculated)               │
  │    └── IsDeprecated, DeprecatedAt, DeprecationReason      │
  │                                                           │
  │  code-index:{realmId}:{CODE} → locationId                 │
  │  realm-index:{realmId} → [locationId, ...]                │
  │  parent-index:{realmId}:{parentId} → [childId, ...]       │
  │  root-locations:{realmId} → [locationId, ...]             │
  └───────────────────────────────────────────────────────────┘
```

---

## Stubs & Unimplemented Features

None. All API endpoints are fully implemented.

---

## Potential Extensions

1. ~~**Batch location loading**~~: **FIXED** (2026-01-31) - `LoadLocationsByIdsAsync` now uses `GetBulkAsync` for O(1) database round-trips instead of N+1. Order preservation from input list is maintained.
2. **Spatial coordinates**: Add optional latitude/longitude or x/y/z coordinates for mapping integration.
<!-- AUDIT:NEEDS_DESIGN:2026-01-31:https://github.com/beyond-immersion/bannou-service/issues/165 -->
3. ~~**Redis caching layer**~~: **FIXED** (2026-01-31) - Added `location-cache` Redis store with read-through caching. All read operations check cache first, write operations invalidate/populate cache. Configurable TTL via `LOCATION_CACHE_TTL_SECONDS` (default 3600s).
4. **Soft-delete reference tracking**: Track which services reference a location before allowing hard delete.
<!-- AUDIT:NEEDS_DESIGN:2026-01-31:https://github.com/beyond-immersion/bannou-service/issues/166 -->

---

## Known Quirks & Caveats

### Bugs

No bugs identified.

### Intentional Quirks

1. **Delete doesn't require deprecation**: Unlike species and relationship-type, locations can be hard-deleted without first being deprecated. However, they cannot be deleted if they have child locations.

2. **Realm validation only at creation**: `CreateLocation` validates realm existence via `IRealmClient`. Subsequent operations (update, set-parent) do not re-validate the realm. A deleted realm's locations remain accessible.

3. **Seed update doesn't publish events**: When updating existing locations during seed with `updateExisting=true`, no `location.updated` event is published. The update uses direct `SaveAsync` bypassing the normal event publishing path.

4. **ListLocationsByParent returns NotFound for missing parent**: If the parent location doesn't exist, returns NotFound. Other list operations (ListByRealm, ListRoot) return empty lists for missing realms/indexes. Inconsistent behavior.

5. **Undeprecate returns BadRequest not Conflict**: Unlike `DeprecateLocation` which returns Conflict when already deprecated, `UndeprecateLocation` returns BadRequest when not deprecated. Inconsistent error status pattern.

6. **Event sentinel values for nullable fields**: Events use `Guid.Empty` for null parent, `default` for null deprecation date, and `new object()` for null metadata. Schema should define these as optional instead of using sentinel values.

### Design Considerations

1. ~~**N+1 query pattern**~~: **FIXED** (2026-01-31) - `LoadLocationsByIdsAsync` now uses `GetBulkAsync` for single-call bulk retrieval.

2. ~~**No caching layer**~~: **FIXED** (2026-01-31) - Added Redis caching with read-through pattern matching lib-item.

3. ~~**Root-locations index maintenance**~~: **VERIFIED** (2026-01-31) - The `root-locations:{realmId}` index IS correctly maintained in all four scenarios: CreateLocation (adds to roots if no parent), SetLocationParent (removes from roots when going from root→child), RemoveLocationParent (adds to roots), DeleteLocation (removes from roots). No gap exists.

4. ~~**Seed realm code resolution is serial**~~: **VERIFIED** (2026-01-31) - Each unique realm code triggers one `IRealmClient.GetRealmByCodeAsync` call serially, but a local `realmCodeToId` dictionary caches results so each realm is only fetched once per seed operation. Seeding 100 locations in 3 realms = 3 realm lookups (not 100). This is optimized for the common case; parallelizing for edge cases (10+ realms) adds complexity without meaningful benefit.

5. ~~**Index updates lack optimistic concurrency**~~: **FIXED** (2026-01-31) - All six index helper methods (`AddToRealmIndexAsync`, `RemoveFromRealmIndexAsync`, `AddToParentIndexAsync`, `RemoveFromParentIndexAsync`, `AddToRootLocationsAsync`, `RemoveFromRootLocationsAsync`) now use `IDistributedLockProvider` with per-key locking via `StateStoreDefinitions.LocationLock`. Lock timeout is configurable via `LOCATION_INDEX_LOCK_TIMEOUT_SECONDS` (default 5s). On lock failure, logs warning and returns without modifying the index (follows lib-inventory pattern).

6. ~~**Empty parent index key not cleaned up**~~: **FIXED** (2026-01-31) - `RemoveFromParentIndexAsync` now deletes the parent index key when the last child is removed instead of saving an empty list.

7. ~~**Depth cascade updates descendants sequentially**~~: **RESOLVED** (2026-01-31) - Issue #168 addressed by adding `SaveBulkAsync` to `IStateStore`, enabling batch writes for descendant depth updates. The Location service can now use bulk save for efficient cascade operations instead of sequential writes.

---

## Work Tracking

### Completed
- **2026-01-31**: Batch location loading - Replaced N+1 `LoadLocationsByIdsAsync` with `GetBulkAsync` for O(1) database round-trips.
- **2026-01-31**: Redis caching layer - Added `location-cache` store with read-through caching for all read operations, cache invalidation/population on writes.
- **2026-01-31**: Root-locations index maintenance - Verified implementation is correct; index is properly maintained in all four scenarios (create, set-parent, remove-parent, delete).
- **2026-01-31**: Seed realm code resolution - Verified implementation is correct; local dictionary caching ensures each realm code is only fetched once per seed operation.
- **2026-01-31**: Index concurrency protection - Added distributed locking to all six index helper methods using `IDistributedLockProvider` via new `location-lock` state store. Configurable timeout via `IndexLockTimeoutSeconds` config property.
- **2026-01-31**: Empty parent index cleanup - `RemoveFromParentIndexAsync` now deletes the key when the last child is removed instead of saving an empty list.
- **2026-01-31**: Bulk state store operations for cascade - Added `SaveBulkAsync`, `ExistsBulkAsync`, `DeleteBulkAsync` to `IStateStore` interface, enabling efficient batch writes for depth cascade updates. See [#168](https://github.com/beyond-immersion/bannou-service/issues/168).
