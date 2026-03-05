# Location Plugin Deep Dive

> **Plugin**: lib-location
> **Schema**: schemas/location-api.yaml
> **Version**: 1.0.0
> **Layer**: GameFoundation
> **State Store**: location-statestore (MySQL)
> **Implementation Map**: [docs/maps/LOCATION.md](../maps/LOCATION.md)

---

## Overview

Hierarchical location management (L2 GameFoundation) for the Arcadia game world. Manages physical places (cities, regions, buildings, rooms, landmarks) within realms as a tree structure with depth tracking. Each location belongs to exactly one realm and optionally has a parent location. Supports deprecation, circular reference prevention, cascading depth updates, code-based lookups, and bulk seeding with two-pass parent resolution.

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-realm | Calls `ListRootLocationsAsync`, `GetLocationDescendantsAsync`, `TransferLocationToRealmAsync`, `SetLocationParentAsync` via `ILocationClient` during realm merge |
| lib-faction | Calls `LocationExistsAsync` via `ILocationClient` for territory claim location validation |
| lib-transit | Calls `LocationExistsAsync`, `GetLocationAsync`, `ReportEntityPositionAsync`, `ClearEntityPositionAsync` via `ILocationClient` for connection/journey location validation and entity position reporting during travel |
| lib-character-encounter | Stores `LocationId` as optional encounter context (stores reference but does not call `ILocationClient`) |

**Contract Integration**: Location registers a `territory_constraint` clause type with Contract during plugin startup. Contract service will call `/location/validate-territory` via `IServiceNavigator` when evaluating territory constraint clauses. This enables Contract to validate territorial boundaries without a direct dependency on Location (SERVICE_HIERARCHY compliant: L2 Location registers with L1 Contract).

---

## Type Field Classification

| Field | Category | Type | Rationale |
|-------|----------|------|-----------|
| `LocationType` | C (System State) | Service-specific enum (`CONTINENT`, `REGION`, `CITY`, `DISTRICT`, `BUILDING`, `ROOM`, `LANDMARK`, `OTHER`) | Finite set of structural categories defining the role of a location in the containment hierarchy. System-owned; adding new location types requires schema changes because they affect hierarchy semantics |
| `BoundsPrecision` | C (System State) | Service-specific enum (`exact`, `approximate`, `none`) | Finite set of system-owned precision levels for spatial bounds data quality |
| `CoordinateMode` | C (System State) | Service-specific enum (`inherit`, `local`, `portal`) | Finite set of system-owned modes describing how a location's coordinate system relates to its parent |
| `TerritoryMode` | C (System State) | Service-specific enum (`exclusive`, `inclusive`) | Finite set of two system-owned validation modes for territory constraint checking |
| `entityType` (entity presence) | B (Content Code) | Opaque string | Entity type for presence tracking (e.g., "character", "actor", "npc", "player"). Described as opaque string in the schema; game-configurable to support arbitrary entity type classifications without schema changes |

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

None identified.

---

## Known Quirks & Caveats

### Bugs

None currently identified.

### Intentional Quirks

1. **Realm validation only at creation and transfer**: `CreateLocation` and `TransferLocationToRealm` validate realm existence via `IRealmClient`. Subsequent operations (update, set-parent) do not re-validate the realm.

2. **ListLocationsByParent returns NotFound for missing parent**: If the parent location doesn't exist, returns NotFound. Other list operations (ListByRealm, ListRoot) return empty lists for missing realms/indexes. This is structurally necessary, not just a validation choice: `ListLocationsByParentAsync` must load the parent entity to obtain its `RealmId` for constructing the `parent-index:{realmId}:{parentId}` key. Without the parent, the index key cannot be built. In contrast, `ListLocationsByRealmAsync` and `ListRootLocationsAsync` receive the `RealmId` directly in the request and build their index keys without entity lookups.

