# Character Implementation Map

> **Plugin**: lib-character
> **Schema**: schemas/character-api.yaml
> **Layer**: GameFoundation
> **Deep Dive**: [docs/plugins/CHARACTER.md](../plugins/CHARACTER.md)

---

## Summary

| Field | Value |
|-------|-------|
| Plugin | lib-character |
| Layer | L2 GameFoundation |
| Endpoints | 12 |
| State Stores | character-statestore (MySQL), character-lock (Redis) |
| Events Published | 6 (character.created, character.updated, character.deleted, character.compressed, character.realm.joined, character.realm.left) |
| Events Consumed | 0 |
| Client Events | 2 (character.updated, character.realm-transferred) |
| Background Services | 0 |

---

## State

**Store**: `character-statestore` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `character:{realmId}:{characterId}` | `CharacterModel` | Full character data (realm-partitioned) |
| `realm-index:{realmId}` | `List<string>` | Character IDs in a realm (for index management) |
| `character-global-index:{characterId}` | `string` | Character ID to realm ID mapping (for ID-only lookups) |
| `archive:{characterId}` | `CharacterArchiveModel` | Compressed character text summaries |
| `refcount:{characterId}` | `RefCountData` | Cleanup eligibility tracking (zero-ref timestamp) |

**Lock Store**: `character-lock` (Backend: Redis)

Used by `IDistributedLockProvider` for update, transfer, and compress operations. Key: `characterId`.

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (`IStateStoreFactory`) | L0 | Hard | MySQL persistence for character data, indexes, archives, refcount |
| lib-state (`IDistributedLockProvider`) | L0 | Hard | Distributed locks for update, transfer, and compress |
| lib-messaging (`IMessageBus`) | L0 | Hard | Publishing character lifecycle events |
| lib-messaging (`IEventConsumer`) | L0 | Hard | Event handler registration (no handlers currently) |
| lib-telemetry (`ITelemetryProvider`) | L0 | Hard | Span instrumentation on async helpers |
| lib-contract (`IContractClient`) | L1 | Hard | Contract reference counting during check-references |
| lib-resource (`IResourceClient`) | L1 | Hard | L4 reference checking and cleanup execution during delete |
| lib-connect (`IEntitySessionRegistry`) | L1 | Hard | Client event push to WebSocket sessions observing a character |
| lib-realm (`IRealmClient`) | L2 | Hard | Realm validation for create and transfer |
| lib-species (`ISpeciesClient`) | L2 | Hard | Species validation for create |
| lib-relationship (`IRelationshipClient`) | L2 | Hard | Family tree construction, reference counting, type code lookup |

**Plugin startup** registers `CharacterBaseTemplate` with `IResourceTemplateRegistry` and compression callback with lib-resource via `CharacterCompressionCallbacks.RegisterAsync`.

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `character.created` | `CharacterCreatedEvent` | CreateCharacter |
| `character.updated` | `CharacterUpdatedEvent` | UpdateCharacter (when fields changed), TransferCharacterToRealm |
| `character.deleted` | `CharacterDeletedEvent` | DeleteCharacter |
| `character.realm.joined` | `CharacterRealmJoinedEvent` | CreateCharacter, TransferCharacterToRealm |
| `character.realm.left` | `CharacterRealmLeftEvent` | DeleteCharacter (reason: Deletion), TransferCharacterToRealm (reason: Transfer) |
| `character.compressed` | `CharacterCompressedEvent` | CompressCharacter |

---

## Events Consumed

This plugin does not consume external events.

---

## DI Services

