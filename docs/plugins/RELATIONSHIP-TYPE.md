# Relationship Type Plugin Deep Dive

> **Plugin**: lib-relationship-type
> **Schema**: schemas/relationship-type-api.yaml
> **Version**: 2.0.0
> **State Store**: relationship-type (Redis/MySQL)

---

## Overview

Hierarchical relationship type definitions for entity-to-entity relationships in the Arcadia game world. Defines the taxonomy of possible relationships (e.g., PARENT → FATHER/MOTHER, FRIEND, RIVAL) with parent-child hierarchy, inverse type tracking, and bidirectional flags. Supports deprecation with merge capability via `IRelationshipClient` to migrate existing relationships. Provides hierarchy queries (ancestors, children, `matchesHierarchy` for polymorphic matching), code-based lookups, and bulk seeding with dependency-ordered creation.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Redis/MySQL persistence for type definitions, code indexes, parent indexes |
| lib-messaging (`IMessageBus`) | Publishing lifecycle events; error event publishing |
| lib-messaging (`IEventConsumer`) | Event handler registration (partial class pattern) |
| lib-relationship (`IRelationshipClient`) | Listing and migrating relationships during merge operations |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-character | Uses `IRelationshipTypeClient` for type validation in character relationships |
| lib-relationship | References type definitions for relationship creation/validation |

---

## State Storage

