# Species Implementation Map

> **Plugin**: lib-species
> **Schema**: schemas/species-api.yaml
> **Layer**: GameFoundation
> **Deep Dive**: [docs/plugins/SPECIES.md](../plugins/SPECIES.md)

---

| Field | Value |
|-------|-------|
| Plugin | lib-species |
| Layer | L2 GameFoundation |
| Endpoints | 13 |
| State Stores | species-statestore (MySQL), species-lock (Redis) |
| Events Published | 4 (`species.created`, `species.updated`, `species.deleted`, `species.merged`) |
| Events Consumed | 0 |
| Client Events | 0 |
| Background Services | 0 |

---

## State

**Store**: `species-statestore` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `species:{speciesId}` | `SpeciesModel` | Individual species definition |
| `code-index:{CODE}` | `string` | Code-to-species-ID reverse lookup (uppercase) |
| `realm-index:{realmId}` | `List<Guid>` | Species IDs available in a realm |
| `all-species` | `List<Guid>` | Global index of all species IDs |

**Store**: `species-lock` (Backend: Redis, Prefix: `species:lock`)

| Key Pattern | Purpose |
|-------------|---------|
| `create:{code}` | Prevents duplicate code creation races |
| `{speciesId}` | Single-species mutations (delete, update, deprecate, undeprecate, realm changes) |
| `min(id1,id2)` + `max(id1,id2)` | Merge acquires two locks in deterministic GUID order |

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (`IStateStoreFactory`) | L0 | Hard | Constructor-cached to `_speciesStore`, `_codeIndexStore`, `_idListStore` |
| lib-state (`IDistributedLockProvider`) | L0 | Hard | All 8 mutation operations |
| lib-messaging (`IMessageBus`) | L0 | Hard | Publishing lifecycle events and error events |
| lib-messaging (`IEventConsumer`) | L0 | Hard | Registered but no active handlers |
| lib-telemetry (`ITelemetryProvider`) | L0 | Hard | Spans on all private async helpers |
| lib-character (`ICharacterClient`) | L2 | Hard | Reference checking (delete, merge, remove-from-realm), character migration (merge) |
| lib-realm (`IRealmClient`) | L2 | Hard | Realm existence/active validation, realm code resolution (seed) |
| lib-resource (`IResourceClient`) | L1 | Hard | Higher-layer reference checking and cleanup coordination (delete) |

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `species.created` | `SpeciesCreatedEvent` | CreateSpecies, SeedSpecies (create path) |
| `species.updated` | `SpeciesUpdatedEvent` | UpdateSpecies, DeprecateSpecies, UndeprecateSpecies, AddSpeciesToRealm, RemoveSpeciesFromRealm, SeedSpecies (update path) |
| `species.deleted` | `SpeciesDeletedEvent` | DeleteSpecies, MergeSpecies (deleteAfterMerge path) |
| `species.merged` | `SpeciesMergedEvent` | MergeSpecies |

---

## Events Consumed

This plugin does not consume external events.

---

## DI Services

| Service | Role |
|---------|------|
| `ILogger<SpeciesService>` | Structured logging |
| `SpeciesServiceConfiguration` | Config: `MergePageSize`, `LockTimeoutSeconds` |
| `IStateStoreFactory` | Constructor-only; resolves 3 typed stores |
| `IDistributedLockProvider` | Distributed locks for mutation safety |
| `IMessageBus` | Event publishing |
| `IEventConsumer` | Event handler registration (no active handlers) |
| `ITelemetryProvider` | Telemetry spans |
| `ICharacterClient` | Character reference checks and migration |
| `IRealmClient` | Realm validation and code resolution |
| `IResourceClient` | Higher-layer reference checking and cleanup |

---

## Method Index

| Method | Route | Roles | Mutates | Publishes |
|--------|-------|-------|---------|-----------|
| GetSpecies | POST /species/get | user | - | - |
| GetSpeciesByCode | POST /species/get-by-code | user | - | - |
| ListSpecies | POST /species/list | user | - | - |
| ListSpeciesByRealm | POST /species/list-by-realm | user | - | - |
| CreateSpecies | POST /species/create | admin | species, code-index, realm-index, all-species | species.created |
| UpdateSpecies | POST /species/update | admin | species | species.updated |
| DeleteSpecies | POST /species/delete | admin | species, code-index, realm-index, all-species | species.deleted |
| DeprecateSpecies | POST /species/deprecate | admin | species | species.updated |
| UndeprecateSpecies | POST /species/undeprecate | admin | species | species.updated |
| MergeSpecies | POST /species/merge | admin | species (via delete path) | species.merged, species.deleted |
| AddSpeciesToRealm | POST /species/add-to-realm | admin | species, realm-index | species.updated |
| RemoveSpeciesFromRealm | POST /species/remove-from-realm | admin | species, realm-index | species.updated |
| SeedSpecies | POST /species/seed | admin | species, code-index, realm-index, all-species | species.created, species.updated |

---

## Methods

### GetSpecies
POST /species/get | Roles: [user]

