# Collection Plugin Deep Dive

> **Plugin**: lib-collection
> **Schema**: schemas/collection-api.yaml
> **Version**: 1.0.0
> **Layer**: GameFoundation
> **State Store**: collection-entry-templates (MySQL), collection-instances (MySQL), collection-area-content-configs (MySQL), collection-cache (Redis), collection-lock (Redis)

## Overview

The Collection service (L2 GameFoundation) manages universal content unlock and archive systems for collectible content: voice galleries, scene archives, music libraries, bestiaries, recipe books, and custom types. Follows the "items in inventories" pattern: entry templates define what can be collected, collection instances create inventory containers per owner, and granting an entry creates an item instance in that container. Unlike License (which orchestrates contracts for LP deduction), Collection uses direct grants without contract delegation. Features dynamic content selection based on unlocked entries and area theme configurations. Collection types are opaque strings (not enums), allowing new types without schema changes. Dispatches unlock notifications to registered `ICollectionUnlockListener` implementations via DI for guaranteed in-process delivery (e.g., Seed growth pipeline). Internal-only, never internet-facing.

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Persistence for entry templates (MySQL), collection instances (MySQL), area content configs (MySQL), collection cache (Redis), global unlock tracking via Redis sets (Redis), distributed locks (Redis) |
| lib-messaging (`IMessageBus`) | Publishing lifecycle events, entry-unlocked, grant-failed, milestone-reached, discovery-advanced events; error event publishing in cleanup |
| lib-inventory (`IInventoryClient`) | Creating unlimited containers for collections, deleting containers on collection deletion, reading container contents for cache rebuilds (L2 hard dependency) |
| lib-item (`IItemClient`) | Creating item instances when entries are granted, validating item template existence for entry templates (L2 hard dependency) |
| lib-game-service (`IGameServiceClient`) | Validating game service existence during template/collection/area-config creation (L2 hard dependency) |
| lib-resource (`IResourceClient`) | Registering/unregistering character references for cascading cleanup, registering cleanup callbacks on startup (L1 hard dependency) |
| `IDistributedLockProvider` | Distributed locks for template updates/deletes, collection deletes, grant operations, metadata updates, discovery advancement (L0 hard dependency) |
| `ITelemetryProvider` | Distributed tracing spans for async operations (L0 hard dependency) |
| `IEntitySessionRegistry` | Publishing client events to collection owner WebSocket sessions for real-time unlock/milestone/discovery notifications (L1 hard dependency) |
| `IEventConsumer` | Registering handler for `account.deleted` cleanup event (character cleanup uses lib-resource x-references, not event subscription) |

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-seed (`SeedCollectionUnlockListener`) | Implements `ICollectionUnlockListener` to drive seed growth from collection entry unlocks via tag-prefix matching against seed type `collectionGrowthMappings` |
| lib-faction (`FactionCollectionUnlockListener`) | Implements `ICollectionUnlockListener` to react to collection entry unlocks for faction-related growth |

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

### Area Content Config Store
**Store**: `collection-area-content-configs` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `acc:{areaConfigId}` | `AreaContentConfigModel` | Primary lookup by ID |
| `acc:{gameServiceId}:{collectionType}:{areaCode}` | `AreaContentConfigModel` | Area code lookup within game + collection type |

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
| `collection-area-content-config.created` | `CollectionAreaContentConfigCreatedEvent` | Area content config created via SetAreaContentConfigAsync |
| `collection-area-content-config.updated` | `CollectionAreaContentConfigUpdatedEvent` | Area content config updated via SetAreaContentConfigAsync (with `changedFields` list) |
| `collection.created` | `CollectionCreatedEvent` | Collection instance created (explicit or auto-create during grant) |
| `collection.deleted` | `CollectionDeletedEvent` | Collection instance deleted (explicit via DeleteCollectionAsync or cascading via owner cleanup) |
| `collection.entry-unlocked` | `CollectionEntryUnlockedEvent` | Entry successfully granted/unlocked in a collection |
| `collection.entry-grant-failed` | `CollectionEntryGrantFailedEvent` | Grant attempt failed (entry not found, max reached, item creation failed) |
| `collection.milestone-reached` | `CollectionMilestoneReachedEvent` | Completion milestone crossed (25%, 50%, 75%, 100%) |
| `collection.discovery-advanced` | `CollectionDiscoveryAdvancedEvent` | Progressive discovery level advanced for an entry |