**Store**: `relationship-type` (via `StateStoreDefinitions.RelationshipType`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `type:{typeId}` | `RelationshipTypeModel` | Individual type definition |
| `code-index:{CODE}` | `string` | Code → type ID reverse lookup (uppercase) |
| `parent-index:{parentId}` | `List<string>` | Child type IDs for a parent |
| `all-types` | `List<string>` | Global index of all type IDs |

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

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<RelationshipTypeService>` | Scoped | Structured logging |
| `RelationshipTypeServiceConfiguration` | Singleton | Config properties (SeedPageSize) |
| `IStateStoreFactory` | Singleton | State store access |
| `IMessageBus` | Scoped | Event publishing and error events |
| `IEventConsumer` | Scoped | Event handler registration |
| `IRelationshipClient` | Scoped | Relationship migration during merge |

Service lifetime is **Scoped** (per-request). No background services.

---

## API Endpoints (Implementation Notes)

### Read Operations (6 endpoints)

- **GetRelationshipType** (`/relationship-type/get`): Direct lookup by type ID. Returns full definition with parent/inverse references.
- **GetRelationshipTypeByCode** (`/relationship-type/get-by-code`): Code index lookup (uppercase-normalized). Validates data consistency on index miss.
- **ListRelationshipTypes** (`/relationship-type/list`): Loads all IDs from `all-types`, bulk-loads types. Filters by `category`, `includeDeprecated`. In-memory pagination.
- **GetChildRelationshipTypes** (`/relationship-type/get-children`): Loads child IDs from parent index. Supports `recursive` flag for full subtree traversal.
- **MatchesHierarchy** (`/relationship-type/matches-hierarchy`): Checks if a type matches or descends from an ancestor type. Walks parent chain to determine match. Returns boolean + matched ancestor path.
- **GetAncestors** (`/relationship-type/get-ancestors`): Returns full ancestry chain from type up to root. Walks parent pointers iteratively.

### Write Operations (7 endpoints)

- **CreateRelationshipType** (`/relationship-type/create`): Normalizes code to uppercase. Validates parent exists (if specified). Resolves inverse type by code. Calculates depth from parent. Updates all indexes. Publishes `relationship-type.created`.
- **UpdateRelationshipType** (`/relationship-type/update`): Partial update with `changedFields` tracking. Code is immutable (uppercase, set at creation). Publishes update event only if changes detected.
- **DeleteRelationshipType** (`/relationship-type/delete`): Requires type to be deprecated. Checks no child types exist (Conflict if any). Removes from all indexes (code, parent, all-types). Publishes `relationship-type.deleted`.
- **DeprecateRelationshipType** (`/relationship-type/deprecate`): Sets `IsDeprecated=true` with timestamp and optional reason. Returns Conflict if already deprecated.
- **UndeprecateRelationshipType** (`/relationship-type/undeprecate`): Restores deprecated type. Returns Conflict if not deprecated.
- **MergeRelationshipType** (`/relationship-type/merge`): Source must be deprecated. Paginates through relationships via `IRelationshipClient.ListRelationshipsByTypeAsync`. Updates each to target type. Partial failures tracked (max 100 error details). Publishes error event if failures occur.
- **SeedRelationshipTypes** (`/relationship-type/seed`): Dependency-ordered bulk creation. Resolves parent/inverse types by code. Multi-pass algorithm with max iteration guard to handle dependency chains. Supports `updateExisting` flag. Returns created/updated/skipped/error counts.

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

  MatchesHierarchy(typeId="SON", ancestorCode="FAMILY")
       │
       ├── Walk parent chain: SON → CHILD → FAMILY
       ├── Match found at FAMILY
       └── Returns: { Matches: true, Path: ["CHILD", "FAMILY"] }

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

1. **Merge doesn't delete source**: After migrating relationships, the source type remains deprecated. Admin must explicitly call `DeleteRelationshipType` separately. The response always returns `SourceDeleted=false`.
2. **No event consumption**: Event consumer registered but no handlers implemented. Future: could listen for relationship deletion events to track type usage.

---

## Potential Extensions

1. **Circular hierarchy prevention**: Add explicit cycle detection during `SetParent` operations (currently only prevents obvious self-reference).
2. **Type constraints**: Define which entity types can participate in each relationship type (e.g., PARENT only between characters, not guilds).
3. **Relationship strength modifiers**: Associate default strength/weight values per type for relationship scoring.
4. **Category-based permissions**: Allow different roles to create relationships of different categories.

---

## Known Quirks & Caveats

### Intentional Quirks (Documented Behavior)

1. **Code immutability**: Once a relationship type is created, its code cannot be changed. The code index key is never updated or deleted on type update.

2. **IsBidirectional vs InverseType**: These are separate concepts. Bidirectional relationships (e.g., FRIEND) are symmetric - if A→B exists, B→A is implied. Inverse types (e.g., PARENT↔CHILD) are asymmetric - A is PARENT of B means B is CHILD of A. A type can have neither, either, or both properties.

3. **Depth auto-calculated**: Depth is `parent.Depth + 1` at creation time. If a parent's depth later changes (e.g., its own parent is reassigned), children are NOT automatically updated.

4. **Update only publishes when fields change**: `UpdateRelationshipTypeAsync` tracks changed fields and only publishes `relationship-type.updated` event if `changedFields.Count > 0`.

5. **Guid.Empty as null marker in events**: Event payloads use `Guid.Empty` for nullable fields like `ParentTypeId` and `InverseTypeId` when no value is set.

6. **Merge error detail limit**: Merge operations track up to 100 individual error details. Beyond that, only the count is accurate.

### Design Considerations (Requires Planning)

1. **No circular hierarchy prevention**: Creating types A.parent=B and then B.parent=A is not explicitly prevented. The depth calculation would produce incorrect values. Hierarchy traversal methods (`MatchesHierarchy`, `GetAncestors`, `GetChildTypeIdsAsync`) have a safety limit of 20 iterations to prevent infinite loops on corrupted data, but circular references are not rejected during parent assignment.

2. **Code index not cleaned on delete**: When a type is deleted, its code index entry is removed. However, if the code was reassigned between deprecation and deletion (unlikely given code immutability), the index could become stale.

3. **Partial merge failure leaves inconsistent state**: If merge fails midway, some relationships are migrated and others are not. No rollback mechanism exists. The response reports counts for manual resolution.

4. **Recursive child query unbounded**: `GetChildRelationshipTypes` with `recursive=true` traverses the full subtree. Deep hierarchies with many branches could generate many state store calls.

5. **Seed multi-pass has fixed iteration limit**: The dependency resolution algorithm uses `maxIterations = pending.Count * 2` (line 672). For a list of N types, max 2N passes. Extremely deep dependency chains could fail to resolve within the limit.

6. **Merge page fetch error stops pagination**: Like species merge, if `ListRelationshipsByTypeAsync` fails on a page, the migration loop terminates. Relationships on subsequent pages remain un-migrated.

7. **Recursive child query is sequential**: `GetChildTypeIdsAsync` with `recursive=true` traverses the subtree via recursive calls with a depth limit of 20 levels. Each level generates a state store call for the parent index, and wide hierarchies could generate many sequential calls.

8. **Depth is snapshot at creation, never updated**: A type's `Depth` field is set based on parent's depth at creation time (line 407: `depth = parent.Depth + 1`). If a parent's depth changes later, children's depths become stale. The `Depth` field is not recomputed on parent reassignment.

---

## Tenet Violations (Audit)

*Audit Date: 2026-01-24*
*Audited Files: RelationshipTypeService.cs, RelationshipTypeServicePlugin.cs, AssemblyInfo.cs*

### Category: QUALITY TENETS

1. **Logging Standards (T10)** - RelationshipTypeService.cs:58 - Operation entry logged at Information level instead of Debug
   - What's wrong: `_logger.LogInformation("Getting relationship type by ID: {TypeId}", body.RelationshipTypeId);` uses Information level for operation entry
   - Fix: Change to `_logger.LogDebug(...)` - Operation entry should be Debug level per T10 ("Operation Entry (Debug): Log input parameters")
   - Additional occurrences at lines: 87, 126, 198, 255, 329, 380, 477, 594, 648, 806, 851, 896, 1281

2. **Logging Standards (T10)** - RelationshipTypeService.cs:834, 879, 1013 - Operation success logged at Information level instead of Debug
   - What's wrong: `_logger.LogInformation("Deprecated relationship type: {TypeId}", ...)` logs routine success at Information level
   - Fix: Routine operation success should be Debug level. Only significant state changes or business decisions warrant Information level.

### Category: IMPLEMENTATION TENETS

3. **Multi-Instance Safety (T9)** - RelationshipTypeService.cs:1091-1101 - Read-modify-write without distributed lock (AddToParentIndexAsync)
   - What's wrong: `AddToParentIndexAsync` performs read-modify-write on the parent index without a distributed lock. Concurrent operations can cause lost updates.
   - Fix: Use `IDistributedLockProvider.LockAsync` to acquire a lock on the parent index key before the read-modify-write sequence, or use optimistic concurrency with ETags via `GetWithETagAsync` and `TrySaveAsync`.

4. **Multi-Instance Safety (T9)** - RelationshipTypeService.cs:1104-1113 - Read-modify-write without distributed lock (RemoveFromParentIndexAsync)
   - What's wrong: `RemoveFromParentIndexAsync` performs read-modify-write on the parent index without a distributed lock.
   - Fix: Same as above - use distributed lock or optimistic concurrency.

5. **Multi-Instance Safety (T9)** - RelationshipTypeService.cs:1116-1125 - Read-modify-write without distributed lock (AddToAllTypesListAsync)
   - What's wrong: `AddToAllTypesListAsync` performs read-modify-write on the all-types list without a distributed lock.
   - Fix: Use distributed lock or optimistic concurrency with ETags.

6. **Multi-Instance Safety (T9)** - RelationshipTypeService.cs:1128-1136 - Read-modify-write without distributed lock (RemoveFromAllTypesListAsync)
   - What's wrong: `RemoveFromAllTypesListAsync` performs read-modify-write on the all-types list without a distributed lock.
   - Fix: Use distributed lock or optimistic concurrency with ETags.

7. **Multi-Instance Safety (T9)** - RelationshipTypeService.cs:656 - Plain Dictionary used instead of ConcurrentDictionary
   - What's wrong: `var codeToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);` uses plain Dictionary
   - Fix: Change to `ConcurrentDictionary<string, string>` for thread safety, or document that this is method-local and single-threaded (seed operation is request-scoped).

8. **Internal Model Type Safety (T25)** - RelationshipTypeService.cs:1293 - String used for GUID field in internal model
   - What's wrong: `public string RelationshipTypeId { get; set; } = string.Empty;` uses string instead of Guid
   - Fix: Change to `public Guid RelationshipTypeId { get; set; }` and update all usages (Guid.Parse calls become unnecessary)

9. **Internal Model Type Safety (T25)** - RelationshipTypeService.cs:1298, 1300 - String used for GUID fields (ParentTypeId, InverseTypeId)
   - What's wrong: `public string? ParentTypeId { get; set; }` and `public string? InverseTypeId { get; set; }` use string instead of Guid?
   - Fix: Change to `public Guid? ParentTypeId { get; set; }` and `public Guid? InverseTypeId { get; set; }`

10. **Configuration-First (T21)** - RelationshipTypeService.cs:30 - Hardcoded tunable constant
    - What's wrong: `private const int MAX_HIERARCHY_DEPTH = 20;` is a hardcoded tunable value
    - Fix: Add `MaxHierarchyDepth` property to relationship-type-configuration.yaml schema with env var `RELATIONSHIP_TYPE_MAX_HIERARCHY_DEPTH` and default of 20, then use `_configuration.MaxHierarchyDepth`

11. **Configuration-First (T21)** - RelationshipTypeService.cs:930 - Hardcoded tunable constant
    - What's wrong: `const int maxErrorsToTrack = 100;` is a hardcoded tunable value for merge error tracking limit
    - Fix: Add `MaxMergeErrorsToTrack` property to configuration schema with appropriate env var and default of 100, then use `_configuration.MaxMergeErrorsToTrack`

12. **Error Handling (T7)** - RelationshipTypeService.cs:52-78 - Missing ApiException catch
    - What's wrong: The catch block only catches generic `Exception`, missing the specific `ApiException` catch pattern required by T7
    - Fix: Add `catch (ApiException ex)` block before the generic catch, log as Warning, and propagate status code. Apply to all endpoint methods.
    - Additional occurrences at: GetRelationshipTypeByCodeAsync, ListRelationshipTypesAsync, GetChildRelationshipTypesAsync, MatchesHierarchyAsync, GetAncestorsAsync, CreateRelationshipTypeAsync, UpdateRelationshipTypeAsync, DeleteRelationshipTypeAsync, SeedRelationshipTypesAsync, DeprecateRelationshipTypeAsync, UndeprecateRelationshipTypeAsync, MergeRelationshipTypeAsync

### Category: FOUNDATION TENETS

13. **Service Implementation Pattern (T6)** - RelationshipTypeService.cs:32-48 - Missing IDistributedLockProvider dependency
    - What's wrong: The service performs read-modify-write operations on indexes but does not inject `IDistributedLockProvider` for distributed locking
    - Fix: Add `IDistributedLockProvider _lockProvider` as a dependency and use it for index modification operations

14. **Event-Driven Architecture (T5)** - RelationshipTypeService.cs:1018 - DeleteAfterMerge check is based on schema field but documentation says "SourceDeleted=false always"
    - What's wrong: The Stubs section says "The response always returns `SourceDeleted=false`" but the code actually implements delete-after-merge functionality (lines 1017-1040). Documentation and code are inconsistent.
    - Fix: Update the Stubs section to reflect that delete-after-merge is implemented (not a stub). The feature works correctly when `DeleteAfterMerge=true` and no failures occurred.

### Summary

| Category | Count |
|----------|-------|
| QUALITY TENETS (T10) | 2 issues (14 log statements at wrong level) |
| IMPLEMENTATION TENETS (T7, T9, T21, T25) | 10 issues |
| FOUNDATION TENETS (T5, T6) | 2 issues |
| **Total** | **14 unique violations** |

### Priority Fixes

1. **High Priority** - T9 violations: Add distributed locking to read-modify-write index operations (4 methods)
2. **High Priority** - T7 violations: Add ApiException catch blocks to all endpoint methods (12 methods)
3. **Medium Priority** - T25 violations: Change RelationshipTypeModel to use Guid types instead of strings
4. **Medium Priority** - T21 violations: Move hardcoded tunables to configuration schema
5. **Low Priority** - T10 violations: Change operation entry logs from Information to Debug level
