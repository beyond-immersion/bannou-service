# Species Plugin Deep Dive

> **Plugin**: lib-species
> **Schema**: schemas/species-api.yaml
> **Version**: 2.0.0
> **State Store**: species (MySQL)

---

## Overview

Realm-scoped species management (L2 GameFoundation) for the Arcadia game world. Manages playable and NPC races with trait modifiers, realm-specific availability, and a full deprecation lifecycle (deprecate, merge, delete). Species are globally defined but assigned to specific realms, enabling different worlds to offer different playable options. Supports bulk seeding from configuration and cross-service character reference checking to prevent orphaned data.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | MySQL persistence for species data, code indexes, realm indexes |
| lib-messaging (`IMessageBus`) | Publishing species lifecycle events; error event publishing |
| lib-messaging (`IEventConsumer`) | Event handler registration (partial class pattern) |
| lib-character (`ICharacterClient`) | Character reference checking for delete/merge/realm-removal safety |
| lib-realm (`IRealmClient`) | Realm existence and status validation |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-character | Uses `ISpeciesClient` for species validation during character creation |
| lib-realm | Uses `ISpeciesClient` for species-realm migration during realm merge (list by realm, add/remove from realm) |

---

## State Storage

**Store**: `species-statestore` (via `StateStoreDefinitions.Species`, Backend: MySQL)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `species:{speciesId}` | `SpeciesModel` | Individual species definition |
| `code-index:{CODE}` | `string` | Code → species ID reverse lookup (uppercase) |
| `realm-index:{realmId}` | `List<Guid>` | Species IDs available in a realm |
| `all-species` | `List<Guid>` | Global index of all species IDs |

---

## Events

### Published Events

| Topic | Event Type | Trigger |
|-------|-----------|---------|
| `species.created` | `SpeciesCreatedEvent` | New species created |
| `species.updated` | `SpeciesUpdatedEvent` | Species fields changed (includes `ChangedFields` list) |
| `species.deleted` | `SpeciesDeletedEvent` | Species hard-deleted |
| `species.merged` | `SpeciesMergedEvent` | Characters migrated from deprecated species to target |

### Consumed Events

This plugin does not consume external events.

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `MergePageSize` | `SPECIES_MERGE_PAGE_SIZE` | `100` | Batch size for paginated character migration during merge |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<SpeciesService>` | Scoped | Structured logging |
| `SpeciesServiceConfiguration` | Singleton | Config properties (MergePageSize) |
| `IStateStoreFactory` | Singleton | MySQL state store access |
| `IMessageBus` | Scoped | Event publishing and error events |
| `IEventConsumer` | Singleton | Event handler registration (no active handlers - species has no event subscriptions) |
| `ICharacterClient` | Scoped | Character reference checking |
| `IRealmClient` | Scoped | Realm validation |

Service lifetime is **Scoped** (per-request). No background services.

---

## API Endpoints (Implementation Notes)

### Read Operations (4 endpoints)

- **GetSpecies** (`/species/get`): Direct lookup by species ID. Returns full species data with realm associations.
- **GetSpeciesByCode** (`/species/get-by-code`): Code index lookup (uppercase-normalized). Validates data consistency if index points to missing species (logs warning, returns 404).
- **ListSpecies** (`/species/list`): Loads all IDs from `all-species` index, bulk-loads species, filters by `isPlayable`, `category`, `includeDeprecated`. In-memory pagination (page/pageSize, max 100).
- **ListSpeciesByRealm** (`/species/list-by-realm`): Validates realm exists via `IRealmClient` (404 if not found), but allows viewing species in deprecated realms (no active check). Loads realm-specific index. Same filtering/pagination as List.

### Write Operations (3 endpoints)

- **CreateSpecies** (`/species/create`): Validates realm existence (if initial realms provided). Normalizes code to uppercase. Checks code index for conflicts. Saves species, updates all indexes (code, realm, all-species). Publishes `species.created`.
- **UpdateSpecies** (`/species/update`): Partial update tracking via `changedFields` list. Only changed fields included in event. Updates `UpdatedAt` timestamp.
- **DeleteSpecies** (`/species/delete`): Requires species to be deprecated (returns BadRequest if not). Checks character references via `ICharacterClient` (Conflict if characters exist). Removes from all indexes (code, realm, all-species). Publishes `species.deleted`.

### Deprecation Operations (3 endpoints)

- **DeprecateSpecies** (`/species/deprecate`): Sets `IsDeprecated=true`, stores timestamp and optional reason. Returns Conflict if already deprecated. Publishes update event.
- **UndeprecateSpecies** (`/species/undeprecate`): Restores deprecated species to active. Returns Conflict if already active.
- **MergeSpecies** (`/species/merge`): Source must be deprecated. Paginates through characters via `ICharacterClient.ListCharactersAsync` (page size from config). Updates each character's species. Partial failures continue (logs warning per failed character). Optional `deleteAfterMerge` flag. Publishes `species.merged`.

### Realm Association (2 endpoints)

- **AddSpeciesToRealm** (`/species/add-to-realm`): Validates realm is active (not deprecated). Adds realm ID to species model and realm index. Publishes update event.
- **RemoveSpeciesFromRealm** (`/species/remove-from-realm`): Checks no characters of this species exist in the realm. Removes from realm index. Publishes update event.

### Admin Operations (1 endpoint)

- **SeedSpecies** (`/species/seed`): Bulk create/update from configuration list. Normalizes codes to uppercase. Supports `updateExisting` flag for idempotency. Creates species with empty realm associations (realm codes in seed data are NOT resolved). Returns created/updated/skipped/error counts.

---

## Visual Aid

```
Species Lifecycle
==================

  CreateSpecies(code, name, realmIds, traitModifiers, ...)
       │
       ├── Normalize code to UPPERCASE
       ├── Validate realm existence (if realmIds provided)
       ├── Check code index for conflict
       ├── Save species model
       ├── Update indexes: code-index, realm-index, all-species
       └── Publish: species.created


