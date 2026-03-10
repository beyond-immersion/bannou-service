# Collection Implementation Map

> **Plugin**: lib-collection
> **Schema**: schemas/collection-api.yaml
> **Layer**: GameFoundation
> **Deep Dive**: [docs/plugins/COLLECTION.md](../plugins/COLLECTION.md)

---

## Summary

| Field | Value |
|-------|-------|
| Plugin | lib-collection |
| Layer | L2 GameFoundation |
| Endpoints | 23 |
| State Stores | collection-entry-templates (MySQL), collection-instances (MySQL), collection-area-content-configs (MySQL), collection-cache (Redis), collection-lock (Redis) |
| Events Published | 11 (entry-template.created/updated/deleted, created, deleted, area-content-config.created/updated/deleted, entry-unlocked, entry-grant-failed, milestone-reached, discovery-advanced) |
| Events Consumed | 1 (account.deleted) |
| Client Events | 3 (entry.unlocked, milestone-reached, discovery.advanced) |
| Background Services | 0 |

---

## State

**Store**: `collection-entry-templates` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `tpl:{entryTemplateId}` | `EntryTemplateModel` | Primary lookup by ID |
| `tpl:{gameServiceId}:{collectionType}:{code}` | `EntryTemplateModel` | Code-uniqueness lookup within type + game |

**Store**: `collection-instances` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `col:{collectionId}` | `CollectionInstanceModel` | Primary lookup by ID |
| `col:{ownerId}:{ownerType}:{gameServiceId}:{collectionType}` | `CollectionInstanceModel` | Owner+type uniqueness lookup |

**Store**: `collection-area-content-configs` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `acc:{areaConfigId}` | `AreaContentConfigModel` | Primary lookup by ID |
| `acc:{gameServiceId}:{collectionType}:{areaCode}` | `AreaContentConfigModel` | Area code lookup within game + type |

**Store**: `collection-cache` (Backend: Redis)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `cache:{collectionId}` | `CollectionCacheModel` | Cached unlocked entries per collection (TTL-based) |
| `global-unlocks:{gameServiceId}:{collectionType}` | Redis Set (string members) | Tracks first-global unlock per entry code via SADD atomicity |

**Store**: `collection-lock` (Backend: Redis)

Used for distributed locks on mutation operations. Lock keys use `tpl:{id}` for template operations and `col:{id}` for collection operations.

---

## Dependencies

| Dependency | Layer | Type | Usage |
|------------|-------|------|-------|
| lib-state (`IStateStoreFactory`) | L0 | Hard | 4 stores + lock store |
| lib-state (`IDistributedLockProvider`) | L0 | Hard | Locks for template update/delete, collection delete, grant, metadata update, discovery |
| lib-messaging (`IMessageBus`) | L0 | Hard | Publishing 11 event topics |
| lib-telemetry (`ITelemetryProvider`) | L0 | Hard | Span instrumentation on async helpers |
| lib-resource (`IResourceClient`) | L1 | Hard | Character reference tracking, cascade cleanup callback registration |
| lib-connect (`IEntitySessionRegistry`) | L1 | Hard | Client event push to owner WebSocket sessions |
| lib-inventory (`IInventoryClient`) | L2 | Hard | Container create/delete, contents retrieval for cache rebuild |
| lib-item (`IItemClient`) | L2 | Hard | Item template validation, item instance creation on grant |
| lib-game-service (`IGameServiceClient`) | L2 | Hard | Game service existence validation |

**DI Listener interface**: Collection discovers `IEnumerable<ICollectionUnlockListener>` at construction. After each successful grant, dispatches `OnEntryUnlockedAsync` to all registered listeners with per-listener error isolation. Known implementors: `SeedCollectionUnlockListener` (lib-seed, L2), `FactionCollectionUnlockListener` (lib-faction, L4).

**Cleanup patterns**: Character-owned collections use lib-resource cascade callback (`/collection/cleanup-by-character`). Account-owned collections use `account.deleted` event subscription (Account Deletion Cleanup Obligation).

---

