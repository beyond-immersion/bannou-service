# Realm Implementation Map

> **Plugin**: lib-realm
> **Schema**: schemas/realm-api.yaml
> **Layer**: GameFoundation
> **Deep Dive**: [docs/plugins/REALM.md](../plugins/REALM.md)

---

| Field | Value |
|-------|-------|
| Plugin | lib-realm |
| Layer | L2 GameFoundation |
| Endpoints | 13 |
| State Stores | realm-statestore (MySQL), realm-lock (Redis) |
| Events Published | 4 (realm.created, realm.updated, realm.deleted, realm.merged) |
| Events Consumed | 0 |
| Client Events | 0 |
| Background Services | 0 |

---

## State

**Store**: `realm-statestore` (Backend: MySQL) — three typed views from `StateStoreDefinitions.Realm`

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `realm:{realmId}` | `RealmModel` | Full realm definition (name, code, gameServiceId, deprecation state, metadata) |
| `code-index:{CODE}` | `string` | Uppercase-normalized code to realm ID lookup |
| `all-realms` | `List<Guid>` | Master list of all realm IDs for list/pagination |

**Store**: `realm-lock` (Backend: Redis) — via `StateStoreDefinitions.RealmLock`

| Key Pattern | Purpose |
|-------------|---------|
| `merge:{smallerId}:{largerId}` | Distributed lock for realm merge (deterministic ordering prevents deadlocks) |

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (IStateStoreFactory) | L0 | Hard | 3 typed store views from StateStoreDefinitions.Realm |
| lib-state (IDistributedLockProvider) | L0 | Hard | Merge operation lock via StateStoreDefinitions.RealmLock |
| lib-messaging (IMessageBus) | L0 | Hard | Publishing realm.created/updated/deleted/merged events |
| lib-messaging (IEventConsumer) | L0 | Hard | Event handler registration (no handlers currently) |
| lib-telemetry (ITelemetryProvider) | L0 | Hard | Span instrumentation on async helpers |
| lib-resource (IResourceClient) | L1 | Hard | CheckReferences + ExecuteCleanup before deletion; compression callback registration at startup |
| lib-species (ISpeciesClient) | L2 | Hard | Species migration during realm merge |
| lib-location (ILocationClient) | L2 | Hard | Location migration during merge; location lookup for compression context |
| lib-character (ICharacterClient) | L2 | Hard | Character migration during realm merge |
| lib-worldstate (IWorldstateClient) | L2 | Hard | Optional clock initialization after creation (soft-failure: warning logged, creation proceeds) |

No DI Provider/Listener interfaces implemented or consumed.

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `realm.created` | `RealmCreatedEvent` | CreateRealm, SeedRealms (new realm path) |
| `realm.updated` | `RealmUpdatedEvent` | UpdateRealm, DeprecateRealm, UndeprecateRealm, SeedRealms (update path) — includes `changedFields` |
| `realm.deleted` | `RealmDeletedEvent` | DeleteRealm |
| `realm.merged` | `RealmMergedEvent` | MergeRealms — includes per-entity-type migration counts |

---

## Events Consumed

This plugin does not consume external events.

---

## DI Services

| Service | Role |
|---------|------|
| `ILogger<RealmService>` | Structured logging |
| `RealmServiceConfiguration` | Typed config (MergePageSize, OptimisticRetryAttempts, MergeLockTimeoutSeconds, AutoInitializeWorldstateClock, DefaultCalendarTemplateCode) |
| `IStateStoreFactory` | State store access (constructor-only, not stored as field) |
| `IDistributedLockProvider` | Distributed locks for merge |
| `ITelemetryProvider` | Span instrumentation |
| `IMessageBus` | Event publishing |
| `IEventConsumer` | Event registration (no handlers) |
| `IResourceClient` | Reference checking and cleanup orchestration |
| `ISpeciesClient` | Species realm association for merge |
| `ILocationClient` | Location transfer for merge; location lookup for compression |
| `ICharacterClient` | Character transfer for merge |
| `IWorldstateClient` | Optional clock initialization after creation |

---

## Method Index

