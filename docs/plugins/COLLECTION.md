# Collection Plugin Deep Dive

> **Plugin**: lib-collection
> **Schema**: schemas/collection-api.yaml
> **Version**: 1.0.0
> **State Store**: collection-entry-templates (MySQL), collection-instances (MySQL), collection-area-music-configs (MySQL), collection-cache (Redis), collection-lock (Redis)

## Overview

The Collection service (L4 GameFeatures) manages universal content unlock and archive systems for collectible content: voice galleries, scene archives, music libraries, bestiaries, recipe books, and custom types. Follows the "items in inventories" pattern: entry templates define what can be collected, collection instances create inventory containers per owner, and granting an entry creates an item instance in that container. Unlike License (which orchestrates contracts for LP deduction), Collection uses direct grants without contract delegation. Features dynamic music track selection based on unlocked tracks and area theme configurations. Internal-only, never internet-facing.

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Persistence for entry templates (MySQL), collection instances (MySQL), area music configs (MySQL), collection cache (Redis), distributed locks (Redis) |
| lib-messaging (`IMessageBus`) | Publishing lifecycle events, entry-unlocked, grant-failed, milestone-reached, discovery-advanced events; error event publishing in cleanup |
| lib-inventory (`IInventoryClient`) | Creating unlimited containers for collections, deleting containers on collection deletion, reading container contents for cache rebuilds (L2 hard dependency) |
| lib-item (`IItemClient`) | Creating item instances when entries are granted, validating item template existence for entry templates (L2 hard dependency) |
| lib-game-service (`IGameServiceClient`) | Validating game service existence during template/collection/area-config creation (L2 hard dependency) |
| `IDistributedLockProvider` | Distributed locks for template updates/deletes, collection deletes, grant operations, metadata updates, discovery advancement (L0 hard dependency) |
| `IEventConsumer` | Registering handlers for `character.deleted` and `account.deleted` cleanup events |

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| *(none currently)* | No other plugins consume Collection events or inject `ICollectionClient` |

## State Storage

### Entry Templates Store
**Store**: `collection-entry-templates` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `tpl:{entryTemplateId}` | `EntryTemplateModel` | Primary lookup by ID |
| `tpl:{gameServiceId}:{collectionType}:{code}` | `EntryTemplateModel` | Code-uniqueness lookup within type + game |

### Collection Instances Store
**Store**: `collection-instances` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `col:{collectionId}` | `CollectionInstanceModel` | Primary lookup by ID |
| `col:{ownerId}:{ownerType}:{gameServiceId}:{collectionType}` | `CollectionInstanceModel` | Owner+type uniqueness lookup |

### Area Music Config Store
**Store**: `collection-area-music-configs` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `amc:{areaConfigId}` | `AreaMusicConfigModel` | Primary lookup by ID |
| `amc:{gameServiceId}:{areaCode}` | `AreaMusicConfigModel` | Area code lookup within game |

