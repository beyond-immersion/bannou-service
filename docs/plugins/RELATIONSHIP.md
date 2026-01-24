# Relationship Plugin Deep Dive

> **Plugin**: lib-relationship
> **Schema**: schemas/relationship-api.yaml
> **Version**: 1.0.0
> **State Store**: relationship-statestore (MySQL)

---

## Overview

A generic relationship management service for entity-to-entity relationships (character friendships, alliances, rivalries, etc.). Supports bidirectional uniqueness enforcement via composite keys, polymorphic entity types, and soft-deletion with the ability to recreate ended relationships. Used by the Character service for managing inter-character bonds.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Persistence for relationship records and indexes |
| lib-messaging (`IMessageBus`) | Publishing lifecycle events and error events |
| lib-messaging (`IEventConsumer`) | Event handler registration (no current handlers) |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-character | Calls `IRelationshipClient` to manage character relationships; deletes relationships on character delete/compress |
| lib-relationship-type | Calls `IRelationshipClient` for type merge migrations (updates relationship types during deprecation merges) |

No services subscribe to relationship events.

---

## State Storage

**Store**: `relationship-statestore` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `rel:{relationshipId}` | `RelationshipModel` | Full relationship record |
| `entity-idx:{entityType}:{entityId}` | `List<string>` | Relationship IDs involving this entity |
| `type-idx:{relationshipTypeId}` | `List<string>` | Relationship IDs of this type |
| `composite:{entity1}:{entity2}:{typeId}` | `string` | Uniqueness constraint (normalized bidirectional key → relationship ID) |

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

- **Create**: Validates entities are not the same (prevents self-relationships). Normalizes composite key bidirectionally (`A→B` and `B→A` produce the same key via string sort). Stores four keys: relationship data, two entity indexes, composite uniqueness key. Publishes `relationship.created`.

- **Get**: Simple key lookup by relationship ID.

- **ListByEntity**: Loads all relationship IDs from entity index, bulk-fetches models, filters in-memory (active/ended, type, etc.), then applies pagination. Returns full `RelationshipListResponse` with pagination metadata.

- **GetBetween**: Fetches entity1's full relationship list, filters in-memory for those involving entity2. Does not paginate (returns all matching relationships). Always reports `HasNextPage: false`.

- **ListByType**: Loads from type index, bulk-fetches, applies in-memory filtering and pagination.

- **Update**: Can modify metadata and relationship type. When type changes, updates type indexes (removes from old, adds to new). Immutable fields: entity1, entity2 (cannot change participants). Publishes `relationship.updated` with `changedFields`.

- **End**: Soft-deletes by setting `EndedAt` timestamp. Deletes the composite uniqueness key (allowing the same relationship to be recreated later). Does NOT remove from entity or type indexes (keeping history queryable). Publishes `relationship.deleted`.

---

## Visual Aid

```
Composite Key Normalization (Bidirectional Uniqueness)
=======================================================

Create: Entity A (character) → Entity B (character), Type: FRIEND

  Step 1: Build composite components
    key1 = "character:A"
    key2 = "character:B"

  Step 2: Normalize via string sort
    "character:A" < "character:B" → no swap needed
    composite = "composite:character:A:character:B:FRIEND"

  Step 3: Check uniqueness
    IF composite key exists → 409 Conflict
    ELSE → create relationship

Create: Entity B (character) → Entity A (character), Type: FRIEND
  → Same composite key (sorted) → 409 Conflict (already exists)

End relationship:
  → Composite key DELETED
  → Relationship can be recreated
```

---

## Stubs & Unimplemented Features

1. **`all-relationships` master list key**: The constant is defined and maintained during create but never queried in any endpoint. Likely vestigial from early development.

---

## Potential Extensions

1. **Relationship strength/weight**: Numeric field for weighted relationship graphs (e.g., closeness scores).
2. **Bidirectional asymmetric metadata**: Allow entity1 and entity2 to have independent metadata perspectives on the same relationship.
3. **Cascade cleanup on entity deletion**: Automatically end all relationships when an entity is permanently deleted.
4. **Pagination for GetBetween**: Currently returns all relationships between two entities without pagination support.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

