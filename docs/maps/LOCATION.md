# Location Implementation Map

> **Plugin**: lib-location
> **Schema**: schemas/location-api.yaml
> **Layer**: GameFoundation
> **Deep Dive**: [docs/plugins/LOCATION.md](../plugins/LOCATION.md)

---

## Summary

| Field | Value |
|-------|-------|
| Plugin | lib-location |
| Layer | L2 GameFoundation |
| Endpoints | 25 |
| State Stores | location-statestore (MySQL), location-cache (Redis), location-lock (Redis), location-entity-presence (Redis), location-entity-set (Redis) |
| Events Published | 5 (`location.created`, `location.updated`, `location.deleted`, `location.entity-arrived`, `location.entity-departed`) |
| Events Consumed | 0 |
| Client Events | 2 (`location.presence-changed`, `location.updated`) |
| Background Services | 1 (EntityPresenceCleanupWorker) |

---

## State

**Store**: `location-statestore` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `location:{locationId}` | `LocationModel` | Primary location record |
| `code-index:{realmId}:{CODE}` | `string` | Code-to-locationId lookup (unique per realm) |
| `realm-index:{realmId}` | `List<Guid>` | All location IDs in a realm |
| `parent-index:{realmId}:{parentId}` | `List<Guid>` | Child location IDs under a parent |
| `root-locations:{realmId}` | `List<Guid>` | Root location IDs (no parent) in a realm |

**Store**: `location-cache` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `location:{locationId}` | `LocationModel` | TTL-based read-through cache (TTL: `CacheTtlSeconds`, default 3600) |

**Store**: `location-lock` (Backend: Redis)

Used by `IDistributedLockProvider` for distributed locks on index mutations. Lock keys match index key patterns (`realm-index:*`, `parent-index:*`, `root-locations:*`).

**Store**: `location-entity-presence` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `entity-location:{entityType}:{entityId}` | `EntityPresenceModel` | Entity-to-location binding with TTL (`EntityPresenceTtlSeconds`, default 30s) |

**Store**: `location-entity-set` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `location-entities:{locationId}` | `Set<string>` | Set of `{entityType}:{entityId}` members at a location |
| `location-entities:__index__` | `Set<string>` | Index of location IDs with active entity sets (cleanup worker discovery) |

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (`IStateStoreFactory`) | L0 | Hard | MySQL persistence, Redis cache, Redis presence, Redis sets |
| lib-state (`IDistributedLockProvider`) | L0 | Hard | Distributed locks on index mutations |
| lib-messaging (`IMessageBus`) | L0 | Hard | Publishing 5 event topics |
| lib-telemetry (`ITelemetryProvider`) | L0 | Hard | Span instrumentation on async helpers |
| lib-realm (`IRealmClient`) | L2 | Hard | Realm existence validation (create, transfer, seed) |
| lib-resource (`IResourceClient`) | L1 | Hard | Reference checks and cleanup execution on delete; compression callback registration at startup |
| lib-contract (`IContractClient`) | L1 | Hard | `territory_constraint` clause type registration at startup (OnRunningAsync) |
| lib-connect (`IEntitySessionRegistry`) | L1 | Hard | Client event delivery to sessions observing locations |

**DI Provider Interfaces**:
- `LocationContextProviderFactory` implements `IVariableProviderFactory` providing `${location.*}` namespace to Actor (L2) via DI discovery

**Startup Registrations** (in `OnRunningAsync`):
- Registers `territory_constraint` clause type with Contract via `IContractClient`
- Compression callback registered via schema `x-compression-callback` (generated code)

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `location.created` | `LocationCreatedEvent` | CreateLocation, SeedLocations (via CreateLocation) |
| `location.updated` | `LocationUpdatedEvent` | UpdateLocation, SetLocationParent, RemoveLocationParent, DeprecateLocation, UndeprecateLocation, TransferLocationToRealm, SeedLocations (when updateExisting and fields changed) |
| `location.deleted` | `LocationDeletedEvent` | DeleteLocation |
| `location.entity-arrived` | `LocationEntityArrivedEvent` | ReportEntityPosition (only on actual location change, not TTL refresh) |
| `location.entity-departed` | `LocationEntityDepartedEvent` | ReportEntityPosition (when moving from previous location), ClearEntityPosition |

