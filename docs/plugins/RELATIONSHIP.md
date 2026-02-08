# Relationship Plugin Deep Dive

> **Plugin**: lib-relationship
> **Schema**: schemas/relationship-api.yaml
> **Version**: 2.0.0
> **State Stores**: relationship-statestore (MySQL), relationship-type-statestore (MySQL)

---

## Overview

A unified relationship management service combining entity-to-entity relationships (character friendships, alliances, rivalries, etc.) with hierarchical relationship type taxonomy definitions. Supports bidirectional uniqueness enforcement via composite keys, polymorphic entity types, soft-deletion with the ability to recreate ended relationships, hierarchical type definitions with parent-child hierarchy, inverse type tracking, bidirectional flags, deprecation with merge capability, and bulk seeding with dependency-ordered creation. Used by the Character service for managing inter-character bonds and family tree categorization, and by the Storyline service for narrative generation.

This plugin was consolidated from the former `lib-relationship` and `lib-relationship-type` plugins into a single service. Type merge operations now call internal methods directly rather than going through HTTP round-trips.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Persistence for relationship records, type definitions, and all indexes (two separate state stores) |
| lib-messaging (`IMessageBus`) | Publishing lifecycle events and error events |
| lib-messaging (`IEventConsumer`) | Event registration infrastructure (no current handlers) |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-character | Calls `IRelationshipClient` for family tree building (entity listing + type code lookup) and reference counting during compression eligibility checks |
| lib-storyline | Injects `IRelationshipClient` for relationship data and type lookups during narrative generation |

No services subscribe to relationship events.

---

## State Storage

### Relationship Store

**Store**: `relationship-statestore` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `rel:{relationshipId}` | `RelationshipModel` | Full relationship record |
| `entity-idx:{entityType}:{entityId}` | `List<Guid>` | Relationship IDs involving this entity |
| `type-idx:{relationshipTypeId}` | `List<Guid>` | Relationship IDs of this type |
| `composite:{entity1}:{entity2}:{typeId}` | `string` | Uniqueness constraint (normalized bidirectional key -> relationship ID) |

### Relationship Type Store

**Store**: `relationship-type-statestore` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `type:{typeId}` | `RelationshipTypeModel` | Individual type definition |
| `code-index:{CODE}` | `string` | Code to type ID reverse lookup (uppercase normalized) |
| `parent-index:{parentId}` | `List<Guid>` | Child type IDs for a parent |
| `all-types` | `List<Guid>` | Global index of all type IDs |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `relationship.created` | `RelationshipCreatedEvent` | New relationship established |
| `relationship.updated` | `RelationshipUpdatedEvent` | Metadata or relationship type changed (includes `changedFields`) |
| `relationship.deleted` | `RelationshipDeletedEvent` | Relationship ended (soft-delete) |
| `relationship-type.created` | `RelationshipTypeCreatedEvent` | New type created |
| `relationship-type.updated` | `RelationshipTypeUpdatedEvent` | Type fields changed (includes `ChangedFields`) |
| `relationship-type.deleted` | `RelationshipTypeDeletedEvent` | Type hard-deleted |

Events are auto-generated from `x-lifecycle` in `relationship-events.yaml`.

### Consumed Events

