# Relationship Type Plugin Deep Dive

> **Plugin**: lib-relationship-type
> **Schema**: schemas/relationship-type-api.yaml
> **Version**: 2.0.0
> **State Store**: relationship-type-statestore (MySQL)

---

## Overview

Hierarchical relationship type definitions for entity-to-entity relationships in the Arcadia game world. Defines the taxonomy of possible relationships (e.g., PARENT → FATHER/MOTHER, FRIEND, RIVAL) with parent-child hierarchy, inverse type tracking, and bidirectional flags. Supports deprecation with merge capability via `IRelationshipClient` to migrate existing relationships. Provides hierarchy queries (ancestors, children, `matchesHierarchy` for polymorphic matching), code-based lookups, and bulk seeding with dependency-ordered creation. Internal-only service (not internet-facing).

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | MySQL persistence for type definitions, code indexes, parent indexes |
| lib-messaging (`IMessageBus`) | Publishing lifecycle events; error event publishing |
| lib-messaging (`IEventConsumer`) | Event handler registration (partial class pattern) |
| lib-relationship (`IRelationshipClient`) | Listing and migrating relationships during merge operations |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-character | Uses `IRelationshipTypeClient` for type validation in character relationships |

Note: lib-relationship does not currently use `IRelationshipTypeClient`. The dependency is inverted - relationship-type depends on lib-relationship (`IRelationshipClient`) for merge operations, not the other way around.

---

## State Storage

**Store**: `relationship-type-statestore` (via `StateStoreDefinitions.RelationshipType`)
**Backend**: MySQL

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `type:{typeId}` | `RelationshipTypeModel` | Individual type definition |
| `code-index:{CODE}` | `string` | Code → type ID reverse lookup (uppercase normalized) |
| `parent-index:{parentId}` | `List<Guid>` | Child type IDs for a parent |
| `all-types` | `List<Guid>` | Global index of all type IDs |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `relationship-type.created` | `RelationshipTypeCreatedEvent` | New type created |
| `relationship-type.updated` | `RelationshipTypeUpdatedEvent` | Type fields changed (includes `ChangedFields`) |
| `relationship-type.deleted` | `RelationshipTypeDeletedEvent` | Type hard-deleted |

### Consumed Events