None identified.

### Intentional Quirks (Documented Behavior)

1. **Bidirectional uniqueness via string sort**: Composite keys are normalized so `A→B` and `B→A` are the same relationship. This prevents duplicate relationships regardless of creation order. Self-relationships (same entity and type) are explicitly rejected.

2. **Self-relationship with different types allowed**: The self-relationship check compares both ID and type. Entity A (type: character) → Entity A (type: npc) is allowed. This supports entities that span multiple type classifications.

3. **Ended relationships remain in indexes**: When a relationship ends, entity and type indexes retain the relationship ID. This preserves queryability of historical relationships but requires filtering in read paths.

4. **Composite key deletion on end**: Ending a relationship removes the uniqueness constraint, explicitly allowing the same pair of entities to form a new relationship of the same type. This models "breaking up and getting back together" scenarios.

5. **Metadata coalesced to empty dict in events**: Internal model allows `null` metadata, but published events always include `new Dictionary<string, object>()` instead of null, ensuring JSON serialization consistency.

6. **Entity types parsed on every response**: `EntityType` enum stored as string in state store, parsed via `Enum.Parse<EntityType>()` in `MapToResponse()` on every read. Adds minor overhead but avoids enum serialization issues.

### Design Considerations (Requires Planning)

1. **In-memory filtering before pagination**: All list operations load the full index, bulk-fetch all relationship models, filter in memory, then paginate. For entities with thousands of relationships, this loads everything into memory before applying page limits.

2. **No index cleanup**: Entity and type indexes accumulate relationship IDs indefinitely (both active and ended). Over time, indexes grow large with ended relationships that must be filtered on every query.

3. **GetBetween only checks entity1's index**: Looks up entity1's relationships and filters for entity2. If entity1's index is corrupted but entity2's is correct, the relationship won't be found. No cross-validation against entity2's index.

4. **No optimistic concurrency**: Updates overwrite without version checking. Two concurrent updates to metadata will result in last-writer-wins with no conflict detection.

5. **Type migration during merge**: The update endpoint allows changing `RelationshipTypeId`, which is used by the RelationshipType service during type merges. This modifies type indexes atomically but without distributed transaction guarantees — a crash between removing from old index and adding to new could leave the relationship in neither index.

---

## Tenet Violations (Audit)

*Audit performed: 2026-01-24*

### Category: IMPLEMENTATION TENETS

1. **Multi-Instance Safety (T9)** - RelationshipService.cs:649-661, 667-679, 685-697, 702-711 - Read-modify-write sequences without distributed locks
   - What's wrong: The `AddToEntityIndexAsync`, `AddToTypeIndexAsync`, `RemoveFromTypeIndexAsync`, and `AddToAllRelationshipsListAsync` methods perform read-modify-write operations on list indexes without distributed locking. Two concurrent requests could both read the same list, add their item, and save - causing one addition to be lost.
   - Fix: Wrap each read-modify-write sequence with `IDistributedLockProvider.LockAsync()` using the index key as the resource ID, or use atomic state store operations if available.

2. **Multi-Instance Safety (T9)** - RelationshipService.cs:393-405 - Composite key uniqueness check without distributed lock
   - What's wrong: The create operation checks for existing composite key and then creates - a classic TOCTOU (time-of-check-time-of-use) race condition. Two concurrent creates for the same entity pair could both pass the uniqueness check and create duplicate relationships.
   - Fix: Acquire a distributed lock on the composite key before the existence check and release after save.

3. **Internal Model Type Safety (T25)** - RelationshipService.cs:868-924 - RelationshipModel uses string for GUIDs and Enums
   - What's wrong: The internal `RelationshipModel` uses `string` for fields that should be typed:
     - `RelationshipId`, `Entity1Id`, `Entity2Id`, `RelationshipTypeId` should be `Guid`
     - `Entity1Type`, `Entity2Type` should be `EntityType` enum
   - Fix: Change the internal POCO to use proper types (`Guid` and `EntityType`). This eliminates the need for `Guid.Parse()` and `Enum.Parse()` in `MapToResponse()` and business logic, and provides compile-time type safety.

