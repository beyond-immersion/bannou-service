# Relationship Plugin Deep Dive

> **Plugin**: lib-relationship
> **Schema**: schemas/relationship-api.yaml
> **Version**: 2.0.0
> **Layer**: GameFoundation
> **State Store**: relationship-statestore (MySQL), relationship-type-statestore (MySQL), relationship-lock (Redis)
> **Implementation Map**: [docs/maps/RELATIONSHIP.md](../maps/RELATIONSHIP.md)
> **Short**: Entity-to-entity relationships with type taxonomy, bidirectional uniqueness, and ABML variable provider

---

## Overview

A unified relationship management service (L2 GameFoundation) combining entity-to-entity relationships (character friendships, alliances, rivalries) with hierarchical relationship type taxonomy definitions. Supports bidirectional uniqueness enforcement, polymorphic entity types, soft-deletion with recreate capability, type deprecation with merge, and bulk seeding. Used by the Character service for inter-character bonds and family tree categorization, and by the Storyline service for narrative generation. Consolidated from the former separate relationship and relationship-type plugins.

### System Realm & Cross-Cutting Use Cases

Relationship's polymorphic entity support makes it a key primitive for system realm entities and cross-cutting game mechanics. Planned relationship type codes and their consumers:

| Use Case | Type Code(s) | Entities | Consumer |
|----------|-------------|----------|----------|
| Family tree | `PARENT`, `CHILD`, `SIBLING`, etc. | Character ↔ Character | lib-character (implemented) |
| NPC social bonds | `FRIEND`, `RIVAL`, `MENTOR`, etc. | Character ↔ Character | lib-storyline (implemented) |
| Divine followers | Follower/devotee types | Character ↔ Deity (PANTHEON) | lib-divine (planned) |
| Marriage bonds | `SPOUSE` | Character ↔ Character | lib-character-lifecycle (planned) |
| Living weapon wielder | `WEAPON_WIELDER` | Character ↔ Weapon (SENTIENT_ARMS) | Zero-plugin pattern (planned) |

The `${relationship.*}` ABML variable namespace is implemented via `RelationshipProviderFactory` (registered as `IVariableProviderFactory`), exposing relationship data to the Actor behavior system. NPCs make social decisions based on relationship type, existence, and hierarchy through variables like `${relationship.has.*}`, `${relationship.count.*}`, and `${relationship.total}`.

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-character | Calls `IRelationshipClient` for family tree building (entity listing + type code lookup) and reference counting during compression eligibility checks |
| lib-storyline | Injects `IRelationshipClient` for relationship data and type lookups during narrative generation |

**Planned dependents** (not yet implemented):

| Dependent | Planned Usage |
|-----------|---------------|
| lib-divine | Follower bonds between deities and characters, deity-to-deity rivalries |
| lib-character-lifecycle | Marriage/spouse bonds, parent-child bonds during procreation |