This plugin does not consume external events.

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `SeedPageSize` | `RELATIONSHIP_TYPE_SEED_PAGE_SIZE` | `100` | Batch size for paginated relationship migration during merge |
| `MaxHierarchyDepth` | `RELATIONSHIP_TYPE_MAX_HIERARCHY_DEPTH` | `20` | Maximum depth for hierarchy traversal to prevent infinite loops |
| `MaxMigrationErrorsToTrack` | `RELATIONSHIP_TYPE_MAX_MIGRATION_ERRORS_TO_TRACK` | `100` | Maximum number of individual migration error details to track |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<RelationshipTypeService>` | Scoped | Structured logging |
| `RelationshipTypeServiceConfiguration` | Singleton | All 3 config properties |
| `IStateStoreFactory` | Singleton | State store access |
| `IMessageBus` | Scoped | Event publishing and error events |
| `IEventConsumer` | Scoped | Event handler registration |
| `IRelationshipClient` | Scoped | Relationship migration during merge |

Service lifetime is **Scoped** (per-request). No background services.

---

## API Endpoints (Implementation Notes)

### Read Operations (6 endpoints)

- **GetRelationshipType** (`/relationship-type/get`): Direct lookup by type ID. Returns full definition with parent/inverse references.
- **GetRelationshipTypeByCode** (`/relationship-type/get-by-code`): Code index lookup (uppercase-normalized). Returns NotFound if code index missing or model missing.
- **ListRelationshipTypes** (`/relationship-type/list`): Loads all IDs from `all-types`, bulk-loads types. Filters by `category`, `rootsOnly`, `includeDeprecated`. **Note**: `includeChildren` parameter is ignored (see Stubs section). In-memory filtering (no pagination support).
- **GetChildRelationshipTypes** (`/relationship-type/get-children`): Verifies parent exists, loads child IDs from parent index. Supports `recursive` flag for full subtree traversal with depth limit.
- **MatchesHierarchy** (`/relationship-type/matches-hierarchy`): Checks if a type matches or descends from an ancestor type. Walks parent chain iteratively. Returns `{ Matches: bool, Depth: int }` where depth is 0 for same type, -1 for no match.
- **GetAncestors** (`/relationship-type/get-ancestors`): Returns full ancestry chain from type up to root. Walks parent pointers iteratively with `MaxHierarchyDepth` limit.

### Write Operations (7 endpoints)

- **CreateRelationshipType** (`/relationship-type/create`): Normalizes code to uppercase. Validates parent exists (if specified). Resolves inverse type ID by code. Calculates depth from parent. Updates all indexes (code, parent, all-types). Publishes `relationship-type.created`.
- **UpdateRelationshipType** (`/relationship-type/update`): Partial update with `changedFields` tracking. Code is immutable. Parent reassignment updates parent indexes and recalculates depth. Publishes update event only if changes detected.
- **DeleteRelationshipType** (`/relationship-type/delete`): Checks no child types exist (Conflict if any). Removes from all indexes (code, parent, all-types). Publishes `relationship-type.deleted`. **Note**: See Bugs section - deprecation and reference checks are not enforced.
- **DeprecateRelationshipType** (`/relationship-type/deprecate`): Sets `IsDeprecated=true` with timestamp and optional reason. Returns Conflict if already deprecated.
- **UndeprecateRelationshipType** (`/relationship-type/undeprecate`): Clears `IsDeprecated`, `DeprecatedAt`, and `DeprecationReason`. Returns Conflict if not deprecated.
- **MergeRelationshipType** (`/relationship-type/merge`): Source must be deprecated (BadRequest otherwise). Paginates through relationships via `IRelationshipClient.ListRelationshipsByTypeAsync` using `SeedPageSize`. Updates each to target type. Partial failures tracked (max `MaxMigrationErrorsToTrack` error details). Publishes error event via `TryPublishErrorAsync` if any failures. Optional `deleteAfterMerge` deletes source if all migrations succeed.
- **SeedRelationshipTypes** (`/relationship-type/seed`): Dependency-ordered bulk creation. Multi-pass algorithm: in each pass, processes types whose parents are already created or have no parent. Max iterations = `pending.Count * 2`. Resolves parent/inverse types by code. Supports `updateExisting` flag. Returns created/updated/skipped/error counts.

---

## Visual Aid

```
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


Merge Operation
=================

  MergeRelationshipType(source=ACQUAINTANCE, target=FRIEND)
       │
       ├── Validate: source deprecated, target exists
       │
       ├── Paginated migration:
       │    ├── ListRelationshipsByType(ACQUAINTANCE, page, pageSize)
       │    ├── For each relationship:
       │    │    └── UpdateRelationship(typeId=FRIEND)
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

1. ~~**Circular hierarchy prevention**~~: **FIXED** (2026-02-01) - Added `WouldCreateCycleAsync` helper that walks the ancestor chain from the proposed parent. `UpdateRelationshipTypeAsync` now returns BadRequest if setting a parent would create a cycle. The check uses `MaxHierarchyDepth` as a safety limit and also rejects self-references.
2. **Type constraints**: Define which entity types can participate in each relationship type (e.g., PARENT only between characters, not guilds).
3. **Relationship strength modifiers**: Associate default strength/weight values per type for relationship scoring.
4. **Category-based permissions**: Allow different roles to create relationships of different categories.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

1. ~~**Delete does not enforce deprecation requirement**~~: **FIXED** (2026-02-01) - Added `IsDeprecated` check before deletion. Returns 409 Conflict if type is not deprecated.

2. ~~**Delete does not check for relationship references**~~: **FIXED** (2026-02-01) - Added `IRelationshipClient.ListRelationshipsByTypeAsync` check before deletion. Returns 409 Conflict if any relationships (including ended) reference the type.

### Intentional Quirks (Documented Behavior)