## Events Published

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `collection.entry-template.created` | `CollectionEntryTemplateCreatedEvent` | CreateEntryTemplate, SeedEntryTemplates |
| `collection.entry-template.updated` | `CollectionEntryTemplateUpdatedEvent` | UpdateEntryTemplate (only if fields changed), DeprecateEntryTemplate |
| `collection.created` | `CollectionCreatedEvent` | CreateCollection, GrantEntry (auto-create) |
| `collection.deleted` | `CollectionDeletedEvent` | DeleteCollection, CleanupByCharacter, HandleAccountDeleted |
| `collection.area-content-config.created` | `CollectionAreaContentConfigCreatedEvent` | SetAreaContentConfig (new config) |
| `collection.area-content-config.updated` | `CollectionAreaContentConfigUpdatedEvent` | SetAreaContentConfig (existing config) |
| `collection.area-content-config.deleted` | `CollectionAreaContentConfigDeletedEvent` | DeleteAreaContentConfig |
| `collection.entry-unlocked` | `CollectionEntryUnlockedEvent` | GrantEntry (new unlock only) |
| `collection.entry-grant-failed` | `CollectionEntryGrantFailedEvent` | GrantEntry (template-not-found, template-deprecated, max-entries, item-creation-failed) |
| `collection.milestone-reached` | `CollectionMilestoneReachedEvent` | GrantEntry (25/50/75/100% thresholds) |
| `collection.discovery-advanced` | `CollectionDiscoveryAdvancedEvent` | AdvanceDiscovery |

Note: `collection.entry-template.deleted` is defined in the lifecycle schema but has no implementation trigger. Entry templates are Category B (deprecate-only, no delete) — deprecation publishes `entry-template.updated` with `changedFields`.

---

## Events Consumed

| Topic | Handler | Action |
|-------|---------|--------|
| `account.deleted` | `HandleAccountDeletedAsync` | Deletes all account-owned collections, their containers, and cache entries via CleanupCollectionsForOwner |

---

## DI Services

| Service | Role |
|---------|------|
| `ILogger<CollectionService>` | Structured logging |
| `CollectionServiceConfiguration` | Typed configuration (limits, TTLs, page sizes, retry counts) |
| `IStateStoreFactory` | State store access (4 stores + lock store, lazy-initialized) |
| `IMessageBus` | Event publishing |
| `IEventConsumer` | Event subscription registration (account.deleted) |
| `IDistributedLockProvider` | Distributed lock acquisition |
| `ITelemetryProvider` | Distributed tracing spans |
| `IInventoryClient` | Container lifecycle and contents retrieval |
| `IItemClient` | Item template validation and instance creation |
| `IGameServiceClient` | Game service existence validation |
| `IResourceClient` | Character reference tracking and cleanup callback registration |
| `IEntitySessionRegistry` | Client event push to owner WebSocket sessions |
| `IEnumerable<ICollectionUnlockListener>` | DI-discovered unlock notification listeners |

---

## Method Index

| Method | Route | Roles | Mutates | Publishes |
|--------|-------|-------|---------|-----------|
| CreateEntryTemplate | POST /collection/entry-template/create | developer | template (id + code keys) | entry-template.created |
| GetEntryTemplate | POST /collection/entry-template/get | developer | - | - |
| ListEntryTemplates | POST /collection/entry-template/list | developer | - | - |
| UpdateEntryTemplate | POST /collection/entry-template/update | developer | template (id + code keys) | entry-template.updated |
| DeprecateEntryTemplate | POST /collection/entry-template/deprecate | developer | template (id + code keys) | entry-template.updated |
| SeedEntryTemplates | POST /collection/entry-template/seed | developer | template (id + code keys) | entry-template.created |
| CreateCollection | POST /collection/create | developer | collection (id + owner keys), container | created |
| GetCollection | POST /collection/get | user | cache (rebuild on miss) | - |
| ListCollections | POST /collection/list | user | - | - |
| DeleteCollection | POST /collection/delete | developer | collection (id + owner keys), cache, container | deleted |
| GrantEntry | POST /collection/grant | user | collection (auto-create), cache, item, global-unlocks set | entry-unlocked, entry-grant-failed, milestone-reached, created |
| HasEntry | POST /collection/has | user | cache (rebuild on miss) | - |
| QueryEntries | POST /collection/query | user | cache (rebuild on miss) | - |
| UpdateEntryMetadata | POST /collection/update-metadata | user | cache | - |
| GetCompletionStats | POST /collection/stats | user | cache (rebuild on miss) | - |
| SelectContentForArea | POST /collection/content/select-for-area | user | cache (rebuild on miss) | - |
| SetAreaContentConfig | POST /collection/content/area-config/set | developer | area-config (id + code keys) | area-content-config.created, area-content-config.updated |
| GetAreaContentConfig | POST /collection/content/area-config/get | user | - | - |
| ListAreaContentConfigs | POST /collection/content/area-config/list | developer | - | - |
| DeleteAreaContentConfig | POST /collection/content/area-config/delete | developer | area-config (id + code keys) | area-content-config.deleted |
| AdvanceDiscovery | POST /collection/discovery/advance | user | cache | discovery-advanced |
| CleanupByCharacter | POST /collection/cleanup-by-character | developer | collection (id + owner keys), cache, container | deleted |
| CleanDeprecatedEntryTemplates | POST /collection/entry-template/clean-deprecated | admin | template (id + code keys) | entry-template.deleted |

