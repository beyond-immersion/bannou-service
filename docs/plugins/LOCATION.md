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

## Tenet Violations (Fix Immediately)

### IMPLEMENTATION TENETS - T25: Internal Model Type Safety

**File**: `plugins/lib-location/LocationService.cs`
**Lines**: 1451 (LocationModel class definition)

**Violation**: The internal `LocationModel` POCO stores `LocationType` as `string` instead of the generated `LocationType` enum type. The generated enum exists in `bannou-service/Generated/Models/LocationModels.cs` and is used by the response model (`LocationResponse.LocationType` is typed as `LocationType` enum). The internal model should use the same enum.

**Impact**: Throughout the service, enum-to-string conversions pollute business logic:
- Line 148: `l.LocationType == body.LocationType.Value.ToString()` (string comparison instead of enum equality)
- Line 217: same pattern repeated
- Line 295: same pattern repeated
- Line 364: same pattern repeated
- Line 485: same pattern repeated
- Line 623: `LocationType = body.LocationType.ToString()` (`.ToString()` populating internal model)
- Line 701-703: `body.LocationType.Value.ToString() != model.LocationType` then `model.LocationType = body.LocationType.Value.ToString()`
- Line 1107: `existingModel.LocationType = seedLocation.LocationType.ToString()` in seed
- Line 1340: `Enum.Parse<LocationType>(model.LocationType)` in `MapToResponse` (fragile runtime parsing in business logic)

**Fix**: Change `LocationModel.LocationType` property from `string` to `LocationType` enum. Remove all `.ToString()` conversions when assigning, use direct enum equality for comparisons, and remove the `Enum.Parse` call in `MapToResponse`. Convert to string only at the event publishing boundary (lines 1363, 1388, 1414) where the event model requires `string`.

---

### FOUNDATION TENETS - T6: Service Implementation Pattern (Missing Null Checks)

**File**: `plugins/lib-location/LocationService.cs`
**Lines**: 31-47 (constructor)

**Violation**: Constructor parameters are assigned directly without null checks. Per T6, all injected dependencies must be validated with `?? throw new ArgumentNullException(nameof(...))` or `ArgumentNullException.ThrowIfNull(...)`. Currently:
- `_stateStoreFactory = stateStoreFactory;` (no null check)
- `_messageBus = messageBus;` (no null check)
- `_logger = logger;` (no null check)
- `_configuration = configuration;` (no null check)
- `_realmClient = realmClient;` (no null check)
- `eventConsumer` is passed to `RegisterEventConsumers` without prior null validation via `ArgumentNullException.ThrowIfNull`

**Fix**: Add null checks per the T6 pattern:
```csharp
_stateStoreFactory = stateStoreFactory ?? throw new ArgumentNullException(nameof(stateStoreFactory));
_messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
_logger = logger ?? throw new ArgumentNullException(nameof(logger));
_configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
_realmClient = realmClient ?? throw new ArgumentNullException(nameof(realmClient));
ArgumentNullException.ThrowIfNull(eventConsumer, nameof(eventConsumer));
```

---

### IMPLEMENTATION TENETS - T21: Configuration-First (Hardcoded Tunables)

**File**: `plugins/lib-location/LocationService.cs`

**Violation 1 - Hardcoded depth/traversal limits**:
- Line 417: `var maxDepth = 20;` (ancestor walk safety limit)
- Line 471: `body.MaxDepth ?? 10` (default max depth for descendants)
- Line 1313: `0, 20` in `IsDescendantOfAsync` (circular reference check depth)
- Line 1320: `0, 20` in `UpdateDescendantDepthsAsync` (depth cascade limit)

These are all tunables that should be configuration properties (e.g., `MaxAncestorDepth`, `DefaultDescendantDepth`, `MaxTreeTraversalDepth`).

**Violation 2 - Hardcoded pagination defaults**:
The schema likely defines default values for `Page` and `PageSize` on the request models, but the deep dive notes "default 50" for page size which is a schema-level default. This is acceptable if the schema handles it. However, the `GetLocationAncestorsAsync` method (line 442) sets `PageSize = ancestors.Count` which is not a configurable value per se but bypasses pagination entirely.

**Fix**: Define these depth/traversal limits in `schemas/location-configuration.yaml` as configuration properties with explicit `env:` keys (e.g., `LOCATION_MAX_TREE_TRAVERSAL_DEPTH`, `LOCATION_DEFAULT_DESCENDANT_DEPTH`). Reference them via `_configuration` in the service.

---