1. **No event consumption by design**: The schema explicitly declares `x-event-subscriptions: []` with the comment "Relationship Type service doesn't subscribe to external events". Event consumer registration is called in the constructor but uses the default no-op implementation. Type usage checks (e.g., during delete) are performed on-demand via `IRelationshipClient.ListRelationshipsByTypeAsync` rather than maintaining cached counts via event subscriptions. This avoids complexity without sacrificing functionality.

2. **Depth auto-calculated but not updated**: Depth is `parent.Depth + 1` at creation time (lines 395-408). If a parent's depth later changes (e.g., its own parent is reassigned), children are NOT automatically updated. Depth recalculation would require traversing all descendants.

2. ~~**Guid.Empty as null marker in events**~~: **FIXED** (2026-02-03) - Schema now specifies `nullable: true` for `parentTypeId`, `inverseTypeId`, `deprecatedAt`, and `metadata`. Generated C# uses `Guid?` and `DateTimeOffset?`, eliminating sentinel values.

3. **Partial merge failure leaves inconsistent state**: If merge fails midway (lines 957-996), some relationships are migrated and others are not. No rollback mechanism exists. The response reports counts (`RelationshipsMigrated`, `RelationshipsFailed`, `MigrationErrors`) for manual resolution.

4. **Merge page fetch error stops pagination**: If fetching a page of relationships fails during merge (lines 991-995), the migration loop terminates with `hasMorePages = false`. Relationships on subsequent pages are silently un-migrated.

5. **Code normalization is case-insensitive**: All codes are converted to uppercase on creation (`body.Code.ToUpperInvariant()`, line 380) and lookup (`body.Code.ToUpperInvariant()`, line 88). "friend" and "FRIEND" resolve to the same type.

6. **Inverse type resolved by code, not ID**: When creating/updating a type with `InverseTypeCode`, the ID is resolved via index lookup at that moment. If the inverse type is later deleted, `InverseTypeId` becomes stale (points to non-existent type).

### Design Considerations (Requires Planning)

1. ~~**No circular hierarchy prevention**~~: **FIXED** (2026-02-01) - Added `WouldCreateCycleAsync` helper in `RelationshipTypeService.cs` that walks the ancestor chain from the proposed parent. If the current type is found as an ancestor (or is the same as the proposed parent), the parent assignment is rejected with BadRequest. Uses `MaxHierarchyDepth` as a safety limit. Existing corrupted data (cycles already in the database) will trigger the safety limit and be treated as a cycle, preventing further corruption.

2. **Recursive child query unbounded**: `GetChildRelationshipTypesAsync` with `recursive=true` traverses the full subtree (lines 1072-1091). Deep hierarchies with many branches could generate many state store calls. The `MaxHierarchyDepth` limit bounds depth but not breadth.

3. **Seed multi-pass has fixed iteration limit**: The dependency resolution algorithm (lines 676-695) uses `maxIterations = pending.Count * 2`. For a list of N types, max 2N passes. Extremely deep dependency chains could fail to resolve within the limit.

4. **Read-modify-write without distributed locks**: Index updates (`AddToParentIndexAsync`, `RemoveFromParentIndexAsync`, `AddToAllTypesListAsync`, `RemoveFromAllTypesListAsync`, lines 1093-1139) perform read-modify-write without distributed locks. Concurrent operations on the same indexes can cause lost updates.

5. **Delete-after-merge skipped on partial failure**: When `deleteAfterMerge=true` but some relationships failed to migrate, the source type is NOT deleted (lines 1020-1026). This prevents data loss but leaves the deprecated type with remaining relationships that need manual cleanup.

---

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above.

### In Progress

- **2026-02-01**: Issue #233 - `IncludeChildren` parameter semantics require design decision (NEEDS_DESIGN)

### Completed

- **2026-02-01**: Audit - Added circular hierarchy prevention via `WouldCreateCycleAsync` helper; parent assignments that would create cycles are now rejected
- **2026-02-01**: Issue #215 - Fixed deletion validation bugs (deprecation requirement + relationship reference check)
- **2026-02-01**: Audit - Moved "No event consumption" from Stubs to Intentional Quirks (by design, schema explicitly declares empty subscriptions, on-demand queries are sufficient)
