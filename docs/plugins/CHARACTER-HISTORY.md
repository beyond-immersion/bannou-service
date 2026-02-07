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
| lib-resource (`IResourceClient`) | Hard dependency (L1): registers cleanup callbacks and compression callbacks on startup |
| lib-resource (events) | Publishes `resource.reference.registered` and `resource.reference.unregistered` events for character reference tracking |
| lib-character-history (`ICharacterHistoryClient`) | Used by `BackstoryCache` to load backstory data on cache miss |

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
| `BackstoryCacheTtlSeconds` | `CHARACTER_HISTORY_BACKSTORY_CACHE_TTL_SECONDS` | 600 | TTL in seconds for backstory cache entries |

The generated `CharacterHistoryServiceConfiguration` is injected into `BackstoryCache` for TTL configuration.

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
| `IBackstoryCache` | Singleton | TTL-based backstory caching for actor behavior execution |
| `BackstoryProviderFactory` (`IVariableProviderFactory`) | Singleton | Factory for ABML expression evaluation (enables `${backstory.*}` paths) |
| `CharacterHistoryTemplate` (`IResourceTemplate`) | Registered at startup | Compile-time path validation for ABML expressions referencing history data |

Service lifetime is **Scoped** (per-request).

### ABML Expression Support

The plugin registers `BackstoryProviderFactory` as an `IVariableProviderFactory` for the Actor service to discover via DI collection. This enables ABML expressions like:
- `${backstory.origin}` - Returns the value of the ORIGIN element
- `${backstory.fear.strength}` - Returns the strength of the FEAR element
- `${backstory.elements.TRAUMA}` - Returns all TRAUMA elements as a list

The `CharacterHistoryTemplate` provides compile-time validation for `${candidate.history.*}` paths during ABML semantic analysis.

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

### Compression Support

- **GetCompressData** (`/character-history/get-compress-data`): Called by Resource service during hierarchical character compression. Returns `HistoryCompressData` containing:
  - `hasParticipations`: Whether any historical participations exist
  - `participations`: List of `ParticipationResponse` records
  - `hasBackstory`: Whether backstory document exists
  - `backstory`: `BackstoryResponse` if exists
  - `participationCount`: Total participation count

  Returns NotFound only if BOTH participations and backstory are absent.

- **RestoreFromArchive** (`/character-history/restore-from-archive`): Called by Resource service during decompression. Accepts Base64-encoded GZip JSON of `HistoryCompressData`. Restores participations and backstory to state store if they don't already exist (idempotent). Returns counts of restored items.

**Compression Callback Registration** (in OnRunningAsync):
```csharp
await resourceClient.DefineCompressCallbackAsync(
    new DefineCompressCallbackRequest
    {
        ResourceType = "character",
        SourceType = "character-history",
        CompressEndpoint = "/character-history/get-compress-data",
        CompressPayloadTemplate = "{\"characterId\": \"{{resourceId}}\"}",
        DecompressEndpoint = "/character-history/restore-from-archive",
        DecompressPayloadTemplate = "{\"characterId\": \"{{resourceId}}\", \"data\": \"{{data}}\"}",
        Priority = 20  // After personality (priority 10)
    }, ct);
```

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

1. ~~**Hardcoded cache TTL (T21 violation)**~~: **FIXED** (2026-02-06) - Added `BackstoryCacheTtlSeconds` to configuration schema (default 600 seconds). `BackstoryCache` now injects `CharacterHistoryServiceConfiguration` and uses configurable TTL.

### Intentional Quirks

1. **Backstory returns NotFound vs empty list**: Unlike the parallel realm-history service which returns OK with empty elements, character-history returns NotFound for missing backstory. Inconsistent design between similar services.

2. **UpdatedAt null semantics**: `GetBackstory` returns `UpdatedAt = null` when backstory has never been modified after initial creation (CreatedAtUnix == UpdatedAtUnix).