---

## Methods

### CreateEntryTemplate
POST /collection/entry-template/create | Roles: [developer]

```
CALL _gameServiceClient.GetServiceAsync(body.GameServiceId) -> 404 if not found
CALL _itemClient.GetItemTemplateAsync(body.ItemTemplateId) -> 404 if not found
READ EntryTemplateStore:"tpl:{gameServiceId}:{collectionType}:{code}" -> 409 if exists
WRITE EntryTemplateStore:"tpl:{template.EntryTemplateId}" <- EntryTemplateModel from request
WRITE EntryTemplateStore:"tpl:{gameServiceId}:{collectionType}:{code}" <- same model
PUBLISH collection.entry-template.created { entryTemplateId, code, collectionType, gameServiceId, displayName, category, hideWhenLocked, itemTemplateId }
RETURN (200, EntryTemplateResponse)
```

---

### GetEntryTemplate
POST /collection/entry-template/get | Roles: [developer]

```
READ EntryTemplateStore:"tpl:{body.EntryTemplateId}" -> 404 if null
RETURN (200, EntryTemplateResponse)
```

---

### ListEntryTemplates
POST /collection/entry-template/list | Roles: [developer]

```
QUERY EntryTemplateStore WHERE $.CollectionType == body.CollectionType AND $.GameServiceId == body.GameServiceId
// In-memory filter: exclude deprecated templates unless body.IncludeDeprecated == true
// Optional in-memory category filter applied after deprecation filter
// Cursor-based pagination: offset encoded as string, defaults to 0
RETURN (200, ListEntryTemplatesResponse { templates, nextCursor, hasMore })
```

---

### UpdateEntryTemplate
POST /collection/entry-template/update | Roles: [developer]

```
LOCK CollectionLock:"tpl:{body.EntryTemplateId}" -> 409 if fails
 READ EntryTemplateStore:"tpl:{body.EntryTemplateId}" -> 404 if null
 // Apply non-null fields from request, track changedFields list
 IF changedFields is empty
 RETURN (200, EntryTemplateResponse) // no-op
 WRITE EntryTemplateStore:"tpl:{template.EntryTemplateId}" <- updated model
 WRITE EntryTemplateStore:"tpl:{gameServiceId}:{collectionType}:{code}" <- same model
 PUBLISH collection.entry-template.updated { entryTemplateId, code, ..., changedFields }
 RETURN (200, EntryTemplateResponse)
```

---

### DeprecateEntryTemplate
POST /collection/entry-template/deprecate | Roles: [developer]

```
LOCK CollectionLock:"tpl:{body.EntryTemplateId}" -> 409 if fails
 READ EntryTemplateStore:"tpl:{body.EntryTemplateId}" -> 404 if null
 IF template.IsDeprecated // Category B idempotency
 RETURN (200, EntryTemplateResponse)
 // Set deprecation triple-field
 template.IsDeprecated = true
 template.DeprecatedAt = now
 template.DeprecationReason = body.Reason
 template.UpdatedAt = now
 WRITE EntryTemplateStore:"tpl:{template.EntryTemplateId}" <- updated model
 WRITE EntryTemplateStore:"tpl:{gameServiceId}:{collectionType}:{code}" <- same model
 PUBLISH collection.entry-template.updated { ..., changedFields: [isDeprecated, deprecatedAt, deprecationReason] }
 RETURN (200, EntryTemplateResponse)
```

---

### SeedEntryTemplates
POST /collection/entry-template/seed | Roles: [developer]