3. **Delete blocks when lib-resource unavailable**: `DeleteLocation` returns `ServiceUnavailable` (503) if `IResourceClient` is not reachable when checking external references. This fail-closed behavior protects referential integrity but means location deletion depends on lib-resource availability.

4. **Delete executes cleanup callbacks**: When external references exist, `DeleteLocation` calls `IResourceClient.ExecuteCleanupAsync` with `CleanupPolicy.ALL_REQUIRED`. This executes CASCADE/DETACH callbacks registered by higher-layer services (L3/L4) before allowing deletion.

5. **TransferLocationToRealm clears parent**: When a location is transferred to a different realm, its parent is cleared and it becomes a root location (depth 0). This avoids cross-realm parent references. Callers (e.g., Realm merge) are responsible for re-parenting descendants after transfer using `SetLocationParent`.

6. **TransferLocationToRealm checks code uniqueness**: Returns 409 Conflict if the target realm already has a location with the same code. The caller must handle this (e.g., rename, skip, or abort the merge for that entity).

7. **Contract clause registration is in OnRunningAsync**: During plugin startup, Location registers the `territory_constraint` clause type with Contract via `OnRunningAsync()` rather than in the constructor. Registration is in `OnRunningAsync` because it requires making an API call (not just storing a reference), and `OnRunningAsync` is the appropriate lifecycle phase for startup API calls. Layer-based plugin loading ensures L1 Contract is already registered when L2 Location's `OnRunningAsync` fires. Uses `GetRequiredService` and fail-fast per FOUNDATION TENETS.

### Design Considerations

1. **Location-bound contracts** ([#274](https://github.com/beyond-immersion/bannou-service/issues/274)): Adding `boundContractIds` to the location model would enable territory-bound agreements with inheritance semantics (child locations inherit parent's effective contracts). This would allow querying "what contracts apply at this location?" by walking the ancestor chain, enabling game-layer territory rule enforcement without Contract (L1) depending on Location (L2). Design questions: should contracts be directly bound to locations or mediated by a separate binding table? What are the inheritance semantics (simple merge vs priority/override)? Is this a Location concern (L2) or a game rules concern (L4)?
<!-- AUDIT:NEEDS_DESIGN:2026-02-25:https://github.com/beyond-immersion/bannou-service/issues/274 -->

2. **Ground containers** ([#164](https://github.com/beyond-immersion/bannou-service/issues/164), [#274](https://github.com/beyond-immersion/bannou-service/issues/274)): Location-owned "ground" containers for items dropped or lost in the game world. Would add a `groundContainerId` and `groundContainerCreationPolicy` (on-demand, explicit, inherit-parent, disabled) to the location model. Cross-cutting with lib-inventory — Inventory provides the container/item mechanics, Location provides the spatial anchor. Design questions: on-demand creation vs explicit seeding, TTL/capacity/cleanup policies per location type, hierarchy-aware drop behavior (walking up the tree to find nearest location with ground container).
<!-- AUDIT:NEEDS_DESIGN:2026-02-25:https://github.com/beyond-immersion/bannou-service/issues/164 -->

3. **LocationExistsResponse realm enrichment** ([#424](https://github.com/beyond-immersion/bannou-service/issues/424)): Faction's `ClaimTerritoryAsync` currently validates location existence via `LocationExistsAsync` but cannot enforce same-realm validation because `LocationExistsResponse` only returns `exists` and `isActive` — no `realmId`. Adding `realmId` to `LocationExistsResponse` would enable all consumers to perform realm-scoped validation without needing the heavier `GetLocationAsync` call. Pending design decision on #424.
<!-- AUDIT:NEEDS_DESIGN:2026-03-04:https://github.com/beyond-immersion/bannou-service/issues/424 -->

---

## Work Tracking

- **2026-03-04**: Added compression/archival support via `x-compression-callback`. Location is now a first-class resource-tracked entity with `GetLocationCompressData` endpoint providing base location data, parent context (one level up), and children summary for the content flywheel.
