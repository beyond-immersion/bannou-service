# Realm History Plugin Deep Dive

> **Plugin**: lib-realm-history
> **Schema**: schemas/realm-history-api.yaml
> **Version**: 1.0.0
> **State Store**: realm-history-statestore (MySQL)

---

## Overview

Historical event participation and lore management for realms. Tracks when realms participate in world events (wars, treaties, cataclysms) with role and impact tracking, and maintains machine-readable lore elements (origin myths, cultural practices, political systems) for behavior system consumption. Provides text summarization for realm archival. Uses a dual-index pattern for efficient queries in both directions (realm's events and event's realms).

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | MySQL persistence for participation records, indexes, and lore |
| lib-messaging (`IMessageBus`) | Publishing participation and lore lifecycle events |
| lib-messaging (`IEventConsumer`) | Event handler registration (no current handlers) |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| (none identified) | No services currently call `IRealmHistoryClient` in production code |

---

## State Storage

**Store**: `realm-history-statestore` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `realm-participation-{participationId}` | `RealmParticipationData` | Individual participation record |
| `realm-participation-index-{realmId}` | `RealmParticipationIndexData` | List of participation IDs for a realm |
| `realm-participation-event-{eventId}` | `RealmParticipationIndexData` | List of participation IDs for an event |
| `realm-lore-{realmId}` | `RealmLoreData` | All lore elements for a realm (single document) |

---

## Events

### Published Events

| Topic | Trigger |
|-------|---------|
| `realm-history.participation.recorded` | New participation recorded |
| `realm-history.participation.deleted` | Participation removed |
| `realm-history.lore.created` | First lore created for a realm |
| `realm-history.lore.updated` | Existing lore modified |
| `realm-history.lore.deleted` | All lore deleted for a realm |
| `realm-history.deleted` | All history (participation + lore) deleted |

### Consumed Events

This plugin does not consume external events.

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| --- | --- | --- | No service-specific configuration properties |

The generated `RealmHistoryServiceConfiguration` contains only the framework-level `ForceServiceId` property.

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<RealmHistoryService>` | Scoped | Structured logging |
| `RealmHistoryServiceConfiguration` | Singleton | Framework config (empty) |
| `IStateStoreFactory` | Singleton | State store access |
| `IMessageBus` | Scoped | Event publishing |
| `IEventConsumer` | Scoped | Event registration (no handlers) |

Service lifetime is **Scoped** (per-request).

---

## API Endpoints (Implementation Notes)

### Participation Operations (4 endpoints)

- **Record** (`/realm-history/record-participation`): Creates unique participation ID. Stores record and updates both realm and event indexes. Publishes recorded event.
- **GetParticipation** (`/realm-history/get-participation`): Fetches all records for a realm, filters by event category and minimum impact, sorts by event date descending, paginates (default page size 20).
- **GetEventParticipants** (`/realm-history/get-event-participants`): Inverse query using secondary index. Filters by role, sorts by impact descending.
- **DeleteParticipation** (`/realm-history/delete-participation`): Removes record and updates both indexes atomically. Publishes deletion event.

### Lore Operations (4 endpoints)

- **GetLore** (`/realm-history/get-lore`): Returns OK with empty list if no lore exists (never 404). Filters by element type and strength threshold. Converts timestamps from Unix.
- **SetLore** (`/realm-history/set-lore`): Merge-or-replace semantics controlled by `replaceExisting` flag. Merge updates existing elements by type+key pair and adds new ones. Publishes created or updated event.
- **AddLoreElement** (`/realm-history/add-lore-element`): Adds single element. Updates if type+key match exists. Creates lore document if none exists.
- **DeleteLore** (`/realm-history/delete-lore`): Removes entire lore document. Returns NotFound if no lore exists.

### Management Operations (2 endpoints)

- **DeleteAll** (`/realm-history/delete-all`): Comprehensive cleanup. Iterates all realm participations, removes from event indexes, deletes records. Also deletes lore. Returns counts of deleted items.
- **Summarize** (`/realm-history/summarize`): Generates human-readable text summaries from machine-readable data. Selects top N elements by strength for lore summaries. Selects top N events by impact for participation summaries. Configurable limits (maxLorePoints: 1-20, default 5; maxHistoricalEvents: 1-20, default 10).

---

## Visual Aid

```
Dual-Index Storage Pattern
============================

  RecordParticipation(realmId=R1, eventId=E1)
       │
       ├──► realm-participation-{id}     → ParticipationData
       │
       ├──► realm-participation-index-R1 → [..., id]  (append)
       │
       └──► realm-participation-event-E1 → [..., id]  (append)


  GetParticipation(realmId=R1)
       │
       └──► realm-participation-index-R1 → [id1, id2, id3]
                │
                ▼
            Fetch each: realm-participation-{id1}, {id2}, {id3}
                │
                ▼
            Filter (category, minImpact) → Sort (date desc) → Paginate


  GetEventParticipants(eventId=E1)
       │
       └──► realm-participation-event-E1 → [id1, id4, id5]
                │
                ▼
            Fetch each: realm-participation-{id1}, {id4}, {id5}
                │
                ▼
            Filter (role) → Sort (impact desc) → Paginate
