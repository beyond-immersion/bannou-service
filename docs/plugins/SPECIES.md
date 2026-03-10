# Species Plugin Deep Dive

> **Plugin**: lib-species
> **Schema**: schemas/species-api.yaml
> **Version**: 2.0.0
> **Layer**: GameFoundation
> **State Stores**: species (MySQL), species-lock (Redis)
> **Implementation Map**: [docs/maps/SPECIES.md](../maps/SPECIES.md)
> **Short**: Realm-scoped species definitions with trait modifiers and deprecation lifecycle

---

## Overview

Realm-scoped species management (L2 GameFoundation) for the Arcadia game world. Manages playable and NPC races with trait modifiers, realm-specific availability, and a full deprecation lifecycle (deprecate, merge, delete). Species are globally defined but assigned to specific realms, enabling different worlds to offer different playable options. Supports bulk seeding from configuration and cross-service character reference checking to prevent orphaned data.

---

## Type Field Classification

| Field | Category | Type | Rationale |
|-------|----------|------|-----------|
| `category` | B (Content Code) | Opaque string | Game-configurable species grouping (e.g., "HUMANOID", "BEAST", "MAGICAL"). Extensible without schema changes; different games define different category taxonomies |

Species has no Category A (entity reference) fields -- species are referenced by ID from Character, not via polymorphic type discriminators. Species has no Category C (system state) fields -- `isPlayable` and `isDeprecated` are booleans, not type discriminators.

---

## Dependents (What Relies On This Plugin)

| Dependent | Relationship |
|-----------|-------------|
| lib-character | Uses `ISpeciesClient` for species validation during character creation |
| lib-realm | Uses `ISpeciesClient` for species-realm migration during realm merge (list by realm, add/remove from realm) |
| lib-transit | Uses `ISpeciesClient.GetSpeciesAsync` for species lookup during journey speed calculations |
| lib-ethology | Uses `ISpeciesClient` for archetype validation; subscribes to `species.deprecated`/`species.deleted` events for cleanup. Provides the `${nature.*}` variable namespace to Actor (L2) via `NatureProviderFactory`, exposing species-level behavioral baselines built on species code lookups |
| lib-character-lifecycle | Uses `ISpeciesClient` for lifecycle template resolution — species determines longevity ranges, stage boundaries, fertility windows, and heritable trait definitions (not yet implemented) |

### Architectural Role in the NPC Intelligence Stack

Species provides the biological identity that higher-layer services build on. Ethology (L4) uses species codes to define behavioral archetypes — structured behavioral baselines exposed as the `${nature.*}` variable namespace to the Actor behavior system. Character-Lifecycle (L4) uses species data to determine lifecycle templates (aging stages, fertility, longevity, heritable traits). Species itself is deliberately game-agnostic: `traitModifiers` and `metadata` are untyped `object?` fields that no Bannou plugin reads by convention (per). Higher-layer services that need structured species data own it in their own state stores — Ethology owns behavioral archetypes, Character-Lifecycle owns lifecycle templates.

---

## Configuration

| Property | Env Var | Default | Purpose |
|----------|---------|---------|---------|
| `MergePageSize` | `SPECIES_MERGE_PAGE_SIZE` | `100` | Batch size for paginated character migration during merge |
| `LockTimeoutSeconds` | `SPECIES_LOCK_TIMEOUT_SECONDS` | `30` | Timeout in seconds for distributed lock acquisition on species mutations |

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

 DeprecateSpecies(speciesId, deprecationReason)
 │
 ├── Acquire distributed lock on {speciesId}
 ├── Set IsDeprecated=true, DeprecatedAt, DeprecationReason
 └── Publish: species.updated (ChangedFields: [isDeprecated, ...])
 │
 ▼
 MergeSpecies(sourceId, targetId, deleteAfterMerge?)
 │
 ├── Acquire two distributed locks (lower GUID first)
 ├── Validate: source is deprecated, target exists, target NOT deprecated
 │
 ├── Paginated character migration loop:
 │ ├── ListCharacters(speciesId=source, page, pageSize=MergePageSize)
 │ ├── For each character:
 │ │ └── UpdateCharacter(speciesId=target)
 │ │ ├── Success → migratedCount++
 │ │ └── Failure → failedEntityIds.Add(characterId)
 │ └── Next page until all migrated
 │
 ├── deleteAfterMerge && no failures? → DeleteSpecies(source)
 └── Publish: species.merged (MergedCharacterCount)
 │
 ▼
 DeleteSpecies(speciesId) [if not auto-deleted by merge]
 │
 ├── Acquire distributed lock on {speciesId}
 ├── Check: species is deprecated (BadRequest if not)
 ├── Check: no remaining character references (Conflict if any)
 ├── Check: no higher-layer references via lib-resource (Conflict if any)
 ├── Execute cleanup callbacks via lib-resource (ALL_REQUIRED)
 ├── Remove from all indexes
 └── Publish: species.deleted


