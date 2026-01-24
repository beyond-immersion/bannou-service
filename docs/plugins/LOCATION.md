# Location Plugin Deep Dive

> **Plugin**: lib-location
> **Schema**: schemas/location-api.yaml
> **Version**: 1.0.0
> **State Store**: location (Redis/MySQL)

---

## Overview

Hierarchical location management for the Arcadia game world. Manages physical places (cities, regions, buildings, rooms, landmarks) within realms as a tree structure with depth tracking. Each location belongs to exactly one realm and optionally has a parent location. Supports deprecation (soft-delete), circular reference prevention during parent reassignment, cascading depth updates, code-based lookups (uppercase-normalized per realm), and bulk seeding with two-pass parent resolution. No caching layer - all reads hit the state store directly.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Redis/MySQL persistence for locations, code indexes, realm indexes, parent indexes |
| lib-messaging (`IMessageBus`) | Publishing location lifecycle events; error event publishing |
| lib-messaging (`IEventConsumer`) | Event handler registration (partial class pattern, no handlers) |
| lib-realm (`IRealmClient`) | Realm existence validation on creation; realm code resolution during seed |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-character | Uses `ILocationClient` for location validation in character placement |
| lib-mapping | References locations for spatial data anchoring |
| lib-character-encounter | Queries locations for encounter context |

---

## State Storage

**Store**: `location` (via `StateStoreDefinitions.Location`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `location:{locationId}` | `LocationModel` | Individual location definition |
| `code-index:{realmId}:{CODE}` | `string` | Code → location ID (unique per realm) |
| `realm-index:{realmId}` | `List<string>` | All location IDs in a realm |
| `parent-index:{realmId}:{parentId}` | `List<string>` | Child location IDs for a parent |
| `root-locations:{realmId}` | `List<string>` | Location IDs with no parent in a realm |

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
| (none) | — | — | Configuration class injected but unused (dead config) |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<LocationService>` | Scoped | Structured logging |
| `LocationServiceConfiguration` | Singleton | Configuration (currently unused) |
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
- **GetLocationAncestors** (`/location/get-ancestors`): Walks parent chain iteratively. Safety limit of 10 iterations to prevent infinite loops from corrupted data.
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

1. **No location type validation**: `LocationType` is stored as a string. Any value is accepted on creation/update. The enum (`LocationType`) is defined in schema but not validated at runtime against the model.
2. **Configuration unused**: `LocationServiceConfiguration` is injected but never referenced. Hardcoded depth limits and pagination defaults could be configurable.

---

## Potential Extensions

1. **Configurable depth limits**: Move hardcoded depth limits (10, 20) to configuration schema.
2. **Batch location loading**: Replace N+1 `LoadLocationsByIdsAsync` with bulk state store operation for list endpoints.
3. **Location type validation**: Validate `LocationType` against the defined enum on creation/update.
4. **Spatial coordinates**: Add optional latitude/longitude or x/y/z coordinates for mapping integration.

---

## Known Quirks & Caveats

### Intentional Quirks (Documented Behavior)

1. **Code uniqueness is per-realm**: The same code (e.g., "TAVERN") can exist in multiple realms. The code index key includes realm ID: `code-index:{realmId}:{CODE}`.

2. **Delete doesn't require deprecation**: Unlike species and relationship-type, locations can be hard-deleted without first being deprecated. However, they cannot be deleted if they have child locations.

3. **Circular reference prevention**: `IsDescendantOfAsync` walks up the parent chain (max 20 levels) to detect if the proposed parent is actually a descendant of the current location. Prevents creating cycles.

4. **Cascading depth updates**: When a location's parent changes, `UpdateDescendantDepthsAsync` recursively updates all descendant depths (max 20 levels deep). This happens synchronously within the request.

5. **Realm validation only at creation**: `CreateLocation` validates realm existence via `IRealmClient`. Subsequent operations (update, set-parent) do not re-validate the realm. A deleted realm's locations remain accessible.

6. **Same-realm enforcement for parents**: `SetLocationParent` validates that the new parent is in the same realm as the location. Cross-realm parent-child relationships are forbidden.

### Design Considerations (Requires Planning)

1. **N+1 query pattern**: `LoadLocationsByIdsAsync` issues one state store call per location ID. List operations for realms with hundreds of locations generate hundreds of individual calls. No bulk-get optimization exists.

2. **LocationType stored as string**: The internal `LocationModel` stores `LocationType` as a string, not the generated enum. Response mapping uses `Enum.Parse<LocationType>()` which could throw on invalid stored values.

3. **No caching layer**: Unlike other services (item, inventory), location has no Redis cache. Every read hits the state store directly. For frequently-accessed locations (realm capitals, major cities), this could be a performance concern.

4. **Root-locations index maintenance**: The `root-locations:{realmId}` index must be updated whenever a location gains or loses a parent. Missing updates could cause roots to be missed in `ListRootLocations`.

5. **Seed realm code resolution is serial**: During seeding, each unique realm code triggers a separate `IRealmClient.GetRealmByCodeAsync` call. Many locations in the same realm still only resolve once (cached in local dict), but multiple realms = serial calls.

7. **Seed update doesn't publish events**: Lines 1084-1101 - when updating existing locations during seed with `updateExisting=true`, no `location.updated` event is published. The update uses direct `SaveAsync` bypassing the normal event publishing path.

8. **SetParent doesn't check if parent unchanged**: Lines 737-810 - `SetLocationParentAsync` doesn't check if the new parent is the same as the current parent. Calling SetParent with the location's existing parent will still update indexes and publish an update event.

9. **Ancestor walk breaks silently on missing parent**: Lines 425-428 - if a parent in the ancestor chain doesn't exist (data corruption), the walk simply stops and returns accumulated ancestors. No warning logged for the orphaned data.

10. **Index updates lack optimistic concurrency**: Lines 1210-1219, 1231-1239, 1252-1260 - index operations (realm, parent, root) load list, modify in-memory, then save without ETag or locking. Concurrent location creations could lose index updates in a race condition.

11. **Empty parent index key not cleaned up**: Lines 1242-1249 - when the last child is removed from a parent index, the key remains with an empty list rather than being deleted. Over time, empty index keys accumulate.

12. **Seed realm lookup doesn't cache NotFound**: Lines 1054-1059 - if a realm code lookup fails, it's not cached in `realmCodeToId`. Subsequent seed locations with the same invalid realm code will retry the API call (and fail again), logging multiple errors.

13. **Depth cascade updates descendants sequentially**: Lines 1303-1315 - `UpdateDescendantDepthsAsync` first collects ALL descendants (up to 20 levels), then updates each one with a separate state store call. A wide tree with hundreds of descendants generates hundreds of sequential writes in a single request.

14. **ListLocationsByParent returns NotFound for missing parent**: Lines 264-267 - if the parent location doesn't exist, returns NotFound. Other list operations (ListByRealm, ListRoot) return empty lists for missing realms/indexes. Inconsistent behavior.

15. **Undeprecate returns BadRequest not Conflict**: Line 1005 - unlike `DeprecateLocation` which returns Conflict when already deprecated, `UndeprecateLocation` returns BadRequest when not deprecated. Inconsistent error status pattern.
