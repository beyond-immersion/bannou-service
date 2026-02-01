# Relationship Plugin Deep Dive

> **Plugin**: lib-relationship
> **Schema**: schemas/relationship-api.yaml
> **Version**: 1.0.0
> **State Store**: relationship-statestore (MySQL)

---

## Overview

A generic relationship management service for entity-to-entity relationships (character friendships, alliances, rivalries, etc.). Supports bidirectional uniqueness enforcement via composite keys, polymorphic entity types, and soft-deletion with the ability to recreate ended relationships. Used by the Character service for managing inter-character bonds and by the RelationshipType service for type merge migrations.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Persistence for relationship records and indexes |
| lib-messaging (`IMessageBus`) | Publishing lifecycle events and error events |
| lib-messaging (`IEventConsumer`) | Event registration infrastructure (no current handlers) |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-character | Calls `IRelationshipClient.ListRelationshipsByEntityAsync` for family tree building and reference counting during compression eligibility checks |
| lib-relationship-type | Calls `IRelationshipClient.ListRelationshipsByTypeAsync` and `UpdateRelationshipAsync` for type merge migrations (updates relationship types during deprecation merges) |

No services subscribe to relationship events.

---

## State Storage

**Store**: `relationship-statestore` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `rel:{relationshipId}` | `RelationshipModel` | Full relationship record |
| `entity-idx:{entityType}:{entityId}` | `List<Guid>` | Relationship IDs involving this entity |
| `type-idx:{relationshipTypeId}` | `List<Guid>` | Relationship IDs of this type |
| `composite:{entity1}:{entity2}:{typeId}` | `string` | Uniqueness constraint (normalized bidirectional key -> relationship ID) |

---

## Events

### Published Events

| Topic | Trigger |
|-------|---------|
| `relationship.created` | New relationship established |
| `relationship.updated` | Metadata or relationship type changed (includes `changedFields`) |
| `relationship.deleted` | Relationship ended (soft-delete) |

Events are auto-generated from `x-lifecycle` in `relationship-events.yaml`.

### Consumed Events

This plugin does not consume external events.

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| — | — | — | No service-specific configuration properties |

The generated `RelationshipServiceConfiguration` contains only the framework-level `ForceServiceId` property.

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<RelationshipService>` | Scoped | Structured logging |
| `RelationshipServiceConfiguration` | Singleton | Framework config (empty) |
| `IStateStoreFactory` | Singleton | State store access |
| `IMessageBus` | Scoped | Event publishing |
| `IEventConsumer` | Scoped | Event registration (no handlers) |

Service lifetime is **Scoped** (per-request).

---

## API Endpoints (Implementation Notes)

- **Create**: Validates entities are not the same (prevents self-relationships). Normalizes composite key bidirectionally (`A->B` and `B->A` produce the same key via string sort). Stores four keys: relationship data, two entity indexes, type index, and composite uniqueness key. Publishes `relationship.created`.

- **Get**: Simple key lookup by relationship ID.

- **ListByEntity**: Loads all relationship IDs from entity index, bulk-fetches models, filters in-memory (active/ended, type, etc.), then applies pagination. Returns full `RelationshipListResponse` with pagination metadata.

- **GetBetween**: Fetches entity1's full relationship list, filters in-memory for those involving entity2. Does not paginate (returns all matching relationships). Always reports `HasNextPage: false`.

- **ListByType**: Loads from type index, bulk-fetches, applies in-memory filtering and pagination.

- **Update**: Can modify metadata and relationship type. When type changes, updates type indexes (removes from old, adds to new). Immutable fields: entity1, entity2 (cannot change participants). Ended relationships cannot be updated (returns Conflict). Publishes `relationship.updated` with `changedFields`.

- **End**: Soft-deletes by setting `EndedAt` timestamp. Deletes the composite uniqueness key (allowing the same relationship to be recreated later). Does NOT remove from entity or type indexes (keeping history queryable). Publishes `relationship.deleted`.

---

## Visual Aid

```
Composite Key Normalization (Bidirectional Uniqueness)
=======================================================