| Method | Route | Roles | Mutates | Publishes |
|--------|-------|-------|---------|-----------|
| GetRealm | POST /realm/get | user | - | - |
| GetRealmByCode | POST /realm/get-by-code | user | - | - |
| ListRealms | POST /realm/list | user | - | - |
| RealmExists | POST /realm/exists | user | - | - |
| RealmsExistBatch | POST /realm/exists-batch | user | - | - |
| CreateRealm | POST /realm/create | admin | realm, code-index, all-realms | realm.created |
| UpdateRealm | POST /realm/update | admin | realm | realm.updated |
| DeleteRealm | POST /realm/delete | admin | realm, code-index, all-realms | realm.deleted |
| DeprecateRealm | POST /realm/deprecate | admin | realm | realm.updated |
| UndeprecateRealm | POST /realm/undeprecate | admin | realm | realm.updated |
| MergeRealms | POST /realm/merge | admin | (delegates to L2 clients) | realm.merged |
| SeedRealms | POST /realm/seed | admin | realm, code-index, all-realms | realm.created, realm.updated |
| GetLocationCompressContext | POST /realm/get-location-compress-context | developer | - | - |

---

## Methods

### GetRealm
POST /realm/get | Roles: [user]

```
READ realm:{realmId} -> 404 if null
RETURN (200, RealmResponse)
```

---

### GetRealmByCode
POST /realm/get-by-code | Roles: [user]

```
READ code-index:{CODE_UPPERCASE} -> 404 if null or invalid GUID
READ realm:{realmId} -> 404 if null (logs warning: data inconsistency)
RETURN (200, RealmResponse)
```

---

### ListRealms
POST /realm/list | Roles: [user]

```
READ all-realms -> empty list if null
IF allRealmIds is empty
 RETURN (200, RealmListResponse { realms: [], totalCount: 0 })
READ (bulk) realm:{id} for each ID // via LoadRealmsByIdsAsync
// In-memory filtering: includeDeprecated, category, isActive
// In-memory pagination: skip/take
RETURN (200, RealmListResponse)
```

---

### RealmExists
POST /realm/exists | Roles: [user]

```
READ realm:{realmId}
IF model == null -> 404
RETURN (200, RealmExistsResponse { isActive: model.IsActive && !model.IsDeprecated })
// 200 = exists, 404 = not found. No `exists` boolean needed.
```

---

### RealmsExistBatch
POST /realm/exists-batch | Roles: [user]

```
IF realmIds is empty
 RETURN (200, RealmsExistBatchResponse { allExist: true, allActive: true })
READ (bulk) realm:{id} for each ID // via LoadRealmsByIdsAsync
FOREACH realmId in request.RealmIds
 // Build per-ID result; track invalidRealmIds and deprecatedRealmIds
RETURN (200, RealmsExistBatchResponse { results, allExist, allActive, invalidRealmIds, deprecatedRealmIds })
```

---

### CreateRealm
POST /realm/create | Roles: [admin]

```
READ code-index:{CODE_UPPERCASE} -> 409 if already exists
WRITE realm:{newRealmId} <- RealmModel from request
WRITE code-index:{CODE_UPPERCASE} <- realmId string
// AddToRealmListAsync (ETag retry loop up to OptimisticRetryAttempts)
READ all-realms [with ETag]
ETAG-WRITE all-realms <- updated list with new ID
IF config.AutoInitializeWorldstateClock
 CALL IWorldstateClient.InitializeRealmClockAsync(realmId, calendarTemplateCode)
 // Failure logged as warning, creation proceeds
PUBLISH realm.created { full realm state }
RETURN (200, RealmResponse)
```

---

### UpdateRealm
POST /realm/update | Roles: [admin]

```
// Retry loop up to OptimisticRetryAttempts
READ realm:{realmId} [with ETag] -> 404 if null
// Smart field tracking: only apply non-null fields that differ from current
IF no fields changed
 RETURN (200, RealmResponse) // no-op, no write, no event
ETAG-WRITE realm:{realmId} <- updated model -> retry on ETag mismatch
IF all retries exhausted -> 409
PUBLISH realm.updated { full state, changedFields }
RETURN (200, RealmResponse)
```

---

### DeleteRealm
POST /realm/delete | Roles: [admin]

```
READ realm:{realmId} -> 404 if null
IF !model.IsDeprecated -> 400 (must deprecate first)
CALL IResourceClient.CheckReferencesAsync(realm, realmId)
 // ApiException 404 = no references (normal)
 // ApiException other = 503 (fail-closed)
IF references exist
 CALL IResourceClient.ExecuteCleanupAsync(realm, realmId, AllRequired)
 IF !cleanup.Success -> 409
DELETE realm:{realmId}
DELETE code-index:{CODE}
// RemoveFromRealmListAsync (ETag retry loop)
READ all-realms [with ETag]
ETAG-WRITE all-realms <- updated list without ID
PUBLISH realm.deleted { full realm state }
RETURN (200)
```

---

### DeprecateRealm
POST /realm/deprecate | Roles: [admin]

