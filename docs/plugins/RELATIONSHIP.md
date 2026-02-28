# Relationship Plugin Deep Dive

> **Plugin**: lib-relationship
> **Schema**: schemas/relationship-api.yaml
> **Version**: 2.0.0
> **Layer**: GameFoundation
> **State Stores**: relationship-statestore (MySQL), relationship-type-statestore (MySQL), relationship-lock (Redis)

---

## Overview

A unified relationship management service (L2 GameFoundation) combining entity-to-entity relationships (character friendships, alliances, rivalries) with hierarchical relationship type taxonomy definitions. Supports bidirectional uniqueness enforcement, polymorphic entity types, soft-deletion with recreate capability, type deprecation with merge, and bulk seeding. Used by the Character service for inter-character bonds and family tree categorization, and by the Storyline service for narrative generation. Consolidated from the former separate relationship and relationship-type plugins.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Persistence for relationship records, type definitions, and all indexes (two separate state stores) |
| lib-state (`IDistributedLockProvider`) | Distributed locks for composite uniqueness enforcement and index read-modify-write operations |
| lib-messaging (`IMessageBus`) | Publishing lifecycle events and error events |
| lib-messaging (`IEventConsumer`) | Event registration infrastructure (no current handlers) |
| lib-resource (`IResourceClient`) | Cleanup callback registration for character and realm reference tracking (via `x-references`) |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-character | Calls `IRelationshipClient` for family tree building (entity listing + type code lookup) and reference counting during compression eligibility checks |
| lib-storyline | Injects `IRelationshipClient` for relationship data and type lookups during narrative generation |

No services subscribe to relationship events.

---

### Type Field Classification

| Field | Category | Type | Rationale |
|-------|----------|------|-----------|
| `entity1Type` / `entity2Type` | A (Entity Reference) | `EntityType` enum (via `$ref` to `common-api.yaml`) | Identifies which first-class Bannou entity participates in the relationship. All valid values are Bannou entities (character, account, realm, etc.) |
| `sourceEntityType` / `targetEntityType` | A (Entity Reference) | `EntityType` enum (via `$ref` to `common-api.yaml`) | Same as above, used in query/filter contexts to find relationships involving a specific entity type |
| `relationshipTypeCode` | B (Content Code) | Opaque string | Game-configurable taxonomy codes (PARENT, FRIEND, RIVAL, WEAPON_WIELDER, etc.). Registered via API, not hardcoded. Uppercase-normalized. New codes added without schema changes |
| `category` | B (Content Code) | Opaque string | Grouping label for relationship types (e.g., "FAMILY", "SOCIAL", "POLITICAL"). Game-configurable, used for filtering |

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
| `relationship-type.merged` | `RelationshipTypeMergedEvent` | Merge operation completed (summary event replacing N individual updates) |

Lifecycle events are auto-generated from `x-lifecycle` in `relationship-events.yaml`. The `RelationshipTypeMergedEvent` is manually defined in the same schema's `components/schemas` section.

### Consumed Events

