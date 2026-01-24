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

## Tenet Violations (Fix Immediately)

1. **[IMPLEMENTATION TENETS - T25 Internal Model Type Safety]** `ParticipationData` internal POCO uses `string` for fields that have proper C# types. `ParticipationId`, `CharacterId`, and `EventId` should be `Guid`; `EventCategory` should be `EventCategory` enum; `Role` should be `ParticipationRole` enum.
   - **File**: `plugins/lib-character-history/CharacterHistoryService.cs`, lines 877-889
   - **What's wrong**: Internal model stores GUIDs and enums as strings, requiring `Guid.Parse()` and `Enum.Parse<T>()` on every read (lines 761-766) and `.ToString()` on every write (lines 124-129).
   - **Fix**: Change `ParticipationId`, `CharacterId`, `EventId` to `Guid` type. Change `EventCategory` to `EventCategory` enum type. Change `Role` to `ParticipationRole` enum type. Remove all `Guid.Parse`/`Enum.Parse` from `MapToHistoricalParticipation` and all `.ToString()` from `RecordParticipationAsync`.

2. **[IMPLEMENTATION TENETS - T25 Internal Model Type Safety]** `BackstoryElementData` internal POCO uses `string` for `ElementType` field that has a proper `BackstoryElementType` C# enum.
   - **File**: `plugins/lib-character-history/CharacterHistoryService.cs`, lines 905-913
   - **What's wrong**: `ElementType` stored as `string`, requiring `Enum.Parse<BackstoryElementType>` on every read (line 778) and `.ToString()` on every write (line 791). Also means `GenerateBackstorySummary` (line 800-814) pattern-matches on raw strings ("ORIGIN", "TRAUMA") instead of using enum values.
   - **Fix**: Change `ElementType` to `BackstoryElementType` enum type. Update `GenerateBackstorySummary` switch to use enum cases (`BackstoryElementType.Origin =>` etc.). Remove `Enum.Parse` from `MapToBackstoryElement` and `.ToString()` from `MapToBackstoryElementData`.

3. **[IMPLEMENTATION TENETS - T25 Internal Model Type Safety]** `BackstoryData` internal POCO uses `string` for `CharacterId` which should be `Guid`.
   - **File**: `plugins/lib-character-history/CharacterHistoryService.cs`, line 896
   - **What's wrong**: `CharacterId` stored as `string` in the backstory model, requiring `.ToString()` conversions when setting and `Guid.Parse` implications in upstream code.
   - **Fix**: Change `CharacterId` to `Guid` type.