| Service | Role |
|---------|------|
| `ILogger<CharacterService>` | Structured logging |
| `CharacterServiceConfiguration` | Pagination limits, retry counts, lock timeout, grace period |
| `IStateStoreFactory` | State store access (constructor-cached into 6 readonly store fields) |
| `IDistributedLockProvider` | Distributed locking for character modifications |
| `IMessageBus` | Event publishing |
| `IEventConsumer` | Event handler registration (no handlers) |
| `ITelemetryProvider` | Span instrumentation |
| `IRealmClient` | Realm existence and status validation |
| `ISpeciesClient` | Species existence and realm assignment validation |
| `IRelationshipClient` | Family tree, reference counting, type code lookup |
| `IContractClient` | Contract reference detection |
| `IResourceClient` | L4 reference checking and cascade cleanup |
| `IEntitySessionRegistry` | Client event push to entity-observing sessions |

---

## Method Index

| Method | Route | Source | Roles | Mutates | Publishes |
|--------|-------|--------|-------|---------|-----------|
| CreateCharacter | POST /character/create | generated | admin | character, realm-index, global-index | character.created, character.realm.joined |
| GetCharacter | POST /character/get | generated | user | - | - |
| UpdateCharacter | POST /character/update | generated | admin | character | character.updated + client push |
| DeleteCharacter | POST /character/delete | generated | admin | character, realm-index, global-index | character.realm.left, character.deleted |
| ListCharacters | POST /character/list | generated | user | - | - |
| GetEnrichedCharacter | POST /character/get-enriched | generated | user | - | - |
| CompressCharacter | POST /character/compress | generated | admin | archive | character.compressed |
| GetCharacterArchive | POST /character/get-archive | generated | user | - | - |
| CheckCharacterReferences | POST /character/check-references | generated | admin | refcount | - |
| GetCharactersByRealm | POST /character/by-realm | generated | user | - | - |
| TransferCharacterToRealm | POST /character/transfer-realm | generated | admin | character, realm-index, global-index | character.realm.left, character.realm.joined, character.updated + client push |
| GetCompressData | POST /character/get-compress-data | generated | [] | - | - |

---

## Methods

### CreateCharacter
POST /character/create | Roles: [admin]

```
CALL IRealmClient.RealmExistsAsync(realmId)           -> 400 if not found or deprecated
CALL ISpeciesClient.GetSpeciesAsync(speciesId)         -> 400 if not found or not in realm
// Auto-set DeathDate if Status=Dead and no DeathDate provided
WRITE character:{realmId}:{characterId} <- new CharacterModel from request
// AddCharacterToRealmIndex (ETag retry loop up to RealmIndexUpdateMaxRetries):
READ realm-index:{realmId} [with ETag]
ETAG-WRITE realm-index:{realmId} <- list with characterId added
WRITE character-global-index:{characterId} <- realmId
PUBLISH character.created { characterId, name, realmId, speciesId, birthDate, status }
PUBLISH character.realm.joined { characterId, realmId, previousRealmId: null }
RETURN (200, CharacterResponse)
```

### GetCharacter
POST /character/get | Roles: [user]

```
// FindCharacterById: two-step lookup
READ character-global-index:{characterId}              -> 404 if null
READ character:{realmId}:{characterId}                 -> 404 if null
RETURN (200, CharacterResponse)
```

### UpdateCharacter
POST /character/update | Roles: [admin]

```
LOCK character-lock:{characterId}                      -> 409 if fails
READ character-global-index:{characterId}              -> 404 if null
READ character:{realmId}:{characterId}                 -> 404 if null
// Apply partial update: only fields present in request
// DeathDate set -> auto-set Status=Dead; Status=Dead -> auto-set DeathDate
WRITE character:{realmId}:{characterId} <- mutated CharacterModel
IF changedFields.Count > 0
  PUBLISH character.updated { characterId, name, realmId, speciesId, status, changedFields }
  PUSH character.updated to entity sessions { characterId, changedFields, name?, status?, deathDate? }
RETURN (200, CharacterResponse)
```

### DeleteCharacter
POST /character/delete | Roles: [admin]

