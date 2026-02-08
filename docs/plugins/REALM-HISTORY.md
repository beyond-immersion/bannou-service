# Realm History Plugin Deep Dive

> **Plugin**: lib-realm-history
> **Schema**: schemas/realm-history-api.yaml
> **Version**: 1.0.0
> **State Store**: realm-history-statestore (MySQL)

---

## Overview

Historical event participation and lore management (L4 GameFeatures) for realms. Tracks when realms participate in world events (wars, treaties, cataclysms) with role and impact tracking, and maintains machine-readable lore elements (origin myths, cultural practices, political systems) for behavior system consumption. Provides text summarization for realm archival via lib-resource. Shares storage helper abstractions with the character-history service.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | MySQL persistence for participation records, indexes, and lore |
| lib-messaging (`IMessageBus`) | Publishing participation, lore lifecycle, and resource reference events |
| lib-messaging (`IEventConsumer`) | Event handler registration (no current handlers) |
| lib-resource (`IResourceClient`) | Registering cleanup and compression callbacks during plugin startup (in `RealmHistoryServicePlugin`, not in service constructor) |
| lib-resource (events) | Publishes `resource.reference.registered` and `resource.reference.unregistered` events for realm reference tracking |
| `IDualIndexHelper` | Dual-index storage pattern for participation records (from `bannou-service/History/`) |
| `IBackstoryStorageHelper` | Lore element storage with merge/replace semantics (from `bannou-service/History/`) |
| `PaginationHelper` | Calculates skip/take for pagination (from `bannou-service/History/`) |
| `TimestampHelper` | Unix timestamp conversion utilities (from `bannou-service/History/`) |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-analytics | Subscribes to `realm-history.participation.recorded`, `realm-history.lore.created`, `realm-history.lore.updated` events to ingest realm history data for analytics aggregation |
| lib-resource | Consumes `resource.reference.registered/unregistered` events to track realm references for cleanup coordination |

---

## State Storage

**Store**: `realm-history-statestore` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `realm-participation-{participationId}` | `RealmParticipationData` | Individual participation record |
| `realm-participation-index-{realmId}` | `HistoryIndexData` | List of participation IDs for a realm |
| `realm-participation-event-{eventId}` | `HistoryIndexData` | List of participation IDs for an event |
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
| `resource.reference.registered` | `ResourceReferenceRegisteredEvent` | Participation or lore created (tracks realm references) |
| `resource.reference.unregistered` | `ResourceReferenceUnregisteredEvent` | Participation or lore deleted (unregisters realm references) |

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
| `RealmHistoryServiceConfiguration` | Singleton | Framework config (minimal, only ForceServiceId) |
| `IStateStoreFactory` | Singleton | State store access (passed to helpers) |
| `IMessageBus` | Scoped | Event publishing |
| `IEventConsumer` | Scoped | Event registration (no handlers currently) |
| `DualIndexHelper<RealmParticipationData>` | Instance | Participation dual-index storage (instantiated in constructor) |
| `BackstoryStorageHelper<RealmLoreData, RealmLoreElementData>` | Instance | Lore element storage (instantiated in constructor) |

Service lifetime is **Scoped** (per-request). The helper classes are instantiated per-service instance, not injected via DI.

---

## API Endpoints (Implementation Notes)

### Participation Operations (4 endpoints)

- **Record** (`/realm-history/record-participation`): Creates unique participation ID. Stores record and updates both realm and event indexes. Registers realm reference with lib-resource. Publishes recorded event.
- **GetParticipation** (`/realm-history/get-participation`): Fetches all records for a realm using bulk fetch, filters by event category and minimum impact, sorts by event date descending, paginates via `PaginationHelper` (default page size 20).
- **GetEventParticipants** (`/realm-history/get-event-participants`): Inverse query using secondary index with bulk fetch. Filters by role, sorts by impact descending.
- **DeleteParticipation** (`/realm-history/delete-participation`): Unregisters realm reference, removes record and updates both indexes. DualIndexHelper deletes empty index documents automatically. Publishes deletion event.

