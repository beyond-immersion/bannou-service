# Location Plugin Deep Dive

> **Plugin**: lib-location
> **Schema**: schemas/location-api.yaml
> **Version**: 1.0.0
> **State Store**: location-statestore (MySQL)

---

## Overview

Hierarchical location management for the Arcadia game world. Manages physical places (cities, regions, buildings, rooms, landmarks) within realms as a tree structure with depth tracking. Each location belongs to exactly one realm and optionally has a parent location. Supports deprecation (soft-delete), circular reference prevention during parent reassignment, cascading depth updates, code-based lookups (uppercase-normalized per realm), and bulk seeding with two-pass parent resolution. No caching layer - all reads hit the state store directly.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | MySQL persistence for locations, code indexes, realm indexes, parent indexes |
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

**Store**: `location-statestore` (via `StateStoreDefinitions.Location`) - MySQL backend

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `location:{locationId}` | `LocationModel` | Individual location definition |
| `code-index:{realmId}:{CODE}` | `string` | Code → location ID (unique per realm) |
| `realm-index:{realmId}` | `List<Guid>` | All location IDs in a realm |
| `parent-index:{realmId}:{parentId}` | `List<Guid>` | Child location IDs for a parent |
| `root-locations:{realmId}` | `List<Guid>` | Location IDs with no parent in a realm |

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

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<LocationService>` | Scoped | Structured logging |
| `LocationServiceConfiguration` | Singleton | All 3 config properties |
| `IStateStoreFactory` | Singleton | State store access |
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

1. **Batch location loading**: Replace N+1 `LoadLocationsByIdsAsync` with bulk state store operation for list endpoints.
2. **Spatial coordinates**: Add optional latitude/longitude or x/y/z coordinates for mapping integration.
3. **Redis caching layer**: Add cache in front of MySQL for frequently-accessed locations (realm capitals, major cities).
4. **Soft-delete reference tracking**: Track which services reference a location before allowing hard delete.

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

1. **N+1 query pattern**: `LoadLocationsByIdsAsync` issues one state store call per location ID. List operations for realms with hundreds of locations generate hundreds of individual calls. No bulk-get optimization exists.

2. **No caching layer**: Unlike other services (item, inventory), location has no Redis cache. Every read hits the state store directly. For frequently-accessed locations (realm capitals, major cities), this could be a performance concern.

3. **Root-locations index maintenance**: The `root-locations:{realmId}` index must be updated whenever a location gains or loses a parent. Missing updates could cause roots to be missed in `ListRootLocations`.

4. **Seed realm code resolution is serial**: During seeding, each unique realm code triggers a separate `IRealmClient.GetRealmByCodeAsync` call. Many locations in the same realm still only resolve once (cached in local dict), but multiple realms = serial calls.

5. **Index updates lack optimistic concurrency**: Index operations (realm, parent, root) load list, modify in-memory, then save without ETag or locking. Concurrent location creations could lose index updates in a race condition.

6. **Empty parent index key not cleaned up**: When the last child is removed from a parent index, the key remains with an empty list rather than being deleted. Over time, empty index keys accumulate.

7. **Depth cascade updates descendants sequentially**: `UpdateDescendantDepthsAsync` first collects ALL descendants (up to 20 levels), then updates each one with a separate state store call. A wide tree with hundreds of descendants generates hundreds of sequential writes in a single request.

---

## Work Tracking

*No active work items at this time.*
