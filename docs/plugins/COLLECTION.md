# Collection Plugin Deep Dive

> **Plugin**: lib-collection
> **Schema**: schemas/collection-api.yaml
> **Version**: 1.0.0
> **Layer**: GameFoundation
> **State Store**: collection-entry-templates (MySQL), collection-instances (MySQL), collection-area-content-configs (MySQL), collection-cache (Redis), collection-lock (Redis)
> **Implementation Map**: [docs/maps/COLLECTION.md](../maps/COLLECTION.md)
> **Short**: Universal content unlock and archive system with DI-dispatched unlock listeners

---

## Overview

The Collection service (L2 GameFoundation) manages universal content unlock and archive systems for collectible content: voice galleries, scene archives, music libraries, bestiaries, recipe books, and custom types. Follows the "items in inventories" pattern: entry templates define what can be collected, collection instances create inventory containers per owner, and granting an entry creates an item instance in that container. Unlike License (which orchestrates contracts for LP deduction), Collection uses direct grants without contract delegation. Features dynamic content selection based on unlocked entries and area theme configurations. Collection types are opaque strings (not enums), allowing new types without schema changes. Dispatches unlock notifications to registered `ICollectionUnlockListener` implementations via DI for guaranteed in-process delivery (e.g., Seed growth pipeline). Internal-only, never internet-facing.

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-seed (`SeedCollectionUnlockListener`) | Implements `ICollectionUnlockListener` to drive seed growth from collection entry unlocks via tag-prefix matching against seed type `collectionGrowthMappings` |
| lib-faction (`FactionCollectionUnlockListener`) | Implements `ICollectionUnlockListener` to react to collection entry unlocks for faction-related growth |

## Type Field Classification

| Field | Category | Type | Rationale |
|-------|----------|------|-----------|
| `ownerType` | A (Entity Reference) | `EntityType` enum | All valid values are first-class Bannou entities (character, account, location, guild). Recently migrated to shared EntityType enum. Mapped to `ContainerOwnerType` for inventory container creation. |
| `collectionType` | B (Content Code) | Opaque string (`CollectionType`) | Game-configurable collection content category. New types added without schema changes (e.g., `voice_gallery`, `scene_archive`, `music_library`, `bestiary`, `recipe_book`). |
| `entryCode` | B (Content Code) | Opaque string | Game-configurable entry identifier, unique within collection type + game service. Represents specific collectible content (e.g., a particular voice line, scene, or creature). |
| `areaCode` | B (Content Code) | Opaque string | Game-configurable area identifier for content selection configuration. Maps areas to theme sets for weighted random content selection. |
| `category` (on entry templates) | B (Content Code) | Opaque string | Game-configurable subcategorization within a collection type. Used for filtering and completion stats breakdown. |
| `reason` (CollectionEntryGrantFailedEvent) | C (System State) | `GrantFailureReason` enum | Finite system-owned failure modes for grant operations. |

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `MaxCollectionsPerOwner` | `COLLECTION_MAX_COLLECTIONS_PER_OWNER` | 20 | Max collections per owner entity (checked in CreateCollectionAsync and GrantEntryAsync auto-create) |
| `MaxEntriesPerCollection` | `COLLECTION_MAX_ENTRIES_PER_COLLECTION` | 500 | Max unlocked entries per collection (checked in GrantEntryAsync) |
| `LockTimeoutSeconds` | `COLLECTION_LOCK_TIMEOUT_SECONDS` | 30 | TTL for distributed locks on mutation operations |
| `CollectionCacheTtlSeconds` | `COLLECTION_CACHE_TTL_SECONDS` | 300 | Redis TTL for collection state cache (5 minutes) |
| `DefaultPageSize` | `COLLECTION_DEFAULT_PAGE_SIZE` | 20 | Default page size for paginated queries |
| `MaxConcurrencyRetries` | `COLLECTION_MAX_CONCURRENCY_RETRIES` | 3 | Max retry attempts for ETag-based optimistic concurrency conflicts |

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

*No known stubs.*

## Potential Extensions

1. **Expiring/seasonal collections**: Support time-limited collection types that expire or rotate on a schedule.
<!-- AUDIT:NEEDS_DESIGN:2026-02-24:https://github.com/beyond-immersion/bannou-service/issues/475 -->
2. **Collection sharing/trading**: Allow owners to share or trade unlocked entries between collections.
<!-- AUDIT:NEEDS_DESIGN:2026-02-24:https://github.com/beyond-immersion/bannou-service/issues/476 -->

## Known Quirks & Caveats

### Bugs (Fix Immediately)

*No known bugs.*

### Intentional Quirks (Documented Behavior)

1. **Polymorphic ownership via EntityType enum**: `ownerType` uses the shared `EntityType` enum (Category A per IMPLEMENTATION TENETS decision tree — all valid values are first-class Bannou entities). Valid owner types that map to `ContainerOwnerType`: `character`, `account`, `location`, `guild`. Unknown types are rejected at the `MapToContainerOwnerType` check, returning `BadRequest`.

2. **Dual-key storage pattern**: Templates, collections, and area configs are saved under both a primary key (by ID) and a lookup key (by code/owner). Both keys must be updated on mutations and deleted on removal. This enables O(1) lookups by both ID and business key.

3. **Redis cache as optimization over inventory**: The collection cache in Redis is purely a performance optimization. Inventory container contents (via lib-inventory) are the authoritative source. Cache misses trigger full rebuilds from inventory. The cache has a configurable TTL (default 300s).

4. **Cache rebuild loses original unlock timestamps**: When rebuilding the cache from inventory contents, `UnlockedAt` is set to `collection.CreatedAt` as a best approximation. Original per-entry unlock timestamps are lost.

5. **RealmId set to GameServiceId for item creation**: Collection items use `GameServiceId` as the `RealmId` partition key since collections are game-service-scoped, not realm-scoped. This is documented in code comments.

6. **GrantEntry is idempotent**: If an entry is already unlocked, GrantEntryAsync returns the existing entry with `AlreadyUnlocked = true` and status OK rather than Conflict.

7. **Milestone events use string labels**: `CollectionMilestoneReachedEvent.Milestone` is a string like `"25%"` rather than a numeric value. The `CompletionPercentage` field carries the precise numeric value.

8. **No owner entity existence validation**: Collection follows the Seed pattern where owner validation is caller-responsibility. The service validates that `ownerType` maps to a known `ContainerOwnerType` and that the game service exists, but does not verify the owner entity (character, account, location, guild) actually exists. This is intentional: Collection is L2 and owner types span L1 (account), L2 (character, location), and L4 (guild via Faction). Injecting clients for all owner types would create hierarchy issues and break the polymorphic ownership model. Callers must ensure valid owners before creating collections.

9. **No event-driven entry template cache invalidation**: When an entry template is updated or deleted, existing collection caches that reference it are not invalidated. Stale template data may be served until the cache TTL expires or the cache is rebuilt. Cache TTL (default 300s) bounds the staleness window.

### Design Considerations (Requires Planning)

*No open design considerations.*

## Work Tracking

*No active work items.*