This plugin does not consume external events.

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `MaxHierarchyDepth` | `RELATIONSHIP_TYPE_MAX_HIERARCHY_DEPTH` | `20` | Maximum depth for hierarchy traversal to prevent infinite loops |
| `MaxMigrationErrorsToTrack` | `RELATIONSHIP_TYPE_MAX_MIGRATION_ERRORS_TO_TRACK` | `100` | Maximum number of individual migration error details to track |
| `LockTimeoutSeconds` | `RELATIONSHIP_LOCK_TIMEOUT_SECONDS` | `30` | Timeout in seconds for distributed lock acquisition on index and uniqueness operations |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<RelationshipService>` | Scoped | Structured logging |
| `RelationshipServiceConfiguration` | Singleton | All 4 config properties |
| `IStateStoreFactory` | Singleton | State store access (both stores) |
| `IDistributedLockProvider` | Singleton | Distributed locks for concurrency protection |
| `IMessageBus` | Scoped | Event publishing and error events |
| `IEventConsumer` | Scoped | Event handler registration |

Service lifetime is **Scoped** (per-request). No background services.

---

## API Endpoints (Implementation Notes)

### Relationship Endpoints (8 endpoints)

- **Create** (`/relationship/create`): Validates entities are not the same (prevents self-relationships). Normalizes composite key bidirectionally (`A->B` and `B->A` produce the same key via string sort). Stores four keys: relationship data, two entity indexes, type index, and composite uniqueness key. Publishes `relationship.created`.

- **Get** (`/relationship/get`): Simple key lookup by relationship ID.

- **ListByEntity** (`/relationship/list-by-entity`): Loads all relationship IDs from entity index, bulk-fetches models, filters in-memory (active/ended, type, etc.), then applies pagination. Returns full `RelationshipListResponse` with pagination metadata.

- **GetBetween** (`/relationship/get-between`): Fetches entity1's full relationship list, filters in-memory for those involving entity2. Supports pagination via `page` and `pageSize` parameters (defaults: 1 and 20).

- **ListByType** (`/relationship/list-by-type`): Loads from type index, bulk-fetches, applies in-memory filtering and pagination.

- **Update** (`/relationship/update`): Can modify metadata and relationship type. When type changes, updates type indexes (adds to new first, then removes from old - crash-safe ordering). Immutable fields: entity1, entity2 (cannot change participants). Ended relationships cannot be updated (returns Conflict). Publishes `relationship.updated` with `changedFields`. Protected by distributed lock on relationship ID.

- **End** (`/relationship/end`): Soft-deletes by setting `EndedAt` timestamp. Returns Conflict if already ended. Deletes the composite uniqueness key (allowing the same relationship to be recreated later). Does NOT remove from entity or type indexes (keeping history queryable). Publishes `relationship.deleted` with optional reason from the request (defaults to "Relationship ended" when null).

- **CleanupByEntity** (`/relationship/cleanup-by-entity`): Called by lib-resource during cascading entity deletion. Loads the entity index for the deleted entity, bulk-fetches all relationships, and soft-deletes (ends) each active relationship. Skips already-ended relationships. Clears composite uniqueness keys for ended relationships to allow future recreation. Publishes `relationship.deleted` events for each ended relationship with reason "Entity deleted (cascade cleanup)". Returns counts of ended and already-ended relationships. Requires `developer` role (internal service-to-service only). Registered via `x-references` for character and realm targets.

### Relationship Type Endpoints (13 endpoints)

#### Read Operations (6 endpoints)

- **GetRelationshipType** (`/relationship-type/get`): Direct lookup by type ID. Returns full definition with parent/inverse references.
- **GetRelationshipTypeByCode** (`/relationship-type/get-by-code`): Code index lookup (uppercase-normalized). Returns NotFound if code index missing or model missing.
- **ListRelationshipTypes** (`/relationship-type/list`): Loads all IDs from `all-types`, bulk-loads types. Filters by `category`, `rootsOnly`, `includeDeprecated`. In-memory filtering (no pagination support).
- **GetChildRelationshipTypes** (`/relationship-type/get-children`): Verifies parent exists, loads child IDs from parent index. Supports `recursive` flag for full subtree traversal with depth limit.
- **MatchesHierarchy** (`/relationship-type/matches-hierarchy`): Checks if a type matches or descends from an ancestor type. Walks parent chain iteratively. Returns `{ Matches: bool, Depth: int }` where depth is 0 for same type, -1 for no match.
- **GetAncestors** (`/relationship-type/get-ancestors`): Returns full ancestry chain from type up to root. Walks parent pointers iteratively with `MaxHierarchyDepth` limit.

#### Write Operations (7 endpoints)

- **CreateRelationshipType** (`/relationship-type/create`): Normalizes code to uppercase. Validates parent exists (if specified). Resolves inverse type ID by code. Calculates depth from parent. Updates all indexes (code, parent, all-types). Publishes `relationship-type.created`.
- **UpdateRelationshipType** (`/relationship-type/update`): Partial update with `changedFields` tracking. Code is immutable. Parent reassignment validates no cycle via `WouldCreateCycleAsync`, updates parent indexes, and recalculates depth. Publishes update event only if changes detected.
- **DeleteRelationshipType** (`/relationship-type/delete`): Requires deprecation (Conflict if not deprecated). Checks for existing relationships via internal type index lookup (Conflict if any, including ended). Checks no child types exist (Conflict if any). Removes from all indexes (code, parent, all-types). Publishes `relationship-type.deleted`.
- **DeprecateRelationshipType** (`/relationship-type/deprecate`): Sets `IsDeprecated=true` with timestamp and optional reason. Idempotent — returns OK with current state if already deprecated.
- **UndeprecateRelationshipType** (`/relationship-type/undeprecate`): Clears `IsDeprecated`, `DeprecatedAt`, and `DeprecationReason`. Idempotent — returns OK with current state if not deprecated.
- **MergeRelationshipType** (`/relationship-type/merge`): Source must be deprecated (BadRequest otherwise). Target must not be deprecated (Conflict otherwise). Acquires distributed locks on both source and target type indexes (source first for deterministic ordering). Reads both type indexes directly from state store, bulk-loads all source relationships via `GetBulkAsync`. Per-relationship migration under individual lock: deletes old composite key, checks for target composite key collision (ends relationship as duplicate if collision detected), creates new composite key for active relationships. Batch-updates both type indexes after all migrations. Partial failures tracked (max `MaxMigrationErrorsToTrack` error details). Publishes error event via `TryPublishErrorAsync` if any failures. Publishes single `RelationshipTypeMergedEvent` summary event. Optional `deleteAfterMerge` deletes source if all migrations succeed.
- **SeedRelationshipTypes** (`/relationship-type/seed`): Dependency-ordered bulk creation. Multi-pass algorithm: in each pass, processes types whose parents are already created or have no parent. Max iterations = `pending.Count * 2`. Resolves parent/inverse types by code. Supports `updateExisting` flag. Returns created/updated/skipped/error counts.

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


Merge Operation (Internalized Bulk)
=====================================

  MergeRelationshipType(source=ACQUAINTANCE, target=FRIEND)
       │
       ├── Validate: source deprecated, target exists, target not deprecated
       │
       ├── Acquire locks: source type index, target type index
       │    (deterministic ordering: source before target to prevent deadlocks)
       │
       ├── Read indexes directly from state store:
       │    ├── type-idx:{ACQUAINTANCE} → [relId1, relId2, ...]
       │    └── type-idx:{FRIEND} → [relId3, relId4, ...]
       │
       ├── Bulk-load all source relationships (GetBulkAsync)
       │
       ├── Per-relationship migration (with individual lock):
       │    ├── Re-read model under lock (may have changed since bulk load)
       │    ├── Delete composite:{e1}:{e2}:{ACQUAINTANCE}
       │    ├── If active (not ended): try-create composite:{e1}:{e2}:{FRIEND}
       │    │    ├── Success → update model, track for index batch
       │    │    └── Collision → end relationship as duplicate, track error
       │    └── Save model
       │
       ├── Batch index update (both locks held):
       │    ├── type-idx:{FRIEND} += migrated IDs
       │    └── type-idx:{ACQUAINTANCE} -= migrated + collision IDs
       │
       ├── Release type index locks
       │
       ├── Publish single RelationshipTypeMergedEvent
       │
       ├── If deleteAfterMerge && failedCount == 0:
       │    └── DeleteRelationshipTypeAsync(ACQUAINTANCE)
       │
       └── Response: { MigratedCount, FailedCount, Errors[], SourceDeleted }


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

*No stubs identified.*

---

## Potential Extensions

1. **Relationship strength/weight**: Numeric field for weighted relationship graphs (e.g., closeness scores).
2. **Bidirectional asymmetric metadata**: Allow entity1 and entity2 to have independent metadata perspectives on the same relationship.
3. **Type constraints**: Define which entity types can participate in each relationship type (e.g., PARENT only between characters, not guilds).
<!-- AUDIT:NEEDS_DESIGN:2026-02-08:https://github.com/beyond-immersion/bannou-service/issues/338 -->
4. **Relationship strength modifiers**: Associate default strength/weight values per type for relationship scoring.
5. **Category-based permissions**: Allow different roles to create relationships of different categories.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

*No bugs identified.*

### Intentional Quirks (Documented Behavior)

1. **Self-relationship with different types allowed**: The self-relationship check compares both ID and type. Entity A (type: character) -> Entity A (type: npc) is allowed. This supports entities that span multiple type classifications.

2. **Ended relationships remain in indexes**: When a relationship ends, entity and type indexes retain the relationship ID. This preserves queryability of historical relationships but requires filtering in read paths.

3. **Composite key deletion on end**: Ending a relationship removes the uniqueness constraint, explicitly allowing the same pair of entities to form a new relationship of the same type. This models "breaking up and getting back together" scenarios.

4. **GetBetween only checks entity1's index**: Looks up entity1's relationships and filters for entity2. If entity1's index is corrupted but entity2's is correct, the relationship won't be found. No cross-validation against entity2's index.

5. **Update rejects ended relationships**: Attempting to update a relationship that has `EndedAt` set returns `StatusCodes.Conflict`. This prevents modifications to historical records.

6. **No event consumption by design**: The schema explicitly declares `x-event-subscriptions: []`. Event consumer registration is called in the constructor but uses the default no-op implementation. Type usage checks (e.g., during delete) are performed on-demand via internal state store lookups rather than maintaining cached counts via event subscriptions. This avoids complexity without sacrificing functionality.

7. **Type depth auto-calculated but not updated**: Depth is `parent.Depth + 1` at creation time. If a parent's depth later changes (e.g., its own parent is reassigned), children are NOT automatically updated. Depth recalculation would require traversing all descendants.

8. **Partial merge failure leaves inconsistent state**: If a per-relationship error occurs during merge, that relationship remains in its original state (type indexes and composite keys are updated atomically per-relationship under lock). The response reports counts (`RelationshipsMigrated`, `RelationshipsFailed`, `MigrationErrors`) for manual resolution. Both type index locks are held throughout the entire merge operation.

9. **Merge composite key collision ends duplicate**: When migrating an active relationship and the target type already has an active relationship for the same entity pair, the source relationship is ended as a duplicate (soft-deleted) rather than silently skipped. This preserves data integrity by surfacing genuine conflicts.

10. **Code normalization is case-insensitive**: All type codes are converted to uppercase on creation (`body.Code.ToUpperInvariant()`) and lookup. "friend" and "FRIEND" resolve to the same type.

11. **Inverse type resolved by code, not ID**: When creating/updating a type with `InverseTypeCode`, the ID is resolved via index lookup at that moment. If the inverse type is later deleted, `InverseTypeId` becomes stale (points to non-existent type).

12. **Merge holds type index locks for full duration**: The internalized merge acquires distributed locks on both source and target type indexes at the start and holds them throughout the entire bulk migration. This prevents concurrent modifications but means long-running merges (thousands of relationships) block other operations on those type indexes. The per-relationship lock is acquired and released individually within the loop.

13. **Recursive child query breadth-unbounded**: `GetChildRelationshipTypesAsync` with `recursive=true` traverses the full subtree. `MaxHierarchyDepth` (default 20) bounds depth but not breadth. This is acceptable because relationship types are admin-curated taxonomies with realistic totals of ~50-100 types, making breadth explosion a theoretical rather than practical concern.

14. **Seed multi-pass iteration limit is `2*N`**: The dependency resolution algorithm uses `maxIterations = pending.Count * 2`. This limit is provably unreachable: each iteration processes at least one type (or the loop breaks early with unresolvable parent errors), so N types require at most N iterations. The `2*N` limit exists as an infinite loop guard but cannot be hit in practice.

### Design Considerations (Requires Planning)

1. **In-memory filtering before pagination**: All list operations load the full index, bulk-fetch all relationship models, filter in memory, then paginate. For entities with thousands of relationships, this loads everything into memory before applying page limits.

2. **No index cleanup**: Entity and type indexes accumulate relationship IDs indefinitely (both active and ended). Over time, indexes grow large with ended relationships that must be filtered on every query.

3. **Delete-after-merge skipped on partial failure**: When `deleteAfterMerge=true` but some relationships failed to migrate, the source type is NOT deleted. This prevents data loss but leaves the deprecated type with remaining relationships that need manual cleanup.

---

## Work Tracking

*This section tracks active development work. Markers are managed by `/audit-plugin`.*

### Pending Issues

- [#147](https://github.com/beyond-immersion/bannou-service/issues/147): Implement RelationshipProviderFactory (`IVariableProviderFactory`) for ABML `${relationship.*}` variable namespace — cache, factory, and provider classes
- [#338](https://github.com/beyond-immersion/bannou-service/issues/338): Type constraints — define which entity types can participate in each relationship type (Potential Extension #3)