```
READ _speciesStore:"species:{speciesId}"                -> 404 if null
RETURN (200, SpeciesResponse)
```

### GetSpeciesByCode
POST /species/get-by-code | Roles: [user]

```
READ _codeIndexStore:"code-index:{code.ToUpperInvariant()}"  -> 404 if null/empty
// Parse string to Guid
                                                              -> 404 if not valid Guid
READ _speciesStore:"species:{speciesId}"                      -> 404 if null
// Stale index logged as warning if index exists but model absent
RETURN (200, SpeciesResponse)
```

### ListSpecies
POST /species/list | Roles: [user]

```
READ _idListStore:"all-species"
IF null or empty
  RETURN (200, SpeciesListResponse { species: [], totalCount: 0 })
// Bulk load all species by ID
READ _speciesStore:GetBulkAsync(["species:{id}" for each id])
// In-memory filtering:
//   - exclude deprecated unless includeDeprecated == true
//   - optional category match (case-insensitive)
//   - optional isPlayable match
// totalCount = filtered count (before pagination)
// Paginate: skip (page-1)*pageSize, take pageSize
RETURN (200, SpeciesListResponse { species, totalCount })
```

### ListSpeciesByRealm
POST /species/list-by-realm | Roles: [user]

```
CALL IRealmClient.RealmExistsAsync({ realmId })
// Checks exists only; allows deprecated realms
                                                              -> 404 if realm not found
READ _idListStore:"realm-index:{realmId}"
IF null or empty
  RETURN (200, SpeciesListResponse { species: [], totalCount: 0 })
READ _speciesStore:GetBulkAsync(["species:{id}" for each id])
// In-memory filtering:
//   - exclude deprecated unless includeDeprecated == true
//   - optional isPlayable match
// Paginate: skip (page-1)*pageSize, take pageSize
RETURN (200, SpeciesListResponse { species, totalCount })
```

### CreateSpecies
POST /species/create | Roles: [admin]

```
// Normalize code to uppercase
IF realmIds provided
  CALL IRealmClient.RealmExistsAsync({ realmId }) (parallel)  -> 400 if any realm not found
                                                               -> 400 if any realm deprecated
LOCK species-lock:"create:{code}"
  READ _codeIndexStore:"code-index:{code}"                    -> 409 if already exists
  // Build SpeciesModel from request
  WRITE _speciesStore:"species:{speciesId}" <- SpeciesModel
  WRITE _codeIndexStore:"code-index:{code}" <- speciesId string
  READ _idListStore:"all-species"
  // Append speciesId if not already present
  WRITE _idListStore:"all-species" <- updated list
  FOREACH realmId in realmIds
    READ _idListStore:"realm-index:{realmId}"
    // Append speciesId if not already present
    WRITE _idListStore:"realm-index:{realmId}" <- updated list
  PUBLISH species.created { full entity state }
RETURN (200, SpeciesResponse)
```

### UpdateSpecies
POST /species/update | Roles: [admin]

```
LOCK species-lock:"{speciesId}"
  READ _speciesStore:"species:{speciesId}"                    -> 404 if null
  // ApplySpeciesFieldUpdates: compare each non-null field, track changedFields
  // traitModifiers and metadata treated as changed if non-null (no deep equality)
  IF changedFields.Count > 0
    WRITE _speciesStore:"species:{speciesId}" <- updated model
    PUBLISH species.updated { full entity state, changedFields }
RETURN (200, SpeciesResponse)
```

### DeleteSpecies
POST /species/delete | Roles: [admin]

```
LOCK species-lock:"{speciesId}"
  READ _speciesStore:"species:{speciesId}"                    -> 404 if null
  IF not deprecated                                           -> 400
  CALL ICharacterClient.ListCharactersAsync({ speciesId, page: 1, pageSize: 1 })
  IF characters exist (totalCount > 0)                        -> 409
  CALL IResourceClient.CheckReferencesAsync({ resourceType: "species", resourceId })
  IF refCount > 0
    CALL IResourceClient.ExecuteCleanupAsync({ resourceType: "species", resourceId, policy: AllRequired })
    IF cleanup blocked                                        -> 409
  DELETE _speciesStore:"species:{speciesId}"
  DELETE _codeIndexStore:"code-index:{code}"
  READ _idListStore:"all-species"
  // Remove speciesId from list
  WRITE _idListStore:"all-species" <- updated list
  FOREACH realmId in model.RealmIds
    READ _idListStore:"realm-index:{realmId}"
    // Remove speciesId from list
    WRITE _idListStore:"realm-index:{realmId}" <- updated list
  PUBLISH species.deleted { full entity state, deletedReason: null }
RETURN (200)
// Note: no response body — returns bare StatusCodes
```

### DeprecateSpecies
POST /species/deprecate | Roles: [admin]

```
LOCK species-lock:"{speciesId}"
  READ _speciesStore:"species:{speciesId}"                    -> 404 if null
  IF already deprecated
    RETURN (200, SpeciesResponse)                              // Idempotent
  // Set IsDeprecated=true, DeprecatedAt=now, DeprecationReason=request.reason
  WRITE _speciesStore:"species:{speciesId}" <- updated model
  PUBLISH species.updated { full entity state, changedFields: [isDeprecated, deprecatedAt, deprecationReason] }
RETURN (200, SpeciesResponse)
```

