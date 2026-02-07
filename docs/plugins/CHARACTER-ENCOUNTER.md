# Character Encounter Plugin Deep Dive

> **Plugin**: lib-character-encounter
> **Schema**: schemas/character-encounter-api.yaml
> **Version**: 1.0.0
> **State Stores**: character-encounter-statestore (MySQL)

---

## Overview

Character encounter tracking service for memorable interactions between characters. Manages the lifecycle of encounters (shared interaction records) and perspectives (individual participant views), enabling dialogue triggers ("We've met before..."), grudges/alliances ("You killed my brother!"), quest hooks ("The merchant you saved has a job"), and NPC memory. Implements a multi-participant design where each encounter has one shared record with N perspectives (one per participant), scaling linearly O(N) for group events. Features time-based memory decay (configurable lazy-on-access or scheduled background modes), weighted sentiment aggregation across encounter histories, configurable encounter type codes (6 built-in + custom), automatic encounter pruning per-character and per-pair limits, and ETag-based optimistic concurrency for perspective updates. All state is maintained via manual index management (character, pair, location, global, custom-type) since the state store does not support prefix queries.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | MySQL persistence for encounters, perspectives, and all indexes |
| lib-messaging (`IMessageBus`) | Publishing encounter lifecycle events; error event publishing |
| lib-messaging (`IEventConsumer`) | Consuming `character.deleted` events for cleanup |
| lib-character (`ICharacterClient`) | Validating participant character IDs exist on RecordEncounter |
| lib-resource (`IResourceClient`) | Registering cleanup and compression callbacks on startup |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-actor (`IEncounterCache`, `EncountersProvider`) | Uses `ICharacterEncounterClient` for encounter queries, sentiment lookups, has-met checks, and between-character queries; provides `${encounters.*}` ABML variable paths for NPC behavior decisions |

---

## State Storage

**Stores**: 1 state store (MySQL-backed, multiple data types distinguished by key prefix)

| Store | Backend | Purpose |
|-------|---------|---------|
| `character-encounter-statestore` | MySQL | All encounter data, perspectives, and indexes |

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `enc-{encounterId}` | `EncounterData` | Shared encounter record (type, outcome, participants, realm, location, metadata) |
| `pers-{perspectiveId}` | `PerspectiveData` | Individual character perspective (emotional impact, sentiment shift, memory strength) |
| `type-{CODE}` | `EncounterTypeData` | Encounter type definitions (built-in and custom) |
| `char-idx-{characterId}` | `CharacterIndexData` | List of perspective IDs for a character |
| `pair-idx-{charA}:{charB}` | `PairIndexData` | List of encounter IDs between a character pair (sorted GUIDs) |
| `loc-idx-{locationId}` | `LocationIndexData` | List of encounter IDs at a location |
| `global-char-idx` | `GlobalCharacterIndexData` | All character IDs with encounter data (for bulk decay) |
| `custom-type-idx` | `CustomTypeIndexData` | All custom encounter type codes (for enumeration) |
| `type-enc-idx-{CODE}` | `TypeEncounterIndexData` | List of encounter IDs using this type code (for type-in-use validation) |
| `enc-pers-idx-{encounterId}` | `EncounterPerspectiveIndexData` | List of perspective IDs for an encounter (enables O(1) perspective lookup) |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `encounter.recorded` | `EncounterRecordedEvent` | New encounter recorded between characters |
| `encounter.memory.faded` | `EncounterMemoryFadedEvent` | Memory decays below fade threshold (0.1) |
| `encounter.memory.refreshed` | `EncounterMemoryRefreshedEvent` | Memory strength boosted via refresh |
| `encounter.perspective.updated` | `EncounterPerspectiveUpdatedEvent` | Perspective emotional impact or sentiment changed |
| `encounter.deleted` | `EncounterDeletedEvent` | Encounter and perspectives permanently deleted |

### Consumed Events