---

## Events Consumed

This plugin does not consume external events.

---

## Client Events

| Event Name | Event Type | Target | Trigger |
|------------|-----------|--------|---------|
| `location.presence-changed` | `LocationPresenceChangedClientEvent` | Sessions observing the location | ReportEntityPosition (arrived/departed), ClearEntityPosition (departed) |
| `location.updated` | `LocationUpdatedClientEvent` | Sessions observing the location | UpdateLocation, SetLocationParent, RemoveLocationParent, DeprecateLocation, UndeprecateLocation, TransferLocationToRealm, SeedLocations (updateExisting) |

Routed via `IEntitySessionRegistry.PublishToEntitySessionsAsync("location", locationId, event)`. Gardener (L4) subscribes to `location.entity-arrived`/`location.entity-departed` to register/unregister session-to-location bindings.

---

## DI Services

| Service | Role |
|---------|------|
| `ILogger<LocationService>` | Structured logging |
| `LocationServiceConfiguration` | 11 configuration properties |
| `IStateStoreFactory` | State store access (5 stores) |
| `IDistributedLockProvider` | Index mutation locks |
| `IMessageBus` | Event publishing |
| `IRealmClient` | Realm validation |
| `IResourceClient` | Reference tracking and cleanup (startup + delete) |
| `ITelemetryProvider` | Span instrumentation |
| `IEntitySessionRegistry` | Client event routing to sessions |
| `ILocationDataCache` | ConcurrentDictionary cache for variable provider context data |
| `EntityPresenceCleanupWorker` | Background hosted service |
| `LocationContextProviderFactory` | `IVariableProviderFactory` implementation (`${location.*}`) |

---

## Method Index

| Method | Route | Roles | Mutates | Publishes |
|--------|-------|-------|---------|-----------|
| GetLocation | POST /location/get | user | - | - |
| GetLocationByCode | POST /location/get-by-code | user | - | - |
| ListLocations | POST /location/list | user | - | - |
| ListLocationsByRealm | POST /location/list-by-realm | user | - | - |
| ListLocationsByParent | POST /location/list-by-parent | user | - | - |
| ListRootLocations | POST /location/list-root | user | - | - |
| GetLocationAncestors | POST /location/get-ancestors | user | - | - |
| GetLocationDescendants | POST /location/get-descendants | user | - | - |
| ValidateTerritory | POST /location/validate-territory | user | - | - |
| LocationExists | POST /location/exists | user | - | - |
| QueryLocationsByPosition | POST /location/query/by-position | user | - | - |
| CreateLocation | POST /location/create | admin | location, code-index, realm-index, parent-index/root-locations, cache | location.created |
| UpdateLocation | POST /location/update | admin | location, cache | location.updated |
| SetLocationParent | POST /location/set-parent | admin | location, parent-index, root-locations, cache, descendants | location.updated |
| RemoveLocationParent | POST /location/remove-parent | admin | location, parent-index, root-locations, cache, descendants | location.updated |
| DeleteLocation | POST /location/delete | admin | location, code-index, realm-index, parent-index/root-locations, cache | location.deleted |
| DeprecateLocation | POST /location/deprecate | admin | location, cache | location.updated |
| UndeprecateLocation | POST /location/undeprecate | admin | location, cache | location.updated |
| TransferLocationToRealm | POST /location/transfer-realm | admin | location, code-index, realm-index, parent-index/root-locations, cache | location.updated |
| SeedLocations | POST /location/seed | admin | (delegates to CreateLocation, SetLocationParent) | location.created, location.updated |
| ReportEntityPosition | POST /location/report-entity-position | developer | entity-presence, entity-set, entity-index | location.entity-arrived, location.entity-departed |
| GetEntityLocation | POST /location/get-entity-location | user | - | - |
| ListEntitiesAtLocation | POST /location/list-entities-at-location | user | - | - |
| ClearEntityPosition | POST /location/clear-entity-position | developer | entity-presence, entity-set | location.entity-departed |
| GetLocationCompressData | POST /location/get-compress-data | developer | - | - |

