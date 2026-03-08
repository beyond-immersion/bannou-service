# Relationship Implementation Map

> **Plugin**: lib-relationship
> **Schema**: schemas/relationship-api.yaml
> **Layer**: GameFoundation
> **Deep Dive**: [docs/plugins/RELATIONSHIP.md](../plugins/RELATIONSHIP.md)

---

## Summary

| Field | Value |
|-------|-------|
| Plugin | lib-relationship |
| Layer | L2 GameFoundation |
| Endpoints | 21 |
| State Stores | relationship-statestore (MySQL), relationship-type-statestore (MySQL), relationship-lock (Redis) |
| Events Published | 7 (relationship.created, relationship.updated, relationship.deleted, relationship.type.created, relationship.type.updated, relationship.type.deleted, relationship.type.merged) |
| Events Consumed | 3 (self-subscriptions for cache invalidation) |
| Client Events | 0 |
| Background Services | 0 |

---

## State

**Store**: `relationship-statestore` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `rel:{relationshipId}` | `RelationshipModel` | Full relationship record |
| `entity-idx:{entityType}:{entityId}` | `List<Guid>` | Relationship IDs involving this entity |
| `type-idx:{relationshipTypeId}` | `List<Guid>` | Relationship IDs of this type |
| `composite:{entity1}:{entity2}:{typeId}` | `string` | Bidirectional uniqueness constraint (normalized key -> relationship ID) |

**Store**: `relationship-type-statestore` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `type:{typeId}` | `RelationshipTypeModel` | Individual type definition |
| `code-index:{CODE}` | `string` | Code to type ID reverse lookup (uppercase normalized) |
| `parent-index:{parentId}` | `List<Guid>` | Child type IDs for a parent |
| `all-types` | `List<Guid>` | Global index of all type IDs |

**Store**: `relationship-lock` (Backend: Redis)

Used by `IDistributedLockProvider` for composite key, relationship ID, type ID, and index locking.

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (`IStateStoreFactory`) | L0 | Hard | 6 typed store references across 2 data stores |
| lib-state (`IDistributedLockProvider`) | L0 | Hard | Composite key, relationship, type, and index locking |
| lib-messaging (`IMessageBus`) | L0 | Hard | Publishing 7 event topics |
| lib-messaging (`IEventConsumer`) | L0 | Hard | Registering 3 self-subscription handlers for cache invalidation |
| lib-telemetry (`ITelemetryProvider`) | L0 | Hard | Span instrumentation |
| lib-resource (`IResourceClient`) | L1 | Hard | Register/unregister character, realm, location references; define cleanup callbacks |

**DI Provider Interface**: Implements `IVariableProviderFactory` via `RelationshipProviderFactory` — exposes `${relationship.*}` variables to Actor (L2) behavior system. Cache populated via self-calling `IRelationshipClient` through `IServiceScopeFactory`.

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `relationship.created` | `RelationshipCreatedEvent` | CreateRelationship |
| `relationship.updated` | `RelationshipUpdatedEvent` | UpdateRelationship |
| `relationship.deleted` | `RelationshipDeletedEvent` | EndRelationship, CleanupByEntity, MergeRelationshipType (collision) |
| `relationship.type.created` | `RelationshipTypeCreatedEvent` | CreateRelationshipType (also via SeedRelationshipTypes) |
| `relationship.type.updated` | `RelationshipTypeUpdatedEvent` | UpdateRelationshipType, DeprecateRelationshipType, UndeprecateRelationshipType |
| `relationship.type.deleted` | `RelationshipTypeDeletedEvent` | DeleteRelationshipType (also via MergeRelationshipType with deleteAfterMerge) |
| `relationship.type.merged` | `RelationshipTypeMergedEvent` | MergeRelationshipType |

---

## Events Consumed

| Topic | Handler | Action |
|-------|---------|--------|
| `relationship.created` | `HandleRelationshipCreatedAsync` | Invalidates `IRelationshipDataCache` for entity1 and entity2 if Character type |
| `relationship.updated` | `HandleRelationshipUpdatedAsync` | Same cache invalidation |
| `relationship.deleted` | `HandleRelationshipDeletedAsync` | Same cache invalidation |