```
// Phase 1: Validate all unique item template IDs upfront
FOREACH unique itemTemplateId in body.Templates
 CALL _itemClient.GetItemTemplateAsync(itemTemplateId)
 // Collect valid set; invalid templates cause referencing entries to be skipped

// Phase 2: Process each template
FOREACH templateRequest in body.Templates
 IF itemTemplateId not in valid set -> skip (increment skipped)
 READ EntryTemplateStore:"tpl:{gameServiceId}:{collectionType}:{code}" -> skip if exists (increment skipped)
 WRITE EntryTemplateStore:"tpl:{template.EntryTemplateId}" <- EntryTemplateModel from request
 WRITE EntryTemplateStore:"tpl:{gameServiceId}:{collectionType}:{code}" <- same model
 PUBLISH collection.entry-template.created { entryTemplateId, code, ... }
 // increment created

RETURN (200, SeedEntryTemplatesResponse { created, skipped })
```

---

### CreateCollection
POST /collection/create | Roles: [developer]

```
IF body.OwnerType cannot map to ContainerOwnerType -> 400
CALL _gameServiceClient.GetServiceAsync(body.GameServiceId) -> 404 if not found
READ CollectionStore:"col:{ownerId}:{ownerType}:{gameServiceId}:{collectionType}" -> 409 if exists
QUERY CollectionStore WHERE $.OwnerId == body.OwnerId AND $.OwnerType == body.OwnerType
IF count >= config.MaxCollectionsPerOwner -> 409
// CreateCollectionInternalAsync:
 CALL _inventoryClient.CreateContainerAsync({ type: "collection_{collectionType}", ownerType, ownerId, unlimited })
 WRITE CollectionStore:"col:{instance.CollectionId}" <- CollectionInstanceModel
 WRITE CollectionStore:"col:{ownerId}:{ownerType}:{gameServiceId}:{collectionType}" <- same model
 IF ownerType == Character
 CALL _resourceClient.RegisterReferenceAsync(...)
 PUBLISH collection.created { collectionId, ownerId, ownerType, collectionType, gameServiceId, containerId }
RETURN (200, CollectionResponse { entryCount: 0 })
```

---

### GetCollection
POST /collection/get | Roles: [user]

```
READ CollectionStore:"col:{body.CollectionId}" -> 404 if null
// LoadOrRebuildCollectionCacheAsync:
 READ CollectionCache:"cache:{collectionId}"
 IF cache miss
 CALL _inventoryClient.GetContainerAsync({ containerId, includeContents: true })
 // Rebuild cache from container contents
 WRITE CollectionCache:"cache:{collectionId}" <- rebuilt CollectionCacheModel [with TTL]
RETURN (200, CollectionResponse { entryCount: cache.UnlockedEntries.Count })
```

---

### ListCollections
POST /collection/list | Roles: [user]

```
QUERY CollectionStore WHERE $.OwnerId == body.OwnerId AND $.OwnerType == body.OwnerType
// Optional in-memory gameServiceId filter
FOREACH collection in results
 READ CollectionCache:"cache:{collection.CollectionId}"
 // entryCount from cache, or 0 on cache miss (no rebuild triggered)
RETURN (200, ListCollectionsResponse { collections })
```

---

### DeleteCollection
POST /collection/delete | Roles: [developer]

```
LOCK CollectionLock:"col:{body.CollectionId}" -> 409 if fails
 READ CollectionStore:"col:{body.CollectionId}" -> 404 if null
 CALL _inventoryClient.DeleteContainerAsync(collection.ContainerId) // tolerates 404
 DELETE CollectionCache:"cache:{collection.CollectionId}"
 DELETE CollectionStore:"col:{collection.CollectionId}"
 DELETE CollectionStore:"col:{ownerId}:{ownerType}:{gameServiceId}:{collectionType}"
 IF ownerType == Character
 CALL _resourceClient.UnregisterReferenceAsync(...)
 PUBLISH collection.deleted { collectionId, ownerId, ownerType, collectionType, gameServiceId, containerId }
 RETURN (200, CollectionResponse { entryCount: 0 })
```

---

### GrantEntry
POST /collection/grant | Roles: [user]

