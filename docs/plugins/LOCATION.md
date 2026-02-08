# Location Plugin Deep Dive

> **Plugin**: lib-location
> **Schema**: schemas/location-api.yaml
> **Version**: 1.0.0
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
| lib-messaging (`IEventConsumer`) | Event handler registration (partial class pattern, no handlers) |
| lib-realm (`IRealmClient`) | Realm existence validation on creation; realm code resolution during seed |
| lib-resource (`IResourceClient`) | Reference tracking checks before deletion; cleanup coordination |
| lib-contract (`IContractClient`) | Territory clause type registration during plugin startup (graceful degradation if unavailable) |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-character-encounter | Stores `LocationId` as optional encounter context (stores reference but does not call `ILocationClient`) |

**Note**: No services currently call `ILocationClient` directly. Location references in other services (character-encounter, mapping) are stored as Guid foreign keys without runtime validation. This is intentional - locations are reference data seeded at startup, not runtime-validated entities.

**Contract Integration**: Location registers a `territory_constraint` clause type with Contract during plugin startup. Contract service will call `/location/validate-territory` via `IServiceNavigator` when evaluating territory constraint clauses. This enables Contract to validate territorial boundaries without a direct dependency on Location (SERVICE_HIERARCHY compliant: L2 Location registers with L1 Contract).

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
| `IResourceClient` | Scoped | Reference tracking checks before deletion |

Service lifetime is **Scoped** (per-request). No background services.

---

## API Endpoints (Implementation Notes)

### Read Operations (10 endpoints)

- **GetLocation** (`/location/get`): Direct lookup by location ID. Returns full location data with parent reference and depth.
- **GetLocationByCode** (`/location/get-by-code`): Code index lookup using `{realmId}:{CODE}` composite key. Codes are unique per realm.
- **ListLocations** (`/location/list`): Loads from realm index, filters by `locationType`, `includeDeprecated`. In-memory pagination (page/pageSize, default 20).
- **ListLocationsByRealm** (`/location/list-by-realm`): Loads all location IDs from realm index, bulk-loads via `GetBulkAsync`. Filters by type and deprecation.
- **ListLocationsByParent** (`/location/list-by-parent`): Loads parent's child index. Validates parent exists first (404 if missing). Filters by type and deprecation.
- **ListRootLocations** (`/location/list-root`): Loads `root-locations:{realmId}` index. Returns top-level locations with no parent.
- **GetLocationAncestors** (`/location/get-ancestors`): Walks parent chain iteratively. Safety limit via `MaxAncestorDepth` config (default 20) to prevent infinite loops from corrupted data.
- **GetLocationDescendants** (`/location/get-descendants`): Recursive traversal via `CollectDescendantsAsync`. Safety limit of 20 depth levels. Optional `maxDepth` parameter.
- **ValidateTerritory** (`/location/validate-territory`): Territory constraint checking designed for Contract service's clause handler system. Builds location + ancestor hierarchy set, checks for overlap with territory location IDs. Supports two modes: `exclusive` (location must NOT overlap territory) and `inclusive` (location MUST be within territory). The `territory_constraint` clause type is registered with Contract during plugin startup via `LocationServicePlugin.OnRunningAsync()`.
- **LocationExists** (`/location/exists`): Quick existence check. Returns `Exists` boolean and `IsActive` (not deprecated) flag.

### Write Operations (8 endpoints)

- **CreateLocation** (`/location/create`): Validates realm exists via `IRealmClient`. Normalizes code to uppercase. Checks code uniqueness within realm. If parent specified: validates parent exists, checks same realm, sets depth=parent.depth+1. Updates all indexes (code, realm, parent/root). Publishes `location.created`.
- **UpdateLocation** (`/location/update`): Partial update for name, description, locationType, metadata. Tracks `changedFields`. Does not allow parent or code changes (use separate endpoints). Publishes `location.updated`.
- **SetLocationParent** (`/location/set-parent`): Circular reference detection via `IsDescendantOfAsync` (max 20 depth). Validates new parent is in same realm. Updates old parent's child index, new parent's child index, root-locations index. Cascading depth update for all descendants via `UpdateDescendantDepthsAsync`. Publishes update event.
- **RemoveLocationParent** (`/location/remove-parent`): Makes location a root (depth=0). Updates parent index and root-locations index. Cascading depth update for descendants.
- **DeleteLocation** (`/location/delete`): Requires no child locations (Conflict if children exist). Checks external references via `IResourceClient` - if references exist, executes cleanup callbacks before proceeding (returns Conflict if cleanup fails). Removes from all indexes. Publishes `location.deleted`. Does NOT require deprecation first (unlike species/relationship types).
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

None. All features are fully implemented.

---

## Potential Extensions

1. **Spatial coordinates**: Add optional latitude/longitude or x/y/z coordinates for mapping integration.
<!-- AUDIT:NEEDS_DESIGN:2026-01-31:https://github.com/beyond-immersion/bannou-service/issues/165 -->

---

## Known Quirks & Caveats

### Bugs

1. **T4 Violation: Graceful degradation for L1 dependency**: `LocationServicePlugin.OnRunningAsync()` uses `GetService<IContractClient>()` with a null check that returns early if Contract is unavailable. Per SERVICE_HIERARCHY and T4, Contract (L1) is guaranteed available when Location (L2) runs. The null check should throw `InvalidOperationException` instead of silently returning. (Note: The code is in `OnRunningAsync` rather than the constructor because plugin load order isn't currently layer-based - that's a separate infrastructure improvement tracked elsewhere.)

### Intentional Quirks

1. **Delete doesn't require deprecation**: Unlike species and relationship types, locations can be hard-deleted without first being deprecated. However, they cannot be deleted if they have child locations.

2. **Realm validation only at creation**: `CreateLocation` validates realm existence via `IRealmClient`. Subsequent operations (update, set-parent) do not re-validate the realm. A deleted realm's locations remain accessible.

3. **Seed update doesn't publish events**: When updating existing locations during seed with `updateExisting=true`, no `location.updated` event is published. The update uses direct `SaveAsync` bypassing the normal event publishing path.

4. **ListLocationsByParent returns NotFound for missing parent**: If the parent location doesn't exist, returns NotFound. Other list operations (ListByRealm, ListRoot) return empty lists for missing realms/indexes. Inconsistent behavior.

5. **Undeprecate returns BadRequest not Conflict**: Unlike `DeprecateLocation` which returns Conflict when already deprecated, `UndeprecateLocation` returns BadRequest when not deprecated. Inconsistent error status pattern.

6. **Delete blocks when lib-resource unavailable**: `DeleteLocation` returns `ServiceUnavailable` (503) if `IResourceClient` is not reachable when checking external references. This fail-closed behavior protects referential integrity but means location deletion depends on lib-resource availability.

7. **Delete executes cleanup callbacks**: When external references exist, `DeleteLocation` calls `IResourceClient.ExecuteCleanupAsync` with `CleanupPolicy.ALL_REQUIRED`. This executes CASCADE/DETACH callbacks registered by higher-layer services (L3/L4) before allowing deletion.

8. **Contract clause registration is in OnRunningAsync**: During plugin startup, Location registers the `territory_constraint` clause type with Contract via `OnRunningAsync()` rather than in the constructor. This is because plugin load order isn't currently layer-based, so cross-plugin DI isn't safe in constructors. The graceful degradation (null check that returns early) is a separate bug - see Bugs section.

### Design Considerations

No design considerations currently pending.

---

## Work Tracking

No active work tracking items.