### UndeprecateSpecies
POST /species/undeprecate | Roles: [admin]

```
LOCK species-lock:"{speciesId}"
  READ _speciesStore:"species:{speciesId}"                    -> 404 if null
  IF not deprecated
    RETURN (200, SpeciesResponse)                              // Idempotent
  // Clear IsDeprecated=false, DeprecatedAt=null, DeprecationReason=null
  WRITE _speciesStore:"species:{speciesId}" <- updated model
  PUBLISH species.updated { full entity state, changedFields: [isDeprecated, deprecatedAt, deprecationReason] }
RETURN (200, SpeciesResponse)
```

### MergeSpecies
POST /species/merge | Roles: [admin]

```
// Deterministic lock ordering: lower GUID first
LOCK species-lock:"{min(sourceId, targetId)}"
  LOCK species-lock:"{max(sourceId, targetId)}"
    READ _speciesStore:"species:{sourceSpeciesId}"            -> 404 if null
    IF source not deprecated                                  -> 400
    READ _speciesStore:"species:{targetSpeciesId}"            -> 404 if null
    IF target is deprecated                                   -> 400
    // Paginated character migration
    FOREACH page until no more characters
      CALL ICharacterClient.ListCharactersAsync({ speciesId: source, page, pageSize: config.MergePageSize })
      FOREACH character in page
        CALL ICharacterClient.UpdateCharacterAsync({ characterId, speciesId: target })
        IF update fails -> add characterId to failedEntityIds
    PUBLISH species.merged { sourceSpeciesId, targetSpeciesId, mergedCharacterCount }
    IF deleteAfterMerge AND failedEntityIds is empty
      // Delegates to DeleteSpeciesAsync (internal call)
      // Delete performs: deprecation check, character check, resource check, cleanup, index removal
RETURN (200, MergeSpeciesResponse { charactersMigrated, sourceDeleted, failedEntityIds })
// failedEntityIds is null if empty (not empty list)
```

### AddSpeciesToRealm
POST /species/add-to-realm | Roles: [admin]

```
CALL IRealmClient.RealmExistsAsync({ realmId })
                                                              -> 404 if realm not found
                                                              -> 400 if realm deprecated
LOCK species-lock:"{speciesId}"
  READ _speciesStore:"species:{speciesId}"                    -> 404 if null
  IF realmId already in model.RealmIds                        -> 409
  // Add realmId to model.RealmIds, update UpdatedAt
  WRITE _speciesStore:"species:{speciesId}" <- updated model
  READ _idListStore:"realm-index:{realmId}"
  // Append speciesId if not already present
  WRITE _idListStore:"realm-index:{realmId}" <- updated list
  PUBLISH species.updated { full entity state, changedFields: [realmIds] }
RETURN (200, SpeciesResponse)
```

### RemoveSpeciesFromRealm
POST /species/remove-from-realm | Roles: [admin]

```
LOCK species-lock:"{speciesId}"
  READ _speciesStore:"species:{speciesId}"                    -> 404 if null
  IF realmId not in model.RealmIds                            -> 404
  CALL ICharacterClient.GetCharactersByRealmAsync({ realmId, speciesId, page: 1, pageSize: 1 })
  IF characters exist (totalCount > 0)                        -> 409
  // Remove realmId from model.RealmIds, update UpdatedAt
  WRITE _speciesStore:"species:{speciesId}" <- updated model
  READ _idListStore:"realm-index:{realmId}"
  // Remove speciesId if present
  WRITE _idListStore:"realm-index:{realmId}" <- updated list
  PUBLISH species.updated { full entity state, changedFields: [realmIds] }
RETURN (200, SpeciesResponse)
```

### SeedSpecies
POST /species/seed | Roles: [admin]

```
FOREACH seedItem in request.species
  // Normalize code to uppercase
  READ _codeIndexStore:"code-index:{code}"
  IF exists AND updateExisting
    READ _speciesStore:"species:{existingId}"
    // ApplySpeciesFieldUpdates: compare fields, track changes
    IF changedFields.Count > 0
      WRITE _speciesStore:"species:{existingId}" <- updated model
      PUBLISH species.updated { full entity state, changedFields }
      // updated++
    ELSE
      // skipped++
  ELSE IF exists AND NOT updateExisting
    // skipped++
  ELSE
    // Resolve realmCodes to realm IDs
    FOREACH realmCode in seedItem.realmCodes
      CALL IRealmClient.GetRealmByCodeAsync({ code })
      // 404 or exception -> skip realm with warning log
    // Delegate to CreateSpeciesAsync (full create logic with lock)
    // created++
  // Per-species exceptions caught, added to errors list
RETURN (200, SeedSpeciesResponse { created, updated, skipped, errors })
// Always returns 200; errors accumulated in response
```

---

## Background Services

No background services.