```
READ character-global-index:{characterId}              -> 404 if null
READ character:{realmId}:{characterId}                 -> 404 if null
CALL IResourceClient.CheckReferencesAsync("character", characterId)
// ApiException 404 from CheckReferences = no references (normal case)
// Any other ApiException = fail-closed -> 503
IF referenceCount > 0
  CALL IResourceClient.ExecuteCleanupAsync("character", characterId, AllRequired)
                                                       -> 409 if cleanup blocked
DELETE character:{realmId}:{characterId}
// RemoveCharacterFromRealmIndex (ETag retry loop):
READ realm-index:{realmId} [with ETag]
ETAG-WRITE realm-index:{realmId} <- list with characterId removed
DELETE character-global-index:{characterId}
PUBLISH character.realm.left { characterId, realmId, reason: Deletion }
PUBLISH character.deleted { characterId, name, realmId, speciesId, status }
RETURN (200)
```

### ListCharacters
POST /character/list | Roles: [user]

```
// Clamp pageSize to MaxPageSize; default to DefaultPageSize if <= 0
QUERY character-statestore WHERE $.RealmId = realmId
  [AND $.Status = status] [AND $.SpeciesId = speciesId]
  ORDER BY $.Name ASC PAGED(offset, pageSize)
RETURN (200, CharacterListResponse { characters, totalCount, page, pageSize, hasNextPage, hasPreviousPage })
```

### GetEnrichedCharacter
POST /character/get-enriched | Roles: [user]

```
READ character-global-index:{characterId}              -> 404 if null
READ character:{realmId}:{characterId}                 -> 404 if null
IF includeFamilyTree
  // BuildFamilyTree:
  CALL IRelationshipClient.ListRelationshipsByEntityAsync(characterId, Character)
  // ApiException 404 -> empty family tree
  // BuildTypeCodeLookup: parallel calls per unique type ID
  FOREACH (parallel) unique relationshipTypeId
    CALL IRelationshipClient.GetRelationshipTypeAsync(typeId)
    // ApiException -> skip type (logged as warning)
  // BulkLoadCharacters: bulk state reads for related character names
  READ character-global-index:{relatedIds} (bulk)
  READ character:{realmId}:{relatedIds} (bulk)
  // Categorize by type code: Parents, Children, Siblings, Spouses, PastLives
  // INCARNATION: only when queried character is entity2 (past lives, not future)
RETURN (200, EnrichedCharacterResponse { character data, familyTree? })
```

### CompressCharacter
POST /character/compress | Roles: [admin]

```
LOCK character-lock:{characterId}                      -> 409 if fails
READ character-global-index:{characterId}              -> 404 if null
READ character:{realmId}:{characterId}                 -> 404 if null
IF status != Dead OR deathDate == null                 -> 400
// GenerateFamilySummary (reuses BuildFamilyTree):
CALL IRelationshipClient.ListRelationshipsByEntityAsync(characterId, Character)
FOREACH (parallel) unique relationshipTypeId
  CALL IRelationshipClient.GetRelationshipTypeAsync(typeId)
READ character-global-index:{relatedIds} (bulk)
READ character:{realmId}:{relatedIds} (bulk)
// L4 fields (personalitySummary, keyBackstoryPoints, majorLifeEvents) = null
WRITE archive:{characterId} <- CharacterArchiveModel from character + family summary
PUBLISH character.compressed { characterId, deletedSourceData }
RETURN (200, CharacterArchive)
```

### GetCharacterArchive
POST /character/get-archive | Roles: [user]

```
READ archive:{characterId}                             -> 404 if null
RETURN (200, CharacterArchive)
```

### CheckCharacterReferences
POST /character/check-references | Roles: [admin]

