# Character Plugin Deep Dive

> **Plugin**: lib-character
> **Schema**: schemas/character-api.yaml
> **Version**: 1.0.0
> **State Store**: character-statestore (MySQL)

---

## Overview

The Character service manages game world characters for Arcadia. Characters are independent world assets (not owned by accounts) with realm-based partitioning for scalable queries. Provides standard CRUD, enriched retrieval with family tree data (from Relationship service, L2), and a compression/archival system for dead characters that generates text summaries and tracks reference counts for cleanup eligibility. Per SERVICE_HIERARCHY, Character (L2) cannot depend on L4 services like CharacterPersonality or CharacterHistory - callers needing that data should aggregate from L4 services directly.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | MySQL persistence for character data, archives, indexes, and refcount tracking |
| lib-state (`IDistributedLockProvider`) | Distributed locks for character update and compression operations |
| lib-messaging (`IMessageBus`) | Publishing lifecycle and compression events |
| lib-messaging (`IEventConsumer`) | Event handler registration (no current handlers) |
| lib-realm (`IRealmClient`) | Validates realm exists and is active before character creation |
| lib-species (`ISpeciesClient`) | Validates species exists and belongs to the specified realm |
| lib-relationship (`IRelationshipClient`) | Queries relationships for family tree and cleanup reference counting |
| lib-relationship-type (`IRelationshipTypeClient`) | Maps relationship type IDs to codes for family tree categorization |
| lib-contract (`IContractClient`) | Queries contracts where character is a party (L1 - allowed) |
| lib-resource (`IResourceClient`) | Queries L4 references (Actor, Encounter) via event-driven pattern (L1 - allowed) |

> **Refactoring Consideration**: This plugin injects 9 service clients individually. Consider whether `IServiceNavigator` would reduce constructor complexity, trading explicit dependencies for cleaner signatures. Currently favoring explicit injection for dependency clarity.

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-analytics | Subscribes to `character.updated` for cache invalidation; calls `ICharacterClient` for realm resolution |
| lib-character-encounter | Registers `x-references` cleanup callback (`/character-encounter/delete-by-character`); calls `ICharacterClient` for character name enrichment |
| lib-character-history | Registers `x-references` cleanup callback (`/character-history/delete-all`); cleanup invoked via lib-resource when character deleted |
| lib-character-personality | Registers `x-references` cleanup callback (`/character-personality/cleanup-by-character`); cleanup invoked via lib-resource when character deleted |
| lib-actor | Registers `x-references` cleanup callback (`/actor/cleanup-by-character`); cleanup invoked via lib-resource when character deleted |
| lib-species | Calls `ICharacterClient` to check character references during species deprecation |

---

## State Storage