Self-subscriptions only. The service subscribes to its own events to keep the Singleton `RelationshipDataCache` (used by the ABML variable provider) consistent. Only Character-type entity caches are invalidated.

---

## DI Services

| Service | Role |
|---------|------|
| `ILogger<RelationshipService>` | Structured logging |
| `RelationshipServiceConfiguration` | 5 config properties (hierarchy depth, migration errors, lock timeout, provider page size, provider cache TTL) |
| `IStateStoreFactory` | State store access (constructor only, not stored as field) |
| `IDistributedLockProvider` | Distributed locking |
| `IMessageBus` | Event publishing |
| `IEventConsumer` | Event handler registration |
| `ITelemetryProvider` | Span instrumentation |
| `IResourceClient` | Reference tracking for character, realm, location entities |
| `IRelationshipDataCache` | In-memory TTL cache for ABML variable provider |
| `RelationshipProviderFactory` | `IVariableProviderFactory` impl (Singleton, registered in plugin) |
| `RelationshipDataCache` | Singleton cache impl; self-calls via `IRelationshipClient` + `IServiceScopeFactory` |
| `RelationshipProvider` | `IVariableProvider` impl; exposes `${relationship.has.*}`, `${relationship.count.*}`, `${relationship.total}` |

---

## Method Index

| Method | Route | Roles | Mutates | Publishes |
|--------|-------|-------|---------|-----------|
| CreateRelationship | POST /relationship/create | admin | rel, entity-idx, type-idx, composite | relationship.created |
| GetRelationship | POST /relationship/get | user | - | - |
| ListRelationshipsByEntity | POST /relationship/list-by-entity | user | - | - |
| GetRelationshipsBetween | POST /relationship/get-between | user | - | - |
| ListRelationshipsByType | POST /relationship/list-by-type | user | - | - |
| UpdateRelationship | POST /relationship/update | admin | rel, type-idx | relationship.updated |
| EndRelationship | POST /relationship/end | admin | rel, composite | relationship.deleted |
| CleanupByEntity | POST /relationship/cleanup-by-entity | developer | rel, composite | relationship.deleted |
| GetRelationshipType | POST /relationship-type/get | user | - | - |
| GetRelationshipTypeByCode | POST /relationship-type/get-by-code | user | - | - |
| ListRelationshipTypes | POST /relationship-type/list | user | - | - |
| GetChildRelationshipTypes | POST /relationship-type/get-children | user | - | - |
| MatchesHierarchy | POST /relationship-type/matches-hierarchy | user | - | - |
| GetAncestors | POST /relationship-type/get-ancestors | user | - | - |
| CreateRelationshipType | POST /relationship-type/create | admin | type, code-index, parent-index, all-types | relationship.type.created |
| UpdateRelationshipType | POST /relationship-type/update | admin | type, parent-index | relationship.type.updated |
| DeleteRelationshipType | POST /relationship-type/delete | admin | type, code-index, parent-index, all-types | relationship.type.deleted |
| DeprecateRelationshipType | POST /relationship-type/deprecate | admin | type | relationship.type.updated |
| UndeprecateRelationshipType | POST /relationship-type/undeprecate | admin | type | relationship.type.updated |
| MergeRelationshipType | POST /relationship-type/merge | admin | rel, composite, type-idx | relationship.type.merged, relationship.deleted |
| SeedRelationshipTypes | POST /relationship-type/seed | admin | (delegates to Create/Update) | (via Create/Update) |

---

## Methods

### CreateRelationship
POST /relationship/create | Roles: [admin]