### Lore Operations (4 endpoints)

- **GetLore** (`/realm-history/get-lore`): Returns OK with empty list if no lore exists (never 404). Filters by element type and strength threshold. Converts timestamps via `TimestampHelper.FromUnixSeconds`.
- **SetLore** (`/realm-history/set-lore`): Merge-or-replace semantics controlled by `replaceExisting` flag. Merge updates existing elements by type+key pair and adds new ones. Registers realm reference on new lore creation. Publishes created or updated event.
- **AddLoreElement** (`/realm-history/add-lore-element`): Adds single element. Updates if type+key match exists. Creates lore document if none exists. Registers realm reference on new lore creation. Publishes created or updated event.
- **DeleteLore** (`/realm-history/delete-lore`): Unregisters realm reference, removes entire lore document. Returns NotFound if no lore exists.

### Management Operations (2 endpoints)

- **DeleteAll** (`/realm-history/delete-all`): Comprehensive cleanup. Unregisters all realm references first, then iterates all realm participations, removes from event indexes, deletes records. Also deletes lore. Deletes the realm index after cleanup. Returns counts of deleted items. Called via lib-resource cleanup callback during realm deletion.
- **Summarize** (`/realm-history/summarize`): Generates human-readable text summaries from machine-readable data. Selects top N elements by strength for lore summaries. Selects top N events by impact for participation summaries. Configurable limits (maxLorePoints: 1-20, default 5; maxHistoricalEvents: 1-20, default 10).

### Compression Operations (2 endpoints)

- **GetCompressData** (`/realm-history/get-compress-data`): Returns complete realm history data (participations + lore) for archive storage. Called by Resource service during realm compression. Returns 404 only if BOTH participations and lore are missing. Includes generated text summaries in the archive.
- **RestoreFromArchive** (`/realm-history/restore-from-archive`): Restores realm history data from a Base64-encoded gzipped JSON archive. Re-registers realm references for restored participations and lore. Called by Resource service during realm decompression.

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
<!-- AUDIT:NEEDS_DESIGN:2026-02-02:https://github.com/beyond-immersion/bannou-service/issues/266 -->
2. **Lore inheritance**: Child realms inheriting lore elements from parent realms (if realm hierarchy is added).
<!-- AUDIT:NEEDS_DESIGN:2026-02-02:https://github.com/beyond-immersion/bannou-service/issues/268 -->
3. **AI-powered summarization**: Replace template-based summaries with LLM-generated narrative text.
<!-- AUDIT:NEEDS_DESIGN:2026-02-02:https://github.com/beyond-immersion/bannou-service/issues/269 -->
4. **Realm timeline visualization**: Chronological event data suitable for timeline UI rendering.
<!-- AUDIT:NEEDS_DESIGN:2026-02-02:https://github.com/beyond-immersion/bannou-service/issues/270 -->

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

None identified.

### Intentional Quirks (Documented Behavior)

1. **GetLore returns OK with empty list**: Unlike DeleteLore (which returns NotFound for missing lore), GetLore always returns 200 OK with an empty elements list. Read operations are lenient; delete operations are strict. This differs from character-history's `GetBackstory` which returns 404 NotFound for missing backstory.
<!-- AUDIT:NEEDS_DESIGN:2026-02-06:https://github.com/beyond-immersion/bannou-service/issues/309 -->

2. **Summarize is a read-only operation (no event)**: `SummarizeRealmHistoryAsync` does not publish any event because it's a pure read operation that doesn't modify state. Per FOUNDATION TENETS (Event-Driven Architecture), events notify about state changes - read operations don't trigger events. This is consistent with other read operations like `GetRealmLore` and `GetRealmParticipation`.

3. **Unknown lore element types display raw enum string**: The `GenerateLoreSummary` method uses `_ => element.ElementType.ToString()` for unrecognized element types, so the raw string like "NEW_TYPE" appears verbatim in summaries.