State Store Layout
===================

 ┌─ species store ─────────────────────────────────────────┐
 │ │
 │ species:{speciesId} → SpeciesModel │
 │ ├── Code (uppercase, unique) │
 │ ├── Name, Description, Category │
 │ ├── IsPlayable, BaseLifespan, MaturityAge │
 │ ├── TraitModifiers (object, game-specific) │
 │ ├── RealmIds (list of assigned realms) │
 │ └── IsDeprecated, DeprecatedAt, DeprecationReason │
 │ │
 │ code-index:{CODE} → speciesId (string) │
 │ realm-index:{realmId} → [speciesId, speciesId, ...] │
 │ all-species → [speciesId, speciesId, ...] │
 └──────────────────────────────────────────────────────────┘
```

---

## Stubs & Unimplemented Features

1. **No realm deletion cleanup via lib-resource**: When a realm is deleted, species that reference that realm retain the stale GUID in their `RealmIds` list. The intended workflow is deprecate → merge → delete, where the merge step migrates species to the target realm. Species (along with Location and Character) should register **RESTRICT** references with lib-resource when associating with a realm, ensuring that attempting to delete a realm with remaining species is blocked. This replaces the earlier event-subscription approach.
<!-- AUDIT:NEEDS_DESIGN:2026-02-10:https://github.com/beyond-immersion/bannou-service/issues/369 -->

---

## Potential Extensions

1. **Species inheritance**: Parent species with trait modifier inheritance for subspecies.
<!-- AUDIT:NEEDS_DESIGN:2026-02-10:https://github.com/beyond-immersion/bannou-service/issues/370 -->

---

## Known Quirks & Caveats

### Bugs (Fix Immediately)

*(none)*

### Intentional Quirks (Documented Behavior)

1. **Merge partial failures don't fail the operation**: Individual character update failures during merge are tracked by character ID in `failedEntityIds` but don't abort. Returns `StatusCodes.OK` even with failures. Only logs warnings. The `deleteAfterMerge` flag is skipped if any failures occurred.

2. **Realm validation asymmetry**: `CreateSpecies` and `AddSpeciesToRealm` validate realm is active (not deprecated). `RemoveSpeciesFromRealm` doesn't validate realm status at all (only checks species membership). `ListSpeciesByRealm` validates realm exists but allows deprecated realms.

3. **Merge published event doesn't include failed entity IDs**: `SpeciesMergedEvent` includes `MergedCharacterCount` (successful migrations) but not individual failed entity IDs. Downstream consumers only know successful migration count, not which characters failed. Failed entity IDs are returned in the API response only.

4. **All-species list loaded in full**: `ListSpecies` and `ListSpeciesByRealm` load all matching species IDs via `GetBulkAsync` then filter and paginate in-memory. Acceptable because species are admin-created definitions (typically <100 per deployment). If a game had thousands of species, this would need migration to `IJsonQueryableStateStore<T>.JsonQueryPagedAsync()` for server-side filtering.

5. **Seed operation not transactional**: Each species in the seed batch is created independently. A partial failure leaves some species created and others not. This is the universal Bannou seed pattern — lib-state doesn't expose cross-key transactions, cross-service calls make transactions infeasible, and idempotent recovery via `updateExisting` flag is the intended approach. Re-run the seed to recover from partial failures.

6. **TraitModifiers and Metadata are client-only untyped objects**: Both `traitModifiers` and `metadata` are `type: object, additionalProperties: true` in the schema and `object?` in the internal model. No schema validation on structure. This is intentional: Species is L2 GameFoundation and must be game-agnostic. Different games define different trait systems (e.g., strength/dexterity vs magic affinity axes). No Bannou plugin reads specific keys from these fields by convention. If a higher-layer service needs species trait data, it owns that data in its own state store.

### Design Considerations

*(none)*

---

## Work Tracking

This section tracks active development work on items from the quirks/bugs lists above. Items here are managed by the `/audit-plugin` workflow.

*(No active work items.)*
