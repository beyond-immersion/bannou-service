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
| `PaginationHelper` | Calculates skip/take for pagination (from `bannou-service/History/`) |
| `TimestampHelper` | Unix timestamp conversion utilities (from `bannou-service/History/`) |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-analytics | Subscribes to `realm-history.participation.recorded`, `realm-history.lore.created`, `realm-history.lore.updated` events to ingest realm history data for analytics aggregation |

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

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `realm-history.participation.recorded` | `RealmParticipationRecordedEvent` | New participation recorded |
| `realm-history.participation.deleted` | `RealmParticipationDeletedEvent` | Participation removed |
| `realm-history.lore.created` | `RealmLoreCreatedEvent` | First lore created for a realm |
| `realm-history.lore.updated` | `RealmLoreUpdatedEvent` | Existing lore modified |
| `realm-history.lore.deleted` | `RealmLoreDeletedEvent` | All lore deleted for a realm |
| `realm-history.deleted` | `RealmHistoryDeletedEvent` | All history (participation + lore) deleted |

### Consumed Events

This plugin does not consume external events.

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `ForceServiceId` | `REALM_HISTORY_FORCE_SERVICE_ID` | null | Framework-level override for service ID |

The generated `RealmHistoryServiceConfiguration` contains only the framework-level `ForceServiceId` property. No service-specific configuration is defined.

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<RealmHistoryService>` | Scoped | Structured logging |
| `RealmHistoryServiceConfiguration` | Singleton | Framework config (minimal) |
| `IStateStoreFactory` | Singleton | State store access |
| `IMessageBus` | Scoped | Event publishing |
| `IEventConsumer` | Scoped | Event registration (no handlers currently) |

Service lifetime is **Scoped** (per-request).

---

## API Endpoints (Implementation Notes)

### Participation Operations (4 endpoints)

- **Record** (`/realm-history/record-participation`): Creates unique participation ID. Stores record and updates both realm and event indexes. Uses `GetBulkAsync` for efficient bulk fetching. Publishes recorded event.
- **GetParticipation** (`/realm-history/get-participation`): Fetches all records for a realm using bulk fetch, filters by event category and minimum impact, sorts by event date descending, paginates via `PaginationHelper` (default page size 20).
- **GetEventParticipants** (`/realm-history/get-event-participants`): Inverse query using secondary index with bulk fetch. Filters by role, sorts by impact descending.
- **DeleteParticipation** (`/realm-history/delete-participation`): Removes record and updates both indexes. Does not delete empty index documents. Publishes deletion event.

### Lore Operations (4 endpoints)

- **GetLore** (`/realm-history/get-lore`): Returns OK with empty list if no lore exists (never 404). Filters by element type and strength threshold. Converts timestamps via `TimestampHelper.FromUnixSeconds`.
- **SetLore** (`/realm-history/set-lore`): Merge-or-replace semantics controlled by `replaceExisting` flag. Merge updates existing elements by type+key pair and adds new ones. Publishes created or updated event.
- **AddLoreElement** (`/realm-history/add-lore-element`): Adds single element. Updates if type+key match exists. Creates lore document if none exists. Publishes created or updated event.
- **DeleteLore** (`/realm-history/delete-lore`): Removes entire lore document. Returns NotFound if no lore exists.

### Management Operations (2 endpoints)

- **DeleteAll** (`/realm-history/delete-all`): Comprehensive cleanup. Iterates all realm participations, removes from event indexes, deletes records. Also deletes lore. Deletes the realm index after cleanup. Returns counts of deleted items.
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
            GetBulkAsync: realm-participation-{id1}, {id2}, {id3}
                │
                ▼
            Filter (category, minImpact) → Sort (date desc) → Paginate


  GetEventParticipants(eventId=E1)
       │
       └──► realm-participation-event-E1 → [id1, id4, id5]
                │
                ▼
            GetBulkAsync: realm-participation-{id1}, {id4}, {id5}
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
5. **Shared helper migration**: Migrate to use `IDualIndexHelper` and `IBackstoryStorageHelper` from `bannou-service/History/` for consistency with character-history.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

None identified.

### Intentional Quirks (Documented Behavior)

1. **GetLore returns OK with empty list**: Unlike DeleteLore (which returns NotFound for missing lore), GetLore always returns 200 OK with an empty elements list. Read operations are lenient; delete operations are strict.

2. **Event index `RealmId` field stores EventId**: When creating an event index, the code sets `RealmId = body.EventId` (line 124). The `RealmParticipationIndexData` class is reused for both realm and event indices - the field named `RealmId` actually stores an EventId for event indexes. This is confusing but not a bug.

3. **Summarize doesn't publish any event**: `SummarizeRealmHistoryAsync` generates summaries silently with no event publication. Consuming services have no notification that summarization occurred.

4. **Unknown lore element types display raw enum string**: The `GenerateLoreSummary` method uses `_ => element.ElementType.ToString()` for unrecognized element types, so the raw string like "NEW_TYPE" appears verbatim in summaries.

5. **Unknown participation roles default to "participated in"**: The `GenerateEventSummary` method uses `_ => "participated in"` for unrecognized roles.

### Design Considerations (Requires Planning)

1. **In-memory filtering and pagination**: All list operations load full indexes, fetch all records via bulk fetch, filter in memory, then paginate. For realms with very high participation counts (thousands of events), this loads everything into memory.

2. **No index cleanup on orphaned events**: Event indexes accumulate participation IDs. If a realm is deleted but its participations aren't cleaned up through `DeleteAll`, event indexes contain stale entries pointing to deleted records.

3. **Lore stored as single document**: All lore elements for a realm are stored in one `RealmLoreData` object. Very large lore collections (hundreds of elements) would be loaded/saved atomically on every modification.

4. **No concurrency control on indexes**: Dual-index updates (add to realm index AND event index) are not transactional. A crash between the two updates could leave indexes inconsistent.

5. **Metadata stored as `object?`**: Participation metadata accepts any JSON structure with no schema validation. Enables flexibility but sacrifices type safety and queryability.

6. **DeleteParticipation doesn't delete empty indices**: Realm and event indices are updated by removing the participation ID but don't delete the index documents when they become empty. This leaves empty index documents in the database.

7. **DeleteAll is O(n) with N+1 queries**: Iterates through all participations for the realm individually, fetching each to find its eventId, then updating each event index separately. For realms with thousands of events, this could be slow. Uses bulk operations for participation records but not for event index updates.

8. **Doesn't use shared helper classes**: Unlike character-history which uses `IDualIndexHelper` and `IBackstoryStorageHelper`, realm-history implements these patterns directly with nearly identical code. This is duplicate implementation that could diverge over time.

9. **Read-modify-write without distributed locks**: Dual-index updates and lore merge operations have no concurrency protection. Concurrent participation recordings for the same realm could result in lost index entries.

---

## Work Tracking

*No active work items.*