No external services subscribe to relationship events. The service self-subscribes to its own lifecycle events for cache invalidation (see Quirk #6).

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `MaxHierarchyDepth` | `RELATIONSHIP_TYPE_MAX_HIERARCHY_DEPTH` | `20` | Maximum depth for hierarchy traversal to prevent infinite loops |
| `MaxMigrationErrorsToTrack` | `RELATIONSHIP_TYPE_MAX_MIGRATION_ERRORS_TO_TRACK` | `100` | Maximum number of individual migration error details to track |
| `LockTimeoutSeconds` | `RELATIONSHIP_LOCK_TIMEOUT_SECONDS` | `30` | Timeout in seconds for distributed lock acquisition on index and uniqueness operations |
| `ProviderQueryPageSize` | `RELATIONSHIP_PROVIDER_QUERY_PAGE_SIZE` | `100` | Number of relationships to fetch per page when loading provider cache data (range: 10-500) |
| `ProviderCacheTtlSeconds` | `RELATIONSHIP_PROVIDER_CACHE_TTL_SECONDS` | `300` | TTL in seconds for relationship variable provider in-memory cache entries (range: 5-600) |

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
<!-- AUDIT:NEEDS_DESIGN:2026-02-28:https://github.com/beyond-immersion/bannou-service/issues/504 -->
2. **Bidirectional asymmetric metadata**: Allow entity1 and entity2 to have independent metadata perspectives on the same relationship.
<!-- AUDIT:NEEDS_DESIGN:2026-02-28:https://github.com/beyond-immersion/bannou-service/issues/505 -->
3. **Type constraints**: Define which entity types can participate in each relationship type (e.g., PARENT only between characters, not guilds).
<!-- AUDIT:NEEDS_DESIGN:2026-02-08:https://github.com/beyond-immersion/bannou-service/issues/338 -->
4. **Relationship strength modifiers**: Associate default strength/weight values per type for relationship scoring.
<!-- AUDIT:NEEDS_DESIGN:2026-02-28:https://github.com/beyond-immersion/bannou-service/issues/504 -->
5. **Category-based permissions**: Allow different roles to create relationships of different categories.
<!-- AUDIT:NEEDS_DESIGN:2026-02-28:https://github.com/beyond-immersion/bannou-service/issues/507 -->

---

## Type Field Classification

| Field | Category | Type | Rationale |
|-------|----------|------|-----------|
| `entity1Type` / `entity2Type` | A (Entity Reference) | `EntityType` enum (via `$ref` to `common-api.yaml`) | Identifies which first-class Bannou entity participates in the relationship. All valid values are Bannou entities (character, account, realm, etc.) |
| `sourceEntityType` / `targetEntityType` | A (Entity Reference) | `EntityType` enum (via `$ref` to `common-api.yaml`) | Same as above, used in query/filter contexts to find relationships involving a specific entity type |
| `relationshipTypeCode` | B (Content Code) | Opaque string | Game-configurable taxonomy codes (PARENT, FRIEND, RIVAL, WEAPON_WIELDER, etc.). Registered via API, not hardcoded. Uppercase-normalized. New codes added without schema changes |
| `category` | B (Content Code) | Opaque string | Grouping label for relationship types (e.g., "FAMILY", "SOCIAL", "POLITICAL"). Game-configurable, used for filtering |

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

1. **Soft-delete pattern for ended relationships violates Foundation Tenets (Deletion Finality)**: The overview describes "soft-deletion with recreate capability" — `EndRelationship` sets `EndedAt` while retaining the record indefinitely, and publishes `relationship.deleted`. This is the exact soft-delete anti-pattern prohibited by Foundation Tenets: "Soft-delete patterns (setting a `DeletedAt`/`IsDeleted` flag while retaining the record indefinitely) are forbidden." The only exception is Account with a time-limited retention worker; no such worker exists for relationships. Instance data (which explicitly includes "relationships" per Implementation Tenets § Deprecation Lifecycle) requires immediate hard delete. The cascade cleanup endpoint (`CleanupByEntity`) also "ends" rather than hard-deletes, compounding the issue. Fix: hard-delete relationship records when ended, and preserve historical relationship data through an appropriate mechanism (e.g., character-history backstory entries, resource compression callbacks) rather than retaining soft-deleted records in the primary store.

### Intentional Quirks (Documented Behavior)

1. **Self-relationship with different types allowed**: The self-relationship check compares both ID and type. Entity A (type: character) -> Entity A (type: npc) is allowed. This supports entities that span multiple type classifications.

2. **Ended relationships remain in indexes**: When a relationship ends, entity and type indexes retain the relationship ID. This preserves queryability of historical relationships but requires filtering in read paths.

3. **Composite key deletion on end**: Ending a relationship removes the uniqueness constraint, explicitly allowing the same pair of entities to form a new relationship of the same type. This models "breaking up and getting back together" scenarios.

4. **GetBetween only checks entity1's index**: Looks up entity1's relationships and filters for entity2. If entity1's index is corrupted but entity2's is correct, the relationship won't be found. No cross-validation against entity2's index.

5. **Update rejects ended relationships**: Attempting to update a relationship that has `EndedAt` set returns `StatusCodes.Conflict`. This prevents modifications to historical records.

6. **Self-subscription for cache invalidation only**: The service subscribes to its own three lifecycle events (`relationship.created`, `relationship.updated`, `relationship.deleted`) solely to invalidate the `IRelationshipDataCache` used by the ABML variable provider. Only Character-type entity caches are invalidated. Type usage checks (e.g., during delete) are still performed on-demand via internal state store lookups rather than maintaining cached counts.

7. **Type depth auto-calculated but not updated**: Depth is `parent.Depth + 1` at creation time. If a parent's depth later changes (e.g., its own parent is reassigned), children are NOT automatically updated. Depth recalculation would require traversing all descendants.

8. **Partial merge failure leaves inconsistent state**: If a per-relationship error occurs during merge, that relationship remains in its original state (type indexes and composite keys are updated atomically per-relationship under lock). The response reports counts (`RelationshipsMigrated`, `RelationshipsFailed`, `MigrationErrors`) for manual resolution. Both type index locks are held throughout the entire merge operation.

9. **Merge composite key collision ends duplicate**: When migrating an active relationship and the target type already has an active relationship for the same entity pair, the source relationship is ended as a duplicate (soft-deleted) rather than silently skipped. This preserves data integrity by surfacing genuine conflicts.

10. **Code normalization is case-insensitive**: All type codes are converted to uppercase on creation (`body.Code.ToUpperInvariant()`) and lookup. "friend" and "FRIEND" resolve to the same type.

11. **Inverse type resolved by code, not ID**: When creating/updating a type with `InverseTypeCode`, the ID is resolved via index lookup at that moment. If the inverse type is later deleted, `InverseTypeId` becomes stale (points to non-existent type).

12. **Merge holds type index locks for full duration**: The internalized merge acquires distributed locks on both source and target type indexes at the start and holds them throughout the entire bulk migration. This prevents concurrent modifications but means long-running merges (thousands of relationships) block other operations on those type indexes. The per-relationship lock is acquired and released individually within the loop.

13. **Recursive child query breadth-unbounded**: `GetChildRelationshipTypesAsync` with `recursive=true` traverses the full subtree. `MaxHierarchyDepth` (default 20) bounds depth but not breadth. This is acceptable because relationship types are admin-curated taxonomies with realistic totals of ~50-100 types, making breadth explosion a theoretical rather than practical concern.

14. **Seed multi-pass iteration limit is `2*N`**: The dependency resolution algorithm uses `maxIterations = pending.Count * 2`. This limit is provably unreachable: each iteration processes at least one type (or the loop breaks early with unresolvable parent errors), so N types require at most N iterations. The `2*N` limit exists as an infinite loop guard but cannot be hit in practice.

15. **Delete-after-merge skipped on partial failure**: When `deleteAfterMerge=true` but some relationships failed to migrate, the source type is NOT deleted. This is doubly-safe: the merge itself skips deletion when `failedCount > 0`, AND `DeleteRelationshipTypeAsync` independently rejects deletion when any relationships still reference the type. The response includes `SourceDeleted = false`, exact failure counts, and per-relationship error details. Recovery path: retry the merge (idempotent for already-migrated relationships), manually end problematic relationships via `/relationship/end`, then delete the source type.

### Design Considerations (Requires Planning)

1. **In-memory filtering before pagination**: All list operations load the full index, bulk-fetch all relationship models, filter in memory, then paginate. For entities with thousands of relationships, this loads everything into memory before applying page limits.
<!-- AUDIT:NEEDS_DESIGN:2026-02-28:https://github.com/beyond-immersion/bannou-service/issues/509 -->

2. **No index cleanup**: Entity and type indexes accumulate relationship IDs indefinitely (both active and ended). Over time, indexes grow large with ended relationships that must be filtered on every query.
<!-- AUDIT:NEEDS_DESIGN:2026-02-28:https://github.com/beyond-immersion/bannou-service/issues/510 -->

---

## Work Tracking

*This section tracks active development work. Markers are managed by `/audit-plugin`.*

### Pending Issues

- [#147](https://github.com/beyond-immersion/bannou-service/issues/147): RelationshipProviderFactory (`IVariableProviderFactory`) — core provider implemented (`RelationshipProviderFactory`, `RelationshipProvider`, `RelationshipDataCache`). Issue remains open for any remaining integration or variable expansion work
- [#338](https://github.com/beyond-immersion/bannou-service/issues/338): Type constraints — define which entity types can participate in each relationship type (Potential Extension #3)
- [#504](https://github.com/beyond-immersion/bannou-service/issues/504): Relationship strength/weight field design — field naming, data type/range, interaction with extensions #2 and #4 (Potential Extension #1)
- [#505](https://github.com/beyond-immersion/bannou-service/issues/505): Bidirectional asymmetric metadata design — per-entity metadata perspectives, replace vs augment unified field, migration (Potential Extension #2)
- [#507](https://github.com/beyond-immersion/bannou-service/issues/507): Category-based permissions design — data-conditional permission enforcement approach, category→role mapping, manifest implications (Potential Extension #5)
- [#509](https://github.com/beyond-immersion/bannou-service/issues/509): In-memory filtering before pagination — list operations load full indexes into memory before paginating, need to evaluate IQueryableStateStore migration (Design Consideration #1). Consider bundling with #510 (same root cause; IQueryableStateStore migration solves both)
- [#510](https://github.com/beyond-immersion/bannou-service/issues/510): Unbounded index growth from ended relationships — entity and type indexes accumulate IDs indefinitely, includes orphaned entity-idx after cascade deletion (Design Consideration #2). Orphaned entity-idx cleanup in `CleanupByEntityAsync` is trivially fixable independent of the broader design
- [#544](https://github.com/beyond-immersion/bannou-service/issues/544): Game-time auto-population for `startedAt`/`endedAt` fields — cross-cutting with Character Encounter. Key open question: relationships are cross-realm (no single clock applies), so game-time may not be applicable here
- [#564](https://github.com/beyond-immersion/bannou-service/issues/564): Expand `x-references` cleanup to cover Organization and Faction entity types (when those plugins are implemented)

### Active

- **Batch lifecycle events** (2026-03-15): Switch to batch: true for high-frequency instance lifecycle events. Tracked via [#656](https://github.com/beyond-immersion/bannou-service/issues/656).

### Completed

*No completed items.*