| Topic | Event Type | Handler |
|-------|-----------|---------|
| `character.deleted` | `CharacterDeletedEvent` | `OnCharacterDeletedAsync` - deletes all encounters and perspectives involving the deleted character |

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `ServerSalt` | `CHARACTER_ENCOUNTER_SERVER_SALT` | `bannou-dev-encounter-salt-change-in-production` | Shared server salt for GUID generation |
| `MemoryDecayEnabled` | `CHARACTER_ENCOUNTER_MEMORY_DECAY_ENABLED` | `true` | Enable/disable memory decay system |
| `MemoryDecayMode` | `CHARACTER_ENCOUNTER_MEMORY_DECAY_MODE` | `lazy` | Decay mode: 'lazy' (on access) or 'scheduled' |
| `ScheduledDecayCheckIntervalMinutes` | `CHARACTER_ENCOUNTER_SCHEDULED_DECAY_CHECK_INTERVAL_MINUTES` | `60` | Minutes between scheduled decay checks (only when mode is 'scheduled') |
| `ScheduledDecayStartupDelaySeconds` | `CHARACTER_ENCOUNTER_SCHEDULED_DECAY_STARTUP_DELAY_SECONDS` | `30` | Startup delay before first scheduled decay check |
| `MemoryDecayIntervalHours` | `CHARACTER_ENCOUNTER_MEMORY_DECAY_INTERVAL_HOURS` | `24` | Hours per decay interval |
| `MemoryDecayRate` | `CHARACTER_ENCOUNTER_MEMORY_DECAY_RATE` | `0.05` | Strength reduction per interval (0.0-1.0) |
| `MemoryFadeThreshold` | `CHARACTER_ENCOUNTER_MEMORY_FADE_THRESHOLD` | `0.1` | Strength below which memories are forgotten |
| `MaxEncountersPerCharacter` | `CHARACTER_ENCOUNTER_MAX_PER_CHARACTER` | `1000` | Max encounters per character before pruning |
| `MaxEncountersPerPair` | `CHARACTER_ENCOUNTER_MAX_PER_PAIR` | `100` | Max encounters per pair before pruning |
| `DefaultPageSize` | `CHARACTER_ENCOUNTER_DEFAULT_PAGE_SIZE` | `20` | Default query page size |
| `MaxPageSize` | `CHARACTER_ENCOUNTER_MAX_PAGE_SIZE` | `100` | Maximum allowed page size |
| `MaxBatchSize` | `CHARACTER_ENCOUNTER_MAX_BATCH_SIZE` | `100` | Maximum items in batch operations |
| `DefaultMemoryStrength` | `CHARACTER_ENCOUNTER_DEFAULT_MEMORY_STRENGTH` | `1.0` | Initial memory strength for new perspectives |
| `MemoryRefreshBoost` | `CHARACTER_ENCOUNTER_MEMORY_REFRESH_BOOST` | `0.2` | Strength boost on memory refresh |
| `SentimentShiftPositive` | `CHARACTER_ENCOUNTER_SENTIMENT_SHIFT_POSITIVE` | `0.2` | Default shift for POSITIVE outcome |
| `SentimentShiftNegative` | `CHARACTER_ENCOUNTER_SENTIMENT_SHIFT_NEGATIVE` | `-0.2` | Default shift for NEGATIVE outcome |
| `SentimentShiftMemorable` | `CHARACTER_ENCOUNTER_SENTIMENT_SHIFT_MEMORABLE` | `0.1` | Default shift for MEMORABLE outcome |
| `SentimentShiftTransformative` | `CHARACTER_ENCOUNTER_SENTIMENT_SHIFT_TRANSFORMATIVE` | `0.3` | Default shift for TRANSFORMATIVE outcome |
| `SeedBuiltInTypesOnStartup` | `CHARACTER_ENCOUNTER_SEED_BUILTIN_TYPES_ON_STARTUP` | `true` | Auto-seed built-in types on startup |
| `DuplicateTimestampToleranceMinutes` | `CHARACTER_ENCOUNTER_DUPLICATE_TIMESTAMP_TOLERANCE_MINUTES` | `5` | Time window in minutes for duplicate encounter detection |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<CharacterEncounterService>` | Scoped | Structured logging |
| `CharacterEncounterServiceConfiguration` | Singleton | All 21 config properties |
| `IStateStoreFactory` | Singleton | MySQL state store access for all data types |
| `IMessageBus` | Scoped | Event publishing and error events |
| `IEventConsumer` | Scoped | Event subscription registration |
| `ICharacterClient` | Scoped | Cross-service character validation |
| `IResourceClient` | Scoped | Cleanup/compression callback registration on startup |
| `IEncounterDataCache` | Singleton | In-memory cache for encounter queries (5-minute TTL), used by Actor's EncountersProvider |
| `EncountersProviderFactory` | Singleton | `IVariableProviderFactory` implementation for Actor's `${encounters.*}` ABML variables |
| `MemoryDecaySchedulerService` | Hosted | Background service for scheduled memory decay (only active when MemoryDecayMode is 'scheduled') |

Service lifetime is **Scoped** (per-request). One background service: `MemoryDecaySchedulerService` (conditionally active). Three singleton helpers: configuration, encounter cache, and variable provider factory. No distributed locks used.

---

## API Endpoints (Implementation Notes)

### Encounter Type Management (6 endpoints)

- **CreateEncounterType** (`/character-encounter/type/create`): Validates code is not a reserved built-in type (COMBAT, DIALOGUE, TRADE, QUEST, SOCIAL, CEREMONY). Uppercases code. Checks for existing type with same code (returns Conflict). Creates `EncounterTypeData` with new GUID. Adds to custom type index for enumeration. No event published.
- **GetEncounterType** (`/character-encounter/type/get`): Looks up by uppercased code. If not found, checks if it is a built-in type and auto-seeds it on demand (lazy seeding pattern). Returns NotFound for truly unknown codes.
- **ListEncounterTypes** (`/character-encounter/type/list`): Ensures built-in types are seeded first. Builds key list from built-in codes plus custom type index. Loads each type individually. Applies filters: `includeInactive`, `builtInOnly`, `customOnly`. Sorts by sortOrder then code.
- **UpdateEncounterType** (`/character-encounter/type/update`): Partial update semantics (null fields unchanged). Built-in types can only have description and defaultEmotionalImpact updated; rejects name or sortOrder changes. No event published.
- **DeleteEncounterType** (`/character-encounter/type/delete`): Rejects deletion of built-in types (returns BadRequest). Validates type is not in use via `type-enc-idx-{CODE}` lookup (returns 409 Conflict if encounters exist). Performs soft-delete by marking `IsActive = false`. Removes from custom type index.
- **SeedEncounterTypes** (`/character-encounter/type/seed`): Idempotent operation. Iterates built-in types. Creates if missing. If `forceReset=true`, overwrites existing built-in types with default values. Reports created/updated/skipped counts.

### Recording (1 endpoint)

- **RecordEncounter** (`/character-encounter/record`): Validates minimum 2 participants. Validates encounter type exists (auto-seeds built-in types). Validates type is active. Deduplicates participant IDs. **Checks for duplicate encounters** (same participants, type, and timestamp within `DuplicateTimestampToleranceMinutes` - returns 409 Conflict). **Validates all participant characters exist** via `ICharacterClient` (returns 404 if any not found). Creates encounter record. Adds to `type-enc-idx-{CODE}` for type-in-use tracking. Creates perspective per participant (uses provided perspectives or generates defaults from outcome). Default emotional impact derived from encounter type or outcome. Default sentiment shift derived from outcome enum. Updates character index, pair indexes (O(N^2) pairs), and location index. Enforces `MaxEncountersPerCharacter` and `MaxEncountersPerPair` by pruning oldest. Publishes `encounter.recorded` event.

### Queries (6 endpoints)

- **QueryByCharacter** (`/character-encounter/query/by-character`): Loads character's perspective IDs from index. For each perspective: applies lazy decay, filters by `minimumMemoryStrength`, loads encounter, applies type/outcome/timestamp filters. Loads all perspectives for matching encounters. Sorts by timestamp descending. Paginates with config-aware page size clamping.
- **QueryBetween** (`/character-encounter/query/between`): Loads pair index for the two characters (using sorted GUID pair key). Loads each encounter. Applies type filter. Memory strength filter checks if EITHER character has a perspective above threshold. Sorts descending, paginates.
- **QueryByLocation** (`/character-encounter/query/by-location`): Loads location index. Loads each encounter. Applies type filter and fromTimestamp filter (no toTimestamp). Loads perspectives for each. Sorts descending, paginates.
- **HasMet** (`/character-encounter/has-met`): Fast boolean check via pair index only. Returns encounter count. Does NOT apply memory decay or load encounter details. O(1) index lookup.
- **GetSentiment** (`/character-encounter/get-sentiment`): Loads pair encounters. For each: finds the querying character's perspective, weights sentiment shift by memory strength. Calculates weighted average sentiment (clamped to [-1, +1]). Tracks emotion frequency counts. Returns dominant emotion (most frequent) and aggregate sentiment. Returns 0 sentiment with 0 encounter count if no encounters exist.
- **BatchGetSentiment** (`/character-encounter/batch-get`): Validates batch size against `MaxBatchSize`. Iterates target IDs calling `GetSentimentAsync` for each. No parallel execution. Returns list of sentiment responses.

### Perspectives (3 endpoints)

- **GetPerspective** (`/character-encounter/get-perspective`): Finds perspective by encounter+character (scans character index). Applies lazy decay before returning. Returns NotFound if perspective does not exist.
- **UpdatePerspective** (`/character-encounter/update-perspective`): Finds perspective, re-loads with ETag for optimistic concurrency. Applies partial updates (emotionalImpact, sentimentShift, rememberedAs). Uses `TrySaveAsync` with ETag; returns Conflict on concurrent modification. Publishes `encounter.perspective.updated` with previous and new values.
- **RefreshMemory** (`/character-encounter/refresh-memory`): Finds perspective, re-loads with ETag. Adds boost amount (request-provided or config default) to memory strength. Clamps to [0, 1]. Uses ETag concurrency. Publishes `encounter.memory.refreshed`. Counteracts memory decay.

### Admin (3 endpoints)

- **DeleteEncounter** (`/character-encounter/delete`): Hard delete. Deletes all perspectives for the encounter. Deletes encounter record. Cleans up pair indexes and location index. Publishes `encounter.deleted` with `deletedByCharacterCleanup = false`.
- **DeleteByCharacter** (`/character-encounter/delete-by-character`): Loads all perspective IDs for the character. Deletes each perspective. For each unique encounter: deletes remaining perspectives, deletes encounter, cleans up pair and location indexes. Publishes `encounter.deleted` per encounter with `deletedByCharacterCleanup = true`. Reports total encounters and perspectives deleted.
- **DecayMemories** (`/character-encounter/decay-memories`): Respects `MemoryDecayEnabled` flag. Targets specific character or all characters (via global index). Loads each perspective with ETag. Calculates decay amount based on elapsed intervals. Supports `dryRun` mode. Uses ETag concurrency (skips on conflict). Publishes `encounter.memory.faded` for perspectives crossing the fade threshold.

### Compression Support (2 endpoints)

Centralized compression via the Resource service (L1). CharacterEncounter registers compression callbacks at startup, enabling the Resource service to gather encounter and perspective data during character archival.

- **GetCompressData** (`/character-encounter/get-compress-data`): Returns all encounters and perspectives for a character, bundled for archival. Uses `DualIndexHelper` to load perspectives via the character index (`char-idx-{characterId}`), then loads corresponding encounter records. Returns `EncounterCompressData` containing the character ID, counts, encounters list, and perspectives list. Returns 404 if no data exists (empty index). GZip-compressed with Base64 encoding during archival by the Resource service.

- **RestoreFromArchive** (`/character-encounter/restore-from-archive`): Accepts Base64-encoded GZip-compressed JSON (`EncounterCompressData`). Decompresses and deserializes using `BannouJson`. Restores encounters by saving to `enc-{encounterId}` keys. Restores perspectives to `pers-{perspectiveId}` keys and rebuilds character index (`char-idx-{characterId}`). Returns success status with counts of restored encounters and perspectives.

**Callback Registration** (in plugin startup):
```csharp
// In CharacterEncounterServicePlugin.OnRunningAsync (priority 30 - after personality and history):
var resourceClient = scope.ServiceProvider.GetRequiredService<IResourceClient>();
await resourceClient.DefineCompressCallbackAsync(
    new DefineCompressCallbackRequest
    {
        ResourceType = "character",
        SourceType = "character-encounter",
        ServiceName = "character-encounter",
        CompressEndpoint = "/character-encounter/get-compress-data",
        CompressPayloadTemplate = "{\"characterId\": \"{{resourceId}}\"}",
        DecompressEndpoint = "/character-encounter/restore-from-archive",
        DecompressPayloadTemplate = "{\"characterId\": \"{{resourceId}}\", \"data\": \"{{data}}\"}",
        Priority = 30,
        Description = "Encounters and perspectives between characters"
    },
    ct);