```

---

## Stubs & Unimplemented Features

None. The service is feature-complete for its scope.

---

## Potential Extensions

1. **Event-level aggregation**: Compute aggregate impact scores per event across all participating realms.
2. **Lore inheritance**: Child realms inheriting lore elements from parent realms (if realm hierarchy is added).
3. **AI-powered summarization**: Replace template-based summaries with LLM-generated narrative text.
4. **Realm timeline visualization**: Chronological event data suitable for timeline UI rendering.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

None identified.

### Intentional Quirks (Documented Behavior)

1. **GetLore returns OK with empty list**: Unlike DeleteLore (which returns NotFound for missing lore), GetLore always returns 200 OK with an empty elements list. Read operations are lenient; delete operations are strict.

2. **Lore merge by type+key pair**: When `replaceExisting=false`, SetLore matches existing elements by combining `ElementType` and `Key`. If a match exists, the value and strength are updated. New type+key combinations are appended.

3. **Enum fallback pattern**: `MapToRealmHistoricalParticipation` uses `Enum.TryParse` with fallback to `RealmEventCategory.FOUNDING` if parsing fails. Handles schema evolution gracefully (new categories in data, old code reads it).

4. **Summarize is template-based (not AI)**: Text generation uses simple switch-case mapping (e.g., "ORIGIN_MYTH" -> "Origin: {key} - {value}"). No LLM or NLP processing involved.

5. **Polymorphic entity references in lore**: `RelatedEntityId` + `RelatedEntityType` in lore elements supports referencing any entity type (characters, locations, species) without foreign key constraints.

6. **Timestamp preservation on update**: SetLore preserves the original `CreatedAtUnix` timestamp when updating existing lore. Only `UpdatedAtUnix` changes.

7. **DeleteAll is O(n)**: Iterates through all participations for the realm, removing each from event indexes individually. For realms with thousands of events, this could be slow.

### Design Considerations (Requires Planning)

1. **In-memory filtering and pagination**: All list operations load full indexes, fetch all records, filter in memory, then paginate. For realms with very high participation counts, this loads everything into memory.

2. **No index cleanup on orphaned events**: Event indexes accumulate participation IDs. If a realm is deleted but its participations aren't cleaned up, event indexes contain stale entries.

3. **Lore stored as single document**: All lore elements for a realm are stored in one `RealmLoreData` object. Very large lore collections (hundreds of elements) would be loaded/saved atomically on every modification.

4. **No concurrency control on indexes**: Dual-index updates (add to realm index AND event index) are not transactional. A crash between the two updates could leave indexes inconsistent.

5. **Metadata stored as `object?`**: Participation metadata accepts any JSON structure with no schema validation. Enables flexibility but sacrifices type safety and queryability.

6. **Event index `RealmId` field is misleading**: At line 124, when creating an event index, the code sets `RealmId = body.EventId.ToString()`. The field name is `RealmId` but stores an EventId. The `RealmParticipationIndexData` class is reused for both realm and event indices with confusing field naming.

7. **Sequential fetches for participations (no bulk get)**: Lines 192-208 and 277-291 iterate through each participation ID and fetch sequentially with `GetAsync`. Unlike character-history which uses `GetBulkAsync` via DualIndexHelper, realm-history makes N individual state store calls.

8. **GetLore always returns both timestamps**: Lines 449-450 always return both `CreatedAt` and `UpdatedAt`. Unlike character-history which returns `UpdatedAt=null` if backstory was never modified after creation, realm-history always populates both.

9. **DeleteParticipation doesn't delete empty indices**: Lines 356-369 update realm and event indices by removing the participation ID but don't delete the index documents when they become empty. Only DeleteAll deletes the realm index.

10. **Summarize doesn't publish any event**: Like character-history, `SummarizeRealmHistoryAsync` (lines 841-926) generates summaries silently with no event publication. Consuming services have no notification that summarization occurred.

11. **ORIGIN and INSTIGATOR roles both map to "instigated"**: Lines 1005 and 1011 in `GenerateEventSummary` both produce "instigated" - semantically different roles with identical summary text.

12. **Unknown lore element types display raw enum string**: Line 995 uses `_ => element.ElementType` for unrecognized element types, so the raw string like "NEW_TYPE" appears verbatim in summaries.

13. **Unknown participation roles default to "participated in"**: Line 1013 uses `_ => "participated in"` for unrecognized roles.

14. **Role enum parsing fallback to AFFECTED**: Lines 943-945 in `MapToRealmHistoricalParticipation` fall back to `RealmEventRole.AFFECTED` if the stored role string can't be parsed. Data with unknown role values silently degrades.

15. **ElementType enum parsing fallback to ORIGIN_MYTH**: Lines 957-959 fall back to `RealmLoreElementType.ORIGIN_MYTH` if parsing fails. Unknown element types silently become origin myths.

16. **Doesn't use shared helper classes**: Unlike character-history which uses `DualIndexHelper` and `BackstoryStorageHelper`, realm-history implements these patterns directly with nearly identical code. This is duplicate implementation that could diverge over time.
