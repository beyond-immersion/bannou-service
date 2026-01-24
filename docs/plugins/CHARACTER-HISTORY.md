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

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-character | Fetches backstory for enriched character response; calls `SummarizeHistoryAsync` and `DeleteAllHistoryAsync` during character compression |
| lib-actor | Reads backstory via `ICharacterHistoryClient` to inform NPC behavior decisions |

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

| Topic | Trigger |
|-------|---------|
| `character-history.participation.recorded` | New participation recorded |
| `character-history.participation.deleted` | Participation removed |
| `character-history.backstory.created` | First backstory created for a character |
| `character-history.backstory.updated` | Existing backstory modified |
| `character-history.backstory.deleted` | All backstory deleted |
| `character-history.deleted` | All history (participation + backstory) deleted |

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
| `IMessageBus` | Scoped | Event publishing |
| `IEventConsumer` | Scoped | Event registration (no handlers) |
| `IDualIndexHelper<ParticipationData>` | (inline) | Dual-index CRUD for participations |
| `IBackstoryStorageHelper<BackstoryData, BackstoryElementData>` | (inline) | Backstory CRUD with merge semantics |

Service lifetime is **Scoped** (per-request).

---

## API Endpoints (Implementation Notes)

### Participation Operations (4 endpoints)

- **Record** (`/character-history/record-participation`): Creates unique participation ID. Stores record and updates dual indexes (character and event). Stores enum values as strings. Publishes recorded event.
- **GetParticipation** (`/character-history/get-participation`): Fetches all records for a character via primary index, filters by event category and minimum significance, sorts by event date descending, paginates in-memory (max 100 per page).
- **GetEventParticipants** (`/character-history/get-event-participants`): Inverse query via secondary (event) index. Filters by role, sorts by significance descending.
- **DeleteParticipation** (`/character-history/delete-participation`): Retrieves record first (to get index keys for cleanup), removes from both indexes, publishes deletion event.

### Backstory Operations (4 endpoints)

- **GetBackstory** (`/character-history/get-backstory`): Returns NotFound if no backstory exists. Filters by element types array and minimum strength. UpdatedAt is null if never updated.
- **SetBackstory** (`/character-history/set-backstory`): Merge-or-replace semantics via `replaceExisting` flag. Merge matches by type+key pair. Publishes created or updated event with element count.
- **AddBackstoryElement** (`/character-history/add-backstory-element`): Adds or updates single element (updates if type+key match exists). Creates backstory document if none exists.
- **DeleteBackstory** (`/character-history/delete-backstory`): Removes entire backstory. Returns NotFound if no backstory exists.

### Management Operations (2 endpoints)

- **DeleteAll** (`/character-history/delete-all`): Uses `RemoveAllByPrimaryKeyAsync` with lambda to extract secondary keys for cleanup. Also deletes backstory. Returns participation count and backstory boolean. Called by character service during compression.
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
2. **Backstory element limits**: Configurable maximum elements per character to prevent unbounded growth.
3. **Participation pagination at store level**: Database-side pagination instead of in-memory for large histories.
4. **Cross-character event correlation**: Query which characters participated together in the same events.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

None identified.

### Intentional Quirks (Documented Behavior)

1. **Backstory returns NotFound; GetLore (realm-history) returns empty list**: Unlike the parallel realm-history service which returns OK with empty elements, character-history returns NotFound for missing backstory. Different design decisions in similar services.

2. **Merge by type+key pair**: When `replaceExisting=false`, SetBackstory matches existing elements by `ElementType` + `Key` combination. If a match exists, value and strength are updated. New combinations are appended.

3. **Template-based summarization (not AI)**: Uses switch-case patterns:
   - ORIGIN -> "From {value}"
   - OCCUPATION -> "Worked as {value}"
   - TRAINING -> "Trained by {value}"
   - TRAUMA -> "Experienced {value}"
   - Participation roles mapped to past tense verbs (LEADER -> "led", HERO -> "was a hero of")

4. **No external service dependencies**: Unlike character service, character-history is fully self-contained. It doesn't validate that the character exists before recording history.