This plugin does not consume external events.

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `SeedPageSize` | `RELATIONSHIP_SEED_PAGE_SIZE` | `100` | Batch size for paginated relationship migration during merge |
| `MaxHierarchyDepth` | `RELATIONSHIP_MAX_HIERARCHY_DEPTH` | `20` | Maximum depth for hierarchy traversal to prevent infinite loops |
| `MaxMigrationErrorsToTrack` | `RELATIONSHIP_MAX_MIGRATION_ERRORS_TO_TRACK` | `100` | Maximum number of individual migration error details to track |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<RelationshipService>` | Scoped | Structured logging |
| `RelationshipServiceConfiguration` | Singleton | All 3 config properties |
| `IStateStoreFactory` | Singleton | State store access (both stores) |
| `IMessageBus` | Scoped | Event publishing and error events |
| `IEventConsumer` | Scoped | Event handler registration |

Service lifetime is **Scoped** (per-request). No background services.

---

## API Endpoints (Implementation Notes)

### Relationship Endpoints (7 endpoints)

- **Create** (`/relationship/create`): Validates entities are not the same (prevents self-relationships). Normalizes composite key bidirectionally (`A->B` and `B->A` produce the same key via string sort). Stores four keys: relationship data, two entity indexes, type index, and composite uniqueness key. Publishes `relationship.created`.

- **Get** (`/relationship/get`): Simple key lookup by relationship ID.

- **ListByEntity** (`/relationship/list-by-entity`): Loads all relationship IDs from entity index, bulk-fetches models, filters in-memory (active/ended, type, etc.), then applies pagination. Returns full `RelationshipListResponse` with pagination metadata.

- **GetBetween** (`/relationship/get-between`): Fetches entity1's full relationship list, filters in-memory for those involving entity2. Does not paginate (returns all matching relationships). Always reports `HasNextPage: false`.

- **ListByType** (`/relationship/list-by-type`): Loads from type index, bulk-fetches, applies in-memory filtering and pagination.

- **Update** (`/relationship/update`): Can modify metadata and relationship type. When type changes, updates type indexes (removes from old, adds to new). Immutable fields: entity1, entity2 (cannot change participants). Ended relationships cannot be updated (returns Conflict). Publishes `relationship.updated` with `changedFields`.

- **End** (`/relationship/end`): Soft-deletes by setting `EndedAt` timestamp. Deletes the composite uniqueness key (allowing the same relationship to be recreated later). Does NOT remove from entity or type indexes (keeping history queryable). Publishes `relationship.deleted`.

### Relationship Type Endpoints (13 endpoints)

#### Read Operations (6 endpoints)

- **GetRelationshipType** (`/relationship/get-type`): Direct lookup by type ID. Returns full definition with parent/inverse references.
- **GetRelationshipTypeByCode** (`/relationship/get-type-by-code`): Code index lookup (uppercase-normalized). Returns NotFound if code index missing or model missing.
- **ListRelationshipTypes** (`/relationship/list-types`): Loads all IDs from `all-types`, bulk-loads types. Filters by `category`, `rootsOnly`, `includeDeprecated`. **Note**: `includeChildren` parameter is ignored (see Stubs section). In-memory filtering (no pagination support).
- **GetChildRelationshipTypes** (`/relationship/get-children`): Verifies parent exists, loads child IDs from parent index. Supports `recursive` flag for full subtree traversal with depth limit.
- **MatchesHierarchy** (`/relationship/matches-hierarchy`): Checks if a type matches or descends from an ancestor type. Walks parent chain iteratively. Returns `{ Matches: bool, Depth: int }` where depth is 0 for same type, -1 for no match.
- **GetAncestors** (`/relationship/get-ancestors`): Returns full ancestry chain from type up to root. Walks parent pointers iteratively with `MaxHierarchyDepth` limit.

#### Write Operations (7 endpoints)

- **CreateRelationshipType** (`/relationship/create-type`): Normalizes code to uppercase. Validates parent exists (if specified). Resolves inverse type ID by code. Calculates depth from parent. Updates all indexes (code, parent, all-types). Publishes `relationship-type.created`.
- **UpdateRelationshipType** (`/relationship/update-type`): Partial update with `changedFields` tracking. Code is immutable. Parent reassignment validates no cycle via `WouldCreateCycleAsync`, updates parent indexes, and recalculates depth. Publishes update event only if changes detected.
- **DeleteRelationshipType** (`/relationship/delete-type`): Requires deprecation (Conflict if not deprecated). Checks for existing relationships via internal type index lookup (Conflict if any, including ended). Checks no child types exist (Conflict if any). Removes from all indexes (code, parent, all-types). Publishes `relationship-type.deleted`.
- **DeprecateRelationshipType** (`/relationship/deprecate-type`): Sets `IsDeprecated=true` with timestamp and optional reason. Returns Conflict if already deprecated.
- **UndeprecateRelationshipType** (`/relationship/undeprecate-type`): Clears `IsDeprecated`, `DeprecatedAt`, and `DeprecationReason`. Returns Conflict if not deprecated.
- **MergeRelationshipType** (`/relationship/merge-type`): Source must be deprecated (BadRequest otherwise). Paginates through relationships via internal `ListRelationshipsByTypeAsync` call using `SeedPageSize`. Updates each to target type via internal `UpdateRelationshipAsync`. Partial failures tracked (max `MaxMigrationErrorsToTrack` error details). Publishes error event via `TryPublishErrorAsync` if any failures. Optional `deleteAfterMerge` deletes source if all migrations succeed.
- **SeedRelationshipTypes** (`/relationship/seed-types`): Dependency-ordered bulk creation. Multi-pass algorithm: in each pass, processes types whose parents are already created or have no parent. Max iterations = `pending.Count * 2`. Resolves parent/inverse types by code. Supports `updateExisting` flag. Returns created/updated/skipped/error counts.

---

## Visual Aid

```
Composite Key Normalization (Bidirectional Uniqueness)
=======================================================

