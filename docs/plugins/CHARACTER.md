# Character Plugin Deep Dive

> **Plugin**: lib-character
> **Schema**: schemas/character-api.yaml
> **Version**: 1.0.0
> **State Store**: character-statestore (MySQL)

---

## Overview

The Character service manages game world characters for Arcadia. Characters are independent world assets (not owned by accounts) with realm-based partitioning for scalable queries. Provides standard CRUD, enriched retrieval with optional cross-service data (personality, backstory, family tree), and a compression/archival system for dead characters that generates text summaries and tracks reference counts for cleanup eligibility.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | MySQL persistence for character data, archives, and indexes |
| lib-messaging (`IMessageBus`) | Publishing lifecycle and compression events |
| lib-messaging (`IEventConsumer`) | Event handler registration (no current handlers) |
| lib-realm (`IRealmClient`) | Validates realm exists and is active before character creation |
| lib-species (`ISpeciesClient`) | Validates species exists and belongs to the specified realm |
| lib-character-personality (`ICharacterPersonalityClient`) | Fetches personality/combat prefs for enrichment; deletes on compression |
| lib-character-history (`ICharacterHistoryClient`) | Fetches backstory for enrichment; summarizes/deletes on compression |
| lib-relationship (`IRelationshipClient`) | Queries relationships for family tree and cleanup reference counting |
| lib-relationship-type (`IRelationshipTypeClient`) | Maps relationship type IDs to codes for family tree categorization |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-analytics | Character lifecycle tracking for skill ratings |
| lib-species | Species reference updates |
| lib-character-personality | Character info for personality association |
| lib-character-history | Character info for historical event tracking |
| lib-character-encounter | Character data for encounter records and perspectives |

---

## State Storage

**Store**: `character-statestore` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `character:{realmId}:{characterId}` | `CharacterModel` | Full character data (realm-partitioned) |
| `realm-index:{realmId}` | `List<string>` | Character IDs in a realm (Redis for speed) |
| `character-global-index:{characterId}` | `string` | Character ID to realm ID mapping (MySQL for durability) |
| `archive:{characterId}` | `CharacterArchiveModel` | Compressed character text summaries |
| `refcount:{characterId}` | `RefCountData` | Cleanup eligibility tracking (zero-ref timestamp) |

---

## Events

### Published Events

| Topic | Trigger |
|-------|---------|
| `character.created` | New character created |
| `character.updated` | Character metadata modified (includes `changedFields`) |
| `character.deleted` | Character permanently deleted |
| `character.realm.joined` | Character created in or transferred to a realm |
| `character.realm.left` | Character deleted from a realm |
| `character.compressed` | Dead character archived (includes `DeletedSourceData` flag) |

### Consumed Events

This plugin does not consume external events.

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `MaxPageSize` | `CHARACTER_MAX_PAGE_SIZE` | `100` | Maximum page size for list operations |
| `DefaultPageSize` | `CHARACTER_DEFAULT_PAGE_SIZE` | `20` | Default page size when not specified |
| `RealmIndexUpdateMaxRetries` | `CHARACTER_REALM_INDEX_UPDATE_MAX_RETRIES` | `3` | Optimistic concurrency retry limit for realm index |
| `CleanupGracePeriodDays` | `CHARACTER_CLEANUP_GRACE_PERIOD_DAYS` | `30` | Days at zero references before cleanup eligible |

### Unused Configuration Properties