```
IF body.OwnerType cannot map to ContainerOwnerType -> 400
READ EntryTemplateStore:"tpl:{gameServiceId}:{collectionType}:{entryCode}"
IF template null
 PUBLISH collection.entry-grant-failed { reason: EntryNotFound }
 RETURN (404, null)
IF template.IsDeprecated // Category B instance creation guard
 PUBLISH collection.entry-grant-failed { reason: TemplateDeprecated }
 RETURN (400, null)

READ CollectionStore:"col:{ownerId}:{ownerType}:{gameServiceId}:{collectionType}"
IF collection null
 // Auto-create collection
 QUERY CollectionStore WHERE $.OwnerId == body.OwnerId AND $.OwnerType == body.OwnerType
 IF count >= config.MaxCollectionsPerOwner -> 409
 // CreateCollectionInternalAsync (same as CreateCollection)

LOCK CollectionLock:"col:{collection.CollectionId}" -> 409 if fails
 // LoadOrRebuildCollectionCacheAsync
 READ CollectionCache:"cache:{collectionId}"
 IF cache miss -> rebuild from inventory

 // Idempotency check
 IF entryCode already in cache.UnlockedEntries
 RETURN (200, GrantEntryResponse { alreadyUnlocked: true })

 IF cache.UnlockedEntries.Count >= config.MaxEntriesPerCollection
 PUBLISH collection.entry-grant-failed { reason: MaxEntriesReached }
 RETURN (409, null)

 CALL _itemClient.CreateItemInstanceAsync({ templateId, containerId, realmId: gameServiceId })
 IF item creation fails
 PUBLISH collection.entry-grant-failed { reason: ItemCreationFailed }
 RETURN (500, null)

 // Add entry to cache with ETag retry loop (up to MaxConcurrencyRetries)
 READ CollectionCache:"cache:{collectionId}" [with ETag]
 // Add new UnlockedEntry to cache
 ETAG-WRITE CollectionCache:"cache:{collectionId}" <- updated cache // retry on conflict

 // First-global tracking via Redis set
 SET-ADD CacheableCollectionCache:"global-unlocks:{gameServiceId}:{collectionType}" <- entryCode
 // isFirstGlobal = true if code was newly added to set

 PUBLISH collection.entry-unlocked { collectionId, ownerId, ownerType, entryCode, displayName, isFirstGlobal, ... }
 PUSH CollectionEntryUnlockedClientEvent -> owner entity sessions

 // Milestone check
 QUERY EntryTemplateStore WHERE $.CollectionType AND $.GameServiceId // total template count
 FOREACH threshold in [25, 50, 75, 100]
 IF percentage crosses threshold
 PUBLISH collection.milestone-reached { milestone, completionPercentage }
 PUSH CollectionMilestoneReachedClientEvent -> owner entity sessions

 // Dispatch DI unlock listeners
 FOREACH listener in _unlockListeners
 listener.OnEntryUnlockedAsync(notification) // per-listener error isolation

 RETURN (200, GrantEntryResponse { alreadyUnlocked: false })
```

---

### HasEntry
POST /collection/has | Roles: [user]

```
READ CollectionStore:"col:{ownerId}:{ownerType}:{gameServiceId}:{collectionType}"
IF collection null
 RETURN (200, HasEntryResponse { hasEntry: false })
// LoadOrRebuildCollectionCacheAsync
IF entryCode in cache.UnlockedEntries
 RETURN (200, HasEntryResponse { hasEntry: true, unlockedAt })
RETURN (200, HasEntryResponse { hasEntry: false })
```

---

### QueryEntries
POST /collection/query | Roles: [user]

```
READ CollectionStore:"col:{body.CollectionId}" -> 404 if null
// LoadOrRebuildCollectionCacheAsync
QUERY EntryTemplateStore WHERE $.CollectionType AND $.GameServiceId // full template load for enrichment
// Optional in-memory category and tags filtering
// Entries with deleted templates are silently excluded
// Cursor-based pagination (offset encoded as string)
RETURN (200, QueryEntriesResponse { entries, nextCursor, hasMore })
```

---

### UpdateEntryMetadata
POST /collection/update-metadata | Roles: [user]

```
LOCK CollectionLock:"col:{body.CollectionId}" -> 409 if fails
 READ CollectionStore:"col:{body.CollectionId}" -> 404 if null
 // LoadOrRebuildCollectionCacheAsync
 IF entryCode not in cache.UnlockedEntries -> 404

 // ETag retry loop (up to MaxConcurrencyRetries)
 READ CollectionCache:"cache:{collectionId}" [with ETag]
 // Apply non-null metadata fields (playCount, killCount, favorited, discoveryLevel, customData)
 // playCount update also sets lastAccessedAt
 ETAG-WRITE CollectionCache:"cache:{collectionId}" <- updated cache // retry on conflict

 READ EntryTemplateStore:"tpl:{gameServiceId}:{collectionType}:{entryCode}" // for response enrichment
 RETURN (200, UnlockedEntryResponse)
```