4. **Unknown participation roles default to "participated in"**: The `GenerateEventSummary` method uses `_ => "participated in"` for unrecognized roles.

### Design Considerations (Requires Planning)

1. **In-memory filtering and pagination**: All list operations load full indexes, fetch all records via bulk fetch, filter in memory, then paginate. For realms with very high participation counts (thousands of events), this loads everything into memory.
<!-- AUDIT:NEEDS_DESIGN:2026-02-02:https://github.com/beyond-immersion/bannou-service/issues/200 -->

2. **Lore stored as single document**: All lore elements for a realm are stored in one `RealmLoreData` object. Very large lore collections (hundreds of elements) would be loaded/saved atomically on every modification.
<!-- AUDIT:NEEDS_DESIGN:2026-02-06:https://github.com/beyond-immersion/bannou-service/issues/306 -->

3. **No concurrency control on indexes**: Dual-index updates (add to realm index AND event index) are not transactional. A crash between the two updates could leave indexes inconsistent.
<!-- AUDIT:NEEDS_DESIGN:2026-02-06:https://github.com/beyond-immersion/bannou-service/issues/307 -->

4. **Metadata stored as `object?`**: Participation metadata accepts any JSON structure with no schema validation. Enables flexibility but sacrifices type safety and queryability.
<!-- AUDIT:NEEDS_DESIGN:2026-02-06:https://github.com/beyond-immersion/bannou-service/issues/308 -->

5. **Read-modify-write without distributed locks**: Dual-index updates and lore merge operations have no concurrency protection. Concurrent participation recordings for the same realm could result in lost index entries. (Duplicate of #3 - same root cause, different symptom description)
<!-- AUDIT:NEEDS_DESIGN:2026-02-06:https://github.com/beyond-immersion/bannou-service/issues/307 -->

---

## Work Tracking

### Pending Design Review
- **2026-02-06**: [#309](https://github.com/beyond-immersion/bannou-service/issues/309) - Resolve NotFound vs empty-list inconsistency between character-history and realm-history (parallel service API consistency; GetLore returns OK with empty list while GetBackstory returns NotFound)
- **2026-02-06**: [#308](https://github.com/beyond-immersion/bannou-service/issues/308) - Replace `object?`/`additionalProperties:true` metadata pattern with typed schemas (systemic issue affecting 14+ services; violates T25 type safety)
- **2026-02-06**: [#307](https://github.com/beyond-immersion/bannou-service/issues/307) - Concurrency control for DualIndexHelper index updates (read-modify-write pattern without locking; shared infrastructure with character-history)
- **2026-02-06**: [#306](https://github.com/beyond-immersion/bannou-service/issues/306) - Single-document storage for lore elements (evaluate whether document storage is problematic for large lore collections; shared pattern with character-history via BackstoryStorageHelper)
- **2026-02-02**: [#200](https://github.com/beyond-immersion/bannou-service/issues/200) - Store-level pagination for list operations (shared issue with character-history; in-memory pagination causes memory pressure for realms with many participations)
- **2026-02-02**: [#266](https://github.com/beyond-immersion/bannou-service/issues/266) - Event-level aggregation (API design decisions needed: metrics, role breakdowns, filtering, new endpoint vs enhancement)
- **2026-02-02**: [#268](https://github.com/beyond-immersion/bannou-service/issues/268) - Lore inheritance (BLOCKED: requires realm hierarchy which contradicts current Realm service design of "peer worlds with no hierarchical relationships")
- **2026-02-02**: [#269](https://github.com/beyond-immersion/bannou-service/issues/269) - AI-powered summarization (BLOCKED on character-history #230 which tracks shared LLM infrastructure work)
- **2026-02-02**: [#270](https://github.com/beyond-immersion/bannou-service/issues/270) - Timeline visualization (may already be satisfied by existing `GetRealmParticipation` endpoint which returns chronologically-sorted data)

### Completed

(No items)