| Property | Env Var | Default | Notes |
|----------|---------|---------|-------|
| `CharacterRetentionDays` | `CHARACTER_RETENTION_DAYS` | `90` | Defined but never referenced in service code |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<CharacterService>` | Scoped | Structured logging |
| `CharacterServiceConfiguration` | Singleton | Pagination and cleanup config |
| `IStateStoreFactory` | Singleton | State store access |
| `IMessageBus` | Scoped | Event publishing |
| `IRealmClient` | Scoped | Realm validation |
| `ISpeciesClient` | Scoped | Species validation |
| `ICharacterPersonalityClient` | Scoped | Enrichment and compression |
| `ICharacterHistoryClient` | Scoped | Enrichment and compression |
| `IRelationshipClient` | Scoped | Family tree and reference counting |
| `IRelationshipTypeClient` | Scoped | Relationship code lookup |
| `IEventConsumer` | Scoped | Event registration (no handlers) |

Service lifetime is **Scoped** (per-request).

---

## API Endpoints (Implementation Notes)

### CRUD Operations (5 endpoints)

- **Create**: Validates realm (must exist AND be active) and species (must exist AND be in specified realm). Fails CLOSED on service unavailability. Generates new GUID. Stores with realm-partitioned key. Maintains both realm index (Redis) and global index (MySQL) with optimistic concurrency retries.
- **Get**: Two-step lookup via global index (characterId -> realmId) then data fetch.
- **Update**: Smart field tracking with changedFields list. Setting DeathDate automatically sets Status to Dead. SpeciesId is mutable (supports species merge migrations).
- **Delete**: Removes from all three storage locations (data, realm index, global index) with optimistic concurrency on index updates.
- **List/ByRealm**: Gets realm index, bulk-fetches all characters, filters in-memory (status, species), then paginates. Clamps page size to MaxPageSize.

### Enriched Character (`/character/get-enriched`)

Opt-in enrichment via boolean flags to avoid unnecessary service calls:

| Flag | Source Service | Data Retrieved |
|------|---------------|----------------|
| `includePersonality` | CharacterPersonality | Trait axes as Dictionary<string, float> |
| `includeCombatPreferences` | CharacterPersonality | Style, range, role, risk/retreat |
| `includeBackstory` | CharacterHistory | BackstoryElementSnapshot list |
| `includeFamilyTree` | Relationship + RelationshipType | Parents, children, siblings, spouse, past lives |

Each enrichment section is independently try-caught. Failures return null for that section (graceful degradation). Main response still returns with available data.

**Family tree categorization** uses string-based type code matching:
- Parents: PARENT, MOTHER, FATHER, STEP_PARENT
- Children: CHILD, SON, DAUGHTER, STEP_CHILD
- Siblings: SIBLING, BROTHER, SISTER, HALF_SIBLING
- Spouse: SPOUSE, HUSBAND, WIFE
- Reincarnation: INCARNATION (tracks past lives)

### Compression (`/character/compress`)

Preconditions: Must be Status=Dead with DeathDate set.

Compression steps:
1. **Personality summary**: Maps trait values to text via threshold logic (>0.3 = "creative", <-0.3 = "traditional")
2. **History summary**: Calls `SummarizeHistoryAsync()` with configurable max backstory/event limits
3. **Family summary**: Text like "married to Elena, parent of 3, reincarnated from 2 past lives"
4. **Archive creation**: Stores text summaries in MySQL under `archive:{characterId}`
5. **Optional source deletion**: If `DeleteSourceData=true`, deletes personality and history data

### Archive Retrieval (`/character/get-archive`)

Simple lookup of compressed archive data by character ID.

### Reference Checking (`/character/check-references`)

Determines cleanup eligibility for compressed characters:
1. Character must be compressed (archive exists)
2. Reference count must be 0 (currently only checks relationships)
3. Must maintain 0 references for grace period (default 30 days)

Tracks `ZeroRefSinceUnix` timestamp in state store for multi-instance safety.

---

## Visual Aid

```
Character Key Architecture (Realm-Partitioned)
================================================

  GET /character/get (by ID only)
       │
       ▼
  character-global-index:{characterId}
       │ returns realmId
       ▼
  character:{realmId}:{characterId}
       │ returns CharacterModel
       ▼
  [CharacterResponse]


  GET /character/by-realm (realm query)
       │
       ▼
  realm-index:{realmId}
       │ returns [id1, id2, id3, ...]
       ▼
  BulkGet: character:{realmId}:{id1}, character:{realmId}:{id2}, ...
       │ returns [CharacterModel, ...]
       ▼
  In-memory filter (status, species)
       │
       ▼
  Paginate (page, pageSize)
       │
       ▼
  [CharacterListResponse]