```
READ character-global-index:{characterId}              -> 404 if null
READ character:{realmId}:{characterId}                 -> 404 if null
READ archive:{characterId}                             // isCompressed flag
// Three independent reference checks (each soft-fails on error):
CALL IRelationshipClient.ListRelationshipsByEntityAsync(characterId, Character)
  // 404 -> 0 references; other error -> log warning, skip
CALL IResourceClient.CheckReferencesAsync("character", characterId)
  // 404 -> 0 references; other error -> log warning, skip
CALL IContractClient.QueryContractInstancesAsync(characterId, Character, pageSize: 1)
  // 404 -> 0 references; HasMore -> add 2 (approximation); other error -> log warning, skip
// Refcount tracking with ETag retry loop (up to RefCountUpdateMaxRetries):
READ refcount:{characterId} [with ETag]
IF referenceCount changed (zeroRefSince needs update)
  ETAG-WRITE refcount:{characterId} <- updated RefCountData
// Eligibility: isCompressed AND refCount == 0 AND zeroRefSince elapsed >= CleanupGracePeriodDays
RETURN (200, CharacterRefCount { characterId, referenceCount, referenceTypes, isCompressed, isEligibleForCleanup, zeroRefSince })
```

### GetCharactersByRealm
POST /character/by-realm | Roles: [user]

```
// Identical to ListCharacters — same internal delegate
QUERY character-statestore WHERE $.RealmId = realmId
  [AND $.Status = status] [AND $.SpeciesId = speciesId]
  ORDER BY $.Name ASC PAGED(offset, pageSize)
RETURN (200, CharacterListResponse { characters, totalCount, page, pageSize, hasNextPage, hasPreviousPage })
```

### TransferCharacterToRealm
POST /character/transfer-realm | Roles: [admin]

```
CALL IRealmClient.RealmExistsAsync(targetRealmId)      -> 404 if not found; 400 if deprecated
LOCK character-lock:{characterId}                      -> 409 if fails
READ character-global-index:{characterId}              -> 404 if null
READ character:{previousRealmId}:{characterId}         -> 404 if null
IF character already in targetRealm                    -> 400
// Re-key character data to new realm:
DELETE character:{previousRealmId}:{characterId}
// RemoveCharacterFromRealmIndex (ETag retry loop):
READ realm-index:{previousRealmId} [with ETag]
ETAG-WRITE realm-index:{previousRealmId} <- list with characterId removed
DELETE character-global-index:{characterId}
// Save to new realm:
WRITE character:{targetRealmId}:{characterId} <- updated CharacterModel
// AddCharacterToRealmIndex (ETag retry loop):
READ realm-index:{targetRealmId} [with ETag]
ETAG-WRITE realm-index:{targetRealmId} <- list with characterId added
WRITE character-global-index:{characterId} <- targetRealmId
PUBLISH character.realm.left { characterId, realmId: previousRealmId, reason: Transfer }
PUBLISH character.realm.joined { characterId, realmId: targetRealmId, previousRealmId }
PUBLISH character.updated { characterId, changedFields: ["realmId"] }
PUSH character.realm-transferred to entity sessions { characterId, previousRealmId, newRealmId }
RETURN (200, CharacterResponse)
```

### GetCompressData
POST /character/get-compress-data | Roles: []

```
READ character-global-index:{characterId}              -> 404 if null
READ character:{realmId}:{characterId}                 -> 404 if null
IF status != Dead OR deathDate == null                 -> 400
// GenerateFamilySummary (same as CompressCharacter):
CALL IRelationshipClient.ListRelationshipsByEntityAsync(characterId, Character)
FOREACH (parallel) unique relationshipTypeId
  CALL IRelationshipClient.GetRelationshipTypeAsync(typeId)
READ character-global-index:{relatedIds} (bulk)
READ character:{realmId}:{relatedIds} (bulk)
// Returns L2-only data; L4 services provide their own callbacks
RETURN (200, CharacterBaseArchive { resourceId, resourceType, characterId, name, realmId, speciesId, birthDate, deathDate, status, familySummary, createdAt })
```

---

## Background Services

No background services.

---

## Non-Standard Implementation Patterns

#### OnRunningAsync

```
// Register resource template for ABML ${candidate.character.*} validation
CALL IResourceTemplateRegistry.Register(CharacterBaseTemplate)
// Register compression callback with lib-resource
CALL IResourceClient via CharacterCompressionCallbacks.RegisterAsync()
  // Points lib-resource at POST /character/get-compress-data (priority: 0)
```