Deprecation & Merge Flow
==========================

  DeprecateSpecies(speciesId, reason)
       │
       ├── Set IsDeprecated=true, DeprecatedAt, DeprecationReason
       └── Publish: species.updated (ChangedFields: [isDeprecated, ...])
            │
            ▼
  MergeSpecies(sourceId, targetId, deleteAfterMerge?)
       │
       ├── Validate: source is deprecated, target exists
       │
       ├── Paginated character migration loop:
       │    ├── ListCharacters(speciesId=source, page, pageSize=MergePageSize)
       │    ├── For each character:
       │    │    └── UpdateCharacter(speciesId=target)
       │    │         ├── Success → migratedCount++
       │    │         └── Failure → failedCount++ (continue)
       │    └── Next page until all migrated
       │
       ├── deleteAfterMerge? → DeleteSpecies(source)
       └── Publish: species.merged (MergedCharacterCount)
            │
            ▼
  DeleteSpecies(speciesId)   [if not auto-deleted by merge]
       │
       ├── Check: species is deprecated (BadRequest if not)
       ├── Check: no remaining character references (Conflict if any)
       ├── Remove from all indexes
       └── Publish: species.deleted


State Store Layout
===================

  ┌─ species store ─────────────────────────────────────────┐
  │                                                          │
  │  species:{speciesId} → SpeciesModel                      │
  │    ├── Code (uppercase, unique)                          │
  │    ├── Name, Description, Category                       │
  │    ├── IsPlayable, BaseLifespan, MaturityAge             │
  │    ├── TraitModifiers (object, game-specific)            │
  │    ├── RealmIds (list of assigned realms)                 │
  │    └── IsDeprecated, DeprecatedAt, DeprecationReason     │
  │                                                          │
  │  code-index:{CODE} → speciesId (string)                  │
  │  realm-index:{realmId} → [speciesId, speciesId, ...]     │
  │  all-species → [speciesId, speciesId, ...]               │
  └──────────────────────────────────────────────────────────┘