Create: Entity A (Character) -> Entity B (Character), TypeId: {guid}

  Step 1: Build composite components
    key1 = "Character:{guidA}"
    key2 = "Character:{guidB}"

  Step 2: Normalize via string sort (Ordinal comparison)
    "Character:{guidA}" < "Character:{guidB}" -> no swap needed
    composite = "composite:Character:{guidA}:Character:{guidB}:{typeGuid}"

  Step 3: Check uniqueness
    IF composite key exists -> 409 Conflict
    ELSE -> create relationship

Create: Entity B (Character) -> Entity A (Character), TypeId: {guid}
  -> Same composite key (sorted) -> 409 Conflict (already exists)

End relationship:
  -> Composite key DELETED
  -> Relationship can be recreated


Relationship Type Hierarchy
==============================

  Example Type Tree:

  FAMILY (root, depth=0)
    ├── PARENT (depth=1)
    │    ├── FATHER (depth=2)
    │    └── MOTHER (depth=2)
    ├── CHILD (depth=1)
    │    ├── SON (depth=2)
    │    └── DAUGHTER (depth=2)
    └── SIBLING (depth=1)
         ├── BROTHER (depth=2)
         └── SISTER (depth=2)

  SOCIAL (root, depth=0)
    ├── FRIEND (depth=1)
    ├── RIVAL (depth=1)
    └── MENTOR (depth=1)
         └── STUDENT (inverse of MENTOR)


MatchesHierarchy Query
========================

  MatchesHierarchy(typeId="SON", ancestorTypeId=<FAMILY_GUID>)
       │
       ├── Walk parent chain: SON → CHILD → FAMILY
       ├── Match found at FAMILY
       └── Returns: { Matches: true, Depth: 2 }

  Use case: "Find all FAMILY relationships" matches
  SON, DAUGHTER, FATHER, MOTHER, BROTHER, SISTER, etc.


Merge Operation (Internal)
============================

  MergeRelationshipType(source=ACQUAINTANCE, target=FRIEND)
       │
       ├── Validate: source deprecated, target exists
       │
       ├── Paginated internal migration:
       │    ├── this.ListRelationshipsByTypeAsync(ACQUAINTANCE, page, pageSize)
       │    ├── For each relationship:
       │    │    └── this.UpdateRelationshipAsync(typeId=FRIEND)
       │    └── Next page
       │
       └── Response: { MigratedCount, FailedCount, Errors[] }


Seed Dependency Resolution
=============================

  Input: [CHILD, FAMILY, SON, DAUGHTER]
  (SON.parent=CHILD, CHILD.parent=FAMILY, DAUGHTER.parent=CHILD)

  Pass 1: Create FAMILY (no parent dependency)
  Pass 2: Create CHILD (parent=FAMILY now exists)
  Pass 3: Create SON, DAUGHTER (parent=CHILD now exists)

  Max iterations guard prevents infinite loops from circular references.


