# Character History Plugin Deep Dive

> **Plugin**: lib-character-history
> **Schema**: schemas/character-history-api.yaml
> **Version**: 1.0.0
> **State Store**: character-history-statestore (MySQL)

---

## Overview

Historical event participation and backstory management for characters. Tracks when characters participate in world events (wars, disasters, political upheavals) with role and significance tracking, and maintains machine-readable backstory elements (origin, occupation, training, trauma, fears, goals) for behavior system consumption. Provides template-based text summarization for character compression. Uses helper abstractions (`IDualIndexHelper`, `IBackstoryStorageHelper`) for storage patterns shared with the realm-history service.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | MySQL persistence for participation records, indexes, and backstory |
| lib-messaging (`IMessageBus`) | Publishing participation and backstory lifecycle events |
| lib-messaging (`IEventConsumer`) | Event handler registration (no current handlers) |
| lib-resource (events) | Publishes `resource.reference.registered` and `resource.reference.unregistered` events for character reference tracking |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-actor | Reads backstory via `ICharacterHistoryClient` (PersonalityCache) to inform NPC behavior decisions |
| lib-analytics | Subscribes to `character-history.participation.recorded`, `character-history.backstory.created`, `character-history.backstory.updated` for historical analytics |
| lib-resource | Consumes `resource.reference.registered/unregistered` events to track character references for cleanup coordination |

**Note**: lib-character (L2) does **not** call this service per SERVICE_HIERARCHY - L2 cannot depend on L4. The character service explicitly notes it cannot call CharacterHistory. Callers needing history data must call this service directly.

---

## State Storage

**Store**: `character-history-statestore` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `participation-{participationId}` | `ParticipationData` | Individual participation record |
| `participation-index-{characterId}` | Index list | List of participation IDs for a character |
| `participation-event-{eventId}` | Index list | List of participation IDs for an event |
| `backstory-{characterId}` | `BackstoryData` | All backstory elements for a character (single document) |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `character-history.participation.recorded` | `CharacterParticipationRecordedEvent` | New participation recorded |
| `character-history.participation.deleted` | `CharacterParticipationDeletedEvent` | Participation removed |
| `character-history.backstory.created` | `CharacterBackstoryCreatedEvent` | First backstory created for a character |
| `character-history.backstory.updated` | `CharacterBackstoryUpdatedEvent` | Existing backstory modified |
| `character-history.backstory.deleted` | `CharacterBackstoryDeletedEvent` | All backstory deleted |
| `character-history.deleted` | `CharacterHistoryDeletedEvent` | All history (participation + backstory) deleted |
| `resource.reference.registered` | `ResourceReferenceRegisteredEvent` | Participation or backstory created (tracks character references) |
| `resource.reference.unregistered` | `ResourceReferenceUnregisteredEvent` | Participation or backstory deleted (unregisters character references) |

### Consumed Events

This plugin does not consume external events.

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| --- | --- | --- | No service-specific configuration properties |