### Consumed Events

| Topic | Handler | Action |
|-------|---------|--------|
| `account.deleted` | `HandleAccountDeletedAsync` | Deletes all account-owned collections, their containers, and cache entries |

### Published Client Events

| Event Name | Event Type | Trigger |
|------------|-----------|---------|
| `collection.entry_unlocked` | `CollectionEntryUnlockedClientEvent` | Entry granted/unlocked; pushed to owner's WebSocket sessions with entry details and first-global status |
| `collection.milestone_reached` | `CollectionMilestoneReachedClientEvent` | Completion milestone crossed (25%, 50%, 75%, 100%); pushed to owner's WebSocket sessions |
| `collection.discovery_advanced` | `CollectionDiscoveryAdvancedClientEvent` | Discovery level advanced; pushed to owner's WebSocket sessions with revealed keys |

Client events are published via `IEntitySessionRegistry.PublishToEntitySessionsAsync` using the collection's `ownerType`/`ownerId` for entity-session resolution. If zero sessions are registered for the owner, zero events are delivered (graceful degradation). Schema: `schemas/collection-client-events.yaml`.

**Note**: Character cleanup is NOT handled via event subscription. Character-owned collection cleanup uses lib-resource (x-references) with a registered cleanup callback (`CleanupByCharacterAsync`) per FOUNDATION TENETS.

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `MaxCollectionsPerOwner` | `COLLECTION_MAX_COLLECTIONS_PER_OWNER` | 20 | Max collections per owner entity (checked in CreateCollectionAsync and GrantEntryAsync auto-create) |
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
| `IResourceClient` | Resource reference tracking for character-owned collections (L1) |
| `IDistributedLockProvider` | Distributed lock acquisition (L0) |
| `ITelemetryProvider` | Distributed tracing span creation (L0) |
| `IEntitySessionRegistry` | Publishing client events to owner WebSocket sessions (L1) |
| `IEnumerable<ICollectionUnlockListener>` | DI-discovered listeners notified on entry unlock (e.g., SeedCollectionUnlockListener, FactionCollectionUnlockListener) |

No helper services or background workers. All logic in `CollectionService.cs`, with events in `CollectionServiceEvents.cs` and models in `CollectionServiceModels.cs`. Plugin startup registration in `CollectionServicePlugin.cs` registers resource cleanup callbacks with lib-resource.

## API Endpoints (Implementation Notes)

### Entry Template Management (6 endpoints)

Standard CRUD on entry templates with code-uniqueness enforcement per collection type + game service. All endpoints require `developer` role.

- **Create**: Validates game service and item template existence via service clients. Saves under both ID and code lookup keys.
- **Update**: Acquires distributed lock. Handles partial updates (null fields are skipped). Saves both keys and publishes updated event with `changedFields` list.
- **Delete**: Acquires lock, logs warnings if any collection caches reference the template code (best-effort check), deletes both keys.
- **Seed**: Bulk create with upfront item template validation. Skips duplicates (by code lookup key). Returns created/skipped counts.
- **List**: Paginated by cursor (index-offset based). Filters by collection type + game service + optional category.

### Collection Instance Management (4 endpoints)

- **Create**: Validates owner type mapping to `ContainerOwnerType`, game service existence, uniqueness (one per type per game per owner), and max collections limit. Creates an unlimited inventory container via lib-inventory. Registers character references with lib-resource for character-owned collections.
- **Get**: Returns collection with entry count from cache (rebuilds cache on miss).
- **List**: Lists all collections for an owner with optional game service filter. Uses cache for entry counts when available (0 on cache miss).
- **Delete**: Acquires lock, deletes inventory container (tolerates 404), deletes cache and both store keys. Unregisters character references with lib-resource for character-owned collections.