```

---

## Visual Aid

```
Memory Decay System
=====================

  Lazy Decay (on access):
    QueryByCharacter / GetPerspective / GetEncounterPerspectives
         |
         v
    ApplyLazyDecayAsync(perspective)
         |
         +--> MemoryDecayEnabled && mode == "lazy"?
         |         |
         |    CalculateDecay(perspective)
         |         |
         |         +--> lastDecayed = LastDecayedAtUnix ?? CreatedAtUnix
         |         |    hoursSince = (now - lastDecayed).TotalHours
         |         |    intervals = hoursSince / MemoryDecayIntervalHours (24h)
         |         |
         |         +--> intervals < 1? --> no decay needed
         |         |
         |         +--> decayAmount = intervals * MemoryDecayRate (0.05)
         |              newStrength = MemoryStrength - decayAmount
         |              willFade = crossed below FadeThreshold (0.1)?
         |
         +--> Update perspective (MemoryStrength, LastDecayedAtUnix)
         +--> If faded: publish encounter.memory.faded event
         +--> Return updated perspective

  Example Timeline (MemoryDecayRate=0.05, Interval=24h, Threshold=0.1):
    Day  0: Strength = 1.00  (new encounter)
    Day  1: Strength = 0.95  (1 interval elapsed)
    Day  5: Strength = 0.75  (5 intervals)
    Day 10: Strength = 0.50  (10 intervals)
    Day 15: Strength = 0.25  (15 intervals)
    Day 18: Strength = 0.10  (18 intervals - FADE THRESHOLD)
    Day 19: Strength = 0.05  (19 intervals - memory.faded event!)