### Collection Cache
**Store**: `collection-cache` (Backend: Redis, prefix: `collection:state`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `cache:{collectionId}` | `CollectionCacheModel` | Cached unlocked entries per collection (with TTL) |

### Distributed Locks
**Store**: `collection-lock` (Backend: Redis, prefix: `collection:lock`)

Used for template update/delete, collection delete, grant, metadata update, and discovery advancement operations.

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `collection-entry-template.created` | `CollectionEntryTemplateCreatedEvent` | Entry template created (single or via seed) |
| `collection-entry-template.updated` | `CollectionEntryTemplateUpdatedEvent` | Entry template fields updated |
| `collection-entry-template.deleted` | `CollectionEntryTemplateDeletedEvent` | Entry template deleted |
| `collection.created` | `CollectionCreatedEvent` | Collection instance created (explicit or auto-create during grant) |
| `collection.deleted` | `CollectionDeletedEvent` | Collection instance explicitly deleted via DeleteCollectionAsync |
| `collection.entry-unlocked` | `CollectionEntryUnlockedEvent` | Entry successfully granted/unlocked in a collection |
| `collection.entry-grant-failed` | `CollectionEntryGrantFailedEvent` | Grant attempt failed (entry not found, max reached, item creation failed) |
| `collection.milestone-reached` | `CollectionMilestoneReachedEvent` | Completion milestone crossed (25%, 50%, 75%, 100%) |
| `collection.discovery-advanced` | `CollectionDiscoveryAdvancedEvent` | Progressive discovery level advanced for an entry |

### Consumed Events

| Topic | Handler | Action |
|-------|---------|--------|
| `character.deleted` | `HandleCharacterDeletedAsync` | Deletes all character-owned collections, their containers, and cache entries |
| `account.deleted` | `HandleAccountDeletedAsync` | Deletes all account-owned collections, their containers, and cache entries |

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `MaxCollectionsPerOwner` | `COLLECTION_MAX_COLLECTIONS_PER_OWNER` | 20 | Max collections per owner entity (checked in CreateCollectionAsync) |
| `MaxEntriesPerCollection` | `COLLECTION_MAX_ENTRIES_PER_COLLECTION` | 500 | Max unlocked entries per collection (checked in GrantEntryAsync) |
| `LockTimeoutSeconds` | `COLLECTION_LOCK_TIMEOUT_SECONDS` | 30 | TTL for distributed locks on mutation operations |
| `CollectionCacheTtlSeconds` | `COLLECTION_CACHE_TTL_SECONDS` | 300 | Redis TTL for collection state cache (5 minutes) |
| `DefaultPageSize` | `COLLECTION_DEFAULT_PAGE_SIZE` | 20 | Default page size for paginated queries |
| `MaxConcurrencyRetries` | `COLLECTION_MAX_CONCURRENCY_RETRIES` | 3 | Max retry attempts for ETag-based optimistic concurrency conflicts |

## DI Services & Helpers

| Service | Role |
|---------|------|
| `ILogger<CollectionService>` | Structured logging |
| `CollectionServiceConfiguration` | Typed configuration access |
| `IStateStoreFactory` | State store access (creates 4 stores + lock store) |
| `IMessageBus` | Event publishing |
| `IEventConsumer` | Event subscription registration |
| `IInventoryClient` | Inventory container operations (L2) |
| `IItemClient` | Item template validation and instance creation (L2) |
| `IGameServiceClient` | Game service existence validation (L2) |
| `IDistributedLockProvider` | Distributed lock acquisition (L0) |

No helper services or background workers. All logic in `CollectionService.cs`, with events in `CollectionServiceEvents.cs` and models in `CollectionServiceModels.cs`.

## API Endpoints (Implementation Notes)

### Entry Template Management (6 endpoints)

Standard CRUD on entry templates with code-uniqueness enforcement per collection type + game service. All endpoints require `developer` role.

- **Create**: Validates game service and item template existence via service clients. Saves under both ID and code lookup keys.
- **Update**: Acquires distributed lock. Handles partial updates (null fields are skipped). Saves both keys and publishes updated event with `changedFields` list.
- **Delete**: Acquires lock, logs warnings if any collection caches reference the template code (best-effort check), deletes both keys.
- **Seed**: Bulk create with upfront item template validation. Skips duplicates (by code lookup key). Returns created/skipped counts.
- **List**: Paginated by cursor (index-offset based). Filters by collection type + game service + optional category.

### Collection Instance Management (4 endpoints)

- **Create**: Validates owner type mapping to `ContainerOwnerType`, game service existence, uniqueness (one per type per game per owner), and max collections limit. Creates an unlimited inventory container via lib-inventory.
- **Get**: Returns collection with entry count from cache (rebuilds cache on miss).
- **List**: Lists all collections for an owner with optional game service filter. Uses cache for entry counts when available (0 on cache miss).
- **Delete**: Acquires lock, deletes inventory container (tolerates 404), deletes cache and both store keys.

### Entry Operations (5 endpoints)

- **Grant** (core operation): Idempotent - returns existing if already unlocked. Auto-creates collection if none exists. Acquires lock, creates item instance via lib-item, updates cache with ETag concurrency, publishes unlock event, checks/publishes milestones.
- **Has**: Quick check if owner has a specific entry unlocked. Returns false if collection doesn't exist.
- **Query**: Paginated query with category and tag filtering. Enriches responses with template data.
- **UpdateMetadata**: Acquires lock, updates metadata fields (playCount, killCount, favorited, discoveryLevel) with ETag concurrency.
- **GetCompletionStats**: Calculates total/unlocked/percentage with per-category breakdown.

### Music Operations (4 endpoints)

- **SelectTrackForArea**: Loads area config themes, finds matching unlocked tracks from owner's music library, performs weighted random selection (weight = number of matching themes). Falls back to default track.
- **SetAreaMusicConfig**: Upsert area-to-theme mapping. Validates game service and default track template existence.
- **GetAreaMusicConfig** / **ListAreaMusicConfigs**: Read operations for area configs.

### Discovery (1 endpoint)

- **AdvanceDiscovery**: Increments discovery level for a bestiary-style entry. Validates level exists in template definition. Publishes event with revealed information keys.

## Visual Aid

```
┌──────────────────────────────────────────────────────────────────────┐
│                      State Store Key Relationships                   │
├──────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  EntryTemplateStore (MySQL)                                          │
│  ┌─────────────────────────┐  ┌─────────────────────────────────┐   │
│  │ tpl:{templateId}        │  │ tpl:{gameId}:{type}:{code}      │   │
│  │ → EntryTemplateModel    │  │ → EntryTemplateModel (same)     │   │
│  └──────────┬──────────────┘  └─────────────────────────────────┘   │
│             │ itemTemplateId                                         │
│             ▼                                                        │
│  lib-item ──── Item Template (external)                              │
│                                                                      │
│  CollectionStore (MySQL)                                             │
│  ┌─────────────────────────┐  ┌─────────────────────────────────┐   │
│  │ col:{collectionId}      │  │ col:{ownerId}:{type}:{game}:... │   │
│  │ → CollectionInstanceModel│ │ → CollectionInstanceModel (same)│   │
│  └──────────┬──────────────┘  └─────────────────────────────────┘   │
│             │ containerId                                            │
│             ▼                                                        │
│  lib-inventory ──── Container (external, unlimited type)             │
│             ▲                                                        │
│             │ items                                                   │
│  CollectionCache (Redis, TTL)                                        │
│  ┌──────────────────────────┐                                        │
│  │ cache:{collectionId}     │ ◄── rebuilt from container on miss     │
│  │ → CollectionCacheModel   │                                        │
│  │   └─ UnlockedEntries[]   │                                        │
│  │      ├─ code             │                                        │
│  │      ├─ itemInstanceId ──┼── points to item in container          │
│  │      └─ metadata         │                                        │
│  └──────────────────────────┘                                        │
│                                                                      │
│  AreaMusicStore (MySQL)                                              │
│  ┌─────────────────────────┐  ┌─────────────────────────────────┐   │
│  │ amc:{configId}          │  │ amc:{gameId}:{areaCode}         │   │
│  │ → AreaMusicConfigModel  │  │ → AreaMusicConfigModel (same)   │   │
│  └─────────────────────────┘  └─────────────────────────────────┘   │
└──────────────────────────────────────────────────────────────────────┘
```

## Stubs & Unimplemented Features

1. **`isFirstGlobal` always false**: `CollectionEntryUnlockedEvent.IsFirstGlobal` is hardcoded to `false` with comment "Would require global tracking to determine". No global first-unlock tracking exists.

## Potential Extensions

1. **Global first-unlock tracking**: Implement tracking to set `isFirstGlobal` correctly on unlock events. Would require a global set of unlocked entry codes per game service.
2. **Client events for real-time unlock notifications**: Define `collection-client-events.yaml` to push unlock/milestone events to connected WebSocket clients via `IClientEventPublisher`.
3. **Expiring/seasonal collections**: Support time-limited collection types that expire or rotate on a schedule.
4. **Collection sharing/trading**: Allow owners to share or trade unlocked entries between collections.

## Known Quirks & Caveats

### Bugs (Fix Immediately)

1. **UpdateEntryTemplateAsync ignores `hideWhenLocked` and `discoveryLevels` fields**: The schema defines both `hideWhenLocked` (nullable boolean) and `discoveryLevels` (nullable array) on `UpdateEntryTemplateRequest`, but the service code only handles displayName, category, tags, assetId, thumbnailAssetId, unlockHint, themes, duration, loopPoint, and composer. These two fields cannot be updated after creation.

2. **ListEntryTemplatesAsync ignores request `pageSize`**: Uses `_configuration.DefaultPageSize` unconditionally, ignoring `body.PageSize` from the request. `QueryEntriesAsync` correctly uses `body.PageSize ?? _configuration.DefaultPageSize`. The same pattern should be applied to `ListEntryTemplatesAsync`.

3. **GrantEntryAsync auto-create bypasses `MaxCollectionsPerOwner` limit**: When auto-creating a collection during `GrantEntryAsync`, `CreateCollectionInternalAsync` is called directly without the max-collections-per-owner check that `CreateCollectionAsync` performs. This allows unlimited collections to be created via the grant path.

4. **Cleanup event handlers don't publish `collection.deleted` events**: `CleanupCollectionsForOwnerAsync` (triggered by `character.deleted` / `account.deleted`) deletes collections but never publishes `collection.deleted` events. Only `DeleteCollectionAsync` publishes these events. Any downstream consumers of `collection.deleted` would miss cascading deletions.

### Intentional Quirks (Documented Behavior)

1. **Polymorphic ownership with opaque strings**: `ownerType` is an opaque string, not an enum, following the Seed pattern per SCHEMA-RULES.md guidance. Valid owner types that map to `ContainerOwnerType`: `character`, `account`, `location`, `guild`. Unknown types are rejected at the `MapToContainerOwnerType` check, returning `BadRequest`.

2. **Dual-key storage pattern**: Templates, collections, and area configs are saved under both a primary key (by ID) and a lookup key (by code/owner). Both keys must be updated on mutations and deleted on removal. This enables O(1) lookups by both ID and business key.

3. **Redis cache as optimization over inventory**: The collection cache in Redis is purely a performance optimization. Inventory container contents (via lib-inventory) are the authoritative source. Cache misses trigger full rebuilds from inventory. The cache has a configurable TTL (default 300s).

4. **Cache rebuild loses original unlock timestamps**: When rebuilding the cache from inventory contents, `UnlockedAt` is set to `collection.CreatedAt` as a best approximation. Original per-entry unlock timestamps are lost.

5. **RealmId set to GameServiceId for item creation**: Collection items use `GameServiceId` as the `RealmId` partition key since collections are game-service-scoped, not realm-scoped. This is documented in code comments.

6. **GrantEntry is idempotent**: If an entry is already unlocked, GrantEntryAsync returns the existing entry with `AlreadyUnlocked = true` and status OK rather than Conflict.

7. **Template deletion is permissive**: DeleteEntryTemplateAsync logs warnings when collections reference the template but does not block deletion. The cache-based reference check is best-effort (expired caches won't detect references).

8. **Milestone events use string labels**: `CollectionMilestoneReachedEvent.Milestone` is a string like `"25%"` rather than a numeric value. The `CompletionPercentage` field carries the precise numeric value.

### Design Considerations (Requires Planning)

1. **No owner validation**: Collection follows the Seed pattern where owner validation is caller-responsibility. The service accepts any `ownerId`/`ownerType` that passes the `MapToContainerOwnerType` check, without verifying that the entity actually exists. Callers must ensure valid owners.

2. **No event-driven entry template cache invalidation**: When an entry template is updated or deleted, existing collection caches that reference it are not invalidated. Stale template data may be served until the cache TTL expires or the cache is rebuilt.

## Work Tracking

*No active work items.*