5. **Enum string storage**: `EventCategory` and `ParticipationRole` enums stored as strings in internal models. Parsed via `Enum.Parse<T>()` on every read. Enables schema evolution.

6. **UpdatedAt null semantics**: `GetBackstory` returns `UpdatedAt = null` when backstory has never been modified after initial creation (CreatedAtUnix == UpdatedAtUnix).

7. **Significance range [0.0, 1.0]**: Default significance is 0.5 (from schema). Higher values indicate more impactful events. Used for summary prioritization (top N by significance).

### Design Considerations (Requires Planning)

1. **In-memory pagination for all list operations**: Fetches ALL participation records into memory, then filters and paginates. Characters with thousands of participations load entire list. Should implement store-level pagination.

2. **No backstory element count limit**: No maximum on elements per character. Unbounded growth possible if AddBackstoryElement is called repeatedly without cleanup.

3. **Helper abstractions are inline**: `DualIndexHelper` and `BackstoryStorageHelper` are constructed in the service constructor with configuration objects. Not registered in DI. Makes testing require constructor inspection.

4. **GUID parsing without validation**: `MapToHistoricalParticipation` uses `Guid.Parse()` without try-catch on stored string IDs. If data is corrupted, throws `FormatException` at read time.

5. **DeleteAll is O(n) with secondary index cleanup**: Iterates all participation records for a character, extracting event IDs via lambda, and removes from each event index individually. For characters with hundreds of participations, this generates many state store operations.

6. **Metadata stored as `object?`**: Participation metadata accepts any JSON structure. On deserialization from JSON, becomes `JsonElement` or similar untyped object. No schema validation.

7. **Parallel service pattern with realm-history**: Both services use nearly identical patterns (dual-index participations, document-based lore/backstory, template summarization) but with subtle behavioral differences (NotFound vs empty list for missing data).

8. **Empty indices left behind after RemoveRecordAsync**: When removing a single participation, `RemoveRecordAsync` updates both primary and secondary indices but doesn't delete them when they become empty. Only `RemoveAllByPrimaryKeyAsync` (called by DeleteAll) deletes the primary index. This leaves orphaned empty index documents in the state store.

9. **Summarize doesn't publish any event**: Unlike all other operations which publish typed events (recorded, deleted, created, updated), `SummarizeHistoryAsync` generates summaries silently with no event publication. Consuming services have no notification that summarization occurred.

10. **Unknown element types fall back to generic format**: Line 813 in `GenerateBackstorySummary` uses `_ => $"{element.Key}: {element.Value}"` for unrecognized element types. If new backstory element types are added to the schema, they'll produce raw key/value summaries until the switch-case is updated.

11. **Unknown participation roles fall back to "participated in"**: Line 829 in `GenerateParticipationSummary` uses `_ => "participated in"` for unrecognized roles. Generic fallback may not reflect actual involvement.

12. **FormatValue is simplistic transformation**: Lines 835-838: `value.Replace("_", " ").ToLowerInvariant()`. Only handles snake_case, doesn't handle camelCase or PascalCase, doesn't capitalize sentences. "NORTHERN_KINGDOM" becomes "northern kingdom" but "NorthernKingdom" stays as-is.

13. **GetBulkAsync results silently drop missing records**: `DualIndexHelper.GetRecordsByIdsAsync` (line 291) returns `.Values` from bulk get - any IDs in the index that no longer exist in the store are silently excluded from results. No logging, no error, just fewer items than expected.

14. **Empty/null entityId returns silently without logging**: Multiple helper methods (GetAsync, DeleteAsync, ExistsAsync in BackstoryStorageHelper; GetRecordAsync, GetRecordIdsByPrimaryKeyAsync, etc. in DualIndexHelper) check `string.IsNullOrEmpty(entityId)` and return null/false/0 without any logging. Invalid calls produce no trace.

15. **AddBackstoryElement is upsert**: The doc mentions "Adds or updates single element" at the API level, but note that this means AddBackstoryElement with the same type+key silently replaces the existing element's value and strength. No event distinguishes "added new" from "updated existing" when using this endpoint.