3. **Helper abstractions constructed inline**: `DualIndexHelper` and `BackstoryStorageHelper` are instantiated directly in the constructor rather than registered in DI. This is intentional: helpers require service-specific configuration (key prefixes, data access lambdas) that can't be generalized. Testing works via `IStateStoreFactory` mocking - no special setup required.

4. **Summarize is a read-only operation (no event)**: `SummarizeHistoryAsync` does not publish any event because it's a pure read operation that doesn't modify state. Per FOUNDATION TENETS (Event-Driven Architecture), events notify about state changes - read operations don't trigger events. This is consistent with other read operations like `GetBackstory` and `GetParticipation`.

5. **Summarization switch expressions have unreachable defaults**: `GenerateBackstorySummary` and `GenerateParticipationSummary` both use switch expressions with `_ =>` defaults that are unreachable at runtime. The defaults (`"{element.Key}: {element.Value}"` and `"participated in"`) exist because: (1) C# requires exhaustive switches, and (2) they provide graceful degradation if enum values are added to the schema but the switch isn't updated. This is defensive programming, not a bug.

6. **FormatValue only handles snake_case**: The `FormatValue` helper (`value.Replace("_", " ").ToLowerInvariant()`) only converts snake_case to readable text. This is intentional: the schema documents snake_case as the convention for machine-readable backstory values (e.g., `knights_guild`, `northlands`). Values using camelCase or PascalCase would render incorrectly, but such values don't follow the documented convention.

7. **GetBulkAsync silently drops missing records**: `DualIndexHelper.GetRecordsByIdsAsync` returns only records that exist. If an index contains stale record IDs (records deleted without index cleanup), they are silently excluded from results. This is self-healing behavior - throwing would crash callers for rare edge cases. Callers receive valid data; counts may be slightly fewer than expected if data inconsistency exists.

### Design Considerations (Requires Planning)

1. **In-memory pagination for all list operations**: Fetches ALL participation records into memory, then filters and paginates. Characters with thousands of participations load entire list. Should implement store-level pagination.
<!-- AUDIT:NEEDS_DESIGN:2026-01-31:https://github.com/beyond-immersion/bannou-service/issues/200 -->

2. **No backstory element count limit**: No maximum on elements per character. Unbounded growth possible if AddBackstoryElement is called repeatedly without cleanup.
<!-- AUDIT:NEEDS_DESIGN:2026-01-31:https://github.com/beyond-immersion/bannou-service/issues/207 -->

3. **Metadata stored as `object?`**: Participation metadata accepts any JSON structure. On deserialization from JSON, becomes `JsonElement` or similar untyped object. No schema validation.
<!-- AUDIT:NEEDS_DESIGN:2026-02-06:https://github.com/beyond-immersion/bannou-service/issues/308 -->

4. **Parallel service pattern with realm-history**: Both services use nearly identical patterns (dual-index participations, document-based lore/backstory, template summarization) but with subtle behavioral differences (NotFound vs empty list for missing data).
<!-- AUDIT:NEEDS_DESIGN:2026-02-06:https://github.com/beyond-immersion/bannou-service/issues/309 -->

5. ~~**Empty indices left behind after RemoveRecordAsync**~~: **FIXED** (2026-02-06) - DualIndexHelper.RemoveRecordAsync and RemoveAllByPrimaryKeyAsync now delete index documents when they become empty rather than saving empty lists. Applies to both character-history and realm-history since they share the DualIndexHelper infrastructure.

6. ~~**Unknown element types fall back to generic format**~~: **RECLASSIFIED** (2026-02-06) - Moved to Intentional Quirks. The switch expression handles all 9 defined enum values. The `_ =>` default is defensive programming for compiler exhaustiveness and future-proofing, not a bug.

7. ~~**Unknown participation roles fall back to "participated in"**~~: **RECLASSIFIED** (2026-02-06) - Moved to Intentional Quirks. Same pattern as item 6 - the switch expression handles all 8 defined `ParticipationRole` enum values. The `_ =>` default is defensive programming.