4. **[IMPLEMENTATION TENETS - T25 Internal Model Type Safety]** `GenerateParticipationSummary` uses string comparison for enum values.
   - **File**: `plugins/lib-character-history/CharacterHistoryService.cs`, lines 819-830
   - **What's wrong**: Pattern matches on raw strings ("LEADER", "COMBATANT", "VICTIM", etc.) instead of using `ParticipationRole` enum values. This is fragile and bypasses compile-time safety.
   - **Fix**: Once `ParticipationData.Role` is typed as `ParticipationRole` enum (per violation #1), update the switch to use `ParticipationRole.Leader =>` etc.

5. **[IMPLEMENTATION TENETS - T7 Error Handling]** All catch blocks catch only `Exception` without distinguishing `ApiException` from unexpected exceptions.
   - **File**: `plugins/lib-character-history/CharacterHistoryService.cs`, lines 160, 226, 290, 344, 406, 478, 549, 594, 653, 736
   - **What's wrong**: T7 requires catching `ApiException` first (for expected API errors from downstream services) and then `Exception` (for unexpected failures). All methods use a single `catch (Exception ex)` block.
   - **Fix**: Add `catch (ApiException ex)` before `catch (Exception ex)` in each try-catch. Log ApiException as `LogWarning` and propagate its status code. Reserve `LogError` and `TryPublishErrorAsync` for the unexpected `Exception` catch.

6. **[FOUNDATION TENETS - T6 Service Implementation Pattern]** Constructor does not perform null-argument validation on injected dependencies.
   - **File**: `plugins/lib-character-history/CharacterHistoryService.cs`, lines 53-56
   - **What's wrong**: The T6 pattern shows `_messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus))` for all constructor parameters. This service assigns directly without null checks: `_messageBus = messageBus;`, `_logger = logger;`, etc.
   - **Fix**: Add `?? throw new ArgumentNullException(nameof(paramName))` for `messageBus`, `stateStoreFactory`, `logger`, `configuration`, and `ArgumentNullException.ThrowIfNull(eventConsumer)`.

7. **[IMPLEMENTATION TENETS - T21 Configuration-First / No Dead Configuration]** `_configuration` field is assigned but never referenced in any method.
   - **File**: `plugins/lib-character-history/CharacterHistoryService.cs`, lines 26, 56
   - **What's wrong**: The `CharacterHistoryServiceConfiguration` is injected and stored, but never used anywhere in the service. T21 states "Every defined config property MUST be referenced in service code" and unused config means dead config.
   - **Fix**: Either remove `_configuration` from the constructor and field declaration (since the configuration class only has the framework `ForceServiceId` property which is handled by the framework), or if the field is needed for future use, document why.

8. **[IMPLEMENTATION TENETS - T21 Configuration-First / No Dead Configuration]** `_stateStoreFactory` field is assigned but never referenced after construction.
   - **File**: `plugins/lib-character-history/CharacterHistoryService.cs`, lines 24, 54
   - **What's wrong**: The `IStateStoreFactory` is stored as a field, but the constructor passes the `stateStoreFactory` parameter directly to the helper constructors. The `_stateStoreFactory` field is never read by any method.
   - **Fix**: Remove the `_stateStoreFactory` field. Pass `stateStoreFactory` directly to helper constructors (which is already done) without storing it.

9. **[QUALITY TENETS - T19 XML Documentation]** Internal data model properties lack XML documentation.
   - **File**: `plugins/lib-character-history/CharacterHistoryService.cs`, lines 877-913
   - **What's wrong**: All properties on `ParticipationData`, `BackstoryData`, and `BackstoryElementData` classes lack `<summary>` XML documentation. While these are `internal` classes, T19 says "all public classes, interfaces, methods, and properties" - these classes have public properties.
   - **Fix**: Add `/// <summary>` documentation to each property on the three internal data model classes.

10. **[QUALITY TENETS - T19 XML Documentation]** `MapToHistoricalParticipation`, `MapToBackstoryElement`, `MapToBackstoryElementData`, `GenerateBackstorySummary`, `GenerateParticipationSummary`, and `FormatValue` methods lack XML `<param>` and `<returns>` tags.
    - **File**: `plugins/lib-character-history/CharacterHistoryService.cs`, lines 757, 774, 787, 800, 817, 835
    - **What's wrong**: These private methods have no XML documentation at all. While private methods are less critical, the public `RegisterServicePermissionsAsync` method (line 848) also lacks `<param>` and `<returns>` tags.
    - **Fix**: Add `<param>` tag for `appId` parameter on `RegisterServicePermissionsAsync`.

11. **[QUALITY TENETS - Duplicate Assembly Attributes]** `InternalsVisibleTo` attributes declared in both `CharacterHistoryService.cs` and `AssemblyInfo.cs`.
    - **Files**: `plugins/lib-character-history/CharacterHistoryService.cs` lines 10-11; `plugins/lib-character-history/AssemblyInfo.cs` lines 5-6
    - **What's wrong**: `[assembly: InternalsVisibleTo("lib-character-history.tests")]` and `[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]` are declared in both files. This causes potential CS0579 duplicate attribute warnings.
    - **Fix**: Remove the `[assembly: InternalsVisibleTo(...)]` declarations from `CharacterHistoryService.cs` (lines 10-11) and the `using System.Runtime.CompilerServices;` import (line 8). Keep them only in `AssemblyInfo.cs`.

12. **[IMPLEMENTATION TENETS - T25 Internal Model Type Safety]** `Guid.Parse()` used in business logic for GUID fields that should be typed.
    - **File**: `plugins/lib-character-history/CharacterHistoryService.cs`, lines 337-338, 761-763, 782
    - **What's wrong**: `Guid.Parse(data.CharacterId)`, `Guid.Parse(data.EventId)`, `Guid.Parse(data.ParticipationId)` are called in business logic mapping methods. T25 states "Enum parsing belongs only at system boundaries" - the same applies to GUID parsing. If the POCO fields were `Guid` type (per violation #1), these parse calls would not be needed.
    - **Fix**: This is resolved by fixing violation #1 (changing POCO fields to `Guid` type).

13. **[IMPLEMENTATION TENETS - T25 Internal Model Type Safety]** `.ToString()` used to populate internal model fields with enum and GUID values.
    - **File**: `plugins/lib-character-history/CharacterHistoryService.cs`, lines 124-129, 152, 791
    - **What's wrong**: `body.EventCategory.ToString()`, `body.Role.ToString()`, `participationId.ToString()`, `body.CharacterId.ToString()`, `body.EventId.ToString()`, and `element.ElementType.ToString()` are used to populate internal POCO string fields. T25 explicitly forbids "`.ToString()` populating internal model."
    - **Fix**: This is resolved by fixing violations #1 and #2 (changing POCO fields to proper types and assigning directly).

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