Sentiment Aggregation
======================

  GetSentimentAsync(characterId, targetCharacterId)
       |
       +--> Get pair encounter IDs from pair index
       |
       +--> For each encounter:
       |         |
       |         +--> Find querying character's perspective
       |         |
       |         +--> weight = perspective.MemoryStrength (0.0-1.0)
       |         |    shift = perspective.SentimentShift (-1.0 to +1.0)
       |         |
       |         +--> totalSentiment += shift * weight
       |         |    totalWeight += weight
       |         |
       |         +--> Track emotionCounts[EmotionalImpact]++
       |
       +--> aggregateSentiment = totalSentiment / totalWeight
       |    (weighted average, clamped to [-1.0, +1.0])
       |
       +--> dominantEmotion = emotion with highest count

  Example:
    Encounter 1: COMBAT, shift=-0.5, strength=0.8
    Encounter 2: TRADE,  shift=+0.3, strength=1.0
    Encounter 3: COMBAT, shift=-0.3, strength=0.2

    totalSentiment = (-0.5*0.8) + (0.3*1.0) + (-0.3*0.2) = -0.16
    totalWeight    = 0.8 + 1.0 + 0.2 = 2.0
    aggregate      = -0.16 / 2.0 = -0.08
    dominant       = ANGER (COMBAT: 2 occurrences)