```
IF entity1Id == entity2Id AND entity1Type == entity2Type  -> 400
READ type-store:"type:{relationshipTypeId}"               -> 400 if null
IF type.IsDeprecated                                      -> 400
// Build normalized composite key: sort entity keys lexicographically
LOCK lock-store:compositeKey
  READ rel-string-store:compositeKey                      -> 409 if exists
  WRITE rel-model-store:"rel:{newId}" <- RelationshipModel from request
  WRITE rel-string-store:compositeKey <- newId
  // AddToEntityIndexAsync (acquires own lock per index key)
  WRITE rel-index-store:"entity-idx:{entity1Type}:{entity1Id}" += newId
  WRITE rel-index-store:"entity-idx:{entity2Type}:{entity2Id}" += newId
  // AddToTypeIndexAsync (acquires own lock)
  WRITE rel-index-store:"type-idx:{relationshipTypeId}" += newId
  // Register resource references for Character, Realm, Location entities
  IF entity1Type in [Character, Realm, Location]
    CALL IResourceClient.RegisterReferenceAsync(entity1)
  IF entity2Type in [Character, Realm, Location]
    CALL IResourceClient.RegisterReferenceAsync(entity2)
  PUBLISH relationship.created { relationshipId, entity1Id, entity1Type, entity2Id, entity2Type, relationshipTypeId, startedAt, metadata }
RETURN (200, RelationshipResponse)
```

### GetRelationship
POST /relationship/get | Roles: [user]

```
READ rel-model-store:"rel:{relationshipId}"               -> 404 if null
RETURN (200, RelationshipResponse)
```

### ListRelationshipsByEntity
POST /relationship/list-by-entity | Roles: [user]

```
READ rel-index-store:"entity-idx:{entityType}:{entityId}"
IF index empty
  RETURN (200, RelationshipListResponse { empty })
READ (bulk) rel-model-store for all IDs in index
// Filter in memory
IF includeEnded != true: exclude ended relationships
IF relationshipTypeId provided: filter by type
IF otherEntityType provided: filter by other participant's type
// Sort by CreatedAt descending, then paginate in memory
RETURN (200, RelationshipListResponse { filtered, paginated })
```

### GetRelationshipsBetween
POST /relationship/get-between | Roles: [user]

```
READ rel-index-store:"entity-idx:{entity1Type}:{entity1Id}"
IF index empty
  RETURN (200, RelationshipListResponse { empty })
READ (bulk) rel-model-store for all IDs in index
// Filter to relationships involving entity2 on either side
IF includeEnded != true: exclude ended
IF relationshipTypeId provided: filter by type
// Sort by CreatedAt descending, paginate in memory
RETURN (200, RelationshipListResponse { filtered, paginated })
```

### ListRelationshipsByType
POST /relationship/list-by-type | Roles: [user]

```
READ rel-index-store:"type-idx:{relationshipTypeId}"
IF index empty
  RETURN (200, RelationshipListResponse { empty })
READ (bulk) rel-model-store for all IDs in index
IF includeEnded != true: exclude ended
IF entity1Type provided: filter by entity1 type
IF entity2Type provided: filter by entity2 type
// Sort by CreatedAt descending, paginate in memory
RETURN (200, RelationshipListResponse { filtered, paginated })
```

### UpdateRelationship
POST /relationship/update | Roles: [admin]

```
LOCK lock-store:"{relationshipId}"                         -> 409 if fails
  READ rel-model-store:"rel:{relationshipId}"              -> 404 if null
  IF model.EndedAt has value                               -> 409 (cannot update ended)
  IF relationshipTypeId provided AND differs from current
    // Migrate type indexes: add-first for crash safety
    WRITE rel-index-store:"type-idx:{newTypeId}" += relationshipId
    WRITE rel-index-store:"type-idx:{oldTypeId}" -= relationshipId
    // Track changed field
  IF metadata provided: update metadata, track changed field
  IF any changes
    WRITE rel-model-store:"rel:{relationshipId}" <- updated model
    PUBLISH relationship.updated { relationshipId, all fields, changedFields }
RETURN (200, RelationshipResponse)
```

### EndRelationship
POST /relationship/end | Roles: [admin]

```
LOCK lock-store:"{relationshipId}"                         -> 409 if fails
  READ rel-model-store:"rel:{relationshipId}"              -> 404 if null
  IF model.EndedAt has value                               -> 409 (already ended)
  // Set EndedAt = request.endedAt ?? UtcNow
  WRITE rel-model-store:"rel:{relationshipId}" <- model with EndedAt
  DELETE rel-string-store:compositeKey
  // Unregister resource references for Character, Realm, Location
  IF entity1Type in [Character, Realm, Location]
    CALL IResourceClient.UnregisterReferenceAsync(entity1)
  IF entity2Type in [Character, Realm, Location]
    CALL IResourceClient.UnregisterReferenceAsync(entity2)
  PUBLISH relationship.deleted { relationshipId, all fields, deletedReason: reason ?? "Relationship ended" }
RETURN (200)
```