### Entry Operations (5 endpoints)

- **Grant** (core operation): Idempotent - returns existing if already unlocked. Auto-creates collection if none exists. Acquires lock, creates item instance via lib-item, updates cache with ETag concurrency, publishes unlock event, checks/publishes milestones.
- **Has**: Quick check if owner has a specific entry unlocked. Returns false if collection doesn't exist.
- **Query**: Paginated query with category and tag filtering. Enriches responses with template data.
- **UpdateMetadata**: Acquires lock, updates metadata fields (playCount, killCount, favorited, discoveryLevel) with ETag concurrency.
- **GetCompletionStats**: Calculates total/unlocked/percentage with per-category breakdown.

### Content Selection Operations (4 endpoints)

- **SelectContentForArea**: Loads area config themes, finds matching unlocked entries from owner's collection of the specified type, performs weighted random selection (weight = number of matching themes). Falls back to default entry.
- **SetAreaContentConfig**: Upsert area-to-theme mapping per collection type. Validates game service and default entry template existence.
- **GetAreaContentConfig** / **ListAreaContentConfigs**: Read operations for area configs, scoped by collection type.

### Discovery (1 endpoint)

- **AdvanceDiscovery**: Increments discovery level for a bestiary-style entry. Validates level exists in template definition. Publishes event with revealed information keys.

### Resource Cleanup (1 endpoint)