8. ~~**FormatValue is simplistic transformation**~~: **RECLASSIFIED** (2026-02-06) - Moved to Intentional Quirks. The schema documents snake_case as the convention for machine-readable values (e.g., `knights_guild`, `northlands`). The method correctly handles the documented convention; supporting other formats would be scope creep.

9. ~~**GetBulkAsync results silently drop missing records**~~: **RECLASSIFIED** (2026-02-06) - Moved to Intentional Quirks. This is self-healing behavior: if an index contains stale record IDs (records deleted without index cleanup), bulk retrieval silently excludes them rather than throwing. This is the correct design - throwing would crash callers for edge cases.

10. **Empty string entityId returns silently instead of throwing**: Helper methods (DualIndexHelper, BackstoryStorageHelper) check `string.IsNullOrEmpty(entityId)` and return null/empty/false. With NRTs, null is a compile-time warning (redundant check). But empty string `""` is a runtime bug that should throw `ArgumentException`, not silently return null. This hides programmer errors.
<!-- AUDIT:NEEDS_DESIGN:2026-02-06:https://github.com/beyond-immersion/bannou-service/issues/310 -->

11. **AddBackstoryElement is upsert**: The doc mentions "Adds or updates single element" at the API level, but note that this means AddBackstoryElement with the same type+key silently replaces the existing element's value and strength. No event distinguishes "added new" from "updated existing" when using this endpoint.
<!-- AUDIT:NEEDS_DESIGN:2026-02-06:https://github.com/beyond-immersion/bannou-service/issues/311 -->

---

## Work Tracking

### Pending Design Review
- **2026-02-06**: [#311](https://github.com/beyond-immersion/bannou-service/issues/311) - AddBackstoryElement event does not distinguish element added vs updated (need to survey consumers for actual requirement)
- **2026-02-06**: [#310](https://github.com/beyond-immersion/bannou-service/issues/310) - Empty string entityId should throw, not return null silently (part of systemic silent-failure pattern fix)
- **2026-02-06**: [#309](https://github.com/beyond-immersion/bannou-service/issues/309) - Resolve NotFound vs empty-list inconsistency between character-history and realm-history (parallel service API consistency)
- **2026-02-06**: [#308](https://github.com/beyond-immersion/bannou-service/issues/308) - Replace `object?`/`additionalProperties:true` metadata pattern with typed schemas (systemic issue affecting 14+ services; violates T25 type safety)
- **2026-01-31**: [#200](https://github.com/beyond-immersion/bannou-service/issues/200) - Store-level pagination for list operations (in-memory pagination causes memory pressure for characters with many participations)
- **2026-01-31**: [#207](https://github.com/beyond-immersion/bannou-service/issues/207) - Add configurable backstory element count limit (prevent unbounded growth)
- **2026-02-01**: [#230](https://github.com/beyond-immersion/bannou-service/issues/230) - AI-powered summarization (requires building new LLM service infrastructure)
- **2026-02-01**: [#231](https://github.com/beyond-immersion/bannou-service/issues/231) - Cross-character event correlation query (API design decisions needed)

### Completed

- **2026-02-06**: Reclassified "GetBulkAsync silently drops missing records" from Design Considerations to Intentional Quirks. This is correct self-healing behavior - silently excluding stale index entries is better than crashing callers.
- **2026-02-06**: Reclassified "FormatValue is simplistic transformation" from Design Considerations to Intentional Quirks. The method correctly handles snake_case per the documented schema convention; supporting other formats would be scope creep.
- **2026-02-06**: Reclassified "unknown element types fallback" and "unknown participation roles fallback" from Design Considerations to Intentional Quirks. Both switch expressions handle all defined enum values; the `_ =>` defaults are defensive programming for compiler exhaustiveness and future-proofing, not bugs.
- **2026-02-06**: Fixed empty index cleanup in DualIndexHelper - RemoveRecordAsync and RemoveAllByPrimaryKeyAsync now delete index documents when they become empty rather than saving empty lists. Shared fix affects both character-history and realm-history.
- **2026-02-06**: Fixed hardcoded cache TTL (T21 violation) - Added `BackstoryCacheTtlSeconds` configuration property