State Store Layout
===================

  ┌─ relationship store ────────────────────────────────────┐
  │                                                          │
  │  rel:{relId} → RelationshipModel                         │
  │  entity-idx:{entityType}:{entityId} → [relId, ...]       │
  │  type-idx:{typeId} → [relId, ...]                        │
  │  composite:{e1}:{e2}:{typeId} → relId                    │
  └──────────────────────────────────────────────────────────┘

  ┌─ relationship-type store ────────────────────────────────┐
  │                                                           │
  │  type:{typeId} → RelationshipTypeModel                    │
  │    ├── Code (uppercase, unique)                           │
  │    ├── Name, Description, Category                        │
  │    ├── ParentTypeId, ParentTypeCode (hierarchy)           │
  │    ├── InverseTypeId, InverseTypeCode (e.g. PARENT↔CHILD)│
  │    ├── IsBidirectional (symmetric relationships)          │
  │    ├── Depth (0 for roots)                                │
  │    └── IsDeprecated, DeprecatedAt, DeprecationReason      │
  │                                                           │
  │  code-index:{CODE} → typeId (string)                      │
  │  parent-index:{parentId} → [childId, childId, ...]        │
  │  all-types → [typeId, typeId, ...]                        │
  └───────────────────────────────────────────────────────────┘