Perspective System (Multi-Participant)
========================================

  RecordEncounter(participantIds=[A, B, C], type=COMBAT, outcome=NEGATIVE)
       |
       +--> Create 1 EncounterData record
       |         encounterId, timestamp, realmId, locationId
       |         encounterTypeCode, outcome, participantIds
       |
       +--> Create N PerspectiveData records (one per participant):
       |         +--> Perspective A: ANGER, shift=-0.2, strength=1.0
       |         +--> Perspective B: FEAR,  shift=-0.1, strength=1.0
       |         +--> Perspective C: PRIDE, shift=+0.1, strength=1.0
       |
       +--> Update indexes:
       |         char-idx-A: [perspA]
       |         char-idx-B: [perspB]
       |         char-idx-C: [perspC]
       |         pair-idx-A:B: [encounterId]   (3 pairs for 3 participants)
       |         pair-idx-A:C: [encounterId]
       |         pair-idx-B:C: [encounterId]
       |         loc-idx-LOC: [encounterId]
       |
       +--> Prune if limits exceeded


Encounter Lifecycle & Pruning
===============================

  Recording:
    Record --> Validate --> Create Encounter --> Create Perspectives
         --> Update Indexes --> Prune (per-char & per-pair)

  Pruning (MaxEncountersPerCharacter=1000):
    perspectiveIds.Count > MaxEncountersPerCharacter?
         |
         +--> Load all perspectives with encounter timestamps
         +--> Sort by timestamp ascending (oldest first)
         +--> Remove (count - max) oldest:
                 +--> Delete perspective
                 +--> Remove from character index
                 +--> If encounter has no remaining perspectives:
                        +--> Delete encounter
                        +--> Clean pair indexes
                        +--> Clean location index

  Deletion flows:
    DeleteEncounter:       1 encounter + N perspectives + indexes
    DeleteByCharacter:     All encounters involving character + all their perspectives
    character.deleted:     Triggers DeleteByCharacter via event consumer