### CleanupByEntity
POST /relationship/cleanup-by-entity | Roles: [developer]

```
READ rel-index-store:"entity-idx:{entityType}:{entityId}"
IF index empty
  RETURN (200, CleanupByEntityResponse { ended: 0, alreadyEnded: 0 })
READ (bulk) rel-model-store for all IDs in index
FOREACH relationship in results
  IF model.EndedAt has value: increment alreadyEnded, skip
  LOCK lock-store:"{relationshipId}"
    // Skip if lock fails (warning log, non-fatal)
    READ rel-model-store:"rel:{relationshipId}"  // Re-read under lock
    IF model.EndedAt has value: increment alreadyEnded, skip
    WRITE rel-model-store:"rel:{relationshipId}" <- model with EndedAt
    DELETE rel-string-store:compositeKey
    // Unregister resource references for Character, Realm, Location
    IF entity1Type in [Character, Realm, Location]
      CALL IResourceClient.UnregisterReferenceAsync(entity1)
    IF entity2Type in [Character, Realm, Location]
      CALL IResourceClient.UnregisterReferenceAsync(entity2)
    PUBLISH relationship.deleted { relationshipId, deletedReason: "Entity deleted (cascade cleanup)" }
    // Increment endedCount
RETURN (200, CleanupByEntityResponse { ended, alreadyEnded })
```

### GetRelationshipType
POST /relationship-type/get | Roles: [user]

```
READ type-model-store:"type:{relationshipTypeId}"          -> 404 if null
RETURN (200, RelationshipTypeResponse)
```

### GetRelationshipTypeByCode
POST /relationship-type/get-by-code | Roles: [user]

```
READ type-string-store:"code-index:{code.ToUpperInvariant()}"
IF null or empty or Guid parse fails                       -> 404
READ type-model-store:"type:{typeId}"                      -> 404 if null
RETURN (200, RelationshipTypeResponse)
```

### ListRelationshipTypes
POST /relationship-type/list | Roles: [user]

```
READ type-index-store:"all-types"
IF index empty
  RETURN (200, RelationshipTypeListResponse { empty })
READ (bulk) type-model-store for all IDs in index
IF includeDeprecated != true: exclude deprecated
IF category provided: filter by category (case-insensitive)
IF rootsOnly == true: filter to types with no parent
RETURN (200, RelationshipTypeListResponse { filtered })
```

### GetChildRelationshipTypes
POST /relationship-type/get-children | Roles: [user]

```
READ type-model-store:"type:{parentTypeId}"                -> 404 if null
// GetChildTypeIdsAsync: reads parent-index, optionally recurses
IF recursive
  // Traverse parent-index entries recursively up to MaxHierarchyDepth
  READ type-index-store:"parent-index:{parentId}" per level
ELSE
  READ type-index-store:"parent-index:{parentTypeId}"
IF no children
  RETURN (200, RelationshipTypeListResponse { empty })
READ (bulk) type-model-store for all child IDs
RETURN (200, RelationshipTypeListResponse)
```

### MatchesHierarchy
POST /relationship-type/matches-hierarchy | Roles: [user]

```
IF typeId == ancestorTypeId
  RETURN (200, MatchesHierarchyResponse { depth: 0 })
READ type-model-store:"type:{typeId}"                      -> 404 if null
READ type-model-store:"type:{ancestorTypeId}"              -> 404 if null
// Walk parent chain from typeId upward
FOREACH depth in 1..MaxHierarchyDepth
  READ type-model-store:"type:{currentParentId}"
  IF currentParentId == ancestorTypeId
    RETURN (200, MatchesHierarchyResponse { depth })
  IF no parent: break
RETURN (404)
// T8: 200 = matches (with depth), 404 = no match. No `matches` boolean needed.
```

### GetAncestors
POST /relationship-type/get-ancestors | Roles: [user]

