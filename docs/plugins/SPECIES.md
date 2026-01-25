# Species Plugin Deep Dive

> **Plugin**: lib-species
> **Schema**: schemas/species-api.yaml
> **Version**: 2.0.0
> **State Store**: species (Redis/MySQL)

---

## Overview

Realm-scoped species management for the Arcadia game world. Manages playable and NPC races with trait modifiers, realm-specific availability, and a full deprecation lifecycle (deprecate → merge → delete). Species are globally defined but assigned to specific realms, enabling different worlds to offer different playable options. Supports bulk seeding from configuration, code-based lookups (uppercase-normalized), and cross-service character reference checking to prevent orphaned data. Integrates with Character and Realm services for validation.

---

## Dependencies (What This Plugin Relies On)

| Dependency | Usage |
|------------|-------|
| lib-state (`IStateStoreFactory`) | Redis/MySQL persistence for species data, code indexes, realm indexes |
| lib-messaging (`IMessageBus`) | Publishing species lifecycle events; error event publishing |
| lib-messaging (`IEventConsumer`) | Event handler registration (partial class pattern) |
| lib-character (`ICharacterClient`) | Character reference checking for delete/merge/realm-removal safety |
| lib-realm (`IRealmClient`) | Realm existence and status validation |

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-character | Uses `ISpeciesClient` for species validation during character creation |
| lib-character-personality | References species for trait modifier lookups |
| lib-character-history | References species context |

---

## State Storage

**Store**: `species` (via `StateStoreDefinitions.Species`)

| Key Pattern | Data Type | Purpose |
|-------------|-----------|---------|
| `species:{speciesId}` | `SpeciesModel` | Individual species definition |
| `code-index:{CODE}` | `string` | Code → species ID reverse lookup (uppercase) |
| `realm-index:{realmId}` | `List<string>` | Species IDs available in a realm |
| `all-species` | `List<string>` | Global index of all species IDs |

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
| `SeedPageSize` | `SPECIES_SEED_PAGE_SIZE` | `100` | Batch size for paginated character migration during merge |

---

## DI Services & Helpers

| Service | Lifetime | Role |
|---------|----------|------|
| `ILogger<SpeciesService>` | Scoped | Structured logging |
| `SpeciesServiceConfiguration` | Singleton | Config properties (SeedPageSize) |
| `IStateStoreFactory` | Singleton | Redis/MySQL state store access |
| `IMessageBus` | Scoped | Event publishing and error events |
| `IEventConsumer` | Scoped | Event handler registration |
| `ICharacterClient` | Scoped | Character reference checking |
| `IRealmClient` | Scoped | Realm validation |

Service lifetime is **Scoped** (per-request). No background services.

---

## API Endpoints (Implementation Notes)

### Read Operations (4 endpoints)

- **GetSpecies** (`/species/get`): Direct lookup by species ID. Returns full species data with realm associations.
- **GetSpeciesByCode** (`/species/get-by-code`): Code index lookup (uppercase-normalized). Validates data consistency if index points to missing species (logs warning, returns 404).
- **ListSpecies** (`/species/list`): Loads all IDs from `all-species` index, bulk-loads species, filters by `isPlayable`, `category`, `includeDeprecated`. In-memory pagination (page/pageSize, max 100).
- **ListSpeciesByRealm** (`/species/list-by-realm`): Loads realm-specific index. Allows viewing species in deprecated realms (no active check). Same filtering/pagination as List.

### Write Operations (3 endpoints)

- **CreateSpecies** (`/species/create`): Validates realm existence (if initial realms provided). Normalizes code to uppercase. Checks code index for conflicts. Saves species, updates all indexes (code, realm, all-species). Publishes `species.created`.
- **UpdateSpecies** (`/species/update`): Partial update tracking via `changedFields` list. Only changed fields included in event. Updates `UpdatedAt` timestamp.
- **DeleteSpecies** (`/species/delete`): Requires species to be deprecated. Checks character references via `ICharacterClient` (Conflict if characters exist). Removes from all indexes (code, realm, all-species). Publishes `species.deleted`.

### Deprecation Operations (3 endpoints)

- **DeprecateSpecies** (`/species/deprecate`): Validates realm exists and is active. Sets `IsDeprecated=true`, stores timestamp and optional reason. Publishes update event.
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
       │    ├── ListCharacters(speciesId=source, page, pageSize=SeedPageSize)
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
       ├── Validate: must be deprecated
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

1. **Seed doesn't resolve realm codes**: `SeedSpecies` accepts `realmCodes` in the seed data but does NOT resolve them to realm IDs or assign realms. Species are created with empty `RealmIds`. Realm assignment must be done manually via `AddSpeciesToRealm` after seeding.
2. **No event consumption**: The service registers `IEventConsumer` but has no event handlers. Future: could listen for realm deletion events to cascade species removal.