```

---

## Stubs & Unimplemented Features

1. **`IncludeChildren` parameter ignored**: The `ListRelationshipTypesRequest.IncludeChildren` property exists in the schema (default: `true`) but is not used in `ListRelationshipTypesAsync`. The list endpoint always returns all types matching the filters, regardless of this flag's value.
<!-- AUDIT:NEEDS_DESIGN:2026-02-01:https://github.com/beyond-immersion/bannou-service/issues/233 -->

---

## Potential Extensions

1. **Relationship strength/weight**: Numeric field for weighted relationship graphs (e.g., closeness scores).
2. **Bidirectional asymmetric metadata**: Allow entity1 and entity2 to have independent metadata perspectives on the same relationship.
3. **Cascade cleanup on entity deletion**: Automatically end all relationships when an entity is permanently deleted.
4. **Pagination for GetBetween**: Currently returns all relationships between two entities without pagination support.
5. **Type constraints**: Define which entity types can participate in each relationship type (e.g., PARENT only between characters, not guilds).
6. **Relationship strength modifiers**: Associate default strength/weight values per type for relationship scoring.
7. **Category-based permissions**: Allow different roles to create relationships of different categories.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

1. **`EndRelationshipAsync` ignores `reason` field**: The `EndRelationshipRequest` schema defines a `reason` field (string, nullable, maxLength 500), but the implementation never reads `body.Reason`. Instead, it passes the hardcoded string `"Relationship ended"` as `deletedReason` to `PublishRelationshipDeletedEventAsync`. The request's reason is silently discarded.

### Intentional Quirks (Documented Behavior)

1. **Self-relationship with different types allowed**: The self-relationship check compares both ID and type. Entity A (type: character) -> Entity A (type: npc) is allowed. This supports entities that span multiple type classifications.

2. **Ended relationships remain in indexes**: When a relationship ends, entity and type indexes retain the relationship ID. This preserves queryability of historical relationships but requires filtering in read paths.

3. **Composite key deletion on end**: Ending a relationship removes the uniqueness constraint, explicitly allowing the same pair of entities to form a new relationship of the same type. This models "breaking up and getting back together" scenarios.

4. **GetBetween only checks entity1's index**: Looks up entity1's relationships and filters for entity2. If entity1's index is corrupted but entity2's is correct, the relationship won't be found. No cross-validation against entity2's index.

5. **Update rejects ended relationships**: Attempting to update a relationship that has `EndedAt` set returns `StatusCodes.Conflict`. This prevents modifications to historical records.

6. **No event consumption by design**: The schema explicitly declares `x-event-subscriptions: []`. Event consumer registration is called in the constructor but uses the default no-op implementation. Type usage checks (e.g., during delete) are performed on-demand via internal state store lookups rather than maintaining cached counts via event subscriptions. This avoids complexity without sacrificing functionality.

7. **Type depth auto-calculated but not updated**: Depth is `parent.Depth + 1` at creation time. If a parent's depth later changes (e.g., its own parent is reassigned), children are NOT automatically updated. Depth recalculation would require traversing all descendants.

8. **Partial merge failure leaves inconsistent state**: If merge fails midway, some relationships are migrated and others are not. No rollback mechanism exists. The response reports counts (`RelationshipsMigrated`, `RelationshipsFailed`, `MigrationErrors`) for manual resolution.

9. **Merge page fetch error stops pagination**: If fetching a page of relationships fails during merge, the migration loop terminates with `hasMorePages = false`. Relationships on subsequent pages are silently un-migrated.

10. **Code normalization is case-insensitive**: All type codes are converted to uppercase on creation (`body.Code.ToUpperInvariant()`) and lookup. "friend" and "FRIEND" resolve to the same type.

11. **Inverse type resolved by code, not ID**: When creating/updating a type with `InverseTypeCode`, the ID is resolved via index lookup at that moment. If the inverse type is later deleted, `InverseTypeId` becomes stale (points to non-existent type).

12. **Merge calls public endpoint methods, not direct state store operations**: `MergeRelationshipTypeAsync` calls `this.ListRelationshipsByTypeAsync()` and `this.UpdateRelationshipAsync()` internally. While this avoids HTTP round-trips (both methods are in the same service), it still goes through the full public endpoint logic: constructing request/response models, publishing update events for each migrated relationship, and returning status code tuples. A deeper internalization would read the `type-idx` directly and bulk-update state store records, avoiding per-relationship event publishing and response model overhead.
<!-- AUDIT:NEEDS_DESIGN:2026-02-08:https://github.com/beyond-immersion/bannou-service/issues/333 -->

### Design Considerations (Requires Planning)

1. **In-memory filtering before pagination**: All list operations load the full index, bulk-fetch all relationship models, filter in memory, then paginate. For entities with thousands of relationships, this loads everything into memory before applying page limits.

2. **No index cleanup**: Entity and type indexes accumulate relationship IDs indefinitely (both active and ended). Over time, indexes grow large with ended relationships that must be filtered on every query.

3. **No optimistic concurrency**: Updates overwrite without version checking. Two concurrent updates to metadata will result in last-writer-wins with no conflict detection.

4. **Type migration during merge**: Merge operations modify type indexes atomically but without distributed transaction guarantees. A crash between removing from old index and adding to new could leave the relationship in neither index.

5. **Read-modify-write without distributed locks**: Index updates (add/remove from list) and composite key checks have no concurrency protection. Two concurrent creates with the same composite key could both pass the uniqueness check if timed precisely. Requires IDistributedLockProvider integration.
<!-- AUDIT:NEEDS_DESIGN:2026-02-01:https://github.com/beyond-immersion/bannou-service/issues/223 -->

6. **Recursive child query unbounded**: `GetChildRelationshipTypesAsync` with `recursive=true` traverses the full subtree. Deep hierarchies with many branches could generate many state store calls. The `MaxHierarchyDepth` limit bounds depth but not breadth.

7. **Seed multi-pass has fixed iteration limit**: The dependency resolution algorithm uses `maxIterations = pending.Count * 2`. For a list of N types, max 2N passes. Extremely deep dependency chains could fail to resolve within the limit.

8. **Read-modify-write on type indexes without distributed locks**: Index updates (`AddToParentIndexAsync`, `RemoveFromParentIndexAsync`, `AddToAllTypesListAsync`, `RemoveFromAllTypesListAsync`) perform read-modify-write without distributed locks. Concurrent operations on the same indexes can cause lost updates.

9. **Delete-after-merge skipped on partial failure**: When `deleteAfterMerge=true` but some relationships failed to migrate, the source type is NOT deleted. This prevents data loss but leaves the deprecated type with remaining relationships that need manual cleanup.

---

## Work Tracking

*This section tracks active development work. Markers are managed by `/audit-plugin`.*
