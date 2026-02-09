# Character Plugin Deep Dive

> **Plugin**: lib-character
> **Schema**: schemas/character-api.yaml
> **Version**: 1.0.0
> **State Store**: character-statestore (MySQL)

---

## Overview

The Character service (L2 GameFoundation) manages game world characters for Arcadia. Characters are independent world assets (not owned by accounts) with realm-based partitioning for scalable queries. Provides standard CRUD, enriched retrieval with family tree data (from lib-relationship), and compression/archival for dead characters via lib-resource. Per the service hierarchy, Character cannot depend on L4 services (personality, history, encounters) -- callers needing that data should aggregate from L4 services directly.

---

## Schema Extensions

| Extension | Value | Purpose |
|-----------|-------|---------|
| `x-service-layer` | `GameFoundation` | L2 service - depends on L0/L1/L2 only |
| `x-resource-lifecycle` | `resourceType: character`, `gracePeriodSeconds: 2592000`, `cleanupPolicy: ALL_REQUIRED` | 30-day grace period before cleanup; deletion aborts if any cleanup callback fails |
| `x-compression-callback` | `resourceType: character`, `priority: 0` | Provides base character data for hierarchical compression via Resource service |

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
| lib-relationship (`IRelationshipClient`) | Queries relationships for family tree and cleanup reference counting; maps type IDs to codes for family tree categorization |
| lib-contract (`IContractClient`) | Queries contracts where character is a party (L1 - allowed) |
| lib-resource (`IResourceClient`) | Queries L4 references (Actor, Encounter) via event-driven pattern (L1 - allowed) |
| lib-resource (`IResourceTemplateRegistry`) | Registers `CharacterBaseTemplate` for ABML compile-time path validation (e.g., `${candidate.character.name}`) |

> **Refactoring Consideration**: This plugin injects 9 service clients individually in the service constructor (10 including `IEventConsumer`). Consider whether `IServiceNavigator` would reduce constructor complexity, trading explicit dependencies for cleaner signatures. Currently favoring explicit injection for dependency clarity.

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
| lib-quest | Calls `ICharacterClient` to validate character existence when accepting quests |

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
| `IRelationshipClient` | Scoped | Family tree, reference counting, and type code lookup |
| `IContractClient` | Scoped | Contract reference counting (L1 - allowed) |
| `IResourceClient` | Scoped | L4 reference counting via event-driven pattern |
| `IEventConsumer` | Scoped | Event registration (no handlers defined) |

Service lifetime is **Scoped** (per-request).

**Plugin Startup** (`CharacterServicePlugin.OnRunningAsync`):
1. Registers `CharacterBaseTemplate` with `IResourceTemplateRegistry` for ABML compile-time path validation
2. Registers compression callback with lib-resource via generated `CharacterCompressionCallbacks.RegisterAsync`

---

## API Endpoints (Implementation Notes)

### CRUD Operations (7 endpoints)

- **Create**: Validates realm (must exist AND be active) and species (must exist AND be in specified realm). Fails CLOSED on service unavailability (throws `InvalidOperationException`). Generates new GUID. Stores with realm-partitioned key. Maintains both realm index and global index with optimistic concurrency retries.
- **Get**: Two-step lookup via global index (characterId -> realmId) then data fetch.
- **Update**: Smart field tracking with `ChangedFields` list. `DeathDate` and `Status=Dead` are bidirectionally linked: setting `DeathDate` auto-sets `Status` to `Dead`, and setting `Status` to `Dead` auto-sets `DeathDate` to now (if not already set). `SpeciesId` is mutable (supports species merge migrations).
- **Delete**: Checks for L4 references via lib-resource, executes cleanup callbacks (CASCADE) to delete dependent data in CharacterPersonality/CharacterHistory/etc., then removes from all three storage locations (data, realm index, global index) with optimistic concurrency on index updates. Returns Conflict if cleanup is blocked by RESTRICT policy.
- **List/ByRealm**: Server-side filtering and pagination via `IJsonQueryableStateStore` MySQL JSON queries. Builds query conditions for realm, status, and species filters, delegates to `JsonQueryPagedAsync` for O(log N + P) performance. Results are sorted by `$.Name` ascending. Clamps page size to `MaxPageSize`.
- **TransferRealm**: Moves a character to a different realm. Validates target realm is active, acquires distributed lock, deletes from old realm-partitioned key, saves to new realm-partitioned key, updates indexes, and publishes `character.realm.left` (reason: "transfer"), `character.realm.joined` (with previousRealmId), and `character.updated` events.