```
READ type-model-store:"type:{typeId}"                      -> 404 if null
// Walk parent chain collecting ancestors
FOREACH depth in 1..MaxHierarchyDepth
  READ type-model-store:"type:{parentId}"
  IF null: break  // Handles inconsistent hierarchy gracefully
  // Add to ancestors list
  IF no further parent: break
RETURN (200, RelationshipTypeListResponse { ancestors from immediate parent to root })
```

### CreateRelationshipType
POST /relationship-type/create | Roles: [admin]

```
// Normalize code to uppercase
LOCK lock-store:"code-index:{code}"                        -> 409 if fails
  READ type-string-store:"code-index:{code}"               -> 409 if exists
  IF parentTypeId provided
    READ type-model-store:"type:{parentTypeId}"            -> 400 if null
    // depth = parent.Depth + 1
  IF inverseTypeCode provided
    READ type-string-store:"code-index:{inverseCode.ToUpperInvariant()}"
    // Resolve inverseTypeId; null if not found (no error)
  WRITE type-model-store:"type:{newId}" <- RelationshipTypeModel
  WRITE type-string-store:"code-index:{code}" <- newId
  IF parentTypeId provided
    // AddToRtParentIndexAsync (acquires own lock)
    WRITE type-index-store:"parent-index:{parentTypeId}" += newId
  // AddToRtAllTypesListAsync (acquires own lock)
  WRITE type-index-store:"all-types" += newId
  PUBLISH relationship.type.created { relationshipTypeId, code, name, category, parentTypeId, isBidirectional, depth }
RETURN (200, RelationshipTypeResponse)
```

### UpdateRelationshipType
POST /relationship-type/update | Roles: [admin]

```
LOCK lock-store:"{relationshipTypeId}"                     -> 409 if fails
  READ type-model-store:"type:{relationshipTypeId}"        -> 404 if null
  // Track changedFields
  IF name provided and differs: update, track
  IF description provided: update, track
  IF category provided: update, track
  IF isBidirectional provided and differs: update, track
  IF metadata provided: update, track
  IF parentTypeId provided and differs from current
    READ type-model-store:"type:{newParentId}"             -> 400 if null
    // WouldCreateCycleAsync: walks ancestor chain of new parent up to MaxHierarchyDepth
    IF cycle detected                                      -> 400
    // Update parent indexes
    IF old parent existed
      WRITE type-index-store:"parent-index:{oldParentId}" -= typeId
    WRITE type-index-store:"parent-index:{newParentId}" += typeId
    // Recalculate depth
  IF inverseTypeCode provided
    READ type-string-store:"code-index:{inverseCode.ToUpperInvariant()}"
    // Update inverseTypeId and inverseTypeCode
  IF any changedFields
    WRITE type-model-store:"type:{relationshipTypeId}" <- updated model
    PUBLISH relationship.type.updated { all fields, changedFields }
RETURN (200, RelationshipTypeResponse)
```

### DeleteRelationshipType
POST /relationship-type/delete | Roles: [admin]

```
LOCK lock-store:"{relationshipTypeId}"                     -> 409 if fails
  READ type-model-store:"type:{relationshipTypeId}"        -> 404 if null
  IF not deprecated                                        -> 400 (must deprecate first)
  // Check for existing relationships (including ended)
  // Internal call: ListRelationshipsByTypeAsync with includeEnded=true, pageSize=1
  IF any relationships exist                               -> 409
  // Check for child types
  // Internal call: GetChildTypeIdsAsync(typeId, recursive: false)
  IF children exist                                        -> 409
  DELETE type-model-store:"type:{relationshipTypeId}"
  DELETE type-string-store:"code-index:{code}"
  IF type had parent
    WRITE type-index-store:"parent-index:{parentId}" -= typeId
  WRITE type-index-store:"all-types" -= typeId
  PUBLISH relationship.type.deleted { relationshipTypeId, code }
RETURN (200)
```

### DeprecateRelationshipType
POST /relationship-type/deprecate | Roles: [admin]

```
LOCK lock-store:"{relationshipTypeId}"                     -> 409 if fails
  READ type-model-store:"type:{relationshipTypeId}"        -> 404 if null
  IF already deprecated
    RETURN (200, RelationshipTypeResponse)  // Idempotent
  // Set IsDeprecated=true, DeprecatedAt=UtcNow, DeprecationReason=body.Reason
  WRITE type-model-store:"type:{relationshipTypeId}" <- updated model
  PUBLISH relationship.type.updated { all fields, changedFields: [isDeprecated, deprecatedAt, deprecationReason] }
RETURN (200, RelationshipTypeResponse)
```