- **CleanupByCharacter**: Called by lib-resource during cascading character deletion. Delegates to `CleanupCollectionsForOwnerAsync` to delete all character-owned collections, their containers, cache entries, and publishes `collection.deleted` events for each.

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
│  AreaContentStore (MySQL)                                            │
│  ┌─────────────────────────┐  ┌─────────────────────────────────┐   │
│  │ acc:{configId}          │  │ acc:{gameId}:{type}:{areaCode}  │   │
│  │ → AreaContentConfigModel│  │ → AreaContentConfigModel (same) │   │
│  └─────────────────────────┘  └─────────────────────────────────┘   │
└──────────────────────────────────────────────────────────────────────┘
```

## Stubs & Unimplemented Features

1. ~~**`isFirstGlobal` always false**~~: **FIXED** (2026-02-24) - Added global first-unlock tracking via Redis set (SADD) on the `collection-cache` store. `CacheableCollectionCache.AddToSetAsync` atomically adds entry codes to a per-game-service per-collection-type set and returns whether the code was newly added (true = first global unlock). Multi-instance safe via Redis atomicity. Global unlock sets persist indefinitely (no TTL).

## Potential Extensions

1. ~~**Global first-unlock tracking**~~: **FIXED** (2026-02-24) - Implemented via Redis set operations in the `collection-cache` store. See Stubs section for details.
2. ~~**Client events for real-time unlock notifications**~~: **FIXED** (2026-02-24) - Added `schemas/collection-client-events.yaml` with three client events (`collection.entry_unlocked`, `collection.milestone_reached`, `collection.discovery_advanced`). CollectionService now pushes real-time notifications to collection owner WebSocket sessions via `IEntitySessionRegistry` after each corresponding service event publish.
3. **Expiring/seasonal collections**: Support time-limited collection types that expire or rotate on a schedule.
<!-- AUDIT:NEEDS_DESIGN:2026-02-24:https://github.com/beyond-immersion/bannou-service/issues/475 -->
4. **Collection sharing/trading**: Allow owners to share or trade unlocked entries between collections.
<!-- AUDIT:NEEDS_DESIGN:2026-02-24:https://github.com/beyond-immersion/bannou-service/issues/476 -->

## Known Quirks & Caveats

### Bugs (Fix Immediately)

*No known bugs.*

### Intentional Quirks (Documented Behavior)

1. **Polymorphic ownership with opaque strings**: `ownerType` is an opaque string, not an enum, following the Seed pattern per SCHEMA-RULES.md guidance. Valid owner types that map to `ContainerOwnerType`: `character`, `account`, `location`, `guild`. Unknown types are rejected at the `MapToContainerOwnerType` check, returning `BadRequest`.

2. **Dual-key storage pattern**: Templates, collections, and area configs are saved under both a primary key (by ID) and a lookup key (by code/owner). Both keys must be updated on mutations and deleted on removal. This enables O(1) lookups by both ID and business key.

3. **Redis cache as optimization over inventory**: The collection cache in Redis is purely a performance optimization. Inventory container contents (via lib-inventory) are the authoritative source. Cache misses trigger full rebuilds from inventory. The cache has a configurable TTL (default 300s).

4. **Cache rebuild loses original unlock timestamps**: When rebuilding the cache from inventory contents, `UnlockedAt` is set to `collection.CreatedAt` as a best approximation. Original per-entry unlock timestamps are lost.

5. **RealmId set to GameServiceId for item creation**: Collection items use `GameServiceId` as the `RealmId` partition key since collections are game-service-scoped, not realm-scoped. This is documented in code comments.

6. **GrantEntry is idempotent**: If an entry is already unlocked, GrantEntryAsync returns the existing entry with `AlreadyUnlocked = true` and status OK rather than Conflict.

7. **Template deletion is permissive**: DeleteEntryTemplateAsync logs warnings when collections reference the template but does not block deletion. The cache-based reference check is best-effort (expired caches won't detect references).

8. **Milestone events use string labels**: `CollectionMilestoneReachedEvent.Milestone` is a string like `"25%"` rather than a numeric value. The `CompletionPercentage` field carries the precise numeric value.

9. **No owner entity existence validation**: Collection follows the Seed pattern where owner validation is caller-responsibility. The service validates that `ownerType` maps to a known `ContainerOwnerType` and that the game service exists, but does not verify the owner entity (character, account, location, guild) actually exists. This is intentional: Collection is L2 and owner types span L1 (account), L2 (character, location), and L4 (guild via Faction). Injecting clients for all owner types would create hierarchy issues and break the polymorphic ownership model. Callers must ensure valid owners before creating collections.

10. **No event-driven entry template cache invalidation**: When an entry template is updated or deleted, existing collection caches that reference it are not invalidated. Stale template data may be served until the cache TTL expires or the cache is rebuilt. Cache TTL (default 300s) bounds the staleness window.

### Design Considerations (Requires Planning)

*No open design considerations.*

## Work Tracking

### Completed

- **`isFirstGlobal` global unlock tracking** (2026-02-24): Added atomic Redis set tracking for first-global unlock determination. `CollectionEntryUnlockedEvent.IsFirstGlobal` now correctly reflects whether this is the first time any owner has unlocked the entry within the game service + collection type scope.
- **Client events for real-time unlock notifications** (2026-02-24): Added `schemas/collection-client-events.yaml` defining three client event types. CollectionService now publishes `CollectionEntryUnlockedClientEvent`, `CollectionMilestoneReachedClientEvent`, and `CollectionDiscoveryAdvancedClientEvent` to owner WebSocket sessions via `IEntitySessionRegistry` after each corresponding service event. Added `IEntitySessionRegistry` as L1 hard dependency.
- **"No owner validation" moved to Intentional Quirks** (2026-02-24): Audit confirmed this follows the established Seed pattern — polymorphic ownership by design, not a missing feature. Moved from Design Considerations to Intentional Quirks #9 with expanded rationale (hierarchy constraints, extensibility).
- **"No entry template cache invalidation" moved to Intentional Quirks** (2026-02-24): Audit confirmed this is bounded by cache TTL (default 300s) — not a design gap requiring planning. Moved from Design Considerations to Intentional Quirks #10.