### IMPLEMENTATION TENETS - T9: Multi-Instance Safety (No Distributed Locks on Index Mutations)

**File**: `plugins/lib-location/LocationService.cs`
**Lines**: 1224-1285 (index helper methods)

**Violation**: All index mutation methods (`AddToRealmIndexAsync`, `RemoveFromRealmIndexAsync`, `AddToParentIndexAsync`, `RemoveFromParentIndexAsync`, `AddToRootLocationsAsync`, `RemoveFromRootLocationsAsync`) follow a read-modify-write pattern without any distributed locking or optimistic concurrency (ETags). Two concurrent `CreateLocation` calls for the same realm could both read the realm index, each add their ID, and the second write would overwrite the first's addition, losing an index entry.

**Fix**: Either use `IDistributedLockProvider` around each index mutation, or use `GetWithETagAsync`/`TrySaveAsync` with retry loops for optimistic concurrency on index operations.

---

### IMPLEMENTATION TENETS - T9: Multi-Instance Safety (Dictionary in Seed)

**File**: `plugins/lib-location/LocationService.cs`
**Lines**: 1055, 1056, 1077

**Violation**: `SeedLocationsAsync` uses `Dictionary<string, string>` and `HashSet<string>` (not `ConcurrentDictionary`/concurrent collections). While these are method-local variables (not instance-level state), T9 specifies `ConcurrentDictionary` for local caches. However, since these are truly local to a single method invocation and not shared across threads, this is a **minor/informational** finding -- the Dictionary is method-scoped and not accessible to other threads within that invocation.

**Severity**: Low (method-local, not shared state). No fix strictly required, but noting for completeness.

---

### IMPLEMENTATION TENETS - T7: Error Handling (Missing ApiException Catch)

**File**: `plugins/lib-location/LocationService.cs`
**Lines**: All endpoint methods (62-87, 90-124, 127-182, etc.)

**Violation**: Every endpoint method catches only `Exception` but never catches `ApiException` specifically. Per T7, the pattern requires catching `ApiException` first (for expected API errors from downstream service calls like `IRealmClient`) and logging at Warning level, then catching generic `Exception` for unexpected failures. The `CreateLocationAsync` (line 566) and `SeedLocationsAsync` (line 1042) methods call `_realmClient` which can throw `ApiException`, but these are not caught distinctly.

**Fix**: Add `catch (ApiException ex)` blocks before the generic `catch (Exception ex)` in methods that call service clients (`CreateLocationAsync`, `SeedLocationsAsync`). Log at Warning level and propagate the status code:
```csharp
catch (ApiException ex)
{
    _logger.LogWarning(ex, "Service call failed with status {Status}", ex.StatusCode);
    return ((StatusCodes)ex.StatusCode, null);
}
```

---

### FOUNDATION TENETS - T5: Event-Driven Architecture (Seed Update Missing Events)

**File**: `plugins/lib-location/LocationService.cs`
**Lines**: 1098-1112

**Violation**: When `updateExisting` is true during seeding, the existing location is updated via direct `SaveAsync` but no `location.updated` event is published. All meaningful state changes must publish events per T5. The normal `UpdateLocationAsync` method publishes events, but the seed bypass does not.

**Fix**: After updating the existing location model during seed, call `PublishLocationUpdatedEventAsync` with the changed fields, or refactor to use `UpdateLocationAsync` for the update path.

---

### QUALITY TENETS - T19: XML Documentation (Missing on Internal Members)

**File**: `plugins/lib-location/LocationService.cs`

**Violation**: Multiple private/internal members lack XML documentation:
- Lines 1209-1222: `LoadLocationsByIdsAsync` - no XML summary
- Lines 1224-1233: `AddToRealmIndexAsync` - no XML summary
- Lines 1235-1243: `RemoveFromRealmIndexAsync` - no XML summary
- Lines 1245-1253: `AddToParentIndexAsync` - no XML summary
- Lines 1256-1264: `RemoveFromParentIndexAsync` - no XML summary
- Lines 1266-1275: `AddToRootLocationsAsync` - no XML summary
- Lines 1277-1285: `RemoveFromRootLocationsAsync` - no XML summary
- Lines 1287-1308: `CollectDescendantsAsync` - no XML summary
- Lines 1310-1315: `IsDescendantOfAsync` - no XML summary
- Lines 1317-1329: `UpdateDescendantDepthsAsync` - no XML summary
- Lines 1331-1350: `MapToResponse` - no XML summary
- Lines 1352-1375: `PublishLocationCreatedEventAsync` - no XML summary
- Lines 1377-1401: `PublishLocationUpdatedEventAsync` - no XML summary
- Lines 1403-1424: `PublishLocationDeletedEventAsync` - no XML summary
- Lines 1444-1460: `LocationModel` class and all its properties - no XML summary