### Enriched Character (`/character/get-enriched`)

Per SERVICE_HIERARCHY, Character (L2) can only enrich with data from L2 or lower services. The following flags are available:

| Flag | Source Service | Status |
|------|---------------|--------|
| `includePersonality` | CharacterPersonality (L4) | **NOT INCLUDED** - callers should call L4 directly |
| `includeCombatPreferences` | CharacterPersonality (L4) | **NOT INCLUDED** - callers should call L4 directly |
| `includeBackstory` | CharacterHistory (L4) | **NOT INCLUDED** - callers should call L4 directly |
| `includeFamilyTree` | Relationship (L2) | ✅ Included |

If L4 enrichment flags are set, the service logs a debug message explaining the SERVICE_HIERARCHY constraint but does not fail.

**Family tree categorization** uses string-based type code matching:
- Parents: PARENT, MOTHER, FATHER, STEP_PARENT
- Children: CHILD, SON, DAUGHTER, STEP_CHILD
- Siblings: SIBLING, BROTHER, SISTER, HALF_SIBLING
- Spouses: SPOUSE, HUSBAND, WIFE (array - supports multiple spousal relationships)
- Reincarnation: INCARNATION (tracks past lives)

### Compression (Centralized via Resource Service)

**IMPORTANT**: Character compression is now centralized through the Resource service (L1). Character (L2) provides a compression callback endpoint; actual compression orchestration happens via `/resource/compress/execute`.

#### Compression Callback (`/character/get-compress-data`)

Preconditions: Must be `Status=Dead` with `DeathDate` set. Returns `BadRequest` for alive characters.

Called by Resource service during `ExecuteCompressAsync`. Returns character base data for archival:
- Core character fields (name, realm, species, birth/death dates, status)
- Family summary text: "married to Elena and Marcus, parent of 3, orphaned" (from Relationship service, L2)

Returns `NotFound` if character doesn't exist, `BadRequest` if character is alive.

#### Triggering Compression

To compress a character with all its L4 data:

```csharp
// Caller invokes Resource service, NOT Character service directly
var result = await _resourceClient.ExecuteCompressAsync(
    new ExecuteCompressRequest
    {
        ResourceType = "character",
        ResourceId = characterId,
        DeleteSourceData = true,  // Clean up after archival
        CompressionPolicy = CompressionPolicy.ALL_REQUIRED
    }, ct);
```

Resource service will:
1. Call `/character/get-compress-data` (priority 0)
2. Call `/character-personality/get-compress-data` (priority 10)
3. Call `/character-history/get-compress-data` (priority 20)
4. Call `/character-encounter/get-compress-data` (priority 30)
5. Bundle all responses into a unified archive stored in MySQL
6. If `deleteSourceData=true`, invoke cleanup callbacks to delete source data

**Legacy**: The old `/character/compress` endpoint still exists but only archives L2 data (family summary). Use Resource service for full hierarchical compression including L4 data.

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
  IJsonQueryableStateStore<CharacterModel>
       │ JsonQueryPagedAsync(conditions, offset, limit)
       │ conditions: $.RealmId, $.Status, $.SpeciesId
       ▼
  MySQL: WHERE JSON_EXTRACT(...) with LIMIT/OFFSET
       │ returns page of CharacterModel + TotalCount
       ▼
  [CharacterListResponse]