```
// Retry loop up to OptimisticRetryAttempts
READ realm:{realmId} [with ETag] -> 404 if null
IF already deprecated
 RETURN (200, RealmResponse) // idempotent
// Set IsDeprecated=true, DeprecatedAt=now, DeprecationReason=body.Reason
ETAG-WRITE realm:{realmId} <- updated model -> retry on ETag mismatch
IF all retries exhausted -> 409
PUBLISH realm.updated { changedFields: [isDeprecated, deprecatedAt, deprecationReason] }
RETURN (200, RealmResponse)
```

---

### UndeprecateRealm
POST /realm/undeprecate | Roles: [admin]

```
// Retry loop up to OptimisticRetryAttempts
READ realm:{realmId} [with ETag] -> 404 if null
IF not deprecated
 RETURN (200, RealmResponse) // idempotent
// Clear IsDeprecated, DeprecatedAt, DeprecationReason
ETAG-WRITE realm:{realmId} <- updated model -> retry on ETag mismatch
IF all retries exhausted -> 409
PUBLISH realm.updated { changedFields: [isDeprecated, deprecatedAt, deprecationReason] }
RETURN (200, RealmResponse)
```

---

### MergeRealms
POST /realm/merge | Roles: [admin]

```
IF sourceRealmId == targetRealmId -> 400
LOCK realm-lock:merge:{smallerId}:{largerId} -> 409 if lock fails
 READ realm:{sourceRealmId} -> 404 if null
 IF !source.IsDeprecated -> 400
 IF source.IsSystemType -> 400
 READ realm:{targetRealmId} -> 404 if null

 // Phase A: Species Migration
 // Page-1-always loop (successful migrations remove from source)
 FOREACH page of species from ISpeciesClient.ListSpeciesByRealmAsync(source)
 FOREACH species
 CALL ISpeciesClient.AddSpeciesToRealmAsync(species, target)
 CALL ISpeciesClient.RemoveSpeciesFromRealmAsync(species, source)
 // Individual failures tracked, do not abort

 // Phase B: Location Migration (root-first)
 FOREACH page of roots from ILocationClient.ListRootLocationsAsync(source)
 FOREACH root
 CALL ILocationClient.GetLocationDescendantsAsync(root)
 CALL ILocationClient.TransferLocationToRealmAsync(root, target)
 FOREACH descendant sorted by depth (shallowest first)
 CALL ILocationClient.TransferLocationToRealmAsync(descendant, target)
 IF descendant has parent
 CALL ILocationClient.SetLocationParentAsync(descendant, parent)

 // Phase C: Character Migration
 // Page-1-always loop
 FOREACH page of characters from ICharacterClient.GetCharactersByRealmAsync(source)
 FOREACH character
 CALL ICharacterClient.TransferCharacterToRealmAsync(character, target)
 // Individual failures tracked, do not abort

 PUBLISH realm.merged { sourceRealmId, targetRealmId, migration counts }

 IF body.DeleteAfterMerge && totalFailed == 0
 // Calls self.DeleteRealmAsync (includes resource check, publishes realm.deleted)

RETURN (200, MergeRealmsResponse { per-type migrated/failed counts, sourceDeleted })
```

---

### SeedRealms
POST /realm/seed | Roles: [admin]

```
FOREACH seedRealm in body.Realms
 READ code-index:{CODE_UPPERCASE}
 IF exists AND body.UpdateExisting
 // ETag retry loop up to OptimisticRetryAttempts
 READ realm:{existingId} [with ETag]
 // Apply changed fields (same smart tracking as UpdateRealm)
 IF fields changed
 ETAG-WRITE realm:{existingId} <- updated model
 PUBLISH realm.updated { changedFields }
 // Increment updated (or skipped if no changes)
 ELSE IF exists AND !body.UpdateExisting
 // Increment skipped
 ELSE
 // Delegate to CreateRealmAsync
 WRITE realm:{newId} <- RealmModel from seed
 WRITE code-index:{CODE} <- newId
 ETAG-WRITE all-realms <- list with new ID
 PUBLISH realm.created { full state }
 // Increment created
 // Per-realm exceptions caught, added to errors list
RETURN (200, SeedRealmsResponse { created, updated, skipped, errors })
```

---

### GetLocationCompressContext
POST /realm/get-location-compress-context | Roles: [developer]

```
CALL ILocationClient.GetLocationAsync(locationId) -> 404 if ApiException(404)
READ realm:{location.RealmId} -> 404 if null
RETURN (200, RealmLocationArchiveContext { realmId, realmName, realmCode, realmDescription })
```

---

## Background Services

No background services.