T19 requires XML documentation on all public classes/methods. While these are private/internal, the `LocationModel` class is `internal` with `InternalsVisibleTo` for test projects, making it effectively part of the public API surface for tests.

**Fix**: Add `<summary>` tags to all private helper methods and the `LocationModel` class with its properties.

---

### QUALITY TENETS - T10: Logging Standards (Information Level for Non-Significant Events)

**File**: `plugins/lib-location/LocationService.cs`
**Line**: 1436

**Violation**: `RegisterServicePermissionsAsync` logs at Information level: `"Registering Location service permissions..."`. This is a routine startup operation, not a significant business state change. Per T10, operation entry points should use Debug level unless they represent significant state changes.

**Fix**: Change to `_logger.LogDebug("Registering Location service permissions")` (also remove trailing ellipsis per plain text convention).

---

### IMPLEMENTATION TENETS - T21: Configuration-First (Dead Configuration Injection)

**File**: `plugins/lib-location/LocationService.cs`
**Line**: 22, 42

**Violation**: `LocationServiceConfiguration` is injected as `_configuration` (line 22) and assigned in the constructor (line 42), but it is never referenced anywhere in the service code. Per T21, "Every defined config property MUST be referenced in service code." The configuration class has no custom properties, but the field itself is dead code.

**Severity**: Low -- the configuration schema has no custom properties so technically nothing is "dead config". However, the injected field `_configuration` is unused, which is dead code. Either remove the field/parameter or add configuration properties for the hardcoded tunables (which addresses the T21 hardcoded tunables violation above).

**Fix**: Either remove the `_configuration` field and constructor parameter (if no config is needed), or add configuration properties for the depth limits and reference them via `_configuration`.

---

### CLAUDE.md: Null-Forgiving Avoidance (Guid.Empty as Default for Nullable)

**File**: `plugins/lib-location/LocationService.cs`
**Lines**: 1364, 1389, 1415 (`ParentLocationId = model.ParentLocationId ?? Guid.Empty`)
**Lines**: 1367, 1392, 1418 (`DeprecatedAt = model.DeprecatedAt ?? default`)
**Lines**: 1369, 1394, 1420 (`Metadata = model.Metadata ?? new object()`)

**Violation**: These lines use `?? Guid.Empty`, `?? default`, and `?? new object()` to satisfy non-nullable event model properties when the source data is nullable. While not technically `?? string.Empty`, the pattern `?? default` is equivalent to coercing null to a zero-value without validation. The event schema defines `parentLocationId` as required non-nullable `Guid`, but the internal model has it as `Guid?`. This means events will contain `Guid.Empty` (all zeros) for root locations, which consumers must interpret as "no parent" -- a sentinel value pattern that should be avoided.

**Severity**: Medium. The event schema should either make `parentLocationId` nullable, or the service should not publish it when null. Same for `deprecatedAt` and `metadata`.

**Fix**: Update the event schema to make `parentLocationId`, `deprecatedAt`, and `metadata` optional/nullable fields. Alternatively, only populate them when non-null.

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

6. **Seed update doesn't publish events**: Lines 1084-1101 - when updating existing locations during seed with `updateExisting=true`, no `location.updated` event is published. The update uses direct `SaveAsync` bypassing the normal event publishing path.

7. **Index updates lack optimistic concurrency**: Index operations (realm, parent, root) load list, modify in-memory, then save without ETag or locking. Concurrent location creations could lose index updates in a race condition.

8. **Empty parent index key not cleaned up**: When the last child is removed from a parent index, the key remains with an empty list rather than being deleted. Over time, empty index keys accumulate.

9. **Depth cascade updates descendants sequentially**: `UpdateDescendantDepthsAsync` first collects ALL descendants (up to 20 levels), then updates each one with a separate state store call. A wide tree with hundreds of descendants generates hundreds of sequential writes in a single request.

10. **ListLocationsByParent returns NotFound for missing parent**: Lines 264-267 - if the parent location doesn't exist, returns NotFound. Other list operations (ListByRealm, ListRoot) return empty lists for missing realms/indexes. Inconsistent behavior.

11. **Undeprecate returns BadRequest not Conflict**: Line 1005 - unlike `DeprecateLocation` which returns Conflict when already deprecated, `UndeprecateLocation` returns BadRequest when not deprecated. Inconsistent error status pattern.