---

## Methods

### GetLocation
POST /location/get | Roles: [user]

```
READ cache:location:{locationId}
IF cache miss
  READ store:location:{locationId}                -> 404 if null
  WRITE cache:location:{locationId} <- model (TTL: CacheTtlSeconds)
RETURN (200, LocationResponse)
```

### GetLocationByCode
POST /location/get-by-code | Roles: [user]

```
READ store:code-index:{realmId}:{CODE_UPPER}      -> 404 if null/empty
// Parse stored string as Guid
IF parse fails                                     -> 404
READ cache:location:{parsedLocationId}
IF cache miss
  READ store:location:{parsedLocationId}           -> 404 if null
  WRITE cache:location:{parsedLocationId} <- model (TTL: CacheTtlSeconds)
RETURN (200, LocationResponse)
```

### ListLocations
POST /location/list | Roles: [user]

```
READ store:realm-index:{realmId}                   // null → empty list
READ BULK cache+store for all locationIds           // cache-first, backfill misses
IF !includeDeprecated: filter out deprecated
IF locationType specified: filter by type
// In-memory pagination
RETURN (200, LocationListResponse)
```

### ListLocationsByRealm
POST /location/list-by-realm | Roles: [user]

```
READ store:realm-index:{realmId}                   // null → empty list
IF empty                                           -> RETURN (200, empty LocationListResponse)
READ BULK cache+store for all locationIds
IF !includeDeprecated: filter out deprecated
IF locationType specified: filter by type
// In-memory pagination
RETURN (200, LocationListResponse)
```

### ListLocationsByParent
POST /location/list-by-parent | Roles: [user]

```
READ store:location:{parentLocationId}             -> 404 if null
READ store:parent-index:{parentModel.RealmId}:{parentLocationId}
IF empty                                           -> RETURN (200, empty LocationListResponse)
READ BULK cache+store for all childIds
IF !includeDeprecated: filter out deprecated
IF locationType specified: filter by type
// In-memory pagination
RETURN (200, LocationListResponse)
```

### ListRootLocations
POST /location/list-root | Roles: [user]

```
READ store:root-locations:{realmId}                // null → empty list
IF empty                                           -> RETURN (200, empty LocationListResponse)
READ BULK cache+store for all rootIds
IF !includeDeprecated: filter out deprecated
IF locationType specified: filter by type
// In-memory pagination
RETURN (200, LocationListResponse)
```

### GetLocationAncestors
POST /location/get-ancestors | Roles: [user]

```
READ store:location:{locationId}                   -> 404 if null
ancestors = []
currentParentId = model.ParentLocationId
FOREACH depth up to config.MaxAncestorDepth
  IF currentParentId is null: break
  READ store:location:{currentParentId}
  IF null: break                                   // log warning, corrupted data
  ancestors.add(parent)
  currentParentId = parent.ParentLocationId
RETURN (200, LocationListResponse from ancestors)
```

### GetLocationDescendants
POST /location/get-descendants | Roles: [user]

```
READ store:location:{locationId}                   -> 404 if null
maxDepth = request.MaxDepth ?? config.DefaultDescendantMaxDepth
// cap at config.MaxDescendantDepth for safety
// see helper: CollectDescendantsAsync (recursive)
descendants = CollectDescendantsAsync(realmId, locationId, maxDepth, currentDepth=0)
IF !includeDeprecated: filter out deprecated
IF locationType specified: filter by type
// In-memory pagination
RETURN (200, LocationListResponse)
```

// CollectDescendantsAsync helper:
// READ store:parent-index:{realmId}:{parentId}
// FOREACH childId: READ store:location:{childId}, recurse if depth < maxDepth

### ValidateTerritory
POST /location/validate-territory | Roles: [user]

