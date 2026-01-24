# Character Encounter Plugin Deep Dive

> **Plugin**: lib-character-encounter
> **Schema**: schemas/character-encounter-api.yaml
> **Version**: 1.0.0
> **State Stores**: character-encounter-statestore (MySQL)

---

## Overview

Character encounter tracking service for memorable interactions between characters. Manages the lifecycle of encounters (shared interaction records) and perspectives (individual participant views), enabling dialogue triggers ("We've met before..."), grudges/alliances ("You killed my brother!"), quest hooks ("The merchant you saved has a job"), and NPC memory. Implements a multi-participant design where each encounter has one shared record with N perspectives (one per participant), scaling linearly O(N) for group events. Features time-based memory decay applied lazily on access (not via background jobs), weighted sentiment aggregation across encounter histories, configurable encounter type codes (6 built-in + custom), automatic encounter pruning per-character and per-pair limits, and ETag-based optimistic concurrency for perspective updates. All state is maintained via manual index management (character, pair, location, global, custom-type) since the state store does not support prefix queries.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | MySQL persistence for encounters, perspectives, and all indexes |
| lib-messaging (`IMessageBus`) | Publishing encounter lifecycle events; error event publishing |
| lib-messaging (`IEventConsumer`) | Consuming `character.deleted` events for cleanup |

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

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<CharacterEncounterService>` | Scoped | Structured logging |
| `CharacterEncounterServiceConfiguration` | Singleton | All 18 config properties |
| `IStateStoreFactory` | Singleton | MySQL state store access for all data types |
| `IMessageBus` | Scoped | Event publishing and error events |
| `IEventConsumer` | Scoped | Event subscription registration |

Service lifetime is **Scoped** (per-request). No background services. No distributed locks used.

---

## API Endpoints (Implementation Notes)

### Encounter Type Management (6 endpoints)

- **CreateEncounterType** (`/character-encounter/type/create`): Validates code is not a reserved built-in type (COMBAT, DIALOGUE, TRADE, QUEST, SOCIAL, CEREMONY). Uppercases code. Checks for existing type with same code (returns Conflict). Creates `EncounterTypeData` with new GUID. Adds to custom type index for enumeration. No event published.
- **GetEncounterType** (`/character-encounter/type/get`): Looks up by uppercased code. If not found, checks if it is a built-in type and auto-seeds it on demand (lazy seeding pattern). Returns NotFound for truly unknown codes.
- **ListEncounterTypes** (`/character-encounter/type/list`): Ensures built-in types are seeded first. Builds key list from built-in codes plus custom type index. Loads each type individually. Applies filters: `includeInactive`, `builtInOnly`, `customOnly`. Sorts by sortOrder then code.
- **UpdateEncounterType** (`/character-encounter/type/update`): Partial update semantics (null fields unchanged). Built-in types can only have description and defaultEmotionalImpact updated; rejects name or sortOrder changes. No event published.
- **DeleteEncounterType** (`/character-encounter/type/delete`): Rejects deletion of built-in types (returns BadRequest). Performs soft-delete by marking `IsActive = false`. Removes from custom type index. Does NOT validate whether encounters reference this type.
- **SeedEncounterTypes** (`/character-encounter/type/seed`): Idempotent operation. Iterates built-in types. Creates if missing. If `forceReset=true`, overwrites existing built-in types with default values. Reports created/updated/skipped counts.

### Recording (1 endpoint)

- **RecordEncounter** (`/character-encounter/record`): Validates minimum 2 participants. Validates encounter type exists (auto-seeds built-in types). Validates type is active. Deduplicates participant IDs. Creates encounter record. Creates perspective per participant (uses provided perspectives or generates defaults from outcome). Default emotional impact derived from encounter type or outcome. Default sentiment shift derived from outcome enum. Updates character index, pair indexes (O(N^2) pairs), and location index. Enforces `MaxEncountersPerCharacter` and `MaxEncountersPerPair` by pruning oldest. Publishes `encounter.recorded` event.

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

  +-----------------------+      +--------------------+
  | global-char-idx       |      | custom-type-idx    |
  | [charA, charB, ...]   |      | [HEALING, ...]     |
  +-----------+-----------+      +--------------------+
              |
   +----------+----------+
   |                     |
   v                     v
  +------------------+  +------------------+
  | char-idx-charA   |  | char-idx-charB   |
  | [pA1, pA2, ...]  |  | [pB1, pB2, ...]  |
  +--------+---------+  +------------------+
           |
           v
  +-------------------+     +------------------+     +------------------+
  | pers-pA1          |     | enc-{id}         |     | pair-idx-A:B     |
  | encounterId       |---->| participants     |<----| [enc1, enc2,...] |
  | emotionalImpact   |     | type, outcome    |     +------------------+
  | memoryStrength    |     | realm, location  |
  | sentimentShift    |     +------------------+     +------------------+
  +-------------------+                              | loc-idx-{locId}  |
                                                     | [enc1, enc2,...] |
                                                     +------------------+
```