The generated `CharacterHistoryServiceConfiguration` contains only the framework-level `ForceServiceId` property.

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<CharacterHistoryService>` | Scoped | Structured logging |
| `CharacterHistoryServiceConfiguration` | Singleton | Framework config (empty) |
| `IStateStoreFactory` | Singleton | State store access |
| `IMessageBus` | Scoped | Event publishing (including resource reference events) |
| `IEventConsumer` | Scoped | Event registration (no handlers) |
| `IDualIndexHelper<ParticipationData>` | (inline) | Dual-index CRUD for participations |
| `IBackstoryStorageHelper<BackstoryData, BackstoryElementData>` | (inline) | Backstory CRUD with merge semantics |
| `CharacterHistoryReferenceTracking` | (partial class) | Generated helper methods for resource reference registration/unregistration |

Service lifetime is **Scoped** (per-request).

---

## API Endpoints (Implementation Notes)

### Participation Operations (4 endpoints)

- **Record** (`/character-history/record-participation`): Checks for existing participation for same characterId+eventId (returns 409 Conflict if duplicate). Creates unique participation ID. Stores record and updates dual indexes (character and event). Registers character reference with lib-resource. Publishes recorded event.
- **GetParticipation** (`/character-history/get-participation`): Fetches all records for a character via primary index, filters by event category and minimum significance, sorts by event date descending, paginates in-memory (max 100 per page).
- **GetEventParticipants** (`/character-history/get-event-participants`): Inverse query via secondary (event) index. Filters by role, sorts by significance descending.
- **DeleteParticipation** (`/character-history/delete-participation`): Retrieves record first (to get index keys for cleanup), removes from both indexes, publishes deletion event.

### Backstory Operations (4 endpoints)

- **GetBackstory** (`/character-history/get-backstory`): Returns NotFound if no backstory exists. Filters by element types array and minimum strength. UpdatedAt is null if never updated.
- **SetBackstory** (`/character-history/set-backstory`): Merge-or-replace semantics via `replaceExisting` flag. Merge matches by type+key pair. Publishes created or updated event with element count.
- **AddBackstoryElement** (`/character-history/add-backstory-element`): Adds or updates single element (updates if type+key match exists). Creates backstory document if none exists.
- **DeleteBackstory** (`/character-history/delete-backstory`): Removes entire backstory. Returns NotFound if no backstory exists.

### Management Operations (2 endpoints)

- **DeleteAll** (`/character-history/delete-all`): Unregisters all character references first, then uses `RemoveAllByPrimaryKeyAsync` with lambda to extract secondary keys for cleanup. Also deletes backstory. Returns participation count and backstory boolean. Called via lib-resource cleanup callback during character deletion.
- **Summarize** (`/character-history/summarize`): Template-based text generation. Selects top N backstory elements by strength (default 5, max 20) and top N participations by significance (default 10, max 20). Uses switch-case text patterns.

---

## Visual Aid

```
Backstory Element Model
========================

  SetBackstory(characterId=C1, elements=[...], replaceExisting=false)
       │
       ▼
  ┌─────────────────────────────────────────┐
  │ backstory-C1                            │
  │                                         │
  │ Elements:                               │
  │ ┌─────────┬──────────┬─────────┬──────┐│
  │ │  Type   │   Key    │  Value  │Streng││
  │ ├─────────┼──────────┼─────────┼──────┤│
  │ │ ORIGIN  │ homeland │ north.. │ 0.9  ││
  │ │ TRAUMA  │ battle   │ siege.. │ 0.7  ││
  │ │ GOAL    │ revenge  │ avenge..│ 0.8  ││
  │ │ FEAR    │ fire     │ burns..│ 0.6  ││
  │ └─────────┴──────────┴─────────┴──────┘│
  │                                         │
  │ CreatedAtUnix: 1706000000              │
  │ UpdatedAtUnix: 1706500000              │
  └─────────────────────────────────────────┘

  Merge Logic (replaceExisting=false):
    New element with type=ORIGIN, key=homeland → UPDATE existing
    New element with type=BELIEF, key=honor   → APPEND new


Text Summarization
===================

  SummarizeHistory(characterId=C1, maxBackstoryPoints=3, maxLifeEvents=2)
       │
       ▼
  Backstory (top 3 by strength):
    "From the northlands"         ← ORIGIN: homeland → northlands
    "Seeks to avenge their kin"   ← GOAL: revenge → avenge their kin
    "Experienced the siege"       ← TRAUMA: battle → the siege

  Participations (top 2 by significance):
    "led the Battle of Stormgate"     ← LEADER + "Battle of Stormgate"
    "survived the Great Plague"       ← SURVIVOR + "Great Plague"