```

---

## Stubs & Unimplemented Features

1. **Incomplete reference counting**: `CheckCharacterReferences` only checks relationships. Does not check character encounters, history events, contracts, documents, or AI agent references.
2. **Dead `CharacterRetentionDays` config**: Defined but never referenced in service code. (See Tenet Violations #2.)

---

## Potential Extensions

1. **Full reference counting**: Expand to check encounters, history, contracts, and other polymorphic references.
2. **Realm transfer**: Move characters between realms with event publishing and index updates.
3. **Batch compression**: Compress multiple dead characters in one operation.
4. **Typed compression event**: Create `CharacterCompressedEvent` model in events schema.

---

## Known Quirks & Caveats

### Tenet Violations (Fix Immediately)

1. [FOUNDATION] **Missing null checks on constructor dependencies (T6)** -- File: `/home/lysander/repos/bannou/plugins/lib-character/CharacterService.cs`, lines 67-76. The constructor assigns dependencies directly without null-guarding any of them (`_stateStoreFactory = stateStoreFactory;`, `_messageBus = messageBus;`, etc.). Per T6, all injected dependencies should use `?? throw new ArgumentNullException(nameof(param))` pattern. All 10 dependencies (stateStoreFactory, messageBus, logger, configuration, realmClient, speciesClient, personalityClient, historyClient, relationshipClient, relationshipTypeClient) are missing guards.

2. [IMPLEMENTATION] **Dead configuration property: CharacterRetentionDays (T21)** -- File: `/home/lysander/repos/bannou/plugins/lib-character/Generated/CharacterServiceConfiguration.cs`, line 72. The property `CharacterRetentionDays` (env: `CHARACTER_RETENTION_DAYS`, default 90) is defined in the configuration schema but never referenced anywhere in `CharacterService.cs`. Per T21, every configuration property must be referenced in service code. Fix: either implement the feature that uses it or remove it from the schema (`schemas/character-configuration.yaml`).

3. [IMPLEMENTATION] **Hardcoded tunables: MaxBackstoryPoints and MaxLifeEvents (T21)** -- File: `/home/lysander/repos/bannou/plugins/lib-character/CharacterService.cs`, lines 627-628. The values `MaxBackstoryPoints = 5` and `MaxLifeEvents = 10` are hardcoded magic numbers. Per T21, any tunable value (limits, thresholds) must be defined in the configuration schema. Fix: add `CompressionMaxBackstoryPoints` and `CompressionMaxLifeEvents` properties to `schemas/character-configuration.yaml` and reference them via `_configuration`.

4. [IMPLEMENTATION] **Hardcoded personality threshold: 0.3f (T21)** -- File: `/home/lysander/repos/bannou/plugins/lib-character/CharacterService.cs`, lines 1044-1051. The threshold `0.3f` for personality trait classification is a hardcoded tunable used 16 times. Per T21, this should be a configuration property (e.g., `PersonalityTraitThreshold`). Fix: add to configuration schema and use `_configuration.PersonalityTraitThreshold`.

5. [QUALITY] **Operation entry logging at Information instead of Debug (T10)** -- File: `/home/lysander/repos/bannou/plugins/lib-character/CharacterService.cs`, lines 90, 180, 216, 303, 362-366, 401, 441, 579, 723, 763. All operation entry log statements (e.g., "Creating character", "Getting character", "Updating character", "Deleting character", "Compressing character", "Getting archive", "Checking references") use `LogInformation` instead of `LogDebug`. Per T10, operation entry with input parameters should be at Debug level. Information level is for significant state changes (which the "Character created/updated/deleted" confirmations already correctly use).

6. [IMPLEMENTATION] **No distributed lock on character update (T9)** -- File: `/home/lysander/repos/bannou/plugins/lib-character/CharacterService.cs`, lines 210-295. `UpdateCharacterAsync` reads a character, modifies it, and saves it without any concurrency protection (no ETag-based optimistic concurrency, no distributed lock). Two simultaneous updates to the same character will both succeed with last-writer-wins semantics. Per T9, services must use atomic state operations or distributed locks for consistency. Fix: use `GetWithETagAsync` and `TrySaveAsync` with retry-on-conflict, or use `IDistributedLockProvider`.

7. [IMPLEMENTATION] **No distributed lock on character compression (T9)** -- File: `/home/lysander/repos/bannou/plugins/lib-character/CharacterService.cs`, lines 570-712. `CompressCharacterAsync` reads a character, generates summaries, stores an archive, and optionally deletes source data without any concurrency protection. Two concurrent compressions of the same character could both proceed, creating duplicate archives or race conditions on source deletion. Fix: use `IDistributedLockProvider` around the compression operation.

8. [IMPLEMENTATION] **Hardcoded string for "balanced personality" fallback (T21)** -- File: `/home/lysander/repos/bannou/plugins/lib-character/CharacterService.cs`, line 1060. The string `"balanced personality"` is a hardcoded literal returned when all traits are in the neutral zone. This should either be a configuration property or the method should return null to indicate no distinguishing traits.

9. [QUALITY] **Missing XML documentation on multiple public/private methods (T19)** -- File: `/home/lysander/repos/bannou/plugins/lib-character/CharacterService.cs`. The following public methods lack `<param>` and `<returns>` XML documentation tags (they have no XML docs at all): `CreateCharacterAsync` (line 84), `GetCharacterAsync` (line 174), `UpdateCharacterAsync` (line 210), `DeleteCharacterAsync` (line 297), `ListCharactersAsync` (line 353), `GetCharactersByRealmAsync` (line 392), `CheckCharacterReferencesAsync` (line 757), `RegisterServicePermissionsAsync` (line 1511). Per T19, all public methods must have `<summary>`, `<param>`, and `<returns>` tags.

10. [IMPLEMENTATION] **Status field uses .ToString() populating event model (T25 boundary exception, but inconsistent)** -- File: `/home/lysander/repos/bannou/plugins/lib-character/CharacterService.cs`, lines 1434, 1458. `Status = character.Status.ToString()` populates the lifecycle event Status field. While the generated event model `CharacterUpdatedEvent.Status` and `CharacterDeletedEvent.Status` are typed as `string?`, this is acceptable as an event boundary conversion per T25. However, it should be noted that this relies on the generated event schema using strings rather than the enum type.

11. [IMPLEMENTATION] **EventId uses Guid.NewGuid().ToString() for non-lifecycle events (type mismatch concern)** -- File: `/home/lysander/repos/bannou/plugins/lib-character/CharacterService.cs`, lines 1475, 1493. `PublishCharacterRealmJoinedEventAsync` and `PublishCharacterRealmLeftEventAsync` set `EventId = Guid.NewGuid().ToString()`. The generated `CharacterRealmJoinedEvent` and `CharacterRealmLeftEvent` models have `EventId` as `string` type (not `Guid`), so this is technically correct per the generated schema. However, this is inconsistent with the lifecycle events and `CharacterCompressedEvent` which use `Guid EventId`. The schema should be updated to use `format: uuid` for EventId on these events for consistency.

12. [FOUNDATION] **Missing IEventConsumer null check before RegisterEventConsumers (T6)** -- File: `/home/lysander/repos/bannou/plugins/lib-character/CharacterService.cs`, line 79. The code calls `((IBannouService)this).RegisterEventConsumers(eventConsumer)` without first calling `ArgumentNullException.ThrowIfNull(eventConsumer, nameof(eventConsumer))` as specified in the T6 pattern. Fix: add `ArgumentNullException.ThrowIfNull(eventConsumer, nameof(eventConsumer));` before the RegisterEventConsumers call.

13. [IMPLEMENTATION] **No IDistributedLockProvider injected for multi-instance safety (T9)** -- File: `/home/lysander/repos/bannou/plugins/lib-character/CharacterService.cs`. The service does not inject `IDistributedLockProvider` despite having multiple operations that need cross-instance coordination (update, compress, reference checking). Per T9 and T6, services needing distributed locks must inject and use `IDistributedLockProvider`.

14. [IMPLEMENTATION] **RefCount update without optimistic concurrency (T9)** -- File: `/home/lysander/repos/bannou/plugins/lib-character/CharacterService.cs`, lines 814-824. The `CheckCharacterReferencesAsync` method reads `RefCountData`, modifies it, and saves it back without ETag-based concurrency control. Two concurrent reference checks could race: both read `ZeroRefSinceUnix = null`, both set it to now, and the second write silently wins. Fix: use `GetWithETagAsync` and `TrySaveAsync` with retry.

15. [IMPLEMENTATION] **Hardcoded "RELATIONSHIP" string for reference types (T25)** -- File: `/home/lysander/repos/bannou/plugins/lib-character/CharacterService.cs`, line 800. The string `"RELATIONSHIP"` is used as a reference type category. If there is a generated enum for reference types, it should be used instead. If not, this is a magic string that should at minimum be a `const`.

16. [IMPLEMENTATION] **Missing ApiException catch in outer try-catch blocks (T7)** -- File: `/home/lysander/repos/bannou/plugins/lib-character/CharacterService.cs`, lines 158, 194, 281, 337, 376, 413, 554, 698, 739, 847. All endpoint methods use only `catch (Exception ex)` at the top level, without a separate `catch (ApiException ex)` handler. Per T7, the standard pattern requires distinguishing `ApiException` (expected API errors - log as warning, propagate status) from general `Exception` (unexpected - log as error, emit error event). Currently, an `ApiException` from a dependency call that escapes the inner try-catch will be logged as an Error and trigger an error event, when it should be logged as a Warning with its status code propagated.

17. [QUALITY] **Missing XML documentation on helper methods (T19)** -- File: `/home/lysander/repos/bannou/plugins/lib-character/CharacterService.cs`. The following methods lack XML docs: `FindCharacterByIdAsync` (line 1199), `GetCharactersByRealmInternalAsync` (line 1218), `AddCharacterToRealmIndexAsync` (line 1291), `RemoveCharacterFromRealmIndexAsync` (line 1339), `MapToCharacterResponse` (line 1380), `MapToArchiveModel` (line 1093), `MapFromArchiveModel` (line 1111), `BuildCharacterKey` (line 1133), `BuildRealmIndexKey` (line 1136). Per T19, even private/internal methods benefit from summary documentation for maintainability.

18. [IMPLEMENTATION] **Silent swallowing of non-404 ApiException during compression deletion (T7)** -- File: `/home/lysander/repos/bannou/plugins/lib-character/CharacterService.cs`, lines 675, 683. The catch blocks `catch (ApiException) { /* Ignore if not found */ }` swallow ALL ApiException statuses, not just 404. A 500 from the downstream service would be silently ignored. Fix: use `catch (ApiException ex) when (ex.StatusCode == 404)` to only swallow not-found cases, and log/emit for other status codes.

### Intentional Quirks (Documented Behavior)

1. **Realm-partitioned keys**: Character data keys include realmId (`character:{realmId}:{characterId}`). Enables efficient "list by realm" queries without full table scans. Requires global index for ID-only lookups.

2. **Dual-index maintenance with optimistic concurrency**: Realm index updates use ETag-based optimistic locking with configurable retries (default 3). Designed for low-contention scenarios (character creation is infrequent per realm).

3. **Fail-CLOSED on realm/species validation**: If realm or species service is unavailable during character creation, the operation fails with an exception rather than proceeding without validation.

4. **Enrichment graceful degradation**: Each enrichment section (personality, backstory, family tree) is independently caught. A failure in one doesn't prevent others from returning. Missing enrichment sections are null in the response.

5. **DeathDate auto-sets Status**: Setting `DeathDate` in an update automatically changes `Status` to `Dead`. The inverse is not true (setting Status=Dead doesn't set DeathDate).

6. **Silent deletion on compression**: When `DeleteSourceData=true`, exceptions from personality/history deletion are caught and ignored. Archive is created even if source data deletion fails.

7. **Family tree type codes as strings**: Relationship type categorization uses string equality (`typeCode == "PARENT"`) rather than enum comparison. Enables extensibility but loses compile-time safety.

### Design Considerations (Requires Planning)

1. **In-memory filtering before pagination**: List operations load all characters in a realm, filter in-memory, then paginate. For realms with thousands of characters, this loads everything into memory before applying page limits.

2. **Global index double-write**: Both Redis (realm index) and MySQL (global index) are updated on create/delete. Extra write for resilience across restarts but adds complexity.

3. **Reference counting only checks relationships**: `CheckCharacterReferences` queries only the Relationship service. Characters referenced by encounters, history events, contracts, or AI agents are not counted, potentially allowing premature cleanup.

4. **No optimistic concurrency on character update**: Two simultaneous updates to the same character will both succeed. Last-writer-wins with no version checking or conflict detection.

5. **Compression personality summary threshold**: The threshold logic for personality summaries (>0.3 = positive label, <-0.3 = negative label) is hardcoded. Values between -0.3 and +0.3 produce no summary text for that trait.

6. **Family tree type lookups are sequential**: `BuildFamilyTreeAsync` (lines 893-910) looks up each unique relationship type ID one at a time via API call. Not parallelized. For N relationship types, N sequential network calls.

7. **Family tree silently skips unknown relationship types**: Line 926-929 - if a relationship type ID can't be looked up, the relationship is silently excluded from the family tree with no indication in the response.

8. **INCARNATION tracking is directional**: Line 1013 only tracks past lives when the character is Entity2 in the INCARNATION relationship. If the character is Entity1 (the "reincarnator"), past lives are not included.

9. **Multiple spouses = last one wins**: Line 1001 uses simple assignment `familyTree.Spouse =`. If a character has multiple spouse relationships, only the last one processed appears in the response.

10. **"orphaned" label ignores parent death status**: Line 1082-1083 adds "orphaned" when `Parents.Count == 0`, regardless of whether parents existed but died. A character whose parents died is not labeled orphaned.

11. **"single parent household" is literal**: Line 1084-1085 adds this label when exactly one parent exists. Doesn't consider whether two parents were expected. A character intentionally created with one parent still gets this label.

12. **Family tree character lookups are sequential**: Line 932 calls `FindCharacterByIdAsync` for each related character during family tree building. A family of 10 means 10+ sequential database lookups.

13. **"balanced personality" fallback**: Lines 1059-1060 - if ALL traits are in neutral zone (-0.3 to +0.3), returns literal string "balanced personality" rather than null or empty.