Index Architecture
====================

  +-----------------------+      +--------------------+     +----------------------+
  | global-char-idx       |      | custom-type-idx    |     | type-enc-idx-{CODE}  |
  | [charA, charB, ...]   |      | [HEALING, ...]     |     | [enc1, enc2, ...]    |
  +-----------+-----------+      +--------------------+     +----------------------+
              |                                                        |
   +----------+----------+                                             |
   |                     |                                             |
   v                     v                                             |
  +------------------+  +------------------+                           |
  | char-idx-charA   |  | char-idx-charB   |                           |
  | [pA1, pA2, ...]  |  | [pB1, pB2, ...]  |                           |
  +--------+---------+  +------------------+                           |
           |                                                           |
           v                                                           |
  +-------------------+     +------------------+     +------------------+
  | pers-pA1          |     | enc-{id}         |     | pair-idx-A:B     |
  | encounterId       |---->| participants     |<----| [enc1, enc2,...] |
  | emotionalImpact   |     | type, outcome    |     +------------------+
  | memoryStrength    |     | realm, location  |<----+
  | sentimentShift    |     +--------+---------+     +------------------+
  +-------------------+              |               | loc-idx-{locId}  |
           ^                         |               | [enc1, enc2,...] |
           |                         v               +------------------+
           |              +------------------------+
           +--------------| enc-pers-idx-{encId}   |
                          | [pA1, pB1, pC1, ...]   |
                          +------------------------+
                          Enables O(1) lookup of all
                          perspectives for an encounter