```

---

## Stubs & Unimplemented Features

None. The service is feature-complete for its scope.

---

## Potential Extensions

1. **AI-powered summarization**: Replace template-based text with LLM-generated narrative prose.
<!-- AUDIT:NEEDS_DESIGN:2026-02-01:https://github.com/beyond-immersion/bannou-service/issues/230 -->
2. **Backstory element limits**: Configurable maximum elements per character to prevent unbounded growth.
<!-- AUDIT:NEEDS_DESIGN:2026-01-31:https://github.com/beyond-immersion/bannou-service/issues/207 -->
3. **Participation pagination at store level**: Database-side pagination instead of in-memory for large histories.
<!-- AUDIT:NEEDS_DESIGN:2026-01-31:https://github.com/beyond-immersion/bannou-service/issues/200 -->
4. **Cross-character event correlation**: Query which characters participated together in the same events.
<!-- AUDIT:NEEDS_DESIGN:2026-02-01:https://github.com/beyond-immersion/bannou-service/issues/231 -->

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

None currently identified.

### Intentional Quirks

1. **Backstory returns NotFound vs empty list**: Unlike the parallel realm-history service which returns OK with empty elements, character-history returns NotFound for missing backstory. Inconsistent design between similar services.

2. **UpdatedAt null semantics**: `GetBackstory` returns `UpdatedAt = null` when backstory has never been modified after initial creation (CreatedAtUnix == UpdatedAtUnix).

3. **Helper abstractions constructed inline**: `DualIndexHelper` and `BackstoryStorageHelper` are instantiated directly in the constructor rather than registered in DI. This is intentional: helpers require service-specific configuration (key prefixes, data access lambdas) that can't be generalized. Testing works via `IStateStoreFactory` mocking - no special setup required.

### Design Considerations (Requires Planning)

1. **In-memory pagination for all list operations**: Fetches ALL participation records into memory, then filters and paginates. Characters with thousands of participations load entire list. Should implement store-level pagination.
<!-- AUDIT:NEEDS_DESIGN:2026-01-31:https://github.com/beyond-immersion/bannou-service/issues/200 -->

2. **No backstory element count limit**: No maximum on elements per character. Unbounded growth possible if AddBackstoryElement is called repeatedly without cleanup.
<!-- AUDIT:NEEDS_DESIGN:2026-01-31:https://github.com/beyond-immersion/bannou-service/issues/207 -->

3. ~~**Helper abstractions are inline**~~: **FIXED** (2026-02-01) - Moved to Intentional Quirks. The inline construction is intentional: helpers require service-specific configuration (key prefixes, data access lambdas) that can't be generalized through DI. Testing is straightforward via `IStateStoreFactory` mocking - the original "requires constructor inspection" claim was inaccurate.

4. ~~**GUID parsing without validation in helper config**~~: **REMOVED** (2026-02-01) - The documented concern was invalid. The `SetEntityId` delegate is only called when WRITING new data (with the `entityId` parameter from the request, which is already a valid GUID string from `Guid.ToString()`). It is never called during READ operations or deserialization of stored data. Corrupted store data cannot trigger this code path.

5. ~~**DeleteAll is O(n) with secondary index cleanup**~~: **FIXED** (2026-02-02) - Rewrote `DualIndexHelper.RemoveAllByPrimaryKeyAsync` to use bulk operations (`GetBulkAsync`, `SaveBulkAsync`, `DeleteBulkAsync`). Now uses O(1) database round-trips (~7 operations) regardless of record count, instead of ~4N operations. Both lib-character-history and lib-realm-history benefit from this shared helper optimization.

6. **Metadata stored as `object?`**: Participation metadata accepts any JSON structure. On deserialization from JSON, becomes `JsonElement` or similar untyped object. No schema validation.

7. **Parallel service pattern with realm-history**: Both services use nearly identical patterns (dual-index participations, document-based lore/backstory, template summarization) but with subtle behavioral differences (NotFound vs empty list for missing data).

8. **Empty indices left behind after RemoveRecordAsync**: When removing a single participation, `RemoveRecordAsync` updates both primary and secondary indices but doesn't delete them when they become empty. Only `RemoveAllByPrimaryKeyAsync` (called by DeleteAll) deletes the primary index. This leaves orphaned empty index documents in the state store.

9. **Summarize doesn't publish any event**: Unlike all other operations which publish typed events (recorded, deleted, created, updated), `SummarizeHistoryAsync` generates summaries silently with no event publication. Consuming services have no notification that summarization occurred.

10. **Unknown element types fall back to generic format**: `GenerateBackstorySummary` uses `_ => $"{element.Key}: {element.Value}"` for unrecognized element types. If new backstory element types are added to the schema, they'll produce raw key/value summaries until the switch-case is updated.

11. **Unknown participation roles fall back to "participated in"**: `GenerateParticipationSummary` uses `_ => "participated in"` for unrecognized roles. Generic fallback may not reflect actual involvement.

12. **FormatValue is simplistic transformation**: `value.Replace("_", " ").ToLowerInvariant()` only handles snake_case, doesn't handle camelCase or PascalCase, doesn't capitalize sentences. "NORTHERN_KINGDOM" becomes "northern kingdom" but "NorthernKingdom" stays as-is.

13. **GetBulkAsync results silently drop missing records**: `DualIndexHelper.GetRecordsByIdsAsync` returns `.Values` from bulk get - any IDs in the index that no longer exist in the store are silently excluded from results. No logging, no error, just fewer items than expected.

14. **Empty/null entityId returns silently without logging**: Multiple helper methods (GetAsync, DeleteAsync, ExistsAsync in BackstoryStorageHelper; GetRecordAsync, GetRecordIdsByPrimaryKeyAsync, etc. in DualIndexHelper) check `string.IsNullOrEmpty(entityId)` and return null/false/0 without any logging. Invalid calls produce no trace.

15. **AddBackstoryElement is upsert**: The doc mentions "Adds or updates single element" at the API level, but note that this means AddBackstoryElement with the same type+key silently replaces the existing element's value and strength. No event distinguishes "added new" from "updated existing" when using this endpoint.

---

## Work Tracking

### Pending Design Review
- **2026-01-31**: [#200](https://github.com/beyond-immersion/bannou-service/issues/200) - Store-level pagination for list operations (in-memory pagination causes memory pressure for characters with many participations)
- **2026-01-31**: [#207](https://github.com/beyond-immersion/bannou-service/issues/207) - Add configurable backstory element count limit (prevent unbounded growth)
- **2026-02-01**: [#230](https://github.com/beyond-immersion/bannou-service/issues/230) - AI-powered summarization (requires building new LLM service infrastructure)
- **2026-02-01**: [#231](https://github.com/beyond-immersion/bannou-service/issues/231) - Cross-character event correlation query (API design decisions needed)

### Completed
- **2026-02-02**: [#261](https://github.com/beyond-immersion/bannou-service/issues/261) - Optimized `RemoveAllByPrimaryKeyAsync` in `DualIndexHelper` to use bulk operations, reducing database round-trips from ~4N to ~7 regardless of record count. Both lib-character-history and lib-realm-history benefit.
- **2026-02-02**: Verified and removed "Schema promises 409 Conflict but implementation allows duplicates" bug - fix is clean with no non-obvious behavior (simple input validation before creation).
- **2026-02-02**: Corrected lib-character dependent documentation - lib-character (L2) cannot call this service per SERVICE_HIERARCHY.
- **2026-02-02**: Added lib-resource dependency and resource reference tracking events documentation.
- **2026-02-01**: Removed "GUID parsing without validation in helper config" from Design Considerations - the documented concern was invalid. The `SetEntityId` delegate is only called when writing new data with validated request parameters, never during reads or deserialization.
- **2026-02-01**: Reclassified "Helper abstractions are inline" from Design Considerations to Intentional Quirks - the inline construction is intentional design, and testing via `IStateStoreFactory` mocking is straightforward. The original "requires constructor inspection" claim was inaccurate.
- **2026-01-31**: Fixed duplicate participation bug - `RecordParticipationAsync` now returns 409 Conflict when character+event pair already exists, matching schema contract.