### UndeprecateRelationshipType
POST /relationship-type/undeprecate | Roles: [admin]

```
LOCK lock-store:"{relationshipTypeId}"                     -> 409 if fails
  READ type-model-store:"type:{relationshipTypeId}"        -> 404 if null
  IF not deprecated
    RETURN (200, RelationshipTypeResponse)  // Idempotent
  // Clear IsDeprecated=false, DeprecatedAt=null, DeprecationReason=null
  WRITE type-model-store:"type:{relationshipTypeId}" <- updated model
  PUBLISH relationship.type.updated { all fields, changedFields: [isDeprecated, deprecatedAt, deprecationReason] }
RETURN (200, RelationshipTypeResponse)
```

### MergeRelationshipType
POST /relationship-type/merge | Roles: [admin]

```
READ type-model-store:"type:{sourceTypeId}"                -> 404 if null
IF source not deprecated                                   -> 400
READ type-model-store:"type:{targetTypeId}"                -> 404 if null
IF target is deprecated                                    -> 409
LOCK lock-store:"type-idx:{sourceTypeId}"                  -> 409 if fails
  LOCK lock-store:"type-idx:{targetTypeId}"                -> 409 if fails
    READ rel-index-store:"type-idx:{sourceTypeId}"
    READ rel-index-store:"type-idx:{targetTypeId}"
    IF source index empty
      // Skip migration, go to summary event
    READ (bulk) rel-model-store for all source relationship IDs
    FOREACH relationship in source relationships
      LOCK lock-store:"{relationshipId}"
        // Skip if lock fails (track error)
        READ rel-model-store:"rel:{relationshipId}"  // Re-read under lock
        DELETE rel-string-store:oldCompositeKey
        IF relationship is active (not ended)
          READ rel-string-store:newCompositeKey  // Collision check
          IF collision exists
            // End as duplicate
            WRITE rel-model-store:"rel:{relationshipId}" <- ended model
            PUBLISH relationship.deleted { deletedReason: "Duplicate detected during merge" }
            // Track as migrated (collision)
          ELSE
            WRITE rel-string-store:newCompositeKey <- relationshipId
            WRITE rel-model-store:"rel:{relationshipId}" <- model with targetTypeId
            // Track as migrated (success)
        ELSE
          // Ended relationship: just update type, no composite key
          WRITE rel-model-store:"rel:{relationshipId}" <- model with targetTypeId
          // Track as migrated
    // Batch update type indexes
    WRITE rel-index-store:"type-idx:{targetTypeId}" += migrated IDs
    WRITE rel-index-store:"type-idx:{sourceTypeId}" -= migrated + collision IDs
    PUBLISH relationship.type.merged { sourceTypeId, targetTypeId, migratedCount, failedCount, sourceDeleted }
    IF deleteAfterMerge AND failedCount == 0
      // Internal call: DeleteRelationshipTypeAsync(sourceTypeId)
RETURN (200, MergeRelationshipTypeResponse { migrated, failed, errors, sourceDeleted })
```

### SeedRelationshipTypes
POST /relationship-type/seed | Roles: [admin]

```
// Load existing code-to-ID mappings for all seed types
FOREACH type in request.types
  READ type-string-store:"code-index:{code.ToUpperInvariant()}"
// Multi-pass topological ordering (parents before children)
// maxIterations = pending.Count * 2 (infinite loop guard)
FOREACH pass in 1..maxIterations
  FOREACH pending type
    IF parent code specified AND parent not yet created: defer to next pass
    IF type exists AND updateExisting != true: skip (increment skipped)
    IF type exists AND updateExisting
      // Internal call: UpdateRelationshipTypeAsync
    ELSE
      // Internal call: CreateRelationshipTypeAsync
    // Track created/updated; catch exceptions per-type
  IF no pending types remain: break
// Types remaining after max iterations: add to errors
RETURN (200, SeedRelationshipTypesResponse { created, updated, skipped, errors })
```

---

## Background Services

No background services.