```

---

## Stubs & Unimplemented Features

No stubs or unimplemented features remain.

---

## Potential Extensions

1. **Encounter aggregation**: Pre-compute and cache sentiment values per character pair, updating on encounter record/delete/perspective-update to avoid O(N) computation on every GetSentiment call.
<!-- AUDIT:NEEDS_DESIGN:2026-02-06:https://github.com/beyond-immersion/bannou-service/issues/312 -->
2. **Location-based encounter proximity**: Integrate with location hierarchy to find encounters "near" a location (ancestor/descendant queries).
<!-- AUDIT:NEEDS_DESIGN:2026-02-06:https://github.com/beyond-immersion/bannou-service/issues/313 -->
3. **Memory decay curves**: Support non-linear decay (exponential, logarithmic) via configurable decay function, allowing traumatic encounters to persist longer than casual ones.
<!-- AUDIT:NEEDS_DESIGN:2026-02-07:https://github.com/beyond-immersion/bannou-service/issues/314 -->
4. **Encounter archival**: Instead of hard-deleting pruned encounters, move them to a compressed archive format for historical queries.
<!-- AUDIT:NEEDS_DESIGN:2026-02-07:https://github.com/beyond-immersion/bannou-service/issues/315 -->

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

No bugs identified.

### Intentional Quirks

1. **Lazy seeding of built-in types**: Built-in encounter types are seeded on-demand when accessed (via GetEncounterType or RecordEncounter), not at service startup. This means the first access to a built-in type may have slightly higher latency.

2. **Sentiment returns 0 for unknown pairs**: If two characters have never met, `GetSentiment` returns a response with sentiment=0, encounterCount=0, and null dominantEmotion. This is not a NotFound - it is a valid "neutral" response.

3. **HasMet does NOT apply memory decay**: The `HasMet` endpoint is a fast index-only check. It reports true even if all memories of the encounters have faded below threshold. This is intentional: "have they met" is a factual record, not a memory.

4. **Lazy decay has write side effects**: When loading perspectives for queries (QueryByCharacter, QueryBetween, QueryByLocation), lazy decay is applied to each perspective. This means read-only queries have write side effects (updating MemoryStrength and LastDecayedAtUnix in the store).

5. **Index updates use 3-attempt ETag retry loops**: All index update methods (character index, pair index, location index, custom type index) use `GetWithETagAsync` + `TrySaveAsync` with a 3-attempt retry pattern. Concurrent modifications are detected via ETag mismatch and retried. After 3 failures, a warning is logged but the operation continues (best-effort index consistency).

### Design Considerations (Requires Planning)

1. ~~**N+1 query pattern everywhere**~~: **FIXED** (2026-02-07) - Added `enc-pers-idx-{encounterId}` index for O(1) encounter-to-perspectives lookup, converted all queries to use `GetBulkAsync`, parallelized lazy decay writes with `Task.WhenAll`. See Issue #319.

2. **No pagination for GetAllPerspectiveIdsAsync**: The `DecayMemories` endpoint with no characterId specified loads ALL perspective IDs from ALL characters via the global index. With many characters and encounters, this could exhaust memory. The code comments acknowledge this: "in production, this would need batching."

3. **Pair index combinatorial explosion**: For a group encounter with N participants, N*(N-1)/2 pair indexes are created/updated. A 10-participant encounter creates 45 pair index entries. This is documented as O(N^2) in the schema.

4. **No transactionality**: Recording an encounter involves multiple sequential writes (encounter, perspectives, character indexes, pair indexes, location index). If the service crashes mid-operation, indexes can become inconsistent with actual data.

5. **Lazy decay write amplification**: Every read of a perspective older than one decay interval triggers a write-back. High-read workloads on stale data will generate significant write traffic. The `encounter.memory.faded` event is also published on read paths.

6. ~~**BatchGetSentiment is sequential**~~: **FIXED** (2026-02-07) - Parallelized with `Task.WhenAll` for concurrent sentiment calculations. See Issue #319.

7. **Global character index unbounded growth**: The `global-char-idx` list grows without bound as new characters encounter each other. Even after `DeleteByCharacter` removes a character, the global index is cleaned up, but during active operation this list could contain tens of thousands of character IDs.

8. ~~**FindPerspective scans character index**~~: **FIXED** (2026-02-07) - Added `enc-pers-idx-{encounterId}` index for direct O(1) lookup of perspectives by encounter ID. Legacy fallback path retained for pre-existing encounters. See Issue #319.

---

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above. Items here are managed by the `/audit-plugin` workflow and should not be manually edited except to add new tracking markers.

### Completed

- **2026-02-07**: Issue #319 - N+1 query optimization. Added `enc-pers-idx-{encounterId}` index, converted all queries to use `GetBulkAsync`, parallelized lazy decay with `Task.WhenAll`, parallelized `BatchGetSentiment`.