---

## Stubs & Unimplemented Features

1. **Scheduled decay mode**: The `MemoryDecayMode` configuration accepts "scheduled" but only "lazy" mode is implemented. No background service exists to periodically process decay. The `DecayMemories` admin endpoint partially fills this gap but must be called externally.
2. **Duplicate encounter detection**: The API schema documents a 409 response for "Duplicate encounter (same participants, timestamp, and type)" but the implementation does not check for duplicates. Recording the same encounter twice creates two separate records.
3. **Delete type with encounters validation**: The schema documents a 409 response for "Cannot delete - type is in use by encounters" but the implementation does not validate whether any encounters reference the type before soft-deleting it.
4. **Character existence validation**: The schema documents a 404 response for "One or more characters not found" on RecordEncounter, but the implementation does not call any character service to validate participant IDs exist.

---

## Potential Extensions

1. **Background decay worker**: Implement a hosted service that periodically calls `DecayMemories` for all characters, enabling the "scheduled" mode path and reducing lazy decay latency on first access.
2. **Encounter deduplication**: Add duplicate detection based on (participantIds, timestamp, typeCode) tuple to prevent recording the same event multiple times.
3. **Encounter aggregation**: Pre-compute and cache sentiment values per character pair, updating on encounter record/delete/perspective-update to avoid O(N) computation on every GetSentiment call.
4. **Location-based encounter proximity**: Integrate with location hierarchy to find encounters "near" a location (ancestor/descendant queries).
5. **Memory decay curves**: Support non-linear decay (exponential, logarithmic) via configurable decay function, allowing traumatic encounters to persist longer than casual ones.
6. **Encounter archival**: Instead of hard-deleting pruned encounters, move them to a compressed archive format for historical queries.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

1. **DeleteByCharacter double-counts perspectives**: The method first deletes the target character's perspectives (incrementing `perspectivesDeleted`), then calls `DeleteEncounterPerspectivesAsync` for each encounter which attempts to delete ALL perspectives including the already-deleted ones. The already-deleted perspectives return null from the store so they are skipped, but the reported `perspectivesDeleted` count may be inaccurate because it sums both passes.

2. **~~DecayMemories double-decay and incorrect PreviousStrength~~** *(FIXED)*: In `DecayMemoriesAsync`, `GetDecayAmount` was called multiple times with mutations in between. The event's `PreviousStrength` was reconstructed after `LastDecayedAtUnix` was already updated (making `GetDecayAmount` return 0). Fixed by computing `decayAmount` and `previousStrength` once before any mutation.

3. **Lazy decay has no ETag concurrency control**: The `ApplyLazyDecayAsync` method (lines 1998-2030) uses `store.SaveAsync` without ETag protection, unlike `DecayMemoriesAsync` which uses `GetWithETagAsync` + `TrySaveAsync`. Two concurrent reads of the same stale perspective could both calculate and apply decay, resulting in double-decay (perspective strength reduced twice). This is particularly problematic because lazy decay happens during read operations (`QueryByCharacter`, `QueryBetween`, `QueryByLocation`, `GetPerspective`, `GetSentiment`).

4. **~~ApplyLazyDecayAsync calculates decay twice~~** *(NOT A BUG)*: While `CalculateDecay` internally calls `GetDecayAmount` and then `GetDecayAmount` is called again directly, the code correctly captures `decayAmount` and `previousStrength` before mutating. The redundant call is a minor inefficiency but not a correctness issue. Reclassified as intentional (acceptable overhead for code clarity).

5. **EncounterDeletedEvent assumes one perspective per participant**: At line 1350, `PerspectivesDeleted = encounter.ParticipantIds.Count` assumes every participant has exactly one perspective. If data inconsistency exists (orphaned perspectives, missing perspectives), the event reports the wrong count.

### Intentional Quirks (Documented Behavior)

1. **Pair key sorting**: Pair indexes use the lexicographically smaller GUID first (`charA < charB ? A:B : B:A`) to ensure both directions of a relationship map to the same index key.