4. **Internal Model Type Safety (T25)** - RelationshipService.cs:737-742 - Enum.Parse in business logic (MapToResponse)
   - What's wrong: `MapToResponse()` calls `Enum.Parse<EntityType>()` on every read operation. Per IMPLEMENTATION TENETS, enum parsing belongs only at system boundaries (deserialization), not in business logic.
   - Fix: Change `RelationshipModel` to use `EntityType` enum directly. BannouJson handles enum serialization automatically in state stores.

5. **Internal Model Type Safety (T25)** - RelationshipService.cs:107, 144, 151-155, 204, 249, 325, 326 - String comparisons for enum values
   - What's wrong: Multiple lines compare enum values as strings (e.g., `r.Entity1Type == entity1Type` where both are strings converted via `.ToString()`). This is fragile and bypasses compile-time type safety.
   - Fix: Use typed enum comparisons after fixing `RelationshipModel` to use proper types.

6. **Internal Model Type Safety (T25)** - RelationshipService.cs:411-423, 501 - ToString() when populating internal model
   - What's wrong: The create operation converts request enums and GUIDs to strings via `.ToString()` when populating `RelationshipModel`. Per IMPLEMENTATION TENETS, internal models should store proper types.
   - Fix: Assign enums and GUIDs directly to the model after fixing `RelationshipModel` to use proper types.

7. **Unused CancellationToken Parameter** - RelationshipService.cs:754, 778, 808 - CancellationToken parameters not used
   - What's wrong: The `PublishRelationshipCreatedEventAsync`, `PublishRelationshipUpdatedEventAsync`, and `PublishRelationshipDeletedEventAsync` methods accept a `cancellationToken` parameter but never pass it to `TryPublishAsync`.
   - Fix: Either pass the cancellation token to `_messageBus.TryPublishAsync()` if the overload supports it, or remove the parameter if it's not needed.

### Category: QUALITY TENETS

8. **Logging Standards (T10)** - RelationshipService.cs:68, 103, 200, 285, 383, 447, 470, 522, 554, 591, 858 - Operation entry logged at Information level instead of Debug
   - What's wrong: Multiple operation entry points use `LogInformation` for routine operation starts. Per QUALITY TENETS, operation entry should be logged at Debug level, with Information reserved for significant business decisions/state changes.
   - Fix: Change operation entry logs to `LogDebug`. Keep success/completion logs at Information level where they represent meaningful state changes (e.g., "Created relationship", "Ended relationship").

9. **Logging Standards (T10)** - RelationshipService.cs:126, 225, 307 - Data inconsistency logged as Error without error event
   - What's wrong: When a relationship ID is in an index but the model fails to load, the code logs an error but does not emit an error event via `TryPublishErrorAsync`. Per IMPLEMENTATION TENETS (T7), unexpected internal failures that warrant Error logging should also emit error events for monitoring.
   - Fix: Call `await _messageBus.TryPublishErrorAsync(...)` when data inconsistency is detected, in addition to the error log.

### Category: FOUNDATION TENETS

10. **Service Implementation Pattern (T6)** - RelationshipService.cs:38-52 - Constructor param XML doc mismatch
    - What's wrong: The constructor XML documentation lists `errorEventEmitter` as a parameter but the actual parameter is `eventConsumer`. This is a documentation error.
    - Fix: Update the XML `<param name="errorEventEmitter">` to `<param name="eventConsumer">` with appropriate description.

### Summary

| Category | Count |
|----------|-------|
| IMPLEMENTATION TENETS | 7 |
| QUALITY TENETS | 2 |
| FOUNDATION TENETS | 1 |
| **Total** | **10** |

**Most Critical Issues**:
1. **Multi-instance safety violations** (T9) - Race conditions in index updates and composite key checks could cause data loss or duplicate relationships in multi-instance deployments.
2. **Internal model type safety** (T25) - Using strings for GUIDs and enums throughout the internal model creates fragile code and runtime parsing overhead.
