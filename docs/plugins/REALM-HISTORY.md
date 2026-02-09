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
| lib-state (`IDistributedLockProvider`) | Distributed locks for DualIndexHelper and BackstoryStorageHelper write operations (per IMPLEMENTATION TENETS: multi-instance safety) |
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
| `MaxLoreElements` | `REALM_HISTORY_MAX_LORE_ELEMENTS` | 100 | Maximum lore elements per realm. Returns BadRequest when exceeded. |
| `IndexLockTimeoutSeconds` | `REALM_HISTORY_INDEX_LOCK_TIMEOUT_SECONDS` | 15 | Distributed lock timeout for DualIndexHelper and BackstoryStorageHelper write operations |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<RealmHistoryService>` | Scoped | Structured logging |
| `RealmHistoryServiceConfiguration` | Singleton | Service config (MaxLoreElements limit, IndexLockTimeoutSeconds, ForceServiceId) |
| `IStateStoreFactory` | Singleton | State store access (passed to helpers) |
| `IMessageBus` | Scoped | Event publishing |
| `IEventConsumer` | Scoped | Event registration (no handlers currently) |
| `IDistributedLockProvider` | Singleton | Distributed locking for helper write operations |
| `DualIndexHelper<RealmParticipationData>` | Instance | Participation dual-index storage (instantiated in constructor) |
| `BackstoryStorageHelper<RealmLoreData, RealmLoreElementData>` | Instance | Lore element storage (instantiated in constructor) |

Service lifetime is **Scoped** (per-request). The helper classes are instantiated per-service instance, not injected via DI.

---

## API Endpoints (Implementation Notes)

### Participation Operations (4 endpoints)

- **Record** (`/realm-history/record-participation`): Creates unique participation ID. Stores record and updates both realm and event indexes under distributed lock. Returns 409 Conflict if lock cannot be acquired. Registers realm reference with lib-resource. Publishes recorded event.
- **GetParticipation** (`/realm-history/get-participation`): Server-side paginated query via `IJsonQueryableStateStore`. Filters by realm ID, optional event category, and minimum impact at the database level. Sorts by event date descending. Bypasses DualIndexHelper for reads.
- **GetEventParticipants** (`/realm-history/get-event-participants`): Server-side paginated query via `IJsonQueryableStateStore`. Filters by event ID and optional role at the database level. Sorts by impact descending. Bypasses DualIndexHelper for reads.
- **DeleteParticipation** (`/realm-history/delete-participation`): Unregisters realm reference, removes record and updates both indexes under distributed lock. Returns 409 Conflict if lock cannot be acquired. DualIndexHelper deletes empty index documents automatically. Publishes deletion event.

### Lore Operations (4 endpoints)

- **GetLore** (`/realm-history/get-lore`): Returns OK with empty list if no lore exists (never 404). Filters by element type and strength threshold. Converts timestamps via `TimestampHelper.FromUnixSeconds`.
- **SetLore** (`/realm-history/set-lore`): Merge-or-replace semantics controlled by `replaceExisting` flag. Merge updates existing elements by type+key pair and adds new ones. Validates against `MaxLoreElements` limit — replace mode checks input count, merge mode calculates post-merge count (existing + truly new). Returns BadRequest when limit exceeded. Write operations protected by distributed lock; returns 409 Conflict if lock cannot be acquired. Registers realm reference on new lore creation. Publishes created or updated event.
- **AddLoreElement** (`/realm-history/add-lore-element`): Adds single element. Updates if type+key match exists. Validates against `MaxLoreElements` limit — only rejects truly new elements when at limit; updates to existing type+key pairs are always allowed. Creates lore document if none exists. Registers realm reference on new lore creation. Publishes created or updated event.
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
       └──► JsonQueryPagedAsync (bypasses index)
                │
                Conditions: $.ParticipationId EXISTS
                            $.RealmId = R1
                            $.EventCategory = WAR  (optional)
                            $.Impact >= 0.5        (optional)
                SortBy: $.EventDateUnix DESC
                │
                ▼
            Server-side filtered + paginated results


  GetEventParticipants(eventId=E1)
       │
       └──► JsonQueryPagedAsync (bypasses index)
                │
                Conditions: $.ParticipationId EXISTS
                            $.EventId = E1
                            $.Role = DEFENDER  (optional)
                SortBy: $.Impact DESC
                │
                ▼
            Server-side filtered + paginated results
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

1. ~~**In-memory filtering and pagination**~~: FIXED - `GetRealmParticipationAsync` and `GetRealmEventParticipantsAsync` now use server-side MySQL JSON queries via `IJsonQueryableStateStore.JsonQueryPagedAsync()`, pushing filters and pagination to the database. See [#200](https://github.com/beyond-immersion/bannou-service/issues/200).
<!-- AUDIT:FIXED:2026-02-08:https://github.com/beyond-immersion/bannou-service/issues/200 -->

2. **Lore stored as single document**: All lore elements for a realm are stored in one `RealmLoreData` object. Very large lore collections (hundreds of elements) would be loaded/saved atomically on every modification.
<!-- AUDIT:NEEDS_DESIGN:2026-02-06:https://github.com/beyond-immersion/bannou-service/issues/306 -->

3. ~~**No concurrency control on indexes**~~: FIXED (2026-02-08) - DualIndexHelper and BackstoryStorageHelper write operations now use distributed locks per primary key. Lock failure returns `StatusCodes.Conflict`. Lock owner IDs use key prefix for traceability (e.g., `"realm-participation-{guid}"`). Secondary index is intentionally NOT locked: locking would serialize all writes across unrelated realms referencing the same event (global bottleneck), and reads bypass indexes entirely via `JsonQueryPagedAsync`. Worst-case race produces a stale index entry that self-heals on next write/delete. See [#307](https://github.com/beyond-immersion/bannou-service/issues/307).
<!-- AUDIT:FIXED:2026-02-08:https://github.com/beyond-immersion/bannou-service/issues/307 -->

4. **Metadata stored as `object?`**: Participation metadata accepts any JSON structure with no schema validation. Enables flexibility but sacrifices type safety and queryability.
<!-- AUDIT:NEEDS_DESIGN:2026-02-06:https://github.com/beyond-immersion/bannou-service/issues/308 -->

5. ~~**Read-modify-write without distributed locks**~~: FIXED (2026-02-08) - See #3 above. Same issue, now resolved.
<!-- AUDIT:FIXED:2026-02-08:https://github.com/beyond-immersion/bannou-service/issues/307 -->

---

## Work Tracking

### Pending Design Review
- **2026-02-06**: [#309](https://github.com/beyond-immersion/bannou-service/issues/309) - Resolve NotFound vs empty-list inconsistency between character-history and realm-history (parallel service API consistency; GetLore returns OK with empty list while GetBackstory returns NotFound)
- **2026-02-06**: [#308](https://github.com/beyond-immersion/bannou-service/issues/308) - Replace `object?`/`additionalProperties:true` metadata pattern with typed schemas (systemic issue affecting 14+ services; violates T25 type safety)
- ~~**2026-02-06**: [#307](https://github.com/beyond-immersion/bannou-service/issues/307) - Concurrency control for DualIndexHelper index updates~~ → **COMPLETED** (see below)
- **2026-02-06**: [#306](https://github.com/beyond-immersion/bannou-service/issues/306) - Single-document storage for lore elements (evaluate whether document storage is problematic for large lore collections; shared pattern with character-history via BackstoryStorageHelper)
- **2026-02-02**: [#266](https://github.com/beyond-immersion/bannou-service/issues/266) - Event-level aggregation (API design decisions needed: metrics, role breakdowns, filtering, new endpoint vs enhancement)
- **2026-02-02**: [#268](https://github.com/beyond-immersion/bannou-service/issues/268) - Lore inheritance (BLOCKED: requires realm hierarchy which contradicts current Realm service design of "peer worlds with no hierarchical relationships")
- **2026-02-02**: [#269](https://github.com/beyond-immersion/bannou-service/issues/269) - AI-powered summarization (BLOCKED on character-history #230 which tracks shared LLM infrastructure work)
- **2026-02-02**: [#270](https://github.com/beyond-immersion/bannou-service/issues/270) - Timeline visualization (may already be satisfied by existing `GetRealmParticipation` endpoint which returns chronologically-sorted data)

### Completed

- **2026-02-08**: [#307](https://github.com/beyond-immersion/bannou-service/issues/307) - Added distributed locking to DualIndexHelper and BackstoryStorageHelper write operations. Lock per primary key (realmId) for index updates, per entityId for lore. Lock failure returns `StatusCodes.Conflict`. Configurable timeout via `IndexLockTimeoutSeconds` (default 15). Lock owner IDs use key prefix for traceability (e.g., `"realm-participation-{guid}"`). Secondary index is intentionally NOT locked: locking would serialize all writes across unrelated realms referencing the same event (global bottleneck), and reads bypass indexes entirely via `JsonQueryPagedAsync`. Worst-case race produces a stale index entry that self-heals on next write/delete.
- **2026-02-08**: [#350](https://github.com/beyond-immersion/bannou-service/issues/350) - Configurable lore element count limit. Added `MaxLoreElements` config property (default 100) with validation in `SetRealmLoreAsync` and `AddRealmLoreElementAsync`. Follow-up from character-history #207.
- **2026-02-08**: [#200](https://github.com/beyond-immersion/bannou-service/issues/200) - Store-level pagination for list operations. Replaced in-memory fetch-all-then-paginate with server-side MySQL JSON queries via `IJsonQueryableStateStore.JsonQueryPagedAsync()`. DualIndexHelper retained for write operations only.