**Store**: `character-statestore` (Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `character:{realmId}:{characterId}` | `CharacterModel` | Full character data (realm-partitioned) |
| `realm-index:{realmId}` | `List<string>` | Character IDs in a realm (for list queries) |
| `character-global-index:{characterId}` | `string` | Character ID to realm ID mapping (for ID-only lookups) |
| `archive:{characterId}` | `CharacterArchiveModel` | Compressed character text summaries |
| `refcount:{characterId}` | `RefCountData` | Cleanup eligibility tracking (zero-ref timestamp) |

**Lock Store**: `character-lock` (Backend: Redis)

Used by `IDistributedLockProvider` to ensure multi-instance safety for character modifications.

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `character.created` | `CharacterCreatedEvent` | New character created |
| `character.updated` | `CharacterUpdatedEvent` | Character metadata modified (includes `ChangedFields` list) |
| `character.deleted` | `CharacterDeletedEvent` | Character permanently deleted |
| `character.realm.joined` | `CharacterRealmJoinedEvent` | Character created in or transferred to a realm (includes `PreviousRealmId` for transfers) |
| `character.realm.left` | `CharacterRealmLeftEvent` | Character deleted from or transferred out of a realm (includes `Reason`: "deletion" or "transfer") |
| `character.compressed` | `CharacterCompressedEvent` | Dead character archived (includes `DeletedSourceData` flag) |

### Consumed Events

This plugin does not consume external events.

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `MaxPageSize` | `CHARACTER_MAX_PAGE_SIZE` | `100` | Maximum page size for list operations |
| `DefaultPageSize` | `CHARACTER_DEFAULT_PAGE_SIZE` | `20` | Default page size when not specified |
| `RealmIndexUpdateMaxRetries` | `CHARACTER_REALM_INDEX_UPDATE_MAX_RETRIES` | `3` | Optimistic concurrency retry limit for realm index |
| `LockTimeoutSeconds` | `CHARACTER_LOCK_TIMEOUT_SECONDS` | `30` | Timeout in seconds for distributed lock acquisition |
| `CleanupGracePeriodDays` | `CHARACTER_CLEANUP_GRACE_PERIOD_DAYS` | `30` | Days at zero references before cleanup eligible |
| `CharacterRetentionDays` | `CHARACTER_RETENTION_DAYS` | `90` | ⚠️ **STUB** - days to retain deleted characters (unimplemented purge feature) |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<CharacterService>` | Scoped | Structured logging |
| `CharacterServiceConfiguration` | Singleton | Pagination and cleanup config |
| `IStateStoreFactory` | Singleton | State store access |
| `IDistributedLockProvider` | Singleton | Distributed locking for multi-instance safety |
| `IMessageBus` | Scoped | Event publishing |
| `IRealmClient` | Scoped | Realm validation |
| `ISpeciesClient` | Scoped | Species validation |
| `IRelationshipClient` | Scoped | Family tree and reference counting |
| `IRelationshipTypeClient` | Scoped | Relationship code lookup |
| `IContractClient` | Scoped | Contract reference counting (L1 - allowed) |
| `IResourceClient` | Scoped | L4 reference counting via event-driven pattern |
| `IEventConsumer` | Scoped | Event registration (no handlers defined) |

Service lifetime is **Scoped** (per-request).

---

## API Endpoints (Implementation Notes)

### CRUD Operations (7 endpoints)

- **Create**: Validates realm (must exist AND be active) and species (must exist AND be in specified realm). Fails CLOSED on service unavailability (throws `InvalidOperationException`). Generates new GUID. Stores with realm-partitioned key. Maintains both realm index and global index with optimistic concurrency retries.
- **Get**: Two-step lookup via global index (characterId -> realmId) then data fetch.
- **Update**: Smart field tracking with `ChangedFields` list. Setting `DeathDate` automatically sets `Status` to `Dead`. `SpeciesId` is mutable (supports species merge migrations).
- **Delete**: Checks for L4 references via lib-resource, executes cleanup callbacks (CASCADE) to delete dependent data in CharacterPersonality/CharacterHistory/etc., then removes from all three storage locations (data, realm index, global index) with optimistic concurrency on index updates. Returns Conflict if cleanup is blocked by RESTRICT policy.
- **List/ByRealm**: Gets realm index, bulk-fetches all characters, filters in-memory (status, species), then paginates. Clamps page size to `MaxPageSize`.
- **TransferRealm**: Moves a character to a different realm. Validates target realm is active, acquires distributed lock, deletes from old realm-partitioned key, saves to new realm-partitioned key, updates indexes, and publishes `character.realm.left` (reason: "transfer"), `character.realm.joined` (with previousRealmId), and `character.updated` events.

### Enriched Character (`/character/get-enriched`)

Per SERVICE_HIERARCHY, Character (L2) can only enrich with data from L2 or lower services. The following flags are available:

| Flag | Source Service | Status |
|------|---------------|--------|
| `includePersonality` | CharacterPersonality (L4) | **NOT INCLUDED** - callers should call L4 directly |
| `includeCombatPreferences` | CharacterPersonality (L4) | **NOT INCLUDED** - callers should call L4 directly |
| `includeBackstory` | CharacterHistory (L4) | **NOT INCLUDED** - callers should call L4 directly |
| `includeFamilyTree` | Relationship + RelationshipType (L2) | ✅ Included |

If L4 enrichment flags are set, the service logs a debug message explaining the SERVICE_HIERARCHY constraint but does not fail.

**Family tree categorization** uses string-based type code matching:
- Parents: PARENT, MOTHER, FATHER, STEP_PARENT
- Children: CHILD, SON, DAUGHTER, STEP_CHILD
- Siblings: SIBLING, BROTHER, SISTER, HALF_SIBLING
- Spouse: SPOUSE, HUSBAND, WIFE
- Reincarnation: INCARNATION (tracks past lives)

### Compression (`/character/compress`)

Preconditions: Must be `Status=Dead` with `DeathDate` set.

Per SERVICE_HIERARCHY, Character (L2) cannot call CharacterPersonality or CharacterHistory (L4) during compression. The archive includes only L2 data:

1. **Family summary**: Text like "married to Elena, parent of 3, reincarnated from 2 past life(s)" (from Relationship service, L2)
2. **Archive creation**: Stores character data and family summary in MySQL under `archive:{characterId}`
3. **Event publication**: Publishes `character.compressed` event so L4 services can clean up their own data

**Note**: `DeleteSourceData` flag works via event-driven cleanup. Character publishes `character.compressed` event; L4 services (CharacterPersonality, CharacterHistory) subscribe and clean up their data when `DeletedSourceData=true`.

### Archive Retrieval (`/character/get-archive`)

Simple lookup of compressed archive data by character ID.

### Reference Checking (`/character/check-references`)

Determines cleanup eligibility for compressed characters:
1. Character must exist
2. Check if compressed (archive exists)
3. Reference count must be 0 (checks relationships, encounters, contracts, and actors)
4. Must maintain 0 references for grace period (default 30 days)

Tracks `ZeroRefSinceUnix` timestamp in state store with optimistic concurrency for multi-instance safety.

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

1. **`CharacterRetentionDays` config placeholder**: Defined for future retention/purge feature but not yet referenced in service code.
<!-- AUDIT:NEEDS_DESIGN:2026-01-31:https://github.com/beyond-immersion/bannou-service/issues/212 -->

---

## Potential Extensions

1. **Batch compression**: Compress multiple dead characters in one operation.
<!-- AUDIT:NEEDS_DESIGN:2026-02-01:https://github.com/beyond-immersion/bannou-service/issues/253 -->
2. **Character purge background service**: Use `CharacterRetentionDays` config to implement automatic purge of characters eligible for cleanup.
<!-- AUDIT:NEEDS_DESIGN:2026-02-02:https://github.com/beyond-immersion/bannou-service/issues/263 -->

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

None currently tracked.

### Intentional Quirks (Documented Behavior)

1. **DeathDate auto-sets Status**: Setting `DeathDate` in an update automatically changes `Status` to `Dead`. The inverse is not true (setting Status=Dead doesn't set DeathDate).

2. **DeleteSourceData flag triggers event-driven cleanup**: When `DeleteSourceData=true` on compression, Character (L2) publishes `character.compressed` event with the flag. L4 services (CharacterPersonality, CharacterHistory) subscribe and delete their data. Character cannot delete L4 data directly per SERVICE_HIERARCHY - the event pattern maintains proper layer separation.

3. **Fail-closed validation**: If realm or species validation services are unavailable, character creation throws `InvalidOperationException` rather than proceeding. This is intentional to prevent data integrity issues.

4. **Distributed locks return Conflict on contention**: Update, compress, and transfer operations acquire distributed locks via `IDistributedLockProvider`. If another instance holds the lock, these operations return `StatusCodes.Conflict` rather than waiting. Callers should retry with backoff.

5. **Lock timeout is configurable but short by default**: The `LockTimeoutSeconds` configuration (default 30s) controls how long a lock can be held. Operations should complete well within this window; the timeout is a safety net for crashed processes.

6. **Global index double-write on create/delete/transfer**: Both realm index (`realm-index:{realmId}` → list of character IDs) and global index (`character-global-index:{characterId}` → realm ID) are maintained. This enables O(1) character lookup by ID (global index → realm ID → character data) without scanning realm indexes. The extra write is intentional for performance: `FindCharacterByIdAsync` reads the global index first to resolve the realm, then fetches the character data with the full realm-partitioned key.

7. **Family tree silently skips unknown relationship types**: If a relationship type ID can't be resolved (type deleted, RelationshipType service unavailable), the relationship is excluded from the family tree with no indication in the response. This is intentional graceful degradation: partial valid data is preferred over failing the entire enrichment. A warning is logged (`"Could not look up relationship type {TypeId}"`) for observability. The alternative (returning uncategorized relationships) would break the structured Parents/Children/Siblings/Spouse/PastLives response format.

8. **INCARNATION tracking is directional (past lives only)**: The `PastLives` field only populates when the queried character is Entity2 in an INCARNATION relationship. This is semantically correct: INCARNATION means "Entity1 died and was reincarnated as Entity2". When querying Entity2, Entity1 is correctly shown as a past life. When querying Entity1, Entity2 is NOT shown because that would be a "future incarnation" (you wouldn't know your future incarnations). The field is named `PastLives`, not `Incarnations`, reinforcing this semantic meaning.

9. **"orphaned" means no parent relationships, not dead parents**: The family summary adds "orphaned" when `Parents.Count == 0` (no parent relationships exist), NOT when parents have died. Dead parents are included in the `Parents` list with `IsAlive = false`. This distinguishes between characters who never had parent records in the game world versus characters whose parents died - a meaningful semantic distinction for backstory generation.

10. **"single parent household" counts relationships, not expected parents**: The family summary adds "single parent household" when exactly one parent relationship exists. It doesn't infer that two parents were expected - it simply reports the current relationship count. A character with one MOTHER relationship shows "single parent household" regardless of whether a FATHER relationship "should" exist.

### Design Considerations (Requires Planning)

1. **In-memory filtering before pagination**: List operations load all characters in a realm, filter in-memory, then paginate. For realms with thousands of characters, this loads everything into memory before applying page limits.
<!-- AUDIT:NEEDS_DESIGN:2026-02-02:https://github.com/beyond-immersion/bannou-service/issues/267 -->

2. **Multiple spouses = last one wins**: Uses simple assignment for spouse. If a character has multiple spouse relationships, only the last one processed appears in the response.
<!-- AUDIT:NEEDS_DESIGN:2026-02-02:https://github.com/beyond-immersion/bannou-service/issues/271 -->

---

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above.

### Active

No active work items.

### Historical

- **2026-02-03**: Fixed Character's delete flow to call `ExecuteCleanupAsync` via lib-resource, properly triggering CASCADE cleanup in L4 services (CharacterPersonality, CharacterHistory, CharacterEncounter, Actor) via their registered cleanup callbacks. This follows the x-references contract pattern (see `docs/reference/RESOURCE_CLEANUP_CONTRACT.md`).
- **2026-02-03**: ~~Implemented L4 cleanup event handlers in CharacterPersonality and CharacterHistory.~~ **SUPERSEDED**: These event handlers were a workaround that bypassed the x-references cleanup contract. The correct fix is `ExecuteCleanupAsync` (above). Event handlers should be removed.
- **2026-02-02**: Removed FIXED item from Potential Extensions (parallel family tree lookups) - verified implemented via `Task.WhenAll` and `GetBulkAsync`, no quirks remain.
- **2026-02-02**: Moved "orphaned" and "single parent household" labels from Design Considerations to Intentional Quirks (#9, #10) - current behavior is semantically correct (orphaned = no parent relationships, single parent = one relationship exists).
- **2026-02-02**: Moved "INCARNATION tracking is directional" from Design Considerations to Intentional Quirks - this is correct semantic behavior (past lives, not future incarnations).
- **2026-02-02**: Moved "Family tree silently skips unknown relationship types" from Design Considerations to Intentional Quirks - this is intentional graceful degradation (partial data preferred over error).
- **2026-02-02**: Moved "Global index double-write" from Design Considerations to Intentional Quirks - this is an intentional performance pattern, not a planning consideration.
- **2026-02-02**: Parallelized family tree lookups - `BuildFamilyTreeAsync` now uses `Task.WhenAll` for relationship type lookups and `GetBulkAsync` for character loading, reducing N+M sequential calls to 2 bulk operations.
- **2026-02-02**: Removed dead configuration properties (`CompressionMaxBackstoryPoints`, `CompressionMaxLifeEvents`, `PersonalityTraitThreshold`) from schema - these were designed for L4 data inclusion that SERVICE_HIERARCHY now prohibits.
- **2026-02-02**: Documentation audit - removed stale hierarchy violation warnings. Code was already using correct event-driven pattern via `IResourceClient` for L4 reference counting; doc was out of date.
- **2026-02-01**: Implemented realm transfer feature (`/character/transfer-realm` endpoint). Validates target realm, acquires distributed lock, atomically moves character between realm-partitioned keys, updates realm and global indexes, publishes realm transition and update events.
- **2026-01-31**: Expanded reference counting in `CheckCharacterReferencesAsync` to check encounters, contracts, and actors in addition to relationships.
- **2026-01-31**: Added distributed locking to `UpdateCharacterAsync` and `CompressCharacterAsync` per [GitHub Issue #189](https://github.com/beyond-immersion/bannou-service/issues/189).
