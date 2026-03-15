# Character Plugin Deep Dive

> **Plugin**: lib-character
> **Schema**: schemas/character-api.yaml
> **Version**: 1.0.0
> **Layer**: GameFoundation
> **State Store**: character-statestore (MySQL)
> **Implementation Map**: [docs/maps/CHARACTER.md](../maps/CHARACTER.md)
> **Short**: Game world character management with realm partitioning and system realm support

---

## Overview

The Character service (L2 GameFoundation) manages game world characters for Arcadia. Characters are independent world assets (not owned by accounts) with realm-based partitioning for scalable queries. Provides standard CRUD, enriched retrieval with family tree data (from lib-relationship), and compression/archival for dead characters via lib-resource. Per the service hierarchy, Character cannot depend on L4 services (personality, history, encounters) -- callers needing that data should aggregate from L4 services directly.

**System realm characters**: The Character service is agnostic to realm type -- it treats characters in system realms (PANTHEON, DUNGEON_CORES, SENTIENT_ARMS, NEXIUS, UNDERWORLD) identically to characters in physical realms. This is by design: system realms give non-physical entities (gods, dungeon cores, sentient weapons, guardian spirits) character records for the actor-bound entity pattern without polluting physical realm queries. System realm filtering is the Realm service's responsibility via `isSystemType`.

---

## Schema Extensions

| Extension | Value | Purpose |
|-----------|-------|---------|
| `x-resource-lifecycle` | `resourceType: character`, `gracePeriodSeconds: 2592000`, `cleanupPolicy: ALL_REQUIRED` | 30-day grace period before cleanup; deletion aborts if any cleanup callback fails |
| `x-compression-callback` | `resourceType: character`, `priority: 0` | Provides base character data for hierarchical compression via Resource service |

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
| lib-realm | Calls `ICharacterClient` to check character references during realm deprecation |
| lib-quest | Calls `ICharacterClient` to validate character existence when accepting quests |
| lib-obligation | Calls `ICharacterClient` for character data retrieval during obligation tracking |
| lib-license | Calls `ICharacterClient` for character owner validation on license board operations |
| lib-transit | Calls `ICharacterClient` for character validation during journey operations |

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `MaxPageSize` | `CHARACTER_MAX_PAGE_SIZE` | `100` | Maximum page size for list operations |
| `DefaultPageSize` | `CHARACTER_DEFAULT_PAGE_SIZE` | `20` | Default page size when not specified |
| `RealmIndexUpdateMaxRetries` | `CHARACTER_REALM_INDEX_UPDATE_MAX_RETRIES` | `3` | Optimistic concurrency retry limit for realm index |
| `RefCountUpdateMaxRetries` | `CHARACTER_REF_COUNT_UPDATE_MAX_RETRIES` | `3` | Optimistic concurrency retry limit for refcount tracking |
| `LockTimeoutSeconds` | `CHARACTER_LOCK_TIMEOUT_SECONDS` | `30` | Timeout in seconds for distributed lock acquisition |
| `CleanupGracePeriodDays` | `CHARACTER_CLEANUP_GRACE_PERIOD_DAYS` | `30` | Days at zero references before cleanup eligible |

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
2. **Character purge background service**: Automated purge of characters eligible for cleanup (zero references past grace period). Dead `CharacterRetentionDays` config was removed (violation); new config with clear semantics should be designed when this is implemented. Deferred until operational need arises.
<!-- AUDIT:NEEDS_DESIGN:2026-02-07:https://github.com/beyond-immersion/bannou-service/issues/263 -->

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

None currently tracked.

### Intentional Quirks (Documented Behavior)

1. **Compression delegated to Resource service**: Full character compression (including L4 data) is now handled by the centralized Resource service (L1). Character provides a `/character/get-compress-data` callback endpoint that Resource invokes during compression orchestration. The legacy `/character/compress` endpoint still exists but only archives L2 data. For hierarchical compression that includes CharacterPersonality, CharacterHistory, and CharacterEncounter data, use `/resource/compress/execute` with `resourceType="character"`.

2. **Family tree silently skips unknown relationship types**: If a relationship type ID can't be resolved (type deleted, Relationship service unavailable), the relationship is excluded from the family tree with no indication in the response. This is intentional graceful degradation: partial valid data is preferred over failing the entire enrichment. A warning is logged (`"Could not look up relationship type {TypeId}"`) for observability. The alternative (returning uncategorized relationships) would break the structured Parents/Children/Siblings/Spouses/PastLives response format.

3. **INCARNATION tracking is directional (past lives only)**: The `PastLives` field only populates when the queried character is Entity2 in an INCARNATION relationship. This is semantically correct: INCARNATION means "Entity1 died and was reincarnated as Entity2". When querying Entity2, Entity1 is correctly shown as a past life. When querying Entity1, Entity2 is NOT shown because that would be a "future incarnation" (you wouldn't know your future incarnations). The field is named `PastLives`, not `Incarnations`, reinforcing this semantic meaning.

4. **Family summary uses relationship counts, not semantic inference**: The `GenerateFamilySummaryAsync` text output uses literal relationship counts rather than inferring intent. Specifically: "orphaned" means `Parents.Count == 0` (no parent relationships exist in the system), NOT that parents have died -- dead parents appear in the `Parents` list with `IsAlive = false`. Similarly, "single parent household" means exactly one parent relationship exists; it doesn't infer that two parents were expected. These are meaningful semantic distinctions for backstory generation: a character with no parent records is fundamentally different from one whose parents died.

### Design Considerations (Requires Planning)

1. **Delete flow O(N) reference unregistration**: When Character is deleted, cleanup callbacks fire on 4 L4 services (CharacterPersonality, CharacterHistory, CharacterEncounter, Actor). Each entity deletion in those services publishes an individual `resource.reference.unregistered` event. For characters with rich data (hundreds of encounters, many history entries), this creates O(N) message bus traffic. A batch unregistration endpoint in lib-resource would reduce this to a single operation.
<!-- AUDIT:NEEDS_DESIGN:2026-02-23:https://github.com/beyond-immersion/bannou-service/issues/351 -->

2. **No realm deletion reference tracking via lib-resource** ([#591](https://github.com/beyond-immersion/bannou-service/issues/591)): Character stores `realmId` on every character but does not register RESTRICT references with lib-resource when creating or transferring characters. This means realm deletion cannot be blocked by the existence of characters in that realm. The intended workflow (deprecate → merge → delete) migrates characters before deletion, but the lib-resource safety net is missing. Species (#369) and Location (#590) have the same gap. Adding `x-references` with `target: realm` and `onDelete: restrict` would complete the realm deletion safety chain documented in REALM.md.
<!-- AUDIT:NEEDS_DESIGN:2026-03-15:https://github.com/beyond-immersion/bannou-service/issues/591 -->


---

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above.

### Active

No active work items.

### Historical

See git history for full changelog.