---

### GetCompletionStats
POST /collection/stats | Roles: [user]

```
QUERY EntryTemplateStore WHERE $.CollectionType == body.CollectionType AND $.GameServiceId == body.GameServiceId
READ CollectionStore:"col:{ownerId}:{ownerType}:{gameServiceId}:{collectionType}"
IF collection null
 RETURN (200, CompletionStatsResponse { totalEntries, unlockedEntries: 0, completionPercentage: 0 })
// LoadOrRebuildCollectionCacheAsync
// Calculate total/unlocked/percentage with per-category breakdown
// Templates with null category excluded from byCategory
RETURN (200, CompletionStatsResponse { collectionType, totalEntries, unlockedEntries, completionPercentage, byCategory })
```

---

### SelectContentForArea
POST /collection/content/select-for-area | Roles: [user]

```
READ AreaContentStore:"acc:{gameServiceId}:{collectionType}:{areaCode}" -> 404 if null
READ CollectionStore:"col:{ownerId}:{ownerType}:{gameServiceId}:{collectionType}"
IF collection null -> fall back to default entry

// LoadOrRebuildCollectionCacheAsync
IF cache empty -> fall back to default entry

QUERY EntryTemplateStore WHERE $.CollectionType AND $.GameServiceId
// Build candidates: unlocked entries with themes matching area config themes
// Weight = number of matched themes per entry
IF no candidates -> fall back to default entry

// Weighted random selection (Random.Shared.Next, non-deterministic)
RETURN (200, ContentSelectionResponse { entryCode, displayName, matchedThemes })

// Default entry fallback (BuildDefaultContentResponseAsync):
 READ EntryTemplateStore:"tpl:{gameServiceId}:{collectionType}:{areaConfig.DefaultEntryCode}" -> 404 if null
 RETURN (200, ContentSelectionResponse { entryCode, displayName, matchedThemes: [] })
```

---

### SetAreaContentConfig
POST /collection/content/area-config/set | Roles: [developer]

```
CALL _gameServiceClient.GetServiceAsync(body.GameServiceId) -> 404 if not found
READ EntryTemplateStore:"tpl:{gameServiceId}:{collectionType}:{defaultEntryCode}" -> 404 if null
READ AreaContentStore:"acc:{gameServiceId}:{collectionType}:{areaCode}"

IF existing config found
 // Update path: apply changes, track changedFields
 WRITE AreaContentStore:"acc:{config.AreaConfigId}" <- updated model
 WRITE AreaContentStore:"acc:{gameServiceId}:{collectionType}:{areaCode}" <- same model
 PUBLISH collection.area-content-config.updated { areaConfigId, areaCode, ..., changedFields }
ELSE
 // Create path
 WRITE AreaContentStore:"acc:{config.AreaConfigId}" <- new AreaContentConfigModel
 WRITE AreaContentStore:"acc:{gameServiceId}:{collectionType}:{areaCode}" <- same model
 PUBLISH collection.area-content-config.created { areaConfigId, areaCode, ... }

RETURN (200, AreaContentConfigResponse)
```

---

### GetAreaContentConfig
POST /collection/content/area-config/get | Roles: [user]

```
READ AreaContentStore:"acc:{gameServiceId}:{collectionType}:{areaCode}" -> 404 if null
RETURN (200, AreaContentConfigResponse)
```

---

### ListAreaContentConfigs
POST /collection/content/area-config/list | Roles: [developer]

```
QUERY AreaContentStore WHERE $.GameServiceId == body.GameServiceId AND $.CollectionType == body.CollectionType
RETURN (200, ListAreaContentConfigsResponse { configs })
```

---

### DeleteAreaContentConfig
POST /collection/content/area-config/delete | Roles: [developer]

```
READ AreaContentStore:"acc:{body.AreaConfigId}" -> 404 if null
DELETE AreaContentStore:"acc:{config.AreaConfigId}"
DELETE AreaContentStore:"acc:{gameServiceId}:{collectionType}:{areaCode}"
PUBLISH collection.area-content-config.deleted { areaConfigId, areaCode, collectionType, gameServiceId, defaultEntryCode }
RETURN (200, AreaContentConfigResponse)
```