```
READ store:location:{locationId}                   -> 404 if null
// Build hierarchy set: location + all ancestors
hierarchySet = { locationId }
currentParentId = model.ParentLocationId
FOREACH depth up to config.MaxAncestorDepth
  IF currentParentId is null: break
  READ store:location:{currentParentId}
  IF null: break
  hierarchySet.add(currentParentId)
  currentParentId = parent.ParentLocationId
// Check overlap with territoryLocationIds
mode = request.TerritoryMode ?? Exclusive
IF mode == Exclusive
  IF any overlap: isValid = false, set violationReason + matchedTerritoryId
  ELSE: isValid = true
IF mode == Inclusive
  IF any overlap: isValid = true, set matchedTerritoryId
  ELSE: isValid = false, set violationReason
RETURN (200, ValidateTerritoryResponse { isValid, violationReason?, matchedTerritoryId? })
```

### LocationExists
POST /location/exists | Roles: [user]

```
READ store:location:{locationId}
IF null
  RETURN (200, LocationExistsResponse { exists: false, isActive: false })
RETURN (200, LocationExistsResponse { exists: true, isActive: !model.IsDeprecated, realmId: model.RealmId })
```

### QueryLocationsByPosition
POST /location/query/by-position | Roles: [user]

```
READ store:realm-index:{realmId}                   // null → empty list
IF empty                                           -> RETURN (200, empty LocationListResponse)
matches = []
FOREACH locationId in realmIndex
  READ cache:location:{locationId}                 // cache-first via GetLocationWithCacheAsync
  IF model.Bounds == null: skip
  IF model.BoundsPrecision == None: skip
  IF maxDepth specified AND model.Depth > maxDepth: skip
  IF position within model.Bounds (AABB containment): matches.add(model)
// Sort matches by Depth descending (most specific first)
// In-memory pagination
RETURN (200, LocationListResponse)
```

### CreateLocation
POST /location/create | Roles: [admin]

```
CALL _realmClient.RealmExistsAsync(realmId)        -> 400 if not found/inactive
READ store:code-index:{realmId}:{CODE_UPPER}       -> 409 if already exists
IF parentLocationId specified
  READ store:location:{parentLocationId}           -> 400 if not found
  IF parentModel.RealmId != realmId                -> 400 cross-realm parent
  depth = parentModel.Depth + 1
ELSE
  depth = 0
WRITE store:location:{newId} <- new LocationModel
WRITE store:code-index:{realmId}:{CODE_UPPER} <- locationId string
LOCK lock:realm-index:{realmId}
  READ store:realm-index:{realmId}
  WRITE store:realm-index:{realmId} <- add locationId
IF parentLocationId specified
  LOCK lock:parent-index:{realmId}:{parentId}
    READ store:parent-index:{realmId}:{parentId}
    WRITE store:parent-index:{realmId}:{parentId} <- add locationId
ELSE
  LOCK lock:root-locations:{realmId}
    READ store:root-locations:{realmId}
    WRITE store:root-locations:{realmId} <- add locationId
WRITE cache:location:{locationId} <- model (TTL: CacheTtlSeconds)
PUBLISH location.created { full model fields }
RETURN (200, LocationResponse)
```

### UpdateLocation
POST /location/update | Roles: [admin]

```
READ store:location:{locationId}                   -> 404 if null
changedFields = []
// Apply each non-null field from request, track changes
IF no fields changed
  RETURN (200, LocationResponse)                   // current state, no event
WRITE store:location:{locationId} <- updated model
WRITE cache:location:{locationId} <- model (TTL: CacheTtlSeconds)
PUBLISH location.updated { model, changedFields }
PUSH location.updated client event to observing sessions { locationId, realmId, name, description, locationType, isDeprecated, changedFields }
RETURN (200, LocationResponse)
```

### SetLocationParent
POST /location/set-parent | Roles: [admin]