Create: Entity A (character) -> Entity B (character), Type: FRIEND

  Step 1: Build composite components
    key1 = "Character:A"
    key2 = "Character:B"

  Step 2: Normalize via string sort (Ordinal comparison)
    "Character:A" < "Character:B" -> no swap needed
    composite = "composite:Character:A:Character:B:FRIEND"

  Step 3: Check uniqueness
    IF composite key exists -> 409 Conflict
    ELSE -> create relationship

Create: Entity B (character) -> Entity A (character), Type: FRIEND
  -> Same composite key (sorted) -> 409 Conflict (already exists)

End relationship:
  -> Composite key DELETED
  -> Relationship can be recreated
```

---

## Stubs & Unimplemented Features

1. ~~**`all-relationships` master list key**~~: **FIXED** (2026-01-31) - Removed dead code: the constant, helper method, and create-time call were all removed. The list was never queried by any endpoint.

---

## Potential Extensions

1. **Relationship strength/weight**: Numeric field for weighted relationship graphs (e.g., closeness scores).
2. **Bidirectional asymmetric metadata**: Allow entity1 and entity2 to have independent metadata perspectives on the same relationship.
3. **Cascade cleanup on entity deletion**: Automatically end all relationships when an entity is permanently deleted.
4. **Pagination for GetBetween**: Currently returns all relationships between two entities without pagination support.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

No bugs identified.

### Intentional Quirks (Documented Behavior)

1. **Self-relationship with different types allowed**: The self-relationship check compares both ID and type. Entity A (type: character) -> Entity A (type: npc) is allowed. This supports entities that span multiple type classifications.

2. **Ended relationships remain in indexes**: When a relationship ends, entity and type indexes retain the relationship ID. This preserves queryability of historical relationships but requires filtering in read paths.

3. **Composite key deletion on end**: Ending a relationship removes the uniqueness constraint, explicitly allowing the same pair of entities to form a new relationship of the same type. This models "breaking up and getting back together" scenarios.

4. **GetBetween only checks entity1's index**: Looks up entity1's relationships and filters for entity2. If entity1's index is corrupted but entity2's is correct, the relationship won't be found. No cross-validation against entity2's index.

5. **Update rejects ended relationships**: Attempting to update a relationship that has `EndedAt` set returns `StatusCodes.Conflict`. This prevents modifications to historical records.

### Design Considerations (Requires Planning)

1. **In-memory filtering before pagination**: All list operations load the full index, bulk-fetch all relationship models, filter in memory, then paginate. For entities with thousands of relationships, this loads everything into memory before applying page limits.

2. **No index cleanup**: Entity and type indexes accumulate relationship IDs indefinitely (both active and ended). Over time, indexes grow large with ended relationships that must be filtered on every query.

3. **No optimistic concurrency**: Updates overwrite without version checking. Two concurrent updates to metadata will result in last-writer-wins with no conflict detection.

4. **Type migration during merge**: The update endpoint allows changing `RelationshipTypeId`, which is used by the RelationshipType service during type merges. This modifies type indexes atomically but without distributed transaction guarantees — a crash between removing from old index and adding to new could leave the relationship in neither index.

5. **Read-modify-write without distributed locks**: Index updates (add/remove from list) and composite key checks have no concurrency protection. Two concurrent creates with the same composite key could both pass the uniqueness check if timed precisely. Requires IDistributedLockProvider integration.
<!-- AUDIT:NEEDS_DESIGN:2026-02-01:https://github.com/beyond-immersion/bannou-service/issues/223 -->

6. ~~**Data inconsistency logging without error events**~~: **FIXED** (2026-02-01) - Added `EmitDataInconsistencyErrorAsync` helper methods that call `TryPublishErrorAsync` with error type `data_inconsistency` at all three bulk-fetch locations (ListRelationshipsByEntity, GetRelationshipsBetween, ListRelationshipsByType). Error events include the orphaned key, index source (entity or type), and relevant context for monitoring and alerting.

---

## Work Tracking

*This section tracks active development work. Markers are managed by `/audit-plugin`.*

### Completed

- **2026-02-01**: Added `TryPublishErrorAsync` calls for data inconsistency detection in bulk-fetch operations (ListRelationshipsByEntity, GetRelationshipsBetween, ListRelationshipsByType).
- **2026-01-31**: Removed dead `all-relationships` master list key (constant, helper method, and create-time call).