---

## Potential Extensions

1. **Realm code resolution in seed**: Resolve `realmCodes` to IDs during seeding using `IRealmClient.GetRealmByCodeAsync`.
2. **Species inheritance**: Parent species with trait modifier inheritance for subspecies.
3. **Lifecycle stages**: Age-based lifecycle stages (child, adolescent, adult, elder) with trait modifiers per stage.
4. **Population tracking**: Track active character counts per species per realm for game balance analytics.

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

None identified.

### Previously Fixed

1. **T7 (ApiException catch for mesh calls)**: Added `ApiException` catches before `Exception` catches for all `ICharacterClient` calls (delete verification, realm removal verification, merge pagination, character update). ApiException logged at Warning with status code; generic Exception logged at Error/Warning depending on operation criticality.

### Intentional Quirks (Documented Behavior)

1. **Code normalization to uppercase**: All species codes are stored and indexed as uppercase via `ToUpperInvariant()`. Lookups are case-insensitive by normalizing input.

2. **Merge partial failures don't fail the operation**: Individual character update failures during merge increment `failedCount` but don't abort. Returns `StatusCodes.OK` even with failures. Only logs warnings.

3. **Character reference check is fail-closed**: If `ICharacterClient` throws during delete/remove-from-realm, the operation fails (exception propagates). This prevents orphaned characters when the character service is unavailable.

4. **ListSpeciesByRealm allows deprecated realms**: Unlike `AddSpeciesToRealm` (which requires active realm), listing species in a realm works regardless of realm deprecation status.

5. **Realm validation asymmetry**: `CreateSpecies` and `AddSpeciesToRealm` validate realm is active. `RemoveSpeciesFromRealm` only validates realm exists (not active status). `ListSpeciesByRealm` performs no realm validation.

6. **Update event includes all current state**: `SpeciesUpdatedEvent` publishes the full species data plus `ChangedFields` list, even if only one field changed. Consumers can use `ChangedFields` to determine what actually changed.

### Design Considerations (Requires Planning)

1. **All-species list loaded in full**: `ListSpecies` loads all species IDs from the `all-species` key, then bulk-loads each. With hundreds of species, this generates O(N) state store calls.

2. **No distributed locks**: Species operations don't use distributed locks. Concurrent create operations with the same code could race on the code index (unlikely in practice, codes are admin-created).

3. **Seed operation not transactional**: Each species in the seed batch is created independently. A partial failure leaves some species created and others not. No rollback mechanism.

4. **Merge without distributed lock on character list**: The paginated character migration reads and updates characters without locking. A new character created during merge could be missed.

5. **TraitModifiers stored as untyped object**: No schema validation on trait modifier structure. Game-specific data stored as arbitrary JSON.

6. **Page fetch error during merge stops migration**: Lines 1073-1077 - if fetching a page of characters fails during merge, `hasMorePages` is set to false which terminates the loop. Characters on subsequent pages are silently un-migrated. No retry mechanism.

7. **Realm validation is sequential**: `ValidateRealmsAsync` (lines 92-113) iterates through each realm ID and calls `ValidateRealmAsync` sequentially. For N realms, N sequential API calls.

8. **Realm service unavailability throws wrapper exception**: Line 85 wraps RealmService exceptions in `InvalidOperationException("Cannot validate realm ... RealmService unavailable")`. This provides context but changes the exception type.

9. **Seed with updateExisting duplicates change tracking logic**: Lines 770-821 duplicate the same `changedFields` tracking pattern from `UpdateSpeciesAsync` (lines 454-503). Changes to update logic need to be made in both places.

10. **Merge published event doesn't include failed count**: `PublishSpeciesMergedEventAsync` (called at line 1089) receives `migratedCount` but not `failedCount`. Downstream consumers only know successful migrations, not total attempted.

11. **Internal model type safety**: `SpeciesModel` uses `string` for `SpeciesId` and `List<string>` for `RealmIds` instead of `Guid` and `List<Guid>`. This requires `Guid.Parse()`/`ToString()` conversions throughout the code. Refactoring to use proper types would improve type safety.

12. **Configuration property naming**: `SeedPageSize` is used for both seeding pagination and character migration during merge. The dual use is not clearly documented and the name is misleading for the merge use case.

13. **species.created event missing fields**: `SpeciesCreatedEvent` omits some fields (Description, BaseLifespan, MaturityAge, TraitModifiers, RealmIds, Metadata, CreatedAt, UpdatedAt) that are included in `SpeciesUpdatedEvent` and `SpeciesDeletedEvent`.