```
READ store:location:{locationId}                   -> 404 if null
IF model.ParentLocationId == newParentId           -> RETURN (200, LocationResponse) // idempotent
READ store:location:{newParentLocationId}          -> 400 if not found
IF parent.RealmId != model.RealmId                 -> 400 cross-realm
// Circular reference check via CollectDescendantsAsync
IF locationId is descendant of newParent           -> 400 circular reference
oldParentId = model.ParentLocationId
model.ParentLocationId = newParentId
model.Depth = parent.Depth + 1
WRITE store:location:{locationId} <- updated model
IF oldParentId was null
  LOCK lock:root-locations:{realmId}
    READ + WRITE: remove locationId from root-locations
ELSE
  LOCK lock:parent-index:{realmId}:{oldParentId}
    READ + WRITE/DELETE: remove locationId from old parent-index
LOCK lock:parent-index:{realmId}:{newParentId}
  READ + WRITE: add locationId to new parent-index
IF depth changed
  // see helper: UpdateDescendantDepthsAsync
  // Recursively updates all descendants' depth, saves to store, invalidates cache
WRITE cache:location:{locationId} <- model (TTL: CacheTtlSeconds)
PUBLISH location.updated { model, changedFields: [parentLocationId, depth] }
PUSH location.updated client event to observing sessions
RETURN (200, LocationResponse)
```

### RemoveLocationParent
POST /location/remove-parent | Roles: [admin]

```
READ store:location:{locationId}                   -> 404 if null
IF model.ParentLocationId is null                  -> RETURN (200, LocationResponse) // idempotent
oldParentId = model.ParentLocationId
oldDepth = model.Depth
model.ParentLocationId = null
model.Depth = 0
WRITE store:location:{locationId} <- updated model
LOCK lock:parent-index:{realmId}:{oldParentId}
  READ + WRITE/DELETE: remove locationId from old parent-index
LOCK lock:root-locations:{realmId}
  READ + WRITE: add locationId to root-locations
IF oldDepth != 0
  // see helper: UpdateDescendantDepthsAsync
WRITE cache:location:{locationId} <- model (TTL: CacheTtlSeconds)
PUBLISH location.updated { model, changedFields: [parentLocationId, depth] }
PUSH location.updated client event to observing sessions
RETURN (200, LocationResponse)
```

### DeleteLocation
POST /location/delete | Roles: [admin]

```
READ store:location:{locationId}                   -> 404 if null
IF !model.IsDeprecated                             -> 400 must deprecate first (Category A)
READ store:parent-index:{realmId}:{locationId}     -> 409 if has children
CALL _resourceClient.CheckReferencesAsync(location, locationId)
  // catch ApiException 404: no references (normal, continue)
  // catch other ApiException: log error, publish error event -> 503
IF referenceCount > 0
  CALL _resourceClient.ExecuteCleanupAsync(AllRequired)
  IF !cleanup.Success                              -> 409 cleanup blocked
DELETE store:location:{locationId}
DELETE store:code-index:{realmId}:{CODE_UPPER}
LOCK lock:realm-index:{realmId}
  READ + WRITE: remove locationId from realm-index
IF model.ParentLocationId is null
  LOCK lock:root-locations:{realmId}
    READ + WRITE: remove locationId from root-locations
ELSE
  LOCK lock:parent-index:{realmId}:{parentId}
    READ + WRITE/DELETE: remove locationId from parent-index
DELETE cache:location:{locationId}
PUBLISH location.deleted { full model snapshot }
RETURN 200
```

### DeprecateLocation
POST /location/deprecate | Roles: [admin]

```
READ store:location:{locationId}                   -> 404 if null
IF model.IsDeprecated                              -> RETURN (200, LocationResponse) // idempotent
model.IsDeprecated = true
model.DeprecatedAt = now
model.DeprecationReason = request.Reason
WRITE store:location:{locationId} <- updated model
WRITE cache:location:{locationId} <- model (TTL: CacheTtlSeconds)
PUBLISH location.updated { model, changedFields: [isDeprecated, deprecatedAt, deprecationReason] }
PUSH location.updated client event to observing sessions
RETURN (200, LocationResponse)
```

### UndeprecateLocation
POST /location/undeprecate | Roles: [admin]

```
READ store:location:{locationId}                   -> 404 if null
IF !model.IsDeprecated                             -> RETURN (200, LocationResponse) // idempotent
model.IsDeprecated = false
model.DeprecatedAt = null
model.DeprecationReason = null
WRITE store:location:{locationId} <- updated model
WRITE cache:location:{locationId} <- model (TTL: CacheTtlSeconds)
PUBLISH location.updated { model, changedFields: [isDeprecated, deprecatedAt, deprecationReason] }
PUSH location.updated client event to observing sessions
RETURN (200, LocationResponse)
```