```

---

## Stubs & Unimplemented Features

1. ~~**Seed doesn't resolve realm codes**~~: **FIXED** (2026-02-10) - `SeedSpeciesAsync` now resolves `realmCodes` to realm IDs via `IRealmClient.GetRealmByCodeAsync` before creating species. Unresolvable codes (not found or service error) are skipped with a warning log per code; the species is still created with whatever realms did resolve.
2. **No event consumption**: The service registers `IEventConsumer` but has no event handlers. Future: could listen for realm deletion events to cascade species removal.
<!-- AUDIT:NEEDS_DESIGN:2026-02-10:https://github.com/beyond-immersion/bannou-service/issues/369 -->

---

## Potential Extensions

1. ~~**Realm code resolution in seed**~~: **FIXED** (2026-02-10) - Implemented in `SeedSpeciesAsync`; see Stubs section above.
2. **Species inheritance**: Parent species with trait modifier inheritance for subspecies.
<!-- AUDIT:NEEDS_DESIGN:2026-02-10:https://github.com/beyond-immersion/bannou-service/issues/370 -->
3. **Lifecycle stages**: Age-based lifecycle stages (child, adolescent, adult, elder) with trait modifiers per stage.
<!-- AUDIT:NEEDS_DESIGN:2026-02-10:https://github.com/beyond-immersion/bannou-service/issues/371 -->
4. **Population tracking**: Track active character counts per species per realm for game balance analytics.
<!-- AUDIT:NEEDS_DESIGN:2026-02-10:https://github.com/beyond-immersion/bannou-service/issues/372 -->

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

None currently identified.

### Intentional Quirks (Documented Behavior)

1. **Merge partial failures don't fail the operation**: Individual character update failures during merge increment `failedCount` but don't abort. Returns `StatusCodes.OK` even with failures. Only logs warnings. The `deleteAfterMerge` flag is skipped if any failures occurred.

2. **Realm validation asymmetry**: `CreateSpecies` and `AddSpeciesToRealm` validate realm is active (not deprecated). `RemoveSpeciesFromRealm` doesn't validate realm status at all (only checks species membership). `ListSpeciesByRealm` validates realm exists but allows deprecated realms.

3. **Merge published event doesn't include failed count**: `PublishSpeciesMergedEventAsync` receives `migratedCount` but not `failedCount`. Downstream consumers only know successful migrations, not total attempted.

4. **All-species list loaded in full**: `ListSpecies` and `ListSpeciesByRealm` load all matching species IDs via `GetBulkAsync` then filter and paginate in-memory. Acceptable because species are admin-created definitions (typically <100 per deployment). If a game had thousands of species, this would need migration to `IJsonQueryableStateStore<T>.JsonQueryPagedAsync()` for server-side filtering.

5. **Seed operation not transactional**: Each species in the seed batch is created independently. A partial failure leaves some species created and others not. This is the universal Bannou seed pattern — lib-state doesn't expose cross-key transactions, cross-service calls make transactions infeasible, and idempotent recovery via `updateExisting` flag is the intended approach. Re-run the seed to recover from partial failures.

6. **TraitModifiers and Metadata are client-only untyped objects**: Both `traitModifiers` and `metadata` are `type: object, additionalProperties: true` in the schema and `object?` in the internal model. No schema validation on structure. This is intentional: Species is L2 GameFoundation and must be game-agnostic. Different games define different trait systems (e.g., strength/dexterity vs magic affinity axes). No Bannou plugin reads specific keys from these fields by convention. If a higher-layer service needs species trait data, it owns that data in its own state store.

### Design Considerations

1. **No distributed locks**: Species operations don't use distributed locks. Concurrent create operations with the same code could race on the code index (unlikely in practice, codes are admin-created).
<!-- AUDIT:NEEDS_DESIGN:2026-02-10:https://github.com/beyond-immersion/bannou-service/issues/373 -->

2. ~~**Seed operation not transactional**~~: **FIXED** (2026-02-10) - Not a gap; this is the universal Bannou seed pattern (confirmed across Location, Realm, Relationship, Species). lib-state doesn't expose cross-key transactions, cross-service calls make transactions infeasible, and idempotent recovery via `updateExisting` flag is the intended approach. Moved to Intentional Quirks.

3. **Merge without distributed lock on character list**: The paginated character migration reads and updates characters without locking. A new character created during merge could be missed.
<!-- AUDIT:NEEDS_DESIGN:2026-02-10:https://github.com/beyond-immersion/bannou-service/issues/373 -->

4. ~~**TraitModifiers stored as untyped object**~~: **FIXED** (2026-02-10) - Not a gap; this is intentional design for L2 game-generic services. Different games define different trait systems, so trait modifiers must remain opaque JSON (`type: object, additionalProperties: true`). Same pattern as Metadata field. Moved to Intentional Quirks.

5. ~~**Realm validation is sequential**~~: **FIXED** (2026-02-10) - `ValidateRealmsAsync` now uses `Task.WhenAll` to validate all realms in parallel. All realm validation calls execute concurrently instead of sequentially.

6. ~~**Seed with updateExisting duplicates change tracking logic**~~: **FIXED** (2026-02-10) - Extracted shared `ApplySpeciesFieldUpdates` helper method. Both `UpdateSpeciesAsync` and `SeedSpeciesAsync` (updateExisting path) now call the same method for field comparison and change tracking.

7. ~~**Configuration property naming**~~: **FIXED** (2026-02-10) - Renamed `SeedPageSize` to `MergePageSize` with env var `SPECIES_MERGE_PAGE_SIZE`. Updated description to accurately reflect its use for character migration during merge operations.

---

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above. Items here are managed by the `/audit-plugin` workflow.

### Completed

- **Seed realm code resolution** (2026-02-10): `SeedSpeciesAsync` now resolves `realmCodes` via `IRealmClient.GetRealmByCodeAsync`. Unresolvable codes are skipped with warning logs.
- **All-species list in-memory load** (2026-02-10): Moved from Design Considerations to Intentional Quirks. In-memory approach is acceptable for admin-created species (<100 typical).
- **Seed operation not transactional** (2026-02-10): Moved from Design Considerations to Intentional Quirks. Universal Bannou seed pattern; idempotent recovery via `updateExisting` is the intended approach.
- **TraitModifiers stored as untyped object** (2026-02-10): Moved from Design Considerations to Intentional Quirks. Intentional L2 game-generic design per IMPLEMENTATION TENETS Type Safety exception for hierarchy isolation.
- **Realm validation is sequential** (2026-02-10): Refactored `ValidateRealmsAsync` to use `Task.WhenAll` for parallel realm validation.
- **Seed with updateExisting duplicates change tracking logic** (2026-02-10): Extracted `ApplySpeciesFieldUpdates` helper. Both `UpdateSpeciesAsync` and `SeedSpeciesAsync` now share the same field comparison logic.
- **Configuration property naming** (2026-02-10): Renamed `SeedPageSize` → `MergePageSize` (env: `SPECIES_MERGE_PAGE_SIZE`). Name and description now accurately reflect its use for character migration page size during merge.