2. **Lazy seeding of built-in types**: Built-in encounter types are seeded on-demand when accessed (via GetEncounterType or RecordEncounter), not at service startup. `EnsureBuiltInTypesSeededAsync` is only called by `ListEncounterTypes`. This means the first access to a built-in type may have slightly higher latency.

3. **Soft-delete for custom types only**: `DeleteEncounterType` performs a soft-delete (marks inactive) rather than hard-delete. Built-in types cannot be deleted at all. Encounters referencing inactive types remain valid.

4. **ETag concurrency on perspective updates**: Both `UpdatePerspective` and `RefreshMemory` use `GetWithETagAsync` + `TrySaveAsync` for optimistic concurrency. On conflict, they return 409 Conflict rather than retrying.

5. **Sentiment returns 0 for unknown pairs**: If two characters have never met, `GetSentiment` returns a response with sentiment=0, encounterCount=0, and null dominantEmotion. This is not a NotFound - it is a valid "neutral" response.

6. **HasMet does NOT apply memory decay**: The `HasMet` endpoint is a fast index-only check. It reports true even if all memories of the encounters have faded below threshold. This is intentional: "have they met" is a factual record, not a memory.

7. **Lazy decay during GetEncounterPerspectives**: When loading all perspectives for an encounter (used by QueryByCharacter, QueryBetween, QueryByLocation), lazy decay is applied to each perspective. This means a read-only query has write side effects (updating MemoryStrength and LastDecayedAtUnix in the store).

8. **Pruning is per-recording not per-query**: Encounter pruning happens immediately after recording a new encounter, not lazily on query. If limits are reduced via configuration, existing over-limit data persists until the next recording for that character/pair.

### Design Considerations (Requires Planning)

1. **N+1 query pattern everywhere**: All query operations (QueryByCharacter, QueryBetween, QueryByLocation) load perspectives and encounters individually by key. A character with 1000 encounters generates thousands of state store calls per query. The pair index and character index approach mitigates this for lookups but not for loading.

2. **No pagination for GetAllPerspectiveIdsAsync**: The `DecayMemories` endpoint with no characterId specified loads ALL perspective IDs from ALL characters via the global index. With many characters and encounters, this could exhaust memory. The code comments acknowledge this: "in production, this would need batching."

3. **Pair index combinatorial explosion**: For a group encounter with N participants, N*(N-1)/2 pair indexes are created/updated. A 10-participant encounter creates 45 pair index entries. This is documented as O(N^2) in the schema.

4. **No transactionality**: Recording an encounter involves multiple sequential writes (encounter, perspectives, character indexes, pair indexes, location index). If the service crashes mid-operation, indexes can become inconsistent with actual data.

5. **Lazy decay write amplification**: Every read of a perspective older than one decay interval triggers a write-back. High-read workloads on stale data will generate significant write traffic. The `encounter.memory.faded` event is also published on read paths.

6. **BatchGetSentiment is sequential**: The batch endpoint calls `GetSentimentAsync` in a loop. Each call does its own pair index lookup and perspective scanning. No parallelism or batching optimization. With MaxBatchSize=100 targets and many encounters per pair, this could be very slow.

7. **Global character index unbounded growth**: The `global-char-idx` list grows without bound as new characters encounter each other. Even after `DeleteByCharacter` removes a character, the global index is cleaned up, but during active operation this list could contain tens of thousands of character IDs.

8. **FindPerspective scans character index**: Finding a specific perspective by (encounterId, characterId) requires loading the character's entire perspective index and then loading each perspective until finding one matching the encounter. There is no direct encounter-to-perspective index.

9. **Index operations are not atomic**: All index update methods (`AddToCharacterIndexAsync`, `AddToGlobalCharacterIndexAsync`, `AddToCustomTypeIndexAsync`, `AddToLocationIndexAsync`, `UpdatePairIndexesAsync`, etc.) use read-modify-write without ETags. Two concurrent encounter recordings for the same character could both read the same index state, both add their perspective ID, and save—the second save overwrites the first, losing an entry. This is mitigated by the scoped service lifetime (requests are typically serialized per character) but could occur under high concurrency.

10. **GetSentiment loads encounters redundantly**: In `GetSentimentAsync` (line 900-901), the encounter is loaded to... do nothing with it. The perspective is then found via `GetEncounterPerspectivesAsync` which loads the encounter AGAIN. The encounter load at line 900 is vestigial code that wastes a state store call per encounter.