---

### AdvanceDiscovery
POST /collection/discovery/advance | Roles: [user]

```
LOCK CollectionLock:"col:{body.CollectionId}" -> 409 if fails
 READ CollectionStore:"col:{body.CollectionId}" -> 404 if null
 // LoadOrRebuildCollectionCacheAsync
 IF entryCode not in cache.UnlockedEntries -> 404
 READ EntryTemplateStore:"tpl:{gameServiceId}:{collectionType}:{entryCode}" -> 404 if null
 IF template has no discoveryLevels -> 400
 // Find next level definition (current + 1)
 IF no next level definition exists -> 409 (already at max)

 // ETag retry loop (up to MaxConcurrencyRetries)
 READ CollectionCache:"cache:{collectionId}" [with ETag]
 // Update entry's discoveryLevel to newLevel
 ETAG-WRITE CollectionCache:"cache:{collectionId}" <- updated cache // retry on conflict

 PUBLISH collection.discovery-advanced { collectionId, ownerId, ownerType, entryCode, newLevel, reveals }
 PUSH CollectionDiscoveryAdvancedClientEvent -> owner entity sessions
 RETURN (200, AdvanceDiscoveryResponse { entryCode, newLevel, reveals })
```

---

### CleanupByCharacter
POST /collection/cleanup-by-character | Roles: [developer]

```
// CleanupCollectionsForOwnerAsync(characterId, EntityType.Character):
QUERY CollectionStore WHERE $.OwnerId == characterId AND $.OwnerType == Character
FOREACH collection in results
 // Per-collection error isolation (catch, log warning, continue)
 CALL _inventoryClient.DeleteContainerAsync(collection.ContainerId)
 DELETE CollectionCache:"cache:{collection.CollectionId}"
 DELETE CollectionStore:"col:{collection.CollectionId}"
 DELETE CollectionStore:"col:{ownerId}:{ownerType}:{gameServiceId}:{collectionType}"
 PUBLISH collection.deleted { collectionId, ..., deletedReason: "Owner Character deleted" }
// Does NOT call UnregisterCharacterReferenceAsync (lib-resource already knows character is gone)
RETURN (200, CleanupByCharacterResponse { deletedCount })
```

---

## Background Services

No background services.

---

## Event Handler: HandleAccountDeletedAsync

**Topic**: `account.deleted`

```
// CleanupCollectionsForOwnerAsync(accountId, EntityType.Account):
QUERY CollectionStore WHERE $.OwnerId == accountId AND $.OwnerType == Account
FOREACH collection in results
 // Per-collection error isolation (catch, log warning, continue)
 CALL _inventoryClient.DeleteContainerAsync(collection.ContainerId)
 DELETE CollectionCache:"cache:{collection.CollectionId}"
 DELETE CollectionStore:"col:{collection.CollectionId}"
 DELETE CollectionStore:"col:{ownerId}:{ownerType}:{gameServiceId}:{collectionType}"
 PUBLISH collection.deleted { collectionId, ..., deletedReason: "Owner Account deleted" }
```

Account cleanup uses event subscription (Account Deletion Cleanup Obligation). Character cleanup uses lib-resource cascade callback.

---

### CleanDeprecatedEntryTemplates
POST /collection/entry-template/clean-deprecated | Roles: [admin]

**Status: UNIMPLEMENTED** (`NotImplementedException` stub)

```
// Implementation should use DeprecationCleanupHelper.ExecuteCleanupSweepAsync
// per IMPLEMENTATION TENETS Category B clean-deprecated (B20-B22).
//
// Expected pseudocode:
// QUERY all entry templates WHERE IsDeprecated == true
// CALL DeprecationCleanupHelper.ExecuteCleanupSweepAsync(
//   deprecatedEntities: deprecated templates,
//   getEntityId: t => t.EntryTemplateId,
//   getDeprecatedAt: t => t.DeprecatedAt,
//   hasActiveInstancesAsync: check if any collection cache references this entry code,
//   deleteAndPublishAsync: delete template from both keys + publish collection.entry-template.deleted,
//   gracePeriodDays: body.GracePeriodDays,
//   dryRun: body.DryRun,
//   logger, telemetryProvider, ct)
// RETURN (200, CleanDeprecatedResponse { cleaned, remaining, errors, cleanedIds })
```
