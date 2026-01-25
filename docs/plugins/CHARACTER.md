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

> **Refactoring Consideration**: This plugin injects 6 service clients individually. Consider whether `IServiceNavigator` would reduce constructor complexity, trading explicit dependencies for cleaner signatures. Currently favoring explicit injection for dependency clarity.

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
| `CompressionMaxBackstoryPoints` | `CHARACTER_COMPRESSION_MAX_BACKSTORY_POINTS` | `5` | Max backstory points in compression summary |
| `CompressionMaxLifeEvents` | `CHARACTER_COMPRESSION_MAX_LIFE_EVENTS` | `10` | Max life events in compression summary |
| `PersonalityTraitThreshold` | `CHARACTER_PERSONALITY_TRAIT_THRESHOLD` | `0.3` | Threshold for trait classification (>threshold = positive, <-threshold = negative) |
| `CharacterRetentionDays` | `CHARACTER_RETENTION_DAYS` | `90` | Days to retain deleted characters (stub for unimplemented purge feature) |

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
2. **`CharacterRetentionDays` config placeholder**: Defined for future retention/purge feature but not yet referenced in service code.

---

## Potential Extensions

1. **Full reference counting**: Expand to check encounters, history, contracts, and other polymorphic references.
2. **Realm transfer**: Move characters between realms with event publishing and index updates.
3. **Batch compression**: Compress multiple dead characters in one operation.
4. **Typed compression event**: Create `CharacterCompressedEvent` model in events schema.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

No bugs identified.

### Intentional Quirks (Documented Behavior)

1. **Realm-partitioned keys**: Character data keys include realmId (`character:{realmId}:{characterId}`). Enables efficient "list by realm" queries without full table scans. Requires global index for ID-only lookups.

2. **Dual-index maintenance with optimistic concurrency**: Realm index updates use ETag-based optimistic locking with configurable retries (default 3). Designed for low-contention scenarios (character creation is infrequent per realm).

3. **Fail-CLOSED on realm/species validation**: If realm or species service is unavailable during character creation, the operation fails with an exception rather than proceeding without validation.

4. **Enrichment graceful degradation**: Each enrichment section (personality, backstory, family tree) is independently caught. A failure in one doesn't prevent others from returning. Missing enrichment sections are null in the response.

5. **DeathDate auto-sets Status**: Setting `DeathDate` in an update automatically changes `Status` to `Dead`. The inverse is not true (setting Status=Dead doesn't set DeathDate).

6. **Silent deletion on compression**: When `DeleteSourceData=true`, exceptions from personality/history deletion are caught and ignored. Archive is created even if source data deletion fails.

7. **Family tree type codes as strings**: Relationship type categorization uses string equality (`typeCode == "PARENT"`) rather than enum comparison. Enables extensibility but loses compile-time safety.

8. **Status field uses .ToString() in event models**: `Status = character.Status.ToString()` is acceptable as an event boundary conversion per T25.

9. **"balanced personality" fallback is a display string**: The string `"balanced personality"` in `GeneratePersonalitySummary` is a display string, not a tunable threshold.

10. **CharacterRetentionDays is stub config**: Defined but not referenced - acceptable scaffolding for unimplemented retention/purge feature.

### Design Considerations (Requires Planning)

1. **No distributed lock on character update (T9)**: `UpdateCharacterAsync` reads a character, modifies it, and saves it without concurrency protection. Two simultaneous updates result in last-writer-wins. Fix requires: inject `IDistributedLockProvider`, add locking around read-modify-write, or use `GetWithETagAsync`/`TrySaveAsync` with retry-on-conflict.

2. **No distributed lock on character compression (T9)**: `CompressCharacterAsync` reads a character, generates summaries, stores archive, and optionally deletes source data without concurrency protection. Fix requires: inject `IDistributedLockProvider`, wrap compression in lock scope.

3. **In-memory filtering before pagination**: List operations load all characters in a realm, filter in-memory, then paginate. For realms with thousands of characters, this loads everything into memory before applying page limits.

4. **Global index double-write**: Both Redis (realm index) and MySQL (global index) are updated on create/delete. Extra write for resilience across restarts but adds complexity.

5. **Reference counting only checks relationships**: `CheckCharacterReferences` queries only the Relationship service. Characters referenced by encounters, history events, contracts, or AI agents are not counted, potentially allowing premature cleanup.

6. **Family tree type lookups are sequential**: `BuildFamilyTreeAsync` looks up each unique relationship type ID one at a time via API call. Not parallelized. For N relationship types, N sequential network calls.

7. **Family tree silently skips unknown relationship types**: If a relationship type ID can't be looked up, the relationship is silently excluded from the family tree with no indication in the response.

8. **INCARNATION tracking is directional**: Only tracks past lives when the character is Entity2 in the INCARNATION relationship. If the character is Entity1 (the "reincarnator"), past lives are not included.

9. **Multiple spouses = last one wins**: Uses simple assignment for spouse. If a character has multiple spouse relationships, only the last one processed appears in the response.

10. **"orphaned" label ignores parent death status**: Adds "orphaned" when `Parents.Count == 0`, regardless of whether parents existed but died.

11. **"single parent household" is literal**: Adds this label when exactly one parent exists. Doesn't consider whether two parents were expected.

12. **Family tree character lookups are sequential**: Calls `FindCharacterByIdAsync` for each related character during family tree building. A family of 10 means 10+ sequential database lookups.