```

---

## Stubs & Unimplemented Features

None currently tracked.

---

## Potential Extensions

1. **Batch compression**: Compress multiple dead characters in one operation. Would need to be implemented in Resource service as a batch variant of `/resource/compress/execute`.
<!-- AUDIT:NEEDS_DESIGN:2026-02-01:https://github.com/beyond-immersion/bannou-service/issues/253 -->
2. **Character purge background service**: Automated purge of characters eligible for cleanup (zero references past grace period). Dead `CharacterRetentionDays` config was removed (T21 violation); new config with clear semantics should be designed when this is implemented. Deferred until operational need arises.
<!-- AUDIT:NEEDS_DESIGN:2026-02-07:https://github.com/beyond-immersion/bannou-service/issues/263 -->

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

1. ~~**ApiException wrapped in InvalidOperationException in validation helpers**~~: **FIXED** (2026-02-09) - `ValidateRealmAsync` and `ValidateSpeciesAsync` were catching all non-404 exceptions (including `ApiException`) and wrapping them in `InvalidOperationException`. This caused dependency failures (e.g., RealmService returning 500) to be misclassified as internal errors, emitting unnecessary error events and returning 500 instead of 503 (ServiceUnavailable). Fixed by using `catch (Exception ex) when (ex is not ApiException)` to let `ApiException` propagate naturally to the caller's `catch (ApiException)` block.

### Intentional Quirks (Documented Behavior)

1. **Compression delegated to Resource service**: Full character compression (including L4 data) is now handled by the centralized Resource service (L1). Character provides a `/character/get-compress-data` callback endpoint that Resource invokes during compression orchestration. The legacy `/character/compress` endpoint still exists but only archives L2 data. For hierarchical compression that includes CharacterPersonality, CharacterHistory, and CharacterEncounter data, use `/resource/compress/execute` with `resourceType="character"`.

2. **Family tree silently skips unknown relationship types**: If a relationship type ID can't be resolved (type deleted, Relationship service unavailable), the relationship is excluded from the family tree with no indication in the response. This is intentional graceful degradation: partial valid data is preferred over failing the entire enrichment. A warning is logged (`"Could not look up relationship type {TypeId}"`) for observability. The alternative (returning uncategorized relationships) would break the structured Parents/Children/Siblings/Spouses/PastLives response format.

3. **INCARNATION tracking is directional (past lives only)**: The `PastLives` field only populates when the queried character is Entity2 in an INCARNATION relationship. This is semantically correct: INCARNATION means "Entity1 died and was reincarnated as Entity2". When querying Entity2, Entity1 is correctly shown as a past life. When querying Entity1, Entity2 is NOT shown because that would be a "future incarnation" (you wouldn't know your future incarnations). The field is named `PastLives`, not `Incarnations`, reinforcing this semantic meaning.

4. **Family summary uses relationship counts, not semantic inference**: The `GenerateFamilySummaryAsync` text output uses literal relationship counts rather than inferring intent. Specifically: "orphaned" means `Parents.Count == 0` (no parent relationships exist in the system), NOT that parents have died -- dead parents appear in the `Parents` list with `IsAlive = false`. Similarly, "single parent household" means exactly one parent relationship exists; it doesn't infer that two parents were expected. These are meaningful semantic distinctions for backstory generation: a character with no parent records is fundamentally different from one whose parents died.

### Design Considerations (Requires Planning)

None currently tracked.

---

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above.

### Active

No active work items.

### Historical

See git history for full changelog. Key milestones:
- **2026-02-09**: Fixed ApiException wrapping in validation helpers (T7 compliance)
- **2026-02-07**: Server-side MySQL JSON queries, plural `spouses`, removed dead config, schema extensions
- **2026-02-03**: Centralized compression via Resource service (L1), delete flow with `ExecuteCleanupAsync`
- **2026-02-02**: Parallel family tree lookups, quirk categorization audit, dead config removal
- **2026-02-01**: Realm transfer feature
- **2026-01-31**: Expanded reference counting, distributed locking