### TransferLocationToRealm
POST /location/transfer-realm | Roles: [admin]

```
READ store:location:{locationId}                   -> 404 if null
IF model.RealmId == targetRealmId                  -> RETURN (200, LocationResponse) // idempotent
CALL _realmClient.RealmExistsAsync(targetRealmId)  -> 404 if not found/inactive
READ store:code-index:{targetRealmId}:{CODE_UPPER} -> 409 if code collision
// Remove from source realm indexes
DELETE store:code-index:{oldRealmId}:{CODE_UPPER}
LOCK lock:realm-index:{oldRealmId}
  READ + WRITE: remove locationId from old realm-index
IF model.ParentLocationId specified
  LOCK lock:parent-index:{oldRealmId}:{parentId}
    READ + WRITE/DELETE: remove from old parent-index
ELSE
  LOCK lock:root-locations:{oldRealmId}
    READ + WRITE: remove from old root-locations
// Update model: new realm, clear parent, depth = 0
model.RealmId = targetRealmId
model.ParentLocationId = null
model.Depth = 0
WRITE store:location:{locationId} <- updated model
WRITE store:code-index:{targetRealmId}:{CODE_UPPER} <- locationId string
// Add to target realm indexes
LOCK lock:realm-index:{targetRealmId}
  READ + WRITE: add locationId to target realm-index
LOCK lock:root-locations:{targetRealmId}
  READ + WRITE: add locationId to target root-locations
DELETE cache:location:{locationId}
PUBLISH location.updated { model, changedFields: [realmId, parentLocationId, depth] }
RETURN (200, LocationResponse)
```

### SeedLocations
POST /location/seed | Roles: [admin]

```
// Build realm code → ID map
realmCodeToId = {}
failedRealmCodes = {}
FOREACH unique realmCode in request.Locations
  CALL _realmClient.GetRealmByCodeAsync(realmCode)
  IF success: realmCodeToId[code] = realmId
  ELSE: failedRealmCodes.add(code), record error

created = 0, updated = 0, skipped = 0, errors = []

// Pass 1: Create/update locations without parents
FOREACH seedLocation in request.Locations
  IF realmCode in failedRealmCodes: skip, record error
  READ store:code-index:{realmId}:{CODE_UPPER}
  IF exists AND updateExisting
    READ store:location:{existingId}
    IF fields changed
      // Update spatial and metadata fields
      WRITE store:location:{existingId} <- updated model
      WRITE cache:location:{existingId} <- model
      PUBLISH location.updated { model, changedFields }
      PUSH location.updated client event
      updated++
    ELSE
      skipped++
  ELSE IF exists AND !updateExisting
    skipped++
  ELSE
    // Delegate to CreateLocationAsync (full create flow with indexes + events)
    created++

// Pass 2: Set parent relationships
FOREACH seedLocation with parentLocationCode
  // Resolve parentCode from pass 1 results
  // Delegate to SetLocationParentAsync (full parent-set flow with circular check, indexes, events)

RETURN (200, SeedLocationsResponse { created, updated, skipped, errors })
```

### ReportEntityPosition
POST /location/report-entity-position | Roles: [developer]

```
READ cache:location:{locationId}                   -> 404 if null (via GetLocationWithCacheAsync)
IF request.PreviousLocationId specified
  previousLocationId = request.PreviousLocationId  // caller-hint fast path
ELSE
  READ presence:entity-location:{entityType}:{entityId}
  previousLocationId = existing?.LocationId
WRITE presence:entity-location:{entityType}:{entityId} <- EntityPresenceModel (TTL: EntityPresenceTtlSeconds)
  // realmId = request.RealmId ?? location.RealmId
IF previousLocationId != locationId                // actual location change
  WRITE set:location-entities:{locationId} <- add "{entityType}:{entityId}"
  WRITE set:location-entities:__index__ <- add locationId
  IF previousLocationId != null
    WRITE set:location-entities:{previousLocationId} <- remove "{entityType}:{entityId}"
    PUBLISH location.entity-departed { entityType, entityId, previousLocationId, realmId, reportedBy }
    PUSH location.presence-changed to sessions observing previousLocationId { changeType: Departed }
  PUBLISH location.entity-arrived { entityType, entityId, locationId, realmId, reportedBy }
  PUSH location.presence-changed to sessions observing locationId { changeType: Arrived }
  RETURN (200, ReportEntityPositionResponse { arrivedAt: locationId, departedFrom: previousLocationId })
ELSE
  // TTL refresh only — no events
  RETURN (200, ReportEntityPositionResponse {})
```

### GetEntityLocation
POST /location/get-entity-location | Roles: [user]

```
READ presence:entity-location:{entityType}:{entityId}
IF null
  RETURN (200, GetEntityLocationResponse {})       // empty — no active presence
RETURN (200, GetEntityLocationResponse { locationId, realmId, reportedAt, reportedBy })
```

### ListEntitiesAtLocation
POST /location/list-entities-at-location | Roles: [user]

```
READ set:location-entities:{locationId} -> all members
// Parse each member as "{entityType}:{entityId}"
IF entityType filter specified: filter by type
// In-memory pagination, capped at config.MaxEntitiesPerLocationQuery
FOREACH paged entry
  READ presence:entity-location:{entityType}:{entityId}  // hydrate metadata
  // null presence = stale member (TTL expired, not yet cleaned); include with null metadata
RETURN (200, ListEntitiesAtLocationResponse { entities, totalCount, locationId })
```

### ClearEntityPosition
POST /location/clear-entity-position | Roles: [developer]

```
READ presence:entity-location:{entityType}:{entityId}
IF null
  RETURN (200, ClearEntityPositionResponse { previousLocationId: null }) // idempotent
DELETE presence:entity-location:{entityType}:{entityId}
WRITE set:location-entities:{existing.LocationId} <- remove "{entityType}:{entityId}"
PUBLISH location.entity-departed { entityType, entityId, existing.LocationId, realmId, reportedBy }
PUSH location.presence-changed to sessions observing existing.LocationId { changeType: Departed }
RETURN (200, ClearEntityPositionResponse { previousLocationId: existing.LocationId })
```

### GetLocationCompressData
POST /location/get-compress-data | Roles: [developer]

```
READ cache:location:{locationId}                   -> 404 if null (via GetLocationWithCacheAsync)
IF !model.IsDeprecated                             -> 400 must be deprecated
IF model.ParentLocationId specified
  READ cache:location:{parentLocationId}           // parent context (one level up)
READ store:parent-index:{realmId}:{locationId}     // children of this location
FOREACH childId in childIndex
  READ cache:location:{childId}                    // child codes for summary
RETURN (200, LocationBaseArchive { location data, parent context, childrenCount, childrenCodes })
```

---

## Background Services

### EntityPresenceCleanupWorker
**Interval**: `config.EntityPresenceCleanupIntervalSeconds` (default 60s)
**Startup Delay**: `config.EntityPresenceCleanupStartupDelaySeconds` (default 15s)
**Purpose**: Evict stale members from Redis entity sets when entity presence TTLs expire

```
WAIT startup delay
LOOP (while not cancelled)
  READ set:location-entities:__index__ -> all location ID strings
  IF empty: wait interval, continue
  FOREACH locationIdStr in index
    IF not valid Guid: mark for index removal, continue
    READ set:location-entities:{locationId} -> all members
    IF empty: mark location for index removal, continue
    FOREACH member in members
      // Parse as "{entityType}:{entityId}"
      IF malformed: mark stale
      ELSE
        READ presence:entity-location:{entityType}:{entityId}
        IF null (TTL expired): mark stale
    FOREACH staleMember
      WRITE set:location-entities:{locationId} <- remove staleMember
    IF all members stale
      DELETE set:location-entities:{locationId}
      mark location for index removal
  FOREACH emptyLocationId
    WRITE set:location-entities:__index__ <- remove emptyLocationId
  WAIT cleanup interval
```
